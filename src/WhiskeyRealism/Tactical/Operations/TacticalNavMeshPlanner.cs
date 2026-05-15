using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct TacticalPathQualitySample
    {
        public TacticalPathQualitySample(
            float x,
            float z,
            float roadPreference01,
            float slopeCost01,
            float congestion01,
            float chokeRisk01,
            float bridgeRisk01,
            float deadGround01,
            float friendlyBlocker01 = 0f,
            float threatExposure01 = 0f,
            float routeContinuity01 = 0f,
            float reservationPressure01 = 0f,
            float fallbackLaneConflict01 = 0f,
            float artilleryDanger01 = 0f)
        {
            X = IsFinite(x) ? x : 0f;
            Z = IsFinite(z) ? z : 0f;
            RoadPreference01 = Clamp01(roadPreference01);
            SlopeCost01 = Clamp01(slopeCost01);
            Congestion01 = Clamp01(congestion01);
            ChokeRisk01 = Clamp01(chokeRisk01);
            BridgeRisk01 = Clamp01(bridgeRisk01);
            DeadGround01 = Clamp01(deadGround01);
            FriendlyBlocker01 = Clamp01(friendlyBlocker01);
            ThreatExposure01 = Clamp01(threatExposure01);
            RouteContinuity01 = Clamp01(routeContinuity01);
            ReservationPressure01 = Clamp01(reservationPressure01);
            FallbackLaneConflict01 = Clamp01(fallbackLaneConflict01);
            ArtilleryDanger01 = Clamp01(artilleryDanger01);
        }

        public float X { get; }
        public float Z { get; }
        public float RoadPreference01 { get; }
        public float SlopeCost01 { get; }
        public float Congestion01 { get; }
        public float ChokeRisk01 { get; }
        public float BridgeRisk01 { get; }
        public float DeadGround01 { get; }
        public float FriendlyBlocker01 { get; }
        public float ThreatExposure01 { get; }
        public float RouteContinuity01 { get; }
        public float ReservationPressure01 { get; }
        public float FallbackLaneConflict01 { get; }
        public float ArtilleryDanger01 { get; }

        private static float Clamp01(float value)
        {
            if (!IsFinite(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class TacticalMovementCostField
    {
        public static float Score(CommandTaskType task, TacticalPathQualitySample sample, bool hasThreat)
        {
            float score = 0f;
            score += sample.RoadPreference01 * 220f;
            score += sample.DeadGround01 * (hasThreat ? 190f : 80f);
            score += sample.RouteContinuity01 * 320f;
            score -= sample.SlopeCost01 * 180f;
            score -= sample.Congestion01 * 220f;
            score -= sample.ChokeRisk01 * 280f;
            score -= sample.BridgeRisk01 * 160f;
            score -= sample.FriendlyBlocker01 * 360f;
            score -= sample.ThreatExposure01 * 260f;
            score -= sample.ReservationPressure01 * 340f;
            score -= sample.ArtilleryDanger01 * 300f;

            if (task == CommandTaskType.FallBackToLine)
                score -= sample.FallbackLaneConflict01 * 420f;
            else
                score -= sample.FallbackLaneConflict01 * 120f;

            return score;
        }
    }

    public readonly struct TacticalNavPlanInput
    {
        public TacticalNavPlanInput(
            CommandTaskType task,
            float ownX,
            float ownZ,
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint fallbackTarget,
            bool hasThreat,
            float threatX,
            float threatZ,
            bool hasCurrentWaypoint,
            float currentWaypointX,
            float currentWaypointZ,
            bool closeEngaged,
            float minWaypointDistance,
            float maxWaypointDistance)
            : this(
                task,
                ownX,
                ownZ,
                primaryTarget,
                fallbackTarget,
                hasThreat,
                threatX,
                threatZ,
                hasCurrentWaypoint,
                currentWaypointX,
                currentWaypointZ,
                closeEngaged,
                minWaypointDistance,
                maxWaypointDistance,
                null)
        {
        }

        public TacticalNavPlanInput(
            CommandTaskType task,
            float ownX,
            float ownZ,
            DoctrineTargetPoint primaryTarget,
            DoctrineTargetPoint fallbackTarget,
            bool hasThreat,
            float threatX,
            float threatZ,
            bool hasCurrentWaypoint,
            float currentWaypointX,
            float currentWaypointZ,
            bool closeEngaged,
            float minWaypointDistance,
            float maxWaypointDistance,
            TacticalPathQualitySample[] pathSamples)
        {
            Task = task;
            HasOwnPosition = IsFinite(ownX) && IsFinite(ownZ);
            OwnX = HasOwnPosition ? ownX : 0f;
            OwnZ = HasOwnPosition ? ownZ : 0f;
            PrimaryTarget = primaryTarget;
            FallbackTarget = fallbackTarget;
            HasThreat = hasThreat && IsFinite(threatX) && IsFinite(threatZ);
            ThreatX = HasThreat ? threatX : 0f;
            ThreatZ = HasThreat ? threatZ : 0f;
            HasCurrentWaypoint = hasCurrentWaypoint && IsFinite(currentWaypointX) && IsFinite(currentWaypointZ);
            CurrentWaypointX = HasCurrentWaypoint ? currentWaypointX : 0f;
            CurrentWaypointZ = HasCurrentWaypoint ? currentWaypointZ : 0f;
            CloseEngaged = closeEngaged;
            MinWaypointDistance = IsFinite(minWaypointDistance) && minWaypointDistance > 0f ? minWaypointDistance : 15f;
            MaxWaypointDistance = IsFinite(maxWaypointDistance) && maxWaypointDistance > MinWaypointDistance
                ? maxWaypointDistance
                : 2500f;
            PathSamples = pathSamples ?? Array.Empty<TacticalPathQualitySample>();
        }

        public CommandTaskType Task { get; }
        public bool HasOwnPosition { get; }
        public float OwnX { get; }
        public float OwnZ { get; }
        public DoctrineTargetPoint PrimaryTarget { get; }
        public DoctrineTargetPoint FallbackTarget { get; }
        public bool HasThreat { get; }
        public float ThreatX { get; }
        public float ThreatZ { get; }
        public bool HasCurrentWaypoint { get; }
        public float CurrentWaypointX { get; }
        public float CurrentWaypointZ { get; }
        public bool CloseEngaged { get; }
        public float MinWaypointDistance { get; }
        public float MaxWaypointDistance { get; }
        public TacticalPathQualitySample[] PathSamples { get; }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalNavPlanDecision
    {
        public TacticalNavPlanDecision(bool hasTarget, DoctrineTargetPoint target, string reason)
        {
            HasTarget = hasTarget && target.HasValue;
            Target = HasTarget ? target : DoctrineTargetPoint.None;
            Reason = string.IsNullOrWhiteSpace(reason) ? "none" : reason;
        }

        public bool HasTarget { get; }
        public DoctrineTargetPoint Target { get; }
        public string Reason { get; }

        public static TacticalNavPlanDecision None(string reason)
        {
            return new TacticalNavPlanDecision(false, DoctrineTargetPoint.None, reason);
        }
    }

    public static class TacticalNavMeshPlanner
    {
        private const float ReconStandOff = 220f;
        private const float ReconLateralOffset = 120f;
        private const float AttackStandOff = 75f;
        private const float SupportStandOff = 125f;
        private const float FixStandOff = 170f;
        private const float AssaultLateralOffset = 160f;
        private const float SupportLateralOffset = 120f;
        private const float DuplicatePenaltyDistance = 25f;

        public static TacticalNavPlanDecision Plan(TacticalNavPlanInput input)
        {
            if (!input.HasOwnPosition) return TacticalNavPlanDecision.None("missing-own-position");

            if (TacticalDecisionDoctrine.ShouldBreakOffRecon(
                    input.Task,
                    input.CloseEngaged,
                    input.FallbackTarget.HasValue))
                return TryDirect(input, input.FallbackTarget, "recon-breakoff");

            if (input.Task == CommandTaskType.FallBackToLine)
                return PlanFallbackLine(input);

            if (IsReconTask(input.Task))
                return PlanReconBound(input);

            if (IsAttackTask(input.Task))
                return PlanAttackApproach(input);

            if (input.PrimaryTarget.HasValue)
                return TryDirect(input, input.PrimaryTarget, "direct");

            return TryDirect(input, input.FallbackTarget, "fallback-direct");
        }

        private static TacticalNavPlanDecision PlanReconBound(TacticalNavPlanInput input)
        {
            if (!input.PrimaryTarget.HasValue)
                return TryDirect(input, input.FallbackTarget, "recon-fallback");

            Candidate baseCandidate = BuildStandOffCandidate(input, input.PrimaryTarget, ReconStandOff, "recon-bound");
            if (!input.HasThreat)
                return CandidateDecision(input, baseCandidate, "recon-bound");

            Candidate best = PickBest(
                input,
                baseCandidate,
                OffsetCandidate(baseCandidate, input, ReconLateralOffset, left: true, "recon-bound-offset"),
                OffsetCandidate(baseCandidate, input, ReconLateralOffset, left: false, "recon-bound-offset"));

            return CandidateDecision(input, best, best.Reason);
        }

        private static TacticalNavPlanDecision PlanAttackApproach(TacticalNavPlanInput input)
        {
            if (!input.PrimaryTarget.HasValue)
                return TryDirect(input, input.FallbackTarget, "attack-fallback");

            float standOff = AttackStandOff;
            float lateralOffset = AssaultLateralOffset;
            string directReason = "attack-approach";
            string offsetReason = "attack-approach-offset";
            if (input.Task == CommandTaskType.SupportAttack)
            {
                standOff = SupportStandOff;
                lateralOffset = SupportLateralOffset;
                directReason = "support-approach";
                offsetReason = "support-approach-offset";
            }
            else if (input.Task == CommandTaskType.FixEnemy)
            {
                standOff = FixStandOff;
                lateralOffset = SupportLateralOffset;
                directReason = "fix-approach";
                offsetReason = "fix-approach-offset";
            }

            Candidate baseCandidate = BuildStandOffCandidate(input, input.PrimaryTarget, standOff, directReason);
            if (!input.HasThreat)
                return CandidateDecision(input, baseCandidate, directReason);

            Candidate left = OffsetCandidate(baseCandidate, input, lateralOffset, left: true, offsetReason);
            Candidate right = OffsetCandidate(baseCandidate, input, lateralOffset, left: false, offsetReason);
            Candidate[] pathCandidates = BuildPathQualityCandidates(input, offsetReason);
            Candidate[] candidates = new Candidate[3 + pathCandidates.Length];
            candidates[0] = baseCandidate;
            candidates[1] = left;
            candidates[2] = right;
            for (int i = 0; i < pathCandidates.Length; i++)
            {
                candidates[3 + i] = pathCandidates[i];
            }

            Candidate best = PickBest(input, candidates);
            return CandidateDecision(input, best, best.Reason);
        }

        private static TacticalNavPlanDecision TryDirect(
            TacticalNavPlanInput input,
            DoctrineTargetPoint point,
            string reason)
        {
            if (!point.HasValue) return TacticalNavPlanDecision.None(reason + "-missing");

            var candidate = new Candidate(point.X, point.Z, reason);
            return CandidateDecision(input, candidate, reason);
        }

        private static Candidate BuildStandOffCandidate(
            TacticalNavPlanInput input,
            DoctrineTargetPoint target,
            float standOff,
            string reason)
        {
            float dx = target.X - input.OwnX;
            float dz = target.Z - input.OwnZ;
            float distance = Length(dx, dz);
            if (distance <= input.MinWaypointDistance)
                return new Candidate(target.X, target.Z, reason);

            float offset = Clamp(standOff, 0f, Math.Max(0f, distance - input.MinWaypointDistance));
            float nx = dx / distance;
            float nz = dz / distance;
            return new Candidate(target.X - (nx * offset), target.Z - (nz * offset), reason);
        }

        private static Candidate OffsetCandidate(
            Candidate baseCandidate,
            TacticalNavPlanInput input,
            float offset,
            bool left,
            string reason)
        {
            float dx = input.PrimaryTarget.X - input.OwnX;
            float dz = input.PrimaryTarget.Z - input.OwnZ;
            float distance = Length(dx, dz);
            if (distance <= 0.001f) return baseCandidate;

            float nx = dx / distance;
            float nz = dz / distance;
            float side = left ? 1f : -1f;
            float px = -nz * side;
            float pz = nx * side;
            return new Candidate(baseCandidate.X + (px * offset), baseCandidate.Z + (pz * offset), reason);
        }

        private static Candidate PickBest(TacticalNavPlanInput input, params Candidate[] candidates)
        {
            Candidate best = default(Candidate);
            float bestScore = float.NegativeInfinity;
            bool found = false;

            for (int i = 0; i < candidates.Length; i++)
            {
                Candidate candidate = candidates[i];
                if (!IsUsable(input, candidate)) continue;

                float score = Score(input, candidate);
                if (!found || score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                    found = true;
                }
            }

            return found ? best : default(Candidate);
        }

        private static float Score(TacticalNavPlanInput input, Candidate candidate)
        {
            float distanceFromOwn = Distance(input.OwnX, input.OwnZ, candidate.X, candidate.Z);
            float score = 1000f - (distanceFromOwn * 0.05f);

            if (input.PrimaryTarget.HasValue)
            {
                float targetDistance = Distance(candidate.X, candidate.Z, input.PrimaryTarget.X, input.PrimaryTarget.Z);
                score -= targetDistance * 0.02f;
            }

            if (input.HasThreat)
            {
                float threatDistance = Distance(candidate.X, candidate.Z, input.ThreatX, input.ThreatZ);
                score += threatDistance * 1.25f;
            }

            if (input.HasCurrentWaypoint &&
                Distance(candidate.X, candidate.Z, input.CurrentWaypointX, input.CurrentWaypointZ) < DuplicatePenaltyDistance)
                score -= 250f * (1f - Math.Min(1f, Math.Max(0f, candidate.RouteContinuity01)));

            if (candidate.HasPathQuality)
            {
                score += TacticalMovementCostField.Score(input.Task, candidate.ToPathQualitySample(), input.HasThreat);
            }

            return score;
        }

        private static TacticalNavPlanDecision PlanFallbackLine(TacticalNavPlanInput input)
        {
            Candidate[] pathCandidates = BuildPathQualityCandidates(input, "fallback-line");
            if (pathCandidates.Length == 0)
                return TryDirect(input, input.FallbackTarget, "fallback-line");

            Candidate best = PickBest(input, pathCandidates);
            return CandidateDecision(input, best, best.Reason);
        }

        private static Candidate[] BuildPathQualityCandidates(TacticalNavPlanInput input, string reason)
        {
            if (input.PathSamples == null || input.PathSamples.Length == 0)
                return Array.Empty<Candidate>();

            var candidates = new Candidate[input.PathSamples.Length];
            for (int i = 0; i < input.PathSamples.Length; i++)
            {
                TacticalPathQualitySample sample = input.PathSamples[i];
                candidates[i] = new Candidate(
                    sample.X,
                    sample.Z,
                    "path-quality-" + reason,
                    sample.RoadPreference01,
                    sample.SlopeCost01,
                    sample.Congestion01,
                    sample.ChokeRisk01,
                    sample.BridgeRisk01,
                    sample.DeadGround01,
                    sample.FriendlyBlocker01,
                    sample.ThreatExposure01,
                    sample.RouteContinuity01,
                    sample.ReservationPressure01,
                    sample.FallbackLaneConflict01,
                    sample.ArtilleryDanger01);
            }

            return candidates;
        }

        private static TacticalNavPlanDecision CandidateDecision(
            TacticalNavPlanInput input,
            Candidate candidate,
            string reason)
        {
            if (!IsUsable(input, candidate)) return TacticalNavPlanDecision.None(reason + "-unsafe");
            return new TacticalNavPlanDecision(
                true,
                DoctrineTargetPoint.From(candidate.X, candidate.Z),
                string.IsNullOrWhiteSpace(candidate.Reason) ? reason : candidate.Reason);
        }

        private static bool IsUsable(TacticalNavPlanInput input, Candidate candidate)
        {
            if (!IsFinite(candidate.X) || !IsFinite(candidate.Z)) return false;
            float distance = Distance(input.OwnX, input.OwnZ, candidate.X, candidate.Z);
            return distance >= input.MinWaypointDistance && distance <= input.MaxWaypointDistance;
        }

        private static bool IsReconTask(CommandTaskType task)
        {
            return task == CommandTaskType.Scout ||
                   task == CommandTaskType.Probe ||
                   task == CommandTaskType.Screen;
        }

        private static bool IsAttackTask(CommandTaskType task)
        {
            return task == CommandTaskType.AttackObjective ||
                   task == CommandTaskType.SupportAttack ||
                   task == CommandTaskType.FixEnemy;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            return Length(ax - bx, az - bz);
        }

        private static float Length(float x, float z)
        {
            return (float)Math.Sqrt((x * x) + (z * z));
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct Candidate
        {
            public Candidate(float x, float z, string reason)
                : this(x, z, reason, 0f, 0f, 0f, 0f, 0f, 0f)
            {
            }

            public Candidate(
                float x,
                float z,
                string reason,
                float roadPreference01,
                float slopeCost01,
                float congestion01,
                float chokeRisk01,
                float bridgeRisk01,
                float deadGround01,
                float friendlyBlocker01 = 0f,
                float threatExposure01 = 0f,
                float routeContinuity01 = 0f,
                float reservationPressure01 = 0f,
                float fallbackLaneConflict01 = 0f,
                float artilleryDanger01 = 0f)
            {
                X = x;
                Z = z;
                Reason = reason;
                RoadPreference01 = roadPreference01;
                SlopeCost01 = slopeCost01;
                Congestion01 = congestion01;
                ChokeRisk01 = chokeRisk01;
                BridgeRisk01 = bridgeRisk01;
                DeadGround01 = deadGround01;
                FriendlyBlocker01 = friendlyBlocker01;
                ThreatExposure01 = threatExposure01;
                RouteContinuity01 = routeContinuity01;
                ReservationPressure01 = reservationPressure01;
                FallbackLaneConflict01 = fallbackLaneConflict01;
                ArtilleryDanger01 = artilleryDanger01;
                HasPathQuality = roadPreference01 > 0f ||
                    slopeCost01 > 0f ||
                    congestion01 > 0f ||
                    chokeRisk01 > 0f ||
                    bridgeRisk01 > 0f ||
                    deadGround01 > 0f ||
                    friendlyBlocker01 > 0f ||
                    threatExposure01 > 0f ||
                    routeContinuity01 > 0f ||
                    reservationPressure01 > 0f ||
                    fallbackLaneConflict01 > 0f ||
                    artilleryDanger01 > 0f;
            }

            public float X { get; }
            public float Z { get; }
            public string Reason { get; }
            public float RoadPreference01 { get; }
            public float SlopeCost01 { get; }
            public float Congestion01 { get; }
            public float ChokeRisk01 { get; }
            public float BridgeRisk01 { get; }
            public float DeadGround01 { get; }
            public float FriendlyBlocker01 { get; }
            public float ThreatExposure01 { get; }
            public float RouteContinuity01 { get; }
            public float ReservationPressure01 { get; }
            public float FallbackLaneConflict01 { get; }
            public float ArtilleryDanger01 { get; }
            public bool HasPathQuality { get; }

            public TacticalPathQualitySample ToPathQualitySample()
            {
                return new TacticalPathQualitySample(
                    X,
                    Z,
                    RoadPreference01,
                    SlopeCost01,
                    Congestion01,
                    ChokeRisk01,
                    BridgeRisk01,
                    DeadGround01,
                    FriendlyBlocker01,
                    ThreatExposure01,
                    RouteContinuity01,
                    ReservationPressure01,
                    FallbackLaneConflict01,
                    ArtilleryDanger01);
            }
        }
    }
}
