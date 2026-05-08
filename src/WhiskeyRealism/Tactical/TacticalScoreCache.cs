using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public sealed class TacticalScoreCache<TValue>
    {
        public readonly struct Key : System.IEquatable<Key>
        {
            public readonly int UnitId;
            public readonly string Signature;
            public Key(int unitId, string signature) { UnitId = unitId; Signature = signature; }
            public bool Equals(Key other) => UnitId == other.UnitId && Signature == other.Signature;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => (UnitId * 397) ^ (Signature?.GetHashCode() ?? 0);
        }

        private readonly Dictionary<Key, TValue> entries = new Dictionary<Key, TValue>();

        public bool TryGet(Key key, out TValue value) => entries.TryGetValue(key, out value);
        public void Set(Key key, TValue value) => entries[key] = value;
        public void Clear() => entries.Clear();
        public int Count => entries.Count;
    }
}
