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
            bool currentCommandParentIsUnderTargetUnit = false)
        {
            WlActive = wlActive;
            IsPlayerAlliance = isPlayerAlliance;
            IsPlayerCic = isPlayerCic;
            IsMovedByPlayer = isMovedByPlayer;
            IsUnderCommander = isUnderCommander;
            IsPartOfPlayerUnit = isPartOfPlayerUnit;
            CurrentCommandIsCampaignGroup = currentCommandIsCampaignGroup;
            CurrentCommandParentIsUnderTargetUnit = currentCommandParentIsUnderTargetUnit;
        }

        internal bool WlActive;
        internal bool IsPlayerAlliance;
        internal bool IsPlayerCic;
        internal bool IsMovedByPlayer;
        internal bool IsUnderCommander;
        internal bool IsPartOfPlayerUnit;
        internal bool CurrentCommandIsCampaignGroup;
        internal bool CurrentCommandParentIsUnderTargetUnit;
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

            try
            {
                Regiment current = GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
                Regiment campaignGroup = BattleUnits.GetCampaignGroup(current);
                Regiment parent = BattleUnits.GetParentUnit(current);

                currentCommandIsCampaignGroup = current != null && campaignGroup == current;
                currentCommandParentIsUnderTargetUnit =
                    parent != null &&
                    request.Unit != null &&
                    parent.transform.IsChildOf(request.Unit.transform);
            }
            catch
            {
                currentCommandIsCampaignGroup = false;
                currentCommandParentIsUnderTargetUnit = false;
            }

            return new WlStrategicRoleFacts(
                wlActive,
                isPlayerAlliance,
                isPlayerCic,
                isMovedByPlayer,
                isUnderCommander,
                isPartOfPlayerUnit,
                currentCommandIsCampaignGroup,
                currentCommandParentIsUnderTargetUnit);
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

            if (facts.IsPlayerCic)
                return Blocked(WlStrategicOrderResult.SkippedPlayerCic, orderType, "player-cic");

            if (facts.IsMovedByPlayer)
                return Blocked(WlStrategicOrderResult.SkippedPlayerControlled, orderType, "moved-by-player");

            if (facts.IsUnderCommander)
            {
                if (!facts.CurrentCommandIsCampaignGroup || !facts.CurrentCommandParentIsUnderTargetUnit)
                    return Blocked(WlStrategicOrderResult.WlCurrentOrderIneligible, orderType, "current-order-chain-ineligible");

                if (!vanillaBridgeSucceeded)
                    return Blocked(WlStrategicOrderResult.FailedVanillaBridge, orderType, "vanilla-bridge-failed");

                return Blocked(WlStrategicOrderResult.IssuedWlCurrentOrder, orderType, "issued-current-order");
            }

            if (facts.IsPartOfPlayerUnit)
                return Blocked(WlStrategicOrderResult.WlCurrentOrderIneligible, orderType, "part-of-player-unit");

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
