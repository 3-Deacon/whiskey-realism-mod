using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #46 — W&L guard for AIBattle.UpdateMovingTargets (decompile 6870).
    // Vanilla skips objective-chain MOVEMENT when the chain center itself is
    // under the player's command (6951: !dlc | !center.dlcw_isundercommander),
    // but still runs CheckCurrentOrderUpdate (6995-7003) so the player's
    // superior issues the order. Ancestors of the player advance: the
    // player's command is protected by its permanentlydetached subtree, not
    // by freezing the parent chain (docs/wl-player-model.md).
    //
    // The guard applies the same rule to a center that is itself player-owned
    // (the player's current command or a battle take-over) but whose cached
    // dlcw_isundercommander flag is not set (pure
    // BattleWriterPolicy.ObjectiveChainGuardRemovesCenter). The Prefix
    // removes such chains so vanilla does not move them, and for
    // each removed chain that vanilla would have reached (pure
    // BattleWriterPolicy.ObjectiveChainWouldReachMovement, 6872-6936) replays
    // vanilla's non-movement side: UpdateGroupStrength, unitlinkedto = null,
    // the assigned objective, and the static CheckCurrentOrderUpdate (8233)
    // with vanilla's type 0/12 rule. Only SetWaypoint/BlockedMovingOrder and
    // the "subordinates moving" flags are suppressed.
    // The removed chains are restored in a [HarmonyFinalizer], so a vanilla
    // throw cannot lose them. Commander Mode != Active: vanilla runs untouched.
    [HarmonyPatch(typeof(AIBattle), "UpdateMovingTargets")]
    internal static class BattleObjectiveChainWlGuardPatch
    {
        private static FieldInfo _bunitsField;
        private static MethodInfo _isGroupStillAbleToFight;
        private static bool _missingRequiredAnchorLogged;

        internal sealed class ObjectiveChainFilterState
        {
            internal List<AIBattle.ObjectiveChain> Chain;
            internal readonly List<RemovedObjectiveChain> Removed = new List<RemovedObjectiveChain>();
        }

        internal readonly struct RemovedObjectiveChain
        {
            internal RemovedObjectiveChain(int index, AIBattle.ObjectiveChain chain)
            {
                Index = index;
                Chain = chain;
            }

            internal int Index { get; }
            internal AIBattle.ObjectiveChain Chain { get; }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        internal static void Prefix(AIBattle __instance, out ObjectiveChainFilterState __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.objective-chain-wl-guard-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                __state = null;
                if (!DLC_WL.dlc_scenarioactive) return;
                if (!BattleWriterPolicy.GateMayIntervene(TacticalWriteGuard.ModeAllowsWrites(), Enabled())) return;

                var state = new ObjectiveChainFilterState();
                try
                {
                    if (__instance == null) return;
                    if (!BattleWriterPolicy.ObjectiveChainCycleActive(__instance.state, __instance.macroai)) return;

                    List<AIBattle.ObjectiveChain> chain = __instance.objectivechain;
                    if (chain == null || chain.Count <= 0) return;
                    state.Chain = chain;
                    __state = state;
                    Regiment playerCommand = PlayerCommand();

                    for (int i = chain.Count - 1; i >= 0; i--)
                    {
                        AIBattle.ObjectiveChain entry = chain[i];
                        Regiment center = entry != null ? entry.linegroup_centerunit : null;
                        if ((UnityEngine.Object)center == null) continue;
                        if (!BattleWriterPolicy.ObjectiveChainGuardRemovesCenter(
                                center.dlcw_isundercommander,
                                (UnityEngine.Object)center == (UnityEngine.Object)playerCommand,
                                DLC_WL.IsUnitTakenOver(center)))
                            continue;

                        state.Removed.Add(new RemovedObjectiveChain(i, entry));
                        chain.RemoveAt(i);
                        OnceLog.Info(
                            "tactical-objective-chain-guard:deny",
                            "[TacticalObjectiveGuard] suppressed objective-chain movement of player-owned center=" +
                            TacticalCurrentOrderSignature.Safe(center.name));
                    }

                    // Vanilla iterates chains in index order; replay in the same order.
                    for (int i = state.Removed.Count - 1; i >= 0; i--)
                        ReplayNonMovementPath(__instance, state.Removed[i].Chain);
                }
                catch (Exception ex)
                {
                    Restore(state);
                    __state = null;
                    OnceLog.Warning("tactical-objective-chain-guard:failed-pre", "BattleObjectiveChainWlGuardPatch failed before vanilla movement; restored filtered chains and falling back to vanilla: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, ObjectiveChainFilterState __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.objective-chain-wl-guard-finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                Restore(__state);
                return __exception;
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlTacticalChargeGuard.Value;
        }

        // Vanilla 6877-6936 + 6995-7003 for one chain, minus the movement.
        private static void ReplayNonMovementPath(AIBattle battle, AIBattle.ObjectiveChain entry)
        {
            Regiment center = entry.linegroup_centerunit;
            if ((UnityEngine.Object)center == null || entry.objectives == null || entry.objectives.Count <= 0) return;

            center.UpdateGroupStrength(overridecalc: true);

            Objectives first = entry.objectives[0];
            BattleUnits bunits = BattleUnitsOf(battle);
            if (bunits == null || bunits.uniStormSystem == null) return;

            var probe = new ObjectiveChainMovementProbe(
                hasCenter: true,
                objectiveCount: entry.objectives.Count,
                hasFirstObjective: (UnityEngine.Object)first != null,
                firstObjectiveTimeToMove: entry.objectivetimetomove != null && entry.objectivetimetomove.Count > 0
                    ? entry.objectivetimetomove[0]
                    : float.MaxValue,
                nowBattleHours: bunits.uniStormSystem.Hour + (float)bunits.battlepasseddays * 24f,
                centerUnitTyp: center.unittyp,
                centerRegimentPaths: center.regimentpaths,
                groupSubordinatesMovingNotFar: center.groupsubordinatesmovingnotfar,
                groupPositionAttachedUnits: center.groupposition != null ? center.groupposition.attachedunits : -1,
                pathInterrupted: center.pathinterrupted,
                blockedMovingOrderRunning: AIBattle.BlockedMovingOrder.OrderRunning(center),
                moveOrderActive: center.ordertypeactive != null && center.ordertypeactive.Length > 1 &&
                    (center.ordertypeactive[0] || center.ordertypeactive[1]),
                combatEngagedNonArtillery: center.combatstatussubordinatesengagednonari,
                assaultMoveActive: center.aiassaultmove || center.aiassaultmoveisolatedtarget,
                ableToFight: IsGroupStillAbleToFight(battle, center));
            if (!BattleWriterPolicy.ObjectiveChainWouldReachMovement(probe)) return;

            center.unitlinkedto = null;
            center.currentsetobjective = first;
            if (center.groupposition == null) return;

            int type = first.owner != center.alliance ? 0 : 12;
            AIBattle.CheckCurrentOrderUpdate(
                center,
                type,
                ((Component)first).gameObject.transform.position,
                center.currentsetobjective.objectivename,
                center.lastsetwaypointrotation,
                center.groupposition.groupwidth,
                center.groupposition.groupdepth);
        }

        private static BattleUnits BattleUnitsOf(AIBattle battle)
        {
            if (_bunitsField == null) _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
            if (_bunitsField == null)
            {
                LogMissingRequiredAnchor("bunits");
                return null;
            }

            return _bunitsField.GetValue(battle) as BattleUnits;
        }

        private static bool IsGroupStillAbleToFight(AIBattle battle, Regiment center)
        {
            if (_isGroupStillAbleToFight == null)
                _isGroupStillAbleToFight = AccessTools.Method(typeof(AIBattle), "IsGroupStillAbleToFight", new[] { typeof(Regiment), typeof(bool) });
            if (_isGroupStillAbleToFight == null)
            {
                LogMissingRequiredAnchor("IsGroupStillAbleToFight");
                return false;
            }

            return _isGroupStillAbleToFight.Invoke(battle, new object[] { center, true }) is bool able && able;
        }

        private static Regiment PlayerCommand()
        {
            int id = DLC_WL.dlc_chosencommander;
            if (GameVars.commander == null || id < 0 || id >= GameVars.commander.Count) return null;
            return GameVars.commander[id].currentcommand;
        }

        private static void Restore(ObjectiveChainFilterState state)
        {
            if (state == null || state.Chain == null || state.Removed.Count <= 0) return;

            try
            {
                for (int i = state.Removed.Count - 1; i >= 0; i--)
                {
                    RemovedObjectiveChain removed = state.Removed[i];
                    int index = Math.Max(0, Math.Min(removed.Index, state.Chain.Count));
                    state.Chain.Insert(index, removed.Chain);
                }
                state.Removed.Clear();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-objective-chain-guard:restore", "BattleObjectiveChainWlGuardPatch failed to restore filtered objective chains: " + ex.Message);
            }
        }

        private static void LogMissingRequiredAnchor(string name)
        {
            if (_missingRequiredAnchorLogged) return;
            _missingRequiredAnchorLogged = true;
            OnceLog.Warning("tactical-objective-chain-guard:missing-anchor", "BattleObjectiveChainWlGuardPatch missing required anchor: " + name + "; the suppressed chain's order update is skipped.");
        }
    }
}
