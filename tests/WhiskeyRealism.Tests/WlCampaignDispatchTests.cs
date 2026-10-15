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
            ("wl campaign dispatch drops a queued move whose order was cancelled", DropsCancelledQueuedMove),
            ("wl campaign dispatch keeps a queued move whose order still runs", KeepsRunningQueuedMove),
            ("wl campaign dispatch final-position correction is no order", FinalPositionCorrectionIsNoOrder),
            ("wl campaign dispatch roll-up maps to offensive operation", RollUpMapsToOffensiveOperation),
            ("wl campaign dispatch enemy at target is vanilla's num2", EnemyAtTargetIsVanillaNum2),
            ("wl campaign dispatch keeps only the last move of a corps in one pass", KeepsLastMoveOfCorps),
            ("wl campaign dispatch issue line reaches the main log", IssueLineReachesMainLog),
            ("wl campaign dispatch skips the queued execution of a skipped terrain correction", SkipsQueuedCorrectionExecution),
            ("wl campaign dispatch correction memory matches unit and target once", CorrectionMemoryMatchesOnce),
            ("wl campaign dispatch #69 Prefix hands the caller's target to the Postfix", MoveUnitToPrefixWiring),
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

    // #25 cancels the queued DefensiveMovingOrder after MoveUnitTo returned true
    // (13771 -> 14523, RemoveOrder 9949): the corps never marches.
    private static void DropsCancelledQueuedMove()
    {
        var context = new WlMoveContext { Queued = true, QueuedStillRunning = false, InDefensive = true };
        AssertEqual(WlCampaignDispatchPolicy.QueuedOrderCancelled, WlCampaignDispatchPolicy.PendingSkipReason(in context));
    }

    private static void KeepsRunningQueuedMove()
    {
        var running = new WlMoveContext { Queued = true, QueuedStillRunning = true, InDefensive = true };
        AssertEqual(null, WlCampaignDispatchPolicy.PendingSkipReason(in running), "queued and running");
        var direct = new WlMoveContext { Queued = false, QueuedStillRunning = false };
        AssertEqual(null, WlCampaignDispatchPolicy.PendingSkipReason(in direct), "a direct move has no queued order");
    }

    private static void FinalPositionCorrectionIsNoOrder()
    {
        var context = new WlMoveContext { Source = WlMoveSource.FinalPositions, WasInOffensive = true, InOffensive = true };
        AssertEqual(WlCampaignDispatchPolicy.PositionCorrection, WlCampaignDispatchPolicy.PendingSkipReason(in context));
    }

    private static void RollUpMapsToOffensiveOperation()
    {
        var context = new WlMoveContext { Source = WlMoveSource.RollUp };
        AssertEqual(WlCampaignDispatchPolicy.OffensiveOperation, WlCampaignDispatchPolicy.ClassifyType(in context));
        AssertEqual(null, WlCampaignDispatchPolicy.PendingSkipReason(in context), "roll-up is an order");
    }

    private static void EnemyAtTargetIsVanillaNum2()
    {
        AssertTrue(WlCampaignDispatchPolicy.VanillaEnemyAtTarget(500f, 0.8f, 1f, 1.1f, 1f), "enemy, morale and weapons");
        AssertFalse(WlCampaignDispatchPolicy.VanillaEnemyAtTarget(500f, 0f, 1f, 1.1f, 1f), "zero morale makes num2 zero");
        AssertFalse(WlCampaignDispatchPolicy.VanillaEnemyAtTarget(500f, 0.8f, 1f, 0f, 1f), "zero weapon factor makes num2 zero");
        AssertFalse(WlCampaignDispatchPolicy.VanillaEnemyAtTarget(0f, 0.8f, 1f, 1.1f, 1f), "no enemy");
        AssertTrue(WlCampaignDispatchPolicy.VanillaEnemyAtTarget(500f, 0.8f, 1f, 1.1f, 0f), "num2 / 0 is +inf: vanilla's num2 <= 0 is false");
    }

    private static void KeepsLastMoveOfCorps()
    {
        int[] ids = { 7, 9, 7, 0 };
        AssertFalse(WlCampaignDispatchPolicy.IsLastMoveOfUnit(ids, 3, 0), "first move of 7 superseded");
        AssertTrue(WlCampaignDispatchPolicy.IsLastMoveOfUnit(ids, 3, 1), "only move of 9");
        AssertTrue(WlCampaignDispatchPolicy.IsLastMoveOfUnit(ids, 3, 2), "last move of 7");
    }

    // The issue line is the #69 smoke proof: its tag must allow the main log (the
    // campaign-default route is sidecar-only and the default TacticalTuning profile
    // drops the campaign layer), and the runtime must write it through
    // LegacyInfoToMainLogIfAllowed. Skip lines keep the sidecar route.
    private static void IssueLineReachesMainLog()
    {
        TelemetryTagRoute issue = TelemetryTagPolicy.Route("[W&LCampaignDispatch] trigger=vanilla-move type=5 unit=X issuer=Y target=1,2 dest=Objective zone=shown action=issue");
        AssertTrue(issue.AllowMainLog, "issue line allowed on the main log");
        AssertTrue(issue.RouteToSidecar, "issue line also on the campaign sidecar");
        AssertEqual(TelemetryLayer.Campaign, issue.Layer);
        string runtime = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Strategic/WlCampaignDispatchRuntime.cs"));
        AssertTrue(Regex.IsMatch(runtime, @"TelemetryRouter\.LegacyInfoToMainLogIfAllowed\(\s*""\[W&LCampaignDispatch\] trigger="""),
            "the runtime writes the issue line through LegacyInfoToMainLogIfAllowed");
    }

    // n2: CheckFinalPositions queues its move (17928); UpdateOrders re-enters
    // MoveUnitTo for it as a direct move (10048) with no source mark.
    private static void SkipsQueuedCorrectionExecution()
    {
        var memory = new WlCorrectionMemory();
        var first = new WlMoveContext { Source = WlMoveSource.FinalPositions, Queued = true, QueuedStillRunning = true };
        AssertEqual(WlCampaignDispatchPolicy.PositionCorrection, WlCampaignDispatchPolicy.PendingSkipReason(in first));
        memory.Record(unit: 7, x: 500f, z: 500f);

        var execution = new WlMoveContext
        {
            Source = WlMoveSource.Other,
            Queued = false,
            ExecutesSkippedCorrection = memory.ConsumeExecution(7, 512f, 497f), // re-stacked destination
        };
        AssertEqual(WlCampaignDispatchPolicy.PositionCorrection, WlCampaignDispatchPolicy.PendingSkipReason(in execution));

        var later = new WlMoveContext { Source = WlMoveSource.Other, ExecutesSkippedCorrection = memory.ConsumeExecution(7, 512f, 497f) };
        AssertEqual(null, WlCampaignDispatchPolicy.PendingSkipReason(in later), "a later move to the same spot is an order again");
    }

    private static void CorrectionMemoryMatchesOnce()
    {
        var memory = new WlCorrectionMemory();
        memory.Record(7, 0f, 0f);
        AssertFalse(memory.ConsumeExecution(8, 0f, 0f), "another unit");
        AssertFalse(memory.ConsumeExecution(7, 200f, 0f), "another target");
        AssertTrue(memory.ConsumeExecution(7, 50f, 0f), "same unit, same target");
        AssertFalse(memory.ConsumeExecution(7, 50f, 0f), "consumed");
        for (int i = 0; i < WlCorrectionMemory.Capacity + 1; i++) memory.Record(100 + i, 0f, 0f);
        AssertFalse(memory.ConsumeExecution(100, 0f, 0f), "oldest overwritten");
        AssertTrue(memory.ConsumeExecution(100 + WlCorrectionMemory.Capacity, 0f, 0f), "newest kept");
        memory.Clear();
        AssertFalse(memory.ConsumeExecution(101, 0f, 0f), "cleared");
    }

    // R16 wiring: vanilla reassigns `position` (14498) before the Postfix reads it,
    // so the Prefix must capture it and the Postfix must use the capture.
    private static void MoveUnitToPrefixWiring()
    {
        string patch = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Patches/WlCampaignMoveDispatchPatch.cs"));
        AssertTrue(Regex.IsMatch(patch, @"Prefix\(Vector3 position, out Vector3 __state\)[\s\S]*?__state = position;"), "the Prefix captures the caller's target");
        AssertTrue(patch.Contains("WlCampaignDispatchRuntime.OnMoveUnitTo(unit, __state, movearoundenemyunits, __result)"), "the Postfix records the captured target");
    }
}
