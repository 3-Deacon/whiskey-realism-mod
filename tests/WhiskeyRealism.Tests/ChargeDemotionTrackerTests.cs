using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Battle.Producers;
using static TestHarness;

// Charge demotion telemetry (battle-core plan § BC7 smoke 2.8): the ChargeDemotion
// producer counts a demotion only once the executor has issued it, read back on the
// next tick from the executor's last Stance outcome.
internal static class ChargeDemotionTrackerTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("charge-demotion tracker NothingConfirmedBeforeTheExecutorIssues", NothingConfirmedBeforeTheExecutorIssues),
            ("charge-demotion tracker IssuedDemotionIsConfirmedOnce", IssuedDemotionIsConfirmedOnce),
            ("charge-demotion tracker EarlierIssueDoesNotConfirmALaterSubmission", EarlierIssueDoesNotConfirmALaterSubmission),
            ("charge-demotion tracker AnotherSourceOnTheStanceChannelIsNotADemotion", AnotherSourceOnTheStanceChannelIsNotADemotion),
            ("charge-demotion tracker OnlyChargeDemotionStanceIntentsAreTracked", OnlyChargeDemotionStanceIntentsAreTracked),
            ("charge-demotion tracker WindowIsTimeBucketed", WindowIsTimeBucketed),
            ("charge-demotion tracker ShortBufferStillCountsEveryDemotion", ShortBufferStillCountsEveryDemotion),
            ("charge-demotion tracker ClearForgetsTheBattle", ClearForgetsTheBattle),
        });
    }

    private const float Period = 30f;

    private static BattleOrderIntent Demote(int groupId, int stance)
    {
        return new BattleOrderIntent
        {
            GroupId = groupId,
            Channel = OrderChannel.Stance,
            Value = stance,
            Heading = -1f,
            Priority = IntentPriority.Safety,
            Source = IntentSource.ChargeDemotion,
            Reason = "charge-demotion",
        };
    }

    private static void Issue(OrderExecutor executor, in BattleOrderIntent intent, double hours)
    {
        executor.RecordIssued(intent, hours, null, 0, null, 0, 0, 0);
    }

    private static void NothingConfirmedBeforeTheExecutorIssues()
    {
        var t = new ChargeDemotionTracker();
        var executor = new OrderExecutor();
        var buffer = new ChargeDemotionTracker.Demotion[4];
        t.Submitted(Demote(10, 2), 1.0);
        AssertEqual(0, t.Confirm(executor, buffer), "no outcome, no demotion");
        AssertEqual(0, t.Total, "total");
        AssertFalse(t.TryTakeWindow(0f, Period, out _, out _), "empty window emits nothing");
    }

    private static void IssuedDemotionIsConfirmedOnce()
    {
        var t = new ChargeDemotionTracker();
        var executor = new OrderExecutor();
        var buffer = new ChargeDemotionTracker.Demotion[4];
        BattleOrderIntent intent = Demote(10, 2);
        t.Submitted(intent, 1.0);
        Issue(executor, intent, 1.0);

        AssertEqual(1, t.Confirm(executor, buffer), "issued demotion confirmed");
        AssertEqual(10, buffer[0].GroupId, "group");
        AssertEqual(2, buffer[0].Stance, "stance");
        AssertEqual(1.0, buffer[0].SubmittedAtHours, "submitted at");
        AssertEqual(1, t.Total, "total");
        AssertEqual(1, t.Window, "window");

        // Pending is consumed: the same outcome is not counted again next tick.
        AssertEqual(0, t.Confirm(executor, buffer), "counted once");
        AssertEqual(1, t.Total, "total unchanged");
    }

    // Vanilla re-escalates the group; the executor dedups or refuses the new
    // demotion, so its last Issued outcome is still the earlier one.
    private static void EarlierIssueDoesNotConfirmALaterSubmission()
    {
        var t = new ChargeDemotionTracker();
        var executor = new OrderExecutor();
        var buffer = new ChargeDemotionTracker.Demotion[4];
        BattleOrderIntent intent = Demote(10, 2);
        t.Submitted(intent, 1.0);
        Issue(executor, intent, 1.0);
        t.Confirm(executor, buffer);

        t.Submitted(intent, 1.25);
        AssertEqual(0, t.Confirm(executor, buffer), "stale issue");
        AssertEqual(1, t.Total, "total");

        t.Submitted(intent, 1.5);
        Issue(executor, intent, 1.5);
        AssertEqual(1, t.Confirm(executor, buffer), "second demotion issued");
        AssertEqual(2, t.Total, "total");
    }

    private static void AnotherSourceOnTheStanceChannelIsNotADemotion()
    {
        var t = new ChargeDemotionTracker();
        var executor = new OrderExecutor();
        var buffer = new ChargeDemotionTracker.Demotion[4];
        t.Submitted(Demote(10, 2), 1.0);
        BattleOrderIntent posture = Demote(10, 4);
        posture.Source = IntentSource.PostureExecutor;
        posture.Priority = IntentPriority.Executor;
        Issue(executor, posture, 1.0);
        AssertEqual(0, t.Confirm(executor, buffer), "posture stance is not a demotion");
        AssertEqual(0, t.Total, "total");
    }

    private static void OnlyChargeDemotionStanceIntentsAreTracked()
    {
        var t = new ChargeDemotionTracker();
        var executor = new OrderExecutor();
        var buffer = new ChargeDemotionTracker.Demotion[4];
        BattleOrderIntent other = Demote(10, 2);
        other.Source = IntentSource.PostureExecutor;
        BattleOrderIntent move = Demote(11, 2);
        move.Channel = OrderChannel.Move;
        t.Submitted(other, 1.0);
        t.Submitted(move, 1.0);
        Issue(executor, other, 1.0);
        Issue(executor, move, 1.0);
        AssertEqual(0, t.Confirm(executor, buffer), "not tracked");
    }

    private static void WindowIsTimeBucketed()
    {
        var t = new ChargeDemotionTracker();
        var executor = new OrderExecutor();
        var buffer = new ChargeDemotionTracker.Demotion[4];
        BattleOrderIntent a = Demote(10, 2);
        t.Submitted(a, 1.0);
        Issue(executor, a, 1.0);
        t.Confirm(executor, buffer);

        AssertTrue(t.TryTakeWindow(5f, Period, out int window, out int total), "first demotion reports at once");
        AssertEqual(1, window, "window");
        AssertEqual(1, total, "total");
        AssertEqual(0, t.Window, "window taken");

        BattleOrderIntent b = Demote(11, 1);
        t.Submitted(b, 1.1);
        Issue(executor, b, 1.1);
        t.Confirm(executor, buffer);
        AssertFalse(t.TryTakeWindow(20f, Period, out _, out _), "inside the bucket");
        AssertTrue(t.TryTakeWindow(35f, Period, out window, out total), "bucket elapsed");
        AssertEqual(1, window, "window");
        AssertEqual(2, total, "total");
        AssertFalse(t.TryTakeWindow(90f, Period, out _, out _), "nothing new, nothing emitted");
    }

    private static void ShortBufferStillCountsEveryDemotion()
    {
        var t = new ChargeDemotionTracker();
        var executor = new OrderExecutor();
        var buffer = new ChargeDemotionTracker.Demotion[1];
        for (int id = 10; id < 13; id++)
        {
            BattleOrderIntent intent = Demote(id, 2);
            t.Submitted(intent, 1.0);
            Issue(executor, intent, 1.0);
        }
        AssertEqual(1, t.Confirm(executor, buffer), "writes what the buffer holds");
        AssertEqual(3, t.Total, "counts all three");
        AssertEqual(3, t.Window, "window");
    }

    private static void ClearForgetsTheBattle()
    {
        var t = new ChargeDemotionTracker();
        var executor = new OrderExecutor();
        var buffer = new ChargeDemotionTracker.Demotion[4];
        BattleOrderIntent a = Demote(10, 2);
        t.Submitted(a, 1.0);
        Issue(executor, a, 1.0);
        t.Confirm(executor, buffer);
        t.TryTakeWindow(5f, Period, out _, out _);
        t.Submitted(a, 1.2);
        t.Clear();

        AssertEqual(0, t.Total, "total");
        AssertEqual(0, t.Window, "window");
        Issue(executor, a, 1.2);
        AssertEqual(0, t.Confirm(executor, buffer), "pending forgotten");

        // The bucket restarts too: a new battle's first demotion reports at once.
        BattleOrderIntent b = Demote(11, 2);
        t.Submitted(b, 0.1);
        Issue(executor, b, 0.1);
        t.Confirm(executor, buffer);
        AssertTrue(t.TryTakeWindow(6f, Period, out _, out _), "first demotion of the next battle");
    }
}
