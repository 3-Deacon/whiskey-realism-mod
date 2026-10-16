using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using static TestHarness;

// Pins TestFixtures.BeginAllocationWindow (docs: the helper's own comment). The allocation tests
// flaked under load with a few KB "allocated" in code that allocates nothing: a background GC
// ending its mark phase voids every thread's allocation context without taking the unused part
// off GC.GetAllocatedBytesForCurrentThread(), so the raw counter jumps by up to one quantum.
// These tests force that GC inside a window with a pressure thread instead of waiting for it.
internal static class AllocationMeasurementTests
{
    private static volatile bool _pressureStop;

    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("alloc window reads exactly zero while background GCs end inside it", WindowIgnoresBackgroundGcMarkEnd),
            ("alloc window still counts a real allocation", WindowCountsRealAllocation),
            ("alloc tests read the thread counter only through the shared window", NoTestReadsTheRawCounter),
        });
    }

    // Runs `body` while another thread allocates large arrays, which keeps background GCs
    // (gen2, concurrent) starting and ending throughout.
    private static void WithPressure(Action body)
    {
        _pressureStop = false;
        var pressure = new Thread(() =>
        {
            var keep = new object[32];
            int k = 0;
            while (!_pressureStop) keep[k++ & 31] = new byte[200000 + (k % 5) * 1000];
        });
        pressure.IsBackground = true;
        pressure.Start();
        try { body(); }
        finally
        {
            _pressureStop = true;
            pressure.Join();
        }
    }

    // Allocation-free wait for the next gen2 collection (or the deadline).
    private static void SpinUntilGen2Or(long deadlineTimestamp, int gen2AtStart)
    {
        while (Stopwatch.GetTimestamp() < deadlineTimestamp && GC.CollectionCount(2) == gen2AtStart) { }
    }

    private static void WindowIgnoresBackgroundGcMarkEnd()
    {
        const int tries = 40;
        long spin = Stopwatch.Frequency / 20;   // 50 ms ceiling per try
        bool concurrent = ConcurrentGcEnabled();
        long backgroundGcsBefore = GC.GetGCMemoryInfo(GCKind.Background).Index;
        long unguardedJumps = 0;
        long guardedBytes = 0;
        WithPressure(() =>
        {
            for (int i = 0; i < tries; i++)
            {
                // A small allocation leaves the thread's context with an unused tail, the
                // part the void adds to the raw counter.
                object tail = new object();
                int gen2 = GC.CollectionCount(2);
                long raw = TestFixtures.UnguardedThreadAllocatedBytes();
                SpinUntilGen2Or(Stopwatch.GetTimestamp() + spin, gen2);
                if (TestFixtures.UnguardedThreadAllocatedBytes() != raw) unguardedJumps++;
                GC.KeepAlive(tail);

                tail = new object();
                long begin = TestFixtures.BeginAllocationWindow();
                gen2 = GC.CollectionCount(2);
                SpinUntilGen2Or(Stopwatch.GetTimestamp() + spin, gen2);
                guardedBytes += TestFixtures.AllocatedSince(begin);
                GC.KeepAlive(tail);
            }
        });
        long backgroundGcs = GC.GetGCMemoryInfo(GCKind.Background).Index - backgroundGcsBefore;

        AssertEqual(0L, guardedBytes, "bytes read in windows that waited out a gen2 collection");
        if (concurrent)
        {
            // The control: without the window the same wait reads phantom bytes. If this ever
            // stops holding, the runtime fixed the accounting and the window can be dropped.
            AssertTrue(backgroundGcs > 0, "the pressure thread produced no background GC (" + backgroundGcs + ")");
            AssertTrue(unguardedJumps > 0, "the raw counter never moved across " + backgroundGcs + " background GCs: the artifact this window guards against no longer occurs");
        }
    }

    private static void WindowCountsRealAllocation()
    {
        object kept;
        long begin = TestFixtures.BeginAllocationWindow();
        kept = new byte[100];
        long small = TestFixtures.AllocatedSince(begin);
        AssertTrue(small >= 100 && small < 16 * 1024, "a 100-byte array reads " + small);
        begin = TestFixtures.BeginAllocationWindow();
        kept = new byte[200000];
        long large = TestFixtures.AllocatedSince(begin);
        AssertTrue(large >= 200000, "a 200000-byte array reads " + large);
        GC.KeepAlive(kept);
    }

    // Every allocation test goes through the window; the raw counter lives only in TestFixtures.
    private const string RawCounterName = "GetAllocatedBytes" + "ForCurrentThread";

    private static void NoTestReadsTheRawCounter()
    {
        string dir = Path.GetDirectoryName(TestFixtures.FindRepoPath("tests/WhiskeyRealism.Tests/TestFixtures.cs"));
        int scanned = 0;
        foreach (string path in Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(path) == "TestFixtures.cs") continue;
            scanned++;
            foreach (string line in File.ReadAllLines(path))
            {
                string code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal)) continue;
                AssertFalse(code.Contains(RawCounterName),
                    Path.GetFileName(path) + " reads the raw per-thread allocation counter; use TestFixtures.BeginAllocationWindow/AllocatedSince");
            }
        }
        AssertTrue(scanned > 100, "test files scanned: " + scanned);
    }

    private static bool ConcurrentGcEnabled()
    {
        foreach (var kv in GC.GetConfigurationVariables())
            if (kv.Key == "Concurrent") return kv.Value is bool b ? b : Convert.ToBoolean(kv.Value);
        return true;
    }
}
