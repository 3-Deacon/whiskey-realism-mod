using System;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;
using static TestFixtures;

// BC16 decision 2: a group whose groupformation changes twice within 0.1 battle-hours is the tug-of-war signature (vanilla's own formation pass and
// the arbiter each ordering their own value). FormationFlipDetector is the pure half of the TacticalFormationFlip row.
internal static partial class FormationSlotTests
{
    private static (string, Action)[] FlipTests()
    {
        return new (string, Action)[]
        {
            ("bc16 FLIP TugOfWarSignatureCountsTwoChangesIn01h", TugOfWarSignatureCountsTwoChangesIn01h),
            ("bc16 FLIP the first sighting of a group is a baseline, not a change", FirstSightingIsBaseline),
            ("bc16 FLIP the same formation again is no change", SameFormationIsNoChange),
            ("bc16 FLIP the window is 0.1 battle-hours inclusive", WindowIsOneTenthHourInclusive),
            ("bc16 FLIP a change that is not countable resets the group's baseline silently", UncountableChangeResets),
            ("bc16 FLIP groups are tracked independently", GroupsAreIndependent),
            ("bc16 FLIP a clock that runs backwards is never a flip", BackwardsClockIsNoFlip),
            ("bc16 FLIP the row budget is capped while the total keeps counting", RowBudgetIsCapped),
            ("bc16 FLIP once the rows are spent a summary row follows every quarter hour", SummaryRowsFollowTheBudget),
            ("bc16 FLIP Clear forgets every group and the totals", ClearForgetsEverything),
            ("bc16 FLIP observing allocates nothing after warm-up", FlipAllocatesNothing),
        };
    }

    private static void TugOfWarSignatureCountsTwoChangesIn01h()
    {
        var d = new FormationFlipDetector();
        // Deployment: Double Line. Vanilla's pass then orders Attack Column, and 0.05 h later the arbiter orders Line back.
        AssertFalse(d.Observe(7, 1, 0.50, countable: true, out _, out _), "baseline");
        AssertFalse(d.Observe(7, 3, 1.00, countable: true, out _, out _), "one change is not a flip");
        AssertTrue(d.Observe(7, 0, 1.05, countable: true, out int from, out double since), "the second change within 0.1 h is");
        AssertEqual(3, from, "the flip left Attack Column");
        AssertNear(0.05f, (float)since, 1e-4f, "0.05 h after the change before");
        AssertTrue(d.Observe(7, 3, 1.09, countable: true, out from, out since), "and the tug-of-war goes on");
        AssertEqual(0, from, "from Line");
        AssertEqual(2, d.TotalFlips, "two flips so far");
    }

    private static void FirstSightingIsBaseline()
    {
        var d = new FormationFlipDetector();
        AssertFalse(d.Observe(1, 3, 2.0, countable: true, out _, out _), "baseline");
        AssertFalse(d.Observe(1, 0, 2.01, countable: true, out _, out _), "the first change after a baseline has no change before it");
        AssertEqual(0, d.TotalFlips, "no flips");
    }

    private static void SameFormationIsNoChange()
    {
        var d = new FormationFlipDetector();
        d.Observe(1, 1, 0.0, true, out _, out _);
        d.Observe(1, 3, 1.00, true, out _, out _);
        for (int i = 1; i <= 20; i++) AssertFalse(d.Observe(1, 3, 1.00 + 0.001 * i, true, out _, out _), "unchanged " + i);
        AssertTrue(d.Observe(1, 0, 1.05, true, out _, out _), "the repeats did not move the last change off 1.00, so 1.05 is the second change within 0.1 h");
        AssertEqual(1, d.TotalFlips, "one flip");
    }

    private static void WindowIsOneTenthHourInclusive()
    {
        var d = new FormationFlipDetector();
        d.Observe(1, 1, 0.0, true, out _, out _);
        d.Observe(1, 3, 1.0, true, out _, out _);
        AssertTrue(d.Observe(1, 0, 1.1, true, out _, out _), "exactly 0.1 h later is inside");
        AssertFalse(d.Observe(1, 3, 1.1 + 0.1000001, true, out _, out _), "a hair over 0.1 h is outside");
        AssertEqual(1, d.TotalFlips, "one flip");
        AssertEqual(0.1, FormationFlipDetector.WindowHours, "the window the plan states");
    }

    private static void UncountableChangeResets()
    {
        var d = new FormationFlipDetector();
        d.Observe(1, 1, 0.0, true, out _, out _);
        d.Observe(1, 3, 1.00, true, out _, out _);
        // An immediate placement (deployment, a re-lay in place) changes the value but is not an order war.
        AssertFalse(d.Observe(1, 0, 1.02, countable: false, out _, out _), "uncountable change is silent");
        AssertFalse(d.Observe(1, 3, 1.04, countable: true, out _, out _), "and the next change has no counted change before it");
        AssertEqual(0, d.TotalFlips, "no flips");
        AssertTrue(d.Observe(1, 0, 1.06, countable: true, out _, out _), "two counted changes within the window do flip");
    }

    private static void GroupsAreIndependent()
    {
        var d = new FormationFlipDetector();
        d.Observe(1, 1, 0.0, true, out _, out _);
        d.Observe(2, 1, 0.0, true, out _, out _);
        d.Observe(1, 3, 1.00, true, out _, out _);
        AssertFalse(d.Observe(2, 3, 1.02, true, out _, out _), "group 2's first change is not group 1's second");
        AssertTrue(d.Observe(1, 0, 1.04, true, out _, out _), "group 1's second is");
        AssertEqual(1, d.TotalFlips, "one");
    }

    private static void BackwardsClockIsNoFlip()
    {
        var d = new FormationFlipDetector();
        d.Observe(1, 1, 5.0, true, out _, out _);
        d.Observe(1, 3, 5.2, true, out _, out _);
        AssertFalse(d.Observe(1, 0, 5.1, true, out _, out _), "an earlier clock reading than the last change");
        AssertEqual(0, d.TotalFlips, "none");
    }

    private static void RowBudgetIsCapped()
    {
        AssertEqual(256, FormationFlipDetector.MaxRows, "the cap the build record states");
        var d = new FormationFlipDetector();
        int taken = 0;
        for (int i = 0; i < FormationFlipDetector.MaxRows + 50; i++) if (d.TryTakeRow()) taken++;
        AssertEqual(FormationFlipDetector.MaxRows, taken, "rows taken");
        // The total is independent of the budget: a battle that overruns the rows still reports how many flips it had.
        d.Observe(1, 1, 0.0, true, out _, out _);
        d.Observe(1, 3, 1.0, true, out _, out _);
        d.Observe(1, 0, 1.01, true, out _, out _);
        AssertEqual(1, d.TotalFlips, "the flip is counted although the budget is spent");
        AssertEqual(FormationFlipDetector.MaxRows, d.RowsTaken, "rows taken, as reported");
    }

    private static void SummaryRowsFollowTheBudget()
    {
        var d = new FormationFlipDetector();
        AssertFalse(d.TrySummary(1.0), "no summary while rows remain");
        for (int i = 0; i < FormationFlipDetector.MaxRows - 1; i++) d.TryTakeRow();
        AssertFalse(d.TrySummary(1.0), "one row still remains");
        d.TryTakeRow();
        AssertTrue(d.TrySummary(2.00), "budget spent: the first summary is due at once");
        AssertFalse(d.TrySummary(2.20), "not again within a quarter hour");
        AssertTrue(d.TrySummary(2.25), "and again at a quarter hour");
        AssertFalse(d.TrySummary(2.30), "then quiet");
        AssertTrue(d.TrySummary(1.00), "a clock that ran backwards restarts the cadence");
        d.Clear();
        AssertFalse(d.TrySummary(9.0), "Clear refills the budget");
    }

    private static void ClearForgetsEverything()
    {
        var d = new FormationFlipDetector();
        d.Observe(1, 1, 0.0, true, out _, out _);
        d.Observe(1, 3, 1.0, true, out _, out _);
        d.Observe(1, 0, 1.01, true, out _, out _);
        d.TryTakeRow();
        d.Clear();
        AssertEqual(0, d.TotalFlips, "total");
        AssertEqual(0, d.RowsTaken, "rows");
        AssertFalse(d.Observe(1, 3, 1.02, true, out _, out _), "group 1 is a new baseline");
    }

    private static void FlipAllocatesNothing()
    {
        var d = new FormationFlipDetector();
        for (int g = 0; g < 64; g++) d.Observe(g, 1, 0.0, true, out _, out _);
        for (int g = 0; g < 64; g++) d.Observe(g, 3, 1.0, true, out _, out _);
        long mark = BeginAllocationWindow();
        int flips = 0;
        for (int i = 0; i < 2000; i++)
        {
            int g = i % 64;
            if (d.Observe(g, i % 5, 2.0 + i * 0.001, countable: i % 7 != 0, out _, out _)) flips++;
        }
        AssertEqual(0L, AllocatedSince(mark), "bytes allocated by 2000 observations over 64 known groups");
        AssertTrue(flips > 0, "the loop did flip");
    }
}
