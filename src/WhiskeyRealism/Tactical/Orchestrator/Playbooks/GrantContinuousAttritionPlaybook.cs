namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Grant-style continuous-attrition doctrine: methodical pressure doctrine
    /// for clearly-favorable odds. Commits reserves continuously rather than
    /// holding for a decisive moment.
    /// </summary>
    public sealed class GrantContinuousAttritionPlaybook : TacticalPlaybook
    {
        public GrantContinuousAttritionPlaybook() : base(
            BattlePlanId.GrantContinuousAttrition,
            "grant-continuous-attrition",
            new PersonalityFit(aggression: 0.6f, caution: 0.2f, audacity: 0.3f),
            new TerrainPreference(open: 0.7f, wooded: 0.6f, river: 0.6f, mountain: 0.5f),
            new OddsRange(min: 1.3f, max: 2.5f),
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
