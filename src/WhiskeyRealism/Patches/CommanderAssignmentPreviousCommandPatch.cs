using System;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
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
            using (TelemetryPerf.Scope("campaign.patch.commander-prev-command.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                __state = default;
                try
                {
                    if (!Enabled() || __instance == null) return;
                    int commanderId = GameVars.commander != null ? GameVars.commander.IndexOf(__instance) : -1;
                    __state = new State(
                        __instance.currentcommand,
                        reg,
                        commanderId,
                        IsCalledFromReplaceCommanderOfUnit());
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("commander-previous-command:prefix", "[Patch:CommanderAssignment] prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(State __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.commander-prev-command.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
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
                            assignedCommanderId: __state.CommanderId,
                            vanillaReplacementWillReadPriorCommander: __state.VanillaReplacementWillReadPriorCommander))
                    {
                        if (__state.VanillaReplacementWillReadPriorCommander)
                        {
                            OnceLog.Info(
                                "commander-previous-command:skip-vanilla-replacement",
                                "[Patch:CommanderAssignment] skipped stale previous-command clear during vanilla ReplaceCommanderOfUnit");
                        }
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
        }

        private static bool Enabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.Enabled.Value; }
            catch { return false; }
        }

        private static bool IsCalledFromReplaceCommanderOfUnit()
        {
            try
            {
                var trace = new StackTrace(1, false);
                var frames = trace.GetFrames();
                if (frames == null) return false;

                for (int i = 0; i < frames.Length; i++)
                {
                    var method = frames[i]?.GetMethod();
                    if (method == null) continue;
                    if (method.Name == "ReplaceCommanderOfUnit" && method.DeclaringType != null && method.DeclaringType.Name == "BattleUnits")
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        internal readonly struct State
        {
            internal readonly Regiment PriorCommand;
            internal readonly Regiment TargetCommand;
            internal readonly int CommanderId;
            internal readonly bool VanillaReplacementWillReadPriorCommander;

            internal State(
                Regiment priorCommand,
                Regiment targetCommand,
                int commanderId,
                bool vanillaReplacementWillReadPriorCommander)
            {
                PriorCommand = priorCommand;
                TargetCommand = targetCommand;
                CommanderId = commanderId;
                VanillaReplacementWillReadPriorCommander = vanillaReplacementWillReadPriorCommander;
            }

            internal bool Valid => CommanderId >= 0;
        }
    }
}
