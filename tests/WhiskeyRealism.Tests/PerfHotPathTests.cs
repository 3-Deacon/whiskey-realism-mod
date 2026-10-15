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
}
