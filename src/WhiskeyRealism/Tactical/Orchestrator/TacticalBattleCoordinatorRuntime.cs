using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// BepInEx-coupled partial — excluded from the test assembly.
    ///
    /// Contains the real runtime entry points called from Harmony patches:
    ///   OnBattleStart  — resolves player CIC side, discovers commanders from vanilla
    ///                    BattleUnits, builds roster, calls BuildAndActivate, attaches
    ///                    ArmyOrchestrator per non-suppressed side, picks initial plan,
    ///                    emits bootstrap + plan telemetry.
    ///   OnBattleEnd    — BattleScope.ResetAll (every per-battle static), tears down orchestrators.
    ///   Tick           — cascades to per-side orchestrators; emits first-tick once-marker.
    ///
    /// All methods are try/catch guarded — this code runs inside or adjacent to Harmony
    /// patches and must never throw.
    /// </summary>
    // ============================================================
    // URGENT RECOVERY SAFETY BOUNDARY (Task 8)
    // ============================================================
    // This runtime coordinator is the ONLY place that:
    //   - Calls ExtractCurrentSignature (cheap, every frequent tick for gate input)
    //   - Calls TacticalHeavyPathGate.Decide
    //   - Calls TacticalBattleSnapshotBuilder.Build (expensive, ONLY on dec.ShouldRun)
    //   - Publishes _lastPublishedSnapshots[side] (Empty or HasData)
    //
    // Urgent recovery (#61 patch + formation/local fallback) MUST NOT be here and does not call these.
    // It consumes the published snapshot (when HasData) via orchestrator/ledger for doctrine/ledger state,
    // then ALWAYS pairs it with fresh live vanilla reads for positions, pathinterrupted, groupsubordinatesmoving,
    // local contacts, recent formation/order state (see full list in TacticalBattleRuntimeSnapshot.cs boundary doc).
    //
    // DriveTickCycle / DriveDirectChildCycle / DriveOperationsLedger share one heavy-gate
    // evaluation per side per Tick (EnsureSideSnapshot):
    //   if (!IsHeavyThrottlingEnabled()) { full build for compat }
    //   else { snap = EnsureSideSnapshot(side) — Extract + Decide once, Build+publish on Run,
    //          else the last published snapshot (may be !HasData) }
    //   then synthesize bundle/objectives from snap (degrade safe on Empty).
    //   The operations ledger additionally only re-ticks on a new snapshot, new command
    //   intents, a mode change or OperationsLedgerMaxIntervalBattleSeconds.
    //
    // All paths are try/catch guarded, degrade on error (per Tactical/AGENTS.md), use bounded OnceLog/Telemetry.
    // Battle-level dedup prevents duplicate heavy work for the two sides in one vanilla CalculateSideStats cycle.
    //
    // When snapshot !HasData or throttled: urgent recovery still fully responsive via live vanilla fields.
    // Authoritative: plan Tasks 6/7/8.
    // ============================================================
    public static partial class TacticalBattleCoordinator
    {
        // AIBattle.bunits FieldInfo, looked up once per process. The field is static
        // (decompile 3306), so it also serves the per-frame doctrine clock sampler
        // without an AIBattle instance. A failed lookup is not retried: a field a game
        // update removed does not come back, and retrying would run AccessTools.Field
        // every frame.
        private static FieldInfo _bunitsFieldCache;
        private static bool _bunitsFieldResolved;

        private static FieldInfo BunitsField
        {
            get
            {
                if (_bunitsFieldResolved) return _bunitsFieldCache;
                _bunitsFieldResolved = true;
                try
                {
                    _bunitsFieldCache = AccessTools.Field(typeof(AIBattle), "bunits");
                }
                catch
                {
                    _bunitsFieldCache = null;
                }
                if (_bunitsFieldCache == null)
                    OnceLog.Warning("orch-bunits-field-missing",
                        "[TacticalOrchestrator] AIBattle.bunits not found; doctrine clock and battle lookup disabled");
                return _bunitsFieldCache;
            }
        }

        static TacticalBattleCoordinator()
        {
            DoctrineClockFrameSource = () => UnityEngine.Time.frameCount;
            DoctrineClockSampler = SampleVanillaBattleClock;
            BattleScope.Register("doctrine-clock", ResetDoctrineClock);
            BattleScope.Register("coordinator-tick-state", ResetRuntimeTickState);
        }

        /// <summary>
        /// Raw battle clock for DoctrineBattleHours: GameVars.currenttimefromstart
        /// (SetTimeVariables 93545) and eodcycle of the battle's BattleUnits, read from
        /// the static AIBattle.bunits (assigned from GameController in AIBattle.Start,
        /// 3354). The campaign map's BattleUnits (IsCampaign, 78782) or a destroyed one
        /// is "no battle" (key 0); there currenttimefromstart is in years (30171).
        /// </summary>
        private static DoctrineClockSample SampleVanillaBattleClock()
        {
            var field = BunitsField;
            if (field == null) return default(DoctrineClockSample);
            var bunits = field.GetValue(null) as BattleUnits;
            if ((UnityEngine.Object)bunits == null || bunits.IsCampaign) return default(DoctrineClockSample);
            return new DoctrineClockSample(bunits.GetInstanceID(), GameVars.currenttimefromstart, bunits.eodcycle);
        }
        // Per-session battle counter for lifecycle log lines; never reset.
        private static int _battleSequence;
        private static int _playerAllianceId = -1;
        private static int _activeBattleUnitsId;
        private static readonly HashSet<string> _tickWarningKeys = new HashSet<string>();
        private static readonly HashSet<int> _directChildDeferLogged = new HashSet<int>();
        private static readonly Dictionary<int, int> _commandTreeTelemetrySignatures = new Dictionary<int, int>();

        // Task 6 heavy-path gate + battle-level dedup caches (per AGENTS.md runtime safety: try/catch guarded, degrade, never throw).
        // Battle-level dedup keyed by BattleUnits owner (GetInstanceID) per revised plan (not per-AIBattle lastsidestatupdate).
        // Per-side (0/1) state for last heavy time, signature, published snapshot, pending flag (pending tracked by caller per gate contract).
        private static FieldInfo _battleUnitsLastSideStatUpdateFieldCache;
        private static readonly Dictionary<int, float> _lastProcessedSideStatUpdateByBunitsId = new Dictionary<int, float>();
        private static readonly float[] _lastHeavyReviewHours = { 0f, 0f };
        private static readonly float[] _lastHeavyReviewRealtimeSeconds = { 0f, 0f };
        // Per-side doctrine hours at the side's last tick (scales with
        // 2x/5x/20x compression); ComputeCompressionAdjustedCycle's battle-time
        // source. Distinct from _lastHeavyReviewHours (the heavy-throttle
        // gate's last review time).
        private static readonly float[] _lastTickBattleHours = { -1f, -1f };
        private static readonly bool[] _replanAfterNight = { false, false };
        private static readonly TacticalBattleStateSignature[] _lastSignatures = new TacticalBattleStateSignature[2];
        private static readonly TacticalBattleRuntimeSnapshot[] _lastPublishedSnapshots = { TacticalBattleRuntimeSnapshot.Empty, TacticalBattleRuntimeSnapshot.Empty };
        private static readonly bool[] _hasPendingChange = { false, false };
        // Layer 2 frame-coalescing (2026-05-22). Per-side Unity Time.frameCount
        // of the most recent heavy snapshot build. Used to coalesce multiple
        // ShouldRun signals in the same frame into a single build per side.
        // Defense against signature-mutation churn — even when N legitimate
        // mutations happen in one frame, we build ONCE.
        private static readonly int[] _lastHeavyBuildFrame = { -1, -1 };
        // Per-Tick memo of the heavy gate (one evaluation per side per Tick).
        private static int _tickSerial;
        private static readonly int[] _sideSnapshotTickSerial = { -1, -1 };
        // Operations-ledger change gate (ShouldTickOperationsLedger).
        // Incremented on every heavy build per side (the snapshot is a struct, so the
        // ledger change gate compares generations, not references).
        private static readonly int[] _snapshotGeneration = { 0, 0 };
        private static readonly int[] _lastLedgerSnapshotGeneration = { -1, -1 };
        private static readonly int[] _directChildrenRegisteredGeneration = { -1, -1 };
        private static readonly int[] _lastLedgerIntentsHash = { 0, 0 };
        private static readonly TacticalCommanderMode[] _lastLedgerMode = new TacticalCommanderMode[2];
        private static readonly float[] _lastLedgerTickBattleSeconds = { -1f, -1f };
        // Per-battle dedupe for the intent / replan info lines (keys embed battle
        // state, so they are not OnceLog keys).
        private static readonly HashSet<long> _battleInfoLogged = new HashSet<long>();

        // "Cheap per-group" cache (2026-05-20). Populated in DriveTickCycle on
        // every heavy-gate-fire — one entry per commandable Regiment in
        // bunits.unitsused for the firing side. Read by the battle-core
        // GroupStance and ChargeDemotion producers so each arbiter tick is O(1)
        // per group (dict lookup + snapshot fields). Doctrine/ledger resolver
        // walks, sector rebuild, and DecideGroupStance recompute all run at the
        // gate-fire boundary.
        //
        // Keyed by Regiment.GetInstanceID(); a miss means no stance intent and
        // vanilla's AdjustGroupAIStance decides the group.
        //
        // Indexed by allianceId (0/1). Reset in ResetRuntimeTickState so
        // stale entries never leak between battles.
        private static readonly Dictionary<int, TacticalResolvedGroupDecision>[] _resolvedDecisionsBySide =
        {
            new Dictionary<int, TacticalResolvedGroupDecision>(),
            new Dictionary<int, TacticalResolvedGroupDecision>()
        };
        // Per-side last-seen GC.CollectionCount snapshot. Mono is Boehm
        // (non-generational) so a single counter suffices; gen0/gen1/gen2
        // return identical values. Used by EmitHeavyGateTelemetry to compute
        // per-gate-decision GC pressure delta so the tuning sidecar shows
        // whether allocation churn correlates with frame hitches at high
        // time compression. Reset per battle in ResetRuntimeTickState.
        private static readonly int[] _lastGen0CountBySide = { 0, 0 };

        // Side-stagger parity (2026-05-20, plan F). Tracks WHICH side gets
        // the heavy-build slot this Tick. Flipped at the end of every Tick
        // body so consecutive Ticks alternate side0 / side1 / side0 / ...
        // When a side's heavy-gate decides ShouldRun but parity is not its
        // turn AND it has a cached snapshot, the build is deferred (the
        // _lastHeavyReview* markers are NOT updated so the side stays
        // eligible for the next Tick). Halves the per-Update frame snapshot
        // cost (~20-25ms → ~10-12ms) at the cost of each side refreshing
        // every other heavy-gate fire instead of every one. Safe because the
        // orchestrator's coarse macro decisions tolerate one cycle of
        // staleness on the deferred side (cheap per-group patch path still
        // reads live vanilla state; only the side-wide objective record
        // snapshot is staggered). Reset to 0 in ResetRuntimeTickState.
        private static int _heavyTickParity = 0;

        /// <summary>
        /// "Cheap per-group" cache lookup. Returns the pre-resolved
        /// doctrine/ledger/charge decision for a Regiment when the most
        /// recent heavy-gate fire pre-computed one for the given alliance.
        /// Returns false on miss; the battle-core stance producers then submit
        /// nothing and vanilla decides. Indexed by alliance id (0/1);
        /// alliances &gt; 1 always miss.
        /// </summary>
        internal static bool TryGetResolvedGroupDecision(int allianceId, int instanceId, out TacticalResolvedGroupDecision resolved)
        {
            resolved = default(TacticalResolvedGroupDecision);
            if (allianceId < 0 || allianceId >= _resolvedDecisionsBySide.Length) return false;
            var dict = _resolvedDecisionsBySide[allianceId];
            return dict != null && dict.TryGetValue(instanceId, out resolved);
        }

        public static void OnBattleStart(AIBattle battle)
        {
            // A start for a different BattleUnits instance while still active means the
            // previous battle's teardown was missed (scene rebuilt without an observed
            // BattleUnits.OnDestroy 79207): tear it down first so no orchestrator, cache
            // or clock state leaks into the new battle.
            int battleUnitsId = GetBattleKeyFromBunits(battle);
            if (active)
            {
                if (battleUnitsId == _activeBattleUnitsId) return;
                OnBattleEnd();
            }
            if (!Plugin.EnableTacticalBattleOrchestrator.Value) return;
            _activeBattleUnitsId = battleUnitsId;

            try
            {
                bool aiVsAi = SafeAiVsAi();
                int playerAllianceId = aiVsAi ? -1 : SafePlayerAllianceId();
                if (!aiVsAi && playerAllianceId < 0)
                {
                    Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleStart skipped: player alliance unresolved and ai_vs_ai=false");
                    ClearForFailure();
                    return;
                }

                int suppressedAllianceId = ResolveSuppressedAllianceId(aiVsAi, playerAllianceId, ResolvePlayerCicAllianceId());
                var commanders = DiscoverCommandersFromVanilla(battle);
                // 2026-05-18: prior code called BuildFromSynthetic here, which was a stub path
                // that bypassed HistoricalFigureRegistry entirely and silently degraded every
                // commander to faction defaults (the source of the persistent TacticalCommanderUnknown
                // lines for Hood/Jackson/Beauregard/Hunter despite registry coverage). BuildFromVanilla
                // routes through HistoricalFigureRegistry.Resolve(commanderObj, allianceId, nameHint)
                // so registered figures pick up their personality vector.
                var roster = TacticalCommanderRoster.BuildFromVanilla(commanders);
                BuildAndActivate(suppressedAllianceId, roster);
                _playerAllianceId = playerAllianceId;
                _battleSequence++;

                if (Plugin.EnableTacticalOrchestratorArmy != null && Plugin.EnableTacticalOrchestratorArmy.Value)
                {
                    AttachArmyIfActive(side0, battle);
                    AttachArmyIfActive(side1, battle);
                    AttachDirectChildrenIfReady(side0);
                    AttachDirectChildrenIfReady(side1);
                    AttachCommandTreeIfReady(side0);
                    AttachCommandTreeIfReady(side1);
                }

                int suppressed = (suppressedAllianceId == 0 || suppressedAllianceId == 1) ? 1 : 0;
                int activated = 2 - suppressed;
                TelemetryRouter.LegacyInfo("[TacticalCommanderRoster] alliance=0 total=" + roster.GetSide(0).Count
                    + " matched=" + MatchedCount(roster, 0) + " unknown=" + UnknownCount(roster, 0), TelemetryLayer.Tactical);
                TelemetryRouter.LegacyInfo("[TacticalCommanderRoster] alliance=1 total=" + roster.GetSide(1).Count
                    + " matched=" + MatchedCount(roster, 1) + " unknown=" + UnknownCount(roster, 1), TelemetryLayer.Tactical);
                foreach (var entry in roster.GetSide(0))
                    if (!entry.MatchedHistoricalRegistry)
                        TelemetryRouter.LegacyInfoToMainLogIfAllowed("[TacticalCommanderUnknown] echelon=" + entry.Echelon
                            + " name=" + (string.IsNullOrEmpty(entry.Name) ? "<null>" : entry.Name), TelemetryLayer.Tactical);
                foreach (var entry in roster.GetSide(1))
                    if (!entry.MatchedHistoricalRegistry)
                        TelemetryRouter.LegacyInfoToMainLogIfAllowed("[TacticalCommanderUnknown] echelon=" + entry.Echelon
                            + " name=" + (string.IsNullOrEmpty(entry.Name) ? "<null>" : entry.Name), TelemetryLayer.Tactical);
                // Lifecycle lines are emitted once per battle (not once per session) so every
                // battle, reload and restart in a session is visible in the log.
                TelemetryRouter.LegacyInfo("[TacticalOrchestrator] bootstrap battle=" + _battleSequence
                    + " sidesActive=" + activated + " sidesSuppressed=" + suppressed, TelemetryLayer.Tactical);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleStart skipped: "
                    + e.GetType().Name + " " + e.Message);
                ClearForFailure();
            }

            // A failed bootstrap leaves the coordinator inactive; do not populate battle
            // caches nothing will consume.
            if (!active) return;

            // Stage A perf cache (2026-05-21): populate battle-lifetime scene-scan cache
            // after vanilla has built the battle scene. Subsequent heavy-gate fires read
            // from this cache instead of re-scanning the scene every cycle. Populate is
            // try/catch-safe internally; on failure the cache stays empty and live-scan
            // fallback engages (strict no-regression contract).
            try { TacticalStableSceneCache.Populate(ResolveBattleUnits(battle)); }
            catch (Exception sce)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] scene-cache populate threw: "
                    + sce.GetType().Name + " " + sce.Message);
            }
        }

        /// <summary>
        /// Battle teardown: every registered per-battle static (BattleScope) is reset
        /// — caches, ledgers, clocks, telemetry dedupe — then the orchestrators are
        /// dropped. Runs even when the coordinator never activated, so state patches
        /// filled during a battle whose bootstrap failed is still released.
        /// </summary>
        public static void OnBattleEnd()
        {
            bool wasActive = active;
            BattleScope.ResetAll();
            side0 = null;
            side1 = null;
            _playerAllianceId = -1;
            _activeBattleUnitsId = 0;
            active = false;
            if (wasActive)
                TelemetryRouter.LegacyInfo("[TacticalOrchestrator] teardown battle=" + _battleSequence, TelemetryLayer.Tactical);
        }

        public static void Tick(AIBattle battle)
        {
            if (!active) return;
            using (TelemetryPerf.Scope("tactical.orchestrator-tick", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    // Task 6 battle-level dedup guard: prevent duplicate Tick work for the two sides
                    // in the same vanilla CalculateSideStatsAndUpdateAITasks cycle.
                    // Uses BattleUnits owner (GetInstanceID) + read of lastsidestatupdate *from the BattleUnits instance* (via reflection on typeof(BattleUnits)) to
                    // detect the second per-side CheckGlobal call within one side-stat update window.
                    // Mark processed ONLY after both DriveTacticalCommanderSide calls complete (per plan contract).
                    int battleKey = GetBattleKeyFromBunits(battle);
                    float vanillaLastSideStat = SafeGetLastSideStatUpdateFromBattleUnitsOwner(battle);
                    float lastProcessed;
                    if (_lastProcessedSideStatUpdateByBunitsId.TryGetValue(battleKey, out lastProcessed)
                        && Math.Abs(vanillaLastSideStat - lastProcessed) < 0.0001f)
                    {
                        // Second side's CheckGlobal in same vanilla cycle — already drove both sides; skip.
                        return;  // note: return is from the try block, outer using/scope still ends cleanly
                    }

                    OnceLog.Info("orch-coordinator", "[TacticalOrchestrator] coordinator first tick");
                    EndOfDayPhase dayPhase = ObserveEndOfDay(SafeEodCycle(battle));
                    SampleDoctrineClock();
                    if (dayPhase == EndOfDayPhase.Suspended)
                    {
                        MarkSideStatCycleProcessed(battleKey, vanillaLastSideStat);
                        return;
                    }
                    if (dayPhase == EndOfDayPhase.Resumed) PrepareForNewBattleDay();
                    unchecked { _tickSerial++; }
                    bool aiVsAi = SafeAiVsAi();

                    // Battle core (D1): one pooled BattleSnapshot per side per frame,
                    // built before any evidence read; the arbiter patch at 5870 reuses
                    // it in this UpdateAITasks call. Every evidence, signature, sector
                    // and posture-input read below takes it (SideSnapshot). The observer
                    // postfix already built it this frame; this call is then a no-op.
                    EnsureBattleCoreSnapshots(battle);

                    // Drive each side; the heavy-gate publishes into
                    // _lastPublishedSnapshots[s] when it fires.
                    DriveTacticalCommanderSide(side0, battle, aiVsAi);
                    DriveTacticalCommanderSide(side1, battle, aiVsAi);

                    // Plan F (2026-05-20): flip the heavy-build parity so the
                    // NEXT vanilla side-stat cycle's Tick alternates which side
                    // gets the build slot. See _heavyTickParity field comment.
                    _heavyTickParity = 1 - _heavyTickParity;

                    // Stage D (2026-05-21): periodic cache validation. Internally
                    // rate-limited to once per 30s wallclock so the per-Tick cost
                    // is a single comparison + early-return on most ticks. Emits
                    // TacticalCacheValidation events on each run; warnings on
                    // any mismatch so smoke catches silent cache staleness.
                    try { TacticalCacheValidator.ValidateIfDue(); }
                    catch (Exception cve)
                    {
                        OnceLog.Warning("orch-cache-validator", "[TacticalOrchestrator] cache-validator threw: "
                            + cve.GetType().Name + " " + cve.Message);
                    }

                    MarkSideStatCycleProcessed(battleKey, vanillaLastSideStat);
                }
                catch (Exception e)
                {
                    OnceLog.Warning("orch-tick-skipped", "[TacticalOrchestrator] Tick skipped: "
                        + e.GetType().Name + " " + e.Message);
                }
            }
        }

        // ---- Private runtime helpers ----

        /// <summary>
        /// Builds the battle snapshot of every AI-run side (vanilla's AI plays the alliance:
        /// both under ai_vs_ai, which W&amp;L forces, else the non-player alliance), each from
        /// that side's own AIBattle. Infrastructure, not doctrine (D1, amended in BC2b): it
        /// runs whenever vanilla's battle AI runs, whatever the orchestrator flag and the
        /// Commander Mode, because fog-honest readers (evidence, TacticalFogOfWarContact,
        /// observer telemetry) run in every mode and the snapshot is a per-frame cache with no
        /// writes. Called from the CheckGlobalAIStrategy postfix (vanilla 5866) and again
        /// first in Tick; the per-frame cache makes the second call free. The plugin's master
        /// "Enabled" switch still stops it, as it stops Plugin.Update's per-frame work (a
        /// switch off at load applies no patch at all). Never throws.
        /// </summary>
        public static void EnsureBattleCoreSnapshots(AIBattle battle)
        {
            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null || plugin.Enabled == null || !plugin.Enabled.Value) return;
                if ((UnityEngine.Object)battle == null) return;
                bool aiVsAi = SafeAiVsAi();
                int playerAllianceId = aiVsAi ? -1 : SafePlayerAllianceId();
                for (int alliance = 0; alliance < 2; alliance++)
                {
                    if (!ShouldRunTacticalCommanderForSide(alliance, playerAllianceId, aiVsAi)) continue;
                    AIBattle sideBattle = ArmyEvidenceBuilder.ResolveAiBattleForAlliance(battle, alliance);
                    Battle.BattleCoreTick.EnsureSnapshot(sideBattle, alliance);
                }
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-battle-core:ensure-snapshots", "[BattleCore] snapshot build skipped: "
                    + e.GetType().Name + " " + e.Message);
            }
        }

        // The side's battle snapshot built at the top of this Tick (EnsureBattleCoreSnapshots);
        // null only when the build failed, which every reader degrades on.
        private static Battle.BattleSnapshot SideSnapshot(TacticalBattleOrchestrator side)
        {
            return side != null && Battle.BattleCore.TryGetSnapshot(side.AllianceId, out Battle.BattleSnapshot snapshot) ? snapshot : null;
        }

        // Mark as processed for this vanilla side-stat cycle (the lastsidestatupdate value
        // vanilla set before calling CheckGlobalAIStrategy, 84572-84574).
        private static void MarkSideStatCycleProcessed(int battleKey, float vanillaLastSideStat)
        {
            if (battleKey != 0 && vanillaLastSideStat > 0f)
            {
                _lastProcessedSideStatUpdateByBunitsId[battleKey] = vanillaLastSideStat;
            }
        }

        private static int SafeEodCycle(AIBattle battle)
        {
            try
            {
                var bunits = ResolveBattleUnits(battle);
                return bunits != null ? bunits.eodcycle : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// First fighting cycle of a new battle day: force a heavy rebuild for both sides
        /// (AI groups may have been redeployed by DoPlacementAIUnitsWithinDeploymentzoneNew,
        /// 85524, when the deployment panel closed) and queue a replan on the fresh
        /// evidence. Orchestrators, ledgers and plan memory are kept, as vanilla keeps its
        /// AI state.
        /// </summary>
        private static void PrepareForNewBattleDay()
        {
            for (int i = 0; i < 2; i++)
            {
                _lastHeavyReviewHours[i] = 0f;
                _lastHeavyReviewRealtimeSeconds[i] = 0f;
                _lastHeavyBuildFrame[i] = -1;
                _replanAfterNight[i] = true;
            }
            TelemetryRouter.LegacyInfo("[TacticalOrchestrator] new battle day: snapshots rebuilt and plans re-evaluated", TelemetryLayer.Tactical);
        }

        private static bool SafeAiVsAi()
        {
            try { return GameVars.ai_vs_ai; }
            catch { return false; }
        }

        private static int SafePlayerAllianceId()
        {
            try
            {
                int allianceId = GameVars.playeralliance;
                return allianceId >= 0 && allianceId <= 1 ? allianceId : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static void DriveTacticalCommanderSide(
            TacticalBattleOrchestrator side,
            AIBattle battle,
            bool aiVsAi)
        {
            if (side == null) return;
            if (!ShouldRunTacticalCommanderForSide(side.AllianceId, _playerAllianceId, aiVsAi)) return;

            // Every per-side read (snapshot, objective records, objectivechain,
            // macroai) uses this side's own AIBattle, not whichever instance fired
            // the tick: each AIBattle's objectivechain holds only its own side's
            // groups (aibattle[] assignment 85203-85212, SetSide 3408).
            AIBattle sideBattle = ArmyEvidenceBuilder.ResolveAiBattleForAlliance(battle, side.AllianceId);

            if (Plugin.EnableTacticalOrchestratorIntentInference != null
                && Plugin.EnableTacticalOrchestratorIntentInference.Value)
            {
                DriveTickCycle(side, sideBattle);
                AttachDirectChildrenIfReady(side);
                AttachCommandTreeIfReady(side);
                DriveDirectChildCycle(side, sideBattle);
            }

            RefreshPostureInputs(side, sideBattle);
            DriveOperationsLedger(side, sideBattle);
        }

        /// <summary>
        /// #61's orchestrator inputs (leaf brigade map, live commander initiative,
        /// reinforcement opportunity), once per side per tick, ahead of that side's
        /// next AIBattle.UpdateAITasks.
        /// </summary>
        private static void RefreshPostureInputs(TacticalBattleOrchestrator side, AIBattle sideBattle)
        {
            if (side.Army == null) return;
            var bunits = ResolveBattleUnits(sideBattle);
            if (bunits == null || bunits.alliance == null) return;
            for (int s = 0; s < 2 && s < bunits.alliance.Length; s++)
            {
                if (bunits.alliance[s] != side.AllianceId) continue;
                ArmyPostureInputsRuntime.Refresh(sideBattle, bunits, s, side.Army);
                return;
            }
        }

        private static void DriveTickCycle(TacticalBattleOrchestrator side, AIBattle battle)
        {
            try
            {
                if (side == null || side.Army == null) return;
                int sideIdx = side.AllianceId;
                if (sideIdx < 0 || sideIdx > 1) sideIdx = 0;
                TickReplanStep step = DecideTickReplanStep(_replanAfterNight[sideIdx], side.Army.HasPlan);
                if (step == TickReplanStep.None)
                {
                    _replanAfterNight[sideIdx] = false; // no plan to re-evaluate
                    return;
                }

                ArmyEvidenceBuilder.Bundle bundleForReplan = IsHeavyThrottlingEnabled()
                    ? BundleFromSnapshot(EnsureSideSnapshot(side, battle))
                    // Throttling off: pre-Task-6 behaviour, full build every tick.
                    : ArmyEvidenceBuilder.Build(SideSnapshot(side), battle);

                int minReplanSeconds = (Plugin.TacticalOrchestratorMinReplanSeconds != null)
                    ? Plugin.TacticalOrchestratorMinReplanSeconds.Value
                    : 60;

                // Last doctrine hours this side ticked at; the heavy gate's
                // compression estimate (ComputeCompressionAdjustedCycle) reads it.
                float nowBattleHours = (float)DoctrineBattleHours;
                _lastTickBattleHours[sideIdx] = nowBattleHours;

                if (step == TickReplanStep.NightReplan)
                {
                    ReplanForNewBattleDay(side.Army, bundleForReplan.OwnEvidence, bundleForReplan.EnemyVisible);
                    // The night replan counts against "Min Replan Seconds" like any other.
                    ArmyTickCycle.RecordReplan(side.AllianceId);
                    // Cleared only once the replan has run; a throw retries next tick.
                    _replanAfterNight[sideIdx] = false;
                    TelemetryRouter.LegacyInfo("[TacticalReplan] side=" + side.AllianceId
                        + " trigger=NewBattleDay newPlan=" + side.Army.CurrentPlan.PlanId
                        + " phase=" + side.Army.CurrentPlan.Phase, TelemetryLayer.Tactical);
                    return;
                }

                // Plan age, phase age and the replan cooldown run on doctrine
                // time; the wall-clock delta is unused when it is supplied.
                var trigger = ArmyTickCycle.MaybeReplan(
                    side.Army,
                    deltaSeconds: 0f,
                    ownEvidence: bundleForReplan.OwnEvidence,
                    enemyVisible: bundleForReplan.EnemyVisible,
                    ownMainEffortStrength: bundleForReplan.OwnMainEffortStrength,
                    ownArmyMorale: bundleForReplan.OwnArmyMorale,
                    ownReservesCommittedFraction: bundleForReplan.OwnReservesCommittedFraction,
                    ownReinforcementsArriving: bundleForReplan.OwnReinforcementsArriving,
                    minReplanSeconds: minReplanSeconds,
                    ownAvgFatigue01: bundleForReplan.OwnAvgFatigue01,
                    ownAvgAmmo01: bundleForReplan.OwnAvgAmmo01,
                    nearestReinforcementHours: bundleForReplan.NearestReinforcementHours,
                    nearestReinforcementStrength: bundleForReplan.NearestReinforcementStrength,
                    doctrineNowSeconds: TacticalDoctrineTime.NowBattleSeconds);

                var intent = side.Army.CurrentIntentModel;
                if (intent.PrimaryIntent != InferredIntent.Unknown)
                {
                    long intentKey = ((long)side.AllianceId << 56) ^ ((long)(int)intent.PrimaryIntent << 32) ^ (uint)intent.InferredMainEffort;
                    if (_battleInfoLogged.Add(intentKey))
                        TelemetryRouter.LegacyInfo("[TacticalIntent] side=" + side.AllianceId
                        + " seesEnemy=" + intent.PrimaryIntent
                        + " mainEffort=" + intent.InferredMainEffort
                        + " confidence=" + intent.Confidence01.ToString("0.00"), TelemetryLayer.Tactical);
                }

                if (trigger != ReplanTrigger.None)
                {
                    long replanKey = (1L << 62) ^ ((long)side.AllianceId << 56) ^ ((long)(int)trigger << 32) ^ (uint)(int)side.Army.CurrentPlan.PlanId;
                    if (_battleInfoLogged.Add(replanKey))
                        TelemetryRouter.LegacyInfo("[TacticalReplan] side=" + side.AllianceId
                        + " trigger=" + trigger
                        + " newPlan=" + side.Army.CurrentPlan.PlanId
                        + " phase=" + side.Army.CurrentPlan.Phase, TelemetryLayer.Tactical);
                }
            }
            catch (Exception e)
            {
                WarnTickCycleOnce(side, e);
            }
        }

        private static void WarnTickCycleOnce(TacticalBattleOrchestrator side, Exception e)
        {
            try
            {
                string sideKey = side == null ? "null" : side.AllianceId.ToString();
                string key = sideKey + ":" + e.GetType().Name;
                if (_tickWarningKeys.Contains(key)) return;
                _tickWarningKeys.Add(key);
                Plugin.Log.LogWarning("[TacticalOrchestrator] Army tick skipped side="
                    + sideKey + ": " + e.GetType().Name + " " + e.Message);
            }
            catch { }
        }

        private static void DriveOperationsLedger(TacticalBattleOrchestrator side, AIBattle battle)
        {
            using (TelemetryPerf.Scope("tactical.operations-ledger", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    var plugin = Plugin.Instance;
                    if (side == null || side.Army == null || plugin == null) return;
                    TacticalCommanderMode mode = plugin.TacticalCommanderModeValue;
                    if (!TacticalCommanderModePolicy.RunsLedger(mode))
                    {
                        int offSide = side.AllianceId;
                        if (offSide >= 0 && offSide <= 1) _lastLedgerMode[offSide] = mode;
                        side.OperationsLedger.SetRuntimeClock(LedgerClockSecondsFromBattleHours((float)DoctrineBattleHours));
                        side.TickOperationsLedger(
                            mode,
                            Array.Empty<ObjectiveRecord>(),
                            StrategicBattleIntentSnapshot.Empty,
                            new ForceAvailabilitySnapshot(0f, 0f),
                            side.Army.CommanderPersonality);
                        return;
                    }

                    IReadOnlyList<ObjectiveRecord> objectivesToUse;
                    ArmyEvidenceBuilder.Bundle bundleForStrategic;
                    float forceMain, forceReserveAvail;
                    int s = side.AllianceId;
                    if (s < 0 || s > 1) s = 0;
                    float nowBattleSeconds = LedgerClockSecondsFromBattleHours((float)DoctrineBattleHours);
                    if (!IsHeavyThrottlingEnabled())
                    {
                        // Preserve exact pre-Task-6 behavior (builds every ledger tick)
                        var snapshot = SideSnapshot(side);
                        var bundle = ArmyEvidenceBuilder.Build(snapshot, battle);
                        var objectives = TacticalBattleSnapshotBuilder.BuildObjectiveRecords(snapshot, battle);
                        objectivesToUse = objectives;
                        bundleForStrategic = bundle;
                        forceMain = bundle.OwnMainEffortStrength;
                        forceReserveAvail = Math.Max(0f, 1f - Clamp01(bundle.OwnReservesCommittedFraction));
                    }
                    else
                    {
                        // Gated: the side's snapshot for this Tick (heavy gate evaluated once).
                        var snap = EnsureSideSnapshot(side, battle);
                        int intentsHash = CommandNodeIntentsHash(side.Army.CurrentCommandNodeIntents);
                        bool inputsChanged = _snapshotGeneration[s] != _lastLedgerSnapshotGeneration[s]
                            || intentsHash != _lastLedgerIntentsHash[s];
                        if (!ShouldTickOperationsLedger(inputsChanged, mode != _lastLedgerMode[s], nowBattleSeconds, _lastLedgerTickBattleSeconds[s]))
                            return;
                        _lastLedgerSnapshotGeneration[s] = _snapshotGeneration[s];
                        _lastLedgerIntentsHash[s] = intentsHash;
                        objectivesToUse = snap.Objectives;
                        bundleForStrategic = BundleFromSnapshot(snap);
                        forceMain = snap.OwnMainEffortStrength;
                        forceReserveAvail = Math.Max(0f, 1f - Clamp01(snap.OwnReservesCommittedFraction));
                    }
                    _lastLedgerMode[s] = mode;
                    _lastLedgerTickBattleSeconds[s] = nowBattleSeconds;

                    var strategic = BuildStrategicBattleIntentSnapshot(side, bundleForStrategic);
                    var force = new ForceAvailabilitySnapshot(forceMain, forceReserveAvail);

                    side.OperationsLedger.SetRuntimeClock(nowBattleSeconds);
                    side.TickOperationsLedger(
                        mode,
                        objectivesToUse,
                        strategic,
                        force,
                        side.Army.CommanderPersonality);
                    side.OperationsLedger.SetOperationAssignedStrength(
                        MeasureOperationAssignedStrength(side.Army.CurrentCommandOperations));
                }
                catch (Exception e)
                {
                    WarnTickCycleOnce(side, e);
                }
            }
        }

        /// <summary>
        /// T14: live strength of the command nodes the current operation has tasked to
        /// attack or support. Each node's units are summed once (a corps and its division
        /// may both be tasked) from Regiment.allattachedunits (vanilla's attachment list,
        /// UpdateAllAttachedUnits 129573), filtered by CountsTowardOperationStrength.
        /// 0 when no node is tasked.
        /// </summary>
        private static float MeasureOperationAssignedStrength(IReadOnlyList<CommandNodeOperationalState> states)
        {
            try
            {
                if (states == null || states.Count == 0) return 0f;
                bool mapBuilt = false;
                _countedUnitIds.Clear();
                float total = 0f;
                for (int i = 0; i < states.Count; i++)
                {
                    var task = states[i].Task;
                    if (task != CommandTaskType.AttackObjective && task != CommandTaskType.SupportAttack) continue;
                    int instanceId = ParseCommandNodeInstanceId(states[i].NodeId);
                    if (instanceId == 0) continue;
                    if (!mapBuilt)
                    {
                        FillGameObjectInstanceMap(_regimentByGameObjectId);
                        mapBuilt = true;
                    }
                    if (!_regimentByGameObjectId.TryGetValue(instanceId, out Regiment group) || group == null) continue;
                    var units = group.allattachedunits;
                    if (units == null) continue;
                    for (int u = 0; u < units.Length; u++)
                    {
                        var unit = units[u];
                        if (unit == null) continue;
                        if (!CountsTowardOperationStrength(
                                unit.unittyp,
                                unit.permanentlydetached,
                                unit.isrouted,
                                unit.markedforrout,
                                unit.gameObject.activeInHierarchy,
                                unit.dlcw_isundercommander))
                            continue;
                        if (!_countedUnitIds.Add(unit.GetInstanceID())) continue;
                        total += Math.Max(0f, unit.strength);
                    }
                }
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        // Reused across ledger ticks (main thread only) to avoid per-tick allocation.
        private static readonly Dictionary<int, Regiment> _regimentByGameObjectId = new Dictionary<int, Regiment>();
        private static readonly HashSet<int> _countedUnitIds = new HashSet<int>();

        private static void FillGameObjectInstanceMap(Dictionary<int, Regiment> map)
        {
            map.Clear();
            var units = BattleUnits.completeunitlist;
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                var reg = units[i];
                if (reg == null) continue;
                map[reg.gameObject.GetInstanceID()] = reg;
            }
        }

        /// <summary>
        /// The side's runtime snapshot for the current Tick. The heavy gate (cheap
        /// signature extract + Decide + telemetry) is evaluated once per side per Tick
        /// and shared by DriveTickCycle, DriveDirectChildCycle and DriveOperationsLedger
        /// (previously each re-evaluated it). On ShouldRun — unless it is the other
        /// side's stagger slot or this side already built in this frame — the heavy
        /// snapshot is rebuilt, published and the per-group resolved decisions refreshed.
        /// </summary>
        private static TacticalBattleRuntimeSnapshot EnsureSideSnapshot(TacticalBattleOrchestrator side, AIBattle battle)
        {
            int s = side.AllianceId;
            if (s < 0 || s > 1) s = 0;
            if (_sideSnapshotTickSerial[s] == _tickSerial) return _lastPublishedSnapshots[s];
            _sideSnapshotTickSerial[s] = _tickSerial;

            float nowH = (float)DoctrineBattleHours;
            Battle.BattleSnapshot battleSnapshot = SideSnapshot(side);
            var currSig = TacticalBattleSnapshotBuilder.ExtractCurrentSignature(battleSnapshot, battle, nowH);
            float lastH = _lastHeavyReviewHours[s];
            float nowReal = SafeRealtimeSeconds();
            float lastReal = _lastHeavyReviewRealtimeSeconds[s];
            var lastS = _lastSignatures[s];
            bool hasP = _hasPendingChange[s];
            float cycle = ComputeCompressionAdjustedCycle(s, nowH, nowReal, lastReal);
            float minReal = (Plugin.Instance != null ? Plugin.Instance.HeavyReviewMinRealtimeSeconds : 2.0f);
            var input = new TacticalHeavyPathGate.Input(currSig, nowH, lastH, lastS, cycle, hasP, nowReal, lastReal, minReal);
            var dec = TacticalHeavyPathGate.Decide(input);
            EmitHeavyGateTelemetry(s, dec, nowH, lastH, cycle, hasP, currSig, nowReal, lastReal, minReal);

            // Plan F (2026-05-20): the heavy build alternates between sides via
            // _heavyTickParity (flipped at the end of Tick). Off-slot with a cached
            // snapshot: defer; _lastHeavyReview* stay put so the gate re-signals next Tick.
            // A clock rewind never defers (the cached snapshot belongs to the old clock).
            bool staggerSkip = dec.ShouldRun
                && dec.Reason != "clock-rewind"
                && dec.Reason != "first-tick"
                && s != _heavyTickParity
                && _lastPublishedSnapshots[s].HasData;
            // Layer 2 frame-coalescing (2026-05-22): at most one heavy build per side per frame.
            int frameNow = UnityEngine.Time.frameCount;
            bool sameFrameAsLastBuild = _lastHeavyBuildFrame[s] == frameNow;

            if (dec.ShouldRun && !staggerSkip && !sameFrameAsLastBuild)
            {
                _lastHeavyBuildFrame[s] = frameNow;
                TacticalBattleRuntimeSnapshot snap;
                using (TelemetryPerf.Scope("tactical.snapshot-build", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                {
                    snap = TacticalBattleSnapshotBuilder.Build(battleSnapshot, battle, currSig, nowH);
                }
                _lastHeavyReviewHours[s] = nowH;
                _lastHeavyReviewRealtimeSeconds[s] = nowReal;
                _lastSignatures[s] = currSig;
                _lastPublishedSnapshots[s] = snap;
                _hasPendingChange[s] = false;
                unchecked { _snapshotGeneration[s]++; }

                // "Cheap per-group" pre-compute (2026-05-20): resolved per-Regiment
                // decisions read by the battle-core stance producers.
                using (TelemetryPerf.Scope("tactical.resolved-group-decisions.build", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                {
                    PopulateResolvedDecisionsForSide(side, battle, battleSnapshot, snap.EnemyVisible);
                }
                return snap;
            }

            if (!currSig.SignatureEquals(lastS) && !hasP)
            {
                _hasPendingChange[s] = true;
            }
            return _lastPublishedSnapshots[s];
        }

        private static ArmyEvidenceBuilder.Bundle BundleFromSnapshot(TacticalBattleRuntimeSnapshot snap)
        {
            // Readiness fields propagate so the throttled path keeps the same
            // fresh-troops decision as the direct path.
            return new ArmyEvidenceBuilder.Bundle(
                snap.OwnEvidence,
                snap.EnemyVisible,
                snap.OwnMainEffortStrength,
                snap.OwnArmyMorale,
                snap.OwnReservesCommittedFraction,
                snap.OwnReinforcementsArriving,
                ownAvgFatigue01: snap.OwnAvgFatigue01,
                ownAvgAmmo01: snap.OwnAvgAmmo01,
                nearestReinforcementHours: snap.NearestReinforcementHours,
                nearestReinforcementStrength: snap.NearestReinforcementStrength);
        }

        /// <summary>
        /// Computes the compression-adjusted heavy-gate cycle for a given
        /// side, using the side's last-tick battle-hours and last-heavy
        /// realtime as the ratio source. At 1x compression returns base
        /// cycle unchanged; at 20x returns ~5x base (54 battle-seconds).
        /// </summary>
        private static float ComputeCompressionAdjustedCycle(int sideIdx, float nowBattleHours, float nowRealtime, float lastRealtime)
        {
            float baseCycle = (Plugin.Instance != null ? Plugin.Instance.HeavyReviewCycleHours : 0.003f);
            if (sideIdx < 0 || sideIdx > 1) return baseCycle;
            float lastBattleH = _lastTickBattleHours[sideIdx];
            float observedRatio = 1f;
            if (lastBattleH > 0f && lastRealtime > 0f && nowRealtime > lastRealtime && nowBattleHours > lastBattleH)
            {
                float battleDelta = (nowBattleHours - lastBattleH) * 3600f;
                float realDelta = nowRealtime - lastRealtime;
                if (realDelta > 0.01f) observedRatio = battleDelta / realDelta;
            }
            return TacticalHeavyPathGate.ScaleCycleForCompression(baseCycle, observedRatio);
        }

        private static float SafeRealtimeSeconds()
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (float.IsNaN(now) || float.IsInfinity(now) || now < 0f) return 0f;
                return now;
            }
            catch
            {
                return 0f;
            }
        }

        // ---- Task 6 helpers (all try/catch guarded per runtime safety; degrade on reflection failure) ----
        private static bool IsHeavyThrottlingEnabled()
        {
            try
            {
                var p = Plugin.Instance;
                if (p == null || p.EnableTacticalHeavyPathThrottling == null) return false;
                return p.EnableTacticalHeavyPathThrottling.Value;
            }
            catch
            {
                return false;
            }
        }

        private static float SafeGetLastSideStatUpdateFromBattleUnitsOwner(AIBattle battle)
        {
            try
            {
                if (battle == null) return 0f;
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return 0f;
                if (_battleUnitsLastSideStatUpdateFieldCache == null)
                    _battleUnitsLastSideStatUpdateFieldCache = AccessTools.Field(typeof(BattleUnits), "lastsidestatupdate");
                if (_battleUnitsLastSideStatUpdateFieldCache == null) return 0f;
                object val = _battleUnitsLastSideStatUpdateFieldCache.GetValue(bunits);
                return val is float f ? f : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static int GetBattleKeyFromBunits(AIBattle battle)
        {
            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits != null)
                {
                    // Use BattleUnits instance ID as stable battle-level owner key (per plan: battle-level via BattleUnits owner)
                    return bunits.GetInstanceID();
                }
                return battle != null ? battle.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static StrategicBattleIntentSnapshot BuildStrategicBattleIntentSnapshot(
            TacticalBattleOrchestrator side,
            ArmyEvidenceBuilder.Bundle bundle)
        {
            var personality = side != null && side.Army != null
                ? side.Army.CommanderPersonality
                : default(PersonalityVector);
            string currentPlan = side != null && side.Army != null && side.Army.HasPlan
                ? side.Army.CurrentPlan.PlanId.ToString()
                : string.Empty;
            string intent = side != null && side.Army != null
                ? side.Army.CurrentIntentModel.PrimaryIntent.ToString()
                : string.Empty;

            return new StrategicBattleIntentSnapshot(
                casualtyPressure: Clamp01(1f - bundle.OwnArmyMorale),
                timePressure: 0f,
                theaterIntent: intent,
                campaignIntent: currentPlan,
                allianceId: side == null ? -1 : side.AllianceId,
                campaignObjectiveId: currentPlan,
                theaterPriority: Clamp01(bundle.OwnEvidence.CurrentOdds / 2f),
                casualtyTolerance: personality.CasualtyTolerance,
                preserveForceBias: Clamp01((personality.Caution + 1f) * 0.5f),
                commanderPersonality: personality);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static int ResolvePlayerCicAllianceId()
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return -1;
                if (!DLC_WL.IsCommanderInChief()) return -1;
                int chosen = DLC_WL.dlc_chosencommander;
                if (chosen < 0 || chosen >= GameVars.commander.Count) return -1;
                return GameVars.commander[chosen].alliance;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] ResolvePlayerCicAllianceId failed (W&L suppression gate disabled until next battle): " + e.GetType().Name + " " + e.Message);
                return -1;
            }
        }

        private static IEnumerable<VanillaCommanderInput> DiscoverCommandersFromVanilla(AIBattle battle)
        {
            var inputs = new List<VanillaCommanderInput>();
            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return inputs;
                for (int side = 0; side < 2; side++)
                {
                    int allianceId = SafeAllianceForSide(bunits, side);
                    if (allianceId < 0 || allianceId >= 2)
                    {
                        if (allianceId >= 2)
                            TelemetryRouter.LegacyInfo("[TacticalOrchestrator] skipping side=" + side + " alliance=" + allianceId + " (Europe/non-belligerent)", TelemetryLayer.Tactical);
                        continue;
                    }
                    int commanderId = SafeCommanderId(bunits, side);
                    object commanderObj = SafeCommanderObject(commanderId);
                    string name = SafeCommanderName(commanderId);
                    if (string.IsNullOrEmpty(name)) name = "ArmyCO_side" + side;
                    inputs.Add(new VanillaCommanderInput(commanderObj, name, EchelonKind.Army, allianceId));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] DiscoverCommandersFromVanilla degraded: "
                    + e.GetType().Name + " " + e.Message);
            }
            return inputs;
        }

        private static object SafeCommanderObject(int commanderId)
        {
            try
            {
                if (commanderId < 0) return null;
                if (GameVars.commander == null || commanderId >= GameVars.commander.Count) return null;
                return GameVars.commander[commanderId];
            }
            catch
            {
                return null;
            }
        }

        private static BattleUnits ResolveBattleUnits(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;
                return BunitsField?.GetValue(battle) as BattleUnits;
            }
            catch
            {
                return null;
            }
        }

        private static int SafeAllianceForSide(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                if (side < 0 || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeCommanderId(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null) return -1;
                return bunits.GetCommandingOfficerFromSide(side);
            }
            catch
            {
                return -1;
            }
        }

        private static string SafeCommanderName(int commanderId)
        {
            try
            {
                if (commanderId < 0) return null;
                if (GameVars.commander == null || commanderId >= GameVars.commander.Count) return null;
                return GameVars.commander[commanderId].name;
            }
            catch
            {
                return null;
            }
        }

        private static CommanderRosterEntry FindArmyEntry(TacticalBattleOrchestrator side)
        {
            try
            {
                if (side == null || side.Roster == null) return null;
                foreach (var entry in side.Roster.GetSide(side.AllianceId))
                {
                    if (entry != null && entry.Echelon == EchelonKind.Army) return entry;
                }
            }
            catch { }
            return null;
        }

        private static void AttachArmyIfActive(TacticalBattleOrchestrator side, AIBattle battle)
        {
            if (side == null) return;
            var armyEntry = FindArmyEntry(side);
            if (armyEntry == null) return;
            var army = new ArmyOrchestrator(side.AllianceId, BuiltInPlaybooks.SeedCatalog(), armyEntry.PersonalityVector);
            side.AttachArmy(army);
            AIBattle sideBattle = ArmyEvidenceBuilder.ResolveAiBattleForAlliance(battle, side.AllianceId);
            // Battle start runs before the first coordinator Tick: build the side's snapshot
            // here (D1; the first Tick in the same frame reuses it).
            var snapshot = Battle.BattleCoreTick.EnsureSnapshot(sideBattle, side.AllianceId);
            army.PickInitialPlan(ArmyEvidenceBuilder.Build(snapshot, sideBattle).OwnEvidence);
            if (army.HasPlan)
            {
                TelemetryRouter.LegacyInfo("[TacticalPlan] " + SideLogContext(side.AllianceId)
                    + " plan=" + army.CurrentPlan.PlanId
                    + " phase=" + army.CurrentPlan.Phase
                    + " mainEffort=" + army.CurrentPlan.MainEffortSector, TelemetryLayer.Tactical);
            }
        }

        private static void AttachDirectChildrenIfReady(TacticalBattleOrchestrator side)
        {
            // Hot-path measurement scope (2026-05-19 hitch investigation).
            // Wraps the full discovery-snapshot + register flow so the tuning
            // sidecar shows the dominant cost when probing why 20x compression
            // hitches on small battles. No behavior change — pure measurement.
            using (TelemetryPerf.Scope("tactical.attach-direct-children", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            try
            {
                if (side == null || side.Army == null || !side.Army.HasPlan) return;
                int s = side.AllianceId;
                if (s < 0 || s > 1) s = 0;
                IReadOnlyList<DirectChildSnapshot> snapshots;
                if (IsHeavyThrottlingEnabled() && _lastPublishedSnapshots[s].HasData)
                {
                    // The heavy snapshot already carries this side's direct-child
                    // discovery (GetAttachedUnitsReg walk); re-register only when a new
                    // snapshot was built instead of re-walking the hierarchy every Tick.
                    if (_directChildrenRegisteredGeneration[s] == _snapshotGeneration[s]
                        && side.Army.CurrentDirectChildIntents.Count > 0)
                        return;
                    _directChildrenRegisteredGeneration[s] = _snapshotGeneration[s];
                    snapshots = _lastPublishedSnapshots[s].DirectChildSnapshots;
                }
                else
                {
                    using (TelemetryPerf.Scope("tactical.direct-child-discovery-snapshot", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                    {
                        snapshots = DirectChildDiscovery.Snapshot(side.AllianceId);
                    }
                }
                if (snapshots.Count == 0)
                {
                    if (side.Army.CurrentDirectChildIntents.Count == 0 &&
                        !_directChildDeferLogged.Contains(side.AllianceId))
                    {
                        _directChildDeferLogged.Add(side.AllianceId);
                        TelemetryRouter.LegacyInfo("[TacticalDirectChildDiscovery] deferred side=" + side.AllianceId
                            + " reason=empty-or-no-command-units", TelemetryLayer.Tactical);
                    }
                    return;
                }

                if (!side.Army.RegisterDirectChildrenIfChanged(snapshots)) return;

                string parentCommandId = snapshots.Count > 0 ? snapshots[0].ParentArmyId : string.Empty;
                var parentCommand = ResolveCommandLabelById(parentCommandId, "army-");
                TelemetryRouter.LegacyInfo("[TacticalDirectChildDiscovery] " + SideLogContext(side.AllianceId, parentCommand, default(CommandLogLabel))
                    + " parentCommandId=" + SafeLog(parentCommandId)
                    + " shift=" + (snapshots.Count > 0 ? snapshots[0].CommandHierarchyShift : 0)
                    + " children=" + snapshots.Count
                    + " synthetic=" + IsSynthetic(snapshots), TelemetryLayer.Tactical);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] AttachDirectChildrenIfReady skipped side="
                    + (side == null ? "null" : side.AllianceId.ToString())
                    + ": " + e.GetType().Name + " " + e.Message);
            }
        }

        private static bool IsSynthetic(IReadOnlyList<DirectChildSnapshot> snaps)
        {
            for (int i = 0; i < snaps.Count; i++)
                if (snaps[i].ChildId.StartsWith("synth-army-")) return true;
            return false;
        }

        private static void AttachCommandTreeIfReady(TacticalBattleOrchestrator side)
        {
            // Hot-path measurement scope (2026-05-19). See AttachDirectChildrenIfReady for context.
            using (TelemetryPerf.Scope("tactical.attach-command-tree", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            try
            {
                if (side == null || side.Army == null || !side.Army.HasPlan) return;

                CommandTreeSnapshot tree;
                using (TelemetryPerf.Scope("tactical.command-tree-runtime-snapshot", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    tree = CommandTreeRuntime.Snapshot(side.AllianceId);
                }
                if (!tree.HasNodes) return;

                var current = side.Army.CurrentCommandTree;
                if (current.HasNodes && IsSyntheticOnlyCommandTree(tree) && !IsSyntheticOnlyCommandTree(current))
                {
                    return;
                }

                if (ReferenceEquals(current, tree)) return;
                if (current.HasNodes && CommandTreeHash(current) == CommandTreeHash(tree)) return;

                side.Army.RegisterCommandTree(tree);
                LogCommandTreeTelemetry(side.AllianceId, tree);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] AttachCommandTreeIfReady skipped side="
                    + (side == null ? "null" : side.AllianceId.ToString())
                    + ": " + e.GetType().Name + " " + e.Message);
            }
        }

        private static bool IsSyntheticOnlyCommandTree(CommandTreeSnapshot tree)
        {
            try
            {
                return tree != null
                    && tree.Nodes.Count == 1
                    && tree.Nodes[0].Synthetic;
            }
            catch
            {
                return false;
            }
        }

        private static void LogCommandTreeTelemetry(int allianceId, CommandTreeSnapshot tree)
        {
            try
            {
                if (tree == null || !tree.HasNodes) return;
                int signature = CommandTreeHash(tree);
                if (_commandTreeTelemetrySignatures.TryGetValue(allianceId, out int existing) && existing == signature)
                {
                    return;
                }

                _commandTreeTelemetrySignatures[allianceId] = signature;
                var root = ResolveRootCommandLabel(tree);
                TelemetryRouter.LegacyInfo("[TacticalCommandTree] " + SideLogContext(allianceId, root, root)
                    + " nodes=" + tree.Nodes.Count
                    + " maxDepth=" + tree.MaxDepth
                    + " unittyps=" + tree.RawUnitTypDistribution
                    + " missingParents=" + tree.MissingParentCount, TelemetryLayer.Tactical);
            }
            catch { }
        }

        private readonly struct CommandLogLabel
        {
            public CommandLogLabel(string id, string name, string level)
            {
                Id = id ?? string.Empty;
                Name = name ?? string.Empty;
                Level = level ?? string.Empty;
            }

            public string Id { get; }
            public string Name { get; }
            public string Level { get; }
            public bool HasValue => !string.IsNullOrWhiteSpace(Id) || !string.IsNullOrWhiteSpace(Name);
        }

        private static string SideLogContext(int allianceId)
        {
            var top = ResolveTopCommandLabelForAlliance(allianceId);
            return SideLogContext(allianceId, top, default(CommandLogLabel));
        }

        private static string SideLogContext(int allianceId, CommandLogLabel topCommand, CommandLogLabel rootCommand)
        {
            if (!topCommand.HasValue)
            {
                topCommand = ResolveTopCommandLabelForAlliance(allianceId);
            }

            return TacticalSideLogFormatter.Format(
                allianceId,
                _playerAllianceId,
                SafeAiVsAi(),
                topCommand.Id,
                topCommand.Name,
                topCommand.Level,
                rootCommand.Id,
                rootCommand.Name);
        }

        private static CommandLogLabel ResolveRootCommandLabel(CommandTreeSnapshot tree)
        {
            try
            {
                if (tree == null || !tree.HasNodes) return default(CommandLogLabel);
                for (int i = 0; i < tree.Nodes.Count; i++)
                {
                    var node = tree.Nodes[i];
                    if (string.Equals(node.NodeId, tree.RootNodeId, StringComparison.Ordinal))
                    {
                        return new CommandLogLabel(
                            node.NodeId,
                            node.DisplayName,
                            RawLevelLabel(node.RawUnitTyp, node.EffectiveCommandLevel));
                    }
                }
            }
            catch { }
            return default(CommandLogLabel);
        }

        private static CommandLogLabel ResolveCommandLabelById(string id, string prefix)
        {
            int instanceId = ParseInstanceId(id, prefix);
            if (instanceId == 0) return default(CommandLogLabel);

            try
            {
                var units = BattleUnits.completeunitlist as System.Collections.IList;
                if (units == null) return default(CommandLogLabel);
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null) continue;
                    var go = ((Component)reg).gameObject;
                    if (go == null || go.GetInstanceID() != instanceId) continue;
                    return new CommandLogLabel(
                        id,
                        ((UnityEngine.Object)go).name,
                        RawLevelLabel(reg.unittyp, reg.unittyp - ArmyEvidenceBuilder.ReadCommandHierarchyShift()));
                }
            }
            catch { }

            return default(CommandLogLabel);
        }

        private static CommandLogLabel ResolveTopCommandLabelForAlliance(int allianceId)
        {
            try
            {
                var units = BattleUnits.completeunitlist as System.Collections.IList;
                if (units == null) return default(CommandLogLabel);
                Regiment best = null;
                GameObject bestGo = null;
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null || reg.alliance != allianceId) continue;
                    var go = ((Component)reg).gameObject;
                    if (go == null || !go.activeInHierarchy) continue;
                    if (best == null || reg.unittyp > best.unittyp)
                    {
                        best = reg;
                        bestGo = go;
                    }
                }

                if (best == null || bestGo == null) return default(CommandLogLabel);
                int shift = ArmyEvidenceBuilder.ReadCommandHierarchyShift();
                return new CommandLogLabel(
                    "unit-" + bestGo.GetInstanceID(),
                    ((UnityEngine.Object)bestGo).name,
                    RawLevelLabel(best.unittyp, best.unittyp - shift));
            }
            catch
            {
                return default(CommandLogLabel);
            }
        }

        private static int ParseInstanceId(string value, string prefix)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrEmpty(prefix)) return 0;
            if (!value.StartsWith(prefix, StringComparison.Ordinal)) return 0;
            string raw = value.Substring(prefix.Length);
            int parsed;
            return int.TryParse(raw, out parsed) ? parsed : 0;
        }

        private static string RawLevelLabel(int rawUnitTyp, int effectiveCommandLevel)
        {
            return "rawUnitTyp" + rawUnitTyp + "_effective" + effectiveCommandLevel;
        }

        private static string SafeLog(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<unresolved>" : value.Trim().Replace(' ', '_');
        }

        private static void DriveDirectChildCycle(TacticalBattleOrchestrator side, AIBattle battle)
        {
            try
            {
                if (side == null || side.Army == null || !side.Army.HasPlan) return;
                if (side.Army.CurrentDirectChildIntents.Count == 0)
                {
                    AttachDirectChildrenIfReady(side);
                    if (side.Army.CurrentDirectChildIntents.Count == 0) return;
                }

                int childCount = side.Army.CurrentDirectChildIntents.Count;

                EnemyVisibleState enemyVisForChildren = IsHeavyThrottlingEnabled()
                    ? EnsureSideSnapshot(side, battle).EnemyVisible
                    // Throttling off: pre-Task-6 behaviour.
                    : ArmyEvidenceBuilder.Build(SideSnapshot(side), battle).EnemyVisible;

                // Sectors are lateral bands of the side's front axis
                // (TacticalFrontAxis), carried with the evidence as an
                // instance-id → band map so a rout elsewhere never renumbers a
                // child's sector. Fallback map: same axis, projected live.
                var axis = enemyVisForChildren.FrontAxis;
                IReadOnlyDictionary<int, int> instanceToSector = enemyVisForChildren.InstanceSectors;
                if (instanceToSector == null || instanceToSector.Count == 0)
                    instanceToSector = ArmyEvidenceBuilder.MapOwnGroupsToSectors(side.AllianceId);
                int fallbackSector = ArmyEvidenceBuilder.DefaultMainEffortSector(axis);

                var primarySectors = new int[childCount];
                var childInstanceIds = new int[childCount];
                int minSector = int.MaxValue;
                int maxSector = int.MinValue;
                for (int i = 0; i < childCount; i++)
                {
                    var existing = side.Army.CurrentDirectChildIntents[i];
                    int instanceId = ParseInstanceIdFromChildId(existing.ChildId);
                    childInstanceIds[i] = instanceId;
                    int sector;
                    if (instanceId != 0 && instanceToSector.TryGetValue(instanceId, out int mapped))
                        sector = mapped;
                    else
                        sector = fallbackSector;
                    primarySectors[i] = sector;
                    if (sector < minSector) minSector = sector;
                    if (sector > maxSector) maxSector = sector;
                }

                // Flank exposure (TacticalFrontAxis.FlankExposureBucket): the
                // leftmost/rightmost children hold the army's wings; a wing is
                // more exposed when the visible enemy line extends past it and
                // when its regiments are outflanked (mean vanilla outflanked
                // weight over perceived flank pressure, ArmyEvidenceBuilder.MeanFlankThreatByGroup).
                // Bucket 3 refuses in DirectChildAllocator.
                var flankBuckets = new int[childCount];
                var perChildIntent = new TacticalIntentModel[childCount];
                bool hasWings = childCount > 1 && minSector != maxSector;
                for (int i = 0; i < childCount; i++)
                {
                    bool leftWing = hasWings && primarySectors[i] == minSector;
                    bool rightWing = hasWings && primarySectors[i] == maxSector;
                    bool overlapped = (leftWing && axis.EnemyOverlapsLeft) || (rightWing && axis.EnemyOverlapsRight);
                    flankBuckets[i] = TacticalFrontAxis.FlankExposureBucket(
                        leftWing || rightWing, overlapped,
                        enemyVisForChildren.FlankThreatForInstance(childInstanceIds[i]));
                    perChildIntent[i] = ArmyIntentInference.BuildForFrontage(primarySectors[i], enemyVisForChildren, ownStrengthBucket: 1);
                }

                var snapshots = new DirectChildSnapshot[childCount];
                for (int i = 0; i < childCount; i++)
                {
                    var existing = side.Army.CurrentDirectChildIntents[i];
                    snapshots[i] = new DirectChildSnapshot(
                        existing.ChildId,
                        parentArmyId: "army-cached",
                        rawUnitTyp: existing.RawUnitTyp,
                        commandHierarchyShift: existing.RawUnitTyp - existing.EffectiveCommandLevel,
                        displayName: existing.DisplayName,
                        active: true);
                }

                var evidence = DirectChildEvidenceBuilder.BuildAll(snapshots, primarySectors, flankBuckets, enemyVisForChildren);
                // Stage 2 / G: geometric play layer. Compute corps→rect
                // assignments from the current plan's template. When zone
                // assignments are available, the allocator uses them in
                // preference to evidence-based role picking (per-corps zone
                // role overrides the per-brigade sector role).
                IReadOnlyList<WhiskeyRealism.Tactical.Orchestrator.Plays.CorpsAssignment> zoneAssignments = null;
                if (Plugin.EnableGeometricPlayTemplates != null && Plugin.EnableGeometricPlayTemplates.Value
                    && side.Army.HasPlan)
                {
                    BattlePlanId planId = side.Army.CurrentPlan.PlanId;
                    int shift = 0;
                    if (childCount > 0)
                    {
                        // commandHierarchyShift = rawUnitTyp - effectiveCommandLevel
                        var ex = side.Army.CurrentDirectChildIntents[0];
                        shift = ex.RawUnitTyp - ex.EffectiveCommandLevel;
                    }
                    zoneAssignments = WhiskeyRealism.Tactical.Orchestrator.Plays.PlayExecutorRuntime
                        .ComputeZoneAssignments(side.AllianceId, planId, shift, childCount, axis);
                }
                side.Army.ObserveDirectChildEvidenceWithZones(evidence, perChildIntent, zoneAssignments);

                for (int i = 0; i < side.Army.CurrentDirectChildIntents.Count; i++)
                {
                    var dci = side.Army.CurrentDirectChildIntents[i];
                    if (dci.Role == DirectChildRole.Unknown) continue;
                    // Per-battle dedupe on (alliance, child, role); OnceLog keys must be static.
                    long childKey = (2L << 62) ^ ((long)side.AllianceId << 56) ^ ((long)(int)dci.Role << 40)
                        ^ (uint)ParseInstanceIdFromChildId(dci.ChildId);
                    if (!_battleInfoLogged.Add(childKey)) continue;
                    TelemetryRouter.LegacyInfo("[TacticalDirectChildIntent] " + SideLogContext(side.AllianceId)
                        + " child=" + dci.ChildId
                        + " childName=" + SafeLog(dci.DisplayName)
                        + " raw=" + dci.RawUnitTyp
                        + " effective=" + dci.EffectiveCommandLevel
                        + " role=" + dci.Role
                        + " sector=" + dci.PrimarySector
                        + " support=" + dci.SupportPriority01.ToString("0.00")
                        + " enemyIntent=" + dci.EnemyIntent.PrimaryIntent
                        + " confidence=" + dci.EnemyIntent.Confidence01.ToString("0.00"), TelemetryLayer.Tactical);
                }
            }
            catch (Exception e)
            {
                WarnTickCycleOnce(side, e);
            }
        }

        // ParseInstanceIdFromChildId moved to the test-included partial
        // TacticalBattleCoordinator.cs so the harness can lock the negative-id parse
        // contract. Both partials see it via the shared `partial class` declaration.

        // The coordinator's own per-battle tick state (BattleScope "coordinator-tick-state").
        private static void ResetRuntimeTickState()
        {
            _tickWarningKeys.Clear();
            _directChildDeferLogged.Clear();
            _commandTreeTelemetrySignatures.Clear();
            // Task 6: reset heavy gate + dedup state for new battle (per-battle via BattleUnits owner + per-side)
            try
            {
                _lastProcessedSideStatUpdateByBunitsId.Clear();
                for (int i = 0; i < 2; i++)
                {
                    _lastHeavyReviewHours[i] = 0f;
                    _lastHeavyReviewRealtimeSeconds[i] = 0f;
                    _lastSignatures[i] = default(TacticalBattleStateSignature);
                    _lastPublishedSnapshots[i] = TacticalBattleRuntimeSnapshot.Empty;
                    _hasPendingChange[i] = false;
                    _lastTickBattleHours[i] = -1f;  // sentinel: first tick has no battle-hours reference
                    _lastGen0CountBySide[i] = 0;
                    // "Cheap per-group" cache: clear so stale Regiment ids
                    // from the prior battle never poison the next one.
                    // Critical — Unity GameObject InstanceIDs are recycled
                    // and a leaked entry would hand the stance producers the
                    // wrong group's cached action.
                    try { _resolvedDecisionsBySide[i]?.Clear(); } catch { }
                }
                _heavyTickParity = 0;
                for (int i = 0; i < 2; i++)
                {
                    _sideSnapshotTickSerial[i] = -1;
                    _snapshotGeneration[i] = 0;
                    _lastLedgerSnapshotGeneration[i] = -1;
                    _directChildrenRegisteredGeneration[i] = -1;
                    _lastLedgerIntentsHash[i] = 0;
                    _lastLedgerMode[i] = TacticalCommanderMode.Off;
                    _lastLedgerTickBattleSeconds[i] = -1f;
                    _lastHeavyBuildFrame[i] = -1;
                }
                _battleInfoLogged.Clear();
                _regimentByGameObjectId.Clear();
                _countedUnitIds.Clear();
                _replanAfterNight[0] = false;
                _replanAfterNight[1] = false;
            }
            catch { }
        }

        /// <summary>
        /// Emits repeated heavy-gate decision telemetry using TelemetryRouter + Category.Gate
        /// (not OnceLog with fixed key) so "executed"/"skipped" + reasons appear frequently
        /// in TacticalTuning / FullTuning profiles. Includes BattleHourBucket for time-bucketing.
        /// Called from all three Drive* gate sites when heavy throttling is enabled.
        /// try/catch guarded per runtime safety rules.
        /// </summary>
        private static void EmitHeavyGateTelemetry(
            int side,
            TacticalHeavyPathGate.Decision dec,
            float nowH,
            float lastH,
            float cycle,
            bool hasP,
            TacticalBattleStateSignature currSig,
            float nowReal,
            float lastReal,
            float minReal)
        {
            try
            {
                // GC pressure delta tracking (2026-05-19 hot-path diagnostic).
                // Mono runs Boehm which is non-generational — GC.CollectionCount(0)
                // returns the same value as (1) and (2). We track only gen0 and
                // surface it as the canonical GC-collections counter. If GTCW
                // ever migrates off Mono Boehm we'll restore the gen1/gen2 split.
                int gcCountNow = System.GC.CollectionCount(0);
                int sideIdx = (side >= 0 && side <= 1) ? side : 0;
                int gcDelta = gcCountNow - _lastGen0CountBySide[sideIdx];
                if (gcDelta < 0) gcDelta = 0;
                _lastGen0CountBySide[sideIdx] = gcCountNow;

                // The input-signature string is built inside the configure callback, which
                // TelemetryRouter only invokes when the active profile emits this event.
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Gate,
                    "TacticalHeavyGate",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithDecision(dec.ShouldRun ? "executed" : "skipped", dec.Reason,
                            "TacticalHeavyGate|side=" + side +
                            "|nowH=" + nowH.ToString("F4") +
                            "|lastH=" + lastH.ToString("F4") +
                            "|cycle=" + cycle.ToString("F4") +
                            "|real=" + nowReal.ToString("F2") +
                            "|minReal=" + minReal.ToString("F2") +
                            "|pending=" + hasP +
                            "|units=" + currSig.ActiveUnitCount +
                            "|objHash=" + currSig.MajorObjectiveAnchorHash +
                            "|bucket=" + currSig.BattleHourBucket)
                        .WithField("cycleHours", cycle)
                        .WithField("battleHours", nowH)
                        .WithField("lastHeavyHours", lastH)
                        .WithField("realtimeSeconds", nowReal)
                        .WithField("lastHeavyRealtimeSeconds", lastReal)
                        .WithField("minRealtimeSeconds", minReal)
                        .WithField("elapsedRealtimeSeconds", nowReal - lastReal)
                        .WithField("hasPending", hasP)
                        .WithField("activeUnits", currSig.ActiveUnitCount)
                        .WithField("majorObjAnchorHash", currSig.MajorObjectiveAnchorHash)
                        .WithField("battleHourBucket", currSig.BattleHourBucket)
                        .WithField("gateReason", dec.Reason)
                        .WithField("gcCountAbs", gcCountNow)
                        .WithField("gcDelta", gcDelta));
            }
            catch
            {
                // degrade silently; telemetry must never break tick path
            }
        }

        // Failed bootstrap: same reset as a teardown, without the teardown line.
        private static void ClearForFailure()
        {
            active = false;
            OnBattleEnd();
        }

        // ---- "Cheap per-group" pre-compute (2026-05-20) ----
        //
        // PopulateResolvedDecisionsForSide is called from DriveTickCycle on
        // every heavy-gate fire for a non-suppressed side. It rebuilds the
        // per-alliance _resolvedDecisionsBySide dict from scratch (Clear +
        // walk + Add) so the producers never see a half-populated state. Branch
        // order (the former #45 order): doctrine first, charge override
        // (vanillaOrdered==4) next, ledger task third, default scorer last.
        //
        // **PERFORMANCE COUPLING:** the cache only repopulates when the
        // heavy-gate fires. That gate is gated by `Enable Tactical Heavy
        // Path Throttling` (default true). If a user disables throttling,
        // this populate is never called, _resolvedDecisionsBySide stays
        // empty and the stance producers submit nothing (vanilla decides
        // every group's stance).
        //
        // All reflection/Unity-API calls are try/catch guarded; on any
        // failure the per-group entry is skipped (no intent for it).

        private static void PopulateResolvedDecisionsForSide(TacticalBattleOrchestrator side, AIBattle battle,
            Battle.BattleSnapshot snapshot, EnemyVisibleState enemyVisible)
        {
            try
            {
                if (side == null) return;
                int allianceId = side.AllianceId;
                if (allianceId < 0 || allianceId >= _resolvedDecisionsBySide.Length) return;

                var cache = _resolvedDecisionsBySide[allianceId];
                cache.Clear();

                var bunits = ResolveBattleUnits(battle);
                if (bunits == null || snapshot == null) return;

                // Walk the side's snapshot roster (every own unit of the alliance, one pass
                // over completeunitlist at build), not AIBattle.unitsused, which holds only
                // the groups in that AI's pool (85227-85262).
                var own = snapshot.Own;

                // The side's live macro is its own AIBattle.macroai (battle is
                // the side's instance). sideinformation[side].macroai is only a
                // scripted override, -1 unless set (CheckGlobalAIStrategy
                // 6383-6386), so it never showed the army retreat (3).
                int macro = SafeMacroAiOfOwnBattle(battle, bunits, allianceId);

                bool chargeDenialOn = false;
                bool localReactionsOn = false;
                bool groupSectorStanceOn = false;
                bool orchestratorArmyOn = Plugin.EnableTacticalOrchestratorArmy != null && Plugin.EnableTacticalOrchestratorArmy.Value;
                try
                {
                    var p = Plugin.Instance;
                    if (p != null)
                    {
                        chargeDenialOn = p.EnableTacticalChargeDenial != null && p.EnableTacticalChargeDenial.Value;
                        localReactionsOn = p.EnableTacticalObserver != null && p.EnableTacticalObserver.Value
                            && p.EnableTacticalCommanderIntentDoctrine != null && p.EnableTacticalCommanderIntentDoctrine.Value
                            && p.EnableTacticalLocalReactionDoctrine != null && p.EnableTacticalLocalReactionDoctrine.Value;
                        groupSectorStanceOn = p.EnableTacticalGroupSectorStance != null && p.EnableTacticalGroupSectorStance.Value;
                    }
                }
                catch { }

                // Sector = the group's lateral band on the side's front axis
                // (EnemyVisibleState.InstanceSectors, keyed by GameObject id), the
                // same banding the direct-child allocator uses; the center band
                // when the group was not banded.
                int fallbackBand = ArmyEvidenceBuilder.DefaultMainEffortSector(enemyVisible.FrontAxis);
                for (int i = 0; i < snapshot.OwnCount; i++)
                {
                    if (!TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(own[i].UnitTyp)) continue;
                    var group = Battle.BattleSnapshotBuilder.UnitAt(snapshot, i, enemy: false);
                    if ((UnityEngine.Object)group == null) continue;

                    // The cache is keyed by the Regiment component's id (the patch's lookup key).
                    int instanceId;
                    try { instanceId = group.GetInstanceID(); }
                    catch { continue; }
                    if (instanceId == 0) continue;

                    // Player-owned group (W&L: the player's command is detached from AI
                    // control but stays in the hierarchy): cache a no-op so the per-group
                    // patch never resolves doctrine for it.
                    if ((own[i].Flags & Battle.UnitFlags.PlayerOwned) != 0)
                    {
                        cache[instanceId] = new TacticalResolvedGroupDecision(
                            finalStance: -1,
                            kind: TacticalDoctrineDecisionKind.Skip,
                            reason: "player-owned",
                            fromDoctrine: false,
                            fromLedger: false,
                            isChargeOverride: false,
                            chargeAction: ChargeOverrideAction.None);
                        continue;
                    }

                    int vanillaOrdered = own[i].AiStanceOrdered;
                    int band = enemyVisible.SectorForInstance(own[i].InstanceId, fallbackBand);
                    var sector = BuildSectorForGroup(snapshot, i, group, band);

                    // 1) Doctrine. When doctrine produces Allow/Deny, that's
                    //    the final answer for this Regiment — overrides
                    //    the charge branch AND ledger AND default scorer.
                    //    ChargeAction carries what to do if vanilla is (or
                    //    races) to assault 4: a doctrine Deny demotes it.
                    DoctrineConsumerAction doctrineAction = DoctrineConsumerAction.Observe;
                    int dStance = -1;
                    string dReason = null;
                    if (macro != 3 && TacticalDoctrineOrderResolver.TryResolve(group, out CommandDoctrineOrder order, out BattlefieldPictureSnapshot picture))
                    {
                        DoctrineStanceDecision dDec = DoctrineConsumerDecisions.DecideStance(
                            order,
                            DoctrineConsumerDecisions.EnemyMainLineExposed(order, picture),
                            sector.Odds);
                        doctrineAction = dDec.Action;
                        dStance = dDec.Action == DoctrineConsumerAction.Allow ? 3 : 2;
                        dReason = dDec.Reason;
                    }

                    ChargeDenialDecision ifAssault = TacticalChargeDenial.DecideForVanillaAssault(new ChargeDenialInput(
                        chargeDenialOn,
                        orchestratorArmyOn,
                        doctrineAction,
                        chargeDenialOn && !orchestratorArmyOn && localReactionsOn && LegacyReactionDeniesCharge(instanceId)));
                    ChargeOverrideAction chargeAction = ifAssault.Demotes
                        ? ChargeOverrideAction.DemoteToHold
                        : ChargeOverrideAction.PreserveVanilla;

                    if (doctrineAction != DoctrineConsumerAction.Observe)
                    {
                        cache[instanceId] = new TacticalResolvedGroupDecision(
                            finalStance: dStance,
                            kind: TacticalDoctrineDecisionKind.Apply,
                            reason: "doctrine-" + dReason,
                            fromDoctrine: true,
                            fromLedger: false,
                            isChargeOverride: vanillaOrdered == TacticalCommandTaskToStanceMapper.StanceAssault,
                            chargeAction: chargeAction);
                        continue;
                    }

                    // 2) Charge override without a doctrine opinion. Cache
                    //    freezes WHETHER to demote and to which stance
                    //    (FinalStance); Kind stays Skip so a group vanilla
                    //    takes out of assault before apply is left alone.
                    if (vanillaOrdered == TacticalCommandTaskToStanceMapper.StanceAssault)
                    {
                        cache[instanceId] = new TacticalResolvedGroupDecision(
                            finalStance: ifAssault.DemoteStance,
                            kind: TacticalDoctrineDecisionKind.Skip,
                            reason: ifAssault.Reason,
                            fromDoctrine: false,
                            fromLedger: false,
                            isChargeOverride: true,
                            chargeAction: chargeAction);
                        continue;
                    }

                    // 3) Ledger task stance.
                    if (macro != 3 && TacticalLedgerStateResolver.TryResolve(group, out CommandNodeOperationalState state))
                    {
                        int lStance = TacticalCommandTaskToStanceMapper.MapForGroupStanceScorer(
                            state.Task, TacticalDoctrineScorer.ShouldCommitVisibleWeakPoint(sector));
                        // The ledger claims ownership whenever it resolves
                        // (the former #45 rule) — even when the stance is
                        // invalid or equals vanillaOrdered. A Skip with
                        // stance=-1 records "ledger had an opinion, no write
                        // needed" so the default scorer never runs for it.
                        if (lStance < 0)
                        {
                            cache[instanceId] = new TacticalResolvedGroupDecision(
                                finalStance: -1,
                                kind: TacticalDoctrineDecisionKind.Skip,
                                reason: "ledger-no-opinion",
                                fromDoctrine: false,
                                fromLedger: true,
                                isChargeOverride: false,
                                chargeAction: ChargeOverrideAction.None);
                        }
                        else
                        {
                            cache[instanceId] = new TacticalResolvedGroupDecision(
                                finalStance: lStance,
                                kind: TacticalDoctrineDecisionKind.Apply,
                                reason: "ledger-" + state.Task,
                                fromDoctrine: false,
                                fromLedger: true,
                                isChargeOverride: false,
                                chargeAction: ChargeOverrideAction.None);
                        }
                        continue;
                    }

                    // 4) Default scorer. Only writes when group-sector
                    //    stance is on; otherwise emit a Skip so the apply
                    //    path no-ops without rebuilding the sector.
                    if (!groupSectorStanceOn)
                    {
                        cache[instanceId] = new TacticalResolvedGroupDecision(
                            finalStance: -1,
                            kind: TacticalDoctrineDecisionKind.Skip,
                            reason: "group-sector-stance-off",
                            fromDoctrine: false,
                            fromLedger: false,
                            isChargeOverride: false,
                            chargeAction: ChargeOverrideAction.None);
                        continue;
                    }

                    var scorerInput = new TacticalGroupStanceDecisionInput(
                        vanillaOrdered,
                        macro,
                        sector,
                        true,
                        true);
                    TacticalGroupStanceDecision dec = TacticalDoctrineScorer.DecideGroupStance(scorerInput);

                    cache[instanceId] = new TacticalResolvedGroupDecision(
                        finalStance: dec.GroupStance,
                        kind: dec.Kind,
                        reason: "scorer-" + dec.Reason,
                        fromDoctrine: false,
                        fromLedger: false,
                        isChargeOverride: false,
                        chargeAction: ChargeOverrideAction.None);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] PopulateResolvedDecisionsForSide degraded side="
                    + (side == null ? "null" : side.AllianceId.ToString())
                    + ": " + e.GetType().Name + " " + e.Message);
            }
        }

        // The legacy B6c producer's explicit DenyCharge for the group (only
        // written while the orchestrator army is off).
        private static bool LegacyReactionDeniesCharge(int instanceId)
        {
            try
            {
                TacticalLocalReactionDecision reaction = TacticalReactionContext.Shared.GetReaction(instanceId);
                return reaction.Reason != "no-decision" && reaction.Reaction == LocalReaction.DenyCharge;
            }
            catch
            {
                return false;
            }
        }

        // The group's sector from what its side perceives (BC2b): enemy strength is the
        // snapshot slice pressure around it (visible plus discounted remembered, replacing
        // vanilla's unfiltered angle-slice strength and in-range enemy count), the closest
        // enemy is the nearest one the side sees, flank risk is perceived flank pressure
        // (replacing flanksthreated / outflanked). Own strength, cover and fort stay own reads.
        // Troop quality (BC8): own strength and the enemy terms are weighed by vanilla's
        // experience blend (GetGroupStrength 6057, as AdjustGroupAIStance 4238 reads it):
        // the group's groupexperience against the closest visible enemy's stars.
        private static TacticalSectorAssessment BuildSectorForGroup(Battle.BattleSnapshot snapshot, int ownIndex, Regiment group, int band)
        {
            try
            {
                ref Battle.UnitRecord unit = ref snapshot.Own[ownIndex];
                Regiment closest = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
                Battle.TroopQuality.SectorBlends(snapshot, ownIndex, Battle.TroopQualityRuntime.Prefs.ExpInfluence,
                    out float ownBlend, out float enemyBlend);
                return TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
                    band,
                    Math.Max(unit.GroupOwnInRange, unit.GroupStrengthAiGroup),
                    enemiesInRangeStrength: 0f,
                    angleEnemyStrength: ArmyEvidenceBuilder.SlicePressureTotal(snapshot, ownIndex),
                    closestEnemyStrength: closest != null ? closest.strength : 0f,
                    closestEnemyUnitType: closest != null ? closest.unittyp : -1,
                    closestEnemyName: closest != null ? SafeRegimentName(closest) : string.Empty,
                    closestEnemyRouted: closest != null && closest.isrouted,
                    closestEnemyPermanentlyDetached: closest != null && closest.permanentlydetached,
                    flankRisk: ArmyEvidenceBuilder.HasFlankPressure(snapshot, ownIndex),
                    strongPoint: group.covervalue > 0.5f || group.fortinrange,
                    ownExperienceBlend: ownBlend,
                    enemyExperienceBlend: enemyBlend));
            }
            catch
            {
                return default(TacticalSectorAssessment);
            }
        }

        private static string SafeRegimentName(Regiment group)
        {
            try { return group != null ? ((Component)group).gameObject.name : string.Empty; }
            catch { return string.Empty; }
        }

        // -1 when battle does not play allianceId (unresolved own instance).
        private static int SafeMacroAiOfOwnBattle(AIBattle battle, BattleUnits bunits, int allianceId)
        {
            try
            {
                if (battle == null || bunits == null || bunits.alliance == null) return -1;
                int side = battle.sideofai;
                if (side < 0 || side >= bunits.alliance.Length || bunits.alliance[side] != allianceId) return -1;
                return battle.macroai;
            }
            catch
            {
                return -1;
            }
        }
    }
}
