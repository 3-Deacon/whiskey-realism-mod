using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Operations
{
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
            bool hasOrders,
            CommandTaskType currentTask = CommandTaskType.None)
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
            CurrentTask = currentTask;
        }

        public string NodeId { get; }
        public CommandNodeRole Role { get; }
        // Ledger task the child already carries. Hold-type tasks are kept
        // (B-divplay): the play never turns a holding brigade into an attacker.
        public CommandTaskType CurrentTask { get; }
        public bool HoldsPosition
        {
            get
            {
                switch (CurrentTask)
                {
                    case CommandTaskType.HoldObjective:
                    case CommandTaskType.HoldChoke:
                    case CommandTaskType.GuardFlank:
                    case CommandTaskType.Delay:
                    case CommandTaskType.Consolidate:
                    case CommandTaskType.ReserveWait:
                    case CommandTaskType.FallBackToLine:
                        return true;
                    default:
                        return false;
                }
            }
        }
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
            IReadOnlyList<TacticalDivisionPlaySubordinate> subordinates,
            bool sideDefending = false)
        {
            ParentNodeId = string.IsNullOrWhiteSpace(parentNodeId) ? "parent-unknown" : parentNodeId.Trim();
            Subordinates = subordinates ?? Array.Empty<TacticalDivisionPlaySubordinate>();
            SideDefending = sideDefending;
        }

        public string ParentNodeId { get; }
        // Vanilla GetBattleType == 2 (defend, 86095): the side holds the
        // objectives, so division plays never convert its brigades to attacks.
        public bool SideDefending { get; }
        public IReadOnlyList<TacticalDivisionPlaySubordinate> Subordinates { get; }
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
                if (input.SideDefending) continue;
                if (child.HoldsPosition) continue;
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
                if (child.HoldsPosition) continue;

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

    // BC5a: the division play's per-child input from the battle snapshot. #61
    // read it from live vanilla fields (TacticalFogOfWarContact.HasVisibleEnemy,
    // groupenemiesinrange, combatstatussubordinates, flanksthreated, morale,
    // orderqueue) and found the parent through the scene graph; here every
    // input is a PostureSense proxy (flank risk is #44's perceived rule,
    // BattleWriterPolicy.PerceivedFlankRisk) and the parent is the snapshot's
    // command parent (plan D5). Courier cadence is the order executor's (one courier
    // move per group per OrderExecutor.CourierIntervalBattleHours).
    public static class TacticalDivisionPlayInputs
    {
        /// <summary>The child's command parent (own index), or -1 at the root (no division).</summary>
        public static int ParentIndex(IBattleUnitView s, int index)
        {
            if (s == null || index < 0 || index >= s.OwnCount) return -1;
            int parent = s.Own[index].ParentIndex;
            return parent >= 0 && parent < s.OwnCount && parent != index ? parent : -1;
        }

        public static TacticalDivisionPlaySubordinate FromSnapshot(
            BattleSnapshot s,
            int index,
            UnitFlags descendantFlags,
            int movingUnits,
            string nodeId,
            CommandNodeRole role,
            CommandTaskType task,
            float[] meanFlankThreat)
        {
            ref UnitRecord unit = ref s.Own[index];
            bool engaged = PostureSense.CloseEngaged(s, index, descendantFlags);
            bool flankRisk = BattleWriterPolicy.PerceivedFlankRisk(s, index, meanFlankThreat);
            bool activeMove = PostureSense.ActiveMove(unit, movingUnits);
            int closest = s.ClosestVisibleEnemy(index, out float distance);
            return new TacticalDivisionPlaySubordinate(
                nodeId,
                role,
                hasTargets: closest >= 0,
                engagingEnemy: engaged,
                underCloseFire: engaged || flankRisk,
                inTrouble: flankRisk || unit.Morale < 0.35f,
                isArtillery: ((unit.Flags | descendantFlags) & UnitFlags.IsArtillery) != 0,
                enemyDistance: closest >= 0 ? distance : 0f,
                hasOrders: PostureSense.PendingOrder(unit, activeMove, allowStaleQueuedBypass: false) || activeMove,
                currentTask: task);
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
        ChargeRaidTarget = 5
    }

    public readonly struct TacticalCavalryFollowInput
    {
        public TacticalCavalryFollowInput(
            TacticalCavalryFollowMode mode,
            bool hasFollowTarget,
            bool targetIsOfficer,
            bool targetInFort,
            bool targetInSquare,
            bool targetHasInfantryOrArtillerySupport,
            bool canChargeTarget,
            float fearAdvantage01,
            bool enemyClose,
            float enemyDistance,
            float longRange)
        {
            Mode = mode;
            HasFollowTarget = hasFollowTarget;
            TargetIsOfficer = targetIsOfficer;
            TargetInFort = targetInFort;
            TargetInSquare = targetInSquare;
            TargetHasInfantryOrArtillerySupport = targetHasInfantryOrArtillerySupport;
            CanChargeTarget = canChargeTarget;
            FearAdvantage01 = Clamp01(fearAdvantage01);
            EnemyClose = enemyClose;
            EnemyDistance = SanitizeNonNegative(enemyDistance);
            LongRange = SanitizePositive(longRange, 300f);
        }

        public TacticalCavalryFollowMode Mode { get; }
        public bool HasFollowTarget { get; }
        public bool TargetIsOfficer { get; }
        public bool TargetInFort { get; }
        public bool TargetInSquare { get; }
        public bool TargetHasInfantryOrArtillerySupport { get; }
        public bool CanChargeTarget { get; }
        public float FearAdvantage01 { get; }
        public bool EnemyClose { get; }
        public float EnemyDistance { get; }
        public float LongRange { get; }

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
            switch (input.Mode)
            {
                case TacticalCavalryFollowMode.Guard:
                    if (!input.HasFollowTarget)
                        return new TacticalCavalryFollowDecision(TacticalCavalryFollowAction.RequestScoutLocation, "guard-needs-target");
                    if (input.EnemyClose && input.CanChargeTarget)
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
            if (input.TargetIsOfficer || input.TargetInFort || input.TargetInSquare)
                return false;
            if (input.TargetHasInfantryOrArtillerySupport)
                return false;
            if (!input.CanChargeTarget)
                return false;
            return input.FearAdvantage01 >= 0.50f;
        }
    }

    public readonly struct TacticalGrandTacticianReconInput
    {
        public TacticalGrandTacticianReconInput(
            CommandNodeRole role,
            CommandTaskType task,
            bool hasFowVisibleEnemy,
            bool underRecentFire,
            bool sideDefending = false)
        {
            Role = role;
            Task = task;
            HasFowVisibleEnemy = hasFowVisibleEnemy;
            UnderRecentFire = underRecentFire;
            SideDefending = sideDefending;
        }

        public CommandNodeRole Role { get; }
        public CommandTaskType Task { get; }
        public bool HasFowVisibleEnemy { get; }
        public bool UnderRecentFire { get; }
        // BC9: the side is the battle's Defender (GetBattleType 2): never sent probing.
        public bool SideDefending { get; }
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
            TacticalGrandTacticianReconDecision decision = DecideForAnyStance(input);
            if (!input.SideDefending) return decision;
            // BC9 stance guard, with or without a visible enemy: a Defender screens
            // instead of probing and holds instead of attacking.
            CommandTaskType guarded = TacticalDecisionDoctrine.TaskForStance(decision.Task, BattleStance.Defender);
            return guarded == decision.Task
                ? decision
                : new TacticalGrandTacticianReconDecision(guarded, guarded == CommandTaskType.Screen ? "defender-screen" : "defender-hold");
        }

        private static TacticalGrandTacticianReconDecision DecideForAnyStance(TacticalGrandTacticianReconInput input)
        {
            if (!IsReconOrAssaultTask(input.Task))
                return new TacticalGrandTacticianReconDecision(input.Task, "not-recon-task");

            if (input.HasFowVisibleEnemy || input.UnderRecentFire)
                return new TacticalGrandTacticianReconDecision(input.Task, "contact-confirmed");

            // BC9: no cavalry branch. A group with one cavalry regiment is no scout;
            // regiment scouting is the recon producer's (Battle/BattleRecon), and the
            // Probe target is BattleRecon.LookPoint, a bounded advance.
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
        Unlimbered = 2
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

            // Point-blank panic (2026-05-22 user-reported bug fix). Smoke
            // showed artillery at 0-50m from enemy infantry with support-count
            // 14+ — Scourge's "friendly closer to artillery than enemy" test
            // counted friendlies BEHIND artillery as "support," which doesn't
            // protect from infantry in front. At true point-blank (≤60m),
            // artillery is in immediate danger regardless of nominal support
            // count — Civil War batteries overrun at this range are doctrinally
            // lost. Force limber-or-retreat unconditionally. Runs BEFORE the
            // ammo conservation check so a battery at point blank doesn't
            // sit and conserve ammo while being charged.
            if (!input.EnemyRoutedOrRetreating
                && input.EnemyCloseDistance > 0f
                && input.EnemyCloseDistance <= 60f)
            {
                if (input.LimberState == TacticalArtilleryLimberState.Limbered)
                    return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.Retreat, TacticalArtilleryAmmoMission.Preserve, "point-blank-retreat");
                return new TacticalArtilleryMicroDecision(TacticalArtilleryMicroAction.Limber, TacticalArtilleryAmmoMission.Preserve, "point-blank-limber");
            }

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
}
