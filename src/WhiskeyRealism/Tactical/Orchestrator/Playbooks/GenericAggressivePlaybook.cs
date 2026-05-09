namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Generic fallback playbook for high-aggression commanders without a more
    /// specific historical match. Always scores above zero so the catalog
    /// never returns null.
    /// </summary>
    public sealed class GenericAggressivePlaybook : TacticalPlaybook
    {
        public GenericAggressivePlaybook() : base(
            BattlePlanId.GenericAggressive,
            "generic-aggressive",
            new PersonalityFit(aggression: 0.7f, caution: -0.4f, audacity: 0.5f),
            new TerrainPreference(open: 0.8f, wooded: 0.6f, river: 0.5f, mountain: 0.4f),
            new OddsRange(min: 0.7f, max: 1.6f),
            reserveCommitTriggerOdds: 1.2f)
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
