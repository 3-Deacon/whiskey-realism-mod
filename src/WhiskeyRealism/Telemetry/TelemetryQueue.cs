using System;
using System.Collections.Generic;
using System.Threading;

namespace WhiskeyRealism.Telemetry
{
    internal enum TelemetryQueueResult
    {
        Enqueued,
        Coalesced,
        Dropped
    }

    // Bounded telemetry queue shared by every emitting thread (main, orchestrator worker,
    // writer) and drained by the writer thread.
    //
    // Rows live in one FIFO ring per eviction rank; a global sequence number lets Drain
    // merge the rings back into enqueue order. Detail and protected (Health/Failure) rows
    // have independent O(1) counters: detail rows are capped at `capacity`, protected rows
    // at ProtectedReserveCapacity, and a protected row never evicts a detail row. When the
    // detail cap is full an incoming row evicts the newest row of the lowest-priority
    // non-empty rank only if that rank is strictly lower priority than the incoming row.
    // Every Enqueue/Drain step is O(rank count) under the lock, never O(queue length).
    internal sealed class TelemetryQueue
    {
        private const int RankCount = 7;

        private readonly object _gate = new object();
        private readonly Ring[] _ranks = new Ring[RankCount];
        private readonly long[] _droppedByCategory = new long[CategorySlots];
        private readonly long[] _protectedOverflowByCategory = new long[CategorySlots];
        private readonly int _capacity;
        private long _nextSequence;
        private int _detailCount;
        private int _protectedCount;
        private long _droppedCount;
        private long _protectedOverflowCount;

        private const int CategorySlots = 8;

        internal TelemetryQueue(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            ProtectedReserveCapacity = Math.Max(4, _capacity);
            for (int i = 0; i < RankCount; i++)
                _ranks[i] = new Ring();
        }

        internal int ProtectedReserveCapacity { get; private set; }

        // Lock-free reads: counters are written under _gate and read with Volatile/Interlocked
        // so diagnostics and frame probes never contend with enqueuers.
        internal int Count
        {
            get { return Volatile.Read(ref _detailCount) + Volatile.Read(ref _protectedCount); }
        }

        internal long DroppedCount
        {
            get { return Interlocked.Read(ref _droppedCount); }
        }

        internal long ProtectedOverflowCount
        {
            get { return Interlocked.Read(ref _protectedOverflowCount); }
        }

        internal TelemetryQueueResult Enqueue(TelemetryEvent ev)
        {
            lock (_gate)
            {
                if (ev == null)
                {
                    RecordDropped(TelemetryCategory.Trace);
                    return TelemetryQueueResult.Dropped;
                }

                if (IsProtected(ev.Category))
                {
                    if (_protectedCount >= ProtectedReserveCapacity)
                    {
                        RecordProtectedOverflow(ev.Category);
                        return TelemetryQueueResult.Coalesced;
                    }

                    Push(ev);
                    Volatile.Write(ref _protectedCount, _protectedCount + 1);
                    return TelemetryQueueResult.Enqueued;
                }

                if (_detailCount < _capacity)
                {
                    Push(ev);
                    Volatile.Write(ref _detailCount, _detailCount + 1);
                    return TelemetryQueueResult.Enqueued;
                }

                int incomingRank = Rank(ev.Category);
                int victimRank = LowestPriorityDetailRank();
                if (victimRank > incomingRank)
                {
                    TelemetryEvent evicted = _ranks[victimRank].PopNewest();
                    RecordDropped(evicted != null ? evicted.Category : TelemetryCategory.Trace);
                    Push(ev);
                    return TelemetryQueueResult.Enqueued;
                }

                RecordDropped(ev.Category);
                return TelemetryQueueResult.Dropped;
            }
        }

        internal List<TelemetryEvent> Drain(int max)
        {
            lock (_gate)
            {
                int count = Math.Min(Math.Max(0, max), _detailCount + _protectedCount);
                var drained = new List<TelemetryEvent>(count);
                int detail = _detailCount;
                int protectedRows = _protectedCount;
                for (int n = 0; n < count; n++)
                {
                    int best = -1;
                    long bestSequence = long.MaxValue;
                    for (int r = 0; r < RankCount; r++)
                    {
                        Ring ring = _ranks[r];
                        if (ring.Count > 0 && ring.OldestSequence < bestSequence)
                        {
                            bestSequence = ring.OldestSequence;
                            best = r;
                        }
                    }

                    if (best < 0)
                        break;

                    TelemetryEvent ev = _ranks[best].PopOldest();
                    if (IsProtected(ev.Category)) protectedRows--; else detail--;
                    drained.Add(ev);
                }

                Volatile.Write(ref _detailCount, detail);
                Volatile.Write(ref _protectedCount, protectedRows);
                return drained;
            }
        }

        internal long ProtectedOverflowCountFor(TelemetryCategory category)
        {
            lock (_gate)
                return _protectedOverflowByCategory[Slot(category)];
        }

        internal Dictionary<string, long> ProtectedOverflowSnapshot()
        {
            lock (_gate)
                return Snapshot(_protectedOverflowByCategory);
        }

        internal long DroppedCountFor(TelemetryCategory category)
        {
            lock (_gate)
                return _droppedByCategory[Slot(category)];
        }

        private void Push(TelemetryEvent ev)
        {
            _ranks[Rank(ev.Category)].Push(ev, _nextSequence++);
        }

        private int LowestPriorityDetailRank()
        {
            for (int r = RankCount - 1; r >= 0; r--)
            {
                if (_ranks[r].Count > 0 && !IsProtectedRank(r))
                    return r;
            }

            return -1;
        }

        private void RecordDropped(TelemetryCategory category)
        {
            Interlocked.Increment(ref _droppedCount);
            _droppedByCategory[Slot(category)]++;
        }

        private void RecordProtectedOverflow(TelemetryCategory category)
        {
            Interlocked.Increment(ref _protectedOverflowCount);
            _protectedOverflowByCategory[Slot(category)]++;
        }

        private static Dictionary<string, long> Snapshot(long[] counts)
        {
            var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] != 0L)
                    snapshot[((TelemetryCategory)i).ToString()] = counts[i];
            }

            return snapshot;
        }

        private static int Slot(TelemetryCategory category)
        {
            int slot = (int)category;
            return slot >= 0 && slot < CategorySlots ? slot : (int)TelemetryCategory.Trace;
        }

        // Eviction rank: higher rank is evicted first. Performance sits below Decision/State
        // (TacticalTuning exists to capture doctrine rows) but above Trace.
        internal static int Rank(TelemetryCategory category)
        {
            switch (category)
            {
                case TelemetryCategory.Health:
                    return 0;
                case TelemetryCategory.Failure:
                    return 1;
                case TelemetryCategory.Gate:
                case TelemetryCategory.Write:
                    return 2;
                case TelemetryCategory.Decision:
                    return 3;
                case TelemetryCategory.State:
                    return 4;
                case TelemetryCategory.Performance:
                    return 5;
                default:
                    return 6;
            }
        }

        private static bool IsProtectedRank(int rank)
        {
            return rank <= 1;
        }

        private static bool IsProtected(TelemetryCategory category)
        {
            return category == TelemetryCategory.Health || category == TelemetryCategory.Failure;
        }

        // Growable FIFO ring of (event, sequence). PopNewest supports newest-first eviction.
        private sealed class Ring
        {
            private TelemetryEvent[] _items = new TelemetryEvent[16];
            private long[] _sequences = new long[16];
            private int _head;

            internal int Count { get; private set; }

            internal long OldestSequence
            {
                get { return _sequences[_head]; }
            }

            internal void Push(TelemetryEvent ev, long sequence)
            {
                if (Count == _items.Length)
                    Grow();

                int tail = (_head + Count) % _items.Length;
                _items[tail] = ev;
                _sequences[tail] = sequence;
                Count++;
            }

            internal TelemetryEvent PopOldest()
            {
                TelemetryEvent ev = _items[_head];
                _items[_head] = null;
                _head = (_head + 1) % _items.Length;
                Count--;
                if (Count == 0)
                    _head = 0;
                return ev;
            }

            internal TelemetryEvent PopNewest()
            {
                int tail = (_head + Count - 1) % _items.Length;
                TelemetryEvent ev = _items[tail];
                _items[tail] = null;
                Count--;
                if (Count == 0)
                    _head = 0;
                return ev;
            }

            private void Grow()
            {
                int length = _items.Length * 2;
                var items = new TelemetryEvent[length];
                var sequences = new long[length];
                for (int i = 0; i < Count; i++)
                {
                    int index = (_head + i) % _items.Length;
                    items[i] = _items[index];
                    sequences[i] = _sequences[index];
                }

                _items = items;
                _sequences = sequences;
                _head = 0;
            }
        }
    }
}
