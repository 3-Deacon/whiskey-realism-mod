using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Strategic.Policies;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    public class StrategicCoordinator : MonoBehaviour
    {
        public static StrategicCoordinator Instance { get; private set; }

        public CIC[] CICs = new CIC[2];
        public EraStageManager[] Eras = new EraStageManager[2];
        public FrontSectorLedger[] Fronts = new FrontSectorLedger[2];
        public ArmyAreaLedger[] ArmyAreas = new ArmyAreaLedger[2];
        public FormationDirectiveLedger[] FormationDirectives = new FormationDirectiveLedger[2];
        public OperationalProbeOutput[] OperationalProbes = new OperationalProbeOutput[2];
        public FiscalOutput[] FiscalIntents = new FiscalOutput[2];
        public ConstructionOutput[] ConstructionIntents = new ConstructionOutput[2];
        public ConstructionTelemetry ConstructionTelemetry = new ConstructionTelemetry();
        public DefenseIntentLedgerOutput[] DefenseIntents = new DefenseIntentLedgerOutput[2];
        // Stage E need-driven policy: per-alliance smoothed needs, the snapshot they came from
        // (ProjectSelectionPatch signals) and the ranked policy plan PolicySelectionPatch reads.
        public NeedVector[] Needs = new NeedVector[2];
        public NeedSnapshot[] NeedSnapshots = new NeedSnapshot[2];
        public PolicyPlanLedger[] PolicyPlans = { new PolicyPlanLedger(), new PolicyPlanLedger() };
        private readonly int[] _needDaySerial = { -1, -1 };
        private readonly FiscalInput[] _fiscalInputs = new FiscalInput[2];
        private readonly FiscalOutput[] _fiscalOutputs = new FiscalOutput[2];
        private readonly DefenseCooldownTable[] _defenseCooldowns = new DefenseCooldownTable[2]
        {
            new DefenseCooldownTable(),
            new DefenseCooldownTable()
        };
        private readonly string[] _defenseIntentSignatures = new string[2];
        public CampaignMapLedger CampaignMap = CampaignMapLedger.Build(null);
        internal SuccessionScheduler Succession = new SuccessionScheduler();
        public Dictionary<int, PersonalityVector> MinorOfficerProfiles = new Dictionary<int, PersonalityVector>();
        internal readonly List<BattleHistoryRecord> BattleHistory = new List<BattleHistoryRecord>();
        internal readonly DirectorMemory[] DirectorMemories = new DirectorMemory[2] { new DirectorMemory(), new DirectorMemory() };
        private readonly OperationDecisionMemory[] OperationMemories = new OperationDecisionMemory[2] { new OperationDecisionMemory(), new OperationDecisionMemory() };
        private DirectorPublishClamp _directorClamp = new DirectorPublishClamp();
        private readonly string[] _directorPostureSignatures = new string[2];
        private readonly CollapseRisk[] _directorRiskLevels = new CollapseRisk[2] { CollapseRisk.Low, CollapseRisk.Low };
        private readonly FiscalStateMemory[] _fiscalMemory = new FiscalStateMemory[2]
        {
            new FiscalStateMemory(),
            new FiscalStateMemory()
        };
        private readonly string[] _frontSignatures = new string[2];
        private readonly string[] _armyAreaSignatures = new string[2];
        private readonly string[] _formationDirectiveSignatures = new string[2];
        private readonly string[] _operationalProbeSignatures = new string[2];
        private readonly OperationalProbeState[] _operationalProbeStates = new OperationalProbeState[2];
        private readonly ProbeHysteresis[] _probeHysteresis = { new ProbeHysteresis(), new ProbeHysteresis() };
        private readonly bool[] _capitalThreatened = new bool[2];
        private readonly int[] _capitalStreakDay = { -1, -1 };
        private readonly int[] _frontChangedDay = { -1, -1 };
        private int _lastReviewSerial = -1;
        private long _saveSequence;
        private readonly string[] _fiscalSignatures = new string[2];
        private readonly string[] _constructionSignatures = new string[2];
        private readonly string[] _formationSourceSignatures = new string[2];
        private readonly string[] _armyAreaSourceSignatures = new string[2];
        private readonly string[] _constructionSourceSignatures = new string[2];
        private string _campaignMapSignature;
        private string _campaignMapDynamicSignature;
        private DailyCadence _operationalCadence = new DailyCadence();
        private bool _erasSeededFromSave;
        internal static readonly CampaignEpochTracker Epoch = new CampaignEpochTracker();
        private bool _operationalRuntimeDeferredLogged;
        private bool _wlCareerStartDeferredLogged;
        private bool _wlJobMachinePauseLogged;
        private bool _dailyOpsAnnounced;
        private readonly bool[] _playerCicAnnounced = new bool[2];

        public int LastSeenMonth = -1;
        public int LastSeenYear  = -1;

        public bool Initialized;

        public static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("WhiskeyRealismStrategicCoordinator");
            Instance = go.AddComponent<StrategicCoordinator>();
            DontDestroyOnLoad(go);
            EmitCampaignInfoToMainLogIfAllowed("[Coordinator] bootstrapped");
        }

        public void InitializeFromGameState()
        {
            try
            {
                RefreshStrategicSettings();
                Eras[0] = Eras[0] ?? new EraStageManager { Stage = EraStage.Amateur1861 };
                Eras[1] = Eras[1] ?? new EraStageManager { Stage = EraStage.Amateur1861 };

                int playerAlliance = ResolvePlayerAlliance();
                bool factionListReady = OperationalRuntimeReady();
                for (int alliance = 0; alliance < 2; alliance++)
                {
                    if (!ShouldRunCicFor(alliance, playerAlliance, factionListReady))
                    {
                        CICs[alliance] = null;
                        continue;
                    }
                    if (CICs[alliance] == null)
                        CICs[alliance] = BuildCICForAlliance(alliance);
                }
                Initialized = true;
                EmitCampaignInfoToMainLogIfAllowed($"[Coordinator] initialized (playerAlliance={playerAlliance})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Coordinator] init failed: " + ex);
            }
        }

        // R15/H3: clears ALL per-campaign state. Called at the start of the AICampaign.Load
        // Postfix (before a sidecar is applied, or when a save has none) and when a new
        // campaign scene appears without a load (CampaignEpochTracker via MonthlyTickHookPatch).
        // The singleton survives scene changes (DontDestroyOnLoad), so without this the previous
        // campaign's plans, era, battle history and succession ids leaked into the next one.
        public void ResetAll(string reason)
        {
            for (int a = 0; a < 2; a++)
            {
                CICs[a] = null;
                Eras[a] = new EraStageManager { Stage = EraStage.Amateur1861 };
                Fronts[a] = null;
                ArmyAreas[a] = null;
                FormationDirectives[a] = null;
                OperationalProbes[a] = null;
                FiscalIntents[a] = null;
                Needs[a] = null;
                NeedSnapshots[a] = null;
                PolicyPlans[a] = new PolicyPlanLedger();
                _needDaySerial[a] = -1;
                _fiscalInputs[a] = null;
                _fiscalOutputs[a] = null;
                ConstructionIntents[a] = null;
                DefenseIntents[a] = null;
                _defenseCooldowns[a] = new DefenseCooldownTable();
                _defenseIntentSignatures[a] = null;
                DirectorMemories[a] = new DirectorMemory();
                OperationMemories[a] = new OperationDecisionMemory();
                _directorPostureSignatures[a] = null;
                _directorRiskLevels[a] = CollapseRisk.Low;
                _fiscalMemory[a] = new FiscalStateMemory();
                _frontSignatures[a] = null;
                _armyAreaSignatures[a] = null;
                _formationDirectiveSignatures[a] = null;
                _operationalProbeSignatures[a] = null;
                _operationalProbeStates[a] = null;
                _probeHysteresis[a] = new ProbeHysteresis();
                _capitalThreatened[a] = false;
                _capitalStreakDay[a] = -1;
                _frontChangedDay[a] = -1;
                _fiscalSignatures[a] = null;
                _constructionSignatures[a] = null;
                _formationSourceSignatures[a] = null;
                _armyAreaSourceSignatures[a] = null;
                _constructionSourceSignatures[a] = null;
                _playerCicAnnounced[a] = false;
            }
            CampaignMap = CampaignMapLedger.Build(null);
            ConstructionTelemetry = new ConstructionTelemetry();
            Succession = new SuccessionScheduler();
            MinorOfficerProfiles.Clear();
            BattleHistory.Clear();
            _directorClamp = new DirectorPublishClamp();
            _campaignMapSignature = null;
            _campaignMapDynamicSignature = null;
            _operationalCadence = new DailyCadence();
            _operationalRuntimeDeferredLogged = false;
            _wlCareerStartDeferredLogged = false;
            _wlJobMachinePauseLogged = false;
            _dailyOpsAnnounced = false;
            _forcedAllSuccession = false;
            _erasSeededFromSave = false;
            _lastReviewSerial = -1;
            _saveSequence = 0;
            _lastSnapshot = default(WarStateObserver.Snapshot);
            _lastSnapshotDay = _lastSnapshotMonth = _lastSnapshotYear = -1;
            _loggedVicksburg = _loggedChattanooga = _loggedAtlanta = false;
            LastSeenMonth = -1;
            LastSeenYear = -1;
            Initialized = false;
            CampaignScope.ResetAll();
            EmitCampaignInfoToMainLogIfAllowed("[Coordinator] strategic state reset (" + reason + ")");
        }

        private CIC BuildCICForAlliance(int allianceId)
        {
            var cic = new CIC { AllianceId = allianceId };
            if (allianceId == 1)
            {
                cic.OfficerName = "Davis";
                cic.OfficerPersonality = new PersonalityVector(-0.1f, +0.3f, -0.3f, -0.3f, +0.5f);
            }
            else
            {
                cic.OfficerName = "Lincoln";
                cic.OfficerPersonality = new PersonalityVector(+0.3f, +0.1f, +0.1f, +0.4f, +0.7f);
            }
            return cic;
        }

        public void NotifyDateAdvanced(int gameDay, int gameMonth, int gameYear)
        {
            if (!Initialized) InitializeFromGameState();

            bool firstCall = (LastSeenMonth < 0);
            bool rollover  = !firstCall && ((gameMonth != LastSeenMonth) || (gameYear != LastSeenYear));
            if (firstCall && !_erasSeededFromSave)
            {
                // Fresh campaign state (reset or no sidecar era): derive the era from the date.
                for (int a = 0; a < Eras.Length; a++)
                    (Eras[a] ?? (Eras[a] = new EraStageManager())).ResetToDate(gameMonth, gameYear);
            }

            // Fire on first valid call (campaign just started / save just loaded)
            // AND on every subsequent month rollover. The first heartbeat gives
            // smoke-testers immediate confirmation the strategic core is running
            // without having to advance game-time past a month boundary first.
            bool ranMonthly = false;
            if (firstCall || rollover)
            {
                LastSeenMonth = gameMonth;
                LastSeenYear  = gameYear;
                // Run the monthly review on the real day of the rollover (fast-forward can
                // land past the 1st); hardcoding day=1 broke every day-keyed gate.
                OnMonthlyTick(gameDay, gameMonth, gameYear);
                ranMonthly = true;
            }

            if (_operationalCadence.ShouldFire(gameDay, gameMonth, gameYear) && !ranMonthly)
                OnDailyOperationalTick(gameDay, gameMonth, gameYear);
        }

        private bool _forcedAllSuccession;

        public void OnDailyOperationalTick(int day, int month, int year)
        {
            try
            {
                if (!_dailyOpsAnnounced)
                {
                    _dailyOpsAnnounced = true;
                    EmitCampaignInfo("[DailyOps] active=true");
                }

                RunStrategicReview(day, month, year, logHeartbeat: false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DailyOps] tick failed: " + ex.Message);
            }
        }

        private static void ForceChapterUpdate()
        {
            try
            {
                var policyType = AccessTools.TypeByName("Policy");
                if (policyType == null) return;
                var m = AccessTools.Method(policyType, "CheckForChapterUpdate");
                m?.Invoke(null, null);
            }
            catch { /* tolerate — CIC.diag will surface CurrentChapter if still wrong */ }
        }

        private static int SafePolicyChapter()
        {
            try { return Policy.CurrentChapter; }
            catch { return -1; }
        }

        public void OnMonthlyTick(int day, int month, int year)
        {
            try
            {
                // Vanilla's Policy.CheckForChapterUpdate() runs from a per-day cycle
                // and sets Policy.CurrentChapter (initial value -1). For scenario "002"
                // (Whiskey & Lemons) it unconditionally sets CurrentChapter = 1. Our
                // OnMonthlyTick can fire BEFORE vanilla's per-day cycle has run on a
                // fresh campaign, so CurrentChapter is still -1 — which deactivates
                // every CampaignObjective (their ObjectiveChapters lists don't contain
                // -1). Call it ourselves to make the chapter current before we read
                // objective state.
                ForceChapterUpdate();

                // Test-mode: bypass gates and fire every event on first tick.
                if (Plugin.Instance.ForceAllSuccessionEvents.Value && !_forcedAllSuccession)
                {
                    _forcedAllSuccession = true;
                    Succession.ForceAllFired();
                    Plugin.Log.LogWarning("[TestMode] Force All Succession Events is ON — all 12 events marked fired this tick. Disable in BepInEx config before a real playthrough.");
                }

                RunStrategicReview(day, month, year, logHeartbeat: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Coordinator] tick failed: " + ex);
            }
        }

        private void RunStrategicReview(int day, int month, int year, bool logHeartbeat)
        {
            using (TelemetryPerf.Scope("campaign.daily-review", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
                RunStrategicReviewCore(day, month, year, logHeartbeat);
        }

        private void RunStrategicReviewCore(int day, int month, int year, bool logHeartbeat)
        {
            var totalWatch = Stopwatch.StartNew();
            double mapMs = 0d;
            double frontMs = 0d;
            double armyAreaMs = 0d;
            double formationMs = 0d;
            double probeMs = 0d;
            double fiscalMs = 0d;
            double policyMs = 0d;
            double constructionMs = 0d;
            double defenseMs = 0d;
            double plannerMs = 0d;

            // Stage G spec C5: only a career whose commander is not chosen yet defers the whole
            // review. Between commands vanilla's job machine is paused (UpdateUnitAI 11368) and the
            // review narrows below instead.
            if (WlCareerNotStarted())
            {
                if (!_wlCareerStartDeferredLogged)
                {
                    _wlCareerStartDeferredLogged = true;
                    EmitCampaignInfoToMainLogIfAllowed("[Coordinator] W&L career start selection pending; strategic review deferred until the player picks a commander");
                }
                return;
            }
            _wlCareerStartDeferredLogged = false;

            // While vanilla's 11368 return freezes every faction's job machine, the ledgers that
            // issue moves or mirror job-machine work (succession swaps and the CIC/probe path,
            // front, formation directives, army areas, defense intent) and the planner's executors
            // pause for both alliances; the state-only ledgers (era, capital threat, director
            // posture, fiscal, policy, construction) and the planner's bookkeeping keep running.
            bool jobMachinePaused = WlVanillaJobMachinePaused();
            if (jobMachinePaused && !_wlJobMachinePauseLogged)
            {
                _wlJobMachinePauseLogged = true;
                EmitCampaignInfoToMainLogIfAllowed("[Coordinator] W&L player has no command (hierarchy -1): planner executors and order-issuing ledgers paused for every alliance, as vanilla's job machine");
            }
            else if (!jobMachinePaused)
            {
                _wlJobMachinePauseLogged = false;
            }

            RefreshStrategicSettings();
            int reviewSerial = ReviewCadenceCatchUp.DaySerial(day, month, year);
            EnemyDetectionRuntime.StampReviewDay(reviewSerial);
            bool weeklyCatchUp = ReviewCadenceCatchUp.SkippedWeeklyGate(_lastReviewSerial, reviewSerial);
            bool alternatingCatchUp = ReviewCadenceCatchUp.SkippedAlternatingGate(_lastReviewSerial, reviewSerial);
            _lastReviewSerial = reviewSerial;
            int playerAlliance = ResolvePlayerAlliance();
            bool operationalRuntimeReady = OperationalRuntimeReady();
            if (!operationalRuntimeReady && !_operationalRuntimeDeferredLogged)
            {
                _operationalRuntimeDeferredLogged = true;
                EmitCampaignInfoToMainLogIfAllowed("[Coordinator] operational ledgers deferred until AICampaign factions initialize");
            }

            if (operationalRuntimeReady)
                mapMs += Measure(() => UpdateCampaignMapLedger(day, logHeartbeat, weeklyCatchUp));

            for (int alliance = 0; alliance < 2; alliance++)
            {
                if (!ShouldRunCicFor(alliance, playerAlliance, operationalRuntimeReady))
                {
                    if (!_playerCicAnnounced[alliance])
                    {
                        _playerCicAnnounced[alliance] = true;
                        EmitCampaignInfoToMainLogIfAllowed(
                            $"alliance {alliance} ({(alliance == 1 ? "CSA" : "Union")}) is not run by vanilla campaign AI (player-controlled) — mod stands down for that faction");
                    }
                    CICs[alliance] = null;
                    // User decision 2026-10-15: a W&L Commander-in-Chief player's alliance gets the
                    // need-driven policy picks too (policies have no CIC exemption in vanilla,
                    // 211019). Its fiscal posture is computed for the needs only, never published.
                    if (operationalRuntimeReady && IsPlayerCICOf(alliance, playerAlliance))
                    {
                        var cicEra = Eras[alliance];
                        var cicWs = ObserveWarStateCached(day, month, year);
                        cicEra.CheckTransition(month, year, cicWs.VicksburgFallen, cicWs.AtlantaThreatened);
                        fiscalMs += Measure(() => UpdateFiscalIntent(alliance, cicEra.Stage, day, month, year, logHeartbeat, publish: false));
                        policyMs += Measure(() => UpdatePolicyPlan(alliance, cicEra.Stage, reviewSerial));
                        // G1: detection is bookkeeping; the planner itself stands down for this alliance.
                        plannerMs += Measure(() => CampaignPlannerRuntime.ReviewPlan(alliance, day, month, year, DirectorMemories[alliance]?.LastPosture));
                    }
                    continue;
                }
                if (CICs[alliance] == null) CICs[alliance] = BuildCICForAlliance(alliance);

                var era = Eras[alliance];
                var ws = ObserveWarStateCached(day, month, year);
                era.CheckTransition(month, year, ws.VicksburgFallen, ws.AtlantaThreatened);

                if (!jobMachinePaused)
                {
                    var fired = Succession.CheckEvents(BuildSchedulerView(month, year, alliance, ws));
                    foreach (var e in fired)
                    {
                        if (e.AllianceId != alliance) continue;
                        SwapOfficer(alliance, e);
                        if (CICs[alliance].ActivePlan != null)
                            CICs[alliance].ActivePlan.IsDirty = true;
                    }
                }

                var cic = CICs[alliance];
                int daySerial = year * 372 + month * 31 + day;
                if (operationalRuntimeReady)
                {
                    UpdateCapitalThreat(alliance, daySerial);
                    PublishDirectorPosture(alliance, cic, era, day, month, year, daySerial);
                }

                if (!jobMachinePaused)
                {
                    var phaseTruth = BuildPhaseTruth(cic, alliance, day, month, year);
                    if (!cic.ReviewPlanWithTruth(day, month, year, phaseTruth))
                    {
                        string beforeOperation = cic.ActivePlan?.OperationId;
                        cic.Replan(
                            era,
                            month,
                            year,
                            daySerial,
                            SafePolicyChapter(),
                            alliance < DirectorMemories.Length ? DirectorMemories[alliance].LastPosture : null,
                            null);
                        string afterOperation = cic.ActivePlan?.OperationId;
                        if (!string.IsNullOrEmpty(afterOperation) && afterOperation != beforeOperation)
                            OperationMemories[alliance].RecordReplan(daySerial);
                    }
                }

                fiscalMs += Measure(() => UpdateFiscalIntent(alliance, era.Stage, day, month, year, logHeartbeat, publish: true));
                if (operationalRuntimeReady)
                    policyMs += Measure(() => UpdatePolicyPlan(alliance, era.Stage, reviewSerial));
                if (operationalRuntimeReady)
                    plannerMs += Measure(() => CampaignPlannerRuntime.ReviewPlan(alliance, day, month, year, DirectorMemories[alliance]?.LastPosture));
                bool frontChanged = false;
                if (!jobMachinePaused)
                {
                    if (operationalRuntimeReady)
                        frontMs += Measure(() => frontChanged = UpdateFrontLedger(alliance, cic));
                    if (frontChanged || _frontChangedDay[alliance] < 0)
                        _frontChangedDay[alliance] = daySerial;

                    defenseMs += Measure(() => UpdateDefenseIntent(alliance, era.Stage, day, month, year));
                    if (operationalRuntimeReady)
                        plannerMs += Measure(() => CampaignPlannerRuntime.ReviewExecute(alliance));
                }

                bool formationChanged = false;
                if (operationalRuntimeReady && !jobMachinePaused)
                {
                    string formationSource = FormationSourceSignature(alliance, cic);
                    bool runFormation = StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day, alliance, alternatingCatchUp) ||
                        StrategicCadencePolicy.SourceChanged(formationSource, _formationSourceSignatures[alliance]) ||
                        frontChanged;
                    if (runFormation)
                    {
                        formationMs += Measure(() => formationChanged = UpdateFormationDirectiveLedger(alliance, cic, era));
                        _formationSourceSignatures[alliance] = formationSource;
                    }

                    probeMs += Measure(() =>
                    {
                        if (UpdateOperationalProbe(alliance, cic, day, month, year))
                            formationChanged = true;
                    });

                    string armyAreaSource = ArmyAreaSourceSignature(alliance, cic);
                    if (StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(day, armyAreaSource, _armyAreaSourceSignatures[alliance], formationChanged || weeklyCatchUp))
                    {
                        armyAreaMs += Measure(() => UpdateArmyAreaLedger(alliance, cic));
                        _armyAreaSourceSignatures[alliance] = armyAreaSource;
                    }

                }

                string constructionSource = ConstructionSourceSignature(alliance);
                if (StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(day, constructionSource, _constructionSourceSignatures[alliance], weeklyCatchUp))
                {
                    constructionMs += Measure(() => UpdateConstructionIntent(alliance, era.Stage, logHeartbeat, jobMachinePaused));
                    _constructionSourceSignatures[alliance] = constructionSource;
                }

                if (Plugin.Instance.VerboseLogging.Value && !logHeartbeat)
                    EmitCampaignInfo($"[DailyOps] {year}-{month:D2}-{day:D2} alliance={alliance}");

                if (logHeartbeat)
                {
                    EmitCampaignInfo(
                        $"[Heartbeat] {year}-{month:D2} alliance={alliance} " +
                        $"era={era.Stage} cic={cic.OfficerName ?? "<none>"} " +
                        $"plan={(cic.ActivePlan == null ? "<none>" : $"phase{cic.ActivePlan.CurrentPhaseIndex + 1}/{cic.ActivePlan.Phases.Count} obj={cic.ActivePlan.CurrentPhase?.TargetObjectiveId}")} " +
                        $"succession_fired={Succession.FiredEventIds.Count}");
                }
            }

            totalWatch.Stop();
            if (ShouldLogStrategicPerf(totalWatch.Elapsed.TotalMilliseconds))
            {
                EmitCampaignInfo(
                    $"[DailyOps:Perf] {year}-{month:D2}-{day:D2} " +
                    $"totalMs={totalWatch.Elapsed.TotalMilliseconds:F2} " +
                    $"mapMs={mapMs:F2} frontMs={frontMs:F2} armyAreaMs={armyAreaMs:F2} " +
                    $"formationMs={formationMs:F2} probeMs={probeMs:F2} fiscalMs={fiscalMs:F2} policyMs={policyMs:F2} " +
                    $"constructionMs={constructionMs:F2} defenseMs={defenseMs:F2} plannerMs={plannerMs:F2}");
            }
        }

        private void PublishDirectorPosture(int alliance, CIC cic, EraStageManager era, int day, int month, int year, int daySerial)
        {
            var paceInput  = BuildCampaignPaceInput(alliance, day, month, year);
            var paceOutput = CampaignPaceLedger.Build(paceInput);
            var personality = cic.Effective(era);
            var newPosture  = StrategicResilienceDirector.ProposePosture(alliance, paceOutput, personality);
            newPosture.SourceSignature = DirectorSourceSignature(alliance);

            // At most one full refresh per alliance per game day (R16).
            if (_directorClamp.TryPublish(alliance, daySerial))
            {
                DirectorMemories[alliance].LastPosture          = newPosture;
                DirectorMemories[alliance].LastFullRefreshDay   = daySerial;
                DirectorMemories[alliance].LastSourceSignature  = newPosture.SourceSignature;

                string previousSig = _directorPostureSignatures[alliance];
                string newSig      = newPosture.Pace + "/" + newPosture.Intent + "/" + newPosture.Risk;
                if (newSig != previousSig)
                {
                    EmitCampaignInfo(
                        $"[CampaignPace] alliance={alliance} pace={newPosture.Pace}" +
                        $" intent={newPosture.Intent} risk={newPosture.Risk}" +
                        $" reason={newPosture.Reason}");
                    _directorPostureSignatures[alliance] = newSig;
                }
                var prevRisk = _directorRiskLevels[alliance];
                if (newPosture.Risk != prevRisk)
                {
                    EmitCampaignInfo($"[CollapseRisk] alliance={alliance} risk={newPosture.Risk} pace={newPosture.Pace}");
                    _directorRiskLevels[alliance] = newPosture.Risk;
                }
                if (Plugin.Instance.DirectorVerboseTrace.Value)
                {
                    EmitDirectorTrace(alliance, day, month, year, newPosture);
                }
            }
            else
            {
                newPosture.Stale = true;
                DirectorMemories[alliance].LastPosture =
                    DirectorMemories[alliance].LastPosture ?? newPosture;
            }
        }

        // Copies the Plugin config values pure strategic types read (CIC, SuccessionScheduler)
        // into their BepInEx-free settings holders.
        private static void RefreshStrategicSettings()
        {
            var plugin = Plugin.Instance;
            if (plugin == null) return;
            try
            {
                CicSettings.OverrideVanillaSettings = ConfigValue(plugin.OverrideVanillaSettings);
                CicSettings.LockedDifficulty = plugin.LockedDifficulty != null
                    ? plugin.LockedDifficulty.Value
                    : DifficultyPersonalityModifier.HistoricalHardDifficultyIndex;
                CicSettings.HistoricalOperationDoctrine = ConfigValue(plugin.EnableHistoricalOperationDoctrine, defaultValue: true);
                CicSettings.PlanTrace = ConfigValue(plugin.PlanTrace);
                SuccessionScheduler.TraceEnabled = ConfigValue(plugin.SuccessionTrace);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinator:settings-refresh", "[Coordinator] settings refresh failed: " + ex.Message);
            }
        }

        private static double Measure(Action action)
        {
            var watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds;
        }

        // Builds a PhaseTruthInput from live game state and evaluates it.
        // Returns null when the plan or phase is absent (ReviewPlanWithTruth falls back to
        // the deadline-only baseline on null).
        // Force inputs are LOCAL (R3): own/enemy campaign strength within the near-target
        // radius around the phase objective, not the whole-theater sector totals.
        private PhaseTruthOutput BuildPhaseTruth(CIC cic, int alliance, int day, int month, int year)
        {
            if (cic?.ActivePlan?.CurrentPhase == null) return null;
            var phase = cic.ActivePlan.CurrentPhase;
            var fronts = alliance < Fronts.Length ? Fronts[alliance] : null;
            var targetPos = ObjectiveAdapter.ResolveObjectivePosition(phase.TargetObjectiveId);
            int daySerial = year * 372 + month * 31 + day;

            HistoricalOperationProfile profile = null;
            if (!string.IsNullOrEmpty(cic.ActivePlan.OperationId))
                HistoricalOperationCatalog.TryGetById(cic.ActivePlan.OperationId, out profile);
            float radius = HistoricalOperationContextBuilder.NearTargetRadius(profile);

            bool engagedRecently = false;
            int lastEngagementDay = -1;
            float localOwn = 0f, localEnemy = 0f;
            if (targetPos.HasValue)
            {
                foreach (var battle in BattleHistoryQuery.Near(BattleHistory, targetPos.Value, radius, daySerial, 14))
                {
                    if (battle == null || !battle.IsLandBattle) continue;
                    engagedRecently = true;
                    int battleDay = battle.Year * 372 + battle.Month * 31 + battle.Day;
                    if (battleDay > lastEngagementDay) lastEngagementDay = battleDay;
                }
                ReadLocalForceBalance(alliance, targetPos.Value, radius, out localOwn, out localEnemy);
            }

            var input = new PhaseTruthInput
            {
                Plan                          = cic.ActivePlan,
                AllianceId                    = alliance,
                DaySerial                     = daySerial,
                TargetAccomplished            = ObjectiveAdapter.IsAccomplished(phase.TargetObjectiveId),
                ObjectiveAvailable            = ObjectiveAdapter.IsAvailable(phase.TargetObjectiveId, alliance),
                TargetPositionResolves        = targetPos.HasValue,
                TargetEngagedRecently         = engagedRecently,
                LastTargetEngagementDaySerial = lastEngagementDay,
                TargetLocalOwnStrength       = localOwn,
                TargetLocalEnemyStrength     = localEnemy,
                CurrentMonth                  = month,
                CurrentYear                   = year
            };
            if (profile != null)
            {
                input.OperationProfile = profile;
                input.OperationContext = HistoricalOperationContextBuilder.Build(
                    alliance,
                    daySerial,
                    phase.TargetObjectiveId,
                    cic.ActivePlan,
                    fronts,
                    fronts != null ? TheaterPressureView.From(fronts) : null,
                    alliance < DirectorMemories.Length ? DirectorMemories[alliance].LastPosture : null,
                    alliance < OperationMemories.Length ? OperationMemories[alliance] : null,
                    BattleHistory,
                    new HistoricalOperationLocalSignals
                    {
                        HasTarget = targetPos.HasValue,
                        TargetX = targetPos.HasValue ? targetPos.Value.x : 0f,
                        TargetZ = targetPos.HasValue ? targetPos.Value.z : 0f,
                        TargetTheater = targetPos.HasValue ? CampaignMapTheaterRuntime.FromPosition(targetPos.Value) : Theater.Unknown,
                        LocalOwnStrength = localOwn,
                        LocalEnemyStrength = localEnemy,
                        EnemyThreatensCapital = alliance < _capitalThreatened.Length && _capitalThreatened[alliance]
                    });
            }
            return PhaseTruthLedger.Evaluate(input);
        }

        // C-HardWired: capital threat per vanilla AssignUnitToDefendCapital's need score
        // (decompile 11668-11760) and the consecutive-day streak the pace ledger reads. The enemy
        // side is the fog-honest belief picture (G1; read before today's ReviewPlan: P1's one-day lag).
        private void UpdateCapitalThreat(int alliance, int daySerial)
        {
            bool threatened = false;
            try
            {
                var capital = GameVars.alliance != null && alliance < GameVars.alliance.Length
                    ? GameVars.alliance[alliance].capital
                    : null;
                int index = ResolveAifactionIndexForAlliance(alliance);
                var faction = index >= 0 ? AICampaignReflect.GetFaction(index) : null;
                if (capital != null && faction != null)
                {
                    Vector3 center = capital.transform.position;
                    var enemies = new List<CapitalForceSample>();
                    var own = new List<CapitalForceSample>();
                    EnemyBeliefReadings.CapitalSamples(EnemyDetectionRuntime.BeliefsFor(alliance), center.x, center.z, enemies);
                    var ownUnits = VanillaFieldCache.List(faction, "ownunits");
                    if (ownUnits != null)
                    {
                        for (int i = 0; i < ownUnits.Count; i++)
                        {
                            if (!(ownUnits[i] is Regiment r) || r == null) continue;
                            own.Add(new CapitalForceSample { Distance = XZDistance(r.GetLastWaypointPos(), center), Strength = r.groupstrengthactive, Morale = r.groupmorale });
                        }
                    }
                    threatened = CampaignResponsivenessSignals.EnemyThreatensCapital(
                        enemies,
                        own,
                        GamePrefs.aimaximumdistancetosearchforunitrelocations,
                        GamePrefs.aiminimumstrengthformovement);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinator:capital-threat", "[Coordinator] capital threat read failed: " + ex.Message);
                threatened = false;
            }

            _capitalThreatened[alliance] = threatened;
            var memory = DirectorMemories[alliance] ?? (DirectorMemories[alliance] = new DirectorMemory());
            memory.CapitalDangerStreakDays = CampaignResponsivenessSignals.NextCapitalStreak(
                memory.CapitalDangerStreakDays,
                threatened,
                _capitalStreakDay[alliance],
                daySerial,
                out _capitalStreakDay[alliance]);
        }

        private static float XZDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // Sums own / enemy campaign strength (groupstrengthactive, same field FrontSectorRuntime
        // reads) for units of this alliance's AIFaction ownunits/enemyunits lists within
        // `radius` of `center`. Zeroes on any reflection failure.
        private static void ReadLocalForceBalance(int alliance, Vector3 center, float radius, out float own, out float enemy)
        {
            own = 0f;
            enemy = 0f;
            try
            {
                int index = ResolveAifactionIndexForAlliance(alliance);
                if (index < 0) return;
                var faction = AICampaignReflect.GetFaction(index);
                if (faction == null) return;
                var factionType = faction.GetType();
                if (_ownUnitsField == null || _ownUnitsField.DeclaringType != factionType)
                {
                    _ownUnitsField = AccessTools.Field(factionType, "ownunits");
                    _enemyUnitsField = AccessTools.Field(factionType, "enemyunits");
                }
                own = SumStrengthWithin(_ownUnitsField?.GetValue(faction) as IList, center, radius);
                enemy = SumStrengthWithin(_enemyUnitsField?.GetValue(faction) as IList, center, radius);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinator:local-force", "[Coordinator] local force read failed: " + ex.Message);
                own = 0f;
                enemy = 0f;
            }
        }

        private static float SumStrengthWithin(IList units, Vector3 center, float radius)
        {
            if (units == null) return 0f;
            float radiusSq = radius * radius;
            float total = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                var pos = unit.transform.position;
                float dx = pos.x - center.x;
                float dz = pos.z - center.z;
                if (dx * dx + dz * dz > radiusSq) continue;
                total += Math.Max(0f, unit.groupstrengthactive);
            }
            return total;
        }

        private static FieldInfo _ownUnitsField;
        private static FieldInfo _enemyUnitsField;

        private static bool ShouldLogStrategicPerf(double elapsedMs)
        {
            var plugin = Plugin.Instance;
            if (plugin == null) return false;
            if (ConfigValue(plugin.VerboseLogging)) return true;
            float threshold = ConfigValue(plugin.FastForwardAiSlowFrameThresholdMs, 8f);
            if (float.IsNaN(threshold) || float.IsInfinity(threshold) || threshold <= 0f)
                threshold = 8f;
            return elapsedMs >= threshold;
        }

        private void UpdateCampaignMapLedger(int day, bool logHeartbeat, bool weeklyCatchUp)
        {
            try
            {
                string dynamicSignature = CampaignMapRuntime.DynamicSignature();
                bool fullRefresh = CampaignMap == null ||
                    CampaignMap.Towns.Count == 0 ||
                    StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(
                        day,
                        dynamicSignature,
                        _campaignMapDynamicSignature,
                        forceRefresh: weeklyCatchUp);
                if (!fullRefresh) return;

                var ledger = CampaignMapRuntime.Build();
                CampaignMap = ledger;
                _campaignMapDynamicSignature = dynamicSignature;
                if (ledger.Signature != _campaignMapSignature)
                {
                    EmitCampaignInfo("[CampaignMap] " + ledger.Summary());
                    _campaignMapSignature = ledger.Signature;
                }
                else if (logHeartbeat && Plugin.Instance != null && Plugin.Instance.VerboseLogging.Value)
                {
                    EmitCampaignInfo("[CampaignMap] " + ledger.Summary());
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-map:update", "[CampaignMap] update failed: " + ex.Message);
            }
        }

        private void UpdateConstructionIntent(int alliance, EraStage era, bool logHeartbeat, bool jobMachinePaused)
        {
            using (TelemetryPerf.Scope("campaign.construction", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
                UpdateConstructionIntentCore(alliance, era, logHeartbeat, jobMachinePaused);
        }

        private void UpdateConstructionIntentCore(int alliance, EraStage era, bool logHeartbeat, bool jobMachinePaused)
        {
            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null || !ConfigValue(plugin.EnableConstructionIntentLedger))
                    return;
                if (alliance < 0 || alliance >= ConstructionIntents.Length || alliance >= _constructionSignatures.Length)
                    return;

                var fiscal = alliance < FiscalIntents.Length ? FiscalIntents[alliance] : null;
                var front = alliance < Fronts.Length ? Fronts[alliance] : null;
                var formation = alliance < FormationDirectives.Length ? FormationDirectives[alliance] : null;
                var input = ConstructionRuntime.BuildInput(alliance, era, fiscal, front, formation);
                var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
                ConstructionIntents[alliance] = output;

                if (ConfigValue(plugin.VerboseLogging) ||
                    ConfigValue(plugin.ConstructionVerboseLogging) ||
                    _constructionSignatures[alliance] != output.Signature)
                {
                    EmitConstructionIntent(alliance, era, output);
                    _constructionSignatures[alliance] = output.Signature;
                }

                if (logHeartbeat && ConfigValue(plugin.ConstructionTelemetryEnabled, defaultValue: true))
                {
                    var telemetry = ConstructionTelemetry ?? (ConstructionTelemetry = new ConstructionTelemetry());
                    EmitConstructionTelemetry(alliance, output, telemetry.Summary(alliance));
                }

                // A telegraph start places a vanilla construction wish (TelegraphConstructionRuntime,
                // CBuilding.AddConstructionWish 97479): an executor write, so it waits while vanilla's
                // job machine is paused (11368, spec C5). The ledger above keeps its bookkeeping.
                if (!jobMachinePaused && ConfigValue(plugin.EnableTelegraphAI))
                    TelegraphConstructionRuntime.TryStartTelegraph(alliance, output);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "construction-intent:update:" + alliance,
                    "[ConstructionIntent] update failed: " + ex.Message);
            }
        }

        private void UpdateDefenseIntent(int alliance, EraStage era, int day, int month, int year)
        {
            using (TelemetryPerf.Scope("campaign.defense-intent", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
                UpdateDefenseIntentCore(alliance, era, day, month, year);
        }

        private void UpdateDefenseIntentCore(int alliance, EraStage era, int day, int month, int year)
        {
            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null || !ConfigValue(plugin.EnableDefenseIntentLedger, defaultValue: true)) return;
                if (alliance < 0 || alliance >= DefenseIntents.Length) return;

                // Tick the cooldown table once per daily review for this alliance.
                _defenseCooldowns[alliance].Tick();

                float total = ComputeAllianceEffectiveStrength(alliance);
                var profile = ResolveGrandStrategyProfile(alliance, era);

                var input = DefenseIntentRuntime.BuildInput(
                    allianceId: alliance,
                    cic: CICs[alliance],
                    era: Eras[alliance],
                    map: CampaignMap,
                    cooldown: _defenseCooldowns[alliance],
                    totalAllianceEffectiveStrength: total,
                    profile: profile);

                var output = DefenseIntentLedger.Build(input);
                DefenseIntents[alliance] = output;

                bool verbose = ConfigValue(plugin.VerboseLogging) || ConfigValue(plugin.DefenseIntentVerboseLogging);
                if (verbose)
                {
                    if (output.Responses != null)
                    {
                        foreach (var r in output.Responses)
                        {
                            EmitDefenseIntentDetail(alliance, day, month, year, r);
                        }
                    }
                    _defenseIntentSignatures[alliance] = output.Signature;
                }
                else if (_defenseIntentSignatures[alliance] != output.Signature)
                {
                    EmitDefenseIntentSummary(alliance, day, month, year, output);
                    _defenseIntentSignatures[alliance] = output.Signature;
                }

                // Issue custom defensive movement orders for active landings and
                // relaxed-filter guard candidates vanilla won't act on.
                CoastalDefenseCustomOrderRunner.Run(alliance, output);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("defense-intent:update:" + alliance,
                    "[DefenseIntent] update failed: " + ex.Message);
            }
        }

        private float ComputeAllianceEffectiveStrength(int alliance)
        {
            try
            {
                int aifactionIndex = ResolveAifactionIndexForAlliance(alliance);
                if (aifactionIndex < 0) return 0f;
                var faction = AICampaignReflect.GetFaction(aifactionIndex);
                if (faction == null) return 0f;
                var ownUnitsField = AccessTools.Field(faction.GetType(), "ownunits");
                var ownUnits = ownUnitsField?.GetValue(faction) as IList;
                if (ownUnits == null) return 0f;
                float total = 0f;
                for (int i = 0; i < ownUnits.Count; i++)
                {
                    var u = ownUnits[i];
                    if (u == null) continue;
                    int strength = 0;
                    float morale = 1f;
                    try
                    {
                        var strengthField = AccessTools.Field(u.GetType(), "groupstrengthactive");
                        if (strengthField != null) strength = Convert.ToInt32(strengthField.GetValue(u));
                        var moraleField = AccessTools.Field(u.GetType(), "groupmorale");
                        if (moraleField != null) morale = Convert.ToSingle(moraleField.GetValue(u));
                    }
                    catch { }
                    float effectiveMorale = morale > 0.25f ? morale : 0.25f;
                    total += (float)strength * effectiveMorale;
                }
                return total;
            }
            catch { return 0f; }
        }

        private static int ResolveAifactionIndexForAlliance(int allianceId)
        {
            try
            {
                var aicType = AccessTools.TypeByName("AICampaign");
                var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
                if (list == null) return -1;
                for (int i = 0; i < list.Count; i++)
                {
                    int aid = AICampaignReflect.GetAllianceId(i);
                    if (aid == allianceId) return i;
                }
            }
            catch { }
            return -1;
        }

        private static GrandStrategyProfile ResolveGrandStrategyProfile(int alliance, EraStage era)
        {
            try { return GrandStrategyRegistry.Resolve(alliance, era); }
            catch { return null; }
        }

        // publish = false (a W&L CIC player's alliance): the posture feeds the policy needs
        // only; FiscalIntents (read by the fiscal, recruitment and construction patches) and the
        // fiscal telemetry stay untouched.
        private void UpdateFiscalIntent(int alliance, EraStage era, int day, int month, int year, bool logHeartbeat, bool publish)
        {
            using (TelemetryPerf.Scope("campaign.fiscal", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
                UpdateFiscalIntentCore(alliance, era, day, month, year, logHeartbeat, publish);
        }

        private void UpdateFiscalIntentCore(int alliance, EraStage era, int day, int month, int year, bool logHeartbeat, bool publish)
        {
            var input = FiscalRuntime.BuildInput(alliance, era, _fiscalMemory[alliance]);
            _fiscalInputs[alliance] = input;
            var options = new FiscalOptions();
            var output = FiscalIntentLedger.Compute(input, options);
            _fiscalOutputs[alliance] = output;
            if (publish) FiscalIntents[alliance] = output;
            bool emergencyRecoveryStable = FiscalIntentLedger.IsEmergencyRecoveryStable(input, options);
            if (output.Posture == FiscalPosture.EmergencySolvency)
            {
                _fiscalMemory[alliance].StableTicksAboveEmergency = 0;
            }
            else if ((_fiscalMemory[alliance].EmergencyResidue ||
                _fiscalMemory[alliance].PreviousPosture == FiscalPosture.CreditDefense) && emergencyRecoveryStable)
            {
                _fiscalMemory[alliance].StableTicksAboveEmergency++;
            }
            else if (!emergencyRecoveryStable)
            {
                _fiscalMemory[alliance].StableTicksAboveEmergency = 0;
            }

            _fiscalMemory[alliance].PreviousPosture = output.Posture;
            _fiscalMemory[alliance].EmergencyResidue = output.Posture == FiscalPosture.EmergencySolvency ||
                _fiscalMemory[alliance].EmergencyResidue && output.Posture == FiscalPosture.CreditDefense;

            if (!publish) return;

            if (Plugin.Instance.VerboseLogging.Value || Plugin.Instance.FiscalTrace.Value || _fiscalSignatures[alliance] != output.Signature)
            {
                EmitFiscalIntent(alliance, day, month, year, output);
                _fiscalSignatures[alliance] = output.Signature;
            }

            if (logHeartbeat)
            {
                EmitFiscalTelemetry(alliance, day, month, year, output);
            }
        }

        // Stage E: the alliance's needs (own-alliance and public reads only) and the ranked
        // policy plan PolicySelectionPatch consumes. Runs after UpdateFiscalIntent, whose input
        // and posture it reuses, and only once AICampaign.aifaction exists (vanilla
        // GetAIPersonality 62716 can reach GetStrengthRatio, which indexes it). Spec
        // docs/superpowers/specs/2026-10-14-need-driven-policy-design.md.
        private void UpdatePolicyPlan(int alliance, EraStage era, int reviewSerial)
        {
            using (TelemetryPerf.Scope("campaign.policy-plan", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
                UpdatePolicyPlanCore(alliance, era, reviewSerial);
        }

        private void UpdatePolicyPlanCore(int alliance, EraStage era, int reviewSerial)
        {
            int[] personality = PolicyRuntime.ReadPersonalityList(alliance, out bool emergencyCredit, out bool emergencyManpower);
            var snapshot = CampaignNeedRuntime.Read(alliance, era, _fiscalInputs[alliance], _fiscalOutputs[alliance], emergencyCredit, emergencyManpower);
            int days = _needDaySerial[alliance] >= 0 ? reviewSerial - _needDaySerial[alliance] : 1;
            var previous = Needs[alliance];
            var need = CampaignNeedBuilder.Next(previous, snapshot, days);
            Needs[alliance] = need;
            NeedSnapshots[alliance] = snapshot;
            _needDaySerial[alliance] = reviewSerial;

            var candidates = PolicyRuntime.ReadCandidates(alliance);
            var profile = GrandStrategyRegistry.Resolve(alliance, era);
            var scored = new List<PolicyScore>();
            foreach (var facts in candidates.Facts)
            {
                if (!PolicyEligibility.IsEligible(facts, alliance, out _)) continue;
                scored.Add(PolicyNeedScorer.Score(facts, need, personality, profile, candidates.UsedSlots, candidates.MaxSlots, candidates.DependentsOf));
            }

            var plan = PolicyPlans[alliance];
            if (plan.Update(scored, need, previous))
                EmitPolicyPlan(alliance, plan, need, emergencyCredit || emergencyManpower);
        }

        private static void EmitPolicyPlan(int alliance, PolicyPlanLedger plan, NeedVector need, bool emergency)
        {
            string signature = "source=PolicySelection|alliance=" + alliance + "|pick=" + plan.CurrentPick + "|reason=plan";
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "FiscalIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithDecision("policy-plan", emergency ? "emergency-personality" : "need-ranked", signature)
                .WithField("source", "PolicySelection")
                .WithField("pick", plan.CurrentPick)
                .WithField("candidates", plan.Ranked.Count)
                .WithField("top", plan.DescribeTop(3))
                .WithField("needs", DescribeNeeds(need)));
        }

        private static string DescribeNeeds(NeedVector need)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < PolicyEffectCatalog.NeedKindCount; k++)
            {
                if (k > 0) sb.Append(' ');
                sb.Append((NeedKind)k).Append('=').Append(need[(NeedKind)k].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static bool OperationalRuntimeReady()
        {
            try
            {
                var aicType = AccessTools.TypeByName("AICampaign");
                var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
                return list != null && list.Count > 0;
            }
            catch { return false; }
        }

        private bool UpdateFrontLedger(int alliance, CIC cic)
        {
            int targetObjectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
            var ledger = FrontSectorRuntime.BuildForAlliance(alliance, targetObjectiveId);
            if (ledger == null)
            {
                OnceLog.Warning(
                    "front-ledger:null:" + alliance,
                    $"[FrontLedger] update skipped: build returned null for alliance={alliance}");
                return false;
            }

            Fronts[alliance] = ledger;
            string signature = FrontSectorRuntime.Signature(ledger);
            bool changed = StrategicCadencePolicy.SourceChanged(signature, _frontSignatures[alliance]);
            if (Plugin.Instance.VerboseLogging.Value || _frontSignatures[alliance] != signature)
            {
                EmitCampaignInfo($"[FrontLedger] alliance={alliance} {FrontSectorRuntime.Summary(ledger)}");
                _frontSignatures[alliance] = signature;
            }
            return changed;
        }

        private void UpdateArmyAreaLedger(int alliance, CIC cic)
        {
            int targetObjectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
            string planTargetAreaKey = null;
            var targetPosition = ObjectiveAdapter.ResolveObjectivePosition(targetObjectiveId);
            if (targetPosition.HasValue)
                planTargetAreaKey = ArmyAreaRuntime.AreaKey(targetPosition.Value);

            var ledger = ArmyAreaRuntime.BuildForAlliance(alliance, planTargetAreaKey);
            if (ledger == null)
            {
                OnceLog.Warning(
                    "army-area:null:" + alliance,
                    $"[ArmyArea] update skipped: build returned null for alliance={alliance}");
                return;
            }

            ArmyAreas[alliance] = ledger;
            string signature = ledger.Summary();
            if (Plugin.Instance.VerboseLogging.Value || _armyAreaSignatures[alliance] != signature)
            {
                EmitCampaignInfo($"[ArmyArea] alliance={alliance} {signature}");
                _armyAreaSignatures[alliance] = signature;
            }
        }

        private bool UpdateFormationDirectiveLedger(int alliance, CIC cic, EraStageManager era)
        {
            int targetObjectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
            string planTargetAreaKey = null;
            var targetPosition = ObjectiveAdapter.ResolveObjectivePosition(targetObjectiveId);
            if (targetPosition.HasValue)
                planTargetAreaKey = ArmyAreaRuntime.AreaKey(targetPosition.Value);

            var ledger = FormationDirectiveRuntime.BuildForAlliance(alliance, era.Stage, planTargetAreaKey);
            if (ledger == null)
            {
                OnceLog.Warning(
                    "formation-directive:null:" + alliance,
                    $"[FormationDirective] update skipped: build returned null for alliance={alliance}");
                return false;
            }

            FormationDirectives[alliance] = ledger;
            string signature = ledger.Summary();
            bool changed = StrategicCadencePolicy.SourceChanged(signature, _formationDirectiveSignatures[alliance]);
            if (Plugin.Instance.VerboseLogging.Value || _formationDirectiveSignatures[alliance] != signature)
            {
                EmitCampaignInfo(
                    $"[FormationDirective] alliance={alliance} summary={signature} " +
                    $"lowSupply={ledger.Pressure.LowSupplyCount} lowAmmo={ledger.Pressure.LowAmmoCount} " +
                    $"recover={ledger.Pressure.RecoverCount} mass={ledger.Pressure.MassCount} " +
                    $"supplyArea={ledger.Pressure.TopSupplyAreaKey ?? "<none>"}");
                _formationDirectiveSignatures[alliance] = signature;
            }
            return changed;
        }

        private bool UpdateOperationalProbe(int alliance, CIC cic, int day, int month, int year)
        {
            try
            {
                if (alliance < 0 || alliance >= OperationalProbes.Length) return false;
                var fronts = alliance < Fronts.Length ? Fronts[alliance] : null;
                var formation = alliance < FormationDirectives.Length ? FormationDirectives[alliance] : null;
                if (fronts == null || formation == null) return false;

                int targetObjectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
                var targetPosition = ObjectiveAdapter.ResolveObjectivePosition(targetObjectiveId);
                int daySerial = year * 372 + month * 31 + day;
                float localOwn = 0f, localEnemy = 0f;
                if (targetPosition.HasValue)
                {
                    HistoricalOperationProfile profile = null;
                    if (!string.IsNullOrEmpty(cic?.ActivePlan?.OperationId))
                        HistoricalOperationCatalog.TryGetById(cic.ActivePlan.OperationId, out profile);
                    ReadLocalForceBalance(alliance, targetPosition.Value, HistoricalOperationContextBuilder.NearTargetRadius(profile), out localOwn, out localEnemy);
                }
                var eraManager = alliance < Eras.Length ? Eras[alliance] : null;
                EraStage era = eraManager != null ? eraManager.Stage : EraStage.Amateur1861;
                PersonalityVector personality = cic != null && eraManager != null
                    ? cic.Effective(eraManager)
                    : new PersonalityVector();
                var input = OperationalProbeRuntime.BuildInput(
                    alliance,
                    cic,
                    fronts,
                    formation,
                    _operationalProbeStates[alliance],
                    daySerial,
                    era,
                    SafePolicyChapter(),
                    month,
                    personality,
                    BattleHistory,
                    localOwn,
                    localEnemy);

                var posture = DirectorMemories[alliance]?.LastPosture;
                if (posture != null)
                    StrategicResilienceDirector.ApplyTo(input.Options, posture);
                input.PackageOptions = CoordinatedOperationOptions.FromDirector(
                    OperationalProbeLedger.ResolvePackageDesiredStrength(input),
                    posture);

                var output = OperationalProbeLedger.Build(input);
                var filtered = _probeHysteresis[alliance].Filter(
                    output.Decision,
                    continuingProbe: input.Previous != null,
                    targetAreaKey: output.TargetAreaKey ?? input.PlanTargetAreaKey,
                    daySerial: daySerial);
                if (filtered != output.Decision)
                {
                    output.Reason = filtered == OperationalProbeDecision.Pause ? "withdraw-confirming" : "reprobe-cooldown";
                    output.Decision = filtered;
                    if (filtered == OperationalProbeDecision.None) output.Package = null;
                }
                OperationalProbes[alliance] = output;

                bool clearState =
                    output.Decision == OperationalProbeDecision.None ||
                    output.Decision == OperationalProbeDecision.Withdraw ||
                    output.Decision == OperationalProbeDecision.Escalate;

                _operationalProbeStates[alliance] = clearState ? null : output.State;
                output.State = _operationalProbeStates[alliance];

                bool overlayChanged = formation.ApplyOperationalProbe(output);
                string signature = output.Signature();
                bool changed = StrategicCadencePolicy.SourceChanged(signature, _operationalProbeSignatures[alliance]);
                if (Plugin.Instance.VerboseLogging.Value || changed)
                {
                    EmitOperationalProbe(alliance, day, month, year, output, signature);
                    _operationalProbeSignatures[alliance] = signature;
                }

                if (overlayChanged)
                {
                    string formationSignature = formation.Summary();
                    _formationDirectiveSignatures[alliance] = formationSignature;
                    EmitCampaignInfo($"[FormationDirective] alliance={alliance} operationalProbe={formationSignature}");
                }

                OperationalProbeRuntime.Run(alliance, output, targetPosition);
                return changed || overlayChanged;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("operational-probe:update:" + alliance,
                    "[OperationalProbe] update failed: " + ex.Message);
                return false;
            }
        }

        private string FormationSourceSignature(int alliance, CIC cic)
        {
            int targetObjectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
            return "front=" + (_frontSignatures[alliance] ?? "") +
                "|target=" + targetObjectiveId +
                "|defense=" + (_defenseIntentSignatures[alliance] ?? "");
        }

        private string ArmyAreaSourceSignature(int alliance, CIC cic)
        {
            int targetObjectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
            return "target=" + targetObjectiveId +
                "|formation=" + (_formationDirectiveSignatures[alliance] ?? "") +
                "|probe=" + (_operationalProbeSignatures[alliance] ?? "");
        }

        private string ConstructionSourceSignature(int alliance)
        {
            return "map=" + (_campaignMapSignature ?? "") +
                "|front=" + (_frontSignatures[alliance] ?? "") +
                "|formation=" + (_formationDirectiveSignatures[alliance] ?? "") +
                "|fiscal=" + (_fiscalSignatures[alliance] ?? "");
        }

        // Produces a short signature string that captures the current input
        // state of the Director for one alliance.  Used for staleness detection
        // across daily ticks: a mismatch triggers a re-evaluate even when the
        // real-second clamp would otherwise suppress a publish.
        private string DirectorSourceSignature(int alliance)
        {
            return "front="      + (_frontSignatures[alliance]              ?? "") +
                "|formation="    + (_formationDirectiveSignatures[alliance] ?? "") +
                "|defense="      + (_defenseIntentSignatures[alliance]      ?? "") +
                "|fiscal="       + (_fiscalSignatures[alliance]             ?? "") +
                "|construction=" + (_constructionSignatures[alliance]       ?? "") +
                "|probe="        + (_operationalProbeSignatures[alliance]   ?? "") +
                "|era="          + (Eras[alliance]?.Stage.ToString()        ?? "") +
                "|chapter="      + SafePolicyChapter();
        }

        // Reads live morale / prefs via reflection (same pattern as GameVars
        // commander lookups already in this file).  Falls back to safe defaults
        // so that a reflection gap silently degrades rather than crashing.
        private CampaignPaceInput BuildCampaignPaceInput(int alliance, int day, int month, int year)
        {
            float ownMorale = 50f, enemyMorale = 50f;
            float breakTrigger = 30f, minSurrender = 18f;
            try
            {
                if (_gameVarsType == null) _gameVarsType = AccessTools.TypeByName("GameVars");
                if (_allianceArrayField == null) _allianceArrayField = AccessTools.Field(_gameVarsType, "alliance");
                var allianceArray = _allianceArrayField?.GetValue(null) as System.Array;
                if (allianceArray != null)
                {
                    int enemyAlliance = 1 - alliance;
                    var ownEntry   = allianceArray.GetValue(alliance);
                    var enemyEntry = enemyAlliance < allianceArray.Length ? allianceArray.GetValue(enemyAlliance) : null;
                    if (ownEntry != null)
                    {
                        if (_nationalMoraleField == null)
                            _nationalMoraleField = AccessTools.Field(ownEntry.GetType(), "nationalmorale");
                        if (_nationalMoraleField != null)
                        {
                            ownMorale = (float)_nationalMoraleField.GetValue(ownEntry);
                            if (enemyEntry != null)
                                enemyMorale = (float)_nationalMoraleField.GetValue(enemyEntry);
                        }
                    }
                }
                if (_gamePrefsType == null) _gamePrefsType = AccessTools.TypeByName("GamePrefs");
                if (_breakMoraleField == null)
                    _breakMoraleField = AccessTools.Field(_gamePrefsType, "breakmoraletrigger");
                if (_minSurrenderField == null)
                    _minSurrenderField = AccessTools.Field(_gamePrefsType, "minnationalmoralesurrender");
                if (_breakMoraleField  != null) breakTrigger  = (float)_breakMoraleField.GetValue(null);
                if (_minSurrenderField != null) minSurrender  = (float)_minSurrenderField.GetValue(null);
            }
            catch (Exception)
            {
                OnceLog.Warning("director:morale-reflect:" + alliance,
                    "[Director] nationalmorale/GamePrefs reflection failed; using defaults");
            }

            int daySerial = year * 372 + month * 31 + day;
            int battles = 0, majorBattles = 0;
            foreach (var b in BattleHistory)
            {
                int bDay = b.Year * 372 + b.Month * 31 + b.Day;
                if (bDay <= daySerial && daySerial - bDay <= 14 && b.IsLandBattle)
                {
                    battles++;
                    if (b.IsMajorResult) majorBattles++;
                }
            }

            return new CampaignPaceInput
            {
                AllianceId                   = alliance,
                Year                         = year,
                Month                        = month,
                PolicyChapter                = SafePolicyChapter(),
                OwnNationalMorale            = ownMorale,
                EnemyNationalMorale          = enemyMorale,
                BreakMoraleTrigger           = breakTrigger,
                MinNationalMoraleSurrender   = minSurrender,
                BattlesIn14Days              = battles,
                MajorBattlesIn14Days         = majorBattles,
                CapitalDangerStreakDays       = DirectorMemories[alliance]?.CapitalDangerStreakDays ?? 0,
                DaysSinceFrontSignatureChange = CampaignResponsivenessSignals.DaysSince(_frontChangedDay[alliance], daySerial),
                IsWinter                     = month == 12 || month == 1 || month == 2,
                TheaterPressure              = TheaterPressureView.From(Fronts[alliance])
            };
        }

        public void OnEventTrigger(int allianceId, string eventType)
        {
            if (allianceId < 0 || allianceId >= CICs.Length) return;
            var cic = CICs[allianceId];
            if (cic?.ActivePlan == null) return;

            cic.ActivePlan.IsDirty = true;
            EmitCampaignInfoToMainLogIfAllowed($"[Coordinator] event '{eventType}' for alliance {allianceId} — plan marked dirty");
        }

        internal void RecordBattleOutcome(BattleHistoryRecord record)
        {
            if (record == null || record.AllianceWon < 0) return;

            foreach (var existing in BattleHistory)
                if (existing.SameBattleKey(record)) return;

            BattleHistory.Add(record);
            while (BattleHistory.Count > 64) BattleHistory.RemoveAt(0);

            string scale = record.IsMajorResult ? "major" : "minor";
            EmitCampaignInfoToMainLogIfAllowed(
                $"[BattleHistory] {record.Year}-{record.Month:D2}-{record.Day:D2} " +
                $"{record.BattleName} winner={record.AllianceWon} loser={record.LosingAlliance} " +
                $"result={scale} theater={record.Theater}");

            DirtyPlanIfBattleInvalidates(record.AllianceWon, record);
            if (record.LosingAlliance != record.AllianceWon)
                DirtyPlanIfBattleInvalidates(record.LosingAlliance, record);
        }

        // R4: only a decisive land battle in the plan's target theater / near its target
        // re-plans an operation; any other battle is recorded in BattleHistory only.
        private void DirtyPlanIfBattleInvalidates(int allianceId, BattleHistoryRecord record)
        {
            if (allianceId < 0 || allianceId >= CICs.Length) return;
            var plan = CICs[allianceId]?.ActivePlan;
            var phase = plan?.CurrentPhase;
            if (phase == null) return;

            var target = ObjectiveAdapter.ResolveObjectivePosition(phase.TargetObjectiveId);
            HistoricalOperationProfile profile = null;
            if (!string.IsNullOrEmpty(plan.OperationId))
                HistoricalOperationCatalog.TryGetById(plan.OperationId, out profile);
            var query = new BattleReplanQuery
            {
                HasTarget = target.HasValue,
                TargetX = target.HasValue ? target.Value.x : 0f,
                TargetZ = target.HasValue ? target.Value.z : 0f,
                TargetTheater = target.HasValue ? CampaignMapTheaterRuntime.FromPosition(target.Value) : Theater.Unknown,
                BattleTheater = CampaignMapTheaterRuntime.FromPosition(new Vector3(record.PositionX, 0f, record.PositionZ)),
                Radius = HistoricalOperationContextBuilder.NearTargetRadius(profile)
            };
            if (CicReviewRouter.BattleInvalidatesPlan(record, query))
                OnEventTrigger(allianceId, "battle_result");
        }

        internal void RecordConstructionStart(ConstructionStartEvent start)
        {
            var telemetry = ConstructionTelemetry ?? (ConstructionTelemetry = new ConstructionTelemetry());
            telemetry.Record(start);

            if (ConstructionVerboseLoggingEnabled())
            {
                EmitCampaignInfo(
                    $"[ConstructionStart] alliance={start.AllianceId} kind={start.Kind} " +
                    $"name={start.Name ?? "<unnamed>"} theater={start.Theater} site={start.SiteKey ?? "<none>"}");
            }
        }

        private static bool ConstructionVerboseLoggingEnabled()
        {
            try
            {
                var plugin = Plugin.Instance;
                return plugin != null &&
                    (ConfigValue(plugin.VerboseLogging) ||
                     ConfigValue(plugin.ConstructionVerboseLogging));
            }
            catch { return false; }
        }

        private static void EmitDirectorTrace(int alliance, int day, int month, int year, DirectorPosture posture)
        {
            if (posture == null) return;
            string signature = "alliance=" + alliance + "|source=" + (posture.SourceSignature ?? "-") + "|stale=" + posture.Stale;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Trace, "Director:trace", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithCampaignDate(CampaignDate(year, month, day))
                .WithDecision("trace", posture.Reason ?? "-", signature)
                .WithField("sourceSignature", posture.SourceSignature ?? "-")
                .WithField("stale", posture.Stale)
                .WithField("pace", posture.Pace.ToString())
                .WithField("intent", posture.Intent.ToString())
                .WithField("risk", posture.Risk.ToString()));
        }

        private static void EmitConstructionIntent(int alliance, EraStage era, ConstructionOutput output)
        {
            if (output == null) return;
            string signature = string.IsNullOrWhiteSpace(output.Signature)
                ? "alliance=" + alliance + "|posture=" + output.Posture
                : output.Signature;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "ConstructionIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithDecision("compute", output.Posture.ToString(), signature)
                .WithField("era", era.ToString())
                .WithField("posture", output.Posture.ToString())
                .WithField("theater", output.TopConstructionTheater ?? "-")
                .WithField("private", output.TopPrivateBuilding.Name ?? "-")
                .WithField("depot", output.TopSupplyDepot.Name ?? "-")
                .WithField("fort", output.TopFort.Name ?? "-")
                .WithField("telegraph", output.TopTelegraph.Name ?? "-")
                .WithField("rail", output.TopRailroad.Name ?? "-")
                .WithField("suppressionCount", output.Suppressions != null ? output.Suppressions.Length : 0));
        }

        private static void EmitConstructionTelemetry(int alliance, ConstructionOutput output, string summary)
        {
            string posture = output != null ? output.Posture.ToString() : "-";
            string safeSummary = string.IsNullOrWhiteSpace(summary) ? "-" : summary;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.State, "ConstructionTelemetry", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithDecision("heartbeat", posture, "alliance=" + alliance + "|posture=" + posture + "|summary=" + safeSummary)
                .WithField("posture", posture)
                .WithField("summary", safeSummary));
        }

        private static void EmitDefenseIntentDetail(int alliance, int day, int month, int year, DefenseResponse response)
        {
            var threat = response?.Threat;
            string threatSignature = threat?.Signature ?? "-";
            string responseSignature = response?.TelemetrySignature ?? "-";
            string reason = threat?.EscalationReason ?? "-";
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "DefenseIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithCampaignDate(CampaignDate(year, month, day))
                .WithDecision("select-package", reason, "alliance=" + alliance + "|threat=" + threatSignature + "|response=" + responseSignature)
                .WithField("posture", threat != null ? threat.Posture.ToString() : "-")
                .WithField("sourceKind", threat != null ? threat.SourceKind.ToString() : "-")
                .WithField("threat", threatSignature)
                .WithField("enemy", threat != null ? threat.EnemyStrength : 0.0)
                .WithField("desired", threat != null ? threat.DesiredStrength : 0.0)
                .WithField("selected", response?.SelectedPackage != null ? response.SelectedPackage.Count : 0)
                .WithField("adequate", response != null && response.Adequate)
                .WithField("understrength", response != null && response.Understrength)
                .WithField("responseSignature", responseSignature));
        }

        private static void EmitDefenseIntentSummary(int alliance, int day, int month, int year, DefenseIntentLedgerOutput output)
        {
            if (output == null) return;
            string summary = DefenseIntentTelemetry.Summary(output);
            string signature = string.IsNullOrWhiteSpace(output.Signature)
                ? "alliance=" + alliance + "|summary=" + summary
                : output.Signature;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.State, "DefenseIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithCampaignDate(CampaignDate(year, month, day))
                .WithDecision("summary", summary, signature)
                .WithField("summary", summary)
                .WithField("responseCount", output.Responses != null ? output.Responses.Count : 0));
        }

        private static void EmitFiscalIntent(int alliance, int day, int month, int year, FiscalOutput output)
        {
            if (output == null) return;
            string signature = string.IsNullOrWhiteSpace(output.Signature)
                ? "alliance=" + alliance + "|posture=" + output.Posture
                : output.Signature;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "FiscalIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithCampaignDate(CampaignDate(year, month, day))
                .WithDecision("compute", output.Posture.ToString(), signature)
                .WithField("posture", output.Posture.ToString())
                .WithField("gate", output.DefendedGate.ToString())
                .WithField("supply", output.SupplyProtection)
                .WithField("forceCap", output.ForceCapWarning)
                .WithField("minimumRating", output.MinimumAcceptableRating)
                .WithField("theater", output.TheaterSupplyPriority ?? "-"));
        }

        private static void EmitFiscalTelemetry(int alliance, int day, int month, int year, FiscalOutput output)
        {
            if (output == null) return;
            string signature = "alliance=" + alliance + "|posture=" + output.Posture + "|gate=" + output.DefendedGate + "|theater=" + (output.TheaterSupplyPriority ?? "-");
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.State, "FiscalTelemetry", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithCampaignDate(CampaignDate(year, month, day))
                .WithDecision("heartbeat", output.Posture.ToString(), signature)
                .WithField("posture", output.Posture.ToString())
                .WithField("gate", output.DefendedGate.ToString())
                .WithField("supply", output.SupplyProtection)
                .WithField("forceCap", output.ForceCapWarning)
                .WithField("theater", output.TheaterSupplyPriority ?? "-"));
        }

        private static void EmitOperationalProbe(int alliance, int day, int month, int year, OperationalProbeOutput output, string signature)
        {
            if (output == null) return;
            string inputSignature = string.IsNullOrWhiteSpace(signature)
                ? output.Signature()
                : signature;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "OperationalProbe", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithCampaignDate(CampaignDate(year, month, day))
                .WithUnit(output.SelectedUnitKey ?? "-")
                .WithDecision(output.Decision.ToString(), output.Reason ?? "-", inputSignature)
                .WithField("objective", output.ObjectiveId)
                .WithField("probeId", output.ProbeId ?? "-")
                .WithField("unit", output.SelectedUnitKey ?? "-")
                .WithField("target", output.TargetAreaKey ?? "-")
                .WithField("sourceSector", output.SourceSectorKey ?? "-")
                .WithField("mass", output.RequiresMassCommitment)
                .WithField("packageSignature", output.Package != null ? output.Package.Signature() : "-"));
        }

        private static string CampaignDate(int year, int month, int day)
        {
            return year + "-" + month.ToString("D2") + "-" + day.ToString("D2");
        }

        private static void EmitCampaignInfo(string line)
        {
            TelemetryRouter.LegacyInfo(line, TelemetryLayer.Campaign);
        }

        private static void EmitCampaignInfoToMainLogIfAllowed(string line)
        {
            TelemetryRouter.LegacyInfoToMainLogIfAllowed(line, TelemetryLayer.Campaign);
        }

        private static bool ConfigValue(ConfigEntry<bool> entry, bool defaultValue = false)
        {
            try
            {
                return entry != null ? entry.Value : defaultValue;
            }
            catch { return defaultValue; }
        }

        private static float ConfigValue(ConfigEntry<float> entry, float defaultValue)
        {
            try
            {
                return entry != null ? entry.Value : defaultValue;
            }
            catch { return defaultValue; }
        }

        public static int ResolvePlayerAlliance()
        {
            try
            {
                var t = AccessTools.TypeByName("GameVars");
                var f = AccessTools.Field(t, "playeralliance");
                return f != null ? (int)f.GetValue(null) : -1;
            }
            catch { return -1; }
        }

        private static bool ShouldRunCicFor(int alliance, int playerAlliance, bool factionListReady)
        {
            bool aiVsAi = false;
            try { aiVsAi = GameVars.ai_vs_ai; } catch { }
            return CicEligibility.ShouldRunCic(
                alliance,
                factionListReady,
                factionListReady && ResolveAifactionIndexForAlliance(alliance) >= 0,
                playerAlliance,
                aiVsAi,
                IsPlayerCICOf(alliance, playerAlliance));
        }

        public static bool IsPlayerCICOf(int allianceId, int playerAlliance)
        {
            if (allianceId != playerAlliance) return false;
            try
            {
                // Direct reads (no reflection: this runs on many per-call paths). Vanilla
                // DLC_WL.IsCommanderInChief(manualcommander = -1) (decompile 47475) checks
                // dlc_chosencommander.
                return DLC_WL.dlc_scenarioactive && DLC_WL.IsCommanderInChief();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Coordinator] IsPlayerCICOf failed: " + ex.Message);
                return false;
            }
        }

        // WlCareerStartGate.CareerNotStarted on vanilla state (direct reads, as IsPlayerCICOf).
        public static bool WlCareerNotStarted()
        {
            try
            {
                return WlCareerStartGate.CareerNotStarted(DLC_WL.dlc_scenarioactive, DLC_WL.dlc_chosencommander);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-career-not-started-gate", "[Coordinator] W&L career start gate failed: " + ex.Message);
                return false;
            }
        }

        // Vanilla's job-machine pause predicate (AICampaign.UpdateUnitAI 11368).
        public static bool WlVanillaJobMachinePaused()
        {
            try
            {
                bool active = DLC_WL.dlc_scenarioactive;
                return active && WlCareerStartGate.VanillaJobMachinePaused(active, DLC_WL.GetPlayerCommandHierarchy(), DLC_WL.IsCommanderInChief());
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-job-machine-pause-gate", "[Coordinator] W&L job-machine pause gate failed: " + ex.Message);
                return false;
            }
        }

        public static bool WlCareerStartPending()
        {
            try
            {
                if (_dlcWlType == null) _dlcWlType = AccessTools.TypeByName("DLC_WL");
                if (_dlcWlType == null) return false;

                if (_dlcScenarioActiveField == null) _dlcScenarioActiveField = AccessTools.Field(_dlcWlType, "dlc_scenarioactive");
                if (_dlcChosenCommanderField == null) _dlcChosenCommanderField = AccessTools.Field(_dlcWlType, "dlc_chosencommander");
                bool active = Convert.ToBoolean(_dlcScenarioActiveField?.GetValue(null) ?? false);
                int chosen = Convert.ToInt32(_dlcChosenCommanderField?.GetValue(null) ?? -1);
                bool hasCommand = false;

                if (_gameVarsType == null) _gameVarsType = AccessTools.TypeByName("GameVars");
                if (_gameVarsCommanderField == null) _gameVarsCommanderField = AccessTools.Field(_gameVarsType, "commander");
                var commanders = _gameVarsCommanderField?.GetValue(null) as IList;
                if (commanders != null && chosen >= 0 && chosen < commanders.Count)
                {
                    var commander = commanders[chosen];
                    if (commander != null)
                    {
                        var commanderType = commander.GetType();
                        if (_commanderType != commanderType)
                        {
                            _commanderType = commanderType;
                            _commanderCurrentCommandField = AccessTools.Field(commanderType, "currentcommand");
                        }
                        hasCommand = _commanderCurrentCommandField?.GetValue(commander) != null;
                    }
                }

                return WlCareerStartGate.ShouldDeferStrategicReview(active, chosen, hasCommand);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-career-start-gate", "[Coordinator] W&L career start gate failed: " + ex.Message);
                return false;
            }
        }

        private static Type _dlcWlType;
        private static Type _gameVarsType;
        private static Type _commanderType;
        private static FieldInfo _dlcScenarioActiveField;
        private static FieldInfo _dlcChosenCommanderField;
        private static FieldInfo _gameVarsCommanderField;
        private static FieldInfo _commanderCurrentCommandField;
        private static FieldInfo _allianceArrayField;
        private static FieldInfo _nationalMoraleField;
        private static Type _gamePrefsType;
        private static FieldInfo _breakMoraleField;
        private static FieldInfo _minSurrenderField;

        // Cached snapshot — recomputed once per strategic review to avoid
        // re-reading 3+ towns per faction iteration.
        private WarStateObserver.Snapshot _lastSnapshot;
        private int _lastSnapshotDay = -1;
        private int _lastSnapshotMonth = -1;
        private int _lastSnapshotYear  = -1;

        private WarStateObserver.Snapshot ObserveWarStateCached(int day, int month, int year)
        {
            if (day == _lastSnapshotDay && month == _lastSnapshotMonth && year == _lastSnapshotYear)
                return _lastSnapshot;

            var snap = WarStateObserver.Observe();
            _lastSnapshot = snap;
            _lastSnapshotDay = day;
            _lastSnapshotMonth = month;
            _lastSnapshotYear  = year;

            // Log only when state changes month-over-month — non-spammy.
            // Helps smoke-testers see WHEN towns fall.
            if (snap.VicksburgFallen   && !_loggedVicksburg)   { EmitCampaignInfoToMainLogIfAllowed($"[WarState] Vicksburg fell ({year}-{month:D2})");   _loggedVicksburg = true; }
            if (snap.ChattanoogaFallen && !_loggedChattanooga) { EmitCampaignInfoToMainLogIfAllowed($"[WarState] Chattanooga fell ({year}-{month:D2})"); _loggedChattanooga = true; }
            if (snap.AtlantaThreatened && !_loggedAtlanta)     { EmitCampaignInfoToMainLogIfAllowed($"[WarState] Atlanta fell ({year}-{month:D2})");     _loggedAtlanta = true; }

            return snap;
        }

        private bool _loggedVicksburg;
        private bool _loggedChattanooga;
        private bool _loggedAtlanta;

        private SuccessionScheduler.WarStateView BuildSchedulerView(int month, int year, int alliance, WarStateObserver.Snapshot snap)
        {
            return new SuccessionScheduler.WarStateView
            {
                CurrentMonth = month,
                CurrentYear  = year,
                // Town-ownership signals (v0.2.1).
                VicksburgFallen     = snap.VicksburgFallen,
                ChattanoogaFallen   = snap.ChattanoogaFallen,
                AtlantaThreatened   = snap.AtlantaThreatened,
                // Battle-history / commander-state signals deferred to v0.2.2.
                ANVHasLostMajorBattle      = snap.ANVHasLostMajorBattle,
                JohnstonWoundedOrDisabled  = snap.JohnstonWoundedOrDisabled,
                BragsCommandRatingLow      = snap.BragsCommandRatingLow,
                AoPHasFailedNOffensives    = snap.AoPHasFailedNOffensives,
                BurnsidesFirstDefeatPassed = snap.BurnsidesFirstDefeatPassed,
                LeeInvadingPennsylvania    = snap.LeeInvadingPennsylvania,
                WesternMajorDefeatPassed   = snap.WesternMajorDefeatPassed,
                DavisPatienceExhausted     = snap.DavisPatienceExhausted,
                ValleyOpsNeeded            = snap.ValleyOpsNeeded,
                WarClearlyLost             = snap.WarClearlyLost
            };
        }

        private void SwapOfficer(int alliance, SuccessionScheduler.Event e)
        {
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "Succession", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithDecision("fired", e.Name, "alliance=" + alliance + "|event=" + e.Id + "|replacement=" + e.ReplacementName)
                .WithField("eventId", e.Id)
                .WithField("name", e.Name)
                .WithField("replacedRole", e.ReplacedRole)
                .WithField("replacement", e.ReplacementName));
        }

        public void SaveSidecar(string fullPath)
        {
            try
            {
                var dto = SidecarDtoCodec.Encode(BuildSnapshot());
                string json;
                using (TelemetryPerf.Scope("campaign.sidecar-json-serialization", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
                    json = SidecarDtoCodec.Serialize(dto);
                using (TelemetryPerf.Scope("campaign.sidecar-file-write", TelemetryLayer.Campaign, TelemetryCategory.Performance, 4.0))
                    SidecarDtoCodec.WriteAtomic(fullPath, json);
                EmitCampaignInfoToMainLogIfAllowed("[Coordinator] sidecar written: " + fullPath);
            }
            catch (Exception ex) { Plugin.Log.LogError("[Coordinator] sidecar save failed: " + ex); }
        }

        // Transactional: a sidecar (or, failing that, its .bak then .tmp) is fully parsed and
        // decoded into a SidecarSnapshot before any live state changes. With no usable file the
        // fresh state from InitializeFromGameState is kept instead of a half-applied mix.
        public void LoadSidecar(string fullPath)
        {
            if (SidecarDtoCodec.TryLoadWithFallback(fullPath, out var snapshot, out var source, out var reason))
            {
                ApplySnapshot(snapshot);
                Initialized = true;
                EmitCampaignInfoToMainLogIfAllowed("[Coordinator] sidecar loaded: " + source);
                return;
            }
            if (reason == "no-sidecar")
                Plugin.Log.LogInfo($"[Coordinator] no sidecar found at {fullPath} — initializing fresh state (this is normal for a brand-new career)");
            else
                Plugin.Log.LogWarning("[Coordinator] sidecar load rejected (" + reason + "); starting fresh strategic state");
            InitializeFromGameState();
        }

        // Vanilla AICampaign.Load restores AIFaction.followedcampaignobjective from AICampaign.dat
        // (decompile 16549) before our Load Postfix applies the sidecar. -1 when unavailable.
        private static int ReadFollowedCampaignObjective(int alliance)
        {
            try
            {
                int index = ResolveAifactionIndexForAlliance(alliance);
                var faction = index >= 0 ? AICampaignReflect.GetFaction(index) : null;
                if (faction == null) return -1;
                var field = AccessTools.Field(faction.GetType(), "followedcampaignobjective");
                return field != null ? Convert.ToInt32(field.GetValue(faction)) : -1;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("coordinator:followed-objective", "[Coordinator] followedcampaignobjective read failed: " + ex.Message);
                return -1;
            }
        }

        private SidecarSnapshot BuildSnapshot()
        {
            var snap = SidecarSnapshot.Empty();
            for (int alliance = 0; alliance < 2; alliance++)
            {
                snap.Cics[alliance] = CICs[alliance];
                snap.Eras[alliance] = Eras[alliance]?.Stage;
                snap.DirectorMemories[alliance] = DirectorMemories[alliance] ?? new DirectorMemory();
                snap.RecentReplans[alliance] = OperationMemories[alliance]?.SnapshotRecentReplans();
                snap.ProbeStates[alliance] = _operationalProbeStates[alliance];
                snap.FiscalMemories[alliance] = _fiscalMemory[alliance];
                snap.PolicyNeeds[alliance] = Needs[alliance]?.ToArray();
                snap.PolicyPicks[alliance] = PolicyPlans[alliance].CurrentPick;
                snap.FrontChangedDay[alliance] = _frontChangedDay[alliance];
                snap.CapitalStreakDay[alliance] = _capitalStreakDay[alliance];
                snap.ProbeHysteresis[alliance] = _probeHysteresis[alliance].Snapshot();
            }
            foreach (var kv in MinorOfficerProfiles) snap.MinorOfficers[kv.Key] = kv.Value;
            snap.FiredEvents.UnionWith(Succession.FiredEventIds);
            snap.AppliedEvents.UnionWith(Succession.AppliedEventIds);
            snap.ForcedEvents.UnionWith(Succession.ForcedEventIds);
            snap.Battles.AddRange(BattleHistory);
            snap.LastSeenMonth = LastSeenMonth;
            snap.LastSeenYear = LastSeenYear;
            snap.SaveSequence = ++_saveSequence;
            snap.PlayerCompliance = PlayerComplianceRuntime.Export();
            var written = PickCampaignObjectivePatch.SnapshotWrittenByMod();
            for (int a = 0; a < written.Length && a < snap.ModWrittenObjective.Length; a++)
                snap.ModWrittenObjective[a] = written[a];
            return snap;
        }

        private void ApplySnapshot(SidecarSnapshot snap)
        {
            for (int alliance = 0; alliance < 2; alliance++)
            {
                CICs[alliance] = snap.Cics[alliance];
                Eras[alliance] = new EraStageManager { Stage = snap.Eras[alliance] ?? EraStage.Amateur1861 };
                if (snap.Eras[alliance].HasValue) _erasSeededFromSave = true;
                DirectorMemories[alliance] = snap.DirectorMemories[alliance] ?? new DirectorMemory();
                OperationMemories[alliance] = new OperationDecisionMemory();
                OperationMemories[alliance].RestoreRecentReplans(snap.RecentReplans[alliance]);
                _operationalProbeStates[alliance] = snap.ProbeStates[alliance];
                _fiscalMemory[alliance] = snap.FiscalMemories[alliance] ?? new FiscalStateMemory();
                Needs[alliance] = snap.PolicyNeeds[alliance] != null ? NeedVector.FromArray(snap.PolicyNeeds[alliance]) : null;
                PolicyPlans[alliance] = new PolicyPlanLedger();
                PolicyPlans[alliance].Restore(snap.PolicyPicks[alliance]);
                _frontChangedDay[alliance] = snap.FrontChangedDay[alliance];
                _capitalStreakDay[alliance] = snap.CapitalStreakDay[alliance];
                _probeHysteresis[alliance] = ProbeHysteresis.FromSnapshot(snap.ProbeHysteresis[alliance]);
            }
            MinorOfficerProfiles.Clear();
            foreach (var kv in snap.MinorOfficers) MinorOfficerProfiles[kv.Key] = kv.Value;
            Succession = new SuccessionScheduler
            {
                FiredEventIds = new HashSet<int>(snap.FiredEvents),
                AppliedEventIds = new HashSet<int>(snap.AppliedEvents)
            };
            BattleHistory.Clear();
            BattleHistory.AddRange(snap.Battles);
            LastSeenMonth = snap.LastSeenMonth;
            LastSeenYear = snap.LastSeenYear;
            _saveSequence = snap.SaveSequence;
            PlayerComplianceRuntime.Restore(snap.PlayerCompliance);
            var written = new int[2];
            for (int a = 0; a < written.Length; a++)
            {
                written[a] = CampaignObjectiveSteering.InferModWritten(
                    snap.ModWrittenObjective[a],
                    ReadFollowedCampaignObjective(a),
                    CICs[a]?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1);
            }
            PickCampaignObjectivePatch.RestoreWrittenByMod(written);
        }
    }
}
