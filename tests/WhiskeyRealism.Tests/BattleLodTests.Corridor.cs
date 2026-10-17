using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;
using static TestFixtures;

// BC15 decision 2(e): TacticalFriendlySpatialGrid.CorridorSignature, the runtime's IFriendlyCorridor (BattleLodRuntime hands the pass the posture
// scratch's friendly grid): a count and an order-free hash of the standing own units inside a group's path corridor.
internal static partial class BattleLodTests
{
    private static (string, Action)[] CorridorTests()
    {
        return new (string, Action)[]
        {
            ("bc15 CORRIDOR counts the units inside the corridor and nothing else", CorridorCounts),
            ("bc15 CORRIDOR the group itself, its first 30 m and what lies beyond the end are not traffic", CorridorEdges),
            ("bc15 CORRIDOR the hash does not depend on the order the grid was filled in, and tells two sets of equal size apart", CorridorHashIsOrderFree),
            ("bc15 CORRIDOR a corridor longer than 2,500 m is cut there, an empty or degenerate one is zero", CorridorBounds),
            ("bc15 CORRIDOR allocates nothing after warm-up", CorridorAllocatesNothingAfterWarmup),
        };
    }

    private static TacticalFriendlySpatialGrid GridWith(params (int id, float x, float z)[] units)
    {
        var grid = new TacticalFriendlySpatialGrid();
        foreach (var u in units) grid.Add(u.id, u.x, u.z);
        return grid;
    }

    private static void CorridorCounts()
    {
        // The corridor runs north from (1000, 1000) for 800 m, 250 m wide (125 m each side).
        var grid = GridWith((1, 1000f, 1400f), (2, 1100f, 1700f), (3, 1000f + 130f, 1400f), (4, 700f, 1400f), (5, 1000f, 2100f), (6, 1000f, 600f));
        int hash = grid.CorridorSignature(99, 1000f, 1000f, 1000f, 1800f, BattleLod.CorridorWidthMeters, out int count);
        AssertEqual(2, count, "units 1 and 2 are inside; 3 is 130 m off, 4 is 300 m off, 5 is past the end, 6 is behind");
        AssertTrue(hash != 0, "a non-empty corridor has a hash");
        grid.Add(7, 1020f, 1000f + 500f);
        grid.CorridorSignature(99, 1000f, 1000f, 1000f, 1800f, BattleLod.CorridorWidthMeters, out int more);
        AssertEqual(3, more, "a unit that walks in is counted");
    }

    private static void CorridorEdges()
    {
        var grid = GridWith((10, 1000f, 1000f), (11, 1000f, 1020f), (12, 1000f, 1040f), (13, 1000f, 1799f), (14, 1000f, 1801f));
        grid.CorridorSignature(10, 1000f, 1000f, 1000f, 1800f, BattleLod.CorridorWidthMeters, out int count);
        AssertEqual(2, count, "self (10) is skipped; 11 stands 20 m ahead (inside CorridorMinForward); 12 and 13 count; 14 is 1 m past the end");
        AssertNear(30f, TacticalFriendlySpatialGrid.CorridorMinForward, 0f, "the runtime's own minimum forward distance");
    }

    private static void CorridorHashIsOrderFree()
    {
        var units = new[] { (1, 1000f, 1400f), (2, 1050f, 1500f), (3, 950f, 1600f), (4, 1000f, 1700f) };
        var forward = GridWith(units);
        var reversed = new TacticalFriendlySpatialGrid();
        for (int u = units.Length - 1; u >= 0; u--) reversed.Add(units[u].Item1, units[u].Item2, units[u].Item3);
        int a = forward.CorridorSignature(99, 1000f, 1000f, 1000f, 1800f, 250f, out int ca);
        int b = reversed.CorridorSignature(99, 1000f, 1000f, 1000f, 1800f, 250f, out int cb);
        AssertEqual(ca, cb, "the same count");
        AssertEqual(a, b, "the same hash, whatever the fill order");

        var other = GridWith((1, 1000f, 1400f), (2, 1050f, 1500f), (3, 950f, 1600f), (5, 1000f, 1700f));
        int c = other.CorridorSignature(99, 1000f, 1000f, 1000f, 1800f, 250f, out int cc);
        AssertEqual(ca, cc, "one unit replaced by another: the same count");
        AssertTrue(a != c, "and a different hash");
    }

    private static void CorridorBounds()
    {
        var grid = GridWith((1, 1000f, 1000f + 2400f), (2, 1000f, 1000f + 2600f), (3, 1000f, 1000f + 4000f));
        grid.CorridorSignature(99, 1000f, 1000f, 1000f, 1000f + 5000f, 250f, out int count);
        AssertEqual(1, count, "a 5,000 m corridor is cut at 2,500 m: only the unit at 2,400 m counts");
        AssertNear(2500f, TacticalFriendlySpatialGrid.MaxCorridorMeters, 0f, "the longest move the posture pipeline orders (PostureSense.MaxWaypointMeters)");
        AssertEqual(0, grid.CorridorSignature(99, 1000f, 1000f, 1000f, 1000.5f, 250f, out int tiny), "a degenerate corridor hashes to zero");
        AssertEqual(0, tiny, "and counts nothing");
        AssertEqual(0, new TacticalFriendlySpatialGrid().CorridorSignature(99, 0f, 0f, 0f, 500f, 250f, out int none), "an empty grid hashes to zero");
        AssertEqual(0, none, "and counts nothing");
    }

    private static void CorridorAllocatesNothingAfterWarmup()
    {
        var grid = new TacticalFriendlySpatialGrid();
        for (int u = 0; u < 400; u++) grid.Add(u + 1, (u % 20) * 90f, (u / 20) * 90f);
        for (int warm = 0; warm < 3; warm++) grid.CorridorSignature(999, 100f, 100f, 1500f, 1500f, 250f, out _);
        long before = BeginAllocationWindow();
        for (int k = 0; k < 200; k++) grid.CorridorSignature(999, 100f, 100f, 1500f, 1500f, 250f, out _);
        AssertEqual(0L, AllocatedSince(before), "bytes allocated by 200 corridor queries over 400 units");
    }
}
