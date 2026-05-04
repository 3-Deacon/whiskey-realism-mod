namespace WhiskeyRealism.Strategic.Construction
{
    public struct TelegraphCandidateFacts
    {
        public bool ConnectedToCapitalOrChain;
        public bool SupportingUnitEligible;
        public bool SupportsActiveCommandCorridor;
        public bool SafeRear;
        public bool AlreadyCoveredByTelegraph;
        public float CommandDelayPressure;
        public float FormationImportance;
    }

    public struct TelegraphIntentDecision
    {
        public bool ShouldBuild;
        public float Score;
        public string Reason;
    }

    public static class TelegraphIntentScorer
    {
        public static TelegraphIntentDecision Score(TelegraphCandidateFacts candidate, ConstructionPosture posture)
        {
            if (!candidate.ConnectedToCapitalOrChain)
                return Decision(false, 0f, "not-connected");
            if (!candidate.SupportingUnitEligible)
                return Decision(false, 0f, "no-supporting-unit");
            if (!candidate.SafeRear)
                return Decision(false, 0f, "unsafe-corridor");
            if (candidate.AlreadyCoveredByTelegraph)
                return Decision(false, 0f, "already-covered");
            if (posture == ConstructionPosture.EmergencyHold && !candidate.SupportsActiveCommandCorridor)
                return Decision(false, 0f, "emergency-noncritical");

            float score = 0.25f;
            if (candidate.SupportsActiveCommandCorridor)
                score += 0.45f;
            score += Clamp01(candidate.CommandDelayPressure) * 0.45f;
            score += Clamp01(candidate.FormationImportance) * 0.35f;
            if (posture == ConstructionPosture.FieldSupply || posture == ConstructionPosture.DefensiveWorks)
                score += 0.2f;

            return score >= 1.0f
                ? Decision(true, score, "active-command-corridor")
                : Decision(false, score, "below-threshold");
        }

        private static TelegraphIntentDecision Decision(bool shouldBuild, float score, string reason)
        {
            return new TelegraphIntentDecision
            {
                ShouldBuild = shouldBuild,
                Score = score,
                Reason = reason
            };
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
