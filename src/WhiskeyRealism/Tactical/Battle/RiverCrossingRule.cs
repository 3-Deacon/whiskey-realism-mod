using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

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
        /// <summary>A road leg (between a BC10 route's two vias) whose crossing the side may use: the road takes its own crossing, so nothing is inserted.</summary>
        RoadCrossing,
        /// <summary>The leg crosses a river and the side knows no chain of crossings it may use: no move.</summary>
        NoKnownCrossing,
    }

    /// <summary>
    /// A leg between the two vias of a BC10 road route: the road follows the terrain's roads and takes its own crossing(s). <see cref="Links"/> are the
    /// BC14 links the road takes (RoadRoute.Link0 / Link1); with none known the crossing is taken to be the best route over every link.
    /// </summary>
    public readonly struct RoadLeg
    {
        public RoadLeg(int linkCount, int link0, int link1)
        {
            Is = true;
            LinkCount = linkCount;
            Link0 = link0;
            Link1 = link1;
        }

        public bool Is { get; }
        public int LinkCount { get; }
        public int Link0 { get; }
        public int Link1 { get; }
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
            => Decide(picture, x0, z0, x1, z1, attack, default(RoadLeg), out route);

        /// <summary>
        /// <paramref name="road"/> (<see cref="RoadLeg.Is"/>): the leg lies between the two vias of a BC10 road route. The road follows the terrain's roads and
        /// takes its own crossing, so no via is inserted; the crossings the road takes (its links, else the best route over every link) are only checked,
        /// by the rule of every other move: Clear or Watched goes; a crossing the selected plan chose goes unless it is Held, and a Held one goes for an
        /// attack-stance task only; anything else is refused, and an Unknown one posts the look.
        /// </summary>
        public static CrossingVerdict Decide(ICrossingPicture picture, float x0, float z0, float x1, float z1, bool attack, in RoadLeg road, out CrossingRoute route)
        {
            route = new CrossingRoute { LookLink = -1 };
            if (picture == null || !picture.Ready) return CrossingVerdict.SameBank;
            int from = picture.RegionAt(x0, z0);
            int to = picture.RegionAt(x1, z1);
            if (from < 0 || to < 0) return CrossingVerdict.SameBank;
            // A road leg that leaves its bank and comes back to it crosses the river twice: the road's recorded links are checked although both ends lie on one bank.
            if (from == to && !(road.Is && road.LinkCount > 0)) return CrossingVerdict.SameBank;

            Graph graph = GraphOf(picture);
            if (graph == null) return from == to ? CrossingVerdict.SameBank : CrossingVerdict.NoKnownCrossing;   // two banks and no crossing at all
            Span<int> path = stackalloc int[MaxLinks];
            Span<bool> admit = stackalloc bool[MaxLinks];
            if (road.Is) return DecideRoadLeg(picture, graph, x0, z0, x1, z1, from, to, attack, road, admit, path, ref route);
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

        // The crossings a road leg takes: the road's own links when the planner found them, else the best route over every link. Each must be one
        // the move may take (Admitted: the rule of a move off the road).
        private static CrossingVerdict DecideRoadLeg(ICrossingPicture picture, Graph graph, float x0, float z0, float x1, float z1, int from, int to, bool attack,
            in RoadLeg road, Span<bool> admit, Span<int> path, ref CrossingRoute route)
        {
            int count;
            if (road.LinkCount > 0 && Chains(graph, from, to, road))
            {
                count = Math.Min(road.LinkCount, 2);
                path[0] = road.Link0;
                if (count > 1) path[1] = road.Link1;
            }
            else
            {
                // No links recorded, or they do not chain from the leg's start region to its target region: the full search decides.
                if (from == to) return CrossingVerdict.SameBank;      // one bank and a record that does not chain: nothing known to check
                for (int i = 0; i < graph.Links; i++) admit[i] = true;
                count = Search(graph, picture, admit, x0, z0, x1, z1, from, to, path);
                if (count == 0) return CrossingVerdict.NoKnownCrossing;      // no crossing joins the banks at all: nothing to look at
            }
            route.Crossings = count;
            bool refused = false;
            for (int k = 0; k < count; k++)
            {
                if (Admitted(picture, path[k], attack)) continue;
                refused = true;
                if (picture.StateOf(path[k]) == CrossingState.Unknown && route.LookLink < 0)
                {
                    route.LookLink = path[k];
                    picture.RequestLook(path[k]);
                }
            }
            return refused ? CrossingVerdict.NoKnownCrossing : CrossingVerdict.RoadCrossing;
        }

        // The recorded links are the road's crossings only if they chain from the leg's start region to its target region: one link joins both; two links,
        // in road order, the first touching the start region, the second the target region, and sharing a region that is neither.
        private static bool Chains(Graph g, int from, int to, in RoadLeg road)
        {
            int count = Math.Min(road.LinkCount, 2);
            if (road.Link0 < 0 || road.Link0 >= g.Links) return false;
            if (count == 1) return Touches(g, road.Link0, from) && Touches(g, road.Link0, to);
            if (road.Link1 < 0 || road.Link1 >= g.Links) return false;
            if (!Touches(g, road.Link0, from) || !Touches(g, road.Link1, to)) return false;
            for (int k = 0; k < g.RegionCount[road.Link0]; k++)
            {
                int middle = g.Regions[road.Link0 * MaxRegionsPerLink + k];
                if (middle != from && middle != to && Touches(g, road.Link1, middle)) return true;
            }
            return false;
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

        // One graph, built from the first picture asked and kept while the same map (Topology) is asked about. It is battle state: a new battle has
        // new RiverRegions, which would never match, but the old regions would stay referenced until the next map was asked about, so the battle's end
        // drops it.
        private static Graph _graph;

        static RiverCrossingRule()
        {
            BattleScope.Register("river-crossing-graph", () => _graph = null);
        }

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
        /// <param name="road">The leg lies between the two vias of a BC10 road route: the road takes its own crossing, which is only checked.</param>
        CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, bool attack, in RoadLeg road, out CrossingRoute route);
    }
}
