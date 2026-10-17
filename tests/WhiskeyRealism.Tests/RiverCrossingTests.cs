using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;

// Smoke 2026-10-17: posture Screen and Probe moves (and division-play moves) were checked for water at their target, and a
// withdrawal along its line, but never for the river between the group and a target on the other bank. A move whose straight
// leg crosses a river takes a crossing the side knows, or is not made (RiverCrossingRule, the executor's step 8).
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
            ("river crossing a screen move across a river with no bridge stays", AScreenMoveAcrossARiverWithNoBridgeStays),
            ("river crossing a screen move across a river with a known ford goes by the ford", AScreenMoveWithAKnownFordGoesByTheFord),
            ("river crossing the division play and the reserve release are gated like the posture executor", DivisionPlayAndReserveReleaseAreGated),
            ("river crossing a withdrawal, a fallback, a scout's look and a placement are not gated", WithdrawalsAndScoutsAreNotGated),
            ("river crossing no gate leaves the move as validated", NoGateLeavesTheMove),
        });
    }

    // Two banks: x < 500 is region 0, x >= 500 is region 1; x in [480, 520) would be the river itself in a real map but the
    // fixture keeps it simple. Links are (x, z, state) at the river.
    private sealed class Picture : ICrossingPicture
    {
        internal readonly List<(float x, float z, CrossingState state, int a, int b)> Links = new List<(float, float, CrossingState, int, int)>();
        public bool Ready { get; set; } = true;
        public int RegionAt(float x, float z) => x < 0f ? -1 : x < 500f ? 0 : 1;
        public int LinkCount => Links.Count;
        public bool LinkTouches(int link, int region) => Links[link].a == region || Links[link].b == region;
        public void LinkPoint(int link, out float x, out float z) { x = Links[link].x; z = Links[link].z; }
        public CrossingState StateOf(int link) => Links[link].state;
    }

    private sealed class Gate : ICrossingGate
    {
        internal readonly Picture Picture = new Picture();
        public CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, out float viaX, out float viaZ)
            => RiverCrossingRule.Decide(Picture, x0, z0, x1, z1, out viaX, out viaZ);
    }

    private static void ALegOnOneBankNeedsNoCrossing()
    {
        var p = new Picture();
        AssertEqual(CrossingVerdict.SameBank, RiverCrossingRule.Decide(p, 100f, -100f, 400f, -300f, out _, out _), "west bank to west bank");
        AssertEqual(CrossingVerdict.SameBank, RiverCrossingRule.Decide(p, 600f, -100f, 900f, -300f, out _, out _), "east bank to east bank");
    }

    private static void ALegAcrossARiverWithNoLinkHasNoKnownCrossing()
    {
        var p = new Picture();
        AssertEqual(CrossingVerdict.NoKnownCrossing, RiverCrossingRule.Decide(p, 100f, -100f, 700f, -100f, out _, out _), "no bridge, no ford");
        p.Links.Add((500f, -100f, CrossingState.Clear, 0, 2)); // a link to some third region only
        AssertEqual(CrossingVerdict.NoKnownCrossing, RiverCrossingRule.Decide(p, 100f, -100f, 700f, -100f, out _, out _), "a link that does not join these two banks");
    }

    private static void OnlyAClearOrWatchedLinkCarriesAMove()
    {
        var p = new Picture();
        p.Links.Add((500f, -900f, CrossingState.Unknown, 0, 1));
        p.Links.Add((500f, -500f, CrossingState.Held, 0, 1));
        AssertEqual(CrossingVerdict.NoKnownCrossing, RiverCrossingRule.Decide(p, 100f, -100f, 700f, -100f, out _, out _), "unknown and held crossings carry no move");
        p.Links.Add((500f, -800f, CrossingState.Watched, 0, 1));
        p.Links.Add((500f, -150f, CrossingState.Clear, 0, 1));
        var verdict = RiverCrossingRule.Decide(p, 100f, -100f, 700f, -100f, out float vx, out float vz);
        AssertEqual(CrossingVerdict.ViaCrossing, verdict, "a clear link carries it");
        AssertEqual(-150f, vz, "the shortest detour among the known ones");
        AssertEqual(500f, vx, "at the crossing point");
        p.Links[3] = (500f, -150f, CrossingState.Held, 0, 1);
        RiverCrossingRule.Decide(p, 100f, -100f, 700f, -100f, out _, out float vz2);
        AssertEqual(-800f, vz2, "a watched crossing is known and usable once the clear one is held");
    }

    private static void AnUnreadyPictureLetsTheLegThrough()
    {
        var p = new Picture { Ready = false };
        AssertEqual(CrossingVerdict.SameBank, RiverCrossingRule.Decide(p, 100f, -100f, 700f, -100f, out _, out _), "no picture, no wall");
        var ready = new Picture();
        AssertEqual(CrossingVerdict.SameBank, RiverCrossingRule.Decide(ready, -5f, -100f, 700f, -100f, out _, out _), "a start on the river or a ford is on no bank");
    }

    // ---- the executor ----

    private sealed class View : IBattleUnitView
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

    private sealed class AllowGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason) { reason = SkipReason.None; return true; }
    }

    private static OrderExecutor Executor(Gate gate)
    {
        var executor = new OrderExecutor();
        executor.Bind(new AllowGuard(), null);
        executor.SetMapSize(2000f, 2000f);
        executor.Crossings = gate;
        return executor;
    }

    private static BattleOrderIntent Move(float x, float z, IntentSource source, CommandTaskType task, IntentFlags flags = IntentFlags.None) =>
        new BattleOrderIntent
        {
            GroupId = 10, Channel = OrderChannel.Move, TargetX = x, TargetZ = z, Heading = -1f, Priority = IntentPriority.Executor,
            Source = source, Task = task, Flags = flags, Reason = "test",
        };

    private static void AScreenMoveAcrossARiverWithNoBridgeStays()
    {
        var gate = new Gate();
        ExecutionPlan plan = Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0);
        AssertFalse(plan.Execute, "the Screen move across the river is not made");
        AssertEqual(SkipReason.NoKnownCrossing, plan.Skip, "and says why");
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
