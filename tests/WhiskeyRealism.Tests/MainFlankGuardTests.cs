using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17: the Union's lone main-effort brigade (2nd Brigade, 5th Massachusetts) advanced with no neighbour and no flank guard and was shot in the
// flank. A main effort advances only when each flank is covered: a friendly line unit abreast of it (a frontage plus a gap sideways, within the depth reach
// forward or back of where the main stands now) or a river at the end. Otherwise it holds, and the leaf map's overlay sends a guard from the reserve or the
// support pool to a post at the main's flank (a real Move); a guard moving there is in transit and the hold goes on until it is in reach. The hold is bounded
// at 20 doctrine minutes and the advance it ends in is cleared only by a flank that stays covered (MainFlankGuard, FlankGuardLeaf).
internal static class MainFlankGuardTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("flank guard a main with line units abreast on both sides is covered", AMainWithNeighboursIsCovered),
            ("flank guard a lone main is open on both flanks and holds", ALoneMainIsOpen),
            ("flank guard only the side with no neighbour in reach is open", OnlyTheBareSideIsOpen),
            ("flank guard a unit past a frontage and a gap is no neighbour", AUnitPastTheGapIsNoNeighbour),
            ("flank guard a reserve, a fallback guard and a unit behind the main cover nothing", HeldBackUnitsCoverNothing),
            ("flank guard a main that has advanced ahead of its line is not covered by it", AMainAheadOfItsLineIsNotCovered),
            ("flank guard a guard moving to its post is in transit and one that is not is no guard", AGuardMovingToItsPostIsInTransit),
            ("flank guard a guard in reach covers the side", AGuardInReachCovers),
            ("flank guard an end that rests on a river is not open", ATerrainAnchorCovers),
            ("flank guard the post is a frontage out, refused back, facing outward", ThePostGeometry),
            ("flank guard the hold starts at once and ends only after its minimum and a covered spell", TheHoldHasHysteresis),
            ("flank guard an open flank during the covered spell restarts it", AnOpenFlankRestartsTheCount),
            ("flank guard with nobody to give the main advances once the bound passes", NobodyToGiveAdvancesAtTheBound),
            ("flank guard a guard in transit keeps the hold until it arrives or the same bound passes", InTransitHoldsUntilArrivalOrTheBound),
            ("flank guard the advance holds while the flank stays open and clears only after a spell of cover", TheAdvanceClearsOnlyAfterASpellOfCover),
            ("flank guard a one-look flicker every 21 minutes does not hold the main again", AFlickerDoesNotRearmTheHold),
            ("flank guard the bound is on the doctrine clock and sits between the arrival hold and the plan commitment", TheBoundIsOnTheDoctrineClock),
            ("flank guard each change of the hold writes one row with its reason", EachChangeHasOneReason),
            ("flank guard the exposed side is given the nearest reserve on that side", TheNearestReserveOnTheSideIsGiven),
            ("flank guard a cascade flank guard standing out on the exposed side is the next candidate", ACascadeFlankGuardIsTheNext),
            ("flank guard a side with no reserve takes the nearest reserve of the other side", TheOtherSidesReserveIsTheNext),
            ("flank guard with no reserve the support pool gives one, and only one that leaves its own side covered", TheSupportPoolGivesOne),
            ("flank guard nobody to give leaves the flank open", NobodyToGiveLeavesItOpen),
            ("flank guard a terrain anchored side is given nobody", AnAnchoredSideIsGivenNobody),
            ("flank guard a line unit ahead of the main does not stop the guard being given", AUnitAheadDoesNotCover),
            ("flank guard a unit that holds a post keeps it however the geometry moves", AnIncumbentKeepsItsPost),
            ("flank guard an incumbent whose main is no longer Main is released", AnIncumbentOfAGoneMainIsReleased),
            ("flank guard the same enemy and odds one minute apart give one answer", TheAnswerIsStable),
            ("flank guard the leaf map overlay posts the reserve and leaves the cascade's roles alone", TheOverlayPostsTheGuard),
            ("flank guard the overlay keeps a post across rebuilds and leaves a Defender alone", TheOverlayIsStickyAndSparesADefender),
            ("flank guard the overlay changes only the units it posts, whatever the geometry does between rebuilds", TheOverlayLeavesTheCascadeAlone),
            ("flank guard the works hold guard forwards a leaf's flank post", TheWorksGuardForwardsThePost),
            ("flank guard the main holds on a snapshot with a lone brigade and goes when a guard is in reach", TheMainHoldsOnASnapshot),
            ("flank guard a guard in transit keeps the main holding and one that is not moving does not", TheSnapshotSeesTransit),
            ("flank guard the guard is sent to the post by a Move and not again while its order leads there", TheGuardIsSentToThePost),
            ("flank guard the guard pass hands back a unit with no post and one in contact", TheGuardPassHandsBack),
            ("flank guard a guard whose group or formation withdraws holds no claim and takes no move", AWithdrawingFormationsGuardHoldsNothing),
            ("flank guard a stopped unit whose last waypoint is the post is sent again and is not in transit", AStoppedUnitIsResentAndNotInTransit),
            ("flank guard the post is right on a rotated axis", ThePostOnARotatedAxis),
            ("flank guard the posture producer calls the guard and the hold and the exploit producer sees it held", ThePostureProducerWiring),
        });
    }

    private static MainFlankGuard.Neighbour N(float lateral, DirectChildRole role, float depth = 0f, int postSide = 0)
        => new MainFlankGuard.Neighbour(lateral, depth, role, postSide);

    private static void AMainWithNeighboursIsCovered()
    {
        var cover = MainFlankGuard.Assess(new[] { N(-600f, DirectChildRole.SupportMain), N(650f, DirectChildRole.Fix) }, false, false);
        AssertTrue(!cover.Open, "covered: " + cover.Reason);
        AssertEqual("flanks-covered", cover.Reason, "reason");
    }

    private static void ALoneMainIsOpen()
    {
        var cover = MainFlankGuard.Assess(Array.Empty<MainFlankGuard.Neighbour>(), false, false);
        AssertTrue(cover.LeftOpen && cover.RightOpen && cover.Open && cover.Unguarded, "both open, nobody on the way");
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
        AssertTrue(past.LeftExposed && past.RightExposed && past.Open, "past it, both flanks are open");
    }

    private static void HeldBackUnitsCoverNothing()
    {
        var cover = MainFlankGuard.Assess(new[]
        {
            N(-400f, DirectChildRole.Reserve), N(400f, DirectChildRole.Fallback), N(50f, DirectChildRole.SupportMain), N(-30f, DirectChildRole.Fix),
        }, false, false);
        AssertTrue(cover.LeftOpen && cover.RightOpen, "a reserve, a fallback guard and units stacked behind the main are no flank cover");
    }

    // The cover is measured against where the main stands now: the line it left 800 m behind is not beside it.
    private static void AMainAheadOfItsLineIsNotCovered()
    {
        var behind = MainFlankGuard.Assess(new[]
        {
            N(-500f, DirectChildRole.SupportMain, depth: -800f), N(500f, DirectChildRole.SupportMain, depth: -800f),
        }, false, false);
        AssertTrue(behind.LeftOpen && behind.RightOpen, "supports 800 m back are not abreast");
        var ahead = MainFlankGuard.Assess(new[] { N(-500f, DirectChildRole.Fix, depth: 600f), N(500f, DirectChildRole.Fix, depth: -401f) }, false, false);
        AssertTrue(ahead.LeftOpen && ahead.RightOpen, "nor are units past the depth reach ahead or behind");
        var abreast = MainFlankGuard.Assess(new[] { N(-500f, DirectChildRole.Fix, depth: 399f), N(500f, DirectChildRole.Fix, depth: -399f) }, false, false);
        AssertTrue(!abreast.Open, "within it they are");
    }

    private static void AGuardMovingToItsPostIsInTransit()
    {
        var transit = MainFlankGuard.Assess(new[] { N(-1500f, DirectChildRole.RefuseLeft, postSide: -1), N(500f, DirectChildRole.SupportMain) }, false, false);
        AssertTrue(transit.LeftGuarded && transit.LeftOpen && transit.InTransit && !transit.Unguarded, "moving to its post, it is in transit: the main keeps holding");
        AssertEqual(MainFlankGuard.InTransitReason, transit.HoldWhy, "the reason says a guard is on its way");
        var idle = MainFlankGuard.Assess(new[] { N(-1500f, DirectChildRole.RefuseLeft), N(500f, DirectChildRole.SupportMain) }, false, false);
        AssertTrue(idle.Unguarded && !idle.InTransit && !idle.LeftGuarded, "a guard that is not moving to its post is no guard: relabelling covers nothing");
        AssertEqual(MainFlankGuard.HoldReason, idle.HoldWhy, "the reason says the flank is unguarded");
    }

    private static void AGuardInReachCovers()
    {
        var arrived = MainFlankGuard.Assess(new[] { N(-450f, DirectChildRole.RefuseLeft, depth: -150f, postSide: -1), N(650f, DirectChildRole.SupportMain) }, false, false);
        AssertTrue(!arrived.Open, "a guard at its post (a frontage out, refused back) covers its side");
    }

    private static void ATerrainAnchorCovers()
    {
        var cover = MainFlankGuard.Assess(new[] { N(500f, DirectChildRole.SupportMain) }, leftAnchored: true, rightAnchored: false);
        AssertTrue(!cover.Open && cover.LeftAnchored, "the left end rests on a river: not open");
    }

    private static void ThePostGeometry()
    {
        var axis = TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1000f); // facing +Z, right is +X
        MainFlankGuard.PostOf(in axis, 100f, 200f, left: false, out float rx, out float rz, out float rh);
        AssertTrue(Math.Abs(rx - 550f) < 0.5f && Math.Abs(rz - 50f) < 0.5f, "right post: a frontage out (450 m) and 150 m back: " + rx + "," + rz);
        AssertTrue(Math.Abs(rh - 45f) < 0.5f, "facing outward, 45 degrees toward the right: " + rh);
        MainFlankGuard.PostOf(in axis, 100f, 200f, left: true, out float lx, out float lz, out float lh);
        AssertTrue(Math.Abs(lx - -350f) < 0.5f && Math.Abs(lz - 50f) < 0.5f, "left post: " + lx + "," + lz);
        AssertTrue(Math.Abs(lh - 315f) < 0.5f, "facing outward, 45 degrees toward the left: " + lh);
        AssertTrue(MainFlankGuard.PostLateralMeters <= MainFlankGuard.ReachMeters, "a guard at its post is within the reach that covers the side");
        AssertTrue(MainFlankGuard.PostRefusedBackMeters <= MainFlankGuard.DepthReachMeters, "and within the depth reach");
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

    // One look every 30 battle seconds for the given minutes, open or covered throughout.
    private static MainFlankGuard.HoldState Hold(float fromHours, float minutes, bool open = true)
    {
        var state = MainFlankGuard.Step(default(MainFlankGuard.HoldState), open, fromHours);
        for (float m = 0.5f; m <= minutes + 0.001f; m += 0.5f) state = MainFlankGuard.Step(state, open, fromHours + m * Minute);
        return state;
    }

    private static void NobodyToGiveAdvancesAtTheBound()
    {
        var bare = MainFlankGuard.Assess(Array.Empty<MainFlankGuard.Neighbour>(), false, false);
        AssertTrue(bare.Unguarded, "nobody is on the way to the flank");
        AssertTrue(Hold(5f, 19.5f).Holding, "after 19.5 minutes the main still holds");
        var past = Hold(5f, 20.5f);
        AssertTrue(!past.Holding && past.Advanced, "after 20 the main advances anyway");
        AssertEqual(MainFlankGuard.AdvanceReason, MainFlankGuard.ChangeReason(Hold(5f, 19.5f), past, bare), "with the row reason flank-unguarded-advance");
        AssertEqual("flank-unguarded-advance", MainFlankGuard.AdvanceReason, "the reason's text");
    }

    private static void InTransitHoldsUntilArrivalOrTheBound()
    {
        var transit = MainFlankGuard.Assess(new[] { N(-1500f, DirectChildRole.RefuseLeft, postSide: -1), N(500f, DirectChildRole.SupportMain) }, false, false);
        AssertTrue(transit.InTransit, "a guard is on its way");
        var state = Hold(5f, 11.5f);
        AssertTrue(state.Holding, "holding in transit");
        float arrived = 5f + 12f * Minute;
        state = MainFlankGuard.Step(state, false, arrived);
        state = MainFlankGuard.Step(state, false, arrived + 1.1f * Minute);
        AssertTrue(!state.Holding && !state.Advanced, "the guard arrived: released by cover, not by the bound");
        AssertEqual(MainFlankGuard.ReleaseReason, MainFlankGuard.ChangeReason(Hold(5f, 11.5f), state, transit), "the release row says covered");
        var stuck = Hold(5f, 20.5f);
        AssertTrue(!stuck.Holding && stuck.Advanced, "a guard that does not arrive is waited for no longer than the bound");
    }

    private static void TheAdvanceClearsOnlyAfterASpellOfCover()
    {
        var advanced = Hold(5f, 20.5f);
        float t = 5f + 21f * Minute;
        var next = MainFlankGuard.Step(advanced, true, t);
        AssertTrue(!next.Holding && next.Advanced, "still open: the main is not stopped again on the next look");
        next = MainFlankGuard.Step(next, true, t + 10f * Minute);
        AssertTrue(!next.Holding && next.Advanced, "ten minutes on, still advancing");
        next = MainFlankGuard.Step(next, false, t + 11f * Minute);
        AssertTrue(!next.Holding && next.Advanced, "one covered look does not clear the advance");
        next = MainFlankGuard.Step(next, false, t + 15f * Minute);
        AssertTrue(next.Advanced, "four minutes covered: still advanced");
        next = MainFlankGuard.Step(next, false, t + 16.1f * Minute);
        AssertTrue(!next.Holding && !next.Advanced, "five minutes covered: cleared");
        next = MainFlankGuard.Step(next, true, t + 17f * Minute);
        AssertTrue(next.Holding, "a flank that opens after that starts a new hold");
        AssertTrue(MainFlankGuard.Step(next, true, t + 17f * Minute + 19f * Minute).Holding, "with its own full bound");
    }

    // A guard passing in and out of reach: one covered look every 21 minutes over three hours, open on every other look.
    private static void AFlickerDoesNotRearmTheHold()
    {
        var state = default(MainFlankGuard.HoldState);
        int heldLooks = 0, looks = 0;
        for (float m = 0f; m <= 180f; m += 0.5f)
        {
            bool flicker = Math.Abs(m % 21f) < 0.01f && m > 0f;
            state = MainFlankGuard.Step(state, open: !flicker, 5f + m * Minute);
            looks++;
            if (state.Holding) heldLooks++;
        }
        AssertTrue(heldLooks <= 41, "the main is held for its first 20 minutes and no more: " + heldLooks + " of " + looks + " looks held");
        AssertTrue(state.Advanced, "and after three hours of the flicker it is still advancing");
    }

    private static void TheBoundIsOnTheDoctrineClock()
    {
        AssertEqual(1200f, MainFlankGuard.MaxHoldBattleSeconds, "twenty battle-minutes");
        AssertTrue(MainFlankGuard.MaxHoldBattleSeconds > MainArrivalHoldLatch.MaxHoldBattleSeconds, "longer than the arrival hold's budget");
        AssertTrue(MainFlankGuard.MaxHoldBattleSeconds <= ArmyReplanTriggers.PlanCommitmentBattleSeconds / 3f, "a third of the plan commitment hold at most: a hold never outlives its plan");
        AssertTrue(MainFlankGuard.AdvancedClearCoveredBattleSeconds < MainFlankGuard.MaxHoldBattleSeconds, "the spell of cover that clears an advance is shorter than a hold");
        var start = MainFlankGuard.Step(default(MainFlankGuard.HoldState), true, 2.0f);
        AssertTrue(MainFlankGuard.Step(start, true, 2.30f).Holding, "0.30 doctrine hours into the hold: holding");
        AssertTrue(MainFlankGuard.Step(start, true, 2.34f).Advanced, "0.34 doctrine hours into the hold: advanced");
        AssertTrue(MainFlankGuard.Step(start, true, 2.0f + 1190f / 3600f).Holding, "1190 battle seconds: still holding");
        AssertTrue(MainFlankGuard.Step(start, true, 2.0f + 1210f / 3600f).Advanced, "1210 battle seconds: advanced");
    }

    private static void EachChangeHasOneReason()
    {
        var unguarded = MainFlankGuard.Assess(Array.Empty<MainFlankGuard.Neighbour>(), false, false);
        var transit = MainFlankGuard.Assess(new[] { N(-1500f, DirectChildRole.RefuseLeft, postSide: -1), N(500f, DirectChildRole.SupportMain) }, false, false);
        var none = default(MainFlankGuard.HoldState);
        var held = MainFlankGuard.Step(none, true, 5f);
        AssertEqual(MainFlankGuard.HoldReason, MainFlankGuard.ChangeReason(none, held, unguarded), "starting with nobody on the way");
        AssertEqual(MainFlankGuard.InTransitReason, MainFlankGuard.ChangeReason(none, held, transit), "starting with a guard on its way");
        AssertTrue(MainFlankGuard.ChangeReason(held, MainFlankGuard.Step(held, true, 5f + Minute), unguarded) == null, "a hold that goes on writes nothing");
        AssertTrue(MainFlankGuard.ChangeReason(none, none, unguarded) == null, "no hold, no change: nothing");
    }

    // ---- the allocation ----

    private static DirectChildRole[] Roles(params DirectChildRole[] roles) => roles;
    private static int[] Ids(int n) { var ids = new int[n]; for (int i = 0; i < n; i++) ids[i] = 100 + i; return ids; }
    private static float[] Zeros(int n) => new float[n];

    private static int Assign(float[] lateral, DirectChildRole[] roles, List<MainFlankGuard.GuardAssignment> report = null, float[] depth = null,
        Func<int, bool, bool> anchored = null, IReadOnlyDictionary<int, MainFlankGuard.Post> incumbents = null)
        => MainFlankGuard.AssignGuards(Ids(roles.Length), lateral, depth ?? Zeros(roles.Length), roles, anchored, incumbents, report);

    private static void TheNearestReserveOnTheSideIsGiven()
    {
        var roles = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.Reserve, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        var report = new List<MainFlankGuard.GuardAssignment>();
        int n = Assign(new[] { 0f, -300f, -1200f, -400f, 500f }, roles, report);
        AssertEqual(1, n, "one guard given");
        AssertEqual(DirectChildRole.RefuseLeft, roles[1], "the nearest left reserve guards the left flank");
        AssertEqual(DirectChildRole.Reserve, roles[2], "the other left reserve stays");
        AssertEqual(DirectChildRole.Reserve, roles[3], "the right reserve stays");
        AssertTrue(report.Count == 1 && report[0].Left && report[0].Main == 0 && report[0].Guard == 1 && report[0].From == DirectChildRole.Reserve && !report[0].Kept,
            "the report names the main, the guard, the side and the pool");
    }

    private static void ACascadeFlankGuardIsTheNext()
    {
        // the cascade's own RefuseLeft stands 1800 m out on the left: it is not abreast, so it is the left flank's guard now, ahead of a reserve on the right.
        var roles = Roles(DirectChildRole.Main, DirectChildRole.RefuseLeft, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        var report = new List<MainFlankGuard.GuardAssignment>();
        Assign(new[] { 0f, -1800f, 900f, 450f }, roles, report);
        AssertEqual(DirectChildRole.RefuseLeft, roles[1], "it stays a flank guard");
        AssertEqual(DirectChildRole.Reserve, roles[2], "the reserve is not taken");
        AssertTrue(report.Count == 1 && report[0].Guard == 1 && report[0].From == DirectChildRole.RefuseLeft, "and is posted: " + report.Count);
    }

    private static void TheOtherSidesReserveIsTheNext()
    {
        var roles = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        Assign(new[] { 0f, 700f, -500f }, roles);
        AssertEqual(DirectChildRole.RefuseRight, roles[1], "the right reserve guards the right flank (the left is covered by the support)");

        var roles2 = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        Assign(new[] { 0f, -900f, 400f }, roles2);
        AssertEqual(DirectChildRole.RefuseLeft, roles2[1], "the left reserve guards the left flank (the right is covered by the support)");

        var roles3 = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        int n3 = Assign(new[] { 0f, 1000f, 400f }, roles3);
        AssertEqual(1, n3, "the left is bare and the only reserve stands to the right: it takes the left");
        AssertEqual(DirectChildRole.RefuseLeft, roles3[1], "a RefuseLeft guard, wherever it stands");
    }

    private static void TheSupportPoolGivesOne()
    {
        var roles = Roles(DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.SupportMain);
        var report = new List<MainFlankGuard.GuardAssignment>();
        Assign(new[] { 0f, 500f, 1400f }, roles, report);
        AssertEqual(DirectChildRole.SupportMain, roles[1], "the support in reach stays");
        AssertEqual(DirectChildRole.RefuseLeft, roles[2], "the far support guards the bare left");
        AssertEqual(DirectChildRole.SupportMain, report[0].From, "from the support pool");

        var alone = Roles(DirectChildRole.Main, DirectChildRole.SupportMain);
        Assign(new[] { 0f, 500f }, alone);
        AssertEqual(DirectChildRole.SupportMain, alone[1], "a lone support is not taken from the side it covers");
    }

    private static void NobodyToGiveLeavesItOpen()
    {
        var roles = Roles(DirectChildRole.Main);
        int n = Assign(new[] { 0f }, roles);
        AssertEqual(0, n, "no one to give");
        AssertEqual(DirectChildRole.Main, roles[0], "the main keeps its role");
        AssertTrue(MainFlankGuard.Assess(Array.Empty<MainFlankGuard.Neighbour>(), false, false).Open, "and the posture producer holds it");
    }

    private static void AnAnchoredSideIsGivenNobody()
    {
        var roles = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
        Assign(new[] { 0f, -800f, 400f }, roles, anchored: (main, left) => left);
        AssertEqual(DirectChildRole.Reserve, roles[1], "the left rests on a river: the reserve stays a reserve");
    }

    // The line unit 800 m ahead of the main is beside it sideways but not abreast: the flank is still open and gets its guard.
    private static void AUnitAheadDoesNotCover()
    {
        var roles = Roles(DirectChildRole.Main, DirectChildRole.Fix, DirectChildRole.Reserve, DirectChildRole.Fix);
        var report = new List<MainFlankGuard.GuardAssignment>();
        Assign(new[] { 0f, -500f, -1200f, 500f }, roles, report, depth: new[] { 0f, 800f, 0f, 0f });
        AssertEqual(DirectChildRole.RefuseLeft, roles[2], "the left reserve is posted, the unit ahead being no cover");
        var abreast = Roles(DirectChildRole.Main, DirectChildRole.Fix, DirectChildRole.Reserve, DirectChildRole.Fix);
        Assign(new[] { 0f, -500f, -1200f, 500f }, abreast, depth: new[] { 0f, 100f, 0f, 0f });
        AssertEqual(DirectChildRole.Reserve, abreast[2], "abreast it does cover, and the reserve stays");
    }

    private static void AnIncumbentKeepsItsPost()
    {
        // the guard (103) has arrived and is now the left cover; a fresh look with the geometry moved would not need a guard, but the post is kept
        var roles = Roles(DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.Reserve);
        var incumbents = new Dictionary<int, MainFlankGuard.Post> { { 102, new MainFlankGuard.Post(100, true) } };
        var report = new List<MainFlankGuard.GuardAssignment>();
        Assign(new[] { 0f, 500f, -450f }, roles, report, incumbents: incumbents);
        AssertEqual(DirectChildRole.RefuseLeft, roles[2], "the incumbent stays the left flank guard");
        AssertTrue(report.Count == 1 && report[0].Kept, "reported as kept");
        // and it is not given a second post
        var again = new List<MainFlankGuard.GuardAssignment>();
        Assign(new[] { 0f, 500f, -450f }, Roles(DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.Reserve), again, incumbents: incumbents);
        AssertEqual(1, again.Count, "one assignment");
    }

    private static void AnIncumbentOfAGoneMainIsReleased()
    {
        var roles = Roles(DirectChildRole.Fix, DirectChildRole.SupportMain, DirectChildRole.Reserve);
        var incumbents = new Dictionary<int, MainFlankGuard.Post> { { 102, new MainFlankGuard.Post(100, true) } };
        int n = Assign(new[] { 0f, 500f, -450f }, roles, incumbents: incumbents);
        AssertEqual(0, n, "the main is no longer Main: no post");
        AssertEqual(DirectChildRole.Reserve, roles[2], "the unit is a reserve again");
        var fallenBack = Roles(DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.Fallback);
        Assign(new[] { 0f, 500f, -450f }, fallenBack, incumbents: incumbents);
        AssertEqual(DirectChildRole.Fallback, fallenBack[2], "a unit that has fallen back is not posted");
    }

    private static void TheAnswerIsStable()
    {
        for (int minute = 0; minute < 2; minute++)
        {
            float drift = 12f * minute;
            var roles = Roles(DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain);
            Assign(new[] { 0f + drift, -900f - drift, 400f + drift }, roles);
            AssertEqual(DirectChildRole.RefuseLeft, roles[1], "minute " + minute + ": the left reserve is the guard");
            AssertEqual(DirectChildRole.SupportMain, roles[2], "minute " + minute + ": the support stays");
        }
    }

    // ---- the leaf map overlay ----

    private static TacticalFrontAxis FacingPlusZ() => TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1000f).WithOwnExtent(-2000f, 2000f, 5);

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

    private static IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> Cascade(IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree, int a, int b)
        => TacticalLeafBrigadeMap.BuildMap(tree, new[]
        {
            new TacticalLeafBrigadeMap.TopAssignment(a, DirectChildRole.Main), new TacticalLeafBrigadeMap.TopAssignment(b, DirectChildRole.Reserve),
        }, 0.5f, CascadeEnvelopmentMode.None, FacingPlusZ());

    private static void TheOverlayPostsTheGuard()
    {
        var (tree, a, b) = TwoDivisions(-1500f);
        var cascade = Cascade(tree, a, b);
        AssertEqual(DirectChildRole.Reserve, cascade[101].LeafRole, "the cascade alone leaves the reserve a reserve");
        AssertTrue(!cascade[101].HasFlankPost, "with no post");

        var report = new List<MainFlankGuard.GuardAssignment>();
        var map = TacticalLeafBrigadeMap.ApplyFlankGuards(cascade, tree, FacingPlusZ(), defender: false, flankAnchored: null, incumbents: null, report);
        AssertEqual(DirectChildRole.Main, map[100].LeafRole, "the main stays the main");
        AssertEqual(DirectChildRole.RefuseLeft, map[101].LeafRole, "the reserve on the exposed left is its flank guard");
        AssertEqual(CommandTaskType.GuardFlank, map[101].LeafTask, "tasked to guard the flank");
        AssertEqual(DirectChildRole.RefuseLeft, map[101].CascadeChain[map[101].CascadeChain.Count - 1], "the chain's own entry follows");
        AssertTrue(map[101].HasFlankPost && map[101].FlankPostMain == 100 && map[101].FlankPostLeft, "and it holds the left post of the main 100");
        AssertTrue(report.Count == 1 && report[0].Main == 100 && report[0].Guard == 101 && report[0].Left, "the report names the leaves by instance id");
        AssertEqual(DirectChildRole.Reserve, cascade[101].LeafRole, "the cascade map itself is not changed");

        var leftRiver = TacticalLeafBrigadeMap.ApplyFlankGuards(cascade, tree, FacingPlusZ(), false, (x, z, ox, oz) => ox < 0f, null, null);
        AssertEqual(DirectChildRole.RefuseRight, leftRiver[101].LeafRole, "the left end rests on a river: the reserve guards the right flank instead");
        var rivers = TacticalLeafBrigadeMap.ApplyFlankGuards(cascade, tree, FacingPlusZ(), false, (x, z, ox, oz) => true, null, null);
        AssertEqual(DirectChildRole.Reserve, rivers[101].LeafRole, "both ends rest on rivers: the reserve is left alone");
        AssertTrue(ReferenceEquals(rivers, cascade), "and the map is the cascade itself");
    }

    private static void TheOverlayIsStickyAndSparesADefender()
    {
        var (tree, a, b) = TwoDivisions(-1500f);
        var cascade = Cascade(tree, a, b);
        var first = TacticalLeafBrigadeMap.ApplyFlankGuards(cascade, tree, FacingPlusZ(), false, null, null, null);
        var incumbents = new Dictionary<int, MainFlankGuard.Post> { { 101, new MainFlankGuard.Post(first[101].FlankPostMain, first[101].FlankPostLeft) } };

        // five minutes on the guard stands beside the main: no longer needed by the geometry, kept by its post
        var (moved, a2, b2) = TwoDivisions(-450f);
        var cascade2 = Cascade(moved, a2, b2);
        var fresh = TacticalLeafBrigadeMap.ApplyFlankGuards(cascade2, moved, FacingPlusZ(), false, null, null, null);
        var kept = TacticalLeafBrigadeMap.ApplyFlankGuards(cascade2, moved, FacingPlusZ(), false, null, incumbents, null);
        AssertEqual(DirectChildRole.RefuseLeft, fresh[101].LeafRole, "(a fresh look posts the cascade's reserve on the left again)");
        AssertEqual(DirectChildRole.RefuseLeft, kept[101].LeafRole, "the incumbent keeps its left post");
        AssertEqual(100, kept[101].FlankPostMain, "for the same main");
        for (int rebuild = 0; rebuild < 5; rebuild++)
        {
            var again = TacticalLeafBrigadeMap.ApplyFlankGuards(cascade2, moved, FacingPlusZ(), false, null, incumbents, null);
            AssertEqual(DirectChildRole.RefuseLeft, again[101].LeafRole, "rebuild " + rebuild + ": the same role");
        }
        AssertEqual(DirectChildRole.Main, cascade2[100].LeafRole, "the cascade's roles never moved");

        var defender = TacticalLeafBrigadeMap.ApplyFlankGuards(cascade, tree, FacingPlusZ(), defender: true, null, null, null);
        AssertTrue(ReferenceEquals(defender, cascade), "a Defender's main advances nowhere: the map is the cascade");
    }

    // Every five doctrine minutes the overlay is laid again over the same cached cascade: a division of six brigades wandering about between the looks
    // keeps every role the cascade gave it except the units that hold a post.
    private static void TheOverlayLeavesTheCascadeAlone()
    {
        var rng = new Random(7);
        float[] x0 = { -1250f, -750f, -250f, 250f, 750f, 1250f };
        TacticalCommandTreeProbe.ExtendedProbe[] Probes(float[] xs, float[] zs)
        {
            var probes = new List<TacticalCommandTreeProbe.ExtendedProbe> { new TacticalCommandTreeProbe.ExtendedProbe(10, 15, "Div", true, 0, true, 0f, 0f, 2) };
            for (int b = 0; b < xs.Length; b++) probes.Add(new TacticalCommandTreeProbe.ExtendedProbe(100 + b, 14, "B" + b, true, 10, false, xs[b], zs[b], 2));
            return probes.ToArray();
        }
        var zeros = new float[6];
        var tree0 = TacticalCommandTreeProbe.BuildTree(Probes(x0, zeros), -1);
        var cascade = TacticalLeafBrigadeMap.BuildMap(tree0, new[] { new TacticalLeafBrigadeMap.TopAssignment(10, DirectChildRole.Main) }, 0.5f,
            CascadeEnvelopmentMode.None, FacingPlusZ());
        var posts = new Dictionary<int, MainFlankGuard.Post>();
        for (int look = 0; look < 25; look++)
        {
            var xs = new float[6];
            var zs = new float[6];
            for (int b = 0; b < 6; b++)
            {
                xs[b] = x0[b] + (float)(rng.NextDouble() * 1200 - 600);
                zs[b] = (float)(rng.NextDouble() * 1200 - 600);
            }
            var tree = TacticalCommandTreeProbe.BuildTree(Probes(xs, zs), -1);
            var overlay = TacticalLeafBrigadeMap.ApplyFlankGuards(cascade, tree, FacingPlusZ(), false, null, posts, null);
            foreach (var pair in overlay)
                if (!pair.Value.HasFlankPost)
                    AssertEqual(cascade[pair.Key].LeafRole, pair.Value.LeafRole, "look " + look + ": " + pair.Key + " keeps the cascade's role");
            posts.Clear();
            foreach (var pair in overlay)
                if (pair.Value.HasFlankPost) posts[pair.Key] = new MainFlankGuard.Post(pair.Value.FlankPostMain, pair.Value.FlankPostLeft);
        }
    }

    private static void TheWorksGuardForwardsThePost()
    {
        var leaf = new TacticalLeafBrigadeMap.LeafAssignment(101, "B", DirectChildRole.Main, CommandTaskType.AttackObjective, new[] { DirectChildRole.Main }, new[] { "B" }, 0,
            worksHeld: false, flankPostMain: 100, flankPostLeft: true);
        var context = new WorksGuardContext { WorksHold = true, FacesWorks = true };
        AssertTrue(WorksHoldGuard.ApplyLeaf(in leaf, in context, out TacticalLeafBrigadeMap.LeafAssignment guarded), "the works guard turns the attack into a hold");
        AssertEqual(CommandTaskType.HoldObjective, guarded.LeafTask, "a hold");
        AssertTrue(guarded.WorksHeld && guarded.FlankPostMain == 100 && guarded.FlankPostLeft, "and the leaf keeps its post");
    }

    // ---- the posture pass over a snapshot (FlankGuardLeaf, what ProduceLeaf calls) ----

    private static BattleSnapshot Field(double hours, params (int id, float x, float z)[] brigades)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, hours);
        foreach (var b in brigades)
            s.AddOwn(new UnitRecord
            {
                InstanceId = b.id, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = b.x, Z = b.z,
                Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup | UnitFlags.IsInfantry,
            });
        s.IndexCommandTree(null);
        s.SetFrontAxis(FacingPlusZ());
        return s;
    }

    private static TacticalLeafBrigadeMap.LeafAssignment Leaf(int id, DirectChildRole role, int postMain = 0, bool postLeft = false)
        => new TacticalLeafBrigadeMap.LeafAssignment(id, "B" + id, role, TacticalRoleCascade.RoleToLeafTask(role), new[] { role }, new[] { "B" + id }, 0,
            worksHeld: false, flankPostMain: postMain, flankPostLeft: postLeft);

    private static Dictionary<int, TacticalLeafBrigadeMap.LeafAssignment> LeafMap(params TacticalLeafBrigadeMap.LeafAssignment[] leaves)
    {
        var map = new Dictionary<int, TacticalLeafBrigadeMap.LeafAssignment>();
        foreach (var l in leaves) map[l.InstanceId] = l;
        return map;
    }

    private static bool Holds(BattleSnapshot s, int mainId, Dictionary<int, TacticalLeafBrigadeMap.LeafAssignment> map, Dictionary<int, MainFlankGuard.HoldState> holds,
        out string reason, out string change)
        => FlankGuardLeaf.MainHolds(s, s.IndexOfOwn(mainId), map, holds, null, new List<MainFlankGuard.Neighbour>(), out _, out change, out reason);

    private static void TheMainHoldsOnASnapshot()
    {
        var holds = new Dictionary<int, MainFlankGuard.HoldState>();
        var lone = Field(5.0, (100, 0f, 0f));
        AssertTrue(Holds(lone, 100, LeafMap(Leaf(100, DirectChildRole.Main)), holds, out string reason, out string change), "a lone Main holds");
        AssertEqual(MainFlankGuard.HoldReason, reason, "nobody on the way");
        AssertEqual(MainFlankGuard.HoldReason, change, "and the start of the hold writes its row");

        // two supports beside it: covered, but the hold lasts its minimum and a minute of cover
        var line = Field(5.0 + 0.5 * Minute, (100, 0f, 0f), (101, -450f, 0f), (102, 450f, 0f));
        var map = LeafMap(Leaf(100, DirectChildRole.Main), Leaf(101, DirectChildRole.SupportMain), Leaf(102, DirectChildRole.SupportMain));
        AssertTrue(Holds(line, 100, map, holds, out _, out _), "covered for an instant: still holding");
        var early = Field(5.0 + 1.5 * Minute, (100, 0f, 0f), (101, -450f, 0f), (102, 450f, 0f));
        AssertTrue(Holds(early, 100, map, holds, out _, out _), "covered a minute but held under two: still holding");
        var released = Field(5.0 + 2.2 * Minute, (100, 0f, 0f), (101, -450f, 0f), (102, 450f, 0f));
        AssertTrue(!Holds(released, 100, map, holds, out _, out string releaseChange), "held two minutes and covered a minute: released");
        AssertEqual(MainFlankGuard.ReleaseReason, releaseChange, "with its row");

        // the same main stood 800 m ahead of the line it was covered by
        var ahead = Field(9.0, (100, 0f, 800f), (101, -450f, 0f), (102, 450f, 0f));
        AssertTrue(Holds(ahead, 100, map, new Dictionary<int, MainFlankGuard.HoldState>(), out _, out _), "ahead of its line it is not covered by it");
    }

    private static void TheSnapshotSeesTransit()
    {
        // guard 101 is the left flank guard of 100; its order leads to the post (-450, -150)
        var s = Field(5.0, (100, 0f, 0f), (101, -1500f, -150f));
        s.Own[s.IndexOfOwn(101)].RegimentPaths = 1;
        s.Own[s.IndexOfOwn(101)].LastWaypointX = -450f;
        s.Own[s.IndexOfOwn(101)].LastWaypointZ = -150f;
        var map = LeafMap(Leaf(100, DirectChildRole.Main), Leaf(101, DirectChildRole.RefuseLeft, postMain: 100, postLeft: true));
        AssertTrue(Holds(s, 100, map, new Dictionary<int, MainFlankGuard.HoldState>(), out string reason, out _), "the main holds while it comes");
        // the right flank is bare too, so the right is unguarded: this is not the transit reason
        AssertEqual(MainFlankGuard.HoldReason, reason, "the right flank has nobody on the way");

        var both = Field(5.0, (100, 0f, 0f), (101, -1500f, -150f), (102, 1500f, -150f));
        foreach (int id in new[] { 101, 102 })
        {
            int at = both.IndexOfOwn(id);
            both.Own[at].RegimentPaths = 1;
            both.Own[at].LastWaypointX = id == 101 ? -450f : 450f;
            both.Own[at].LastWaypointZ = -150f;
        }
        var both2 = LeafMap(Leaf(100, DirectChildRole.Main), Leaf(101, DirectChildRole.RefuseLeft, 100, true), Leaf(102, DirectChildRole.RefuseRight, 100, false));
        AssertTrue(Holds(both, 100, both2, new Dictionary<int, MainFlankGuard.HoldState>(), out string transit, out _), "both guards moving to their posts: holding");
        AssertEqual(MainFlankGuard.InTransitReason, transit, "in transit");

        // the same guards standing still where they are, relabelled: no cover
        var idle = Field(5.0, (100, 0f, 0f), (101, -1500f, -150f), (102, 1500f, -150f));
        AssertTrue(Holds(idle, 100, both2, new Dictionary<int, MainFlankGuard.HoldState>(), out string idleReason, out _), "guards that do not move cover nothing");
        AssertEqual(MainFlankGuard.HoldReason, idleReason, "unguarded");

        // arrived at the posts: abreast, covered. The hold the guards' march started ends a minute after they stand there, not before it has lasted two.
        var holds = new Dictionary<int, MainFlankGuard.HoldState>();
        AssertTrue(Holds(both, 100, both2, holds, out _, out _), "in transit the hold is on");
        var arrived = Field(5.0 + 1.0 * Minute, (100, 0f, 0f), (101, -450f, -150f), (102, 450f, -150f));
        AssertTrue(Holds(arrived, 100, both2, holds, out _, out _), "the guards are at their posts: covered, but held under two minutes");
        var done = Field(5.0 + 2.2 * Minute, (100, 0f, 0f), (101, -450f, -150f), (102, 450f, -150f));
        AssertTrue(!Holds(done, 100, both2, holds, out _, out string released), "with the guards at their posts the main goes");
        AssertEqual(MainFlankGuard.ReleaseReason, released, "and the row says covered");
        var fresh = Field(5.0, (100, 0f, 0f), (101, -450f, -150f), (102, 450f, -150f));
        AssertTrue(!Holds(fresh, 100, both2, new Dictionary<int, MainFlankGuard.HoldState>(), out _, out _), "a main whose guards already stand at their posts is never held");
    }

    private static void TheGuardIsSentToThePost()
    {
        var s = Field(5.0, (100, 0f, 0f), (101, -1500f, -300f));
        var leaf = Leaf(101, DirectChildRole.RefuseLeft, postMain: 100, postLeft: true);
        AssertEqual(CommandTaskType.GuardFlank, leaf.LeafTask, "its task is GuardFlank, which LeafBrigadeMovePolicy gives no move");
        AssertTrue(!LeafBrigadeMovePolicy.Decide(leaf.LeafTask, false, true, false, false, false).Move, "(the slot path moves nothing for it)");

        var outcome = FlankGuardLeaf.PlanGuard(s, s.IndexOfOwn(101), in leaf, closeEngaged: false, pending: false, withdrawing: false, out BattleOrderIntent intent, out string why);
        AssertEqual(FlankGuardLeaf.GuardOutcome.Move, outcome, "the guard is sent: " + why);
        AssertEqual(OrderChannel.Move, intent.Channel, "by a Move");
        AssertEqual(101, intent.GroupId, "to the guard");
        AssertTrue(Math.Abs(intent.TargetX - -450f) < 0.5f && Math.Abs(intent.TargetZ - -150f) < 0.5f, "to the post at the main's left flank: " + intent.TargetX + "," + intent.TargetZ);
        AssertTrue(Math.Abs(intent.Heading - 315f) < 0.5f, "facing outward: " + intent.Heading);
        AssertEqual(IntentPriority.Post, intent.Priority, "above the posture executor, so its division's own move fans out around it");
        AssertEqual(IntentSource.PostureExecutor, intent.Source, "as the posture executor's");
        AssertEqual(IntentFlags.None | IntentFlags.UseOrderDelay, intent.Flags, "a real order, through the courier delay like the other moves");

        // its order already leads there: not sent again
        int at = s.IndexOfOwn(101);
        s.Own[at].RegimentPaths = 1;
        s.Own[at].LastWaypointX = -440f;
        s.Own[at].LastWaypointZ = -140f;
        AssertEqual(FlankGuardLeaf.GuardOutcome.Handled, FlankGuardLeaf.PlanGuard(s, at, in leaf, false, false, false, out BattleOrderIntent claim, out string toward), "on its way: " + toward);
        AssertEqual("toward-post", toward, "the reason says so");
        AssertTrue((claim.Flags & IntentFlags.StandingClaim) != 0 && claim.Priority == IntentPriority.Post && claim.Channel == OrderChannel.Move,
            "it keeps its claim: a standing Move claim at Post priority, so its division's move fans out around it");

        // the main moved forward 600 m: the post moved, the old order no longer leads to it
        var moved = Field(5.0, (100, 0f, 600f), (101, -1500f, -300f));
        int at2 = moved.IndexOfOwn(101);
        moved.Own[at2].RegimentPaths = 1;
        moved.Own[at2].LastWaypointX = -440f;
        moved.Own[at2].LastWaypointZ = -140f;
        AssertEqual(FlankGuardLeaf.GuardOutcome.Move, FlankGuardLeaf.PlanGuard(moved, at2, in leaf, false, false, false, out BattleOrderIntent follow, out _), "a main that advances draws its guard after it");
        AssertTrue(Math.Abs(follow.TargetZ - 450f) < 0.5f, "to the new post: " + follow.TargetZ);

        var at3 = Field(5.0, (100, 0f, 0f), (101, -440f, -140f));
        AssertEqual(FlankGuardLeaf.GuardOutcome.Handled, FlankGuardLeaf.PlanGuard(at3, at3.IndexOfOwn(101), in leaf, false, false, false, out BattleOrderIntent held, out string atPost), "at its post it stays");
        AssertEqual("at-post", atPost, "the reason says so");
        AssertTrue((held.Flags & IntentFlags.StandingClaim) != 0 && held.Priority == IntentPriority.Post,
            "and holds its claim: a guard at its post submits a standing claim every tick, not nothing");
    }

    private static void TheGuardPassHandsBack()
    {
        var s = Field(5.0, (100, 0f, 0f), (101, -1500f, -300f));
        var leaf = Leaf(101, DirectChildRole.RefuseLeft, postMain: 100, postLeft: true);
        AssertEqual(FlankGuardLeaf.GuardOutcome.Handled, FlankGuardLeaf.PlanGuard(s, s.IndexOfOwn(101), in leaf, closeEngaged: true, false, false, out _, out _), "in contact it is not sent");
        AssertEqual(FlankGuardLeaf.GuardOutcome.Handled, FlankGuardLeaf.PlanGuard(s, s.IndexOfOwn(101), in leaf, false, pending: true, withdrawing: false, out _, out _), "with an order pending it is not sent");
        var gone = Leaf(101, DirectChildRole.RefuseLeft, postMain: 999, postLeft: true);
        AssertEqual(FlankGuardLeaf.GuardOutcome.NoPost, FlankGuardLeaf.PlanGuard(s, s.IndexOfOwn(101), in gone, false, false, false, out _, out _), "a main that is not in the snapshot leaves no post");
        var plain = Leaf(101, DirectChildRole.Reserve);
        AssertEqual(FlankGuardLeaf.GuardOutcome.NoPost, FlankGuardLeaf.PlanGuard(s, s.IndexOfOwn(101), in plain, false, false, false, out _, out _), "a unit with no post runs the normal leaf pass");
        s.Own[s.IndexOfOwn(100)].Flags |= UnitFlags.Routed;
        AssertEqual(FlankGuardLeaf.GuardOutcome.NoPost, FlankGuardLeaf.PlanGuard(s, s.IndexOfOwn(101), in leaf, false, false, false, out _, out _), "a routed main leaves no post");
    }

    private struct WalkHost : IGroupWalkHost
    {
        public CommandTaskType[] Task; public bool[] Has; public bool[] Recent;
        public bool HasPosture(int index) => Has[index];
        public CommandTaskType TaskOf(int index) => Task[index];
        public bool WithdrawalRecent(int index) => Recent[index];
    }

    // Verify round 3: a posted guard's Post claim outranks Support-priority withdrawal moves (WithdrawalIntentProducer.TryPlanStalledFallback), so the guard of a
    // formation that withdraws would stay while the formation walks away. The producer asks the ancestor walk unit churn's column deployment uses, and the
    // guard planner holds no claim and takes no move when it says so.
    private static void AWithdrawingFormationsGuardHoldsNothing()
    {
        // division 10 -> guard 101; the main 100 stands beside
        var s = Field(5.0, (10, 0f, 0f), (100, 0f, 0f), (101, -1500f, -300f));
        int g = s.IndexOfOwn(101);
        s.Own[g].ParentIndex = s.IndexOfOwn(10);
        var leaf = Leaf(101, DirectChildRole.RefuseLeft, postMain: 100, postLeft: true);

        AssertEqual(FlankGuardLeaf.GuardOutcome.Move, FlankGuardLeaf.PlanGuard(s, g, in leaf, false, false, withdrawing: false, out _, out _), "(a formation that does not withdraw: the guard is sent)");
        AssertEqual(FlankGuardLeaf.GuardOutcome.Withdrawing, FlankGuardLeaf.PlanGuard(s, g, in leaf, false, false, withdrawing: true, out BattleOrderIntent none, out string why),
            "its formation withdraws: " + why);
        AssertEqual(0, none.GroupId, "no claim and no move are handed back");

        // what the producer's walk reads: a withdrawal task on the division, or a recent withdrawal move for the division or the guard
        int n = s.OwnCount;
        for (int mode = 0; mode < 3; mode++)
        {
            var host = new WalkHost { Task = new CommandTaskType[n], Has = new bool[n], Recent = new bool[n] };
            int division = s.IndexOfOwn(10);
            if (mode == 0) { host.Has[division] = true; host.Task[division] = CommandTaskType.FallBackToLine; }
            if (mode == 1) host.Recent[division] = true;
            if (mode == 2) host.Recent[g] = true;
            ApproachLine.Walk(s.Own, n, g, ref host, out _, out bool walked);
            AssertTrue(walked, "mode " + mode + ": the walk finds the withdrawal");
        }
        var calm = new WalkHost { Task = new CommandTaskType[n], Has = new bool[n], Recent = new bool[n] };
        calm.Has[s.IndexOfOwn(10)] = true;
        calm.Task[s.IndexOfOwn(10)] = CommandTaskType.AttackObjective;
        ApproachLine.Walk(s.Own, n, g, ref calm, out _, out bool calmWalked);
        AssertFalse(calmWalked, "a division on an attack task is no withdrawal");
        AssertTrue(ColumnDeployment.WithdrawalTask(CommandTaskType.FallBackToLine) && !ColumnDeployment.WithdrawalTask(CommandTaskType.GuardFlank), "and the guard's own task is not one");

        // the producer's walk host and ledger lookup are the ones above
        string producer = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/Producers/PostureIntentProducer.cs");
        string method = FlankResponseTests.Between(producer, "private static bool FormationWithdraws(", "// BR-M1: holds a Main-effort brigade");
        FlankResponseTests.AssertPinned(method, "ApproachLine.Walk(s.Own, s.OwnCount, index, ref host, out _, out bool walked);", "the ancestor walk");
        FlankResponseTests.AssertPinned(method, "ColumnDeployment.WithdrawalTask(state.Task)", "and the ledger's task of every group above");
        FlankResponseTests.AssertPinned(method, "if (ColumnDeployment.WithdrawalTask(task)) return true;", "and the guard's own task");
    }

    // A unit whose order ended (no path, no queued order, not moving) with its last waypoint still the post is not on its way: it is sent again, and the
    // main it guards does not count it as in transit.
    private static void AStoppedUnitIsResentAndNotInTransit()
    {
        var s = Field(5.0, (100, 0f, 0f), (101, -1500f, -150f));
        int at = s.IndexOfOwn(101);
        s.Own[at].RegimentPaths = 0;
        s.Own[at].QueuedOrders = 0;
        s.Own[at].MovementMode = 0;
        s.Own[at].LastWaypointX = -450f;
        s.Own[at].LastWaypointZ = -150f;
        var leaf = Leaf(101, DirectChildRole.RefuseLeft, postMain: 100, postLeft: true);
        AssertEqual(FlankGuardLeaf.GuardOutcome.Move, FlankGuardLeaf.PlanGuard(s, at, in leaf, false, false, false, out BattleOrderIntent resent, out string why), "re-sent: " + why);
        AssertTrue(Math.Abs(resent.TargetX - -450f) < 0.5f, "to the post");

        var map = LeafMap(Leaf(100, DirectChildRole.Main), leaf);
        AssertTrue(Holds(s, 100, map, new Dictionary<int, MainFlankGuard.HoldState>(), out string reason, out _), "the main holds");
        AssertEqual(MainFlankGuard.HoldReason, reason, "with nobody on the way: a stopped unit is no guard in transit");

        // the same unit with a path under way is in transit
        s.Own[at].RegimentPaths = 1;
        Holds(s, 100, map, new Dictionary<int, MainFlankGuard.HoldState>(), out string moving, out _);
        AssertEqual(MainFlankGuard.HoldReason, moving, "(the right flank still has nobody, so the reason stays unguarded)");
        AssertTrue(FlankGuardLeaf.MovingToPost(in s.Own[at], FacingPlusZ(), 0f, 0f, true), "with a path it is moving to its post");
        s.Own[at].RegimentPaths = 0;
        AssertFalse(FlankGuardLeaf.MovingToPost(in s.Own[at], FacingPlusZ(), 0f, 0f, true), "without one it is not");
        s.Own[at].QueuedOrders = 1;
        AssertTrue(FlankGuardLeaf.MovingToPost(in s.Own[at], FacingPlusZ(), 0f, 0f, true), "a queued order counts");
        s.Own[at].QueuedOrders = 0;
        s.Own[at].MovementMode = 1;
        AssertTrue(FlankGuardLeaf.MovingToPost(in s.Own[at], FacingPlusZ(), 0f, 0f, true), "so does a movement mode");
    }

    // The post computed by the formula with the axis's own vectors, checked against an independent computation on axes facing +X and diagonally.
    private static void ThePostOnARotatedAxis()
    {
        // facing +X: forward (1,0), right (0,-1)
        var east = TacticalFrontAxis.FromCentroids(0f, 0f, 1000f, 0f);
        MainFlankGuard.PostOf(in east, 100f, 200f, left: false, out float rx, out float rz, out float rh);
        AssertTrue(Math.Abs(rx - -50f) < 0.5f && Math.Abs(rz - -250f) < 0.5f, "right post facing east: 150 m back along x, 450 m out along -z: " + rx + "," + rz);
        AssertTrue(Math.Abs(rh - 135f) < 0.5f, "facing outward to the right of an eastward front: " + rh);
        MainFlankGuard.PostOf(in east, 100f, 200f, left: true, out float lx, out float lz, out float lh);
        AssertTrue(Math.Abs(lx - -50f) < 0.5f && Math.Abs(lz - 650f) < 0.5f, "left post facing east: " + lx + "," + lz);
        AssertTrue(Math.Abs(lh - 45f) < 0.5f, "facing outward to the left: " + lh);

        // diagonal: forward (0.6, 0.8), right (0.8, -0.6)
        var diagonal = TacticalFrontAxis.FromCentroids(0f, 0f, 300f, 400f);
        foreach (bool left in new[] { false, true })
        {
            float side = left ? -1f : 1f;
            double ex = 100 + 0.8 * side * 450 - 0.6 * 150, ez = 200 - 0.6 * side * 450 - 0.8 * 150;
            double dx = 0.6 * Math.Cos(Math.PI / 4) + 0.8 * side * Math.Sin(Math.PI / 4), dz = 0.8 * Math.Cos(Math.PI / 4) - 0.6 * side * Math.Sin(Math.PI / 4);
            double eh = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            if (eh < 0) eh += 360.0;
            MainFlankGuard.PostOf(in diagonal, 100f, 200f, left, out float x, out float z, out float h);
            AssertTrue(Math.Abs(x - ex) < 0.5 && Math.Abs(z - ez) < 0.5, (left ? "left" : "right") + " post on the diagonal: " + x + "," + z + " expected " + ex + "," + ez);
            AssertTrue(Math.Abs(h - eh) < 0.5, (left ? "left" : "right") + " heading on the diagonal: " + h + " expected " + eh);
            // and the post is a frontage out sideways and 150 m back, whatever the axis
            float lateral = diagonal.LateralOf(x, z) - diagonal.LateralOf(100f, 200f);
            float depth = diagonal.DepthOf(x, z) - diagonal.DepthOf(100f, 200f);
            AssertTrue(Math.Abs(lateral - side * 450f) < 0.5f && Math.Abs(depth - -150f) < 0.5f, "lateral " + lateral + " depth " + depth);
        }
    }

    // ---- wiring: ProduceLeaf is Unity-bound and runs only in game; it must call the two pure halves tested above ----

    private static void ThePostureProducerWiring()
    {
        string producer = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/Producers/PostureIntentProducer.cs");
        int at = producer.IndexOf("private static void ProduceLeaf(", StringComparison.Ordinal);
        AssertTrue(at >= 0, "ProduceLeaf exists");
        string body = producer.Substring(at, 9000);
        FlankResponseTests.AssertPinned(body, "FlankHolding(s, i, leafMap", "the leaf pass asks whether the main's flanks are covered");
        FlankResponseTests.AssertPinned(body, "task = CommandTaskType.HoldObjective;", "an open flank holds the main in place");
        FlankResponseTests.AssertPinned(body, "army.CurrentBattleStance != BattleStance.Defender", "a Defender's main advances nowhere and is not gated");
        FlankResponseTests.AssertPinned(body, "FlankGuardLeaf.PlanGuard(s, i, in leaf, closeEngaged, pending, withdrawing, out BattleOrderIntent guardMove", "a flank guard is planned before the slot path");
        // The whole block, once, comments aside: a submit that waits on the Move outcome (the standing claim then lost) or a block that does not return fails this.
        UnitChurnTests.PinStatement(UnitChurnTests.Code(body),
            "if (guardOutcome != FlankGuardLeaf.GuardOutcome.NoPost) { if (guardOutcome != FlankGuardLeaf.GuardOutcome.Withdrawing) sink.Submit(guardMove); "
            + "if (guardOutcome == FlankGuardLeaf.GuardOutcome.Move) t.Groups[i].Submitted = true; "
            + "PostureTelemetry.Leaf(s, i, leaf, task, guardWhy, guardOutcome == FlankGuardLeaf.GuardOutcome.Move); return; }",
            "a posted guard is handled here, submits its Move or its standing claim every tick it is posted, and never runs the slot path");
        FlankResponseTests.AssertPinned(body, "bool withdrawing = FormationWithdraws(s, t, i, task, side);", "a withdrawing formation is asked before the guard is planned");
        FlankResponseTests.AssertPinned(producer, "FlankGuardLeaf.MainHolds(s, mainIndex, leafMap, _flankHold", "the hold is the pure half's");
        FlankResponseTests.AssertPinned(producer, "return _flankHold.TryGetValue(groupId, out MainFlankGuard.HoldState hold) && hold.Holding", "a main held on its flank is no exploit asset");
        FlankResponseTests.AssertPinned(producer, "if (change != null) EmitFlankHold(", "one row per change of the hold, advance included");
        FlankResponseTests.AssertPinned(TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Battle/Producers/FlankGuardLeaf.cs"), "change = MainFlankGuard.ChangeReason(previous, next, cover);", "the change reason is the pure half's");
        string army = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Orchestrator/ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(army, "FlankAnchoredProbe(), _flankPosts, _flankGuardReport", "the overlay is built with the terrain picture's anchored ends and the standing posts");
        FlankResponseTests.AssertPinned(army, "if (builtAgo >= LeafMapRefreshHours) LayFlankGuards(tree, cascadeRebuilt: false);", "the overlay is laid again every few doctrine minutes, the cascade not");
        FlankResponseTests.AssertPinned(army, "includeCrest: false", "a crest is cover, not an impassable end");
    }
}
