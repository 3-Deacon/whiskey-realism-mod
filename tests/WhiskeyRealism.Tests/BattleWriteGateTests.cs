using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

// Release-mode contract: Tactical Commander Mode MonitorOnly/Off writes nothing
// into vanilla battle state. Every source file that issues a vanilla battle
// write must consult the commander-mode gate (TacticalWriteGuard.CanWrite /
// ModeAllowsWrites, or TacticalCommanderModePolicy.AllowsWrites). File-level:
// it catches a new writer that never looks at the mode, not a misplaced check.
internal static class BattleWriteGateTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("battle write gate flags a vanilla write without a mode check", FlagsUngatedWriter),
            ("battle write gate accepts a vanilla write behind a mode check", AcceptsGatedWriter),
            ("battle write gate ignores writes named only in comments", IgnoresComments),
            ("every tactical vanilla battle writer checks commander mode", EveryWriterChecksCommanderMode),
        });
    }

    private static readonly Regex VanillaBattleWrite = new Regex(
        @"\.(ChangeStance|ChangeCombatBehavior|SetWaypoint|DeleteOrderQueue|AddToOrderQueue|ChangeRegimentFormation|SetGroupFormation|SetFormation|RegimentSetPath|PerformActionForGroupForAllUnitsOnLevel|CheckCurrentOrderUpdate|MountUnits|RotateRegiment)\(|\.macroai\s*=(?!=)");

    private static readonly Regex CommanderModeGate = new Regex(
        @"TacticalWriteGuard\.(CanWrite|ModeAllowsWrites)\(|TacticalCommanderModePolicy\.AllowsWrites\(");

    private static readonly Regex LineComment = new Regex(@"//[^\n]*");

    internal static bool IsUngatedWriter(string source)
    {
        string code = LineComment.Replace(source ?? string.Empty, string.Empty);
        return VanillaBattleWrite.IsMatch(code) && !CommanderModeGate.IsMatch(code);
    }

    private static void FlagsUngatedWriter()
    {
        TestHarness.AssertTrue(IsUngatedWriter("bunits.ChangeStance(go, 2);"), "stance write");
        TestHarness.AssertTrue(IsUngatedWriter("battle.macroai = write;"), "macroai write");
        TestHarness.AssertTrue(IsUngatedWriter("bunits.SetFormation(go, 2, true);"), "formation write");
        TestHarness.AssertTrue(IsUngatedWriter("bunits.PerformActionForGroupForAllUnitsOnLevel(go, -1, \"StopRegiment\", p);"), "group action write");
        TestHarness.AssertTrue(IsUngatedWriter("bunits.CheckCurrentOrderUpdate(reg, 3, p);"), "player dispatch write");
    }

    private static void AcceptsGatedWriter()
    {
        TestHarness.AssertFalse(IsUngatedWriter(
            "if (!TacticalWriteGuard.CanWrite(unit)) return; bunits.ChangeCombatBehavior(go, 7);"), "CanWrite");
        TestHarness.AssertFalse(IsUngatedWriter(
            "bool ok = TacticalCommanderModePolicy.AllowsWrites(mode); battle.macroai = 2;"), "policy");
        TestHarness.AssertFalse(IsUngatedWriter("if (battle.macroai == 3) return;"), "comparison is not a write");
    }

    private static void IgnoresComments()
    {
        TestHarness.AssertFalse(IsUngatedWriter("// vanilla calls bunits.ChangeStance(go, 2) here"), "comment-only write");
        TestHarness.AssertTrue(IsUngatedWriter(
            "// TacticalWriteGuard.CanWrite(unit)\nbunits.SetWaypoint(unit, p);"), "gate named only in a comment");
    }

    private static void EveryWriterChecksCommanderMode()
    {
        var ungated = new List<string>();
        int writers = 0;
        string src = TestFixtures.FindRepoPath("src/WhiskeyRealism");
        foreach (string dir in new[] { "Patches", "Tactical", "Util" })
        {
            foreach (string path in Directory.GetFiles(Path.Combine(src, dir), "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(path);
                if (!VanillaBattleWrite.IsMatch(LineComment.Replace(text, string.Empty))) continue;
                writers++;
                if (IsUngatedWriter(text)) ungated.Add(Path.GetFileName(path));
            }
        }

        TestHarness.AssertTrue(writers > 0, "found the vanilla battle writers");
        TestHarness.AssertEqual(0, ungated.Count, "writers without a commander-mode check: " + string.Join(", ", ungated));
    }
}
