namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalOperationShape
    {
        SingleMainEffort,
        SequentialObjectives,
        ParallelObjectives,
        FixAndFlank,
        DefensiveNetwork,
        DelayAndFallback
    }

    public enum TacticalOperationPhase
    {
        Planning,
        Scouting,
        Forming,
        Committed,
        Exploiting,
        Consolidating,
        Aborting,
        Complete
    }

    public enum TacticalObjectiveStatus
    {
        Unknown,
        Scouting,
        WeaklyHeld,
        StronglyHeld,
        Contested,
        Secured,
        Lost
    }

    public enum TacticalReassessmentTier
    {
        Continue,
        SoftAbortReview,
        HardAbort
    }

    public readonly struct ObjectiveRecord
    {
        public ObjectiveObservationInput Observation { get; }
        public TacticalObjectiveStatus Status { get; }
        public float EnemyStrength { get; }
        public float FriendlyAssignedStrength { get; }
        public bool HasUsableStrengthEvidence { get; }

        public ObjectiveRecord(
            ObjectiveObservationInput observation,
            TacticalObjectiveStatus status,
            float enemyStrength,
            float friendlyAssignedStrength)
        {
            Observation = TacticalObjectiveSourceModel.Normalize(observation);
            Status = status;
            EnemyStrength = SanitizeFloorZero(enemyStrength);
            FriendlyAssignedStrength = SanitizeFloorZero(friendlyAssignedStrength);
            HasUsableStrengthEvidence = IsFiniteNonnegative(enemyStrength) &&
                IsFiniteNonnegative(friendlyAssignedStrength) &&
                (enemyStrength > 0f || friendlyAssignedStrength > 0f);
        }

        private static float SanitizeFloorZero(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        private static bool IsFiniteNonnegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }

    public readonly struct OperationRecord
    {
        public TacticalOperationShape Shape { get; }
        public TacticalOperationPhase Phase { get; }
        public string PrimaryObjectiveId { get; }
        public float MinimumCommitSeconds { get; }

        public OperationRecord(
            TacticalOperationShape shape,
            TacticalOperationPhase phase,
            string primaryObjectiveId,
            float minimumCommitSeconds)
        {
            Shape = shape;
            Phase = phase;
            PrimaryObjectiveId = string.IsNullOrWhiteSpace(primaryObjectiveId) ? "objective-unknown" : primaryObjectiveId;
            MinimumCommitSeconds = SanitizeFloorZero(minimumCommitSeconds);
        }

        private static float SanitizeFloorZero(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }
    }

    public static class TacticalOperationsLedgerModel
    {
        public static TacticalReassessmentTier ReassessCommittedOperation(
            float progressStalledSeconds,
            float confidence,
            float odds,
            bool forceCollapsed,
            bool objectiveSecured)
        {
            if (forceCollapsed || objectiveSecured)
            {
                return TacticalReassessmentTier.HardAbort;
            }

            float stalledSeconds = SanitizeElapsedSeconds(progressStalledSeconds);
            float sanitizedConfidence = SanitizeFiniteOrZero(confidence);
            float sanitizedOdds = SanitizeFiniteOrZero(odds);

            if (stalledSeconds >= 300f || sanitizedConfidence < 0.35f || sanitizedOdds < 0.65f)
            {
                return TacticalReassessmentTier.SoftAbortReview;
            }

            return TacticalReassessmentTier.Continue;
        }

        private static float SanitizeElapsedSeconds(float value)
        {
            if (float.IsPositiveInfinity(value)) return float.MaxValue;
            if (float.IsNaN(value) || float.IsNegativeInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        private static float SanitizeFiniteOrZero(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
