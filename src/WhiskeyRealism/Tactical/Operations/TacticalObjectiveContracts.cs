namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalObjectiveType
    {
        UnknownVanillaObjective = 0,
        VictoryPoint = 1,
        Bridge = 2,
        Ford = 3,
        RoadJunction = 4,
        Town = 5,
        Ridge = 6,
        ChokePoint = 7,
        EnemyLine = 8,
        FriendlyLine = 9,
        FallbackLine = 10,
        StagingArea = 11
    }

    public enum TacticalObjectiveSource
    {
        Unknown = 0,
        ObjectiveChain = 1,
        CurrentSetObjective = 2,
        VisibleEnemyLine = 3,
        FriendlyLineShape = 4,
        TerrainSample = 5,
        VerifiedSceneObject = 6
    }

    public readonly struct TacticalMapPoint
    {
        public float X { get; }
        public float Z { get; }

        public TacticalMapPoint(float x, float z)
        {
            X = SanitizeFinite(x);
            Z = SanitizeFinite(z);
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }

    public readonly struct ObjectiveObservationInput
    {
        public string ObjectiveId { get; }
        public TacticalObjectiveType Type { get; }
        public TacticalObjectiveSource Source { get; }
        public TacticalMapPoint Location { get; }
        public float SourceConfidence { get; }
        public float Value { get; }
        public bool TypeAnchorVerified { get; }

        public ObjectiveObservationInput(
            string objectiveId,
            TacticalObjectiveType type,
            TacticalObjectiveSource source,
            TacticalMapPoint location,
            float sourceConfidence,
            float value,
            bool typeAnchorVerified)
        {
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId;
            Type = type;
            Source = source;
            Location = location;
            SourceConfidence = Clamp01(sourceConfidence);
            Value = SanitizeValue(value);
            TypeAnchorVerified = typeAnchorVerified;
        }

        private static float Clamp01(float input)
        {
            if (float.IsNaN(input) || float.IsInfinity(input)) return 0f;
            if (input < 0f) return 0f;
            return input > 1f ? 1f : input;
        }

        private static float SanitizeValue(float input)
        {
            if (float.IsNaN(input) || float.IsInfinity(input)) return 0f;
            return input < 0f ? 0f : input;
        }
    }
}
