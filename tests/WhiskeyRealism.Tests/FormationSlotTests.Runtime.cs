using System;
using System.IO;
using System.Text.RegularExpressions;
using static TestHarness;
using static TestFixtures;

// BC16 step 0's runtime half (the Harmony patch, the telemetry routes, the flag text) is not compiled into this project, so its wiring is pinned at
// the source, as BattleLodTests.Runtime pins BC15's.
internal static partial class FormationSlotTests
{
    private static (string, Action)[] RuntimeTests()
    {
        return new (string, Action)[]
        {
            ("bc16 RUNTIME LayoutObserverPatch is one non-skipping Postfix on the Regiment overload of SetGroupFormation", PatchShape),
            ("bc16 RUNTIME the attribute's 14 argument types resolve SetGroupFormation on the real Assembly-CSharp, with the parameter names the Postfix binds", SignatureResolvesOnTheGame),
            ("bc16 RUNTIME the patch entry has its scope, its first-fire mark first, a flag gate and a campaign skip", EntryDiscipline),
            ("bc16 RUNTIME the observer reads the resolved facing and the placed position the way decision 9 says", ReadsWhatDecisionNineSays),
            ("bc16 RUNTIME the observer writes nothing into the battle", WritesNothing),
            ("bc16 RUNTIME the mirror row, the flip row and the field probe carry the plan's fields and texts", RowsCarryThePlansFields),
            ("bc16 RUNTIME a deployment placement is baseline for the flip count, not a flip", ImmediateIsNotCountable),
            ("bc16 RUNTIME both rows have a telemetry route and the observer flag describes them", RowsAreRoutedAndDescribed),
            ("bc16 RUNTIME the pure files read no Unity type and no real time", PureFilesArePure),
            ("bc16 RUNTIME the patch, its rows and its probe are in the living docs", DocsNameTheObserver),
        };
    }

    private const string PatchFile = "src/WhiskeyRealism/Patches/LayoutObserverPatch.cs";

    private static string Code(string repoRelative)
        => Regex.Replace(ReadRepoFile(repoRelative), @"//[^\n]*", string.Empty);

    private static void PatchShape()
    {
        string code = Code(PatchFile);
        AssertTrue(Regex.IsMatch(code,
            @"\[HarmonyPatch\(typeof\(BattleUnits\), ""SetGroupFormation"", new\[\]\s*\{\s*typeof\(Regiment\), typeof\(int\), typeof\(float\), typeof\(Vector3\), typeof\(bool\), typeof\(bool\), typeof\(bool\),\s*typeof\(int\), typeof\(int\), typeof\(bool\), typeof\(bool\), typeof\(bool\), typeof\(bool\), typeof\(bool\)\s*\}\)\]"),
            "the 14-argument Regiment overload (91822), not the GameObject wrapper (91815), which calls it");
        AssertEqual(1, Regex.Matches(code, @"\[HarmonyPostfix\]").Count, "one Postfix");
        AssertFalse(Regex.IsMatch(code, @"\[Harmony(Prefix|Finalizer|Transpiler)\]"), "nothing else: it never skips and never rewrites vanilla");
        AssertTrue(code.Contains("static void Postfix(BattleUnits __instance, Regiment grpreg, bool immediateplacement)"), "vanilla's own parameter names");
    }

    private static void SignatureResolvesOnTheGame()
    {
        var types = new[]
        {
            typeof(Regiment), typeof(int), typeof(float), typeof(UnityEngine.Vector3), typeof(bool), typeof(bool), typeof(bool),
            typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
        };
        var method = typeof(BattleUnits).GetMethod("SetGroupFormation", types);
        AssertTrue(method != null, "BattleUnits.SetGroupFormation(Regiment, int, float, Vector3, bool x3, int, int, bool x5) exists");
        var parameters = method.GetParameters();
        AssertEqual("grpreg", parameters[0].Name, "Harmony binds the Postfix's grpreg by name");
        AssertEqual("immediateplacement", parameters[4].Name, "and immediateplacement");
        // The fields the observer reads off the layout and the units (a rename in a game update fails here, not in a battle).
        AssertEqual(typeof(int[]), typeof(BattleUnits.GroupPosition).GetField("row").FieldType, "GroupPosition.row");
        AssertEqual(typeof(int[]), typeof(BattleUnits.GroupPosition).GetField("column").FieldType, "GroupPosition.column");
        AssertEqual(typeof(Regiment[]), typeof(BattleUnits.GroupPosition).GetField("attachedunitreg").FieldType, "GroupPosition.attachedunitreg");
        AssertTrue(typeof(BattleUnits).GetField("IsCampaign") != null, "BattleUnits.IsCampaign");
        AssertTrue(typeof(BattleUnits).GetField("completeunitlist") != null, "BattleUnits.completeunitlist");
        foreach (string field in new[] { "groupformation", "formationordered", "formation", "lastsetwaypointrotation", "lastsetwaypointposition", "permanentlydetached", "isrouted", "ai_stance", "groupposition" })
            AssertTrue(typeof(Regiment).GetField(field) != null, "Regiment." + field);
    }

    private static void EntryDiscipline()
    {
        string code = Code(PatchFile);
        Match scope = Regex.Match(code, @"using \(TelemetryPerf\.Scope\(""tactical\.patch\.layout-observer"", TelemetryLayer\.Tactical, TelemetryCategory\.Performance, 2\.0\)\)\s*\{\s*PatchFirstFire\.Mark\(""tactical\.patch\.layout-observer""\);");
        AssertTrue(scope.Success, "the scope, then the first-fire mark as its first statement");
        AssertTrue(code.Contains("BattleScope.Register("), "per-battle state registers its reset");
        AssertTrue(Regex.IsMatch(code, @"Plugin\.Instance\.Enabled\.Value\s*&&\s*Plugin\.Instance\.EnableTacticalObserver\.Value"), "under Enable Tactical Observer");
        AssertTrue(Regex.IsMatch(code, @"__instance\.IsCampaign"), "the campaign map's group orders are not a battle layout");
        AssertTrue(Regex.IsMatch(code, @"catch \(Exception"), "a failure is caught, never thrown out of the patch");
        AssertTrue(code.Contains("OnceLog.Warning(\"tactical-battle-core:layout-observer-failed\""), "and warned once");
    }

    private static void ReadsWhatDecisionNineSays()
    {
        string code = Code(PatchFile);
        AssertTrue(code.Contains("ordered.lastsetwaypointrotation"), "the facing is the one SetGroupFormation stored after resolving a -1 argument (92067-92068)");
        AssertFalse(code.Contains("manualfinalrotation"), "not the raw argument, which is -1 when the caller wants the group's own facing");
        AssertTrue(Regex.IsMatch(code, @"immediate\s*\?\s*\w+\.GetPosition\(\)\s*:\s*\w+\.lastsetwaypointposition"), "GetPosition() when placed at once, the waypoint otherwise (decision 9)");
        AssertTrue(code.Contains("permanentlydetached") && code.Contains("isrouted"), "vanilla never places a detached or routed unit (92101-92108)");
        AssertTrue(Regex.IsMatch(code, @"\.row\b") && Regex.IsMatch(code, @"\.column\b"), "the layout's own rows and columns (92546-92547)");
        AssertTrue(code.Contains("LayoutMirror.Classify(") && code.Contains("LayoutMirror.Lateral("), "the pure classifier and axis");
    }

    private static void WritesNothing()
    {
        string code = Code(PatchFile);
        AssertFalse(Regex.IsMatch(code, @"\.(formation|formationordered|groupformation|lastformation|ai_stance\w*|lastsetwaypoint\w*|regimentpaths|movementmode|combatbehavior\w*)\s*=(?!=)"),
            "no assignment to a battle field");
        AssertFalse(Regex.IsMatch(code, @"\.(SetFormation|ChangeStance|ChangeCombatBehavior|SetWaypoint|RegimentSetPath|SetNewPosition|PerformActionForGroupForAllUnitsOnLevel)\("), "no vanilla writer");
        AssertFalse(code.Contains("TacticalWriteGuard"), "a record-only observer has no write to guard");
    }

    private static void RowsCarryThePlansFields()
    {
        string code = Code(PatchFile);
        AssertTrue(code.Contains("\"TacticalLayoutMirror\""), "the mirror row");
        foreach (string field in new[] { "depth", "brigades", "agree", "reversed", "immediate" })
            AssertTrue(code.Contains(".WithField(\"" + field + "\""), "mirror field " + field);
        AssertTrue(code.Contains("\"TacticalFormationFlip\""), "the flip row");
        foreach (string field in new[] { "from", "to", "sinceHours", "stance", "ordered", "unittyp", "flips", "hour" })
            AssertTrue(code.Contains(".WithField(\"" + field + "\""), "flip field " + field);
        AssertTrue(code.Contains(".TrySummary(") && code.Contains(".WithField(\"summary\""), "a spent budget keeps a summary row with the running total");
        AssertTrue(code.Contains("OnceLog.Info(\"tactical-battle-core:formation-field-probe\""), "the probe line, once per battle (the tactical key prefix)");
        AssertTrue(code.Contains("\"[FormationProbe] groups=\"") && code.Contains("\" formation3=\"") && code.Contains("\" formationOther=\""), "with the checklist's text (smoke item 37)");
        AssertFalse(Regex.IsMatch(code, @"IsFeudInstance|feud", RegexOptions.IgnoreCase), "the observer does not branch on the W&L feud instance: it records every order");
    }

    private static void ImmediateIsNotCountable()
    {
        string code = Code(PatchFile);
        AssertTrue(Regex.IsMatch(code, @"countable:\s*!immediate"), "an immediate placement (deployment) is only a baseline");
        AssertTrue(code.Contains("TacticalDoctrineTime.NowBattleHours"), "the flip window is on the doctrine clock");
    }

    private static void RowsAreRoutedAndDescribed()
    {
        string policy = ReadRepoFile("src/WhiskeyRealism/Telemetry/TelemetryTagPolicy.cs");
        AssertTrue(Regex.IsMatch(policy, @"AddTactical\(""TacticalLayoutMirror"", TelemetryCategory\.State\)"), "mirror route (State: Trace sheds first and these rows decide A4)");
        AssertTrue(Regex.IsMatch(policy, @"AddTactical\(""TacticalFormationFlip"", TelemetryCategory\.State\)"), "flip route");
        string plugin = ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        Match bind = Regex.Match(plugin, @"""Enable Tactical Observer"",\s*true,(?<d>.*?)\);", RegexOptions.Singleline);
        AssertTrue(bind.Success, "the observer bind");
        AssertTrue(bind.Groups["d"].Value.Contains("TacticalLayoutMirror") && bind.Groups["d"].Value.Contains("TacticalFormationFlip"), "its description names the BC16 rows");
    }

    private static void PureFilesArePure()
    {
        foreach (string file in new[] { "src/WhiskeyRealism/Tactical/Battle/LayoutMirror.cs", "src/WhiskeyRealism/Tactical/Battle/FormationFlipDetector.cs" })
        {
            string code = Code(file);
            AssertFalse(code.Contains("UnityEngine"), file + " names a Unity type");
            AssertFalse(Regex.IsMatch(code, @"realtimeSinceStartup|Environment\.TickCount|DateTime"), file + " reads real time");
        }
    }

    private static void DocsNameTheObserver()
    {
        AssertTrue(ReadRepoFile("docs/patch-catalog.md").Contains("LayoutObserverPatch"), "docs/patch-catalog.md");
        string telemetry = ReadRepoFile("docs/telemetry.md");
        AssertTrue(telemetry.Contains("TacticalLayoutMirror") && telemetry.Contains("TacticalFormationFlip") && telemetry.Contains("[FormationProbe]"), "docs/telemetry.md");
    }
}
