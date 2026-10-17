using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;

// verify r2 F7: a Defender's reserve is a real reserve on the field. A held reserve stands in a slot behind its holding command's line
// (DivisionSupportMeters back, on the own side of every river, inside the own deployment zone while deploying); a released one moves to its
// trigger point and returns to the slot when the trigger ends; neither flips with the picture; nothing touches the W&L player's command.
internal static class LeafReserveFieldTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("leaf reserve a held reserve stands in a slot behind the line, on lanes across the front", AHeldReserveStandsInASlotBehindTheLine),
            ("leaf reserve the slot stays on the own side of the river and inside the deployment zone", TheSlotStaysOnTheOwnSideAndInTheZone),
            ("leaf reserve no move in contact, at the slot, under an ordering parent or for the player's command", NoMoveInContactAtTheSlotOrForThePlayer),
            ("leaf reserve a released reserve goes to its trigger point and returns to its slot when released no more", AReleasedReserveGoesToItsTriggerAndReturns),
            ("leaf reserve no release move over a pending order and no slot move while the reserve is moving", NoMoveOverAPendingOrderOrWhileMoving),
            ("leaf reserve the release target line leaves out the reserves still held", TheReleaseLineLeavesOutHeldReserves),
            ("leaf reserve crossing guards keep their own binding and are not damped by the hysteresis", CrossingGuardsAreNotDamped),
            ("leaf reserve a new plan clears the army-level hysteresis", ANewPlanClearsTheArmyHysteresis),
            ("leaf reserve the moves go through the crossing gate", TheMovesGoThroughTheCrossingGate),
            ("leaf reserve an alternating picture neither releases nor returns a reserve", AnAlternatingPictureDoesNotThrash),
            ("leaf reserve the army-level release does not flip with an alternating picture either", TheArmyLevelReleaseDoesNotFlip),
            ("leaf reserve the release hysteresis: confirm, minimum hold, return margin, rejoin cooldown", TheHysteresisTimes),
            ("leaf reserve the release and the planner allocate nothing in the steady state", NothingIsAllocatedInTheSteadyState),
            ("leaf reserve the release rows are throttled per child and reason", TheReleaseRowsAreThrottled),
        });
    }

    private sealed class Sink : IIntentSink
    {
        public readonly List<BattleOrderIntent> Intents = new List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) => Intents.Add(intent);
        public BattleOrderIntent For(int group) => Intents.Single(i => i.GroupId == group);
        public bool Has(int group) => Intents.Any(i => i.GroupId == group);
    }

    private sealed class Zone : IOwnDeploymentZone
    {
        public bool Active { get; set; } = true;
        public float MaxX = float.MaxValue;
        public bool Fails;
        public bool TryClamp(float fromX, float fromZ, ref float x, ref float z)
        {
            if (Fails) return false;
            if (x > MaxX) x = MaxX;
            return true;
        }
    }

    // Two banks split at X: the river is the band [X - 10, X + 10).
    private sealed class Bank : ICrossingPicture
    {
        public float Boundary;
        public Bank(float boundary) { Boundary = boundary; }
        public bool Ready => true;
        public int RegionAt(float x, float z) => x < Boundary - 10f ? 0 : x < Boundary + 10f ? -1 : 1;
        public int LinkCount => 0;
        public int RegionsOfLink(int link, Span<int> regions) => 0;
        public void LinkPoint(int link, out float x, out float z) { x = z = 0f; }
        public CrossingState StateOf(int link) => CrossingState.Unknown;
        public bool IsPlanned(int link) => false;
        public void RequestLook(int link) { }
        public object Topology => this;
    }

    private sealed class BankGate : ICrossingGate
    {
        public readonly Bank Picture;
        public BankGate(Bank picture) { Picture = picture; }
        public CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, bool attack, bool road, out CrossingRoute route)
            => RiverCrossingRule.Decide(Picture, x0, z0, x1, z1, attack, road, out route);
    }

    // A division of six brigades across the front (facing +Z, lateral = x), x = 0 .. 500, standing on one line at LineZ; 103 and 105 held back.
    private const float LineZ = -1000f;
    private static readonly int[] Ids = { 101, 102, 103, 104, 105, 106 };

    private sealed class Field
    {
        public readonly ManeuverFeasibilityFixtures.TreeFixture Tree = new ManeuverFeasibilityFixtures.TreeFixture();
        public readonly int Division;
        public readonly int[] Brigade = new int[6];
        public DefenderReserveRecord Record;
        public readonly TacticalFrontAxis Axis = TacticalFrontAxis.FromCentroids(0f, LineZ, 0f, LineZ + 1000f).WithOwnExtent(0f, 500f, 5);

        public Field(Func<int, float> x = null, UnitFlags extraOn103 = 0)
        {
            Division = Tree.Add(TacticalUnitType.BattleGroupDivision, 250f, LineZ, -1);
            for (int b = 0; b < 6; b++)
            {
                float bx = x != null ? x(b) : 100f * b;
                Brigade[b] = Tree.Add(TacticalUnitType.BattleGroupBrigade, bx, LineZ, Division, b == 2 ? extraOn103 : UnitFlags.None, 500f);
            }
            var roles = new[]
            {
                DirectChildRole.RefuseLeft, DirectChildRole.SupportMain, DirectChildRole.Reserve, DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.RefuseRight,
            };
            Record = new DefenderReserveRecord(Tree.InstanceId(Division), "Division", 6, 2, DefenderReserveRecord.Reserved,
                Brigade.Select(i => Tree.InstanceId(i)).ToArray(), roles, new int[6], Enumerable.Range(0, 6).Select(b => 100f * b).ToArray(),
                Enumerable.Repeat(LineZ, 6).ToArray());
        }

        public int Id(int b) => Tree.InstanceId(Brigade[b]);

        public BattleSnapshot Snapshot(params EnemyObservation[] enemies)
        {
            BattleSnapshot s = Tree.Build(10.0, enemies);
            s.SetFrontAxis(Axis);
            return s;
        }

        public int Produce(BattleSnapshot s, Sink sink, IReadOnlyDictionary<int, DirectChildRole> released = null, IReadOnlyList<LeafRelease> releases = null,
            ICrossingPicture picture = null, IOwnDeploymentZone zone = null, List<LeafReserveMove> moves = null)
        {
            UnitFlags[] below = null;
            int[] moving = null;
            PostureSense.AggregateDescendants(s, ref below, ref moving);
            return LeafReservePlanner.Produce(s, new[] { Record }, released ?? new Dictionary<int, DirectChildRole>(), releases ?? new List<LeafRelease>(), below, moving,
                picture, zone, sink, moves);
        }
    }

    private static void AHeldReserveStandsInASlotBehindTheLine()
    {
        var f = new Field();
        var sink = new Sink();
        var moves = new List<LeafReserveMove>();
        AssertEqual(2, f.Produce(f.Snapshot(), sink, moves: moves), "both held reserves are sent to their slots");
        // The line is the four standing brigades (x 0, 100, 300, 500: centre 225); the slot is DivisionSupportMeters behind it, on lanes of +-ReserveLaneMeters.
        float lane = DeploymentDoctrine.ReserveLaneMeters;
        BattleOrderIntent a = sink.For(f.Id(2)), b = sink.For(f.Id(4));
        AssertNear(225f - lane, a.TargetX, 0.01f, "the left reserve's lane");
        AssertNear(225f + lane, b.TargetX, 0.01f, "the right reserve's lane");
        AssertNear(LineZ - DeploymentDoctrine.DivisionSupportMeters, a.TargetZ, 0.01f, "behind the line by DivisionSupportMeters");
        AssertNear(LineZ - DeploymentDoctrine.DivisionSupportMeters, b.TargetZ, 0.01f, "behind the line by DivisionSupportMeters");
        AssertEqual(200f, DeploymentDoctrine.DivisionSupportMeters, "the one constant BC16 reads");
        AssertNear(0f, a.Heading, 0.01f, "facing the front");
        AssertEqual(IntentSource.ReserveRelease, a.Source, "the release producer's source: the crossing gate sees it");
        AssertEqual(CommandTaskType.ReserveWait, a.Task, "a reserve waits");
        AssertEqual(LeafReservePlanner.SlotReason, a.Reason, "named");
        AssertEqual(IntentPriority.Support, a.Priority, "support");
        AssertTrue(moves.All(m => m.Kind == LeafReserveMoveKind.Slot) && moves.Count == 2, "and the trace says slot");

        // One reserve alone stands on the centre lane.
        var single = new Field();
        single.Record = new DefenderReserveRecord(single.Record.ParentInstanceId, "Division", 6, 1, DefenderReserveRecord.Reserved, single.Record.ChildIds,
            new[] { DirectChildRole.RefuseLeft, DirectChildRole.SupportMain, DirectChildRole.Reserve, DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.RefuseRight },
            new int[6], single.Record.WorldX, single.Record.WorldZ);
        var one = new Sink();
        single.Produce(single.Snapshot(), one);
        AssertEqual(1, one.Intents.Count, "one held reserve");
        AssertNear((0f + 100f + 300f + 400f + 500f) / 5f, one.Intents[0].TargetX, 0.01f, "on the centre lane of the standing line (five brigades here)");
    }

    private static void TheSlotStaysOnTheOwnSideAndInTheZone()
    {
        var f = new Field();
        // The river lies at x = 300: the line's centre (225) is on the west bank, the right reserve's slot (315) would be across it.
        var sink = new Sink();
        f.Produce(f.Snapshot(), sink, picture: new Bank(300f));
        float right = sink.For(f.Id(4)).TargetX;
        AssertTrue(right < 290f, "the slot is pulled back to the own bank: " + right);
        AssertNear(135f, sink.For(f.Id(2)).TargetX, 0.01f, "the other reserve's slot was already on the own bank");

        // The line's centre on the river itself: no land to stand on, no slot.
        var none = new Sink();
        AssertEqual(0, f.Produce(f.Snapshot(), none, picture: new Bank(225f)), "no slot when the line's own ground is not land");

        // Deployment: inside the zone.
        var zoned = new Sink();
        f.Produce(f.Snapshot(), zoned, zone: new Zone { MaxX = 150f });
        AssertTrue(zoned.For(f.Id(4)).TargetX <= 150f, "the slot is clamped into the own deployment zone");
        var failed = new Sink();
        AssertEqual(0, f.Produce(f.Snapshot(), failed, zone: new Zone { Fails = true }), "no zone point, no slot");
        var inactive = new Sink();
        f.Produce(f.Snapshot(), inactive, zone: new Zone { Active = false, Fails = true });
        AssertEqual(2, inactive.Intents.Count, "outside the deployment phase the zone is not asked");
    }

    private static void NoMoveInContactAtTheSlotOrForThePlayer()
    {
        var f = new Field();
        var enemy = new EnemyObservation { InstanceId = 9000, X = 200f, Z = LineZ + 120f, Strength = 800f, State = ObservationState.Visible, Confidence01 = 1f };
        BattleSnapshot contact = f.Snapshot(enemy);
        contact.SetClosestVisibleEnemy(f.Brigade[2], 0, 120f);
        var sink = new Sink();
        f.Produce(contact, sink);
        AssertFalse(sink.Has(f.Id(2)), "a reserve in contact is not moved");
        AssertTrue(sink.Has(f.Id(4)), "the other one is");

        // At its slot already (within the dedupe distance): nothing to send.
        var atSlot = new Field();
        BattleSnapshot s = atSlot.Snapshot();
        s.Own[atSlot.Brigade[2]].X = 225f - DeploymentDoctrine.ReserveLaneMeters + 20f;
        s.Own[atSlot.Brigade[2]].Z = LineZ - DeploymentDoctrine.DivisionSupportMeters;
        var quiet = new Sink();
        atSlot.Produce(s, quiet);
        AssertFalse(quiet.Has(atSlot.Id(2)), "no move for a reserve that stands within the dedupe distance of its slot");

        // The holding command itself has an order in flight: vanilla re-forms its subordinates with it.
        var ordered = new Field();
        BattleSnapshot o = ordered.Snapshot();
        o.Own[ordered.Division].RegimentPaths = 1;
        var held = new Sink();
        AssertEqual(0, ordered.Produce(o, held), "no slot move under a parent with an order in flight");

        // The W&L player's command: nothing moves it, nor a reserve that holds it.
        var player = new Field(extraOn103: UnitFlags.PlayerOwned);
        var playerSink = new Sink();
        player.Produce(player.Snapshot(), playerSink);
        AssertFalse(playerSink.Has(player.Id(2)), "a player-owned reserve is never moved");
        var commandUnder = new Field();
        int regiment = commandUnder.Tree.Add(TacticalUnitType.Infantry, 400f, LineZ, commandUnder.Brigade[4], UnitFlags.PlayerCommand, 300f);
        var commandSink = new Sink();
        commandUnder.Produce(commandUnder.Snapshot(), commandSink);
        AssertFalse(commandSink.Has(commandUnder.Id(4)), "a reserve that holds the player's command is never moved");
        AssertTrue(commandSink.Has(commandUnder.Id(2)), "the other reserve still is");
        AssertTrue(regiment > 0, "(fixture)");
    }

    private static LeafRelease Release(Field f, int brigade, DirectChildRole role, int sector, string reason = "flank-threat")
        => new LeafRelease(f.Record.ParentInstanceId, f.Id(brigade), role, reason, 1, 0, sector);

    private static void AReleasedReserveGoesToItsTriggerAndReturns()
    {
        var f = new Field();
        BattleSnapshot s = f.Snapshot();
        int wing = f.Axis.SectorOf(500f, LineZ);
        var released = new Dictionary<int, DirectChildRole> { { f.Id(4), DirectChildRole.RefuseRight } };
        var releases = new List<LeafRelease> { Release(f, 4, DirectChildRole.RefuseRight, wing) };
        var sink = new Sink();
        var moves = new List<LeafReserveMove>();
        f.Produce(s, sink, released, releases, moves: moves);

        // The exposed wing's end: the standing line of that band (brigade 106 at x = 500), stood off behind it.
        BattleOrderIntent go = sink.For(f.Id(4));
        AssertNear(500f, go.TargetX, 0.01f, "to the wing the perceived threat is on");
        AssertNear(LineZ - TacticalReserveCommitGate.ReleaseStandOffMeters, go.TargetZ, 0.01f, "stood off behind its line");
        AssertEqual(CommandTaskType.ReleaseReserve, go.Task, "a release");
        AssertEqual(LeafReservePlanner.ReleaseReason, go.Reason, "named");
        AssertEqual(IntentSource.ReserveRelease, go.Source, "through the release source, so the crossing gate sees it");
        AssertTrue(moves.Any(m => m.Kind == LeafReserveMoveKind.Release && m.Sector == wing && m.Role == DirectChildRole.RefuseRight), "and the trace says release, band and role");
        // The other reserve is still held: alone now, on the centre lane of the line (the released one is not part of the line).
        AssertNear(225f, sink.For(f.Id(2)).TargetX, 0.01f, "the reserve still held takes the centre lane");

        // The crisis band: the standing line there.
        var crisis = new Sink();
        int band = f.Axis.SectorOf(100f, LineZ);
        f.Produce(s, crisis, new Dictionary<int, DirectChildRole> { { f.Id(2), DirectChildRole.Fix } }, new List<LeafRelease> { Release(f, 2, DirectChildRole.Fix, band, "crisis") });
        AssertNear(100f, crisis.For(f.Id(2)).TargetX, 0.01f, "a crisis reserve goes to the line of the crisis band");

        // No standing line in the band: no invented target.
        var empty = new Sink();
        f.Produce(s, empty, new Dictionary<int, DirectChildRole> { { f.Id(2), DirectChildRole.Fix } }, new List<LeafRelease> { Release(f, 2, DirectChildRole.Fix, 9, "crisis") });
        AssertFalse(empty.Has(f.Id(2)), "a band with no standing line gets no move");

        // The release ends: the reserve, wherever it stands, is sent back to its slot.
        BattleSnapshot out_ = f.Snapshot();
        out_.Own[f.Brigade[4]].X = 500f;
        out_.Own[f.Brigade[4]].Z = LineZ - TacticalReserveCommitGate.ReleaseStandOffMeters;
        var back = new Sink();
        f.Produce(out_, back);
        AssertNear(225f + DeploymentDoctrine.ReserveLaneMeters, back.For(f.Id(4)).TargetX, 0.01f, "back to its slot behind the line");
        AssertNear(LineZ - DeploymentDoctrine.DivisionSupportMeters, back.For(f.Id(4)).TargetZ, 0.01f, "back to its slot behind the line");
        AssertEqual(CommandTaskType.ReserveWait, back.For(f.Id(4)).Task, "a reserve waits again");
    }

    // verify r3 X09 / X15: the gates of the leaf pass hold for the release as well (pending order) and for the slot (already moving).
    private static void NoMoveOverAPendingOrderOrWhileMoving()
    {
        var f = new Field();
        int band = f.Axis.SectorOf(100f, LineZ);
        var released = new Dictionary<int, DirectChildRole> { { f.Id(2), DirectChildRole.Fix } };
        var releases = new List<LeafRelease> { Release(f, 2, DirectChildRole.Fix, band, "crisis") };

        var ready = new Sink();
        f.Produce(f.Snapshot(), ready, released, releases);
        AssertTrue(ready.Has(f.Id(2)), "(control) the release is sent when nothing is pending");

        BattleSnapshot pending = f.Snapshot();
        pending.Own[f.Brigade[2]].QueuedOrders = 1;
        var held = new Sink();
        f.Produce(pending, held, released, releases);
        AssertFalse(held.Has(f.Id(2)), "no release move over an order still in the courier queue");
        var slot = new Sink();
        f.Produce(pending, slot);
        AssertFalse(slot.Has(f.Id(2)), "nor a slot move");
        AssertTrue(slot.Has(f.Id(4)), "(the other reserve is unaffected)");

        // A reserve that is already moving is not sent a slot. The holding command is out of the picture (no parent known) so that only
        // the reserve's own movement can stop it; with its parent known, the moving subtree also marks the parent as ordered.
        BattleSnapshot moving = f.Snapshot();
        moving.Own[f.Brigade[2]].RegimentPaths = 1;
        moving.Own[f.Brigade[2]].MovementMode = 1;
        moving.Own[f.Brigade[2]].ParentIndex = -1;
        var walking = new Sink();
        f.Produce(moving, walking);
        AssertFalse(walking.Has(f.Id(2)), "no slot move for a reserve that is already moving");
        BattleSnapshot underParent = f.Snapshot();
        underParent.Own[f.Brigade[2]].RegimentPaths = 1;
        underParent.Own[f.Brigade[2]].MovementMode = 1;
        var parented = new Sink();
        f.Produce(underParent, parented);
        AssertFalse(parented.Has(f.Id(2)), "nor under its parent");
    }

    // verify r3 X08: the line a release goes to is the standing line; a reserve still held in that band is not part of it.
    private static void TheReleaseLineLeavesOutHeldReserves()
    {
        var f = new Field();
        BattleSnapshot s = f.Snapshot();
        int reserveBand = f.Axis.SectorOf(200f, LineZ);      // brigade 103 (held) stands alone in this band
        var released = new Dictionary<int, DirectChildRole> { { f.Id(4), DirectChildRole.Fix } };
        var none = new Sink();
        f.Produce(s, none, released, new List<LeafRelease> { Release(f, 4, DirectChildRole.Fix, reserveBand, "crisis") });
        AssertFalse(none.Has(f.Id(4)), "a band whose only brigade is a held reserve has no line to go to");
        var line = new Sink();
        f.Produce(s, line, released, new List<LeafRelease> { Release(f, 4, DirectChildRole.Fix, f.Axis.SectorOf(100f, LineZ), "crisis") });
        AssertNear(100f, line.For(f.Id(4)).TargetX, 0.01f, "(control) the band with a standing brigade is the target");
    }

    // verify r3 X14: a reserve sent to guard a crossing keeps the crossing release's own binding; the hysteresis does not hold it back.
    private static void CrossingGuardsAreNotDamped()
    {
        var snapshots = new[]
        {
            new DirectChildSnapshot("c0", "", 15, 0, "A", true), new DirectChildSnapshot("c1", "", 15, 0, "B", true), new DirectChildSnapshot("c2", "", 15, 0, "C", true),
        };
        var evidence = new[] { new DirectChildEvidence(1, 0, false, 0, 0, 0.9f), new DirectChildEvidence(1, 0, false, 0, 0, 0.9f), new DirectChildEvidence(1, 0, false, 0, 0, 0.9f) };
        var plan = new TacticalBattlePlan(BattlePlanId.PreparedDefense, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7);
        var threat = new CrossingThreat { Link = 3, X = 100f, Z = 200f, Left = true, Sector = 0, GuardX = 10f, GuardZ = 20f, GuardHeading = 90f, EnemyAtCrossing = 0.4f, EnemyAtFlank = 0.7f, CandidateOwnShift = 9f };
        var shifts = new[] { 9f, 0.2f, 0.9f };
        var state = new ReserveReleaseState();
        var context = new ReserveReleaseContext(0.5f, false, state, null, 10.0, 0.5f, 0.5f, 0.5f, false, new[] { threat }, 1, shifts, 3, new ReleaseHysteresis());
        var roles = new[] { DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.Reserve };
        var guards = new GuardPoint[3];
        DirectChildAllocator.ReleaseReservesInto(plan, evidence, roles, context, snapshots, guards, new int[3]);
        AssertEqual(DirectChildRole.RefuseLeft, roles[1], "the soonest reserve guards the crossing at once, with the hysteresis in the context");
        AssertTrue(guards[1].HasValue, "with its guard point");
        AssertEqual(DirectChildRole.Reserve, roles[2], "the other stays");
        AssertTrue(state.TryCrossingOf("c1", out _), "and the binding is the crossing release's own");
    }

    // verify r3 X13: a re-picked plan starts the releases over, the army-level hysteresis with them.
    private static void ANewPlanClearsTheArmyHysteresis()
    {
        var snap = new[] { new DirectChildSnapshot("a", "", 15, 0, "A", true), new DirectChildSnapshot("b", "", 15, 0, "B", true), new DirectChildSnapshot("reserve", "", 15, 0, "R", true) };
        var orch = new ArmyOrchestrator(0, BuiltInPlaybooks.SeedCatalog(), default(WhiskeyRealism.Strategic.PersonalityVector));
        orch.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.GenericMethodical, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7));
        orch.RegisterDirectChildren(snap);
        double clock = 5.0;
        orch.HoursSource = () => clock;
        orch.ObserveGlobalOdds(1.5f);
        DirectChildEvidence Ev(int sector, int own) => new DirectChildEvidence(own, 1, false, sector, 0, 0.9f);
        for (int k = 0; k < 4; k++)
        {
            orch.ObserveDirectChildEvidenceWithZones(new[] { Ev(0, 3), Ev(1, 1), Ev(3, 3) }, null, null);
            clock += 0.02;
        }
        var field = typeof(ArmyOrchestrator).GetField("_armyReleaseHysteresis", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var hysteresis = (ReleaseHysteresis)field.GetValue(orch);
        AssertEqual(DirectChildRole.SupportMain, orch.GetDirectChildRole("reserve"), "(fixture) the reserve is released at the commit odds");
        AssertTrue(hysteresis.ActiveCount > 0, "(fixture) and the hysteresis holds it");
        orch.Replan(new ArmyEvidence(0.8f, TerrainKind.Open, 1));
        AssertEqual(0, hysteresis.ActiveCount, "a new plan clears the army-level hysteresis");
    }

    private static void TheMovesGoThroughTheCrossingGate()
    {
        var f = new Field();
        var bank = new Bank(1000f);               // everything west of x = 990 is the own bank
        var gate = new BankGate(bank);
        var sink = new Sink();
        f.Produce(f.Snapshot(), sink);
        BattleSnapshot s = f.Snapshot();
        OrderExecutor executor = RiverCrossingTests.Executor(gate, 2500f);
        ExecutionPlan plan = executor.Plan(s, sink.For(f.Id(2)), 1.0);
        AssertTrue(plan.Execute, "a slot on the own bank is made: " + plan.Skip);

        // A release whose target lies across the river with no crossing known is not made: the reserve stands on the east bank, the line it is sent to on the west.
        var across = new Field(x: b => b == 2 ? 1100f : 100f * b);
        BattleSnapshot cs = across.Snapshot();
        var go = new Sink();
        int band = across.Axis.SectorOf(500f, LineZ);
        across.Produce(cs, go, new Dictionary<int, DirectChildRole> { { across.Id(2), DirectChildRole.Fix } },
            new List<LeafRelease> { Release(across, 2, DirectChildRole.Fix, band, "crisis") });
        AssertTrue(go.Has(across.Id(2)), "(fixture) the release has a target");
        ExecutionPlan refused = RiverCrossingTests.Executor(gate, 2500f).Plan(cs, go.For(across.Id(2)), 1.0);
        AssertEqual(SkipReason.NoKnownCrossing, refused.Skip, "a release across a river with no known crossing is refused by the gate");
    }

    // 20 ticks of 0.015 h on an alternating picture: no release (the trigger never holds), then a held release that the same alternation does not undo.
    private static void AnAlternatingPictureDoesNotThrash()
    {
        var f = new Field();
        var release = new LeafReserveRelease();
        var plan = new TacticalBattlePlan(BattlePlanId.PreparedDefense, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7);
        int band = f.Axis.SectorOf(500f, LineZ);
        EnemyVisibleState crisis = new EnemyVisibleState(new[] { new EnemyVisibleSector(band, 500f, 4000f, true) }, 0f, true, false, f.Axis);
        EnemyVisibleState calm = new EnemyVisibleState(new EnemyVisibleSector[0], 0f, false, false, f.Axis);
        var records = new[] { f.Record };
        BattleSnapshot s = f.Snapshot();

        int flips = 0, orders = 0;
        for (int tick = 0; tick < 20; tick++)
        {
            if (release.Compute(records, plan, f.Axis, tick % 2 == 0 ? crisis : calm, 0.7f, 5.0 + 0.015 * tick, 0.5f, 0.5f, 0.5f, true, true, s)) flips++;
            var sink = new Sink();
            f.Produce(s, sink, release.Released, release.Releases);
            orders += sink.Intents.Count(i => i.Task == CommandTaskType.ReleaseReserve);
        }
        AssertEqual(0, flips, "an alternating picture releases nothing in 20 ticks");
        AssertEqual(0, orders, "and sends no release order");

        // A crisis that holds releases once; the alternation after it does not send the reserve back.
        int changes = 0;
        for (int tick = 0; tick < 6; tick++) if (release.Compute(records, plan, f.Axis, crisis, 0.7f, 6.0 + 0.015 * tick, 0.5f, 0.5f, 0.5f, true, true, s)) changes++;
        AssertEqual(1, changes, "released once");
        AssertEqual(1, release.Released.Count, "one out");
        for (int tick = 0; tick < 20; tick++)
            if (release.Compute(records, plan, f.Axis, tick % 2 == 0 ? calm : crisis, 0.7f, 6.1 + 0.015 * tick, 0.5f, 0.5f, 0.5f, true, true, s)) changes++;
        AssertEqual(1, changes, "20 ticks of alternation neither return it nor release another");
        // The trigger gone for good: back after the hold.
        for (int tick = 0; tick < 4; tick++)
            if (release.Compute(records, plan, f.Axis, calm, 0.7f, 8.0 + 0.1 * tick, 0.5f, 0.5f, 0.5f, true, true, s)) changes++;
        AssertEqual(2, changes, "and returned once when the picture stays clear");
        AssertEqual(0, release.Released.Count, "it is a reserve again");
    }

    private static void TheArmyLevelReleaseDoesNotFlip()
    {
        // Three siblings in one band row: 0 and 2 on the line, 1 a reserve beside the crisis (the allocator's crisis release, stateless).
        DirectChildEvidence Crisis(bool on) => new DirectChildEvidence(1, on ? 3 : 0, on, 1, 0, 0.9f);
        var snapshots = new[]
        {
            new DirectChildSnapshot("child-1", "", 15, 0, "A", true), new DirectChildSnapshot("child-2", "", 15, 0, "B", true), new DirectChildSnapshot("child-3", "", 15, 0, "C", true),
        };
        var plan = new TacticalBattlePlan(BattlePlanId.PreparedDefense, BattlePhase.MainEffort, 1, null, null, 1.4f, 0f, 7);
        int FlipsWith(ReleaseHysteresis hysteresis, int ticks, Func<int, bool> picture)
        {
            var state = new ReserveReleaseState();
            int flips = 0;
            DirectChildRole last = DirectChildRole.Reserve;
            for (int tick = 0; tick < ticks; tick++)
            {
                var roles = new[] { DirectChildRole.Main, DirectChildRole.Reserve, DirectChildRole.SupportMain };
                var evidence = new[] { Crisis(picture(tick)), new DirectChildEvidence(1, 0, false, 1, 0, 0.9f), new DirectChildEvidence(1, 0, false, 2, 0, 0.9f) };
                var context = new ReserveReleaseContext(1f, false, state, null, 5.0 + 0.015 * tick, 0.5f, 0.5f, 0.5f, staggered: false, hysteresis: hysteresis);
                DirectChildAllocator.ReleaseReservesInto(plan, evidence, roles, context, snapshots, new GuardPoint[3], new int[3]);
                if (roles[1] != last) flips++;
                last = roles[1];
            }
            return flips;
        }
        AssertEqual(20, FlipsWith(null, 20, t => t % 2 == 0), "(without it the release flips with every tick of the picture: 20 flips in 20 ticks)");
        AssertEqual(0, FlipsWith(new ReleaseHysteresis(), 20, t => t % 2 == 0), "with it an alternating picture releases nothing");
        AssertEqual(1, FlipsWith(new ReleaseHysteresis(), 20, t => t >= 2), "a crisis that holds releases once and stays released");
    }

    private static void TheHysteresisTimes()
    {
        var h = new ReleaseHysteresis();
        DirectChildRole role;
        int sector;
        AssertFalse(h.Step("c", true, DirectChildRole.Fix, 2, 5.00, out role, out sector), "the first look does not release");
        AssertFalse(h.Step("c", true, DirectChildRole.Fix, 2, 5.00, out _, out _), "a second look at the same hour is the same look");
        AssertFalse(h.Step("c", true, DirectChildRole.Fix, 2, 5.02, out _, out _), "two looks inside the confirmation time");
        AssertTrue(h.Step("c", true, DirectChildRole.Fix, 2, 5.04, out role, out sector), "released once the trigger has held for the confirmation time");
        AssertEqual(DirectChildRole.Fix, role, "with its role");
        AssertEqual(2, sector, "and band");
        AssertTrue(h.Step("c", false, DirectChildRole.Reserve, -1, 5.10, out role, out sector), "the trigger gone: still out inside the minimum hold");
        AssertEqual(DirectChildRole.Fix, role, "holding its last role");
        AssertTrue(h.Step("c", false, DirectChildRole.Reserve, -1, 5.20, out _, out _), "and still out");
        AssertFalse(h.Step("c", false, DirectChildRole.Reserve, -1, 5.30, out role, out sector), "back after the minimum hold and the return margin");
        AssertEqual(DirectChildRole.Reserve, role, "a reserve");
        // The cooldown: a new trigger right after the return waits.
        for (int k = 0; k < 4; k++) h.Step("c", true, DirectChildRole.Fix, 2, 5.31 + 0.01 * k, out _, out _);
        AssertFalse(h.Step("c", true, DirectChildRole.Fix, 2, 5.38, out _, out _), "not released again inside the rejoin cooldown");
        AssertTrue(h.Step("c", true, DirectChildRole.Fix, 2, 5.45, out _, out _), "released after it");
    }

    private static void NothingIsAllocatedInTheSteadyState()
    {
        var f = new Field();
        var release = new LeafReserveRelease();
        var plan = new TacticalBattlePlan(BattlePlanId.PreparedDefense, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7);
        int band = f.Axis.SectorOf(500f, LineZ);
        EnemyVisibleState crisis = new EnemyVisibleState(new[] { new EnemyVisibleSector(band, 500f, 4000f, true) }, 0f, true, false, f.Axis);
        var records = new[] { f.Record };
        BattleSnapshot s = f.Snapshot();
        var sink = new Sink();
        var moves = new List<LeafReserveMove>();
        UnitFlags[] below = null;
        int[] moving = null;
        PostureSense.AggregateDescendants(s, ref below, ref moving);
        var noZone = new Zone { Active = false };

        void Tick(int k)
        {
            release.Compute(records, plan, f.Axis, crisis, 0.7f, 5.0 + 0.02 * k, 0.5f, 0.5f, 0.5f, true, true, s);
            sink.Intents.Clear();
            moves.Clear();
            LeafReservePlanner.Produce(s, records, release.Released, release.Releases, below, moving, null, noZone, sink, moves);
        }
        for (int k = 0; k < 30; k++) Tick(k);                  // warm-up: releases made, scratch grown
        AssertTrue(release.Released.Count > 0, "(fixture) a reserve is out");
        long begin = TestFixtures.BeginAllocationWindow();
        for (int k = 30; k < 130; k++) Tick(k);
        long bytes = TestFixtures.AllocatedSince(begin);
        AssertEqual(0L, bytes, "100 ticks of release and planning allocate nothing");
    }

    private static void TheReleaseRowsAreThrottled()
    {
        var method = typeof(ArmyOrchestrator).GetMethod("ReleaseRowDue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        AssertTrue(method != null, "the throttle exists");
        WhiskeyRealism.Util.BattleScope.ResetAll();
        bool Due(string child, string reason) => (bool)method.Invoke(null, new object[] { 0, child, reason });
        AssertTrue(Due("child-9", "crisis"), "the first row for a child and reason is written");
        for (int k = 0; k < 20; k++) AssertFalse(Due("child-9", "crisis"), "and not again within the window (tick " + k + ")");
        AssertTrue(Due("child-9", "stagger"), "another reason is its own row");
        AssertTrue(Due("child-10", "crisis"), "another child is its own row");
        WhiskeyRealism.Util.BattleScope.ResetAll();
        AssertTrue(Due("child-9", "crisis"), "a new battle starts the table over");
    }
}
