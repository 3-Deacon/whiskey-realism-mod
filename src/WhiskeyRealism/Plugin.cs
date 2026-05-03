using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism
{
    [BepInPlugin(GUID, "Whiskey Realism — Strategic AI Overhaul", "0.2.0")]
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

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Config.Bind(
                "[General]", "Enabled", true,
                "Master enable. Disable to short-circuit every patch in this mod.");
            VerboseLogging = Config.Bind(
                "[Diagnostics]", "Verbose Logging", false,
                "Emit per-patch first-fire markers and decision-trace logs to LogOutput.log.");
            PlanTrace = Config.Bind(
                "[Diagnostics]", "Plan Trace Logging", false,
                "On each monthly tick, dump CIC's plan reasoning (objective scores, top-3, picked, phases, deadline).");
            SuccessionTrace = Config.Bind(
                "[Diagnostics]", "Succession Trace Logging", false,
                "On each monthly tick, log every succession event check (date gate, war-state gate, fired/not-fired).");

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

            Log.LogInfo($"{GUID} v0.2.0 loaded — strategic-brain patches registered.");
        }
    }
}
