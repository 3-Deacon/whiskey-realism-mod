namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Generic fallback playbook for methodical commanders without a more
    /// specific historical match. Always scores above zero so the catalog
    /// never returns null.
    /// </summary>
    public sealed class GenericMethodicalPlaybook : TacticalPlaybook
    {
        public GenericMethodicalPlaybook() : base(
            BattlePlanId.GenericMethodical,
            "generic-methodical",
            new PersonalityFit(aggression: 0.0f, caution: 0.3f, audacity: 0.0f),
            new TerrainPreference(open: 0.7f, wooded: 0.7f, river: 0.6f, mountain: 0.6f),
            new OddsRange(min: 0.8f, max: 1.5f),
            reserveCommitTriggerOdds: 1.3f)
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
