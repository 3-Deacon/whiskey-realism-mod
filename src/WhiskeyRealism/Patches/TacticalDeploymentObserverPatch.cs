using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla places AI units through BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew
    // and toggles deployment through BattleUI.SetActiveDeploymentPhase. This observer
    // snapshots before/after state only; it does not write positions, orders, or formation.
    [HarmonyPatch]
    internal static class TacticalDeploymentObserverPatch
    {
        private const int TopMoveLimit = 8;
        private static FieldInfo _battleUiBattleUnitsField;

        [HarmonyPatch(typeof(BattleUnits), "DoPlacementAIUnitsWithinDeploymentzoneNew")]
        [HarmonyPrefix]
        internal static void DoPlacementPrefix(BattleUnits __instance, int foralliance, ref TacticalDeploymentSnapshot __state)
        {
            if (!Enabled()) return;
            __state = Capture(__instance, "pre-placement", foralliance);
        }

        [HarmonyPatch(typeof(BattleUnits), "DoPlacementAIUnitsWithinDeploymentzoneNew")]
        [HarmonyPostfix]
        internal static void DoPlacementPostfix(BattleUnits __instance, int foralliance, TacticalDeploymentSnapshot __state)
        {
            if (!Enabled()) return;
            try
            {
                OnceLog.Info("tactical-deployment-observer", "TacticalDeploymentObserverPatch wired.");
                var after = Capture(__instance, "post-placement", foralliance);
                EmitDelta("DoPlacementAIUnitsWithinDeploymentzoneNew", __state, after);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-deployment-observer:placement", "TacticalDeploymentObserverPatch placement observer failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(BattleUI), "SetActiveDeploymentPhase")]
        [HarmonyPrefix]
        internal static void SetActiveDeploymentPhasePrefix(
            BattleUI __instance,
            bool active,
            bool showsupplybydefault,
            bool calledfromsave,
            ref TacticalDeploymentSnapshot __state)
        {
            if (!Enabled()) return;
            BattleUnits battleUnits = GetBattleUnits(__instance);
            __state = Capture(battleUnits, active ? "pre-open" : "pre-close", -1);
        }

        [HarmonyPatch(typeof(BattleUI), "SetActiveDeploymentPhase")]
        [HarmonyPostfix]
        internal static void SetActiveDeploymentPhasePostfix(
            BattleUI __instance,
            bool active,
            bool showsupplybydefault,
            bool calledfromsave,
            TacticalDeploymentSnapshot __state)
        {
            if (!Enabled()) return;
            try
            {
                BattleUnits battleUnits = GetBattleUnits(__instance);
                var after = Capture(battleUnits, active ? "post-open" : "post-close", -1);
                string action = active ? "open" : "close";
                Plugin.Log.LogInfo("[TacticalDeploymentPhase] action=" + action +
                                   " calledFromSave=" + calledfromsave +
                                   " eod=" + after.EodCycle +
                                   " days=" + after.BattlePassedDays +
                                   " groups=" + after.Groups.Count);
                EmitDelta("SetActiveDeploymentPhase:" + action, __state, after);
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

        private static TacticalDeploymentSnapshot Capture(BattleUnits battleUnits, string label, int alliance)
        {
            if ((object)battleUnits == null)
            {
                return TacticalDeploymentSnapshot.Empty(label, alliance, 0, 0);
            }

            var groups = new List<TacticalDeploymentGroupSnapshot>();
            BattleUnits.Grp[] battleGroups = battleUnits.grp;
            if (battleGroups != null)
            {
                for (int i = 0; i < battleGroups.Length; i++)
                {
                    BattleUnits.Grp group = battleGroups[i];
                    if (group == null || (object)group.regref == null) continue;
                    Regiment regiment = group.regref;
                    if (alliance >= 0 && regiment.alliance != alliance) continue;
                    groups.Add(SnapshotGroup(regiment, group, i));
                }
            }

            return new TacticalDeploymentSnapshot(label, alliance, battleUnits.eodcycle, battleUnits.battlepasseddays, groups);
        }

        private static TacticalDeploymentGroupSnapshot SnapshotGroup(Regiment regiment, BattleUnits.Grp group, int index)
        {
            Vector3 position = ((Component)regiment).transform.position;
            string name = !string.IsNullOrEmpty(group.name) ? group.name : ((UnityEngine.Object)regiment).name;
            string key = regiment.GetInstanceID().ToString();
            int formation = SafeInt(() => regiment.formation);
            int formationOrdered = SafeInt(() => regiment.formationordered);
            int pathCount = SafeInt(() => regiment.regimentpaths);
            bool routed = SafeBool(() => regiment.isrouted);
            bool active = SafeBool(() => ((Component)regiment).gameObject.activeInHierarchy);

            return new TacticalDeploymentGroupSnapshot(
                key + ":" + index,
                name,
                regiment.alliance,
                regiment.unittyp,
                position.x,
                position.z,
                formation,
                formationOrdered,
                pathCount,
                routed,
                active);
        }

        private static void EmitDelta(string surface, TacticalDeploymentSnapshot before, TacticalDeploymentSnapshot after)
        {
            var delta = TacticalDeploymentTelemetry.Delta(surface, before, after);
            Plugin.Log.LogInfo(TacticalDeploymentTelemetry.FormatSummary(delta));
            foreach (string line in TopMoveLines(surface, before, after))
            {
                Plugin.Log.LogInfo(line);
            }
        }

        private static IEnumerable<string> TopMoveLines(string surface, TacticalDeploymentSnapshot before, TacticalDeploymentSnapshot after)
        {
            if (before == null || after == null) yield break;

            var beforeByKey = before.Groups.GroupBy(g => g.Key).ToDictionary(g => g.Key, g => g.First());
            var moves = after.Groups
                .Where(g => beforeByKey.ContainsKey(g.Key))
                .Select(g => new { Before = beforeByKey[g.Key], After = g, Distance = beforeByKey[g.Key].DistanceTo(g) })
                .Where(m => m.Distance >= TacticalDeploymentTelemetry.LargeMoveThreshold)
                .OrderByDescending(m => m.Distance)
                .Take(TopMoveLimit);

            foreach (var move in moves)
            {
                yield return "[TacticalDeploymentMove]" +
                             " surface=" + surface.Replace(' ', '_') +
                             " phase=" + after.Phase +
                             " alliance=" + move.After.Alliance +
                             " unitType=" + move.After.UnitType +
                             " name=" + move.After.Name +
                             " distance=" + move.Distance.ToString("0.0") +
                             " from=" + move.Before.X.ToString("0.0") + "," + move.Before.Z.ToString("0.0") +
                             " to=" + move.After.X.ToString("0.0") + "," + move.After.Z.ToString("0.0") +
                             " formation=" + move.Before.Formation + "->" + move.After.Formation +
                             " orderedFormation=" + move.Before.FormationOrdered + "->" + move.After.FormationOrdered +
                             " paths=" + move.Before.PathCount + "->" + move.After.PathCount +
                             " routed=" + move.Before.Routed + "->" + move.After.Routed +
                             " active=" + move.Before.Active + "->" + move.After.Active;
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
    }
}
