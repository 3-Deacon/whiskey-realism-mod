using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC15 decision 7: the posture producer's NavMesh plan budget (4 plans per side per tick) walks the groups in ascending instance id, starting
// after the last id it planned. The cursor is an id, not an index modulo the walked count, so a roster that changes between ticks neither
// skips nor repeats a group, and a full re-plan of D waiting groups takes ceil(D / 4) ticks.
internal static partial class BattleLodTests
{
    private static (string, Action)[] NavTests()
    {
        return new (string, Action)[]
        {
            ("bc15 NAV the budget defers once it is spent and refills on the next apply", NavBudgetDefersAndRefills),
            ("bc15 NAV NavCursorStableAscendingId: the walk starts after the last planned id", NavCursorStableAscendingId),
            ("bc15 NAV the cursor survives a roster that changes between ticks", NavCursorSurvivesRosterChange),
            ("bc15 NAV negative instance ids order and wrap", NavCursorOrdersNegativeIds),
            ("bc15 NAV an anonymous consumption (the road-route budget) leaves the cursor alone", AnonymousConsumeLeavesTheCursor),
            ("bc15 NAV WholeSideReplanPlansEveryContactGroupWithinBound: 100 groups, 4 plans a tick, 25 ticks", WholeSideReplanWithinBound),
            ("bc15 NAV endless demand is served round-robin by id", PersistentDemandIsServedRoundRobin),
            ("bc15 NAV a contact group is never behind a non-due Distant group", ContactGroupNeverWaitsBehindADistantGroup),
            ("bc15 NAV NavDeferredGroupStaysFullUntilPlanned", NavDeferredGroupStaysFullUntilPlanned),
        };
    }

    // The posture loop in miniature: walk the sorted ids from the budget's start, a group that needs a plan takes one if the budget has it.
    private static int WalkTick(NavPlanBudget budget, int[] sortedIds, bool[] needsPlan, bool[] walked, List<int> planned)
    {
        budget.NextApply();
        int deferred = 0;
        int start = budget.StartIndex(sortedIds, sortedIds.Length);
        for (int k = 0; k < sortedIds.Length; k++)
        {
            int at = (start + k) % sortedIds.Length;
            if (walked != null && !walked[at]) continue;
            if (!needsPlan[at]) continue;
            if (budget.TryConsume(sortedIds[at]))
            {
                needsPlan[at] = false;
                planned?.Add(sortedIds[at]);
            }
            else
            {
                deferred++;
            }
        }
        return deferred;
    }

    private static void NavBudgetDefersAndRefills()
    {
        var budget = new NavPlanBudget(2);
        AssertTrue(budget.TryConsume(10), "first");
        AssertTrue(budget.TryConsume(11), "second");
        AssertFalse(budget.TryConsume(12), "third deferred");
        AssertEqual(2, budget.StartIndex(new[] { 10, 11, 12, 13 }, 4), "the cursor is the last id that got a plan (11), not the deferred one: the walk resumes at 12");
        budget.NextApply();
        AssertTrue(budget.TryConsume(12), "refilled next apply");
    }

    private static void NavCursorStableAscendingId()
    {
        var budget = new NavPlanBudget(2);
        int[] ids = { 5, 9, 12, 20 };
        var needs = new[] { true, true, true, true };
        var planned = new List<int>();
        WalkTick(budget, ids, needs, null, planned);
        AssertEqual("5,9", string.Join(",", planned), "tick 1 plans the two lowest ids");
        planned.Clear();
        WalkTick(budget, ids, needs, null, planned);
        AssertEqual("12,20", string.Join(",", planned), "tick 2 starts after id 9");
        AssertEqual(0, budget.StartIndex(ids, ids.Length), "after the highest id the walk wraps to the first");
        AssertEqual(0, budget.StartIndex(ids, 0), "an empty list");
        AssertEqual(0, new NavPlanBudget(4).StartIndex(ids, ids.Length), "no plan yet: from the first");
    }

    private static void NavCursorSurvivesRosterChange()
    {
        var budget = new NavPlanBudget(2);
        int[] ids = { 5, 9, 12, 20 };
        var needs = new[] { true, true, true, true };
        WalkTick(budget, ids, needs, null, null);   // plans 5 and 9; the cursor is 9
        // Group 5 is destroyed and a new group 10 arrives; an index cursor would now point at 12 or skip 10.
        int[] changed = { 9, 10, 12, 20 };
        var needsChanged = new[] { false, true, true, true };
        var planned = new List<int>();
        WalkTick(budget, changed, needsChanged, null, planned);
        AssertEqual("10,12", string.Join(",", planned), "the new group is reached in id order, nothing is skipped or repeated");
    }

    private static void NavCursorOrdersNegativeIds()
    {
        // Unity instance ids of runtime-created objects are negative.
        var budget = new NavPlanBudget(1);
        int[] ids = { -40, -7, 3, 18 };
        var needs = new[] { true, true, true, true };
        var planned = new List<int>();
        for (int t = 0; t < 5; t++) WalkTick(budget, ids, needs, null, planned);
        AssertEqual("-40,-7,3,18", string.Join(",", planned), "ascending across zero, one plan a tick");
    }

    private static void AnonymousConsumeLeavesTheCursor()
    {
        var budget = new NavPlanBudget(1);
        AssertTrue(budget.TryConsume(), "an anonymous plan");
        AssertFalse(budget.TryConsume(), "spent");
        int[] ids = { 5, 9 };
        AssertEqual(0, budget.StartIndex(ids, 2), "no cursor was set");
    }

    private static void WholeSideReplanWithinBound()
    {
        const int groups = 100, perTick = 4, bound = 25;
        int[] ids = new int[groups];
        for (int g = 0; g < groups; g++) ids[g] = 1000 + g * 7;
        var needs = new bool[groups];
        for (int g = 0; g < groups; g++) needs[g] = true;
        var budget = new NavPlanBudget(perTick);
        var tickOf = new int[groups];
        for (int t = 1; t <= bound + 5; t++)
        {
            var planned = new List<int>();
            WalkTick(budget, ids, needs, null, planned);
            foreach (int id in planned) tickOf[Array.IndexOf(ids, id)] = t;
        }
        for (int g = 0; g < groups; g++)
        {
            AssertTrue(tickOf[g] > 0, "group " + g + " was planned");
            AssertTrue(tickOf[g] <= bound, "group " + g + " planned on tick " + tickOf[g] + ", within " + bound);
        }
    }

    // Demand that never ends (every group needs a fresh plan every tick, as when the targets keep moving) is served round-robin: a restart at the
    // first group each tick would serve the same four for ever.
    private static void PersistentDemandIsServedRoundRobin()
    {
        const int groups = 100, perTick = 4;
        int[] ids = new int[groups];
        for (int g = 0; g < groups; g++) ids[g] = 300 + g;
        var needs = new bool[groups];
        var served = new int[groups];
        var budget = new NavPlanBudget(perTick);
        for (int t = 0; t < 50; t++)
        {
            for (int g = 0; g < groups; g++) needs[g] = true;
            var planned = new List<int>();
            WalkTick(budget, ids, needs, null, planned);
            foreach (int id in planned) served[id - 300]++;
        }
        for (int g = 0; g < groups; g++) AssertEqual(2, served[g], "group " + g + " served twice in 50 ticks of 4 plans (every 25th tick)");
    }

    private static void ContactGroupNeverWaitsBehindADistantGroup()
    {
        const int groups = 100;
        int[] ids = new int[groups];
        var needs = new bool[groups];
        var walked = new bool[groups];
        int contact = 0;
        for (int g = 0; g < groups; g++)
        {
            ids[g] = 500 + g;
            bool isContact = g % 3 != 0;               // two thirds are Full (in contact or moving)
            walked[g] = isContact;                      // a non-due Distant group is not walked
            needs[g] = true;                            // every group would need a plan
            if (isContact) contact++;
        }
        var budget = new NavPlanBudget(4);
        int ticks = 0;
        var planned = new List<int>();
        int bound = (contact + 3) / 4;
        while (ticks < bound)
        {
            ticks++;
            WalkTick(budget, ids, needs, walked, planned);
        }
        AssertEqual(contact, planned.Count, "every walked group planned within ceil(" + contact + " / 4) = " + bound + " ticks");
        for (int g = 0; g < groups; g++)
            if (!walked[g]) AssertTrue(needs[g], "a non-due Distant group took no plan");
    }

    private static void NavDeferredGroupStaysFullUntilPlanned()
    {
        LodWorld w = QuietWorld();
        w.State.MarkNavDeferred(100);
        for (int t = 0; t < 6; t++)
        {
            w.Tick();
            AssertEqual(LodTier.Full, w.Tier(100), "deferred, tick " + t);
        }
        AssertEqual(1, w.State.NavDeferredCount, "counted for the telemetry row");
        w.State.MarkNavPlanned(100);
        w.Tick();
        w.Tick();
        w.Tick();
        AssertEqual(LodTier.Distant, w.Tier(100), "Distant again once a plan ran for it");
        AssertEqual(0, w.State.NavDeferredCount, "and no longer counted");
    }
}
