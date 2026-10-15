namespace WhiskeyRealism.Strategic.Construction
{
    public enum ConstructionProbabilityStatus
    {
        Valid,
        Skip,
        Invalid
    }

    public static class ConstructionProbabilitySanitizer
    {
        public static ConstructionProbabilityStatus Classify(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return ConstructionProbabilityStatus.Invalid;

            return value > 0f
                ? ConstructionProbabilityStatus.Valid
                : ConstructionProbabilityStatus.Skip;
        }
    }
}

namespace WhiskeyRealism.Strategic.Construction
{
    // Per-call selection bias for vanilla AICampaign.UpdateCompanyFoundations
    // (decompile 15000-15080). The biased array is written only for the duration of the
    // vanilla call and restored afterward, so UpdateCompanyFoundationList (15253-15261)
    // always compares new sites against the raw stored probability.
    public static class ConstructionProbabilityBias
    {
        // raw[k] * multiplier[k]. Candidates in vanilla's rolled (non-subsidy) loop are
        // then scaled by a common factor so the largest biased value never exceeds the
        // largest raw value: ordering changes, the Random < prob*timediff*50 build roll
        // (15056) does not get more likely than vanilla's own best candidate.
        public static float[] Bias(float[] raw, float[] multiplier, bool[] rolled)
        {
            if (raw == null) return null;
            var biased = new float[raw.Length];
            float maxRaw = 0f;
            float maxBiased = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float m = multiplier != null && i < multiplier.Length && IsFinite(multiplier[i]) && multiplier[i] >= 0f
                    ? multiplier[i]
                    : 1f;
                biased[i] = raw[i] * m;
                if (rolled != null && i < rolled.Length && rolled[i])
                {
                    if (raw[i] > maxRaw) maxRaw = raw[i];
                    if (biased[i] > maxBiased) maxBiased = biased[i];
                }
            }

            if (maxBiased > maxRaw && maxBiased > 0f)
            {
                float scale = maxRaw / maxBiased;
                for (int i = 0; i < biased.Length; i++)
                {
                    if (rolled != null && i < rolled.Length && rolled[i])
                        biased[i] *= scale;
                }
            }
            return biased;
        }

        // Restore the raw value only if vanilla left the slot as we biased it. If vanilla
        // consumed it (place nulled, prob zeroed at 15032-15033 / 15078-15079) or
        // replaced it, its write stands.
        public static bool ShouldRestore(float current, float biased, bool samePlace)
        {
            return samePlace && System.Math.Abs(current - biased) < 0.0001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
