using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryBudget
    {
        private readonly Dictionary<TelemetryCategory, long> _droppedByCategory =
            new Dictionary<TelemetryCategory, long>();

        internal TelemetryBudget(long totalBytes, long rotateBytes)
        {
            TotalBytes = Math.Max(1L, totalBytes);
            RotateBytes = Math.Max(1L, rotateBytes);
        }

        internal long TotalBytes { get; private set; }
        internal long RotateBytes { get; private set; }
        internal long EmittedBytes { get; private set; }
        internal long CurrentFileBytes { get; private set; }
        internal long DroppedCount { get; private set; }
        internal int RotationIndex { get; private set; }

        internal bool Allow(TelemetryCategory category, long estimatedBytes)
        {
            long safeBytes = Math.Max(0L, estimatedBytes);
            if (IsProtected(category))
                return true;

            bool allowed = EmittedBytes + safeBytes <= TotalBytes;
            if (!allowed)
                RecordDropped(category);
            return allowed;
        }

        internal void RecordBytes(TelemetryCategory category, long bytes)
        {
            long safeBytes = Math.Max(0L, bytes);
            EmittedBytes += safeBytes;
            CurrentFileBytes += safeBytes;
        }

        internal bool ShouldRotateBefore(long estimatedBytes)
        {
            long safeBytes = Math.Max(0L, estimatedBytes);
            return CurrentFileBytes > 0L && CurrentFileBytes + safeBytes > RotateBytes;
        }

        internal void MarkRotated()
        {
            RotationIndex++;
            CurrentFileBytes = 0L;
        }

        internal void RecordDropped(TelemetryCategory category)
        {
            DroppedCount++;
            long count;
            _droppedByCategory.TryGetValue(category, out count);
            _droppedByCategory[category] = count + 1L;
        }

        internal long DroppedCountFor(TelemetryCategory category)
        {
            long count;
            return _droppedByCategory.TryGetValue(category, out count) ? count : 0L;
        }

        internal Dictionary<string, long> DroppedSnapshot()
        {
            var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var entry in _droppedByCategory)
                snapshot[entry.Key.ToString()] = entry.Value;
            return snapshot;
        }

        private static bool IsProtected(TelemetryCategory category)
        {
            return category == TelemetryCategory.Failure || category == TelemetryCategory.Health;
        }
    }
}
