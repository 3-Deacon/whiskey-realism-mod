namespace WhiskeyRealism.Strategic.Planner
{
    // Config values the planner reads. StrategicCoordinator refreshes these from Plugin config
    // before every review so the planner files stay free of BepInEx types and compile in the harness.
    internal static class PlannerSettings
    {
        // Plan Trace Logging: the per-candidate trace of TheaterPlanner.Plan, emitted as a
        // CampaignPlan:trace row when the plan row changes.
        internal static bool PlanTrace;
    }
}
