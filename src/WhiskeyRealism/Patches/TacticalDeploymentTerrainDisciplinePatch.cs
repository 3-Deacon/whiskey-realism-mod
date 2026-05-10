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
        private static readonly FieldInfo EodCycleField = AccessTools.Field(typeof(BattleUnits), "eodcycle");
        private static readonly FieldInfo BattlePassedDaysField = AccessTools.Field(typeof(BattleUnits), "battlepasseddays");
        private static readonly HashSet<string> EmittedAdvice = new HashSet<string>();
        private static string _emittedAdviceBattleKey;

        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance, int foralliance)
        {
            if (!Enabled()) return;

            try
            {
                if ((object)__instance == null) return;
                if (GameVars.playeralliance == foralliance && !GameVars.ai_vs_ai) return;
                if (GameVars.tutorialactive && !Tutorial.engaged) return;

                BattleUnits.Grp[] groups = ReadGroups(__instance);
                if (groups.Length == 0) return;

                if (!TryReadInitialDeployment(__instance, out bool initialDeployment))
                    return;

                string phase = ReadDeploymentPhase(__instance);
                ResetAdviceLogForBattle(__instance, phase);

                OnceLog.Info(
                    "tactical-deployment-terrain-advice",
                    "TacticalDeploymentTerrainDisciplinePatch advice surface wired.");

                for (int i = 0; i < groups.Length; i++)
                {
                    BattleUnits.Grp group = groups[i];
                    if (group == null || (object)group.regref == null) continue;

                    Regiment regiment = group.regref;
                    if (!EligibleForVanillaDeploymentPlacement(
                            __instance,
                            group,
                            regiment,
                            foralliance,
                            initialDeployment))
                    {
                        continue;
                    }

                    TryDisciplineGroup(__instance, group, regiment, phase);
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

        private static bool EligibleForVanillaDeploymentPlacement(
            BattleUnits battleUnits,
            BattleUnits.Grp group,
            Regiment regiment,
            int alliance,
            bool initialDeployment)
        {
            if ((object)battleUnits == null || group == null) return false;
            if (regiment == null) return false;
            if (group.alliance != alliance) return false;
            if (regiment.alliance != alliance) return false;
            if (regiment.unittyp <= 13) return false;
            if (regiment.isrouted) return false;

            try
            {
                if (regiment.gameObject == null || !regiment.gameObject.activeInHierarchy)
                    return false;
                if ((object)group.go == null)
                    return false;

                bool vanillaOwned =
                    group.isbattledataunit ||
                    (UnityEngine.Object)regiment.groupaiobject == (UnityEngine.Object)regiment.gameObject;
                if (!vanillaOwned)
                    return false;
                if (regiment.groupposition == null)
                    return false;
                if (!TryIsWlCurrentCommandExcluded(regiment, out bool excluded) || excluded)
                    return false;

                return !group.IsApplicableForDeployment(battleUnits, initialDeployment);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:eligibility",
                    "TacticalDeploymentTerrainDisciplinePatch eligibility failed: " + ex.GetType().Name);
                return false;
            }
        }

        private static bool TryReadInitialDeployment(BattleUnits battleUnits, out bool initialDeployment)
        {
            initialDeployment = false;
            if ((object)battleUnits == null)
                return false;
            if (BattlePassedDaysField == null)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:missing-battlepasseddays",
                    "Missing BattleUnits.battlepasseddays; tactical deployment terrain discipline disabled.");
                return false;
            }

            try
            {
                object value = BattlePassedDaysField.GetValue(battleUnits);
                if (value is int days)
                {
                    initialDeployment = days <= 0;
                    return true;
                }
                if (value is float daysFloat)
                {
                    initialDeployment = daysFloat <= 0f;
                    return true;
                }

                OnceLog.Warning(
                    "tactical-deployment-terrain:battlepasseddays-type",
                    "Unsupported BattleUnits.battlepasseddays type; tactical deployment terrain discipline disabled.");
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:battlepasseddays",
                    "Failed reading BattleUnits.battlepasseddays: " + ex.GetType().Name);
                return false;
            }
        }

        private static bool TryIsWlCurrentCommandExcluded(Regiment regiment, out bool excluded)
        {
            excluded = false;

            try
            {
                if (!DLC_WL.dlc_scenarioactive)
                    return true;
                if (DLC_WL.dlc_chosencommander < 0)
                    return true;
                if (GameVars.commander == null || DLC_WL.dlc_chosencommander >= GameVars.commander.Count)
                    return false;

                Regiment currentCommand = GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
                if ((UnityEngine.Object)currentCommand == null)
                    return true;
                if ((UnityEngine.Object)currentCommand.groupaiobjectreg == null)
                    return true;

                excluded =
                    (UnityEngine.Object)currentCommand.groupaiobjectreg == (UnityEngine.Object)currentCommand &&
                    (UnityEngine.Object)currentCommand == (UnityEngine.Object)regiment;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:wl-current-command",
                    "Failed reading W&L current-command deployment exclusion: " + ex.GetType().Name);
                excluded = false;
                return false;
            }
        }

        private static void TryDisciplineGroup(BattleUnits battleUnits, BattleUnits.Grp group, Regiment regiment, string phase)
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

                EmitEvidence(group, regiment, phase, center, footprintWater, enemy, decision);
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

                ClearPostDeploymentState(regiment);
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
            string phase,
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
                    phase,
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

        private static void ResetAdviceLogForBattle(BattleUnits battleUnits, string phase)
        {
            string key = BuildAdviceBattleKey(battleUnits, phase);
            if (string.Equals(_emittedAdviceBattleKey, key, StringComparison.Ordinal))
                return;

            _emittedAdviceBattleKey = key;
            EmittedAdvice.Clear();
        }

        private static string BuildAdviceBattleKey(BattleUnits battleUnits, string phase)
        {
            try
            {
                int instanceId = (UnityEngine.Object)battleUnits != null
                    ? ((UnityEngine.Object)battleUnits).GetInstanceID()
                    : 0;
                int days = ReadIntField(battleUnits, BattlePassedDaysField, "battlepasseddays");
                int eod = ReadIntField(battleUnits, EodCycleField, "eodcycle");

                return instanceId.ToString(CultureInfo.InvariantCulture) +
                    ":" + days.ToString(CultureInfo.InvariantCulture) +
                    ":" + eod.ToString(CultureInfo.InvariantCulture) +
                    ":" + phase;
            }
            catch
            {
                return string.IsNullOrWhiteSpace(phase) ? "unknown" : phase;
            }
        }

        private static string ReadDeploymentPhase(BattleUnits battleUnits)
        {
            int eod = ReadIntField(battleUnits, EodCycleField, "eodcycle");
            int days = ReadIntField(battleUnits, BattlePassedDaysField, "battlepasseddays");
            return TacticalDeploymentTelemetry.PhaseFromPrefix(false, false, eod, days);
        }

        private static int ReadIntField(BattleUnits battleUnits, FieldInfo field, string fieldName)
        {
            if ((object)battleUnits == null || field == null) return 0;

            try
            {
                object value = field.GetValue(battleUnits);
                if (value is int intValue) return intValue;
                if (value is float floatValue) return (int)Math.Floor(floatValue);
                return 0;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:" + fieldName,
                    "Failed reading BattleUnits." + fieldName + ": " + ex.GetType().Name);
                return 0;
            }
        }

        private static void ClearPostDeploymentState(Regiment regiment)
        {
            var visited = new HashSet<int>();
            ClearPostDeploymentState(regiment, visited);
        }

        private static void ClearPostDeploymentState(Regiment regiment, HashSet<int> visited)
        {
            try
            {
                if ((UnityEngine.Object)regiment == null) return;
                int id = regiment.GetInstanceID();
                if (!visited.Add(id)) return;

                regiment.lastsetwaypointposition = default(Vector3);
                regiment.immediateunitplacement = false;

                if (regiment.groupposition == null || regiment.groupposition.attachedunitreg == null)
                    return;

                int attached = Math.Min(
                    regiment.groupposition.attachedunits,
                    regiment.groupposition.attachedunitreg.Length);
                for (int i = 0; i < attached; i++)
                    ClearPostDeploymentState(regiment.groupposition.attachedunitreg[i], visited);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-terrain:cleanup",
                    "TacticalDeploymentTerrainDisciplinePatch cleanup failed: " + ex.GetType().Name);
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
