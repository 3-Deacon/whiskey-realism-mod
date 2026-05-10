using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct ForceAvailabilitySnapshot
    {
        public float AvailableStrength { get; }
        public float ReserveFraction { get; }

        public ForceAvailabilitySnapshot(float availableStrength, float reserveFraction)
        {
            AvailableStrength = SanitizeFloorZero(availableStrength);
            ReserveFraction = Clamp01(reserveFraction);
        }

        private static float SanitizeFloorZero(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    public static class TacticalOperationSelectionModel
    {
        public static TacticalOperationShape Select(
            ObjectiveRecord first,
            ObjectiveRecord second,
            ForceAvailabilitySnapshot force,
            PersonalityVector personality)
        {
            float aggression = Clamp01((personality.Aggression + 1f) * 0.5f);
            bool firstUsable = HasUsableObjectiveEvidence(first);
            bool secondUsable = HasUsableObjectiveEvidence(second);
            bool firstWeak = firstUsable && first.EnemyStrength <= Max(1f, first.FriendlyAssignedStrength) * 0.75f;
            bool secondWeak = secondUsable && second.EnemyStrength <= Max(1f, second.FriendlyAssignedStrength) * 0.75f;
            bool firstStrong = firstUsable && !firstWeak;
            bool reserveSafe = force.ReserveFraction >= (aggression > 0.65f ? 0.15f : 0.25f);

            if (firstWeak && secondWeak && reserveSafe && aggression >= 0.45f)
            {
                return TacticalOperationShape.ParallelObjectives;
            }

            if (firstStrong && secondWeak)
            {
                return TacticalOperationShape.FixAndFlank;
            }

            if (force.AvailableStrength < first.EnemyStrength + second.EnemyStrength)
            {
                return TacticalOperationShape.SequentialObjectives;
            }

            return TacticalOperationShape.SingleMainEffort;
        }

        private static bool HasUsableObjectiveEvidence(ObjectiveRecord record)
        {
            return record.HasUsableStrengthEvidence &&
                record.Status != TacticalObjectiveStatus.Unknown &&
                record.Observation.SourceConfidence > 0f;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float Max(float left, float right)
        {
            return left > right ? left : right;
        }
    }
}
