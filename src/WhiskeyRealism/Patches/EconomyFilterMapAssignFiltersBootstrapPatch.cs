using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla BattlefieldSetup.AssignFilters assumes Economy.UpdateFilterMaps
    // has already created every economy FilterMap texture. In W&L campaign
    // startup that ordering can be inverted: AssignFilters throws before it
    // creates productionmapsymbols, then UpdateFilterMaps later throws when it
    // reaches productionmapsymbols.ResetValues() in the full-cycle tail.
    [HarmonyPatch(typeof(BattlefieldSetup), "AssignFilters")]
    internal static class EconomyFilterMapAssignFiltersBootstrapPatch
    {
        [HarmonyPrefix]
        internal static void Prefix()
        {
            using (TelemetryPerf.Scope("campaign.patch.economy-filter-assign-bootstrap", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
            try
            {
                if (!Enabled()) return;

                string[] missing = GetMissingFilterMaps();
                if (missing.Length == 0) return;

                GameObject gameController = GameObject.Find("GameController");
                if (gameController == null)
                {
                    OnceLog.Warning(
                        "filter-map-assignfilters-bootstrap:gamecontroller-null",
                        "[Patch:FilterMapAssignFilters] cannot bootstrap economy filter maps before AssignFilters; GameController is null missing="
                            + string.Join(",", missing));
                    return;
                }

                EnsureFilterMap(
                    ref Economy.availableworkforce,
                    gameController,
                    map => map.Initialize(GamePrefs.workforcemapresolution, GamePrefs.minworkforcemap));
                EnsureFilterMap(
                    ref Economy.slavery,
                    gameController,
                    map => map.Initialize(GamePrefs.workforcemapresolution, 0f, 5f));
                EnsureFilterMap(
                    ref Economy.tradeandsupply,
                    gameController,
                    map => map.Initialize(GamePrefs.tradeandsupplyresolution));
                EnsureFilterMap(
                    ref Economy.supply,
                    gameController,
                    map => map.Initialize(GamePrefs.tradeandsupplyresolution));
                EnsureFilterMap(
                    ref Economy.availablecapital,
                    gameController,
                    map => map.Initialize(GamePrefs.availablecapitalfiltermapresolution, GamePrefs.minimumavailablecapital, 5f));
                EnsureFilterMap(
                    ref Economy.transportbottlenecks,
                    gameController,
                    map => map.Initialize(GamePrefs.marketcapacityfiltermapresolution));
                EnsureFilterMap(
                    ref Economy.marketcapacity,
                    gameController,
                    map => map.Initialize(GamePrefs.marketcapacityfiltermapresolution));
                EnsureFilterMap(
                    ref Economy.hospitals,
                    gameController,
                    map => map.Initialize(GamePrefs.hospitalsfiltermapresolution));

                string[] stillMissing = GetMissingFilterMaps();
                if (stillMissing.Length == 0)
                {
                    OnceLog.Warning(
                        "filter-map-assignfilters-bootstrap:initialized",
                        "[Patch:FilterMapAssignFilters] bootstrapped economy filter maps before BattlefieldSetup.AssignFilters missing="
                            + string.Join(",", missing));
                }
                else
                {
                    OnceLog.Warning(
                        "filter-map-assignfilters-bootstrap:still-missing",
                        "[Patch:FilterMapAssignFilters] economy filter-map bootstrap incomplete before BattlefieldSetup.AssignFilters missing="
                            + string.Join(",", stillMissing));
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "filter-map-assignfilters-bootstrap:failed",
                    "[Patch:FilterMapAssignFilters] bootstrap failed: " + ex.GetType().Name + ":" + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        private static string[] GetMissingFilterMaps()
        {
            return CampaignFilterMapInitializationGuard.GetMissingAssignFiltersMapNames(
                MapReady(Economy.availableworkforce),
                MapReady(Economy.slavery),
                MapReady(Economy.tradeandsupply),
                MapReady(Economy.supply),
                MapReady(Economy.availablecapital),
                MapReady(Economy.transportbottlenecks),
                MapReady(Economy.marketcapacity),
                MapReady(Economy.hospitals));
        }

        private static void EnsureFilterMap(ref FilterMap map, GameObject gameController, Action<FilterMap> initialize)
        {
            if (!UnityAlive(map))
                map = gameController.AddComponent<FilterMap>();

            if (!MapReady(map))
                initialize(map);
        }

        private static bool MapReady(FilterMap map)
        {
            return UnityAlive(map) && UnityAlive(map.maptexture);
        }

        private static bool UnityAlive(UnityEngine.Object value)
        {
            return !ReferenceEquals(value, null) && value != null;
        }

        private static bool Enabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.Enabled.Value; }
            catch { return false; }
        }
    }
}
