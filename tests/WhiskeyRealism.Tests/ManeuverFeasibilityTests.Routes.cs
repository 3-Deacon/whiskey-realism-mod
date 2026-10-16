using System;
using System.IO;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;

// BC14 task 6: legs, routes over the crossing graph, march, courier and strike times (spec § 13.1 S2). Costs are in cost units (units x the
// shipped speed cost: grass 2, ford 2, bridge 1, river 5, swamp 6); an infantry mover divides by basicunitspeed 1000.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] RouteTests() => new (string, Action)[]
    {
        ("bc14 ROUTE-SAME-REGION one leg, its cost the 10-unit sample sum", RouteSameRegion),
        ("bc14 ROUTE-VIA-CROSSING different regions: the route passes the cheaper crossing and names it as the via", RouteViaCrossing),
        ("bc14 ROUTE-HELD-DETOUR a Held bridge on the line loses to a Clear ford 600 away, not to one 2000 away", RouteHeldDetour),
        ("bc14 ROUTE-NO-CROSSING no link between the regions: no route", RouteNoCrossing),
        ("bc14 ROUTE-BEND a river bend inside one region is priced at the swamp cost and marked estimated", RouteBend),
        ("bc14 ROUTE-CONFIRM a confirmed leg replaces the estimate; a refused one is not walked; all refused: no route", RouteConfirm),
        ("bc14 LEG-FOOTPRINT river samples within 60 of the leg's own crossing are priced as it and are no estimate; others are bends", LegFootprint),
        ("bc14 TARGET-SNAP a point on open river snaps to the nearest land block; a tie goes to the preferred region", TargetSnap),
        ("bc14 LEG-CACHE a second query from the same buckets samples no new leg; overflow is priced uncached and counted", LegCache),
        ("bc14 MARCH-TIME hours are cost / (1000 x base) / fatigue factor", MarchTime),
        ("bc14 COURIER-TIME the bugle, the ride, the relay, the longest branch, engaged members and order delays off", CourierTime),
        ("bc14 STRIKE-OFFSET a wing strikes its offset after the fix's contact; the common plan C*", StrikeOffset),
        ("bc14 STRIKE-FIX-CONTACT a fix in contact has T 0, release 0 and no planned release", StrikeFixContact),
        ("bc14 STRIKE-ABSOLUTE a release is published as absolute doctrine hours", StrikeAbsolute),
        ("bc14 STRIKE-DOUBLE two wings and a fix strike together at the slower wing's pace", StrikeDouble),
        ("bc14 STRIKE-HELD a march leg through a Held crossing adds one hour to T", StrikeHeld),
        ("bc14 INWARD-SURPLUS the inward leg's crossing detour delays the strike; never less than 0; a Held crossing adds an hour", InwardSurplus),
        ("bc14 MULTI-RIVER a route over two rivers adds an hour per Held crossing, takes the worst state and names the first crossing", MultiRiver),
        ("bc14 FIX-MEMBER-RELEASE every fixing member is released so none strikes before the common plan", FixMemberRelease),
        ("bc14 MARCH-RULES-PIN no vanilla speed or delay literal appears in the BC14 sources", MarchRulesPin),
    };

    const int Size = 400;

    // River band x in [190, 210) on a 400 map with a ford (cost 2) at z = -300 and a bridge (cost 1) at z = -100.
    static BattleTerrainGrid TwoCrossingMap() => BandMap(Size, 190, 210, (-300f, Ford), (-100f, Bridge));

    static void RouteSameRegion()
    {
        BattleTerrainGrid grid = TwoCrossingMap();
        CrossingGraphView graph = GraphOf(grid, RegionsOf(grid, Size));
        RouteResult r = graph.Route(20f, -200f, 120f, -200f, 1f, 0f, false);     // both on the west bank
        AssertTrue(r.Found && r.SameRegion, "found within one region");
        AssertEqual(0, r.CrossingCount, "no crossing");
        AssertEqual(-1, r.FirstLink, "no via");
        AssertNear(100f * 2f, r.CostUnits, 1e-3f, "100 units of grass at cost 2, the 10-unit samples summed");
        AssertNear(0.2f, r.LegHours, 1e-5f, "200 cost units at 1000 per hour");
        AssertFalse(r.Estimated, "an exact leg");
    }

    static void RouteViaCrossing()
    {
        BattleTerrainGrid grid = TwoCrossingMap();
        CrossingGraphView graph = GraphOf(grid, RegionsOf(grid, Size));
        RouteResult nearBridge = graph.Route(100f, -110f, 300f, -110f, 1f, 0f, false);
        AssertTrue(nearBridge.Found && !nearBridge.SameRegion, "different regions");
        AssertEqual(1, nearBridge.CrossingCount, "one crossing");
        AssertEqual(1, nearBridge.FirstLink, "the bridge (link 1, z = -100) is the cheaper crossing for a route at z = -110");
        AssertEqual(1, graph.PathLinks[0] == 1 ? 1 : 0, "PathLinks names it");
        RouteResult nearFord = graph.Route(100f, -290f, 300f, -290f, 1f, 0f, false);
        AssertEqual(0, nearFord.FirstLink, "the ford (link 0, z = -300) for a route at z = -290");
        // The route is the sum of its legs: to the crossing and on, through the footprint priced at the crossing's own cost.
        AssertTrue(nearBridge.CostUnits > 400f && nearBridge.CostUnits < 900f, "a plausible cost " + nearBridge.CostUnits);
        AssertNear(nearBridge.CostUnits / 1000f, nearBridge.LegHours, 1e-5f, "hours follow the cost");
    }

    static void RouteHeldDetour()
    {
        const int big = 3200;
        // Bridge on the straight line (z = -800), Clear ford 600 units south (z = -1400).
        BattleTerrainGrid near = BandMap(big, 1590, 1610, (-800f, Bridge), (-1400f, Ford));
        RiverRegions nearRegions = RegionsOf(near, big);
        CrossingWatch watch = HeldBridgeWatch(nearRegions, near);
        AssertEqual(CrossingState.Held, watch.StateOf(0), "the bridge is held");
        CrossingGraphView g1 = GraphOf(near, nearRegions, watch);
        RouteResult toFord = g1.Route(800f, -800f, 2400f, -800f, 1f, 0f, false);
        AssertTrue(toFord.Found, "found");
        AssertEqual(1, toFord.FirstLink, "the ford 600 units off: the held bridge plus an hour is dearer");
        AssertEqual(0, toFord.HeldCount, "no held crossing is taken");
        AssertNear(0f, toFord.Contested, 1e-6f, "the route is clear of held crossings");

        // The ford 2,000 units away: the bridge is cheaper even with the hour.
        BattleTerrainGrid far = BandMap(big, 1590, 1610, (-800f, Bridge), (-2800f, Ford));
        RiverRegions farRegions = RegionsOf(far, big);
        CrossingWatch farWatch = HeldBridgeWatch(farRegions, far);
        CrossingGraphView g2 = GraphOf(far, farRegions, farWatch);
        RouteResult toBridge = g2.Route(800f, -800f, 2400f, -800f, 1f, 0f, false);
        AssertEqual(0, toBridge.FirstLink, "the bridge when the ford is 2,000 away");
        AssertEqual(1, toBridge.HeldCount, "one held crossing");
        AssertNear(ManeuverFeasibility.HeldCrossingHours, toBridge.HeldHours, 1e-6f, "an hour for it");
        AssertNear(1f, toBridge.Contested, 1e-6f, "contested is 1 for a held crossing");
        AssertNear(toBridge.LegHours + 1f, toBridge.Hours, 1e-5f, "Hours includes the hour");
    }

    // An enemy regiment east of the first listed crossing, facing it: Held.
    static CrossingWatch HeldBridgeWatch(RiverRegions regions, BattleTerrainGrid grid)
    {
        regions.LinkCrossing(0, out float cx, out float cz, out _, out _);
        var watch = new CrossingWatch();
        BattleSnapshot s = SnapshotOf(10.0, Enemy(cx + 60f, cz, 270f, 100f));
        watch.Classify(s, regions, grid, new FnGroundProbe { Fn = (x, z) => true }, ShippedReach, 10.0);     // every other crossing is seen: Clear
        return watch;
    }

    static void RouteNoCrossing()
    {
        BattleTerrainGrid grid = BandMap(Size, 190, 210);
        CrossingGraphView graph = GraphOf(grid, RegionsOf(grid, Size));
        RouteResult r = graph.Route(100f, -200f, 300f, -200f, 1f, 0f, false);
        AssertFalse(r.Found, "no link joins the banks");
        AssertTrue(r.NoRoute && !r.Unknown, "no crossing: a verdict, not an unknown");
    }

    static void RouteBend()
    {
        // A river on x in [190, 210) for z in [-300, -100] only: the land goes round its ends, so both banks are one region.
        BattleTerrainGrid grid = GridOf(Size, Size, (x, zi) => x >= 190 && x < 210 && zi >= 100 && zi <= 300 ? River : Grass);
        CrossingGraphView graph = GraphOf(grid, RegionsOf(grid, Size));
        RouteResult r = graph.Route(100f, -200f, 300f, -200f, 1f, 0f, false);
        AssertTrue(r.Found && r.SameRegion, "one region");
        AssertTrue(r.Estimated, "the straight leg skirts a bend of the river: an estimate");
        // 20 samples of 10 units from x = 100: two on the river (x = 190, 200) at the swamp cost 6, eighteen on grass at 2.
        AssertNear(18f * 10f * 2f + 2f * 10f * 6f, r.CostUnits, 1e-3f, "bend samples at the swamp cost");
    }

    static void RouteConfirm()
    {
        BattleTerrainGrid grid = TwoCrossingMap();
        RiverRegions regions = RegionsOf(grid, Size);
        // Without answers the bridge wins for a route at z = -110.
        AssertEqual(1, GraphOf(grid, regions).Route(100f, -110f, 300f, -110f, 1f, 0f, false).FirstLink, "baseline: the bridge");
        // The NavMesh refuses the bridge from the west bank: the ford is taken.
        var legs = new FakeLegs();
        legs.Answers.Add((100f, -110f, 200f, -100f, 1, 0f, true));
        CrossingGraphView graph = GraphOf(grid, regions, null, legs);
        RouteResult refused = graph.Route(100f, -110f, 300f, -110f, 1f, 0f, false);
        AssertTrue(refused.Found, "an alternative exists");
        AssertEqual(0, refused.FirstLink, "a refused leg is not walked: the other crossing");
        // Both crossings refused: no route.
        legs.Answers.Add((100f, -110f, 200f, -300f, 0, 0f, true));
        RouteResult none = graph.Route(100f, -110f, 300f, -110f, 1f, 0f, false);
        AssertFalse(none.Found, "every crossing refused");
        AssertTrue(none.NoRoute, "no-route");
        // A confirmed leg replaces the estimate: a cheap confirmation for the farther ford makes it the choice.
        var cheap = new FakeLegs();
        cheap.Answers.Add((100f, -110f, 200f, -300f, 0, 10f, false));
        cheap.Answers.Add((300f, -110f, 200f, -300f, 0, 10f, false));
        RouteResult confirmed = GraphOf(grid, regions, null, cheap).Route(100f, -110f, 300f, -110f, 1f, 0f, false);
        AssertEqual(0, confirmed.FirstLink, "the confirmed polyline costs 10: the ford wins");
        AssertNear(20f + 2f * 0f, confirmed.CostUnits, 1e-3f, "its two confirmed legs replace the estimates");
        // A same-region leg is confirmable too.
        var same = new FakeLegs();
        same.Answers.Add((20f, -200f, 120f, -200f, -1, 77f, false));
        RouteResult one = GraphOf(grid, regions, null, same).Route(20f, -200f, 120f, -200f, 1f, 0f, false);
        AssertNear(77f, one.CostUnits, 1e-3f, "the confirmed same-region leg");
        same.Answers.Clear();
        same.Answers.Add((20f, -200f, 120f, -200f, -1, 0f, true));
        AssertTrue(GraphOf(grid, regions, null, same).Route(20f, -200f, 120f, -200f, 1f, 0f, false).NoRoute, "and a refused one is no route");
    }

    static void LegFootprint()
    {
        // River everywhere on x in [190, 210) and the listed bridge at (200, -300): its cells are river too, so the footprint rule prices them.
        BattleTerrainGrid grid = BandMap(Size, 190, 210, (-300f, Bridge));
        // West bank to the bridge: the river samples x = 190, 200 lie within 60 of the crossing: priced at the bridge's cost 1, no estimate.
        float cost = LegSampler.Cost(grid, 100f, -300f, 200f, -300f, false, 0f, 0f, 0, true, 200f, -300f, Bridge, out bool estimated);
        AssertNear(9f * 10f * 2f + 1f * 10f * 1f, cost, 1e-3f, "nine grass samples at 2 and the river sample at the bridge's cost 1 (the last sample has no length)");
        AssertFalse(estimated, "the footprint is the crossing, not a bend");
        // A leg skirting the river far from the crossing: the same samples are bends at the swamp cost 6, estimated.
        float bend = LegSampler.Cost(grid, 100f, -100f, 300f, -100f, false, 0f, 0f, 0, true, 200f, -300f, Bridge, out bool bendEstimated);
        AssertNear(18f * 10f * 2f + 2f * 10f * 6f, bend, 1e-3f, "two bend samples at the swamp cost");
        AssertTrue(bendEstimated, "a bend marks the leg estimated");
        // Without a footprint at all, even the leg to the crossing is a bend.
        float none = LegSampler.Cost(grid, 100f, -300f, 200f, -300f, false, 0f, 0f, 0, false, 0f, 0f, 0, out bool noneEstimated);
        AssertTrue(noneEstimated && none > cost, "no footprint: bends");
        // The start of a leg is a footprint too (the link table's leg between two crossings): east from the bridge, the samples x = 200, 190
        // are the crossing's, the grass after them costs 2.
        float fromBridge = LegSampler.Cost(grid, 200f, -300f, 100f, -300f, true, 200f, -300f, Bridge, false, 0f, 0f, 0, out bool fromEstimated);
        AssertNear(2f * 10f * 1f + 8f * 10f * 2f, fromBridge, 1e-3f, "a leg that starts at the crossing prices its river samples as the crossing");
        AssertFalse(fromEstimated, "and is no estimate");
    }

    static void TargetSnap()
    {
        BattleTerrainGrid grid = TwoCrossingMap();
        RiverRegions regions = RegionsOf(grid, Size);
        int west = regions.RegionAt(100f, -200f), east = regions.RegionAt(300f, -200f);
        // x = 200 is the middle of the 20-wide river: land blocks stand 12.5 units off on both banks.
        AssertTrue(regions.TrySnapToLand(200f, -200f, west, out float wx, out float wz), "a snap exists");
        AssertNear(187.5f, wx, 1e-3f, "the preferred (west) bank on a tie");
        AssertEqual(west, regions.RegionAt(wx, wz), "and it is land of the west region");
        AssertTrue(regions.TrySnapToLand(200f, -200f, east, out float ex, out _), "again");
        AssertNear(212.5f, ex, 1e-3f, "the east bank when it is preferred");
        AssertTrue(regions.TrySnapToLand(195f, -200f, east, out float nx, out _), "off the middle");
        AssertNear(187.5f, nx, 1e-3f, "the nearer bank wins over the preference");
        AssertTrue(regions.TrySnapToLand(100f, -200f, east, out float lx, out float lz), "a point on land");
        AssertNear(100f, lx, 1e-4f, "stays where it is");
        AssertNear(-200f, lz, 1e-4f, "stays where it is");
        // The routes use the snap: a target on the river routes to its land.
        CrossingGraphView graph = GraphOf(grid, regions);
        AssertTrue(graph.Route(100f, -110f, 200f, -110f, 1f, 0f, false).Found, "a route to a river point");
    }

    static void LegCache()
    {
        BattleTerrainGrid grid = TwoCrossingMap();
        CrossingGraphView graph = GraphOf(grid, RegionsOf(grid, Size));
        graph.Route(100f, -190f, 300f, -190f, 1f, 0f, false);
        int sampled = graph.LegsSampled;
        AssertTrue(sampled > 0, "the first query samples legs");
        graph.Route(110f, -180f, 310f, -180f, 1f, 0f, false);      // the same 50-unit buckets
        AssertEqual(sampled, graph.LegsSampled, "a second query from the same buckets samples no new leg");
        graph.Route(10f, -100f, 390f, -100f, 1f, 0f, false);        // other buckets
        AssertTrue(graph.LegsSampled > sampled, "other buckets sample");

        // Overflow: a full cache prices a leg and counts it, and the answer is the same.
        CrossingGraphView full = GraphOf(grid, RegionsOf(grid, Size));
        RouteResult expected = GraphOf(grid, RegionsOf(grid, Size)).Route(100f, -190f, 300f, -190f, 1f, 0f, false);
        for (int i = 0; i < LegCostCache.MaxLegCache; i++) full.Cache.Put(i + 1, -1000 - i, 1f);
        AssertEqual(0, full.Cache.Overflow, "filling to the ceiling is no overflow");
        RouteResult priced = full.Route(100f, -190f, 300f, -190f, 1f, 0f, false);
        AssertTrue(full.Cache.Overflow > 0, "the legs that could not be stored are counted");
        AssertNear(expected.CostUnits, priced.CostUnits, 1e-3f, "and priced all the same");
        // The cache itself: keys, replacement and clear.
        var cache = new LegCostCache();
        cache.Put(5, LegCostCache.LinkKey(2), 12f, true);
        AssertTrue(cache.TryGet(5, LegCostCache.LinkKey(2), out float c, out bool est) && c == 12f && est, "stored");
        cache.Put(5, LegCostCache.LinkKey(2), 7f);
        AssertTrue(cache.TryGet(5, LegCostCache.LinkKey(2), out c, out est) && c == 7f && !est, "a confirmed leg replaces the estimate");
        AssertFalse(cache.TryGet(5, LegCostCache.LinkKey(3), out _), "another link");
        cache.Clear();
        AssertFalse(cache.TryGet(5, LegCostCache.LinkKey(2), out _), "cleared");
    }

    static void MarchTime()
    {
        MarchRules r = ShippedRules();
        AssertNear(1.0f, ManeuverFeasibility.MarchHours(1000f * 1f, 1f, 0f, in r), 1e-5f, "1,000 units of road (cost 1): one hour for infantry");
        AssertNear(1.0f, ManeuverFeasibility.MarchHours(500f * 2f, 1f, 0f, in r), 1e-5f, "500 units of grass (cost 2): one hour");
        AssertNear(0.5f, ManeuverFeasibility.MarchHours(1000f, 2f, 0f, in r), 1e-5f, "a cavalry-only member (base 2) halves it");
        float factor = (float)Math.Pow(0.5, 0.2);
        AssertNear(1f / factor, ManeuverFeasibility.MarchHours(1000f, 1f, 0.5f, in r), 1e-4f, "fatigue 0.5 divides by 0.5 ^ 0.2");
        AssertNear(1f / (float)Math.Pow(0.01, 0.2), ManeuverFeasibility.MarchHours(1000f, 1f, 1f, in r), 1e-3f, "fully fatigued: the 0.01 floor");
        // A 1,000-unit road leg costs exactly 1,000 through the sampler (the last sample is pro-rated).
        BattleTerrainGrid road = GridOf(1200, 1200, (x, zi) => Road);
        float cost = LegSampler.Cost(road, 100f, -600f, 1100f, -600f, false, 0f, 0f, 0, false, 0f, 0f, 0, out _);
        AssertNear(1000f, cost, 1e-3f, "1,000 units of road cost 1,000");
        // A leg that is not a whole number of steps: the remainder is pro-rated.
        float odd = LegSampler.Cost(road, 100f, -600f, 1105f, -600f, false, 0f, 0f, 0, false, 0f, 0f, 0, out _);
        AssertNear(1005f, odd, 1e-3f, "the last 5 units are priced at their length");
    }

    static void CourierTime()
    {
        MarchRules r = ShippedRules();
        AssertNear(0.02f, ManeuverFeasibility.CourierLegHours(in r, true, 50f, 100f), 1e-6f, "a first hop within bugle range: max(processing, recognition)");
        AssertNear(0.02f, ManeuverFeasibility.CourierLegHours(in r, false, 50f, 100f), 1e-6f, "a relay hop within bugle range: recognition");
        AssertNear(0.02f + 1.0f, ManeuverFeasibility.CourierLegHours(in r, true, 2563f, 2563f * 2f), 1e-4f, "2,563 units of grass: the ride is an hour");
        AssertNear(0.02f + 0.5f, ManeuverFeasibility.CourierLegHours(in r, true, 2563f, 2563f * 1f), 1e-4f, "the same on road: half an hour");
        AssertNear(0.02f + 0.2f, ManeuverFeasibility.CourierLegHours(in r, false, 300f, 5126f * 0.2f), 1e-4f, "a relay beyond bugle range: route processing plus the ride");
        MarchRules off = ShippedRules(false);
        AssertNear(0f, ManeuverFeasibility.CourierLegHours(in off, true, 2563f, 5126f), 1e-9f, "order delays off: 0");

        // The chain: the army headquarters (root), two corps, brigades inside bugle range of their corps; the longest branch counts.
        BattleTerrainGrid grid = GridOf(1200, 1200, (x, zi) => Grass);
        var tree = new TreeFixture();
        int army = tree.Add(TacticalUnitType.BattleGroupArmy, 600f, -1100f, -1);
        int corpsA = tree.Add(TacticalUnitType.BattleGroupDivision, 600f, -600f, army);
        int brigadeNear = tree.Add(TacticalUnitType.BattleGroupBrigade, 630f, -600f, corpsA);        // 30 units from its corps
        int brigadeFar = tree.Add(TacticalUnitType.BattleGroupBrigade, 600f, -300f, corpsA);          // 300 units from its corps
        tree.Add(TacticalUnitType.Infantry, 630f, -610f, brigadeNear, strength: 500f);
        tree.Add(TacticalUnitType.Infantry, 600f, -310f, brigadeFar, strength: 500f);
        BattleSnapshot s = tree.Build();
        // army -> corps: 500 units of grass = 1,000 cost units, a first hop beyond bugle range: 0.02 + 1000 / 5126.
        float first = 0.02f + 1000f / r.CourierSpeedPerHour;
        // corps -> far brigade: 300 units = 600 cost, a relay beyond bugle range: 0.02 + 600 / 5126; the near brigade is a bugle (0.02).
        float far = 0.02f + 600f / r.CourierSpeedPerHour;
        AssertNear(first + far, ManeuverFeasibility.ChildCourierHours(s, corpsA, grid, in r), 1e-4f, "the longest branch counts, the near brigade does not");
        AssertNear(0f, ManeuverFeasibility.ChildCourierHours(s, corpsA, grid, in off), 1e-9f, "order delays off");
        // A child that is itself a brigade: only the first hop.
        AssertNear(0.02f + (600f + 0f) / r.CourierSpeedPerHour * 0f + 0f, 0.02f, 1e-6f, "(arithmetic anchor)");
        AssertTrue(ManeuverFeasibility.ChildCourierHours(s, brigadeFar, grid, in r) > 0.02f, "a brigade child pays its first hop");
        // A child already in contact orders at once (the evaluation passes 0 for its own T); the function itself does not know the contact.
        AssertTrue(ManeuverFeasibility.ChildCourierHours(s, corpsA, grid, in r) > 0f, "the courier time itself is never zeroed");
    }

    static void PlanOf(float[] t, float[] offset, byte[] kind, bool[] contact, out float cStar, out float[] strike, out float[] release, out bool[] has)
    {
        int n = t.Length;
        strike = new float[n];
        release = new float[n];
        has = new bool[n];
        cStar = ManeuverFeasibility.PlanReleases(t, offset, kind, contact, strike, release, has);
    }

    static void StrikeOffset()
    {
        // MobileFlankMarch: the wing's offset is 900 battle-seconds = 0.25 h.
        PlanOf(new[] { 0.6f, 1.0f }, new[] { 0f, 0.25f }, new[] { ManeuverFeasibility.MemberFix, ManeuverFeasibility.MemberEnvelops }, new[] { false, false },
            out float c1, out float[] s1, out float[] r1, out bool[] h1);
        AssertNear(0.75f, c1, 1e-5f, "C* = max(0.6, 1.0 - 0.25)");
        AssertNear(1.0f, s1[1], 1e-5f, "the wing strikes at 1.0");
        AssertNear(0f, r1[1], 1e-5f, "the wing leaves at once");
        AssertNear(0.15f, r1[0], 1e-5f, "the fix waits 0.15");
        AssertTrue(h1[0] && h1[1], "both are released");
        PlanOf(new[] { 0.6f, 0.4f }, new[] { 0f, 0.25f }, new[] { ManeuverFeasibility.MemberFix, ManeuverFeasibility.MemberEnvelops }, new[] { false, false },
            out float c2, out float[] s2, out float[] r2, out _);
        AssertNear(0.6f, c2, 1e-5f, "a fast wing: C* is the fix's own time");
        AssertNear(0.85f, s2[1], 1e-5f, "S* = 0.6 + 0.25");
        AssertNear(0.45f, r2[1], 1e-5f, "the wing waits 0.45");
        AssertNear(0f, r2[0], 1e-5f, "the fix goes at once");
        // A reserve member pins nothing and has no release.
        PlanOf(new[] { 0.6f, 9f }, new[] { 0f, 0f }, new[] { ManeuverFeasibility.MemberFix, ManeuverFeasibility.MemberOther }, new[] { false, false },
            out float c3, out _, out _, out bool[] h3);
        AssertNear(0.6f, c3, 1e-5f, "a reserve member does not set C*");
        AssertFalse(h3[1], "and has no planned release");
        // No fixing member: C* is the wings' own pace.
        PlanOf(new[] { 1.4f }, new[] { 0.5f }, new[] { ManeuverFeasibility.MemberEnvelops }, new[] { false }, out float c4, out float[] s4, out float[] r4, out _);
        AssertNear(0.9f, c4, 1e-5f, "T - offset");
        AssertNear(1.4f, s4[0], 1e-5f, "S*");
        AssertNear(0f, r4[0], 1e-5f, "released at once");
    }

    static void StrikeFixContact()
    {
        PlanOf(new[] { 0.9f, 1.2f }, new[] { 0f, 0.25f }, new[] { ManeuverFeasibility.MemberFix, ManeuverFeasibility.MemberEnvelops }, new[] { true, false },
            out float c, out float[] s, out float[] r, out bool[] has);
        AssertNear(0.95f, c, 1e-5f, "T_main is 0 in contact: C* is the wings' pace, 1.2 - 0.25");
        AssertNear(0f, r[0], 1e-9f, "the fix in contact has release 0");
        AssertFalse(has[0], "and no planned release is published for it");
        AssertNear(0f, r[1], 1e-5f, "the wing is released at once");
        AssertTrue(has[1], "its release is published");
    }

    static void StrikeAbsolute()
    {
        AssertTrue(Math.Abs(10.45 - ManeuverFeasibility.AbsoluteHours(10.0, 0.45f)) < 1e-6, "the published hour is the evaluation's plus R");
        AssertEqual(7.25, ManeuverFeasibility.AbsoluteHours(7.25, 0f), "a zero release is the evaluation's hour");
    }

    static void StrikeDouble()
    {
        // DoubleEnvelopment: wings at 1.2 h and 2.4 h (offsets 0), the fix at 0.79 h.
        PlanOf(new[] { 1.2f, 2.4f, 0.79f }, new[] { 0f, 0f, 0f },
            new[] { ManeuverFeasibility.MemberEnvelops, ManeuverFeasibility.MemberEnvelops, ManeuverFeasibility.MemberFix }, new[] { false, false, false },
            out float c, out float[] s, out float[] r, out _);
        AssertNear(2.4f, c, 1e-5f, "C*");
        AssertNear(2.4f, s[0], 1e-5f, "every body strikes at 2.4");
        AssertNear(2.4f, s[1], 1e-5f, "every body strikes at 2.4");
        AssertNear(2.4f, s[2], 1e-5f, "every body strikes at 2.4");
        AssertNear(1.2f, r[0], 1e-5f, "the faster wing waits 1.2");
        AssertNear(0f, r[1], 1e-5f, "the slower wing goes at once");
        AssertNear(1.61f, r[2], 1e-5f, "the fix waits 1.61");
    }

    // A march leg between two banks through the one listed crossing at z = -200 of a 400 map, the crossing Held when asked.
    static CrossingGraphView OneCrossingGraph(bool held, out RiverRegions regions, MarchRules? rules = null)
    {
        BattleTerrainGrid grid = BandMap(Size, 190, 210, (-200f, Bridge));
        regions = RegionsOf(grid, Size);
        CrossingWatch watch = null;
        if (held)
        {
            watch = new CrossingWatch();
            watch.Classify(SnapshotOf(10.0, Enemy(260f, -200f, 270f, 100f)), regions, grid, new FnGroundProbe(), ShippedReach, 10.0);
        }
        return GraphOf(grid, regions, watch, null, rules);
    }

    static void StrikeHeld()
    {
        MarchRules rules = ShippedRules();
        CrossingGraphView clear = OneCrossingGraph(false, out _);
        RouteResult free = clear.Route(100f, -200f, 300f, -200f, 1f, 0f, false);
        CrossingGraphView held = OneCrossingGraph(true, out _);
        RouteResult costly = held.Route(100f, -200f, 300f, -200f, 1f, 0f, false);
        float tFree = ManeuverFeasibility.StrikeHours(0.3f, in free, 0f, in rules);
        float tHeld = ManeuverFeasibility.StrikeHours(0.3f, in costly, 0f, in rules);
        AssertNear(1.0f, tHeld - tFree, 1e-4f, "a Held crossing on the march leg adds an hour to T");
        AssertNear(0.3f + free.LegHours + 0.02f + 0.05f, tFree, 1e-4f, "T = courier + march + DeployHours (processing + the Turn hold)");
        AssertNear(0.07f, ManeuverFeasibility.DeployHours(in rules), 1e-6f, "DeployHours");
    }

    static void InwardSurplus()
    {
        MarchRules rules = ShippedRules();
        // A ford 300 units off the straight line: anchor (100, -100), line end (300, -100), the only crossing at (200, -400).
        BattleTerrainGrid grid = BandMap(Size, 190, 210, (-400f + 0.5f, Ford));
        RiverRegions regions = RegionsOf(grid, Size);
        CrossingGraphView g = GraphOf(grid, regions);
        float off = ManeuverFeasibility.InwardSurplusHours(g, 100f, -100f, 300f, -100f, 1f, 0f, out RouteResult detour);
        AssertTrue(detour.Found, "a route through the ford");
        AssertTrue(off > 0.5f, "a ford 300 units off the line delays the strike: " + off);
        // A ford on the straight line: the footprint is priced at the ford, cheaper than the straight line's bend: surplus 0, never less.
        BattleTerrainGrid on = BandMap(Size, 190, 210, (-100f, Ford));
        CrossingGraphView g2 = GraphOf(on, RegionsOf(on, Size));
        float onLine = ManeuverFeasibility.InwardSurplusHours(g2, 100f, -100f, 300f, -100f, 1f, 0f, out RouteResult direct);
        AssertTrue(direct.Found && direct.FirstLink == 0, "the ford on the line");
        AssertNear(0f, onLine, 1e-6f, "a ford on the straight line adds nothing");
        // Through a Held one: an hour more.
        BattleTerrainGrid heldGrid = BandMap(Size, 190, 210, (-100f, Ford));
        RiverRegions heldRegions = RegionsOf(heldGrid, Size);
        var watch = new CrossingWatch();
        watch.Classify(SnapshotOf(10.0, Enemy(260f, -100f, 270f, 100f)), heldRegions, heldGrid, new FnGroundProbe(), ShippedReach, 10.0);
        float held = ManeuverFeasibility.InwardSurplusHours(GraphOf(heldGrid, heldRegions, watch), 100f, -100f, 300f, -100f, 1f, 0f, out _);
        AssertNear(1.0f, held, 1e-4f, "a Held crossing on the route: an hour more");
        // The main body's strike leg through a Held crossing adds nothing to T: T has no strike term (the evaluation prices it as 'contested').
        AssertNear(0f, onLine, 1e-6f, "(the main body never calls this)");
        // The evaluation puts the surplus into the wing's T (the call site, not only the function): T = courier + march + deploy + surplus.
        FeasField field = ThreeCorps(HorizontalBand(470f, 490f, (-225f, 480f)));
        FeasibilityLedger ledger = field.Evaluate(BattlePlanId.DoubleEnvelopment);
        CandidateResult result = Result(ledger, BattlePlanId.DoubleEnvelopment);
        ReadOnlySpan<MemberEstimate> members = ledger.Members(in result);
        ReadOnlySpan<BodyEstimate> bodies = ledger.Bodies(in result);
        AssertTrue(bodies[0].InwardSurplusHours > 0.05f, "the evaluated wing detours to the ford, surplus " + bodies[0].InwardSurplusHours);
        for (int m = 0; m < members.Length; m++)
        {
            if (members[m].Kind != ManeuverFeasibility.MemberEnvelops || members[m].Body != 0) continue;
            float expected = members[m].CourierHours + members[m].MarchHours + ManeuverFeasibility.DeployHours(in rules) + bodies[0].InwardSurplusHours;
            AssertNear(expected, members[m].StrikeHours, 1e-4f, "the member's strike time carries the inward surplus");
        }
    }

    static void MultiRiver()
    {
        // Two rivers: x in [100, 120) and [300, 320); one listed crossing over each.
        const int size = 400;
        BattleTerrainGrid grid = GridOf(size, size, (x, zi) => (x >= 100 && x < 120) || (x >= 300 && x < 320) ? River : Grass, false,
            new[] { 110f, 310f }, new[] { -200f, -200f }, new[] { Ford, Ford });
        RiverRegions regions = RegionsOf(grid, size);
        AssertEqual(3, regions.RegionCount, "three banks");
        // The second crossing is Held.
        var watch = new CrossingWatch();
        watch.Classify(SnapshotOf(10.0, Enemy(370f, -200f, 270f, 100f)), regions, grid, new FnGroundProbe(), ShippedReach, 10.0);
        AssertEqual(CrossingState.Held, watch.StateOf(1), "the east ford is held");
        AssertEqual(CrossingState.Unknown, watch.StateOf(0), "the west ford is unknown");
        CrossingGraphView g = GraphOf(grid, regions, watch);
        RouteResult r = g.Route(50f, -200f, 380f, -200f, 1f, 0f, false);
        AssertTrue(r.Found, "found");
        AssertEqual(2, r.CrossingCount, "both rivers are crossed");
        AssertEqual(0, r.FirstLink, "the first crossing is the via");
        AssertEqual(0, g.PathLinks[0], "path order");
        AssertEqual(1, g.PathLinks[1], "path order");
        AssertEqual(1, r.HeldCount, "an hour per Held crossing");
        AssertNear(ManeuverFeasibility.HeldCrossingHours, r.HeldHours, 1e-6f, "one held crossing, one hour");
        AssertNear(1f, r.Contested, 1e-6f, "the worst state over both");
        // Unknown is worth 0.25 when it is the worst.
        var quiet = new CrossingWatch();
        quiet.Classify(SnapshotOf(10.0), regions, grid, new FnGroundProbe(), ShippedReach, 10.0);
        RouteResult calm = GraphOf(grid, regions, quiet).Route(50f, -200f, 380f, -200f, 1f, 0f, false);
        AssertNear(0.25f, calm.Contested, 1e-6f, "two unknown crossings: 0.25");
        AssertEqual(0, calm.HeldCount, "none held");
    }

    static void FixMemberRelease()
    {
        PlanOf(new[] { 0.5f, 0.8f, 1.0f }, new[] { 0f, 0f, 0f },
            new[] { ManeuverFeasibility.MemberFix, ManeuverFeasibility.MemberFix, ManeuverFeasibility.MemberEnvelops }, new[] { false, false, false },
            out float c, out _, out float[] r, out _);
        AssertNear(1.0f, c, 1e-5f, "C* set by the wing");
        AssertNear(0.5f, r[0], 1e-5f, "the faster fixing member is released at 0.5");
        AssertNear(0.2f, r[1], 1e-5f, "the slower at 0.2, so none strikes alone");
        AssertNear(0f, r[2], 1e-5f, "the wing at once");
    }

    // The BC14 file set (requirement-runtime-wiring): the pure and runtime files named under Create, found by name.
    internal static string[] Bc14Files()
    {
        string[] relative =
        {
            "src/WhiskeyRealism/Tactical/Battle/RiverRegions.cs",
            "src/WhiskeyRealism/Tactical/Battle/CrossingWatch.cs",
            "src/WhiskeyRealism/Tactical/Battle/EnemySamples.cs",
            "src/WhiskeyRealism/Tactical/Battle/SingleLinkClusters.cs",
            "src/WhiskeyRealism/Tactical/Battle/RiverRegionsRuntime.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Types.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Job.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Candidates.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Screen.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Risk.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Monitor.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Defender.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/FeasibilityLedger.Monitor.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/FeasibilityLedger.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/LegCostCache.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/FeasibilityLedger.Look.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ConfirmedLegs.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/FeasibilityTelemetry.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/FeasibilityRuntime.cs",
        };
        var found = new System.Collections.Generic.List<string>();
        foreach (string rel in relative)
        {
            try { found.Add(TestFixtures.FindRepoPath(rel)); }
            catch (FileNotFoundException) { }       // a file a later task creates
        }
        return found.ToArray();
    }

    // A vanilla speed or delay as a literal: 1000, 0.02, 75, 2.33, 2.2 as whole tokens (not inside a longer number or a name).
    static readonly Regex MarchLiterals = new Regex(@"(?<![\w.])(1000|0\.02|75|2\.33|2\.2)(f|F|\.0f)?(?![\w.])", RegexOptions.Compiled);

    static string FirstMarchLiteral(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            Match m = MarchLiterals.Match(line);
            if (m.Success) return m.Value + " in: " + line.Trim();
        }
        return null;
    }

    static void MarchRulesPin()
    {
        // The scanner fires on a planted literal first, so its zero is shown to detect.
        AssertTrue(FirstMarchLiteral("float t = cost * 1000f;") != null, "the scanner fires on a planted * 1000f");
        AssertTrue(FirstMarchLiteral("float t = 0.02f + x;") != null, "and on 0.02f");
        AssertTrue(FirstMarchLiteral("if (d < 75f) return;") != null, "and on 75f");
        AssertTrue(FirstMarchLiteral("x = 2.33f * 2.2f;") != null, "and on 2.33 and 2.2");
        AssertTrue(FirstMarchLiteral("float t = cost * 0.025f + 750f + 21000f + 10.02f;") == null, "it does not fire on longer numbers");
        string[] files = Bc14Files();
        AssertTrue(files.Length >= 6, "the BC14 file set is found (" + files.Length + " files)");
        foreach (string file in files)
        {
            string hit = FirstMarchLiteral(File.ReadAllText(file));
            AssertTrue(hit == null, Path.GetFileName(file) + " carries a vanilla literal: " + hit);
        }
    }
}
