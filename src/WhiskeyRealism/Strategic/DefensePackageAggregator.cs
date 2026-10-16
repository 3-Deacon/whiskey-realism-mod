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

        // heldStrength is strength already in place at the target that counts toward it but is
        // not a package member (the W&L map-group player's group standing there): it counts
        // toward every stop and adequacy test, never toward CumulativeEffective or the cap, and
        // when it alone reaches AdequateRatio nothing is selected.
        public static DefensePackageResult Select(
            IEnumerable<DefenseCandidate> candidates,
            float desiredStrength,
            float caution,
            float aggression,
            float maxEffectiveStrength = 0f,
            string maxEffectiveReason = "package-cap",
            float heldStrength = 0f)
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
            if (heldStrength >= desired * AdequateRatio)
            {
                result.Adequate = true;
                return result;
            }

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

                if (heldStrength + cumulative >= desired * StopRatio)
                {
                    float wouldBe = heldStrength + cumulative + c.EffectiveStrength;
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

                if (heldStrength + cumulative >= desired * WorseTierStop &&
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
            result.Adequate = heldStrength + cumulative >= desired * AdequateRatio;
            result.Understrength = !result.Adequate;
            return result;
        }

    }
}
