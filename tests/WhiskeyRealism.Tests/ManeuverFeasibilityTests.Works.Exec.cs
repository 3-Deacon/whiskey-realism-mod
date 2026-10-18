using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Util;
using static TestHarness;
using static TestFixtures;

// BC14W task 8: the focal assault's execution around the column (spec § 7A requirement-focal-execution; § 13.2 FOCAL-EXEC, FOCAL-PREP, FOCAL-PLAYER):
// the followers' release, the preparation floor and the children's preparation point, and the player's share. The runtime halves are pinned in the source.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksExecTests() => new (string, Action)[]
    {
        ("bc14w FOCAL-EXEC-FOLLOWERS the support follows the column's strike by the echelon delay, the demonstrations release with it, both wait while it has not struck", WFollowers),
        ("bc14w FOCAL-PREP-TABLE the floor is the first bombardment plus PreparationHours, for the plan, activation and focal point it was noted for", WPreparationTable),
        ("bc14w FOCAL-PREP the children's orders carry the focal point as the preparation point from the plan's activation until the column strikes, and not while the hold keeps it", WPreparationOrders),
        ("bc14w FOCAL-PLAYER a child that holds a player-owned unit is never given the column or the support rect, in the executor's table or the evaluation's", WPlayerShare),
        ("bc14w FOCAL-EXEC-WIRING PlayExecutorRuntime takes the focal point as the column's inward target, reads the group formation and the floor, and releases the followers by the column", WExecWiring),
    };

    static void NearD(double expected, double actual, string label)
        => AssertTrue(Math.Abs(expected - actual) < 1e-6, label + ": expected " + expected + " got " + actual);

    static void WFollowers()
    {
        // The column strikes at 11.0.
        NearD(11.1, EnvelopmentManeuver.FollowerReleaseHours(PlayRectKind.AssaultSupport, true, ManeuverPhase.Attack, 11.0, 11.05), "the support follows by the echelon delay");
        NearD(11.0, EnvelopmentManeuver.FollowerReleaseHours(PlayRectKind.Demonstration, true, ManeuverPhase.Attack, 11.0, 11.05), "the demonstrations with the strike");
        foreach (ManeuverPhase phase in new[] { ManeuverPhase.None, ManeuverPhase.Hold, ManeuverPhase.March, ManeuverPhase.Turn })
        {
            AssertTrue(double.IsPositiveInfinity(EnvelopmentManeuver.FollowerReleaseHours(PlayRectKind.AssaultSupport, true, phase, 10.0, 10.5)), "the support waits while the column is " + phase);
            AssertTrue(double.IsPositiveInfinity(EnvelopmentManeuver.FollowerReleaseHours(PlayRectKind.Demonstration, true, phase, 10.0, 10.5)), "so do the demonstrations in " + phase);
        }
        NearD(10.5, EnvelopmentManeuver.FollowerReleaseHours(PlayRectKind.AssaultSupport, true, ManeuverPhase.Abandoned, 10.2, 10.5), "an abandoned column releases them at once");
        NearD(10.5, EnvelopmentManeuver.FollowerReleaseHours(PlayRectKind.AssaultSupport, false, ManeuverPhase.None, 0.0, 10.5), "no column in the plan, nothing to follow");

        // Through the maneuver: a support held with an infinite release is never released by the ceiling, and goes at the strike plus the delay.
        ManeuverInput support = FlankResponseTests.Maneuver();
        support.Kind = PlayRectKind.AssaultSupport;
        support.Envelops = false;
        support.HoldsForRelease = true;
        support.StartOffsetSeconds = 0f;
        support.HasPlannedRelease = true;
        support.PlannedReleaseHours = double.PositiveInfinity;
        var state = default(ManeuverState);
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(support, 10.0)).Phase, "the support holds");
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(support, 14.0)).Phase, "past any ceiling while the column has not struck");
        support.PlannedReleaseHours = 14.2;
        AssertEqual(ManeuverPhase.Hold, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(support, 14.1)).Phase, "the strike plus the delay has not come");
        AssertEqual(ManeuverPhase.Released, EnvelopmentManeuver.Step(ref state, FlankResponseTests.After(support, 14.2)).Phase, "released behind the strike");
    }

    static void WPreparationTable()
    {
        BattleScope.ResetAll();
        PreparationTable table = PreparationTable.ForAlliance(0);
        AssertTrue(table != null && PreparationTable.ForAlliance(1) != table && PreparationTable.ForAlliance(2) == null, "one per side, bound-checked");
        AssertFalse(table.TryFloor(BattlePlanId.FocalAssault, 10.0, -300f, 600f, out _), "no battery has passed: no floor");

        table.Note(BattlePlanId.FocalAssault, 10.0, -300f, 600f, 10.4);
        AssertTrue(table.TryFloor(BattlePlanId.FocalAssault, 10.0, -300f, 600f, out double floor), "a battery passed");
        NearD(10.4 + WorksRules.PreparationHours, floor, "the floor is its hour plus PreparationHours");
        table.Note(BattlePlanId.FocalAssault, 10.0, -290f, 610f, 10.9);
        table.TryFloor(BattlePlanId.FocalAssault, 10.0, -300f, 600f, out floor);
        NearD(10.4 + WorksRules.PreparationHours, floor, "a later battery for the same point keeps the first hour (a focal point within FocalSampleMeters is the same)");

        AssertFalse(table.TryFloor(BattlePlanId.ContinuousAttrition, 10.0, -300f, 600f, out _), "another plan has no floor");
        AssertFalse(table.TryFloor(BattlePlanId.FocalAssault, 12.0, -300f, 600f, out _), "nor a plan activated at another hour");
        AssertFalse(table.TryFloor(BattlePlanId.FocalAssault, 10.0, -300f + WorksRules.FocalSampleMeters + 1f, 600f, out _), "nor a focal point moved on");

        // A note for another key resets the record: the old floor never floors the new assault.
        table.Note(BattlePlanId.FocalAssault, 12.0, -300f, 600f, 12.2);
        AssertFalse(table.TryFloor(BattlePlanId.FocalAssault, 10.0, -300f, 600f, out _), "the old assault's floor is gone");
        AssertTrue(table.TryFloor(BattlePlanId.FocalAssault, 12.0, -300f, 600f, out floor), "the new one has its own");
        NearD(12.2 + WorksRules.PreparationHours, floor, "from its own first battery");

        BattleScope.ResetAll();
        AssertFalse(table.TryFloor(BattlePlanId.FocalAssault, 12.0, -300f, 600f, out _), "BattleScope clears it");
    }

    static void WPreparationOrders()
    {
        var lookup = new HoldLookup { HasFocal = true, Focal = new FocalPoint { X = -300f, Z = 600f } };
        ArmyOrchestrator army = TwoCorpsArmy(lookup);
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.FocalAssault, BattlePhase.Probe, 2, new int[0], new int[0], 1.2f, 0f, 0));
        TacticalOperationsLedgerRuntime ledger = ActiveLedger();
        army.UpdateOperationsLedger(ledger, FourStates());

        foreach (string node in new[] { "node-2", "node-10", "node-3", "node-20" })
        {
            CommandDoctrineOrder o = OrderFor(army.CurrentDoctrineOrders, node);
            AssertTrue(o.HasPreparation && o.PreparationX == -300f && o.PreparationZ == 600f, node + " carries the focal point as its preparation point");
        }
        
        AssertFalse(OrderFor(ledger.CurrentDoctrineOrders, "node-2").HasPreparation, "the ledger's own list is untouched");

        // Another plan: none.
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.DoubleEnvelopment, BattlePhase.Probe, 2, new int[0], new int[0], 1.2f, 0f, 0));
        army.UpdateOperationsLedger(ledger, FourStates());
        AssertFalse(OrderFor(army.CurrentDoctrineOrders, "node-2").HasPreparation, "only the focal assault prepares");

        // The column strikes: the point is cleared with its Attack, and so when it is abandoned.
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.FocalAssault, BattlePhase.Probe, 2, new int[0], new int[0], 1.2f, 0f, 0));
        var march = new ManeuverOrder { Phase = ManeuverPhase.March, Task = CommandTaskType.AdvanceToAssembly, TargetX = 1f, TargetZ = 2f, Heading = -1f, Reason = "march-anchor" };
        army.SetManeuvers(new[] { new ChildManeuver("child-2", march, 1f, 0f, PlayRectKind.AssaultColumn) });
        army.UpdateOperationsLedger(ledger, FourStates());
        AssertTrue(OrderFor(army.CurrentDoctrineOrders, "node-3").HasPreparation, "the column on its march: still preparing");
        var attack = new ManeuverOrder { Phase = ManeuverPhase.Attack, Task = CommandTaskType.AttackObjective, TargetX = -300f, TargetZ = 600f, Heading = -1f, Reason = "attack-focal-point" };
        army.SetManeuvers(new[] { new ChildManeuver("child-2", attack, 1f, 0f, PlayRectKind.AssaultColumn) });
        army.UpdateOperationsLedger(ledger, FourStates());
        AssertFalse(OrderFor(army.CurrentDoctrineOrders, "node-3").HasPreparation, "cleared at the column's Attack");
        AssertFalse(OrderFor(army.CurrentDoctrineOrders, "node-10").HasPreparation, "for every node");
        var abandoned = new ManeuverOrder { Phase = ManeuverPhase.Abandoned, Task = CommandTaskType.None, Heading = -1f, Reason = "river-on-leg" };
        army.SetManeuvers(new[] { new ChildManeuver("child-2", abandoned, 1f, 0f, PlayRectKind.AssaultColumn) });
        army.UpdateOperationsLedger(ledger, FourStates());
        AssertFalse(OrderFor(army.CurrentDoctrineOrders, "node-3").HasPreparation, "and when the column is abandoned");
        army.SetManeuvers(new ChildManeuver[0]);

        // An attack of a flank (not a column) clears nothing.
        var flankAttack = new ManeuverOrder { Phase = ManeuverPhase.Attack, Task = CommandTaskType.AttackObjective, TargetX = 1f, TargetZ = 2f, Heading = -1f, Reason = "attack-line-end" };
        army.SetManeuvers(new[] { new ChildManeuver("child-2", flankAttack, 1f, 0f, PlayRectKind.FlankMarch) });
        army.UpdateOperationsLedger(ledger, FourStates());
        AssertTrue(OrderFor(army.CurrentDoctrineOrders, "node-3").HasPreparation, "another rect's Attack is not the column's");
        army.SetManeuvers(new ChildManeuver[0]);

        // Under the hold that keeps the column (hopeless assault) there is no preparation for an assault that is not going in.
        lookup.Hold = true;
        army.UpdateOperationsLedger(ledger, FourStates());
        AssertFalse(OrderFor(army.CurrentDoctrineOrders, "node-3").HasPreparation, "no preparation while the hold keeps the column");
        lookup.Hold = false;

        // No focal point, no preparation.
        lookup.HasFocal = false;
        army.UpdateOperationsLedger(ledger, FourStates());
        AssertFalse(OrderFor(army.CurrentDoctrineOrders, "node-3").HasPreparation, "no focal point, nothing to prepare");
    }

    static BattleSnapshot PlayerTree(float playerStrength)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1L, 1.0);
        s.SetSliceCount(8);
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
        s.AddOwn(new UnitRecord { InstanceId = 10, UnitTyp = 9, Flags = live | UnitFlags.IsGroup, X = 0f, Z = 0f });
        s.AddOwn(new UnitRecord { InstanceId = 11, UnitTyp = 8, Flags = live | UnitFlags.IsGroup, X = 0f, Z = 0f });
        s.AddOwn(new UnitRecord { InstanceId = 12, UnitTyp = 1, Flags = live | UnitFlags.IsInfantry | UnitFlags.PlayerOwned, Strength = (int)playerStrength });
        s.AddOwn(new UnitRecord { InstanceId = 20, UnitTyp = 9, Flags = live | UnitFlags.IsGroup, X = 500f, Z = 0f });
        s.AddOwn(new UnitRecord { InstanceId = 21, UnitTyp = 1, Flags = live | UnitFlags.IsInfantry, Strength = 400 });
        s.Own[1].ParentIndex = 0;
        s.Own[2].ParentIndex = 1;
        s.Own[4].ParentIndex = 3;
        return s;
    }

    static void WPlayerShare()
    {
        BattleSnapshot s = PlayerTree(400f);
        AssertTrue(PlayAnchor.ContainsPlayerOwned(s, 0), "the corps holds a player-owned regiment through its division");
        AssertTrue(PlayAnchor.ContainsPlayerOwned(s, 1), "and so does the division");
        AssertFalse(PlayAnchor.ContainsPlayerOwned(s, 3), "the other corps does not");
        AssertTrue(PlayAnchor.ContainsPlayerOwned(PlayerTree(0f), 0), "a player unit at zero strength counts too");
        AssertFalse(PlayAnchor.ContainsPlayerOwned(s, -1) || PlayAnchor.ContainsPlayerOwned(s, 99), "an index outside the roster holds none");

        var ids = new HashSet<string>(StringComparer.Ordinal) { "child-10", "child-20" };
        var xs = new List<float>();
        var zs = new List<float>();
        var corps = new List<CorpsPosition>();
        PlayAnchor.CollectOwn(s, 8, xs, zs, corps, ids);
        AssertEqual(2, corps.Count, "both corps are candidates");
        AssertTrue(corps[0].ContainsPlayerOwned, "the executor's table flags the corps with the player");
        AssertFalse(corps[1].ContainsPlayerOwned, "and not the other");

        // Assign: the corps with the player is closest to the column's anchor, and is given the demonstration instead.
        var column = new WorldPlayRect(PlayRectKind.AssaultColumn, 0f, 0f, 50f, 50f, 0f, 10, true);
        var demonstration = new WorldPlayRect(PlayRectKind.Demonstration, 400f, 0f, 50f, 50f, 0f, 7, false);
        var rects = new[] { column, demonstration };
        var positions = new[] { new CorpsPosition("child-10", 0f, 0f, containsPlayerOwned: true), new CorpsPosition("child-20", 500f, 0f) };
        IReadOnlyList<CorpsAssignment> result = PlayCorpsAssignment.Assign(rects, positions, Array.Empty<ObjectivePosition>());
        string columnChild = null, demoChild = null;
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i].RectKind == PlayRectKind.AssaultColumn) columnChild = result[i].ChildId;
            if (result[i].RectKind == PlayRectKind.Demonstration) demoChild = result[i].ChildId;
        }
        AssertEqual("child-20", columnChild, "the column goes to the corps with no player");
        AssertEqual("child-10", demoChild, "the corps with the player may hold the demonstration");

        // Only the player's corps: the column and the support are skipped, the demonstration is assigned.
        var support = new WorldPlayRect(PlayRectKind.AssaultSupport, 0f, 0f, 50f, 50f, 0f, 9, false);
        var only = new[] { new CorpsPosition("child-10", 0f, 0f, containsPlayerOwned: true) };
        IReadOnlyList<CorpsAssignment> alone = PlayCorpsAssignment.Assign(new[] { column, support, demonstration }, only, Array.Empty<ObjectivePosition>());
        AssertEqual(1, alone.Count, "one corps, one rect");
        AssertEqual(PlayRectKind.Demonstration, alone[0].RectKind, "and it is not the column or the support");
        Span<CorpsAssignment> into = new CorpsAssignment[3];
        AssertEqual(1, PlayCorpsAssignment.AssignInto(new[] { column, support, demonstration }, only, Array.Empty<ObjectivePosition>(), into), "AssignInto agrees");
        AssertEqual(PlayRectKind.Demonstration, into[0].RectKind, "the same rect");

        string job = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "ManeuverFeasibility.Job.cs");
        FlankResponseTests.AssertPinned(job, "PlayAnchor.ContainsPlayerOwned(", "the evaluation's per-child table fills it from the same source");
    }

    static void WExecWiring()
    {
        string play = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        string build = FlankResponseTests.Between(play, "private static ManeuverInput BuildInput(", "// What the ledger says of one child this step");
        FlankResponseTests.AssertPinned(build, "spec.Kind == PlayRectKind.AssaultColumn", "the column is read by its rect kind");
        FlankResponseTests.AssertPinned(build, "input.HasFocal", "its focal point reaches BC11");
        FlankResponseTests.AssertPinned(build, "input.GroupFormation = child.Formation;", "the group's formation comes from the snapshot");
        FlankResponseTests.AssertPinned(build, "PreparationTable.ForAlliance(", "the preparation floor comes from the table");
        FlankResponseTests.AssertPinned(build, "input.HasPreparationFloor = true;", "and is set when the table has one");
        FlankResponseTests.AssertPinned(build, "input.PreparationFloorHours = floor;", "to the table's hour");
        FlankResponseTests.AssertPinned(build, "EnvelopmentManeuver.FollowerReleaseHours(", "the followers' release is the column's");
        // The inward leg: anchor to the focal point (the corridor, the river sub-legs).
        int focal = build.IndexOf("input.HasFocal", StringComparison.Ordinal);
        int inward = build.IndexOf("float inwardX", StringComparison.Ordinal);
        AssertTrue(focal >= 0 && inward > focal, "the focal point is read before the inward target is taken");
        FlankResponseTests.AssertPinned(build, "inwardX = input.HasFocal ? input.FocalX", "which a column takes as its inward target");
        string step = FlankResponseTests.Between(play, "public static IReadOnlyList<ChildManeuver> StepManeuvers(", "internal static int ManeuveringChildren(");
        FlankResponseTests.AssertPinned(step, "new ChildManeuver(intents[i].ChildId, order, axis.RightX, axis.RightZ, spec.Kind)", "the army reads the column by the kind");
    }
}
