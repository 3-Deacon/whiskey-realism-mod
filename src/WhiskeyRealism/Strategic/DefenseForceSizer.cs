using System;

namespace WhiskeyRealism.Strategic
{
    public static class DefenseForceSizer
    {
        public static float ComputeEffective(float activeStrength, float morale, float readinessStep)
        {
            return Math.Max(0f, activeStrength) *
                   Clamp(Math.Max(0.25f, morale), 0.25f, 1.25f) *
                   ReadinessMultiplier(readinessStep);
        }

        public static float ScoreCandidate(
            float activeStrength,
            float morale,
            float readinessStep,
            float distance,
            float desiredStrength,
            bool inOffensiveOperation,
            float caution,
            float aggression)
        {
            float desired = Math.Max(1f, desiredStrength);
            float effective = ComputeEffective(activeStrength, morale, readinessStep);
            float ratio = effective / desired;

            float score = Math.Max(0f, distance);
            if (ratio < 0.75f)
                score += (0.75f - ratio) * 600f;
            if (ratio > 1.75f)
                score += (ratio - 1.75f) * (ratio - 1.75f) * 140f;
            if (ratio > 3f && desired <= 6000f)
                score += 350f;
            if (inOffensiveOperation)
                score += 75f * (1f + Math.Max(0f, aggression));

            score -= Math.Min(60f, Math.Max(0f, caution) * Math.Min(1.25f, ratio) * 25f);
            return score;
        }

        private static float ReadinessMultiplier(float readinessStep)
        {
            if (readinessStep < 1f) return 0.25f;
            if (readinessStep < 2f) return 0.75f;
            return 1f;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
