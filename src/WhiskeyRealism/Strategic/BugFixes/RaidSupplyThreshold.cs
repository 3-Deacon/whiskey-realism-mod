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

        internal static float ThresholdOnGroupSupplyScale(float configuredThreshold)
        {
            return configuredThreshold * GroupSupplyStateScale;
        }
    }
}
