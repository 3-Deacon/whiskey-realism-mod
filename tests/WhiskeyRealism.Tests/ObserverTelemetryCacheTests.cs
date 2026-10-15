using System;
using System.IO;
using WhiskeyRealism.Tactical;
using static TestHarness;

// Perf slice F1: the observer / decision-matrix caches and their wiring.
internal static class ObserverTelemetryCacheTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("observer odds memo returns the stored value only for an equal key", OddsMemoHitsOnlyEqualKey),
            ("observer odds memo keeps one slot per battle", OddsMemoOneSlotPerBattle),
            ("matrix rows written on change or keep-alive only", MatrixRowsOnChangeOrKeepAlive),
            ("matrix row buckets match the decision input signature buckets", MatrixRowBucketsMatchSignature),
            ("matrix written once per battle per frame with the frame's events", MatrixOncePerBattlePerFrame),
            ("observer builds nothing when the profile writes no tactical rows", ObserverGatedOnProfile),
            ("observer memoizes only the side-wide odds, counts stay live", ObserverMemoizesOnlyOdds),
        });
    }

    private static void OddsMemoHitsOnlyEqualKey()
    {
        var memo = new OddsMemo<int>();
        object battle = new object(), snapshot = new object(), units = new object();
        var key = new OddsMemoKey(battle, 10, snapshot, 5, units, 3);
        AssertFalse(memo.TryGet(in key, out _), "empty memo misses");
        memo.Store(in key, 42);
        AssertTrue(memo.TryGet(new OddsMemoKey(battle, 10, snapshot, 5, units, 3), out int v) && v == 42, "equal key hits");
        AssertFalse(memo.TryGet(new OddsMemoKey(battle, 11, snapshot, 5, units, 3), out _), "next frame misses");
        AssertFalse(memo.TryGet(new OddsMemoKey(battle, 10, new object(), 5, units, 3), out _), "rebuilt snapshot object misses");
        AssertFalse(memo.TryGet(new OddsMemoKey(battle, 10, snapshot, 6, units, 3), out _), "next snapshot tick misses");
        AssertFalse(memo.TryGet(new OddsMemoKey(battle, 10, snapshot, 5, new object(), 3), out _), "another unit list misses");
        AssertFalse(memo.TryGet(new OddsMemoKey(battle, 10, snapshot, 5, units, 4), out _), "a list of another length misses");
        AssertFalse(memo.TryGet(new OddsMemoKey(new object(), 10, snapshot, 5, units, 3), out _), "another battle misses");
        memo.Clear();
        AssertFalse(memo.TryGet(in key, out _), "cleared");
    }

    private static void OddsMemoOneSlotPerBattle()
    {
        var memo = new OddsMemo<int>();
        var battles = new[] { new object(), new object(), new object() };
        for (int frame = 0; frame < 5; frame++)
            for (int b = 0; b < battles.Length; b++)
                memo.Store(new OddsMemoKey(battles[b], frame, null, 0, null, 0), frame * 10 + b);
        for (int b = 0; b < battles.Length; b++)
            AssertTrue(memo.TryGet(new OddsMemoKey(battles[b], 4, null, 0, null, 0), out int v) && v == 40 + b, "battle " + b + " keeps its latest frame");
    }

    private static void MatrixRowsOnChangeOrKeepAlive()
    {
        var rows = new MatrixRowDedupe();
        AssertTrue(rows.ShouldEmit(7, 100, 0f), "first row");
        AssertFalse(rows.ShouldEmit(7, 100, 10f), "unchanged row skipped");
        AssertTrue(rows.ShouldEmit(8, 100, 10f), "another group is independent");
        AssertTrue(rows.ShouldEmit(7, 101, 11f), "changed inputs written");
        AssertFalse(rows.ShouldEmit(7, 101, 11f + MatrixRowDedupe.KeepAliveSeconds - 0.01f), "inside the keep-alive");
        AssertTrue(rows.ShouldEmit(7, 101, 11f + MatrixRowDedupe.KeepAliveSeconds), "keep-alive rewrites");
        rows.Clear();
        AssertTrue(rows.ShouldEmit(8, 100, 12f), "cleared per battle");
    }

    private static void MatrixRowBucketsMatchSignature()
    {
        // Two floats share a dedupe bucket exactly when DecisionInputSignature writes them alike.
        var rng = new Random(9);
        for (int i = 0; i < 2000; i++)
        {
            float a = (float)(rng.NextDouble() * 8.0 - 4.0), b = (float)(rng.NextDouble() * 8.0 - 4.0);
            if (i % 3 == 0) b = a + 0.01f;
            bool sameText = TacticalTelemetry.DecisionInputSignature("x", a) == TacticalTelemetry.DecisionInputSignature("x", b);
            AssertEqual(sameText, MatrixRowDedupe.Bucket(a) == MatrixRowDedupe.Bucket(b), "bucket " + a + " / " + b);
        }
        AssertEqual(0, MatrixRowDedupe.Bucket(float.NaN), "NaN bucket");
    }

    private static void MatrixOncePerBattlePerFrame()
    {
        var gate = new MatrixCycleGate();
        object a = new object(), b = new object();
        AssertEqual(1, gate.Note(a, 5, 1), "first event");
        AssertEqual(3, gate.Note(a, 5, 2), "events accumulate in the frame");
        AssertTrue(gate.TryWrite(a, 5), "first write of the frame");
        AssertFalse(gate.TryWrite(a, 5), "a replay in the same frame writes nothing");
        AssertTrue(gate.TryWrite(b, 5), "the other side writes its own");
        AssertEqual(4, gate.Note(a, 6, 4), "next frame starts empty");
        AssertTrue(gate.TryWrite(a, 6), "next frame writes");
    }

    private static string Observer()
    {
        return File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Patches/TacticalObserverPatch.cs"));
    }

    private static void ObserverGatedOnProfile()
    {
        string src = Observer();
        AssertTrue(src.Contains("TelemetryRouter.ShouldEmit(TelemetryRouter.CurrentProfile, TelemetryLayer.Tactical, TelemetryCategory.State)"), "rows gate reads the router");
        foreach (string gate in new[]
        {
            "if (!Enabled() || !WritesRows()) return null;",          // BuildContextIfEnabled
            "if (!WritesRows()) return;",                              // Observe
            "if (!DecisionMatrixEnabled() || !WritesRows()) return;",  // ops monitor
        })
            AssertTrue(src.Contains(gate), "gate: " + gate);
        AssertEqual(2, Count(src, "if (!Enabled() || !WritesRows()) return;"), "charge and feud prefixes gated");
        AssertFalse(src.Contains("BuildRegimentInstanceMap"), "no per-cycle regiment map");
        // The lifecycle detector, snapshots and coordinator tick stay ungated.
        int check = src.IndexOf("internal static void CheckGlobalAIStrategyPostfix", StringComparison.Ordinal);
        string body = src.Substring(check, src.IndexOf("private static void ResetOperationsTelemetry", check, StringComparison.Ordinal) - check);
        AssertTrue(body.IndexOf("EnsureBattleCoreSnapshots", StringComparison.Ordinal) < body.IndexOf("BuildContextIfEnabled", StringComparison.Ordinal), "snapshots before the gated context");
        AssertTrue(body.Contains("TacticalBattleCoordinator.Tick(__instance);"), "coordinator tick kept");
    }

    private static void ObserverMemoizesOnlyOdds()
    {
        string src = Observer();
        int start = src.IndexOf("private static TacticalBattleContext BuildContext(AIBattle battle, Regiment group)", StringComparison.Ordinal);
        string body = src.Substring(start, src.IndexOf("private static TacticalOddsAssessment SideOdds", start, StringComparison.Ordinal) - start);
        AssertTrue(body.Contains("var odds = SideOdds(battle, bunits, unitsUsed, side, context.Alliance);"), "odds through the memo");
        AssertTrue(body.Contains("CountUnits(unitsUsed, context)") && body.Contains("MergeGroupCounts(group, context)"), "counts stay live");
        AssertTrue(body.Contains("BuildOrderSignature(unitsUsed)"), "order signature stays live");
        AssertFalse(body.Contains("BuildOddsDoctrine("), "no unmemoized odds in BuildContext");
        int snap = src.IndexOf("private static TacticalObserverSnapshot SnapshotBattle(AIBattle battle)", StringComparison.Ordinal);
        AssertTrue(src.Substring(snap, 200).Contains("var context = BuildContext(battle, null);"), "feud before/after snapshots build a fresh context");
    }

    private static int Count(string text, string token)
    {
        int n = 0;
        for (int i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + token.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
