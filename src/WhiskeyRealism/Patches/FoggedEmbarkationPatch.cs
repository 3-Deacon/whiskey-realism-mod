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
    // call (UnitFogFlip); the Finalizer sets it again, even if vanilla throws, unless the
    // removal branch re-ran CheckRegimentVisibility (116463-116467) and so recomputed it.
    // It then deactivates the transport model while the player cannot see the unit and
    // reactivates it once the unit is visible. The model's Ship.Update only follows the
    // unit's position (141265, 141358); Ship.Initialize plays nothing (141228-141262).
    // A fleet (unittyp 17) and player-alliance units are never touched (FoggedModelRule).
    //
    // SharedFiringSounds: Ship.Initialize adds 20 ship-firing SoundObjects per model to
    // SoundEngine lists that never shrink (141255 -> AddPurposeArray 146747-146756). A land
    // unit's transport never fires (AnimateFire's callers 19058/19283/19672 use fleet
    // vessels' models, 18230), so while this patch creates a fogged unit's model the Prefix
    // returns one remembered array index per SoundEngine instead of adding another 20
    // (a Prefix that skips vanilla, limited to that purpose inside this patch's call).
    // Rollback: Campaign Bug Fixes / Enable Fogged AI Embarkation Fix (models are still
    // re-shown for visible units when the flag is off).
    internal static class FoggedEmbarkationPatch
    {
        private static readonly UnitFogFlip<Regiment, object> Flip = new UnitFogFlip<Regiment, object>(
            unit => unit.iswithinfog,
            (unit, fog) => unit.iswithinfog = fog);

        private static readonly SharedArrayIndex FiringSounds = new SharedArrayIndex();

        [HarmonyPatch(typeof(Regiment), "CheckModelReplacement")]
        internal static class ModelReplacement
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
                        __state = (UnityEngine.Object)__instance.shownmodel != null;
                        Flip.Apply(__instance);
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
                        bool flipped = Flip.Active;
                        bool hasModel = __instance != null && (UnityEngine.Object)__instance.shownmodel != null;
                        string failure = Flip.Restore(FoggedModelRule.VanillaRecomputedFog(__state, hasModel));
                        if (failure != null)
                            OnceLog.Warning("fogged-embarkation:restore", "[Patch:FoggedEmbarkation] restoring the fog flag failed: " + failure);
                        if (__instance == null) return;
                        if (flipped && hasModel)
                            OnceLog.Info("fogged-embarkation:kept", "[Patch:FoggedEmbarkation] a fogged AI unit is embarked (transport model hidden from the player)");

                        bool isPlayerAlliance = __instance.alliance == GameVars.playeralliance;
                        if (isPlayerAlliance || __instance.unittyp == 17) return;
                        int change = ModelActiveSync.Apply(
                            __instance.shownmodel,
                            m => (UnityEngine.Object)m == null,
                            m => ((Component)m).gameObject.activeSelf,
                            (m, on) => ((Component)m).gameObject.SetActive(on),
                            !FoggedModelRule.ShouldHideModels(isPlayerAlliance, __instance.iswithinfog));
                        if (change < 0)
                            OnceLog.Info("fogged-embarkation:hidden", "[Patch:FoggedEmbarkation] hid the transport model of an AI unit the player cannot see");
                        else if (change > 0)
                            OnceLog.Info("fogged-embarkation:shown", "[Patch:FoggedEmbarkation] re-showed the transport model of an AI unit the player now sees");
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("fogged-embarkation:finalizer", "[Patch:FoggedEmbarkation] finalizer failed: " + ex.Message);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SoundEngine), "AddPurposeArray")]
        internal static class SharedFiringSounds
        {
            [HarmonyPrefix]
            internal static bool Prefix(SoundEngine __instance, int soundpurposeid, ref int __result)
            {
                using (TelemetryPerf.Scope("campaign.patch.fogged-embarkation.shared-sounds.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.fogged-embarkation.shared-sounds.prefix");
                    try
                    {
                        if (!FoggedModelRule.ShouldShareFiringSounds(Flip.Active, soundpurposeid, Regiment.csound_shipfiring)) return true;
                        if (!FiringSounds.TryGet(__instance, out int index)) return true;
                        __result = index;
                        OnceLog.Info("fogged-embarkation:shared-sounds", "[Patch:FoggedEmbarkation] a hidden AI transport model reuses one ship-firing sound array");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("fogged-embarkation:shared-sounds-failed", "[Patch:FoggedEmbarkation] sound sharing failed (vanilla adds its array): " + ex.Message);
                        return true;
                    }
                }
            }

            [HarmonyPostfix]
            // After the Prefix skipped vanilla, __result is the remembered index already;
            // remembering it again is a no-op.
            internal static void Postfix(SoundEngine __instance, int soundpurposeid, int __result)
            {
                using (TelemetryPerf.Scope("campaign.patch.fogged-embarkation.shared-sounds.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.fogged-embarkation.shared-sounds.postfix");
                    try
                    {
                        if (FoggedModelRule.ShouldShareFiringSounds(Flip.Active, soundpurposeid, Regiment.csound_shipfiring))
                            FiringSounds.Remember(__instance, __result);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("fogged-embarkation:shared-sounds-remember", "[Patch:FoggedEmbarkation] remembering the sound array failed: " + ex.Message);
                    }
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
