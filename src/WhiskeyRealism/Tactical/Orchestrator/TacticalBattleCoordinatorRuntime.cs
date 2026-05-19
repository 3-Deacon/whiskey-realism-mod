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
        private static readonly TacticalBattleStateSignature[] _lastSignatures = new TacticalBattleStateSignature[2];
        private static readonly TacticalBattleRuntimeSnapshot[] _lastPublishedSnapshots = { TacticalBattleRuntimeSnapshot.Empty, TacticalBattleRuntimeSnapshot.Empty };
        private static readonly bool[] _hasPendingChange = { false, false };

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
                var roster = TacticalCommanderRoster.BuildFromSynthetic(commanders);
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
                    DriveTacticalCommanderSide(side0, battle, aiVsAi, deltaSeconds);
                    DriveTacticalCommanderSide(side1, battle, aiVsAi, deltaSeconds);

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
                    float cycle = (Plugin.Instance != null ? Plugin.Instance.HeavyReviewCycleHours : 0.003f);
                    float minReal = (Plugin.Instance != null ? Plugin.Instance.HeavyReviewMinRealtimeSeconds : 2.0f);
                    var input = new TacticalHeavyPathGate.Input(currSig, nowH, lastH, lastS, cycle, hasP, nowReal, lastReal, minReal);
                    var dec = TacticalHeavyPathGate.Decide(input);
                    EmitHeavyGateTelemetry(s, dec, nowH, lastH, cycle, hasP, currSig, nowReal, lastReal, minReal);
                    TacticalBattleRuntimeSnapshot snap;
                    if (dec.ShouldRun)
                    {
                        snap = TacticalBattleSnapshotBuilder.Build(battle, s, currSig, nowH);
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
                    // Synthesize bundle-like data from snapshot (reuses the expensive data built only when gate allowed)
                    bundleForReplan = new ArmyEvidenceBuilder.Bundle(
                        snap.OwnEvidence,
                        snap.EnemyVisible,
                        snap.OwnMainEffortStrength,
                        snap.OwnArmyMorale,
                        snap.OwnReservesCommittedFraction,
                        snap.ReinforcementsArrivingDelta);
                }

                int minReplanSeconds = (Plugin.TacticalOrchestratorMinReplanSeconds != null)
                    ? Plugin.TacticalOrchestratorMinReplanSeconds.Value
                    : 60;

                var trigger = ArmyTickCycle.MaybeReplan(
                    side.Army,
                    deltaSeconds: deltaSeconds,
                    ownEvidence: bundleForReplan.OwnEvidence,
                    enemyVisible: bundleForReplan.EnemyVisible,
                    ownMainEffortStrength: bundleForReplan.OwnMainEffortStrength,
                    ownArmyMorale: bundleForReplan.OwnArmyMorale,
                    ownReservesCommittedFraction: bundleForReplan.OwnReservesCommittedFraction,
                    reinforcementsArrivingDelta: bundleForReplan.ReinforcementsArrivingDelta,
                    minReplanSeconds: minReplanSeconds);

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
                        float cycle = (Plugin.Instance != null ? Plugin.Instance.HeavyReviewCycleHours : 0.003f);
                        float minReal = (Plugin.Instance != null ? Plugin.Instance.HeavyReviewMinRealtimeSeconds : 2.0f);
                        var input = new TacticalHeavyPathGate.Input(currSig, nowH, lastH, lastS, cycle, hasP, nowReal, lastReal, minReal);
                        var dec = TacticalHeavyPathGate.Decide(input);
                        EmitHeavyGateTelemetry(s, dec, nowH, lastH, cycle, hasP, currSig, nowReal, lastReal, minReal);
                        TacticalBattleRuntimeSnapshot snap;
                        if (dec.ShouldRun)
                        {
                            snap = TacticalBattleSnapshotBuilder.Build(battle, s, currSig, nowH);
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

        private static IEnumerable<SyntheticCommanderInput> DiscoverCommandersFromVanilla(AIBattle battle)
        {
            var inputs = new List<SyntheticCommanderInput>();
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
                    string name = SafeCommanderName(commanderId);
                    if (string.IsNullOrEmpty(name)) name = "ArmyCO_side" + side;
                    inputs.Add(new SyntheticCommanderInput(name, EchelonKind.Army, allianceId));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] DiscoverCommandersFromVanilla degraded: "
                    + e.GetType().Name + " " + e.Message);
            }
            return inputs;
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
            try
            {
                if (side == null || side.Army == null || !side.Army.HasPlan) return;
                var snapshots = DirectChildDiscovery.Snapshot(side.AllianceId);
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
            try
            {
                if (side == null || side.Army == null || !side.Army.HasPlan) return;

                var tree = CommandTreeRuntime.Snapshot(side.AllianceId);
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
                    float cycle = (Plugin.Instance != null ? Plugin.Instance.HeavyReviewCycleHours : 0.003f);
                    float minReal = (Plugin.Instance != null ? Plugin.Instance.HeavyReviewMinRealtimeSeconds : 2.0f);
                    var input = new TacticalHeavyPathGate.Input(currSig, nowH, lastH, lastS, cycle, hasP, nowReal, lastReal, minReal);
                    var dec = TacticalHeavyPathGate.Decide(input);
                    EmitHeavyGateTelemetry(s, dec, nowH, lastH, cycle, hasP, currSig, nowReal, lastReal, minReal);
                    TacticalBattleRuntimeSnapshot snap;
                    if (dec.ShouldRun)
                    {
                        snap = TacticalBattleSnapshotBuilder.Build(battle, s, currSig, nowH);
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
                }
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
                        .WithField("gateReason", dec.Reason));
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
