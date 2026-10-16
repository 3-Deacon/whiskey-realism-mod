using System;
using System.IO;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Util;
using static TestHarness;
using static BattleMovementHonestyTests;

// BC14 task 4: the river regions and the per-battle link table (spec § 13.1 S1, LINK-TABLE). Frame: BattleTerrainGrid's, x in
// [0, size] and z in [-size, 0]; a grid index iz is world z = iz - size.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] RegionTests() => new (string, Action)[]
    {
        ("bc14 REG-SPLIT a river across the map gives two regions; a bridge and a ford each link them", RegSplit),
        ("bc14 REG-CREEK a creek does not split", RegCreek),
        ("bc14 REG-FROZEN a frozen map is one region with no links", RegFrozen),
        ("bc14 REG-LINK-RADIUS a crossing 50 units from land on both banks links; one 70 from a bank links only the near region", RegLinkRadius),
        ("bc14 REG-STEP the build honours the deadline across rows and is not ready until complete", RegStep),
        ("bc14 REG-TRUNC the 65th region and the 257th link are dropped with the OnceLog", RegTrunc),
        ("bc14 LINK-TABLE the link table holds every link pair of a region at the leg-cost sum, built across steps", LinkTable),
    };

    // A square map with a vertical river band on x in [riverFrom, riverTo), a ford on grid rows [95, 105) and a bridge on rows
    // [295, 305) (the crossing ids written on the river cells there), and the two listed crossings at their centres.
    static BattleTerrainGrid BandGrid(int size, int riverFrom, int riverTo, int bandId = River, bool frozen = false, bool withCrossings = true)
    {
        Func<int, int, int> idAt = (x, zi) =>
        {
            if (x < riverFrom || x >= riverTo) return Grass;
            if (!withCrossings) return bandId;
            if (zi >= 95 && zi < 105) return Ford;
            if (zi >= 295 && zi < 305) return Bridge;
            return bandId;
        };
        float mid = (riverFrom + riverTo) * 0.5f;
        return withCrossings
            ? BattleMovementHonestyTests.GridOf(size, size, idAt, frozen, new[] { mid, mid }, new[] { 100f - size, 300f - size }, new[] { Ford, Bridge })
            : BattleMovementHonestyTests.GridOf(size, size, idAt, frozen);
    }

    static TerrainPicture ReadyPicture(BattleTerrainGrid grid, int size) => TerrainPictureTests.Build(grid, size, TerrainPictureTests.Flat());

    // Steps to Ready with no deadline.
    static RiverRegions BuildRegions(BattleTerrainGrid grid, int size)
    {
        var regions = new RiverRegions();
        TerrainPicture picture = ReadyPicture(grid, size);
        AssertTrue(regions.Step(picture, grid, long.MaxValue, () => 0L), "the build completes");
        AssertTrue(regions.Ready, "ready");
        return regions;
    }

    static void RegSplit()
    {
        const int size = 400;
        BattleTerrainGrid grid = BandGrid(size, 190, 210);
        RiverRegions r = BuildRegions(grid, size);
        AssertEqual(2, r.RegionCount, "a river across the map gives two regions");
        AssertFalse(r.Frozen, "not frozen");
        int west = r.RegionAt(100f, -200f), east = r.RegionAt(300f, -200f);
        AssertTrue(west >= 0 && east >= 0 && west != east, "the banks are two regions (" + west + ", " + east + ")");
        AssertEqual(-1, r.RegionAt(200f, -200f), "open river reads -1");
        AssertEqual(-1, r.RegionAt(200f, -300f), "a bridge deck is not land either (the crossing links, it does not join)");
        AssertEqual(-1, r.RegionAt(-5f, -200f), "outside the map reads -1");
        AssertEqual(2, r.LinkCount, "the ford and the bridge each link the banks");
        Span<int> regions = stackalloc int[4];
        for (int link = 0; link < 2; link++)
        {
            AssertEqual(2, r.RegionsOfLink(link, regions), "link " + link + " joins two regions");
            AssertTrue(regions[0] != regions[1], "link " + link + " joins distinct regions");
            AssertTrue(r.IsRouted(link), "link " + link + " is routed");
        }
        r.LinkCrossing(0, out float x0, out float z0, out int grid0, out int id0);
        r.LinkCrossing(1, out float x1, out float z1, out int grid1, out int id1);
        AssertEqual(Ford, id0, "the first listed crossing is the ford");
        AssertEqual(Bridge, id1, "the second is the bridge");
        AssertEqual(0, grid0, "first crossing's grid index");
        AssertEqual(1, grid1, "second crossing's grid index");
        AssertNear(-300f, z0, 1e-3f, "the ford's z");
        AssertNear(-100f, z1, 1e-3f, "the bridge's z");
        AssertNear(200f, x0, 1e-3f, "the ford's x");
        AssertEqual(2, grid.CrossingCount, "the grid lists both");
    }

    static void RegCreek()
    {
        const int size = 400;
        BattleTerrainGrid grid = BandGrid(size, 190, 210, bandId: Creek, withCrossings: false);
        RiverRegions r = BuildRegions(grid, size);
        AssertEqual(1, r.RegionCount, "a creek does not split");
        AssertEqual(r.RegionAt(100f, -200f), r.RegionAt(300f, -200f), "both banks share the region");
        AssertTrue(r.RegionAt(200f, -200f) >= 0, "the creek itself is land");
        AssertEqual(0, r.LinkCount, "no crossings, no links");
    }

    static void RegFrozen()
    {
        const int size = 400;
        BattleTerrainGrid grid = BandGrid(size, 190, 210, frozen: true);
        var regions = new RiverRegions();
        AssertTrue(regions.Step(ReadyPicture(grid, size), grid, long.MaxValue, () => 0L), "a frozen map is ready at the first step");
        AssertTrue(regions.Frozen, "frozen");
        AssertEqual(1, regions.RegionCount, "one region");
        AssertEqual(0, regions.LinkCount, "no crossing logic applies");
        AssertEqual(0, regions.RegionAt(100f, -200f), "the west bank");
        AssertEqual(0, regions.RegionAt(300f, -200f), "the east bank is the same region");
        AssertEqual(0, regions.RegionAt(200f, -200f), "the frozen river is crossed");
        AssertEqual(-1, regions.RegionAt(500f, -200f), "outside the map is still -1");
    }

    static void RegLinkRadius()
    {
        const int size = 400;
        // River on x in [150, 250), one crossing at its middle (200): 52.5 from the nearest land block centre on each bank.
        BattleTerrainGrid near = BattleMovementHonestyTests.GridOf(size, size, (x, zi) => x >= 150 && x < 250 ? Ford : Grass, false,
            new[] { 200f }, new[] { -200f }, new[] { Ford });
        RiverRegions a = BuildRegions(near, size);
        AssertEqual(2, a.RegionCount, "two banks");
        AssertEqual(1, a.LinkCount, "a crossing about 50 units from land on both banks links them");

        // River on x in [150, 270): the east bank's nearest land block centre is 72.5 from the crossing at 200.
        BattleTerrainGrid far = BattleMovementHonestyTests.GridOf(size, size, (x, zi) => x >= 150 && x < 270 ? Ford : Grass, false,
            new[] { 200f }, new[] { -200f }, new[] { Ford });
        RiverRegions b = BuildRegions(far, size);
        AssertEqual(2, b.RegionCount, "still two banks");
        AssertEqual(0, b.LinkCount, "a crossing 70 units from one bank reaches only the near region: no link");
        AssertEqual(1, b.CrossingTotal, "it is recorded as a crossing");
    }

    static void RegStep()
    {
        const int size = 400;
        BattleTerrainGrid grid = BandGrid(size, 190, 210);
        var regions = new RiverRegions();
        var unready = new TerrainPicture(grid, size, size);
        AssertFalse(regions.Step(unready, grid, long.MaxValue, () => 0L), "a picture that is not ready: nothing is done");
        AssertEqual(0, regions.TotalUnits, "no unit of work while the picture is not ready");
        AssertFalse(regions.Ready, "not ready");

        TerrainPicture picture = ReadyPicture(grid, size);
        long t = 0;
        Func<long> clock = () => ++t;
        int steps = 0;
        while (true)
        {
            steps++;
            bool ready = regions.Step(picture, grid, t + 3, clock);
            if (!ready)
            {
                AssertEqual(3, regions.UnitsLastStep, "step " + steps + " stops after the first unit that ends past the deadline: three units");
                AssertEqual(-1, regions.RegionAt(100f, -200f), "the regions answer 'unknown' until the whole build is complete");
            }
            else break;
            AssertTrue(steps < 2000, "the build terminates");
        }
        AssertTrue(regions.Ready, "ready after the last step");
        AssertTrue(steps >= 10, "the build spans many steps: " + steps);
        AssertTrue(regions.RegionAt(100f, -200f) >= 0, "answers once ready");
        // Progress is guaranteed: an expired deadline does exactly one unit.
        var slow = new RiverRegions();
        long t2 = 1000;
        slow.Step(picture, grid, 0L, () => ++t2);
        AssertEqual(1, slow.UnitsLastStep, "an expired deadline stops after exactly one unit");

        // The runtime half steps the regions behind the picture's own step (the runtime is not compiled into the harness).
        string picRuntime = File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/TerrainPictureRuntime.cs"));
        int call = picRuntime.IndexOf("RiverRegionsRuntime.Step(", StringComparison.Ordinal);
        int ret = picRuntime.IndexOf("return;", call < 0 ? 0 : call, StringComparison.Ordinal);
        int begin = picRuntime.IndexOf("internal static void Step()", StringComparison.Ordinal);
        AssertTrue(begin >= 0 && call > begin, "TerrainPictureRuntime.Step calls RiverRegionsRuntime.Step");
        AssertTrue(ret > call, "the Ready branch calls it before its return");
        AssertTrue(picRuntime.IndexOf("_builder.Picture.Ready", begin, StringComparison.Ordinal) < call, "the call sits in the Ready branch");
    }

    static void RegTrunc()
    {
        OnceLog.ResetTactical();
        // 100 islands: land blocks of 50 units separated by 10-unit river lines on a 600 map.
        const int size = 600;
        Func<int, int, int> islands = (x, zi) => (x % 60 >= 50 || zi % 60 >= 50) ? River : Grass;
        BattleTerrainGrid grid = BattleMovementHonestyTests.GridOf(size, size, islands);
        RiverRegions r = BuildRegions(grid, size);
        AssertEqual(RiverRegions.MaxRegions, r.RegionCount, "the first 64 regions are kept");
        AssertTrue(r.RegionsTruncated, "the truncation is recorded");
        AssertTrue(OnceLog.AlreadyLogged("tactical-battle-core:river-regions-truncated"), "and logged once");
        AssertEqual(-1, r.RegionAt(595f - 10f, -10f), "a dropped region reads -1 like river (the last island in lattice order)");
        AssertTrue(r.RegionAt(10f, -size + 10f) >= 0, "the first island in lattice order is kept");

        // 260 listed crossings on a two-bank map: the 257th link is dropped.
        OnceLog.ResetTactical();
        const int s2 = 400;
        var cx = new float[260];
        var cz = new float[260];
        var cid = new int[260];
        for (int i = 0; i < 260; i++) { cx[i] = 200f; cz[i] = -380f + i * 1.4f; cid[i] = Ford; }
        BattleTerrainGrid many = BattleMovementHonestyTests.GridOf(s2, s2, (x, zi) => x >= 190 && x < 210 ? Ford : Grass, false, cx, cz, cid);
        RiverRegions m = BuildRegions(many, s2);
        AssertEqual(RiverRegions.MaxCrossings, m.LinkCount, "256 links are kept");
        AssertTrue(m.LinksTruncated, "the 257th link is dropped");
        AssertEqual(260, m.CrossingTotal, "every crossing was looked at");
        AssertTrue(OnceLog.AlreadyLogged("tactical-battle-core:river-regions-truncated"), "logged");
    }

    static void LinkTable()
    {
        OnceLog.ResetTactical();
        const int size = 400;
        BattleTerrainGrid grid = BandGrid(size, 190, 210);
        var regions = new RiverRegions();
        TerrainPicture picture = ReadyPicture(grid, size);
        long t = 0;
        Func<long> clock = () => ++t;
        int guard = 0;
        // One unit per call: the table is the last stage, so the map stays unknown until its single pair is done.
        while (!regions.Step(picture, grid, t + 1, clock))
        {
            AssertEqual(-1, regions.RegionAt(100f, -200f), "the map is unknown until the link table is complete");
            AssertTrue(++guard < 5000, "terminates");
        }
        AssertEqual(2, regions.LinkCount, "two links");
        AssertEqual(2, regions.RoutedCount, "both routed");
        AssertNear(0f, regions.LinkLegCost(0, 0), 1e-6f, "a link and itself");
        float ab = regions.LinkLegCost(0, 1), ba = regions.LinkLegCost(1, 0);
        AssertNear(ab, ba, 1e-6f, "symmetric");
        // The ford (cost 2) at z = -300 and the bridge (cost 1) at z = -100, 200 units apart along the river: the 10-unit samples within
        // 60 of an end are that crossing's footprint (7 x 10 x 2 at the ford, 6 x 10 x 1 at the bridge; the last sample has no length
        // left), the 7 between are a bend at the swamp cost 6 (420): 140 + 420 + 60.
        AssertNear(620f, ab, 0.01f, "the leg-cost sum of the pair");
        AssertTrue(regions.LinkLegEstimated(0, 1), "a leg over a bend of the river is an estimate");

        // Two river bands (x in [100,120) and [300,320)) give three regions A|B|C; crossing 0 links A-B, crossing 1 links B-C,
        // crossing 2 links A-B again: links 0 and 2 share regions, link 1 shares B with both.
        BattleTerrainGrid two = BattleMovementHonestyTests.GridOf(size, size,
            (x, zi) => (x >= 100 && x < 120) || (x >= 300 && x < 320) ? River : Grass, false,
            new[] { 110f, 310f, 110f }, new[] { -100f, -100f, -300f }, new[] { Ford, Ford, Ford });
        RiverRegions r2 = BuildRegions(two, size);
        AssertEqual(3, r2.RegionCount, "three regions");
        AssertEqual(3, r2.LinkCount, "three links");
        AssertFalse(float.IsNaN(r2.LinkLegCost(0, 2)), "two links over the same pair of banks share a region");
        AssertFalse(float.IsNaN(r2.LinkLegCost(0, 1)), "links that share the middle region have a leg");
        // A link pair that shares no region has no leg: a river across the middle with one vertical river above it and another below,
        // four banks, and one crossing over each vertical river (north: N-left | N-right, south: S-left | S-right).
        BattleTerrainGrid four = BattleMovementHonestyTests.GridOf(size, size,
            (x, zi) => (zi >= 195 && zi < 205) || (zi < 195 && x >= 100 && x < 120) || (zi >= 205 && x >= 300 && x < 320) ? River : Grass, false,
            new[] { 110f, 310f }, new[] { -300f, -100f }, new[] { Ford, Ford });
        RiverRegions r4 = BuildRegions(four, size);
        AssertEqual(4, r4.RegionCount, "four banks");
        AssertEqual(2, r4.LinkCount, "and two links");
        AssertTrue(float.IsNaN(r4.LinkLegCost(0, 1)), "links that share no region have no leg");
        AssertFalse(r4.LinksShareRegion(0, 1), "and say so");

        // More than MaxRoutedLinks links: the first 64 are routed, the rest kept as crossings and not routed.
        OnceLog.ResetTactical();
        const int s2 = 400;
        var cx = new float[70];
        var cz = new float[70];
        var cid = new int[70];
        for (int i = 0; i < 70; i++) { cx[i] = 200f; cz[i] = -380f + i * 5f; cid[i] = Ford; }
        BattleTerrainGrid many = BattleMovementHonestyTests.GridOf(s2, s2, (x, zi) => x >= 190 && x < 210 ? Ford : Grass, false, cx, cz, cid);
        RiverRegions m = BuildRegions(many, s2);
        AssertEqual(70, m.LinkCount, "70 links");
        AssertEqual(RiverRegions.MaxRoutedLinks, m.RoutedCount, "64 are routed");
        AssertTrue(m.IsRouted(63) && !m.IsRouted(64), "the 65th is kept as a crossing and not routed");
        AssertTrue(float.IsNaN(m.LinkLegCost(0, 64)), "no leg to an unrouted link");
        AssertFalse(float.IsNaN(m.LinkLegCost(0, 63)), "a leg between routed links");
        AssertTrue(OnceLog.AlreadyLogged("tactical-battle-core:river-regions-truncated"), "the truncation is logged");
    }
}
