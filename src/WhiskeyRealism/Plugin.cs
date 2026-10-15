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
        internal ConfigEntry<bool> FiscalTelemetryCsv;
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
        internal ConfigEntry<bool> EnableWlPlayerSubordinateOrderBridge;
        internal ConfigEntry<bool> EnableWlPlayerOrderDoctrineDiagnostics;
        internal ConfigEntry<bool> EnableWlOperationNullGuard;
        internal ConfigEntry<bool> EnableTacticalMacroStanceScorer;
        internal ConfigEntry<bool> EnableTacticalGroupSectorStance;
        internal ConfigEntry<bool> EnableTacticalCommanderIntentDoctrine;
        internal ConfigEntry<bool> EnableTacticalLocalReactionDoctrine;
        internal ConfigEntry<bool> EnableTacticalChargeDenial;
        internal ConfigEntry<bool> EnableTacticalReserveIntentTelemetry;
        internal ConfigEntry<bool> EnableTacticalReserveListMutation;
        internal static ConfigEntry<bool> EnablePlayerOrderDoctrine { get; private set; }
        internal static ConfigEntry<bool> EnablePlayerOrderDoctrineDiagnostics { get; private set; }
        public static ConfigEntry<bool> EnableTacticalArtilleryDoctrine;
        public static ConfigEntry<bool> EnableTacticalWithdrawalDoctrine;
        public static ConfigEntry<bool> EnableTacticalBattleOrchestrator;
        public static ConfigEntry<bool> EnableTacticalOrchestratorArmy;
        public static ConfigEntry<int> TacticalOrchestratorMinReplanSeconds;
        public static ConfigEntry<bool> TacticalOrchestratorVerboseLogging;
        public static ConfigEntry<bool> EnableTacticalOrchestratorIntentInference;
        public static ConfigEntry<bool> EnableTacticalOrchestratorDirectChildGate;
        internal ConfigEntry<string> TacticalCommanderModeRaw;
        internal ConfigEntry<bool> EnableTacticalOrchestratorReserveCommitGate;
        internal ConfigEntry<bool> EnableTacticalOrchestratorChargeGate;
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
        // Worker-migration (2026-05-21): when ON, BuildObjectiveRecordsFromAggregate is
        // computed on the orchestrator worker thread instead of inline on main during
        // TacticalBattleSnapshotBuilder.Build. Default OFF until parity is verified in
        // smoke; flip to default-on after observation that worker records match the
        // legacy on-main records for a few cycles.
        public static ConfigEntry<bool> EnableWorkerObjectiveRecords;
        // Per-side cooldown for BattleGroupStancePatch.Apply. At high time compression
        // (20x/50x), vanilla calls AdjustGroupAIStance many times per wallclock-second;
        // the Postfix walks unitsused on each call even when no write happens. Cooldown
        // gates the walk so it runs at most once per N seconds wallclock per side.
        // Default 1.5s. 0 disables (preserves pre-2026-05-21 behavior).
        public static ConfigEntry<float> BattleGroupStanceMinIntervalSeconds;
        // Regimental intelligence (2026-05-21). When the orchestrator worker
        // detects a regiment is being flanked, pressed on multiple sides, or
        // isolated from its brigade, the doctrine produces a recommended
        // action (fall back, retreat, wheel). This flag controls whether main
        // thread actually WRITES the order. Default OFF — telemetry-only mode
        // emits TacticalRegimentalIntel decisions to tactical.jsonl without
        // touching vanilla state. Flip to true once smoke verifies the
        // decisions look sensible for known scenarios (flanked regiments,
        // isolated battalions).
        public static ConfigEntry<bool> EnableRegimentalIntelligenceWrites;
        // Friendly-path blocking gate (2026-05-21, ported from Scourge of War's
        // offcmds.cpp three-layer waypoint-issuance gate). When ON, every
        // SetWaypoint call (brigade-posture + regimental-intelligence paths)
        // first consults TacticalFriendlyPathBlockingGate. If the proposed
        // waypoint would cross a friendly brigade's path or overlap its
        // destination, the write is skipped. Default OFF until smoke verifies
        // false-positive rate is acceptable; turning ON should eliminate
        // remaining "H formation" crossings beyond what the opportunity-override
        // fix achieves.
        public static ConfigEntry<bool> EnableFriendlyPathBlockingGate;
        // Friendly-fire detection (2026-05-21, ports Scourge unitai.cpp:1484
        // StopOverlapTarget). Telemetry-only — emits TacticalFriendlyFireBlocked
        // events when a friendly sits in the firing line between a regiment
        // and its in-range enemy. Default OFF.
        public static ConfigEntry<bool> EnableFriendlyFireDetection;
        // Opportunity-hysteresis minimum hold time (2026-05-21). Once
        // TacticalReinforcementOpportunityDoctrine commits to a category
        // (AttackNow / DefensiveHold / WithdrawalToFightLater / etc.),
        // hold that decision for at least N wallclock seconds before
        // allowing transition. Prevents the ~500ms-1s thrash observed in
        // smoke when ratio inputs hovered near category thresholds.
        // Default 6.0s. Escalations to WithdrawalToFightLater bypass the
        // hold (situation got materially worse — react immediately).
        public static ConfigEntry<float> OpportunityHysteresisMinHoldSeconds;
        // Brigade task-commitment hold (2026-05-22, ports Scourge offai.cpp:343
        // `if (base.Orders()) return` + offai.cpp:816 CourTime throttle). Once
        // a brigade is given a task waypoint (probe / screen / guard-flank /
        // etc), suppress re-issuance of the SAME task for N seconds. Different
        // task bypasses (legitimate role change). Prevents the "set up line,
        // move it, set up again" churn observed when probe targets drift
        // across cycles. Default 12.0s wallclock.
        public static ConfigEntry<float> BrigadeTaskHoldSeconds;
        // Artillery panic-retreat doctrine (2026-05-21, ports Scourge of War's
        // artyai.cpp:180-225 panic logic). When ON, walks each brigade's
        // attached artillery sub-units. If an artillery unit is within panic
        // distance of an enemy AND has no friendly infantry/cavalry interposed
        // AND the enemy is not routed/retreating, issues a retreat waypoint
        // backward. Handles BOTH standalone artillery brigades AND artillery
        // attached to infantry brigades — vanilla's CheckAIBombardment only
        // runs for stance 3/4 so this fills the gap for all other stances.
        // Default OFF.
        // Comprehensive artillery audit (2026-05-21, ports Scourge artyai.cpp
        // full lifecycle). Vanilla GT's CheckAIBombardment only runs for
        // ai_stance 3/4 — every other stance leaves artillery on pure vanilla
        // AI which dueles at point-blank range without limber/retreat
        // decisions. When ON, BattleGroupStancePatch.ApplyArtilleryAudit
        // walks all artillery sub-units (standalone batteries + brigade-
        // attached batteries) for the patch's alliance, runs
        // TacticalArtilleryMicroDoctrine, and applies Limber / Unlimber /
        // Retreat / WheelToBestThreat / ConserveAmmo actions via the
        // existing TacticalArtilleryApplier write surface. Per-unit
        // hysteresis prevents thrash. Default OFF for safety.
        public static ConfigEntry<bool> EnableArtilleryAllStanceAudit;
        // Artillery point-blank waypoint veto (2026-05-22). Postfix on
        // BattleUnits.SetWaypoint detects artillery being sent toward enemy
        // within 60m and overwrites with a 200m retreat backward. Closes the
        // timing gap between vanilla's forward-move and our 1.5s BGS audit.
        // Default-on per the 2026-05-18 default-on policy for behavioral
        // writes.
        public static ConfigEntry<bool> EnableArtilleryWaypointVeto;
        public static ConfigEntry<float> ArtilleryAuditMinHoldSeconds;
        public static ConfigEntry<bool> EnableTacticalOrchestratorWorker;
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
        internal ConfigEntry<bool> EnableSupplyDepotSteering;
        internal ConfigEntry<bool> EnableFortSteering;
        internal ConfigEntry<bool> FortConstructionGovernorEnabled;
        internal ConfigEntry<bool> EnableTelegraphAI;
        internal ConfigEntry<bool> EnableRailroadSteering;
        internal ConfigEntry<bool> ConstructionTelemetryEnabled;
        internal ConfigEntry<bool> ConstructionVerboseLogging;
        internal ConfigEntry<int> MaxActiveTelegraphConstructionsPerFaction;
        internal ConfigEntry<int> MaxRailroadStartsPerFactionPerMonth;
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
        // 12 scripted succession events to fire on first strategic review. For
        // verifying the commander-swap apply mechanic without playing through
        // a multi-month campaign. Default off; remember to disable before
        // playing for real.
        internal ConfigEntry<bool> ForceAllSuccessionEvents;

        private Harmony _harmony;
        private bool _loggedTacticalCommanderMode;
        private TelemetryRuntime _telemetryRuntime;

        internal TacticalCommanderMode TacticalCommanderModeValue =>
            ResolveTacticalCommanderMode();

        internal bool TacticalOperationsLedgerEnabled =>
            TacticalCommanderModePolicy.RunsLedger(TacticalCommanderModeValue);

        internal bool TacticalOperationsLedgerAllowsWrites =>
            TacticalCommanderModePolicy.AllowsWrites(TacticalCommanderModeValue);

        public float HeavyReviewCycleHours => TacticalHeavyReviewCycleHours?.Value ?? 0.003f;
        public float HeavyReviewMinRealtimeSeconds => TacticalHeavyReviewMinRealtimeSeconds?.Value ?? 2.0f;

        private TacticalCommanderMode ResolveTacticalCommanderMode()
        {
            var mode = TacticalCommanderModePolicy.Parse(
                TacticalCommanderModeRaw?.Value,
                TacticalCommanderMode.Active);

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
                "Diagnostics", "Verbose Logging", false,
                "Emit per-patch first-fire markers and decision-trace logs to LogOutput.log.");
            PlanTrace = Config.Bind(
                "Diagnostics", "Plan Trace Logging", false,
                "On each strategic review tick, dump CIC's plan reasoning (objective scores, top-3, picked, phases, deadline).");
            SuccessionTrace = Config.Bind(
                "Diagnostics", "Succession Trace Logging", false,
                "On each strategic review tick, log every succession event check (date gate, war-state gate, fired/not-fired).");
            FiscalTrace = Config.Bind(
                "Diagnostics", "Fiscal Trace Logging", false,
                "Emit fiscal posture, gate, supply, and finance override reasoning.");
            FiscalTelemetryCsv = Config.Bind(
                "Diagnostics", "Fiscal Telemetry Csv", false,
                "Reserved for future CSV telemetry export. Current fiscal telemetry is emitted to LogOutput.log.");
            DirectorVerboseTrace = Config.Bind(
                "Telemetry",
                "Director Verbose Trace",
                false,
                "When true, logs detailed Director slice traces every advanced game day. Default off — only [CampaignPace] and [CollapseRisk] level-change lines emit.");
            TelemetryLoggingProfileRaw = Config.Bind(
                "Telemetry",
                "Logging Profile",
                "TacticalTuning",
                "Off, TacticalTuning, CampaignTuning, or FullTuning. Default TacticalTuning per 2026-05-18 user direction so battles produce diagnostic sidecars by default. Set to Off if disk usage is a concern (sidecars under <GTCW>/BepInEx/WhiskeyRealism/tuning-logs/<session>/ — bounded by Max Tuning Log MB). This setting controls structured tuning sidecars only; it does not enable or disable tactical/campaign behavior gates.");
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
                "Reserved for human-readable summary output at shutdown.");
            TelemetryPerformanceWarnings = Config.Bind(
                "Telemetry",
                "Telemetry Performance Warnings",
                true,
                "Emit bounded telemetry performance warning rows when the writer detects pressure.");
            TelemetryCreateIssueBundleOnShutdown = Config.Bind(
                "Telemetry",
                "Create Issue Bundle On Shutdown",
                false,
                "When true, future telemetry closeout may create a redacted issue bundle on shutdown.");
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
                false,
                "Default ON for Slice B B0. Emits bounded read-only battle telemetry when enabled; does not change tactical AI behavior.");
            TacticalObserverVerboseLogging = Config.Bind(
                "Tactical",
                "Tactical Observer Verbose Logging",
                false,
                "Emit lower-throttle tactical observer detail for focused smoke runs. Default observer mode remains signature-gated.");
            TacticalObserverMinSecondsBetweenSummaries = Config.Bind(
                "Tactical",
                "Tactical Observer Min Seconds Between Summaries",
                30,
                "Minimum wall-clock seconds between repeated tactical observer summaries with the same signature.");
            EnableTacticalDecisionMatrixLogging = Config.Bind(
                "Tactical",
                "Enable Tactical Decision Matrix Logging",
                true,
                "When Tactical Observer is enabled, emit high-volume [TacticalDecisionMatrix] battle/group rows for tactical AI bug hunts. Disable this switch to remove matrix noise without disabling the rest of the observer.");
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
                    "Maximum group rows emitted per tactical decision-matrix snapshot. Lower this if LogOutput.log gets too large.",
                    new AcceptableValueRange<int>(1, 300)));
            EnableTacticalBugTelemetry = Config.Bind(
                "Tactical",
                "Enable Tactical Bug Telemetry",
                false,
                "Default ON. Emits focused read-only telemetry for tactical order/current-order bug hunts; does not change battlefield behavior.");
            EnableTacticalFallbackRetreatNullGuard = Config.Bind(
                "Tactical",
                "Enable Tactical Fallback Retreat Null Guard",
                true,
                "Default ON. Suppresses NullReferenceException from two vanilla tactical fallback/retreat methods during focused bug-smoke runs; all non-null exceptions still propagate.");
            EnableTacticalPathfinderDiscipline = Config.Bind(
                "Tactical",
                "Enable Tactical Pathfinder Discipline",
                true,
                "Default ON. Corrects BUG-TAC-010 by accepting close complete tactical path endpoints, removing failed non-target path fragments, and rejecting non-complete NavMesh paths before they poison movement retries.");
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
                "Default ON for Slice B1/BUG-TAC-005. When enabled, blocks ungated W&L AI feud/charge/objective-chain movement for player-subordinate units while preserving charge cancellation and AI-vs-AI behavior.");
            EnableWlPlayerSubordinateOrderBridge = Config.Bind(
                "W&L",
                "Enable Player Order Doctrine",
                true,
                "Default ON. When true, the tactical operations ledger may issue player-subordinate W&L current orders through vanilla AIBattle.CheckCurrentOrderUpdate after vanilla UpdateDLCPlayerOrders runs.");
            EnableWlPlayerOrderDoctrineDiagnostics = Config.Bind(
                "W&L",
                "Enable Player Order Doctrine Diagnostics",
                true,
                "Logs bounded classify/dedupe/issue decisions for the player-order doctrine. Diagnostics do not issue orders when Enable Player Order Doctrine is off.");
            EnableWlOperationNullGuard = Config.Bind(
                "W&L",
                "Enable Operation Null Guard",
                true,
                "Default ON. Suppresses the vanilla Operation.UpdateOperation null-before-transform cleanup crash and finishes the missing operation instead.");
            EnablePlayerOrderDoctrine = Config.Bind(
                "W&L",
                "Enable Player Order Doctrine",
                false,
                "Default-off write valve for Whiskey-authored player-facing W&L orders. Per AGENTS.md named-exception scope, #62 PlayerSubordinateOrderPatch stays default-off until focused smoke proves no player-subordinate retasking. Diagnostics can run while this is disabled.");
            EnablePlayerOrderDoctrineDiagnostics = Config.Bind(
                "W&L",
                "Enable Player Order Doctrine Diagnostics",
                true,
                "Logs bounded classify/dedupe/issue decisions for the player-order doctrine.");
            EnableTacticalMacroStanceScorer = Config.Bind(
                "Tactical",
                "Enable Tactical Macro Stance Scorer",
                true,
                "Default ON for Slice B4. Uses B3 odds doctrine to bias battle-level macroai after vanilla dynamic macro logic runs.");
            EnableTacticalGroupSectorStance = Config.Bind(
                "Tactical",
                "Enable Tactical Group Sector Stance",
                true,
                "Default ON for Slice B5. Uses B3 sector doctrine to bias group ai_stance without issuing movement, reserve, artillery, fallback, or charge orders.");
            EnableTacticalCommanderIntentDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Commander Intent Doctrine",
                true,
                "Default ON for Slice B6a. Computes tactical commander intent and playbook from B3-B5 evidence and the active OperationPosture, and emits read-only [TacticalIntent] and [TacticalPlaybook] telemetry. Does not change any vanilla battle state.");
            EnableTacticalLocalReactionDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Local Reaction Doctrine",
                true,
                "Default ON for Slice B6c. Computes per-group local reactions from B6a intent + playbook + B3 evidence and emits read-only [TacticalLocalReaction] telemetry. Enables stance-4 preservation/demotion contract in BattleGroupStancePatch.");
            EnableTacticalChargeDenial = Config.Bind(
                "Tactical",
                "Enable Tactical Charge Denial",
                true,
                "Default ON for Slice B6c. When local reaction is not PermitCharge, BattleGroupStancePatch demotes vanilla stance 4 to 3 with [TacticalChargeDeny] telemetry, and BattleChargeGatePatch denies SetMovementMode(3) at the per-unit charge initiation surface as defense in depth.");
            EnableTacticalReserveIntentTelemetry = Config.Bind(
                "Tactical",
                "Enable Tactical Reserve Intent Telemetry",
                true,
                "Default ON for Slice B6c. Emits read-only [TacticalReserveIntent] lines aggregating LineReliefRequest signals + reserve availability per side. Does not mutate reserve lists.");
            EnableTacticalReserveListMutation = Config.Bind(
                "Tactical",
                "Enable Tactical Reserve List Mutation",
                true,
                "Default ON for Slice B6c. Allows BattleReserveDoctrinePatch to bias objectivechain[i].reservegroups membership under snapshot/restore protection when reserve intent allows mutation. W&L ownership and stale-order gates apply.");
            EnableTacticalArtilleryDoctrine = Config.Bind(
                "Tactical Doctrine",
                "Enable Tactical Artillery Doctrine",
                true,
                "Default-off. When true, B7 may rewrite vanilla artillery combatbehaviorordered to favor counter-battery, preserve-fire, or cancel-bombard decisions based on doctrine. Read the patch source before enabling.");
            EnableTacticalWithdrawalDoctrine = Config.Bind(
                "Tactical Doctrine",
                "Enable Tactical Withdrawal Doctrine",
                true,
                "Default-off. When true, B8 may call BattleUnits.SetWithdrawal for individual units classified as WithdrawalCandidate or CollapseCandidate by TacticalWithdrawalDoctrine. Read the patch source before enabling.");
            TacticalCommanderModeRaw = Config.Bind(
                "Tactical Orchestrator",
                "Tactical Commander Mode",
                // User-approved default for the full doctrine feature: Active. Existing config files still take precedence over this default.
                "Active",
                "Default Active. Off disables the operations-ledger command system; MonitorOnly runs vision/ledger/tasks/monitor without vanilla writes; Active runs the full tactical command system for AI sides.");
            _ = TacticalCommanderModeValue;
            EnableTacticalHeavyPathThrottling = Config.Bind(
                "TacticalTickOptimization",
                "Enable Tactical Heavy Path Throttling",
                true,
                "Enable the heavy path throttle for side-wide evidence, vision, and doctrine assignment (default ON per 2026-05-18 user direction; smoke verified).");
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
                "Master switch for the multi-echelon tactical battle orchestrator. " +
                "Default on per orchestrator umbrella spec. Disable to revert to vanilla " +
                "+ existing default-off Slice B scorer paths.");
            EnableTacticalOrchestratorArmy = Config.Bind(
                "Tactical Orchestrator",
                "Enable Tactical Orchestrator Army",
                true,
                "Default ON. O1: instantiate the per-side ArmyOrchestrator at battle start, " +
                "pick a personality-keyed playbook, and let BattleMacroStrategyPatch read " +
                "ArmyOrchestrator.CurrentMacroAi instead of running the doctrine scorer. " +
                "Disable to fall back to the scorer-driven macro behavior for regression triage.");
            TacticalOrchestratorMinReplanSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Min Replan Seconds",
                60,
                new ConfigDescription(
                    "Minimum wall-clock seconds between army replan events. Triggers may detect " +
                    "earlier; orchestrator rate-limits actual plan re-pick to avoid thrash.",
                    new AcceptableValueRange<int>(10, 600)));
            TacticalOrchestratorVerboseLogging = Config.Bind(
                "Tactical Orchestrator",
                "Verbose Logging",
                false,
                "Default ON. When true, emit per-tick [TacticalCascade] and per-trigger " +
                "[TacticalReplan] lines instead of just first-fire and on-change markers.");
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
                "Default ON. O3: when true, BattleFeudActionGatePatch (#42) consults " +
                "ArmyOrchestrator.GetDirectChildRole(group) between the W&L decision and " +
                "SetWaypoint, denying off-axis Main/SupportMain, wide Fix, out-of-sector " +
                "Screen/Refuse, toward-enemy Fallback, and any Reserve movement on AI- " +
                "controlled sides. Disable to keep #42's existing W&L-only behavior. " +
                "Telemetry runs regardless of this flag; only deny actions are gated.");
            EnableTacticalOrchestratorReserveCommitGate = Config.Bind(
                "Tactical Orchestrator",
                "Enable Tactical Orchestrator Reserve Commit Gate",
                true,
                "Default ON. Slice 1: when true, AIBattle.CheckUseOfReserves consults " +
                "the command-node intent for the calling command group and rolls back new " +
                "vanilla reserve support paths when the group resolves to a Reserve role. " +
                "Allowed vanilla reserve movement remains eligible for the separate order-delay guard.");
            EnableTacticalOrchestratorChargeGate = Config.Bind(
                "Tactical Orchestrator",
                "Enable Tactical Orchestrator Charge Gate",
                true,
                "Default ON. Slice 3: when true, AIBattle.MicroAICheckForCharges consults " +
                "the command-node intent for the calling command group before allowing vanilla " +
                "SetMovementMode(3) charge initiation. Main charges require favorable local odds; " +
                "SupportMain requires main-effort support evidence; Fix/Reserve/Fallback/Refuse/Screen " +
                "roles deny charge initiation unless the Screen routed-target exception is proven.");
            EnableSinglePassObjectiveRecords = Config.Bind(
                "Tactical Orchestrator",
                "Enable Single-Pass Objective Records",
                true,
                "Single-pass aggregation for ObjectiveRecord building. Walks " +
                "BattleUnits.completeunitlist once instead of six times. Default ON. " +
                "Set false to roll back to the legacy per-walk path if a regression " +
                "appears. Default-on per AGENTS.md tactical policy. Measured " +
                "performance characteristics are documented in docs/telemetry.md " +
                "once smoke-verified.");
            EnableTacticalOrchestratorWorker = Config.Bind(
                "Tactical Orchestrator",
                "Enable Tactical Orchestrator Worker",
                true,
                "Background-thread orchestrator analysis. Main thread captures a "
                + "snapshot and reads pre-computed decisions; analysis runs on a "
                + "dedicated worker thread. Default ON per AGENTS.md tactical policy. "
                + "Set false to roll back to synchronous on-main-thread analysis if "
                + "a regression appears. Performance characteristics documented in "
                + "docs/telemetry.md once smoke-verified.");
            EnableWorkerObjectiveRecords = Config.Bind(
                "Tactical Orchestrator",
                "Enable Worker Objective Records",
                false,
                "Computes BuildObjectiveRecordsFromAggregate on the orchestrator worker "
                + "thread instead of inline on main during snapshot build. Removes ~5ms "
                + "per side per heavy-gate fire from the main-thread frame budget at the "
                + "cost of one-cycle staleness on the observation→record path. Default "
                + "OFF (2026-05-21) until smoke verifies worker records match the legacy "
                + "on-main records. Main always falls back to the legacy on-main builder "
                + "when the worker has not yet published records for a side (first cycle "
                + "of a battle, suppressed side, or worker fault).");
            BattleGroupStanceMinIntervalSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Battle Group Stance Min Interval Seconds",
                1.5f,
                "Per-side wallclock cooldown for BattleGroupStancePatch.Apply. At 20x/50x "
                + "compression vanilla calls AdjustGroupAIStance N× more often per wallclock "
                + "— our Postfix walks unitsused on each call even when no write happens. "
                + "Cooldown gates the walk so it runs at most once per N seconds wallclock "
                + "per side. Default 1.5s. 0 disables. Smoke at 20× compression showed "
                + "the patch contributing ~22ms/sec of sustained main-thread cost before "
                + "the cooldown; cooldown drops it to ~3ms/sec at 1.5s while preserving "
                + "tactical AI responsiveness (writes are rare; vanilla preserves stance "
                + "between fires).");
            EnableArtilleryWaypointVeto = Config.Bind(
                "Tactical Orchestrator",
                "Enable Artillery Waypoint Veto",
                true,
                "When ON, Postfix on BattleUnits.SetWaypoint detects artillery "
                + "being sent toward a destination within 60m of any visible "
                + "enemy and overwrites with a 200m retreat backward. Closes "
                + "the timing gap between vanilla's autonomous forward-moves "
                + "and Whiskey's 1.5s artillery audit cycle. Default ON "
                + "(behavioral-write default-on policy). Set false for rollback.");
            EnableArtilleryAllStanceAudit = Config.Bind(
                "Tactical Orchestrator",
                "Enable Artillery All-Stance Audit",
                false,
                "When ON, BattleGroupStancePatch walks all artillery sub-units "
                + "(standalone batteries + brigade-attached batteries) for the "
                + "patch's alliance, runs TacticalArtilleryMicroDoctrine, and "
                + "applies Limber / Unlimber / Retreat / Wheel / ConserveAmmo "
                + "actions through TacticalArtilleryApplier. Ports Scourge of "
                + "War's full artyai.cpp lifecycle. Fills the gap left by B7 "
                + "(which only runs for ai_stance 3/4 via vanilla's "
                + "CheckAIBombardment hook). Default OFF.");
            ArtilleryAuditMinHoldSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Artillery Audit Min Hold Seconds",
                3.0f,
                "Per-artillery-unit hysteresis hold time. Once an action is "
                + "applied to an artillery unit (e.g. Retreat), hold that "
                + "decision for at least N wallclock seconds before issuing "
                + "another. Mirrors Scourge's tier-priority cascade pattern. "
                + "Default 3.0s. Lowering increases responsiveness at the "
                + "cost of more frequent re-writes during borderline conditions.");
            BrigadeTaskHoldSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Brigade Task Hold Seconds",
                12.0f,
                "Scourge-style brigade task-commitment hold. Once a brigade is "
                + "issued a task waypoint (probe/screen/guard-flank/etc), the "
                + "same task can't be re-issued for N seconds. Different task "
                + "bypasses the hold (legitimate role change). Prevents the "
                + "'set up line, move it, set up again' churn observed when "
                + "probe targets drift across cycles. Default 12.0s wallclock "
                + "(~5-10 game-minutes at 25-50× compression). Set to 0 to disable.");
            OpportunityHysteresisMinHoldSeconds = Config.Bind(
                "Tactical Orchestrator",
                "Opportunity Hysteresis Min Hold Seconds",
                6.0f,
                "Anti-thrash hold time for TacticalReinforcementOpportunityDoctrine. "
                + "Once the doctrine commits to a category (AttackNow / DefensiveHold "
                + "/ WithdrawalToFightLater / WaitAndConsolidate), hold that decision "
                + "for at least N wallclock seconds before allowing transition. Default "
                + "6.0s. Escalations to WithdrawalToFightLater bypass the hold to keep "
                + "reaction time on collapse scenarios. Set to 0 to disable.");
            EnableFriendlyFireDetection = Config.Bind(
                "Tactical Orchestrator",
                "Enable Friendly Fire Detection",
                false,
                "Telemetry-only detector for friendly-fire risk. When ON, for "
                + "each own regiment with an in-range enemy, checks whether a "
                + "friendly sits in the fire corridor between the regiment and "
                + "its target. Emits TacticalFriendlyFireBlocked events. No "
                + "behavioral writes — purely diagnostic. Default OFF.");
            EnableFriendlyPathBlockingGate = Config.Bind(
                "Tactical Orchestrator",
                "Enable Friendly Path Blocking Gate",
                false,
                "Ports Scourge of War's three-layer waypoint-issuance gate "
                + "(angle cones, destination overlap, brigade cohesion) into "
                + "every SetWaypoint call. When ON, refuses to write waypoints "
                + "that would cross a friendly brigade's frontal cone or overlap "
                + "a sibling regiment's destination. Default OFF until smoke "
                + "verifies false-positive rate; enable to eliminate remaining "
                + "H-formation crossings.");
            EnableRegimentalIntelligenceWrites = Config.Bind(
                "Tactical Orchestrator",
                "Enable Regimental Intelligence Writes",
                false,
                "Master switch for the regimental-intelligence layer's vanilla writes. "
                + "When ON, the worker's per-regiment decisions (fall back when flanked, "
                + "retreat when pressed on multiple sides, wheel to face flank) issue "
                + "actual SetWaypoint / SetGroupFormation orders. When OFF (default), "
                + "the doctrine still runs and emits TacticalRegimentalIntel telemetry "
                + "events for diagnostic verification, but no vanilla writes happen. "
                + "Smoke decisions-only first; flip to true once tactical.jsonl confirms "
                + "the right regiments are being flagged for the right reasons.");
            EnableTacticalRegimentDiagnostics = Config.Bind(
                "Tactical Diagnostics",
                "Enable Tactical Regiment Diagnostics",
                true,
                "Default-on. Per-regiment delta logger for diagnosing stuck-retreat / stuck-charge " +
                "/ unit-state bugs that the matrix observer (#35) cannot see. Pure observation; " +
                "no behavior writes. Bounded by Tactical Regiment Diagnostic Names watch list, " +
                "per-unit min 0.5s sample interval, and a hard cap of 2000 lines per battle.");
            TacticalRegimentDiagnosticNames = Config.Bind(
                "Tactical Diagnostics",
                "Tactical Regiment Diagnostic Names",
                "Hampton,Tennessee",
                "Comma-separated case-insensitive substrings matched against Regiment names. " +
                "Default seeds Hampton's Legion + any Tennessee-named regiment (the reported " +
                "stuck-retreat units). Add more as needed: 'Hampton,Tennessee,2nd_South_Carolina'. " +
                "Empty string disables logging (feature stays enabled but watches nothing).");
            EnableTacticalMapKnowledgeDiagnostics = Config.Bind(
                "Tactical Diagnostics",
                "Enable Tactical Map Knowledge Diagnostics",
                false,
                "Default ON. Read-only tactical scene-object probe for map-knowledge debugging. " +
                "When enabled, emits bounded [TacticalMapKnowledge] rows for Objectives, EntryPoints, " +
                "AIFortifications, FortificationGroup, road/railroad anchors, and watched-regiment " +
                "cover/facing context. Does not change unit orders, facing, formation, or pathing.");
            EnableTacticalContactFacingPulse = Config.Bind(
                "Tactical",
                "Enable Tactical Contact Facing Pulse",
                true,
                "Default ON. The active operations-ledger posture executor may run a " +
                "bounded division/brigade/regiment contact-facing pulse after vanilla formation adjustment. " +
                "Division scope only publishes/logs axis intent; brigade scope may correct command-group " +
                "frontage; regiment scope may reform and face attached combat regiments under visible " +
                "contact. Disable this flag for rollback without turning off Tactical Commander Mode.");
            EnableTacticalReinforcementOpportunityDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Reinforcement Opportunity Doctrine",
                true,
                "Default ON. Refreshes a per-army reinforcement-opportunity decision (AttackNow / WaitAndConsolidate / DefensiveHold / WithdrawalToFightLater) " +
                "from BattleUnits.scheduledarrival + sideinformation.totalactiveforce. Considers current force ratio, per-arrival timing, and commander " +
                "aggression. Telemetry: TacticalReinforcementOpportunity events with currentRatio, enemyParityHours, ownParityHours, threshold, window. " +
                "Disable to fall back to the previous purely-current-strength playbook selection.");
            EnableVanillaMacroAiSync = Config.Bind(
                "Tactical",
                "Enable Vanilla Macro AI Sync",
                true,
                "Default ON. Writes Whiskey's doctrine output (AttackNow / DefensiveHold / WaitAndConsolidate / WithdrawalToFightLater) to " +
                "BattleUnits.sideinformation[side].macroai via a Prefix on AIBattle.CheckGlobalAIStrategy. This lets vanilla's AdjustGroupAIStance " +
                "drive per-group ai_stance for brigades we don't have explicit roles for — the line of battle then forms via vanilla's macroai==2 " +
                "cascade. Skips for player-controlled sides (honors GameVars.playeralliance + ai_vs_ai). Disable for rollback.");
            EnableGeometricPlayTemplates = Config.Bind(
                "Tactical",
                "Enable Geometric Play Templates",
                true,
                "Default ON. Stage 2 of the Scourge-of-War-pattern integration. When the army selects a playbook (DoubleEnvelopment / PreparedDefense / etc), " +
                "look up its geometric template — a set of army-relative rectangles, each tagged with a tactical purpose (LeftWing / Center / Reserve / etc.) — " +
                "project to world coordinates (army centroid + enemy direction), and assign corps to rects by closest-distance. The role allocator then " +
                "uses the rect kind as the role basis instead of per-brigade sector evidence. Falls back to evidence-based allocation when no template is " +
                "available for the current plan or positions cannot be computed.");
            EnableTacticalDeploymentObserver = Config.Bind(
                "Tactical Diagnostics",
                "Enable Tactical Deployment Observer",
                true,
                "Default-on. Read-only deployment/redeployment observer for battle-start and end-of-day placement. Logs [TacDeployObs], [TacDeployObsMove], and [TacticalDeploymentPhase] rows; does not change unit positions or orders.");
            EnableTacticalDeploymentTerrainDiscipline = Config.Bind(
                "Tactical",
                "Enable Tactical Deployment Terrain Discipline",
                true,
                "Default off. When enabled, may correct clear AI deployment terrain or deployment-zone failures after vanilla deployment using bounded terrain-safe candidates. Visible enemy bearing may align final facing only as part of those terrain corrections.");
            TacticalDeploymentTerrainMaxCorrectionMeters = Config.Bind(
                "Tactical",
                "Tactical Deployment Terrain Discipline Max Correction Meters",
                60f,
                "Maximum candidate correction distance sampled for default-off AI deployment terrain or deployment-zone corrections.");
            TacticalDeploymentTerrainMaxCandidates = Config.Bind(
                "Tactical",
                "Tactical Deployment Terrain Discipline Max Candidates",
                16,
                "Maximum candidate points sampled around a failed AI deployment placement.");
            TacticalDeploymentFacingPreferredDeltaDegrees = Config.Bind(
                "Tactical",
                "Tactical Deployment Facing Preferred Delta Degrees",
                90f,
                "Preferred maximum final facing delta from visible enemy bearing for deployment terrain corrections.");
            EnableConstructionIntentLedger = Config.Bind(
                "Construction", "Enable Construction Intent Ledger", true,
                "Compute weekly construction intent for telemetry and later steering. Does not directly change vanilla construction by itself.");
            EnableHistoricalOperationDoctrine = Config.Bind(
                "Strategic",
                "Enable Historical Operation Doctrine",
                true,
                "Enable named historical operation doctrine for AI CIC planning. When enabled, catalog misses are logged as NoProfile and do not create generic replacement plans.");
            EnableDefenseIntentLedger = Config.Bind(
                "Defense Intent Ledger",
                "Enable Defense Intent Ledger",
                true,
                "Compute the daily defense ledger (Slice 1 observer). Disable to suppress all [DefenseIntent] output.");
            DefenseIntentVerboseLogging = Config.Bind(
                "Defense Intent Ledger",
                "Defense Intent Verbose Logging",
                false,
                "Log per-tick defense intent telemetry even when the signature has not changed.");
            EnableConstructionSiteSteering = Config.Bind(
                "Construction", "Enable Construction Site Steering", false,
                "Default ON. Enables ConstructionIntentLedger private-building probability steering without replacing bestiipplaces or bypassing vanilla gates.");
            EnableSupplyDepotSteering = Config.Bind(
                "Construction", "Enable Supply Depot Steering", false,
                "Default ON. Future valve for supply depot steering after observer telemetry proves safe candidate selection.");
            EnableFortSteering = Config.Bind(
                "Construction", "Enable Fort Steering", false,
                "Default ON. Future valve for fort site steering after fort-site and unit-range telemetry prove realizable sites.");
            FortConstructionGovernorEnabled = Config.Bind(
                "Construction", "Fort Construction Governor Enabled", true,
                "Default ON. Filters saturated vanilla fort construction sites before AICampaign.CheckFortConstruction so either side cannot stack excessive forts in one local area unless threat justifies more.");
            EnableTelegraphAI = Config.Bind(
                "Construction", "Enable Telegraph AI", false,
                "Default ON. Enables conservative connected-chain telegraph construction with support-unit and final-placement validation.");
            EnableRailroadSteering = Config.Bind(
                "Construction", "Enable Railroad Steering", false,
                "Default ON. Future valve for per-line railroad steering. Observation remains active through telemetry.");
            ConstructionTelemetryEnabled = Config.Bind(
                "Construction", "Construction Telemetry", true,
                "Emit no-spam construction intent and actual-start heartbeat lines.");
            ConstructionVerboseLogging = Config.Bind(
                "Construction", "Construction Verbose Logging", false,
                "Emit verbose construction candidate and actual-start details.");
            MaxActiveTelegraphConstructionsPerFaction = Config.Bind(
                "Construction", "Max Active Telegraph Constructions Per Faction", 1,
                "Caps simultaneously active Whiskey telegraph constructions per faction when Telegraph AI is enabled.");
            MaxRailroadStartsPerFactionPerMonth = Config.Bind(
                "Construction", "Max Railroad Starts Per Faction Per Month", 1,
                "Future railroad steering cap. Current slice observes vanilla railroad starts only.");
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
                "Performance", "Campaign AI Governor Max Passes At 20x", 2,
                "Maximum vanilla AICampaign.UpdateUnitAI passes per frame at 20x when Campaign AI Governor is enabled. Vanilla normally runs 4.");
            CampaignAiGovernorMaxPasses50x = Config.Bind(
                "Performance", "Campaign AI Governor Max Passes At 50x", 3,
                "Maximum vanilla AICampaign.UpdateUnitAI passes per frame at 50x when Campaign AI Governor is enabled. Vanilla normally runs 7.");
            CampaignAiGovernorFrameBudgetMs = Config.Bind(
                "Performance", "Campaign AI Governor Frame Budget Ms", 3f,
                "Maximum wall-clock milliseconds per AICampaign.Update wrapper frame before the governor stops issuing more UpdateUnitAI passes.");
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
                "W&L Camp", "Enable W&L Camp Verbose Trace", false,
                "Emit bounded W&L camp accounting and modifier trace lines for focused smoke tests.");
            OverrideVanillaSettings = Config.Bind(
                "Strategic", "Override Vanilla Settings", true,
                "When true, Whiskey Realism locks Aggressiveness to Mediocre, Historic AI Personality to true, and Difficulty to the value of LockedDifficulty (default Hard) at campaign creation. " +
                "Set false to allow vanilla settings to apply (advanced — may produce incoherent AI behavior or weak historical immersion).");
            LockedDifficulty = Config.Bind(
                "Strategic", "Locked Difficulty", 3,
                "Difficulty index 0-4 to lock when OverrideVanillaSettings is true. 0=Very Easy, 1=Easy, 2=Mediocre, 3=Hard (default — historical brutality), 4=Very Hard.");
            ForceAllSuccessionEvents = Config.Bind(
                "Diagnostics", "Force All Succession Events", false,
                "TEST MODE — bypass date and war-state gates and force all 12 scripted succession events to fire on first strategic review tick. Lets you verify the concrete commander-swap mechanic in seconds without playing through a multi-month campaign. DISABLE before a real playthrough.");

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
