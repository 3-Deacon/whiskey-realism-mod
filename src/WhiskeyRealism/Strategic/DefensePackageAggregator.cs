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
            float aggression,
            float maxEffectiveStrength = 0f,
            string maxEffectiveReason = "package-cap")
        {
            var result = new DefensePackageResult();
            if (candidates == null) return result;

            var scored = new List<DefenseCandidate>();
            foreach (var c in candidates)
            {
                if (c == null) continue;
                c.EffectiveStrength = DefenseForceSizer.ComputeEffective(c.ActiveStrength, c.Morale, c.ReadinessStep);
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
            scored.Sort((a, b) =>
            {
                int s = a.Score.CompareTo(b.Score);
                return s != 0 ? s : a.UnitInstanceId.CompareTo(b.UnitInstanceId);
            });

            float desired = Math.Max(1f, desiredStrength);
            float cumulative = 0f;
            CandidateTier currentTier = CandidateTier.Local;

            foreach (var c in scored)
            {
                if (maxEffectiveStrength > 0f &&
                    cumulative + c.EffectiveStrength > maxEffectiveStrength)
                {
                    result.Suppressed.Add(new DefenseSuppression
                    {
                        UnitInstanceId = c.UnitInstanceId,
                        Reason = maxEffectiveReason
                    });
                    continue;
                }

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

    }
}
