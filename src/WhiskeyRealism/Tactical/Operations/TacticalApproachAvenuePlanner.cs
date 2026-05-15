using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalApproachAvenueSource
    {
        None = 0,
        EntryPoint = 1,
        ScheduledArrival = 2,
        DeploymentGroup = 3,
        VisibleEnemyLine = 4,
        MovementAnchor = 5,
        SyntheticMapSide = 6
    }

    public readonly struct TacticalApproachAvenueObservation
    {
        public TacticalApproachAvenueObservation(
            TacticalApproachAvenueSource source,
            int ownerAllianceId,
            float originX,
            float originZ,
            float targetX,
            float targetZ,
            float confidence01,
            bool roadAnchored,
            bool crossingAnchored,
            string reason)
        {
            Source = source;
            OwnerAllianceId = ownerAllianceId;
            Origin = new TacticalMapPoint(originX, originZ);
            Target = new TacticalMapPoint(targetX, targetZ);
            Confidence01 = Clamp01(confidence01);
            RoadAnchored = roadAnchored;
            CrossingAnchored = crossingAnchored;
            Reason = string.IsNullOrWhiteSpace(reason) ? "avenue-observation" : reason;
        }

        public TacticalApproachAvenueSource Source { get; }
        public int OwnerAllianceId { get; }
        public TacticalMapPoint Origin { get; }
        public TacticalMapPoint Target { get; }
        public float Confidence01 { get; }
        public bool RoadAnchored { get; }
        public bool CrossingAnchored { get; }
        public string Reason { get; }

        internal bool HasUsablePoints
        {
            get { return IsUsable(Origin) && IsUsable(Target); }
        }

        private static bool IsUsable(TacticalMapPoint point)
        {
            return !float.IsNaN(point.X) &&
                !float.IsNaN(point.Z) &&
                !float.IsInfinity(point.X) &&
                !float.IsInfinity(point.Z) &&
                (Math.Abs(point.X) >= 0.01f || Math.Abs(point.Z) >= 0.01f);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    public readonly struct TacticalApproachAvenueEstimate
    {
        private TacticalApproachAvenueEstimate(
            bool hasAvenue,
            TacticalMapPoint origin,
            TacticalMapPoint objective,
            float axisX,
            float axisZ,
            TacticalApproachAvenueSource source,
            float confidence01,
            bool roadAnchored,
            bool crossingAnchored,
            string reason)
        {
            HasAvenue = hasAvenue;
            Origin = origin;
            Objective = objective;
            AxisX = SanitizeAxis(axisX);
            AxisZ = SanitizeAxis(axisZ);
            Source = source;
            Confidence01 = Clamp01(confidence01);
            RoadAnchored = roadAnchored;
            CrossingAnchored = crossingAnchored;
            Reason = string.IsNullOrWhiteSpace(reason) ? "approach-avenue-unspecified" : reason;
        }

        public bool HasAvenue { get; }
        public TacticalMapPoint Origin { get; }
        public TacticalMapPoint Objective { get; }
        public float AxisX { get; }
        public float AxisZ { get; }
        public TacticalApproachAvenueSource Source { get; }
        public float Confidence01 { get; }
        public bool RoadAnchored { get; }
        public bool CrossingAnchored { get; }
        public string Reason { get; }

        public static TacticalApproachAvenueEstimate None
        {
            get
            {
                return new TacticalApproachAvenueEstimate(
                    false,
                    default(TacticalMapPoint),
                    default(TacticalMapPoint),
                    0f,
                    1f,
                    TacticalApproachAvenueSource.None,
                    0f,
                    false,
                    false,
                    "approach-avenue-none");
            }
        }

        public static TacticalApproachAvenueEstimate Create(
            TacticalMapPoint origin,
            TacticalMapPoint objective,
            TacticalApproachAvenueSource source,
            float confidence01,
            bool roadAnchored,
            bool crossingAnchored,
            string reason)
        {
            double dx = (double)objective.X - origin.X;
            double dz = (double)objective.Z - origin.Z;
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (double.IsNaN(length) || double.IsInfinity(length) || length < 1d)
            {
                return None;
            }

            return new TacticalApproachAvenueEstimate(
                true,
                origin,
                objective,
                (float)(dx / length),
                (float)(dz / length),
                source,
                confidence01,
                roadAnchored,
                crossingAnchored,
                "approach-avenue-" + source.ToString().ToLowerInvariant() + ":" + reason);
        }

        private static float SanitizeAxis(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value > 1f) return 1f;
            return value < -1f ? -1f : value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    public static class TacticalApproachAvenuePlanner
    {
        public static TacticalApproachAvenueEstimate SelectBest(
            BattlefieldObjectiveEstimate objective,
            int ownAllianceId,
            int enemyAllianceId,
            TacticalApproachAvenueObservation[] observations)
        {
            if (observations == null || observations.Length == 0)
            {
                return TacticalApproachAvenueEstimate.None;
            }

            TacticalApproachAvenueObservation best = default(TacticalApproachAvenueObservation);
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < observations.Length; i++)
            {
                TacticalApproachAvenueObservation observation = observations[i];
                if (!observation.HasUsablePoints) continue;

                float score = Score(observation, objective, ownAllianceId, enemyAllianceId);
                if (score <= bestScore) continue;

                best = observation;
                bestScore = score;
            }

            if (float.IsNegativeInfinity(bestScore))
            {
                return TacticalApproachAvenueEstimate.None;
            }

            float confidence = Clamp01(best.Confidence01 + Math.Max(0f, bestScore - best.Confidence01) * 0.20f);
            return TacticalApproachAvenueEstimate.Create(
                best.Origin,
                new TacticalMapPoint(objective.X, objective.Z),
                best.Source,
                confidence,
                best.RoadAnchored,
                best.CrossingAnchored,
                best.Reason);
        }

        private static float Score(
            TacticalApproachAvenueObservation observation,
            BattlefieldObjectiveEstimate objective,
            int ownAllianceId,
            int enemyAllianceId)
        {
            float score = observation.Confidence01;
            if (observation.OwnerAllianceId == enemyAllianceId) score += 0.35f;
            else if (observation.OwnerAllianceId == ownAllianceId) score -= 0.40f;
            else if (observation.OwnerAllianceId < 0) score += 0.10f;

            if (observation.Source == TacticalApproachAvenueSource.ScheduledArrival) score += 0.15f;
            else if (observation.Source == TacticalApproachAvenueSource.EntryPoint) score += 0.10f;
            else if (observation.Source == TacticalApproachAvenueSource.DeploymentGroup) score += 0.08f;

            if (observation.RoadAnchored) score += 0.05f;
            if (observation.CrossingAnchored) score += 0.05f;

            float originDistance = Distance(observation.Origin.X, observation.Origin.Z, objective.X, objective.Z);
            float targetDistance = Distance(observation.Target.X, observation.Target.Z, objective.X, objective.Z);
            if (targetDistance < originDistance) score += 0.10f;
            if (originDistance < 100f) score -= 0.30f;

            return score;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            double dx = (double)ax - bx;
            double dz = (double)az - bz;
            double value = Math.Sqrt(dx * dx + dz * dz);
            if (double.IsNaN(value) || double.IsInfinity(value)) return float.MaxValue;
            return value > float.MaxValue ? float.MaxValue : (float)value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
