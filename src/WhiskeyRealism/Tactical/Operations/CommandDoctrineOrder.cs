namespace WhiskeyRealism.Tactical.Operations
{
    public enum DoctrineAllowedIdleReason
    {
        None = 0,
        HeldReserve = 1,
        FormingUp = 2,
        WaitingForCommitWindow = 3,
        DefendingObjective = 4,
        RecoveringAfterFallback = 5,
        PlayerProtected = 6
    }

    public readonly struct CommandDoctrineOrder
    {
        private CommandDoctrineOrder(
            string nodeId,
            CommandNodeRole role,
            CommandTaskType task,
            string objectiveId,
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint supportTarget,
            DoctrineTargetPoint fallbackTarget,
            DoctrineAllowedIdleReason allowedIdle,
            float minCommitUntilSeconds,
            float issuedAtSeconds,
            float confidence01,
            string reason)
        {
            NodeId = SanitizeText(nodeId, "node-unknown");
            Role = role;
            Task = task;
            ObjectiveId = SanitizeText(objectiveId, "objective-unknown");
            PrimaryTarget = primaryTarget;
            SupportTarget = supportTarget;
            FallbackTarget = fallbackTarget;
            AllowedIdle = allowedIdle;
            MinCommitUntilSeconds = SanitizeNonNegative(minCommitUntilSeconds);
            IssuedAtSeconds = SanitizeNonNegative(issuedAtSeconds);
            Confidence01 = Clamp01(confidence01);
            Reason = SanitizeText(reason, "unspecified");
        }

        public string NodeId { get; }
        public CommandNodeRole Role { get; }
        public CommandTaskType Task { get; }
        public string ObjectiveId { get; }
        public DoctrineTargetPoint PrimaryTarget { get; }
        public DoctrineTargetPoint SupportTarget { get; }
        public DoctrineTargetPoint FallbackTarget { get; }
        public DoctrineAllowedIdleReason AllowedIdle { get; }
        public float MinCommitUntilSeconds { get; }
        public float IssuedAtSeconds { get; }
        public float Confidence01 { get; }
        public string Reason { get; }

        public bool HasPurpose { get { return Role != CommandNodeRole.Unknown || Task != CommandTaskType.None; } }
        public bool AllowsIdle { get { return AllowedIdle != DoctrineAllowedIdleReason.None; } }
        public bool HasConcreteMovementTarget { get { return PrimaryTarget.HasValue || SupportTarget.HasValue || FallbackTarget.HasValue; } }

        public static CommandDoctrineOrder Create(
            string nodeId,
            CommandNodeRole role,
            CommandTaskType task,
            string objectiveId,
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint supportTarget,
            DoctrineTargetPoint fallbackTarget,
            DoctrineAllowedIdleReason allowedIdle,
            float minCommitUntilSeconds,
            float issuedAtSeconds,
            float confidence01,
            string reason)
        {
            return new CommandDoctrineOrder(
                nodeId,
                role,
                task,
                objectiveId,
                primaryTarget,
                supportTarget,
                fallbackTarget,
                allowedIdle,
                minCommitUntilSeconds,
                issuedAtSeconds,
                confidence01,
                reason);
        }

        public CommandDoctrineOrder WithAllowedIdle(DoctrineAllowedIdleReason reason)
        {
            return new CommandDoctrineOrder(
                NodeId,
                Role,
                Task,
                ObjectiveId,
                PrimaryTarget,
                SupportTarget,
                FallbackTarget,
                reason,
                MinCommitUntilSeconds,
                IssuedAtSeconds,
                Confidence01,
                Reason);
        }

        private static string SanitizeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) && value > 0f ? value : 0f;
        }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
