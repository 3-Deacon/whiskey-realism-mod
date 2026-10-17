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
            ("river crossing an unready picture lets the leg through and says it had no map", AnUnreadyPictureLetsTheLegThrough),
            ("river crossing an end on the river takes the bank the leg reaches or leaves it by (smoke 2026-10-17: a leg whose end read no region passed as one bank)", AnEndOnTheRiverTakesTheBankAlongTheLeg),
            ("river crossing an end with no land along the leg within reach stays unjudged and says so", AnEndWithNoLandAlongTheLegStaysUnjudged),
            ("river crossing a move waits for the river map while it is being built, for a bounded time", AMoveWaitsForTheRiverMapForABoundedTime),
            ("river crossing a leg over several rivers is routed link by link and the first two crossings are the vias", ALegOverSeveralRiversIsRoutedLinkByLink),
            ("river crossing a refused leg names the first unknown crossing of its best route and asks for a look at it", ARefusedLegAsksForALook),
            ("river crossing a held crossing is no look and a crossing the plan chose is not refused", AHeldCrossingIsNoLookAndAPlannedOneIsNotRefused),
            ("river crossing a screen move across a river with no bridge stays and wants the ford seen", AScreenMoveAcrossARiverWithNoBridgeStays),
            ("river crossing a screen move across a river with a known ford goes by the ford", AScreenMoveWithAKnownFordGoesByTheFord),
            ("river crossing a screen move over two rivers goes by both crossings", AScreenMoveOverTwoRiversGoesByBoth),
            ("river crossing a planned or road via is kept and the gate does not touch the move", APlannedViaIsKept),
            ("river crossing an attack the plan sent across a held crossing is made", AnAttackAcrossAPlannedHeldCrossingIsMade),
            ("river crossing a Screen or Probe never crosses a Held crossing, even one the plan chose; an attack may", OnlyAnAttackCrossesAHeldCrossingThePlanChose),
            ("river crossing every leg of a move with a via is gated", EveryLegOfAMoveWithAViaIsGated),
            ("river crossing the search over 252 links is bounded", TheSearchOver252LinksIsBounded),
            ("river crossing two maps with the same number of links do not share a link graph", TwoMapsDoNotShareALinkGraph),
            ("river crossing a leg after a via gets its crossing within the two-via bound, else the move is refused", ALegAfterAViaGetsItsCrossingOrTheMoveIsRefused),
            ("river crossing the link graph is battle state and goes with the battle", TheLinkGraphGoesWithTheBattle),
            ("river crossing a road march takes its own crossing: Clear or Watched goes, Held only for an attack, Unknown is refused with a look", ARoadMarchTakesItsOwnCrossing),
            ("river crossing a road march is checked at the crossing the road takes, not at the nearest one", ARoadMarchIsCheckedAtItsOwnCrossing),
            ("river crossing a road flag on a move with one via is ignored", ARoadFlagOnOneViaIsIgnored),
            ("river crossing a road leg that leaves its bank and returns to it is checked at both crossings", AnOutAndBackRoadLegIsChecked),
            ("river crossing recorded road links are used only when they chain from the leg's start region to its target region, else the full search decides", RecordedRoadLinksMustChain),
            ("river crossing the road legs around a road route are gated and get vias within the bound; the route's flag comes from the posture request", TheLegsAroundARoadRouteAreGated),
            ("river crossing the division play and the reserve release are gated like the posture executor", DivisionPlayAndReserveReleaseAreGated),
            ("river crossing a withdrawal, a fallback, a scout's look and a placement are not gated", WithdrawalsAndScoutsAreNotGated),
            ("river crossing no gate leaves the move as validated", NoGateLeavesTheMove),
            ("river crossing a pending river map refuses the gated move for now and asks again, an ungated move goes", APendingMapRefusesTheGatedMoveForNow),
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
        // A fixture that lays open river (or a ford block) between banks sets Water: points it holds read as no land.
        internal Func<float, float, bool> Water;
        public int RegionAt(float x, float z) => x < 0f || (Water != null && Water(x, z)) ? -1 : x < 500f ? 0 : x < 1000f ? 1 : x < 1500f ? 2 : 3;
        public int LinkCount => Links.Count;
        public object Topology => this;
        public int RegionsOfLink(int link, Span<int> regions) { regions[0] = Links[link].a; regions[1] = Links[link].b; return 2; }
        public void LinkPoint(int link, out float x, out float z) { x = Links[link].x; z = Links[link].z; }
        public CrossingState StateOf(int link) => Links[link].state;
        public bool IsPlanned(int link) => Planned.Contains(link);
        public void RequestLook(int link) => Looks.Add(link);
    }

    internal sealed class Gate : ICrossingGate
    {
        internal readonly Picture Picture = new Picture();
        public CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, bool attack, in RoadLeg road, out CrossingRoute route)
            => RiverCrossingRule.Decide(Picture, x0, z0, x1, z1, attack, road, out route);
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
        AssertEqual(CrossingVerdict.SameBank, RiverCrossingRule.Decide(p, 100f, -100f, 700f, -100f, out CrossingRoute route), "no picture, no wall");
        AssertTrue(route.PictureNotReady, "and the route says it had no map, so a pass is not read as one bank");
        AssertEqual(-1, route.FromRegion, "no region judged");
        RiverCrossingRule.Decide(new Picture(), 100f, -100f, 400f, -100f, out CrossingRoute same);
        AssertFalse(same.PictureNotReady, "control: a ready picture says so");
        AssertEqual(0, same.FromRegion, "the start's region");
        AssertEqual(0, same.ToRegion, "the target's region");
    }

    // The river lies between x = 480 and 520 (water), a ford block at x = 700 to 710. A group standing in the river belongs to the bank its leg takes
    // it to; a target laid on the river line belongs to the bank the leg reaches it from.
    private static void AnEndOnTheRiverTakesTheBankAlongTheLeg()
    {
        Func<float, float, bool> water = (x, z) => (x >= 480f && x < 520f) || (x >= 700f && x < 710f);
        var none = new Picture { Water = water };
        // Standing in the first river, going east: it is leaving the river for region 1 and crosses nothing.
        AssertEqual(CrossingVerdict.SameBank, RiverCrossingRule.Decide(none, 500f, -100f, 800f, -100f, out CrossingRoute east), "a start in the river heading for the east bank");
        AssertEqual(1, east.FromRegion, "the east bank");
        AssertTrue(east.FromResolved, "resolved along the leg");
        AssertFalse(east.EndUnresolved, "judged");
        // The same start heading west leaves it for region 0.
        AssertEqual(CrossingVerdict.SameBank, RiverCrossingRule.Decide(none, 500f, -100f, 100f, -100f, out CrossingRoute west), "a start in the river heading for the west bank");
        AssertEqual(0, west.FromRegion, "the west bank");
        // A start in the river, a target two banks on: the leg leaves for region 1 and needs a crossing into region 2 that is not known.
        AssertEqual(CrossingVerdict.NoKnownCrossing, RiverCrossingRule.Decide(none, 500f, -100f, 1200f, -100f, out CrossingRoute far), "region 1 to region 2, no crossing");
        AssertEqual(1, far.FromRegion, "taken east");
        AssertEqual(2, far.ToRegion, "the target");
        // A target on a ford block, reached from the west across the river at x = 500: the block belongs to region 1, the bank the leg reaches it from.
        AssertEqual(CrossingVerdict.NoKnownCrossing, RiverCrossingRule.Decide(none, 100f, -100f, 705f, -100f, out CrossingRoute ford), "region 0 to the block, no crossing known");
        AssertEqual(1, ford.ToRegion, "the bank the leg reaches the block from");
        AssertTrue(ford.ToResolved, "resolved along the leg");
        // The known crossing carries that leg, with the end resolved: before, a block target read no region and the leg passed as one bank.
        var known = new Picture { Water = water };
        known.Links.Add((500f, -100f, CrossingState.Clear, 0, 1));
        AssertEqual(CrossingVerdict.ViaCrossing, RiverCrossingRule.Decide(known, 100f, -100f, 705f, -100f, out CrossingRoute via), "a known crossing carries the leg to the ford block");
        AssertEqual(500f, via.ViaX0, "by that crossing");
    }

    private static void AnEndWithNoLandAlongTheLegStaysUnjudged()
    {
        // A start 300 m or more out in water, with the nearest land past the reach: nothing to judge by.
        var lake = new Picture { Water = (x, z) => x >= 100f && x < 800f };
        AssertEqual(CrossingVerdict.SameBank, RiverCrossingRule.Decide(lake, 400f, -100f, 1200f, -100f, out CrossingRoute far), "no land within reach of the start");
        AssertTrue(far.EndUnresolved, "unjudged: the pass is for want of a bank");
        AssertFalse(far.PictureNotReady, "the picture was ready");
        // Land inside the reach resolves; the walk goes along the leg only.
        var shore = new Picture { Water = (x, z) => x >= 100f && x < 430f };
        AssertEqual(0, RiverCrossingRule.LandRegionAlong(shore, 400f, -100f, 1200f, -100f), "east: region 0 starts at 430");
        AssertEqual(-1, RiverCrossingRule.LandRegionAlong(shore, 400f, -100f, 100f, -100f), "west: water to the reach, no land");
        AssertEqual(-1, RiverCrossingRule.LandRegionAlong(shore, 400f, -100f, 400f, -100f), "no leg, no direction");
        AssertEqual(2, RiverCrossingRule.LandRegionAlong(shore, 1200f, -100f, 400f, -100f), "an end on land is its own region");
        AssertEqual(CrossingVerdict.NoKnownCrossing, RiverCrossingRule.Decide(shore, 400f, -100f, 1200f, -100f, out CrossingRoute near), "region 0 to region 2: a crossing that is not known");
        AssertFalse(near.EndUnresolved, "judged");
    }

    private static void AMoveWaitsForTheRiverMapForABoundedTime()
    {
        var wait = new CrossingPictureWait();
        double t = 5.0;
        AssertTrue(wait.Waits(pending: true, t), "the first pending move waits");
        AssertTrue(wait.Waits(pending: true, t + CrossingPictureWait.CapHours * 0.9), "inside the cap");
        AssertFalse(wait.Waits(pending: true, t + CrossingPictureWait.CapHours * 1.01), "the cap runs out: the gate opens");
        AssertFalse(wait.Waits(pending: true, t + 1.0), "and stays open while the map is pending");
        AssertFalse(wait.Waits(pending: false, t + 1.1), "the map arrived (or never will): nothing waits");
        AssertTrue(wait.Waits(pending: true, t + 2.0), "a later pending spell waits afresh");
        wait.Clear();
        AssertTrue(wait.Waits(pending: true, t + 2.5), "a cleared wait forgets the spell it was counting and starts a new one");
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
        AssertEqual(CrossingVerdict.NoKnownCrossing, Decide(p, 100f, 100f, 700f, 100f), "a Held crossing the plan chose is still no way across for a move that is not an attack");
        var held = RiverCrossingRule.Decide(p, 100f, 100f, 700f, 100f, attack: true, out CrossingRoute byPlan);
        AssertEqual(CrossingVerdict.ViaCrossing, held, "an attack goes by the Held crossing the plan chose");
        AssertEqual(500f, byPlan.ViaX0, "by that crossing");
        AssertEqual(0, p.Looks.Count, "with nothing to look at for it");
        p.Links[0] = (500f, 100f, CrossingState.Unknown, 0, 1);
        AssertEqual(CrossingVerdict.ViaCrossing, Decide(p, 100f, 100f, 700f, 100f), "an unknown crossing the plan chose is not refused");
        AssertEqual(0, p.Looks.Count, "which needs no look of the gate's");
        p.Links[0] = (500f, 100f, CrossingState.Clear, 0, 1);
        AssertEqual(CrossingVerdict.ViaCrossing, Decide(p, 100f, 100f, 700f, 100f), "a planned crossing the side knows is a via like any other");
    }

    private static void OnlyAnAttackCrossesAHeldCrossingThePlanChose()
    {
        var gate = new Gate();
        gate.Picture.Links.Add((500f, 100f, CrossingState.Held, 0, 1));
        gate.Picture.Planned.Add(0);
        foreach (CommandTaskType task in new[] { CommandTaskType.Screen, CommandTaskType.Probe, CommandTaskType.AdvanceToAssembly, CommandTaskType.Scout, CommandTaskType.ReleaseReserve })
            AssertEqual(SkipReason.NoKnownCrossing, Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, task), 1.0).Skip,
                task + " never crosses a Held crossing, planned or not");
        foreach (CommandTaskType task in new[] { CommandTaskType.AttackObjective, CommandTaskType.SupportAttack, CommandTaskType.FixEnemy, CommandTaskType.Counterstroke })
        {
            ExecutionPlan plan = Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.DivisionPlay, task), 1.0);
            AssertTrue(plan.Execute, task + " may cross the Held crossing the plan chose");
            AssertEqual(1, plan.ViaCount, "by it");
        }
    }

    // verify r2: the gate skipped a move that had a via, so the leg after a road via was never checked.
    private static void EveryLegOfAMoveWithAViaIsGated()
    {
        BattleOrderIntent WithVia(float vx, float vz, float tx)
        {
            BattleOrderIntent m = Move(tx, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen);
            m.ViaCount = 1;
            m.ViaX0 = vx;
            m.ViaZ0 = vz;
            return m;
        }
        var none = new Gate();
        AssertEqual(SkipReason.NoKnownCrossing, Executor(none).Plan(new View(100f, 100f), WithVia(300f, -200f, 700f), 1.0).Skip,
            "a via on the own bank does not excuse the leg after it across an unbridged river");
        AssertTrue(Executor(none).Plan(new View(100f, 100f), WithVia(300f, -200f, 450f), 1.0).Execute, "legs that cross nothing are made, via kept");
        AssertEqual(SkipReason.NoKnownCrossing, Executor(none).Plan(new View(100f, 100f), WithVia(700f, -200f, 400f), 1.0).Skip, "nor the leg before it");

        var ford = new Gate();
        ford.Picture.Links.Add((500f, 600f, CrossingState.Clear, 0, 1));
        ExecutionPlan kept = Executor(ford).Plan(new View(100f, 100f), WithVia(300f, -200f, 700f), 1.0);
        AssertTrue(kept.Execute, "with a known ford the leg after the via is crossable");
        AssertEqual(2, kept.ViaCount, "the via the mover chose stays and the leg after it gets its crossing");
        AssertEqual(300f, kept.ViaX0, "the mover's via first, as it was");
        AssertEqual(500f, kept.ViaX1, "then the ford the second leg needs");
    }

    // Four banks joined by 63 parallel links each (252 links), every link Clear: the worst the rule meets on a real map is far smaller (verify r2: 2 links).
    private sealed class Wide : ICrossingPicture
    {
        private readonly List<(float x, float z, int a, int b)> _links = new List<(float, float, int, int)>();
        public Wide()
        {
            for (int bank = 0; bank < 3; bank++)
                for (int k = 0; k < 84; k++) _links.Add((500f * (bank + 1), 10f * k, bank, bank + 1));
        }
        public bool Ready => true;
        public int RegionAt(float x, float z) => x < 0f ? -1 : Math.Min((int)(x / 500f), 3);
        public int LinkCount => _links.Count;
        public int RegionsOfLink(int link, Span<int> regions) { regions[0] = _links[link].a; regions[1] = _links[link].b; return 2; }
        public void LinkPoint(int link, out float x, out float z) { x = _links[link].x; z = _links[link].z; }
        public CrossingState StateOf(int link) => CrossingState.Clear;
        public bool IsPlanned(int link) => false;
        public void RequestLook(int link) { }
        public object Topology => this;
    }

    // The link graph is built once per map (its Topology); a second map with as many links but other regions must not read the first's.
    private static void TwoMapsDoNotShareALinkGraph()
    {
        var joined = new Picture();
        joined.Links.Add((500f, 100f, CrossingState.Clear, 0, 1));
        AssertEqual(CrossingVerdict.ViaCrossing, Decide(joined, 100f, 100f, 700f, 100f), "(the first map: its one link joins the two banks)");
        var other = new Picture();
        other.Links.Add((500f, 100f, CrossingState.Clear, 0, 2));
        AssertEqual(CrossingVerdict.NoKnownCrossing, Decide(other, 100f, 100f, 700f, 100f), "the second map's one link joins other regions: no crossing, whatever the first map's graph said");
        AssertEqual(CrossingVerdict.ViaCrossing, Decide(joined, 100f, 100f, 700f, 100f), "and the first map still answers for itself");
    }

    // Open item of verify r3: a leg after a via was checked but never given its crossing.
    private static void ALegAfterAViaGetsItsCrossingOrTheMoveIsRefused()
    {
        BattleOrderIntent WithVia(float vx, float tx)
        {
            BattleOrderIntent m = Move(tx, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen);
            m.ViaCount = 1;
            m.ViaX0 = vx;
            m.ViaZ0 = -200f;
            return m;
        }
        var ford = new Gate();
        ford.Picture.Links.Add((500f, -300f, CrossingState.Clear, 0, 1));
        // The via is across the river: the first leg needs the ford, ahead of the via; the second leg is on the far bank.
        ExecutionPlan a = Executor(ford).Plan(new View(100f, 100f), WithVia(700f, 900f), 1.0);
        AssertTrue(a.Execute, "made: " + a.Skip);
        AssertEqual(2, a.ViaCount, "the ford and the mover's via");
        AssertEqual(500f, a.ViaX0, "the ford first (the leg before the via needs it)");
        AssertEqual(700f, a.ViaX1, "then the mover's via");
        AssertEqual(900f, a.X, "to the target");

        // Out and back over the same ford is three vias: more than the executor carries, so the move is refused.
        ExecutionPlan b = Executor(ford).Plan(new View(100f, 100f), WithVia(700f, 400f), 1.0);
        AssertFalse(b.Execute, "a move that would need three vias is not made");
        AssertEqual(SkipReason.NoKnownCrossing, b.Skip, "and says no crossing it can route");

        // A via and a leg over two rivers: also three.
        var two = new Gate();
        two.Picture.Links.Add((500f, -300f, CrossingState.Clear, 0, 1));
        two.Picture.Links.Add((1000f, -300f, CrossingState.Clear, 1, 2));
        ExecutionPlan c = Executor(two).Plan(new View(100f, 100f), WithVia(300f, 1200f), 1.0);
        AssertEqual(SkipReason.NoKnownCrossing, c.Skip, "a mover's via plus two crossings is refused");
        // Without the via the same leg takes both crossings.
        ExecutionPlan d = Executor(two).Plan(new View(100f, 100f), Move(1200f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0);
        AssertEqual(2, d.ViaCount, "(and without a via the same leg is two vias)");
        // A via that needs nothing is kept alone.
        ExecutionPlan e = Executor(ford).Plan(new View(100f, 100f), WithVia(300f, 400f), 1.0);
        AssertEqual(1, e.ViaCount, "a via on a leg that crosses nothing stays the only one");
        AssertEqual(300f, e.ViaX0, "as it was");
    }

    // verify landing F9: a BC10 road march (vias at the first road entry and the last road exit) over a bridge between them.
    private static BattleOrderIntent RoadMarch(float entryX, float exitX, float targetX, bool roadFlag, CommandTaskType task = CommandTaskType.AdvanceToAssembly)
    {
        BattleOrderIntent m = Move(targetX, 100f, IntentSource.PostureExecutor, task, roadFlag ? IntentFlags.RoadRoute : IntentFlags.None);
        m.ViaCount = 2;
        m.ViaX0 = entryX;
        m.ViaZ0 = -200f;
        m.ViaX1 = exitX;
        m.ViaZ1 = -200f;
        return m;
    }

    private static void ARoadMarchTakesItsOwnCrossing()
    {
        // Vias at 300 and 800 (the bridge at 500 lies between them), start and target on the banks of the vias.
        var clear = new Gate();
        clear.Picture.Links.Add((500f, -200f, CrossingState.Clear, 0, 1));
        ExecutionPlan ok = Executor(clear).Plan(new View(100f, 100f), RoadMarch(300f, 800f, 900f, roadFlag: true), 1.0);
        AssertTrue(ok.Execute, "a road march over a Clear bridge goes: " + ok.Skip);
        AssertEqual(2, ok.ViaCount, "with the road's two vias, no third");
        AssertEqual(300f, ok.ViaX0, "the road entry");
        AssertEqual(800f, ok.ViaX1, "the road exit");
        // Without the road flag the same two vias are an ordinary move: the bridge would be a third via and the move is refused.
        AssertEqual(SkipReason.NoKnownCrossing, Executor(clear).Plan(new View(100f, 100f), RoadMarch(300f, 800f, 900f, roadFlag: false), 1.0).Skip,
            "(control) two vias that are not a road route cannot take a third for the bridge");

        var watched = new Gate();
        watched.Picture.Links.Add((500f, -200f, CrossingState.Watched, 0, 1));
        AssertTrue(Executor(watched).Plan(new View(100f, 100f), RoadMarch(300f, 800f, 900f, roadFlag: true), 1.0).Execute, "a Watched bridge goes too");

        // A Held bridge goes only for an attack-stance task whose selected plan chose that crossing, the rule of a move off the road.
        var held = new Gate();
        held.Picture.Links.Add((500f, -200f, CrossingState.Held, 0, 1));
        foreach (CommandTaskType task in new[] { CommandTaskType.Screen, CommandTaskType.AdvanceToAssembly, CommandTaskType.AttackObjective })
            AssertEqual(SkipReason.NoKnownCrossing, Executor(held).Plan(new View(100f, 100f), RoadMarch(300f, 800f, 900f, roadFlag: true, task), 1.0).Skip,
                task + " over a Held bridge the plan did not choose is refused");
        held.Picture.Planned.Add(0);
        foreach (CommandTaskType task in new[] { CommandTaskType.Screen, CommandTaskType.AdvanceToAssembly })
            AssertEqual(SkipReason.NoKnownCrossing, Executor(held).Plan(new View(100f, 100f), RoadMarch(300f, 800f, 900f, roadFlag: true, task), 1.0).Skip,
                task + " over a Held bridge the plan chose is refused too");
        ExecutionPlan assault = Executor(held).Plan(new View(100f, 100f), RoadMarch(300f, 800f, 900f, roadFlag: true, CommandTaskType.AttackObjective), 1.0);
        AssertTrue(assault.Execute, "an attack march over the Held bridge the plan chose goes: " + assault.Skip);
        AssertEqual(2, assault.ViaCount, "on the road's vias");
        AssertEqual(0, held.Picture.Looks.Count, "a Held bridge is no look");

        var unknown = new Gate();
        unknown.Picture.Links.Add((500f, -200f, CrossingState.Unknown, 0, 1));
        ExecutionPlan blind = Executor(unknown).Plan(new View(100f, 100f), RoadMarch(300f, 800f, 900f, roadFlag: true), 1.0);
        AssertEqual(SkipReason.NoKnownCrossing, blind.Skip, "an Unknown bridge refuses the march");
        AssertEqual(1, unknown.Picture.Looks.Count, "and posts the look");
        AssertEqual(0, unknown.Picture.Looks[0], "at that bridge");
        unknown.Picture.Planned.Add(0);
        AssertTrue(Executor(unknown).Plan(new View(100f, 100f), RoadMarch(300f, 800f, 900f, roadFlag: true), 1.0).Execute, "(a bridge the plan chose is not refused)");

        // Road vias on one bank: no crossing to check.
        AssertTrue(Executor(new Gate()).Plan(new View(100f, 100f), RoadMarch(200f, 400f, 450f, roadFlag: true), 1.0).Execute, "a road march on one bank is not touched");
    }

    // verify landing r2 #3: a nearer Unknown ford off the road does not refuse a march over the Clear bridge the road takes.
    private static void ARoadMarchIsCheckedAtItsOwnCrossing()
    {
        var gate = new Gate();
        gate.Picture.Links.Add((500f, -900f, CrossingState.Clear, 0, 1));      // the bridge: far from the vias' line, on the road
        gate.Picture.Links.Add((500f, -200f, CrossingState.Unknown, 0, 1));    // an unseen ford beside the straight line, off the road
        BattleOrderIntent onTheRoad = RoadMarch(300f, 800f, 900f, roadFlag: true);
        onTheRoad.RoadLinkCount = 1;
        onTheRoad.RoadLink0 = 0;
        ExecutionPlan ok = Executor(gate).Plan(new View(100f, 100f), onTheRoad, 1.0);
        AssertTrue(ok.Execute, "the march goes: the road takes the Clear bridge, not the ford: " + ok.Skip);
        AssertEqual(0, gate.Picture.Looks.Count, "and the ford off the road is not asked about");
        // Without the road's links the crossing is taken to be the best route over every link: the nearer, Unknown ford.
        ExecutionPlan guess = Executor(gate).Plan(new View(100f, 100f), RoadMarch(300f, 800f, 900f, roadFlag: true), 1.0);
        AssertEqual(SkipReason.NoKnownCrossing, guess.Skip, "(control) with no road links the nearest crossing is what is checked");
        AssertEqual(1, gate.Picture.Looks[0], "and it is the ford that is asked about");
        // The road's own crossing unseen: refused, with the look at that one.
        var blind = new Gate();
        blind.Picture.Links.Add((500f, -900f, CrossingState.Unknown, 0, 1));
        blind.Picture.Links.Add((500f, -200f, CrossingState.Clear, 0, 1));
        BattleOrderIntent roadUnseen = RoadMarch(300f, 800f, 900f, roadFlag: true);
        roadUnseen.RoadLinkCount = 1;
        roadUnseen.RoadLink0 = 0;
        AssertEqual(SkipReason.NoKnownCrossing, Executor(blind).Plan(new View(100f, 100f), roadUnseen, 1.0).Skip, "a road over a bridge nobody has seen is refused though a ford is Clear");
        AssertEqual(0, blind.Picture.Looks[0], "with the look at the road's bridge");
    }

    private static BattleOrderIntent RoadMarchWith(float entryX, float exitX, float targetX, params int[] links)
    {
        BattleOrderIntent m = RoadMarch(entryX, exitX, targetX, roadFlag: true);
        m.RoadLinkCount = links.Length;
        if (links.Length > 0) m.RoadLink0 = links[0];
        if (links.Length > 1) m.RoadLink1 = links[1];
        return m;
    }

    // verify landing r3 F11: the road leg's recorded links are the road's crossings only if they chain from the leg's start region to its
    // target region. Banks: region 0 below x 500, 1 below 1000, 2 below 1500. The road leg here runs from the vias at 300 (region 0) to 1200 (region 2).
    private static void RecordedRoadLinksMustChain()
    {
        // A road over two rivers with only the first crossing recorded: the record does not reach the target, so the full search decides, and
        // it finds the second river's crossing unseen.
        var half = new Gate();
        half.Picture.Links.Add((500f, -200f, CrossingState.Clear, 0, 1));
        half.Picture.Links.Add((1000f, -200f, CrossingState.Unknown, 1, 2));
        ExecutionPlan one = Executor(half).Plan(new View(100f, 100f), RoadMarchWith(300f, 1200f, 1300f, 0), 1.0);
        AssertEqual(SkipReason.NoKnownCrossing, one.Skip, "one crossing recorded for a two-river road leg falls back: the unseen second crossing refuses it");
        AssertEqual(1, half.Picture.Looks.Count, "and asks about it");
        AssertEqual(1, half.Picture.Looks[0], "the second river's crossing");

        // The same road with both crossings seen goes, recorded in road order or not recorded at all.
        var both = new Gate();
        both.Picture.Links.Add((500f, -200f, CrossingState.Clear, 0, 1));
        both.Picture.Links.Add((1000f, -200f, CrossingState.Watched, 1, 2));
        AssertTrue(Executor(both).Plan(new View(100f, 100f), RoadMarchWith(300f, 1200f, 1300f, 0, 1), 1.0).Execute, "two crossings in road order go");
        AssertTrue(Executor(both).Plan(new View(100f, 100f), RoadMarchWith(300f, 1200f, 1300f), 1.0).Execute, "and with none recorded the search finds them");

        // A stray recorded link that joins other regions is not the road's crossing: the real bridge, unseen, still refuses the march.
        var stray = new Gate();
        stray.Picture.Links.Add((1200f, -200f, CrossingState.Clear, 2, 3));     // a ford far down the river, joins other banks
        stray.Picture.Links.Add((500f, -900f, CrossingState.Unknown, 0, 1));    // the bridge the road really takes
        stray.Picture.Links.Add((1000f, -900f, CrossingState.Clear, 1, 2));
        ExecutionPlan used = Executor(stray).Plan(new View(100f, 100f), RoadMarchWith(300f, 1200f, 1300f, 0), 1.0);
        AssertEqual(SkipReason.NoKnownCrossing, used.Skip, "a recorded link that joins other regions is not used in place of the bridge");
        AssertEqual(1, stray.Picture.Looks.Count, "the look is at the bridge");
        AssertEqual(1, stray.Picture.Looks[0], "not the stray");

        // Reverse order is not a chain: the first recorded link must touch the start region. Both rivers unseen, so the look says which came first.
        var unseen = new Gate();
        unseen.Picture.Links.Add((500f, -200f, CrossingState.Unknown, 0, 1));
        unseen.Picture.Links.Add((1000f, -200f, CrossingState.Unknown, 1, 2));
        AssertEqual(SkipReason.NoKnownCrossing, Executor(unseen).Plan(new View(100f, 100f), RoadMarchWith(300f, 1200f, 1300f, 1, 0), 1.0).Skip, "reversed links refuse (unseen)");
        AssertEqual(0, unseen.Picture.Looks[0], "and the look is at the first river met, the full search's order, not the record's");
        var inOrder = new Gate();
        inOrder.Picture.Links.AddRange(unseen.Picture.Links);
        Executor(inOrder).Plan(new View(100f, 100f), RoadMarchWith(300f, 1200f, 1300f, 0, 1), 1.0);
        AssertEqual(0, inOrder.Picture.Looks[0], "(control) in order the look is at the first river too");

        // Two links that do not share a middle region are no chain (0-1 then 2-3 for a 0 to 2 leg).
        var gap = new Gate();
        gap.Picture.Links.Add((500f, -200f, CrossingState.Clear, 0, 1));
        gap.Picture.Links.Add((1500f, -200f, CrossingState.Clear, 2, 3));
        gap.Picture.Links.Add((1000f, -200f, CrossingState.Unknown, 1, 2));
        AssertEqual(SkipReason.NoKnownCrossing, Executor(gap).Plan(new View(100f, 100f), RoadMarchWith(300f, 1200f, 1300f, 0, 1), 1.0).Skip,
            "links that do not meet are no chain: the full search finds the unseen link between the banks");
        AssertEqual(2, gap.Picture.Looks[0], "and looks at it");

        // An out-of-range recorded link is no chain.
        AssertTrue(Executor(both).Plan(new View(100f, 100f), RoadMarchWith(300f, 1200f, 1300f, 9), 1.0).Execute, "a recorded link the map does not have falls back to the search");
    }

    // verify landing r4 minor 1: a road leg whose vias lie on one bank but whose road crosses the river and comes back. Region 0 is below x 500;
    // the vias at 300 and 450 and the target at 480 are all on it.
    private static void AnOutAndBackRoadLegIsChecked()
    {
        var clear = new Gate();
        clear.Picture.Links.Add((500f, -200f, CrossingState.Clear, 0, 1));
        clear.Picture.Links.Add((500f, -900f, CrossingState.Clear, 0, 1));
        ExecutionPlan ok = Executor(clear).Plan(new View(100f, 100f), RoadMarchWith(300f, 450f, 480f, 0, 1), 1.0);
        AssertTrue(ok.Execute, "out and back over two Clear crossings goes: " + ok.Skip);
        AssertEqual(2, ok.ViaCount, "on the road's two vias, none added");

        var unseen = new Gate();
        unseen.Picture.Links.Add((500f, -200f, CrossingState.Clear, 0, 1));
        unseen.Picture.Links.Add((500f, -900f, CrossingState.Unknown, 0, 1));
        AssertEqual(SkipReason.NoKnownCrossing, Executor(unseen).Plan(new View(100f, 100f), RoadMarchWith(300f, 450f, 480f, 0, 1), 1.0).Skip,
            "the way back over a crossing nobody has seen refuses the leg although both ends lie on one bank");
        AssertEqual(1, unseen.Picture.Looks.Count, "with one look");
        AssertEqual(1, unseen.Picture.Looks[0], "at that crossing");

        var held = new Gate();
        held.Picture.Links.Add((500f, -200f, CrossingState.Held, 0, 1));
        held.Picture.Planned.Add(0);
        AssertEqual(SkipReason.NoKnownCrossing, Executor(held).Plan(new View(100f, 100f), RoadMarchWith(300f, 450f, 480f, 0, 0), 1.0).Skip,
            "out and back over the same Held crossing is refused for a march");
        BattleOrderIntent assault = RoadMarchWith(300f, 450f, 480f, 0, 0);
        assault.Task = CommandTaskType.AttackObjective;
        AssertTrue(Executor(held).Plan(new View(100f, 100f), assault, 1.0).Execute, "and goes for an attack the plan chose");

        // Nothing to check without a record, or when the record is not a chain from this bank back to it: the leg is on one bank.
        AssertTrue(Executor(unseen).Plan(new View(100f, 100f), RoadMarchWith(300f, 450f, 480f), 1.0).Execute, "no record, no check");
        var stray = new Gate();
        stray.Picture.Links.Add((1200f, -200f, CrossingState.Unknown, 2, 3));
        AssertTrue(Executor(stray).Plan(new View(100f, 100f), RoadMarchWith(300f, 450f, 480f, 0, 0), 1.0).Execute, "a record that joins other regions is not this leg's crossing");
        AssertTrue(Executor(unseen).Plan(new View(100f, 100f), RoadMarchWith(300f, 450f, 480f, 9), 1.0).Execute, "a link the map does not have is no crossing");

        // A move that is not a road route is not checked on one bank.
        BattleOrderIntent plain = RoadMarchWith(300f, 450f, 480f, 0, 1);
        plain.Flags &= ~IntentFlags.RoadRoute;
        AssertTrue(Executor(unseen).Plan(new View(100f, 100f), plain, 1.0).Execute, "(control) without the road flag the links are not read");
    }

    // verify landing r2 #2: the flag means a road route only with the road's two vias.
    private static void ARoadFlagOnOneViaIsIgnored()
    {
        var ford = new Gate();
        ford.Picture.Links.Add((500f, -300f, CrossingState.Clear, 0, 1));
        BattleOrderIntent oneVia = Move(900f, 100f, IntentSource.PostureExecutor, CommandTaskType.AdvanceToAssembly, IntentFlags.RoadRoute);
        oneVia.ViaCount = 1;
        oneVia.ViaX0 = 300f;
        oneVia.ViaZ0 = -200f;
        ExecutionPlan plan = Executor(ford).Plan(new View(100f, 100f), oneVia, 1.0);
        AssertTrue(plan.Execute, "made: " + plan.Skip);
        AssertEqual(2, plan.ViaCount, "the leg after the one via gets its ford as for any move");
        AssertEqual(500f, plan.ViaX1, "the ford");
    }

    private static void TheLegsAroundARoadRouteAreGated()
    {
        var bridge = new Gate();
        bridge.Picture.Links.Add((500f, -200f, CrossingState.Clear, 0, 1));
        // Before the road entry: a start across the river from the entry needs the ford, and the road's two vias leave no room for it.
        ExecutionPlan before = Executor(bridge).Plan(new View(100f, 100f), RoadMarch(700f, 800f, 900f, roadFlag: true), 1.0);
        AssertEqual(SkipReason.NoKnownCrossing, before.Skip, "a leg before the road entry that needs a crossing cannot get it past the two-via bound");
        // After the road exit likewise.
        ExecutionPlan after = Executor(bridge).Plan(new View(100f, 100f), RoadMarch(200f, 300f, 700f, roadFlag: true), 1.0);
        AssertEqual(SkipReason.NoKnownCrossing, after.Skip, "and a leg after the road exit");
        var none = new Gate();
        AssertEqual(SkipReason.NoKnownCrossing, Executor(none).Plan(new View(100f, 100f), RoadMarch(200f, 300f, 700f, roadFlag: true), 1.0).Skip,
            "a leg after the exit over a river with no crossing known is refused too");

        // The flag is the posture request's: a road route sets it, a maneuver via does not.
        var sink = new FlagSink();
        PostureIntentPlan.Submit(sink, new PostureIntentRequest
        {
            GroupId = 10, Action = PostureExecutionAction.SetWaypoint, Task = CommandTaskType.AdvanceToAssembly, HasTarget = true, TargetX = 900f, TargetZ = -100f,
            ViaCount = 2, ViaX0 = 300f, ViaZ0 = -200f, ViaX1 = 800f, ViaZ1 = -200f, RoadRoute = true, RoadLinkCount = 1, RoadLink0 = 7, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "t",
        });
        AssertTrue(sink.Moves.Count == 1 && (sink.Moves[0].Flags & IntentFlags.RoadRoute) != 0, "a road route's move carries the road flag");
        AssertTrue(sink.Moves[0].RoadLinkCount == 1 && sink.Moves[0].RoadLink0 == 7, "and the crossings the road takes");
        var plain = new FlagSink();
        PostureIntentPlan.Submit(plain, new PostureIntentRequest
        {
            GroupId = 10, Action = PostureExecutionAction.SetWaypoint, Task = CommandTaskType.AdvanceToAssembly, HasTarget = true, TargetX = 900f, TargetZ = -100f,
            ViaCount = 1, ViaX0 = 500f, ViaZ0 = -200f, RoadRoute = false, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "t",
        });
        AssertTrue(plain.Moves.Count == 1 && (plain.Moves[0].Flags & IntentFlags.RoadRoute) == 0, "a maneuver via does not");
    }

    private sealed class FlagSink : IIntentSink
    {
        public readonly List<BattleOrderIntent> Moves = new List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) { if (intent.Channel == OrderChannel.Move) Moves.Add(intent); }
    }

    // The link graph is built from the first map asked and kept; it goes with the battle (BattleScope) so the old map's regions are not held.
    private static void TheLinkGraphGoesWithTheBattle()
    {
        var p = new Picture();
        p.Links.Add((500f, 100f, CrossingState.Clear, 0, 1));
        Decide(p, 100f, 100f, 700f, 100f);
        var field = typeof(RiverCrossingRule).GetField("_graph", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        AssertTrue(field != null, "the graph cache exists");
        AssertTrue(field.GetValue(null) != null, "built by the first question");
        WhiskeyRealism.Util.BattleScope.ResetAll();
        AssertTrue(field.GetValue(null) == null, "dropped with the battle");
        AssertEqual(CrossingVerdict.ViaCrossing, Decide(p, 100f, 100f, 700f, 100f), "and built again when the next battle asks");
    }

    private static void TheSearchOver252LinksIsBounded()
    {
        var wide = new Wide();
        AssertEqual(252, wide.LinkCount, "(fixture) 252 links");
        RiverCrossingRule.Decide(wide, 100f, 400f, 1700f, 400f, out CrossingRoute warm);
        AssertEqual(3, warm.Crossings, "the leg over three rivers is routed link by link");
        const int calls = 400;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < calls; i++) RiverCrossingRule.Decide(wide, 100f + i % 7, 400f, 1700f, 400f, out _);
        watch.Stop();
        double ms = watch.Elapsed.TotalMilliseconds / calls;
        Console.WriteLine("INFO RIVER-ROUTE 252 links, 3 crossings: " + ms.ToString("0.000") + " ms a call (adjacency built once per map)");
        AssertTrue(ms < 1.0, "a call over 252 links stays under a millisecond: " + ms);
        long begin = TestFixtures.BeginAllocationWindow();
        for (int i = 0; i < 100; i++) RiverCrossingRule.Decide(wide, 100f, 400f, 1700f, 400f, out _);
        AssertEqual(0L, TestFixtures.AllocatedSince(begin), "and allocates nothing once the adjacency is built");
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
        AssertEqual(2, plan.ViaCount, "with its own via and the crossing its second leg needs");
        AssertEqual(300f, plan.ViaX0, "the first is the intent's, not the gate's crossing");
        AssertEqual(-200f, plan.ViaZ0, "the first is the intent's, not the gate's crossing");
        AssertEqual(500f, plan.ViaX1, "the second is the ford");
        AssertEqual(0, gate.Picture.Looks.Count, "and nothing asked for");
        var none = new Gate();
        AssertEqual(SkipReason.NoKnownCrossing, Executor(none).Plan(new View(100f, 100f), withVia, 1.0).Skip,
            "a via is kept, but the leg after it across a river the side knows no crossing for is still gated");
    }

    // verify F1: a planned attack across a Held crossing (Burnside's bridge) is the plan's decision.
    private static void AnAttackAcrossAPlannedHeldCrossingIsMade()
    {
        var gate = new Gate();
        gate.Picture.Links.Add((500f, 100f, CrossingState.Held, 0, 1));
        AssertEqual(SkipReason.NoKnownCrossing, Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0).Skip,
            "an unplanned move over a held crossing is refused");
        gate.Picture.Planned.Add(0);
        AssertEqual(SkipReason.NoKnownCrossing, Executor(gate).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.PostureExecutor, CommandTaskType.Screen), 1.0).Skip,
            "and so is a Screen, even over a crossing the plan chose");
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

    private sealed class PendingGate : ICrossingGate
    {
        public CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, bool attack, in RoadLeg road, out CrossingRoute route)
        {
            route = default;
            return CrossingVerdict.Pending;
        }
    }

    private static void APendingMapRefusesTheGatedMoveForNow()
    {
        var executor = Executor(new PendingGate());
        ExecutionPlan plan = executor.Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.ReserveRelease, CommandTaskType.ReleaseReserve), 1.0);
        AssertFalse(plan.Execute, "the river map is not built: the move does not pass unchecked");
        AssertEqual(SkipReason.CrossingPending, plan.Skip, "and says why, so the producer asks again next tick");
        AssertTrue(Executor(new PendingGate()).Plan(new View(100f, 100f), Move(700f, 100f, IntentSource.Withdrawal, CommandTaskType.FallBackToLine), 1.0).Execute,
            "a withdrawal is not gated");
    }
}
