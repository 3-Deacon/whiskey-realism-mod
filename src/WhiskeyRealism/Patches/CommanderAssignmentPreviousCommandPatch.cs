using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Commander.AssignCommando clears the old commander from the target
    // unit, but does not clear this commander's previous unit before assigning
    // the new command. This Postfix clears that stale previous unit reference.
    [HarmonyPatch(typeof(GameVars.Commander), "AssignCommando")]
    internal static class CommanderAssignmentPreviousCommandPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(GameVars.Commander __instance, Regiment reg, ref State __state)
        {
            __state = default;
            try
            {
                if (!Enabled() || __instance == null) return;
                int commanderId = GameVars.commander != null ? GameVars.commander.IndexOf(__instance) : -1;
                __state = new State(__instance.currentcommand, reg, commanderId);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("commander-previous-command:prefix", "[Patch:CommanderAssignment] prefix failed: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(State __state)
        {
            try
            {
                if (!__state.Valid) return;
                var prior = __state.PriorCommand;
                if ((UnityEngine.Object)(object)prior == (UnityEngine.Object)null) return;

                bool priorIsTarget = (UnityEngine.Object)(object)prior == (UnityEngine.Object)(object)__state.TargetCommand;
                if (!CommanderAssignmentGuard.ShouldClearPreviousCommand(
                        priorCommandExists: true,
                        priorIsAssignedTarget: priorIsTarget,
                        priorCommandCommanderId: prior.commander,
                        assignedCommanderId: __state.CommanderId))
                {
                    return;
                }

                prior.commander = -1;
                try { prior.UpdateCommanderPicture(); }
                catch { }

                OnceLog.Info(
                    "commander-previous-command",
                    "[Patch:CommanderAssignment] cleared stale previous command after AssignCommando");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("commander-previous-command:postfix", "[Patch:CommanderAssignment] postfix failed: " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.Enabled.Value; }
            catch { return false; }
        }

        internal readonly struct State
        {
            internal readonly Regiment PriorCommand;
            internal readonly Regiment TargetCommand;
            internal readonly int CommanderId;

            internal State(Regiment priorCommand, Regiment targetCommand, int commanderId)
            {
                PriorCommand = priorCommand;
                TargetCommand = targetCommand;
                CommanderId = commanderId;
            }

            internal bool Valid => CommanderId >= 0;
        }
    }
}
