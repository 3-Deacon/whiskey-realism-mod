using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;

// Smoke 2026-10-17: posture Screen and Probe moves (and division-play moves) were checked for water at their target, and a
// withdrawal along its line, but never for the river between the group and a target on the other bank. A move whose straight
// leg crosses a river takes a chain of crossings the side knows, or is not made (RiverCrossingRule, the executor's step 8): a
// planned or road via is kept, a crossing the selected plan chose is not refused, a leg over several rivers is routed link by
// link, and a refused leg posts a look request into BC14's look mechanism (the ledger-backed tests are in ManeuverFeasibilityTests.Look).
internal static class RiverCrossingTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("river crossing a leg on one bank needs no crossing", ALegOnOneBankNeedsNoCrossing),
            ("river crossing a leg across a river with no link has no known crossing", ALegAcrossARiverWithNoLinkHasNoKnownCrossing),
            ("river crossing only a clear or watched link carries a move, the shortest detour first", OnlyAClearOrWatchedLinkCarriesAMove),
            ("river crossing an unready picture or a point on the river lets the leg through", AnUnreadyPictureLetsTheLegThrough),
            ("river crossing a leg over several rivers is routed link by link and the first two crossings are the vias", ALegOverSeveralRiversIsRoutedLinkByLink),
            ("river crossing a refused leg names the first unknown crossing of its best route and asks for a look at it", ARefusedLegAsksForALook),
            ("river crossing a held crossing is no look and a crossing the plan chose is not refused", AHeldCrossingIsNoLookAndAPlannedOneIsNotRefused),
            ("river crossing a screen move across a river with no bridge stays and wants the ford seen", AScreenMoveAcrossARiverWithNoBridgeStays),
            ("river crossing a screen move across a river with a known ford goes by the ford", AScreenMoveWithAKnownFordGoesByTheFord),
            ("river crossing a screen move over two rivers goes by both crossings", AScreenMoveOverTwoRiversGoesByBoth),
            ("river crossing a planned or road via is kept and the gate does not touch the move", APlannedViaIsKept),
            ("river crossing an attack the plan sent across a held crossing is made", AnAttackAcrossAPlannedHeldCrossingIsMade),
            ("river crossing the division play and the reserve release are gated like the posture executor", DivisionPlayAndReserveReleaseAreGated),
            ("river crossing a withdrawal, a fallback, a scout's look and a placement are not gated", WithdrawalsAndScoutsAreNotGated),
            ("river crossing no gate leaves the move as validated", NoGateLeavesTheMove),
        });
    }

    // Four banks side by side: x < 0 is no land, x < 500 region 0, x < 1000 region 1, x < 1500 region 2, beyond region 3; the river lies at each
    // boundary in a real map but the fixture keeps it simple. Links are (x, z, state) at a boundary joining regions a and b.
    internal sealed class Picture : ICrossingPicture
    {
        internal readonly List<(float x, float z, CrossingState state, int a, int b)> Links = new List<(float, float, CrossingState, int, int)>();
        internal readonly HashSet<int> Planned = new HashSet<int>();
        internal readonly List<int> Looks = new List<int>();
        public bool Ready { get; set; } = true;
        public int RegionAt(float x, float z) => x < 0f ? -1 : x < 500f ? 0 : x < 1000f ? 1 : x < 1500f ? 2 : 3;
        public int LinkCount => Links.Count;
        public bool LinkTouches(int link, int region) => Links[link].a == region || Links[link].b == region;
        public int RegionsOfLink(int link, Span<int> regions) { regions[0] = Links[link].a; regions[1] = Links[link].b; return 2; }
        public void LinkPoint(int link, out float x, out float z) { x = Links[link].x; z = Links[link].z; }
        public CrossingState StateOf(int link) => Links[link].state;
        public bool IsPlanned(int link) => Planned.Contains(link);
        public void RequestLook(int link) => Looks.Add(link);
    }

    internal sealed class Gate : ICrossingGate
    {
        internal readonly Picture Picture = new Picture();
        public CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, out CrossingRoute route)
            => RiverCrossingRule.Decide(Picture, x0, z0, x1, z1, out route);
    }

    private static CrossingVerdict Decide(Picture p, float x0, float z0, float x1, float z1) => RiverCrossingRule.Decide(p, x0, z0, x1, z1, out _);

    private static void ALegOnOneBankNeedsNoCrossing()
    {
        var p = new Picture();
        AssertEqual(CrossingVerdict.SameBank, Decide(p, 100f, -100f, 400f, -300f), "west bank to west bank");
        AssertEqual(CrossingVerdict.SameBank, Decide(p, 600f, -100f, 900f, -300f), "the next bank to itself");
    }

    private static void ALegAcrossARiverWithNoLinkHasNoKnownCrossing()
    {
        var p = new Picture();
        AssertEqual(CrossingVerdict.NoKnownCrossing, Decide(p, 100f, -100f, 700f, -100f), "no bridge, no ford");
        p.Links.Add((500f, -100f, CrossingState.Clear, 0, 2)); // a link to some other region only
        AssertEqual(CrossingVerdict.NoKnownCrossing, Decide(p, 100f, -100f, 700f, -100f), "a link that does not join these two banks");
        AssertEqual(0, p.Looks.Count, "and nothing to look at: there is no crossing that would join them");
    }

    private static void OnlyAClearOrWatchedLinkCarriesAMove()
    {
        var p = new Picture();
        p.Links.Add((500f, -900f, CrossingState.Unknown, 0, 1));
        p.Links.Add((500f, -500f, CrossingState.Held, 0, 1));
        AssertEqual(CrossingVerdict.NoKnownCrossing, Decide(p, 100f, -100f, 700f, -100f), "unknown and held crossings carry no move");
        p.Links.Add((500f, -800f, CrossingState.Watched, 0, 1));
        p.Links.Add((500f, -150f, CrossingState.Clear, 0, 1));
        var verdict = RiverCrossingRule.Decide(p, 100f, -100f, 700f, -100f, out CrossingRoute route);
        AssertEqual(CrossingVerdict.ViaCrossing, verdict, "a clear link carries it");
        AssertEqual(-150f, route.ViaZ0, "the shortest detour among the known ones");
        AssertEqual(500f, route.ViaX0, "at the crossing point");
        AssertEqual(1, route.ViaCount, "one via");
        p.Links[3] = (500f, -150f, CrossingState.Held, 0, 1);
        RiverCrossingRule.Decide(p, 100f, -100f, 700f, -100f, out CrossingRoute again);
        AssertEqual(-800f, again.ViaZ0, "a watched crossing is known and usable once the clear one is held");
    }

    private static void AnUnreadyPictureLetsTheLegThrough()
    {
        var p = new Picture { Ready = false };
        AssertEqual(CrossingVerdict.SameBank, Decide(p, 100f, -100f, 700f, -100f), "no picture, no wall");
        var ready = new Picture();
        AssertEqual(CrossingVerdict.SameBank, Decide(ready, -5f, -100f, 700f, -100f), "a start on the river or a ford is on no bank");
    }

    // verify F2: the rule took one link joining both banks, so a leg over two rivers had none and was refused although the chain
    // of crossings was known.
    private static void ALegOverSeveralRiversIsRoutedLinkByLink()
    {
        var p = new Picture();
        p.Links.Add((500f, 100f, CrossingState.Clear, 0, 1));
        p.Links.Add((1000f, 300f, CrossingState.Watched, 1, 2));
        var verdict = RiverCrossingRule.Decide(p, 100f, 100f, 1200f, 100f, out CrossingRoute two);
        AssertEqual(CrossingVerdict.ViaCrossing, verdict, "two known crossings carry a leg over two rivers");
        AssertEqual(2, two.Crossings, "the route has two crossings");
        AssertEqual(2, two.ViaCount, "and two vias");
        AssertEqual(500f, two.ViaX0, "the first river first");
        AssertEqual(1000f, two.ViaX1, "then the second");
        AssertEqual(300f, two.ViaZ1, "at its own crossing");

        // The shortest chain: a far-off ford of the first river is not taken while a nearer one is known.
        p.Links.Add((500f, -700f, CrossingState.Clear, 0, 1));
        RiverCrossingRule.Decide(p, 100f, 100f, 1200f, 100f, out CrossingRoute nearer);
        AssertEqual(100f, nearer.ViaZ0, "the nearer ford of the first river");

        // Three rivers: the first two are the vias (the executor's bound), the route knows it has three.
        p.Links.Add((1500f, 100f, CrossingState.Clear, 2, 3));
        RiverCrossingRule.Decide(p, 100f, 100f, 1800f, 100f, out CrossingRoute three);
        AssertEqual(3, three.Crossings, "three crossings in the route");
        AssertEqual(2, three.ViaCount, "the first two are vias");
        AssertEqual(1000f, three.ViaX1, "the second river's crossing is the second via");

        // A middle link the side does not know breaks the chain.
        var q = new Picture();
        q.Links.Add((500f, 100f, CrossingState.Clear, 0, 1));
        q.Links.Add((1000f, 300f, CrossingState.Unknown, 1, 2));
        AssertEqual(CrossingVerdict.NoKnownCrossing, Decide(q, 100f, 100f, 1200f, 100f), "an unknown second crossing leaves the chain open");
    }

    // verify F3: the refused leg asks for the crossing that would make it known.
    private static void ARefusedLegAsksForALook()
    {
        var p = new Picture();
        p.Links.Add((500f, 100f, CrossingState.Clear, 0, 1));
        p.Links.Add((1000f, 300f, CrossingState.Unknown, 1, 2));
        p.Links.Add((1000f, 900f, CrossingState.Held, 1, 2));
        var verdict = RiverCrossingRule.Decide(p, 100f, 100f, 1200f, 100f, out CrossingRoute route);
        AssertEqual(CrossingVerdict.NoKnownCrossing, verdict, "refused");
        AssertEqual(1, route.LookLink, "the unknown crossing of the best route is the one to look at");
        AssertEqual(1, p.Looks.Count, "one look requested");
        AssertEqual(1, p.Looks[0], "for that link");
    }

    private static void AHeldCrossingIsNoLookAndAPlannedOneIsNotRefused()
    {
        var p = new Picture();
        p.Links.Add((500f, 100f, CrossingState.Held, 0, 1));
        var verdict = RiverCrossingRule.Decide(p, 100f, 100f, 700f, 100f, out CrossingRoute route);
        AssertEqual(CrossingVerdict.NoKnownCrossing, verdict, "a held crossing is no way across");
        AssertEqual(-1, route.LookLink, "and no look: seeing it again changes nothing");
        AssertEqual(0, p.Looks.Count, "none requested");

        p.Planned.Add(0);
        var held = RiverCrossingRule.Decide(p, 100f, 100f, 700f, 100f, out CrossingRoute byPlan);
        AssertEqual(CrossingVerdict.ViaCrossing, held, "the plan chose that crossing: not the gate's to refuse, and the move goes by it");
        AssertEqual(500f, byPlan.ViaX0, "by that crossing");
        AssertEqual(0, p.Looks.Count, "and nothing to look at for it");
        p.Links[0] = (500f, 100f, CrossingState.Unknown, 0, 1);
        AssertEqual(CrossingVerdict.ViaCrossing, Decide(p, 100f, 100f, 700f, 100f), "nor an unknown one the plan chose");
        AssertEqual(0, p.Looks.Count, "which needs no look of the gate's");
        p.Links[0] = (500f, 100f, CrossingState.Clear, 0, 1);
        AssertEqual(CrossingVerdict.ViaCrossing, Decide(p, 100f, 100f, 700f, 100f), "a planned crossing the side knows is a via like any other");
    }

    // ---- the executor ----

    internal sealed class View : IBattleUnitView
    {
        private readonly UnitRecord[] _own = new UnitRecord[4];
        public View(float x, float z)
        {
            _own[0] = new UnitRecord
            {
                InstanceId = 10, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = x, Z = z, ParentIndex = -1, Strength = 1000,
                Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup, LastWaypointX = x, LastWaypointZ = z,
            };
        }
        public int Alliance => 0;
        public int OwnCount => 1;
        public UnitRecord[] Own => _own;
        public int IndexOfOwn(int instanceId) => instanceId == 10 ? 0 : -1;
        public bool IsAncestorOf(int ancestorIndex, int descendantIndex) => false;
        public int ObservationCount => 0;
        public EnemyObservation[] Observations => new EnemyObservation[1];
    }

    internal sealed class AllowGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason) { reason = SkipReason.None; return true; }
    }

    internal static OrderExecutor Executor(ICrossingGate gate, float size = 2500f)
    {
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        executor.SetMapSize(size, size);
        executor.Crossings = gate;
        return executor;
    }

    internal static BattleOrderIntent Move(float x, float z, IntentSource source, CommandTaskType task, IntentFlags flags = IntentFlags.None) =>
        new BattleOrderIntent
        {
            GroupId = 10, Channel = OrderChannel.Move, TargetX = x, TargetZ = z, Heading = -1f, Priority = IntentPriority.Executor,
            Source = source, Task = task, Flags = flags, Reason = "test",
        };

    private static void AScreenMoveAcrossARiverWithNoBridgeStays()
    {
        var gate = new Gate();
        gate.Picture.Links.Add((500f, 300f, CrossingState.Unknown, 0, 1));
        ExecutionPlan plan = Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0);
        AssertFalse(plan.Execute, "the Screen move across the river is not made");
        AssertEqual(SkipReason.NoKnownCrossing, plan.Skip, "and says why");
        AssertEqual(1, gate.Picture.Looks.Count, "and the ford nobody has seen is asked to be looked at");
        AssertEqual(0, gate.Picture.Looks[0], "that ford");
        ExecutionPlan probe = Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Probe), 1.0);
        AssertEqual(SkipReason.NoKnownCrossing, probe.Skip, "nor the Probe");
        ExecutionPlan same = Executor(gate).Plan(new View(100f, 100f), Move(400f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0);
        AssertTrue(same.Execute, "a Screen move on its own bank is made");
    }

    private static void AScreenMoveWithAKnownFordGoesByTheFord()
    {
        var gate = new Gate();
        gate.Picture.Links.Add((500f, 300f, CrossingState.Clear, 0, 1));
        ExecutionPlan plan = Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0);
        AssertTrue(plan.Execute, "the move is made");
        AssertEqual(1, plan.ViaCount, "through one via point");
        AssertEqual(500f, plan.ViaX0, "the ford");
        AssertEqual(300f, plan.ViaZ0, "the ford");
        AssertEqual(700f, plan.X, "to the target on the far bank");
    }

    private static void AScreenMoveOverTwoRiversGoesByBoth()
    {
        var gate = new Gate();
        gate.Picture.Links.Add((500f, 300f, CrossingState.Clear, 0, 1));
        gate.Picture.Links.Add((1000f, 200f, CrossingState.Watched, 1, 2));
        ExecutionPlan plan = Executor(gate).Plan(new View(100f, 100f), Move(1200f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0);
        AssertTrue(plan.Execute, "the move is made");
        AssertEqual(2, plan.ViaCount, "through two crossings");
        AssertEqual(500f, plan.ViaX0, "the first river's");
        AssertEqual(1000f, plan.ViaX1, "then the second's");
        AssertEqual(200f, plan.ViaZ1, "at its crossing");
        AssertEqual(1200f, plan.X, "to the target two rivers away");
    }

    // verify F1: the old gate replaced a via the move already had (a road, a play's march via) with its own crossing.
    private static void APlannedViaIsKept()
    {
        var gate = new Gate();
        gate.Picture.Links.Add((500f, 900f, CrossingState.Clear, 0, 1));
        BattleOrderIntent withVia = Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen);
        withVia.ViaCount = 1;
        withVia.ViaX0 = 300f;
        withVia.ViaZ0 = -200f;
        ExecutionPlan plan = Executor(gate).Plan(new View(100f, 100f), withVia, 1.0);
        AssertTrue(plan.Execute, "the move is made");
        AssertEqual(1, plan.ViaCount, "with its own via");
        AssertEqual(300f, plan.ViaX0, "which is the intent's, not the gate's crossing");
        AssertEqual(-200f, plan.ViaZ0, "which is the intent's, not the gate's crossing");
        AssertEqual(0, gate.Picture.Looks.Count, "and nothing asked for");
        var none = new Gate();
        AssertTrue(Executor(none).Plan(new View(100f, 100f), withVia, 1.0).Execute, "a via is kept even when the side knows no crossing: the mover chose it");
    }

    // verify F1: a planned attack across a Held crossing (Burnside's bridge) is the plan's decision.
    private static void AnAttackAcrossAPlannedHeldCrossingIsMade()
    {
        var gate = new Gate();
        gate.Picture.Links.Add((500f, 100f, CrossingState.Held, 0, 1));
        AssertEqual(SkipReason.NoKnownCrossing, Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0).Skip,
            "an unplanned move over a held crossing is refused");
        gate.Picture.Planned.Add(0);
        ExecutionPlan plan = Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.DivisionPlay, CommandTaskType.AttackObjective), 1.0);
        AssertTrue(plan.Execute, "once the plan chose the crossing the attack is made");
        AssertEqual(1, plan.ViaCount, "by the crossing the plan chose");
        AssertEqual(500f, plan.ViaX0, "the bridge itself");
        AssertEqual(700f, plan.X, "to its target");
    }

    private static void DivisionPlayAndReserveReleaseAreGated()
    {
        var gate = new Gate();
        foreach (var source in new[] { IntentSource.DivisionPlay, IntentSource.ReserveRelease, IntentSource.PostureExecutor })
        {
            ExecutionPlan plan = Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, source, CommandTaskType.SupportAttack), 1.0);
            AssertEqual(SkipReason.NoKnownCrossing, plan.Skip, source + " is gated");
        }
    }

    private static void WithdrawalsAndScoutsAreNotGated()
    {
        var gate = new Gate();
        AssertTrue(Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.Withdrawal, CommandTaskType.FallBackToLine), 1.0).Execute, "withdrawal source");
        AssertTrue(Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.FallBackToLine), 1.0).Execute, "a fallback task");
        AssertTrue(Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen, IntentFlags.InContactWithdrawal), 1.0).Execute, "an in-contact withdrawal");
        AssertTrue(Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.Recon, CommandTaskType.Scout), 1.0).Execute, "a scout looks at the crossing that makes it known");
        AssertTrue(Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.Deployment, CommandTaskType.None, IntentFlags.ImmediatePlacement), 1.0).Execute, "a deployment placement");
    }

    private static void NoGateLeavesTheMove()
    {
        var executor = Executor(null);
        AssertTrue(executor.Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0).Execute, "without a gate the move is as validated");
    }
}
