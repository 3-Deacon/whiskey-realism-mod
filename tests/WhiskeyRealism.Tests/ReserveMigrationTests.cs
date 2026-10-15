using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC5c: reserves (#59 with B8's doctrine deny merged in, #56 courier parity
// through the inline executor) and the B8 withdrawal fallback as an arbiter
// producer. Pure halves only: TacticalReserveCommitGate and
// WithdrawalIntentProducer.Plan.cs.
internal static class ReserveMigrationTests
{
    public static void Run()
    {
        RunAll(new (string name, Action run)[]
        {
            ("reserve deny rolls back for role reserve", ReserveDenyRollsBackForRoleReserve),
            ("reserve courier parity removes vanilla's path only for an executing plan", CourierParityRemovesPathOnlyForExecutingPlan),
            ("reserve courier parity inline uses order delay", ReserveCourierParityInlineUsesOrderDelay),
            ("reserve courier parity skipped under safety claim", ReserveCourierParitySkippedUnderSafetyClaim),
            ("withdrawal fallback is a safety move validated", WithdrawalFallbackIsSafetyMoveValidated),
            ("withdrawal combat behavior 2 via writer", WithdrawalCombatBehavior2ViaWriter),
            ("withdrawal skips player-owned and feud units", WithdrawalSkipsPlayerOwnedAndFeud),
            ("withdrawal retreat bearing is fog-honest", WithdrawalRetreatBearingIsFogHonest),
            ("release reserve producer support priority", ReleaseReserveProducerSupportPriority),
            ("stalled fallback reissue from producer", StalledFallbackReissueFromProducer),
            ("vanilla may commit reserve mirrors early exits", VanillaMayCommitReserveMirrorsEarlyExits),
            ("reserve odds read snapshot slice pressure", ReserveOddsReadSnapshotSlicePressure),
            ("withdrawal reads own-side cover and friendly routs", WithdrawalReadsOwnSideCoverAndRouts),
            ("withdrawal retreat bearing matches vanilla retreatangle", WithdrawalRetreatBearingMatchesVanillaRetreatAngle),
            ("snapshot carries own-side fallback fields through assembly", SnapshotCarriesFallbackFieldsThroughAssembly),
        });
    }

    private const int Army = 100, Reserve = 101, LeftLine = 102, RightLine = 103, Leaf = 104, Enemy = 900;

    // ---------------------------------------------------------------- #59 + B8

    private static void ReserveDenyRollsBackForRoleReserve()
    {
        // A Reserve role holds (the deleted B8 doctrine deny implied the same role:
        // DecideReserve denies only order.Role == Reserve, and order.Role is the ledger
        // node's role, which HeldResolution reads first).
        AssertEqual(TacticalReserveCommitGate.Action.Deny, Gate(Resolved(DirectChildRole.Reserve)).Action, "role reserve holds");
        AssertEqual("role-reserve-hold", Gate(Resolved(DirectChildRole.Reserve)).Reason, "reason");
        AssertEqual(TacticalReserveCommitGate.Action.Allow,
            Gate(Resolved(DirectChildRole.Reserve), released: true).Action, "released by the allocator");
        AssertEqual(TacticalReserveCommitGate.Action.Allow,
            Gate(Resolved(DirectChildRole.Reserve), engaged: true).Action, "already engaged");
        AssertEqual(TacticalReserveCommitGate.Action.Observe,
            Gate(Resolved(DirectChildRole.Reserve), playerControlled: true).Action, "player-owned unit or group");
        AssertEqual(TacticalReserveCommitGate.Action.Observe,
            Gate(Resolved(DirectChildRole.Reserve), vanillaCommitted: false).Action, "no vanilla commit");
        AssertEqual(TacticalReserveCommitGate.Action.Allow, Gate(Resolved(DirectChildRole.Main)).Action, "main role");
        AssertEqual(TacticalReserveCommitGate.Action.Observe, Gate(NotFound()).Action, "nothing resolved");

        // The rollback set: units vanilla gave a direct path (paths rose, no courier queued).
        AssertTrue(TacticalReserveCommitGate.VanillaCommittedUnit(0, 1, 0, 0, useOrderDelays: true), "direct path");
        AssertFalse(TacticalReserveCommitGate.VanillaCommittedUnit(0, 1, 0, 1, useOrderDelays: true), "courier queued");
        AssertFalse(TacticalReserveCommitGate.VanillaCommittedUnit(1, 1, 0, 0, useOrderDelays: true), "no new path");

        // Ledger state first, then the allocator: a ledger Reserve / ReserveWait holds.
        var ops = new[]
        {
            new CommandNodeOperationalState("node-" + Reserve, CommandEchelonKind.DivisionLike, CommandNodeRole.SupportingAttack,
                CommandTaskType.ReserveWait, CommandTaskState.Committed),
        };
        CommandIntentResolution held = TacticalReserveCommitGate.HeldResolution(ops, Reserve, 0, Resolved(DirectChildRole.SupportMain));
        AssertTrue(held.Found, "ledger state found");
        AssertEqual("operations-ledger-command-state:SupportingAttack:ReserveWait", held.Reason, "reason keeps role and task");
        AssertEqual(DirectChildRole.Reserve, held.Intent.Role, "ReserveWait task holds in reserve");
        AssertTrue(TacticalReserveCommitGate.ReleasedByAllocator(held, Resolved(DirectChildRole.SupportMain)), "allocator released it");
        CommandIntentResolution noLedger = TacticalReserveCommitGate.HeldResolution(ops, LeftLine, 0, Resolved(DirectChildRole.Fix));
        AssertEqual(DirectChildRole.Fix, noLedger.Intent.Role, "no ledger state: the allocator's view");
    }

    // ---------------------------------------------------------------- #56

    private static void ReserveCourierParityInlineUsesOrderDelay()
    {
        BattleOrderIntent intent = TacticalReserveCommitGate.CourierParityIntent(Leaf, 520f, -480f, 90f);
        AssertEqual(OrderChannel.Move, intent.Channel, "move");
        AssertEqual(IntentSource.ReserveCourierParity, intent.Source, "source");
        AssertEqual(IntentPriority.Support, intent.Priority, "support priority");
        AssertTrue((intent.Flags & IntentFlags.UseOrderDelay) != 0, "courier delay");
        AssertTrue((intent.Flags & IntentFlags.DoubleQuick) != 0, "double quick, as vanilla 6171");
        AssertTrue((intent.Flags & IntentFlags.ReliefOrSupport) != 0, "support move may target beside a friendly group");
        AssertEqual(CommandTaskType.None, intent.Task, "no task hold stamped on a leaf");
        AssertEqual(90f, intent.Heading, "vanilla's support facing");

        // Through the executor pipeline: planned, with the courier delay.
        BattleSnapshot s = LeafSnapshot(morale: 1f);
        OrderExecutor executor = NewExecutor(new Terrain());
        ExecutionPlan plan = executor.Plan(s, intent, 1.0);
        AssertTrue(plan.Execute, "planned: " + plan.Skip);
        AssertTrue(plan.UseOrderDelay, "useorderdelay: true, like the old #56 SetWaypoint");
    }

    private static void ReserveCourierParitySkippedUnderSafetyClaim()
    {
        BattleOrderIntent intent = TacticalReserveCommitGate.CourierParityIntent(Leaf, 520f, -480f, -1f);
        var arbiter = new OrderArbiter();

        // No claim: convert (remove vanilla's immediate path, re-issue delayed).
        arbiter.BeginTick();
        arbiter.Resolve(new BattleOrderIntent[4]);
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Convert,
            Parity(arbiter, intent), "no claim");

        // A published Safety withdrawal on the unit: leave vanilla's path alone.
        arbiter.BeginTick();
        arbiter.Submit(new BattleOrderIntent
        {
            GroupId = Leaf, Channel = OrderChannel.Move, TargetX = 500f, TargetZ = -600f, Heading = -1f,
            Priority = IntentPriority.Safety, Source = IntentSource.Withdrawal, Reason = "test",
        });
        arbiter.Resolve(new BattleOrderIntent[4]);
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Leave,
            Parity(arbiter, intent), "safety claim outranks courier parity: path not removed");

        // A same-priority claim does not refuse it (D4: refused only by a higher claim).
        arbiter.BeginTick();
        arbiter.Submit(new BattleOrderIntent
        {
            GroupId = Leaf, Channel = OrderChannel.Move, TargetX = 500f, TargetZ = -600f, Heading = -1f,
            Priority = IntentPriority.Support, Source = IntentSource.ReserveRelease, Reason = "test",
        });
        arbiter.Resolve(new BattleOrderIntent[4]);
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Convert, Parity(arbiter, intent), "same priority");

        arbiter.ClearClaims();
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Leave,
            Parity(arbiter, intent, unitWritable: false), "player-owned reserve: vanilla's own pick stays untouched");
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Leave,
            Parity(arbiter, intent, inSnapshot: false), "no snapshot record: the inline executor could not re-issue it");
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Leave,
            Parity(arbiter, intent, conversionApplies: false), "SetWaypoint would not queue it (91449/91463)");
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Leave,
            Parity(arbiter, intent, useOrderDelays: false), "order delays off");
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Leave,
            Parity(arbiter, intent, afterPaths: 0), "vanilla added no path (or #59 rolled it back)");
    }

    private static void CourierParityRemovesPathOnlyForExecutingPlan()
    {
        BattleOrderIntent intent = TacticalReserveCommitGate.CourierParityIntent(Leaf, 520f, -480f, -1f);
        OrderExecutor executor = NewExecutor(new Terrain());

        // A plan that executes: vanilla's immediate path goes, the delayed order replaces it.
        ExecutionPlan fresh = executor.Plan(LeafSnapshot(morale: 1f), intent, 1.0);
        AssertTrue(fresh.Execute, "fresh plan executes");
        AssertTrue(TacticalReserveCommitGate.RemoveVanillaPath(TacticalReserveCommitGate.CourierParityAction.Convert, fresh), "remove then execute");

        // The unit already stands at the target with that waypoint: the plan dedupes,
        // so vanilla's commit is left untouched rather than lost.
        BattleSnapshot there = LeafSnapshot(morale: 1f, leafX: 515f, leafZ: -485f, waypointX: 520f, waypointZ: -480f);
        ExecutionPlan duplicate = executor.Plan(there, intent, 1.0);
        AssertFalse(duplicate.Execute, "duplicate refused");
        AssertEqual(SkipReason.Duplicate, duplicate.Skip, "skip reason");
        AssertFalse(TacticalReserveCommitGate.RemoveVanillaPath(TacticalReserveCommitGate.CourierParityAction.Convert, duplicate),
            "refused plan keeps vanilla's path");

        // A water target the validator cannot step back from: refused, path kept.
        ExecutionPlan water = NewExecutor(new Terrain { WaterBelowZ = float.MaxValue }).Plan(LeafSnapshot(morale: 1f), intent, 1.0);
        AssertEqual(SkipReason.Water, water.Skip, "water");
        AssertFalse(TacticalReserveCommitGate.RemoveVanillaPath(TacticalReserveCommitGate.CourierParityAction.Convert, water), "water keeps path");

        AssertFalse(TacticalReserveCommitGate.RemoveVanillaPath(TacticalReserveCommitGate.CourierParityAction.Leave, fresh),
            "screened out: never removed");
    }

    // ---------------------------------------------------------------- withdrawal

    private static void WithdrawalFallbackIsSafetyMoveValidated()
    {
        // Morale 0.45 < 1.2 x 0.4, a visible enemy in range (under fire) and flank
        // pressure, no cover known: WithdrawalCandidate -> RearGuard -> Fallback.
        BattleSnapshot s = LeafSnapshot(morale: 0.45f, flank: 50f);
        int leaf = s.IndexOfOwn(Leaf);
        var kind = WithdrawalIntentProducer.PlanUnit(s, leaf, Settings(), out BattleOrderIntent move, out BattleOrderIntent behavior);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.Fallback, kind, "rear guard fallback");
        AssertEqual(OrderChannel.Move, move.Channel, "move");
        AssertEqual(IntentPriority.Safety, move.Priority, "safety");
        AssertEqual(IntentSource.Withdrawal, move.Source, "source");
        AssertTrue((move.Flags & IntentFlags.InContactWithdrawal) != 0, "in-contact: immediate, line-checked");
        AssertEqual(Leaf, move.GroupId, "the regiment itself");
        // Enemy due north (+z): fall back due south by the fallback distance, facing the enemy.
        AssertNear(500f, move.TargetX, 0.01f, "x");
        AssertNear(-600f, move.TargetZ, 0.01f, "z");
        AssertNear(0f, move.Heading, 0.01f, "face the enemy (retreat bearing + 180)");

        // The executor validates it: a water target steps back toward the unit.
        OrderExecutor executor = NewExecutor(new Terrain { WaterBelowZ = -590f });
        ExecutionPlan plan = executor.Plan(s, move, 1.0);
        AssertTrue(plan.Execute, "planned: " + plan.Skip);
        AssertFalse(plan.UseOrderDelay, "in-contact withdrawal is immediate");
        AssertNear(-575f, plan.Z, 0.01f, "stepped 25 m back off the water");

        // Collapse (morale under the trigger): full retreat, current facing kept.
        BattleSnapshot collapse = LeafSnapshot(morale: 0.3f);
        var retreat = WithdrawalIntentProducer.PlanUnit(collapse, collapse.IndexOfOwn(Leaf), Settings(), out BattleOrderIntent run, out _);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.Retreat, retreat, "full retreat");
        AssertNear(-700f, run.TargetZ, 0.01f, "retreat distance");
        AssertNear(45f, run.Heading, 0.01f, "keeps its facing (MicroAICheckForRetreats 4858)");

        // Already moving: vanilla's CheckLineFallbacks filter (5137) refuses.
        BattleSnapshot moving = LeafSnapshot(morale: 0.3f, paths: 1);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(moving, moving.IndexOfOwn(Leaf), Settings(), out _, out _), "moving");
        // A move the validator refuses (water everywhere) is not produced, nor its behavior.
        var refused = WithdrawalIntentProducer.PlanUnit(s, leaf, Settings(new Terrain { WaterBelowZ = float.MaxValue }),
            out BattleOrderIntent noMove, out BattleOrderIntent noBehavior);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None, refused, "validator-refused fallback");
        AssertEqual(0, noMove.GroupId, "no move");
        AssertEqual(0, noBehavior.GroupId, "no combat behavior 2 for a move that will not happen");

        BattleSnapshot steady = LeafSnapshot(morale: 0.9f);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(steady, steady.IndexOfOwn(Leaf), Settings(), out _, out _), "steady");
    }

    private static void WithdrawalCombatBehavior2ViaWriter()
    {
        BattleSnapshot s = LeafSnapshot(morale: 0.45f, flank: 50f);
        WithdrawalIntentProducer.PlanUnit(s, s.IndexOfOwn(Leaf), Settings(), out _, out BattleOrderIntent behavior);
        AssertEqual(OrderChannel.CombatBehavior, behavior.Channel, "combat behavior channel, not a field write");
        AssertEqual(2, behavior.Value, "vanilla fallback behavior 2 (CheckLineFallbacks 5179-5180)");
        AssertTrue((behavior.Flags & IntentFlags.PerRegiment) != 0, "per regiment (D10)");
        AssertEqual(IntentPriority.Safety, behavior.Priority, "safety");
        AssertEqual(Leaf, behavior.GroupId, "the regiment");

        BattleSnapshot collapse = LeafSnapshot(morale: 0.3f);
        WithdrawalIntentProducer.PlanUnit(collapse, collapse.IndexOfOwn(Leaf), Settings(), out _, out BattleOrderIntent none);
        AssertEqual(0, none.GroupId, "a retreat sets no behavior (MicroAICheckForRetreats)");

        // Planned through the executor's CombatBehavior channel.
        ExecutionPlan plan = NewExecutor(new Terrain()).Plan(s, behavior, 1.0);
        AssertTrue(plan.Execute, "planned: " + plan.Skip);
    }

    private static void WithdrawalSkipsPlayerOwnedAndFeud()
    {
        BattleSnapshot owned = LeafSnapshot(morale: 0.3f, leafFlags: UnitFlags.PlayerOwned);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(owned, owned.IndexOfOwn(Leaf), Settings(), out _, out _), "player-owned");
        BattleSnapshot feud = LeafSnapshot(morale: 0.3f, leafFlags: UnitFlags.Feud);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(feud, feud.IndexOfOwn(Leaf), Settings(), out _, out _), "feud");
        BattleSnapshot stanceOff = LeafSnapshot(morale: 0.3f, groupStance: -1);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(stanceOff, stanceOff.IndexOfOwn(Leaf), Settings(), out _, out _), "group without an AI stance");
        BattleSnapshot feudGroup = LeafSnapshot(morale: 0.3f, groupFlags: UnitFlags.Feud);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(feudGroup, feudGroup.IndexOfOwn(Leaf), Settings(), out _, out _),
            "a feuding group's units (B8: ai_feudstance == -1 gate)");
        BattleSnapshot routed = LeafSnapshot(morale: 0.3f, leafFlags: UnitFlags.Routed);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(routed, routed.IndexOfOwn(Leaf), Settings(), out _, out _), "routed");
    }

    private static void WithdrawalRetreatBearingIsFogHonest()
    {
        BattleSnapshot s = LeafSnapshot(morale: 0.3f);
        AssertTrue(WithdrawalIntentProducer.TryRetreatBearing(s, s.IndexOfOwn(Leaf), out float away), "visible enemy");
        AssertNear(180f, away, 0.01f, "away from the closest visible enemy (vanilla retreatangle 122833)");

        // Nothing visible: away from the front axis.
        BattleSnapshot blind = LeafSnapshot(morale: 0.3f, enemyVisible: false);
        AssertTrue(WithdrawalIntentProducer.TryRetreatBearing(blind, blind.IndexOfOwn(Leaf), out float rear), "front axis");
        AssertNear(180f, rear, 0.01f, "front axis heading + 180");

        // A remembered enemy is not a bearing source, however close: the closest VISIBLE one is.
        BattleSnapshot mixed = LeafSnapshot(morale: 0.3f, enemyVisible: false);
        mixed.AddObservation(new EnemyObservation { InstanceId = 901, X = 560f, Z = -500f, Strength = 500f, State = ObservationState.Remembered, Confidence01 = 1f });
        mixed.AddObservation(new EnemyObservation { InstanceId = Enemy, X = 500f, Z = -400f, Strength = 500f, State = ObservationState.Visible, Confidence01 = 1f });
        mixed.SetClosestVisibleEnemy(1, 1, 100f);
        AssertTrue(WithdrawalIntentProducer.TryRetreatBearing(mixed, 1, out float awayVisible), "visible enemy present");
        AssertNear(180f, awayVisible, 0.01f, "away from the visible enemy, not the closer remembered one");
        BattleSnapshot rememberedOnly = LeafSnapshot(morale: 0.3f, enemyVisible: false);
        rememberedOnly.AddObservation(new EnemyObservation { InstanceId = 901, X = 560f, Z = -500f, Strength = 500f, State = ObservationState.Remembered, Confidence01 = 1f });
        AssertTrue(WithdrawalIntentProducer.TryRetreatBearing(rememberedOnly, 1, out float awayAxis), "front axis");
        AssertNear(180f, awayAxis, 0.01f, "only remembered enemies: away from the front axis");

        BattleSnapshot none = LeafSnapshot(morale: 0.3f, enemyVisible: false, axis: false);
        AssertFalse(WithdrawalIntentProducer.TryRetreatBearing(none, none.IndexOfOwn(Leaf), out _), "no direction known");
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(none, none.IndexOfOwn(Leaf), Settings(), out _, out _), "no direction: no move");
    }

    // ---------------------------------------------------------------- release

    private static void ReleaseReserveProducerSupportPriority()
    {
        BattleSnapshot s = ArmySnapshot();
        int reserve = s.IndexOfOwn(Reserve);
        int leftSector = s.FrontAxis.SectorOf(300f, -500f);
        AssertTrue(TacticalReserveCommitGate.TryPlanRelease(s, reserve, leftSector, out BattleOrderIntent intent), "planned");
        AssertEqual(OrderChannel.Move, intent.Channel, "move");
        AssertEqual(IntentPriority.Support, intent.Priority, "support (spec §4 ladder: #59 reserve release)");
        AssertEqual(IntentSource.ReserveRelease, intent.Source, "source");
        AssertEqual(CommandTaskType.ReleaseReserve, intent.Task, "task");
        AssertTrue((intent.Flags & IntentFlags.UseOrderDelay) != 0, "planned group move: courier");
        AssertTrue((intent.Flags & IntentFlags.ReliefOrSupport) != 0, "relief move");
        AssertEqual(Reserve, intent.GroupId, "the released group, not its leaves");
        // Behind the line in that sector: the sibling at (300, -500) stood off back along the front.
        AssertNear(300f, intent.TargetX, 0.01f, "x");
        AssertNear(-500f - TacticalReserveCommitGate.ReleaseStandOffMeters, intent.TargetZ, 0.01f, "z");
        AssertNear(0f, intent.Heading, 0.01f, "faces the front");

        // Executed through the pipeline although the target sits by a friendly group.
        ExecutionPlan plan = NewExecutor(new Terrain()).Plan(s, intent, 1.0);
        AssertTrue(plan.Execute, "planned: " + plan.Skip);
        AssertTrue(plan.UseOrderDelay, "courier");

        AssertFalse(TacticalReserveCommitGate.TryPlanRelease(s, reserve, 2, out _), "no own line in that sector: no invented target");
        // The release band is the direct child's AxisSector, not its own PrimarySector.
        var intents = new[]
        {
            ChildIntent("child-" + LeftLine, DirectChildRole.Main, primarySector: 0, axisSector: 0),
            ChildIntent("child-" + Reserve, DirectChildRole.Fix, primarySector: 2, axisSector: leftSector),
        };
        AssertTrue(TacticalReserveCommitGate.TryReleasedDirectChild(intents, Reserve, out int band), "found");
        AssertEqual(leftSector, band, "crisis band");
        AssertFalse(TacticalReserveCommitGate.TryReleasedDirectChild(intents, Reserve + 1000, out _), "unknown child");
        AssertFalse(TacticalReserveCommitGate.TryReleasedDirectChild(new[] { ChildIntent("child-" + Reserve, DirectChildRole.Reserve, 0, 1) },
            Reserve, out _), "allocator still holds it in reserve");
        AssertFalse(TacticalReserveCommitGate.TryReleasedDirectChild(new[] { ChildIntent("child-" + Reserve + "0", DirectChildRole.Fix, 0, 1) },
            Reserve, out _), "prefix of a longer id is not a match");
        AssertTrue(TacticalReserveCommitGate.TryReleasedDirectChild(new[] { ChildIntent("synth-army--" + Reserve, DirectChildRole.Fix, 0, 3) },
            -Reserve, out int synth) && synth == 3, "synthetic army id, negative instance id");

        BattleSnapshot owned = ArmySnapshot(reserveFlags: UnitFlags.PlayerOwned);
        AssertFalse(TacticalReserveCommitGate.TryPlanRelease(owned, owned.IndexOfOwn(Reserve), leftSector, out _), "player-owned reserve");
        AssertTrue(TacticalReserveCommitGate.HeldInReserve(new CommandNodeOperationalState("node-1", CommandEchelonKind.DivisionLike,
            CommandNodeRole.Reserve, CommandTaskType.ReleaseReserve, CommandTaskState.Committed)), "reserve role holds");
        BattleSnapshot noAxis = ArmySnapshot(axis: false);
        AssertFalse(TacticalReserveCommitGate.TryPlanRelease(noAxis, noAxis.IndexOfOwn(Reserve), leftSector, out _), "no front axis");
    }

    private static void StalledFallbackReissueFromProducer()
    {
        CommandDoctrineOrder order = FallbackOrder(500f, -900f);
        BattleSnapshot stalled = ArmySnapshot(reserveFlags: UnitFlags.PathInterrupted);
        int reserve = stalled.IndexOfOwn(Reserve);
        AssertTrue(WithdrawalIntentProducer.TryPlanStalledFallback(stalled, reserve, order, out BattleOrderIntent move), "contact interrupted it short");
        AssertEqual(IntentPriority.Support, move.Priority, "a courier nudge, below #61's executor move");
        AssertEqual(IntentSource.Withdrawal, move.Source, "source");
        AssertEqual(CommandTaskType.FallBackToLine, move.Task, "task");
        AssertTrue((move.Flags & IntentFlags.UseOrderDelay) != 0, "courier, as the old #56 re-issue");
        AssertNear(500f, move.TargetX, 0.01f, "x");
        AssertNear(-900f, move.TargetZ, 0.01f, "z");

        AssertFalse(WithdrawalIntentProducer.TryPlanStalledFallback(ArmySnapshot(), reserve, order, out _),
            "standing short but not interrupted: #61's move owns it");
        AssertFalse(WithdrawalIntentProducer.TryPlanStalledFallback(ArmySnapshot(reserveFlags: UnitFlags.PathInterrupted, reserveQueued: 1),
            reserve, order, out _), "a courier order is already in flight");
        AssertFalse(WithdrawalIntentProducer.TryPlanStalledFallback(ArmySnapshot(reserveFlags: UnitFlags.PathInterrupted | UnitFlags.PlayerOwned),
            reserve, order, out _), "player-owned group: never re-issued");
        AssertFalse(WithdrawalIntentProducer.TryPlanStalledFallback(ArmySnapshot(reserveFlags: UnitFlags.PathInterrupted | UnitFlags.Feud),
            reserve, order, out _), "feuding group");
        AssertFalse(WithdrawalIntentProducer.TryPlanStalledFallback(stalled, reserve, FallbackOrder(500f, -830f), out _), "already there");
        AssertFalse(WithdrawalIntentProducer.TryPlanStalledFallback(stalled, reserve,
            TestFixtures.DoctrineOrder(CommandTaskType.FallBackToLine), out _), "no fallback target");
        AssertFalse(WithdrawalIntentProducer.TryPlanStalledFallback(stalled, reserve,
            TestFixtures.DoctrineOrder(CommandTaskType.ReserveWait, fallback: DoctrineTargetPoint.From(500f, -900f)), out _), "not a fallback task");
    }

    // ---------------------------------------------------------------- vanilla parity

    private static void VanillaMayCommitReserveMirrorsEarlyExits()
    {
        // CheckUseOfReserves 6084: null / inactive / not a group (unittyp <= 13) returns.
        AssertFalse(TacticalReserveCommitGate.GroupMayCommit(activeInHierarchy: false, groupUnitTyp: 15), "inactive");
        AssertFalse(TacticalReserveCommitGate.GroupMayCommit(activeInHierarchy: true, groupUnitTyp: 13), "regiment-level");
        AssertTrue(TacticalReserveCommitGate.GroupMayCommit(activeInHierarchy: true, groupUnitTyp: 14), "group");
        // 6093-6113: an endangered attached unit must exist (outflanked 1|5 / 2|6 on an uncovered side).
        AssertTrue(BattleWriterPolicy.IsEndangeredForReserveSupport(0, 0, false, 5, 0), "left (5) uncovered");
        AssertFalse(BattleWriterPolicy.IsEndangeredForReserveSupport(0, 0, false, 2, 2), "right covered");
        // 6121: the support unit vanilla may send.
        AssertTrue(BattleWriterPolicy.IsReserveSupportCandidate(1, 0, false, 0, false), "idle cavalry");
        AssertFalse(BattleWriterPolicy.IsReserveSupportCandidate(2, 0, false, 0, false), "artillery never");
    }

    private static void ReserveOddsReadSnapshotSlicePressure()
    {
        BattleSnapshot s = LeafSnapshot(morale: 1f);
        int leaf = s.IndexOfOwn(Leaf);
        s.SetSlicePressure(leaf, 0, 300f);
        s.SetSlicePressure(leaf, 9, 200f);
        AssertNear(500f, TacticalReserveCommitGate.VisibleAngleStrength(s, leaf), 0.01f, "sum of the fog-honest slices");
        AssertEqual(0f, TacticalReserveCommitGate.VisibleAngleStrength(s, -1), "unknown unit");
        AssertEqual(0f, TacticalReserveCommitGate.VisibleAngleStrength(null, leaf), "no snapshot: nothing seen");

        // #59's odds: own strength bucket over that visible pressure, nothing else.
        s.Own[leaf].GroupOwnInRange = 1000;
        s.Own[leaf].GroupStrengthAiGroup = 800;
        AssertNear(2f, TacticalReserveCommitGate.LocalOdds(s, leaf), 0.001f, "1000 / 500");
        AssertEqual(1f, TacticalReserveCommitGate.LocalOdds(LeafSnapshot(morale: 1f), leaf), "nothing visible: even");
    }

    private static void WithdrawalReadsOwnSideCoverAndRouts()
    {
        // CheckLineFallbacks 5167 / TacticalMoralePressure: outflanked + under fire +
        // low morale withdraws only without cover (covervalue <= 0 or coverobject 3).
        BattleSnapshot covered = LeafSnapshot(morale: 0.45f, flank: 50f, cover: 0.6f, coverObject: 1);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(covered, covered.IndexOfOwn(Leaf), Settings(), out _, out _), "in cover: holds");
        BattleSnapshot exposed = LeafSnapshot(morale: 0.45f, flank: 50f, cover: 0.6f, coverObject: 3);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.Fallback,
            WithdrawalIntentProducer.PlanUnit(exposed, exposed.IndexOfOwn(Leaf), Settings(), out _, out _), "coverobject 3 is no cover");

        // Under fire needs the range-list enemy to be visible: the same picture with that
        // enemy hidden (only remembered) and another visible one is not under fire,
        // so morale 0.45 above the 0.4 trigger holds.
        BattleSnapshot hidden = LeafSnapshot(morale: 0.45f, flank: 50f, rangeEnemyVisible: false);
        AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            WithdrawalIntentProducer.PlanUnit(hidden, hidden.IndexOfOwn(Leaf), Settings(), out _, out _), "hidden range enemy: not under fire");

        // friendlyroutednear is a morale penalty in [-1, 0] (128253): its magnitude is the pressure.
        AssertEqual(0.5f, WithdrawalIntentProducer.FriendlyRoutPressure(-0.5f), "magnitude");
        AssertEqual(0f, WithdrawalIntentProducer.FriendlyRoutPressure(0f), "none");
        AssertEqual(0f, WithdrawalIntentProducer.FriendlyRoutPressure(float.NaN), "non-finite");
    }

    private static void WithdrawalRetreatBearingMatchesVanillaRetreatAngle()
    {
        // Vanilla: retreatangle = Tools.GetAngle(own, enemy) = atan2(dx, dz) deg - 180
        // (151304, 122833); the point is GetAngleTerrainPosStatic(pos, angle, d):
        // x + cos(angle - 90) d, z - sin(angle - 90) d (26897-26906).
        float[,] enemies = { { 600f, -400f }, { 380f, -560f }, { 500f, -650f }, { 900f, -500f } };
        for (int e = 0; e < enemies.GetLength(0); e++)
        {
            float ex = enemies[e, 0], ez = enemies[e, 1];
            BattleSnapshot s = LeafSnapshot(morale: 0.3f, enemyVisible: false);
            s.AddObservation(new EnemyObservation { InstanceId = Enemy, X = ex, Z = ez, Strength = 500f, State = ObservationState.Visible, Confidence01 = 1f });
            s.SetClosestVisibleEnemy(1, 0, 1f);
            WithdrawalIntentProducer.PlanUnit(s, 1, Settings(), out BattleOrderIntent move, out _);

            double vanillaAngle = Math.Atan2(ex - 500f, ez - (-500f)) / Math.PI * 180.0 - 180.0;
            double a = (vanillaAngle - 90.0) * Math.PI / 180.0;
            float vx = 500f + (float)(Math.Cos(a) * 200f);
            float vz = -500f - (float)(Math.Sin(a) * 200f);
            AssertNear(vx, move.TargetX, 0.01f, "retreat x vs vanilla, enemy " + e);
            AssertNear(vz, move.TargetZ, 0.01f, "retreat z vs vanilla, enemy " + e);
        }
    }

    private static void SnapshotCarriesFallbackFieldsThroughAssembly()
    {
        var s = new BattleSnapshot();
        var scratch = new BattleSnapshotScratch();
        var own = new[]
        {
            new UnitRecord { InstanceId = Army, UnitTyp = 14, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup },
            new UnitRecord { InstanceId = Leaf, UnitTyp = 0, Strength = 400, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry },
        };
        int[][] attached = { new[] { Army, Leaf }, null };
        int[] counts = { 2, 0 };

        void Build(long tick, float cover, int coverObject, float routs, bool interrupted)
        {
            own[1].CoverValue = cover;
            own[1].CoverObject = coverObject;
            own[1].FriendlyRoutedNear = routs;
            own[1].Flags = interrupted ? own[1].Flags | UnitFlags.PathInterrupted : own[1].Flags & ~UnitFlags.PathInterrupted;
            s.BeginBuild(1, 0, 0, tick, 1.0);
            s.SetSliceCount(20);
            for (int i = 0; i < own.Length; i++) s.AddOwn(in own[i]);
            BattleSnapshotAssembler.AssembleCommandTree(s.Own, s.OwnCount, attached, counts, scratch);
            s.IndexCommandTree(scratch);
        }

        Build(1, 0.4f, 2, -0.25f, interrupted: true);
        int generation = scratch.TreeGeneration;
        int leaf = s.IndexOfOwn(Leaf);
        AssertEqual(0.4f, s.Own[leaf].CoverValue, "cover value");
        AssertEqual(2, s.Own[leaf].CoverObject, "cover object");
        AssertEqual(-0.25f, s.Own[leaf].FriendlyRoutedNear, "friendly routed near");
        AssertTrue((s.Own[leaf].Flags & UnitFlags.PathInterrupted) != 0, "path interrupted");
        AssertEqual(s.IndexOfOwn(Army), s.Own[leaf].ParentIndex, "parent kept");

        // The live fields change every tick; they are not roster: no tree rebuild, no allocation.
        Build(2, 0f, 0, 0f, interrupted: false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Build(3, 0.7f, 1, -1f, interrupted: true);
        AssertEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "same-roster rebuild allocated bytes");
        AssertEqual(generation, scratch.TreeGeneration, "live fields do not rebuild the tree");
        AssertEqual(0.7f, s.Own[leaf].CoverValue, "updated cover");
        AssertTrue((s.Own[leaf].Flags & UnitFlags.PathInterrupted) != 0, "updated flag");
    }

    // ---------------------------------------------------------------- helpers

    private static TacticalReserveCommitGate.Decision Gate(
        CommandIntentResolution resolution,
        bool released = false,
        bool playerControlled = false,
        bool vanillaCommitted = true,
        bool engaged = false)
    {
        return TacticalReserveCommitGate.Decide(new TacticalReserveCommitGate.Input(
            vanillaCommitted, resolution, playerControlled, committedUnitAlreadyEngaged: engaged,
            ownStrengthRatio: 1f, localOdds: 1f, reserveReleased: released));
    }

    private static TacticalReserveCommitGate.CourierParityAction Parity(
        OrderArbiter arbiter, in BattleOrderIntent intent,
        bool unitWritable = true, bool conversionApplies = true, bool useOrderDelays = true, int afterPaths = 1,
        bool inSnapshot = true)
    {
        return TacticalReserveCommitGate.PlanCourierParity(arbiter, intent, unitWritable, inSnapshot, conversionApplies,
            beforePaths: 0, afterPaths: afterPaths, beforeQueue: 0, afterQueue: 0, useOrderDelays: useOrderDelays);
    }

    private static CommandIntentResolution Resolved(DirectChildRole role)
    {
        return new CommandIntentResolution(true,
            new CommandNodeIntent("node-1", "node-1", role, DirectChildAxis.Hold, 0, 0, 0f, 0), "test");
    }

    private static DirectChildIntent ChildIntent(string childId, DirectChildRole role, int primarySector, int axisSector)
    {
        return new DirectChildIntent(childId, 15, 15, "test", primarySector, role, DirectChildAxis.SectorAxis, axisSector, 0.5f, 0.5f,
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()));
    }

    private static CommandIntentResolution NotFound() => new CommandIntentResolution(false, default, "no-side-orchestrator");

    private static CommandDoctrineOrder FallbackOrder(float x, float z)
    {
        return TestFixtures.DoctrineOrder(CommandTaskType.FallBackToLine, fallback: DoctrineTargetPoint.From(x, z));
    }

    private static WithdrawalIntentProducer.Settings Settings(ITerrainProbe terrain = null)
    {
        return new WithdrawalIntentProducer.Settings(
            fallbackDistance: 100f, retreatDistance: 200f, moraleTriggerByStance: new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0.4f },
            terrain: terrain, mapSizeX: 2000f, mapSizeZ: 2000f);
    }

    private static OrderExecutor NewExecutor(ITerrainProbe terrain)
    {
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), terrain);
        executor.SetMapSize(2000f, 2000f);
        return executor;
    }

    // A brigade (stance 1) with one infantry regiment at (500, -500) facing 45;
    // one enemy 100 m due north, in the regiment's range list.
    private static BattleSnapshot LeafSnapshot(
        float morale, float flank = 0f, int paths = 0, UnitFlags leafFlags = UnitFlags.None,
        bool enemyVisible = true, bool axis = true, int groupStance = 1, UnitFlags groupFlags = UnitFlags.None,
        float cover = 0f, int coverObject = 0, float friendlyRoutedNear = 0f, bool rangeEnemyVisible = true,
        float leafX = 500f, float leafZ = -500f, float waypointX = 0f, float waypointZ = 0f)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        s.AddOwn(new UnitRecord
        {
            InstanceId = Army, UnitTyp = 15, X = 500f, Z = -520f, AiStance = groupStance,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup | groupFlags,
        });
        s.AddOwn(new UnitRecord
        {
            CoverValue = cover, CoverObject = coverObject, FriendlyRoutedNear = friendlyRoutedNear,
            InstanceId = Leaf, UnitTyp = 0, X = leafX, Z = leafZ, LastWaypointX = waypointX, LastWaypointZ = waypointZ, RotationY = 45f, Strength = 400,
            Morale = morale, BattleStartMorale = 1f, Fatigue = 0.1f, RegimentPaths = paths, CombatBehaviorOrdered = 1,
            FirstEnemyInRangeId = Enemy,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | leafFlags,
        });
        s.Own[1].ParentIndex = 0;
        s.IndexCommandTree(null);
        if (enemyVisible)
        {
            // rangeEnemyVisible false: the range-list enemy is only remembered and a
            // different enemy at the same place is the visible one.
            s.AddObservation(new EnemyObservation
            {
                InstanceId = rangeEnemyVisible ? Enemy : 902, UnitTyp = 0, X = 500f, Z = -400f, Strength = 500f,
                State = ObservationState.Visible, Confidence01 = 1f,
            });
            if (!rangeEnemyVisible)
                s.AddObservation(new EnemyObservation
                {
                    InstanceId = Enemy, UnitTyp = 0, X = 500f, Z = -400f, Strength = 500f,
                    State = ObservationState.Remembered, Confidence01 = 1f,
                });
            s.SetClosestVisibleEnemy(1, 0, 100f);
        }
        if (flank > 0f) s.SetFlankPressure(1, 0, flank);
        if (axis) s.SetFrontAxis(TacticalFrontAxis.FromCentroids(500f, -510f, 500f, -200f).WithOwnExtent(-50f, 50f));
        return s;
    }

    // Army root with a reserve brigade behind two line brigades (left at x 300,
    // right at x 700), front facing +z.
    private static BattleSnapshot ArmySnapshot(bool axis = true, int reserveQueued = 0, UnitFlags reserveFlags = UnitFlags.None)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        const UnitFlags group = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup;
        s.AddOwn(new UnitRecord { InstanceId = Army, UnitTyp = 17, X = 500f, Z = -600f, Flags = group });
        s.AddOwn(new UnitRecord { InstanceId = Reserve, UnitTyp = 15, X = 500f, Z = -800f, QueuedOrders = reserveQueued, Flags = group | reserveFlags });
        s.AddOwn(new UnitRecord { InstanceId = LeftLine, UnitTyp = 15, X = 300f, Z = -500f, Flags = group });
        s.AddOwn(new UnitRecord { InstanceId = RightLine, UnitTyp = 15, X = 700f, Z = -500f, Flags = group });
        for (int i = 1; i < 4; i++) s.Own[i].ParentIndex = 0;
        s.IndexCommandTree(null);
        if (axis) s.SetFrontAxis(TacticalFrontAxis.FromCentroids(500f, -600f, 500f, -200f).WithOwnExtent(-300f, 300f));
        return s;
    }

    private sealed class AllowGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason)
        {
            reason = SkipReason.None;
            return true;
        }
    }

    private sealed class Terrain : ITerrainProbe
    {
        public float WaterBelowZ = float.MinValue;

        public bool IsWaterOrFence(float x, float z) => z < WaterBelowZ;
        public bool LineCrossesWater(float x0, float z0, float x1, float z1) => false;
        public bool StepBackFromRiver(float x0, float z0, ref float x1, ref float z1) => false;
    }
}
