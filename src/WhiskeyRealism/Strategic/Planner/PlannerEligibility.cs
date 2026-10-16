namespace WhiskeyRealism.Strategic.Planner
{
    // R16: which alliances get an AI planner. Vanilla only runs campaign AI for alliances that
    // own an AIFaction, created in AICampaign.UpdateUnitAI (decompile 11356-11361) when
    // `(k != GameVars.playeralliance) | GameVars.ai_vs_ai`; ai_vs_ai is true exactly when a
    // W&L scenario is active (CampaignController 29787-29794). Once the aifaction list
    // exists it is the ground truth; before that, mirror vanilla's creation predicate.
    // A W&L player who is commander-in-chief never gets an AI planner.
    internal static class PlannerEligibility
    {
        internal static bool ShouldRun(
            int allianceId,
            bool factionListReady,
            bool hasAiFaction,
            int playerAlliance,
            bool aiVsAi,
            bool playerIsWlCic)
        {
            if (allianceId < 0 || allianceId > 1) return false;
            if (playerIsWlCic && allianceId == playerAlliance) return false;
            if (factionListReady) return hasAiFaction;
            return allianceId != playerAlliance || aiVsAi;
        }
    }
}
