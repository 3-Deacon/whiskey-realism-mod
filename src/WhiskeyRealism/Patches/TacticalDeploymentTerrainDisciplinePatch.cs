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
    // Postfix is advice-only for now: it samples terrain/facing candidates and logs
    // bounded evidence rows without changing vanilla deployment state.
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

                    TryAdviseGroup(__instance, group, regiment);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain-advice:failed",
                    "TacticalDeploymentTerrainDisciplinePatch advice failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                   Plugin.Instance.Enabled != null &&
                   Plugin.Instance.Enabled.Value &&
                   ReadBoolConfig("EnableTacticalDeploymentTerrainDiscipline", false);
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

        private static void TryAdviseGroup(BattleUnits battleUnits, BattleUnits.Grp group, Regiment regiment)
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

                float preferredFacingDelta = ReadFloatConfig(
                    "TacticalDeploymentFacingPreferredDeltaDegrees",
                    DefaultPreferredFacingDeltaDegrees);
                float facingDelta = enemy.Visible
                    ? TacticalTerrainFacingDiscipline.AngleDelta(originalFacing, enemy.BearingDegrees)
                    : 0f;
                bool terrainFailure = center.Water || footprintWater || !center.InDeploymentZone || footprintOutOfZone;
                bool facingAdvice = enemy.Visible && facingDelta > preferredFacingDelta;

                if (!terrainFailure && !facingAdvice)
                    return;

                var rules = new TacticalTerrainRules(
                    ReadFloatConfig("TacticalDeploymentTerrainMaxCorrectionMeters", DefaultMaxCorrectionMeters),
                    preferredFacingDelta,
                    requireDeploymentZone: true,
                    requireVisibleEnemyForFacing: false);

                TacticalTerrainDecision decision = TacticalTerrainFacingDiscipline.Choose(
                    new TacticalPoint2(original.x, original.z),
                    originalFacing,
                    BuildCandidates(regiment, deploymentZone, original, originalFacing, enemy),
                    enemy,
                    rules);

                EmitAdvice(group, regiment, center, footprintWater, enemy, decision);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain-advice:group",
                    "TacticalDeploymentTerrainDisciplinePatch group advice failed: " + ex.GetType().Name);
            }
        }

        private static IEnumerable<TacticalTerrainCandidate> BuildCandidates(
            Regiment regiment,
            Frontline2 deploymentZone,
            Vector3 original,
            float originalFacing,
            TacticalEnemyBearingEvidence enemy)
        {
            int max = Math.Max(1, ReadIntConfig("TacticalDeploymentTerrainMaxCandidates", DefaultMaxCandidates));
            float maxCorrection = ReadFloatConfig(
                "TacticalDeploymentTerrainMaxCorrectionMeters",
                DefaultMaxCorrectionMeters);
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

        private static void EmitAdvice(
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

        private static bool ReadBoolConfig(string fieldName, bool fallback)
        {
            object value = ReadConfigValue(fieldName);
            return value is bool boolValue ? boolValue : fallback;
        }

        private static int ReadIntConfig(string fieldName, int fallback)
        {
            object value = ReadConfigValue(fieldName);
            if (value is int intValue) return intValue;

            try
            {
                return value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static float ReadFloatConfig(string fieldName, float fallback)
        {
            object value = ReadConfigValue(fieldName);
            if (value is float floatValue && IsPositiveFinite(floatValue)) return floatValue;

            try
            {
                float converted = value != null
                    ? Convert.ToSingle(value, CultureInfo.InvariantCulture)
                    : fallback;
                return IsPositiveFinite(converted) ? converted : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static object ReadConfigValue(string fieldName)
        {
            try
            {
                FieldInfo field = typeof(Plugin).GetField(
                    fieldName,
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) return null;

                object owner = field.IsStatic ? null : Plugin.Instance;
                object entry = field.GetValue(owner);
                if (entry == null) return null;

                PropertyInfo valueProperty = entry.GetType().GetProperty("Value");
                return valueProperty != null ? valueProperty.GetValue(entry, null) : null;
            }
            catch
            {
                return null;
            }
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
