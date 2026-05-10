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

    public readonly struct PostureExecutionDecision
    {
        public PostureExecutionDecision(PostureExecutionAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
        }

        public PostureExecutionAction Action { get; }
        public string Reason { get; }
    }

    public readonly struct WriteEligibilitySnapshot
    {
        public WriteEligibilitySnapshot(
            bool modeAllowsWrites,
            bool playerProtected,
            bool routed,
            bool orderPending,
            bool recentOrder)
        {
            ModeAllowsWrites = modeAllowsWrites;
            PlayerProtected = playerProtected;
            Routed = routed;
            OrderPending = orderPending;
            RecentOrder = recentOrder;
        }

        public bool ModeAllowsWrites { get; }
        public bool PlayerProtected { get; }
        public bool Routed { get; }
        public bool OrderPending { get; }
        public bool RecentOrder { get; }
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

            if (eligibility.PlayerProtected)
            {
                return NoWrite("player-protected");
            }

            if (eligibility.Routed)
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

            if (physical.PathInterrupted && physical.Paths <= 0 && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.RecoverInterruptedOrder,
                    "illegal-idle-path-interrupted");
            }

            switch (state.Task)
            {
                case CommandTaskType.FormUp:
                    return FormationAndWaypoint("form-up");
                case CommandTaskType.AdvanceToAssembly:
                    return FormationAndWaypoint("advance-to-assembly");
                case CommandTaskType.AttackObjective:
                    return FormationAndWaypoint("attack-objective");
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
                case CommandTaskType.GuardFlank:
                    return Formation("guard-flank");
                case CommandTaskType.FallBackToLine:
                    return new PostureExecutionDecision(PostureExecutionAction.FallbackToLine, "fallback-line");
                case CommandTaskType.ReserveWait:
                    return NoWrite("valid-reserve-wait");
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
            return new PostureExecutionDecision(PostureExecutionAction.SetFormation, reason);
        }

        private static PostureExecutionDecision FormationAndWaypoint(string reason)
        {
            return new PostureExecutionDecision(PostureExecutionAction.SetFormationAndWaypoint, reason);
        }
    }
}
