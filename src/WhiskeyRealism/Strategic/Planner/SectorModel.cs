using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The planner's own sub-theater sectors (plan P11, spec C15): vanilla AI areas grouped by
    // adjacency and seeded at each live campaign objective; FrontSectorLedger keeps its 6 buckets.
    //   Adjacency: AreaGeometry.AreaDistance (vanilla GetClosestAreaDistance(AIArea, AIArea),
    //     14863-14900) <= aimaximumdistancetosearchforunitrelocations, the radius vanilla uses for
    //     "nearby areas" (13106, 14835).
    //   Seeds: the closest area of every target of an objective that is not accomplished, one
    //     sector per distinct seed area, numbered in area-list order (so the order vanilla lists
    //     objectives in does not matter).
    //   Growth: multi-source Dijkstra over adjacent areas weighted by centre distance; an area
    //     goes to the seed it is nearest to along the adjacency, a tie to the lower sector id.
    //   Unseeded components: each connected group of areas no seed reaches becomes its own
    //     sector, numbered after the seeded ones in area-list order.
    // Each sector's theater is TheaterClassifier.FromPosition of its centroid (the mean of its
    // areas' averageposition). The model copies what it needs: the snapshot it was built from is
    // pooled and rebuilt in place, while the store keeps the model until the next publish.
    public sealed class SectorModel
    {
        private int[] _sectorOfPosition = new int[0];       // per area list position
        private AreaRecord[] _areas = new AreaRecord[0];
        private readonly Dictionary<int, int> _positionOfIndex = new Dictionary<int, int>();
        private float[] _centroidX = new float[0];
        private float[] _centroidZ = new float[0];
        private Theater[] _theater = new Theater[0];

        public int Count => _theater.Length;

        public static SectorModel Build(CampaignSnapshot s)
        {
            var model = new SectorModel();
            if (s == null || s.Areas.Count == 0) return model;

            int n = s.Areas.Count;
            model._areas = new AreaRecord[n];
            for (int i = 0; i < n; i++)
            {
                var a = s.Areas[i];
                a.EdgeX = a.EdgeX == null ? null : (float[])a.EdgeX.Clone();
                a.EdgeZ = a.EdgeZ == null ? null : (float[])a.EdgeZ.Clone();
                model._areas[i] = a;
                model._positionOfIndex[a.Index] = i;
            }

            bool[,] adjacent = new bool[n, n];
            float range = s.RelocationRange;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    bool near = range > 0f && AreaGeometry.AreaDistance(model._areas[i], model._areas[j]) <= range;
                    adjacent[i, j] = near;
                    adjacent[j, i] = near;
                }

            var seedArea = new bool[n];
            for (int k = 0; k < s.Objectives.Count; k++)
            {
                var objective = s.Objectives[k];
                if (objective.Accomplished) continue;
                int position = AreaGeometry.AreaAt(model._areas, objective.X, objective.Z);
                if (position >= 0) seedArea[position] = true;
            }

            var owner = new int[n];
            var dist = new float[n];
            var done = new bool[n];
            int sectors = 0;
            for (int i = 0; i < n; i++)
            {
                owner[i] = -1;
                dist[i] = float.MaxValue;
                if (!seedArea[i]) continue;
                owner[i] = sectors++;
                dist[i] = 0f;
            }
            Grow(model._areas, adjacent, owner, dist, done);

            for (int i = 0; i < n; i++)
            {
                if (owner[i] >= 0) continue;
                owner[i] = sectors++;
                dist[i] = 0f;
                Grow(model._areas, adjacent, owner, dist, done);
            }

            model._sectorOfPosition = owner;
            model._centroidX = new float[sectors];
            model._centroidZ = new float[sectors];
            model._theater = new Theater[sectors];
            var members = new int[sectors];
            for (int i = 0; i < n; i++)
            {
                model._centroidX[owner[i]] += model._areas[i].X;
                model._centroidZ[owner[i]] += model._areas[i].Z;
                members[owner[i]]++;
            }
            for (int sector = 0; sector < sectors; sector++)
            {
                if (members[sector] > 0)
                {
                    model._centroidX[sector] /= members[sector];
                    model._centroidZ[sector] /= members[sector];
                }
                model._theater[sector] = TheaterClassifier.FromPosition(model._centroidX[sector], model._centroidZ[sector]);
            }
            return model;
        }

        // Dijkstra over the areas not yet finalized: repeatedly finalize the unfinished area with
        // the smallest (distance, sector id, list position) and relax its unowned or farther
        // neighbours. O(n^2), deterministic.
        private static void Grow(AreaRecord[] areas, bool[,] adjacent, int[] owner, float[] dist, bool[] done)
        {
            int n = areas.Length;
            while (true)
            {
                int pick = -1;
                for (int i = 0; i < n; i++)
                {
                    if (done[i] || owner[i] < 0) continue;
                    if (pick < 0 || dist[i] < dist[pick] || (dist[i] == dist[pick] && owner[i] < owner[pick]))
                        pick = i;
                }
                if (pick < 0) return;
                done[pick] = true;
                for (int j = 0; j < n; j++)
                {
                    if (done[j] || !adjacent[pick, j]) continue;
                    float dx = areas[j].X - areas[pick].X;
                    float dz = areas[j].Z - areas[pick].Z;
                    float candidate = dist[pick] + (float)Math.Sqrt(dx * dx + dz * dz);
                    if (owner[j] < 0 || candidate < dist[j] || (candidate == dist[j] && owner[pick] < owner[j]))
                    {
                        owner[j] = owner[pick];
                        dist[j] = candidate;
                    }
                }
            }
        }

        // areaIndex is vanilla's aiarea index (FormationRecord.AreaIndex, AreaRecord.Index).
        public int SectorOfArea(int areaIndex)
        {
            return _positionOfIndex.TryGetValue(areaIndex, out int position) ? _sectorOfPosition[position] : -1;
        }

        // The sector of the area closest to a map point (vanilla's own point-to-area distance, 14835).
        public int SectorAt(float x, float z)
        {
            int position = AreaGeometry.AreaAt(_areas, x, z);
            return position >= 0 ? _sectorOfPosition[position] : -1;
        }

        public Theater TheaterOf(int sector)
        {
            return sector >= 0 && sector < _theater.Length ? _theater[sector] : Theater.Unknown;
        }

        public void Centroid(int sector, out float x, out float z)
        {
            bool known = sector >= 0 && sector < _centroidX.Length;
            x = known ? _centroidX[sector] : 0f;
            z = known ? _centroidZ[sector] : 0f;
        }
    }

    // Vanilla's area distances on AreaRecord (pure mirrors of AICampaign.GetClosestAreaDistance).
    internal static class AreaGeometry
    {
        // GetClosestAreaDistance(AIArea, Vector3) 14835-14860: the closest of the area's zone
        // edges and its averageposition (99999 when it has neither).
        internal static float PointDistance(in AreaRecord a, float x, float z)
        {
            float best = 99999f;
            int edges = EdgeCount(a);
            for (int i = 0; i < edges; i++)
                best = Math.Min(best, Distance(a.EdgeX[i], a.EdgeZ[i], x, z));
            return Math.Min(best, Distance(a.X, a.Z, x, z));
        }

        // GetClosestAreaDistance(AIArea, AIArea) 14863-14900, including vanilla's cross term:
        // inside the edge-pair loop it measures area2.zoneedges[i] (not [j]) to area1's centre.
        // No centre-to-centre term.
        internal static float AreaDistance(in AreaRecord a, in AreaRecord b)
        {
            if (a.Index == b.Index) return 0f;
            float best = 99999f;
            int aEdges = EdgeCount(a);
            int bEdges = EdgeCount(b);
            for (int i = 0; i < aEdges; i++)
            {
                for (int j = 0; j < bEdges; j++)
                {
                    best = Math.Min(best, Distance(a.EdgeX[i], a.EdgeZ[i], b.EdgeX[j], b.EdgeZ[j]));
                    if (i < bEdges)
                        best = Math.Min(best, Distance(b.EdgeX[i], b.EdgeZ[i], a.X, a.Z));
                }
                best = Math.Min(best, Distance(a.EdgeX[i], a.EdgeZ[i], b.X, b.Z));
            }
            return best;
        }

        // List position of the area closest to the point (lowest position on a tie), -1 for none.
        internal static int AreaAt(IReadOnlyList<AreaRecord> areas, float x, float z)
        {
            if (areas == null) return -1;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < areas.Count; i++)
            {
                float d = PointDistance(areas[i], x, z);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = i;
            }
            return best;
        }

        private static int EdgeCount(in AreaRecord a)
        {
            if (a.EdgeX == null || a.EdgeZ == null) return 0;
            return Math.Min(a.EdgeX.Length, a.EdgeZ.Length);
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
