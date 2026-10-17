using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static ManeuverFeasibilityFixtures;

/// <summary>BC14O task 4: secondary objectives screened by at most two Screen-role children, and the screen target's copy site (requirement-main-effort-objective).</summary>
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ObjectiveScreenTests() => new (string, Action)[]
    {
        ("bc14o READER-CHILD-COPY-O a direct-child intent keeps its guard point and its screen target through the held-main swap", ReaderChildCopyO),
        ("bc14o FEAS-SCN-12 a high-value distant objective against a low-value near one: the main effort goes to A and B draws one Screen child", FeasScn12),
        ("bc14o OBJ-SCREEN at most two secondary objectives are screened, one Screen-role child each, the laterally nearest, never a Main, Fix, Support or Reserve child", ObjScreen),
        ("bc14o OBJ-SCREEN-DRAW no player-owned child and no FocalAssault demonstration is drawn; a screening child leaves the main body; the target rides the node table", ObjScreenDraw),
        ("bc14o OBJ-SCREEN-ARMY the army draws the ledger's secondary objectives into its intents after the main-role hysteresis", ObjScreenArmy),
    };

    static void ReaderChildCopyO()
    {
        DirectChildIntent plain = DirectIntent("child-1", DirectChildRole.Screen, DirectChildAxis.Hold, 4, 0.3f);
        AssertFalse(plain.HasScreenTarget, "an ordinary intent has no screen target");
        DirectChildIntent screened = plain.WithScreenTarget(1000f, 210f, 90f);
        AssertTrue(screened.HasScreenTarget, "WithScreenTarget sets it");
        AssertNear(1000f, screened.ScreenX, 1e-4f, "screen x");
        AssertNear(210f, screened.ScreenZ, 1e-4f, "screen z");
        AssertNear(90f, screened.ScreenHeading, 1e-4f, "and the heading it faces");
        AssertEqual(plain.ChildId, screened.ChildId, "it keeps the child");
        AssertEqual(plain.Role, screened.Role, "the role");
        AssertEqual(plain.Axis, screened.Axis, "the axis");
        AssertEqual(plain.AxisSector, screened.AxisSector, "the axis sector");
        AssertEqual(plain.PrimarySector, screened.PrimarySector, "the primary sector");
        AssertNear(plain.SupportPriority01, screened.SupportPriority01, 1e-4f, "the support priority");
        AssertEqual(plain.ZoneRectKind, screened.ZoneRectKind, "the zone rect");

        // A guard point is kept by the screen target, and the screen target by the held-main swap (DirectChildAllocator.With).
        DirectChildIntent both = new DirectChildIntent(plain.ChildId, plain.RawUnitTyp, plain.EffectiveCommandLevel, plain.DisplayName, plain.PrimarySector, plain.Role,
            plain.Axis, plain.AxisSector, plain.SupportPriority01, plain.EnemyIntent, plain.Maneuver, plain.ZoneRectKind, new GuardPoint(7f, 8f, 9f)).WithScreenTarget(1f, 2f, 3f);
        AssertTrue(both.HasGuardPoint && both.HasScreenTarget, "both ride together");
        DirectChildIntent incumbent = both;
        DirectChildIntent challenger = new DirectChildIntent("child-2", plain.RawUnitTyp, plain.EffectiveCommandLevel, plain.DisplayName, 3, DirectChildRole.Main, DirectChildAxis.SectorAxis,
            3, 1f, plain.EnemyIntent, plain.Maneuver, plain.ZoneRectKind, new GuardPoint(4f, 5f, 6f)).WithScreenTarget(40f, 50f, 60f);
        var incumbentMain = new DirectChildIntent("child-1", plain.RawUnitTyp, plain.EffectiveCommandLevel, plain.DisplayName, 2, DirectChildRole.SupportMain, DirectChildAxis.SectorAxis,
            2, 0.7f, plain.EnemyIntent, plain.Maneuver, plain.ZoneRectKind, new GuardPoint(1f, 2f, 3f)).WithScreenTarget(10f, 20f, 30f);
        var state = new MainRoleHysteresisState { IncumbentMainId = "child-1" };
        var held = DirectChildAllocator.StabilizeMain(new[] { incumbentMain, challenger }, state);
        AssertEqual(DirectChildRole.Main, held[0].Role, "the incumbent is held in Main");
        AssertTrue(held[0].HasGuardPoint && Math.Abs(held[0].GuardX - 1f) < 1e-4f, "the held incumbent keeps its guard point");
        AssertTrue(held[0].HasScreenTarget && Math.Abs(held[0].ScreenX - 10f) < 1e-4f && Math.Abs(held[0].ScreenHeading - 30f) < 1e-4f, "and its screen target");
        AssertTrue(held[1].HasGuardPoint && Math.Abs(held[1].GuardX - 4f) < 1e-4f, "the swapped challenger keeps its guard point");
        AssertTrue(held[1].HasScreenTarget && Math.Abs(held[1].ScreenX - 40f) < 1e-4f && Math.Abs(held[1].ScreenZ - 50f) < 1e-4f, "and its screen target");
    }

    // The axis of the scorer-free fixtures: origin (0, 0) facing +z, own extent -300..300 in five bands of 120 (band 0 is lateral -300..-180).
    static TacticalFrontAxis ScreenAxis() => TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1000f).WithOwnExtent(-300f, 300f, 5);

    static SecondaryObjective Secondary(string id, float lateral, float depth, bool own = false, float score = 0.5f)
        => new SecondaryObjective { Id = id, X = lateral, Z = depth, OwnHeld = own, Score = score };

    static DirectChildIntent ScreenChild(string id, int sector, PlayRectKind rect = PlayRectKind.ForwardScreen, DirectChildRole role = DirectChildRole.Screen)
        => new DirectChildIntent(id, 15, 15, id, sector, role, DirectChildAxis.Hold, sector, 0.3f, default(TacticalIntentModel), default(ManeuverSpec), rect);

    static void FeasScn12()
    {
        // A holds 100 points 900 units out, B 20 points 200 units out, both lightly held, daylight ample: r_A 0.1, r_B 0, every director term 0.
        var pressure = ObjectiveScoring.Pressure(Clock(0f, 0.5f, 0.5f, 10f), 0.5f);
        var a = new ObjectiveEstimate { Id = "A", Value = ObjectiveScoring.Value(1f, false), Penalty = 0.1f, Feasible = true };
        a.Score = ObjectiveScoring.Score(in a, in pressure);
        var b = new ObjectiveEstimate { Id = "B", Value = ObjectiveScoring.Value(ObjectiveScoring.Normalised(20f, 100f), false), Penalty = 0f, Feasible = true };
        b.Score = ObjectiveScoring.Score(in b, in pressure);
        AssertNear(0.76f, a.Score, 0.005f, "A scores 0.80 - 0.04");
        AssertNear(0.32f, b.Score, 0.005f, "and B 0.2 + 0.6 x 0.2");
        var rows = new[] { a, b };
        AssertEqual(0, ObjectiveScoring.PickMainEffort(rows, new float[2], null), "the main effort goes to A");
        // Vanilla's dynamic rule takes the nearest: B, 200 units out against 900.
        float[] distance = { 900f, 200f };
        int nearest = distance[0] <= distance[1] ? 0 : 1;
        AssertEqual(1, nearest, "control: the nearest objective is B");

        // B, enemy-held, draws the one Screen-role child (a Demonstration rect's); the Main child is not drawn.
        DirectChildIntent main = ScreenChild("child-1", 2, PlayRectKind.Center, DirectChildRole.Main);
        DirectChildIntent demonstration = ScreenChild("child-2", 3, PlayRectKind.Demonstration);
        var result = DirectChildAllocator.ScreenSecondaryObjectives(new[] { main, demonstration }, new[] { Secondary("B", 60f, 200f) }, 1, ScreenAxis(),
            BattlePlanId.ManeuverAndFix, null);
        AssertFalse(result[0].HasScreenTarget, "the Main child screens nothing");
        AssertTrue(result[1].HasScreenTarget, "the Demonstration child screens B");
        AssertNear(60f, result[1].ScreenX, 1e-3f, "at B's lateral");
        AssertNear(200f - WorksRules.ReserveStationMeters, result[1].ScreenZ, 1e-3f, "150 short of it on the own side");
        AssertNear(0f, result[1].ScreenHeading, 1e-3f, "facing the enemy");
    }

    static void ObjScreen()
    {
        TacticalFrontAxis axis = ScreenAxis();
        // Three Screen children in bands 0, 2 and 4 (laterals -240, 0, +240) and a Main in band 2.
        DirectChildIntent[] intents =
        {
            ScreenChild("left", 0), ScreenChild("main", 2, PlayRectKind.Center, DirectChildRole.Main), ScreenChild("mid", 2), ScreenChild("right", 4),
            ScreenChild("fix", 4, PlayRectKind.Center, DirectChildRole.Fix), ScreenChild("support", 3, PlayRectKind.LeftWing, DirectChildRole.SupportMain),
            ScreenChild("reserve", 4, PlayRectKind.Reserve, DirectChildRole.Reserve),
        };
        // An enemy-held objective at lateral 230 (the right child's side), another at -250, a third at 10: at most two are screened.
        SecondaryObjective[] secondaries = { Secondary("east", 230f, 600f), Secondary("west", -250f, 600f), Secondary("centre", 10f, 600f) };
        var result = DirectChildAllocator.ScreenSecondaryObjectives(intents, secondaries, 3, axis, BattlePlanId.ManeuverAndFix, null);
        AssertTrue(result[3].HasScreenTarget, "the right child, laterally nearest, screens the east objective");
        AssertNear(230f, result[3].ScreenX, 1e-3f, "at its lateral");
        AssertNear(600f - 150f, result[3].ScreenZ, 1e-3f, "150 short of an enemy-held objective");
        AssertTrue(result[0].HasScreenTarget, "the left child screens the west one");
        AssertNear(-250f, result[0].ScreenX, 1e-3f, "at its lateral");
        AssertFalse(result[2].HasScreenTarget, "the middle child draws no third screen: at most two objectives");
        AssertEqual(2, result.Count(i => i.HasScreenTarget), "exactly two screens");
        for (int i = 0; i < intents.Length; i++)
            if (intents[i].Role != DirectChildRole.Screen) AssertFalse(result[i].HasScreenTarget, intents[i].ChildId + " (" + intents[i].Role + ") is never drawn");

        // An own-held objective is screened at the objective itself.
        var own = DirectChildAllocator.ScreenSecondaryObjectives(new[] { ScreenChild("only", 2) }, new[] { Secondary("hill", 15f, 500f, own: true) }, 1, axis,
            BattlePlanId.ManeuverAndFix, null);
        AssertNear(500f, own[0].ScreenZ, 1e-3f, "at it when own-held");
        AssertNear(15f, own[0].ScreenX, 1e-3f, "lateral too");

        // One child per objective and one objective per child: one Screen child, two objectives, one screen.
        var one = DirectChildAllocator.ScreenSecondaryObjectives(new[] { ScreenChild("only", 2) }, new[] { Secondary("a", 0f, 600f), Secondary("b", 50f, 600f) }, 2, axis,
            BattlePlanId.ManeuverAndFix, null);
        AssertNear(0f, one[0].ScreenX, 1e-3f, "the best-scored objective (first) takes the child");
        // A play with no Screen child screens nothing: the intents come back as they were.
        var none = new[] { ScreenChild("main", 2, PlayRectKind.Center, DirectChildRole.Main) };
        AssertTrue(ReferenceEquals(none, DirectChildAllocator.ScreenSecondaryObjectives(none, new[] { Secondary("a", 0f, 600f) }, 1, axis, BattlePlanId.ManeuverAndFix, null)),
            "no Screen-role child, no screen");
        // No secondaries or no axis: nothing.
        AssertTrue(ReferenceEquals(intents, DirectChildAllocator.ScreenSecondaryObjectives(intents, secondaries, 0, axis, BattlePlanId.ManeuverAndFix, null)), "no secondary objective");
        AssertTrue(ReferenceEquals(intents, DirectChildAllocator.ScreenSecondaryObjectives(intents, secondaries, 3, TacticalFrontAxis.None, BattlePlanId.ManeuverAndFix, null)), "no axis");
    }

    static void ObjScreenDraw()
    {
        TacticalFrontAxis axis = ScreenAxis();
        SecondaryObjective[] one = { Secondary("east", 230f, 600f) };
        // A child with player-owned strength is passed over for the next nearest.
        DirectChildIntent[] intents = { ScreenChild("near", 4), ScreenChild("far", 0) };
        var skipped = DirectChildAllocator.ScreenSecondaryObjectives(intents, one, 1, axis, BattlePlanId.ManeuverAndFix, new[] { true, false });
        AssertFalse(skipped[0].HasScreenTarget, "the player-owned child is not drawn");
        AssertTrue(skipped[1].HasScreenTarget, "the next nearest is");
        AssertTrue(DirectChildAllocator.ScreenSecondaryObjectives(intents, one, 1, axis, BattlePlanId.ManeuverAndFix, new[] { true, true }).All(i => !i.HasScreenTarget),
            "with every Screen child player-owned, none is");

        // A FocalAssault demonstration pins for the assault: not drawn; the same child in another play is.
        DirectChildIntent[] demonstrations = { ScreenChild("demo", 4, PlayRectKind.Demonstration) };
        AssertFalse(DirectChildAllocator.ScreenSecondaryObjectives(demonstrations, one, 1, axis, BattlePlanId.FocalAssault, null)[0].HasScreenTarget,
            "a FocalAssault demonstration is not drawn");
        AssertTrue(DirectChildAllocator.ScreenSecondaryObjectives(demonstrations, one, 1, axis, BattlePlanId.ManeuverAndFix, null)[0].HasScreenTarget,
            "control: another play's Demonstration child is");

        // The target rides the node table as a Screen-task guard-style entry at the target, facing the enemy.
        DirectChildIntent screened = ScreenChild("child-7", 4).WithScreenTarget(230f, 450f, 0f);
        var nodes = new[] { FlankResponseTests.NodeIntent("node-7", "node-7"), FlankResponseTests.NodeIntent("node-71", "node-7") };
        ManeuverNodeTable table = EnvelopmentManeuver.ExpandToNodes(new ChildManeuver[0], nodes, new[] { screened });
        AssertTrue(table.TryGet("node-7", out ManeuverNodeEntry entry) && table.TryGet("node-71", out _), "every node of the child has an entry");
        AssertEqual(CommandTaskType.Screen, entry.Task, "the task is Screen");
        AssertTrue(entry.HasTarget && Math.Abs(entry.TargetX - 230f) < 1e-3f && Math.Abs(entry.TargetZ - 450f) < 1e-3f, "it moves to the screen target");
        AssertTrue(entry.IsGuard, "and a Defender's node takes it too");
        AssertEqual("screen-secondary", entry.Reason, "named");
        // The hash the ledger gate reads changes with the target.
        int before = DirectChildAllocator.GuardHash(new[] { ScreenChild("child-7", 4) });
        AssertTrue(before != DirectChildAllocator.GuardHash(new[] { screened }), "a screen target re-stores the orders");

        // The next evaluation leaves a screening child out of the main body: the body is weaker, so the penalty of the same objective rises.
        FeasField open = ObjField(corps: 8000f);
        open.AddObjective("t", 0f, 600f, 50f);
        float full = Row(open.Evaluate(), "t").Penalty;
        FeasField drawn = ObjField(corps: 8000f);
        drawn.ScreenChildren.Add(drawn.ChildIds[2]);
        drawn.AddObjective("t", 0f, 600f, 50f);
        AssertTrue(Row(drawn.Evaluate(), "t").Penalty > full + 0.05f, "a screening child is out of the body: the same objective is harder");
    }

    static void ObjScreenArmy()
    {
        // A ledger with the main effort A and two secondary objectives; a plan with two ForwardScreen children and a Main.
        FeasField f = ObjField();
        f.AddObjective("A", 0f, 900f, 100f);
        f.AddObjective("B", 250f, 700f, 40f);
        f.AddObjective("C", -250f, 700f, 30f);
        f.AddObjective("D", 0f, 400f, 10f, neutral: true);
        FeasibilityLedger ledger = f.Evaluate();
        var secondaries = new SecondaryObjective[ObjectiveScoring.MaxSecondaryScreens];
        int count = ObjectiveScoring.Secondaries(ledger, "A", secondaries);
        AssertEqual(2, count, "the table's enemy-held objectives but the main effort; the neutral one is not a secondary; at most two");
        AssertTrue(secondaries[0].Score >= secondaries[1].Score, "best score first");
        AssertTrue(secondaries.Take(count).All(s => s.Id == "B" || s.Id == "C"), "B and C");
        AssertEqual(0, ObjectiveScoring.Secondaries(ledger, "A", new SecondaryObjective[0]), "no room, none");
        AssertEqual(3, ObjectiveScoring.Secondaries(ledger, "none", new SecondaryObjective[4]), "with no main effort named the three enemy-held ones qualify");

        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(FeasField.X(0f), FeasField.Z(0f), FeasField.X(0f), FeasField.Z(600f)).WithOwnExtent(-300f, 300f, 5);
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var army = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.ManeuverAndFix, BattlePhase.MainEffort, 2, null, null, 1.4f, 0f, 7));
        army.SetFeasibility(ledger);
        army.ObserveFrontGeometry(new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, frontAxis: axis));
        army.RegisterDirectChildren(new[]
        {
            new DirectChildSnapshot("child-1", "army-1", 15, 0, "Main", true),
            new DirectChildSnapshot("child-2", "army-1", 15, 0, "West screen", true),
            new DirectChildSnapshot("child-3", "army-1", 15, 0, "East screen", true),
        });
        var evidence = new[]
        {
            new DirectChildEvidence(3, 2, true, 2, 0, 0.9f), new DirectChildEvidence(2, 1, false, 0, 0, 0.9f), new DirectChildEvidence(2, 1, false, 4, 0, 0.9f),
        };
        var zones = new[]
        {
            new CorpsAssignment("child-1", PlayRectKind.LeftWing, 0, 0f, 0f, 0f),
            new CorpsAssignment("child-2", PlayRectKind.ForwardScreen, 1, 0f, 0f, 0f),
            new CorpsAssignment("child-3", PlayRectKind.ForwardScreen, 2, 0f, 0f, 0f),
        };
        ObjectiveLatch latch = ObjectiveLatch.ForAlliance(1);
        try
        {
            latch.Set("A", FeasField.X(0f), FeasField.Z(900f));
            army.ObserveDirectChildEvidenceWithZones(evidence, null, zones);
            var intents = army.CurrentDirectChildIntents;
            AssertEqual(DirectChildRole.Screen, intents[1].Role, "the ForwardScreen children screen");
            AssertTrue(intents[1].HasScreenTarget && intents[2].HasScreenTarget, "each draws a secondary objective");
            AssertFalse(intents[0].HasScreenTarget, "the Main child is not drawn");
            AssertTrue(intents[2].ScreenX > intents[1].ScreenX, "the east child screens the east objective and the west child the west");
            AssertEqual(2, army.SecondaryScreens, "two screens are counted");

            // Flag off: no screens.
            bool before = Plugin.ObjectiveDoctrine;
            try
            {
                Plugin.ObjectiveDoctrine = false;
                army.ObserveDirectChildEvidenceWithZones(evidence, null, zones);
                AssertEqual(0, army.SecondaryScreens, "flag off: no screens");
            }
            finally { Plugin.ObjectiveDoctrine = before; }
        }
        finally { latch.Clear(); }
    }
}
