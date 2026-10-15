namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-LOG-001. AICampaign.GetGroupSupplyState (decompile 14063-14066) returns
    // max(avg(groupsupplystate[0..3]) * 0.5, supplystate * 0.5); both inputs are 0..1,
    // so the value never exceeds 0.5. RaidForce.SearchObjective (9135) requires it to
    // reach minimumsupplyoffensivetargets (campaignprefs 1411: "the minimum supply level
    // to continue offensive operations", 0.75), so a vanilla raid force never searches
    // for a target. The fix compares the same value against the configured threshold
    // put on the function's own halved scale, i.e. max(avg, supplystate) >= 0.75.
    internal static class RaidSupplyThreshold
    {
        // Both terms of GetGroupSupplyState are multiplied by this.
        internal const float GroupSupplyStateScale = 0.5f;

        // Vanilla only raises raid forces for factions its job machine runs
        // (CheckCreationOfRaidForces 17568, job 11573); a W&L CIC player's faction is skipped
        // (11620-11628), and every unit of it is dlcw_isundercommander. A raid group under the
        // W&L player's command keeps vanilla's gate: the fix never sets it moving on its own.
        internal static bool ShouldRescale(bool fixEnabled, bool hasRaidGroup, bool wlScenarioActive, bool underPlayerCommand)
        {
            return fixEnabled && hasRaidGroup && !(wlScenarioActive && underPlayerCommand);
        }

        internal static float ThresholdOnGroupSupplyScale(float configuredThreshold)
        {
            return configuredThreshold * GroupSupplyStateScale;
        }
    }
}
