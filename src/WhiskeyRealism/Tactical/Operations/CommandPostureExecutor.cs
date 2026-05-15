namespace WhiskeyRealism.Tactical.Operations
{
    public enum PostureExecutionAction
    {
        NoWrite,
        SetFormation,
        SetWaypoint,
        SetFormationAndWaypoint,
        ChangeStance,
        ReleaseReserve,
        FallbackToLine,
        RecoverInterruptedOrder
    }

    public enum PostureExecutionTarget
    {
        None,
        CurrentPosition,
        AssemblyArea,
        ObjectiveApproach,
        ReserveArea,
        FallbackLine,
        RecoveryPath,
        ReleasePoint,
        DoctrinePrimaryTarget,
        DoctrineSupportTarget,
        DoctrineFallbackTarget
    }

    public readonly struct PostureExecutionDecision
    {
        public PostureExecutionDecision(
            PostureExecutionAction action,
            string reason,
            PostureExecutionTarget target = PostureExecutionTarget.None,
            bool clearInterruptedPaths = false)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            Target = target;
            ClearInterruptedPaths = clearInterruptedPaths;
        }

        public PostureExecutionAction Action { get; }
        public string Reason { get; }
        public PostureExecutionTarget Target { get; }
        public bool ClearInterruptedPaths { get; }
    }

    public readonly struct WriteEligibilitySnapshot
    {
        public WriteEligibilitySnapshot(
            bool modeAllowsWrites,
            bool playerProtected,
            bool routed,
            bool orderPending,
            bool recentOrder,
            bool alreadyDoingCorrectTask = false,
            bool atAssignedLocation = false,
            bool missingLedgerAssignment = false,
            bool closeEngaged = false)
        {
            ModeAllowsWrites = modeAllowsWrites;
            PlayerProtected = playerProtected;
            Routed = routed;
            OrderPending = orderPending;
            RecentOrder = recentOrder;
            AlreadyDoingCorrectTask = alreadyDoingCorrectTask;
            AtAssignedLocation = atAssignedLocation;
            MissingLedgerAssignment = missingLedgerAssignment;
            CloseEngaged = closeEngaged;
        }

        public bool ModeAllowsWrites { get; }
        public bool PlayerProtected { get; }
        public bool Routed { get; }
        public bool OrderPending { get; }
        public bool RecentOrder { get; }
        public bool AlreadyDoingCorrectTask { get; }
        public bool AtAssignedLocation { get; }
        public bool MissingLedgerAssignment { get; }
        public bool CloseEngaged { get; }
    }

    public static class CommandWaypointWritePolicy
    {
        public static bool ShouldSkipDuplicateWaypoint(
            float currentWaypointX,
            float currentWaypointZ,
            float targetX,
            float targetZ,
            float tolerance,
            bool pathInterrupted)
        {
            return ShouldSkipDuplicateWaypoint(
                currentWaypointX,
                currentWaypointZ,
                targetX,
                targetZ,
                tolerance,
                pathInterrupted,
                regimentPaths: 1,
                activeMove: true,
                currentX: targetX,
                currentZ: targetZ);
        }

        public static bool ShouldSkipDuplicateWaypoint(
            float currentWaypointX,
            float currentWaypointZ,
            float targetX,
            float targetZ,
            float tolerance,
            bool pathInterrupted,
            int regimentPaths,
            bool activeMove,
            float currentX,
            float currentZ)
        {
            if (pathInterrupted) return false;
            if (!IsFinite(currentWaypointX) || !IsFinite(currentWaypointZ)) return false;
            if (!IsFinite(targetX) || !IsFinite(targetZ)) return false;
            if (currentWaypointX == 0f && currentWaypointZ == 0f) return false;

            float safeTolerance = IsFinite(tolerance) && tolerance > 0f ? tolerance : 1f;
            float dx = currentWaypointX - targetX;
            float dz = currentWaypointZ - targetZ;
            if ((dx * dx) + (dz * dz) > safeTolerance * safeTolerance) return false;

            if (!activeMove && regimentPaths <= 0 && IsFinite(currentX) && IsFinite(currentZ))
            {
                float currentDx = currentX - targetX;
                float currentDz = currentZ - targetZ;
                if ((currentDx * currentDx) + (currentDz * currentDz) > safeTolerance * safeTolerance)
                    return false;
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class CommandPostureExecutor
    {
        public static PostureExecutionDecision Decide(
            CommandDoctrineOrder order,
            CommandPhysicalState physical,
            float nowSeconds)
        {
            return Decide(
                order,
                physical,
                new WriteEligibilitySnapshot(
                    modeAllowsWrites: true,
                    playerProtected: physical.PlayerProtected,
                    routed: physical.Routed,
                    orderPending: false,
                    recentOrder: false),
                nowSeconds);
        }

        public static PostureExecutionDecision Decide(
            CommandDoctrineOrder order,
            CommandPhysicalState physical,
            WriteEligibilitySnapshot eligibility,
            float nowSeconds)
        {
            if (!eligibility.ModeAllowsWrites)
            {
                return NoWrite("mode-monitor-only");
            }

            if (eligibility.PlayerProtected || physical.PlayerProtected)
            {
                return NoWrite("player-protected");
            }

            if (eligibility.Routed || physical.Routed)
            {
                return NoWrite("routed");
            }

            if (eligibility.OrderPending)
            {
                return NoWrite("order-pending");
            }

            if (eligibility.RecentOrder)
            {
                return NoWrite("recent-order");
            }

            if (!order.HasPurpose || order.Task == CommandTaskType.None)
            {
                return NoWrite("missing-ledger-assignment");
            }

            if (eligibility.AlreadyDoingCorrectTask)
            {
                return NoWrite("already-correct");
            }

            if (order.AllowsIdle && order.Task == CommandTaskType.ReserveWait)
            {
                if (order.PrimaryTarget.HasValue && !eligibility.AtAssignedLocation)
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetWaypoint,
                        "reserve-area",
                        PostureExecutionTarget.DoctrinePrimaryTarget);
                }

                return NoWrite("legal-idle");
            }

            if (eligibility.CloseEngaged)
            {
                if (TacticalDecisionDoctrine.ShouldBreakOffRecon(
                        order.Task,
                        closeEngaged: true,
                        hasFallbackTarget: order.FallbackTarget.HasValue))
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetFormationAndWaypoint,
                        "close-engaged-screen-breakoff",
                        PostureExecutionTarget.DoctrineFallbackTarget);
                }

                if (order.Task == CommandTaskType.FallBackToLine)
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetFormationAndWaypoint,
                        "close-engaged-fallback-line",
                        PostureExecutionTarget.DoctrineFallbackTarget);
                }

                return Formation("close-engaged-" + TaskReason(order.Task));
            }

            if (order.Task == CommandTaskType.FallBackToLine && physical.PathInterrupted && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.FallbackToLine,
                    "fallback-line",
                    PostureExecutionTarget.DoctrineFallbackTarget,
                    clearInterruptedPaths: true);
            }

            if (physical.PathInterrupted && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.RecoverInterruptedOrder,
                    "interrupted-inactive",
                    PostureExecutionTarget.RecoveryPath,
                    clearInterruptedPaths: true);
            }

            if (physical.ActiveMove)
            {
                return NoWrite("movement-in-progress");
            }

            switch (order.Task)
            {
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                    return FormationAndWaypoint(TaskReason(order.Task), PostureExecutionTarget.DoctrinePrimaryTarget);
                case CommandTaskType.Scout:
                case CommandTaskType.Probe:
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.Screen:
                case CommandTaskType.GuardFlank:
                    return FormationAndWaypoint(TaskReason(order.Task), PostureExecutionTarget.DoctrinePrimaryTarget);
                case CommandTaskType.FallBackToLine:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.FallbackToLine,
                        "fallback-line",
                        PostureExecutionTarget.DoctrineFallbackTarget,
                        clearInterruptedPaths: true);
                default:
                    return Decide(
                        new CommandNodeOperationalState(
                            order.NodeId,
                            CommandEchelonKind.DivisionLike,
                            order.Role,
                            order.Task,
                            CommandTaskState.Committed),
                        physical,
                        eligibility);
            }
        }

        public static PostureExecutionDecision Decide(
            CommandNodeOperationalState state,
            CommandPhysicalState physical,
            WriteEligibilitySnapshot eligibility)
        {
            if (!eligibility.ModeAllowsWrites)
            {
                return NoWrite("mode-monitor-only");
            }

            if (eligibility.PlayerProtected || physical.PlayerProtected)
            {
                return NoWrite("player-protected");
            }

            if (eligibility.Routed || physical.Routed)
            {
                return NoWrite("routed");
            }

            if (eligibility.OrderPending)
            {
                return NoWrite("order-pending");
            }

            if (eligibility.RecentOrder)
            {
                return NoWrite("recent-order");
            }

            if (eligibility.MissingLedgerAssignment || state.Task == CommandTaskType.None)
            {
                return NoWrite("missing-ledger-assignment");
            }

            if (eligibility.AlreadyDoingCorrectTask)
            {
                return NoWrite("already-correct");
            }

            if (physical.ActiveMove)
            {
                return NoWrite("movement-in-progress");
            }

            if (eligibility.CloseEngaged)
            {
                if (state.Task == CommandTaskType.FallBackToLine)
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetFormationAndWaypoint,
                        "close-engaged-fallback-line",
                        PostureExecutionTarget.FallbackLine);
                }

                return Formation("close-engaged-" + TaskReason(state.Task));
            }

            if (physical.PathInterrupted && physical.Paths <= 0 && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.RecoverInterruptedOrder,
                    "illegal-idle-path-interrupted",
                    PostureExecutionTarget.RecoveryPath,
                    clearInterruptedPaths: true);
            }

            switch (state.Task)
            {
                case CommandTaskType.Scout:
                    return FormationAndWaypoint("scout", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.FormUp:
                    return FormationAndWaypoint("form-up", PostureExecutionTarget.AssemblyArea);
                case CommandTaskType.AdvanceToAssembly:
                    return FormationAndWaypoint("advance-to-assembly", PostureExecutionTarget.AssemblyArea);
                case CommandTaskType.AttackObjective:
                    return FormationAndWaypoint("attack-objective", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.HoldObjective:
                    return Formation("hold-objective");
                case CommandTaskType.FixEnemy:
                    return FormationAndWaypoint("fix-enemy", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.Screen:
                    return FormationAndWaypoint("screen", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.Probe:
                    return FormationAndWaypoint("probe", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.SupportAttack:
                    return FormationAndWaypoint("support-attack", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.HoldChoke:
                    return Formation("hold-choke");
                case CommandTaskType.GuardFlank:
                    return Formation("guard-flank");
                case CommandTaskType.Delay:
                    return Formation("delay");
                case CommandTaskType.Consolidate:
                    return Formation("consolidate");
                case CommandTaskType.FallBackToLine:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.FallbackToLine,
                        "fallback-line",
                        PostureExecutionTarget.FallbackLine);
                case CommandTaskType.ReleaseReserve:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.ReleaseReserve,
                        "release-reserve",
                        PostureExecutionTarget.ReleasePoint);
                case CommandTaskType.RecoverStuckOrder:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.RecoverInterruptedOrder,
                        "recover-stuck-order",
                        PostureExecutionTarget.RecoveryPath,
                        clearInterruptedPaths: true);
                case CommandTaskType.ReserveWait:
                    return eligibility.AtAssignedLocation
                        ? Formation("reserve-hold")
                        : new PostureExecutionDecision(
                            PostureExecutionAction.SetWaypoint,
                            "reserve-area",
                            PostureExecutionTarget.ReserveArea);
                default:
                    return NoWrite("already-valid");
            }
        }

        private static PostureExecutionDecision NoWrite(string reason)
        {
            return new PostureExecutionDecision(PostureExecutionAction.NoWrite, reason);
        }

        private static PostureExecutionDecision Formation(string reason)
        {
            return new PostureExecutionDecision(
                PostureExecutionAction.SetFormation,
                reason,
                PostureExecutionTarget.CurrentPosition);
        }

        private static PostureExecutionDecision FormationAndWaypoint(
            string reason,
            PostureExecutionTarget target)
        {
            return new PostureExecutionDecision(
                PostureExecutionAction.SetFormationAndWaypoint,
                reason,
                target);
        }

        private static string TaskReason(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.Scout:
                    return "scout";
                case CommandTaskType.Probe:
                    return "probe";
                case CommandTaskType.Screen:
                    return "screen";
                case CommandTaskType.FormUp:
                    return "form-up";
                case CommandTaskType.AdvanceToAssembly:
                    return "advance-to-assembly";
                case CommandTaskType.AttackObjective:
                    return "attack-objective";
                case CommandTaskType.FixEnemy:
                    return "fix-enemy";
                case CommandTaskType.SupportAttack:
                    return "support-attack";
                case CommandTaskType.HoldObjective:
                    return "hold-objective";
                case CommandTaskType.HoldChoke:
                    return "hold-choke";
                case CommandTaskType.GuardFlank:
                    return "guard-flank";
                case CommandTaskType.ReserveWait:
                    return "reserve-wait";
                case CommandTaskType.ReleaseReserve:
                    return "release-reserve";
                case CommandTaskType.FallBackToLine:
                    return "fallback-line";
                case CommandTaskType.Delay:
                    return "delay";
                case CommandTaskType.Consolidate:
                    return "consolidate";
                case CommandTaskType.RecoverStuckOrder:
                    return "recover-stuck-order";
                default:
                    return "unknown";
            }
        }
    }
}
