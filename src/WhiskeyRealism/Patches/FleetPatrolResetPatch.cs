using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Regiment.StopRegiment immediately restores patrol waypoints for
    // fleetorders == 2, so AI fleets can keep reversing the same patrol route.
    // When an AI fleet is idle at its patrol start, reset it to normal orders
    // before vanilla enters that restore branch.
    [HarmonyPatch]
    internal static class FleetPatrolResetPatch
    {
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Regiment),
                "StopRegiment",
                new[]
                {
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool)
                });
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.fleet-patrol-reset", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
            try
            {
                if (!Enabled() || __instance == null) return;

                float radius = Math.Max(1f, __instance.buglerange > 0f ? __instance.buglerange : __instance.commanderrange);
                float distance = Tools.GetXZDistance(__instance.GetPosition(), __instance.startpointforpatrols);
                bool isPlayerFleet = __instance.alliance == GameVars.playeralliance;

                if (!FleetPatrolResetGuard.ShouldResetAiPatrolToIdle(
                        isPlayerFleet,
                        __instance.unittyp,
                        __instance.fleetorders,
                        __instance.regimentpaths,
                        distance,
                        radius,
                        __instance.isrouted,
                        __instance.onretreat,
                        __instance.inbattle,
                        __instance.withinrotationprocess))
                {
                    return;
                }

                __instance.SetFleetOrders(0);
                OnceLog.Info("fleet-patrol-reset", "[Patch:FleetPatrol] reset completed AI patrol fleet to normal orders");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fleet-patrol-reset:prefix", "[Patch:FleetPatrol] prefix failed: " + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        private static bool Enabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.Enabled.Value; }
            catch { return false; }
        }
    }
}
