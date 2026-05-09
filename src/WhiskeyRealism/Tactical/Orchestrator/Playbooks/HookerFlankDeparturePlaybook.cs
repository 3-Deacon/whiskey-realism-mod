namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Aggressive opening, methodical execution, but loses nerve mid-battle.
    /// Prefers favorable initial odds and open terrain to deploy a wide flank arm.
    /// </summary>
    public sealed class HookerFlankDeparturePlaybook : TacticalPlaybook
    {
        public HookerFlankDeparturePlaybook() : base(
            BattlePlanId.HookerFlankDeparture,
            "hooker-flank-departure",
            new PersonalityFit(aggression: 0.6f, caution: -0.2f, audacity: -0.4f),
            new TerrainPreference(open: 0.7f, wooded: 0.6f, river: 0.5f, mountain: 0.4f),
            new OddsRange(min: 1.0f, max: 1.5f),
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
