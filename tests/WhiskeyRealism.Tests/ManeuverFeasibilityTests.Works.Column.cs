using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;

// BC14W task 8: the focal assault's column through BC11 (spec § 7A requirement-focal-execution; § 13.2 FOCAL-EXEC, FORMATION-CARRY, READER-NODE-COPY-W).
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksColumnTests() => new (string, Action)[]
    {
        ("bc14w FORMATION-CARRY a doctrine order's formation is the target formation of its task, and a caller with none to read keeps the line", WFormationCarry),
        ("bc14w READER-NODE-COPY-W a column's formation reaches the node table and the doctrine order, and the maneuver hash sees it", WNodeCopyFormation),
        ("bc14w FOCAL-EXEC-COLUMN the column runs Hold, March, Turn and Attack on the focal point, in March Column, with the Turn waiting to form and for the preparation", WColumnMachine),
        ("bc14w FOCAL-EXEC-CHECKS the column skips the anchored-high-ground check, keeps the river and route checks, and a flank is unchanged", WColumnChecks),
    };

    static void WFormationCarry()
    {
        // The order's formation wins for any task; without one the shipped line.
        AssertEqual(4, CommandFormationCorrection.TargetFormationForTask(CommandTaskType.AttackObjective, 0, true, 4), "an attack in column");
        AssertEqual(4, CommandFormationCorrection.TargetFormationForTask(CommandTaskType.AdvanceToAssembly, 2, true, 4), "a march in column");
        AssertEqual(0, CommandFormationCorrection.TargetFormationForTask(CommandTaskType.AttackObjective, 3, false, 4), "no order formation: line");
        AssertEqual(CommandFormationCorrection.TargetFormationForTask(CommandTaskType.Screen, 2), CommandFormationCorrection.TargetFormationForTask(CommandTaskType.Screen, 2, false, -1),
            "the two-argument form is the four-argument one with none");
        AssertEqual(0, CommandFormationCorrection.TargetFormationForTask(CommandTaskType.AttackObjective, 0, true, -1), "a set flag with no valid formation is none");

        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        FlankResponseTests.AssertPinned(producer, "CommandFormationCorrection.TargetFormationForTask(state.Task, rec.Formation, hasOrder && order.HasFormation, order.Formation)",
            "the posture pipeline passes the order's formation");
        FlankResponseTests.AssertPinned(producer, "CommandFormationCorrection.TargetFormationForTask(g.State.Task, rec.Formation)",
            "the facing pulse's brigade call keeps the two-argument form");
    }

    static void WNodeCopyFormation()
    {
        var nodes = new[]
        {
            new CommandNodeOperationalState("node-7", CommandEchelonKind.CorpsLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning),
            new CommandNodeOperationalState("node-70", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, CommandTaskState.Planning),
        };
        var intents = new[] { FlankResponseTests.NodeIntent("node-7", "node-7"), FlankResponseTests.NodeIntent("node-70", "node-7") };
        var march = new ManeuverOrder { Phase = ManeuverPhase.March, Task = CommandTaskType.AdvanceToAssembly, TargetX = 1000f, TargetZ = 500f, Heading = -1f,
            Reason = "march-anchor", HasFormation = true, Formation = 4 };
        var table = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", march, 1f, 0f) }, intents);
        table.TryGet("node-7", out ManeuverNodeEntry corps);
        table.TryGet("node-70", out ManeuverNodeEntry division);
        AssertTrue(corps.HasFormation && corps.Formation == 4, "the child's node carries the formation");
        AssertTrue(division.HasFormation && division.Formation == 4, "and the nodes below it");
        CommandDoctrineOrder[] orders = FlankResponseTests.DoctrineOrders(nodes, BattleStance.Meeting, table);
        AssertTrue(orders[0].HasFormation && orders[0].Formation == 4, "the doctrine order of the child's node");
        AssertTrue(orders[1].HasFormation && orders[1].Formation == 4, "and of the node below it");
        var plain = new ManeuverOrder { Phase = ManeuverPhase.March, Task = CommandTaskType.AdvanceToAssembly, TargetX = 1000f, TargetZ = 500f, Heading = -1f, Reason = "march-anchor" };
        var plainTable = EnvelopmentManeuver.ExpandToNodes(new[] { new ChildManeuver("child-7", plain, 1f, 0f) }, intents);
        AssertFalse(FlankResponseTests.DoctrineOrders(nodes, BattleStance.Meeting, plainTable)[0].HasFormation, "an order with none gives none");

        int withFormation = EnvelopmentManeuver.Hash(new[] { new ChildManeuver("child-7", march, 1f, 0f) });
        int without = EnvelopmentManeuver.Hash(new[] { new ChildManeuver("child-7", plain, 1f, 0f) });
        var other = march;
        other.Formation = 3;
        int differs = EnvelopmentManeuver.Hash(new[] { new ChildManeuver("child-7", other, 1f, 0f) });
        AssertTrue(withFormation != without, "setting the formation changes the hash (the orders are re-stored)");
        AssertTrue(withFormation != differs, "and so does changing it");
    }

    // A column 600 m short of the focal point, released at 10.2 (the ledger's synchronized release), the rect's form-up point (-300, 450) and the
    // focal point (-300, 600). No Center child; the rect offset 10 battle-s.
    static ManeuverInput Column()
    {
        ManeuverInput input = FlankResponseTests.Maneuver();
        input.Kind = PlayRectKind.AssaultColumn;
        input.Envelops = true;
        input.AnchorX = -300f;
        input.AnchorZ = 450f;
        input.StartOffsetSeconds = 10f;
        input.TemplateHasCenter = false;
        input.HasEnemyExtent = false;
        input.EnemyAnchorX = 0f;
        input.EnemyAnchorZ = 1500f;
        input.HasFocal = true;
        input.FocalX = -300f;
        input.FocalZ = 600f;
        input.HasPlannedRelease = true;
        input.PlannedReleaseHours = 10.2;
        input.GroupFormation = 0;
        return input;
    }

    static void WColumnMachine()
    {
        ManeuverInput input = Column();
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.0));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "held until the synchronized release");
        AssertFalse(order.HasFormation, "a Hold asks for no formation");

        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.2));
        AssertEqual(ManeuverPhase.March, order.Phase, "marches at its release");
        AssertTrue(order.HasFormation && order.Formation == 4, "in March Column from the March on");
        AssertEqual(450f, order.TargetZ, "to its form-up point");
        AssertTrue(state.HasFocal && state.FocalX == -300f && state.FocalZ == 600f, "the focal point is latched at the March");

        // A later evaluation moves the focal point: the latched one stands.
        input.FocalX = 900f;
        input.FocalZ = 900f;
        input.ChildX = -300f;
        input.ChildZ = 440f;
        input.ChildArrived = true;
        order = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.6));
        AssertEqual(ManeuverPhase.Turn, order.Phase, "turns on arrival");
        AssertTrue(order.HasFormation && order.Formation == 4, "the Turn asks for the column");
        AssertNear(0f, order.Heading, 1e-3f, "facing the latched focal point (straight ahead, +z)");

        // The Turn waits for the column: not at the shipped 0.05 h, not before the group reads formation 4 or 0.1 h have passed, not in contact.
        AssertEqual(ManeuverPhase.Turn, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.6 + EnvelopmentManeuver.TurnHoldHours + 0.01)).Phase,
            "the shipped turn hold is not enough");
        AssertEqual(ManeuverPhase.Turn, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.6 + RoadRoutePlanner.ColumnWaitHours - 0.01)).Phase,
            "still forming just before the column wait");
        ManeuverOrder go = EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(input, 10.6 + RoadRoutePlanner.ColumnWaitHours));
        AssertEqual(ManeuverPhase.Attack, go.Phase, "attacks after the column wait");
        AssertTrue(go.HasFormation && go.Formation == 4, "in column");
        AssertEqual(CommandTaskType.AttackObjective, go.Task, "an attack");
        AssertTrue(go.TargetX == -300f && go.TargetZ == 600f, "on the focal point latched at the March");

        // A group that reads formation 4 attacks at once; contact attacks too.
        var formed = default(ManeuverState);
        ManeuverInput formedInput = Column();
        EnvelopmentManeuver.Step(ref formed, FlankResponseTests.After(formedInput, 10.0));
        EnvelopmentManeuver.Step(ref formed, FlankResponseTests.After(formedInput, 10.2));
        formedInput.ChildArrived = true;
        EnvelopmentManeuver.Step(ref formed, FlankResponseTests.After(formedInput, 10.3));
        formedInput.GroupFormation = 4;
        AssertEqual(ManeuverPhase.Attack, EnvelopmentManeuver.Step(ref formed, FlankResponseTests.After(formedInput, 10.31)).Phase, "formation 4 read: attack");
        var contact = default(ManeuverState);
        ManeuverInput contactInput = Column();
        EnvelopmentManeuver.Step(ref contact, FlankResponseTests.After(contactInput, 10.0));
        EnvelopmentManeuver.Step(ref contact, FlankResponseTests.After(contactInput, 10.2));
        contactInput.ChildArrived = true;
        EnvelopmentManeuver.Step(ref contact, FlankResponseTests.After(contactInput, 10.3));
        contactInput.ChildInContact = true;
        AssertEqual(ManeuverPhase.Attack, EnvelopmentManeuver.Step(ref contact, FlankResponseTests.After(contactInput, 10.31)).Phase, "first contact: attack");

        // The preparation floor: the column neither leaves its Hold nor leaves its Turn before it, whatever else is satisfied.
        var prepared = default(ManeuverState);
        ManeuverInput prep = Column();
        prep.HasPreparationFloor = true;
        prep.PreparationFloorHours = 10.6;
        EnvelopmentManeuver.Step(ref prepared, FlankResponseTests.After(prep, 10.0));
        order = EnvelopmentManeuver.Step(ref prepared, FlankResponseTests.After(prep, 10.3));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "the release has passed but the bombardment has not run its quarter hour");
        AssertEqual("await-preparation", order.Reason, "and says so");
        prep.PreparationFloorHours = 11.6;
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref prepared, FlankResponseTests.After(prep, 11.5)).Phase, "the floor beats the Hold ceiling too");
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref prepared, FlankResponseTests.After(prep, 11.6)).Phase, "marches at the floor");
        prep.ChildArrived = true;
        EnvelopmentManeuver.Step(ref prepared, FlankResponseTests.After(prep, 11.7));
        prep.GroupFormation = 4;
        prep.PreparationFloorHours = 12.5;
        AssertEqual(ManeuverPhase.Turn, EnvelopmentManeuver.Step(ref prepared, FlankResponseTests.After(prep, 12.4)).Phase, "a formed column still waits for the floor in its Turn");
        AssertEqual(ManeuverPhase.Attack, EnvelopmentManeuver.Step(ref prepared, FlankResponseTests.After(prep, 12.5)).Phase, "and attacks at it");

        // No floor (no battery passed): no wait.
        var none = default(ManeuverState);
        ManeuverInput noFloor = Column();
        EnvelopmentManeuver.Step(ref none, FlankResponseTests.After(noFloor, 10.0));
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref none, FlankResponseTests.After(noFloor, 10.2)).Phase, "no floor, no wait");

        // The abandon rules are the shared ones, and a Defender never runs it.
        ManeuverInput lost = Column();
        var lostState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref lostState, FlankResponseTests.After(lost, 10.0));
        EnvelopmentManeuver.Step(ref lostState, FlankResponseTests.After(lost, 10.2));
        lost.ChildStrengthBucket = 0;
        AssertEqual("lost-strength", EnvelopmentManeuver.Step(ref lostState, FlankResponseTests.After(lost, 10.3)).Reason, "a column that lost its strength is abandoned");
    }

    static void WColumnChecks()
    {
        // A perceived enemy on the inward corridor above the anchor abandons a flank, never the column (the works are the target).
        ManeuverInput flank = FlankResponseTests.Maneuver();
        flank.CorridorEnemyCount = 1;
        flank.MaxCorridorEnemyHeight = flank.AnchorHeight + 50f;
        flank.CenterContactHours = 10.0;
        var flankState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref flankState, FlankResponseTests.After(flank, 10.0));
        ManeuverOrder abandoned = EnvelopmentManeuver.Step(ref flankState, FlankResponseTests.After(flank, 10.3));
        AssertEqual("anchored-high-ground", abandoned.Reason, "a flank is abandoned on high ground");

        ManeuverInput column = Column();
        column.CorridorEnemyCount = 1;
        column.MaxCorridorEnemyHeight = column.AnchorHeight + 50f;
        var columnState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref columnState, FlankResponseTests.After(column, 10.0));
        AssertEqual(ManeuverPhase.March, EnvelopmentManeuver.Step(ref columnState, FlankResponseTests.After(column, 10.2)).Phase, "the column is not");

        // The river on the inward leg (anchor to the focal point) and a missing route abandon it as they do a flank.
        ManeuverInput river = Column();
        river.InwardLegHitsRiver = true;
        var riverState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref riverState, FlankResponseTests.After(river, 10.0));
        AssertEqual("river-on-leg", EnvelopmentManeuver.Step(ref riverState, FlankResponseTests.After(river, 10.2)).Reason, "a river on the leg to the focal point");
        ManeuverInput route = Column();
        route.MarchRouteMissing = true;
        var routeState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref routeState, FlankResponseTests.After(route, 10.0));
        AssertEqual("no-route", EnvelopmentManeuver.Step(ref routeState, FlankResponseTests.After(route, 10.2)).Reason, "no route to the form-up point");

        // A flank's orders are unchanged: no formation anywhere, its Turn ends at the shipped hold.
        ManeuverInput plain = FlankResponseTests.Maneuver();
        plain.CenterContactHours = 10.0;
        var plainState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref plainState, FlankResponseTests.After(plain, 10.0));
        ManeuverOrder march = EnvelopmentManeuver.Step(ref plainState, FlankResponseTests.After(plain, 10.3));
        AssertEqual(ManeuverPhase.March, march.Phase, "a flank marches");
        AssertFalse(march.HasFormation, "and asks for no formation");
        plain.ChildArrived = true;
        ManeuverOrder turn = EnvelopmentManeuver.Step(ref plainState, FlankResponseTests.After(plain, 10.5));
        AssertFalse(turn.HasFormation, "nor on its Turn");
        ManeuverOrder attack = EnvelopmentManeuver.Step(ref plainState, FlankResponseTests.After(plain, 10.5 + EnvelopmentManeuver.TurnHoldHours));
        AssertEqual(ManeuverPhase.Attack, attack.Phase, "its Turn ends at the shipped hold");
        AssertFalse(attack.HasFormation, "and its Attack asks for none");
        AssertEqual("attack-line-end", attack.Reason, "at the line end");
    }
}
