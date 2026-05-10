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
        public readonly float X;
        public readonly float Z;

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
        public readonly string objectiveId;
        public readonly TacticalObjectiveType type;
        public readonly TacticalObjectiveSource source;
        public readonly TacticalMapPoint location;
        public readonly float sourceConfidence;
        public readonly float value;
        public readonly bool typeAnchorVerified;

        public string ObjectiveId => objectiveId;
        public TacticalObjectiveType Type => type;
        public TacticalObjectiveSource Source => source;
        public TacticalMapPoint Location => location;
        public float SourceConfidence => sourceConfidence;
        public float Value => value;
        public bool TypeAnchorVerified => typeAnchorVerified;

        public ObjectiveObservationInput(
            string objectiveId,
            TacticalObjectiveType type,
            TacticalObjectiveSource source,
            TacticalMapPoint location,
            float sourceConfidence,
            float value,
            bool typeAnchorVerified)
        {
            this.objectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId;
            this.type = type;
            this.source = source;
            this.location = location;
            this.sourceConfidence = Clamp01(sourceConfidence);
            this.value = SanitizeValue(value);
            this.typeAnchorVerified = typeAnchorVerified;
        }

        private static float Clamp01(float input)
        {
            if (float.IsNaN(input)) return 0f;
            if (float.IsNegativeInfinity(input)) return 0f;
            if (float.IsPositiveInfinity(input)) return 1f;
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
