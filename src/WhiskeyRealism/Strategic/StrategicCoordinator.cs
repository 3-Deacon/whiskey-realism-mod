using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
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
        public FiscalOutput[] FiscalIntents = new FiscalOutput[2];
        public ConstructionOutput[] ConstructionIntents = new ConstructionOutput[2];
        public ConstructionTelemetry ConstructionTelemetry = new ConstructionTelemetry();
        internal SuccessionScheduler Succession = new SuccessionScheduler();
        public Dictionary<int, PersonalityVector> MinorOfficerProfiles = new Dictionary<int, PersonalityVector>();
        internal readonly List<BattleHistoryRecord> BattleHistory = new List<BattleHistoryRecord>();
        private readonly FiscalStateMemory[] _fiscalMemory = new FiscalStateMemory[2]
        {
            new FiscalStateMemory(),
            new FiscalStateMemory()
        };
        private readonly string[] _frontSignatures = new string[2];
        private readonly string[] _armyAreaSignatures = new string[2];
        private readonly string[] _formationDirectiveSignatures = new string[2];
        private readonly string[] _fiscalSignatures = new string[2];
        private readonly WeeklyCadence _operationalCadence = new WeeklyCadence();
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
                OnWeeklyOperationalTick(gameDay, gameMonth, gameYear);
        }

        private bool _forcedAllSuccession;

        public void OnWeeklyOperationalTick(int day, int month, int year)
        {
            try
            {
                OnceLog.Info("weeklyops", "Weekly operational analysis active");

                RunStrategicReview(day, month, year, logHeartbeat: false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[WeeklyOps] tick failed: " + ex.Message);
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
                if (!cic.ReviewPlan(month, year))
                    cic.Replan(era, month, year);

                if (operationalRuntimeReady)
                {
                    UpdateFrontLedger(alliance, cic);
                    UpdateArmyAreaLedger(alliance, cic);
                    UpdateFormationDirectiveLedger(alliance, cic, era);
                }
                UpdateFiscalIntent(alliance, era.Stage, day, month, year, logHeartbeat);

                if (Plugin.Instance.VerboseLogging.Value && !logHeartbeat)
                    Plugin.Log.LogInfo($"[WeeklyOps] {year}-{month:D2}-{day:D2} alliance={alliance}");

                if (logHeartbeat)
                {
                    Plugin.Log.LogInfo(
                        $"[Heartbeat] {year}-{month:D2} alliance={alliance} " +
                        $"era={era.Stage} cic={cic.OfficerName ?? "<none>"} " +
                        $"plan={(cic.ActivePlan == null ? "<none>" : $"phase{cic.ActivePlan.CurrentPhaseIndex + 1}/{cic.ActivePlan.Phases.Count} obj={cic.ActivePlan.CurrentPhase?.TargetObjectiveId}")} " +
                        $"succession_fired={Succession.FiredEventIds.Count}");
                }
            }
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
                _fiscalMemory[alliance].StableWeeksAboveEmergency = 0;
            }
            else if ((_fiscalMemory[alliance].EmergencyResidue ||
                _fiscalMemory[alliance].PreviousPosture == FiscalPosture.CreditDefense) && emergencyRecoveryStable)
            {
                _fiscalMemory[alliance].StableWeeksAboveEmergency++;
            }
            else if (!emergencyRecoveryStable)
            {
                _fiscalMemory[alliance].StableWeeksAboveEmergency = 0;
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

        private void UpdateFrontLedger(int alliance, CIC cic)
        {
            int targetObjectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
            var ledger = FrontSectorRuntime.BuildForAlliance(alliance, targetObjectiveId);
            if (ledger == null)
            {
                OnceLog.Warning(
                    "front-ledger:null:" + alliance,
                    $"[FrontLedger] update skipped: build returned null for alliance={alliance}");
                return;
            }

            Fronts[alliance] = ledger;
            string signature = FrontSectorRuntime.Summary(ledger);
            if (Plugin.Instance.VerboseLogging.Value || _frontSignatures[alliance] != signature)
            {
                Plugin.Log.LogInfo($"[FrontLedger] alliance={alliance} {signature}");
                _frontSignatures[alliance] = signature;
            }
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

        private void UpdateFormationDirectiveLedger(int alliance, CIC cic, EraStageManager era)
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
                return;
            }

            FormationDirectives[alliance] = ledger;
            string signature = ledger.Summary();
            if (Plugin.Instance.VerboseLogging.Value || _formationDirectiveSignatures[alliance] != signature)
            {
                Plugin.Log.LogInfo(
                    $"[FormationDirective] alliance={alliance} summary={signature} " +
                    $"lowSupply={ledger.Pressure.LowSupplyCount} lowAmmo={ledger.Pressure.LowAmmoCount} " +
                    $"recover={ledger.Pressure.RecoverCount} mass={ledger.Pressure.MassCount} " +
                    $"supplyArea={ledger.Pressure.TopSupplyAreaKey ?? "<none>"}");
                _formationDirectiveSignatures[alliance] = signature;
            }
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
            ConstructionTelemetry.Record(start);

            if (ConstructionVerboseLoggingEnabled())
            {
                Plugin.Log.LogInfo(
                    $"[ConstructionStart] alliance={start.AllianceId} kind={start.Kind} " +
                    $"name={start.Name ?? "<unnamed>"} theater={start.Theater} site={start.SiteKey ?? "<none>"}");
            }
        }

        private static bool ConstructionVerboseLoggingEnabled()
        {
            // Task 4 will introduce construction-specific config. Until then,
            // construction start detail follows the existing global verbose flag.
            try
            {
                return Plugin.Instance != null && Plugin.Instance.VerboseLogging.Value;
            }
            catch { return false; }
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
                foreach (var tc in CICs[alliance].Theaters)
                {
                    f.TheaterCommanders.Add(new TheaterCommanderDto
                    {
                        TheaterId   = tc.TheaterId,
                        OfficerName = tc.OfficerName,
                        Personality = PersonalityDto.FromVector(tc.Personality)
                    });
                }
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
                foreach (var tc in f.TheaterCommanders)
                {
                    cic.Theaters.Add(new TheaterCommander
                    {
                        TheaterId   = tc.TheaterId,
                        OfficerName = tc.OfficerName,
                        Personality = tc.Personality?.ToVector() ?? default(PersonalityVector)
                    });
                }
                CICs[f.FactionId] = cic;
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
