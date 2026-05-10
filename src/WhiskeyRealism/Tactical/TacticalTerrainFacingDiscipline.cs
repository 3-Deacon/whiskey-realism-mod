using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalTerrainDecisionReason
    {
        Accepted,
        VanillaKept,
        NonFiniteBaseline,
        NonFiniteCandidate,
        UnknownTerrain,
        WaterCenter,
        WaterFootprint,
        OutsideDeploymentZone,
        ExcessiveCorrectionDistance,
        MissingVisibleEnemy,
        NoSafeCandidate
    }

    public readonly struct TacticalPoint2
    {
        public TacticalPoint2(float x, float z)
        {
            X = Sanitize(x);
            Z = Sanitize(z);
        }

        public float X { get; }
        public float Z { get; }
        public bool IsFinite => IsFiniteValue(X) && IsFiniteValue(Z);

        public float DistanceTo(TacticalPoint2 other)
        {
            float dx = X - other.X;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? float.NaN : value;
        }

        private static bool IsFiniteValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct TacticalTerrainSample
    {
        public TacticalTerrainSample(int terrainId, bool isWater, bool isInsideDeploymentZone, bool known = true)
        {
            TerrainId = terrainId;
            IsWater = isWater;
            IsInsideDeploymentZone = isInsideDeploymentZone;
            Known = known;
        }

        public int TerrainId { get; }
        public bool IsWater { get; }
        public bool IsInsideDeploymentZone { get; }
        public bool Known { get; }

        public static TacticalTerrainSample Unknown => new TacticalTerrainSample(-1, false, true, known: false);
    }

    public readonly struct TacticalEnemyBearingEvidence
    {
        public TacticalEnemyBearingEvidence(bool visible, float bearingDegrees, float distanceMeters, float strength)
        {
            Visible = visible;
            BearingDegrees = NormalizeAngle(bearingDegrees);
            DistanceMeters = SanitizeNonNegative(distanceMeters);
            Strength = SanitizeNonNegative(strength);
        }

        public bool Visible { get; }
        public float BearingDegrees { get; }
        public float DistanceMeters { get; }
        public float Strength { get; }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        internal static float NormalizeAngle(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            double normalized = (double)value % 360d;
            if (normalized < 0d) normalized += 360d;
            return (float)normalized;
        }
    }

    public readonly struct TacticalTerrainCandidate
    {
        public TacticalTerrainCandidate(
            TacticalPoint2 point,
            float facingDegrees,
            TacticalTerrainSample center,
            IEnumerable<TacticalTerrainSample> footprint)
        {
            Point = point;
            FacingDegrees = TacticalEnemyBearingEvidence.NormalizeAngle(facingDegrees);
            Center = center;
            Footprint = (footprint ?? Array.Empty<TacticalTerrainSample>())
                .ToArray();
        }

        public TacticalPoint2 Point { get; }
        public float FacingDegrees { get; }
        public TacticalTerrainSample Center { get; }
        public IReadOnlyList<TacticalTerrainSample> Footprint { get; }
    }

    public readonly struct TacticalTerrainRules
    {
        public TacticalTerrainRules(float maxCorrectionMeters, float preferredFacingDeltaDegrees, bool requireDeploymentZone, bool requireVisibleEnemyForFacing)
        {
            MaxCorrectionMeters = ClampPositive(maxCorrectionMeters, 60f);
            PreferredFacingDeltaDegrees = ClampPositive(preferredFacingDeltaDegrees, 90f);
            RequireDeploymentZone = requireDeploymentZone;
            RequireVisibleEnemyForFacing = requireVisibleEnemyForFacing;
        }

        public float MaxCorrectionMeters { get; }
        public float PreferredFacingDeltaDegrees { get; }
        public bool RequireDeploymentZone { get; }
        public bool RequireVisibleEnemyForFacing { get; }

        public static TacticalTerrainRules DeploymentDefault =>
            new TacticalTerrainRules(60f, 90f, requireDeploymentZone: true, requireVisibleEnemyForFacing: false);

        private static float ClampPositive(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f) return fallback;
            return value;
        }
    }

    public readonly struct TacticalTerrainDecision
    {
        public TacticalTerrainDecision(
            bool accepted,
            TacticalTerrainDecisionReason reason,
            TacticalTerrainCandidate candidate,
            float correctionDistance,
            float facingDelta)
        {
            Accepted = accepted;
            Reason = reason;
            Candidate = candidate;
            CorrectionDistance = Sanitize(correctionDistance);
            FacingDelta = Sanitize(facingDelta);
        }

        public bool Accepted { get; }
        public TacticalTerrainDecisionReason Reason { get; }
        public TacticalTerrainCandidate Candidate { get; }
        public float CorrectionDistance { get; }
        public float FacingDelta { get; }

        public string Signature =>
            "accepted=" + Accepted.ToString(CultureInfo.InvariantCulture).ToLowerInvariant() +
            "|reason=" + Reason +
            "|dist=" + Bucket(CorrectionDistance) +
            "|faceDelta=" + Bucket(FacingDelta);

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static string Bucket(float value)
        {
            return (Math.Round(Sanitize(value) / 5f) * 5f).ToString("0", CultureInfo.InvariantCulture);
        }
    }

    public static class TacticalTerrainFacingDiscipline
    {
        public static TacticalTerrainDecision Choose(
            TacticalPoint2 vanillaPoint,
            float vanillaFacingDegrees,
            IEnumerable<TacticalTerrainCandidate> candidates,
            TacticalEnemyBearingEvidence enemy,
            TacticalTerrainRules rules)
        {
            if (!vanillaPoint.IsFinite)
            {
                var kept = new TacticalTerrainCandidate(
                    vanillaPoint,
                    vanillaFacingDegrees,
                    TacticalTerrainSample.Unknown,
                    Array.Empty<TacticalTerrainSample>());
                return new TacticalTerrainDecision(false, TacticalTerrainDecisionReason.NonFiniteBaseline, kept, 0f, 0f);
            }

            var best = default(TacticalTerrainCandidate);
            float bestScore = float.MinValue;
            float bestDistance = 0f;
            float bestFacingDelta = 0f;
            bool found = false;

            foreach (var candidate in candidates ?? Array.Empty<TacticalTerrainCandidate>())
            {
                var rejection = Reject(vanillaPoint, candidate, enemy, rules, out float distance, out float facingDelta);
                if (rejection != TacticalTerrainDecisionReason.Accepted)
                    continue;

                float score = Score(distance, facingDelta, rules, enemy);
                if (!found || score > bestScore)
                {
                    found = true;
                    best = candidate;
                    bestScore = score;
                    bestDistance = distance;
                    bestFacingDelta = facingDelta;
                }
            }

            if (!found)
            {
                var kept = new TacticalTerrainCandidate(
                    vanillaPoint,
                    vanillaFacingDegrees,
                    TacticalTerrainSample.Unknown,
                    Array.Empty<TacticalTerrainSample>());
                return new TacticalTerrainDecision(false, TacticalTerrainDecisionReason.NoSafeCandidate, kept, 0f, 0f);
            }

            var accepted = enemy.Visible
                ? best
                : new TacticalTerrainCandidate(best.Point, vanillaFacingDegrees, best.Center, best.Footprint);
            return new TacticalTerrainDecision(true, TacticalTerrainDecisionReason.Accepted, accepted, bestDistance, bestFacingDelta);
        }

        public static TacticalTerrainDecisionReason Reject(
            TacticalPoint2 vanillaPoint,
            TacticalTerrainCandidate candidate,
            TacticalEnemyBearingEvidence enemy,
            TacticalTerrainRules rules,
            out float correctionDistance,
            out float facingDelta)
        {
            correctionDistance = 0f;
            facingDelta = 0f;

            if (!vanillaPoint.IsFinite)
                return TacticalTerrainDecisionReason.NonFiniteBaseline;

            if (!candidate.Point.IsFinite)
                return TacticalTerrainDecisionReason.NonFiniteCandidate;

            correctionDistance = vanillaPoint.DistanceTo(candidate.Point);
            if (correctionDistance > rules.MaxCorrectionMeters)
                return TacticalTerrainDecisionReason.ExcessiveCorrectionDistance;

            if (!candidate.Center.Known)
                return TacticalTerrainDecisionReason.UnknownTerrain;

            if (candidate.Center.Known && candidate.Center.IsWater)
                return TacticalTerrainDecisionReason.WaterCenter;

            if (candidate.Footprint.Any(s => !s.Known))
                return TacticalTerrainDecisionReason.UnknownTerrain;

            if (candidate.Footprint.Any(s => s.IsWater))
                return TacticalTerrainDecisionReason.WaterFootprint;

            if (rules.RequireDeploymentZone && candidate.Center.Known && !candidate.Center.IsInsideDeploymentZone)
                return TacticalTerrainDecisionReason.OutsideDeploymentZone;

            if (rules.RequireVisibleEnemyForFacing && !enemy.Visible)
                return TacticalTerrainDecisionReason.MissingVisibleEnemy;

            facingDelta = enemy.Visible ? AngleDelta(candidate.FacingDegrees, enemy.BearingDegrees) : 0f;
            return TacticalTerrainDecisionReason.Accepted;
        }

        public static float AngleDelta(float a, float b)
        {
            float delta = Math.Abs(TacticalEnemyBearingEvidence.NormalizeAngle(a) - TacticalEnemyBearingEvidence.NormalizeAngle(b));
            return delta > 180f ? 360f - delta : delta;
        }

        private static float Score(float distance, float facingDelta, TacticalTerrainRules rules, TacticalEnemyBearingEvidence enemy)
        {
            float score = 1000f - distance;
            if (enemy.Visible)
            {
                score += Math.Max(0f, rules.PreferredFacingDeltaDegrees - facingDelta) * 2f;
                score += Math.Min(5000f, enemy.Strength) / 100f;
            }
            return score;
        }
    }
}
