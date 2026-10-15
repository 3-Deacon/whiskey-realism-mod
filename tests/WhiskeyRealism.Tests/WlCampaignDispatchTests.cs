using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.PlayerOrders;

using static TestHarness;

internal static class WlCampaignDispatchTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("wl campaign dispatch triggers for subordinate player's campaign group", TriggersForSubordinateCampaignGroup),
            ("wl campaign dispatch trigger refuses each failed precondition", TriggerRefusesEachFailedPrecondition),
            ("wl campaign dispatch engage wins over every other list", EngageWinsOverOtherLists),
            ("wl campaign dispatch capital defense maps to type 8", CapitalDefenseMapsToType8),
            ("wl campaign dispatch new offensive maps to 16 or 5 by enemy at target", NewOffensiveMapsBySightedEnemy),
            ("wl campaign dispatch continuing offensive maps to type 6", ContinuingOffensiveMapsToType6),
            ("wl campaign dispatch other moves map to redeploy", OtherMovesMapToRedeploy),
            ("wl campaign dispatch issues a fresh order", IssuesFreshOrder),
            ("wl campaign dispatch skips a no-op move", SkipsNoOpMove),
            ("wl campaign dispatch skips a non-finite target", SkipsNonFiniteTarget),
            ("wl campaign dispatch skips the active order duplicate", SkipsActiveDuplicate),
            ("wl campaign dispatch active order of another type does not block", ActiveOrderOfOtherTypeDoesNotBlock),
            ("wl campaign dispatch rate limits the same target within a day", RateLimitsSameTargetWithinDay),
            ("wl campaign dispatch absorbs the queued second call of another type", AbsorbsQueuedSecondCall),
            ("wl campaign dispatch reissues after a game day", ReissuesAfterGameDay),
            ("wl campaign dispatch issues to a new target the same day", IssuesNewTargetSameDay),
            ("wl campaign dispatch rate limits A after B within a day", RateLimitsAlternatingTargets),
            ("wl campaign dispatch history keeps the last four targets", HistoryKeepsLastFour),
            ("wl campaign dispatch subordinate orders keep vanilla zone and prestige", SubordinateOrdersKeepVanillaZone),
        });
    }

    private static WlMoveFacts Subordinate()
    {
        return new WlMoveFacts
        {
            MoveSucceeded = true,
            WlActive = true,
            HasCommand = true,
            PlayerIsCic = false,
            PlayerIsOwnCampaignGroup = false,
            MovedIsPlayerCampaignGroup = true,
            MovedIsPlayerOwned = false,
            MovedByPlayer = false,
            MovedIsPlayerAlliance = true,
        };
    }

    private static void TriggersForSubordinateCampaignGroup()
    {
        var facts = Subordinate();
        AssertTrue(WlCampaignDispatchPolicy.IsTrigger(in facts, out string reason), "subordinate trigger");
        AssertEqual("trigger", reason);
    }

    private static void TriggerRefusesEachFailedPrecondition()
    {
        AssertRefused(f => f.MoveSucceeded = false, "move-failed");
        AssertRefused(f => f.WlActive = false, "wl-inactive");
        AssertRefused(f => f.HasCommand = false, "no-command");
        AssertRefused(f => f.PlayerIsCic = true, "player-cic");
        AssertRefused(f => f.PlayerIsOwnCampaignGroup = true, "player-top-level");
        AssertRefused(f => f.MovedIsPlayerCampaignGroup = false, "not-player-campaign-group");
        AssertRefused(f => f.MovedIsPlayerOwned = true, "player-owned");
        AssertRefused(f => f.MovedByPlayer = true, "moved-by-player");
        AssertRefused(f => f.MovedIsPlayerAlliance = false, "other-alliance");
    }

    private static void AssertRefused(Action<Box> mutate, string expectedReason)
    {
        var box = new Box { Facts = Subordinate() };
        mutate(box);
        var facts = box.Facts;
        AssertFalse(WlCampaignDispatchPolicy.IsTrigger(in facts, out string reason), expectedReason);
        AssertEqual(expectedReason, reason);
    }

    private sealed class Box
    {
        public WlMoveFacts Facts;
        public bool MoveSucceeded { set { Facts.MoveSucceeded = value; } }
        public bool WlActive { set { Facts.WlActive = value; } }
        public bool HasCommand { set { Facts.HasCommand = value; } }
        public bool PlayerIsCic { set { Facts.PlayerIsCic = value; } }
        public bool PlayerIsOwnCampaignGroup { set { Facts.PlayerIsOwnCampaignGroup = value; } }
        public bool MovedIsPlayerCampaignGroup { set { Facts.MovedIsPlayerCampaignGroup = value; } }
        public bool MovedIsPlayerOwned { set { Facts.MovedIsPlayerOwned = value; } }
        public bool MovedByPlayer { set { Facts.MovedByPlayer = value; } }
        public bool MovedIsPlayerAlliance { set { Facts.MovedIsPlayerAlliance = value; } }
    }

    private static void EngageWinsOverOtherLists()
    {
        // The defensive attack branch removes the unit from the offensive list and
        // adds it to the defensive one (13773-13774); a stale offensive entry must
        // not turn an engage order into an offensive one.
        var context = new WlMoveContext { WasInOffensive = true, InOffensive = true, InDefensive = true, InCapitalDefense = true, EnemyAtTarget = true };
        AssertEqual(WlCampaignDispatchPolicy.Engage, WlCampaignDispatchPolicy.ClassifyType(in context));
    }

    private static void CapitalDefenseMapsToType8()
    {
        var context = new WlMoveContext { InCapitalDefense = true, InOffensive = true };
        AssertEqual(WlCampaignDispatchPolicy.DefendCapital, WlCampaignDispatchPolicy.ClassifyType(in context));
    }

    private static void NewOffensiveMapsBySightedEnemy()
    {
        var withEnemy = new WlMoveContext { InOffensive = true, EnemyAtTarget = true };
        var empty = new WlMoveContext { InOffensive = true, EnemyAtTarget = false };
        AssertEqual(WlCampaignDispatchPolicy.Offensive, WlCampaignDispatchPolicy.ClassifyType(in withEnemy));
        AssertEqual(WlCampaignDispatchPolicy.Redeploy, WlCampaignDispatchPolicy.ClassifyType(in empty));
    }

    private static void ContinuingOffensiveMapsToType6()
    {
        var context = new WlMoveContext { WasInOffensive = true, InOffensive = true, EnemyAtTarget = true };
        AssertEqual(WlCampaignDispatchPolicy.OffensiveOperation, WlCampaignDispatchPolicy.ClassifyType(in context));
    }

    private static void OtherMovesMapToRedeploy()
    {
        var context = new WlMoveContext { EnemyAtTarget = true };
        AssertEqual(WlCampaignDispatchPolicy.Redeploy, WlCampaignDispatchPolicy.ClassifyType(in context));
        // Dropped from the offensive during the same call (14038-14040) is not an offensive.
        var dropped = new WlMoveContext { WasInOffensive = true };
        AssertEqual(WlCampaignDispatchPolicy.Redeploy, WlCampaignDispatchPolicy.ClassifyType(in dropped));
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

    private static void AbsorbsQueuedSecondCall()
    {
        // A queued move re-enters MoveUnitTo when it executes (10048); by then the
        // unit is listed in the offensive, so it classifies as 6, not 16.
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

    private static void SubordinateOrdersKeepVanillaZone()
    {
        // User decision 2026-10-15: subordinate dispatches pay vanilla's arrival prestige
        // (zone shown, CheckDLCWLTopics 123484-123517 unchanged).
        AssertTrue(WlCampaignDispatchPolicy.ShowsZone(subordinateDispatch: true), "subordinate: vanilla zone");
        AssertTrue(WlCampaignDispatchPolicy.ShowsZone(subordinateDispatch: false), "top-level player decides: zone");
    }
}
