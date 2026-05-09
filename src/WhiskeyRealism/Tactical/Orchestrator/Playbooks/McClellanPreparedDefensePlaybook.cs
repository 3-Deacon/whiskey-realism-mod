namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// McClellan-style prepared-defense doctrine: cautious, methodical
    /// defensive doctrine with low audacity. Strong on prepared positions;
    /// relies on reserves and terrain rather than maneuver.
    /// </summary>
    public sealed class McClellanPreparedDefensePlaybook : TacticalPlaybook
    {
        public McClellanPreparedDefensePlaybook() : base(
            BattlePlanId.McClellanPreparedDefense,
            "mcclellan-prepared-defense",
            new PersonalityFit(aggression: -0.6f, caution: 0.8f, audacity: -0.7f),
            new TerrainPreference(open: 0.6f, wooded: 0.7f, river: 0.7f, mountain: 0.7f),
            new OddsRange(min: 0.6f, max: 1.5f),
            reserveCommitTriggerOdds: 1.6f)
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
