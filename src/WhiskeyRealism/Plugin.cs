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
        internal ConfigEntry<bool> EnableTacticalMacroStanceScorer;
        internal ConfigEntry<bool> EnableTacticalGroupSectorStance;
        internal ConfigEntry<bool> EnableTacticalCommanderIntentDoctrine;
        internal ConfigEntry<bool> EnableTacticalLocalReactionDoctrine;
        internal ConfigEntry<bool> EnableTacticalChargeDenial;
        internal ConfigEntry<bool> EnableTacticalReserveIntentTelemetry;
        internal static ConfigEntry<bool> EnablePlayerOrderDoctrine { get; private set; }
        internal static ConfigEntry<bool> EnablePlayerOrderDoctrineDiagnostics { get; private set; }
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
        public static ConfigEntry<bool> EnableTacticalReinforcementOpportunityDoctrine;
        public static ConfigEntry<bool> EnableTacticalDeploymentObserver;
        public static ConfigEntry<bool> EnableTacticalDeploymentTerrainDiscipline;
        public static ConfigEntry<bool> EnableSinglePassObjectiveRecords;
        public static ConfigEntry<bool> EnableVanillaMacroAiSync;
        public static ConfigEntry<bool> EnableGeometricPlayTemplates;
        public static ConfigEntry<float> BattleGroupStanceMinIntervalSeconds;
        public static ConfigEntry<float> OpportunityHysteresisMinHoldSeconds;
        public static ConfigEntry<float> BrigadeTaskHoldSeconds;
        public static ConfigEntry<bool> EnableArtilleryAllStanceAudit;
        public static ConfigEntry<bool> EnableArtilleryWaypointVeto;
        public static ConfigEntry<float> ArtilleryAuditMinHoldSeconds;
        public static ConfigEntry<float> TacticalDeploymentTerrainMaxCorrectionMeters;
        public static ConfigEntry<int> TacticalDeploymentTerrainMaxCandidates;
        public static ConfigEntry<float> TacticalDeploymentFacingPreferredDeltaDegrees;
        internal ConfigEntry<bool> EnableTacticalHeavyPathThrottling;
        internal ConfigEntry<float> TacticalHeavyReviewCycleHours;
        internal ConfigEntry<float> TacticalHeavyReviewMinRealtimeSeconds;
        public const int TacticalMoraleSnapshotLedgerCapacity = 4;
        public static TacticalMoraleSnapshotLedger MoraleSnapshotLedger;
        internal ConfigEntry<bool> EnableConstructionIntentLedger;
        internal ConfigEntry<bool> EnableHistoricalOperationDoctrine;
        internal ConfigEntry<bool> EnableDefenseIntentLedger;
        internal ConfigEntry<bool> DefenseIntentVerboseLogging;
        internal ConfigEntry<bool> EnableConstructionSiteSteering;
        internal ConfigEntry<bool> FortConstructionGovernorEnabled;
        internal ConfigEntry<bool> EnableTelegraphAI;
        internal ConfigEntry<bool> ConstructionTelemetryEnabled;
        internal ConfigEntry<bool> ConstructionVerboseLogging;
        internal ConfigEntry<int> MaxActiveTelegraphConstructionsPerFaction;
        public static ConfigEntry<bool> EnableArtilleryCombineGunSync;
        internal ConfigEntry<bool> FastForwardAiCatchUp;
        internal ConfigEntry<float> FastForwardAiFrameBudgetMs;
        internal ConfigEntry<int> FastForwardAi20xExtraPasses;
        internal ConfigEntry<int> FastForwardAi50xExtraPasses;
        internal ConfigEntry<float> FastForwardAiSlowFrameThresholdMs;
        internal ConfigEntry<int> FastForwardAiSlowFrameCooldownFrames;
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

        internal bool TacticalOperationsLedgerEnabled =>
            TacticalCommanderModePolicy.RunsLedger(TacticalCommanderModeValue);

        internal bool TacticalOperationsLedgerAllowsWrites =>
            TacticalCommanderModePolicy.AllowsWrites(TacticalCommanderModeValue);

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

            Enabled = Config.Bind(
                "General", "Enabled", true,
                "Master enable. Disable to short-circuit every patch in this mod.");
            VerboseLogging = Config.Bind(
                "Diagnostics", "Verbose Logging", true,
                "Default ON. Emits campaign decision-trace rows: daily ledger summaries (DailyOps, front, army-area, formation-directive, fiscal, defense, construction) every day instead of only on change, plus plan steering rows (importance target, campaign objective, unit transfer — each only when its signature changes) and construction probability-bias rows. Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
            PlanTrace = Config.Bind(
                "Diagnostics", "Plan Trace Logging", true,
                "Default ON. When the CIC builds a generic (non-historical) plan, emits the pick and the top-5 objective scores (id, score, theater, category). Rows go to the campaign tuning sidecar only (never LogOutput.log) and appear only when Logging Profile is CampaignTuning or FullTuning; bounded by Max Tuning Log MB.");
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
                "It is also a prerequisite of the legacy local-reaction producer that feeds Enable Tactical Charge Denial while Enable Tactical Orchestrator Army is off; with the orchestrator army on the observer changes no battle state.");
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
                "Default ON. Suppresses NullReferenceException from two vanilla tactical fallback/retreat methods during focused bug-smoke runs; all non-null exceptions still propagate.");
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
                "Default ON. Converts vanilla reserve support moves that were issued by direct RegimentSetPath into normal delayed SetWaypoint orders after removing the immediate path.");
            EnableWlTacticalChargeGuard = Config.Bind(
                "Tactical",
                "Enable W&L Tactical Charge Guard",
                true,
                "Default ON. #42: Whiskey runs its own copy of vanilla AIBattle.CheckForFeudGroupActions: feud moves target only the nearest " +
                "visible enemy within vanilla's feud distance, W&L player-subordinate groups never feud-move, and the orchestrator direct-child " +
                "gate is applied. Needs Tactical Commander Mode = Active. Off: vanilla feud movement runs unmodified and the direct-child gate is " +
                "skipped too. (The old charge and objective-chain guards were removed; the key name is kept for existing cfg files.)");
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
                "writes only, no movement. Doctrine and ledger-task stance writes run regardless of this flag.");
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
                "attack (reserve held, or falling back to the line), demotes it to defend (2): no cover-skipping assault approach, skirmishers may " +
                "deploy again, and its cavalry stops picking charge targets. Infantry melee from contact is not affected. With Enable Tactical " +
                "Orchestrator Army off, a legacy local-reaction DenyCharge also demotes an assault, to attack (3). Logs [TacticalChargeDeny] / " +
                "[TacticalChargePreserved]; writes need Tactical Commander Mode = Active; W&L/player groups are never touched.");
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
                "Default ON. B7: Postfix on AIBattle.CheckAIBombardment. For AI batteries in groups at ai_stance 3/4, applies the artillery " +
                "micro-doctrine (limber/unlimber, rotate, fallback) and rewrites combatbehaviorordered: bombard 9 to counter-battery 8 or cancel to " +
                "7; also releases counter-battery 8 to 7 once no enemy battery is in range. Needs Tactical Commander Mode = Active; W&L/player " +
                "units are protected.");
            EnableTacticalWithdrawalDoctrine = Config.Bind(
                "Tactical Doctrine",
                "Enable Tactical Withdrawal Doctrine",
                true,
                "Default ON. B8: in AIBattle.CheckUseOfReserves, attached AI infantry/cavalry that the withdrawal doctrine marks for fallback or " +
                "retreat get vanilla's own tactical fallback move (RegimentSetPath along the retreat angle plus SetMovementMode 2); never " +
                "Regiment.SetWithdrawal. Needs Tactical Commander Mode = Active; W&L/player units are protected. Off: no fallback writes; the " +
                "reserve doctrine-deny rollback still runs.");
            TacticalCommanderModeRaw = Config.Bind(
                "Tactical Orchestrator",
                "Tactical Commander Mode",
                // User-approved default for the full doctrine feature: Active. Existing config files still take precedence over this default.
                "Active",
                "Default Active. Values: Off, MonitorOnly, Active (case-insensitive). Off disables every Whiskey battle write (full tactical rollback); MonitorOnly runs vision/ledger/tasks/monitor without any vanilla writes; Active runs the full tactical command system for AI-controlled units. Unrecognized values log a warning and fall back to Active.");
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
                "Default ON. AIBattle.CheckUseOfReserves consults the command-node intent for the calling command group and rolls back new vanilla " +
                "reserve support paths when the group resolves to a Reserve role. Allowed vanilla reserve movement remains eligible for the " +
                "separate order-delay guard. Requires Enable Tactical Battle Orchestrator.");
            EnableSinglePassObjectiveRecords = Config.Bind(
                "Tactical Orchestrator",
                "Enable Single-Pass Objective Records",
                true,
                "Default ON. Builds objective records in one walk of BattleUnits.completeunitlist instead of six. Set false to fall back to the " +
                "per-walk builder.");
            BattleGroupStanceMinIntervalSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Battle Group Stance Min Interval Seconds",
                1.5f,
                "Wall-clock cooldown, per AI battle side, between BattleGroupStancePatch passes over the side's groups (at 20x/50x vanilla calls " +
                "AdjustGroupAIStance many times per real second). Default 1.5. 0 disables the cooldown.");
            EnableArtilleryWaypointVeto = Config.Bind(
                "Tactical Orchestrator",
                "Enable Artillery Waypoint Veto",
                true,
                "Default ON. Postfix on BattleUnits.SetWaypoint: when vanilla AI orders an AI battery to within 60 m of a visible enemy, cancels " +
                "the order, limbers and pulls it back along its retreat angle by vanilla's artillery fallback distance (200 m if that is unset). " +
                "Player orders are never touched. Set false for rollback.");
            EnableArtilleryAllStanceAudit = Config.Bind(
                "Tactical Orchestrator",
                "Enable Artillery All-Stance Audit",
                true,
                "Default ON. Each BattleGroupStancePatch pass (at most every Battle Group Stance Min Interval Seconds per side) walks the side's " +
                "AI batteries in every group stance (vanilla CheckAIBombardment only covers stances 3/4) and applies the artillery micro-doctrine " +
                "through vanilla calls: limber or retreat when the enemy is within 60 m, or within 100 m with no interposed infantry/cavalry and group " +
                "morale below 0.6 (idle batteries only); unlimber and face an enemy in fire range; turn toward a visible enemy beyond range (only when off by vanilla's " +
                "turn trigger); cancel bombardment below 10% ammo (mean of the three ammo slots). A battery is left alone for Artillery Audit Min " +
                "Hold Seconds after a write. AI batteries only (W&L/player units protected); writes need Tactical Commander Mode = Active.");
            ArtilleryAuditMinHoldSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Artillery Audit Min Hold Seconds",
                3.0f,
                "Per-battery hold after an All-Stance Audit write before the next one. Battle seconds on the battle clock, 1x-equivalent (600 per " +
                "battle hour, so 3 = 18 battle seconds), the same span at any time compression. Default 3.");
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
                "Default ON. The active operations-ledger posture executor may run a bounded division/brigade/regiment contact-facing pulse after " +
                "vanilla formation adjustment. Division scope only publishes/logs axis intent; brigade scope may correct command-group frontage; " +
                "regiment scope may rotate attached combat regiments in place to face visible contact. Disable for rollback without turning off " +
                "Tactical Commander Mode.");
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
            EnableTacticalDeploymentObserver = Config.Bind(
                "Tactical Diagnostics",
                "Enable Tactical Deployment Observer",
                true,
                "Default-on. Read-only deployment/redeployment observer for battle-start and end-of-day placement. Logs [TacDeployObs], [TacDeployObsMove], and [TacticalDeploymentPhase] rows; does not change unit positions or orders.");
            EnableTacticalDeploymentTerrainDiscipline = Config.Bind(
                "Tactical",
                "Enable Tactical Deployment Terrain Discipline",
                true,
                "Default ON. #60: after vanilla AI deployment, moves AI groups standing in water or outside the deployment zone to the best nearby " +
                "safe point through SetGroupFormation. Visible enemy bearing only adds a facing bonus to candidate points. Fortification-line " +
                "placements and the player's side are left alone.");
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
            EnableHistoricalOperationDoctrine = Config.Bind(
                "Strategic",
                "Enable Historical Operation Doctrine",
                true,
                "Default ON. AI CICs plan named historical operations when one fits. With no matching profile the CIC uses a generic plan only on " +
                "objectives vanilla itself would pick that have towns/IIPs; otherwise vanilla picks. A phase in a defensive posture never sets the " +
                "campaign objective. Off: historical operations are skipped and only generic plans are built.");
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
                "Capped by Max Active Telegraph Constructions Per Faction, skipped in the EmergencyHold fiscal posture, and a final placement that fails its connection/support/safe-rear checks is removed with the wish cost refunded (treasury, subsidy funding and spending stats, exactly as charged). A wish vanilla itself drops for lack of a site is not refunded, as in vanilla.");
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
                "menu controls frozen. The locked difficulty also feeds the CIC personality modifier. Set false to allow vanilla settings " +
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

            // Heuristic Community Hotfix detection — best-effort sentinel check.
            try
            {
                var hotfixType = AccessTools.TypeByName("CommunityHotfix");
                if (hotfixType != null)
                    Log.LogWarning("Community Hotfix detected — Whiskey Realism is INCOMPATIBLE. Strategic patches may not behave as expected.");
            }
            catch { /* ignore — best-effort only */ }

            _harmony = new Harmony(GUID);

            // Strategic-brain bootstrap before patches register so patches
            // never see a null Instance on their first invocation.
            StrategicCoordinator.Bootstrap();

            MoraleSnapshotLedger = new TacticalMoraleSnapshotLedger(capacity: TacticalMoraleSnapshotLedgerCapacity);

            StartTelemetryRuntime();

            // PatchAll(assembly) reflects all [HarmonyPatch] attributed classes
            // (including nested types like AICampaignSaveLoadPatch.SavePatch /
            // .LoadPatch). Cleaner than enumerating each class explicitly.
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{GUID} v0.2.2 loaded — strategic-brain patches registered.");
        }

        // Per-frame hitch probe for the telemetry sidecar. Tick never throws, but a
        // Unity message must not either, so the guard stays.
        private void Update()
        {
            try
            {
                if (Enabled == null || !Enabled.Value) return;
                WhiskeyRealism.Telemetry.TelemetryFrameProbe.Tick(UnityEngine.Time.unscaledDeltaTime);
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
