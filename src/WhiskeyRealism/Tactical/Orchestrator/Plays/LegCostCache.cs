using System;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14 requirement-link-legs: the per-evaluation cache of leg costs, so a point's legs to the links of its region are sampled once
    // however many routes start or end there. A leg from a point to a link is keyed by (the point's 50-unit bucket, the link); a leg
    // between two points of one region by (bucket, bucket). The first point that fills an entry is the one priced, so two points in one
    // bucket share a cost to within one bucket's diagonal. A pooled open-addressing table of MaxLegCache entries, allocated once and
    // cleared when an evaluation starts; when it is full a leg is priced and not stored, and Overflow counts it.
    public sealed class LegCostCache
    {
        public const int MaxLegCache = 4096;
        public const float BucketMeters = 50f;
        private const int BucketOffset = 4096, BucketStride = 8192;

        private readonly int[] _a = new int[MaxLegCache];
        private readonly int[] _b = new int[MaxLegCache];
        private readonly float[] _cost = new float[MaxLegCache];
        private readonly bool[] _estimated = new bool[MaxLegCache];
        private readonly bool[] _used = new bool[MaxLegCache];
        private int _count;

        public int Overflow { get; private set; }
        public int Count => _count;

        /// <summary>The 50-unit bucket of a world point, non-negative for any point on the map.</summary>
        public static int BucketKey(float x, float z)
        {
            int bx = (int)Math.Floor(x / BucketMeters) + BucketOffset;
            int bz = (int)Math.Floor(z / BucketMeters) + BucketOffset;
            if (bx < 0) bx = 0;
            if (bz < 0) bz = 0;
            return bx * BucketStride + bz;
        }

        /// <summary>The key of a link as the far end of a leg; negative, so it never meets a bucket key.</summary>
        public static int LinkKey(int link) => -(link + 1);

        public void Clear()
        {
            Array.Clear(_used, 0, MaxLegCache);
            _count = 0;
            Overflow = 0;
        }

        public bool TryGet(int bucketKey, int otherKey, out float cost) => TryGet(bucketKey, otherKey, out cost, out _);

        public bool TryGet(int bucketKey, int otherKey, out float cost, out bool estimated)
        {
            int i = Find(bucketKey, otherKey, out bool found);
            if (found)
            {
                cost = _cost[i];
                estimated = _estimated[i];
                return true;
            }
            cost = 0f;
            estimated = false;
            return false;
        }

        /// <summary>Stores or replaces an entry (a confirmed leg replaces the estimate). When the table is full a new entry is dropped and counted.</summary>
        public void Put(int bucketKey, int otherKey, float cost, bool estimated = false)
        {
            int i = Find(bucketKey, otherKey, out bool found);
            if (!found)
            {
                if (_count >= MaxLegCache)
                {
                    Overflow++;
                    return;
                }
                _used[i] = true;
                _a[i] = bucketKey;
                _b[i] = otherKey;
                _count++;
            }
            _cost[i] = cost;
            _estimated[i] = estimated;
        }

        // The slot of the key, or the empty slot where it would go. On a full table a missing key probes the whole table and finds none
        // (found false); Put checks the count before it uses the slot.
        private int Find(int a, int b, out bool found)
        {
            int i = (int)(((uint)a * 73856093u ^ (uint)b * 19349663u) % MaxLegCache);
            for (int probes = 0; probes < MaxLegCache; probes++)
            {
                if (!_used[i])
                {
                    found = false;
                    return i;
                }
                if (_a[i] == a && _b[i] == b)
                {
                    found = true;
                    return i;
                }
                i = i + 1 == MaxLegCache ? 0 : i + 1;
            }
            found = false;
            return 0;
        }
    }
}
