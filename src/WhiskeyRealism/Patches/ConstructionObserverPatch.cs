using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Observes vanilla construction starts after CBuilding.Place and
    // BattleUnits.Railroad.StartConstruction complete. This patch only records
    // successful starts; it does not alter vanilla placement, funding, or
    // railroad construction decisions. A building removed by an earlier Place
    // postfix (Telegraph AI rejecting a station) is not counted.
    [HarmonyPatch]
    internal static class ConstructionObserverPatch
    {
        [HarmonyPatch(typeof(CBuilding), "Place")]
        [HarmonyPostfix]
        internal static void CBuildingPlacePostfix(
            CBuilding __result,
            Vector3 position,
            int type,
            int owner,
            bool newlycreated)
        {
            using (TelemetryPerf.Scope("campaign.patch.construction-observer.building", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.construction-observer.building");
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (__result == null) return;
                if (!ConstructionStartFilter.CountsBuildingStart(
                        newlycreated,
                        owner,
                        IsConstructionWishPlaceholder(position),
                        StillRegistered(__result)))
                    return;

                try
                {
                    OnceLog.Info("construction-observer:building", "ConstructionObserverPatch wired (CBuilding.Place)");

                    Vector3 actualPosition = ((Component)__result).transform.position;
                    var start = new ConstructionStartEvent
                    {
                        AllianceId = owner,
                        Kind = KindForBuilding(type),
                        Name = SafeBuildingName(__result, type),
                        Theater = TheaterFromPosition(actualPosition),
                        SiteKey = actualPosition.x.ToString("0") + "," + actualPosition.z.ToString("0")
                    };

                    StrategicCoordinator.Instance?.RecordConstructionStart(start);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("construction-observer:building-failed", "[ConstructionObserver] building observer failed: " + ex.Message);
                }
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
            using (TelemetryPerf.Scope("campaign.patch.construction-observer.railroad", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.construction-observer.railroad");
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
                        Name = name,
                        Theater = TheaterFromPosition(position),
                        SiteKey = name
                    };

                    StrategicCoordinator.Instance?.RecordConstructionStart(start);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("construction-observer:railroad-failed", "[ConstructionObserver] railroad observer failed: " + ex.Message);
                }
            }
        }

        private static ConstructionCandidateKind KindForBuilding(int type)
        {
            if (type == CBuilding.id_supplydepot) return ConstructionCandidateKind.SupplyDepot;
            if (type == CBuilding.id_fort) return ConstructionCandidateKind.Fort;
            if (type == CBuilding.id_telegraphstation) return ConstructionCandidateKind.Telegraph;
            return ConstructionCandidateKind.PrivateBuilding;
        }

        // TelegraphPlacementPatch runs first (Priority.High) and may remove the
        // building it was handed; RemoveBuilding drops it from cbuildings.
        private static bool StillRegistered(CBuilding building)
        {
            try
            {
                return BattleUnits.cbuildings != null && BattleUnits.cbuildings.Contains(building);
            }
            catch
            {
                return true;
            }
        }

        private static bool IsConstructionWishPlaceholder(Vector3 position)
        {
            return position.x < -10000f && position.y < -10000f && position.z < -10000f;
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

        private static Theater TheaterFromPosition(Vector3 position)
        {
            return CampaignMapTheaterRuntime.FromPosition(position);
        }
    }
}
