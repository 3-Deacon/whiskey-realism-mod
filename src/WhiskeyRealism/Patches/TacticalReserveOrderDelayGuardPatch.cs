using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #56 — Vanilla AIBattle.CheckUseOfReserves (6062) issues reserve support
    // movement with a direct RegimentSetPath at decompile line 6170 (the AI is
    // immune to order delay there; doublequick 6171). When order delays are
    // enabled, this Postfix removes the immediate path and re-issues the same
    // target through the battle-core inline executor
    // (OrderExecutorRuntime.TryPlanInline + ExecutePlannedInline, plan D4) as a ReserveCourierParity
    // Support Move with UseOrderDelay and DoubleQuick: BattleUnits.SetWaypoint
    // (91232) then queues the courier (AddToOrderQueue 91483-91510). Only units
    // SetWaypoint actually queues are converted (it forces the delay off without a
    // groupposition, 91449, or for unittyp 5 / a parentregiment, 91463;
    // BattleWriterPolicy.ReserveDelayConversionApplies).
    //
    // Nothing is removed until the re-issue is known to execute:
    // TacticalReserveCommitGate.PlanCourierParity screens first (a unit Whiskey may
    // not write, W&L player-owned or feud; one missing from the side's battle
    // snapshot; a higher-priority arbiter claim on its Move channel), then
    // OrderExecutorRuntime.TryPlanInline runs the executor's plan (chain,
    // WaypointValidator, silent-exit pre-check, dedupe, cooldown). Only a plan that
    // executes removes vanilla's path (RemoveVanillaPath) before
    // ExecutePlannedInline; any refusal keeps vanilla's own move untouched. Vanilla picks the reserve itself (PerformAIActionDLCWL 6129).
    //
    // No stance is written: the reserve is an attached sub-unit (ChangeStance
    // 90772 would set permanentlydetached).
    //
    // Ordering: #59 (BattleReserveCommitGatePatch) runs first (Priority.High). A
    // denied commit is rolled back to its pre-call path count, so it is not
    // converted here. The stalled FallBackToLine re-issue that used to live here
    // is the Withdrawal producer's (WithdrawalIntentProducer).
    [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
    internal static class TacticalReserveOrderDelayGuardPatch
    {
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
        internal static void Prefix(Regiment aigroup, out UnitState[] __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.reserve-order-delay-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.reserve-order-delay-prefix");
                __state = null;
                try
                {
                    if (!Enabled() || !SafeUseOrderDelays()) return;
                    if (!TacticalWriteGuard.CanWrite(aigroup)) return;
                    if (!BattleReserveCommitGatePatch.VanillaMayCommitReserve(aigroup)) return;
                    __state = Snapshot(aigroup);
                }
                catch (Exception ex)
                {
                    __state = null;
                    OnceLog.Warning(
                        "tactical-reserve-delay-guard:prefix",
                        "[TacticalReserveOrderDelayGuard] Prefix failed; vanilla reserve movement remains active: " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(Regiment aigroup, UnitState[] __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.reserve-order-delay", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.reserve-order-delay");
                try
                {
                    if (__state == null || __state.Length == 0 || aigroup == null) return;
                    if (!Enabled()) return;

                    BattleUnits battleUnits = BattleCoreTick.ResolveBattleUnits();
                    if (battleUnits == null) return;
                    OrderArbiter arbiter = OrderArbiter.ForAlliance(aigroup.alliance);
                    if (arbiter == null) return;
                    // The inline executor plans against the side's snapshot; without one
                    // (orchestrator off, before its first tick) vanilla's move stays.
                    if (!BattleCore.TryGetSnapshot(aigroup.alliance, out BattleSnapshot snapshot)) return;
                    bool useOrderDelays = SafeUseOrderDelays();

                    for (int i = 0; i < __state.Length; i++)
                        Convert(battleUnits, arbiter, snapshot, aigroup, __state[i], useOrderDelays);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "tactical-reserve-delay-guard:failed",
                        "[TacticalReserveOrderDelayGuard] failed; vanilla reserve movement remains active: " + ex.Message);
                }
            }
        }

        private static void Convert(BattleUnits battleUnits, OrderArbiter arbiter, BattleSnapshot snapshot, Regiment group, UnitState before, bool useOrderDelays)
        {
            Regiment unit = before.Unit;
            if (unit == null) return;

            int afterPaths = SafePathCount(unit);
            int afterQueue = SafeQueueCount(unit);
            // Vanilla's own target and facing, as RegimentSetPath stamped them
            // (131254-131259; the facing is GetAngle(target, endangered unit), 6168).
            Vector3 target = unit.lastsetwaypointposition;
            int unitId = TacticalPatchIds.GameObjectInstanceId(unit);
            BattleOrderIntent intent = TacticalReserveCommitGate.CourierParityIntent(
                unitId, target.x, target.z, unit.lastsetwaypointrotation);

            var action = TacticalReserveCommitGate.PlanCourierParity(
                arbiter,
                intent,
                unitWritable: TacticalWriteGuard.CanWrite(unit),
                unitInSnapshot: snapshot.IndexOfOwn(unitId) >= 0,
                conversionApplies: BattleWriterPolicy.ReserveDelayConversionApplies(
                    unit.groupposition != null, unit.unittyp, (UnityEngine.Object)unit.parentregiment != null),
                before.Paths,
                afterPaths,
                before.QueueCount,
                afterQueue,
                useOrderDelays);
            if (action != TacticalReserveCommitGate.CourierParityAction.Convert) return;

            // Plan the re-issue first (claim, guard, chain, validation, silent-exit
            // pre-check, dedupe, cooldown). Vanilla's path is removed only when the
            // delayed order will be issued; a refused plan leaves vanilla's commit.
            bool planned = OrderExecutorRuntime.TryPlanInline(battleUnits, unit, intent, out ExecutionPlan plan, out OrderOutcome outcome);
            if (!TacticalReserveCommitGate.RemoveVanillaPath(action, planned ? plan : default))
            {
                OnceLog.Info(
                    "tactical-reserve-delay-guard:refused",
                    "[TacticalReserveOrderDelayGuard] delayed re-issue refused, vanilla's reserve move kept reason=" + outcome.Reason);
                return;
            }

            BattleReserveCommitGatePatch.RemoveAddedPaths(unit, before.Paths, afterPaths);
            if (OrderExecutorRuntime.ExecutePlannedInline(battleUnits, unit, intent, plan, ref outcome))
            {
                OnceLog.Info(
                    "tactical-reserve-delay-guard:converted",
                    "[TacticalReserveOrderDelayGuard] converted direct reserve path to delayed order unit=" +
                    SafeUnitName(unit) +
                    " group=" + SafeUnitName(group) +
                    " paths=" + before.Paths + "->" + afterPaths +
                    " queue=" + before.QueueCount + "->" + SafeQueueCount(unit));
            }
            else
            {
                OnceLog.Warning(
                    "tactical-reserve-delay-guard:execute-failed",
                    "[TacticalReserveOrderDelayGuard] planned delayed re-issue did not reach SetWaypoint; the reserve move lapses");
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

        // Only the units CheckUseOfReserves 6121 could send.
        private static UnitState[] Snapshot(Regiment group)
        {
            Regiment[] attached = group != null ? group.allattachedunits : null;
            if (attached == null) return null;

            System.Collections.Generic.List<UnitState> units = null;
            for (int i = 0; i < attached.Length; i++)
            {
                Regiment unit = attached[i];
                if ((UnityEngine.Object)unit == null) continue;
                if (!BattleWriterPolicy.IsReserveSupportCandidate(
                        unit.unittyp, unit.regimentpaths, unit.isrouted, unit.outflanked, unit.inengagement))
                    continue;
                if (units == null) units = new System.Collections.Generic.List<UnitState>();
                units.Add(new UnitState(unit, SafePathCount(unit), SafeQueueCount(unit)));
            }

            return units?.ToArray();
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

        private static string SafeUnitName(Regiment unit)
        {
            if (unit == null) return "-";
            return TacticalCurrentOrderSignature.Safe(unit.name);
        }
    }
}
