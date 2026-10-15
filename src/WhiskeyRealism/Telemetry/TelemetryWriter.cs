using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WhiskeyRealism.Telemetry
{
    // Background JSONL writer for one telemetry session.
    //
    // - One open StreamWriter per output file, flushed once per batch and closed on
    //   rotation, failure, and shutdown (previously File.AppendAllText per row).
    // - manifest.json is rewritten only when the output-file set changes (new file or
    //   rotation), every ManifestIntervalMs, and at shutdown (previously once per batch).
    // - A failed write closes that stream, keeps the unwritten rows pending, and retries
    //   after an exponential backoff; the file sink is disabled only after
    //   MaxConsecutiveWriteFailures consecutive failures (previously the first exception
    //   disabled the sink for the whole session).
    // - Run() has an outer guard that logs once and keeps the thread alive.
    // - The shutdown drain is bounded so a producer racing shutdown cannot pin the thread.
    internal sealed class TelemetryWriter
    {
        private const int FileWritePerfWindowMs = 1000;
        private const int FileWritePerfWindowMinEvents = 2;
        private const int StreamBufferBytes = 64 * 1024;
        private const int MaxRetryBackoffMs = 2000;
        internal const int MaxConsecutiveWriteFailures = 5;

        // Internal (not const) so tests can shorten them.
        internal static int ManifestIntervalMs = 5000;
        internal static int RetryBaseMilliseconds = 50;

        private static readonly System.Text.UTF8Encoding Utf8NoBom = new System.Text.UTF8Encoding(false);

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
        private readonly object _streamGate = new object();
        private readonly HashSet<string> _outputFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _warnedSinkFailureModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failureRowsAttempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _fileBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StreamWriter> _streams = new Dictionary<string, StreamWriter>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingRow> _pending = new List<PendingRow>();
        private readonly FileWritePerfAccumulator _fileWritePerf = new FileWritePerfAccumulator();
        private readonly Thread _thread;
        private volatile bool _stopRequested;
        private volatile bool _fileSinkDisabled;
        private long _sinkFailureCount;
        private int _consecutiveWriteFailures;
        private long _retryNotBeforeTimestamp;
        private int _streamRotationIndex = -1;
        private bool _manifestDue;
        private long _lastManifestTimestamp;
        private bool _selfFailureFileWriteDisabled;
        private bool _shutdownTimeoutWarningEmitted;
        private bool _loopFaultWarned;

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
            _lastManifestTimestamp = Stopwatch.GetTimestamp();
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "WhiskeyRealismTelemetryWriter"
            };
        }

        internal long SinkFailureCount
        {
            get { return Interlocked.Read(ref _sinkFailureCount); }
        }

        internal bool FileSinkDisabled
        {
            get { return _fileSinkDisabled; }
        }

        internal int OpenStreamCount
        {
            get
            {
                lock (_streamGate)
                    return _streams.Count;
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

                if ((_queue != null && _queue.Count > 0) || _pending.Count > 0)
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
                try
                {
                    _signal.WaitOne(WaitMilliseconds());
                    FlushBatch(_flushRows);
                    MaybeWriteManifest(force: false);
                }
                catch (Exception ex)
                {
                    OnLoopFault(ex);
                }
            }

            try
            {
                FlushUntilEmpty();
                EmitPendingFileWritePerf(force: true);
                FlushUntilEmpty();
            }
            catch (Exception ex)
            {
                OnLoopFault(ex);
            }
            finally
            {
                CloseAllStreams();
                SafeWriteManifest();
            }
        }

        private void OnLoopFault(Exception ex)
        {
            if (!_loopFaultWarned)
            {
                _loopFaultWarned = true;
                RecordSinkFailure("writer-loop", ex);
            }
            else
            {
                Interlocked.Increment(ref _sinkFailureCount);
            }

            try
            {
                Thread.Sleep(Math.Min(_flushMilliseconds, 250));
            }
            catch
            {
            }
        }

        private int WaitMilliseconds()
        {
            long retryAt = Interlocked.Read(ref _retryNotBeforeTimestamp);
            if (retryAt == 0L)
                return _flushMilliseconds;

            long remainingMs = (retryAt - Stopwatch.GetTimestamp()) * 1000L / Stopwatch.Frequency;
            if (remainingMs <= 0L)
                return 1;
            return (int)Math.Min(_flushMilliseconds, remainingMs);
        }

        // Drains what was queued when shutdown started (plus a small slack), not "until the
        // queue is empty": a producer still enqueuing could otherwise keep the thread here
        // forever. Rows left behind are reported by StopAndFlush as writer-shutdown-undrained.
        private void FlushUntilEmpty()
        {
            if (_queue == null)
                return;

            int budgetRows = _queue.Count + _pending.Count + _flushRows * 4;
            int maxIterations = budgetRows / Math.Max(1, _flushRows) + MaxConsecutiveWriteFailures + 4;
            for (int i = 0; i < maxIterations && (_queue.Count > 0 || _pending.Count > 0); i++)
            {
                WaitOutRetryBackoff();
                FlushBatch(_flushRows);
            }
        }

        private void WaitOutRetryBackoff()
        {
            long retryAt = Interlocked.Read(ref _retryNotBeforeTimestamp);
            if (retryAt == 0L)
                return;

            long remainingTicks = retryAt - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0L)
                return;

            // Round up: a sub-millisecond remainder must still wait, or the bounded shutdown
            // loop burns its iterations inside the backoff window.
            long remainingMs = (remainingTicks * 1000L + Stopwatch.Frequency - 1L) / Stopwatch.Frequency;
            Thread.Sleep((int)Math.Min(remainingMs, MaxRetryBackoffMs));
        }

        private void FlushBatch(int maxRows)
        {
            if (InRetryBackoff())
                return;

            if (_pending.Count == 0)
            {
                List<TelemetryEvent> drained;
                try
                {
                    drained = _queue.Drain(maxRows);
                }
                catch (Exception ex)
                {
                    RecordSinkFailure("queue-drain", ex);
                    return;
                }

                for (int i = 0; i < drained.Count; i++)
                    _pending.Add(new PendingRow(drained[i]));
            }

            if (_pending.Count == 0)
                return;

            bool hasNonPerformanceRow = HasNonPerformanceRow(_pending);
            int written = 0;
            using (hasNonPerformanceRow
                ? TelemetryPerf.Scope("telemetry.flush", TelemetryLayer.System, TelemetryCategory.Performance, 10.0)
                : NoopDisposable.Instance)
            {
                for (; written < _pending.Count; written++)
                {
                    PendingRow row = _pending[written];
                    RowWriteResult result = WritePendingRow(ref row);
                    _pending[written] = row;
                    if (result == RowWriteResult.Retry)
                        break;
                    if (IsFileWritePerfRow(row.Event) && result != RowWriteResult.Written)
                        _fileWritePerf.RecordDropped();
                }

                if (!FlushOpenStreams())
                    written = 0;
            }

            if (written > 0)
                _pending.RemoveRange(0, written);

            if (hasNonPerformanceRow)
                EmitPendingFileWritePerf(force: false);
        }

        private bool InRetryBackoff()
        {
            long retryAt = Interlocked.Read(ref _retryNotBeforeTimestamp);
            if (retryAt == 0L)
                return false;
            if (Stopwatch.GetTimestamp() < retryAt)
                return true;

            Interlocked.Exchange(ref _retryNotBeforeTimestamp, 0L);
            return false;
        }

        private void MaybeWriteManifest(bool force)
        {
            long now = Stopwatch.GetTimestamp();
            long intervalTicks = (long)Math.Max(1, ManifestIntervalMs) * Stopwatch.Frequency / 1000L;
            if (!force && !_manifestDue && now - _lastManifestTimestamp < intervalTicks)
                return;

            _manifestDue = false;
            _lastManifestTimestamp = now;
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

        private RowWriteResult WritePendingRow(ref PendingRow row)
        {
            TelemetryEvent ev = row.Event;
            if (IsFileSinkDisabled())
            {
                RecordSinkFailure("write-row-disabled", null);
                return RowWriteResult.SinkDisabled;
            }

            try
            {
                if (row.Json == null)
                {
                    row.Json = TelemetryJson.ToJsonLine(ev);
                    row.Bytes = Utf8NoBom.GetByteCount(row.Json);
                }

                string fileName = FileNameForNextWrite(ev, row.Bytes);
                if (!row.Reserved)
                {
                    if (!_budget.TryReserve(ev.Category, row.Bytes, lowPriority: true, protectedSummary: IsCapSurvivingRuntimeRow(ev)))
                        return RowWriteResult.Dropped;
                    row.Reserved = true;
                }

                long start = IsFileWritePerfRow(ev) ? Stopwatch.GetTimestamp() : 0L;
                StreamWriter stream = OpenStream(fileName);
                stream.Write(row.Json);
                if (start != 0L)
                    _fileWritePerf.RecordWritten((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency, row.Bytes);

                RecordOutput(fileName, row.Bytes);
                return RowWriteResult.Written;
            }
            catch (Exception ex)
            {
                OnWriteFailure("write-row", ex);
                return IsFileSinkDisabled() ? RowWriteResult.SinkFailed : RowWriteResult.Retry;
            }
        }

        private bool FlushOpenStreams()
        {
            lock (_streamGate)
            {
                if (_streams.Count == 0)
                    return true;

                try
                {
                    foreach (KeyValuePair<string, StreamWriter> entry in _streams)
                        entry.Value.Flush();
                    Interlocked.Exchange(ref _consecutiveWriteFailures, 0);
                    return true;
                }
                catch (Exception ex)
                {
                    CloseAllStreamsLocked();
                    OnWriteFailure("flush", ex);
                    return false;
                }
            }
        }

        private void OnWriteFailure(string reason, Exception ex)
        {
            CloseAllStreams();
            int failures = Interlocked.Increment(ref _consecutiveWriteFailures);
            RecordSinkFailure(reason, ex);
            if (failures >= MaxConsecutiveWriteFailures)
            {
                DisableFileSink();
                Interlocked.Exchange(ref _retryNotBeforeTimestamp, 0L);
                return;
            }

            long baseMs = Math.Max(1, RetryBaseMilliseconds);
            long backoffMs = _stopRequested
                ? baseMs
                : Math.Min(MaxRetryBackoffMs, baseMs << Math.Min(failures - 1, 16));
            Interlocked.Exchange(ref _retryNotBeforeTimestamp, Stopwatch.GetTimestamp() + backoffMs * Stopwatch.Frequency / 1000L);
        }

        private StreamWriter OpenStream(string fileName)
        {
            lock (_streamGate)
            {
                StreamWriter stream;
                if (_streams.TryGetValue(fileName, out stream))
                    return stream;

                string path = Path.Combine(_sessionDirectory, fileName);
                var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, StreamBufferBytes);
                stream = new StreamWriter(file, Utf8NoBom, StreamBufferBytes);
                _streams[fileName] = stream;
                return stream;
            }
        }

        private void CloseAllStreams()
        {
            lock (_streamGate)
                CloseAllStreamsLocked();
        }

        private void CloseAllStreamsLocked()
        {
            foreach (KeyValuePair<string, StreamWriter> entry in _streams)
            {
                try
                {
                    entry.Value.Dispose();
                }
                catch
                {
                }
            }

            _streams.Clear();
        }

        private void RecordOutput(string fileName, long bytes)
        {
            lock (_gate)
            {
                if (_outputFiles.Add(fileName))
                    _manifestDue = true;
                long existing;
                _fileBytes.TryGetValue(fileName, out existing);
                _fileBytes[fileName] = existing + bytes;
            }
        }

        // One-shot append used for self-reported sink-failure rows: these can be written from
        // the runtime's shutdown path after the writer thread has closed its streams.
        private RowWriteResult WriteFailureRow(TelemetryEvent ev)
        {
            try
            {
                string json = TelemetryJson.ToJsonLine(ev);
                long bytes = Utf8NoBom.GetByteCount(json);
                string fileName = FileNameForNextWrite(ev, bytes);
                if (!_budget.TryReserve(ev.Category, bytes, lowPriority: true, protectedSummary: IsCapSurvivingRuntimeRow(ev)))
                    return RowWriteResult.Dropped;

                StreamWriter open = null;
                bool onWriterThread = Thread.CurrentThread == _thread;
                if (onWriterThread)
                {
                    lock (_streamGate)
                        _streams.TryGetValue(fileName, out open);
                }

                if (open != null)
                {
                    open.Write(json);
                    open.Flush();
                }
                else
                {
                    // Other threads (shutdown timeout, runtime shutdown) never touch the
                    // writer's StreamWriters; append through a separate shared handle.
                    using (var file = new FileStream(
                        Path.Combine(_sessionDirectory, fileName),
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete))
                    {
                        byte[] payload = Utf8NoBom.GetBytes(json);
                        file.Write(payload, 0, payload.Length);
                    }
                }

                RecordOutput(fileName, bytes);
                return RowWriteResult.Written;
            }
            catch
            {
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

            if (rotationIndex != _streamRotationIndex)
            {
                if (_streamRotationIndex >= 0)
                {
                    FlushOpenStreams();
                    CloseAllStreams();
                    lock (_gate)
                        _manifestDue = true;
                }

                _streamRotationIndex = rotationIndex;
            }

            return FileNameFor(ev, rotationIndex);
        }

        private void RecordSinkFailure(string reason, Exception ex)
        {
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            bool shouldAttemptFailureRow = false;
            Interlocked.Increment(ref _sinkFailureCount);
            lock (_gate)
            {
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
                RowWriteResult result = WriteFailureRow(failure);
                if (result == RowWriteResult.SinkFailed)
                    DisableSelfFailureWrites();
            }
            catch
            {
                DisableSelfFailureWrites();
            }
        }

        private bool IsFileSinkDisabled()
        {
            return _fileSinkDisabled;
        }

        private void DisableFileSink()
        {
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

        private static bool HasNonPerformanceRow(List<PendingRow> rows)
        {
            if (rows == null) return false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Event != null && rows[i].Event.Category != TelemetryCategory.Performance)
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
            SinkDisabled,
            Retry
        }

        private struct PendingRow
        {
            internal PendingRow(TelemetryEvent ev)
            {
                Event = ev;
                Json = null;
                Bytes = 0L;
                Reserved = false;
            }

            internal TelemetryEvent Event;
            internal string Json;
            internal long Bytes;
            internal bool Reserved;
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
