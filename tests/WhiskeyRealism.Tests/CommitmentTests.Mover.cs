using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// F7, the mover: the stop of an ended push's corps (vanilla's own campaign stop, spending no move), the judging of a stop, the readiness skip
// (no new march and no retry onto ground vanilla would refuse), and the offensive list. The fake host is CampaignExecutorTests.cs's.
internal static partial class CommitmentTests
{
    private static (string name, Action run)[] MoverCases()
    {
        return new (string name, Action run)[]
        {
            ("ACommittedWaitIsStoppedWhereItStands", ACommittedWaitIsStoppedWhereItStands),
            ("OnlyAMarchingFormationIsStopped", OnlyAMarchingFormationIsStopped),
            ("AQueuedOrderIsRemovedByTheStop", AQueuedOrderIsRemovedByTheStop),
            ("TheStopRespectsMayMoveAndMovable", TheStopRespectsMayMoveAndMovable),
            ("TheStopIsServedAfterCapitalGuard", TheStopIsServedAfterCapitalGuard),
            ("TheStopSpendsNoMoveAndWritesOneIssueRow", TheStopSpendsNoMoveAndWritesOneIssueRow),
            ("TheStopNeverWaitsForTheDailyCap", TheStopNeverWaitsForTheDailyCap),
            ("AWorkingStopIsJudgedConfirmedAndAFailedOneOnce", AWorkingStopIsJudgedConfirmedAndAFailedOneOnce),
            ("AStopWithAnOrderRunningIsJudgedNotSkipped", AStopWithAnOrderRunningIsJudgedNotSkipped),
            ("TheStopIsNotTheMoversMoveForRoleOwnership", TheStopIsNotTheMoversMoveForRoleOwnership),
            ("TheStopIsVanillasOwnStopPair", TheStopIsVanillasOwnStopPair),
            ("CampaignStopCallersAreNamed", CampaignStopCallersAreNamed),
            ("NoNewMarchAtStepOne", NoNewMarchAtStepOne),
            ("AMovingUnreadyCorpsIsConfirmed", AMovingUnreadyCorpsIsConfirmed),
            ("AFailedRecordKeepsItsRetryWhileUnready", AFailedRecordKeepsItsRetryWhileUnready),
            ("LowReadinessAloneNeverGivesUp", LowReadinessAloneNeverGivesUp),
            ("AFailedRecordIsNotWithheldFromDefence", AFailedRecordIsNotWithheldFromDefence),
            ("HeldReadinessCountedAfterTheArrivedCheck", HeldReadinessCountedAfterTheArrivedCheck),
            ("TheReadinessSkipAsksVanillasEnemySoilTest", TheReadinessSkipAsksVanillasEnemySoilTest),
            ("APointOnNoStateCountsAsEnemyForTheSkip", APointOnNoStateCountsAsEnemyForTheSkip),
            ("AnEndedPushsCorpsIsTakenOffVanillasOffensiveList", AnEndedPushsCorpsIsTakenOffVanillasOffensiveList),
            ("ACapitalGuardIsTakenOffVanillasOffensiveList", ACapitalGuardIsTakenOffVanillasOffensiveList),
            ("FlagOffNeitherDelistsNorStops", FlagOffNeitherDelistsNorStops),
            ("APostedRoleMovesAnEndedFormationToItsPost", APostedRoleMovesAnEndedFormationToItsPost),
            ("TheMoverRowCountsTheStopAndTheHeldReadiness", TheMoverRowCountsTheStopAndTheHeldReadiness),
        };
    }

    // ---- fixtures ----

    private static FormationRecord Marcher(int id, float x, FormationFlags flags = FormationFlags.HasPath, int readiness = 3)
    {
        var f = ExecutorFixtures.Corps(id, x, 9000f);
        f.ReadinessStep = readiness;
        f.Flags |= flags;
        return f;
    }

    private static Assignment Wait(int id, float x)
    {
        return new Assignment { InstanceId = id, Role = FormationRole.Hold, SectorId = -1, TargetX = x, TargetZ = 0f, Reason = ForceAllocator.ReasonCommittedWait };
    }

    private static Allocation Alloc(params Assignment[] assignments)
    {
        var a = new Allocation { Alliance = 0 };
        a.Assignments.AddRange(assignments);
        return a;
    }

    private static TheaterPlan NoPush(CampaignSnapshot s)
    {
        return new TheaterPlan { Alliance = 0, DaySerial = s.DaySerial };
    }

    private static MoverTally Run(CampaignSnapshot s, TheaterPlan plan, Allocation a, FakeMoverHost host, MoveTracker tracker, PlannerState state = null)
    {
        return CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), host, tracker, state ?? ExecutorFixtures.State());
    }

    private static CampaignSnapshot Wide(params FormationRecord[] own)
    {
        var s = ExecutorFixtures.Wide();
        foreach (var f in own) s.Own.Add(f);
        return s;
    }

    // ---- the stop ----

    private static void ACommittedWaitIsStoppedWhereItStands()
    {
        var s = Wide(Marcher(1, 240f));
        var host = new FakeMoverHost();
        var tracker = new MoveTracker();
        var tally = Run(s, NoPush(s), Alloc(Wait(1, 240f)), host, tracker);
        AssertEqual("1", string.Join(",", host.Stops), "vanilla's stop is sent to the marching corps");
        AssertEqual(0, host.Moves.Count, "and no move is made");
        AssertTrue(tracker.IsStop(1) && tracker.InTransit(1), "the tracker holds a stop record, in transit until the next review judges it");
        AssertEqual(1, tally.Stopped, "counted in the tally");
        AssertEqual(1, tally.IssueRows.Count, "with one row");
        AssertContains(tally.IssueRows[0], " role=Hold reason=committed-wait target=240,0 ", "naming the role, the reason and the formation's own position: " + tally.IssueRows[0]);
    }

    private static void OnlyAMarchingFormationIsStopped()
    {
        var s = Wide(Marcher(1, 240f, 0));
        var host = new FakeMoverHost();
        var tracker = new MoveTracker();
        var tally = Run(s, NoPush(s), Alloc(Wait(1, 240f)), host, tracker);
        AssertEqual(0, host.Stops.Count, "neither a path nor a queued order: nothing to stop");
        AssertFalse(tracker.IsStop(1) || tracker.InTransit(1), "and no record");
        AssertEqual(0, tally.Stopped, "not counted");
    }

    // A march still waiting in vanilla's queue is removed with the stop (RemoveOrder drops every queued order of the regiment), and the tracker
    // records a stop, not a queued move.
    private static void AQueuedOrderIsRemovedByTheStop()
    {
        var s = Wide(Marcher(1, 240f, FormationFlags.MoveQueued));
        var host = new FakeMoverHost();
        var tracker = new MoveTracker();
        Run(s, NoPush(s), Alloc(Wait(1, 240f)), host, tracker);
        AssertEqual("1", string.Join(",", host.Stops), "a formation with only an order waiting is stopped: the stop removes the order");
        AssertTrue(tracker.IsStop(1), "recorded as a stop");
        AssertEqual(0, host.Moves.Count, "not as a queued move");
        tracker.TryGetMove(1, out _, out _, out float tx, out _, out _, out _, out _, out string role);
        AssertTrue(tx == 240f && role == "Hold", "at its own position, as the Hold it is");
    }

    private static void TheStopRespectsMayMoveAndMovable()
    {
        var player = Marcher(1, 240f);
        player.Chain = WlChainClass.PlayerCommand;
        var s = Wide(player, Marcher(2, 250f, FormationFlags.HasPath | FormationFlags.InBattle));
        var host = new FakeMoverHost();
        var tally = Run(s, NoPush(s), Alloc(Wait(1, 240f), Wait(2, 250f)), host, new MoveTracker());
        AssertEqual(0, host.Stops.Count, "the player's own command and a formation in battle are never stopped");
        AssertEqual(1, tally.SkippedPlayer, "the first is counted as the player's");
        AssertEqual(1, tally.SkippedBusy, "the second as busy");
    }

    private static void TheStopIsServedAfterCapitalGuard()
    {
        var guard = Marcher(5, 100f, 0);
        var main = Marcher(2, 300f, 0);
        var s = Wide(guard, main, Marcher(1, 240f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 2 });
        var a = Alloc(Wait(1, 240f),
                      new Assignment { InstanceId = 2, Role = FormationRole.MainEffort, SectorId = plan.MainSectorId, TargetX = 100f, TargetZ = 0f, Reason = "mass" },
                      new Assignment { InstanceId = 5, Role = FormationRole.CapitalGuard, SectorId = 0, TargetX = 0f, TargetZ = 0f, Reason = "capital-floor", Withheld = true });
        var host = new FakeMoverHost();
        Run(s, plan, a, host, new MoveTracker());
        AssertEqual("move:5,stop:1,move:2", string.Join(",", host.Calls), "home defence first, then the stop, then the main effort");
    }

    private static void TheStopSpendsNoMoveAndWritesOneIssueRow()
    {
        var s = Wide(Marcher(1, 240f));
        var tracker = new MoveTracker();
        var host = new FakeMoverHost();
        var tally = Run(s, NoPush(s), Alloc(Wait(1, 240f)), host, tracker);
        AssertEqual(0, tally.Issued, "the stop is no issued move");
        for (int i = 10; i < 10 + MoveTracker.MaxMovesPerDay - 1; i++) tracker.RecordIssued(i, 300f, 0f, 100f, 0f, s.DaySerial, queued: false);
        AssertTrue(tracker.ShouldIssue(99, 100f, 0f, s.DaySerial), "the day's whole budget is still there after a stop: five moves and a sixth");
        AssertEqual(1, tally.IssueRows.Count, "one issue row for the stop");
    }

    private static void TheStopNeverWaitsForTheDailyCap()
    {
        int movers = MoveTracker.MaxMovesPerDay + 2;
        var own = new List<FormationRecord>();
        var assignments = new List<Assignment>();
        var plan0 = NoPush(ExecutorFixtures.Wide());
        for (int k = 0; k < movers; k++)
        {
            own.Add(Marcher(20 + k, 300f + k, 0));
            assignments.Add(new Assignment { InstanceId = 20 + k, Role = FormationRole.CapitalGuard, SectorId = 0, TargetX = 0f, TargetZ = 0f, Reason = "capital-floor", Withheld = true });
        }
        for (int k = 0; k < 4; k++)
        {
            own.Add(Marcher(40 + k, 200f + k));
            assignments.Add(Wait(40 + k, 200f + k));
        }
        var s = Wide(own.ToArray());
        var host = new FakeMoverHost();
        var tracker = new MoveTracker();
        var tally = Run(s, plan0, Alloc(assignments.ToArray()), host, tracker);
        AssertEqual(MoveTracker.MaxMovesPerDay, tally.Issued, "the day's other moves are the cap, unchanged");
        AssertEqual(4, host.Stops.Count, "and every ended corps is stopped, whatever the moves the day has left");
        AssertEqual(4, tally.Stopped, "all four counted");
    }

    // ---- the judging of a stop ----

    private static void AWorkingStopIsJudgedConfirmedAndAFailedOneOnce()
    {
        var tracker = new MoveTracker();
        var host = new FakeMoverHost();
        var first = Wide(Marcher(1, 240f), Marcher(2, 250f));
        Run(first, NoPush(first), Alloc(Wait(1, 240f), Wait(2, 250f)), host, tracker);
        var next = ExecutorFixtures.Wide(1);
        next.Own.Add(Marcher(1, 240f, 0));                      // stopped: neither a path nor a queued order
        next.Own.Add(Marcher(2, 250f, FormationFlags.HasPath));   // still marching
        var tally = Run(next, NoPush(next), Alloc(), host, tracker);
        AssertEqual(1, tally.Confirmed, "the stop that worked is confirmed");
        AssertEqual(1, tally.Failed, "the one that did not is a failure, judged and not skipped");
        AssertEqual(1, tally.MissRows.Count, "with its miss row");
        AssertContains(tally.MissRows[0], " role=Hold outcome=failed ", "naming the role and the outcome: " + tally.MissRows[0]);
        AssertFalse(tracker.InTransit(1) || tracker.InTransit(2), "neither is in transit any more");
        var third = ExecutorFixtures.Wide(2);
        third.Own.Add(Marcher(1, 240f, 0));
        third.Own.Add(Marcher(2, 250f, FormationFlags.HasPath));
        var again = Run(third, NoPush(third), Alloc(), host, tracker);
        AssertEqual(0, again.Failed + again.GaveUp, "a failed stop is judged once and never retried");
    }

    private static void AStopWithAnOrderRunningIsJudgedNotSkipped()
    {
        var tracker = new MoveTracker();
        var host = new FakeMoverHost();
        var first = Wide(Marcher(1, 240f));
        Run(first, NoPush(first), Alloc(Wait(1, 240f)), host, tracker);
        var next = ExecutorFixtures.Wide(1);
        next.Own.Add(Marcher(1, 240f, FormationFlags.MoveQueued));   // an order is running again
        var tally = Run(next, NoPush(next), Alloc(), host, tracker);
        AssertEqual(1, tally.Failed, "the shipped judge loop skips a formation with an order running; a stop is judged all the same");
    }

    private static void TheStopIsNotTheMoversMoveForRoleOwnership()
    {
        AssertFalse(CampaignMover.MovesRole(FormationRole.Hold), "a Hold is never a role the mover sends: the recon executor's ownership test and the give-up fallback read it, and the stop is no march");
        AssertTrue(CampaignMover.MovesRole(FormationRole.Redeploy) && CampaignMover.MovesRole(FormationRole.CapitalGuard), "the marching roles still are");
    }

    // ---- the stop is vanilla's own ----

    private static void TheStopIsVanillasOwnStopPair()
    {
        string host = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/VanillaMoveHost.cs");
        string stop = string.Join("\n", PlannerReadCensusTests.CodeLines(BodyOf(host, "public bool Stop(int instanceId)")));
        int stopCall = stop.IndexOf("StopRegiment(skipfinalrotation: true, manualstop: true, skipmarchcolumnstop: false, skipadvancefallbackstop: false, clearregimentpathsbeforerotation: false, playstopbugle: false)", StringComparison.Ordinal);
        int remove = stop.IndexOf("AICampaign.DefensiveMovingOrder.RemoveOrder(", StringComparison.Ordinal);
        AssertTrue(stopCall >= 0 && remove > stopCall, "StopRegiment(skipfinalrotation: true, manualstop: true, ...) then DefensiveMovingOrder.RemoveOrder, as 13350-13351");
        AssertFalse(stop.Contains("MoveUnitTo") || stop.Contains("SetWaypoint"), "no move and no waypoint: vanilla's readiness refusal is not met");
        string mover = string.Join("\n", PlannerReadCensusTests.CodeLines(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMover.cs")));
        AssertEqual(1, mover.Split(new[] { "host.Stop(" }, StringSplitOptions.None).Length - 1, "the mover sends the stop through IMoveHost.Stop, in one place");
        AssertFalse(mover.Contains("StopRegiment(") || mover.Contains("RemoveOrder("), "and never calls vanilla's stop or its order removal itself");
    }

    // The planner's one caller of the campaign stop is VanillaMoveHost; the shipped coastal runner is the other, for its own defensive-operation
    // units, which the stop never reaches (InDefensiveOps is Busy, so such a unit is not Movable).
    private static void CampaignStopCallersAreNamed()
    {
        var offenders = new List<string>();
        string root = FindRepoPath("src/WhiskeyRealism/Strategic");
        foreach (string path in System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
        {
            string name = System.IO.Path.GetFileName(path);
            if (name == "VanillaMoveHost.cs" || name == "CoastalDefenseCustomOrderRunner.cs") continue;
            foreach (string line in PlannerReadCensusTests.CodeLines(System.IO.File.ReadAllText(path)))
                if (line.Contains("StopRegiment(") || line.Contains("DefensiveMovingOrder.RemoveOrder(")) offenders.Add(name);
        }
        AssertEqual("", string.Join(",", offenders.Distinct()), "StopRegiment( and DefensiveMovingOrder.RemoveOrder( are called under Strategic/ from the two named files alone");
        AssertTrue(PlannerReadCensusTests.CodeLines(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/VanillaMoveHost.cs")).Any(l => l.Contains("StopRegiment(")), "VanillaMoveHost.cs is one");
        AssertTrue(PlannerReadCensusTests.CodeLines(ReadRepoFile("src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs")).Any(l => l.Contains("StopRegiment(")), "CoastalDefenseCustomOrderRunner.cs is the other");
    }

    // ---- the readiness skip ----

    private static MoveTracker TrackerAfterFirstMarch(CampaignSnapshot s, FakeMoverHost host, TheaterPlan plan, Allocation a)
    {
        var tracker = new MoveTracker();
        Run(s, plan, a, host, tracker);
        return tracker;
    }

    private static (CampaignSnapshot s, TheaterPlan plan, Allocation a) PushAt(int readiness, float x = 300f)
    {
        var s = Wide(Marcher(2, x, 0, readiness));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 2 });
        var a = Alloc(new Assignment { InstanceId = 2, Role = FormationRole.MainEffort, SectorId = plan.MainSectorId, TargetX = 100f, TargetZ = 0f, Reason = "mass" });
        return (s, plan, a);
    }

    private static void NoNewMarchAtStepOne()
    {
        foreach (int step in new[] { 0, 1 })
        {
            var (s, plan, a) = PushAt(step);
            var host = new FakeMoverHost();
            var tracker = new MoveTracker();
            var tally = Run(s, plan, a, host, tracker);
            AssertEqual(0, host.Moves.Count, "step " + step + ": no march onto ground vanilla's SetWaypoint would refuse in silence (91336-91350)");
            AssertFalse(tracker.InTransit(2), "step " + step + ": and no record");
            AssertEqual(1, tally.HeldReadiness, "step " + step + ": counted as held for readiness");
            AssertEqual(0, tally.Deferred, "not as a tempo deferral");
        }
        var (s2, plan2, a2) = PushAt(2);
        var ready = new FakeMoverHost();
        Run(s2, plan2, a2, ready, new MoveTracker());
        AssertEqual(1, ready.Moves.Count, "step 2 marches");
        var (s3, plan3, a3) = PushAt(1);
        var own = new FakeMoverHost { EnemySoil = (x, z) => false };
        Run(s3, plan3, a3, own, new MoveTracker());
        AssertEqual(1, own.Moves.Count, "and an unready corps marches over ground that is not enemy soil");
    }

    private static void AMovingUnreadyCorpsIsConfirmed()
    {
        var (s, plan, a) = PushAt(3);
        var host = new FakeMoverHost();
        var tracker = TrackerAfterFirstMarch(s, host, plan, a);
        var next = ExecutorFixtures.Wide(1);
        var f = Marcher(2, 260f, FormationFlags.HasPath, 1);   // the march dropped its readiness; it is on its way
        f.WaypointX = 100f;
        next.Own.Add(f);
        var plan2 = ExecutorFixtures.HandPlan(next, new[] { 2 });
        var tally = Run(next, plan2, a, host, tracker);
        AssertEqual(1, tally.Confirmed, "an issued march continues at low readiness (113937-113965 clears no path): confirmed");
        AssertEqual(0, tally.Failed, "not failed");
    }

    private static void AFailedRecordKeepsItsRetryWhileUnready()
    {
        var (s, plan, a) = PushAt(3);
        var host = new FakeMoverHost();
        var tracker = TrackerAfterFirstMarch(s, host, plan, a);
        var stuck = ExecutorFixtures.Wide(1);
        stuck.Own.Add(Marcher(2, 300f, 0, 1));                // the march never started and the readiness fell
        var plan2 = ExecutorFixtures.HandPlan(stuck, new[] { 2 });
        var tally = Run(stuck, plan2, a, host, tracker);
        AssertEqual(1, tally.Failed, "the first miss is a failure");
        AssertEqual(1, host.Moves.Count, "and no retry is sent while the formation is unready: the skip is the retry withheld");
        AssertEqual(1, tally.HeldReadiness, "counted as held");
        var ready = ExecutorFixtures.Wide(2);
        ready.Own.Add(Marcher(2, 300f, 0, 3));
        var plan3 = ExecutorFixtures.HandPlan(ready, new[] { 2 });
        var retry = Run(ready, plan3, a, host, tracker);
        AssertEqual(1, retry.Retried, "back at step 2 the retry it still owes is sent");
        AssertEqual(2, host.Moves.Count, "as the one retry");
    }

    private static void LowReadinessAloneNeverGivesUp()
    {
        var (s, plan, a) = PushAt(3);
        var host = new FakeMoverHost();
        var tracker = TrackerAfterFirstMarch(s, host, plan, a);
        int gaveUp = 0;
        for (int day = 1; day <= 6; day++)
        {
            var w = ExecutorFixtures.Wide(day);
            w.Own.Add(Marcher(2, 300f, 0, 1));
            var tally = Run(w, ExecutorFixtures.HandPlan(w, new[] { 2 }), a, host, tracker);
            gaveUp += tally.GaveUp;
        }
        AssertEqual(0, gaveUp, "six reviews at readiness step 1: no give-up, since the retry is withheld and not wasted");
        var list = new List<GaveUpMove>();
        tracker.CollectGaveUp(list, ExecutorFixtures.Day + 6);
        AssertEqual(0, list.Count, "and the tracker holds none");
    }

    private static void AFailedRecordIsNotWithheldFromDefence()
    {
        var (s, plan, a) = PushAt(3);
        var host = new FakeMoverHost();
        var tracker = TrackerAfterFirstMarch(s, host, plan, a);
        var w = ExecutorFixtures.Wide(1);
        w.Own.Add(Marcher(2, 300f, 0, 1));
        Run(w, ExecutorFixtures.HandPlan(w, new[] { 2 }), a, host, tracker);
        AssertFalse(tracker.InTransit(2), "a Failed record is not in transit, so vanilla's defence may use the corps meanwhile");
    }

    // The skip sits after the arrived-or-underway return: a formation whose waypoint is already the target is not counted as held.
    private static void HeldReadinessCountedAfterTheArrivedCheck()
    {
        var (s, plan, a) = PushAt(1, 105f);
        var f = s.Own[0];
        f.WaypointX = 100f;
        s.Own[0] = f;
        var tally = Run(s, plan, a, new FakeMoverHost(), new MoveTracker());
        AssertEqual(0, tally.HeldReadiness, "already underway to the target: the arrived return comes first");
    }

    // 139304-139336 with no closest-state fallback: a point on no state is enemy ground; the occupation test removes ground the side holds.
    private static void TheReadinessSkipAsksVanillasEnemySoilTest()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMoverRuntime.cs");
        string body = string.Join("\n", PlannerReadCensusTests.CodeLines(BodyOf(runtime, "public bool OnEnemySoil(float x, float z)")));
        AssertFalse(body.Contains("usecloseststate"), "a point on no state counts as enemy: there is no closest-state fallback");
        AssertTrue(body.Contains("GetStateOfField(") && body.Contains("occupiedby") && body.Contains("IsAtWarWith("), "the state's alliance at war with this one, less the state this side occupies");
        AssertTrue(body.Contains("CampaignMover.EnemySoilVerdict("), "the facts it reads go to the one verdict the harness tests (APointOnNoStateCountsAsEnemyForTheSkip)");
        string mover = string.Join("\n", PlannerReadCensusTests.CodeLines(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMover.cs")));
        AssertTrue(mover.Contains("host.OnEnemySoil("), "the skip asks it, and not the tempo gate's EnemyHeld");
    }

    // The verdict of IsOnEnemySoil(false, point, returntrueifnotonstate: true, usestateoccupation: true) (139304-139336) on the facts the runtime
    // reads, branch by branch (the runtime file reads Unity's tables and is outside the harness; the verdict is the pure half).
    private static void APointOnNoStateCountsAsEnemyForTheSkip()
    {
        const int us = 0, them = 1;
        AssertTrue(CampaignMover.EnemySoilVerdict(true, onAState: false, allianceKnown: false, atWar: false, occupiedBy: -1, ownAlliance: us),
            "a point on no state counts as enemy ground (returntrueifnotonstate, no closest-state fallback), whatever else is known");
        AssertTrue(CampaignMover.EnemySoilVerdict(true, onAState: true, allianceKnown: true, atWar: true, occupiedBy: them, ownAlliance: us),
            "a state of an alliance at war with us, held by its owner: enemy ground");
        AssertFalse(CampaignMover.EnemySoilVerdict(true, onAState: true, allianceKnown: true, atWar: true, occupiedBy: us, ownAlliance: us),
            "the same state once we occupy it (usestateoccupation): ours");
        AssertFalse(CampaignMover.EnemySoilVerdict(true, onAState: true, allianceKnown: true, atWar: false, occupiedBy: them, ownAlliance: us),
            "a state of an alliance not at war with us: no enemy");
        AssertFalse(CampaignMover.EnemySoilVerdict(true, onAState: true, allianceKnown: false, atWar: false, occupiedBy: them, ownAlliance: us),
            "a state whose alliance index is out of range: no enemy");
        AssertFalse(CampaignMover.EnemySoilVerdict(false, onAState: false, allianceKnown: false, atWar: false, occupiedBy: -1, ownAlliance: us),
            "the game's tables missing: no refusal, the mover marches as before (gap 44), even for a point on no state");
    }

    // ---- the offensive list ----

    private static void AnEndedPushsCorpsIsTakenOffVanillasOffensiveList()
    {
        var s = Wide(Marcher(1, 240f, FormationFlags.InOffensiveOps | FormationFlags.HasPath),   // the mover listed it
                     Marcher(2, 250f, FormationFlags.InOffensiveOps | FormationFlags.HasPath),   // vanilla's job 4 listed it first
                     Marcher(3, 260f, 0),                                                       // not listed: nothing to remove
                     Marcher(4, 270f, FormationFlags.InOffensiveOps),                           // listed, and a MainEffort again
                     Marcher(6, 280f, FormationFlags.InOffensiveOps));                          // listed, but not an ended push's corps
        var plan = NoPush(s);
        plan.CommitmentEnded.AddRange(new[] { 1, 2, 3, 4 });
        var a = Alloc(Wait(1, 240f), Wait(2, 250f), Wait(3, 260f),
                      new Assignment { InstanceId = 4, Role = FormationRole.MainEffort, SectorId = 0, TargetX = 280f, TargetZ = 0f, Reason = "mass" });
        var host = new FakeMoverHost();
        Run(s, plan, a, host, new MoveTracker());
        AssertEqual("1,2", string.Join(",", host.Listed.Where(l => !l.listed).Select(l => l.id).OrderBy(i => i)), "an ended push's corps still in the list leave it, the mover's own and a vanilla-listed one alike; a MainEffort again, an unlisted and an unrelated corps are untouched");
    }

    private static void ACapitalGuardIsTakenOffVanillasOffensiveList()
    {
        var s = Wide(Marcher(1, 100f, FormationFlags.InOffensiveOps, 3));
        var a = Alloc(new Assignment { InstanceId = 1, Role = FormationRole.CapitalGuard, SectorId = 0, TargetX = 0f, TargetZ = 0f, Reason = "capital-floor", Withheld = true });
        var host = new FakeMoverHost();
        Run(s, NoPush(s), a, host, new MoveTracker());
        AssertEqual("1", string.Join(",", host.Listed.Where(l => !l.listed).Select(l => l.id)), "vanilla's job 6 re-targets every idle listed unit to an enemy objective: the guard leaves the list");
    }

    private static void FlagOffNeitherDelistsNorStops()
    {
        bool before = PlannerSettings.MainEffortCommitment;
        PlannerSettings.MainEffortCommitment = false;
        try
        {
            var s = Wide(Marcher(1, 240f, FormationFlags.InOffensiveOps | FormationFlags.HasPath), Marcher(5, 100f, FormationFlags.InOffensiveOps));
            var plan = NoPush(s);
            plan.CommitmentEnded.Add(1);
            var a = Alloc(Wait(1, 240f), new Assignment { InstanceId = 5, Role = FormationRole.CapitalGuard, SectorId = 0, TargetX = 0f, TargetZ = 0f, Reason = "capital-floor", Withheld = true });
            var host = new FakeMoverHost();
            var tracker = new MoveTracker();
            var tally = Run(s, plan, a, host, tracker);
            AssertEqual(0, host.Stops.Count, "no stop for an ended push");
            AssertEqual(0, host.Listed.Count(l => !l.listed), "no de-list for it or for a guard");
            AssertEqual(0, tally.Stopped + tally.HeldReadiness, "and no readiness skip");
            var (s2, plan2, a2) = PushAt(1);
            var unready = new FakeMoverHost();
            Run(s2, plan2, a2, unready, new MoveTracker());
            AssertEqual(1, unready.Moves.Count, "the mover sends the unready corps as it did before F7");
        }
        finally { PlannerSettings.MainEffortCommitment = before; }
    }

    // From the next review the role the holds, the capital reserve or department reinforcement give an ended push's formation moves it as
    // that role always does; the stop was only for the corps with no post.
    private static void APostedRoleMovesAnEndedFormationToItsPost()
    {
        var s = Wide(Marcher(1, 240f, FormationFlags.HasPath, 3));
        var a = Alloc(new Assignment { InstanceId = 1, Role = FormationRole.CapitalGuard, SectorId = 0, TargetX = 0f, TargetZ = 0f, Reason = "capital-floor", Withheld = true });
        var plan = NoPush(s);
        plan.CommitmentEnded.Add(1);
        var host = new FakeMoverHost();
        var tally = Run(s, plan, a, host, new MoveTracker());
        AssertEqual(0, host.Stops.Count, "it is not stopped: it has a post");
        AssertEqual("1", string.Join(",", host.Moves.Select(m => m.id)), "it is sent to the post");
        AssertEqual(1, tally.Issued, "as one move");
    }

    private static void TheMoverRowCountsTheStopAndTheHeldReadiness()
    {
        var s = Wide(Marcher(1, 240f));
        var tally = Run(s, NoPush(s), Alloc(Wait(1, 240f)), new FakeMoverHost(), new MoveTracker());
        AssertTrue(tally.Any, "a review that only stopped has a row");
        AssertContains(tally.Row(0), " heldReadiness=0 stopped=1", "the row carries both counts: " + tally.Row(0));
        var (s2, plan2, a2) = PushAt(1);
        var held = Run(s2, plan2, a2, new FakeMoverHost(), new MoveTracker());
        AssertTrue(held.Any, "and one that only held a corps for its readiness");
        AssertContains(held.Row(0), " heldReadiness=1 stopped=0", "with the count: " + held.Row(0));
    }
}
