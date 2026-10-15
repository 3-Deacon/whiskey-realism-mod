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
}
