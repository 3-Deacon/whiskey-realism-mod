using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class DefensePackageResult
    {
        public List<DefenseCandidate> SelectedPackage = new List<DefenseCandidate>();
        public List<DefenseSuppression> Suppressed = new List<DefenseSuppression>();
        public bool Adequate;
        public bool Understrength;
        public float CumulativeEffective;
    }

    public static class DefensePackageAggregator
    {
        public const float AdequateRatio  = 0.75f;
        public const float StopRatio      = 1.00f;
        public const float OvershootRatio = 1.25f;
        public const float WorseTierStop  = 0.85f;

        public static DefensePackageResult Select(
            IEnumerable<DefenseCandidate> candidates,
            float desiredStrength,
            float caution,
            float aggression)
        {
            var result = new DefensePackageResult();
            if (candidates == null) return result;

            var scored = new List<DefenseCandidate>();
            foreach (var c in candidates)
            {
                if (c == null) continue;
                c.EffectiveStrength = EffectiveStrength(c);
                c.Score = DefenseForceSizer.ScoreCandidate(
                    activeStrength: c.ActiveStrength,
                    morale: c.Morale,
                    readinessStep: c.ReadinessStep,
                    distance: c.DistanceToThreat,
                    desiredStrength: desiredStrength,
                    inOffensiveOperation: c.InOffensiveOperation,
                    caution: caution,
                    aggression: aggression);
                scored.Add(c);
            }
            scored.Sort((a, b) => a.Score.CompareTo(b.Score));

            float desired = Math.Max(1f, desiredStrength);
            float cumulative = 0f;
            CandidateTier currentTier = CandidateTier.Local;

            foreach (var c in scored)
            {
                if (cumulative >= desired * StopRatio)
                {
                    float wouldBe = cumulative + c.EffectiveStrength;
                    if (wouldBe >= desired * OvershootRatio)
                    {
                        result.Suppressed.Add(new DefenseSuppression
                        {
                            UnitInstanceId = c.UnitInstanceId,
                            Reason = "overmatch"
                        });
                        continue;
                    }
                }

                if (cumulative >= desired * WorseTierStop &&
                    result.SelectedPackage.Count > 0 &&
                    c.Tier > currentTier)
                {
                    result.Suppressed.Add(new DefenseSuppression
                    {
                        UnitInstanceId = c.UnitInstanceId,
                        Reason = "worse-tier"
                    });
                    continue;
                }

                if (c.Tier > currentTier) currentTier = c.Tier;
                result.SelectedPackage.Add(c);
                cumulative += c.EffectiveStrength;
            }

            result.CumulativeEffective = cumulative;
            result.Adequate = cumulative >= desired * AdequateRatio;
            result.Understrength = !result.Adequate;
            return result;
        }

        private static float EffectiveStrength(DefenseCandidate c)
        {
            float morale = Clamp(c.Morale, 0.25f, 1.25f);
            float readiness = c.ReadinessStep < 1f ? 0.25f : (c.ReadinessStep < 2f ? 0.75f : 1f);
            return Math.Max(0f, c.ActiveStrength) * morale * readiness;
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
