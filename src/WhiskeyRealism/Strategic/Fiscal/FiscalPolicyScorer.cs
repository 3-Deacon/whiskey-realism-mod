namespace WhiskeyRealism.Strategic.Fiscal
{
    public static class FiscalPolicyScorer
    {
        public static float ProjectWeight(FiscalOutput intent, int alliance, int projectId, int subsidyType)
        {
            if (intent == null) return 0f;
            float score = 0f;

            if (projectId == 97 && intent.Posture >= FiscalPosture.CreditDefense)
                score += 1.25f;
            if (projectId == 103 && alliance == 1)
                score += intent.Posture <= FiscalPosture.BalancedWar ? 0.9f : 0.35f;
            if ((projectId == 0 || projectId == 1 || projectId == 2 || projectId == 3 || projectId == 4) && alliance == 1)
                score += 1.0f;
            if ((projectId == 35 || projectId == 38 || projectId == 39 || projectId == 40 || projectId == 41) && alliance == 1 && intent.Posture >= FiscalPosture.CreditDefense)
                score -= 1.0f;
            if ((projectId == 99 || projectId == 100 || projectId == 119) && (intent.SupplyProtection || intent.LogisticsExpansion))
                score += 1.1f;
            if (projectId == 120 && alliance == 1 && intent.Posture >= FiscalPosture.CreditDefense)
                score -= 0.6f;

            return score;
        }

        public static float PolicyWeight(FiscalOutput intent, int alliance, int policyId)
        {
            if (intent == null) return 0f;
            float score = 0f;

            if ((policyId == 22 || policyId == 122 || policyId == 23 || policyId == 123) && intent.Posture >= FiscalPosture.CreditDefense)
                score += 1.5f;
            if (alliance == 1 && (policyId == 103 || policyId == 104 || policyId == 105 || policyId == 106) && intent.Posture <= FiscalPosture.BalancedWar)
                score += 0.8f;
            if (alliance == 1 && policyId == 141 && intent.Posture <= FiscalPosture.BalancedWar)
                score += 0.9f;
            if (alliance == 1 && policyId == 142 && intent.SupplyProtection)
                score += 0.7f;
            if ((policyId == 36 || policyId == 136) && intent.ForceCapWarning)
                score -= 0.8f;

            return score;
        }
    }

    public struct PolicySelection
    {
        public int Index;
        public string Reason;
    }

    // Pure decision for PolicySelectionPatch over the available personality policies in
    // vanilla order (Policies.CheckAIPolicyChange, decompile 211076-211086, researches the
    // first available). Index -1 => let vanilla run. Never blocks research outright: when
    // every available policy is penalised, vanilla's first choice proceeds.
    public static class FiscalPolicySelector
    {
        public const float MinimumPolicyScore = 0.75f;
        public const float PolicyWinMargin = 0.35f;

        public static PolicySelection Select(System.Collections.Generic.IList<int> policyIds, System.Collections.Generic.IList<float> scores)
        {
            var vanilla = new PolicySelection { Index = -1, Reason = "vanilla" };
            if (policyIds == null || scores == null) return vanilla;
            int count = policyIds.Count < scores.Count ? policyIds.Count : scores.Count;
            if (count == 0) return vanilla;

            int firstNonSuppressed = -1;
            int best = -1;
            float bestScore = MinimumPolicyScore;
            for (int i = 0; i < count; i++)
            {
                if (firstNonSuppressed < 0 && scores[i] >= 0f) firstNonSuppressed = i;
                if (scores[i] > bestScore)
                {
                    bestScore = scores[i];
                    best = i;
                }
            }

            if (best > 0 && bestScore >= scores[0] + PolicyWinMargin)
                return new PolicySelection { Index = best, Reason = "score-win" };
            if (scores[0] < 0f && firstNonSuppressed > 0)
                return new PolicySelection { Index = firstNonSuppressed, Reason = "suppress-force-growth" };
            return vanilla;
        }
    }
}
