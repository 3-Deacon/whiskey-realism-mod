using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    internal static class AICampaignSaveLoadPatch
    {
        private const string SidecarFile = "whiskeyrealism.json";

        [HarmonyPatch(typeof(AICampaign), "Save")]
        internal static class SavePatch
        {
            [HarmonyPostfix]
            internal static void Postfix(string folder)
            {
                OnceLog.Info("save", "AICampaign.Save Postfix wired");
                try
                {
                    if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                    var fullPath = Path.Combine(Application.persistentDataPath, folder, SidecarFile);
                    StrategicCoordinator.Instance.SaveSidecar(fullPath);
                }
                catch (Exception ex) { Plugin.Log.LogError("[SavePatch] " + ex); }
            }
        }

        [HarmonyPatch(typeof(AICampaign), "Load")]
        internal static class LoadPatch
        {
            [HarmonyPostfix]
            internal static void Postfix(string folder)
            {
                OnceLog.Info("load", "AICampaign.Load Postfix wired");
                try
                {
                    if (StrategicCoordinator.Instance == null) StrategicCoordinator.Bootstrap();
                    var fullPath = Path.Combine(Application.persistentDataPath, folder, SidecarFile);
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
