using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-014. Vanilla Regiment.OnDestroy (decompile 127111-127146) clears the unit's
    // path arrows, waypoints, sprites, icons, arcs and lines but never its camp or
    // entrenchment (Regiment.campbuilding / entrenchment, 110616-110620). Those are
    // procedural CBuildings placed by UpdateCampsAndEntrenchments (115410, 115424, 115459)
    // under Battlemap/3DMap/Buildings, not children of the unit, and nothing else tracks
    // them (they are not in BattleUnits.cbuildings and are skipped by SaveBuildings,
    // 72305). A merged, disbanded or destroyed unit therefore leaves its tents and
    // earthworks on the map for the rest of the session; with BUG-LOG-002 a unit removed
    // while hidden would leave an inactive one.
    //
    // The Postfix removes both the way vanilla does when a unit breaks camp
    // (Object.Destroy of the building's GameObject, 115428-115433), for every unit. During
    // a scene unload Unity has already destroyed or will destroy them; a destroyed
    // reference is skipped. Rollback: Campaign Bug Fixes / Enable Orphan Camp Cleanup.
    [HarmonyPatch(typeof(Regiment), "OnDestroy")]
    internal static class OrphanCampCleanupPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Regiment __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.orphan-camp-cleanup.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.orphan-camp-cleanup.postfix");
                try
                {
                    if (!Enabled() || (object)__instance == null) return;
                    bool removed = Remove(__instance.campbuilding);
                    removed |= Remove(__instance.entrenchment);
                    __instance.campbuilding = null;
                    __instance.entrenchment = null;
                    if (removed)
                        OnceLog.Info("orphan-camp-cleanup:removed", "[Patch:OrphanCamp] removed the camp or entrenchment of a destroyed unit");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("orphan-camp-cleanup:postfix", "[Patch:OrphanCamp] postfix failed (camp left in place): " + ex.Message);
                }
            }
        }

        private static bool Remove(CBuilding building)
        {
            if ((UnityEngine.Object)building == null) return false;
            UnityEngine.Object.Destroy(((Component)building).gameObject);
            return true;
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableOrphanCampCleanup != null
                    && Plugin.EnableOrphanCampCleanup.Value;
            }
            catch { return false; }
        }
    }
}
