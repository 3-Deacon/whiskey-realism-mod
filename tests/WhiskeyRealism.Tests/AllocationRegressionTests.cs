using System;
using System.IO;
using WhiskeyRealism;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;
using static TestHarness;

// Perf slice B4: zero-GC hot paths, enforced. Each test warms the path up, then asserts
// GC.GetAllocatedBytesForCurrentThread() does not move across many calls. Paths pinned
// elsewhere: snapshot assembly (BattleSnapshotTests), arbiter resolve (OrderArbiterTests),
// perception update (BattlePerceptionTests), slice pressure (SlicePressureBuilderTests),
// the recon planner (BattleReconTests, BattleReconSearchTests).
internal static class AllocationRegressionTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("alloc perception publish then slice pressure allocate nothing once warm", PerceptionPublishAndPressure),
            ("alloc telemetry scope enter and exit allocate nothing under every profile", TelemetryScopeEnterExit),
            ("alloc OnceLog fired key allocates nothing", OnceLogFiredKey),
            ("alloc PatchFirstFire fired id allocates nothing", FirstFireFiredId),
            ("alloc observer caches allocate nothing in steady state", ObserverCaches),
            ("alloc deferred flag ledger allocates nothing in steady state", DeferredFlagLedgerSteadyState),
        });
    }

    // #83 hidden transport flags: defer, hidden cycles and the replay on show reuse the
    // pre-sized dictionary.
    private static void DeferredFlagLedgerSteadyState()
    {
        var ledger = new DeferredFlagLedger<object, object>(64);
        var units = new object[32];
        var models = new object[32];
        for (int i = 0; i < units.Length; i++) { units[i] = new object(); models[i] = new object(); }
        Action cycle = () =>
        {
            for (int i = 0; i < units.Length; i++) ledger.Defer(units[i], models[i]);
            for (int i = 0; i < units.Length; i++) ledger.Resolve(units[i], models[i], false);
            for (int i = 0; i < units.Length; i++) ledger.Resolve(units[i], models[i], true);
            ledger.Resolve(units[0], models[0], true);
        };
        AssertEqual(0L, Measure(cycle, 200), "deferred flag ledger bytes");
    }

    private static long Measure(Action body, int calls)
    {
        for (int i = 0; i < 16; i++) body();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < calls; i++) body();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void PerceptionPublishAndPressure()
    {
        const int enemies = 600;
        var probe = PerceptionProbeResult.Create(enemies);
        var memory = new EnemyObservation[enemies * 2];
        for (int i = 0; i < enemies; i++)
        {
            probe.EnemyIds[i] = 70000 + i;
            probe.Visible[i] = i % 3 != 0;
            probe.X[i] = i * 3f; probe.Z[i] = 400f + (i % 7) * 20f; probe.Strength[i] = 400; probe.UnitTyp[i] = 0;
            probe.Experience[i] = 0.5f; probe.Morale[i] = 0.8f;
        }
        probe.Count = enemies;
        int memoryCount = 0;
        BattlePerception.Update(memory, ref memoryCount, in probe, 1.0);
        var s = new BattleSnapshot();
        var geometry = new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f };
        Action tick = () =>
        {
            s.BeginBuild(1, 0, 0, 1, 1.0);
            s.SetSliceCount(20);
            for (int i = 0; i < 500; i++)
                s.AddOwn(new UnitRecord { InstanceId = i + 1, X = i * 3.5f, Z = 0f, ParentIndex = -1 });
            s.IndexCommandTree(null);
            int mc = memoryCount;
            BattlePerception.Update(memory, ref mc, in probe, 1.0);
            BattlePerception.Publish(s, PerceptionMode.FogGrid, PerceptionFallbackReason.None, memory, mc, in probe);
            SlicePressureBuilder.Build(s, geometry);
        };
        AssertEqual(0L, Measure(tick, 20), "perception publish + slice pressure bytes");
    }

    private static void TelemetryScopeEnterExit()
    {
        long savedWindow = TelemetryPerf.AggregateWindowMs;
        string gameRoot = Path.Combine(Path.GetTempPath(), "wr-alloc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gameRoot);
        try
        {
            // No aggregate window closes inside the measurement.
            TelemetryPerf.AggregateWindowMs = 3600000;
            foreach (TelemetryProfile profile in new[] { TelemetryProfile.Off, TelemetryProfile.TacticalTuning, TelemetryProfile.CampaignTuning, TelemetryProfile.FullTuning })
            {
                if (profile == TelemetryProfile.Off) TelemetryRouter.AttachRuntime(null);
                else TelemetryRouter.AttachRuntime(TelemetryRuntime.Start(TelemetryRuntimeConfig.Create(
                    gameRoot, "alloc", "0000", profile, maxTuningLogMb: 25, fileRotateMb: 25, retainedSessions: 1,
                    emitHumanSummary: false, performanceWarnings: true, createIssueBundleOnShutdown: false, warningCallback: null)));
                TelemetryFrameProbe.Tick(0.016f);
                long bytes = Measure(() =>
                {
                    using (TelemetryPerf.Scope("tactical.patch.alloc-test", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1000.0))
                    using (TelemetryPerf.Scope("campaign.patch.alloc-test", TelemetryLayer.Campaign, TelemetryCategory.Performance, 1000.0))
                    {
                    }
                }, 20000);
                AssertEqual(0L, bytes, "scope enter/exit bytes under " + profile);
                TelemetryRouter.Shutdown("alloc-test");
            }
        }
        finally
        {
            TelemetryPerf.AggregateWindowMs = savedWindow;
            TelemetryRouter.Shutdown("cleanup");
            try { Directory.Delete(gameRoot, true); } catch { }
        }
    }

    private static void OnceLogFiredKey()
    {
        TelemetryRouter.AttachRuntime(null);
        Plugin.Log = new TestLog();
        OnceLog.Info("alloc-test:once", "first");
        AssertEqual(0L, Measure(() => OnceLog.Info("alloc-test:once", "message"), 20000), "fired OnceLog key bytes");
    }

    private static void FirstFireFiredId()
    {
        TelemetryRouter.AttachRuntime(null);
        Plugin.Log = new TestLog();
        PatchFirstFire.Mark("test.alloc.first-fire");
        AssertEqual(0L, Measure(() => PatchFirstFire.Mark("test.alloc.first-fire"), 20000), "fired first-fire id bytes");
    }

    private static void ObserverCaches()
    {
        var memo = new OddsMemo<double>();
        var rows = new MatrixRowDedupe();
        var gate = new MatrixCycleGate();
        object battle = new object(), snapshot = new object(), units = new object();
        int frame = 0;
        Action cycle = () =>
        {
            frame++;
            var key = new OddsMemoKey(battle, frame, snapshot, frame, units, 80);
            if (!memo.TryGet(in key, out _)) memo.Store(in key, 1.5);
            gate.Note(battle, frame, 4);
            gate.TryWrite(battle, frame);
            for (int g = 0; g < 80; g++) rows.ShouldEmit(g, g * 7 + (frame & 1), frame);
        };
        AssertEqual(0L, Measure(cycle, 2000), "observer cache bytes");
    }
}
