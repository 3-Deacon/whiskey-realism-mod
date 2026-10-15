using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WhiskeyRealism.Patches;
using static TestHarness;

// Perf vanilla allocation trims (perf review M3 and the IsUnitUnderCommander name read).
// The list logic and both IL edits live in Patches/AllocationTrimIl.cs; the edits run
// here on the real vanilla bodies (refs/Assembly-CSharp.dll). The hooks themselves
// need the game, so their shape is pinned from source.
internal static class AllocationTrimTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("allocation trim reused list copy equals tolist", ReusedCopyEqualsToList),
            ("allocation trim reused list is one instance and never the source", ReusedListIsOneInstance),
            ("allocation trim unit list copy off returns a fresh list", CopyOffReturnsFreshList),
            ("allocation trim unit list copy null source throws like tolist", NullSourceThrowsLikeToList),
            ("allocation trim unit list il rewrite swaps the one tolist call", UnitListIlSwapsTheOneCall),
            ("allocation trim unit list il rewrite refuses other counts", UnitListIlRefusesOtherCounts),
            ("allocation trim name read il rewrite drops the one dead compare", NameReadIlDropsTheDeadCompare),
            ("allocation trim name read il rewrite refuses other counts", NameReadIlRefusesOtherCounts),
            ("allocation trim hooks keep vanilla stack shapes and live flags", HooksKeepShapesAndFlags),
        });
    }

    private sealed class Unit
    {
        internal readonly int Id;
        internal Unit(int id) { Id = id; }
    }

    private static List<Unit> Units(int count)
    {
        var list = new List<Unit>(count);
        for (int i = 0; i < count; i++) list.Add(new Unit(i));
        return list;
    }

    private static void AssertSameSequence(List<Unit> expected, List<Unit> actual, string label)
    {
        AssertEqual(expected.Count, actual.Count, label + " count");
        for (int i = 0; i < expected.Count; i++)
            AssertTrue(ReferenceEquals(expected[i], actual[i]), label + " element " + i);
    }

    // Fixtures: empty, one, duplicates (the same reference twice and three times), null
    // entries (destroyed Regiments read as null), a large list, then a shorter list after
    // a longer one so a missed Clear shows.
    private static void ReusedCopyEqualsToList()
    {
        var a = new Unit(1);
        var b = new Unit(2);
        var fixtures = new List<List<Unit>>
        {
            new List<Unit>(),
            new List<Unit> { a },
            new List<Unit> { a, b, a, a, b },
            new List<Unit> { null, a, null, b, null },
            Units(26000),
            Units(3000),
            new List<Unit> { b },
            new List<Unit>(),
        };
        var reused = new List<Unit>();
        foreach (var source in fixtures)
        {
            var expected = source.ToList();
            var actual = AllocationTrimIl.CopyList(reused, source, true);
            AssertSameSequence(expected, actual, "fixture of " + source.Count);
            AssertSameSequence(expected, source, "source untouched " + source.Count);
        }
        var lazy = Enumerable.Range(0, 5).Select(i => i % 2 == 0 ? a : b);
        AssertSameSequence(lazy.ToList(), AllocationTrimIl.CopyList(reused, lazy, true), "non-collection source");
    }

    private static void ReusedListIsOneInstance()
    {
        var reused = new List<Unit>();
        var first = Units(10);
        var second = Units(4);
        var r1 = AllocationTrimIl.CopyList(reused, first, true);
        var r2 = AllocationTrimIl.CopyList(reused, second, true);
        AssertTrue(ReferenceEquals(reused, r1) && ReferenceEquals(reused, r2), "the reused list is handed back each pass");
        AssertFalse(ReferenceEquals(first, r1) || ReferenceEquals(second, r2), "never the source list");
        AssertEqual(4, r2.Count, "refilled, not appended");
    }

    private static void CopyOffReturnsFreshList()
    {
        var reused = new List<Unit> { new Unit(99) };
        var source = Units(50);
        var r1 = AllocationTrimIl.CopyList(reused, source, false);
        var r2 = AllocationTrimIl.CopyList(reused, source, false);
        AssertFalse(ReferenceEquals(r1, r2), "a fresh list every pass");
        AssertFalse(ReferenceEquals(r1, reused) || ReferenceEquals(r1, source), "neither the reused list nor the source");
        AssertSameSequence(source.ToList(), r1, "same elements as ToList");
        AssertEqual(1, reused.Count, "reused list untouched while off");
        var noBuffer = AllocationTrimIl.CopyList(null, source, true);
        AssertFalse(ReferenceEquals(noBuffer, source), "no reused list: a fresh copy");
        AssertSameSequence(source, noBuffer, "no reused list: same elements");
    }

    private static void NullSourceThrowsLikeToList()
    {
        AssertThrowsArgumentNull(() => Enumerable.ToList<Unit>(null), "vanilla ToList");
        AssertThrowsArgumentNull(() => AllocationTrimIl.CopyList(new List<Unit>(), null, true), "reuse on");
        AssertThrowsArgumentNull(() => AllocationTrimIl.CopyList(new List<Unit>(), null, false), "reuse off");
    }

    private static void AssertThrowsArgumentNull(Action action, string label)
    {
        try
        {
            action();
        }
        catch (ArgumentNullException)
        {
            return;
        }
        throw new Exception(label + ": expected ArgumentNullException");
    }

    // AICampaign.UpdateUnitAI (11310): ldsfld completeunitlist; call ToList<Regiment>; stloc.1.
    private static void UnitListIlSwapsTheOneCall()
    {
        var method = AccessTools.Method(typeof(AICampaign), "UpdateUnitAI");
        AssertTrue(method != null, "UpdateUnitAI resolves");
        var vanilla = PatchProcessor.GetOriginalInstructions(method);
        var toList = AllocationTrimIl.ResolveToListOfRegiment();
        var field = AllocationTrimIl.ResolveCompleteUnitList();
        AssertTrue(toList != null && field != null, "ToList<Regiment> and completeunitlist resolve");
        var hook = AccessTools.Method(typeof(AllocationTrimTests), nameof(UnitListHookStub));
        var edited = AllocationTrimIl.TranspileUnitListCopy(vanilla, field, toList, hook, out int matches);
        AssertEqual(1, matches, "exactly one ToList site");
        AssertFalse(ReferenceEquals(edited, vanilla), "edited copy returned");
        AssertEqual(vanilla.Count, edited.Count, "no instruction added or removed");
        int at = vanilla.FindIndex(ci => ci.Calls(toList));
        AssertTrue(at > 0 && vanilla[at - 1].LoadsField(field) && vanilla[at + 1].IsStloc(), "site is completeunitlist.ToList() into a local");
        AssertEqual(0, vanilla.Count(ci => ci.Calls(hook)), "vanilla list untouched");
        for (int i = 0; i < vanilla.Count; i++)
        {
            if (i == at)
            {
                AssertTrue(edited[i].opcode == OpCodes.Call && Equals(edited[i].operand, hook), "ToList call swapped for the hook");
                AssertEqual(vanilla[i].labels.Count, edited[i].labels.Count, "site labels kept");
                continue;
            }
            AssertSameInstruction(vanilla[i], edited[i], "UpdateUnitAI @" + i);
        }
        AssertEqual(0, edited.Count(ci => ci.Calls(toList)), "no ToList left");
        AssertEqual(vanilla.Sum(ci => ci.labels.Count), edited.Sum(ci => ci.labels.Count), "no label lost");
        AssertEqual(vanilla.Sum(ci => ci.blocks.Count), edited.Sum(ci => ci.blocks.Count), "no block lost");
    }

    private static void UnitListIlRefusesOtherCounts()
    {
        var toList = AllocationTrimIl.ResolveToListOfRegiment();
        var field = AllocationTrimIl.ResolveCompleteUnitList();
        var hook = AccessTools.Method(typeof(AllocationTrimTests), nameof(UnitListHookStub));

        var other = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(AICampaign), "CheckOffensiveMovements"));
        var same = AllocationTrimIl.TranspileUnitListCopy(other, field, toList, hook, out int none);
        AssertEqual(0, none, "no site in CheckOffensiveMovements");
        AssertTrue(ReferenceEquals(other, same), "vanilla instance handed back");

        var twice = UnitListShape(field, toList);
        twice.AddRange(UnitListShape(field, toList));
        var snapshot = twice.Select(ci => ci.operand).ToList();
        var back = AllocationTrimIl.TranspileUnitListCopy(twice, field, toList, hook, out int two);
        AssertEqual(2, two, "two sites reported");
        AssertTrue(ReferenceEquals(twice, back), "two sites: vanilla instance handed back");
        AssertTrue(snapshot.SequenceEqual(twice.Select(ci => ci.operand)), "two sites: nothing rewritten");
        AssertEqual(2, AllocationTrimIl.RewriteUnitListCopy(twice, field, toList, hook), "Rewrite reports two");
        AssertEqual(0, twice.Count(ci => ci.Calls(hook)), "Rewrite edits nothing on two");

        var noStore = UnitListShape(field, toList);
        noStore[2] = new CodeInstruction(OpCodes.Pop);
        AssertEqual(0, AllocationTrimIl.RewriteUnitListCopy(noStore, field, toList, hook), "a copy not stored to a local is not the site");
        AssertEqual(0, AllocationTrimIl.RewriteUnitListCopy(UnitListShape(field, toList), field, toList, null), "no hook, no edit");
    }

    private static List<CodeInstruction> UnitListShape(FieldInfo field, MethodInfo toList)
    {
        return new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Ldsfld, field),
            new CodeInstruction(OpCodes.Call, toList),
            new CodeInstruction(OpCodes.Stloc_1),
        };
    }

    public static List<Regiment> UnitListHookStub(IEnumerable<Regiment> source)
    {
        return source.ToList();
    }

    // BattleUnits.IsUnitUnderCommander (91186): ldarg.1 (branch target);
    // callvirt Object::get_name; ldstr "1st Delaware Sharpshooters"; call op_Equality; pop.
    private static void NameReadIlDropsTheDeadCompare()
    {
        var method = AccessTools.Method(typeof(BattleUnits), "IsUnitUnderCommander");
        AssertTrue(method != null, "IsUnitUnderCommander resolves");
        var vanilla = PatchProcessor.GetOriginalInstructions(method);
        var getName = AllocationTrimIl.ResolveObjectNameGetter();
        var equality = AllocationTrimIl.ResolveStringEquality();
        AssertTrue(getName != null && equality != null, "Object.name getter and string == resolve");
        var hook = AccessTools.Method(typeof(AllocationTrimTests), nameof(NameHookStub));
        var edited = AllocationTrimIl.TranspileNameRead(vanilla, getName, equality, hook, out int matches);
        AssertEqual(1, matches, "exactly one dead name read");
        AssertFalse(ReferenceEquals(edited, vanilla), "edited copy returned");
        AssertEqual(vanilla.Count - 3, edited.Count, "three instructions removed");

        int at = vanilla.FindIndex(ci => ci.Calls(getName));
        AssertTrue(at > 0 && vanilla[at - 1].opcode == OpCodes.Ldarg_1, "the unit argument is loaded before the read");
        AssertTrue(vanilla[at - 1].labels.Count == 1, "that load is the null check's branch target");
        AssertTrue(vanilla[at + 1].opcode == OpCodes.Ldstr && vanilla[at + 2].Calls(equality) && vanilla[at + 3].opcode == OpCodes.Pop, "compare discarded");
        for (int i = 0; i < at; i++) AssertSameInstruction(vanilla[i], edited[i], "IsUnitUnderCommander @" + i);
        AssertTrue(edited[at].opcode == OpCodes.Call && Equals(edited[at].operand, hook), "name read swapped for the hook");
        for (int i = at + 4; i < vanilla.Count; i++) AssertSameInstruction(vanilla[i], edited[i - 3], "IsUnitUnderCommander @" + i);

        AssertEqual(vanilla.Sum(ci => ci.labels.Count), edited.Sum(ci => ci.labels.Count), "no label lost");
        AssertEqual(vanilla.Sum(ci => ci.blocks.Count), edited.Sum(ci => ci.blocks.Count), "no block lost");
        AssertEqual(0, edited.Count(ci => ci.Calls(getName)), "no name read left");
        AssertEqual(0, edited.Count(ci => ci.opcode == OpCodes.Ldstr), "no literal left");
        AssertEqual(1, vanilla.Count(ci => ci.Calls(getName)), "vanilla list untouched");
    }

    private static void NameReadIlRefusesOtherCounts()
    {
        var getName = AllocationTrimIl.ResolveObjectNameGetter();
        var equality = AllocationTrimIl.ResolveStringEquality();
        var hook = AccessTools.Method(typeof(AllocationTrimTests), nameof(NameHookStub));

        var other = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(BattleUnits), "UpdateUnitUnderCommanderAll"));
        var same = AllocationTrimIl.TranspileNameRead(other, getName, equality, hook, out int none);
        AssertEqual(0, none, "no site in UpdateUnitUnderCommanderAll");
        AssertTrue(ReferenceEquals(other, same), "vanilla instance handed back");

        var twice = NameShape(getName, equality, AllocationTrimIl.DeadNameLiteral);
        twice.AddRange(NameShape(getName, equality, AllocationTrimIl.DeadNameLiteral));
        var back = AllocationTrimIl.TranspileNameRead(twice, getName, equality, hook, out int two);
        AssertEqual(2, two, "two sites reported");
        AssertTrue(ReferenceEquals(twice, back) && twice.Count == 10, "two sites: vanilla handed back untouched");
        AssertEqual(2, AllocationTrimIl.RewriteNameRead(twice, getName, equality, hook), "Rewrite reports two");
        AssertEqual(10, twice.Count, "Rewrite edits nothing on two");

        AssertEqual(0, AllocationTrimIl.RewriteNameRead(NameShape(getName, equality, "Some Other Unit"), getName, equality, hook), "another literal is not the dead read");

        var kept = NameShape(getName, equality, AllocationTrimIl.DeadNameLiteral);
        kept[4] = new CodeInstruction(OpCodes.Stloc_0);
        AssertEqual(0, AllocationTrimIl.RewriteNameRead(kept, getName, equality, hook), "a stored compare is not dead");

        var labelled = NameShape(getName, equality, AllocationTrimIl.DeadNameLiteral);
        labelled[4].labels.Add(new Label());
        AssertEqual(0, AllocationTrimIl.RewriteNameRead(labelled, getName, equality, hook), "a branch target inside the compare is not removed");
        AssertEqual(5, labelled.Count, "labelled shape untouched");

        var exact = NameShape(getName, equality, AllocationTrimIl.DeadNameLiteral);
        AssertEqual(1, AllocationTrimIl.RewriteNameRead(exact, getName, equality, hook), "exact shape");
        AssertEqual(2, exact.Count, "load kept, hook in place of the read");
        AssertTrue(exact[0].opcode == OpCodes.Ldarg_1 && exact[1].Calls(hook), "ldarg.1; call hook");
    }

    private static List<CodeInstruction> NameShape(MethodInfo getName, MethodInfo equality, string literal)
    {
        return new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Callvirt, getName),
            new CodeInstruction(OpCodes.Ldstr, literal),
            new CodeInstruction(OpCodes.Call, equality),
            new CodeInstruction(OpCodes.Pop),
        };
    }

    public static void NameHookStub(UnityEngine.Object unit)
    {
    }

    private static void AssertSameInstruction(CodeInstruction expected, CodeInstruction actual, string label)
    {
        AssertTrue(expected.opcode == actual.opcode, label + " opcode " + expected.opcode + " vs " + actual.opcode);
        AssertTrue(Equals(expected.operand, actual.operand), label + " operand");
        AssertEqual(expected.labels.Count, actual.labels.Count, label + " labels");
        AssertEqual(expected.blocks.Count, actual.blocks.Count, label + " blocks");
    }

    // The hooks need the game (Plugin, Regiment), so their contract is pinned from source:
    // the same stack input as the vanilla instruction they replace, the live flag, the
    // vanilla fallback when off, and a first-fire mark.
    private static void HooksKeepShapesAndFlags()
    {
        string list = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Patches/CampaignUnitListReusePatch.cs");
        AssertContains(list, "[HarmonyPatch(typeof(AICampaign), \"UpdateUnitAI\")]", "targets UpdateUnitAI");
        AssertContains(list, "public static List<Regiment> CompleteUnitListCopy(IEnumerable<Regiment> source)", "hook takes ToList's argument and returns its type");
        AssertContains(list, "PatchFirstFire.Mark(\"campaign.patch.unit-list-reuse.hook\");", "hook marks first fire");
        AssertContains(list, "if (source != null && Enabled())", "reuse only with a list and the flag on");
        AssertContains(list, "return Enumerable.ToList(source);", "otherwise vanilla's ToList");
        AssertContains(list, "Plugin.ReuseCampaignAiUnitList.Value", "flag read live");
        AssertContains(list, "CampaignScope.Register(\"campaign-unit-list-reuse\", () => _reused = null);", "reused list dropped between campaigns");

        string name = TestFixtures.ReadRepoFile("src/WhiskeyRealism/Patches/WlUnitNameReadPatch.cs");
        AssertContains(name, "[HarmonyPatch(typeof(BattleUnits), \"IsUnitUnderCommander\")]", "targets IsUnitUnderCommander");
        AssertContains(name, "public static void UnitNameRead(UnityEngine.Object unit)", "hook consumes the loaded unit and returns nothing");
        AssertContains(name, "PatchFirstFire.Mark(\"campaign.patch.wl-unit-name-read.hook\");", "hook marks first fire");
        AssertContains(name, "if (!Enabled())\n                _ = unit.name == AllocationTrimIl.DeadNameLiteral;", "flag off repeats vanilla's read");
        AssertContains(name, "Plugin.SkipDeadWlUnitNameRead.Value", "flag read live");
    }
}
