using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Orchestrator;

internal static class TacticalDecisionSnapshotTests
{
    public static void Run()
    {
        EmptyHasNoDecisions();
        LogPass("tactical decision snapshot empty has no decisions");
        ConstructAndLookupGroupStanceByAlliance();
        LogPass("tactical decision snapshot construct and lookup group stance by alliance");
        AllianceOutOfRangeReturnsFalse();
        LogPass("tactical decision snapshot alliance out of range returns false");
        PublishSwapsCurrent();
        LogPass("tactical decision snapshot publish swaps current");
        ResetForBattleEndRestoresEmpty();
        LogPass("tactical decision snapshot reset for battle end restores empty");
    }

    private static void LogPass(string message)
    {
        Console.WriteLine("PASS " + message);
    }

    private static TacticalGroupStanceDecision MakeDecision()
    {
        // TacticalDoctrineDecisionKind only has Skip=0, Apply=1 (see
        // TacticalDoctrineDecisionContracts.cs). Use Apply for a non-default
        // decision body; tests don't depend on the specific values.
        return new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Apply, 1, "test");
    }

    private static TacticalDecisionSnapshot Construct(
        int battleSeq,
        long cycleId,
        Dictionary<int, TacticalGroupStanceDecision> g0,
        Dictionary<int, TacticalGroupStanceDecision> g1)
    {
        return new TacticalDecisionSnapshot(
            battleSequence: battleSeq,
            cycleId: cycleId,
            capturedAtBattleHours: 0f,
            capturedAtRealtimeSeconds: 0f,
            side0: TacticalSideDecision.Empty,
            side1: TacticalSideDecision.Empty,
            groupStance0: g0,
            groupStance1: g1);
    }

    private static void EmptyHasNoDecisions()
    {
        var snap = TacticalDecisionSnapshot.Empty;
        TestHarness.AssertFalse(snap.TryGetGroupStance(0, 12345, out _), "noGroupStance0");
        TestHarness.AssertFalse(snap.TryGetGroupStance(1, 12345, out _), "noGroupStance1");
        TestHarness.AssertEqual(-1, snap.GetSide(0).AllianceId, "emptySideAlliance");
    }

    private static void ConstructAndLookupGroupStanceByAlliance()
    {
        var g0 = new Dictionary<int, TacticalGroupStanceDecision> { { 100, MakeDecision() } };
        var g1 = new Dictionary<int, TacticalGroupStanceDecision>();
        var snap = Construct(1, 42L, g0, g1);

        TestHarness.AssertTrue(snap.TryGetGroupStance(0, 100, out var got), "lookupSide0");
        TestHarness.AssertEqual(1, got.GroupStance, "lookupValueRoundTrip");
        TestHarness.AssertFalse(snap.TryGetGroupStance(1, 100, out _), "isolatedFromSide1");
        TestHarness.AssertEqual(1, snap.BattleSequence, "battleSequence");
        TestHarness.AssertEqual(42L, snap.CycleId, "cycleId");
    }

    private static void AllianceOutOfRangeReturnsFalse()
    {
        // Pass null dicts so the snapshot allocates empty ones internally;
        // covers both the null-coalescing branch in the constructor and the
        // alliance-range check in TryGetGroupStance.
        var snap = Construct(1, 1L, null, null);
        TestHarness.AssertFalse(snap.TryGetGroupStance(2, 0, out _), "alliance2-rejected");
        TestHarness.AssertFalse(snap.TryGetGroupStance(-1, 0, out _), "allianceNeg1-rejected");
        TestHarness.AssertEqual(-1, snap.GetSide(2).AllianceId, "getSide2-empty");
    }

    private static void PublishSwapsCurrent()
    {
        var initial = TacticalDecisionSnapshot.Current;
        var next = Construct(99, 99L, null, null);
        TacticalDecisionSnapshot.Publish(next);
        TestHarness.AssertEqual(99, TacticalDecisionSnapshot.Current.BattleSequence, "currentSwapped");
        // Restore so other tests/test ordering can't see a polluted Current.
        TacticalDecisionSnapshot.Publish(initial);
    }

    private static void ResetForBattleEndRestoresEmpty()
    {
        var next = Construct(99, 99L, null, null);
        TacticalDecisionSnapshot.Publish(next);
        TacticalDecisionSnapshot.ResetForBattleEnd();
        TestHarness.AssertTrue(
            object.ReferenceEquals(TacticalDecisionSnapshot.Current, TacticalDecisionSnapshot.Empty),
            "currentIsEmpty");
    }
}
