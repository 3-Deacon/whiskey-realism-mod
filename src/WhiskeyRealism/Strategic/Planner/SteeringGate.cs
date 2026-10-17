namespace WhiskeyRealism.Strategic.Planner
{
    // The one stand-down rule of the planner's alliance-level steering patches that run between reviews (the railroad roll, G5c; the
    // re-arming order and the weapon purchase, G9). Vanilla's own AI runs unchanged unless every clause holds:
    //   - the steering flags are on;
    //   - the alliance is 0 or 1 (AICampaign.aifaction also holds Europe, alliance 2);
    //   - it is not the player's alliance outside ai_vs_ai (vanilla returns there too, 16054, 16082, 16263);
    //   - it is not the W&L commander-in-chief's own alliance (the planner stands down there, 11620-11628);
    //   - vanilla's job machine is not paused (11368: the planner's picture is bookkeeping then);
    //   - the published plan is fresh (a stale plan or none never steers).
    public static class SteeringGate
    {
        public static bool ShouldSteer(bool enabled, int alliance, int playerAlliance, bool aiVsAi, bool wlCicPlayerAlliance, bool jobMachinePaused, bool freshPlan)
        {
            if (!enabled || alliance < 0 || alliance >= 2) return false;
            if (alliance == playerAlliance && !aiVsAi) return false;
            if (wlCicPlayerAlliance || jobMachinePaused) return false;
            return freshPlan;
        }
    }
}
