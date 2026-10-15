using System;
using System.Threading.Tasks;
using WhiskeyRealism;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

internal static class PatchFirstFireTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("patch first-fire logs one tagged line per patch id", LogsOneTaggedLinePerPatchId),
            ("patch first-fire keeps distinct ids independent", KeepsDistinctIdsIndependent),
            ("patch first-fire retries after logger failure", RetriesAfterLoggerFailure),
            ("patch first-fire concurrent marks log once", ConcurrentMarksLogOnce),
            ("patch first-fire id built per call logs once", IdBuiltPerCallLogsOnce),
        });
    }

    private static TestLog FreshLog()
    {
        TelemetryRouter.AttachRuntime(null);
        var log = new TestLog();
        Plugin.Log = log;
        return log;
    }

    private static void LogsOneTaggedLinePerPatchId()
    {
        var log = FreshLog();
        PatchFirstFire.Mark("test.patch.first-fire.once");
        PatchFirstFire.Mark("test.patch.first-fire.once");
        PatchFirstFire.Mark("test.patch.first-fire.once");

        TestHarness.AssertEqual(1, log.InfoCount, "one line for repeated marks");
        TestHarness.AssertEqual("[first-fire] test.patch.first-fire.once", log.LastInfo, "line format");
        TestHarness.AssertTrue(PatchFirstFire.HasFired("test.patch.first-fire.once"), "id recorded");
    }

    private static void KeepsDistinctIdsIndependent()
    {
        var log = FreshLog();
        PatchFirstFire.Mark("test.patch.first-fire.a");
        PatchFirstFire.Mark("test.patch.first-fire.b");
        PatchFirstFire.Mark("test.patch.first-fire.a");

        TestHarness.AssertEqual(2, log.InfoCount, "one line per distinct id");
        TestHarness.AssertFalse(PatchFirstFire.HasFired("test.patch.first-fire.never"), "unmarked id not recorded");
    }

    private static void RetriesAfterLoggerFailure()
    {
        Plugin.Log = null;
        PatchFirstFire.Mark("test.patch.first-fire.retry");
        TestHarness.AssertFalse(PatchFirstFire.HasFired("test.patch.first-fire.retry"), "no logger: not recorded");

        var log = FreshLog();
        log.ThrowOnInfo = true;
        PatchFirstFire.Mark("test.patch.first-fire.retry");
        TestHarness.AssertFalse(PatchFirstFire.HasFired("test.patch.first-fire.retry"), "throwing logger: not recorded");

        log.ThrowOnInfo = false;
        PatchFirstFire.Mark("test.patch.first-fire.retry");
        PatchFirstFire.Mark("test.patch.first-fire.retry");
        TestHarness.AssertEqual(1, log.InfoCount, "logs once after logger recovers");
        TestHarness.AssertTrue(PatchFirstFire.HasFired("test.patch.first-fire.retry"), "recorded after logging");
    }

    private static void ConcurrentMarksLogOnce()
    {
        var log = FreshLog();
        var tasks = new Task[32];
        for (int i = 0; i < tasks.Length; i++)
            tasks[i] = Task.Run(() => PatchFirstFire.Mark("test.patch.first-fire.concurrent"));
        Task.WaitAll(tasks);

        TestHarness.AssertEqual(1, log.InfoCount, "concurrent marks log once");
    }

    // F3 identity fast path: an id built per call (a new instance each time) is still
    // one id, past the alias cap too.
    private static void IdBuiltPerCallLogsOnce()
    {
        var log = FreshLog();
        char[] text = "test.patch.first-fire.per-call".ToCharArray();
        for (int i = 0; i < PatchFirstFire.MaxAliases + 100; i++)
            PatchFirstFire.Mark(new string(text));

        TestHarness.AssertEqual(1, log.InfoCount, "one line for an id built per call");
        TestHarness.AssertTrue(PatchFirstFire.HasFired("test.patch.first-fire.per-call"), "id recorded");
    }
}
