using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC12 (docs/superpowers/plans/2026-10-15-battle-core.md § BC12): the terrain picture for planning. This file holds the
// fixtures and the build tests (decisions 1-2); the consumers' tests are in TerrainPictureTests.Planning.cs.
// World frame of the fixtures: x in [0, size], z in [-size, 0] (BattleTerrainGrid.IdAt: index z + SizeZ).
internal static partial class TerrainPictureTests
{
    public static void Run()
    {
        RunAll(Concat(BuildTests(), PlanningTests()));
    }

    private static (string, Action)[] BuildTests()
    {
        return new (string, Action)[]
        {
            ("bc12 PictureCellIdPrefersWaterAndCrossing", PictureCellIdPrefersWaterAndCrossing),
            ("bc12 CrestNeedsReliefAndSteepnessLikeCheckTerrainPeak", CrestNeedsReliefAndSteepnessLikeCheckTerrainPeak),
            ("bc12 CrestAxisPerpendicularToSlope", CrestAxisPerpendicularToSlope),
            ("bc12 CoverSegmentRasterisedAlongDiagonalWall", CoverSegmentRasterisedAlongDiagonalWall),
            ("bc12 RoadJunctionNeedsThreeRuns", RoadJunctionNeedsThreeRuns),
            ("bc12 TownNeedsThreeBuildingsWithin150m", TownNeedsThreeBuildingsWithin150m),
            ("bc12 BuilderHonoursDeadlineAcrossCoverAndCells", BuilderHonoursDeadlineAcrossCoverAndCells),
            ("bc12 MissingTerrainDataNeverReady", MissingTerrainDataNeverReady),
            ("bc12 PictureNotReadyMeansNoTerrain", PictureNotReadyMeansNoTerrain),
            ("bc12 PictureBuildAllocatesOnlyAtStart", PictureBuildAllocatesOnlyAtStart),
        };
    }

    private static (string, Action)[] Concat((string, Action)[] a, (string, Action)[] b)
    {
        var all = new (string, Action)[a.Length + b.Length];
        a.CopyTo(all, 0);
        b.CopyTo(all, a.Length);
        return all;
    }

    // ------------------------------------------------------------------ fixtures

    internal const int Grass = 0, Creek = 3, River = 4, Ford = 5, Bridge = 6, Road = 7;

    // Cover ids of vanilla's GetCoverIDFromName (138482-138502).
    internal const int CoverFence = 1, CoverWall = 2, CoverRailroad = 4, CoverSunken = 15;

    private sealed class FnHeights : IHeightSource
    {
        private readonly float _sizeX, _sizeZ;
        private readonly Func<float, float, float> _height;
        public int Rows { get; }
        public int Columns { get; }
        public bool Fail;
        public FnHeights(float sizeX, float sizeZ, Func<float, float, float> height)
        {
            _sizeX = sizeX;
            _sizeZ = sizeZ;
            _height = height;
            Rows = (int)sizeZ + 1;
            Columns = (int)sizeX + 1;
        }
        public bool TryReadRow(int row, float[] heights)
        {
            if (Fail || row < 0 || row >= Rows || heights.Length < Columns) return false;
            float z = -_sizeZ + row * _sizeZ / (Rows - 1);
            for (int c = 0; c < Columns; c++) heights[c] = _height(c * _sizeX / (Columns - 1), z);
            return true;
        }
    }

    private sealed class ListCover : ICoverSource
    {
        private readonly CoverSegment[] _segments;
        private int _next;
        public int Calls;
        public ListCover(params CoverSegment[] segments) { _segments = segments ?? new CoverSegment[0]; }
        public bool Done => _next >= _segments.Length;
        public int NextChunk(CoverSegment[] into)
        {
            Calls++;
            int n = 0;
            // One "object" per segment, CoverChunkObjects per call.
            while (n < TerrainPictureBuilder.CoverChunkObjects && _next < _segments.Length && n < into.Length) into[n++] = _segments[_next++];
            return n;
        }
    }

    private sealed class ListBuildings : IBuildingSource
    {
        private readonly float[] _x, _z;
        private int _next;
        public ListBuildings(float[] x, float[] z) { _x = x ?? new float[0]; _z = z ?? new float[0]; }
        public bool Done => _next >= _x.Length;
        public int NextChunk(float[] x, float[] z)
        {
            int n = 0;
            while (n < TerrainPictureBuilder.BuildingChunk && _next < _x.Length && n < x.Length) { x[n] = _x[_next]; z[n] = _z[_next]; _next++; n++; }
            return n;
        }
    }

    internal static float CellCenterX(int cx) => (cx + 0.5f) * TerrainPicture.CellMeters;
    internal static float CellCenterZ(float size, int cz) => -size + (cz + 0.5f) * TerrainPicture.CellMeters;

    internal static BattleTerrainGrid Grid(int size, Func<int, int, int> idAt)
    {
        return BattleMovementHonestyTests.GridOf(size, size, idAt);
    }

    internal static BattleTerrainGrid GridWithCrossings(int size, Func<int, int, int> idAt, float[] cx, float[] cz, int[] cid)
    {
        return BattleMovementHonestyTests.GridOf(size, size, idAt, false, cx, cz, cid);
    }

    // Runs the builder to Ready (or Failed) in one unbounded step; the picture is returned either way.
    internal static TerrainPicture Build(BattleTerrainGrid grid, int size, Func<float, float, float> height,
        CoverSegment[] cover = null, float[] bx = null, float[] bz = null, TerrainShapeRules? rules = null, float[] coverValues = null)
    {
        var builder = new TerrainPictureBuilder(grid, size, size, rules ?? TerrainShapeRules.Shipped);
        if (coverValues != null) builder.CoverValues = coverValues;
        var heights = new FnHeights(size, size, height);
        var covers = new ListCover(cover);
        var buildings = new ListBuildings(bx, bz);
        int guard = 0;
        while (!builder.Step(heights, covers, buildings, long.MaxValue, () => 0L))
        {
            if (builder.Failed || ++guard > 100000) break;
        }
        return builder.Picture;
    }

    internal static Func<float, float, float> Flat(float h = 0f) => (x, z) => h;

    // ------------------------------------------------------------------ decision 1: the layers

    private static void PictureCellIdPrefersWaterAndCrossing()
    {
        // Cell (5, 5) = x 100-120, z -300..-280 (size 400): a single 2 m river column inside it, the rest grass.
        const int size = 400;
        // The cell's 16 samples read grid indexes x = 102/107/112/117 (+20 per cell) and z = 2/7/12/17 above the cell's start.
        BattleTerrainGrid grid = Grid(size, (x, z) =>
        {
            int worldZ = z - size;
            bool row5 = worldZ >= -300 && worldZ < -280;
            // river: one column (x 110-114: sample x = 112) inside cell (5,5)
            if (row5 && x >= 110 && x < 115) return River;
            // cell (8,5): river all over, a bridge on the sample (172, -298)
            if (row5 && x >= 160 && x < 180) return (x >= 170 && x < 175 && worldZ < -295) ? Bridge : River;
            // cell (11,5): river all over, a ford on the sample (232, -298)
            if (row5 && x >= 220 && x < 240) return (x >= 230 && x < 235 && worldZ < -295) ? Ford : River;
            // creek: one column (x 50-54: sample x = 52) inside cell (2,5)
            if (row5 && x >= 50 && x < 55) return Creek;
            return Grass;
        });
        TerrainPicture p = Build(grid, size, Flat());
        AssertTrue(p.Ready, "the picture is ready");
        AssertEqual(River, p.Id(5, 5), "one river column (4 of 16 samples) among grass: water wins over land");
        AssertEqual(Bridge, p.Id(8, 5), "a bridge sample beats river and grass");
        AssertEqual(Ford, p.Id(11, 5), "a ford sample beats river");
        AssertEqual(Creek, p.Id(2, 5), "a creek wins over grass");
        AssertEqual(Grass, p.Id(14, 5), "plain land is the dominant id");
        AssertTrue(p.Water(5, 5) && p.Water(2, 5), "river and creek cells are water");
        AssertFalse(p.Water(8, 5) || p.Water(14, 5), "a bridge cell and grass are not water");

        // Land is the plurality of the 16 samples: 10 forest samples against 6 grass.
        BattleTerrainGrid wood = Grid(size, (x, z) => (x >= 100 && x < 117 && z - size >= -300 && z - size < -280) ? 14 : Grass);
        AssertEqual(14, Build(wood, size, Flat()).Id(5, 5), "the plurality of land samples");
    }

    // heights for a ridge running along z at world x = 410 (cell 20 of a 800 m map): h falls by 'slope' per 20 m cell either side.
    private static Func<float, float, float> RidgeAlongZ(float peak, float slopePerCell) =>
        (x, z) => peak - slopePerCell * Math.Abs(x - 410f) / 20f;

    private static Func<float, float, float> RidgeAlongX(float peak, float slopePerCell, int size) =>
        (x, z) => peak - slopePerCell * Math.Abs((z + size) - 410f) / 20f;

    private static void CrestNeedsReliefAndSteepnessLikeCheckTerrainPeak()
    {
        const int size = 800;
        BattleTerrainGrid grid = Grid(size, (x, z) => Grass);
        // 1.5 m per cell: the cell stands exactly 3 m above the cells 40 m away (CheckTerrainPeak's maxsteepness 3 m): kept.
        TerrainPicture at3 = Build(grid, size, RidgeAlongZ(20f, 1.5f));
        AssertNear(3f, at3.CrestProminence(20, 10), 0.001f, "prominence is the height over the higher neighbour 40 m away");
        AssertTrue(at3.CrestProminence(20, 10) >= TerrainShapeRules.Shipped.MinSteepness, "kept at exactly 3 m");
        AssertEqual(0f, at3.CrestProminence(15, 10), "a slope cell, not the peak, is no crest");
        // 1.4 m per cell: 2.8 m, under the 3 m steepness.
        TerrainPicture under = Build(grid, size, RidgeAlongZ(20f, 1.4f));
        AssertEqual(0f, under.CrestProminence(20, 10), "2.8 m of prominence is not a crest");
        // Relief: the same 3 m peak is rejected when the 160 m window's relief must be 10 m (6 m here).
        var rules = new TerrainShapeRules { MinRelief = 10f, MinSteepness = 3f, PeakNearMeters = 40f, PeakFarMeters = 80f, AnchorRiverMeters = 80f, AnchorCrestMeters = 50f };
        TerrainPicture relief = Build(grid, size, RidgeAlongZ(20f, 1.5f), rules: rules);
        AssertEqual(0f, relief.CrestProminence(20, 10), "relief under the rule rejects the peak");
        AssertEqual(3f, TerrainShapeRules.Shipped.MinRelief, "maxterrainshapeheightdifference 3 m");
        AssertEqual(3f, TerrainShapeRules.Shipped.MinSteepness, "maxsteepnessofterrainforshape 3 m");
        AssertEqual(80f, TerrainShapeRules.Shipped.AnchorRiverMeters, "distancetocheckforanchoredflanksriver 80 m");
        AssertEqual(50f, TerrainShapeRules.Shipped.AnchorCrestMeters, "distancetocheckforanchoredflankhills 50 m");
        // A steeper 10 m crest reads its full prominence.
        TerrainPicture steep = Build(grid, size, RidgeAlongZ(30f, 5f));
        AssertNear(10f, steep.CrestProminence(20, 10), 0.001f, "5 m per cell: 10 m over the cells 40 m away");
    }

    private static void CrestAxisPerpendicularToSlope()
    {
        const int size = 800;
        BattleTerrainGrid grid = Grid(size, (x, z) => Grass);
        // A ridge along z falls off along x: the profile axis is x (axis 0), the crest runs perpendicular, along z.
        TerrainPicture alongZ = Build(grid, size, RidgeAlongZ(30f, 3f));
        AssertEqual(0, alongZ.CrestAxis(20, 10), "a ridge along z is read on the x axis (0 degrees)");
        TerrainPicture alongX = Build(grid, size, RidgeAlongX(30f, 3f, size));
        AssertEqual(2, alongX.CrestAxis(10, 20), "a ridge along x is read on the z axis (90 degrees)");
        // A diagonal ridge: cells with x + z-index constant.
        TerrainPicture diagonal = Build(grid, size, (x, z) => 30f - 3f * Math.Abs((x + (z + size)) - 820f) / 20f / 1.4142f);
        int axis = diagonal.CrestAxis(20, 20);
        AssertTrue(axis == 1 || axis == 3, "a diagonal ridge is read on a diagonal axis, got " + axis);
        AssertEqual(-1, alongZ.CrestAxis(3, 3), "no crest, no axis");
    }

    private static void CoverSegmentRasterisedAlongDiagonalWall()
    {
        const int size = 400;
        BattleTerrainGrid grid = Grid(size, (x, z) => Grass);
        // A stone wall from (60, -340) to (260, -140): a diagonal in the lattice.
        var wall = new CoverSegment { X0 = 60f, Z0 = -340f, X1 = 260f, Z1 = -140f, CoverId = CoverWall };
        TerrainPicture p = Build(grid, size, Flat(), cover: new[] { wall });
        // The wall passes through (160, -240): cell (8, 8).
        AssertEqual(CoverWall, p.Cover(8, 8), "the wall's midpoint cell carries its cover id");
        AssertEqual(CoverWall, p.Cover(3, 3), "its start cell");
        AssertEqual(CoverWall, p.Cover(13, 13), "its end cell");
        AssertEqual(0, p.Cover(3, 13), "a cell off the diagonal has no cover");
        AssertEqual(0, p.Cover(13, 3), "the other off-diagonal cell has none either");
        // Every cell of the diagonal is covered and joined 8-connected.
        int covered = 0;
        for (int c = 3; c <= 13; c++) if (p.Cover(c, c) == CoverWall) covered++;
        AssertEqual(11, covered, "no gap along the diagonal");
        // The stronger cover keeps a shared cell.
        var fence = new CoverSegment { X0 = 100f, Z0 = -300f, X1 = 220f, Z1 = -300f, CoverId = CoverFence };
        TerrainPicture both = Build(grid, size, Flat(), cover: new[] { fence, wall });
        AssertEqual(CoverWall, both.Cover(8, 8), "where a fence and a wall share a cell the wall (0.35 over 0.10) is kept");
        AssertNear(0.35f, both.CoverValue(CoverWall), 0.0001f, "wall value");
        AssertNear(0.30f, both.CoverValue(CoverSunken), 0.0001f, "sunken road value");
        AssertNear(0.20f, both.CoverValue(CoverRailroad), 0.0001f, "embankment value");
        AssertNear(0.10f, both.CoverValue(CoverFence), 0.0001f, "fence value");
        AssertNear(0.08f, both.CoverValue(5), 0.0001f, "creek value");
        AssertEqual(0f, both.CoverValue(0), "no cover");
        // The builder takes the vanilla table when the runtime has it.
        var table = new float[17];
        table[CoverWall] = 0.5f;
        TerrainPicture custom = Build(grid, size, Flat(), cover: new[] { wall }, coverValues: table);
        AssertNear(0.5f, custom.CoverValue(CoverWall), 0.0001f, "GamePrefs.coverobjectvalues replaces the defaults");
    }

    // Paints a 6 m wide road along a line of the 1 m grid.
    internal static bool OnRoad(int x, int worldZ, int cx, int cz, bool alongX, bool alongZ, int half)
    {
        if (alongX && Math.Abs(worldZ - cz) <= 3 && Math.Abs(x - cx) <= half) return true;
        if (alongZ && Math.Abs(x - cx) <= 3 && Math.Abs(worldZ - cz) <= half) return true;
        return false;
    }

    private static void RoadJunctionNeedsThreeRuns()
    {
        const int size = 600;
        // A crossroads at (300, -300) (four arms), a straight road at the east (x 520-600 along... see below), a T at (100, -100).
        BattleTerrainGrid grid = Grid(size, (x, z) =>
        {
            int wz = z - size;
            if (OnRoad(x, wz, 300, -300, true, true, 200)) return Road;       // + : four arms
            if (OnRoad(x, wz, 100, -500, true, false, 90)) return Road;        // straight road along x at z -500
            if (OnRoad(x, wz, 480, -100, true, false, 90) || (Math.Abs(x - 480) <= 3 && wz >= -100 && wz <= 0)) return Road;   // T: stem up to z 0
            return Grass;
        });
        TerrainPicture p = Build(grid, size, Flat());
        AssertTrue(p.RoadJunctionNear(300f, -300f, 40f), "four arms meeting: a junction");
        AssertFalse(p.RoadJunctionNear(100f, -500f, 40f), "a straight road has two runs on the ring: no junction");
        AssertTrue(p.RoadJunctionNear(480f, -100f, 40f), "a T has three runs on the ring: a junction");
        AssertFalse(p.RoadJunctionNear(500f, -450f, 40f), "open ground is no junction");
        AssertTrue(p.RoadJunctionNear(330f, -330f, 80f), "the radius reaches a junction cell nearby");
    }

    private static void TownNeedsThreeBuildingsWithin150m()
    {
        const int size = 800;
        BattleTerrainGrid grid = Grid(size, (x, z) => Grass);
        // A village of three buildings inside 100 m at (200, -200); two buildings only at (600, -200);
        // three buildings spread over 400 m at (200, -600).
        var bx = new[] { 200f, 240f, 220f, 600f, 640f, 100f, 300f, 500f };
        var bz = new[] { -200f, -210f, -260f, -200f, -230f, -600f, -600f, -600f };
        TerrainPicture p = Build(grid, size, Flat(), bx: bx, bz: bz);
        AssertTrue(p.TownNear(220f, -220f, 60f), "three buildings within 150 m make a town");
        AssertFalse(p.TownNear(620f, -215f, 60f), "two buildings are a farm, not a town");
        AssertFalse(p.TownNear(300f, -600f, 60f), "three buildings 200 m apart are no town");
        AssertTrue(p.TownNear(300f, -300f, 200f), "the radius reaches the town");
    }

    // ------------------------------------------------------------------ decision 2: the stepped build

    private static void BuilderHonoursDeadlineAcrossCoverAndCells()
    {
        const int size = 200;      // 10 x 10 cells
        BattleTerrainGrid grid = Grid(size, (x, z) => Grass);
        var segments = new CoverSegment[70];      // two chunks of 32 and one of 6: three cover units
        for (int i = 0; i < segments.Length; i++)
            segments[i] = new CoverSegment { X0 = 10f, Z0 = -190f + i, X1 = 150f, Z1 = -190f + i, CoverId = CoverFence };
        var builder = new TerrainPictureBuilder(grid, size, size, TerrainShapeRules.Shipped);
        var heights = new FnHeights(size, size, Flat());
        var covers = new ListCover(segments);
        var buildings = new ListBuildings(new float[] { 20f, 40f, 60f }, new float[] { -20f, -40f, -60f });

        long t = 0;
        Func<long> clock = () => ++t;
        // Every step may do exactly three units of work: the clock ticks once per unit and the deadline is three ticks away.
        int steps = 0;
        int coverCallsAfterFirstStep = -1;
        int previousTotal = 0;
        while (true)
        {
            steps++;
            bool ready = builder.Step(heights, covers, buildings, t + 3, clock);
            if (steps == 1) coverCallsAfterFirstStep = covers.Calls;
            int done = builder.TotalUnits - previousTotal;
            previousTotal = builder.TotalUnits;
            AssertEqual(done, builder.UnitsLastStep, "UnitsLastStep is the step's work");
            if (!ready) AssertEqual(3, done, "step " + steps + " stops after the first unit that ends past the deadline: three units");
            else AssertTrue(done >= 1 && done <= 3, "the last step finishes the remaining units");
            if (ready) break;
            AssertTrue(steps < 500, "the build terminates");
        }
        AssertTrue(builder.Picture.Ready, "ready");
        AssertEqual(3, coverCallsAfterFirstStep, "the first step spent its three units in the cover stage (one chunk each)");
        AssertTrue(steps >= 5, "the build spans several steps: " + steps);
        AssertEqual(10, covers.Calls > 0 ? 10 : 0, "cover chunks were read");

        // A deadline already in the past still does the first unit of work (progress is guaranteed), and only that unit.
        var slow = new TerrainPictureBuilder(grid, size, size, TerrainShapeRules.Shipped);
        long t2 = 1000;
        slow.Step(heights, new ListCover(segments), buildings, 0L, () => ++t2);
        AssertEqual(1, slow.UnitsLastStep, "an expired deadline stops after exactly one unit");
        // The measured budget the runtime uses.
        AssertEqual(4f, TerrainPictureBuilder.BuildStepBudgetMs, "BuildStepBudgetMs");
        AssertEqual(32, TerrainPictureBuilder.CoverChunkObjects, "CoverChunkObjects");
        AssertEqual(64, TerrainPictureBuilder.BuildingChunk, "BuildingChunk");
    }

    private static void MissingTerrainDataNeverReady()
    {
        const int size = 200;
        BattleTerrainGrid grid = Grid(size, (x, z) => Grass);
        var builder = new TerrainPictureBuilder(grid, size, size, TerrainShapeRules.Shipped);
        var heights = new FnHeights(size, size, Flat()) { Fail = true };
        for (int i = 0; i < 50; i++)
            AssertFalse(builder.Step(heights, new ListCover(), new ListBuildings(null, null), long.MaxValue, () => 0L), "never ready without heights");
        AssertTrue(builder.Failed, "the failure is latched");
        AssertFalse(builder.Picture.Ready, "the picture stays unready");
        AssertEqual("heights", builder.FailureReason, "the reason is the missing heights");

        var none = new TerrainPictureBuilder(grid, size, size, TerrainShapeRules.Shipped);
        AssertFalse(none.Step(null, new ListCover(), new ListBuildings(null, null), long.MaxValue, () => 0L), "no height source at all");
        AssertTrue(none.Failed && !none.Picture.Ready, "no source: failed, unready");
        var noGrid = new TerrainPictureBuilder(null, size, size, TerrainShapeRules.Shipped);
        AssertFalse(noGrid.Step(new FnHeights(size, size, Flat()), null, null, long.MaxValue, () => 0L), "no terrain grid");
        AssertTrue(noGrid.Failed, "no grid: failed");
    }

    private static void PictureNotReadyMeansNoTerrain()
    {
        const int size = 200;
        var builder = new TerrainPictureBuilder(Grid(size, (x, z) => Grass), size, size, TerrainShapeRules.Shipped);
        TerrainPicture raw = builder.Picture;
        AssertFalse(raw.Ready, "a fresh picture is not ready");
        AssertEqual(0f, raw.TerrainStrength(100f, -100f, 0f, 1f), "unready: no terrain strength");
        AssertEqual(0f, raw.ApproachDifficulty(100f, -100f, 0f, 1f), "unready: no approach difficulty");
        AssertFalse(raw.TownNear(100f, -100f, 500f) || raw.RoadJunctionNear(100f, -100f, 500f), "unready: no town or junction");
        AssertFalse(raw.TryNearestCrossing(100f, -100f, 500f, out _, out _, out _), "unready: no crossing");
        AssertEqual(0f, raw.CrestProminence(1, 1), "unready: no crest");

        Func<TerrainPicture> saved = TerrainPicture.Provider;
        try
        {
            TerrainPicture.Provider = () => raw;
            AssertTrue(TerrainPicture.Current() == null, "Current() is null until the picture is ready: every consumer keeps today's behaviour");
            TerrainPicture ready = Build(Grid(size, (x, z) => Grass), size, Flat());
            TerrainPicture.Provider = () => ready;
            AssertTrue(ReferenceEquals(ready, TerrainPicture.Current()), "a ready picture is current");
            TerrainPicture.PlanningEnabled = () => false;
            AssertTrue(TerrainPicture.CurrentForPlanning() == null, "the planning flag off hides the picture from the planners");
            AssertTrue(ReferenceEquals(ready, TerrainPicture.Current()), "...but not from the playbook terrain");
            TerrainPicture.PlanningEnabled = () => true;
            AssertTrue(ReferenceEquals(ready, TerrainPicture.CurrentForPlanning()), "the planning flag on");
        }
        finally
        {
            TerrainPicture.Provider = saved;
            TerrainPicture.PlanningEnabled = null;
        }
    }

    private static void PictureBuildAllocatesOnlyAtStart()
    {
        const int size = 400;
        BattleTerrainGrid grid = Grid(size, (x, z) => (x > 100 && x < 110) ? River : Grass);
        var segments = new CoverSegment[40];
        for (int i = 0; i < segments.Length; i++)
            segments[i] = new CoverSegment { X0 = 10f, Z0 = -390f + i * 5f, X1 = 300f, Z1 = -390f + i * 5f, CoverId = CoverWall };
        var bx = new float[200];
        var bz = new float[200];
        for (int i = 0; i < bx.Length; i++) { bx[i] = 20f + (i % 20) * 10f; bz[i] = -20f - (i / 20) * 10f; }
        var builder = new TerrainPictureBuilder(grid, size, size, TerrainShapeRules.Shipped);
        var heights = new FnHeights(size, size, RidgeAlongZ(20f, 2f));
        var covers = new ListCover(segments);
        var buildings = new ListBuildings(bx, bz);
        long t = 0;
        Func<long> clock = () => ++t;

        // The first step allocates the lattice layers.
        builder.Step(heights, covers, buildings, t + 2, clock);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int steps = 0;
        bool ready = false;
        while (!ready && steps < 2000)
        {
            ready = builder.Step(heights, covers, buildings, t + 6, clock);
            steps++;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertTrue(ready, "the build finished");
        AssertEqual(0L, bytes, "every step after the first allocates nothing (" + bytes + " bytes over " + steps + " steps)");
    }
}
