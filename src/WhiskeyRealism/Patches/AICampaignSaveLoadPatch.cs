using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Sidecar persistence next to vanilla's campaign save.
    //  - AICampaign.Save(string folder) (decompile 16631) is called from SceneManagement.SaveAll
    //    (36708, AICampaign.Save at 36770) whenever campaign data is saved, including the save
    //    right before a campaign battle (PrepareLaunchingBattleFromCampaign -> SaveAll()).
    //  - AICampaign.Load(string folder) (16435) is called only for currentoperation 5 (return
    //    from battle) / 6 (load save) (CampaignController 30003-30023); it returns early for
    //    pre-0.9102 saves, our Postfix still runs. A brand-new campaign never reaches it; that
    //    case is handled by CampaignEpochTracker in MonthlyTickHookPatch.
    internal static class AICampaignSaveLoadPatch
    {
        private const string SidecarFile = "whiskeyrealism.json";

        // NOTE: vanilla writes campaign saves to the GAME INSTALL DIR (current
        // working directory at runtime), NOT Application.persistentDataPath.
        // Vanilla's SceneManagement.SaveAll calls Directory.CreateDirectory(text)
        // where `text` is a relative path like "Campaigns/002/A/Save/" — CWD-resolved.
        // Our patch must use the same convention so the sidecar lands beside
        // vanilla's files.
        [HarmonyPatch(typeof(AICampaign), "Save")]
        internal static class SavePatch
        {
            [HarmonyPostfix]
            internal static void Postfix(string folder)
            {
                using (TelemetryPerf.Scope("campaign.patch.save-load.save", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    OnceLog.Info("save", "AICampaign.Save Postfix wired");
                    try
                    {
                        if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                        // Relative — let .NET resolve against CWD (the game install dir).
                        var fullPath = Path.Combine(folder, SidecarFile);
                        StrategicCoordinator.Instance?.SaveSidecar(fullPath);
                    }
                    catch (Exception ex) { Plugin.Log.LogError("[SavePatch] " + ex); }
                }
            }
        }

        [HarmonyPatch(typeof(AICampaign), "Load")]
        internal static class LoadPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(string folder)
            {
                using (TelemetryPerf.Scope("campaign.patch.save-load.load", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    OnceLog.Info("load", "AICampaign.Load Postfix wired");
                    try
                    {
                        if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                        var coordinator = StrategicCoordinator.Instance;
                        if (coordinator == null) return;

                        // R15: wipe the previous campaign's state first, then apply this save's
                        // sidecar (or start fresh when it has none). Mark this campaign scene as
                        // loaded so the new-scene detector doesn't reset it again.
                        StrategicCoordinator.Epoch.NoteLoad(UnityEngine.Object.FindObjectOfType<AICampaign>());
                        coordinator.ResetAll("load");
                        // Falls back to .bak / .tmp, or fresh state when the save has no sidecar.
                        coordinator.LoadSidecar(Path.Combine(folder, SidecarFile));
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("[LoadPatch] failed, falling back to fresh init: " + ex);
                        try
                        {
                            // H-saveload: Bootstrap itself may be what threw.
                            var coordinator = StrategicCoordinator.Instance;
                            if (coordinator != null)
                            {
                                coordinator.ResetAll("load-failed");
                                coordinator.InitializeFromGameState();
                            }
                        }
                        catch (Exception inner)
                        {
                            Plugin.Log.LogWarning("[LoadPatch] fresh init failed: " + inner.Message);
                        }
                    }
                }
            }
        }
    }
}
