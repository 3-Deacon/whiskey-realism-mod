using System;
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
        // Per-side last-seen GC.CollectionCount snapshot. Mono is Boehm
        // (non-generational) so a single counter suffices; gen0/gen1/gen2
        // return identical values. Used by EmitHeavyGateTelemetry to compute
        // per-gate-decision GC pressure delta so the tuning sidecar shows
        // whether allocation churn correlates with frame hitches at high
        // time compression. Reset per battle in ResetRuntimeTickState.
        private static readonly int[] _lastGen0CountBySide = { 0, 0 };

        // Slice 2 parity window: compare worker-published decisions against the
        // synchronous orchestrator state for the first N cycles per (battleSeq,
        // alliance). After N clean compares, the window logs "window-clean" and
        // subsequent cycles no longer touch the comparator path. On any mismatch:
        // emit TacticalOrchestratorParityMismatch (registered in Task 6) and stop
        // comparing for that key to avoid log spam.
        // Key formula: _battleSequence * 10 + allianceId.
        private static readonly Dictionary<int, int> _workerParityComparesRemaining = new Dictionary<int, int>();
        private static readonly HashSet<int> _workerParityMismatchObserved = new HashSet<int>();
        private const int WorkerParityCompareBudget = 20;

        // Slice 2: dedicated worker thread for tactical orchestrator analysis.
        // Started in OnBattleStart, joined in OnBattleEnd. Disabled when
        // EnableTacticalOrchestratorWorker is false. Currently runs as a non-skipping
        // shadow — sync path still owns behavior; worker produces partial decisions
        // that patches don't yet consume (Task 14 will wire fallback-to-legacy lookups).
        private static TacticalOrchestratorWorker _worker;

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

        public static void OnBattleStart(AIBattle battle)
        {
            if (active) return;
            if (!Plugin.EnableTacticalBattleOrchestrator.Value) return;

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

            // Slice 2: start the worker thread once bootstrap has settled.
            // If bootstrap above failed ClearForFailure already nulled state out;
            // we still attempt worker start (Tick won't dispatch if !active).
            try
            {
                if (IsWorkerEnabled())
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
                // RequestStop + bounded Join so we don't block teardown if the worker
                // is wedged. Background thread will die on AppDomain unload regardless.
                try
                {
                    if (_worker != null)
                    {
                        _worker.RequestStop();
                        _worker.Join(250);
                        _worker = null;
                    }
                }
                catch (Exception we)
                {
                    Plugin.Log.LogWarning("[TacticalOrchestrator] worker stop failed: "
                        + we.GetType().Name + " " + we.Message);
                }

                ClearLedgersBetweenBattles();
                ResetRuntimeTickState();
                _directChildDeferLogged.Clear();
                _commandTreeTelemetrySignatures.Clear();
                side0 = null;
                side1 = null;
                _playerAllianceId = -1;
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

                    // Slice 2: capture worker input and enqueue. Worker is async
                    // by design — being one tick behind on data is fine. Worker
                    // capture reads the snapshots Drive* just published; if
                    // neither side has published yet (very first tick), capture
                    // returns null and the worker waits one more tick. Patches
                    // fall through to legacy in the meantime.
                    bool workerActive = IsWorkerEnabled() && _worker != null;
                    if (workerActive)
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
                    if (dec.ShouldRun)
                    {
                        using (TelemetryPerf.Scope("tactical.snapshot-build.tick-cycle", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
                        {
                            snap = TacticalBattleSnapshotBuilder.Build(battle, s, currSig, nowH);
                        }
                        _lastHeavyReviewHours[s] = nowH;
                        _lastHeavyReviewRealtimeSeconds[s] = nowReal;
                        _lastSignatures[s] = currSig;
                        _lastPublishedSnapshots[s] = snap;
                        _hasPendingChange[s] = false;
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
                        side.OperationsLedger.SetRuntimeClock(SafeRealtimeSeconds());
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
                        if (dec.ShouldRun)
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

                    side.OperationsLedger.SetRuntimeClock(SafeRealtimeSeconds());
                    side.TickOperationsLedger(
                        mode,
                        objectivesToUse,
                        strategic,
                        force,
                        side.Army.CommanderPersonality);
                }
                catch (Exception e)
                {
                    WarnTickCycleOnce(side, e);
                }
            }
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
                    if (dec.ShouldRun)
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
                side.Army.ObserveDirectChildEvidenceWithIntent(evidence, perChildIntent);

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
                // mutation is from side1's sync build (or side0's if side1 was
                // suppressed). Clear the aggregate so each side's
                // SnapshotAggregateForWorker only counts data tagged with its
                // own alliance: SnapshotAggregateForWorker checks
                // CapturedForAlliance, and ClearForBattleEnd resets that to -1.
                // (Steady state: the worker sees a one-tick-lagged aggregate,
                // which is fine — async worker design.)
                try { TacticalUnitObservationAggregate.Shared.ClearForBattleEnd(); } catch { }
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

                CommandTreeSnapshot tree0 = SafeCommandTreeSnapshot(allianceA);
                CommandTreeSnapshot tree1 = SafeCommandTreeSnapshot(allianceB);
                IReadOnlyList<DirectChildSnapshot> dc0 = SafeDirectChildSnapshot(allianceA);
                IReadOnlyList<DirectChildSnapshot> dc1 = SafeDirectChildSnapshot(allianceB);

                var sectorMap0 = BuildInstanceToSectorIndexMap(battle, allianceA);
                var sectorMap1 = BuildInstanceToSectorIndexMap(battle, allianceB);

                CommanderRosterEntry entry0 = FindArmyEntry(side0);
                CommanderRosterEntry entry1 = FindArmyEntry(side1);

                // Gap-fill chunk 3: snapshot ArmyOrchestrator plan state for
                // each side. Each side may be null (suppressed). Plan state is
                // value-typed; default() == Empty when no plan is fielded.
                var planState0 = CaptureArmyPlanState(side0);
                var planState1 = CaptureArmyPlanState(side1);

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
                    side0DirectChildren: dc0,
                    side1DirectChildren: dc1,
                    side0CommanderEntry: entry0,
                    side1CommanderEntry: entry1,
                    side0InstanceToSectorMap: sectorMap0,
                    side1InstanceToSectorMap: sectorMap1,
                    config: config,
                    side0Observations: obs0,
                    side1Observations: obs1,
                    side0AllianceId: allianceA,
                    side1AllianceId: allianceB,
                    side0MacroAi: macro0,
                    side1MacroAi: macro1,
                    side0PlanState: planState0,
                    side1PlanState: planState1);
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

        private static CommandTreeSnapshot SafeCommandTreeSnapshot(int allianceId)
        {
            try { return CommandTreeRuntime.Snapshot(allianceId); }
            catch { return CommandTreeSnapshot.Empty; }
        }

        private static IReadOnlyList<DirectChildSnapshot> SafeDirectChildSnapshot(int allianceId)
        {
            try { return DirectChildDiscovery.Snapshot(allianceId); }
            catch { return Array.Empty<DirectChildSnapshot>(); }
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
                }
            }
            catch { }
            // Tasks 10/11: clear aggregate pool and parity window state for new battle
            // (global singletons — outside per-side loop)
            try { TacticalUnitObservationAggregate.Shared.ClearForBattleEnd(); } catch { }
            // Active-set scoping: drop the cached BattleUnits singleton so the
            // next battle's Capture resolves a fresh instance via aibattleref.
            try { TacticalUnitObservationAggregate.ResetBattleUnitsCache(); } catch { }
            try { TacticalBattleSnapshotBuilder.ClearParityState(); } catch { }
            // Slice 2: reset the worker decision snapshot so stale per-cycle data
            // from the previous battle never leaks to readers in the next one.
            try { TacticalDecisionSnapshot.ResetForBattleEnd(); } catch { }
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
            ResetRuntimeTickState();
            side0 = null;
            side1 = null;
            _playerAllianceId = -1;
            active = false;
        }
    }
}
