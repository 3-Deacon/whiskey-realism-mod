using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
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
                        StrategicCoordinator.Instance.SaveSidecar(fullPath);
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
                        var fullPath = Path.Combine(folder, SidecarFile);
                        if (File.Exists(fullPath))
                        {
                            StrategicCoordinator.Instance.LoadSidecar(fullPath);
                        }
                        else
                        {
                            Plugin.Log.LogInfo($"[Coordinator] no sidecar found at {fullPath} — initializing fresh state (this is normal for a brand-new career)");
                            StrategicCoordinator.Instance.InitializeFromGameState();
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("[LoadPatch] failed, falling back to fresh init: " + ex);
                        StrategicCoordinator.Instance.InitializeFromGameState();
                    }
                }
            }
        }
    }
}
