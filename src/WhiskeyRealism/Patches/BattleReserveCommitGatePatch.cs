using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine.AI;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckUseOfReserves directly issues RegimentSetPath for a
    // reserve support unit at decompile line 6170. This patch snapshots
    // attached-unit path state before vanilla, then rolls back only newly-added
    // paths when the command-node role says the calling command group should
    // remain in Reserve.
    [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
    internal static class BattleReserveCommitGatePatch
    {
        internal sealed class ReserveCommitState
        {
            public UnitState[] Units = Array.Empty<UnitState>();
        }

        internal readonly struct UnitState
        {
            public UnitState(Regiment unit, int paths, int queueCount, bool doubleQuick, bool inEngagement)
            {
                Unit = unit;
                Paths = Math.Max(0, paths);
                QueueCount = Math.Max(0, queueCount);
                DoubleQuick = doubleQuick;
                InEngagement = inEngagement;
            }

            public Regiment Unit { get; }
            public int Paths { get; }
            public int QueueCount { get; }
            public bool DoubleQuick { get; }
            public bool InEngagement { get; }
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment aigroup, out ReserveCommitState __state)
        {
            __state = null;
            try
            {
                if (Enabled())
                    __state = Snapshot(aigroup);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:prefix",
                    "[TacticalReserveCommitGate] Prefix failed; vanilla reserve movement remains active: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        internal static void Postfix(Regiment aigroup, ReserveCommitState __state)
        {
            try
            {
                if (!Enabled()) return;
                if (__state == null || __state.Units == null || aigroup == null) return;

                UnitState[] changed = FindChangedUnits(__state);
                if (changed.Length == 0)
                {
                    Log(aigroup, TacticalReserveCommitGate.Action.Observe, DirectChildRole.Unknown, "no-vanilla-commit", 0);
                    return;
                }

                CommandIntentResolution resolution = ResolveIntent(aigroup);
                var input = new TacticalReserveCommitGate.Input(
                    vanillaCommitted: true,
                    resolution: resolution,
                    playerControlled: HasPlayerOwnership(aigroup),
                    committedUnitAlreadyEngaged: AnyAlreadyEngaged(changed),
                    ownStrengthRatio: OwnStrengthRatio(aigroup),
                    localOdds: LocalOdds(aigroup));

                TacticalReserveCommitGate.Decision decision = TacticalReserveCommitGate.Decide(input);
                if (decision.Action == TacticalReserveCommitGate.Action.Deny)
                    RollBackChangedUnits(changed);

                Log(aigroup, decision.Action, decision.Role, decision.Reason, changed.Length);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:postfix",
                    "[TacticalReserveCommitGate] Postfix failed; vanilla reserve movement remains active: " + ex.Message);
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null
                    && Plugin.Instance.Enabled != null
                    && Plugin.Instance.Enabled.Value
                    && Plugin.EnableTacticalBattleOrchestrator != null
                    && Plugin.EnableTacticalBattleOrchestrator.Value
                    && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate != null
                    && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate.Value;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:enabled",
                    "[TacticalReserveCommitGate] Enable check failed; patch disabled: " + ex.Message);
                return false;
            }
        }

        private static ReserveCommitState Snapshot(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null)
                    return new ReserveCommitState();

                UnitState[] units = new UnitState[group.allattachedunits.Length];
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    units[i] = new UnitState(
                        unit,
                        SafePathCount(unit),
                        SafeQueueCount(unit),
                        SafeDoubleQuick(unit),
                        SafeInEngagement(unit));
                }

                return new ReserveCommitState { Units = units };
            }
            catch
            {
                return new ReserveCommitState();
            }
        }

        private static UnitState[] FindChangedUnits(ReserveCommitState state)
        {
            var changed = new List<UnitState>();
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState before = state.Units[i];
                Regiment unit = before.Unit;
                if (unit == null) continue;

                int afterPaths = SafePathCount(unit);
                int afterQueue = SafeQueueCount(unit);
                if (afterPaths > before.Paths && afterQueue <= before.QueueCount)
                    changed.Add(before);
            }

            return changed.ToArray();
        }

        private static void RollBackChangedUnits(UnitState[] changed)
        {
            for (int i = 0; i < changed.Length; i++)
            {
                UnitState before = changed[i];
                Regiment unit = before.Unit;
                if (unit == null) continue;

                RemoveAddedPaths(unit, before.Paths, SafePathCount(unit));
                unit.doublequick = before.DoubleQuick;
            }
        }

        private static CommandIntentResolution ResolveIntent(Regiment group)
        {
            try
            {
                if (group == null)
                    return new CommandIntentResolution(false, default, "no-group");

                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                if (side == null || side.Army == null)
                    return new CommandIntentResolution(false, default, "no-side-orchestrator");

                return side.Army.ResolveCommandIntentForGroup(group.GetInstanceID());
            }
            catch (Exception ex)
            {
                return new CommandIntentResolution(false, default, "resolve-error:" + ex.GetType().Name);
            }
        }

        private static bool HasPlayerOwnership(Regiment group)
        {
            try
            {
                if (group == null) return true;
                if (!SafeAiVsAi() && group.alliance == SafePlayerAlliance()) return true;
                if (group.dlcw_isundercommander) return true;
                if (group.allattachedunits == null) return false;

                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null && unit.dlcw_isundercommander) return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool AnyAlreadyEngaged(UnitState[] changed)
        {
            for (int i = 0; i < changed.Length; i++)
            {
                Regiment unit = changed[i].Unit;
                if (unit != null && (changed[i].InEngagement || SafeInEngagement(unit)))
                    return true;
            }

            return false;
        }

        private static float OwnStrengthRatio(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                float active = Sanitize(group.groupstrengthactive);
                float total = Math.Max(1f, Sanitize(group.groupstrength));
                return active / total;
            }
            catch
            {
                return 1f;
            }
        }

        private static float LocalOdds(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                float own = Math.Max(Sanitize(group.groupowninrange), Sanitize(group.groupstrengthaigroup));
                float enemy = Math.Max(Sanitize(group.groupenemiesinrange), SumEnemyStrengthWithinAngle(group));
                return enemy <= 0f ? 1f : own / Math.Max(1f, enemy);
            }
            catch
            {
                return 1f;
            }
        }

        private static float SumEnemyStrengthWithinAngle(Regiment group)
        {
            try
            {
                if (group == null || group.unitrange == null || group.unitrange.enemystrengthwithinangle == null)
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

        private static bool SafeDoubleQuick(Regiment unit)
        {
            try { return unit != null && unit.doublequick; }
            catch { return false; }
        }

        private static bool SafeInEngagement(Regiment unit)
        {
            try { return unit != null && unit.inengagement; }
            catch { return false; }
        }

        private static int SafePlayerAlliance()
        {
            try { return GameVars.playeralliance; }
            catch { return -99; }
        }

        private static bool SafeAiVsAi()
        {
            try { return GameVars.ai_vs_ai; }
            catch { return false; }
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static void Log(
            Regiment group,
            TacticalReserveCommitGate.Action action,
            DirectChildRole role,
            string reason,
            int changedUnits)
        {
            try
            {
                string groupName = SafeGroupName(group);
                string safeReason = TacticalCurrentOrderSignature.Safe(reason);
                string key = "tactical-reserve-commit-gate:"
                    + (group != null ? group.GetInstanceID().ToString() : "null")
                    + ":" + action
                    + ":" + role
                    + ":" + safeReason
                    + ":" + Math.Max(0, changedUnits);
                OnceLog.Info(
                    key,
                    "[TacticalReserveCommitGate] group=" + groupName
                    + " action=" + action
                    + " role=" + role
                    + " reason=" + safeReason
                    + " changedUnits=" + Math.Max(0, changedUnits));
            }
            catch { }
        }

        private static string SafeGroupName(Regiment group)
        {
            try { return group != null ? TacticalCurrentOrderSignature.Safe(group.name) : "-"; }
            catch { return "-"; }
        }
    }
}
