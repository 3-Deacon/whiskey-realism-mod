using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-002. Vanilla Regiment.UpdateCampsAndEntrenchments (decompile 115392-115470)
    // runs for every campaign unit each regular cycle (112224, right after
    // UpdateUnitVisuals) and from BattleUnits.UpdateUnitsEntrenchments (79240). It places a
    // summer camp only when !iswithinfog (115408), turns it into winter quarters in winter
    // months (115421-115425), destroys camp and entrenchment when iswithinfog (115426),
    // and places the entrenchment model only when !iswithinfog (115455). For a unit outside
    // the player's alliance, iswithinfog means "hidden from the player"
    // (CheckRegimentVisibility 127209-127229), so AI armies the player cannot see never
    // encamp, never take winter quarters and never entrench, and lose the effects that
    // test campbuilding / entrenchment: winter supply and movement penalty reset
    // (GetWeatherAdjustment 132917-132922), readiness (115297), attrition (115078-115086),
    // morale (127881-127887) and entrenchment order delay (125041-125046). The manual:
    // "The units will automatically enter winter quarters during winter, if stationary."
    // The fog gate exists because a CBuilding is never fog-hidden (it has no fog code), so
    // a camp model would reveal the unit.
    //
    // For a fogged AI land unit on the campaign map the Prefix clears iswithinfog for the
    // call and swaps sound_objectref for an empty array (vanilla plays the "army encamps"
    // bugle only when sound_objectref.Length != 0, 115411-115414; nothing else in the method
    // reads it); the Finalizer restores both, even if vanilla throws, then deactivates the
    // camp and entrenchment GameObjects while the unit stays hidden from the player and
    // reactivates them once it is visible. An inactive camp still counts (vanilla only tests
    // campbuilding != null and BuildingType), its CBuilding.Update/icon stop (camps have no
    // construction timer, IncreaseCondition returns at once, 96607) and
    // GetComponentsInChildren<CBuilding>() skips it (camps are excluded from saves anyway,
    // 72305). Player-alliance units are never touched (they are never fogged on the campaign
    // map, 127211-127215). Rollback: Campaign Bug Fixes / Enable Fogged AI Camp Fix (models
    // are still re-shown for visible units when the flag is off).
    [HarmonyPatch(typeof(Regiment), "UpdateCampsAndEntrenchments")]
    internal static class FoggedCampPatch
    {
        internal struct Flip
        {
            internal bool Active;
            internal int[] Sounds;
        }

        private static readonly int[] NoSounds = new int[0];
        // NavMesh area ids; -1 until the campaign navmesh resolves them.
        private static int _riverArea = -1;
        private static int _seaArea = -1;

        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance, ref Flip __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.fogged-camp.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.fogged-camp.prefix");
                __state = default(Flip);
                try
                {
                    if (__instance == null || !__instance.iswithinfog) return;
                    if (!FoggedCampRule.ShouldTreatAsVisibleForCamp(
                            Enabled(),
                            __instance.calledfromcampaign,
                            GameVars.usefow,
                            __instance.alliance == GameVars.playeralliance,
                            __instance.iswithinfog,
                            OnWater(__instance.currentterrain)))
                    {
                        return;
                    }

                    __state.Active = true;
                    __state.Sounds = __instance.sound_objectref;
                    __instance.iswithinfog = false;
                    __instance.sound_objectref = NoSounds;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("fogged-camp:prefix", "[Patch:FoggedCamp] prefix failed (vanilla runs unchanged): " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(Regiment __instance, Flip __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.fogged-camp.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.fogged-camp.finalizer");
                try
                {
                    if (__instance == null) return;
                    if (__state.Active)
                    {
                        __instance.iswithinfog = true;
                        __instance.sound_objectref = __state.Sounds;
                        if ((UnityEngine.Object)__instance.campbuilding != null)
                            OnceLog.Info("fogged-camp:kept", "[Patch:FoggedCamp] a fogged AI unit kept its camp (model hidden from the player)");
                    }

                    bool isPlayerAlliance = __instance.alliance == GameVars.playeralliance;
                    if (isPlayerAlliance) return;
                    bool hide = FoggedCampRule.ShouldHideModels(isPlayerAlliance, __instance.iswithinfog);
                    SetModelActive(__instance.campbuilding, !hide);
                    SetModelActive(__instance.entrenchment, !hide);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("fogged-camp:finalizer", "[Patch:FoggedCamp] finalizer failed: " + ex.Message);
                }
            }
        }

        private static void SetModelActive(CBuilding building, bool active)
        {
            if ((UnityEngine.Object)building == null) return;
            var go = ((Component)building).gameObject;
            if (go.activeSelf == active) return;
            go.SetActive(active);
            if (!active)
                OnceLog.Info("fogged-camp:hidden", "[Patch:FoggedCamp] hid a camp or entrenchment model of an AI unit the player cannot see");
            else
                OnceLog.Info("fogged-camp:shown", "[Patch:FoggedCamp] re-showed a camp or entrenchment model of an AI unit the player now sees");
        }

        // CheckModelReplacement (116383, 116399) shows a boat on these areas when the unit
        // is visible, and a unit with a model gets no camp (115408).
        private static bool OnWater(int terrain)
        {
            if (_riverArea < 0) _riverArea = UnityEngine.AI.NavMesh.GetAreaFromName("Rivers");
            if (_seaArea < 0) _seaArea = UnityEngine.AI.NavMesh.GetAreaFromName("FencesWalls, Sea (C)");
            return (_riverArea >= 0 && terrain == _riverArea) || (_seaArea >= 0 && terrain == _seaArea);
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableFoggedAiCampFix != null
                    && Plugin.EnableFoggedAiCampFix.Value;
            }
            catch { return false; }
        }
    }
}
