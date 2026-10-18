using System;
using WhiskeyRealism.Tactical.Battle;

using static TestHarness;

// What the executor remembers about its W&L dispatches (2026-10-17 smoke, order 13: an interrupted path re-issued for the
// 4th Division as FormUp reached the panel as "Deploy to engage"; the division's task flipped every 18-36 s).
internal static class PlayerDispatchMemoryTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("dispatch memory a target within 110 m of the last is a repeat", TargetWithin110OfTheLastIsARepeat),
            ("dispatch memory is per group", MemoryIsPerGroup),
            ("dispatch memory forget makes the same place new", ForgetMakesTheSamePlaceNew),
            ("dispatch memory a task holds from when it began", ATaskHoldsFromWhenItBegan),
            ("dispatch memory another task starts the clock again", AnotherTaskStartsTheClockAgain),
            ("dispatch memory no task is ignored", NoTaskIsIgnored),
            ("dispatch memory a task never noted does not hold", ATaskNeverNotedDoesNotHold),
            ("dispatch memory flipping task never holds for the older one", FlippingTaskNeverHoldsForTheOlderOne),
            ("dispatch memory another order taking the panel ends the repeat", AnotherOrderTakingThePanelEndsTheRepeat),
            ("dispatch memory the accepted dispatch keeps its own target", TheAcceptedDispatchKeepsItsOwnTarget),
            ("dispatch memory a session already noted changes nothing", ASessionAlreadyNotedChangesNothing),
            ("dispatch memory steady state allocates nothing", SteadyStateAllocatesNothing),
        });
    }

    private static void TargetWithin110OfTheLastIsARepeat()
    {
        var m = new PlayerDispatchMemory();
        AssertFalse(m.IsRepeat(7, 0f, 0f), "nothing dispatched yet");
        m.RecordDispatch(7, 1000f, 500f);
        AssertTrue(m.IsRepeat(7, 1000f, 500f), "the same point");
        AssertTrue(m.IsRepeat(7, 1110f, 500f), "110 m is inclusive");
        AssertFalse(m.IsRepeat(7, 1110.5f, 500f), "just past it is a new place");
        AssertTrue(m.IsRepeat(7, 1077f, 577f), "diagonal 108.9 m is near");
        AssertFalse(m.IsRepeat(7, 1078f, 578f), "diagonal 110.3 m is not");
    }

    private static void MemoryIsPerGroup()
    {
        var m = new PlayerDispatchMemory();
        m.RecordDispatch(7, 0f, 0f);
        AssertFalse(m.IsRepeat(8, 0f, 0f), "another group has no record");
    }

    private static void ForgetMakesTheSamePlaceNew()
    {
        var m = new PlayerDispatchMemory();
        m.RecordDispatch(7, 0f, 0f);
        m.ForgetDispatch(7);
        AssertFalse(m.IsRepeat(7, 0f, 0f), "a dispatch that never reached the panel does not block the next");
    }

    private static void ATaskHoldsFromWhenItBegan()
    {
        var m = new PlayerDispatchMemory();
        m.NoteTask(7, task: 4, nowHours: 10.0);
        m.NoteTask(7, task: 4, nowHours: 10.5);
        AssertTrue(m.TaskHolds(7, 4, 0.02, out double at), "same task");
        AssertNear(10.02f, (float)at, 1e-4f, "held from when it began, not from the latest write");
    }

    private static void AnotherTaskStartsTheClockAgain()
    {
        var m = new PlayerDispatchMemory();
        m.NoteTask(7, 4, 10.0);
        m.NoteTask(7, 9, 10.3);
        AssertFalse(m.TaskHolds(7, 4, 0.02, out _), "the group has been given another task");
        AssertTrue(m.TaskHolds(7, 9, 0.02, out double at), "the new one");
        AssertNear(10.32f, (float)at, 1e-4f, "its clock began at 10.3");
    }

    private static void NoTaskIsIgnored()
    {
        var m = new PlayerDispatchMemory();
        m.NoteTask(7, 4, 10.0);
        m.NoteTask(7, 0, 10.4);
        AssertTrue(m.TaskHolds(7, 4, 0.02, out double at) && Math.Abs(at - 10.02) < 1e-9, "a write with no task does not change the task");
    }

    private static void ATaskNeverNotedDoesNotHold()
    {
        var m = new PlayerDispatchMemory();
        AssertFalse(m.TaskHolds(7, 4, 0.02, out _), "no write of the group was ever seen");
    }

    private static void FlippingTaskNeverHoldsForTheOlderOne()
    {
        // The smoke: FormUp, Hold, FormUp... every 18-36 s. A dispatch made for the first FormUp is stale by the second.
        var m = new PlayerDispatchMemory();
        m.NoteTask(7, 3, 10.00);
        m.NoteTask(7, 5, 10.01);
        m.NoteTask(7, 3, 10.02);
        AssertTrue(m.TaskHolds(7, 3, 0.02, out double at), "FormUp is the task again");
        AssertNear(10.04f, (float)at, 1e-4f, "but its clock restarted at the third write: the dispatch of the first FormUp waits, and a later flip discards it");
    }

    // Verify round 1, major 2: an executor order to A, a vanilla order replaces it on the panel, then ContestReissue to A: the reissue is a
    // new order for the player, not a repeat.
    private static void AnotherOrderTakingThePanelEndsTheRepeat()
    {
        var m = new PlayerDispatchMemory();
        m.NotePanel(4, 0);
        m.RecordDispatch(7, 1000f, 0f);
        m.NotePanel(5, 7);
        AssertTrue(m.IsRepeat(7, 1000f, 0f), "the executor's own order is on the panel");
        m.NotePanel(6, 0);
        AssertFalse(m.IsRepeat(7, 1000f, 0f), "vanilla's order took the panel: the same place again is a new order");
    }

    private static void TheAcceptedDispatchKeepsItsOwnTarget()
    {
        var m = new PlayerDispatchMemory();
        m.NotePanel(4, 0);
        m.RecordDispatch(7, 1000f, 0f);
        m.RecordDispatch(8, 5000f, 0f);
        m.NotePanel(5, 7);
        AssertTrue(m.IsRepeat(7, 1000f, 0f), "group 7's dispatch opened the session");
        AssertFalse(m.IsRepeat(8, 5000f, 0f), "group 8's earlier dispatch is no longer on the panel");
    }

    private static void ASessionAlreadyNotedChangesNothing()
    {
        var m = new PlayerDispatchMemory();
        m.NotePanel(4, 0);
        m.RecordDispatch(7, 1000f, 0f);
        m.NotePanel(5, 7);
        m.NotePanel(5, 0);
        AssertTrue(m.IsRepeat(7, 1000f, 0f), "the next pass sees the same session: the accepted dispatch stays remembered");
        m.Clear();
        m.RecordDispatch(7, 1000f, 0f);
        m.NotePanel(5, 0);
        AssertFalse(m.IsRepeat(7, 1000f, 0f), "a cleared memory notes the panel afresh");
    }

    private static void SteadyStateAllocatesNothing()
    {
        var m = new PlayerDispatchMemory();
        m.RecordDispatch(7, 0f, 0f);
        m.NoteTask(7, 4, 10.0);
        long mark = TestFixtures.BeginAllocationWindow();
        for (int i = 0; i < 1000; i++)
        {
            m.NoteTask(7, 4, 10.0 + i);
            m.IsRepeat(7, 5f, 5f);
            m.TaskHolds(7, 4, 0.02, out _);
            m.RecordDispatch(7, i, 0f);
        }
        AssertEqual(0L, TestFixtures.AllocatedSince(mark), "repeat tests, task notes and re-records of known groups allocate nothing");
    }
}
