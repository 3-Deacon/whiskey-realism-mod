using System;

namespace WhiskeyRealism.Strategic
{
    internal static class WlCampRealism
    {
        public static bool TryCorrectShortCampMinimumCredits(
            float actualCampHours,
            float[] stationMinimumHours,
            float[] correctedCredits,
            out float minimumTotal)
        {
            minimumTotal = 0f;
            if (stationMinimumHours == null || correctedCredits == null) return false;
            if (correctedCredits.Length < stationMinimumHours.Length) return false;

            for (int i = 0; i < stationMinimumHours.Length; i++)
                if (IsFinite(stationMinimumHours[i]) && stationMinimumHours[i] > 0f)
                    minimumTotal += stationMinimumHours[i];

            if (!IsFinite(actualCampHours)) return false;
            if (minimumTotal <= 0f || actualCampHours >= minimumTotal) return false;

            float ratio = Math.Max(0f, actualCampHours) / minimumTotal;
            for (int i = 0; i < stationMinimumHours.Length; i++)
                correctedCredits[i] = IsFinite(stationMinimumHours[i]) && stationMinimumHours[i] > 0f
                    ? stationMinimumHours[i] * ratio
                    : 0f;

            return true;
        }

        public static bool UsesResponsiveBonusWeighting(int stationId)
        {
            switch (stationId)
            {
                case 0:
                case 1:
                case 3:
                case 4:
                case 6:
                case 7:
                case 8:
                case 10:
                case 11:
                    return true;
                default:
                    return false;
            }
        }

        public static float ComputeResponsiveBonus(
            int stationId,
            bool useAverage,
            float vanillaBonus,
            float longStationAverage,
            float longCompanionAverage,
            float[] stationHistory,
            float[] companionHistory,
            float minTimeBonus,
            float maxTimeBonus,
            int recentWindowDays,
            float recentWeight)
        {
            float fallback = FiniteOr(vanillaBonus, 0f);
            if (!useAverage || !UsesResponsiveBonusWeighting(stationId)) return fallback;
            if (!IsFinite(minTimeBonus) || !IsFinite(maxTimeBonus) || maxTimeBonus <= minTimeBonus) return fallback;

            int window = ClampInt(recentWindowDays, 3, 14);
            float weight = Clamp(recentWeight, 0f, 0.5f);
            float recentStation = RecentAverage(stationHistory, window);
            float recentCompanion = RecentAverage(companionHistory, window);
            float stationHours = FiniteOr(longStationAverage, 0f) * (1f - weight) + recentStation * weight;
            float companionHours = FiniteOr(longCompanionAverage, 0f) * (1f - weight) + recentCompanion * weight;

            float bonus = ComputeBonus(stationHours, companionHours, minTimeBonus, maxTimeBonus);
            return IsFinite(bonus) ? bonus : fallback;
        }

        public static bool UsesUnitPayoffTuning(int stationId, bool divideByCommandedUnits)
        {
            if (!divideByCommandedUnits) return false;
            return stationId == 6 || stationId == 7 || stationId == 8 || stationId == 11;
        }

        public static float EffectiveCommandedUnitDivisor(int commandedUnitCount, float divisorPower)
        {
            int count = Math.Max(1, commandedUnitCount);
            float power = Clamp(divisorPower, 0.5f, 1.0f);
            return Math.Max(1f, (float)Math.Pow(count, power));
        }

        public static float ComputeUnitPayoffModifier(
            int stationId,
            bool divideByCommandedUnits,
            float vanillaModifier,
            float bonus,
            float maxBonusMalus,
            int commandedUnitCount,
            float divisorPower)
        {
            float fallback = FiniteOr(vanillaModifier, 0f);
            if (!IsFinite(vanillaModifier)) return fallback;
            if (!UsesUnitPayoffTuning(stationId, divideByCommandedUnits)) return fallback;
            if (!IsFinite(bonus) || !IsFinite(maxBonusMalus)) return fallback;

            float divisor = EffectiveCommandedUnitDivisor(commandedUnitCount, divisorPower);
            float modifier = 1f + bonus * maxBonusMalus / divisor;
            if (!IsFinite(modifier)) return fallback;
            return modifier < 0f ? 0f : modifier;
        }

        private static float ComputeBonus(float stationHours, float companionHours, float minTimeBonus, float maxTimeBonus)
        {
            float denominator = Math.Max(0.001f, maxTimeBonus - minTimeBonus);
            float bonus = (stationHours + companionHours - minTimeBonus) / denominator;
            return Clamp(bonus, -1f, 1f);
        }

        private static float RecentAverage(float[] history, int window)
        {
            if (history == null || history.Length == 0) return 0f;
            int count = Math.Min(window, history.Length);
            float sum = 0f;
            for (int i = history.Length - count; i < history.Length; i++)
                if (IsFinite(history[i]))
                    sum += history[i];
            return sum / Math.Max(1, window);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (!IsFinite(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float FiniteOr(float value, float fallback)
        {
            return IsFinite(value) ? value : fallback;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
