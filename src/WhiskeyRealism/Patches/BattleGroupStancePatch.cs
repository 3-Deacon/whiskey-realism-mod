using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B5 group sector stance scorer. This is deliberately limited to stance
    // pressure; it does not issue movement, reserve, artillery, fallback, or
    // charge orders.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
    internal static class BattleGroupStancePatch
    {
        private static readonly Dictionary<string, float> _lastLoggedAt = new Dictionary<string, float>();
        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _orderedStanceField;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.battle-group-stance", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (!Enabled() || __instance == null) return;

                try
                {
                    Apply(__instance);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-group-stance:failed", "Tactical group sector stance failed: " + ex.Message);
                }
            }
        }

        private static void Apply(AIBattle battle)
        {
            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");
            if (side < 0 || macro < 0 || bunits == null || units == null) return;

            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (group == null || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)) continue;
                ApplyGroup(bunits, side, macro, group, i);
            }
        }

        private static void ApplyGroup(BattleUnits bunits, int side, int macro, Regiment group, int index)
        {
            if (!WlAllowsControl(group)) return;
            if (!OrderFrictionAllowsChange(group)) return;

            int vanillaOrdered = SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered);
            var sector = BuildGroupSector(group, index);

            if (TryApplyDoctrineStance(bunits, side, macro, group, vanillaOrdered, sector))
                return;

            if (vanillaOrdered == 4)
            {
                if (!Plugin.Instance.EnableTacticalChargeDenial.Value || !LocalReactionProducerEnabled())
                {
                    LogChargePreserved(side, group, "vanilla-charge-preserved");
                    return;
                }

                var reaction = TacticalReactionContext.Shared.GetReaction(SafeInstanceId(group));
                if (IsExplicitChargeDenial(reaction))
                {
                    if (!AllowsVanillaWrites())
                    {
                        LogChargePreserved(side, group, "mode-monitor-only");
                        return;
                    }

                    DemoteCharge(bunits, group, side, reaction);
                    return;
                }

                LogChargePreserved(side, group, "vanilla-charge-preserved");
                return;
            }

            if (TryApplyLedgerTaskStance(bunits, side, macro, group, vanillaOrdered, sector))
                return;

            if (!Plugin.Instance.EnableTacticalGroupSectorStance.Value) return;

            var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
                vanillaOrdered,
                macro,
                sector,
                true,
                true));

            if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (decision.GroupStance == vanillaOrdered) return;
            if (decision.GroupStance == 4) return;
            if (decision.GroupStance < 0 || decision.GroupStance > 3) return;
            if (!AllowsVanillaWrites()) return;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return;

            bunits.ChangeStance(gameObject, decision.GroupStance, immediate: false, overwriteaigroups: false);
            group.ai_stance = decision.GroupStance;
            group.ai_stanceordered = decision.GroupStance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogDecision(side, group, sector, decision);
        }

        private static bool TryApplyDoctrineStance(
            BattleUnits bunits,
            int side,
            int macro,
            Regiment group,
            int vanillaOrdered,
            TacticalSectorAssessment sector)
        {
            if (macro == 3) return false;
            if (!TryResolveDoctrineOrder(group, out CommandDoctrineOrder order, out BattlefieldPictureSnapshot picture)) return false;

            DoctrineStanceDecision decision = DoctrineConsumerDecisions.DecideStance(
                order,
                DoctrineConsumerDecisions.EnemyMainLineExposed(order, picture),
                sector.Odds);
            if (decision.Action == DoctrineConsumerAction.Observe) return false;

            int stance = decision.Action == DoctrineConsumerAction.Allow ? 3 : 2;
            if (stance == vanillaOrdered) return true;
            if (!AllowsVanillaWrites()) return true;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return true;

            bunits.ChangeStance(gameObject, stance, immediate: false, overwriteaigroups: false);
            group.ai_stance = stance;
            group.ai_stanceordered = stance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogDoctrineStanceDecision(side, group, order, decision, stance);
            return true;
        }

        private static bool TryApplyLedgerTaskStance(BattleUnits bunits, int side, int macro, Regiment group, int vanillaOrdered, TacticalSectorAssessment sector)
        {
            if (macro == 3) return true;
            if (!TryResolveLedgerState(group, out CommandNodeOperationalState state)) return false;

            int stance = StanceForTask(state.Task, vanillaOrdered, sector);
            if (stance < 0) return true;
            if (stance == vanillaOrdered) return true;
            if (stance == 4 || stance < 0 || stance > 3) return true;
            if (!AllowsVanillaWrites()) return true;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return true;

            bunits.ChangeStance(gameObject, stance, immediate: false, overwriteaigroups: false);
            group.ai_stance = stance;
            group.ai_stanceordered = stance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogLedgerTaskDecision(side, group, state, stance);
            return true;
        }

        private static int StanceForTask(CommandTaskType task, int vanillaOrdered, TacticalSectorAssessment sector)
        {
            if ((task == CommandTaskType.HoldObjective ||
                 task == CommandTaskType.HoldChoke ||
                 task == CommandTaskType.FixEnemy) &&
                TacticalDoctrineScorer.ShouldCommitVisibleWeakPoint(sector))
                return 3;

            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                    return 3;
                case CommandTaskType.FixEnemy:
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.FallBackToLine:
                    return 2;
                default:
                    return -1;
            }
        }

        private static bool TryResolveLedgerState(Regiment group, out CommandNodeOperationalState state)
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

        private static bool TryResolveDoctrineOrder(
            Regiment group,
            out CommandDoctrineOrder order,
            out BattlefieldPictureSnapshot picture)
        {
            order = default(CommandDoctrineOrder);
            picture = new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>());
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
                    if (side?.OperationsLedger != null)
                        picture = side.OperationsLedger.CurrentBattlefieldPicture;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static void LogDoctrineStanceDecision(
            int side,
            Regiment group,
            CommandDoctrineOrder order,
            DoctrineStanceDecision decision,
            int stance)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + order.Task + "|" +
                decision.Action + "|" + stance + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-doctrine-consumer", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " stance=" + stance +
                " doctrineTask=" + order.Task +
                " action=" + decision.Action +
                " reason=" + decision.Reason, TelemetryLayer.Tactical);
        }

        private static void LogLedgerTaskDecision(int side, Regiment group, CommandNodeOperationalState state, int stance)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + state.Task + "|" + stance;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-ledger-task", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " stance=" + stance +
                " ledgerTask=" + state.Task +
                " ledgerRole=" + state.Role +
                " reason=operations-ledger-task", TelemetryLayer.Tactical);
        }

        private static bool IsExplicitChargeDenial(TacticalLocalReactionDecision reaction)
        {
            return reaction.Reason != "no-decision" &&
                reaction.Reaction == LocalReaction.DenyCharge;
        }

        private static void DemoteCharge(
            BattleUnits bunits,
            Regiment group,
            int side,
            TacticalLocalReactionDecision reaction)
        {
            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return;

            bunits.ChangeStance(gameObject, 3, immediate: false, overwriteaigroups: false);
            group.ai_stance = 3;
            group.ai_stanceordered = 3;
            group.lastaistancechangetime = GameVars.currenttimefromstart;

            string signature = side + "|" + SafeInstanceId(group) + "|" + reaction.Reaction + "|" + reaction.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "b6c-charge-deny-stance", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalChargeDeny] surface=stance side=" + side +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reaction=" + reaction.Reaction +
                " reason=" + reaction.Reason, TelemetryLayer.Tactical);
        }

        private static void LogChargePreserved(int side, Regiment group, string reason)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "b6c-charge-preserved", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalChargePreserved] surface=stance side=" + side +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reason=" + reason, TelemetryLayer.Tactical);
        }

        private static TacticalSectorAssessment BuildGroupSector(Regiment group, int index)
        {
            Regiment closest = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
            return TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
                index,
                group != null ? Math.Max(group.groupowninrange, group.groupstrengthaigroup) : 0f,
                group != null ? group.groupenemiesinrange : 0f,
                SumEnemyStrengthWithinAngle(group),
                closest != null ? closest.strength : 0f,
                closest != null ? closest.unittyp : -1,
                closest != null ? SafeName(closest) : string.Empty,
                closest != null && closest.isrouted,
                closest != null && closest.permanentlydetached,
                group != null && (group.flanksthreated > 0f || group.outflanked > 0),
                group != null && (group.covervalue > 0.5f || group.fortinrange)));
        }

        private static bool WlAllowsControl(Regiment group)
        {
            var decision = TacticalWlActionGuard.Decide(
                true,
                DLC_WL.dlc_scenarioactive,
                TacticalWlGuardAction.FeudMovement,
                group != null && group.dlcw_isundercommander,
                group != null && group.dlcw_isundercommander,
                AttachedUnitUnderPlayerCommander(group));
            return decision.Allow;
        }

        private static bool AttachedUnitUnderPlayerCommander(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return false;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit != null && unit.dlcw_isundercommander) return true;
            }

            return false;
        }

        private static bool OrderFrictionAllowsChange(Regiment group)
        {
            return TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
            {
                OrderQueueCount = SafeOrderQueueCount(group),
                OrderState = group != null ? group.orderstate : -1,
                RegimentPaths = group != null ? group.regimentpaths : 0,
                PathInterrupted = group != null && group.pathinterrupted,
                MovementMode = group != null ? group.movementmode : -1,
                ActiveMove = HasActiveMoveOrder(group)
            }).AllowChange;
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
                return false;
            }
        }

        private static int SafeOrderQueueCount(Regiment group)
        {
            try { return group != null && group.orderqueue != null ? group.orderqueue.Count : 0; }
            catch { return 1; }
        }

        private static void LogDecision(
            int side,
            Regiment group,
            TacticalSectorAssessment sector,
            TacticalGroupStanceDecision decision)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + sector.SectorId + "|" +
                decision.GroupStance + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "group-decision", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            TelemetryRouter.LegacyInfo("[TacticalGroupDecision] side=" + side +
                " group=" + SafeInstanceId(group) +
                " sector=" + sector.SectorId +
                " stance=" + decision.GroupStance +
                " mission=" + sector.Mission +
                " reason=" + decision.Reason +
                " odds=" + sector.Odds.ToString("0.00") +
                " confidence=" + sector.Confidence.ToString("0.00"), TelemetryLayer.Tactical);
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                (Plugin.Instance.EnableTacticalGroupSectorStance.Value ||
                    Plugin.Instance.TacticalOperationsLedgerAllowsWrites);
        }

        private static bool AllowsVanillaWrites()
        {
            try
            {
                return Plugin.Instance != null &&
                    TacticalCommanderModePolicy.AllowsWrites(Plugin.Instance.TacticalCommanderModeValue);
            }
            catch
            {
                return false;
            }
        }

        private static bool LocalReactionProducerEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalCommanderIntentDoctrine.Value &&
                Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value;
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

        private static T SafeField<T>(object instance, ref FieldInfo cache, string name) where T : class
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as T : null;
            }
            catch
            {
                return null;
            }
        }

        private static IList SafeList(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as IList : null;
            }
            catch
            {
                return null;
            }
        }

        private static GameObject UnityObject(Regiment unit)
        {
            try
            {
                return unit != null ? ((Component)unit).gameObject : null;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeName(Regiment group)
        {
            try
            {
                return group != null ? ((Component)group).gameObject.name : "<null>";
            }
            catch
            {
                return "<err>";
            }
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try
            {
                return obj != null ? obj.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
