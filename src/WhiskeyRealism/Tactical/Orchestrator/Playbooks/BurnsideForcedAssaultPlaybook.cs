namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Externally pressured frontal commitment — low caution, low audacity.
    /// Issues attacks on schedule rather than opportunity.
    /// </summary>
    public sealed class BurnsideForcedAssaultPlaybook : TacticalPlaybook
    {
        public BurnsideForcedAssaultPlaybook() : base(
            BattlePlanId.BurnsideForcedAssault,
            "burnside-forced-assault",
            new PersonalityFit(aggression: 0.5f, caution: -0.5f, audacity: -0.3f),
            new TerrainPreference(open: 0.6f, wooded: 0.6f, river: 0.5f, mountain: 0.5f),
            new OddsRange(min: 0.6f, max: 1.3f),
            reserveCommitTriggerOdds: 1.1f)
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
