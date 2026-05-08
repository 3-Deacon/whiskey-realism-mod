using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckUseOfReserves issues reserve support movement with a
    // direct RegimentSetPath at decompile line 6170. When order delays are
    // enabled, this Postfix removes the immediate path and reissues the same
    // target through BattleUnits.SetWaypoint(useorderdelay: true).
    [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
    internal static class TacticalReserveOrderDelayGuardPatch
    {
        private static FieldInfo _bunitsField;

        internal sealed class ReserveState
        {
            public UnitState[] Units;
        }

        internal readonly struct UnitState
        {
            public UnitState(Regiment unit, int paths, int queueCount)
            {
                Unit = unit;
                Paths = paths;
                QueueCount = queueCount;
            }

            public Regiment Unit { get; }
            public int Paths { get; }
            public int QueueCount { get; }
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment aigroup, out ReserveState __state)
        {
            if (!Enabled())
            {
                __state = null;
                return;
            }

            __state = Snapshot(aigroup);
        }

        [HarmonyPostfix]
        internal static void Postfix(AIBattle __instance, Regiment aigroup, ReserveState __state)
        {
            if (!Enabled()) return;

            try
            {
                if (__state == null || __state.Units == null) return;
                if (!SafeUseOrderDelays()) return;

                BattleUnits battleUnits = ResolveBattleUnits(__instance);
                if (battleUnits == null) return;

                for (int i = 0; i < __state.Units.Length; i++)
                {
                    UnitState before = __state.Units[i];
                    Regiment unit = before.Unit;
                    if (unit == null) continue;

                    int afterPaths = SafePathCount(unit);
                    int afterQueue = SafeQueueCount(unit);
                    if (afterPaths <= before.Paths) continue;
                    if (afterQueue > before.QueueCount) continue;

                    Vector3 target = unit.lastsetwaypointposition;
                    float rotation = SafeTargetAngle(unit, target);
                    RemoveAddedPaths(unit, before.Paths, afterPaths);
                    battleUnits.SetWaypoint(
                        unit,
                        target,
                        newpath: true,
                        doublequick: true,
                        manualfinalrotation: rotation,
                        modifylastwaypoint: false,
                        useorderdelay: true,
                        timetomove: -1f,
                        direction: -1,
                        showmovementoptions: false,
                        ignorebattlemonuments: false,
                        groupmoveonly: false,
                        ignoredisabledships: false,
                        checkforreadiness: true,
                        clearinterruptionpaths: true);
                    unit.doublequick = true;

                    OnceLog.Info(
                        "tactical-reserve-delay-guard:" + SafeUnitName(unit),
                        "[TacticalReserveOrderDelayGuard] converted direct reserve path to delayed order unit=" +
                        SafeUnitName(unit) +
                        " group=" + SafeUnitName(aigroup) +
                        " paths=" + before.Paths + "->" + afterPaths +
                        " queue=" + before.QueueCount + "->" + SafeQueueCount(unit));
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-delay-guard:failed",
                    "[TacticalReserveOrderDelayGuard] failed; vanilla reserve movement remains active: " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalReserveOrderDelayGuard != null &&
                Plugin.Instance.EnableTacticalReserveOrderDelayGuard.Value;
        }

        private static ReserveState Snapshot(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null)
                    return new ReserveState { Units = Array.Empty<UnitState>() };

                UnitState[] units = new UnitState[group.allattachedunits.Length];
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    units[i] = new UnitState(unit, SafePathCount(unit), SafeQueueCount(unit));
                }

                return new ReserveState { Units = units };
            }
            catch
            {
                return new ReserveState { Units = Array.Empty<UnitState>() };
            }
        }

        private static BattleUnits ResolveBattleUnits(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;
                if (_bunitsField == null)
                    _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                if (_bunitsField == null)
                {
                    OnceLog.Warning(
                        "tactical-reserve-delay-guard:missing-bunits",
                        "[TacticalReserveOrderDelayGuard] missing AIBattle.bunits anchor.");
                    return null;
                }

                return _bunitsField.GetValue(battle) as BattleUnits;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-delay-guard:bunits",
                    "[TacticalReserveOrderDelayGuard] failed reading AIBattle.bunits: " + ex.Message);
                return null;
            }
        }

        private static void RemoveAddedPaths(Regiment unit, int before, int after)
        {
            int safeBefore = Math.Max(0, before);
            int safeAfter = Math.Max(safeBefore, after);
            if (unit.regimentpath != null)
            {
                int max = Math.Min(safeAfter, unit.regimentpath.Length);
                for (int i = safeBefore; i < max; i++)
                    unit.regimentpath[i] = new NavMeshPath();
            }

            if (unit.pathstatus != null)
            {
                int max = Math.Min(safeAfter, unit.pathstatus.Length);
                for (int i = safeBefore; i < max; i++)
                    unit.pathstatus[i] = 0;
            }

            unit.regimentpaths = safeBefore;
        }

        private static int SafePathCount(Regiment unit)
        {
            try { return unit != null ? Math.Max(0, unit.regimentpaths) : 0; }
            catch { return 0; }
        }

        private static int SafeQueueCount(Regiment unit)
        {
            try { return unit != null && unit.orderqueue != null ? unit.orderqueue.Count : 0; }
            catch { return 0; }
        }

        private static bool SafeUseOrderDelays()
        {
            try { return GameVars.useorderdelays; }
            catch { return false; }
        }

        private static float SafeTargetAngle(Regiment unit, Vector3 target)
        {
            try
            {
                Vector3 position = ((Component)unit).transform.position;
                return Tools.GetAngle(target, position);
            }
            catch
            {
                return -1f;
            }
        }

        private static string SafeUnitName(Regiment unit)
        {
            if (unit == null) return "-";
            return TacticalCurrentOrderSignature.Safe(unit.name);
        }
    }
}
