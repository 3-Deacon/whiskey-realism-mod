using System;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC10 decision 11: the avenue flags read the terrain grid. Before, every entry point with a
// trooptargetposition was road-anchored, and "crossing-anchored" meant a river or fence within
// 3 m of three points (BattlefieldSetup.SearchTerrainInRangePos).
internal static partial class BattleMovementHonestyTests
{
    // World z = index - sizeZ (battle z is negative): a road band on world z -205..-195.
    private static BattleTerrainGrid RoadBandGrid(Func<int, int, int> extra = null)
    {
        return GridOf(1000, 1000, (x, z) => z >= 795 && z <= 805 ? Road : (extra != null ? extra(x, z) : Grass));
    }

    private static void RoadAnchoredNeedsRoadSamples()
    {
        BattleTerrainGrid g = RoadBandGrid();
        AssertTrue(AvenueAnchors.RoadAnchored(g, 100f, -200f, 600f, -200f), "an avenue that runs along a road");
        AssertFalse(AvenueAnchors.RoadAnchored(g, 100f, -500f, 600f, -500f), "an open-ground avenue is not road-anchored whatever its entry point's target");

        // The share counts road (7) and bridge (6) samples every 10 m: 39% is not enough, 59% is.
        BattleTerrainGrid partial = GridOf(1000, 1000, (x, z) => z >= 795 && z <= 805 && x >= 200 && x < 400 ? Road : Grass);
        AssertFalse(AvenueAnchors.RoadAnchored(partial, 100f, -200f, 600f, -200f), "20 of 51 samples is under half");
        partial = GridOf(1000, 1000, (x, z) => z >= 795 && z <= 805 && x >= 200 && x < 500 ? Bridge : Grass);
        AssertTrue(AvenueAnchors.RoadAnchored(partial, 100f, -200f, 600f, -200f), "30 of 51 bridge samples is over half (a bridge is road); the entry point (100 m short of it) adds nothing");

        // An entry point within 20 m of a road cell is road-anchored on its own.
        AssertTrue(AvenueAnchors.RoadAnchored(g, 100f, -215f, 100f, -500f), "the entry point is 10 m from the road band");
        AssertFalse(AvenueAnchors.RoadAnchored(g, 100f, -240f, 100f, -500f), "35 m is too far");
        AssertFalse(AvenueAnchors.RoadAnchored(null, 100f, -200f, 600f, -200f), "no grid: nothing is anchored");
    }

    private static void CrossingAnchoredNeedsFordOrBridge()
    {
        // A river along x = 500 with a ford on world z -305..-295.
        BattleTerrainGrid g = GridOf(1000, 1000, (x, z) => x == 500 ? (z >= 695 && z <= 705 ? Ford : River) : Grass);
        AssertTrue(AvenueAnchors.CrossingAnchored(g, 400f, -300f, 600f, -300f), "the avenue crosses the ford");
        AssertFalse(AvenueAnchors.CrossingAnchored(g, 400f, -600f, 600f, -600f), "a plain river is no crossing (it was one before: a river within 3 m)");

        BattleTerrainGrid bridge = GridOf(1000, 1000, (x, z) => x == 500 ? (z >= 695 && z <= 705 ? Bridge : River) : Grass);
        AssertTrue(AvenueAnchors.CrossingAnchored(bridge, 400f, -300f, 600f, -300f), "a bridge is a crossing");

        // Within 50 m of a vanilla rivercrossingpositions point.
        BattleTerrainGrid listed = GridOf(1000, 1000, (x, z) => Grass, crossingX: new[] { 500f }, crossingZ: new[] { -900f }, crossingId: new[] { Ford });
        AssertTrue(AvenueAnchors.CrossingAnchored(listed, 400f, -860f, 600f, -860f), "the avenue passes 40 m from a listed crossing");
        AssertFalse(AvenueAnchors.CrossingAnchored(listed, 400f, -800f, 600f, -800f), "100 m away does not");
        AssertTrue(AvenueAnchors.CrossingAnchored(listed, 500f, -850f, 500f, -850f), "a point avenue 50 m from the crossing counts (inclusive)");
        AssertFalse(AvenueAnchors.CrossingAnchored(null, 400f, -300f, 600f, -300f), "no grid: nothing is anchored");
    }

    // The adapter feeds the flags from the grid and no longer asks vanilla for river or fence terrain.
    private static void AvenueFlagsComeFromTheGrid()
    {
        string adapter = Code("src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs");
        AssertTrue(adapter.Contains("AvenueAnchors.RoadAnchored(") && adapter.Contains("AvenueAnchors.CrossingAnchored("), "the adapter reads the anchors");
        AssertFalse(adapter.Contains("SearchTerrainInRangePos"), "the 3 m river probe is gone");
        AssertFalse(System.Text.RegularExpressions.Regex.IsMatch(adapter, @"roadAnchored\s*=\s*true"), "a target position no longer makes an avenue road-anchored");
        AssertTrue(adapter.Contains("BattleTerrainGrid.Current()"), "the grid comes through the provider seam");
        AssertTrue(Code("src/WhiskeyRealism/Plugin.cs").Contains("BattleTerrainGrid.Provider = Tactical.Battle.BattleTerrainGridRuntime.Get;"),
            "the plugin binds the provider to the runtime's lazy grid");
    }
}
