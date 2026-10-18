using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using static TestHarness;
using static TestFixtures;

// BC16 step 0: the 80 % rule that decides whether A4 is built lives in tools/bc16-mirror-verdict.jq, so the user's smoke reading and this harness
// run the same program. The sidecars in tests/WhiskeyRealism.Tests/data/bc16-mirror/ are synthetic TacticalLayoutMirror streams in the
// TelemetryJson schema (c1-c11b built by the verifier of step 0, c12 added here): each pins one edge of decision 9.
internal static partial class FormationSlotTests
{
    private static (string, Action)[] JqTests()
    {
        return new (string, Action)[]
        {
            ("bc16 JQ the verdict program gives the answer decision 9 predicts on every synthetic sidecar", VerdictOnEverySidecar),
            ("bc16 JQ the 80 % edge is inclusive and 0.7 is not enough", EdgeIsEightyPercent),
            ("bc16 JQ the immediate filter separates deployment rows from path rows", ImmediateFilterSeparates),
        };
    }

    private const string Confirmed = "mirror confirmed: build A4";
    private const string NotReproduced = "not reproduced: close BUG-TAC-026";
    private const string FewRows = "inconclusive: fewer than 10 rows at a depth";
    private const string NoPattern = "inconclusive: neither pattern at 80 %";

    private static string DataFile(string name) => FindRepoPath("tests/WhiskeyRealism.Tests/data/bc16-mirror/" + name + ".jsonl");

    // Runs the repo's jq program over the files and returns its JSON result. Fails (never skips) when jq is not installed: a silently skipped
    // test would leave the rule that decides A4 uncovered.
    private static JsonElement RunVerdict(string immediate, params string[] files)
    {
        var info = new ProcessStartInfo("jq") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("-s");
        info.ArgumentList.Add("-c");
        if (immediate != null)
        {
            info.ArgumentList.Add("--arg");
            info.ArgumentList.Add("immediate");
            info.ArgumentList.Add(immediate);
        }
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add(FindRepoPath("tools/bc16-mirror-verdict.jq"));
        foreach (string f in files) info.ArgumentList.Add(DataFile(f));
        Process p;
        try { p = Process.Start(info); }
        catch (System.ComponentModel.Win32Exception) { throw new Exception("jq is not installed: the BC16 verdict program cannot be tested (apt install jq)"); }
        using (p)
        {
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            AssertEqual(0, p.ExitCode, "jq exit code, stderr: " + error);
            return JsonDocument.Parse(output).RootElement.Clone();
        }
    }

    private static string Verdict(string immediate, params string[] files) => RunVerdict(immediate, files).GetProperty("verdict").GetString();

    private static void VerdictOnEverySidecar()
    {
        var expected = new (string[] files, string verdict, string why)[]
        {
            (new[] { "c1" }, Confirmed, "depths 1-2 agree, depth 3 reversed, 10 rows each"),
            (new[] { "c2" }, NotReproduced, "all three depths agree"),
            (new[] { "c3" }, FewRows, "nine rows at depth 3, however clear"),
            (new[] { "c4" }, Confirmed, "exactly 0.8 on both sides"),
            (new[] { "c5" }, NoPattern, "depth 3 reversed at 0.7"),
            (new[] { "c6" }, Confirmed, "the other way round: depths 1-2 reversed, depth 3 agree"),
            (new[] { "c7" }, Confirmed, "flip and lod rows in the stream are ignored"),
            (new[] { "c8" }, FewRows, "nine rows at depth 2"),
            (new[] { "c9" }, NotReproduced, "all three depths reversed"),
            (new[] { "c10" }, NoPattern, "depths 1-2 split half and half"),
            (new[] { "c11b" }, FewRows, "five rows a depth alone"),
            (new[] { "c11a", "c11b" }, Confirmed, "two sidecars together reach 15 rows a depth"),
        };
        foreach (var (files, verdict, why) in expected)
            AssertEqual(verdict, Verdict(null, files), string.Join("+", files) + ": " + why);
    }

    private static void EdgeIsEightyPercent()
    {
        JsonElement c4 = RunVerdict(null, "c4");
        AssertNear(0.8f, (float)c4.GetProperty("agree12").GetDouble(), 1e-6f, "c4 agree12");
        AssertNear(0.8f, (float)c4.GetProperty("reversed3").GetDouble(), 1e-6f, "c4 reversed3");
        AssertNear(0.7f, (float)RunVerdict(null, "c5").GetProperty("reversed3").GetDouble(), 1e-6f, "c5 reversed3");
        AssertEqual(NoPattern, Verdict(null, "c5"), "0.7 is below the rule");
    }

    private static void ImmediateFilterSeparates()
    {
        // c12: the deployment rows show the mirror, the path rows show none.
        AssertEqual(NoPattern, Verdict(null, "c12"), "all rows together cancel at depth 3");
        AssertEqual(Confirmed, Verdict("true", "c12"), "deployment rows alone");
        AssertEqual(NotReproduced, Verdict("false", "c12"), "path rows alone");
        AssertEqual(FewRows, Verdict("false", "c1"), "a stream with no path rows has none to read");
    }
}
