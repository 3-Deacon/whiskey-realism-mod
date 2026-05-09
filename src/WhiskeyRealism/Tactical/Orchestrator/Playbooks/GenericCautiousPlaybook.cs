namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Generic fallback playbook for high-caution commanders without a more
    /// specific historical match. Always scores above zero so the catalog
    /// never returns null.
    /// </summary>
    public sealed class GenericCautiousPlaybook : TacticalPlaybook
    {
        public GenericCautiousPlaybook() : base(
            BattlePlanId.GenericCautious,
            "generic-cautious",
            new PersonalityFit(aggression: -0.5f, caution: 0.7f, audacity: -0.4f),
            new TerrainPreference(open: 0.6f, wooded: 0.7f, river: 0.7f, mountain: 0.7f),
            new OddsRange(min: 0.6f, max: 1.4f),
            reserveCommitTriggerOdds: 1.5f)
        { }

        public override TacticalBattlePlan Instantiate(PlaybookContext ctx) =>
            new TacticalBattlePlan(
                Id,
                BattlePhase.Probe,
                ctx.DefaultMainEffortSector,
                fixingSectors: null,
                screeningSectors: null,
                ReserveCommitTriggerOdds,
                ageSeconds: 0f,
                ctx.JitterSeed);
    }
}
