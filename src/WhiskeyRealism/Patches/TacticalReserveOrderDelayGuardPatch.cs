using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
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
            public bool HasDoctrineDecision;
            public DoctrineReserveDecision DoctrineDecision;
            public CommandDoctrineOrder DoctrineOrder;
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
            using (TelemetryPerf.Scope("tactical.patch.reserve-order-delay-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (!Enabled())
                {
                    __state = null;
                    return;
                }

                __state = Snapshot(aigroup);
                if (TryResolveDoctrineOrder(aigroup, out CommandDoctrineOrder order))
                {
                    __state.HasDoctrineDecision = true;
                    __state.DoctrineOrder = order;
                    __state.DoctrineDecision = DoctrineConsumerDecisions.DecideReserve(
                        order,
                        BuildOperationalReserveInput(aigroup),
                        SafeCurrentTimeSeconds());
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(AIBattle __instance, Regiment aigroup, ReserveState __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.reserve-order-delay", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
            if (!Enabled()) return;

            try
            {
                if (__state == null || __state.Units == null) return;

                bool doctrineDeny = __state.HasDoctrineDecision &&
                    __state.DoctrineDecision.Action == DoctrineConsumerAction.Deny;
                bool doctrineFallback = __state.HasDoctrineDecision &&
                    __state.DoctrineDecision.Action == DoctrineConsumerAction.Allow &&
                    __state.DoctrineDecision.Task == CommandTaskType.FallBackToLine;
                bool useOrderDelays = SafeUseOrderDelays();

                if (doctrineDeny) return;
                if (!useOrderDelays) return;

                BattleUnits battleUnits = ResolveBattleUnits(__instance);
                if (battleUnits == null) return;

                for (int i = 0; i < __state.Units.Length; i++)
                {
                    UnitState before = __state.Units[i];
                    Regiment unit = before.Unit;
                    if (unit == null) continue;

                    int afterPaths = SafePathCount(unit);
                    int afterQueue = SafeQueueCount(unit);
                    if (!TacticalReservePolicyLedger.ShouldDelayGuardConsumeReserveMovement(
                        doctrineDeny,
                        before.Paths,
                        afterPaths,
                        before.QueueCount,
                        afterQueue,
                        useOrderDelays))
                    {
                        continue;
                    }

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

                if (doctrineFallback)
                    TryClearStalledFallbackOrder(battleUnits, aigroup, __state.DoctrineOrder);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-delay-guard:failed",
                    "[TacticalReserveOrderDelayGuard] failed; vanilla reserve movement remains active: " + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        private static void TryClearStalledFallbackOrder(
            BattleUnits battleUnits,
            Regiment group,
            CommandDoctrineOrder order)
        {
            try
            {
                if (battleUnits == null || group == null) return;
                if (!group.pathinterrupted || HasActiveMoveOrder(group)) return;
                if (!order.FallbackTarget.HasValue) return;

                Vector3 target = new Vector3(order.FallbackTarget.X, 0f, order.FallbackTarget.Z);
                if (!IsSafeTarget(group, target)) return;

                battleUnits.SetWaypoint(
                    group,
                    target,
                    newpath: true,
                    doublequick: false,
                    manualfinalrotation: -1f,
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

                OnceLog.Info(
                    "tactical-reserve-delay-guard:fallback-clear:" + SafeUnitName(group),
                    "[TacticalReserveOrderDelayGuard] cleared stalled fallback order reason=fallback-relief group=" +
                    SafeUnitName(group));
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-delay-guard:fallback-clear-failed",
                    "[TacticalReserveOrderDelayGuard] fallback clear skipped: " + ex.Message);
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

        private static bool TryResolveDoctrineOrder(Regiment group, out CommandDoctrineOrder order)
        {
            order = default(CommandDoctrineOrder);

            try
            {
                if (group == null) return false;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                ArmyOrchestrator army = side?.Army;
                var orders = army?.CurrentDoctrineOrders;
                if (orders == null || orders.Count == 0) return false;

                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                for (int i = 0; i < orders.Count; i++)
                {
                    CommandDoctrineOrder candidate = orders[i];
                    if (!TacticalPatchIds.NodeIdMatches(candidate.NodeId, gameObjectInstanceId, componentInstanceId))
                        continue;
                    if (!candidate.HasPurpose) return false;

                    order = candidate;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static float LocalOdds(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                float own = Math.Max(Sanitize(group.groupowninrange), Sanitize(group.groupstrengthaigroup));
                float enemy = Math.Max(Sanitize(group.groupenemiesinrange), SumEnemyStrengthWithinAngle(group));
                if (enemy <= 0f) return own > 0f ? 2f : 1f;
                return own / Math.Max(1f, enemy);
            }
            catch
            {
                return 1f;
            }
        }

        private static float ReserveFraction(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null || group.allattachedunits.Length == 0)
                    return 0f;

                float total = 0f;
                float reserve = 0f;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit == null) continue;
                    if (unit.isrouted || unit.markedforrout || unit.permanentlydetached) continue;
                    if (unit.unittyp > TacticalUnitType.Cavalry) continue;

                    float strength = Sanitize(unit.groupstrengthactive);
                    total += strength;
                    if (unit.regimentpaths <= 0 && !unit.inengagement)
                        reserve += strength;
                }

                if (total <= 0f) return 0f;
                return reserve / total;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SumEnemyStrengthWithinAngle(Regiment group)
        {
            try
            {
                if (group == null ||
                    group.unitrange == null ||
                    group.unitrange.enemystrengthwithinangle == null ||
                    !TacticalFogOfWarContact.HasVisibleEnemy(group))
                    return 0f;

                float total = 0f;
                for (int i = 0; i < group.unitrange.enemystrengthwithinangle.Length; i++)
                    total += Math.Max(0f, group.unitrange.enemystrengthwithinangle[i]);
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static OperationalReserveInput BuildOperationalReserveInput(Regiment group)
        {
            float odds = LocalOdds(group);
            float reserveFraction = ReserveFraction(group);
            float flankThreat = FlankThreat(group, odds);
            float endurance = ReserveEndurance(group);

            return new OperationalReserveInput(
                reserveFraction,
                odds,
                flankThreat,
                endurance,
                assaultAuthorized: odds >= 1.60f && flankThreat < 0.70f && endurance >= 0.35f,
                fallbackPressure: odds < 0.75f || flankThreat >= 0.85f);
        }

        private static float FlankThreat(Regiment group, float odds)
        {
            try
            {
                if (group == null) return 0f;
                float threat = 0f;
                if (group.outflanked > 0) threat = Math.Max(threat, 0.80f);
                if (group.friendlyroutednear > 0f) threat = Math.Max(threat, 0.65f);

                float enemy = Math.Max(Sanitize(group.groupenemiesinrange), SumEnemyStrengthWithinAngle(group));
                float own = Math.Max(1f, Math.Max(Sanitize(group.groupowninrange), Sanitize(group.groupstrengthaigroup)));
                if (enemy > own) threat = Math.Max(threat, Math.Min(1f, enemy / Math.Max(1f, own * 1.5f)));
                if (odds < 0.70f) threat = Math.Max(threat, 0.70f);
                return Clamp01(threat);
            }
            catch
            {
                return 0f;
            }
        }

        private static float ReserveEndurance(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null || group.allattachedunits.Length == 0)
                    return 0f;

                float total = 0f;
                int count = 0;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit == null) continue;
                    if (unit.isrouted || unit.markedforrout || unit.permanentlydetached) continue;
                    if (unit.unittyp > TacticalUnitType.Cavalry) continue;
                    if (unit.regimentpaths > 0 || unit.inengagement) continue;

                    total += 0.40f * AverageAmmoRatio(unit) +
                        0.30f * (1f - Clamp01(unit.fatigue)) +
                        0.30f * MoraleRatio(unit);
                    count++;
                }

                return count > 0 ? Clamp01(total / count) : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static float AverageAmmoRatio(Regiment unit)
        {
            try
            {
                if (unit == null || unit.ammo == null || unit.ammo.Length == 0) return 0f;
                float total = 0f;
                for (int i = 0; i < unit.ammo.Length; i++) total += Math.Max(0f, unit.ammo[i]);
                return Clamp01(total / unit.ammo.Length);
            }
            catch
            {
                return 0f;
            }
        }

        private static float MoraleRatio(Regiment unit)
        {
            try
            {
                if (unit == null) return 0f;
                if (unit.battlestartmorale > 0f)
                    return Clamp01(unit.morale / Math.Max(0.01f, unit.battlestartmorale));
                return Clamp01(unit.morale);
            }
            catch
            {
                return 0f;
            }
        }

        private static bool HasActiveMoveOrder(Regiment unit)
        {
            try
            {
                return unit != null &&
                    unit.ordertypeactive != null &&
                    unit.ordertypeactive.Length > 1 &&
                    (unit.ordertypeactive[0] || unit.ordertypeactive[1]);
            }
            catch
            {
                return true;
            }
        }

        private static bool IsSafeTarget(Regiment group, Vector3 target)
        {
            try
            {
                if (!IsFinite(target.x) || !IsFinite(target.z)) return false;
                Vector3 position = ((Component)group).transform.position;
                if (!IsFinite(position.x) || !IsFinite(position.z)) return false;
                return Vector3.Distance(position, target) > 5f;
            }
            catch
            {
                return false;
            }
        }

        private static float SafeCurrentTimeSeconds()
        {
            try { return Time.realtimeSinceStartup; }
            catch { return 0f; }
        }

        private static float Sanitize(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value;
        }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
