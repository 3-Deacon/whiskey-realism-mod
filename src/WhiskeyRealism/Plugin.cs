using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism
{
    [BepInPlugin(GUID, "Whiskey Realism — Strategic AI Overhaul", "0.2.2")]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "dev.kyle.whiskey-realism";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        // Master enable. Setting false short-circuits every patch in the suite.
        internal ConfigEntry<bool> Enabled;

        // Diagnostic logging.
        internal ConfigEntry<bool> VerboseLogging;
        internal ConfigEntry<bool> PlanTrace;
        internal ConfigEntry<bool> SuccessionTrace;
        internal ConfigEntry<bool> FiscalTrace;
        internal ConfigEntry<bool> DirectorVerboseTrace;
        internal ConfigEntry<string> TelemetryLoggingProfileRaw;
        internal ConfigEntry<int> TelemetryMaxTuningLogMb;
        internal ConfigEntry<int> TelemetryFileRotateMb;
        internal ConfigEntry<int> TelemetryRetainedSessions;
        internal ConfigEntry<bool> TelemetryEmitHumanSummary;
        internal ConfigEntry<bool> TelemetryPerformanceWarnings;
        internal ConfigEntry<bool> TelemetryCreateIssueBundleOnShutdown;
        internal ConfigEntry<int> TelemetryQueueCapacity;
        internal ConfigEntry<int> TelemetryFlushMilliseconds;
        internal ConfigEntry<int> TelemetryFlushRows;
        internal ConfigEntry<bool> EnableTacticalObserver;
        internal ConfigEntry<bool> TacticalObserverVerboseLogging;
        internal ConfigEntry<int> TacticalObserverMinSecondsBetweenSummaries;
        internal ConfigEntry<bool> EnableTacticalDecisionMatrixLogging;
        internal ConfigEntry<int> TacticalDecisionMatrixMinSecondsBetweenSnapshots;
        internal ConfigEntry<int> TacticalDecisionMatrixMaxRows;
        internal ConfigEntry<bool> EnableTacticalBugTelemetry;
        internal ConfigEntry<bool> EnableTacticalFallbackRetreatNullGuard;
        internal ConfigEntry<bool> EnableTacticalPathfinderDiscipline;
        internal ConfigEntry<bool> EnableTacticalHqLinkGuard;
        internal ConfigEntry<bool> EnableTacticalReserveOrderDelayGuard;
        internal ConfigEntry<bool> EnableWlTacticalChargeGuard;
        internal ConfigEntry<bool> EnableWlOperationNullGuard;
        internal static ConfigEntry<bool> EnableWlAttritionScopeFix;
        internal static ConfigEntry<bool> EnableWlBuySuppliesFix;
        internal static ConfigEntry<bool> EnableWlCareerTransferFix;
        internal static ConfigEntry<bool> EnableWlStaffScopeFix;
        internal static ConfigEntry<bool> EnableWlStaffPanelScopeFix;
        internal static ConfigEntry<bool> EnableWlSupplyEfficiencySignFix;
        internal static ConfigEntry<bool> EnableWlActionOnceEffectsFix;
        internal static ConfigEntry<bool> EnableBattleEntrenchmentAverageFix;
        internal static ConfigEntry<bool> EnableBattleHqConditionFix;
        internal static ConfigEntry<bool> EnableFlankResponseAndEnvelopment;
        internal static ConfigEntry<bool> EnableVanillaFlankThreatTallyFix;
        internal static ConfigEntry<bool> EnableBattleTerrainPicture;
        internal static ConfigEntry<bool> EnableBattleAiLevelOfDetail;
        internal static ConfigEntry<bool> EnablePerCommanderPersonality;
        internal static ConfigEntry<bool> EnableCommanderOpportunity;
        internal static ConfigEntry<bool> EnableDefenderCounterstroke;
        internal static ConfigEntry<bool> EnableStaggeredReserveRelease;
        internal static ConfigEntry<bool> EnableCavalryStrikeDiscipline;
        internal static ConfigEntry<bool> EnableVanillaStanceCooldownFix;
        internal static ConfigEntry<bool> EnableSeparatedEnemySteer;
        internal static ConfigEntry<bool> EnableVanillaReserveSupportPickFix;
        internal static ConfigEntry<bool> EnableVanillaArtilleryChainSideFix;
        internal static ConfigEntry<bool> EnableVanillaMacroAssaultUpgradeFix;
        internal static ConfigEntry<bool> EnableManeuverFeasibility;
        internal static ConfigEntry<bool> EnableEnemyWorksDoctrine;
        internal static ConfigEntry<bool> EnableObjectiveDoctrine;
        internal static ConfigEntry<bool> EnableCrossingMoveGate;

        /// <summary>Tactical / Enable Crossing Move Gate (a doctrine move whose straight leg crosses a river takes a crossing the side knows, or is not made).</summary>
        internal static bool CrossingMoveGateEnabled()
        {
            try { return EnableCrossingMoveGate != null && EnableCrossingMoveGate.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Battle Terrain Picture (BC12 decisions 3-6, the strong-ground rule of 7, the crest playbooks of 8).</summary>
        internal static bool TerrainPictureEnabled()
        {
            try { return EnableBattleTerrainPicture != null && EnableBattleTerrainPicture.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Battle AI Level Of Detail (BC15: the tier pass, the carried claims and the id-ordered nav walk).</summary>
        internal static bool BattleAiLevelOfDetailEnabled()
        {
            try { return EnableBattleAiLevelOfDetail != null && EnableBattleAiLevelOfDetail.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Per-Commander Personality (BC13 section 4: the commander table, the derived army vector, the attack-threshold band, the casualty threshold's basis).</summary>
        internal static bool PerCommanderPersonalityEnabled()
        {
            try { return EnablePerCommanderPersonality != null && EnablePerCommanderPersonality.Value; }
            catch { return false; }
        }

        /// <summary>The plugin's master switch (Enabled), false before the plugin loaded.</summary>
        internal static bool MasterEnabled()
        {
            try { return Instance != null && Instance.Enabled.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Commander Opportunity (BC13 sections 5-6: the opening detector, the per-commander decision and the exploit override).</summary>
        internal static bool CommanderOpportunityEnabled()
        {
            try { return EnableCommanderOpportunity != null && EnableCommanderOpportunity.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Staggered Reserve Release (BC13: one reserve per release interval above a floor; one per crisis sector).</summary>
        internal static bool StaggeredReserveReleaseEnabled()
        {
            try { return EnableStaggeredReserveRelease != null && EnableStaggeredReserveRelease.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Maneuver Feasibility (BC14 core: the feasibility evaluation, the selection term, the synchronized release, the monitor, the crossing look and the Defender's crossing guard).</summary>
        internal static bool ManeuverFeasibilityEnabled()
        {
            try { return EnableManeuverFeasibility != null && EnableManeuverFeasibility.Value; }
            catch { return false; }
        }

        /// <summary>
        /// Tactical / Enable Enemy Works Doctrine (BC14W: the works picture, turn-first pricing, the works hold, FocalAssault with the bombard start,
        /// the Defender's works station). True only with Enable Maneuver Feasibility also on: the slice reads the core's ledger.
        /// </summary>
        internal static bool EnemyWorksDoctrineEnabled()
        {
            try { return ManeuverFeasibilityEnabled() && EnableEnemyWorksDoctrine != null && EnableEnemyWorksDoctrine.Value; }
            catch { return false; }
        }

        /// <summary>
        /// Tactical / Enable Objective Doctrine (BC14O: an objective's value from vanilla's own points, the victory clock, one main effort on the
        /// best feasible objective with screens for the others, the designer chains as a timed prior). True only with Enable Maneuver Feasibility
        /// also on: the slice reads the core's ledger.
        /// </summary>
        internal static bool ObjectiveDoctrineEnabled()
        {
            try { return ManeuverFeasibilityEnabled() && EnableObjectiveDoctrine != null && EnableObjectiveDoctrine.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Cavalry Strike Discipline (#120: vanilla's cavalry charge target is kept only when the side perceives it, and it is worth charging).</summary>
        internal static bool CavalryStrikeDisciplineEnabled()
        {
            try { return EnableCavalryStrikeDiscipline != null && EnableCavalryStrikeDiscipline.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Vanilla Stance Cooldown Fix (#119: vanilla's own stance change holds its group for the game-hour the battle prefs promise).</summary>
        internal static bool VanillaStanceCooldownFixEnabled()
        {
            try { return EnableVanillaStanceCooldownFix != null && EnableVanillaStanceCooldownFix.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Separated Enemy Steer (#121: vanilla's omniscient isolated-target move does not also send Whiskey-commanded groups).</summary>
        internal static bool SeparatedEnemySteerEnabled()
        {
            try { return EnableSeparatedEnemySteer != null && EnableSeparatedEnemySteer.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Defender Counterstroke (BC13: the Penetration opening and the Defender's bounded local counterstroke).</summary>
        internal static bool DefenderCounterstrokeEnabled()
        {
            try { return EnableDefenderCounterstroke != null && EnableDefenderCounterstroke.Value; }
            catch { return false; }
        }

        /// <summary>Tactical / Enable Flank Response And Envelopment (BC11 decisions 1, 3, 4).</summary>
        internal static bool FlankResponseEnabled()
        {
            try { return EnableFlankResponseAndEnvelopment != null && EnableFlankResponseAndEnvelopment.Value; }
            catch { return false; }
        }
        internal ConfigEntry<bool> EnableTacticalMacroStanceScorer;
        internal ConfigEntry<bool> EnableTacticalGroupSectorStance;
        internal ConfigEntry<bool> EnableTacticalCommanderIntentDoctrine;
        internal ConfigEntry<bool> EnableTacticalLocalReactionDoctrine;
        internal ConfigEntry<bool> EnableTacticalChargeDenial;
        internal ConfigEntry<bool> EnableTacticalReserveIntentTelemetry;
        internal static ConfigEntry<bool> EnablePlayerOrderDoctrine { get; private set; }
        internal static ConfigEntry<bool> EnablePlayerOrderDoctrineDiagnostics { get; private set; }
        internal static ConfigEntry<bool> EnableCampaignChainDispatches { get; private set; }
        internal static ConfigEntry<bool> EnablePlayerComplianceLedger { get; private set; }
        internal static ConfigEntry<bool> EnableWlOrderReadmit { get; private set; }
        internal static ConfigEntry<bool> EnableWlOrderCouriers { get; private set; }
        internal static ConfigEntry<bool> EnableWlOrderText { get; private set; }
        internal static ConfigEntry<bool> FixCapitalDefensePlayerOrder { get; private set; }
        internal static ConfigEntry<bool> EnableWlOrderFollowUp { get; private set; }
        internal static ConfigEntry<bool> EnableWlEnemyRetreatGuard { get; private set; }
        internal static ConfigEntry<bool> FixLostOrdersFogGrid { get; private set; }
        internal static ConfigEntry<bool> EnableCicFactionAiBugGuard { get; private set; }
        public static ConfigEntry<bool> EnableTacticalArtilleryDoctrine;
        public static ConfigEntry<bool> EnableTacticalWithdrawalDoctrine;
        public static ConfigEntry<bool> EnableTacticalBattleOrchestrator;
        public static ConfigEntry<bool> EnableTacticalOrchestratorArmy;
        public static ConfigEntry<int> TacticalOrchestratorMinReplanSeconds;
        public static ConfigEntry<bool> EnableTacticalOrchestratorIntentInference;
        public static ConfigEntry<bool> EnableTacticalOrchestratorDirectChildGate;
        internal ConfigEntry<string> TacticalCommanderModeRaw;
        internal ConfigEntry<bool> EnableTacticalOrchestratorReserveCommitGate;
        public static ConfigEntry<bool> EnableTacticalRegimentDiagnostics;
        public static ConfigEntry<string> TacticalRegimentDiagnosticNames;
        public static ConfigEntry<bool> EnableTacticalMapKnowledgeDiagnostics;
        public static ConfigEntry<bool> EnableTacticalContactFacingPulse;
        public static ConfigEntry<bool> EnableColumnDeployment;
        public static ConfigEntry<bool> EnableApproachLineAdvance;
        public static ConfigEntry<bool> EnableTacticalReinforcementOpportunityDoctrine;
        public static ConfigEntry<bool> EnableTacticalDeploymentObserver;
        public static ConfigEntry<bool> EnableTacticalDeploymentTerrainDiscipline;
        public static ConfigEntry<bool> EnableVanillaMacroAiSync;
        public static ConfigEntry<bool> EnableGeometricPlayTemplates;
        public static ConfigEntry<bool> EnableBattleFogHonestPerception;
        public static ConfigEntry<bool> EnableInfantryChargeDoctrine;
        public static ConfigEntry<bool> EnableExperienceAndDrillAiWeighting;
        public static ConfigEntry<bool> EnableBattleReconnaissance;
        public static ConfigEntry<bool> EnableDeploymentAndMarchDoctrine;
        public static ConfigEntry<float> OpportunityHysteresisMinHoldSeconds;
        public static ConfigEntry<float> BrigadeTaskHoldSeconds;
        public static ConfigEntry<bool> EnableArtilleryAllStanceAudit;
        public static ConfigEntry<float> ArtilleryAuditMinHoldSeconds;
        public static ConfigEntry<float> TacticalDeploymentTerrainMaxCorrectionMeters;
        public static ConfigEntry<int> TacticalDeploymentTerrainMaxCandidates;
        public static ConfigEntry<float> TacticalDeploymentFacingPreferredDeltaDegrees;
        internal ConfigEntry<bool> EnableTacticalHeavyPathThrottling;
        internal ConfigEntry<float> TacticalHeavyReviewCycleHours;
        internal ConfigEntry<float> TacticalHeavyReviewMinRealtimeSeconds;
        internal ConfigEntry<bool> EnableConstructionIntentLedger;
        internal ConfigEntry<bool> EnableNeedDrivenPolicySelection;
        internal ConfigEntry<bool> EnableDefenseIntentLedger;
        internal ConfigEntry<bool> DefenseIntentVerboseLogging;
        internal ConfigEntry<bool> EnableConstructionSiteSteering;
        internal ConfigEntry<bool> FortConstructionGovernorEnabled;
        internal ConfigEntry<bool> EnableTelegraphAI;
        internal ConfigEntry<bool> ConstructionTelemetryEnabled;
        internal ConfigEntry<bool> ConstructionVerboseLogging;
        internal ConfigEntry<int> MaxActiveTelegraphConstructionsPerFaction;
        public static ConfigEntry<bool> EnableArtilleryCombineGunSync;
        public static ConfigEntry<bool> EnableRaidSupplyThresholdFix;
        public static ConfigEntry<bool> EnableFoggedAiCampFix;
        public static ConfigEntry<bool> EnableFoggedAiCampPlacementBudget;
        public static ConfigEntry<int> FoggedAiCampPlacementsPerFrame;
        public static ConfigEntry<bool> EnableFoggedAiEmbarkationFix;
        public static ConfigEntry<bool> EnableHiddenTransportFlagDeferral;
        public static ConfigEntry<bool> EnableOrphanCampCleanup;
        public static ConfigEntry<bool> EnableAiAreaMembershipRefresh;
        public static ConfigEntry<bool> EnableWinterOffensiveModifierFix;
        public static ConfigEntry<bool> EnableSupplyDepotBuilderFix;
        public static ConfigEntry<bool> EnableSubordinateStrengthGrabFix;
        public static ConfigEntry<bool> EnableOffensiveWeaponTrimFix;
        public static ConfigEntry<bool> EnableAiRandomCandidatePickFix;
        public static ConfigEntry<bool> EnableConfiscationSupplyFlowFix;
        public static ConfigEntry<bool> ReuseCampaignAiUnitList;
        public static ConfigEntry<bool> SkipDeadWlUnitNameRead;
        public static ConfigEntry<bool> EnableAutoresolveStandardGunFix;
        public static ConfigEntry<bool> EnableCombineWeaponStockFix;
        public static ConfigEntry<bool> EnableAiWeaponPurchaseOrderableFix;
        public static ConfigEntry<bool> EnableGroupWeaponStrengthGuard;
        public static ConfigEntry<bool> EnableWlWeaponBudgetScopeFix;
        public static ConfigEntry<bool> EnableWlWeaponBudgetPrestigeGateFix;
        internal static ConfigEntry<bool> FixPerkIdMismatches;
        internal static ConfigEntry<bool> FixPerkEffectScope;
        internal static ConfigEntry<bool> EnableCampaignPlanner;
        internal static ConfigEntry<bool> EnableCampaignPlannerExecutors;
        internal static ConfigEntry<bool> EnableVanillaDefensiveInterception;
        internal static ConfigEntry<bool> EnableStrategicForesightPicture;
        internal static ConfigEntry<bool> EnableSpottingMarkBeliefs;
        internal static ConfigEntry<bool> FixSpottingMarkTiming;
        internal static ConfigEntry<bool> EnableCoveringForce;
        internal static ConfigEntry<bool> EnableMainEffortCommitment;
        internal static ConfigEntry<bool> EnableDepartmentReinforcement;
        internal static ConfigEntry<bool> EnableCampaignReconnaissance;
        internal static ConfigEntry<bool> EnableCampaignFogHonestBeliefs;
        internal static ConfigEntry<bool> EnableCommanderAppointmentPlanner;
        internal static ConfigEntry<bool> EnableSupplyDepotSteering;
        internal static ConfigEntry<bool> EnableFortSteering;
        internal static ConfigEntry<bool> EnableRailroadSteering;
        internal static ConfigEntry<bool> EnableGarrisonManning;
        internal static ConfigEntry<bool> EnableRearmPriority;
        internal static ConfigEntry<bool> EnableOrdnancePurchaseSteering;
        internal static ConfigEntry<bool> FixAiPerkPickRange;
        internal static ConfigEntry<bool> FixPontoonTrainCreekSpeed;
        internal static ConfigEntry<bool> FixEngineerBranchBonus;
        internal static ConfigEntry<bool> FixGoodGroundCoverCap;
        internal static ConfigEntry<bool> FixAutoresolveEntrenchmentSide;
        internal static ConfigEntry<bool> FixPlayerEntrenchmentPoints;
        internal ConfigEntry<bool> FastForwardAiCatchUp;
        internal ConfigEntry<float> FastForwardAiFrameBudgetMs;
        internal ConfigEntry<int> FastForwardAi20xExtraPasses;
        internal ConfigEntry<int> FastForwardAi50xExtraPasses;
        internal ConfigEntry<float> FastForwardAiSlowFrameThresholdMs;
        internal ConfigEntry<int> FastForwardAiSlowFrameCooldownFrames;
        internal ConfigEntry<bool> TimeSliceDailyReview;
        internal ConfigEntry<float> DailyReviewFrameBudgetMs;
        internal ConfigEntry<bool> CampaignAiGovernorEnabled;
        internal ConfigEntry<int> CampaignAiGovernorMaxPasses20x;
        internal ConfigEntry<int> CampaignAiGovernorMaxPasses50x;
        internal ConfigEntry<float> CampaignAiGovernorFrameBudgetMs;
        internal ConfigEntry<bool> EnableWlCampAccountingFix;
        internal ConfigEntry<bool> EnableWlCampRestRewardCap;
        internal ConfigEntry<float> WlCampRestNeutralHours;
        internal ConfigEntry<float> WlCampRestMaxRewardHours;
        internal ConfigEntry<bool> EnableWlCampResponsiveBonusWeighting;
        internal ConfigEntry<int> WlCampRecentBonusWindowDays;
        internal ConfigEntry<float> WlCampRecentBonusWeight;
        internal ConfigEntry<bool> EnableWlCampUnitPayoffTuning;
        internal ConfigEntry<float> WlCampUnitEffectDivisorPower;
        internal ConfigEntry<bool> EnableWlCampVerboseTrace;

        // Vanilla-settings override — lock Aggressiveness + Historic AI Personality
        // + Difficulty at campaign creation.
        internal ConfigEntry<bool> OverrideVanillaSettings;
        internal ConfigEntry<int>  LockedDifficulty;

        // Diagnostic test mode — bypass date + war-state gates and force all
        // 12 scripted succession events to fire on the first monthly tick. For
        // verifying the commander-swap apply mechanic without playing through
        // a multi-month campaign. Default off; remember to disable before
        // playing for real.
        internal ConfigEntry<bool> ForceAllSuccessionEvents;

        private Harmony _harmony;
        private bool _loggedTacticalCommanderMode;
        private TelemetryRuntime _telemetryRuntime;

        // Parsed once and re-parsed only when the config value changes; this is
        // read from per-group hot paths.
        private TacticalCommanderMode? _cachedTacticalCommanderMode;

        internal TacticalCommanderMode TacticalCommanderModeValue =>
            _cachedTacticalCommanderMode ?? (_cachedTacticalCommanderMode = ResolveTacticalCommanderMode()).Value;

        public float HeavyReviewCycleHours => TacticalHeavyReviewCycleHours?.Value ?? 0.003f;
        public float HeavyReviewMinRealtimeSeconds => TacticalHeavyReviewMinRealtimeSeconds?.Value ?? 2.0f;

        private TacticalCommanderMode ResolveTacticalCommanderMode()
        {
            string raw = TacticalCommanderModeRaw?.Value;
            var mode = TacticalCommanderModePolicy.Parse(raw, TacticalCommanderMode.Active);
            if (!TacticalCommanderModePolicy.IsRecognized(raw))
            {
                Log?.LogWarning(
                    $"[TacticalCommanderMode] unrecognized value '{raw}' — expected Off, MonitorOnly or Active; using {mode}.");
            }

            LogTacticalCommanderModeOnce(mode);
            return mode;
        }

        private void LogTacticalCommanderModeOnce(TacticalCommanderMode mode)
        {
            if (_loggedTacticalCommanderMode || Log == null) return;

            _loggedTacticalCommanderMode = true;
            Log.LogInfo($"[TacticalCommanderMode] mode={mode} source=config legacyFlags=ignored-for-ledger releaseDefault=Active");
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Tactical.Battle.BattleTerrainGrid.Provider = Tactical.Battle.BattleTerrainGridRuntime.Get;
            Tactical.Battle.TerrainPicture.Provider = Tactical.Battle.TerrainPictureRuntime.Get;
            Tactical.Battle.TerrainPicture.PlanningEnabled = TerrainPictureEnabled;

            Enabled = Config.Bind(
                "General", "Enabled", true,
                "Master enable. Disable to short-circuit every patch in this mod.");
            VerboseLogging = Config.Bind(
                "Diagnostics", "Verbose Logging", true,
                "Default ON. Emits campaign decision-trace rows: daily ledger summaries (DailyOps, front, formation-directive, fiscal, defense, construction) every day instead of only on change, plus plan steering rows (importance target, campaign objective, unit transfer — each only when its signature changes) and construction probability-bias rows. Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
            PlanTrace = Config.Bind(
                "Diagnostics", "Plan Trace Logging", true,
                "Default ON. When the campaign planner's plan row changes, emits the per-candidate trace behind it (at most 12 candidates: score and its terms, odds against the required ratio, eligibility) as a CampaignPlan:trace row. Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
            SuccessionTrace = Config.Bind(
                "Diagnostics", "Succession Trace Logging", true,
                "Default ON. Each day, per alliance, emits one row per not-yet-fired scripted succession event with its date-gate and war-state-gate result (at most 24 rows a day, fewer as events fire). Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
            FiscalTrace = Config.Bind(
                "Diagnostics", "Fiscal Trace Logging", true,
                "Default ON. Emits the daily fiscal posture, gate, supply and finance-override row for each alliance every day instead of only when its signature changes. Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
            DirectorVerboseTrace = Config.Bind(
                "Telemetry",
                "Director Verbose Trace",
                true,
                "Default ON. Emits one detailed Director trace row per alliance per game day, in addition to the [CampaignPace] and [CollapseRisk] level-change rows. Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
            TelemetryLoggingProfileRaw = Config.Bind(
                "Telemetry",
                "Logging Profile",
                "TacticalTuning",
                "Off, TacticalTuning, CampaignTuning or FullTuning. Default TacticalTuning. Picks which structured rows reach the tuning sidecars " +
                "under <GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session>/: TacticalTuning = battle rows, CampaignTuning = campaign rows " +
                "(including every Diagnostics trace), FullTuning = both. Total size is capped by Max Tuning Log MB. Does not enable or disable any " +
                "tactical or campaign behavior.");
            TelemetryMaxTuningLogMb = Config.Bind(
                "Telemetry",
                "Max Tuning Log MB",
                250,
                new ConfigDescription(
                    "Total per-session structured telemetry cap in megabytes.",
                    new AcceptableValueRange<int>(25, 2000)));
            TelemetryFileRotateMb = Config.Bind(
                "Telemetry",
                "Tuning Log File Rotate MB",
                25,
                new ConfigDescription(
                    "Approximate JSONL file rotation size in megabytes.",
                    new AcceptableValueRange<int>(1, 250)));
            TelemetryRetainedSessions = Config.Bind(
                "Telemetry",
                "Tuning Log Retained Sessions",
                2,
                new ConfigDescription(
                    "Number of newest telemetry sessions to retain under BepInEx/WhiskeyRealism/tuning-logs.",
                    new AcceptableValueRange<int>(1, 10)));
            TelemetryEmitHumanSummary = Config.Bind(
                "Telemetry",
                "Emit Human Summary",
                true,
                "Default ON. At telemetry shutdown, writes summary.md in the session folder, built from up to 32 MB of that session's telemetry rows.");
            TelemetryPerformanceWarnings = Config.Bind(
                "Telemetry",
                "Telemetry Performance Warnings",
                true,
                "Default ON. true = individual slow-scope rows (at most 8 per scope per 5 s window, counted per thread); false = slow calls counted " +
                "only in PerformanceAggregate rows.");
            TelemetryCreateIssueBundleOnShutdown = Config.Bind(
                "Telemetry",
                "Create Issue Bundle On Shutdown",
                true,
                "Default ON. At telemetry shutdown, writes issue-bundle.json in the session folder: a redacted index of that session's own telemetry files for bug reports (copies nothing). Nothing is written when Logging Profile is Off.");
            TelemetryQueueCapacity = Config.Bind(
                "Telemetry",
                "Telemetry Queue Capacity",
                TelemetryRuntimeConfig.DefaultQueueCapacity,
                new ConfigDescription(
                    "Bounded structured telemetry detail queue capacity; protected health/failure rows have a separate reserve. Default preserves the shipped smoke profile.",
                    new AcceptableValueRange<int>(TelemetryRuntimeConfig.MinQueueCapacity, TelemetryRuntimeConfig.MaxQueueCapacity)));
            TelemetryFlushMilliseconds = Config.Bind(
                "Telemetry",
                "Telemetry Flush Milliseconds",
                TelemetryRuntimeConfig.DefaultFlushMilliseconds,
                new ConfigDescription(
                    "Maximum writer wait interval before flushing queued structured telemetry rows.",
                    new AcceptableValueRange<int>(TelemetryRuntimeConfig.MinFlushMilliseconds, TelemetryRuntimeConfig.MaxFlushMilliseconds)));
            TelemetryFlushRows = Config.Bind(
                "Telemetry",
                "Telemetry Flush Rows",
                TelemetryRuntimeConfig.DefaultFlushRows,
                new ConfigDescription(
                    "Maximum structured telemetry rows drained by the writer per flush batch.",
                    new AcceptableValueRange<int>(TelemetryRuntimeConfig.MinFlushRows, TelemetryRuntimeConfig.MaxFlushRows)));
            EnableTacticalObserver = Config.Bind(
                "Tactical",
                "Enable Tactical Observer",
                true,
                "Default ON. Emits tactical observer summaries, decision-matrix rows, ops-ledger/posture rows and the commander-intent/reserve-drift rows to the tactical tuning sidecar, deduped per signature (Tactical Observer Min Seconds Between Summaries). " +
                "It is also a prerequisite of the legacy local-reaction producer that feeds Enable Tactical Charge Denial while Enable Tactical Orchestrator Army is off; with the orchestrator army on the observer changes no battle state. " +
                "It also gates the BC16 record-only formation observers (LayoutObserverPatch): TacticalLayoutMirror rows (how each group order laid its brigades' regiments out, left to right or mirrored), TacticalFormationFlip rows " +
                "(a group whose formation changed twice within 0.1 battle-hours) and the once-per-battle [FormationProbe] line. They write nothing into the battle. " +
                "It also gates the BUG-WL-024 record-only PlayerUnitRepathObserverPatch: TacticalPlayerUnitRepath rows naming the vanilla routine (fallback, skirmisher target, unit link, reattach, contact interruption, or the player's own click) " +
                "that re-paths or changes the movement mode of a unit the W&L player owns (a W&L career only), and origin=player-ui on a queued order that came from the player's click.");
            TacticalObserverVerboseLogging = Config.Bind(
                "Tactical",
                "Tactical Observer Verbose Logging",
                true,
                "Default ON. Shortens the tactical observer, bug-telemetry and decision-matrix repeat window to one row per key and signature per second (instead of the configured interval). Rows still go only to the tactical tuning sidecar, capped by Max Tuning Log MB.");
            TacticalObserverMinSecondsBetweenSummaries = Config.Bind(
                "Tactical",
                "Tactical Observer Min Seconds Between Summaries",
                30,
                "Minimum wall-clock seconds between repeated tactical observer summaries with the same signature.");
            EnableTacticalDecisionMatrixLogging = Config.Bind(
                "Tactical",
                "Enable Tactical Decision Matrix Logging",
                true,
                "Default ON. While Tactical Observer is on, emits high-volume [TacticalDecisionMatrix] battle and group rows to the tactical tuning " +
                "sidecar, at most one snapshot per event signature per Tactical Decision Matrix Min Seconds Between Snapshots. Disable to drop the " +
                "matrix without disabling the rest of the observer.");
            TacticalDecisionMatrixMinSecondsBetweenSnapshots = Config.Bind(
                "Tactical",
                "Tactical Decision Matrix Min Seconds Between Snapshots",
                1,
                new ConfigDescription(
                    "Minimum wall-clock seconds between repeated [TacticalDecisionMatrix] snapshots with the same event signature.",
                    new AcceptableValueRange<int>(1, 60)));
            TacticalDecisionMatrixMaxRows = Config.Bind(
                "Tactical",
                "Tactical Decision Matrix Max Rows",
                80,
                new ConfigDescription(
                    "Maximum group rows per tactical decision-matrix snapshot. Lower it if the tactical tuning sidecar fills too fast.",
                    new AcceptableValueRange<int>(1, 300)));
            EnableTacticalBugTelemetry = Config.Bind(
                "Tactical",
                "Enable Tactical Bug Telemetry",
                true,
                "Default ON. Emits read-only tactical sidecar rows when vanilla current-order, courier, waypoint, path, formation, reserve or objective-chain handling looks risky; deduped per signature, no behavior change.");
            EnableTacticalFallbackRetreatNullGuard = Config.Bind(
                "Tactical",
                "Enable Tactical Fallback Retreat Null Guard",
                true,
                "Default ON. Suppresses NullReferenceException from three vanilla tactical fallback/retreat methods (MicroAICheckForRetreats, CheckLineFallbacks, CheckOutOfFireRange) during focused bug-smoke runs; all non-null exceptions still propagate.");
            EnableTacticalPathfinderDiscipline = Config.Bind(
                "Tactical",
                "Enable Tactical Pathfinder Discipline",
                true,
                "Default ON. Postfix on Regiment.AddPath (BUG-TAC-010): ends vanilla's path retry loop early when the segment just added already " +
                "ends within 5 m of the target, so AI units stop piling up sliver segments. Never removes a segment. AI units only (Tactical " +
                "Commander Mode and W&L/player protection apply); player orders are untouched.");
            EnableTacticalHqLinkGuard = Config.Bind(
                "Tactical",
                "Enable Tactical HQ Link Guard",
                true,
                "Default ON. Corrects cross-command group/HQ auto-links created by Regiment.MoveNonAIUnits while preserving same-command and same-hierarchy links.");
            EnableTacticalReserveOrderDelayGuard = Config.Bind(
                "Tactical",
                "Enable Tactical Reserve Order Delay Guard",
                true,
                "Default ON. #56: with order delays on, a reserve support move vanilla issues by direct RegimentSetPath (CheckUseOfReserves 6170, " +
                "no courier) is re-issued through the battle-core order executor as a courier order (SetWaypoint with order delay and double " +
                "quick). Vanilla's immediate path is removed only when that executor plan executes; any refusal (W&L player-owned or feud unit, " +
                "a higher-priority claim, an invalid target, dedupe, cooldown) keeps vanilla's own move. Writes need Tactical Commander Mode = Active.");
            EnableWlTacticalChargeGuard = Config.Bind(
                "Tactical",
                "Enable W&L Tactical Charge Guard",
                true,
                "Default ON. #42: Whiskey replays vanilla AIBattle.CheckForFeudGroupActions with vanilla's eligibility and initiative roll, but the " +
                "target is the nearest visible enemy within vanilla's feud distance and the move is a feud-replay order through the battle-core " +
                "order executor (ownership, chain, waypoint validation, dedupe, cooldown; refused under a higher-priority claim), so W&L " +
                "player-owned groups never feud-move. The orchestrator direct-child gate may deny the move. Without a battle snapshot for the " +
                "side it fails closed: vanilla's move goes out directly only where the write guard allows it. Needs Tactical Commander Mode = " +
                "Active. Off: vanilla feud movement runs unmodified and the direct-child gate is skipped. (The key name is kept for existing cfg files.)");
            FixGoodGroundCoverCap = Config.Bind(
                "W&L",
                "Fix Good Ground Cover Cap",
                true,
                "Default ON. W&L incident 'A Good Ground' doubles the cover of the player's command with no ceiling, so behind " +
                "parapets cover reaches 1.0 or more and the units take no ranged losses at all. With this on the doubled cover is " +
                "capped at 0.9 (ranged losses never below 10%); below that the incident keeps its full doubling, and a unit never ends " +
                "below its cover without the incident. Set false for rollback to vanilla.");
            EnableWlOperationNullGuard = Config.Bind(
                "W&L",
                "Enable Operation Null Guard",
                true,
                "Default ON. Suppresses the vanilla Operation.UpdateOperation null-before-transform cleanup crash and finishes the missing operation instead.");
            EnablePlayerOrderDoctrine = Config.Bind(
                "W&L",
                "Enable Player Order Doctrine",
                true,
                "Default ON. W&L only: after vanilla AIBattle.UpdateDLCPlayerOrders, the tactical operations ledger may give the player's command its current order (the order your AI superior issues you) through vanilla AIBattle.CheckCurrentOrderUpdate. Yields to vanilla's own order transitions, dedupes repeats and retries a rejected order at most once per 120 ticks, and writes only when Tactical Commander Mode = Active.");
            EnablePlayerOrderDoctrineDiagnostics = Config.Bind(
                "W&L",
                "Enable Player Order Doctrine Diagnostics",
                true,
                "Default ON. Writes the player-order doctrine's issue/suppress/skip decisions as TacticalPlayerOrder rows to the tactical tuning sidecar, once per distinct unit + decision signature (the last 128 signatures are remembered). Logs even when Enable Player Order Doctrine is off (decisions then show reason writes-disabled).");
            EnableWlAttritionScopeFix = Config.Bind(
                "W&L",
                "Enable W&L Attrition Scope Fix",
                true,
                "Default ON. W&L career only. Vanilla Regiment.UpdateCampaignUnitStats applies the player's attrition effect (Camp Rules, " +
                "Provost Marshal) to the sickness and desertion of every regiment in the game, enemies included. With this on it applies only " +
                "where vanilla scopes medical care, training and readiness: regiments of a campaign group the player owns, and the player's " +
                "own regiments, which always keep it whether or not Enable W&L Staff Scope Fix is on. Off: vanilla.");
            EnableWlBuySuppliesFix = Config.Bind(
                "W&L",
                "Enable W&L Buy Supplies Fix",
                true,
                "Default ON. W&L career only. Vanilla \"Buy Supplies\" (and the average-supply value it and its conditions read) tests the " +
                "player's command type instead of each subordinate's, so a division, corps or army commander pays 1000 money and no unit gets " +
                "supplies. With this on, an HQ commander's owned basic units are filled the way vanilla fills a brigade commander's. Off: vanilla.");
            EnableWlCareerTransferFix = Config.Bind(
                "W&L",
                "Enable W&L Career Transfer Fix",
                true,
                "Default ON. W&L career only. Vanilla's AI transfer of units never happens in a career: CheckTransferOfUnits queues the unit and " +
                "then BattleUnits.MoveUnit refuses unless both units are player-owned, so no AI faction ever reinforces a weak front by transfer. " +
                "With this on, the AI's own transfers go through (the vanilla check is skipped for the calls CheckTransferOfUnits makes), never " +
                "for a unit the player owns. Off: vanilla.");
            EnableWlStaffScopeFix = Config.Bind(
                "W&L",
                "Enable W&L Staff Scope Fix",
                true,
                "Default ON. W&L career only. Vanilla applies several of the player's staff, camp and action effects only when the player's " +
                "command is itself a campaign-map group, so a brigade or regiment commander, or a division inside an AI corps, gets nothing " +
                "from them. With this on: supply (Quartermaster, Subsistence, Ordnance, reinforcement speed, camp station Inspect Logistics) " +
                "applies to the map group carrying the player's command (supply is one pool per map group); medical care, training, " +
                "readiness (camp station Readiness) and morale apply to the player's own regiments inside that group. No other AI group is " +
                "affected. With Enable W&L Supply Efficiency Sign Fix off, supply efficiency and Inspect Logistics stay neutral for that " +
                "group rather than slowing its resupply. Off: vanilla.");
            EnableWlStaffPanelScopeFix = Config.Bind(
                "W&L",
                "Enable W&L Staff Panel Scope Fix",
                true,
                "Default ON. W&L career only. Vanilla's campaign army panel lists the player's staff effects on supply, morale and fighting " +
                "spirit for whichever group is selected, enemies included. With this on they are listed only for the player's own groups " +
                "and the map group carrying the player's command. Display only. Off: vanilla.");
            EnableWlSupplyEfficiencySignFix = Config.Bind(
                "W&L",
                "Enable W&L Supply Efficiency Sign Fix",
                true,
                "Default ON. W&L career only. Vanilla multiplies the resupply duration by the player's supply-efficiency bonus and by Inspect " +
                "Logistics, so a better Quartermaster or camp result makes resupply slower. With this on the same bonus shortens the duration " +
                "instead (a +25% bonus resupplies in 1/1.25 of the time; a camp malus still lengthens it). Off: vanilla direction.");
            EnableWlActionOnceEffectsFix = Config.Bind(
                "W&L",
                "Enable W&L Action Once-Effects Fix",
                true,
                "Default ON. W&L career only. A sweep of every quality of every type (actions, items, companions, injuries, staff, career " +
                "events) against what vanilla reads found paid actions whose bonus lands where vanilla never reads it: Logistics Corps and " +
                "Not Yet Ready (supply efficiency), Signal Corps (battle and campaign order delays), High Spirits (fighting spirit), " +
                "Recruitment Campaign, Organize Recruitment, Camp Pleasures, Uncle Sam and Irish Legion (contract renewals), Engineer Corps " +
                "(engineering points), Staff Standards and Examine Details (staff efficiency, the rate at which staff skills grow toward " +
                "yours), and Gamble (its incident-chance cost). With this on each applies where vanilla reads it, in the direction the " +
                "action states. The supply-efficiency part needs Enable W&L Supply Efficiency Sign Fix as well (otherwise it would slow " +
                "resupply). Left vanilla because their texts promise something else: the attrition values of Harsh Punishments, Ample " +
                "Rewards and Purge, and the prestige values on items, injuries and one career event; three companions' rank values use a " +
                "quality type vanilla has no meaning for. Off: vanilla.");
            FixPlayerEntrenchmentPoints = Config.Bind(
                "Tactical",
                "Fix Player Entrenchment Points",
                true,
                "Default ON. At battle start the player's engineering points get a bonus for the army's campaign entrenchment, " +
                "but vanilla multiplies it by the entrenchment level twice, so the player gets less than the AI for the same works " +
                "(the AI's bonus is linear). With this on the player's bonus is linear too. Set false for rollback to vanilla.");
            EnableBattleEntrenchmentAverageFix = Config.Bind(
                "Tactical",
                "Enable Battle Entrenchment Average Fix",
                true,
                "Default ON. Vanilla BattleUnits.CalculateAllUnitsOfAlliance averages the AI side's entrenchment level over the player side's " +
                "group count, which sets the AI's battle fortification tier and engineering time. With this on it is averaged over the AI " +
                "side's own groups, as readiness beside it is. A stat correction, not an order. Off: vanilla.");
            EnableBattleHqConditionFix = Config.Bind(
                "Tactical",
                "Enable Battle HQ Condition Fix",
                true,
                "Default ON. Vanilla BattleUnits.AssignConditionToSubordinates sets each group HQ's horse and gun condition from the men " +
                "value at battle start. With this on the HQ gets the horse and gun values its sub-units get, and the side's gun-condition " +
                "average is recomputed from them. A stat correction, not an order. Off: vanilla.");
            EnableFlankResponseAndEnvelopment = Config.Bind(
                "Tactical",
                "Enable Flank Response And Envelopment",
                true,
                "Default ON. BC11: a holding group faces the strongest perceived pressure on its front (a 3-bin window of the slice " +
                "pressure, with hysteresis) instead of only its closest visible enemy; a wing under flank threat with contact draws the " +
                "nearest reserve to guard the threatened flank; and the geometric plays that envelop (double envelopment, flank march, " +
                "wide flank approach, the maneuvering wing of maneuver-and-fix, the cavalry deep raid) hold, march round the enemy line " +
                "end, face it and attack it as orders to the child, instead of only relabelling roles. Never for a Defender side. " +
                "The refused flank that reaches vanilla (the wheel and the in-combat rotation) is a bug fix and is not behind this. Off: " +
                "the closest visible enemy decides facing, reserves release as before, plays only relabel roles.");
            EnableBattleTerrainPicture = Config.Bind(
                "Tactical",
                "Enable Battle Terrain Picture",
                true,
                "Default ON. BC12: the AI picks ground. A map picture built once per battle from public terrain data (crests, walls, " +
                "fences, sunken roads and embankments, river lines, fords, bridges, road junctions, towns) classifies the objectives " +
                "(Bridge, Ford, Town, RoadJunction, Ridge), scores terrain strength and approach difficulty, lets a defending side form " +
                "on the best crest, wall or river line near its objective and skip a flank guard on a river- or crest-anchored flank, " +
                "reads an enemy standing on strong ground as defending, and makes ReverseSlopeDefense need a crest across the front. " +
                "The picture itself builds either way (the playbook terrain reads it). Off: objectives keep their types and zero " +
                "terrain strength, the defensive anchor keeps its terrain-anchor-preserved behaviour, no strong-ground evidence, and " +
                "ReverseSlopeDefense is never chosen (it needs a crest line).");
            EnableBattleAiLevelOfDetail = Config.Bind(
                "Tactical",
                "Enable Battle AI Level Of Detail",
                true,
                "Default ON. BC15: the battle AI spends its producer time where the battle is. A group far from every enemy the side perceives " +
                "(none within 1.5 km, no fire, no melee, nothing moving under it), settled, on a holding task (hold, form up, flank guard, reserve " +
                "wait, consolidate), with no order in flight and nothing changed in what its producers read, re-evaluates every fourth " +
                "side-stat cycle instead of every cycle; its last claims stand in between. Every wake-up reads perceived and own state only: a " +
                "sighting within 3 km, fire, its parent moving, its order target or its path corridor changing, a new closest enemy bearing, a " +
                "plan change. The player's own command chain is never slowed. Off: every group runs every producer on every cycle, as before.");
            EnablePerCommanderPersonality = Config.Bind(
                "Tactical",
                "Enable Per-Commander Personality",
                true,
                "Default ON. BC13: each corps and division commander has his own temperament. A historical commander keeps the list's " +
                "values; every other commander's aggression, caution, audacity and casualty tolerance are derived from his game stats " +
                "(initiative, cunning, leadership, administration, veteran, political), so an unlisted army commander is no longer the " +
                "faction default. The army's attack threshold for a Meeting or Attacker side becomes a band (1.20 at aggression 0 to 0.80 at " +
                "1.0, 1.00 at 0.5) instead of the parity cap, and the casualty level that ends a delaying defence follows the commander's " +
                "casualty tolerance instead of his aggression. Off: the army vector is the roster's as before, every node uses it, the " +
                "threshold cap at 1.0 and the aggression-based casualty threshold return.");
            EnableCommanderOpportunity = Config.Bind(
                "Tactical",
                "Enable Commander Opportunity",
                true,
                "Default ON. BC13: each AI corps and division commander looks for local openings the side perceives (a gap in the enemy line, a " +
                "shaken sector, a flank in the air, an unsupported battery, an isolated cluster) and, by his own temperament, sends one of his " +
                "brigades or divisions at one as a bounded exploit: advance, strike, consolidate, then back to its post. The detection reads " +
                "only what the side perceives (the fog grid), never the enemy's true positions. At most three exploits at a time, one per " +
                "commander, with a reserve floor and a line-integrity check. Off: no detection and no exploits; the posture is the " +
                "operations ledger's alone.");
            EnableDefenderCounterstroke = Config.Bind(
                "Tactical",
                "Enable Defender Counterstroke",
                true,
                "Default ON. BC13: a Defender side may answer an enemy that broke through its line (a visible unit 100 to 400 m behind the " +
                "own front line, clear of it) with a bounded local counterstroke by a reserve or second-line brigade, never beyond the own " +
                "line and never by cavalry. Other attack tasks on a Defender are still held. Needs Enable Commander Opportunity. Off: no " +
                "penetration is detected and a Defender never launches an exploit.");
            EnableStaggeredReserveRelease = Config.Bind(
                "Tactical",
                "Enable Staggered Reserve Release",
                true,
                "Default ON. BC13: the army releases its reserves one at a time. When the plan reaches its Exploit phase or the odds reach the " +
                "reserve-commit trigger, at most one Reserve division or corps is released per interval (0.05 to 0.15 game-hours, shorter for " +
                "an aggressive army commander), the one nearest the main effort, and the army keeps a floor of reserves held (30 % of the " +
                "reserves it had, more for a cautious commander, fewer for one who accepts casualties; at least one unless it only had one). " +
                "A crisis still releases at once, one reserve per outmatched sector, the nearest, even below the floor. A reserve with a live " +
                "local exploit is never released. Off: every reserve is released together, as before.");
            EnableCavalryStrikeDiscipline = Config.Bind(
                "Tactical",
                "Enable Cavalry Strike Discipline",
                true,
                "Default ON. #120 (BUG-TAC-030): vanilla assigns a cavalry regiment's charge from the true state of the battle: an enemy " +
                "it cannot see, a regiment in square or a fort, artillery with infantry standing beside it, a target its odds do not " +
                "favour, even when the charging regiment is itself outflanked. With this on, vanilla's pick stands only when the side " +
                "perceives the target, it is not in square or garrisoned, it is artillery with no infantry or cavalry within 250 m or a " +
                "shaken unit the regiment's odds favour, and the charger is not outflanked; a live local exploit's cavalry target replaces " +
                "a refused pick when it is within reach. Vanilla's own cycle does the charging; this only removes bad targets. " +
                "Off: vanilla's pick.");
            EnableVanillaStanceCooldownFix = Config.Bind(
                "Tactical",
                "Enable Vanilla Stance Cooldown Fix",
                true,
                "Default ON. #119 (BUG-TAC-027): vanilla's battle AI is meant to wait one game-hour (battleprefs: time to wait for resetting of " +
                "new ai stances once set) after it changes a group's stance, but its comparison is inverted and re-decides every cycle. With " +
                "this on, a group whose stance vanilla itself just changed is held for that hour, so vanilla's weak-group defend and screen " +
                "changes wait up to an hour after the group's previous change instead of flipping every cycle. An army retreat releases the " +
                "holds at once. Whiskey's own stance holds and the W&L feud instance are untouched. Off: vanilla's re-decision every cycle.");
            EnableSeparatedEnemySteer = Config.Bind(
                "Tactical",
                "Enable Separated Enemy Steer",
                true,
                "Default ON. #121 (a steer): vanilla sends an AI group straight at an isolated enemy cluster it finds from the true " +
                "state of the battlefield, whatever the group's orders, and Whiskey's posture then re-issues the ledger order: two movers " +
                "on one group. With this on, a group Whiskey commands (in the AI pool with a doctrine state) is left out of that vanilla " +
                "behaviour; Whiskey's own isolated-cluster opening, which only sees what the side perceives, takes over. Groups vanilla " +
                "commands, the player's groups and the W&L feud instance are unchanged. Needs Tactical Commander Mode Active. Off: vanilla " +
                "also sends Whiskey-commanded groups.");
            EnableVanillaReserveSupportPickFix = Config.Bind(
                "Tactical",
                "Enable Vanilla Reserve Support Pick Fix",
                true,
                "Default ON. #124 (BUG-TAC-008): when vanilla's battle AI sends a reserve to support an endangered regiment it picks one with " +
                "a random range that excludes the last candidate, so the last endangered regiment is never supported and, with two, always the " +
                "first. With this on the pick includes every endangered regiment. A live rollback: turning it off restores vanilla's pick " +
                "without a restart. Whiskey's reserve gate still denies this vanilla path for Reserve-role children.");
            EnableVanillaMacroAssaultUpgradeFix = Config.Bind(
                "Tactical",
                "Enable Vanilla Macro Assault Upgrade Fix",
                true,
                "Default ON. #122 (BUG-TAC-028): when vanilla's battle AI finds that no flanking is possible and the force balance is high " +
                "it means to upgrade a macro attack to a full assault, but it writes the upgrade where the method's own decision never " +
                "stores it, so the upgrade only lands when the pending value already was attack. With this on the assault upgrade reaches " +
                "the stored decision. A live rollback: turning it off restores vanilla's behaviour without a restart.");
            EnableManeuverFeasibility = Config.Bind(
                "Tactical",
                "Enable Maneuver Feasibility",
                true,
                "Default ON. BC14: before the army picks a battle plan, every candidate play is timed over the map's rivers and crossings as " +
                "the side perceives them (held, watched, clear or unknown), and the risk that a divided army is beaten in detail is priced " +
                "from the march, courier and enemy shift times. Plays that cannot work are skipped, risky ones penalized; flanking wings " +
                "march on a synchronized release, inside a time budget, through a chosen crossing; a broken assumption replans; a Defender " +
                "sends a reserve to guard a crossing the enemy can reach first. Off: plans are picked and executed as before, and Enable " +
                "Enemy Works Doctrine and Enable Objective Doctrine act as off.");
            EnableEnemyWorksDoctrine = Config.Bind(
                "Tactical",
                "Enable Enemy Works Doctrine",
                true,
                "Default ON. BC14W: against enemy works the AI has seen (soldiers behind parapets, breastworks, walls or sunken roads, and " +
                "entrenchment pieces once perceived), the army turns an open end of the works first and leaves a force holding in front; when " +
                "no turn will do, a willing commander assaults the works' focal point (a salient or a junction) in march column after a " +
                "bombardment, and an unwilling one holds instead of ordering a hopeless assault. A Defender in works keeps a reserve behind its " +
                "most threatened point. Needs Enable Maneuver Feasibility. Off: works are priced as open ground, no works assault and no hold.");
            EnableObjectiveDoctrine = Config.Bind(
                "Tactical",
                "Enable Objective Doctrine",
                true,
                "Default ON. BC14O: the army puts its main effort on the most valuable objective it can take by vanilla's own victory points, " +
                "not the nearest; it presses harder when it is behind on the victory bar late in the day and holds more when it is ahead; " +
                "secondary objectives draw at most two screens; a historical battle's designer timetable is a strong hint from its due hour, " +
                "not an override. The AI reads the victory bar exactly as the player sees it, nothing more. Needs Enable Maneuver Feasibility. " +
                "Off: objectives are valued and chosen as before.");
            EnableCrossingMoveGate = Config.Bind(
                "Tactical",
                "Enable Crossing Move Gate",
                true,
                "Default ON. A doctrine move (posture Screen and Probe, the leaf slots, division-play, the reserve release) whose straight " +
                "leg crosses a river is made through a crossing the side knows (perceived clear or watched, BC14's crossing picture) or is not " +
                "made; a withdrawal, a fallback, a deployment placement and a scout's look are not gated. A leg end on open river or a ford takes the " +
                "bank the leg reaches or leaves it by. Needs Enable Maneuver Feasibility (the river map): while the map is still being built on " +
                "a terrain with fords a gated move waits (at most 72 battle seconds); with the feasibility off, the build failed or no ford on " +
                "the map the gate lets every move through. Off: moves are checked for water at their target as before.");
            EnableVanillaArtilleryChainSideFix = Config.Bind(
                "Tactical",
                "Enable Vanilla Artillery Chain Side Fix",
                true,
                "Default ON. #123 (BUG-TAC-029): vanilla's battle AI decides which end of the line an artillery group joins with a random range " +
                "that can only give one answer, so every artillery group joins the left end of the chain and none the right. With this on " +
                "artillery groups split between the left and right ends at random. A live rollback: turning it off restores vanilla's " +
                "value without a restart.");
            EnableVanillaFlankThreatTallyFix = Config.Bind(
                "Tactical",
                "Enable Vanilla Flank Threat Tally Fix",
                true,
                "Default ON. Vanilla Regiment.UpdateGroupStrength adds the group's own outflanked code once per subordinate to " +
                "flanksthreated, so the tally is usually 0 and a flanked line group reads as strong as an unflanked one to the AI's " +
                "flank-move, reserve and flank-extension decisions. With this on the tally sums each subordinate's own outflanked weight. " +
                "Vanilla's own AI then launches more counter-flank moves, on a Defender side too (CheckFlankMoves skips only macro AI " +
                "-1 and 3). A stat correction on vanilla's field, not an order. Off: vanilla.");
            EnableCampaignChainDispatches = Config.Bind(
                "W&L",
                "Enable Campaign Chain Dispatches",
                true,
                "Default ON. Whiskey's own strategic orders to a W&L player whose command is itself a campaign-map group (a corps or a " +
                "top-level division) reach his career panel as the order vanilla would give him (Redeploy, Offensive, Offensive Operation, " +
                "Engage Enemy, Defend Capital), with vanilla's order zone and arrival prestige and at most one order per target per game " +
                "day. A player who is not his own map group (a brigade or division carried by an AI corps) never receives a campaign " +
                "order, as in the base game: the corps that carries him marches without a word. Whiskey never moves the player's " +
                "command. Off: no Whiskey campaign orders; vanilla's own are unchanged.");
            EnablePlayerComplianceLedger = Config.Bind(
                "W&L",
                "Enable Player Compliance Ledger",
                true,
                "Default ON. W&L: measures how often the player reaches the zone of the orders his superiors give (campaign: within three " +
                "game days, only while his command is its own map group; battle: within half a battle hour) and keeps a per-career " +
                "reliability (saved in the sidecar). The battle AI counts the player's regiments at that reliability when it weighs its " +
                "sectors, so it does not build its main effort on a player who ignores orders. Starts at full trust; no penalty for the player. Off: the " +
                "player's regiments count fully and nothing is recorded.");
            EnableWlOrderReadmit = Config.Bind(
                "W&L",
                "Enable W&L Order Readmit",
                true,
                "Default ON. W&L subordinate career: a new order from your superiors reaches you when the situation changes " +
                "(the same kind of order at a new place after 15 game minutes, a retreat over 'Engage at will', a defend once the " +
                "objective is held or the army turns defensive), instead of being dropped by vanilla's one-order-per-type rule; " +
                "'Engage at will' is released to 'hold current position' when contact ends. Writes the W&L order slot " +
                "(DLC_WL.givenorder) and restores it if vanilla refuses the new order. Needs Tactical Commander Mode = Active. " +
                "Off: vanilla's order dedupe.");
            EnableWlOrderCouriers = Config.Bind(
                "W&L",
                "Enable W&L Order Couriers",
                true,
                "Default ON. W&L career: orders from your superiors (vanilla's and Whiskey's) arrive after the courier delay from the " +
                "issuing headquarters instead of appearing the instant they are given. In battle that is vanilla's own order-delay " +
                "settings (bugle within range, a mounted courier beyond it, relays down the chain; needs Tactical Commander Mode = " +
                "Active). On the campaign map it is vanilla's campaign order delay from your senior commander (distance, telegraph, " +
                "winter quarters, entrenchment, readiness and perks of the sender); a campaign order still on its way is saved with " +
                "the game. Off: orders are delivered at the next order pass.");
            EnableWlOrderText = Config.Bind(
                "W&L",
                "Enable W&L Order Text",
                true,
                "Default ON. W&L career: order texts name real places and formations. Whiskey's orders name the nearest named objective " +
                "or say where the point lies from your command (\"about 1,200 yards ahead and to the right of your position\") instead of " +
                "an internal key, vanilla's \"My Division\" becomes the formation's name (\"Hood's Division\"), and Whiskey's orders carry " +
                "a short condition line and your neighbours. Also corrects the portrait of an issuer who has been replaced. Off: vanilla texts " +
                "(Whiskey's orders name the objective or \"Objective\").");
            EnableWlOrderFollowUp = Config.Bind(
                "W&L",
                "Enable W&L Order Follow-Up",
                true,
                "Default ON. W&L career: after you reach and hold the position of a battle order from your superiors, your superior " +
                "tells you to hold that position (an information-style 'hold current position' order, no prestige, no compliance " +
                "sample; needs Enable W&L Order Readmit); on the campaign map a reached Redeploy, Offensive, Offensive Operation " +
                "or Engage order is acknowledged with a short message. Information only: nothing moves and nothing is taken away. " +
                "Off: no follow-up and no acknowledgement.");
            EnableWlEnemyRetreatGuard = Config.Bind(
                "W&L",
                "Enable W&L Enemy Retreat Order Guard",
                true,
                "Default ON. Vanilla checks the enemy army's order list with the same code as yours, so when the ENEMY army retreats a " +
                "top-level W&L player is told 'our army retreats'. With this on that order is not sent to you (BUG-WL-019). Your own " +
                "army's retreat order is unchanged. Off: vanilla.");
            FixLostOrdersFogGrid = Config.Bind(
                "W&L",
                "Fix Lost Orders Fog Grid",
                true,
                "Default ON. Vanilla hides the paper-map fog-of-war grid when the Lost Orders incident is rolled and then, when the " +
                "incident does not apply (no Expert Spy companion), leaves it hidden until the paper map is closed and opened again. " +
                "With this on the grid is shown again at once (BUG-WL-020). Off: vanilla.");
            FixCapitalDefensePlayerOrder = Config.Bind(
                "W&L",
                "Fix Capital Defense Player Order",
                true,
                "Default ON. Vanilla re-sends the defend-capital order to a top-level W&L player on every pass and sets forced march " +
                "(running and double quick, which costs condition on the campaign map) on the player's own group each time. With this " +
                "on the order is shown once per capital and your group keeps the march mode you set (BUG-WL-018). Off: vanilla.");
            EnableCicFactionAiBugGuard = Config.Bind(
                "W&L",
                "Enable CIC Faction AI Bug Guard",
                true,
                "Default ON. #70 (vanilla bug BUG-WL-001): when the W&L player is commander-in-chief, vanilla's ship-construction and " +
                "army-group AI skip every faction, not only the player's. The guard lets the other factions run them; the player's own " +
                "faction stays the player's. Off: vanilla behaviour (the enemy builds no ships and forms no army groups in a CIC career).");
            EnableTacticalMacroStanceScorer = Config.Bind(
                "Tactical",
                "Enable Tactical Macro Stance Scorer",
                true,
                "Default ON. #44: after vanilla AIBattle.CheckGlobalAIStrategy, sets each AI side's battle-level AIBattle.macroai from the " +
                "operations-ledger operation, otherwise from the doctrine fallback (or, with Enable Tactical Orchestrator Army off, the legacy odds " +
                "scorer). Never writes retreat (3) and defers to scripted values. Off: vanilla macroai.");
            EnableTacticalGroupSectorStance = Config.Bind(
                "Tactical",
                "Enable Tactical Group Sector Stance",
                true,
                "Default ON. Lets the sector-odds scorer bias ai_stance (1-3) of AI brigade groups that have no doctrine or ledger task. Stance " +
                "writes only, no movement. Doctrine and ledger-task stance writes do not depend on this flag; they follow Tactical Commander Mode (written only when Active).");
            EnableTacticalCommanderIntentDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Commander Intent Doctrine",
                true,
                "Default ON. Computes the tactical commander intent and playbook from vanilla battle context and emits read-only [TacticalIntent] / " +
                "[TacticalPlaybook] telemetry; changes no battle state. Required by Enable Tactical Local Reaction Doctrine.");
            EnableTacticalLocalReactionDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Local Reaction Doctrine",
                true,
                "Default ON. Computes per-group local reactions from the commander intent and emits read-only [TacticalLocalReaction] telemetry; " +
                "required by Enable Tactical Reserve Intent Telemetry. While Enable Tactical Orchestrator Army is off, a DenyCharge reaction is " +
                "also a reason for Enable Tactical Charge Denial to demote a vanilla assault; with the army on it changes no battle state.");
            EnableTacticalChargeDenial = Config.Bind(
                "Tactical",
                "Enable Tactical Charge Denial",
                true,
                "Default ON. When vanilla escalates an AI group to the assault stance (ai_stance 4) that the command doctrine says must not " +
                "attack (reserve held, or falling back to the line), the battle-core charge-demotion producer orders it back to defend (2) at " +
                "top priority through the order executor: no cover-skipping assault approach, skirmishers may deploy again, its cavalry stops " +
                "picking charge targets, and the infantry charge doctrine hands back its charges. Infantry melee from contact is not affected. " +
                "With Enable Tactical Orchestrator Army off, a legacy local-reaction DenyCharge also demotes an assault, to attack (3). The first " +
                "demotion per battle logs [ChargeDemotion]; each one is a ChargeDemotion telemetry row. Writes " +
                "need Tactical Commander Mode = Active; W&L/player groups are never touched.");
            EnableTacticalReserveIntentTelemetry = Config.Bind(
                "Tactical",
                "Enable Tactical Reserve Intent Telemetry",
                true,
                "Default ON. Emits read-only [TacticalReserveIntent] rows (line-relief requests and reserve availability per side) and the " +
                "[TacticalReserveDrift] observer rows. Requires Enable Tactical Local Reaction Doctrine. Never changes reserve lists.");
            EnableTacticalArtilleryDoctrine = Config.Bind(
                "Tactical Doctrine",
                "Enable Tactical Artillery Doctrine",
                true,
                "Default ON. Battle-core artillery producer, doctrine half: each arbiter tick, an AI battery bombarding (combat behavior 9) may be " +
                "switched to counter-battery (8) or told to cease (7, resetting bombardstarttime as vanilla does) from its group's doctrine order " +
                "and the support screen, and a counter-battery battery with no visible enemy battery in fire range is released to 7 (vanilla's own " +
                "release is unreachable). Every group stance, not only vanilla CheckAIBombardment's 3/4. Orders go through the battle-core order " +
                "executor. This flag or Enable Artillery All-Stance Audit also runs the point-blank watchdog: a battery vanilla sends to within " +
                "60 m of a visible enemy is pulled back. W&L/player units protected; writes need Tactical Commander Mode = Active.");
            EnableTacticalWithdrawalDoctrine = Config.Bind(
                "Tactical Doctrine",
                "Enable Tactical Withdrawal Doctrine",
                true,
                "Default ON. Battle-core withdrawal producer: each arbiter tick, attached AI infantry/cavalry regiments the withdrawal doctrine " +
                "marks for fallback or retreat (morale pressure, rear threat, fatigue, friendly routs, cover) get vanilla's own fallback shape as a " +
                "top-priority order through the battle-core order executor: an immediate, terrain-checked move away from the closest visible enemy " +
                "(else from the side's front) by vanilla's standard fallback or retreat distance, ending in movement mode 2, plus combat behavior 2 " +
                "for a fallback. A group whose ledger FallBackToLine move stalled is re-ordered by courier. Never Regiment.SetWithdrawal. W&L " +
                "player-owned and feud units are never written; writes need Tactical Commander Mode = Active. Off: no withdrawal orders.");
            TacticalCommanderModeRaw = Config.Bind(
                "Tactical Orchestrator",
                "Tactical Commander Mode",
                // User-approved default for the full doctrine feature: Active. Existing config files still take precedence over this default.
                "Active",
                "Default Active. Values: Off, MonitorOnly, Active (case-insensitive). Active runs the full tactical command system for AI-controlled " +
                "units: every Whiskey battle order goes through the battle-core order executor (the arbiter's producers and the #42/#56 inline " +
                "paths), plus the #59 reserve rollback, #44 macroai and #60 deployment corrections. MonitorOnly keeps the battle snapshot, " +
                "perception, orchestrator, operations ledger and telemetry but writes nothing to vanilla (the arbiter stands down). Off also stops " +
                "the operations ledger: full tactical rollback. Unrecognized values log a warning and fall back to Active.");
            TacticalCommanderModeRaw.SettingChanged += (_, __) =>
            {
                _cachedTacticalCommanderMode = null;
                _loggedTacticalCommanderMode = false;
            };
            _ = TacticalCommanderModeValue;
            EnableTacticalHeavyPathThrottling = Config.Bind(
                "TacticalTickOptimization",
                "Enable Tactical Heavy Path Throttling",
                true,
                "Default ON. Throttles the side-wide evidence, vision and doctrine-assignment heavy path to the Heavy Ledger Review Cycle Hours / Min Realtime Seconds cadence.");
            TacticalHeavyReviewCycleHours = Config.Bind(
                "TacticalTickOptimization",
                "Heavy Ledger Review Cycle Hours",
                0.003f,
                "Max battle hours between full heavy planning passes even if signature is unchanged (0.003 hours ≈ 10.8 battle seconds). Must be <= vanilla sidestatupdatecycle for constraint C.");
            TacticalHeavyReviewMinRealtimeSeconds = Config.Bind(
                "TacticalTickOptimization",
                "Heavy Ledger Review Min Realtime Seconds",
                4.0f,
                "Minimum real seconds between heavy planning passes under time compression (default 4.0; raised from 2.0 on 2026-05-20 to halve heavy-rebuild rate at 10×/20× compression where the snapshot rebuild was the dominant orchestrator-tick cost). First tick still runs; urgent recovery still uses the latest snapshot plus live vanilla reads.");
            EnableTacticalBattleOrchestrator = Config.Bind(
                "Tactical Orchestrator",
                "Enable Tactical Battle Orchestrator",
                true,
                "Default ON. Master switch for the per-battle tactical orchestrator (army plan, operations ledger, direct-child and reserve gates). " +
                "Off: no orchestrator, so the ledger-driven writes, the orchestrator macroai source and the reserve commit gate stop. Independent " +
                "bug-fix patches still run.");
            EnableTacticalOrchestratorArmy = Config.Bind(
                "Tactical Orchestrator",
                "Enable Tactical Orchestrator Army",
                true,
                "Default ON. Attaches the per-side ArmyOrchestrator (plan, direct children, command tree) at battle start and makes the operations " +
                "ledger the macroai source for #44. Off: #44 uses the legacy odds scorer and the legacy local-reaction path writes reactions.");
            TacticalOrchestratorMinReplanSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Min Replan Seconds",
                600,
                new ConfigDescription(
                    "Minimum battle seconds between army replans, measured on the doctrine battle clock (frozen overnight), so it covers the same span " +
                    "of battle at any time compression. Replan triggers may fire earlier; the re-pick waits until the cooldown has passed. The " +
                    "new-battle-day replan counts against it. Default 600.",
                    new AcceptableValueRange<int>(60, 3600)));
            EnableTacticalOrchestratorIntentInference = Config.Bind(
                "Tactical Orchestrator",
                "Enable Tactical Orchestrator Intent Inference",
                true,
                "Default ON. O2: per-tick TacticalIntentModel built from visible enemy " +
                "state, fed into ArmyOrchestrator's replan trigger evaluator and " +
                "playbook selection bias. Disable to keep O1 initial-pick-only behavior " +
                "(plans never advance phase or replan during a battle).");
            EnableTacticalOrchestratorDirectChildGate = Config.Bind(
                "Tactical Orchestrator",
                "Enable Tactical Orchestrator Direct-Child Gate",
                true,
                "Default ON. O3: inside the W&L feud guard (#42), denies AI feud moves for Reserve-role children, Fix children moving beyond 70% of " +
                "the feud distance, and Fallback children moving toward the enemy. Requires Enable W&L Tactical Charge Guard and Tactical Commander " +
                "Mode = Active. Off: the gate is skipped silently.");
            EnableTacticalOrchestratorReserveCommitGate = Config.Bind(
                "Tactical Orchestrator",
                "Enable Tactical Orchestrator Reserve Commit Gate",
                true,
                "Default ON. #59: when vanilla AIBattle.CheckUseOfReserves commits a reserve regiment (direct RegimentSetPath) for a command group " +
                "whose role is Reserve (operations-ledger state first, then the command-node intent), the new path is rolled back unless the " +
                "direct-child allocator has already released the group; commits touching W&L player-owned units are only observed. A released " +
                "group also gets a courier move to stand behind its own line in the sector the allocator gave it (reserve-release producer, " +
                "through the battle-core order executor). Allowed commits stay eligible for the Reserve Order Delay Guard. Requires Enable " +
                "Tactical Battle Orchestrator; writes need Tactical Commander Mode = Active.");
            EnableArtilleryAllStanceAudit = Config.Bind(
                "Tactical Orchestrator",
                "Enable Artillery All-Stance Audit",
                true,
                "Default ON. Battle-core artillery producer, micro half: each arbiter tick, every AI battery in any group stance (vanilla " +
                "CheckAIBombardment only covers stances 3/4) may limber or retreat when a visible enemy is within 60 m, or within 100 m with no " +
                "interposed infantry/cavalry and group morale below 0.6 (idle batteries only); unlimber and face a visible enemy in fire range; wheel " +
                "toward the strongest visible threat (only when off by vanilla's turn trigger); cease bombardment below 10% ammo (mean of the three " +
                "ammo slots). Orders go through the battle-core order executor, and a battery is left alone for Artillery Audit Min Hold Seconds " +
                "after one. This flag or Enable Tactical Artillery Doctrine also runs the point-blank watchdog. AI batteries only (W&L/player " +
                "units protected); writes need Tactical Commander Mode = Active.");
            ArtilleryAuditMinHoldSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Artillery Audit Min Hold Seconds",
                3.0f,
                "Per-battery hold after the order executor issued an artillery doctrine or micro order to a battery (combat behavior, formation, " +
                "facing or move) before the next one. Battle seconds on the doctrine clock (frozen overnight), 1x-equivalent (600 per battle " +
                "hour, so 3 = 18 battle seconds), the same span at any time compression. The point-blank watchdog is not held. Default 3.");
            BrigadeTaskHoldSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Brigade Task Hold Seconds",
                12.0f,
                "After a brigade is given a task waypoint (probe/screen/guard-flank/...), the same task is not re-issued for this long; a different " +
                "task bypasses the hold. Battle seconds on the battle clock, 1x-equivalent (600 per battle hour, so 12 = 72 battle seconds), the " +
                "same span at any time compression. Default 12. 0 disables.");
            OpportunityHysteresisMinHoldSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Opportunity Hysteresis Min Hold Seconds",
                10f,
                "Value is battle MINUTES (the key says Seconds only so existing cfg files keep working). Once the reinforcement-opportunity " +
                "doctrine commits to a category (AttackNow / DefensiveHold / WaitAndConsolidate / WithdrawalToFightLater) it is held for this many " +
                "minutes of battle time on the doctrine clock (frozen overnight) before it may change; an escalation to WithdrawalToFightLater " +
                "bypasses the hold. Default 10. 0 disables.");
            EnableTacticalRegimentDiagnostics = Config.Bind(
                "Tactical Diagnostics",
                "Enable Tactical Regiment Diagnostics",
                true,
                "Default ON. Read-only per-regiment delta logger for stuck-retreat / stuck-charge / unit-state bugs. Watches only regiments matched " +
                "by Tactical Regiment Diagnostic Names (default empty = nothing), at most one sample per unit per 0.5 s and 2000 lines per battle. " +
                "Also required for the map-knowledge watched-regiment rows.");
            TacticalRegimentDiagnosticNames = Config.Bind(
                "Tactical Diagnostics",
                "Tactical Regiment Diagnostic Names",
                "",
                "Comma-separated case-insensitive substrings matched against regiment names, e.g. 'Hampton,Tennessee'. " +
                "Default empty = nothing watched, nothing scanned: the regiment and map-knowledge diagnostics skip the unit walk entirely. " +
                "Each name you add makes those diagnostics read every unit's name once per AI cycle to find matches.");
            EnableTacticalMapKnowledgeDiagnostics = Config.Bind(
                "Tactical Diagnostics",
                "Enable Tactical Map Knowledge Diagnostics",
                true,
                "Default ON. Read-only. Emits one [TacticalMapKnowledge] scene-anchor inventory row per battle (objectives, entry points, AI fortifications, " +
                "fortification groups, road/railroad anchors) to the tactical tuning sidecar, plus cover/facing/objective context rows (at most one per unit " +
                "per 2 s, 1000 per battle) for regiments matched by Tactical Regiment Diagnostic Names. Does not change orders, facing, formation or pathing.");
            EnableTacticalContactFacingPulse = Config.Bind(
                "Tactical",
                "Enable Tactical Contact Facing Pulse",
                true,
                "Default ON. Battle-core facing-pulse producer (lowest priority) for AI command groups the posture producer left without an order " +
                "this tick: division scope only logs its axis intent; a brigade may get a frontage (formation) correction and, on a holding task " +
                "with no detached or player-owned subordinate, a group facing toward the closest visible enemy; up to 4 of its regiments in " +
                "contact may be rotated in place to face visible contact. Orders go through the battle-core order executor. Disable for rollback " +
                "without turning off Tactical Commander Mode.");
            EnableColumnDeployment = Config.Bind(
                "Tactical",
                "Enable Column Deployment",
                true,
                "Default ON. Battle-core facing-pulse producer: an AI infantry regiment that vanilla put in march column (a path longer than 60 m with no " +
                "enemy within 100 m) and that is fired on, shot in the flank, outflanked or in close contact with its brigade stops its march (the game's " +
                "own manual stop restores its line) and turns to face the pressure, instead of marching on in column until an enemy is within 100 m " +
                "(vanilla deploys it there, facing the way it was walking). A regiment on a bridge or crossing, of a withdrawing group, routed or mounted " +
                "is left alone. A regiment marked for the approach in line (Enable Approach Line Advance), one whose only cause is its brigade's perceived flank risk, and one with no bearing to turn to are deployed where they stand and keep marching, facing the way they walk, but only while marked for the approach in line: an unmarked " +
                "regiment would be columned again by the game within a second, so it is left in the game's column. " +
                "Needs Enable Tactical Contact Facing Pulse. Disable for rollback without turning off Tactical Commander Mode.");
            EnableApproachLineAdvance = Config.Bind(
                "Tactical",
                "Enable Approach Line Advance",
                true,
                "Default ON. User-approved skipping Prefix on the game's CheckFormationChange (2026-10-17). For an AI infantry regiment that Whiskey commands " +
                "(Commander Mode Active, never a player's unit, never the W&L feud instance) in a group whose current intent is a line advance or hold (not a " +
                "road route or a march column) and whose closest perceived enemy is inside 500 m, the game's re-column step is skipped, so the regiment keeps " +
                "its line and advances in line instead of falling back into march column. Independent of Enable Column Deployment and of the contact " +
                "facing pulse: only this switch, Commander Mode Active and the battle core's posture record decide which regiments are marked. The " +
                "mark reads the task the operations ledger committed to the group and the closest enemy the side perceives (seen or remembered), which change " +
                "only when something really changes, and is rebuilt every pass; a withdrawal, a road route or a lost ownership unmarks at once. With " +
                "Enable Column Deployment on, a column regiment inside 500 m also deploys to line first, where it stands, and keeps marching. Every other regiment runs the game's method " +
                "unchanged. Disable for rollback: vanilla's formation change runs for every regiment.");
            EnableTacticalReinforcementOpportunityDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Reinforcement Opportunity Doctrine",
                true,
                "Default ON. Refreshes a per-army reinforcement-opportunity decision (AttackNow / WaitAndConsolidate / DefensiveHold / " +
                "WithdrawalToFightLater) from BattleUnits.scheduledarrival and sideinformation.totalactiveforce, weighing the current force ratio, " +
                "arrival timing and commander aggression. Telemetry: TacticalReinforcementOpportunity events. Off: the decision stops refreshing, " +
                "which disables the opportunity replan trigger, the brigade task overrides that use it and the Vanilla Macro AI Sync fallback.");
            EnableVanillaMacroAiSync = Config.Bind(
                "Tactical",
                "Enable Vanilla Macro AI Sync",
                true,
                "Default ON. When the operations ledger has no macro opinion for an AI side, #44 uses the reinforcement-opportunity decision " +
                "(AttackNow / DefensiveHold / WaitAndConsolidate / WithdrawalToFightLater, mapped to macroai 0-2) as the fallback AIBattle.macroai. " +
                "Never writes for the W&L player side. Requires Enable Tactical Macro Stance Scorer and Enable Tactical Orchestrator Army.");
            EnableGeometricPlayTemplates = Config.Bind(
                "Tactical",
                "Enable Geometric Play Templates",
                true,
                "Default ON. When the army selects a playbook (DoubleEnvelopment / PreparedDefense / ...), looks up its geometric template " +
                "(army-relative rectangles, each tagged LeftWing / Center / Reserve / ...), projects it to world coordinates from the army centroid " +
                "and enemy direction, and assigns corps to rectangles by closest distance. The role allocator then uses the rectangle kind as the " +
                "role basis. Falls back to evidence-based allocation when no template exists or positions cannot be computed.");
            EnableBattleFogHonestPerception = Config.Bind(
                "Tactical",
                "Enable Battle Fog-Honest Perception",
                true,
                "Default ON. The battle core's AI perception: an enemy is seen when vanilla's own AI-side fog grid shows it (FogOfWar.UnitIsHidden " +
                "with the arguments vanilla uses at 127225) or it is in melee; a W&L / AI-vs-AI player-alliance side uses vanilla's iswithinfog. " +
                "An enemy that drops out of sight is remembered at its last position with fading weight (halved every 20 battle minutes, dropped " +
                "after 90). Every Whiskey battle reader (evidence, plays, posture, artillery, charge, feud targets) sees only seen or remembered " +
                "enemies, and reinforcements only once their entry point is seen. Falls back to vanilla's range lists while fog of war is off, " +
                "with HQ in the saddle, or before vanilla has painted the grid. Off = vanilla range-only perception for every side. Reads only.");
            EnableInfantryChargeDoctrine = Config.Bind(
                "Tactical",
                "Enable Infantry Charge Doctrine",
                true,
                "Default ON. Fills vanilla's dead AI infantry charge (MicroAICheckForCharges never fires): in an AI brigade in assault stance (4), " +
                "the two moving regiments closest to their target get combat behavior 3 when their own first range-list enemy is visible, within " +
                "vanilla's charge radius of the brigade HQ (maxchargeradius, 50 m shipped, so charges are rare), and weaker in morale by 0.15, " +
                "disordered or flanked; own morale at least 0.5, fatigue at most 0.6, no friendly in the lane, and (vanilla's unread pref No Micro " +
                "AI Charge If Outflanked) not while outflanked or under flanking fire; with Enable Experience And Drill AI Weighting on, only when " +
                "vanilla's own experience-weighted charge odds would pass. Vanilla's own odds check still decides the charge. Reverts " +
                "to behavior 2 when the charge resolves, the conditions fail or the brigade leaves assault, waits out vanilla's renew interval " +
                "(at least 120 battle seconds) after a refused charge, and restores vanilla's intended cancel: an AI infantry regiment left in " +
                "charge mode whose brigade is out of assault is taken out of it. Never on W&L player-owned or feud units; orders go through the " +
                "battle-core order executor (Tactical Commander Mode = Active). Off: arms nothing, hands back what it armed and stops the " +
                "restored cancel; AI infantry then reaches melee only by contact, as in vanilla.");
            EnableExperienceAndDrillAiWeighting = Config.Bind(
                "Tactical",
                "Enable Experience And Drill AI Weighting",
                true,
                "Default ON. Whiskey's battle decisions weigh troop quality the way vanilla's own do. Army attack/hold (reinforcement " +
                "opportunity doctrine, #44's macro opinion): vanilla's force balance, the strength ratio x (1 + own - enemy mean morale) x " +
                "(1 + own - enemy mean experience); the attack/defend boundary is divided by 1 + commander initiative x vanilla's " +
                "initiative influence (0.7 shipped) and the withdrawal threshold is not, as vanilla divides its attack and defend " +
                "triggers but not its retreat trigger. Group stance sectors and #44's odds: own and enemy strength x vanilla's blends " +
                "1 - w + experience x w and 1 - w + morale x w (expinfluenceonaidecisions / moraleinfluenceonaidecisions, 0.25 shipped), " +
                "a group's own experience and morale against the closest visible enemy's. Withdrawal: an AI regiment under fire whose losses reach 80% of its own rout point (vanilla's unbearable-losses " +
                "trigger, which rises with experience, drill and leadership) falls back before it breaks. Infantry charge: armed only when " +
                "vanilla's own charge odds (strength, experience, morale, fatigue of both) would pass. Enemy experience is what the player " +
                "sees: the experience stars of enemy regiments while they are visible, remembered after they drop out of sight. Reads only. " +
                "Off: headcount ratios and sectors, undivided thresholds, no break-point withdrawal, no charge odds pre-filter. The battle " +
                "rules read this flag once per battle (a change applies from the next battle).");
            EnableDeploymentAndMarchDoctrine = Config.Bind(
                "Tactical",
                "Enable Deployment And March Doctrine",
                true,
                "Default ON. The battle AI marches and deploys on what it can know. March: a long move (400 m or more) through woods or swamp is " +
                "routed over a road when that is at least 20% faster than the way vanilla's pathfinder takes (up to two via points; the group takes " +
                "march column first). Deployment (#60): the AI's objective-path groups are re-placed from the nearest enemy-owned entry point (never " +
                "the player's hidden deployment) and about a third of the eligible direct children are placed 425 m back as a reserve echelon. " +
                "OFF = vanilla's own pathing and placement. The slice's bug fixes (perceived covered crossings and flanking, the non-mutating path " +
                "probe, #60 writing through the order executor, the reinforcement re-path measurement) are not behind this flag.");
            EnableBattleReconnaissance = Config.Bind(
                "Tactical",
                "Enable Battle Reconnaissance",
                true,
                "Default ON. The fog-honest battle AI goes and looks. Cavalry reconnaissance: when an enemy-side entry point has not been " +
                "watched for an hour, a stretch of the front has gone unobserved, or intel confidence is below 0.35 (straight ahead " +
                "before any contact, the nearest remembered enemy after it), one standing AI cavalry regiment of a settled brigade rides to look (150 m short of an " +
                "entry point, vanilla's out-of-contact distance), stops on first sight, watches for 3 battle minutes and rides back to its " +
                "brigade; never toward a visible enemy at least as strong, at most 4 missions per side per battle hour. Reconnaissance in " +
                "force: in the army's Probe phase, a side that is not the battle's defender advances one AI group per front band with no " +
                "visible enemy 200 m (infantry sight) in screen stance and stops it on contact, once per band per battle hour; a probe " +
                "that met no enemy at least its size holds the ground it gained until its ledger task changes. Scout and " +
                "Probe tasks advance by such bounds along the front (toward the objective before any sighting) instead of to an " +
                "objective stand-off; screening and probing brigades (and their regiments) with nothing in sight turn to face the " +
                "stalest unwatched enemy entry point, flank guards too; holding and reserve tasks keep their front facing. Skirmisher screen: AI " +
                "skirmisher companies of a screening (or advancing attack-stance) line stand 100 m ahead (vanilla's contact distance) " +
                "instead of 30 m while nothing is in their range, and fall back to vanilla's 30 m on contact. Never W&L player-owned " +
                "units; orders go through the battle-core order executor (Tactical Commander Mode = Active). Off: no recon or probe " +
                "moves, the shipped objective stand-off for Scout/Probe tasks, vanilla's 30 m skirmish line.");
            EnableTacticalDeploymentObserver = Config.Bind(
                "Tactical Diagnostics",
                "Enable Tactical Deployment Observer",
                true,
                "Default-on. Read-only deployment/redeployment observer for battle-start and end-of-day placement. Logs [TacDeployObs], [TacDeployObsMove], and [TacticalDeploymentPhase] rows; does not change unit positions or orders.");
            EnableTacticalDeploymentTerrainDiscipline = Config.Bind(
                "Tactical",
                "Enable Tactical Deployment Terrain Discipline",
                true,
                "Default ON. #60: after vanilla AI deployment, moves AI groups that vanilla placed in this pass and that stand in water or outside " +
                "the deployment zone to the best nearby safe point (an immediate placement through the battle core's order executor). The assumed " +
                "enemy bearing (what the side sees, else the nearest enemy entry point) only adds a facing bonus to candidate points. Fortification-line " +
                "placements, the player's side and the W&L command's AI group are left alone. Independent of Enable Deployment And March Doctrine, " +
                "which governs the fog-honest re-placement and the reserve echelon.");
            TacticalDeploymentTerrainMaxCorrectionMeters = Config.Bind(
                "Tactical",
                "Tactical Deployment Terrain Discipline Max Correction Meters",
                60f,
                "Maximum distance sampled when correcting an AI deployment terrain/zone failure. The sample rings are fixed at 0/8/16/32/48/60 m, " +
                "so values above 60 change nothing.");
            TacticalDeploymentTerrainMaxCandidates = Config.Bind(
                "Tactical",
                "Tactical Deployment Terrain Discipline Max Candidates",
                16,
                "Maximum sample points tried around a failed AI deployment placement.");
            TacticalDeploymentFacingPreferredDeltaDegrees = Config.Bind(
                "Tactical",
                "Tactical Deployment Facing Preferred Delta Degrees",
                90f,
                "During deployment correction, candidate points whose facing is within this many degrees of the visible enemy bearing score higher " +
                "(a soft bonus, not a limit).");
            EnableConstructionIntentLedger = Config.Bind(
                "Construction",
                "Enable Construction Intent Ledger",
                true,
                "Default ON. Computes the per-alliance construction intent weekly (sooner when its inputs change) during the daily strategic " +
                "review. Feeds construction telemetry, Construction Site Steering and Telegraph AI; off disables those.");
            EnableNeedDrivenPolicySelection = Config.Bind(
                "Strategic",
                "Enable Need-Driven Policy Selection",
                true,
                "Default ON. When an alliance the daily review runs for has nothing in policy research, starts the policy its war needs most " +
                "(credit, manpower, recognition, blockade, industry...), with the faction's vanilla AI personality list as a strong preference and " +
                "irreversible acts never started by Whiskey or vanilla's fallback until their need is high. Runs for every AI alliance and a W&L career player's alliance at any " +
                "rank (Commander-in-Chief included); never for a non-career player alliance. Never cancels research and never starts one while " +
                "any research (a prestige purchase included) is in progress. Off: vanilla Policies.CheckAIPolicyChange picks. The player's in-progress research is protected " +
                "from vanilla's emergency cancel under the master switch either way.");
            EnableDefenseIntentLedger = Config.Bind(
                "Defense Intent Ledger",
                "Enable Defense Intent Ledger",
                true,
                "Default ON. Computes the daily defense-intent ledger and uses it to keep forbidden units out of vanilla defensive operations " +
                "(#25). Off: no [DefenseIntent] output and no candidate filtering.");
            DefenseIntentVerboseLogging = Config.Bind(
                "Defense Intent Ledger",
                "Defense Intent Verbose Logging",
                true,
                "Default ON. Emits the daily defense-intent package rows every day instead of only on change, plus defensive-operation candidate filter/revert rows and coastal-defense custom-order rows. Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
            EnableConstructionSiteSteering = Config.Bind(
                "Construction", "Enable Construction Site Steering", true,
                "Default ON. AI factions only: during vanilla Economy.UpdateCompanyFoundations, multiplies vanilla's private-building construction probabilities (bestiipplacesprob) by the Construction Intent Ledger's steering score (clamped 0.1-3). Keeps vanilla's candidate per building group, its build-rate cap and its subsidy/funding/rating gates, and restores the slots vanilla did not consume.");
            FortConstructionGovernorEnabled = Config.Bind(
                "Construction", "Fort Construction Governor Enabled", true,
                "Default ON. Filters saturated vanilla fort construction sites before AICampaign.CheckFortConstruction so either side cannot stack excessive forts in one local area unless threat justifies more.");
            EnableTelegraphAI = Config.Bind(
                "Construction", "Enable Telegraph AI", true,
                "Default ON. AI factions only: when the construction intent updates (weekly), may start vanilla telegraph-station construction wishes that extend the faction's connected chain toward a large, uncovered, enemy-free field command, paid through vanilla. " +
                "Capped by Max Active Telegraph Constructions Per Faction, skipped in the EmergencyHold fiscal posture, and a final placement that fails its connection/support/safe-rear checks is removed with the wish cost refunded (treasury, subsidy funding and spending stats as charged; the interest re-roll vanilla did when charging stays). A wish vanilla itself drops for lack of a site is not refunded, as in vanilla.");
            ConstructionTelemetryEnabled = Config.Bind(
                "Construction",
                "Construction Telemetry",
                true,
                "Default ON. Emits the monthly construction heartbeat (intent summary plus observed vanilla construction starts). Intent-change " +
                "rows are emitted regardless.");
            ConstructionVerboseLogging = Config.Bind(
                "Construction", "Construction Verbose Logging", true,
                "Default ON. Emits the daily construction-intent row every day instead of only on change, every actual construction start, and telegraph no-start/reject reasons (once per alliance and reason). Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
            MaxActiveTelegraphConstructionsPerFaction = Config.Bind(
                "Construction",
                "Max Active Telegraph Constructions Per Faction",
                1,
                "Telegraph AI starts no new station while the faction already has this many telegraph stations under construction or damaged plus " +
                "pending AI placements (vanilla's and Whiskey's together). Default 1; a negative value counts as 1.");
            EnableArtilleryCombineGunSync = Config.Bind(
                "Campaign", "Enable Artillery Combine Gun Sync", true,
                "Default ON. After BattleUnits.CombineUnits merges artillery on the campaign map, sets the receiving battery's guns (and the surviving source battery's, if it was not deleted) to GetSollGuns(true) and refreshes gun stats — the value the next Regiment.Initialize would compute on load. Set false for rollback to vanilla, which leaves guns stale until the next load.");
            // Campaign logistics vanilla bug fixes (docs/bug-fixes/vanilla-campaign-logistics.md).
            EnableRaidSupplyThresholdFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Raid Supply Threshold Fix", true,
                "Default ON. Vanilla raid forces never pick a target: RaidForce.SearchObjective needs GetGroupSupplyState >= 'minimum supply level to continue offensive operations' (0.75), but that function halves its 0-1 inputs and never exceeds 0.5. During the search the threshold is put on the same halved scale, so a raid force sets out once its real supply reaches the configured level. Raid groups under the W&L player's command stay vanilla. Off: vanilla (raid forces wait forever).");
            EnableFoggedAiCampFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Fogged AI Camp Fix", true,
                "Default ON. Vanilla never lets an AI army the player cannot see encamp, take winter quarters or entrench (UpdateCampsAndEntrenchments requires the unit to be out of the player's fog), so hidden AI armies lose the winter supply/movement relief, readiness, drill, morale and entrenchment effects. AI land units in the player's fog now camp as visible ones do; the camp and entrenchment models (and the encamp bugle) stay hidden until the player can see the unit. Player units are never touched. Off: vanilla (visible units' models are still re-shown).");
            EnableFoggedAiCampPlacementBudget = Config.Bind(
                "Campaign Bug Fixes", "Enable Fogged AI Camp Placement Budget", true,
                "Default ON. With the Fogged AI Camp Fix, hidden AI armies that halted for the night all reach vanilla's camp time in the same few seconds, and each camp placement fits its model to the ground. A hidden AI unit without a camp places one only while this frame's placement budget ('Fogged AI Camp Placements Per Frame') has room; the others place theirs on a later regular cycle (vanilla re-checks each unit every 25 frames). Each unit still gets its camp as in vanilla; only the moment is spread. Units that already have a camp are never deferred. Off: every hidden AI camp is placed on the first cycle it qualifies.");
            FoggedAiCampPlacementsPerFrame = Config.Bind(
                "Campaign Bug Fixes", "Fogged AI Camp Placements Per Frame", 2,
                "Default 2. Most new camps placed per frame for AI units the player cannot see, when 'Enable Fogged AI Camp Placement Budget' is on. A value below 1 counts as 1.");
            EnableFoggedAiEmbarkationFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Fogged AI Embarkation Fix", true,
                "Default ON. Vanilla creates a unit's train, river boat or ship model (which is its embarked state) only while the player can see it, so an AI land unit in the player's fog is never embarked: no rail speed, it halts at night, uses no transport capacity, locomotives or ships, skips the sea embarkation delay and keeps full rations and reinforcements. AI land units in fog now embark as visible ones do, with the transport model hidden until the player can see the unit; those models share one never-used ship-firing sound array instead of adding 20 sound objects each. Fleets and player units are never touched. Off: vanilla (visible units' models are still re-shown).");
            EnableHiddenTransportFlagDeferral = Config.Bind(
                "Campaign Bug Fixes", "Enable Hidden Transport Flag Deferral", true,
                "Default ON. With the Fogged AI Embarkation Fix, a transport model created for an AI unit the player cannot see is hidden at once, yet vanilla still sets up its flag (texture loads and a material copy the model's removal does not free). That setup is skipped for such a model and run when the model is shown, which is when vanilla itself would have created it; a model removed while hidden never sets its flag up. Off: the flag is set up when the model is created (models already deferred still set theirs up when shown).");
            EnableOrphanCampCleanup = Config.Bind(
                "Campaign Bug Fixes", "Enable Orphan Camp Cleanup", true,
                "Default ON. Vanilla never removes a unit's camp or entrenchment when the unit is destroyed (merged, disbanded or wiped out), so its tents and earthworks stay on the map. They are removed with the unit, the way vanilla removes them when a unit breaks camp. Off: vanilla.");
            EnableAiAreaMembershipRefresh = Config.Bind(
                "Campaign Bug Fixes", "Enable AI Area Membership Refresh", true,
                "Default ON. Vanilla fills each AI area's IIP, building and town lists only during the first two AI runs, so depots and other buildings created later never count toward the area's supply capacity or as offensive objectives. Registers them into their area with vanilla's own position rule (no duplicates) and drops destroyed buildings once per full pass. Off: vanilla.");
            EnableWinterOffensiveModifierFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Winter Offensive Modifier Fix", true,
                "Default ON. Vanilla's winter roll for AI offensives uses the 'Probability modifier for defensive operations in winter' (0.2) and never reads the offensive one (0.05). Uses the offensive modifier for vanilla's offensive roll and for the campaign planner's chapter tempo roll, which mirrors it; it scales the roll's time term (the idle-time term is not scaled, as in vanilla). Off: vanilla.");
            EnableSupplyDepotBuilderFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Supply Depot Builder Fix", true,
                "Default ON. In vanilla's depot job a unit near any depot its side started, even a finished, empty or captured one, is skipped (no move to resupply, no depot wish), and depot 'builders' (kept out of offensive, defensive and fort jobs) are counted within command range and several per depot, while construction only advances with a unit inside bugle range. Only depots still under construction skip a unit, and each such depot keeps one builder, its smallest unit inside bugle range. Off: vanilla.");
            EnableSubordinateStrengthGrabFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Subordinate Strength Grab Fix", true,
                "Default ON. Vanilla's BattleUnits.GrabSubordinateByStrength compares each brigade only with the last one picked instead of the running total, so AI transfers between armies move more than the deficit they were sized for. The pick now stops at the requested strength, with vanilla's other filters. Off: vanilla.");
            EnableOffensiveWeaponTrimFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Offensive Weapon Trim Fix", true,
                "Default ON. When vanilla trims surplus units from an AI offensive it credits each with its weapon factor squared into the blend instead of the blended factor used when the unit was added. A narrow IL rewrite of AICampaign.CheckOffensiveMovements (left as vanilla if the code pattern is not found) applies the same blend; this flag switches it live. Off: vanilla.");
            EnableAiRandomCandidatePickFix = Config.Bind(
                "Campaign Bug Fixes", "Enable AI Random Candidate Pick Fix", true,
                "Default ON. Eight vanilla campaign-AI random picks (fleet port, fleet, ship type, recruit division and corps, recruiting IIP, army for a new corps, raid-force town) use Random.Range(0, Count - 1), which never returns the last candidate. A narrow IL rewrite (left as vanilla where the code pattern is not found) lets the pick include it; this flag switches it live. Off: vanilla.");
            EnableConfiscationSupplyFlowFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Confiscation Supply Flow Fix", true,
                "Default ON. The Confiscation Act should improve supply on enemy soil by 'Confiscation, supply flow improvement' (1.15, manual: +15%), but vanilla multiplies the resupply duration by it, making supply slower. The duration is divided by it instead. Off: vanilla.");
            // Armament vanilla bug fixes (docs/bug-fixes/vanilla-armament.md).
            EnableAutoresolveStandardGunFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Autoresolve Standard Gun Fix", true,
                "Default ON. In vanilla autoresolve a battery armed with weapon id 0, the 6-pounder Field Gun that every campaign issues as its standard artillery, inflicts no casualties: Autocalc.FightUnit only counts weapon ids above 0. A narrow IL rewrite (left as vanilla if the code pattern is not found) counts id 0 as armed, for every side including the W&L player; this flag switches it live. Off: vanilla.");
            EnableCombineWeaponStockFix = Config.Bind(
                "Campaign Bug Fixes", "Enable Combine Weapon Stock Fix", true,
                "Default ON. When two units merge, vanilla's weapon-stock bookkeeping counts a battery's men as guns (100-240 phantom guns per merge when the target's model is short), returns a partly merged battery's whole gun count, draws more guns than the target gains (downgrading it to the standard gun when that count is short), and adds a standard-armed target's whole holding to stock before re-arming it with the same model. The stock is set to the correct count (guns for batteries, men otherwise; only what changes hands) and the target keeps its model whenever the stock covers what it gains; a real shortfall still re-arms it with the standard weapon, as vanilla does. Every side, W&L player merges included. Off: vanilla.");
            EnableAiWeaponPurchaseOrderableFix = Config.Bind(
                "Campaign Bug Fixes", "Enable AI Weapon Purchase Orderable Fix", true,
                "Default ON. Vanilla AI weapon purchasing may order models it has researched but does not produce or import (Gatling, Coffee Mill and Williams Gun), which the player's order screen refuses; it pays the whole order up front for a delivery of up to two years. The AI now orders only models the order screen allows for its own alliance (researched and produced, or imported), choosing among them as vanilla does; units still receive the machine guns the projects stock. Every side, including the AI buying for the W&L player's alliance. Off: vanilla.");
            EnableGroupWeaponStrengthGuard = Config.Bind(
                "Campaign Bug Fixes", "Enable Group Weapon Strength Guard", true,
                "Default ON. Vanilla's group weapon factor divides by the group's armed men with no zero check, so a group with no armed men returns NaN, which corrupts the campaign AI's area weapon averages and offensive outgunned tests. Such a group gets weapon factor 0. Off: vanilla.");
            EnableWlWeaponBudgetScopeFix = Config.Bind(
                "W&L", "Enable W&L Weapon Budget Scope Fix", true,
                "Default ON. Vanilla subtracts every alliance's weapon orders, the enemy's included, from the W&L career player's federal weapon budget. Only orders of the player's own alliance (his own, or the AI's for his alliance) spend it now. Off: vanilla.");
            EnableWlWeaponBudgetPrestigeGateFix = Config.Bind(
                "W&L", "Enable W&L Weapon Budget Prestige Gate Fix", true,
                "Default ON. Buying weapon budget as commander-in-chief costs 500 prestige, but vanilla only checks that prestige is at least -500, so it can be bought into negative prestige. The purchase now needs the prestige to pay for it; the charge is vanilla's. Off: vanilla.");
            FixPerkIdMismatches = Config.Bind(
                "Campaign", "Fix Perk Id Mismatches", true,
                "Default ON. Vanilla reads some perk ids from the wrong perk table (battle, campaign or fleet). With this on each perk does what " +
                "its own tooltip says: Expert Scouts sight and concealment only for battle units with Expert Scouts / Expert Skirmishers (not a " +
                "corps with Ambulance Corps or Bureau of Military Information, a fleet, or a battle HQ); a regiment's wounded mortality and " +
                "forced-march sickness from its corps' and army's Ambulance Corps and Foot Cavalry; autoresolve battles use Sharpshooters, Zouave " +
                "and River Expedition instead of their id neighbours; Foot Cavalry speed and marching experience only for land units, Sea " +
                "Raiders trade-node raiding only for fleets; battle HQs no longer run, tire less, take cover or gain engineers from campaign perks " +
                "read as battle perks; and Cold Steel III, not an impossible Expert Scouts/Cold Steel pair, keeps a unit from doubting its charge. " +
                "Same rule for every side, including the W&L player. Set false for rollback to vanilla.");
            FixPerkEffectScope = Config.Bind(
                "Campaign", "Fix Perk Effect Scope", true,
                "Default ON. Applies two perks only where their tooltips say: Foot Cavalry's forced-march speed only while the unit is on a " +
                "forced march (vanilla applies it to every campaign move), and Embedded Reporters' fame bonus for the unit that has the perk at " +
                "that unit's level (vanilla ignores a corps' own perk when the corps sits under an army, and reads the level from the corps even when its army holds the perk). Set false for rollback to vanilla.");
            EnableCampaignPlanner = Config.Bind(
                "Campaign Planner", "Enable Campaign Planner", true,
                "Default ON. Runs the live theater campaign planner in the daily strategic review for each AI alliance (snapshot, fog-honest enemy beliefs, " +
                "commander profiles, theater plan, allocation) and logs it. OFF = no planner; vanilla campaign AI and the kept strategic ledgers only.");
            EnableCampaignPlannerExecutors = Config.Bind(
                "Campaign Planner", "Enable Campaign Planner Executors", true,
                "Default ON. Lets the planner steer vanilla: offensive target pointers, campaign objective, theater boxes, availability, capital pledges, " +
                "transfers, MoveUnitTo, depots, forts, railroads. Never moves or orders the W&L player's own command. OFF = plan and telemetry only.");
            EnableVanillaDefensiveInterception = Config.Bind(
                "Campaign Planner", "Enable Vanilla Defensive Interception", true,
                "Default ON. While the campaign planner runs for an AI alliance (a fresh published plan, executors on), vanilla's own defensive operations " +
                "(CheckForDefensiveOperations, the side's only reaction to an advance onto its soil) choose from the side's whole formation list again: " +
                "the defense intent ledger's candidate filter (patch #25) removes nothing and reverts nothing, and only logs what it would have removed " +
                "(DefenseIntent filter-observed). The planner's withheld formations (reserve, capital guard, depot builder, scout, recovering) stay out " +
                "of vanilla's defence through the availability patch. OFF = #25 removes the ledger's forbidden formations from vanilla's candidates and " +
                "reverts a forbidden commit, as before. Needs Enable Campaign Planner and Enable Campaign Planner Executors; without them #25 always filters.");
            EnableStrategicForesightPicture = Config.Bind(
                "Campaign Planner", "Enable Strategic Foresight Picture", true,
                "Default ON. Each AI side plans from a fog-honest picture of the whole enemy army: the enemy's public total (the strategy screen's " +
                "men fielded) less every army the side believes in is presumed at the enemy capital and the enemy-held objectives that are not " +
                "scouted, the sectors facing them hold against those men at the same ratio as against seen armies, the hold on a front is " +
                "released for a target only when every army it faces is at the target, and the capital floor counts every enemy that can reach " +
                "the capital before the capital's own department can concentrate, with vanilla's own baseline. OFF = the earlier rules: a " +
                "presumption under every unscouted sector, the capital threat within the relocation range and a floor without vanilla's baseline, " +
                "the nearest-belief hold release, the department sum of the sector needs, and a minimum strength of 5000. The eligibility floor " +
                "(a candidate needs a capacity of at least the minimum strength) does not read this flag: it applies ON or OFF. Needs Enable Campaign Planner.");
            EnableSpottingMarkBeliefs = Config.Bind(
                "Campaign Planner", "Enable Spotting Mark Beliefs", true,
                "Default ON. Vanilla draws a spotting mark on the W&L player's map for each enemy group he cannot see, within 25 map units of the truth, " +
                "and gives the AI no such channel. With this on both sides plan from them: the player's side reads the newest mark of each hidden enemy " +
                "group off the campaign map, and an AI side draws its own marks with vanilla's formula on its own fog grid (seeded, never Unity's random). " +
                "A mark is a belief that counts in every strength sum and the picture's accounting, widens from its radius, and never scouts a target, " +
                "lands a group, raises a raid or counts as contact. OFF = no mark detections on either side. Needs Enable Campaign Planner.");
            FixSpottingMarkTiming = Config.Bind(
                "Campaign Planner", "Fix Spotting Mark Timing", true,
                "Default ON. Vanilla's spotting marks (the enemy symbols on the W&L player's campaign map) read the unit concealment array where the " +
                "time-factor pref is meant (BUG-INTEL-001), so a hidden enemy group gets a new mark every time it moves 50 map units. With this on " +
                "vanilla's mark timer reads the time-factor pref (0.1 and 0.006 game years), so marks appear over days; the AI side's own marks " +
                "follow the same rate. It changes the player's own map, whether or not the campaign planner runs. OFF = vanilla's timing on both " +
                "sides. Needs fog of war.");
            EnableCoveringForce = Config.Bind(
                "Campaign Planner", "Enable Covering Force", true,
                "Default ON. Each AI side keeps a covering force between the enemy capital and its own: the hold reserves of the sectors that face " +
                "enemy armies on the road between the two capitals are posted, as one force, at the own-held town nearest the point one relocation " +
                "range from the enemy capital (forward) or from its own capital (rear), forward when the force's strength there reaches the strength " +
                "the enemy capital's neighbourhood calls for and rear otherwise, and are not drawn into a main effort unless it is aimed at the " +
                "enemy capital itself. A member counts toward its own sector's hold wherever it stands, and vanilla's defence may use it while an " +
                "enemy stands on the side's soil near the post. OFF = no covering force: the axis sectors hold where they stand, as the other sectors " +
                "do. Needs Enable Campaign Planner.");
            EnableMainEffortCommitment = Config.Bind(
                "Campaign Planner", "Enable Main Effort Commitment", true,
                "Default ON. A formation the mover has sent toward the main effort's objective stays in that push through a readiness dip, for as " +
                "long as the planner keeps the objective (a long march lowers readiness, and the push would otherwise lose its corps and be dropped). " +
                "Vanilla refuses a new march onto enemy ground at readiness step 0 or 1, so the mover issues none and retries none there. When the " +
                "planner ends the push (the objective accomplished, unavailable, switched, or its odds below the required ratio for three reviews) its " +
                "corps are stopped where they stand by vanilla's own campaign stop and taken off vanilla's offensive list, then placed by the holds, " +
                "the capital reserve and department reinforcement. Capital guards are taken off the offensive list too. OFF = the readiness rule for " +
                "every mass member, no exemption for the last mass, no stop, no de-list and no readiness skip (the last mass is still recorded). " +
                "Needs Enable Campaign Planner.");
            EnableDepartmentReinforcement = Config.Bind(
                "Campaign Planner", "Enable Department Reinforcement", true,
                "Default ON. When the planner has no main effort, the department whose hold is shortest (the strength standing in its sectors " +
                "against what they need to hold) is given one formation from a department whose hold has surplus: the soonest one within ten " +
                "march-days of the short department's neediest sector, taken only if the department it leaves still meets its own hold without " +
                "it. One formation at a time per department; its home department changes only when it arrives. A formation whose push has ended " +
                "is placed the same way. OFF = no department pick or move: the moves under way are dropped and their formations hold where they " +
                "stand. Needs Enable Campaign Planner.");
            EnableCampaignReconnaissance = Config.Bind(
                "Campaign Planner", "Enable Campaign Reconnaissance", true,
                "Default ON. Vanilla's campaign AI never scouts, so the planner plans reconnaissance itself in the daily review: cavalry-majority " +
                "screens toward the oldest or most uncertain believed armies and along approaches no viewer covers, pickets on threatened towns, an " +
                "advance guard ahead of a main effort with strength to spare, and a belief-driven probe short of a main objective whose picture is stale. " +
                "A formation on a mission gets the Screen formation directive and the Scouting perk role, is sent to its post by a plain move " +
                "(no forced march) and kept there, and the [Recon] and [ReconMission] rows log the missions. A cavalry-majority screen also gets the " +
                "Scouting cavalry order (the one vanilla's AI never gives) and loses it when recalled or cleared. " +
                "Never uses the W&L player's own command. A formation the allocation gives a marching role is released from its mission. " +
                "OFF = every mission ends at the next review (a recalled formation stays where it stands, vanilla's again), screens lose " +
                "the Scouting order and nothing is moved; needs Enable Campaign Planner and " +
                "Enable Campaign Planner Executors for the moves and orders.");
            EnableCampaignFogHonestBeliefs = Config.Bind(
                "Campaign Planner", "Enable Campaign Fog-Honest Beliefs", true,
                "Default ON. Enemy armies are known only when vanilla's campaign fog shows them to the side (FogOfWar.UnitIsHidden, 127225 arguments; " +
                "fows[1] for the non-player AI, iswithinfog for the W&L player's AI side) or by contact; unseen armies are remembered and decay. " +
                "OFF (or Enable Campaign Planner OFF) = vanilla's omniscient enemy lists.");
            EnableCommanderAppointmentPlanner = Config.Bind(
                "Campaign Planner", "Enable Commander Appointment Planner", true,
                "Default ON. Each AI alliance puts its best available commanders (vanilla's competence metric GetAverageAttributeValue, vanilla's " +
                "appointment pool and rank rules) on its main-effort commands, at most one appointment a day; the historical succession events add a " +
                "bonus when their conditions hold; appointees are shielded from vanilla's job-24 swap for 60 days. Never appoints or displaces the W&L " +
                "player or his officers, and never changes a command that holds the player's. OFF = vanilla job 24 only and no succession swaps.");
            EnableSupplyDepotSteering = Config.Bind(
                "Campaign Planner", "Enable Supply Depot Steering", true,
                "Default ON. When the planner's main effort would advance past its supply reach, a formation it picks marches to a site along the " +
                "axis and, once there, places a vanilla supply depot construction wish (the same wish vanilla's depot job places), then stays " +
                "out of vanilla's offensive and defensive pools until the depot stands or 20 days pass. The treasury is charged as for any " +
                "depot. Never uses the W&L player's own command. Needs Enable Campaign Planner and Enable Campaign Planner Executors; OFF = " +
                "vanilla's depot job only.");
            EnableFortSteering = Config.Bind(
                "Campaign Planner", "Enable Fort Steering", true,
                "Default ON. Vanilla's fort job orders the visiting unit onto the first fortification site in its list that fits. When a site on " +
                "the planner's main axis or in a threatened sector fits the unit, the others are hidden for that call, so vanilla orders the " +
                "fort there (the site the construction ledger ranks highest first). Vanilla's one-order-per-side rule, its range and builder " +
                "tests are unchanged. Works with or without Fort Construction Governor Enabled. Needs Enable Campaign Planner and Enable " +
                "Campaign Planner Executors; OFF = vanilla's list order.");
            EnableRailroadSteering = Config.Bind(
                "Campaign Planner", "Enable Railroad Steering", true,
                "Default ON. Vanilla rolls every predefined railroad line independently each tick. With this on the roll keeps vanilla's exact " +
                "chance and number of starts (including a success on a line it cannot start, which starts nothing) and only chooses which " +
                "startable line begins: the one that connects own supply to the main axis or a threatened sector. Not used for the player's " +
                "alliance, the W&L commander-in-chief's side, Europe, or while vanilla's job machine is paused (vanilla's own roll runs then). " +
                "Needs Enable Campaign Planner and Enable Campaign Planner Executors; OFF = vanilla's roll.");
            EnableGarrisonManning = Config.Bind(
                "Campaign Planner", "Enable Garrison Manning", true,
                "Default ON. Vanilla defends only the capital ahead of an attack; key towns, rail hubs, ports, depots and forts away from it are " +
                "held by whatever happens to stand there. With this on, an own objective town, rail hub, port, built depot or fort that a " +
                "believed enemy army threatens (vanilla's own capital threat weighting, on what the side has seen) gets a garrison: the " +
                "smallest fighting formation that covers the need (else the nearest two together), from those that would otherwise just hold or sit in reserve, never the main " +
                "effort, the capital guard, a scout or the W&L player's own command. It marches to the post and holds it, out of vanilla's " +
                "offensive and defensive pools, until no army has been near for a fortnight; at most six posts at a time. The secession " +
                "garrison for Kentucky and Missouri is separate and not behind this. Needs Enable Campaign Planner; the march also needs " +
                "Enable Campaign Planner Executors; OFF = no ordinary post.");
            EnableRearmPriority = Config.Bind(
                "Campaign Planner", "Enable Rearm Priority", true,
                "Default ON. Vanilla re-arms its units in the order they stand in the scene, twice a game day, so the units that happen to come " +
                "first take the scarce new models. With this on the AI alliances' units are re-armed in the order of the planner's roles: the " +
                "main effort first, then support, reserve, hold, capital guard, depot builders, scouts and garrisons, then units the plan does " +
                "not name, and units recovering or in transit last. Vanilla's own rule for which model a unit takes, its stock checks and its " +
                "skip of the W&L player's units are unchanged; only the order changes, and vanilla's list order is restored afterward. Not used " +
                "for the new-campaign equipping pass, the player's alliance outside W&L, the W&L commander-in-chief's side, Europe, or while " +
                "vanilla's job machine is paused. Needs Enable Campaign Planner and Enable Campaign Planner Executors; OFF = vanilla's order.");
            EnableOrdnancePurchaseSteering = Config.Bind(
                "Campaign Planner", "Enable Ordnance Purchase Steering", true,
                "Default ON. When vanilla's AI orders rifles or guns it picks at random among the researched or imported models that beat its " +
                "own average, ignoring price, delivery time and rate of fire, and can order models the order screen refuses (Gatling, Coffee " +
                "Mill, Williams gun). With this on the model is the best of those same candidates that the order screen allows, by value per " +
                "cost, delivery time, battle value (rate of fire counted in full), standardization and an edge over the enemy arms the side has " +
                "actually seen. Vanilla still decides when to buy, how much, whether the credit and the capital allow it, and charges the " +
                "order; with no allowed candidate vanilla's pick stands. Never used for the W&L commander-in-chief's side, the player's " +
                "alliance outside W&L, Europe, or while vanilla's job machine is paused. Needs Enable Campaign Planner and Enable Campaign " +
                "Planner Executors; OFF = vanilla's random pick.");
            FixAiPerkPickRange = Config.Bind(
                "Campaign", "Fix AI Perk Pick Range", true,
                "Default ON. Vanilla's campaign AI perk pick draws Random.Range(0, count - 1), so no AI army ever takes River Expedition " +
                "and no AI fleet Port Defender. With this on every perk id is a candidate, both for Whiskey's role-aware pick and for the " +
                "plain random pick vanilla makes where Whiskey does not steer (a W&L commander-in-chief's own alliance, Europe). Set false " +
                "for rollback to vanilla.");
            FixPontoonTrainCreekSpeed = Config.Bind(
                "Campaign", "Fix Pontoon Train Creek Speed", true,
                "Default ON. Pontoon Train promises +25/50/75% movement speed across small rivers; vanilla only lowers the creek path cost " +
                "(x0.75/0.5/0.25), so armies route through creeks they still cross at normal creek speed. With this on a division, corps, " +
                "army or army group with the perk (or under an army that has it) crosses creeks 1.25/1.5/1.75 times as fast, and its creek " +
                "path cost is 1/1.25, 1/1.5, 1/1.75 so routes are chosen by real travel time. Every side, including the W&L player. Set " +
                "false for rollback to vanilla.");
            FixAutoresolveEntrenchmentSide = Config.Bind(
                "Campaign", "Fix Autoresolve Entrenchment Side", true,
                "Default ON. In an autoresolved battle a defending side's entrenchments reduce the fire it takes, but vanilla always " +
                "reads side 1's engineering points, so when side 0 defends it is protected by its attacker's works. With this on each " +
                "defending side uses its own engineering points. Set false for rollback to vanilla.");
            FixEngineerBranchBonus = Config.Bind(
                "Campaign", "Fix Engineer Branch Bonus", true,
                "Default ON. The engineering-point bonus for an engineer regiment whose commander is an engineer officer (unitprefs " +
                "'if commander fits to branch of arms (engineers)', x1.5) tests the navy branch in vanilla, so no engineer officer " +
                "(Lee, McClellan, Meade, a W&L Engineer) ever gets it. With this on the bonus goes to engineer-branch commanders, in " +
                "battle deployment and in autoresolve. Set false for rollback to vanilla.");
            FastForwardAiCatchUp = Config.Bind(
                "Performance", "Fast Forward AI Catch Up", true,
                "Default ON. At 20x/50x campaign speed, lets Whiskey run a bounded number of extra vanilla campaign-AI job passes per frame so strategy does not fall as far behind calendar time.");
            FastForwardAiFrameBudgetMs = Config.Bind(
                "Performance", "Fast Forward AI Frame Budget Ms", 1.5f,
                "Maximum wall-clock milliseconds per frame Whiskey may spend on extra fast-forward AI catch-up passes.");
            FastForwardAi20xExtraPasses = Config.Bind(
                "Performance", "Fast Forward AI Extra Passes At 20x", 2,
                "Maximum extra vanilla AICampaign.UpdateUnitAI passes per frame at 20x campaign speed.");
            FastForwardAi50xExtraPasses = Config.Bind(
                "Performance", "Fast Forward AI Extra Passes At 50x", 4,
                "Maximum extra vanilla AICampaign.UpdateUnitAI passes per frame at 50x campaign speed.");
            FastForwardAiSlowFrameThresholdMs = Config.Bind(
                "Performance", "Fast Forward AI Slow Frame Threshold Ms", 8f,
                "When vanilla or extra fast-forward AI work exceeds this many milliseconds, skip extra catch-up passes for a cooldown window and emit a bounded diagnostic.");
            FastForwardAiSlowFrameCooldownFrames = Config.Bind(
                "Performance", "Fast Forward AI Slow Frame Cooldown Frames", 180,
                "Unity frames to skip extra fast-forward AI catch-up after a slow 20x/50x AI frame.");
            TimeSliceDailyReview = Config.Bind(
                "Performance", "Time-Slice Daily Review", true,
                "Default ON. Spreads the daily strategic review (campaign map, then per alliance the head job and every ledger) over a few " +
                "frames as an ordered job list, using at most Daily Review Frame Budget Ms plus one job per frame, instead of running it all " +
                "on the frame the date changes. Jobs keep their order and inputs; vanilla state they read (unit positions, strengths, " +
                "treasury) is read when each job runs, a few frames after the date change. Patches keep seeing the previous day's complete " +
                "results until the whole review finishes, then the new day is published at once. Set false for rollback to the single-frame review.");
            DailyReviewFrameBudgetMs = Config.Bind(
                "Performance", "Daily Review Frame Budget Ms", 4f,
                "Wall-clock milliseconds per frame the time-sliced daily strategic review may use before it continues on the next frame; " +
                "every frame runs at least one review job. Default 4.");
            CampaignAiGovernorEnabled = Config.Bind(
                "Performance", "Campaign AI Governor Enabled", true,
                "Default ON. Replaces vanilla AICampaign.Update high-speed pass scheduling with a bounded wrapper that preserves vanilla side effects.");
            CampaignAiGovernorMaxPasses20x = Config.Bind(
                "Performance",
                "Campaign AI Governor Max Passes At 20x",
                2,
                "Vanilla AICampaign.UpdateUnitAI passes guaranteed every frame at 20x (the lower of this and vanilla's 4), run regardless of the " +
                "frame budget. Passes the governor defers are owed and repaid later (up to 2x vanilla per frame under budget, forced past an " +
                "8-frame backlog), so no AI work is lost. Default 2.");
            CampaignAiGovernorMaxPasses50x = Config.Bind(
                "Performance",
                "Campaign AI Governor Max Passes At 50x",
                3,
                "Vanilla AICampaign.UpdateUnitAI passes guaranteed every frame at 50x (the lower of this and vanilla's 7), run regardless of the " +
                "frame budget. Passes the governor defers are owed and repaid later (up to 2x vanilla per frame under budget, forced past an " +
                "8-frame backlog), so no AI work is lost. Default 3.");
            CampaignAiGovernorFrameBudgetMs = Config.Bind(
                "Performance",
                "Campaign AI Governor Frame Budget Ms",
                3f,
                "Wall-clock milliseconds per AICampaign.Update frame above which the governor stops running passes beyond the guaranteed ones, " +
                "unless the owed backlog forces catch-up. Default 3.");
            ReuseCampaignAiUnitList = Config.Bind(
                "Performance", "Reuse Campaign AI Unit List", true,
                "Default ON. Every vanilla AICampaign.UpdateUnitAI pass copies the whole campaign unit list into a new list it only reads (8 bytes per unit per pass; the governor and fast-forward catch-up add passes). " +
                "A narrow IL rewrite (left as vanilla if the code pattern is not found) refills one reused list with the same units in the same order instead; this flag switches it live. Off: vanilla.");
            SkipDeadWlUnitNameRead = Config.Bind(
                "Performance", "Skip Dead W&L Unit Name Read", true,
                "Default ON. In a W&L career vanilla BattleUnits.IsUnitUnderCommander reads the unit's name and discards the comparison on every call (about 90-100 calls per frame), allocating a string each time. " +
                "A narrow IL rewrite (left as vanilla if the code pattern is not found) skips the read; no result changes. This flag switches it live. Off: vanilla.");
            EnableWlCampAccountingFix = Config.Bind(
                "W&L Camp", "Enable W&L Camp Accounting Fix", true,
                "Default ON. Corrects vanilla short-camp minimum allocation so credited station time sums to actual camp time.");
            EnableWlCampRestRewardCap = Config.Bind(
                "W&L Camp", "Enable W&L Camp Rest Reward Cap", true,
                "Default ON. Replaces vanilla Rest bonus curve of 6h neutral / 9h full reward with a field-duty curve.");
            WlCampRestNeutralHours = Config.Bind(
                "W&L Camp", "W&L Camp Rest Neutral Hours", WlCampRealism.DefaultRestNeutralHours,
                new ConfigDescription(
                    "Rest hours treated as neutral before health bonus begins. Vanilla Rest station minimum is 3h.",
                    new AcceptableValueRange<float>(0f, 5f)));
            WlCampRestMaxRewardHours = Config.Bind(
                "W&L Camp", "W&L Camp Rest Max Reward Hours", WlCampRealism.DefaultRestMaxRewardHours,
                new ConfigDescription(
                    "Rest hours needed for full positive Rest reward. Vanilla Rest max reward is 9h.",
                    new AcceptableValueRange<float>(3f, 9f)));
            EnableWlCampResponsiveBonusWeighting = Config.Bind(
                "W&L Camp", "Enable W&L Camp Responsive Bonus Weighting", true,
                "Default ON. Blends safe camp stations with recent station history so allocation payoff is less delayed. Responsive weighting is suppressed inside diary/event threshold checks.");
            WlCampRecentBonusWindowDays = Config.Bind(
                "W&L Camp", "W&L Camp Recent Bonus Window Days", 7,
                new ConfigDescription(
                    "Recent station-history window used for responsive camp bonus weighting.",
                    new AcceptableValueRange<int>(3, 14)));
            WlCampRecentBonusWeight = Config.Bind(
                "W&L Camp", "W&L Camp Recent Bonus Weight", 0.35f,
                new ConfigDescription(
                    "Blend weight for recent camp history. 0 disables responsiveness; 0.5 is the maximum Slice 1 weighting.",
                    new AcceptableValueRange<float>(0f, 0.5f)));
            EnableWlCampUnitPayoffTuning = Config.Bind(
                "W&L Camp", "Enable W&L Camp Unit Payoff Tuning", true,
                "Default ON. Softens command-count dilution for Drill, Motivate, Recruitment, and Readiness camp modifiers.");
            WlCampUnitEffectDivisorPower = Config.Bind(
                "W&L Camp", "W&L Camp Unit Effect Divisor Power", 0.5f,
                new ConfigDescription(
                    "Power applied to commanded-unit count for unit-facing camp effects. 0.5 uses square-root scaling; 1.0 is vanilla-equivalent.",
                    new AcceptableValueRange<float>(0.5f, 1.0f)));
            EnableWlCampVerboseTrace = Config.Bind(
                "W&L Camp", "Enable W&L Camp Verbose Trace", true,
                "Default ON. Emits W&L camp accounting and modifier trace rows, each kind only when its value signature changes. Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
            OverrideVanillaSettings = Config.Bind(
                "Strategic",
                "Override Vanilla Settings",
                true,
                "Default ON. At campaign creation locks Aggressiveness to Mediocre, Historic AI Personality to true and Difficulty to Locked " +
                "Difficulty (default Hard), and forces Fog of War, order delays, feuds, readiness gameplay and all automanage options on with those " +
                "menu controls frozen. Set false to allow vanilla settings " +
                "(advanced; may produce incoherent AI behavior).");
            LockedDifficulty = Config.Bind(
                "Strategic", "Locked Difficulty", 3,
                "Difficulty index 0-4 to lock when OverrideVanillaSettings is true. 0=Very Easy, 1=Easy, 2=Mediocre, 3=Hard (default — historical brutality), 4=Very Hard.");
            ForceAllSuccessionEvents = Config.Bind(
                "Diagnostics",
                "Force All Succession Events",
                false,
                "Default OFF. TEST MODE cheat: bypasses the date and war-state gates and fires all 12 scripted succession events on the first " +
                "monthly strategic tick, falling back to any AI army group when the historical one is missing. Forced events are not saved to the " +
                "sidecar. Corrupts a real playthrough; leave false outside testing.");

            if (!Enabled.Value)
            {
                Log.LogInfo($"{GUID} is disabled via config — skipping all patches.");
                return;
            }

            // Heuristic Community Hotfix detection — best-effort sentinel check. A direct
            // per-assembly lookup: AccessTools.TypeByName logs a HarmonyX warning on every
            // launch when the type is absent, which is the normal case.
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetType("CommunityHotfix", false) == null) continue;
                    Log.LogWarning("Community Hotfix detected — Whiskey Realism is INCOMPATIBLE. Strategic patches may not behave as expected.");
                    break;
                }
            }
            catch { /* ignore — best-effort only */ }

            _harmony = new Harmony(GUID);

            // Strategic-brain bootstrap before patches register so patches
            // never see a null Instance on their first invocation.
            StrategicCoordinator.Bootstrap();


            StartTelemetryRuntime();

            // PatchAll(assembly) reflects all [HarmonyPatch] attributed classes
            // (including nested types like AICampaignSaveLoadPatch.SavePatch /
            // .LoadPatch). Cleaner than enumerating each class explicitly.
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{GUID} v0.2.2 loaded — strategic-brain patches registered.");
        }

        // Per-frame hitch probe for the telemetry sidecar, and one doctrine clock
        // sample per frame so the clock sees every end-of-day frame even when no
        // doctrine reader runs overnight (a sample is skipped off the battle map).
        // Neither throws, but a Unity message must not either, so the guard stays.
        private void Update()
        {
            try
            {
                if (Enabled == null || !Enabled.Value) return;
                WhiskeyRealism.Telemetry.TelemetryFrameProbe.Tick(UnityEngine.Time.unscaledDeltaTime);
                WhiskeyRealism.Tactical.Orchestrator.TacticalBattleCoordinator.SampleDoctrineClock();
            }
            catch
            {
                // Telemetry must never break the frame.
            }
        }

        private void OnDestroy()
        {
            TelemetryRouter.Shutdown("plugin-destroy");
            _telemetryRuntime = null;
        }

        private void OnApplicationQuit()
        {
            TelemetryRouter.Shutdown("application-quit");
            _telemetryRuntime = null;
        }

        private void StartTelemetryRuntime()
        {
            var profile = TelemetryRouter.ParseProfile(TelemetryLoggingProfileRaw?.Value);
            var config = TelemetryRuntimeConfig.Create(
                Paths.GameRootPath,
                "0.2.2",
                ComputeAssemblySha256(),
                profile,
                TelemetryMaxTuningLogMb?.Value ?? 250,
                TelemetryFileRotateMb?.Value ?? 25,
                TelemetryRetainedSessions?.Value ?? 2,
                TelemetryEmitHumanSummary?.Value ?? true,
                TelemetryPerformanceWarnings?.Value ?? true,
                TelemetryCreateIssueBundleOnShutdown?.Value ?? false,
                message => Log?.LogWarning("[Telemetry] " + message),
                TelemetryQueueCapacity?.Value ?? TelemetryRuntimeConfig.DefaultQueueCapacity,
                TelemetryFlushMilliseconds?.Value ?? TelemetryRuntimeConfig.DefaultFlushMilliseconds,
                TelemetryFlushRows?.Value ?? TelemetryRuntimeConfig.DefaultFlushRows);

            _telemetryRuntime = TelemetryRuntime.Start(config);
            TelemetryRouter.AttachRuntime(_telemetryRuntime);
        }

        private static string ComputeAssemblySha256()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
                    return "-";

                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(location))
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
            catch
            {
                return "-";
            }
        }
    }
}
