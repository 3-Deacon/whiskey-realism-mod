using System;
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
    //                  a W&L dispatch, and an unreliable executor: never pledged, packaged
    //                  or given a must-hold role, yet counted toward the coverage of the
    //                  area where its last waypoint lies (CountsTowardCoverage).
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

        // May Whiskey commit the unit: move, stop, re-task, pledge to the capital, put in a
        // defense package or give a must-hold role. False for the map-group player: an
        // unreliable executor whose command Whiskey never moves.
        public static bool MayMove(WlChainClass chain)
        {
            return chain == WlChainClass.None || chain == WlChainClass.CarryingGroup;
        }

        // Does the unit count toward coverage that already exists where its last waypoint
        // lies, as vanilla counts every own group at strength x morale (AICampaign.
        // AssignUnitToDefendCapital 11733-11740, no player exclusion). The map-group player
        // does, though MayMove is false for him: he is counted, never counted on. The
        // units under his command are inside the group that is counted, and a CIC side stands down.
        public static bool CountsTowardCoverage(WlChainClass chain)
        {
            return chain == WlChainClass.None || chain == WlChainClass.CarryingGroup || chain == WlChainClass.PlayerMapGroup;
        }

        // A unit Whiskey still addresses, by a W&L dispatch.
        public static bool OrderOnly(WlChainClass chain)
        {
            return chain == WlChainClass.PlayerMapGroup;
        }
    }

    // The W&L player's side of the chain, read once per pass (the per-unit facts
    // are in WlStrategicOrderBridge.FactsFor). Direct reads of vanilla statics, no
    // reflection: DLC_WL.dlc_scenarioactive, IsCommanderInChief (47475), the chosen
    // commander's currentcommand, its campaign group (82991) and parent (93883).
    internal struct WlChainContext
    {
        public bool WlActive;
        public int PlayerAlliance;
        public bool PlayerIsCic;
        public Regiment Command;
        public Regiment CampaignGroup;
        public Regiment CommandParent;
        public bool CommandIsCampaignGroup;

        // The player-level facts every unit's classification starts from.
        internal WlStrategicRoleFacts PlayerFacts()
        {
            return new WlStrategicRoleFacts { WlActive = WlActive, IsPlayerCic = PlayerIsCic };
        }

        internal static WlChainContext Capture()
        {
            var context = new WlChainContext { PlayerAlliance = -1 };
            try
            {
                context.WlActive = DLC_WL.dlc_scenarioactive;
                if (!context.WlActive) return context;
                context.PlayerAlliance = GameVars.playeralliance;
                context.PlayerIsCic = DLC_WL.IsCommanderInChief();
                int chosen = DLC_WL.dlc_chosencommander;
                if (chosen < 0 || GameVars.commander == null || chosen >= GameVars.commander.Count) return context;
                context.Command = GameVars.commander[chosen].currentcommand;
                if ((UnityEngine.Object)context.Command == null) return context;
                context.CampaignGroup = BattleUnits.GetCampaignGroup(context.Command);
                context.CommandParent = BattleUnits.GetParentUnit(context.Command);
                context.CommandIsCampaignGroup = (UnityEngine.Object)context.CampaignGroup == (UnityEngine.Object)context.Command;
            }
            catch
            {
            }
            return context;
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
            return ClassifyOnly(WlChainContext.Capture(), request.AllianceId, request.Unit, request.Intent);
        }

        // For loops: the caller captures the context once per pass.
        internal static WlStrategicOrderDecision ClassifyOnly(in WlChainContext context, int allianceId, Regiment unit, WlStrategicIntent intent)
        {
            return Classify(intent, FactsFor(in context, allianceId, unit));
        }

        // The unit's chain class for code that changes vanilla state without an
        // order of its own (capital pre-pledge, #25 revert, probe stop, coastal
        // runner, defense coverage). Loops capture the context once per pass.
        internal static WlChainClass ChainClassOf(int allianceId, Regiment unit)
        {
            return ChainClassOf(WlChainContext.Capture(), allianceId, unit);
        }

        internal static WlChainClass ChainClassOf(in WlChainContext context, int allianceId, Regiment unit)
        {
            if (unit == null || !context.WlActive) return WlChainClass.None;
            return WlChainRules.Classify(FactsFor(in context, allianceId, unit));
        }

        private static WlStrategicRoleFacts BuildFacts(WlStrategicOrderRequest request)
        {
            return FactsFor(WlChainContext.Capture(), request.AllianceId, request.Unit);
        }

        // Per-unit facts: unit fields and vanilla's own DLC_WL helpers only, no
        // reflection (the player-level facts come from the context).
        private static WlStrategicRoleFacts FactsFor(in WlChainContext context, int allianceId, Regiment unit)
        {
            var facts = context.PlayerFacts();
            if (unit == null) return facts;
            try
            {
                facts.IsPlayerAlliance = unit.alliance == context.PlayerAlliance && allianceId == context.PlayerAlliance;
                if (!context.WlActive || !facts.IsPlayerAlliance) return facts;
                facts.IsMovedByPlayer = DLC_WL.IsMovedByPlayer(unit);
                facts.IsUnderCommander = unit.dlcw_isundercommander;
                facts.IsPartOfPlayerUnit = DLC_WL.IsPlayerPartOfUnit(unit);
                facts.CurrentCommandIsCampaignGroup = context.CommandIsCampaignGroup;
                facts.IsPlayerCampaignGroup = (UnityEngine.Object)context.Command != null && (UnityEngine.Object)context.CampaignGroup == (UnityEngine.Object)unit;
                facts.CurrentCommandParentIsUnderTargetUnit = (UnityEngine.Object)context.CommandParent != null
                    && context.CommandParent.transform.IsChildOf(unit.transform);
            }
            catch
            {
                facts.IsMovedByPlayer = false;
                facts.IsUnderCommander = false;
                facts.IsPartOfPlayerUnit = false;
                facts.IsPlayerCampaignGroup = false;
                facts.CurrentCommandParentIsUnderTargetUnit = false;
            }
            return facts;
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
