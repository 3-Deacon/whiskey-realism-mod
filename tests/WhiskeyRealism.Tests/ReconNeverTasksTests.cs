using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;

using static TestHarness;

// The smoke of 2026-10-17 (docs/loop notes build-recon-never-tasks): the Union's [Recon] row read pool=2..7 unwatchedApproaches=3
// screens=probes=advanceGuards=0 on every review, its mass stayed staged ("scouted=no staged=yes") and its mover deferred one
// formation on all 22 reviews. The fixture below is that shape with vanilla's real prefs (aiminimumstrengthformovement 2000,
// morale 0.65, relocation range 200, dominance 0.65 from Config/campaignprefs.txt): two enemy objectives (30 West at 300, 31 East
// at 900), one ready body in no sector that needs a hold (it is in vanilla's capital-defence list, so it cannot scout), unready
// and busy formations that already cover the West and East needs, and one ready 3000-man formation that the East hold reserve
// keeps: the only formation that could scout is the one the hold reserve claimed first.
internal static class ReconNeverTasksTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("TheSmokeShapeHasNothingFreeToScoutWith", TheSmokeShapeHasNothingFreeToScoutWith),
            ("AStagedMainEffortGetsOneScoutFromTheHoldReserve", AStagedMainEffortGetsOneScoutFromTheHoldReserve),
            ("TheHoldReserveIsDrawnOnceNotPerTask", TheHoldReserveIsDrawnOnceNotPerTask),
            ("ANonMainTaskNeverDrawsTheHoldReserve", ANonMainTaskNeverDrawsTheHoldReserve),
            ("AScoutedMainEffortDrawsNothing", AScoutedMainEffortDrawsNothing),
            ("TheLastMassBodyIsNeverSpentOnScouting", TheLastMassBodyIsNeverSpentOnScouting),
            ("TheDrawnScoutKeepsItsPostTheNextReview", TheDrawnScoutKeepsItsPostTheNextReview),
            ("AGaveUpFormationIsNotDrawnForTheTaskItGaveUpOn", AGaveUpFormationIsNotDrawnForTheTaskItGaveUpOn),
            ("TheDrawServesTheMainObjectivesTasksNotThePostureOrBeliefTasks", TheDrawServesTheMainObjectivesTasksNotThePostureOrBeliefTasks),
            ("ASecessionGarrisonIsNeverDrawnAsAScout", ASecessionGarrisonIsNeverDrawnAsAScout),
            ("ScoutsMainObjectiveNamesTheMainTasksOnly", ScoutsMainObjectiveNamesTheMainTasksOnly),
            ("TheUnfilledCountMatchesTasksByKindPointAndReason", TheUnfilledCountMatchesTasksByKindPointAndReason),
            ("TheReconRowCountsTasksAndUnfilled", TheReconRowCountsTasksAndUnfilled),
            ("AScoutDrawnFromTheReserveReturnsWhenTheObjectiveIsScouted", AScoutDrawnFromTheReserveReturnsWhenTheObjectiveIsScouted),
            ("ARetargetWithinTheObjectiveIsNotScoutedByTheOldTarget", ARetargetWithinTheObjectiveIsNotScoutedByTheOldTarget),
            ("RecordsWhoseScoresAlternateStayOnOneRecord", RecordsWhoseScoresAlternateStayOnOneRecord),
            ("ARecordChangesAfterTheMarginHoldsForThreeReviews", ARecordChangesAfterTheMarginHoldsForThreeReviews),
            ("AnInterruptedLeadRestartsTheRecordWindow", AnInterruptedLeadRestartsTheRecordWindow),
            ("AnInvalidKeptRecordIsLeftAtOnce", AnInvalidKeptRecordIsLeftAtOnce),
            ("DifferentChallengersDoNotAddUp", DifferentChallengersDoNotAddUp),
            ("RecordsThatDifferOnlyInZAreDistinctRecords", RecordsThatDifferOnlyInZAreDistinctRecords),
            ("ALeadWithinTheMarginBreaksTheRecordWindow", ALeadWithinTheMarginBreaksTheRecordWindow),
            ("TheKeptRecordHasTheOddsDeadband", TheKeptRecordHasTheOddsDeadband),
            ("TheScoutedLatchFollowsTheKeptRecord", TheScoutedLatchFollowsTheKeptRecord),
            ("TheKeptRecordAndItsChallengerSurviveTheSidecar", TheKeptRecordAndItsChallengerSurviveTheSidecar),
            ("TheScoutedTargetSurvivesTheSidecarAndAnOldFileHasNone", TheScoutedTargetSurvivesTheSidecarAndAnOldFileHasNone),
            ("TheMoverRowNamesAStagedGatheringThatWaits", TheMoverRowNamesAStagedGatheringThatWaits),
            ("AZeroCountedFailureIsNotAFailureRow", AZeroCountedFailureIsNotAFailureRow),
        });
    }

    // ---- fixtures ----

    private static FormationRecord Corps(int id, float x, float strength, int readiness = 3, FormationFlags extra = 0)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.SightRadius = 30f;
        f.Morale = 0.9f;
        f.ReadinessStep = readiness;
        f.Flags |= extra;
        return f;
    }

    private static CampaignSnapshot Base()
    {
        var s = ExecutorFixtures.Line(0, 11);
        s.MinStrengthForMovement = 2000f;
        s.MinMoraleForMovement = 0.65f;
        s.RelocationRange = 200f;
        s.OffensiveDominance = 0.65f;
        for (int i = 1; i <= 8; i++) s.Supply.Add(ExecutorFixtures.Iip(600 + i, 100f * i, 0));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 30, X = 300f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.2f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 31, X = 900f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.8f });
        return s;
    }

    // Body 1: ready, 9563 strong, in vanilla's capital-defence list (CanMove refuses it as a scout; the mass takes it). Unready 9, 10 and
    // the busy 15 cover the West's need (6000); the busy 13, 14 cover two thirds of the East's (9000) and the ready 3000-man
    // formation 2 is the East hold reserve that completes it.
    private static CampaignSnapshot SmokeUnion(FormationFlags bodyFlags = FormationFlags.CapitalDefense, bool withReserve = true, bool secondEastBusy = true)
    {
        var s = Base();
        s.Own.Add(Corps(1, 100f, 9563f, 3, bodyFlags));
        if (withReserve) s.Own.Add(Corps(2, 760f, 3000f));
        s.Own.Add(Corps(9, 200f, 4000f, 1));
        s.Own.Add(Corps(10, 220f, 4000f, 1));
        s.Own.Add(Corps(13, 790f, 3000f, 3, FormationFlags.InBattle));
        if (secondEastBusy) s.Own.Add(Corps(14, 800f, 3000f, 3, FormationFlags.InBattle));
        s.Own.Add(Corps(15, 150f, 5000f, 3, FormationFlags.InBattle));
        return s;
    }

    private struct Day
    {
        public TheaterPlan Plan;
        public List<ReconTask> Tasks;
        public Allocation Allocation;
        public ReconMissionLedger Ledger;
        public CampaignSnapshot S;
    }

    private static Day Review(CampaignSnapshot s, PlannerState state = null, ReconMissionLedger ledger = null)
    {
        ledger = ledger ?? new ReconMissionLedger();
        var sectors = ExecutorFixtures.Sectors(s);
        var day = new Day { Ledger = ledger, Tasks = new List<ReconTask>(), S = s };
        ledger.OnReview(s, null);
        day.Plan = TheaterPlanner.Plan(s, sectors, null, state ?? new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        ReconStep.Plan(s, sectors, day.Plan, new List<BattleHistoryRecord>(), 0, ledger, day.Tasks);
        day.Allocation = ForceAllocator.Allocate(day.Plan, s, sectors, null, day.Tasks, ledger);
        ledger.Sync(s, day.Tasks, day.Allocation);
        return day;
    }

    private static int Roles(Allocation a, FormationRole role)
    {
        int n = 0;
        foreach (var x in a.Assignments) if (x.Role == role) n++;
        return n;
    }

    private static Assignment Of(Allocation a, int id)
    {
        a.TryGet(id, out var x);
        return x;
    }

    // ---- the cause ----

    // The cause, pinned as the smoke saw it: tasks are planned (three, the main axis among them), the pool is not empty, the main
    // effort is staged, and nobody is sent, because the one formation that could go is the East hold reserve (already given its
    // role when the recon pick runs) and the body is in vanilla's capital-defence list. Without the draw this stays 0 screens.
    private static void TheSmokeShapeHasNothingFreeToScoutWith()
    {
        var s = SmokeUnion(withReserve: true);
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        AssertEqual(30, plan.MainObjectiveId, "the push is at the West objective");
        AssertTrue(plan.MainStaged, "unscouted: staged");
        AssertEqual(1, plan.MassInstanceIds.Count, "the mass is the one body");
        var tasks = new List<ReconTask>();
        ReconStep.Plan(s, sectors, plan, new List<BattleHistoryRecord>(), 0, new ReconMissionLedger(), tasks);
        AssertTrue(tasks.Count >= 3, "tasks are planned: " + tasks.Count);
        bool mainAxis = false;
        foreach (var t in tasks) if (t.Reason == ReconPlanner.ReasonMainAxis) mainAxis = true;
        AssertTrue(mainAxis, "the main axis is unwatched");
        AssertEqual(1, ReconPlanner.ScoutPool(s), "the pool counts the one ready formation that is not in a vanilla list");
        var held = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(held, 2).Reason, "and the East hold reserve has it");
        AssertEqual(FormationRole.MainEffort, Of(held, 1).Role, "the body is the main effort");
    }

    // The fix: a staged main effort that has no free scout takes the smallest ready formation its hold reserve keeps, once.
    private static void AStagedMainEffortGetsOneScoutFromTheHoldReserve()
    {
        var day = Review(SmokeUnion());
        AssertTrue(day.Plan.MainStaged, "staged");
        var scout = Of(day.Allocation, 2);
        AssertEqual(FormationRole.Recon, scout.Role, "the reserve formation scouts");
        AssertTrue(scout.Reason == ReconPlanner.ReasonMainAxis || scout.Reason == "probe" || scout.Reason == "advance-guard", "for the main objective: " + scout.Reason);
        AssertEqual(1, Roles(day.Allocation, FormationRole.Recon), "and only one is drawn");
        AssertEqual(FormationRole.MainEffort, Of(day.Allocation, 1).Role, "the main effort keeps its body");
        AssertEqual(1, day.Ledger.Missions.Count, "one mission");
        AssertTrue(ReconMissionLedger.IsActive(day.Ledger.Missions[0].State), "and it is active");
        AssertTrue(scout.Withheld, "withheld from vanilla like any scout");
    }

    private static void TheHoldReserveIsDrawnOnceNotPerTask()
    {
        // One busy formation covers a third of the East's 9000: the ready 2, 3 and 4 (3000 each) are all hold reserve.
        var s = SmokeUnion(secondEastBusy: false);
        s.Own.Add(Corps(3, 770f, 2000f));
        s.Own.Add(Corps(4, 780f, 2000f));
        var held = ForceAllocator.Allocate(ExecutorFixtures.RealPlan(s, new PlannerState()), s, ExecutorFixtures.Sectors(s), null, null, null);
        int reserveBefore = 0;
        foreach (var a in held.Assignments) if (a.Reason == ForceAllocator.ReasonHoldReserve) reserveBefore++;
        AssertEqual(3, reserveBefore, "the East keeps three ready formations");
        var east = Review(s);
        int reserve = 0;
        foreach (var a in east.Allocation.Assignments) if (a.Reason == ForceAllocator.ReasonHoldReserve) reserve++;
        AssertTrue(east.Tasks.Count >= 3, "there were several tasks: " + east.Tasks.Count);
        AssertEqual(1, Roles(east.Allocation, FormationRole.Recon), "one scout from the reserve however many main-objective tasks there are");
        AssertEqual(2, reserve, "the rest of the reserve keeps holding");
    }

    // The approach tasks (capital-approach, front-approach, belief, picket) are not a staged push's eyes: they never cost a sector its hold.
    private static void ANonMainTaskNeverDrawsTheHoldReserve()
    {
        var s = SmokeUnion();
        // The main objective is seen (a fresh belief at 30) but another army is believed somewhere no viewer covers.
        var near = ExecutorFixtures.Belief(310f, 0f, 1000f);
        s.Beliefs.Add(near);
        var day = Review(s);
        AssertTrue(day.Plan.MainScouted, "the main objective is scouted");
        AssertTrue(day.Tasks.Count > 0, "approach tasks still exist");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "none of them takes the hold reserve");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day.Allocation, 2).Reason, "which keeps holding");
    }

    private static void AScoutedMainEffortDrawsNothing()
    {
        var s = SmokeUnion();
        s.Beliefs.Add(ExecutorFixtures.Belief(310f, 0f, 1000f));
        var state = new PlannerState();
        var first = Review(s, state);
        var again = Review(s, state, first.Ledger);
        AssertFalse(again.Plan.MainStaged, "scouted, so not staged");
        AssertEqual(0, Roles(again.Allocation, FormationRole.Recon), "no draw on the second review either");
    }

    // The other half of the starvation: with no reserve to draw on and a body that is not in a vanilla list, the body itself was the only
    // formation the pick could return, and the main effort lost all of it to its own screen.
    private static void TheLastMassBodyIsNeverSpentOnScouting()
    {
        var s = SmokeUnion(bodyFlags: 0, withReserve: false);
        var day = Review(s);
        AssertEqual(1, day.Plan.MassInstanceIds.Count, "one body");
        AssertEqual(FormationRole.MainEffort, Of(day.Allocation, 1).Role, "it stays the main effort");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "and nobody scouts: there is nobody to send");
        AssertTrue(day.Tasks.Count > 0, "though the tasks exist");
    }

    // Reviews are daily and the reserve is rebuilt each time: the formation drawn yesterday is the incumbent today (no churn between
    // reserve formations, the mission is not cleared and re-opened).
    private static void TheDrawnScoutKeepsItsPostTheNextReview()
    {
        var s = SmokeUnion();
        var state = new PlannerState();
        var day1 = Review(s, state);
        int first = -1;
        foreach (var a in day1.Allocation.Assignments) if (a.Role == FormationRole.Recon) first = a.InstanceId;
        AssertTrue(first > 0, "a scout is drawn");
        int mission = day1.Ledger.Missions[0].MissionId;
        var s2 = SmokeUnion();
        var day2 = Review(s2, state, day1.Ledger);
        AssertEqual(FormationRole.Recon, Of(day2.Allocation, first).Role, "the same formation scouts again");
        AssertEqual(1, Roles(day2.Allocation, FormationRole.Recon), "and still only one");
        AssertEqual(mission, day2.Ledger.Missions[0].MissionId, "the mission was kept, not re-opened");
    }

    // The tracker gave up on sending the reserve formation to this task's post (two misses): the next candidate is tried, and nobody here.
    private static void AGaveUpFormationIsNotDrawnForTheTaskItGaveUpOn()
    {
        var s = SmokeUnion();
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        var tasks = new List<ReconTask>();
        ReconStep.Plan(s, sectors, plan, new List<BattleHistoryRecord>(), 0, new ReconMissionLedger(), tasks);
        var gaveUp = new List<GaveUpMove>();
        foreach (var task in tasks) gaveUp.Add(new GaveUpMove { InstanceId = 2, TargetX = task.HoldX, TargetZ = task.HoldZ, ObjectiveId = -1 });
        var alloc = ForceAllocator.Allocate(plan, s, sectors, null, tasks, new ReconMissionLedger(), gaveUp);
        AssertEqual(0, Roles(alloc, FormationRole.Recon), "the formation that gave up on every post is not drawn for any of them");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(alloc, 2).Reason, "it keeps holding");
    }

    // The draw is for the push's own eyes. A higher-priority task of another kind (a screen posture, a belief) comes first in the list and
    // must not take the one formation the reserve can spare.
    private static void TheDrawServesTheMainObjectivesTasksNotThePostureOrBeliefTasks()
    {
        var s = SmokeUnion();
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        AssertTrue(plan.MainStaged, "staged");
        var tasks = new List<ReconTask>
        {
            new ReconTask { Kind = ReconKind.Screen, SectorId = plan.MainSectorId, FromX = 100f, FromZ = 0f, HoldX = 200f, HoldZ = 0f, Priority = 5f, Reason = "screen-posture" },
            new ReconTask { Kind = ReconKind.Screen, SectorId = plan.MainSectorId, FromX = 100f, FromZ = 0f, HoldX = 210f, HoldZ = 0f, Priority = 2.5f, Reason = "belief" },
            new ReconTask { Kind = ReconKind.Probe, SectorId = plan.MainSectorId, FromX = 100f, FromZ = 0f, HoldX = 250f, HoldZ = 0f, Priority = 4.2f, Reason = "probe" },
        };
        var alloc = ForceAllocator.Allocate(plan, s, sectors, null, tasks, new ReconMissionLedger());
        AssertEqual(FormationRole.Recon, Of(alloc, 2).Role, "the reserve formation scouts");
        AssertEqual("probe", Of(alloc, 2).Reason, "for the probe, not for the posture or the belief that precede it");
    }

    // A Union garrison inside a live secession state (Kentucky, D8) holds where it stands, withheld; the draw takes only a hold reserve.
    private static void ASecessionGarrisonIsNeverDrawnAsAScout()
    {
        var s = SmokeUnion(withReserve: false);
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 15, StateId = 14, X = 500f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 0.3f });
        s.Secessions.Add(new SecessionRecord { ObjectiveId = 15, StateId = 14, StateAlliance = 0, Live = true, HasTown = true, TownX = 500f, TownZ = 0f });
        var garrison = Corps(20, 480f, 4000f);
        garrison.StateId = 14;
        s.Own.Add(garrison);
        s.Own.Add(Corps(21, 470f, 9500f, 3, FormationFlags.InBattle));   // holds the state's sector (9000), so no hold reserve takes the garrison first
        // The body is nearer the West objective than the garrison and big enough alone: the garrison is not in the mass.
        var body = s.Own[s.IndexOfOwn(1)];
        body.X = 345f;
        body.WaypointX = 345f;
        body.Strength = 12000f;
        s.Own[s.IndexOfOwn(1)] = body;
        var day = Review(s);
        AssertTrue(day.Plan.MainStaged, "staged");
        AssertEqual(ForceAllocator.ReasonSecession, Of(day.Allocation, 20).Reason, "the formation is the state's garrison");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "and is not the push's scout");
        AssertTrue(ReconPlanner.ScoutPool(s) >= 1, "though the pool counts it");
    }

    private static void ScoutsMainObjectiveNamesTheMainTasksOnly()
    {
        foreach (string reason in new[] { ReconPlanner.ReasonMainAxis })
            AssertTrue(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.Screen, Reason = reason }), "the main-axis screen");
        AssertTrue(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.Probe, Reason = "probe" }), "the probe");
        AssertTrue(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.AdvanceGuard, Reason = "advance-guard" }), "the advance guard");
        foreach (string reason in new[] { ReconPlanner.ReasonCapitalApproach, ReconPlanner.ReasonFrontApproach, "belief", "screen-posture" })
            AssertFalse(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.Screen, Reason = reason }), reason + " is not the push's eyes");
        AssertFalse(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.Picket, Reason = ReconPlanner.ReasonMainAxis }), "a picket is never one");
    }

    // The row's unfilled count pairs tasks with missions as the ledger does: kind, hold point and reason.
    private static void TheUnfilledCountMatchesTasksByKindPointAndReason()
    {
        var s = Base();
        s.Own.Add(Corps(1, 100f, 6000f));
        var ledger = new ReconMissionLedger();
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.Recon, SectorId = 0, TargetX = 200f, TargetZ = 5f, Reason = "belief" });
        var tasks = new List<ReconTask>
        {
            new ReconTask { Kind = ReconKind.Screen, SectorId = 0, HoldX = 200f, HoldZ = 5f, Reason = "belief" },
            new ReconTask { Kind = ReconKind.Screen, SectorId = 0, HoldX = 300f, HoldZ = 5f, Reason = "belief" },   // another point
            new ReconTask { Kind = ReconKind.Screen, SectorId = 0, HoldX = 200f, HoldZ = 9f, Reason = "belief" },   // another point (z)
            new ReconTask { Kind = ReconKind.Screen, SectorId = 0, HoldX = 200f, HoldZ = 5f, Reason = ReconPlanner.ReasonMainAxis },   // another reason
            new ReconTask { Kind = ReconKind.Probe, SectorId = 0, HoldX = 200f, HoldZ = 5f, Reason = "belief" },     // another kind
        };
        ledger.Sync(s, tasks, a);
        var counts = ReconStep.CountsOf(s, ledger, tasks);
        AssertEqual(5, counts.Tasks, "five tasks");
        AssertEqual(4, counts.Unfilled, "one has its mission");
        var cleared = ledger.Missions[0];
        cleared.State = ReconState.Cleared;       // same kind, point and reason, but over: it fills nothing
        ledger.Restore(new[] { cleared });
        AssertEqual("belief", ledger.Missions[0].Reason, "the cleared mission still carries the task's reason");
        AssertEqual(5, ReconStep.CountsOf(s, ledger, tasks).Unfilled, "a cleared mission fills nothing");
    }

    private static void TheReconRowCountsTasksAndUnfilled()
    {
        // The body is the pool's only member and the last of the main effort: tasks exist, nobody can be sent.
        var none = Review(SmokeUnion(bodyFlags: 0, withReserve: false));
        AssertTrue(none.Tasks.Count >= 3, "tasks exist: " + none.Tasks.Count);
        var rowNone = ReconStep.CountsOf(none.S, none.Ledger, none.Tasks).Row(0);
        AssertContains(rowNone, " tasks=" + none.Tasks.Count + " unfilled=" + none.Tasks.Count + " ", "nobody was sent: every task is unfilled, " + rowNone);
        AssertTrue(rowNone.EndsWith("pool=1"), "though the pool is not empty: " + rowNone);

        var drawn = Review(SmokeUnion());
        var row = ReconStep.CountsOf(drawn.S, drawn.Ledger, drawn.Tasks).Row(0);
        AssertContains(row, " tasks=" + drawn.Tasks.Count + " unfilled=" + (drawn.Tasks.Count - 1) + " ", "one task has its scout: " + row);
        AssertContains(row, " listed=1 pool=1", "the capital-defence body is named, and the one formation the pick can take is the pool: " + row);
        AssertEqual(1, ReconPlanner.ListedCount(SmokeUnion(FormationFlags.InOffensiveOps)), "an offensive-operations entry is listed too");
        AssertEqual(0, ReconPlanner.ListedCount(SmokeUnion(0)), "an unlisted body is not");
        var busyListed = SmokeUnion(0);
        var inBattle = busyListed.Own[busyListed.IndexOfOwn(13)];
        inBattle.Flags |= FormationFlags.CapitalDefense;
        busyListed.Own[busyListed.IndexOfOwn(13)] = inBattle;
        AssertEqual(0, ReconPlanner.ListedCount(busyListed), "a busy formation is not massable, so its list entry says nothing about the mass");
    }

    // The probe/screen is a question, not a post: answered by sight or a fresh belief, the task is gone and the formation goes back to
    // the reserve the next review.
    private static void AScoutDrawnFromTheReserveReturnsWhenTheObjectiveIsScouted()
    {
        var state = new PlannerState();
        var day1 = Review(SmokeUnion(), state);
        AssertEqual(FormationRole.Recon, Of(day1.Allocation, 2).Role, "day 1: drawn");
        var s2 = SmokeUnion();
        s2.Beliefs.Add(ExecutorFixtures.Belief(310f, 0f, 1000f));
        var day2 = Review(s2, state, day1.Ledger);
        AssertTrue(day2.Plan.MainScouted, "day 2: the objective is scouted");
        AssertEqual(FormationRole.Hold, Of(day2.Allocation, 2).Role, "the formation is back in its sector's hold");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day2.Allocation, 2).Reason, "as its hold reserve");
    }

    // ---- Q3: the retarget within one objective ----

    // The smoke: main West/29 scouted=yes at 18:17:02, East/29 scouted=latched at 18:17:32 with the trace reading "unscouted est=9947".
    // An objective has several target records (29@3, 29@2, 29@6) and the decision follows the objective's id, so the best record of the
    // same objective is chosen each review with switch=hold; the scouting latch then belonged to the decision, not to the ground that was
    // scouted, and a different record, unseen, marched as if it were scouted.
    private static CampaignSnapshot TwoRecords(float beliefAtFirst, float beliefAtSecond)
    {
        var s = Base();
        s.Objectives.Clear();
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 400f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.6f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 700f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.6f });
        for (int i = 0; i < 8; i++) s.Own.Add(Corps(i + 1, 450f + 30f * i, 7000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(410f, 0f, beliefAtFirst));
        if (beliefAtSecond > 0f) s.Beliefs.Add(ExecutorFixtures.Belief(710f, 0f, beliefAtSecond));
        return s;
    }

    private static void ARetargetWithinTheObjectiveIsNotScoutedByTheOldTarget()
    {
        var state = new PlannerState();
        var day1 = Review(TwoRecords(1000f, 300000f), state);   // the second record is held by an army far too strong for the mass
        AssertEqual(40, day1.Plan.MainObjectiveId, "objective 40");
        AssertNear(400f, day1.Plan.MainObjectiveX, 0.5f, "the first record, seen");
        AssertTrue(day1.Plan.MainScouted && !day1.Plan.MainStaged, "day 1: scouted, not staged");
        int serial = state.DecisionSerial;

        // Day 2: the first record is now held by far more than the mass can beat; the same objective's other record, never seen, is the best.
        var day2 = Review(TwoRecords(300000f, 0f), state);
        AssertEqual(40, day2.Plan.MainObjectiveId, "the same objective");
        AssertNear(700f, day2.Plan.MainObjectiveX, 0.5f, "now its other record");
        AssertEqual(serial, state.DecisionSerial, "the same decision (the objective did not change)");
        AssertEqual("hold", day2.Plan.SwitchReason, "switch=hold, as in the smoke");
        AssertFalse(day2.Plan.MainScouted, "but nobody has seen this ground");
        AssertFalse(day2.Plan.MainScoutLatched, "and the old record's picture is not carried over to it");
        AssertTrue(day2.Plan.MainStaged, "so the mass gathers and scouts first");
    }

    // ---- hysteresis between the records of one objective ----

    // Objective 40 with two records, 400 (the West one) and 700; the importance of each sets its value term (0.30 x 0.5 x importance), so
    // swapping the two importances swaps which record leads by more than SwitchMargin (0.12). Eight corps stand between them, in supply.
    private static CampaignSnapshot Records(float importanceFirst, float importanceSecond, float beliefAtFirst = 0f)
    {
        var s = Base();
        s.Objectives.Clear();
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 400f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importanceFirst });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 700f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importanceSecond });
        for (int i = 0; i < 8; i++) s.Own.Add(Corps(i + 1, 450f + 30f * i, 7000f));
        if (beliefAtFirst > 0f) s.Beliefs.Add(ExecutorFixtures.Belief(410f, 0f, beliefAtFirst));
        return s;
    }

    private const float Lean = 0.95f, Slight = 0.05f;   // the record that leads, the one that trails

    private static TheaterPlan PlanOf(CampaignSnapshot s, PlannerState state)
    {
        return TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, state, null, null, ExecutorFixtures.Speed(), out _);
    }

    private static float ScoreOf(CampaignSnapshot s, float x)
    {
        var inputs = new TheaterPlanner.Inputs { S = s, Sectors = ExecutorFixtures.Sectors(s), Speed = ExecutorFixtures.Speed() };
        var list = new List<TheaterPlanner.Candidate>();
        TheaterPlanner.Evaluate(inputs, list);
        foreach (var c in list) if (Math.Abs(c.X - x) < 1f) return c.Score;
        throw new Exception("no record at " + x);
    }

    private static void RecordsWhoseScoresAlternateStayOnOneRecord()
    {
        var state = new PlannerState();
        var day1 = PlanOf(Records(Lean, Slight), state);
        float first = day1.MainObjectiveX;
        AssertNear(400f, first, 0.5f, "day 1: the record that leads");
        int serial = state.DecisionSerial;
        for (int day = 2; day <= 9; day++)
        {
            bool swapped = day % 2 == 0;
            var s = swapped ? Records(Slight, Lean) : Records(Lean, Slight);
            if (swapped)
                AssertTrue(ScoreOf(s, 700f) - ScoreOf(s, 400f) > TheaterPlanner.SwitchMargin, "day " + day + ": the other record leads by more than the margin");
            var plan = PlanOf(s, state);
            AssertNear(first, plan.MainObjectiveX, 0.5f, "day " + day + ": the main effort stays on its record");
            AssertEqual(40, plan.MainObjectiveId, "the same objective");
            AssertEqual(serial, state.DecisionSerial, "and the same decision");
            AssertEqual("hold", plan.SwitchReason, "switch=hold");
        }
    }

    // The objective switch's own rule: the margin on SwitchDays consecutive reviews.
    private static void ARecordChangesAfterTheMarginHoldsForThreeReviews()
    {
        var state = new PlannerState();
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "day 1");
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "day 2: one review of lead");
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "day 3: two");
        int serial = state.DecisionSerial;
        var day4 = PlanOf(Records(Slight, Lean), state);
        AssertNear(700f, day4.MainObjectiveX, 0.5f, "day 4: the third consecutive review of lead moves it");
        AssertEqual(serial, state.DecisionSerial, "within the decision (the objective did not change)");
        AssertNear(700f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "and the new record is kept in its turn (day 5: the old one leads once)");
    }

    private static void AnInterruptedLeadRestartsTheRecordWindow()
    {
        var state = new PlannerState();
        PlanOf(Records(Lean, Slight), state);
        PlanOf(Records(Slight, Lean), state);       // 1
        PlanOf(Records(Slight, Lean), state);       // 2
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "a review where the kept record leads again");
        PlanOf(Records(Slight, Lean), state);       // 1 again
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "two in a row after the interruption: still the kept record");
        AssertNear(700f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "the third in a row moves it");
    }

    // Invalid at once: the kept record is held by an army the mass cannot beat (no longer eligible), or is gone (accomplished or unavailable).
    private static void AnInvalidKeptRecordIsLeftAtOnce()
    {
        var state = new PlannerState();
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "day 1");
        int serial = state.DecisionSerial;
        var beaten = Records(Lean, Slight, beliefAtFirst: 300000f);
        var day2 = PlanOf(beaten, state);
        AssertNear(700f, day2.MainObjectiveX, 0.5f, "day 2: the kept record is below its odds, so the other is taken the same review");
        AssertEqual(serial, state.DecisionSerial, "same objective, same decision");

        var state2 = new PlannerState();
        PlanOf(Records(Lean, Slight), state2);
        var gone = Records(Lean, Slight);
        var rec = gone.Objectives[0];
        rec.Accomplished = true;
        gone.Objectives[0] = rec;
        AssertNear(700f, PlanOf(gone, state2).MainObjectiveX, 0.5f, "an accomplished record is lost: the other at once");
        var state3 = new PlannerState();
        PlanOf(Records(Lean, Slight), state3);
        var unavailable = Records(Lean, Slight);
        rec = unavailable.Objectives[0];
        rec.Available = false;
        unavailable.Objectives[0] = rec;
        AssertNear(700f, PlanOf(unavailable, state3).MainObjectiveX, 0.5f, "an unavailable one too");
    }

    // Three records: the kept one is beaten by the margin every review, but by B and C in turn: no record has three reviews in a row.
    private static CampaignSnapshot ThreeRecords(float a, float b, float c)
    {
        var s = Records(a, b);
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 600f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = c });
        return s;
    }

    private static void DifferentChallengersDoNotAddUp()
    {
        var state = new PlannerState();
        AssertNear(400f, PlanOf(ThreeRecords(Lean, Slight, Slight), state).MainObjectiveX, 0.5f, "day 1: the first record");
        for (int day = 2; day <= 8; day++)
        {
            var s = day % 2 == 0 ? ThreeRecords(Slight, Lean, 0.9f) : ThreeRecords(Slight, 0.9f, Lean);
            var plan = PlanOf(s, state);
            AssertNear(400f, plan.MainObjectiveX, 0.5f, "day " + day + ": two records beat it in turn, neither for three reviews");
        }
    }

    // Two records at one x, 10 apart in z: the record is its position, both axes.
    private static CampaignSnapshot SideBySide(float importanceA, float importanceB)
    {
        var s = Records(importanceA, 0.05f);
        s.Objectives.Clear();
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 400f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importanceA });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 400f, Z = 10f, HolderAlliance = 1, Available = true, Importance01 = importanceB });
        return s;
    }

    private static void RecordsThatDifferOnlyInZAreDistinctRecords()
    {
        var state = new PlannerState();
        AssertNear(0f, PlanOf(SideBySide(1f, 0f), state).MainObjectiveZ, 0.5f, "day 1: the record on the line");
        PlanOf(SideBySide(0f, 1f), state);
        PlanOf(SideBySide(0f, 1f), state);
        AssertNear(10f, PlanOf(SideBySide(0f, 1f), state).MainObjectiveZ, 0.5f, "day 4: the other record after three reviews");
        AssertNear(10f, PlanOf(SideBySide(1f, 0f), state).MainObjectiveZ, 0.5f, "day 5: the first leads once, the kept one stays");
    }

    // A record that leads, but by no more than the margin, is not a challenger: it breaks the run of reviews.
    private static void ALeadWithinTheMarginBreaksTheRecordWindow()
    {
        float close = -1f;
        for (float imp = 0.05f; imp <= 1f && close < 0f; imp += 0.05f)
        {
            var s = Records(0.5f, imp);
            float lead = ScoreOf(s, 700f) - ScoreOf(s, 400f);
            if (lead > 0.005f && lead < TheaterPlanner.SwitchMargin - 0.02f) close = imp;
        }
        AssertTrue(close > 0f, "a lead inside the margin exists for the fixture");
        var state = new PlannerState();
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "day 1");
        PlanOf(Records(Slight, Lean), state);          // 1
        PlanOf(Records(Slight, Lean), state);          // 2
        AssertNear(400f, PlanOf(Records(0.5f, close), state).MainObjectiveX, 0.5f, "a lead inside the margin: stays, and the run is broken");
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "the run starts again at one");
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "two");
        AssertNear(700f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "three");
    }

    // The kept record, not whichever record scores best today, carries the odds deadband (F1): within OddsDeadband below its required ratio it
    // stays eligible for fewer than SwitchDays reviews, so a better-scoring record that has led for one review does not take the target.
    private static void TheKeptRecordHasTheOddsDeadband()
    {
        var state = new PlannerState();
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "day 1");
        var s = Records(Slight, Lean, beliefAtFirst: 46000f);   // 28000 against 46000: 0.61 against a required 0.65, inside the deadband
        var plan = PlanOf(s, state);
        AssertNear(400f, plan.MainObjectiveX, 0.5f, "day 2: the kept record is retained by the deadband");
        AssertEqual(1, state.BelowOddsDays, "and the odds debt is counted");
    }

    // The latch follows the record kept: while the main effort stays on the scouted record the alternation of the other does not unscout it.
    private static void TheScoutedLatchFollowsTheKeptRecord()
    {
        var state = new PlannerState();
        var day1 = PlanOf(Records(Lean, Slight, beliefAtFirst: 800f), state);
        AssertNear(400f, day1.MainObjectiveX, 0.5f, "the scouted record");
        AssertTrue(day1.MainScouted && !day1.MainStaged, "scouted, not staged");
        for (int day = 2; day <= 5; day++)
        {
            var plan = PlanOf(day % 2 == 0 ? Records(Slight, Lean, 800f) : Records(Lean, Slight, 800f), state);
            AssertNear(400f, plan.MainObjectiveX, 0.5f, "day " + day + ": kept");
            AssertTrue(plan.MainScouted && !plan.MainStaged, "day " + day + ": and still scouted");
        }
        // Moving to the other record starts unscouted (it has not been seen).
        PlanOf(Records(Slight, Lean, 800f), state);
        PlanOf(Records(Slight, Lean, 800f), state);
        var moved = PlanOf(Records(Slight, Lean, 800f), state);
        AssertNear(700f, moved.MainObjectiveX, 0.5f, "after the window");
        AssertTrue(moved.MainStaged && !moved.MainScoutLatched, "the new record is staged until it is scouted");
    }

    private static void TheKeptRecordAndItsChallengerSurviveTheSidecar()
    {
        var state = new PlannerState();
        state.Restore(40, -1, 0, 3, 77L, currentTargetX: 400.5f, currentTargetZ: -2f, recordChallengerX: 700f, recordChallengerZ: 0f, recordChallengerDays: 2);
        var dto = SidecarDtoCodec.PlannerToDto(new PlannerSnapshot { State = state });
        var back = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>(Newtonsoft.Json.JsonConvert.SerializeObject(dto)));
        AssertNear(400.5f, back.State.CurrentTargetX, 0.001f, "kept x");
        AssertNear(-2f, back.State.CurrentTargetZ, 0.001f, "kept z");
        AssertNear(700f, back.State.RecordChallengerX, 0.001f, "challenger x");
        AssertNear(0f, back.State.RecordChallengerZ, 0.001f, "challenger z");
        AssertEqual(2, back.State.RecordChallengerDays, "and its reviews");
        var old = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>("{\"currentMainObjective\":40}"));
        AssertTrue(float.IsNaN(old.State.CurrentTargetX) && old.State.RecordChallengerDays == 0, "an older file keeps no record: the best one is taken");
    }

    private static void TheScoutedTargetSurvivesTheSidecarAndAnOldFileHasNone()
    {
        var state = new PlannerState();
        state.Restore(31, -1, 0, 4, 77L, scoutedDecision: 4, scoutedTargetX: 812.5f, scoutedTargetZ: -40f);
        var dto = SidecarDtoCodec.PlannerToDto(new PlannerSnapshot { State = state });
        var back = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>(Newtonsoft.Json.JsonConvert.SerializeObject(dto)));
        AssertNear(812.5f, back.State.ScoutedTargetX, 0.001f, "x restored");
        AssertNear(-40f, back.State.ScoutedTargetZ, 0.001f, "z restored");
        var old = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>("{\"currentMainObjective\":31,\"scoutedDecision\":4}"));
        AssertEqual(4, old.State.ScoutedDecision, "an older file's latch still reads");
        AssertTrue(float.IsNaN(old.State.ScoutedTargetX), "without a target: the latch speaks for the decision's target as before");
    }

    // ---- Q2: what the deferred rows are ----

    private static void TheMoverRowNamesAStagedGatheringThatWaits()
    {
        var g = new MoverGate();
        var tally = g.RunStagedInEnemyGround();
        AssertEqual(1, tally.Deferred, "the main effort waits");
        AssertEqual(1, tally.DeferredStaged, "and the row says it is a staged gathering, not the chapter roll");
        AssertContains(tally.Row(0), "deferredStaged=1", "in the row the smoke reads");
        AssertFalse(tally.Tempo.NeedsRoll, "no roll was evaluated (the roll is the march's)");

        var marching = g.RunScoutedClosedTempo();
        AssertEqual(1, marching.Deferred, "a scouted march waits for the roll");
        AssertEqual(0, marching.DeferredStaged, "which is not a staged gathering");
        AssertContains(marching.Row(0), "deferredStaged=0", "the row tells them apart");
    }

    private sealed class MoverGate
    {
        private static Allocation AllocOf(CampaignSnapshot s, TheaterPlan plan)
        {
            return ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        }

        private (CampaignSnapshot s, TheaterPlan plan) Fixture(bool staged, float startX)
        {
            var s = ExecutorFixtures.Wide();
            s.Own.Add(ExecutorFixtures.Corps(1, 340f, 20000f));
            s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
            var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
            plan.MainStaged = staged;
            plan.StartLineX = startX;
            plan.StartLineZ = 0f;
            return (s, plan);
        }

        internal MoverTally RunStagedInEnemyGround()
        {
            var (s, plan) = Fixture(true, 120f);   // x <= 150 is alliance 1's ground in the fake host
            return CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, AllocOf(s, plan), ExecutorFixtures.ClosedTempo()), new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State());
        }

        internal MoverTally RunScoutedClosedTempo()
        {
            var (s, plan) = Fixture(false, 0f);
            return CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, AllocOf(s, plan), ExecutorFixtures.ClosedTempo()), new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State());
        }
    }

    // ---- Q4: the row's category ----

    // The smoke's failures.jsonl held 26 rows, all [CampaignMover] with failed=0: the router read the counter's name as an event.
    private static void AZeroCountedFailureIsNotAFailureRow()
    {
        const string quiet = "[CampaignMover] alliance=0 issued=0 confirmed=0 failed=0 retried=0 gaveUp=0 queued=0 skippedPlayer=0 skippedBusy=0 deferred=1";
        var route = TelemetryTagPolicy.Route(quiet);
        AssertEqual(TelemetryCategory.Write, route.Category, "a mover row with nothing failed is a Write row");
        AssertEqual(TelemetrySeverity.Info, route.Severity, "at Info");
        AssertEqual(TelemetryCategory.Write, TelemetryTagPolicy.Route("[ReconMission] alliance=1 issued=2 retried=0 failed=0 queued=0 ordered=1 skippedPlayer=0 skippedBusy=0").Category, "and so is a recon summary");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 issued=0 confirmed=0 failed=2 retried=0 gaveUp=0 queued=0 skippedPlayer=0 skippedBusy=0 deferred=0").Category, "a failed count above zero is still a failure");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=10").Category, "ten is not zero");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=0.5").Category, "nor is 0.5");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=05").Category, "nor is 05");
        AssertEqual(TelemetryCategory.Write, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=0").Category, "a zero at the end of the line is a zero");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=0 exception while moving").Category, "a zero counter does not hide a real exception word");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 move failed for corps 4").Category, "a failed word with no value is an event");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 error=0 failed=1").Category, "a later non-zero failed still counts");
    }
}
