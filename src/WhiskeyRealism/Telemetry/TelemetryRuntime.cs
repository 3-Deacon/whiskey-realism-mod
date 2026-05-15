using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace WhiskeyRealism.Telemetry
{
    internal readonly struct TelemetryRuntimeDiagnostics
    {
        internal TelemetryRuntimeDiagnostics(
            int queueDepth,
            long emittedCount,
            long queueDroppedCount,
            long protectedOverflowCount,
            long budgetDroppedCount,
            long emittedBytes,
            long sinkFailureCount)
        {
            QueueDepth = queueDepth;
            EmittedCount = emittedCount;
            QueueDroppedCount = queueDroppedCount;
            ProtectedOverflowCount = protectedOverflowCount;
            BudgetDroppedCount = budgetDroppedCount;
            EmittedBytes = emittedBytes;
            SinkFailureCount = sinkFailureCount;
        }

        internal int QueueDepth { get; }
        internal long EmittedCount { get; }
        internal long QueueDroppedCount { get; }
        internal long ProtectedOverflowCount { get; }
        internal long BudgetDroppedCount { get; }
        internal long EmittedBytes { get; }
        internal long SinkFailureCount { get; }
        internal long DroppedCount => QueueDroppedCount + ProtectedOverflowCount + BudgetDroppedCount;

        internal static TelemetryRuntimeDiagnostics Empty =>
            new TelemetryRuntimeDiagnostics(0, 0L, 0L, 0L, 0L, 0L, 0L);
    }

    internal sealed class TelemetryRuntimeConfig
    {
        private TelemetryRuntimeConfig()
        {
        }

        internal string GameRoot { get; private set; }
        internal string PluginVersion { get; private set; }
        internal string RuntimeAssemblySha256 { get; private set; }
        internal TelemetryProfile Profile { get; private set; }
        internal long MaxTuningLogBytes { get; private set; }
        internal long FileRotateBytes { get; private set; }
        internal int RetainedSessions { get; private set; }
        internal bool EmitHumanSummary { get; private set; }
        internal bool PerformanceWarnings { get; private set; }
        internal bool CreateIssueBundleOnShutdown { get; private set; }
        internal Action<string> WarningCallback { get; private set; }

        internal static TelemetryRuntimeConfig Create(
            string gameRoot,
            string pluginVersion,
            string runtimeAssemblySha256,
            TelemetryProfile profile,
            int maxTuningLogMb,
            int fileRotateMb,
            int retainedSessions,
            bool emitHumanSummary,
            bool performanceWarnings,
            bool createIssueBundleOnShutdown,
            Action<string> warningCallback)
        {
            return new TelemetryRuntimeConfig
            {
                GameRoot = string.IsNullOrWhiteSpace(gameRoot) ? "." : gameRoot,
                PluginVersion = TelemetryEvent.Safe(pluginVersion),
                RuntimeAssemblySha256 = TelemetryEvent.Safe(runtimeAssemblySha256),
                Profile = profile,
                MaxTuningLogBytes = MegabytesToBytes(maxTuningLogMb, 250),
                FileRotateBytes = MegabytesToBytes(fileRotateMb, 25),
                RetainedSessions = Math.Max(0, retainedSessions),
                EmitHumanSummary = emitHumanSummary,
                PerformanceWarnings = performanceWarnings,
                CreateIssueBundleOnShutdown = createIssueBundleOnShutdown,
                WarningCallback = warningCallback
            };
        }

        private static long MegabytesToBytes(int value, int fallback)
        {
            int safe = value > 0 ? value : fallback;
            return (long)safe * 1024L * 1024L;
        }
    }

    internal sealed class TelemetryRuntime
    {
        private readonly object _gate = new object();
        private readonly object _manifestGate = new object();
        private readonly TelemetryRuntimeConfig _config;
        private readonly TelemetryQueue _queue;
        private readonly TelemetryBudget _budget;
        private readonly TelemetryWriter _writer;
        private readonly string _sessionDirectory;
        private readonly DateTime _startUtc;
        private bool _shutdown;
        private bool _finalManifestWritten;
        private bool _writerShutdownTimedOut;
        private long _acceptedCount;

        private TelemetryRuntime(TelemetryRuntimeConfig config)
        {
            _config = config;
            Profile = config != null ? config.Profile : TelemetryProfile.Off;
            SessionId = "-";
            _sessionDirectory = "-";
            _startUtc = DateTime.UtcNow;
        }

        private TelemetryRuntime(
            TelemetryRuntimeConfig config,
            string sessionId,
            string sessionDirectory,
            TelemetryQueue queue,
            TelemetryBudget budget)
        {
            _config = config;
            Profile = config.Profile;
            SessionId = TelemetryEvent.Safe(sessionId);
            _sessionDirectory = sessionDirectory;
            _queue = queue;
            _budget = budget;
            _startUtc = DateTime.UtcNow;
            _writer = new TelemetryWriter(_queue, _budget, _sessionDirectory, WriteManifest, SessionId, Profile, config.WarningCallback);
        }

        internal TelemetryProfile Profile { get; private set; }
        internal string SessionId { get; private set; }

        internal bool IsRunning
        {
            get { return _writer != null && !_shutdown; }
        }

        internal int UnflushedCount
        {
            get { return _queue != null ? _queue.Count : 0; }
        }

        internal TelemetryRuntimeDiagnostics DiagnosticsSnapshot()
        {
            try
            {
                long queueDropped = _queue != null ? _queue.DroppedCount : 0L;
                long protectedOverflow = _queue != null ? _queue.ProtectedOverflowCount : 0L;
                long budgetDropped = _budget != null ? _budget.DroppedCount : 0L;
                long sinkFailures = _writer != null ? _writer.SinkFailureCount : 0L;
                return new TelemetryRuntimeDiagnostics(
                    queueDepth: _queue != null ? _queue.Count : 0,
                    emittedCount: Interlocked.Read(ref _acceptedCount),
                    queueDroppedCount: queueDropped,
                    protectedOverflowCount: protectedOverflow,
                    budgetDroppedCount: budgetDropped,
                    emittedBytes: _budget != null ? _budget.EmittedBytes : 0L,
                    sinkFailureCount: sinkFailures);
            }
            catch
            {
                return TelemetryRuntimeDiagnostics.Empty;
            }
        }

        internal static TelemetryRuntime Start(TelemetryRuntimeConfig config)
        {
            if (config == null || config.Profile == TelemetryProfile.Off)
                return new TelemetryRuntime(config ?? TelemetryRuntimeConfig.Create(".", "-", "-", TelemetryProfile.Off, 250, 25, 2, true, true, false, null));

            try
            {
                string sessionBase = TelemetrySession.CreateSessionId(
                    DateTime.UtcNow,
                    SafeProcessId(),
                    config.RuntimeAssemblySha256);
                TelemetrySessionDirectory sessionDirectory = TelemetrySession.CreateUniqueSessionDirectory(config.GameRoot, sessionBase);
                TelemetrySession.ApplyRetention(config.GameRoot, sessionDirectory.SessionId, config.RetainedSessions);

                var queue = new TelemetryQueue(capacity: 8192);
                var budget = new TelemetryBudget(config.MaxTuningLogBytes, config.FileRotateBytes);
                var runtime = new TelemetryRuntime(config, sessionDirectory.SessionId, sessionDirectory.DirectoryPath, queue, budget);
                runtime.WriteManifest();
                runtime._writer.Start();
                return runtime;
            }
            catch (Exception ex)
            {
                Warn(config, "Telemetry startup failed closed: " + ex.GetType().Name + " " + ex.Message);
                return new TelemetryRuntime(config);
            }
        }

        internal bool TryEmit(TelemetryEvent ev)
        {
            try
            {
                if (!IsRunning || ev == null || _queue == null)
                    return false;
                if (!TelemetryRouter.ShouldEmit(Profile, ev.Layer, ev.Category))
                    return false;

                TelemetryQueueResult result = _queue.Enqueue(ev);
                if (result == TelemetryQueueResult.Enqueued && _writer != null)
                {
                    Interlocked.Increment(ref _acceptedCount);
                    _writer.Signal();
                }
                return result != TelemetryQueueResult.Dropped;
            }
            catch
            {
                return false;
            }
        }

        internal void Shutdown(string reason)
        {
            lock (_gate)
            {
                if (_shutdown)
                    return;
            }

            try
            {
                if (_writer != null)
                {
                    _queue.Enqueue(TelemetryEvent.Create(
                        SessionId,
                        Profile,
                        TelemetryLayer.System,
                        TelemetryCategory.Health,
                        "TelemetryShutdown",
                        TelemetrySeverity.Info).WithField("reason", TelemetryEvent.Safe(reason)));
                    try
                    {
                        WriteSummaryOnShutdown();
                    }
                    catch (Exception ex)
                    {
                        _writer.RecordRuntimeSinkFailure("summary-write", ex);
                        Warn(_config, "Telemetry summary failed closed: " + ex.GetType().Name);
                    }
                    _writer.Signal();
                    if (!_writer.StopAndFlush(2500))
                    {
                        lock (_manifestGate)
                            _writerShutdownTimedOut = true;
                    }
                    lock (_gate)
                        _shutdown = true;
                }
                else
                {
                    lock (_gate)
                        _shutdown = true;
                }
            }
            catch (Exception ex)
            {
                lock (_gate)
                    _shutdown = true;
                Warn(_config, "Telemetry shutdown failed closed: " + ex.GetType().Name);
            }

            try
            {
                WriteIssueBundleOnShutdown();
            }
            catch (Exception ex)
            {
                if (_writer != null)
                    _writer.RecordRuntimeSinkFailure("issue-bundle-write", ex);
                Warn(_config, "Telemetry issue bundle failed closed: " + ex.GetType().Name);
            }

            try
            {
                WriteManifest(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                Warn(_config, "Telemetry shutdown manifest failed closed: " + ex.GetType().Name);
            }
        }

        private void WriteManifest()
        {
            WriteManifest(null);
        }

        private void WriteManifest(DateTime? endUtc)
        {
            if (_budget == null || string.IsNullOrWhiteSpace(_sessionDirectory) || _sessionDirectory == "-")
                return;

            lock (_manifestGate)
            {
                if (!endUtc.HasValue && _finalManifestWritten)
                    return;

                var manifest = TelemetryManifest.Create(
                    SessionId,
                    _config.PluginVersion,
                    _config.RuntimeAssemblySha256,
                    Profile,
                    _startUtc,
                    _budget,
                    UnflushedCount);

                manifest.EndUtc = endUtc;
                manifest.ConfigSnapshot["loggingProfile"] = Profile.ToString();
                manifest.ConfigSnapshot["maxTuningLogBytes"] = _config.MaxTuningLogBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
                manifest.ConfigSnapshot["fileRotateBytes"] = _config.FileRotateBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
                manifest.ConfigSnapshot["retainedSessions"] = _config.RetainedSessions.ToString(System.Globalization.CultureInfo.InvariantCulture);
                manifest.ConfigSnapshot["emitHumanSummary"] = _config.EmitHumanSummary ? "true" : "false";
                manifest.ConfigSnapshot["performanceWarnings"] = _config.PerformanceWarnings ? "true" : "false";
                manifest.ConfigSnapshot["createIssueBundleOnShutdown"] = _config.CreateIssueBundleOnShutdown ? "true" : "false";

                if (_queue != null)
                {
                    foreach (KeyValuePair<string, long> entry in _queue.ProtectedOverflowSnapshot())
                        manifest.DroppedCounters["queueProtectedOverflow." + entry.Key] = entry.Value;
                    manifest.DroppedCounters["queueDropped"] = _queue.DroppedCount;
                }

                if (_writer != null)
                {
                    foreach (string file in _writer.OutputFilesSnapshot())
                        manifest.OutputFiles.Add(file);
                    manifest.DroppedCounters["sinkFailures"] = _writer.SinkFailureCount;
                }

                if (_writerShutdownTimedOut)
                    manifest.DroppedCounters["writerShutdownTimedOut"] = 1L;

                string path = Path.Combine(_sessionDirectory, "manifest.json");
                File.WriteAllText(path, manifest.ToJson());
                if (endUtc.HasValue)
                    _finalManifestWritten = true;
            }
        }

        private void WriteIssueBundleOnShutdown()
        {
            if (_config == null || !_config.CreateIssueBundleOnShutdown)
                return;
            if (string.IsNullOrWhiteSpace(_sessionDirectory) || _sessionDirectory == "-")
                return;

            var files = new List<string>();
            AddIfExists(files, Path.Combine(_sessionDirectory, "manifest.json"));
            AddManifestOutputFiles(files);
            if (_writer != null)
            {
                foreach (string file in _writer.OutputFilesSnapshot())
                    AddIfExists(files, Path.Combine(_sessionDirectory, file));
            }

            AddStandardShutdownOutputs(files);

            string note = "Telemetry issue bundle manifest generated on shutdown for session " + SessionId + ".";
            string json = TelemetryIssueBundle.CreateManifest(SessionId, _sessionDirectory, files, note).ToJson();
            File.WriteAllText(Path.Combine(_sessionDirectory, "issue-bundle.json"), json);
        }

        private void WriteSummaryOnShutdown()
        {
            if (_config == null || !_config.EmitHumanSummary)
                return;
            if (string.IsNullOrWhiteSpace(_sessionDirectory) || _sessionDirectory == "-")
                return;

            using (TelemetryPerf.Scope("telemetry.summary-generation", TelemetryLayer.System, TelemetryCategory.Performance, 5.0))
            {
                var lines = new List<string>();
                lines.Add("# Whiskey Realism Telemetry Summary");
                lines.Add("");
                lines.Add("- session: " + SessionId);
                lines.Add("- profile: " + Profile);
                lines.Add("- queuedRows: " + Interlocked.Read(ref _acceptedCount).ToString(System.Globalization.CultureInfo.InvariantCulture));
                lines.Add("- unflushedRows: " + UnflushedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (_budget != null)
                {
                    lines.Add("- emittedBytes: " + _budget.EmittedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    lines.Add("- budgetDroppedRows: " + _budget.DroppedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                if (_queue != null)
                {
                    lines.Add("- queueDroppedRows: " + _queue.DroppedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    lines.Add("- protectedOverflowRows: " + _queue.ProtectedOverflowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                if (_writer != null)
                    lines.Add("- sinkFailures: " + _writer.SinkFailureCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

                using (TelemetryPerf.Scope("telemetry.summary-file-write", TelemetryLayer.System, TelemetryCategory.Performance, 5.0))
                    File.WriteAllText(Path.Combine(_sessionDirectory, "summary.md"), string.Join(Environment.NewLine, lines) + Environment.NewLine);
            }
        }

        private void AddManifestOutputFiles(List<string> files)
        {
            try
            {
                string manifestPath = Path.Combine(_sessionDirectory, "manifest.json");
                if (!File.Exists(manifestPath))
                    return;

                JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var outputs = manifest["outputFiles"] as JArray;
                if (outputs == null)
                    return;

                for (int i = 0; i < outputs.Count; i++)
                {
                    string fileName = (string)outputs[i];
                    if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0)
                        continue;
                    AddIfExists(files, Path.Combine(_sessionDirectory, fileName));
                }
            }
            catch
            {
            }
        }

        private void AddStandardShutdownOutputs(List<string> files)
        {
            AddIfExists(files, Path.Combine(_sessionDirectory, "summary.md"));
            AddIfExists(files, Path.Combine(_sessionDirectory, "health.jsonl"));
            AddIfExists(files, Path.Combine(_sessionDirectory, "failures.jsonl"));
            AddIfExists(files, Path.Combine(_sessionDirectory, "performance.jsonl"));
            AddIfExists(files, Path.Combine(_sessionDirectory, "tactical.jsonl"));
            AddIfExists(files, Path.Combine(_sessionDirectory, "campaign.jsonl"));
        }

        private static void AddIfExists(List<string> files, string path)
        {
            if (files == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            for (int i = 0; i < files.Count; i++)
            {
                if (string.Equals(files[i], path, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            files.Add(path);
        }

        private static int SafeProcessId()
        {
            try
            {
                return Process.GetCurrentProcess().Id;
            }
            catch
            {
                return 0;
            }
        }

        private static void Warn(TelemetryRuntimeConfig config, string message)
        {
            try
            {
                if (config != null && config.WarningCallback != null)
                    config.WarningCallback(message);
            }
            catch
            {
            }
        }
    }
}
