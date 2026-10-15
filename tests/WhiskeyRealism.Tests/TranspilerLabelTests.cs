using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using static TestHarness;

// HarmonyX 2.10 emits a transpiler's output marking only the labels carried on the returned
// instructions (ILManipulator.WriteTo); a branch whose label is never marked targets a
// detached placeholder (MonoMod CecilILGenerator.LabelInfo). CodeInstruction.Clone() drops
// labels and exception blocks, so a transpiler that edits a Clone() copy loses every branch
// target in the method. These tests run each Transpile helper on the real vanilla IL and
// require every label and block to survive.
internal static class TranspilerLabelTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("transpiler offensive trim keeps every label and block", OffensiveTrimKeepsLabels),
            ("transpiler random pick keeps every label and block", RandomPickKeepsLabels),
            ("transpiler autoresolve weapon gate keeps every label and block", AutoresolveGateKeepsLabels),
            ("transpiler sources never copy instructions with clone", SourcesNeverCloneInstructions),
        });
    }

    private static void OffensiveTrimKeepsLabels()
    {
        var vanilla = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(AICampaign), "CheckOffensiveMovements"));
        var hook = AccessTools.Method(typeof(TranspilerLabelTests), nameof(FloatHookStub));
        var edited = OffensiveWeaponTrimIl.Transpile(vanilla, OffensiveWeaponTrimIl.ResolveWeaponStrengthFactor(),
            OffensiveWeaponTrimIl.ResolveInfluenceField(), hook, out int matches);
        AssertEqual(1, matches, "trim site");
        AssertFalse(ReferenceEquals(edited, vanilla), "edited copy");
        AssertMarksKept(vanilla, edited, "CheckOffensiveMovements");
    }

    private static void RandomPickKeepsLabels()
    {
        var hook = AccessTools.Method(typeof(TranspilerLabelTests), nameof(IntHookStub));
        foreach (var site in RandomPickRangeIl.Sites)
        {
            var vanilla = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(AICampaign), site.Method));
            var edited = RandomPickRangeIl.Transpile(vanilla, site.Method, RandomPickRangeIl.ResolveRandomRangeInt(), hook, out _, out int matches);
            AssertEqual(site.Expected, matches, site.Method + " sites");
            AssertFalse(ReferenceEquals(edited, vanilla), site.Method + " edited copy");
            AssertMarksKept(vanilla, edited, site.Method);
        }
    }

    private static void AutoresolveGateKeepsLabels()
    {
        var vanilla = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Autocalc), "FightUnit"));
        var hook = AccessTools.Method(typeof(TranspilerLabelTests), nameof(IntHookStub));
        var edited = AutoresolveWeaponGateIl.Transpile(vanilla, AutoresolveWeaponGateIl.ResolveWeaponField(), hook, out int matches);
        AssertEqual(AutoresolveWeaponGateIl.ExpectedSites, matches, "FightUnit sites");
        AssertFalse(ReferenceEquals(edited, vanilla), "FightUnit edited copy");
        AssertMarksKept(vanilla, edited, "FightUnit");
    }

    // PerkIdTranspiler needs the game-bound hooks, so it is not compiled here; this sweep
    // covers it and any future transpiler. Comment lines are skipped.
    private static void SourcesNeverCloneInstructions()
    {
        string root = TestFixtures.FindRepoPath("src/WhiskeyRealism");
        int ilFiles = 0;
        foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(path);
            if (!lines.Any(l => l.Contains("CodeInstruction"))) continue;
            ilFiles++;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("//")) continue;
                AssertFalse(line.Contains(".Clone()"), Path.GetFileName(path) + ":" + (i + 1) + " copies a CodeInstruction with Clone(), which drops labels and blocks");
            }
        }
        AssertTrue(ilFiles >= 5, "the sweep sees the transpiler sources");
    }

    private static void AssertMarksKept(List<CodeInstruction> vanilla, List<CodeInstruction> edited, string label)
    {
        AssertEqual(vanilla.Sum(ci => ci.labels.Count), edited.Sum(ci => ci.labels.Count), label + " labels");
        AssertEqual(vanilla.Sum(ci => ci.blocks.Count), edited.Sum(ci => ci.blocks.Count), label + " blocks");
        var marked = new HashSet<Label>(edited.SelectMany(ci => ci.labels));
        foreach (var ci in edited)
        {
            if (ci.operand is Label target) AssertTrue(marked.Contains(target), label + " branch target marked");
            if (ci.operand is Label[] targets) AssertTrue(targets.All(marked.Contains), label + " switch targets marked");
        }
    }

    public static float FloatHookStub(float w)
    {
        return w;
    }

    public static int IntHookStub()
    {
        return 1;
    }
}
