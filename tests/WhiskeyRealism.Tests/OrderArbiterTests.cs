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
            ("battle-core MoveBeforeStanceBeforeFormationOrder", MoveBeforeStanceBeforeFormationOrder),
            ("battle-core EveryMoveComesBeforeAnyStanceAcrossGroups", EveryMoveComesBeforeAnyStanceAcrossGroups),
            ("battle-core InlineIntentRefusedUnderHigherClaim", InlineIntentRefusedUnderHigherClaim),
            ("battle-core ArbiterResolveAllocatesNothingAfterWarmup", ArbiterResolveAllocatesNothingAfterWarmup),
            ("battle-core ArbiterForAllianceBoundsChecked", ArbiterForAllianceBoundsChecked),
            ("battle-core DedupeWithin60m", DedupeWithin60m),
            ("bc14w WORKS-CONTEST-EXECUTOR the executor records the last move it issued, and a contest intent lands inside the recent-order window", WorksContestExecutor),
            ("battle-core SafetyBypassesRecentOrderButNotDedupe", SafetyBypassesRecentOrderButNotDedupe),
            ("battle-core TaskHoldBlocksSameTaskOnly", TaskHoldBlocksSameTaskOnly),
            ("battle-core ClampMatchesVanillaBoundsNegativeZ", ClampMatchesVanillaBoundsNegativeZ),
            ("battle-core WaterTargetStepsBack", WaterTargetStepsBack),
            ("battle-core WaterTargetRejectedAfterFourSteps", WaterTargetRejectedAfterFourSteps),
            ("battle-core WithdrawalFromFenceCellSkipsStartCell", WithdrawalFromFenceCellSkipsStartCell),
            ("battle-core FriendlyFootprintRejectedUnlessRelief", FriendlyFootprintRejectedUnlessRelief),
            ("battle-core FriendlyFireLineRejectsArtilleryPosition", FriendlyFireLineRejectsArtilleryPosition),
            ("battle-core PointBlankRejectsArtilleryTarget", PointBlankRejectsArtilleryTarget),
            ("battle-core PrecheckSkipsRoutedOnRetreatGarrison", PrecheckSkipsRoutedOnRetreatGarrison),
            ("battle-core CourierMoveCountsAtTheCall", CourierMoveCountsAtTheCall),
            ("battle-core SilentExitFailsAtNextObservation", SilentExitFailsAtNextObservation),
            ("battle-core FailedRetriesOnceRotatedThenGivesUp", FailedRetriesOnceRotatedThenGivesUp),
            ("battle-core RedirectOfMovingGroupIsMoved", RedirectOfMovingGroupIsMoved),
            ("battle-core StaleWaypointNearTargetIsNotATake", StaleWaypointNearTargetIsNotATake),
            ("battle-core DescendantAlreadyOnTargetIsNotATake", DescendantAlreadyOnTargetIsNotATake),
            ("battle-core PlayerOwnedNeverExecuted", PlayerOwnedNeverExecuted),
            ("battle-core FeudNeverExecuted", FeudNeverExecuted),
            ("battle-core GuardRefusalSkipsWithGuardReason", GuardRefusalSkipsWithGuardReason),
            ("battle-core DetachedPlayerAncestorOrdersItselfAndDispatchesMovesOnly", DetachedPlayerAncestorOrdersItselfAndDispatchesMovesOnly),
            ("battle-core MaintenanceMoveOfThePlayersAncestorSendsNoDispatch", MaintenanceMoveOfThePlayersAncestorSendsNoDispatch),
            ("battle-core RecoveryAndLeafSlotMovesAreMarkedMaintenance", RecoveryAndLeafSlotMovesAreMarkedMaintenance),
            ("battle-core AncestorFanOutDispatchesOnce", AncestorFanOutDispatchesOnce),
            ("battle-core FanOutOutcomeCountsTheOrderedSet", FanOutOutcomeCountsTheOrderedSet),
            ("battle-core GatedOffArbiterPublishesNoClaims", GatedOffArbiterPublishesNoClaims),
            ("battle-core StanceRecordUsesDoctrineClock", StanceRecordUsesDoctrineClock),
            ("battle-core StanceSourceMapsIntentLadder", StanceSourceMapsIntentLadder),
            ("battle-core StanceCheapPathNeedsLiveVanillaStamp", StanceCheapPathNeedsLiveVanillaStamp),
            ("battle-core RetryRunsOnlyWhileItsSourceStillClaims", RetryRunsOnlyWhileItsSourceStillClaims),
            ("battle-core SupersededRetryIsReportedToTheSource", SupersededRetryIsReportedToTheSource),
            ("battle-core SafetyRegimentMoveSurvivesAnExecutorBrigadeMove", SafetyRegimentMoveSurvivesAnExecutorBrigadeMove),
            ("battle-core ClaimedBranchFansOutTheRestMovesWhole", ClaimedBranchFansOutTheRestMovesWhole),
            ("battle-core EqualOrLowerDescendantClaimDoesNotFanOut", EqualOrLowerDescendantClaimDoesNotFanOut),
            ("battle-core UnrelatedSafetyClaimDoesNotShieldAnEqualDescendant", UnrelatedSafetyClaimDoesNotShieldAnEqualDescendant),
            ("battle-core GroupMoveYieldsWhenEveryChildIsClaimed", GroupMoveYieldsWhenEveryChildIsClaimed),
        });
    }

    // ------------------------------------------------------------------ arbiter

    private static void HigherPriorityWinsPerChannel()
    {
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(Move(1, 100f, -100f, IntentPriority.Play, IntentSource.DivisionPlay));
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
        arbiter.Submit(Move(2, 400f, -400f, IntentPriority.Play, IntentSource.DivisionPlay));
        var winners = new BattleOrderIntent[8];
        int n = arbiter.Resolve(winners);
        AssertEqual(3, n, "group 1 move + group 1 stance + group 2 move");
        AssertTrue(arbiter.TryGetClaim(1, OrderChannel.Stance, out BattleOrderIntent stance), "stance claim");
        AssertEqual(IntentPriority.Scorer, stance.Priority, "a Scorer stance survives an Executor move");
        AssertTrue(arbiter.TryGetClaim(2, OrderChannel.Move, out BattleOrderIntent move2), "group 2 move claim");
        AssertEqual(IntentSource.DivisionPlay, move2.Source, "group 2 move source");
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

    private static void MoveBeforeStanceBeforeFormationOrder()
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
        AssertEqual(OrderChannel.Move, winners[0].Channel, "move first: a stance order queued ahead of it would delay it (AddToOrderQueue 124954-124966)");
        AssertEqual(OrderChannel.Stance, winners[1].Channel, "stance after the move");
        AssertEqual(OrderChannel.Formation, winners[2].Channel, "formation after the stance (#61 L976-988: after the move)");
        AssertEqual(OrderChannel.Facing, winners[3].Channel, "facing after formation");
        AssertEqual(OrderChannel.CombatBehavior, winners[4].Channel, "combat behavior last");
    }

    private static void EveryMoveComesBeforeAnyStanceAcrossGroups()
    {
        // The smoke: stance orders of eight brigades went out before the first brigade's move. Whatever the
        // groups, no stance winner precedes a move winner, and a stop precedes the move of its group.
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        for (int g = 1; g <= 8; g++) arbiter.Submit(Stance(g, 2, IntentPriority.Scorer, IntentSource.GroupStanceScorer));
        arbiter.Submit(Move(1, 100f, -100f, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Submit(Move(5, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor));
        arbiter.Submit(new BattleOrderIntent { GroupId = 5, Channel = OrderChannel.Stop, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "test" });
        var winners = new BattleOrderIntent[16];
        int n = arbiter.Resolve(winners);
        AssertEqual(11, n, "eight stances, two moves, one stop");
        int lastMove = -1, firstStance = int.MaxValue, stop = -1, moveOf5 = -1;
        for (int i = 0; i < n; i++)
        {
            if (winners[i].Channel == OrderChannel.Move) { lastMove = i; if (winners[i].GroupId == 5) moveOf5 = i; }
            if (winners[i].Channel == OrderChannel.Stance && i < firstStance) firstStance = i;
            if (winners[i].Channel == OrderChannel.Stop) stop = i;
        }
        AssertTrue(lastMove < firstStance, "the last move (" + lastMove + ") comes before the first stance (" + firstStance + ")");
        AssertTrue(stop >= 0 && stop < moveOf5, "a stop still comes before the move of its group");
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
        long before = TestFixtures.BeginAllocationWindow();
        int resolved = 0;
        for (int tick = 0; tick < 5; tick++) resolved += SubmitTick(arbiter, winners);
        long allocated = TestFixtures.AllocatedSince(before);
        AssertEqual(5 * 600, resolved, "winners per tick");
        AssertEqual(0L, allocated, "arbiter BeginTick/Submit/Resolve allocated bytes after warm-up");
    }

    private static int SubmitTick(OrderArbiter arbiter, BattleOrderIntent[] winners)
    {
        arbiter.BeginTick();
        for (int g = 1; g <= 200; g++)
        {
            arbiter.Submit(Move(g, g, -g, IntentPriority.Executor, IntentSource.PostureExecutor));
            arbiter.Submit(Move(g, g + 50f, -g, IntentPriority.Play, IntentSource.DivisionPlay));
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
        AssertTrue(executor.Plan(view, Stance(1, 3, IntentPriority.Executor, IntentSource.PostureExecutor), 1.0).Execute, "same ordered stance: a hold refresh for the writer (BC5b)");
        view.Own[g].CombatBehaviorOrdered = 2;
        AssertEqual(SkipReason.Duplicate, executor.Plan(view, Behavior(1, 2, IntentPriority.Executor, IntentSource.FireControl), 1.0).Skip, "same ordered behavior");
        AssertTrue(executor.Plan(view, Behavior(1, 3, IntentPriority.Executor, IntentSource.FireControl), 1.0).Execute, "new behavior");
    }

    // BC14W (spec § 7A, the contest rule): LastIssuedMove is what the contest tells Whiskey's own moves from vanilla's by; a contest intent lands at once.
    private static void WorksContestExecutor()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        int child = view.AddGroup(2, 120f, -100f);
        var executor = NewExecutor();
        executor.TaskHoldHours = 0f;
        double t = 4.0;

        AssertFalse(executor.LastIssuedMove(1, out _, out _, out _), "nothing issued yet");
        var first = Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor);
        Record(executor, first, t, pathsBefore: 0, pathsAfter: 1);
        AssertTrue(executor.LastIssuedMove(1, out float x, out float z, out double hours), "a Move is recorded");
        AssertTrue(x == 500f && z == -500f && hours == t, "its target and hour");
        var second = Move(1, 700f, -300f, IntentPriority.Executor, IntentSource.PostureExecutor);
        Record(executor, second, t + 0.5, pathsBefore: 1, pathsAfter: 1);
        executor.LastIssuedMove(1, out x, out z, out hours);
        AssertTrue(x == 700f && z == -300f && hours == t + 0.5, "each issued Move overwrites it");

        // A group Move that fanned out to a child records the child's own target too (its waypoint is the child's, offset from the group's).
        var fan = Move(1, 900f, -900f, IntentPriority.Executor, IntentSource.PostureExecutor);
        var targets = new[]
        {
            new IssuedTarget { InstanceId = 1, X = 900f, Z = -900f },
            new IssuedTarget { InstanceId = 2, X = 920f, Z = -900f },
        };
        executor.RecordIssued(fan, t + 1.0, targets, 2, new UnitWaypoint[0], 0, 0, 1);
        AssertTrue(executor.LastIssuedMove(2, out x, out z, out _) && x == 920f && z == -900f, "the fanned-out child has its own");
        executor.RecordIssued(Stance(1, 2, IntentPriority.Executor, IntentSource.PostureExecutor), t + 2.0, null, 0, null, 0, 0, 0);
        executor.LastIssuedMove(1, out x, out z, out hours);
        AssertTrue(x == 900f && hours == t + 1.0, "a stance is no move");

        // BattleScope clears the static executors' records.
        OrderExecutor shared = OrderExecutor.ForAlliance(0);
        Record(shared, first, t, 0, 1);
        AssertTrue(shared.LastIssuedMove(1, out _, out _, out _), "recorded on the side's executor");
        WhiskeyRealism.Util.BattleScope.ResetAll();
        AssertFalse(shared.LastIssuedMove(1, out _, out _, out _), "BattleScope clears it");

        // The contest intent lands inside the recent-order window; the same move without the flag is cooling down.
        view.Own[g].LastWaypointX = 700f;
        view.Own[g].LastWaypointZ = -300f;
        view.Own[g].RegimentPaths = 1;
        double soon = t + 1.0 + 10.0 / 3600.0;
        var back = Move(1, 200f, -200f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.HoldObjective);
        AssertEqual(SkipReason.Cooldown, executor.Plan(view, back, soon).Skip, "an ordinary move 10 battle-s after an order cools down");
        BattleOrderIntent contest = back;
        contest.Flags |= IntentFlags.Contest;
        AssertTrue(executor.Plan(view, contest, soon).Execute, "the contest move lands");
        BattleOrderIntent usesDelay = contest;
        usesDelay.Flags |= IntentFlags.UseOrderDelay;
        AssertTrue(executor.Plan(view, usesDelay, soon).Execute, "and inside the courier cadence too");

        // A contest stop of a moving group lands inside the window; an ordinary stop is cooling down.
        var stop = new BattleOrderIntent { GroupId = 1, Channel = OrderChannel.Stop, Heading = -1f, Priority = IntentPriority.Executor,
            Source = IntentSource.PostureExecutor, Task = CommandTaskType.HoldObjective, Reason = "test" };
        executor.RecordIssued(stop, soon - 0.001, null, 0, null, 0, 0, 0);
        AssertEqual(SkipReason.Cooldown, executor.Plan(view, stop, soon).Skip, "an ordinary stop right after a stop cools down");
        stop.Flags |= IntentFlags.Contest;
        AssertTrue(executor.Plan(view, stop, soon).Execute, "the contest stop lands");
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
        Record(executor, first, t, pathsBefore: 0, pathsAfter: 1);
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
        Record(executor, attack, t, 0, 1);
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

    // CheckTerrainLine tests its start position first (27659-27686), so a group
    // standing at a fence or wall (battle terrain 8) would have every in-contact
    // withdrawal rejected. The validator starts the line one step (2 m) out. (A
    // ford is terrain 5, outside {4, 8}.)
    private static void WithdrawalFromFenceCellSkipsStartCell()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        var withdrawal = Move(1, 40f, -100f, IntentPriority.Safety, IntentSource.Withdrawal);
        withdrawal.Flags = IntentFlags.InContactWithdrawal;

        var fence = new FixtureTerrain { WaterDiscX = 100f, WaterDiscZ = -100f, WaterDiscRadius = 1f };
        AssertEqual(SkipReason.None, WaypointValidator.Validate(view, g, withdrawal, 1000f, 1000f, fence, out _, out _),
            "a group at a fence cell may withdraw off it");

        var stream = new FixtureTerrain { WaterDiscX = 70f, WaterDiscZ = -100f, WaterDiscRadius = 1.5f };
        AssertEqual(SkipReason.None, WaypointValidator.Validate(view, g, withdrawal, 1000f, 1000f, stream, out float sx, out _),
            "water further along the line still blocks the crossing");
        AssertNear(90f, sx, 0.01f, "stepped back twice to the near bank (40 -> 65 -> 90)");

        var shortHop = Move(1, 101.5f, -100f, IntentPriority.Safety, IntentSource.Withdrawal);
        shortHop.Flags = IntentFlags.InContactWithdrawal;
        var everywhereButTarget = new FixtureTerrain { LineCrossesWaterAlways = true };
        AssertEqual(SkipReason.None, WaypointValidator.Validate(view, g, shortHop, 1000f, 1000f, everywhereButTarget, out _, out _),
            "a move inside the start cell has no line to test");
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

    // Under useorderdelay SetWaypoint still reaches RegimentSetPath in the same
    // call, and AddPath adds the path at once (130519) with pathstatus 0 until
    // the courier arrives (131243-131246): a courier move from a standstill is
    // Moved at the call, with no wait for the courier.
    private static void CourierMoveCountsAtTheCall()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        var executor = NewExecutor();
        var outcomes = new OrderOutcome[8];
        var order = Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor);
        order.Flags = IntentFlags.UseOrderDelay;
        Record(executor, order, 1.0, pathsBefore: 0, pathsAfter: 1);
        view.Own[g].RegimentPaths = 1;

        executor.ObserveOutcomes(view, 1.01, outcomes, out int n);
        AssertEqual(1, n, "one outcome");
        AssertEqual(OrderOutcomeKind.Moved, outcomes[0].Kind, "paths rose at the call");
        AssertTrue(executor.TryGetLastOutcome(1, OrderChannel.Move, out OrderOutcome last), "last outcome kept");
        AssertEqual(OrderOutcomeKind.Moved, last.Kind, "last outcome is Moved");
        executor.ObserveOutcomes(view, 1.02, outcomes, out n);
        AssertEqual(0, n, "resolved orders are not reported again");
    }

    private static void SilentExitFailsAtNextObservation()
    {
        var view = new FixtureBattleView();
        view.AddGroup(1, 100f, -100f);
        var executor = NewExecutor();
        var outcomes = new OrderOutcome[8];
        var order = Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor);
        order.Flags = IntentFlags.UseOrderDelay;
        Record(executor, order, 1.0, pathsBefore: 0, pathsAfter: 0);
        executor.ObserveOutcomes(view, 1.001, outcomes, out int n);
        AssertTrue(n >= 1, "an outcome");
        AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "no paths and no waypoint stamp: a silent vanilla exit, Failed at once");
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

        Record(executor, order, 1.0, 0, 0);
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

        Record(executor, retries[0], 1.012, 0, 0);
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

            Record(executor, order, 1.0, pathsBefore: 2, pathsAfter: 2,
                waypointBeforeX: 300f, waypointBeforeZ: -300f);
            view.Own[g].LastWaypointX = 800f;
            view.Own[g].LastWaypointZ = -800f;
            executor.ObserveOutcomes(view, 1.01, outcomes, out int n);
            AssertEqual(1, n, "one outcome, courier=" + courier);
            AssertEqual(OrderOutcomeKind.Moved, outcomes[0].Kind, "redirected moving group took the order, courier=" + courier);

            Record(executor, Move(1, 200f, -900f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.02,
                2, 2, waypointBeforeX: 800f, waypointBeforeZ: -800f);
            view.Own[child].LastWaypointX = 210f;
            view.Own[child].LastWaypointZ = -880f;
            executor.ObserveOutcomes(view, 1.03, outcomes, out n);
            AssertEqual(OrderOutcomeKind.Moved, outcomes[0].Kind, "a descendant's waypoint on the target counts, courier=" + courier);

            Record(executor, Move(1, 900f, -200f, IntentPriority.Executor, IntentSource.PostureExecutor), 1.04,
                2, 2, waypointBeforeX: 800f, waypointBeforeZ: -800f);
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
            0, 0, waypointBeforeX: 790f, waypointBeforeZ: -790f);
        executor.ObserveOutcomes(view, 1.01, outcomes, out int n);
        AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "a waypoint already on the target before the call is no evidence");
    }

    // Every waypoint TargetTaken inspects needs its pre-call value: a brigade
    // whose waypoint was already within 60 m of the division's new target is no
    // evidence that vanilla took the order.
    private static void DescendantAlreadyOnTargetIsNotATake()
    {
        var outcomes = new OrderOutcome[8];
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 100f, -100f);
        int child = view.AddGroup(2, 90f, -110f, parentIndex: g);
        view.Own[g].RegimentPaths = 1;
        view.Own[g].LastWaypointX = 800f;
        view.Own[g].LastWaypointZ = -800f;
        view.Own[child].LastWaypointX = 210f;
        view.Own[child].LastWaypointZ = -880f;
        var executor = NewExecutor();
        var order = Move(1, 200f, -900f, IntentPriority.Executor, IntentSource.PostureExecutor);

        Record(executor, order, 1.0, 1, 1, waypointBeforeX: 800f, waypointBeforeZ: -800f,
            childId: 2, childBeforeX: 210f, childBeforeZ: -880f);
        executor.ObserveOutcomes(view, 1.01, outcomes, out int n);
        AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "the brigade was already there before the call: not a take");

        var fresh = NewExecutor();
        Record(fresh, order, 1.0, 1, 1, waypointBeforeX: 800f, waypointBeforeZ: -800f,
            childId: 2, childBeforeX: 500f, childBeforeZ: -500f);
        fresh.ObserveOutcomes(view, 1.01, outcomes, out n);
        AssertEqual(OrderOutcomeKind.Moved, outcomes[0].Kind, "the brigade's waypoint moved onto the target: a take");
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
        guarded.Bind(new FixtureGuard { Refuse = SkipReason.Feud }, new FixtureTerrain());
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
        executor.Bind(new FixtureGuard { Refuse = SkipReason.NotAiControlled }, new FixtureTerrain());
        AssertEqual(SkipReason.NotAiControlled, executor.Plan(view, Move(1, 500f, -500f, IntentPriority.Safety, IntentSource.Withdrawal), 1.0).Skip,
            "guard reason propagates");
        AssertEqual(SkipReason.Inactive, executor.Plan(view, Move(99, 500f, -500f, IntentPriority.Safety, IntentSource.Withdrawal), 1.0).Skip,
            "unknown group");
    }

    // BC3 chain rule through the executor: an ancestor of a detached player
    // command has no exposed player-owned descendant, so it is ordered as a group,
    // and the player's share of its move goes out as a dispatch. Only a Move
    // carries one: the dispatch is the move's target.
    private static void DetachedPlayerAncestorOrdersItselfAndDispatchesMovesOnly()
    {
        var view = new FixtureBattleView();
        int division = view.AddGroup(1, 100f, -100f, flags: UnitFlags.AncestorOfPlayerCommand);
        view.AddGroup(2, 100f, -140f, parentIndex: division,
            flags: UnitFlags.PlayerCommand | UnitFlags.PlayerOwned | UnitFlags.PermanentlyDetached);
        int other = view.AddGroup(3, 400f, -400f);
        var executor = NewExecutor();

        var plan = executor.Plan(view, Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.HoldObjective), 1.0);
        AssertTrue(plan.Execute, "the ancestor of a detached player command is ordered");
        AssertEqual(1, plan.Targets.Count, "as a group");
        AssertEqual(division, plan.Targets.TargetIndices[0], "target index is the group");
        AssertFalse(plan.Targets.FannedOut, "no fan-out: the player's command is detached");
        AssertEqual(12, plan.DispatchType, "the player's share: defend (HoldObjective)");

        BattleOrderIntent stanceIntent = Stance(1, 4, IntentPriority.Executor, IntentSource.PostureExecutor);
        stanceIntent.Task = CommandTaskType.HoldObjective;
        var stance = executor.Plan(view, stanceIntent, 1.0);
        AssertTrue(stance.Execute, "a stance on the ancestor executes");
        AssertEqual(OrderExecutor.NoDispatch, stance.DispatchType, "no dispatch for a stance, even one carrying a task");

        var ordinary = executor.Plan(view, Move(3, 700f, -700f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.HoldObjective), 1.0);
        AssertTrue(ordinary.Execute, "an ordinary group");
        AssertEqual(other, ordinary.Targets.TargetIndices[0], "orders itself");
        AssertEqual(OrderExecutor.NoDispatch, ordinary.DispatchType, "no W&L dispatch outside the player's chain");
    }

    // Smoke 2026-10-17 order 13: an interrupted path the posture executor re-issued for the player's division reached the panel as an order.
    // A Maintenance move is carried out like any other but sends the player nothing.
    private static void MaintenanceMoveOfThePlayersAncestorSendsNoDispatch()
    {
        var view = new FixtureBattleView();
        int division = view.AddGroup(1, 100f, -100f, flags: UnitFlags.AncestorOfPlayerCommand);
        view.AddGroup(2, 100f, -140f, parentIndex: division,
            flags: UnitFlags.PlayerCommand | UnitFlags.PlayerOwned | UnitFlags.PermanentlyDetached);
        var executor = NewExecutor();

        BattleOrderIntent maintenance = Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.HoldObjective);
        maintenance.Flags |= IntentFlags.Maintenance;
        var plan = executor.Plan(view, maintenance, 1.0);
        AssertTrue(plan.Execute, "the move itself is carried out");
        AssertEqual(OrderExecutor.NoDispatch, plan.DispatchType, "and the player is sent no order for it");
        AssertFalse(OrderExecutor.ShouldDispatch(plan, executedTargets: 1), "so nothing is dispatched");

        var control = NewExecutor().Plan(view, Move(1, 500f, -500f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.HoldObjective), 1.0);
        AssertEqual(12, control.DispatchType, "control: the same move without the flag dispatches");
    }

    // The two producers of a re-lay mark their Move: PostureIntentPlan.Submit for RecoverInterruptedOrder, LeafMove for a formation slot.
    private static void RecoveryAndLeafSlotMovesAreMarkedMaintenance()
    {
        var leaf = PostureIntentPlan.LeafMove(5, CommandTaskType.AdvanceToAssembly, 100f, 200f, 90f, inContactWithdrawal: false);
        AssertEqual(OrderChannel.Move, leaf.Channel, "a leaf slot move is a Move");
        AssertTrue((leaf.Flags & IntentFlags.Maintenance) != 0, "and a re-lay, not an order for the player");

        foreach (var action in new[] { PostureExecutionAction.RecoverInterruptedOrder, PostureExecutionAction.SetWaypoint, PostureExecutionAction.FallbackToLine })
        {
            var sink = new MaintenanceSink();
            PostureIntentPlan.Submit(sink, new PostureIntentRequest
            {
                GroupId = 5, Action = action, Task = CommandTaskType.AdvanceToAssembly, HasTarget = true, TargetX = 100f, TargetZ = 200f,
                Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "test",
            });
            int moves = 0;
            foreach (var intent in sink.Intents)
            {
                if (intent.Channel != OrderChannel.Move) continue;
                moves++;
                bool marked = (intent.Flags & IntentFlags.Maintenance) != 0;
                AssertEqual(action == PostureExecutionAction.RecoverInterruptedOrder, marked, action + ": only an interrupted-order recovery is maintenance");
            }
            AssertEqual(1, moves, action + " submits one Move");
        }
    }

    private sealed class MaintenanceSink : IIntentSink
    {
        public readonly System.Collections.Generic.List<BattleOrderIntent> Intents = new System.Collections.Generic.List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) => Intents.Add(intent);
    }

    private static void AncestorFanOutDispatchesOnce()
    {
        var view = new FixtureBattleView();
        int division = view.AddGroup(10, 100f, -100f, flags: UnitFlags.AncestorOfPlayerCommand);
        view.AddGroup(11, 80f, -120f, parentIndex: division);
        view.AddGroup(12, 120f, -120f, parentIndex: division);
        view.AddGroup(13, 100f, -140f, parentIndex: division, flags: UnitFlags.PlayerCommand | UnitFlags.PlayerOwned);
        view.AddEnemy(90, 420f, -420f, ObservationState.Visible); // the objective is enemy-held
        var executor = NewExecutor();

        var plan = executor.Plan(view, Move(10, 400f, -400f, IntentPriority.Executor, IntentSource.PostureExecutor, CommandTaskType.AttackObjective), 1.0);
        AssertTrue(plan.Execute, "the ancestor's order fans out");
        AssertEqual(2, plan.Targets.Count, "the two AI brigades, not the player's");
        AssertTrue(plan.Targets.FannedOut, "fanned out");
        AssertEqual(0, plan.DispatchType, "one vanilla order type for the player's dispatch: take the enemy-held objective");
        AssertTrue(OrderExecutor.ShouldDispatch(plan, executedTargets: 2), "dispatch when a target executed");
        AssertFalse(OrderExecutor.ShouldDispatch(plan, executedTargets: 0), "no dispatch when nothing moved");

        OrderExecutor.OffsetTarget(view.Own[division], view.Own[1], plan.X, plan.Z, 1000f, 1000f, out float cx, out float cz);
        AssertNear(380f, cx, 0.01f, "a fanned-out brigade keeps its offset from the division");
        AssertNear(-420f, cz, 0.01f, "z offset kept");
    }

    // The runtime sums moving units over the units it ordered (the AI brigades),
    // so the outcome compares the same set — not the whole division, whose
    // player brigade moves on the player's orders.
    private static void FanOutOutcomeCountsTheOrderedSet()
    {
        var view = new FixtureBattleView();
        int division = view.AddGroup(10, 100f, -100f, flags: UnitFlags.AncestorOfPlayerCommand);
        int left = view.AddGroup(11, 80f, -120f, parentIndex: division);
        view.AddGroup(12, 120f, -120f, parentIndex: division);
        int player = view.AddGroup(13, 100f, -140f, parentIndex: division, flags: UnitFlags.PlayerCommand | UnitFlags.PlayerOwned);
        var executor = NewExecutor();
        var outcomes = new OrderOutcome[8];
        var order = Move(10, 400f, -400f, IntentPriority.Executor, IntentSource.PostureExecutor);
        order.Flags = IntentFlags.UseOrderDelay;
        var targets = new[]
        {
            new IssuedTarget { InstanceId = 11, X = 380f, Z = -420f },
            new IssuedTarget { InstanceId = 12, X = 420f, Z = -420f },
        };
        executor.RecordIssued(order, 1.0, targets, 2, null, 0, pathsBefore: 0, pathsAfter: 0);

        view.Own[player].RegimentPaths = 1;
        executor.ObserveOutcomes(view, 1.01, outcomes, out int n);
        AssertTrue(n >= 1, "an outcome");
        AssertEqual(OrderOutcomeKind.Failed, outcomes[0].Kind, "the player's brigade moving is not the ordered brigades moving");

        var again = NewExecutor();
        again.RecordIssued(order, 1.02, targets, 2, null, 0, pathsBefore: 0, pathsAfter: 0);
        view.Own[left].RegimentPaths = 1;
        again.ObserveOutcomes(view, 1.03, outcomes, out n);
        AssertEqual(OrderOutcomeKind.Moved, outcomes[0].Kind, "an ordered brigade gained paths");
    }

    private static void GatedOffArbiterPublishesNoClaims()
    {
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(Move(7, 100f, -100f, IntentPriority.Safety, IntentSource.Withdrawal));
        arbiter.Resolve(new BattleOrderIntent[4]);
        var parity = Move(7, 300f, -300f, IntentPriority.Support, IntentSource.ReserveCourierParity);
        AssertEqual(SkipReason.LowerPriorityThanClaim, OrderExecutor.InlineClaimCheck(arbiter, parity), "claim published");

        arbiter.ClearClaims();
        AssertFalse(arbiter.TryGetClaim(7, OrderChannel.Move, out _), "gated off: no claim left");
        AssertEqual(SkipReason.None, OrderExecutor.InlineClaimCheck(arbiter, parity), "an inline intent is not refused by a stale claim");
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

    // The writer's steady-state short-circuit skips the full pass (and the
    // re-stamp) while its doctrine-clock record is fresh. Vanilla's 4264 guard
    // runs on the raw clock, which jumps ~9 h overnight: the short-circuit must
    // also see the raw stamp still live or vanilla re-decides a held group.
    private static void StanceCheapPathNeedsLiveVanillaStamp()
    {
        var record = new StanceHoldRecord(2, 10.00f, StanceWriteSource.Executor);
        const float reset = 1f;
        float stamp = TacticalStanceOwnership.VanillaHoldStamp(30.0f, TacticalStanceOwnership.HoldHours, reset);

        AssertTrue(TacticalStanceOwnership.SteadyStateHolds(record, 2, StanceWriteSource.Executor, 2, 10.05f, stamp, 30.05f, reset),
            "fresh record, live stamp: skip the full pass");
        AssertFalse(TacticalStanceOwnership.SteadyStateHolds(record, 2, StanceWriteSource.Executor, 2, 10.05f, stamp, 39.50f, reset),
            "night jump: the doctrine record is fresh but vanilla's raw stamp lapsed");
        AssertTrue(TacticalStanceOwnership.SteadyStateHolds(record, 2, StanceWriteSource.Executor, 2, 10.05f,
            30.25f + reset, 30.25f, reset), "a stamp exactly at now + reset is still honoured by 4264");
        AssertFalse(TacticalStanceOwnership.SteadyStateHolds(record, 2, StanceWriteSource.Executor, 2,
            10.00f + TacticalStanceOwnership.HoldHours * 0.5f, stamp, 30.05f, reset), "record older than half the hold");
        AssertFalse(TacticalStanceOwnership.SteadyStateHolds(record, 3, StanceWriteSource.Executor, 2, 10.05f, stamp, 30.05f, reset),
            "another target stance");
        AssertFalse(TacticalStanceOwnership.SteadyStateHolds(record, 2, StanceWriteSource.ChargeDemotion, 2, 10.05f, stamp, 30.05f, reset),
            "a higher source takes the full pass");
        AssertFalse(TacticalStanceOwnership.SteadyStateHolds(record, 2, StanceWriteSource.Executor, 3, 10.05f, stamp, 30.05f, reset),
            "vanilla changed the ordered stance");
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

    // BC7: a retry whose source lost the (group, Move) claim is not run; the
    // source learns it was superseded (the "superseded" count of the
    // [OrderArbiter] row), not a stale Retried.
    private static void SupersededRetryIsReportedToTheSource()
    {
        var view = new FixtureBattleView();
        int g = view.AddGroup(1, 500f, -500f);
        var executor = NewExecutor();
        var outcomes = new OrderOutcome[8];
        var retries = new BattleOrderIntent[4];
        var order = Move(1, 500f, -700f, IntentPriority.Executor, IntentSource.PostureExecutor);

        Record(executor, order, 1.0, 0, 0);
        executor.ObserveOutcomes(view, 1.01, outcomes, out int n);
        AssertEqual(OrderOutcomeKind.Retried, outcomes[n - 1].Kind, "a retry is pending");
        AssertEqual(1, executor.TakeRetries(retries), "one retry handed out");

        OrderOutcome superseded = executor.RecordSuperseded(retries[0], 1.012);
        AssertEqual(OrderOutcomeKind.Superseded, superseded.Kind, "superseded");
        AssertEqual(1, superseded.Retries, "the retry's count");
        AssertEqual(OrderChannel.Move, superseded.Channel, "move channel");
        AssertTrue(executor.TryGetLastOutcome(1, OrderChannel.Move, out OrderOutcome last), "reported");
        AssertEqual(OrderOutcomeKind.Superseded, last.Kind, "LastOutcome is Superseded, not Retried");
        AssertEqual(IntentSource.PostureExecutor, last.Source, "names the retry's source");
        AssertNear(1.012f, (float)last.AtHours, 0.0001f, "at the tick it lapsed");

        executor.ObserveOutcomes(view, 1.02, outcomes, out n);
        AssertEqual(0, n, "nothing pending: the retry lapsed");
        AssertEqual(0, executor.TakeRetries(retries), "and is not handed out again");
    }

    // ------------------------------------------------------------------ helpers

    // ------------------------------------------------------- cross-level claims

    // Claims are per (unit, channel), so a regiment's Safety withdrawal and its
    // brigade's Executor move both win. Vanilla SetGroupFormation re-paths every
    // non-detached, non-routed child of the moved group (92076-92108,
    // RegimentSetPath 92108), so the brigade's move must not reach the regiment:
    // it fans out to the other children instead.
    private static void SafetyRegimentMoveSurvivesAnExecutorBrigadeMove()
    {
        var view = new FixtureBattleView();
        int brigade = view.AddGroup(10, 100f, -100f);
        int withdrawing = view.AddGroup(11, 80f, -120f, parentIndex: brigade);
        int left = view.AddGroup(12, 100f, -120f, parentIndex: brigade);
        int right = view.AddGroup(13, 120f, -120f, parentIndex: brigade);
        var executor = NewExecutor();
        var winners = new[]
        {
            Move(11, 60f, -40f, IntentPriority.Safety, IntentSource.Withdrawal, CommandTaskType.None),
            Move(10, 400f, -400f, IntentPriority.Executor, IntentSource.PostureExecutor),
        };
        executor.BeginTickClaims(view, winners, winners.Length);

        var regiment = executor.Plan(view, winners[0], 1.0);
        AssertTrue(regiment.Execute, "the withdrawal executes");
        AssertEqual(1, regiment.Targets.Count, "on the regiment itself");
        AssertEqual(withdrawing, regiment.Targets.TargetIndices[0], "the withdrawing regiment");

        var group = executor.Plan(view, winners[1], 1.0);
        AssertTrue(group.Execute, "the brigade still moves");
        AssertTrue(group.Targets.FannedOut, "fanned out around the claimed regiment");
        AssertEqual(2, group.Targets.Count, "the brigade's other children");
        AssertTrue(Targets(group, left) && Targets(group, right), "both other children move");
        AssertFalse(Targets(group, withdrawing), "the withdrawing regiment is not re-pathed");
        AssertFalse(Targets(group, brigade), "the brigade is not ordered as a group");

        executor.EndTickClaims();
        var later = executor.Plan(view, winners[1], 1.0);
        AssertFalse(later.Targets.FannedOut, "outside the tick no claim shields anything");
        AssertEqual(brigade, later.Targets.TargetIndices[0], "the group orders itself");
    }

    private static void ClaimedBranchFansOutTheRestMovesWhole()
    {
        var view = new FixtureBattleView();
        int division = view.AddGroup(1, 100f, -100f);
        int claimedBrigade = view.AddGroup(2, 80f, -120f, parentIndex: division);
        int otherBrigade = view.AddGroup(3, 120f, -120f, parentIndex: division);
        view.AddGroup(4, 70f, -130f, parentIndex: claimedBrigade);
        int sibling = view.AddGroup(5, 90f, -130f, parentIndex: claimedBrigade);
        view.AddGroup(6, 120f, -130f, parentIndex: otherBrigade);
        var executor = NewExecutor();
        var winners = new[]
        {
            Move(4, 40f, -60f, IntentPriority.Safety, IntentSource.Withdrawal, CommandTaskType.None),
            Move(1, 400f, -400f, IntentPriority.Executor, IntentSource.PostureExecutor),
        };
        executor.BeginTickClaims(view, winners, winners.Length);

        var plan = executor.Plan(view, winners[1], 1.0);
        AssertTrue(plan.Execute, "the division moves");
        AssertEqual(2, plan.Targets.Count, "the unclaimed brigade and the claimed regiment's sibling");
        AssertTrue(Targets(plan, otherBrigade), "the other brigade moves whole");
        AssertTrue(Targets(plan, sibling), "the sibling regiment moves on its own");
        AssertFalse(Targets(plan, claimedBrigade), "the claimed regiment's brigade is not ordered as a group");
    }

    private static void EqualOrLowerDescendantClaimDoesNotFanOut()
    {
        var view = new FixtureBattleView();
        int brigade = view.AddGroup(10, 100f, -100f);
        view.AddGroup(11, 80f, -120f, parentIndex: brigade);
        view.AddGroup(12, 120f, -120f, parentIndex: brigade);
        var groupMove = Move(10, 400f, -400f, IntentPriority.Executor, IntentSource.PostureExecutor);

        foreach (IntentPriority child in new[] { IntentPriority.Executor, IntentPriority.Play })
        {
            var executor = NewExecutor();
            var winners = new[] { Move(11, 60f, -40f, child, IntentSource.DivisionPlay), groupMove };
            executor.BeginTickClaims(view, winners, winners.Length);
            var plan = executor.Plan(view, groupMove, 1.0);
            AssertFalse(plan.Targets.FannedOut, child + " claim below does not outrank the group move");
            AssertEqual(brigade, plan.Targets.TargetIndices[0], "the group orders itself");
        }
    }

    // A Safety claim elsewhere on the field raises the tick's highest claim, so
    // the group's own descendants are each compared with the group's priority:
    // an Executor claim under an Executor group still does not fan it out.
    private static void UnrelatedSafetyClaimDoesNotShieldAnEqualDescendant()
    {
        var view = new FixtureBattleView();
        int brigade = view.AddGroup(10, 100f, -100f);
        view.AddGroup(11, 80f, -120f, parentIndex: brigade);
        view.AddGroup(12, 120f, -120f, parentIndex: brigade);
        int other = view.AddGroup(20, 300f, -100f);
        view.AddGroup(21, 300f, -120f, parentIndex: other);
        var groupMove = Move(10, 400f, -400f, IntentPriority.Executor, IntentSource.PostureExecutor);
        var executor = NewExecutor();
        var winners = new[]
        {
            Move(21, 300f, -40f, IntentPriority.Safety, IntentSource.Withdrawal, CommandTaskType.None),
            Move(11, 60f, -40f, IntentPriority.Executor, IntentSource.DivisionPlay),
            groupMove,
        };
        executor.BeginTickClaims(view, winners, winners.Length);
        var plan = executor.Plan(view, groupMove, 1.0);
        AssertFalse(plan.Targets.FannedOut, "an equal claim below does not outrank the group move");
        AssertEqual(1, plan.Targets.Count, "one target");
        AssertEqual(brigade, plan.Targets.TargetIndices[0], "the group orders itself");
    }

    private static void GroupMoveYieldsWhenEveryChildIsClaimed()
    {
        var view = new FixtureBattleView();
        int brigade = view.AddGroup(10, 100f, -100f);
        view.AddGroup(11, 80f, -120f, parentIndex: brigade);
        view.AddGroup(12, 120f, -120f, parentIndex: brigade);
        var executor = NewExecutor();
        var winners = new[]
        {
            Move(11, 60f, -40f, IntentPriority.Safety, IntentSource.Withdrawal, CommandTaskType.None),
            Move(12, 140f, -40f, IntentPriority.Safety, IntentSource.Withdrawal, CommandTaskType.None),
            Move(10, 400f, -400f, IntentPriority.Executor, IntentSource.PostureExecutor),
        };
        executor.BeginTickClaims(view, winners, winners.Length);
        var plan = executor.Plan(view, winners[2], 1.0);
        AssertFalse(plan.Execute, "nothing left to move");
        AssertEqual(SkipReason.ChainNoTargets, plan.Skip, "the group move yields");
    }

    private static bool Targets(in ExecutionPlan plan, int index)
    {
        for (int t = 0; t < plan.Targets.Count; t++)
            if (plan.Targets.TargetIndices[t] == index) return true;
        return false;
    }

    private static OrderExecutor NewExecutor()
    {
        var executor = new OrderExecutor();
        executor.Bind(new FixtureGuard(), new FixtureTerrain());
        executor.SetMapSize(1000f, 1000f);
        return executor;
    }

    private static readonly IssuedTarget[] _issued = new IssuedTarget[1];
    private static readonly UnitWaypoint[] _before = new UnitWaypoint[2];

    // A Move issued to the group itself (self-only chain) at the intent's target,
    // with the pre-call waypoints of the group and optionally one descendant.
    private static void Record(OrderExecutor executor, in BattleOrderIntent intent, double now,
        int pathsBefore, int pathsAfter,
        float waypointBeforeX = 0f, float waypointBeforeZ = 0f,
        int childId = 0, float childBeforeX = 0f, float childBeforeZ = 0f)
    {
        _issued[0] = new IssuedTarget { InstanceId = intent.GroupId, X = intent.TargetX, Z = intent.TargetZ };
        _before[0] = new UnitWaypoint { InstanceId = intent.GroupId, Target = 0, X = waypointBeforeX, Z = waypointBeforeZ };
        _before[1] = new UnitWaypoint { InstanceId = childId, Target = 0, X = childBeforeX, Z = childBeforeZ };
        executor.RecordIssued(intent, now, _issued, 1, _before, childId != 0 ? 2 : 1, pathsBefore, pathsAfter);
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

    // Array-backed stand-in for BattleSnapshot behind IBattleUnitView.
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

    private sealed class FixtureTerrain : ITerrainProbe
    {
        public float WaterAboveX = float.MaxValue;
        public bool LineCrossesWaterAlways;
        public float WaterDiscX, WaterDiscZ, WaterDiscRadius;
        public int StepBackCalls;

        public bool IsWaterOrFence(float x, float z) => x > WaterAboveX || InDisc(x, z);

        // Vanilla CheckTerrainLine (27659-27686): floor(d / 2) samples, 2 m apart,
        // starting at (x0, z0) itself.
        public bool LineCrossesWater(float x0, float z0, float x1, float z1)
        {
            if (LineCrossesWaterAlways) return true;
            float dx = x1 - x0;
            float dz = z1 - z0;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            int samples = (int)Math.Floor(d / 2f);
            for (int i = 0; i < samples; i++)
            {
                float t = i * 2f / d;
                if (IsWaterOrFence(x0 + dx * t, z0 + dz * t)) return true;
            }
            return false;
        }

        private bool InDisc(float x, float z)
        {
            if (WaterDiscRadius <= 0f) return false;
            float dx = x - WaterDiscX;
            float dz = z - WaterDiscZ;
            return dx * dx + dz * dz <= WaterDiscRadius * WaterDiscRadius;
        }

        public bool StepBackFromRiver(float x0, float z0, ref float x1, ref float z1)
        {
            StepBackCalls++;
            return false;
        }
    }
}
