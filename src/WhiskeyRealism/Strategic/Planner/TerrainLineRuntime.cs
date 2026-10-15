using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The planner's terrain and movement inputs from vanilla (G3; runtime, not in the test csproj).
    // Reads only. The terrain source of ReviewTerrain (ITerrainLineSource): TryLine/TryRiver answer
    // from the campaign memo for free; ProbeLine/ProbeRiver run vanilla's lookups within this
    // review's budget and return false, leaving the answer unknown, once it is spent.
    //   Lines   vanilla's engagement line test exactly as CheckIfUnitIsWithinModifiedRange
    //           (80511-80517) runs it for EstablishCampaignBattle (80683-80687):
    //           bfs.CountTerrainLine(start, Tools.GetAngle(start, end) + 180f, distance,
    //           GameVars.terrainrestrictions_combat) < distance. CountTerrainLine (27695) steps one map
    //           unit at a time, each step a GetCurrentTerrainOnPos (26727) and a GetAngleTerrainPos
    //           with a Terrain.SampleHeight (26891-26935), so a probe costs `distance` steps.
    //   Rivers  BattlefieldSetup.GetCurrentTerrainOnPos at the point and 8 points on each of two
    //           rings (half and full range) for terrain 3/4/5 (small river, river, river crossing:
    //           the cells River Expedition speeds, 133482-133491).
    // The terrain-spec grid of a campaign map is static, so answers are memoized for the map, on
    // endpoints snapped to a 16-unit grid (the probe runs between the snapped points), bounded to
    // MaxMemoEntries. The memo, the drain and the partial lines survive CampaignScope resets (a
    // sidecar load, a return from a battle) and are cleared only when the map's identity changes
    // (TerrainMemoDrain.MapIdentity: scenario and area geometry; PrepareDrain).
    // Per review or frame: at most MaxLineStepsPerReview map units of lines, MaxRiverProbesPerReview
    // river points, and MaxProbeMillisPerReview (a drain frame: MaxDrainMillisPerFrame) of wall clock
    // spent probing (a CPU budget, AGENTS.md
    // § Clocks). A long line is walked in segments: each call walks what the budgets leave (the
    // measured cost per step times the steps stays within the CPU budget; at least MinSegmentSteps
    // on the first probe of a budget so the memo always progresses) and keeps its offset in
    // Partials; the line is answered (memoized) when a segment meets a blocking cell or the last
    // segment ends. A segment starts at the point vanilla's own walk reaches after the steps
    // already walked (AlongLine), so the union of the segments samples the
    // cells the single CountTerrainLine call would. A refused or unfinished probe changes no answer
    // the planner uses: ReviewTerrain scores a review with no river term until every question is
    // known (G3 verify F3).
    //   Drain   the whole static question set (TerrainMemoDrain: every area-centre pair within
    //           ReviewTerrain.MaxProbedLine, every area centre at every own command range) is
    //           prepared from each review's snapshot and drained by Plugin.Update a frame at a
    //           time within the same budgets (DrainTick, segmenting long lines so no frame passes
    //           the CPU budget), only while GameController's BattleUnits
    //           is the campaign map's (IsCampaign, 78782); a battle map's terrain is never probed.
    //           The review keeps its own budget as a fallback. When fix/perf-hot-paths' frame-budget
    //           daily-review scheduler merges, the drain becomes a job of that scheduler.
    //   ReadMarchSpeed  the campaign speed terms of the march-time estimate (MarchSpeedInputs).
    internal sealed class TerrainLineRuntime : ITerrainLineSource
    {
        internal const float MaxLineStepsPerReview = 1500f; // map units of CountTerrainLine per review
        internal const int MaxRiverProbesPerReview = 128;
        internal const double MaxProbeMillisPerReview = 1.0;
        // A drain frame probes for at most 0.9 ms, so the frame stays under the 1 ms target with the
        // drain's own bookkeeping and the error of the measured cost per step.
        internal const double MaxDrainMillisPerFrame = 0.9;
        internal const int MaxMemoEntries = 200000; // also the most questions the drain takes
        internal const float MinSegmentSteps = 32f;
        private const int MaxPartials = 256;
        private const float Snap = 16f;
        private const int RingPoints = 8;

        private static readonly TerrainLineRuntime Instance = new TerrainLineRuntime();
        private static readonly Dictionary<long, bool> LineMemo = new Dictionary<long, bool>();
        private static readonly Dictionary<long, bool> RiverMemo = new Dictionary<long, bool>();
        private static readonly Dictionary<long, float> Partials = new Dictionary<long, float>(); // line key -> steps walked
        private static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
        private static float _lineSteps;
        private static double _millisPerStep = 0.003; // learned from the probes this session (starts at 3 us)
        private static double _probeMillis;            // time spent probing this review or frame
        private static double _budgetMillis = MaxProbeMillisPerReview;
        private static int _riverProbes;
        private static BattlefieldSetup _bfs;
        private static BattleUnits _bunits;
        private static readonly TerrainMemoDrain Drain = new TerrainMemoDrain();
        private static int _drainFrames;
        private static int _findAfterFrame;
        private const int FindRetryFrames = 60; // no GameController (menus): look again a second later

        static TerrainLineRuntime()
        {
            CampaignScope.Register("campaign-planner-terrain-probe", Reset);
        }

        // A CampaignScope reset drops the scene references and the budgets only; the memo belongs to
        // the map (ClearMap).
        private static void Reset()
        {
            NewBudget(MaxProbeMillisPerReview);
            _bfs = null;
            _bunits = null;
            _findAfterFrame = 0;
        }

        private static void ClearMap()
        {
            LineMemo.Clear();
            RiverMemo.Clear();
            Partials.Clear();
            _drainFrames = 0;
        }

        // The source for one review: the memo lives for the campaign, the budgets for this review.
        internal static TerrainLineRuntime ForReview()
        {
            NewBudget(MaxProbeMillisPerReview);
            return Instance;
        }

        private static void NewBudget(double millis)
        {
            _budgetMillis = millis;
            _lineSteps = 0f;
            _riverProbes = 0;
            _probeMillis = 0.0;
        }

        // Called by each review after its snapshot: queues the map's question set once and the river
        // points of any new command range.
        internal static void PrepareDrain(CampaignSnapshot snapshot)
        {
            try
            {
                if (snapshot == null) return;
                long map = TerrainMemoDrain.MapIdentity(SceneManagement.currentcampaignscenario, snapshot.Areas);
                bool grew = Drain.Prepare(map, snapshot.Areas, snapshot.Own, MaxMemoEntries, out bool mapChanged);
                if (mapChanged) ClearMap();
                if (grew)
                    OnceLog.Info("campaign-planner:terrain-drain-start", "[CampaignPlanner] terrain drain started: lines=" + Drain.Lines
                        + " rivers=" + Drain.Rivers + " remaining=" + Drain.Remaining);
                if (Drain.Oversize)
                    OnceLog.Warning("campaign-planner:terrain-drain-oversize", "[CampaignPlanner] terrain question set exceeds " + MaxMemoEntries
                        + " entries; not drained, river terms only as reviews fill the memo");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:terrain-drain-failed", "[CampaignPlanner] terrain drain failed: " + ex.Message);
            }
        }

        // Plugin.Update, once a frame: drains the queue within one frame's budget on the campaign map.
        internal static void DrainTick()
        {
            if (Drain.Remaining == 0) return;
            using (TelemetryPerf.Scope("campaign.planner.terrain-drain", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    var flag = Plugin.EnableCampaignPlanner;
                    if (flag == null || !flag.Value) return;
                    if ((UnityEngine.Object)ResolveBattlefieldSetup() == null) return;
                    NewBudget(MaxDrainMillisPerFrame);
                    _drainFrames++;
                    Drain.Drain(Instance);
                    if (Drain.Remaining == 0)
                        OnceLog.Info("campaign-planner:terrain-drain-complete", "[CampaignPlanner] terrain drain complete: lines=" + Drain.Lines
                            + " rivers=" + Drain.Rivers + " frames=" + _drainFrames);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:terrain-drain-failed", "[CampaignPlanner] terrain drain failed: " + ex.Message);
                }
            }
        }

        public bool TryLine(float ax, float az, float bx, float bz, out bool blocked)
        {
            blocked = false;
            if (LineDistance(ax, az, bx, bz, out _, out _) < 1f) return true;
            return LineMemo.TryGetValue(LineKey(ax, az, bx, bz), out blocked);
        }

        public bool TryRiver(float x, float z, float range, out bool river)
        {
            return RiverMemo.TryGetValue(RiverKey(x, z, range), out river);
        }

        // True when the line is answered (memoized); false when refused or walked only partly (the
        // next call continues from its offset).
        public bool ProbeLine(float ax, float az, float bx, float bz)
        {
            float distance = LineDistance(ax, az, bx, bz, out Vector3 start, out Vector3 end);
            if (distance < 1f) return true;
            if (OverTime()) return Refused();
            long key = LineKey(ax, az, bx, bz);
            Partials.TryGetValue(key, out float walked);
            float steps = LineSegments.Steps(distance - walked, _lineSteps, MaxLineStepsPerReview, _probeMillis, _budgetMillis,
                _millisPerStep, !ProbedThisReview(), MinSegmentSteps);
            if (steps <= 0f) return Refused();
            var bfs = ResolveBattlefieldSetup();
            if ((UnityEngine.Object)bfs == null) return false;
            _lineSteps += steps;
            Clock.Restart();
            float angle = Tools.GetAngle(start, end) + 180f;
            Vector3 from = start;
            if (walked > 0f)
            {
                LineSegments.AlongLine(start.x, start.z, angle, walked, out float px, out float pz);
                from = new Vector3(px, 0f, pz);
            }
            bool blocked = bfs.CountTerrainLine(from, angle, steps, GameVars.terrainrestrictions_combat) < steps;
            double spent = Clock.Elapsed.TotalMilliseconds;
            _probeMillis += spent;
            _millisPerStep = 0.8 * _millisPerStep + 0.2 * (spent / steps);
            if (!blocked && walked + steps < distance)
            {
                if (Partials.Count >= MaxPartials) Partials.Clear();
                Partials[key] = walked + steps;
                return false;
            }
            Partials.Remove(key);
            Remember(LineMemo, key, blocked);
            return true;
        }

        public bool ProbeRiver(float x, float z, float range)
        {
            if (_riverProbes >= MaxRiverProbesPerReview || OverTime()) return Refused();
            _riverProbes++;
            Clock.Restart();
            float px = Cell(x) * Snap, pz = Cell(z) * Snap;
            bool river = IsRiver(BattlefieldSetup.GetCurrentTerrainOnPos(new Vector3(px, 0f, pz)));
            for (int ring = 1; ring <= 2 && !river; ring++)
            {
                float radius = range * ring * 0.5f;
                for (int k = 0; k < RingPoints && !river; k++)
                {
                    double a = 2.0 * Math.PI * k / RingPoints;
                    river = IsRiver(BattlefieldSetup.GetCurrentTerrainOnPos(new Vector3(px + radius * (float)Math.Cos(a), 0f, pz + radius * (float)Math.Sin(a))));
                }
            }
            _probeMillis += Clock.Elapsed.TotalMilliseconds;
            Remember(RiverMemo, RiverKey(x, z, range), river);
            return true;
        }

        // The first probe of a budget always walks at least MinSegmentSteps, so the memo makes
        // progress every review and frame whatever a step costs.
        private static bool ProbedThisReview()
        {
            return _lineSteps > 0f || _riverProbes > 0;
        }

        private static bool OverTime()
        {
            return _probeMillis > _budgetMillis;
        }

        // Over budget: nothing is probed or remembered; the question stays unknown.
        private static bool Refused()
        {
            OnceLog.Warning("campaign-planner:terrain-probe-cap", "[CampaignPlanner] river-line probe budget reached this review; the memo fills on later reviews");
            return false;
        }

        private static float LineDistance(float ax, float az, float bx, float bz, out Vector3 start, out Vector3 end)
        {
            start = new Vector3(Cell(ax) * Snap, 0f, Cell(az) * Snap);
            end = new Vector3(Cell(bx) * Snap, 0f, Cell(bz) * Snap);
            return Tools.GetXZDistance(start, end);
        }

        private static long LineKey(float ax, float az, float bx, float bz)
        {
            return Key(Cell(ax), Cell(az), Cell(bx), Cell(bz));
        }

        private static long RiverKey(float x, float z, float range)
        {
            return Key(Cell(x), Cell(z), Mathf.RoundToInt(range), int.MinValue);
        }

        private static int Cell(float v)
        {
            return Mathf.RoundToInt(v / Snap);
        }

        private static void Remember(Dictionary<long, bool> memo, long key, bool value)
        {
            if (memo.Count >= MaxMemoEntries) memo.Clear();
            memo[key] = value;
        }

        private static bool IsRiver(int terrain)
        {
            return terrain == 3 || terrain == 4 || terrain == 5;
        }

        // Vanilla campaign speed terms (MarchTime's header names each anchor); alliance-level terms
        // only for alliances 0 and 1, bound-checked.
        internal static MarchSpeedInputs ReadMarchSpeed(int alliance)
        {
            var m = new MarchSpeedInputs();
            var basic = GamePrefs.unitbasicspeed;
            if (basic != null && basic.GetLength(1) > 1)
            {
                m.MountedBaseSpeed = new float[basic.GetLength(0)];
                for (int t = 0; t < m.MountedBaseSpeed.Length; t++) m.MountedBaseSpeed[t] = basic[t, 1];
            }
            m.BasicUnitSpeed = GamePrefs.basicunitspeed;
            m.CampaignSpeedFactor = GamePrefs.generalcampaignunitspeedfactor;
            var cost = GamePrefs.terrainspeccost;
            if (cost != null && cost.Length > 9)
            {
                m.WalkCost = cost[0];
                m.RiverCost = cost[4];
                m.RoadCost = cost[7];
                m.RailCost = cost[9];
            }
            var perMan = GamePrefs.usedtransportcapacitypermenstrength;
            m.TrainCapacityPerMan = perMan != null && perMan.Length > 0 ? perMan[0] : 0f;
            var alliances = GameVars.alliance;
            if (alliances != null && alliance >= 0 && alliance <= 1 && alliance < alliances.Length && alliances[alliance] != null)
            {
                var a = alliances[alliance];
                m.RailroadSpeedAdj = a.railroadspeedadj;
                if (a.transportcapacity != null && a.transportcapacity.Length > 0 && a.transportcapacityused != null && a.transportcapacityused.Length > 0)
                    m.TrainCapacityFree = a.transportcapacity[0] - a.transportcapacityused[0];
            }
            return m;
        }

        private static long Key(int a, int b, int c, int d)
        {
            unchecked
            {
                long h = 17;
                h = h * 1000003L + a;
                h = h * 1000003L + b;
                h = h * 1000003L + c;
                h = h * 1000003L + d;
                return h;
            }
        }

        // AICampaign's own lookup (11143-11144), as CampaignSnapshotRuntime does; null unless the
        // GameController is the campaign map's (BattleUnits.IsCampaign, 78782), so a battle map's
        // terrain is never probed or memoised. Found again only when the scene's objects are gone.
        private static BattlefieldSetup ResolveBattlefieldSetup()
        {
            if ((UnityEngine.Object)_bunits == null)
            {
                if (Time.frameCount < _findAfterFrame) return null;
                _findAfterFrame = Time.frameCount + FindRetryFrames;
                var go = GameObject.Find("GameController");
                _bunits = (UnityEngine.Object)go != null ? go.GetComponent<BattleUnits>() : null;
                _bfs = (UnityEngine.Object)go != null ? go.GetComponent<BattlefieldSetup>() : null;
            }
            if ((UnityEngine.Object)_bunits == null || !_bunits.IsCampaign) return null;
            return _bfs;
        }
    }
}
