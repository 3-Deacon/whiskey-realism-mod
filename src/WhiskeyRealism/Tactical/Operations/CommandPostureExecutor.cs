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
        ReleasePoint
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

    public static class CommandPostureExecutor
    {
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
                    return Formation("scout");
                case CommandTaskType.FormUp:
                    return FormationAndWaypoint("form-up", PostureExecutionTarget.AssemblyArea);
                case CommandTaskType.AdvanceToAssembly:
                    return FormationAndWaypoint("advance-to-assembly", PostureExecutionTarget.AssemblyArea);
                case CommandTaskType.AttackObjective:
                    return FormationAndWaypoint("attack-objective", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.HoldObjective:
                    return Formation("hold-objective");
                case CommandTaskType.FixEnemy:
                    return Formation("fix-enemy");
                case CommandTaskType.Screen:
                    return Formation("screen");
                case CommandTaskType.Probe:
                    return Formation("probe");
                case CommandTaskType.SupportAttack:
                    return Formation("support-attack");
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
