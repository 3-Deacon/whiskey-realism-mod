using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    // Perf slice F1: the observer / decision-matrix telemetry caches. Pure; the runtime
    // (Patches/TacticalObserverPatch) supplies the keys.

    /// <summary>
    /// What a side-wide odds evaluation reads, as identities: the AIBattle, the frame, the
    /// side's battle snapshot and its tick, and the unit list with its length. Within one
    /// frame vanilla's AI tasks run back to back (UpdateAITasks 5857-5872) and none of them
    /// moves units or rebuilds the range lists the odds read, so an equal key means an equal
    /// evaluation; the snapshot rebuild inside CheckGlobalAIStrategy changes the key.
    /// </summary>
    public readonly struct OddsMemoKey : IEquatable<OddsMemoKey>
    {
        public OddsMemoKey(object battle, int frame, object snapshot, long snapshotTick, object units, int unitCount)
        {
            Battle = battle;
            Frame = frame;
            Snapshot = snapshot;
            SnapshotTick = snapshotTick;
            Units = units;
            UnitCount = unitCount;
        }

        public object Battle { get; }
        public int Frame { get; }
        public object Snapshot { get; }
        public long SnapshotTick { get; }
        public object Units { get; }
        public int UnitCount { get; }

        public bool Equals(OddsMemoKey other)
        {
            return ReferenceEquals(Battle, other.Battle) && Frame == other.Frame && ReferenceEquals(Snapshot, other.Snapshot)
                && SnapshotTick == other.SnapshotTick && ReferenceEquals(Units, other.Units) && UnitCount == other.UnitCount;
        }

        public override bool Equals(object obj) => obj is OddsMemoKey other && Equals(other);

        public override int GetHashCode() => Frame ^ (int)SnapshotTick ^ UnitCount;
    }

    /// <summary>
    /// Last side-wide value per AIBattle (vanilla runs at most three, aibattle[0..2]), by
    /// key; a different key for the same battle replaces its slot. Holds references only
    /// until the next key for that battle; the owner clears it per battle.
    /// </summary>
    public sealed class OddsMemo<T>
    {
        public const int Slots = 4;

        private readonly OddsMemoKey[] _keys = new OddsMemoKey[Slots];
        private readonly T[] _values = new T[Slots];
        private readonly bool[] _used = new bool[Slots];
        private int _next;

        public bool TryGet(in OddsMemoKey key, out T value)
        {
            for (int i = 0; i < Slots; i++)
            {
                if (_used[i] && _keys[i].Equals(key))
                {
                    value = _values[i];
                    return true;
                }
            }
            value = default(T);
            return false;
        }

        public void Store(in OddsMemoKey key, T value)
        {
            int slot = -1;
            for (int i = 0; i < Slots && slot < 0; i++)
                if (_used[i] && ReferenceEquals(_keys[i].Battle, key.Battle)) slot = i;
            if (slot < 0)
            {
                slot = _next;
                _next = (_next + 1) % Slots;
            }
            _keys[slot] = key;
            _values[slot] = value;
            _used[slot] = true;
        }

        public void Clear()
        {
            for (int i = 0; i < Slots; i++)
            {
                _keys[i] = default(OddsMemoKey);
                _values[i] = default(T);
                _used[i] = false;
            }
            _next = 0;
        }
    }

    /// <summary>
    /// Decision-matrix group rows are written when the row's decision inputs changed since
    /// that group's last row, or after a keep-alive; an unchanged row is not even built.
    /// Real time is a telemetry throttle only (AGENTS.md § Clocks).
    /// </summary>
    public sealed class MatrixRowDedupe
    {
        public const float KeepAliveSeconds = 30f;

        private readonly Dictionary<int, int> _signature = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _emittedAt = new Dictionary<int, float>();

        public bool ShouldEmit(int groupId, int signature, float nowSeconds)
        {
            if (_signature.TryGetValue(groupId, out int last) && last == signature
                && _emittedAt.TryGetValue(groupId, out float at) && nowSeconds - at < KeepAliveSeconds)
                return false;
            _signature[groupId] = signature;
            _emittedAt[groupId] = nowSeconds;
            return true;
        }

        public void Clear()
        {
            _signature.Clear();
            _emittedAt.Clear();
        }

        /// <summary>The 0.5 buckets TacticalTelemetry.DecisionInputSignature writes floats in.</summary>
        public static int Bucket(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            double bucket = Math.Round(value * 2f);
            // A negative value rounding to zero is written "-0.0": its own bucket.
            if (bucket == 0.0 && double.IsNegative(bucket)) return int.MinValue;
            if (bucket > int.MaxValue) return int.MaxValue;
            if (bucket <= int.MinValue) return int.MinValue + 1;
            return (int)bucket;
        }

        public static int Combine(int hash, int value)
        {
            unchecked { return hash * 31 + value; }
        }
    }

    /// <summary>
    /// One decision matrix per AIBattle per frame: the side-wide events of the frame are
    /// collected, and the matrix is written once, at the first event that may write it.
    /// </summary>
    public sealed class MatrixCycleGate
    {
        private readonly List<object> _battles = new List<object>(4);
        private readonly List<int> _frames = new List<int>(4);
        private readonly List<int> _events = new List<int>(4);
        private readonly List<bool> _written = new List<bool>(4);

        /// <summary>Records the event for the battle's frame; returns the frame's event mask so far.</summary>
        public int Note(object battle, int frame, int eventBit)
        {
            int i = Slot(battle, frame);
            _events[i] |= eventBit;
            return _events[i];
        }

        /// <summary>True the first time it is asked for the battle's frame.</summary>
        public bool TryWrite(object battle, int frame)
        {
            int i = Slot(battle, frame);
            if (_written[i]) return false;
            _written[i] = true;
            return true;
        }

        public void Clear()
        {
            _battles.Clear();
            _frames.Clear();
            _events.Clear();
            _written.Clear();
        }

        private int Slot(object battle, int frame)
        {
            for (int i = 0; i < _battles.Count; i++)
            {
                if (!ReferenceEquals(_battles[i], battle)) continue;
                if (_frames[i] != frame)
                {
                    _frames[i] = frame;
                    _events[i] = 0;
                    _written[i] = false;
                }
                return i;
            }
            _battles.Add(battle);
            _frames.Add(frame);
            _events.Add(0);
            _written.Add(false);
            return _battles.Count - 1;
        }
    }
}
