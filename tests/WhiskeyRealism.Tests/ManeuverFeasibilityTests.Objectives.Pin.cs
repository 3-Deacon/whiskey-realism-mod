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

    // The allow-list (user decision D39 restated as a list): the vanilla members the objective runtime, the chains' reader and the capture's live read may
    // touch. A member of a vanilla receiver that is not listed is an enemy-state read nobody approved, whatever it is called: the deny-list above names
    // the aggregates we know of, this names the only reads there are.
    static readonly (string receiver, string[] members)[] VanillaReads =
    {
        ("bunits", new[] { "GetSideFromAlliance", "GetVictoryPointsRelation", "sideinformation" }),
        ("own", new[] { "objectivechain" }),
        ("GamePrefs", new[] { "continuousobjectivepoints" }),
        ("GameVars", new[] { "currenttimefromstart" }),
        ("group", new[] { "gameObject" }),
        ("gameObject", new[] { "GetInstanceID" }),
        ("obj", new[] { "owner", "points" }),
        ("chain", new[] { "usedstrategyid", "objectives", "objectivetimetomove", "linegroup_centerunit" }),
    };

    // The members read through an indexer: the side's accrued points of the continuous mode, and a chain's own group.
    static readonly (string indexed, string member, bool whole)[] IndexedReads = { ("sideinformation", "objectivepoints", true), ("chains", "linegroup_centerunit", false) };

    static string FirstUnlistedRead(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string code = line;
            int comment = code.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) code = code.Substring(0, comment);
            foreach ((string receiver, string[] members) in VanillaReads)
            {
                foreach (Match m in Regex.Matches(code, @"\b" + receiver + @"\.(\w+)"))
                    if (!members.Contains(m.Groups[1].Value)) return receiver + "." + m.Groups[1].Value + " in: " + line.Trim();
            }
            foreach ((string indexed, string member, bool whole) in IndexedReads)
            {
                // The side table is read only through its indexer: taking it whole could read any side's fields.
                if (whole)
                    foreach (Match m in Regex.Matches(code, @"\b" + indexed + @"\b"))
                        if (!Regex.IsMatch(code.Substring(m.Index), @"^" + indexed + @"\[[^\]]*\]\." + member + @"\b")) return indexed + " not read as ." + member + " in: " + line.Trim();
                foreach (Match m in Regex.Matches(code, @"\b" + indexed + @"\[[^\]]*\]\.(\w+)"))
                    if (m.Groups[1].Value != member) return indexed + "[...]." + m.Groups[1].Value + " in: " + line.Trim();
            }
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

        // The allow-list scanner fires on a planted read of an enemy field nobody listed, and passes every listed read.
        AssertTrue(FirstUnlistedRead("float f = bunits.sideinformation[1 - side].totalactiveforce;") != null, "the allow-list fires on the enemy's totalactiveforce");
        AssertTrue(FirstUnlistedRead("var ai = bunits.aibattle;") != null, "on an AIBattle read through bunits");
        AssertTrue(FirstUnlistedRead("int m = own.macroai;") != null, "on another member of the side's AIBattle");
        AssertTrue(FirstUnlistedRead("var all = bunits.sideinformation;") != null, "on the side table taken whole");
        AssertTrue(FirstUnlistedRead("var theirs = bunits.sideinformation[1 - side]; float f = theirs.totalactiveforce;") != null, "and on a side record taken into a variable");
        AssertTrue(FirstUnlistedRead("bool c = GamePrefs.aiusefartherobjectivesforscreenstance;") != null, "on another preference");
        AssertTrue(FirstUnlistedRead("float a = obj.alliancepoints[0];") != null, "on an objective's other fields");
        AssertTrue(FirstUnlistedRead("var r = chains[c].reservegroups;") != null, "on another member of a chain");
        AssertTrue(FirstUnlistedRead("float p = bunits.sideinformation[side].objectivepoints;") == null, "the accrued points of the continuous mode are listed");
        AssertTrue(FirstUnlistedRead("float bar = bunits.GetVictoryPointsRelation(side);") == null, "and the displayed bar");
        AssertTrue(FirstUnlistedRead("Regiment g = chains[c].linegroup_centerunit; int id = group.gameObject.GetInstanceID();") == null, "and a chain's own group and its id");
        AssertTrue(FirstUnlistedRead("// bunits.sideinformation[0].totalactiveforce is never read") == null, "a comment is no read");

        string[] files = Bc14oFiles();
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            string read = FirstObjectiveRead(text);
            AssertTrue(read == null, Path.GetFileName(file) + " reads the enemy's hidden state: " + read);
            string literal = FirstMarchLiteral(text);
            AssertTrue(literal == null, Path.GetFileName(file) + " carries a vanilla literal: " + literal);
        }

        // The files that touch vanilla's state read only what the allow-list names.
        foreach (string name in new[] { "ObjectiveRuntime.cs", "ObjectiveChains.cs" })
        {
            string file = files.First(f => Path.GetFileName(f) == name);
            string unlisted = FirstUnlistedRead(File.ReadAllText(file));
            AssertTrue(unlisted == null, name + " reads vanilla state nobody listed: " + unlisted);
        }
        string adapter = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Tactical/Orchestrator/TacticalVisionRuntimeAdapter.cs");
        string live = MethodBody(adapter, "internal static MapObjectiveCapture ReadObjectiveLive(");
        AssertTrue(live != null, "the capture's live read is found");
        string liveUnlisted = FirstUnlistedRead(live);
        AssertTrue(liveUnlisted == null, "ReadObjectiveLive reads vanilla state nobody listed: " + liveUnlisted);
    }
}
