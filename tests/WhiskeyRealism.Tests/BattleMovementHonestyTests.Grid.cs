using System;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC10 decision 1: the per-battle terrain grid over vanilla's 1 m terrain-id array.
internal static partial class BattleMovementHonestyTests
{
    // GamePrefs.terrainspeccost of the shipped battleprefs (ids 0-16): grass 2, 1 and 2 unused,
    // creek 5, river 5, ford 2, bridge 1, road 1, fences 4, pontoons 2, forest 4, swamp 6, field 3.
    internal static readonly int[] ShippedSpeedCost = { 2, 1, 2, 5, 5, 2, 1, 1, 4, 2, 2, 2, 2, 2, 4, 6, 3 };

    internal const int Grass = 0, Creek = 3, River = 4, Ford = 5, Bridge = 6, Road = 7, Fence = 8, Forest = 14, Swamp = 15;

    internal static BattleTerrainGrid GridOf(int sizeX, int sizeZ, Func<int, int, int> idAt, bool frozen = false,
        float[] crossingX = null, float[] crossingZ = null, int[] crossingId = null)
    {
        var ids = new int[sizeX + 1, sizeZ + 1];
        for (int x = 0; x <= sizeX; x++)
            for (int z = 0; z <= sizeZ; z++)
                ids[x, z] = idAt(x, z);
        int n = crossingX != null ? crossingX.Length : 0;
        return new BattleTerrainGrid(ids, sizeX, sizeZ, ShippedSpeedCost, frozen, crossingX, crossingZ, crossingId, n);
    }

    // BattlefieldSetup.GetCurrentTerrainOnPos (26727-26771): x index = (int)x, z index =
    // (int)(z + sizeZ) (battle z is negative); each index clamps to size - 1 (compared on the
    // truncated value) and to 0.
    private static void GridIndexesLikeGetCurrentTerrainOnPos()
    {
        BattleTerrainGrid g = GridOf(100, 80, (x, z) => x * 1000 + z);
        AssertEqual(10 * 1000 + 59, g.IdAt(10.7f, -20.3f), "x truncates, z = (int)(z + sizeZ)");
        AssertEqual(0 * 1000 + 0, g.IdAt(-3f, -90f), "negative indexes clamp to 0");
        AssertEqual(99 * 1000 + 79, g.IdAt(150f, 5f), "indexes clamp to size - 1");
        AssertEqual(99 * 1000 + 79, g.IdAt(100.2f, -0.2f), "(int)x = 100 > 99 clamps; z + sizeZ = 79.8 truncates to 79");
        AssertEqual(99 * 1000 + 0, g.IdAt(99.5f, -80f), "x 99.5 stays 99; z + sizeZ = 0");
        AssertEqual(5 * 1000 + 1, g.IdAt(5.99f, -78.99f), "truncation toward zero, not rounding");
    }

    private static void GridSampleLineCountsForestShare()
    {
        // The east half (x >= 200) is forest.
        BattleTerrainGrid g = GridOf(400, 100, (x, z) => x >= 200 ? Forest : Grass);
        var ids = new int[64];
        int n = g.SampleLine(0f, -50f, 400f, -50f, 10f, ids);
        AssertEqual(41, n, "samples every 10 m, both ends included");
        int forest = 0;
        for (int i = 0; i < n; i++) if (ids[i] == Forest) forest++;
        AssertEqual(21, forest, "x = 200..400 is forest");
        AssertEqual(1, g.SampleLine(5f, -5f, 5f, -5f, 10f, ids), "a zero-length line reads its one cell");
        AssertEqual(3, g.SampleLine(0f, -50f, 400f, -50f, 10f, new int[3]), "the span bounds the count");
        AssertEqual(0, g.SampleLine(0f, -50f, 400f, -50f, 0f, ids), "a non-positive step reads nothing");
    }

    private static void GridRiverImpassableUnlessFrozen()
    {
        BattleTerrainGrid water = GridOf(50, 50, (x, z) => Grass, frozen: false);
        AssertFalse(water.Passable(River), "an open river cannot be crossed (cost 500000 in AddPath, 130364-130394)");
        AssertTrue(water.Passable(Ford) && water.Passable(Bridge) && water.Passable(Creek) && water.Passable(Fence), "fords, bridges, creeks and fences are crossed");
        BattleTerrainGrid ice = GridOf(50, 50, (x, z) => Grass, frozen: true);
        AssertTrue(ice.Passable(River), "frozen ground carries the river (26230-26233)");
        ice.Frozen = false;
        AssertFalse(ice.Passable(River), "the flag is refreshed by the runtime accessor");
    }

    private static void GridSpeedCostFallsBackToGrassForUnknownIds()
    {
        BattleTerrainGrid g = GridOf(10, 10, (x, z) => Grass);
        AssertEqual(2, g.SpeedCost(Grass), "grass");
        AssertEqual(1, g.SpeedCost(Road), "road");
        AssertEqual(4, g.SpeedCost(Forest), "forest");
        AssertEqual(6, g.SpeedCost(Swamp), "swamp");
        AssertEqual(2, g.SpeedCost(40), "an id past the table (107567 writes ids 9 + rank) reads as grass");
        AssertEqual(2, g.SpeedCost(-1), "a negative id reads as grass");
    }

    private static void GridCrossingsAreCopiedAndBounded()
    {
        BattleTerrainGrid g = GridOf(100, 100, (x, z) => Grass,
            crossingX: new[] { 10f, 20f }, crossingZ: new[] { -10f, -20f }, crossingId: new[] { Ford, Bridge });
        AssertEqual(2, g.CrossingCount, "two crossings");
        g.Crossing(1, out float cx, out float cz, out int id);
        AssertNear(20f, cx, 0.001f, "x");
        AssertNear(-20f, cz, 0.001f, "z");
        AssertEqual(Bridge, id, "id");
        g.Crossing(7, out cx, out cz, out id);
        AssertEqual(0, id, "an out-of-range crossing reads as none");
        BattleTerrainGrid none = GridOf(10, 10, (x, z) => Grass);
        AssertEqual(0, none.CrossingCount, "no crossings");
        var ids = new int[64];
        AssertNoAllocation(() => { g.IdAt(5f, -5f); g.SampleLine(0f, -5f, 90f, -5f, 10f, ids); g.Passable(River); g.SpeedCost(Forest); }, 100, "grid reads allocate nothing");
    }
}
