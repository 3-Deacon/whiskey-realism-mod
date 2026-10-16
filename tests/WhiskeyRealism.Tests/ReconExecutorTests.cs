using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G slice G5d (docs/superpowers/plans/2026-10-15-campaign-planner.md § G5d): the reconnaissance executor
// (ReconExecutor, pure, against a recording fake host), the second move budget, the #15 stand-down and the runtime
// pins. ReconOrders' own decisions (CavalryOrderFor, MayAct) are tested with the planner's pure half in ReconPlannerTests.
internal static class ReconExecutorTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("NoScoutingOrderOnGroupContainingPlayerCommand", NoScoutingOrderOnGroupContainingPlayerCommand),
            ("ScoutingOrderGoesToACavalryScreenWithItsMove", ScoutingOrderGoesToACavalryScreenWithItsMove),
            ("InfantryScreenAndOtherKindsGetNoCavalryOrder", InfantryScreenAndOtherKindsGetNoCavalryOrder),
            ("RecalledScreenGetsTheClearOrderAndMovesBack", RecalledScreenGetsTheClearOrderAndMovesBack),
            ("ClearedScreenGetsTheClearOrderAndNoMove", ClearedScreenGetsTheClearOrderAndNoMove),
            ("RetaskedScreenKeepsItsOrderOnlyAsAScreen", RetaskedScreenKeepsItsOrderOnlyAsAScreen),
            ("OneCavalryCallPerFormation", OneCavalryCallPerFormation),
            ("PlayerCommandRefusedLiveByTheExecutor", PlayerCommandRefusedLiveByTheExecutor),
            ("MapGroupPlayerGetsNoReconOrderAndNoMove", MapGroupPlayerGetsNoReconOrderAndNoMove),
            ("MissionMovesToItsHoldPoint", MissionMovesToItsHoldPoint),
            ("HoldingMissionIsNotMoved", HoldingMissionIsNotMoved),
            ("FormationAlreadyHeadingForItsPostIsNotReissued", FormationAlreadyHeadingForItsPostIsNotReissued),
            ("RecallPointDriftBelowArrivalIssuesNothing", RecallPointDriftBelowArrivalIssuesNothing),
            ("ReconBudgetSeparateFromMainEffort", ReconBudgetSeparateFromMainEffort),
            ("ReconMovesCappedAtThreePerReview", ReconMovesCappedAtThreePerReview),
            ("RecalledMissionsAreServedFirstUnderTheCap", RecalledMissionsAreServedFirstUnderTheCap),
            ("BusyLockedAndNonCombatFormationsAreSkipped", BusyLockedAndNonCombatFormationsAreSkipped),
            ("RestrictedPostIsRefusedThenGivenUp", RestrictedPostIsRefusedThenGivenUp),
            ("RefusedMoveIsAMissNotAnIssue", RefusedMoveIsAMissNotAnIssue),
            ("MoveQueuesBehindARunningOrder", MoveQueuesBehindARunningOrder),
            ("WlSideFormationGetsVanillaAiTransportDefault", WlSideFormationGetsVanillaAiTransportDefault),
            ("RowOncePerTransition", RowOncePerTransition),
            ("ClearedAndMissingMissionsLogOnce", ClearedAndMissingMissionsLogOnce),
            ("ReconTallyCountsWhatTheExecutorDid", ReconTallyCountsWhatTheExecutorDid),
            ("ReconGaveUpFeedsTheAllocator", ReconGaveUpFeedsTheAllocator),
            ("CavalryModeIsTheCodeMinusOneTwenty", CavalryModeIsTheCodeMinusOneTwenty),
            ("ReconFormationStandsDownFromAreaMoves", ReconFormationStandsDownFromAreaMoves),
            ("ReconExecutorFilesHoldNoUnityTypes", ReconExecutorFilesHoldNoUnityTypes),
            ("ReconExecutorRuntimeStatementsPinned", ReconExecutorRuntimeStatementsPinned),
            ("ReconExecutorHostReadsLiveStateAndSendsOnlyNewOrders", ReconExecutorHostReadsLiveStateAndSendsOnlyNewOrders),
            ("ReconExecutorWiringPinned", ReconExecutorWiringPinned),
            ("OldProbeStandsDownWhileTheReconExecutorRuns", OldProbeStandsDownWhileTheReconExecutorRuns),
            ("AreaMoveStandDownIsReadByTheRuntimeGate", AreaMoveStandDownIsReadByTheRuntimeGate),
            ("ReconFlagDescriptionNamesTheExecutor", ReconFlagDescriptionNamesTheExecutor),
            ("ReconMissionRowRoutesToTheRegisteredEvent", ReconMissionRowRoutesToTheRegisteredEvent),
        });
    }

    private const int Day = 100;

    // ---- the cavalry order ----

    private static void NoScoutingOrderOnGroupContainingPlayerCommand()
    {
        // A CarryingGroup screen holds the W&L player's brigade: the order would reach him (PerformActionForGroupForAllUnitsOnLevel
        // at level -1, 92572-92620). The group is moved like any AI formation (stage F's MoveUnitTo Postfix dispatches).
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.9f, chain: WlChainClass.CarryingGroup));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        var host = new FakeReconHost();
        host.Chain[1] = WlChainClass.CarryingGroup;
        var tally = new ReconExecutor().Review(s, ledger, host, new MoveTracker(), wlPlayerAlliance: true);
        AssertEqual(0, host.Orders.Count, "no ChangeCampaignOrders call for a group carrying the player's command");
        AssertEqual(1, host.Moves.Count, "and the screen still moves");
        AssertEqual(300f, host.Moves[0].x, "to its hold point");
        AssertEqual(0, tally.Ordered, "nothing counted as ordered");
    }

    private static void ScoutingOrderGoesToACavalryScreenWithItsMove()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.8f));
        var host = new FakeReconHost();
        var tally = new ReconExecutor().Review(s, Ledger(s, (ReconKind.Screen, 1, 300f, 0f)), host, new MoveTracker(), false);
        AssertEqual(1, host.Orders.Count, "one cavalry call");
        AssertEqual(1, host.Orders[0].id, "for the screen");
        AssertEqual(ReconOrders.ScoutingOrder, host.Orders[0].code, "Scouting");
        AssertEqual(1, host.Moves.Count, "and one move");
        AssertEqual(1, tally.Ordered, "counted");
        AssertEqual(1, tally.Issued, "counted");
    }

    private static void InfantryScreenAndOtherKindsGetNoCavalryOrder()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        s.Own.Add(Unit(2, 0f, 10f, cavalry: 0.9f));
        s.Own.Add(Unit(3, 0f, 20f, cavalry: 0.9f));
        s.Own.Add(Unit(4, 0f, 30f, cavalry: 0.9f));
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, Ledger(s,
            (ReconKind.Screen, 1, 300f, 0f), (ReconKind.Picket, 2, 300f, 10f), (ReconKind.AdvanceGuard, 3, 300f, 20f), (ReconKind.Probe, 4, 300f, 30f)),
            host, new MoveTracker(), false);
        AssertEqual(0, host.Orders.Count, "an infantry screen, a picket, an advance guard and a probe get no cavalry order");
    }

    private static void RecalledScreenGetsTheClearOrderAndMovesBack()
    {
        var s = World();
        s.HasCapital = true;
        s.CapitalX = 10f;
        s.CapitalZ = 0f;
        var tired = Unit(1, 300f, 0f, cavalry: 0.8f);
        tired.Supply01 = 0.3f;
        s.Own.Add(tired);
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        ledger.OnReview(s, null);                                       // low supply: recalled to the capital
        AssertEqual(ReconState.Recalled, ledger.Missions[0].State, "recalled");
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, ledger, host, new MoveTracker(), false);
        AssertEqual(1, host.Orders.Count, "one call");
        AssertEqual(ReconOrders.NoCavalryOrder, host.Orders[0].code, "the Scouting order is taken off (120)");
        AssertEqual(1, host.Moves.Count, "and it moves back");
        AssertEqual(10f, host.Moves[0].x, "to the recall point the ledger gives");
    }

    private static void ClearedScreenGetsTheClearOrderAndNoMove()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        ledger.Sync(s, new List<ReconTask>(), new Allocation { Alliance = 0 });          // no task holds it any more: Cleared "unassigned"
        AssertEqual(ReconState.Cleared, ledger.Missions[0].State, "cleared");
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, ledger, host, new MoveTracker(), false);
        AssertEqual(1, host.Orders.Count, "the review it is cleared in is the one that takes the order off");
        AssertEqual(ReconOrders.NoCavalryOrder, host.Orders[0].code, "120");
        AssertEqual(0, host.Moves.Count, "a cleared mission sends nothing anywhere");
    }

    private static void RetaskedScreenKeepsItsOrderOnlyAsAScreen()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        // The same kind re-targeted in place: still a screen.
        Resync(ledger, s, (ReconKind.Screen, 1, 400f, 0f));
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, ledger, host, new MoveTracker(), false);
        AssertEqual(1, host.Orders.Count, "one call for the unit");
        AssertEqual(ReconOrders.ScoutingOrder, host.Orders[0].code, "a re-targeted screen keeps Scouting");

        // Re-tasked to another kind: the old screen mission is Cleared, the picket gets no order, and the scouting it
        // was carrying is taken off.
        var s2 = World();
        s2.Own.Add(Unit(1, 0f, 0f, cavalry: 0.8f));
        var ledger2 = Ledger(s2, (ReconKind.Screen, 1, 300f, 0f));
        Resync(ledger2, s2, (ReconKind.Picket, 1, 400f, 0f));
        var host2 = new FakeReconHost();
        new ReconExecutor().Review(s2, ledger2, host2, new MoveTracker(), false);
        AssertEqual(1, host2.Orders.Count, "one call for the unit");
        AssertEqual(ReconOrders.NoCavalryOrder, host2.Orders[0].code, "a picket is not a scout: 120");
        AssertEqual(1, host2.Moves.Count, "and it goes to the picket post");
        AssertEqual(400f, host2.Moves[0].x, "the new hold point");
    }

    private static void OneCavalryCallPerFormation()
    {
        // Active screen mission + a cleared one for the same unit (a re-task to the same kind at a new post): 121 wins, once.
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        Resync(ledger, s, (ReconKind.Picket, 1, 400f, 0f));
        Resync(ledger, s, (ReconKind.Screen, 1, 500f, 0f));
        int forUnit = 0;
        foreach (var m in ledger.Missions) if (m.InstanceId == 1) forUnit++;
        AssertTrue(forUnit >= 2, "fixture: the unit carries several missions in the list");
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, ledger, host, new MoveTracker(), false);
        AssertEqual(1, host.Orders.Count, "one call, not one per mission");
        AssertEqual(ReconOrders.ScoutingOrder, host.Orders[0].code, "the active screen's order beats a cleared mission's");
    }

    // ---- the W&L chain, re-read live ----

    private static void PlayerCommandRefusedLiveByTheExecutor()
    {
        // The snapshot is the day's start; stage F's classification is read again before any act (the player may have
        // taken the group since).
        foreach (var chain in new[] { WlChainClass.PlayerCommand, WlChainClass.CicSide })
        {
            var s = World();
            s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.9f));
            var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
            var host = new FakeReconHost();
            host.Chain[1] = chain;
            var tally = new ReconExecutor().Review(s, ledger, host, new MoveTracker(), true);
            AssertEqual(0, host.Moves.Count, chain + ": not moved");
            AssertEqual(0, host.Orders.Count, chain + ": not ordered");
            AssertEqual(0, host.Raised.Count, chain + ": its transport modes untouched");
            AssertEqual(1, tally.SkippedPlayer, chain + ": counted");
        }
    }

    private static void MapGroupPlayerGetsNoReconOrderAndNoMove()
    {
        // G4 never picks him; if the chain changed after the pick, recon addresses him by neither a move nor a dispatch.
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.9f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        var host = new FakeReconHost();
        host.Chain[1] = WlChainClass.PlayerMapGroup;
        var tally = new ReconExecutor().Review(s, ledger, host, new MoveTracker(), true);
        AssertEqual(0, host.Moves.Count, "not moved");
        AssertEqual(0, host.Orders.Count, "not ordered");
        AssertEqual(1, tally.SkippedPlayer, "counted");
    }

    // ---- the moves ----

    private static void MissionMovesToItsHoldPoint()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var tracker = new MoveTracker();
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, Ledger(s, (ReconKind.Picket, 1, 300f, 40f)), host, tracker, false);
        AssertEqual(1, host.Moves.Count, "one move");
        AssertEqual(300f, host.Moves[0].x, "x of the hold point");
        AssertEqual(40f, host.Moves[0].z, "z of the hold point");
        AssertTrue(tracker.InTransit(1), "the tracker waits for the next review's judgement");
        AssertFalse(tracker.ShouldIssue(1, 300f, 40f, Day, MoveBudget.Recon), "and does not issue the same move twice");
    }

    private static void HoldingMissionIsNotMoved()
    {
        var s = World();
        s.Own.Add(Unit(1, 295f, 0f, cavalry: 0.1f));
        var ledger = Ledger(s, (ReconKind.Picket, 1, 300f, 0f));
        ledger.OnReview(s, null);
        AssertEqual(ReconState.Holding, ledger.Missions[0].State, "fixture: at its post");
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, ledger, host, new MoveTracker(), false);
        AssertEqual(0, host.Moves.Count, "a holding mission stays put");
    }

    private static void FormationAlreadyHeadingForItsPostIsNotReissued()
    {
        var s = World();
        var u = Unit(1, 0f, 0f, cavalry: 0.1f);
        u.WaypointX = 305f;                                              // within CampaignMover.ArrivalRadius of the post
        s.Own.Add(u);
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, Ledger(s, (ReconKind.Picket, 1, 300f, 0f)), host, new MoveTracker(), false);
        AssertEqual(0, host.Moves.Count, "its last waypoint already is the post");
    }

    private static void RecallPointDriftBelowArrivalIssuesNothing()
    {
        // The recall point is the nearest held formation's position, recomputed every review; MoveTracker.SameTarget
        // takes 2 units for a new target, so without the arrival test a drifting point would spend a move a day.
        var s = World();
        var tired = Unit(1, 300f, 0f, cavalry: 0.1f);
        tired.Supply01 = 0.3f;
        s.Own.Add(tired);
        s.Own.Add(Unit(5, 10f, 0f, cavalry: 0.1f));
        var held = new Allocation { Alliance = 0 };
        held.Assignments.Add(new Assignment { InstanceId = 5, Role = FormationRole.Hold, ReconMissionId = -1 });
        var ledger = Ledger(s, (ReconKind.Picket, 1, 300f, 0f));
        ledger.OnReview(s, held);
        var tracker = new MoveTracker();
        var host = new FakeReconHost();
        var executor = new ReconExecutor();
        executor.Review(s, ledger, host, tracker, false);
        AssertEqual(1, host.Moves.Count, "day one: back toward the held formation");
        AssertEqual(10f, host.Moves[0].x, "at its position");

        var s2 = World(Day + 1);
        var back = Unit(1, 200f, 0f, cavalry: 0.1f);
        back.Supply01 = 0.3f;
        back.WaypointX = 10f;
        s2.Own.Add(back);
        s2.Own.Add(Unit(5, 14f, 0f, cavalry: 0.1f));                    // the held formation shifted 4 units
        ledger.OnReview(s2, held);
        AssertEqual(14f, ledger.Missions[0].HoldX, "fixture: the recall point drifted");
        var next = new FakeReconHost();
        executor.Review(s2, ledger, next, tracker, false);
        AssertEqual(0, next.Moves.Count, "day two: no new move for a point that moved less than the arrival radius");
    }

    private static void ReconBudgetSeparateFromMainEffort()
    {
        var t = new MoveTracker();
        int day = 100;
        for (int i = 1; i <= MoveTracker.MaxMovesPerDay; i++) t.RecordIssued(i, 500f, 0f, 100f, 0f, day, queued: false);
        AssertFalse(t.ShouldIssue(50, 100f, 0f, day), "the planner's six are spent");
        for (int i = 1; i <= MoveTracker.MaxReconMovesPerDay; i++)
        {
            AssertTrue(t.ShouldIssue(100 + i, 100f, 0f, day, MoveBudget.Recon), "recon move " + i + " still has its own budget");
            t.RecordIssued(100 + i, 500f, 0f, 100f, 0f, day, queued: false, budget: MoveBudget.Recon);
        }
        AssertFalse(t.ShouldIssue(120, 100f, 0f, day, MoveBudget.Recon), "recon's three are spent");
        AssertFalse(t.ShouldIssue(50, 100f, 0f, day), "and they did not give the planner any back");
        AssertTrue(t.ShouldIssue(120, 100f, 0f, day + 1, MoveBudget.Recon), "the next review day has a fresh recon budget");

        var other = new MoveTracker();
        for (int i = 1; i <= MoveTracker.MaxReconMovesPerDay; i++) other.RecordIssued(i, 500f, 0f, 100f, 0f, day, queued: false, budget: MoveBudget.Recon);
        AssertTrue(other.ShouldIssue(50, 100f, 0f, day), "recon moves spend none of the main effort's budget");
        AssertEqual(3, MoveTracker.MaxReconMovesPerDay, "the plan's cap");
        AssertEqual(6, MoveTracker.MaxMovesPerDay, "and P8's, unchanged");
        other.RecordIssued(60, 500f, 0f, 100f, 0f, day, queued: true, budget: MoveBudget.Recon);
        AssertTrue(other.ShouldIssue(70, 100f, 0f, day + 1, MoveBudget.Recon), "a queued recon move spends nothing");
    }

    private static void ReconMovesCappedAtThreePerReview()
    {
        var s = World();
        var ms = new List<(ReconKind, int, float, float)>();
        for (int i = 1; i <= 5; i++)
        {
            s.Own.Add(Unit(i, 0f, i * 10f, cavalry: 0.1f));
            ms.Add((ReconKind.Picket, i, 300f, i * 10f));
        }
        var tracker = new MoveTracker();
        var host = new FakeReconHost();
        var tally = new ReconExecutor().Review(s, Ledger(s, ms.ToArray()), host, tracker, false);
        AssertEqual(MoveTracker.MaxReconMovesPerDay, host.Moves.Count, "three of five missions are sent");
        AssertEqual(3, tally.Issued, "counted");
        // The planner's own budget is untouched: a main effort move the same day is still allowed six times.
        for (int i = 0; i < MoveTracker.MaxMovesPerDay; i++) AssertTrue(tracker.ShouldIssue(900 + i, 1f, 1f, Day), "main effort move " + i);
    }

    private static void RecalledMissionsAreServedFirstUnderTheCap()
    {
        var s = World();
        s.HasCapital = true;
        s.CapitalX = 10f;
        for (int i = 1; i <= 4; i++) s.Own.Add(Unit(i, 100f, i * 10f, cavalry: 0.1f));
        var tired = Unit(5, 100f, 100f, cavalry: 0.1f);
        tired.Supply01 = 0.3f;
        s.Own.Add(tired);
        var ledger = Ledger(s,
            (ReconKind.Picket, 1, 300f, 10f), (ReconKind.Picket, 2, 300f, 20f), (ReconKind.Picket, 3, 300f, 30f),
            (ReconKind.Picket, 4, 300f, 40f), (ReconKind.Picket, 5, 300f, 100f));
        ledger.OnReview(s, null);
        AssertEqual(ReconState.Recalled, ledger.Missions[4].State, "fixture: the last mission is the recalled one");
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, ledger, host, new MoveTracker(), false);
        AssertEqual(3, host.Moves.Count, "the cap");
        AssertEqual(5, host.Moves[0].id, "a recalled formation is sent first");
    }

    private static void BusyLockedAndNonCombatFormationsAreSkipped()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f, extra: FormationFlags.InBattle));
        s.Own.Add(Unit(2, 0f, 10f, cavalry: 0.1f));
        var nonCombat = Unit(3, 0f, 20f, cavalry: 0.1f);
        nonCombat.Flags = FormationFlags.None;
        s.Own.Add(nonCombat);
        var ledger = Ledger(s, (ReconKind.Picket, 1, 300f, 0f), (ReconKind.Picket, 2, 300f, 10f), (ReconKind.Picket, 3, 300f, 20f));
        var host = new FakeReconHost();
        host.Locked.Add(2);
        var tally = new ReconExecutor().Review(s, ledger, host, new MoveTracker(), false);
        AssertEqual(0, host.Moves.Count, "a formation in battle, one the old package layer holds and a non-combat group are not sent");
        AssertEqual(3, tally.SkippedBusy, "counted");
    }

    private static void RestrictedPostIsRefusedThenGivenUp()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var ledger = Ledger(s, (ReconKind.Picket, 1, 300f, 0f));
        var tracker = new MoveTracker();
        var host = new FakeReconHost { Restricted = (x, z) => true };
        var executor = new ReconExecutor();
        var first = executor.Review(s, ledger, host, tracker, false);
        AssertEqual(0, host.Moves.Count, "no final waypoint can stand there: no MoveUnitTo");
        AssertEqual(1, first.Failed, "a miss");
        var second = executor.Review(s, ledger, host, tracker, false);
        AssertEqual(1, second.Retried, "one retry");
        var third = executor.Review(s, ledger, host, tracker, false);
        AssertEqual(0, third.Failed + third.Retried + third.Issued, "then it is given up at this post");
        var gaveUp = new List<GaveUpMove>();
        tracker.CollectGaveUp(gaveUp, Day);
        AssertEqual(1, gaveUp.Count, "and the allocator is told");
    }

    private static void RefusedMoveIsAMissNotAnIssue()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var tracker = new MoveTracker();
        var host = new FakeReconHost { MoveResult = false };
        var tally = new ReconExecutor().Review(s, Ledger(s, (ReconKind.Picket, 1, 300f, 0f)), host, tracker, false);
        AssertEqual(1, host.Moves.Count, "vanilla was asked");
        AssertEqual(1, tally.Failed, "and refused (a state neither own, allied nor at war, 14486)");
        AssertEqual(0, tally.Issued, "not an issue");
        AssertFalse(tracker.InTransit(1), "not in transit");
    }

    private static void MoveQueuesBehindARunningOrder()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f, extra: FormationFlags.MoveQueued));
        var tracker = new MoveTracker();
        var host = new FakeReconHost();
        var tally = new ReconExecutor().Review(s, Ledger(s, (ReconKind.Picket, 1, 300f, 0f)), host, tracker, false);
        AssertEqual(0, host.Moves.Count, "no second order while one is running (14493)");
        AssertEqual(1, tally.Queued, "recorded as queued");
        AssertTrue(tracker.InTransit(1), "and not judged yet");
        AssertTrue(tracker.ShouldIssue(2, 1f, 1f, Day, MoveBudget.Recon) && tracker.ShouldIssue(3, 1f, 1f, Day, MoveBudget.Recon), "a queued move spends none of the budget");
    }

    private static void WlSideFormationGetsVanillaAiTransportDefault()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, Ledger(s, (ReconKind.Picket, 1, 300f, 0f)), host, new MoveTracker(), wlPlayerAlliance: true);
        AssertEqual(1, host.Raised.Count, "on the W&L player's side userails and userivers are raised before the move, as for the mover");
        var plain = new FakeReconHost();
        new ReconExecutor().Review(s, Ledger(s, (ReconKind.Picket, 1, 300f, 0f)), plain, new MoveTracker(), wlPlayerAlliance: false);
        AssertEqual(0, plain.Raised.Count, "and nowhere else");
    }

    // ---- the telemetry ----

    private static void RowOncePerTransition()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var ledger = Ledger(s, (ReconKind.Picket, 1, 300f, 0f));
        var executor = new ReconExecutor();
        var first = executor.Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        AssertEqual(1, first.Rows.Count, "one row for a new mission");
        AssertEqual("[ReconMission] alliance=0 unit=u1 kind=Picket state=Assigned reason=test", first.Rows[0], "the plan's row");
        var again = executor.Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        AssertEqual(0, again.Rows.Count, "none while nothing changed");
        var arrived = World();
        arrived.Own.Add(Unit(1, 298f, 0f, cavalry: 0.1f));
        ledger.OnReview(arrived, null);
        var holding = executor.Review(arrived, ledger, new FakeReconHost(), new MoveTracker(), false);
        AssertEqual(1, holding.Rows.Count, "one on the transition");
        AssertContains(holding.Rows[0], "state=Holding", "to Holding");
    }

    private static void ClearedAndMissingMissionsLogOnce()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        s.Own.Add(Unit(2, 0f, 10f, cavalry: 0.1f));
        var ledger = Ledger(s, (ReconKind.Picket, 1, 300f, 0f), (ReconKind.Screen, 2, 300f, 10f));
        var executor = new ReconExecutor();
        executor.Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        var gone = World();
        gone.Own.Add(Unit(2, 0f, 10f, cavalry: 0.1f));
        ledger.OnReview(gone, null);                                    // unit 1 is no longer there: Cleared "missing"
        var tally = executor.Review(gone, ledger, new FakeReconHost(), new MoveTracker(), false);
        AssertEqual(1, tally.Rows.Count, "one row for the cleared mission");
        AssertContains(tally.Rows[0], "unit=u1:1 ", "named by its unit key when the formation is gone");
        AssertContains(tally.Rows[0], "state=Cleared reason=missing", "with its reason");
        AssertEqual(0, executor.Review(gone, ledger, new FakeReconHost(), new MoveTracker(), false).Rows.Count, "and not again");
        ledger.OnReview(gone, null);                                    // dropped
        var next = World(Day + 1);
        next.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        next.Own.Add(Unit(2, 0f, 10f, cavalry: 0.1f));
        Resync(ledger, next, (ReconKind.Screen, 2, 300f, 10f), (ReconKind.Picket, 1, 300f, 0f));
        AssertEqual(1, executor.Review(next, ledger, new FakeReconHost(), new MoveTracker(), false).Rows.Count, "a new mission logs its own row");
    }

    private static void ReconTallyCountsWhatTheExecutorDid()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.9f));
        s.Own.Add(Unit(2, 0f, 10f, cavalry: 0.1f));
        s.Own.Add(Unit(3, 0f, 20f, cavalry: 0.1f, extra: FormationFlags.InBattle));
        var host = new FakeReconHost();
        host.Chain[2] = WlChainClass.PlayerCommand;
        var tally = new ReconExecutor().Review(s, Ledger(s, (ReconKind.Screen, 1, 300f, 0f), (ReconKind.Picket, 2, 300f, 10f), (ReconKind.Picket, 3, 300f, 20f)), host, new MoveTracker(), false);
        AssertEqual(1, tally.Issued, "issued");
        AssertEqual(1, tally.Ordered, "ordered");
        AssertEqual(1, tally.SkippedPlayer, "skipped (player)");
        AssertEqual(1, tally.SkippedBusy, "skipped (busy)");
    }

    private static void ReconGaveUpFeedsTheAllocator()
    {
        // The allocator reads MoveTracker.CollectGaveUp for every move the tracker gave up on (G4, P8), so a recon post a
        // formation cannot reach is not given to it again before the give-up lapses.
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var ledger = Ledger(s, (ReconKind.Picket, 1, 300f, 0f));
        var tracker = new MoveTracker();
        tracker.RecordIssued(1, 0f, 0f, 300f, 0f, Day - 3, queued: false, budget: MoveBudget.Recon);
        tracker.Judge(1, 0f, 0f, 0f, 0f, Day - 2);                      // failed
        tracker.RecordIssued(1, 0f, 0f, 300f, 0f, Day - 2, queued: false, budget: MoveBudget.Recon);
        AssertEqual(MoveOutcome.GaveUp, tracker.Judge(1, 0f, 0f, 0f, 0f, Day - 1), "fixture: the retry missed");
        var gaveUp = new List<GaveUpMove>();
        tracker.CollectGaveUp(gaveUp, Day);
        AssertEqual(1, gaveUp.Count, "listed");
        var host = new FakeReconHost();
        new ReconExecutor().Review(s, ledger, host, tracker, false);
        AssertEqual(0, host.Moves.Count, "and the executor does not send it there again until the give-up lapses");
    }

    private static void CavalryModeIsTheCodeMinusOneTwenty()
    {
        AssertEqual(0, ReconOrders.ModeOf(ReconOrders.NoCavalryOrder), "120 is mode 0");
        AssertEqual(1, ReconOrders.ModeOf(ReconOrders.ScoutingOrder), "121 is mode 1 (Scouting)");
    }

    // ---- #15 and the old probe ----

    private static void ReconFormationStandsDownFromAreaMoves()
    {
        var onMission = new FormationDirectiveAssignment { UnitKey = "a:1", Directive = FormationDirective.Screen, Reason = "recon-mission", DirectMovementAllowed = true };
        AssertFalse(FormationDirectiveLedger.AllowsAreaMovement(onMission, reconExecutorActive: true), "a Screen formation is not pulled back to its area while the executor runs");
        AssertTrue(FormationDirectiveLedger.AllowsAreaMovement(onMission, reconExecutorActive: false), "with the executor off #15 may move it, as before");
        var guard = new FormationDirectiveAssignment { UnitKey = "a:2", Directive = FormationDirective.Guard, DirectMovementAllowed = true };
        AssertTrue(FormationDirectiveLedger.AllowsAreaMovement(guard, reconExecutorActive: true), "another directive is unaffected");
        AssertFalse(FormationDirectiveLedger.AllowsAreaMovement(new FormationDirectiveAssignment { Directive = FormationDirective.Delay, DirectMovementAllowed = true }, false), "Delay still blocks");
        AssertFalse(FormationDirectiveLedger.AllowsAreaMovement(new FormationDirectiveAssignment { Directive = FormationDirective.Concede, DirectMovementAllowed = true }, false), "Concede still blocks");
        AssertFalse(FormationDirectiveLedger.AllowsAreaMovement(new FormationDirectiveAssignment { Directive = FormationDirective.Guard, DirectMovementAllowed = false }, false), "a formation that cannot take direct movement still cannot");
        AssertTrue(FormationDirectiveLedger.AllowsAreaMovement(null, true), "no assignment: unchanged");
    }

    // ---- source pins ----

    private static void ReconExecutorFilesHoldNoUnityTypes()
    {
        foreach (string file in new[] { "ReconExecutor.cs", "MoveIssue.cs" })
        {
            string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/" + file);
            AssertFalse(src.Contains("UnityEngine") || Regex.IsMatch(src, @"\bVector[23]\b") || src.Contains("Regiment "), file + " holds no Unity or vanilla type");
        }
    }

    private static void ReconExecutorRuntimeStatementsPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReconExecutorRuntime.cs");
        AssertTrue(HasLine(src, "using (TelemetryPerf.Scope(\"campaign.planner.recon-execute\", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))"), "the plan's scope, 2 ms");
        AssertTrue(src.Contains(": VanillaMoveHost, IReconHost"), "the move half is the mover's (VanillaMoveHost): plain MoveUnitTo, no forced march, the package lock and the transport default are pinned there");
        string code = Regex.Replace(src, @"//[^\n]*", ""); // code, not the anchor comments
        AssertFalse(code.Contains("doublequick") || code.Contains("forcedmarch") || code.Contains("MoveUnitTo"), "screens halt at night like any group (133958-133961): no forced march, and no second MoveUnitTo call site");
        AssertTrue(HasLine(src, "return WlStrategicOrderBridge.ChainClassOf(in _chain, Alliance, unit);"), "the chain class is read again live, from a context captured once per pass");
        AssertTrue(HasLine(src, "Host.Bind(alliance, WlChainContext.Capture());"), "captured once per pass");
        AssertFalse(code.Contains("closestenemyunitfar") || code.Contains("GetLastTransmittedPathPos"), "contact is judged from beliefs and inbattle only, never an enemy destination (9099)");
        AssertTrue(HasLine(src, "for (int i = 0; i < tally.Rows.Count; i++) TelemetryRouter.LegacyInfo(tally.Rows[i], TelemetryLayer.Campaign);"), "a row per transition through the legacy sink the mover uses");
        AssertTrue(HasLine(src, "if (tally.Any) TelemetryRouter.LegacyInfo(tally.Summary(alliance), TelemetryLayer.Campaign);"), "and the counts when anything was done");
        AssertTrue(HasLine(src, "var tally = Executors[alliance].Review(snapshot, ledger, Host, tracker, DLC_WL.dlc_scenarioactive && GameVars.playeralliance == alliance);"), "the pure pass over the alliance's tracker, on the W&L player's side flagged");
        string host = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/VanillaMoveHost.cs");
        AssertTrue(HasLine(host, "return AICampaign.MoveUnitTo(unit, new Vector3(x, unit.GetPosition().y, z), movearoundenemyunits: true);"), "MoveUnitTo with movearoundenemyunits: true, as every Whiskey mover");
        AssertTrue(HasLine(host, "if (CoordinatedOperationRuntime.IsPackageLocked(unit)) return true;"), "the old package layer's lock holds until G6 (P10)");
        AssertTrue(HasLine(host, "unit.userails = true;") && HasLine(host, "unit.userivers = true;"), "the transport defaults of the mover");
    }

    private static void ReconExecutorHostReadsLiveStateAndSendsOnlyNewOrders()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReconExecutorRuntime.cs");
        string send = BodyOf(src, "public bool SendCavalryOrder(");
        AssertTrue(HasLine(send, "int mode = ReconOrders.ModeOf(orderCode);"), "the mode comes from the pure helper");
        AssertTrue(HasLine(send, "if (unit.cavalryordersordered == mode) return false;"), "ChangeCampaignOrders tests cavalryorders only (90738), so a queued order would be queued again every review: the host compares what was ordered");
        AssertTrue(HasLine(send, "battleUnits.ChangeCampaignOrders(unit, orderCode);"), "the player's own order path (90720-90770)");
        AssertTrue(HasLine(src, "if ((UnityEngine.Object)battleUnits == null || !battleUnits.IsCampaign) return null;"), "BattleUnits comes from the campaign map's GameController, never AICampaign's private bunits");
        AssertTrue(src.Contains("113960"), "the readiness reset that makes a repeat each review right is named");
    }

    private static void ReconExecutorWiringPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string body = BodyOf(src, "internal static void ReviewExecute(");
        int mover = body.IndexOf("CampaignMoverRuntime.Execute(", StringComparison.Ordinal);
        int recon = body.IndexOf("ReconExecutorRuntime.Execute(", StringComparison.Ordinal);
        AssertTrue(mover >= 0 && recon > mover, "G5d runs after G5a's mover, inside ReviewExecute");
        AssertTrue(HasLine(body, "ReconExecutorRuntime.Execute(alliance, Snapshots[alliance], Recon[alliance]);"), "it reads the alliance's ledger in the review, passed in: nothing outside the review can name the ledger");
        int pause = body.IndexOf("PlayerCommandClassifier.ReviewMode(live", StringComparison.Ordinal);
        int preWar = body.IndexOf("PlannerChapterRules.ExecutorsAllowed(", StringComparison.Ordinal);
        AssertTrue(pause >= 0 && preWar > pause && recon > preWar, "behind the pause, the pre-war gate and today's-plan test, like every executor");
        AssertTrue(HasLine(src, "return ExecutorsEnabled() && FlagOn(Plugin.EnableCampaignReconnaissance);"), "ReconExecutorActive: both planner flags and the recon flag");
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReconExecutorRuntime.cs");
        AssertTrue(runtime.Contains("CampaignScope.Register(\"campaign-planner-recon-executors\""), "the row memory resets with the campaign");
    }

    private static void OldProbeStandsDownWhileTheReconExecutorRuns()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs");
        string run = BodyOf(src, "internal static void Run(");
        AssertTrue(HasLine(run, "if (CampaignPlannerRuntime.ReconExecutorActive()) return;"), "the old probe never moves, stops or releases a formation beside the new probe missions");
        int standDown = run.IndexOf("ReconExecutorActive()", StringComparison.Ordinal);
        int firstWrite = run.IndexOf("StopRegiment", StringComparison.Ordinal);
        AssertTrue(standDown >= 0 && standDown < firstWrite, "before any write");
    }

    private static void AreaMoveStandDownIsReadByTheRuntimeGate()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/FormationDirectiveRuntime.cs");
        string gate = BodyOf(src, "internal static bool ShouldAllowAreaMovement(");
        AssertTrue(HasLine(gate, "return FormationDirectiveLedger.AllowsAreaMovement(GetAssignment(allianceId, unitKey), CampaignPlannerRuntime.ReconExecutorActive());"), "the one rule, with the executor's state");
    }

    private static void ReconFlagDescriptionNamesTheExecutor()
    {
        string plugin = ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        string flag = plugin.Substring(plugin.IndexOf("\"Enable Campaign Reconnaissance\"", StringComparison.Ordinal), 2400);
        AssertFalse(flag.Contains("until the reconnaissance executor ships"), "the executor has shipped: no 'until it ships' text");
        AssertFalse(flag.Contains("can still move them"), "#15 stands down for a formation on a mission");
        AssertContains(flag, "Scouting", "the cavalry order is named");
        AssertContains(flag, "Enable Campaign Planner Executors", "and the flag the executor also needs");
    }

    private static void ReconMissionRowRoutesToTheRegisteredEvent()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var tally = new ReconExecutor().Review(s, Ledger(s, (ReconKind.Picket, 1, 300f, 0f)), new FakeReconHost(), new MoveTracker(), false);
        AssertEqual(1, tally.Rows.Count, "fixture: the executor's own row");
        var route = WhiskeyRealism.Telemetry.TelemetryTagPolicy.Route(tally.Rows[0]);
        AssertTrue(route.RouteToSidecar, "the row reaches the campaign sidecar");
        AssertEqual("ReconMission", route.EventName, "as the event G0 registered");
        AssertEqual(WhiskeyRealism.Telemetry.TelemetryLayer.Campaign, route.Layer, "on the campaign layer");
        AssertEqual(WhiskeyRealism.Telemetry.TelemetryCategory.Write, route.Category, "as a write");
    }

    // ---- fixtures ----

    private static CampaignSnapshot World(int day = Day)
    {
        var s = new CampaignSnapshot();
        s.Reset(0, 0, day, PlayerCommandClass.NotCareer, -1, true, null);
        s.RelocationRange = 150f;
        s.OffensiveDominance = 1.1f;
        s.MinStrengthForMovement = 1000f;
        s.MinMoraleForMovement = 0.3f;
        return s;
    }

    private static FormationRecord Unit(int id, float x, float z, float cavalry, WlChainClass chain = WlChainClass.None, FormationFlags extra = FormationFlags.None)
    {
        return new FormationRecord
        {
            InstanceId = id, UnitKey = "u" + id + ":" + id, Name = "u" + id, UnitTyp = 15,
            X = x, Z = z, WaypointX = x, WaypointZ = z, Strength = 3000f, Morale = 0.8f, Supply01 = 1f,
            ReadinessStep = 3, CommandRange = 5000f, BugleRange = 10f, SightRadius = 50f,
            CavalryShare = cavalry, CommanderId = id, ParentInstanceId = -1, Chain = chain,
            Flags = FormationFlags.FightingForce | extra
        };
    }

    // Puts the formations on missions in one Sync: (kind, instance id, hold x, hold z); each mission is its own sector.
    private static ReconMissionLedger Ledger(CampaignSnapshot s, params (ReconKind kind, int id, float x, float z)[] missions)
    {
        var ledger = new ReconMissionLedger();
        Resync(ledger, s, missions);
        return ledger;
    }

    private static void Resync(ReconMissionLedger ledger, CampaignSnapshot s, params (ReconKind kind, int id, float x, float z)[] missions)
    {
        var tasks = new List<ReconTask>();
        var a = new Allocation { Alliance = 0 };
        int sector = 0;
        foreach (var m in missions)
        {
            var task = new ReconTask { Kind = m.kind, SectorId = sector++, FromX = m.x, FromZ = m.z, HoldX = m.x, HoldZ = m.z, Priority = 1f, Reason = "test" };
            tasks.Add(task);
            a.Assignments.Add(new Assignment { InstanceId = m.id, Role = FormationRole.Recon, SectorId = task.SectorId, TargetX = task.HoldX, TargetZ = task.HoldZ, Reason = task.Reason, ReconMissionId = -1 });
        }
        ledger.Sync(s, tasks, a);
    }
}

// The vanilla actions the recon executor performs, recorded.
internal sealed class FakeReconHost : IReconHost
{
    public readonly Dictionary<int, WlChainClass> Chain = new Dictionary<int, WlChainClass>();
    public Func<float, float, bool> Restricted = (x, z) => false;
    public readonly HashSet<int> Locked = new HashSet<int>();
    public bool MoveResult = true;
    public readonly List<(int id, float x, float z)> Moves = new List<(int id, float x, float z)>();
    public readonly List<(int id, int code)> Orders = new List<(int id, int code)>();
    public readonly List<int> Raised = new List<int>();

    public WlChainClass LiveChain(int instanceId) { return Chain.TryGetValue(instanceId, out var c) ? c : WlChainClass.None; }
    public bool TargetRestricted(float x, float z) { return Restricted(x, z); }
    public bool PackageLocked(int instanceId) { return Locked.Contains(instanceId); }
    public bool Move(int instanceId, float x, float z) { Moves.Add((instanceId, x, z)); return MoveResult; }
    public void RaiseTransportModes(int instanceId) { Raised.Add(instanceId); }
    public bool SendCavalryOrder(int instanceId, int orderCode) { Orders.Add((instanceId, orderCode)); return true; }
}
