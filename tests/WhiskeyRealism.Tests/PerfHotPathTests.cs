using System;
using System.IO;
using System.Text.RegularExpressions;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

// Perf slice (docs/superpowers/plans/2026-10-15-perf-hot-paths.md): equivalence and
// allocation pins for the hot-path optimisations.
internal static class PerfHotPathTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("perf scope names and first-fire ids are literals at every call site", ScopeNamesAndFirstFireIdsAreLiterals),
            ("perf W&L scope hooks return before their scope outside a W&L career", WlScopeHooksGateBeforeScope),
            ("perf skirmisher push uses compiled accessors in vanilla's write order", SkirmisherPushCompiledAccessors),
            ("perf compiled field setter writes like FieldInfo.SetValue", CompiledFieldSetterMatchesReflection),
            ("perf posture nav probe reuses its path and reads every corner", PostureNavProbeNoAlloc),
        });
    }

    // F3: TelemetryPerf and PatchFirstFire resolve names by identity first (two-tier,
    // ReferenceStringComparer). A computed name stays correct (ordinal fallback, capped
    // aliases) but loses the fast path, so call sites pass a literal, or a choice
    // between two literals.
    private static readonly Regex CallSite = new Regex(@"(TelemetryPerf\.Scope|PatchFirstFire\.Mark)\(\s*", RegexOptions.Compiled);
    private static readonly Regex Literal = new Regex(@"^""[^""\\]+""\s*[,)]", RegexOptions.Compiled);
    private static readonly Regex LiteralChoice = new Regex(@"^[A-Za-z_][A-Za-z0-9_.]*\s*\?\s*""[^""\\]+""\s*:\s*""[^""\\]+""\s*[,)]", RegexOptions.Compiled);

    private static void ScopeNamesAndFirstFireIdsAreLiterals()
    {
        int sites = 0;
        foreach (string path in Directory.GetFiles(TestFixtures.FindRepoPath("src/WhiskeyRealism"), "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(path);
            foreach (Match m in CallSite.Matches(source))
            {
                int lineStart = source.LastIndexOf('\n', m.Index) + 1;
                string linePrefix = source.Substring(lineStart, m.Index - lineStart);
                if (linePrefix.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                string rest = source.Substring(m.Index + m.Length, Math.Min(200, source.Length - m.Index - m.Length));
                bool ok = Literal.IsMatch(rest) || LiteralChoice.IsMatch(rest);
                TestHarness.AssertTrue(ok, Path.GetFileName(path) + ": non-literal name at " + m.Value + rest.Split('\n')[0]);
                sites++;
            }
        }
        TestHarness.AssertTrue(sites > 400, "call sites scanned: " + sites);
    }

    private static string Src(string relative) => File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/" + relative));

    // L3: outside a W&L career the per-regiment hooks return before their scope; the
    // Finalizer only when nothing was stored as well.
    private static void WlScopeHooksGateBeforeScope()
    {
        foreach (var (file, scope) in new[]
        {
            ("Patches/WlMoraleScopePatch.cs", "campaign.patch.wl-morale-scope.prefix"),
            ("Patches/WlUnitStatsScopePatch.cs", "campaign.patch.wl-unit-stats-scope.prefix"),
        })
        {
            string src = Src(file);
            int prefix = src.IndexOf("internal static void Prefix(", StringComparison.Ordinal);
            int gate = src.IndexOf("if (!DLC_WL.dlc_scenarioactive) return;", prefix, StringComparison.Ordinal);
            int open = src.IndexOf("TelemetryPerf.Scope(\"" + scope + "\"", prefix, StringComparison.Ordinal);
            TestHarness.AssertTrue(gate > prefix && gate < open, file + ": W&L gate before the scope");
            TestHarness.AssertTrue(src.IndexOf("__state = default;", prefix, StringComparison.Ordinal) < gate, file + ": state cleared before the gate");
            int fin = src.IndexOf("internal static void Finalizer(", StringComparison.Ordinal);
            string head = src.Substring(fin, src.IndexOf("TelemetryPerf.Scope(", fin, StringComparison.Ordinal) - fin);
            TestHarness.AssertTrue(head.Contains("Active") && head.Contains("!DLC_WL.dlc_scenarioactive) return;"), file + ": finalizer returns early only with nothing stored");
        }
    }

    private static void SkirmisherPushCompiledAccessors()
    {
        string src = Src("Patches/SkirmisherScreenDistancePatch.cs");
        TestHarness.AssertFalse(src.Contains(".GetValue(") || src.Contains(".SetValue("), "no per-call reflection");
        string[] order =
        {
            "_setTargetArea(data, pushed);", "_setDistance(data, XZ(start, pushed));", "_setDistanceToWpTarget(data, XZ(block, pushed));",
            "_setDistanceLastWp(data, XZ(unit.GetLastTransmittedPathPos(ignoreorderdelay: true), pushed));",
            "_setOnWater(data, BattlefieldSetup.GetCurrentTerrainOnPos(pushed) == RiverTerrain);", "_setSollDistance(data, _getSollDistance(data) + extra);",
        };
        int at = 0;
        foreach (string write in order)
        {
            int next = src.IndexOf(write, at, StringComparison.Ordinal);
            TestHarness.AssertTrue(next >= at, "write in vanilla's order: " + write);
            at = next;
        }
        foreach (string field in new[] { "\"targetarea\"", "\"rotationtarget\"", "\"distance\"", "\"solldistance\"", "\"onwater\"", "\"distancetowptarget\"", "\"distancelastwp\"" })
            TestHarness.AssertTrue(src.Contains(field), "field " + field);
        TestHarness.AssertTrue(src.Contains("if (!_firstPushLogged)") && src.Contains("BattleScope.Register(\"skirmisher-screen-first-push\""), "first push logged once per battle by a latch");
    }

    private sealed class SetterFixture
    {
        public float F;
        public bool B;
        public UnityEngine.Vector3 V;
    }

    private static void CompiledFieldSetterMatchesReflection()
    {
        var t = typeof(SetterFixture);
        var f = WhiskeyRealism.Patches.CompiledFieldSetter.Build<float>(t.GetField("F"));
        var b = WhiskeyRealism.Patches.CompiledFieldSetter.Build<bool>(t.GetField("B"));
        var v = WhiskeyRealism.Patches.CompiledFieldSetter.Build<UnityEngine.Vector3>(t.GetField("V"));
        var getV = WhiskeyRealism.Patches.CompiledFieldGetter.Build<UnityEngine.Vector3>(t.GetField("V"));
        var a = new SetterFixture();
        var r = new SetterFixture();
        foreach (float value in new[] { 0f, -1.5f, float.NaN, 1e30f })
        {
            f(a, value);
            t.GetField("F").SetValue(r, value);
            TestHarness.AssertTrue(BitConverter.SingleToInt32Bits(a.F) == BitConverter.SingleToInt32Bits(r.F), "float " + value);
        }
        b(a, true);
        TestHarness.AssertTrue(a.B, "bool");
        var vec = new UnityEngine.Vector3(1f, 2f, 3f);
        v(a, vec);
        TestHarness.AssertTrue(getV(a).Equals(vec), "vector round trip");
        TestHarness.AssertTrue(WhiskeyRealism.Patches.CompiledFieldSetter.Build<int>(t.GetField("F")) == null, "type mismatch refused");
        TestHarness.AssertTrue(WhiskeyRealism.Patches.CompiledFieldSetter.Build<float>(null) == null, "missing field refused");
    }

    private static void PostureNavProbeNoAlloc()
    {
        string src = Src("Tactical/Battle/Producers/PostureIntentProducer.cs");
        TestHarness.AssertFalse(src.Contains("new NavMeshPath()") && !src.Contains("_navPath ?? (_navPath = new NavMeshPath())"), "one reused path");
        TestHarness.AssertFalse(src.Contains("= path.corners;"), "no corners array per sample");
        TestHarness.AssertTrue(src.Contains("while (count == _corners.Length)"), "a full buffer is grown and read again");
        TestHarness.AssertFalse(src.Contains("NavMesh.GetAreaFromName(PostureNavAreaCosts.AreaNames[a]);\n                    if (area >= 0)"), "area ids resolved once");
        TestHarness.AssertFalse(src.Contains("return new[]\n            {\n                Sample("), "samples array reused");
    }
}
