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
        OnceLogClaimReturnsTrueOnlyOnce();
        OnceLogFiredKeySkipsMessageWork();
        OnceLogBoundsFiredKeys();
        QueueProtectedRowDoesNotEvictDetailRows();
        QueuePerformanceDoesNotEvictDecisionOrState();
        QueueEvictsTraceBeforePerformance();
        QueueDrainPreservesFifoAcrossCategories();
        QueueCountersStayExactUnderChurn();
        QueueEnqueueIsConstantTimeWhenFull();
        RouterSeriousLineMatchesWholeTokens();
        RouterBlankDecisionSignatureIsFlaggedNotDropped();
        RouterEmitsDecisionWithBlankSignature();
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        int hits = 0;
        for (int i = 0; i < 10000; i++)
        {
            if (TacticalPatchIds.NodeIdMatches(a, 123456, 7)) hits++;
            if (TacticalPatchIds.NodeIdMatches(b, 11, -98765)) hits++;
            if (TacticalPatchIds.NodeIdMatches(a, 11, 12)) hits++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestHarness.AssertEqual(20000, hits, "hit count");
        TestHarness.AssertEqual(0L, allocated, "NodeIdMatches allocated bytes");
        LogPass("node id matching does not allocate");
    }

    // ---------------------------------------------------------------- OnceLog

    private static void OnceLogClaimReturnsTrueOnlyOnce()
    {
        OnceLog.Reset();
        TestHarness.AssertTrue(OnceLog.Claim("p-test:claim"), "first claim wins");
        TestHarness.AssertFalse(OnceLog.Claim("p-test:claim"), "second claim loses");
        int infoBefore = WhiskeyRealism.Plugin.Log.InfoCount;
        OnceLog.Info("p-test:claim", "[PTest] should not log; key already claimed");
        TestHarness.AssertEqual(infoBefore, WhiskeyRealism.Plugin.Log.InfoCount, "claimed key suppresses Info");
        TestHarness.AssertTrue(OnceLog.Claim("p-test:claim-other"), "independent key claims");
        OnceLog.Reset();
        TestHarness.AssertTrue(OnceLog.Claim("p-test:claim"), "reset releases claims");
        OnceLog.Reset();
        LogPass("oncelog claim returns true only once");
    }

    private static void OnceLogFiredKeySkipsMessageWork()
    {
        OnceLog.Reset();
        const string key = "p-test:fired";
        const string message = "[PTest] fired once";
        OnceLog.Info(key, message);
        OnceLog.Warning(key, message);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            OnceLog.Info(key, message);
            OnceLog.Warning(key, message);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestHarness.AssertEqual(0L, allocated, "fired OnceLog key allocated bytes");
        OnceLog.Reset();
        LogPass("oncelog fired key skips message work");
    }

    private static void OnceLogBoundsFiredKeys()
    {
        OnceLog.Reset();
        int warningsBefore = WhiskeyRealism.Plugin.Log.WarningCount;
        for (int i = 0; i < OnceLog.MaxKeys; i++)
            TestHarness.AssertTrue(OnceLog.Claim("p-test:bound:" + i.ToString(CultureInfo.InvariantCulture)), "claim under cap " + i.ToString(CultureInfo.InvariantCulture));
        TestHarness.AssertEqual(OnceLog.MaxKeys, OnceLog.FiredCount, "fired set at cap");
        TestHarness.AssertFalse(OnceLog.Claim("p-test:bound:over"), "claim over cap refused");
        int infoBefore = WhiskeyRealism.Plugin.Log.InfoCount;
        OnceLog.Info("p-test:bound:over-info", "[PTest] over cap");
        TestHarness.AssertEqual(infoBefore, WhiskeyRealism.Plugin.Log.InfoCount, "info over cap suppressed");
        TestHarness.AssertEqual(OnceLog.MaxKeys, OnceLog.FiredCount, "fired set stays bounded");
        TestHarness.AssertEqual(warningsBefore + 1, WhiskeyRealism.Plugin.Log.WarningCount, "cap reached warns exactly once");
        TestHarness.AssertTrue(OnceLog.SuppressedOverCapCount >= 2, "over-cap suppressions counted");
        OnceLog.Reset();
        TestHarness.AssertEqual(0, OnceLog.FiredCount, "reset clears fired set");
        LogPass("oncelog bounds fired keys");
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

    private static void QueuePerformanceDoesNotEvictDecisionOrState()
    {
        var queue = new TelemetryQueue(capacity: 2);
        queue.Enqueue(Ev(TelemetryCategory.Decision, "decision-a"));
        queue.Enqueue(Ev(TelemetryCategory.State, "state-a"));
        TestHarness.AssertEqual(TelemetryQueueResult.Dropped, queue.Enqueue(Ev(TelemetryCategory.Performance, "perf-a")), "performance dropped when only decision/state queued");
        TestHarness.AssertEqual(1L, queue.DroppedCountFor(TelemetryCategory.Performance), "performance drop counted");
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.Decision, "decision-b")), "decision evicts state");
        var drained = queue.Drain(10);
        TestHarness.AssertEqual(2, drained.Count, "count");
        TestHarness.AssertEqual("decision-a", drained[0].EventName, "decision-a kept");
        TestHarness.AssertEqual("decision-b", drained[1].EventName, "decision-b kept");
        LogPass("queue performance does not evict decision or state");
    }

    private static void QueueEvictsTraceBeforePerformance()
    {
        var queue = new TelemetryQueue(capacity: 2);
        queue.Enqueue(Ev(TelemetryCategory.Performance, "perf-a"));
        queue.Enqueue(Ev(TelemetryCategory.Trace, "trace-a"));
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.Performance, "perf-b")), "performance evicts trace");
        TestHarness.AssertEqual(TelemetryQueueResult.Enqueued, queue.Enqueue(Ev(TelemetryCategory.Gate, "gate-a")), "gate evicts performance");
        var drained = queue.Drain(10);
        TestHarness.AssertEqual(2, drained.Count, "count");
        TestHarness.AssertEqual("perf-a", drained[0].EventName, "oldest performance kept (newest evicted)");
        TestHarness.AssertEqual("gate-a", drained[1].EventName, "gate kept");
        TestHarness.AssertEqual(1L, queue.DroppedCountFor(TelemetryCategory.Trace), "trace evicted");
        TestHarness.AssertEqual(1L, queue.DroppedCountFor(TelemetryCategory.Performance), "performance evicted");
        LogPass("queue evicts trace before performance");
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
}
