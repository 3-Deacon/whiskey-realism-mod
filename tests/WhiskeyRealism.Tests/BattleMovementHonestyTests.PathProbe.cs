using System;
using System.IO;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC10 decision 4-5: the path probe's scores read the terrain grid, and the probe leaves
// vanilla's global NavMesh area costs alone.
internal static partial class BattleMovementHonestyTests
{
    private const int MapSize = 400;

    // A 400 x 100 map with a river band at x in [190, 210) crossed by a strip of the given id
    // within halfWidth metres of z = -50. World z = zIndex - 100.
    private static BattleTerrainGrid RiverGrid(int crossingId, float halfWidth = 3f, bool frozen = false, int bandId = River)
    {
        return GridOf(MapSize, 100, (x, zi) =>
        {
            float z = zi - 100f;
            if (x >= 190 && x < 210) return Math.Abs(z + 50f) <= halfWidth ? crossingId : bandId;
            return Grass;
        }, frozen: frozen);
    }

    private static PathTerrainScore ScoreEastWest(BattleTerrainGrid grid)
    {
        return PathTerrainScorer.Score(grid, new[] { 0f, 400f }, new[] { -50f, -50f }, 2);
    }

    private static void RoadShareCountsRoadAndBridgeSamples()
    {
        // A road on x in [0, 100) then open ground, a bridge in the middle: 41 samples at 10 m.
        BattleTerrainGrid grid = GridOf(MapSize, 100, (x, zi) =>
        {
            if (x < 100) return Road;
            if (x >= 190 && x < 210) return Bridge;
            return Grass;
        });
        PathTerrainScore score = ScoreEastWest(grid);
        AssertNear((10f + 2f) / 41f, score.RoadShare, 0.001f, "10 road samples (x 0..90) and 2 bridge samples (x 190, 200) over 41");

        BattleTerrainGrid open = GridOf(MapSize, 100, (x, zi) => Grass);
        AssertNear(0f, ScoreEastWest(open).RoadShare, 0.0001f, "open ground has no road share");
        BattleTerrainGrid road = GridOf(MapSize, 100, (x, zi) => Road);
        AssertNear(1f, ScoreEastWest(road).RoadShare, 0.0001f, "a road all the way");
        // A multi-corner polyline is sampled along its legs (the corners themselves are not samples).
        BattleTerrainGrid bend = GridOf(MapSize, 100, (x, zi) => zi >= 80 ? Road : Grass);
        PathTerrainScore elbow = PathTerrainScorer.Score(bend, new[] { 0f, 100f, 100f }, new[] { -50f, -50f, -10f }, 3);
        AssertTrue(elbow.RoadShare > 0f && elbow.RoadShare < 1f, "only the second leg runs on the road (z index >= 80): " + elbow.RoadShare);
        AssertNear(0f, PathTerrainScorer.Score(bend, new float[0], new float[0], 0).RoadShare, 0.0001f, "no corners");
        AssertNear(0f, PathTerrainScorer.Score(null, new[] { 0f, 400f }, new[] { -50f, -50f }, 2).RoadShare, 0.0001f, "no grid: neutral");
    }

    // The old probe read the three corners of the NavMesh path: a bridge corner was a river risk.
    private static void BridgePathIsNotRiverRisk()
    {
        PathTerrainScore wide = ScoreEastWest(RiverGrid(Bridge, halfWidth: 3f));
        AssertNear(0f, wide.TerrainRisk, 0.0001f, "walking over a bridge is no river risk");
        AssertNear(0.55f, wide.BridgeRisk, 0.0001f, "but it is a choke");
        PathTerrainScore narrow = ScoreEastWest(RiverGrid(Bridge, halfWidth: 1f));
        AssertNear(0f, narrow.TerrainRisk, 0.0001f, "a narrow bridge is still no river risk");
        AssertNear(0.70f, narrow.BridgeRisk, 0.0001f, "a crossing that passes within 2 m of the river is the worse choke");
    }

    private static void FordPathIsChokeNotRisk()
    {
        PathTerrainScore ford = ScoreEastWest(RiverGrid(Ford, halfWidth: 3f));
        AssertNear(0f, ford.TerrainRisk, 0.0001f, "a ford is no river risk");
        AssertNear(0.55f, ford.BridgeRisk, 0.0001f, "a ford is a choke");
        AssertNear(0f, ScoreEastWest(RiverGrid(Ford, halfWidth: 3f)).RoadShare, 0.0001f, "a ford is not road");
    }

    private static void FenceOnPathIsRisk()
    {
        PathTerrainScore fence = ScoreEastWest(GridOf(MapSize, 100, (x, zi) => x >= 190 && x < 195 ? Fence : Grass));
        AssertNear(0.85f, fence.TerrainRisk, 0.0001f, "a fence or wall on the path");
        PathTerrainScore river = ScoreEastWest(RiverGrid(River, halfWidth: -1f));
        AssertNear(0.85f, river.TerrainRisk, 0.0001f, "an open river with no crossing under the path");
        AssertNear(0f, river.BridgeRisk, 0.0001f, "no crossing cell, no choke");
    }

    private static void FrozenRiverIsNotRisk()
    {
        PathTerrainScore ice = ScoreEastWest(RiverGrid(River, halfWidth: -1f, frozen: true));
        AssertNear(0f, ice.TerrainRisk, 0.0001f, "a frozen river carries the path");
    }

    // The old proxy gave every threat a distance-based dead ground; now it is 0 without a
    // threat and 0.70 only when the terrain blocks the line of sight (runtime Map-layer raycast).
    private static void DeadGroundNeedsAThreat()
    {
        AssertNear(0f, PathTerrainScorer.DeadGround(hasThreat: false, terrainBlocksSight: true), 0.0001f, "no threat, no dead ground");
        AssertNear(0f, PathTerrainScorer.DeadGround(hasThreat: true, terrainBlocksSight: false), 0.0001f, "an open line is not dead ground");
        AssertNear(0.70f, PathTerrainScorer.DeadGround(hasThreat: true, terrainBlocksSight: true), 0.0001f, "terrain between the threat and the candidate");
    }

    // Source pins (the probe runs in the Unity runtime, which the harness cannot load):
    // no code under Tactical/ calls the global NavMesh.SetAreaCost; the posture producer builds
    // a NavMeshQueryFilter, copies every area's cost into it (a fresh filter prices every area
    // at 1) and passes it to CalculatePath; and the raycast is guarded by the threat.
    private static void PathProbeUsesAQueryFilterNotTheGlobalAreaCosts()
    {
        string tactical = TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical");
        foreach (string path in Directory.GetFiles(tactical, "*.cs", SearchOption.AllDirectories))
        {
            string code = StripComments(File.ReadAllText(path));
            AssertFalse(Regex.IsMatch(code, @"NavMesh\.SetAreaCost\("), Path.GetFileName(path) + " writes the global NavMesh area costs");
        }

        string posture = StripComments(File.ReadAllText(Path.Combine(tactical, "Battle/Producers/PostureIntentProducer.cs")));
        AssertTrue(posture.Contains("NavMeshQueryFilter"), "the probe builds a NavMeshQueryFilter");
        AssertTrue(Regex.IsMatch(posture, @"NavMesh\.CalculatePath\(\s*\w+,\s*\w+,\s*[\w.]*[Ff]ilter\s*,\s*\w+\)"), "CalculatePath takes the filter overload");
        AssertTrue(Regex.IsMatch(posture, @"NavMesh\.GetAreaCost\("), "every area's cost is copied from the live NavMesh");
        AssertTrue(posture.Contains("filter.SetAreaCost("), "the four AddPath costs go on the filter");
        AssertFalse(posture.Contains("area-cost-failed"), "the old OnceLog key is gone");
        AssertTrue(posture.Contains("tactical-producer-posture:nav-filter-failed"), "the renamed key");
        AssertTrue(Regex.IsMatch(posture, @"if\s*\(\s*hasThreat\s*\)\s*\{[^}]*TerrainBlocksSight\(", RegexOptions.Singleline), "the raycast sits behind the threat");
        AssertEqual(2, Regex.Matches(posture, @"TerrainBlocksSight\(").Count, "the raycast helper is called from one place");
    }
}
