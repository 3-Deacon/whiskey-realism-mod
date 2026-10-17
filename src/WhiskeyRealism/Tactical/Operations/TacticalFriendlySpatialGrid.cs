using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Operations
{
    // #61 perf (2026-10-14): friendly positions bucketed once per Postfix per
    // alliance, so the path-quality friendly-blocker estimate queries nearby
    // cells instead of walking BattleUnits.completeunitlist per candidate path
    // (3 candidates per nav plan, plus fire-control checks).
    public sealed class TacticalFriendlySpatialGrid
    {
        public const float CorridorLateral = 120f;
        public const float CorridorAhead = 120f;
        public const float CorridorMinForward = 30f;
        private const float CellSize = 240f;

        private readonly Dictionary<long, List<Entry>> _cells = new Dictionary<long, List<Entry>>();

        private readonly struct Entry
        {
            public Entry(int id, float x, float z)
            {
                Id = id;
                X = x;
                Z = z;
            }

            public int Id { get; }
            public float X { get; }
            public float Z { get; }
        }

        public int Count { get; private set; }

        public void Clear()
        {
            _cells.Clear();
            Count = 0;
        }

        public void Add(int id, float x, float z)
        {
            if (!IsFinite(x) || !IsFinite(z)) return;
            long key = Key(Cell(x), Cell(z));
            if (!_cells.TryGetValue(key, out List<Entry> list))
            {
                list = new List<Entry>(4);
                _cells[key] = list;
            }
            list.Add(new Entry(id, x, z));
            Count++;
        }

        // 0..1: how badly a friendly blocks the corridor from own to candidate
        // (same scoring the executor used: proximity to the candidate within
        // 80..300 m, or lateral closeness to the axis).
        public float EstimateFrontBlocker(int selfId, float ownX, float ownZ, float candX, float candZ)
        {
            float ax = candX - ownX;
            float az = candZ - ownZ;
            float len = (float)Math.Sqrt(ax * ax + az * az);
            if (len < 1f || Count == 0) return 0f;
            ax /= len;
            az /= len;

            float endX = candX + ax * CorridorAhead;
            float endZ = candZ + az * CorridorAhead;
            int minCx = Cell(Math.Min(ownX, endX) - CorridorLateral);
            int maxCx = Cell(Math.Max(ownX, endX) + CorridorLateral);
            int minCz = Cell(Math.Min(ownZ, endZ) - CorridorLateral);
            int maxCz = Cell(Math.Max(ownZ, endZ) + CorridorLateral);

            float worst = 0f;
            for (int cx = minCx; cx <= maxCx; cx++)
            {
                for (int cz = minCz; cz <= maxCz; cz++)
                {
                    if (!_cells.TryGetValue(Key(cx, cz), out List<Entry> list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        Entry e = list[i];
                        if (e.Id == selfId) continue;
                        float rx = e.X - ownX;
                        float rz = e.Z - ownZ;
                        float forward = rx * ax + rz * az;
                        if (forward < CorridorMinForward || forward > len + CorridorAhead) continue;
                        float lx = rx - ax * forward;
                        float lz = rz - az * forward;
                        float lateral = (float)Math.Sqrt(lx * lx + lz * lz);
                        if (lateral > CorridorLateral) continue;
                        float dx = e.X - candX;
                        float dz = e.Z - candZ;
                        float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                        float proximity = distance <= 80f ? 1f : Math.Max(0f, 1f - ((distance - 80f) / 220f));
                        float lateralBlock = 1f - Math.Min(1f, lateral / CorridorLateral);
                        worst = Math.Max(worst, Math.Max(proximity, lateralBlock * 0.8f));
                    }
                }
            }

            return Math.Min(1f, worst);
        }

        private static int Cell(float v)
        {
            return (int)Math.Floor(v / CellSize);
        }

        private static long Key(int cx, int cz)
        {
            return ((long)cx << 32) ^ (uint)cz;
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }

    // #61 perf: NavMesh path-quality sampling (3 CalculatePath per plan) is
    // budgeted per Postfix. Groups past the budget defer to the next call.
    //
    // BC15 decision 7: the walk is by instance id. The caller keeps the groups sorted by ascending instance id and starts each tick at
    // StartIndex, the first id above the last one that got a plan, so a roster that changes between ticks neither skips nor repeats a group
    // (an index cursor modulo the walked count shifted with every death and arrival). With D groups waiting and a budget of B, every waiting
    // group is planned within ceil(D / B) ticks; a group that is not due is not walked and takes no plan.
    public sealed class NavPlanBudget
    {
        private readonly int _perApply;
        private int _consumed;
        private int _lastPlannedId;
        private bool _hasLastPlanned;

        public NavPlanBudget(int perApply)
        {
            _perApply = Math.Max(1, perApply);
        }

        /// <summary>The instance id of the last group that got a plan; meaningful once one has (see <see cref="StartIndex"/>).</summary>
        public int LastPlannedId => _lastPlannedId;

        /// <summary>A plan for no particular group (the road-route budget): spends the budget, moves no cursor.</summary>
        public bool TryConsume()
        {
            if (_consumed >= _perApply) return false;
            _consumed++;
            return true;
        }

        /// <summary>A plan for group <paramref name="groupId"/>: spends the budget and moves the cursor to the group.</summary>
        public bool TryConsume(int groupId)
        {
            if (!TryConsume()) return false;
            _lastPlannedId = groupId;
            _hasLastPlanned = true;
            return true;
        }

        public void NextApply()
        {
            _consumed = 0;
        }

        /// <summary>
        /// The index in <paramref name="sortedIds"/> (ascending, <paramref name="count"/> entries) to start the walk at: the first id above
        /// the last planned one, or 0 with no plan yet or when every id is at or below it.
        /// </summary>
        public int StartIndex(int[] sortedIds, int count)
        {
            if (!_hasLastPlanned || sortedIds == null || count <= 0) return 0;
            int low = 0, high = count;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (sortedIds[mid] <= _lastPlannedId) low = mid + 1;
                else high = mid;
            }
            return low < count ? low : 0;
        }
    }
}
