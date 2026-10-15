namespace WhiskeyRealism.Strategic.Planner
{
    // The W&L player's echelon (spec C4/C5) and what it means for the planner.
    //   NotCareer           no W&L scenario.
    //   CommanderInChief    DLC_WL.IsCommanderInChief (47475): the planner stands down for the
    //                       player's own alliance (vanilla job 36 skips it too, 11620-11628).
    //   NoCommand           GetPlayerCommandHierarchy() == -1 (44397-44427; it is -1 only with no
    //                       chosen commander, or no currentcommand, no army group and not CIC):
    //                       exactly vanilla's job-machine pause (11368), which freezes every
    //                       faction, so the executors of every alliance pause.
    //   ArmyGroupCommander  hierarchy 18 from an army group without a currentcommand: the planner
    //                       runs (stage F classes his army group's units PlayerCommand).
    //   MapGroup            the command is its own campaign group (GetCampaignGroup(cmd) == cmd,
    //                       vanilla's flag2, 8271): order-only, unreliable executor.
    //   BelowMapLevel       the command sits inside a map group: its carrying group is movable.
    public static class PlayerCommandClassifier
    {
        public static PlayerCommandClass Classify(bool scenarioActive, int chosenCommander, bool isCommanderInChief,
                                                  int hierarchy, bool hasCommand, bool commandIsMapGroup)
        {
            if (!scenarioActive) return PlayerCommandClass.NotCareer;
            if (isCommanderInChief) return PlayerCommandClass.CommanderInChief;
            if (chosenCommander < 0 || hierarchy == -1) return PlayerCommandClass.NoCommand;
            if (!hasCommand) return PlayerCommandClass.ArmyGroupCommander;
            return commandIsMapGroup ? PlayerCommandClass.MapGroup : PlayerCommandClass.BelowMapLevel;
        }

        // CIC player: this alliance only; the enemy side's planner keeps running.
        public static bool PlannerStandsDown(PlayerCommandClass c, int playerAlliance, int alliance)
        {
            return c == PlayerCommandClass.CommanderInChief && alliance == playerAlliance;
        }

        // Vanilla's 11368 return freezes every faction's job machine: every alliance pauses.
        public static bool ExecutorsPaused(PlayerCommandClass c)
        {
            return c == PlayerCommandClass.NoCommand;
        }

        public static PlannerReviewMode ReviewMode(PlayerCommandClass c, int playerAlliance, int alliance)
        {
            if (PlannerStandsDown(c, playerAlliance, alliance)) return PlannerReviewMode.StandDown;
            return ExecutorsPaused(c) ? PlannerReviewMode.Bookkeeping : PlannerReviewMode.Full;
        }
    }
}
