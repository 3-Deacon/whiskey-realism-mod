using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Unit churn (smoke 2026-10-17, the Union 3rd Division): a brigade halted under fire at assault stance, regiments facing every way, a battery whose
// combat behavior flipped 7 <-> 9. The artillery loop is pinned in StanceArtilleryMigrationTests; this class holds the other causes.
internal static class UnitChurnTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("unit-churn LEAF-POSTURE a leaf brigade records the posture it was given, so the facing pulse and fire control see it", LeafBrigadeRecordsItsPosture),
            ("unit-churn LEAF-POSTURE-WIRING ProduceLeaf records before it decides to move, and marks the submitted move", LeafPostureWiring),
        });
    }

    // ------------------------------------------------------------------ leaf brigades had no local combat response

    // The 2026-10-17 session: Union (side 0) rows of TacticalFacingPulse were 186 Division scope and none at Brigade or Regiment scope; Confederate (side 1)
    // had 317 Brigade and 105 Regiment rows. All eleven Union brigades were leaf brigades under a pool division. PostureIntentProducer.ProduceLeaf never set
    // the brigade's GroupPosture row, and FacingPulseIntentProducer and FireControlIntentProducer read only groups with Has set.
    private static void LeafBrigadeRecordsItsPosture()
    {
        GroupPosture reserve = LeafPosture.Record(DirectChildRole.Reserve, CommandTaskType.ReserveWait, 708956, closeEngaged: false, flankRisk: true);
        AssertTrue(reserve.Has, "the row exists");
        AssertEqual(CommandEchelonKind.BrigadeLike, reserve.State.Echelon, "a brigade: the pulse's Brigade scope");
        AssertEqual(CommandNodeRole.Reserve, reserve.State.Role, "the leaf role as the ledger names it");
        AssertEqual(CommandTaskType.ReserveWait, reserve.State.Task, "the leaf's task");
        AssertTrue(reserve.FlankRisk && !reserve.CloseEngaged, "the contact flags the pulse reads");
        AssertFalse(reserve.Submitted, "no move yet");
        AssertFalse(reserve.FacingSubmitted, "nor a pulse");
        AssertEqual(CommandNodeRole.Defender, LeafPosture.Record(DirectChildRole.RefuseRight, CommandTaskType.GuardFlank, 1, false, false).State.Role, "a refused flank defends");
        AssertEqual(CommandNodeRole.Unknown, LeafPosture.Record(DirectChildRole.Unknown, CommandTaskType.GuardFlank, 1, false, false).State.Role, "an unknown role stays unknown");

        // What the record buys: the pulse's brigade decision for a reserve brigade with a flank under pressure (a held task faces the threat).
        TacticalFacingPulseDecision d = TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
            TacticalFacingPulseScope.Brigade, reserve.State.Task, currentFormation: 1, targetFormation: 0, currentFacingDegrees: 10f, targetFacingDegrees: 120f,
            hasThreatBearing: true, TacticalFacingThreatSource.Visible, reserve.CloseEngaged, reserve.FlankRisk, playerProtected: false, routed: false,
            pendingOrder: false, activeMove: false, recentPulse: false, toleranceDegrees: 20f));
        AssertEqual(TacticalFacingPulseAction.CorrectFormationAndFacing, d.Action, "the brigade is turned and re-formed");
    }

    private static void LeafPostureWiring()
    {
        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string leaf = FlankResponseTests.Between(producer, "private static void ProduceLeaf(", "// BR-M1: holds a Main-effort brigade");
        FlankResponseTests.AssertPinned(leaf, "t.Groups[i] = LeafPosture.Record(", "the leaf path fills the brigade's posture row");
        int record = leaf.IndexOf("t.Groups[i] = LeafPosture.Record(", StringComparison.Ordinal);
        int decide = leaf.IndexOf("LeafBrigadeMovePolicy.Decide(", StringComparison.Ordinal);
        int exit = leaf.IndexOf("if (!move.Move)", StringComparison.Ordinal);
        AssertTrue(record >= 0 && decide > record && exit > decide, "before the move decision, so a brigade that does not move (a reserve, a hold) is recorded too");
        FlankResponseTests.AssertPinned(leaf, "t.Groups[i].Submitted = true;", "and marked when the move was submitted, as the pool path marks it");
    }
}
