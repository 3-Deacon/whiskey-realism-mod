using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryWriter
    {
        private const int FileWritePerfWindowMs = 1000;
        private const int FileWritePerfWindowMinEvents = 2;

        private readonly TelemetryQueue _queue;
        private readonly TelemetryBudget _budget;
        private readonly string _sessionDirectory;
        private readonly Action _manifestWriter;
        private readonly string _sessionId;
        private readonly TelemetryProfile _profile;
        private readonly Action<string> _warningCallback;
        private readonly int _flushMilliseconds;
        private readonly int _flushRows;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly object _gate = new object();
        private readonly HashSet<string> _outputFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _warnedSinkFailureModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failureRowsAttempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _fileBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly FileWritePerfAccumulator _fileWritePerf = new FileWritePerfAccumulator();
        private readonly Thread _thread;
        private volatile bool _stopRequested;
        private long _sinkFailureCount;
        private bool _fileSinkDisabled;
        private bool _selfFailureFileWriteDisabled;
        private bool _shutdownTimeoutWarningEmitted;

        internal TelemetryWriter(
            TelemetryQueue queue,
            TelemetryBudget budget,
            string sessionDirectory,
            Action manifestWriter,
            string sessionId = "sink",
            TelemetryProfile profile = TelemetryProfile.FullTuning,
            Action<string> warningCallback = null,
            int flushMilliseconds = TelemetryRuntimeConfig.DefaultFlushMilliseconds,
            int flushRows = TelemetryRuntimeConfig.DefaultFlushRows)
        {
            _queue = queue;
            _budget = budget;
            _sessionDirectory = sessionDirectory;
            _manifestWriter = manifestWriter;
            _sessionId = TelemetryEvent.Safe(sessionId);
            _profile = profile;
            _warningCallback = warningCallback;
            _flushMilliseconds = TelemetryRuntimeConfig.ClampFlushMilliseconds(flushMilliseconds);
            _flushRows = TelemetryRuntimeConfig.ClampFlushRows(flushRows);
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "WhiskeyRealismTelemetryWriter"
            };
        }

        internal long SinkFailureCount
        {
            get
            {
                lock (_gate)
                    return _sinkFailureCount;
            }
        }

        internal List<string> OutputFilesSnapshot()
        {
            lock (_gate)
                return new List<string>(_outputFiles);
        }

        internal void Start()
        {
            _thread.Start();
        }

        internal void Signal()
        {
            try
            {
                _signal.Set();
            }
            catch
            {
            }
        }

        internal bool StopAndFlush(int timeoutMs)
        {
            _stopRequested = true;
            Signal();
            try
            {
                if (!_thread.Join(Math.Max(1, timeoutMs)))
                {
                    WarnShutdownTimeout();
                    RecordSinkFailure("writer-shutdown-timeout", null);
                    return false;
                }

                if (_queue != null && _queue.Count > 0)
                {
                    RecordSinkFailure("writer-shutdown-undrained", null);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                RecordSinkFailure("writer-shutdown", ex);
                return false;
            }
        }

        internal void RecordRuntimeSinkFailure(string reason, Exception ex)
        {
            RecordSinkFailure(reason, ex);
        }

        internal static string FileNameFor(TelemetryEvent ev, int rotationIndex)
        {
            string stem = StemFor(ev);
            if (rotationIndex <= 0)
                return stem + ".jsonl";

            return stem + "." + rotationIndex.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl";
        }

        private void Run()
        {
            while (!_stopRequested)
            {
                _signal.WaitOne(_flushMilliseconds);
                FlushBatch(_flushRows);
            }

            FlushUntilEmpty();

            EmitPendingFileWritePerf(force: true);
            FlushUntilEmpty();

            SafeWriteManifest();
        }

        private void FlushUntilEmpty()
        {
            while (_queue != null && _queue.Count > 0)
                FlushBatch(_flushRows);
        }

        private void FlushBatch(int maxRows)
        {
            List<TelemetryEvent> rows;
            try
            {
                rows = _queue.Drain(maxRows);
            }
            catch (Exception ex)
            {
                RecordSinkFailure("queue-drain", ex);
                return;
            }

            if (rows.Count == 0)
                return;

            bool hasNonPerformanceRow = HasNonPerformanceRow(rows);
            using (hasNonPerformanceRow
                ? TelemetryPerf.Scope("telemetry.flush", TelemetryLayer.System, TelemetryCategory.Performance, 10.0)
                : NoopDisposable.Instance)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    RowWriteResult result = WriteRow(rows[i], _fileWritePerf);
                    if (IsFileWritePerfRow(rows[i]) && result != RowWriteResult.Written)
                        _fileWritePerf.RecordDropped();
                }
            }

            if (hasNonPerformanceRow)
                EmitPendingFileWritePerf(force: false);

            SafeWriteManifest();
        }

        private void EmitPendingFileWritePerf(bool force)
        {
            if (!_fileWritePerf.ShouldEmit(
                force,
                DateTime.UtcNow,
                _flushRows,
                FileWritePerfWindowMs,
                FileWritePerfWindowMinEvents))
                return;

            _fileWritePerf.Emit();
            _fileWritePerf.Reset();
        }

        private void WriteRow(TelemetryEvent ev)
        {
            WriteRow(ev, reportSinkFailure: true, bypassSinkDisabled: false, fileWritePerf: null);
        }

        private RowWriteResult WriteRow(TelemetryEvent ev, bool reportSinkFailure)
        {
            return WriteRow(ev, reportSinkFailure, bypassSinkDisabled: false, fileWritePerf: null);
        }

        private RowWriteResult WriteRow(TelemetryEvent ev, FileWritePerfAccumulator fileWritePerf)
        {
            return WriteRow(ev, reportSinkFailure: true, bypassSinkDisabled: false, fileWritePerf: fileWritePerf);
        }

        private RowWriteResult WriteRow(TelemetryEvent ev, bool reportSinkFailure, bool bypassSinkDisabled)
        {
            return WriteRow(ev, reportSinkFailure, bypassSinkDisabled, fileWritePerf: null);
        }

        private RowWriteResult WriteRow(TelemetryEvent ev, bool reportSinkFailure, bool bypassSinkDisabled, FileWritePerfAccumulator fileWritePerf)
        {
            try
            {
                if (!bypassSinkDisabled && IsFileSinkDisabled())
                {
                    if (reportSinkFailure)
                        RecordSinkFailure("write-row-disabled", null);
                    return RowWriteResult.SinkDisabled;
                }

                string json = TelemetryJson.ToJsonLine(ev);
                long bytes = System.Text.Encoding.UTF8.GetByteCount(json);
                string fileName = FileNameForNextWrite(ev, bytes);
                if (!_budget.TryReserve(ev.Category, bytes, lowPriority: true, protectedSummary: IsCapSurvivingRuntimeRow(ev)))
                    return RowWriteResult.Dropped;

                string path = Path.Combine(_sessionDirectory, fileName);
                Stopwatch fileWriteWatch = IsFileWritePerfRow(ev) && fileWritePerf != null
                    ? Stopwatch.StartNew()
                    : null;
                File.AppendAllText(path, json);
                if (fileWriteWatch != null)
                {
                    fileWriteWatch.Stop();
                    fileWritePerf.RecordWritten(fileWriteWatch.Elapsed.TotalMilliseconds, bytes);
                }
                lock (_gate)
                {
                    _outputFiles.Add(fileName);
                    long existing;
                    _fileBytes.TryGetValue(fileName, out existing);
                    _fileBytes[fileName] = existing + bytes;
                }

                return RowWriteResult.Written;
            }
            catch (Exception ex)
            {
                if (reportSinkFailure)
                {
                    DisableFileSink();
                    RecordSinkFailure("write-row", ex);
                }

                return RowWriteResult.SinkFailed;
            }
        }

        private string FileNameForNextWrite(TelemetryEvent ev, long bytes)
        {
            int rotationIndex = _budget.RotationIndex;
            string candidate = FileNameFor(ev, rotationIndex);
            long currentBytes;
            lock (_gate)
                _fileBytes.TryGetValue(candidate, out currentBytes);

            if (currentBytes > 0L && currentBytes + bytes > _budget.RotateBytes)
            {
                _budget.MarkRotated();
                rotationIndex = _budget.RotationIndex;
            }

            return FileNameFor(ev, rotationIndex);
        }

        private void RecordSinkFailure(string reason, Exception ex)
        {
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            bool shouldAttemptFailureRow = false;
            lock (_gate)
            {
                _sinkFailureCount++;
                if (!_selfFailureFileWriteDisabled && _failureRowsAttempted.Add(safeReason))
                    shouldAttemptFailureRow = true;
            }

            if (!string.Equals(safeReason, "writer-shutdown-timeout", StringComparison.OrdinalIgnoreCase))
                WarnSinkFailureOnce(safeReason, ex);

            if (!shouldAttemptFailureRow)
                return;

            try
            {
                string message = ex == null ? safeReason : safeReason + ": " + ex.GetType().Name;
                var failure = TelemetryEvent.Create(
                    _sessionId,
                    _profile,
                    TelemetryLayer.System,
                    TelemetryCategory.Failure,
                    "TelemetrySinkFailure",
                    TelemetrySeverity.Warning)
                    .WithField("protectedSummary", true)
                    .WithField("reason", message);
                RowWriteResult result = WriteRow(failure, reportSinkFailure: false, bypassSinkDisabled: true);
                if (result == RowWriteResult.SinkFailed || result == RowWriteResult.SinkDisabled)
                    DisableSelfFailureWrites();
            }
            catch
            {
                DisableSelfFailureWrites();
            }
        }

        private bool IsFileSinkDisabled()
        {
            lock (_gate)
                return _fileSinkDisabled;
        }

        private void DisableFileSink()
        {
            lock (_gate)
                _fileSinkDisabled = true;
        }

        private void DisableSelfFailureWrites()
        {
            lock (_gate)
                _selfFailureFileWriteDisabled = true;
        }

        private void WarnSinkFailureOnce(string reason, Exception ex)
        {
            try
            {
                Action<string> callback = _warningCallback;
                if (callback == null)
                    return;

                string safeReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
                lock (_gate)
                {
                    if (!_warnedSinkFailureModes.Add(safeReason))
                        return;
                }

                string message = ex == null
                    ? safeReason
                    : safeReason + ": " + ex.GetType().Name;
                callback("Telemetry sink failure (" + message + "); telemetry rows may be truncated.");
            }
            catch
            {
            }
        }

        private void WarnShutdownTimeout()
        {
            try
            {
                Action<string> callback = _warningCallback;
                if (callback == null)
                    return;

                lock (_gate)
                {
                    if (_shutdownTimeoutWarningEmitted)
                        return;
                    _shutdownTimeoutWarningEmitted = true;
                }

                callback("Telemetry writer shutdown timed out; telemetry rows may be truncated.");
            }
            catch
            {
            }
        }

        private void SafeWriteManifest()
        {
            try
            {
                if (_manifestWriter != null)
                    _manifestWriter();
            }
            catch (Exception ex)
            {
                RecordSinkFailure("manifest-write", ex);
            }
        }

        private static string StemFor(TelemetryEvent ev)
        {
            if (ev == null)
                return "health";

            switch (ev.Category)
            {
                case TelemetryCategory.Health:
                    return "health";
                case TelemetryCategory.Failure:
                    return "failures";
                case TelemetryCategory.Performance:
                    return "performance";
                default:
                    return ev.Layer == TelemetryLayer.Campaign ? "campaign" : "tactical";
            }
        }

        private static bool IsCapSurvivingRuntimeRow(TelemetryEvent ev)
        {
            if (ev == null || !IsCapSurvivingCategory(ev.Category))
                return false;
            if (ev.Fields != null && ev.Fields.GetBool("protectedSummary"))
                return true;

            string eventName = TelemetryEvent.Safe(ev.EventName);
            if (string.Equals(eventName, "TelemetryShutdown", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(eventName, "TelemetrySinkFailure", StringComparison.OrdinalIgnoreCase))
                return true;
            if (eventName.IndexOf("summary", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (eventName.IndexOf("counter", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static bool IsCapSurvivingCategory(TelemetryCategory category)
        {
            return category == TelemetryCategory.Failure
                || category == TelemetryCategory.Health
                || category == TelemetryCategory.Performance;
        }

        private static bool HasNonPerformanceRow(List<TelemetryEvent> rows)
        {
            if (rows == null) return false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].Category != TelemetryCategory.Performance)
                    return true;
            }
            return false;
        }

        private static bool IsFileWritePerfRow(TelemetryEvent ev)
        {
            return ev != null && ev.Category != TelemetryCategory.Performance;
        }

        private enum RowWriteResult
        {
            Written,
            Dropped,
            SinkFailed,
            SinkDisabled
        }

        private sealed class NoopDisposable : IDisposable
        {
            internal static readonly NoopDisposable Instance = new NoopDisposable();

            public void Dispose()
            {
            }
        }

        private sealed class FileWritePerfAccumulator
        {
            private long _eventsEmitted;
            private long _eventsDropped;
            private long _bytesWritten;
            private double _durationMs;
            private DateTime _firstRecordedUtc;

            internal void RecordWritten(double durationMs, long bytesWritten)
            {
                MarkStarted();
                _eventsEmitted++;
                _bytesWritten += Math.Max(0L, bytesWritten);
                _durationMs += TelemetryFields.SanitizedNumber(durationMs);
            }

            internal void RecordDropped()
            {
                MarkStarted();
                _eventsDropped++;
            }

            internal bool ShouldEmit(bool force, DateTime utcNow, int batchSize, int windowMs, int minWindowEvents)
            {
                long events = TotalEvents;
                if (events <= 0L)
                    return false;
                if (force)
                    return true;
                if (events >= Math.Max(1, batchSize))
                    return true;

                int safeWindowMs = Math.Max(1, windowMs);
                int safeMinWindowEvents = Math.Max(1, minWindowEvents);
                return events >= safeMinWindowEvents
                    && _firstRecordedUtc != default(DateTime)
                    && (utcNow - _firstRecordedUtc).TotalMilliseconds >= safeWindowMs;
            }

            internal void Emit()
            {
                if (_eventsEmitted <= 0L && _eventsDropped <= 0L)
                    return;

                TelemetryPerf.EmitAggregate(
                    "telemetry.file-write",
                    TelemetryLayer.System,
                    TelemetryCategory.Performance,
                    _durationMs,
                    5.0,
                    _eventsEmitted,
                    _eventsDropped,
                    _bytesWritten);
            }

            internal void Reset()
            {
                _eventsEmitted = 0L;
                _eventsDropped = 0L;
                _bytesWritten = 0L;
                _durationMs = 0.0;
                _firstRecordedUtc = default(DateTime);
            }

            private long TotalEvents
            {
                get { return _eventsEmitted + _eventsDropped; }
            }

            private void MarkStarted()
            {
                if (_firstRecordedUtc == default(DateTime))
                    _firstRecordedUtc = DateTime.UtcNow;
            }
        }
    }
}
