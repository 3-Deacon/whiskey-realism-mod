using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryBudget
    {
        private readonly object _gate = new object();
        private readonly Dictionary<TelemetryCategory, long> _droppedByCategory =
            new Dictionary<TelemetryCategory, long>();
        private readonly long _totalBytes;
        private readonly long _rotateBytes;
        private long _emittedBytes;
        private long _currentFileBytes;
        private long _droppedCount;
        private int _rotationIndex;

        internal TelemetryBudget(long totalBytes, long rotateBytes)
        {
            _totalBytes = Math.Max(1L, totalBytes);
            _rotateBytes = Math.Max(1L, rotateBytes);
        }

        internal long TotalBytes { get { lock (_gate) return _totalBytes; } }
        internal long RotateBytes { get { lock (_gate) return _rotateBytes; } }
        internal long EmittedBytes { get { lock (_gate) return _emittedBytes; } }
        internal long CurrentFileBytes { get { lock (_gate) return _currentFileBytes; } }
        internal long DroppedCount { get { lock (_gate) return _droppedCount; } }
        internal int RotationIndex { get { lock (_gate) return _rotationIndex; } }

        internal bool Allow(TelemetryCategory category, long estimatedBytes)
        {
            return Allow(category, estimatedBytes, lowPriority: true, protectedSummary: false);
        }

        internal bool Allow(TelemetryCategory category, long estimatedBytes, bool lowPriority)
        {
            return Allow(category, estimatedBytes, lowPriority, protectedSummary: false);
        }

        internal bool Allow(TelemetryCategory category, long estimatedBytes, bool lowPriority, bool protectedSummary)
        {
            lock (_gate)
            {
                long safeBytes = Math.Max(0L, estimatedBytes);
                if (IsProtected(category))
                {
                    if (protectedSummary)
                        return true;

                    bool protectedAllowed = WithinTotalCap(safeBytes);
                    if (!protectedAllowed)
                        RecordDroppedLocked(category);
                    return protectedAllowed;
                }

                bool allowed = lowPriority ? WithinCategoryCut(category, safeBytes) : WithinTotalCap(safeBytes);
                if (!allowed)
                    RecordDroppedLocked(category);
                return allowed;
            }
        }

        internal void RecordBytes(TelemetryCategory category, long bytes)
        {
            lock (_gate)
            {
                long safeBytes = Math.Max(0L, bytes);
                _emittedBytes += safeBytes;
                _currentFileBytes += safeBytes;
            }
        }

        internal bool ShouldRotateBefore(long estimatedBytes)
        {
            lock (_gate)
            {
                long safeBytes = Math.Max(0L, estimatedBytes);
                return _currentFileBytes > 0L && _currentFileBytes + safeBytes > _rotateBytes;
            }
        }

        internal void MarkRotated()
        {
            lock (_gate)
            {
                _rotationIndex++;
                _currentFileBytes = 0L;
            }
        }

        internal void RecordDropped(TelemetryCategory category)
        {
            lock (_gate)
                RecordDroppedLocked(category);
        }

        internal long DroppedCountFor(TelemetryCategory category)
        {
            lock (_gate)
            {
                long count;
                return _droppedByCategory.TryGetValue(category, out count) ? count : 0L;
            }
        }

        internal Dictionary<string, long> DroppedSnapshot()
        {
            lock (_gate)
            {
                var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var entry in _droppedByCategory)
                    snapshot[entry.Key.ToString()] = entry.Value;
                return snapshot;
            }
        }

        private void RecordDroppedLocked(TelemetryCategory category)
        {
            _droppedCount++;
            long count;
            _droppedByCategory.TryGetValue(category, out count);
            _droppedByCategory[category] = count + 1L;
        }

        private static bool IsProtected(TelemetryCategory category)
        {
            return category == TelemetryCategory.Failure || category == TelemetryCategory.Health;
        }

        private bool WithinCategoryCut(TelemetryCategory category, long estimatedBytes)
        {
            decimal projected = (decimal)_emittedBytes + estimatedBytes;
            return projected * 100m <= (decimal)_totalBytes * CutPercent(category);
        }

        private bool WithinTotalCap(long estimatedBytes)
        {
            return _emittedBytes + estimatedBytes <= _totalBytes;
        }

        private static int CutPercent(TelemetryCategory category)
        {
            switch (category)
            {
                case TelemetryCategory.Trace:
                    return 90;
                case TelemetryCategory.State:
                    return 95;
                case TelemetryCategory.Decision:
                    return 98;
                case TelemetryCategory.Gate:
                case TelemetryCategory.Write:
                default:
                    return 100;
            }
        }
    }
}
