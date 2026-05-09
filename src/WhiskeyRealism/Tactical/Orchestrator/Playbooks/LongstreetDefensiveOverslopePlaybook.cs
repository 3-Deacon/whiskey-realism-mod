namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Lee's senior corps commander on defense — methodical, low-audacity,
    /// prefers prepared positions on the reverse slope of dominant terrain
    /// (wooded or mountain) at near-parity odds.
    /// </summary>
    public sealed class LongstreetDefensiveOverslopePlaybook : TacticalPlaybook
    {
        public LongstreetDefensiveOverslopePlaybook() : base(
            BattlePlanId.LongstreetDefensiveOverslope,
            "longstreet-defensive-overslope",
            new PersonalityFit(aggression: -0.2f, caution: 0.5f, audacity: -0.5f),
            new TerrainPreference(open: 0.5f, wooded: 0.7f, river: 0.6f, mountain: 0.7f),
            new OddsRange(min: 0.7f, max: 1.2f),
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
