using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew places AI groups
    // immediately, then clamps deployment-zone and terrain state. This default-off
    // Postfix samples bounded terrain-safe candidates and corrects clear AI
    // terrain/deployment failures through vanilla SetGroupFormation when a safe candidate exists.
    // Visible enemy bearing may shape final facing only after that terrain/deployment gate trips.
    [HarmonyPatch(typeof(BattleUnits), "DoPlacementAIUnitsWithinDeploymentzoneNew")]
    internal static class TacticalDeploymentTerrainDisciplinePatch
    {
        private const int DefaultMaxCandidates = 16;
        private const int MaxAdviceRowsPerBattle = 64;
        private const float DefaultMaxCorrectionMeters = 60f;
        private const float DefaultPreferredFacingDeltaDegrees = 90f;

        private static readonly FieldInfo GrpField = AccessTools.Field(typeof(BattleUnits), "grp");
        private static readonly HashSet<string> EmittedAdvice = new HashSet<string>();

        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance, int foralliance)
        {
            if (!Enabled()) return;

            try
            {
                if ((object)__instance == null) return;
                if (GameVars.playeralliance == foralliance && !GameVars.ai_vs_ai) return;

                BattleUnits.Grp[] groups = ReadGroups(__instance);
                if (groups.Length == 0) return;

                OnceLog.Info(
                    "tactical-deployment-terrain-advice",
                    "TacticalDeploymentTerrainDisciplinePatch advice surface wired.");

                for (int i = 0; i < groups.Length; i++)
                {
                    BattleUnits.Grp group = groups[i];
                    if (group == null || (object)group.regref == null) continue;

                    Regiment regiment = group.regref;
                    if (!Eligible(regiment, foralliance)) continue;

                    TryDisciplineGroup(__instance, group, regiment);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:failed",
                    "TacticalDeploymentTerrainDisciplinePatch failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                   Plugin.Instance.Enabled != null &&
                   Plugin.Instance.Enabled.Value &&
                   Plugin.EnableTacticalDeploymentTerrainDiscipline != null &&
                   Plugin.EnableTacticalDeploymentTerrainDiscipline.Value;
        }

        private static bool Eligible(Regiment regiment, int alliance)
        {
            if (regiment == null) return false;
            if (regiment.alliance != alliance) return false;
            if (regiment.unittyp <= 13) return false;
            if (regiment.isrouted) return false;

            try
            {
                return regiment.gameObject != null && regiment.gameObject.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDisciplineGroup(BattleUnits battleUnits, BattleUnits.Grp group, Regiment regiment)
        {
            try
            {
                Vector3 original = regiment.transform.position;
                float originalFacing = regiment.transform.eulerAngles.y;
                Frontline2 deploymentZone = battleUnits.frontline2;

                TacticalTerrainRuntimeSample center =
                    TacticalTerrainProbe.SampleCenter(regiment, deploymentZone, original);
                IReadOnlyList<TacticalTerrainRuntimeSample> footprint =
                    TacticalTerrainProbe.SampleFootprint(regiment, deploymentZone);
                bool footprintWater = footprint.Any(sample => sample.Water);
                bool footprintOutOfZone = footprint.Any(sample => !sample.InDeploymentZone);
                TacticalEnemyBearingEvidence enemy =
                    TacticalTerrainProbe.GetVisibleEnemyBearing(regiment, original);

                bool terrainFailure = center.Water || footprintWater || !center.InDeploymentZone || footprintOutOfZone;

                if (!terrainFailure)
                    return;

                float preferredFacingDelta = ReadPreferredFacingDeltaDegrees();
                var rules = new TacticalTerrainRules(
                    ReadTerrainMaxCorrectionMeters(),
                    preferredFacingDelta,
                    requireDeploymentZone: true,
                    requireVisibleEnemyForFacing: false);

                TacticalTerrainDecision decision = TacticalTerrainFacingDiscipline.Choose(
                    new TacticalPoint2(original.x, original.z),
                    originalFacing,
                    BuildCandidates(regiment, deploymentZone, original, originalFacing, enemy),
                    enemy,
                    rules);

                EmitEvidence(group, regiment, center, footprintWater, enemy, decision);
                if (!decision.Accepted)
                    return;

                ApplyCorrection(battleUnits, group, regiment, original, decision);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:group",
                    "TacticalDeploymentTerrainDisciplinePatch group correction failed: " + ex.GetType().Name);
            }
        }

        private static void ApplyCorrection(
            BattleUnits battleUnits,
            BattleUnits.Grp group,
            Regiment regiment,
            Vector3 original,
            TacticalTerrainDecision decision)
        {
            try
            {
                if ((object)battleUnits == null || group == null || (object)group.go == null || regiment == null)
                    return;

                Vector3 corrected = new Vector3(
                    decision.Candidate.Point.X,
                    original.y,
                    decision.Candidate.Point.Z);
                corrected = TacticalTerrainProbe.WithTerrainHeight(corrected);

                battleUnits.SetGroupFormation(
                    group.go,
                    regiment.groupformation,
                    decision.Candidate.FacingDegrees,
                    corrected,
                    immediateplacement: true,
                    newpath: true,
                    modifylastwaypoint: false,
                    newstate: 2,
                    refuseflank: -1,
                    ignoredeplyomentzone: false,
                    skiprotation: false,
                    showmovementoptions: false,
                    placeentrenchments: false,
                    adjustbyterrainshape: true);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:set-group-formation",
                    "TacticalDeploymentTerrainDisciplinePatch SetGroupFormation failed: " + ex.GetType().Name);
            }
        }

        private static IEnumerable<TacticalTerrainCandidate> BuildCandidates(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 original,
            float originalFacing,
            TacticalEnemyBearingEvidence enemy)
        {
            int max = Math.Max(1, Plugin.TacticalDeploymentTerrainMaxCandidates != null
                ? Plugin.TacticalDeploymentTerrainMaxCandidates.Value
                : DefaultMaxCandidates);
            float maxCorrection = ReadTerrainMaxCorrectionMeters();
            float[] radii = { 0f, 8f, 16f, 32f, 48f, 60f };
            int produced = 0;

            for (int r = 0; r < radii.Length && produced < max; r++)
            {
                float radius = Math.Min(radii[r], maxCorrection);
                for (int angle = 0; angle < 360 && produced < max; angle += 45)
                {
                    Vector3 point = radius <= 0f
                        ? original
                        : PointAt(original, angle, radius);

                    if (TacticalTerrainProbe.TryBuildCandidate(
                            regiment,
                            deploymentZone,
                            point,
                            originalFacing,
                            enemy,
                            out TacticalTerrainCandidate candidate))
                    {
                        yield return candidate;
                    }

                    produced++;
                    if (radius <= 0f) break;
                }
            }
        }

        private static void EmitEvidence(
            BattleUnits.Grp group,
            Regiment regiment,
            TacticalTerrainRuntimeSample center,
            bool footprintWater,
            TacticalEnemyBearingEvidence enemy,
            TacticalTerrainDecision decision)
        {
            try
            {
                if (EmittedAdvice.Count >= MaxAdviceRowsPerBattle) return;

                string unit = SafeUnitName(group, regiment);
                string key =
                    (regiment != null ? regiment.GetInstanceID().ToString(CultureInfo.InvariantCulture) : unit) +
                    "|" + center.TerrainId +
                    "|" + center.Water +
                    "|" + footprintWater +
                    "|" + center.InDeploymentZone +
                    "|" + decision.Signature;

                if (!EmittedAdvice.Add(key)) return;

                string line = TacticalTerrainFacingTelemetry.Format(new TacticalTerrainFacingLogRow(
                    "DoPlacementAIUnitsWithinDeploymentzoneNew",
                    TacticalDeploymentTelemetry.PhaseInitial,
                    regiment != null ? regiment.alliance : -1,
                    unit,
                    center.TerrainId,
                    center.Water,
                    footprintWater,
                    center.InDeploymentZone,
                    regiment != null ? regiment.transform.eulerAngles.y : 0f,
                    enemy.Visible ? enemy.BearingDegrees : 0f,
                    enemy.Visible ? enemy.DistanceMeters : 0f,
                    decision));

                Plugin.Log.LogInfo(line.Replace("[TacDeployTerrain]", "[TacDeployTerrainAdvice]"));
            }
            catch
            {
            }
        }

        private static BattleUnits.Grp[] ReadGroups(BattleUnits battleUnits)
        {
            if ((object)battleUnits == null) return Array.Empty<BattleUnits.Grp>();
            if (GrpField == null)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain-advice:missing-grp",
                    "Missing BattleUnits.grp; tactical deployment terrain advice disabled.");
                return Array.Empty<BattleUnits.Grp>();
            }

            try
            {
                return GrpField.GetValue(battleUnits) as BattleUnits.Grp[] ?? Array.Empty<BattleUnits.Grp>();
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain-advice:grp",
                    "Failed reading BattleUnits.grp for deployment terrain advice: " + ex.GetType().Name);
                return Array.Empty<BattleUnits.Grp>();
            }
        }

        private static float ReadPreferredFacingDeltaDegrees()
        {
            float value = Plugin.TacticalDeploymentFacingPreferredDeltaDegrees != null
                ? Plugin.TacticalDeploymentFacingPreferredDeltaDegrees.Value
                : DefaultPreferredFacingDeltaDegrees;
            return IsPositiveFinite(value) ? value : DefaultPreferredFacingDeltaDegrees;
        }

        private static float ReadTerrainMaxCorrectionMeters()
        {
            float value = Plugin.TacticalDeploymentTerrainMaxCorrectionMeters != null
                ? Plugin.TacticalDeploymentTerrainMaxCorrectionMeters.Value
                : DefaultMaxCorrectionMeters;
            return IsPositiveFinite(value) ? value : DefaultMaxCorrectionMeters;
        }

        private static Vector3 PointAt(Vector3 origin, float angleDegrees, float distance)
        {
            float radians = angleDegrees * (float)Math.PI / 180f;
            return TacticalTerrainProbe.WithTerrainHeight(new Vector3(
                origin.x + (float)Math.Sin(radians) * distance,
                origin.y,
                origin.z + (float)Math.Cos(radians) * distance));
        }

        private static bool IsPositiveFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }

        private static string SafeUnitName(BattleUnits.Grp group, Regiment regiment)
        {
            string value = group != null && !string.IsNullOrWhiteSpace(group.name)
                ? group.name
                : regiment != null ? regiment.name : "-";
            return TacticalCurrentOrderSignature.Safe(value);
        }
    }
}
