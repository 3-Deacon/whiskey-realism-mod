using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Regiment.AddPath calculates one NavMesh path segment, but exact
    // endpoint equality and retained failed fragments can poison tactical retries.
    [HarmonyPatch(typeof(Regiment), "AddPath")]
    internal static class TacticalPathfinderDisciplinePatch
    {
        private const float EndpointToleranceMeters = 5f;

        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance, out int __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.pathfinder-discipline-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                __state = SafePathCount(__instance);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(
            Regiment __instance,
            Vector3 target,
            int iterations,
            ref int __result,
            int __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.pathfinder-discipline", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (!Enabled()) return;

                try
                {
                    if (__instance == null || __instance.calledfromcampaign || __instance.unittyp == 17) return;
                    if (__instance.regimentpath == null || __state < 0) return;

                    int after = SafePathCount(__instance);
                    int pathIndex = after - 1;
                    NavMeshPath path = pathIndex >= 0 && pathIndex < __instance.regimentpath.Length
                        ? __instance.regimentpath[pathIndex]
                        : null;
                    Vector3[] corners = path != null ? path.corners : null;
                    int cornerCount = corners != null ? corners.Length : 0;
                    float finalDistance = cornerCount > 0
                        ? XzDistance(corners[cornerCount - 1], target)
                        : 0f;

                    TacticalPathfinderAddPathDecision decision =
                        TacticalBattlefieldBugDiagnostics.ClassifyAddPathOutcome(
                            vanillaResult: __result,
                            pathCountBefore: __state,
                            pathCountAfter: after,
                            cornerCount: cornerCount,
                            navStatus: path != null ? path.status.ToString() : "-",
                            finalDistanceToTarget: finalDistance,
                            endpointTolerance: EndpointToleranceMeters);

                    if (!decision.IsBehaviorChange) return;

                    if (decision.ShouldRemoveAddedPath)
                        RemoveAddedPaths(__instance, __state, after);
                    if (decision.ShouldOverrideResult)
                        __result = decision.OverrideResult;

                    OnceLog.Info(
                        "tactical-pathfinder-discipline:" + decision.Reason,
                        "[TacticalPathfinderDiscipline] reason=" + decision.Reason +
                        " result=" + __result +
                        " iterations=" + iterations +
                        " unit=" + SafeUnitName(__instance) +
                        " " + decision.Signature);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "tactical-pathfinder-discipline:failed",
                        "TacticalPathfinderDisciplinePatch failed; falling back to vanilla AddPath result: " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalPathfinderDiscipline != null &&
                Plugin.Instance.EnableTacticalPathfinderDiscipline.Value;
        }

        private static int SafePathCount(Regiment regiment)
        {
            if (regiment == null) return 0;
            return regiment.regimentpaths < 0 ? 0 : regiment.regimentpaths;
        }

        private static void RemoveAddedPaths(Regiment regiment, int before, int after)
        {
            int safeBefore = Math.Max(0, before);
            int safeAfter = Math.Max(safeBefore, after);
            if (regiment.regimentpath != null)
            {
                int max = Math.Min(safeAfter, regiment.regimentpath.Length);
                for (int i = safeBefore; i < max; i++)
                    regiment.regimentpath[i] = new NavMeshPath();
            }

            if (regiment.pathstatus != null)
            {
                int max = Math.Min(safeAfter, regiment.pathstatus.Length);
                for (int i = safeBefore; i < max; i++)
                    regiment.pathstatus[i] = 0;
            }

            regiment.regimentpaths = safeBefore;
        }

        private static float XzDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string SafeUnitName(Regiment regiment)
        {
            if (regiment == null) return "-";
            string value = regiment.name;
            return TacticalCurrentOrderSignature.Safe(value);
        }
    }
}
