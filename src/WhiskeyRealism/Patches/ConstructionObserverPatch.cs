using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Observes vanilla construction starts after CBuilding.Place and
    // BattleUnits.Railroad.StartConstruction complete. This patch only records
    // successful starts; it does not alter vanilla placement, funding, or
    // railroad construction decisions.
    [HarmonyPatch]
    internal static class ConstructionObserverPatch
    {
        [HarmonyPatch(typeof(CBuilding), "Place")]
        [HarmonyPostfix]
        internal static void CBuildingPlacePostfix(
            CBuilding __result,
            int type,
            int owner,
            bool newlycreated,
            bool pay)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            if (!newlycreated || !pay || __result == null) return;
            if (owner < 0 || owner >= 2) return;

            try
            {
                OnceLog.Info("construction-observer:building", "ConstructionObserverPatch wired (CBuilding.Place)");

                Vector3 position = ((Component)__result).transform.position;
                var start = new ConstructionStartEvent
                {
                    AllianceId = owner,
                    Kind = KindForBuilding(type),
                    BuildingTypeId = type,
                    Name = SafeBuildingName(__result, type),
                    Theater = TheaterFromPosition(position),
                    SiteKey = position.x.ToString("0") + "," + position.z.ToString("0"),
                    Year = SafeYear(),
                    Month = SafeMonth(),
                    Day = SafeDay()
                };

                StrategicCoordinator.Instance?.RecordConstructionStart(start);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-observer:building-failed", "[ConstructionObserver] building observer failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(BattleUnits.Railroad), "StartConstruction")]
        [HarmonyPostfix]
        internal static void RailroadStartPostfix(
            BattleUnits.Railroad __instance,
            int _alliancetoconstruct,
            bool checkonly,
            bool __result)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            if (checkonly || !__result) return;
            if (_alliancetoconstruct < 0 || _alliancetoconstruct >= 2) return;

            try
            {
                OnceLog.Info("construction-observer:railroad", "ConstructionObserverPatch wired (Railroad.StartConstruction)");

                string name = SafeRailroadName(__instance);
                Vector3 position = __instance != null ? __instance.averageposition : default(Vector3);
                var start = new ConstructionStartEvent
                {
                    AllianceId = _alliancetoconstruct,
                    Kind = ConstructionCandidateKind.Railroad,
                    BuildingTypeId = -1,
                    Name = name,
                    Theater = TheaterFromPosition(position),
                    SiteKey = name,
                    Year = SafeYear(),
                    Month = SafeMonth(),
                    Day = SafeDay()
                };

                StrategicCoordinator.Instance?.RecordConstructionStart(start);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("construction-observer:railroad-failed", "[ConstructionObserver] railroad observer failed: " + ex.Message);
            }
        }

        private static ConstructionCandidateKind KindForBuilding(int type)
        {
            if (type == CBuilding.id_supplydepot) return ConstructionCandidateKind.SupplyDepot;
            if (type == CBuilding.id_fort) return ConstructionCandidateKind.Fort;
            if (type == CBuilding.id_telegraphstation) return ConstructionCandidateKind.Telegraph;
            return ConstructionCandidateKind.PrivateBuilding;
        }

        private static string SafeBuildingName(CBuilding building, int type)
        {
            try
            {
                if (building != null && !string.IsNullOrEmpty(building.BuildingName))
                    return building.BuildingName;

                return GameVars.buildingtypes != null && type >= 0 && type < GameVars.buildingtypes.Count
                    ? GameVars.buildingtypes[type].name
                    : "building-" + type;
            }
            catch
            {
                return "building-" + type;
            }
        }

        private static string SafeRailroadName(BattleUnits.Railroad railroad)
        {
            try
            {
                return railroad != null && railroad.scriptref != null && !string.IsNullOrEmpty(railroad.scriptref.RailroadName)
                    ? railroad.scriptref.RailroadName
                    : "<railroad>";
            }
            catch
            {
                return "<railroad>";
            }
        }

        private static int SafeYear()
        {
            try { return Mathf.FloorToInt(GameVars.GetCampaignFloatingDate()); }
            catch { return 0; }
        }

        private static int SafeMonth()
        {
            try { return GameVars.GetCampaignFloatingDateSep().month; }
            catch { return 0; }
        }

        private static int SafeDay()
        {
            try { return GameVars.GetCampaignFloatingDateSep().day; }
            catch { return 0; }
        }

        private static Theater TheaterFromPosition(Vector3 position)
        {
            if (position.x < -200f) return Theater.TransMiss;
            if (position.x > 800f && position.z < -100f) return Theater.Coast;
            if (position.x > 600f) return Theater.East;
            return Theater.West;
        }
    }
}
