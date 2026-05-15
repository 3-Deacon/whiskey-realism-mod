using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Telemetry
{
    internal enum TelemetryQueueResult
    {
        Enqueued,
        Coalesced,
        Dropped
    }

    internal sealed class TelemetryQueue
    {
        private readonly object _gate = new object();
        private readonly List<TelemetryEvent> _events = new List<TelemetryEvent>();
        private readonly Dictionary<TelemetryCategory, long> _droppedByCategory =
            new Dictionary<TelemetryCategory, long>();
        private readonly Dictionary<TelemetryCategory, long> _protectedOverflowByCategory =
            new Dictionary<TelemetryCategory, long>();
        private readonly int _capacity;
        private long _droppedCount;
        private long _protectedOverflowCount;

        internal TelemetryQueue(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            ProtectedReserveCapacity = Math.Max(4, _capacity);
        }

        internal int ProtectedReserveCapacity { get; private set; }

        internal int Count
        {
            get
            {
                lock (_gate)
                    return _events.Count;
            }
        }

        internal long DroppedCount
        {
            get
            {
                lock (_gate)
                    return _droppedCount;
            }
        }

        internal long ProtectedOverflowCount
        {
            get
            {
                lock (_gate)
                    return _protectedOverflowCount;
            }
        }

        internal bool TryEnqueue(TelemetryEvent ev)
        {
            return Enqueue(ev) == TelemetryQueueResult.Enqueued;
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
                    if (ProtectedCount() >= ProtectedReserveCapacity)
                    {
                        RecordProtectedOverflow(ev.Category);
                        return TelemetryQueueResult.Coalesced;
                    }

                    if (DetailCount() >= _capacity)
                    {
                        int protectedEvictIndex = FindLowestPriorityIndex();
                        if (protectedEvictIndex >= 0)
                        {
                            RecordDropped(_events[protectedEvictIndex].Category);
                            _events.RemoveAt(protectedEvictIndex);
                        }
                    }

                    _events.Add(ev);
                    return TelemetryQueueResult.Enqueued;
                }

                if (DetailCount() < _capacity)
                {
                    _events.Add(ev);
                    return TelemetryQueueResult.Enqueued;
                }

                int evictIndex = FindLowestPriorityIndex();
                if (evictIndex >= 0 && Priority(ev.Category) < Priority(_events[evictIndex].Category))
                {
                    RecordDropped(_events[evictIndex].Category);
                    _events.RemoveAt(evictIndex);
                    _events.Add(ev);
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
                int count = Math.Min(Math.Max(0, max), _events.Count);
                var drained = _events.GetRange(0, count);
                _events.RemoveRange(0, count);
                return drained;
            }
        }

        internal long ProtectedOverflowCountFor(TelemetryCategory category)
        {
            lock (_gate)
            {
                long count;
                return _protectedOverflowByCategory.TryGetValue(category, out count) ? count : 0L;
            }
        }

        internal Dictionary<string, long> ProtectedOverflowSnapshot()
        {
            lock (_gate)
            {
                var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var entry in _protectedOverflowByCategory)
                    snapshot[entry.Key.ToString()] = entry.Value;
                return snapshot;
            }
        }

        internal long DroppedCountFor(TelemetryCategory category)
        {
            lock (_gate)
            {
                long count;
                return _droppedByCategory.TryGetValue(category, out count) ? count : 0L;
            }
        }

        private int FindLowestPriorityIndex()
        {
            int index = -1;
            int priority = int.MinValue;
            for (int i = 0; i < _events.Count; i++)
            {
                if (IsProtected(_events[i].Category))
                    continue;

                int candidate = Priority(_events[i].Category);
                if (candidate >= priority)
                {
                    priority = candidate;
                    index = i;
                }
            }

            return index;
        }

        private int DetailCount()
        {
            int count = 0;
            for (int i = 0; i < _events.Count; i++)
            {
                if (!IsProtected(_events[i].Category))
                    count++;
            }

            return count;
        }

        private int ProtectedCount()
        {
            int count = 0;
            for (int i = 0; i < _events.Count; i++)
            {
                if (IsProtected(_events[i].Category))
                    count++;
            }

            return count;
        }

        private void RecordDropped(TelemetryCategory category)
        {
            _droppedCount++;
            long count;
            _droppedByCategory.TryGetValue(category, out count);
            _droppedByCategory[category] = count + 1L;
        }

        private void RecordProtectedOverflow(TelemetryCategory category)
        {
            _protectedOverflowCount++;
            long count;
            _protectedOverflowByCategory.TryGetValue(category, out count);
            _protectedOverflowByCategory[category] = count + 1L;
        }

        private static int Priority(TelemetryCategory category)
        {
            switch (category)
            {
                case TelemetryCategory.Health:
                    return 0;
                case TelemetryCategory.Failure:
                    return 1;
                case TelemetryCategory.Performance:
                    return 2;
                case TelemetryCategory.Gate:
                case TelemetryCategory.Write:
                    return 4;
                case TelemetryCategory.Decision:
                    return 5;
                case TelemetryCategory.State:
                    return 6;
                default:
                    return 7;
            }
        }

        private static bool IsProtected(TelemetryCategory category)
        {
            return category == TelemetryCategory.Health || category == TelemetryCategory.Failure;
        }
    }
}
