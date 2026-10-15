using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

// Cross-branch integration wiring (Stage B). Pure seams the coordinator and
// patches now feed from live state.
internal static class IntegrationWiringTests
{
    public static void Run()
    {
        Pass("reserve released by the allocator while the ledger still holds it", ReserveReleasedByAllocator);
        Pass("feud gate bands the target on the front axis", FeudGateBandsTarget);
        Pass("support-main adjacency is one front band", SupportMainAdjacencyIsOneBand);
    }

    private static void Pass(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    private static CommandIntentResolution Resolved(DirectChildRole role)
        => new CommandIntentResolution(true,
            new CommandNodeIntent("node-200", "node-200", role, DirectChildAxis.Hold,
                primarySector: 2, supportPriority: 50, aggressionBias01: 0.5f, depth: 1),
            "test");

    private static readonly CommandIntentResolution NotFound = new CommandIntentResolution(false, default, "missing");

    // T1's allocator re-roles a reserve (Exploit / commit odds / crisis); the
    // operations ledger's command state may still say Reserve until its next tick.
    private static void ReserveReleasedByAllocator()
    {
        var held = Resolved(DirectChildRole.Reserve);
        TestHarness.AssertTrue(TacticalReserveCommitGate.ReleasedByAllocator(held, Resolved(DirectChildRole.SupportMain)), "re-roled to support");
        TestHarness.AssertTrue(TacticalReserveCommitGate.ReleasedByAllocator(held, Resolved(DirectChildRole.Fix)), "re-roled to crisis fix");
        TestHarness.AssertFalse(TacticalReserveCommitGate.ReleasedByAllocator(held, Resolved(DirectChildRole.Reserve)), "allocator still holds it");
        TestHarness.AssertFalse(TacticalReserveCommitGate.ReleasedByAllocator(held, Resolved(DirectChildRole.Unknown)), "no allocator opinion");
        TestHarness.AssertFalse(TacticalReserveCommitGate.ReleasedByAllocator(held, NotFound), "allocator unresolved");
        TestHarness.AssertFalse(TacticalReserveCommitGate.ReleasedByAllocator(Resolved(DirectChildRole.Main), Resolved(DirectChildRole.SupportMain)), "not held");

        bool released = TacticalReserveCommitGate.ReleasedByAllocator(held, Resolved(DirectChildRole.SupportMain));
        var decision = TacticalReserveCommitGate.Decide(new TacticalReserveCommitGate.Input(
            true, held, false, false, 1f, 1f, reserveReleased: released));
        TestHarness.AssertEqual(TacticalReserveCommitGate.Action.Allow, decision.Action, "gate lets vanilla commit");
        TestHarness.AssertEqual("reserve-released", decision.Reason, "reason");
    }

    // The #42 feud gate passed target sector -1, which the gate coerces to the
    // child's own sector, so the Main / SupportMain / Screen / Refuse sector
    // denials never fired. The target is now banded on the side's front axis.
    private static void FeudGateBandsTarget()
    {
        // Facing +Z; right is +X. Five 200 m bands across x = -500..500.
        var axis = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1000f).WithOwnExtent(-500f, 500f);
        TestHarness.AssertEqual(2, ArmyEvidenceBuilder.BandOfPoint(axis, 0f, 600f), "center band");
        int farRight = ArmyEvidenceBuilder.BandOfPoint(axis, 450f, 600f);
        TestHarness.AssertEqual(4, farRight, "right flank band");
        TestHarness.AssertEqual(-1, ArmyEvidenceBuilder.BandOfPoint(TacticalFrontAxis.None, 450f, 600f), "no axis yet");

        var screen = TacticalDirectChildGate.Decide(GateInput(DirectChildRole.Screen, farRight));
        TestHarness.AssertFalse(screen.Allow, "screen drawn out of its band");
        TestHarness.AssertEqual("screen-out-of-sector", screen.Reason, "screen reason");
        var main = TacticalDirectChildGate.Decide(GateInput(DirectChildRole.Main, farRight));
        TestHarness.AssertFalse(main.Allow, "main drawn off its axis");
        TestHarness.AssertEqual("off-axis", main.Reason, "main reason");
        TestHarness.AssertTrue(TacticalDirectChildGate.Decide(GateInput(DirectChildRole.Screen, 2)).Allow, "screen in its band");
        TestHarness.AssertTrue(TacticalDirectChildGate.Decide(GateInput(DirectChildRole.Screen, -1)).Allow, "unbanded target allows");
    }

    private static TacticalDirectChildGate.Input GateInput(DirectChildRole role, int targetSector)
        => new TacticalDirectChildGate.Input(true, true, role, axisSector: 2, primarySector: 2,
            intendedTargetBearingFromGroupRadians: 0f, intendedTargetDistanceFromGroup: 500f,
            nearestEnemyBearingFromGroupRadians: 0f, feudMaxDistance: 2000f, intendedTargetSector: targetSector);

    // Sectors are the five front-axis bands; at range 2 every band was adjacent
    // to the center. A reserve two bands from a crisis no longer abandons its
    // post for it.
    private static void SupportMainAdjacencyIsOneBand()
    {
        TestHarness.AssertEqual(1, DirectChildAllocator.SupportMainAdjacencyRange, "range");

        var plan = new TacticalBattlePlan(BattlePlanId.GenericMethodical, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7);
        var snaps = new[] { Snap("main"), Snap("pressed"), Snap("near"), Snap("far") };
        var evidence = new[]
        {
            new DirectChildEvidence(3, 0, false, 0, 0, 0.9f),
            new DirectChildEvidence(1, 4, true, 2, 0, 0.9f),
            new DirectChildEvidence(3, 0, false, 3, 0, 0.9f),
            new DirectChildEvidence(3, 0, false, 4, 0, 0.9f),
        };
        var intents = DirectChildAllocator.AllocateWithZones(plan, default(WhiskeyRealism.Strategic.PersonalityVector),
            snaps, evidence, null, null, new ReserveReleaseContext(1.0f));
        var near = Role(intents, "near");
        var far = Role(intents, "far");
        TestHarness.AssertEqual(DirectChildRole.Fix, near.Role, "reserve one band from the crisis moves up");
        TestHarness.AssertEqual(2, near.AxisSector, "toward the crisis band");
        TestHarness.AssertEqual(DirectChildRole.Reserve, far.Role, "reserve two bands away stays");
    }

    private static DirectChildSnapshot Snap(string id) => new DirectChildSnapshot(id, "army", 15, 0, id, true);

    private static DirectChildIntent Role(IReadOnlyList<DirectChildIntent> intents, string id)
    {
        for (int i = 0; i < intents.Count; i++) if (intents[i].ChildId == id) return intents[i];
        throw new Exception("missing " + id);
    }
}
