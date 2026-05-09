namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Sherman-style maneuver-fix doctrine: open-terrain maneuver doctrine
    /// for aggressive commanders at favorable odds. Pin one part of the
    /// enemy line while a wider arm marches around it.
    /// </summary>
    public sealed class ShermanManeuverFixPlaybook : TacticalPlaybook
    {
        public ShermanManeuverFixPlaybook() : base(
            BattlePlanId.ShermanManeuverFix,
            "sherman-maneuver-fix",
            new PersonalityFit(aggression: 0.7f, caution: -0.3f, audacity: 0.6f),
            new TerrainPreference(open: 0.9f, wooded: 0.5f, river: 0.5f, mountain: 0.4f),
            new OddsRange(min: 0.9f, max: 1.6f),
            reserveCommitTriggerOdds: 1.2f)
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
