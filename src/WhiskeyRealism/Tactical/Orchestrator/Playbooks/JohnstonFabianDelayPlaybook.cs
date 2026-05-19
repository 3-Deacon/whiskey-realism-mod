namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Joseph Johnston's Atlanta-campaign Fabian doctrine: disciplined
    /// fight-yield-fight-yield delay, preserving the force at the cost
    /// of ground. Distinct from Fallback (panic retreat), GenericDesperate
    /// (last stand), and BufordCavalryScreenDelay (cavalry-led). Johnston
    /// applied this with infantry across the entire Atlanta campaign,
    /// trading ground from Dalton to Atlanta over four months.
    ///
    /// Uses existing CommandTaskType.Delay (already in the vocabulary)
    /// and TacticalWithdrawalDoctrine. Personality fit: cautious +
    /// methodical commander who values force preservation over decisive
    /// engagement. Won't fight on open ground (TerrainPreference low
    /// for Open), will fight on wooded/mountain (defensible).
    /// </summary>
    public sealed class JohnstonFabianDelayPlaybook : TacticalPlaybook
    {
        public JohnstonFabianDelayPlaybook() : base(
            BattlePlanId.JohnstonFabianDelay,
            "johnston-fabian-delay",
            new PersonalityFit(aggression: -0.2f, caution: 0.8f, audacity: 0.2f),
            new TerrainPreference(open: 0.3f, wooded: 0.8f, river: 0.7f, mountain: 0.8f),
            new OddsRange(min: 0.4f, max: 0.9f),
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
