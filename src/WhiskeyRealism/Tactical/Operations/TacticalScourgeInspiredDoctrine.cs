using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalOutboundCourierState
    {
        None = 0,
        IssueCourier = 1,
        Cooldown = 2,
        ChildAlreadyOrdered = 3,
        PlayerControlled = 4,
        NoCommanderOrder = 5,
        ObjectiveContinuation = 6
    }

    public readonly struct TacticalOutboundCourierInput
    {
        public TacticalOutboundCourierInput(
            string parentNodeId,
            string childNodeId,
            float nowSeconds,
            float lastCourierAtSeconds,
            bool childHasOrders,
            bool commanderHasOrder,
            bool commanderHasPlay,
            bool childIsPlayerControlled,
            float courierIntervalSeconds,
            bool allowObjectiveMovementContinuation = false,
            bool childActiveMove = false)
        {
            ParentNodeId = string.IsNullOrWhiteSpace(parentNodeId) ? "parent-unknown" : parentNodeId.Trim();
            ChildNodeId = string.IsNullOrWhiteSpace(childNodeId) ? "child-unknown" : childNodeId.Trim();
            NowSeconds = SanitizeNonNegative(nowSeconds);
            LastCourierAtSeconds = SanitizeNonNegative(lastCourierAtSeconds);
            ChildHasOrders = childHasOrders;
            CommanderHasOrder = commanderHasOrder;
            CommanderHasPlay = commanderHasPlay;
            ChildIsPlayerControlled = childIsPlayerControlled;
            CourierIntervalSeconds = SanitizePositive(courierIntervalSeconds, 900f);
            AllowObjectiveMovementContinuation = allowObjectiveMovementContinuation;
            ChildActiveMove = childActiveMove;
        }

        public string ParentNodeId { get; }
        public string ChildNodeId { get; }
        public float NowSeconds { get; }
        public float LastCourierAtSeconds { get; }
        public bool ChildHasOrders { get; }
        public bool CommanderHasOrder { get; }
        public bool CommanderHasPlay { get; }
        public bool ChildIsPlayerControlled { get; }
        public float CourierIntervalSeconds { get; }
        public bool AllowObjectiveMovementContinuation { get; }
        public bool ChildActiveMove { get; }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static float SanitizePositive(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalOutboundCourierDecision
    {
        public TacticalOutboundCourierDecision(bool allowIssue, TacticalOutboundCourierState state, string reason)
        {
            AllowIssue = allowIssue;
            State = state;
            Reason = string.IsNullOrWhiteSpace(reason) ? "courier-unspecified" : reason.Trim();
        }

        public bool AllowIssue { get; }
        public TacticalOutboundCourierState State { get; }
        public string Reason { get; }
    }

    public static class TacticalOutboundCourierCadence
    {
        public static TacticalOutboundCourierDecision Decide(TacticalOutboundCourierInput input)
        {
            if (input.ChildIsPlayerControlled)
                return new TacticalOutboundCourierDecision(false, TacticalOutboundCourierState.PlayerControlled, "player-protected");
            if (!input.CommanderHasOrder || !input.CommanderHasPlay)
                return new TacticalOutboundCourierDecision(false, TacticalOutboundCourierState.NoCommanderOrder, "no-commander-play");
            if (input.ChildHasOrders)
                return new TacticalOutboundCourierDecision(false, TacticalOutboundCourierState.ChildAlreadyOrdered, "child-already-ordered");
            if (input.LastCourierAtSeconds > 0f &&
                input.NowSeconds < input.LastCourierAtSeconds + input.CourierIntervalSeconds)
            {
                if (input.AllowObjectiveMovementContinuation && !input.ChildActiveMove)
                {
                    return new TacticalOutboundCourierDecision(
                        true,
                        TacticalOutboundCourierState.ObjectiveContinuation,
                        "objective-continuation");
                }

                return new TacticalOutboundCourierDecision(false, TacticalOutboundCourierState.Cooldown, "courier-cooldown");
            }

            return new TacticalOutboundCourierDecision(true, TacticalOutboundCourierState.IssueCourier, "courier-open");
        }
    }

    public enum TacticalOutboundOrderLedgerState
    {
        Issue = 0,
        ActiveMove = 1,
        DuplicatePendingOrder = 2,
        CommandChanged = 3
    }

    public readonly struct TacticalOutboundOrderLedgerInput
    {
        public TacticalOutboundOrderLedgerInput(
            bool childHasOrders,
            bool duplicatePendingSignature,
            bool activeMove,
            bool commandChanged)
        {
            ChildHasOrders = childHasOrders;
            DuplicatePendingSignature = duplicatePendingSignature;
            ActiveMove = activeMove;
            CommandChanged = commandChanged;
        }

        public bool ChildHasOrders { get; }
        public bool DuplicatePendingSignature { get; }
        public bool ActiveMove { get; }
        public bool CommandChanged { get; }
    }

    public readonly struct TacticalOutboundOrderLedgerDecision
    {
        public TacticalOutboundOrderLedgerDecision(
            bool allowIssue,
            TacticalOutboundOrderLedgerState state,
            string reason)
        {
            AllowIssue = allowIssue;
            State = state;
            Reason = string.IsNullOrWhiteSpace(reason) ? "outbound-order" : reason.Trim();
        }

        public bool AllowIssue { get; }
        public TacticalOutboundOrderLedgerState State { get; }
        public string Reason { get; }
    }

    public static class TacticalOutboundOrderLedger
    {
        public static TacticalOutboundOrderLedgerDecision Decide(TacticalOutboundOrderLedgerInput input)
        {
            if (input.ActiveMove)
                return new TacticalOutboundOrderLedgerDecision(false, TacticalOutboundOrderLedgerState.ActiveMove, "active-move");

            if (input.ChildHasOrders && input.DuplicatePendingSignature && !input.CommandChanged)
            {
                return new TacticalOutboundOrderLedgerDecision(
                    false,
                    TacticalOutboundOrderLedgerState.DuplicatePendingOrder,
                    "duplicate-pending-order");
            }

            if (input.ChildHasOrders && input.CommandChanged)
                return new TacticalOutboundOrderLedgerDecision(true, TacticalOutboundOrderLedgerState.CommandChanged, "command-changed");

            return new TacticalOutboundOrderLedgerDecision(true, TacticalOutboundOrderLedgerState.Issue, "issue");
        }
    }

    public readonly struct TacticalDivisionPlaySubordinate
    {
        public TacticalDivisionPlaySubordinate(
            string nodeId,
            CommandNodeRole role,
            bool hasTargets,
            bool engagingEnemy,
            bool underCloseFire,
            bool inTrouble,
            bool isArtillery,
            float enemyDistance,
            bool hasOrders)
        {
            NodeId = string.IsNullOrWhiteSpace(nodeId) ? "node-unknown" : nodeId.Trim();
            Role = role;
            HasTargets = hasTargets;
            EngagingEnemy = engagingEnemy;
            UnderCloseFire = underCloseFire;
            InTrouble = inTrouble;
            IsArtillery = isArtillery;
            EnemyDistance = IsFinite(enemyDistance) && enemyDistance > 0f ? enemyDistance : float.MaxValue;
            HasOrders = hasOrders;
        }

        public string NodeId { get; }
        public CommandNodeRole Role { get; }
        public bool HasTargets { get; }
        public bool EngagingEnemy { get; }
        public bool UnderCloseFire { get; }
        public bool InTrouble { get; }
        public bool IsArtillery { get; }
        public float EnemyDistance { get; }
        public bool HasOrders { get; }
        public bool IsEngagedCandidate { get { return HasTargets || EngagingEnemy || UnderCloseFire; } }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalDivisionPlayInput
    {
        public TacticalDivisionPlayInput(
            string parentNodeId,
            float nowSeconds,
            IReadOnlyList<TacticalDivisionPlaySubordinate> subordinates)
        {
            ParentNodeId = string.IsNullOrWhiteSpace(parentNodeId) ? "parent-unknown" : parentNodeId.Trim();
            NowSeconds = IsFinite(nowSeconds) && nowSeconds > 0f ? nowSeconds : 0f;
            Subordinates = subordinates ?? Array.Empty<TacticalDivisionPlaySubordinate>();
        }

        public string ParentNodeId { get; }
        public float NowSeconds { get; }
        public IReadOnlyList<TacticalDivisionPlaySubordinate> Subordinates { get; }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalDivisionPlayOrder
    {
        public TacticalDivisionPlayOrder(
            string nodeId,
            string parentNodeId,
            CommandTaskType task,
            TacticalOrderDelivery delivery,
            string reason)
        {
            NodeId = string.IsNullOrWhiteSpace(nodeId) ? "node-unknown" : nodeId.Trim();
            ParentNodeId = string.IsNullOrWhiteSpace(parentNodeId) ? "parent-unknown" : parentNodeId.Trim();
            Task = task;
            Delivery = delivery;
            Reason = string.IsNullOrWhiteSpace(reason) ? "division-play" : reason.Trim();
        }

        public string NodeId { get; }
        public string ParentNodeId { get; }
        public CommandTaskType Task { get; }
        public TacticalOrderDelivery Delivery { get; }
        public string Reason { get; }
        public bool HasOrder { get { return Task != CommandTaskType.None; } }
    }

    public readonly struct TacticalDivisionPlayDecision
    {
        public TacticalDivisionPlayDecision(
            string parentNodeId,
            string anchorNodeId,
            bool helpRequested,
            IReadOnlyList<TacticalDivisionPlayOrder> orders)
        {
            ParentNodeId = string.IsNullOrWhiteSpace(parentNodeId) ? "parent-unknown" : parentNodeId.Trim();
            AnchorNodeId = string.IsNullOrWhiteSpace(anchorNodeId) ? string.Empty : anchorNodeId.Trim();
            HelpRequested = helpRequested;
            Orders = orders ?? Array.Empty<TacticalDivisionPlayOrder>();
        }

        public string ParentNodeId { get; }
        public string AnchorNodeId { get; }
        public bool HelpRequested { get; }
        public IReadOnlyList<TacticalDivisionPlayOrder> Orders { get; }
        public bool HasAnchor { get { return !string.IsNullOrWhiteSpace(AnchorNodeId); } }

        public TacticalDivisionPlayOrder OrderFor(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return default;
            string key = nodeId.Trim();
            for (int i = 0; i < Orders.Count; i++)
            {
                if (string.Equals(Orders[i].NodeId, key, StringComparison.Ordinal))
                    return Orders[i];
            }

            return default;
        }
    }

    public static class TacticalDivisionPlayExecutor
    {
        public static TacticalDivisionPlayDecision Decide(TacticalDivisionPlayInput input)
        {
            int anchorIndex = -1;
            float bestDistance = float.MaxValue;
            bool help = false;
            for (int i = 0; i < input.Subordinates.Count; i++)
            {
                TacticalDivisionPlaySubordinate child = input.Subordinates[i];
                help = help || child.InTrouble;
                if (!child.IsEngagedCandidate) continue;

                float scoreDistance = child.EnemyDistance;
                if (child.UnderCloseFire) scoreDistance -= 50f;
                if (child.IsArtillery) scoreDistance += 75f;
                if (scoreDistance >= bestDistance) continue;

                bestDistance = scoreDistance;
                anchorIndex = i;
            }

            if (anchorIndex < 0)
                return new TacticalDivisionPlayDecision(input.ParentNodeId, string.Empty, help, Array.Empty<TacticalDivisionPlayOrder>());

            TacticalDivisionPlaySubordinate anchor = input.Subordinates[anchorIndex];
            var orders = new List<TacticalDivisionPlayOrder>(input.Subordinates.Count);
            for (int i = 0; i < input.Subordinates.Count; i++)
            {
                TacticalDivisionPlaySubordinate child = input.Subordinates[i];
                if (child.HasOrders && child.NodeId != anchor.NodeId) continue;

                CommandTaskType task = SelectTask(child, anchor.NodeId);
                string reason = child.NodeId == anchor.NodeId
                    ? "division-play-anchor"
                    : "division-play-slot";
                orders.Add(new TacticalDivisionPlayOrder(child.NodeId, input.ParentNodeId, task, TacticalOrderDelivery.Courier, reason));
            }

            return new TacticalDivisionPlayDecision(input.ParentNodeId, anchor.NodeId, help, orders);
        }

        private static CommandTaskType SelectTask(TacticalDivisionPlaySubordinate child, string anchorNodeId)
        {
            if (child.Role == CommandNodeRole.Reserve)
                return CommandTaskType.ReserveWait;
            if (string.Equals(child.NodeId, anchorNodeId, StringComparison.Ordinal))
                return CommandTaskType.AttackObjective;
            if (child.Role == CommandNodeRole.FixingForce)
                return CommandTaskType.FixEnemy;
            if (child.Role == CommandNodeRole.ScreeningForce || child.Role == CommandNodeRole.Probe)
                return CommandTaskType.Screen;
            if (child.Role == CommandNodeRole.FallbackGuard)
                return CommandTaskType.FallBackToLine;
            return CommandTaskType.SupportAttack;
        }
    }

    public enum TacticalCavalryFollowMode
    {
        None = 0,
        Guard = 1,
        Scout = 2,
        Screen = 3,
        Raid = 4
    }

    public enum TacticalCavalryFollowAction
    {
        None = 0,
        ClearFollowTarget = 1,
        MoveBehindTarget = 2,
        RequestScoutLocation = 3,
        GetAway = 4,
        ChargeRaidTarget = 5,
        MoveToFollowTarget = 6
    }

    public readonly struct TacticalCavalryFollowInput
    {
        public TacticalCavalryFollowInput(
            TacticalCavalryFollowMode mode,
            bool hasFollowTarget,
            bool targetHidden,
            bool targetIsOfficer,
            bool targetInFort,
            bool targetInSquare,
            bool targetHasInfantryOrArtillerySupport,
            bool canChargeTarget,
            bool leaderAllowsCharge,
            float fearAdvantage01,
            bool enemyClose,
            float enemyDistance,
            float longRange,
            bool atFollowLocation)
        {
            Mode = mode;
            HasFollowTarget = hasFollowTarget;
            TargetHidden = targetHidden;
            TargetIsOfficer = targetIsOfficer;
            TargetInFort = targetInFort;
            TargetInSquare = targetInSquare;
            TargetHasInfantryOrArtillerySupport = targetHasInfantryOrArtillerySupport;
            CanChargeTarget = canChargeTarget;
            LeaderAllowsCharge = leaderAllowsCharge;
            FearAdvantage01 = Clamp01(fearAdvantage01);
            EnemyClose = enemyClose;
            EnemyDistance = SanitizeNonNegative(enemyDistance);
            LongRange = SanitizePositive(longRange, 300f);
            AtFollowLocation = atFollowLocation;
        }

        public TacticalCavalryFollowMode Mode { get; }
        public bool HasFollowTarget { get; }
        public bool TargetHidden { get; }
        public bool TargetIsOfficer { get; }
        public bool TargetInFort { get; }
        public bool TargetInSquare { get; }
        public bool TargetHasInfantryOrArtillerySupport { get; }
        public bool CanChargeTarget { get; }
        public bool LeaderAllowsCharge { get; }
        public float FearAdvantage01 { get; }
        public bool EnemyClose { get; }
        public float EnemyDistance { get; }
        public float LongRange { get; }
        public bool AtFollowLocation { get; }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static float SanitizePositive(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalCavalryFollowDecision
    {
        public TacticalCavalryFollowDecision(TacticalCavalryFollowAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "cavalry-follow" : reason.Trim();
        }

        public TacticalCavalryFollowAction Action { get; }
        public string Reason { get; }
    }

    public static class TacticalCavalryFollowDoctrine
    {
        public static TacticalCavalryFollowDecision Decide(TacticalCavalryFollowInput input)
        {
            if (input.HasFollowTarget &&
                input.TargetHidden &&
                input.Mode != TacticalCavalryFollowMode.Guard)
            {
                return new TacticalCavalryFollowDecision(
                    TacticalCavalryFollowAction.ClearFollowTarget,
                    "follow-target-hidden");
            }

            switch (input.Mode)
            {
                case TacticalCavalryFollowMode.Guard:
                    if (!input.HasFollowTarget)
                        return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.RequestScoutLocation, "guard-needs-target");
                    if (input.EnemyClose && input.CanChargeTarget && input.LeaderAllowsCharge)
                        return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.ChargeRaidTarget, "guard-charge-attacker");
                    return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.MoveBehindTarget, "guard-behind-target");
                case TacticalCavalryFollowMode.Scout:
                    return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.RequestScoutLocation, "scout-follow");
                case TacticalCavalryFollowMode.Screen:
                    if (input.EnemyClose && input.EnemyDistance <= input.LongRange)
                        return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.GetAway, "screen-get-away");
                    return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.RequestScoutLocation, "screen-follow");
                case TacticalCavalryFollowMode.Raid:
                    if (!input.HasFollowTarget)
                        return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.RequestScoutLocation, "raid-needs-target");
                    if (!RaidTargetAllowed(input))
                        return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.ClearFollowTarget, "raid-target-filtered");
                    return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.ChargeRaidTarget, "raid-charge-target");
                default:
                    return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.None, "no-cavalry-follow");
            }
        }

        private static bool RaidTargetAllowed(TacticalCavalryFollowInput input)
        {
            if (input.TargetHidden || input.TargetIsOfficer || input.TargetInFort || input.TargetInSquare)
                return false;
            if (input.TargetHasInfantryOrArtillerySupport)
                return false;
            if (!input.CanChargeTarget || !input.LeaderAllowsCharge)
                return false;
            return input.FearAdvantage01 >= 0.50f;
        }
    }

    public readonly struct TacticalGrandTacticianReconInput
    {
        public TacticalGrandTacticianReconInput(
            CommandNodeRole role,
            CommandTaskType task,
            bool hasCavalryCapability,
            bool hasFowVisibleEnemy,
            bool underRecentFire)
        {
            Role = role;
            Task = task;
            HasCavalryCapability = hasCavalryCapability;
            HasFowVisibleEnemy = hasFowVisibleEnemy;
            UnderRecentFire = underRecentFire;
        }

        public CommandNodeRole Role { get; }
        public CommandTaskType Task { get; }
        public bool HasCavalryCapability { get; }
        public bool HasFowVisibleEnemy { get; }
        public bool UnderRecentFire { get; }
    }

    public readonly struct TacticalGrandTacticianReconDecision
    {
        public TacticalGrandTacticianReconDecision(CommandTaskType task, string reason)
        {
            Task = task;
            Reason = string.IsNullOrWhiteSpace(reason) ? "gt-recon" : reason.Trim();
        }

        public CommandTaskType Task { get; }
        public string Reason { get; }
    }

    public static class TacticalGrandTacticianReconDoctrine
    {
        public static TacticalGrandTacticianReconDecision Decide(TacticalGrandTacticianReconInput input)
        {
            if (!IsReconOrAssaultTask(input.Task))
                return new TacticalGrandTacticianReconDecision(input.Task, "not-recon-task");

            if (input.HasFowVisibleEnemy || input.UnderRecentFire)
                return new TacticalGrandTacticianReconDecision(input.Task, "contact-confirmed");

            if (input.HasCavalryCapability)
            {
                if (IsScreenRole(input.Role) || input.Task == CommandTaskType.Screen)
                    return new TacticalGrandTacticianReconDecision(CommandTaskType.Screen, "cavalry-screen-fow");

                return new TacticalGrandTacticianReconDecision(CommandTaskType.Scout, "cavalry-scout-fow");
            }

            if (IsScreenRole(input.Role) || input.Task == CommandTaskType.Screen)
                return new TacticalGrandTacticianReconDecision(CommandTaskType.Screen, "infantry-screen-by-bounds");

            return new TacticalGrandTacticianReconDecision(CommandTaskType.Probe, "infantry-probe-by-bounds");
        }

        private static bool IsReconOrAssaultTask(CommandTaskType task)
        {
            return task == CommandTaskType.Scout ||
                task == CommandTaskType.Probe ||
                task == CommandTaskType.Screen ||
                IsAssaultTask(task);
        }

        private static bool IsAssaultTask(CommandTaskType task)
        {
            return task == CommandTaskType.AttackObjective ||
                task == CommandTaskType.SupportAttack ||
                task == CommandTaskType.FixEnemy;
        }

        private static bool IsScreenRole(CommandNodeRole role)
        {
            return role == CommandNodeRole.ScreeningForce ||
                role == CommandNodeRole.FixingForce ||
                role == CommandNodeRole.FallbackGuard;
        }
    }

    public enum TacticalArtilleryLimberState
    {
        Unknown = 0,
        Limbered = 1,
        Unlimbered = 2,
        Unlimbering = 3
    }

    public enum TacticalArtilleryMicroAction
    {
        Preserve = 0,
        Limber = 1,
        Retreat = 2,
        Unlimber = 3,
        WheelToBestThreat = 4,
        Fire = 5,
        ConserveAmmo = 6
    }

    public readonly struct TacticalArtilleryMicroInput
    {
        public TacticalArtilleryMicroInput(
            bool hasEnemy,
            float enemyCloseDistance,
            float panicDistance,
            int supportCount,
            bool moraleBonus,
            bool enemyRoutedOrRetreating,
            TacticalArtilleryLimberState limberState,
            float ammoRatio01,
            float targetDistance,
            float maxRange,
            float currentQuadrantThreat,
            float bestQuadrantThreat,
            bool canWheel,
            bool frontalTargetInRange)
        {
            HasEnemy = hasEnemy;
            EnemyCloseDistance = SanitizeNonNegative(enemyCloseDistance);
            PanicDistance = SanitizePositive(panicDistance, 140f);
            SupportCount = supportCount < 0 ? 0 : supportCount;
            MoraleBonus = moraleBonus;
            EnemyRoutedOrRetreating = enemyRoutedOrRetreating;
            LimberState = limberState;
            AmmoRatio01 = Clamp01(ammoRatio01);
            TargetDistance = SanitizeNonNegative(targetDistance);
            MaxRange = SanitizePositive(maxRange, 1f);
            CurrentQuadrantThreat = SanitizeNonNegative(currentQuadrantThreat);
            BestQuadrantThreat = SanitizeNonNegative(bestQuadrantThreat);
            CanWheel = canWheel;
            FrontalTargetInRange = frontalTargetInRange;
        }

        public bool HasEnemy { get; }
        public float EnemyCloseDistance { get; }
        public float PanicDistance { get; }
        public int SupportCount { get; }
        public bool MoraleBonus { get; }
        public bool EnemyRoutedOrRetreating { get; }
        public TacticalArtilleryLimberState LimberState { get; }
        public float AmmoRatio01 { get; }
        public float TargetDistance { get; }
        public float MaxRange { get; }
        public float CurrentQuadrantThreat { get; }
        public float BestQuadrantThreat { get; }
        public bool CanWheel { get; }
        public bool FrontalTargetInRange { get; }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static float SanitizePositive(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalArtilleryMicroDecision
    {
        public TacticalArtilleryMicroDecision(
            TacticalArtilleryMicroAction action,
            TacticalArtilleryAmmoMission ammoMission,
            string reason)
        {
            Action = action;
            AmmoMission = ammoMission;
            Reason = string.IsNullOrWhiteSpace(reason) ? "artillery-micro" : reason.Trim();
        }

        public TacticalArtilleryMicroAction Action { get; }
        public TacticalArtilleryAmmoMission AmmoMission { get; }
        public string Reason { get; }
    }

    public static class TacticalArtilleryMicroDoctrine
    {
        public static TacticalArtilleryMicroDecision Decide(TacticalArtilleryMicroInput input)
        {
            TacticalArtilleryAmmoMission ammo = SelectAmmo(input);
            if (!input.HasEnemy)
                return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.Preserve, TacticalArtilleryAmmoMission.Preserve, "no-enemy");
            if (input.AmmoRatio01 < 0.10f)
                return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.ConserveAmmo, TacticalArtilleryAmmoMission.Preserve, "low-ammo");
            if (!input.EnemyRoutedOrRetreating &&
                input.EnemyCloseDistance > 0f &&
                input.EnemyCloseDistance <= input.PanicDistance &&
                input.SupportCount <= 0 &&
                !input.MoraleBonus)
            {
                if (input.LimberState == TacticalArtilleryLimberState.Limbered)
                    return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.Retreat, TacticalArtilleryAmmoMission.Preserve, "unsupported-close-retreat");
                return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.Limber, TacticalArtilleryAmmoMission.Preserve, "unsupported-close-limber");
            }
            if (input.LimberState == TacticalArtilleryLimberState.Limbered ||
                input.LimberState == TacticalArtilleryLimberState.Unknown)
            {
                if (input.TargetDistance <= input.MaxRange)
                    return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.Unlimber, ammo, "unlimber-to-fire");
            }
            if (input.CanWheel &&
                (!input.FrontalTargetInRange ||
                 input.BestQuadrantThreat > input.CurrentQuadrantThreat + 1000f))
            {
                return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.WheelToBestThreat, ammo, "wheel-best-threat");
            }
            if (input.TargetDistance <= input.MaxRange)
                return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.Fire, ammo, "fire");
            return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.Preserve, TacticalArtilleryAmmoMission.Preserve, "out-of-range");
        }

        private static TacticalArtilleryAmmoMission SelectAmmo(TacticalArtilleryMicroInput input)
        {
            if (input.TargetDistance <= 150f)
                return TacticalArtilleryAmmoMission.Canister;
            if (input.TargetDistance <= input.MaxRange * 0.35f)
                return TacticalArtilleryAmmoMission.Shrapnel;
            if (input.TargetDistance <= input.MaxRange * 0.50f)
                return TacticalArtilleryAmmoMission.Shell;
            return TacticalArtilleryAmmoMission.SolidShot;
        }
    }

    public enum TacticalCloseCombatKind
    {
        Infantry = 0,
        Cavalry = 1,
        Artillery = 2,
        Skirmisher = 3
    }

    public enum TacticalMeleeFearPosture
    {
        NoOpinion = 0,
        Hold = 1,
        ChargeAllowed = 2,
        ChargeEncouraged = 3,
        FallbackPressure = 4
    }

    public readonly struct TacticalMeleeFearInput
    {
        public TacticalMeleeFearInput(
            TacticalCloseCombatKind ownKind,
            TacticalCloseCombatKind enemyKind,
            float ownMen,
            float enemyMen,
            float ownMorale01,
            float enemyMorale01,
            float leaderAggression01,
            float flanking01,
            float rearThreat01,
            float ownHighGround01,
            float enemyHighGround01,
            float enemyDefensiveTerrain01,
            float ownUnderFire01,
            float enemyUnderFire01,
            bool ownChargingOrRunning,
            bool targetRouted)
        {
            OwnKind = ownKind;
            EnemyKind = enemyKind;
            OwnMen = SanitizeNonNegative(ownMen);
            EnemyMen = SanitizeNonNegative(enemyMen);
            OwnMorale01 = Clamp01(ownMorale01);
            EnemyMorale01 = Clamp01(enemyMorale01);
            LeaderAggression01 = Clamp01(leaderAggression01);
            Flanking01 = Clamp01(flanking01);
            RearThreat01 = Clamp01(rearThreat01);
            OwnHighGround01 = Clamp01(ownHighGround01);
            EnemyHighGround01 = Clamp01(enemyHighGround01);
            EnemyDefensiveTerrain01 = Clamp01(enemyDefensiveTerrain01);
            OwnUnderFire01 = Clamp01(ownUnderFire01);
            EnemyUnderFire01 = Clamp01(enemyUnderFire01);
            OwnChargingOrRunning = ownChargingOrRunning;
            TargetRouted = targetRouted;
        }

        public TacticalCloseCombatKind OwnKind { get; }
        public TacticalCloseCombatKind EnemyKind { get; }
        public float OwnMen { get; }
        public float EnemyMen { get; }
        public float OwnMorale01 { get; }
        public float EnemyMorale01 { get; }
        public float LeaderAggression01 { get; }
        public float Flanking01 { get; }
        public float RearThreat01 { get; }
        public float OwnHighGround01 { get; }
        public float EnemyHighGround01 { get; }
        public float EnemyDefensiveTerrain01 { get; }
        public float OwnUnderFire01 { get; }
        public float EnemyUnderFire01 { get; }
        public bool OwnChargingOrRunning { get; }
        public bool TargetRouted { get; }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalMeleeFearDecision
    {
        public TacticalMeleeFearDecision(
            TacticalMeleeFearPosture posture,
            float advantageRatio,
            bool allowsCharge,
            bool encouragesCharge,
            bool requiresFallbackPressure,
            string reason)
        {
            Posture = posture;
            AdvantageRatio = IsFinite(advantageRatio) && advantageRatio > 0f ? advantageRatio : 0f;
            AllowsCharge = allowsCharge;
            EncouragesCharge = encouragesCharge;
            RequiresFallbackPressure = requiresFallbackPressure;
            Reason = string.IsNullOrWhiteSpace(reason) ? "melee-fear" : reason.Trim();
        }

        public TacticalMeleeFearPosture Posture { get; }
        public float AdvantageRatio { get; }
        public bool AllowsCharge { get; }
        public bool EncouragesCharge { get; }
        public bool RequiresFallbackPressure { get; }
        public string Reason { get; }
        public bool HasOpinion { get { return Posture != TacticalMeleeFearPosture.NoOpinion; } }

        public static TacticalMeleeFearDecision NoOpinion
        {
            get
            {
                return new TacticalMeleeFearDecision(
                    TacticalMeleeFearPosture.NoOpinion,
                    0f,
                    false,
                    false,
                    false,
                    "no-melee-fear-opinion");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class TacticalMeleeFearDoctrine
    {
        public static TacticalMeleeFearDecision Decide(TacticalMeleeFearInput input)
        {
            if (input.OwnMen <= 0f || input.EnemyMen <= 0f)
                return TacticalMeleeFearDecision.NoOpinion;

            float own = input.OwnMen * KindWeight(input.OwnKind);
            float enemy = input.EnemyMen * KindWeight(input.EnemyKind);
            own *= 0.65f + input.OwnMorale01;
            enemy *= 0.65f + input.EnemyMorale01;

            own *= 1f + (input.LeaderAggression01 * 0.25f);
            own *= 1f + (input.Flanking01 * 0.40f) + (input.RearThreat01 * 0.55f);
            own *= 1f + (input.OwnHighGround01 * 0.25f);
            own *= 1f - (input.OwnUnderFire01 * 0.25f);
            if (input.OwnChargingOrRunning) own *= 1.12f;
            if (input.TargetRouted) own *= 1.35f;

            enemy *= 1f + (input.EnemyHighGround01 * 0.20f) + (input.EnemyDefensiveTerrain01 * 0.45f);
            enemy *= 1f - (input.EnemyUnderFire01 * 0.20f);

            own = Math.Max(1f, own);
            enemy = Math.Max(1f, enemy);
            float ratio = own / enemy;

            if (ratio >= 1.90f)
            {
                return new TacticalMeleeFearDecision(
                    TacticalMeleeFearPosture.ChargeEncouraged,
                    ratio,
                    true,
                    true,
                    false,
                    "fear-overmatch");
            }
            if (ratio >= 1.25f)
            {
                return new TacticalMeleeFearDecision(
                    TacticalMeleeFearPosture.ChargeAllowed,
                    ratio,
                    true,
                    false,
                    false,
                    "fear-advantage");
            }
            if (ratio <= 0.75f)
            {
                return new TacticalMeleeFearDecision(
                    TacticalMeleeFearPosture.FallbackPressure,
                    ratio,
                    false,
                    false,
                    true,
                    "fear-disadvantage");
            }

            return new TacticalMeleeFearDecision(
                TacticalMeleeFearPosture.Hold,
                ratio,
                false,
                false,
                false,
                "fear-hold");
        }

        private static float KindWeight(TacticalCloseCombatKind kind)
        {
            switch (kind)
            {
                case TacticalCloseCombatKind.Cavalry:
                    return 1.45f;
                case TacticalCloseCombatKind.Artillery:
                    return 0.25f;
                case TacticalCloseCombatKind.Skirmisher:
                    return 0.35f;
                default:
                    return 1f;
            }
        }
    }

    public enum TacticalSkirmisherAction
    {
        None = 0,
        HoldScreen = 1,
        RecallToParent = 2,
        RetreatToParent = 3
    }

    public readonly struct TacticalSkirmisherInput
    {
        public TacticalSkirmisherInput(
            bool hasParent,
            bool parentMoving,
            bool hasEnemyTarget,
            float enemyDistance,
            float parentDistance,
            float maxParentDistance,
            float ammo01,
            float fatigue01,
            float morale01,
            bool underCloseThreat)
        {
            HasParent = hasParent;
            ParentMoving = parentMoving;
            HasEnemyTarget = hasEnemyTarget;
            EnemyDistance = SanitizeNonNegative(enemyDistance);
            ParentDistance = SanitizeNonNegative(parentDistance);
            MaxParentDistance = SanitizePositive(maxParentDistance, 300f);
            Ammo01 = Clamp01(ammo01);
            Fatigue01 = Clamp01(fatigue01);
            Morale01 = Clamp01(morale01);
            UnderCloseThreat = underCloseThreat;
        }

        public bool HasParent { get; }
        public bool ParentMoving { get; }
        public bool HasEnemyTarget { get; }
        public float EnemyDistance { get; }
        public float ParentDistance { get; }
        public float MaxParentDistance { get; }
        public float Ammo01 { get; }
        public float Fatigue01 { get; }
        public float Morale01 { get; }
        public bool UnderCloseThreat { get; }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static float SanitizePositive(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalSkirmisherDecision
    {
        public TacticalSkirmisherDecision(TacticalSkirmisherAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "skirmisher" : reason.Trim();
        }

        public TacticalSkirmisherAction Action { get; }
        public string Reason { get; }
    }

    public static class TacticalSkirmisherDoctrine
    {
        public static TacticalSkirmisherDecision Decide(TacticalSkirmisherInput input)
        {
            if (!input.HasParent)
                return new TacticalSkirmisherDecision(TacticalSkirmisherAction.RetreatToParent, "missing-parent");
            if (input.UnderCloseThreat || input.Morale01 < 0.30f)
                return new TacticalSkirmisherDecision(TacticalSkirmisherAction.RetreatToParent, "close-threat");
            if (input.ParentDistance > input.MaxParentDistance)
                return new TacticalSkirmisherDecision(TacticalSkirmisherAction.RecallToParent, "parent-distance");
            if (input.Ammo01 < 0.20f)
                return new TacticalSkirmisherDecision(TacticalSkirmisherAction.RecallToParent, "low-ammo");
            if (input.Fatigue01 >= 0.75f)
                return new TacticalSkirmisherDecision(TacticalSkirmisherAction.RecallToParent, "fatigue");
            if (input.ParentMoving && !input.HasEnemyTarget)
                return new TacticalSkirmisherDecision(TacticalSkirmisherAction.RecallToParent, "parent-moving");
            if (!input.HasEnemyTarget)
                return new TacticalSkirmisherDecision(TacticalSkirmisherAction.RecallToParent, "no-target");
            return new TacticalSkirmisherDecision(TacticalSkirmisherAction.HoldScreen, "screen-hold");
        }
    }

    public enum TacticalInfantryFireAction
    {
        None = 0,
        HoldFire = 1,
        FireVolley = 2,
        ReacquireTarget = 3,
        AdvanceToVolleyRange = 4
    }

    public readonly struct TacticalInfantryFireInput
    {
        public TacticalInfantryFireInput(
            bool hasTarget,
            float targetDistance,
            float volleyRange,
            bool canFire,
            bool loadedVolley,
            bool alignedToTarget,
            bool targetInFort,
            float targetLateralExposure01,
            bool enemyMarchingToward,
            bool friendlyBlockedAhead,
            float morale01,
            float imminentChargePressure01)
        {
            HasTarget = hasTarget;
            TargetDistance = SanitizeNonNegative(targetDistance);
            VolleyRange = SanitizePositive(volleyRange, 300f);
            CanFire = canFire;
            LoadedVolley = loadedVolley;
            AlignedToTarget = alignedToTarget;
            TargetInFort = targetInFort;
            TargetLateralExposure01 = Clamp01(targetLateralExposure01);
            EnemyMarchingToward = enemyMarchingToward;
            FriendlyBlockedAhead = friendlyBlockedAhead;
            Morale01 = Clamp01(morale01);
            ImminentChargePressure01 = Clamp01(imminentChargePressure01);
        }

        public bool HasTarget { get; }
        public float TargetDistance { get; }
        public float VolleyRange { get; }
        public bool CanFire { get; }
        public bool LoadedVolley { get; }
        public bool AlignedToTarget { get; }
        public bool TargetInFort { get; }
        public float TargetLateralExposure01 { get; }
        public bool EnemyMarchingToward { get; }
        public bool FriendlyBlockedAhead { get; }
        public float Morale01 { get; }
        public float ImminentChargePressure01 { get; }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static float SanitizePositive(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalInfantryFireDecision
    {
        public TacticalInfantryFireDecision(TacticalInfantryFireAction action, bool blocksCharge, string reason)
        {
            Action = action;
            BlocksCharge = blocksCharge;
            Reason = string.IsNullOrWhiteSpace(reason) ? "infantry-fire" : reason.Trim();
        }

        public TacticalInfantryFireAction Action { get; }
        public bool BlocksCharge { get; }
        public string Reason { get; }

        public static TacticalInfantryFireDecision NoOpinion
        {
            get
            {
                return new TacticalInfantryFireDecision(
                    TacticalInfantryFireAction.None,
                    false,
                    "no-fire-opinion");
            }
        }
    }

    public static class TacticalInfantryFireDoctrine
    {
        public static TacticalInfantryFireDecision Decide(TacticalInfantryFireInput input)
        {
            if (!input.HasTarget)
                return new TacticalInfantryFireDecision(TacticalInfantryFireAction.HoldFire, false, "no-target");
            if (input.TargetDistance > input.VolleyRange)
                return new TacticalInfantryFireDecision(TacticalInfantryFireAction.AdvanceToVolleyRange, false, "outside-volley-range");
            if (!input.CanFire)
                return new TacticalInfantryFireDecision(TacticalInfantryFireAction.HoldFire, input.ImminentChargePressure01 < 0.70f, "cannot-fire");
            if (!input.AlignedToTarget || input.TargetLateralExposure01 < 0.25f)
                return new TacticalInfantryFireDecision(TacticalInfantryFireAction.ReacquireTarget, true, "bad-shot-reacquire");
            if (input.TargetInFort && input.TargetLateralExposure01 < 0.60f)
                return new TacticalInfantryFireDecision(TacticalInfantryFireAction.HoldFire, true, "poor-fort-shot");
            if (input.LoadedVolley && input.Morale01 >= 0.35f)
                return new TacticalInfantryFireDecision(TacticalInfantryFireAction.FireVolley, true, input.EnemyMarchingToward ? "volley-target-advancing" : "volley-ready");
            if (input.FriendlyBlockedAhead)
                return new TacticalInfantryFireDecision(TacticalInfantryFireAction.HoldFire, true, "friend-ahead");
            return new TacticalInfantryFireDecision(TacticalInfantryFireAction.HoldFire, input.ImminentChargePressure01 < 0.85f, "hold-fire");
        }
    }

    public readonly struct TacticalFireControlInput
    {
        public TacticalFireControlInput(
            int unitType,
            bool mounted,
            CommandTaskType task,
            CommandNodeRole role,
            bool hasTarget,
            float targetDistance,
            float effectiveFireRange,
            float ammoRatio01,
            float morale01,
            float fatigue01,
            bool inCover,
            bool enemyAdvancing,
            bool friendlyBlockedAhead,
            bool alignedToTarget,
            bool loadedVolley,
            bool currentChargeOrdered,
            int currentCombatBehaviorOrdered)
        {
            UnitType = unitType;
            Mounted = mounted;
            Task = task;
            Role = role;
            HasTarget = hasTarget;
            TargetDistance = SanitizeNonNegative(targetDistance);
            EffectiveFireRange = SanitizeNonNegative(effectiveFireRange);
            AmmoRatio01 = Clamp01(ammoRatio01);
            Morale01 = Clamp01(morale01);
            Fatigue01 = Clamp01(fatigue01);
            InCover = inCover;
            EnemyAdvancing = enemyAdvancing;
            FriendlyBlockedAhead = friendlyBlockedAhead;
            AlignedToTarget = alignedToTarget;
            LoadedVolley = loadedVolley;
            CurrentChargeOrdered = currentChargeOrdered;
            CurrentCombatBehaviorOrdered = currentCombatBehaviorOrdered;
        }

        public int UnitType { get; }
        public bool Mounted { get; }
        public CommandTaskType Task { get; }
        public CommandNodeRole Role { get; }
        public bool HasTarget { get; }
        public float TargetDistance { get; }
        public float EffectiveFireRange { get; }
        public float AmmoRatio01 { get; }
        public float Morale01 { get; }
        public float Fatigue01 { get; }
        public bool InCover { get; }
        public bool EnemyAdvancing { get; }
        public bool FriendlyBlockedAhead { get; }
        public bool AlignedToTarget { get; }
        public bool LoadedVolley { get; }
        public bool CurrentChargeOrdered { get; }
        public int CurrentCombatBehaviorOrdered { get; }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalFireControlDecision
    {
        public TacticalFireControlDecision(bool shouldWrite, int recommendedCombatBehavior, TacticalInfantryFireDecision fireDiscipline, string reason)
        {
            ShouldWrite = shouldWrite;
            RecommendedCombatBehavior = recommendedCombatBehavior;
            FireDiscipline = fireDiscipline;
            Reason = string.IsNullOrWhiteSpace(reason) ? "fire-control" : reason.Trim();
        }

        public bool ShouldWrite { get; }
        public int RecommendedCombatBehavior { get; }
        public TacticalInfantryFireDecision FireDiscipline { get; }
        public string Reason { get; }

        public static TacticalFireControlDecision NoWrite(int currentBehavior, string reason)
        {
            return new TacticalFireControlDecision(
                false,
                currentBehavior,
                TacticalInfantryFireDecision.NoOpinion,
                reason);
        }
    }

    public static class TacticalFireControlDoctrine
    {
        public const int InfantryShort = 0;
        public const int InfantryMedium = 1;
        public const int InfantryLong = 2;
        public const int InfantryCharge = 3;
        public const int CavalryEvade = 4;
        public const int CavalryNeutral = 5;
        public const int CavalryCharge = 6;

        public static TacticalFireControlDecision Decide(TacticalFireControlInput input)
        {
            if (input.CurrentChargeOrdered)
                return TacticalFireControlDecision.NoWrite(input.CurrentCombatBehaviorOrdered, "already-charge-ordered");
            if (!input.HasTarget)
                return TacticalFireControlDecision.NoWrite(input.CurrentCombatBehaviorOrdered, "no-target");
            if (input.UnitType == TacticalUnitType.Artillery || input.UnitType > TacticalUnitType.MaxCombat)
                return TacticalFireControlDecision.NoWrite(input.CurrentCombatBehaviorOrdered, "unsupported-unit-type");

            if (input.UnitType == TacticalUnitType.Cavalry)
                return DecideCavalry(input);

            if (input.UnitType != TacticalUnitType.Infantry &&
                input.UnitType != TacticalUnitType.Skirmisher)
            {
                return TacticalFireControlDecision.NoWrite(input.CurrentCombatBehaviorOrdered, "unsupported-unit-type");
            }

            TacticalInfantryFireDecision fire = TacticalInfantryFireDoctrine.Decide(new TacticalInfantryFireInput(
                hasTarget: input.HasTarget,
                targetDistance: input.TargetDistance,
                volleyRange: HistoricalMediumRange(input),
                canFire: input.EffectiveFireRange <= 0f || input.TargetDistance <= input.EffectiveFireRange,
                loadedVolley: input.LoadedVolley,
                alignedToTarget: input.AlignedToTarget,
                targetInFort: false,
                targetLateralExposure01: input.AlignedToTarget ? 0.80f : 0.10f,
                enemyMarchingToward: input.EnemyAdvancing,
                friendlyBlockedAhead: input.FriendlyBlockedAhead,
                morale01: input.Morale01,
                imminentChargePressure01: ChargePressure(input)));

            int behavior = InfantryMedium;
            string reason = "historical-medium-band";

            if (ShouldUseLongRange(input))
            {
                behavior = InfantryLong;
                reason = input.UnitType == TacticalUnitType.Skirmisher ? "skirmisher-long" : "covered-harassing-fire";
            }
            else if (ShouldUseShortRange(input))
            {
                behavior = InfantryShort;
                reason = "decisive-close-volley";
            }

            return new TacticalFireControlDecision(
                behavior != input.CurrentCombatBehaviorOrdered,
                behavior,
                fire,
                reason);
        }

        private static TacticalFireControlDecision DecideCavalry(TacticalFireControlInput input)
        {
            int behavior = CavalryNeutral;
            string reason = "cavalry-neutral";

            if (IsScreenOrFallback(input.Task) && input.Mounted)
            {
                behavior = CavalryEvade;
                reason = "cavalry-screen-evade";
            }
            else if (IsAttackTask(input.Task) &&
                     input.TargetDistance > 0f &&
                     input.TargetDistance <= 100f &&
                     input.Morale01 >= 0.45f &&
                     input.Fatigue01 <= 0.80f)
            {
                behavior = CavalryCharge;
                reason = "cavalry-close-charge";
            }

            return new TacticalFireControlDecision(
                behavior != input.CurrentCombatBehaviorOrdered,
                behavior,
                TacticalInfantryFireDecision.NoOpinion,
                reason);
        }

        private static bool ShouldUseShortRange(TacticalFireControlInput input)
        {
            if (input.TargetDistance > 0f && input.TargetDistance <= 100f)
                return true;
            if (input.AmmoRatio01 < 0.25f || input.Morale01 < 0.35f)
                return true;
            return IsAttackTask(input.Task) &&
                input.LoadedVolley &&
                input.EnemyAdvancing &&
                input.TargetDistance <= HistoricalMediumRange(input);
        }

        private static bool ShouldUseLongRange(TacticalFireControlInput input)
        {
            if (input.TargetDistance <= HistoricalMediumRange(input))
                return false;
            if (input.AmmoRatio01 < 0.45f || input.Morale01 < 0.45f || input.Fatigue01 > 0.70f)
                return false;
            if (IsNonDecisiveFireTask(input.Task))
                return true;
            return input.InCover &&
                (input.Task == CommandTaskType.HoldObjective ||
                 input.Task == CommandTaskType.HoldChoke ||
                 input.Task == CommandTaskType.Delay);
        }

        private static float HistoricalMediumRange(TacticalFireControlInput input)
        {
            float cap = input.EffectiveFireRange > 0f ? input.EffectiveFireRange : 150f;
            return Math.Min(150f, Math.Max(100f, cap * 0.35f));
        }

        private static float ChargePressure(TacticalFireControlInput input)
        {
            if (!IsAttackTask(input.Task)) return 0f;
            if (input.TargetDistance <= 80f) return 0.90f;
            if (input.TargetDistance <= 150f) return 0.70f;
            return 0.35f;
        }

        private static bool IsAttackTask(CommandTaskType task)
        {
            return task == CommandTaskType.AttackObjective ||
                task == CommandTaskType.SupportAttack ||
                task == CommandTaskType.ReleaseReserve;
        }

        private static bool IsNonDecisiveFireTask(CommandTaskType task)
        {
            return task == CommandTaskType.Screen ||
                task == CommandTaskType.Scout ||
                task == CommandTaskType.Probe ||
                task == CommandTaskType.Delay ||
                task == CommandTaskType.FallBackToLine ||
                task == CommandTaskType.FormUp ||
                task == CommandTaskType.AdvanceToAssembly ||
                task == CommandTaskType.ReserveWait ||
                task == CommandTaskType.GuardFlank;
        }

        private static bool IsScreenOrFallback(CommandTaskType task)
        {
            return task == CommandTaskType.Screen ||
                task == CommandTaskType.Scout ||
                task == CommandTaskType.Probe ||
                task == CommandTaskType.Delay ||
                task == CommandTaskType.FallBackToLine;
        }
    }

    public readonly struct TacticalBattlefieldAttributeInput
    {
        public TacticalBattlefieldAttributeInput(
            float experience01,
            float morale01,
            float fatigue01,
            float commanderAggression01)
        {
            Experience01 = Clamp01(experience01);
            Morale01 = Clamp01(morale01);
            Fatigue01 = Clamp01(fatigue01);
            CommanderAggression01 = Clamp01(commanderAggression01);
        }

        public float Experience01 { get; }
        public float Morale01 { get; }
        public float Fatigue01 { get; }
        public float CommanderAggression01 { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    public readonly struct TacticalBattlefieldAttributeDecision
    {
        public TacticalBattlefieldAttributeDecision(
            float fallbackChance01,
            float retreatChance01,
            float fireModifier01,
            float loadModifier01,
            float meleeModifier01,
            float supportDistanceMultiplier,
            float chargeRangeMultiplier,
            float artilleryPanicMultiplier)
        {
            FallbackChance01 = Clamp01(fallbackChance01);
            RetreatChance01 = Clamp01(retreatChance01);
            FireModifier01 = Clamp01(fireModifier01);
            LoadModifier01 = Clamp01(loadModifier01);
            MeleeModifier01 = Clamp01(meleeModifier01);
            SupportDistanceMultiplier = ClampRange(supportDistanceMultiplier, 0.5f, 1.6f);
            ChargeRangeMultiplier = ClampRange(chargeRangeMultiplier, 0.5f, 1.6f);
            ArtilleryPanicMultiplier = ClampRange(artilleryPanicMultiplier, 0.5f, 1.8f);
        }

        public float FallbackChance01 { get; }
        public float RetreatChance01 { get; }
        public float FireModifier01 { get; }
        public float LoadModifier01 { get; }
        public float MeleeModifier01 { get; }
        public float SupportDistanceMultiplier { get; }
        public float ChargeRangeMultiplier { get; }
        public float ArtilleryPanicMultiplier { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float ClampRange(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return min;
            if (value < min) return min;
            return value > max ? max : value;
        }
    }

    public static class TacticalBattlefieldAttributeMatrix
    {
        public static TacticalBattlefieldAttributeDecision Evaluate(TacticalBattlefieldAttributeInput input)
        {
            float brokenPressure = (1f - input.Morale01) * 0.55f + input.Fatigue01 * 0.35f + (1f - input.Experience01) * 0.20f;
            float steadyCredit = input.Morale01 * 0.35f + input.Experience01 * 0.30f + (1f - input.Fatigue01) * 0.20f;
            float fallback = brokenPressure - steadyCredit * 0.55f - input.CommanderAggression01 * 0.10f;
            float retreat = fallback + (1f - input.Morale01) * 0.22f + input.Fatigue01 * 0.12f - input.CommanderAggression01 * 0.08f;
            float fire = 0.25f + input.Experience01 * 0.45f + input.Morale01 * 0.25f - input.Fatigue01 * 0.25f;
            float load = 0.25f + input.Experience01 * 0.40f + input.Morale01 * 0.20f - input.Fatigue01 * 0.30f;
            float melee = 0.25f + input.Experience01 * 0.30f + input.Morale01 * 0.25f + input.CommanderAggression01 * 0.15f - input.Fatigue01 * 0.22f;
            float support = 0.85f + input.Experience01 * 0.25f + input.CommanderAggression01 * 0.20f - input.Fatigue01 * 0.15f;
            float charge = 0.80f + input.CommanderAggression01 * 0.35f + input.Morale01 * 0.20f - input.Fatigue01 * 0.22f;
            float artyPanic = 1.25f - input.Morale01 * 0.30f - input.Experience01 * 0.20f + input.Fatigue01 * 0.25f;

            return new TacticalBattlefieldAttributeDecision(
                fallback,
                retreat,
                fire,
                load,
                melee,
                support,
                charge,
                artyPanic);
        }
    }
}
