namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime-side seed catalog of all 18 historical + generic playbooks. Mirrors
    /// the test-private SeedCatalog in tests/WhiskeyRealism.Tests/Program.cs so the
    /// runtime never imports test code.
    ///
    /// Used by TacticalBattleCoordinatorRuntime.AttachArmyIfActive to seed each
    /// per-side ArmyOrchestrator with the same catalog the harness exercises.
    /// </summary>
    internal static class BuiltInPlaybooks
    {
        public static TacticalPlaybookCatalog SeedCatalog()
        {
            var c = new TacticalPlaybookCatalog();
            c.Register(new DoubleEnvelopmentPlaybook());
            c.Register(new MobileFlankMarchPlaybook());
            c.Register(new PreparedDefensePlaybook());
            c.Register(new ManeuverAndFixPlaybook());
            c.Register(new ContinuousAttritionPlaybook());
            c.Register(new ReverseSlopeDefensePlaybook());
            c.Register(new WideFlankApproachPlaybook());
            c.Register(new FrontalShockAssaultPlaybook());
            c.Register(new ForcedFrontalAttackPlaybook());
            c.Register(new HesitatedCommitPlaybook());
            c.Register(new GenericAggressivePlaybook());
            c.Register(new GenericCautiousPlaybook());
            c.Register(new GenericMethodicalPlaybook());
            c.Register(new GenericDesperatePlaybook());
            c.Register(new CavalryScreeningDelayPlaybook());
            c.Register(new CavalryRaidPlaybook());
            c.Register(new MeetingEngagementPlaybook());
            c.Register(new DelayingDefensePlaybook());
            return c;
        }
    }
}
