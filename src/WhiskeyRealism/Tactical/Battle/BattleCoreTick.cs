using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Read-only snapshot access for patches (D1). Patches never build or write it.
    public static class BattleCore
    {
        public static bool TryGetSnapshot(int alliance, out BattleSnapshot snapshot)
        {
            snapshot = BattleCoreTick.Current(alliance);
            return snapshot != null;
        }
    }

    // The battle-core tick (plan BC4, D1/D3). Runtime only.
    //
    // EnsureSnapshot builds at most one snapshot per (battle, alliance, frame):
    // the coordinator calls it first in TacticalBattleCoordinator.Tick, which runs
    // from the CheckGlobalAIStrategy Postfix (vanilla 5866), and the arbiter patch
    // (Postfix on AdjustGroupFormations, 5870) reuses that object in the same
    // UpdateAITasks call (CalculateSideStatsAndUpdateAITasks runs aibattle[0..2]
    // in one frame, 84864-84869). Each side keeps two pooled instances (current
    // and previous).
    //
    // RunArbiter: producers (fixed order) → OrderArbiter.Resolve → outcomes of
    // the last tick's moves and their retries → OrderExecutor.Plan +
    // OrderExecutorRuntime.Execute per winner. With no producer registered it
    // returns before touching anything.
    internal static class BattleCoreTick
    {
        // Plan BC5 producer order; IBattleIntentProducer.Name is matched against it.
        // Unknown names run after these, in registration order.
        private static readonly string[] ProducerOrder =
        {
            "Withdrawal", "ArtilleryDoctrine", "ArtilleryPointBlank", "GroupStance", "ChargeDemotion", "Posture",
            "FacingPulse", "FireControl", "Play", "DivisionPlay", "ReserveRelease", "Charge",
        };

        private const float TelemetryIntervalRealSeconds = 30f;
        private const int SkipReasonCount = (int)SkipReason.LowerPriorityThanClaim + 1;

        private static IBattleIntentProducer[] _producers = new IBattleIntentProducer[16];
        private static int _producerCount;

        private static readonly BattleSnapshot[] _current = new BattleSnapshot[2];
        private static readonly BattleSnapshot[] _previous = new BattleSnapshot[2];
        private static readonly int[] _builtFrame = { -1, -1 };
        private static readonly int[] _builtBattleKey = { 0, 0 };
        private static long _tickId;

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

        internal static bool HasProducers => _producerCount > 0;

        /// <summary>Called once from each producer's static constructor.</summary>
        internal static void RegisterProducer(IBattleIntentProducer producer)
        {
            if (producer == null) return;
            for (int i = 0; i < _producerCount; i++)
                if (ReferenceEquals(_producers[i], producer)) return;
            if (_producerCount == _producers.Length) Array.Resize(ref _producers, _producers.Length * 2);

            int rank = Rank(producer.Name);
            int at = _producerCount;
            while (at > 0 && Rank(_producers[at - 1].Name) > rank)
            {
                _producers[at] = _producers[at - 1];
                at--;
            }
            _producers[at] = producer;
            _producerCount++;
        }

        internal static BattleSnapshot EnsureSnapshot(AIBattle battle, int alliance)
        {
            if (alliance < 0 || alliance > 1 || (UnityEngine.Object)battle == null) return null;
            try
            {
                BattleUnits bunits = ResolveBattleUnits();
                int battleKey = (UnityEngine.Object)bunits != null ? bunits.GetInstanceID() : 0;
                int frame = UnityEngine.Time.frameCount;
                if (_builtFrame[alliance] == frame && _builtBattleKey[alliance] == battleKey) return _current[alliance];

                BattleSnapshot reuse = _previous[alliance];
                _previous[alliance] = _current[alliance];
                unchecked { _tickId++; }
                _current[alliance] = BuildSnapshot(battle, alliance, reuse, _tickId, TacticalDoctrineTime.NowBattleHours);
                _builtFrame[alliance] = frame;
                _builtBattleKey[alliance] = battleKey;
                return _current[alliance];
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:snapshot-failed",
                    "[BattleCore] EnsureSnapshot threw: " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        internal static BattleSnapshot Current(int alliance)
        {
            return alliance >= 0 && alliance < _current.Length ? _current[alliance] : null;
        }

        // BC1 seam: BattleSnapshot implements IBattleUnitView once BC1 merges
        // (`public sealed partial class BattleSnapshot : IBattleUnitView { }`); then
        // this is the identity. Until then it is null and RunArbiter stands down.
        internal static IBattleUnitView ViewOf(BattleSnapshot snapshot)
        {
            return (object)snapshot as IBattleUnitView;
        }

        internal static void RunArbiter(AIBattle battle, int alliance)
        {
            if (_producerCount == 0) return;
            OrderArbiter arbiter = OrderArbiter.ForAlliance(alliance);
            OrderExecutor executor = OrderExecutor.ForAlliance(alliance);
            if (arbiter == null || executor == null) return;

            BattleSnapshot snapshot = EnsureSnapshot(battle, alliance);
            IBattleUnitView view = ViewOf(snapshot);
            BattleUnits bunits = ResolveBattleUnits();
            if (view == null || (UnityEngine.Object)bunits == null)
            {
                OnceLog.Warning("tactical-battle-core:snapshot-unavailable",
                    "[OrderArbiter] no battle snapshot; producers registered but the arbiter stands down");
                return;
            }

            if (!_producersLogged)
            {
                _producersLogged = true;
                OnceLog.Info("tactical-battle-core:producers", "[BattleCore] producers count=" + _producerCount + " order=" + ProducerNames());
            }
            double now = TacticalDoctrineTime.NowBattleHours;
            ArbiterStats stats = _stats[alliance];

            arbiter.BeginTick();
            for (int p = 0; p < _producerCount; p++)
            {
                IBattleIntentProducer producer = _producers[p];
                try
                {
                    producer.Produce(snapshot, arbiter);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-battle-core:producer-threw:" + producer.Name,
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
                executor.ObserveOutcomes(view, now, _outcomes, out int outcomeCount);
                for (int o = 0; o < outcomeCount; o++) stats.Tally(_outcomes[o]);

                int retries = executor.TakeRetries(_retries);
                for (int r = 0; r < retries; r++)
                {
                    if (!OrderExecutor.RetryStillClaimed(arbiter, _retries[r]))
                    {
                        stats.Superseded++;
                        continue;
                    }
                    ExecutionPlan retryPlan = executor.PlanRetry(view, _retries[r], now);
                    if (retryPlan.Execute) stats.Issued += Math.Min(1, OrderExecutorRuntime.Execute(bunits, view, executor, _retries[r], retryPlan, now));
                    else stats.Skip(retryPlan.Skip);
                }

                for (int w = 0; w < winners; w++)
                {
                    ExecutionPlan plan = executor.Plan(view, _winners[w], now);
                    if (!plan.Execute)
                    {
                        stats.Skip(plan.Skip);
                        continue;
                    }
                    stats.Issued += Math.Min(1, OrderExecutorRuntime.Execute(bunits, view, executor, _winners[w], plan, now));
                }
            }

            stats.EmitIfDue(alliance);
        }

        // BC1 seam: `return BattleSnapshotBuilder.Build(battle, alliance, reuse ?? new
        // BattleSnapshot(), tickId, doctrineHours);` once BC1 merges. Null until then.
        private static BattleSnapshot BuildSnapshot(AIBattle battle, int alliance, BattleSnapshot reuse, long tickId, double doctrineHours)
        {
            return null;
        }

        private static int Rank(string name)
        {
            for (int i = 0; i < ProducerOrder.Length; i++)
                if (string.Equals(ProducerOrder[i], name, StringComparison.Ordinal)) return i;
            return ProducerOrder.Length;
        }

        private static string ProducerNames()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _producerCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_producers[i].Name);
            }
            return sb.ToString();
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
            for (int a = 0; a < _current.Length; a++)
            {
                _current[a] = null;
                _previous[a] = null;
                _builtFrame[a] = -1;
                _builtBattleKey[a] = 0;
                _stats[a].Clear();
            }
        }

        // Per-side counters, one "[OrderArbiter]" row per 30 s real time
        // (telemetry throttle, not doctrine timing).
        private sealed class ArbiterStats
        {
            public int Intents, Winners, Issued, Pending, Moved, Failed, Retried, GaveUp, Skipped, Deduped, Superseded;
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
                    case OrderOutcomeKind.Pending: Pending++; break;
                    case OrderOutcomeKind.Moved: Moved++; break;
                    case OrderOutcomeKind.Failed: Failed++; break;
                    case OrderOutcomeKind.Retried: Retried++; break;
                    case OrderOutcomeKind.GaveUp: GaveUp++; break;
                    case OrderOutcomeKind.Skipped: Skip(outcome.Reason); break;
                }
            }

            public void EmitIfDue(int alliance)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (_windowStart < 0f) _windowStart = now;
                if (now - _windowStart < TelemetryIntervalRealSeconds) return;
                TelemetryRouter.LegacyInfo("[OrderArbiter] alliance=" + alliance
                    + " intents=" + Intents + " winners=" + Winners + " issued=" + Issued
                    + " pending=" + Pending + " moved=" + Moved + " failed=" + Failed + " retried=" + Retried
                    + " gaveUp=" + GaveUp + " skipped=" + Skipped + " deduped=" + Deduped + " superseded=" + Superseded
                    + " topSkip=" + TopSkip(), TelemetryLayer.Tactical);
                Clear();
                _windowStart = now;
            }

            public void Clear()
            {
                Intents = Winners = Issued = Pending = Moved = Failed = Retried = GaveUp = Skipped = Deduped = Superseded = 0;
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
