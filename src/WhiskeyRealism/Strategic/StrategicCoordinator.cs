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
        private readonly DefenseCooldownTable[] _defenseCooldowns = new DefenseCooldownTable[2]
        {
            new DefenseCooldownTable(),
            new DefenseCooldownTable()
        };
        private readonly string[] _defenseIntentSignatures = new string[2];
        private readonly HashSet<string>[] _previousSifSignatures = new HashSet<string>[2]
        {
            new HashSet<string>(),
            new HashSet<string>()
        };
        public CampaignMapLedger CampaignMap = CampaignMapLedger.Build(null);
        internal SuccessionScheduler Succession = new SuccessionScheduler();
        public Dictionary<int, PersonalityVector> MinorOfficerProfiles = new Dictionary<int, PersonalityVector>();
        internal readonly List<BattleHistoryRecord> BattleHistory = new List<BattleHistoryRecord>();
        internal readonly DirectorMemory[] DirectorMemories = new DirectorMemory[2] { new DirectorMemory(), new DirectorMemory() };
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
        private readonly string[] _fiscalSignatures = new string[2];
        private readonly string[] _constructionSignatures = new string[2];
        private readonly string[] _formationSourceSignatures = new string[2];
        private readonly string[] _armyAreaSourceSignatures = new string[2];
        private readonly string[] _constructionSourceSignatures = new string[2];
        private string _campaignMapSignature;
        private string _campaignMapDynamicSignature;
        private readonly DailyCadence _operationalCadence = new DailyCadence();
        private bool _operationalRuntimeDeferredLogged;
        private bool _wlCareerStartDeferredLogged;

        public int LastSeenMonth = -1;
        public int LastSeenYear  = -1;

        public bool Initialized;

        public static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("WhiskeyRealismStrategicCoordinator");
            Instance = go.AddComponent<StrategicCoordinator>();
            DontDestroyOnLoad(go);
            Plugin.Log.LogInfo("[Coordinator] bootstrapped");
        }

        public void InitializeFromGameState()
        {
            try
            {
                Eras[0] = Eras[0] ?? new EraStageManager { Stage = EraStage.Amateur1861 };
                Eras[1] = Eras[1] ?? new EraStageManager { Stage = EraStage.Amateur1861 };

                int playerAlliance = ResolvePlayerAlliance();
                for (int alliance = 0; alliance < 2; alliance++)
                {
                    if (IsPlayerCICOf(alliance, playerAlliance))
                    {
                        CICs[alliance] = null;
                        continue;
                    }
                    if (CICs[alliance] == null)
                        CICs[alliance] = BuildCICForAlliance(alliance);
                }
                Initialized = true;
                Plugin.Log.LogInfo($"[Coordinator] initialized (playerAlliance={playerAlliance})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Coordinator] init failed: " + ex);
            }
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

        public void NotifyDateAdvanced(int gameMonth, int gameYear)
        {
            NotifyDateAdvanced(1, gameMonth, gameYear);
        }

        public void NotifyDateAdvanced(int gameDay, int gameMonth, int gameYear)
        {
            if (!Initialized) InitializeFromGameState();

            bool firstCall = (LastSeenMonth < 0);
            bool rollover  = !firstCall && ((gameMonth != LastSeenMonth) || (gameYear != LastSeenYear));

            // Fire on first valid call (campaign just started / save just loaded)
            // AND on every subsequent month rollover. The first heartbeat gives
            // smoke-testers immediate confirmation the strategic core is running
            // without having to advance game-time past a month boundary first.
            bool ranMonthly = false;
            if (firstCall || rollover)
            {
                LastSeenMonth = gameMonth;
                LastSeenYear  = gameYear;
                OnMonthlyTick(gameMonth, gameYear);
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
                OnceLog.Info("dailyops", "Daily operational analysis active");

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

        public void OnMonthlyTick(int month, int year)
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

                RunStrategicReview(1, month, year, logHeartbeat: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Coordinator] tick failed: " + ex);
            }
        }

        private void RunStrategicReview(int day, int month, int year, bool logHeartbeat)
        {
            var totalWatch = Stopwatch.StartNew();
            double mapMs = 0d;
            double frontMs = 0d;
            double armyAreaMs = 0d;
            double formationMs = 0d;
            double probeMs = 0d;
            double fiscalMs = 0d;
            double constructionMs = 0d;
            double defenseMs = 0d;

            if (WlCareerStartPending())
            {
                if (!_wlCareerStartDeferredLogged)
                {
                    _wlCareerStartDeferredLogged = true;
                    Plugin.Log.LogInfo("[Coordinator] W&L career start selection pending; strategic review deferred until player has a command");
                }
                return;
            }
            _wlCareerStartDeferredLogged = false;

            int playerAlliance = ResolvePlayerAlliance();
            bool operationalRuntimeReady = OperationalRuntimeReady();
            if (!operationalRuntimeReady && !_operationalRuntimeDeferredLogged)
            {
                _operationalRuntimeDeferredLogged = true;
                Plugin.Log.LogInfo("[Coordinator] operational ledgers deferred until AICampaign factions initialize");
            }

            if (operationalRuntimeReady)
                mapMs += Measure(() => UpdateCampaignMapLedger(day, logHeartbeat));

            for (int alliance = 0; alliance < 2; alliance++)
            {
                if (IsPlayerCICOf(alliance, playerAlliance))
                {
                    OnceLog.Info("playerciconly:" + alliance,
                        $"player is CIC of alliance {alliance} ({(alliance == 1 ? "CSA" : "Union")}) — mod stands down for that faction");
                    CICs[alliance] = null;
                    continue;
                }
                if (CICs[alliance] == null) CICs[alliance] = BuildCICForAlliance(alliance);

                var era = Eras[alliance];
                var ws = ObserveWarStateCached(day, month, year);
                era.CheckTransition(month, year, ws.VicksburgFallen, ws.AtlantaThreatened);

                var fired = Succession.CheckEvents(BuildSchedulerView(month, year, alliance, ws));
                foreach (var e in fired)
                {
                    if (e.AllianceId != alliance) continue;
                    SwapOfficer(alliance, e);
                    if (CICs[alliance].ActivePlan != null)
                        CICs[alliance].ActivePlan.IsDirty = true;
                }

                var cic = CICs[alliance];
                var phaseTruth = BuildPhaseTruth(cic, alliance, day, month, year);
                if (!cic.ReviewPlanWithTruth(month, year, phaseTruth))
                    cic.Replan(era, month, year);

                fiscalMs += Measure(() => UpdateFiscalIntent(alliance, era.Stage, day, month, year, logHeartbeat));
                bool frontChanged = false;
                if (operationalRuntimeReady)
                    frontMs += Measure(() => frontChanged = UpdateFrontLedger(alliance, cic));

                defenseMs += Measure(() => UpdateDefenseIntent(alliance, era.Stage, day, month, year));

                bool formationChanged = false;
                if (operationalRuntimeReady)
                {
                    string formationSource = FormationSourceSignature(alliance, cic);
                    bool runFormation = StrategicCadencePolicy.ShouldRunAlternatingByAlliance(day, alliance) ||
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
                    if (StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(day, armyAreaSource, _armyAreaSourceSignatures[alliance], formationChanged))
                    {
                        armyAreaMs += Measure(() => UpdateArmyAreaLedger(alliance, cic));
                        _armyAreaSourceSignatures[alliance] = armyAreaSource;
                    }
                }

                string constructionSource = ConstructionSourceSignature(alliance);
                if (StrategicCadencePolicy.ShouldRunWeeklyOrSourceChanged(day, constructionSource, _constructionSourceSignatures[alliance], false))
                {
                    constructionMs += Measure(() => UpdateConstructionIntent(alliance, era.Stage, logHeartbeat));
                    _constructionSourceSignatures[alliance] = constructionSource;
                }

                if (Plugin.Instance.VerboseLogging.Value && !logHeartbeat)
                    Plugin.Log.LogInfo($"[DailyOps] {year}-{month:D2}-{day:D2} alliance={alliance}");

                if (logHeartbeat)
                {
                    Plugin.Log.LogInfo(
                        $"[Heartbeat] {year}-{month:D2} alliance={alliance} " +
                        $"era={era.Stage} cic={cic.OfficerName ?? "<none>"} " +
                        $"plan={(cic.ActivePlan == null ? "<none>" : $"phase{cic.ActivePlan.CurrentPhaseIndex + 1}/{cic.ActivePlan.Phases.Count} obj={cic.ActivePlan.CurrentPhase?.TargetObjectiveId}")} " +
                        $"succession_fired={Succession.FiredEventIds.Count}");
                }
            }

            totalWatch.Stop();
            if (ShouldLogStrategicPerf(totalWatch.Elapsed.TotalMilliseconds))
            {
                Plugin.Log.LogInfo(
                    $"[DailyOps:Perf] {year}-{month:D2}-{day:D2} " +
                    $"totalMs={totalWatch.Elapsed.TotalMilliseconds:F2} " +
                    $"mapMs={mapMs:F2} frontMs={frontMs:F2} armyAreaMs={armyAreaMs:F2} " +
                    $"formationMs={formationMs:F2} probeMs={probeMs:F2} fiscalMs={fiscalMs:F2} " +
                    $"constructionMs={constructionMs:F2} defenseMs={defenseMs:F2}");
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
        // the deadline-only ReviewPlan path on null).
        private PhaseTruthOutput BuildPhaseTruth(CIC cic, int alliance, int day, int month, int year)
        {
            if (cic?.ActivePlan?.CurrentPhase == null) return null;
            var phase = cic.ActivePlan.CurrentPhase;
            var fronts = alliance < Fronts.Length ? Fronts[alliance] : null;
            var targetPos = ObjectiveAdapter.ResolveObjectivePosition(phase.TargetObjectiveId);
            string sectorKey = targetPos.HasValue ? FrontSectorRuntime.SectorKey(targetPos.Value) : null;
            var sector = fronts?.GetSector(sectorKey);
            int daySerial = year * 372 + month * 31 + day;
            bool engagedRecently = false;
            if (targetPos.HasValue)
            {
                foreach (var _ in BattleHistoryQuery.Near(
                    BattleHistory,
                    targetPos.Value,
                    GamePrefs.aimaximumdistancetosearchforunitrelocations,
                    daySerial,
                    14))
                {
                    engagedRecently = true; break;
                }
            }
            var input = new PhaseTruthInput
            {
                Plan                     = cic.ActivePlan,
                TargetAccomplished       = ObjectiveAdapter.IsAccomplished(phase.TargetObjectiveId),
                ObjectiveAvailable       = ObjectiveAdapter.IsAvailable(phase.TargetObjectiveId, alliance),
                TargetPositionResolves   = targetPos.HasValue,
                TargetEngagedRecently    = engagedRecently,
                TargetSectorOwnStrength  = sector?.OwnStrength ?? 0f,
                RequiredForce            = phase.ForceFractionRequired *
                                           ((sector?.OwnStrength ?? 0f) + (sector?.EnemyStrength ?? 0f)),
                CurrentMonth             = month,
                CurrentYear              = year
            };
            return PhaseTruthLedger.Evaluate(input);
        }

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

        private void UpdateCampaignMapLedger(int day, bool logHeartbeat)
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
                        forceRefresh: false);
                if (!fullRefresh) return;

                var ledger = CampaignMapRuntime.Build();
                CampaignMap = ledger;
                _campaignMapDynamicSignature = dynamicSignature;
                if (ledger.Signature != _campaignMapSignature)
                {
                    Plugin.Log.LogInfo("[CampaignMap] " + ledger.Summary());
                    _campaignMapSignature = ledger.Signature;
                }
                else if (logHeartbeat && Plugin.Instance != null && Plugin.Instance.VerboseLogging.Value)
                {
                    Plugin.Log.LogInfo("[CampaignMap] " + ledger.Summary());
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-map:update", "[CampaignMap] update failed: " + ex.Message);
            }
        }

        private void UpdateConstructionIntent(int alliance, EraStage era, bool logHeartbeat)
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
                    Plugin.Log.LogInfo(
                        $"[ConstructionIntent] alliance={alliance} posture={output.Posture} " +
                        $"theater={output.TopConstructionTheater ?? ""} " +
                        $"private={output.TopPrivateBuilding.Name} depot={output.TopSupplyDepot.Name} " +
                        $"fort={output.TopFort.Name} rail={output.TopRailroad.Name}");
                    _constructionSignatures[alliance] = output.Signature;
                }

                if (logHeartbeat && ConfigValue(plugin.ConstructionTelemetryEnabled, defaultValue: true))
                {
                    var telemetry = ConstructionTelemetry ?? (ConstructionTelemetry = new ConstructionTelemetry());
                    Plugin.Log.LogInfo(
                        $"[ConstructionTelemetry] alliance={alliance} posture={output.Posture} " +
                        $"{telemetry.Summary(alliance)}");
                }

                if (ConfigValue(plugin.EnableTelegraphAI))
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
                    profile: profile,
                    previousSifSignatures: _previousSifSignatures[alliance]);

                var output = DefenseIntentLedger.Build(input);
                DefenseIntents[alliance] = output;

                // Refresh the per-alliance "seen this tick" SIF set so the NEXT
                // tick can detect collapsed entries via VanillaCollapsed. Source
                // from input.Threats — the runtime's observation of vanilla
                // seainvasionforce is the ground truth, not the builder output
                // (which may filter Recovered/expired entries before they reach
                // output.Responses).
                _previousSifSignatures[alliance] = CollectSifSignatures(input);

                bool verbose = ConfigValue(plugin.VerboseLogging) || ConfigValue(plugin.DefenseIntentVerboseLogging);
                if (verbose)
                {
                    if (output.Responses != null)
                    {
                        foreach (var r in output.Responses)
                        {
                            Plugin.Log.LogInfo(
                                $"[DefenseIntent] alliance={alliance} posture={r.Threat.Posture} " +
                                $"threat={r.Threat.Signature} enemy={r.Threat.EnemyStrength:F0} " +
                                $"desired={r.Threat.DesiredStrength:F0} selected={r.SelectedPackage.Count} " +
                                $"reason={r.Threat.EscalationReason ?? ""} sig={r.TelemetrySignature ?? ""}");
                        }
                    }
                    _defenseIntentSignatures[alliance] = output.Signature;
                }
                else if (_defenseIntentSignatures[alliance] != output.Signature)
                {
                    Plugin.Log.LogInfo(
                        $"[DefenseIntent] alliance={alliance} {DefenseIntentTelemetry.Summary(output)}");
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

        private static HashSet<string> CollectSifSignatures(DefenseIntentInput input)
        {
            var set = new HashSet<string>();
            if (input == null || input.Threats == null) return set;
            foreach (var t in input.Threats)
            {
                if (t == null) continue;
                if (t.Kind != DefenseThreatSourceKind.SeaInvasion) continue;
                string sig = DefenseThreatSignature.ForSeaInvasion(
                    t.InvasionForceInstanceId, t.SpotName, t.SourcePortName);
                set.Add(sig);
            }
            return set;
        }

        private void UpdateFiscalIntent(int alliance, EraStage era, int day, int month, int year, bool logHeartbeat)
        {
            var input = FiscalRuntime.BuildInput(alliance, era, _fiscalMemory[alliance]);
            var options = new FiscalOptions();
            var output = FiscalIntentLedger.Compute(input, options);
            FiscalIntents[alliance] = output;
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

            if (Plugin.Instance.VerboseLogging.Value || Plugin.Instance.FiscalTrace.Value || _fiscalSignatures[alliance] != output.Signature)
            {
                Plugin.Log.LogInfo($"[FiscalIntent] alliance={alliance} posture={output.Posture} gate={output.DefendedGate} supply={output.SupplyProtection} forceCap={output.ForceCapWarning}");
                _fiscalSignatures[alliance] = output.Signature;
            }

            if (logHeartbeat)
            {
                Plugin.Log.LogInfo($"[FiscalTelemetry] alliance={alliance} posture={output.Posture} gate={output.DefendedGate} supply={output.SupplyProtection} theater={output.TheaterSupplyPriority}");
            }
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
                Plugin.Log.LogInfo($"[FrontLedger] alliance={alliance} {FrontSectorRuntime.Summary(ledger)}");
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
                Plugin.Log.LogInfo($"[ArmyArea] alliance={alliance} {signature}");
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
                Plugin.Log.LogInfo(
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
                    BattleHistory);

                var posture = DirectorMemories[alliance]?.LastPosture;
                if (posture != null)
                    StrategicResilienceDirector.ApplyTo(input.Options, posture);

                var output = OperationalProbeLedger.Build(input);
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
                    Plugin.Log.LogInfo(
                        $"[OperationalProbe] alliance={alliance} decision={output.Decision} " +
                        $"unit={output.SelectedUnitKey ?? "<none>"} target={output.TargetAreaKey ?? "<none>"} " +
                        $"reason={output.Reason ?? ""} mass={output.RequiresMassCommitment}");
                    _operationalProbeSignatures[alliance] = signature;
                }

                if (overlayChanged)
                {
                    string formationSignature = formation.Summary();
                    _formationDirectiveSignatures[alliance] = formationSignature;
                    Plugin.Log.LogInfo($"[FormationDirective] alliance={alliance} operationalProbe={formationSignature}");
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

        public void OnEventTrigger(int allianceId, string eventType)
        {
            if (allianceId < 0 || allianceId >= CICs.Length) return;
            var cic = CICs[allianceId];
            if (cic?.ActivePlan == null) return;

            cic.ActivePlan.IsDirty = true;
            Plugin.Log.LogInfo($"[Coordinator] event '{eventType}' for alliance {allianceId} — plan marked dirty");
        }

        internal void RecordBattleOutcome(BattleHistoryRecord record)
        {
            if (record == null || record.AllianceWon < 0) return;

            foreach (var existing in BattleHistory)
                if (existing.SameBattleKey(record)) return;

            BattleHistory.Add(record);
            while (BattleHistory.Count > 64) BattleHistory.RemoveAt(0);

            string scale = record.IsMajorResult ? "major" : "minor";
            Plugin.Log.LogInfo(
                $"[BattleHistory] {record.Year}-{record.Month:D2}-{record.Day:D2} " +
                $"{record.BattleName} winner={record.AllianceWon} loser={record.LosingAlliance} " +
                $"result={scale} theater={record.Theater}");

            OnEventTrigger(record.AllianceWon, "battle_result");
            OnEventTrigger(record.LosingAlliance, "battle_result");
        }

        internal void RecordConstructionStart(ConstructionStartEvent start)
        {
            var telemetry = ConstructionTelemetry ?? (ConstructionTelemetry = new ConstructionTelemetry());
            telemetry.Record(start);

            if (ConstructionVerboseLoggingEnabled())
            {
                Plugin.Log.LogInfo(
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

        public static bool IsPlayerCICOf(int allianceId, int playerAlliance)
        {
            if (allianceId != playerAlliance) return false;
            try
            {
                var dlcType = AccessTools.TypeByName("DLC_WL");
                if (dlcType == null) return false;
                var scenarioActive = (bool)AccessTools.Field(dlcType, "dlc_scenarioactive").GetValue(null);
                if (!scenarioActive) return false;
                // Vanilla signature: public static bool IsCommanderInChief(int manualcommander = -1)
                // (decompile line 47475). Pass -1 to use the default behavior (check dlc_chosencommander).
                var isCIC = AccessTools.Method(dlcType, "IsCommanderInChief", new[] { typeof(int) });
                if (isCIC == null) return false;
                return (bool)isCIC.Invoke(null, new object[] { -1 });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Coordinator] IsPlayerCICOf failed: " + ex.Message);
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
            if (snap.VicksburgFallen   && !_loggedVicksburg)   { Plugin.Log.LogInfo($"[WarState] Vicksburg fell ({year}-{month:D2})");   _loggedVicksburg = true; }
            if (snap.ChattanoogaFallen && !_loggedChattanooga) { Plugin.Log.LogInfo($"[WarState] Chattanooga fell ({year}-{month:D2})"); _loggedChattanooga = true; }
            if (snap.AtlantaThreatened && !_loggedAtlanta)     { Plugin.Log.LogInfo($"[WarState] Atlanta fell ({year}-{month:D2})");     _loggedAtlanta = true; }

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
            Plugin.Log.LogInfo($"[Succession:{e.Id}] FIRED — {e.Name}, replacing role={e.ReplacedRole} with={e.ReplacementName}");
        }

        public void SaveSidecar(string fullPath)
        {
            try
            {
                var dto = BuildDto();
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(dto, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(fullPath, json);
                Plugin.Log.LogInfo("[Coordinator] sidecar written: " + fullPath);
            }
            catch (Exception ex) { Plugin.Log.LogError("[Coordinator] sidecar save failed: " + ex); }
        }

        public void LoadSidecar(string fullPath)
        {
            try
            {
                var json = System.IO.File.ReadAllText(fullPath);
                var dto = Newtonsoft.Json.JsonConvert.DeserializeObject<SidecarDto>(json);
                if (dto == null) { InitializeFromGameState(); return; }
                ApplyDto(dto);
                Initialized = true;
                Plugin.Log.LogInfo("[Coordinator] sidecar loaded: " + fullPath);
                OnceLog.Reset();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Coordinator] sidecar load failed (falling back to fresh init): " + ex.Message);
                InitializeFromGameState();
            }
        }

        private SidecarDto BuildDto()
        {
            var dto = new SidecarDto();
            for (int alliance = 0; alliance < 2; alliance++)
            {
                if (CICs[alliance] == null) continue;
                var f = new FactionDto
                {
                    FactionId   = alliance,
                    FactionName = (alliance == 1) ? "CSA" : "Union",
                    CurrentEra  = Eras[alliance].Stage.ToString(),
                    Cic = new CICDto
                    {
                        OfficerName = CICs[alliance].OfficerName,
                        Personality = PersonalityDto.FromVector(CICs[alliance].OfficerPersonality),
                        ActivePlan  = (CICs[alliance].ActivePlan != null) ? PlanToDto(CICs[alliance].ActivePlan) : null
                    }
                };
                f.DirectorMemory = StrategicResilienceDirector.MemoryToDto(DirectorMemories[alliance]);
                dto.Factions.Add(f);
            }
            foreach (var kv in MinorOfficerProfiles)
            {
                dto.MinorOfficerProfiles.Add(new MinorOfficerDto
                {
                    CommanderId = kv.Key,
                    Personality = PersonalityDto.FromVector(kv.Value)
                });
            }
            dto.Succession.FiredEvents   = new List<int>(Succession.FiredEventIds);
            dto.Succession.AppliedEvents = new List<int>(Succession.AppliedEventIds);
            dto.Succession.LastChecked   = LastSeenYear + "-" + LastSeenMonth.ToString("D2") + "-01";
            foreach (var battle in BattleHistory)
            {
                var battleDto = BattleToDto(battle);
                if (battleDto != null) dto.BattleHistory.Add(battleDto);
            }
            return dto;
        }

        private void ApplyDto(SidecarDto dto)
        {
            for (int alliance = 0; alliance < 2; alliance++)
            {
                Eras[alliance] = Eras[alliance] ?? new EraStageManager();
            }
            foreach (var f in dto.Factions)
            {
                if (f.FactionId < 0 || f.FactionId >= 2) continue;
                if (Enum.TryParse<EraStage>(f.CurrentEra, out var era)) Eras[f.FactionId].Stage = era;
                var cic = new CIC
                {
                    AllianceId         = f.FactionId,
                    OfficerName        = f.Cic?.OfficerName,
                    OfficerPersonality = f.Cic?.Personality?.ToVector() ?? default(PersonalityVector),
                    ActivePlan         = (f.Cic?.ActivePlan != null) ? PlanFromDto(f.Cic.ActivePlan, f.FactionId) : null
                };
                CICs[f.FactionId] = cic;
                DirectorMemories[f.FactionId] = StrategicResilienceDirector.MemoryFromDto(f.DirectorMemory);
            }
            MinorOfficerProfiles.Clear();
            foreach (var m in dto.MinorOfficerProfiles)
                MinorOfficerProfiles[m.CommanderId] = m.Personality.ToVector();
            Succession.FiredEventIds   = new HashSet<int>(dto.Succession.FiredEvents);
            Succession.AppliedEventIds = new HashSet<int>(dto.Succession.AppliedEvents ?? new List<int>());
            BattleHistory.Clear();
            if (dto.BattleHistory != null)
            {
                foreach (var battle in dto.BattleHistory)
                {
                    var record = BattleFromDto(battle);
                    if (record != null) BattleHistory.Add(record);
                }
                while (BattleHistory.Count > 64) BattleHistory.RemoveAt(0);
            }
        }

        private BattleHistoryDto BattleToDto(BattleHistoryRecord record)
        {
            if (record == null) return null;
            return new BattleHistoryDto
            {
                BattleName = record.BattleName,
                Day = record.Day,
                Month = record.Month,
                Year = record.Year,
                LandOrSea = record.LandOrSea,
                AllianceWon = record.AllianceWon,
                BattleResultType = record.BattleResultType,
                BattleEndType = record.BattleEndType,
                Theater = record.Theater.ToString(),
                PositionX = record.PositionX,
                PositionZ = record.PositionZ,
                Alliance = new List<int>(record.Alliance),
                Commander = new List<int>(record.Commander),
                CommanderName = new List<string>(record.CommanderName),
                Casualties = new List<int>(record.Casualties),
                CommanderKia = new List<int>(record.CommanderKia)
            };
        }

        private BattleHistoryRecord BattleFromDto(BattleHistoryDto dto)
        {
            if (dto == null) return null;
            var record = new BattleHistoryRecord
            {
                BattleName = dto.BattleName,
                Day = dto.Day,
                Month = dto.Month,
                Year = dto.Year,
                LandOrSea = dto.LandOrSea,
                AllianceWon = dto.AllianceWon,
                BattleResultType = dto.BattleResultType,
                BattleEndType = dto.BattleEndType,
                PositionX = dto.PositionX,
                PositionZ = dto.PositionZ
            };
            if (!Enum.TryParse<Theater>(dto.Theater, out record.Theater))
                record.Theater = Theater.Unknown;
            CopyList(dto.Alliance, record.Alliance);
            CopyList(dto.Commander, record.Commander);
            CopyList(dto.CommanderName, record.CommanderName);
            CopyList(dto.Casualties, record.Casualties);
            if (dto.CommanderKia != null) record.CommanderKia.AddRange(dto.CommanderKia);
            return record;
        }

        private static void CopyList<T>(List<T> source, T[] target)
        {
            if (source == null || target == null) return;
            for (int i = 0; i < source.Count && i < target.Length; i++)
                target[i] = source[i];
        }

        private OperationalPlanDto PlanToDto(OperationalPlan p)
        {
            var dto = new OperationalPlanDto
            {
                AssignedTheaterId = p.AssignedTheaterId,
                CurrentPhaseIndex = p.CurrentPhaseIndex,
                PlanDeadlineMonth = p.PlanDeadlineMonth,
                PlanDeadlineYear  = p.PlanDeadlineYear,
                Rationale         = p.Rationale,
                IsDirty           = p.IsDirty
            };
            foreach (var ph in p.Phases)
                dto.Phases.Add(new PhaseDto
                {
                    TargetAreaId          = ph.TargetAreaId,
                    TargetObjectiveId     = ph.TargetObjectiveId,
                    ForceFractionRequired = ph.ForceFractionRequired,
                    Transition            = ph.Transition.ToString(),
                    DeadlineMonth         = ph.DeadlineMonth,
                    DeadlineYear          = ph.DeadlineYear
                });
            return dto;
        }

        private OperationalPlan PlanFromDto(OperationalPlanDto dto, int allianceId)
        {
            var p = new OperationalPlan
            {
                CICFactionAllianceId = allianceId,
                AssignedTheaterId    = dto.AssignedTheaterId,
                CurrentPhaseIndex    = dto.CurrentPhaseIndex,
                PlanDeadlineMonth    = dto.PlanDeadlineMonth,
                PlanDeadlineYear     = dto.PlanDeadlineYear,
                Rationale            = dto.Rationale,
                IsDirty              = dto.IsDirty
            };
            foreach (var ph in dto.Phases)
            {
                Enum.TryParse<PhaseTransition>(ph.Transition, out var trans);
                p.Phases.Add(new Phase
                {
                    TargetAreaId          = ph.TargetAreaId,
                    TargetObjectiveId     = ph.TargetObjectiveId,
                    ForceFractionRequired = ph.ForceFractionRequired,
                    Transition            = trans,
                    DeadlineMonth         = ph.DeadlineMonth,
                    DeadlineYear          = ph.DeadlineYear,
                    Fallback              = null
                });
            }
            return p;
        }
    }
}
