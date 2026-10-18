using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F1 (D11 requirement-telemetry, first half): the rows the 2026-10-18 investigation could not read.
// The mover names each move it makes, the plan trace carries each candidate's coordinates, [Reserves] reports the capital and hold
// shares, the departments' strengths and what vanilla's defence did with the planner's withheld formations, #25 tallies its reasons,
// and the planner's rows carry the review date. Plan: docs/superpowers/plans/2026-10-18-strategic-foresight.md F1; cases
// TC-TEL-* in docs/loop/foresight/test-plan.md.
internal static class ForesightTelemetryTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("DateOfInvertsDaySerial", DateOfInvertsDaySerial),
            ("DatedRowsKeepTheirDedupe", DatedRowsKeepTheirDedupe),
            ("IssueRowNamesUnitRoleTargetObjectiveAndOrigin", IssueRowNamesUnitRoleTargetObjectiveAndOrigin),
            ("IssueRowsNeverExceedTheDailyCap", IssueRowsNeverExceedTheDailyCap),
            ("ARefusedRetryWritesNoIssueRow", ARefusedRetryWritesNoIssueRow),
            ("IssueRowPickIsPinnedNewOrDash", IssueRowPickIsPinnedNewOrDash),
            ("RowNamesAreQuoted", RowNamesAreQuoted),
            ("TheMoverRuntimeWritesTheIssueAndTheMissRows", TheMoverRuntimeWritesTheIssueAndTheMissRows),
            ("TraceCarriesEachCandidatesCoordinates", TraceCarriesEachCandidatesCoordinates),
            ("NewRowsAreRegisteredInTheTagPolicy", NewRowsAreRegisteredInTheTagPolicy),
            ("PinnedBuilderCarriesItsOwnReason", PinnedBuilderCarriesItsOwnReason),
            ("AllocatorHelpersKeepTheShippedSums", AllocatorHelpersKeepTheShippedSums),
            ("ReservesRowNamesCapitalAndHoldShares", ReservesRowNamesCapitalAndHoldShares),
            ("ReservesRowListsAtMostSixSectorsLargestNeedFirst", ReservesRowListsAtMostSixSectorsLargestNeedFirst),
            ("ReservesRowNamesEachDepartmentsStrength", ReservesRowNamesEachDepartmentsStrength),
            ("ReservesDefenceCountsReadYesterdaysAllocation", ReservesDefenceCountsReadYesterdaysAllocation),
            ("ReservesRowSharesTheAllocatorsCountedHold", ReservesRowSharesTheAllocatorsCountedHold),
            ("ReservesRowReadsOnlyTheMassOfYesterdaysPlan", ReservesRowReadsOnlyTheMassOfYesterdaysPlan),
            ("ReservesRowEveryReview", ReservesRowEveryReview),
            ("DefenseFilterReasonsAreTalliedAndSorted", DefenseFilterReasonsAreTalliedAndSorted),
        });
    }

    // ---- dates ----

    private static void DateOfInvertsDaySerial()
    {
        int checkedDays = 0;
        foreach (int year in new[] { 1, 1800, 1860, 1861, 1864, 1865, 1900, 2000, 2024, 2100 })
            for (int month = 1; month <= 12; month++)
                for (int day = 1; day <= DateTime.DaysInMonth(year, month); day++)
                {
                    int serial = ReviewCadenceCatchUp.DaySerial(day, month, year);
                    ReviewCadenceCatchUp.DateOf(serial, out int y, out int m, out int d);
                    AssertTrue(y == year && m == month && d == day, "serial " + serial + " is " + year + "-" + month + "-" + day + ", read as " + y + "-" + m + "-" + d);
                    checkedDays++;
                }
        AssertTrue(checkedDays > 3600, "every day of ten years was inverted: " + checkedDays);

        // Month ends: the next serial is the first of the next month; February of a leap year has a 29th, of 1900 and 2100 it has not.
        ReviewCadenceCatchUp.DateOf(ReviewCadenceCatchUp.DaySerial(30, 6, 1861) + 1, out int y2, out int m2, out int d2);
        AssertTrue(y2 == 1861 && m2 == 7 && d2 == 1, "30 June 1861 + 1 is 1 July: " + y2 + "-" + m2 + "-" + d2);
        ReviewCadenceCatchUp.DateOf(ReviewCadenceCatchUp.DaySerial(31, 12, 1861) + 1, out int y3, out int m3, out int d3);
        AssertTrue(y3 == 1862 && m3 == 1 && d3 == 1, "31 December rolls the year: " + y3 + "-" + m3 + "-" + d3);
        ReviewCadenceCatchUp.DateOf(ReviewCadenceCatchUp.DaySerial(28, 2, 1864) + 1, out int y4, out int m4, out int d4);
        AssertTrue(y4 == 1864 && m4 == 2 && d4 == 29, "1864 is a leap year: " + y4 + "-" + m4 + "-" + d4);
        ReviewCadenceCatchUp.DateOf(ReviewCadenceCatchUp.DaySerial(28, 2, 1900) + 1, out int y5, out int m5, out int d5);
        AssertTrue(y5 == 1900 && m5 == 3 && d5 == 1, "1900 is not: " + y5 + "-" + m5 + "-" + d5);

        AssertEqual("1861-06-02", ReviewCadenceCatchUp.FormatDate(ReviewCadenceCatchUp.DaySerial(2, 6, 1861)), "the baseline save's first day");
        AssertEqual("0001-01-01", ReviewCadenceCatchUp.FormatDate(ReviewCadenceCatchUp.DaySerial(1, 1, 1)), "zero-padded");
        AssertEqual("row date=1861-06-23", ReviewCadenceCatchUp.Dated("row", ReviewCadenceCatchUp.DaySerial(23, 6, 1861)), "the field a row gains");
    }

    // The date is appended to the emitted line after the signature compare, so the same plan on two days still writes one row
    // (D11 requirement-telemetry: the dated rows keep their dedupe; Summary stays dateless).
    private static void DatedRowsKeepTheirDedupe()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string plan = BodyOf(runtime, "private static void EmitPlan(");
        AssertInOrder(plan, "EmitPlan", "string.Equals(LastPlanRow[a], row, StringComparison.Ordinal)) return;", "LastPlanRow[a] = row;", "ReviewCadenceCatchUp.Dated(row,");
        AssertFalse(plan.Contains("TheaterPlanner.Summary(plan) + "), "the stored signature is the dateless summary");
        string alloc = BodyOf(runtime, "private static void EmitAllocation(");
        AssertInOrder(alloc, "EmitAllocation", "string.Equals(LastAllocationRow[a], row, StringComparison.Ordinal)) return;", "LastAllocationRow[a] = row;", "ReviewCadenceCatchUp.Dated(row,");
        string beliefs = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/EnemyDetectionRuntime.cs"), "internal static void Report(");
        AssertInOrder(beliefs, "EnemyDetectionRuntime.Report", "string.Equals(LastSignature[side], line, StringComparison.Ordinal)) return;", "LastSignature[side] = line;", "ReviewCadenceCatchUp.Dated(line,");
        // The signature that is compared is the dateless text: nothing before the compare dates it.
        foreach (var emitter in new[] { plan, alloc, beliefs })
            AssertTrue(emitter.IndexOf("ReviewCadenceCatchUp.Dated(", StringComparison.Ordinal) > emitter.IndexOf("StringComparison.Ordinal)) return;", StringComparison.Ordinal),
                "the first dating comes after the compare");
        string mover = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMover.cs");
        AssertContains(BodyOf(mover, "public string Row(int alliance)"), "ReviewCadenceCatchUp.Dated(", "the mover's tally row is dated");
        AssertContains(BodyOf(mover, "private static string MissRow("), "ReviewCadenceCatchUp.Dated(", "and so is each miss row");
    }

    // ---- the mover's issue rows ----

    // Corps 1 and 2 are the mass of objective 98; both are sent, each from where it stands to the sector's target.
    private static MoverTally MoveTwoCorps(MoveTracker tracker, FakeMoverHost host, PlannerState state, int dayOffset)
    {
        var s = ExecutorFixtures.Wide(dayOffset);
        var mass = new List<int>();
        for (int k = 1; k <= 2; k++)
        {
            s.Own.Add(ExecutorFixtures.Corps(k, 340f + 10f * k, 6000f));
            mass.Add(k);
        }
        s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
        var plan = ExecutorFixtures.HandPlan(s, mass.ToArray());
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        return CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), host, tracker, state);
    }

    private static void IssueRowNamesUnitRoleTargetObjectiveAndOrigin()
    {
        var tally = MoveTwoCorps(new MoveTracker(), new FakeMoverHost(), ExecutorFixtures.State(), 0);
        AssertEqual(2, tally.Issued, "both bodies are sent");
        AssertEqual(2, tally.IssueRows.Count, "one issue row each");
        string row = tally.IssueRows[0];
        AssertEqual("[CampaignMoverIssue] alliance=0 date=1861-06-15 unit=corps1:1 role=MainEffort reason=mass target=100,0 objective=98 from=350,0 readiness=3 pick=-", row,
            "the row names the formation, its role and reason, where it was sent from and to, and the objective");
        AssertContains(tally.IssueRows[1], " unit=corps2:2 role=MainEffort ", "and the second body");
        AssertContains(tally.IssueRows[1], " from=360,0 ", "from where it stood");
    }

    private static void IssueRowsNeverExceedTheDailyCap()
    {
        // Three moves waiting in vanilla's queue (they spend none of the budget) and nine formations assigned the main effort (a hand
        // allocation: the real allocator would hold the surplus in reserve). The queued ones are served first, in allocation order.
        var s = ExecutorFixtures.Wide();
        var alloc = new Allocation { Alliance = 0 };
        int total = 3 + MoveTracker.MaxMovesPerDay + 3;
        for (int k = 1; k <= total; k++)
        {
            var corps = ExecutorFixtures.Corps(k, 300f + 10f * k, 6000f);
            if (k <= 3) corps.Flags |= FormationFlags.MoveQueued;
            s.Own.Add(corps);
            alloc.Assignments.Add(ExecutorFixtures.Assign(k, FormationRole.MainEffort, 1, 100f, 0f));
        }
        var plan = ExecutorFixtures.HandPlan(s, new int[0]);
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(3, tally.Queued, "three moves wait in the queue");
        AssertEqual(MoveTracker.MaxMovesPerDay, tally.Issued, "the day's budget is spent");
        AssertEqual(MoveTracker.MaxMovesPerDay, tally.IssueRows.Count, "and there is one row for each move made, no more: a queued move is not one");
    }

    // A retry the host refused (MoveIssue reports it as Retried with moved == false) made no move and writes no row; one the host took does.
    private static void ARefusedRetryWritesNoIssueRow()
    {
        var host = new FakeMoverHost();
        var tracker = new MoveTracker();
        var state = ExecutorFixtures.State();
        MoveTwoCorps(tracker, host, state, 0);
        host.MoveResult = false;
        var refused = MoveTwoCorps(tracker, host, state, 1);     // the fake never moves a formation: both missed, both retried, vanilla refuses
        AssertEqual(2, refused.Retried, "both are retried");
        AssertEqual(0, refused.IssueRows.Count, "a refused retry moved nothing: no issue row");

        var againHost = new FakeMoverHost();
        var againTracker = new MoveTracker();
        MoveTwoCorps(againTracker, againHost, state, 0);
        var taken = MoveTwoCorps(againTracker, againHost, state, 1);
        AssertEqual(2, taken.Retried, "both are retried");
        AssertEqual(2, taken.IssueRows.Count, "and the host took them: a retry that moved is an issue row");

        // A move queued behind a running order spends none of the budget and writes none either.
        var queuedHost = new FakeMoverHost();
        var s = ExecutorFixtures.Wide();
        var queued = ExecutorFixtures.Corps(1, 350f, 6000f);
        queued.Flags |= FormationFlags.MoveQueued;
        s.Own.Add(queued);
        s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), queuedHost, new MoveTracker(), state);
        AssertEqual(1, tally.Queued, "the move waits in vanilla's queue");
        AssertEqual(0, tally.IssueRows.Count, "and is no issue row");
    }

    private static void IssueRowPickIsPinnedNewOrDash()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 6000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 360f, 6000f));
        s.Own.Add(ExecutorFixtures.Corps(3, 370f, 6000f));
        var plan = ExecutorFixtures.HandPlan(s, new int[0]);
        var alloc = new Allocation { Alliance = 0 };
        var fresh = ExecutorFixtures.Assign(1, FormationRole.DepotBuilder, 3, 300f, 0f);
        fresh.Reason = ForceAllocator.ReasonDepotTask;
        var pinned = ExecutorFixtures.Assign(2, FormationRole.DepotBuilder, 3, 300f, 0f);
        pinned.Reason = ForceAllocator.ReasonPinnedBuilder;
        var other = ExecutorFixtures.Assign(3, FormationRole.CapitalGuard, 0, 0f, 0f);
        other.Reason = ForceAllocator.ReasonCapitalFloor;
        alloc.Assignments.Add(fresh);
        alloc.Assignments.Add(pinned);
        alloc.Assignments.Add(other);
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(3, tally.IssueRows.Count, "three moves");
        string Row(string unit) => tally.IssueRows.Find(r => r.Contains(" unit=" + unit + " "));
        AssertContains(Row("corps1:1"), " reason=depot-task ", "a freshly picked builder");
        AssertTrue(Row("corps1:1").EndsWith(" pick=new", StringComparison.Ordinal), "is pick=new");
        AssertContains(Row("corps2:2"), " reason=depot-task-pinned ", "a Wished task's builder");
        AssertTrue(Row("corps2:2").EndsWith(" pick=pinned", StringComparison.Ordinal), "is pick=pinned");
        AssertTrue(Row("corps3:3").EndsWith(" pick=-", StringComparison.Ordinal), "every other role is pick=-");
        AssertContains(Row("corps3:3"), " objective=-1 ", "and a role with no objective writes -1");
    }

    // A unit key with a space is one value: the parser keeps all of it and the field after it (the convention of [CampaignMoverMiss]).
    private static void RowNamesAreQuoted()
    {
        var s = ExecutorFixtures.Wide();
        var named = ExecutorFixtures.Corps(1, 350f, 6000f);
        named.UnitKey = "10th Indiana:5";
        s.Own.Add(named);
        s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(1, tally.IssueRows.Count, "one move");
        AssertContains(tally.IssueRows[0], " unit=\"10th Indiana:5\" role=MainEffort ", "the unit key is quoted");
        AssertTrue(TelemetryLegacyParser.TryParse(tally.IssueRows[0], TelemetryProfile.CampaignTuning, "s", out var ev), "the row parses");
        AssertEqual("10th Indiana:5", ev.Unit, "the whole name survives");
        AssertEqual("MainEffort", ev.Fields.GetString("role"), "and the field after it is its own");
        AssertEqual("1861-06-15", ev.CampaignDate, "the parser lifts date= into the event's campaign date");
    }

    // The tally's rows are built in the pure mover and written by the runtime: a row nobody writes is no telemetry (verify, F1). Both loops are
    // pinned in the runtime's Execute, each through TelemetryRouter.LegacyInfo, the issue rows beside the miss rows.
    private static void TheMoverRuntimeWritesTheIssueAndTheMissRows()
    {
        string execute = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMoverRuntime.cs"), "internal static void Execute(");
        AssertContains(execute, "for (int m = 0; m < tally.IssueRows.Count; m++) TelemetryRouter.LegacyInfo(tally.IssueRows[m], TelemetryLayer.Campaign);", "the issue rows are written");
        AssertContains(execute, "for (int m = 0; m < tally.MissRows.Count; m++) TelemetryRouter.LegacyInfo(tally.MissRows[m], TelemetryLayer.Campaign);", "so are the miss rows");
        AssertInOrder(execute, "Execute", "CampaignMover.Review(", "tally.IssueRows.Count", "tally.MissRows.Count");
        // Every list of rows the tally builds has its write loop: a new list that nobody writes fails here.
        string mover = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMover.cs");
        foreach (Match list in Regex.Matches(mover, @"public readonly List<string> (\w+Rows) = new List<string>\(\);"))
            AssertContains(execute, "tally." + list.Groups[1].Value + "[m]", "the runtime writes " + list.Groups[1].Value);
    }

    // ---- the plan trace ----

    private static void TraceCarriesEachCandidatesCoordinates()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 6000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 360f, 6000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
        TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, ExecutorFixtures.State(), null, null, ExecutorFixtures.Speed(), out string trace);
        var tokens = Regex.Matches(trace, @"\| (-?\d+)@(-?\d+) at=(-?\d+),(-?\d+) s=");
        AssertTrue(tokens.Count >= 2, "each candidate: objective@sector, then its coordinates, then its score (" + tokens.Count + "): " + trace);
        bool saw98 = false;
        foreach (Match t in tokens)
            if (t.Groups[1].Value == "98") { saw98 = true; AssertTrue(t.Groups[3].Value == "100" && t.Groups[4].Value == "0", "objective 98 stands at 100,0: " + t.Value); }
        AssertTrue(saw98, "objective 98 is a candidate: " + trace);
    }

    // ---- the tag policy ----

    private static void NewRowsAreRegisteredInTheTagPolicy()
    {
        var issue = TelemetryTagPolicy.Route("[CampaignMoverIssue] alliance=1 date=1861-06-23 unit=a role=DepotBuilder");
        AssertEqual("CampaignMoverIssue", issue.EventName, "the router knows the issue row");
        AssertEqual(TelemetryCategory.Write, issue.Category, "it is a Write");
        AssertEqual(TelemetryLayer.Campaign, issue.Layer, "on the campaign layer");
        var reserves = TelemetryTagPolicy.Route("[Reserves] alliance=1 date=1861-06-23 capital=1/2");
        AssertEqual("Reserves", reserves.EventName, "the router knows the reserves row");
        AssertEqual(TelemetryCategory.State, reserves.Category, "it is State");
        AssertEqual(TelemetryLayer.Campaign, reserves.Layer, "on the campaign layer");
    }

    // ---- the allocator's pinned-builder reason and the shared sums ----

    private static void PinnedBuilderCarriesItsOwnReason()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 6000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 360f, 5000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = ExecutorFixtures.HandPlan(s, new int[0]);
        int sector = sectors.SectorAt(350f, 0f);
        var pin = new[] { new HeldBuilder { Wished = true, InstanceId = 1, SectorId = sector, TargetX = 250f, TargetZ = 0f } };
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null, null, pin);
        AssertTrue(a.TryGet(1, out var pinned) && pinned.Role == FormationRole.DepotBuilder, "the pinned formation builds");
        AssertEqual(ForceAllocator.ReasonPinnedBuilder, pinned.Reason, "with the pinned reason");
        AssertEqual("depot-task-pinned", ForceAllocator.ReasonPinnedBuilder, "the string the issue row's pick reads");
        AssertTrue(ForceAllocator.ReasonPinnedBuilder != ForceAllocator.ReasonDepotTask, "split from the fresh pick's reason");
    }

    // RoleStrength and CountedHold are the pass's own sums, lifted out so [Reserves] reads the same definition: the capital guards'
    // strength (any role member, floored at 0) and a sector's counted hold (Hold and CapitalGuard standing in the role sector, at
    // HoldStrength, the player's own command counting nothing).
    private static void AllocatorHelpersKeepTheShippedSums()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 10f, 9000f));      // the capital guard, standing in the capital's sector
        s.Own.Add(ExecutorFixtures.Corps(2, 550f, 7000f));
        s.Own.Add(ExecutorFixtures.Corps(3, 560f, 4000f));
        var sectors = ExecutorFixtures.Sectors(s);
        int capital = sectors.SectorAt(s.CapitalX, s.CapitalZ), east = sectors.SectorAt(550f, 0f);
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.CapitalGuard, capital, 0f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.Hold, east, 550f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(3, FormationRole.Hold, capital, 560f, 0f));   // role sector is not the sector it stands in
        AssertNear(9000f, ForceAllocator.RoleStrength(s, a, FormationRole.CapitalGuard), 0.01f, "the capital guard's strength");
        AssertNear(0f, ForceAllocator.RoleStrength(s, a, FormationRole.Recon), 0.01f, "no scouts");
        AssertNear(ReserveRules.HoldStrength(s.Own[1]), ForceAllocator.CountedHold(s, sectors, a, east), 0.01f, "the east holds what stands there in its role sector");
        AssertNear(ReserveRules.HoldStrength(s.Own[0]), ForceAllocator.CountedHold(s, sectors, a, capital), 0.01f,
            "the capital's sector counts its guard, not the formation that stands elsewhere with the capital as its role sector");
        s.Own[1] = WithChain(s.Own[1], WlChainClass.PlayerCommand);
        AssertNear(0f, ForceAllocator.CountedHold(s, sectors, a, east), 0.01f, "the player's own command holds nothing");
    }

    // ---- [Reserves] ----

    // The Wide snapshot with the capital guard (corps 1, in the capital's sector) and an eastern hold (corps 2 at x 550, need 9000).
    private sealed class ReserveDay
    {
        public CampaignSnapshot S;
        public SectorModel Sectors;
        public TheaterPlan Plan;
        public Allocation Alloc;
        public PlannerState State;
        public int Capital, East;
    }

    private static ReserveDay Day()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 10f, 9000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 550f, 7000f));
        s.Own.Add(ExecutorFixtures.Corps(3, 330f, 4000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var d = new ReserveDay
        {
            S = s, Sectors = sectors, State = ExecutorFixtures.State(), Plan = ExecutorFixtures.HandPlan(s, new int[0]), Alloc = new Allocation { Alliance = 0 },
            Capital = sectors.SectorAt(s.CapitalX, s.CapitalZ), East = sectors.SectorAt(550f, 0f)
        };
        d.Plan.CapitalFloorStrength = 12000f;
        d.Plan.Sectors.Add(new SectorPlan { SectorId = d.East, Posture = TheaterPosture.Hold, TargetObjectiveId = -1, TargetX = 550f, TargetZ = 0f, HoldNeed = 9000f });
        d.Alloc.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.CapitalGuard, d.Capital, 0f, 0f));
        d.Alloc.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.Hold, d.East, 550f, 0f));
        d.Alloc.Assignments.Add(ExecutorFixtures.Assign(3, FormationRole.Hold, sectors.SectorAt(330f, 0f), 330f, 0f));
        return d;
    }

    private static string Row(ReserveDay d, Allocation previous = null, TheaterPlan previousPlan = null)
    {
        return ReservesRow.Build(d.S, d.Sectors, d.Plan, d.Alloc, d.State, previous, previousPlan);
    }

    private static string Field(string row, string name)
    {
        var m = Regex.Match(row, " " + name + "=(\\S+)");
        AssertTrue(m.Success, "the row has " + name + "=: " + row);
        return m.Groups[1].Value;
    }

    private static void ReservesRowNamesCapitalAndHoldShares()
    {
        var d = Day();
        string row = Row(d);
        AssertTrue(row.StartsWith("[Reserves] alliance=0 date=1861-06-15 capital=9000/12000 hold=", StringComparison.Ordinal), "the capital's held and floor, after the alliance and date: " + row);
        var parts = Field(row, "hold").Split(',');
        AssertEqual(d.East + ":7000/9000", parts[0], "the sector with the largest need first: its held against max(HoldNeed, the garrison minimum)");
        string capital = d.Capital + ":9000/" + ((int)TheaterPlanner.MinStrengthFallback);
        AssertTrue(Array.IndexOf(parts, capital) > 0, "the capital's sector holds its guard: " + capital + " in " + row);
        AssertTrue(TelemetryLegacyParser.TryParse(row, TelemetryProfile.CampaignTuning, "s", out var ev), "the row parses");
        AssertEqual("1861-06-15", ev.CampaignDate, "date= is lifted into the campaign date");
        AssertEqual("Reserves", TelemetryTagPolicy.Route(row).EventName, "and the router files it");
    }

    private static void ReservesRowListsAtMostSixSectorsLargestNeedFirst()
    {
        var s = ExecutorFixtures.Line(0, 14);
        for (int k = 0; k < 9; k++)   // a live objective seeds a sector of its own
            s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40 + k, X = 100f * k, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.5f });
        s.Own.Add(ExecutorFixtures.Corps(1, 10f, 9000f));
        var sectors = ExecutorFixtures.Sectors(s);
        AssertTrue(sectors.Count > 6, "the fixture has more than six sectors: " + sectors.Count);
        var plan = ExecutorFixtures.HandPlan(s, new int[0]);
        plan.Sectors.Clear();
        plan.Sectors.Add(new SectorPlan { SectorId = sectors.Count - 1, HoldNeed = 20000f });
        plan.Sectors.Add(new SectorPlan { SectorId = 2, HoldNeed = 15000f });
        plan.Sectors.Add(new SectorPlan { SectorId = 1, HoldNeed = 15000f });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.Hold, sectors.SectorAt(10f, 0f), 10f, 0f));
        string row = ReservesRow.Build(s, sectors, plan, a, ExecutorFixtures.State(), null, null);
        var parts = Field(row, "hold").Split(',');
        AssertEqual(6, parts.Length, "six sectors and no more: " + row);
        AssertTrue(parts[0].StartsWith((sectors.Count - 1) + ":", StringComparison.Ordinal), "the largest need first: " + row);
        AssertTrue(parts[1].StartsWith("1:", StringComparison.Ordinal) && parts[2].StartsWith("2:", StringComparison.Ordinal), "equal needs by the lower sector id: " + row);
        AssertTrue(parts[3].EndsWith("/" + (int)TheaterPlanner.MinStrengthFallback, StringComparison.Ordinal), "the rest at the garrison minimum: " + row);
    }

    private static void ReservesRowNamesEachDepartmentsStrength()
    {
        var d = Day();
        d.State.Transfer(d.S.Own[0].UnitKey, Theater.East);
        d.State.Transfer(d.S.Own[1].UnitKey, Theater.West);
        d.State.Transfer(d.S.Own[2].UnitKey, Theater.East);
        AssertEqual("East:13000,West:7000", Field(Row(d), "dept"), "each department's HoldStrength, in department order");
        // The player's own command holds nothing, so it adds nothing to any department.
        d.S.Own[1] = WithChain(d.S.Own[1], WlChainClass.PlayerCommand);
        AssertEqual("East:13000", Field(Row(d), "dept"), "and a department with nothing counted is not listed");
        d.S.Own[0] = WithChain(d.S.Own[0], WlChainClass.PlayerCommand);
        d.S.Own[2] = WithChain(d.S.Own[2], WlChainClass.PlayerCommand);
        AssertEqual("none", Field(Row(d), "dept"), "none when nothing counts");
    }

    // Defence counts read today's snapshot against YESTERDAY's allocation and plan, captured before today's Publish.
    private static void ReservesDefenceCountsReadYesterdaysAllocation()
    {
        var d = Day();
        var yesterday = new Allocation { Alliance = 0 };
        yesterday.Assignments.Add(Withheld(ExecutorFixtures.Assign(1, FormationRole.Reserve, d.Capital, 0f, 0f)));
        yesterday.Assignments.Add(Withheld(ExecutorFixtures.Assign(2, FormationRole.Hold, d.East, 550f, 0f)));
        yesterday.Assignments.Add(ExecutorFixtures.Assign(3, FormationRole.MainEffort, d.East, 550f, 0f));
        var yesterdaysPlan = ExecutorFixtures.HandPlan(d.S, new[] { 3 });
        // Nothing is in a vanilla defensive operation yet.
        string quiet = Row(d, yesterday, yesterdaysPlan);
        AssertEqual("0", Field(quiet, "vanillaDefWithheld"), "no violation");
        AssertEqual("0", Field(quiet, "vanillaDefHoldOrCovering"), "no exception");
        AssertEqual("0", Field(quiet, "massInVanillaDef"), "no mass member");
        // Today formation 1 (yesterday a withheld Reserve) and 2 (yesterday a withheld Hold) and 3 (yesterday's mass) are in vanilla's operations.
        for (int i = 0; i < 3; i++) d.S.Own[i] = WithFlag(d.S.Own[i], FormationFlags.InDefensiveOps);
        string row = Row(d, yesterday, yesterdaysPlan);
        AssertEqual("1", Field(row, "vanillaDefWithheld"), "a withheld Reserve in a vanilla defensive operation is a violation");
        AssertEqual("1", Field(row, "vanillaDefHoldOrCovering"), "a withheld Hold is the exception the rules allow");
        AssertEqual("1", Field(row, "massInVanillaDef"), "yesterday's mass member is counted apart");
        // Today's allocation is not what the counts read: with yesterday's allocation unknown (the first review) they are all 0.
        string first = Row(d, null, null);
        AssertEqual("0", Field(first, "vanillaDefWithheld"), "no yesterday, no violation");
        AssertEqual("0", Field(first, "massInVanillaDef"), "and no mass");
        // Each role the violation names counts; a Garrison or a Support formation does not.
        foreach (var role in new[] { FormationRole.CapitalGuard, FormationRole.DepotBuilder, FormationRole.Recon, FormationRole.Recover })
        {
            var one = new Allocation { Alliance = 0 };
            one.Assignments.Add(Withheld(ExecutorFixtures.Assign(1, role, d.Capital, 0f, 0f)));
            AssertEqual("1", Field(Row(d, one, yesterdaysPlan), "vanillaDefWithheld"), role + " withheld by yesterday's allocation");
        }
        var support = new Allocation { Alliance = 0 };
        support.Assignments.Add(Withheld(ExecutorFixtures.Assign(1, FormationRole.Support, d.Capital, 0f, 0f)));
        AssertEqual("0", Field(Row(d, support, yesterdaysPlan), "vanillaDefWithheld"), "a Support formation is not one the planner withheld");

        // The capture is the working copy before today's Publish overwrites it (as AgeRecon reads it).
        string body = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs"), "internal static void ReviewPlan(");
        AssertInOrder(body, "ReviewPlan", "CampaignPlanStore.TryGetAllocation(alliance, out var yesterdaysAllocation)", "CampaignPlanStore.TryGetPlan(alliance, out var yesterdaysPlan)",
            "CampaignPlanStore.Publish(alliance, plan, allocation, sectors);", "EmitReserves(");
    }

    private static Assignment Withheld(Assignment a)
    {
        a.Withheld = true;
        return a;
    }

    private static FormationRecord WithFlag(FormationRecord f, FormationFlags flag)
    {
        f.Flags |= flag;
        return f;
    }

    // Capital held and a sector's held are the allocator's own sums; the row defines none of its own.
    private static void ReservesRowSharesTheAllocatorsCountedHold()
    {
        var d = Day();
        string row = Row(d);
        AssertEqual(((int)ForceAllocator.RoleStrength(d.S, d.Alloc, FormationRole.CapitalGuard)).ToString(CultureInfo.InvariantCulture), Regex.Match(row, "capital=(\\d+)/").Groups[1].Value,
            "capital= is RoleStrength");
        string east = d.East + ":" + ((int)ForceAllocator.CountedHold(d.S, d.Sectors, d.Alloc, d.East)).ToString(CultureInfo.InvariantCulture) + "/";
        AssertContains(Field(row, "hold"), east, "the east's held is CountedHold");
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReservesRow.cs");
        AssertContains(src, "ForceAllocator.RoleStrength(", "the row calls the allocator's capital sum");
        AssertContains(src, "ForceAllocator.CountedHold(", "and its hold sum");
        AssertEqual(1, Regex.Matches(src, @"HoldStrength\(").Count, "the only strength it adds up itself is a department's, once");
        AssertFalse(src.Contains(".Strength") || src.Contains("_roleSector") || src.Contains("SectorId == sector"), "no capital or hold sum of its own");
    }

    // The shipped ReviewPlanPassesTheTrackersGaveUpSet pins that no objective is read back from an earlier plan. The row reads yesterday's
    // plan for its mass list alone (telemetry), and the review's locals are named for the day, not "previousPlan", so that pin stands unedited.
    private static void ReservesRowReadsOnlyTheMassOfYesterdaysPlan()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReservesRow.cs");
        var uses = Regex.Matches(src, @"previousPlan\.(\w+)");
        AssertTrue(uses.Count >= 1, "the row reads yesterday's plan");
        foreach (Match use in uses) AssertEqual("MassInstanceIds", use.Groups[1].Value, "yesterday's plan is read for its mass list alone");
    }

    // [Reserves] is written every review, from the Full path after [Allocation], with no signature compare.
    private static void ReservesRowEveryReview()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string review = BodyOf(runtime, "internal static void ReviewPlan(");
        AssertInOrder(review, "ReviewPlan", "EmitPlan(plan, trace, day, month, year);", "EmitAllocation(allocation, plan, snapshot.Player, reliability, NonCompliance[alliance].Days);", "EmitReserves(");
        string emit = BodyOf(runtime, "private static void EmitReserves(");
        AssertContains(emit, "TelemetryRouter.LegacyInfo(ReservesRow.Build(", "the row goes out");
        AssertFalse(emit.Contains("string.Equals") || emit.Contains("Last"), "with no signature compare: the review is the clock every criterion counts on");
        // The paths that return before the plan write no row: a stand-down or bookkeeping day has no review day.
        AssertTrue(review.IndexOf("if (mode == PlannerReviewMode.Bookkeeping) return;", StringComparison.Ordinal) < review.IndexOf("EmitReserves(", StringComparison.Ordinal),
            "[Reserves] is emitted after the Bookkeeping and StandDown returns");
    }

    // ---- #25's reasons ----

    private static void DefenseFilterReasonsAreTalliedAndSorted()
    {
        var reasons = new Dictionary<int, string>
        {
            { 5, "min-hold" }, { 6, "asset-proximity-local-only" }, { 7, "min-hold" }, { 8, "player-controlled" }, { 9, "min-hold" }, { 99, "critical-front" }
        };
        AssertEqual("asset-proximity-local-only:1,min-hold:3,player-controlled:1", DefenseFilterTally.Reasons(reasons, new[] { 9, 5, 8, 6, 7 }), "sorted by reason, counted over the ids");
        AssertEqual("min-hold:1", DefenseFilterTally.Reasons(reasons, new[] { 5 }), "over the ids asked for only (99 is not among them)");
        AssertEqual("-", DefenseFilterTally.Reasons(reasons, new int[0]), "no ids is a dash");
        AssertEqual("-", DefenseFilterTally.Reasons(reasons, new[] { 1000 }), "an id with no reason is not counted");
        AssertEqual("-", DefenseFilterTally.Reasons(null, new[] { 5 }), "no reasons is a dash");
        AssertEqual("-", DefenseFilterTally.Reasons(reasons, null), "no ids is a dash");
        string patch = ReadRepoFile("src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs");
        AssertContains(patch, "DefenseFilterTally.Reasons(", "#25 reports its reasons");
        AssertContains(BodyOf(patch, "private static void EmitDefenseIntentFilter("), "reasons", "on the filter row");
    }

    private static FormationRecord WithChain(FormationRecord f, WlChainClass chain)
    {
        f.Chain = chain;
        return f;
    }

    private static void AssertInOrder(string body, string label, params string[] anchors)
    {
        int at = -1;
        foreach (string anchor in anchors)
        {
            int next = body.IndexOf(anchor, at + 1, StringComparison.Ordinal);
            AssertTrue(next > at, label + " has \"" + anchor + "\" after the one before it");
            at = next;
        }
    }
}
