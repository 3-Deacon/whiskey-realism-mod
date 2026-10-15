using System;

namespace WhiskeyRealism.Tactical.Operations
{

    public readonly struct BattlefieldObjectiveEstimate
    {
        public string ObjectiveId { get; }
        public TacticalObjectiveType Type { get; }
        public float EnemyStrength { get; }
        public float Confidence01 { get; }
        public bool MainLineExposed { get; }
        public float Value { get; }
        public float X { get; }
        public float Z { get; }
        public float TerrainStrength { get; }
        public float ApproachDifficulty { get; }
        public TacticalApproachAvenueEstimate ApproachAvenue { get; }

        public BattlefieldObjectiveEstimate(
            string objectiveId,
            TacticalObjectiveType type,
            float enemyStrength,
            float confidence01,
            bool mainLineExposed,
            float value,
            float x,
            float z,
            float terrainStrength,
            float approachDifficulty)
            : this(
                objectiveId,
                type,
                enemyStrength,
                confidence01,
                mainLineExposed,
                value,
                x,
                z,
                terrainStrength,
                approachDifficulty,
                TacticalApproachAvenueEstimate.None)
        {
        }

        public BattlefieldObjectiveEstimate(
            string objectiveId,
            TacticalObjectiveType type,
            float enemyStrength,
            float confidence01,
            bool mainLineExposed,
            float value,
            float x,
            float z,
            float terrainStrength,
            float approachDifficulty,
            TacticalApproachAvenueEstimate approachAvenue)
        {
            ObjectiveId = string.IsNullOrWhiteSpace(objectiveId) ? "objective-unknown" : objectiveId;
            Type = type;
            EnemyStrength = SanitizeFloorZero(enemyStrength);
            Confidence01 = Clamp01(confidence01);
            MainLineExposed = mainLineExposed;
            Value = Clamp01(value);
            X = SanitizeFinite(x);
            Z = SanitizeFinite(z);
            TerrainStrength = Clamp01(terrainStrength);
            ApproachDifficulty = Clamp01(approachDifficulty);
            ApproachAvenue = approachAvenue.HasAvenue ? approachAvenue : TacticalApproachAvenueEstimate.None;
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

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }

    public readonly struct BattlefieldPictureSnapshot
    {
        public BattlefieldObjectiveEstimate[] Objectives { get; }

        public BattlefieldPictureSnapshot(BattlefieldObjectiveEstimate[] objectives)
        {
            Objectives = objectives ?? Array.Empty<BattlefieldObjectiveEstimate>();
        }
    }
}
