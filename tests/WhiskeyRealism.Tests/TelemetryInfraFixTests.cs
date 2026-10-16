using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

// WS P (telemetry pipeline, logging infra, hitch probe, diagnostics & guard patches)
// regression tests. See docs/superpowers/plans/2026-10-14-full-review-remediation.md.
internal static class TelemetryInfraFixTests
{
    public static void Run()
    {
        NodeIdMatchesGameObjectAndComponentIds();
        NodeIdMatchesNegativeIds();
        NodeIdMatchesRejectsMalformedIds();
        NodeIdMatchesDoesNotAllocate();
        OnceLogFiredKeySkipsMessageWork();
        OnceLogBoundsFiredKeys();
        OnceLogWarningsAreNeverSuppressedByCap();
        OnceLogResetTacticalClearsOnlyTacticalKeys();
        QueueProtectedRowDoesNotEvictDetailRows();
        QueuePerformanceDoesNotEvictDecisionOrState();
        QueueEvictsTraceBeforePerformance();
        QueueShedsTraceThenInfoNeverWarning();
        QueueDrainPreservesFifoAcrossCategories();
        QueueCountersStayExactUnderChurn();
        QueueEnqueueIsConstantTimeWhenFull();
        RouterSeriousLineMatchesWholeTokens();
        RouterBlankDecisionSignatureIsFlaggedNotDropped();
        RouterEmitsDecisionWithBlankSignature();
        PerfSubThresholdScopesDoNotAllocate();
        PerfSubThresholdScopesAggregateIntoOneRowPerScope();
        PerfSlowRowsAreBoundedPerWindow();
        PerfWarningsOffFoldsSlowCallsIntoAggregate();
        PerfAggregateWindowFlushesAutomatically();
        PerfPerCallScopeNameSharesOneAggregate();
        ProbeStateMatchesScopeNamesByValue();
        WriterDoesNotRewriteManifestPerBatch();
        WriterRewritesManifestOnTimer();
        WriterKeepsOneOpenStreamPerFileAndClosesOnStop();
        WriterRetriesTransientFailureWithoutDisablingSink();
        WriterDisablesSinkAfterConsecutiveFailures();
        WriterShutdownDrainIsBounded();
        ShutdownWritesFinalManifestAndSummaryOnce();
        ShutdownRewritesManifestOnlyAfterLateFailure();
        SummaryKeepsBoundedSlowestScopes();
        SummaryBoundsInputBytesAndReadsFailuresFirst();
        ProbeStateDetectsHitchWithGcDeltasAndTopScopes();
        ProbeStateTracksSessionMaxAndSevereCount();
        ProbeStateBoundsRecordedScopes();
        ProbeTickDoesNotAllocateOnNormalFrames();
        ProbeTickEmitsProtectedHitchRowWithTopLevelScopes();
        SummaryReportsFrameHitches();
        ProbeRecordsTopLevelScopesSuppressedByProfile();
        PerfShutdownFlushesEveryThreadsAggregates();
        PerfProbeTickFlushesDueAggregateWindows();
        PerfTickBetweenThreadExitAndShutdownKeepsDeadThreadCounts();
        PerfShutdownKeepsWriterFinalDrainScopes();
        PerfAggregateWindowDefaultsToFiveSeconds();
        WriterRetryAfterTornWriteDoesNotDuplicateRows();
        WriterCountsRowsOnlyAfterTheirWriteSucceeds();
        WriterShutdownTimeoutFailureRowNeverOpensSecondHandle();
        WriterClosesOldRotationHandleOpenedInRotatingBatch();
        WriterUnknownLengthAfterFaultReopensInsteadOfDuplicating();
        WriterUntruncatableTornRowIsIsolatedOnItsOwnLine();
        WriterFailureRowsUseTornWriteHandling();
        RegimentDiagnosticsWatchTokensParse();
        RegimentDiagnosticsRescanOnlyWhenRosterOrWatchListChanges();
        RegimentDiagnosticsLineCapWarnsOncePerBattle();
        FaultSiteDetectsModFramesInStack();
        FaultSiteAcceptsOnlyListedExceptionTypes();
        FaultSiteDiarySupplyCycleMatchesVanillaNullHandling();
        ReflectionGetFieldReadsAndWarnsOncePerMissingField();
    }

    private static void LogPass(string message)
    {
        Console.WriteLine("PASS " + message);
    }

    // ---------------------------------------------------------------- P-nodeid

    private static void NodeIdMatchesGameObjectAndComponentIds()
    {
        TestHarness.AssertTrue(TacticalPatchIds.NodeIdMatches("node-1234", 1234, 99), "matches gameobject id");
        TestHarness.AssertTrue(TacticalPatchIds.NodeIdMatches("node-99", 1234, 99), "matches component id");
        TestHarness.AssertFalse(TacticalPatchIds.NodeIdMatches("node-12345", 1234, 99), "longer id does not match");
        TestHarness.AssertFalse(TacticalPatchIds.NodeIdMatches("node-123", 1234, 99), "prefix id does not match");
        TestHarness.AssertFalse(TacticalPatchIds.NodeIdMatches("node-0", 0, 0), "zero ids never match");
        TestHarness.AssertFalse(TacticalPatchIds.NodeIdMatches("node-1234", 0, 0), "zero ids are ignored");
        TestHarness.AssertTrue(TacticalPatchIds.NodeIdMatches("node-2147483647", int.MaxValue, 0), "int max");
        LogPass("node id matches gameobject and component ids");
    }

    private static void NodeIdMatchesNegativeIds()
    {
        TestHarness.AssertTrue(TacticalPatchIds.NodeIdMatches("node--5120", -5120, 0), "negative gameobject id");
        TestHarness.AssertTrue(TacticalPatchIds.NodeIdMatches("node--7", 3, -7), "negative component id");
        TestHarness.AssertFalse(TacticalPatchIds.NodeIdMatches("node-5120", -5120, 0), "sign matters");
        TestHarness.AssertFalse(TacticalPatchIds.NodeIdMatches("node--5120", 5120, 0), "sign matters reversed");
        TestHarness.AssertTrue(TacticalPatchIds.NodeIdMatches("node--2147483648", int.MinValue, 0), "int min");
        LogPass("node id matches negative ids");
    }

    private static void NodeIdMatchesRejectsMalformedIds()
    {
        string[] malformed = { null, "", "node-", "node--", "node-12a", "Node-12", "node-+12", "x-12", "node- 12", "node-99999999999", "node--99999999999", "12" };
        for (int i = 0; i < malformed.Length; i++)
            TestHarness.AssertFalse(TacticalPatchIds.NodeIdMatches(malformed[i], 12, 12), "malformed rejected: " + (malformed[i] ?? "<null>"));
        TestHarness.AssertFalse(TacticalPatchIds.NodeIdMatches("node-012", 12, 0), "leading zero is not the canonical id string");
        LogPass("node id rejects malformed ids");
    }

    private static void NodeIdMatchesDoesNotAllocate()
    {
        string a = "node-123456";
        string b = "node--98765";
        TacticalPatchIds.NodeIdMatches(a, 123456, 7);
        long before = TestFixtures.BeginAllocationWindow();
        int hits = 0;
        for (int i = 0; i < 10000; i++)
        {
            if (TacticalPatchIds.NodeIdMatches(a, 123456, 7)) hits++;
            if (TacticalPatchIds.NodeIdMatches(b, 11, -98765)) hits++;
            if (TacticalPatchIds.NodeIdMatches(a, 11, 12)) hits++;
        }
        long allocated = TestFixtures.AllocatedSince(before);
        TestHarness.AssertEqual(20000, hits, "hit count");
        TestHarness.AssertEqual(0L, allocated, "NodeIdMatches allocated bytes");
        LogPass("node id matching does not allocate");
    }

    // ---------------------------------------------------------------- OnceLog

    private static void OnceLogFiredKeySkipsMessageWork()
    {
        OnceLog.Reset();
        const string key = "p-test:fired";
        const string message = "[PTest] fired once";
        OnceLog.Info(key, message);
        OnceLog.Warning(key, message);
        long before = TestFixtures.BeginAllocationWindow();
        for (int i = 0; i < 1000; i++)
        {
            OnceLog.Info(key, message);
            OnceLog.Warning(key, message);
        }
        long allocated = TestFixtures.AllocatedSince(before);
        TestHarness.AssertEqual(0L, allocated, "fired OnceLog key allocated bytes");
        OnceLog.Reset();
        LogPass("oncelog fired key skips message work");
    }

    private static void OnceLogBoundsFiredKeys()
    {
        OnceLog.Reset();
        int warningsBefore = WhiskeyRealism.Plugin.Log.WarningCount;
        int infoBefore = WhiskeyRealism.Plugin.Log.InfoCount;
        for (int i = 0; i < OnceLog.MaxKeys; i++)
            OnceLog.Info("p-test:bound:" + i.ToString(CultureInfo.InvariantCulture), "[PTest] bound");
        TestHarness.AssertEqual(infoBefore + OnceLog.MaxKeys, WhiskeyRealism.Plugin.Log.InfoCount, "info logged under cap");
        TestHarness.AssertEqual(OnceLog.MaxKeys, OnceLog.FiredCount, "fired set at cap");
        OnceLog.Info("p-test:bound:over-info", "[PTest] over cap");
        TestHarness.AssertEqual(infoBefore + OnceLog.MaxKeys, WhiskeyRealism.Plugin.Log.InfoCount, "info over cap suppressed");
        TestHarness.AssertEqual(warningsBefore + 1, WhiskeyRealism.Plugin.Log.WarningCount, "cap reached warns exactly once");
        TestHarness.AssertTrue(OnceLog.SuppressedOverCapCount >= 1, "over-cap suppressions counted");
        LogPass("oncelog bounds fired keys");
    }

    private static void OnceLogWarningsAreNeverSuppressedByCap()
    {
        // Continues from the full set left by OnceLogBoundsFiredKeys.
        TestHarness.AssertEqual(OnceLog.MaxKeys, OnceLog.FiredCount, "precondition: info cap reached");
        int warningsBefore = WhiskeyRealism.Plugin.Log.WarningCount;
        OnceLog.Warning("p-test:bound:warning", "[PTest] warning past the info cap");
        OnceLog.Warning("p-test:bound:warning", "[PTest] warning past the info cap");
        TestHarness.AssertEqual(warningsBefore + 1, WhiskeyRealism.Plugin.Log.WarningCount, "warning logs once even when the info cap is full");
        OnceLog.Reset();
        TestHarness.AssertEqual(0, OnceLog.FiredCount, "reset clears fired set");
        LogPass("oncelog warnings are never suppressed by cap");
    }

    private static void OnceLogResetTacticalClearsOnlyTacticalKeys()
    {
        OnceLog.Reset();
        int infoBefore = WhiskeyRealism.Plugin.Log.InfoCount;
        OnceLog.Info("tactical-p-test:battle", "[PTest] tactical first fire");
        OnceLog.Info("campaign-p-test:day", "[PTest] campaign first fire");
        OnceLog.ResetTactical();
        OnceLog.Info("tactical-p-test:battle", "[PTest] tactical first fire next battle");
        OnceLog.Info("campaign-p-test:day", "[PTest] campaign stays fired");
        TestHarness.AssertEqual(infoBefore + 3, WhiskeyRealism.Plugin.Log.InfoCount, "tactical key re-fires after ResetTactical; campaign key does not");
        OnceLog.Reset();
        LogPass("oncelog reset tactical clears only tactical keys");
    }

    // ---------------------------------------------------------------- TEL-queue / TEL-prio

    private static TelemetryEvent Ev(TelemetryCategory category, string name)
    {
        return TelemetryEvent.Create("s", TelemetryProfile.FullTuning, TelemetryLayer.Tactical, category, name, TelemetrySeverity.Info);
    }

    private static void QueueProtectedRowDoesNotEvictDetailRows()
    {
        var queue = new TelemetryQueue(capacity: 2);
        queue.Enqueue(Ev(TelemetryCategory.Trace, "trace-a"));
        queue.Enqueue(Ev(TelemetryCategory.State, "state-a"));
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.Health, "health-a")), "health enqueued");
        TestHarness.AssertEqual(0L, queue.DroppedCount, "protected row uses its own reserve; no detail eviction");
        var drained = queue.Drain(10);
        TestHarness.AssertEqual(3, drained.Count, "all three rows retained");
        TestHarness.AssertEqual("trace-a", drained[0].EventName, "trace retained");
        LogPass("queue protected row does not evict detail rows");
    }

    private static TelemetryEvent Ev(TelemetryCategory category, string name, TelemetrySeverity severity)
    {
        return TelemetryEvent.Create("s", TelemetryProfile.FullTuning, TelemetryLayer.Tactical, category, name, severity);
    }

    // B6 shedding order (user direction 2026-10-15): Trace first, then Info rows, never
    // Performance or Warning rows.
    private static void QueuePerformanceDoesNotEvictDecisionOrState()
    {
        var queue = new TelemetryQueue(capacity: 2);
        queue.Enqueue(Ev(TelemetryCategory.Decision, "decision-a"));
        queue.Enqueue(Ev(TelemetryCategory.State, "state-a"));
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.Performance, "perf-a")), "performance evicts an info state row");
        TestHarness.AssertEqual(1L, queue.DroppedCountFor(TelemetryCategory.State), "state info row shed");
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.Gate, "gate-a")), "gate info evicts decision info");
        TestHarness.AssertEqual(TelemetryQueueResult.Dropped, queue.Enqueue(Ev(TelemetryCategory.Gate, "gate-b")), "gate info cannot evict performance");
        var drained = queue.Drain(10);
        TestHarness.AssertEqual(2, drained.Count, "count");
        TestHarness.AssertEqual("perf-a", drained[0].EventName, "performance kept");
        TestHarness.AssertEqual("gate-a", drained[1].EventName, "gate kept");
        LogPass("queue performance does not evict decision or state");
    }

    private static void QueueEvictsTraceBeforePerformance()
    {
        var queue = new TelemetryQueue(capacity: 2);
        queue.Enqueue(Ev(TelemetryCategory.Performance, "perf-a"));
        queue.Enqueue(Ev(TelemetryCategory.Trace, "trace-a"));
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.Performance, "perf-b")), "performance evicts trace");
        TestHarness.AssertEqual(TelemetryQueueResult.Dropped, queue.Enqueue(Ev(TelemetryCategory.Gate, "gate-a")), "gate info never evicts performance");
        TestHarness.AssertEqual(TelemetryQueueResult.Dropped, queue.Enqueue(Ev(TelemetryCategory.Gate, "gate-w", TelemetrySeverity.Warning)), "a warning row does not evict performance either");
        var drained = queue.Drain(10);
        TestHarness.AssertEqual(2, drained.Count, "count");
        TestHarness.AssertEqual("perf-a", drained[0].EventName, "performance kept");
        TestHarness.AssertEqual("perf-b", drained[1].EventName, "performance kept");
        TestHarness.AssertEqual(1L, queue.DroppedCountFor(TelemetryCategory.Trace), "trace evicted");
        TestHarness.AssertEqual(0L, queue.DroppedCountFor(TelemetryCategory.Performance), "performance never evicted");
        LogPass("queue evicts trace before performance");
    }

    private static void QueueShedsTraceThenInfoNeverWarning()
    {
        var queue = new TelemetryQueue(capacity: 3);
        queue.Enqueue(Ev(TelemetryCategory.State, "state-warning", TelemetrySeverity.Warning));
        queue.Enqueue(Ev(TelemetryCategory.Decision, "decision-info"));
        queue.Enqueue(Ev(TelemetryCategory.Trace, "trace-info"));
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.State, "state-info")), "state info evicts trace");
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.Trace, "trace-warning", TelemetrySeverity.Warning)), "a warning trace row evicts an info row");
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.Write, "write-info")), "write info evicts decision info");
        TestHarness.AssertEqual(TelemetryQueueResult.Dropped, queue.Enqueue(Ev(TelemetryCategory.Write, "write-info-2")), "info never evicts warning rows");
        var drained = queue.Drain(10);
        TestHarness.AssertEqual(3, drained.Count, "count");
        TestHarness.AssertEqual("state-warning", drained[0].EventName, "warning state kept");
        TestHarness.AssertEqual("trace-warning", drained[1].EventName, "warning trace kept");
        TestHarness.AssertEqual("write-info", drained[2].EventName, "highest info kept");
        LogPass("queue sheds trace then info, never warning");
    }

    private static void QueueDrainPreservesFifoAcrossCategories()
    {
        var queue = new TelemetryQueue(capacity: 16);
        var order = new[]
        {
            TelemetryCategory.State, TelemetryCategory.Failure, TelemetryCategory.Decision, TelemetryCategory.Performance,
            TelemetryCategory.Health, TelemetryCategory.Trace, TelemetryCategory.Gate, TelemetryCategory.Write
        };
        for (int i = 0; i < order.Length; i++)
            queue.Enqueue(Ev(order[i], "row-" + i.ToString(CultureInfo.InvariantCulture)));
        var first = queue.Drain(3);
        var rest = queue.Drain(100);
        TestHarness.AssertEqual(3, first.Count, "partial drain count");
        TestHarness.AssertEqual(5, rest.Count, "rest drain count");
        for (int i = 0; i < 3; i++)
            TestHarness.AssertEqual("row-" + i.ToString(CultureInfo.InvariantCulture), first[i].EventName, "fifo first " + i.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < 5; i++)
            TestHarness.AssertEqual("row-" + (i + 3).ToString(CultureInfo.InvariantCulture), rest[i].EventName, "fifo rest " + i.ToString(CultureInfo.InvariantCulture));
        TestHarness.AssertEqual(0, queue.Count, "drained empty");
        LogPass("queue drain preserves fifo across categories");
    }

    private static void QueueCountersStayExactUnderChurn()
    {
        var queue = new TelemetryQueue(capacity: 128);
        var rng = new Random(1234);
        var categories = (TelemetryCategory[])Enum.GetValues(typeof(TelemetryCategory));
        int expectedDetail = 0;
        int expectedProtected = 0;
        for (int round = 0; round < 50; round++)
        {
            for (int i = 0; i < 200; i++)
            {
                var category = categories[rng.Next(categories.Length)];
                var result = queue.Enqueue(Ev(category, "churn"));
                bool isProtected = category == TelemetryCategory.Health || category == TelemetryCategory.Failure;
                if (result == TelemetryQueueResult.Enqueued)
                {
                    if (isProtected) expectedProtected++;
                    else if (expectedDetail < 128) expectedDetail++;
                }
            }
            TestHarness.AssertEqual(expectedDetail + expectedProtected, queue.Count, "count after enqueue round " + round.ToString(CultureInfo.InvariantCulture));
            var drained = queue.Drain(rng.Next(1, 300));
            for (int i = 0; i < drained.Count; i++)
            {
                bool isProtected = drained[i].Category == TelemetryCategory.Health || drained[i].Category == TelemetryCategory.Failure;
                if (isProtected) expectedProtected--; else expectedDetail--;
            }
            TestHarness.AssertEqual(expectedDetail + expectedProtected, queue.Count, "count after drain round " + round.ToString(CultureInfo.InvariantCulture));
            TestHarness.AssertTrue(expectedDetail <= 128, "detail bounded");
            TestHarness.AssertTrue(expectedProtected <= queue.ProtectedReserveCapacity, "protected bounded");
        }
        LogPass("queue counters stay exact under churn");
    }

    private static void QueueEnqueueIsConstantTimeWhenFull()
    {
        var queue = new TelemetryQueue(capacity: TelemetryRuntimeConfig.MaxQueueCapacity);
        for (int i = 0; i < TelemetryRuntimeConfig.MaxQueueCapacity; i++)
            queue.Enqueue(Ev(TelemetryCategory.Decision, "fill"));
        var trace = Ev(TelemetryCategory.Trace, "trace");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 20000; i++)
            queue.Enqueue(trace);
        watch.Stop();
        TestHarness.AssertTrue(watch.ElapsedMilliseconds < 250, "20k enqueues into a full 64k queue took " + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
        TestHarness.AssertEqual(TelemetryRuntimeConfig.MaxQueueCapacity, queue.Count, "full queue count");
        TestHarness.AssertEqual(20000L, queue.DroppedCount, "drops counted");
        LogPass("queue enqueue is constant time when full");
    }

    // ---------------------------------------------------------------- Router

    private static string TempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "wr-p-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteQuietly(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private static TelemetryRuntime StartRuntime(string gameRoot, bool performanceWarnings = true, TelemetryProfile profile = TelemetryProfile.FullTuning)
    {
        var config = TelemetryRuntimeConfig.Create(
            gameRoot, "0.2.2-test", "abcdef1234567890", profile,
            maxTuningLogMb: 8, fileRotateMb: 4, retainedSessions: 2,
            emitHumanSummary: true, performanceWarnings: performanceWarnings,
            createIssueBundleOnShutdown: false, warningCallback: null);
        TelemetryRuntime runtime = TelemetryRuntime.Start(config);
        TelemetryRouter.AttachRuntime(runtime);
        return runtime;
    }

    private static string SessionDir(string gameRoot, TelemetryRuntime runtime)
    {
        return Path.Combine(TelemetrySession.TuningLogRoot(gameRoot), runtime.SessionId);
    }

    private static string ReadIfExists(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    private static void RouterSeriousLineMatchesWholeTokens()
    {
        TestHarness.AssertEqual(TelemetryCategory.Trace, TelemetryTagPolicy.Route("[TacticalPTest] errorBudget=0.3 failureRisk=0.1 terrain=ridge").Category, "embedded substrings are not serious");
        TestHarness.AssertEqual(TelemetryCategory.Trace, TelemetryTagPolicy.Route("[TacticalPTest] flank=exceptional").Category, "exceptional is not exception");
        TestHarness.AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[TacticalPTest] resolve failed for group").Category, "failed token");
        TestHarness.AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[TacticalPTest] error: bad state").Category, "error token");
        TestHarness.AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[TacticalPTest] caught NullReferenceException: x").Category, "exception type name");
        TestHarness.AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[TacticalPTest] missing-anchor AIBattle.Foo").Category, "missing-anchor phrase");
        TestHarness.AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[TacticalPTest] FATAL").Category, "fatal token case-insensitive");
        LogPass("router serious line matches whole tokens");
    }

    private static void RouterBlankDecisionSignatureIsFlaggedNotDropped()
    {
        var ev = TelemetryEvent.Create("s", TelemetryProfile.FullTuning, TelemetryLayer.Tactical, TelemetryCategory.Decision, "PTestDecision", TelemetrySeverity.Info)
            .WithDecision("Attack", "test", "  ");
        TestHarness.AssertEqual("Attack", ev.Decision, "decision kept");
        TestHarness.AssertEqual(TelemetryEvent.MissingInputSignature, ev.InputSignature, "missing signature marker");
        TestHarness.AssertTrue(ev.Fields.GetBool("inputSignatureMissing"), "missing signature flagged");
        LogPass("router blank decision signature is flagged not dropped");
    }

    private static void RouterEmitsDecisionWithBlankSignature()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            TestHarness.AssertEqual(TelemetryProfile.FullTuning, TelemetryRouter.CurrentProfile, "profile attached");
            bool emitted = TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "PTestBlankSig", TelemetrySeverity.Info,
                ev => ev.WithDecision("Hold", "test", null));
            string dir = SessionDir(gameRoot, runtime);
            TelemetryRouter.Shutdown("p-test");
            TestHarness.AssertTrue(emitted, "blank-signature decision still emitted");
            string tactical = ReadIfExists(Path.Combine(dir, "tactical.jsonl"));
            TestHarness.AssertTrue(tactical.Contains("\"event\":\"PTestBlankSig\""), "row written");
            TestHarness.AssertTrue(tactical.Contains("\"inputSignatureMissing\":true"), "row flagged");
            TestHarness.AssertEqual(TelemetryProfile.Off, TelemetryRouter.CurrentProfile, "profile off after shutdown");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("router emits decision with blank signature");
    }

    // ---------------------------------------------------------------- P-telemetry-rows

    private static string[] PerformanceLines(string dir)
    {
        string text = ReadIfExists(Path.Combine(dir, "performance.jsonl"));
        return text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static List<Newtonsoft.Json.Linq.JObject> RowsForScope(string dir, string scope, string eventName)
    {
        var rows = new List<Newtonsoft.Json.Linq.JObject>();
        foreach (string line in PerformanceLines(dir))
        {
            var row = Newtonsoft.Json.Linq.JObject.Parse(line);
            if ((string)row["event"] == eventName && (string)row["fields"]["scope"] == scope)
                rows.Add(row);
        }
        return rows;
    }

    private static void PerfSubThresholdScopesDoNotAllocate()
    {
        string gameRoot = TempDir();
        try
        {
            StartRuntime(gameRoot);
            for (int i = 0; i < 4; i++)
            {
                using (TelemetryPerf.Scope("p-test.alloc.outer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                using (TelemetryPerf.Scope("p-test.alloc.inner", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                {
                }
            }

            long before = TestFixtures.BeginAllocationWindow();
            for (int i = 0; i < 10000; i++)
            {
                using (TelemetryPerf.Scope("p-test.alloc.outer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                using (TelemetryPerf.Scope("p-test.alloc.inner", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                {
                }
            }
            long allocated = TestFixtures.AllocatedSince(before);
            TestHarness.AssertTrue(allocated < 1024, "20k sub-threshold scopes allocated " + allocated.ToString(CultureInfo.InvariantCulture) + " bytes");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("perf sub-threshold scopes do not allocate");
    }

    private static void PerfSubThresholdScopesAggregateIntoOneRowPerScope()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            string dir = SessionDir(gameRoot, runtime);
            for (int i = 0; i < 500; i++)
            {
                using (TelemetryPerf.Scope("p-test.agg", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                {
                }
            }
            TelemetryRouter.Shutdown("p-test");

            TestHarness.AssertEqual(0, RowsForScope(dir, "p-test.agg", "Performance").Count, "no per-call rows for sub-threshold scopes");
            var aggregates = RowsForScope(dir, "p-test.agg", "PerformanceAggregate");
            TestHarness.AssertEqual(1, aggregates.Count, "one aggregate row per scope per window");
            TestHarness.AssertEqual(500L, (long)aggregates[0]["fields"]["count"], "aggregate count");
            TestHarness.AssertEqual(0L, (long)aggregates[0]["fields"]["slowCount"], "aggregate slow count");
            TestHarness.AssertTrue(aggregates[0]["fields"]["totalMs"] != null && aggregates[0]["fields"]["maxMs"] != null, "aggregate timing fields");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("perf sub-threshold scopes aggregate into one row per scope");
    }

    private static void PerfSlowRowsAreBoundedPerWindow()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            string dir = SessionDir(gameRoot, runtime);
            for (int i = 0; i < 100; i++)
            {
                using (TelemetryPerf.Scope("p-test.slow", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.0))
                {
                }
            }
            TelemetryRouter.Shutdown("p-test");

            int slowRows = RowsForScope(dir, "p-test.slow", "Performance").Count;
            TestHarness.AssertTrue(slowRows >= 1 && slowRows <= TelemetryPerf.MaxSlowRowsPerScopeWindow, "slow rows bounded: " + slowRows.ToString(CultureInfo.InvariantCulture));
            var aggregates = RowsForScope(dir, "p-test.slow", "PerformanceAggregate");
            TestHarness.AssertEqual(1, aggregates.Count, "aggregate row");
            TestHarness.AssertEqual(100L, (long)aggregates[0]["fields"]["slowCount"], "aggregate counts every slow call");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("perf slow rows are bounded per window");
    }

    private static void PerfWarningsOffFoldsSlowCallsIntoAggregate()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryRuntime runtime = StartRuntime(gameRoot, performanceWarnings: false);
            string dir = SessionDir(gameRoot, runtime);
            for (int i = 0; i < 5; i++)
            {
                using (TelemetryPerf.Scope("p-test.nowarn", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.0))
                {
                }
            }
            TelemetryRouter.Shutdown("p-test");

            TestHarness.AssertEqual(0, RowsForScope(dir, "p-test.nowarn", "Performance").Count, "no individual slow rows when warnings off");
            var aggregates = RowsForScope(dir, "p-test.nowarn", "PerformanceAggregate");
            TestHarness.AssertEqual(1, aggregates.Count, "aggregate row");
            TestHarness.AssertEqual(5L, (long)aggregates[0]["fields"]["slowCount"], "slow calls counted in aggregate");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("perf warnings off folds slow calls into aggregate");
    }

    // F3 two-tier lookup: a scope name built per call (a new string instance each time)
    // must still land on one aggregate, and must not use up the per-thread name cap so
    // that later literal scopes are dropped.
    private static void PerfPerCallScopeNameSharesOneAggregate()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            string dir = SessionDir(gameRoot, runtime);
            const int calls = TelemetryPerf.MaxScopesPerThread + TelemetryPerf.MaxAliasesPerThread + 100;
            char[] text = "p-test.per-call-name".ToCharArray();
            for (int i = 0; i < calls; i++)
            {
                using (TelemetryPerf.Scope(new string(text), TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                {
                }
            }
            using (TelemetryPerf.Scope("p-test.after-per-call-name", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
            {
            }
            TelemetryRouter.Shutdown("p-test");

            var rows = RowsForScope(dir, "p-test.per-call-name", "PerformanceAggregate");
            TestHarness.AssertEqual(1, rows.Count, "one aggregate for a per-call name");
            TestHarness.AssertEqual((long)calls, (long)rows[0]["fields"]["count"], "every per-call scope counted once");
            TestHarness.AssertEqual(1, RowsForScope(dir, "p-test.after-per-call-name", "PerformanceAggregate").Count,
                "a later literal scope still gets its aggregate");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("perf per-call scope name shares one aggregate");
    }

    // The frame probe's per-frame table matches by reference first, then by value.
    private static void ProbeStateMatchesScopeNamesByValue()
    {
        var state = new FrameProbeState();
        state.RecordScope("p-test.probe.value", 100.0);
        state.RecordScope(new string("p-test.probe.value".ToCharArray()), 200.0);
        TelemetryFrameHitch hitch;
        TestHarness.AssertTrue(state.Observe(400.0, 0, 0, 0, out hitch), "hitch frame");
        TestHarness.AssertEqual("p-test.probe.value:300.0x2", hitch.TopScopes, "equal names share one slot");
        LogPass("probe state matches scope names by value");
    }

    private static void PerfAggregateWindowFlushesAutomatically()
    {
        string gameRoot = TempDir();
        long savedWindow = TelemetryPerf.AggregateWindowMs;
        try
        {
            TelemetryPerf.AggregateWindowMs = 20;
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            string dir = SessionDir(gameRoot, runtime);
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < 10; i++)
                {
                    using (TelemetryPerf.Scope("p-test.window", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                    {
                    }
                }
                System.Threading.Thread.Sleep(30);
            }
            using (TelemetryPerf.Scope("p-test.window", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
            {
            }
            TelemetryRouter.Shutdown("p-test");

            var aggregates = RowsForScope(dir, "p-test.window", "PerformanceAggregate");
            TestHarness.AssertTrue(aggregates.Count >= 3, "window elapsed flushes aggregates without explicit flush: " + aggregates.Count.ToString(CultureInfo.InvariantCulture));
            long total = 0;
            foreach (var row in aggregates)
                total += (long)row["fields"]["count"];
            TestHarness.AssertEqual(31L, total, "every call accounted across windows");
        }
        finally
        {
            TelemetryPerf.AggregateWindowMs = savedWindow;
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("perf aggregate window flushes automatically");
    }

    // ---------------------------------------------------------------- P-queue / TEL-sink / TEL-run

    private static TelemetryEvent Decision(string session, int i)
    {
        return TelemetryEvent.Create(session, TelemetryProfile.FullTuning, TelemetryLayer.Tactical, TelemetryCategory.Decision, "PTestWriterDecision", TelemetrySeverity.Info)
            .WithDecision("hold", "writer", "sig-" + i.ToString(CultureInfo.InvariantCulture));
    }

    private static int CountLines(string path)
    {
        if (!File.Exists(path)) return 0;
        int count = 0;
        foreach (string line in File.ReadAllLines(path))
            if (!string.IsNullOrWhiteSpace(line)) count++;
        return count;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            System.Threading.Thread.Sleep(5);
        }
        return condition();
    }

    // The writer's retry/manifest clock (TelemetryWriter.Clock), advanced only by
    // the test: retry and timer outcomes then do not depend on machine speed.
    private sealed class FakeWriterClock
    {
        private long _ticks = 1L;

        public long Now()
        {
            return System.Threading.Interlocked.Read(ref _ticks);
        }

        public void AdvanceMs(long ms)
        {
            System.Threading.Interlocked.Add(ref _ticks, ms * System.Diagnostics.Stopwatch.Frequency / 1000L);
        }
    }

    private static bool AdvanceAndSignal(FakeWriterClock clock, TelemetryWriter writer)
    {
        clock.AdvanceMs(5000);
        writer.Signal();
        return true;
    }

    private static void WriterDoesNotRewriteManifestPerBatch()
    {
        string dir = TempDir();
        try
        {
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            int manifestCalls = 0;
            for (int i = 0; i < 10; i++)
                queue.Enqueue(Decision("w", i));
            var writer = new TelemetryWriter(queue, budget, dir, delegate { System.Threading.Interlocked.Increment(ref manifestCalls); },
                "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 1);
            writer.Start();
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer stops");
            TestHarness.AssertEqual(10, CountLines(Path.Combine(dir, "tactical.jsonl")), "all rows written");
            TestHarness.AssertTrue(manifestCalls >= 1 && manifestCalls <= 3, "manifest written on new file + shutdown only, got " + manifestCalls.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            DeleteQuietly(dir);
        }
        LogPass("writer does not rewrite manifest per batch");
    }

    private static void WriterRewritesManifestOnTimer()
    {
        string dir = TempDir();
        int savedInterval = TelemetryWriter.ManifestIntervalMs;
        var savedClock = TelemetryWriter.Clock;
        try
        {
            var clock = new FakeWriterClock();
            TelemetryWriter.Clock = clock.Now;
            TelemetryWriter.ManifestIntervalMs = 40;
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            int manifestCalls = 0;
            var writer = new TelemetryWriter(queue, budget, dir, delegate { System.Threading.Interlocked.Increment(ref manifestCalls); },
                "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 16);
            writer.Start();

            queue.Enqueue(Decision("w", 0));
            writer.Signal();
            TestHarness.AssertTrue(WaitUntil(() => System.Threading.Volatile.Read(ref manifestCalls) >= 1, 5000), "new output file writes the manifest");

            for (int i = 1; i <= 5; i++)
            {
                int before = System.Threading.Volatile.Read(ref manifestCalls);
                queue.Enqueue(Decision("w", i));
                clock.AdvanceMs(41);
                writer.Signal();
                TestHarness.AssertTrue(WaitUntil(() => System.Threading.Volatile.Read(ref manifestCalls) > before, 5000), "interval elapsed: manifest rewritten, round " + i.ToString(CultureInfo.InvariantCulture));
            }

            queue.Enqueue(Decision("w", 6));
            writer.Signal();
            TestHarness.AssertTrue(WaitUntil(() => CountLines(Path.Combine(dir, "tactical.jsonl")) == 7, 5000), "rows written");
            // Real time passes well beyond the 40 ms interval while the writer keeps
            // looping: with MaybeWriteManifest on the real clock this rewrites the
            // manifest; on TelemetryWriter.Clock it must not.
            int idleBefore = System.Threading.Volatile.Read(ref manifestCalls);
            for (int i = 0; i < 4; i++)
            {
                System.Threading.Thread.Sleep(60);
                writer.Signal();
            }
            TestHarness.AssertEqual(idleBefore, System.Threading.Volatile.Read(ref manifestCalls), "real time alone never rewrites the manifest");
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer stops");
            TestHarness.AssertEqual(7, manifestCalls, "new file + five elapsed intervals + shutdown; a batch inside the interval writes none");
        }
        finally
        {
            TelemetryWriter.ManifestIntervalMs = savedInterval;
            TelemetryWriter.Clock = savedClock;
            DeleteQuietly(dir);
        }
        LogPass("writer rewrites manifest on timer");
    }

    private static void WriterKeepsOneOpenStreamPerFileAndClosesOnStop()
    {
        string dir = TempDir();
        try
        {
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            var writer = new TelemetryWriter(queue, budget, dir, delegate { }, "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 4);
            writer.Start();
            for (int i = 0; i < 12; i++)
                queue.Enqueue(Decision("w", i));
            queue.Enqueue(TelemetryEvent.Create("w", TelemetryProfile.FullTuning, TelemetryLayer.System, TelemetryCategory.Health, "PTestHealth", TelemetrySeverity.Info));
            writer.Signal();
            // Both files must be written (their streams open) before counting streams:
            // with flushRows 4 the health row lands in a later batch than the queue
            // drain the first condition sees.
            TestHarness.AssertTrue(WaitUntil(() => queue.Count == 0 && CountLines(Path.Combine(dir, "tactical.jsonl")) == 12
                && CountLines(Path.Combine(dir, "health.jsonl")) == 1, 5000), "rows flushed to disk while stream stays open");
            TestHarness.AssertEqual(2, writer.OpenStreamCount, "one open stream per output file");
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer stops");
            TestHarness.AssertEqual(0, writer.OpenStreamCount, "streams closed on stop");
            TestHarness.AssertEqual(1, CountLines(Path.Combine(dir, "health.jsonl")), "health row written");
        }
        finally
        {
            DeleteQuietly(dir);
        }
        LogPass("writer keeps one open stream per file and closes on stop");
    }

    private static void WriterRetriesTransientFailureWithoutDisablingSink()
    {
        string dir = TempDir();
        int savedBackoff = TelemetryWriter.RetryBaseMilliseconds;
        var savedClock = TelemetryWriter.Clock;
        try
        {
            var clock = new FakeWriterClock();
            TelemetryWriter.Clock = clock.Now;
            TelemetryWriter.RetryBaseMilliseconds = 20;
            Directory.Delete(dir, true);
            var warnings = new List<string>();
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            var writer = new TelemetryWriter(queue, budget, dir, delegate { }, "w", TelemetryProfile.FullTuning, warnings.Add, flushMilliseconds: 50, flushRows: 8);
            writer.Start();
            for (int i = 0; i < 3; i++)
                queue.Enqueue(Decision("w", i));
            writer.Signal();
            TestHarness.AssertTrue(WaitUntil(() => writer.SinkFailureCount >= 1, 5000), "transient failure observed");
            TestHarness.AssertEqual(1L, writer.SinkFailureCount, "no retry before the backoff elapses");
            Directory.CreateDirectory(dir);
            // The failure is counted before the writer sets its retry deadline
            // (OnWriteFailure), so advance on every poll, past whatever deadline it set.
            TestHarness.AssertTrue(WaitUntil(() => AdvanceAndSignal(clock, writer) && CountLines(Path.Combine(dir, "tactical.jsonl")) == 3, 5000), "rows retried and written after the sink recovers");
            TestHarness.AssertFalse(writer.FileSinkDisabled, "transient failure does not disable the sink");
            queue.Enqueue(Decision("w", 99));
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer stops");
            TestHarness.AssertEqual(4, CountLines(Path.Combine(dir, "tactical.jsonl")), "later rows still written");
            TestHarness.AssertTrue(warnings.Count >= 1, "failure warned");
        }
        finally
        {
            TelemetryWriter.RetryBaseMilliseconds = savedBackoff;
            TelemetryWriter.Clock = savedClock;
            DeleteQuietly(dir);
        }
        LogPass("writer retries transient failure without disabling sink");
    }

    private static void WriterDisablesSinkAfterConsecutiveFailures()
    {
        string dir = TempDir();
        int savedBackoff = TelemetryWriter.RetryBaseMilliseconds;
        var savedClock = TelemetryWriter.Clock;
        try
        {
            var clock = new FakeWriterClock();
            TelemetryWriter.Clock = clock.Now;
            TelemetryWriter.RetryBaseMilliseconds = 5;
            Directory.Delete(dir, true);
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            var writer = new TelemetryWriter(queue, budget, dir, delegate { }, "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 8);
            writer.Start();
            queue.Enqueue(Decision("w", 0));
            writer.Signal();
            TestHarness.AssertTrue(WaitUntil(() => AdvanceAndSignal(clock, writer) && writer.FileSinkDisabled, 10000), "sink disabled after repeated failures");
            TestHarness.AssertTrue(writer.SinkFailureCount >= TelemetryWriter.MaxConsecutiveWriteFailures, "each failed attempt counted");
            writer.StopAndFlush(2000);
            TestHarness.AssertFalse(Directory.Exists(dir), "writer never recreates the session directory");
        }
        finally
        {
            TelemetryWriter.RetryBaseMilliseconds = savedBackoff;
            TelemetryWriter.Clock = savedClock;
            DeleteQuietly(dir);
        }
        LogPass("writer disables sink after consecutive failures");
    }

    private static void WriterShutdownDrainIsBounded()
    {
        string dir = TempDir();
        bool stopProducer = false;
        System.Threading.Thread producer = null;
        try
        {
            var queue = new TelemetryQueue(capacity: 4096);
            var budget = new TelemetryBudget(totalBytes: 64L << 20, rotateBytes: 64L << 20);
            var writer = new TelemetryWriter(queue, budget, dir, delegate { }, "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 64);
            writer.Start();
            producer = new System.Threading.Thread(() =>
            {
                int i = 0;
                while (!System.Threading.Volatile.Read(ref stopProducer))
                {
                    queue.Enqueue(Decision("w", i++));
                    if ((i & 255) == 0) System.Threading.Thread.Yield();
                }
            });
            producer.IsBackground = true;
            producer.Start();
            System.Threading.Thread.Sleep(50);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            writer.StopAndFlush(6000);
            watch.Stop();
            TestHarness.AssertTrue(watch.ElapsedMilliseconds < 4000, "shutdown drain bounded while a producer keeps enqueuing, took " + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
        }
        finally
        {
            System.Threading.Volatile.Write(ref stopProducer, true);
            if (producer != null) producer.Join(2000);
            DeleteQuietly(dir);
        }
        LogPass("writer shutdown drain is bounded");
    }

    // ---------------------------------------------------------------- P-shutdown

    private static void ShutdownWritesFinalManifestAndSummaryOnce()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            TestHarness.AssertTrue(runtime.TryEmit(Decision(runtime.SessionId, 1)), "row accepted");
            TelemetryRouter.Shutdown("p-test");
            TestHarness.AssertEqual(1, runtime.FinalManifestWriteCount, "final manifest written once");
            TestHarness.AssertEqual(1, runtime.SummaryWriteCount, "summary written once");
            TestHarness.AssertTrue(File.Exists(Path.Combine(SessionDir(gameRoot, runtime), "summary.md")), "summary exists");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("shutdown writes final manifest and summary once");
    }

    private static void ShutdownRewritesManifestOnlyAfterLateFailure()
    {
        string gameRoot = TempDir();
        try
        {
            var config = TelemetryRuntimeConfig.Create(
                gameRoot, "0.2.2-test", "abcdef1234567890", TelemetryProfile.FullTuning,
                maxTuningLogMb: 8, fileRotateMb: 4, retainedSessions: 2,
                emitHumanSummary: true, performanceWarnings: true,
                createIssueBundleOnShutdown: true, warningCallback: null);
            TelemetryRuntime runtime = TelemetryRuntime.Start(config);
            string dir = SessionDir(gameRoot, runtime);
            Directory.CreateDirectory(Path.Combine(dir, "issue-bundle.json"));
            runtime.Shutdown("p-test");
            TestHarness.AssertEqual(2, runtime.FinalManifestWriteCount, "manifest rewritten once to record the late failure");
            TestHarness.AssertEqual(1, runtime.SummaryWriteCount, "summary still written once");
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")));
            TestHarness.AssertTrue((long)manifest["droppedCounters"]["sinkFailures"] >= 1L, "late failure recorded in final manifest");
            TestHarness.AssertTrue(File.ReadAllText(Path.Combine(dir, "summary.md")).Contains("TelemetrySinkFailure"), "summary includes the late failure");
        }
        finally
        {
            DeleteQuietly(gameRoot);
        }
        LogPass("shutdown rewrites manifest only after late failure");
    }

    private static string PerfRow(string scope, double durationMs)
    {
        var ev = TelemetryEvent.Create("sum", TelemetryProfile.FullTuning, TelemetryLayer.Tactical, TelemetryCategory.Performance, "Performance", TelemetrySeverity.Info)
            .WithDurationMs(durationMs)
            .WithField("scope", scope);
        return TelemetryJson.ToJsonLine(ev);
    }

    private static void SummaryKeepsBoundedSlowestScopes()
    {
        string dir = TempDir();
        try
        {
            var lines = new System.Text.StringBuilder();
            for (int i = 1; i <= 2000; i++)
                lines.Append(PerfRow("scope-" + (i % 50).ToString(CultureInfo.InvariantCulture), i * 0.5));
            File.WriteAllText(Path.Combine(dir, "performance.jsonl"), lines.ToString());
            TelemetrySummary summary = TelemetrySummary.FromDirectory(dir);
            var slowest = summary.SlowestScopes;
            TestHarness.AssertEqual(8, slowest.Count, "top 8 slowest");
            TestHarness.AssertEqual(1000.0, slowest[0].DurationMs, "slowest first");
            TestHarness.AssertEqual(996.5, slowest[7].DurationMs, "eighth slowest");
            TestHarness.AssertTrue(summary.RetainedSlowScopeCount <= 8, "slow scopes retained incrementally, not all rows");
        }
        finally
        {
            DeleteQuietly(dir);
        }
        LogPass("summary keeps bounded slowest scopes");
    }

    private static void SummaryBoundsInputBytesAndReadsFailuresFirst()
    {
        string dir = TempDir();
        long saved = TelemetrySummary.MaxInputBytes;
        try
        {
            var perf = new System.Text.StringBuilder();
            for (int i = 0; i < 2000; i++)
                perf.Append(PerfRow("bulk", 1.0));
            File.WriteAllText(Path.Combine(dir, "performance.jsonl"), perf.ToString());
            File.WriteAllText(Path.Combine(dir, "campaign.jsonl"), perf.ToString());
            var failure = TelemetryEvent.Create("sum", TelemetryProfile.FullTuning, TelemetryLayer.System, TelemetryCategory.Failure, "PTestFailure", TelemetrySeverity.Warning)
                .WithField("reason", "boom");
            File.WriteAllText(Path.Combine(dir, "failures.jsonl"), TelemetryJson.ToJsonLine(failure));

            TelemetrySummary.MaxInputBytes = 64 * 1024;
            TelemetrySummary summary = TelemetrySummary.FromDirectory(dir);
            string markdown = summary.ToMarkdown();
            TestHarness.AssertTrue(markdown.Contains("PTestFailure=1"), "failures read first even when bulk files exceed the bound");
            TestHarness.AssertTrue(summary.InputTruncated, "summary marks truncated input");
            TestHarness.AssertTrue(markdown.Contains("inputTruncated: true"), "markdown reports truncation");
            long rowsRead = 0;
            foreach (var item in summary.CountByLayer)
                rowsRead += item.Count;
            TestHarness.AssertTrue(rowsRead < 4001, "row reading stopped at the byte bound");
        }
        finally
        {
            TelemetrySummary.MaxInputBytes = saved;
            DeleteQuietly(dir);
        }
        LogPass("summary bounds input bytes and reads failures first");
    }

    // ---------------------------------------------------------------- P-probe

    private static void ProbeStateDetectsHitchWithGcDeltasAndTopScopes()
    {
        var state = new FrameProbeState();
        TelemetryFrameHitch hitch;
        TestHarness.AssertFalse(state.Observe(16.0, 10, 2, 1, out hitch), "normal first frame");
        state.ResetFrameScopes();
        state.RecordScope("a", 1.0);
        state.RecordScope("b", 300.0);
        state.RecordScope("c", 5.0);
        state.RecordScope("b", 20.0);
        TestHarness.AssertTrue(state.Observe(400.0, 13, 2, 1, out hitch), "400 ms frame is a hitch");
        TestHarness.AssertEqual(400.0, hitch.FrameMs, "frame ms");
        TestHarness.AssertEqual(3L, hitch.Gen0Delta, "gen0 delta");
        TestHarness.AssertEqual(0L, hitch.Gen1Delta, "gen1 delta");
        TestHarness.AssertEqual(4, hitch.ScopeCount, "scope call count");
        TestHarness.AssertEqual(326.0, hitch.ScopeTotalMs, "scope total");
        TestHarness.AssertEqual("b:320.0x2;c:5.0x1;a:1.0x1", hitch.TopScopes, "top scopes aggregated by name, ordered by total ms");
        TestHarness.AssertFalse(hitch.Severe, "400 ms below severe threshold");
        state.ResetFrameScopes();
        TestHarness.AssertFalse(state.Observe(30.0, 13, 2, 1, out hitch), "back to normal");
        LogPass("probe state detects hitch with gc deltas and top scopes");
    }

    private static void ProbeStateTracksSessionMaxAndSevereCount()
    {
        var state = new FrameProbeState();
        TelemetryFrameHitch hitch;
        state.Observe(16.0, 0, 0, 0, out hitch);
        state.Observe(260.0, 0, 0, 0, out hitch);
        state.Observe(5200.0, 1, 0, 0, out hitch);
        TestHarness.AssertTrue(hitch.Severe, "5.2 s frame is severe");
        state.Observe(700.0, 1, 0, 0, out hitch);
        state.Observe(100.0, 1, 0, 0, out hitch);
        TestHarness.AssertEqual(3L, state.HitchCount, "frames over 250 ms");
        TestHarness.AssertEqual(2L, state.SevereHitchCount, "frames over 500 ms");
        TestHarness.AssertEqual(5200.0, state.MaxFrameMs, "max frame ms");
        TestHarness.AssertEqual(5L, state.FrameCount, "frames observed");
        LogPass("probe state tracks session max and severe count");
    }

    private static void ProbeStateBoundsRecordedScopes()
    {
        var state = new FrameProbeState();
        var names = new string[FrameProbeState.MaxScopesPerFrame + 40];
        for (int i = 0; i < names.Length; i++)
            names[i] = "s" + i.ToString(CultureInfo.InvariantCulture);

        // Per-regiment style churn: thousands of cheap calls across a few names must not
        // crowd out the one slow orchestrator tick recorded late in the frame.
        for (int i = 0; i < 5000; i++)
            state.RecordScope("tactical.patch.per-regiment", 0.01);
        for (int i = 0; i < names.Length; i++)
            state.RecordScope(names[i], 1.0);
        state.RecordScope("tactical.orchestrator-tick", 900.0);

        TelemetryFrameHitch hitch;
        TestHarness.AssertTrue(state.Observe(1200.0, 0, 0, 0, out hitch), "hitch");
        TestHarness.AssertEqual(5000 + names.Length + 1, hitch.ScopeCount, "every call counted");
        TestHarness.AssertTrue(hitch.ScopeNamesEvicted > 0, "distinct-name evictions counted");
        TestHarness.AssertTrue(hitch.TopScopes.StartsWith("tactical.orchestrator-tick:900.0x1;tactical.patch.per-regiment:50.0x5000"), "slow late scope kept and ranked first: " + hitch.TopScopes);
        TestHarness.AssertEqual(5000 * 0.01 + names.Length + 900.0, Math.Round(hitch.ScopeTotalMs, 6), "total includes evicted names");
        LogPass("probe state bounds recorded scopes");
    }

    private static void ProbeTickDoesNotAllocateOnNormalFrames()
    {
        TelemetryFrameProbe.ResetForTests();
        for (int i = 0; i < 4; i++)
            TelemetryFrameProbe.Tick(0.016f);
        long before = TestFixtures.BeginAllocationWindow();
        for (int i = 0; i < 5000; i++)
        {
            TelemetryFrameProbe.RecordTopLevelScope("p-test.frame", 0.1);
            TelemetryFrameProbe.Tick(0.016f);
        }
        long allocated = TestFixtures.AllocatedSince(before);
        TestHarness.AssertEqual(0L, allocated, "normal-frame probe ticks allocated bytes");
        TelemetryFrameProbe.ResetForTests();
        LogPass("probe tick does not allocate on normal frames");
    }

    private static void ProbeTickEmitsProtectedHitchRowWithTopLevelScopes()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryFrameProbe.ResetForTests();
            TelemetryRuntime runtime = StartRuntime(gameRoot, profile: TelemetryProfile.TacticalTuning);
            string dir = SessionDir(gameRoot, runtime);
            TelemetryFrameProbe.Tick(0.016f);
            using (TelemetryPerf.Scope("p-test.probe.outer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
            {
                using (TelemetryPerf.Scope("p-test.probe.inner", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                {
                }
            }
            TelemetryFrameProbe.Tick(0.9f);
            TelemetryFrameProbe.Tick(0.016f);
            TelemetryRouter.Shutdown("p-test");

            string health = ReadIfExists(Path.Combine(dir, "health.jsonl"));
            TestHarness.AssertEqual(1, CountOccurrences(health, "\"event\":\"FrameHitch\""), "one hitch row");
            string hitchLine = null;
            foreach (string line in health.Split('\n'))
                if (line.Contains("\"event\":\"FrameHitch\"")) hitchLine = line;
            var row = Newtonsoft.Json.Linq.JObject.Parse(hitchLine);
            TestHarness.AssertEqual("Health", (string)row["category"], "protected category");
            TestHarness.AssertTrue((double)row["durationMs"] >= 899.0, "frame duration recorded");
            TestHarness.AssertTrue(((string)row["fields"]["topScopes"]).StartsWith("p-test.probe.outer:"), "top-level scope recorded");
            TestHarness.AssertFalse(((string)row["fields"]["topScopes"]).Contains("p-test.probe.inner"), "nested scope not recorded as top-level");
            TestHarness.AssertTrue((bool)row["fields"]["severe"], "900 ms is severe");
            foreach (string field in new[] { "gen0Delta", "gen1Delta", "gen2Delta", "gcTotalMemoryBytes", "monoUsedBytes", "monoHeapBytes", "queueDepth", "scopeCount", "scopeTotalMs", "wallIntervalMs" })
                TestHarness.AssertTrue(row["fields"][field] != null, "hitch field " + field);

            var manifest = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")));
            TestHarness.AssertEqual(1L, (long)manifest["frameProbe"]["severeHitchCount"], "manifest severe hitch count");
            TestHarness.AssertTrue((double)manifest["frameProbe"]["maxFrameMs"] >= 899.0, "manifest max frame");
            string summary = File.ReadAllText(Path.Combine(dir, "summary.md"));
            TestHarness.AssertTrue(summary.Contains("- frameHitches: count=1 over500ms=1 maxMs=9"), "summary frame hitch line: " + summary);
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            TelemetryFrameProbe.ResetForTests();
            DeleteQuietly(gameRoot);
        }
        LogPass("probe tick emits protected hitch row with top-level scopes");
    }

    private static void SummaryReportsFrameHitches()
    {
        string dir = TempDir();
        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (double ms in new[] { 300.0, 5400.0, 800.0 })
            {
                var ev = TelemetryEvent.Create("sum", TelemetryProfile.FullTuning, TelemetryLayer.System, TelemetryCategory.Health, "FrameHitch", TelemetrySeverity.Warning)
                    .WithDurationMs(ms);
                sb.Append(TelemetryJson.ToJsonLine(ev));
            }
            File.WriteAllText(Path.Combine(dir, "health.jsonl"), sb.ToString());
            string markdown = TelemetrySummary.FromDirectory(dir).ToMarkdown();
            TestHarness.AssertTrue(markdown.Contains("- frameHitches: count=3 over500ms=2 maxMs=5400"), "summary from rows: " + markdown);
        }
        finally
        {
            DeleteQuietly(dir);
        }
        LogPass("summary reports frame hitches");
    }

    // ---------------------------------------------------------------- P-regdiag / map knowledge

    private static void RegimentDiagnosticsWatchTokensParse()
    {
        TestHarness.AssertEqual(0, TacticalRegimentDiagnosticsPatch.ParseWatchTokens(null).Length, "null list watches nothing");
        TestHarness.AssertEqual(0, TacticalRegimentDiagnosticsPatch.ParseWatchTokens("").Length, "empty list watches nothing");
        TestHarness.AssertEqual(0, TacticalRegimentDiagnosticsPatch.ParseWatchTokens(" , ,").Length, "blank tokens ignored");
        string[] tokens = TacticalRegimentDiagnosticsPatch.ParseWatchTokens(" Hampton ,Tennessee,,2nd_SC ");
        TestHarness.AssertEqual(3, tokens.Length, "three tokens");
        TestHarness.AssertEqual("Hampton", tokens[0], "trimmed token");
        TestHarness.AssertEqual("2nd_SC", tokens[2], "last token");
        TestHarness.AssertTrue(TacticalRegimentDiagnosticsPatch.MatchesAnyToken("28th Tennessee Infantry", tokens), "case-insensitive substring match");
        TestHarness.AssertFalse(TacticalRegimentDiagnosticsPatch.MatchesAnyToken("1st Virginia", tokens), "no match");
        TestHarness.AssertFalse(TacticalRegimentDiagnosticsPatch.MatchesAnyToken(null, tokens), "null name");
        LogPass("regiment diagnostics watch tokens parse");
    }

    private static void RegimentDiagnosticsRescanOnlyWhenRosterOrWatchListChanges()
    {
        TestHarness.AssertTrue(TacticalRegimentDiagnosticsPatch.ShouldRescan(false, 7, 7, 0, 120, "Hampton", "Hampton", false), "no cache yet");
        TestHarness.AssertFalse(TacticalRegimentDiagnosticsPatch.ShouldRescan(true, 7, 7, 120, 120, "Hampton", "Hampton", false), "same battle, roster and list reuses cache");
        TestHarness.AssertTrue(TacticalRegimentDiagnosticsPatch.ShouldRescan(true, 7, 7, 120, 121, "Hampton", "Hampton", false), "roster count change rescans");
        TestHarness.AssertTrue(TacticalRegimentDiagnosticsPatch.ShouldRescan(true, 7, 7, 120, 120, "Hampton", "Hampton,Tennessee", false), "watch list change rescans");
        TestHarness.AssertTrue(TacticalRegimentDiagnosticsPatch.ShouldRescan(true, 7, 9, 120, 120, "Hampton", "Hampton", false), "new battle (BattleUnits identity) rescans without OnBattleEnd");
        TestHarness.AssertTrue(TacticalRegimentDiagnosticsPatch.ShouldRescan(true, 7, 7, 120, 120, "Hampton", "Hampton", true), "a destroyed cached unit rescans");
        TestHarness.AssertTrue(TacticalRegimentDiagnosticsPatch.IsNewBattle(7, 9), "battle identity change");
        TestHarness.AssertFalse(TacticalRegimentDiagnosticsPatch.IsNewBattle(7, 7), "same battle");
        TestHarness.AssertFalse(TacticalRegimentDiagnosticsPatch.IsNewBattle(7, 0), "unknown identity keeps state");
        LogPass("regiment diagnostics rescan only when roster or watch list changes");
    }

    private static void RegimentDiagnosticsLineCapWarnsOncePerBattle()
    {
        int cap = TacticalRegimentDiagnosticsPatch.MaxLinesPerBattle;
        TestHarness.AssertFalse(TacticalRegimentDiagnosticsPatch.ShouldWarnLineCap(cap - 2, false), "below cap");
        TestHarness.AssertTrue(TacticalRegimentDiagnosticsPatch.ShouldWarnLineCap(cap - 1, false), "last line of the battle warns");
        TestHarness.AssertFalse(TacticalRegimentDiagnosticsPatch.ShouldWarnLineCap(cap - 1, true), "already warned this battle");
        TacticalRegimentDiagnosticsPatch.Reset();
        TestHarness.AssertFalse(TacticalRegimentDiagnosticsPatch.LineCapWarnedThisBattle, "battle reset re-arms the warning");
        LogPass("regiment diagnostics line cap warns once per battle");
    }

    // ---------------------------------------------------------------- H-finalizer

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception ex) { return ex; }
        return null;
    }

    private static void FaultSiteDetectsModFramesInStack()
    {
        var modAssembly = typeof(TelemetryInfraFixTests).Assembly;
        var jsonAssembly = typeof(Newtonsoft.Json.JsonConvert).Assembly;

        // Thrown inside Newtonsoft, caught here: the trace holds Newtonsoft frames plus this
        // test frame (the catch site), mirroring vanilla frames plus the patched DMD frame.
        Exception jsonFault = Capture(() => Newtonsoft.Json.Linq.JObject.Parse("{"));
        TestHarness.AssertTrue(jsonFault != null, "fault captured");
        TestHarness.AssertTrue(VanillaFaultSite.HasFrameFrom(jsonFault, jsonAssembly), "frames from the throwing assembly detected");
        TestHarness.AssertFalse(VanillaFaultSite.HasFrameFrom(jsonFault, typeof(System.Xml.XmlDocument).Assembly), "unrelated assembly not detected");

        Exception modFault = Capture(() => { string s = null; GC.KeepAlive(s.Length); });
        TestHarness.AssertTrue(VanillaFaultSite.HasFrameFrom(modFault, modAssembly), "mod-thrown NRE has a mod frame");
        TestHarness.AssertFalse(VanillaFaultSite.HasFrameFrom(new NullReferenceException("never thrown"), modAssembly), "no trace means no mod frame (keep old suppression)");
        TestHarness.AssertFalse(VanillaFaultSite.HasFrameFrom(null, modAssembly), "null exception");
        LogPass("fault site detects mod frames in stack");
    }

    private static void FaultSiteAcceptsOnlyListedExceptionTypes()
    {
        TestHarness.AssertTrue(VanillaFaultSite.IsNullOrMissingReference(new NullReferenceException()), "NRE");
        TestHarness.AssertTrue(VanillaFaultSite.IsNullOrMissingReference(new UnityEngine.MissingReferenceException()), "Unity missing reference");
        TestHarness.AssertFalse(VanillaFaultSite.IsNullOrMissingReference(new InvalidOperationException()), "other type");
        TestHarness.AssertTrue(VanillaFaultSite.IsFilterMapInitializationFault(new IndexOutOfRangeException()), "cursor out of range");
        TestHarness.AssertTrue(VanillaFaultSite.IsFilterMapInitializationFault(new ArgumentOutOfRangeException()), "list index out of range");
        TestHarness.AssertTrue(VanillaFaultSite.IsFilterMapInitializationFault(new NullReferenceException()), "null map item");
        TestHarness.AssertFalse(VanillaFaultSite.IsFilterMapInitializationFault(new InvalidOperationException()), "unrelated failure not forced complete");
        TestHarness.AssertFalse(VanillaFaultSite.IsFilterMapInitializationFault(new OutOfMemoryException()), "fatal failure not forced complete");
        TestHarness.AssertFalse(VanillaFaultSite.IsFilterMapInitializationFault(null), "null");
        LogPass("fault site accepts only listed exception types");
    }

    private static void FaultSiteDiarySupplyCycleMatchesVanillaNullHandling()
    {
        TestHarness.AssertTrue(VanillaFaultSite.DiarySupplyCycleSafe(10, null), "regiment-level group uses supplystate");
        TestHarness.AssertTrue(VanillaFaultSite.DiarySupplyCycleSafe(14, null), "null groupsupplystate is skipped by vanilla (Diary.UpdateEvents ~183037)");
        TestHarness.AssertFalse(VanillaFaultSite.DiarySupplyCycleSafe(14, new float[2]), "short array would throw at index 2");
        TestHarness.AssertTrue(VanillaFaultSite.DiarySupplyCycleSafe(14, new float[4]), "normal array");
        LogPass("fault site diary supply cycle matches vanilla null handling");
    }

    // ---------------------------------------------------------------- Util/Reflection

    private sealed class ReflectionProbe
    {
        private string combinedname = "Ulysses S. Grant";
        internal string Peek() { return combinedname; }
    }

    private static void ReflectionGetFieldReadsAndWarnsOncePerMissingField()
    {
        OnceLog.Reset();
        var probe = new ReflectionProbe();
        TestHarness.AssertEqual("Ulysses S. Grant", WhiskeyRealism.Util.Reflection.GetField<string>(probe, "combinedname"), "reads private field");
        TestHarness.AssertEqual(null, WhiskeyRealism.Util.Reflection.GetField<string>(null, "combinedname"), "null instance");
        int before = WhiskeyRealism.Plugin.Log.WarningCount;
        for (int i = 0; i < 50; i++)
            TestHarness.AssertEqual(null, WhiskeyRealism.Util.Reflection.GetField<string>(probe, "nosuchfield"), "missing field returns null");
        TestHarness.AssertEqual(before + 1, WhiskeyRealism.Plugin.Log.WarningCount, "missing field warns once, not per call");
        GC.KeepAlive(probe.Peek());
        OnceLog.Reset();
        LogPass("reflection get field reads and warns once per missing field");
    }

    // ---------------------------------------------------------------- M2

    private static void ProbeRecordsTopLevelScopesSuppressedByProfile()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryFrameProbe.ResetForTests();
            TelemetryRuntime runtime = StartRuntime(gameRoot, profile: TelemetryProfile.TacticalTuning);
            string dir = SessionDir(gameRoot, runtime);
            TelemetryFrameProbe.Tick(0.016f);
            for (int i = 0; i < 4; i++)
            {
                using (TelemetryPerf.Scope("campaign.p-test.suppressed", TelemetryLayer.Campaign, TelemetryCategory.Performance, 0.0))
                {
                }
            }
            TelemetryFrameProbe.Tick(0.016f);

            long before = TestFixtures.BeginAllocationWindow();
            for (int i = 0; i < 2000; i++)
            {
                using (TelemetryPerf.Scope("campaign.p-test.suppressed", TelemetryLayer.Campaign, TelemetryCategory.Performance, 0.0))
                {
                }
            }
            long allocated = TestFixtures.AllocatedSince(before);
            TestHarness.AssertEqual(0L, allocated, "record-only scopes allocate nothing");

            TelemetryFrameProbe.Tick(0.6f);
            TelemetryRouter.Shutdown("p-test");

            string health = ReadIfExists(Path.Combine(dir, "health.jsonl"));
            TestHarness.AssertTrue(health.Contains("campaign.p-test.suppressed:"), "profile-suppressed top-level scope still reaches the hitch row");
            TestHarness.AssertEqual(0, RowsForScope(dir, "campaign.p-test.suppressed", "Performance").Count, "no slow rows for suppressed layer");
            TestHarness.AssertEqual(0, RowsForScope(dir, "campaign.p-test.suppressed", "PerformanceAggregate").Count, "no aggregate rows for suppressed layer");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            TelemetryFrameProbe.ResetForTests();
            DeleteQuietly(gameRoot);
        }
        LogPass("probe records top-level scopes suppressed by profile");
    }

    // ---------------------------------------------------------------- L5

    private static void PerfShutdownFlushesEveryThreadsAggregates()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            string dir = SessionDir(gameRoot, runtime);
            var worker = new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 7; i++)
                {
                    using (TelemetryPerf.Scope("p-test.worker-thread", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                    {
                    }
                }
            });
            worker.Start();
            worker.Join();
            TelemetryRouter.Shutdown("p-test");

            var aggregates = RowsForScope(dir, "p-test.worker-thread", "PerformanceAggregate");
            TestHarness.AssertEqual(1, aggregates.Count, "other thread's final window flushed at shutdown");
            TestHarness.AssertEqual(7L, (long)aggregates[0]["fields"]["count"], "worker calls counted");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("perf shutdown flushes every thread's aggregates");
    }

    private static void PerfProbeTickFlushesDueAggregateWindows()
    {
        string gameRoot = TempDir();
        long savedWindow = TelemetryPerf.AggregateWindowMs;
        try
        {
            TelemetryPerf.AggregateWindowMs = 20;
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            string dir = SessionDir(gameRoot, runtime);
            var worker = new System.Threading.Thread(() =>
            {
                using (TelemetryPerf.Scope("p-test.tick-flush", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                {
                }
            });
            worker.Start();
            worker.Join();
            System.Threading.Thread.Sleep(40);
            TelemetryFrameProbe.Tick(0.016f);
            TestHarness.AssertTrue(
                WaitUntil(() => ReadIfExists(Path.Combine(dir, "performance.jsonl")).Contains("p-test.tick-flush"), 3000),
                "probe tick flushes an elapsed window with no further scope calls");
        }
        finally
        {
            TelemetryPerf.AggregateWindowMs = savedWindow;
            TelemetryRouter.Shutdown("cleanup");
            TelemetryFrameProbe.ResetForTests();
            DeleteQuietly(gameRoot);
        }
        LogPass("perf probe tick flushes due aggregate windows");
    }

    private static void PerfTickBetweenThreadExitAndShutdownKeepsDeadThreadCounts()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            string dir = SessionDir(gameRoot, runtime);
            var worker = new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    using (TelemetryPerf.Scope("p-test.dead-thread", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                    {
                    }
                }
            });
            worker.Start();
            worker.Join();
            TelemetryFrameProbe.Tick(0.016f); // window not due; owner is dead
            TelemetryRouter.Shutdown("p-test");

            var aggregates = RowsForScope(dir, "p-test.dead-thread", "PerformanceAggregate");
            TestHarness.AssertEqual(1, aggregates.Count, "dead thread's partial window flushed before its accumulator is dropped");
            TestHarness.AssertEqual(3L, (long)aggregates[0]["fields"]["count"], "dead thread calls counted");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            TelemetryFrameProbe.ResetForTests();
            DeleteQuietly(gameRoot);
        }
        LogPass("perf tick between thread exit and shutdown keeps dead thread counts");
    }

    private static void PerfShutdownKeepsWriterFinalDrainScopes()
    {
        string gameRoot = TempDir();
        try
        {
            TelemetryRuntime runtime = StartRuntime(gameRoot);
            string dir = SessionDir(gameRoot, runtime);
            // The only non-performance row is the TelemetryShutdown health row, so the writer's
            // telemetry.flush scope runs only during the shutdown drain.
            TelemetryRouter.Shutdown("p-test");
            TestHarness.AssertTrue(RowsForScope(dir, "telemetry.flush", "PerformanceAggregate").Count >= 1, "writer's final-drain telemetry.flush scope is flushed and written");
        }
        finally
        {
            TelemetryRouter.Shutdown("cleanup");
            DeleteQuietly(gameRoot);
        }
        LogPass("perf shutdown keeps writer final drain scopes");
    }

    // ---------------------------------------------------------------- L8

    private static void PerfAggregateWindowDefaultsToFiveSeconds()
    {
        // ~150-200 MB/hr of 1 s aggregate rows against a 250 MB session cap; 5 s windows
        // cut that 5x while slow rows stay immediate (capped per scope per window).
        TestHarness.AssertEqual(5000L, TelemetryPerf.AggregateWindowMs, "default aggregate window");
        LogPass("perf aggregate window defaults to five seconds");
    }

    // ---------------------------------------------------------------- L2 / L3

    // Stream wrapper for writer fault injection: optionally writes only part of the first
    // Write call before throwing (a torn write), or fails every Write while Fail is set.
    private sealed class FaultStream : Stream
    {
        private readonly FileStream _inner;
        internal int TornWritesRemaining;
        internal Func<bool> Fail = () => false;
        internal bool SetLengthFails;
        internal bool LengthFailsAfterFault;
        private bool _faulted;

        internal FaultStream(string path, FileMode mode)
        {
            _inner = new FileStream(path, mode, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);
        }

        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return _inner.CanSeek; } }
        public override bool CanWrite { get { return true; } }
        public override long Length
        {
            get
            {
                if (LengthFailsAfterFault && _faulted)
                    throw new IOException("injected length failure on faulted handle");
                return _inner.Length;
            }
        }
        public override long Position { get { return _inner.Position; } set { _inner.Position = value; } }
        public override void Flush() { _inner.Flush(); }
        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { return _inner.Seek(offset, origin); }
        public override void SetLength(long value)
        {
            if (SetLengthFails)
                throw new IOException("injected SetLength failure");
            _inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Fail())
                throw new IOException("injected sharing violation");
            if (TornWritesRemaining > 0 && count > 1)
            {
                TornWritesRemaining--;
                _faulted = true;
                _inner.Write(buffer, offset, count / 2 + 7);
                _inner.Flush();
                throw new IOException("injected torn write");
            }
            _inner.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private static void WriterRetryAfterTornWriteDoesNotDuplicateRows()
    {
        string dir = TempDir();
        var saved = TelemetryWriter.OpenFileStream;
        int savedBackoff = TelemetryWriter.RetryBaseMilliseconds;
        try
        {
            int torn = 1;
            TelemetryWriter.RetryBaseMilliseconds = 5;
            TelemetryWriter.OpenFileStream = (path, mode) =>
            {
                var stream = new FaultStream(path, mode);
                if (mode == FileMode.Append && path.EndsWith("tactical.jsonl", StringComparison.Ordinal) && torn-- > 0)
                    stream.TornWritesRemaining = 1;
                return stream;
            };
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            for (int i = 0; i < 6; i++)
                queue.Enqueue(Decision("w", i));
            var writer = new TelemetryWriter(queue, budget, dir, delegate { }, "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 16);
            writer.Start();
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer stops");

            string[] lines = File.ReadAllLines(Path.Combine(dir, "tactical.jsonl"));
            TestHarness.AssertEqual(6, lines.Length, "each row exactly once after a torn write + retry");
            var seen = new HashSet<string>();
            foreach (string line in lines)
            {
                var row = Newtonsoft.Json.Linq.JObject.Parse(line);
                TestHarness.AssertTrue(seen.Add((string)row["inputSignature"]), "no duplicate row");
            }
            TestHarness.AssertTrue(writer.SinkFailureCount >= 1, "torn write counted as a failure");
            TestHarness.AssertFalse(writer.FileSinkDisabled, "single failure does not disable the sink");
        }
        finally
        {
            TelemetryWriter.OpenFileStream = saved;
            TelemetryWriter.RetryBaseMilliseconds = savedBackoff;
            DeleteQuietly(dir);
        }
        LogPass("writer retry after torn write does not duplicate rows");
    }

    private static void WriterCountsRowsOnlyAfterTheirWriteSucceeds()
    {
        string dir = TempDir();
        var saved = TelemetryWriter.OpenFileStream;
        int savedBackoff = TelemetryWriter.RetryBaseMilliseconds;
        var savedClock = TelemetryWriter.Clock;
        bool failing = true;
        try
        {
            var clock = new FakeWriterClock();
            TelemetryWriter.Clock = clock.Now;
            TelemetryWriter.RetryBaseMilliseconds = 20;
            TelemetryWriter.OpenFileStream = (path, mode) =>
            {
                var stream = new FaultStream(path, mode);
                if (path.EndsWith("tactical.jsonl", StringComparison.Ordinal))
                    stream.Fail = () => System.Threading.Volatile.Read(ref failing);
                return stream;
            };
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            var writer = new TelemetryWriter(queue, budget, dir, delegate { }, "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 16);
            writer.Start();
            for (int i = 0; i < 3; i++)
                queue.Enqueue(Decision("w", i));
            writer.Signal();
            TestHarness.AssertTrue(WaitUntil(() => writer.SinkFailureCount >= 1, 5000), "failure observed");
            TestHarness.AssertFalse(writer.OutputFilesSnapshot().Contains("tactical.jsonl"), "failed rows are not accounted as written output");
            System.Threading.Volatile.Write(ref failing, false);
            TestHarness.AssertTrue(WaitUntil(() => AdvanceAndSignal(clock, writer) && writer.OutputFilesSnapshot().Contains("tactical.jsonl"), 5000), "accounted once the write succeeds");
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer stops");
            TestHarness.AssertEqual(3, CountLines(Path.Combine(dir, "tactical.jsonl")), "rows written once");
        }
        finally
        {
            System.Threading.Volatile.Write(ref failing, false);
            TelemetryWriter.OpenFileStream = saved;
            TelemetryWriter.RetryBaseMilliseconds = savedBackoff;
            TelemetryWriter.Clock = savedClock;
            DeleteQuietly(dir);
        }
        LogPass("writer counts rows only after their write succeeds");
    }

    private static void WriterShutdownTimeoutFailureRowNeverOpensSecondHandle()
    {
        string dir = TempDir();
        var saved = TelemetryWriter.OpenFileStream;
        var entered = new System.Threading.ManualResetEvent(false);
        var release = new System.Threading.ManualResetEvent(false);
        int offThreadFailureOpens = 0;
        try
        {
            TelemetryWriter.OpenFileStream = (path, mode) =>
            {
                if (path.EndsWith("failures.jsonl", StringComparison.Ordinal)
                    && System.Threading.Thread.CurrentThread.Name != "WhiskeyRealismTelemetryWriter")
                    System.Threading.Interlocked.Increment(ref offThreadFailureOpens);
                return new FaultStream(path, mode);
            };
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            int manifestCalls = 0;
            var writer = new TelemetryWriter(queue, budget, dir, delegate
            {
                if (System.Threading.Interlocked.Increment(ref manifestCalls) == 1)
                {
                    entered.Set();
                    release.WaitOne(5000);
                }
            }, "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 16);
            writer.Start();
            queue.Enqueue(Decision("w", 1));
            writer.Signal();
            TestHarness.AssertTrue(entered.WaitOne(2000), "writer blocked in manifest");
            TestHarness.AssertFalse(writer.StopAndFlush(1), "shutdown times out while writer is blocked");
            TestHarness.AssertEqual(0, offThreadFailureOpens, "timeout failure row does not open failures.jsonl off the writer thread");
            release.Set();
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer finishes after release");
            TestHarness.AssertTrue(ReadIfExists(Path.Combine(dir, "failures.jsonl")).Contains("writer-shutdown-timeout"), "timeout failure row written by the writer thread");
            TestHarness.AssertEqual(0, offThreadFailureOpens, "still no second handle");
        }
        finally
        {
            release.Set();
            TelemetryWriter.OpenFileStream = saved;
            DeleteQuietly(dir);
        }
        LogPass("writer shutdown timeout failure row never opens second handle");
    }

    // ---------------------------------------------------------------- R2-3

    private static void WriterClosesOldRotationHandleOpenedInRotatingBatch()
    {
        string dir = TempDir();
        try
        {
            long rowBytes = System.Text.Encoding.UTF8.GetByteCount(TelemetryJson.ToJsonLine(Decision("w", 0)));
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: rowBytes + rowBytes / 2);
            queue.Enqueue(Decision("w", 0));
            queue.Enqueue(Decision("w", 1));
            var writer = new TelemetryWriter(queue, budget, dir, delegate { }, "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 16);
            writer.Start();
            TestHarness.AssertTrue(
                WaitUntil(() => queue.Count == 0 && CountLines(Path.Combine(dir, "tactical.jsonl")) == 1 && CountLines(Path.Combine(dir, "tactical.001.jsonl")) == 1, 3000),
                "batch split across a rotation");
            TestHarness.AssertTrue(WaitUntil(() => writer.OpenStreamCount == 1, 1000), "old-rotation handle opened in the rotating batch is closed; open=" + writer.OpenStreamCount.ToString(CultureInfo.InvariantCulture));
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer stops");
            TestHarness.AssertEqual(0, TelemetryWriter.RotationIndexOf("tactical.jsonl"), "base file rotation");
            TestHarness.AssertEqual(12, TelemetryWriter.RotationIndexOf("performance.012.jsonl"), "rotated file rotation");
        }
        finally
        {
            DeleteQuietly(dir);
        }
        LogPass("writer closes old rotation handle opened in rotating batch");
    }

    // ---------------------------------------------------------------- R2-4

    private static Func<string, FileMode, Stream> TornFactory(string fileSuffix, int tornWrites, bool setLengthFails, bool lengthFailsAfterFault)
    {
        int remaining = tornWrites;
        return (path, mode) =>
        {
            var stream = new FaultStream(path, mode);
            if (path.EndsWith(fileSuffix, StringComparison.Ordinal))
            {
                if (mode == FileMode.Append && remaining > 0) { remaining--; stream.TornWritesRemaining = 1; }
                stream.SetLengthFails = setLengthFails;
                stream.LengthFailsAfterFault = lengthFailsAfterFault;
            }
            return stream;
        };
    }

    private static List<Newtonsoft.Json.Linq.JObject> ValidRows(string path, out int invalidLines)
    {
        invalidLines = 0;
        var rows = new List<Newtonsoft.Json.Linq.JObject>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { rows.Add(Newtonsoft.Json.Linq.JObject.Parse(line)); }
            catch (Newtonsoft.Json.JsonException) { invalidLines++; }
        }
        return rows;
    }

    private static void WriterUnknownLengthAfterFaultReopensInsteadOfDuplicating()
    {
        string dir = TempDir();
        var saved = TelemetryWriter.OpenFileStream;
        int savedBackoff = TelemetryWriter.RetryBaseMilliseconds;
        try
        {
            TelemetryWriter.RetryBaseMilliseconds = 5;
            TelemetryWriter.OpenFileStream = TornFactory("tactical.jsonl", 1, setLengthFails: false, lengthFailsAfterFault: true);
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            for (int i = 0; i < 6; i++)
                queue.Enqueue(Decision("w", i));
            var writer = new TelemetryWriter(queue, budget, dir, delegate { }, "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 16);
            writer.Start();
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer stops");

            int invalid;
            var rows = ValidRows(Path.Combine(dir, "tactical.jsonl"), out invalid);
            TestHarness.AssertEqual(6, rows.Count, "each row once although the faulted handle could not report its length");
            TestHarness.AssertEqual(0, invalid, "torn fragment truncated via the reopened handle");
        }
        finally
        {
            TelemetryWriter.OpenFileStream = saved;
            TelemetryWriter.RetryBaseMilliseconds = savedBackoff;
            DeleteQuietly(dir);
        }
        LogPass("writer unknown length after fault reopens instead of duplicating");
    }

    private static void WriterUntruncatableTornRowIsIsolatedOnItsOwnLine()
    {
        string dir = TempDir();
        var saved = TelemetryWriter.OpenFileStream;
        int savedBackoff = TelemetryWriter.RetryBaseMilliseconds;
        try
        {
            TelemetryWriter.RetryBaseMilliseconds = 5;
            TelemetryWriter.OpenFileStream = TornFactory("tactical.jsonl", 1, setLengthFails: true, lengthFailsAfterFault: false);
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            for (int i = 0; i < 6; i++)
                queue.Enqueue(Decision("w", i));
            var writer = new TelemetryWriter(queue, budget, dir, delegate
            {
                var manifest = TelemetryManifest.Create("w", "t", "-", TelemetryProfile.FullTuning, DateTime.UtcNow, budget, 0);
                manifest.DroppedCounters["tornRows"] = 1L;
                File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest.ToJson());
            }, "w", TelemetryProfile.FullTuning, null, flushMilliseconds: 50, flushRows: 16);
            writer.Start();
            TestHarness.AssertTrue(writer.StopAndFlush(3000), "writer stops");

            int invalid;
            var rows = ValidRows(Path.Combine(dir, "tactical.jsonl"), out invalid);
            TestHarness.AssertEqual(6, rows.Count, "every row intact on its own line");
            TestHarness.AssertEqual(1, invalid, "the torn fragment is isolated as one invalid line");
            TestHarness.AssertEqual(1L, writer.TornRowCount, "torn row counted");
            var seen = new HashSet<string>();
            foreach (var row in rows)
                TestHarness.AssertTrue(seen.Add((string)row["inputSignature"]), "no duplicate row");

            var validation = TelemetrySessionValidator.ValidateDirectory(dir);
            TestHarness.AssertTrue(validation.Success, "validator tolerates the counted torn row: " + validation.Summary);
            string summary = TelemetrySummary.FromDirectory(dir).ToMarkdown();
            TestHarness.AssertTrue(summary.Contains("PTestWriterDecision=6"), "summary skips the torn line");
        }
        finally
        {
            TelemetryWriter.OpenFileStream = saved;
            TelemetryWriter.RetryBaseMilliseconds = savedBackoff;
            DeleteQuietly(dir);
        }
        LogPass("writer untruncatable torn row is isolated on its own line");
    }

    private static void WriterFailureRowsUseTornWriteHandling()
    {
        string dir = TempDir();
        var saved = TelemetryWriter.OpenFileStream;
        try
        {
            TelemetryWriter.OpenFileStream = TornFactory("failures.jsonl", 1, setLengthFails: true, lengthFailsAfterFault: false);
            var queue = new TelemetryQueue(capacity: 64);
            var budget = new TelemetryBudget(totalBytes: 1 << 20, rotateBytes: 1 << 20);
            var writer = new TelemetryWriter(queue, budget, dir, delegate { }, "w", TelemetryProfile.FullTuning, null);
            // Writer thread never started: failure rows take the one-shot path.
            writer.RecordRuntimeSinkFailure("p-test-first", null);
            writer.RecordRuntimeSinkFailure("p-test-second", null);

            int invalid;
            var rows = ValidRows(Path.Combine(dir, "failures.jsonl"), out invalid);
            TestHarness.AssertEqual(1, invalid, "torn fragment of the first failure row isolated on its own line");
            TestHarness.AssertEqual(2, rows.Count, "first failure row retried once and second row intact, neither glued to the fragment");
            TestHarness.AssertTrue(((string)rows[0]["fields"]["reason"]).StartsWith("p-test-first"), "first row retried");
            TestHarness.AssertTrue(((string)rows[1]["fields"]["reason"]).StartsWith("p-test-second"), "second row parsed");
            TestHarness.AssertEqual(1L, writer.TornRowCount, "torn failure row counted");
        }
        finally
        {
            TelemetryWriter.OpenFileStream = saved;
            DeleteQuietly(dir);
        }
        LogPass("writer failure rows use torn write handling");
    }
}
