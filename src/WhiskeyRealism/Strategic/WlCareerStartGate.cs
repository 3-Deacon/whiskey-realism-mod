namespace WhiskeyRealism.Strategic
{
    public static class WlCareerStartGate
    {
        public static bool ShouldDeferStrategicReview(bool dlcScenarioActive, int chosenCommanderId, bool chosenCommanderHasCommand)
        {
            return dlcScenarioActive && (chosenCommanderId < 0 || !chosenCommanderHasCommand);
        }
    }
}
