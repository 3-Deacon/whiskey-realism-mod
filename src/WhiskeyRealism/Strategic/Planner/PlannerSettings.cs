namespace WhiskeyRealism.Strategic.Planner
{
    // Config values the planner reads. StrategicCoordinator refreshes these from Plugin config
    // before every review so the planner files stay free of BepInEx types and compile in the harness.
    internal static class PlannerSettings
    {
        // Plan Trace Logging: the per-candidate trace of TheaterPlanner.Plan, emitted as a
        // CampaignPlan:trace row when the plan row changes.
        internal static bool PlanTrace;
        // Enable Strategic Foresight Picture (foresight F3): the planner presumes the enemy's unaccounted men at the anchors that are not
        // scouted, holds against them, and races the capital reach. Off restores D2's presumption, P7's floor and the relocation-range
        // capital threat, the nearest-belief hold release and D6's department sum. All-on: true until the coordinator refreshes it.
        internal static bool ForesightPicture = true;
    }
}
