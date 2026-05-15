using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.UpdateDLCPlayerOrders runs during the tactical player-order
    // cadence and may self-issue W&L transition/support orders through
    // CheckCurrentOrderUpdate. This Postfix runs after vanilla, yields to those
    // transition orders, and routes operations-ledger intent to the same vanilla
    // current-order bridge without using campaign bypass mode.
    [HarmonyPatch(typeof(AIBattle), "UpdateDLCPlayerOrders")]
    internal static class PlayerSubordinateOrderPatch
    {
        private const float DuplicateDistance = 110f;
        private const float DefaultOrderWidth = 100f;
        private const float DefaultOrderDepth = 50f;

        private static readonly Dictionary<int, string> _lastSignatures = new Dictionary<int, string>();

        internal readonly struct OrderSnapshot
        {
            public OrderSnapshot(bool hasOrder, int type, int session, Vector3 position)
            {
                HasOrder = hasOrder;
                Type = type;
                Session = session;
                Position = position;
            }

            public bool HasOrder { get; }
            public int Type { get; }
            public int Session { get; }
            public Vector3 Position { get; }
        }

        internal static void Prefix(out OrderSnapshot __state)
        {
            __state = SnapshotActiveOrder();
        }

        internal static void Postfix(AIBattle __instance, OrderSnapshot __state)
        {
            if (__instance == null) return;

            try
            {
                OnceLog.Info("wl-player-subordinate-order", "PlayerSubordinateOrderPatch wired");

                OrderSnapshot after = SnapshotActiveOrder();
                bool vanillaChanged = after.Session != __state.Session || after.Type != __state.Type;
                if (!TryComposeCandidate(out Regiment currentCommand, out CommandDoctrineOrder order, out PlayerSubordinateOrderDecision decision))
                {
                    EmitDiagnostics("suppress:no-candidate");
                    return;
                }

                int vanillaType = VanillaTypeFor(decision.Intent);
                if (vanillaType < 0)
                {
                    EmitDiagnostics("suppress:no-vanilla-type:" + decision.Intent);
                    return;
                }

                DoctrineTargetPoint targetPoint = TargetFor(order, decision.Intent);
                if (!targetPoint.HasValue)
                {
                    EmitDiagnostics("suppress:no-target:" + decision.Intent);
                    return;
                }

                Vector3 target = new Vector3(targetPoint.X, SafeBattleY(), targetPoint.Z);
                if (ShouldYieldToVanilla(vanillaChanged, after, vanillaType, target))
                {
                    EmitDiagnostics("yield:vanilla-type-" + after.Type);
                    return;
                }

                string signature = currentCommand.GetInstanceID() + "|" + vanillaType + "|" +
                    Math.Round(target.x) + "|" + Math.Round(target.z) + "|" + order.Task;
                int key = currentCommand.GetInstanceID();
                if (_lastSignatures.TryGetValue(key, out string previous) &&
                    string.Equals(previous, signature, StringComparison.Ordinal))
                {
                    EmitDiagnostics("suppress:signature-unchanged:" + vanillaType);
                    return;
                }

                if (!WritesEnabled())
                {
                    EmitDiagnostics("classify:writes-disabled:" + vanillaType + ":" + decision.Reason);
                    return;
                }

                AIBattle.CheckCurrentOrderUpdate(
                    currentCommand,
                    vanillaType,
                    target,
                    string.IsNullOrWhiteSpace(order.ObjectiveId) ? "Objective" : order.ObjectiveId,
                    -1f,
                    DefaultOrderWidth,
                    DefaultOrderDepth);

                _lastSignatures[key] = signature;
                EmitDiagnostics("issue:type-" + vanillaType + ":" + decision.Intent + ":" + order.Task);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "wl-player-subordinate-order:failed",
                    "PlayerSubordinateOrderPatch failed: " + ex.Message);
            }
        }

        private static bool TryComposeCandidate(
            out Regiment currentCommand,
            out CommandDoctrineOrder order,
            out PlayerSubordinateOrderDecision decision)
        {
            currentCommand = null;
            order = default(CommandDoctrineOrder);
            decision = default(PlayerSubordinateOrderDecision);

            if (!SafeWlActive()) return false;
            if (SafePlayerIsCic()) return false;
            currentCommand = SafeCurrentCommand();
            if (currentCommand == null) return false;
            if (!TryResolveDoctrineOrder(currentCommand, out order)) return false;

            Regiment commandForFacts = currentCommand;
            decision = PlayerSubordinateOrderDoctrine.Decide(new PlayerSubordinateOrderInput(
                wlScenarioActive: true,
                playerIsCommander: false,
                playerUnderCommander: SafeBool(() => commandForFacts.dlcw_isundercommander),
                existingOrderFresh: false,
                order: order));
            return decision.ShouldIssueWlOrder;
        }

        private static bool TryResolveDoctrineOrder(Regiment group, out CommandDoctrineOrder order)
        {
            order = default(CommandDoctrineOrder);

            try
            {
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                ArmyOrchestrator army = side?.Army;
                IReadOnlyList<CommandDoctrineOrder> orders = army?.CurrentDoctrineOrders;
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
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static DoctrineTargetPoint TargetFor(CommandDoctrineOrder order, PlayerSubordinateOrderIntent intent)
        {
            switch (intent)
            {
                case PlayerSubordinateOrderIntent.Fallback:
                    return order.FallbackTarget.HasValue ? order.FallbackTarget : order.PrimaryTarget;
                case PlayerSubordinateOrderIntent.Hold:
                    return order.PrimaryTarget.HasValue ? order.PrimaryTarget : order.SupportTarget;
                case PlayerSubordinateOrderIntent.Support:
                    return order.SupportTarget.HasValue ? order.SupportTarget : order.PrimaryTarget;
                default:
                    return order.PrimaryTarget.HasValue ? order.PrimaryTarget : order.SupportTarget;
            }
        }

        private static int VanillaTypeFor(PlayerSubordinateOrderIntent intent)
        {
            switch (intent)
            {
                case PlayerSubordinateOrderIntent.Attack:
                    return 7;
                case PlayerSubordinateOrderIntent.Support:
                    return 6;
                case PlayerSubordinateOrderIntent.Fallback:
                    return 12;
                case PlayerSubordinateOrderIntent.Screen:
                case PlayerSubordinateOrderIntent.Move:
                    return 5;
                case PlayerSubordinateOrderIntent.Hold:
                    return 12;
                default:
                    return -1;
            }
        }

        private static bool ShouldYieldToVanilla(
            bool vanillaChanged,
            OrderSnapshot active,
            int candidateType,
            Vector3 target)
        {
            if (!active.HasOrder) return false;
            if (active.Type == 15 || active.Type == 13) return true;
            if (vanillaChanged && active.Type == 14) return true;
            if (active.Type == candidateType && Vector3.Distance(active.Position, target) < DuplicateDistance)
                return true;
            if (candidateType != 14 && active.Type == 14) return true;
            if (candidateType != 12 && active.Type == 12) return true;
            return false;
        }

        private static bool WritesEnabled()
        {
            try
            {
                return Plugin.Instance != null &&
                    Plugin.Instance.Enabled.Value &&
                    Plugin.Instance.EnableWlPlayerSubordinateOrderBridge.Value &&
                    Plugin.Instance.TacticalOperationsLedgerAllowsWrites;
            }
            catch
            {
                return false;
            }
        }

        private static void EmitDiagnostics(string message)
        {
            try
            {
                if (Plugin.Instance == null ||
                    Plugin.Instance.EnableWlPlayerOrderDoctrineDiagnostics == null ||
                    !Plugin.Instance.EnableWlPlayerOrderDoctrineDiagnostics.Value)
                    return;
                OnceLog.Info("wl-player-subordinate-order:" + message, "[WlPlayerOrder] " + message);
            }
            catch { }
        }

        private static OrderSnapshot SnapshotActiveOrder()
        {
            try
            {
                object given = DLC_WL.givenorder;
                if (given == null)
                    return new OrderSnapshot(false, -1, ReadSession(), default(Vector3));
                return new OrderSnapshot(
                    true,
                    SafeInt(() => DLC_WL.givenorder.type, -1),
                    ReadSession(),
                    SafeVector(() => DLC_WL.givenorder.position));
            }
            catch
            {
                return new OrderSnapshot(false, -1, ReadSession(), default(Vector3));
            }
        }

        private static Regiment SafeCurrentCommand()
        {
            try
            {
                if (DLC_WL.dlc_chosencommander < 0 ||
                    DLC_WL.dlc_chosencommander >= GameVars.commander.Count)
                    return null;
                return GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
            }
            catch
            {
                return null;
            }
        }

        private static bool SafeWlActive()
        {
            return SafeBool(() => DLC_WL.dlc_scenarioactive);
        }

        private static bool SafePlayerIsCic()
        {
            return SafeBool(() => DLC_WL.IsCommanderInChief());
        }

        private static int ReadSession()
        {
            return SafeInt(() => DLC_WL.GivenOrders.givenorderssession, -1);
        }

        private static float SafeBattleY()
        {
            return 0f;
        }

        private static bool SafeBool(Func<bool> read)
        {
            try { return read(); }
            catch { return false; }
        }

        private static int SafeInt(Func<int> read, int fallback)
        {
            try { return read(); }
            catch { return fallback; }
        }

        private static Vector3 SafeVector(Func<Vector3> read)
        {
            try { return read(); }
            catch { return default(Vector3); }
        }
    }
}
