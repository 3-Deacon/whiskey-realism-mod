using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;

// BC10 decision 6: road march routing and its executor contract (via points, column march).
internal static partial class BattleMovementHonestyTests
{
    // 1200 x 1200 map. A forest band x in [300, 900), z in [-700, -500) lies across the
    // straight line from (100, -600) to (1100, -600); a road runs along z = -400 from x = 200
    // to x = 1000. The corridor (0.6 x 1000 m = +-300 m) holds both.
    private const float RouteLength = 1000f;
    private static readonly float[] StraightX = { 100f, 1100f };
    private static readonly float[] StraightZ = { -600f, -600f };

    private static BattleTerrainGrid ForestRoadGrid(int[] speedCost = null, bool road = true, int roadFrom = 200, int roadTo = 1000)
    {
        var ids = new int[1201, 1201];
        for (int x = 0; x <= 1200; x++)
        {
            for (int zi = 0; zi <= 1200; zi++)
            {
                float z = zi - 1200f;
                int id = Grass;
                if (x >= 300 && x < 900 && z >= -700f && z < -500f) id = Forest;
                if (road && x >= roadFrom && x <= roadTo && Math.Abs(z + 400f) <= 3f) id = Road;
                ids[x, zi] = id;
            }
        }
        return new BattleTerrainGrid(ids, 1200f, 1200f, speedCost ?? ShippedSpeedCost, false, null, null, null, 0);
    }

    private static RoadRoute Route(BattleTerrainGrid g, float x0 = 100f, float z0 = -600f, float x1 = 1100f, float z1 = -600f,
        float[] pathX = null, float[] pathZ = null, RoadRouteScratch scratch = null)
    {
        pathX = pathX ?? new[] { x0, x1 };
        pathZ = pathZ ?? new[] { z0, z1 };
        return RoadRoutePlanner.Plan(g, x0, z0, x1, z1, pathX, pathZ, pathX.Length, scratch ?? new RoadRouteScratch());
    }

    private static void RouteNotPlannedBelow400m()
    {
        BattleTerrainGrid g = ForestRoadGrid();
        // 350 m straight through the forest band: under MinRouteMeters.
        RoadRoute r = Route(g, 320f, -600f, 670f, -600f);
        AssertFalse(r.Use, "a 350 m move is not routed");
        AssertEqual(0, r.ViaCount, "no via points");
        AssertTrue(Route(g).Use, "the 1000 m move is (control)");
    }

    private static void RouteNotPlannedBelowWoodedShare()
    {
        // The straight line crosses 600 m of forest of 1000 m (60%) in the control; shrink the
        // forest so under 30% of the samples are wooded.
        var ids = new int[1201, 1201];
        for (int x = 0; x <= 1200; x++)
            for (int zi = 0; zi <= 1200; zi++)
            {
                float z = zi - 1200f;
                int id = Grass;
                if (x >= 500 && x < 700 && z >= -700f && z < -500f) id = Forest; // 200 m of 1000
                if (x >= 200 && x <= 1000 && Math.Abs(z + 400f) <= 3f) id = Road;
                ids[x, zi] = id;
            }
        var g = new BattleTerrainGrid(ids, 1200f, 1200f, ShippedSpeedCost, false, null, null, null, 0);
        AssertFalse(Route(g).Use, "20% wooded: vanilla's path is fine");
        // 30% is the trigger: 29 samples of 101 do not reach it, 31 do.
        for (int x = 500; x < 800; x++) for (int zi = 500; zi < 700; zi++) ids[x, zi] = Forest;
        AssertFalse(Route(g).Use, "29.7% wooded (30 of 101 samples) is under the trigger");
        for (int x = 800; x < 810; x++) for (int zi = 500; zi < 700; zi++) ids[x, zi] = Forest;
        AssertTrue(Route(g).Use, "30.7% wooded plans a route");
    }

    private static void RouteTakesRoadAroundForest()
    {
        BattleTerrainGrid g = ForestRoadGrid();
        RoadRoute r = Route(g);
        AssertTrue(r.Use, "the road loop is 1.6x the forest line but 0.4x its cost");
        AssertEqual(2, r.ViaCount, "entry and exit");
        AssertTrue(r.RouteCost <= RoadRoutePlanner.RouteGainRatio * r.VanillaCost, "at most 0.80 of vanilla's cost: " + r.RouteCost + " vs " + r.VanillaCost);
        AssertTrue(Math.Abs(r.ViaZ0 + 400f) < 13f && Math.Abs(r.ViaZ1 + 400f) < 13f, "both via points sit on the road (z -400)");
        AssertTrue(r.ViaX0 >= 200f && r.ViaX0 < r.ViaX1, "entry before exit along the route");
        AssertTrue(Dist(r.ViaX0, r.ViaZ0, 100f, -600f) >= RoadRoutePlanner.MinViaSeparationMeters, "entry at least 60 m from the start");
        AssertTrue(Dist(r.ViaX1, r.ViaZ1, r.ViaX0, r.ViaZ0) >= RoadRoutePlanner.MinViaSeparationMeters, "exit at least 60 m from the entry");
    }

    private static void RouteRejectedWhenGainBelow20Percent()
    {
        // Woods that cost as much as open ground (2): vanilla's path costs 2000, the road route
        // 1694 + lattice slack = ratio 0.85 > 0.80.
        int[] cheapWoods = (int[])ShippedSpeedCost.Clone();
        cheapWoods[Forest] = 2;
        RoadRoute r = Route(ForestRoadGrid(cheapWoods));
        AssertFalse(r.Use, "less than a 20% saving is not worth leaving vanilla's path");
        AssertTrue(r.RouteCost > RoadRoutePlanner.RouteGainRatio * r.VanillaCost, "the ratio is what rejected it");
    }

    private static void RouteNeverViaWithinLast250m()
    {
        BattleTerrainGrid g = ForestRoadGrid();
        RoadRoute r = Route(g);
        AssertTrue(r.Use, "control");
        for (int k = 0; k < r.ViaCount; k++)
        {
            float x = k == 0 ? r.ViaX0 : r.ViaX1;
            float z = k == 0 ? r.ViaZ0 : r.ViaZ1;
            AssertTrue(Dist(x, z, 1100f, -600f) >= RoadRoutePlanner.NoRoadNearTargetMeters, "via " + k + " is at least 250 m from the target");
        }
        // The only road lies in the last 250 m: no via point, no route.
        BattleTerrainGrid late = ForestRoadGrid(roadFrom: 960, roadTo: 1000);
        AssertFalse(Route(late).Use, "a road only in the final stretch pulls nothing");
    }

    private static void RouteRiverImpassableUsesBridge()
    {
        // A river band x in [600, 620) across the whole corridor (z in [-900, -300]); vanilla's
        // path (the NavMesh's) crosses at the bridge on the road line z = -400.
        BattleTerrainGrid Make(bool withBridge)
        {
            var ids = new int[1201, 1201];
            for (int x = 0; x <= 1200; x++)
                for (int zi = 0; zi <= 1200; zi++)
                {
                    float z = zi - 1200f;
                    int id = Grass;
                    if (x >= 300 && x < 900 && z >= -700f && z < -500f) id = Forest;
                    if (x >= 200 && x <= 1000 && Math.Abs(z + 400f) <= 3f) id = Road;
                    if (x >= 600 && x < 620 && z >= -950f && z <= -250f) id = withBridge && Math.Abs(z + 400f) <= 3f ? Bridge : River;
                    ids[x, zi] = id;
                }
            return new BattleTerrainGrid(ids, 1200f, 1200f, ShippedSpeedCost, false, null, null, null, 0);
        }
        float[] vx = { 100f, 600f, 1100f };
        float[] vz = { -600f, -400f, -600f };
        AssertTrue(Route(Make(true), pathX: vx, pathZ: vz).Use, "the route crosses at the bridge");
        AssertFalse(Route(Make(false), pathX: vx, pathZ: vz).Use, "an unfrozen river across the whole corridor cannot be crossed");
    }

    private static void RouteCorridorShrinksToFit6000CellsElseNotPlanned()
    {
        AssertTrue(RoadRoutePlanner.Corridor(1000f, out int rows, out int columns), "1 km");
        AssertEqual(41, rows, "rows of 25 m");
        AssertEqual(25, columns, "0.6 x 1000 m = +-300 m -> 25 columns");
        AssertTrue(RoadRoutePlanner.Corridor(4000f, out rows, out columns), "4 km fits");
        AssertEqual(161, rows, "rows");
        AssertEqual(37, columns, "narrowed to 37 columns (925 m)");
        AssertTrue(rows * columns <= RoadRoutePlanner.MaxCells, "within the cell budget");
        AssertTrue(columns * RoadRoutePlanner.RouteCellMeters >= RoadRoutePlanner.MinCorridorShare * 4000f, "925 m >= 0.2 x 4000 m");
        AssertFalse(RoadRoutePlanner.Corridor(6000f, out rows, out columns), "6 km would leave 575 m, under 0.2 x its length");
    }

    private static void RoutePlanAllocatesNothingAfterWarmup()
    {
        // A 3 km move: 121 rows x 49 columns = 5,929 cells, the 6,000-cell corridor of the plan.
        var ids = new int[1, 1];
        ids[0, 0] = Forest;
        var g = new BattleTerrainGrid(ids, 3400f, 3400f, ShippedSpeedCost, false, null, null, null, 0);
        var scratch = new RoadRouteScratch();
        float[] px = { 100f, 3100f };
        float[] pz = { -1700f, -1700f };
        AssertNoAllocation(() => RoadRoutePlanner.Plan(g, 100f, -1700f, 3100f, -1700f, px, pz, 2, scratch), 3, "no allocation after warm-up (6,000-cell corridor)");
    }

    // The posture pipeline's decision: group formation 4 first, the routed move after it.
    private static void RouteWaitsForColumnThenGoesUnrouted()
    {
        AssertEqual(ColumnDecision.NoRoute, RoadRoutePlanner.DecideColumn(false, 0, 0.0), "no route planned");
        AssertEqual(ColumnDecision.Routed, RoadRoutePlanner.DecideColumn(true, RoadRoutePlanner.ColumnGroupFormation, 0.0), "already in march column");
        AssertEqual(ColumnDecision.Wait, RoadRoutePlanner.DecideColumn(true, 1, 0.0), "double line: order the column and wait");
        AssertEqual(ColumnDecision.Wait, RoadRoutePlanner.DecideColumn(true, 1, 0.099), "still inside 0.1 doctrine-h");
        AssertEqual(ColumnDecision.TimedOutUnrouted, RoadRoutePlanner.DecideColumn(true, 1, 0.1), "no column after 0.1 h: the move goes unrouted");
        AssertEqual(4, RoadRoutePlanner.ColumnGroupFormation, "March Column (5883)");
    }

    private static BattleOrderIntent RoutedMove(int group, float tx, float tz, int vias, float v0x, float v0z, float v1x, float v1z, float heading = -1f)
    {
        return new BattleOrderIntent
        {
            GroupId = group, Channel = OrderChannel.Move, TargetX = tx, TargetZ = tz, Heading = heading, Task = CommandTaskType.AdvanceToAssembly,
            Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "test", Flags = IntentFlags.UseOrderDelay,
            ViaCount = vias, ViaX0 = v0x, ViaZ0 = v0z, ViaX1 = v1x, ViaZ1 = v1z,
        };
    }

    // The posture request becomes: a Move carrying the via points (leg headings are the
    // executor's: the bearing from each via point to the next point) and, while the group is
    // not in column, a Formation intent for group formation 4 with no Move.
    private static void RoutedMoveCarriesColumnFormationAndLegHeadings()
    {
        var sink = new RecordingSink();
        var request = new PostureIntentRequest
        {
            GroupId = 7, Action = PostureExecutionAction.SetFormationAndWaypoint, Task = CommandTaskType.AdvanceToAssembly,
            HasTarget = true, TargetX = 1100f, TargetZ = -600f, TargetFormation = RoadRoutePlanner.ColumnGroupFormation, NeedsFormation = false,
            Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "test",
            ViaCount = 2, ViaX0 = 250f, ViaZ0 = -400f, ViaX1 = 950f, ViaZ1 = -400f,
        };
        PostureIntentPlan.Submit(sink, in request);
        BattleOrderIntent move = sink.Find(OrderChannel.Move);
        AssertEqual(2, move.ViaCount, "the move carries both via points");
        AssertNear(250f, move.ViaX0, 0.001f, "via 0 x");
        AssertNear(-400f, move.ViaZ1, 0.001f, "via 1 z");
        AssertEqual(0, sink.Count(OrderChannel.Formation), "already in column: no formation intent");

        // Not in column yet: the formation intent only, the move waits.
        sink = new RecordingSink();
        request.NeedsFormation = true;
        request.ColumnWait = true;
        PostureIntentPlan.Submit(sink, in request);
        AssertEqual(0, sink.Count(OrderChannel.Move), "the routed move waits for the column");
        BattleOrderIntent formation = sink.Find(OrderChannel.Formation);
        AssertEqual(RoadRoutePlanner.ColumnGroupFormation, formation.Value, "group formation 4");

        // Leg headings: the bearing from each via point to the next point.
        AssertNear(90f, OrderExecutor.LegHeading(250f, -400f, 950f, -400f), 0.01f, "via 0 -> via 1 runs east");
        AssertNear(0f, OrderExecutor.LegHeading(950f, -400f, 950f, -200f), 0.01f, "+z is heading 0");
        AssertNear(135f, OrderExecutor.LegHeading(0f, 0f, 100f, -100f), 0.01f, "south-east");
        AssertNear(270f, OrderExecutor.LegHeading(100f, 0f, 0f, 0f), 0.01f, "west");
    }

    private sealed class RecordingSink : IIntentSink
    {
        private readonly BattleOrderIntent[] _items = new BattleOrderIntent[16];
        private int _count;
        public void Submit(in BattleOrderIntent intent) { if (_count < _items.Length) _items[_count++] = intent; }
        public int Count(OrderChannel channel) { int n = 0; for (int i = 0; i < _count; i++) if (_items[i].Channel == channel) n++; return n; }
        public BattleOrderIntent Find(OrderChannel channel) { for (int i = 0; i < _count; i++) if (_items[i].Channel == channel) return _items[i]; return default(BattleOrderIntent); }
    }

    private sealed class FixtureView : IBattleUnitView
    {
        private readonly UnitRecord[] _own = new UnitRecord[8];
        public int Alliance => 0;
        public int OwnCount { get; private set; }
        public UnitRecord[] Own => _own;
        public int ObservationCount => 0;
        public EnemyObservation[] Observations => new EnemyObservation[0];
        public int IndexOfOwn(int id) { for (int i = 0; i < OwnCount; i++) if (_own[i].InstanceId == id) return i; return -1; }
        public bool IsAncestorOf(int a, int d) { return false; }
        public int Add(int id, float x, float z, UnitFlags extra = UnitFlags.None)
        {
            _own[OwnCount] = new UnitRecord
            {
                InstanceId = id, UnitTyp = 15, X = x, Z = z, ParentIndex = -1, CombatBehaviorOrdered = -1, Formation = 0,
                Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup | extra,
            };
            return OwnCount++;
        }
    }

    private sealed class OpenGuard : IOrderGuard
    {
        public bool CanWrite(in UnitRecord unit, out SkipReason reason) { reason = SkipReason.None; return true; }
    }

    private sealed class WaterDisc : ITerrainProbe
    {
        public float X, Z, Radius;
        public bool IsWaterOrFence(float x, float z) { float dx = x - X, dz = z - Z; return Radius > 0f && dx * dx + dz * dz <= Radius * Radius; }
        public bool LineCrossesWater(float x0, float z0, float x1, float z1) { return false; }
        public bool StepBackFromRiver(float x0, float z0, ref float x1, ref float z1) { return false; }
    }

    private static OrderExecutor ExecutorOver(ITerrainProbe terrain)
    {
        var executor = new OrderExecutor();
        executor.Bind(new OpenGuard(), terrain);
        executor.SetMapSize(1200f, 1200f);
        return executor;
    }

    // Each via point is validated like the target (clamped to the map, pulled off water);
    // one that cannot be made safe is dropped, the others stay, and the friendly-footprint rule
    // does not apply to a point the group only passes.
    private static void ExecutorValidatesViaPoints()
    {
        var view = new FixtureView();
        view.Add(1, 100f, -600f);
        view.Add(2, 250f, -400f); // another group standing on via 0: a pass-through point is not refused for it
        OrderExecutor executor = ExecutorOver(new WaterDisc { X = 950f, Z = -400f, Radius = 150f });

        ExecutionPlan plan = executor.Plan(view, RoutedMove(1, 1100f, -600f, 2, 250f, -400f, 950f, -400f), 1.0);
        AssertTrue(plan.Execute, "the move executes");
        AssertEqual(1, plan.ViaCount, "via 1 stands in water and is dropped; via 0 stays despite the friendly group on it");
        AssertNear(250f, plan.ViaX0, 0.01f, "via 0 kept");

        plan = ExecutorOver(null).Plan(view, RoutedMove(1, 1100f, -600f, 2, -50f, -1500f, 950f, -400f), 1.0);
        AssertTrue(plan.Execute, "clamped via points still execute");
        AssertEqual(2, plan.ViaCount, "both kept");
        AssertNear(1f, plan.ViaX0, 0.01f, "x clamps to 1 like RegimentSetPath (131057-131068)");
        AssertNear(-1198f, plan.ViaZ0, 0.01f, "z clamps to -(size - 2)");

        // A move without a route plans no via points whatever the intent's stale fields hold.
        BattleOrderIntent plain = RoutedMove(1, 1100f, -600f, 0, 250f, -400f, 950f, -400f);
        AssertEqual(0, ExecutorOver(null).Plan(view, plain, 1.0).ViaCount, "ViaCount 0 means none");
    }

    // The outcome and the dedupe belong to the destination: a route to the same target is a duplicate.
    private static void DedupeComparesFinalTargetOnly()
    {
        var view = new FixtureView();
        int g = view.Add(1, 100f, -600f);
        OrderExecutor executor = ExecutorOver(null);
        BattleOrderIntent routed = RoutedMove(1, 1100f, -600f, 2, 250f, -400f, 950f, -400f);
        ExecutionPlan first = executor.Plan(view, routed, 1.0);
        AssertTrue(first.Execute, "first issue");
        _issued[0] = new IssuedTarget { InstanceId = 1, X = 1100f, Z = -600f };
        executor.RecordIssued(routed, 1.0, _issued, 1, null, 0, 0, 1);
        view.Own[g].RegimentPaths = 1;
        view.Own[g].LastWaypointX = 1100f;
        view.Own[g].LastWaypointZ = -600f;

        BattleOrderIntent otherRoute = RoutedMove(1, 1100f, -600f, 1, 300f, -300f, 0f, 0f);
        ExecutionPlan again = executor.Plan(view, otherRoute, 5.0);
        AssertFalse(again.Execute, "a different via list to the same destination is no new order");
        AssertEqual(SkipReason.Duplicate, again.Skip, "duplicate on the final target");
        BattleOrderIntent unrouted = RoutedMove(1, 1100f, -600f, 0, 0f, 0f, 0f, 0f);
        AssertEqual(SkipReason.Duplicate, executor.Plan(view, unrouted, 5.0).Skip, "and so is the unrouted move");
    }

    // 6953-6956 vanilla order: a move that avoids covered crossings with a non-empty perceived
    // list goes through BlockedMovingOrder, which has no via points (safety before speed).
    private static void CoveredCrossingMoveTakesNoVia()
    {
        BattleOrderIntent w = RoutedMove(1, 1100f, -600f, 2, 250f, -400f, 950f, -400f);
        w.Flags |= IntentFlags.AvoidCoveredCrossings;
        AssertTrue(OrderExecutor.TakesBlockedMovingOrder(w, 3), "covered crossings: the blocked-order path");
        AssertEqual(0, OrderExecutor.ViaLegs(w, coveredPoints: 3, planned: 2), "which carries no via leg");
        AssertEqual(2, OrderExecutor.ViaLegs(w, coveredPoints: 0, planned: 2), "no perceived covered point: the plain SetWaypoint legs");
        w.Flags &= ~IntentFlags.AvoidCoveredCrossings;
        AssertEqual(2, OrderExecutor.ViaLegs(w, coveredPoints: 3, planned: 2), "an unflagged move never takes the blocked order");
    }

    // Runtime pins (the harness cannot load the Unity half): the posture pipeline resolves a
    // route before it builds the request and passes it on; the reserve release attaches via points;
    // the executor issues one SetWaypoint per leg with newpath only on the first; the flag gates it.
    private static void RoutingIsWiredIntoThePostureAndExecutorRuntime()
    {
        string posture = StripComments(System.IO.File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/Producers/PostureIntentProducer.cs")));
        AssertTrue(posture.Contains("PostureRoutes.Resolve("), "the pipeline resolves a route");
        AssertTrue(posture.Contains("ViaCount = roadRoute.Use ? roadRoute.ViaCount : 0"), "and puts the via points on the request");
        AssertTrue(posture.Contains("ColumnWait = columnWait"), "and the column wait");
        AssertTrue(posture.Contains("Plugin.EnableDeploymentAndMarchDoctrine.Value"), "the doctrine flag gates the route");
        string reserve = StripComments(System.IO.File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/Producers/ReserveReleaseIntentProducer.cs")));
        AssertTrue(reserve.Contains("PostureRoutes.AttachIfInColumn("), "the reserve release routes too");
        string exec = StripComments(System.IO.File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs")));
        AssertTrue(exec.Contains("OrderExecutor.ViaLegs(w, 0, plan.ViaCount)"), "the executor reads the planned via legs");
        AssertEqual(1, System.Text.RegularExpressions.Regex.Matches(exec, @"newPath: true,\s*\n?\s*OrderExecutor\.LegHeading").Count, "only the first leg starts a new path");
        AssertTrue(System.Text.RegularExpressions.Regex.Matches(exec, @"newPath: false").Count == 2, "the second via leg and the final leg append");
    }

    private static float Dist(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // The route cache is keyed by (group, 50 m target bucket) and entries live 0.25 doctrine-h; a long
    // battle must not keep every key it ever saw. Expired entries go when the cache fills, and a hard
    // ceiling bounds it within one hour of doctrine time.
    private static void RouteCacheIsPrunedDuringABattle()
    {
        var cache = new RoutePlanCache();
        var route = new RoadRoute { Use = true, ViaCount = 1 };
        for (int k = 0; k < 400; k++) cache.Put(k, in route, 1.0);
        AssertTrue(cache.TryGet(5, 1.1, out _), "a fresh entry is served");
        AssertFalse(cache.TryGet(5, 1.0 + RoutePlanCache.LifetimeHours, out _), "an expired entry is not served");
        AssertFalse(cache.TryGet(5, 0.5, out _), "nor one stamped in the future of the clock (a new battle)");
        cache.Put(1000, in route, 2.0);   // the cache is over its prune size: the expired 400 go
        AssertTrue(cache.Count < 10, "expired entries were pruned: " + cache.Count);
        for (int k = 0; k < 5000; k++) cache.Put(10000 + k, in route, 3.0);
        AssertTrue(cache.Count <= RoutePlanCache.MaxEntries, "the ceiling holds within one instant: " + cache.Count);
        string posture = Code("src/WhiskeyRealism/Tactical/Battle/Producers/PostureIntentProducer.cs");
        AssertTrue(posture.Contains("RoutePlanCache") && !posture.Contains("Dictionary<long, CachedRoute>"), "the posture routes use the pruned cache");
    }
}
