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
        public TacticalApproachAvenueEstimate ApproachAvenue { get; }
        /// <summary>BC12 decision 4: the terrain picture's strength of the ground at the objective against the own side's approach to it (0 without a picture).</summary>
        public float TerrainStrength { get; }
        /// <summary>BC12 decision 4: the speed-cost difficulty of the 300 m the own side crosses to reach the objective, ending at it (0 without a picture).</summary>
        public float ApproachDifficulty { get; }

        public ObjectiveObservationInput(
            string objectiveId,
            TacticalObjectiveType type,
            TacticalObjectiveSource source,
            TacticalMapPoint location,
            float sourceConfidence,
            float value,
            bool typeAnchorVerified)
            : this(
                objectiveId,
                type,
                source,
                location,
                sourceConfidence,
                value,
                typeAnchorVerified,
                TacticalApproachAvenueEstimate.None)
        {
        }

        public ObjectiveObservationInput(
            string objectiveId,
            TacticalObjectiveType type,
            TacticalObjectiveSource source,
            TacticalMapPoint location,
            float sourceConfidence,
            float value,
            bool typeAnchorVerified,
            TacticalApproachAvenueEstimate approachAvenue)
            : this(objectiveId, type, source, location, sourceConfidence, value, typeAnchorVerified, approachAvenue, 0f, 0f)
        {
        }

        public ObjectiveObservationInput(
            string objectiveId,
            TacticalObjectiveType type,
            TacticalObjectiveSource source,
            TacticalMapPoint location,
            float sourceConfidence,
            float value,
            bool typeAnchorVerified,
            TacticalApproachAvenueEstimate approachAvenue,
            float terrainStrength,
            float approachDifficulty)
        {
            TerrainStrength = Clamp01(terrainStrength);
            ApproachDifficulty = Clamp01(approachDifficulty);
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId;
            Type = type;
            Source = source;
            Location = location;
            SourceConfidence = Clamp01(sourceConfidence);
            Value = SanitizeValue(value);
            TypeAnchorVerified = typeAnchorVerified;
            ApproachAvenue = approachAvenue.HasAvenue ? approachAvenue : TacticalApproachAvenueEstimate.None;
        }

        /// <summary>The observation with an approach avenue matched (confidence raised by the caller).</summary>
        public ObjectiveObservationInput WithAvenue(float sourceConfidence, TacticalApproachAvenueEstimate avenue)
        {
            return new ObjectiveObservationInput(ObjectiveId, Type, Source, Location, sourceConfidence, Value, TypeAnchorVerified,
                avenue, TerrainStrength, ApproachDifficulty);
        }

        /// <summary>
        /// The terrain picture's reading of the objective (BC12 decision 3): its classified type, verified by the picture
        /// (<see cref="TacticalObjectiveSourceModel.Normalize"/> then keeps it), and the terrain numbers of decision 4.
        /// </summary>
        public ObjectiveObservationInput WithTerrain(TacticalObjectiveType type, bool typeAnchorVerified, float terrainStrength, float approachDifficulty)
        {
            return new ObjectiveObservationInput(ObjectiveId, type, Source, Location, SourceConfidence, Value, typeAnchorVerified,
                ApproachAvenue, terrainStrength, approachDifficulty);
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
