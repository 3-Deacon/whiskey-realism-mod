namespace WhiskeyRealism.Strategic
{
    public static class WlCareerStartGate
    {
        public static bool ShouldDeferStrategicReview(bool dlcScenarioActive, int chosenCommanderId, bool chosenCommanderHasCommand)
        {
            return dlcScenarioActive && (chosenCommanderId < 0 || !chosenCommanderHasCommand);
        }

        // Stage G spec C5: the coordinator defers the whole strategic review only until the W&L
        // player has picked a commander. Between commands (hierarchy -1) the review runs and
        // VanillaJobMachinePaused narrows what it may do.
        public static bool CareerNotStarted(bool dlcScenarioActive, int chosenCommanderId)
        {
            return dlcScenarioActive && chosenCommanderId < 0;
        }

        // Vanilla's job-machine pause, AICampaign.UpdateUnitAI 11368:
        // DLC_WL.dlc_scenarioactive && GetPlayerCommandHierarchy() == -1 && !IsCommanderInChief().
        // While it holds vanilla freezes every faction's job machine, so Whiskey's order-issuing
        // ledgers and the planner's executors pause for every alliance; bookkeeping keeps running.
        // A CIC or army-group player without a currentcommand has hierarchy 18 and never pauses.
        public static bool VanillaJobMachinePaused(bool dlcScenarioActive, int hierarchy, bool isCommanderInChief)
        {
            return dlcScenarioActive && hierarchy == -1 && !isCommanderInChief;
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
            bool campaignGroupLookupReady = true,
            bool updateCycleReady = true)
        {
            if (!dlcScenarioActive) return false;
            if (frame < 50) return true;
            if (chosenCommanderId < 0 || !chosenCommanderRecordReady || !chosenCommanderHasCommand) return true;
            if (!campaignGroupLookupReady) return true;
            if (!diaryEventsReady || !foodReady || !cardinalPointsReady) return true;
            if (!updateCycleReady) return true;
            return updateCycle == 1 && !weatherReady;
        }

        public static bool ShouldAllowCapitalDefenseDiaryCycle(
            bool campaignGroupPresent,
            bool aiFactionStateReady,
            bool capitalDefenseListReady,
            bool campaignGroupLookupReady)
        {
            if (!campaignGroupPresent) return true;
            return aiFactionStateReady && capitalDefenseListReady && campaignGroupLookupReady;
        }
    }
}
