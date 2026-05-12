namespace WhiskeyRealism.Strategic
{
    public static class WlCareerStartGate
    {
        public static bool ShouldDeferStrategicReview(bool dlcScenarioActive, int chosenCommanderId, bool chosenCommanderHasCommand)
        {
            return dlcScenarioActive && (chosenCommanderId < 0 || !chosenCommanderHasCommand);
        }

        public static bool ShouldSkipDiaryEventUpdate(
            bool dlcScenarioActive,
            int frame,
            int chosenCommanderId,
            bool chosenCommanderRecordReady,
            bool chosenCommanderHasCommand,
            bool diaryEventsReady,
            bool foodReady,
            bool cardinalPointsReady,
            bool weatherReady,
            int updateCycle,
            bool campaignGroupLookupReady = true)
        {
            if (!dlcScenarioActive) return false;
            if (frame < 50) return true;
            if (chosenCommanderId < 0 || !chosenCommanderRecordReady || !chosenCommanderHasCommand) return true;
            if (!campaignGroupLookupReady) return true;
            if (!diaryEventsReady || !foodReady || !cardinalPointsReady) return true;
            return updateCycle == 1 && !weatherReady;
        }
    }
}
