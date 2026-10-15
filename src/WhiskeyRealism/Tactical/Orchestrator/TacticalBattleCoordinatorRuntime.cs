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
    // DriveTickCycle / DriveDirectChildCycle / DriveOperationsLedger all follow the same pattern:
    //   if (!IsHeavyThrottlingEnabled()) { full build for compat }
    //   else { sig = Extract...; dec = Decide(...); if (Run) Build+publish else reuse lastPublished (may be !HasData) }
    //   then synthesize bundle/objectives from snap (degrade safe on Empty)
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
        private static readonly Dictionary<string, string> _commandTreeTelemetrySignatures = new Dictionary<string, string>();

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
        private static readonly TacticalBattleStateSignature[] _lastSignatures = new TacticalBattleStateSignature[2];
        private static readonly TacticalBattleRuntimeSnapshot[] _lastPublishedSnapshots = { TacticalBattleRuntimeSnapshot.Empty, TacticalBattleRuntimeSnapshot.Empty };
        private static readonly bool[] _hasPendingChange = { false, false };
        // Layer 2 frame-coalescing (2026-05-22). Per-side Unity Time.frameCount
        // of the most recent heavy snapshot build. Used to coalesce multiple
        // ShouldRun signals in the same frame into a single build per side.
        // Defense against signature-mutation churn — even when N legitimate
        // mutations happen in one frame, we build ONCE.
        private static readonly int[] _lastHeavyBuildFrame = { -1, -1 };

        // "Cheap per-group" cache (2026-05-20). Populated in DriveTickCycle on
        // every heavy-gate-fire — one entry per commandable Regiment in
        // bunits.unitsused for the firing side. Read by BattleGroupStancePatch's
        // ApplyResolvedDecisionFast so the per-tick patch becomes O(1) per
        // group: dict lookup + a few cheap field reads + a single ChangeStance
        // call. Doctrine/ledger resolver walks, sector rebuild, and
        // DecideGroupStance recompute all move to the gate-fire boundary.
        //
        // Keyed by Regiment.GetInstanceID() — same key the worker-thread
        // group-stance dicts use, so cache misses route to the legacy
        // ApplyGroup path identically.
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

        // Slice 2 parity window: compare worker-published decisions against the
        // synchronous orchestrator state for the first N cycles per (battleSeq,
        // alliance). After N clean compares, the window logs "window-clean" and
        // subsequent cycles no longer touch the comparator path. On any mismatch:
        // emit TacticalOrchestratorParityMismatch (registered in Task 6) and stop
        // comparing for that key to avoid log spam.
        // Key formula: _battleSequence * 10 + allianceId.
        // Budget cut 20 → 5 (2026-05-20) → 0 (2026-05-21). Matches TacticalBattleSnapshotBuilder.ParityCompareBudget retirement.
        private static readonly Dictionary<int, int> _workerParityComparesRemaining = new Dictionary<int, int>();
        private static readonly HashSet<int> _workerParityMismatchObserved = new HashSet<int>();
        private const int WorkerParityCompareBudget = 0;

        // Slice 2: dedicated worker thread for tactical orchestrator analysis.
        // Started in OnBattleStart, joined in OnBattleEnd. Disabled when
        // EnableTacticalOrchestratorWorker is false. Currently runs as a non-skipping
        // shadow — sync path still owns behavior; worker produces partial decisions
        // that patches don't yet consume (Task 14 will wire fallback-to-legacy lookups).
        private static TacticalOrchestratorWorker _worker;
        // Set when any side rebuilt its heavy snapshot during the current Tick; gates
        // the worker input capture (BR-M2).
        private static bool _heavyBuiltThisTick;

        /// <summary>
        /// Battle sequence integer used as the parity-window key in
        /// TacticalBattleSnapshotBuilder. Increments on each OnBattleStart;
        /// reset to 0 in ClearForFailure. Public so the snapshot builder can
        /// dedupe parity state per battle.
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

                int playerCicAllianceId = aiVsAi ? -1 : ResolvePlayerCicAllianceId();
                int suppressedAllianceId = aiVsAi ? -1 : playerAllianceId;
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
                TacticalDecisionSnapshot.SetLiveBattleSequence(_battleSequence);

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
                OnceLog.Info("orch-bootstrap", "[TacticalOrchestrator] bootstrap sidesActive=" + activated
                    + " sidesSuppressed=" + suppressed);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleStart skipped: "
                    + e.GetType().Name + " " + e.Message);
                ClearForFailure();
            }

            // Slice 2: start the worker thread once bootstrap has settled. Never after
            // a failed bootstrap (ClearForFailure left active=false), and never while a
            // previous worker reference is still held (its Join may have timed out):
            // stop that one first so two threads never run for one battle. Its late
            // publishes are rejected by the battle-sequence gate (BR-M2).
            try
            {
                StopWorker();
                if (ShouldStartWorker(IsWorkerEnabled(), active))
                {
                    _worker = new TacticalOrchestratorWorker();
                    _worker.Start();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] worker start failed: "
                    + ex.GetType().Name + " " + ex.Message);
                _worker = null;
            }

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

        // RequestStop + bounded Join so teardown never blocks on a wedged worker. If the
        // Join times out the background thread finishes its current cycle; its publish
        // is rejected because the battle sequence is closed (TacticalDecisionSnapshot).
        private static void StopWorker()
        {
            var worker = _worker;
            _worker = null;
            if (worker == null) return;
            try
            {
                worker.RequestStop();
                worker.Join(250);
            }
            catch (Exception we)
            {
                OnceLog.Warning("orch-worker-stop", "[TacticalOrchestrator] worker stop failed: "
                    + we.GetType().Name + " " + we.Message);
            }
        }

        private static bool IsWorkerEnabled()
        {
            try
            {
                return Plugin.EnableTacticalOrchestratorWorker != null
                    && Plugin.EnableTacticalOrchestratorWorker.Value;
            }
            catch
            {
                return false;
            }
        }

        public static void OnBattleEnd()
        {
            if (!active) return;
            try
            {
                // Slice 2: stop the worker before tearing down orchestrator state.
                StopWorker();

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
                OnceLog.Info("orch-teardown", "[TacticalOrchestrator] teardown");
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
                    _heavyBuiltThisTick = false;
                    bool aiVsAi = SafeAiVsAi();
                    float deltaSeconds = ComputeTickDeltaSeconds();

                    // Drive synchronous per-side tick FIRST. This publishes into
                    // _lastPublishedSnapshots[s] whenever the heavy-gate fires
                    // (DriveTickCycle line ~457). Worker capture below reads
                    // that published snapshot — never calls Build itself —
                    // eliminating the double-build that occurred when both
                    // worker-capture and DriveTickCycle independently consulted
                    // TacticalHeavyPathGate and both built on the same tick.
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
                        Plugin.Log.LogWarning("[TacticalOrchestrator] cache-validator threw: "
                            + cve.GetType().Name + " " + cve.Message);
                    }

                    // Slice 2: capture worker input and enqueue. Worker is async
                    // by design — being one tick behind on data is fine. Worker
                    // capture reads the snapshots Drive* just published; if
                    // neither side has published yet (very first tick), capture
                    // returns null and the worker waits one more tick. Patches
                    // fall through to legacy in the meantime.
                    bool workerActive = IsWorkerEnabled() && _worker != null;
                    if (ShouldCaptureWorkerInput(workerActive, _heavyBuiltThisTick))
                    {
                        try
                        {
                            var workerInput = CaptureWorkerInputOnMain(battle, deltaSeconds);
                            if (workerInput != null) _worker.Enqueue(workerInput);
                        }
                        catch (Exception ce)
                        {
                            Plugin.Log.LogWarning("[TacticalOrchestrator] worker capture skipped: "
                                + ce.GetType().Name + " " + ce.Message);
                        }
                    }

                    // Slice 2 parity window: compare worker-published vs synchronous
                    // state for the first N cycles per (battleSeq, alliance). The
                    // sync path above has already run ArmyTickCycle (which calls
                    // ObserveIntent), so side.Army.CurrentIntentModel now reflects
                    // the synchronous tick — safe to snapshot here for comparison.
                    if (workerActive)
                    {
                        try
                        {
                            if (side0 != null && side0.Army != null)
                            {
                                var sync0 = SnapshotSideDecisionForParity(side0);
                                CompareWorkerVsSynchronous(side0.AllianceId, sync0);
                            }
                            if (side1 != null && side1.Army != null)
                            {
                                var sync1 = SnapshotSideDecisionForParity(side1);
                                CompareWorkerVsSynchronous(side1.AllianceId, sync1);
                            }
                        }
                        catch (Exception pe)
                        {
                            Plugin.Log.LogWarning("[TacticalOrchestrator] parity compare failed: "
                                + pe.GetType().Name + " " + pe.Message);
                        }
                    }

                    // Mark as processed for this vanilla side-stat cycle (using the value the vanilla set before calling CheckGlobal)
                    if (battleKey != 0 && vanillaLastSideStat > 0f)
                    {
                        _lastProcessedSideStatUpdateByBunitsId[battleKey] = vanillaLastSideStat;
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("[TacticalOrchestrator] Tick skipped: "
                        + e.GetType().Name + " " + e.Message);
                }
            }
        }

        // ---- Private runtime helpers ----

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

                ArmyEvidenceBuilder.Bundle bundleForReplan;
                if (!IsHeavyThrottlingEnabled())
                {
                    // Preserve exact pre-Task-6 behavior when feature disabled (or config missing)
                    bundleForReplan = ArmyEvidenceBuilder.Build(battle, side.AllianceId);
                }
                else
                {
                    // Heavy path gated: cheap signature extract + gate decide + reuse or build snapshot once
                    float nowH = SafeCurrentBattleHours();
                    var currSig = TacticalBattleSnapshotBuilder.ExtractCurrentSignature(battle, nowH);
                    int s = side.AllianceId;
                    if (s < 0 || s > 1) s = 0;
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
                    TacticalBattleRuntimeSnapshot snap;
                    // Plan F (2026-05-20): the heavy build is alternated between
                    // sides via _heavyTickParity (flipped at end of Tick body).
                    // If parity is not this side's turn AND we have a cached
                    // snapshot for it, defer the build so the per-Update-frame
                    // hitch doesn't pile both sides' BuildObjectiveRecords walks
                    // on the same frame. We do NOT update _lastHeavyReview* on
                    // stagger-skip so the gate keeps signaling ShouldRun next cycle.
                    bool staggerSkip = dec.ShouldRun
                        && s != _heavyTickParity
                        && _lastPublishedSnapshots[s].HasData;
                    if (staggerSkip)
                    {
                        OnceLog.Info(
                            "tactical-orch:side-stagger:" + s,
                            "[TacticalOrchestrator] side-stagger-skip side=" + s
                            + " parity=" + _heavyTickParity
                            + " (reusing cached snapshot; other side gets this build slot)");
                    }
                    // Layer 2 frame-coalescing (2026-05-22). If we already built
                    // a heavy snapshot for this side IN THIS FRAME, skip the
                    // rebuild even on ShouldRun. Defense against multiple Tick
                    // calls per frame producing redundant heavy work — even
                    // legitimate signature mutations from different sources
                    // (mass rout + objective change + reinforcement) coalesce
                    // into ONE build per frame per side.
                    int frameNow = UnityEngine.Time.frameCount;
                    bool sameFrameAsLastBuild = _lastHeavyBuildFrame[s] == frameNow;
                    if (dec.ShouldRun && !staggerSkip && sameFrameAsLastBuild)
                    {
                        OnceLog.Info(
                            "tactical-orch:frame-coalesce:" + s,
                            "[TacticalOrchestrator] frame-coalesce side=" + s
                            + " frame=" + frameNow + " — already built this frame, reusing snapshot");
                    }
                    if (dec.ShouldRun && !staggerSkip && !sameFrameAsLastBuild)
                    {
                        _lastHeavyBuildFrame[s] = frameNow;
                        using (TelemetryPerf.Scope("tactical.snapshot-build.tick-cycle", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                        {
                            snap = TacticalBattleSnapshotBuilder.Build(battle, s, currSig, nowH);
                        }
                        _lastHeavyReviewHours[s] = nowH;
                        _lastHeavyReviewRealtimeSeconds[s] = nowReal;
                        _lastSignatures[s] = currSig;
                        _lastPublishedSnapshots[s] = snap;
                        _hasPendingChange[s] = false;
                        _heavyBuiltThisTick = true;

                        // "Cheap per-group" pre-compute (2026-05-20). Build
                        // resolved per-Regiment decisions for every commandable
                        // group in bunits.unitsused for this side. The patch
                        // reads from _resolvedDecisionsBySide via
                        // TryGetResolvedGroupDecision so per-tick work
                        // collapses to dict lookup + cheap safety gates.
                        using (TelemetryPerf.Scope("tactical.resolved-group-decisions.build", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                        {
                            PopulateResolvedDecisionsForSide(side, battle);
                        }
                    }
                    else
                    {
                        snap = _lastPublishedSnapshots[s];
                        if (!currSig.SignatureEquals(lastS) && !hasP)
                        {
                            _hasPendingChange[s] = true;
                        }
                    }
                    // Synthesize bundle-like data from snapshot (reuses the expensive data built only when gate allowed).
                    // Readiness fields propagate so the throttled tick keeps the
                    // same fresh-troops decision as the direct path.
                    bundleForReplan = new ArmyEvidenceBuilder.Bundle(
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
                float nowBattleHours = SafeCurrentBattleHours();
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
                    OnceLog.Info("orch-intent:" + _battleSequence + ":" + side.AllianceId + ":" + intent.PrimaryIntent + ":" + intent.InferredMainEffort,
                        "[TacticalIntent] side=" + side.AllianceId
                        + " seesEnemy=" + intent.PrimaryIntent
                        + " mainEffort=" + intent.InferredMainEffort
                        + " confidence=" + intent.Confidence01.ToString("0.00"));
                }

                if (trigger != ReplanTrigger.None)
                {
                    OnceLog.Info("orch-replan:" + _battleSequence + ":" + side.AllianceId + ":" + trigger + ":" + side.Army.CurrentPlan.PlanId,
                        "[TacticalReplan] side=" + side.AllianceId
                        + " trigger=" + trigger
                        + " newPlan=" + side.Army.CurrentPlan.PlanId
                        + " phase=" + side.Army.CurrentPlan.Phase);
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
                        side.OperationsLedger.SetRuntimeClock(LedgerClockSecondsFromBattleHours(SafeCurrentBattleHours()));
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
                        // Gated: use cached snapshot (built only when Decide says Run); objectives + scalars from snapshot
                        float nowH = SafeCurrentBattleHours();
                        var currSig = TacticalBattleSnapshotBuilder.ExtractCurrentSignature(battle, nowH);
                        int s = side.AllianceId;
                        if (s < 0 || s > 1) s = 0;
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
                        TacticalBattleRuntimeSnapshot snap;
                        // Plan F stagger — same logic as DriveTickCycle.
                        bool staggerSkip = dec.ShouldRun
                            && s != _heavyTickParity
                            && _lastPublishedSnapshots[s].HasData;
                        if (dec.ShouldRun && !staggerSkip)
                        {
                            using (TelemetryPerf.Scope("tactical.snapshot-build.operations-ledger", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                            {
                                snap = TacticalBattleSnapshotBuilder.Build(battle, s, currSig, nowH);
                            }
                            _lastHeavyReviewHours[s] = nowH;
                            _lastHeavyReviewRealtimeSeconds[s] = nowReal;
                            _lastSignatures[s] = currSig;
                            _lastPublishedSnapshots[s] = snap;
                            _hasPendingChange[s] = false;
                            _heavyBuiltThisTick = true;
                        }
                        else
                        {
                            snap = _lastPublishedSnapshots[s];
                            if (!currSig.SignatureEquals(lastS) && !hasP)
                            {
                                _hasPendingChange[s] = true;
                            }
                        }
                        objectivesToUse = snap.Objectives;
                        bundleForStrategic = new ArmyEvidenceBuilder.Bundle(
                            snap.OwnEvidence,
                            snap.EnemyVisible,
                            snap.OwnMainEffortStrength,
                            snap.OwnArmyMorale,
                            snap.OwnReservesCommittedFraction,
                            snap.ReinforcementsArrivingDelta);
                        forceMain = snap.OwnMainEffortStrength;
                        forceReserveAvail = Math.Max(0f, 1f - Clamp01(snap.OwnReservesCommittedFraction));
                    }

                    var strategic = BuildStrategicBattleIntentSnapshot(side, bundleForStrategic);
                    var force = new ForceAvailabilitySnapshot(forceMain, forceReserveAvail);

                    side.OperationsLedger.SetRuntimeClock(LedgerClockSecondsFromBattleHours(SafeCurrentBattleHours()));
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
        /// attack or support. Each node's combat units are summed once (a corps and its
        /// division may both be tasked) from Regiment.allattachedunits (vanilla's
        /// attachment list, UpdateAllAttachedUnits 129573), with the default filters of
        /// vanilla GetAttachedUnitsReg: no dechained (permanentlydetached) units, no
        /// skirmisher companies (unittyp 3/4), no inactive or routed units; plus
        /// markedforrout. 0 when no node is tasked.
        /// </summary>
        private static float MeasureOperationAssignedStrength(IReadOnlyList<CommandNodeOperationalState> states)
        {
            try
            {
                if (states == null || states.Count == 0) return 0f;
                Dictionary<int, Regiment> byInstanceId = null;
                HashSet<int> counted = null;
                float total = 0f;
                for (int i = 0; i < states.Count; i++)
                {
                    var task = states[i].Task;
                    if (task != CommandTaskType.AttackObjective && task != CommandTaskType.SupportAttack) continue;
                    int instanceId = ParseCommandNodeInstanceId(states[i].NodeId);
                    if (instanceId == 0) continue;
                    if (byInstanceId == null)
                    {
                        byInstanceId = BuildGameObjectInstanceMap();
                        counted = new HashSet<int>();
                    }
                    if (!byInstanceId.TryGetValue(instanceId, out Regiment group) || group == null) continue;
                    var units = group.allattachedunits;
                    if (units == null) continue;
                    for (int u = 0; u < units.Length; u++)
                    {
                        var unit = units[u];
                        if (unit == null || unit.unittyp > TacticalUnitType.MaxCombat) continue;
                        if (unit.unittyp == 3 || unit.unittyp == 4 || unit.permanentlydetached) continue;
                        if (unit.isrouted || unit.markedforrout || !unit.gameObject.activeInHierarchy) continue;
                        if (!counted.Add(unit.GetInstanceID())) continue;
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

        private static Dictionary<int, Regiment> BuildGameObjectInstanceMap()
        {
            var map = new Dictionary<int, Regiment>();
            var units = BattleUnits.completeunitlist;
            if (units == null) return map;
            for (int i = 0; i < units.Count; i++)
            {
                var reg = units[i];
                if (reg == null) continue;
                map[reg.gameObject.GetInstanceID()] = reg;
            }
            return map;
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

        // ---- Slice 2 parity window: worker vs synchronous decision compare ----
        //
        // Snapshot the live (post-sync-tick) orchestrator state into a
        // TacticalSideDecision so it can be compared against the worker's
        // published decision. We mirror only the worker-populated fields:
        //   AllianceId, CurrentIntent (via Army.CurrentIntentModel, set by
        //   ArmyTickCycle.ObserveIntent during the sync tick), CommandTree,
        //   CommanderPersonality (via FindArmyEntry).
        //
        // Skipped fields (worker doesn't populate, comparator skips):
        //   LastReplanTrigger, StrategicIntent text.
        //   (Gap-fill chunk 3 (2026-05-19): HasPlan + CurrentPlan now populated
        //   on both sides; comparator covers HasPlan + CurrentPlan.MainEffortSector
        //   when HasPlan is true. PlanAgeSeconds drifts continuously and stays
        //   out of the comparator.)
        //
        // Skipped field — ForceAvailability: ArmyOrchestrator does NOT cache the
        // last ArmyEvidenceBuilder.Bundle that the worker used for its force
        // computation. Recomputing ArmyEvidenceBuilder.Build per parity cycle
        // would double the sync path's heaviest call (it already builds once in
        // DriveOperationsLedger). Intent divergence catches the same bug class
        // (stale input / ordering / config drift), so we accept the gap.
        private static TacticalSideDecision SnapshotSideDecisionForParity(TacticalBattleOrchestrator side)
        {
            try
            {
                if (side == null || side.Army == null) return TacticalSideDecision.Empty;

                var entry = FindArmyEntry(side);
                PersonalityVector personality = entry != null
                    ? entry.PersonalityVector
                    : side.Army.CommanderPersonality;

                // Gap-fill chunk 3: capture sync-side plan state so the
                // comparator's two inputs populate HasPlan + CurrentPlan
                // identically. Without this the worker's chunk-3 population
                // would fire a parity mismatch on every cycle for any side
                // with a plan.
                bool syncHasPlan = side.Army.HasPlan;
                var syncPlan = syncHasPlan ? side.Army.CurrentPlan : default(TacticalBattlePlan);

                return new TacticalSideDecision(
                    allianceId: side.AllianceId,
                    currentPlan: syncPlan,                                     // chunk 3
                    currentIntent: side.Army.CurrentIntentModel,
                    commandTree: side.Army.CurrentCommandTree ?? CommandTreeSnapshot.Empty,
                    strategicIntent: default,                                  // skipped (text fields)
                    forceAvailability: new ForceAvailabilitySnapshot(0f, 0f),  // skipped (see note above)
                    commanderPersonality: personality,
                    lastReplanTrigger: ReplanTrigger.None,                     // skipped
                    hasPlan: syncHasPlan);                                     // chunk 3
            }
            catch
            {
                return TacticalSideDecision.Empty;
            }
        }

        private static void CompareWorkerVsSynchronous(int allianceId, TacticalSideDecision synchronous)
        {
            var current = TacticalDecisionSnapshot.Current;
            if (current == null || current == TacticalDecisionSnapshot.Empty) return;
            var workerDecision = current.GetSide(allianceId);

            int key = _battleSequence * 10 + allianceId;
            if (_workerParityMismatchObserved.Contains(key)) return;

            if (!SideDecisionsEqual(synchronous, workerDecision))
            {
                _workerParityMismatchObserved.Add(key);
                EmitWorkerParityMismatch(allianceId, synchronous, workerDecision);
                return;
            }

            int remaining;
            if (!_workerParityComparesRemaining.TryGetValue(key, out remaining)) remaining = WorkerParityCompareBudget;
            if (remaining > 0)
            {
                _workerParityComparesRemaining[key] = remaining - 1;
                if (remaining == 1)
                {
                    OnceLog.Info("orch-worker-parity:" + key,
                        "[TacticalOrchestratorWorkerParity] window-clean battleSeq="
                        + _battleSequence + " alliance=" + allianceId
                        + " compares=" + WorkerParityCompareBudget);
                }
            }
        }

        private static bool SideDecisionsEqual(TacticalSideDecision a, TacticalSideDecision b)
        {
            if (a.AllianceId != b.AllianceId) return false;
            // CurrentIntent: compare PrimaryIntent + InferredMainEffort — the
            // substantive fields both sides derive from ArmyIntentInference.Build.
            // Confidence/age skipped because floating-point ordering may drift.
            if (a.CurrentIntent.PrimaryIntent != b.CurrentIntent.PrimaryIntent) return false;
            if (a.CurrentIntent.InferredMainEffort != b.CurrentIntent.InferredMainEffort) return false;
            // Gap-fill chunk 3 (2026-05-19): plan-state parity coverage.
            // HasPlan must match; when both are HasPlan==true we also compare
            // the plan's MainEffortSector (stable, integer-valued). PlanId /
            // Phase / AgeSeconds intentionally skipped — phase progression and
            // age drift on the sync side every tick, and PlanId is set on
            // replan which the worker doesn't drive (LastReplanTrigger still
            // deferred).
            if (a.HasPlan != b.HasPlan) return false;
            if (a.HasPlan && b.HasPlan)
            {
                if (a.CurrentPlan.MainEffortSector != b.CurrentPlan.MainEffortSector) return false;
            }
            // Skipped (worker doesn't populate yet — remaining gap-fills):
            //   LastReplanTrigger, StrategicIntent text fields,
            //   ForceAvailability (Army doesn't cache the bundle; see SnapshotSideDecisionForParity).
            return true;
        }

        private static void EmitWorkerParityMismatch(int allianceId, TacticalSideDecision synchronous, TacticalSideDecision worker)
        {
            try
            {
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Gate,
                    "TacticalOrchestratorParityMismatch",
                    TelemetrySeverity.Warning,
                    ev => ev
                        .WithSide(allianceId)
                        .WithDecision("mismatch", "side-decision", "battleSeq=" + _battleSequence + "|alliance=" + allianceId)
                        .WithField("allianceId", allianceId)
                        .WithField("battleSequence", _battleSequence)
                        .WithField("syncIntent", synchronous.CurrentIntent.PrimaryIntent.ToString())
                        .WithField("workerIntent", worker.CurrentIntent.PrimaryIntent.ToString())
                        .WithField("syncMainEffort", synchronous.CurrentIntent.InferredMainEffort)
                        .WithField("workerMainEffort", worker.CurrentIntent.InferredMainEffort));
            }
            catch { }
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
                IReadOnlyList<DirectChildSnapshot> snapshots;
                using (TelemetryPerf.Scope("tactical.direct-child-discovery-snapshot", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    snapshots = DirectChildDiscovery.Snapshot(side.AllianceId);
                }
                if (snapshots.Count == 0)
                {
                    if (side.Army.CurrentDirectChildIntents.Count == 0 &&
                        !_directChildDeferLogged.Contains(side.AllianceId))
                    {
                        _directChildDeferLogged.Add(side.AllianceId);
                        OnceLog.Info("o3-defer-discovery:" + side.AllianceId,
                            "side=" + side.AllianceId + " reason=empty-or-no-command-units");
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

                if (current.HasNodes
                    && string.Equals(CommandTreeSignature(current), CommandTreeSignature(tree), StringComparison.Ordinal))
                {
                    return;
                }

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

        private static string CommandTreeSignature(CommandTreeSnapshot tree)
        {
            if (tree == null || !tree.HasNodes)
            {
                return string.Empty;
            }

            var signature = new StringBuilder();
            signature.Append(tree.RootNodeId).Append(':')
                .Append(tree.Nodes.Count).Append(':')
                .Append(tree.MaxDepth).Append(':')
                .Append(tree.RawUnitTypDistribution).Append(':')
                .Append(tree.MissingParentCount);

            for (int i = 0; i < tree.Nodes.Count; i++)
            {
                var node = tree.Nodes[i];
                signature.Append('|')
                    .Append(node.NodeId).Append('<')
                    .Append(node.ParentNodeId).Append(':')
                    .Append(node.RawUnitTyp).Append(':')
                    .Append(node.Depth);
            }

            return signature.ToString();
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
                string key = _battleSequence + ":" + allianceId;
                string signature = CommandTreeSignature(tree);

                string existing;
                if (_commandTreeTelemetrySignatures.TryGetValue(key, out existing)
                    && string.Equals(existing, signature, StringComparison.Ordinal))
                {
                    return;
                }

                _commandTreeTelemetrySignatures[key] = signature;
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

                EnemyVisibleState enemyVisForChildren;
                if (!IsHeavyThrottlingEnabled())
                {
                    // Preserve exact pre-Task-6 behavior
                    var bundle = ArmyEvidenceBuilder.Build(battle, side.AllianceId);
                    enemyVisForChildren = bundle.EnemyVisible;
                }
                else
                {
                    // Gated heavy snapshot reuse for DirectChild evidence/intent (EnemyVisible is the needed part)
                    float nowH = SafeCurrentBattleHours();
                    var currSig = TacticalBattleSnapshotBuilder.ExtractCurrentSignature(battle, nowH);
                    int s = side.AllianceId;
                    if (s < 0 || s > 1) s = 0;
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
                    TacticalBattleRuntimeSnapshot snap;
                    // Plan F stagger — same logic as DriveTickCycle.
                    bool staggerSkip = dec.ShouldRun
                        && s != _heavyTickParity
                        && _lastPublishedSnapshots[s].HasData;
                    if (dec.ShouldRun && !staggerSkip)
                    {
                        using (TelemetryPerf.Scope("tactical.snapshot-build.direct-child-cycle", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                        {
                            snap = TacticalBattleSnapshotBuilder.Build(battle, s, currSig, nowH);
                        }
                        _lastHeavyReviewHours[s] = nowH;
                        _lastHeavyReviewRealtimeSeconds[s] = nowReal;
                        _lastSignatures[s] = currSig;
                        _lastPublishedSnapshots[s] = snap;
                        _hasPendingChange[s] = false;
                        _heavyBuiltThisTick = true;
                    }
                    else
                    {
                        snap = _lastPublishedSnapshots[s];
                        if (!currSig.SignatureEquals(lastS) && !hasP)
                        {
                            _hasPendingChange[s] = true;
                        }
                    }
                    enemyVisForChildren = snap.EnemyVisible;
                }

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
                    OnceLog.Info("o3-direct-child-intent:" + _battleSequence + ":" + side.AllianceId + ":" + dci.ChildId + ":" + dci.Role,
                        "[TacticalDirectChildIntent] " + SideLogContext(side.AllianceId)
                        + " child=" + dci.ChildId
                        + " childName=" + SafeLog(dci.DisplayName)
                        + " raw=" + dci.RawUnitTyp
                        + " effective=" + dci.EffectiveCommandLevel
                        + " role=" + dci.Role
                        + " sector=" + dci.PrimarySector
                        + " support=" + dci.SupportPriority01.ToString("0.00")
                        + " enemyIntent=" + dci.EnemyIntent.PrimaryIntent
                        + " confidence=" + dci.EnemyIntent.Confidence01.ToString("0.00"));
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

        // ---- Slice 2: worker input capture (runs on main thread) ----

        /// <summary>
        /// Builds a TacticalOrchestratorWorkerInput from live vanilla state. All
        /// reflection / Unity API touches happen here on the main thread; the worker
        /// only reads the resulting immutable container. Returns null on failure so
        /// the caller can skip enqueue without breaking the sync drive path.
        /// </summary>
        private static TacticalOrchestratorWorkerInput CaptureWorkerInputOnMain(AIBattle battle, float deltaSeconds)
        {
            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return null;

                int allianceA = SafeAllianceForSide(bunits, 0);
                int allianceB = SafeAllianceForSide(bunits, 1);
                if (allianceA < 0 || allianceB < 0) return null;

                float nowH = SafeCurrentBattleHours();
                float nowReal = SafeRealtimeSeconds();
                // Worker-input battleDeltaSeconds: kept at 0 in Slice 2. The per-side
                // _lastTickBattleHours scratch state used by DriveTickCycle is owned
                // by the sync path; threading a battle-time delta through here without
                // disturbing it is a Task 14+ concern.
                float battleDelta = 0f;

                // Worker capture reads the snapshots Drive* just published into
                // _lastPublishedSnapshots[s]. Drive* runs before this in Tick(),
                // and TacticalBattleSnapshotBuilder.Build mutates
                // TacticalUnitObservationAggregate.Shared (line 263) during the
                // sync build — by the time we're here, the aggregate's last
                // mutation is from whichever side actually built this Tick.
                //
                // Pre-existing bug fix (2026-05-21): the prior ClearForBattleEnd()
                // call here reset CapturedForAlliance to -1 BEFORE SnapshotAggregateForWorker
                // could read it, which made the alliance filter `CapturedForAlliance != allianceId`
                // ALWAYS true. Result: obs0/obs1 always empty → worker had no observations
                // to compute group stance decisions OR (chunk 3) objective records. The clear
                // was originally added to prevent side-A from receiving side-B's stale data
                // when both sides built per Tick — but with F-stagger (2026-05-20) only one
                // side builds per Tick, so the agg's CapturedForAlliance correctly identifies
                // the building side and SnapshotAggregateForWorker naturally returns empty
                // for the non-building side.
                TacticalBattleRuntimeSnapshot snap0;
                ArmyEvidenceBuilder.Bundle bundle0;
                CaptureSideForWorker(battle, allianceA, nowH, out snap0, out bundle0);
                var obs0 = SnapshotAggregateForWorker(allianceA);
                TacticalBattleRuntimeSnapshot snap1;
                ArmyEvidenceBuilder.Bundle bundle1;
                CaptureSideForWorker(battle, allianceB, nowH, out snap1, out bundle1);
                var obs1 = SnapshotAggregateForWorker(allianceB);

                // First-tick fallback: if neither side has a published snapshot
                // (Drive* hasn't fired the heavy-gate for either side yet),
                // skip enqueue. The worker waits one more tick. Patches fall
                // through to legacy in the meantime.
                if (!snap0.HasData && !snap1.HasData)
                {
                    return null;
                }

                // Per-side macroAI snapshot — sourced from bunits.sideinformation
                // so the worker's DecideGroupStance sees the same vanilla state
                // BattleGroupStancePatch reads from AIBattle.macroai for the side.
                int macro0 = SafeMacroAiForSide(bunits, 0);
                int macro1 = SafeMacroAiForSide(bunits, 1);

                // Command tree comes from the snapshot the heavy build just published
                // (no second CommandTreeRuntime walk). Direct-child snapshots and the
                // instance->sector maps are not read by the worker analysis, so they are
                // no longer captured (was 2x DirectChildDiscovery + 2x sector-map walk per
                // Tick on main, BR-H4/BR-M2).
                CommandTreeSnapshot tree0 = snap0.CommandTree ?? CommandTreeSnapshot.Empty;
                CommandTreeSnapshot tree1 = snap1.CommandTree ?? CommandTreeSnapshot.Empty;

                CommanderRosterEntry entry0 = FindArmyEntry(side0);
                CommanderRosterEntry entry1 = FindArmyEntry(side1);

                // Gap-fill chunk 3: snapshot ArmyOrchestrator plan state for
                // each side. Each side may be null (suppressed). Plan state is
                // value-typed; default() == Empty when no plan is fielded.
                var planState0 = CaptureArmyPlanState(side0);
                var planState1 = CaptureArmyPlanState(side1);

                // Worker-migration (2026-05-21): per-side ObjectiveRecordsCaptureContext.
                // Only capture when the feature flag is on; empty context otherwise so
                // the worker.BuildObjectivesForSide short-circuits and main's
                // TryGetObjectiveRecords returns false, routing snapshot.Build back to
                // the legacy on-main BuildObjectiveRecordsFromAggregate path.
                ObjectiveRecordsCaptureContext objCtx0 = ObjectiveRecordsCaptureContext.Empty;
                ObjectiveRecordsCaptureContext objCtx1 = ObjectiveRecordsCaptureContext.Empty;
                if (Plugin.EnableWorkerObjectiveRecords != null && Plugin.EnableWorkerObjectiveRecords.Value)
                {
                    objCtx0 = TacticalVisionRuntimeAdapter.CaptureObjectiveRecordsContextForSide(battle, allianceA);
                    objCtx1 = TacticalVisionRuntimeAdapter.CaptureObjectiveRecordsContextForSide(battle, allianceB);
                }

                var config = BuildWorkerConfigSnapshot();

                return new TacticalOrchestratorWorkerInput(
                    battleSequence: _battleSequence,
                    playerAllianceId: _playerAllianceId,
                    aiVsAi: SafeAiVsAi(),
                    battleHours: nowH,
                    realtimeSeconds: nowReal,
                    deltaSeconds: deltaSeconds,
                    battleDeltaSeconds: battleDelta,
                    side0Snapshot: snap0,
                    side1Snapshot: snap1,
                    side0Bundle: bundle0,
                    side1Bundle: bundle1,
                    side0CommandTree: tree0,
                    side1CommandTree: tree1,
                    side0DirectChildren: null,
                    side1DirectChildren: null,
                    side0CommanderEntry: entry0,
                    side1CommanderEntry: entry1,
                    side0InstanceToSectorMap: null,
                    side1InstanceToSectorMap: null,
                    config: config,
                    side0Observations: obs0,
                    side1Observations: obs1,
                    side0AllianceId: allianceA,
                    side1AllianceId: allianceB,
                    side0MacroAi: macro0,
                    side1MacroAi: macro1,
                    side0PlanState: planState0,
                    side1PlanState: planState1,
                    side0ObjectiveCaptureContext: objCtx0,
                    side1ObjectiveCaptureContext: objCtx1);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] CaptureWorkerInputOnMain failed: "
                    + e.GetType().Name + " " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Read the snapshot just published by <c>DriveTacticalCommanderSide</c>
        /// for the given alliance and synthesize an evidence bundle from the
        /// snapshot fields. Never calls
        /// <see cref="TacticalBattleSnapshotBuilder.Build"/> or
        /// <see cref="ArmyEvidenceBuilder.Build"/> — that work is owned by the
        /// synchronous Drive* path, and the worker reads the cached result so
        /// no double-build occurs when both consult <c>TacticalHeavyPathGate</c>.
        ///
        /// Worker is async by design: being one tick behind on data is fine
        /// because the worker writes its decisions another tick later. The
        /// bundle synthesis mirrors the post-gate synthesis inside
        /// <see cref="DriveTickCycle"/> (search for "Synthesize bundle-like
        /// data from snapshot") — same fields, same constructor.
        ///
        /// First-tick fallback: if Drive* hasn't published yet (<c>snap.HasData
        /// == false</c>), return <see cref="TacticalBattleRuntimeSnapshot.Empty"/>
        /// + <c>default(Bundle)</c>. The caller short-circuits the enqueue when
        /// both sides come back empty, so the worker waits one more tick.
        ///
        /// Out-of-range alliance (Europe / unmapped, allianceId &gt; 1): the
        /// snapshot/cache arrays are sized 2, so we return Empty rather than
        /// touch them. The worker has no role for Europe alliances anyway.
        /// </summary>
        private static void CaptureSideForWorker(
            AIBattle battle,
            int allianceId,
            float nowH,
            out TacticalBattleRuntimeSnapshot snap,
            out ArmyEvidenceBuilder.Bundle bundle)
        {
            try
            {
                int s = allianceId;
                if (s < 0 || s > 1)
                {
                    snap = TacticalBattleRuntimeSnapshot.Empty;
                    bundle = default(ArmyEvidenceBuilder.Bundle);
                    return;
                }

                // Read the snapshot Drive* just published (or the prior cached
                // value if the heavy-gate skipped this tick). If Drive* hasn't
                // run yet for this side (first tick, or
                // ShouldRunTacticalCommanderForSide returned false), HasData is
                // false and the caller skips enqueue.
                snap = _lastPublishedSnapshots[s];
                if (!snap.HasData)
                {
                    bundle = default(ArmyEvidenceBuilder.Bundle);
                    return;
                }

                // Synthesize a Bundle from snapshot fields — same shape as the
                // synthesis DriveTickCycle does internally (search for
                // "Synthesize bundle-like data from snapshot").
                bundle = new ArmyEvidenceBuilder.Bundle(
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
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] CaptureSideForWorker failed: "
                    + e.GetType().Name + " " + e.Message);
                snap = TacticalBattleRuntimeSnapshot.Empty;
                bundle = default(ArmyEvidenceBuilder.Bundle);
            }
        }

        /// <summary>
        /// Gap-fill chunk 3: snapshot the current <see cref="ArmyOrchestrator"/>
        /// plan-state for a side into an immutable
        /// <see cref="TacticalArmyPlanStateSnapshot"/>. Reads happen on the
        /// main thread (Plugin/Unity safe), then the worker reads the snapshot
        /// later off-thread.
        ///
        /// Returns <see cref="TacticalArmyPlanStateSnapshot.Empty"/> when the
        /// side is null/suppressed or has no plan. All reads are try/catch
        /// guarded per runtime safety; reflection-style failures degrade to
        /// Empty rather than throwing from the capture path.
        /// </summary>
        private static TacticalArmyPlanStateSnapshot CaptureArmyPlanState(TacticalBattleOrchestrator side)
        {
            try
            {
                if (side == null || side.Army == null) return TacticalArmyPlanStateSnapshot.Empty;
                var army = side.Army;
                bool hasPlan = army.HasPlan;
                if (!hasPlan) return TacticalArmyPlanStateSnapshot.Empty;
                var plan = army.CurrentPlan;
                return new TacticalArmyPlanStateSnapshot(
                    hasPlan: true,
                    currentPlan: plan,
                    planAgeSeconds: army.PlanAgeSeconds,
                    phaseAgeSeconds: army.PhaseAgeSeconds,
                    historyGlobalOdds: army.HistoryGlobalOdds,
                    mainEffortSector: plan.MainEffortSector);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] CaptureArmyPlanState failed: "
                    + e.GetType().Name + " " + e.Message);
                return TacticalArmyPlanStateSnapshot.Empty;
            }
        }

        /// <summary>
        /// Snapshots TacticalUnitObservationAggregate.Shared into an immutable
        /// per-side array. Called immediately after each side's heavy-snapshot
        /// Build so the next side's Build doesn't overwrite our observations.
        /// Filters to units captured for the requested alliance — defends
        /// against the singleton holding a stale capture from a prior cycle.
        /// </summary>
        private static IReadOnlyList<TacticalUnitObservation> SnapshotAggregateForWorker(int allianceId)
        {
            try
            {
                var agg = TacticalUnitObservationAggregate.Shared;
                if (agg == null || agg.CapturedForAlliance != allianceId)
                    return Array.Empty<TacticalUnitObservation>();
                var src = agg.AllUnits;
                if (src == null || src.Count == 0)
                    return Array.Empty<TacticalUnitObservation>();
                var copy = new TacticalUnitObservation[src.Count];
                for (int i = 0; i < src.Count; i++) copy[i] = src[i];
                return copy;
            }
            catch
            {
                return Array.Empty<TacticalUnitObservation>();
            }
        }

        /// <summary>
        /// Reads bunits.sideinformation[side].macroai with defensive guards.
        /// Returns -1 on failure (matches BattleGroupStancePatch's fallback).
        /// </summary>
        private static int SafeMacroAiForSide(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return -1;
                if (side < 0 || side >= bunits.sideinformation.Length) return -1;
                var info = bunits.sideinformation[side];
                if (info == null) return -1;
                return info.macroai;
            }
            catch
            {
                return -1;
            }
        }

        private static TacticalOrchestratorWorkerConfigSnapshot BuildWorkerConfigSnapshot()
        {
            try
            {
                var p = Plugin.Instance;
                bool heavy = false;
                float heavyMin = 0f;
                float heavyCycle = 0f;
                TacticalCommanderMode mode = default;
                if (p != null)
                {
                    try { heavy = p.EnableTacticalHeavyPathThrottling != null && p.EnableTacticalHeavyPathThrottling.Value; } catch { }
                    try { heavyMin = p.HeavyReviewMinRealtimeSeconds; } catch { }
                    try { heavyCycle = p.HeavyReviewCycleHours; } catch { }
                    try { mode = p.TacticalCommanderModeValue; } catch { }
                }

                return new TacticalOrchestratorWorkerConfigSnapshot(
                    enableHeavyPathThrottling: heavy,
                    enableIntentInference: SafeStaticBool(Plugin.EnableTacticalOrchestratorIntentInference),
                    heavyReviewMinRealtimeSeconds: heavyMin,
                    heavyReviewCycleHours: heavyCycle,
                    minReplanSeconds: SafeStaticInt(Plugin.TacticalOrchestratorMinReplanSeconds, 60),
                    enableSinglePassObjectiveRecords: SafeStaticBool(Plugin.EnableSinglePassObjectiveRecords),
                    commanderMode: mode);
            }
            catch
            {
                return TacticalOrchestratorWorkerConfigSnapshot.Empty;
            }
        }

        private static bool SafeStaticBool(BepInEx.Configuration.ConfigEntry<bool> e)
        {
            try { return e != null && e.Value; }
            catch { return false; }
        }

        private static int SafeStaticInt(BepInEx.Configuration.ConfigEntry<int> e, int fallback)
        {
            try { return e != null ? e.Value : fallback; }
            catch { return fallback; }
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
            }
            catch { }
            // Tasks 10/11: clear aggregate pool and parity window state for new battle
            // (global singletons — outside per-side loop)
            try { TacticalUnitObservationAggregate.Shared.ClearForBattleEnd(); } catch { }
            try { TacticalBattleSnapshotBuilder.ClearParityState(); } catch { }
            // Slice 2: reset the worker decision snapshot so stale per-cycle data
            // from the previous battle never leaks to readers in the next one.
            try
            {
                TacticalDecisionSnapshot.SetLiveBattleSequence(TacticalDecisionSnapshot.ClosedBattleSequence);
                TacticalDecisionSnapshot.ResetForBattleEnd();
                TacticalOrchestratorWorkerAnalysis.ResetForBattleStart();
            }
            catch { }
            // Slice 2 parity window: reset per-battle compare counters so the
            // next battle starts a fresh 20-cycle window per side.
            try
            {
                _workerParityComparesRemaining.Clear();
                _workerParityMismatchObserved.Clear();
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

                // Time-bucketed input signature (coarse BattleHourBucket changes over time)
                string inputSig = "TacticalHeavyGate|side=" + side +
                    "|nowH=" + nowH.ToString("F4") +
                    "|lastH=" + lastH.ToString("F4") +
                    "|cycle=" + cycle.ToString("F4") +
                    "|real=" + nowReal.ToString("F2") +
                    "|minReal=" + minReal.ToString("F2") +
                    "|pending=" + hasP +
                    "|units=" + currSig.ActiveUnitCount +
                    "|objHash=" + currSig.MajorObjectiveAnchorHash +
                    "|bucket=" + currSig.BattleHourBucket;

                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Gate,
                    "TacticalHeavyGate",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithDecision(dec.ShouldRun ? "executed" : "skipped", dec.Reason, inputSig)
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
            StopWorker();
            ResetRuntimeTickState();
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
                            "tactical-resolved-cache:prepopulated:" + s,
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
                // Mirrors CaptureWorkerInputOnMain's SafeMacroAiForSide.
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
