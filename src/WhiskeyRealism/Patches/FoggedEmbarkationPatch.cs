using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-002a. Vanilla Regiment.CheckModelReplacement (decompile 116372-116503) runs
    // for every campaign unit each regular cycle (112225, right after
    // UpdateCampsAndEntrenchments). It creates the river boat (116383), sea ship (116399,
    // with SetEmbarkmentMode 116418) or train model (116446) and sets currentmodeltype only
    // when !iswithinfog, and removes the model when fogged (116461, currentmodeltype = -1).
    // The model is the embarkation state: rail speed (133401) and terrain cost (132891),
    // the night halt of marching troops (133958), transport-capacity use and the speed
    // limit from it (116471-116501), locomotive/ship consumption (133900-133915), the
    // transport ration factor (114186) and no reinforcements while embarked (114501) all
    // read shownmodel/currentmodeltype. A hidden AI unit was therefore never embarked.
    // Vanilla gates the model on fog because it would reveal the unit.
    //
    // For a fogged AI land unit on the campaign map the Prefix clears iswithinfog for the
    // call and the Finalizer restores it (even if vanilla throws), then deactivates the
    // transport model while the player cannot see the unit and reactivates it once the
    // unit is visible. The model's Ship.Update only follows the unit's position (141265,
    // 141358); Ship.Initialize registers a firing sound array but plays nothing (141228).
    // A fleet (unittyp 17) and player-alliance units are never touched (FoggedModelRule).
    // Rollback: Campaign Bug Fixes / Enable Fogged AI Embarkation Fix (models are still
    // re-shown for visible units when the flag is off).
    [HarmonyPatch(typeof(Regiment), "CheckModelReplacement")]
    internal static class FoggedEmbarkationPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance, ref bool __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.fogged-embarkation.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.fogged-embarkation.prefix");
                __state = false;
                try
                {
                    if (__instance == null || !__instance.iswithinfog) return;
                    if (!FoggedModelRule.ShouldTreatAsVisibleForEmbarkation(
                            Enabled(),
                            __instance.calledfromcampaign,
                            GameVars.usefow,
                            __instance.alliance == GameVars.playeralliance,
                            __instance.iswithinfog,
                            __instance.unittyp == 17))
                    {
                        return;
                    }

                    __state = true;
                    __instance.iswithinfog = false;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("fogged-embarkation:prefix", "[Patch:FoggedEmbarkation] prefix failed (vanilla runs unchanged): " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(Regiment __instance, bool __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.fogged-embarkation.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.fogged-embarkation.finalizer");
                try
                {
                    if (__instance == null) return;
                    // The removal branch may re-run CheckRegimentVisibility (116467), which
                    // recomputes iswithinfog; the snapshot value is restored either way.
                    if (__state)
                    {
                        __instance.iswithinfog = true;
                        if ((UnityEngine.Object)__instance.shownmodel != null)
                            OnceLog.Info("fogged-embarkation:kept", "[Patch:FoggedEmbarkation] a fogged AI unit is embarked (transport model hidden from the player)");
                    }

                    bool isPlayerAlliance = __instance.alliance == GameVars.playeralliance;
                    if (isPlayerAlliance || __instance.unittyp == 17) return;
                    var model = __instance.shownmodel;
                    if ((UnityEngine.Object)model == null) return;
                    bool active = !FoggedModelRule.ShouldHideModels(isPlayerAlliance, __instance.iswithinfog);
                    var go = ((Component)model).gameObject;
                    if (go.activeSelf == active) return;
                    go.SetActive(active);
                    if (!active)
                        OnceLog.Info("fogged-embarkation:hidden", "[Patch:FoggedEmbarkation] hid the transport model of an AI unit the player cannot see");
                    else
                        OnceLog.Info("fogged-embarkation:shown", "[Patch:FoggedEmbarkation] re-showed the transport model of an AI unit the player now sees");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("fogged-embarkation:finalizer", "[Patch:FoggedEmbarkation] finalizer failed: " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableFoggedAiEmbarkationFix != null
                    && Plugin.EnableFoggedAiEmbarkationFix.Value;
            }
            catch { return false; }
        }
    }
}
