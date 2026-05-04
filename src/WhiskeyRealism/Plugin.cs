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
        internal ConfigEntry<bool> EnableConstructionIntentLedger;
        internal ConfigEntry<bool> EnableConstructionSiteSteering;
        internal ConfigEntry<bool> EnableSupplyDepotSteering;
        internal ConfigEntry<bool> EnableFortSteering;
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
            EnableConstructionIntentLedger = Config.Bind(
                "Construction", "Enable Construction Intent Ledger", true,
                "Compute weekly construction intent for telemetry and later steering. Does not directly change vanilla construction by itself.");
            EnableConstructionSiteSteering = Config.Bind(
                "Construction", "Enable Construction Site Steering", false,
                "Default OFF. Enables ConstructionIntentLedger private-building probability steering without replacing bestiipplaces or bypassing vanilla gates.");
            EnableSupplyDepotSteering = Config.Bind(
                "Construction", "Enable Supply Depot Steering", false,
                "Default OFF. Future valve for supply depot steering after observer telemetry proves safe candidate selection.");
            EnableFortSteering = Config.Bind(
                "Construction", "Enable Fort Steering", false,
                "Default OFF. Future valve for fort site steering after fort-site and unit-range telemetry prove realizable sites.");
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
