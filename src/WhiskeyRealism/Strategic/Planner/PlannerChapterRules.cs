namespace WhiskeyRealism.Strategic.Planner
{
    // The pre-war gate (plan decision 8, research gap 3). Pure.
    //
    // Vanilla refuses every campaign move before the war starts: SetWaypoint returns at 91366 while
    // `IsCampaign && CurrentChapter == 0 && leveltoload == "001"`, and its offensive aggression is 0
    // (campaignprefs aiagressiveness[0], 1417). Scenario 002 is forced to chapter >= 1 (211671), and
    // chapter -1 is the value before vanilla's frame-50 chapter update (211208, 29857), so it counts
    // as pre-war. While the war has not started the planner's executors idle: no move, order or
    // steering, nothing withheld from vanilla's offensive and no pointer redirect, so vanilla's own
    // pre-war state is untouched. Snapshot, beliefs, plan and allocation rows continue as bookkeeping,
    // and G2's appointments still run (they move no unit, and no chapter test guards vanilla's own
    // commander replacement: 12012, 12343, 12383, 12451, 13730, 15155, 15479, 15489, 16409 include none).
    public static class PlannerChapterRules
    {
        public const string PreWarScenario = "001";

        public static bool ExecutorsAllowed(string scenario, int chapter)
        {
            return !(scenario == PreWarScenario && chapter <= 0);
        }
    }
}
