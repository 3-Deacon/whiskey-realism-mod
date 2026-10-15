using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The planner's terrain and movement inputs from vanilla (G3; runtime, not in the test csproj).
    // Reads only.
    //   Blocks     vanilla's engagement line test exactly as CheckIfUnitIsWithinModifiedRange
    //              (80511-80517) runs it for EstablishCampaignBattle (80683-80687):
    //              bfs.CountTerrainLine(start, Tools.GetAngle(start, end) + 180f, distance,
    //              GameVars.terrainrestrictions_combat) < distance. CountTerrainLine (27695) steps one
    //              map unit at a time, each step a GetCurrentTerrainOnPos (26727) and a
    //              GetAngleTerrainPos with a Terrain.SampleHeight (26891-26935), so a probe costs
    //              `distance` steps. The terrain-spec grid does not change during a campaign, so
    //              results are memoized for the campaign (CampaignScope) on endpoints snapped to an
    //              8-unit grid (the probe runs between the snapped points), bounded to
    //              MaxMemoEntries; new probes are capped at MaxLineStepsPerReview map units per
    //              review. Past the cap a line reads as clear (the straight distance, the no-river
    //              answer) for this review and one static-key warning is logged; the memo fills on
    //              later reviews.
    //   RiverNear  BattlefieldSetup.GetCurrentTerrainOnPos at the point and 8 points on each of two
    //              rings (half and full range) for terrain 3/4/5 (small river, river, river
    //              crossing: the cells River Expedition speeds, 133482-133491); memoized the same
    //              way, at most MaxRiverProbesPerReview new points per review.
    //   ReadMarchSpeed  the campaign speed terms of the march-time estimate (MarchSpeedInputs).
    internal sealed class TerrainLineRuntime : ITerrainLineProbe
    {
        internal const float MaxLineStepsPerReview = 4000f; // map units of CountTerrainLine per review
        internal const int MaxRiverProbesPerReview = 128;
        internal const int MaxMemoEntries = 20000;
        private const float Snap = 8f;
        private const int RingPoints = 8;

        private static readonly TerrainLineRuntime Instance = new TerrainLineRuntime();
        private static readonly Dictionary<long, bool> LineMemo = new Dictionary<long, bool>();
        private static readonly Dictionary<long, bool> RiverMemo = new Dictionary<long, bool>();
        private static float _lineSteps;
        private static int _riverProbes;
        private static BattlefieldSetup _bfs;

        static TerrainLineRuntime()
        {
            CampaignScope.Register("campaign-planner-terrain-probe", Reset);
        }

        private static void Reset()
        {
            LineMemo.Clear();
            RiverMemo.Clear();
            _lineSteps = 0f;
            _riverProbes = 0;
            _bfs = null;
        }

        // The probe for one review: the memo lives for the campaign, the budgets for this review.
        internal static TerrainLineRuntime ForReview()
        {
            _lineSteps = 0f;
            _riverProbes = 0;
            return Instance;
        }

        public bool Blocks(float ax, float az, float bx, float bz)
        {
            int sax = Cell(ax), saz = Cell(az), sbx = Cell(bx), sbz = Cell(bz);
            var start = new Vector3(sax * Snap, 0f, saz * Snap);
            var end = new Vector3(sbx * Snap, 0f, sbz * Snap);
            float distance = Tools.GetXZDistance(start, end);
            if (distance < 1f) return false;
            long key = Key(sax, saz, sbx, sbz);
            if (LineMemo.TryGetValue(key, out bool known)) return known;
            if (_lineSteps + distance > MaxLineStepsPerReview)
            {
                OnceLog.Warning("campaign-planner:terrain-probe-cap", "[CampaignPlanner] river-line probe budget reached this review; further lines read as clear");
                return false;
            }
            var bfs = ResolveBattlefieldSetup();
            if ((UnityEngine.Object)bfs == null) return false;
            _lineSteps += distance;
            float angle = Tools.GetAngle(start, end) + 180f;
            bool blocked = bfs.CountTerrainLine(start, angle, distance, GameVars.terrainrestrictions_combat) < distance;
            Remember(LineMemo, key, blocked);
            return blocked;
        }

        public bool RiverNear(float x, float z, float range)
        {
            int cx = Cell(x), cz = Cell(z);
            long key = Key(cx, cz, Mathf.RoundToInt(range), int.MinValue);
            if (RiverMemo.TryGetValue(key, out bool known)) return known;
            x = cx * Snap;
            z = cz * Snap;
            if (_riverProbes >= MaxRiverProbesPerReview)
            {
                OnceLog.Warning("campaign-planner:terrain-probe-cap", "[CampaignPlanner] river-line probe budget reached this review; further lines read as clear");
                return false;
            }
            _riverProbes++;
            bool river = IsRiver(BattlefieldSetup.GetCurrentTerrainOnPos(new Vector3(x, 0f, z)));
            for (int ring = 1; ring <= 2 && !river; ring++)
            {
                float radius = range * ring * 0.5f;
                for (int k = 0; k < RingPoints && !river; k++)
                {
                    double a = 2.0 * Math.PI * k / RingPoints;
                    river = IsRiver(BattlefieldSetup.GetCurrentTerrainOnPos(new Vector3(x + radius * (float)Math.Cos(a), 0f, z + radius * (float)Math.Sin(a))));
                }
            }
            Remember(RiverMemo, key, river);
            return river;
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

        // AICampaign's own lookup (11143), as CampaignSnapshotRuntime does.
        private static BattlefieldSetup ResolveBattlefieldSetup()
        {
            if ((UnityEngine.Object)_bfs != null) return _bfs;
            var go = GameObject.Find("GameController");
            _bfs = (UnityEngine.Object)go != null ? go.GetComponent<BattlefieldSetup>() : null;
            return _bfs;
        }
    }
}
