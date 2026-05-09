namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime-side seed catalog of all 14 historical + generic playbooks. Mirrors
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
            c.Register(new LeeEnvelopmentPlaybook());
            c.Register(new JacksonValleyShufflePlaybook());
            c.Register(new McClellanPreparedDefensePlaybook());
            c.Register(new ShermanManeuverFixPlaybook());
            c.Register(new GrantContinuousAttritionPlaybook());
            c.Register(new LongstreetDefensiveOverslopePlaybook());
            c.Register(new HookerFlankDeparturePlaybook());
            c.Register(new HoodFrontalAssaultPlaybook());
            c.Register(new BurnsideForcedAssaultPlaybook());
            c.Register(new BraggIndecisiveCommitPlaybook());
            c.Register(new GenericAggressivePlaybook());
            c.Register(new GenericCautiousPlaybook());
            c.Register(new GenericMethodicalPlaybook());
            c.Register(new GenericDesperatePlaybook());
            return c;
        }
    }
}
