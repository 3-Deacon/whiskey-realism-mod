namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Lee-style envelopment doctrine: aggressive, audacious commanders in
    /// favorable wooded terrain with rough parity in odds. Maps the
    /// Confederate Army of Northern Virginia's preferred shape — fix one
    /// flank, swing the other into the enemy rear.
    /// </summary>
    public sealed class LeeEnvelopmentPlaybook : TacticalPlaybook
    {
        public LeeEnvelopmentPlaybook() : base(
            BattlePlanId.LeeEnvelopment,
            "lee-envelopment",
            new PersonalityFit(aggression: 0.8f, caution: -0.4f, audacity: 0.7f),
            new TerrainPreference(open: 0.7f, wooded: 0.8f, river: 0.5f, mountain: 0.4f),
            new OddsRange(min: 0.8f, max: 1.4f),
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
