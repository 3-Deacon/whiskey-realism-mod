namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Methodical but indecisive — plans well, commits late, low audacity.
    /// Mid-odds preference; reserve trigger conservative.
    /// </summary>
    public sealed class BraggIndecisiveCommitPlaybook : TacticalPlaybook
    {
        public BraggIndecisiveCommitPlaybook() : base(
            BattlePlanId.BraggIndecisiveCommit,
            "bragg-indecisive-commit",
            new PersonalityFit(aggression: 0.0f, caution: 0.3f, audacity: -0.4f),
            new TerrainPreference(open: 0.6f, wooded: 0.6f, river: 0.6f, mountain: 0.6f),
            new OddsRange(min: 0.8f, max: 1.4f),
            reserveCommitTriggerOdds: 1.4f)
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
