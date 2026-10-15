using System;
using System.Reflection;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    internal enum WlStrategicIntent
    {
        Redeploy,
        Probe,
        Reinforce,
        Offensive,
        OffensiveContinuation,
        EngageEnemy,
        DefendCapital,
        ConstructFort,
        ConstructSupplyDepot,
        ReportOnly
    }

    internal enum WlStrategicOrderResult
    {
        InvalidRequest,
        NotWl,
        DirectMovementAllowed,
        IssuedWlCurrentOrder,
        SkippedPlayerControlled,
        SkippedPlayerCic,
        FailedVanillaBridge,
        WlCurrentOrderIneligible,
        ReportOnly
    }

    internal struct WlStrategicRoleFacts
    {
        internal WlStrategicRoleFacts(
            bool wlActive,
            bool isPlayerAlliance,
            bool isPlayerCic = false,
            bool isMovedByPlayer = false,
            bool isUnderCommander = false,
            bool isPartOfPlayerUnit = false,
            bool currentCommandIsCampaignGroup = false,
            bool currentCommandParentIsUnderTargetUnit = false,
            bool isPlayerCampaignGroup = false)
        {
            WlActive = wlActive;
            IsPlayerAlliance = isPlayerAlliance;
            IsPlayerCic = isPlayerCic;
            IsMovedByPlayer = isMovedByPlayer;
            IsUnderCommander = isUnderCommander;
            IsPartOfPlayerUnit = isPartOfPlayerUnit;
            CurrentCommandIsCampaignGroup = currentCommandIsCampaignGroup;
            CurrentCommandParentIsUnderTargetUnit = currentCommandParentIsUnderTargetUnit;
            IsPlayerCampaignGroup = isPlayerCampaignGroup;
        }

        internal bool WlActive;
        internal bool IsPlayerAlliance;
        internal bool IsPlayerCic;
        internal bool IsMovedByPlayer;
        internal bool IsUnderCommander;
        internal bool IsPartOfPlayerUnit;
        internal bool CurrentCommandIsCampaignGroup;
        internal bool CurrentCommandParentIsUnderTargetUnit;
        internal bool IsPlayerCampaignGroup; // unit == BattleUnits.GetCampaignGroup(player's command)
    }

    // The W&L player's chain of command as Whiskey treats it (stage G spec decision
    // C4, docs/wl-player-model.md):
    //   CicSide        a CIC career: Whiskey stands down for the player's side.
    //   PlayerCommand  the player's own command or a unit under it (player-owned,
    //                  dlcw_isundercommander; in his operation, IsMovedByPlayer; a unit
    //                  inside the map group that carries him; an army-group player's
    //                  units): never moved, never stopped, never re-tasked by Whiskey.
    //   PlayerMapGroup the player's command is itself a map group (vanilla's flag2,
    //                  AIBattle.CheckCurrentOrderUpdate 8271-8274): order-only through
    //                  a W&L dispatch, and an unreliable executor: never counted as a
    //                  committed defender or the holder of a must-hold role.
    //   CarryingGroup  the AI map group above a player whose command is below map-group
    //                  level: a movable AI asset, as vanilla moves it (8366 sends that
    //                  player no campaign order); #69 dispatches its march to him.
    internal enum WlChainClass
    {
        None,
        CicSide,
        PlayerCommand,
        PlayerMapGroup,
        CarryingGroup,
    }

    internal static class WlChainRules
    {
        public static WlChainClass Classify(in WlStrategicRoleFacts facts)
        {
            if (!facts.WlActive || !facts.IsPlayerAlliance) return WlChainClass.None;
            if (facts.IsPlayerCic) return WlChainClass.CicSide;
            if (facts.IsMovedByPlayer) return WlChainClass.PlayerCommand;
            if (facts.IsUnderCommander)
                return facts.CurrentCommandIsCampaignGroup ? WlChainClass.PlayerMapGroup : WlChainClass.PlayerCommand;
            if (facts.IsPartOfPlayerUnit)
                return facts.IsPlayerCampaignGroup && !facts.CurrentCommandIsCampaignGroup
                    ? WlChainClass.CarryingGroup
                    : WlChainClass.PlayerCommand;
            return WlChainClass.None;
        }

        // Moves, stops, operation-list changes, pledges and reverts.
        public static bool MayMove(WlChainClass chain)
        {
            return chain == WlChainClass.None || chain == WlChainClass.CarryingGroup;
        }

        // Defense coverage (must-hold roles, committed defenders).
        public static bool CountsAsCommittedDefender(WlChainClass chain)
        {
            return chain == WlChainClass.None || chain == WlChainClass.CarryingGroup;
        }

        // A unit Whiskey still addresses, by a W&L dispatch.
        public static bool OrderOnly(WlChainClass chain)
        {
            return chain == WlChainClass.PlayerMapGroup;
        }
    }

    internal readonly struct WlStrategicOrderDecision
    {
        internal WlStrategicOrderDecision(
            WlStrategicOrderResult result,
            int wlOrderType,
            bool mayDirectMove,
            bool mayMutateOperationList,
            string reason)
        {
            Result = result;
            WlOrderType = wlOrderType;
            MayDirectMove = mayDirectMove;
            MayMutateOperationList = mayMutateOperationList;
            Reason = reason;
        }

        internal WlStrategicOrderResult Result { get; }
        internal int WlOrderType { get; }
        internal bool MayDirectMove { get; }
        internal bool MayMutateOperationList { get; }
        internal string Reason { get; }
    }

#pragma warning disable 0649
    internal sealed class WlStrategicOrderRequest
    {
        internal int AllianceId;
        internal int AifactionIndex;
        internal Regiment Unit;
        internal Vector3 TargetPosition;
        internal string TargetName;
        internal int ObjectiveId;
        internal WlStrategicIntent Intent;
        internal float Width;
        internal float Depth;
        internal string SourceSystem;
    }
#pragma warning restore 0649

    internal static class WlStrategicOrderBridge
    {
        internal const float DefaultOrderWidth = 20f;
        internal const float DefaultOrderDepth = 20f;

        // A null request fails closed: no movement, no operation-list change.
        private static WlStrategicOrderDecision InvalidRequest()
        {
            return Blocked(WlStrategicOrderResult.InvalidRequest, -1, "null-request");
        }

        // A player-owned top-level map group gets Whiskey's order as a W&L dispatch
        // through the campaign dispatcher (chain-of-command spec §D trigger 2), never a
        // move. Vanilla's own campaign gate (AIBattle.CheckCurrentOrderUpdate 8366)
        // admits exactly that case; the dispatcher emits vanilla's order with the
        // superior as issuer and its own dedupe.
        internal static WlStrategicOrderDecision TryIssue(WlStrategicOrderRequest request)
        {
            if (request == null) return InvalidRequest();

            var facts = BuildFacts(request);
            var decision = Classify(request.Intent, facts);
            if (decision.Result != WlStrategicOrderResult.IssuedWlCurrentOrder)
                return decision;

            string outcome = WlCampaignDispatchRuntime.EmitForPlayerCommand(
                request.Unit,
                decision.WlOrderType,
                request.TargetPosition,
                string.IsNullOrEmpty(request.TargetName) ? "Objective" : request.TargetName,
                DefaultWidth(request.Width),
                DefaultDepth(request.Depth));
            if (outcome == WlCampaignDispatchPolicy.Issue)
                return decision;
            if (outcome == "failed")
                return Classify(request.Intent, facts, vanillaBridgeSucceeded: false);
            return Blocked(WlStrategicOrderResult.WlCurrentOrderIneligible, decision.WlOrderType, "campaign-dispatch:" + outcome);
        }

        internal static WlStrategicOrderDecision ClassifyOnly(WlStrategicOrderRequest request)
        {
            if (request == null) return InvalidRequest();
            return Classify(request.Intent, BuildFacts(request));
        }

        // The unit's chain class for code that changes vanilla state without an
        // order of its own (capital pre-pledge, #25 revert, probe stop, defense
        // coverage). Cheap exit outside a W&L career or the player's alliance.
        internal static WlChainClass ChainClassOf(int allianceId, Regiment unit)
        {
            if (unit == null) return WlChainClass.None;
            if (!SafeBool(() => DLC_WL.dlc_scenarioactive)) return WlChainClass.None;
            if (!SafeBool(() => unit.alliance == GameVars.playeralliance)) return WlChainClass.None;
            return WlChainRules.Classify(BuildFacts(new WlStrategicOrderRequest { AllianceId = allianceId, Unit = unit }));
        }

        private static WlStrategicRoleFacts BuildFacts(WlStrategicOrderRequest request)
        {
            bool wlActive = SafeBool(() => DLC_WL.dlc_scenarioactive);
            bool isPlayerAlliance = SafeBool(() =>
                request.Unit != null &&
                request.Unit.alliance == GameVars.playeralliance &&
                request.AllianceId == GameVars.playeralliance);
            bool isPlayerCic =
                SafeBool(() => DLC_WL.IsCommanderInChief()) ||
                SafeBool(() => IsPlayerCICViaCoordinator(request.AllianceId, GameVars.playeralliance));
            bool isMovedByPlayer = SafeBool(() => DLC_WL.IsMovedByPlayer(request.Unit));
            bool isUnderCommander = SafeBool(() => request.Unit != null && request.Unit.dlcw_isundercommander);
            bool isPartOfPlayerUnit = SafeBool(() => DLC_WL.IsPlayerPartOfUnit(request.Unit));
            bool currentCommandIsCampaignGroup = false;
            bool currentCommandParentIsUnderTargetUnit = false;
            bool isPlayerCampaignGroup = false;

            try
            {
                Regiment current = GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
                Regiment campaignGroup = BattleUnits.GetCampaignGroup(current);
                Regiment parent = BattleUnits.GetParentUnit(current);

                currentCommandIsCampaignGroup = current != null && campaignGroup == current;
                isPlayerCampaignGroup = current != null && request.Unit != null && campaignGroup == request.Unit;
                currentCommandParentIsUnderTargetUnit =
                    parent != null &&
                    request.Unit != null &&
                    parent.transform.IsChildOf(request.Unit.transform);
            }
            catch
            {
                currentCommandIsCampaignGroup = false;
                currentCommandParentIsUnderTargetUnit = false;
                isPlayerCampaignGroup = false;
            }

            return new WlStrategicRoleFacts(
                wlActive,
                isPlayerAlliance,
                isPlayerCic,
                isMovedByPlayer,
                isUnderCommander,
                isPartOfPlayerUnit,
                currentCommandIsCampaignGroup,
                currentCommandParentIsUnderTargetUnit,
                isPlayerCampaignGroup);
        }

        private static bool SafeBool(Func<bool> read)
        {
            try { return read(); }
            catch { return false; }
        }

        // StrategicCoordinator is not compiled into the test harness; reflection keeps
        // this file harness-buildable.
        private static bool IsPlayerCICViaCoordinator(int allianceId, int playerAlliance)
        {
            try
            {
                Type coordinatorType = typeof(WlStrategicOrderBridge).Assembly.GetType("WhiskeyRealism.Strategic.StrategicCoordinator");
                MethodInfo method = coordinatorType?.GetMethod(
                    "IsPlayerCICOf",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(int), typeof(int) },
                    modifiers: null);
                if (method == null) return false;
                return Convert.ToBoolean(method.Invoke(null, new object[] { allianceId, playerAlliance }));
            }
            catch
            {
                return false;
            }
        }


        internal static WlStrategicOrderDecision Classify(
            WlStrategicIntent intent,
            WlStrategicRoleFacts facts,
            bool vanillaBridgeSucceeded = true)
        {
            int orderType = WlOrderTypeForIntent(intent);

            if (!facts.WlActive)
                return Allowed(WlStrategicOrderResult.NotWl, orderType, "wl-inactive");

            if (!facts.IsPlayerAlliance)
                return Allowed(WlStrategicOrderResult.DirectMovementAllowed, orderType, "non-player-alliance");

            if (intent == WlStrategicIntent.ReportOnly)
                return Blocked(WlStrategicOrderResult.ReportOnly, orderType, "report-only");

            switch (WlChainRules.Classify(in facts))
            {
                case WlChainClass.CicSide:
                    return Blocked(WlStrategicOrderResult.SkippedPlayerCic, orderType, "player-cic");
                case WlChainClass.PlayerCommand when facts.IsMovedByPlayer:
                    return Blocked(WlStrategicOrderResult.SkippedPlayerControlled, orderType, "moved-by-player");
                case WlChainClass.PlayerMapGroup:
                    if (!facts.CurrentCommandParentIsUnderTargetUnit)
                        return Blocked(WlStrategicOrderResult.WlCurrentOrderIneligible, orderType, "current-order-chain-ineligible");
                    if (!vanillaBridgeSucceeded)
                        return Blocked(WlStrategicOrderResult.FailedVanillaBridge, orderType, "vanilla-bridge-failed");
                    return Blocked(WlStrategicOrderResult.IssuedWlCurrentOrder, orderType, "issued-current-order");
                case WlChainClass.PlayerCommand when facts.IsUnderCommander:
                    return Blocked(WlStrategicOrderResult.WlCurrentOrderIneligible, orderType, "current-order-chain-ineligible");
                case WlChainClass.PlayerCommand:
                    return Blocked(WlStrategicOrderResult.WlCurrentOrderIneligible, orderType, "part-of-player-unit");
                case WlChainClass.CarryingGroup:
                    // Vanilla moves this AI group (AICampaign.MoveUnitTo); #69's MoveUnitTo
                    // Postfix turns the march into the player's dispatch.
                    return Allowed(WlStrategicOrderResult.DirectMovementAllowed, orderType, "carrying-group");
            }

            return Allowed(WlStrategicOrderResult.DirectMovementAllowed, orderType, "direct-movement-allowed");
        }

        internal static int WlOrderTypeForIntent(WlStrategicIntent intent)
        {
            switch (intent)
            {
                case WlStrategicIntent.Redeploy:
                case WlStrategicIntent.Probe:
                case WlStrategicIntent.Reinforce:
                    return 5;
                case WlStrategicIntent.Offensive:
                    return 16;
                case WlStrategicIntent.OffensiveContinuation:
                    return 6;
                case WlStrategicIntent.EngageEnemy:
                    return 7;
                case WlStrategicIntent.DefendCapital:
                    return 8;
                case WlStrategicIntent.ConstructFort:
                    return 9;
                case WlStrategicIntent.ConstructSupplyDepot:
                    return 10;
                default:
                    return -1;
            }
        }

        internal static float DefaultWidth(float requestedWidth)
        {
            return requestedWidth > 0f ? requestedWidth : DefaultOrderWidth;
        }

        internal static float DefaultDepth(float requestedDepth)
        {
            return requestedDepth > 0f ? requestedDepth : DefaultOrderDepth;
        }

        private static WlStrategicOrderDecision Allowed(WlStrategicOrderResult result, int orderType, string reason)
        {
            return new WlStrategicOrderDecision(result, orderType, mayDirectMove: true, mayMutateOperationList: true, reason);
        }

        private static WlStrategicOrderDecision Blocked(WlStrategicOrderResult result, int orderType, string reason)
        {
            return new WlStrategicOrderDecision(result, orderType, mayDirectMove: false, mayMutateOperationList: false, reason);
        }
    }
}
