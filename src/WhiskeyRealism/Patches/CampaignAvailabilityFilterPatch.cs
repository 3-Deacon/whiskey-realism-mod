using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Campaign planner availability filters (plan G5a, spec component 6). Two Postfixes that only ever turn a vanilla "yes" into
    // "no": they keep the formations the allocation reserves, the mover's own and (offense only) the planned mass and a deferred
    // decision's Support out of vanilla's candidate pools, so vanilla's own AI cannot spend them behind the planner's back.
    //
    //   IsUnitAvailableForOffensiveOperations(int, Regiment, Vector3, bool, bool)  (decompile 14080, public static)
    //     the test CheckOffensiveMovements applies to its lead and to every formation it recruits into a package (14234, 14263),
    //     and RollUpEnemyObjectivesInZone (13953) and CheckOccupationOfLostCities (17448) apply too. __result = false when
    //     CampaignWithholdView.WithheldFromOffense: Reserve, CapitalGuard, DepotBuilder, Recon, Recover, Hold at its sector's
    //     need, a formation in transit, a MainEffort member of the mass (only the mover launches it: chapters decision 5) and a
    //     Support formation of a decision whose launch the tempo roll has not released (the map-group player is never withheld).
    //   IsUnitAvailableForDefensiveOperation(int _aifaction, Regiment unit, Vector3 enemyposition)  (13460, private static)
    //     the test CheckForDefensiveOperations applies at 13610 and 13658. __result = false when
    //     CampaignWithholdView.WithheldFromDefense: the always-withheld roles and a formation in transit; never the mass or the
    //     deferred Support (vanilla's defence may still take them, and the next snapshot sees them in a defensive operation:
    //     TheaterPlanner.Busy), and a Hold formation withheld at its sector's need stays available against an enemy standing in
    //     that sector (vanilla passes enemyunits[j]'s position, its unsorted-list bug BUG-AI-002; the Postfix judges whatever
    //     position vanilla passed).
    //
    // Reads the published plan only (a stale plan, a pre-war scenario and an alliance other than 0 and 1 withhold nothing).
    // Rollback: Enable Campaign Planner Executors. Postfix only: no Prefix, no Transpiler.
    [HarmonyPatch]
    internal static class PlannerOffensiveAvailabilityPatch
    {
        [HarmonyTargetMethod]
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AICampaign), "IsUnitAvailableForOffensiveOperations",
                new[] { typeof(int), typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool) });
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction, Regiment unit, ref bool __result)
        {
            using (TelemetryPerf.Scope("campaign.patch.planner-availability.offensive", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.planner-availability.offensive");
                try
                {
                    if (!__result) return;
                    if ((UnityEngine.Object)unit == null || !CampaignPlannerRuntime.ExecutorsEnabled()) return;
                    int alliance = PlannerFactionMap.AllianceOf(_aifaction);
                    if (CampaignWithholdView.WithheldFromOffense(alliance, unit.GetInstanceID()))
                        __result = false;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("planner-availability:offensive", "[Patch:PlannerAvailability] offensive Postfix failed (vanilla's answer kept): " + ex.Message);
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class PlannerDefensiveAvailabilityPatch
    {
        [HarmonyTargetMethod]
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AICampaign), "IsUnitAvailableForDefensiveOperation",
                new[] { typeof(int), typeof(Regiment), typeof(Vector3) });
        }

        [HarmonyPostfix]
        internal static void Postfix(int _aifaction, Regiment unit, Vector3 enemyposition, ref bool __result)
        {
            using (TelemetryPerf.Scope("campaign.patch.planner-availability.defensive", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.planner-availability.defensive");
                try
                {
                    if (!__result) return;
                    if ((UnityEngine.Object)unit == null || !CampaignPlannerRuntime.ExecutorsEnabled()) return;
                    int alliance = PlannerFactionMap.AllianceOf(_aifaction);
                    if (CampaignWithholdView.WithheldFromDefense(alliance, unit.GetInstanceID(), enemyposition.x, enemyposition.z))
                        __result = false;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("planner-availability:defensive", "[Patch:PlannerAvailability] defensive Postfix failed (vanilla's answer kept): " + ex.Message);
                }
            }
        }
    }
}
