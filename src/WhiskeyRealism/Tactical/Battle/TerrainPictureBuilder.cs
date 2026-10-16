using System;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC12 decision 2: the stepwise, resumable build of TerrainPicture. Pure: the Unity-side reads (the heightmap, the tagged
    // cover colliders, the Building components) arrive through three small interfaces so the harness drives the same code with
    // fixtures. Every stage is made of units of work; a Step does units until the injected clock passes the deadline (real
    // time is allowed for a CPU budget, AGENTS § Clocks) and stops after the first unit that ends past it, so a Step always
    // makes progress. All storage is allocated in the first Step; later steps allocate nothing.

    /// <summary>One sampled heightmap strip: runtime TerrainData.GetHeights, tests a fixture.</summary>
    public interface IHeightSource
    {
        int Rows { get; }
        int Columns { get; }
        /// <summary>World heights (meters) of heightmap row <paramref name="row"/>, Columns values; false when unreadable.</summary>
        bool TryReadRow(int row, float[] heights);
    }

    /// <summary>Tagged cover colliders as line segments; one tag search or CoverChunkObjects objects per call.</summary>
    public interface ICoverSource
    {
        bool Done { get; }
        int NextChunk(CoverSegment[] into);
    }

    /// <summary>Building positions, BuildingChunk per call.</summary>
    public interface IBuildingSource
    {
        bool Done { get; }
        int NextChunk(float[] x, float[] z);
    }

    public struct CoverSegment
    {
        public float X0, Z0, X1, Z1;
        public int CoverId;
    }

    /// <summary>Vanilla's CheckTerrainPeak and anchored-flank numbers (battleprefs / unitprefs), as the picture reads them.</summary>
    public struct TerrainShapeRules
    {
        public float MinRelief, MinSteepness, PeakNearMeters, PeakFarMeters, AnchorRiverMeters, AnchorCrestMeters;

        /// <summary>maxterrainshapeheightdifference 3 m (unitprefs 918), maxsteepnessofterrainforshape 3 m (920),
        /// distancetocheckforanchoredflanksriver 80 m (battleprefs 644), ...hills 50 m (646); peaks 40 and 80 m away.</summary>
        public static TerrainShapeRules Shipped => new TerrainShapeRules
        {
            MinRelief = 3f, MinSteepness = 3f, PeakNearMeters = 40f, PeakFarMeters = 80f, AnchorRiverMeters = 80f, AnchorCrestMeters = 50f,
        };
    }

    public sealed class TerrainPictureBuilder
    {
        /// <summary>CPU budget of one Step after it starts (the runtime turns it into ticks); a step ends after the unit that passes it.</summary>
        public const float BuildStepBudgetMs = 4f;
        public const int CoverChunkObjects = 32;
        public const int BuildingChunk = 64;
        public const int MaxCoverSegments = 8192;
        public const int MaxBuildings = 4096;

        private const float TownRadiusMeters = 150f;
        private const int TownBuildings = 3;
        private const int JunctionRingCells = 3;
        private const int JunctionRingPoints = 16;
        private const int JunctionRuns = 3;
        private const int RoadSamplesForCell = 2;

        private enum Stage { Cover, Ids, Heights, Crests, CoverRaster, Buildings, BuildingIndex, Towns, Junctions, Done }

        private readonly BattleTerrainGrid _grid;
        private readonly TerrainShapeRules _rules;
        private readonly TerrainPicture _picture;
        private bool _allocated;
        private Stage _stage = Stage.Cover;
        private int _index;

        // storage (first Step)
        private CoverSegment[] _chunk;
        private CoverSegment[] _segments;
        private int _segmentCount;
        private float[] _heightRow;
        private float[] _bx, _bz;
        private int _buildingCount;
        private float[] _chunkX, _chunkZ;
        private int[] _bucketStart, _bucketOrder, _bucketOf;
        private int _bucketColumns, _bucketRows;
        private readonly int[] _idCounts = new int[32];

        public TerrainPictureBuilder(BattleTerrainGrid grid, float sizeX, float sizeZ, in TerrainShapeRules rules)
        {
            _grid = grid;
            _rules = rules;
            _picture = new TerrainPicture(grid, sizeX, sizeZ) { Rules = rules };
        }

        public TerrainPicture Picture => _picture;
        public bool Failed { get; private set; }
        public string FailureReason { get; private set; }

        /// <summary>Units of work done by the last Step / by every Step (the runtime reports steps and the harness pins budgets).</summary>
        public int UnitsLastStep { get; private set; }
        public int TotalUnits { get; private set; }

        /// <summary>GamePrefs.coverobjectvalues by cover id, when the runtime has it (else the doctrine defaults).</summary>
        public float[] CoverValues
        {
            get { return _picture.CoverValues; }
            set { if (value != null && value.Length > 0) _picture.CoverValues = value; }
        }

        /// <summary>
        /// Runs units of work until the build is Ready, <paramref name="clock"/> reaches <paramref name="deadlineTicks"/> after a
        /// unit, or the build fails. True when Ready. The clock is Stopwatch.GetTimestamp at runtime, a scripted counter in tests.
        /// </summary>
        public bool Step(IHeightSource heights, ICoverSource cover, IBuildingSource buildings, long deadlineTicks, Func<long> clock)
        {
            UnitsLastStep = 0;
            if (_picture.Ready) return true;
            if (Failed) return false;
            if (!_allocated && !Allocate(heights)) return false;

            while (true)
            {
                bool worked;
                try
                {
                    worked = DoUnit(heights, cover, buildings);
                }
                catch (Exception ex)
                {
                    Fail("exception:" + ex.GetType().Name);
                    return false;
                }
                if (Failed) return false;
                if (worked)
                {
                    UnitsLastStep++;
                    TotalUnits++;
                }
                if (_stage == Stage.Done)
                {
                    _picture.Ready = true;
                    return true;
                }
                if (worked && clock() >= deadlineTicks) return false;
            }
        }

        private void Fail(string reason)
        {
            Failed = true;
            FailureReason = reason;
        }

        private bool Allocate(IHeightSource heights)
        {
            if (_grid == null) { Fail("grid"); return false; }
            if (heights == null || heights.Rows < 2 || heights.Columns < 2) { Fail("heights"); return false; }
            int n = _picture.Columns * _picture.Rows;
            _picture.IdLayer = new byte[n];
            _picture.HeightLayer = new float[n];
            _picture.ProminenceLayer = new float[n];
            _picture.AxisLayer = new sbyte[n];
            _picture.CoverLayer = new byte[n];
            _picture.RoadLayer = new bool[n];
            _picture.JunctionLayer = new bool[n];
            _picture.TownLayer = new bool[n];
            for (int i = 0; i < n; i++) _picture.AxisLayer[i] = -1;
            _chunk = new CoverSegment[CoverChunkObjects * 16];
            _segments = new CoverSegment[MaxCoverSegments];
            _heightRow = new float[heights.Columns];
            _bx = new float[MaxBuildings];
            _bz = new float[MaxBuildings];
            _chunkX = new float[BuildingChunk];
            _chunkZ = new float[BuildingChunk];
            _bucketColumns = (int)Math.Ceiling(_picture.SizeX / TownRadiusMeters) + 1;
            _bucketRows = (int)Math.Ceiling(_picture.SizeZ / TownRadiusMeters) + 1;
            _bucketStart = new int[_bucketColumns * _bucketRows + 1];
            _bucketOrder = new int[MaxBuildings];
            _bucketOf = new int[MaxBuildings];
            _allocated = true;
            return true;
        }

        // One unit of work in the current stage; false (and a stage change) when the stage had nothing left.
        private bool DoUnit(IHeightSource heights, ICoverSource cover, IBuildingSource buildings)
        {
            switch (_stage)
            {
                case Stage.Cover:
                    if (cover == null || cover.Done) { Advance(Stage.Ids); return false; }
                    CollectCover(cover);
                    return true;
                case Stage.Ids:
                    if (_index >= _picture.Rows) { Advance(Stage.Heights); return false; }
                    BuildIdRow(_index++);
                    return true;
                case Stage.Heights:
                    if (_index >= _picture.Rows) { Advance(Stage.Crests); return false; }
                    if (!BuildHeightRow(heights, _index++)) { Fail("heights"); return false; }
                    return true;
                case Stage.Crests:
                    if (_index >= _picture.Rows) { Advance(Stage.CoverRaster); return false; }
                    BuildCrestRow(_index++);
                    return true;
                case Stage.CoverRaster:
                    if (_index >= _segmentCount) { _picture.CoverSegmentCount = _segmentCount; Advance(Stage.Buildings); return false; }
                    Rasterise(ref _segments[_index++]);
                    return true;
                case Stage.Buildings:
                    if (buildings == null || buildings.Done) { Advance(Stage.BuildingIndex); return false; }
                    CollectBuildings(buildings);
                    return true;
                case Stage.BuildingIndex:
                    IndexBuildings();
                    Advance(Stage.Towns);
                    return true;
                case Stage.Towns:
                    if (_index >= _buildingCount) { Advance(Stage.Junctions); return false; }
                    MarkTowns();
                    return true;
                case Stage.Junctions:
                    if (_index >= _picture.Rows) { Advance(Stage.Done); return false; }
                    BuildJunctionRow(_index++);
                    return true;
                default:
                    return false;
            }
        }

        private void Advance(Stage next)
        {
            _stage = next;
            _index = 0;
        }

        // ---- stage 0: cover collection ------------------------------------------------------------------------

        private void CollectCover(ICoverSource cover)
        {
            int n = cover.NextChunk(_chunk);
            for (int i = 0; i < n && _segmentCount < MaxCoverSegments; i++) _segments[_segmentCount++] = _chunk[i];
        }

        // ---- stage 1: ids (16 reads per cell) ----------------------------------------------------------------

        private void BuildIdRow(int cz)
        {
            TerrainPicture p = _picture;
            float zBase = -p.SizeZ + cz * TerrainPicture.CellMeters;
            for (int cx = 0; cx < p.Columns; cx++)
            {
                Array.Clear(_idCounts, 0, _idCounts.Length);
                float xBase = cx * TerrainPicture.CellMeters;
                int roads = 0;
                for (int j = 0; j < 4; j++)
                {
                    float z = zBase + (j + 0.5f) * TerrainPicture.SampleMeters;
                    for (int i = 0; i < 4; i++)
                    {
                        float x = xBase + (i + 0.5f) * TerrainPicture.SampleMeters;
                        int id = _grid.IdAt(x, z);
                        if (id == BattleTerrainGrid.RoadId) roads++;
                        _idCounts[id >= 0 && id < _idCounts.Length ? id : 0]++;
                    }
                }
                int index = cz * p.Columns + cx;
                int chosen = DominantId();
                p.IdLayer[index] = (byte)chosen;
                p.RoadLayer[index] = roads >= RoadSamplesForCell;
                if (chosen == TerrainPicture.IdRiver || chosen == TerrainPicture.IdCreek) p.WaterCells++;
            }
        }

        // Bridge > ford > river > creek win over land; otherwise the most frequent id (the lower id on a tie).
        private int DominantId()
        {
            if (_idCounts[TerrainPicture.IdBridge] > 0) return TerrainPicture.IdBridge;
            if (_idCounts[TerrainPicture.IdFord] > 0) return TerrainPicture.IdFord;
            if (_idCounts[TerrainPicture.IdRiver] > 0) return TerrainPicture.IdRiver;
            if (_idCounts[TerrainPicture.IdCreek] > 0) return TerrainPicture.IdCreek;
            int best = 0, bestCount = -1;
            for (int id = 0; id < _idCounts.Length; id++)
            {
                if (_idCounts[id] > bestCount) { bestCount = _idCounts[id]; best = id; }
            }
            return best;
        }

        // ---- stage 2: heights (one heightmap strip per cell row, the sample nearest each cell center) --------

        private bool BuildHeightRow(IHeightSource heights, int cz)
        {
            TerrainPicture p = _picture;
            float zc = (cz + 0.5f) * TerrainPicture.CellMeters;
            int row = Round(zc / p.SizeZ * (heights.Rows - 1), heights.Rows - 1);
            if (!heights.TryReadRow(row, _heightRow)) return false;
            for (int cx = 0; cx < p.Columns; cx++)
            {
                float xc = (cx + 0.5f) * TerrainPicture.CellMeters;
                int col = Round(xc / p.SizeX * (heights.Columns - 1), heights.Columns - 1);
                p.HeightLayer[cz * p.Columns + cx] = _heightRow[col];
            }
            return true;
        }

        private static int Round(float v, int max)
        {
            int r = (int)Math.Floor(v + 0.5f);
            return r < 0 ? 0 : (r > max ? max : r);
        }

        // ---- stage 3: crests (four axes; CheckTerrainPeak's two tests) ----------------------------------------

        private static readonly int[] AxisDx = { 1, 1, 0, 1 };
        private static readonly int[] AxisDz = { 0, 1, 1, -1 };
        // The axes are compared by slope (prominence per meter of the axis step), so a ridge along x reads on the z axis, not on
        // the diagonals that drop by the same height over a longer step.
        private static readonly float[] AxisLength = { 1f, 1.41421356f, 1f, 1.41421356f };

        private void BuildCrestRow(int cz)
        {
            TerrainPicture p = _picture;
            int near = Math.Max(1, (int)Math.Round(_rules.PeakNearMeters / TerrainPicture.CellMeters));
            int far = Math.Max(near + 1, (int)Math.Round(_rules.PeakFarMeters / TerrainPicture.CellMeters));
            for (int cx = 0; cx < p.Columns; cx++)
            {
                int index = cz * p.Columns + cx;
                float h = p.HeightLayer[index];
                float bestProminence = 0f, bestSlope = 0f;
                int bestAxis = -1;
                for (int a = 0; a < 4; a++)
                {
                    int dx = AxisDx[a], dz = AxisDz[a];
                    // The window of the far offset on both sides must lie inside the map.
                    int x1 = cx + far * dx, z1 = cz + far * dz, x2 = cx - far * dx, z2 = cz - far * dz;
                    if (x1 < 0 || x1 >= p.Columns || x2 < 0 || x2 >= p.Columns || z1 < 0 || z1 >= p.Rows || z2 < 0 || z2 >= p.Rows) continue;
                    float higher = float.MinValue, lo = h, hi = h;
                    for (int k = -far; k <= far; k++)
                    {
                        if (k == 0) continue;
                        float v = p.HeightLayer[(cz + k * dz) * p.Columns + (cx + k * dx)];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                        int abs = k < 0 ? -k : k;
                        if ((abs == near || abs == far) && v > higher) higher = v;
                    }
                    float prominence = h - higher;
                    if (prominence < _rules.MinSteepness) continue;
                    if (hi - lo < _rules.MinRelief) continue;
                    float slope = prominence / AxisLength[a];
                    if (slope > bestSlope)
                    {
                        bestSlope = slope;
                        bestProminence = prominence;
                        bestAxis = a;
                    }
                }
                p.ProminenceLayer[index] = bestProminence;
                p.AxisLayer[index] = (sbyte)bestAxis;
                if (bestAxis >= 0) p.CrestCells++;
            }
        }

        // ---- stage 4: cover rasterising (one segment per unit) ----------------------------------------------

        private void Rasterise(ref CoverSegment s)
        {
            TerrainPicture p = _picture;
            if (s.CoverId <= 0 || s.CoverId > 255) return;
            float dx = s.X1 - s.X0, dz = s.Z1 - s.Z0;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            float step = TerrainPicture.CellMeters * 0.5f;
            int steps = Math.Max(1, (int)Math.Ceiling(length / step));
            float value = p.CoverValue(s.CoverId);
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = s.X0 + dx * t, z = s.Z0 + dz * t;
                int cx = p.CellX(x), cz = p.CellZ(z);
                int index = cz * p.Columns + cx;
                int existing = p.CoverLayer[index];
                if (existing == 0 || p.CoverValue(existing) < value) p.CoverLayer[index] = (byte)s.CoverId;
            }
        }

        // ---- stage 5: buildings, the bucket index, towns ----------------------------------------------------

        private void CollectBuildings(IBuildingSource buildings)
        {
            int n = buildings.NextChunk(_chunkX, _chunkZ);
            for (int i = 0; i < n && _buildingCount < MaxBuildings; i++)
            {
                _bx[_buildingCount] = _chunkX[i];
                _bz[_buildingCount] = _chunkZ[i];
                _buildingCount++;
            }
        }

        // Counting sort of the buildings into 150 m buckets (one unit): a town test reads the 3 x 3 buckets around a building.
        private void IndexBuildings()
        {
            Array.Clear(_bucketStart, 0, _bucketStart.Length);
            for (int i = 0; i < _buildingCount; i++)
            {
                int b = BucketOf(_bx[i], _bz[i]);
                _bucketOf[i] = b;
                _bucketStart[b + 1]++;
            }
            for (int b = 0; b < _bucketColumns * _bucketRows; b++) _bucketStart[b + 1] += _bucketStart[b];
            // _bucketStart[b] is now the first slot of bucket b; one pass per bucket fills it (the building count is small).
            for (int b = 0; b < _bucketColumns * _bucketRows; b++)
            {
                int slot = _bucketStart[b];
                for (int i = 0; i < _buildingCount; i++)
                    if (_bucketOf[i] == b) _bucketOrder[slot++] = i;
            }
        }

        private int BucketOf(float x, float z)
        {
            int bx = (int)Math.Floor(x / TownRadiusMeters);
            int bz = (int)Math.Floor((z + _picture.SizeZ) / TownRadiusMeters);
            if (bx < 0) bx = 0; else if (bx >= _bucketColumns) bx = _bucketColumns - 1;
            if (bz < 0) bz = 0; else if (bz >= _bucketRows) bz = _bucketRows - 1;
            return bz * _bucketColumns + bx;
        }

        // One chunk of BuildingChunk buildings per unit: a building is a town building when at least three buildings (itself
        // included) stand within 150 m of it; its cell is marked.
        private void MarkTowns()
        {
            TerrainPicture p = _picture;
            int end = Math.Min(_buildingCount, _index + BuildingChunk);
            float r2 = TownRadiusMeters * TownRadiusMeters;
            for (; _index < end; _index++)
            {
                int i = _index;
                int bucket = _bucketOf[i];
                int bx = bucket % _bucketColumns, bz = bucket / _bucketColumns;
                int near = 0;
                for (int dz = -1; dz <= 1 && near < TownBuildings; dz++)
                {
                    int z = bz + dz;
                    if (z < 0 || z >= _bucketRows) continue;
                    for (int dx = -1; dx <= 1 && near < TownBuildings; dx++)
                    {
                        int x = bx + dx;
                        if (x < 0 || x >= _bucketColumns) continue;
                        int b = z * _bucketColumns + x;
                        for (int s = _bucketStart[b]; s < _bucketStart[b + 1]; s++)
                        {
                            int j = _bucketOrder[s];
                            float ex = _bx[j] - _bx[i], ez = _bz[j] - _bz[i];
                            if (ex * ex + ez * ez <= r2) near++;
                        }
                    }
                }
                if (near >= TownBuildings)
                {
                    int index = p.CellZ(_bz[i]) * p.Columns + p.CellX(_bx[i]);
                    if (!p.TownLayer[index]) { p.TownLayer[index] = true; p.TownCells++; }
                }
            }
        }

        // ---- stage 7: road junctions (a road cell whose 60 m ring is crossed by at least three separate runs) ----

        private static readonly int[] RingDx = BuildRing(true);
        private static readonly int[] RingDz = BuildRing(false);

        private static int[] BuildRing(bool x)
        {
            var ring = new int[JunctionRingPoints];
            for (int k = 0; k < JunctionRingPoints; k++)
            {
                double a = k * 2.0 * Math.PI / JunctionRingPoints;
                ring[k] = (int)Math.Round(JunctionRingCells * (x ? Math.Cos(a) : Math.Sin(a)));
            }
            return ring;
        }

        private void BuildJunctionRow(int cz)
        {
            TerrainPicture p = _picture;
            for (int cx = 0; cx < p.Columns; cx++)
            {
                int index = cz * p.Columns + cx;
                if (!p.RoadLayer[index]) continue;
                int runs = 0;
                bool previous = RingRoad(cx, cz, JunctionRingPoints - 1);
                bool all = previous;
                for (int k = 0; k < JunctionRingPoints; k++)
                {
                    bool road = RingRoad(cx, cz, k);
                    if (road && !previous) runs++;
                    if (!road) all = false;
                    previous = road;
                }
                if (all) runs = 1;
                if (runs >= JunctionRuns)
                {
                    p.JunctionLayer[index] = true;
                    p.JunctionCells++;
                }
            }
        }

        private bool RingRoad(int cx, int cz, int k)
        {
            int x = cx + RingDx[k], z = cz + RingDz[k];
            return x >= 0 && z >= 0 && x < _picture.Columns && z < _picture.Rows && _picture.RoadLayer[z * _picture.Columns + x];
        }
    }
}
