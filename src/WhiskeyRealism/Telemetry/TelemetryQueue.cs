using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryQueue
    {
        private readonly object _gate = new object();
        private readonly List<TelemetryEvent> _events = new List<TelemetryEvent>();
        private readonly Dictionary<TelemetryCategory, long> _droppedByCategory =
            new Dictionary<TelemetryCategory, long>();
        private readonly int _capacity;

        internal TelemetryQueue(int capacity)
        {
            _capacity = Math.Max(1, capacity);
        }

        internal int Count
        {
            get
            {
                lock (_gate)
                    return _events.Count;
            }
        }

        internal long DroppedCount { get; private set; }

        internal bool TryEnqueue(TelemetryEvent ev)
        {
            if (ev == null)
            {
                RecordDropped(TelemetryCategory.Trace);
                return false;
            }

            lock (_gate)
            {
                if (_events.Count < _capacity)
                {
                    _events.Add(ev);
                    return true;
                }

                int evictIndex = FindLowestPriorityIndex();
                if (evictIndex >= 0 && Priority(ev.Category) < Priority(_events[evictIndex].Category))
                {
                    RecordDropped(_events[evictIndex].Category);
                    _events.RemoveAt(evictIndex);
                    _events.Add(ev);
                    return true;
                }

                RecordDropped(ev.Category);
                return false;
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
                int candidate = Priority(_events[i].Category);
                if (candidate >= priority)
                {
                    priority = candidate;
                    index = i;
                }
            }

            return index;
        }

        private void RecordDropped(TelemetryCategory category)
        {
            DroppedCount++;
            long count;
            _droppedByCategory.TryGetValue(category, out count);
            _droppedByCategory[category] = count + 1L;
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
                case TelemetryCategory.Decision:
                    return 3;
                case TelemetryCategory.Gate:
                    return 4;
                case TelemetryCategory.Write:
                    return 5;
                case TelemetryCategory.State:
                    return 6;
                default:
                    return 7;
            }
        }
    }
}
