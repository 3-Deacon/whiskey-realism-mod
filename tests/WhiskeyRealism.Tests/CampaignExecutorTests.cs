using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G5a (docs/superpowers/plans/2026-10-15-campaign-planner.md § G5a): the move tracker, the
// withhold view the availability Postfixes read, the pure mover pass (CampaignMover) against a fake host,
// the objective / theater-box / importance rules, and the runtime pins. The chapter tempo gate and the
// pre-war gate have their own class (CampaignTempoGateTests); both share ExecutorFixtures.
internal static class CampaignExecutorTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("MoveCapRespectsMaxMovesPerDay", MoveCapRespectsMaxMovesPerDay),
            ("MoveConfirmedWhenWaypointChangesTowardTarget", MoveConfirmedWhenWaypointChangesTowardTarget),
            ("MoveFailedThenRetriedThenGaveUp", MoveFailedThenRetriedThenGaveUp),
            ("QueuedMoveIsNotAFailure", QueuedMoveIsNotAFailure),
            ("RefusedMoveCountsAsFailedNotQueued", RefusedMoveCountsAsFailedNotQueued),
            ("MoveRecordsKeyedByUnitKey", MoveRecordsKeyedByUnitKey),
            ("MoveTrackersArePerAllianceAndPerCampaign", MoveTrackersArePerAllianceAndPerCampaign),
            ("PlayerCommandChainNeverMoved", PlayerCommandChainNeverMoved),
            ("MapGroupPlayerGetsOrderNotMove", MapGroupPlayerGetsOrderNotMove),
            ("CarryingGroupMovedNormally", CarryingGroupMovedNormally),
            ("MoverDispatchesTheRolesInPriorityOrder", MoverDispatchesTheRolesInPriorityOrder),
            ("MoverListsMainEffortAndSupportInTheOffensiveList", MoverListsMainEffortAndSupportInTheOffensiveList),
            ("MoverSkipsBusyLockedAndArrivedFormations", MoverSkipsBusyLockedAndArrivedFormations),
            ("MoverSkipsARestrictedTarget", MoverSkipsARestrictedTarget),
            ("MoverQueuesBehindARunningOrder", MoverQueuesBehindARunningOrder),
            ("MoverJudgesLastReviewsMovesFirst", MoverJudgesLastReviewsMovesFirst),
            ("MoverDoesNotJudgeAMoveWhoseOrderIsStillPending", MoverDoesNotJudgeAMoveWhoseOrderIsStillPending),
            ("WlSideMovableGetsVanillaAiTransportDefault", WlSideMovableGetsVanillaAiTransportDefault),
            ("PlayerCommandTransportFlagsUntouched", PlayerCommandTransportFlagsUntouched),
            ("RecoverRemovedFromOffensiveList", RecoverRemovedFromOffensiveList),
            ("WithheldFromOffenseIsWithheldTransitMassOrDeferredSupport", WithheldFromOffenseIsWithheldTransitMassOrDeferredSupport),
            ("HoldAtNeedAvailableForDefenseInOwnSector", HoldAtNeedAvailableForDefenseInOwnSector),
            ("HoldAtNeedWithheldFromDefenseOutsideSector", HoldAtNeedWithheldFromDefenseOutsideSector),
            ("HoldAboveNeedNeverWithheld", HoldAboveNeedNeverWithheld),
            ("StalePlanIgnoredByPatches", StalePlanIgnoredByPatches),
            ("ExecutorContextPublishesAtomicallyWithThePlan", ExecutorContextPublishesAtomicallyWithThePlan),
            ("WithholdViewAllocatesNothingAfterWarmup", WithholdViewAllocatesNothingAfterWarmup),
            ("TheaterBoxKeepsUnitInside", TheaterBoxKeepsUnitInside),
            ("TheaterBoxRateLimited", TheaterBoxRateLimited),
            ("TheaterWritesFollowTheRoles", TheaterWritesFollowTheRoles),
            ("ObjectiveWriteOnlyWhenChanged", ObjectiveWriteOnlyWhenChanged),
            ("ObjectiveResetOnlyWhenModWritten", ObjectiveResetOnlyWhenModWritten),
            ("ImportanceRedirectOnlyForMainOrSupportSectors", ImportanceRedirectOnlyForMainOrSupportSectors),
            ("ImportanceRedirectRequiresVanillaGates", ImportanceRedirectRequiresVanillaGates),
            ("ExecutorFilesHoldNoUnityTypes", ExecutorFilesHoldNoUnityTypes),
            ("MoverHostNeverTouchesTheDefensiveList", MoverHostNeverTouchesTheDefensiveList),
            ("MoverRuntimeStatementsPinned", MoverRuntimeStatementsPinned),
            ("SteeringRuntimeStatementsPinned", SteeringRuntimeStatementsPinned),
            ("AvailabilityPatchesPinned", AvailabilityPatchesPinned),
            ("ImportancePatchPinned", ImportancePatchPinned),
            ("PickObjectiveStandsDownForTheExecutors", PickObjectiveStandsDownForTheExecutors),
            ("ReviewExecuteWiringPinned", ReviewExecuteWiringPinned),
            ("ReviewPlanStampsTheClockAndPublishesTheContext", ReviewPlanStampsTheClockAndPublishesTheContext),
            ("TempoWorldReadsVanillasInputs", TempoWorldReadsVanillasInputs),
            ("MoverHostReadsVanillasPublicFacts", MoverHostReadsVanillasPublicFacts),
            ("SteeringRuntimeWritesWhatTheRulesDecide", SteeringRuntimeWritesWhatTheRulesDecide),
            ("SnapshotReadsTheIdleClock", SnapshotReadsTheIdleClock),
        });
    }

    // ---- the move tracker ----

    private static void MoveCapRespectsMaxMovesPerDay()
    {
        var t = new MoveTracker();
        int day = 100;
        for (int i = 1; i <= MoveTracker.MaxMovesPerDay; i++)
        {
            AssertTrue(t.ShouldIssue(i, 100f, 0f, day), "move " + i + " is allowed");
            t.RecordIssued(i, 500f, 0f, 100f, 0f, day, queued: false);
        }
        AssertEqual(6, MoveTracker.MaxMovesPerDay, "P8: six moves per review");
        AssertFalse(t.ShouldIssue(7, 100f, 0f, day), "the seventh waits");
        AssertTrue(t.ShouldIssue(7, 100f, 0f, day + 1), "the next review day has a fresh budget");
        for (int i = 8; i < 16; i++) t.RecordIssued(i, 500f, 0f, 100f, 0f, day + 1, queued: true);
        AssertTrue(t.ShouldIssue(20, 100f, 0f, day + 1), "eight queued moves spent none of the budget");
        for (int i = 21; i < 27; i++) t.RecordIssued(i, 500f, 0f, 100f, 0f, day + 1, queued: false);
        AssertFalse(t.ShouldIssue(30, 100f, 0f, day + 1), "six issued ones did");
    }

    private static void MoveConfirmedWhenWaypointChangesTowardTarget()
    {
        int day = 100;
        var t = new MoveTracker();
        t.RecordIssued(1, 500f, 0f, 100f, 0f, day, queued: false);
        AssertEqual(MoveOutcome.None, t.Judge(1, 500f, 0f, 500f, 0f, day), "not judged on the day it was issued");
        AssertEqual(MoveOutcome.Confirmed, t.Judge(1, 500f, 0f, 100f, 0f, day + 1), "the last waypoint moved toward the target");
        AssertFalse(t.InTransit(1), "judged");

        var far = new MoveTracker();
        far.RecordIssued(2, 500f, 0f, 100f, 0f, day, queued: false);
        AssertEqual(MoveOutcome.Confirmed, far.Judge(2, 390f, 0f, 500f, 0f, day + 1), "or it covered 25% of the way (110 of 400)");

        var miss = new MoveTracker();
        miss.RecordIssued(3, 500f, 0f, 100f, 0f, day, queued: false);
        AssertEqual(MoveOutcome.Failed, miss.Judge(3, 450f, 0f, 500f, 0f, day + 1), "12% of the way and the waypoint unchanged");

        var away = new MoveTracker();
        away.RecordIssued(4, 500f, 0f, 100f, 0f, day, queued: false);
        AssertEqual(MoveOutcome.Failed, away.Judge(4, 500f, 0f, 520f, 0f, day + 1), "a waypoint away from the target is no confirmation");
        AssertEqual(MoveOutcome.None, away.Judge(99, 0f, 0f, 0f, 0f, day + 1), "an unknown formation");
    }

    private static void MoveFailedThenRetriedThenGaveUp()
    {
        int day = 100;
        var t = new MoveTracker();
        AssertEqual(MoveOutcome.Issued, t.IssueOutcome(1, 100f, 0f, queued: false), "a first issue");
        t.RecordIssued(1, 500f, 0f, 100f, 0f, day, queued: false);
        AssertFalse(t.ShouldIssue(1, 100f, 0f, day + 1), "in transit: not issued twice");
        AssertEqual(MoveOutcome.Failed, t.Judge(1, 500f, 0f, 500f, 0f, day + 1), "the first miss");
        AssertTrue(t.ShouldIssue(1, 100f, 0f, day + 1), "one retry at the same review");
        AssertEqual(MoveOutcome.Retried, t.IssueOutcome(1, 100f, 0f, queued: false), "which is reported as a retry");
        t.RecordIssued(1, 500f, 0f, 100f, 0f, day + 1, queued: false);
        AssertEqual(MoveOutcome.GaveUp, t.Judge(1, 500f, 0f, 500f, 0f, day + 2), "the retry missed too");
        AssertFalse(t.ShouldIssue(1, 100f, 0f, day + 2), "no third attempt at this target");
        AssertFalse(t.ShouldIssue(1, 100f, 0f, day + 40), "...however long it waits");
        AssertTrue(t.ShouldIssue(1, 200f, 0f, day + 2), "a new target starts afresh");
        AssertEqual(MoveOutcome.Issued, t.IssueOutcome(1, 200f, 0f, queued: false), "and is a first issue");
        AssertFalse(t.InTransit(1), "a gave-up record is settled");
    }

    private static void QueuedMoveIsNotAFailure()
    {
        int day = 100;
        var t = new MoveTracker();
        t.RecordIssued(1, 500f, 0f, 100f, 0f, day, queued: true);
        AssertTrue(t.InTransit(1), "a queued move holds the formation out of vanilla's offense");
        for (int d = 1; d <= 10; d++)
        {
            AssertEqual(MoveOutcome.Queued, t.Judge(1, 500f, 0f, 500f, 0f, day + d), "review +" + d + ": still queued, never failed");
            AssertTrue(t.ShouldIssue(1, 100f, 0f, day + d), "retried at every review");
        }
        AssertEqual(MoveOutcome.Queued, t.IssueOutcome(1, 100f, 0f, queued: true), "the outcome of queueing");
        t.RecordIssued(1, 500f, 0f, 100f, 0f, day + 11, queued: false);
        AssertEqual(MoveOutcome.Failed, t.Judge(1, 500f, 0f, 500f, 0f, day + 12), "a queued move that finally issued is judged like any other: retries start at zero");
    }

    // MoveUnitTo itself refuses a target in a state that is neither own, allied nor at war (14493), returning false
    // with no order running: that is a refused target, not a queue.
    private static void RefusedMoveCountsAsFailedNotQueued()
    {
        int day = 100;
        var t = new MoveTracker();
        t.RecordRefused(1, 100f, 0f, day, "corps1:1");
        AssertFalse(t.InTransit(1), "nothing was issued");
        AssertTrue(t.ShouldIssue(1, 100f, 0f, day), "the first refusal allows one retry");
        AssertEqual(MoveOutcome.Retried, t.IssueOutcome(1, 100f, 0f, queued: false), "reported as a retry");
        t.RecordRefused(1, 100f, 0f, day + 1, "corps1:1");
        AssertFalse(t.ShouldIssue(1, 100f, 0f, day + 1), "the second refusal ends it");
        AssertTrue(t.ShouldIssue(1, 120f, 0f, day + 1), "until the target changes");
    }

    private static void MoveRecordsKeyedByUnitKey()
    {
        var t = new MoveTracker();
        t.RecordIssued(11, 500f, 0f, 100f, 0f, 100, queued: false, unitKey: "corps1:1");
        t.RecordIssued(12, 400f, 0f, 90f, 0f, 100, queued: false, unitKey: "corps2:2");
        var records = new List<MoveRecord>(t.Records);
        records.Sort((a, b) => string.CompareOrdinal(a.UnitKey, b.UnitKey));
        AssertEqual(2, records.Count, "one record per formation");
        AssertEqual("corps1:1", records[0].UnitKey, "persisted by unit key, the sidecar convention");
        AssertNear(100f, records[0].TargetX, 0.001f, "target x");
        AssertEqual(100, records[0].IssuedDay, "issued day");

        var back = new MoveTracker();
        back.Restore(records);
        var again = new List<MoveRecord>(back.Records);
        again.Sort((a, b) => string.CompareOrdinal(a.UnitKey, b.UnitKey));
        AssertEqual(2, again.Count, "restored");
        AssertEqual(records[1].UnitKey, again[1].UnitKey, "keys round-trip");
        AssertEqual(records[1].Retries, again[1].Retries, "retries round-trip");
        AssertFalse(back.InTransit(11), "a restored record is settled: the next review reissues what is still unfinished");
        back.Restore(null);
        AssertEqual(0, new List<MoveRecord>(back.Records).Count, "a null restore clears");
    }

    private static void MoveTrackersArePerAllianceAndPerCampaign()
    {
        CampaignScope.ResetAll();
        var zero = MoveTrackers.For(0);
        var one = MoveTrackers.For(1);
        AssertTrue(!ReferenceEquals(zero, one), "one tracker per alliance");
        AssertTrue(ReferenceEquals(zero, MoveTrackers.For(0)), "stable within a campaign");
        AssertTrue(MoveTrackers.For(2) == null && MoveTrackers.For(-1) == null, "alliances 0 and 1 only");
        zero.RecordIssued(1, 500f, 0f, 100f, 0f, 100, queued: false);
        CampaignScope.ResetAll();
        AssertFalse(MoveTrackers.For(0).InTransit(1), "a new campaign starts empty");
    }

    // ---- the mover pass ----

    private static void PlayerCommandChainNeverMoved()
    {
        foreach (var chain in new[] { WlChainClass.PlayerCommand, WlChainClass.CicSide })
        {
            var s = ExecutorFixtures.Wide();
            var own = ExecutorFixtures.Corps(1, 350f, 15000f);
            own.Chain = chain;
            s.Own.Add(own);
            var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
            var a = new Allocation { Alliance = 0 };
            a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
            var host = new FakeMoverHost();
            var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
            AssertEqual(0, host.Moves.Count, chain + " is never moved");
            AssertEqual(0, host.Orders.Count, chain + " gets no order either");
            AssertEqual(1, tally.SkippedPlayer, chain + " counted as skipped");
        }
    }

    // His Support order is a W&L dispatch (the host's IssuePlayerOrder, intent Redeploy in the runtime, pinned below); he is
    // never MainEffort, CapitalGuard or DepotBuilder (G4), and if such an assignment ever reached the mover it is skipped.
    private static void MapGroupPlayerGetsOrderNotMove()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 200f, 20000f));
        var player = ExecutorFixtures.Corps(2, 550f, 6000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        int support = ExecutorFixtures.Sectors(s).SectorAt(550f, 0f);
        plan.Sectors.Add(new SectorPlan { SectorId = support, Posture = TheaterPosture.Support, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f });
        var a = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        AssertEqual(FormationRole.Support, a.TryGet(2, out var x) ? x.Role : FormationRole.Hold, "the allocator gave him a Support order");

        var host = new FakeMoverHost();
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertFalse(host.Moves.Exists(m => m.id == 2), "an order, never a move, for the map-group player");
        AssertEqual(1, host.Orders.Count, "one order");
        AssertEqual(2, host.Orders[0].id, "to him");
        AssertNear(100f, host.Orders[0].x, 0.01f, "toward the Support target");
        AssertEqual(1, tally.Ordered, "counted as ordered");
        AssertTrue(host.Moves.Exists(m => m.id == 1), "the AI corps is moved");

        foreach (var role in new[] { FormationRole.MainEffort, FormationRole.CapitalGuard, FormationRole.DepotBuilder, FormationRole.Recover })
        {
            var forced = new Allocation { Alliance = 0 };
            forced.Assignments.Add(ExecutorFixtures.Assign(2, role, support, 100f, 0f));
            var other = new FakeMoverHost();
            var t2 = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, forced, ExecutorFixtures.OpenTempo()), other, new MoveTracker(), ExecutorFixtures.State());
            AssertEqual(0, other.Moves.Count + other.Orders.Count, role + " is neither moved nor ordered for him");
            AssertEqual(1, t2.SkippedPlayer, role + " skipped");
        }
    }

    private static void CarryingGroupMovedNormally()
    {
        var s = ExecutorFixtures.Wide();
        var carrying = ExecutorFixtures.Corps(1, 350f, 15000f);
        carrying.Chain = WlChainClass.CarryingGroup;
        s.Own.Add(carrying);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(1, host.Moves.Count, "the AI map group above a player below map level is moved as any AI corps");
        AssertEqual(0, host.Orders.Count, "...by MoveUnitTo (stage F's Postfix dispatches it), not by an order");
    }

    private static void MoverDispatchesTheRolesInPriorityOrder()
    {
        var s = ExecutorFixtures.Wide();
        int[] ids = { 1, 2, 3, 4, 5 };
        float[] xs = { 560f, 540f, 520f, 500f, 480f };
        foreach (int id in ids) s.Own.Add(ExecutorFixtures.Corps(id, xs[id - 1], 9000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 3 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.DepotBuilder, 0, 300f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.Support, 0, 300f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(3, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(4, FormationRole.Recover, 0, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(5, FormationRole.CapitalGuard, 0, 0f, 0f));
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        var order = new List<int>();
        foreach (var m in host.Moves) order.Add(m.id);
        AssertEqual("5,3,4,1,2", string.Join(",", order), "CapitalGuard, MainEffort, Recover, DepotBuilder, Support; the main effort releases the tempo before Support is judged");
    }

    private static void MoverListsMainEffortAndSupportInTheOffensiveList()
    {
        var s = ExecutorFixtures.Wide();
        for (int id = 1; id <= 4; id++) s.Own.Add(ExecutorFixtures.Corps(id, 500f + id, 9000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.Support, 0, 320f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(3, FormationRole.CapitalGuard, 0, 0f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(4, FormationRole.DepotBuilder, 0, 300f, 0f));
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(4, host.Moves.Count, "all four moved");
        AssertEqual("1,2", string.Join(",", host.Listed.ConvertAll(l => l.id)), "only MainEffort and Support join unitsinoffensiveoperations");
        AssertTrue(host.Listed.TrueForAll(l => l.listed), "added, never removed, by a dispatch");
    }

    private static void MoverSkipsBusyLockedAndArrivedFormations()
    {
        var s = ExecutorFixtures.Wide();
        var busy = ExecutorFixtures.Corps(1, 350f, 9000f);
        busy.Flags |= FormationFlags.InBattle;
        s.Own.Add(busy);
        s.Own.Add(ExecutorFixtures.Corps(2, 360f, 9000f));
        var arrived = ExecutorFixtures.Corps(3, 99f, 9000f);
        s.Own.Add(arrived);
        var underway = ExecutorFixtures.Corps(4, 400f, 9000f);
        underway.WaypointX = 100f; // the final waypoint is already the target
        s.Own.Add(underway);
        var defending = ExecutorFixtures.Corps(5, 370f, 9000f);
        defending.Flags |= FormationFlags.InDefensiveOps;
        s.Own.Add(defending);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2, 3, 4, 5 });
        var a = new Allocation { Alliance = 0 };
        for (int id = 1; id <= 5; id++) a.Assignments.Add(ExecutorFixtures.Assign(id, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        var host = new FakeMoverHost();
        host.Locked.Add(2);
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(0, host.Moves.Count, "busy, package-locked, arrived, underway and defending formations are not moved");
        AssertEqual(3, tally.SkippedBusy, "busy, locked and in a vanilla defensive operation are counted as skipped");
    }

    private static void MoverSkipsARestrictedTarget()
    {
        var host = new FakeMoverHost { Restricted = (x, z) => x < 120f };
        var tracker = new MoveTracker();
        var state = ExecutorFixtures.State();
        var tallies = new List<MoverTally>();
        for (int day = 0; day < 4; day++)
        {
            var s = ExecutorFixtures.Wide(day);
            s.Own.Add(ExecutorFixtures.Corps(1, 350f, 9000f));
            var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
            var a = new Allocation { Alliance = 0 };
            a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
            tallies.Add(CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, tracker, state));
            AssertFalse(tracker.InTransit(1), "a refused target leaves nothing in transit, review " + day);
        }
        AssertEqual(0, host.Moves.Count, "a restricted target is skipped (CheckIfFinalWaypointIsPassable semantics): MoveUnitTo is never called");
        AssertEqual(1, tallies[0].Failed, "the first refusal counts as failed");
        AssertFalse(tracker.InTransit(1), "nothing in transit");
        AssertEqual(1, tallies[1].Retried, "one more look the next review");
        AssertEqual(0, tallies[2].Failed + tallies[2].Retried, "then it is given up for this target");
        AssertEqual(0, tallies[3].Failed + tallies[3].Retried, "and stays given up");
    }

    private static void MoverQueuesBehindARunningOrder()
    {
        var s = ExecutorFixtures.Wide();
        var running = ExecutorFixtures.Corps(1, 350f, 9000f);
        running.Flags |= FormationFlags.MoveQueued;
        s.Own.Add(running);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        var host = new FakeMoverHost();
        var tracker = new MoveTracker();
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        AssertEqual(0, host.Moves.Count, "MoveUnitTo is not called while a defensive moving order runs (it would refuse)");
        AssertEqual(1, tally.Queued, "queued");
        AssertTrue(tracker.InTransit(1), "held out of vanilla's offense meanwhile");
        AssertEqual(0, tally.Failed, "not a failure");
    }

    private static void MoverJudgesLastReviewsMovesFirst()
    {
        var s = ExecutorFixtures.Wide();
        var moving = ExecutorFixtures.Corps(1, 350f, 9000f);
        moving.WaypointX = 100f;
        s.Own.Add(moving);
        s.Own.Add(ExecutorFixtures.Corps(2, 360f, 9000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        var tracker = new MoveTracker();
        int yesterday = s.DaySerial - 1;
        tracker.RecordIssued(1, 450f, 0f, 100f, 0f, yesterday, queued: false);   // its waypoint moved toward the target
        tracker.RecordIssued(2, 360f, 0f, 100f, 0f, yesterday, queued: false);   // did not move at all
        tracker.RecordIssued(99, 360f, 0f, 100f, 0f, yesterday, queued: false);  // a formation that left the snapshot
        var host = new FakeMoverHost();
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        AssertEqual(1, tally.Confirmed, "formation 1 confirmed");
        AssertEqual(1, tally.Failed, "formation 2 failed");
        AssertEqual(1, tally.Retried, "and is retried at once, in the same review");
        AssertTrue(host.Moves.Exists(m => m.id == 2) && !host.Moves.Exists(m => m.id == 1), "only the failed one is moved again");
        AssertFalse(tracker.InTransit(99), "a vanished formation is forgotten");
    }

    // DefensiveMovingOrder.UpdateOrders serves one order at a time (9966-10052): a move issued yesterday may still be
    // waiting, and a formation with its order still queued is not judged (and so not failed) yet.
    private static void MoverDoesNotJudgeAMoveWhoseOrderIsStillPending()
    {
        var s = ExecutorFixtures.Wide();
        var pending = ExecutorFixtures.Corps(1, 350f, 9000f);
        pending.Flags |= FormationFlags.MoveQueued;
        s.Own.Add(pending);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        var tracker = new MoveTracker();
        tracker.RecordIssued(1, 350f, 0f, 100f, 0f, s.DaySerial - 1, queued: false);
        var host = new FakeMoverHost();
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        AssertEqual(0, tally.Failed + tally.Confirmed + tally.GaveUp, "not judged while its order waits in vanilla's queue");
        AssertEqual(0, host.Moves.Count, "and not issued twice");
        AssertTrue(tracker.InTransit(1), "still in transit");
    }

    private static void WlSideMovableGetsVanillaAiTransportDefault()
    {
        var s = ExecutorFixtures.Wide();
        var plain = ExecutorFixtures.Corps(1, 350f, 9000f);
        plain.UseRails = false;
        plain.UseRivers = false;
        s.Own.Add(plain);
        var carrying = ExecutorFixtures.Corps(2, 360f, 9000f);
        carrying.UseRails = false;
        carrying.UseRivers = true;
        carrying.Chain = WlChainClass.CarryingGroup;
        s.Own.Add(carrying);
        var fine = ExecutorFixtures.Corps(3, 370f, 9000f);
        s.Own.Add(fine);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2, 3 });
        var a = new Allocation { Alliance = 0 };
        for (int id = 1; id <= 3; id++) a.Assignments.Add(ExecutorFixtures.Assign(id, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        var inputs = ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo());
        inputs.WlPlayerAlliance = true;
        var host = new FakeMoverHost();
        CampaignMover.Review(inputs, host, new MoveTracker(), ExecutorFixtures.State());
        host.Raised.Sort();
        AssertEqual("1,2", string.Join(",", host.Raised), "a movable formation of the W&L player's alliance whose rails or rivers flag is false gets vanilla's AI default (112862-112867)");

        var other = new FakeMoverHost();
        inputs.WlPlayerAlliance = false;
        CampaignMover.Review(inputs, other, new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(0, other.Raised.Count, "never on an alliance that is not the W&L player's: vanilla already set it (112862)");
    }

    private static void PlayerCommandTransportFlagsUntouched()
    {
        var s = ExecutorFixtures.Wide();
        var command = ExecutorFixtures.Corps(1, 350f, 9000f);
        command.UseRails = false;
        command.Chain = WlChainClass.PlayerCommand;
        s.Own.Add(command);
        var mapGroup = ExecutorFixtures.Corps(2, 360f, 9000f);
        mapGroup.UseRails = false;
        mapGroup.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(mapGroup);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        int support = ExecutorFixtures.Sectors(s).SectorAt(360f, 0f);
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.Support, support, 100f, 0f));
        var inputs = ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo());
        inputs.WlPlayerAlliance = true;
        var host = new FakeMoverHost();
        CampaignMover.Review(inputs, host, new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(0, host.Raised.Count, "the player's command and the map-group player's flags are the player's: never written");
    }

    private static void RecoverRemovedFromOffensiveList()
    {
        var s = ExecutorFixtures.Wide();
        var recovering = ExecutorFixtures.Corps(1, 350f, 9000f);
        recovering.Flags |= FormationFlags.InOffensiveOps;
        recovering.Supply01 = 0.2f;
        s.Own.Add(recovering);
        var healthy = ExecutorFixtures.Corps(2, 360f, 9000f);
        healthy.Flags |= FormationFlags.InOffensiveOps;
        s.Own.Add(healthy);
        var player = ExecutorFixtures.Corps(3, 370f, 9000f);
        player.Flags |= FormationFlags.InOffensiveOps;
        player.Chain = WlChainClass.PlayerCommand;
        s.Own.Add(player);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 2 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.Recover, 0, 300f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(3, FormationRole.Recover, 0, 300f, 0f));
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertEqual("1", string.Join(",", host.Listed.FindAll(l => !l.listed).ConvertAll(l => l.id)),
            "only the Recover formation the planner may move leaves unitsinoffensiveoperations (vanilla's RelieveUnitsInOperations does the same, 14068-14078)");
        AssertTrue(host.Moves.Exists(m => m.id == 1), "and the mover walks it toward supply");
    }

    private static void MoverHostNeverTouchesTheDefensiveList()
    {
        foreach (var method in typeof(IMoverHost).GetMethods())
            AssertFalse(method.Name.IndexOf("Defens", StringComparison.OrdinalIgnoreCase) >= 0, method.Name + " must not name the defensive list");
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMoverRuntime.cs");
        AssertFalse(runtime.Contains("unitsindefensiveoperations"), "unitsindefensiveoperations is vanilla's (13782-13848): the runtime does not name it");
    }

    // ---- the withhold view ----

    private static void WithheldFromOffenseIsWithheldTransitMassOrDeferredSupport()
    {
        var w = ExecutorFixtures.PublishedWorld(out var tracker);
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Reserve), "an always-withheld role");
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.CapitalGuard), "the capital guard");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.FreeHold), "Hold above its need is vanilla's");
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Main), "a MainEffort member of the mass: only the mover launches it (decision 5)");
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Support), "Support while its decision is deferred");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.MapGroupSupport), "...except the map-group player's Support");
        tracker.RecordIssued(w.FreeHold, 300f, 0f, 100f, 0f, ExecutorFixtures.Day, queued: false);
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.FreeHold), "a formation in transit");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, 9999), "a formation the plan does not know");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(1, w.Reserve), "the other alliance has its own (empty) store");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(2, w.Reserve), "alliances 0 and 1 only");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(-1, w.Reserve), "negative alliance");

        ExecutorFixtures.Release(0);
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Main), "the mass stays withheld after the release: only undispatched members reach vanilla's pool");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.Support), "Support is vanilla's again once its decision is released");
        CampaignScope.ResetAll();
    }

    private static void HoldAtNeedAvailableForDefenseInOwnSector()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        AssertTrue(w.HoldAtNeedWithheld, "fixture: the Hold-at-need formation is withheld from offense");
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.HoldAtNeed), "withheld from the offensive gather");
        AssertFalse(CampaignWithholdView.WithheldFromDefense(0, w.HoldAtNeed, w.HoldSectorX, 0f), "but available to a defense against an enemy in its own sector");
        CampaignScope.ResetAll();
    }

    private static void HoldAtNeedWithheldFromDefenseOutsideSector()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        AssertTrue(CampaignWithholdView.WithheldFromDefense(0, w.HoldAtNeed, 100f, 0f), "an enemy in another sector does not pull the sector's holder away");
        AssertTrue(CampaignWithholdView.WithheldFromDefense(0, w.Reserve, w.HoldSectorX, 0f), "a Reserve formation is withheld from defence wherever the enemy stands");
        AssertTrue(CampaignWithholdView.WithheldFromDefense(0, w.CapitalGuard, w.HoldSectorX, 0f), "...and the capital guard");
        CampaignScope.ResetAll();
    }

    private static void HoldAboveNeedNeverWithheld()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.FreeHold), "above need: not withheld from offense");
        AssertFalse(CampaignWithholdView.WithheldFromDefense(0, w.FreeHold, 100f, 0f), "nor from defense");
        CampaignScope.ResetAll();
    }

    // A paused alliance or a failing review never steers on stale state: more than two review days old is ignored.
    private static void StalePlanIgnoredByPatches()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        PlanFreshness.NoteToday(ExecutorFixtures.Day + PlanFreshness.MaxAgeDays);
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Reserve), "two days old: still read");
        AssertTrue(CampaignWithholdView.TrySteering(0, out _, out _), "steering reads it too");
        PlanFreshness.NoteToday(ExecutorFixtures.Day + PlanFreshness.MaxAgeDays + 1);
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.Reserve), "three days old: ignored");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.Main), "the mass too");
        AssertFalse(CampaignWithholdView.WithheldFromDefense(0, w.Reserve, 100f, 0f), "and the defensive Postfix");
        AssertFalse(CampaignWithholdView.TrySteering(0, out var plan, out var sectors) || plan != null || sectors != null, "and the importance steering");
        AssertTrue(PlanFreshness.IsFresh(10, 12) && !PlanFreshness.IsFresh(10, 13), "the pure rule: more than 2 days older");
        PlanFreshness.NoteToday(ExecutorFixtures.Day);
        AssertTrue(PlanFreshness.IsToday(ExecutorFixtures.Day), "the executors' test: today's plan");
        AssertFalse(PlanFreshness.IsToday(ExecutorFixtures.Day - 1), "yesterday's plan is not acted on");
        PlanFreshness.NoteToday(-1);
        AssertFalse(PlanFreshness.IsToday(ExecutorFixtures.Day), "no clock noted: nothing is today");
        AssertFalse(PlanFreshness.IsToday(-1), "and the unnoted clock is not a day either");
        PlanFreshness.NoteToday(ExecutorFixtures.Day + PlanFreshness.MaxAgeDays + 1);
        AssertTrue(PlanFreshness.IsFresh(10, 5), "a plan from the future (the clock went back after a load) is fresh");
        PlanFreshness.NoteToday(-1);
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Reserve), "no clock noted yet: the plan is read");
        CampaignScope.ResetAll();
    }

    private static void ExecutorContextPublishesAtomicallyWithThePlan()
    {
        CampaignScope.ResetAll();
        try
        {
            bool inJob = true;
            DailyReviewView.Attach(() => inJob);
            var s = ExecutorFixtures.Wide();
            s.Own.Add(ExecutorFixtures.Corps(1, 200f, 20000f));
            var sectors = ExecutorFixtures.Sectors(s);
            var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
            var a = new Allocation { Alliance = 0 };
            a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
            CampaignPlanStore.Publish(0, plan, a, sectors);
            CampaignPlanStore.PublishExecutorContext(0, new ExecutorContext { DecisionSerial = 1, ReleasedDecision = -1, ExecutorsAllowed = true, MapGroupInstanceId = -1 });
            CampaignPlanStore.PreparePublish();
            CampaignPlanStore.SwapPublished();

            CampaignPlanStore.PublishExecutorContext(0, new ExecutorContext { DecisionSerial = 2, ReleasedDecision = 2, ExecutorsAllowed = true, MapGroupInstanceId = -1 });
            inJob = false;
            AssertTrue(CampaignPlanStore.TryGetExecutorContext(0, out var published), "published context");
            AssertEqual(1, published.DecisionSerial, "between slices the reader sees the last commit's decision, not today's working one");
            inJob = true;
            AssertTrue(CampaignPlanStore.TryGetExecutorContext(0, out var working), "working context");
            AssertEqual(2, working.DecisionSerial, "inside a job: the working one");
            CampaignPlanStore.PreparePublish();
            CampaignPlanStore.SwapPublished();
            inJob = false;
            AssertTrue(CampaignPlanStore.TryGetExecutorContext(0, out var after) && after.ReleasedDecision == 2, "after the commit: today's");
            AssertFalse(CampaignPlanStore.TryGetExecutorContext(1, out _), "none for an alliance with no plan");
            AssertFalse(CampaignPlanStore.TryGetExecutorContext(2, out _), "alliances 0 and 1 only");
        }
        finally
        {
            DailyReviewView.Attach(null);
            CampaignScope.ResetAll();
        }
    }

    // The availability tests run for every candidate of every lead (14080-14158): a call is an index read.
    private static void WithholdViewAllocatesNothingAfterWarmup()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        for (int i = 0; i < 20; i++)
        {
            CampaignWithholdView.WithheldFromOffense(0, w.Main);
            CampaignWithholdView.WithheldFromDefense(0, w.HoldAtNeed, 560f, 0f);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool any = false;
        for (int i = 0; i < 2000; i++)
        {
            any |= CampaignWithholdView.WithheldFromOffense(0, w.Main + (i % 7));
            any |= CampaignWithholdView.WithheldFromDefense(0, w.HoldAtNeed, 560f, 0f);
            any |= CampaignWithholdView.WithheldFromOffense(1, w.Main);
        }
        AssertEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "no allocation after warm-up");
        AssertTrue(any, "and the calls did answer");
        CampaignScope.ResetAll();
    }

    // ---- theater box, objective, importance ----

    private const float HalfX = 150f, HalfZ = 100f;

    private static void TheaterBoxKeepsUnitInside()
    {
        foreach (var target in new[] { new[] { 100f, 0f }, new[] { -5000f, 0f }, new[] { 5000f, 4000f }, new[] { 310f, -9000f }, new[] { 300f, 0f } })
        {
            SteeringRules.TheaterCenter(300f, 0f, target[0], target[1], HalfX, HalfZ, out float cx, out float cz);
            AssertTrue(Math.Abs(300f - cx) < HalfX, "x stays inside the box for target " + target[0] + "," + target[1] + ": " + cx);
            AssertTrue(Math.Abs(0f - cz) < HalfZ, "z stays inside the box for target " + target[0] + "," + target[1] + ": " + cz);
        }
        SteeringRules.TheaterCenter(300f, 0f, 250f, 20f, HalfX, HalfZ, out float nx, out float nz);
        AssertNear(250f, nx, 0.001f, "a target inside the box becomes the centre");
        AssertNear(20f, nz, 0.001f, "...on both axes");
        SteeringRules.TheaterCenter(300f, 0f, 5000f, 0f, HalfX, HalfZ, out float fx, out _);
        AssertNear(300f + SteeringRules.BoxMargin * HalfX, fx, 0.001f, "a far target pulls the centre to the margin of the box, toward it");
        SteeringRules.TheaterCenter(300f, 0f, -5000f, 0f, HalfX, HalfZ, out float wx, out _);
        AssertNear(300f - SteeringRules.BoxMargin * HalfX, wx, 0.001f, "in either direction");
    }

    private static void TheaterBoxRateLimited()
    {
        var limiter = new TheaterWriteLimiter();
        AssertTrue(limiter.MayWrite(1, 100), "the first");
        limiter.Record(1, 100);
        AssertTrue(limiter.MayWrite(1, 101), "the second in the week");
        limiter.Record(1, 101);
        AssertFalse(limiter.MayWrite(1, 102), "the third waits");
        AssertTrue(limiter.MayWrite(2, 102), "another formation has its own budget");
        AssertFalse(limiter.MayWrite(1, 100 + TheaterWriteLimiter.WeekDays - 1), "still inside the week of the first write");
        AssertTrue(limiter.MayWrite(1, 100 + TheaterWriteLimiter.WeekDays), "the first write has aged out of the week");
        limiter.Clear();
        AssertTrue(limiter.MayWrite(1, 102), "cleared with the campaign");
        AssertEqual(2, TheaterWriteLimiter.MaxPerWeek, "spec C8: twice per formation per week");
    }

    private static void TheaterWritesFollowTheRoles()
    {
        var s = ExecutorFixtures.Wide();
        s.TheaterHalfExtentX = HalfX;
        s.TheaterHalfExtentZ = HalfZ;
        s.Own.Add(ExecutorFixtures.Corps(1, 120f, 15000f));   // MainEffort, inside the main sector
        s.Own.Add(ExecutorFixtures.Corps(2, 550f, 9000f));    // Support, inside its own sector
        s.Own.Add(ExecutorFixtures.Corps(3, 350f, 9000f));    // Hold
        s.Own.Add(ExecutorFixtures.Corps(4, 560f, 9000f));    // Reserve: no theater write
        var covered = ExecutorFixtures.Corps(5, 130f, 9000f); // MainEffort whose centre already serves it
        covered.HasTheater = true;
        covered.TheaterX = 100f;
        covered.TheaterZ = 0f;
        s.Own.Add(covered);
        var player = ExecutorFixtures.Corps(6, 125f, 9000f);
        player.Chain = WlChainClass.PlayerCommand;
        s.Own.Add(player);
        s.Own.Add(ExecutorFixtures.Corps(7, 350f, 9000f));    // Support assigned to a sector it has not reached
        var mapGroup = ExecutorFixtures.Corps(8, 355f, 9000f);
        mapGroup.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(mapGroup);
        var sectors = ExecutorFixtures.Sectors(s);
        int main = sectors.SectorAt(120f, 0f), hold = sectors.SectorAt(350f, 0f), east = sectors.SectorAt(550f, 0f);
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, main, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.Support, east, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(3, FormationRole.Hold, hold, 350f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(4, FormationRole.Reserve, east, 560f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(5, FormationRole.MainEffort, main, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(6, FormationRole.Hold, main, 125f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(7, FormationRole.Support, east, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(8, FormationRole.Hold, hold, 355f, 0f));
        var writes = new List<TheaterWrite>();
        var limiter = new TheaterWriteLimiter();
        SteeringRules.PlanTheaterWrites(s, a, sectors, limiter, writes);
        var byId = new Dictionary<int, TheaterWrite>();
        foreach (var w in writes) byId[w.InstanceId] = w;
        AssertTrue(byId.ContainsKey(1), "a MainEffort formation inside its sector is boxed toward its target");
        AssertNear(100f, byId[1].X, 0.001f, "the target is inside the box: it becomes the centre");
        AssertTrue(byId.ContainsKey(2), "a Support formation inside its own sector too");
        AssertNear(550f - SteeringRules.BoxMargin * HalfX, byId[2].X, 0.001f, "the far target pulls the centre to the margin of the box");
        AssertFalse(byId.ContainsKey(7), "a Support formation outside its sector is not boxed yet");
        AssertTrue(byId.ContainsKey(3), "a Hold formation is centred on itself");
        AssertNear(350f, byId[3].X, 0.001f, "x");
        AssertNear(0f, byId[3].Z, 0.001f, "z");
        AssertFalse(byId.ContainsKey(4), "Reserve gets no theater");
        AssertFalse(byId.ContainsKey(5), "a formation whose centre already serves it is left alone (idempotent)");
        AssertFalse(byId.ContainsKey(6), "the player's command is never written");
        AssertFalse(byId.ContainsKey(8), "nor the map-group player's group: his theater is his");
        AssertEqual(3, writes.Count, "nothing else");

        var again = new List<TheaterWrite>();
        SteeringRules.PlanTheaterWrites(s, a, sectors, limiter, again);
        AssertEqual(3, again.Count, "planning records no write: the runtime records the writes it makes");
        foreach (var w in writes) limiter.Record(w.InstanceId, s.DaySerial);
        foreach (var w in writes) limiter.Record(w.InstanceId, s.DaySerial + 1);
        var third = new List<TheaterWrite>();
        SteeringRules.PlanTheaterWrites(s, a, sectors, limiter, third);
        AssertEqual(0, third.Count, "twice per formation per week");
        var noBox = new List<TheaterWrite>();
        s.TheaterHalfExtentX = 0f;
        SteeringRules.PlanTheaterWrites(s, a, sectors, new TheaterWriteLimiter(), noBox);
        AssertEqual(0, noBox.Count, "no known theater size: nothing written");
    }

    private static void ObjectiveWriteOnlyWhenChanged()
    {
        AssertEqual(CampaignObjectiveSteering.NoWrite, SteeringRules.ObjectiveWrite(5, true, 5, 5), "already followed: no write");
        AssertEqual(CampaignObjectiveSteering.NoWrite, SteeringRules.ObjectiveWrite(5, true, 5, -1), "already followed (vanilla's own pick equal to the plan's): no write");
        AssertEqual(5, SteeringRules.ObjectiveWrite(5, true, 3, 3), "a different objective is replaced");
        AssertEqual(5, SteeringRules.ObjectiveWrite(5, true, -1, -1), "nothing followed yet");
        AssertEqual(5, SteeringRules.ObjectiveWrite(5, true, 12, -1), "a vanilla pick is overridden by the plan's main objective (spec C6)");
    }

    private static void ObjectiveResetOnlyWhenModWritten()
    {
        AssertEqual(-1, SteeringRules.ObjectiveWrite(-1, false, 5, 5), "no main effort and the mod wrote it: reset so vanilla re-picks (17780-17786)");
        AssertEqual(CampaignObjectiveSteering.NoWrite, SteeringRules.ObjectiveWrite(-1, false, 5, 7), "vanilla's own pick is left alone");
        AssertEqual(CampaignObjectiveSteering.NoWrite, SteeringRules.ObjectiveWrite(-1, false, -1, 5), "nothing followed: nothing to reset");
        AssertEqual(-1, SteeringRules.ObjectiveWrite(5, false, 5, 5), "a main objective vanilla no longer offers is not followed: reset what the mod wrote");
        AssertEqual(CampaignObjectiveSteering.NoWrite, SteeringRules.ObjectiveWrite(5, false, 9, 5), "...and only that");
    }

    private static void ImportanceRedirectOnlyForMainOrSupportSectors()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 200f, 20000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = new TheaterPlan { Alliance = 0, DaySerial = s.DaySerial };
        int mainSector = sectors.SectorAt(100f, 0f), supportSector = sectors.SectorAt(350f, 0f);
        int holdSector = sectors.SectorAt(550f, 0f);
        plan.Sectors.Add(new SectorPlan { SectorId = mainSector, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f });
        plan.Sectors.Add(new SectorPlan { SectorId = supportSector, Posture = TheaterPosture.Support, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f });
        plan.Sectors.Add(new SectorPlan { SectorId = holdSector, Posture = TheaterPosture.Hold, TargetObjectiveId = -1, TargetX = 550f, TargetZ = 0f });
        AssertTrue(SteeringRules.ImportanceSteers(plan, sectors, 11, out float tx, out float tz), "an area of the main sector");
        AssertNear(100f, tx, 0.001f, "points at the sector's target x");
        AssertNear(0f, tz, 0.001f, "z");
        AssertTrue(SteeringRules.ImportanceSteers(plan, sectors, 13, out tx, out _) && Math.Abs(tx - 100f) < 0.001f, "an area of a Support sector points at the main target too");
        AssertFalse(SteeringRules.ImportanceSteers(plan, sectors, 16, out _, out _), "a Hold sector's area keeps vanilla's pick");
        plan.Sectors[2].TargetObjectiveId = 98;     // a target alone does not steer: the posture decides
        AssertFalse(SteeringRules.ImportanceSteers(plan, sectors, 16, out _, out _), "a Hold sector with a target is still vanilla's");
        foreach (var posture in new[] { TheaterPosture.Screen, TheaterPosture.EconomyOfForce, TheaterPosture.Recover })
        {
            plan.Sectors[2].Posture = posture;
            AssertFalse(SteeringRules.ImportanceSteers(plan, sectors, 16, out _, out _), posture + " keeps vanilla's pick");
        }
        AssertFalse(SteeringRules.ImportanceSteers(plan, sectors, 999, out _, out _), "an area the sector model does not know");
        AssertFalse(SteeringRules.ImportanceSteers(plan, null, 11, out _, out _), "no sector model");
        AssertFalse(SteeringRules.ImportanceSteers(null, sectors, 11, out _, out _), "no plan");
        plan.Sectors[0].TargetObjectiveId = -1;
        AssertFalse(SteeringRules.ImportanceSteers(plan, sectors, 11, out _, out _), "a main sector with no target steers nothing");
    }

    // The redirect keeps vanilla's own candidate gates (CalculateMostValueableAIZones 10984), re-keyed on the plan: the
    // theater equality clause is gone (the source sector's posture replaces it).
    private static void ImportanceRedirectRequiresVanillaGates()
    {
        AssertTrue(CampaignMilitaryGates.ShouldRedirectToPlanTarget(false, true, 80f, 150f, 3f, 5f, true), "all gates pass");
        AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(true, true, 80f, 150f, 3f, 5f, true), "the source is the target");
        AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(false, false, 80f, 150f, 3f, 5f, true), "the source is not in a steered sector");
        AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(false, true, 151f, 150f, 3f, 5f, true), "beyond aimaximumdistancetosearchforunitrelocations x rangefactoroffensivetargets");
        AssertTrue(CampaignMilitaryGates.ShouldRedirectToPlanTarget(false, true, 150f, 150f, 3f, 5f, true), "exactly at the distance is allowed (<=)");
        AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(false, true, 80f, 150f, 6f, 5f, true), "ownpoints above supplycapacity (the supply gate)");
        AssertTrue(CampaignMilitaryGates.ShouldRedirectToPlanTarget(false, true, 80f, 150f, 5f, 5f, true), "ownpoints equal to supplycapacity passes (<=)");
        AssertFalse(CampaignMilitaryGates.ShouldRedirectToPlanTarget(false, true, 80f, 150f, 3f, 5f, false), "vanilla's value clause");
    }

    // ---- source pins ----

    private static int CountLines(string body, string line)
    {
        int n = 0;
        foreach (var raw in body.Split('\n'))
            if (raw.Trim() == line) n++;
        return n;
    }

    private static void ExecutorFilesHoldNoUnityTypes()
    {
        foreach (string file in new[] { "PlannerChapterRules.cs", "CampaignTempoGate.cs", "MoveTracker.cs", "CampaignWithholdView.cs", "SteeringRules.cs", "CampaignMover.cs" })
        {
            string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/" + file);
            AssertFalse(src.Contains("UnityEngine") || System.Text.RegularExpressions.Regex.IsMatch(src, @"\bVector[23]\b") || src.Contains("Regiment"), file + " holds no Unity or vanilla type");
        }
    }

    private static void MoverRuntimeStatementsPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMoverRuntime.cs");
        AssertTrue(HasLine(src, "Intent = WlStrategicIntent.Redeploy,"), "the map-group player's Support order is a Redeploy dispatch (order type 5), never Offensive, DefendCapital or ConstructSupplyDepot");
        AssertFalse(src.Contains("WlStrategicIntent.Offensive") || src.Contains("WlStrategicIntent.DefendCapital") || src.Contains("WlStrategicIntent.ConstructSupplyDepot"), "no other intent");
        AssertTrue(HasLine(src, "SourceSystem = \"campaign-planner\""), "the dispatch names its source");
        AssertTrue(HasLine(src, "return AICampaign.MoveUnitTo(unit, new Vector3(x, unit.GetPosition().y, z), movearoundenemyunits: true);"), "MoveUnitTo with movearoundenemyunits: true, as every Whiskey mover");
        AssertFalse(src.Contains("EnterMoveSource"), "G5a does not call EnterMoveSource: stage F's Postfix types the march by list membership");
        AssertTrue(HasLine(src, "if (CoordinatedOperationRuntime.IsPackageLocked(unit)) return true;"), "the mover refuses #38's locked packages until G6 deletes them (P10)");
        AssertTrue(HasLine(src, "unit.userails = true;"), "vanilla's AI default for rails (112862-112867)");
        AssertTrue(HasLine(src, "unit.userivers = true;"), "...and rivers");
        AssertTrue(HasLine(src, "AifactionIndex = _faction,") || HasLine(src, "AifactionIndex = faction,"), "the dispatch carries the faction index (OperationalProbeRuntime's shape)");
    }

    private static void SteeringRuntimeStatementsPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSteeringRuntime.cs");
        AssertTrue(HasLine(src, "PickCampaignObjectivePatch.NoteWrittenByMod(alliance, write);"), "the mod-written value goes through PickCampaignObjectivePatch's statics (still persisted by the coordinator at 1911/1957)");
        AssertTrue(HasLine(src, "unit.theaterposition = new Vector3(write.X, unit.GetPosition().y, write.Z);"), "theaterposition is written whole");
    }

    private static void AvailabilityPatchesPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Patches/CampaignAvailabilityFilterPatch.cs");
        AssertTrue(HasLine(src, "PatchFirstFire.Mark(\"campaign.patch.planner-availability.offensive\");"), "first-fire id, offensive");
        AssertTrue(HasLine(src, "PatchFirstFire.Mark(\"campaign.patch.planner-availability.defensive\");"), "first-fire id, defensive");
        AssertTrue(HasLine(src, "using (TelemetryPerf.Scope(\"campaign.patch.planner-availability.offensive\", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))"), "scope, offensive");
        AssertTrue(HasLine(src, "using (TelemetryPerf.Scope(\"campaign.patch.planner-availability.defensive\", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))"), "scope, defensive");
        AssertFalse(src.Contains("__result = true"), "never sets true");
        AssertTrue(HasLine(src, "CampaignWithholdView.WithheldFromOffense(alliance, unit.GetInstanceID())") || src.Contains("CampaignWithholdView.WithheldFromOffense(alliance"), "the offensive view");
        AssertTrue(src.Contains("CampaignWithholdView.WithheldFromDefense(alliance"), "the defensive view");
        AssertTrue(HasLine(src, "new[] { typeof(int), typeof(Regiment), typeof(Vector3) });"), "the private defensive test is targeted by name and its full parameter list (13460)");
        AssertTrue(HasLine(src, "new[] { typeof(int), typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool) });"), "and the public offensive one by its five (14080)");
        AssertEqual(2, CountLines(src, "if (!__result) return;"), "both Postfixes leave a unit vanilla already refused alone");
        AssertEqual(2, CountLines(src, "__result = false;"), "and only ever set false");
        AssertTrue(src.Contains("IsUnitAvailableForDefensiveOperation") && src.Contains("IsUnitAvailableForOffensiveOperations"), "both vanilla tests");
        AssertTrue(src.Contains("CampaignPlannerRuntime.ExecutorsEnabled()"), "rollback: Enable Campaign Planner Executors (with Enable Campaign Planner)");
        AssertTrue(src.Contains("[HarmonyFinalizer]") == false && !src.Contains("HarmonyPrefix"), "Postfix only: no Prefix, no Transpiler");
    }

    private static void ImportancePatchPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Patches/ImportanceValuesPatch.cs");
        AssertTrue(HasLine(src, "PatchFirstFire.Mark(\"campaign.patch.importance-values\");"), "first-fire id kept");
        AssertTrue(src.Contains("CampaignWithholdView.TrySteering("), "reads the published plan through the view");
        AssertTrue(src.Contains("SteeringRules.ImportanceSteers("), "only a main or Support sector steers");
        AssertTrue(HasLine(src, "return CampaignMilitaryGates.ShouldRedirectToPlanTarget("), "vanilla's own candidate gates decide");
        AssertTrue(HasLine(src, "__instance.mostvalueableaiareaclose[aifaction] = target;") || HasLine(src, "picks[aifaction] = targetArea;"), "the close pointer");
        AssertTrue(HasLine(src, "var distanceOnly = __instance.mostvaluealbeaiareaclosedistanceonly;"), "the distance-only pointer (14243-14248 reads it when the source area has no enemy)");
        AssertTrue(HasLine(src, "if (distanceOnly != null && aifaction < distanceOnly.Length) distanceOnly[aifaction] = targetArea;"), "...is set too, bound-checked");
        AssertFalse(src.Contains("ActivePlan") || src.Contains("CICs"), "the phased-plan CIC is no longer read");
        AssertTrue(src.Contains("CampaignPlannerRuntime.ExecutorsEnabled()"), "rollback: Enable Campaign Planner Executors (with Enable Campaign Planner)");
    }

    private static void PickObjectiveStandsDownForTheExecutors()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Patches/PickCampaignObjectivePatch.cs");
        string body = BodyOf(src, "internal static bool Prefix(int _aifaction)");
        int mark = body.IndexOf("PatchFirstFire.Mark(\"campaign.patch.pick-campaign-objective\");", StringComparison.Ordinal);
        int stand = body.IndexOf("if (CampaignPlannerRuntime.ExecutorsEnabled()) return true;", StringComparison.Ordinal);
        AssertTrue(mark >= 0 && stand > mark, "the first-fire mark comes first, then the stand-down (P10: one writer per surface)");
        AssertTrue(HasLine(body, "if (CampaignPlannerRuntime.ExecutorsEnabled()) return true;"), "whole line");
        AssertTrue(src.Contains("internal static void NoteWrittenByMod(int alliance, int objectiveId)"), "the executor records its writes here");
        AssertTrue(src.Contains("SnapshotWrittenByMod") && src.Contains("RestoreWrittenByMod"), "the coordinator's sidecar calls keep their statics until G6");
    }

    private static void ReviewPlanStampsTheClockAndPublishesTheContext()
    {
        string body = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs"), "internal static void ReviewPlan(");
        int stamp = body.IndexOf("PlanFreshness.NoteToday(daySerial);", StringComparison.Ordinal);
        int stand = body.IndexOf("if (mode == PlannerReviewMode.StandDown) return;", StringComparison.Ordinal);
        int publish = body.IndexOf("CampaignPlanStore.PublishExecutorContext(alliance, ExecutorContextOf(alliance, snapshot));", StringComparison.Ordinal);
        int store = body.IndexOf("CampaignPlanStore.Publish(alliance, plan, allocation, sectors);", StringComparison.Ordinal);
        AssertTrue(stamp >= 0 && stand > stamp, "the clock is noted in every mode, before the stand-down and bookkeeping returns (a paused alliance's plan ages out)");
        AssertTrue(store >= 0 && publish > store, "the context joins the plan's publish");
        AssertTrue(HasLine(body, "PlanFreshness.NoteToday(daySerial);"), "whole line");
    }

    // The tempo roll's inputs are vanilla's (14303-14347; #38's mirror reads the same): each statement, whole line.
    private static void TempoWorldReadsVanillasInputs()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMoverRuntime.cs");
        string body = BodyOf(src, "private static TempoWorld ReadTempoWorld(");
        AssertTrue(HasLine(body, "ChapterAggression = CampaignTempoGate.ChapterAggression(GamePrefs.aiagressiveness, Policy.CurrentChapter),"), "the chapter's aggression (14339)");
        AssertTrue(HasLine(body, "FactionAggression = factionPrefs != null && alliance >= 0 && alliance < factionPrefs.Length ? factionPrefs[alliance] : 1f,"), "the faction's, 1 above alliance 1 (14317-14319)");
        AssertTrue(HasLine(body, "var factionPrefs = GamePrefs.aiagressivenessoffensiveoperationfaction;"), "from the offensive-operation faction prefs");
        AssertTrue(HasLine(body, "CampaignAggression = GameVars.usedcampaignagressiveness,"), "the campaign's (14339)");
        AssertTrue(HasLine(body, "WinterModifier = WinterOffensiveModifierPatch.CurrentRollModifier(),"), "the winter factor #38's mirror reads (14310-14313 under BUG-LOG-004)");
        AssertTrue(HasLine(body, "WlAggressiveness = GamePrefs.dlcwl_careerincreasedaggressiveness,"), "the W&L multiplier (14344)");
        AssertTrue(HasLine(body, "Forced = GameVars.debug_permanentaimovement || CampaignTempoGate.ForcedInvasion(GamePrefs.leveltoload, CampaignObjective.GetObjectiveAccomplishment(6), alliance, date),"), "the scripted push or the debug flag (14303-14306, 14350)");
        AssertTrue(HasLine(body, "float date = GameVars.GetCampaignFloatingDate();"), "the campaign date (14301)");
        AssertTrue(HasLine(body, "CampaignDate = date"), "the idle clock's date (14327)");
        string execute = BodyOf(src, "internal static void Execute(");
        AssertTrue(HasLine(execute, "Inputs.WlPlayerAlliance = wlActive && GameVars.playeralliance == alliance;"), "the W&L player's alliance");
        AssertTrue(HasLine(execute, "Inputs.Chapter = Policy.CurrentChapter;"), "the chapter (telemetry)");
        AssertTrue(HasLine(execute, "var tally = CampaignMover.Review(Inputs, Host, MoveTrackers.For(alliance), state);"), "the pure pass over the alliance's tracker");
        AssertTrue(HasLine(execute, "if (tally.TempoFirstRow || tally.TempoReleaseRow) TelemetryRouter.LegacyInfo(tally.TempoRow(alliance), TelemetryLayer.Campaign);"), "the tempo row");
        AssertTrue(HasLine(execute, "if (tally.Any) TelemetryRouter.LegacyInfo(tally.Row(alliance), TelemetryLayer.Campaign);"), "the mover row, only when a count is above 0");
        AssertTrue(HasLine(execute, "Inputs.Snapshot = snapshot;") && HasLine(execute, "Inputs.Snapshot = null;"), "the pooled inputs are released after the pass");
    }

    // The host's vanilla reads: public map facts only (the state's owner, the war, the final waypoint test).
    private static void MoverHostReadsVanillasPublicFacts()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMoverRuntime.cs");
        AssertTrue(HasLine(src, "int state = BattlefieldSetup.GetStateOfField(new Vector3(x, 0f, z), usecloseststate: true);"), "the state at the point (25714; as 14486 and 14335)");
        AssertTrue(HasLine(src, "return GameVars.nation[state].alliance;"), "its owner");
        AssertTrue(HasLine(src, "return GameVars.alliance[_alliance].IsAtWarWith(otherAlliance);"), "at war (63127)");
        AssertTrue(HasLine(src, "return bfs.IsTerrainPositionRestricted(new Vector3(x, 0f, z), _alliance);"), "the final waypoint's test (25755)");
        AssertTrue(HasLine(src, "if (state < 0 || GameVars.nation == null || state >= GameVars.nation.Length) return -1;"), "bound-checked state");
        AssertTrue(HasLine(src, "return unit != null && unit.alliance == _alliance ? unit : null;"), "only the reviewing alliance's own groups are touched");
        AssertTrue(HasLine(src, "if (listed && !present) _offensive.Add(unit);"), "added to unitsinoffensiveoperations");
        AssertTrue(HasLine(src, "else if (!listed && present) _offensive.Remove(unit);"), "removed from it");
        AssertTrue(src.Contains("VanillaFieldCache.List(_factionObject, \"unitsinoffensiveoperations\")"), "the offensive list, by cached field");
        string planner = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        AssertTrue(HasLine(planner, "return ExecutorContext.From(States[alliance], PlannerChapterRules.ExecutorsAllowed(GamePrefs.leveltoload, Policy.CurrentChapter), mapGroup);"), "the pre-war gate reaches the published context");
        AssertTrue(HasLine(planner, "if (snapshot.Own[i].Chain == WlChainClass.PlayerMapGroup) mapGroup = snapshot.Own[i].InstanceId;"), "the map-group player's formation");
    }

    private static void SteeringRuntimeWritesWhatTheRulesDecide()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSteeringRuntime.cs");
        AssertTrue(HasLine(src, "int write = SteeringRules.ObjectiveWrite(plan.MainObjectiveId, ObjectiveAvailable(snapshot, plan.MainObjectiveId), current, PickCampaignObjectivePatch.WrittenByMod(alliance));"), "the objective rule's inputs");
        AssertTrue(HasLine(src, "int current = Convert.ToInt32(field.GetValue(faction));"), "what is followed now");
        AssertTrue(HasLine(src, "field.SetValue(faction, write);"), "written once");
        AssertTrue(HasLine(src, "if (write == CampaignObjectiveSteering.NoWrite) return write;"), "only when the rule says so");
        AssertTrue(HasLine(src, "if (s.Objectives[i].ObjectiveId == objectiveId && s.Objectives[i].Available) return true;"), "GetAvailableObjectives as the snapshot recorded it");
        AssertTrue(HasLine(src, "SteeringRules.PlanTheaterWrites(snapshot, allocation, sectors, limiter, Writes);"), "the box rule");
        AssertTrue(HasLine(src, "limiter.Record(write.InstanceId, snapshot.DaySerial);"), "each write is recorded for the weekly cap");
        AssertTrue(HasLine(src, "if (unit == null || unit.alliance != alliance) continue;"), "only the alliance's own groups");
    }

    private static void SnapshotReadsTheIdleClock()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSnapshotRuntime.cs");
        AssertTrue(HasLine(src, "TimeOfNotMoving = reg.timeofnotmoving"), "Regiment.timeofnotmoving (110614): the campaign date the group last moved");
        AssertTrue(HasLine(src, "OwnById[reg.GetInstanceID()] = reg;"), "the executors' lookup, filled with the own formations");
        string patch = ReadRepoFile("src/WhiskeyRealism/Patches/ImportanceValuesPatch.cs");
        foreach (string raw in patch.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("//")) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b(points|distancepoints)\b"))
                AssertContains(line, "// vanilla's own 10984 gate", "every read of vanilla's area aggregates is annotated as its own gate (the fog sweep's expected residue): " + line);
        }
    }

    private static void ReviewExecuteWiringPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string body = BodyOf(src, "internal static void ReviewExecute(");
        int appointments = body.IndexOf("CommanderAppointmentRuntime.Execute(alliance, Snapshots[alliance]);", StringComparison.Ordinal);
        int gate = body.IndexOf("PlannerChapterRules.ExecutorsAllowed(", StringComparison.Ordinal);
        int steering = body.IndexOf("CampaignSteeringRuntime.Execute(", StringComparison.Ordinal);
        int mover = body.IndexOf("CampaignMoverRuntime.Execute(", StringComparison.Ordinal);
        AssertTrue(appointments >= 0 && gate > appointments, "the pre-war gate follows G2's appointments");
        AssertTrue(steering > gate && mover > gate, "and precedes every move, order and steering executor");
        int todays = body.IndexOf("PlanFreshness.IsToday(todaysPlan.DaySerial)", StringComparison.Ordinal);
        AssertTrue(todays > gate && todays < steering, "the executors act on today's plan only (a failed Plan job leaves yesterday's)");
        AssertTrue(steering < mover, "the objective and theater steering run before the mover");
        AssertTrue(HasLine(body, "CampaignPlanStore.PublishExecutorContext(alliance, ExecutorContextOf(alliance, snapshot));") || body.Contains("PublishExecutorContext("), "the tempo release reaches the published context");
    }
}

// Builders shared by CampaignExecutorTests and CampaignTempoGateTests (the repo's per-class fixtures, kept in one place for G5a).
internal static class ExecutorFixtures
{
    internal static readonly int Day = ReviewCadenceCatchUp.DaySerial(15, 6, 1861);
    internal const float Date = 1861.6f;

    internal static CampaignSnapshot Line(int alliance, int areas, int dayOffset = 0)
    {
        var s = new CampaignSnapshot();
        s.Reset(alliance, alliance, Day + dayOffset, PlayerCommandClass.NotCareer, -1, true, null);
        s.RelocationRange = 150f;
        s.OffensiveDominance = 1.1f;
        s.MinStrengthForMovement = 1000f;
        s.MinMoraleForMovement = 0.3f;
        s.HasCapital = true;
        for (int i = 0; i < areas; i++)
            s.Areas.Add(new AreaRecord { Index = 10 + i, X = i * 100f, Z = 0f, EdgeX = new[] { i * 100f }, EdgeZ = new[] { 0f }, Importance = 1f, SupplyCapacity = 100f });
        s.Supply.Add(Iip(501, 100f, alliance));
        s.Supply.Add(Iip(503, 300f, alliance));
        return s;
    }

    // Seven areas (x 0..600), objectives at 100 / 300 / 500; own capital at x 0, own IIPs at 100, 300 and 500; the
    // west (x <= 150) is alliance 1's ground in the fake host.
    internal static CampaignSnapshot Wide(int dayOffset = 0)
    {
        var s = Line(0, 7, dayOffset);
        s.Supply.Add(Iip(505, 500f, 0));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 98, X = 100f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 1f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 99, X = 300f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 97, X = 500f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0f });
        return s;
    }

    internal static FormationRecord Corps(int id, float x, float strength)
    {
        return new FormationRecord
        {
            InstanceId = id, UnitKey = "corps" + id + ":" + id, UnitTyp = 15, X = x, Z = 0f, WaypointX = x, WaypointZ = 0f,
            Strength = strength, Morale = 1f, Supply01 = 1f, ReadinessStep = 3, CommandRange = 40f, BugleRange = 5f, CommanderId = id,
            AreaIndex = -1, Flags = FormationFlags.FightingForce, Name = "corps" + id, ParentInstanceId = -1,
            UseRails = true, UseRivers = true, GroupSpeed = 1f,
            TimeOfNotMoving = Date // moved today: no idle term unless a test sets one
        };
    }

    internal static SupplySourceRecord Iip(int id, float x, int owner)
    {
        return new SupplySourceRecord { Id = id, Kind = SupplySourceKind.Iip, Owner = owner, X = x, Z = 0f, TransportCapacity = 10f, Condition = 1f, Built = true };
    }

    internal static EnemyBelief Belief(float x, float z, float strength)
    {
        return new EnemyBelief { InstanceId = -1, UnitKey = "enemy:" + x, UnitTyp = 15, X = x, Z = z, Strength = strength, Confidence01 = 1f, LastSeenDay = Day };
    }

    internal static MarchSpeedInputs Speed()
    {
        var mounted = new float[19];
        for (int t = 14; t <= 16; t++) mounted[t] = 2f;
        return new MarchSpeedInputs
        {
            MountedBaseSpeed = mounted, BasicUnitSpeed = 1000f, CampaignSpeedFactor = 30f,
            WalkCost = 4f, RiverCost = 1f, RoadCost = 2f, RailCost = 1f,
            RailroadSpeedAdj = 1f, TrainCapacityFree = 0f, TrainCapacityPerMan = 0.001f
        };
    }

    internal static SectorModel Sectors(CampaignSnapshot s) => SectorModel.Build(s);

    internal static TheaterPlan RealPlan(CampaignSnapshot s, PlannerState state)
    {
        return TheaterPlanner.Plan(s, Sectors(s), null, state, null, null, Speed(), out _);
    }

    // A hand-built main effort at objective 98 (sector of x 100) over the given mass.
    internal static TheaterPlan HandPlan(CampaignSnapshot s, int[] mass)
    {
        var plan = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial };
        int sector = Sectors(s).SectorAt(100f, 0f);
        plan.MainSectorId = sector;
        plan.MainObjectiveId = 98;
        plan.MainRequiredRatio = 1.1f;
        plan.MainEnemyStrength = EnemyBeliefQuery.StrengthWithin(s.Beliefs, 100f, 0f, s.RelocationRange);
        plan.Sectors.Add(new SectorPlan { SectorId = sector, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f, RequiredRatio = 1.1f });
        plan.MassInstanceIds.AddRange(mass);
        return plan;
    }

    internal static Assignment Assign(int id, FormationRole role, int sector, float tx, float tz)
    {
        return new Assignment { InstanceId = id, Role = role, SectorId = sector, TargetX = tx, TargetZ = tz, Reason = "t", ReconMissionId = -1 };
    }

    internal static PlannerState State()
    {
        return new PlannerState { DecisionSerial = 1, CampaignSeed = 1861L };
    }

    // The tempo world in which every roll passes (the chapter term alone exceeds 1).
    internal static TempoWorld OpenTempo()
    {
        return new TempoWorld { ChapterAggression = 1000f, FactionAggression = 1f, CampaignAggression = 1f, WinterModifier = 1f, WlAggressiveness = 1f, CampaignDate = Date };
    }

    // The tempo world in which no roll passes: no chapter aggression and a mass that moved today (idle 0).
    internal static TempoWorld ClosedTempo()
    {
        return new TempoWorld { ChapterAggression = 0f, FactionAggression = 1f, CampaignAggression = 1f, WinterModifier = 1f, WlAggressiveness = 1f, CampaignDate = Date };
    }

    internal static MoverInputs Inputs(CampaignSnapshot s, TheaterPlan plan, Allocation allocation, TempoWorld tempo)
    {
        return new MoverInputs { Snapshot = s, Plan = plan, Allocation = allocation, WlPlayerAlliance = false, Chapter = 1, Tempo = tempo };
    }

    internal sealed class World
    {
        public int Main, Support, MapGroupSupport, Reserve, CapitalGuard, FreeHold, HoldAtNeed;
        public bool HoldAtNeedWithheld;
        public float HoldSectorX;
    }

    // A published plan, allocation, sector model and executor context (decision 4 deferred, not released) for the
    // withhold view: corps 1 MainEffort, 2 Support, 3 map-group Support, 4 Reserve, 5 CapitalGuard, 6 Hold released
    // above its need, 7 Hold withheld at need in the sector of x 550.
    internal static World PublishedWorld(out MoveTracker tracker)
    {
        CampaignScope.ResetAll();
        var s = Wide();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 330f, 9000f));
        var player = Corps(3, 340f, 6000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        s.Own.Add(Corps(4, 210f, 9000f));
        s.Own.Add(Corps(5, 10f, 9000f));
        s.Own.Add(Corps(6, 550f, 9000f));
        s.Own.Add(Corps(7, 560f, 9000f));
        var sectors = Sectors(s);
        var plan = HandPlan(s, new[] { 1, 4 });
        plan.DaySerial = Day;
        int east = sectors.SectorAt(550f, 0f), support = sectors.SectorAt(330f, 0f);
        plan.Sectors.Add(new SectorPlan { SectorId = support, Posture = TheaterPosture.Support, TargetObjectiveId = 98, TargetX = 100f, TargetZ = 0f });
        plan.Sectors.Add(new SectorPlan { SectorId = east, Posture = TheaterPosture.Hold, TargetObjectiveId = -1, TargetX = 550f, TargetZ = 0f, HoldNeed = 9000f });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        a.Assignments.Add(Assign(2, FormationRole.Support, support, 100f, 0f));
        a.Assignments.Add(Assign(3, FormationRole.Support, support, 100f, 0f));
        var reserve = Assign(4, FormationRole.Reserve, plan.MainSectorId, 210f, 0f);
        reserve.Withheld = true;
        a.Assignments.Add(reserve);
        var guard = Assign(5, FormationRole.CapitalGuard, 0, 0f, 0f);
        guard.Withheld = true;
        a.Assignments.Add(guard);
        a.Assignments.Add(Assign(6, FormationRole.Hold, east, 550f, 0f));
        var atNeed = Assign(7, FormationRole.Hold, east, 560f, 0f);
        atNeed.Withheld = true;
        a.Assignments.Add(atNeed);
        PlanFreshness.NoteToday(Day);
        CampaignPlanStore.Publish(0, plan, a, sectors);
        CampaignPlanStore.PublishExecutorContext(0, new ExecutorContext { DecisionSerial = 1, ReleasedDecision = -1, ExecutorsAllowed = true, MapGroupInstanceId = 3 });
        tracker = MoveTrackers.For(0);
        return new World { Main = 1, Support = 2, MapGroupSupport = 3, Reserve = 4, CapitalGuard = 5, FreeHold = 6, HoldAtNeed = 7, HoldAtNeedWithheld = true, HoldSectorX = 560f };
    }

    internal static void Release(int alliance)
    {
        CampaignPlanStore.TryGetExecutorContext(alliance, out var ctx);
        ctx.ReleasedDecision = ctx.DecisionSerial;
        CampaignPlanStore.PublishExecutorContext(alliance, ctx);
    }
}

// The vanilla actions the mover pass performs, recorded.
internal sealed class FakeMoverHost : IMoverHost
{
    public int Own;
    public Func<float, float, int> State = (x, z) => x <= 150f ? 1 : 0;
    public Func<float, float, bool> Restricted = (x, z) => false;
    public readonly HashSet<int> Locked = new HashSet<int>();
    public bool MoveResult = true;
    public readonly List<(int id, float x, float z)> Moves = new List<(int id, float x, float z)>();
    public readonly List<(int id, float x, float z)> Orders = new List<(int id, float x, float z)>();
    public readonly List<(int id, bool listed)> Listed = new List<(int id, bool listed)>();
    public readonly List<int> Raised = new List<int>();

    public bool TargetRestricted(float x, float z) { return Restricted(x, z); }
    public int StateAllianceAt(float x, float z) { return State(x, z); }
    public bool AtWar(int otherAlliance) { return otherAlliance != Own && otherAlliance >= 0 && otherAlliance <= 1; }
    public bool PackageLocked(int instanceId) { return Locked.Contains(instanceId); }
    public bool Move(int instanceId, float x, float z) { Moves.Add((instanceId, x, z)); return MoveResult; }
    public bool IssuePlayerOrder(int instanceId, float x, float z) { Orders.Add((instanceId, x, z)); return true; }
    public void ListOffensive(int instanceId, bool listed) { Listed.Add((instanceId, listed)); }
    public void RaiseTransportModes(int instanceId) { Raised.Add(instanceId); }
}
