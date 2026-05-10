namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalContactSource
    {
        VisualContact = 0,
        RecentFire = 1,
        ObjectivePressure = 2,
        FriendlyRoutedFromArea = 3,
        InferredMovement = 4,
    }

    public readonly struct ContactObservationInput
    {
        public TacticalContactSource Source { get; }
        public float EstimatedStrength { get; }
        public float SecondsSinceObserved { get; }
        public bool CurrentlyVisible { get; }
        public bool ObjectiveLinked { get; }
        public bool ScoutTaskLinked { get; }

        public ContactObservationInput(
            TacticalContactSource source,
            float estimatedStrength,
            float secondsSinceObserved,
            bool currentlyVisible,
            bool objectiveLinked,
            bool scoutTaskLinked)
        {
            Source = source;
            EstimatedStrength = SanitizeFloorZero(estimatedStrength);
            SecondsSinceObserved = SanitizeFloorZero(secondsSinceObserved);
            CurrentlyVisible = currentlyVisible;
            ObjectiveLinked = objectiveLinked;
            ScoutTaskLinked = scoutTaskLinked;
        }

        private static float SanitizeFloorZero(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }
    }

    public readonly struct EnemyContactReport
    {
        public ContactObservationInput Input { get; }
        public float Confidence { get; }

        public EnemyContactReport(ContactObservationInput input, float confidence)
        {
            Input = input;
            Confidence = TacticalVisionModel.Clamp01(confidence);
        }
    }

    public static class TacticalVisionModel
    {
        public static EnemyContactReport BuildContact(ContactObservationInput input, float staleAfterSeconds)
        {
            float baseWeight = SourceWeight(input.Source);
            float bonuses = 0f;
            if (input.CurrentlyVisible) bonuses += 0.10f;
            if (input.ObjectiveLinked) bonuses += 0.05f;
            if (input.ScoutTaskLinked) bonuses += 0.05f;

            float staleAfter = SanitizeFinite(staleAfterSeconds);
            if (staleAfter < 1f) staleAfter = 1f;

            float staleness = Clamp01(input.SecondsSinceObserved / staleAfter);
            float confidence = Clamp01((baseWeight + bonuses) * (1f - staleness));
            return new EnemyContactReport(input, confidence);
        }

        internal static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SourceWeight(TacticalContactSource source)
        {
            switch (source)
            {
                case TacticalContactSource.VisualContact:
                    return 0.90f;
                case TacticalContactSource.RecentFire:
                    return 0.65f;
                case TacticalContactSource.ObjectivePressure:
                    return 0.55f;
                case TacticalContactSource.FriendlyRoutedFromArea:
                    return 0.50f;
                case TacticalContactSource.InferredMovement:
                    return 0.35f;
                default:
                    return 0.25f;
            }
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
