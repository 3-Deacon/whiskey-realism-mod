namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Generic fallback playbook for desperate commanders fighting at long
    /// odds without a more specific historical match. Always scores above
    /// zero so the catalog never returns null.
    /// </summary>
    public sealed class GenericDesperatePlaybook : TacticalPlaybook
    {
        public GenericDesperatePlaybook() : base(
            BattlePlanId.GenericDesperate,
            "generic-desperate",
            new PersonalityFit(aggression: 0.4f, caution: -0.8f, audacity: 0.3f),
            new TerrainPreference(open: 0.5f, wooded: 0.5f, river: 0.5f, mountain: 0.5f),
            new OddsRange(min: 0.3f, max: 0.8f),
            reserveCommitTriggerOdds: 0.9f)
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
