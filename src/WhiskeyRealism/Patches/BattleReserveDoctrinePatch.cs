using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B6c reserve assignment observer. Vanilla AssignReserves owns moving units
    // from reservegroups into linegroup_* fields; LinkReservesToLineGroup depends
    // on that vanilla flow. Task 9 keeps this Postfix telemetry-only and reports
    // drift when the current ledger command state says a protected reserve was
    // assigned into a line group. No reservegroups reordering happens here.
    [HarmonyPatch(typeof(AIBattle), "AssignReserves")]
    internal static class BattleReserveDoctrinePatch
    {
        private static FieldInfo _objectiveChainField;
        private static FieldInfo _lineGroupCenterUnitField;
        private static FieldInfo _lineGroupLeftUnitsField;
        private static FieldInfo _lineGroupRightUnitsField;
        private static FieldInfo _sideOfAiField;
        private static bool _missingAnchorLogged;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.LowerThanNormal)]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.reserve-doctrine", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (!Enabled() || __instance == null) return;

                int side = SafeIntField(__instance, ref _sideOfAiField, "sideofai", -1);
                if (side < 0) return;

                if (!BattleCommanderIntentObserverPatch.RefreshRuntimeState(__instance, emitTelemetry: false))
                    return;

                IList chain = ObjectiveChain(__instance);
                if (chain == null || chain.Count == 0) return;
                LogProtectedReserveDrift(side, chain);
            }
        }

        private static void LogProtectedReserveDrift(int side, IList chain)
        {
            try
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    object entry = chain[i];
                    LogProtectedLineGroup(side, i, "center", 0, LineGroupCenterUnit(entry));
                    LogProtectedLineGroupList(side, i, "left", LineGroupUnits(entry, ref _lineGroupLeftUnitsField, "linegroup_leftunits"));
                    LogProtectedLineGroupList(side, i, "right", LineGroupUnits(entry, ref _lineGroupRightUnitsField, "linegroup_rightunits"));
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-drift:assign-reserves:failed",
                    "[TacticalReserveDrift] AssignReserves drift inspection failed: " + ex.Message);
            }
        }

        private static void LogProtectedLineGroupList(int side, int chainIndex, string slot, IList groups)
        {
            if (groups == null) return;
            for (int i = 0; i < groups.Count; i++)
            {
                LogProtectedLineGroup(side, chainIndex, slot, i, groups[i] as Regiment);
            }
        }

        private static void LogProtectedLineGroup(int side, int chainIndex, string slot, int slotIndex, Regiment group)
        {
            if (group == null) return;
            if (!TryResolveLedgerCommandState(group, out CommandNodeOperationalState state)) return;
            if (!IsLedgerCommandStateProtectedReserve(state)) return;

            OnceLog.Info(
                "tactical-reserve-drift:assign-reserves-linegroup:" + side + ":" + chainIndex + ":" + slot + ":" + slotIndex + ":" + SafeInstanceId(group),
                "[TacticalReserveDrift] surface=AssignReserves side=" + side +
                " chain=" + chainIndex +
                " slot=" + slot +
                " slotIndex=" + slotIndex +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " ledgerRole=" + state.Role +
                " ledgerTask=" + state.Task +
                " assignmentSource=ledger-command-state" +
                " reason=ledger-command-state-protected-line-assignment");
        }

        private static bool IsLedgerCommandStateProtectedReserve(CommandNodeOperationalState state)
        {
            return state.Role == CommandNodeRole.Reserve || state.Task == CommandTaskType.ReserveWait;
        }

        private static bool TryResolveLedgerCommandState(Regiment group, out CommandNodeOperationalState state)
        {
            state = default;
            try
            {
                if (group == null) return false;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                var operations = side?.Army?.CurrentCommandOperations;
                if (operations == null || operations.Count == 0) return false;

                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                for (int i = 0; i < operations.Count; i++)
                {
                    if (TacticalPatchIds.NodeIdMatches(operations[i].NodeId, gameObjectInstanceId, componentInstanceId))
                    {
                        state = operations[i];
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static Regiment LineGroupCenterUnit(object objectiveChainEntry)
        {
            if (objectiveChainEntry == null)
            {
                LogMissingAnchor("objectivechain:entry");
                return null;
            }

            if (_lineGroupCenterUnitField == null)
                _lineGroupCenterUnitField = AccessTools.Field(objectiveChainEntry.GetType(), "linegroup_centerunit");
            if (_lineGroupCenterUnitField == null)
            {
                LogMissingAnchor("linegroup_centerunit");
                return null;
            }

            return _lineGroupCenterUnitField.GetValue(objectiveChainEntry) as Regiment;
        }

        private static IList LineGroupUnits(object objectiveChainEntry, ref FieldInfo cache, string fieldName)
        {
            if (objectiveChainEntry == null)
            {
                LogMissingAnchor("objectivechain:entry");
                return null;
            }

            if (cache == null)
                cache = AccessTools.Field(objectiveChainEntry.GetType(), fieldName);
            if (cache == null)
            {
                LogMissingAnchor(fieldName);
                return null;
            }

            IList groups = cache.GetValue(objectiveChainEntry) as IList;
            if (groups == null) LogMissingAnchor(fieldName + ":value");
            return groups;
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

        private static IList ObjectiveChain(AIBattle battle)
        {
            if (_objectiveChainField == null)
                _objectiveChainField = AccessTools.Field(typeof(AIBattle), "objective" + "chain");
            if (_objectiveChainField == null)
            {
                LogMissingAnchor("objectivechain");
                return null;
            }

            IList chain = _objectiveChainField.GetValue(battle) as IList;
            if (chain == null) LogMissingAnchor("objectivechain:value");
            return chain;
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return Convert.ToInt32(cache.GetValue(instance));
            }
            catch
            {
                return fallback;
            }
        }

        private static void LogMissingAnchor(string anchor)
        {
            if (_missingAnchorLogged) return;
            _missingAnchorLogged = true;
            OnceLog.Warning(
                "tactical-reserve-doctrine:missing-anchor:" + anchor,
                "BattleReserveDoctrinePatch missing required anchor " + anchor + "; running inert");
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value &&
                ((Plugin.Instance.EnableTacticalReserveIntentTelemetry != null &&
                  Plugin.Instance.EnableTacticalReserveIntentTelemetry.Value) ||
                 (Plugin.Instance.EnableTacticalReserveListMutation != null &&
                  Plugin.Instance.EnableTacticalReserveListMutation.Value));
        }
    }
}
