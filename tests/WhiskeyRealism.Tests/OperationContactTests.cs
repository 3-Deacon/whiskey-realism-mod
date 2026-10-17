using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17: both sides' operation stayed "Scouting" through heavy contact. The director picked the best-scoring
// objective, an empty map objective on strong ground, and a newly selected objective with no enemy at it is no-contact-scout,
// although the side already perceived a formed enemy line elsewhere (the VisibleEnemyLine objective). A side that sees the
// enemy does not scout for him. docs/tactical-operations-ledger.md.
internal static class OperationContactTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("operation contact elsewhere ends the scouting of an empty objective for an attacker", ContactElsewhereEndsScoutingForAnAttacker),
            ("operation contact elsewhere holds a defender in a forming defensive network", ContactElsewhereHoldsADefender),
            ("operation no contact anywhere still scouts", NoContactAnywhereStillScouts),
            ("operation weak contact elsewhere is reconnaissance contact, not a commitment", WeakContactElsewhereIsReconnaissance),
            ("operation an active operation on its own objective keeps its rules when contact appears elsewhere", AnActiveOperationKeepsItsObjective),
        });
    }

    // A neutral map objective on strong ground: it outscores everything for a side that has no enemy at it.
    private static BattlefieldObjectiveEstimate EmptyStrongGround() =>
        new BattlefieldObjectiveEstimate("Applepie_Ridge", TacticalObjectiveType.Ridge, 0f, 0.8f, false, 0.9f, 100f, 100f, 0.7f, 0.5f);

    private static BattlefieldObjectiveEstimate FormedEnemyLine(float strength, float confidence) =>
        new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, strength, confidence, true, 0.4f, 900f, 100f, 0f, 0f);

    private static TacticalOperationDirectorDecision Decide(BattleStance stance, OperationRecord current, params BattlefieldObjectiveEstimate[] objectives) =>
        TacticalOperationDirector.Decide(TacticalOperationDirectorInput.ForTest(current, 100f, 20000f, 0.3f, 0.5f, 0.5f, objectives).WithStance(stance));

    private static void ContactElsewhereEndsScoutingForAnAttacker()
    {
        foreach (BattleStance stance in new[] { BattleStance.Meeting, BattleStance.Attacker })
        {
            var decision = Decide(stance, OperationRecord.Noop, EmptyStrongGround(), FormedEnemyLine(9000f, 0.9f));
            AssertTrue(decision.Operation.Phase != TacticalOperationPhase.Scouting, stance + ": the operation leaves Scouting: " + decision.Operation.Phase + " " + decision.Reason);
            AssertEqual("enemy-line-1", decision.Operation.PrimaryObjectiveId, stance + ": it works the contact the side perceives");
            AssertTrue(decision.Reason != "no-contact-scout", stance + ": not a scouting reason");
        }
    }

    private static void ContactElsewhereHoldsADefender()
    {
        var decision = Decide(BattleStance.Defender, OperationRecord.Noop, EmptyStrongGround(), FormedEnemyLine(9000f, 0.9f));
        AssertEqual(TacticalOperationPhase.Forming, decision.Operation.Phase, "the Defender forms its line instead of scouting");
        AssertEqual(TacticalOperationShape.DefensiveNetwork, decision.Operation.Shape, "as a defensive network");
        AssertEqual("Applepie_Ridge", decision.Operation.PrimaryObjectiveId, "on the ground it holds, not the enemy's line");
        AssertEqual("contact-elsewhere-hold", decision.Reason, "and says why");
    }

    private static void NoContactAnywhereStillScouts()
    {
        foreach (BattleStance stance in new[] { BattleStance.Meeting, BattleStance.Defender })
        {
            var decision = Decide(stance, OperationRecord.Noop, EmptyStrongGround());
            AssertEqual(TacticalOperationPhase.Scouting, decision.Operation.Phase, stance + ": nothing perceived anywhere, scout");
        }
    }

    private static void WeakContactElsewhereIsReconnaissance()
    {
        // A screen of 400 men at confidence 0.5 is not a formed line: reconnaissance contact, the shipped rule.
        var weak = new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 400f, 0.5f, false, 0.4f, 900f, 100f, 0f, 0f);
        var decision = Decide(BattleStance.Meeting, OperationRecord.Noop, EmptyStrongGround(), weak);
        AssertEqual("recon-contact", decision.Reason, "a reconnaissance contact keeps the shipped Scouting");
        AssertEqual(TacticalOperationPhase.Scouting, decision.Operation.Phase, "scouting");
    }

    private static void AnActiveOperationKeepsItsObjective()
    {
        var active = new OperationRecord(TacticalOperationShape.DefensiveNetwork, TacticalOperationPhase.Forming, "Applepie_Ridge", 0f);
        var decision = Decide(BattleStance.Defender, active, EmptyStrongGround(), FormedEnemyLine(9000f, 0.9f));
        AssertEqual("Applepie_Ridge", decision.Operation.PrimaryObjectiveId, "an operation already on its objective is not retargeted");
        AssertTrue(decision.Operation.Phase != TacticalOperationPhase.Scouting, "and the next review does not fall back to Scouting: " + decision.Operation.Phase);
    }
}
