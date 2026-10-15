using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>The terrain-honest part of one candidate path's quality sample.</summary>
    public struct PathTerrainScore
    {
        public float RoadShare;   // share of 10 m samples on road (7) or bridge (6)
        public float TerrainRisk; // 0.85 when a sample is an open river (4) or fence/wall (8) and no crossing carries it
        public float BridgeRisk;  // the choke: 0.55 on a ford (5) or bridge (6), 0.70 when also within 2 m of river or fence
    }

    // The posture producer's path probe scores from the terrain grid (BC10 decision 5).
    // The shipped probe read the three corners of the NavMesh path: a path of at most three
    // corners scored 0.65 road preference whatever it walked on, and a bridge or ford
    // corner read as a river risk. This samples the polyline every 10 m instead. Pure.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   Terrain ids: BattleTerrainGrid (4 river, 5 ford, 6 bridge, 7 road, 8 fences/walls).
    //   AddPath prices Roads and Bridges at 1 (2 with skiproads) against 2 for open ground,
    //     FencesWalls at 2 (500 for an interrupted path) and the open river at 500000
    //     (130359-130394), so the NavMesh route already prefers roads; the scores say what
    //     the route actually walks on.
    //   Dead ground (a Map-layer raycast from the threat's eye to the candidate's, +4 m each,
    //     DrawVisibility 101061-101063) is a runtime read; DeadGround here only names the value.
    public static class PathTerrainScorer
    {
        public const float SampleMeters = 10f;
        public const float RiskedTerrain = 0.85f;
        public const float ChokeRisk = 0.55f;
        public const float ChokeNearTerrainRisk = 0.70f;
        public const float DeadGroundValue = 0.70f;
        public const float NearTerrainMeters = 2f;
        private const int MaxSamples = 4096;

        public static PathTerrainScore Score(BattleTerrainGrid g, ReadOnlySpan<float> cornerX, ReadOnlySpan<float> cornerZ, int corners)
        {
            var score = new PathTerrainScore();
            if (g == null) return score;
            corners = Math.Min(corners, Math.Min(cornerX.Length, cornerZ.Length));
            if (corners < 1) return score;

            int samples = 0, roads = 0;
            bool usesCrossing = false, nearTerrain = false, risk = false;
            int before = -1, current = -1; // the previous two sample ids (-1: none)
            // One sample for a single corner, else every 10 m along each leg (a leg's start
            // is the previous leg's end, so only the first leg samples its start).
            for (int c = 0; c < Math.Max(corners - 1, 1) && samples < MaxSamples; c++)
            {
                float x0 = cornerX[c];
                float z0 = cornerZ[c];
                float x1 = corners > 1 ? cornerX[c + 1] : x0;
                float z1 = corners > 1 ? cornerZ[c + 1] : z0;
                float dx = x1 - x0;
                float dz = z1 - z0;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                int n = (int)Math.Floor(d / SampleMeters) + 1;
                float ux = d > 0f ? dx / d : 0f;
                float uz = d > 0f ? dz / d : 0f;
                for (int i = c == 0 ? 0 : 1; i < n && samples < MaxSamples; i++)
                {
                    float x = x0 + ux * i * SampleMeters;
                    float z = z0 + uz * i * SampleMeters;
                    int next = g.IdAt(x, z);
                    samples++;
                    if (next == BattleTerrainGrid.RoadId || next == BattleTerrainGrid.BridgeId) roads++;
                    if (IsCrossing(next)) usesCrossing = true;
                    if (NearRiverOrFence(g, x, z)) nearTerrain = true;
                    if (CarriesNoCrossing(g, before, current, next)) risk = true;
                    before = current;
                    current = next;
                }
            }
            if (CarriesNoCrossing(g, before, current, -1)) risk = true;

            score.RoadShare = samples > 0 ? (float)roads / samples : 0f;
            if (risk) score.TerrainRisk = RiskedTerrain;
            if (usesCrossing) score.BridgeRisk = nearTerrain ? ChokeNearTerrainRisk : ChokeRisk;
            return score;
        }

        /// <summary>0.70 when terrain stands between a threat and the candidate, else 0; always 0 without a threat.</summary>
        public static float DeadGround(bool hasThreat, bool terrainBlocksSight)
        {
            return hasThreat && terrainBlocksSight ? DeadGroundValue : 0f;
        }

        private static bool IsRiskTerrain(BattleTerrainGrid g, int id)
        {
            return id == BattleTerrainGrid.FenceId || (id == BattleTerrainGrid.RiverId && !g.Passable(id));
        }

        private static bool IsCrossing(int id)
        {
            return id == BattleTerrainGrid.FordId || id == BattleTerrainGrid.BridgeId;
        }

        // A risked sample (open river, fence or wall) that neither neighbouring sample carries
        // over a ford or bridge: the path really walks into it.
        private static bool CarriesNoCrossing(BattleTerrainGrid g, int before, int current, int next)
        {
            return current >= 0 && IsRiskTerrain(g, current) && !IsCrossing(before) && !IsCrossing(next);
        }

        private static bool NearRiverOrFence(BattleTerrainGrid g, float x, float z)
        {
            return IsRiskTerrain(g, g.IdAt(x, z))
                || IsRiskTerrain(g, g.IdAt(x + NearTerrainMeters, z)) || IsRiskTerrain(g, g.IdAt(x - NearTerrainMeters, z))
                || IsRiskTerrain(g, g.IdAt(x, z + NearTerrainMeters)) || IsRiskTerrain(g, g.IdAt(x, z - NearTerrainMeters));
        }
    }
}
