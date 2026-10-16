using System;
using System.Diagnostics;
using UnityEngine;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime half of TerrainPicture (BC12 decisions 1-2). Excluded from the test csproj.
    //
    // Steps the pure TerrainPictureBuilder once per frame from BattleCoreTick.EnsureSnapshot (the first snapshot of a battle,
    // #60's deployment build, and every per-frame build after it) until the picture is Ready, inside the measured scope
    // `tactical.terrain-picture.build` (5.0 ms threshold): each step is held to BuildStepBudgetMs (4 ms) of real time by the
    // builder's deadline (real time is allowed for a CPU budget, AGENTS § Clocks) plus at most the one unit of work that ends
    // past it. The Unity reads run inside the step through three sources:
    //   heights   Terrain.activeTerrain.terrainData.GetHeights, one heightmap strip per unit (through the UnityEngine.TerrainModule
    //             reference). There is no fallback height source: with no active terrain or terrain data the runtime logs
    //             tactical-battle-core:terrain-heights-missing once and the picture never becomes Ready, so every consumer keeps
    //             its pre-BC12 behaviour (Get() returns the unready picture, TerrainPicture.Current() answers null).
    //   cover     GameObject.FindGameObjectsWithTag for StoneWall, Fence, SunkenRoad, Railroad, UnfinishedRailroad and SmallRiver
    //             (one tag per unit), then 32 tagged objects per unit; the colliders of an object are its
    //             GetComponentsInChildren<Collider>() (vanilla takes the tag on the hit collider or its parent, GetCoverCollider
    //             138471, 138479). Trenches and log walls are built during the battle and are left out.
    //   buildings FindObjectsOfType<Building>() once, then 64 positions per unit.
    // Public map data only: no unit is read.
    internal static class TerrainPictureRuntime
    {
        private const string LogId = "tactical-battle-core:terrain-picture";

        private static TerrainPictureBuilder _builder;
        private static UnityHeights _heights;
        private static readonly UnityCover Cover = new UnityCover();
        private static readonly UnityBuildings Buildings = new UnityBuildings();
        private static bool _disabled;
        private static int _lastFrame = -1;
        private static int _steps;
        private static double _buildMs;
        private static double _maxStepMs;

        static TerrainPictureRuntime()
        {
            BattleScope.Register("terrain-picture", Reset);
        }

        /// <summary>The battle's picture, ready or not (TerrainPicture.Current filters on Ready); null before the builder exists.</summary>
        internal static TerrainPicture Get()
        {
            return _builder != null ? _builder.Picture : null;
        }

        internal static void Step()
        {
            if (_disabled || (_builder != null && _builder.Picture.Ready)) return;
            int frame = UnityEngine.Time.frameCount;
            if (frame == _lastFrame) return;
            using (TelemetryPerf.Scope("tactical.terrain-picture.build", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                try
                {
                    if (_builder == null && !Begin()) return;
                    _lastFrame = frame;
                    long start = Stopwatch.GetTimestamp();
                    long deadline = start + (long)(TerrainPictureBuilder.BuildStepBudgetMs * 0.001 * Stopwatch.Frequency);
                    bool ready = _builder.Step(_heights, Cover, Buildings, deadline, Stopwatch.GetTimestamp);
                    double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                    _steps++;
                    _buildMs += ms;
                    if (ms > _maxStepMs) _maxStepMs = ms;
                    if (_builder.Failed)
                    {
                        _disabled = true;
                        if (_builder.FailureReason == "heights")
                            OnceLog.Warning("tactical-battle-core:terrain-heights-missing",
                                "[BattleTerrain] terrain heights unreadable; the terrain picture is not built and every consumer keeps its pre-BC12 behaviour");
                        else
                            OnceLog.Warning("tactical-battle-core:terrain-picture-failed",
                                "[BattleTerrain] picture build failed (" + _builder.FailureReason + "); consumers keep their pre-BC12 behaviour");
                    }
                    else if (ready)
                    {
                        TerrainPicture p = _builder.Picture;
                        OnceLog.Info(LogId, "[BattleTerrain] picture ready: cells=" + (p.Columns * p.Rows) + " crests=" + p.CrestCells
                            + " coverLines=" + p.CoverSegmentCount + " waterCells=" + p.WaterCells + " crossings=" + p.CrossingCount
                            + " towns=" + p.TownCells + " junctions=" + p.JunctionCells
                            + " buildMs=" + _buildMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                            + " steps=" + _steps + " maxStepMs=" + _maxStepMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception ex)
                {
                    _disabled = true;
                    OnceLog.Warning("tactical-battle-core:terrain-picture-failed",
                        "[BattleTerrain] picture step threw, consumers keep their pre-BC12 behaviour: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        // The builder needs BC10's grid (the map's terrain pass has run) and the active terrain's data.
        private static bool Begin()
        {
            BattleTerrainGrid grid = BattleTerrainGridRuntime.Get();
            if (grid == null) return false;
            Terrain terrain = Terrain.activeTerrain;
            TerrainData data = (UnityEngine.Object)terrain != null ? terrain.terrainData : null;
            _heights = (UnityEngine.Object)data != null ? new UnityHeights(data) : null;
            var rules = new TerrainShapeRules
            {
                MinRelief = GamePrefs.maxterrainshapeheightdifference,
                MinSteepness = GamePrefs.maxsteepnessofterrainforshape,
                PeakNearMeters = 40f,
                PeakFarMeters = 80f,
                AnchorRiverMeters = GamePrefs.distancetocheckforanchoredflanksriver,
                AnchorCrestMeters = GamePrefs.distancetocheckforanchoredflankhills,
            };
            _builder = new TerrainPictureBuilder(grid, grid.SizeX, grid.SizeZ, in rules);
            float[] values = GamePrefs.coverobjectvalues;
            if (values != null && values.Length >= TerrainPicture.CoverIdCount) _builder.CoverValues = values;
            return true;
        }

        private static void Reset()
        {
            _builder = null;
            _heights = null;
            _disabled = false;
            _lastFrame = -1;
            _steps = 0;
            _buildMs = 0.0;
            _maxStepMs = 0.0;
            Cover.Reset();
            Buildings.Reset();
        }

        // ---- heights ------------------------------------------------------------------------------------------

        private sealed class UnityHeights : IHeightSource
        {
            private readonly TerrainData _data;
            private readonly float _sizeY;

            internal UnityHeights(TerrainData data)
            {
                _data = data;
                _sizeY = data.size.y;
                Rows = Columns = data.heightmapResolution;
            }

            public int Rows { get; }
            public int Columns { get; }

            // TerrainData.GetHeights returns normalized [row, column] values; Terrain.SampleHeight scales them by size.y.
            public bool TryReadRow(int row, float[] heights)
            {
                if (row < 0 || row >= Rows || heights.Length < Columns) return false;
                float[,] strip = _data.GetHeights(0, row, Columns, 1);
                for (int c = 0; c < Columns; c++) heights[c] = strip[0, c] * _sizeY;
                return true;
            }
        }

        // ---- cover --------------------------------------------------------------------------------------------

        private sealed class UnityCover : ICoverSource
        {
            // GetCoverIDFromName (138482-138502): StoneWall 2, Fence 1, SunkenRoad 15, Railroad 4, UnfinishedRailroad 16, SmallRiver 5.
            private static readonly string[] Tags = { "StoneWall", "Fence", "SunkenRoad", "Railroad", "UnfinishedRailroad", "SmallRiver" };
            private static readonly int[] Ids = { 2, 1, 15, 4, 16, 5 };

            private int _tag;
            private GameObject[] _objects;
            private int _next;
            private bool _done;
            private bool _closestPointWarned;

            public bool Done => _done;

            internal void Reset()
            {
                _tag = 0;
                _objects = null;
                _next = 0;
                _done = false;
                _closestPointWarned = false;
            }

            public int NextChunk(CoverSegment[] into)
            {
                if (_done) return 0;
                if (_objects == null)
                {
                    // One tag search is one unit. An undefined tag throws: that tag has no objects.
                    try { _objects = GameObject.FindGameObjectsWithTag(Tags[_tag]); }
                    catch (UnityException) { _objects = new GameObject[0]; }
                    _next = 0;
                    return 0;
                }

                int n = 0;
                int end = Math.Min(_objects.Length, _next + TerrainPictureBuilder.CoverChunkObjects);
                for (; _next < end; _next++)
                {
                    GameObject go = _objects[_next];
                    if ((UnityEngine.Object)go == null) continue;
                    n = AddColliders(go, Ids[_tag], into, n);
                }
                if (_next >= _objects.Length)
                {
                    _objects = null;
                    _tag++;
                    if (_tag >= Tags.Length) _done = true;
                }
                return n;
            }

            private int AddColliders(GameObject go, int coverId, CoverSegment[] into, int n)
            {
                Collider[] colliders = go.GetComponentsInChildren<Collider>();
                for (int i = 0; i < colliders.Length && n < into.Length; i++)
                {
                    Collider collider = colliders[i];
                    if ((UnityEngine.Object)collider == null) continue;
                    Bounds b = collider.bounds;
                    Vector3 min = b.min, max = b.max;
                    // The collider's segment is the AABB diagonal whose quarter point lies closer to its surface.
                    bool firstDiagonal = true;
                    try
                    {
                        // Both diagonals share the AABB centre: compare the surface distance of the points a quarter of the way
                        // along each, which differ.
                        var a = new Vector3(min.x * 0.75f + max.x * 0.25f, b.center.y, min.z * 0.75f + max.z * 0.25f);
                        var c = new Vector3(min.x * 0.75f + max.x * 0.25f, b.center.y, max.z * 0.75f + min.z * 0.25f);
                        firstDiagonal = (collider.ClosestPoint(a) - a).sqrMagnitude <= (collider.ClosestPoint(c) - c).sqrMagnitude;
                    }
                    catch (Exception ex)
                    {
                        // ClosestPoint needs a primitive or convex collider; a concave mesh keeps the first diagonal.
                        if (!_closestPointWarned)
                        {
                            _closestPointWarned = true;
                            OnceLog.Warning("tactical-battle-core:terrain-cover-closestpoint",
                                "[BattleTerrain] Collider.ClosestPoint unavailable for a cover collider, its AABB diagonal is taken unchecked: " + ex.GetType().Name);
                        }
                    }
                    into[n].CoverId = coverId;
                    if (firstDiagonal) { into[n].X0 = min.x; into[n].Z0 = min.z; into[n].X1 = max.x; into[n].Z1 = max.z; }
                    else { into[n].X0 = min.x; into[n].Z0 = max.z; into[n].X1 = max.x; into[n].Z1 = min.z; }
                    n++;
                }
                return n;
            }
        }

        // ---- buildings ----------------------------------------------------------------------------------------

        private sealed class UnityBuildings : IBuildingSource
        {
            private Building[] _all;
            private int _next;
            private bool _loaded;

            public bool Done => _loaded && _next >= _all.Length;

            internal void Reset()
            {
                _all = null;
                _next = 0;
                _loaded = false;
            }

            public int NextChunk(float[] x, float[] z)
            {
                if (!_loaded)
                {
                    _all = UnityEngine.Object.FindObjectsOfType<Building>();
                    _loaded = true;
                    return 0;
                }
                int n = 0;
                int end = Math.Min(_all.Length, _next + TerrainPictureBuilder.BuildingChunk);
                for (; _next < end && n < x.Length; _next++)
                {
                    Building building = _all[_next];
                    if ((UnityEngine.Object)building == null) continue;
                    Vector3 p = building.transform.position;
                    x[n] = p.x;
                    z[n] = p.z;
                    n++;
                }
                return n;
            }
        }
    }
}
