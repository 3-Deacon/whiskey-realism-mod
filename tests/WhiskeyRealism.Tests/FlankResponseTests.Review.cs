using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;

// BC11 verify round 1: the Center the wings wait on, the Hold ceiling, the in-combat refuse's back-off, the march leg judged on the
// planned route, and the allocation-free refuse row.
internal static partial class FlankResponseTests
{
    private static (string, Action)[] ReviewTests()
    {
        return new (string, Action)[]
        {
            ("bc11 DeployedReserveCenterChildDoesNotMakeWingsWait", DeployedReserveCenterChildDoesNotMakeWingsWait),
            ("bc11 DeployedReserveIsKeptOutOfTheRectInput", DeployedReserveIsKeptOutOfTheRectInput),
            ("bc11 HoldEndsAtTheCeiling", HoldEndsAtTheCeiling),
            ("bc11 TurnBackMemoryBacksOffAndReleases", TurnBackMemoryBacksOffAndReleases),
            ("bc11 TurnedBackRegimentIsLeftAloneByRefuseRotations", TurnedBackRegimentIsLeftAloneByRefuseRotations),
            ("bc11 TurnBackMemoryIsBoundedAndAllocatesNothing", TurnBackMemoryIsBoundedAndAllocatesNothing),
            ("bc11 RouteViaBridgeIsNotARiverCrossing", RouteViaBridgeIsNotARiverCrossing),
            ("bc11 MarchLegRecheckIsBounded", MarchLegRecheckIsBounded),
            ("bc11 MarchLegRouteIsWired", MarchLegRouteIsWired),
            ("bc11 RefuseRowThrottleBuildsNoStringOnRepeat", RefuseRowThrottleBuildsNoStringOnRepeat),
            ("bc11 NavMeshQueriesSitAtTerrainHeight", NavMeshQueriesSitAtTerrainHeight),
            ("bc11 NoRouteResultKeepsTheChildInItsPhase", NoRouteResultKeepsTheChildInItsPhase),
            ("bc11 SecondWingWaitsOneStepForItsRouteAndIsNotAbandoned", SecondWingWaitsOneStepForItsRouteAndIsNotAbandoned),
            ("bc11 RoutePlanOrderIsFairAndBounded", RoutePlanOrderIsFairAndBounded),
            ("bc11 TurnBackMemoryKeepsAnActiveBackOffOnOverflow", TurnBackMemoryKeepsAnActiveBackOffOnOverflow),
        };
    }

    // c0 on a wing rect, c1 assigned the Center rect (a deployed reserve or not), c2 on the other wing.
    private static IReadOnlyList<DirectChildIntent> CenterAllocation(bool centerIsDeployedReserve, out CorpsAssignment[] zones)
    {
        zones = new[]
        {
            new CorpsAssignment("c0", PlayRectKind.LeftWing, 0, -600f, 300f, 0f, true),
            new CorpsAssignment("c1", PlayRectKind.Center, 1, 0f, 300f, 0f),
            new CorpsAssignment("c2", PlayRectKind.RightWing, 2, 600f, 300f, 0f, true),
        };
        var evidence = new[]
        {
            new DirectChildEvidence(3, 1, false, 0, 0, 0.8f),
            new DirectChildEvidence(3, 0, false, 2, 0, 0.7f, deployedReserve: centerIsDeployedReserve),
            new DirectChildEvidence(3, 1, false, 4, 0, 0.8f),
        };
        var personality = new PersonalityVector(0.2f, 0f, 0f, 0f, 0f);
        return DirectChildAllocator.AllocateWithZones(FlankPlan(), personality, Children(3), evidence, null, zones, new ReserveReleaseContext(0.5f));
    }

    // BC10 keeps a deployed reserve out of its zone role, so a Center rect given to one is no Center: the wings must not wait on it.
    private static void DeployedReserveCenterChildDoesNotMakeWingsWait()
    {
        var acting = CenterAllocation(false, out CorpsAssignment[] zones);
        AssertEqual(PlayRectKind.Center, acting[1].ZoneRectKind, "a child that acts takes the Center role from its zone");
        AssertEqual(PlayRectKind.LeftWing, acting[0].ZoneRectKind, "the wing child's role came from its rect");
        AssertTrue((EnvelopmentManeuver.CenterChildId(zones, acting) != null), "the wings wait on a Center child whose role is its zone's");
        AssertEqual("c1", EnvelopmentManeuver.CenterChildId(zones, acting), "the waited-on child");

        var reserve = CenterAllocation(true, out zones);
        AssertEqual(DirectChildRole.Reserve, reserve[1].Role, "the deployed reserve keeps the Reserve role (BC10)");
        AssertEqual(PlayRectKind.Unknown, reserve[1].ZoneRectKind, "...so its role is not from its zone");
        AssertFalse((EnvelopmentManeuver.CenterChildId(zones, reserve) != null), "the wings do not wait on a Center that will never act");
        AssertTrue(EnvelopmentManeuver.CenterChildId(zones, reserve) == null, "no Center child");

        // A role from evidence (no zone) is no Center either.
        var fromEvidence = new[] { ZoneIntent("c0", PlayRectKind.LeftWing), ZoneIntent("c1", PlayRectKind.Unknown), ZoneIntent("c2", PlayRectKind.RightWing) };
        AssertFalse((EnvelopmentManeuver.CenterChildId(zones, fromEvidence) != null), "a Center child whose effective role did not come from its zone does not make the wings wait");
    }

    private static void DeployedReserveIsKeptOutOfTheRectInput()
    {
        string coordinator = SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        AssertPinned(coordinator, "childIds[c] = deployedReserve[c] ? string.Empty : side.Army.CurrentDirectChildIntents[c].ChildId;",
            "a deployed reserve is not offered to the zone assignment");
        // CollectOwn skips an empty id, so the reserve never reaches PlayCorpsAssignment and the Center goes to a child that acts.
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup;
        for (int i = 0; i < 3; i++)
            s.AddOwn(new UnitRecord { InstanceId = i + 1, UnitTyp = 15, X = i * 300f, Z = 0f, Flags = live });
        s.IndexCommandTree(null);
        var xs = new System.Collections.Generic.List<float>();
        var zs = new System.Collections.Generic.List<float>();
        var corps = new System.Collections.Generic.List<CorpsPosition>();
        var ids = new System.Collections.Generic.HashSet<string> { "child-1", "child-3" };   // child-2 is the deployed reserve
        PlayAnchor.CollectOwn(s, 1, xs, zs, corps, ids);
        AssertEqual(2, corps.Count, "the reserve child is not a corps for the rects");
        foreach (CorpsPosition c in corps) AssertTrue(c.ChildId != "child-2", "child-2 is never offered a rect");
    }

    private static void HoldEndsAtTheCeiling()
    {
        AssertEqual(1.0f, EnvelopmentManeuver.MaxManeuverHours, "the ceiling is MaxManeuverHours from the play's activation");
        ManeuverInput input = Maneuver();
        input.TemplateHasCenter = true;
        input.CenterContactHours = -1.0;
        input.PlayActivatedHours = 10.0;
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.99));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "still holding just under the ceiling");
        AssertEqual("await-center-contact", order.Reason, "awaiting the Center child's contact");
        order = EnvelopmentManeuver.Step(ref state, After(input, 11.0));
        AssertEqual(ManeuverPhase.March, order.Phase, "the wings proceed at the ceiling although the Center never made contact");
        AssertEqual("hold-ceiling", state.ProceedReason, "the reason is stated for the telemetry row");
        // The march clock starts then: the timeout counts from the march, not from the activation.
        order = EnvelopmentManeuver.Step(ref state, After(input, 11.2));
        AssertEqual(ManeuverPhase.March, order.Phase, "marching, not timed out");

        var contact = default(ManeuverState);
        input.CenterContactHours = 10.5;
        EnvelopmentManeuver.Step(ref contact, After(input, 10.75));
        AssertEqual("center-contact", contact.ProceedReason, "a normal release names the contact");
        string runtime = SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        AssertPinned(runtime, ".WithField(\"proceed\", proceed)", "the proceed reason rides the TacticalEnvelopment row");
    }

    private static void TurnBackMemoryBacksOffAndReleases()
    {
        double h = RefuseTurnBackMemory.BackOffHours;
        var m = new RefuseTurnBackMemory();
        AssertFalse(m.IsBackedOff(7, 40f, 100f, 5.0), "a regiment never rotated is not backed off");
        m.NoteRotated(7, 100f, 5.0);
        AssertFalse(m.IsBackedOff(7, 100f, 100f, 5.0), "still where it was put");
        AssertFalse(m.IsBackedOff(7, 110f, 125f, 5.0), "within the tolerance of where it was put");
        AssertTrue(m.IsBackedOff(7, 40f, 100f, 5.0), "settled elsewhere: vanilla (or the player) turned it back");
        AssertTrue(m.IsBackedOff(7, 40f, 105f, 5.0 + h * 0.5), "still backed off inside the window while the threat bearing holds");
        AssertFalse(m.IsBackedOff(7, 40f, 125f, 5.0 + h * 0.5), "the wanted rotation moved by more than the facing tolerance: the back-off ends");

        m.NoteRotated(8, 100f, 5.0);
        AssertTrue(m.IsBackedOff(8, 40f, 100f, 5.0), "backed off");
        AssertFalse(m.IsBackedOff(8, 40f, 100f, 5.0 + h), "the window passed");

        m.NoteRotated(9, 100f, 5.0);
        AssertTrue(m.IsBackedOff(9, 40f, 100f, 5.0), "backed off");
        m.NoteRotated(9, 90f, 5.0);
        AssertFalse(m.IsBackedOff(9, 90f, 90f, 5.01), "a new rotation ends the back-off");

        m.NoteRotated(10, 100f, 5.0);
        AssertTrue(m.IsBackedOff(10, 40f, 100f, 5.0), "backed off");
        AssertFalse(m.IsBackedOff(10, 40f, 100f, 1.0), "a clock that went back (reload) ends it");
    }

    // Through RefuseRotations: a regiment the executor rotated and vanilla turned back (the snapshot sees no enemy to explain it,
    // as when the enemy is not perceived) is not offered again until the window passes.
    private static void TurnedBackRegimentIsLeftAloneByRefuseRotations()
    {
        BattleSnapshot s = FixtureWith(20);
        MicroTurnRule rule = ShippedRule;
        var memory = new RefuseTurnBackMemory();
        var queue = new int[16]; var regiments = new int[16]; var headings = new float[16];
        int count = FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out int skipped, memory, 10.0);
        AssertEqual(2, count, "the two left-wing regiments");
        bool left1 = false;
        for (int k = 0; k < count; k++)
        {
            if (s.Own[regiments[k]].InstanceId == Left1) left1 = true;
            memory.NoteRotated(s.Own[regiments[k]].InstanceId, headings[k], 10.0);
        }
        AssertTrue(left1, "Left1 is rotated the first time");

        // Both stand at 0 again: vanilla turned them back to their enemy.
        count = FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out skipped, memory, 10.01);
        AssertEqual(0, count, "turned-back regiments are not rotated again at once");
        AssertEqual(2, skipped, "...and counted as skipped");

        count = FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out skipped, memory,
            10.01 + RefuseTurnBackMemory.BackOffHours + 0.001);
        AssertEqual(2, count, "after the window they are offered again");

        // Without a memory the behaviour is the shipped one.
        count = FlankFacing.RefuseRotations(s, 0, RefuseFlank.Left, 240f, 0.5f, 4f, in rule, queue, regiments, headings, out skipped);
        AssertEqual(2, count, "no memory, no back-off");

        string runtime = SourceText("Tactical", "Battle", "OrderExecutorRuntime.cs");
        AssertPinned(runtime, "_refuseTurnBacks.NoteRotated(view.Own[_rotationTargets[k]].InstanceId, _rotationHeadings[k], TacticalDoctrineTime.NowBattleHours);", "the executor remembers what it rotated");
        AssertPinned(runtime, "_refuseTurnBacks, TacticalDoctrineTime.NowBattleHours);", "the back-off runs on the doctrine clock");
        AssertPinned(runtime, "_refuseTurnBacks.Clear();", "the memory is cleared with the battle");
    }

    private static void TurnBackMemoryIsBoundedAndAllocatesNothing()
    {
        var m = new RefuseTurnBackMemory();
        for (int id = 1; id <= 200; id++) m.NoteRotated(id, 100f, 5.0);
        AssertEqual(RefuseTurnBackMemory.Capacity, m.Count, "the table never outgrows its capacity");
        m.Clear();
        AssertEqual(0, m.Count, "cleared");
        int sink = 0;
        AssertNoAllocation(() =>
        {
            m.NoteRotated(5, 100f, 5.0);
            sink += m.IsBackedOff(5, 40f, 100f, 5.0) ? 1 : 0;
            sink += m.IsBackedOff(5, 40f, 100f, 5.2) ? 1 : 0;
            sink += m.IsBackedOff(6, 40f, 100f, 5.0) ? 1 : 0;
        }, 100, "the turn-back memory");
        GC.KeepAlive(sink);
    }

    // A grid whose river (id 4) runs the full width at x 400-439 with a bridge (id 6) at iz 590-609. World z is iz - 1200.
    private static BattleTerrainGrid RiverWithBridgeGrid()
    {
        var ids = new int[1201, 1201];
        for (int iz = 0; iz < 1201; iz++)
            for (int ix = 400; ix < 440; ix++)
                ids[ix, iz] = (iz >= 590 && iz < 610) ? BattleTerrainGrid.BridgeId : BattleTerrainGrid.RiverId;
        return new BattleTerrainGrid(ids, 1200f, 1200f, null, false, null, null, null, 0);
    }

    // The straight line from the child to its anchor crosses the river; the route the child will walk uses the bridge.
    private static void RouteViaBridgeIsNotARiverCrossing()
    {
        BattleTerrainGrid grid = RiverWithBridgeGrid();
        var scratch = new int[4096];
        var straightX = new[] { 200f, 700f };
        var straightZ = new[] { -1100f, -1100f };
        AssertTrue(EnvelopmentManeuver.RouteHitsRiver(grid, straightX, straightZ, 2, EnvelopmentManeuver.MarchStepMeters, scratch),
            "the straight line crosses the river");
        var routeX = new[] { 200f, 390f, 450f, 700f };
        var routeZ = new[] { -1100f, -600f, -600f, -1100f };
        AssertFalse(EnvelopmentManeuver.RouteHitsRiver(grid, routeX, routeZ, 4, EnvelopmentManeuver.MarchStepMeters, scratch),
            "the route crosses on the bridge: not a river crossing");
        var fordless = new[] { 200f, 390f, 450f, 700f };
        var fordlessZ = new[] { -1100f, -1000f, -1000f, -1100f };
        AssertTrue(EnvelopmentManeuver.RouteHitsRiver(grid, fordless, fordlessZ, 4, EnvelopmentManeuver.MarchStepMeters, scratch),
            "a route that crosses the river off the bridge still hits it");
        AssertTrue(EnvelopmentManeuver.RouteHitsRiver(grid, new[] { 420f }, new[] { -1100f }, 1, 10f, scratch), "a single corner is read as a point");
        AssertFalse(EnvelopmentManeuver.RouteHitsRiver(grid, routeX, routeZ, 0, 10f, scratch), "no corners, no crossing");
        AssertFalse(EnvelopmentManeuver.RouteHitsRiver(null, routeX, routeZ, 4, 10f, scratch), "no grid, no verdict");

        // The state machine judges the route's verdict: the straight line crossing the river mirrors a lone flank only if it were the
        // result; the route over the bridge marches it.
        ManeuverInput input = Maneuver();
        input.CenterContactHours = 10.5;
        input.CanMirror = true;
        input.MarchLegHitsRiver = true;
        var straightState = default(ManeuverState);
        EnvelopmentManeuver.Step(ref straightState, After(input, 10.75));
        AssertTrue(straightState.Mirrored, "a route that hits the river mirrors the flank");
        input.MarchLegHitsRiver = false;
        var routeState = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref routeState, After(input, 10.75));
        AssertFalse(routeState.Mirrored, "a route over the bridge is not mirrored");
        AssertEqual(ManeuverPhase.March, order.Phase, "it marches");
        AssertEqual(1000f, order.TargetX, "to its own anchor");
    }

    private static void MarchLegRecheckIsBounded()
    {
        var none = default(EnvelopmentManeuver.MarchRouteCheck);
        AssertTrue(EnvelopmentManeuver.NeedsRouteRecheck(in none, 10.0, 1000f, 500f), "no result yet: the leg's start");
        var last = new EnvelopmentManeuver.MarchRouteCheck { Valid = true, CheckedHours = 10.0, AnchorX = 1000f, AnchorZ = 500f };
        AssertFalse(EnvelopmentManeuver.NeedsRouteRecheck(in last, 10.01, 1000f, 500f), "same leg, a moment later: no new route query");
        AssertFalse(EnvelopmentManeuver.NeedsRouteRecheck(in last, 10.04, 1040f, 500f), "an anchor drift of 40 m is the same leg");
        AssertTrue(EnvelopmentManeuver.NeedsRouteRecheck(in last, 10.01, 1060f, 500f), "an anchor 60 m away is a new leg");
        AssertTrue(EnvelopmentManeuver.NeedsRouteRecheck(in last, 10.0 + EnvelopmentManeuver.RouteRecheckHours, 1000f, 500f), "the bounded interval passed");
        AssertTrue(EnvelopmentManeuver.NeedsRouteRecheck(in last, 9.0, 1000f, 500f), "the clock went back");
        AssertTrue(EnvelopmentManeuver.NeedsRouteRecheck(in last, 10.01, -1000f, 500f), "the mirrored side is a new leg");
        AssertTrue(EnvelopmentManeuver.CheckForAnchor(in last, 1040f, 500f).Valid, "a result holds for an anchor that drifted 40 m");
        AssertFalse(EnvelopmentManeuver.CheckForAnchor(in last, -1000f, 500f).Valid, "a result for the other leg is no result for the mirrored anchor");
    }

    private static void MarchLegRouteIsWired()
    {
        string runtime = SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        AssertPinned(runtime, "else MarchRouteStatus(side, childId, grid, in child, anchorX, anchorZ, planned.CheckVia, planned.CheckViaX, planned.CheckViaZ, now, snapshot.TickId, ref input);",
            "the march leg is judged by the route result, and a frozen map skips it");
        AssertPinned(runtime, "NavMesh.CalculatePath(new Vector3(fromX, fromY, fromZ), new Vector3(toX, HeightAt(toX, toZ), toZ), _routeFilter, path)", "the planned route, to its end at terrain height");
        AssertPinned(runtime, "WhiskeyRealism.Tactical.Battle.Producers.PostureTargets.CreateNavFilter()", "priced like the posture probe");
        AssertPinned(runtime, "EnvelopmentManeuver.RouteHitsRiver(grid, _routeX, _routeZ, count, EnvelopmentManeuver.MarchStepMeters, _riverSamples)", "sampled segment by segment on the grid");
        AssertPinned(runtime, "side.PlanChildId = PickRoutePlanChild(side, snapshot, grid, intents, axis, stance, now, planId, feasibility);", "one route plan per side and step, served fairly");
        AssertPinned(runtime, "&& string.Equals(side.PlanChildId, childId, StringComparison.Ordinal))", "only the picked child is re-planned");
        AssertFalse(Flat(runtime).Contains("SampleLine(child.X"), "a straight line from the child to its anchor is never sampled");
        AssertPinned(runtime, "input.MarchRouteKnown = last.Valid;", "no result for the anchor: the child stays in its phase");
        AssertPinned(runtime, "hitsRiver = false; noPath = true;", "a failed plan is a NoPath result, not a straight-line fallback");
        string producers = SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        AssertPinned(producers, "t.NavFilter = CreateNavFilter();", "the posture probe shares the filter builder");
    }

    private static void RefuseRowThrottleBuildsNoStringOnRepeat()
    {
        var t = new RefuseRowThrottle();
        AssertTrue(t.ShouldEmit(1, 77, RefuseFlank.Left, 2, 0, 100f, 10f), "the first row of a group");
        AssertFalse(t.ShouldEmit(1, 77, RefuseFlank.Left, 2, 0, 105f, 10f), "the same tuple inside the window");
        AssertTrue(t.ShouldEmit(1, 77, RefuseFlank.Left, 3, 0, 106f, 10f), "a changed tuple is a new row");
        AssertTrue(t.ShouldEmit(1, 77, RefuseFlank.Right, 3, 0, 107f, 10f), "a changed flank is a new row");
        AssertFalse(t.ShouldEmit(1, 77, RefuseFlank.Right, 3, 0, 108f, 10f), "repeat");
        AssertTrue(t.ShouldEmit(1, 77, RefuseFlank.Right, 3, 0, 117f, 10f), "the window passed");
        AssertTrue(t.ShouldEmit(1, 78, RefuseFlank.Right, 3, 0, 118f, 10f), "another group has its own row");
        AssertTrue(t.ShouldEmit(0, 77, RefuseFlank.Right, 3, 0, 118f, 10f), "so does the same id on the other side");
        int sink = 0;
        AssertNoAllocation(() =>
        {
            sink += t.ShouldEmit(1, 77, RefuseFlank.Right, 3, 0, 118f, 10f) ? 1 : 0;
            sink += t.ShouldEmit(1, 78, RefuseFlank.Right, 3, 0, 119f, 10f) ? 1 : 0;
        }, 100, "the throttled repeat of the in-place refuse row");
        GC.KeepAlive(sink);

        string producers = SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string method = Between(producers, "internal static void RefuseInPlace(", "private static void Reset()");
        int throttle = method.IndexOf("_refuseRows.ShouldEmit(", StringComparison.Ordinal);
        int signature = method.IndexOf("string signature", StringComparison.Ordinal);
        AssertTrue(throttle >= 0 && signature > throttle, "the throttle is checked before the signature string is built");
        AssertFalse(method.Contains("TacticalTelemetry.ShouldEmit"), "the string-keyed throttle is not on this path");
    }

    // A child with no route result for its leg is neither launched nor abandoned: it stays where it is, whatever stale flags say.
    private static void NoRouteResultKeepsTheChildInItsPhase()
    {
        ManeuverInput input = Maneuver();
        input.StartOffsetSeconds = 0f;
        input.TemplateHasCenter = false;
        input.PlayActivatedHours = 10.0;
        input.MarchRouteKnown = false;
        input.MarchLegHitsRiver = true;       // a stale or straight-line river flag must not be read
        input.CanMirror = true;
        var state = default(ManeuverState);
        ManeuverOrder order = EnvelopmentManeuver.Step(ref state, After(input, 10.2));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "the offset is over but the route is not known: Hold");
        AssertEqual("await-route", order.Reason, "reason");
        AssertFalse(state.Mirrored, "not mirrored on a flag without a result");
        order = EnvelopmentManeuver.Step(ref state, After(input, 10.9));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "still waiting, not abandoned");
        // Even past the Hold ceiling the launch needs its route.
        order = EnvelopmentManeuver.Step(ref state, After(input, 11.5));
        AssertEqual(ManeuverPhase.Hold, order.Phase, "the ceiling does not launch a wing without a route result");

        input.MarchRouteKnown = true;
        input.MarchLegHitsRiver = false;
        order = EnvelopmentManeuver.Step(ref state, After(input, 11.51));
        AssertEqual(ManeuverPhase.March, order.Phase, "launched the step its route is known");

        // A launched child whose leg changed keeps marching until the new result: no abandon on the missing result.
        input.MarchRouteKnown = false;
        input.MarchLegHitsRiver = true;
        order = EnvelopmentManeuver.Step(ref state, After(input, 11.6));
        AssertEqual(ManeuverPhase.March, order.Phase, "marching, not abandoned, without a route result");
        // A known result with no complete path is a result: the leg cannot be walked.
        input.MarchRouteKnown = true;
        input.MarchLegHitsRiver = false;
        input.MarchRouteMissing = true;
        input.CanMirror = false;
        order = EnvelopmentManeuver.Step(ref state, After(input, 11.7));
        AssertEqual(ManeuverPhase.Abandoned, order.Phase, "no path is abandoned");
        AssertEqual("no-route", order.Reason, "reason");
    }

    // DoubleEnvelopment: two wings at offset 0, no Center. One route plan per step: the second wing holds one step for its route and
    // is then judged on it (a bridge beside the river), not on the straight line across the river.
    private static void SecondWingWaitsOneStepForItsRouteAndIsNotAbandoned()
    {
        BattleTerrainGrid grid = RiverWithBridgeGrid();
        var scratch = new int[4096];
        bool straightHitsRiver = EnvelopmentManeuver.RouteHitsRiver(grid, new[] { 200f, 700f }, new[] { -1100f, -1100f }, 2, 10f, scratch);
        bool routeHitsRiver = EnvelopmentManeuver.RouteHitsRiver(grid, new[] { 200f, 390f, 450f, 700f }, new[] { -1100f, -600f, -600f, -1100f }, 4, 10f, scratch);
        AssertTrue(straightHitsRiver && !routeHitsRiver, "fixture: the straight line crosses the river, the route uses the bridge");

        ManeuverInput wing = Maneuver();
        wing.Kind = PlayRectKind.RightWing;
        wing.StartOffsetSeconds = 0f;
        wing.TemplateHasCenter = false;
        wing.PlayActivatedHours = 10.0;
        wing.CanMirror = false;                       // a double wing: a failure would be final
        wing.MarchRouteKnown = false;
        var checks = new EnvelopmentManeuver.MarchRouteCheck[2];
        var needs = new[] { true, true };
        var states = new ManeuverState[2];

        // Step 1: the picker serves wing 0; wing 1 has no result and holds.
        int pick = EnvelopmentManeuver.PickRoutePlan(checks, needs, 2);
        AssertEqual(0, pick, "step 1 plans the first wing");
        checks[0] = new EnvelopmentManeuver.MarchRouteCheck { Valid = true, CheckedHours = 10.0, AnchorX = 1000f, AnchorZ = 500f, HitsRiver = routeHitsRiver };
        needs[0] = false;
        ManeuverInput a = wing; a.MarchRouteKnown = true; a.MarchLegHitsRiver = checks[0].HitsRiver;
        ManeuverInput b = wing;                       // no result yet
        ManeuverOrder oa = EnvelopmentManeuver.Step(ref states[0], After(a, 10.0));
        ManeuverOrder ob = EnvelopmentManeuver.Step(ref states[1], After(b, 10.0));
        AssertEqual(ManeuverPhase.March, oa.Phase, "wing 0 is judged on its route and launches");
        AssertEqual(ManeuverPhase.Hold, ob.Phase, "wing 1 holds one step");
        AssertEqual("await-route", ob.Reason, "waiting for its route");

        // Step 2: wing 1 is next; its route uses the bridge, so it marches.
        pick = EnvelopmentManeuver.PickRoutePlan(checks, needs, 2);
        AssertEqual(1, pick, "step 2 plans the second wing");
        checks[1] = new EnvelopmentManeuver.MarchRouteCheck { Valid = true, CheckedHours = 10.01, AnchorX = 1000f, AnchorZ = 500f, HitsRiver = routeHitsRiver };
        b.MarchRouteKnown = true;
        b.MarchLegHitsRiver = checks[1].HitsRiver;
        ob = EnvelopmentManeuver.Step(ref states[1], After(b, 10.01));
        AssertEqual(ManeuverPhase.March, ob.Phase, "wing 1 marches on its route, it is not abandoned for the straight line's river");
        AssertTrue(states[1].AbandonReason == null, "never abandoned");
    }

    private static void RoutePlanOrderIsFairAndBounded()
    {
        // N enveloping children, none with a result yet: each gets its first plan within N steps, one plan per step.
        for (int n = 1; n <= 6; n++)
        {
            var checks = new EnvelopmentManeuver.MarchRouteCheck[n];
            var needs = new bool[n];
            for (int i = 0; i < n; i++) needs[i] = true;
            var served = new bool[n];
            for (int step = 1; step <= n; step++)
            {
                int pick = EnvelopmentManeuver.PickRoutePlan(checks, needs, n);
                AssertTrue(pick >= 0 && !served[pick], "step " + step + " of " + n + " serves a child without a result");
                served[pick] = true;
                checks[pick] = new EnvelopmentManeuver.MarchRouteCheck { Valid = true, CheckedHours = 10.0 + step * 0.01, AnchorX = 1f, AnchorZ = 1f };
                needs[pick] = false;
            }
            for (int i = 0; i < n; i++) AssertTrue(served[i], "child " + i + " of " + n + " has its first route within " + n + " steps");
        }

        // A first plan goes before any re-check, even for a lower index and a stale result.
        var mix = new[]
        {
            new EnvelopmentManeuver.MarchRouteCheck { Valid = true, CheckedHours = 5.0, AnchorX = 1f, AnchorZ = 1f },
            default(EnvelopmentManeuver.MarchRouteCheck),
            new EnvelopmentManeuver.MarchRouteCheck { Valid = true, CheckedHours = 4.0, AnchorX = 1f, AnchorZ = 1f },
        };
        AssertEqual(1, EnvelopmentManeuver.PickRoutePlan(mix, new[] { true, true, true }, 3), "the child with no result outranks the stalest re-check");
        mix[1] = new EnvelopmentManeuver.MarchRouteCheck { Valid = true, CheckedHours = 9.0, AnchorX = 1f, AnchorZ = 1f };
        AssertEqual(2, EnvelopmentManeuver.PickRoutePlan(mix, new[] { true, true, true }, 3), "then the stalest result goes first");
        // "No result goes first" is not "stalest first": a result checked at doctrine hour 0 (the battle's start) is as stale as an empty
        // one by the clock, yet the empty one must still go first whatever the index order.
        var atZero = new[]
        {
            new EnvelopmentManeuver.MarchRouteCheck { Valid = true, CheckedHours = 0.0, AnchorX = 1f, AnchorZ = 1f },
            default(EnvelopmentManeuver.MarchRouteCheck),
        };
        AssertEqual(1, EnvelopmentManeuver.PickRoutePlan(atZero, new[] { true, true }, 2), "an empty result goes before a result checked at hour 0");
        AssertTrue(EnvelopmentManeuver.PlansBefore(in atZero[1], in atZero[0]) && !EnvelopmentManeuver.PlansBefore(in atZero[0], in atZero[1]),
            "the order is strict: empty before a result checked at hour 0, never the reverse");
        AssertEqual(-1, EnvelopmentManeuver.PickRoutePlan(mix, new[] { false, false, false }, 3), "nobody needs one: no plan");
        AssertEqual(-1, EnvelopmentManeuver.PickRoutePlan(null, null, 0), "no children, no plan");
    }

    private static void TurnBackMemoryKeepsAnActiveBackOffOnOverflow()
    {
        double h = RefuseTurnBackMemory.BackOffHours;
        // 64 entries, one of them backed off: the 65th rotation evicts a plain entry, not the active back-off.
        var m = new RefuseTurnBackMemory();
        for (int id = 1; id <= RefuseTurnBackMemory.Capacity; id++) m.NoteRotated(id, 100f, 5.0);
        AssertTrue(m.IsBackedOff(1, 40f, 100f, 5.0), "regiment 1 is backed off");
        m.NoteRotated(1000, 100f, 5.01);
        AssertEqual(RefuseTurnBackMemory.Capacity, m.Count, "the table stays at its capacity");
        AssertTrue(m.IsBackedOff(1, 40f, 100f, 5.02), "the 65th rotation did not forget the active back-off");
        AssertTrue(m.IsBackedOff(1000, 40f, 100f, 5.02), "and the new regiment is remembered");

        // An expired back-off goes before a plain entry.
        var e = new RefuseTurnBackMemory();
        for (int id = 1; id <= RefuseTurnBackMemory.Capacity; id++) e.NoteRotated(id, 100f, 5.0);
        e.IsBackedOff(2, 40f, 100f, 5.0);                      // backed off until 5.0 + h
        e.NoteRotated(2000, 100f, 5.0 + h + 0.01);             // 2 has expired
        AssertFalse(e.IsBackedOff(2, 40f, 100f, 5.0 + h + 0.02), "the expired back-off was evicted");
        AssertTrue(e.IsBackedOff(3, 40f, 100f, 5.0 + h + 0.02), "a plain entry was kept");
        AssertTrue(e.IsBackedOff(1, 40f, 100f, 5.0 + h + 0.02), "the plain entry the ring would reach first was kept too: the expired one went first");

        // All 64 active: the one closest to expiry goes.
        var all = new RefuseTurnBackMemory();
        for (int id = 1; id <= RefuseTurnBackMemory.Capacity; id++) all.NoteRotated(id, 100f, 5.0);
        for (int id = 1; id <= RefuseTurnBackMemory.Capacity; id++) all.IsBackedOff(id, 40f, 100f, 5.0 + id * 0.0001);   // regiment 1 expires first
        all.NoteRotated(3000, 100f, 5.0 + 0.01);
        AssertFalse(all.IsBackedOff(1, 40f, 100f, 5.011), "the back-off closest to expiry was evicted");
        AssertTrue(all.IsBackedOff(2, 40f, 100f, 5.011), "the others stay");
        AssertTrue(all.IsBackedOff(64, 40f, 100f, 5.011), "the latest back-off stays");
        AssertEqual(RefuseTurnBackMemory.Capacity, all.Count, "capacity held");
    }

    // CalculatePath / SamplePosition snap each end to the mesh within a small extent: a target built with y = 0 misses it on any map with
    // relief (vanilla paths from GetPosition() + (0, 1.5, 0) to waypoint positions at the terrain height, AddPath 130259 and
    // GetTerrainHeight 27002). Every Whiskey query is pinned: the set of call sites, and the target of each at terrain height.
    private static void NavMeshQueriesSitAtTerrainHeight()
    {
        string root = TestFixtures.FindRepoPath("src/WhiskeyRealism");
        var calls = new System.Collections.Generic.List<string>();
        var query = new System.Text.RegularExpressions.Regex(@"NavMesh\.(CalculatePath|SamplePosition)\(([^;]*?)\)\s*(;|&&|\)|\|\|)");
        foreach (string file in System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
        {
            string text = System.IO.File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in query.Matches(text))
            {
                string line = text.Substring(0, m.Index);
                // A comment mention is not a call.
                int lineStart = line.LastIndexOf('\n') + 1;
                if (text.Substring(lineStart, m.Index - lineStart).TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                string args = m.Groups[2].Value;
                calls.Add(System.IO.Path.GetFileName(file) + ": " + Flat(args));
                AssertFalse(System.Text.RegularExpressions.Regex.IsMatch(args, @"new Vector3\([^()]*,\s*0(\.0)?f?\s*,"),
                    "no NavMesh query is built with a literal 0 height: " + Flat(args));
            }
        }
        AssertEqual(4, calls.Count, "the Whiskey NavMesh queries (a new one must be added here with its target at terrain height): " + string.Join(" | ", calls));

        string producers = SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        AssertPinned(producers, "Vector3 to = PostureTargets.AtTerrain(x, z);", "the road-route plan target is at terrain height");
        AssertPinned(producers, "NavMesh.CalculatePath(own, AtTerrain(candidate.x, candidate.z), t.NavFilter, path)", "the probe's candidate is at terrain height");
        AssertPinned(producers, "return new Vector3(x, SampleHeight(new Vector3(x, 0f, z)), z);", "AtTerrain reads the terrain height");
        AssertFalse(Flat(producers).Contains("var to = new Vector3(x, 0f, z);"), "the old y = 0 target is gone");
        string runtime = SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        AssertPinned(runtime, "new Vector3(toX, HeightAt(toX, toZ), toZ), _routeFilter, path)", "the envelopment route target is at terrain height");
        string confirm = SourceText("Tactical", "Orchestrator", "Plays", "FeasibilityRuntime.cs");
        AssertPinned(confirm, "NavMesh.CalculatePath(PostureTargets.AtTerrain(x0, z0), PostureTargets.AtTerrain(x1, z1), filter, path)",
            "BC14's confirmation query (the fourth site) starts and ends at terrain height");
    }
}
