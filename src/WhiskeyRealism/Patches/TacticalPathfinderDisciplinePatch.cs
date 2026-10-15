using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #53 — Regiment.AddPath (decompile 130259) calculates one NavMesh path
    // segment for RegimentSetPath's retry loop (131167-131178, up to 20
    // iterations). AddPath returns 1 only on exact float equality between the
    // last corner and the target (130517), which a navmesh-snapped target never
    // satisfies, so vanilla keeps adding sliver segments until the iteration
    // cap. This Postfix ends that loop (result 0 -> 1) when the added segment
    // already ends within tolerance of the target.
    //
    // It never removes a segment: vanilla deliberately accepts partial/invalid
    // paths once iterations >= 15 (130511) and chains from a mismatched
    // endpoint, and removing them left units with zero paths (R10). AI-controlled
    // units only, behind TacticalWriteGuard (Commander Mode + W&L/player), so
    // player right-click orders are untouched.
    [HarmonyPatch(typeof(Regiment), "AddPath")]
    internal static class TacticalPathfinderDisciplinePatch
    {
        private const float EndpointToleranceMeters = 5f;
        private static readonly Vector3[] CornerBuffer = new Vector3[128];

        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance, out int __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.pathfinder-discipline-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.pathfinder-discipline-prefix");
                __state = (UnityEngine.Object)__instance != null ? Math.Max(0, __instance.regimentpaths) : -1;
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
                PatchFirstFire.Mark("tactical.patch.pathfinder-discipline");
                if (__state < 0 || __result != 0) return;

                try
                {
                    int after = __instance.regimentpaths;
                    if (after <= __state) return;
                    if (!Enabled()) return;
                    if (__instance.calledfromcampaign || __instance.unittyp == 17) return;

                    NavMeshPath[] paths = __instance.regimentpath;
                    int pathIndex = after - 1;
                    if (paths == null || pathIndex < 0 || pathIndex >= paths.Length || paths[pathIndex] == null) return;

                    if (!TryLastCorner(paths[pathIndex], out Vector3 lastCorner, out int cornerCount)) return;

                    AddPathDecision decision = BattleWriterPolicy.DecideAddPath(
                        pathAdded: true,
                        cornerCount: cornerCount,
                        vanillaResult: __result,
                        finalDistance: XzDistance(lastCorner, target),
                        tolerance: EndpointToleranceMeters);
                    if (!decision.OverrideResult) return;
                    if (!TacticalWriteGuard.CanWrite(__instance)) return;

                    __result = decision.Result;
                    OnceLog.Info(
                        "tactical-pathfinder-discipline:endpoint-within-tolerance",
                        "[TacticalPathfinderDiscipline] reason=" + decision.Reason +
                        " iterations=" + iterations +
                        " corners=" + cornerCount);
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

        // GetCornersNonAlloc truncates to the buffer; fall back to the
        // allocating corners array only for very long paths.
        private static bool TryLastCorner(NavMeshPath path, out Vector3 lastCorner, out int cornerCount)
        {
            lastCorner = default(Vector3);
            cornerCount = path.GetCornersNonAlloc(CornerBuffer);
            if (cornerCount <= 0) return false;
            if (cornerCount < CornerBuffer.Length)
            {
                lastCorner = CornerBuffer[cornerCount - 1];
                return true;
            }

            Vector3[] corners = path.corners;
            cornerCount = corners != null ? corners.Length : 0;
            if (cornerCount <= 0) return false;
            lastCorner = corners[cornerCount - 1];
            return true;
        }

        private static float XzDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
