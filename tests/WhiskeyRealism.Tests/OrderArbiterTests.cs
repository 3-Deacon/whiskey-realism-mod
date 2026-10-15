using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;
using VanillaMapBounds = WhiskeyRealism.Tactical.Battle.VanillaMapBounds;

// BC4: OrderArbiter, OrderExecutor pipeline, WaypointValidator, SilentExitPrecheck
// and the doctrine-clock stance record (docs/superpowers/plans/2026-10-15-battle-core.md § BC4).
internal static class OrderArbiterTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("battle-core HigherPriorityWinsPerChannel", HigherPriorityWinsPerChannel),
            ("battle-core ChannelsResolveIndependently", ChannelsResolveIndependently),
            ("battle-core TieKeepsPreviousWinner", TieKeepsPreviousWinner),
            ("battle-core StanceBeforeMoveBeforeFormationOrder", StanceBeforeMoveBeforeFormationOrder),
            ("battle-core InlineIntentRefusedUnderHigherClaim", InlineIntentRefusedUnderHigherClaim),
            ("battle-core ArbiterResolveAllocatesNothingAfterWarmup", ArbiterResolveAllocatesNothingAfterWarmup),
            ("battle-core ArbiterForAllianceBoundsChecked", ArbiterForAllianceBoundsChecked),
            ("battle-core DedupeWithin60m", DedupeWithin60m),
            ("battle-core SafetyBypassesRecentOrderButNotDedupe", SafetyBypassesRecentOrderButNotDedupe),
            ("battle-core TaskHoldBlocksSameTaskOnly", TaskHoldBlocksSameTaskOnly),
            ("battle-core ClampMatchesVanillaBoundsNegativeZ", ClampMatchesVanillaBoundsNegativeZ),
            ("battle-core WaterTargetStepsBack", WaterTargetStepsBack),
            ("battle-core WaterTargetRejectedAfterFourSteps", WaterTargetRejectedAfterFourSteps),
            ("battle-core FriendlyFootprintRejectedUnlessRelief", FriendlyFootprintRejectedUnlessRelief),
            ("battle-core FriendlyFireLineRejectsArtilleryPosition", FriendlyFireLineRejectsArtilleryPosition),
            ("battle-core PointBlankRejectsArtilleryTarget", PointBlankRejectsArtilleryTarget),
            ("battle-core PrecheckSkipsRoutedOnRetreatGarrison", PrecheckSkipsRoutedOnRetreatGarrison),
            ("battle-core QueuedOrderIsPendingNotFailed", QueuedOrderIsPendingNotFailed),
            ("battle-core PendingTimesOutToFailed", PendingTimesOutToFailed),
            ("battle-core FailedRetriesOnceRotatedThenGivesUp", FailedRetriesOnceRotatedThenGivesUp),
            ("battle-core RedirectOfMovingGroupIsMoved", RedirectOfMovingGroupIsMoved),
            ("battle-core StaleWaypointNearTargetIsNotATake", StaleWaypointNearTargetIsNotATake),
            ("battle-core PlayerOwnedNeverExecuted", PlayerOwnedNeverExecuted),
            ("battle-core FeudNeverExecuted", FeudNeverExecuted),
            ("battle-core GuardRefusalSkipsWithGuardReason", GuardRefusalSkipsWithGuardReason),
            ("battle-core SelfOnlyChainFailsClosedForPlayerAncestor", SelfOnlyChainFailsClosedForPlayerAncestor),
            ("battle-core AncestorFanOutDispatchesOnce", AncestorFanOutDispatchesOnce),
            ("battle-core StanceRecordUsesDoctrineClock", StanceRecordUsesDoctrineClock),
            ("battle-core StanceSourceMapsIntentLadder", StanceSourceMapsIntentLadder),
            ("battle-core RetryRunsOnlyWhileItsSourceStillClaims", RetryRunsOnlyWhileItsSourceStillClaims),
        });
    }

    // ------------------------------------------------------------------ arbiter

    private static void HigherPriorityWinsPerChannel()
    {
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(Move(1, 100f, -100f, IntentPriority.Play, IntentSource.Play));
        arbiter.Submit(Move(1, 200f, -200f, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Submit(Move(1, 300f, -300f, IntentPriority.Scorer, IntentSource.GroupStanceScorer));
        var winners = new BattleOrderIntent[8];
        int n = arbiter.Resolve(winners);
        AssertEqual(1, n, "one (group, channel) claim");
        AssertEqual(IntentPriority.Executor, winners[0].Priority, "Executor beats Play and Scorer");
        AssertNear(200f, winners[0].TargetX, 0.001f, "winning target");
        AssertTrue(arbiter.TryGetClaim(1, OrderChannel.Move, out BattleOrderIntent claim), "claim published");
        AssertEqual(IntentSource.PostureExecutor, claim.Source, "published claim source");
        AssertFalse(arbiter.TryGetClaim(1, OrderChannel.Stance, out _), "no stance claim");
    }

    private static void ChannelsResolveIndependently()
    {
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(Move(1, 100f, -100f, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Submit(Stance(1, 2, IntentPriority.Scorer, IntentSource.GroupStanceScorer));
        arbiter.Submit(Move(2, 400f, -400f, IntentPriority.Play, IntentSource.Play));
        var winners = new BattleOrderIntent[8];
        int n = arbiter.Resolve(winners);
        AssertEqual(3, n, "group 1 move + group 1 stance + group 2 move");
        AssertTrue(arbiter.TryGetClaim(1, OrderChannel.Stance, out BattleOrderIntent stance), "stance claim");
        AssertEqual(IntentPriority.Scorer, stance.Priority, "a Scorer stance survives an Executor move");
        AssertTrue(arbiter.TryGetClaim(2, OrderChannel.Move, out BattleOrderIntent move2), "group 2 move claim");
        AssertEqual(IntentSource.Play, move2.Source, "group 2 move source");
    }

    private static void TieKeepsPreviousWinner()
    {
        var arbiter = new OrderArbiter();
        var winners = new BattleOrderIntent[8];

        arbiter.BeginTick();
        arbiter.Submit(Move(1, 100f, -100f, IntentPriority.Safety, IntentSource.Withdrawal));
        arbiter.Submit(Move(1, 200f, -200f, IntentPriority.Safety, IntentSource.ArtilleryPointBlank));
        arbiter.Resolve(winners);
        AssertEqual(IntentSource.Withdrawal, winners[0].Source, "no history: first submitted wins a tie");

        arbiter.BeginTick();
        arbiter.Submit(Move(1, 200f, -200f, IntentPriority.Safety, IntentSource.ArtilleryPointBlank));
        arbiter.Submit(Move(1, 100f, -100f, IntentPriority.Safety, IntentSource.Withdrawal));
        arbiter.Resolve(winners);
        AssertEqual(IntentSource.Withdrawal, winners[0].Source, "tie keeps the previous tick's source");

        arbiter.BeginTick();
        arbiter.Submit(Move(1, 200f, -200f, IntentPriority.Safety, IntentSource.ArtilleryPointBlank));
        arbiter.Submit(Move(1, 100f, -100f, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Resolve(winners);
        AssertEqual(IntentSource.ArtilleryPointBlank, winners[0].Source, "hysteresis never beats a higher priority");
    }

    private static void StanceBeforeMoveBeforeFormationOrder()
    {
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(Formation(1, 1, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Submit(Move(1, 100f, -100f, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Submit(Behavior(1, 2, IntentPriority.Executor, IntentSource.FireControl));
        arbiter.Submit(Stance(1, 3, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Submit(Facing(1, 90f, IntentPriority.Scorer, IntentSource.FacingPulse));
        var winners = new BattleOrderIntent[8];
        int n = arbiter.Resolve(winners);
        AssertEqual(5, n, "five channels");
        AssertEqual(OrderChannel.Stance, winners[0].Channel, "stance first (#61 L768-774)");
        AssertEqual(OrderChannel.Move, winners[1].Channel, "move before formation (#61 L976-988)");
        AssertEqual(OrderChannel.Formation, winners[2].Channel, "formation third");
        AssertEqual(OrderChannel.Facing, winners[3].Channel, "facing after formation");
        AssertEqual(OrderChannel.CombatBehavior, winners[4].Channel, "combat behavior last");
    }

    private static void InlineIntentRefusedUnderHigherClaim()
    {
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(Move(7, 100f, -100f, IntentPriority.Safety, IntentSource.Withdrawal));
        arbiter.Resolve(new BattleOrderIntent[4]);

        var parity = Move(7, 300f, -300f, IntentPriority.Support, IntentSource.ReserveCourierParity);
        AssertEqual(SkipReason.LowerPriorityThanClaim, OrderExecutor.InlineClaimCheck(arbiter, parity), "Support under a Safety claim");
        var stance = Stance(7, 2, IntentPriority.Support, IntentSource.FeudReplay);
        AssertEqual(SkipReason.None, OrderExecutor.InlineClaimCheck(arbiter, stance), "another channel is free");
        var other = Move(8, 300f, -300f, IntentPriority.Support, IntentSource.FeudReplay);
        AssertEqual(SkipReason.None, OrderExecutor.InlineClaimCheck(arbiter, other), "another group is free");
        AssertEqual(SkipReason.None, OrderExecutor.InlineClaimCheck(null, parity), "no arbiter: no claim");
    }

    private static void ArbiterResolveAllocatesNothingAfterWarmup()
    {
        var arbiter = new OrderArbiter();
        var winners = new BattleOrderIntent[1024];
        for (int warm = 0; warm < 3; warm++) SubmitTick(arbiter, winners);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int resolved = 0;
        for (int tick = 0; tick < 5; tick++) resolved += SubmitTick(arbiter, winners);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertEqual(5 * 600, resolved, "winners per tick");
        AssertEqual(0L, allocated, "arbiter BeginTick/Submit/Resolve allocated bytes after warm-up");
    }

    private static int SubmitTick(OrderArbiter arbiter, BattleOrderIntent[] winners)
    {
        arbiter.BeginTick();
        for (int g = 1; g <= 200; g++)
        {
            arbiter.Submit(Move(g, g, -g, IntentPriority.Executor, IntentSource.PostureExecutor));
            arbiter.Submit(Move(g, g + 50f, -g, IntentPriority.Play, IntentSource.Play));
            arbiter.Submit(Stance(g, 2, IntentPriority.Scorer, IntentSource.GroupStanceScorer));
            arbiter.Submit(Behavior(g, 2, IntentPriority.Executor, IntentSource.FireControl));
        }
        return arbiter.Resolve(winners);
    }

    private static void ArbiterForAllianceBoundsChecked()
    {
        AssertTrue(OrderArbiter.ForAlliance(0) != null, "alliance 0");
        AssertTrue(OrderArbiter.ForAlliance(1) != null, "alliance 1");
        AssertTrue(OrderArbiter.ForAlliance(0) != OrderArbiter.ForAlliance(1), "one arbiter per side");
        AssertTrue(OrderArbiter.ForAlliance(2) == null, "Europe (2) has no arbiter");
        AssertTrue(OrderArbiter.ForAlliance(-1) == null, "negative alliance");
        AssertTrue(OrderExecutor.ForAlliance(2) == null, "Europe (2) has no executor");
        AssertTrue(OrderExecutor.ForAlliance(1) != null, "executor alliance 1");
    }

    // ----------------------------------------------------------------- executor

    // #61 MinWaypointDistance (60 m since the 2026-05-21 microshift fix,
    // BattleCommandPostureExecutorPatch.cs:36-43): probe targets drift 20-50 m.
    private static void DedupeWithin60m()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        view.Own[g].LastWaypointX = 500f;
        view.Own[g].LastWaypointZ = -500f;
        view.Own[g].RegimentPaths = 1;
        var executor = NewExecutor();

        var near = executor.Plan(view, Move(1, 540f, -520f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0);
        AssertFalse(near.Execute, "45 m from the active waypoint (probe drift)");
        AssertEqual(SkipReason.Duplicate, near.Skip, "deduped");

        var far = executor.Plan(view, Move(1, 580f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0);
        AssertTrue(far.Execute, "80 m away is a new target");

        view.Own[g].RegimentPaths = 0;
        view.Own[g].QueuedOrders = 0;
        var idleAway = executor.Plan(view, Move(1, 505f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0);
        AssertTrue(idleAway.Execute, "same waypoint but the group stopped 500 m short: re-issue");

        view.Own[g].X = 495f;
        view.Own[g].Z = -490f;
        var arrived = executor.Plan(view, Move(1, 505f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0);
        AssertEqual(SkipReason.Duplicate, arrived.Skip, "same waypoint and already there");

        view.Own[g].Formation = 1;
        AssertEqual(SkipReason.Duplicate, executor.Plan(view, Formation(1, 1, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0).Skip, "same formation");
        view.Own[g].AiStanceOrdered = 3;
        AssertEqual(SkipReason.Duplicate, executor.Plan(view, Stance(1, 3, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0).Skip, "same ordered stance");
        view.Own[g].CombatBehaviorOrdered = 2;
        AssertEqual(SkipReason.Duplicate, executor.Plan(view, Behavior(1, 2, IntentPriority.Executor, IntentSource.FireControl), 1.0).Skip, "same ordered behavior");
        AssertTrue(executor.Plan(view, Behavior(1, 3, IntentPriority.Executor, IntentSource.FireControl), 1.0).Execute, "new behavior");
    }

    private static void SafetyBypassesRecentOrderButNotDedupe()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        var executor = NewExecutor();
        executor.TaskHoldHours = 0f; // isolate the recent-order cooldown from the same-task hold
        double t = 2.0;

        var first = Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor);
        AssertTrue(executor.Plan(view, first, t).Execute, "first order");
        Record(executor, first, t, queuedBefore: 0, pathsBefore: 0, queuedAfter: 0, pathsAfter: 1);
        view.Own[g].LastWaypointX = 500f;
        view.Own[g].LastWaypointZ = -500f;
        view.Own[g].RegimentPaths = 1;

        double soon = t + 10.0 / 3600.0;
        var redirect = Move(1, 900f, -900f, IntentPriority.Executor, IntentSource.PostureExecutor);
        AssertEqual(SkipReason.Cooldown, executor.Plan(view, redirect, soon).Skip, "10 battle-s after an order: recent-order cooldown");

        var safety = Move(1, 900f, -900f, IntentPriority.Safety, IntentSource.Withdrawal);
        AssertTrue(executor.Plan(view, safety, soon).Execute, "Safety bypasses the recent-order cooldown");

        var safetySame = Move(1, 505f, -505f, IntentPriority.Safety, IntentSource.Withdrawal);
        AssertEqual(SkipReason.Duplicate, executor.Plan(view, safetySame, soon).Skip, "Safety is still deduped");

        // #61 RecentOrderHours was 0.05 h = 180 battle-s; the macro cycle is
        // 0.015 h (54 battle-s), so the cooldown must span several cycles.
        double nextCycles = t + 3 * 0.015;
        AssertEqual(SkipReason.Cooldown, executor.Plan(view, redirect, nextCycles).Skip, "three macro cycles later: still cooling down");

        double later = t + (OrderExecutor.RecentOrderBattleSeconds + 1f) / 3600.0;
        AssertTrue(executor.Plan(view, redirect, later).Execute, "cooldown expires after 180 battle-s");
    }

    private static void TaskHoldBlocksSameTaskOnly()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        var executor = NewExecutor();
        executor.TaskHoldHours = 0.1f; // longer than the 180 battle-s recent-order cooldown
        double t = 3.0;

        var attack = Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.AttackObjective);
        Record(executor, attack, t, 0, 0, 0, 1);
        view.Own[g].RegimentPaths = 1;
        view.Own[g].LastWaypointX = 500f;
        view.Own[g].LastWaypointZ = -500f;

        double afterRecent = t + (OrderExecutor.RecentOrderBattleSeconds + 5f) / 3600.0;
        var attackElsewhere = Move(1, 800f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.AttackObjective);
        AssertEqual(SkipReason.Cooldown, executor.Plan(view, attackElsewhere, afterRecent).Skip, "same task inside the hold");

        var fallBack = Move(1, 50f, -50f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.FallBackToLine);
        AssertTrue(executor.Plan(view, fallBack, afterRecent).Execute, "a task change bypasses the hold");

        AssertTrue(executor.Plan(view, attackElsewhere, t + 0.101).Execute, "the hold lapses");
    }

    // ---------------------------------------------------------------- validator

    private static void ClampMatchesVanillaBoundsNegativeZ()
    {
        // RegimentSetPath 131057-131068: x in [1, sizeX-2], z in [-sizeZ+2, -1].
        VanillaMapBounds.ClampXZ(-5f, 10f, 1000f, 800f, out float x, out float z);
        AssertNear(1f, x, 0.0001f, "x low clamp");
        AssertNear(-1f, z, 0.0001f, "z high clamp (battle z is negative)");
        VanillaMapBounds.ClampXZ(2000f, -2000f, 1000f, 800f, out x, out z);
        AssertNear(998f, x, 0.0001f, "x high clamp");
        AssertNear(-798f, z, 0.0001f, "z low clamp");

        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 500f, -400f);
        var reason = WaypointValidator.Validate(view, g, Move(1, 2000f, -2000f, IntentPriority.Executor, IntentSource.PostureExecutor),
            1000f, 800f, new FixtureTerrain(), out x, out z);
        AssertEqual(SkipReason.None, reason, "an off-map target is clamped, not rejected");
        AssertNear(998f, x, 0.0001f, "validator clamps x like vanilla");
        AssertNear(-798f, z, 0.0001f, "validator clamps z like vanilla");

        reason = WaypointValidator.Validate(view, g, Move(1, float.NaN, -100f, IntentPriority.Executor, IntentSource.PostureExecutor),
            1000f, 800f, new FixtureTerrain(), out _, out _);
        AssertEqual(SkipReason.OffMap, reason, "a non-finite target is off-map");
    }

    private static void WaterTargetStepsBack()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        var terrain = new FixtureTerrain { WaterAboveX = 400f };
        var reason = WaypointValidator.Validate(view, g, Move(1, 440f, -100f, IntentPriority.Executor, IntentSource.PostureExecutor),
            1000f, 1000f, terrain, out float x, out float z);
        AssertEqual(SkipReason.None, reason, "a dry point within four 25 m steps");
        AssertNear(390f, x, 0.01f, "stepped back twice toward the start");
        AssertNear(-100f, z, 0.01f, "along the start bearing");
        AssertEqual(1, terrain.StepBackCalls, "vanilla's river pull-back (CheckIfFinalWaypointIsOnTerrain) runs first");
    }

    private static void WaterTargetRejectedAfterFourSteps()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        var terrain = new FixtureTerrain { WaterAboveX = 300f };
        var reason = WaypointValidator.Validate(view, g, Move(1, 440f, -100f, IntentPriority.Executor, IntentSource.PostureExecutor),
            1000f, 1000f, terrain, out _, out _);
        AssertEqual(SkipReason.Water, reason, "440, 415, 390, 365, 340 all water");

        var line = new FixtureTerrain { LineCrossesWaterAlways = true };
        var withdrawal = Move(1, 440f, -100f, IntentPriority.Safety, IntentSource.Withdrawal);
        withdrawal.Flags = IntentFlags.InContactWithdrawal;
        AssertEqual(SkipReason.Water, WaypointValidator.Validate(view, g, withdrawal, 1000f, 1000f, line, out _, out _),
            "an in-contact withdrawal never crosses water or fences (CheckTerrainLine {4,8})");
        AssertEqual(SkipReason.None, WaypointValidator.Validate(view, g, Move(1, 440f, -100f, IntentPriority.Executor, IntentSource.PostureExecutor),
            1000f, 1000f, line, out _, out _), "a planned move may cross; vanilla's NavMesh routes it over a ford or bridge");
    }

    private static void FriendlyFootprintRejectedUnlessRelief()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        view.AddGroup(2, 500f, -500f);
        view.AddGroup(3, 300f, -300f, parentIndex: g);
        var terrain = new FixtureTerrain();

        var into = Move(1, 520f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor);
        AssertEqual(SkipReason.FriendlyFootprint, WaypointValidator.Validate(view, g, into, 1000f, 1000f, terrain, out _, out _), "20 m from another group");
        into.Flags = IntentFlags.ReliefOrSupport;
        AssertEqual(SkipReason.None, WaypointValidator.Validate(view, g, into, 1000f, 1000f, terrain, out _, out _), "relief may pass through");

        var ownChild = Move(1, 310f, -300f, IntentPriority.Executor, IntentSource.PostureExecutor);
        AssertEqual(SkipReason.None, WaypointValidator.Validate(view, g, ownChild, 1000f, 1000f, terrain, out _, out _), "the group's own brigade is not a blocker");
    }

    private static void FriendlyFireLineRejectsArtilleryPosition()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f, flags: UnitFlags.IsArtillery);
        int friend = view.AddGroup(2, 110f, -400f);
        view.AddEnemy(900, 100f, -600f, ObservationState.Visible);
        var terrain = new FixtureTerrain();

        var position = Move(1, 100f, -200f, IntentPriority.Executor, IntentSource.ArtilleryDoctrine);
        position.Flags = IntentFlags.ArtilleryFirePosition;
        position.FireTargetId = 900;
        AssertEqual(SkipReason.FriendlyFireLine, WaypointValidator.Validate(view, g, position, 1000f, 1000f, terrain, out _, out _),
            "friendly 10 m off the gun-target line");

        view.Own[friend].X = 200f;
        AssertEqual(SkipReason.None, WaypointValidator.Validate(view, g, position, 1000f, 1000f, terrain, out _, out _), "line clear at 100 m");
    }

    private static void PointBlankRejectsArtilleryTarget()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f, flags: UnitFlags.IsArtillery);
        int enemy = view.AddEnemy(901, 100f, -240f, ObservationState.Visible);
        var terrain = new FixtureTerrain();
        var position = Move(1, 100f, -200f, IntentPriority.Executor, IntentSource.ArtilleryDoctrine);
        position.Flags = IntentFlags.ArtilleryFirePosition;

        AssertEqual(SkipReason.PointBlank, WaypointValidator.Validate(view, g, position, 1000f, 1000f, terrain, out _, out _),
            "visible enemy 40 m from the battery position");
        view.Observations[enemy].State = ObservationState.Remembered;
        AssertEqual(SkipReason.None, WaypointValidator.Validate(view, g, position, 1000f, 1000f, terrain, out _, out _),
            "a remembered (hidden) enemy is not point-blank");
    }

    private static void PrecheckSkipsRoutedOnRetreatGarrison()
    {
        var unit = new UnitRecord { Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup };
        AssertEqual(SkipReason.None, SilentExitPrecheck.Check(unit, OrderChannel.Move), "healthy group");

        unit.Flags |= UnitFlags.Routed;
        AssertEqual(SkipReason.Routed, SilentExitPrecheck.Check(unit, OrderChannel.Move), "SetWaypoint 91362 isrouted");
        unit.Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.OnRetreat;
        AssertEqual(SkipReason.OnRetreat, SilentExitPrecheck.Check(unit, OrderChannel.Stance), "SetWaypoint 91362 onretreat");
        unit.Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.Garrisoned;
        AssertEqual(SkipReason.Garrison, SilentExitPrecheck.Check(unit, OrderChannel.Move), "SetWaypoint 91331 garrisonreference");
        AssertEqual(SkipReason.None, SilentExitPrecheck.Check(unit, OrderChannel.CombatBehavior), "a garrison may change combat behavior");
        unit.Flags = UnitFlags.Alive;
        AssertEqual(SkipReason.Inactive, SilentExitPrecheck.Check(unit, OrderChannel.Move), "inactive");
    }

    // ----------------------------------------------------------------- outcomes

    private static void QueuedOrderIsPendingNotFailed()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        var executor = NewExecutor();
        var outcomes = new OrderOutcome[8];
        var order = Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor);
        order.Flags = IntentFlags.UseOrderDelay;
        Record(executor, order, 1.0, queuedBefore: 0, pathsBefore: 0, queuedAfter: 1, pathsAfter: 0);

        executor.ObserveOutcomes(view, 1.1, outcomes, out int n);
        AssertEqual(1, n, "one outcome");
        AssertEqual(OrderOutcomeKind.Pending, outcomes[0].Kind, "courier still travelling (D6)");

        view.Own[g].RegimentPaths = 1;
        executor.ObserveOutcomes(view, 1.15, outcomes, out n);
        AssertEqual(OrderOutcomeKind.Moved, outcomes[0].Kind, "paths rose once the courier arrived");
        AssertTrue(executor.TryGetLastOutcome(1, OrderChannel.Move, out OrderOutcome last), "last outcome kept");
        AssertEqual(OrderOutcomeKind.Moved, last.Kind, "last outcome is Moved");
        executor.ObserveOutcomes(view, 1.2, outcomes, out n);
        AssertEqual(0, n, "resolved orders are not reported again");
    }

    private static void PendingTimesOutToFailed()
    {
        var view = new FixtureBattleView();
        view.AddGroup(1, 100f, -100f);
        var executor = NewExecutor();
        var outcomes = new OrderOutcome[8];
        var order = Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor);
        Record(executor, order, 1.0, 0, 0, 1, 0);

        executor.ObserveOutcomes(view, 1.0 + OrderExecutor.PendingCourierTimeoutHours + 0.01, outcomes, out int n);
        AssertTrue(n >= 1, "an outcome");
        AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "the courier never delivered");

        var direct = new OrderExecutor();
        direct.Bind(new FixtureGuard(), new SelfOnlyChainResolver(), new FixtureDispatchMapper(), new FixtureTerrain());
        Record(direct, Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0, 0, 0, 0, 0);
        direct.ObserveOutcomes(view, 1.001, outcomes, out n);
        AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "neither queue nor paths rose: Failed at once (silent vanilla exit)");
    }

    private static void FailedRetriesOnceRotatedThenGivesUp()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 0f, 0f);
        view.Own[g].Z = -500f;
        var executor = NewExecutor();
        var outcomes = new OrderOutcome[8];
        var retries = new BattleOrderIntent[4];
        var order = Move(1, 0f, -700f, IntentPriority.Executor, IntentSource.PostureExecutor);
        view.Own[g].X = 500f;
        order.TargetX = 500f;

        Record(executor, order, 1.0, 0, 0, 0, 0);
        executor.ObserveOutcomes(view, 1.01, outcomes, out int n);
        AssertEqual(2, n, "Failed then Retried");
        AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "first failure");
        AssertEqual(OrderOutcomeKind.Retried, outcomes[1].Kind, "one retry");
        AssertEqual(1, outcomes[1].Retries, "retry count");

        int r = executor.TakeRetries(retries);
        AssertEqual(1, r, "one retry intent");
        float dx = retries[0].TargetX - 500f;
        float dz = retries[0].TargetZ + 500f;
        AssertNear(200f, (float)Math.Sqrt(dx * dx + dz * dz), 0.01f, "same distance from the group");
        AssertNear(30f, (float)(Math.Atan2(Math.Abs(dx), Math.Abs(dz)) * 180.0 / Math.PI), 0.01f, "bearing rotated 30 degrees");
        AssertEqual(0, executor.TakeRetries(retries), "retries are handed out once");

        var retryPlan = executor.PlanRetry(view, retries[0], 1.012);
        AssertTrue(retryPlan.Execute, "a retry bypasses the failed-write backoff");
        AssertEqual(SkipReason.Cooldown, executor.Plan(view, order, 1.012).Skip, "the producer's own re-issue is backed off");

        Record(executor, retries[0], 1.012, 0, 0, 0, 0);
        executor.ObserveOutcomes(view, 1.02, outcomes, out n);
        AssertEqual(2, n, "Failed then GaveUp");
        AssertEqual(OrderOutcomeKind.GaveUp, outcomes[1].Kind, "MaxRetries = 1");
        AssertEqual(0, executor.TakeRetries(retries), "no second retry");
        AssertTrue(executor.TryGetLastOutcome(1, OrderChannel.Move, out OrderOutcome last), "reported to the source");
        AssertEqual(OrderOutcomeKind.GaveUp, last.Kind, "LastOutcome is GaveUp");
        AssertEqual(IntentSource.PostureExecutor, last.Source, "LastOutcome names the source");
    }

    // A group already on the move keeps its moving-unit count when redirected, so
    // the take shows as its waypoint: SetGroupFormation stamps
    // lastsetwaypointposition = target (92067) on every group move, courier or
    // not (91559 runs with newpath:false under useorderdelay). A silent vanilla
    // exit returns before that and leaves the old waypoint.
    private static void RedirectOfMovingGroupIsMoved()
    {
        var outcomes = new OrderOutcome[8];
        foreach (bool courier in new[] { false, true })
        {
            var view = new FixtureBattleView();
            int g = view.AddGroup(1, 100f, -100f);
            int child = view.AddGroup(2, 90f, -110f, parentIndex: g);
            view.Own[g].RegimentPaths = 1;
            view.Own[child].RegimentPaths = 1;
            view.Own[g].LastWaypointX = 300f;
            view.Own[g].LastWaypointZ = -300f;
            var executor = NewExecutor();
            var order = Move(1, 800f, -800f, IntentPriority.Executor, IntentSource.PostureExecutor);
            if (courier) order.Flags = IntentFlags.UseOrderDelay;

            Record(executor, order, 1.0, queuedBefore: 0, pathsBefore: 2, queuedAfter: courier ? 1 : 0, pathsAfter: 2,
                waypointBeforeX: 300f, waypointBeforeZ: -300f);
            view.Own[g].LastWaypointX = 800f;
            view.Own[g].LastWaypointZ = -800f;
            executor.ObserveOutcomes(view, 1.01, outcomes, out int n);
            AssertEqual(1, n, "one outcome, courier=" + courier);
            AssertEqual(OrderOutcomeKind.Moved, outcomes[0].Kind, "redirected moving group took the order, courier=" + courier);

            Record(executor, Move(1, 200f, -900f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.02,
                0, 2, courier ? 1 : 0, 2, waypointBeforeX: 800f, waypointBeforeZ: -800f);
            view.Own[child].LastWaypointX = 210f;
            view.Own[child].LastWaypointZ = -880f;
            executor.ObserveOutcomes(view, 1.03, outcomes, out n);
            AssertEqual(OrderOutcomeKind.Moved, outcomes[0].Kind, "a descendant's waypoint on the target counts, courier=" + courier);

            Record(executor, Move(1, 900f, -200f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.04,
                0, 2, 0, 2, waypointBeforeX: 800f, waypointBeforeZ: -800f);
            view.Own[child].LastWaypointX = 0f;
            view.Own[child].LastWaypointZ = 0f;
            executor.ObserveOutcomes(view, 1.05, outcomes, out n);
            AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "old waypoint kept: a genuine non-take still fails, courier=" + courier);
        }
    }

    private static void StaleWaypointNearTargetIsNotATake()
    {
        // Idle group that stopped 500 m short of its old waypoint: the order goes
        // out (not a duplicate), vanilla drops it, the waypoint is unchanged.
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        view.Own[g].LastWaypointX = 790f;
        view.Own[g].LastWaypointZ = -790f;
        var executor = NewExecutor();
        var outcomes = new OrderOutcome[8];
        Record(executor, Move(1, 800f, -800f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0,
            0, 0, 0, 0, waypointBeforeX: 790f, waypointBeforeZ: -790f);
        executor.ObserveOutcomes(view, 1.01, outcomes, out int n);
        AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "a waypoint already on the target before the call is no evidence");
    }

    // ---------------------------------------------------------------- ownership

    private static void PlayerOwnedNeverExecuted()
    {
        var view = new FixtureBattleView();
        view.AddGroup(1, 100f, -100f, flags: UnitFlags.PlayerOwned);
        var executor = NewExecutor();
        foreach (IntentSource source in Enum.GetValues(typeof(IntentSource)))
        {
            var plan = executor.Plan(view, Move(1, 500f, -500f, IntentPriority.Safety, source), 1.0);
            AssertFalse(plan.Execute, "player-owned never executed: " + source);
            AssertEqual(SkipReason.PlayerOwned, plan.Skip, "skip reason for " + source);
        }
    }

    private static void FeudNeverExecuted()
    {
        var view = new FixtureBattleView();
        view.AddGroup(1, 100f, -100f, flags: UnitFlags.Feud);
        var executor = NewExecutor();
        var posture = executor.Plan(view, Move(1, 500f, -500f, IntentPriority.Safety, IntentSource.PostureExecutor), 1.0);
        AssertEqual(SkipReason.Feud, posture.Skip, "a feud group is vanilla's feud AI's");
        var replay = executor.Plan(view, Move(1, 500f, -500f, IntentPriority.Support, IntentSource.FeudReplay), 1.0);
        AssertTrue(replay.Execute, "#42 replays vanilla's own feud move (FeudReplay exemption)");

        var guarded = new OrderExecutor();
        guarded.Bind(new FixtureGuard { Refuse = SkipReason.Feud }, new SelfOnlyChainResolver(), new FixtureDispatchMapper(), new FixtureTerrain());
        AssertTrue(guarded.Plan(view, Move(1, 500f, -500f, IntentPriority.Support, IntentSource.FeudReplay), 1.0).Execute,
            "the guard's Feud refusal is exempt for FeudReplay");
    }

    private static void GuardRefusalSkipsWithGuardReason()
    {
        var view = new FixtureBattleView();
        view.AddGroup(1, 100f, -100f);
        var executor = new OrderExecutor();
        AssertEqual(SkipReason.ModeNotActive, executor.Plan(view, Move(1, 500f, -500f, IntentPriority.Safety, IntentSource.Withdrawal), 1.0).Skip,
            "an unbound executor fails closed");
        executor.Bind(new FixtureGuard { Refuse = SkipReason.NotAiControlled }, new SelfOnlyChainResolver(), new FixtureDispatchMapper(), new FixtureTerrain());
        AssertEqual(SkipReason.NotAiControlled, executor.Plan(view, Move(1, 500f, -500f, IntentPriority.Safety, IntentSource.Withdrawal), 1.0).Skip,
            "guard reason propagates");
        AssertEqual(SkipReason.Inactive, executor.Plan(view, Move(99, 500f, -500f, IntentPriority.Safety, IntentSource.Withdrawal), 1.0).Skip,
            "unknown group");
    }

    private static void SelfOnlyChainFailsClosedForPlayerAncestor()
    {
        var view = new FixtureBattleView();
        view.AddGroup(1, 100f, -100f, flags: UnitFlags.AncestorOfPlayerCommand);
        view.AddGroup(2, 400f, -400f);
        var executor = NewExecutor();
        AssertEqual(SkipReason.ChainNoTargets, executor.Plan(view, Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0).Skip,
            "without the chain planner an ancestor of the player is not ordered");
        var plan = executor.Plan(view, Move(2, 700f, -700f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0);
        AssertTrue(plan.Execute, "an ordinary group");
        AssertEqual(1, plan.Targets.Count, "orders itself");
        AssertEqual(1, plan.Targets.TargetIndices[0], "target index is the group");
        AssertEqual(OrderExecutor.NoDispatch, plan.DispatchType, "no W&L dispatch");
    }

    private static void AncestorFanOutDispatchesOnce()
    {
        var view = new FixtureBattleView();
        int division = view.AddGroup(10, 100f, -100f, flags: UnitFlags.AncestorOfPlayerCommand);
        view.AddGroup(11, 80f, -120f, parentIndex: division);
        view.AddGroup(12, 120f, -120f, parentIndex: division);
        view.AddGroup(13, 100f, -140f, parentIndex: division, flags: UnitFlags.PlayerCommand | UnitFlags.PlayerOwned);
        var mapper = new FixtureDispatchMapper { Type = 0 };
        var executor = new OrderExecutor();
        executor.Bind(new FixtureGuard(), new FixtureFanOutChain(), mapper, new FixtureTerrain());
        executor.SetMapSize(1000f, 1000f);

        var plan = executor.Plan(view, Move(10, 400f, -400f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.AttackObjective), 1.0);
        AssertTrue(plan.Execute, "the ancestor's order fans out");
        AssertEqual(2, plan.Targets.Count, "the two AI brigades, not the player's");
        AssertTrue(plan.Targets.FannedOut, "fanned out");
        AssertEqual(0, plan.DispatchType, "one vanilla order type for the player's dispatch");
        AssertEqual(1, mapper.Calls, "mapped once per plan, not per target");
        AssertTrue(OrderExecutor.ShouldDispatch(plan, executedTargets: 2), "dispatch when a target executed");
        AssertFalse(OrderExecutor.ShouldDispatch(plan, executedTargets: 0), "no dispatch when nothing moved");

        OrderExecutor.OffsetTarget(view.Own[division], view.Own[1], plan.X, plan.Z, 1000f, 1000f, out float cx, out float cz);
        AssertNear(380f, cx, 0.01f, "a fanned-out brigade keeps its offset from the division");
        AssertNear(-420f, cz, 0.01f, "z offset kept");
    }

    // ------------------------------------------------------------ stance clock

    private static void StanceRecordUsesDoctrineClock()
    {
        // Night jump: the raw clock (GameVars.currenttimefromstart) leaps ~9 h over
        // vanilla's end-of-day cycle while the doctrine clock is frozen. The record
        // (hold, dwell) runs on doctrine hours; vanilla's stamp stays on the raw clock
        // because vanilla compares it with that clock (4264).
        var record = new StanceHoldRecord(2, 10.00f, StanceWriteSource.Executor);
        var nightInput = new StanceWriteInput(
            isAiGroup: true,
            targetStance: 3,
            currentOrderedStance: 2,
            macroAi: 1,
            nowHours: 10.10f,
            record: record,
            source: StanceWriteSource.Scorer,
            vanillaGroupStance: 3,
            vanillaNowHours: 19.50f);
        var decision = TacticalStanceOwnership.Decide(nightInput);
        AssertEqual(StanceWriteAction.Refuse, decision.Action, "inside the executor hold on the doctrine clock");
        AssertEqual("whiskey-hold", decision.Reason, "hold reason");

        var rawClockInput = new StanceWriteInput(true, 3, 2, 1, 19.50f, record, StanceWriteSource.Scorer, 3);
        AssertEqual(StanceWriteAction.Write, TacticalStanceOwnership.Decide(rawClockInput).Action,
            "the raw clock would have expired the hold overnight (the bug)");
        AssertNear(19.50f, rawClockInput.VanillaNowHours, 0.0001f, "vanilla now defaults to now");

        float stamp = TacticalStanceOwnership.VanillaHoldStamp(nightInput, 1f);
        AssertNear(19.50f + TacticalStanceOwnership.HoldHours + 1f, stamp, 0.0001f, "vanilla's 4264 stamp stays on the raw clock");
        var next = TacticalStanceOwnership.Next(new StanceWriteDecision(StanceWriteAction.Write, "write"), nightInput);
        AssertNear(10.10f, next.WrittenAtHours, 0.0001f, "records are written in doctrine hours");
    }

    private static void StanceSourceMapsIntentLadder()
    {
        AssertEqual(StanceWriteSource.ChargeDemotion, TacticalStanceOwnership.SourceFor(IntentSource.ChargeDemotion), "charge denial");
        AssertEqual(StanceWriteSource.Scorer, TacticalStanceOwnership.SourceFor(IntentSource.GroupStanceScorer), "#45 scorer");
        AssertEqual(StanceWriteSource.Executor, TacticalStanceOwnership.SourceFor(IntentSource.PostureExecutor), "#61 posture");
        AssertEqual(StanceWriteSource.Executor, TacticalStanceOwnership.SourceFor(IntentSource.Withdrawal), "safety sources hold like the executor");
    }

    private static void RetryRunsOnlyWhileItsSourceStillClaims()
    {
        var arbiter = new OrderArbiter();
        var retry = Move(4, 600f, -600f, IntentPriority.Executor, IntentSource.PostureExecutor);
        arbiter.BeginTick();
        arbiter.Submit(Move(4, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Resolve(new BattleOrderIntent[4]);
        AssertTrue(OrderExecutor.RetryStillClaimed(arbiter, retry), "the source still wants (group, Move)");

        arbiter.BeginTick();
        arbiter.Submit(Move(4, 100f, -100f, IntentPriority.Safety, IntentSource.Withdrawal));
        arbiter.Resolve(new BattleOrderIntent[4]);
        AssertFalse(OrderExecutor.RetryStillClaimed(arbiter, retry), "superseded by a Safety withdrawal");

        arbiter.BeginTick();
        arbiter.Resolve(new BattleOrderIntent[4]);
        AssertFalse(OrderExecutor.RetryStillClaimed(arbiter, retry), "the source dropped the move");
    }

    // ------------------------------------------------------------------ helpers

    private static OrderExecutor NewExecutor()
    {
        var executor = new OrderExecutor();
        executor.Bind(new FixtureGuard(), new SelfOnlyChainResolver(), new FixtureDispatchMapper(), new FixtureTerrain());
        executor.SetMapSize(1000f, 1000f);
        return executor;
    }

    private static readonly IssuedTarget[] _issued = new IssuedTarget[1];

    // A Move issued to the group itself (self-only chain) at the intent's target.
    private static void Record(OrderExecutor executor, in BattleOrderIntent intent, double now,
        int queuedBefore, int pathsBefore, int queuedAfter, int pathsAfter,
        float waypointBeforeX = 0f, float waypointBeforeZ = 0f)
    {
        _issued[0] = new IssuedTarget
        {
            InstanceId = intent.GroupId, X = intent.TargetX, Z = intent.TargetZ,
            WaypointBeforeX = waypointBeforeX, WaypointBeforeZ = waypointBeforeZ,
        };
        executor.RecordIssued(intent, now, _issued, 1, queuedBefore, pathsBefore, queuedAfter, pathsAfter);
    }

    private static BattleOrderIntent Move(int group, float x, float z, IntentPriority priority, IntentSource source,
        CommandTaskType task = CommandTaskType.AttackObjective)
    {
        return new BattleOrderIntent
        {
            GroupId = group, Channel = OrderChannel.Move, TargetX = x, TargetZ = z, Heading = -1f,
            Priority = priority, Source = source, Task = task, Reason = "test",
        };
    }

    private static BattleOrderIntent Stance(int group, int stance, IntentPriority priority, IntentSource source)
    {
        return new BattleOrderIntent { GroupId = group, Channel = OrderChannel.Stance, Value = stance, Heading = -1f, Priority = priority, Source = source, Reason = "test" };
    }

    private static BattleOrderIntent Formation(int group, int formation, IntentPriority priority, IntentSource source)
    {
        return new BattleOrderIntent { GroupId = group, Channel = OrderChannel.Formation, Value = formation, Heading = -1f, Priority = priority, Source = source, Reason = "test" };
    }

    private static BattleOrderIntent Behavior(int group, int behavior, IntentPriority priority, IntentSource source)
    {
        return new BattleOrderIntent { GroupId = group, Channel = OrderChannel.CombatBehavior, Value = behavior, Heading = -1f, Priority = priority, Source = source, Reason = "test" };
    }

    private static BattleOrderIntent Facing(int group, float heading, IntentPriority priority, IntentSource source)
    {
        return new BattleOrderIntent { GroupId = group, Channel = OrderChannel.Facing, Heading = heading, Priority = priority, Source = source, Reason = "test" };
    }

    // Array-backed stand-in for BattleSnapshot (BC1) behind IBattleUnitView.
    private sealed class FixtureBattleView : IBattleUnitView
    {
        private readonly UnitRecord[] _own = new UnitRecord[64];
        private readonly EnemyObservation[] _observations = new EnemyObservation[32];

        public int Alliance => 0;
        public int OwnCount { get; private set; }
        public UnitRecord[] Own => _own;
        public int ObservationCount { get; private set; }
        public EnemyObservation[] Observations => _observations;

        public int IndexOfOwn(int instanceId)
        {
            for (int i = 0; i < OwnCount; i++)
                if (_own[i].InstanceId == instanceId) return i;
            return -1;
        }

        public bool IsAncestorOf(int ancestorIndex, int descendantIndex)
        {
            int p = descendantIndex >= 0 && descendantIndex < OwnCount ? _own[descendantIndex].ParentIndex : -1;
            while (p >= 0)
            {
                if (p == ancestorIndex) return true;
                p = _own[p].ParentIndex;
            }
            return false;
        }

        public int AddGroup(int id, float x, float z, int parentIndex = -1, UnitFlags flags = UnitFlags.None)
        {
            int i = OwnCount++;
            _own[i] = new UnitRecord
            {
                InstanceId = id, UnitTyp = 15, X = x, Z = z, ParentIndex = parentIndex, AiStanceOrdered = 0, Formation = 0,
                CombatBehaviorOrdered = -1, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup | flags,
            };
            return i;
        }

        public int AddEnemy(int id, float x, float z, ObservationState state)
        {
            int i = ObservationCount++;
            _observations[i] = new EnemyObservation { InstanceId = id, X = x, Z = z, State = state, Strength = 500f, Confidence01 = 1f };
            return i;
        }
    }

    private sealed class FixtureGuard : IOrderGuard
    {
        public SkipReason Refuse = SkipReason.None;

        public bool CanWrite(in UnitRecord unit, out SkipReason reason)
        {
            reason = Refuse;
            return Refuse == SkipReason.None;
        }
    }

    // Ancestor of the player command, not detached: the order goes to the direct
    // children except the player's (plan BC3 chain rule).
    private sealed class FixtureFanOutChain : IChainResolver
    {
        public void ResolveTargets(IBattleUnitView s, int groupIndex, int[] childBuffer, ref ChainResolution result)
        {
            int n = 0;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if (s.Own[i].ParentIndex != groupIndex) continue;
                if ((s.Own[i].Flags & UnitFlags.PlayerOwned) != 0) continue;
                result.TargetIndices[n++] = i;
            }
            result.Count = n;
            result.FannedOut = true;
            result.PlayerShareNeedsDispatch = true;
        }
    }

    private sealed class FixtureDispatchMapper : IDispatchTypeMapper
    {
        public int Type = OrderExecutor.NoDispatch;
        public int Calls;

        public int Map(CommandTaskType task, bool targetEnemyHeld, bool chargeSupported)
        {
            Calls++;
            return Type;
        }
    }

    private sealed class FixtureTerrain : ITerrainProbe
    {
        public float WaterAboveX = float.MaxValue;
        public bool LineCrossesWaterAlways;
        public int StepBackCalls;

        public bool IsWaterOrFence(float x, float z) => x > WaterAboveX;

        public bool LineCrossesWater(float x0, float z0, float x1, float z1) => LineCrossesWaterAlways;

        public bool StepBackFromRiver(float x0, float z0, ref float x1, ref float z1)
        {
            StepBackCalls++;
            return false;
        }
    }
}
