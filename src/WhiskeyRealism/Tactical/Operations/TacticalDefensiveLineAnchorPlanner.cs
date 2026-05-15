using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct TacticalDefensiveLineAnchor
    {
        public TacticalDefensiveLineAnchor(
            bool hasAnchor,
            DoctrineTargetPoint center,
            TacticalApproachAvenueEstimate approachAvenue,
            float frontageWidth,
            float defensiveArtilleryDepth,
            bool corridorAnchor,
            string reason)
        {
            HasAnchor = hasAnchor && center.HasValue && approachAvenue.HasAvenue;
            Center = HasAnchor ? center : DoctrineTargetPoint.None;
            ApproachAvenue = HasAnchor ? approachAvenue : TacticalApproachAvenueEstimate.None;
            FrontageWidth = SanitizeNonNegative(frontageWidth);
            DefensiveArtilleryDepth = SanitizeNonNegative(defensiveArtilleryDepth);
            CorridorAnchor = HasAnchor && corridorAnchor;
            Reason = string.IsNullOrWhiteSpace(reason) ? "defensive-line-none" : reason;
        }

        public bool HasAnchor { get; }
        public DoctrineTargetPoint Center { get; }
        public TacticalApproachAvenueEstimate ApproachAvenue { get; }
        public float FrontageWidth { get; }
        public float DefensiveArtilleryDepth { get; }
        public bool CorridorAnchor { get; }
        public string Reason { get; }

        public static TacticalDefensiveLineAnchor None(string reason)
        {
            return new TacticalDefensiveLineAnchor(
                false,
                DoctrineTargetPoint.None,
                TacticalApproachAvenueEstimate.None,
                0f,
                0f,
                false,
                reason);
        }

        public BattlefieldObjectiveEstimate AsLineObjective(BattlefieldObjectiveEstimate objective)
        {
            if (!HasAnchor) return objective;

            TacticalObjectiveType lineType = CorridorAnchor
                ? TacticalObjectiveType.ChokePoint
                : objective.Type;

            return new BattlefieldObjectiveEstimate(
                objective.ObjectiveId,
                lineType,
                objective.EnemyStrength,
                objective.Confidence01,
                objective.MainLineExposed,
                objective.Value,
                Center.X,
                Center.Z,
                CorridorAnchor ? Math.Max(objective.TerrainStrength, 0.55f) : objective.TerrainStrength,
                objective.ApproachDifficulty,
                TacticalApproachAvenueEstimate.Create(
                    ApproachAvenue.Origin,
                    new TacticalMapPoint(Center.X, Center.Z),
                    ApproachAvenue.Source,
                    ApproachAvenue.Confidence01,
                    ApproachAvenue.RoadAnchored,
                    ApproachAvenue.CrossingAnchored,
                    ApproachAvenue.Reason));
        }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value;
        }
    }

    public static class TacticalDefensiveLineAnchorPlanner
    {
        private const float MinimumAvenueConfidence = 0.50f;
        private const float MinimumAvenueLength = 350f;
        private const float RoadInterceptDistance = 360f;
        private const float CrossingInterceptDistance = 440f;
        private const float GenericAvenueInterceptDistance = 300f;
        private const float RoadFrontageWidth = 620f;
        private const float CrossingFrontageWidth = 520f;
        private const float GenericAvenueFrontageWidth = 760f;
        private const float MinimumFrontageWidth = 350f;
        private const float DefensiveArtilleryDepth = 260f;

        public static TacticalDefensiveLineAnchor Plan(
            BattlefieldObjectiveEstimate objective,
            TacticalBattlefrontSnapshot battlefront,
            int nodeCount)
        {
            TacticalApproachAvenueEstimate avenue = objective.ApproachAvenue;
            if (!avenue.HasAvenue)
                return TacticalDefensiveLineAnchor.None("defensive-line-no-avenue");

            if (avenue.Confidence01 < MinimumAvenueConfidence)
                return TacticalDefensiveLineAnchor.None("defensive-line-low-confidence");

            if (objective.MainLineExposed)
                return TacticalDefensiveLineAnchor.None("defensive-line-visible-enemy-line");

            bool strongTerrainAnchor = IsStrongTerrainAnchor(objective);
            bool explicitChokeObjective = IsExplicitChokeObjective(objective);
            bool corridorEvidence = avenue.CrossingAnchored ||
                explicitChokeObjective ||
                (!strongTerrainAnchor && avenue.RoadAnchored);
            if (strongTerrainAnchor && !corridorEvidence)
                return TacticalDefensiveLineAnchor.None("defensive-line-terrain-anchor-preserved");

            float distance = Distance(avenue.Origin.X, avenue.Origin.Z, objective.X, objective.Z);
            if (distance < MinimumAvenueLength)
                return TacticalDefensiveLineAnchor.None("defensive-line-short-avenue");

            float desiredDistance = avenue.CrossingAnchored
                ? CrossingInterceptDistance
                : avenue.RoadAnchored ? RoadInterceptDistance : GenericAvenueInterceptDistance;
            float interceptDistance = Math.Min(desiredDistance, Math.Max(160f, distance * 0.55f));
            float centerX = objective.X - avenue.AxisX * interceptDistance;
            float centerZ = objective.Z - avenue.AxisZ * interceptDistance;

            float frontage = avenue.CrossingAnchored
                ? CrossingFrontageWidth
                : avenue.RoadAnchored ? RoadFrontageWidth : GenericAvenueFrontageWidth;
            if (battlefront.HasFrontage && battlefront.FrontageWidth > 0f)
            {
                frontage = Math.Min(frontage, Math.Max(MinimumFrontageWidth, battlefront.FrontageWidth));
            }

            int safeNodeCount = Math.Max(1, nodeCount);
            frontage = Math.Max(MinimumFrontageWidth, Math.Min(frontage, safeNodeCount * 180f));

            string reason = "approach-intercept";
            if (avenue.RoadAnchored) reason += ":road";
            if (avenue.CrossingAnchored) reason += ":crossing";
            reason += ":" + avenue.Source.ToString().ToLowerInvariant();

            return new TacticalDefensiveLineAnchor(
                true,
                DoctrineTargetPoint.From(centerX, centerZ),
                avenue,
                frontage,
                DefensiveArtilleryDepth,
                corridorAnchor: corridorEvidence,
                reason);
        }

        private static bool IsStrongTerrainAnchor(BattlefieldObjectiveEstimate objective)
        {
            return objective.Type == TacticalObjectiveType.Ridge ||
                objective.Type == TacticalObjectiveType.Town ||
                objective.TerrainStrength >= 0.55f;
        }

        private static bool IsExplicitChokeObjective(BattlefieldObjectiveEstimate objective)
        {
            return objective.Type == TacticalObjectiveType.Bridge ||
                objective.Type == TacticalObjectiveType.Ford ||
                objective.Type == TacticalObjectiveType.RoadJunction ||
                objective.Type == TacticalObjectiveType.ChokePoint;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            double dx = (double)ax - bx;
            double dz = (double)az - bz;
            double value = Math.Sqrt(dx * dx + dz * dz);
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0f;
            return value > float.MaxValue ? float.MaxValue : (float)value;
        }
    }
}
