using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

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
            ("MoveIssueReportsMovedOnlyWhenVanillaTookTheMove", MoveIssueReportsMovedOnlyWhenVanillaTookTheMove),
            ("WlSideFormationGetsVanillaAiTransportDefault", WlSideFormationGetsVanillaAiTransportDefault),
            ("RowOncePerTransition", RowOncePerTransition),
            ("ClearedAndMissingMissionsLogOnce", ClearedAndMissingMissionsLogOnce),
            ("RowLoggedWhenOnlyTheReasonChanges", RowLoggedWhenOnlyTheReasonChanges),
            ("ReconTallyCountsWhatTheExecutorDid", ReconTallyCountsWhatTheExecutorDid),
            ("OrderedCountsOnlyCallsTheHostMade", OrderedCountsOnlyCallsTheHostMade),
            ("RowMemoryIsPrunedWhenMissionsLeaveTheLedger", RowMemoryIsPrunedWhenMissionsLeaveTheLedger),
            ("ReconGiveUpsNeverFeedTheAllocatorOrG3", ReconGiveUpsNeverFeedTheAllocatorOrG3),
            ("OnlyTheRolesTheMoverSendsAreMoverRoles", OnlyTheRolesTheMoverSendsAreMoverRoles),
            ("FormationReassignedToTheMoverIsReleasedFromItsMission", FormationReassignedToTheMoverIsReleasedFromItsMission),
            ("InTransitFormationIsNotMovedByRecon", InTransitFormationIsNotMovedByRecon),
            ("LowSupplyRecallThatBecomesRecoverIsReleasedAndMovedOnlyByTheMover", LowSupplyRecallThatBecomesRecoverIsReleasedAndMovedOnlyByTheMover),
            ("ReassignedScreenStillGetsItsStandDown", ReassignedScreenStillGetsItsStandDown),
            ("ReconFlagOffEndsEveryMissionAndMovesNothing", ReconFlagOffEndsEveryMissionAndMovesNothing),
            ("ClearedScreenWaitsForItsStandDownAcrossDaysTheExecutorsMiss", ClearedScreenWaitsForItsStandDownAcrossDaysTheExecutorsMiss),
            ("PendingStandDownIsBoundedAtMaxMissionDays", PendingStandDownIsBoundedAtMaxMissionDays),
            ("PendingStandDownIsLetGoWhenItCanNeverBeSent", PendingStandDownIsLetGoWhenItCanNeverBeSent),
            ("StandDownIsPaidForTheNamedFormationOnly", StandDownIsPaidForTheNamedFormationOnly),
            ("QueuedStandDownIsKeptUntilItIsDelivered", QueuedStandDownIsKeptUntilItIsDelivered),
            ("RetaskedScreenSupersedesTheDebtOfItsClearedMission", RetaskedScreenSupersedesTheDebtOfItsClearedMission),
            ("StandDownDebtRoundTripsThroughRestore", StandDownDebtRoundTripsThroughRestore),
            ("MoverRefusalListsNothingAndKeepsTheObjective", MoverRefusalListsNothingAndKeepsTheObjective),
            ("QueuedReconMoveIsResentOnceItsOrderHasEnded", QueuedReconMoveIsResentOnceItsOrderHasEnded),
            ("QueuedReconMoveIsReleasedWhenItsMissionEnds", QueuedReconMoveIsReleasedWhenItsMissionEnds),
            ("QueuedMoverMoveSettlesWhenItsRoleNoLongerSends", QueuedMoverMoveSettlesWhenItsRoleNoLongerSends),
            ("QueuedMoverMoveIsResentWhenItsOrderEndedAndTheRoleStillSends", QueuedMoverMoveIsResentWhenItsOrderEndedAndTheRoleStillSends),
            ("QueuedRecordsSettleByOneRuleAndExpire", QueuedRecordsSettleByOneRuleAndExpire),
            ("QueuedRetryKeepsItsCountWhenItSettles", QueuedRetryKeepsItsCountWhenItSettles),
            ("QueuedSettleCallSitesPinned", QueuedSettleCallSitesPinned),
            ("GoneUnitReleasesTheDebtButMissingBattleUnitsKeepsIt", GoneUnitReleasesTheDebtButMissingBattleUnitsKeepsIt),
            ("ReconSyncAndFlagOffWiringPinned", ReconSyncAndFlagOffWiringPinned),
            ("CavalryModeIsTheCodeMinusOneTwenty", CavalryModeIsTheCodeMinusOneTwenty),
            ("QueuedOrderOfAnotherModeIsNeitherRepeatedNorCountedAndSelfHeals", QueuedOrderOfAnotherModeIsNeitherRepeatedNorCountedAndSelfHeals),
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
        // At its post, though its last waypoint leads elsewhere (a move vanilla gave it): the mission is Holding and recon does
        // not fight the move today; if the group is really leaving, the ledger's next state is Moving or Assigned and it is sent back.
        var s = World();
        var u = Unit(1, 295f, 0f, cavalry: 0.1f);
        u.WaypointX = 100f;
        s.Own.Add(u);
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
        var cleared = new MoveTracker();
        for (int i = 1; i <= MoveTracker.MaxReconMovesPerDay; i++) cleared.RecordIssued(i, 500f, 0f, 100f, 0f, day, queued: false, budget: MoveBudget.Recon);
        for (int i = 11; i <= 10 + MoveTracker.MaxMovesPerDay; i++) cleared.RecordIssued(i, 500f, 0f, 100f, 0f, day, queued: false);
        cleared.Clear();
        AssertTrue(cleared.ShouldIssue(30, 100f, 0f, day, MoveBudget.Recon) && cleared.ShouldIssue(31, 100f, 0f, day), "Clear gives both budgets back");
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
        AssertEqual(0, gaveUp.Count, "the allocator is not told: it matches a give-up by point (ReconGiveUpsNeverFeedTheAllocatorOrG3)");
        AssertFalse(tracker.ShouldIssue(1, 300f, 0f, Day, MoveBudget.Recon), "the executor's own give-up holds the post shut until it lapses");
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

    // The shared issue path's contract, which the mover reads to decide whether to list a formation in vanilla's offensive list.
    private static void MoveIssueReportsMovedOnlyWhenVanillaTookTheMove()
    {
        var f = Unit(1, 0f, 0f, cavalry: 0.1f);
        var took = new FakeReconHost();
        AssertTrue(MoveIssue.Issue(took, new MoveTracker(), in f, 300f, 0f, -1, Day, false, MoveBudget.Planner, out var issued), "vanilla took it");
        AssertEqual(MoveOutcome.Issued, issued, "an issue");
        var refused = new FakeReconHost { MoveResult = false };
        AssertFalse(MoveIssue.Issue(refused, new MoveTracker(), in f, 300f, 0f, -1, Day, false, MoveBudget.Planner, out var miss), "vanilla refused");
        AssertEqual(MoveOutcome.Failed, miss, "a miss");
        var restricted = new FakeReconHost { Restricted = (x, z) => true };
        AssertFalse(MoveIssue.Issue(restricted, new MoveTracker(), in f, 300f, 0f, -1, Day, false, MoveBudget.Planner, out var blocked), "restricted ground");
        AssertEqual(MoveOutcome.Failed, blocked, "a miss");
        AssertEqual(0, restricted.Moves.Count, "and no call");
        var queuedUnit = Unit(2, 0f, 0f, cavalry: 0.1f, extra: FormationFlags.MoveQueued);
        AssertFalse(MoveIssue.Issue(took, new MoveTracker(), in queuedUnit, 300f, 0f, -1, Day, false, MoveBudget.Planner, out var queued), "queued behind a running order");
        AssertEqual(MoveOutcome.Queued, queued, "queued");
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

    private static void RowLoggedWhenOnlyTheReasonChanges()
    {
        // A mission re-targeted in place keeps its state but takes the new task's reason (ReconMissionLedger.Bind): that is a transition.
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var ledger = Ledger(s, (ReconKind.Picket, 1, 300f, 0f));
        var executor = new ReconExecutor();
        executor.Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        Resync(ledger, s, "main-axis", (ReconKind.Picket, 1, 300f, 0f));
        var tally = executor.Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        AssertEqual(1, tally.Rows.Count, "the new reason is a row");
        AssertContains(tally.Rows[0], "state=Assigned reason=main-axis", "with the state it kept");
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

    // The host reports whether it called ChangeCampaignOrders (not when the order is already set or queued): only those are counted.
    private static void OrderedCountsOnlyCallsTheHostMade()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.9f));
        var host = new FakeReconHost { OrderResult = CavalrySend.AlreadySet };
        var tally = new ReconExecutor().Review(s, Ledger(s, (ReconKind.Screen, 1, 300f, 0f)), host, new MoveTracker(), false);
        AssertEqual(1, host.Orders.Count, "the host was asked");
        AssertEqual(0, tally.Ordered, "and said it was already so: nothing counted");
    }

    // Mission ids restart when a ledger is rebuilt (a load, G6's Restore): a row's memory must not outlive its mission or the
    // new mission with the same id would log nothing.
    private static void RowMemoryIsPrunedWhenMissionsLeaveTheLedger()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var executor = new ReconExecutor();
        var first = Ledger(s, (ReconKind.Picket, 1, 300f, 0f));
        AssertEqual(1, executor.Review(s, first, new FakeReconHost(), new MoveTracker(), false).Rows.Count, "fixture: mission 1 logs");
        AssertEqual(0, executor.Review(s, new ReconMissionLedger(), new FakeReconHost(), new MoveTracker(), false).Rows.Count, "an empty ledger logs nothing");
        var rebuilt = Ledger(s, (ReconKind.Picket, 1, 300f, 0f));      // mission id 1 again
        AssertEqual(1, executor.Review(s, rebuilt, new FakeReconHost(), new MoveTracker(), false).Rows.Count, "the new mission 1 logs its own row");
    }

    // ---- verify F1: one owner per formation per review ----

    private static void ReconGiveUpsNeverFeedTheAllocatorOrG3()
    {
        // G4 matches a give-up by target point (ForceAllocator.GaveUpAt) and a recall point can be the capital, a CapitalGuard's
        // post: a scout that could not reach it must not cost the guard its role. Only mover-budget records count.
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(30, 50f, 8000f));
        var busy = ExecutorFixtures.Corps(31, 60f, 9000f);
        busy.Flags |= FormationFlags.InBattle;
        s.Own.Add(busy);
        var plan = ExecutorFixtures.HandPlan(s, new int[0]);
        plan.CapitalFloorStrength = 5000f;
        var sectors = ExecutorFixtures.Sectors(s);
        var tracker = new MoveTracker();
        tracker.RecordRefused(30, s.CapitalX, s.CapitalZ, Day - 2, budget: MoveBudget.Recon);
        tracker.RecordRefused(30, s.CapitalX, s.CapitalZ, Day - 1, budget: MoveBudget.Recon);   // the second refusal gives up
        var gaveUp = new List<GaveUpMove>();
        tracker.CollectGaveUp(gaveUp, Day);
        AssertEqual(0, gaveUp.Count, "a recon give-up is not in the set G3 and G4 read");
        var alloc = ForceAllocator.Allocate(plan, s, sectors, null, null, null, gaveUp);
        AssertTrue(alloc.TryGet(30, out var guard) && guard.Role == FormationRole.CapitalGuard && guard.Withheld, "the capital guard keeps its role");
        var planner = new MoveTracker();
        planner.RecordRefused(30, s.CapitalX, s.CapitalZ, Day - 2);
        planner.RecordRefused(30, s.CapitalX, s.CapitalZ, Day - 1);
        planner.CollectGaveUp(gaveUp, Day);
        AssertEqual(1, gaveUp.Count, "control: the same refusals on the planner budget are listed");
        // The same through an issued move that missed twice (Judge), the path the executor's own moves take.
        var issued = new MoveTracker();
        issued.RecordIssued(31, 500f, 0f, s.CapitalX, s.CapitalZ, Day - 4, queued: false, budget: MoveBudget.Recon);
        issued.Judge(31, 500f, 0f, 500f, 0f, Day - 3);
        issued.RecordIssued(31, 500f, 0f, s.CapitalX, s.CapitalZ, Day - 3, queued: false, budget: MoveBudget.Recon);
        AssertEqual(MoveOutcome.GaveUp, issued.Judge(31, 500f, 0f, 500f, 0f, Day - 2), "fixture: the second miss gives up");
        issued.CollectGaveUp(gaveUp, Day);
        AssertEqual(0, gaveUp.Count, "an issued recon move that gave up is not listed either");
        var records = new List<MoveRecord>(tracker.Records);
        AssertEqual(0, records.Count, "recon records are not persisted (a load restarts the scouts)");
        AssertEqual(1, new List<MoveRecord>(planner.Records).Count, "planner records are");
    }

    private static void OnlyTheRolesTheMoverSendsAreMoverRoles()
    {
        foreach (var role in new[] { FormationRole.CapitalGuard, FormationRole.MainEffort, FormationRole.Recover, FormationRole.DepotBuilder, FormationRole.Support })
            AssertTrue(CampaignMover.MovesRole(role), role + " is sent by the mover");
        foreach (var role in new[] { FormationRole.Hold, FormationRole.Reserve, FormationRole.Recon })
            AssertFalse(CampaignMover.MovesRole(role), role + " is not");
    }

    // The verifier's probe 2 through the real allocator, the sync, the mover and the recon pass: a Recalled probe corps the allocator
    // gives MainEffort. The mover sends it; the mission ends "reassigned"; recon sends nothing, and vanilla's refusal of a second
    // MoveUnitTo (an order is running, 9933-9945, 14519-14523) is never reached.
    private static void FormationReassignedToTheMoverIsReleasedFromItsMission()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 15000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 450f, 20000f));
        var ledger = RecalledProbeLedger(s);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2 });
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, new List<ReconTask>(), ledger, null);
        AssertTrue(alloc.TryGet(1, out var a1) && CampaignMover.MovesRole(a1.Role), "fixture: the allocator gives the recalled corps a role the mover sends (" + a1.Role + ")");
        ledger.Sync(s, new List<ReconTask>(), alloc);
        AssertEqual(ReconState.Cleared, ledger.Missions[0].State, "its mission ended");
        AssertEqual(ReconMissionLedger.ReasonReassigned, ledger.Missions[0].Reason, "as reassigned");

        var tracker = new MoveTracker();
        var host = new OneReviewHost { RefuseSecondMoveSameReview = true };
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        var tally = new ReconExecutor().Review(s, ledger, host, tracker, false);
        AssertEqual(1, host.Moves.FindAll(m => m.id == 1).Count, "one MoveUnitTo for the corps in the review");
        AssertEqual(0, tally.Issued + tally.Failed + tally.Retried + tally.Queued, "recon did nothing for it");
        AssertTrue(tracker.InTransit(1), "the mover's record is intact, so vanilla's offense and defence keep off it");
        MoveRecord record = default(MoveRecord);
        foreach (var r in tracker.Records) if (r.InstanceId == 1) record = r;
        AssertEqual(98, record.ObjectiveId, "and still carries the objective");
        AssertEqual(100f, record.TargetX, "and the mover's target");
    }

    // Belt and braces: the same overlap without the release (the mission still Recalled): recon does not send a formation the
    // tracker holds in transit.
    private static void InTransitFormationIsNotMovedByRecon()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 15000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 450f, 20000f));
        var ledger = RecalledProbeLedger(s);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2 });
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, new List<ReconTask>(), ledger, null);
        var tracker = new MoveTracker();
        var host = new OneReviewHost { RefuseSecondMoveSameReview = true };
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        AssertEqual(ReconState.Recalled, ledger.Missions[0].State, "fixture: the mission was not released");
        var tally = new ReconExecutor().Review(s, ledger, host, tracker, false);
        AssertEqual(1, host.Moves.FindAll(m => m.id == 1).Count, "no second MoveUnitTo");
        AssertEqual(1, tally.SkippedBusy, "counted as busy");
        AssertTrue(tracker.InTransit(1), "the mover's record stands");
    }

    private static void LowSupplyRecallThatBecomesRecoverIsReleasedAndMovedOnlyByTheMover()
    {
        var s = ExecutorFixtures.Wide();
        var tired = ExecutorFixtures.Corps(1, 350f, 15000f);
        tired.Supply01 = 0.3f;                                          // below the recall (0.5) and the Recover line (0.4)
        s.Own.Add(tired);
        s.Own.Add(ExecutorFixtures.Corps(2, 450f, 20000f));
        var ledger = new ReconMissionLedger();
        var task = new ReconTask { Kind = ReconKind.Screen, SectorId = 0, FromX = 300f, HoldX = 300f, Priority = 1f, Reason = "test" };
        var first = new Allocation { Alliance = 0 };
        first.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.Recon, SectorId = 0, TargetX = 300f, TargetZ = 0f, Reason = "test", ReconMissionId = -1 });
        ledger.Sync(s, new List<ReconTask> { task }, first);
        ledger.OnReview(s, null);
        AssertEqual(ReconState.Recalled, ledger.Missions[0].State, "fixture: recalled for low supply");
        var plan = ExecutorFixtures.HandPlan(s, new[] { 2 });
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, new List<ReconTask>(), ledger, null);
        AssertTrue(alloc.TryGet(1, out var a1), "assigned");
        AssertEqual(FormationRole.Recover, a1.Role, "fixture: the tired corps is a Recover formation");
        ledger.Sync(s, new List<ReconTask>(), alloc);
        AssertEqual(ReconMissionLedger.ReasonReassigned, ledger.Missions[0].Reason, "its mission is released");
        var tracker = new MoveTracker();
        var host = new OneReviewHost { RefuseSecondMoveSameReview = true };
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        new ReconExecutor().Review(s, ledger, host, tracker, false);
        AssertEqual(1, host.Moves.FindAll(m => m.id == 1).Count, "the mover's one move, not a second from recon");
    }

    private static void ReassignedScreenStillGetsItsStandDown()
    {
        var s = ExecutorFixtures.Wide();
        var scout = ExecutorFixtures.Corps(1, 350f, 15000f);
        scout.CavalryShare = 0.9f;
        s.Own.Add(scout);
        s.Own.Add(ExecutorFixtures.Corps(2, 450f, 20000f));
        var ledger = new ReconMissionLedger();
        var task = new ReconTask { Kind = ReconKind.Screen, SectorId = 0, FromX = 300f, HoldX = 300f, Priority = 1f, Reason = "test" };
        var recon = new Allocation { Alliance = 0 };
        recon.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.Recon, SectorId = 0, TargetX = 300f, TargetZ = 0f, Reason = "test", ReconMissionId = -1 });
        ledger.Sync(s, new List<ReconTask> { task }, recon);
        var executor = new ReconExecutor();
        var first = new OneReviewHost();
        executor.Review(s, ledger, first, new MoveTracker(), false);
        AssertEqual(ReconOrders.ScoutingOrder, first.Orders[0].code, "fixture: Scouting given");
        AssertTrue(ledger.Missions[0].StandDownDue, "and owed");

        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2 });
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, new List<ReconTask>(), ledger, null);
        AssertTrue(alloc.TryGet(1, out var a1) && CampaignMover.MovesRole(a1.Role), "fixture: reassigned to a mover role");
        ledger.Sync(s, new List<ReconTask>(), alloc);
        var second = new OneReviewHost();
        var tracker = new MoveTracker();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), second, tracker, ExecutorFixtures.State());
        executor.Review(s, ledger, second, tracker, false);
        AssertEqual(1, second.Orders.Count, "one cavalry call");
        AssertEqual(ReconOrders.NoCavalryOrder, second.Orders[0].code, "taking Scouting off");
        AssertEqual(1, second.Moves.FindAll(m => m.id == 1).Count, "and the mover's move is the only one");
    }

    // ---- verify F2: the recon flag off ----

    private static void ReconFlagOffEndsEveryMissionAndMovesNothing()
    {
        var s = World();
        s.HasCapital = true;
        s.CapitalX = 10f;
        s.CapitalZ = 0f;
        var tired = Unit(1, 300f, 0f, cavalry: 0.8f);
        tired.Supply01 = 0.3f;
        s.Own.Add(tired);
        s.Own.Add(Unit(2, 0f, 40f, cavalry: 0.1f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f), (ReconKind.Picket, 2, 300f, 40f));
        ledger.OnReview(s, null);
        AssertEqual(ReconState.Recalled, ledger.Missions[0].State, "fixture: one mission is Recalled (Sync alone leaves it)");
        ledger.ReleaseAll(ReconMissionLedger.ReasonReconOff);
        foreach (var m in ledger.Missions)
        {
            AssertEqual(ReconState.Cleared, m.State, "every mission ended, " + m.Kind);
            AssertEqual(ReconMissionLedger.ReasonReconOff, m.Reason, "with the flag's reason");
        }
        var host = new FakeReconHost();
        var tally = new ReconExecutor().Review(s, ledger, host, new MoveTracker(), false);
        AssertEqual(0, host.Moves.Count, "nothing is moved: a recalled formation is vanilla's where it stands");
        AssertEqual(0, tally.Issued, "nor counted");
        foreach (var o in host.Orders) AssertEqual(ReconOrders.NoCavalryOrder, o.code, "only stand-down orders are sent");
    }

    private static void ReconSyncAndFlagOffWiringPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string sync = BodyOf(src, "private static void SyncRecon(");
        AssertTrue(HasLine(sync, "Recon[snapshot.Alliance].ReleaseAll(ReconMissionLedger.ReasonReconOff);"), "the flag off ends every mission, Recalled ones included");
        AssertTrue(HasLine(sync, "Recon[snapshot.Alliance].Sync(snapshot, ReconTasks[snapshot.Alliance], allocation);"), "the flag on binds the allocation");
        int flag = sync.IndexOf("FlagOn(Plugin.EnableCampaignReconnaissance)", StringComparison.Ordinal);
        AssertTrue(flag >= 0 && flag < sync.IndexOf("ReleaseAll(", StringComparison.Ordinal) && sync.IndexOf("ReleaseAll(", StringComparison.Ordinal) > sync.IndexOf("else", StringComparison.Ordinal), "ReleaseAll is the else of the flag");
    }

    // ---- verify F3: a stand-down the executors owe survives the days they miss ----

    private static void ClearedScreenWaitsForItsStandDownAcrossDaysTheExecutorsMiss()
    {
        var s = World();
        s.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        var executor = new ReconExecutor();
        var first = new FakeReconHost();
        executor.Review(s, ledger, first, new MoveTracker(), false);
        AssertEqual(ReconOrders.ScoutingOrder, first.Orders[0].code, "Scouting given");
        AssertTrue(ledger.Missions[0].StandDownDue, "and owed");

        // The mission expires on a day the executors do not run (the W&L pause, a stale plan): OnReview alone, twice.
        var late = World(Day + 60);
        late.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        ledger.OnReview(late, null);
        AssertEqual(ReconState.Cleared, ledger.Missions[0].State, "expired");
        for (int d = 61; d <= 63; d++)
        {
            var day = World(Day + d);
            day.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
            ledger.OnReview(day, null);
        }
        AssertEqual(1, ledger.Missions.Count, "the cleared mission is still there after several review days without an executor pass");
        AssertEqual(ReconState.Cleared, ledger.Missions[0].State, "still Cleared, never re-derived into a live state");

        var back = World(Day + 64);
        back.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        var host = new FakeReconHost();
        executor.Review(back, ledger, host, new MoveTracker(), false);
        AssertEqual(1, host.Orders.Count, "the first executor pass takes the order off");
        AssertEqual(ReconOrders.NoCavalryOrder, host.Orders[0].code, "120");
        AssertEqual(0, host.Moves.Count, "and moves nothing");
        AssertFalse(ledger.Missions[0].StandDownDue, "the debt is paid");
        ledger.OnReview(back, null);
        AssertEqual(0, ledger.Missions.Count, "so the mission is dropped at the next review");
    }

    private static void PendingStandDownIsBoundedAtMaxMissionDays()
    {
        var s = World();
        s.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        new ReconExecutor().Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        var late = World(Day + ReconMissionLedger.MaxMissionDays);
        late.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        ledger.OnReview(late, null);                                       // expired
        int cleared = late.DaySerial;
        var next = World(cleared + 1);
        next.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        ledger.OnReview(next, null);                                       // first review that sees it Cleared: stamps the day
        AssertEqual(next.DaySerial, ledger.Missions[0].ClearedDay, "stamped");
        var edge = World(next.DaySerial + ReconMissionLedger.MaxMissionDays - 1);
        edge.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        ledger.OnReview(edge, null);
        AssertEqual(1, ledger.Missions.Count, "kept one day short of the bound");
        var over = World(next.DaySerial + ReconMissionLedger.MaxMissionDays);
        over.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        ledger.OnReview(over, null);
        AssertEqual(0, ledger.Missions.Count, "dropped at MaxMissionDays: the executors may be off for good");
    }

    private static void PendingStandDownIsLetGoWhenItCanNeverBeSent()
    {
        // The formation is gone.
        var s = World();
        s.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        var executor = new ReconExecutor();
        executor.Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        var gone = World(Day + 1);
        ledger.OnReview(gone, null);                                        // unit 1 missing: Cleared "missing"
        var host = new FakeReconHost();
        executor.Review(gone, ledger, host, new MoveTracker(), false);
        AssertEqual(0, host.Orders.Count, "no formation to order");
        AssertFalse(ledger.Missions[0].StandDownDue, "nothing is owed to a formation that is not there");

        // Its chain is no longer None (N2, a known limit): level -1 would reach the player's units, so it is never sent; the
        // group keeps Scouting until a readiness reset (113960).
        var s2 = World();
        s2.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        var ledger2 = Ledger(s2, (ReconKind.Screen, 1, 300f, 0f));
        var ex2 = new ReconExecutor();
        ex2.Review(s2, ledger2, new FakeReconHost(), new MoveTracker(), false);
        ledger2.Sync(s2, new List<ReconTask>(), new Allocation { Alliance = 0 });   // cleared "unassigned"
        var carrying = new FakeReconHost();
        carrying.Chain[1] = WlChainClass.CarryingGroup;
        ex2.Review(s2, ledger2, carrying, new MoveTracker(), false);
        AssertEqual(0, carrying.Orders.Count, "never a cavalry call on a group in the player's chain");
        AssertFalse(ledger2.Missions[0].StandDownDue, "and the debt is let go");
        ledger2.OnReview(s2, null);
        AssertEqual(0, ledger2.Missions.Count, "so the mission is dropped");
    }

    private static void StandDownIsPaidForTheNamedFormationOnly()
    {
        var s = World();
        s.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        s.Own.Add(Unit(2, 300f, 40f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f), (ReconKind.Screen, 2, 300f, 40f));
        new ReconExecutor().Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        ledger.NoteStoodDown(1);
        foreach (var m in ledger.Missions) AssertEqual(m.InstanceId == 2, m.StandDownDue, "unit " + m.InstanceId + " owes only if it is unit 2");
    }

    private static void QueuedStandDownIsKeptUntilItIsDelivered()
    {
        var s = World();
        s.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        var executor = new ReconExecutor();
        executor.Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        ledger.Sync(s, new List<ReconTask>(), new Allocation { Alliance = 0 });
        var queued = new FakeReconHost { OrderResult = CavalrySend.QueuedOther };
        executor.Review(s, ledger, queued, new MoveTracker(), false);
        AssertTrue(ledger.Missions[0].StandDownDue, "a queued 121 is still waiting: the stand-down was not delivered");
        ledger.OnReview(s, null);
        AssertEqual(1, ledger.Missions.Count, "the mission stays");
        var done = new FakeReconHost { OrderResult = CavalrySend.AlreadySet };
        executor.Review(s, ledger, done, new MoveTracker(), false);
        AssertFalse(ledger.Missions[0].StandDownDue, "delivered (the wanted mode is set) on a later pass");
    }

    private static void RetaskedScreenSupersedesTheDebtOfItsClearedMission()
    {
        var s = World();
        s.Own.Add(Unit(1, 0f, 0f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        var executor = new ReconExecutor();
        executor.Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        Resync(ledger, s, (ReconKind.Picket, 1, 400f, 0f));                  // old screen Cleared "re-tasked", the picket active
        var host = new FakeReconHost();
        executor.Review(s, ledger, host, new MoveTracker(), false);
        AssertEqual(ReconOrders.NoCavalryOrder, host.Orders[0].code, "the picket is no scout: Scouting comes off");
        foreach (var m in ledger.Missions) AssertFalse(m.StandDownDue, "nothing is owed once it has");
        ledger.OnReview(s, null);
        AssertEqual(1, ledger.Missions.Count, "only the picket remains");

        // A cleared screen (Scouting given) and a new screen mission for the same unit in one review: the new mission carries the
        // debt, the cleared one stops owing and is dropped at the next review.
        var s2 = World();
        s2.Own.Add(Unit(1, 0f, 0f, cavalry: 0.8f));
        var l2 = Ledger(s2, (ReconKind.Screen, 1, 300f, 0f));
        var e2 = new ReconExecutor();
        e2.Review(s2, l2, new FakeReconHost(), new MoveTracker(), false);
        l2.Sync(s2, new List<ReconTask>(), new Allocation { Alliance = 0 });             // A: Cleared "unassigned", still owing
        Resync(l2, s2, (ReconKind.Screen, 1, 500f, 0f));                                 // B: a new screen mission
        AssertEqual(2, l2.Missions.Count, "fixture: the cleared and the new mission");
        var host2 = new FakeReconHost();
        e2.Review(s2, l2, host2, new MoveTracker(), false);
        AssertEqual(ReconOrders.ScoutingOrder, host2.Orders[0].code, "the active screen keeps Scouting");
        int owing = 0;
        foreach (var m in l2.Missions) if (m.InstanceId == 1 && m.StandDownDue) owing++;
        AssertEqual(1, owing, "one mission owes the stand-down of unit 1");
        l2.OnReview(s2, null);
        AssertEqual(1, l2.Missions.Count, "the cleared one is gone");
    }

    private static void StandDownDebtRoundTripsThroughRestore()
    {
        var s = World();
        s.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
        new ReconExecutor().Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
        ledger.Sync(s, new List<ReconTask>(), new Allocation { Alliance = 0 });
        var late = World(Day + 1);
        late.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
        ledger.OnReview(late, null);                                          // stamps ClearedDay
        var saved = new List<ReconMission>(ledger.Missions);
        var loaded = new ReconMissionLedger();
        loaded.Restore(saved);
        AssertEqual(1, loaded.Missions.Count, "restored");
        AssertTrue(loaded.Missions[0].StandDownDue, "the owed stand-down survives a save");
        AssertEqual(late.DaySerial, loaded.Missions[0].ClearedDay, "and so does the day that bounds it");
        var host = new FakeReconHost();
        new ReconExecutor().Review(late, loaded, host, new MoveTracker(), false);
        AssertEqual(ReconOrders.NoCavalryOrder, host.Orders[0].code, "and is paid after the load");
    }

    // ---- verify N4: the mover path (the refactor kept it; these were unpinned before) ----

    private static void MoverRefusalListsNothingAndKeepsTheObjective()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 15000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        var tracker = new MoveTracker();
        var host = new FakeMoverHost { MoveResult = false };
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        AssertEqual(1, host.Moves.Count, "vanilla was asked");
        AssertEqual(1, tally.Failed, "and refused");
        AssertEqual(0, host.Listed.Count, "a refused move is not listed in unitsinoffensiveoperations");
        var records = new List<MoveRecord>(tracker.Records);
        AssertEqual(1, records.Count, "the refusal is recorded");
        AssertEqual(98, records[0].ObjectiveId, "with the objective the move was for, so G3 can leave the formation out of that mass if it gives up");
    }

    // ---- verify r2 R1/R2: a queued record settles ----

    // R2-Q: day 1 the screen is picked while vanilla runs a DefensiveMovingOrder on it (MoveQueued): a queued record. Day 2 the order has
    // ended. The mover's pass (JudgeLastReview) settles it first when it runs; the recon pass settles it itself when it does not.
    private static void QueuedReconMoveIsResentOnceItsOrderHasEnded()
    {
        var s1 = World();
        s1.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f, extra: FormationFlags.MoveQueued));
        var ledger = Ledger(s1, (ReconKind.Picket, 1, 300f, 0f));
        var tracker = new MoveTracker();
        var executor = new ReconExecutor();
        var day1 = executor.Review(s1, ledger, new FakeReconHost(), tracker, false);
        AssertEqual(1, day1.Queued, "fixture: queued behind the running order");
        AssertTrue(tracker.InTransit(1), "and in transit");

        var running = World(Day + 1);
        running.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f, extra: FormationFlags.MoveQueued));
        var stillRunning = new FakeReconHost();
        executor.Review(running, ledger, stillRunning, tracker, false);
        AssertEqual(0, stillRunning.Moves.Count, "the order is still running: no second move");
        AssertTrue(tracker.InTransit(1), "and the record stands");

        var ended = World(Day + 2);
        ended.Own.Add(Unit(1, 0f, 0f, cavalry: 0.1f));
        var host = new FakeReconHost();
        var tally = executor.Review(ended, ledger, host, tracker, false);
        AssertEqual(1, host.Moves.Count, "the order ended: the move is sent");
        AssertEqual(1, tally.Issued, "as an issue");
        AssertEqual(300f, host.Moves[0].x, "to the post");
    }

    // The same through the real mover pass, which runs first in a review and settles the ended order before recon's pass.
    private static void QueuedMoverMoveIsResentWhenItsOrderEndedAndTheRoleStillSends()
    {
        var s = ExecutorFixtures.Wide();
        var pending = ExecutorFixtures.Corps(1, 350f, 15000f);
        pending.Flags |= FormationFlags.MoveQueued;
        s.Own.Add(pending);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, new List<ReconTask>(), null, null);
        AssertTrue(alloc.TryGet(1, out var a1) && a1.Role == FormationRole.MainEffort, "fixture: a main effort");
        var tracker = new MoveTracker();
        var queuedHost = new FakeMoverHost();
        var first = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), queuedHost, tracker, ExecutorFixtures.State());
        AssertEqual(1, first.Queued, "fixture: queued");
        AssertEqual(0, queuedHost.Moves.Count, "no move while the order runs");

        var s2 = ExecutorFixtures.Wide(1);
        s2.Own.Add(ExecutorFixtures.Corps(1, 350f, 15000f));         // the order ended
        var plan2 = ExecutorFixtures.HandPlan(s2, new[] { 1 });
        var alloc2 = ForceAllocator.Allocate(plan2, s2, ExecutorFixtures.Sectors(s2), null, new List<ReconTask>(), null, null);
        var host = new FakeMoverHost();
        var second = CampaignMover.Review(ExecutorFixtures.Inputs(s2, plan2, alloc2, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        AssertEqual(1, host.Moves.Count, "the order ended and the role still sends it: the mover sends it now");
        AssertEqual(1, second.Issued, "as an issue");
        AssertTrue(tracker.InTransit(1), "recorded as issued");
    }

    // R2-Q2: the mission ends while its move waits behind a running order. The record is released (not in transit, not withheld from
    // vanilla's pools), though the order is still running.
    private static void QueuedReconMoveIsReleasedWhenItsMissionEnds()
    {
        var w = ExecutorFixtures.PublishedWorld(out var tracker);              // the withhold view reads this tracker
        var s = World();
        s.Own.Add(Unit(w.FreeHold, 0f, 0f, cavalry: 0.1f, extra: FormationFlags.MoveQueued));
        var ledger = Ledger(s, (ReconKind.Picket, w.FreeHold, 300f, 0f));
        var executor = new ReconExecutor();
        executor.Review(s, ledger, new FakeReconHost(), tracker, false);
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.FreeHold), "fixture: the queued formation is withheld from vanilla's offence");
        AssertTrue(CampaignWithholdView.WithheldFromDefense(0, w.FreeHold, 0f, 0f), "and from its defence");

        var later = World(Day + 25);
        later.Own.Add(Unit(w.FreeHold, 0f, 0f, cavalry: 0.1f, extra: FormationFlags.MoveQueued));  // the vanilla order is still running
        ledger.Sync(later, new List<ReconTask>(), new Allocation { Alliance = 0 });                // the mission is over
        executor.Review(later, ledger, new FakeReconHost(), tracker, false);
        AssertFalse(tracker.InTransit(w.FreeHold), "the record is released with the mission");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.FreeHold), "so vanilla's offence has the formation back");
        AssertFalse(CampaignWithholdView.WithheldFromDefense(0, w.FreeHold, 0f, 0f), "and its defence");
        CampaignScope.ResetAll();
    }

    // R2: a MainEffort move queued behind a vanilla order whose role then becomes one the mover does not send (through the real
    // allocator): the record settles at once, though the order is still running, and the formation is vanilla's again.
    private static void QueuedMoverMoveSettlesWhenItsRoleNoLongerSends()
    {
        var s = ExecutorFixtures.Wide();
        var pending = ExecutorFixtures.Corps(1, 350f, 15000f);
        pending.Flags |= FormationFlags.MoveQueued;
        s.Own.Add(pending);
        s.Own.Add(ExecutorFixtures.Corps(2, 450f, 20000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2 });
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, new List<ReconTask>(), null, null);
        AssertTrue(alloc.TryGet(1, out var a1) && CampaignMover.MovesRole(a1.Role), "fixture: a mover role on day 1 (" + a1.Role + ")");
        var tracker = new MoveTracker();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), new FakeMoverHost(), tracker, ExecutorFixtures.State());
        AssertTrue(tracker.InTransit(1), "fixture: queued");

        var s2 = ExecutorFixtures.Wide(1);
        var still = ExecutorFixtures.Corps(1, 350f, 15000f);
        still.Flags |= FormationFlags.MoveQueued;                       // vanilla's order is still running
        s2.Own.Add(still);
        s2.Own.Add(ExecutorFixtures.Corps(2, 450f, 20000f));
        var plan2 = ExecutorFixtures.HandPlan(s2, new[] { 2 });
        var alloc2 = ForceAllocator.Allocate(plan2, s2, ExecutorFixtures.Sectors(s2), null, new List<ReconTask>(), null, null);
        AssertTrue(alloc2.TryGet(1, out var b1) && !CampaignMover.MovesRole(b1.Role), "fixture: a role the mover does not send on day 2 (" + b1.Role + ")");
        CampaignMover.Review(ExecutorFixtures.Inputs(s2, plan2, alloc2, ExecutorFixtures.OpenTempo()), new FakeMoverHost(), tracker, ExecutorFixtures.State());
        AssertFalse(tracker.InTransit(1), "the queued record settled: not in transit, not withheld from vanilla's pools");
    }

    // The one rule in the tracker: ended order or unwanted settles; wanted with the order running does not; the other budget's record is
    // untouched; the bound drops what nobody touched.
    private static void QueuedRecordsSettleByOneRuleAndExpire()
    {
        var t = new MoveTracker();
        t.RecordIssued(1, 500f, 0f, 100f, 0f, Day, queued: true);
        AssertFalse(t.SettleQueued(1, MoveBudget.Planner, wanted: true, orderRunning: true), "wanted and the order running: stays");
        AssertFalse(t.SettleQueued(1, MoveBudget.Recon, wanted: false, orderRunning: false), "the other budget's record is not its business");
        AssertTrue(t.InTransit(1), "still queued");
        AssertTrue(t.SettleQueued(1, MoveBudget.Planner, wanted: false, orderRunning: true), "not wanted: settles though the order runs");
        AssertFalse(t.InTransit(1), "released");
        t.RecordIssued(2, 500f, 0f, 100f, 0f, Day, queued: true, budget: MoveBudget.Recon);
        AssertTrue(t.SettleQueued(2, MoveBudget.Recon, wanted: true, orderRunning: false), "the order ahead ended: settles");
        t.RecordIssued(3, 500f, 0f, 100f, 0f, Day, queued: false);
        AssertFalse(t.SettleQueued(3, MoveBudget.Planner, wanted: false, orderRunning: false), "only a Queued record: an issued move is judged, not settled");
        AssertTrue(t.InTransit(3), "still issued");

        var e = new MoveTracker();
        e.RecordIssued(4, 500f, 0f, 100f, 0f, Day, queued: true);
        e.RecordIssued(5, 500f, 0f, 100f, 0f, Day, queued: false);
        e.SettleExpiredQueued(Day + MoveTracker.QueuedExpiryDays - 1);
        AssertTrue(e.InTransit(4), "one day short of the bound");
        e.SettleExpiredQueued(Day + MoveTracker.QueuedExpiryDays);
        AssertFalse(e.InTransit(4), "a queued record nobody touched is dropped at the bound");
        AssertTrue(e.InTransit(5), "an issued move is not (the judgement owns it)");
        e.RecordIssued(6, 500f, 0f, 100f, 0f, Day, queued: true);
        e.RecordIssued(6, 500f, 0f, 100f, 0f, Day + 10, queued: true);   // re-recorded by its owner: the clock restarts
        e.SettleExpiredQueued(Day + MoveTracker.QueuedExpiryDays);
        AssertTrue(e.InTransit(6), "a record its owner re-records every review does not age out");
        AssertEqual(14, MoveTracker.QueuedExpiryDays, "the bound");
    }

    // Verify r3 N1 (the verifier's R3-G): Failed, a retry queued behind an order, the order ends and the record settles, the re-issue is
    // still the retry, and its miss gives up (it did before SettleQueued existed). A first attempt that never failed settles to nothing.
    private static void QueuedRetryKeepsItsCountWhenItSettles()
    {
        var t = new MoveTracker();
        t.RecordIssued(1, 500f, 0f, 100f, 0f, Day, queued: false);
        AssertEqual(MoveOutcome.Failed, t.Judge(1, 500f, 0f, 500f, 0f, Day + 1), "fixture: the first attempt missed");
        t.RecordIssued(1, 500f, 0f, 100f, 0f, Day + 1, queued: true);                     // the retry queued behind an order
        AssertTrue(t.SettleQueued(1, MoveBudget.Planner, wanted: true, orderRunning: false), "the order ended: settled");
        AssertFalse(t.InTransit(1), "no longer in transit");
        AssertEqual(MoveOutcome.Retried, t.IssueOutcome(1, 100f, 0f, queued: false), "and the re-issue is still the retry");
        t.RecordIssued(1, 500f, 0f, 100f, 0f, Day + 2, queued: false);
        AssertEqual(MoveOutcome.GaveUp, t.Judge(1, 500f, 0f, 500f, 0f, Day + 3), "its miss gives up, as the unsettled sequence does");

        var fresh = new MoveTracker();
        fresh.RecordIssued(2, 500f, 0f, 100f, 0f, Day, queued: true);                     // a first attempt that queued
        AssertTrue(fresh.SettleQueued(2, MoveBudget.Planner, wanted: true, orderRunning: false), "settled");
        AssertEqual(MoveOutcome.Issued, fresh.IssueOutcome(2, 100f, 0f, queued: false), "a first attempt that never failed re-issues as a fresh move");

        var unwanted = new MoveTracker();
        unwanted.RecordIssued(3, 500f, 0f, 100f, 0f, Day, queued: false);
        unwanted.Judge(3, 500f, 0f, 500f, 0f, Day + 1);
        unwanted.RecordIssued(3, 500f, 0f, 100f, 0f, Day + 1, queued: true);
        AssertTrue(unwanted.SettleQueued(3, MoveBudget.Planner, wanted: false, orderRunning: true), "nobody wants the move any more");
        AssertEqual(MoveOutcome.Issued, unwanted.IssueOutcome(3, 100f, 0f, queued: false), "the record is gone, not kept as a failure");
        AssertEqual(0, new List<GaveUpMove>(GaveUpFrom(unwanted)).Count, "and nothing is given up on");
    }

    private static List<GaveUpMove> GaveUpFrom(MoveTracker t)
    {
        var list = new List<GaveUpMove>();
        t.CollectGaveUp(list, Day + 5);
        return list;
    }

    private static void QueuedSettleCallSitesPinned()
    {
        string mover = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMover.cs"), "private static void JudgeLastReview(");
        AssertTrue(HasLine(mover, "tracker.SettleExpiredQueued(s.DaySerial);"), "the mover applies the bound");
        AssertTrue(HasLine(mover, "tracker.SettleQueued(f.InstanceId, MoveBudget.Planner, RoleStillSends(allocation, f.InstanceId), orderRunning);"), "and the rule for its own records");
        AssertTrue(HasLine(mover, "tracker.SettleQueued(f.InstanceId, MoveBudget.Recon, wanted: true, orderRunning);"), "and the ended order for recon's, whose mission only the recon pass knows");
        AssertTrue(HasLine(mover, "if (!tracker.InTransit(f.InstanceId) || orderRunning) continue;"), "judging only what is still in transit and has no order ahead");
        string recon = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReconExecutor.cs"), "private void SettleQueuedMoves(");
        AssertTrue(HasLine(recon, "tracker.SettleExpiredQueued(s.DaySerial);"), "recon applies the bound too");
        AssertTrue(HasLine(recon, "tracker.SettleQueued(id, MoveBudget.Recon, ledger.IsRecon(id) || ledger.IsRecalling(id), orderRunning);"), "recon settles its own records by its missions");
        AssertTrue(HasLine(recon, "bool orderRunning = i >= 0 && (s.Own[i].Flags & FormationFlags.MoveQueued) != 0;"), "from the snapshot's running-order flag");
        string review = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReconExecutor.cs"), "public ReconTally Review(");
        AssertTrue(review.IndexOf("SettleQueuedMoves(s, ledger, tracker);", StringComparison.Ordinal) >= 0
            && review.IndexOf("SettleQueuedMoves(s, ledger, tracker);", StringComparison.Ordinal) < review.IndexOf("Move(s, missions", StringComparison.Ordinal), "before any move");
    }

    // ---- verify r2 N3 ----

    private static void GoneUnitReleasesTheDebtButMissingBattleUnitsKeepsIt()
    {
        foreach (var answer in new[] { CavalrySend.NoUnit, CavalrySend.NoBattleUnits })
        {
            var s = World();
            s.Own.Add(Unit(1, 300f, 0f, cavalry: 0.8f));
            var ledger = Ledger(s, (ReconKind.Screen, 1, 300f, 0f));
            var executor = new ReconExecutor();
            executor.Review(s, ledger, new FakeReconHost(), new MoveTracker(), false);
            ledger.Sync(s, new List<ReconTask>(), new Allocation { Alliance = 0 });        // cleared, 120 owed
            AssertTrue(ledger.Missions[0].StandDownDue, "fixture: owed");
            executor.Review(s, ledger, new FakeReconHost { OrderResult = answer }, new MoveTracker(), false);
            if (answer == CavalrySend.NoUnit) AssertFalse(ledger.Missions[0].StandDownDue, "the group is gone since the snapshot: nothing is owed to it");
            else AssertTrue(ledger.Missions[0].StandDownDue, "BattleUnits could not be reached (transient): the debt is kept for the next pass");
        }
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReconExecutorRuntime.cs");
        AssertTrue(HasLine(src, "if ((UnityEngine.Object)battleUnits == null) return CavalrySend.NoBattleUnits;"), "the host tells a missing BattleUnits from a missing unit");
    }

    private static void CavalryModeIsTheCodeMinusOneTwenty()
    {
        AssertEqual(0, ReconOrders.ModeOf(ReconOrders.NoCavalryOrder), "120 is mode 0");
        AssertEqual(1, ReconOrders.ModeOf(ReconOrders.ScoutingOrder), "121 is mode 1 (Scouting)");
    }

    // A 121 waits in the group's queue (cavalryorders 0, ordered 1) and 120 is wanted: ChangeCampaignOrders returns at 90738
    // because cavalryorders already is 0, and no call cancels a queued entry, so the call is not made and not counted; once the
    // 121 applies (both fields 1) the same wish sends 120. The mirror case (applied 1, queued 0, wanted 1) is the same.
    private static void QueuedOrderOfAnotherModeIsNeitherRepeatedNorCountedAndSelfHeals()
    {
        AssertEqual(ReconOrders.CavalryCall.QueuedOther, ReconOrders.CallFor(mode: 0, current: 0, ordered: 1), "wanted 0, 1 queued, 0 applied: vanilla would return at 90738");
        AssertEqual(ReconOrders.CavalryCall.QueuedOther, ReconOrders.CallFor(mode: 1, current: 1, ordered: 0), "the mirror case");
        AssertEqual(ReconOrders.CavalryCall.Send, ReconOrders.CallFor(mode: 0, current: 1, ordered: 1), "the queued order has applied: the wanted mode is sent");
        AssertEqual(ReconOrders.CavalryCall.Send, ReconOrders.CallFor(mode: 1, current: 0, ordered: 0), "nothing ordered: sent");
        AssertEqual(ReconOrders.CavalryCall.AlreadySet, ReconOrders.CallFor(mode: 1, current: 0, ordered: 1), "the wanted mode is queued: not queued again");
        AssertEqual(ReconOrders.CavalryCall.AlreadySet, ReconOrders.CallFor(mode: 1, current: 1, ordered: 1), "applied");
        AssertEqual(ReconOrders.CavalryCall.Send, ReconOrders.CallFor(mode: 0, current: 1, ordered: 0 + 1), "applied 1, ordered 1, wanted 0");
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReconExecutorRuntime.cs");
        string send = BodyOf(src, "public CavalrySend SendCavalryOrder(");
        AssertTrue(HasLine(send, "if (unit == null) return CavalrySend.NoUnit;"), "no such unit: the pass lets its debt go");
        AssertTrue(HasLine(send, "return CavalrySend.Sent;"), "a call made is reported as made");
        AssertTrue(HasLine(send, "var call = ReconOrders.CallFor(mode, unit.cavalryorders, unit.cavalryordersordered);"), "the host asks the pure rule with vanilla's two fields");
        AssertTrue(HasLine(send, "NoteQueuedOther(unit, mode);"), "the queued-other case is logged");
        int noted = send.IndexOf("NoteQueuedOther(unit, mode);", StringComparison.Ordinal);
        int already = send.IndexOf("if (call == ReconOrders.CavalryCall.AlreadySet)", StringComparison.Ordinal);
        AssertTrue(noted >= 0 && already > noted && send.Substring(noted, already - noted).Contains("return CavalrySend.QueuedOther;"), "and makes no call: vanilla would return at 90738 and the case would count as an order");
        AssertTrue(HasLine(send, "if (call == ReconOrders.CavalryCall.AlreadySet) return CavalrySend.AlreadySet;"), "and an order already set is not repeated");
        AssertTrue(HasLine(BodyOf(src, "private void NoteQueuedOther("), "if (!Noted.Add(unit.GetInstanceID())) return;"), "once per formation");
        AssertTrue(HasLine(BodyOf(src, "private static void Reset()"), "Noted.Clear();"), "and forgotten with the campaign");
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
        string send = BodyOf(src, "public CavalrySend SendCavalryOrder(");
        AssertTrue(HasLine(send, "int mode = ReconOrders.ModeOf(orderCode);"), "the mode comes from the pure helper");
        AssertTrue(HasLine(send, "if (call == ReconOrders.CavalryCall.AlreadySet) return CavalrySend.AlreadySet;"), "ChangeCampaignOrders tests cavalryorders only (90738), so a queued order would be queued again every review: the host compares what was ordered");
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

    // Corps 1 on a probe mission that an overmatched contact recalled (the verifier's probe 2 setup): hold (200, 0), recall point set.
    private static ReconMissionLedger RecalledProbeLedger(CampaignSnapshot s)
    {
        var ledger = new ReconMissionLedger();
        var task = new ReconTask { Kind = ReconKind.Probe, SectorId = 0, FromX = 200f, FromZ = 0f, HoldX = 200f, HoldZ = 0f, Priority = 1f, Reason = "probe" };
        var a0 = new Allocation { Alliance = 0 };
        a0.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.Recon, SectorId = 0, TargetX = 200f, TargetZ = 0f, Reason = "probe", ReconMissionId = -1 });
        ledger.Sync(s, new List<ReconTask> { task }, a0);
        ledger.OnProbeContact(1, new ContactEvidenceOutput { Evidence = ContactEvidence.OvermatchedContact }, s.DaySerial);
        ledger.OnReview(s, null);
        AssertEqual(ReconState.Recalled, ledger.Missions[0].State, "fixture: the probe is recalled");
        return ledger;
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
        Resync(ledger, s, "test", missions);
    }

    private static void Resync(ReconMissionLedger ledger, CampaignSnapshot s, string reason, params (ReconKind kind, int id, float x, float z)[] missions)
    {
        var tasks = new List<ReconTask>();
        var a = new Allocation { Alliance = 0 };
        int sector = 0;
        foreach (var m in missions)
        {
            var task = new ReconTask { Kind = m.kind, SectorId = sector++, FromX = m.x, FromZ = m.z, HoldX = m.x, HoldZ = m.z, Priority = 1f, Reason = reason };
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
    public CavalrySend OrderResult = CavalrySend.Sent;
    public readonly List<(int id, float x, float z)> Moves = new List<(int id, float x, float z)>();
    public readonly List<(int id, int code)> Orders = new List<(int id, int code)>();
    public readonly List<int> Raised = new List<int>();

    public WlChainClass LiveChain(int instanceId) { return Chain.TryGetValue(instanceId, out var c) ? c : WlChainClass.None; }
    public bool TargetRestricted(float x, float z) { return Restricted(x, z); }
    public bool PackageLocked(int instanceId) { return Locked.Contains(instanceId); }
    public bool Move(int instanceId, float x, float z) { Moves.Add((instanceId, x, z)); return MoveResult; }
    public void RaiseTransportModes(int instanceId) { Raised.Add(instanceId); }
    public CavalrySend SendCavalryOrder(int instanceId, int orderCode) { Orders.Add((instanceId, orderCode)); return OrderResult; }
}

// One fake for the mover pass and the recon pass of one review. RefuseSecondMoveSameReview models MoveUnitTo's
// DefensiveMovingOrder.OrderRunning(unit) (14519-14523, 9933-9945): a unit that took a move this review has an order running, so a
// second MoveUnitTo for it returns false.
internal sealed class OneReviewHost : IMoverHost, IReconHost
{
    public bool RefuseSecondMoveSameReview;
    public readonly List<(int id, float x, float z)> Moves = new List<(int id, float x, float z)>();
    public readonly List<(int id, int code)> Orders = new List<(int id, int code)>();
    private readonly HashSet<int> _running = new HashSet<int>();

    public bool TargetRestricted(float x, float z) { return false; }
    public bool PackageLocked(int instanceId) { return false; }
    public bool Move(int instanceId, float x, float z)
    {
        Moves.Add((instanceId, x, z));
        if (RefuseSecondMoveSameReview && _running.Contains(instanceId)) return false;
        _running.Add(instanceId);
        return true;
    }
    public void RaiseTransportModes(int instanceId) { }
    public int StateAllianceAt(float x, float z) { return x <= 150f ? 1 : 0; }
    public bool AtWar(int otherAlliance) { return otherAlliance == 1; }
    public bool IssuePlayerOrder(int instanceId, float x, float z) { return true; }
    public void ListOffensive(int instanceId, bool listed) { }
    public WlChainClass LiveChain(int instanceId) { return WlChainClass.None; }
    public CavalrySend SendCavalryOrder(int instanceId, int orderCode) { Orders.Add((instanceId, orderCode)); return CavalrySend.Sent; }
}
