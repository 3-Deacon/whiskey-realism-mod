using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // W&L guard for AIBattle.UpdateMovingTargets. Vanilla skips objective-chain
    // movement when the center group itself is under player command, but not
    // when that AI center group carries player-commanded attached units.
    [HarmonyPatch(typeof(AIBattle), "UpdateMovingTargets")]
    internal static class BattleObjectiveChainWlGuardPatch
    {
        private static FieldInfo _objectiveChainField;
        private static FieldInfo _chainCenterUnitField;
        private static bool _missingRequiredAnchorLogged;

        internal sealed class ObjectiveChainFilterState
        {
            internal IList Chain;
            internal readonly List<RemovedObjectiveChain> Removed = new List<RemovedObjectiveChain>();
        }

        internal readonly struct RemovedObjectiveChain
        {
            internal RemovedObjectiveChain(int index, object chain, Regiment center, int attachedUnderCommander, string reason)
            {
                Index = index;
                Chain = chain;
                Center = center;
                AttachedUnderCommander = attachedUnderCommander;
                Reason = reason;
            }

            internal int Index { get; }
            internal object Chain { get; }
            internal Regiment Center { get; }
            internal int AttachedUnderCommander { get; }
            internal string Reason { get; }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        internal static void Prefix(AIBattle __instance, out ObjectiveChainFilterState __state)
        {
            __state = new ObjectiveChainFilterState();
            if (!Enabled()) return;

            try
            {
                IList chain = ObjectiveChain(__instance);
                if (chain == null || chain.Count <= 0) return;
                __state.Chain = chain;

                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    object entry = chain[i];
                    Regiment center = CenterUnit(entry);
                    if (center == null) continue;

                    int attachedUnderCommander = CountAttachedUnderCommander(center);
                    var decision = TacticalWlActionGuard.Decide(
                        configEnabled: Plugin.Instance.EnableWlTacticalChargeGuard.Value,
                        dlcScenarioActive: DLC_WL.dlc_scenarioactive,
                        action: TacticalWlGuardAction.ObjectiveChainAdvance,
                        unitUnderCommander: center.dlcw_isundercommander,
                        groupUnderCommander: center.dlcw_isundercommander,
                        attachedUnitUnderCommander: attachedUnderCommander > 0);

                    if (decision.Allow) continue;

                    __state.Removed.Add(new RemovedObjectiveChain(i, entry, center, attachedUnderCommander, decision.Reason));
                    chain.RemoveAt(i);
                    LogDenied(center, i, attachedUnderCommander, decision.Reason);
                }
            }
            catch (Exception ex)
            {
                Restore(__state);
                OnceLog.Warning("tactical-objective-chain-guard:failed-pre", "BattleObjectiveChainWlGuardPatch failed before vanilla movement; restored filtered chains and falling back to vanilla: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        internal static void Postfix(ObjectiveChainFilterState __state)
        {
            Restore(__state);
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableWlTacticalChargeGuard.Value;
        }

        private static IList ObjectiveChain(AIBattle battle)
        {
            if (battle == null) return null;
            if (_objectiveChainField == null)
                _objectiveChainField = AccessTools.Field(typeof(AIBattle), "objective" + "chain");
            if (_objectiveChainField == null)
            {
                LogMissingRequiredAnchor("objectivechain");
                return null;
            }

            var chain = _objectiveChainField.GetValue(battle) as IList;
            if (chain == null) LogMissingRequiredAnchor("objectivechain:value");
            return chain;
        }

        private static Regiment CenterUnit(object chain)
        {
            if (chain == null) return null;
            if (_chainCenterUnitField == null)
                _chainCenterUnitField = AccessTools.Field(chain.GetType(), "linegroup_centerunit");
            if (_chainCenterUnitField == null)
            {
                LogMissingRequiredAnchor("linegroup_centerunit");
                return null;
            }

            return _chainCenterUnitField.GetValue(chain) as Regiment;
        }

        private static int CountAttachedUnderCommander(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return 0;

            int count = 0;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                Regiment unit = group.allattachedunits[i];
                if (unit != null && unit.dlcw_isundercommander) count++;
            }

            return count;
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

        private static void LogDenied(Regiment center, int chainIndex, int attachedUnderCommander, string reason)
        {
            string centerName = center != null && center.gameObject != null
                ? ((UnityEngine.Object)center.gameObject).name
                : "unknown";
            int centerId = center != null ? center.GetInstanceID() : 0;

            OnceLog.Info(
                "tactical-objective-chain-guard:" + centerId,
                "[TacticalObjectiveGuard] denied objective-chain advance center=" + centerName +
                "#" + centerId +
                " chain=" + chainIndex +
                " attachedUnderCommander=" + attachedUnderCommander +
                " reason=" + reason);
        }

        private static void LogMissingRequiredAnchor(string name)
        {
            if (_missingRequiredAnchorLogged) return;
            _missingRequiredAnchorLogged = true;
            OnceLog.Warning("tactical-objective-chain-guard:missing-anchor", "BattleObjectiveChainWlGuardPatch missing required anchor: " + name + "; vanilla objective-chain movement will run.");
        }
    }
}
