using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Strategic;
using static TestHarness;

// Runtime decisions of the perk-id patches and hooks (flag handling, perks swap and restore,
// compiled field getters), on stand-ins and on the real FogOfWar types.
internal static class PerkIdRuntimeTests
{
    private sealed class FakeUnit
    {
        public List<int> Perks;
    }

    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("PerkHookRemapsOnlyWithFlag", PerkHookRemapsOnlyWithFlag),
            ("PerkPatchDecisionsAreVanillaWithFlagOff", PerkPatchDecisionsAreVanillaWithFlagOff),
            ("PerkPatchDecisionsFollowTableWithFlagOn", PerkPatchDecisionsFollowTableWithFlagOn),
            ("HqFlagResetOnlyForPerkSetFlagInBattle", HqFlagResetOnlyForPerkSetFlagInBattle),
            ("PerkListSwapHidesAndRestores", PerkListSwapHidesAndRestores),
            ("PerkListSwapLeavesListsVanillaReplaced", PerkListSwapLeavesListsVanillaReplaced),
            ("CampaignWoundedSwapAppliesAndRestoresAllThree", CampaignWoundedSwapAppliesAndRestoresAllThree),
            ("CampaignWoundedSwapIgnoresVanillaPlan", CampaignWoundedSwapIgnoresVanillaPlan),
            ("CompiledFieldGetterReadsFogUnitsAndReg", CompiledFieldGetterReadsFogUnitsAndReg),
            ("PerkSwapPatchesRestoreInPrefixAndFinalizer", PerkSwapPatchesRestoreInPrefixAndFinalizer),
            ("PerkIdHooksAndPatchesPassTheirFlag", PerkIdHooksAndPatchesPassTheirFlag),
        });
    }

    private static void PerkHookRemapsOnlyWithFlag()
    {
        AssertEqual(1, VanillaPerkTables.RemapId(true, 2, VanillaPerkTables.BattleSharpshooters), "fix on: Sharpshooters");
        AssertEqual(2, VanillaPerkTables.RemapId(false, 2, VanillaPerkTables.BattleSharpshooters), "fix off: vanilla id");
        AssertEqual(17, VanillaPerkTables.RemapId(true, 10, VanillaPerkTables.CampaignRiverExpedition), "fix on: River Expedition");
        AssertEqual(10, VanillaPerkTables.RemapId(false, 10, VanillaPerkTables.CampaignRiverExpedition), "fix off: vanilla id");
    }

    private static void PerkPatchDecisionsAreVanillaWithFlagOff()
    {
        AssertFalse(VanillaPerkTables.HideExpertScoutsSight(false, 15, true), "sight");
        AssertFalse(VanillaPerkTables.DropBattleConcealment(false, 15, true), "concealment");
        AssertTrue(VanillaPerkTables.GrantMarchPerkExp(false, 17), "march XP");
        AssertTrue(VanillaPerkTables.BattlePerkCounts(false, 15), "Zouave sound");
        AssertFalse(VanillaPerkTables.HideSeaRaiders(false, 15, true), "sea raiders");
        AssertFalse(VanillaPerkTables.HideBattleIdsOnHq(false, 15, true), "HQ swaps");
        AssertFalse(VanillaPerkTables.ResetHqPerkFlag(false, false, 15, true, true, false), "HQ flags");
        AssertFalse(VanillaPerkTables.PlanCampaignWounded(false, 0, 15, true, true, 0.5f, 0.7f).Rewrite, "wounded");
    }

    private static void PerkPatchDecisionsFollowTableWithFlagOn()
    {
        AssertTrue(VanillaPerkTables.HideExpertScoutsSight(true, 15, true), "corps 8 hidden");
        AssertFalse(VanillaPerkTables.HideExpertScoutsSight(true, 1, true), "cavalry Expert Scouts kept");
        AssertFalse(VanillaPerkTables.HideExpertScoutsSight(true, 15, false), "nothing to hide");
        AssertTrue(VanillaPerkTables.DropBattleConcealment(true, 17, true), "fleet 3/8 dropped");
        AssertFalse(VanillaPerkTables.DropBattleConcealment(true, 3, true), "skirmishers kept");
        AssertFalse(VanillaPerkTables.GrantMarchPerkExp(true, 17), "fleet march XP withheld");
        AssertTrue(VanillaPerkTables.GrantMarchPerkExp(true, 15), "corps march XP granted");
        AssertFalse(VanillaPerkTables.BattlePerkCounts(true, 16), "army HQ 2 is Balloon Corps");
        AssertTrue(VanillaPerkTables.BattlePerkCounts(true, 0), "infantry Zouave");
        AssertTrue(VanillaPerkTables.HideSeaRaiders(true, 15, true), "corps 6 hidden");
        AssertFalse(VanillaPerkTables.HideSeaRaiders(true, 17, true), "fleet Sea Raiders kept");
        AssertTrue(VanillaPerkTables.HideBattleIdsOnHq(true, 14, true), "division HQ hidden");
        AssertFalse(VanillaPerkTables.HideBattleIdsOnHq(true, 13, true), "brigade (battle table) kept");
    }

    private static void HqFlagResetOnlyForPerkSetFlagInBattle()
    {
        AssertTrue(VanillaPerkTables.ResetHqPerkFlag(true, false, 15, true, true, false), "corps HQ in battle");
        AssertFalse(VanillaPerkTables.ResetHqPerkFlag(true, true, 15, true, true, false), "campaign map: vanilla never reads it");
        AssertFalse(VanillaPerkTables.ResetHqPerkFlag(true, false, 1, true, true, false), "battle unit keeps its perk");
        AssertFalse(VanillaPerkTables.ResetHqPerkFlag(true, false, 15, true, true, true), "vanilla would set it anyway");
        AssertFalse(VanillaPerkTables.ResetHqPerkFlag(true, false, 15, true, false, false), "not from a perk");
        AssertFalse(VanillaPerkTables.ResetHqPerkFlag(true, false, 15, false, true, false), "flag not set");
    }

    private static void PerkListSwapHidesAndRestores()
    {
        var swap = new PerkListSwap<FakeUnit, int>(u => u.Perks, (u, p) => u.Perks = p);
        var a = new FakeUnit { Perks = new List<int> { 8 } };
        var b = new FakeUnit { Perks = new List<int> { 6, 3 } };
        var listA = a.Perks;
        var listB = b.Perks;
        swap.Hide(a);
        swap.Hide(b);
        AssertTrue(swap.Active, "active while hidden");
        AssertEqual(0, a.Perks.Count, "a hidden");
        AssertEqual(0, b.Perks.Count, "b hidden");
        AssertEqual(null, swap.Restore(), "clean restore");
        AssertTrue(ReferenceEquals(listA, a.Perks), "a restored");
        AssertTrue(ReferenceEquals(listB, b.Perks), "b restored");
        AssertFalse(swap.Active, "inactive after restore");
        AssertEqual(null, swap.Restore(), "second restore is a no-op");
        AssertTrue(ReferenceEquals(listA, a.Perks), "a still restored");
    }

    // If vanilla gave the unit a new list during the call, restore must not overwrite it.
    private static void PerkListSwapLeavesListsVanillaReplaced()
    {
        var swap = new PerkListSwap<FakeUnit, int>(u => u.Perks, (u, p) => u.Perks = p);
        var a = new FakeUnit { Perks = new List<int> { 8 } };
        swap.Hide(a);
        var replaced = new List<int> { 1 };
        a.Perks = replaced;
        swap.Restore();
        AssertTrue(ReferenceEquals(replaced, a.Perks), "vanilla's new list kept");
    }

    private static void CampaignWoundedSwapAppliesAndRestoresAllThree()
    {
        float[] ambulance = { 0.9f, 0.75f, 0.5f };
        float[] foot = { 0.9f, 0.8f, 0.7f };
        var swap = new CampaignWoundedSwap<FakeUnit, int>(
            u => u.Perks, (u, p) => u.Perks = p,
            () => ambulance, f => ambulance = f,
            () => foot, f => foot = f,
            VanillaPerkTables.CampaignAmbulanceCorps, VanillaPerkTables.CampaignFootCavalry);
        var regiment = new FakeUnit { Perks = new List<int> { VanillaPerkTables.BattleExpertScouts } };
        var ownList = regiment.Perks;
        float[] ownAmbulance = ambulance, ownFoot = foot;

        swap.Apply(regiment, new CampaignWoundedPerkPlan(true, 0.5f * 0.75f, 0.8f));
        AssertTrue(swap.Active, "active");
        AssertEqual(2, regiment.Perks.Count, "ids 8 and 6 at level 0");
        AssertTrue(regiment.Perks.Contains(8) && regiment.Perks.Contains(6), "stand-in ids");
        AssertNear(0.375f, ambulance[0], 1e-6f, "ambulance factor at level 0");
        AssertNear(0.8f, foot[0], 1e-6f, "foot cavalry factor at level 0");

        AssertEqual(null, swap.Restore(), "clean restore");
        AssertTrue(ReferenceEquals(ownList, regiment.Perks), "perks restored");
        AssertTrue(ReferenceEquals(ownAmbulance, ambulance), "ambulance array restored");
        AssertTrue(ReferenceEquals(ownFoot, foot), "foot cavalry array restored");
        AssertFalse(swap.Active, "inactive");

        // Only the wrong id to hide: empty list, arrays untouched.
        swap.Apply(regiment, new CampaignWoundedPerkPlan(true, 1f, 1f));
        AssertEqual(0, regiment.Perks.Count, "Expert Scouts hidden");
        AssertTrue(ReferenceEquals(ownAmbulance, ambulance), "ambulance array untouched");
        swap.Restore();
        AssertTrue(ReferenceEquals(ownList, regiment.Perks), "perks restored again");
    }

    private static void CampaignWoundedSwapIgnoresVanillaPlan()
    {
        float[] ambulance = { 0.9f, 0.75f, 0.5f };
        float[] foot = { 0.9f, 0.8f, 0.7f };
        var swap = new CampaignWoundedSwap<FakeUnit, int>(
            u => u.Perks, (u, p) => u.Perks = p, () => ambulance, f => ambulance = f, () => foot, f => foot = f, 8, 6);
        var regiment = new FakeUnit { Perks = new List<int> { 8 } };
        var ownList = regiment.Perks;
        swap.Apply(regiment, CampaignWoundedPerkPlan.Vanilla);
        AssertFalse(swap.Active, "nothing applied");
        AssertTrue(ReferenceEquals(ownList, regiment.Perks), "perks untouched");
    }

    // FogPerkSightPatch reads FogOfWar.units and FOWUnit.reg through compiled getters.
    private static void CompiledFieldGetterReadsFogUnitsAndReg()
    {
        var unitsField = AccessTools.Field(typeof(FogOfWar), "units");
        Type fowUnit = AccessTools.Inner(typeof(FogOfWar), "FOWUnit");
        var regField = AccessTools.Field(fowUnit, "reg");
        AssertTrue(unitsField != null && fowUnit != null && regField != null, "vanilla fields exist");

        Func<object, IList> units = CompiledFieldGetter.Build<IList>(unitsField);
        Func<object, Regiment> reg = CompiledFieldGetter.Build<Regiment>(regField);

        var fog = RuntimeHelpers.GetUninitializedObject(typeof(FogOfWar));
        var list = (IList)Activator.CreateInstance(unitsField.FieldType);
        unitsField.SetValue(fog, list);
        AssertTrue(ReferenceEquals(list, units(fog)), "units read");

        var entry = RuntimeHelpers.GetUninitializedObject(fowUnit);
        var regiment = (Regiment)RuntimeHelpers.GetUninitializedObject(typeof(Regiment));
        regField.SetValue(entry, regiment);
        AssertTrue(ReferenceEquals(regiment, reg(entry)), "reg read");
        AssertEqual(null, CompiledFieldGetter.Build<IList>(null), "missing field");
    }

    // Every patch holding a perks swap restores at the top of its Prefix and in its Finalizer.
    private static void PerkSwapPatchesRestoreInPrefixAndFinalizer()
    {
        string dir = TestFixtures.FindRepoPath("src/WhiskeyRealism/Patches");
        int swaps = 0;
        foreach (string path in Directory.GetFiles(dir, "*.cs"))
        {
            string text = File.ReadAllText(path);
            if (!Regex.IsMatch(text, @"Swap\s*=\s*(PerkIdIlHooks\.NewPerkSwap\(\)|new CampaignWoundedSwap<)")) continue;
            string name = Path.GetFileName(path);
            int declared = Regex.Matches(text, @"Swap\s*=\s*(PerkIdIlHooks\.NewPerkSwap\(\)|new CampaignWoundedSwap<)").Count;
            var finalizers = Regex.Matches(text, @"\[HarmonyFinalizer\][\s\S]*?(?=\[Harmony|$)");
            var prefixes = Regex.Matches(text, @"\[HarmonyPrefix\][\s\S]*?(?=\[Harmony|$)");
            AssertEqual(declared, finalizers.Count, name + ": one Finalizer per swap");
            AssertEqual(declared, prefixes.Count, name + ": one Prefix per swap");
            foreach (Match f in finalizers)
                AssertTrue(f.Value.Contains("Swap.Restore()"), name + ": Finalizer restores");
            foreach (Match p in prefixes)
            {
                int restore = p.Value.IndexOf("Swap.Restore()", StringComparison.Ordinal);
                int hide = Math.Max(p.Value.IndexOf("Swap.Hide(", StringComparison.Ordinal), p.Value.IndexOf("Swap.Apply(", StringComparison.Ordinal));
                AssertTrue(restore >= 0 && hide > restore, name + ": Prefix restores before it hides");
            }
            swaps += declared;
        }
        AssertEqual(5, swaps, "five swap patches: fog sight, group perks, sea raiders, HQ fatigue, HQ cover");
    }

    // The IL hooks and the patches are runtime code outside the harness; each must hand its
    // flag to the pure decision above, so turning a flag off restores vanilla.
    private static void PerkIdHooksAndPatchesPassTheirFlag()
    {
        string dir = TestFixtures.FindRepoPath("src/WhiskeyRealism/Patches");
        string hooks = File.ReadAllText(Path.Combine(dir, "PerkIdIlHooks.cs"));
        foreach (string hook in new[] { "AutocalcSharpshooters", "AutocalcZouave", "AutocalcRiverExpedition", "MoraleColdSteel" })
            AssertTrue(Body(hooks, "public static int " + hook + "(").Contains("RemapId(FixIds(), vanillaId,"), hook + " passes the ids flag");
        AssertTrue(Body(hooks, "public static void MarchPerkExp(").Contains("GrantMarchPerkExp(FixIds(),"), "MarchPerkExp passes the ids flag");
        AssertTrue(Body(hooks, "public static bool BattleTableHasPerk(").Contains("BattlePerkCounts(FixIds(),"), "BattleTableHasPerk passes the ids flag");
        AssertTrue(Body(hooks, "private static bool FootCavalryGate(").Contains("FixIds(), FixScope()"), "Foot Cavalry gate passes both flags");
        AssertTrue(Body(hooks, "public static Regiment FameReportersHolder(").Contains("FixScope()"), "fame holder passes the scope flag");
        AssertTrue(Body(hooks, "public static bool FootCavalrySpeedHas(").Contains("FootCavalryGate(unit)"), "Foot Cavalry hook is gated");
        AssertTrue(Body(hooks, "public static bool FootCavalrySpeedHasParent(").Contains("FootCavalryGate(unit)"), "Foot Cavalry parent hook is gated");
        foreach (string file in new[] { "FogPerkSightPatch.cs", "FogPerkConcealmentPatch.cs", "CampaignGroupPerkPatch.cs", "SeaRaidersBlockadePatch.cs" })
            AssertTrue(File.ReadAllText(Path.Combine(dir, file)).Contains("PerkIdIlHooks.FixIds()"), file + " passes the ids flag");
        AssertEqual(4, Regex.Matches(File.ReadAllText(Path.Combine(dir, "BattleHqPerkPatch.cs")), @"VanillaPerkTables\.\w+\(PerkIdIlHooks\.FixIds\(\)").Count,
            "every BattleHqPerkPatch decision (run, fatigue, cover, engineers) passes the ids flag");
    }

    // Text from a member's signature to the end of its body (brace matched).
    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(start >= 0, "found " + signature);
        int open = source.IndexOf('{', start), depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
        }
        return source.Substring(start);
    }
}
