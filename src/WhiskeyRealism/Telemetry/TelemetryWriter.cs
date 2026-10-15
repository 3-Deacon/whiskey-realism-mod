using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WhiskeyRealism.Telemetry
{
    // Background JSONL writer for one telemetry session.
    //
    // - One open handle per output file; each batch writes one encoded chunk per file and
    //   handles are closed on rotation, failure, and shutdown (previously File.AppendAllText
    //   per row). Rows count as written (budget output bytes, manifest file list) only once
    //   their bytes are on disk.
    // - manifest.json is rewritten only when the output-file set changes (new file or
    //   rotation), every ManifestIntervalMs, and at shutdown (previously once per batch).
    // - A failed chunk write commits the rows that fully reached the file (measured by file
    //   length re-derived from a fresh handle, never the faulted one), truncates a torn
    //   partial row (or, if truncation fails, isolates it on its own line and counts it in
    //   tornRows), closes the handle, keeps the rest pending, and retries after an exponential
    //   backoff, so a retry never duplicates rows. Sink-failure rows use the same path. The file sink
    //   is disabled only after MaxConsecutiveWriteFailures consecutive failures (previously
    //   the first exception disabled the sink for the whole session).
    // - Sink-failure rows raised off the writer thread while it is alive are queued, never
    //   written through a second handle.
    // - Run() has an outer guard that logs once and keeps the thread alive.
    // - The shutdown drain is bounded so a producer racing shutdown cannot pin the thread.
    internal sealed class TelemetryWriter
    {
        private const int FileWritePerfWindowMs = 1000;
        private const int FileWritePerfWindowMinEvents = 2;
        private const int MaxRetryBackoffMs = 2000;
        internal const int MaxConsecutiveWriteFailures = 5;

        // Internal (not const) so tests can shorten them.
        internal static int ManifestIntervalMs = 5000;
        internal static int RetryBaseMilliseconds = 50;
        // Retry-backoff and manifest-interval clock (Stopwatch ticks). A test seam like
        // OpenFileStream: harness tests advance it explicitly, so retry and timer
        // behaviour does not depend on how fast the machine runs. Write durations
        // (perf rows) keep the real Stopwatch.
        internal static Func<long> Clock = Stopwatch.GetTimestamp;

        // Opens a write handle on one output file: FileMode.Append for row writes (one encoded
        // chunk per file per batch, so unbuffered), FileMode.Open for post-fault length checks
        // and truncation (an Append handle cannot truncate below its append start). Internal
        // so tests can inject faults.
        internal static Func<string, FileMode, Stream> OpenFileStream = (path, mode) =>
            new FileStream(path, mode, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);

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
        private readonly Dictionary<string, OpenFile> _streams = new Dictionary<string, OpenFile>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingRow> _pending = new List<PendingRow>();
        private readonly List<string> _batchFiles = new List<string>();
        private readonly List<PendingRow> _chunkRows = new List<PendingRow>();
        private readonly Dictionary<string, long> _prospectiveBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private byte[] _chunk = new byte[64 * 1024];
        private int[] _rowEnds = new int[256];
        // Files whose last write left an untruncatable fragment: the next write starts with a
        // newline so the fragment stays on its own (invalid, skipped) line.
        private readonly HashSet<string> _tornFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private long _tornRows;
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
            _lastManifestTimestamp = Clock();
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

        // Rows left as an isolated invalid fragment because a torn write could not be
        // truncated. Recorded in manifest droppedCounters["tornRows"]; the validator tolerates
        // that many fragment lines.
        internal long TornRowCount
        {
            get { return Interlocked.Read(ref _tornRows); }
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

        // Inverse of FileNameFor: "stem.jsonl" -> 0, "stem.NNN.jsonl" -> NNN. A handle is tagged
        // with its own file's rotation so CloseStreamsFromOldRotations closes it even when it
        // was first opened in the batch that rotated.
        internal static int RotationIndexOf(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                return 0;
            string stem = fileName.Substring(0, fileName.Length - ".jsonl".Length);
            int dot = stem.LastIndexOf('.');
            int index;
            if (dot < 0 || !int.TryParse(stem.Substring(dot + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index))
                return 0;
            return index;
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
                // Router.Shutdown flushed perf aggregates before stopping the writer; the scopes
                // this drain just ran (telemetry.flush) would otherwise be lost. Flush them here,
                // while the runtime still accepts rows, and write them in the last drain.
                TelemetryPerf.FlushAggregates();
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

            long remainingMs = (retryAt - Clock()) * 1000L / Stopwatch.Frequency;
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

            long remainingTicks = retryAt - Clock();
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
            using (hasNonPerformanceRow
                ? TelemetryPerf.Scope("telemetry.flush", TelemetryLayer.System, TelemetryCategory.Performance, 10.0)
                : NoopDisposable.Instance)
            {
                PrepareRows();
                WriteChunks();
                CloseStreamsFromOldRotations();
            }

            RemoveDoneRows();

            if (hasNonPerformanceRow)
                EmitPendingFileWritePerf(force: false);
        }

        // Encodes each not-yet-prepared row once, assigns its output file (rotation-aware,
        // counting rows already assigned in this batch) and reserves its budget once. Rows the
        // budget refuses, or that arrive while the sink is disabled, are finished here.
        private void PrepareRows()
        {
            _prospectiveBytes.Clear();
            for (int i = 0; i < _pending.Count; i++)
            {
                PendingRow row = _pending[i];
                if (!row.Done && row.FileName != null)
                    AddProspective(row.FileName, row.Bytes);
            }

            for (int i = 0; i < _pending.Count; i++)
            {
                PendingRow row = _pending[i];
                if (row.Done)
                    continue;

                if (IsFileSinkDisabled())
                {
                    row.Done = true;
                    RecordSinkFailure("write-row-disabled", null);
                    if (IsFileWritePerfRow(row.Event))
                        _fileWritePerf.RecordDropped();
                    continue;
                }

                if (row.FileName != null)
                    continue;

                try
                {
                    row.Json = TelemetryJson.ToJsonLine(row.Event);
                    row.Bytes = Utf8NoBom.GetByteCount(row.Json);
                    string fileName = FileNameForNextWrite(row.Event, row.Bytes);
                    if (!_budget.TryReserve(row.Event.Category, row.Bytes, lowPriority: true, protectedSummary: IsCapSurvivingRuntimeRow(row.Event)))
                    {
                        row.Done = true;
                        if (IsFileWritePerfRow(row.Event))
                            _fileWritePerf.RecordDropped();
                        continue;
                    }

                    row.FileName = fileName;
                    AddProspective(fileName, row.Bytes);
                }
                catch (Exception ex)
                {
                    row.Done = true;
                    RecordSinkFailure("encode-row", ex);
                }
            }
        }

        private void AddProspective(string fileName, long bytes)
        {
            long existing;
            _prospectiveBytes.TryGetValue(fileName, out existing);
            _prospectiveBytes[fileName] = existing + bytes;
        }

        // Writes one chunk per output file, in first-appearance order. Stops at the first
        // failing file; its unwritten rows and every later file stay pending for the retry.
        private void WriteChunks()
        {
            _batchFiles.Clear();
            for (int i = 0; i < _pending.Count; i++)
            {
                PendingRow row = _pending[i];
                if (!row.Done && row.FileName != null && !_batchFiles.Contains(row.FileName))
                    _batchFiles.Add(row.FileName);
            }

            for (int f = 0; f < _batchFiles.Count; f++)
            {
                if (IsFileSinkDisabled() || !WriteChunk(_batchFiles[f]))
                    return;
            }

            Interlocked.Exchange(ref _consecutiveWriteFailures, 0);
        }

        private bool WriteChunk(string fileName)
        {
            _chunkRows.Clear();
            int prefix = NeedsSeparator(fileName) ? 1 : 0;
            int total = prefix;
            EnsureChunkCapacity(total);
            if (prefix == 1)
                _chunk[0] = (byte)'\n';

            for (int i = 0; i < _pending.Count; i++)
            {
                PendingRow row = _pending[i];
                if (row.Done || !string.Equals(row.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                EnsureChunkCapacity(total + (int)row.Bytes);
                total += Utf8NoBom.GetBytes(row.Json, 0, row.Json.Length, _chunk, total);
                if (_rowEnds.Length <= _chunkRows.Count)
                    Array.Resize(ref _rowEnds, _rowEnds.Length * 2);
                _rowEnds[_chunkRows.Count] = total;
                _chunkRows.Add(row);
            }

            if (_chunkRows.Count == 0)
                return true;

            long started = Stopwatch.GetTimestamp();
            int landed;
            Exception failure;
            bool ok = AppendRows(fileName, _chunk, total, prefix, _rowEnds, _chunkRows.Count, out landed, out failure);
            if (landed > 0)
                CommitRows(fileName, landed, ok ? (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency : 0.0);
            if (ok)
                return true;

            OnWriteFailure("write-row", failure);
            return false;
        }

        private bool NeedsSeparator(string fileName)
        {
            lock (_streamGate)
                return _tornFiles.Contains(fileName);
        }

        // Appends buffer[0..total) to fileName. buffer may start with a one-byte newline
        // separator (prefix); rowEnds[i] is the end offset of row i within buffer. Returns true
        // when everything landed; otherwise `landed` is the number of rows that fully reached
        // the file, measured by file length:
        //  - the start length comes from the handle, else the file on disk; if neither is
        //    known nothing is written (no way to tell later what landed);
        //  - after a failure the handle is closed and the end length is re-derived from a
        //    reopened handle (else the file on disk), never from the faulted handle;
        //  - a torn partial row is truncated; if truncation fails the fragment is left, the
        //    file is marked so the next write starts with a newline, and tornRows is counted.
        private bool AppendRows(string fileName, byte[] buffer, int total, int prefix, int[] rowEnds, int rowCount, out int landed, out Exception failure)
        {
            landed = 0;
            failure = null;
            OpenFile file;
            long start;
            try
            {
                file = GetOrOpen(fileName);
                start = MeasureLength(fileName, file.Stream);
            }
            catch (Exception ex)
            {
                CloseStream(fileName);
                failure = ex;
                return false;
            }

            if (start < 0L)
            {
                CloseStream(fileName);
                failure = new IOException("telemetry file length unavailable before write: " + fileName);
                return false;
            }

            try
            {
                file.Stream.Write(buffer, 0, total);
                file.Stream.Flush();
                landed = rowCount;
                ClearTorn(fileName);
                return true;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            CloseStream(fileName);
            long end = MeasureLengthAfterFault(fileName);
            if (end < 0L)
            {
                // Unknowable: assume nothing landed, but isolate any fragment from the retry.
                MarkTorn(fileName, countRow: false);
                return false;
            }

            long written = Math.Max(0L, end - start);
            if (prefix > 0 && written >= prefix)
                ClearTorn(fileName);

            int boundary = written >= prefix ? prefix : 0;
            while (landed < rowCount && rowEnds[landed] <= written)
            {
                boundary = rowEnds[landed];
                landed++;
            }

            if (written > boundary && !TryTruncateFile(fileName, start + boundary))
                MarkTorn(fileName, countRow: true);
            return false;
        }

        private long MeasureLength(string fileName, Stream stream)
        {
            long length = TryLength(stream);
            return length >= 0L ? length : LengthOnDisk(fileName);
        }

        // Never trusts the faulted handle (already closed): a fresh non-append handle, else the
        // file on disk.
        private long MeasureLengthAfterFault(string fileName)
        {
            try
            {
                using (Stream fresh = OpenFileStream(Path.Combine(_sessionDirectory, fileName), FileMode.Open))
                {
                    long length = TryLength(fresh);
                    if (length >= 0L)
                        return length;
                }
            }
            catch
            {
            }

            return LengthOnDisk(fileName);
        }

        private long LengthOnDisk(string fileName)
        {
            try
            {
                var info = new FileInfo(Path.Combine(_sessionDirectory, fileName));
                return info.Exists ? info.Length : -1L;
            }
            catch
            {
                return -1L;
            }
        }

        // The append handle is already closed here, so the next write reopens at the true end.
        private bool TryTruncateFile(string fileName, long length)
        {
            try
            {
                using (Stream fresh = OpenFileStream(Path.Combine(_sessionDirectory, fileName), FileMode.Open))
                    fresh.SetLength(length);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void MarkTorn(string fileName, bool countRow)
        {
            lock (_streamGate)
                _tornFiles.Add(fileName);
            if (countRow)
                Interlocked.Increment(ref _tornRows);
        }

        private void ClearTorn(string fileName)
        {
            lock (_streamGate)
                _tornFiles.Remove(fileName);
        }

        private void CommitRows(string fileName, int count, double durationMs)
        {
            long bytes = 0L;
            long perfRows = 0L;
            long perfBytes = 0L;
            for (int i = 0; i < count; i++)
            {
                PendingRow row = _chunkRows[i];
                row.Done = true;
                bytes += row.Bytes;
                if (IsFileWritePerfRow(row.Event))
                {
                    perfRows++;
                    perfBytes += row.Bytes;
                }
            }

            if (perfRows > 0L)
                _fileWritePerf.RecordWritten(durationMs, perfBytes, perfRows);
            RecordOutput(fileName, bytes);
        }

        private void EnsureChunkCapacity(int needed)
        {
            if (needed <= _chunk.Length)
                return;
            int size = _chunk.Length;
            while (size < needed)
                size *= 2;
            Array.Resize(ref _chunk, size);
        }

        private void RemoveDoneRows()
        {
            int keep = 0;
            for (int i = 0; i < _pending.Count; i++)
            {
                if (!_pending[i].Done)
                    _pending[keep++] = _pending[i];
            }

            if (keep < _pending.Count)
                _pending.RemoveRange(keep, _pending.Count - keep);
        }

        private static long TryLength(Stream stream)
        {
            try
            {
                return stream.Length;
            }
            catch
            {
                return -1L;
            }
        }

        private bool InRetryBackoff()
        {
            long retryAt = Interlocked.Read(ref _retryNotBeforeTimestamp);
            if (retryAt == 0L)
                return false;
            if (Clock() < retryAt)
                return true;

            Interlocked.Exchange(ref _retryNotBeforeTimestamp, 0L);
            return false;
        }

        private void MaybeWriteManifest(bool force)
        {
            long now = Clock();
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

        private void OnWriteFailure(string reason, Exception ex)
        {
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
            Interlocked.Exchange(ref _retryNotBeforeTimestamp, Clock() + backoffMs * Stopwatch.Frequency / 1000L);
        }

        private OpenFile GetOrOpen(string fileName)
        {
            lock (_streamGate)
            {
                OpenFile file;
                if (_streams.TryGetValue(fileName, out file))
                    return file;

                file = new OpenFile(OpenFileStream(Path.Combine(_sessionDirectory, fileName), FileMode.Append), RotationIndexOf(fileName));
                _streams[fileName] = file;
                return file;
            }
        }

        private void CloseStream(string fileName)
        {
            lock (_streamGate)
            {
                OpenFile file;
                if (!_streams.TryGetValue(fileName, out file))
                    return;
                _streams.Remove(fileName);
                DisposeQuietly(file);
            }
        }

        private void CloseStreamsFromOldRotations()
        {
            lock (_streamGate)
            {
                if (_streams.Count == 0)
                    return;

                List<string> stale = null;
                foreach (KeyValuePair<string, OpenFile> entry in _streams)
                {
                    if (entry.Value.RotationIndex != _streamRotationIndex)
                        (stale ?? (stale = new List<string>())).Add(entry.Key);
                }

                if (stale == null)
                    return;
                for (int i = 0; i < stale.Count; i++)
                {
                    DisposeQuietly(_streams[stale[i]]);
                    _streams.Remove(stale[i]);
                }
            }
        }

        private void CloseAllStreams()
        {
            lock (_streamGate)
            {
                foreach (KeyValuePair<string, OpenFile> entry in _streams)
                    DisposeQuietly(entry.Value);
                _streams.Clear();
            }
        }

        private static void DisposeQuietly(OpenFile file)
        {
            try
            {
                file.Stream.Dispose();
            }
            catch
            {
            }
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

        // Self-reported sink-failure rows. On the writer thread they go straight through the
        // writer's own handle. From another thread while the writer is alive (shutdown
        // timeout) they are queued for the writer, never written through a second handle;
        // once the writer has exited (its handles are closed) a one-shot append is safe.
        private RowWriteResult WriteFailureRow(TelemetryEvent ev)
        {
            try
            {
                bool onWriterThread = Thread.CurrentThread == _thread;
                if (!onWriterThread && _thread.IsAlive)
                {
                    TelemetryQueueResult queued = _queue != null ? _queue.Enqueue(ev) : TelemetryQueueResult.Dropped;
                    Signal();
                    return queued == TelemetryQueueResult.Dropped ? RowWriteResult.Dropped : RowWriteResult.Written;
                }

                string json = TelemetryJson.ToJsonLine(ev);
                long bytes = Utf8NoBom.GetByteCount(json);
                string fileName = FileNameForNextWrite(ev, bytes);
                if (!_budget.TryReserve(ev.Category, bytes, lowPriority: true, protectedSummary: IsCapSurvivingRuntimeRow(ev)))
                    return RowWriteResult.Dropped;

                // Same torn-write handling as row chunks; one immediate retry (the fragment has
                // been truncated or isolated) before self-failure writes are given up.
                int landed = 0;
                bool ok = false;
                for (int attempt = 0; attempt < 2 && !ok && landed == 0; attempt++)
                {
                    int prefix = NeedsSeparator(fileName) ? 1 : 0;
                    byte[] payload = new byte[prefix + (int)bytes];
                    if (prefix == 1)
                        payload[0] = (byte)'\n';
                    Utf8NoBom.GetBytes(json, 0, json.Length, payload, prefix);
                    int[] rowEnd = { payload.Length }; // call-local: failure rows can come from any thread

                    Exception failure;
                    ok = AppendRows(fileName, payload, payload.Length, prefix, rowEnd, 1, out landed, out failure);
                }

                if (!onWriterThread)
                    CloseStream(fileName); // the writer has exited; do not leave a handle open
                if (landed == 1)
                    RecordOutput(fileName, bytes);
                return landed == 1 ? RowWriteResult.Written : RowWriteResult.SinkFailed;
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
            long batchBytes;
            if (_prospectiveBytes.TryGetValue(candidate, out batchBytes))
                currentBytes += batchBytes;

            if (currentBytes > 0L && currentBytes + bytes > _budget.RotateBytes)
            {
                _budget.MarkRotated();
                rotationIndex = _budget.RotationIndex;
            }

            // Handles of the previous rotation are closed after this batch's chunks are
            // written (CloseStreamsFromOldRotations); nothing is buffered, so no flush here.
            if (rotationIndex != _streamRotationIndex)
            {
                if (_streamRotationIndex >= 0)
                {
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
            SinkFailed
        }

        private sealed class PendingRow
        {
            internal PendingRow(TelemetryEvent ev)
            {
                Event = ev;
            }

            internal readonly TelemetryEvent Event;
            internal string Json;
            internal long Bytes;
            internal string FileName;   // set once encoded + budget-reserved
            internal bool Done;         // written, dropped, or failed permanently
        }

        private sealed class OpenFile
        {
            internal OpenFile(Stream stream, int rotationIndex)
            {
                Stream = stream;
                RotationIndex = rotationIndex;
            }

            internal readonly Stream Stream;
            internal readonly int RotationIndex;
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

            internal void RecordWritten(double durationMs, long bytesWritten, long rows)
            {
                MarkStarted();
                _eventsEmitted += Math.Max(0L, rows);
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
