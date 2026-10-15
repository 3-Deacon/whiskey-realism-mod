using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Util;

using static TestFixtures;
using static TestHarness;

internal static class TelemetrySessionTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("telemetry runtime config sanitizes smoke controls", TelemetryRuntimeConfigSanitizesSmokeControls),
            ("telemetry runtime manifest snapshots smoke controls", TelemetryRuntimeManifestSnapshotsSmokeControls),
            ("telemetry writer honors configured flush rows", TelemetryWriterHonorsConfiguredFlushRows),
            ("telemetry writer shutdown drains past internal deadline", TelemetryWriterShutdownDrainsPastInternalDeadline),
            ("telemetry session id sorts by utc milliseconds", TelemetrySessionIdSortsByUtcMilliseconds),
            ("telemetry manifest redacts user paths", TelemetryManifestRedactsUserPaths),
            ("telemetry session creates directory under tuning logs", TelemetrySessionCreatesDirectoryUnderTuningLogs),
            ("telemetry session sanitizes hostile directory ids under tuning logs", TelemetrySessionSanitizesHostileDirectoryIdsUnderTuningLogs),
            ("telemetry session creates unique directory on collision", TelemetrySessionCreatesUniqueDirectoryOnCollision),
            ("telemetry retention keeps newest two by manifest dir and mtime", TelemetryRetentionKeepsNewestTwoByManifestDirAndMtime),
            ("telemetry retention deletes old dirs and pins current", TelemetryRetentionDeletesOldDirsAndPinsCurrent),
            ("telemetry retention refuses deletes outside tuning root", TelemetryRetentionRefusesDeletesOutsideTuningRoot),
            ("telemetry issue bundle redacts spaced windows usernames", TelemetryIssueBundleRedactsSpacedWindowsUsernames),
            ("telemetry issue bundle redacts quoted secrets", TelemetryIssueBundleRedactsQuotedSecrets),
            ("telemetry issue bundle redacts public bundle secrets and user paths", TelemetryIssueBundleRedactsPublicBundleSecretsAndUserPaths),
            ("telemetry retention no manifest orders by directory before mtime", TelemetryRetentionNoManifestOrdersByDirectoryBeforeMtime),
            ("telemetry retention mixed manifest prefers directory before mtime", TelemetryRetentionMixedManifestPrefersDirectoryBeforeMtime),
            ("telemetry issue bundle redacts json and bearer equals secrets", TelemetryIssueBundleRedactsJsonAndBearerEqualsSecrets),
            ("telemetry issue bundle redacts auth bypass forms", TelemetryIssueBundleRedactsAuthBypassForms),
            ("telemetry issue bundle preserves json redaction validity", TelemetryIssueBundlePreservesJsonRedactionValidity),
            ("telemetry issue bundle redacts nested json strings", TelemetryIssueBundleRedactsNestedJsonStrings),
            ("telemetry issue bundle manifest has explicit json", TelemetryIssueBundleManifestHasExplicitJson),
            ("telemetry issue bundle manifest redacts unsafe session id", TelemetryIssueBundleManifestRedactsUnsafeSessionId),
            ("telemetry issue bundle manifest filters non telemetry files", TelemetryIssueBundleManifestFiltersNonTelemetryFiles),
            ("telemetry issue bundle manifest scopes files to active session", TelemetryIssueBundleManifestScopesFilesToActiveSession),
            ("telemetry issue bundle scopes equivalent windows and wsl paths", TelemetryIssueBundleScopesEquivalentWindowsAndWslPaths),
            ("telemetry summary includes drops failures and slow scopes", TelemetrySummaryIncludesDropsFailuresAndSlowScopes),
            ("telemetry validator tolerates partial final line", TelemetryValidatorToleratesPartialFinalLine),
            ("telemetry validator rejects non telemetry json", TelemetryValidatorRejectsNonTelemetryJson),
            ("telemetry validator rejects empty session directory", TelemetryValidatorRejectsEmptySessionDirectory),
            ("telemetry validator rejects session without valid rows", TelemetryValidatorRejectsSessionWithoutValidRows),
            ("telemetry validator cli rejects missing path", TelemetryValidatorCliRejectsMissingPath),
            ("telemetry runtime full tuning writes sidecars and drains shutdown", TelemetryRuntimeFullTuningWritesSidecarsAndDrainsShutdown),
            ("telemetry runtime budget drops oversized detail rows", TelemetryRuntimeBudgetDropsOversizedDetailRows),
            ("telemetry runtime protected summaries survive consumed budget", TelemetryRuntimeProtectedSummariesSurviveConsumedBudget),
            ("telemetry writer sink failure rows use output accounting", TelemetryWriterSinkFailureRowsUseOutputAccounting),
            ("telemetry writer reports shutdown timeout", TelemetryWriterReportsShutdownTimeout),
            ("telemetry writer sink failure rows use active identity", TelemetryWriterSinkFailureRowsUseActiveIdentity),
            ("telemetry writer aggregates file write performance", TelemetryWriterAggregatesFileWritePerformance),
            ("telemetry runtime aggregates file write performance across small drains", TelemetryRuntimeAggregatesFileWritePerformanceAcrossSmallDrains),
            ("telemetry runtime final manifest ignores late non final writes", TelemetryRuntimeFinalManifestIgnoresLateNonFinalWrites),
            ("telemetry runtime writes scoped issue bundle on shutdown", TelemetryRuntimeWritesScopedIssueBundleOnShutdown),
            ("telemetry runtime final manifest reports issue bundle failure", TelemetryRuntimeFinalManifestReportsIssueBundleFailure),
            ("telemetry writer bounds repeated sink failures", TelemetryWriterBoundsRepeatedSinkFailures),
        });
    }

    private static void TelemetryRuntimeConfigSanitizesSmokeControls()
    {
        var defaults = TelemetryRuntimeConfig.Create(
            ".",
            "0.2.2-test",
            "abcdef1234567890",
            TelemetryProfile.FullTuning,
            maxTuningLogMb: 250,
            fileRotateMb: 25,
            retainedSessions: 2,
            emitHumanSummary: true,
            performanceWarnings: true,
            createIssueBundleOnShutdown: false,
            warningCallback: null);

        AssertEqual(TelemetryRuntimeConfig.DefaultQueueCapacity, defaults.QueueCapacity, "default queue capacity");
        AssertEqual(TelemetryRuntimeConfig.DefaultFlushMilliseconds, defaults.FlushMilliseconds, "default flush milliseconds");
        AssertEqual(TelemetryRuntimeConfig.DefaultFlushRows, defaults.FlushRows, "default flush rows");

        var low = TelemetryRuntimeConfig.Create(
            ".",
            "0.2.2-test",
            "abcdef1234567890",
            TelemetryProfile.FullTuning,
            maxTuningLogMb: 250,
            fileRotateMb: 25,
            retainedSessions: 2,
            emitHumanSummary: true,
            performanceWarnings: true,
            createIssueBundleOnShutdown: false,
            warningCallback: null,
            queueCapacity: int.MinValue,
            flushMilliseconds: int.MinValue,
            flushRows: int.MinValue);

        AssertEqual(TelemetryRuntimeConfig.MinQueueCapacity, low.QueueCapacity, "low queue capacity clamps");
        AssertEqual(TelemetryRuntimeConfig.MinFlushMilliseconds, low.FlushMilliseconds, "low flush milliseconds clamps");
        AssertEqual(TelemetryRuntimeConfig.MinFlushRows, low.FlushRows, "low flush rows clamps");

        var high = TelemetryRuntimeConfig.Create(
            ".",
            "0.2.2-test",
            "abcdef1234567890",
            TelemetryProfile.FullTuning,
            maxTuningLogMb: 250,
            fileRotateMb: 25,
            retainedSessions: 2,
            emitHumanSummary: true,
            performanceWarnings: true,
            createIssueBundleOnShutdown: false,
            warningCallback: null,
            queueCapacity: int.MaxValue,
            flushMilliseconds: int.MaxValue,
            flushRows: int.MaxValue);

        AssertEqual(TelemetryRuntimeConfig.MaxQueueCapacity, high.QueueCapacity, "high queue capacity clamps");
        AssertEqual(TelemetryRuntimeConfig.MaxFlushMilliseconds, high.FlushMilliseconds, "high flush milliseconds clamps");
        AssertEqual(TelemetryRuntimeConfig.MaxFlushRows, high.FlushRows, "high flush rows clamps");
    }

    private static void TelemetryRuntimeManifestSnapshotsSmokeControls()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: null,
                queueCapacity: 1024,
                flushMilliseconds: 777,
                flushRows: 17);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            runtime.Shutdown("config-snapshot");

            JObject snapshot = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")))["configSnapshot"];
            AssertEqual("1024", (string)snapshot["queueCapacity"], "manifest queue capacity");
            AssertEqual("777", (string)snapshot["flushMilliseconds"], "manifest flush milliseconds");
            AssertEqual("17", (string)snapshot["flushRows"], "manifest flush rows");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryWriterHonorsConfiguredFlushRows()
    {
        string sessionDirectory = CreateTempDirectory();
        try
        {
            var queue = new TelemetryQueue(capacity: 8);
            var budget = new TelemetryBudget(totalBytes: 65536, rotateBytes: 65536);
            int manifestCalls = 0;
            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(TelemetryEvent.Create(
                    "flush-session",
                    TelemetryProfile.FullTuning,
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "FlushRowsDecision",
                    TelemetrySeverity.Info).WithDecision("hold", "flush-test", "sig-flush-" + i.ToString(CultureInfo.InvariantCulture)));
            }

            var writer = new TelemetryWriter(
                queue,
                budget,
                sessionDirectory,
                delegate { manifestCalls++; },
                "flush-session",
                TelemetryProfile.FullTuning,
                null,
                flushMilliseconds: 10000,
                flushRows: 2);

            writer.Start();
            AssertTrue(writer.StopAndFlush(1000), "writer stops after configured row batches");
            AssertEqual(5, CountJsonLines(File.ReadAllText(Path.Combine(sessionDirectory, "tactical.jsonl"))), "configured flush writer row count");
            // WS P P-queue: the manifest is rewritten on new output file / rotation / timer /
            // shutdown only, not once per flush batch.
            AssertTrue(manifestCalls >= 1 && manifestCalls <= 3, "flushRows=2 batches do not each rewrite the manifest");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryWriterShutdownDrainsPastInternalDeadline()
    {
        string sessionDirectory = CreateTempDirectory();
        try
        {
            var queue = new TelemetryQueue(capacity: 16);
            var budget = new TelemetryBudget(totalBytes: 65536, rotateBytes: 65536);
            int manifestCalls = 0;
            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(TelemetryEvent.Create(
                    "shutdown-drain-session",
                    TelemetryProfile.FullTuning,
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "ShutdownDrainDecision",
                    TelemetrySeverity.Info).WithDecision("hold", "shutdown-drain", "sig-shutdown-" + i.ToString(CultureInfo.InvariantCulture)));
            }

            var writer = new TelemetryWriter(
                queue,
                budget,
                sessionDirectory,
                delegate
                {
                    if (System.Threading.Interlocked.Increment(ref manifestCalls) == 1)
                        System.Threading.Thread.Sleep(2200);
                },
                "shutdown-drain-session",
                TelemetryProfile.FullTuning,
                null,
                flushMilliseconds: 5000,
                flushRows: 1);

            writer.Start();
            AssertTrue(writer.StopAndFlush(6000), "writer should finish when caller timeout is long enough");
            AssertEqual(0, queue.Count, "successful shutdown must drain queued rows");
            AssertEqual(5, CountJsonLines(File.ReadAllText(Path.Combine(sessionDirectory, "tactical.jsonl"))), "shutdown drain writes every queued row");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryRuntimeFullTuningWritesSidecarsAndDrainsShutdown()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            AssertTrue(runtime.IsRunning, "full tuning runtime starts writer");

            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalDecision", TelemetrySeverity.Info).WithDecision("advance", "test", "sig-a")), "tactical event accepted");
            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.Campaign, TelemetryCategory.State, "CampaignState", TelemetrySeverity.Info).WithField("campaign", "ok")), "campaign event accepted");
            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.System, TelemetryCategory.Performance, "PerformanceWarning", TelemetrySeverity.Warning).WithDurationMs(12.5)), "performance event accepted");
            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.System, TelemetryCategory.Failure, "FailureWarning", TelemetrySeverity.Warning).WithField("reason", "test")), "failure event accepted");

            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            runtime.Shutdown("test-full");

            AssertFalse(runtime.IsRunning, "runtime stopped after shutdown");
            AssertEqual(0, runtime.UnflushedCount, "shutdown drains queue");
            AssertTrue(Directory.Exists(sessionDirectory), "session directory exists");
            AssertTrue(File.Exists(Path.Combine(sessionDirectory, "manifest.json")), "manifest exists");
            AssertTrue(File.Exists(Path.Combine(sessionDirectory, "tactical.jsonl")), "tactical sidecar exists");
            AssertTrue(File.Exists(Path.Combine(sessionDirectory, "campaign.jsonl")), "campaign sidecar exists");
            AssertTrue(File.Exists(Path.Combine(sessionDirectory, "performance.jsonl")), "performance sidecar exists");
            AssertTrue(File.Exists(Path.Combine(sessionDirectory, "failures.jsonl")), "failures sidecar exists");

            JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")));
            AssertEqual(runtime.SessionId, (string)manifest["sessionId"], "manifest session id");
            AssertEqual("FullTuning", (string)manifest["profile"], "manifest profile");
            AssertTrue(manifest["endUtc"].Type != JTokenType.Null, "manifest records shutdown end");
            AssertTrue(JsonArrayContainsSubstring((JArray)manifest["outputFiles"], "tactical.jsonl"), "manifest includes tactical output");
            AssertTrue(JsonArrayContainsSubstring((JArray)manifest["outputFiles"], "campaign.jsonl"), "manifest includes campaign output");
            AssertTrue(JsonArrayContainsSubstring((JArray)manifest["outputFiles"], "performance.jsonl"), "manifest includes performance output");
            AssertTrue(JsonArrayContainsSubstring((JArray)manifest["outputFiles"], "failures.jsonl"), "manifest includes failures output");
            AssertTrue(manifest["droppedCounters"]["queueDropped"] != null, "manifest includes queue drop state");
            AssertTrue(manifest["droppedCounters"]["sinkFailures"] != null, "manifest includes sink failure state");

            string summary = File.ReadAllText(Path.Combine(sessionDirectory, "summary.md"));
            AssertContains(summary, "- sessionId: " + runtime.SessionId, "runtime summary uses telemetry summary session id");
            AssertContains(summary, "- pluginVersion: 0.2.2-test", "runtime summary includes plugin version");
            AssertContains(summary, "- runtimeAssemblySha256: abcdef1234567890", "runtime summary includes runtime assembly hash");
            AssertContains(summary, "Tactical=1", "runtime summary includes tactical count");
            AssertContains(summary, "Campaign=1", "runtime summary includes campaign count");
            AssertContains(summary, "FailureWarning=1", "runtime summary includes failure event count");
            AssertContains(summary, "category=Failure", "runtime summary includes inspection query");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryRuntimeBudgetDropsOversizedDetailRows()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 1,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            string huge = new string('x', 1024 * 1024);
            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.Tactical, TelemetryCategory.Decision, "OversizedDecision", TelemetrySeverity.Info).WithField("payload", huge)), "oversized row queues before writer budget cut");
            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.System, TelemetryCategory.Failure, "ProtectedFailure", TelemetrySeverity.Warning).WithField("reason", "after-drop")), "protected row queues after drop");

            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            runtime.Shutdown("test-budget");

            JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")));
            AssertTrue((long)manifest["droppedCounters"]["Decision"] >= 1L, "manifest records budget decision drop");
            AssertTrue(File.Exists(Path.Combine(sessionDirectory, "failures.jsonl")), "protected failure sidecar survives tiny cap");
            AssertEqual(0, runtime.UnflushedCount, "budget shutdown drains queue");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryRuntimeProtectedSummariesSurviveConsumedBudget()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 1,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            // A Warning row fills up to the total cap (B6: Info Write rows stop at 98%).
            var probe = TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.Tactical, TelemetryCategory.Write, "BudgetFiller", TelemetrySeverity.Warning).WithField("payload", "");
            int fillerOverhead = System.Text.Encoding.UTF8.GetByteCount(TelemetryJson.ToJsonLine(probe));
            string filler = new string('x', (int)Math.Max(1L, config.MaxTuningLogBytes - fillerOverhead - 40L));

            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.Tactical, TelemetryCategory.Write, "BudgetFiller", TelemetrySeverity.Warning).WithField("payload", filler)), "filler row queues before writer consumes budget");
            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.System, TelemetryCategory.Failure, "RawFailureDetail", TelemetrySeverity.Warning).WithField("payload", new string('d', 128))), "raw failure detail queues before writer cap check");
            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.System, TelemetryCategory.Failure, "FailureSummary", TelemetrySeverity.Warning).WithField("protectedSummary", true).WithField("reason", "after-cap")), "failure summary queues before writer cap check");

            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            runtime.Shutdown("after-cap");

            string failures = File.ReadAllText(Path.Combine(sessionDirectory, "failures.jsonl"));
            string health = File.ReadAllText(Path.Combine(sessionDirectory, "health.jsonl"));
            JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")));
            AssertContains(failures, "\"event\":\"FailureSummary\"", "protected failure summary survives consumed cap");
            AssertFalse(failures.Contains("\"event\":\"RawFailureDetail\""), "raw failure detail remains capped after consumed budget");
            AssertContains(health, "\"event\":\"TelemetryShutdown\"", "shutdown health survives consumed cap");
            AssertTrue((long)manifest["droppedCounters"]["Failure"] >= 1L, "raw failure detail budget drop recorded");
            AssertTrue(JsonArrayContainsSubstring((JArray)manifest["outputFiles"], "failures.jsonl"), "manifest includes protected failure output");
            AssertTrue(JsonArrayContainsSubstring((JArray)manifest["outputFiles"], "health.jsonl"), "manifest includes shutdown health output");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryWriterSinkFailureRowsUseOutputAccounting()
    {
        string sessionDirectory = CreateTempDirectory();
        try
        {
            var queue = new TelemetryQueue(capacity: 8);
            var budget = new TelemetryBudget(totalBytes: 256, rotateBytes: 64);
            TelemetryWriter writer = null;
            writer = new TelemetryWriter(queue, budget, sessionDirectory, delegate
            {
                var manifest = TelemetryManifest.Create("sink-test", "0.2.2-test", "abcdef1234567890", TelemetryProfile.FullTuning, DateTime.UtcNow, budget, queue.Count);
                foreach (string file in writer.OutputFilesSnapshot())
                    manifest.OutputFiles.Add(file);
                File.WriteAllText(Path.Combine(sessionDirectory, "manifest.json"), manifest.ToJson());
            });

            writer.RecordRuntimeSinkFailure("unit-test-sink", null);
            writer.Start();
            AssertTrue(writer.StopAndFlush(1000), "writer stops after sink failure accounting manifest");

            string failuresPath = Path.Combine(sessionDirectory, "failures.jsonl");
            AssertTrue(File.Exists(failuresPath), "sink failure sidecar written");
            AssertContains(File.ReadAllText(failuresPath), "\"event\":\"TelemetrySinkFailure\"", "sink failure row event");
            AssertTrue(budget.EmittedBytes > 0L, "sink failure row consumes budget accounting");
            AssertTrue(writer.OutputFilesSnapshot().Contains("failures.jsonl"), "sink failure row appears in output file accounting");
            JObject manifestJson = JObject.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")));
            AssertTrue(JsonArrayContainsSubstring((JArray)manifestJson["outputFiles"], "failures.jsonl"), "manifest includes sink failure output");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryWriterReportsShutdownTimeout()
    {
        string sessionDirectory = CreateTempDirectory();
        var enteredManifest = new System.Threading.ManualResetEvent(false);
        var releaseManifest = new System.Threading.ManualResetEvent(false);
        var warnings = new List<string>();
        try
        {
            var queue = new TelemetryQueue(capacity: 8);
            var budget = new TelemetryBudget(totalBytes: 4096, rotateBytes: 1024);
            int manifestCalls = 0;
            var writer = new TelemetryWriter(
                queue,
                budget,
                sessionDirectory,
                delegate
                {
                    if (System.Threading.Interlocked.Increment(ref manifestCalls) == 1)
                    {
                        enteredManifest.Set();
                        releaseManifest.WaitOne(5000);
                    }
                },
                "timeout-session",
                TelemetryProfile.CampaignTuning,
                warnings.Add);

            writer.Start();
            queue.Enqueue(TelemetryEvent.Create("timeout-session", TelemetryProfile.CampaignTuning, TelemetryLayer.Tactical, TelemetryCategory.Decision, "TimeoutDecision", TelemetrySeverity.Info).WithDecision("hold", "test", "sig-timeout"));
            writer.Signal();

            AssertTrue(enteredManifest.WaitOne(1000), "writer entered blocking manifest callback");
            AssertFalse(writer.StopAndFlush(1), "writer reports timeout when background thread is still blocked");
            AssertEqual(1, warnings.Count, "shutdown timeout emits one warning callback");
            AssertContains(warnings[0], "Telemetry writer shutdown timed out", "shutdown timeout warning message");
            releaseManifest.Set();
            AssertTrue(writer.StopAndFlush(1000), "writer joins after blocked manifest callback releases");
        }
        finally
        {
            releaseManifest.Set();
            enteredManifest.Dispose();
            releaseManifest.Dispose();
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryWriterSinkFailureRowsUseActiveIdentity()
    {
        string sessionDirectory = CreateTempDirectory();
        try
        {
            var queue = new TelemetryQueue(capacity: 8);
            var budget = new TelemetryBudget(totalBytes: 4096, rotateBytes: 1024);
            var writer = new TelemetryWriter(
                queue,
                budget,
                sessionDirectory,
                delegate { },
                "active-session",
                TelemetryProfile.CampaignTuning,
                null);

            writer.RecordRuntimeSinkFailure("unit-test-sink", null);
            writer.Start();
            AssertTrue(writer.StopAndFlush(1000), "writer stops after active identity sink failure");

            string failuresPath = Path.Combine(sessionDirectory, "failures.jsonl");
            AssertTrue(File.Exists(failuresPath), "sink failure sidecar written");
            JObject row = ParseJsonLine(File.ReadAllText(failuresPath));
            AssertEqual("active-session", (string)row["sessionId"], "sink failure session id");
            AssertEqual("CampaignTuning", (string)row["profile"], "sink failure profile");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryWriterAggregatesFileWritePerformance()
    {
        string gameRoot = CreateTempDirectory();
        string writerSessionDirectory = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            TelemetryRouter.AttachRuntime(runtime);
            string runtimeSessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            const int normalRows = 5;

            var queue = new TelemetryQueue(capacity: 16);
            var budget = new TelemetryBudget(totalBytes: 65536, rotateBytes: 65536);
            for (int i = 0; i < normalRows; i++)
            {
                queue.Enqueue(TelemetryEvent.Create(
                    "writer-batch",
                    TelemetryProfile.FullTuning,
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "BatchDecision",
                    TelemetrySeverity.Info).WithDecision("hold", "batch", "sig-" + i.ToString(CultureInfo.InvariantCulture)));
            }

            var writer = new TelemetryWriter(
                queue,
                budget,
                writerSessionDirectory,
                delegate { },
                "writer-batch",
                TelemetryProfile.FullTuning,
                null);

            writer.Start();
            AssertTrue(writer.StopAndFlush(1000), "batch writer stops after preloaded rows");

            TelemetryRouter.Shutdown("writer-performance-test");

            string tactical = File.ReadAllText(Path.Combine(writerSessionDirectory, "tactical.jsonl"));
            AssertEqual(normalRows, CountJsonLines(tactical), "normal writer row count");

            string performance = File.ReadAllText(Path.Combine(runtimeSessionDirectory, "performance.jsonl"));
            int fileWriteScopes = CountPerformanceScope(performance, "telemetry.file-write");
            AssertTrue(fileWriteScopes > 0, "writer emits file write performance");
            AssertTrue(fileWriteScopes < normalRows, "writer file write performance is aggregated, not one row per normal row");
            AssertEqual(1, CountPerformanceScopeWithNumber(performance, "telemetry.file-write", "eventsEmitted", normalRows), "one file write performance row summarizes the normal row batch");
            AssertTrue(AnyPerformanceScopeWithNumberAtLeast(performance, "telemetry.file-write", "bytesWritten", 1.0), "aggregated file write performance records bytes written");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteDirectoryQuietly(gameRoot);
            DeleteDirectoryQuietly(writerSessionDirectory);
        }
    }

    private static void TelemetryRuntimeAggregatesFileWritePerformanceAcrossSmallDrains()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            TelemetryRouter.AttachRuntime(runtime);
            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            const int normalRows = 6;

            for (int i = 0; i < normalRows; i++)
            {
                AssertTrue(runtime.TryEmit(TelemetryEvent.Create(
                    runtime.SessionId,
                    runtime.Profile,
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "SmallDrainDecision",
                    TelemetrySeverity.Info).WithDecision("hold", "small-drain", "sig-" + i.ToString(CultureInfo.InvariantCulture))), "small drain row accepted");
                WaitForUnflushedCount(runtime, 0, 1000);
            }

            TelemetryRouter.Shutdown("small-drain-performance-test");

            string tactical = File.ReadAllText(Path.Combine(sessionDirectory, "tactical.jsonl"));
            AssertEqual(normalRows, CountJsonLines(tactical), "small drain normal writer row count");

            string performance = File.ReadAllText(Path.Combine(sessionDirectory, "performance.jsonl"));
            int fileWriteScopes = CountPerformanceScope(performance, "telemetry.file-write");
            AssertTrue(fileWriteScopes > 0, "small drain writer emits file write performance");
            AssertTrue(fileWriteScopes < normalRows, "small drain file write performance is aggregated, not one row per normal row");
            AssertEqual((double)normalRows + 1.0, SumPerformanceScopeNumber(performance, "telemetry.file-write", "eventsEmitted"), "small drain aggregate accounts for normal and shutdown rows");
            AssertTrue(AnyPerformanceScopeWithNumberAtLeast(performance, "telemetry.file-write", "bytesWritten", 1.0), "small drain aggregate records bytes written");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryRuntimeFinalManifestIgnoresLateNonFinalWrites()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: false,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            runtime.Shutdown("final-manifest");

            JObject finalManifest = JObject.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")));
            AssertTrue(finalManifest["endUtc"].Type != JTokenType.Null, "shutdown writes final manifest");

            typeof(TelemetryRuntime)
                .GetMethod("WriteManifest", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                .Invoke(runtime, new object[0]);

            JObject afterLateWrite = JObject.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")));
            AssertTrue(afterLateWrite["endUtc"].Type != JTokenType.Null, "late non-final manifest cannot clear final end");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryRuntimeWritesScopedIssueBundleOnShutdown()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: true,
                warningCallback: null);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            File.WriteAllText(Path.Combine(sessionDirectory, "summary.md"), "shutdown summary");
            File.WriteAllText(Path.Combine(sessionDirectory, "not-telemetry.txt"), "token=secret");
            AssertTrue(runtime.TryEmit(TelemetryEvent.Create(runtime.SessionId, runtime.Profile, TelemetryLayer.Tactical, TelemetryCategory.Decision, "BundleDecision", TelemetrySeverity.Info).WithDecision("hold", "bundle", "sig-bundle")), "bundle event accepted");

            runtime.Shutdown("test-bundle");

            string bundlePath = Path.Combine(sessionDirectory, "issue-bundle.json");
            AssertTrue(File.Exists(bundlePath), "issue bundle written on shutdown");
            string bundleJson = File.ReadAllText(bundlePath);
            JObject bundle = JObject.Parse(bundleJson);
            AssertEqual(runtime.SessionId, (string)bundle["sessionId"], "bundle session id");
            JArray files = (JArray)bundle["files"];
            AssertTrue(files.Count >= 3, "bundle includes known telemetry files");
            AssertTrue(JsonArrayContainsSubstring(files, "manifest.json"), "bundle includes manifest");
            AssertTrue(JsonArrayContainsSubstring(files, "tactical.jsonl"), "bundle includes tactical output");
            AssertTrue(JsonArrayContainsSubstring(files, "summary.md"), "bundle includes standard summary output");
            AssertFalse(JsonArrayContainsSubstring(files, "not-telemetry.txt"), "bundle excludes non-telemetry file");
            AssertFalse(bundleJson.Contains("token=secret"), "bundle does not include unrelated secret content");
            for (int i = 0; i < files.Count; i++)
                AssertContains((string)files[i], runtime.SessionId, "bundle file scoped to session");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryRuntimeFinalManifestReportsIssueBundleFailure()
    {
        string gameRoot = CreateTempDirectory();
        var warnings = new List<string>();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot,
                "0.2.2-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                maxTuningLogMb: 4,
                fileRotateMb: 1,
                retainedSessions: 2,
                emitHumanSummary: true,
                performanceWarnings: true,
                createIssueBundleOnShutdown: true,
                warningCallback: warnings.Add);

            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            string sessionDirectory = Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
            Directory.CreateDirectory(Path.Combine(sessionDirectory, "issue-bundle.json"));

            runtime.Shutdown("bundle-failure");

            string failuresPath = Path.Combine(sessionDirectory, "failures.jsonl");
            AssertTrue(File.Exists(failuresPath), "issue bundle failure writes failures sidecar");
            AssertContains(File.ReadAllText(failuresPath), "issue-bundle-write", "failure row records issue bundle reason");
            JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(sessionDirectory, "manifest.json")));
            AssertTrue(manifest["endUtc"].Type != JTokenType.Null, "final manifest keeps endUtc");
            AssertTrue((long)manifest["droppedCounters"]["sinkFailures"] >= 1L, "final manifest records issue bundle sink failure");
            AssertTrue(JsonArrayContainsSubstring((JArray)manifest["outputFiles"], "failures.jsonl"), "final manifest includes failure sidecar");
            AssertTrue(warnings.Count >= 1, "issue bundle failure emits bounded warning callback");

            string summary = File.ReadAllText(Path.Combine(sessionDirectory, "summary.md"));
            AssertContains(summary, "Failure=1", "summary regenerated after issue bundle failure includes failure count");
            AssertContains(summary, "TelemetrySinkFailure=1", "summary regenerated after issue bundle failure includes failure event");
            AssertContains(summary, "category=Failure", "summary regenerated after issue bundle failure includes inspection query");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryWriterBoundsRepeatedSinkFailures()
    {
        string sessionDirectory = CreateTempDirectory();
        var warnings = new List<string>();
        try
        {
            Directory.Delete(sessionDirectory, true);
            var queue = new TelemetryQueue(capacity: 16);
            var budget = new TelemetryBudget(totalBytes: 4096, rotateBytes: 1024);
            var writer = new TelemetryWriter(
                queue,
                budget,
                sessionDirectory,
                delegate { },
                "missing-session",
                TelemetryProfile.FullTuning,
                warnings.Add);

            writer.Start();
            for (int i = 0; i < 5; i++)
            {
                queue.Enqueue(TelemetryEvent.Create(
                    "missing-session",
                    TelemetryProfile.FullTuning,
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "MissingSinkDecision",
                    TelemetrySeverity.Info).WithDecision("hold", "missing-sink", "sig-" + i.ToString(CultureInfo.InvariantCulture)));
            }

            writer.Signal();
            AssertTrue(writer.StopAndFlush(1000), "writer stops with missing session directory");

            AssertTrue(writer.SinkFailureCount >= 5L, "writer counts each failed row");
            AssertTrue(warnings.Count <= 2, "sink warnings are bounded by failure mode");
            AssertTrue(warnings.Count >= 1, "sink failure emits warning callback");
            AssertFalse(Directory.Exists(sessionDirectory), "writer does not recreate missing session directory while failing");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetrySessionIdSortsByUtcMilliseconds()
    {
        string a = TelemetrySession.CreateSessionId(new DateTime(2026, 5, 15, 12, 0, 0, 1, DateTimeKind.Utc), 12, "abcdef1234567890");
        string b = TelemetrySession.CreateSessionId(new DateTime(2026, 5, 15, 12, 0, 0, 2, DateTimeKind.Utc), 12, "abcdef1234567890");
        AssertTrue(string.CompareOrdinal(a, b) < 0, "session ids sort by start time");
        AssertContains(a, "p12", "pid");
        AssertContains(a, "abcdef123456", "hash prefix");
    }

    private static void TelemetryManifestRedactsUserPaths()
    {
        string redacted = TelemetryIssueBundle.Redact(@"C:\Users\Kyle\AppData\Roaming\test token=secret");
        AssertContains(redacted, @"C:\Users\<redacted>", "user path redacted");
        AssertFalse(redacted.Contains("secret"), "token redacted");
    }

    private static void TelemetrySessionCreatesDirectoryUnderTuningLogs()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            string sessionId = "20260515-120000-000-p12-abcdef123456";
            string sessionDirectory = TestFixtures.CreateSessionDirectory(gameRoot, sessionId);
            string expected = Path.Combine(gameRoot, "BepInEx", "WhiskeyRealism", "tuning-logs", sessionId);

            AssertTrue(Directory.Exists(sessionDirectory), "session directory created");
            AssertEqual(Path.GetFullPath(expected), Path.GetFullPath(sessionDirectory), "session directory path");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetrySessionSanitizesHostileDirectoryIdsUnderTuningLogs()
    {
        string gameRoot = CreateTempDirectory();
        string outsideRoot = CreateTempDirectory();
        try
        {
            string[] hostileIds =
            {
                "../escape",
                "..\\escape",
                "/tmp/wr-escape",
                @"C:\Windows\Temp\wr-escape",
                "C:/Windows/Temp/wr-escape",
                Path.Combine(outsideRoot, "absolute"),
                ".",
                ".."
            };

            string root = Path.GetFullPath(TelemetrySession.TuningLogRoot(gameRoot)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string hostileId in hostileIds)
            {
                string sessionDirectory = TestFixtures.CreateSessionDirectory(gameRoot, hostileId);
                string fullPath = Path.GetFullPath(sessionDirectory);
                AssertTrue(IsUnderDirectory(fullPath, root), "hostile id remains under tuning root: " + hostileId);
                AssertFalse(fullPath.Contains(".."), "hostile id does not retain traversal: " + hostileId);
            }

            AssertFalse(Directory.Exists(Path.Combine(outsideRoot, "absolute")), "absolute hostile path not created");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
            DeleteDirectoryQuietly(outsideRoot);
        }
    }

    private static void TelemetrySessionCreatesUniqueDirectoryOnCollision()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            string baseSessionId = "20260515-120000-000-p12-abcdef123456";
            TelemetrySessionDirectory first = TelemetrySession.CreateUniqueSessionDirectory(gameRoot, baseSessionId);
            TelemetrySessionDirectory second = TelemetrySession.CreateUniqueSessionDirectory(gameRoot, baseSessionId);

            AssertTrue(Directory.Exists(first.DirectoryPath), "first unique session directory exists");
            AssertTrue(Directory.Exists(second.DirectoryPath), "second unique session directory exists");
            AssertFalse(string.Equals(first.DirectoryPath, second.DirectoryPath, StringComparison.OrdinalIgnoreCase), "second unique session directory is distinct");
            AssertEqual(baseSessionId, first.SessionId, "first session id uses base");
            AssertEqual(baseSessionId + "-r001", second.SessionId, "second session id uses deterministic restart suffix");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryRetentionKeepsNewestTwoByManifestDirAndMtime()
    {
        var candidates = new[]
        {
            new TelemetryRetentionCandidate("old-with-start", new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc)),
            new TelemetryRetentionCandidate("same-start-a", new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 15, 8, 0, 0, DateTimeKind.Utc)),
            new TelemetryRetentionCandidate("same-start-b", new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 15, 8, 0, 0, DateTimeKind.Utc)),
            new TelemetryRetentionCandidate("mtime-fallback-new", null, new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Utc))
        };

        var deletes = TelemetrySession.SelectRetentionDeletes(candidates, keepNewest: 2);
        AssertEqual(2, deletes.Count, "delete count");
        AssertTrue(deletes.Exists(c => c.DirectoryName == "mtime-fallback-new"), "no-manifest mtime loses to directory name ordering");
        AssertTrue(deletes.Exists(c => c.DirectoryName == "old-with-start"), "oldest manifest start deleted");
        AssertFalse(deletes.Exists(c => c.DirectoryName == "same-start-a"), "same manifest start older directory retained ahead of mtime fallback");
        AssertFalse(deletes.Exists(c => c.DirectoryName == "same-start-b"), "newer directory name retained");
    }

    private static void TelemetryRetentionDeletesOldDirsAndPinsCurrent()
    {
        string gameRoot = CreateTempDirectory();
        try
        {
            string currentSession = "20260515-080000-000-p1-current0000";
            string newestSession = "20260515-120000-000-p1-newest0000";
            string middleSession = "20260515-110000-000-p1-middle0000";
            string malformedSession = "20260515-070000-000-p1-badmanifest";

            string currentDir = TestFixtures.CreateSessionDirectory(gameRoot, currentSession);
            string newestDir = CreateTelemetrySessionDirectory(gameRoot, newestSession, "2026-05-15T12:00:00Z");
            string middleDir = CreateTelemetrySessionDirectory(gameRoot, middleSession, "2026-05-15T11:00:00Z");
            string malformedDir = CreateTelemetrySessionDirectory(gameRoot, malformedSession, null);
            File.WriteAllText(Path.Combine(malformedDir, "manifest.json"), "{ malformed manifest");

            Directory.SetLastWriteTimeUtc(currentDir, new DateTime(2026, 5, 15, 8, 0, 0, DateTimeKind.Utc));
            Directory.SetLastWriteTimeUtc(newestDir, new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc));
            Directory.SetLastWriteTimeUtc(middleDir, new DateTime(2026, 5, 15, 11, 0, 0, DateTimeKind.Utc));
            Directory.SetLastWriteTimeUtc(malformedDir, new DateTime(2026, 5, 15, 7, 0, 0, DateTimeKind.Utc));

            var deletes = TelemetrySession.ApplyRetention(gameRoot, currentSession, keepNewest: 2);

            AssertEqual(2, deletes.Count, "deleted directory count");
            AssertTrue(Directory.Exists(currentDir), "current session pinned without manifest");
            AssertTrue(Directory.Exists(newestDir), "newest other session retained");
            AssertFalse(Directory.Exists(middleDir), "middle session deleted because current is pinned");
            AssertFalse(Directory.Exists(malformedDir), "malformed old session deleted without throwing");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
        }
    }

    private static void TelemetryRetentionRefusesDeletesOutsideTuningRoot()
    {
        string gameRoot = CreateTempDirectory();
        string outsideRoot = CreateTempDirectory();
        try
        {
            string insideDir = CreateTelemetrySessionDirectory(gameRoot, "20260515-090000-000-p1-inside0000", null);
            string outsideDir = Path.Combine(outsideRoot, "20260515-080000-000-p1-outside000");
            Directory.CreateDirectory(outsideDir);

            var candidates = new[]
            {
                new TelemetryRetentionCandidate(insideDir, Path.GetFileName(insideDir), null, new DateTime(2026, 5, 15, 9, 0, 0, DateTimeKind.Utc)),
                new TelemetryRetentionCandidate(outsideDir, Path.GetFileName(outsideDir), null, new DateTime(2026, 5, 15, 8, 0, 0, DateTimeKind.Utc))
            };

            var deleted = TelemetrySession.DeleteRetentionCandidates(gameRoot, candidates);

            AssertEqual(1, deleted.Count, "only in-root candidate deleted");
            AssertFalse(Directory.Exists(insideDir), "inside candidate deleted");
            AssertTrue(Directory.Exists(outsideDir), "outside candidate retained");
        }
        finally
        {
            DeleteDirectoryQuietly(gameRoot);
            DeleteDirectoryQuietly(outsideRoot);
        }
    }

    private static void TelemetryIssueBundleRedactsSpacedWindowsUsernames()
    {
        string redacted = TelemetryIssueBundle.Redact(@"C:\Users\Kyle Davis\AppData\Roaming\test");
        AssertContains(redacted, @"C:\Users\<redacted>\AppData", "spaced user path redacted");
        AssertFalse(redacted.Contains("Kyle Davis"), "spaced username removed");
    }

    private static void TelemetryIssueBundleRedactsQuotedSecrets()
    {
        string redacted = TelemetryIssueBundle.Redact("token=\"secret\" secret = 'value with spaces' api_key=\"abc\"");
        AssertFalse(redacted.Contains("secret\""), "quoted token value removed");
        AssertFalse(redacted.Contains("value with spaces"), "quoted secret value removed");
        AssertFalse(redacted.Contains("\"abc\""), "api key value removed");
        AssertContains(redacted, "token=<redacted>", "token redacted");
        AssertContains(redacted, "secret=<redacted>", "secret redacted");
        AssertContains(redacted, "api_key=<redacted>", "api key redacted");
    }

    private static void TelemetryIssueBundleRedactsPublicBundleSecretsAndUserPaths()
    {
        string redacted = TelemetryIssueBundle.Redact(
            "OPENAI_API_KEY=sk-test GH_TOKEN=\"ghp_secret\" access_token='abc def' password=hunter2 Authorization: Bearer abc123 /home/onebodyamerica/project /mnt/c/Users/Kyle Davis/AppData");

        AssertFalse(redacted.Contains("sk-test"), "openai key removed");
        AssertFalse(redacted.Contains("ghp_secret"), "github token removed");
        AssertFalse(redacted.Contains("abc def"), "access token removed");
        AssertFalse(redacted.Contains("hunter2"), "password removed");
        AssertFalse(redacted.Contains("abc123"), "bearer removed");
        AssertFalse(redacted.Contains("onebodyamerica"), "linux user path removed");
        AssertFalse(redacted.Contains("Kyle Davis"), "mnt windows user path removed");
        AssertContains(redacted, "OPENAI_API_KEY=<redacted>", "openai key context");
        AssertContains(redacted, "GH_TOKEN=<redacted>", "github token context");
        AssertContains(redacted, "access_token=<redacted>", "access token context");
        AssertContains(redacted, "password=<redacted>", "password context");
        AssertContains(redacted, "Authorization: Bearer <redacted>", "bearer context");
        AssertContains(redacted, "/home/<redacted>/project", "linux path context");
        AssertContains(redacted, "/mnt/c/Users/<redacted>/AppData", "mnt path context");
    }

    private static void TelemetryRetentionNoManifestOrdersByDirectoryBeforeMtime()
    {
        var candidates = new[]
        {
            new TelemetryRetentionCandidate("20260515-120001-newer-dir", null, new DateTime(2026, 5, 15, 9, 0, 0, DateTimeKind.Utc)),
            new TelemetryRetentionCandidate("20260515-120000-older-dir", null, new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc))
        };

        var ordered = TelemetrySession.OrderRetentionCandidates(candidates);
        AssertEqual("20260515-120001-newer-dir", ordered[0].DirectoryName, "newer directory name wins before mtime");
        AssertEqual("20260515-120000-older-dir", ordered[1].DirectoryName, "older directory name loses despite newer mtime");
    }

    private static void TelemetryRetentionMixedManifestPrefersDirectoryBeforeMtime()
    {
        var candidates = new[]
        {
            new TelemetryRetentionCandidate("20260515-120001-manifest", new DateTime(2026, 5, 15, 12, 0, 1, DateTimeKind.Utc), new DateTime(2026, 5, 15, 9, 0, 0, DateTimeKind.Utc)),
            new TelemetryRetentionCandidate("20260515-120000-no-manifest", null, new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Utc))
        };

        var ordered = TelemetrySession.OrderRetentionCandidates(candidates);
        AssertEqual("20260515-120001-manifest", ordered[0].DirectoryName, "manifest newer directory wins before no-manifest mtime");
        AssertEqual("20260515-120000-no-manifest", ordered[1].DirectoryName, "no-manifest mtime is final fallback only");
    }

    private static void TelemetryIssueBundleRedactsJsonAndBearerEqualsSecrets()
    {
        string redacted = TelemetryIssueBundle.Redact("\"token\":\"abc\" \"api_key\": \"def\" password: \"ghi\" Authorization=Bearer jkl");
        AssertFalse(redacted.Contains("abc"), "json token removed");
        AssertFalse(redacted.Contains("def"), "json api key removed");
        AssertFalse(redacted.Contains("ghi"), "colon password removed");
        AssertFalse(redacted.Contains("jkl"), "bearer equals removed");
        AssertContains(redacted, "\"token\":<redacted>", "json token context");
        AssertContains(redacted, "\"api_key\":<redacted>", "json api key context");
        AssertContains(redacted, "password:<redacted>", "password context");
        AssertContains(redacted, "Authorization=Bearer <redacted>", "bearer equals context");
    }

    private static void TelemetryIssueBundleRedactsAuthBypassForms()
    {
        string redacted = TelemetryIssueBundle.Redact("C:/Users/Kyle/AppData Authorization: Basic abc123 Authorization=Bearer def456 X-Api-Key: ghi789 client-secret: jkl012 Authorization=Basic mno345 X-Api-Key=pqr678");
        AssertFalse(redacted.Contains("Kyle"), "forward slash windows username removed");
        AssertFalse(redacted.Contains("abc123"), "basic auth removed");
        AssertFalse(redacted.Contains("def456"), "bearer equals removed");
        AssertFalse(redacted.Contains("ghi789"), "x api key removed");
        AssertFalse(redacted.Contains("jkl012"), "client secret removed");
        AssertFalse(redacted.Contains("mno345"), "basic equals removed");
        AssertFalse(redacted.Contains("pqr678"), "x api key equals removed");
        AssertContains(redacted, "C:/Users/<redacted>/AppData", "forward slash windows path context");
        AssertContains(redacted, "Authorization: Basic <redacted>", "basic auth context");
        AssertContains(redacted, "Authorization=Bearer <redacted>", "bearer equals context");
        AssertContains(redacted, "X-Api-Key:<redacted>", "x api key context");
        AssertContains(redacted, "client-secret:<redacted>", "client secret context");
        AssertContains(redacted, "Authorization=Basic <redacted>", "basic equals context");
        AssertContains(redacted, "X-Api-Key=<redacted>", "x api key equals context");
    }

    private static void TelemetryIssueBundlePreservesJsonRedactionValidity()
    {
        string redacted = TelemetryIssueBundle.Redact("{\"token\":\"abc\",\"password\":\"hunter2\"}");
        var parsed = JObject.Parse(redacted);
        AssertEqual("<redacted>", (string)parsed["token"], "json token value redacted");
        AssertEqual("<redacted>", (string)parsed["password"], "json password value redacted");
        AssertFalse(redacted.Contains("abc"), "json raw token removed");
        AssertFalse(redacted.Contains("hunter2"), "json raw password removed");
    }

    private static void TelemetryIssueBundleRedactsNestedJsonStrings()
    {
        string redacted = TelemetryIssueBundle.Redact("{\"path\":\"C:\\\\Users\\\\Kyle\\\\AppData\",\"headers\":{\"Authorization\":\"Bearer abc\"}}");
        var parsed = JObject.Parse(redacted);

        AssertFalse(redacted.Contains("Kyle"), "json path username removed");
        AssertFalse(redacted.Contains("abc"), "json authorization value removed");
        AssertContains((string)parsed["path"], @"C:\Users\<redacted>\AppData", "json path redacted");
        string authorization = (string)parsed["headers"]["Authorization"];
        AssertTrue(authorization == "<redacted>" || authorization == "Bearer <redacted>", "json authorization redacted");
    }

    private static void TelemetryIssueBundleManifestHasExplicitJson()
    {
        string sessionDirectory = @"C:\Users\Kyle\AppData\BepInEx\WhiskeyRealism\tuning-logs\session-1";
        TelemetryIssueBundleManifest manifest = TelemetryIssueBundle.CreateManifest(
            "session-1",
            sessionDirectory,
            new[] { sessionDirectory + @"\manifest.json" },
            "Authorization=Bearer abc token=secret");

        string json = manifest.ToJson();
        var parsed = JObject.Parse(json);

        AssertEqual("session-1", (string)parsed["sessionId"], "manifest session id");
        AssertContains((string)parsed["files"][0], @"C:\Users\<redacted>\AppData", "manifest file redacted");
        AssertContains((string)parsed["note"], "<redacted>", "manifest note redacted");
        AssertFalse(json.Contains("Kyle"), "manifest username removed");
        AssertFalse(json.Contains("abc"), "manifest authorization value removed");
        AssertFalse(json.Contains("secret"), "manifest token value removed");
    }

    private static void TelemetryIssueBundleManifestRedactsUnsafeSessionId()
    {
        TelemetryIssueBundleManifest manifest = TelemetryIssueBundle.CreateManifest(
            @"C:\Users\Kyle\AppData Authorization=Bearer abc token=secret",
            new[] { "manifest.json" },
            "safe note");

        string json = manifest.ToJson();
        var parsed = JObject.Parse(json);

        AssertTrue(!string.IsNullOrWhiteSpace((string)parsed["sessionId"]), "manifest session id retained");
        AssertFalse(json.Contains("Kyle"), "manifest session username removed");
        AssertFalse(json.Contains("abc"), "manifest session bearer removed");
        AssertFalse(json.Contains("secret"), "manifest session token removed");
    }

    private static void TelemetryIssueBundleManifestFiltersNonTelemetryFiles()
    {
        string sessionDirectory = @"C:\Games\Grand Tactician\BepInEx\WhiskeyRealism\tuning-logs\session-1";
        TelemetryIssueBundleManifest manifest = TelemetryIssueBundle.CreateManifest(
            "session-1",
            sessionDirectory,
            new[]
            {
                sessionDirectory + @"\manifest.json",
                sessionDirectory + @"\summary.md",
                sessionDirectory + @"\campaign.001.jsonl",
                @"C:\Games\Grand Tactician\BepInEx\plugins\WhiskeyRealism.dll",
                @"C:\Games\Grand Tactician\Campaigns\001\save.dat",
                @"C:\Games\Grand Tactician\BepInEx\LogOutput.log",
                @"C:\Games\Grand Tactician\BepInEx\plugins\OtherPlugin.log",
                @"C:\Users\Kyle\Documents\summary.md"
            },
            null);

        string json = manifest.ToJson();
        var parsed = JObject.Parse(json);
        var files = (JArray)parsed["files"];

        AssertEqual(3, files.Count, "only telemetry-owned files included");
        AssertContains((string)files[0], "manifest.json", "manifest included");
        AssertContains((string)files[1], "summary.md", "summary included");
        AssertContains((string)files[2], "campaign.001.jsonl", "rotated campaign included");
        AssertFalse(json.Contains("WhiskeyRealism.dll"), "dll excluded");
        AssertFalse(json.Contains("save.dat"), "save excluded");
        AssertFalse(json.Contains("LogOutput.log"), "bepinex log excluded");
        AssertFalse(json.Contains("OtherPlugin.log"), "other plugin log excluded");
        AssertFalse(json.Contains("Kyle"), "user document path excluded");
    }

    private static void TelemetryIssueBundleManifestScopesFilesToActiveSession()
    {
        string sessionA = @"C:\Games\Grand Tactician\BepInEx\WhiskeyRealism\tuning-logs\session-a";
        string sessionB = @"C:\Games\Grand Tactician\BepInEx\WhiskeyRealism\tuning-logs\session-b";
        TelemetryIssueBundleManifest manifest = TelemetryIssueBundle.CreateManifest(
            "session-a",
            sessionA,
            new[]
            {
                sessionA + @"\manifest.json",
                sessionA + @"\summary.md",
                sessionA + @"\health.jsonl",
                sessionA + @"\failures.002.jsonl",
                sessionB + @"\manifest.json",
                sessionB + @"\summary.md",
                sessionA + @"\health.2.jsonl",
                sessionA + @"\health.0002.jsonl",
                sessionA + @"\LogOutput.log",
                "manifest.json",
                @"C:\Users\Kyle\Documents\tokens\summary.md"
            },
            null);

        string json = manifest.ToJson();
        var parsed = JObject.Parse(json);
        var files = (JArray)parsed["files"];

        AssertEqual(4, files.Count, "only active session telemetry files included");
        AssertContains((string)files[0], @"session-a\manifest.json", "manifest included");
        AssertContains((string)files[1], @"session-a\summary.md", "summary included");
        AssertContains((string)files[2], @"session-a\health.jsonl", "health included");
        AssertContains((string)files[3], @"session-a\failures.002.jsonl", "rotated failures included");
        AssertFalse(json.Contains("session-b"), "other session excluded");
        AssertFalse(json.Contains("LogOutput.log"), "bepinex log excluded");
        AssertFalse(json.Contains("Kyle"), "user document username redacted or excluded");
        AssertFalse(json.Contains("tokens"), "user token path excluded");
    }

    private static void TelemetryIssueBundleScopesEquivalentWindowsAndWslPaths()
    {
        string windowsSession = @"C:\Games\Grand Tactician\BepInEx\WhiskeyRealism\tuning-logs\session-a";
        string wslSession = "/mnt/c/Games/Grand Tactician/BepInEx/WhiskeyRealism/tuning-logs/session-a";

        TelemetryIssueBundleManifest windowsManifest = TelemetryIssueBundle.CreateManifest(
            "session-a",
            windowsSession,
            new[]
            {
                wslSession + "/manifest.json",
                "/mnt/c/Games/Grand Tactician/BepInEx/WhiskeyRealism/tuning-logs/session-b/manifest.json"
            },
            null);

        TelemetryIssueBundleManifest wslManifest = TelemetryIssueBundle.CreateManifest(
            "session-a",
            wslSession,
            new[]
            {
                windowsSession + @"\summary.md",
                @"C:\Games\Grand Tactician\BepInEx\WhiskeyRealism\tuning-logs\session-b\summary.md"
            },
            null);

        JArray windowsFiles = (JArray)JObject.Parse(windowsManifest.ToJson())["files"];
        JArray wslFiles = (JArray)JObject.Parse(wslManifest.ToJson())["files"];

        AssertEqual(1, windowsFiles.Count, "windows session accepts equivalent wsl active-session file only");
        AssertContains((string)windowsFiles[0], "manifest.json", "equivalent wsl manifest included");
        AssertEqual(1, wslFiles.Count, "wsl session accepts equivalent windows active-session file only");
        AssertContains((string)wslFiles[0], "summary.md", "equivalent windows summary included");
    }

    private static void TelemetrySummaryIncludesDropsFailuresAndSlowScopes()
    {
        string sessionDirectory = CreateTempDirectory();
        try
        {
            var budget = new TelemetryBudget(totalBytes: 1024, rotateBytes: 512);
            budget.RecordDropped(TelemetryCategory.Decision);
            var manifest = TelemetryManifest.Create(
                "summary-session",
                "0.8.0-test",
                "abcdef1234567890",
                TelemetryProfile.FullTuning,
                new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc),
                budget,
                unflushedCount: 3);
            manifest.EndUtc = new DateTime(2026, 5, 15, 12, 30, 0, DateTimeKind.Utc);
            manifest.DroppedCounters["queueDropped"] = 2L;
            manifest.DroppedCounters["queueProtectedOverflow.Failure"] = 1L;
            manifest.DroppedCounters["capTransition.detailToSummary"] = 1L;
            manifest.DroppedCounters["sinkFailures"] = 4L;
            manifest.OutputFiles.Add("tactical.jsonl");
            manifest.OutputFiles.Add("failures.jsonl");
            manifest.OutputFiles.Add("performance.jsonl");
            File.WriteAllText(Path.Combine(sessionDirectory, "manifest.json"), manifest.ToJson());

            WriteJsonLines(
                Path.Combine(sessionDirectory, "tactical.jsonl"),
                SummaryRow("summary-session", "FullTuning", "Tactical", "Decision", "TacticalDecision", "Info", "1862-05-01", "battle-7", 4.0, "advance", "odds-high", "sig-a", new Dictionary<string, object>()),
                SummaryRow("summary-session", "FullTuning", "Tactical", "Gate", "ReserveGate", "Warning", "1862-05-01", "battle-7", 2.0, "deny", "reserve-low", "sig-b", new Dictionary<string, object> { { "allowed", false }, { "anchor", "reserve" } }),
                SummaryRow("summary-session", "FullTuning", "Tactical", "Write", "PostureWrite", "Info", "1862-05-02", "battle-8", 3.0, "write", "applied", "sig-c", new Dictionary<string, object> { { "result", "applied" } }));
            WriteJsonLines(
                Path.Combine(sessionDirectory, "failures.jsonl"),
                SummaryRow("summary-session", "FullTuning", "System", "Failure", "TelemetrySinkFailure", "Error", "-", "-", 1.0, "-", "write-row", "-", new Dictionary<string, object> { { "reason", "write-row" } }),
                SummaryRow("summary-session", "FullTuning", "System", "Failure", "TelemetrySinkFailure", "Error", "-", "-", 1.0, "-", "write-row", "-", new Dictionary<string, object> { { "reason", "write-row" } }),
                SummaryRow("summary-session", "FullTuning", "Campaign", "Failure", "MissingAnchor", "Warning", "1862-05-03", "-", 1.0, "-", "missing-anchor", "-", new Dictionary<string, object> { { "anchor", "CampaignObjective" } }));
            WriteJsonLines(
                Path.Combine(sessionDirectory, "performance.jsonl"),
                SummaryRow("summary-session", "FullTuning", "System", "Performance", "PerfScope", "Warning", "-", "-", 27.5, "-", "-", "-", new Dictionary<string, object> { { "scope", "telemetry.flush" } }),
                SummaryRow("summary-session", "FullTuning", "System", "Performance", "PerfScope", "Warning", "-", "-", 42.0, "-", "-", "-", new Dictionary<string, object> { { "scope", "telemetry.summary-generation" } }));

            TelemetrySummary summary = TelemetrySummary.FromDirectory(sessionDirectory);
            string markdown = summary.ToMarkdown();

            AssertEqual("summary-session", summary.SessionId, "summary session id");
            AssertEqual("FullTuning", summary.Profile, "summary profile");
            AssertContains(markdown, "pluginVersion: 0.8.0-test", "plugin version");
            AssertContains(markdown, "runtimeAssemblySha256: abcdef1234567890", "assembly hash");
            AssertContains(markdown, "campaignDates: 1862-05-01, 1862-05-02, 1862-05-03", "campaign date range");
            AssertContains(markdown, "battles: battle-7, battle-8", "battle ids");
            AssertContains(markdown, "Tactical=3", "layer count");
            AssertContains(markdown, "Decision=1", "category count");
            AssertContains(markdown, "TacticalDecision=1", "event count");
            AssertContains(markdown, "advance=1", "top decision");
            AssertContains(markdown, "odds-high=1", "top reason");
            AssertContains(markdown, "ReserveGate reserve-low", "denied gate");
            AssertContains(markdown, "PostureWrite applied=1", "write result");
            AssertContains(markdown, "telemetry.summary-generation 42", "slow scope");
            AssertContains(markdown, "queueDropped=2", "queue drop");
            AssertContains(markdown, "budgetDrops: Decision=1", "budget drop");
            AssertContains(markdown, "detailToSummary=1", "cap transition");
            AssertContains(markdown, "runtimeCounters: sinkFailures=4", "manifest sink failure counter");
            AssertContains(markdown, "TelemetrySinkFailure=4", "manifest reconciles sink failure event count");
            AssertContains(markdown, "TelemetrySinkFailure write-row x2", "repeated failure");
            AssertContains(markdown, "TelemetrySinkFailure manifest-sinkFailures x2", "manifest sink failures repeated");
            AssertContains(markdown, "CampaignObjective", "missing anchor");
            AssertContains(markdown, "category=Failure", "inspection query");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryValidatorToleratesPartialFinalLine()
    {
        string sessionDirectory = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(sessionDirectory, "health.jsonl"),
                SummaryRow("validator-session", "FullTuning", "System", "Health", "TelemetryStartup", "Info", "-", "-", 0.0, "-", "-", "-", new Dictionary<string, object>())
                + Environment.NewLine
                + "{\"schema\":\"wr.telemetry.v1\",\"event\":\"partial\"");

            TelemetryValidationResult result = TelemetrySessionValidator.ValidateDirectory(sessionDirectory);
            AssertTrue(result.Success, "partial final json line is tolerated");
            AssertContains(result.Summary, "validRows=1", "valid row count");
            AssertContains(result.Summary, "partialFinalLines=1", "partial final count");
            AssertContains(result.Summary, "invalidRows=0", "invalid row count");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryValidatorRejectsNonTelemetryJson()
    {
        string sessionDirectory = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(sessionDirectory, "health.jsonl"),
                SummaryRow("validator-session", "FullTuning", "System", "Health", "TelemetryStartup", "Info", "-", "-", 0.0, "-", "-", "-", new Dictionary<string, object>())
                + Environment.NewLine
                + "{}"
                + Environment.NewLine
                + "{\"schema\":\"wrong\",\"event\":\"bad\"}"
                + Environment.NewLine
                + "42"
                + Environment.NewLine
                + "{\"schema\":\"wr.telemetry.v1\",\"ts\":0,\"sessionId\":{},\"profile\":999,\"layer\":[],\"category\":{},\"event\":false,\"severity\":true}"
                + Environment.NewLine
                + "{\"schema\":\"wr.telemetry.v1\",\"ts\":\"2026-05-15T12:00:00Z\",\"sessionId\":\"validator-session\",\"profile\":\"Unknown\",\"layer\":\"System\",\"category\":\"Health\",\"event\":\"bad-profile\",\"severity\":\"Info\"}"
                + Environment.NewLine);

            TelemetryValidationResult result = TelemetrySessionValidator.ValidateDirectory(sessionDirectory);
            AssertFalse(result.Success, "non telemetry json rows fail validation");
            AssertContains(result.Summary, "validRows=1", "valid telemetry row count");
            AssertContains(result.Summary, "invalidRows=5", "invalid telemetry row count");
            AssertContains(result.Summary, "health.jsonl:2", "empty object line reported");
            AssertContains(result.Summary, "health.jsonl:3", "wrong schema line reported");
            AssertContains(result.Summary, "health.jsonl:4", "scalar json line reported");
            AssertContains(result.Summary, "health.jsonl:5", "bad field types line reported");
            AssertContains(result.Summary, "health.jsonl:6", "bad enum domain line reported");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryValidatorRejectsEmptySessionDirectory()
    {
        string sessionDirectory = CreateTempDirectory();
        try
        {
            TelemetryValidationResult result = TelemetrySessionValidator.ValidateDirectory(sessionDirectory);
            AssertFalse(result.Success, "empty telemetry session directory fails validation");
            AssertContains(result.Summary, "files=0", "empty directory file count");
            AssertContains(result.Summary, "invalidRows=1", "empty directory invalid marker");
            AssertContains(result.Summary, "no telemetry JSONL files found", "empty directory issue");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryValidatorRejectsSessionWithoutValidRows()
    {
        string sessionDirectory = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(sessionDirectory, "health.jsonl"),
                Environment.NewLine
                + "{\"schema\":\"wr.telemetry.v1\",\"event\":\"partial\"");

            TelemetryValidationResult result = TelemetrySessionValidator.ValidateDirectory(sessionDirectory);
            AssertFalse(result.Success, "session without valid telemetry rows fails validation");
            AssertContains(result.Summary, "files=1", "blank file count");
            AssertContains(result.Summary, "validRows=0", "no valid row count");
            AssertContains(result.Summary, "partialFinalLines=1", "partial final line still tracked");
            AssertContains(result.Summary, "no valid telemetry rows found", "no valid row issue");
        }
        finally
        {
            DeleteDirectoryQuietly(sessionDirectory);
        }
    }

    private static void TelemetryValidatorCliRejectsMissingPath()
    {
        AssertEqual(1, Program.Main(new[] { "--validate-telemetry" }), "validator cli missing path exit code");
        AssertEqual(1, Program.Main(new[] { "--unknown-telemetry-arg" }), "validator cli unknown arg exit code");
    }

    private static void WriteJsonLines(string path, params string[] rows)
    {
        File.WriteAllText(path, string.Join(Environment.NewLine, rows) + Environment.NewLine);
    }

    private static string SummaryRow(
        string sessionId,
        string profile,
        string layer,
        string category,
        string eventName,
        string severity,
        string campaignDate,
        string battleId,
        double durationMs,
        string decision,
        string reason,
        string inputSignature,
        Dictionary<string, object> fields)
    {
        var row = new JObject
        {
            ["schema"] = TelemetryEvent.Schema,
            ["ts"] = "2026-05-15T12:00:00.0000000Z",
            ["sessionId"] = sessionId,
            ["profile"] = profile,
            ["layer"] = layer,
            ["category"] = category,
            ["event"] = eventName,
            ["severity"] = severity,
            ["campaignDate"] = campaignDate,
            ["battleId"] = battleId,
            ["side"] = -1,
            ["alliance"] = -1,
            ["unit"] = "-",
            ["phase"] = "-",
            ["durationMs"] = durationMs,
            ["decision"] = decision,
            ["reason"] = reason,
            ["inputSignature"] = inputSignature,
            ["fields"] = JObject.FromObject(fields ?? new Dictionary<string, object>())
        };

        return row.ToString(Formatting.None);
    }

    private static bool JsonArrayContainsSubstring(JArray array, string expected)
    {
        if (array == null)
            return false;

        for (int i = 0; i < array.Count; i++)
        {
            string value = (string)array[i];
            if (value != null && value.Contains(expected))
                return true;
        }

        return false;
    }

    private static int CountJsonLines(string jsonl)
    {
        if (string.IsNullOrWhiteSpace(jsonl))
            return 0;

        int count = 0;
        using (var reader = new StringReader(jsonl))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    count++;
            }
        }

        return count;
    }

    private static int CountPerformanceScope(string jsonl, string scope)
    {
        if (string.IsNullOrWhiteSpace(jsonl))
            return 0;

        int count = 0;
        using (var reader = new StringReader(jsonl))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                JObject row = ParseJsonLine(line);
                JObject fields = row["fields"] as JObject;
                if (fields != null && string.Equals((string)fields["scope"], scope, StringComparison.Ordinal))
                    count++;
            }
        }

        return count;
    }

    private static int CountPerformanceScopeWithNumber(string jsonl, string scope, string fieldName, double expected)
    {
        int count = 0;
        foreach (JObject fields in PerformanceScopeFields(jsonl, scope))
        {
            JToken value = fields[fieldName];
            if (value != null && Math.Abs((double)value - expected) < 0.0001)
                count++;
        }

        return count;
    }

    private static bool AnyPerformanceScopeWithNumberAtLeast(string jsonl, string scope, string fieldName, double minimum)
    {
        foreach (JObject fields in PerformanceScopeFields(jsonl, scope))
        {
            JToken value = fields[fieldName];
            if (value != null && (double)value >= minimum)
                return true;
        }

        return false;
    }

    private static double SumPerformanceScopeNumber(string jsonl, string scope, string fieldName)
    {
        double sum = 0.0;
        foreach (JObject fields in PerformanceScopeFields(jsonl, scope))
        {
            JToken value = fields[fieldName];
            if (value != null)
                sum += (double)value;
        }

        return sum;
    }

    private static void WaitForUnflushedCount(TelemetryRuntime runtime, int expected, int timeoutMs)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (runtime.UnflushedCount == expected)
                return;
            System.Threading.Thread.Sleep(10);
        }

        AssertEqual(expected, runtime.UnflushedCount, "runtime unflushed count");
    }

    private static IEnumerable<JObject> PerformanceScopeFields(string jsonl, string scope)
    {
        if (string.IsNullOrWhiteSpace(jsonl))
            yield break;

        using (var reader = new StringReader(jsonl))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                JObject row = ParseJsonLine(line);
                JObject fields = row["fields"] as JObject;
                if (fields != null && string.Equals((string)fields["scope"], scope, StringComparison.Ordinal))
                    yield return fields;
            }
        }
    }

    private static string CreateTelemetrySessionDirectory(string gameRoot, string sessionId, string startUtc)
    {
        string directory = Path.Combine(gameRoot, "BepInEx", "WhiskeyRealism", "tuning-logs", sessionId);
        Directory.CreateDirectory(directory);
        if (startUtc != null)
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{\"startUtc\":\"" + startUtc + "\"}");
        return directory;
    }

    private static bool IsUnderDirectory(string path, string root)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
