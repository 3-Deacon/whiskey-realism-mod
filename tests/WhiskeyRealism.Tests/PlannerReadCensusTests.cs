using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using static TestHarness;
using static TestFixtures;

// Stage G slice G7 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G7, "Read census"): the stage's contract
// members and flags each have a named reader, and the harness proves it.
//
// Why a test and not orphan-sweep: orphan-sweep counts a write as a reference, so a member that is only ever assigned
// passes it. This census asks for a READ: a line of a named reader file, not a comment, that names `.Member` on a receiver of
// the member's own type and is not a plain assignment to it (`.Member = ...`; `==`, `+=`, `|=` and passing it on all count as
// reads). A member with no such line in any file its row names fails the run; the fix is to delete the member or to name a
// real reader, never to list a member whose row cannot pass.
//
// What "a receiver of its own type" means (CensusReceiverTypes, verify G7 F2): an enum value must be read as `Type.Value`;
// any other member must be read through the type's name or through a variable, field, parameter, foreach variable, `out var`,
// lambda parameter or `var` local that the file declares (or infers from a declared return type) as that type, or through a
// member or method declared to return it. There is no compiler here, so the limit is stated: the check works from declarations
// in the source, it treats one identifier name as one type across a file (it can over-accept a reused name), and a receiver it
// cannot resolve (the result of an undeclared call chain) is not credited, so such a row names a reader that does resolve.
// `CensusReadsAreTypeAware` pins it, including the probe that re-pointed `CrossingRecord.X` at a file reading only `AreaRecord.X`.
//
// Scope, parsed from source so a member added later without a row turns the harness red: every instance field, auto-property
// and enum value that is `public` or `internal` in
//   - CampaignPlannerContracts.cs (every type) and CampaignSnapshot.cs (CampaignSnapshot only; its static helper classes
//     hold methods, which orphan-sweep covers),
//   - the state records the later slices added and their as-built notes ask G7 to census: PlannerState, ReconMission and
//     ReconState, the depot task records, MoveRecord and GaveUpMove, ExecutorContext,
// plus the stage's config flags (the "Campaign Planner" section and the W&L career transfer fix).
// Methods are not data and are not rows. An enum value's reader is any line that names it outside its declaration: for the
// PlayerCommandClass echelon values the consumer is the classifier and the `player=` telemetry the smoke checklist reads.
//
// Reader files are given relative to src/WhiskeyRealism/ with three shorthands: P/ = Strategic/Planner/, S/ = Strategic/,
// X/ = Patches/. A row lists every file the harness checks; each listed file must read the member. The declaring files are
// never readers. The third column names the slice and method that consumes the member (the plan's "Read by" column).
internal static class PlannerReadCensusTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("CensusReadMatchIgnoresWritesAndComments", CensusReadMatchIgnoresWritesAndComments),
            ("CensusReadsAreTypeAware", CensusReadsAreTypeAware),
            ("CensusParserSeesDataMembersOnly", CensusParserSeesDataMembersOnly),
            ("CensusNamesEveryPlannerMember", CensusNamesEveryPlannerMember),
            ("CensusHasNoStaleOrDuplicateRow", CensusHasNoStaleOrDuplicateRow),
            ("CensusRowsNameTheirConsumer", CensusRowsNameTheirConsumer),
            ("EveryCensusReaderReadsItsMember", EveryCensusReaderReadsItsMember),
            ("CensusNamesEveryPlannerFlag", CensusNamesEveryPlannerFlag),
            ("EveryCensusFlagIsReadByItsConsumer", EveryCensusFlagIsReadByItsConsumer),
        });
    }

    // ---- the read test ----

    // A read of `member`: a code line (not a comment) with `.member` that is not the left side of a plain assignment.
    internal static bool Reads(string text, string member)
    {
        var rx = new Regex(@"\." + Regex.Escape(member) + @"\b(?!\s*=[^=])");
        foreach (string line in CodeLines(text))
            if (rx.IsMatch(line)) return true;
        return false;
    }

    // A read of the flag's config field: `Plugin.<Field>` on a code line.
    internal static bool ReadsFlag(string text, string field)
    {
        var rx = new Regex(@"\bPlugin\." + Regex.Escape(field) + @"\b");
        foreach (string line in CodeLines(text))
            if (rx.IsMatch(line)) return true;
        return false;
    }

    // The code of a source file line by line: line comments, block comments and blank lines removed.
    internal static IEnumerable<string> CodeLines(string text)
    {
        bool inBlock = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw;
            if (inBlock)
            {
                int end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) continue;
                line = line.Substring(end + 2);
                inBlock = false;
            }
            line = Regex.Replace(line, @"/\*.*?\*/", " ");
            int open = line.IndexOf("/*", StringComparison.Ordinal);
            if (open >= 0) { inBlock = true; line = line.Substring(0, open); }
            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0) line = line.Substring(0, slashes);
            if (line.Trim().Length > 0) yield return line;
        }
    }

    private static void CensusReadMatchIgnoresWritesAndComments()
    {
        AssertTrue(Reads("var v = a.Strength;", "Strength"), "a read on the right of an assignment");
        AssertTrue(Reads("if (a.Strength == 0f) return;", "Strength"), "a comparison is a read");
        AssertTrue(Reads("a.Strength += 1f;", "Strength"), "a compound assignment reads the member");
        AssertTrue(Reads("a.Flags |= FormationFlags.InBattle;", "Flags"), "so does an or-assignment");
        AssertTrue(Reads("Use(a.Strength);", "Strength"), "passing it on is a read");
        AssertTrue(Reads("b.Strength = a.Strength;", "Strength"), "a write whose right side reads it");
        AssertFalse(Reads("a.Strength = 5f;", "Strength"), "a plain assignment is a write");
        AssertFalse(Reads("a.Strength=5f;", "Strength"), "with no space too");
        AssertFalse(Reads("// a.Strength", "Strength"), "a line comment is not code");
        AssertFalse(Reads("var v = 1; // a.Strength", "Strength"), "nor a trailing comment");
        AssertFalse(Reads("/* a.Strength */ var v = 1;", "Strength"), "nor a block comment");
        AssertFalse(Reads("/*\n a.Strength\n*/\nvar v = 1;", "Strength"), "nor a multi-line block comment");
        AssertFalse(Reads("var v = a.StrengthMax;", "Strength"), "a longer name is another member");
        AssertFalse(Reads("var v = Strength;", "Strength"), "a bare name is not a member access");
        AssertTrue(ReadsFlag("if (Plugin.EnableX != null) return;", "EnableX"), "a flag read");
        AssertFalse(ReadsFlag("// Plugin.EnableX", "EnableX"), "a commented flag read is not one");
        AssertFalse(ReadsFlag("var v = Plugin.EnableXY;", "EnableX"), "a longer flag name is another flag");
    }

    // F2 (verify G7, probe P12): `.X` of another type must not satisfy a row. The reader has to name the type or reach the
    // member through a variable the file declares as that type (CensusReceiverTypes).
    private static void CensusReadsAreTypeAware()
    {
        const string areas = "List<AreaRecord> _areas = new List<AreaRecord>();\nfor (int a = 0; a < _areas.Count; a++) { float x = _areas[a].X; }\nforeach (var area in _areas) Use(area.Z);";
        AssertTrue(CensusReceiverTypes.ReadsMember(areas, "t1", "AreaRecord", "X"), "an indexed element of a List<AreaRecord> is an AreaRecord");
        AssertTrue(CensusReceiverTypes.ReadsMember(areas, "t1", "AreaRecord", "Z"), "so is a foreach variable over it");
        AssertFalse(CensusReceiverTypes.ReadsMember(areas, "t1", "CrossingRecord", "X"), "but it is not a CrossingRecord");
        AssertFalse(CensusReceiverTypes.ReadsMember(areas, "t1", "CrossingRecord", "Z"), "whatever the member name");

        const string crossing = "CrossingRecord c = crossings[0];\nfloat dx = c.X - other.X;";
        AssertTrue(CensusReceiverTypes.ReadsMember(crossing, "t2", "CrossingRecord", "X"), "a declared CrossingRecord variable");
        AssertFalse(CensusReceiverTypes.ReadsMember(crossing, "t2", "AreaRecord", "X"), "is not an AreaRecord");
        AssertFalse(CensusReceiverTypes.ReadsMember("float v = thing.X;", "t3", "CrossingRecord", "X"), "an undeclared receiver is not credited to any type");
        AssertFalse(CensusReceiverTypes.ReadsMember("CrossingRecord c = d;\nc.X = 4f;", "t4", "CrossingRecord", "X"), "a plain assignment is still a write");
        AssertTrue(CensusReceiverTypes.ReadsMember("var v = CrossingRecord.X;", "t5", "CrossingRecord", "X"), "the type named in the access");

        AssertTrue(CensusReceiverTypes.ReadsEnumValue("if (role == FormationRole.Hold) return;", "FormationRole", "Hold"), "an enum value is read qualified");
        AssertFalse(CensusReceiverTypes.ReadsEnumValue("if (posture == TheaterPosture.Hold) return;", "FormationRole", "Hold"), "another enum's value of the same name is not it");
        AssertFalse(CensusReceiverTypes.ReadsEnumValue("// FormationRole.Hold", "FormationRole", "Hold"), "a comment is not a read");

        // The verifier's probe, on the real files: SectorModel.cs reads AreaRecord's X and Z but no CrossingRecord's.
        string sector = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/SectorModel.cs");
        AssertTrue(CensusReceiverTypes.ReadsMember(sector, "SectorModel.cs", "AreaRecord", "X"), "SectorModel reads area X");
        AssertFalse(CensusReceiverTypes.ReadsMember(sector, "SectorModel.cs", "CrossingRecord", "X"), "and no crossing X: a CrossingRecord.X row naming it fails");
        string river = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/RiverLines.cs");
        AssertTrue(CensusReceiverTypes.ReadsMember(river, "RiverLines.cs", "CrossingRecord", "X"), "RiverLines reads the crossing X its row names");
    }

    // ---- the parser ----

    private static readonly (string path, string[] types)[] Scope =
    {
        ("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerContracts.cs", null),
        ("src/WhiskeyRealism/Strategic/Planner/CampaignSnapshot.cs", new[] { "CampaignSnapshot" }),
        ("src/WhiskeyRealism/Strategic/Planner/PlannerState.cs", new[] { "PlannerState" }),
        ("src/WhiskeyRealism/Strategic/Planner/ReconMissionLedger.cs", new[] { "ReconMission", "ReconState" }),
        ("src/WhiskeyRealism/Strategic/Planner/DepotTaskLedger.cs", new[] { "DepotTaskRecord", "DepotTaskState", "DepotWishMoment", "DepotTaskTransition", "DepotWishFacts", "HeldBuilder" }),
        ("src/WhiskeyRealism/Strategic/Planner/MoveTracker.cs", new[] { "MoveRecord", "GaveUpMove" }),
        ("src/WhiskeyRealism/Strategic/Planner/CampaignWithholdView.cs", new[] { "ExecutorContext" }),
    };

    private static readonly Regex TypeDecl = new Regex(@"^(?:public|internal|private|protected)?\s*(?:(?:sealed|static|abstract|readonly|partial)\s+)*(enum|struct|class)\s+(\w+)");
    private static readonly Regex FieldDecl = new Regex(@"^(?:public|internal)\s+(?!static\b|const\b)(?:readonly\s+)?(?<decl>[^=;{(]+?)\s*(?<term>=>|[=;{(])");
    private static readonly Regex EnumValue = new Regex(@"^(\w+)\s*(?:=[^,]*)?,?\s*$");

    // "Type.Member" for every data member in the census scope.
    internal static List<string> ParseScope()
    {
        var keys = new List<string>();
        foreach (var (path, types) in Scope)
            keys.AddRange(ParseMembers(ReadRepoFile(path), types));
        return keys;
    }

    internal static List<string> ParseMembers(string text, string[] only)
    {
        var keys = new List<string>();
        var stack = new Stack<(string name, string kind, int depth)>();
        int depth = 0;
        string pendingName = null, pendingKind = null;
        foreach (string line in CodeLines(text))
        {
            string t = line.Trim();
            var type = TypeDecl.Match(t);
            if (type.Success)
            {
                pendingKind = type.Groups[1].Value;
                pendingName = type.Groups[2].Value;
                // An enum written on one line: `public enum S { A = 0, B = 1 }`.
                int open = t.IndexOf('{'), close = t.LastIndexOf('}');
                if (pendingKind == "enum" && open >= 0 && close > open && (only == null || only.Contains(pendingName)))
                    foreach (string item in t.Substring(open + 1, close - open - 1).Split(','))
                    {
                        var value = EnumValue.Match(item.Trim());
                        if (value.Success) keys.Add(pendingName + "." + value.Groups[1].Value);
                    }
            }
            else if (stack.Count > 0 && depth == stack.Peek().depth && (only == null || only.Contains(stack.Peek().name)))
            {
                var top = stack.Peek();
                if (top.kind == "enum")
                {
                    var value = EnumValue.Match(t);
                    if (value.Success) keys.Add(top.name + "." + value.Groups[1].Value);
                }
                else
                {
                    var field = FieldDecl.Match(t);
                    if (field.Success && field.Groups["term"].Value != "(" && field.Groups["term"].Value != "=>")
                        foreach (string name in DeclaredNames(field.Groups["decl"].Value))
                            keys.Add(top.name + "." + name);
                }
            }
            foreach (char c in t)
            {
                if (c == '{')
                {
                    depth++;
                    if (pendingName != null) { stack.Push((pendingName, pendingKind, depth)); pendingName = null; }
                }
                else if (c == '}')
                {
                    if (stack.Count > 0 && stack.Peek().depth == depth) stack.Pop();
                    depth--;
                }
            }
        }
        return keys;
    }

    // `float X, Z, WaypointX` -> X, Z, WaypointX; `List<FormationRecord> Own` -> Own.
    private static IEnumerable<string> DeclaredNames(string decl)
    {
        string flat = decl;
        string previous;
        do { previous = flat; flat = Regex.Replace(flat, @"<[^<>]*>", ""); } while (flat != previous);
        foreach (string segment in flat.Split(','))
        {
            var words = segment.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0) yield return words[words.Length - 1].Trim('[', ']', '?');
        }
    }

    private static void CensusParserSeesDataMembersOnly()
    {
        const string sample = @"
namespace N {
    [System.Flags]
    public enum Role { None = 0, A = 1, B = 2 }
    public struct Rec
    {
        public int Id;
        public float X, Z;          // two names on one line
        internal Hidden Chain;
        public List<Foo> Items { get; } = new List<Foo>();
        public int Count { get; private set; }
        public const int Max = 4;
        public static int Shared;
        public int Method(int a) { return a; }
        internal void Reset(int a) { Id = a; }
        public int Computed => Id + 1;
        private int hidden;
    }
    internal static class Helpers { public static int One; internal static int Two(int a) { return a; } }
}";
        var all = ParseMembers(sample, null);
        AssertTrue(all.Contains("Role.None") && all.Contains("Role.A") && all.Contains("Role.B"), "enum values, including the zero one");
        AssertTrue(all.Contains("Rec.Id") && all.Contains("Rec.X") && all.Contains("Rec.Z"), "fields, a name list split");
        AssertTrue(all.Contains("Rec.Chain") && all.Contains("Rec.Items") && all.Contains("Rec.Count"), "internal fields and auto-properties");
        AssertFalse(all.Contains("Rec.Max") || all.Contains("Rec.Shared"), "constants and statics are not state");
        AssertFalse(all.Contains("Rec.Method") || all.Contains("Rec.Reset"), "methods are not rows");
        AssertFalse(all.Contains("Rec.hidden"), "private members are not contract");
        AssertFalse(all.Any(k => k.StartsWith("Helpers.")), "static helper classes hold no data");
        var only = ParseMembers(sample, new[] { "Rec" });
        AssertFalse(only.Any(k => k.StartsWith("Role.")), "a type filter keeps only the named types");
        AssertTrue(ParseScope().Count > 150, "the real scope parses to a few hundred members: " + ParseScope().Count);
        var real = ParseScope();
        AssertTrue(real.Contains("FormationRecord.WaypointZ") && real.Contains("CampaignSnapshot.Beliefs") && real.Contains("FormationFlags.MoveQueued"),
            "the real contract files are read through the same parser");
        AssertTrue(real.Contains("PlannerState.TempoReleasedDecision") && real.Contains("MoveRecord.ObjectiveId"),
            "and so are the state records");
    }

    // The enum types in the census scope: their values are read as `Type.Value`, never as a bare `.Value`.
    internal static HashSet<string> EnumTypes()
    {
        var enums = new HashSet<string>();
        foreach (var (path, types) in Scope)
            foreach (Match m in Regex.Matches(string.Join("\n", CodeLines(ReadRepoFile(path))), @"\benum\s+(\w+)"))
                enums.Add(m.Groups[1].Value);
        return enums;
    }

    // ---- the table ----

    private static readonly Dictionary<string, string> Shorthand = new Dictionary<string, string>
    {
        { "P/", "Strategic/Planner/" }, { "S/", "Strategic/" }, { "X/", "Patches/" }
    };

    private sealed class Row
    {
        public string Key;
        public List<string> Files = new List<string>();
        public string Consumer;
    }

    private static List<Row> Table()
    {
        var rows = new List<Row>();
        foreach (string text in CensusRows.All)
        {
            string[] parts = text.Split('|');
            if (parts.Length != 3) throw new Exception("a census row has three columns (member | reader files | consumer): " + text);
            var row = new Row { Key = parts[0].Trim(), Consumer = parts[2].Trim() };
            foreach (string file in parts[1].Split(','))
            {
                string f = file.Trim();
                foreach (var kv in Shorthand)
                    if (f.StartsWith(kv.Key, StringComparison.Ordinal)) { f = kv.Value + f.Substring(kv.Key.Length); break; }
                row.Files.Add("src/WhiskeyRealism/" + f);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static void CensusNamesEveryPlannerMember()
    {
        var named = new HashSet<string>(Table().Select(r => r.Key));
        var missing = ParseScope().Where(k => !named.Contains(k)).ToList();
        AssertTrue(missing.Count == 0,
            missing.Count + " planner member(s) have no census row; name a reader or delete the member: " + string.Join(", ", missing));
    }

    private static void CensusHasNoStaleOrDuplicateRow()
    {
        var parsed = new HashSet<string>(ParseScope());
        var seen = new HashSet<string>();
        var problems = new List<string>();
        foreach (var row in Table())
        {
            if (row.Key.StartsWith("Flag:", StringComparison.Ordinal)) continue;
            if (!seen.Add(row.Key)) problems.Add("duplicate " + row.Key);
            if (!parsed.Contains(row.Key)) problems.Add("stale " + row.Key);
        }
        AssertTrue(problems.Count == 0, "census rows that name no member: " + string.Join(", ", problems));
    }

    private static void CensusRowsNameTheirConsumer()
    {
        var problems = new List<string>();
        foreach (var row in Table())
        {
            if (row.Files.Count == 0) problems.Add(row.Key + " names no reader file");
            if (row.Consumer.Length < 6) problems.Add(row.Key + " names no consuming slice and method");
            foreach (string file in row.Files)
                if (file.EndsWith("CampaignPlannerContracts.cs", StringComparison.Ordinal) || file.EndsWith("CampaignSnapshot.cs", StringComparison.Ordinal))
                    problems.Add(row.Key + " names its own declaring file " + file + " as a reader");
        }
        AssertTrue(problems.Count == 0, string.Join("; ", problems));
    }

    private static void EveryCensusReaderReadsItsMember()
    {
        var cache = new Dictionary<string, string>();
        var problems = new List<string>();
        foreach (var row in Table())
        {
            if (row.Key.StartsWith("Flag:", StringComparison.Ordinal)) continue;
            string member = row.Key.Substring(row.Key.IndexOf('.') + 1);
            foreach (string file in row.Files)
            {
                if (!cache.TryGetValue(file, out string text))
                {
                    string path;
                    try { path = FindRepoPath(file); } catch (FileNotFoundException) { problems.Add(row.Key + ": " + file + " does not exist"); continue; }
                    cache[file] = text = File.ReadAllText(path);
                }
                string type = row.Key.Substring(0, row.Key.IndexOf('.'));
                bool read = EnumTypes().Contains(type)
                    ? CensusReceiverTypes.ReadsEnumValue(text, type, member)
                    : CensusReceiverTypes.ReadsMember(text, file, type, member);
                if (!read) problems.Add(row.Key + ": no read of " + type + "." + member + " in " + file);
            }
        }
        AssertTrue(problems.Count == 0, problems.Count + " census reader(s) do not read their member (delete the member or name a real reader): " + string.Join("; ", problems));
    }

    // ---- the flags ----

    private static List<(string field, string name)> ParseFlags()
    {
        string plugin = ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        var flags = new List<(string, string)>();
        foreach (Match m in Regex.Matches(plugin, @"(\w+)\s*=\s*Config\.Bind\(\s*""Campaign Planner""\s*,\s*""([^""]+)"""))
            flags.Add((m.Groups[1].Value, m.Groups[2].Value));
        var transfer = Regex.Match(plugin, @"(\w+)\s*=\s*Config\.Bind\(\s*""W&L""\s*,\s*""(Enable W&L Career Transfer Fix)""");
        AssertTrue(transfer.Success, "the W&L career transfer fix flag is bound");
        flags.Add((transfer.Groups[1].Value, transfer.Groups[2].Value));
        return flags;
    }

    private static void CensusNamesEveryPlannerFlag()
    {
        var named = new HashSet<string>(Table().Where(r => r.Key.StartsWith("Flag:", StringComparison.Ordinal)).Select(r => r.Key.Substring("Flag:".Length)));
        var flags = ParseFlags();
        AssertTrue(flags.Count >= 9, "the nine stage G flags are found in Plugin.cs, found " + flags.Count);
        var missing = flags.Where(f => !named.Contains(f.field)).Select(f => f.field + " (" + f.name + ")").ToList();
        AssertTrue(missing.Count == 0, "planner flag(s) with no census row: " + string.Join(", ", missing));
        var stale = named.Where(n => !flags.Any(f => f.field == n)).ToList();
        AssertTrue(stale.Count == 0, "flag rows that name no [Campaign Planner] flag: " + string.Join(", ", stale));
    }

    private static void EveryCensusFlagIsReadByItsConsumer()
    {
        var problems = new List<string>();
        foreach (var row in Table().Where(r => r.Key.StartsWith("Flag:", StringComparison.Ordinal)))
        {
            string field = row.Key.Substring("Flag:".Length);
            foreach (string file in row.Files)
            {
                string text = ReadRepoFile(file);
                if (!ReadsFlag(text, field)) problems.Add(field + ": no read of Plugin." + field + " in " + file);
            }
        }
        AssertTrue(problems.Count == 0, string.Join("; ", problems));
    }
}

// The census table (plan § G7), one string per member: `Type.Member | reader files | slice: method that consumes it`.
internal static class CensusRows
{
    internal static readonly string[] All =
    {
        "PlayerCommandClass.NotCareer | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (W&L rules apply only in a career); telemetry player=",
        "PlayerCommandClass.NoCommand | P/PlayerCommandClassifier.cs | G0 PlayerCommandClassifier.ExecutorsPaused; telemetry player=",
        "PlayerCommandClass.CommanderInChief | P/PlayerCommandClassifier.cs, P/ForceAllocator.cs | G0 PlannerStandsDown; G4 ForceAllocator.Allocate (CIC player: empty allocation)",
        "PlayerCommandClass.MapGroup | P/PlayerCommandClassifier.cs | G0 PlayerCommandClassifier.Classify; telemetry player= (smoke Sessions B-D read it)",
        "PlayerCommandClass.BelowMapLevel | P/PlayerCommandClassifier.cs | G0 PlayerCommandClassifier.Classify; telemetry player= (smoke Sessions B-D read it)",
        "PlayerCommandClass.ArmyGroupCommander | P/PlayerCommandClassifier.cs | G0 PlayerCommandClassifier.Classify; telemetry player= (smoke Sessions B-D read it)",
        "PlannerReviewMode.Full | P/CampaignPlannerRuntime.cs | G0 CampaignPlannerRuntime.ReviewPlan / ReviewExecute (the one mode decision)",
        "PlannerReviewMode.Bookkeeping | P/CampaignPlannerRuntime.cs | G0 CampaignPlannerRuntime.ReviewPlan / ReviewExecute (the one mode decision)",
        "PlannerReviewMode.StandDown | P/CampaignPlannerRuntime.cs | G0 CampaignPlannerRuntime.ReviewPlan (CIC player's alliance keeps detection only)",
        "FormationFlags.InBattle | P/ReconPlanner.cs, P/TheaterPlanner.cs, P/ReconMissionLedger.cs | G4 busy rule (TheaterPlanner.Busy); G1b ReconPlanner busy set; ReconMissionLedger.OnReview",
        "FormationFlags.OnRetreat | P/ReconPlanner.cs, P/TheaterPlanner.cs, P/ReconMissionLedger.cs | G4 busy rule; G1b ReconPlanner busy set; ReconMissionLedger.OnReview (retreat)",
        "FormationFlags.InOffensiveOps | P/CampaignMover.cs, P/ReconPlanner.cs | G5a CampaignMover Recover relief (removal from vanilla's offensive list); G1b ReconPlanner busy set",
        "FormationFlags.InDefensiveOps | P/TheaterPlanner.cs, P/ReconPlanner.cs | G3/G4 TheaterPlanner.Busy mask; G1b ReconPlanner busy set",
        "FormationFlags.CapitalDefense | P/ReconPlanner.cs | G1b ReconPlanner busy set (already pledged)",
        "FormationFlags.VanillaDepotBuilder | P/TheaterPlanner.cs, P/DepotTaskLedger.cs | G4 TheaterPlanner.Busy; G5c DepotTaskLedger.WishMoment (Adopt)",
        "FormationFlags.FortBuilder | P/TheaterPlanner.cs, P/ReconPlanner.cs | G4 TheaterPlanner.Busy; G1b ReconPlanner busy set",
        "FormationFlags.SeaInvasion | P/TheaterPlanner.cs, P/ReconPlanner.cs | G4 TheaterPlanner.Busy; G1b ReconPlanner busy set",
        "FormationFlags.RaidForce | P/TheaterPlanner.cs, P/ReconPlanner.cs | G4 TheaterPlanner.Busy; G1b ReconPlanner busy set",
        "FormationFlags.InTransfer | P/TheaterPlanner.cs, P/ReinforcementRouting.cs | G4 TheaterPlanner.Busy; G5b ReinforcementRouting.IsProtectedFromDonation",
        "FormationFlags.Embarked | P/TheaterPlanner.cs, P/ReconPlanner.cs | G4 Busy rule (TheaterPlanner busy mask); G1b busy set (ReconPlanner)",
        "FormationFlags.FightingForce | P/TheaterPlanner.cs, P/ForceAllocator.cs, P/ReconPlanner.cs | G4 CapitalGuard and DepotBuilder picks; G1b ReconPlanner.PickFormation",
        "FormationFlags.HasPath | P/ReconMissionLedger.cs | G1b ReconMissionLedger.OnReview (Moving); MoveQueued also G5a MoveTracker.ShouldIssue via CampaignMover",
        "FormationFlags.MoveQueued | P/ReconMissionLedger.cs | G1b ReconMissionLedger.OnReview (Moving); MoveQueued also G5a MoveTracker.ShouldIssue via CampaignMover",
        "FormationRecord.InstanceId | P/ForceAllocator.cs, P/CampaignMover.cs, P/ReconExecutor.cs | every executor (live lookup)",
        "FormationRecord.UnitKey | P/PlannerSnapshot.cs | G6 sidecar rebinding (PlannerRebind, IndexOfKey)",
        "FormationRecord.UnitTyp | P/CommanderAppointmentPlanner.cs, P/MarchTime.cs | G2 TryPlan target commands; G3 MarchTime.EstimateDays",
        "FormationRecord.X | P/TheaterPlanner.cs, P/ForceAllocator.cs, P/ReconPlanner.cs | G3 TheaterPlanner.Plan, G4 ForceAllocator.Allocate, G1b ReconPlanner, G5b, G8",
        "FormationRecord.Z | P/TheaterPlanner.cs, P/ForceAllocator.cs, P/ReconPlanner.cs | G3 TheaterPlanner.Plan, G4 ForceAllocator.Allocate, G1b ReconPlanner, G5b, G8",
        "FormationRecord.WaypointX | P/CampaignMover.cs, P/ReserveRules.cs | G5a MoveTracker.Judge via CampaignMover; G4 ReserveRules.HoldPoint",
        "FormationRecord.WaypointZ | P/CampaignMover.cs, P/ReserveRules.cs | G5a MoveTracker.Judge via CampaignMover; G4 ReserveRules.HoldPoint",
        "FormationRecord.Strength | P/TheaterPlanner.cs, P/ForceAllocator.cs | G3 TheaterPlanner.Plan, G4 ForceAllocator.Allocate",
        "FormationRecord.Morale | P/TheaterPlanner.cs, P/CapitalFloorPolicy.cs | G3 Recover posture; G5b CapitalFloorPolicy.VanillaNeed (str x morale)",
        "FormationRecord.Supply01 | P/TheaterPlanner.cs, P/ReconMissionLedger.cs | G3 Recover posture; G1b ReconMissionLedger.OnReview (low-supply recall)",
        "FormationRecord.ReadinessStep | P/ReconPlanner.cs | G1b ReconPlanner.PickFormation (movement minima)",
        "FormationRecord.CommandRange | P/TheaterPlanner.cs, P/ReconPlanner.cs | G3/G0 LogisticsGate.Evaluate callers (reach per formation)",
        "FormationRecord.BugleRange | P/ReconMissionLedger.cs, P/ReconPlanner.cs, P/DepotSteeringRuntime.cs | G1b ReconMissionLedger.OnReview (contact, arrival), ReconPlanner probe contact; G5c DepotTaskLedger.ShouldWish",
        "FormationRecord.CommanderId | P/CommanderAppointmentPlanner.cs, P/TheaterPlanner.cs | G3 sector profile lookup; G2 CommanderAppointmentPlanner.TryPlan (sitting commander)",
        "FormationRecord.HasTheater | P/SteeringRules.cs | G5a theater-box writer (write only when it differs)",
        "FormationRecord.TheaterX | P/SteeringRules.cs | G5a theater-box writer (write only when it differs)",
        "FormationRecord.TheaterZ | P/SteeringRules.cs | G5a theater-box writer (write only when it differs)",
        "FormationRecord.AreaIndex | P/ReserveRules.cs, P/ReconPlanner.cs, P/CommanderAppointmentPlanner.cs | G0 SectorModel.SectorOfArea callers (G3, G4, G1b; G2 main-effort targets)",
        "FormationRecord.Flags | P/TheaterPlanner.cs, P/ForceAllocator.cs, P/ReconPlanner.cs, P/CampaignMover.cs | G3/G4 busy rule, G1b busy set, G5a MoveQueued test",
        "FormationRecord.Name | P/ReconExecutor.cs | G5a [CampaignMover], G5c [DepotTask], G5d [ReconMission] rows",
        "FormationRecord.Chain | P/ForceAllocator.cs, P/CampaignMover.cs, P/ReconPlanner.cs, P/TheaterPlanner.cs, P/ReserveRules.cs | G4 busy and player rules; G5a mover; G1b recon (all via WlChainRules)",
        "FormationRecord.CavalryShare | P/ReconPlanner.cs | G1b ReconPlanner.PickFormation / standoff (SightRadius also G1 viewer coverage via the snapshot)",
        "FormationRecord.SightRadius | P/ReconPlanner.cs | G1b ReconPlanner.PickFormation / standoff (SightRadius also G1 viewer coverage via the snapshot)",
        "FormationRecord.ParentInstanceId | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (ancestors of the player's command)",
        "FormationRecord.UseRails | P/MarchTime.cs, P/MoveIssue.cs, P/CampaignPlannerRuntime.cs | G3 MarchTime.EstimateDays (rail and river legs); G5a MoveIssue transport default; G0 first-review line",
        "FormationRecord.UseRivers | P/MarchTime.cs, P/MoveIssue.cs, P/CampaignPlannerRuntime.cs | G3 MarchTime.EstimateDays (rail and river legs); G5a MoveIssue transport default; G0 first-review line",
        "FormationRecord.GroupSpeed | P/MarchTime.cs, P/TheaterPlanner.cs | G3 MarchTime.EstimateDays (SpeedPerDay)",
        "FormationRecord.TimeOfNotMoving | P/CampaignMover.cs | G5a CampaignMover.EvaluateLaunch (the tempo roll's idle term)",
        "AreaRecord.Index | P/SectorModel.cs, P/TheaterPlanner.cs | G0 SectorModel.Build (adjacency, 14835-14860); G3 TheaterPlanner value term",
        "AreaRecord.X | P/SectorModel.cs | G0 SectorModel.Build (adjacency, 14835-14860; the centroid and, from F6b-1, the sector radius TheaterPlanner.BuildSectorFacts reads)",
        "AreaRecord.Z | P/SectorModel.cs | G0 SectorModel.Build (adjacency, 14835-14860; the centroid and, from F6b-1, the sector radius TheaterPlanner.BuildSectorFacts reads)",
        "AreaRecord.EdgeX | P/SectorModel.cs | G0 SectorModel.Build (zone edges)",
        "AreaRecord.EdgeZ | P/SectorModel.cs | G0 SectorModel.Build (zone edges)",
        "AreaRecord.Importance | P/TheaterPlanner.cs | G3 TheaterPlanner value term",
        "AreaRecord.OwnPoints | P/LogisticsGate.cs | G0 LogisticsGate.Evaluate (stacking check)",
        "AreaRecord.SupplyCapacity | P/LogisticsGate.cs | G0 LogisticsGate.Evaluate (stacking check)",
        "ObjectiveRole.None | P/ReconPlanner.cs | G1b ReconPlanner approach targets (an objective with no role and low importance is not an approach)",
        "ObjectiveRole.Capital | P/TheaterPlanner.cs | G3 TheaterPlanner value term (role)",
        "ObjectiveRole.RailHub | P/TheaterPlanner.cs | G3 TheaterPlanner value term (role)",
        "ObjectiveRole.Port | P/TheaterPlanner.cs | G3 TheaterPlanner value term (role)",
        "ObjectiveRole.Industry | P/TheaterPlanner.cs | G3 TheaterPlanner value term (role)",
        "ObjectiveRecord.ObjectiveId | P/TheaterPlanner.cs, P/CampaignSteeringRuntime.cs | G3 candidates, triggers, value; G5a objective writer",
        "ObjectiveRecord.X | P/TheaterPlanner.cs, P/ReconPlanner.cs | G3 candidates; G1b approaches, standoff anchors and pickets",
        "ObjectiveRecord.Z | P/TheaterPlanner.cs, P/ReconPlanner.cs | G3 candidates; G1b approaches, standoff anchors and pickets",
        "ObjectiveRecord.HolderAlliance | P/ReconPlanner.cs, P/EnemyDetectionRuntime.cs, P/TheaterPlanner.cs | G1 ObjectiveGarrison detections; G1b approaches; G3 candidates",
        "ObjectiveRecord.Available | P/TheaterPlanner.cs | G3 candidates and triggers",
        "ObjectiveRecord.Accomplished | P/TheaterPlanner.cs | G3 candidates and triggers",
        "ObjectiveRecord.Roles | P/TheaterPlanner.cs, P/ReconPlanner.cs, P/GarrisonPlanner.cs | G3 value term; G1b pickets; G8 GarrisonPlanner.CollectSites (capital, port, rail hub)",
        "ObjectiveRecord.Importance01 | P/TheaterPlanner.cs, P/ReconPlanner.cs | G3 value term; G1b pickets (Roles also G8 posts)",
        "SupplySourceKind.Iip | P/LogisticsGate.cs, P/MarchTime.cs | G0 LogisticsGate.Evaluate (own IIP sources); G3 MarchTime.RailLeg (own endpoints)",
        "SupplySourceKind.Depot | P/DepotTaskLedger.cs | G5c DepotTaskLedger (a built own depot near a site)",
        "SupplySourceRecord.Id | P/MarchTime.cs, P/RailLineScorer.cs | G3 MarchTime.RailLeg (own endpoint IIPs); G5c RailLineScorer.Score (a line's endpoint IIPs)",
        "SupplySourceRecord.Kind | P/LogisticsGate.cs | G0 LogisticsGate.Evaluate",
        "SupplySourceRecord.Owner | P/LogisticsGate.cs | G0 LogisticsGate.Evaluate",
        "SupplySourceRecord.X | P/LogisticsGate.cs | G0 LogisticsGate.Evaluate",
        "SupplySourceRecord.Z | P/LogisticsGate.cs | G0 LogisticsGate.Evaluate",
        "SupplySourceRecord.Condition | P/LogisticsGate.cs | G0 LogisticsGate.Evaluate",
        "SupplySourceRecord.Built | P/LogisticsGate.cs | G0 LogisticsGate.Evaluate",
        "SupplySourceRecord.Invisible | P/LogisticsGate.cs | G0 LogisticsGate.Evaluate",
        "RailLineRecord.Index | P/ConstructionSteeringBoard.cs | G5c ConstructionSteeringBoard.Build / RailroadLineSteeringPatch (line index)",
        "RailLineRecord.Progress | P/MarchTime.cs | G3 MarchTime.RailLeg (a completed line)",
        "RailLineRecord.AX | P/RailLineScorer.cs, P/MarchTime.cs | G5c RailLineScorer.Score; G3 MarchTime.RailLeg",
        "RailLineRecord.AZ | P/RailLineScorer.cs, P/MarchTime.cs | G5c RailLineScorer.Score; G3 MarchTime.RailLeg",
        "RailLineRecord.BX | P/RailLineScorer.cs, P/MarchTime.cs | G5c RailLineScorer.Score; G3 MarchTime.RailLeg",
        "RailLineRecord.BZ | P/RailLineScorer.cs, P/MarchTime.cs | G5c RailLineScorer.Score; G3 MarchTime.RailLeg",
        "RailLineRecord.AIipId | P/RailLineScorer.cs, P/MarchTime.cs | G5c RailLineScorer.Score; G3 MarchTime.RailLeg",
        "RailLineRecord.BIipId | P/RailLineScorer.cs, P/MarchTime.cs | G5c RailLineScorer.Score; G3 MarchTime.RailLeg",
        "CrossingRecord.X | P/RiverLines.cs | G3 RiverLines.EffectiveDistance (detour through the nearest crossing)",
        "CrossingRecord.Z | P/RiverLines.cs | G3 RiverLines.EffectiveDistance (detour through the nearest crossing)",
        "DetectionSource.Seen | P/EnemyArmyModel.cs | G1 DetectionRules.TryDecide (any fog, own-side or fog-off sighting; the persisted default)",
        "DetectionSource.Contact | P/EnemyArmyModel.cs, P/EnemyDetectionRuntime.cs | G1 EnemyDetectionRuntime.Report ([EnemyBeliefs] contact count)",
        "DetectionSource.ObjectiveGarrison | P/EnemyArmyModel.cs | G1 EnemyArmyModel (a garrison belief keeps its radius)",
        "DetectionSource.Mark | P/EnemyArmyModel.cs, P/TheaterPlanner.cs, P/EnemyBeliefReadings.cs, P/ReconMissionLedger.cs, P/ReconPlanner.cs | Foresight F4 a mark belief never scouts (TheaterPlanner.FreshLandBeliefWithin), lands a group or raises a raid (EnemyBeliefReadings), counts as contact (ReconMissionLedger.InContact) or answers the probe (ReconPlanner.ProbeWanted); EnemyArmyModel's second pass and step 4 read it",
        "EnemyDetection.MarkDay | P/EnemyArmyModel.cs | Foresight F4 EnemyArmyModel.ApplyMarks (the belief's LastSeenDay, the age test)",
        "EnemyDetection.MarkRadius | P/EnemyArmyModel.cs | Foresight F4 EnemyArmyModel.ApplyMarks (the belief's BaseRadius)",
        "EnemyBelief.BaseRadius | P/EnemyArmyModel.cs | Foresight F4 EnemyArmyModel.RadiusAt (the radius widens from the mark's base) and the step-3 re-key reach",
        "CampaignSnapshot.DetectionsSeen | P/EnemyPicture.cs | Foresight F4 EnemyPicture.Build (the [EnemyPicture] seen= count)",
        "CampaignSnapshot.DetectionsMarked | P/EnemyPicture.cs | Foresight F4 EnemyPicture.Build (the [EnemyPicture] marks= count)",
        "EnemyTraits.Embarked | P/EnemyBeliefReadings.cs | G1 EnemyBeliefReadings (sea-invasion and raid inference)",
        "EnemyTraits.CavalryMajority | P/EnemyBeliefReadings.cs | G1 EnemyBeliefReadings (sea-invasion and raid inference)",
        "EnemyTraits.OnOwnSoil | P/EnemyBeliefReadings.cs | G1 EnemyBeliefReadings (sea-invasion and raid inference)",
        "EnemyTraits.NearOwnCoast | P/EnemyBeliefReadings.cs | G1 EnemyBeliefReadings (sea-invasion and raid inference)",
        "EnemyDetection.InstanceId | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update",
        "EnemyDetection.UnitKey | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update",
        "EnemyDetection.UnitTyp | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update",
        "EnemyDetection.X | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update",
        "EnemyDetection.Z | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update",
        "EnemyDetection.Strength | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update",
        "EnemyDetection.Source | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update",
        "EnemyDetection.Traits | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update",
        "EnemyBelief.InstanceId | S/DefenseIntentRuntime.cs | G1 DefenseIntentRuntime.BuildInput (the believed enemy group is a threat source by its in-session instance id)",
        "EnemyBelief.UnitKey | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update (identity and garrison provenance)",
        "EnemyBelief.UnitTyp | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update (identity and garrison provenance)",
        "EnemyBelief.LastSource | P/EnemyArmyModel.cs | G1 EnemyArmyModel.Update (identity and garrison provenance)",
        "EnemyBelief.X | P/EnemyBeliefQuery.cs | G0 EnemyBeliefQuery (StrengthWithin, Nearest)",
        "EnemyBelief.Z | P/EnemyBeliefQuery.cs | G0 EnemyBeliefQuery (StrengthWithin, Nearest)",
        "EnemyBelief.Strength | P/EnemyBeliefQuery.cs | G0 EnemyBeliefQuery (StrengthWithin, Nearest)",
        "EnemyBelief.Confidence01 | P/EnemyBeliefQuery.cs | G0 EnemyBeliefQuery (StrengthWithin, Nearest)",
        "EnemyBelief.LastSeenDay | P/EnemyBeliefReadings.cs, P/ReconStep.cs | G1 recency (EnemyBeliefReadings); G1b ReconStep [Recon] oldestBeliefDays",
        "EnemyBelief.UncertaintyRadius | P/ReconPlanner.cs | G1b ReconPlanner scoring",
        "EnemyBelief.Traits | P/EnemyBeliefReadings.cs | G1 EnemyBeliefReadings.IsSeaInvasionThreat (landing inference)",
        "EnemyBelief.LastEmbarkedDay | P/EnemyBeliefReadings.cs | G1 EnemyBeliefReadings.IsSeaInvasionThreat (landing inference)",
        "EnemyDetection.RifleQuality | P/EnemyArmyModel.cs | G9 EnemyArmyModel.Update (the belief takes the sighting's rifle-lane quality)",
        "EnemyDetection.GunQuality | P/EnemyArmyModel.cs | G9 EnemyArmyModel.Update (the belief takes the sighting's gun-lane quality)",
        "EnemyBelief.RifleQuality | P/EnemyWeaponQuality.cs | G9 BelievedQuality.Of (OrdnanceRuntime.TryPick: the purchase pick's edge term)",
        "EnemyBelief.GunQuality | P/EnemyWeaponQuality.cs | G9 BelievedQuality.Of (OrdnanceRuntime.TryPick: the purchase pick's edge term)",
        "CommanderRecord.Id | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.Rank | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.Average | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.Available | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.Navy | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.Dummy | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.RefreshCommand | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.ServingUnderPlayer | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.IsPlayer | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.Pickable | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.FreeApartFromArmyGroup | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.CommandInstanceId | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.CommandUnitTyp | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (candidate filters, rank, sitting commander)",
        "CommanderRecord.Initiative | P/CommanderProfiles.cs | G2 CommanderProfiles.From",
        "CommanderRecord.Leadership | P/CommanderProfiles.cs | G2 CommanderProfiles.From",
        "CommanderRecord.Administration | P/CommanderProfiles.cs | G2 CommanderProfiles.From",
        "CommanderRecord.Name | P/CommanderAppointmentRuntime.cs | G2 [Appointment] row (CommanderAppointmentRuntime.NameOf)",
        "ArmyGroupRecord.InstanceId | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (army-group targets)",
        "ArmyGroupRecord.CommanderId | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (army-group targets)",
        "ArmyGroupRecord.X | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (army-group targets)",
        "ArmyGroupRecord.Z | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (army-group targets)",
        "ArmyGroupRecord.Strength | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (army-group targets)",
        "ArmyGroupRecord.MemberInstanceIds | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan (army-group targets)",
        "TheaterPosture.Hold | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (postures)",
        "TheaterPosture.MainEffort | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (postures)",
        "TheaterPosture.Support | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (postures)",
        "TheaterPosture.EconomyOfForce | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (postures)",
        "TheaterPosture.Recover | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (postures)",
        "TheaterPosture.Screen | P/ReconPlanner.cs | G1b ReconPlanner.AddScreenPostureTasks",
        "FormationRole.Hold | P/ForceAllocator.cs | G4 ForceAllocator.Allocate and Summary ([Allocation] hold= reserve=)",
        "FormationRole.Reserve | P/ForceAllocator.cs | G4 ForceAllocator.Allocate and Summary ([Allocation] hold= reserve=)",
        "FormationRole.MainEffort | P/CampaignMover.cs | G5a CampaignMover (MovesRole), CampaignWithholdView (withheld roles)",
        "FormationRole.Garrison | P/CampaignMover.cs, P/ForceAllocator.cs | D8 and G8 CampaignMover (MovesRole); ForceAllocator.Marches (the give-up rule)",
        "FormationRole.Support | P/CampaignMover.cs | G5a CampaignMover (MovesRole), CampaignWithholdView (withheld roles)",
        "FormationRole.Recover | P/CampaignMover.cs | G5a CampaignMover (MovesRole), CampaignWithholdView (withheld roles)",
        "FormationRole.CapitalGuard | P/CampaignMover.cs, P/CapitalFloorPolicy.cs | G5a mover; G5b CapitalFloorPolicy.PickPledge",
        "FormationRole.DepotBuilder | P/CampaignMover.cs, P/DepotTaskLedger.cs | G5a mover; G5c DepotTaskLedger.Sync",
        "FormationRole.Recon | P/ReconMissionLedger.cs | G5d ReconExecutor; G1b ReconMissionLedger.Sync",
        "FormationRole.Redeploy | P/CampaignMover.cs, P/CampaignWithholdView.cs, P/ForceAllocator.cs | Foresight F6a CampaignMover (the priority list, the offensive de-listing), CampaignWithholdView (withheld from the offence), ForceAllocator.Marches and Summary ([Allocation] redeploy=)",
        "ReconKind.Screen | P/ReconPlanner.cs | G1b ReconPlanner.PlanTasks / PickFormation",
        "ReconKind.Picket | P/ReconPlanner.cs | G1b ReconPlanner.PlanTasks / PickFormation",
        "ReconKind.AdvanceGuard | P/ReconPlanner.cs | G1b ReconPlanner.PlanTasks / PickFormation",
        "ReconKind.Probe | P/ReconStep.cs, P/ReconMissionLedger.cs | G1b ReconStep.ProbeVerdicts; ReconMissionLedger.OnReview",
        "ReconTask.Kind | P/ReconPlanner.cs | G4 order (Priority); G1b PickFormation (Kind)",
        "ReconTask.Priority | P/ReconPlanner.cs | G4 order (Priority); G1b PickFormation (Kind)",
        "ReconTask.SectorId | P/ReconMissionLedger.cs | G1b ReconMissionLedger.Sync (pairing, mission hold point, reason)",
        "ReconTask.HoldX | P/ReconMissionLedger.cs | G1b ReconMissionLedger.Sync (pairing, mission hold point, reason)",
        "ReconTask.HoldZ | P/ReconMissionLedger.cs | G1b ReconMissionLedger.Sync (pairing, mission hold point, reason)",
        "ReconTask.Reason | P/ReconMissionLedger.cs | G1b ReconMissionLedger.Sync (pairing, mission hold point, reason)",
        "ReconTask.FromX | P/ReconPlanner.cs | G1b ReconPlanner.PickFormation (nearest formation to the task)",
        "ReconTask.FromZ | P/ReconPlanner.cs | G1b ReconPlanner.PickFormation (nearest formation to the task)",
        "SectorPlan.SectorId | P/ForceAllocator.cs | G4 ForceAllocator.Allocate",
        "SectorPlan.Theater | X/RecruitmentPatch.cs, X/PerkSelectionPatch.cs | G6 RecruitmentPatch / PerkSelectionPatch (sector theater)",
        "SectorPlan.Posture | P/SteeringRules.cs | G5a SteeringRules (theater boxes, importance redirect)",
        "SectorPlan.TargetObjectiveId | P/CampaignMover.cs, P/SteeringRules.cs | G5a mover targets and the importance redirect",
        "SectorPlan.TargetX | P/CampaignMover.cs, P/SteeringRules.cs | G5a mover targets and the importance redirect",
        "SectorPlan.TargetZ | P/CampaignMover.cs, P/SteeringRules.cs | G5a mover targets and the importance redirect",
        "SectorPlan.RequiredRatio | P/ReconPlanner.cs | G1b ReconPlanner (advance-guard and probe strength)",
        "SectorPlan.HoldNeed | P/ForceAllocator.cs, P/ReinforcementRouting.cs | G4 hold need; G5b ReinforcementRouting.ShortfallStrength",
        "DepotTask.X | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / ConstructionRuntime.BuildInput",
        "DepotTask.Z | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / ConstructionRuntime.BuildInput",
        "DepotTask.SectorId | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / ConstructionRuntime.BuildInput",
        "DepotTask.Priority | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / ConstructionRuntime.BuildInput",
        "TheaterPlan.Alliance | P/CampaignPlannerRuntime.cs | G0 CampaignPlannerRuntime ([CampaignPlan] row, Publish)",
        "TheaterPlan.DaySerial | P/CampaignPlannerRuntime.cs, P/CampaignWithholdView.cs | G5a PlanFreshness (a plan older than two review days is ignored)",
        "TheaterPlan.MainSectorId | P/CommanderAppointmentPlanner.cs, P/ForceAllocator.cs, P/ReconPlanner.cs | G2 appointment targets; G4; G1b",
        "TheaterPlan.MainObjectiveId | P/CampaignMover.cs, P/ReconStep.cs | G5a mover and objective writer; G1b ReconStep",
        "TheaterPlan.MainObjectiveX | P/CampaignPlanStore.cs | G6 CampaignPlanStore.TryGetMainTarget (front ledger target theater, formation-directive target area)",
        "TheaterPlan.MainObjectiveZ | P/CampaignPlanStore.cs | G6 CampaignPlanStore.TryGetMainTarget (front ledger target theater, formation-directive target area)",
        "TheaterPlan.MainRequiredRatio | P/ForceAllocator.cs, P/ReplacementFlowPolicy.cs | G4 reserve split; G5b ReplacementFlowPolicy.ShouldSteer",
        "TheaterPlan.Sectors | P/ForceAllocator.cs, P/CampaignMover.cs | G4 allocation; G5a pointers and theater box",
        "TheaterPlan.CapitalFloorStrength | P/ForceAllocator.cs, X/DefensiveOpsPatch.cs | G4; G5b capital need (CapitalFloorPolicy)",
        "TheaterPlan.CapitalReach | P/EnemyPicture.cs, X/DefensiveOpsPatch.cs | Foresight F3 the [Picture] capital= element count; #4's [CapitalFloor] reach=",
        "TheaterPlan.CapitalWeighted | P/EnemyPicture.cs | Foresight F3 the [Picture] row's capital weight (weighted=)",
        "TheaterPlan.CapitalThreat01 | P/EnemyPicture.cs | Foresight F3 the [Picture] row's capital threat (threat=)",
        "TheaterPlan.CapitalReliefDays | P/EnemyPicture.cs | Foresight F3 the [Picture] row's relief race (relief=)",
        "TheaterPlan.CapitalCorpsPerDay | P/EnemyPicture.cs | Foresight F3 the [Picture] row's relief race (corpsPerDay=)",
        "TheaterPlan.DepotTasks | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync",
        "TheaterPlan.Claims | P/ForceAllocator.cs | Foresight F6a ForceAllocator.Allocate (the ownership table the roles are read from)",
        "TheaterPlan.SectorHeld | P/ForceAllocator.cs | Foresight F6a ForceAllocator.SectorMayGiveScout (D9's measure of a sector's hold)",
        "Claimant.None | P/ForceAllocator.cs | Foresight F6a ForceAllocator.AssignClaims (the later steps place an unclaimed formation)",
        "Claimant.PinnedBuilder | P/ForceAllocator.cs | Foresight F6a ForceAllocator.RoleOf",
        "Claimant.Immobile | P/ForceAllocator.cs | Foresight F6a ForceAllocator.RoleOf",
        "Claimant.Recover | P/ForceAllocator.cs | Foresight F6a ForceAllocator.RoleOf",
        "Claimant.Committed | P/ForceAllocator.cs | Foresight F6a ForceAllocator.RoleOf (a push that ended waits: Hold committed-wait)",
        "Claimant.DepartmentMove | P/ForceAllocator.cs | Foresight F6a ForceAllocator.RoleOf (Redeploy, department-reinforcement)",
        "Claimant.Capital | P/ForceAllocator.cs | Foresight F6a ForceAllocator.RoleOf",
        "Claimant.Hold | P/ForceAllocator.cs | Foresight F6a ForceAllocator.RoleOf",
        "Claimant.Mass | P/ForceAllocator.cs | Foresight F6a ForceAllocator.MarkMass",
        "Claim.InstanceId | P/ForceAllocator.cs | Foresight F6a ForceAllocator.RequireClaims (the table is for this roster)",
        "Claim.Claimant | P/ForceAllocator.cs | Foresight F6a ForceAllocator.AssignClaims",
        "Claim.SectorId | P/ForceAllocator.cs | Foresight F6a ForceAllocator.HoldLeaving (the sector the hold counted a formation in)",
        "Claim.Share | P/ForceAllocator.cs | Foresight F6a ForceAllocator.HoldLeaving (what the hold counted it for)",
        "Claim.TargetX | P/ForceAllocator.cs | Foresight F6a ForceAllocator.AssignClaims (the role's target)",
        "Claim.TargetZ | P/ForceAllocator.cs | Foresight F6a ForceAllocator.AssignClaims (the role's target)",
        "Claim.Reason | P/ForceAllocator.cs | Foresight F6a ForceAllocator.RoleOf (covering-force)",
        "TheaterPlan.SwitchReason | P/CampaignPlannerRuntime.cs | [CampaignPlan] row and its trace (CampaignPlannerRuntime.EmitPlan)",
        "TheaterPlan.PriorId | P/TheaterPlanner.cs | [CampaignPlan] row (TheaterPlanner.Summary: prior=)",
        "TheaterPlan.MassInstanceIds | P/CampaignMover.cs | G5a mover (the tempo roll's idle time and the player in the mass); the allocator reads the mass as the table's Mass claims",
        "TheaterPlan.MassStrength | P/ForceAllocator.cs | G4 ForceAllocator.Allocate (reserve split; [Allocation] mass=)",
        "TheaterPlan.MainEnemyStrength | P/ForceAllocator.cs | G4 ForceAllocator.Allocate (reserve split; [Allocation] mass=)",
        "TheaterPlan.MainScouted | P/TheaterPlanner.cs | D1 [CampaignPlan] scouted=",
        "TheaterPlan.MainScoutLatched | P/TheaterPlanner.cs | F1 [CampaignPlan] scouted=latched",
        "PlannerState.ScoutedDecision | P/TheaterPlanner.cs | F1 TheaterPlanner.Plan (a scouted decision is not re-staged)",
        "SectorPlan.BelievedHold | P/ForceAllocator.cs | smoke 2026-10-17 ForceAllocator.SectorMayGiveScout (is the sector's hold need driven by a believed army)",
        "Allocation.ReconUnfilledWhy | P/ReconStep.cs | smoke 2026-10-18 [Recon] why= (the reason each task has no scout)",
        "Allocation.ReconFree | P/ReconStep.cs | smoke 2026-10-18 [Recon] free= (the scouts no role had claimed)",
        "Allocation.ReconMass | P/ReconStep.cs | smoke 2026-10-18 [Recon] mass= (the main effort's bodies a pick could still take)",
        "PlannerState.ScoutedTargetX | P/TheaterPlanner.cs | smoke 2026-10-17 TheaterPlanner.Plan (the latch speaks for the scouted record only)",
        "PlannerState.ScoutedTargetZ | P/TheaterPlanner.cs | smoke 2026-10-17 TheaterPlanner.Plan (the latch speaks for the scouted record only)",
        "PlannerState.CurrentTargetX | P/TheaterPlanner.cs | smoke 2026-10-17 TheaterPlanner.HeldRecord (the target record the main effort keeps)",
        "PlannerState.CurrentTargetZ | P/TheaterPlanner.cs | smoke 2026-10-17 TheaterPlanner.HeldRecord (the target record the main effort keeps)",
        "PlannerState.RecordChallengerX | P/TheaterPlanner.cs | smoke 2026-10-17 TheaterPlanner.HeldRecord (the target record the main effort keeps)",
        "PlannerState.RecordChallengerZ | P/TheaterPlanner.cs | smoke 2026-10-17 TheaterPlanner.HeldRecord (the target record the main effort keeps)",
        "PlannerState.RecordChallengerDays | P/TheaterPlanner.cs | smoke 2026-10-17 TheaterPlanner.HeldRecord (the target record the main effort keeps)",
        "TheaterPlan.MainStaged | P/ForceAllocator.cs, P/CampaignMover.cs | D1 ForceAllocator.MainTarget (the start line); CampaignMover (a gathering is not a march)",
        "TheaterPlan.StartLineX | P/ForceAllocator.cs | D1 ForceAllocator.MainTarget",
        "TheaterPlan.StartLineZ | P/ForceAllocator.cs | D1 ForceAllocator.MainTarget",
        "TheaterPlan.MainNeedStrength | P/ForceAllocator.cs | D3 [Allocation] need=",
        "TheaterPlan.MainCapacityStrength | P/ForceAllocator.cs | D3 [Allocation] cap=",
        "TheaterPlan.ReinforcementInstanceId | P/ForceAllocator.cs | D6 ForceAllocator.AssignMainEffort (the Support formation)",
        "TheaterPlan.ReinforcementName | P/TheaterPlanner.cs | D6 TheaterPlanner.Summary ([CampaignPlan] xdept=)",
        "TheaterPlan.ReinforcementFrom | P/TheaterPlanner.cs | D6 TheaterPlanner.Summary ([CampaignPlan] xdept=)",
        "TheaterPlan.ReinforcementTo | P/TheaterPlanner.cs | D6 TheaterPlanner.Summary ([CampaignPlan] xdept=)",
        "TheaterPlan.Garrisons | P/ForceAllocator.cs | D8 ForceAllocator.AssignGarrisons ([Allocation] garrison=)",
        "GarrisonPost.StateName | P/ForceAllocator.cs | D8 [Allocation] garrison=",
        "GarrisonPost.X | P/ForceAllocator.cs | D8 ForceAllocator.AssignGarrisons (the post is the town)",
        "GarrisonPost.Z | P/ForceAllocator.cs | D8 ForceAllocator.AssignGarrisons (the post is the town)",
        "GarrisonPost.InState | P/ForceAllocator.cs | D8 ForceAllocator.AssignGarrisons (hold in place or march); [Allocation] garrison=",
        "GarrisonPost.Need | P/ForceAllocator.cs | D8 [Allocation] garrison=",
        "GarrisonPost.InstanceIds | P/ForceAllocator.cs | D8 ForceAllocator.AssignGarrisons",
        "GarrisonPost.Name | P/ForceAllocator.cs | D8 [Allocation] garrison=",
        "TheaterPlan.DepartmentCounts | P/TheaterPlanner.cs | D6 TheaterPlanner.Summary ([CampaignPlan] depts=)",
        "PlannerState.ReinforcementKey | P/TheaterPlanner.cs | D6 TheaterPlanner.NoteReinforcement (the decision's one reinforcement)",
        "PlannerState.ReinforcementDecision | P/TheaterPlanner.cs | D6 TheaterPlanner.NoteReinforcement (the decision it was picked for)",
        "CommanderProfile.RequiredOddsShift | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan",
        "CommanderProfile.AttackBias | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan",
        "CommanderProfile.SupplySlack | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan",
        "RankBand.UnitTyp | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.RankFits",
        "RankBand.MinRank | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.RankFits",
        "RankBand.MaxRank | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.RankFits",
        "Assignment.InstanceId | P/CampaignMover.cs | G5a CampaignMover (role dispatch)",
        "Assignment.Role | P/CampaignMover.cs | G5a CampaignMover (role dispatch)",
        "Assignment.SectorId | P/SteeringRules.cs, P/CampaignMover.cs | G5a theater box (SteeringRules) and mover (the assignment's sector plan)",
        "Assignment.TargetX | P/CampaignMover.cs | G5a CampaignMover (the move target)",
        "Assignment.TargetZ | P/CampaignMover.cs | G5a CampaignMover (the move target)",
        "Assignment.Reason | P/ForceAllocator.cs | [Allocation] row and tests of the role rules (ForceAllocator.Summary)",
        "Assignment.Withheld | P/CampaignWithholdView.cs | G5a CampaignWithholdView (the availability Postfixes)",
        "Allocation.Alliance | P/CampaignPlannerRuntime.cs | [Allocation] row (CampaignPlannerRuntime.EmitAllocation)",
        "Allocation.Assignments | P/CampaignMover.cs, P/CampaignWithholdView.cs | G5a mover and withhold view",
        "Allocation.Shortfalls | P/ReinforcementRouting.cs | G5b ReinforcementRouting (recruit routing, transfer pairs)",
        "Allocation.Surpluses | P/ReinforcementRouting.cs | G5b ReinforcementRouting (recruit routing, transfer pairs)",
        "CampaignSnapshot.Alliance | P/CampaignPlannerRuntime.cs, P/TheaterPlanner.cs | every pure step",
        "CampaignSnapshot.DaySerial | P/CampaignPlannerRuntime.cs, P/TheaterPlanner.cs | every pure step",
        "CampaignSnapshot.FactionIndex | P/CampaignSnapshotRuntime.cs, P/CampaignMoverRuntime.cs | G5a executor host (the faction the executor writes through)",
        "CampaignSnapshot.Player | P/CommanderAppointmentPlanner.cs, P/ForceAllocator.cs | G0 gates; G4; G2 no-vertical-swap check",
        "CampaignSnapshot.PlayerCommandInstanceId | P/CommanderAppointmentPlanner.cs | G2 no-vertical-swap check",
        "CampaignSnapshot.Tempo | P/TheaterPlanner.cs, P/ReconPlanner.cs | G3 required ratio (MassRatioModifier); G4 hold need (MinimumHoldRatioModifier)",
        "CampaignSnapshot.CapitalX | P/TheaterPlanner.cs, P/ReconPlanner.cs, P/ForceAllocator.cs | G3 capital-threat term; G1b capital approaches; G4",
        "CampaignSnapshot.CapitalZ | P/TheaterPlanner.cs, P/ReconPlanner.cs, P/ForceAllocator.cs | G3 capital-threat term; G1b capital approaches; G4",
        "CampaignSnapshot.HasCapital | P/TheaterPlanner.cs, P/ReconPlanner.cs, P/ForceAllocator.cs | G3 capital-threat term; G1b capital approaches; G4",
        "CampaignSnapshot.RelocationRange | P/TheaterPlanner.cs, P/ReconPlanner.cs, P/SectorModel.cs | G0 SectorModel (adjacency radius), G3, G1b",
        "CampaignSnapshot.TheaterHalfExtentX | P/SteeringRules.cs | G5a SteeringRules theater box (both axes, 17105)",
        "CampaignSnapshot.TheaterHalfExtentZ | P/SteeringRules.cs | G5a SteeringRules theater box (both axes, 17105)",
        "CampaignSnapshot.OffensiveDominance | P/TheaterPlanner.cs, P/ReconPlanner.cs | G3 required ratio; G1b",
        "CampaignSnapshot.MinStrengthForMovement | P/TheaterPlanner.cs | Foresight F5 TheaterPlanner.PlanMinimum (the scenario's aiminimumstrengthformovement becomes TheaterPlan.MinGarrisonStrength; ReconPlanner reads the plan's validated value, FlagsOf keeps the raw read)",
        "TheaterPlan.MinGarrisonStrength | P/ForceAllocator.cs, P/ReinforcementRouting.cs, P/ReservesRow.cs, P/ReconPlanner.cs, P/CampaignPlannerRuntime.cs | Foresight F5 the one definition site of the minimum: ForceAllocator.Target and SectorMayGiveScout, ReinforcementRouting.ShortfallStrength, the [Reserves] row's quiet sectors, ReconPlanner's CanMove and CouldScout, the first-review row",
        "CampaignSnapshot.EnemyCapitalX | P/EnemyPicture.cs | Foresight F3 EnemyPicture.Build anchor",
        "CampaignSnapshot.EnemyCapitalZ | P/EnemyPicture.cs | Foresight F3 EnemyPicture.Build anchor",
        "CampaignSnapshot.HasEnemyCapital | P/EnemyPicture.cs | Foresight F3 EnemyPicture.Build",
        "CampaignSnapshot.EnemyCapitalName | P/EnemyPicture.cs | Foresight F3 EnemyPicture.Build (the anchor's name in the [Picture] row)",
        "CampaignSnapshot.EnemyTroopsFielded | P/EnemyPicture.cs | Foresight F3 EnemyPicture.Build (the unaccounted men)",
        "CampaignSnapshot.MinMoraleForMovement | P/ReserveRules.cs, P/TheaterPlanner.cs, P/ReconPlanner.cs, P/ForceAllocator.cs | G3/G4 Recover and movement minima; G1b",
        "CampaignSnapshot.Own | P/ForceAllocator.cs, P/TheaterPlanner.cs, P/ReconPlanner.cs, P/ReconMissionLedger.cs | every step",
        "CampaignSnapshot.Areas | P/TheaterPlanner.cs, P/SectorModel.cs, P/LogisticsGate.cs | G0 SectorModel.Build; G3; LogisticsGate.Evaluate",
        "CampaignSnapshot.Objectives | P/TheaterPlanner.cs, P/ReconPlanner.cs, P/EnemyDetectionRuntime.cs | G3 candidates; G1b; G1 ObjectiveGarrison detections",
        "CampaignSnapshot.MappedObjectiveCount | P/CampaignPlannerRuntime.cs | G3c CampaignPlannerRuntime.LogFirstReview (mapped=)",
        "CampaignSnapshot.Supply | P/LogisticsGate.cs, P/DepotTaskLedger.cs | G0 LogisticsGate.Evaluate; G5c DepotTaskLedger",
        "CampaignSnapshot.Rails | P/ConstructionSteeringBoard.cs, P/MarchTime.cs | G5c ConstructionSteeringBoard.Build; G3 MarchTime.RailLeg",
        "CampaignSnapshot.Commanders | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan",
        "CampaignSnapshot.RankBands | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan",
        "CampaignSnapshot.ArmyGroups | P/CommanderAppointmentPlanner.cs | G2 CommanderAppointmentPlanner.TryPlan",
        "CampaignSnapshot.Secessions | P/TheaterPlanner.cs | D8 TheaterPlanner.Garrisons / SecessionValue (read live each review)",
        "SecessionRecord.ObjectiveId | P/TheaterPlanner.cs | D8 TheaterPlanner.SecessionValue (the town's own bonus)",
        "SecessionRecord.StateId | P/TheaterPlanner.cs | D8 TheaterPlanner.Garrisons / SecessionValue",
        "SecessionRecord.StateAlliance | P/TheaterPlanner.cs | D8 TheaterPlanner.Garrisons (only the state's owner garrisons it)",
        "SecessionRecord.Live | P/TheaterPlanner.cs | D8 TheaterPlanner.Garrisons / SecessionValue (vanilla's IsDeactivated is false)",
        "SecessionRecord.OwnTroops | P/TheaterPlanner.cs | F2 TheaterPlanner.Garrisons (vanilla's alliancetroops[0]: a garrison alone blocks the secession)",
        "SecessionRecord.GarrisonStrength | P/TheaterPlanner.cs | Landing round TheaterPlanner.Garrisons (the flag alone: the hold less the town garrison's own strength)",
        "GarrisonPost.StateId | P/ForceAllocator.cs | Landing round ForceAllocator.AssignGarrisons (a member inside holds, one outside marches)",
        "GarrisonPost.Shortfall | P/ForceAllocator.cs | Landing check [Allocation] garrison=...!short<n> (the need the mass would have had to meet)",
        "GarrisonPost.Marching | P/ForceAllocator.cs | Landing round [Allocation] garrison= (in+moving)",
        "GarrisonPost.MassPresence | P/ForceAllocator.cs | Landing r2 [Allocation] garrison=mass-presence (the mass inside the state is presence, not pinned)",
        "GarrisonPost.PlayerPresence | P/ForceAllocator.cs | Landing round [Allocation] garrison=player-presence",
        "SecessionRecord.HasTown | P/TheaterPlanner.cs | D8 TheaterPlanner.Garrisons",
        "SecessionRecord.TownX | P/TheaterPlanner.cs | D8 TheaterPlanner.Garrisons (the post)",
        "SecessionRecord.TownZ | P/TheaterPlanner.cs | D8 TheaterPlanner.Garrisons (the post)",
        "FormationRecord.StateId | P/TheaterPlanner.cs | D8 TheaterPlanner.Garrisons (a Union group inside the state denies the secession)",
        "ObjectiveRecord.StateId | P/TheaterPlanner.cs | D8 TheaterPlanner.SecessionValue (the state's other targets)",
        "ObjectiveRecord.GarrisonStrength | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites (own and allied forts' garrison dummies at the target; the need subtracts it)",
        "CampaignSnapshot.Forts | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites (the side's own forts are post sites)",
        "CampaignSnapshot.Places | P/CoveringForce.cs | Foresight F6b-1 CoveringForce.PostNear and CorridorRow (the towns a covering post is chosen from)",
        "PlaceRecord.Name | P/CoveringForce.cs | Foresight F6b-1 CoveringForce.CorridorRow (the [CoveringCorridor] row names the candidates and the posts)",
        "PlaceRecord.X | P/CoveringForce.cs | Foresight F6b-1 CoveringForce.PostNear (straight distance to a point)",
        "PlaceRecord.Z | P/CoveringForce.cs | Foresight F6b-1 CoveringForce.PostNear (straight distance to a point)",
        "PlaceRecord.Owner | P/CoveringForce.cs | Foresight F6b-1 CoveringForce.PostNear (only an own-held town is a post)",
        "FortRecord.X | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites (the post is the fort)",
        "FortRecord.Z | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites (the post is the fort)",
        "FortRecord.GarrisonStrength | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites (the need subtracts the fort's own garrison dummy)",
        "GarrisonPostKind.ObjectiveTown | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites",
        "GarrisonPostKind.RailHub | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites",
        "GarrisonPostKind.Port | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites",
        "GarrisonPostKind.Depot | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites",
        "GarrisonPostKind.Fort | P/GarrisonPlanner.cs | G8 GarrisonPlanner.CollectSites",
        "GarrisonPostKind.Secession | P/ForceAllocator.cs, P/GarrisonLedger.cs | D8 posts: ForceAllocator seats them early (AssignGarrisons); the ledger records them too and names them by state",
        "GarrisonPost.Kind | P/ForceAllocator.cs, P/GarrisonLedger.cs | G8 ForceAllocator.AssignGarrisons / AssignGarrisonPosts (which sort of post); ledger records and names",
        "GarrisonPost.ExistingStrength | P/GarrisonLedger.cs | G8 [GarrisonPost] existing= (why the need is what it is)",
        "GarrisonPost.QuietSinceDay | P/GarrisonLedger.cs | G8 GarrisonLedger.Sync (the record keeps when the quiet began; GarrisonPlanner releases after ReleaseQuietDays)",
        "GarrisonSeat.PostIndex | P/GarrisonLedger.cs | G8 GarrisonLedger.Sync (which post a seat holds)",
        "GarrisonSeat.InstanceId | P/GarrisonLedger.cs | G8 GarrisonLedger.Sync (who holds it)",
        "GarrisonSeat.AtPost | P/GarrisonLedger.cs | G8 GarrisonLedger.Sync (Holding or Moving)",
        "Allocation.Seats | P/GarrisonLedger.cs | G8 GarrisonLedger.Sync",
        "CampaignSnapshot.Crossings | P/TheaterPlanner.cs | G3 RiverLines.EffectiveDistance (via the planner's terrain source)",
        "CampaignSnapshot.Detections | P/EnemyDetectionRuntime.cs | G1 EnemyDetectionRuntime.Report / Detect",
        "CampaignSnapshot.Beliefs | P/ReconPlanner.cs, P/TheaterPlanner.cs, P/ReconStep.cs | G3 balance; G1b recon scoring",
        "PlannerState.CurrentMainObjective | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (hysteresis, odds deadband, capital latch, seed, serial)",
        "PlannerState.ChallengerObjective | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (hysteresis, odds deadband, capital latch, seed, serial)",
        "PlannerState.ChallengerDays | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (hysteresis, odds deadband, capital latch, seed, serial)",
        "PlannerState.DecisionSerial | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (hysteresis, odds deadband, capital latch, seed, serial)",
        "PlannerState.CampaignSeed | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (hysteresis, odds deadband, capital latch, seed, serial)",
        "PlannerState.BelowOddsDays | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (hysteresis, odds deadband, capital latch, seed, serial)",
        "PlannerState.CapitalThreatLatched | P/TheaterPlanner.cs | G3 TheaterPlanner.Plan (hysteresis, odds deadband, capital latch, seed, serial)",
        "PlannerState.TempoReleasedDecision | P/CampaignMover.cs | G5a CampaignMover (tempo release and clock)",
        "PlannerState.LastFullReviewDay | P/CampaignMover.cs | G5a CampaignMover (tempo release and clock)",
        "PlannerState.TempoRowDecision | P/CampaignMover.cs | G5a CampaignMover ([CampaignTempo] rows, once per decision)",
        "PlannerState.UndispatchableRowDecision | P/CampaignMover.cs | G5a CampaignMover ([CampaignTempo] rows, once per decision)",
        "ReconState.Assigned | P/ReconMissionLedger.cs | G1b ReconMissionLedger.OnReview / Sync; G5d ReconOrders.CavalryOrderFor",
        "ReconState.Moving | P/ReconMissionLedger.cs | G1b ReconMissionLedger.OnReview / Sync; G5d ReconOrders.CavalryOrderFor",
        "ReconState.Holding | P/ReconMissionLedger.cs | G1b ReconMissionLedger.OnReview / Sync; G5d ReconOrders.CavalryOrderFor",
        "ReconState.Recalled | P/ReconMissionLedger.cs | G1b ReconMissionLedger.OnReview / Sync; G5d ReconOrders.CavalryOrderFor",
        "ReconState.Cleared | P/ReconMissionLedger.cs | G1b ReconMissionLedger.OnReview / Sync; G5d ReconOrders.CavalryOrderFor",
        "ReconMission.MissionId | P/ReconMissionLedger.cs, P/ReconExecutor.cs | G5d ReconExecutor ([ReconMission] rows, change detection)",
        "ReconMission.Kind | P/ReconMissionLedger.cs, P/ReconExecutor.cs | G5d ReconExecutor and [ReconMission] rows; G1b ReconMissionLedger",
        "ReconMission.InstanceId | P/ReconMissionLedger.cs, P/ReconExecutor.cs | G5d ReconExecutor and [ReconMission] rows; G1b ReconMissionLedger",
        "ReconMission.UnitKey | P/ReconMissionLedger.cs, P/ReconExecutor.cs | G5d ReconExecutor and [ReconMission] rows; G1b ReconMissionLedger",
        "ReconMission.HoldX | P/ReconMissionLedger.cs, P/ReconExecutor.cs | G5d ReconExecutor and [ReconMission] rows; G1b ReconMissionLedger",
        "ReconMission.HoldZ | P/ReconMissionLedger.cs, P/ReconExecutor.cs | G5d ReconExecutor and [ReconMission] rows; G1b ReconMissionLedger",
        "ReconMission.State | P/ReconMissionLedger.cs, P/ReconExecutor.cs | G5d ReconExecutor and [ReconMission] rows; G1b ReconMissionLedger",
        "ReconMission.Reason | P/ReconExecutor.cs | G5d ReconExecutor and [ReconMission] rows; G1b ReconMissionLedger",
        "ReconMission.SectorId | P/ReconStep.cs | G1b ReconStep.ProbeVerdicts (a probe serves the main sector)",
        "ReconMission.IssuedDay | P/ReconMissionLedger.cs | G1b ReconMissionLedger.OnReview (MaxMissionDays expiry)",
        "ReconMission.ObservedEnemyStrength | P/ReconStep.cs | G1b ReconStep.ProbeVerdicts (the enemy-reaction reference)",
        "ReconMission.ObservedObjectiveId | P/ReconStep.cs | G1b ReconStep.ProbeVerdicts (the enemy-reaction reference)",
        "ReconMission.StandDownDue | P/ReconMissionLedger.cs, P/ReconExecutor.cs | G5d ReconExecutor (the stand-down debt); ReconMissionLedger.OnReview",
        "ReconMission.ClearedDay | P/ReconMissionLedger.cs | G5d ReconMissionLedger.OnReview (bounds a pending stand-down)",
        "DepotTaskState.Marching | P/DepotTaskLedger.cs | G5c DepotTaskLedger state machine and HoldsBuilder",
        "DepotTaskState.Wished | P/DepotTaskLedger.cs | G5c DepotTaskLedger state machine and HoldsBuilder",
        "DepotTaskState.Built | P/DepotTaskLedger.cs | G5c DepotTaskLedger state machine and HoldsBuilder",
        "DepotTaskState.Abandoned | P/DepotTaskLedger.cs | G5c DepotTaskLedger state machine and HoldsBuilder",
        "DepotWishMoment.Wait | P/DepotTaskLedger.cs | G5c DepotTaskLedger.WishMoment (no wish yet; the runtime's switch places none)",
        "DepotWishMoment.Here | P/DepotTaskLedger.cs, P/DepotSteeringRuntime.cs | G5c DepotTaskLedger.WishMoment; DepotSteeringRuntime.Execute (the wish placed)",
        "DepotWishMoment.Adopt | P/DepotTaskLedger.cs, P/DepotSteeringRuntime.cs | G5c DepotTaskLedger.WishMoment; DepotSteeringRuntime.Execute (the wish placed)",
        "DepotWishMoment.Ahead | P/DepotTaskLedger.cs, P/DepotSteeringRuntime.cs | G5c DepotTaskLedger.WishMoment; DepotSteeringRuntime.Execute (the wish placed)",
        "DepotTaskRecord.X | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskRecord.Z | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskRecord.SectorId | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskRecord.BuilderInstanceId | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskRecord.BuilderUnitKey | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskRecord.State | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskRecord.StartedDay | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskRecord.WishX | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskRecord.WishZ | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskRecord.EndedDay | P/DepotTaskLedger.cs | G5c DepotTaskLedger.Sync / WatchWish; G6 sidecar v3 (depotTasks)",
        "DepotTaskTransition.X | P/DepotSteeringRuntime.cs | G5c DepotSteeringRuntime ([DepotTask] rows)",
        "DepotTaskTransition.Z | P/DepotSteeringRuntime.cs | G5c DepotSteeringRuntime ([DepotTask] rows)",
        "DepotTaskTransition.BuilderUnitKey | P/DepotSteeringRuntime.cs | G5c DepotSteeringRuntime ([DepotTask] rows)",
        "DepotTaskTransition.State | P/DepotSteeringRuntime.cs | G5c DepotSteeringRuntime ([DepotTask] rows)",
        "DepotTaskTransition.Reason | P/DepotSteeringRuntime.cs | G5c DepotSteeringRuntime ([DepotTask] rows)",
        "DepotWishFacts.DaySerial | P/DepotTaskLedger.cs | G5c DepotTaskLedger.WishMoment",
        "DepotWishFacts.MarchIssuedEarlier | P/DepotTaskLedger.cs | G5c DepotTaskLedger.WishMoment",
        "DepotWishFacts.OwnUnbuiltDepotAtSite | P/DepotTaskLedger.cs | G5c DepotTaskLedger.WishMoment",
        "DepotWishFacts.AdoptX | P/DepotSteeringRuntime.cs | G5c DepotSteeringRuntime (the adopted depot's position)",
        "DepotWishFacts.AdoptZ | P/DepotSteeringRuntime.cs | G5c DepotSteeringRuntime (the adopted depot's position)",
        "HeldBuilder.InstanceId | P/ForceAllocator.cs | G5c ForceAllocator.RoleSectorOf (the pin's sector)",
        "HeldBuilder.SectorId | P/ForceAllocator.cs | G5c ForceAllocator.RoleSectorOf (the pin's sector)",
        "HeldBuilder.TargetX | P/ClaimTable.cs | G5c, F6a ClaimTable.ClaimPinned (the pin's position, row 1)",
        "HeldBuilder.TargetZ | P/ClaimTable.cs | G5c, F6a ClaimTable.ClaimPinned (the pin's position, row 1)",
        "HeldBuilder.Wished | P/ForceAllocator.cs | G5c ForceAllocator.HasPinnedBuilder",
        "HeldBuilder.SiteX | P/ForceAllocator.cs | G5c ForceAllocator (one task, one builder)",
        "HeldBuilder.SiteZ | P/ForceAllocator.cs | G5c ForceAllocator (one task, one builder)",
        "MoveRecord.InstanceId | P/MoveTracker.cs | G5a MoveTracker.Restore (a saved record becomes a settled entry); G6 sidecar v3 (moves)",
        "MoveRecord.UnitKey | P/MoveTracker.cs | G5a MoveTracker.Restore (a saved record becomes a settled entry); G6 sidecar v3 (moves)",
        "MoveRecord.TargetX | P/MoveTracker.cs | G5a MoveTracker.Restore (a saved record becomes a settled entry); G6 sidecar v3 (moves)",
        "MoveRecord.TargetZ | P/MoveTracker.cs | G5a MoveTracker.Restore (a saved record becomes a settled entry); G6 sidecar v3 (moves)",
        "MoveRecord.IssuedDay | P/MoveTracker.cs | G5a MoveTracker.Restore (a saved record becomes a settled entry); G6 sidecar v3 (moves)",
        "MoveRecord.Retries | P/MoveTracker.cs | G5a MoveTracker.Restore (a saved record becomes a settled entry); G6 sidecar v3 (moves)",
        "MoveRecord.ObjectiveId | P/MoveTracker.cs | G5a MoveTracker.Restore (a saved record becomes a settled entry); G6 sidecar v3 (moves)",
        "GaveUpMove.InstanceId | P/ForceAllocator.cs | G4 ForceAllocator.GaveUpAt (a formation that gave up at the target loses the role)",
        "GaveUpMove.TargetX | P/ForceAllocator.cs | G4 ForceAllocator.GaveUpAt (a formation that gave up at the target loses the role)",
        "GaveUpMove.TargetZ | P/ForceAllocator.cs | G4 ForceAllocator.GaveUpAt (a formation that gave up at the target loses the role)",
        "GaveUpMove.ObjectiveId | P/TheaterPlanner.cs | G3 TheaterPlanner.MassFor (leaves a formation out of the mass of the objective it gave up on)",
        "ExecutorContext.Released | P/CampaignWithholdView.cs | G5a CampaignWithholdView (deferred Support, pre-war gate)",
        "ExecutorContext.ExecutorsAllowed | P/CampaignWithholdView.cs | G5a CampaignWithholdView (deferred Support, pre-war gate)",
        "Flag:EnableCampaignPlanner | P/CampaignPlannerRuntime.cs, P/EnemyDetectionRuntime.cs | G0 CampaignPlannerRuntime.ReviewPlan; G1 EnemyDetectionRuntime.BeliefsFor (fallback)",
        "Flag:EnableCampaignPlannerExecutors | P/CampaignPlannerRuntime.cs | G0 CampaignPlannerRuntime.ExecutorsEnabled / ReviewExecute; the G5 planner patches",
        "Flag:EnableVanillaDefensiveInterception | X/CheckForDefensiveOperationsCandidateFilterPatch.cs | Foresight F2 #25 telemetry-only while the planner runs (DefenseInterceptionGate.ModeFor)",
        "Flag:EnableSpottingMarkBeliefs | P/EnemyDetectionRuntime.cs | Foresight F4 EnemyDetectionRuntime.ProbeMarks (no mark detection on either side with it off)",
        "Flag:FixSpottingMarkTiming | X/SpottingMarkTimingFixPatch.cs, P/EnemyDetectionRuntime.cs | Foresight F4b BUG-INTEL-001: the patch swaps the right time-factor pair into vanilla's UpdateMarks and the AI side's mark inputs take the same pair (MarkRules.TimingPair)",
        "Flag:EnableStrategicForesightPicture | S/StrategicCoordinator.cs | Foresight F3 the daily review copies it to PlannerSettings.ForesightPicture (TheaterPlanner, ForceAllocator, ReserveRules read the setting; the rollback to D2/P7)",
        "Flag:EnableCampaignReconnaissance | P/CampaignPlannerRuntime.cs | G1b recon step; G5d executor",
        "Flag:EnableCampaignFogHonestBeliefs | P/EnemyDetectionRuntime.cs | G1 EnemyDetectionRuntime.Detect / BeliefsFor",
        "Flag:EnableCommanderAppointmentPlanner | P/CommanderAppointmentRuntime.cs | G2 appointment step and the #6 guard (CommanderAppointmentRuntime.Enabled)",
        "Flag:EnableSupplyDepotSteering | P/DepotSteeringRuntime.cs | G5c DepotSteeringRuntime",
        "Flag:EnableFortSteering | X/FortConstructionGovernorPatch.cs | G5c FortConstructionGovernorPatch (#27)",
        "Flag:EnableRailroadSteering | X/RailroadLineSteeringPatch.cs | G5c RailroadLineSteeringPatch",
        "Flag:EnableRearmPriority | P/OrdnanceRuntime.cs | G9 OrdnanceRuntime.BeginRearm (RearmPriorityPatch)",
        "Flag:EnableOrdnancePurchaseSteering | P/OrdnanceRuntime.cs | G9 OrdnanceRuntime.PurchaseScopeOpens (OrdnancePurchasePatch)",
        "Flag:EnableGarrisonManning | P/GarrisonRuntime.cs | G8 GarrisonRuntime.PlanPosts",
        "Flag:EnableWlCareerTransferFix | X/WlCareerTransferFixPatch.cs | G5b WlCareerTransferFixPatch",
    };
}
