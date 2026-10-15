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
    ///   OnBattleEnd    — clears inter-battle ledger state, tears down orchestrators.
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
        private const float MaxTickDeltaSeconds = 5f;

        // Cached AIBattle.bunits FieldInfo — avoids reflection cost on every battle bootstrap.
        private static FieldInfo _bunitsFieldCache;
        private static float _lastTickTimeSeconds;
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
        // Per-side last-seen battle hours (game-clock time, scales with
        // 2x/5x/20x compression). Used to derive a battle-time delta for
        // phase progression so phase budgets track commander pace, not
        // wallclock pace. Distinct from _lastHeavyReviewHours (the heavy-
        // throttle gate's last review time).
        private static readonly float[] _lastTickBattleHours = { -1f, -1f };
        // End-of-day handling: vanilla keeps all AI state across the night
        // (battlepasseddays++ at 86384 is mid-night, eodcycle 2->3). While eodcycle != 0
        // doctrine Ticks pause and the doctrine clock stops (the game clock races at
        // speed 1000 overnight); when fighting resumes (eodcycle back to 0 at 164881)
        // both sides rebuild their snapshot and replan, keeping their orchestrators.
        private static TacticalEndOfDayGate _endOfDayGate = new TacticalEndOfDayGate();
        private static TacticalDoctrineClock _doctrineClock = new TacticalDoctrineClock();
        private static float _doctrineHours;
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
        // bunits.unitsused for the firing side. Read by BattleGroupStancePatch's
        // ApplyResolvedDecisionFast so the per-tick patch becomes O(1) per
        // group: dict lookup + a few cheap field reads + a single ChangeStance
        // call. Doctrine/ledger resolver walks, sector rebuild, and
        // DecideGroupStance recompute all move to the gate-fire boundary.
        //
        // Keyed by Regiment.GetInstanceID(); cache misses route to the legacy
        // ApplyGroup path.
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
        /// Per-session battle counter, incremented on each successful OnBattleStart and
        /// never reset (so a value never identifies two battles). Still read by
        /// BattleGroupStancePatch's worker-snapshot check, which is dead since the
        /// worker thread was removed and goes away with it at integration.
        /// </summary>
        public static int BattleSequenceForParity
        {
            get { return _battleSequence; }
        }

        /// <summary>
        /// "Cheap per-group" cache lookup. Returns the pre-resolved
        /// doctrine/ledger/charge decision for a Regiment when the most
        /// recent heavy-gate fire pre-computed one for the given alliance.
        /// Returns false on miss; callers (currently
        /// <c>BattleGroupStancePatch</c>) fall through to the legacy
        /// resolver-driven apply path. Indexed by alliance id (0/1);
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
                _doctrineHours = _doctrineClock.Advance(SafeCurrentBattleHours(), suspended: false);
                BuildAndActivate(suppressedAllianceId, roster);
                _playerAllianceId = playerAllianceId;
                _battleSequence++;

                if (Plugin.EnableTacticalOrchestratorArmy != null && Plugin.EnableTacticalOrchestratorArmy.Value)
                {
                    AttachArmyIfActive(side0, battle);
                    AttachArmyIfActive(side1, battle);
                    AttachDirectChildrenIfReady(side0, battle);
                    AttachDirectChildrenIfReady(side1, battle);
                    AttachCommandTreeIfReady(side0, battle);
                    AttachCommandTreeIfReady(side1, battle);
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
            // caches nothing will consume (and nothing would clear on OnBattleEnd, which
            // early-outs while inactive).
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

            // Stage C perf cache (2026-05-21): pre-populate the per-group resolved
            // decision cache with Skip defaults so BattleGroupStancePatch tier-1 hits
            // from the very first patch invocation. Eliminates the battle-start window
            // where tier-3 ApplyGroup fallback would fire on every group walk (~5-15 ms
            // per missed group). First heavy-gate fire replaces these placeholders with
            // real doctrine/ledger decisions via PopulateResolvedDecisionsForSide.
            try { PrePopulateResolvedDecisionsAtBattleStart(); }
            catch (Exception rce)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] resolved-cache pre-populate threw: "
                    + rce.GetType().Name + " " + rce.Message);
            }
        }

        public static void OnBattleEnd()
        {
            if (!active) return;
            try
            {

                ClearLedgersBetweenBattles();
                ResetRuntimeTickState();
                _directChildDeferLogged.Clear();
                _commandTreeTelemetrySignatures.Clear();

                // Stage A perf cache (2026-05-21): clear battle-lifetime scene-scan
                // cache so the next battle starts with a fresh state. Idempotent if
                // cache wasn't populated.
                try { TacticalStableSceneCache.Clear(); }
                catch (Exception sce)
                {
                    Plugin.Log.LogWarning("[TacticalOrchestrator] scene-cache clear threw: "
                        + sce.GetType().Name + " " + sce.Message);
                }

                // Stage B perf cache (2026-05-21): invalidate command tree cache. Next
                // battle's first Snapshot() will rebuild from a fresh roster.
                try { CommandTreeRuntime.InvalidateAll(); }
                catch (Exception cte)
                {
                    Plugin.Log.LogWarning("[TacticalOrchestrator] command-tree-cache clear threw: "
                        + cte.GetType().Name + " " + cte.Message);
                }

                // Stage D (2026-05-21): reset validator counters so the next battle
                // starts fresh and the next validation cycle isn't skipped due to a
                // stale lastValidationRealtimeSeconds value.
                try { TacticalCacheValidator.ResetForBattleEnd(); } catch { }
                try { DirectChildDiscovery.ResetForBattleEnd(); } catch { }

                // Regimental control state (2026-05-22, Scope B): claim-set
                // tracking removed. Reset now only clears the artillery-action
                // timestamp dictionary so next battle starts with fresh hold
                // windows. Doctrine writes via vanilla ChangeStance — no
                // mod-owned state to release.
                try { WhiskeyRealism.Patches.BattleGroupStancePatch.ResetRegimentalControlForBattleEnd(); } catch { }
                try { WhiskeyRealism.Patches.BattleCommandPostureExecutorPatch.ResetTaskHoldForBattleEnd(); } catch { }

                side0 = null;
                side1 = null;
                _playerAllianceId = -1;
                _activeBattleUnitsId = 0;
                active = false;
                TelemetryRouter.LegacyInfo("[TacticalOrchestrator] teardown battle=" + _battleSequence, TelemetryLayer.Tactical);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleEnd partial: "
                    + e.GetType().Name + " " + e.Message);
                ClearForFailure();
            }
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
                    EndOfDayPhase dayPhase = _endOfDayGate.Observe(SafeEodCycle(battle));
                    _doctrineHours = _doctrineClock.Advance(SafeCurrentBattleHours(), dayPhase == EndOfDayPhase.Suspended);
                    if (dayPhase == EndOfDayPhase.Suspended)
                    {
                        MarkSideStatCycleProcessed(battleKey, vanillaLastSideStat);
                        return;
                    }
                    if (dayPhase == EndOfDayPhase.Resumed) PrepareForNewBattleDay();
                    unchecked { _tickSerial++; }
                    bool aiVsAi = SafeAiVsAi();
                    float deltaSeconds = ComputeTickDeltaSeconds();

                    // Drive each side; the heavy-gate publishes into
                    // _lastPublishedSnapshots[s] when it fires.
                    DriveTacticalCommanderSide(side0, battle, aiVsAi, deltaSeconds);
                    DriveTacticalCommanderSide(side1, battle, aiVsAi, deltaSeconds);

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

        private static void ReplanAfterNightIfDue(TacticalBattleOrchestrator side, AIBattle battle)
        {
            int s = side.AllianceId;
            if (s < 0 || s > 1 || !_replanAfterNight[s]) return;
            _replanAfterNight[s] = false;
            if (side.Army == null || !side.Army.HasPlan) return;
            ArmyEvidence evidence = IsHeavyThrottlingEnabled()
                ? EnsureSideSnapshot(side, battle).OwnEvidence
                : ArmyEvidenceBuilder.Build(battle, side.AllianceId).OwnEvidence;
            side.Army.Replan(evidence, side.Army.CurrentIntentModel);
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
            bool aiVsAi,
            float deltaSeconds)
        {
            if (side == null) return;
            if (!ShouldRunTacticalCommanderForSide(side.AllianceId, _playerAllianceId, aiVsAi)) return;

            side.Tick();

            if (Plugin.EnableTacticalOrchestratorIntentInference != null
                && Plugin.EnableTacticalOrchestratorIntentInference.Value)
            {
                DriveTickCycle(side, battle, deltaSeconds);
                ReplanAfterNightIfDue(side, battle);
                AttachDirectChildrenIfReady(side, battle);
                AttachCommandTreeIfReady(side, battle);
                DriveDirectChildCycle(side, battle);
            }

            DriveOperationsLedger(side, battle);
        }

        private static float ComputeTickDeltaSeconds()
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                float delta = _lastTickTimeSeconds <= 0f ? 1f : Math.Max(0f, now - _lastTickTimeSeconds);
                _lastTickTimeSeconds = now;
                return SanitizeDelta(delta);
            }
            catch
            {
                return 1f;
            }
        }

        private static float SanitizeDelta(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return 0f;
            return Math.Min(deltaSeconds, MaxTickDeltaSeconds);
        }

        private static void DriveTickCycle(TacticalBattleOrchestrator side, AIBattle battle, float deltaSeconds)
        {
            try
            {
                if (side == null || side.Army == null || !side.Army.HasPlan) return;

                ArmyEvidenceBuilder.Bundle bundleForReplan = IsHeavyThrottlingEnabled()
                    ? BundleFromSnapshot(EnsureSideSnapshot(side, battle))
                    // Throttling off: pre-Task-6 behaviour, full build every tick.
                    : ArmyEvidenceBuilder.Build(battle, side.AllianceId);

                int minReplanSeconds = (Plugin.TacticalOrchestratorMinReplanSeconds != null)
                    ? Plugin.TacticalOrchestratorMinReplanSeconds.Value
                    : 60;

                // Battle-time delta: compute from GameVars.currenttimefromstart
                // diff vs last tick. At 2x/5x/20x compression, this advances
                // proportionally faster than wallclock deltaSeconds. Drives
                // phase progression budgets so commander decisions track
                // battle pace, not wallclock pace. Hours→seconds conversion.
                int sideIdx = side.AllianceId;
                if (sideIdx < 0 || sideIdx > 1) sideIdx = 0;
                float nowBattleHours = _doctrineHours;
                float lastBattleHours = _lastTickBattleHours[sideIdx];
                float battleDeltaSeconds;
                if (lastBattleHours < 0f)
                {
                    battleDeltaSeconds = 0f;  // first tick — no prior reference
                }
                else
                {
                    float deltaHours = nowBattleHours - lastBattleHours;
                    if (deltaHours < 0f) deltaHours = 0f;          // battle reset / clock rollback
                    if (deltaHours > 1f) deltaHours = 1f;          // cap at 1 battle hour per tick
                    battleDeltaSeconds = deltaHours * 3600f;
                }
                _lastTickBattleHours[sideIdx] = nowBattleHours;

                var trigger = ArmyTickCycle.MaybeReplan(
                    side.Army,
                    deltaSeconds: deltaSeconds,
                    ownEvidence: bundleForReplan.OwnEvidence,
                    enemyVisible: bundleForReplan.EnemyVisible,
                    ownMainEffortStrength: bundleForReplan.OwnMainEffortStrength,
                    ownArmyMorale: bundleForReplan.OwnArmyMorale,
                    ownReservesCommittedFraction: bundleForReplan.OwnReservesCommittedFraction,
                    reinforcementsArrivingDelta: bundleForReplan.ReinforcementsArrivingDelta,
                    minReplanSeconds: minReplanSeconds,
                    ownAvgFatigue01: bundleForReplan.OwnAvgFatigue01,
                    ownAvgAmmo01: bundleForReplan.OwnAvgAmmo01,
                    nearestReinforcementHours: bundleForReplan.NearestReinforcementHours,
                    nearestReinforcementStrength: bundleForReplan.NearestReinforcementStrength,
                    battleDeltaSeconds: battleDeltaSeconds);

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
                        side.OperationsLedger.SetRuntimeClock(LedgerClockSecondsFromBattleHours(_doctrineHours));
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
                    float nowBattleSeconds = LedgerClockSecondsFromBattleHours(_doctrineHours);
                    if (!IsHeavyThrottlingEnabled())
                    {
                        // Preserve exact pre-Task-6 behavior (builds every ledger tick)
                        var bundle = ArmyEvidenceBuilder.Build(battle, side.AllianceId);
                        var objectives = TacticalVisionRuntimeAdapter.BuildObjectiveRecordsFromBattle(battle, side.AllianceId);
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

        // Command node ids are "node-<GameObject InstanceID>" (CommandTreeBuilder) or a
        // direct-child id ("child-<id>" / "synth-army-<id>").
        private static int ParseCommandNodeInstanceId(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return 0;
            const string prefix = "node-";
            if (nodeId.StartsWith(prefix, StringComparison.Ordinal))
                return int.TryParse(nodeId.Substring(prefix.Length), out int id) ? id : 0;
            return ParseInstanceIdFromChildId(nodeId);
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

            float nowH = _doctrineHours;
            var currSig = TacticalBattleSnapshotBuilder.ExtractCurrentSignature(battle, nowH);
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
                    snap = TacticalBattleSnapshotBuilder.Build(battle, s, currSig, nowH);
                }
                _lastHeavyReviewHours[s] = nowH;
                _lastHeavyReviewRealtimeSeconds[s] = nowReal;
                _lastSignatures[s] = currSig;
                _lastPublishedSnapshots[s] = snap;
                _hasPendingChange[s] = false;
                unchecked { _snapshotGeneration[s]++; }

                // "Cheap per-group" pre-compute (2026-05-20): resolved per-Regiment
                // decisions read by BattleGroupStancePatch tier-1.
                using (TelemetryPerf.Scope("tactical.resolved-group-decisions.build", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                {
                    PopulateResolvedDecisionsForSide(side, battle);
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
                snap.ReinforcementsArrivingDelta,
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

        private static float SafeCurrentBattleHours()
        {
            try
            {
                float t = GameVars.currenttimefromstart;
                if (float.IsNaN(t) || float.IsInfinity(t) || t < 0f) return 0f;
                return t;
            }
            catch
            {
                return 0f;
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
                if (_bunitsFieldCache == null)
                    _bunitsFieldCache = AccessTools.Field(typeof(AIBattle), "bunits");
                return _bunitsFieldCache?.GetValue(battle) as BattleUnits;
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
            var evidence = BuildArmyEvidenceForSide(side.AllianceId, battle);
            army.PickInitialPlan(evidence);
            if (army.HasPlan)
            {
                TelemetryRouter.LegacyInfo("[TacticalPlan] " + SideLogContext(side.AllianceId)
                    + " plan=" + army.CurrentPlan.PlanId
                    + " phase=" + army.CurrentPlan.Phase
                    + " mainEffort=" + army.CurrentPlan.MainEffortSector, TelemetryLayer.Tactical);
            }
        }

        private static void AttachDirectChildrenIfReady(TacticalBattleOrchestrator side, AIBattle battle)
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

        private static void AttachCommandTreeIfReady(TacticalBattleOrchestrator side, AIBattle battle)
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
                    AttachDirectChildrenIfReady(side, battle);
                    if (side.Army.CurrentDirectChildIntents.Count == 0) return;
                }

                int childCount = side.Army.CurrentDirectChildIntents.Count;

                EnemyVisibleState enemyVisForChildren = IsHeavyThrottlingEnabled()
                    ? EnsureSideSnapshot(side, battle).EnemyVisible
                    // Throttling off: pre-Task-6 behaviour.
                    : ArmyEvidenceBuilder.Build(battle, side.AllianceId).EnemyVisible;

                // Build instanceId → sector-index map matching ArmyEvidenceBuilder's iteration
                // (BattleUnits.completeunitlist filtered by IsUsableOwnGroup). The post-filter
                // index IS the SectorId in enemyVisForChildren.Sectors, so a real per-child
                // primary sector lets BuildForFrontage actually find a matching sector and
                // lets the allocator's adjacency / flank rules engage.
                var instanceToSector = BuildInstanceToSectorIndexMap(battle, side.AllianceId);

                var primarySectors = new int[childCount];
                int minSector = int.MaxValue;
                int maxSector = int.MinValue;
                for (int i = 0; i < childCount; i++)
                {
                    var existing = side.Army.CurrentDirectChildIntents[i];
                    int instanceId = ParseInstanceIdFromChildId(existing.ChildId);
                    int sector;
                    if (instanceId != 0 && instanceToSector.TryGetValue(instanceId, out int mapped))
                        sector = mapped;
                    else
                        sector = existing.PrimarySector >= 0 ? existing.PrimarySector : 0;
                    primarySectors[i] = sector;
                    if (sector < minSector) minSector = sector;
                    if (sector > maxSector) maxSector = sector;
                }

                // Flank exposure: leftmost (lowest sector index) and rightmost (highest) children
                // are wing positions. Bucket >= FlankExposureRefuseThreshold (=2) triggers
                // RefuseLeft/RefuseRight in the allocator. Single-child armies have min==max so
                // both checks land on the same child and it stays bucket=2 either way.
                var flankBuckets = new int[childCount];
                var perChildIntent = new TacticalIntentModel[childCount];
                for (int i = 0; i < childCount; i++)
                {
                    bool isWing = childCount > 1 && (primarySectors[i] == minSector || primarySectors[i] == maxSector);
                    flankBuckets[i] = isWing ? 2 : 0;
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
                        .ComputeZoneAssignments(side.AllianceId, planId, shift, childCount);
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

        // Walk BattleUnits.completeunitlist with the same shifted command-level
        // filter ArmyEvidenceBuilder uses and record each command-level group's
        // GameObject InstanceID → post-filter index. The post-filter index equals the SectorId
        // assigned by ArmyEvidenceBuilder, so per-child primary-sector lookups stay aligned.
        private static System.Collections.Generic.Dictionary<int, int> BuildInstanceToSectorIndexMap(AIBattle battle, int allianceId)
        {
            var map = new System.Collections.Generic.Dictionary<int, int>();
            try
            {
                var units = BattleUnits.completeunitlist as System.Collections.IList;
                if (units == null) return map;
                int effectiveCommandMin = ArmyEvidenceBuilder.ClampShiftedMin(ArmyEvidenceBuilder.ReadCommandHierarchyShift());
                int sectorIndex = 0;
                for (int i = 0; i < units.Count; i++)
                {
                    var group = units[i] as Regiment;
                    if (group == null) continue;
                    if (group.alliance != allianceId) continue;
                    if (group.unittyp < effectiveCommandMin) continue;
                    if (group.isrouted || group.markedforrout) continue;
                    var go = ((Component)group).gameObject;
                    if (go == null) continue;
                    map[go.GetInstanceID()] = sectorIndex;
                    sectorIndex++;
                }
            }
            catch
            {
                // Empty map → primary sectors fall back to existing.PrimarySector per child.
            }
            return map;
        }

        private static ArmyEvidence BuildArmyEvidenceForSide(int allianceId, AIBattle battle)
        {
            var bunits = ResolveBattleUnits(battle);
            int side = -1;
            try
            {
                if (bunits != null && bunits.alliance != null)
                {
                    for (int s = 0; s < 2 && s < bunits.alliance.Length; s++)
                        if (bunits.alliance[s] == allianceId) { side = s; break; }
                }
            }
            catch { }

            float own = 0f;
            float enemyTotal = 0f;
            try
            {
                if (bunits != null && bunits.sideinformation != null && side >= 0 && side < bunits.sideinformation.Length)
                    own = System.Math.Max(1f, bunits.sideinformation[side].totalactiveforce);
                if (bunits != null && bunits.sideinformation != null)
                {
                    for (int s = 0; s < 2 && s < bunits.sideinformation.Length; s++)
                        if (s != side) enemyTotal += System.Math.Max(0f, bunits.sideinformation[s].totalactiveforce);
                }
            }
            catch { }

            float odds = enemyTotal <= 0f ? 1f : own / enemyTotal;
            return new ArmyEvidence(odds, TerrainKind.Open, defaultMainEffortSector: 0);
        }

        private static void ClearLedgersBetweenBattles()
        {
            try { TacticalSectorLedger.ClearHelpRequests(); } catch { }
            try { Plugin.MoraleSnapshotLedger?.Clear(); } catch { }
            try { WhiskeyRealism.Patches.TacticalRegimentDiagnosticsPatch.Reset(); } catch { }
            try { WhiskeyRealism.Patches.TacticalMapKnowledgeDiagnosticsPatch.Reset(); } catch { }
            try { WhiskeyRealism.Patches.BattleCommandPostureExecutorPatch.Reset(); } catch { }
        }

        private static void ResetRuntimeTickState()
        {
            try { ArmyTickCycle.Reset(); } catch { }
            _lastTickTimeSeconds = 0f;
            _tickWarningKeys.Clear();
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
                    // and a leaked entry would route to ApplyResolvedDecisionFast
                    // with the wrong group's cached action.
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
                _endOfDayGate = new TacticalEndOfDayGate();
                _doctrineClock = new TacticalDoctrineClock();
                _doctrineHours = 0f;
                _replanAfterNight[0] = false;
                _replanAfterNight[1] = false;
            }
            catch { }
            // Clear the shared observation aggregate (global singleton) for the next battle.
            try { TacticalUnitObservationAggregate.Shared.ClearForBattleEnd(); } catch { }
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

        private static void ClearForFailure()
        {
            // ResetRuntimeTickState clears the resolved-decision caches; the battle-lifetime
            // scene and command-tree caches are cleared here too because OnBattleEnd
            // early-outs once active is false.
            ResetRuntimeTickState();
            try { TacticalStableSceneCache.Clear(); } catch { }
            try { CommandTreeRuntime.InvalidateAll(); } catch { }
            side0 = null;
            side1 = null;
            _playerAllianceId = -1;
            _activeBattleUnitsId = 0;
            active = false;
        }

        // Stage C perf cache (2026-05-21): pre-populates _resolvedDecisionsBySide at
        // battle start with Skip defaults for every commandable Regiment in
        // BattleUnits.completeunitlist. Eliminates the GAP between battle-start and
        // first heavy-gate fire where BattleGroupStancePatch.TryApplyFromDecisionSnapshot
        // tier-1 lookup would miss and fall through to tier-3 ApplyGroup (~5-15ms per
        // missed group). After first heavy-gate fire, PopulateResolvedDecisionsForSide
        // takes over with the normal Clear+Refill cycle.
        //
        // Diagnostic emissions:
        //   tactical-resolved-cache:prepopulated:<allianceId> — first populate count per side
        //
        // Safe to call multiple times — clears + refills idempotently.
        public static void PrePopulateResolvedDecisionsAtBattleStart()
        {
            try
            {
                var units = BattleUnits.completeunitlist as IList;
                if (units == null || units.Count == 0) return;

                // Skip default: Kind=Skip, FinalStance=-1, no charge override. Patch's
                // tier-1 hit will see Kind != Apply, early-return without writing —
                // identical to vanilla behavior until first heavy-gate fire replaces
                // these placeholders with real doctrine/ledger decisions.
                var skipDefault = new TacticalResolvedGroupDecision(
                    finalStance: -1,
                    kind: TacticalDoctrineDecisionKind.Skip,
                    reason: "battle-start-noop",
                    fromDoctrine: false,
                    fromLedger: false,
                    isChargeOverride: false,
                    chargeAction: ChargeOverrideAction.None);

                int[] counts = new int[_resolvedDecisionsBySide.Length];
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null) continue;
                    int alliance;
                    int unittyp;
                    int instanceId;
                    try
                    {
                        alliance = reg.alliance;
                        unittyp = reg.unittyp;
                        instanceId = reg.GetInstanceID();
                    }
                    catch { continue; }

                    if (alliance < 0 || alliance >= _resolvedDecisionsBySide.Length) continue;
                    if (!TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(unittyp)) continue;
                    if (instanceId == 0) continue;

                    var cache = _resolvedDecisionsBySide[alliance];
                    if (cache == null) continue;
                    cache[instanceId] = skipDefault;
                    counts[alliance]++;
                }

                for (int s = 0; s < counts.Length; s++)
                {
                    if (counts[s] > 0)
                    {
                        OnceLog.Info(
                            "tactical-resolved-cache:prepopulated",
                            "[TacticalResolvedCache] pre-populated alliance=" + s
                            + " count=" + counts[s]
                            + " — battle-start gap closed; tier-1 hits begin immediately");
                    }
                }
            }
            catch (Exception e)
            {
                OnceLog.Warning(
                    "tactical-resolved-cache:prepopulate-failed",
                    "[TacticalResolvedCache] pre-populate failed: " + e.GetType().Name + " " + e.Message
                    + " — tier-3 fallback path engages for the gap window");
            }
        }

        // ---- "Cheap per-group" pre-compute (2026-05-20) ----
        //
        // PopulateResolvedDecisionsForSide is called from DriveTickCycle on
        // every heavy-gate fire for a non-suppressed side. It rebuilds the
        // per-alliance _resolvedDecisionsBySide dict from scratch (Clear +
        // walk + Add) so the patch never sees a half-populated state. The
        // pre-compute mirrors BattleGroupStancePatch.ApplyGroup's branch
        // order — doctrine first, charge override (vanillaOrdered==4) next,
        // ledger task third, default scorer last — so the cached final-stance
        // answer matches what the legacy path would have written.
        //
        // **PERFORMANCE COUPLING:** the cache only repopulates when the
        // heavy-gate fires. That gate is gated by `Enable Tactical Heavy
        // Path Throttling` (default true). If a user disables throttling,
        // this populate is never called, _resolvedDecisionsBySide stays
        // empty, BattleGroupStancePatch always falls through to legacy
        // ApplyGroup, and the per-tick p99 returns to pre-refactor levels
        // (~13-15ms). This is by design — the throttle is what enables the
        // cache cadence — but it is not obvious from outside.
        //
        // All reflection/Unity-API calls are try/catch guarded; on any
        // failure the per-group entry is skipped, and the patch falls
        // through to ApplyGroup for that Regiment.

        private static void PopulateResolvedDecisionsForSide(TacticalBattleOrchestrator side, AIBattle battle)
        {
            try
            {
                if (side == null) return;
                int allianceId = side.AllianceId;
                if (allianceId < 0 || allianceId >= _resolvedDecisionsBySide.Length) return;

                var cache = _resolvedDecisionsBySide[allianceId];
                cache.Clear();

                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return;

                // Walk BattleUnits.completeunitlist filtered by alliance —
                // NOT AIBattle.unitsused. Tick() is debounced per vanilla
                // CalculateSideStats cycle and drives both side0 and side1
                // with whichever AIBattle fired first. That AIBattle's
                // unitsused only contains its own side's regiments. Using
                // completeunitlist + alliance filter gives us the right
                // Regiment population for whichever side we're populating
                // regardless of which AIBattle triggered Tick (mirrors
                // BuildInstanceToSectorIndexMap above).
                var units = BattleUnits.completeunitlist as IList;
                if (units == null || units.Count == 0) return;

                // Macro is per-side; AIBattle.macroai belongs to the
                // AIBattle that fired Tick, which may not match the side
                // we're pre-computing. Pull from bunits.sideinformation
                // indexed by the side whose alliance == allianceId.
                int macro = SafeMacroAiForSideByAlliance(bunits, allianceId);

                bool chargeDenialOn = false;
                bool localReactionsOn = false;
                bool groupSectorStanceOn = false;
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

                int sectorIndex = 0;
                for (int i = 0; i < units.Count; i++)
                {
                    var group = units[i] as Regiment;
                    if (group == null) continue;
                    // Alliance filter — completeunitlist is global, so we
                    // skip Regiments belonging to other sides.
                    if (group.alliance != allianceId) continue;
                    if (!TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;

                    int instanceId;
                    try { instanceId = group.GetInstanceID(); }
                    catch { continue; }
                    if (instanceId == 0) continue;

                    // Player-owned group (W&L: the player's command is detached from AI
                    // control but stays in the hierarchy): cache a no-op so the per-group
                    // patch never resolves doctrine for it.
                    if (group.dlcw_isundercommander)
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

                    int vanillaOrdered;
                    try { vanillaOrdered = group.ai_stanceordered; }
                    catch { continue; }

                    // sectorIndex is the post-filter index for this side
                    // (mirrors BuildInstanceToSectorIndexMap's accounting).
                    // It's used as TacticalSectorAssessment.SectorId; the
                    // scorers ignore the value, it just shows up in logs.
                    var sector = BuildSectorForGroup(group, sectorIndex);
                    sectorIndex++;

                    // 1) Doctrine. When doctrine produces Allow/Deny, that's
                    //    the final answer for this Regiment — overrides
                    //    charge AND ledger AND default scorer.
                    if (macro != 3 && TacticalDoctrineOrderResolver.TryResolve(group, out CommandDoctrineOrder order, out BattlefieldPictureSnapshot picture))
                    {
                        DoctrineStanceDecision dDec = DoctrineConsumerDecisions.DecideStance(
                            order,
                            DoctrineConsumerDecisions.EnemyMainLineExposed(order, picture),
                            sector.Odds);
                        if (dDec.Action != DoctrineConsumerAction.Observe)
                        {
                            int dStance = dDec.Action == DoctrineConsumerAction.Allow ? 3 : 2;
                            cache[instanceId] = new TacticalResolvedGroupDecision(
                                finalStance: dStance,
                                kind: TacticalDoctrineDecisionKind.Apply,
                                reason: "doctrine-" + dDec.Reason,
                                fromDoctrine: true,
                                fromLedger: false,
                                isChargeOverride: false,
                                chargeAction: ChargeOverrideAction.None);
                            continue;
                        }
                    }

                    // 2) Charge override. Mirrors BattleGroupStancePatch's
                    //    vanillaOrdered==4 branch. Cache freezes WHETHER to
                    //    demote; the apply path re-reads the live reaction
                    //    only to fill the demote-telemetry detail.
                    if (vanillaOrdered == 4)
                    {
                        ChargeOverrideAction action;
                        string reason;
                        if (!chargeDenialOn || !localReactionsOn)
                        {
                            action = ChargeOverrideAction.PreserveVanilla;
                            reason = "vanilla-charge-preserved";
                        }
                        else
                        {
                            TacticalLocalReactionDecision reaction;
                            try { reaction = TacticalReactionContext.Shared.GetReaction(instanceId); }
                            catch { reaction = new TacticalLocalReactionDecision(LocalReaction.MaintainLine, false, 0f, "no-decision"); }
                            bool explicitDeny = reaction.Reason != "no-decision" && reaction.Reaction == LocalReaction.DenyCharge;
                            if (explicitDeny)
                            {
                                action = ChargeOverrideAction.DemoteToHold;
                                reason = "charge-demote";
                            }
                            else
                            {
                                action = ChargeOverrideAction.PreserveVanilla;
                                reason = "vanilla-charge-preserved";
                            }
                        }

                        cache[instanceId] = new TacticalResolvedGroupDecision(
                            finalStance: -1,
                            kind: TacticalDoctrineDecisionKind.Skip,
                            reason: reason,
                            fromDoctrine: false,
                            fromLedger: false,
                            isChargeOverride: true,
                            chargeAction: action);
                        continue;
                    }

                    // 3) Ledger task stance.
                    if (macro != 3 && TacticalLedgerStateResolver.TryResolve(group, out CommandNodeOperationalState state))
                    {
                        int lStance = StanceForLedgerTask(state.Task, sector);
                        // BattleGroupStancePatch.TryApplyLedgerTaskStance
                        // claims ownership whenever the ledger resolves
                        // (return true) — even when stance is invalid or
                        // equals vanillaOrdered. Mirror that: write a Skip
                        // with stance=-1 so the apply path treats this
                        // group as "ledger had an opinion, no write
                        // needed" and DOESN'T fall through to the default
                        // scorer.
                        if (lStance < 0 || lStance > 3 || lStance == 4)
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

        private static TacticalSectorAssessment BuildSectorForGroup(Regiment group, int index)
        {
            try
            {
                Regiment closest = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
                return TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
                    index,
                    group != null ? Math.Max(group.groupowninrange, group.groupstrengthaigroup) : 0f,
                    group != null ? group.groupenemiesinrange : 0f,
                    SumEnemyStrengthWithinAngleSafe(group),
                    closest != null ? closest.strength : 0f,
                    closest != null ? closest.unittyp : -1,
                    closest != null ? SafeRegimentName(closest) : string.Empty,
                    closest != null && closest.isrouted,
                    closest != null && closest.permanentlydetached,
                    group != null && (group.flanksthreated > 0f || group.outflanked > 0),
                    group != null && (group.covervalue > 0.5f || group.fortinrange)));
            }
            catch
            {
                return default(TacticalSectorAssessment);
            }
        }

        private static float SumEnemyStrengthWithinAngleSafe(Regiment group)
        {
            try
            {
                if (group == null ||
                    group.unitrange == null ||
                    group.unitrange.enemystrengthwithinangle == null ||
                    !TacticalFogOfWarContact.HasVisibleEnemy(group))
                    return 0f;

                float total = 0f;
                for (int i = 0; i < group.unitrange.enemystrengthwithinangle.Length; i++)
                    total += Math.Max(0f, group.unitrange.enemystrengthwithinangle[i]);
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static string SafeRegimentName(Regiment group)
        {
            try { return group != null ? ((Component)group).gameObject.name : string.Empty; }
            catch { return string.Empty; }
        }

        private static int StanceForLedgerTask(CommandTaskType task, TacticalSectorAssessment sector)
        {
            // Mirrors BattleGroupStancePatch.StanceForTask. Returns -1 when
            // the task has no stance opinion (default scorer would have
            // been consulted in the legacy path, but ledger claims
            // ownership when it resolves).
            if ((task == CommandTaskType.HoldObjective ||
                 task == CommandTaskType.HoldChoke ||
                 task == CommandTaskType.FixEnemy) &&
                TacticalDoctrineScorer.ShouldCommitVisibleWeakPoint(sector))
                return 3;

            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                    return 3;
                case CommandTaskType.FixEnemy:
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.FallBackToLine:
                    return 2;
                default:
                    return -1;
            }
        }

        private static int SafeMacroAiForSideByAlliance(BattleUnits bunits, int allianceId)
        {
            try
            {
                if (bunits == null || bunits.alliance == null || bunits.sideinformation == null) return -1;
                for (int side = 0; side < bunits.alliance.Length && side < bunits.sideinformation.Length; side++)
                {
                    if (bunits.alliance[side] != allianceId) continue;
                    var info = bunits.sideinformation[side];
                    if (info == null) return -1;
                    return info.macroai;
                }
                return -1;
            }
            catch
            {
                return -1;
            }
        }
    }
}
