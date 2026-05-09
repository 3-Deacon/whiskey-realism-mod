namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// High aggression, low caution, audacious — desperate frontal assaults
    /// regardless of force position. Reserve commit triggers early; willing
    /// to spend forces.
    /// </summary>
    public sealed class HoodFrontalAssaultPlaybook : TacticalPlaybook
    {
        public HoodFrontalAssaultPlaybook() : base(
            BattlePlanId.HoodFrontalAssault,
            "hood-frontal-assault",
            new PersonalityFit(aggression: 0.9f, caution: -0.7f, audacity: 0.6f),
            new TerrainPreference(open: 0.7f, wooded: 0.6f, river: 0.5f, mountain: 0.4f),
            new OddsRange(min: 0.5f, max: 1.2f),
            reserveCommitTriggerOdds: 1.0f)
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
