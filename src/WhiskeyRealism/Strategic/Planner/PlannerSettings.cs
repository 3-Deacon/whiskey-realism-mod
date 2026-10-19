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
        // Enable Covering Force (foresight F6b-2): the axis sectors' own hold reserves are posted between the two capitals as a covering
        // force. Off: no covering force, the axis sectors hold where they stand as the other sectors do.
        internal static bool CoveringForce = true;
        // Enable Main Effort Commitment (foresight F7): a formation the mover dispatched toward the main objective stays in its push through
        // readiness dips while the planner keeps the objective, the mover issues no march at readiness step 0 or 1 onto ground vanilla would
        // refuse, and an ended push's corps are stopped and taken off vanilla's offensive list. Off: D5's readiness rule for every mass member
        // and no stop, de-list or readiness skip (the last mass is still recorded).
        internal static bool MainEffortCommitment = true;
        // Enable Department Reinforcement (foresight F8): with no push, the shortest department is given one formation from a department whose
        // hold has surplus. Off: no pick and no move; the department-move record is cleared and its formations fall to the holds where they stand.
        internal static bool DepartmentReinforcement = true;
    }
}
