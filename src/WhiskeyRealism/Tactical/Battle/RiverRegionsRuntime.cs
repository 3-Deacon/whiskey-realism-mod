using System;
using System.Diagnostics;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime half of RiverRegions (BC14 requirement-river-regions). Excluded from the test csproj.
    //
    // One RiverRegions per battle, stepped by TerrainPictureRuntime.Step once the terrain picture is Ready (the regions follow the
    // picture), once per frame, inside the measured scope `tactical.river-regions.build` (5.0 ms) and the picture's own step budget
    // (TerrainPictureBuilder.BuildStepBudgetMs, real time is allowed for a CPU budget, AGENTS § Clocks) plus at most the one unit
    // of work that ends past it. With Enable Maneuver Feasibility off nothing is built and every consumer sees no regions. Any
    // exception leaves the regions not Ready for the rest of the battle: every verdict stays Unknown.
    internal static class RiverRegionsRuntime
    {
        private static RiverRegions _regions;
        private static bool _failed;
        private static int _lastFrame = -1;
        private static double _buildMs;

        static RiverRegionsRuntime()
        {
            BattleScope.Register("river-regions", Reset);
        }

        /// <summary>The battle's regions once they are Ready, else null (every consumer then treats the map as unknown).</summary>
        internal static RiverRegions Ready() => _regions != null && _regions.Ready ? _regions : null;

        /// <summary>
        /// The build can still arrive: the feasibility is on, the build has not failed and the regions are not Ready yet. A crossing gate with no picture
        /// waits for it (CrossingPictureWait) when the terrain has a river and lets the leg through otherwise.
        /// </summary>
        internal static bool Pending() => !_failed && Plugin.ManeuverFeasibilityEnabled() && (_regions == null || !_regions.Ready);

        internal static void Step(TerrainPicture picture)
        {
            if (_failed || picture == null || !picture.Ready) return;
            if (!Plugin.ManeuverFeasibilityEnabled()) return;
            if (_regions != null && _regions.Ready) return;
            int frame = UnityEngine.Time.frameCount;
            if (frame == _lastFrame) return;
            using (TelemetryPerf.Scope("tactical.river-regions.build", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                try
                {
                    BattleTerrainGrid grid = BattleTerrainGridRuntime.Get();
                    if (grid == null) return;
                    _lastFrame = frame;
                    if (_regions == null) _regions = new RiverRegions();
                    long start = Stopwatch.GetTimestamp();
                    long deadline = start + (long)(TerrainPictureBuilder.BuildStepBudgetMs * 0.001 * Stopwatch.Frequency);
                    bool ready = _regions.Step(picture, grid, deadline, Stopwatch.GetTimestamp);
                    _buildMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                    if (_regions.Failed)
                    {
                        _failed = true;
                        OnceLog.Warning("tactical-battle-core:river-regions-failed", "[RiverRegions] the map has no size; no regions, every verdict stays unknown");
                    }
                    else if (ready)
                    {
                        OnceLog.Info("tactical-battle-core:river-regions", "[RiverRegions] regions=" + _regions.RegionCount + " links=" + _regions.LinkCount
                            + " crossings=" + _regions.CrossingTotal + " frozen=" + _regions.Frozen
                            + " buildMs=" + _buildMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception ex)
                {
                    _failed = true;
                    OnceLog.Warning("tactical-battle-core:river-regions-failed",
                        "[RiverRegions] the build threw, every verdict stays unknown: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static void Reset()
        {
            _regions = null;
            _failed = false;
            _lastFrame = -1;
            _buildMs = 0.0;
        }
    }
}
