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
            ("reserve deny rolls back for doctrine and role reserve", ReserveDenyRollsBackForDoctrineAndRoleReserve),
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
        });
    }

    private const int Army = 100, Reserve = 101, LeftLine = 102, RightLine = 103, Leaf = 104, Enemy = 900;

    // ---------------------------------------------------------------- #59 + B8

    private static void ReserveDenyRollsBackForDoctrineAndRoleReserve()
    {
        // B8's doctrine deny (DoctrineConsumerDecisions.DecideReserve == Deny) now
        // denies inside #59 for any resolved role, and without a resolution.
        AssertEqual(TacticalReserveCommitGate.Action.Deny,
            Gate(Resolved(DirectChildRole.Main), doctrineDeny: true).Action, "doctrine deny on a main-role group");
        AssertEqual(TacticalReserveCommitGate.Action.Deny,
            Gate(NotFound(), doctrineDeny: true).Action, "doctrine deny without a command-node resolution");
        AssertEqual(TacticalReserveCommitGate.Action.Deny,
            Gate(Resolved(DirectChildRole.Reserve)).Action, "role reserve still holds");
        AssertEqual("doctrine-reserve-hold",
            Gate(Resolved(DirectChildRole.Main), doctrineDeny: true).Reason, "doctrine reason");

        // A reserve the allocator released goes, whatever the stale ledger doctrine says.
        AssertEqual(TacticalReserveCommitGate.Action.Allow,
            Gate(Resolved(DirectChildRole.Reserve), doctrineDeny: true, released: true).Action, "released beats stale doctrine");
        // Player-owned and no-vanilla-commit stay observe-only.
        AssertEqual(TacticalReserveCommitGate.Action.Observe,
            Gate(Resolved(DirectChildRole.Main), doctrineDeny: true, playerControlled: true).Action, "player");
        AssertEqual(TacticalReserveCommitGate.Action.Observe,
            Gate(Resolved(DirectChildRole.Main), doctrineDeny: true, vanillaCommitted: false).Action, "no vanilla commit");
        // No deny opinion: vanilla's commit stands.
        AssertEqual(TacticalReserveCommitGate.Action.Allow,
            Gate(Resolved(DirectChildRole.Main)).Action, "main, no doctrine deny");
        AssertEqual(TacticalReserveCommitGate.Action.Observe,
            Gate(NotFound()).Action, "nothing resolved, no doctrine");

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
            Parity(arbiter, intent, conversionApplies: false), "SetWaypoint would not queue it (91449/91463)");
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Leave,
            Parity(arbiter, intent, useOrderDelays: false), "order delays off");
        AssertEqual(TacticalReserveCommitGate.CourierParityAction.Leave,
            Parity(arbiter, intent, afterPaths: 0), "vanilla added no path (or #59 rolled it back)");
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
        BattleSnapshot stalled = ArmySnapshot();
        int reserve = stalled.IndexOfOwn(Reserve);
        AssertTrue(WithdrawalIntentProducer.TryPlanStalledFallback(stalled, reserve, order, out BattleOrderIntent move), "stopped short");
        AssertEqual(IntentPriority.Support, move.Priority, "a courier nudge, below #61's executor move");
        AssertEqual(IntentSource.Withdrawal, move.Source, "source");
        AssertEqual(CommandTaskType.FallBackToLine, move.Task, "task");
        AssertTrue((move.Flags & IntentFlags.UseOrderDelay) != 0, "courier, as the old #56 re-issue");
        AssertNear(500f, move.TargetX, 0.01f, "x");
        AssertNear(-900f, move.TargetZ, 0.01f, "z");

        AssertFalse(WithdrawalIntentProducer.TryPlanStalledFallback(ArmySnapshot(reservePaths: 1), reserve, order, out _), "still moving");
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
    }

    // ---------------------------------------------------------------- helpers

    private static TacticalReserveCommitGate.Decision Gate(
        CommandIntentResolution resolution,
        bool doctrineDeny = false,
        bool released = false,
        bool playerControlled = false,
        bool vanillaCommitted = true)
    {
        return TacticalReserveCommitGate.Decide(new TacticalReserveCommitGate.Input(
            vanillaCommitted, resolution, playerControlled, committedUnitAlreadyEngaged: false,
            ownStrengthRatio: 1f, localOdds: 1f, reserveReleased: released, doctrineDeny: doctrineDeny));
    }

    private static TacticalReserveCommitGate.CourierParityAction Parity(
        OrderArbiter arbiter, in BattleOrderIntent intent,
        bool unitWritable = true, bool conversionApplies = true, bool useOrderDelays = true, int afterPaths = 1)
    {
        return TacticalReserveCommitGate.PlanCourierParity(arbiter, intent, unitWritable, conversionApplies,
            beforePaths: 0, afterPaths: afterPaths, beforeQueue: 0, afterQueue: 0, useOrderDelays: useOrderDelays);
    }

    private static CommandIntentResolution Resolved(DirectChildRole role)
    {
        return new CommandIntentResolution(true,
            new CommandNodeIntent("node-1", "node-1", role, DirectChildAxis.Hold, 0, 0, 0f, 0), "test");
    }

    private static CommandIntentResolution NotFound() => new CommandIntentResolution(false, default, "no-side-orchestrator");

    private static CommandDoctrineOrder FallbackOrder(float x, float z)
    {
        return TestFixtures.DoctrineOrder(CommandTaskType.FallBackToLine, fallback: DoctrineTargetPoint.From(x, z));
    }

    private static WithdrawalIntentProducer.Settings Settings()
    {
        return new WithdrawalIntentProducer.Settings(
            fallbackDistance: 100f, retreatDistance: 200f, moraleTriggerByStance: new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0.4f });
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
        bool enemyVisible = true, bool axis = true, int groupStance = 1)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        s.AddOwn(new UnitRecord
        {
            InstanceId = Army, UnitTyp = 15, X = 500f, Z = -520f, AiStance = groupStance,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup,
        });
        s.AddOwn(new UnitRecord
        {
            InstanceId = Leaf, UnitTyp = 0, X = 500f, Z = -500f, RotationY = 45f, Strength = 400,
            Morale = morale, BattleStartMorale = 1f, Fatigue = 0.1f, RegimentPaths = paths, CombatBehaviorOrdered = 1,
            FirstEnemyInRangeId = Enemy,
            Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry | leafFlags,
        });
        s.Own[1].ParentIndex = 0;
        s.IndexCommandTree(null);
        if (enemyVisible)
        {
            s.AddObservation(new EnemyObservation
            {
                InstanceId = Enemy, UnitTyp = 0, X = 500f, Z = -400f, Strength = 500f,
                State = ObservationState.Visible, Confidence01 = 1f,
            });
            s.SetClosestVisibleEnemy(1, 0, 100f);
        }
        if (flank > 0f) s.SetFlankPressure(1, 0, flank);
        if (axis) s.SetFrontAxis(TacticalFrontAxis.FromCentroids(500f, -510f, 500f, -200f).WithOwnExtent(-50f, 50f));
        return s;
    }

    // Army root with a reserve brigade behind two line brigades (left at x 300,
    // right at x 700), front facing +z.
    private static BattleSnapshot ArmySnapshot(bool axis = true, int reservePaths = 0, UnitFlags reserveFlags = UnitFlags.None)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(20);
        const UnitFlags group = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup;
        s.AddOwn(new UnitRecord { InstanceId = Army, UnitTyp = 17, X = 500f, Z = -600f, Flags = group });
        s.AddOwn(new UnitRecord { InstanceId = Reserve, UnitTyp = 15, X = 500f, Z = -800f, RegimentPaths = reservePaths, Flags = group | reserveFlags });
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
