using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static TestHarness;

/// <summary>BC14O FEAS-PIN-O: the objective doctrine reads the enemy's true state only through the displayed victory bar (user decision D39).</summary>
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] ObjectivePinTests() => new (string, Action)[]
    {
        ("bc14o FEAS-PIN-O the BC14O sources read no enemy aggregate but the displayed bar, index no aibattle and carry no vanilla literal", FeasPinO),
    };

    // The enemy's true aggregates (its losses, morale, routs, points) and vanilla's own held-points bookkeeping, which counts inactive objectives and 0.01 each.
    static readonly Regex ObjectiveReads = new Regex(@"totallosses|averagemorale|routedratio|totalpoints|objectivepointsheld|alliancepoints|aibattle\[", RegexOptions.Compiled);

    static string FirstObjectiveRead(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string code = line;
            int comment = code.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) code = code.Substring(0, comment);
            Match m = ObjectiveReads.Match(code);
            if (m.Success) return m.Value + " in: " + line.Trim();
        }
        return null;
    }

    internal static string[] Bc14oFiles()
    {
        string[] relative =
        {
            "src/WhiskeyRealism/Tactical/Battle/Objectives/ObjectiveScoring.cs",
            "src/WhiskeyRealism/Tactical/Battle/Objectives/ObjectiveLatch.cs",
            "src/WhiskeyRealism/Tactical/Battle/Objectives/ObjectiveChains.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/FeasibilityTelemetry.Objectives.cs",
            "src/WhiskeyRealism/Tactical/Battle/Objectives/ObjectiveRuntime.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/ManeuverFeasibility.Objectives.cs",
            "src/WhiskeyRealism/Tactical/Orchestrator/Plays/FeasibilityLedger.Objectives.cs",
        };
        // A file that is not found is a path that moved, and a pin over fewer files than it names says nothing: FindRepoPath throws on a missing one.
        string[] found = relative.Select(TestFixtures.FindRepoPath).ToArray();
        AssertEqual(relative.Length, found.Length, "every BC14O file is found");
        // The directory holds no BC14O file this list does not name.
        string dir = Path.GetDirectoryName(found[0]);
        int named = relative.Count(r => r.Contains("/Battle/Objectives/"));
        AssertEqual(named, Directory.GetFiles(dir, "*.cs").Length, "the Objectives directory holds only the files the pin names");
        return found;
    }

    static void FeasPinO()
    {
        // The scanner fires on a planted read first, so its zero is shown to detect.
        AssertTrue(FirstObjectiveRead("float t = bunits.sideinformation[1].totallosses;") != null, "the scanner fires on a planted totallosses read");
        AssertTrue(FirstObjectiveRead("var m = s.averagemorale;") != null, "and on averagemorale");
        AssertTrue(FirstObjectiveRead("float r = s.routedratio;") != null, "and on routedratio");
        AssertTrue(FirstObjectiveRead("float p = s.totalpoints;") != null, "and on totalpoints");
        AssertTrue(FirstObjectiveRead("float h = s.objectivepointsheld;") != null, "and on objectivepointsheld");
        AssertTrue(FirstObjectiveRead("float a = obj.alliancepoints[0];") != null, "and on alliancepoints");
        AssertTrue(FirstObjectiveRead("var ai = bunits.aibattle[side];") != null, "and on an aibattle index");
        AssertTrue(FirstObjectiveRead("float o = s.objectivepoints;") == null, "the accrued objectivepoints of the continuous-mode shares is not one of them");
        AssertTrue(FirstObjectiveRead("// vanilla's totalpoints are never read") == null, "a comment naming the rule is no read");
        AssertTrue(FirstObjectiveRead("float v = bunits.GetVictoryPointsRelation(side);") == null, "the displayed bar is the one accepted read");

        string[] files = Bc14oFiles();
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            string read = FirstObjectiveRead(text);
            AssertTrue(read == null, Path.GetFileName(file) + " reads the enemy's hidden state: " + read);
            string literal = FirstMarchLiteral(text);
            AssertTrue(literal == null, Path.GetFileName(file) + " carries a vanilla literal: " + literal);
        }
    }
}
