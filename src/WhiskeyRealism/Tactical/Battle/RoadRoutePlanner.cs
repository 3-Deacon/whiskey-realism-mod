using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>A planned road march: up to two via points that pull a move onto the road around woods or swamp.</summary>
    public struct RoadRoute
    {
        public bool Use;
        public int ViaCount;
        public float ViaX0, ViaZ0, ViaX1, ViaZ1;
        public float RouteCost, VanillaCost;
    }

    /// <summary>What the posture pipeline does with a planned route, given the group's formation.</summary>
    public enum ColumnDecision
    {
        /// <summary>No route: the move goes as the pipeline planned it.</summary>
        NoRoute = 0,
        /// <summary>The group is in march column: the routed move goes out.</summary>
        Routed = 1,
        /// <summary>Group formation 4 is ordered and the move waits for it (at most ColumnWaitHours).</summary>
        Wait = 2,
        /// <summary>The group did not reach march column in time: the move goes unrouted.</summary>
        TimedOutUnrouted = 3,
    }

    /// <summary>Pooled working arrays of one route plan, sized to <see cref="RoadRoutePlanner.MaxCells"/>; grow-only.</summary>
    public sealed class RoadRouteScratch
    {
        internal float[] G = new float[RoadRoutePlanner.MaxCells];
        internal int[] Parent = new int[RoadRoutePlanner.MaxCells];
        internal byte[] Mark = new byte[RoadRoutePlanner.MaxCells];      // 0 unseen, 1 open, 2 closed
        internal byte[] Class = new byte[RoadRoutePlanner.MaxCells];     // 0 unclassified, 1 impassable, 2 passable, 3 passable road
        internal float[] SnapX = new float[RoadRoutePlanner.MaxCells];
        internal float[] SnapZ = new float[RoadRoutePlanner.MaxCells];
        internal float[] Cost = new float[RoadRoutePlanner.MaxCells];
        internal int[] HeapNode = new int[RoadRoutePlanner.MaxCells * 4];
        internal float[] HeapKey = new float[RoadRoutePlanner.MaxCells * 4];
        internal int[] PathNodes = new int[RoadRoutePlanner.MaxCells];
        internal int HeapCount;
    }

    // Road march routing (BC10 decision 6), pure. A long move through woods or swamp is cheap
    // for vanilla's pathfinder and slow for the troops: texture terrains (forest 14, swamp 15,
    // field 16) get no NavMesh area cost (mesh ids get theirs at load, 54522; textures do not,
    // 54537-54555), so the pathfinder treats woods as open ground (cost 2) while units move at
    // 1 / terrainspeccost (forest 4, swamp 6; GetTerrainSpeedAdjustment 132888-132901). AI
    // regiments already take roads (useroads true from initialisation, 112853; Roads and Bridges
    // priced 1 against 2 for open ground, AddPath 130359-130394), except within
    // maximumdistancetoobjectiveroadmovement (250 m, battleprefs 846) of their target
    // (131163-131166). This finds, with an A* over a lattice of the terrain grid priced by speed
    // cost, whether a road way round the woods is clearly faster than the way vanilla's
    // pathfinder takes, and if so names up to two via points (the route's first road entry and
    // last road exit) the executor sends the group through.
    //
    // The lattice is 25 m cells aligned to the move, inside a corridor 0.6 x the move wide,
    // narrowed until it fits MaxCells; each cell is classified from a 5 m sample grid so a
    // road 4-8 m wide is seen: a cell holding a bridge or road sample is a road cell and
    // snaps to it, a cell holding open-river samples and no ford or bridge is impassable.
    public static class RoadRoutePlanner
    {
        public const float MinRouteMeters = 400f;
        public const float WoodedShareTrigger = 0.30f;
        public const float RouteCellMeters = 25f;
        public const float RouteGainRatio = 0.80f;
        public const float NoRoadNearTargetMeters = 250f;
        public const float MinCorridorShare = 0.2f;
        public const float CorridorWidthShare = 0.6f;
        public const float ColumnWaitHours = 0.1f;
        public const float MinViaSeparationMeters = OrderArbiter.DedupeMeters;
        public const int ColumnGroupFormation = 4; // March Column (5883); regiments take formation 3 (91909-91917)
        public const int MaxCells = 6000;
        public const int MaxVia = 2;

        private const float StraightSampleMeters = 10f;
        private const float CellSampleStep = 5f;

        /// <summary>
        /// Plans a road route for the move (x0, z0) -> (x1, z1). <paramref name="vanillaPathX"/> /
        /// <paramref name="vanillaPathZ"/> are the corners of the NavMesh path vanilla's own
        /// pathfinder takes (the probe's filter path); with none, nothing is planned.
        /// </summary>
        public static RoadRoute Plan(BattleTerrainGrid g, float x0, float z0, float x1, float z1,
            ReadOnlySpan<float> vanillaPathX, ReadOnlySpan<float> vanillaPathZ, int vanillaCorners, RoadRouteScratch scratch)
        {
            var route = new RoadRoute();
            if (g == null || scratch == null || !Wanted(g, x0, z0, x1, z1)) return route;
            float dx = x1 - x0;
            float dz = z1 - z0;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);

            route.VanillaCost = PolylineCost(g, vanillaPathX, vanillaPathZ, vanillaCorners);
            if (!(route.VanillaCost > 0f)) return route;

            float ux = dx / length, uz = dz / length;   // along the move
            float vx = -uz, vz = ux;                    // across it (left)
            if (!Corridor(length, out int rows, out int columns)) return route;
            int halfWidth = (columns - 1) / 2;

            var lattice = new Lattice(g, scratch, x0, z0, ux, uz, vx, vz, length, rows, columns, halfWidth);
            int start = lattice.Index(0, halfWidth);
            int goal = lattice.Index(rows - 1, halfWidth);
            int nodes = FindPath(in lattice, start, goal, x1, z1);
            if (nodes < 2) return route;

            route.RouteCost = scratch.G[goal];
            if (!(route.RouteCost <= RouteGainRatio * route.VanillaCost)) return route;
            ChooseVia(scratch, nodes, x0, z0, x1, z1, ref route);
            route.Use = route.ViaCount > 0;
            return route;
        }

        /// <summary>
        /// The cheap precondition of a route plan: the move is at least 400 m and its straight
        /// line (10 m samples) runs at least 30% through woods or swamp. The posture pipeline asks
        /// this before it spends a NavMesh path and an A*.
        /// </summary>
        public static bool Wanted(BattleTerrainGrid g, float x0, float z0, float x1, float z1)
        {
            if (g == null) return false;
            float dx = x1 - x0;
            float dz = z1 - z0;
            if (!(Math.Sqrt(dx * dx + dz * dz) >= MinRouteMeters)) return false;
            return WoodedShare(g, x0, z0, x1, z1) >= WoodedShareTrigger;
        }

        /// <summary>
        /// The lattice of a move of <paramref name="length"/> metres: rows along it, columns
        /// across it (odd, centred on the move), inside a corridor 0.6 x the length wide,
        /// narrowed until rows x columns fits <see cref="MaxCells"/>. False when the narrowed
        /// corridor is under <see cref="MinCorridorShare"/> x the length (no route is planned).
        /// A 4 km move is 161 rows and fits 37 columns (925 m, 0.23 x its length).
        /// </summary>
        public static bool Corridor(float length, out int rows, out int columns)
        {
            rows = (int)Math.Ceiling(length / RouteCellMeters) + 1;
            columns = 0;
            if (rows < 2 || rows > MaxCells) return false;
            int halfWidth = (int)Math.Floor(length * CorridorWidthShare / 2f / RouteCellMeters);
            int fit = (MaxCells / rows - 1) / 2;
            if (halfWidth > fit) halfWidth = fit;
            if (halfWidth < 0) return false;
            columns = halfWidth * 2 + 1;
            return columns * RouteCellMeters >= MinCorridorShare * length;
        }

        /// <summary>
        /// The posture pipeline's decision for a planned route: the group's formation against the
        /// column the route needs, and how long the move has waited for it.
        /// </summary>
        public static ColumnDecision DecideColumn(bool routePlanned, int groupFormation, double waitedHours)
        {
            if (!routePlanned) return ColumnDecision.NoRoute;
            if (groupFormation == ColumnGroupFormation) return ColumnDecision.Routed;
            return (float)waitedHours < ColumnWaitHours ? ColumnDecision.Wait : ColumnDecision.TimedOutUnrouted;
        }

        // ------------------------------------------------------------------ straight line and vanilla's path

        private static float WoodedShare(BattleTerrainGrid g, float x0, float z0, float x1, float z1)
        {
            // 1024 samples of StraightSampleMeters cover any battle map's longest move.
            Span<int> ids = stackalloc int[1024];
            int count = g.SampleLine(x0, z0, x1, z1, StraightSampleMeters, ids);
            int wooded = 0;
            for (int i = 0; i < count; i++)
                if (ids[i] == BattleTerrainGrid.ForestId || ids[i] == BattleTerrainGrid.SwampId) wooded++;
            return count > 0 ? (float)wooded / count : 0f;
        }

        // Movement time of a polyline in metre-cost units: each leg in steps of at most 10 m,
        // each priced at the speed cost of the cell under its midpoint.
        private static float PolylineCost(BattleTerrainGrid g, ReadOnlySpan<float> px, ReadOnlySpan<float> pz, int corners)
        {
            corners = Math.Min(corners, Math.Min(px.Length, pz.Length));
            float total = 0f;
            for (int c = 0; c + 1 < corners; c++)
            {
                float dx = px[c + 1] - px[c];
                float dz = pz[c + 1] - pz[c];
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (!(d > 0f)) continue;
                int steps = (int)Math.Ceiling(d / StraightSampleMeters);
                float stepLength = d / steps;
                for (int s = 0; s < steps; s++)
                {
                    float t = (s + 0.5f) * stepLength / d;
                    total += stepLength * g.SpeedCost(g.IdAt(px[c] + dx * t, pz[c] + dz * t));
                }
            }
            return total;
        }

        // ------------------------------------------------------------------ the lattice

        private readonly struct Lattice
        {
            private readonly BattleTerrainGrid _g;
            private readonly RoadRouteScratch _s;
            private readonly float _x0, _z0, _ux, _uz, _vx, _vz, _length;
            internal readonly int Rows, Columns, HalfWidth;

            internal Lattice(BattleTerrainGrid g, RoadRouteScratch s, float x0, float z0, float ux, float uz, float vx, float vz,
                float length, int rows, int columns, int halfWidth)
            {
                _g = g; _s = s; _x0 = x0; _z0 = z0; _ux = ux; _uz = uz; _vx = vx; _vz = vz; _length = length;
                Rows = rows; Columns = columns; HalfWidth = halfWidth;
                int cells = rows * columns;
                Array.Clear(s.Mark, 0, cells);
                Array.Clear(s.Class, 0, cells);
            }

            internal RoadRouteScratch Scratch => _s;

            internal int Index(int row, int column) => row * Columns + column;

            // The cell's class, its price and its snapped position (lazily, once per plan).
            internal bool Passable(int node, out float x, out float z, out float cost)
            {
                if (_s.Class[node] == 0) Classify(node);
                x = _s.SnapX[node];
                z = _s.SnapZ[node];
                cost = _s.Cost[node];
                return _s.Class[node] >= 2;
            }

            private void Classify(int node)
            {
                int row = node / Columns;
                int column = node % Columns;
                float along = Math.Min(row * RouteCellMeters, _length);
                float across = (column - HalfWidth) * RouteCellMeters;
                float cx = _x0 + _ux * along + _vx * across;
                float cz = _z0 + _uz * along + _vz * across;
                _s.SnapX[node] = cx;
                _s.SnapZ[node] = cz;

                // Off the playable map (vanilla clamps path targets to [1, size - 2], 131057-131068).
                if (cx < 1f || cx > _g.SizeX - 2f || cz > -1f || cz < -_g.SizeZ + 2f)
                {
                    _s.Class[node] = 1;
                    return;
                }

                bool river = false, crossing = false, road = false;
                float bestRoadDistance = float.MaxValue, roadX = cx, roadZ = cz;
                int centreId = _g.IdAt(cx, cz);
                for (int ox = -2; ox <= 2; ox++)
                {
                    for (int oz = -2; oz <= 2; oz++)
                    {
                        float sx = cx + ox * CellSampleStep;
                        float sz = cz + oz * CellSampleStep;
                        int id = _g.IdAt(sx, sz);
                        if (id == BattleTerrainGrid.RiverId && !_g.Passable(id)) river = true;
                        else if (id == BattleTerrainGrid.FordId || id == BattleTerrainGrid.BridgeId) crossing = true;
                        if (id == BattleTerrainGrid.RoadId || id == BattleTerrainGrid.BridgeId)
                        {
                            road = true;
                            float d = ox * ox + oz * oz;
                            if (d < bestRoadDistance)
                            {
                                bestRoadDistance = d;
                                roadX = sx;
                                roadZ = sz;
                            }
                        }
                    }
                }

                if (river && !crossing)
                {
                    _s.Class[node] = 1;
                    return;
                }
                if (road)
                {
                    _s.SnapX[node] = roadX;
                    _s.SnapZ[node] = roadZ;
                    _s.Cost[node] = _g.SpeedCost(BattleTerrainGrid.RoadId);
                    _s.Class[node] = 3;
                    return;
                }
                int priced = centreId == BattleTerrainGrid.RiverId && !_g.Passable(centreId) ? BattleTerrainGrid.FordId : centreId;
                _s.Cost[node] = _g.SpeedCost(priced);
                _s.Class[node] = 2;
            }
        }

        // ------------------------------------------------------------------ A*

        // 8-connected A* from start to goal over the lattice. Edge cost = distance between the
        // cells' snapped positions x the mean speed cost of the two cells; the heuristic is the
        // straight distance to the goal at the cheapest speed cost (1), so it never overestimates.
        // Returns the node count of the path (written start -> goal into scratch.PathNodes), or 0.
        private static int FindPath(in Lattice lattice, int start, int goal, float goalX, float goalZ)
        {
            RoadRouteScratch s = lattice.Scratch;
            s.HeapCount = 0;
            lattice.Passable(start, out _, out _, out _); // the start cell is where the group stands: always usable
            if (!lattice.Passable(goal, out _, out _, out _)) return 0;

            s.G[start] = 0f;
            s.Parent[start] = -1;
            s.Mark[start] = 1;
            if (!Push(s, start, Heuristic(s, start, goalX, goalZ))) return 0;

            while (s.HeapCount > 0)
            {
                int n = Pop(s);
                if (s.Mark[n] == 2) continue;
                s.Mark[n] = 2;
                if (n == goal) return Reconstruct(s, start, goal);

                int row = n / lattice.Columns;
                int column = n % lattice.Columns;
                for (int dr = -1; dr <= 1; dr++)
                {
                    int nr = row + dr;
                    if (nr < 0 || nr >= lattice.Rows) continue;
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int nc = column + dc;
                        if ((dr == 0 && dc == 0) || nc < 0 || nc >= lattice.Columns) continue;
                        int m = lattice.Index(nr, nc);
                        if (s.Mark[m] == 2) continue;
                        if (!lattice.Passable(m, out float mx, out float mz, out float mCost)) continue;
                        float ex = mx - s.SnapX[n];
                        float ez = mz - s.SnapZ[n];
                        float step = (float)Math.Sqrt(ex * ex + ez * ez) * 0.5f * (s.Cost[n] + mCost);
                        float candidate = s.G[n] + step;
                        if (s.Mark[m] == 1 && !(candidate < s.G[m])) continue;
                        s.G[m] = candidate;
                        s.Parent[m] = n;
                        s.Mark[m] = 1;
                        if (!Push(s, m, candidate + Heuristic(s, m, goalX, goalZ))) return 0;
                    }
                }
            }
            return 0;
        }

        private static float Heuristic(RoadRouteScratch s, int node, float goalX, float goalZ)
        {
            float dx = goalX - s.SnapX[node];
            float dz = goalZ - s.SnapZ[node];
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static int Reconstruct(RoadRouteScratch s, int start, int goal)
        {
            int count = 0;
            for (int n = goal; n >= 0 && count < s.PathNodes.Length; n = s.Parent[n])
            {
                s.PathNodes[count++] = n;
                if (n == start) break;
            }
            Array.Reverse(s.PathNodes, 0, count);
            return count;
        }

        // Binary min-heap on HeapKey. Lazy deletion: a node may sit in it more than once and a stale
        // entry is skipped when popped (its Mark is closed). A full heap fails the plan.
        private static bool Push(RoadRouteScratch s, int node, float key)
        {
            if (s.HeapCount >= s.HeapNode.Length) return false;
            int i = s.HeapCount++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (s.HeapKey[parent] <= key) break;
                s.HeapNode[i] = s.HeapNode[parent];
                s.HeapKey[i] = s.HeapKey[parent];
                i = parent;
            }
            s.HeapNode[i] = node;
            s.HeapKey[i] = key;
            return true;
        }

        private static int Pop(RoadRouteScratch s)
        {
            int top = s.HeapNode[0];
            int last = --s.HeapCount;
            if (last > 0)
            {
                int node = s.HeapNode[last];
                float key = s.HeapKey[last];
                int i = 0;
                while (true)
                {
                    int child = i * 2 + 1;
                    if (child >= last) break;
                    if (child + 1 < last && s.HeapKey[child + 1] < s.HeapKey[child]) child++;
                    if (s.HeapKey[child] >= key) break;
                    s.HeapNode[i] = s.HeapNode[child];
                    s.HeapKey[i] = s.HeapKey[child];
                    i = child;
                }
                s.HeapNode[i] = node;
                s.HeapKey[i] = key;
            }
            return top;
        }

        // The route's first road entry and last road exit that are at least 60 m from the start,
        // the target and each other, and not within the last 250 m (vanilla skips roads there,
        // 131163-131166, so a via point in that stretch would pull the group off nothing).
        private static void ChooseVia(RoadRouteScratch s, int count, float x0, float z0, float x1, float z1, ref RoadRoute route)
        {
            int entry = -1;
            for (int k = 0; k < count && entry < 0; k++)
            {
                int node = s.PathNodes[k];
                if (s.Class[node] != 3) continue;
                if (Distance(s.SnapX[node], s.SnapZ[node], x0, z0) >= MinViaSeparationMeters &&
                    Distance(s.SnapX[node], s.SnapZ[node], x1, z1) >= NoRoadNearTargetMeters)
                    entry = k;
            }
            if (entry < 0) return;

            int entryNode = s.PathNodes[entry];
            route.ViaX0 = s.SnapX[entryNode];
            route.ViaZ0 = s.SnapZ[entryNode];
            route.ViaCount = 1;
            for (int k = count - 1; k > entry; k--)
            {
                int node = s.PathNodes[k];
                if (s.Class[node] != 3) continue;
                if (Distance(s.SnapX[node], s.SnapZ[node], x1, z1) < NoRoadNearTargetMeters) continue;
                if (Distance(s.SnapX[node], s.SnapZ[node], x0, z0) < MinViaSeparationMeters) continue;
                if (Distance(s.SnapX[node], s.SnapZ[node], route.ViaX0, route.ViaZ0) < MinViaSeparationMeters) continue;
                route.ViaX1 = s.SnapX[node];
                route.ViaZ1 = s.SnapZ[node];
                route.ViaCount = MaxVia;
                break;
            }
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
