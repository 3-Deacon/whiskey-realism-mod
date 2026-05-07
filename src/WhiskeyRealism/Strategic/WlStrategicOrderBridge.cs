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

        internal static WlStrategicOrderDecision TryIssue(WlStrategicOrderRequest request)
        {
            if (request == null)
            {
                return new WlStrategicOrderDecision(
                    WlStrategicOrderResult.DirectMovementAllowed,
                    wlOrderType: -1,
                    mayDirectMove: true,
                    mayMutateOperationList: true,
                    reason: "null-request");
            }

            var facts = BuildFacts(request);
            var decision = Classify(request.Intent, facts);
            if (decision.Result != WlStrategicOrderResult.IssuedWlCurrentOrder)
                return decision;

            int beforeSession = ReadGivenOrdersSession();
            object beforeOrder = ReadGivenOrder();

            try
            {
                AIBattle.CheckCurrentOrderUpdate(
                    request.Unit,
                    decision.WlOrderType,
                    request.TargetPosition,
                    string.IsNullOrEmpty(request.TargetName) ? "Objective" : request.TargetName,
                    -1f,
                    DefaultWidth(request.Intent, request.Width),
                    DefaultDepth(request.Intent, request.Depth),
                    calledfromcampaign: true);
            }
            catch (Exception ex)
            {
                LogVanillaBridgeFailure(request, ex);
                return Classify(request.Intent, facts, vanillaBridgeSucceeded: false);
            }

            int afterSession = ReadGivenOrdersSession();
            object afterOrder = ReadGivenOrder();
            if (beforeSession == afterSession && ReferenceEquals(beforeOrder, afterOrder))
                return Classify(request.Intent, facts, vanillaBridgeSucceeded: false);

            return decision;
        }

        internal static WlStrategicOrderDecision ClassifyOnly(WlStrategicOrderRequest request)
        {
            if (request == null)
            {
                return new WlStrategicOrderDecision(
                    WlStrategicOrderResult.DirectMovementAllowed,
                    wlOrderType: -1,
                    mayDirectMove: true,
                    mayMutateOperationList: true,
                    reason: "null-request");
            }
            return Classify(request.Intent, BuildFacts(request));
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
                return Allowed(WlStrategicOrderResult.DirectMovementAllowed, orderType, "part-of-player-unit-direct-for-c0c");

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

        internal static float DefaultWidth(WlStrategicIntent intent, float requestedWidth)
        {
            return requestedWidth > 0f ? requestedWidth : DefaultOrderWidth;
        }

        internal static float DefaultDepth(WlStrategicIntent intent, float requestedDepth)
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

        private static void LogVanillaBridgeFailure(WlStrategicOrderRequest request, Exception ex)
        {
            string source = string.IsNullOrEmpty(request.SourceSystem) ? string.Empty : " source=" + request.SourceSystem;
            string keySource = string.IsNullOrEmpty(request.SourceSystem) ? "unknown" : request.SourceSystem;
            WarnOnce(
                "wl-order-bridge:vanilla-call:" + keySource,
                "[W&LOrderBridge] vanilla CheckCurrentOrderUpdate failed" +
                source +
                " unit=" + SafeUnitName(request.Unit) +
                ": " + ex.Message);
        }

        private static string SafeUnitName(Regiment unit)
        {
            try { return unit != null ? unit.name : "null"; }
            catch { return "unknown"; }
        }

        private static void WarnOnce(string key, string message)
        {
            try
            {
                Type onceLogType = typeof(WlStrategicOrderBridge).Assembly.GetType("WhiskeyRealism.Util.OnceLog");
                MethodInfo warningMethod = onceLogType?.GetMethod(
                    "Warning",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(string), typeof(string) },
                    modifiers: null);
                warningMethod?.Invoke(null, new object[] { key, message });
            }
            catch
            {
            }
        }

        private static object ReadGivenOrder()
        {
            try { return DLC_WL.givenorder; }
            catch { return null; }
        }

        private static int ReadGivenOrdersSession()
        {
            try
            {
                Type givenOrdersType = typeof(DLC_WL).GetNestedType("GivenOrders", BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo sessionField = givenOrdersType?.GetField(
                    "givenorderssession",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (sessionField == null) return -1;
                return Convert.ToInt32(sessionField.GetValue(null));
            }
            catch
            {
                return -1;
            }
        }

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
    }
}
