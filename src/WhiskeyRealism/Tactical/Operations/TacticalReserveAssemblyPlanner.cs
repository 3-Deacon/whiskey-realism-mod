using System;

namespace WhiskeyRealism.Tactical.Operations
{
    public readonly struct TacticalReserveAssemblyCandidate
    {
        public TacticalReserveAssemblyCandidate(
            DoctrineTargetPoint target,
            float cover01,
            float congestion01,
            bool pathReachable,
            string reason)
        {
            Target = target;
            Cover01 = Clamp01(cover01);
            Congestion01 = Clamp01(congestion01);
            PathReachable = pathReachable;
            Reason = string.IsNullOrWhiteSpace(reason) ? "reserve-candidate" : reason;
        }

        public DoctrineTargetPoint Target { get; }
        public float Cover01 { get; }
        public float Congestion01 { get; }
        public bool PathReachable { get; }
        public string Reason { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    public readonly struct TacticalReserveAssemblyInput
    {
        public TacticalReserveAssemblyInput(
            float ownX,
            float ownZ,
            float objectiveX,
            float objectiveZ,
            bool hasThreat,
            float threatX,
            float threatZ,
            TacticalReserveAssemblyCandidate[] candidates)
        {
            HasOwnPosition = IsFinite(ownX) && IsFinite(ownZ);
            OwnX = HasOwnPosition ? ownX : 0f;
            OwnZ = HasOwnPosition ? ownZ : 0f;
            HasObjective = IsFinite(objectiveX) && IsFinite(objectiveZ);
            ObjectiveX = HasObjective ? objectiveX : 0f;
            ObjectiveZ = HasObjective ? objectiveZ : 0f;
            HasThreat = hasThreat && IsFinite(threatX) && IsFinite(threatZ);
            ThreatX = HasThreat ? threatX : ObjectiveX;
            ThreatZ = HasThreat ? threatZ : ObjectiveZ;
            Candidates = candidates ?? Array.Empty<TacticalReserveAssemblyCandidate>();
        }

        public bool HasOwnPosition { get; }
        public float OwnX { get; }
        public float OwnZ { get; }
        public bool HasObjective { get; }
        public float ObjectiveX { get; }
        public float ObjectiveZ { get; }
        public bool HasThreat { get; }
        public float ThreatX { get; }
        public float ThreatZ { get; }
        public TacticalReserveAssemblyCandidate[] Candidates { get; }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalReserveAssemblyDecision
    {
        public TacticalReserveAssemblyDecision(bool hasTarget, DoctrineTargetPoint target, string reason)
        {
            HasTarget = hasTarget && target.HasValue;
            Target = HasTarget ? target : DoctrineTargetPoint.None;
            Reason = string.IsNullOrWhiteSpace(reason) ? "reserve-none" : reason;
        }

        public bool HasTarget { get; }
        public DoctrineTargetPoint Target { get; }
        public string Reason { get; }
    }

    public static class TacticalReserveAssemblyPlanner
    {
        private const float PreferredReserveDistance = 425f;
        private const float DeepReserveDistance = 575f;
        private const float LateralReserveOffset = 180f;
        private const float MinimumBehindObjective = 275f;
        private const float MaximumBehindObjective = 700f;
        private const float MaximumLateralOffset = 520f;
        private const float MinimumThreatDistance = 325f;

        public static TacticalReserveAssemblyDecision ChooseGenerated(
            float ownX,
            float ownZ,
            float facingDegrees,
            float objectiveX,
            float objectiveZ,
            int commandIndex)
        {
            if (!IsFinite(ownX) || !IsFinite(ownZ) || !IsFinite(objectiveX) || !IsFinite(objectiveZ))
                return new TacticalReserveAssemblyDecision(false, DoctrineTargetPoint.None, "reserve-missing-anchor");

            Direction direction = Direction.FromOwnToObjective(ownX, ownZ, facingDegrees, objectiveX, objectiveZ);
            int side = commandIndex % 2 == 0 ? -1 : 1;
            var candidates = new[]
            {
                CandidateBehind(objectiveX, objectiveZ, direction, PreferredReserveDistance, 0f, "battle-line-reserve-center"),
                CandidateBehind(objectiveX, objectiveZ, direction, PreferredReserveDistance, LateralReserveOffset * side, "battle-line-reserve-lateral-preferred"),
                CandidateBehind(objectiveX, objectiveZ, direction, PreferredReserveDistance, LateralReserveOffset * -side, "battle-line-reserve-lateral-alternate"),
                CandidateBehind(objectiveX, objectiveZ, direction, DeepReserveDistance, 0f, "battle-line-reserve-deep"),
            };

            return Choose(new TacticalReserveAssemblyInput(
                ownX,
                ownZ,
                objectiveX,
                objectiveZ,
                hasThreat: true,
                threatX: objectiveX,
                threatZ: objectiveZ,
                candidates: candidates));
        }

        public static TacticalReserveAssemblyDecision Choose(TacticalReserveAssemblyInput input)
        {
            if (!input.HasOwnPosition || !input.HasObjective)
                return new TacticalReserveAssemblyDecision(false, DoctrineTargetPoint.None, "reserve-missing-anchor");

            TacticalReserveAssemblyCandidate best = default(TacticalReserveAssemblyCandidate);
            float bestScore = float.MinValue;
            bool found = false;

            for (int i = 0; i < input.Candidates.Length; i++)
            {
                TacticalReserveAssemblyCandidate candidate = input.Candidates[i];
                if (!IsUsable(input, candidate)) continue;

                float score = Score(input, candidate);
                if (!found || score > bestScore)
                {
                    found = true;
                    best = candidate;
                    bestScore = score;
                }
            }

            return found
                ? new TacticalReserveAssemblyDecision(true, best.Target, best.Reason)
                : new TacticalReserveAssemblyDecision(false, DoctrineTargetPoint.None, "reserve-no-safe-candidate");
        }

        private static TacticalReserveAssemblyCandidate CandidateBehind(
            float objectiveX,
            float objectiveZ,
            Direction direction,
            float reserveDistance,
            float lateralOffset,
            string reason)
        {
            return new TacticalReserveAssemblyCandidate(
                DoctrineTargetPoint.From(
                    objectiveX - direction.X * reserveDistance + direction.LateralX * lateralOffset,
                    objectiveZ - direction.Z * reserveDistance + direction.LateralZ * lateralOffset),
                cover01: 0.5f,
                congestion01: 0f,
                pathReachable: true,
                reason);
        }

        private static bool IsUsable(TacticalReserveAssemblyInput input, TacticalReserveAssemblyCandidate candidate)
        {
            if (!candidate.PathReachable || !candidate.Target.HasValue) return false;

            Direction direction = Direction.FromOwnToObjective(
                input.OwnX,
                input.OwnZ,
                0f,
                input.ObjectiveX,
                input.ObjectiveZ);
            float behind = ((input.ObjectiveX - candidate.Target.X) * direction.X) +
                ((input.ObjectiveZ - candidate.Target.Z) * direction.Z);
            if (behind < MinimumBehindObjective || behind > MaximumBehindObjective) return false;

            float lateral = Math.Abs(((candidate.Target.X - input.ObjectiveX) * direction.LateralX) +
                ((candidate.Target.Z - input.ObjectiveZ) * direction.LateralZ));
            if (lateral > MaximumLateralOffset) return false;

            float threatX = input.HasThreat ? input.ThreatX : input.ObjectiveX;
            float threatZ = input.HasThreat ? input.ThreatZ : input.ObjectiveZ;
            if (Distance(candidate.Target.X, candidate.Target.Z, threatX, threatZ) < MinimumThreatDistance)
                return false;

            if (candidate.Congestion01 >= 0.80f) return false;
            return true;
        }

        private static float Score(TacticalReserveAssemblyInput input, TacticalReserveAssemblyCandidate candidate)
        {
            Direction direction = Direction.FromOwnToObjective(
                input.OwnX,
                input.OwnZ,
                0f,
                input.ObjectiveX,
                input.ObjectiveZ);
            float behind = ((input.ObjectiveX - candidate.Target.X) * direction.X) +
                ((input.ObjectiveZ - candidate.Target.Z) * direction.Z);
            float idealDistancePenalty = Math.Abs(behind - PreferredReserveDistance) / PreferredReserveDistance;
            float lateral = Math.Abs(((candidate.Target.X - input.ObjectiveX) * direction.LateralX) +
                ((candidate.Target.Z - input.ObjectiveZ) * direction.LateralZ));
            float lateralPenalty = lateral / MaximumLateralOffset;
            float threatX = input.HasThreat ? input.ThreatX : input.ObjectiveX;
            float threatZ = input.HasThreat ? input.ThreatZ : input.ObjectiveZ;
            float threatDistance = Distance(candidate.Target.X, candidate.Target.Z, threatX, threatZ);
            float threatBonus = Math.Min(1f, threatDistance / MaximumBehindObjective);

            return (candidate.Cover01 * 2.0f) +
                threatBonus -
                (candidate.Congestion01 * 2.5f) -
                idealDistancePenalty -
                (lateralPenalty * 0.5f);
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct Direction
        {
            private Direction(float x, float z)
            {
                X = x;
                Z = z;
                LateralX = -z;
                LateralZ = x;
            }

            public float X { get; }
            public float Z { get; }
            public float LateralX { get; }
            public float LateralZ { get; }

            public static Direction FromOwnToObjective(
                float ownX,
                float ownZ,
                float facingDegrees,
                float objectiveX,
                float objectiveZ)
            {
                float dx = objectiveX - ownX;
                float dz = objectiveZ - ownZ;
                float length = Distance(0f, 0f, dx, dz);
                if (length > 0.001f)
                    return new Direction(dx / length, dz / length);

                double radians = facingDegrees / 180d * Math.PI;
                float fx = (float)Math.Sin(radians);
                float fz = (float)Math.Cos(radians);
                length = Distance(0f, 0f, fx, fz);
                return length > 0.001f
                    ? new Direction(fx / length, fz / length)
                    : new Direction(0f, 1f);
            }
        }
    }
}
