using System;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Commander.AssignCommando (decompile 60343) clears the old commander
    // from the target unit, but leaves this commander's previous unit still
    // pointing at him. This Postfix repairs that stale previous unit.
    //
    // 2026-10-14 (C-CmdrReplace): the repair used to set prior.commander = -1,
    // leaving a commanderless unit that vanilla indexes as GameVars.commander[-1].
    // It now refills the vacated command the way vanilla does
    // (BattleUnits.ReplaceCommanderOfUnit, 81389, the same call
    // ArmyGroup.AppointCommander makes first at 75575). ReplaceCommanderOfUnit
    // reads the moved commander's id from the prior unit, sees his currentcommand
    // is elsewhere, and assigns a subordinate/new commander without removing him
    // from his new command. It re-enters AssignCommando; a thread-static guard
    // keeps this Postfix from recursing.
    [HarmonyPatch(typeof(GameVars.Commander), "AssignCommando")]
    internal static class CommanderAssignmentPreviousCommandPatch
    {
        [ThreadStatic] private static bool _replacing;

        [HarmonyPrefix]
        internal static void Prefix(GameVars.Commander __instance, Regiment reg, ref State __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.commander-prev-command.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.commander-prev-command.prefix");
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
                PatchFirstFire.Mark("campaign.patch.commander-prev-command.postfix");
                try
                {
                    if (!__state.Valid) return;
                    var prior = __state.PriorCommand;
                    if ((UnityEngine.Object)(object)prior == (UnityEngine.Object)null) return;

                    bool priorIsTarget = (UnityEngine.Object)(object)prior == (UnityEngine.Object)(object)__state.TargetCommand;
                    bool stale = CommanderAssignmentGuard.ShouldClearPreviousCommand(
                        priorCommandExists: true,
                        priorIsAssignedTarget: priorIsTarget,
                        priorCommandCommanderId: prior.commander,
                        assignedCommanderId: __state.CommanderId,
                        vanillaReplacementWillReadPriorCommander: __state.VanillaReplacementWillReadPriorCommander);
                    if (!stale && __state.VanillaReplacementWillReadPriorCommander)
                    {
                        OnceLog.Info(
                            "commander-previous-command:skip-vanilla-replacement",
                            "[Patch:CommanderAssignment] skipped stale previous-command repair during vanilla ReplaceCommanderOfUnit");
                    }

                    if (CommanderAssignmentGuard.ResolvePreviousCommandAction(stale, _replacing) != PreviousCommandAction.ReplaceViaVanilla)
                        return;

                    _replacing = true;
                    try
                    {
                        BattleUnits.ReplaceCommanderOfUnit(prior);
                    }
                    finally
                    {
                        _replacing = false;
                    }

                    OnceLog.Info(
                        "commander-previous-command",
                        "[Patch:CommanderAssignment] refilled stale previous command via BattleUnits.ReplaceCommanderOfUnit");
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
