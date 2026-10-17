using System;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// Smoke 2026-10-17: 18 queued "move-new" orders read relation=ai-chain whoever queued them, so Whiskey's own reserve release was mistaken for vanilla's.
// The executor names the intent's source around the vanilla calls of a winner (OrderIssueOrigin); the observer's AddToOrderQueue row prints it as origin=.
internal static class OrderIssueOriginTests
{
    public static void Run()
    {
        RunAll(new (string name, Action run)[]
        {
            ("order origin: the executor's source is current only while it writes", TheSourceIsCurrentOnlyWhileTheExecutorWrites),
            ("order origin: a nested write restores the outer source and an extra exit leaves none", ANestedWriteRestoresTheOuterSource),
            ("order origin: every intent source has a name", EverySourceHasAName),
            ("order origin: the executor wraps every winner and the observer rows print origin=", TheExecutorAndTheObserverAreWired),
        });
    }

    private static void TheSourceIsCurrentOnlyWhileTheExecutorWrites()
    {
        AssertTrue(OrderIssueOrigin.Current == null, "outside an executor write there is no origin");
        IntentSource outer = OrderIssueOrigin.Enter(IntentSource.ReserveRelease);
        AssertEqual("ReserveRelease", OrderIssueOrigin.Current, "inside, the intent's source");
        OrderIssueOrigin.Exit(outer);
        AssertTrue(OrderIssueOrigin.Current == null, "after, none again");
    }

    private static void ANestedWriteRestoresTheOuterSource()
    {
        IntentSource a = OrderIssueOrigin.Enter(IntentSource.PostureExecutor);
        IntentSource b = OrderIssueOrigin.Enter(IntentSource.ReserveCourierParity);
        AssertEqual("ReserveCourierParity", OrderIssueOrigin.Current, "the inner write");
        OrderIssueOrigin.Exit(b);
        AssertEqual("PostureExecutor", OrderIssueOrigin.Current, "the outer write again");
        OrderIssueOrigin.Exit(a);
        AssertTrue(OrderIssueOrigin.Current == null, "none");
        // An unbalanced exit never leaves the flag stuck on or counts below zero.
        OrderIssueOrigin.Exit(IntentSource.PostureExecutor);
        AssertTrue(OrderIssueOrigin.Current == null, "an extra exit leaves it inactive");
        IntentSource c = OrderIssueOrigin.Enter(IntentSource.Recon);
        AssertEqual("Recon", OrderIssueOrigin.Current, "and it still works after");
        OrderIssueOrigin.Exit(c);
    }

    private static void EverySourceHasAName()
    {
        foreach (IntentSource source in Enum.GetValues(typeof(IntentSource)))
            AssertEqual(source.ToString(), OrderIssueOrigin.NameOf(source), "name of " + source);
        AssertEqual("Unknown", OrderIssueOrigin.NameOf((IntentSource)999), "a value outside the enum");
    }

    private static void TheExecutorAndTheObserverAreWired()
    {
        string runtime = FlankResponseTests.SourceText("Tactical", "Battle", "OrderExecutorRuntime.cs");
        FlankResponseTests.AssertPinned(runtime, "IntentSource outer = OrderIssueOrigin.Enter(winner.Source);", "Execute names the winner's source");
        FlankResponseTests.AssertPinned(runtime, "finally { OrderIssueOrigin.Exit(outer); }", "and always leaves it");
        string observer = FlankResponseTests.SourceText("Patches", "TacticalObserverPatch.cs");
        FlankResponseTests.AssertPinned(observer, "string origin = WhiskeyRealism.Tactical.Battle.OrderIssueOrigin.Current ?? \"other\";", "the observer reads it");
        FlankResponseTests.AssertPinned(observer, "\"[TacticalPlayerOrder] event=queued relation=\" + relation + \" origin=\" + origin", "the player-order row prints origin=");
        FlankResponseTests.AssertPinned(observer, "\"[TacticalCommand] event=queued relation=\" + relation + \" origin=\" + origin", "the command row prints origin=");
    }
}
