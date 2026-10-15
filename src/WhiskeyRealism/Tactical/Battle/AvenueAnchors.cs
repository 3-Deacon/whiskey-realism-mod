using System;

namespace WhiskeyRealism.Tactical.Battle
{
    // Whether an enemy approach avenue (entry point to its trooptargetposition) follows a road
    // or a crossing, read from the terrain grid (BC10 decision 11). Pure.
    //
    // Before, TacticalVisionRuntimeAdapter marked every entry point that had a target position
    // road-anchored and called an avenue crossing-anchored when a river or fence lay within 3 m
    // of its origin, midpoint or target (BattlefieldSetup.SearchTerrainInRangePos). The anchors
    // feed TacticalDefensiveLineAnchorPlanner's road intercept and frontage, so an avenue that
    // only looked like a road moved the line.
    //   road-anchored: at least half of the segment's 10 m samples are road (7) or bridge (6), or
    //     the entry point lies within 20 m of a road cell;
    //   crossing-anchored: a sample is a ford (5) or bridge (6), or the segment passes within 50 m of
    //     a vanilla rivercrossingpositions point (the grid's listed crossings).
    // A missing grid anchors nothing.
    public static class AvenueAnchors
    {
        public const float SampleMeters = 10f;
        public const float RoadShareMin = 0.5f;
        public const float EntryRoadMeters = 20f;
        public const float CrossingPointMeters = 50f;
        private const int MaxSamples = 1024;

        public static bool RoadAnchored(BattleTerrainGrid g, float entryX, float entryZ, float targetX, float targetZ)
        {
            if (g == null) return false;
            Span<int> ids = stackalloc int[MaxSamples];
            int n = g.SampleLine(entryX, entryZ, targetX, targetZ, SampleMeters, ids);
            int road = 0;
            for (int i = 0; i < n; i++)
                if (ids[i] == BattleTerrainGrid.RoadId || ids[i] == BattleTerrainGrid.BridgeId) road++;
            if (n > 0 && road >= RoadShareMin * n) return true;
            return RoadWithin(g, entryX, entryZ);
        }

        public static bool CrossingAnchored(BattleTerrainGrid g, float entryX, float entryZ, float targetX, float targetZ)
        {
            if (g == null) return false;
            Span<int> ids = stackalloc int[MaxSamples];
            int n = g.SampleLine(entryX, entryZ, targetX, targetZ, SampleMeters, ids);
            for (int i = 0; i < n; i++)
                if (ids[i] == BattleTerrainGrid.FordId || ids[i] == BattleTerrainGrid.BridgeId) return true;
            for (int k = 0; k < g.CrossingCount; k++)
            {
                g.Crossing(k, out float cx, out float cz, out _);
                if (DistanceToSegment(cx, cz, entryX, entryZ, targetX, targetZ) <= CrossingPointMeters) return true;
            }
            return false;
        }

        // The point and a ring of eight at 10 m and at 20 m.
        private static bool RoadWithin(BattleTerrainGrid g, float x, float z)
        {
            if (g.IdAt(x, z) == BattleTerrainGrid.RoadId) return true;
            for (int ring = 1; ring <= 2; ring++)
            {
                float radius = ring * (EntryRoadMeters / 2f);
                for (int step = 0; step < 8; step++)
                {
                    double angle = step * Math.PI / 4.0;
                    int id = g.IdAt(x + (float)Math.Cos(angle) * radius, z + (float)Math.Sin(angle) * radius);
                    if (id == BattleTerrainGrid.RoadId) return true;
                }
            }
            return false;
        }

        private static float DistanceToSegment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax;
            float dz = bz - az;
            float lengthSq = dx * dx + dz * dz;
            float t = lengthSq > 0f ? ((px - ax) * dx + (pz - az) * dz) / lengthSq : 0f;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            float qx = ax + dx * t - px;
            float qz = az + dz * t - pz;
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }
    }
}
