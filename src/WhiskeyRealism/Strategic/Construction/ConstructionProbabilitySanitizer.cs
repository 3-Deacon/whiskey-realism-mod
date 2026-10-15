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
        // Group ids mirror vanilla's two candidate loops: RolledGroup = non-subsidy
        // candidates (15044-15055, build roll Random < prob*timediff*50 at 15056);
        // subsidytype >= 0 = one per-subsidy-type argmax (15016-15028); NotCandidate =
        // ignored by vanilla this call (value irrelevant, left raw).
        public const int RolledGroup = -1;
        public const int NotCandidate = -2;

        // raw[k] * multiplier[k], then rescaled per group with one common factor (argmax
        // within a group is unchanged by uniform scaling):
        //  - rolled group: vanilla compares and rolls prob * num3 (15049, 15056), so the
        //    group is capped so max(biased*w) <= max(raw*w) with w = RolledWeights ->
        //    ordering changes, the build roll never gets likelier than vanilla's own
        //    best (dampening allowed);
        //  - each subsidy type: normalised so max(biased) == max(raw). Vanilla does not
        //    reset num after the subsidy loop (15018 vs 15042), so the last type's best
        //    prob is the rolled loop's entry threshold; keeping each type's max raw keeps
        //    that threshold exactly vanilla's.
        // A multiplier of 0 yields 0 and stays 0 under any scaling.
        public static float[] Bias(float[] raw, float[] multiplier, int[] group, float[] rolledWeight = null)
        {
            if (raw == null) return null;
            var biased = new float[raw.Length];
            var maxRaw = new System.Collections.Generic.Dictionary<int, float>();
            var maxBiased = new System.Collections.Generic.Dictionary<int, float>();
            for (int i = 0; i < raw.Length; i++)
            {
                int g = GroupOf(group, i);
                if (g == NotCandidate)
                {
                    biased[i] = raw[i];
                    continue;
                }
                float m = multiplier != null && i < multiplier.Length && IsFinite(multiplier[i]) && multiplier[i] >= 0f
                    ? multiplier[i]
                    : 1f;
                biased[i] = raw[i] * m;
                float w = g == RolledGroup ? WeightOf(rolledWeight, i) : 1f;
                float rawValue = raw[i] * w;
                float biasedValue = biased[i] * w;
                maxRaw[g] = maxRaw.TryGetValue(g, out float r) && r > rawValue ? r : rawValue;
                maxBiased[g] = maxBiased.TryGetValue(g, out float b) && b > biasedValue ? b : biasedValue;
            }

            for (int i = 0; i < biased.Length; i++)
            {
                int g = GroupOf(group, i);
                if (g == NotCandidate) continue;
                float top = maxBiased[g];
                if (top <= 0f) continue;
                float target = maxRaw[g];
                if (g == RolledGroup && top <= target) continue;
                biased[i] *= target / top;
            }
            return biased;
        }

        // Vanilla's num3 (15043-15048): starts at 1 and is divided, in index order, by
        // max(1, 1 + recent foundings of that type) for every rolled-loop candidate. The
        // value at index k is the weight vanilla applies to prob[k].
        public static float[] RolledWeights(bool[] inRolledLoop, int[] recentFoundings)
        {
            int n = inRolledLoop != null ? inRolledLoop.Length : 0;
            var weights = new float[n];
            float num3 = 1f;
            for (int i = 0; i < n; i++)
            {
                if (inRolledLoop[i])
                {
                    int count = recentFoundings != null && i < recentFoundings.Length ? recentFoundings[i] : 0;
                    num3 /= System.Math.Max(1f, 1f + count);
                }
                weights[i] = num3;
            }
            return weights;
        }

        // Whether the biased value must be written into bestiipplacesprob for the
        // vanilla call. A suppression to 0 must be written, or the raw value would
        // still compete.
        public static bool ShouldWrite(float raw, float biased)
        {
            return raw > 0f && IsFinite(biased) && biased >= 0f && System.Math.Abs(raw - biased) >= 0.0001f;
        }

        private static float WeightOf(float[] weights, int index)
        {
            return weights != null && index < weights.Length && IsFinite(weights[index]) && weights[index] > 0f
                ? weights[index]
                : 1f;
        }

        private static int GroupOf(int[] group, int index)
        {
            return group != null && index < group.Length ? group[index] : NotCandidate;
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
