using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Strategic;
using static TestHarness;

// BUG-AI-006..010: the perk-id transpilers' IL edits, run on the real game IL from
// refs/Assembly-CSharp.dll, plus the pure rules behind their hooks.
internal static class PerkIdIlTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("PerkIdConstantsMatchTooltipFiles", PerkIdConstantsMatchTooltipFiles),
            ("FootCavalrySpeedOnlyForLandUnitsOnForcedMarch", FootCavalrySpeedOnlyForLandUnitsOnForcedMarch),
            ("MarchPerkExpOnlyForCampaignGroups", MarchPerkExpOnlyForCampaignGroups),
            ("SeaRaidersOnlyForFleets", SeaRaidersOnlyForFleets),
            ("FameReportersHolderIsUnitThenAncestor", FameReportersHolderIsUnitThenAncestor),
            ("FightUnitIlRemapsSharpshooterZouaveAndRiverExpedition", FightUnitIlRemapsSharpshooterZouaveAndRiverExpedition),
            ("AutocalcFatigueIlRemapsZouave", AutocalcFatigueIlRemapsZouave),
            ("UpdateMoraleIlRemapsColdSteel", UpdateMoraleIlRemapsColdSteel),
            ("RegimentMoveIlHooksFootCavalryXpAndZouaveSound", RegimentMoveIlHooksFootCavalryXpAndZouaveSound),
            ("CommanderFameIlHooksReportersHolderAndLevel", CommanderFameIlHooksReportersHolderAndLevel),
            ("IlRewritesLeaveVanillaOnPatternMiss", IlRewritesLeaveVanillaOnPatternMiss),
        });
    }

    // Stand-ins with the hooks' stack shapes.
    public static int RemapA(int id) => id;
    public static int RemapB(int id) => id;
    public static int RemapC(int id) => id;
    public static bool HasA(Regiment unit, int perk) => false;
    public static bool HasB(Regiment unit, int perk) => false;
    public static bool HasC(Regiment unit, int perk) => false;
    public static void Exp(Regiment unit, float exp, int perk) { }
    public static Regiment Find(BattleUnits units, Regiment unit, int perk) => null;
    public static int Level(Regiment unit, int perk) => 0;

    private static MethodInfo M(string name) => AccessTools.Method(typeof(PerkIdIlTests), name);

    private static List<CodeInstruction> Il(Type type, string method)
    {
        var original = AccessTools.Method(type, method);
        AssertTrue(original != null, type.Name + "." + method + " exists");
        return PatchProcessor.ReadMethodBody(original).Select(kv => new CodeInstruction(kv.Key, kv.Value)).ToList();
    }

    private static int Calls(List<CodeInstruction> code, MethodInfo method) => code.Count(i => i.Calls(method));

    // The remap call sits between `ldc.i4 id` and the call it feeds.
    private static int RemappedSites(List<CodeInstruction> code, MethodInfo remap, MethodInfo target, int id)
    {
        int n = 0;
        for (int i = 1; i + 1 < code.Count; i++)
            if (code[i].Calls(remap) && code[i - 1].LoadsConstant(id) && code[i + 1].Calls(target)) n++;
        return n;
    }

    // PerkBattleTooltips.txt (0/1 Engineering ... 18 Sapper gunners), PerkTooltipsArmyCampaign.txt
    // (5 Flying Column, 6 Foot Cavalry, 10 Sappers and Miners, 14 Embedded reporters, 17 River
    // Expedition), PerkFleetsTooltips.txt (6 Sea Raiders).
    private static void PerkIdConstantsMatchTooltipFiles()
    {
        AssertEqual(0, VanillaPerkTables.BattleEngineering, "Engineering");
        AssertEqual(1, VanillaPerkTables.BattleSharpshooters, "Sharpshooters");
        AssertEqual(2, VanillaPerkTables.BattleZouave, "Zouave");
        AssertEqual(9, VanillaPerkTables.BattleColdSteel, "Cold steel");
        AssertEqual(11, VanillaPerkTables.BattleStormers, "Stormers");
        AssertEqual(13, VanillaPerkTables.BattleLightningBrigade, "Lightning brigade");
        AssertEqual(18, VanillaPerkTables.BattleSapperGunners, "Sapper gunners");
        AssertEqual(5, VanillaPerkTables.CampaignFlyingColumn, "Flying Column");
        AssertEqual(10, VanillaPerkTables.CampaignSappersAndMiners, "Sappers and Miners");
        AssertEqual(14, VanillaPerkTables.CampaignEmbeddedReporters, "Embedded reporters");
        AssertEqual(17, VanillaPerkTables.CampaignRiverExpedition, "River Expedition");
        AssertEqual(6, VanillaPerkTables.FleetSeaRaiders, "Sea Raiders");
    }

    private static void FootCavalrySpeedOnlyForLandUnitsOnForcedMarch()
    {
        AssertTrue(VanillaPerkTables.FootCavalrySpeedApplies(15, forcedMarch: true, fixIds: true, fixScope: true), "corps on forced march");
        AssertFalse(VanillaPerkTables.FootCavalrySpeedApplies(15, forcedMarch: false, fixIds: true, fixScope: true), "corps on a normal march");
        AssertFalse(VanillaPerkTables.FootCavalrySpeedApplies(17, forcedMarch: true, fixIds: true, fixScope: true), "fleet 6 is Sea Raiders");
        AssertTrue(VanillaPerkTables.FootCavalrySpeedApplies(17, forcedMarch: false, fixIds: false, fixScope: false), "both flags off = vanilla");
        AssertTrue(VanillaPerkTables.FootCavalrySpeedApplies(15, forcedMarch: false, fixIds: true, fixScope: false), "scope flag off = every move");
        AssertFalse(VanillaPerkTables.FootCavalrySpeedApplies(0, forcedMarch: true, fixIds: true, fixScope: false), "a regiment's 6 is Ace of Spade");
    }

    private static void MarchPerkExpOnlyForCampaignGroups()
    {
        AssertTrue(VanillaPerkTables.MarchPerkExpApplies(14), "division");
        AssertTrue(VanillaPerkTables.MarchPerkExpApplies(16), "army");
        AssertFalse(VanillaPerkTables.MarchPerkExpApplies(17), "fleet 5/6 = Blockading Squadron / Sea Raiders");
        AssertFalse(VanillaPerkTables.MarchPerkExpApplies(0), "regiment 5/6 = Deadly Volley / Ace of Spade");
    }

    private static void SeaRaidersOnlyForFleets()
    {
        AssertTrue(VanillaPerkTables.SeaRaidersApplies(17), "fleet");
        AssertFalse(VanillaPerkTables.SeaRaidersApplies(15), "corps 6 = Foot Cavalry");
        AssertFalse(VanillaPerkTables.SeaRaidersApplies(1), "regiment 6 = Ace of Spade");
    }

    private static void FameReportersHolderIsUnitThenAncestor()
    {
        AssertEqual(PerkHolder.Self, VanillaPerkTables.FameReportersHolder(true, false, fixScope: true), "own perk counts");
        AssertEqual(PerkHolder.Self, VanillaPerkTables.FameReportersHolder(true, true, fixScope: true), "own perk first");
        AssertEqual(PerkHolder.Ancestor, VanillaPerkTables.FameReportersHolder(false, true, fixScope: true), "army's perk at the army's level");
        AssertEqual(PerkHolder.None, VanillaPerkTables.FameReportersHolder(false, false, fixScope: true), "none");
        AssertEqual(PerkHolder.None, VanillaPerkTables.FameReportersHolder(true, false, fixScope: false), "vanilla skips the unit");
        AssertEqual(PerkHolder.AncestorWithOwnLevel, VanillaPerkTables.FameReportersHolder(true, true, fixScope: false), "vanilla reads the unit's level");
    }

    private static void FightUnitIlRemapsSharpshooterZouaveAndRiverExpedition()
    {
        var code = Il(typeof(Autocalc), "FightUnit");
        AssertTrue(PerkIdIlRewrites.RewriteFightUnit(code, M(nameof(RemapA)), M(nameof(RemapB)), M(nameof(RemapC))), "rewritten");
        AssertEqual(1, RemappedSites(code, M(nameof(RemapA)), PerkIdIlRewrites.HasPerk, 2), "unit0.HasPerk(2) -> Sharpshooters");
        AssertEqual(1, RemappedSites(code, M(nameof(RemapA)), PerkIdIlRewrites.GetPerkLevel, 2), "unit0.GetPerkLevel(2) -> Sharpshooters");
        AssertEqual(1, RemappedSites(code, M(nameof(RemapB)), PerkIdIlRewrites.HasPerk, 1), "unit1.HasPerk(1) -> Zouave");
        AssertEqual(1, RemappedSites(code, M(nameof(RemapC)), PerkIdIlRewrites.HasPerk, 10), "embarked HasPerk(10) -> River Expedition");
        AssertEqual(1, PerkIdIlRewrites.Sites(code, PerkIdIlRewrites.HasPerk, 10).Count, "siege-assault HasPerk(10) (21100) untouched");
        AssertEqual(4, Calls(code, M(nameof(RemapA))) + Calls(code, M(nameof(RemapB))) + Calls(code, M(nameof(RemapC))), "four edits");
    }

    private static void AutocalcFatigueIlRemapsZouave()
    {
        var code = Il(typeof(Autocalc), "UpdateFatigue");
        AssertTrue(PerkIdIlRewrites.RewriteAutocalcFatigue(code, M(nameof(RemapB))), "rewritten");
        AssertEqual(1, RemappedSites(code, M(nameof(RemapB)), PerkIdIlRewrites.HasPerk, 1), "HasPerk(1) -> Zouave");
        AssertEqual(1, RemappedSites(code, M(nameof(RemapB)), PerkIdIlRewrites.GetPerkLevel, 1), "GetPerkLevel(1) -> Zouave");
    }

    private static void UpdateMoraleIlRemapsColdSteel()
    {
        var code = Il(typeof(Regiment), "UpdateMorale");
        AssertTrue(PerkIdIlRewrites.RewriteUpdateMorale(code, M(nameof(RemapA))), "rewritten");
        AssertEqual(1, RemappedSites(code, M(nameof(RemapA)), PerkIdIlRewrites.HasPerk, 8), "HasPerk(8) -> Cold Steel");
        AssertEqual(1, Calls(code, M(nameof(RemapA))), "one edit");
    }

    private static void RegimentMoveIlHooksFootCavalryXpAndZouaveSound()
    {
        var code = Il(typeof(Regiment), nameof(Regiment.RegimentMove));
        AssertTrue(PerkIdIlRewrites.RewriteRegimentMove(code, M(nameof(HasA)), M(nameof(HasB)), M(nameof(Exp)), M(nameof(HasC))), "rewritten");
        AssertEqual(1, PerkIdIlRewrites.Sites(code, M(nameof(HasA)), 6).Count, "Foot Cavalry HasPerk(6)");
        AssertEqual(1, PerkIdIlRewrites.Sites(code, M(nameof(HasB)), 6).Count, "Foot Cavalry HasPerkParent(6)");
        AssertEqual(1, PerkIdIlRewrites.Sites(code, M(nameof(Exp)), 5).Count, "Flying Column XP");
        AssertEqual(1, PerkIdIlRewrites.Sites(code, M(nameof(Exp)), 6).Count, "Foot Cavalry XP");
        AssertEqual(2, PerkIdIlRewrites.Sites(code, M(nameof(HasC)), 2).Count, "Zouave running sound");
        AssertEqual(1, PerkIdIlRewrites.Sites(code, PerkIdIlRewrites.HasPerk, 2).Count, "fleet Supply Colliers HasPerk(2) (133405) untouched");
        AssertEqual(0, PerkIdIlRewrites.Sites(code, PerkIdIlRewrites.HasPerk, 6).Count, "no vanilla HasPerk(6) left");
        AssertEqual(1, PerkIdIlRewrites.Sites(code, PerkIdIlRewrites.IncreasePerkExp, 17).Count, "River Expedition XP untouched");
    }

    private static void CommanderFameIlHooksReportersHolderAndLevel()
    {
        var code = Il(typeof(BattleUnits), nameof(BattleUnits.CalculateCommanderFame));
        AssertTrue(PerkIdIlRewrites.RewriteCommanderFame(code, M(nameof(Find)), M(nameof(Level))), "rewritten");
        AssertEqual(1, PerkIdIlRewrites.Sites(code, M(nameof(Find)), 14).Count, "holder lookup hooked");
        AssertEqual(1, PerkIdIlRewrites.Sites(code, M(nameof(Level)), 14).Count, "level read hooked");
        AssertEqual(0, Calls(code, PerkIdIlRewrites.CheckForGroupPerk), "vanilla lookup gone");
    }

    // A game update that moves or drops one site must leave the whole method as vanilla.
    private static void IlRewritesLeaveVanillaOnPatternMiss()
    {
        AssertMissLeavesVanilla(Il(typeof(Autocalc), "FightUnit"), PerkIdIlRewrites.HasPerk, 1,
            c => PerkIdIlRewrites.RewriteFightUnit(c, M(nameof(RemapA)), M(nameof(RemapB)), M(nameof(RemapC))), "FightUnit");
        AssertMissLeavesVanilla(Il(typeof(Autocalc), "UpdateFatigue"), PerkIdIlRewrites.GetPerkLevel, 1,
            c => PerkIdIlRewrites.RewriteAutocalcFatigue(c, M(nameof(RemapB))), "Autocalc.UpdateFatigue");
        AssertMissLeavesVanilla(Il(typeof(Regiment), "UpdateMorale"), PerkIdIlRewrites.HasPerk, 8,
            c => PerkIdIlRewrites.RewriteUpdateMorale(c, M(nameof(RemapA))), "UpdateMorale");
        AssertMissLeavesVanilla(Il(typeof(Regiment), nameof(Regiment.RegimentMove)), PerkIdIlRewrites.IncreasePerkExp, 5,
            c => PerkIdIlRewrites.RewriteRegimentMove(c, M(nameof(HasA)), M(nameof(HasB)), M(nameof(Exp)), M(nameof(HasC))), "RegimentMove");
        AssertMissLeavesVanilla(Il(typeof(BattleUnits), nameof(BattleUnits.CalculateCommanderFame)), PerkIdIlRewrites.GetPerkLevel, 14,
            c => PerkIdIlRewrites.RewriteCommanderFame(c, M(nameof(Find)), M(nameof(Level))), "CalculateCommanderFame");
    }

    // Breaks one expected site (its constant becomes 99) and checks nothing else changed.
    private static void AssertMissLeavesVanilla(List<CodeInstruction> code, MethodInfo target, int id, Func<List<CodeInstruction>, bool> rewrite, string label)
    {
        var site = PerkIdIlRewrites.Sites(code, target, id);
        AssertTrue(site.Count > 0, label + ": site to break exists");
        code[site[0] - 1] = new CodeInstruction(OpCodes.Ldc_I4, 99);
        var before = code.Select(i => i.ToString()).ToList();
        AssertFalse(rewrite(code), label + ": rewrite refuses a partial match");
        AssertEqual(string.Join("\n", before), string.Join("\n", code.Select(i => i.ToString())), label + ": IL unchanged");
    }
}
