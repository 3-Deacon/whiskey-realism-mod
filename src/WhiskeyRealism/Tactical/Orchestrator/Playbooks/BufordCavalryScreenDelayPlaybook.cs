namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Buford-style cavalry screen-and-delay doctrine: cavalry is the main
    /// effort, used to trade ground for time when outnumbered. Historical
    /// anchor: Buford at McPherson's Ridge / Seminary Ridge morning of
    /// Gettysburg Day 1 — fight from successive ridgelines, withdraw before
    /// being fixed, buy hours for I Corps infantry to arrive.
    ///
    /// Distinct from Fallback (which is panic retreat) and JohnstonFabianDelay
    /// (which is infantry-led). Personality fit: moderately aggressive +
    /// audacious commander who will fight outnumbered, paired with high
    /// caution to preserve the force.
    /// </summary>
    public sealed class BufordCavalryScreenDelayPlaybook : TacticalPlaybook
    {
        public BufordCavalryScreenDelayPlaybook() : base(
            BattlePlanId.BufordCavalryScreenDelay,
            "buford-cavalry-screen-delay",
            new PersonalityFit(aggression: 0.3f, caution: 0.6f, audacity: 0.7f),
            new TerrainPreference(open: 0.7f, wooded: 0.6f, river: 0.5f, mountain: 0.4f),
            new OddsRange(min: 0.3f, max: 0.8f),
            reserveCommitTriggerOdds: 0.7f)
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
