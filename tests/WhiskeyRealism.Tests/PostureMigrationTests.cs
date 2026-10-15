using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;

// BC5a: #61 posture executor, facing pulse, fire control and division play as
// battle-core intent producers (docs/superpowers/plans/2026-10-15-battle-core.md § BC5a).
// The producers are runtime; these pin the pure decisions they call.
internal static class PostureMigrationTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("bc5a PostureProducerEmitsStanceMoveFormationForLedgerTask", PostureProducerEmitsStanceMoveFormationForLedgerTask),
            ("bc5a PostureProducerSubmitsNothingWithoutATarget", PostureProducerSubmitsNothingWithoutATarget),
            ("bc5a PostureProducerLeafBrigadeTargetFromCommandParentMap", PostureProducerLeafBrigadeTargetFromCommandParentMap),
            ("bc5a PostureProducerKeepsBattleNavMeshAreaCosts", PostureProducerKeepsBattleNavMeshAreaCosts),
            ("bc5a PostureProducerFacingPulseIsFacingChannelScorer", PostureProducerFacingPulseIsFacingChannelScorer),
            ("bc5a FireControlIsCombatBehaviorChannelExecutor", FireControlIsCombatBehaviorChannelExecutor),
            ("bc5a DivisionPlayLosesToExecutorOnSameChannel", DivisionPlayLosesToExecutorOnSameChannel),
            ("bc5a PlayCentroidsUseVisibleEnemiesOnly", PlayCentroidsUseVisibleEnemiesOnly),
            ("bc5a PlayRecomputeGateOnDoctrineClock", PlayRecomputeGateOnDoctrineClock),
            ("bc5a ArmyPostureInputsExcludePlayerOwnedNotAncestors", ArmyPostureInputsExcludePlayerOwnedNotAncestors),
            ("bc5a BlockedMovingOrderPathIsSkippedOutcome", BlockedMovingOrderPathIsSkippedOutcome),
            ("bc5a RotatableRegimentsFollowVanillaDetachedExclusion", RotatableRegimentsFollowVanillaDetachedExclusion),
            ("bc5a GroupFacingOnlyOnACleanSubtree", GroupFacingOnlyOnACleanSubtree),
            ("bc5a PostureSenseProxiesReadTheSnapshot", PostureSenseProxiesReadTheSnapshot),
            ("bc5a EnemyStrengthInRangeIsFogHonest", EnemyStrengthInRangeIsFogHonest),
            ("bc5a DivisionPlayInputFromTheSnapshot", DivisionPlayInputFromTheSnapshot),
            ("bc5a ExecutorOutcomeStampsDoctrineCooldowns", ExecutorOutcomeStampsDoctrineCooldowns),
            ("bc5a LeafAtItsSlotHasNoMove", LeafAtItsSlotHasNoMove),
            ("bc5a MovingSubordinateIsAnActiveMove", MovingSubordinateIsAnActiveMove),
            ("bc5a FireControlNeverTargetsArtillery", FireControlNeverTargetsArtillery),
            ("bc5a RecentOrderCooldownIsPerChannelWithShippedValues", RecentOrderCooldownIsPerChannelWithShippedValues),
            ("bc5a RecentOrderCooldownOverridesForSelfHoldingSources", RecentOrderCooldownOverridesForSelfHoldingSources),
            ("bc5a RefusedFlankTravelsOnTheIntentAndBypassesDedupe", RefusedFlankTravelsOnTheIntentAndBypassesDedupe),
            ("bc5a PostureRefusesTheThreatenedFlankInContact", PostureRefusesTheThreatenedFlankInContact),
            ("bc5a RotationWalkSkipsEveryUnitUnderADetachedNode", RotationWalkSkipsEveryUnitUnderADetachedNode),
            ("bc5a GroupFacingOnACleanSubtreeElsePerRegiment", GroupFacingOnACleanSubtreeElsePerRegiment),
        });
    }

    private const UnitFlags Live = UnitFlags.Alive | UnitFlags.Active;

    // ------------------------------------------------------------------ fixtures

    private sealed class Sink : IIntentSink
    {
        public readonly List<BattleOrderIntent> Items = new List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) { Items.Add(intent); }

        public BattleOrderIntent On(OrderChannel channel)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Channel == channel) return Items[i];
            throw new InvalidOperationException("no intent on " + channel);
        }

        public bool Has(OrderChannel channel)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Channel == channel) return true;
            return false;
        }
    }

    private static UnitRecord Group(int id, float x, float z, int typ = 15, UnitFlags flags = UnitFlags.None)
    {
        return new UnitRecord
        {
            InstanceId = id, UnitTyp = typ, X = x, Z = z, ParentIndex = -1, Formation = 0, CombatBehaviorOrdered = -1,
            Flags = Live | UnitFlags.IsGroup | flags,
        };
    }

    private static UnitRecord Regiment(int id, float x, float z, int typ = TacticalUnitType.Infantry, UnitFlags flags = UnitFlags.None)
    {
        UnitFlags type = typ == TacticalUnitType.Artillery ? UnitFlags.IsArtillery
            : typ == TacticalUnitType.Cavalry ? UnitFlags.IsCavalry
            : typ == TacticalUnitType.Infantry ? UnitFlags.IsInfantry : UnitFlags.None;
        return new UnitRecord
        {
            InstanceId = id, UnitTyp = typ, X = x, Z = z, ParentIndex = -1, Strength = 400, CombatBehaviorOrdered = -1,
            Flags = Live | type | flags,
        };
    }

    // Builds a snapshot: records in order, parents[i] is the own index of i's
    // command parent (-1 root), the way BattleSnapshotAssembler sets ParentIndex.
    private static BattleSnapshot Snapshot(UnitRecord[] own, int[] parents, double hours = 10.0)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, hours);
        for (int i = 0; i < own.Length; i++) s.AddOwn(in own[i]);
        for (int i = 0; i < own.Length; i++) s.Own[i].ParentIndex = parents[i];
        s.IndexCommandTree(null);
        return s;
    }

    private static EnemyObservation Enemy(int id, float x, float z, ObservationState state, float strength = 500f,
        float confidence = 1f, bool reinforcement = false, int typ = TacticalUnitType.Infantry)
    {
        return new EnemyObservation
        {
            InstanceId = id, X = x, Z = z, State = state, Strength = strength, Confidence01 = confidence,
            IsReinforcement = reinforcement, UnitTyp = typ,
        };
    }

    private static PostureIntentRequest Request(PostureExecutionAction action, CommandTaskType task,
        IntentPriority priority = IntentPriority.Executor, IntentSource source = IntentSource.PostureExecutor)
    {
        return new PostureIntentRequest
        {
            GroupId = 7, Action = action, Task = task, HasTarget = true, TargetX = 400f, TargetZ = -400f,
            TargetFormation = 1, NeedsFormation = true,
            Priority = priority, Source = source, Reason = PostureIntentPlan.ExecutorReason,
        };
    }

    // ------------------------------------------------------------------ tests

    private static void PostureProducerEmitsStanceMoveFormationForLedgerTask()
    {
        var sink = new Sink();
        int n = PostureIntentPlan.Submit(sink, Request(PostureExecutionAction.SetFormationAndWaypoint, CommandTaskType.AttackObjective));
        AssertEqual(3, n, "stance + move + formation");
        BattleOrderIntent stance = sink.On(OrderChannel.Stance);
        AssertEqual(TacticalCommandTaskToStanceMapper.Map(CommandTaskType.AttackObjective), stance.Value, "stance from the shared task mapping");
        BattleOrderIntent move = sink.On(OrderChannel.Move);
        AssertNear(400f, move.TargetX, 0.001f, "move x");
        AssertNear(-400f, move.TargetZ, 0.001f, "move z");
        AssertEqual(CommandTaskType.AttackObjective, move.Task, "move carries the task (executor task hold)");
        AssertTrue((move.Flags & IntentFlags.UseOrderDelay) != 0, "planned group move uses the courier delay (UpdateMovingTargets 6959)");
        AssertTrue((move.Flags & IntentFlags.InContactWithdrawal) == 0, "not a withdrawal");
        AssertEqual(1, sink.On(OrderChannel.Formation).Value, "formation value");
        for (int i = 0; i < sink.Items.Count; i++)
        {
            AssertEqual(IntentPriority.Executor, sink.Items[i].Priority, "executor priority");
            AssertEqual(IntentSource.PostureExecutor, sink.Items[i].Source, "posture source");
            AssertEqual(7, sink.Items[i].GroupId, "group");
        }

        var withdrawal = new Sink();
        PostureIntentRequest fallback = Request(PostureExecutionAction.FallbackToLine, CommandTaskType.FallBackToLine);
        fallback.InContactWithdrawal = true;
        AssertEqual(2, PostureIntentPlan.Submit(withdrawal, fallback), "fallback: stance + move, no formation");
        BattleOrderIntent retreat = withdrawal.On(OrderChannel.Move);
        AssertTrue((retreat.Flags & IntentFlags.InContactWithdrawal) != 0, "in-contact withdrawal flagged");
        AssertTrue((retreat.Flags & IntentFlags.UseOrderDelay) == 0, "in-contact withdrawal is immediate (CheckIfOutflanked 6235)");

        var hold = new Sink();
        AssertEqual(1, PostureIntentPlan.Submit(hold, Request(PostureExecutionAction.SetFormation, CommandTaskType.HoldObjective)), "hold: formation only");
        AssertTrue(hold.Has(OrderChannel.Formation), "formation intent");
        AssertFalse(hold.Has(OrderChannel.Move), "no move for a formation-only decision");

        var noWrite = new Sink();
        AssertEqual(0, PostureIntentPlan.Submit(noWrite, Request(PostureExecutionAction.NoWrite, CommandTaskType.AttackObjective)), "NoWrite submits nothing");
    }

    private static void PostureProducerSubmitsNothingWithoutATarget()
    {
        var sink = new Sink();
        PostureIntentRequest r = Request(PostureExecutionAction.SetFormationAndWaypoint, CommandTaskType.AttackObjective);
        r.HasTarget = false;
        AssertEqual(0, PostureIntentPlan.Submit(sink, r), "no target: no stance, no move, no formation (#61 ApplyDecision)");
        r.Action = PostureExecutionAction.SetWaypoint;
        AssertEqual(0, PostureIntentPlan.Submit(sink, r), "no target: no waypoint");
    }

    private static void PostureProducerLeafBrigadeTargetFromCommandParentMap()
    {
        // Division 10 (index 0) holds brigades 11 and 12; brigade 12 is listed first
        // but its command parent is the division, not the other brigade.
        var own = new[]
        {
            Group(10, 1000f, -1000f),
            Group(12, 1050f, -980f, typ: 14),
            Group(11, 900f, -1000f, typ: 14),
        };
        own[0].LastWaypointX = 1200f;
        own[0].LastWaypointZ = -1400f;
        var s = Snapshot(own, new[] { -1, 0, 0 });

        AssertTrue(LeafSlotTarget.TryResolve(s, 1, out int parent, out float x, out float z), "slot resolved");
        AssertEqual(0, parent, "parent from the snapshot's command-parent map");
        AssertNear(1250f, x, 0.001f, "parent's last waypoint + the leaf's offset (x)");
        AssertNear(-1380f, z, 0.001f, "parent's last waypoint + the leaf's offset (z)");

        var orphan = Snapshot(new[] { Group(20, 0f, -10f, typ: 14) }, new[] { -1 });
        AssertFalse(LeafSlotTarget.TryResolve(orphan, 0, out _, out _, out _), "root leaf: no parent, no slot");

        own[0].LastWaypointX = 0f;
        own[0].LastWaypointZ = 0f;
        var noWaypoint = Snapshot(own, new[] { -1, 0, 0 });
        AssertFalse(LeafSlotTarget.TryResolve(noWaypoint, 1, out _, out _, out _), "parent without a waypoint: no slot");
    }

    private static void PostureProducerKeepsBattleNavMeshAreaCosts()
    {
        // R5: Regiment.AddPath's battle costs, roads allowed, path not interrupted (130364-130394).
        AssertEqual(4, PostureNavAreaCosts.AreaNames.Length, "four areas set before every CalculatePath");
        AssertEqual(VanillaPathAreaCosts.RiversAreaName, PostureNavAreaCosts.AreaNames[0], "rivers");
        AssertEqual("Roads", PostureNavAreaCosts.AreaNames[1], "roads");
        AssertEqual("Bridges", PostureNavAreaCosts.AreaNames[2], "bridges");
        AssertEqual("FencesWalls, Sea (C)", PostureNavAreaCosts.AreaNames[3], "fences/walls area name");
        AssertEqual(500000f, PostureNavAreaCosts.Cost(0, frozen: false), "rivers impassable");
        AssertEqual(2f, PostureNavAreaCosts.Cost(0, frozen: true), "frozen rivers walkable");
        AssertEqual(1f, PostureNavAreaCosts.Cost(1, frozen: false), "roads 1");
        AssertEqual(1f, PostureNavAreaCosts.Cost(2, frozen: false), "bridges 1");
        AssertEqual(2f, PostureNavAreaCosts.Cost(3, frozen: false), "fences/walls 2");
    }

    private static void PostureProducerFacingPulseIsFacingChannelScorer()
    {
        BattleOrderIntent facing = PostureIntentPlan.FacingPulse(31, -90f);
        AssertEqual(OrderChannel.Facing, facing.Channel, "facing channel");
        AssertEqual(IntentPriority.Scorer, facing.Priority, "scorer priority (spec ladder)");
        AssertEqual(IntentSource.FacingPulse, facing.Source, "facing pulse source");
        AssertEqual(31, facing.GroupId, "regiment target");
        AssertNear(270f, facing.Heading, 0.001f, "heading normalized to [0, 360) (the executor skips negative headings)");
        AssertTrue(facing.TargetX == 0f && facing.TargetZ == 0f, "no point: no waypoint validation");

        BattleOrderIntent formation = PostureIntentPlan.FacingPulseFormation(30, 0);
        AssertEqual(OrderChannel.Formation, formation.Channel, "brigade pulse formation channel");
        AssertEqual(IntentPriority.Scorer, formation.Priority, "scorer priority");
        AssertEqual(IntentSource.FacingPulse, formation.Source, "facing pulse source");

        // A Scorer facing pulse never beats the posture executor's facing.
        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        arbiter.Submit(PostureIntentPlan.FacingPulse(30, 45f));
        var executorFacing = new Sink();
        var group = Snapshot(new[] { Group(30, 0f, -100f, typ: 14) }, new[] { -1 });
        PostureIntentPlan.SubmitFacing(executorFacing, group, 0, UnitFlags.None, 180f, CommandTaskType.HoldObjective,
            RefuseFlank.None, IntentPriority.Executor, IntentSource.PostureExecutor, PostureIntentPlan.ExecutorReason, new int[4], new int[4]);
        arbiter.Submit(executorFacing.On(OrderChannel.Facing));
        arbiter.Resolve(new BattleOrderIntent[4]);
        AssertTrue(arbiter.TryGetClaim(30, OrderChannel.Facing, out BattleOrderIntent claim), "facing claim");
        AssertEqual(IntentSource.PostureExecutor, claim.Source, "executor facing wins");
    }

    private static void FireControlIsCombatBehaviorChannelExecutor()
    {
        BattleOrderIntent fire = PostureIntentPlan.FireControl(41, TacticalFireControlDoctrine.InfantryShort);
        AssertEqual(OrderChannel.CombatBehavior, fire.Channel, "combat behavior channel");
        AssertEqual(IntentPriority.Executor, fire.Priority, "executor priority");
        AssertEqual(IntentSource.FireControl, fire.Source, "fire control source");
        AssertTrue((fire.Flags & IntentFlags.PerRegiment) != 0, "per regiment (D10: never on the group)");
        AssertEqual(TacticalFireControlDoctrine.InfantryShort, fire.Value, "behavior value");
        AssertEqual(41, fire.GroupId, "the regiment is the target");
    }

    private static void DivisionPlayLosesToExecutorOnSameChannel()
    {
        // D8: the spec ladder Executor > Play. Group 7 gets a posture move and a
        // division-play move; group 8 only a division-play move.
        var sink = new Sink();
        PostureIntentPlan.Submit(sink, Request(PostureExecutionAction.SetWaypoint, CommandTaskType.HoldObjective));
        PostureIntentRequest play = Request(PostureExecutionAction.SetFormationAndWaypoint, CommandTaskType.AttackObjective,
            IntentPriority.Play, IntentSource.DivisionPlay);
        play.TargetX = 900f;
        PostureIntentPlan.Submit(sink, play);
        PostureIntentRequest otherChild = play;
        otherChild.GroupId = 8;
        PostureIntentPlan.Submit(sink, otherChild);

        var arbiter = new OrderArbiter();
        arbiter.BeginTick();
        for (int i = 0; i < sink.Items.Count; i++) arbiter.Submit(sink.Items[i]);
        arbiter.Resolve(new BattleOrderIntent[16]);

        AssertTrue(arbiter.TryGetClaim(7, OrderChannel.Move, out BattleOrderIntent move7), "group 7 move");
        AssertEqual(IntentSource.PostureExecutor, move7.Source, "executor beats division play on Move");
        AssertNear(400f, move7.TargetX, 0.001f, "executor target");
        AssertTrue(arbiter.TryGetClaim(7, OrderChannel.Formation, out BattleOrderIntent formation7), "group 7 formation");
        AssertEqual(IntentSource.DivisionPlay, formation7.Source, "a channel the executor left empty goes to the play");
        AssertTrue(arbiter.TryGetClaim(8, OrderChannel.Move, out BattleOrderIntent move8), "group 8 move");
        AssertEqual(IntentSource.DivisionPlay, move8.Source, "division play wins where the executor submits nothing");
    }

    private static void PlayCentroidsUseVisibleEnemiesOnly()
    {
        var s = Snapshot(new[] { Group(1, 0f, -1000f) }, new[] { -1 });
        s.AddObservation(Enemy(100, 0f, -400f, ObservationState.Visible, strength: 1000f));
        s.AddObservation(Enemy(101, 400f, -400f, ObservationState.Visible, strength: 1000f));
        s.AddObservation(Enemy(102, 5000f, -5000f, ObservationState.Remembered, strength: 9000f));
        s.AddObservation(Enemy(103, -5000f, -5000f, ObservationState.Visible, strength: 9000f, reinforcement: true));

        PlayAnchor.EnemyAnchor(s, TacticalFrontAxis.None, 0f, -1000f, out float x, out float z);
        AssertNear(200f, x, 0.01f, "strength-weighted centroid of the visible on-map enemies (x)");
        AssertNear(-400f, z, 0.01f, "strength-weighted centroid of the visible on-map enemies (z)");

        var blind = Snapshot(new[] { Group(1, 0f, -1000f) }, new[] { -1 });
        blind.AddObservation(Enemy(102, 5000f, -5000f, ObservationState.Remembered, strength: 9000f));
        PlayAnchor.EnemyAnchor(blind, TacticalFrontAxis.None, 0f, -1000f, out x, out z);
        AssertNear(0f, x, 0.01f, "nothing visible, no axis: forward along +z (x)");
        AssertNear(-1000f + PlayProjection.DefaultEngagementDistanceMeters, z, 0.01f, "nothing visible, no axis: forward along +z (z)");

        TacticalFrontAxis axis = TacticalFrontAxis.FromCentroids(0f, -1000f, 600f, -1000f);
        PlayAnchor.EnemyAnchor(blind, axis, 0f, -1000f, out x, out z);
        AssertNear(600f, x, 0.5f, "nothing visible: project along the front axis (x)");
        AssertNear(-1000f, z, 0.5f, "nothing visible: project along the front axis (z)");

        var lists = (new List<float>(), new List<float>(), new List<CorpsPosition>());
        var own = Snapshot(new[]
        {
            Group(1, 0f, -1000f, typ: 16), Group(2, 100f, -1000f, typ: 15),
            Regiment(3, 200f, -1000f), Regiment(4, 9999f, -9999f, flags: UnitFlags.None),
        }, new[] { -1, 0, 1, 1 });
        own.Own[3].Flags &= ~UnitFlags.Active;
        int corps = PlayAnchor.CollectOwn(own, 15, lists.Item1, lists.Item2, lists.Item3);
        AssertEqual(3, lists.Item1.Count, "own centroid from the snapshot's active units only");
        AssertEqual(2, corps, "command-level units at or above the shifted command minimum");
        AssertEqual("child-1", lists.Item3[0].ChildId, "corps id format matches DirectChildDiscovery");
    }

    private static void PlayRecomputeGateOnDoctrineClock()
    {
        double interval = PlayAnchor.MinRecomputeHours;
        AssertNear(3f / 600f, (float)interval, 1e-6f, "3 real seconds at 1x on the doctrine clock");
        AssertTrue(PlayAnchor.RecomputeDue(hasCache: false, samePlan: true, lastHours: 10.0, nowHours: 10.0), "no cache: compute");
        AssertTrue(PlayAnchor.RecomputeDue(hasCache: true, samePlan: false, lastHours: 10.0, nowHours: 10.0), "plan changed: compute");
        AssertFalse(PlayAnchor.RecomputeDue(hasCache: true, samePlan: true, lastHours: 10.0, nowHours: 10.0 + interval * 0.5), "inside the window: reuse");
        AssertTrue(PlayAnchor.RecomputeDue(hasCache: true, samePlan: true, lastHours: 10.0, nowHours: 10.0 + interval), "window elapsed: compute");
        AssertFalse(PlayAnchor.RecomputeDue(hasCache: true, samePlan: true, lastHours: 10.0, nowHours: 10.0),
            "a frozen night clock does not force recomputes");
        AssertTrue(PlayAnchor.RecomputeDue(hasCache: true, samePlan: true, lastHours: 12.0, nowHours: 10.0), "clock went back (reload): compute");
    }

    private static TacticalCommandTreeProbe.ExtendedProbe Probe(int id, int parent, int typ = 14)
    {
        return new TacticalCommandTreeProbe.ExtendedProbe(id, typ, "u" + id, true, parent, parent != 0, 0f, 0f, 1);
    }

    private static void ArmyPostureInputsExcludePlayerOwnedNotAncestors()
    {
        // AI corps 1 → AI brigades 2, 3 and the W&L player's brigade 4 with its
        // regiments 5, 6. Corps 1 is the player's ancestor: it keeps its AI brigades.
        var own = new[]
        {
            Group(1, 0f, -10f, typ: 16, flags: UnitFlags.AncestorOfPlayerCommand),
            Group(2, 0f, -10f, typ: 14), Group(3, 0f, -10f, typ: 14),
            Group(4, 0f, -10f, typ: 14, flags: UnitFlags.PlayerOwned | UnitFlags.PlayerCommand),
            Regiment(5, 0f, -10f, flags: UnitFlags.PlayerOwned), Regiment(6, 0f, -10f, flags: UnitFlags.PlayerOwned),
        };
        var s = Snapshot(own, new[] { -1, 0, 0, 0, 3, 3 });
        var probes = new[] { Probe(1, 0, 16), Probe(2, 1), Probe(3, 1), Probe(4, 1), Probe(5, 4, 0), Probe(6, 4, 0) };

        var filtered = PostureProbeFilter.WithoutPlayerOwned(probes, s);
        AssertEqual(3, filtered.Count, "player-owned subtree removed");
        var tree = TacticalCommandTreeProbe.BuildTree(filtered, 0);
        AssertTrue(tree.ContainsKey(1), "the player's AI ancestor is kept (ancestors are not protected)");
        AssertEqual(2, tree[1].ChildInstanceIds.Count, "the ancestor keeps its AI brigades");
        AssertFalse(tree.ContainsKey(4), "the player's brigade gets no cascade role");

        var aiOnly = Snapshot(new[] { Group(1, 0f, -10f, typ: 16), Group(2, 0f, -10f, typ: 14) }, new[] { -1, 0 });
        var both = new[] { Probe(1, 0, 16), Probe(2, 1) };
        AssertTrue(ReferenceEquals(both, PostureProbeFilter.WithoutPlayerOwned(both, aiOnly)), "nothing player-owned: same list, no copy");
        AssertEqual(0, PostureProbeFilter.WithoutPlayerOwned(probes, null).Count, "no snapshot: no probes (nothing to vouch for)");
    }

    private static void BlockedMovingOrderPathIsSkippedOutcome()
    {
        // #61 queued a vanilla BlockedMovingOrder for attack moves over 150 m when
        // the battle had blocked-crossing data. That path is gone: the move is one
        // plain Move intent, and vanilla's own UpdateBlockedCrossings (7917)
        // handles crossings. A move vanilla would drop silently ends as a Skipped
        // outcome with its reason, never as a queued side order.
        var sink = new Sink();
        PostureIntentRequest far = Request(PostureExecutionAction.SetFormationAndWaypoint, CommandTaskType.AttackObjective);
        far.TargetX = 2000f;
        far.TargetZ = -2000f;
        PostureIntentPlan.Submit(sink, far);
        int moves = 0;
        for (int i = 0; i < sink.Items.Count; i++) if (sink.Items[i].Channel == OrderChannel.Move) moves++;
        AssertEqual(1, moves, "one plain move, whatever the distance");

        var own = new[] { Group(7, 0f, -100f, flags: UnitFlags.Garrisoned) };
        var s = Snapshot(own, new[] { -1 });
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        ExecutionPlan plan = executor.Plan(s, sink.On(OrderChannel.Move), 10.0);
        AssertFalse(plan.Execute, "not issued");
        AssertEqual(SkipReason.Garrison, plan.Skip, "skipped with the vanilla silent-exit reason (91331)");
    }

    private sealed class AllowGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason)
        {
            reason = SkipReason.None;
            return true;
        }
    }

    private static void RotatableRegimentsFollowVanillaDetachedExclusion()
    {
        // Brigade 1: regiments 2, 3 (3 is artillery), detached AI sub-group 4 with
        // regiment 5, a player-owned regiment 6 and a moving regiment 7.
        var own = new[]
        {
            Group(1, 0f, -100f, typ: 14),
            Regiment(2, 0f, -100f), Regiment(3, 0f, -100f, TacticalUnitType.Artillery),
            Group(4, 0f, -100f, typ: 14, flags: UnitFlags.PermanentlyDetached), Regiment(5, 0f, -100f),
            Regiment(6, 0f, -100f, flags: UnitFlags.PlayerOwned), Regiment(7, 0f, -100f),
        };
        own[6].RegimentPaths = 1;
        var s = Snapshot(own, new[] { -1, 0, 0, 0, 3, 0, 0 });
        var result = new int[8];
        int n = PostureSense.CollectRotatableRegiments(s, 0, new int[8], result);
        AssertEqual(1, n, "only regiment 2");
        AssertEqual(1, result[0], "own index of regiment 2");
    }

    private static void GroupFacingOnlyOnACleanSubtree()
    {
        AssertTrue(PostureSense.SubtreeAcceptsGroupFacing(UnitFlags.IsInfantry | Live), "plain AI subtree");
        AssertFalse(PostureSense.SubtreeAcceptsGroupFacing(UnitFlags.PlayerOwned), "a player-owned descendant");
        AssertFalse(PostureSense.SubtreeAcceptsGroupFacing(UnitFlags.PermanentlyDetached),
            "a detached descendant (vanilla group orders skip it, GetAttachedUnitsReg 119865)");
    }

    private static void PostureSenseProxiesReadTheSnapshot()
    {
        var own = new[]
        {
            Group(1, 0f, -1000f),
            Regiment(2, 0f, -1000f, flags: UnitFlags.InMelee),
            Group(3, 2000f, -1000f),
            Regiment(4, 2000f, -1000f),
        };
        own[3].RegimentPaths = 2;
        own[2].LastWaypointX = 2000f;
        own[2].LastWaypointZ = -1500f;
        var s = Snapshot(own, new[] { -1, 0, -1, 2 });
        UnitFlags[] desc = null;
        int[] moving = null;
        PostureSense.AggregateDescendants(s, ref desc, ref moving);

        AssertTrue((desc[0] & UnitFlags.InMelee) != 0, "descendant melee rolls up");
        AssertTrue(PostureSense.CloseEngaged(s, 0, desc[0]), "melee below: engaged");
        AssertFalse(PostureSense.CloseEngaged(s, 2, desc[2]), "nothing seen, no melee: not engaged");
        AssertEqual(1, moving[2], "one moving descendant");
        AssertTrue(PostureSense.ActiveMove(s.Own[2], moving[2]), "moving and far from its waypoint");
        AssertFalse(PostureSense.ActiveMove(s.Own[0], moving[0]), "nothing moving");

        s.AddObservation(Enemy(50, 2000f, -800f, ObservationState.Visible));
        s.SetClosestVisibleEnemy(2, 0, 200f);
        AssertTrue(PostureSense.CloseEngaged(s, 2, desc[2]), "a visible enemy within 250 m");
        s.SetClosestVisibleEnemy(2, 0, 400f);
        AssertFalse(PostureSense.CloseEngaged(s, 2, desc[2]), "a visible enemy beyond 250 m");
        AssertFalse(PostureSense.FlankRisk(s, 2), "no flank pressure");
        s.SetFlankPressure(2, 2, 10f);
        AssertTrue(PostureSense.FlankRisk(s, 2), "rear pressure is flank risk");

        CommandPhysicalState physical = PostureSense.Physical(s.Own[2], moving[2]);
        AssertFalse(physical.PathInterrupted, "the snapshot carries no pathinterrupted");
        AssertTrue(physical.ActiveMove, "active move proxy");
        AssertNear(0f, PostureSense.HeadingTo(0f, 0f, 0f, 10f), 0.001f, "north is 0");
        AssertNear(90f, PostureSense.HeadingTo(0f, 0f, 10f, 0f), 0.001f, "east is 90");
        AssertNear(270f, PostureSense.HeadingTo(0f, 0f, -10f, 0f), 0.001f, "west is 270, never negative");
    }

    private static void EnemyStrengthInRangeIsFogHonest()
    {
        var own = new[] { Group(1, 0f, -1000f), Regiment(2, 600f, -1000f) };
        var s = Snapshot(own, new[] { -1, 0 });
        s.AddObservation(Enemy(10, 0f, -1300f, ObservationState.Visible, strength: 400f));
        s.AddObservation(Enemy(11, 900f, -1000f, ObservationState.Remembered, strength: 400f, confidence: 0.5f));
        s.AddObservation(Enemy(12, 0f, -2000f, ObservationState.Visible, strength: 9000f));
        s.AddObservation(Enemy(13, 0f, -1100f, ObservationState.Visible, strength: 9000f, reinforcement: true));
        AssertNear(400f + 200f, PostureSense.EnemyStrengthInRange(s, 0, 350f), 0.01f,
            "visible + confidence-weighted remembered, within range of the group or a descendant; far and off-map excluded");
        AssertNear(0f, PostureSense.EnemyStrengthInRange(s, 0, 100f), 0.01f, "nothing that close");
        AssertNear(0f, PostureSense.EnemyStrengthInRange(s, 7, 350f), 0.01f, "bad index reads 0");
    }

    private static void DivisionPlayInputFromTheSnapshot()
    {
        var own = new[] { Group(1, 0f, -1000f), Group(2, 0f, -1000f, typ: 14), Regiment(3, 0f, -1000f, TacticalUnitType.Artillery) };
        own[1].Morale = 0.2f;
        own[1].QueuedOrders = 1;
        var s = Snapshot(own, new[] { -1, 0, 1 });
        s.AddObservation(Enemy(10, 0f, -1200f, ObservationState.Visible));
        s.SetClosestVisibleEnemy(1, 0, 200f);
        UnitFlags[] desc = null;
        int[] moving = null;
        PostureSense.AggregateDescendants(s, ref desc, ref moving);

        TacticalDivisionPlaySubordinate child = TacticalDivisionPlayInputs.FromSnapshot(
            s, 1, desc[1], moving[1], "node-2", CommandNodeRole.SupportingAttack, CommandTaskType.SupportAttack);
        AssertEqual("node-2", child.NodeId, "node id");
        AssertTrue(child.HasTargets, "visible enemy");
        AssertTrue(child.EngagingEnemy, "visible within close contact");
        AssertTrue(child.UnderCloseFire, "engaged");
        AssertTrue(child.InTrouble, "morale below 0.35");
        AssertTrue(child.IsArtillery, "an artillery descendant");
        AssertNear(200f, child.EnemyDistance, 0.01f, "closest visible distance");
        AssertTrue(child.HasOrders, "a queued courier order");
        AssertEqual(CommandTaskType.SupportAttack, child.CurrentTask, "ledger task");

        AssertEqual(-1, TacticalDivisionPlayInputs.ParentIndex(s, 0), "root: no division");
        AssertEqual(0, TacticalDivisionPlayInputs.ParentIndex(s, 1), "command parent from the snapshot map");
    }

    private static void ExecutorOutcomeStampsDoctrineCooldowns()
    {
        var own = new[] { Group(1, 0f, -1000f) };
        var s = Snapshot(own, new[] { -1 });
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        AssertFalse(PostureIntentPlan.IssuedWithin(executor, 1, OrderChannel.Facing, IntentSource.FacingPulse, 10.0, 0.1), "nothing issued yet");

        BattleOrderIntent pulse = PostureIntentPlan.FacingPulse(1, 90f);
        executor.RecordIssued(pulse, 10.0, null, 0, null, 0, 0, 0);
        AssertTrue(PostureIntentPlan.IssuedWithin(executor, 1, OrderChannel.Facing, IntentSource.FacingPulse, 10.05, 0.1), "inside the window");
        AssertFalse(PostureIntentPlan.IssuedWithin(executor, 1, OrderChannel.Facing, IntentSource.FacingPulse, 10.2, 0.1), "window over");
        AssertFalse(PostureIntentPlan.IssuedWithin(executor, 1, OrderChannel.Facing, IntentSource.PostureExecutor, 10.05, 0.1), "another source's write");
        AssertFalse(PostureIntentPlan.IssuedWithin(executor, 1, OrderChannel.Facing, IntentSource.FacingPulse, 9.0, 0.1), "clock went back: expired");
        AssertFalse(PostureIntentPlan.IssuedWithin(null, 1, OrderChannel.Facing, IntentSource.FacingPulse, 10.05, 0.1), "no executor");
    }

    private static void LeafAtItsSlotHasNoMove()
    {
        // The division arrived: its last waypoint is where it stands, so the leaf's
        // slot is the leaf's own position — no move onto itself (Failed/retry churn).
        var own = new[] { Group(10, 1000f, -1000f), Group(11, 1050f, -980f, typ: 14) };
        own[0].LastWaypointX = 1010f;
        own[0].LastWaypointZ = -1000f;
        var s = Snapshot(own, new[] { -1, 0 });
        AssertFalse(LeafSlotTarget.TryResolve(s, 1, out int parent, out _, out _), "slot within 60 m: no move");
        AssertEqual(0, parent, "the parent is still reported");
        AssertFalse(PostureSense.IsMoveAway(s.Own[1], 1080f, -980f), "30 m away is underfoot");
        AssertTrue(PostureSense.IsMoveAway(s.Own[1], 1150f, -980f), "100 m away is a move");
        AssertFalse(PostureSense.IsMoveAway(s.Own[1], 0f, 0f), "no point is no move");
    }

    private static void MovingSubordinateIsAnActiveMove()
    {
        // #61 IsExecutorMovementActive counted groupsubordinatesmoving: a brigade
        // whose regiment marches is moving even with its own waypoint underfoot.
        var own = new[] { Group(1, 0f, -1000f, typ: 14), Regiment(2, 0f, -1000f) };
        own[0].RegimentPaths = 1;
        own[0].LastWaypointX = 10f;
        own[0].LastWaypointZ = -1000f;
        own[1].RegimentPaths = 1;
        var s = Snapshot(own, new[] { -1, 0 });
        UnitFlags[] desc = null;
        int[] moving = null;
        PostureSense.AggregateDescendants(s, ref desc, ref moving);
        AssertEqual(2, moving[0], "group and regiment moving");
        AssertTrue(PostureSense.ActiveMove(s.Own[0], moving[0]), "a moving subordinate");
        AssertFalse(PostureSense.ActiveMove(s.Own[0], 1), "only its own path near its waypoint: settled");
    }

    private static void FireControlNeverTargetsArtillery()
    {
        AssertTrue(PostureSense.IsFireControlUnit(Regiment(1, 0f, -10f)), "infantry");
        AssertTrue(PostureSense.IsFireControlUnit(Regiment(2, 0f, -10f, TacticalUnitType.Cavalry)), "cavalry");
        AssertTrue(PostureSense.IsFireControlUnit(Regiment(3, 0f, -10f, TacticalUnitType.Skirmisher)), "skirmishers");
        AssertFalse(PostureSense.IsFireControlUnit(Regiment(4, 0f, -10f, TacticalUnitType.Artillery)),
            "a battery belongs to the artillery doctrine (BC5b)");
        AssertFalse(PostureSense.IsFireControlUnit(Regiment(5, 0f, -10f, flags: UnitFlags.IsArtillery)),
            "anything flagged artillery");
        AssertFalse(PostureSense.IsFireControlUnit(Group(6, 0f, -10f, typ: 14)), "never a group (D10)");
    }

    // Plans the intent on a one-group snapshot after the executor recorded the
    // same intent at t; true when the executor would issue it at t + seconds.
    private static bool IssuesAfter(BattleOrderIntent intent, UnitRecord rec, float seconds)
    {
        return IssuesAfter(intent, intent, rec, seconds);
    }

    // The recorded order, then another on the same (group, channel).
    private static bool IssuesAfter(BattleOrderIntent recorded, BattleOrderIntent intent, UnitRecord rec, float seconds)
    {
        var s = Snapshot(new[] { rec }, new[] { -1 });
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        executor.TaskHoldHours = 0f;
        executor.RecordIssued(recorded, 10.0, null, 0, null, 0, 0, 0);
        return executor.Plan(s, intent, 10.0 + seconds / 3600.0).Execute;
    }

    private static void RecentOrderCooldownIsPerChannelWithShippedValues()
    {
        // #61 RecentOrderHours = RealSecondsAtOneXToHours(30) = 0.05 h = 180 battle-s.
        AssertEqual(180f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.Move, IntentSource.PostureExecutor), "move");
        AssertEqual(180f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.Stance, IntentSource.PostureExecutor), "stance");
        // #61 RegimentFacingPulseCooldownHours = RealSecondsAtOneXToHours(5) = 5/600 h = 30 battle-s.
        AssertNear(WhiskeyRealism.Tactical.Battle.TacticalBattleClock.RealSecondsAtOneXToHours(5f) * 3600f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.Facing, IntentSource.FacingPulse), 0.01f, "facing");
        AssertNear(30f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.Facing, IntentSource.FacingPulse), 0.01f, "facing 30 s");
        // CommandFormationWritePolicy.CooldownHours 0.05 h (0.1 h under fire stays the policy's own rule).
        AssertNear(CommandFormationWritePolicy.CooldownHours * 3600f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.Formation, IntentSource.PostureExecutor), 0.01f, "formation");
        // #61 FireControlOrderCooldownHours = RealSecondsAtOneXToHours(45) = 0.075 h = 270 battle-s.
        AssertNear(WhiskeyRealism.Tactical.Battle.TacticalBattleClock.RealSecondsAtOneXToHours(45f) * 3600f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.CombatBehavior, IntentSource.FireControl), 0.01f, "combat behavior");

        UnitRecord group = Group(1, 0f, -1000f, typ: 14);
        UnitRecord regiment = Regiment(1, 0f, -1000f);
        BattleOrderIntent facing = PostureIntentPlan.FacingPulse(1, 90f);
        AssertFalse(IssuesAfter(facing, regiment, 25f), "facing inside 30 s");
        AssertTrue(IssuesAfter(facing, regiment, 35f), "facing after 30 s (not 180)");
        BattleOrderIntent formation = PostureIntentPlan.FacingPulseFormation(1, 2);
        AssertFalse(IssuesAfter(formation, group, 170f), "formation inside 180 s");
        AssertTrue(IssuesAfter(formation, group, 190f), "formation after 180 s");
        BattleOrderIntent fire = PostureIntentPlan.FireControl(1, TacticalFireControlDoctrine.InfantryShort);
        AssertFalse(IssuesAfter(fire, regiment, 260f), "fire control inside 270 s");
        AssertTrue(IssuesAfter(fire, regiment, 280f), "fire control after 270 s");
        // A new target (the pending move would dedupe the same one).
        BattleOrderIntent move = PostureIntentPlan.LeafMove(1, CommandTaskType.AttackObjective, 900f, -100f, false);
        BattleOrderIntent next = PostureIntentPlan.LeafMove(1, CommandTaskType.SupportAttack, 600f, -600f, false);
        AssertFalse(IssuesAfter(move, next, group, 170f), "move inside 180 s");
        AssertTrue(IssuesAfter(move, next, group, 190f), "move after 180 s");
    }

    private static void RefusedFlankTravelsOnTheIntentAndBypassesDedupe()
    {
        AssertEqual(-1, OrderExecutor.VanillaRefuseFlank(RefuseFlank.None), "none: SetGroupFormation refuseflank -1");
        AssertEqual(0, OrderExecutor.VanillaRefuseFlank(RefuseFlank.Left), "left: 0 (CommandFormationCorrection.RefuseFlankParameter)");
        AssertEqual(1, OrderExecutor.VanillaRefuseFlank(RefuseFlank.Right), "right: 1");
        AssertEqual(RefuseFlank.None, default(BattleOrderIntent).Refuse, "a default intent refuses nothing");

        var sink = new Sink();
        PostureIntentRequest r = Request(PostureExecutionAction.SetFormation, CommandTaskType.GuardFlank);
        r.NeedsFormation = false;
        r.TargetFormation = 0;
        r.Refuse = RefuseFlank.Right;
        AssertEqual(1, PostureIntentPlan.Submit(sink, r), "a refuse alone still writes the formation channel");
        BattleOrderIntent formation = sink.On(OrderChannel.Formation);
        AssertEqual(RefuseFlank.Right, formation.Refuse, "refuse carried");

        var own = new[] { Group(7, 0f, -1000f, typ: 14) };
        var s = Snapshot(own, new[] { -1 });
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        AssertTrue(executor.Plan(s, formation, 10.0).Execute, "same formation but a refuse: not a duplicate");
        BattleOrderIntent plain = formation;
        plain.Refuse = RefuseFlank.None;
        AssertEqual(SkipReason.Duplicate, executor.Plan(s, plain, 10.0).Skip, "same formation, no refuse: duplicate");
    }

    private static void PostureRefusesTheThreatenedFlankInContact()
    {
        // Facing north (0); the threat due east (90) is 90 degrees off: refuse right.
        var own = new[] { Group(1, 0f, -1000f, typ: 14) };
        var s = Snapshot(own, new[] { -1 });
        s.AddObservation(Enemy(9, 200f, -1000f, ObservationState.Visible));
        s.SetClosestVisibleEnemy(0, 0, 200f);
        AssertEqual(RefuseFlank.Right, PostureSense.RefuseFor(s, 0, CommandTaskType.GuardFlank, closeEngaged: true, flankRisk: true), "threat on the right");
        AssertEqual(RefuseFlank.None, PostureSense.RefuseFor(s, 0, CommandTaskType.GuardFlank, closeEngaged: true, flankRisk: false), "no flank risk");
        AssertEqual(RefuseFlank.None, PostureSense.RefuseFor(s, 0, CommandTaskType.GuardFlank, closeEngaged: false, flankRisk: true), "not in contact");
        AssertEqual(RefuseFlank.None, PostureSense.RefuseFor(s, 0, CommandTaskType.AttackObjective, closeEngaged: true, flankRisk: true), "an attack never skews");
        s.Own[0].RotationY = 90f;
        AssertEqual(RefuseFlank.None, PostureSense.RefuseFor(s, 0, CommandTaskType.GuardFlank, closeEngaged: true, flankRisk: true), "already facing it");
    }

    private static void RotationWalkSkipsEveryUnitUnderADetachedNode()
    {
        // Brigade 1 → detached sub-group 2 → nested group 3 → regiment 4; regiment 5
        // directly under 1. Vanilla's group order (GetAttachedUnitsReg
        // excludedechainedunits, 119865) never reaches 3 or 4.
        var own = new[]
        {
            Group(1, 0f, -100f, typ: 15),
            Group(2, 0f, -100f, typ: 14, flags: UnitFlags.PermanentlyDetached),
            Group(3, 0f, -100f, typ: 14), Regiment(4, 0f, -100f),
            Regiment(5, 0f, -100f),
        };
        var s = Snapshot(own, new[] { -1, 0, 1, 2, 0 });
        var result = new int[8];
        int n = PostureSense.CollectRotatableRegiments(s, 0, new int[8], result);
        AssertEqual(1, n, "only the regiment outside the detached subtree");
        AssertEqual(4, result[0], "regiment 5");
    }

    private static void GroupFacingOnACleanSubtreeElsePerRegiment()
    {
        var clean = Snapshot(new[] { Group(1, 0f, -100f, typ: 14), Regiment(2, 0f, -100f), Regiment(3, 0f, -100f) }, new[] { -1, 0, 0 });
        var sink = new Sink();
        int n = PostureIntentPlan.SubmitFacing(sink, clean, 0, UnitFlags.IsInfantry, 90f, CommandTaskType.HoldObjective,
            RefuseFlank.None, IntentPriority.Executor, IntentSource.PostureExecutor, PostureIntentPlan.ExecutorReason, new int[8], new int[8]);
        AssertEqual(1, n, "one group facing (the executor rotates or wheels as shipped)");
        AssertEqual(1, sink.Items[0].GroupId, "the group");

        var dirty = Snapshot(new[]
        {
            Group(1, 0f, -100f, typ: 15), Regiment(2, 0f, -100f),
            Group(3, 0f, -100f, typ: 14, flags: UnitFlags.PermanentlyDetached), Regiment(4, 0f, -100f),
        }, new[] { -1, 0, 0, 2 });
        var perRegiment = new Sink();
        n = PostureIntentPlan.SubmitFacing(perRegiment, dirty, 0, UnitFlags.PermanentlyDetached | UnitFlags.IsInfantry, 90f,
            CommandTaskType.HoldObjective, RefuseFlank.None, IntentPriority.Executor, IntentSource.PostureExecutor,
            PostureIntentPlan.ExecutorReason, new int[8], new int[8]);
        AssertEqual(1, n, "a detached descendant: face the reachable regiments one by one");
        AssertEqual(2, perRegiment.Items[0].GroupId, "regiment 2 only");
        AssertEqual(OrderChannel.Facing, perRegiment.Items[0].Channel, "facing channel");
        AssertEqual(IntentPriority.Executor, perRegiment.Items[0].Priority, "the caller's priority");
    }

    private static void RecentOrderCooldownOverridesForSelfHoldingSources()
    {
        // The artillery doctrine holds each battery itself (Plugin.ArtilleryAuditMinHoldSeconds,
        // doctrine clock): no executor window on its combat behavior, formation or facing.
        foreach (IntentSource artillery in new[] { IntentSource.ArtilleryDoctrine, IntentSource.ArtilleryPointBlank })
        {
            AssertEqual(0f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.CombatBehavior, artillery), artillery + " combat behavior");
            AssertEqual(0f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.Formation, artillery), artillery + " formation");
            AssertEqual(0f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.Facing, artillery), artillery + " facing");
            AssertEqual(180f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.Move, artillery), artillery + " move keeps the move window");
        }
        // Charge/revert is stateful (BC6a deferred disarm, refusal back-off): never wait 270 s to revert.
        AssertEqual(0f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.CombatBehavior, IntentSource.ChargeDoctrine), "charge doctrine");
        AssertEqual(270f, OrderExecutor.RecentOrderBattleSecondsFor(OrderChannel.CombatBehavior, IntentSource.FireControl), "fire control keeps 270 s");

        UnitRecord regiment = Regiment(1, 0f, -1000f, TacticalUnitType.Artillery);
        var battery = new BattleOrderIntent
        {
            GroupId = 1, Channel = OrderChannel.CombatBehavior, Value = 7, Heading = -1f, Flags = IntentFlags.PerRegiment,
            Priority = IntentPriority.Executor, Source = IntentSource.ArtilleryDoctrine, Reason = "test",
        };
        AssertTrue(IssuesAfter(battery, regiment, 1f), "an artillery doctrine change one second later is not held by the executor");
        BattleOrderIntent charge = battery;
        charge.Source = IntentSource.ChargeDoctrine;
        charge.Value = 3;
        AssertTrue(IssuesAfter(charge, Regiment(1, 0f, -1000f), 1f), "a charge revert one second later is not held");
        BattleOrderIntent fire = PostureIntentPlan.FireControl(1, TacticalFireControlDoctrine.InfantryShort);
        AssertFalse(IssuesAfter(fire, Regiment(1, 0f, -1000f), 1f), "fire control is held");
    }
}
