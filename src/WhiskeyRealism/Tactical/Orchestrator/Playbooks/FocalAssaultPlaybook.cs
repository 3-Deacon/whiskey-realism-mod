namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// BC14W: the frontal assault on the focal point of perceived enemy works, in march column after a bombardment (Grant at Spotsylvania and
    /// Upton, not Burnside at Fredericksburg). Offered only when the ledger has a focal point; the feasibility evaluation then rules it out
    /// while a turn will do (<c>turn-available</c>) or the commander is unwilling (<c>declined</c>).
    /// </summary>
    public sealed class FocalAssaultPlaybook : TacticalPlaybook
    {
        public FocalAssaultPlaybook() : base(
            BattlePlanId.FocalAssault,
            "grant-focal-assault",
            new PersonalityFit(aggression: 0.6f, caution: -0.6f, audacity: 0.5f),
            new TerrainPreference(open: 0.6f, wooded: 0.5f, river: 0.4f, mountain: 0.4f),
            new OddsRange(min: 1.2f, max: 4.0f),
            reserveCommitTriggerOdds: 1.2f)
        { }

        /// <summary>
        /// Offered only with a focal point in the last published evaluation (so never with the flag off, no held works, or before the first
        /// evaluation). The check is a precondition, ahead of the jitter draw, so no other candidate's jitter depends on whether works exist. A
        /// selection that scores with the ledger also needs the ledger to have judged the assault: an evaluation made before the works were first
        /// seen did not list it, and an unjudged play must not win unpriced.
        /// </summary>
        public override bool MatchesPreconditions(PlaybookContext ctx)
            => Plugin.EnemyWorksDoctrineEnabled() && ctx.HasFocalPoint && (ctx.Feasibility == null || ctx.Feasibility.TryGet(Id, out _));

        public override TacticalBattlePlan Instantiate(PlaybookContext ctx) =>
            new TacticalBattlePlan(
                Id,
                BattlePhase.Probe,
                ctx.FocalSector >= 0 ? ctx.FocalSector : ctx.DefaultMainEffortSector,
                fixingSectors: null,
                screeningSectors: null,
                ReserveCommitTriggerOdds,
                ageSeconds: 0f,
                ctx.JitterSeed);
    }
}
