using System;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// Single-link clustering of points on the ground (extracted from BC13's <c>OpeningDetector.ClusterPass</c> for BC14): two
    /// points are in one cluster when a chain of points each within <c>linkMeters</c> of the next joins them. Points are hashed
    /// into cells of <c>linkMeters</c> so each point only meets the 3 x 3 cells around it, then joined with a union-find whose root
    /// is always the lowest index. Labels are numbered in order of each cluster's lowest member index. The scratch is pooled and
    /// grows at first use; a call allocates nothing once the arrays are large enough.
    /// </summary>
    public sealed class SingleLinkClusters
    {
        private const int CellOffset = 16384, CellStride = 32768;

        private int[] _key = new int[256], _order = new int[256], _parent = new int[256], _labelOfRoot = new int[256];

        /// <summary>
        /// Writes each of the first <paramref name="count"/> points' cluster number into <paramref name="labels"/> and returns the
        /// number of clusters. <paramref name="labels"/> must hold at least <paramref name="count"/> entries.
        /// </summary>
        public int Label(ReadOnlySpan<float> xs, ReadOnlySpan<float> zs, int count, float linkMeters, Span<int> labels)
        {
            if (count <= 0) return 0;
            if (xs.Length < count || zs.Length < count || labels.Length < count)
                throw new ArgumentException("xs, zs and labels must each hold count entries");
            if (!(linkMeters > 0f)) throw new ArgumentOutOfRangeException(nameof(linkMeters));
            if (_key.Length < count)
            {
                int size = Math.Max(count, _key.Length * 2);
                _key = new int[size];
                _order = new int[size];
                _parent = new int[size];
                _labelOfRoot = new int[size];
            }

            for (int i = 0; i < count; i++)
            {
                int cx = (int)Math.Floor(xs[i] / linkMeters);
                int cz = (int)Math.Floor(zs[i] / linkMeters);
                _key[i] = (cx + CellOffset) * CellStride + (cz + CellOffset);
                _order[i] = i;
                _parent[i] = i;
                _labelOfRoot[i] = -1;
            }
            Array.Sort(_key, _order, 0, count);

            float link2 = linkMeters * linkMeters;
            for (int p = 0; p < count; p++)
            {
                int u = _order[p];
                int ucx = _key[p] / CellStride - CellOffset;
                int ucz = _key[p] % CellStride - CellOffset;
                for (int dcx = -1; dcx <= 1; dcx++)
                {
                    for (int dcz = -1; dcz <= 1; dcz++)
                    {
                        int key = (ucx + dcx + CellOffset) * CellStride + (ucz + dcz + CellOffset);
                        for (int q = LowerBound(key, count); q < count && _key[q] == key; q++)
                        {
                            int v = _order[q];
                            if (v <= u) continue;
                            float dx = xs[v] - xs[u], dz = zs[v] - zs[u];
                            if (dx * dx + dz * dz <= link2) Union(u, v);
                        }
                    }
                }
            }

            int clusters = 0;
            for (int i = 0; i < count; i++)
            {
                int root = Find(i);
                if (_labelOfRoot[root] < 0) _labelOfRoot[root] = clusters++;
                labels[i] = _labelOfRoot[root];
            }
            return clusters;
        }

        private int Find(int i)
        {
            while (_parent[i] != i)
            {
                _parent[i] = _parent[_parent[i]];
                i = _parent[i];
            }
            return i;
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _parent[ra < rb ? rb : ra] = ra < rb ? ra : rb;
        }

        private int LowerBound(int key, int count)
        {
            int lo = 0, hi = count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_key[mid] < key) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
