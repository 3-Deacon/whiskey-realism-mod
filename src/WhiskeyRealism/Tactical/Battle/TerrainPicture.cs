using System;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC12 decision 1: the battle's terrain picture, a fog-free map of the public terrain on a 20 m lattice over
    // BattleTerrainGrid's 1 m id grid. Pure; built stepwise by TerrainPictureBuilder, immutable after Ready.
    //
    // World frame (BattleTerrainGrid.IdAt, GetCurrentTerrainOnPos 26727-26771): x in [0, SizeX], z in [-SizeZ, 0]; the
    // lattice cell of a point is (floor(x / 20), floor((z + SizeZ) / 20)). Every query on a picture that is not Ready
    // answers "no terrain" (0 / false / -1): the consumers keep their pre-BC12 behaviour until it is.
    public sealed class TerrainPicture
    {
        public const float CellMeters = 20f;
        /// <summary>The lattice cell whose id the 20 x 20 m cell takes: sampled every 5 m (16 reads).</summary>
        public const float SampleMeters = 5f;

        public const int IdRiver = BattleTerrainGrid.RiverId, IdCreek = 3, IdFord = BattleTerrainGrid.FordId, IdBridge = BattleTerrainGrid.BridgeId;

        // Vanilla cover ids (GetCoverIDFromName 138482-138502) and the defaults of GamePrefs.coverobjectvalues the doctrine pins
        // (the loss-reduction factors: wall 0.35, sunken road 0.30, embankment 0.20, fence 0.10, creek 0.08). The runtime hands
        // the builder the live table when vanilla has loaded it.
        public const int CoverFence = 1, CoverWall = 2, CoverRailroad = 4, CoverCreek = 5, CoverSunkenRoad = 15, CoverUnfinishedRailroad = 16;
        public const int CoverIdCount = 17;
        internal static readonly float[] DefaultCoverValues = BuildDefaultCoverValues();

        /// <summary>The 0.55 line planners treat as a strong anchor; the 0.35 floor of "strong ground" for the inference rule.</summary>
        public const float AnchorStrength = 0.55f;
        public const float StrongGroundStrength = 0.35f;

        public const float ClassifyRadiusMeters = 60f;
        public const float CoverRadiusMeters = 60f;
        public const float TownBuildingValue = 0.7f;
        public const float WaterBlockValue = 0.4f;
        public const float CrestFullProminenceMeters = 15f;
        public const float WaterShare = 0.6f;
        public const float ApproachMeters = 300f;

        private readonly BattleTerrainGrid _grid;

        internal byte[] IdLayer;
        internal float[] HeightLayer;
        internal float[] ProminenceLayer;
        internal sbyte[] AxisLayer;
        internal byte[] CoverLayer;
        internal bool[] RoadLayer;
        internal bool[] JunctionLayer;
        internal bool[] TownLayer;
        internal float[] CoverValues = DefaultCoverValues;

        internal TerrainPicture(BattleTerrainGrid grid, float sizeX, float sizeZ)
        {
            _grid = grid;
            SizeX = sizeX;
            SizeZ = sizeZ;
            Columns = Math.Max(1, (int)Math.Ceiling(sizeX / CellMeters));
            Rows = Math.Max(1, (int)Math.Ceiling(sizeZ / CellMeters));
        }

        public bool Ready { get; internal set; }
        /// <summary>The crest and anchored-flank rules this picture was built with (vanilla's prefs at runtime).</summary>
        public TerrainShapeRules Rules { get; internal set; } = TerrainShapeRules.Shipped;
        public float SizeX { get; }
        public float SizeZ { get; }
        public int Columns { get; }
        public int Rows { get; }

        // Counts for the "picture ready" line.
        public int CrestCells { get; internal set; }
        public int CoverSegmentCount { get; internal set; }
        public int WaterCells { get; internal set; }
        public int CrossingCount => _grid != null ? _grid.CrossingCount : 0;
        public int TownCells { get; internal set; }
        public int JunctionCells { get; internal set; }

        // ---- provider (the runtime binds it once; null in the harness unless a test sets it) --------------------------

        public static Func<TerrainPicture> Provider { get; set; }

        /// <summary>The Rollback flag (Enable Battle Terrain Picture) for decisions 3-6, 7's strong-ground rule and 8.</summary>
        public static Func<bool> PlanningEnabled { get; set; }

        /// <summary>The battle's picture once it is Ready, else null (every consumer then keeps today's behaviour).</summary>
        public static TerrainPicture Current()
        {
            TerrainPicture p = Provider != null ? Provider() : null;
            return p != null && p.Ready ? p : null;
        }

        /// <summary><see cref="Current"/> for the planners behind the rollback flag.</summary>
        public static TerrainPicture CurrentForPlanning()
        {
            Func<bool> enabled = PlanningEnabled;
            return enabled != null && enabled() ? Current() : null;
        }

        // ---- cell addressing ------------------------------------------------------------------------------------

        public int CellX(float x) => Clamp((int)Math.Floor(x / CellMeters), Columns - 1);
        public int CellZ(float z) => Clamp((int)Math.Floor((z + SizeZ) / CellMeters), Rows - 1);
        public float CenterX(int cx) => (cx + 0.5f) * CellMeters;
        public float CenterZ(int cz) => -SizeZ + (cz + 0.5f) * CellMeters;

        private static int Clamp(int v, int max) => v < 0 ? 0 : (v > max ? max : v);

        private bool In(int cx, int cz) => cx >= 0 && cz >= 0 && cx < Columns && cz < Rows;

        private int Index(int cx, int cz) => cz * Columns + cx;

        // ---- layers ----------------------------------------------------------------------------------------------

        /// <summary>The dominant terrain id of the cell (water and crossing ids win over land); 0 when unknown.</summary>
        public int Id(int cx, int cz) => Ready && In(cx, cz) ? IdLayer[Index(cx, cz)] : 0;

        public float Height(int cx, int cz) => Ready && In(cx, cz) ? HeightLayer[Index(cx, cz)] : 0f;

        /// <summary>Meters the cell stands above the cells 40 and 80 m away on both sides of its best axis; 0 when it is no crest.</summary>
        public float CrestProminence(int cx, int cz) => Ready && In(cx, cz) ? ProminenceLayer[Index(cx, cz)] : 0f;

        /// <summary>The profile axis of the crest (0, 1, 2, 3 = 0, 45, 90, 135 degrees; the crest runs perpendicular), -1 when none.</summary>
        public int CrestAxis(int cx, int cz) => Ready && In(cx, cz) ? AxisLayer[Index(cx, cz)] : -1;

        /// <summary>The cover id rasterised into the cell (vanilla GetCoverIDFromName), 0 when none.</summary>
        public int Cover(int cx, int cz) => Ready && In(cx, cz) ? CoverLayer[Index(cx, cz)] : 0;

        public float CoverValue(int coverId) => coverId > 0 && coverId < CoverValues.Length ? CoverValues[coverId] : 0f;

        /// <summary>River (4) or creek (3).</summary>
        public bool Water(int cx, int cz)
        {
            int id = Id(cx, cz);
            return id == IdRiver || id == IdCreek;
        }

        // ---- queries at world points ----------------------------------------------------------------------------

        public int IdAt(float x, float z) => Id(CellX(x), CellZ(z));

        public bool WaterAt(float x, float z) => Water(CellX(x), CellZ(z));

        public float CrestProminenceAt(float x, float z) => CrestProminence(CellX(x), CellZ(z));

        public int CoverAt(float x, float z) => Cover(CellX(x), CellZ(z));

        public float HeightAt(float x, float z) => Height(CellX(x), CellZ(z));

        /// <summary>The nearest listed ford or bridge within the radius (BC10's crossing list), in world coordinates.</summary>
        public bool TryNearestCrossing(float x, float z, float radius, out float cxW, out float czW, out int id)
        {
            cxW = czW = 0f;
            id = 0;
            if (!Ready || _grid == null) return false;
            float best = radius * radius;
            bool found = false;
            for (int k = 0; k < _grid.CrossingCount; k++)
            {
                _grid.Crossing(k, out float kx, out float kz, out int kid);
                float dx = kx - x, dz = kz - z;
                float d2 = dx * dx + dz * dz;
                if (d2 > best) continue;
                best = d2;
                cxW = kx;
                czW = kz;
                id = kid;
                found = true;
            }
            return found;
        }

        /// <summary>Whether a listed crossing of exactly this id (ford 5, bridge 6) lies within the radius.</summary>
        public bool CrossingNear(float x, float z, float radius, int wantedId)
        {
            if (!Ready || _grid == null) return false;
            float r2 = radius * radius;
            for (int k = 0; k < _grid.CrossingCount; k++)
            {
                _grid.Crossing(k, out float kx, out float kz, out int kid);
                if (kid != wantedId) continue;
                float dx = kx - x, dz = kz - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        /// <summary>Whether a crest cell of at least <paramref name="minProminence"/> meters lies within the radius.</summary>
        public bool CrestNear(float x, float z, float radius, float minProminence)
        {
            if (!Ready) return false;
            int reach = (int)Math.Ceiling(radius / CellMeters);
            int cx0 = CellX(x), cz0 = CellZ(z);
            float r2 = (radius + CellMeters * 0.5f) * (radius + CellMeters * 0.5f);
            for (int dz = -reach; dz <= reach; dz++)
            {
                int cz = cz0 + dz;
                if (cz < 0 || cz >= Rows) continue;
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int cx = cx0 + dx;
                    if (cx < 0 || cx >= Columns || ProminenceLayer[Index(cx, cz)] < minProminence || ProminenceLayer[Index(cx, cz)] <= 0f) continue;
                    float ex = CenterX(cx) - x, ez = CenterZ(cz) - z;
                    if (ex * ex + ez * ez <= r2) return true;
                }
            }
            return false;
        }

        public bool RoadJunctionNear(float x, float z, float radius) => Near(JunctionLayer, x, z, radius);

        public bool TownNear(float x, float z, float radius) => Near(TownLayer, x, z, radius);

        private bool Near(bool[] layer, float x, float z, float radius)
        {
            if (!Ready || layer == null) return false;
            int reach = (int)Math.Ceiling(radius / CellMeters) + 1;
            int cx0 = CellX(x), cz0 = CellZ(z);
            float r2 = (radius + CellMeters * 0.5f) * (radius + CellMeters * 0.5f);
            for (int dz = -reach; dz <= reach; dz++)
            {
                int cz = cz0 + dz;
                if (cz < 0 || cz >= Rows) continue;
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int cx = cx0 + dx;
                    if (cx < 0 || cx >= Columns || !layer[Index(cx, cz)]) continue;
                    float ex = CenterX(cx) - x, ez = CenterZ(cz) - z;
                    if (ex * ex + ez * ez <= r2) return true;
                }
            }
            return false;
        }

        // ---- decision 4: terrain strength and approach difficulty ----------------------------------------------

        /// <summary>
        /// 1 - (1 - c)(1 - h)(1 - w)(1 - b) for a point and the threat axis (unit vector toward the enemy): c the best cover
        /// value within 60 m, h = min(1, crest prominence / 15 m) x 0.5 (height raises fire range and cuts the enemy's sight),
        /// w = 0.4 when water covers at least 60 % of a lateral transect 100-300 m in front, b = 0.7 for a town.
        /// </summary>
        public float TerrainStrength(float x, float z, float axisX, float axisZ)
        {
            if (!Ready) return 0f;
            float c = BestCoverWithin(x, z, CoverRadiusMeters);
            float h = Math.Min(1f, MaxProminenceWithin(x, z, CellMeters) / CrestFullProminenceMeters) * 0.5f;
            float w = WaterAcrossFront(x, z, axisX, axisZ) ? WaterBlockValue : 0f;
            float b = TownNear(x, z, ClassifyRadiusMeters) ? TownBuildingValue : 0f;
            return 1f - (1f - c) * (1f - h) * (1f - w) * (1f - b);
        }

        /// <summary>
        /// clamp01((mean speed cost of the 300 m approach from the enemy side - grass cost) / (swamp cost - grass cost)),
        /// BattleTerrainGrid.SpeedCost per cell id (GamePrefs.terrainspeccost: grass 2, swamp 6). Speed-cost units.
        /// </summary>
        public float ApproachDifficulty(float x, float z, float axisX, float axisZ)
        {
            if (!Ready || _grid == null) return 0f;
            float len = (float)Math.Sqrt(axisX * axisX + axisZ * axisZ);
            if (!(len > 0.0001f)) return 0f;
            float ux = axisX / len, uz = axisZ / len;
            int samples = (int)(ApproachMeters / CellMeters);
            float sum = 0f;
            for (int i = 1; i <= samples; i++)
            {
                float d = i * CellMeters;
                sum += _grid.SpeedCost(IdAt(x + ux * d, z + uz * d));
            }
            float mean = sum / samples;
            float grass = _grid.SpeedCost(0);
            float swamp = _grid.SpeedCost(BattleTerrainGrid.SwampId);
            if (!(swamp > grass)) return 0f;
            float v = (mean - grass) / (swamp - grass);
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        private float BestCoverWithin(float x, float z, float radius)
        {
            int reach = (int)Math.Ceiling(radius / CellMeters);
            int cx0 = CellX(x), cz0 = CellZ(z);
            float r2 = (radius + CellMeters * 0.5f) * (radius + CellMeters * 0.5f);
            float best = 0f;
            for (int dz = -reach; dz <= reach; dz++)
            {
                int cz = cz0 + dz;
                if (cz < 0 || cz >= Rows) continue;
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int cx = cx0 + dx;
                    if (cx < 0 || cx >= Columns) continue;
                    int cover = CoverLayer[Index(cx, cz)];
                    if (cover == 0) continue;
                    float ex = CenterX(cx) - x, ez = CenterZ(cz) - z;
                    if (ex * ex + ez * ez > r2) continue;
                    float v = CoverValue(cover);
                    if (v > best) best = v;
                }
            }
            return best;
        }

        private float MaxProminenceWithin(float x, float z, float radius)
        {
            int reach = (int)Math.Ceiling(radius / CellMeters);
            int cx0 = CellX(x), cz0 = CellZ(z);
            float best = 0f;
            for (int dz = -reach; dz <= reach; dz++)
            {
                int cz = cz0 + dz;
                if (cz < 0 || cz >= Rows) continue;
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int cx = cx0 + dx;
                    if (cx < 0 || cx >= Columns) continue;
                    float p = ProminenceLayer[Index(cx, cz)];
                    if (p > best) best = p;
                }
            }
            return best;
        }

        // Water across the front: on any lateral transect 100-300 m in front (every 20 m), at least 60 % of the sample points
        // (every 30 m, +-150 m) are river or creek: a river line across the approach.
        private bool WaterAcrossFront(float x, float z, float axisX, float axisZ)
        {
            float len = (float)Math.Sqrt(axisX * axisX + axisZ * axisZ);
            if (!(len > 0.0001f)) return false;
            float ux = axisX / len, uz = axisZ / len;
            float lx = -uz, lz = ux;
            const int lateralSteps = 11;
            for (int d = 100; d <= 300; d += 20)
            {
                int water = 0;
                for (int s = -5; s <= 5; s++)
                {
                    float px = x + ux * d + lx * (s * 30f);
                    float pz = z + uz * d + lz * (s * 30f);
                    if (WaterAt(px, pz)) water++;
                }
                if (water >= (int)Math.Ceiling(WaterShare * lateralSteps)) return true;
            }
            return false;
        }

        private static float[] BuildDefaultCoverValues()
        {
            var table = new float[CoverIdCount];
            table[CoverFence] = 0.10f;
            table[CoverWall] = 0.35f;
            table[CoverRailroad] = 0.20f;
            table[CoverCreek] = 0.08f;
            table[CoverSunkenRoad] = 0.30f;
            table[CoverUnfinishedRailroad] = 0.20f;
            return table;
        }
    }
}
