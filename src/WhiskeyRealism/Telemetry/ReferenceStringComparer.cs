using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WhiskeyRealism.Telemetry
{
    // Identity comparer for the hot-path name caches in front of an ordinal map
    // (TelemetryPerf aggregates, profiler markers, PatchFirstFire). Scope names and
    // first-fire ids are string literals, so the same text arrives as the same
    // instance on every call: one RuntimeHelpers hash and one reference compare,
    // instead of hashing ~40 characters. A cache keyed by this comparer is only ever
    // an alias table: the ordinal map stays the canonical one, so two equal strings
    // with different identities still resolve to the same entry, and each alias table
    // is capped so a name built per call cannot grow it without bound.
    internal sealed class ReferenceStringComparer : IEqualityComparer<string>
    {
        internal static readonly ReferenceStringComparer Instance = new ReferenceStringComparer();

        private ReferenceStringComparer()
        {
        }

        public bool Equals(string x, string y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(string value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
