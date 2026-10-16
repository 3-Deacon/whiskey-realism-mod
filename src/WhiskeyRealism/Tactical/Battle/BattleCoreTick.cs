using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Read-only snapshot access for patches (D1). Patches never build or write it.
    // The one registry is the builder's latest build per alliance.
    public static class BattleCore
    {
        public static bool TryGetSnapshot(int alliance, out BattleSnapshot snapshot)
        {
            return BattleSnapshotBuilder.TryGetCurrent(alliance, out snapshot);
        }
    }

    // The battle-core tick (plan BC4, D1/D3). Runtime only.
    //
    // EnsureSnapshot builds at most one snapshot per (battle, alliance, frame):
    // the coordinator calls it first in TacticalBattleCoordinator.Tick, which runs
    // from the CheckGlobalAIStrategy Postfix (vanilla 5866), and the arbiter patch
    // (Postfix on AdjustGroupFormations, 5870) reuses that object in the same
    // UpdateAITasks call (CalculateSideStatsAndUpdateAITasks runs aibattle[0..2]
    // in one frame, 84864-84869). Each side keeps two pooled instances: the
    // builder's current one and a spare the next build fills. Every build is
    // followed by BattlePerceptionProbe.Probe (BC2a), so perception memory
    // advances once per built snapshot.
    //
    // RunArbiter: producers (fixed order) → OrderArbiter.Resolve → outcomes of
    // the last tick's moves and their retries → OrderExecutor.Plan +
    // OrderExecutorRuntime.Execute per winner.
    //
    // Producers (plan BC5 order, BC7 registry): built here, once, from one
    // explicit list (BattleProducerRegistry.Order through CreateProducer). They
    // used to register from their own static constructors, which never ran
    // because nothing else referenced the producer types.
    internal static class BattleCoreTick
    {
        private const float TelemetryIntervalRealSeconds = 30f;
        private const int SkipReasonCount = (int)SkipReason.LowerPriorityThanClaim + 1;

        private static readonly BattleProducerRegistry Producers = new BattleProducerRegistry(CreateProducer);

        private static readonly BattleSnapshot[] _spare = new BattleSnapshot[2];
        private static readonly int[] _builtFrame = { -1, -1 };
        private static readonly int[] _builtBattleKey = { 0, 0 };
        private static long _tickId;

        // BC11 decision 6: per-side watch of vanilla's own flank moves (lastaiorder 0 -> 4 / 5).
        private static readonly VanillaFlankMoveWatch[] _flankMoves = { new VanillaFlankMoveWatch(), new VanillaFlankMoveWatch() };
        private static readonly int[] _flankMoveIds = new int[16];
        private static readonly int[] _flankMoveDirections = new int[16];

        private static readonly int[] _macroAi = new int[2];
        private static BattleOrderIntent[] _winners = new BattleOrderIntent[256];
        private static readonly BattleOrderIntent[] _retries = new BattleOrderIntent[32];
        private static readonly OrderOutcome[] _outcomes = new OrderOutcome[256];
        private static readonly ArbiterStats[] _stats = { new ArbiterStats(), new ArbiterStats() };

        private static FieldInfo _bunitsField;
        private static bool _bunitsFieldResolved;
        // Per-battle latch for the producer line, so the message string is built
        // once per battle (its OnceLog key re-arms at battle end too).
        private static bool _producersLogged;

        static BattleCoreTick()
        {
            BattleScope.Register("battle-core-tick", Reset);
        }

        internal static bool HasProducers => Producers.HasProducers;

        // One case per BattleProducerRegistry.Order name, in that order.
        private static IBattleIntentProducer CreateProducer(string name)
        {
            switch (name)
            {
                case "Withdrawal": return new WithdrawalIntentProducer();
                case "ArtilleryDoctrine": return new ArtilleryIntentProducer(pointBlank: false);
                case "ArtilleryPointBlank": return new ArtilleryIntentProducer(pointBlank: true);
                case "GroupStance": return new GroupStanceIntentProducer();
                case "ChargeDemotion": return new ChargeDemotionIntentProducer();
                case "Opportunity": return new OpportunityIntentProducer();
                case "Posture": return new PostureIntentProducer();
                case "FacingPulse": return new FacingPulseIntentProducer();
                case "FireControl": return new FireControlIntentProducer();
                case "DivisionPlay": return new DivisionPlayIntentProducer();
                case "Recon": return new ReconIntentProducer();
                case "ReserveRelease": return new ReserveReleaseIntentProducer();
                case "Charge": return new ChargeIntentProducer();
                default: return null;
            }
        }

        internal static BattleSnapshot EnsureSnapshot(AIBattle battle, int alliance)
        {
            if (alliance < 0 || alliance > 1 || (UnityEngine.Object)battle == null) return null;
            try
            {
                BattleUnits bunits = ResolveBattleUnits();
                int battleKey = (UnityEngine.Object)bunits != null ? bunits.GetInstanceID() : 0;
                int frame = UnityEngine.Time.frameCount;
                BattleSnapshotBuilder.TryGetCurrent(alliance, out BattleSnapshot current);
                if (_builtFrame[alliance] == frame && _builtBattleKey[alliance] == battleKey && current != null) return current;

                BattleSnapshot reuse = _spare[alliance];
                if (ReferenceEquals(reuse, current)) reuse = null;
                _spare[alliance] = current;
                unchecked { _tickId++; }
                BattleSnapshot built = BattleSnapshotBuilder.Build(battle, alliance, reuse, _tickId, TacticalDoctrineTime.NowBattleHours);
                if (built != null) BattlePerceptionProbe.Probe(built, battle);
                // The battle's terrain grid exists from the first snapshot (deployment) on.
                BattleTerrainGridRuntime.Get();
                // BC12: the terrain picture builds one budgeted step per frame until it is Ready.
                TerrainPictureRuntime.Step();
                _builtFrame[alliance] = frame;
                _builtBattleKey[alliance] = battleKey;
                return built;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:snapshot-failed",
                    "[BattleCore] EnsureSnapshot threw: " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        /// <summary>The side's arbiter is gated off this tick: drop its published claims.</summary>
        internal static void StandDown(int alliance)
        {
            OrderArbiter arbiter = OrderArbiter.ForAlliance(alliance);
            if (arbiter != null) arbiter.ClearClaims();
            // BC13: a gated-off side ends every live exploit (stand-down) and the posture producer finds an empty table; the
            // staggered reserve release starts over.
            OpportunityIntentProducer.StandDown(alliance);
            TacticalBattleCoordinator.GetSideOrchestrator(alliance)?.Army?.ReserveRelease.Clear();
        }

        /// <summary>The side's vanilla macroai as of its last arbiter tick (0 before one).</summary>
        internal static int MacroAiOf(int alliance) => alliance >= 0 && alliance < _macroAi.Length ? _macroAi[alliance] : 0;

        internal static void RunArbiter(AIBattle battle, int alliance)
        {
            if (!Producers.HasProducers) return;
            OrderArbiter arbiter = OrderArbiter.ForAlliance(alliance);
            OrderExecutor executor = OrderExecutor.ForAlliance(alliance);
            if (arbiter == null || executor == null) return;

            BattleSnapshot snapshot = EnsureSnapshot(battle, alliance);
            BattleUnits bunits = ResolveBattleUnits();
            if (snapshot == null || (UnityEngine.Object)bunits == null)
            {
                arbiter.ClearClaims();
                OnceLog.Warning("tactical-battle-core:snapshot-unavailable",
                    "[OrderArbiter] no battle snapshot; producers registered but the arbiter stands down");
                return;
            }

            WatchVanillaFlankMoves(battle, alliance, snapshot);

            if (!_producersLogged)
            {
                _producersLogged = true;
                OnceLog.Info("tactical-battle-core:producers", "[BattleCore] producers count=" + Producers.Count + " order=" + Producers.Names);
                if (Producers.Missing.Length > 0)
                    OnceLog.Warning("tactical-battle-core:producers-missing",
                        "[BattleCore] producers not built (they never run): " + Producers.Missing);
            }
            double now = TacticalDoctrineTime.NowBattleHours;
            ArbiterStats stats = _stats[alliance];
            _macroAi[alliance] = battle.macroai;

            arbiter.BeginTick();
            for (int p = 0; p < Producers.Count; p++)
            {
                IBattleIntentProducer producer = Producers[p];
                try
                {
                    producer.Produce(snapshot, arbiter);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(Producers.ThrewKey(p),
                        "[OrderArbiter] producer " + producer.Name + " threw: " + ex.GetType().Name + " " + ex.Message);
                }
            }

            int intents = arbiter.IntentCount;
            if (_winners.Length < intents) _winners = new BattleOrderIntent[Math.Max(intents, _winners.Length * 2)];
            int winners;
            using (TelemetryPerf.Scope("tactical.arbiter.resolve", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.5))
            {
                winners = arbiter.Resolve(_winners);
            }
            stats.Intents += intents;
            stats.Winners += winners;

            using (TelemetryPerf.Scope("tactical.arbiter.execute", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                OrderExecutorRuntime.BindForTick(executor);
                executor.BeginTickClaims(snapshot, _winners, winners);
                try
                {
                    executor.ObserveOutcomes(snapshot, now, _outcomes, out int outcomeCount);
                    for (int o = 0; o < outcomeCount; o++) stats.Tally(_outcomes[o]);

                    int retries = executor.TakeRetries(_retries);
                    for (int r = 0; r < retries; r++)
                    {
                        if (!OrderExecutor.RetryStillClaimed(arbiter, _retries[r]))
                        {
                            stats.Tally(executor.RecordSuperseded(_retries[r], now));
                            continue;
                        }
                        ExecutionPlan retryPlan = executor.PlanRetry(snapshot, _retries[r], now);
                        if (retryPlan.Execute) stats.Issued += Math.Min(1, OrderExecutorRuntime.Execute(bunits, snapshot, executor, _retries[r], retryPlan, now));
                        else stats.Skip(retryPlan.Skip);
                    }

                    for (int w = 0; w < winners; w++)
                    {
                        ExecutionPlan plan = executor.Plan(snapshot, _winners[w], now);
                        // BC13 playerSkips: what an exploit's own intent left out because of the player.
                        ExploitFeedback.NotePlan(alliance, in _winners[w], in plan);
                        if (!plan.Execute)
                        {
                            stats.Skip(plan.Skip);
                            continue;
                        }
                        stats.Issued += Math.Min(1, OrderExecutorRuntime.Execute(bunits, snapshot, executor, _winners[w], plan, now));
                    }
                }
                finally
                {
                    executor.EndTickClaims();
                }
            }

            stats.EmitIfDue(alliance);
        }

        // BC11 decision 6: one TacticalVanillaFlankMove Observation row per vanilla FlankMove (117089-117107) a group
        // starts; with Enable Tactical Observer. Smoke item 2.33 compares the rate with the tally fix on and off.
        private static void WatchVanillaFlankMoves(AIBattle battle, int alliance, BattleSnapshot snapshot)
        {
            try
            {
                if (alliance < 0 || alliance >= _flankMoves.Length) return;
                int count = _flankMoves[alliance].Observe(snapshot, _flankMoveIds, _flankMoveDirections);
                if (count == 0 || Plugin.Instance == null || !Plugin.Instance.EnableTacticalObserver.Value) return;
                int macroAi = battle.macroai;
                string stance = (TacticalBattleCoordinator.GetSideOrchestrator(alliance)?.Army?.CurrentBattleStance ?? BattleStance.Meeting).ToString();
                bool tallyFix = Plugin.EnableVanillaFlankThreatTallyFix != null && Plugin.EnableVanillaFlankThreatTallyFix.Value;
                for (int i = 0; i < count; i++)
                {
                    int groupId = _flankMoveIds[i];
                    int direction = _flankMoveDirections[i];
                    int index = snapshot.IndexOfOwn(groupId);
                    bool playerOwned = index >= 0 && (snapshot.Own[index].Flags & UnitFlags.PlayerOwned) != 0;
                    string group = index >= 0 ? PostureTelemetry.NameOf(snapshot, index) : "#" + groupId;
                    TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalVanillaFlankMove", TelemetrySeverity.Info,
                        ev => ev
                            .WithSide(alliance)
                            .WithUnit(group)
                            .WithDecision("TacticalVanillaFlankMove", direction == 0 ? "left" : "right", "side=" + alliance + "|group=" + groupId + "|direction=" + direction)
                            .WithField("direction", direction)
                            .WithField("macroai", macroAi)
                            .WithField("stance", stance)
                            .WithField("playerOwned", playerOwned)
                            .WithField("tallyFix", tallyFix));
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:flank-move-watch-failed",
                    "[BattleCore] vanilla flank-move watch threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // AIBattle.bunits is a private static field (3306) holding the battle's
        // BattleUnits.
        internal static BattleUnits ResolveBattleUnits()
        {
            try
            {
                if (!_bunitsFieldResolved)
                {
                    _bunitsFieldResolved = true;
                    _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                    if (_bunitsField == null)
                        OnceLog.Warning("tactical-battle-core:bunits-missing", "[BattleCore] AIBattle.bunits not found; the arbiter stands down");
                }
                return _bunitsField != null ? _bunitsField.GetValue(null) as BattleUnits : null;
            }
            catch
            {
                return null;
            }
        }

        private static void Reset()
        {
            _producersLogged = false;
            for (int a = 0; a < _spare.Length; a++)
            {
                _spare[a] = null;
                _builtFrame[a] = -1;
                _builtBattleKey[a] = 0;
                _stats[a].Clear();
                _flankMoves[a].Clear();
                _macroAi[a] = 0;
            }
        }

        // Per-side counters, one "[OrderArbiter]" row per 30 s real time
        // (telemetry throttle, not doctrine timing).
        private sealed class ArbiterStats
        {
            public int Intents, Winners, Issued, Moved, Failed, Retried, GaveUp, Skipped, Deduped, Superseded;
            private readonly int[] _skips = new int[SkipReasonCount];
            private float _windowStart = -1f;

            public void Skip(SkipReason reason)
            {
                if (reason == SkipReason.Duplicate) Deduped++;
                else Skipped++;
                int r = (int)reason;
                if (r >= 0 && r < _skips.Length) _skips[r]++;
            }

            public void Tally(in OrderOutcome outcome)
            {
                switch (outcome.Kind)
                {
                    case OrderOutcomeKind.Moved: Moved++; break;
                    case OrderOutcomeKind.Failed: Failed++; break;
                    case OrderOutcomeKind.Retried: Retried++; break;
                    case OrderOutcomeKind.GaveUp: GaveUp++; break;
                    case OrderOutcomeKind.Superseded: Superseded++; break;
                    case OrderOutcomeKind.Skipped: Skip(outcome.Reason); break;
                }
            }

            public void EmitIfDue(int alliance)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (_windowStart < 0f) _windowStart = now;
                if (now - _windowStart < TelemetryIntervalRealSeconds) return;
                // [OrderArbiter] has no sidecar route: LegacyInfo alone writes it
                // nowhere, so the main-log write is ours.
                TelemetryRouter.LegacyInfoToMainLogIfAllowed("[OrderArbiter] alliance=" + alliance
                    + " intents=" + Intents + " winners=" + Winners + " issued=" + Issued
                    + " moved=" + Moved + " failed=" + Failed + " retried=" + Retried
                    + " gaveUp=" + GaveUp + " skipped=" + Skipped + " deduped=" + Deduped + " superseded=" + Superseded
                    + " topSkip=" + TopSkip(), TelemetryLayer.Tactical);
                Clear();
                _windowStart = now;
            }

            public void Clear()
            {
                Intents = Winners = Issued = Moved = Failed = Retried = GaveUp = Skipped = Deduped = Superseded = 0;
                Array.Clear(_skips, 0, _skips.Length);
                _windowStart = -1f;
            }

            private SkipReason TopSkip()
            {
                int best = 0;
                for (int r = 1; r < _skips.Length; r++)
                    if (_skips[r] > _skips[best]) best = r;
                return _skips[best] > 0 ? (SkipReason)best : SkipReason.None;
            }
        }
    }
}
