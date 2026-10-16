using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum TerrainLineKind { None = 0, Crest, Wall, SunkenRoad, Fence, Embankment, River }

    /// <summary>A terrain line the picture offers a defender: endpoints, centre and its strength against the threat axis.</summary>
    public struct TerrainLine
    {
        public bool Has;
        public float X0, Z0, X1, Z1, CenterX, CenterZ, Strength;
        public TerrainLineKind Kind;

        public float Length
        {
            get
            {
                float dx = X1 - X0, dz = Z1 - Z0;
                return (float)Math.Sqrt(dx * dx + dz * dz);
            }
        }
    }

    // BC12 decision 6: the defensive terrain lines. Pure over TerrainPicture.
    //
    // BestLine scans the cells within LineSearchMeters (400 m) of the objective for crest runs, cover lines (stone wall, sunken
    // road, fence, embankment) and river lines. A line is a connected run (8-neighbour) of cells of one kind; its direction is
    // the run's principal axis, its length the extent along it plus one cell. A candidate must lie within 30 degrees of
    // perpendicular to the threat axis, be at least MinLineMeters (200 m) long, stand on the own side of the objective or at most
    // MaxForwardMeters (100 m) in front of it, and within the frontage laterally. Score (unitless) = the line's terrain strength
    // (TerrainPicture.TerrainStrength at its defensive point) - its distance from the objective / 1,000 m. A river line is held
    // from RiverStandOffMeters behind it, a crest at its forward edge (the military crest, half a cell forward) or, under
    // ReverseSlopeDefense, ReverseSlopeSetbackMeters behind the crest.
    public static class TacticalTerrainLinePlanner
    {
        public const float LineSearchMeters = 400f;
        public const float MinLineMeters = 200f;
        public const float MaxForwardMeters = 100f;
        public const float ReverseSlopeSetbackMeters = 80f;
        public const float RiverStandOffMeters = 150f;
        public const float MaxAngleDegreesOffPerpendicular = 30f;
        /// <summary>The weakest line worth holding: a fence's 0.10.</summary>
        public const float MinLineStrength = 0.10f;
        /// <summary>The military crest: the defensive point is half a cell forward of the crest cells' centre.</summary>
        public const float ForwardEdgeMeters = TerrainPicture.CellMeters * 0.5f;

        private const int KindCount = 6;
        private const int MemoSize = 8;

        private struct Memo
        {
            public TerrainPicture Picture;
            public int Mask;
            public int X, Z, Axis, Frontage;
            public TerrainLine Line;
        }

        private static readonly Memo[] _memo = new Memo[MemoSize];
        private static int _memoNext;
        private static int[] _stamp = new int[1];
        private static int[] _stack = new int[1];
        private static int[] _members = new int[1];
        private static int _generation;

        static TacticalTerrainLinePlanner()
        {
            BattleScope.Register("terrain-line-planner", Reset);
        }

        private static void Reset()
        {
            for (int i = 0; i < MemoSize; i++) _memo[i] = default(Memo);
            _memoNext = 0;
            _generation = 0;
            Array.Clear(_stamp, 0, _stamp.Length);
        }

        /// <summary>The best crest, wall, sunken road, fence, embankment or river line near the objective; Has = false when none.</summary>
        public static TerrainLine BestLine(TerrainPicture p, float objX, float objZ, float axisX, float axisZ, float frontage)
        {
            return Best(p, AllKinds, objX, objZ, axisX, axisZ, frontage);
        }

        /// <summary>The best crest run alone (the crest playbooks, decision 8).</summary>
        public static TerrainLine BestCrest(TerrainPicture p, float objX, float objZ, float axisX, float axisZ, float frontage)
        {
            return Best(p, 1 << (int)TerrainLineKind.Crest, objX, objZ, axisX, axisZ, frontage);
        }

        private const int AllKinds = (1 << (int)TerrainLineKind.Crest) | (1 << (int)TerrainLineKind.Wall) | (1 << (int)TerrainLineKind.SunkenRoad)
            | (1 << (int)TerrainLineKind.Fence) | (1 << (int)TerrainLineKind.Embankment) | (1 << (int)TerrainLineKind.River);

        /// <summary>
        /// The point a defender holds for the line: a crest's forward edge (or <see cref="ReverseSlopeSetbackMeters"/> behind the
        /// crest under ReverseSlopeDefense), a river line from <see cref="RiverStandOffMeters"/> behind it, any other line on its centre.
        /// </summary>
        public static void AnchorPoint(in TerrainLine line, float axisX, float axisZ, bool reverseSlope, out float x, out float z)
        {
            x = line.CenterX;
            z = line.CenterZ;
            if (!Unit(ref axisX, ref axisZ)) return;
            if (line.Kind == TerrainLineKind.Crest)
            {
                float shift = reverseSlope ? -ReverseSlopeSetbackMeters : ForwardEdgeMeters;
                x += axisX * shift;
                z += axisZ * shift;
            }
            else if (line.Kind == TerrainLineKind.River)
            {
                x -= axisX * RiverStandOffMeters;
                z -= axisZ * RiverStandOffMeters;
            }
        }

        /// <summary>
        /// Vanilla's anchored-flank rule (CheckIfFlanksAreAnchored 6599-6640): a flank end is anchored by river id 4 within
        /// AnchorRiverMeters (80) sideways (along the outward direction), or a crest within AnchorCrestMeters (50) of the end.
        /// </summary>
        public static bool FlankAnchored(TerrainPicture p, float endX, float endZ, float outwardX, float outwardZ)
        {
            if (p == null || !p.Ready) return false;
            if (Unit(ref outwardX, ref outwardZ))
            {
                float reach = p.Rules.AnchorRiverMeters;
                for (float d = 0f; d <= reach; d += TerrainPicture.CellMeters * 0.5f)
                    if (p.IdAt(endX + outwardX * d, endZ + outwardZ * d) == TerrainPicture.IdRiver) return true;
            }
            return p.CrestNear(endX, endZ, p.Rules.AnchorCrestMeters, p.Rules.MinSteepness);
        }

        // ---- the search --------------------------------------------------------------------------------------

        private static TerrainLine Best(TerrainPicture p, int mask, float objX, float objZ, float axisX, float axisZ, float frontage)
        {
            var none = default(TerrainLine);
            if (p == null || !p.Ready) return none;
            if (!Unit(ref axisX, ref axisZ)) return none;

            int key0 = (int)Math.Round(objX / 10f), key1 = (int)Math.Round(objZ / 10f);
            int keyAxis = (int)Math.Round(Math.Atan2(axisZ, axisX) * (180.0 / Math.PI) / 10.0);
            int keyFrontage = (int)Math.Round(frontage / 50f);
            for (int i = 0; i < MemoSize; i++)
            {
                if (ReferenceEquals(_memo[i].Picture, p) && _memo[i].Mask == mask && _memo[i].X == key0 && _memo[i].Z == key1
                    && _memo[i].Axis == keyAxis && _memo[i].Frontage == keyFrontage)
                    return _memo[i].Line;
            }

            TerrainLine best = Search(p, mask, objX, objZ, axisX, axisZ, frontage);
            _memo[_memoNext] = new Memo { Picture = p, Mask = mask, X = key0, Z = key1, Axis = keyAxis, Frontage = keyFrontage, Line = best };
            _memoNext = (_memoNext + 1) % MemoSize;
            return best;
        }

        private static TerrainLine Search(TerrainPicture p, int mask, float objX, float objZ, float ax, float az, float frontage)
        {
            int cells = p.Columns * p.Rows;
            if (_stamp.Length < cells)
            {
                _stamp = new int[cells];
                _stack = new int[cells];
                _members = new int[cells];
                _generation = 0;
            }
            float perpX = -az, perpZ = ax;
            float cosLimit = (float)Math.Cos(MaxAngleDegreesOffPerpendicular * Math.PI / 180.0);
            float lateralLimit = Math.Max(frontage, MinLineMeters);
            int reach = (int)Math.Ceiling(LineSearchMeters / TerrainPicture.CellMeters);
            int cx0 = p.CellX(objX), cz0 = p.CellZ(objZ);

            TerrainLine best = default(TerrainLine);
            float bestScore = float.NegativeInfinity;

            for (int kind = 1; kind <= KindCount; kind++)
            {
                if ((mask & (1 << kind)) == 0) continue;
                _generation++;
                for (int dz = -reach; dz <= reach; dz++)
                {
                    int cz = cz0 + dz;
                    if (cz < 0 || cz >= p.Rows) continue;
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int cx = cx0 + dx;
                        if (cx < 0 || cx >= p.Columns) continue;
                        int index = cz * p.Columns + cx;
                        if (_stamp[index] == _generation || !InWindow(p, cx, cz, objX, objZ) || !IsMember(p, (TerrainLineKind)kind, cx, cz)) continue;

                        int count = Flood(p, (TerrainLineKind)kind, index, objX, objZ);
                        TerrainLine line = ToLine(p, (TerrainLineKind)kind, count, perpX, perpZ, cosLimit, ax, az);
                        if (!line.Has) continue;

                        // Own side, or at most 100 m in front of the objective along the threat axis; inside the frontage laterally.
                        float forward = (line.CenterX - objX) * ax + (line.CenterZ - objZ) * az;
                        float lateral = (line.CenterX - objX) * perpX + (line.CenterZ - objZ) * perpZ;
                        if (forward > MaxForwardMeters || Math.Abs(lateral) > lateralLimit) continue;

                        AnchorPoint(in line, ax, az, false, out float px, out float pz);
                        line.Strength = p.TerrainStrength(px, pz, ax, az);
                        if (line.Strength < MinLineStrength) continue;
                        float ox = line.CenterX - objX, oz = line.CenterZ - objZ;
                        float score = line.Strength - (float)Math.Sqrt(ox * ox + oz * oz) / 1000f;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = line;
                        }
                    }
                }
            }
            return best;
        }

        private static bool InWindow(TerrainPicture p, int cx, int cz, float objX, float objZ)
        {
            float dx = p.CenterX(cx) - objX, dz = p.CenterZ(cz) - objZ;
            return dx * dx + dz * dz <= LineSearchMeters * LineSearchMeters;
        }

        private static bool IsMember(TerrainPicture p, TerrainLineKind kind, int cx, int cz)
        {
            switch (kind)
            {
                case TerrainLineKind.Crest: return p.CrestProminence(cx, cz) > 0f;
                case TerrainLineKind.Wall: return p.Cover(cx, cz) == TerrainPicture.CoverWall;
                case TerrainLineKind.SunkenRoad: return p.Cover(cx, cz) == TerrainPicture.CoverSunkenRoad;
                case TerrainLineKind.Fence: return p.Cover(cx, cz) == TerrainPicture.CoverFence;
                case TerrainLineKind.Embankment:
                    int cover = p.Cover(cx, cz);
                    return cover == TerrainPicture.CoverRailroad || cover == TerrainPicture.CoverUnfinishedRailroad;
                case TerrainLineKind.River: return p.Id(cx, cz) == TerrainPicture.IdRiver;
                default: return false;
            }
        }

        // 8-neighbour flood fill of one run inside the window; the members are left in _members[0..count).
        private static int Flood(TerrainPicture p, TerrainLineKind kind, int start, float objX, float objZ)
        {
            int top = 0, count = 0;
            _stack[top++] = start;
            _stamp[start] = _generation;
            while (top > 0)
            {
                int index = _stack[--top];
                _members[count++] = index;
                int cx = index % p.Columns, cz = index / p.Columns;
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nz = cz + dz;
                    if (nz < 0 || nz >= p.Rows) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx;
                        if (nx < 0 || nx >= p.Columns) continue;
                        int n = nz * p.Columns + nx;
                        if (_stamp[n] == _generation || !InWindow(p, nx, nz, objX, objZ) || !IsMember(p, kind, nx, nz)) continue;
                        _stamp[n] = _generation;
                        _stack[top++] = n;
                    }
                }
            }
            return count;
        }

        // The run's principal axis and extent; Has = false when too short or not across the threat axis.
        private static TerrainLine ToLine(TerrainPicture p, TerrainLineKind kind, int count, float perpX, float perpZ, float cosLimit, float ax, float az)
        {
            var none = default(TerrainLine);
            if (count <= 0) return none;
            double sx = 0, sz = 0;
            for (int i = 0; i < count; i++)
            {
                int index = _members[i];
                sx += p.CenterX(index % p.Columns);
                sz += p.CenterZ(index / p.Columns);
            }
            double mx = sx / count, mz = sz / count;
            double sxx = 0, szz = 0, sxz = 0;
            for (int i = 0; i < count; i++)
            {
                int index = _members[i];
                double ex = p.CenterX(index % p.Columns) - mx, ez = p.CenterZ(index / p.Columns) - mz;
                sxx += ex * ex;
                szz += ez * ez;
                sxz += ex * ez;
            }
            double theta = 0.5 * Math.Atan2(2.0 * sxz, sxx - szz);
            float dx = (float)Math.Cos(theta), dz = (float)Math.Sin(theta);
            if (Math.Abs(dx * perpX + dz * perpZ) < cosLimit) return none;

            float tMin = float.MaxValue, tMax = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                int index = _members[i];
                float t = (float)((p.CenterX(index % p.Columns) - mx) * dx + (p.CenterZ(index / p.Columns) - mz) * dz);
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
            }
            if (tMax - tMin + TerrainPicture.CellMeters < MinLineMeters) return none;

            var line = new TerrainLine
            {
                Has = true,
                Kind = kind,
                X0 = (float)mx + dx * tMin, Z0 = (float)mz + dz * tMin,
                X1 = (float)mx + dx * tMax, Z1 = (float)mz + dz * tMax,
            };
            line.CenterX = (line.X0 + line.X1) * 0.5f;
            line.CenterZ = (line.Z0 + line.Z1) * 0.5f;
            return line;
        }

        private static bool Unit(ref float x, ref float z)
        {
            float len = (float)Math.Sqrt(x * x + z * z);
            if (!(len > 0.0001f)) return false;
            x /= len;
            z /= len;
            return true;
        }
    }
}
