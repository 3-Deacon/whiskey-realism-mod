using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Read-only observer for vanilla deployment positioning:
    // BattleUnits.DoUnitPositioning, BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew,
    // and BattleUI.SetActiveDeploymentPhase. The patch snapshots vanilla group positions
    // before/after those surfaces and logs bounded telemetry without changing unit state.
    [HarmonyPatch]
    internal static class TacticalDeploymentObserverPatch
    {
        private const int TopMoveLimit = 8;
        private static readonly FieldInfo GrpField = AccessTools.Field(typeof(BattleUnits), "grp");
        private static readonly FieldInfo EodCycleField = AccessTools.Field(typeof(BattleUnits), "eodcycle");
        private static readonly FieldInfo BattlePassedDaysField = AccessTools.Field(typeof(BattleUnits), "battlepasseddays");
        private static FieldInfo _battleUiBattleUnitsField;

        internal sealed class ObservationState
        {
            public TacticalDeploymentSnapshot Before { get; set; }
            public string Phase { get; set; }
            public bool SuppressOuterDelta { get; set; }
            public bool Noop { get; set; }
        }

        [HarmonyPatch(typeof(BattleUnits), "DoUnitPositioning")]
        [HarmonyPrefix]
        internal static void DoUnitPositioningPrefix(BattleUnits __instance, ref ObservationState __state)
        {
            if (!Enabled()) return;
            __state = SafeCaptureState(__instance, "pre-unit-positioning", -1, TacticalDeploymentTelemetry.PhaseInitialPositioning);
        }

        [HarmonyPatch(typeof(BattleUnits), "DoUnitPositioning")]
        [HarmonyPostfix]
        internal static void DoUnitPositioningPostfix(BattleUnits __instance, ObservationState __state)
        {
            if (!Enabled()) return;
            EmitDelta(__instance, "DoUnitPositioning", -1, __state);
        }

        [HarmonyPatch(typeof(BattleUnits), "DoPlacementAIUnitsWithinDeploymentzoneNew")]
        [HarmonyPrefix]
        internal static void DoPlacementPrefix(BattleUnits __instance, int foralliance, ref ObservationState __state)
        {
            if (!Enabled()) return;
            var skipped = IsDoPlacementSkipped(foralliance);
            var eod = ReadInt(__instance, EodCycleField, "eodcycle", "eodcycle");
            var days = ReadInt(__instance, BattlePassedDaysField, "battlepasseddays", "battlepasseddays");
            var phase = TacticalDeploymentTelemetry.PhaseFromPrefix(skipped, false, eod, days);
            __state = SafeCaptureState(__instance, "pre-placement", foralliance, phase);
        }

        [HarmonyPatch(typeof(BattleUnits), "DoPlacementAIUnitsWithinDeploymentzoneNew")]
        [HarmonyPostfix]
        internal static void DoPlacementPostfix(BattleUnits __instance, int foralliance, ObservationState __state)
        {
            if (!Enabled()) return;
            EmitDelta(__instance, "DoPlacementAIUnitsWithinDeploymentzoneNew", foralliance, __state);
        }

        [HarmonyPatch(typeof(BattleUI), "SetActiveDeploymentPhase")]
        [HarmonyPrefix]
        internal static void SetActiveDeploymentPhasePrefix(
            BattleUI __instance,
            bool active,
            bool showsupplybydefault,
            bool calledfromsave,
            ref ObservationState __state)
        {
            if (!Enabled()) return;
            if ((object)__instance == null || __instance.IsCampaign)
            {
                __state = new ObservationState { Noop = true };
                return;
            }

            BattleUnits battleUnits = GetBattleUnits(__instance);
            var eod = ReadInt(battleUnits, EodCycleField, "eodcycle", "eodcycle");
            var days = ReadInt(battleUnits, BattlePassedDaysField, "battlepasseddays", "battlepasseddays");
            var phase = TacticalDeploymentTelemetry.PhaseFromPrefix(false, false, eod, days);
            __state = SafeCaptureState(battleUnits, active ? "pre-open" : "pre-close", -1, phase);
            if (__state == null) return;
            __state.SuppressOuterDelta = !active && __state.Before != null && __state.Before.EodCycle > 0;
        }

        [HarmonyPatch(typeof(BattleUI), "SetActiveDeploymentPhase")]
        [HarmonyPostfix]
        internal static void SetActiveDeploymentPhasePostfix(
            BattleUI __instance,
            bool active,
            bool showsupplybydefault,
            bool calledfromsave,
            ObservationState __state)
        {
            if (!Enabled()) return;
            if (__state == null || __state.Noop || (object)__instance == null || __instance.IsCampaign) return;
            try
            {
                BattleUnits battleUnits = GetBattleUnits(__instance);
                var before = __state.Before ?? TacticalDeploymentSnapshot.Empty("before", -1, 0, 0, __state.Phase);
                string action = active ? "open" : "close";
                LogDeploymentTelemetry("[TacticalDeploymentPhase]" +
                                       " action=" + action +
                                       " calledFromSave=" + calledfromsave +
                                       " eod=" + before.EodCycle +
                                       " days=" + before.BattlePassedDays +
                                       " groups=" + before.Groups.Count +
                                       " outerDeltaSuppressed=" + __state.SuppressOuterDelta);
                if (!__state.SuppressOuterDelta)
                {
                    EmitDelta(battleUnits, "SetActiveDeploymentPhase:" + action, -1, __state);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-observer:phase", "TacticalDeploymentObserverPatch deployment-phase observer failed: " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                   Plugin.Instance.Enabled != null &&
                   Plugin.Instance.Enabled.Value &&
                   Plugin.EnableTacticalDeploymentObserver != null &&
                   Plugin.EnableTacticalDeploymentObserver.Value;
        }

        internal static ObservationState CaptureState(BattleUnits battleUnits, string label, int alliance, string phase)
        {
            return new ObservationState
            {
                Before = Capture(battleUnits, label, alliance, phase),
                Phase = TacticalDeploymentTelemetry.NormalizePhase(phase, 0, 0)
            };
        }

        private static ObservationState SafeCaptureState(BattleUnits battleUnits, string label, int alliance, string phase)
        {
            try
            {
                return CaptureState(battleUnits, label, alliance, phase);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-observer:capture:" + label,
                    "TacticalDeploymentObserverPatch capture failed for " + label + ": " + ex.GetType().Name);
                return null;
            }
        }

        private static TacticalDeploymentSnapshot Capture(BattleUnits battleUnits, string label, int alliance, string phase)
        {
            if ((object)battleUnits == null)
            {
                return TacticalDeploymentSnapshot.Empty(label, alliance, 0, 0, phase);
            }

            var groups = new List<TacticalDeploymentGroupSnapshot>();
            BattleUnits.Grp[] battleGroups = ReadGroups(battleUnits);
            if (battleGroups != null)
            {
                for (int i = 0; i < battleGroups.Length; i++)
                {
                    BattleUnits.Grp group = battleGroups[i];
                    if (group == null || (object)group.regref == null) continue;
                    Regiment regiment = group.regref;
                    if (alliance >= 0 && regiment.alliance != alliance) continue;
                    groups.Add(SnapshotGroup(regiment, group));
                }
            }

            int eod = ReadInt(battleUnits, EodCycleField, "eodcycle", "eodcycle");
            int days = ReadInt(battleUnits, BattlePassedDaysField, "battlepasseddays", "battlepasseddays");
            return new TacticalDeploymentSnapshot(label, alliance, eod, days, groups, phase);
        }

        private static TacticalDeploymentGroupSnapshot SnapshotGroup(Regiment regiment, BattleUnits.Grp group)
        {
            Vector3 position = ((Component)regiment).transform.position;
            string name = !string.IsNullOrEmpty(group.name) ? group.name : ((UnityEngine.Object)regiment).name;
            string key = regiment.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            int formation = SafeInt(() => regiment.formation);
            int formationOrdered = SafeInt(() => regiment.formationordered);
            int pathCount = SafeInt(() => regiment.regimentpaths);
            bool routed = SafeBool(() => regiment.isrouted);
            bool active = SafeBool(() => ((Component)regiment).gameObject.activeInHierarchy);
            float facing = SafeFloat(() => ((Component)regiment).transform.eulerAngles.y);

            return new TacticalDeploymentGroupSnapshot(
                key,
                name,
                regiment.alliance,
                regiment.unittyp,
                position.x,
                position.z,
                formation,
                formationOrdered,
                pathCount,
                routed,
                active,
                facing: facing);
        }

        private static void EmitDelta(BattleUnits battleUnits, string surface, int alliance, ObservationState state)
        {
            if (state == null || state.Noop) return;
            try
            {
                OnceLog.Info("tactical-deployment-observer", "TacticalDeploymentObserverPatch wired.");
                var before = state.Before ?? TacticalDeploymentSnapshot.Empty("before", alliance, 0, 0, state.Phase);
                var after = Capture(battleUnits, "post", alliance, state.Phase);
                var delta = TacticalDeploymentTelemetry.Delta(surface, before, after);
                LogDeploymentTelemetry(TacticalDeploymentTelemetry.FormatSummary(delta));
                foreach (string line in TopMoveLines(battleUnits, surface, before, after))
                {
                    LogDeploymentTelemetry(line);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-observer:delta", "TacticalDeploymentObserverPatch delta observer failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static IEnumerable<string> TopMoveLines(BattleUnits battleUnits, string surface, TacticalDeploymentSnapshot before, TacticalDeploymentSnapshot after)
        {
            if (before == null || after == null) yield break;

            var beforeByKey = before.Groups.GroupBy(g => g.Key).ToDictionary(g => g.Key, g => g.First());
            var moves = after.Groups
                .Where(g => beforeByKey.ContainsKey(g.Key))
                .Select(g => new { Before = beforeByKey[g.Key], After = g, Distance = beforeByKey[g.Key].DistanceTo(g) })
                .Where(m => m.Distance >= TacticalDeploymentTelemetry.LargeMoveThreshold)
                .OrderByDescending(m => m.Distance)
                .Take(TopMoveLimit)
                .ToList();

            if (moves.Count == 0) yield break;

            var liveGroupsByKey = LiveGroupsByKey(battleUnits);

            foreach (var move in moves)
            {
                yield return "[TacDeployObsMove]" +
                             " surface=" + Safe(surface) +
                             " phase=" + before.Phase +
                             " alliance=" + move.After.Alliance +
                             " unitType=" + move.After.UnitType +
                             " name=" + move.After.Name +
                             " distance=" + FormatFloat(move.Distance) +
                             " from=" + FormatFloat(move.Before.X) + "," + FormatFloat(move.Before.Z) +
                             " to=" + FormatFloat(move.After.X) + "," + FormatFloat(move.After.Z) +
                             " formation=" + move.Before.Formation + "->" + move.After.Formation +
                             " orderedFormation=" + move.Before.FormationOrdered + "->" + move.After.FormationOrdered +
                             " paths=" + move.Before.PathCount + "->" + move.After.PathCount +
                             " routed=" + move.Before.Routed + "->" + move.After.Routed +
                             " active=" + move.Before.Active + "->" + move.After.Active;

                string terrainLine = TerrainEvidenceLine(surface, before.Phase, EnrichTerrainEvidence(move.After, liveGroupsByKey), move.Distance);
                if (terrainLine != null)
                    yield return terrainLine;
            }
        }

        private static Dictionary<string, BattleUnits.Grp> LiveGroupsByKey(BattleUnits battleUnits)
        {
            var groups = new Dictionary<string, BattleUnits.Grp>();
            foreach (BattleUnits.Grp group in ReadGroups(battleUnits))
            {
                if (group == null || (object)group.regref == null) continue;
                string key = group.regref.GetInstanceID().ToString(CultureInfo.InvariantCulture);
                if (!groups.ContainsKey(key))
                {
                    groups.Add(key, group);
                }
            }

            return groups;
        }

        private static TacticalDeploymentGroupSnapshot EnrichTerrainEvidence(
            TacticalDeploymentGroupSnapshot snapshot,
            IReadOnlyDictionary<string, BattleUnits.Grp> liveGroupsByKey)
        {
            if (snapshot == null) return null;
            if (liveGroupsByKey == null || !liveGroupsByKey.TryGetValue(snapshot.Key, out var group)) return snapshot;
            if (group == null || (object)group.regref == null) return snapshot;

            try
            {
                Regiment regiment = group.regref;
                Vector3 position = ((Component)regiment).transform.position;
                TacticalTerrainRuntimeSample centerSample = TacticalTerrainProbe.SampleCenter(regiment, null, position);
                IReadOnlyList<TacticalTerrainRuntimeSample> footprintSamples = TacticalTerrainProbe.SampleFootprint(regiment, null);
                bool footprintWater = footprintSamples.Any(sample => sample.Water);
                TacticalEnemyBearingEvidence enemy = TacticalTerrainProbe.GetVisibleEnemyBearing(regiment, position);

                return new TacticalDeploymentGroupSnapshot(
                    snapshot.Key,
                    snapshot.Name,
                    snapshot.Alliance,
                    snapshot.UnitType,
                    snapshot.X,
                    snapshot.Z,
                    snapshot.Formation,
                    snapshot.FormationOrdered,
                    snapshot.PathCount,
                    snapshot.Routed,
                    snapshot.Active,
                    centerSample.TerrainId,
                    centerSample.Water,
                    footprintWater,
                    centerSample.InDeploymentZone,
                    snapshot.Facing,
                    enemy.Visible ? enemy.BearingDegrees : 0f,
                    enemy.Visible ? enemy.DistanceMeters : 0f);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-deployment-observer:terrain-enrich",
                    "TacticalDeploymentObserverPatch terrain evidence probe failed: " + ex.GetType().Name);
                return snapshot;
            }
        }

        private static string TerrainEvidenceLine(
            string surface,
            string phase,
            TacticalDeploymentGroupSnapshot group,
            float moveDistance)
        {
            if (group == null) return null;
            if (!group.HasTerrainEvidence && !group.CenterWater && !group.FootprintWater) return null;

            var center = new TacticalTerrainSample(
                group.TerrainId,
                group.CenterWater,
                group.InsideDeploymentZone,
                group.TerrainId >= 0);
            var footprint = new TacticalTerrainSample(
                group.TerrainId,
                group.FootprintWater,
                group.InsideDeploymentZone,
                group.TerrainId >= 0);
            var candidate = new TacticalTerrainCandidate(
                new TacticalPoint2(group.X, group.Z),
                group.Facing,
                center,
                new[] { footprint });
            float facingDelta = group.HasVisibleEnemyBearing
                ? TacticalTerrainFacingDiscipline.AngleDelta(group.Facing, group.NearestVisibleEnemyBearing)
                : 0f;
            var decision = new TacticalTerrainDecision(
                false,
                TacticalTerrainDecisionReason.VanillaKept,
                candidate,
                moveDistance,
                facingDelta);

            return TacticalTerrainFacingTelemetry.Format(new TacticalTerrainFacingLogRow(
                surface,
                phase,
                group.Alliance,
                group.Name,
                group.TerrainId,
                group.CenterWater,
                group.FootprintWater,
                group.InsideDeploymentZone,
                group.Facing,
                group.NearestVisibleEnemyBearing,
                group.NearestVisibleEnemyDistance,
                decision));
        }

        private static BattleUnits.Grp[] ReadGroups(BattleUnits battleUnits)
        {
            if ((object)battleUnits == null) return Array.Empty<BattleUnits.Grp>();
            if (GrpField == null)
            {
                OnceLog.Warning("tactical-deployment-observer:missing-grp", "Missing BattleUnits.grp; tactical deployment observer snapshot disabled.");
                return Array.Empty<BattleUnits.Grp>();
            }

            try
            {
                return GrpField.GetValue(battleUnits) as BattleUnits.Grp[] ?? Array.Empty<BattleUnits.Grp>();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-observer:read-grp", "Failed to read BattleUnits.grp: " + ex.GetType().Name);
                return Array.Empty<BattleUnits.Grp>();
            }
        }

        private static void LogDeploymentTelemetry(string line)
        {
            TelemetryRouter.LegacyInfoToMainLogIfAllowed(line, TelemetryLayer.Tactical);
        }

        private static int ReadInt(BattleUnits battleUnits, FieldInfo field, string fieldName, string warningKey)
        {
            if ((object)battleUnits == null) return 0;
            if (field == null)
            {
                OnceLog.Warning("tactical-deployment-observer:missing-" + warningKey, "Missing BattleUnits." + fieldName + "; tactical deployment observer will use 0.");
                return 0;
            }

            try
            {
                object value = field.GetValue(battleUnits);
                return value is int intValue ? intValue : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-observer:read-" + warningKey, "Failed to read BattleUnits." + fieldName + ": " + ex.GetType().Name);
                return 0;
            }
        }

        private static bool IsDoPlacementSkipped(int forAlliance)
        {
            try
            {
                return (GameVars.playeralliance == forAlliance && !GameVars.ai_vs_ai) ||
                       (GameVars.tutorialactive && !Tutorial.engaged);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-observer:skip-detect", "Failed to evaluate DoPlacement skip guard: " + ex.GetType().Name);
                return false;
            }
        }

        private static BattleUnits GetBattleUnits(BattleUI battleUi)
        {
            if ((object)battleUi == null) return null;
            try
            {
                if (_battleUiBattleUnitsField == null)
                {
                    _battleUiBattleUnitsField = AccessTools.Field(typeof(BattleUI), "BU");
                    if (_battleUiBattleUnitsField == null)
                    {
                        OnceLog.Warning("tactical-deployment-observer:missing-bu", "TacticalDeploymentObserverPatch missing BattleUI.BU field; phase snapshots will be empty.");
                        return null;
                    }
                }

                return _battleUiBattleUnitsField.GetValue(battleUi) as BattleUnits;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-observer:bu-read", "TacticalDeploymentObserverPatch failed reading BattleUI.BU: " + ex.Message);
                return null;
            }
        }

        private static int SafeInt(Func<int> read)
        {
            try { return read(); }
            catch { return 0; }
        }

        private static bool SafeBool(Func<bool> read)
        {
            try { return read(); }
            catch { return false; }
        }

        private static float SafeFloat(Func<float> read)
        {
            try
            {
                float value = read();
                return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
            }
            catch
            {
                return 0f;
            }
        }

        private static string FormatFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.0";
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace(' ', '_');
        }
    }
}
