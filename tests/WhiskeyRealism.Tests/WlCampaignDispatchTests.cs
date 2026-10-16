using System;
using System.IO;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

internal static class WlCampaignDispatchTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("wl campaign dispatch issues a fresh order", IssuesFreshOrder),
            ("wl campaign dispatch skips a no-op move", SkipsNoOpMove),
            ("wl campaign dispatch skips a non-finite target", SkipsNonFiniteTarget),
            ("wl campaign dispatch skips the active order duplicate", SkipsActiveDuplicate),
            ("wl campaign dispatch active order of another type does not block", ActiveOrderOfOtherTypeDoesNotBlock),
            ("wl campaign dispatch rate limits the same target within a day", RateLimitsSameTargetWithinDay),
            ("wl campaign dispatch rate limit holds for the same target under another intent", RateLimitHoldsAcrossIntents),
            ("wl campaign dispatch reissues after a game day", ReissuesAfterGameDay),
            ("wl campaign dispatch issues to a new target the same day", IssuesNewTargetSameDay),
            ("wl campaign dispatch rate limits A after B within a day", RateLimitsAlternatingTargets),
            ("wl campaign dispatch history keeps the last four targets", HistoryKeepsLastFour),
            ("wl campaign dispatch issue line reaches the main log", IssueLineReachesMainLog),
        });
    }

    private static readonly GivenOrderView NoOrder = new GivenOrderView(false, -1, 0f, 0f, 0);

    private static void IssuesFreshOrder()
    {
        string reason = WlCampaignDispatchPolicy.Decide(5, 100f, 100f, moveDistance: 300f, history: new WlDispatchHistory(), current: NoOrder, nowDays: 10.0);
        AssertEqual(WlCampaignDispatchPolicy.Issue, reason);
    }

    private static void SkipsNoOpMove()
    {
        string reason = WlCampaignDispatchPolicy.Decide(5, 100f, 100f, moveDistance: 19.9f, history: new WlDispatchHistory(), current: NoOrder, nowDays: 10.0);
        AssertEqual("no-op-move", reason);
    }

    private static void SkipsNonFiniteTarget()
    {
        AssertEqual("invalid-target", WlCampaignDispatchPolicy.Decide(5, float.NaN, 100f, 300f, new WlDispatchHistory(), NoOrder, 10.0));
        AssertEqual("invalid-target", WlCampaignDispatchPolicy.Decide(5, 100f, float.PositiveInfinity, 300f, new WlDispatchHistory(), NoOrder, 10.0));
        AssertEqual("invalid-target", WlCampaignDispatchPolicy.Decide(-1, 100f, 100f, 300f, new WlDispatchHistory(), NoOrder, 10.0));
    }

    private static void SkipsActiveDuplicate()
    {
        var current = new GivenOrderView(true, 7, 150f, 150f, 3);
        string reason = WlCampaignDispatchPolicy.Decide(7, 200f, 200f, 300f, new WlDispatchHistory(), current, 10.0);
        AssertEqual("active-duplicate", reason);
    }

    private static void ActiveOrderOfOtherTypeDoesNotBlock()
    {
        var current = new GivenOrderView(true, 8, 100f, 100f, 3);
        string reason = WlCampaignDispatchPolicy.Decide(7, 100f, 100f, 300f, new WlDispatchHistory(), current, 10.0);
        AssertEqual(WlCampaignDispatchPolicy.Issue, reason);
    }

    private static void RateLimitsSameTargetWithinDay()
    {
        var last = History((100f, 100f, 10.0));
        string reason = WlCampaignDispatchPolicy.Decide(16, 150f, 120f, 300f, last, NoOrder, 10.9);
        AssertEqual("rate-limit", reason);
    }

    private static void RateLimitHoldsAcrossIntents()
    {
        // The same target under another intent (an offensive 16, then its continuation 6) is one order.
        var last = History((100f, 100f, 10.0));
        string reason = WlCampaignDispatchPolicy.Decide(6, 101f, 99f, 300f, last, NoOrder, 10.01);
        AssertEqual("rate-limit", reason);
    }

    private static void ReissuesAfterGameDay()
    {
        var last = History((100f, 100f, 10.0));
        string reason = WlCampaignDispatchPolicy.Decide(16, 100f, 100f, 300f, last, NoOrder, 11.0);
        AssertEqual(WlCampaignDispatchPolicy.Issue, reason);
    }

    private static void IssuesNewTargetSameDay()
    {
        var last = History((100f, 100f, 10.0));
        string reason = WlCampaignDispatchPolicy.Decide(16, 211f, 100f, 300f, last, NoOrder, 10.1);
        AssertEqual(WlCampaignDispatchPolicy.Issue, reason);
    }

    private static WlDispatchHistory History(params (float x, float z, double day)[] emitted)
    {
        var history = new WlDispatchHistory();
        foreach (var e in emitted) history.Record(e.x, e.z, e.day);
        return history;
    }

    private static void RateLimitsAlternatingTargets()
    {
        var history = History((100f, 100f, 10.0), (900f, 900f, 10.1));
        AssertEqual("rate-limit", WlCampaignDispatchPolicy.Decide(5, 100f, 100f, 300f, history, NoOrder, 10.2), "A after B");
        AssertEqual("rate-limit", WlCampaignDispatchPolicy.Decide(5, 900f, 900f, 300f, history, NoOrder, 10.2), "B again");
    }

    private static void HistoryKeepsLastFour()
    {
        var history = History((100f, 100f, 10.0), (300f, 300f, 10.0), (500f, 500f, 10.0), (700f, 700f, 10.0));
        AssertEqual("rate-limit", WlCampaignDispatchPolicy.Decide(5, 100f, 100f, 300f, history, NoOrder, 10.5), "oldest of four still remembered");
        history.Record(900f, 900f, 10.1);
        AssertEqual(WlCampaignDispatchPolicy.Issue, WlCampaignDispatchPolicy.Decide(5, 100f, 100f, 300f, history, NoOrder, 10.5), "fifth target evicts the oldest");
        AssertEqual("rate-limit", WlCampaignDispatchPolicy.Decide(5, 300f, 300f, 300f, history, NoOrder, 10.5), "second oldest kept");
    }

    // The issue line is the smoke proof of the bridge's order: its tag must allow the main log (the
    // campaign-default route is sidecar-only and the default TacticalTuning profile
    // drops the campaign layer), and the runtime must write it through
    // LegacyInfoToMainLogIfAllowed. Skip lines keep the sidecar route.
    private static void IssueLineReachesMainLog()
    {
        TelemetryTagRoute issue = TelemetryTagPolicy.Route("[W&LCampaignDispatch] trigger=whiskey-order type=5 unit=X issuer=Y target=1,2 dest=Objective action=issue");
        AssertTrue(issue.AllowMainLog, "issue line allowed on the main log");
        AssertTrue(issue.RouteToSidecar, "issue line also on the campaign sidecar");
        AssertEqual(TelemetryLayer.Campaign, issue.Layer);
        string runtime = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Strategic/WlCampaignDispatchRuntime.cs"));
        AssertTrue(Regex.IsMatch(runtime, @"TelemetryRouter\.LegacyInfoToMainLogIfAllowed\(\s*""\[W&LCampaignDispatch\] trigger="""),
            "the runtime writes the issue line through LegacyInfoToMainLogIfAllowed");
    }
}
