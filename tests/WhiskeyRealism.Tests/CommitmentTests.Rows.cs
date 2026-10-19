using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// F7, the [Reserves] row's three fields: committed=<n>/<men>, endedListed=<n> and endedMarching=<n> (D11 requirement-telemetry).
internal static partial class CommitmentTests
{
    private static (string name, Action run)[] RowCases()
    {
        return new (string name, Action run)[]
        {
            ("TheReservesRowCountsTheCommittedAndTheirMen", TheReservesRowCountsTheCommittedAndTheirMen),
            ("EndedMarchingCountsAPathOrAQueuedOrder", EndedMarchingCountsAPathOrAQueuedOrder),
            ("EndedListedIsWhatVanillaListsAgain", EndedListedIsWhatVanillaListsAgain),
            ("TheReservesRowKeepsItsFieldOrder", TheReservesRowKeepsItsFieldOrder),
        };
    }

    private static string Field(string row, string name)
    {
        foreach (string part in row.Split(' '))
            if (part.StartsWith(name + "=", StringComparison.Ordinal)) return part.Substring(name.Length + 1);
        return null;
    }

    private static string ReservesOf(CampaignSnapshot s, TheaterPlan plan, Allocation a, Allocation previous = null, TheaterPlan previousPlan = null)
    {
        return ReservesRow.Build(s, ExecutorFixtures.Sectors(s), plan, a, new PlannerState(), previous, previousPlan);
    }

    // committed= counts the formations committed this review: the push's corps the mass kept (a Mass claim with the committed reason) and the
    // corps waiting out an ended push (a Committed claim), and their men.
    private static void TheReservesRowCountsTheCommittedAndTheirMen()
    {
        var state = FirstDay(out _);
        var s = PushWorld(readiness2: 1);
        var plan = Replan(s, state, Moves(98, 1, 2));
        var a = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        AssertEqual("2/8000", Field(ReservesOf(s, plan, a), "committed"), "the push goes on: both corps are committed to it: " + Describe(plan));
        var world = AccomplishedWorld(1);
        var endPlan = Replan(world, FirstDay(out _), Moves(98, 1, 2));
        var endAlloc = ForceAllocator.Allocate(endPlan, world, ExecutorFixtures.Sectors(world), null, null, null);
        AssertEqual("1/4000", Field(ReservesOf(world, endPlan, endAlloc), "committed"), "the push ended: the corps the new mass did not take waits, the other is an ordinary member: " + Describe(endPlan));
        var quiet = PushWorld();
        var quietPlan = Replan(quiet, new PlannerState(), null);
        var quietAlloc = ForceAllocator.Allocate(quietPlan, quiet, ExecutorFixtures.Sectors(quiet), null, null, null);
        AssertEqual("0/0", Field(ReservesOf(quiet, quietPlan, quietAlloc), "committed"), "no push is committed on its first review");
    }

    private static void EndedMarchingCountsAPathOrAQueuedOrder()
    {
        var yesterday = new TheaterPlan { Alliance = 0 };
        yesterday.CommitmentEnded.AddRange(new[] { 1, 2, 3, 4 });
        var before = Alloc(Wait(1, 240f), Wait(2, 250f), Wait(3, 260f),
                           new Assignment { InstanceId = 4, Role = FormationRole.MainEffort, SectorId = 0, TargetX = 100f, TargetZ = 0f, Reason = "mass" });
        var s = Wide(Marcher(1, 240f, FormationFlags.HasPath), Marcher(2, 250f, FormationFlags.MoveQueued), Marcher(3, 260f, 0), Marcher(4, 270f, FormationFlags.HasPath));
        var today = Alloc(HoldAt(1, 240f), HoldAt(2, 250f), HoldAt(3, 260f), HoldAt(4, 270f));
        string row = ReservesOf(s, NoPush(s), today, before, yesterday);
        AssertEqual("2", Field(row, "endedMarching"), "of yesterday's corps that waited, those still with a path or a queued order: the stop did not work for them (3 stopped, 4 was never one)");
        var none = ReservesOf(s, NoPush(s), today, before, null);
        AssertEqual("0", Field(none, "endedMarching"), "with no previous plan there is nothing to count");
    }

    private static void EndedListedIsWhatVanillaListsAgain()
    {
        var yesterday = new TheaterPlan { Alliance = 0 };
        yesterday.CommitmentEnded.AddRange(new[] { 1, 2, 3 });
        var s = Wide(Marcher(1, 240f, FormationFlags.InOffensiveOps, 3), Marcher(2, 250f, FormationFlags.InOffensiveOps, 3), Marcher(3, 260f, 0, 3));
        var today = Alloc(HoldAt(1, 240f),
                          new Assignment { InstanceId = 2, Role = FormationRole.Support, SectorId = 0, TargetX = 100f, TargetZ = 0f, Reason = "reinforcement" },
                          HoldAt(3, 260f));
        string row = ReservesOf(s, NoPush(s), today, null, yesterday);
        AssertEqual("1", Field(row, "endedListed"), "of yesterday's ended corps, the one in vanilla's offensive list again and not a Support or MainEffort today");
    }

    private static Assignment HoldAt(int id, float x)
    {
        return new Assignment { InstanceId = id, Role = FormationRole.Hold, SectorId = 0, TargetX = x, TargetZ = 0f, Reason = ForceAllocator.ReasonHoldReserve };
    }

    private static void TheReservesRowKeepsItsFieldOrder()
    {
        var s = Wide(Marcher(1, 240f, 0));
        string row = ReservesOf(s, NoPush(s), Alloc(HoldAt(1, 240f)));
        int holdReserve = row.IndexOf(" holdReserve=", StringComparison.Ordinal);
        int committed = row.IndexOf(" committed=", StringComparison.Ordinal);
        int listed = row.IndexOf(" endedListed=", StringComparison.Ordinal);
        int marching = row.IndexOf(" endedMarching=", StringComparison.Ordinal);
        int dept = row.IndexOf(" dept=", StringComparison.Ordinal);
        AssertTrue(holdReserve > 0 && holdReserve < committed && committed < listed && listed < marching && marching < dept, "holdReserve, committed, endedListed, endedMarching, then dept: " + row);
    }
}
