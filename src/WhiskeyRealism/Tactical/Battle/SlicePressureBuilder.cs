// Slice, flank and closest-enemy pressure from perceived enemies (BC2a, spec §2). Pure.
//
// Replaces doctrine reads of vanilla's per-unit pressure, which has no fog filter for
// either side (UpdateUnitRangeFast 122545-122937):
// - enemystrengthwithinangle (122707-122732): bearing = Tools.GetAngle(own, enemy) + 180
//   (GetAngle 151304 is atan2(dx, dz) - 180), normalized to [0, 360); bin =
//   floor(bearing / aidefensiveslices) into an array of aidefensiveslices slots; adds
//   enemy.strength (no morale) when within aidefensivemaxrange. The divisor is the slot
//   count, so each bin is aidefensiveslices degrees wide: with the shipped 20 (battleprefs)
//   bins 0-17 fill and slots 18-19 stay empty. MostEnemiesAreOutsideAngle (4800) reads
//   bin j as centred on j*slices + slices/2, which agrees; GetBestFallbackPosition (140263)
//   uses 360/slices instead (vanilla inconsistency, not copied). Vanilla's
//   Min(360, Max(0, bin)) clamp cannot stop an overrun for other slice counts; this
//   builder clamps to the last slot. Vanilla also range-gates on the distance to the first
//   enemy beyond 15 m found in the scan (val2, 122696-122719); here the gate is the
//   enemy's own distance.
// - flankstrength (122799-122820, positions 122903-122907): vanilla tests enemies within
//   flankpositionshift (150, unitprefs) of points shifted flankpositionshift sideways from
//   the unit's regression line ends, subtracts strength x morale for enemies, adds 9999999
//   for friendly units of other groups, and slot 2 is the line centre. Here the slots are
//   left / right / rear points flankpositionshift from the unit's own position along
//   RotationY (UnitRecord has no line ends or enemy morale), and the value is positive
//   enemy pressure only. Consumers porting vanilla flankstrength thresholds must not
//   reuse them unchanged.
// Weights: Visible 1, Remembered RememberedPressureWeight x Confidence01. Reinforcement
// columns are off-map and excluded (they feed the snapshot's reinforcement totals).

using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>Vanilla pressure geometry the snapshot does not carry, read once per battle by
    /// the probe: GamePrefs.aidefensivemaxrange and flankpositionshift. The slice count is
    /// BattleSnapshot.SliceCount (GamePrefs.aidefensiveslices, set by the builder).</summary>
    public struct SliceGeometry
    {
        public float MaxRangeMeters;
        public float FlankShiftMeters;
    }

    public static class SlicePressureBuilder
    {
        public const float RememberedPressureWeight = 0.5f;

        // Slice accumulator lives on the stack; vanilla ships 20.
        private const int MaxSlices = 360;

        // Uniform-grid bound (perf slice F2): at most this many cells; a larger field
        // widens the cells instead (correct, only less selective).
        private const int MaxGridCells = 16384;

        // Cell padding: the 3x3 block around a unit must hold every observation any of the
        // unit's tests can accept (slice range, or a flank point's radius around a point one
        // shift away), with room for float rounding of the cell index.
        private const float CellPadFactor = 1.01f;
        private const float CellPadMeters = 1f;
        private const float RingBoundFactor = 0.99f;

        [ThreadStatic] private static GridScratch t_grid;

        private sealed class GridScratch
        {
            internal int[] Head = new int[64];
            internal int[] Count = new int[64];
            internal int[] Next = new int[BattleSnapshot.InitialCapacity];
            // Observation positions, compact for the dense path's box test; a skipped
            // observation (reinforcement) is placed where no box reaches.
            internal float[] X = new float[BattleSnapshot.InitialCapacity];
            internal float[] Z = new float[BattleSnapshot.InitialCapacity];
        }

        /// <summary>
        /// Writes slice, flank and closest-visible pressure for every own unit of the
        /// snapshot from its observations, overwriting each unit's row (safe to repeat).
        /// Allocation-free once the pooled grid fits.
        /// </summary>
        /// <remarks>
        /// Perf slice F2. Every test a pair can pass (slice range, a flank point's radius
        /// around a point one shift away) lies within reach = max(|range|, 2 x |shift|) of
        /// the unit, so a pair farther than that on either axis only matters for the
        /// closest visible enemy. Exact paths, chosen per unit by ChooseMode from cell counts:
        ///   - sparse: observations are bucketed once per build into a uniform XZ grid
        ///     (cell = reach, padded); the unit reads only the 3x3 cells around it, merged
        ///     in ascending observation index, and finds a closest enemy beyond that block
        ///     by rings, stopping once a ring cannot hold anything nearer;
        ///   - dense (the block holds over a quarter of the observations, few of them inside
        ///     the box): every observation in index order, skipping a pair outside the
        ///     padded box with two compares; when no visible enemy is safely inside the box,
        ///     one more pass finds the closest the way the full scan does;
        ///   - otherwise (most observations within reach, e.g. lines in contact) the plain
        ///     scan of every pair, whose cost is then mostly pairs that do pass a test.
        /// Either way every sum adds the same terms in the same order as a scan of all
        /// observations, and equal distances keep the lowest index, so the results are
        /// bit-identical. Non-finite geometry, or a unit with a non-finite position, takes
        /// the full scan. Equivalence: SlicePressureBuilderTests.
        /// </remarks>
        public static void Build(BattleSnapshot s, in SliceGeometry g)
        {
            if (s == null) return;
            int slices = Math.Min(s.SliceCount, MaxSlices);
            Span<float> slice = stackalloc float[Math.Max(slices, 1)];
            Span<int> cursor = stackalloc int[9];
            float maxRangeSq = g.MaxRangeMeters * g.MaxRangeMeters;
            float flankRadiusSq = g.FlankShiftMeters * g.FlankShiftMeters;
            UnitRecord[] own = s.Own;
            EnemyObservation[] obs = s.Observations;
            int obsCount = s.ObservationCount;

            bool grid = TryBuildGrid(obs, obsCount, g, maxRangeSq, flankRadiusSq,
                out GridScratch scratch, out float minX, out float minZ, out float cell, out int nx, out int nz, out int visibleCount);
            // Box half-width: no test can pass beyond it on either axis. A full scan uses an
            // infinite box (and the plain array walk).
            float safeClosestSq = cell * RingBoundFactor * (cell * RingBoundFactor);
            float[] xs = grid ? scratch.X : null, zs = grid ? scratch.Z : null;
            int[] next = grid ? scratch.Next : null;

            for (int o = 0; o < s.OwnCount; o++)
            {
                float ox = own[o].X;
                float oz = own[o].Z;
                float facing = own[o].RotationY;
                AnglePos(ox, oz, facing - 90f, g.FlankShiftMeters, out float leftX, out float leftZ);
                AnglePos(ox, oz, facing + 90f, g.FlankShiftMeters, out float rightX, out float rightZ);
                AnglePos(ox, oz, facing + 180f, g.FlankShiftMeters, out float rearX, out float rearZ);

                slice.Clear();
                float left = 0f, right = 0f, rear = 0f;
                int closest = -1;
                float closestSq = float.MaxValue;

                // Candidate source (ChooseMode): 0 every observation (full scan), 1 every
                // observation inside the box (dense), 2 the 3x3 block merged by index (sparse).
                int mode = 0;
                int cx = 0, cz = 0, lists = 0;
                if (grid && IsFinite(ox) && IsFinite(oz))
                {
                    cx = CellIndex(ox, minX, cell);
                    cz = CellIndex(oz, minZ, cell);
                    mode = ChooseMode(scratch, cx, cz, nx, nz, obsCount);
                    if (mode == 2) lists = BlockLists(scratch, cx, cz, nx, nz, cursor);
                }

                int k = -1;
                while (true)
                {
                    if (mode == 2)
                    {
                        if (lists == 0) break;
                        int pick = 0;
                        for (int i = 1; i < lists; i++)
                            if (cursor[i] < cursor[pick]) pick = i;
                        k = cursor[pick];
                        int following = next[k];
                        if (following >= 0) cursor[pick] = following;
                        else cursor[pick] = cursor[--lists];
                    }
                    else
                    {
                        if (++k >= obsCount) break;
                        if (mode == 1 && (Math.Abs(xs[k] - ox) > cell || Math.Abs(zs[k] - oz) > cell)) continue;
                    }

                    // The full scan's per-pair body (c3f35b1), unchanged.
                    if (obs[k].IsReinforcement) continue;
                    bool visible = obs[k].State == ObservationState.Visible;
                    float weighted = obs[k].Strength * (visible ? 1f : RememberedPressureWeight * obs[k].Confidence01);
                    float dx = obs[k].X - ox;
                    float dz = obs[k].Z - oz;
                    float distSq = dx * dx + dz * dz;

                    if (visible && distSq < closestSq)
                    {
                        closestSq = distSq;
                        closest = k;
                    }
                    if (slices > 0 && distSq <= maxRangeSq) slice[SliceIndex(Bearing(dx, dz), slices)] += weighted;
                    if (WithinSq(obs[k].X, obs[k].Z, leftX, leftZ, flankRadiusSq)) left += weighted;
                    if (WithinSq(obs[k].X, obs[k].Z, rightX, rightZ, flankRadiusSq)) right += weighted;
                    if (WithinSq(obs[k].X, obs[k].Z, rearX, rearZ, flankRadiusSq)) rear += weighted;
                }

                // A visible enemy safely inside the box or block is nearer than anything
                // skipped (skipped pairs lie beyond the box on one axis); otherwise find it
                // by rings (sparse) or by a closest-only scan (dense).
                if (mode != 0 && visibleCount > 0 && !(closest >= 0 && closestSq < safeClosestSq))
                {
                    if (mode == 2) ScanRings(scratch, obs, cx, cz, nx, nz, cell, ox, oz, ref closest, ref closestSq);
                    else ClosestByScan(obs, obsCount, ox, oz, out closest, out closestSq);
                }

                for (int j = 0; j < slices; j++) s.SetSlicePressure(o, j, slice[j]);
                s.SetFlankPressure(o, 0, left);   // 0 left, 1 right, 2 rear
                s.SetFlankPressure(o, 1, right);
                s.SetFlankPressure(o, 2, rear);
                s.SetClosestVisibleEnemy(o, closest, closest >= 0 ? (float)Math.Sqrt(closestSq) : 0f);
            }
        }

        // The full scan's closest visible enemy (first strictly nearest), from scratch.
        private static void ClosestByScan(EnemyObservation[] obs, int obsCount, float ox, float oz, out int closest, out float closestSq)
        {
            closest = -1;
            closestSq = float.MaxValue;
            for (int k = 0; k < obsCount; k++)
            {
                if (obs[k].IsReinforcement || obs[k].State != ObservationState.Visible) continue;
                float dx = obs[k].X - ox;
                float dz = obs[k].Z - oz;
                float distSq = dx * dx + dz * dz;
                if (distSq < closestSq)
                {
                    closestSq = distSq;
                    closest = k;
                }
            }
        }

        // Picks the cheaper exact path for one unit from the cell counts (all three give
        // identical results): the merged 3x3 block while it holds at most a quarter of the
        // observations; the box scan while the box (the centre cell, about half of each
        // edge cell, a quarter of each corner) is expected to hold under 30% of them, so
        // its skip test mostly predicts; otherwise the plain scan, which then costs no more
        // than the box test would save.
        private static int ChooseMode(GridScratch scratch, int cx, int cz, int nx, int nz, int obsCount)
        {
            int block = 0;
            float inBox = 0f;
            for (int z = Math.Max(0, cz - 1); z <= Math.Min(nz - 1, cz + 1); z++)
            {
                for (int x = Math.Max(0, cx - 1); x <= Math.Min(nx - 1, cx + 1); x++)
                {
                    int count = scratch.Count[z * nx + x];
                    block += count;
                    int edges = (x != cx ? 1 : 0) + (z != cz ? 1 : 0);
                    inBox += edges == 0 ? count : edges == 1 ? 0.5f * count : 0.25f * count;
                }
            }
            if (block * 4 <= obsCount) return 2;
            return inBox <= 0.3f * obsCount ? 1 : 0;
        }

        // Buckets the non-reinforcement observations with finite positions; each cell's
        // list ascends by observation index. False (full scan) when the geometry is not
        // finite or the field is degenerate. An excluded observation cannot pass any test:
        // a reinforcement is skipped by the scan, and a non-finite position gives a NaN or
        // infinite distance that no finite threshold accepts.
        private static bool TryBuildGrid(EnemyObservation[] obs, int obsCount, in SliceGeometry g, float maxRangeSq, float flankRadiusSq,
            out GridScratch scratch, out float minX, out float minZ, out float cell, out int nx, out int nz, out int visibleCount)
        {
            scratch = null;
            minX = minZ = cell = 0f;
            nx = nz = 0;
            visibleCount = 0;
            if (obsCount <= 0 || !IsFinite(maxRangeSq) || !IsFinite(flankRadiusSq)) return false;
            float reach = Math.Max(Math.Abs(g.MaxRangeMeters), 2f * Math.Abs(g.FlankShiftMeters));
            cell = reach * CellPadFactor + CellPadMeters;
            if (!IsFinite(cell)) return false;

            float maxX = float.MinValue, maxZ = float.MinValue;
            minX = float.MaxValue;
            minZ = float.MaxValue;
            int bucketed = 0;
            for (int k = 0; k < obsCount; k++)
            {
                if (!Bucketed(in obs[k])) continue;
                bucketed++;
                if (obs[k].X < minX) minX = obs[k].X;
                if (obs[k].X > maxX) maxX = obs[k].X;
                if (obs[k].Z < minZ) minZ = obs[k].Z;
                if (obs[k].Z > maxZ) maxZ = obs[k].Z;
                if (obs[k].State == ObservationState.Visible) visibleCount++;
            }
            if (bucketed == 0)
            {
                // Nothing can pass a test: every unit reads zero, no closest enemy.
                nx = nz = 0;
                visibleCount = 0;
                scratch = t_grid ?? (t_grid = new GridScratch());
                return true;
            }
            float spanX = maxX - minX, spanZ = maxZ - minZ;
            if (!IsFinite(spanX) || !IsFinite(spanZ)) return false;
            while (true)
            {
                double cellsX = Math.Floor(spanX / cell) + 1.0;
                double cellsZ = Math.Floor(spanZ / cell) + 1.0;
                if (cellsX * cellsZ <= MaxGridCells)
                {
                    nx = (int)cellsX;
                    nz = (int)cellsZ;
                    break;
                }
                cell *= 2f;
                if (!IsFinite(cell)) return false;
            }

            scratch = t_grid ?? (t_grid = new GridScratch());
            int cells = nx * nz;
            if (scratch.Head.Length < cells)
            {
                scratch.Head = new int[Math.Max(cells, scratch.Head.Length * 2)];
                scratch.Count = new int[scratch.Head.Length];
            }
            if (scratch.Next.Length < obsCount)
            {
                int size = Math.Max(obsCount, scratch.Next.Length * 2);
                scratch.Next = new int[size];
                scratch.X = new float[size];
                scratch.Z = new float[size];
            }
            int[] head = scratch.Head, next = scratch.Next, count = scratch.Count;
            for (int c = 0; c < cells; c++)
            {
                head[c] = -1;
                count[c] = 0;
            }
            // Descending insert at the head leaves every cell list ascending.
            for (int k = obsCount - 1; k >= 0; k--)
            {
                if (obs[k].IsReinforcement)
                {
                    scratch.X[k] = float.PositiveInfinity;
                    scratch.Z[k] = float.PositiveInfinity;
                    continue;
                }
                scratch.X[k] = obs[k].X;
                scratch.Z[k] = obs[k].Z;
                if (!Bucketed(in obs[k])) continue;
                int c = Clamp(CellIndex(obs[k].Z, minZ, cell), nz) * nx + Clamp(CellIndex(obs[k].X, minX, cell), nx);
                next[k] = head[c];
                head[c] = k;
                count[c]++;
            }
            return true;
        }

        private static bool Bucketed(in EnemyObservation o)
        {
            return !o.IsReinforcement && IsFinite(o.X) && IsFinite(o.Z);
        }

        // The heads of the non-empty cell lists of the 3x3 block around the unit's cell.
        private static int BlockLists(GridScratch scratch, int cx, int cz, int nx, int nz, Span<int> cursor)
        {
            int lists = 0;
            for (int z = Math.Max(0, cz - 1); z <= Math.Min(nz - 1, cz + 1); z++)
            {
                for (int x = Math.Max(0, cx - 1); x <= Math.Min(nx - 1, cx + 1); x++)
                {
                    int first = scratch.Head[z * nx + x];
                    if (first >= 0) cursor[lists++] = first;
                }
            }
            return lists;
        }

        // Closest visible enemy outside the 3x3 block: Chebyshev rings r = 2, 3, ... An
        // observation in ring r lies at least (r - 1) cells from the unit, so the search
        // stops once that bound (shrunk for rounding) exceeds the best distance. Equal
        // distances keep the lowest observation index, as the full scan does.
        private static void ScanRings(GridScratch scratch, EnemyObservation[] obs, int cx, int cz, int nx, int nz, float cell,
            float ox, float oz, ref int closest, ref float closestSq)
        {
            if (nx == 0) return;
            int maxRing = Math.Max(Math.Max(Math.Abs(cx), Math.Abs(nx - 1 - cx)), Math.Max(Math.Abs(cz), Math.Abs(nz - 1 - cz)));
            // Rings nearer than the grid hold no cell (a unit outside the observed field).
            int outside = Math.Max(cx < 0 ? -cx : cx >= nx ? cx - (nx - 1) : 0, cz < 0 ? -cz : cz >= nz ? cz - (nz - 1) : 0);
            for (int r = Math.Max(2, outside); r <= maxRing; r++)
            {
                if (closest >= 0)
                {
                    float bound = (r - 1) * cell * RingBoundFactor;
                    if (bound * bound > closestSq) return;
                }
                int x0 = cx - r, x1 = cx + r, z0 = cz - r, z1 = cz + r;
                int xa = Math.Max(0, x0), xb = Math.Min(nx - 1, x1);
                if (z0 >= 0) for (int x = xa; x <= xb; x++) RingCell(scratch, obs, z0 * nx + x, ox, oz, ref closest, ref closestSq);
                if (z1 < nz && z1 != z0) for (int x = xa; x <= xb; x++) RingCell(scratch, obs, z1 * nx + x, ox, oz, ref closest, ref closestSq);
                int za = Math.Max(0, z0 + 1), zb = Math.Min(nz - 1, z1 - 1);
                if (x0 >= 0) for (int z = za; z <= zb; z++) RingCell(scratch, obs, z * nx + x0, ox, oz, ref closest, ref closestSq);
                if (x1 < nx && x1 != x0) for (int z = za; z <= zb; z++) RingCell(scratch, obs, z * nx + x1, ox, oz, ref closest, ref closestSq);
            }
        }

        private static void RingCell(GridScratch scratch, EnemyObservation[] obs, int c, float ox, float oz, ref int closest, ref float closestSq)
        {
            for (int k = scratch.Head[c]; k >= 0; k = scratch.Next[k])
            {
                if (obs[k].State != ObservationState.Visible) continue;
                float dx = obs[k].X - ox;
                float dz = obs[k].Z - oz;
                float distSq = dx * dx + dz * dz;
                if (distSq < closestSq || (distSq == closestSq && closest >= 0 && k < closest))
                {
                    closestSq = distSq;
                    closest = k;
                }
            }
        }

        private static int CellIndex(float v, float min, float cell)
        {
            double index = Math.Floor(((double)v - min) / cell);
            if (index < int.MinValue / 4) return int.MinValue / 4;
            if (index > int.MaxValue / 4) return int.MaxValue / 4;
            return (int)index;
        }

        private static int Clamp(int index, int count)
        {
            return index < 0 ? 0 : index >= count ? count - 1 : index;
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        /// <summary>Bearing from a unit to an offset (dx, dz), degrees clockwise from +z in
        /// [0, 360): vanilla's Tools.GetAngle(own, enemy) + 180, normalized (122726-122731).</summary>
        public static float Bearing(float dx, float dz)
        {
            float degrees = (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
            if (degrees < 0f) degrees += 360f;
            if (degrees >= 360f) degrees -= 360f;
            return degrees;
        }

        /// <summary>Vanilla bin floor(bearing / sliceCount) (122732), clamped to the array.</summary>
        public static int SliceIndex(float bearingDegrees, int sliceCount)
        {
            if (sliceCount <= 0) return 0;
            int index = (int)Math.Floor(bearingDegrees / sliceCount);
            if (index < 0) return 0;
            return index >= sliceCount ? sliceCount - 1 : index;
        }

        // Tools.GetAnglePos (151207): angle in vanilla facing degrees (0 = +z, 90 = +x).
        private static void AnglePos(float x, float z, float angleDegrees, float distance, out float px, out float pz)
        {
            double radians = (angleDegrees - 90f) * (Math.PI / 180.0);
            px = x + (float)(Math.Cos(radians) * distance);
            pz = z - (float)(Math.Sin(radians) * distance);
        }

        private static bool WithinSq(float x, float z, float cx, float cz, float radiusSq)
        {
            float dx = x - cx;
            float dz = z - cz;
            return dx * dx + dz * dz <= radiusSq;
        }
    }
}
