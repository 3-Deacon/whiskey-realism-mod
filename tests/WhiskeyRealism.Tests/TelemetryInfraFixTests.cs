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
}
