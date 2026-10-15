using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// The flank sectors and range of vanilla's UpdateFlanking, read once per battle:
    /// GamePrefs.fireangles {300, 60, 225, 135} (unitprefs.txt, "Fireangle front" and
    /// "back") and distancetoenemyforoutflank (60 m).
    /// </summary>
    public struct FlankGeometry
    {
        public float FrontFrom;   // fireangles[0]
        public float FrontTo;     // fireangles[1]
        public float LeftFrom;    // fireangles[2]
        public float RightTo;     // fireangles[3]
        public float RangeMeters; // distancetoenemyforoutflank
    }

    // Regiment.outflanked from perceived units (BC10 decision 3). Pure.
    //
    // Vanilla's UpdateFlanking (122959-123175) takes, per regiment, the nearest enemy of
    // its enemyinrangereg in each of three sectors relative to its facing (left, right,
    // rear: [fireangles[2], fireangles[0]], [fireangles[1], fireangles[3]],
    // [fireangles[2], fireangles[3]], tested with Tools.IsWithinAngle on bearing - facing),
    // skipping skirmishers, scouts and enemies in melee, and only within
    // distancetoenemyforoutflank. An own regiment nearer than that enemy in the same sector
    // clears it (123094-123111). The code is the or of left 1, right 2, rear 4. A
    // regiment in fallback (movementmode 2) faces the other way (122969-122971). The range
    // lists have no fog filter for the AI side (122650), so a flanker hidden in woods 40 m
    // away counts. Here the threats are the Visible enemy regiments (the snapshot's
    // records of enemies it perceives) and the screens are the regiments of the unit's own
    // side the snapshot lists: own units read the enemy rows they see; a Visible enemy
    // row reads our regiments as its threats and the other Visible enemy rows as its
    // screens. A hidden enemy row reads 0.
    //
    // Cost: threats and screens come from a bucket grid of RangeMeters cells over the
    // regiments of both sides, built once per call from pooled arrays (sorted keys, no
    // dictionary), so each regiment reads its own cell and the eight around it.
    public static class PerceivedFlanking
    {
        private const int CellOffset = 16384;
        private const int CellStride = 32768;

        // Pooled, grow-only. One call at a time (the main thread builds one snapshot at a time).
        private static float[] _x = new float[0], _z = new float[0], _facing = new float[0];
        private static byte[] _kind = new byte[0];      // bit0 enemy side, bit1 fallback, bit2 may threaten, bit3 subject
        private static int[] _slot = new int[0];        // index into Own or EnemyRecords
        private static int[] _key = new int[0];
        private static int[] _order = new int[0];
        private static float[] _threatX = new float[0], _threatZ = new float[0], _screenX = new float[0], _screenZ = new float[0];

        private const byte EnemySide = 1, Fallback = 2, MayThreaten = 4;

        /// <summary>
        /// The code 0-7 for a regiment at (cx, cz) facing <paramref name="facing"/>, from explicit
        /// threat and screen positions: UpdateFlanking 122959-123175 with its inputs given.
        /// </summary>
        public static int Code(float cx, float cz, float facing, bool fallbackMode,
            ReadOnlySpan<float> threatX, ReadOnlySpan<float> threatZ, int threats,
            ReadOnlySpan<float> screenX, ReadOnlySpan<float> screenZ, int screens, in FlankGeometry g)
        {
            if (fallbackMode) facing += 180f;
            float leftThreat = float.MaxValue, rightThreat = float.MaxValue, rearThreat = float.MaxValue;
            for (int i = 0; i < threats && i < threatX.Length && i < threatZ.Length; i++)
            {
                float dx = threatX[i] - cx;
                float dz = threatZ[i] - cz;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (!(d < g.RangeMeters)) continue;
                float rel = Bearing(dx, dz) - facing;
                if (d < leftThreat && IsWithinAngle(rel, g.LeftFrom, g.FrontFrom)) leftThreat = d;
                if (d < rightThreat && IsWithinAngle(rel, g.FrontTo, g.RightTo)) rightThreat = d;
                if (d < rearThreat && IsWithinAngle(rel, g.LeftFrom, g.RightTo)) rearThreat = d;
            }

            if (leftThreat == float.MaxValue && rightThreat == float.MaxValue && rearThreat == float.MaxValue) return 0;

            float leftScreen = float.MaxValue, rightScreen = float.MaxValue, rearScreen = float.MaxValue;
            for (int i = 0; i < screens && i < screenX.Length && i < screenZ.Length; i++)
            {
                float dx = screenX[i] - cx;
                float dz = screenZ[i] - cz;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (!(d < g.RangeMeters)) continue;
                float rel = Bearing(dx, dz) - facing;
                if (d < leftScreen && IsWithinAngle(rel, g.LeftFrom, g.FrontFrom)) leftScreen = d;
                if (d < rightScreen && IsWithinAngle(rel, g.FrontTo, g.RightTo)) rightScreen = d;
                if (d < rearScreen && IsWithinAngle(rel, g.LeftFrom, g.RightTo)) rearScreen = d;
            }

            // A sector stays flagged unless a friend stands strictly nearer than its nearest enemy.
            int code = 0;
            if (leftThreat != float.MaxValue && !(leftScreen < leftThreat)) code |= 1;
            if (rightThreat != float.MaxValue && !(rightScreen < rightThreat)) code |= 2;
            if (rearThreat != float.MaxValue && !(rearScreen < rearThreat)) code |= 4;
            return code;
        }

        /// <summary>
        /// Fills UnitRecord.Outflanked of the snapshot's own regiments and of its Visible
        /// enemy rows (every other row reads 0). Allocation-free after warm-up.
        /// </summary>
        public static void Apply(BattleSnapshot s, in FlankGeometry g)
        {
            if (s == null) return;
            UnitRecord[] own = s.Own;
            UnitRecord[] enemy = s.EnemyRecords;
            for (int i = 0; i < s.OwnCount; i++) own[i].Outflanked = 0;
            for (int e = 0; e < s.EnemyRecordCount; e++) enemy[e].Outflanked = 0;
            if (!(g.RangeMeters > 0f)) return;

            int capacity = s.OwnCount + s.EnemyRecordCount;
            Ensure(capacity);
            int n = 0;
            for (int i = 0; i < s.OwnCount; i++)
                if (Participates(own[i])) n = Add(n, own[i], i, 0, g);
            for (int e = 0; e < s.EnemyRecordCount; e++)
                if (Participates(enemy[e]) && s.IsVisible(enemy[e].InstanceId)) n = Add(n, enemy[e], e, EnemySide, g);
            if (n < 2) return;

            Array.Sort(_key, _order, 0, n);

            for (int p = 0; p < n; p++)
            {
                int u = _order[p];
                bool unitEnemy = (_kind[u] & EnemySide) != 0;
                int threats = 0, screens = 0;
                int ucx = _key[p] / CellStride - CellOffset;
                int ucz = _key[p] % CellStride - CellOffset;
                for (int dcx = -1; dcx <= 1; dcx++)
                {
                    for (int dcz = -1; dcz <= 1; dcz++)
                    {
                        int key = (ucx + dcx + CellOffset) * CellStride + (ucz + dcz + CellOffset);
                        for (int q = LowerBound(key, n); q < n && _key[q] == key; q++)
                        {
                            int v = _order[q];
                            if (v == u) continue;
                            float dx = _x[v] - _x[u];
                            float dz = _z[v] - _z[u];
                            if (dx * dx + dz * dz >= g.RangeMeters * g.RangeMeters) continue;
                            bool vEnemy = (_kind[v] & EnemySide) != 0;
                            if (vEnemy == unitEnemy)
                            {
                                _screenX[screens] = _x[v];
                                _screenZ[screens] = _z[v];
                                screens++;
                            }
                            else if ((_kind[v] & MayThreaten) != 0)
                            {
                                _threatX[threats] = _x[v];
                                _threatZ[threats] = _z[v];
                                threats++;
                            }
                        }
                    }
                }

                if (threats == 0) continue;
                int code = Code(_x[u], _z[u], _facing[u], (_kind[u] & Fallback) != 0, _threatX, _threatZ, threats, _screenX, _screenZ, screens, in g);
                if (unitEnemy) enemy[_slot[u]].Outflanked = code;
                else own[_slot[u]].Outflanked = code;
            }
        }

        // A regiment in the field: alive, active, not routed, a combat unit type (not a courier).
        private static bool Participates(in UnitRecord r)
        {
            UnitFlags f = r.Flags;
            if ((f & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active) || (f & UnitFlags.Routed) != 0) return false;
            if (float.IsNaN(r.X) || float.IsNaN(r.Z) || float.IsInfinity(r.X) || float.IsInfinity(r.Z)) return false;
            return r.UnitTyp <= TacticalUnitType.MaxCombat && r.UnitTyp != TacticalUnitType.Courier;
        }

        private static int Add(int n, in UnitRecord r, int slot, byte side, in FlankGeometry g)
        {
            byte kind = side;
            if (r.MovementMode == 2) kind |= Fallback;
            // Skirmishers, scouts and units in melee are never a flanker (122993).
            if (r.UnitTyp != TacticalUnitType.Skirmisher && r.UnitTyp != TacticalUnitType.Scout && (r.Flags & UnitFlags.InMelee) == 0)
                kind |= MayThreaten;
            _x[n] = r.X;
            _z[n] = r.Z;
            _facing[n] = r.RotationY;
            _kind[n] = kind;
            _slot[n] = slot;
            _order[n] = n;
            int cx = (int)Math.Floor(r.X / g.RangeMeters);
            int cz = (int)Math.Floor(r.Z / g.RangeMeters);
            _key[n] = (cx + CellOffset) * CellStride + (cz + CellOffset);
            return n + 1;
        }

        private static int LowerBound(int key, int n)
        {
            int lo = 0, hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_key[mid] < key) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static void Ensure(int capacity)
        {
            if (_x.Length >= capacity) return;
            int size = Math.Max(capacity, Math.Max(512, _x.Length * 2));
            _x = new float[size];
            _z = new float[size];
            _facing = new float[size];
            _kind = new byte[size];
            _slot = new int[size];
            _key = new int[size];
            _order = new int[size];
            _threatX = new float[size];
            _threatZ = new float[size];
            _screenX = new float[size];
            _screenZ = new float[size];
        }

        // Tools.GetAngle(a, b) + 180 (151304): degrees clockwise from +z, in (-180, 180].
        private static float Bearing(float dx, float dz)
        {
            return (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
        }

        // Tools.IsWithinAngle (150629): angles normalised to [0, 360); true when the angle lies
        // within the (shorter) arc between the two bounds, both ends included.
        internal static bool IsWithinAngle(float angle, float min, float max)
        {
            angle = Normalize(angle);
            min = Normalize(min);
            max = Normalize(max);
            float range = Math.Abs(max - min);
            if (range > 180f) range = 360f - range;
            float toMin = Math.Abs(angle - min);
            if (toMin > 180f) toMin = 360f - toMin;
            if (toMin > range) return false;
            float toMax = Math.Abs(angle - max);
            if (toMax > 180f) toMax = 360f - toMax;
            return toMax <= range;
        }

        private static float Normalize(float angle)
        {
            while (angle < 0f) angle += 360f;
            while (angle >= 360f) angle -= 360f;
            return angle;
        }
    }
}
