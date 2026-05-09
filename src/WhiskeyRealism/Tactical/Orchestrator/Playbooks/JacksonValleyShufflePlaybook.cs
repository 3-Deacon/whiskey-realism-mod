namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Jackson-style valley-shuffle doctrine: small-force, high-tempo doctrine
    /// for outnumbered audacious commanders in mountain or river terrain.
    /// Speed and surprise compensate for force disadvantage.
    /// </summary>
    public sealed class JacksonValleyShufflePlaybook : TacticalPlaybook
    {
        public JacksonValleyShufflePlaybook() : base(
            BattlePlanId.JacksonValleyShuffle,
            "jackson-valley-shuffle",
            new PersonalityFit(aggression: 0.7f, caution: -0.5f, audacity: 0.9f),
            new TerrainPreference(open: 0.5f, wooded: 0.7f, river: 0.7f, mountain: 0.9f),
            new OddsRange(min: 0.5f, max: 0.9f),
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
