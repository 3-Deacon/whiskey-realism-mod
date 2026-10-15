using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The campaign terrain questions the planner asks (G3, G0 landing B), as the planner's code
    // sees them during one review. ReviewTerrain answers them for TheaterPlanner.Plan; a fixture may
    // answer them directly in the harness.
    public interface ITerrainLineProbe
    {
        // Vanilla's engagement line test (CheckIfUnitIsWithinModifiedRange 80502-80517, called by
        // EstablishCampaignBattle 80683-80687): true when BattlefieldSetup.CountTerrainLine from a
        // toward b over terrainrestrictions_combat {1,3,4,5,6,8} (64535) stops short of the
        // distance, i.e. a river, sea or impassable cell lies on the line.
        bool Blocks(float ax, float az, float bx, float bz);

        // A river cell (terrain 3 small river, 4 river, 5 river crossing: the cells River Expedition
        // speeds, 133482-133491) within range of the point (BattlefieldSetup.GetCurrentTerrainOnPos,
        // 26727). The march-time river corridor.
        bool RiverNear(float x, float z, float range);
    }

    // Where the answers come from (TerrainLineRuntime in the game): a memo lookup is free; a probe
    // spends the review's budget and returns false, leaving the answer unknown, once it is spent.
    public interface ITerrainLineSource
    {
        bool TryLine(float ax, float az, float bx, float bz, out bool blocked);
        bool TryRiver(float x, float z, float range, out bool river);
        bool ProbeLine(float ax, float az, float bx, float bz);
        bool ProbeRiver(float x, float z, float range);
    }

    // One review's terrain view (G3 verify F3: river discounts are order-independent). A first pass
    // of the plan asks every question through it: a known answer comes from the source's memo, an
    // unknown one is recorded and read as clear (straight distance, no river corridor). When any
    // answer was unknown the plan does not use the river terms at all for this review: TheaterPlanner
    // re-scores with no probe, so no candidate gets a discount another candidate lacks. The unknown
    // questions are then probed within the budget in a fixed order (group: capital, candidates,
    // sectors; then ascending objective or sector id; then the target position; then the order of
    // the question inside it, which follows the snapshot's belief order).
    // Every question is asked between AI area centres (AreaRecord averageposition, the closest area
    // of each point by vanilla's GetClosestAreaDistance, 14835): a line joins the two points' area
    // centres (none, i.e. clear, inside one area), a river point is its area's centre. Exact
    // endpoints re-key every day an army moves, so a per-review budget could never complete the
    // memo; area centres are static (11259-11286), so the question set of a campaign is finite
    // (area pairs within MaxProbedLine, areas x command ranges) and the memo completes once and
    // stays complete; the terrain grid is static.
    internal sealed class ReviewTerrain : ITerrainLineProbe
    {
        internal const int GroupCapital = 0, GroupCandidate = 1, GroupSector = 2;
        // Lines longer than this are never probed and read as clear, in every review alike: a probe
        // costs one CountTerrainLine step per map unit, and a line longer than a review's step budget
        // could never be probed, so the memo would never complete. 600 is a little over twice the
        // relocation range (275, campaignprefs 1392-1393): every threat line of the plan is shorter;
        // only long marches lose the river detour.
        internal const float MaxProbedLine = 600f;

        private struct Question
        {
            public bool IsLine;
            public int Group, Id, Seq;
            public float OrderX, OrderZ;
            public float AX, AZ, BX, BZ, Range;
        }

        private readonly ITerrainLineSource _source;
        private readonly IReadOnlyList<AreaRecord> _areas;
        private readonly Dictionary<long, int> _areaOf = new Dictionary<long, int>();
        private readonly List<Question> _unknown = new List<Question>();
        private readonly HashSet<(int, int, float)> _asked = new HashSet<(int, int, float)>(); // an unknown is recorded once
        private int _group, _id, _seq;
        private float _orderX, _orderZ;

        internal ReviewTerrain(ITerrainLineSource source, IReadOnlyList<AreaRecord> areas)
        {
            _source = source;
            _areas = areas;
        }

        internal int Unknown => _unknown.Count;

        // The plan item asking the next questions (TheaterPlanner calls it before each group of
        // questions).
        internal void Mark(int group, int id, float x, float z)
        {
            _group = group;
            _id = id;
            _orderX = x;
            _orderZ = z;
        }

        public bool Blocks(float ax, float az, float bx, float bz)
        {
            int a = AreaOf(ax, az), b = AreaOf(bx, bz);
            if (_source == null || a < 0 || b < 0 || a == b) return false;
            if (a > b) { int t = a; a = b; b = t; } // one question per area pair
            ax = _areas[a].X; az = _areas[a].Z; bx = _areas[b].X; bz = _areas[b].Z;
            if (RiverLines.Distance(ax, az, bx, bz) > MaxProbedLine) return false;
            if (_source.TryLine(ax, az, bx, bz, out bool blocked)) return blocked;
            if (!_asked.Add((a, b, -1f))) return false;
            _unknown.Add(new Question { IsLine = true, Group = _group, Id = _id, Seq = _seq++, OrderX = _orderX, OrderZ = _orderZ, AX = ax, AZ = az, BX = bx, BZ = bz });
            return false;
        }

        public bool RiverNear(float x, float z, float range)
        {
            int a = AreaOf(x, z);
            if (_source == null || a < 0) return false;
            x = _areas[a].X;
            z = _areas[a].Z;
            if (_source.TryRiver(x, z, range, out bool river)) return river;
            if (!_asked.Add((a, -1, range))) return false;
            _unknown.Add(new Question { IsLine = false, Group = _group, Id = _id, Seq = _seq++, OrderX = _orderX, OrderZ = _orderZ, AX = x, AZ = z, Range = range });
            return false;
        }

        // The list position of the point's closest area (AreaGeometry.AreaAt), cached for the review.
        private int AreaOf(float x, float z)
        {
            if (_areas == null || _areas.Count == 0) return -1;
            long key = ((long)(int)Math.Round(x) << 32) ^ (uint)(int)Math.Round(z);
            if (_areaOf.TryGetValue(key, out int area)) return area;
            area = AreaGeometry.AreaAt(_areas, x, z);
            _areaOf[key] = area;
            return area;
        }

        // Probes the unknown questions in the fixed order until the source's budget refuses one;
        // returns how many were probed.
        internal int ProbeUnknown()
        {
            if (_source == null) return 0;
            var order = new List<Question>(_unknown);
            order.Sort(Compare);
            int probed = 0;
            for (int i = 0; i < order.Count; i++)
            {
                var q = order[i];
                bool done = q.IsLine ? _source.ProbeLine(q.AX, q.AZ, q.BX, q.BZ) : _source.ProbeRiver(q.AX, q.AZ, q.Range);
                if (!done) break;
                probed++;
            }
            return probed;
        }

        private static int Compare(Question a, Question b)
        {
            int c = a.Group.CompareTo(b.Group);
            if (c == 0) c = a.Id.CompareTo(b.Id);
            if (c == 0) c = a.OrderX.CompareTo(b.OrderX);
            if (c == 0) c = a.OrderZ.CompareTo(b.OrderZ);
            if (c == 0) c = a.Seq.CompareTo(b.Seq);
            return c;
        }
    }

    // A river delays a threat, it never removes one. Vanilla starts a campaign battle only when
    // the engagement line is clear (80683-80687), so an army behind a river line must cross at a
    // ford or bridge (BattlefieldSetup.rivercrossingpositions, 26628-26638) to fight.
    public static class RiverLines
    {
        // Straight XZ distance when the line is clear; when it is blocked, the shortest detour
        // a -> crossing -> b over the snapshot's crossings (never shorter than the straight line);
        // with no crossing the straight distance stays. The detour legs are not re-probed: the
        // crossing cells (terrain 5/6) are themselves in terrainrestrictions_combat, so a leg that
        // ends on one would always read as blocked.
        public static float EffectiveDistance(float ax, float az, float bx, float bz, ITerrainLineProbe probe,
                                              IReadOnlyList<CrossingRecord> crossings)
        {
            float straight = Distance(ax, az, bx, bz);
            if (probe == null || crossings == null || crossings.Count == 0) return straight;
            if (!probe.Blocks(ax, az, bx, bz)) return straight;

            float best = float.MaxValue;
            for (int i = 0; i < crossings.Count; i++)
            {
                var c = crossings[i];
                float viaCrossing = Distance(ax, az, c.X, c.Z) + Distance(c.X, c.Z, bx, bz);
                if (viaCrossing < best) best = viaCrossing;
            }
            return Math.Max(straight, best);
        }

        internal static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
