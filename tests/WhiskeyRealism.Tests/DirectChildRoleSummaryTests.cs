using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17: a Defender's TacticalDirectChildIntent rows read Main, SupportMain and Fix (main=1 sup=2 fix=1). The allocator's role is structural and
// behaviour reads it (the plays, the reserve gate, the exploit producer, the leaf cascade), so it is not relabelled at the source; the trace reports what the
// child is on this side of the field, the label its command node carries.
internal static class DirectChildRoleSummaryTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("role summary a Defender's line roles are reported as Defender", ADefendersLineRolesAreDefender),
            ("role summary an Attacker's roles keep their counts and a refused flank is a flank guard", AnAttackersRolesKeepTheirCounts),
            ("role summary the detail rows carry the stance label, not the allocator's", TheDetailCarriesTheLabel),
            ("role summary the allocator's structural role is not relabelled", TheAllocatorRoleIsUntouched),
            ("role summary the telemetry and the exploit producer read the stance label", TheReadersUseTheLabel),
        });
    }

    private static DirectChildIntent[] Intents(params DirectChildRole[] roles)
    {
        var intents = new DirectChildIntent[roles.Length];
        for (int i = 0; i < roles.Length; i++)
            intents[i] = new DirectChildIntent("child-" + (100 + i), 3, 3, "C" + i, 2, roles[i], DirectChildAxis.SectorAxis, 2, 0.5f,
                new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()));
        return intents;
    }

    private static readonly DirectChildRole[] Mixed =
    {
        DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.SupportMain, DirectChildRole.Fix, DirectChildRole.RefuseLeft,
        DirectChildRole.Reserve, DirectChildRole.Screen, DirectChildRole.Fallback,
    };

    private static void ADefendersLineRolesAreDefender()
    {
        var counts = DirectChildRoleSummary.Count(Intents(Mixed), BattleStance.Defender);
        AssertEqual(0, counts.Main, "a Defender has no main count");
        AssertEqual(0, counts.SupportMain, "a Defender has no support count");
        AssertEqual(0, counts.Fix, "a Defender has no fix count");
        AssertEqual(0, counts.RefuseLeft, "a Defender's refused flank is a Defender");
        AssertEqual(5, counts.Defender, "Main, two SupportMain, Fix and the refused flank hold the line");
        AssertEqual(1, counts.Reserve, "a reserve is a reserve");
        AssertEqual(1, counts.Screen, "a screen is a screen");
        AssertEqual(1, counts.Fallback, "a fallback guard is a fallback guard");
        AssertTrue(counts.Summary.Contains("def=5") && counts.Summary.Contains("main=0"), "the summary line: " + counts.Summary);
    }

    private static void AnAttackersRolesKeepTheirCounts()
    {
        foreach (BattleStance stance in new[] { BattleStance.Attacker, BattleStance.Meeting })
        {
            var counts = DirectChildRoleSummary.Count(Intents(Mixed), stance);
            AssertEqual(1, counts.Main, stance + " main");
            AssertEqual(2, counts.SupportMain, stance + " support");
            AssertEqual(1, counts.Fix, stance + " fix");
            AssertEqual(1, counts.RefuseLeft, stance + " refused flank");
            AssertEqual(0, counts.Defender, stance + " has no Defender children");
        }
        AssertEqual("FlankMarch", DirectChildRoleSummary.Label(DirectChildRole.RefuseLeft, BattleStance.Attacker), "an Attacker's refused flank is a flank guard, never a Defender");
    }

    private static void TheDetailCarriesTheLabel()
    {
        string defender = DirectChildRoleSummary.Detail(Intents(DirectChildRole.Main, DirectChildRole.Reserve), BattleStance.Defender);
        AssertEqual("child-100=Defender:2,child-101=Reserve:2", defender, "a Defender's detail");
        string attacker = DirectChildRoleSummary.Detail(Intents(DirectChildRole.Main, DirectChildRole.SupportMain), BattleStance.Attacker);
        AssertEqual("child-100=MainEffort:2,child-101=SupportingAttack:2", attacker, "an Attacker's detail");
    }

    private static void TheAllocatorRoleIsUntouched()
    {
        var intents = Intents(Mixed);
        DirectChildRoleSummary.Count(intents, BattleStance.Defender);
        DirectChildRoleSummary.Detail(intents, BattleStance.Defender);
        for (int i = 0; i < Mixed.Length; i++)
            AssertEqual(Mixed[i], intents[i].Role, "child " + i + " keeps the allocator's role: the plays, the reserve gate and the exploit producer read it");
    }

    private static void TheReadersUseTheLabel()
    {
        string army = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs");
        int at = army.IndexOf("private void EmitDirectChildIntentTelemetry()", StringComparison.Ordinal);
        AssertTrue(at >= 0, "the emitter exists");
        string body = army.Substring(at, 3200);
        FlankResponseTests.AssertPinned(body, "DirectChildRoleSummary.Count(_directChildIntents, stance)", "the counts are the stance-mapped counts");
        FlankResponseTests.AssertPinned(body, "DirectChildRoleSummary.Detail(_directChildIntents, stance)", "the per-child detail is the stance-mapped label");
        AssertTrue(body.IndexOf("case DirectChildRole.Main:", StringComparison.Ordinal) < 0, "the emitter no longer counts the raw allocator role");
        string producer = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/Producers/OpportunityIntentProducer.cs");
        FlankResponseTests.AssertPinned(producer, "CommandNodeOperationsRuntime.MapRole(leaf.LeafRole, _army.CurrentBattleStance)", "the leaf fallback role is the stance label, as the ledger path's is");
    }
}
