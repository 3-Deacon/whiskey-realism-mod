using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace WhiskeyRealism.Telemetry
{
    internal sealed class TelemetryWriter
    {
        internal const int FlushIntervalMs = 250;
        internal const int FlushBatchSize = 256;

        private readonly TelemetryQueue _queue;
        private readonly TelemetryBudget _budget;
        private readonly string _sessionDirectory;
        private readonly Action _manifestWriter;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly object _gate = new object();
        private readonly HashSet<string> _outputFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _fileBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Thread _thread;
        private volatile bool _stopRequested;
        private long _sinkFailureCount;

        internal TelemetryWriter(
            TelemetryQueue queue,
            TelemetryBudget budget,
            string sessionDirectory,
            Action manifestWriter)
        {
            _queue = queue;
            _budget = budget;
            _sessionDirectory = sessionDirectory;
            _manifestWriter = manifestWriter;
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

        internal void StopAndFlush(int timeoutMs)
        {
            _stopRequested = true;
            Signal();
            try
            {
                if (!_thread.Join(Math.Max(1, timeoutMs)))
                    RecordSinkFailure("writer-shutdown-timeout", null);
            }
            catch (Exception ex)
            {
                RecordSinkFailure("writer-shutdown", ex);
            }
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
                _signal.WaitOne(FlushIntervalMs);
                FlushBatch(FlushBatchSize);
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (_queue.Count > 0 && DateTime.UtcNow < deadline)
                FlushBatch(FlushBatchSize);

            SafeWriteManifest();
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

            for (int i = 0; i < rows.Count; i++)
                WriteRow(rows[i]);

            SafeWriteManifest();
        }

        private void WriteRow(TelemetryEvent ev)
        {
            try
            {
                string json = TelemetryJson.ToJsonLine(ev);
                long bytes = System.Text.Encoding.UTF8.GetByteCount(json);
                string fileName = FileNameForNextWrite(ev, bytes);
                if (!_budget.TryReserve(ev.Category, bytes))
                    return;

                string path = Path.Combine(_sessionDirectory, fileName);
                File.AppendAllText(path, json);
                lock (_gate)
                {
                    _outputFiles.Add(fileName);
                    long existing;
                    _fileBytes.TryGetValue(fileName, out existing);
                    _fileBytes[fileName] = existing + bytes;
                }
            }
            catch (Exception ex)
            {
                RecordSinkFailure("write-row", ex);
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
            lock (_gate)
                _sinkFailureCount++;

            try
            {
                string message = ex == null ? reason : reason + ": " + ex.GetType().Name;
                var failure = TelemetryEvent.Create(
                    "sink",
                    TelemetryProfile.FullTuning,
                    TelemetryLayer.System,
                    TelemetryCategory.Failure,
                    "TelemetrySinkFailure",
                    TelemetrySeverity.Warning)
                    .WithField("reason", message);
                string json = TelemetryJson.ToJsonLine(failure);
                string path = Path.Combine(_sessionDirectory, FileNameFor(failure, _budget.RotationIndex));
                File.AppendAllText(path, json);
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
    }
}
