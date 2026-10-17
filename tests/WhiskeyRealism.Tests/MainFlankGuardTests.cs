using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17: the Union's lone main-effort brigade (2nd Brigade, 5th Massachusetts) advanced with no neighbour and no flank guard and was shot in the
// flank. A main effort advances only when its exposed flank (the side with no friendly line unit within a frontage plus a gap) has a guard assigned (a flank
// guard or a supporting attack) or rests on impassable terrain; otherwise it holds, and the allocation gives the exposed side a guard from the reserve, else
// from the support pool (MainFlankGuard).
internal static class MainFlankGuardTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("flank guard a main with line units in reach on both sides is covered", AMainWithNeighboursIsCovered),
            ("flank guard a lone main is open on both flanks and holds", ALoneMainIsOpen),
            ("flank guard only the side with no neighbour in reach is open", OnlyTheBareSideIsOpen),
            ("flank guard a unit past a frontage and a gap is no neighbour", AUnitPastTheGapIsNoNeighbour),
            ("flank guard a reserve, a fallback guard and a unit behind the main cover nothing", HeldBackUnitsCoverNothing),
            ("flank guard an assigned guard not yet in reach is in transit and the main keeps holding", AnAssignedGuardInTransitHolds),
            ("flank guard a guard that has come into reach covers the side", AGuardInReachCovers),
            ("flank guard an end that rests on a river is not open", ATerrainAnchorCovers),
            ("flank guard the hold starts at once and ends only after its minimum and a covered spell", TheHoldHasHysteresis),
            ("flank guard an open flank during the covered spell restarts it", AnOpenFlankRestartsTheCount),
            ("flank guard with nobody to give the main advances once the bound passes", NobodyToGiveAdvancesAtTheBound),
            ("flank guard a guard in transit keeps the hold until it arrives or the same bound passes", InTransitHoldsUntilArrivalOrTheBound),
            ("flank guard the advance holds while the flank stays open and a covered flank clears it", TheAdvanceIsStickyUntilCovered),
            ("flank guard the bound is on the doctrine clock and sits between the arrival hold and the plan commitment", TheBoundIsOnTheDoctrineClock),
            ("flank guard each change of the hold writes one row with its reason", EachChangeHasOneReason),
            ("flank guard the exposed side is given the nearest reserve on that side", TheNearestReserveOnTheSideIsGiven),
            ("flank guard a side with no reserve takes the nearest reserve of the other side", TheOtherSidesReserveIsTheNext),
            ("flank guard with no reserve the support pool gives one, and only one that leaves its own side covered", TheSupportPoolGivesOne),
            ("flank guard nobody to give leaves the flank open", NobodyToGiveLeavesItOpen),
            ("flank guard a terrain anchored side is given nobody", AnAnchoredSideIsGivenNobody),
            ("flank guard the same enemy and odds one minute apart give one answer", TheAnswerIsStable),
            ("flank guard the leaf map gives the lone main's open side a guard from the reserve", TheLeafMapAssignsTheGuard),
            ("flank guard the leaf map leaves a Defender alone", TheLeafMapLeavesADefenderAlone),
            ("flank guard the posture producer holds the main on an open flank and the exploit producer sees it held", ThePostureProducerHolds),
        });
    }

    private static MainFlankGuard.Neighbour N(float lateral, DirectChildRole role) => new MainFlankGuard.Neighbour(lateral, role);

    private static void AMainWithNeighboursIsCovered()
    {
        var cover = MainFlankGuard.Assess(new[] { N(-600f, DirectChildRole.SupportMain), N(650f, DirectChildRole.Fix) }, false, false);
        AssertTrue(!cover.Open, "covered: " + cover.Reason);
        AssertEqual("flanks-covered", cover.Reason, "reason");
    }

    private static void ALoneMainIsOpen()
    {
        var cover = MainFlankGuard.Assess(Array.Empty<MainFlankGuard.Neighbour>(), false, false);
        AssertTrue(cover.LeftOpen && cover.RightOpen && cover.Open, "both open");
        AssertEqual("both-flanks-open", cover.Reason, "reason");
    }

    private static void OnlyTheBareSideIsOpen()
    {
        var cover = MainFlankGuard.Assess(new[] { N(500f, DirectChildRole.SupportMain) }, false, false);
        AssertTrue(cover.LeftOpen && !cover.RightOpen, "left open, right covered");
        AssertEqual("left-flank-open", cover.Reason, "reason");
    }

    private static void AUnitPastTheGapIsNoNeighbour()
    {
        float edge = MainFlankGuard.ReachMeters;
        AssertTrue(!MainFlankGuard.Assess(new[] { N(-edge, DirectChildRole.Fix), N(edge, DirectChildRole.Fix) }, false, false).Open, "exactly a frontage plus a gap away is a neighbour");
        var past = MainFlankGuard.Assess(new[] { N(-(edge + 50f), DirectChildRole.Fix), N(edge + 50f, DirectChildRole.Fix) }, false, false);
        AssertTrue(past.LeftExposed && past.RightExposed, "past it, both flanks are exposed (a Fix is no guard)");
        AssertTrue(past.Open, "and open");
    }

    private static void HeldBackUnitsCoverNothing()
    {
        var cover = MainFlankGuard.Assess(new[]
        {
            N(-400f, DirectChildRole.Reserve), N(400f, DirectChildRole.Fallback), N(50f, DirectChildRole.SupportMain), N(-30f, DirectChildRole.Fix),
        }, false, false);
        AssertTrue(cover.LeftOpen && cover.RightOpen, "a reserve, a fallback guard and units stacked behind the main are no flank cover");
    }

    private static void AnAssignedGuardInTransitHolds()
    {
        var far = MainFlankGuard.Assess(new[] { N(-1800f, DirectChildRole.RefuseLeft), N(1500f, DirectChildRole.SupportMain) }, false, false);
        AssertTrue(far.LeftGuarded && far.RightGuarded, "a flank guard and a support in range are assigned to the sides they stand for");
        AssertTrue(far.Open && far.InTransit && !far.Unguarded, "but past the reach they are in transit: the main keeps holding");
        AssertEqual(MainFlankGuard.InTransitReason, far.HoldWhy, "the hold's reason says a guard is on its way");
        var out0 = MainFlankGuard.Assess(new[] { N(-(MainFlankGuard.GuardRangeMeters + 100f), DirectChildRole.RefuseLeft) }, false, false);
        AssertTrue(out0.Unguarded && !out0.InTransit, "a guard beyond its range is no guard: nobody is assigned");
        AssertEqual(MainFlankGuard.HoldReason, out0.HoldWhy, "the reason says the flank is unguarded");
        var wrongSide = MainFlankGuard.Assess(new[] { N(900f, DirectChildRole.RefuseLeft) }, false, false);
        AssertTrue(wrongSide.LeftGuarded && wrongSide.LeftOpen && !wrongSide.RightGuarded, "a RefuseLeft guards the left, wherever it stands; it is still on its way, and the right has nobody");
    }

    private static void AGuardInReachCovers()
    {
        var arrived = MainFlankGuard.Assess(new[] { N(-600f, DirectChildRole.RefuseLeft), N(650f, DirectChildRole.SupportMain) }, false, false);
        AssertTrue(!arrived.Open, "guards within a frontage plus a gap cover both sides");
    }

    private static void ATerrainAnchorCovers()
    {
        var cover = MainFlankGuard.Assess(new[] { N(500f, DirectChildRole.SupportMain) }, leftAnchored: true, rightAnchored: false);
        AssertTrue(!cover.Open && cover.LeftAnchored, "the left end rests on a river: not open");
    }

    private static void TheHoldHasHysteresis()
    {
        const float minute = 1f / 60f;
        var state = default(MainFlankGuard.HoldState);
        state = MainFlankGuard.Step(state, open: true, nowHours: 5.0f);
        AssertTrue(state.Holding, "an open flank holds the main at once");
        state = MainFlankGuard.Step(state, open: false, nowHours: 5.0f + 0.5f * minute);
        AssertTrue(state.Holding, "covered for an instant: still holding");
        state = MainFlankGuard.Step(state, open: false, nowHours: 5.0f + 1.6f * minute);
        AssertTrue(state.Holding, "covered for over a minute but the hold has not lasted two: still holding");
        state = MainFlankGuard.Step(state, open: false, nowHours: 5.0f + 2.1f * minute);
        AssertTrue(!state.Holding, "held two minutes and covered for a minute: released");
    }

    private static void AnOpenFlankRestartsTheCount()
    {
        const float minute = 1f / 60f;
        var state = MainFlankGuard.Step(default(MainFlankGuard.HoldState), true, 5f);
        state = MainFlankGuard.Step(state, false, 5f + 2f * minute);
        state = MainFlankGuard.Step(state, true, 5f + 2.5f * minute);
        state = MainFlankGuard.Step(state, false, 5f + 3f * minute);
        AssertTrue(state.Holding, "covered again only half a minute: the count restarted");
        state = MainFlankGuard.Step(state, false, 5f + 4.1f * minute);
        AssertTrue(!state.Holding, "covered a full minute again: released");
    }

    private const float Minute = 1f / 60f;

    private static MainFlankGuard.HoldState Hold(float fromHours, float minutes, bool open = true)
    {
        var state = default(MainFlankGuard.HoldState);
        float now = fromHours;
        state = MainFlankGuard.Step(state, open, now);
        // one look every 30 battle seconds, as the posture pass sees it
        for (float m = 0.5f; m <= minutes + 0.001f; m += 0.5f) state = MainFlankGuard.Step(state, open, fromHours + m * Minute);
        return state;
    }

    private static void NobodyToGiveAdvancesAtTheBound()
    {
        var bare = MainFlankGuard.Assess(Array.Empty<MainFlankGuard.Neighbour>(), false, false);
        AssertTrue(bare.Unguarded, "nobody is assigned to the flank");
        AssertTrue(Hold(5f, 19.5f).Holding, "after 19.5 minutes the main still holds");
        var past = Hold(5f, 20.5f);
        AssertTrue(!past.Holding && past.Advanced, "after 20 the main advances anyway");
        AssertEqual(MainFlankGuard.AdvanceReason, MainFlankGuard.ChangeReason(Hold(5f, 19.5f), past, bare), "with the row reason flank-unguarded-advance");
        AssertEqual("flank-unguarded-advance", MainFlankGuard.AdvanceReason, "the reason's text");
    }

    private static void InTransitHoldsUntilArrivalOrTheBound()
    {
        var transit = MainFlankGuard.Assess(new[] { N(-1500f, DirectChildRole.RefuseLeft), N(500f, DirectChildRole.SupportMain) }, false, false);
        AssertTrue(transit.InTransit, "a guard is on its way");
        // arrives at minute 12: the flanks are covered for a minute and the hold is released, no advance
        var state = Hold(5f, 11.5f);
        AssertTrue(state.Holding, "holding in transit");
        float arrived = 5f + 12f * Minute;
        state = MainFlankGuard.Step(state, false, arrived);
        state = MainFlankGuard.Step(state, false, arrived + 1.1f * Minute);
        AssertTrue(!state.Holding && !state.Advanced, "the guard arrived: released by cover, not by the bound");
        AssertEqual(MainFlankGuard.ReleaseReason, MainFlankGuard.ChangeReason(Hold(5f, 11.5f), state, transit), "the release row says covered");
        // never arrives: the same bound passes
        var stuck = Hold(5f, 20.5f);
        AssertTrue(!stuck.Holding && stuck.Advanced, "a guard that does not arrive is waited for no longer than the bound");
    }

    private static void TheAdvanceIsStickyUntilCovered()
    {
        var advanced = Hold(5f, 20.5f);
        float t = 5f + 21f * Minute;
        var next = MainFlankGuard.Step(advanced, true, t);
        AssertTrue(!next.Holding && next.Advanced, "still open: the main is not stopped again on the next look");
        next = MainFlankGuard.Step(next, true, t + 10f * Minute);
        AssertTrue(!next.Holding && next.Advanced, "ten minutes on, still advancing");
        next = MainFlankGuard.Step(next, false, t + 11f * Minute);
        AssertTrue(!next.Holding && !next.Advanced, "a covered flank clears the advance");
        next = MainFlankGuard.Step(next, true, t + 12f * Minute);
        AssertTrue(next.Holding, "a flank that opens again starts a new hold");
        AssertTrue(MainFlankGuard.Step(next, true, t + 12f * Minute + 19f * Minute).Holding, "with its own full bound");
    }

    private static void TheBoundIsOnTheDoctrineClock()
    {
        AssertEqual(1200f, MainFlankGuard.MaxHoldBattleSeconds, "twenty battle-minutes");
        AssertTrue(MainFlankGuard.MaxHoldBattleSeconds > MainArrivalHoldLatch.MaxHoldBattleSeconds, "longer than the arrival hold's budget");
        AssertTrue(MainFlankGuard.MaxHoldBattleSeconds <= ArmyReplanTriggers.PlanCommitmentBattleSeconds / 3f, "a third of the plan commitment hold at most: a hold never outlives its plan");
        // The clock is the doctrine hours: 0.3 h is 18 minutes, 0.34 h is 20.4.
        var start = MainFlankGuard.Step(default(MainFlankGuard.HoldState), true, 2.0f);
        AssertTrue(MainFlankGuard.Step(start, true, 2.30f).Holding, "0.30 doctrine hours into the hold: holding");
        AssertTrue(MainFlankGuard.Step(start, true, 2.34f).Advanced, "0.34 doctrine hours into the hold: advanced");
        AssertTrue(MainFlankGuard.Step(start, true, 2.0f + 1190f / 3600f).Holding, "1190 battle seconds: still holding");
        AssertTrue(MainFlankGuard.Step(start, true, 2.0f + 1210f / 3600f).Advanced, "1210 battle seconds: advanced");
    }

    private static void EachChangeHasOneReason()
    {
        var unguarded = MainFlankGuard.Assess(Array.Empty<MainFlankGuard.Neighbour>(), false, false);
        var transit = MainFlankGuard.Assess(new[] { N(-1500f, DirectChildRole.RefuseLeft), N(500f, DirectChildRole.SupportMain) }, false, false);
        var none = default(MainFlankGuard.HoldState);
        var held = MainFlankGuard.Step(none, true, 5f);
        AssertEqual(MainFlankGuard.HoldReason, MainFlankGuard.ChangeReason(none, held, unguarded), "starting with nobody assigned");
        AssertEqual(MainFlankGuard.InTransitReason, MainFlankGuard.ChangeReason(none, held, transit), "starting with a guard on its way");
        AssertTrue(MainFlankGuard.ChangeReason(held, MainFlankGuard.Step(held, true, 5f + Minute), unguarded) == null, "a hold that goes on writes nothing");
        AssertTrue(MainFlankGuard.ChangeReason(none, none, unguarded) == null, "no hold, no change: nothing");
    }

    private static DirectChildRole[] Roles(params DirectChildRole[] roles) => roles;

    private static void TheNearestReserveOnTheSideIsGiven()
    {
        // main at 0; reserves at -300 and -1200 (left), -400 (right); a support at +500 covers the right.
        var roles = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.Reserve, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        var lateral = new[] { 0f, -300f, -1200f, -400f, 500f };
        var report = new List<MainFlankGuard.GuardAssignment>();
        int n = MainFlankGuard.AssignGuards(lateral, roles, null, report);
        AssertEqual(1, n, "one guard given");
        AssertEqual(DirectChildRole.RefuseLeft, roles[1], "the nearest left reserve guards the left flank");
        AssertEqual(DirectChildRole.Reserve, roles[2], "the other left reserve stays");
        AssertEqual(DirectChildRole.Reserve, roles[3], "the right reserve stays");
        AssertTrue(report.Count == 1 && report[0].Left && report[0].Main == 0 && report[0].Guard == 1 && report[0].From == DirectChildRole.Reserve, "the report names the main, the guard, the side and the pool");
    }

    private static void TheOtherSidesReserveIsTheNext()
    {
        var roles = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        var lateral = new[] { 0f, 700f, -500f };
        // right: the reserve is in reach but a reserve covers nothing; left is covered by the support.
        int n = MainFlankGuard.AssignGuards(lateral, roles, null, null);
        AssertEqual(1, n, "one guard given");
        AssertEqual(DirectChildRole.RefuseRight, roles[1], "the right reserve guards the right flank");

        var roles2 = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        var lateral2 = new[] { 0f, -900f, 400f };
        MainFlankGuard.AssignGuards(lateral2, roles2, null, null);
        AssertEqual(DirectChildRole.RefuseLeft, roles2[1], "the left reserve guards the left flank (the right is covered by the support)");

        var roles3 = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        var lateral3 = new[] { 0f, 1000f, 400f };
        int n3 = MainFlankGuard.AssignGuards(lateral3, roles3, null, null);
        AssertEqual(1, n3, "the left is bare and the only reserve stands to the right: it takes the left");
        AssertEqual(DirectChildRole.RefuseLeft, roles3[1], "a RefuseLeft guard, wherever it stands");
    }

    private static void TheSupportPoolGivesOne()
    {
        // main 0; left bare; right: supports at +500 and +1400. No reserve. The farthest right support moves to the left.
        var roles = Roles(DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.SupportMain);
        var lateral = new[] { 0f, 500f, 1400f };
        var report = new List<MainFlankGuard.GuardAssignment>();
        MainFlankGuard.AssignGuards(lateral, roles, null, report);
        AssertEqual(DirectChildRole.SupportMain, roles[1], "the support in reach stays");
        AssertEqual(DirectChildRole.RefuseLeft, roles[2], "the far support guards the bare left");
        AssertEqual(DirectChildRole.SupportMain, report[0].From, "from the support pool");

        // One support only: it cannot be spared (the right would be bare).
        var alone = Roles(DirectChildRole.Main, DirectChildRole.SupportMain);
        MainFlankGuard.AssignGuards(new[] { 0f, 500f }, alone, null, null);
        AssertEqual(DirectChildRole.SupportMain, alone[1], "a lone support is not taken from the side it covers");
    }

    private static void NobodyToGiveLeavesItOpen()
    {
        var roles = Roles(DirectChildRole.Main);
        int n = MainFlankGuard.AssignGuards(new[] { 0f }, roles, null, null);
        AssertEqual(0, n, "no one to give");
        AssertEqual(DirectChildRole.Main, roles[0], "the main keeps its role");
        AssertTrue(MainFlankGuard.Assess(Array.Empty<MainFlankGuard.Neighbour>(), false, false).Open, "and the posture producer holds it");
    }

    private static void AnAnchoredSideIsGivenNobody()
    {
        var roles = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        MainFlankGuard.AssignGuards(new[] { 0f, -800f, 400f }, roles, (main, left) => left, null);
        AssertEqual(DirectChildRole.Reserve, roles[1], "the left rests on a river: the reserve stays a reserve");
    }

    private static void TheAnswerIsStable()
    {
        // Same layout, a minute apart, units drifting a few metres: one answer both times.
        for (int minute = 0; minute < 2; minute++)
        {
            float drift = 12f * minute;
            var roles = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
            MainFlankGuard.AssignGuards(new[] { 0f + drift, -900f - drift, 400f + drift }, roles, null, null);
            AssertEqual(DirectChildRole.RefuseLeft, roles[1], "minute " + minute + ": the left reserve is the guard");
            AssertEqual(DirectChildRole.SupportMain, roles[2], "minute " + minute + ": the support stays");
        }
    }

    // ---- the leaf map ----

    private static TacticalFrontAxis FacingPlusZ() => TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1000f).WithOwnExtent(-2000f, 2000f, 5);

    // Two divisions of one brigade each across the front (facing +Z, so the lateral is x): A at x = 0, B at x = bx.
    private static (IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree, int a, int b) TwoDivisions(float bx)
    {
        var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>
        {
            new TacticalCommandTreeProbe.ExtendedProbe(10, 15, "DivA", true, 0, true, 0f, 0f, 2),
            new TacticalCommandTreeProbe.ExtendedProbe(11, 15, "DivB", true, 0, true, bx, 0f, 2),
            new TacticalCommandTreeProbe.ExtendedProbe(100, 14, "BrigA", true, 10, false, 0f, 0f, 2),
            new TacticalCommandTreeProbe.ExtendedProbe(101, 14, "BrigB", true, 11, false, bx, 0f, 2),
        };
        return (TacticalCommandTreeProbe.BuildTree(probes, -1), 10, 11);
    }

    private static void TheLeafMapAssignsTheGuard()
    {
        var (tree, a, b) = TwoDivisions(-1500f);
        var tops = new[] { new TacticalLeafBrigadeMap.TopAssignment(a, DirectChildRole.Main), new TacticalLeafBrigadeMap.TopAssignment(b, DirectChildRole.Reserve) };
        var plain = TacticalLeafBrigadeMap.BuildMap(tree, tops, 0.5f, CascadeEnvelopmentMode.None, FacingPlusZ(), null, defenderReserve: true);
        AssertEqual(DirectChildRole.Reserve, plain[101].LeafRole, "(a Defender's map leaves the reserve a reserve)");

        var report = new List<MainFlankGuard.GuardAssignment>();
        var map = TacticalLeafBrigadeMap.BuildMap(tree, tops, 0.5f, CascadeEnvelopmentMode.None, FacingPlusZ(), null, defenderReserve: false, flankReport: report);
        AssertEqual(DirectChildRole.Main, map[100].LeafRole, "the main stays the main");
        AssertEqual(DirectChildRole.RefuseLeft, map[101].LeafRole, "the reserve on the exposed left is its flank guard");
        AssertEqual(CommandTaskType.GuardFlank, map[101].LeafTask, "tasked to guard the flank");
        AssertEqual(DirectChildRole.RefuseLeft, map[101].CascadeChain[map[101].CascadeChain.Count - 1], "the chain's own entry follows");
        AssertTrue(report.Count == 1 && report[0].Main == 100 && report[0].Guard == 101 && report[0].Left, "the report names the leaves by instance id");

        // A river on the left end: the open side is the right, and that is the side the reserve guards.
        var leftRiver = TacticalLeafBrigadeMap.BuildMap(tree, tops, 0.5f, CascadeEnvelopmentMode.None, FacingPlusZ(), null, false, flankAnchored: (x, z, ox, oz) => ox < 0f);
        AssertEqual(DirectChildRole.RefuseRight, leftRiver[101].LeafRole, "the left end rests on a river: the reserve guards the right flank instead");
        // A river on both ends: nothing is open, nothing is taken from the reserve.
        var rivers = TacticalLeafBrigadeMap.BuildMap(tree, tops, 0.5f, CascadeEnvelopmentMode.None, FacingPlusZ(), null, false, flankAnchored: (x, z, ox, oz) => true);
        AssertEqual(DirectChildRole.Reserve, rivers[101].LeafRole, "both ends rest on rivers: the reserve is left alone");
    }

    private static void TheLeafMapLeavesADefenderAlone()
    {
        var (tree, a, b) = TwoDivisions(-1500f);
        var tops = new[] { new TacticalLeafBrigadeMap.TopAssignment(a, DirectChildRole.Main), new TacticalLeafBrigadeMap.TopAssignment(b, DirectChildRole.Reserve) };
        var report = new List<MainFlankGuard.GuardAssignment>();
        var map = TacticalLeafBrigadeMap.BuildMap(tree, tops, 0.5f, CascadeEnvelopmentMode.None, FacingPlusZ(), null, defenderReserve: true, flankReport: report);
        AssertEqual(0, report.Count, "a Defender's main advances nowhere: no guard is taken from its reserve");
        AssertEqual(DirectChildRole.Reserve, map[101].LeafRole, "its reserve stays");
    }

    // ---- wiring ----

    private static void ThePostureProducerHolds()
    {
        string producer = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/Producers/PostureIntentProducer.cs");
        int at = producer.IndexOf("private static void ProduceLeaf(", StringComparison.Ordinal);
        AssertTrue(at >= 0, "ProduceLeaf exists");
        string body = producer.Substring(at, 6500);
        FlankResponseTests.AssertPinned(body, "FlankHolding(s, i, leafMap", "the leaf pass asks whether the main's flanks are covered");
        FlankResponseTests.AssertPinned(body, "task = CommandTaskType.HoldObjective;", "an open flank holds the main in place");
        FlankResponseTests.AssertPinned(producer, "MainFlankGuard.ChangeReason(previous, next, cover)", "one row per change of the hold, advance included");
        FlankResponseTests.AssertPinned(body, "army.CurrentBattleStance != BattleStance.Defender", "a Defender's main advances nowhere and is not gated");
        FlankResponseTests.AssertPinned(producer, "return _flankHold.TryGetValue(groupId, out MainFlankGuard.HoldState hold) && hold.Holding", "a main held on its flank is no exploit asset");
        string army = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(army, "flankAnchored: FlankAnchoredProbe()", "the leaf map is built with the terrain picture's anchored ends");
        FlankResponseTests.AssertPinned(army, "!(builtAgo >= LeafMapRefreshHours)", "the cached map is rebuilt every few doctrine minutes: the guards read where the brigades stand");
        FlankResponseTests.AssertPinned(army, "includeCrest: false", "a crest is cover, not an impassable end");
    }
}
