using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckLineFallbacks(Regiment) at decompile line 5118 writes
    // fallback paths/movement modes for line units in morale danger. This Postfix
    // observes only - no writes - and emits a first-fire marker so smoke can verify
    // the patch loaded.
    [HarmonyPatch(typeof(AIBattle), "CheckLineFallbacks")]
    public static class B8CheckLineFallbacksObserverPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Regiment aigroup)
        {
            try
            {
                OnceLog.Info("b8-check-line-fallbacks", "");
                ReportPlannedFallbackContext(aigroup);
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b8-check-line-fallbacks-error",
                    "[B8] CheckLineFallbacks observer error: " + ex.Message);
            }
        }

        private static void ReportPlannedFallbackContext(Regiment group)
        {
            if (!TryResolveLedgerState(group, out CommandNodeOperationalState state, out OperationRecord operation))
                return;
            if (state.Task != CommandTaskType.FallBackToLine && state.Role != CommandNodeRole.FallbackGuard)
                return;

            OnceLog.Info(
                "b8-check-line-fallbacks-task:" + SafeInstanceId(group) + ":" + state.Task + ":" + operation.Phase,
                "[B8] plannedFallback group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " task=" + state.Task +
                " taskState=" + state.TaskState +
                " role=" + state.Role +
                " operation=" + operation.Shape +
                " opPhase=" + operation.Phase);
        }

        private static bool TryResolveLedgerState(
            Regiment group,
            out CommandNodeOperationalState state,
            out OperationRecord operation)
        {
            state = default;
            operation = default;
            try
            {
                if (group == null) return false;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                ArmyOrchestrator army = side?.Army;
                var operations = army?.CurrentCommandOperations;
                if (operations == null || operations.Count == 0) return false;

                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                for (int i = 0; i < operations.Count; i++)
                {
                    if (!TacticalPatchIds.NodeIdMatches(operations[i].NodeId, gameObjectInstanceId, componentInstanceId)) continue;
                    state = operations[i];
                    operation = army.CurrentOperation;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static int SafeInstanceId(Regiment group)
        {
            try { return group != null ? group.GetInstanceID() : 0; }
            catch { return 0; }
        }

        private static string SafeName(Regiment group)
        {
            try { return group != null ? TacticalCurrentOrderSignature.Safe(group.name) : "-"; }
            catch { return "-"; }
        }
    }
}
