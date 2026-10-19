using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F7 (D11 requirements commitment, commitment-mover and decision-end; root cause 4): a dispatched member of
// the last mass stays in its objective's capacity through readiness dips for exactly as long as D4 keeps the objective; when D4 ends the push its
// corps are stopped where they stand by vanilla's own campaign stop and taken off vanilla's offensive list. Plan:
// docs/superpowers/plans/2026-10-18-strategic-foresight.md F7; the decisions are docs/loop/foresight/decisions.md (the five open questions, item 1,
// and the fourth round). This file holds the move tracker's half, the claim table's row 4 and the planner; CommitmentTests.Mover.cs the mover's.
internal static partial class CommitmentTests
{
    public static void Run()
    {
        var all = new List<(string name, Action run)>();
        all.AddRange(TrackerCases());
        all.AddRange(PlannerCases());
        all.AddRange(MassCases());
        all.AddRange(MoverCases());
        all.AddRange(RowCases());
        TestHarness.RunAll(all.ToArray());
    }

    // ---- the move tracker: what was dispatched, the stop and how it is judged ----

    private static (string name, Action run)[] TrackerCases()
    {
        return new (string name, Action run)[]
        {
            ("CollectDispatchedListsThePlannersMovesTowardAnObjective", CollectDispatchedListsThePlannersMovesTowardAnObjective),
            ("ADispatchSurvivesALoad", ADispatchSurvivesALoad),
            ("ARestoredRecordWithItsRetryUsedIsNoDispatch", ARestoredRecordWithItsRetryUsedIsNoDispatch),
            ("RecordStopIsAnIssuedRecordAtTheFormationWithNoSpend", RecordStopIsAnIssuedRecordAtTheFormationWithNoSpend),
            ("ALaterMoveOrAStopEndsTheDispatch", ALaterMoveOrAStopEndsTheDispatch),
            ("AWorkingStopIsJudgedConfirmed", AWorkingStopIsJudgedConfirmed),
            ("AStopOfAFormationStillMarchingOrQueuedFailsOnceAndIsNotRetried", AStopOfAFormationStillMarchingOrQueuedFailsOnceAndIsNotRetried),
            ("TheStopIsNeverJudgedByTheDistanceRule", TheStopIsNeverJudgedByTheDistanceRule),
            ("AnOrdinaryMoveIsStillJudgedByTheDistanceWhateverMovingSays", AnOrdinaryMoveIsStillJudgedByTheDistanceWhateverMovingSays),
            ("AStopInTransitWithholdsBothPoolsUntilJudged", AStopInTransitWithholdsBothPoolsUntilJudged),
        };
    }

    private const int Day = 100;

    private static MoveTracker TrackerWithMove(int id, int objective, MoveBudget budget = MoveBudget.Planner)
    {
        var t = new MoveTracker();
        t.RecordIssued(id, 300f, 0f, 100f, 0f, Day, queued: false, unitKey: "u" + id, objectiveId: objective, budget: budget, role: "MainEffort");
        return t;
    }

    private static List<DispatchedMove> Dispatched(MoveTracker t)
    {
        var list = new List<DispatchedMove>();
        t.CollectDispatched(list);
        return list;
    }

    // Requirement-commitment: a member is dispatched when the tracker holds a planner move of it toward an objective in any state but GaveUp.
    private static void CollectDispatchedListsThePlannersMovesTowardAnObjective()
    {
        var t = new MoveTracker();
        t.RecordIssued(5, 300f, 0f, 100f, 0f, Day, queued: false, unitKey: "u5", objectiveId: 98, role: "MainEffort");            // issued
        t.RecordIssued(3, 300f, 0f, 100f, 0f, Day, queued: true, unitKey: "u3", objectiveId: 98, role: "Support");                  // queued behind an order
        t.RecordIssued(4, 300f, 0f, 500f, 0f, Day, queued: false, unitKey: "u4", objectiveId: -1, role: "Garrison");                // no objective
        t.RecordIssued(6, 300f, 0f, 100f, 0f, Day, queued: false, unitKey: "u6", objectiveId: 98, budget: MoveBudget.Recon);        // a scout's
        t.RecordRefused(7, 100f, 0f, Day, "u7", 98, MoveBudget.Planner, "MainEffort");                                               // refused once: a failed attempt owes its retry
        t.RecordRefused(8, 100f, 0f, Day, "u8", 98, MoveBudget.Planner, "MainEffort");
        t.RecordRefused(8, 100f, 0f, Day, "u8", 98, MoveBudget.Planner, "MainEffort");                                               // refused twice: given up
        var list = Dispatched(t);
        AssertEqual("3,5,7", string.Join(",", list.Select(d => d.InstanceId)), "issued, queued and failed moves toward an objective, in instance order; no objective, a scout's and a give-up are none");
        AssertTrue(list.All(d => d.ObjectiveId == 98), "each names the objective it was sent toward");
    }

    private static void ADispatchSurvivesALoad()
    {
        var t = TrackerWithMove(5, 98);
        t.RecordRefused(7, 100f, 0f, Day, "u7", 98, MoveBudget.Planner, "MainEffort");
        var saved = t.Records.ToList();
        var loaded = new MoveTracker();
        loaded.Restore(saved);
        AssertEqual("5,7", string.Join(",", Dispatched(loaded).Select(d => d.InstanceId)), "a restored record whose retry is unused is still the dispatch");
        AssertTrue(Dispatched(loaded).All(d => d.ObjectiveId == 98), "and keeps its ObjectiveId");
    }

    private static void ARestoredRecordWithItsRetryUsedIsNoDispatch()
    {
        var t = new MoveTracker();
        t.Restore(new[] { new MoveRecord { InstanceId = 5, UnitKey = "u5", TargetX = 100f, TargetZ = 0f, IssuedDay = Day, Retries = 1, ObjectiveId = 98 } });
        AssertEqual(0, Dispatched(t).Count, "a restored record that had used its retry is a give-up, and a give-up is no dispatch");
    }

    private static void RecordStopIsAnIssuedRecordAtTheFormationWithNoSpend()
    {
        var t = new MoveTracker();
        t.RecordStop(50, 180f, 0f, Day, "u50");
        for (int i = 1; i < MoveTracker.MaxMovesPerDay; i++) t.RecordIssued(i, 300f, 0f, 100f, 0f, Day, queued: false);
        AssertTrue(t.ShouldIssue(99, 100f, 0f, Day), "a stop spent none: the sixth move of the day is still there after it and five moves");
        t.RecordIssued(6, 300f, 0f, 100f, 0f, Day, queued: false);
        AssertFalse(t.ShouldIssue(98, 100f, 0f, Day), "and the cap is the six moves alone");
        AssertTrue(t.IsStop(50), "the record is a stop");
        AssertTrue(t.InTransit(50), "issued, so the formation is in transit until it is judged");
        t.TryGetMove(50, out float fx, out float fz, out float tx, out float tz, out int issued, out int retries, out int objective, out string role);
        AssertTrue(fx == 180f && tx == 180f && fz == 0f && tz == 0f, "at the formation's own position");
        AssertEqual(Day, issued, "issued today");
        AssertEqual(-1, objective, "toward no objective");
        AssertEqual("Hold", role, "as the Hold the formation now is");
        AssertFalse(t.IsStop(1), "an ordinary move is not one");
    }

    private static void ALaterMoveOrAStopEndsTheDispatch()
    {
        var t = TrackerWithMove(5, 98);
        AssertEqual(1, Dispatched(t).Count, "dispatched");
        t.RecordStop(5, 180f, 0f, Day + 1, "u5");
        AssertEqual(0, Dispatched(t).Count, "the stop replaces the push's record and ends the dispatch");
        var u = TrackerWithMove(6, 98);
        u.RecordIssued(6, 300f, 0f, 500f, 0f, Day + 1, queued: false, unitKey: "u6", objectiveId: -1, role: "Garrison");
        AssertEqual(0, Dispatched(u).Count, "so does a later move toward no objective");
    }

    private static void AWorkingStopIsJudgedConfirmed()
    {
        var t = new MoveTracker();
        t.RecordStop(5, 180f, 0f, Day, "u5");
        AssertEqual(MoveOutcome.None, t.Judge(5, 180f, 0f, 180f, 0f, Day, moving: false), "not judged on the day it was sent");
        AssertEqual(MoveOutcome.Confirmed, t.Judge(5, 180f, 0f, 180f, 0f, Day + 1, moving: false), "neither a path nor a queued order at the next review: it stopped");
        AssertFalse(t.InTransit(5), "judged, so the formation is out of transit");
    }

    private static void AStopOfAFormationStillMarchingOrQueuedFailsOnceAndIsNotRetried()
    {
        var t = new MoveTracker();
        t.RecordStop(5, 180f, 0f, Day, "u5");
        AssertEqual(MoveOutcome.Failed, t.Judge(5, 140f, 0f, 100f, 0f, Day + 1, moving: true), "still marching or with an order running: failed, judged and not skipped");
        AssertFalse(t.InTransit(5), "no longer in transit");
        AssertTrue(t.ShouldIssue(5, 100f, 0f, Day + 1), "the tracker holds nothing against a new move");
        AssertEqual(MoveOutcome.None, t.Judge(5, 140f, 0f, 100f, 0f, Day + 2, moving: true), "a failed stop is judged once: there is no second miss and no give-up");
        var gaveUp = new List<GaveUpMove>();
        t.CollectGaveUp(gaveUp, Day + 2);
        AssertEqual(0, gaveUp.Count, "so it never becomes one");
    }

    // The stop's record sits at the formation's own position, so the distance rule's start distance is 0 and it would confirm anything.
    private static void TheStopIsNeverJudgedByTheDistanceRule()
    {
        var t = new MoveTracker();
        t.RecordStop(5, 180f, 0f, Day, "u5");
        AssertEqual(MoveOutcome.Failed, t.Judge(5, 180f, 0f, 180f, 0f, Day + 1, moving: true), "standing at its record but with a path still running: failed, though the distance rule would confirm it");
    }

    private static void AnOrdinaryMoveIsStillJudgedByTheDistanceWhateverMovingSays()
    {
        var a = TrackerWithMove(5, 98);
        AssertEqual(MoveOutcome.Confirmed, a.Judge(5, 200f, 0f, 150f, 0f, Day + 1, moving: false), "a waypoint toward the target confirms, whatever moving says");
        var b = TrackerWithMove(6, 98);
        AssertEqual(MoveOutcome.Failed, b.Judge(6, 300f, 0f, 300f, 0f, Day + 1, moving: true), "a waypoint that did not move fails, whatever moving says");
    }

    private static void AStopInTransitWithholdsBothPoolsUntilJudged()
    {
        CampaignScope_Reset();
        var tracker = MoveTrackers.For(0);
        tracker.RecordStop(5, 180f, 0f, ExecutorFixtures.Day, "u5");
        var ids = new List<int>();
        tracker.CollectInTransit(ids);
        AssertEqual("5", string.Join(",", ids), "a stop in transit is in the list the withhold view reads");
        CampaignScope_Reset();
    }

    private static void CampaignScope_Reset()
    {
        WhiskeyRealism.Util.CampaignScope.ResetAll();
    }
}
