using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// A telemetry row's throttle, decided before anything is built (pure): a row is due when its key has not been written for the throttle's seconds.
    /// The key is a number mixed from the row's identity (<see cref="Key"/>), so the steady state, a row that is not due, allocates nothing: no string
    /// and no closure exists until the caller has been told the row is due. The table is bounded: past <see cref="MaxKeys"/> it is cleared (a row may
    /// then be written once more than its throttle says). A mixed key may collide with another's; a collision suppresses a row for one throttle at worst.
    /// </summary>
    internal sealed class RowThrottle
    {
        public const int MaxKeys = 1024;

        private readonly Dictionary<long, float> _lastAt = new Dictionary<long, float>();
        private readonly float _seconds;

        public RowThrottle(float seconds) => _seconds = seconds;

        public void Clear() => _lastAt.Clear();

        /// <summary>The keys held (never more than <see cref="MaxKeys"/>).</summary>
        public int Count => _lastAt.Count;

        /// <summary>True, and the row is recorded as written now, when it is due.</summary>
        public bool Due(long key, float nowSeconds)
        {
            if (_lastAt.TryGetValue(key, out float last) && nowSeconds - last < _seconds) return false;
            if (_lastAt.Count >= MaxKeys && !_lastAt.ContainsKey(key)) _lastAt.Clear();
            _lastAt[key] = nowSeconds;
            return true;
        }

        public static long Key(int a, int b = 0, int c = 0, int d = 0, int e = 0)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                h = (h ^ (uint)a) * 1099511628211UL;
                h = (h ^ (uint)b) * 1099511628211UL;
                h = (h ^ (uint)c) * 1099511628211UL;
                h = (h ^ (uint)d) * 1099511628211UL;
                h = (h ^ (uint)e) * 1099511628211UL;
                return (long)h;
            }
        }
    }
}
