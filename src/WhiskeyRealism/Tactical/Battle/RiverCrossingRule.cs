using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>The side's picture of the map's river crossings (BC14): the land regions, the links between them, and how it knows each.</summary>
    public interface ICrossingPicture
    {
        bool Ready { get; }
        /// <summary>The land region of a point; -1 on open river, on a crossing block, off the map or before the picture is ready.</summary>
        int RegionAt(float x, float z);
        int LinkCount { get; }
        bool LinkTouches(int link, int region);
        /// <summary>The land regions the link joins (at most <see cref="RiverCrossingRule.MaxRegionsPerLink"/>), written into the span; the count.</summary>
        int RegionsOfLink(int link, Span<int> regions);
        void LinkPoint(int link, out float x, out float z);
        CrossingState StateOf(int link);
        /// <summary>The link is on a route of the plan the side selected (BC14's ledger): a crossing the plan chose is never the gate's to refuse.</summary>
        bool IsPlanned(int link);
        /// <summary>A move was refused for want of a known crossing: the link the side should look at (BC14's look mechanism takes it from here).</summary>
        void RequestLook(int link);
    }

    public enum CrossingVerdict
    {
        /// <summary>The leg stays on one bank (or the picture cannot say): nothing to route.</summary>
        SameBank,
        /// <summary>The leg crosses one or more rivers and the side has a chain of crossings for it, known or chosen by the plan: go by them (<see cref="CrossingRoute"/>).</summary>
        ViaCrossing,
        /// <summary>The leg crosses a river and the side knows no chain of crossings it may use: no move.</summary>
        NoKnownCrossing,
    }

    /// <summary>The gate's answer for one leg: the vias to take (at most two, the executor's bound), and the link a refused leg wants looked at.</summary>
    public struct CrossingRoute
    {
        public int ViaCount;
        public float ViaX0, ViaZ0, ViaX1, ViaZ1;
        /// <summary>The crossings the whole route needs; above <see cref="ViaCount"/> the rest are met when the group re-orders from where the vias leave it.</summary>
        public int Crossings;
        /// <summary>With <see cref="CrossingVerdict.NoKnownCrossing"/>: the first Unknown link of the best route if every link were passable, else -1.</summary>
        public int LookLink;
    }

    /// <summary>
    /// A move whose straight leg crosses a river line takes a chain of crossings the side knows, or is not made (smoke 2026-10-17:
    /// posture Screen and Probe moves and division-play moves were checked for water at their target, never for the river they cross).
    /// The start and the target lie in different land regions (RiverRegions.RegionAt; open river and crossing blocks are not land). The
    /// route is a shortest path over the region/link graph (BC14's own: a link joins up to four regions, two links are neighbours when
    /// they share a region), so a leg over several rivers is routed link by link. A link carries a move when the side perceives it Clear or
    /// Watched, or when the selected plan chose it (whatever the side knows of it: the plan's crossings are never refused, and an
    /// attack across a Held bridge the plan ordered is made, by that bridge). Unknown and Held links the plan did not choose do not. When no
    /// route exists the best route over every link names the first Unknown one, which is posted as a look request: the move is not made
    /// now, and is made once the crossing reads Clear.
    /// </summary>
    public static class RiverCrossingRule
    {
        public const int MaxRegionsPerLink = 4;
        private const int MaxLinks = 256;

        public static CrossingVerdict Decide(ICrossingPicture picture, float x0, float z0, float x1, float z1, out CrossingRoute route)
        {
            route = new CrossingRoute { LookLink = -1 };
            if (picture == null || !picture.Ready) return CrossingVerdict.SameBank;
            int from = picture.RegionAt(x0, z0);
            int to = picture.RegionAt(x1, z1);
            if (from < 0 || to < 0 || from == to) return CrossingVerdict.SameBank;

            Span<int> path = stackalloc int[MaxLinks];
            int count = Search(picture, x0, z0, x1, z1, from, to, Admission.KnownOrPlanned, path);
            if (count > 0)
            {
                route.Crossings = count;
                route.ViaCount = Math.Min(count, 2);
                picture.LinkPoint(path[0], out route.ViaX0, out route.ViaZ0);
                if (route.ViaCount > 1) picture.LinkPoint(path[1], out route.ViaX1, out route.ViaZ1);
                return CrossingVerdict.ViaCrossing;
            }

            // No route the side may take. The best route over every link says which Unknown crossing would make one.
            int wished = Search(picture, x0, z0, x1, z1, from, to, Admission.Any, path);
            for (int k = 0; k < wished; k++)
            {
                if (picture.StateOf(path[k]) != CrossingState.Unknown) continue;
                route.LookLink = path[k];
                picture.RequestLook(path[k]);
                break;
            }
            return CrossingVerdict.NoKnownCrossing;
        }

        private static bool IsKnown(CrossingState state) => state == CrossingState.Clear || state == CrossingState.Watched;

        private enum Admission { KnownOrPlanned, Any }

        // Dijkstra over the links: a link's cost is the straight distance from the point the group is at when it takes it. The start
        // links are those of the start region; the route ends at a link of the target region. Returns the links in order; 0 when none.
        private static int Search(ICrossingPicture p, float x0, float z0, float x1, float z1, int from, int to, Admission admission, Span<int> path)
        {
            int links = Math.Min(p.LinkCount, MaxLinks);
            if (links <= 0) return 0;
            Span<float> dist = stackalloc float[MaxLinks];
            Span<int> prev = stackalloc int[MaxLinks];
            Span<bool> done = stackalloc bool[MaxLinks];
            Span<int> regionsU = stackalloc int[MaxRegionsPerLink];
            Span<int> regionsV = stackalloc int[MaxRegionsPerLink];
            for (int i = 0; i < links; i++)
            {
                prev[i] = -1;
                done[i] = false;
                dist[i] = float.PositiveInfinity;
                if (!Admits(p, i, admission) || !p.LinkTouches(i, from)) continue;
                p.LinkPoint(i, out float lx, out float lz);
                dist[i] = Distance(x0, z0, lx, lz);
            }

            int last = -1;
            float total = float.PositiveInfinity;
            while (true)
            {
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < links; i++)
                    if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                p.LinkPoint(u, out float ux, out float uz);
                if (p.LinkTouches(u, to))
                {
                    float through = dist[u] + Distance(ux, uz, x1, z1);
                    if (through < total) { total = through; last = u; }
                }
                int nu = Math.Min(p.RegionsOfLink(u, regionsU), MaxRegionsPerLink);
                for (int v = 0; v < links; v++)
                {
                    if (done[v] || !Admits(p, v, admission)) continue;
                    bool shares = false;
                    int nv = Math.Min(p.RegionsOfLink(v, regionsV), MaxRegionsPerLink);
                    for (int a = 0; a < nu && !shares; a++)
                        for (int b = 0; b < nv; b++)
                            if (regionsU[a] == regionsV[b]) { shares = true; break; }
                    if (!shares) continue;
                    p.LinkPoint(v, out float vx, out float vz);
                    float via = dist[u] + Distance(ux, uz, vx, vz);
                    if (via < dist[v]) { dist[v] = via; prev[v] = u; }
                }
            }
            if (last < 0) return 0;

            int n = 0;
            for (int k = last; k >= 0 && n < path.Length; k = prev[k]) path[n++] = k;
            path.Slice(0, n).Reverse();
            return n;
        }

        private static bool Admits(ICrossingPicture p, int link, Admission admission)
            => admission == Admission.Any || IsKnown(p.StateOf(link)) || p.IsPlanned(link);

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>The executor's seam to the picture: one answer per planned Move (the runtime reads the side's own crossing watch).</summary>
    public interface ICrossingGate
    {
        CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, out CrossingRoute route);
    }
}
