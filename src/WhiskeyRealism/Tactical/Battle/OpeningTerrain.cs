using System;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 (spec § 5, requirement-detect-flank-in-air; § 6, requirement-risk-check recovery-path): the public terrain the
    // opening detector and the risk check read, over the BC10 terrain grid and the BC12 terrain picture. Pure.
    //
    // River anchor: a river cell (BC10 id 4) within the distance beyond a point along a direction, vanilla's
    // CheckIfFlanksAreAnchored river test (6599) applied to the enemy's line. A frozen map carries units, so a frozen
    // river anchors nothing. Crest anchor: a picture crest cell of the picture's own steepness minimum (the
    // maxsteepnessofterrainforshape, 3 m) within the distance, vanilla's peak test; false while the picture is not ready.
    // Recovery path: the segment is sampled every 2 m and crosses a river when it meets an id-4 cell; a ford (5) or a
    // bridge (6) passes. A frozen map passes. With no grid there is no knowledge, so the check fails closed (an exploit
    // needs a path home) while the anchor tests answer "no river" (spec § 10, Terrain).
    public sealed class OpeningTerrain : IOpeningTerrain
    {
        public const float RiverStepMeters = 5f;
        public const float SegmentStepMeters = 2f;

        private readonly BattleTerrainGrid _grid;
        private readonly TerrainPicture _picture;

        public OpeningTerrain(BattleTerrainGrid grid, TerrainPicture picture)
        {
            _grid = grid;
            _picture = picture;
        }

        public bool RiverWithin(float x, float z, float dirX, float dirZ, float meters)
        {
            if (_grid == null || _grid.Frozen || !(meters >= 0f)) return false;
            float length = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (length < 1e-6f) return false;
            float ux = dirX / length, uz = dirZ / length;
            for (float d = 0f; d <= meters; d += RiverStepMeters)
                if (_grid.IdAt(x + ux * d, z + uz * d) == BattleTerrainGrid.RiverId) return true;
            return _grid.IdAt(x + ux * meters, z + uz * meters) == BattleTerrainGrid.RiverId;
        }

        public bool CrestWithin(float x, float z, float meters)
        {
            return _picture != null && _picture.Ready && _picture.CrestNear(x, z, meters, _picture.Rules.MinSteepness);
        }

        public bool SegmentCrossesRiver(float x0, float z0, float x1, float z1)
        {
            if (_grid == null) return true;
            if (_grid.Frozen) return false;
            float dx = x1 - x0, dz = z1 - z0;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length <= 0f) return _grid.IdAt(x0, z0) == BattleTerrainGrid.RiverId;
            float ux = dx / length, uz = dz / length;
            for (float d = 0f; d <= length; d += SegmentStepMeters)
                if (_grid.IdAt(x0 + ux * d, z0 + uz * d) == BattleTerrainGrid.RiverId) return true;
            return _grid.IdAt(x1, z1) == BattleTerrainGrid.RiverId;
        }
    }
}
