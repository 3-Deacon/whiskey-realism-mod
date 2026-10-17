using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>The side's picture of the map's river crossings (BC14): the land regions, the links between them, and how it knows each.</summary>
    public interface ICrossingPicture
    {
        bool Ready { get; }
        /// <summary>The land region of a point; -1 on open river, on a crossing block, off the map or before the picture is ready.</summary>
        int RegionAt(float x, float z);
        int LinkCount { get; }
        /// <summary>The land regions the link joins (at most <see cref="RiverCrossingRule.MaxRegionsPerLink"/>), written into the span; the count.</summary>
        int RegionsOfLink(int link, Span<int> regions);
        void LinkPoint(int link, out float x, out float z);
        CrossingState StateOf(int link);
        /// <summary>The link is on a route of the plan the side selected (BC14's ledger): a crossing the plan chose is not the gate's to refuse for an attack.</summary>
        bool IsPlanned(int link);
        /// <summary>A move was refused for want of a known crossing: the link the side should look at (BC14's look mechanism takes it from here).</summary>
        void RequestLook(int link);
        /// <summary>
        /// Identifies the link graph (the regions and the links between them, which do not change within a battle): two pictures of the same map
        /// answer with the same object, so the rule builds its adjacency once for both sides.
        /// </summary>
        object Topology { get; }
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
            => Decide(picture, x0, z0, x1, z1, attack: false, out route);

        /// <summary>
        /// <paramref name="attack"/>: the move is an attack (AttackObjective, SupportAttack, Counterstroke). Only an attack may cross a Held
        /// crossing the selected plan chose; a Screen, a Probe or any other move never crosses a Held one.
        /// </summary>
        public static CrossingVerdict Decide(ICrossingPicture picture, float x0, float z0, float x1, float z1, bool attack, out CrossingRoute route)
        {
            route = new CrossingRoute { LookLink = -1 };
            if (picture == null || !picture.Ready) return CrossingVerdict.SameBank;
            int from = picture.RegionAt(x0, z0);
            int to = picture.RegionAt(x1, z1);
            if (from < 0 || to < 0 || from == to) return CrossingVerdict.SameBank;

            Graph graph = GraphOf(picture);
            if (graph == null) return CrossingVerdict.NoKnownCrossing;   // two banks and no crossing at all
            Span<int> path = stackalloc int[MaxLinks];
            Span<bool> admit = stackalloc bool[MaxLinks];
            for (int i = 0; i < graph.Links; i++) admit[i] = Admitted(picture, i, attack);
            int count = Search(graph, picture, admit, x0, z0, x1, z1, from, to, path);
            if (count > 0)
            {
                route.Crossings = count;
                route.ViaCount = Math.Min(count, 2);
                picture.LinkPoint(path[0], out route.ViaX0, out route.ViaZ0);
                if (route.ViaCount > 1) picture.LinkPoint(path[1], out route.ViaX1, out route.ViaZ1);
                return CrossingVerdict.ViaCrossing;
            }

            // No route the side may take. The best route over every link says which Unknown crossing would make one.
            for (int i = 0; i < graph.Links; i++) admit[i] = true;
            int wished = Search(graph, picture, admit, x0, z0, x1, z1, from, to, path);
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

        // A link carries the move when the side perceives it Clear or Watched, or when the plan chose it: whatever the side knows of it for an
        // attack, and unless it is Held for any other move (a Screen or Probe never crosses a Held crossing).
        private static bool Admitted(ICrossingPicture p, int link, bool attack)
        {
            CrossingState state = p.StateOf(link);
            if (IsKnown(state)) return true;
            return p.IsPlanned(link) && (state != CrossingState.Held || attack);
        }

        // The link graph, built once per map: each link's regions and point, and the links that share a region with it (CSR). Two sides share it.
        private sealed class Graph
        {
            public object Key;
            public int Links;
            public int[] RegionCount, Regions, NeighbourStart, Neighbours;
            public float[] X, Z, Weight;
        }

        private static Graph _graph;

        private static Graph GraphOf(ICrossingPicture p)
        {
            int links = Math.Min(p.LinkCount, MaxLinks);
            Graph g = _graph;
            if (g != null && ReferenceEquals(g.Key, p.Topology) && g.Links == links) return g;
            if (links <= 0) return null;
            g = new Graph
            {
                Key = p.Topology, Links = links, RegionCount = new int[links], Regions = new int[links * MaxRegionsPerLink],
                NeighbourStart = new int[links + 1], X = new float[links], Z = new float[links],
            };
            Span<int> regions = stackalloc int[MaxRegionsPerLink];
            for (int i = 0; i < links; i++)
            {
                int n = Math.Min(p.RegionsOfLink(i, regions), MaxRegionsPerLink);
                g.RegionCount[i] = n;
                for (int k = 0; k < n; k++) g.Regions[i * MaxRegionsPerLink + k] = regions[k];
                p.LinkPoint(i, out g.X[i], out g.Z[i]);
            }
            var neighbours = new List<int>(links * 4);
            var weights = new List<float>(links * 4);
            for (int u = 0; u < links; u++)
            {
                g.NeighbourStart[u] = neighbours.Count;
                for (int v = 0; v < links; v++)
                {
                    if (v == u || !Shares(g, u, v)) continue;
                    neighbours.Add(v);
                    weights.Add(Distance(g.X[u], g.Z[u], g.X[v], g.Z[v]));
                }
            }
            g.NeighbourStart[links] = neighbours.Count;
            g.Neighbours = neighbours.ToArray();
            g.Weight = weights.ToArray();
            _graph = g;
            return g;
        }

        private static bool Shares(Graph g, int u, int v)
        {
            for (int a = 0; a < g.RegionCount[u]; a++)
                for (int b = 0; b < g.RegionCount[v]; b++)
                    if (g.Regions[u * MaxRegionsPerLink + a] == g.Regions[v * MaxRegionsPerLink + b]) return true;
            return false;
        }

        private static bool Touches(Graph g, int link, int region)
        {
            for (int k = 0; k < g.RegionCount[link]; k++)
                if (g.Regions[link * MaxRegionsPerLink + k] == region) return true;
            return false;
        }

        // Dijkstra over the admitted links: a link's cost is the straight distance from the point the group is at when it takes it. The start
        // links are those of the start region; the route ends at a link of the target region. Returns the links in order; 0 when none.
        private static int Search(Graph g, ICrossingPicture p, ReadOnlySpan<bool> admit, float x0, float z0, float x1, float z1, int from, int to, Span<int> path)
        {
            int links = g.Links;
            Span<float> dist = stackalloc float[MaxLinks];
            Span<int> prev = stackalloc int[MaxLinks];
            Span<bool> done = stackalloc bool[MaxLinks];
            for (int i = 0; i < links; i++)
            {
                prev[i] = -1;
                done[i] = false;
                dist[i] = admit[i] && Touches(g, i, from) ? Distance(x0, z0, g.X[i], g.Z[i]) : float.PositiveInfinity;
            }

            int last = -1;
            float total = float.PositiveInfinity;
            while (true)
            {
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < links; i++)
                    if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0 || best >= total) break;      // nothing left that can beat the route found
                done[u] = true;
                if (Touches(g, u, to))
                {
                    float through = dist[u] + Distance(g.X[u], g.Z[u], x1, z1);
                    if (through < total) { total = through; last = u; }
                }
                for (int e = g.NeighbourStart[u]; e < g.NeighbourStart[u + 1]; e++)
                {
                    int v = g.Neighbours[e];
                    if (done[v] || !admit[v]) continue;
                    float via = dist[u] + g.Weight[e];
                    if (via < dist[v]) { dist[v] = via; prev[v] = u; }
                }
            }
            if (last < 0) return 0;

            int n = 0;
            for (int k = last; k >= 0 && n < path.Length; k = prev[k]) path[n++] = k;
            path.Slice(0, n).Reverse();
            return n;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>The executor's seam to the picture: one answer per planned Move (the runtime reads the side's own crossing watch).</summary>
    public interface ICrossingGate
    {
        /// <param name="attack">The move is an attack task: only an attack may cross a Held crossing the plan chose.</param>
        CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, bool attack, out CrossingRoute route);
    }
}
