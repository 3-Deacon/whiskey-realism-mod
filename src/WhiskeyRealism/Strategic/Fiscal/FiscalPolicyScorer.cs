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
}
