using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using WhiskeyRealism.Strategic;

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
        internal ConfigEntry<bool> EnableTacticalObserver;
        internal ConfigEntry<bool> TacticalObserverVerboseLogging;
        internal ConfigEntry<int> TacticalObserverMinSecondsBetweenSummaries;
        internal ConfigEntry<bool> EnableTacticalDecisionMatrixLogging;
        internal ConfigEntry<int> TacticalDecisionMatrixMinSecondsBetweenSnapshots;
        internal ConfigEntry<int> TacticalDecisionMatrixMaxRows;
        internal ConfigEntry<bool> EnableTacticalBugTelemetry;
        internal ConfigEntry<bool> EnableTacticalFallbackRetreatNullGuard;
        internal ConfigEntry<bool> EnableWlTacticalChargeGuard;
        internal ConfigEntry<bool> EnableTacticalMacroStanceScorer;
        internal ConfigEntry<bool> EnableTacticalGroupSectorStance;
        internal ConfigEntry<bool> EnableTacticalCommanderIntentDoctrine;
        internal ConfigEntry<bool> EnableTacticalLocalReactionDoctrine;
        internal ConfigEntry<bool> EnableTacticalChargeDenial;
        internal ConfigEntry<bool> EnableTacticalReserveIntentTelemetry;
        internal ConfigEntry<bool> EnableTacticalReserveListMutation;
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
            EnableTacticalObserver = Config.Bind(
                "Tactical",
                "Enable Tactical Observer",
                false,
                "Default OFF for Slice B B0. Emits bounded read-only battle telemetry when enabled; does not change tactical AI behavior.");
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
                "Default OFF. Emits focused read-only telemetry for tactical order/current-order bug hunts; does not change battlefield behavior.");
            EnableTacticalFallbackRetreatNullGuard = Config.Bind(
                "Tactical",
                "Enable Tactical Fallback Retreat Null Guard",
                false,
                "Default OFF. Suppresses NullReferenceException from two vanilla tactical fallback/retreat methods during focused bug-smoke runs; all non-null exceptions still propagate.");
            EnableWlTacticalChargeGuard = Config.Bind(
                "Tactical",
                "Enable W&L Tactical Charge Guard",
                false,
                "Default OFF for Slice B1/BUG-TAC-005. When enabled, blocks ungated W&L AI feud/charge/objective-chain movement for player-subordinate units while preserving charge cancellation and AI-vs-AI behavior.");
            EnableTacticalMacroStanceScorer = Config.Bind(
                "Tactical",
                "Enable Tactical Macro Stance Scorer",
                false,
                "Default OFF for Slice B4. Uses B3 odds doctrine to bias battle-level macroai after vanilla dynamic macro logic runs.");
            EnableTacticalGroupSectorStance = Config.Bind(
                "Tactical",
                "Enable Tactical Group Sector Stance",
                false,
                "Default OFF for Slice B5. Uses B3 sector doctrine to bias group ai_stance without issuing movement, reserve, artillery, fallback, or charge orders.");
            EnableTacticalCommanderIntentDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Commander Intent Doctrine",
                false,
                "Default OFF for Slice B6a. Computes tactical commander intent and playbook from B3-B5 evidence and the active OperationPosture, and emits read-only [TacticalIntent] and [TacticalPlaybook] telemetry. Does not change any vanilla battle state.");
            EnableTacticalLocalReactionDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Local Reaction Doctrine",
                false,
                "Default OFF for Slice B6c. Computes per-group local reactions from B6a intent + playbook + B3 evidence and emits read-only [TacticalLocalReaction] telemetry. Enables stance-4 preservation/demotion contract in BattleGroupStancePatch.");
            EnableTacticalChargeDenial = Config.Bind(
                "Tactical",
                "Enable Tactical Charge Denial",
                false,
                "Default OFF for Slice B6c. When local reaction is not PermitCharge, BattleGroupStancePatch demotes vanilla stance 4 to 3 with [TacticalChargeDeny] telemetry, and BattleChargeGatePatch denies SetMovementMode(3) at the per-unit charge initiation surface as defense in depth.");
            EnableTacticalReserveIntentTelemetry = Config.Bind(
                "Tactical",
                "Enable Tactical Reserve Intent Telemetry",
                false,
                "Default OFF for Slice B6c. Emits read-only [TacticalReserveIntent] lines aggregating LineReliefRequest signals + reserve availability per side. Does not mutate reserve lists.");
            EnableTacticalReserveListMutation = Config.Bind(
                "Tactical",
                "Enable Tactical Reserve List Mutation",
                false,
                "Default OFF for Slice B6c. Allows BattleReserveDoctrinePatch to bias objectivechain[i].reservegroups membership under snapshot/restore protection when reserve intent allows mutation. W&L ownership and stale-order gates apply.");
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
                "Default OFF. Enables ConstructionIntentLedger private-building probability steering without replacing bestiipplaces or bypassing vanilla gates.");
            EnableSupplyDepotSteering = Config.Bind(
                "Construction", "Enable Supply Depot Steering", false,
                "Default OFF. Future valve for supply depot steering after observer telemetry proves safe candidate selection.");
            EnableFortSteering = Config.Bind(
                "Construction", "Enable Fort Steering", false,
                "Default OFF. Future valve for fort site steering after fort-site and unit-range telemetry prove realizable sites.");
            FortConstructionGovernorEnabled = Config.Bind(
                "Construction", "Fort Construction Governor Enabled", true,
                "Default ON. Filters saturated vanilla fort construction sites before AICampaign.CheckFortConstruction so either side cannot stack excessive forts in one local area unless threat justifies more.");
            EnableTelegraphAI = Config.Bind(
                "Construction", "Enable Telegraph AI", false,
                "Default OFF. Enables conservative connected-chain telegraph construction with support-unit and final-placement validation.");
            EnableRailroadSteering = Config.Bind(
                "Construction", "Enable Railroad Steering", false,
                "Default OFF. Future valve for per-line railroad steering. Observation remains active through telemetry.");
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

            // PatchAll(assembly) reflects all [HarmonyPatch] attributed classes
            // (including nested types like AICampaignSaveLoadPatch.SavePatch /
            // .LoadPatch). Cleaner than enumerating each class explicitly.
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{GUID} v0.2.2 loaded — strategic-brain patches registered.");
        }
    }
}
