using System;
using WhiskeyRealism.Tactical.Battle;

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
            string reason,
            bool terrainLine = false,
            bool anchoredLeft = false,
            bool anchoredRight = false,
            float lineDirX = 0f,
            float lineDirZ = 0f,
            float lineHalfLength = 0f,
            bool ownFront = false)
        {
            HasAnchor = hasAnchor && center.HasValue && approachAvenue.HasAvenue;
            Center = HasAnchor ? center : DoctrineTargetPoint.None;
            ApproachAvenue = HasAnchor ? approachAvenue : TacticalApproachAvenueEstimate.None;
            FrontageWidth = SanitizeNonNegative(frontageWidth);
            DefensiveArtilleryDepth = SanitizeNonNegative(defensiveArtilleryDepth);
            CorridorAnchor = HasAnchor && corridorAnchor;
            OnTerrainLine = HasAnchor && terrainLine && !corridorAnchor;
            OnOwnFront = HasAnchor && ownFront && !corridorAnchor && !terrainLine;
            AnchoredLeft = OnTerrainLine && anchoredLeft;
            AnchoredRight = OnTerrainLine && anchoredRight;
            bool hasDirection = OnTerrainLine && lineHalfLength > 0f && (lineDirX != 0f || lineDirZ != 0f);
            LineDirX = hasDirection ? lineDirX : 0f;
            LineDirZ = hasDirection ? lineDirZ : 0f;
            LineHalfLength = hasDirection ? lineHalfLength : 0f;
            Reason = string.IsNullOrWhiteSpace(reason) ? "defensive-line-none" : reason;
        }

        public bool HasAnchor { get; }
        public DoctrineTargetPoint Center { get; }
        public TacticalApproachAvenueEstimate ApproachAvenue { get; }
        public float FrontageWidth { get; }
        public float DefensiveArtilleryDepth { get; }
        public bool CorridorAnchor { get; }
        /// <summary>BC12: the anchor is a terrain line (crest, wall, river ...) from the terrain picture, not an approach intercept.</summary>
        public bool OnTerrainLine { get; }
        /// <summary>The anchor is the side's own front (PlanOwnFront): the centre of its placed nodes, the axis from the enemy line to it; the line slots stand on it.</summary>
        public bool OnOwnFront { get; }
        /// <summary>BC12: the +L end of the line (L = (-Dz, Dx) of the approach axis; command index 0) rests on a river or crest.</summary>
        public bool AnchoredLeft { get; }
        /// <summary>BC12: the -L end of the line (command index n-1) rests on a river or crest.</summary>
        public bool AnchoredRight { get; }
        /// <summary>BC12: unit direction of the terrain line itself (a wall's cover segment, a crest or river run); 0 when the anchor is not a terrain line.</summary>
        public float LineDirX { get; }
        public float LineDirZ { get; }
        /// <summary>BC12: half the terrain line's own length (m), which can be less than half the frontage; 0 when not a terrain line.</summary>
        public float LineHalfLength { get; }
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
                CorridorAnchor ? Math.Max(objective.TerrainStrength, TerrainPicture.AnchorStrength) : objective.TerrainStrength,
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

        /// <summary>The anchor over the planning picture (null when the flag is off or the picture is not ready).</summary>
        public static TacticalDefensiveLineAnchor Plan(
            BattlefieldObjectiveEstimate objective,
            TacticalBattlefrontSnapshot battlefront,
            int nodeCount)
        {
            return Plan(objective, battlefront, nodeCount, TerrainPicture.CurrentForPlanning());
        }

        /// <summary>
        /// BC12 decision 6: a strong terrain objective with no corridor evidence takes the best terrain line near it as its anchor
        /// (reason terrain-line:kind[:anchored-left|:anchored-right]); without a picture or a line it stays preserved.
        /// <paramref name="reverseSlope"/> holds a crest 80 m behind it instead of at its forward edge.
        /// </summary>
        public static TacticalDefensiveLineAnchor Plan(
            BattlefieldObjectiveEstimate objective,
            TacticalBattlefrontSnapshot battlefront,
            int nodeCount,
            TerrainPicture picture,
            bool reverseSlope = false)
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
                return TerrainLineAnchor(objective, avenue, picture, reverseSlope)
                    ?? TacticalDefensiveLineAnchor.None("defensive-line-terrain-anchor-preserved");

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

        private const float MaxTerrainFrontage = 760f;
        private const float OwnFrontAvenueConfidence = 0.80f;

        /// <summary>
        /// A Defender whose objective is the enemy line (the visible-enemy-line fallback, <see cref="TacticalObjectiveType.EnemyLine"/>) has no
        /// line to defend in that objective: every slot laid relative to it lies on the enemy's side of its own front (smoke 2026-10-17: a
        /// Defender formed 210 m short of the enemy line and 325 m past it). It stands on its own front instead: the centre is the mean of its
        /// placed nodes, the approach axis runs from the enemy line to that centre, and the frontage is the placed nodes' spread (at least the
        /// minimum, at most 180 m a node), so the line slots, the reserve post and the flank guards all lie on the line or behind it.
        /// None without a placed node, or when the front is on the enemy line.
        /// </summary>
        public static TacticalDefensiveLineAnchor PlanOwnFront(
            BattlefieldObjectiveEstimate objective,
            CommandNodeOperationalState[] nodes,
            TacticalBattlefrontSnapshot battlefront)
        {
            if (objective.Type != TacticalObjectiveType.EnemyLine)
                return TacticalDefensiveLineAnchor.None("defensive-line-not-enemy-line");

            double sumX = 0d, sumZ = 0d;
            int placed = 0;
            for (int i = 0; nodes != null && i < nodes.Length; i++)
            {
                if (nodes[i].Role == CommandNodeRole.Unknown || !nodes[i].HasPosition) continue;
                sumX += nodes[i].X;
                sumZ += nodes[i].Z;
                placed++;
            }

            if (placed == 0) return TacticalDefensiveLineAnchor.None("defensive-line-no-placed-node");
            float centerX = (float)(sumX / placed);
            float centerZ = (float)(sumZ / placed);
            if (Distance(objective.X, objective.Z, centerX, centerZ) < 1f)
                return TacticalDefensiveLineAnchor.None("defensive-line-on-enemy-line");

            TacticalApproachAvenueEstimate avenue = TacticalApproachAvenueEstimate.Create(
                new TacticalMapPoint(objective.X, objective.Z),
                new TacticalMapPoint(centerX, centerZ),
                TacticalApproachAvenueSource.VisibleEnemyLine,
                OwnFrontAvenueConfidence,
                roadAnchored: false,
                crossingAnchored: false,
                "own-front");
            float spread = battlefront.HasFrontage ? battlefront.FrontageWidth : 0f;
            float frontage = Math.Max(MinimumFrontageWidth, Math.Min(spread, Math.Max(1, placed) * 180f));
            return new TacticalDefensiveLineAnchor(
                true,
                DoctrineTargetPoint.From(centerX, centerZ),
                avenue,
                frontage,
                DefensiveArtilleryDepth,
                corridorAnchor: false,
                "own-front",
                ownFront: true);
        }

        // Null when there is no usable line. The threat axis is the reverse of the avenue axis (the enemy's travel direction).
        private static TacticalDefensiveLineAnchor? TerrainLineAnchor(
            BattlefieldObjectiveEstimate objective,
            TacticalApproachAvenueEstimate avenue,
            TerrainPicture picture,
            bool reverseSlope)
        {
            if (picture == null || !picture.Ready) return null;
            float threatX = -avenue.AxisX, threatZ = -avenue.AxisZ;
            TerrainLine line = TacticalTerrainLinePlanner.BestLine(picture, objective.X, objective.Z, threatX, threatZ, MaxTerrainFrontage);
            if (!line.Has) return null;

            TacticalTerrainLinePlanner.AnchorPoint(in line, threatX, threatZ, reverseSlope, out float cx, out float cz);
            float frontage = Math.Max(MinimumFrontageWidth, Math.Min(line.Length, MaxTerrainFrontage));

            // The line's ends: L = (-Dz, Dx) of the approach axis D (command index 0 stands on +L).
            float lx = -avenue.AxisZ, lz = avenue.AxisX;
            float half = frontage * 0.5f;
            bool includeCrest = line.Kind != TerrainLineKind.Crest;
            bool left = TacticalTerrainLinePlanner.FlankAnchored(picture, cx + lx * half, cz + lz * half, lx, lz, includeCrest);
            bool right = TacticalTerrainLinePlanner.FlankAnchored(picture, cx - lx * half, cz - lz * half, -lx, -lz, includeCrest);

            string reason = "terrain-line:" + KindName(line.Kind) + (left ? ":anchored-left" : string.Empty) + (right ? ":anchored-right" : string.Empty);
            return new TacticalDefensiveLineAnchor(
                true,
                DoctrineTargetPoint.From(cx, cz),
                avenue,
                frontage,
                DefensiveArtilleryDepth,
                corridorAnchor: false,
                reason,
                terrainLine: true,
                anchoredLeft: left,
                anchoredRight: right,
                lineDirX: (line.X1 - line.X0) / Math.Max(0.001f, line.Length),
                lineDirZ: (line.Z1 - line.Z0) / Math.Max(0.001f, line.Length),
                lineHalfLength: line.Length * 0.5f);
        }

        private static string KindName(TerrainLineKind kind)
        {
            switch (kind)
            {
                case TerrainLineKind.Crest: return "crest";
                case TerrainLineKind.Wall: return "wall";
                case TerrainLineKind.SunkenRoad: return "sunken";
                case TerrainLineKind.Fence: return "fence";
                case TerrainLineKind.Embankment: return "embankment";
                case TerrainLineKind.River: return "river";
                default: return "line";
            }
        }

        private static bool IsStrongTerrainAnchor(BattlefieldObjectiveEstimate objective)
        {
            return objective.Type == TacticalObjectiveType.Ridge ||
                objective.Type == TacticalObjectiveType.Town ||
                objective.TerrainStrength >= TerrainPicture.AnchorStrength;
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
