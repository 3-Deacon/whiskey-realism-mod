using System;
using WhiskeyRealism.Strategic;
using static TestHarness;

// BUG-AI-003 / BUG-AI-004 / BUG-AI-005: vanilla reads perk ids from the wrong table.
// Unit type picks the table (PerkRow.GetPerksShown 208802): <= 13 battle, 17 fleet,
// everything else campaign. Battle 8 = Expert Scouts, campaign 8 = Ambulance Corps,
// fleet 8 = Blockade Busters.
internal static class PerkIdFixTests
{
    private static readonly float[] AmbulanceMortality = { 0.9f, 0.75f, 0.5f }; // campaignprefs.txt 2434-2439
    private static readonly float[] FootCavalryAttrition = { 0.9f, 0.8f, 0.7f }; // campaignprefs.txt 2408-2413

    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("PerkTableFollowsVanillaUnitTypeSplit", PerkTableFollowsVanillaUnitTypeSplit),
            ("CampaignPerkEightGetsNoScoutSight", CampaignPerkEightGetsNoScoutSight),
            ("BattlePerkEightKeepsScoutSight", BattlePerkEightKeepsScoutSight),
            ("FleetPerkEightGetsNoScoutSight", FleetPerkEightGetsNoScoutSight),
            ("BattleConcealmentPerksOnlyOnBattleTable", BattleConcealmentPerksOnlyOnBattleTable),
            ("AmbulanceCorpsMortalityComesFromCampaignGroup", AmbulanceCorpsMortalityComesFromCampaignGroup),
            ("AmbulanceCorpsStacksWithParentArmy", AmbulanceCorpsStacksWithParentArmy),
            ("RegimentExpertScoutsNoLongerCutsWoundedMortality", RegimentExpertScoutsNoLongerCutsWoundedMortality),
            ("FootCavalryAttritionComesFromCampaignGroup", FootCavalryAttritionComesFromCampaignGroup),
            ("RegimentAceOfSpadeNoLongerCutsForcedMarchSickness", RegimentAceOfSpadeNoLongerCutsForcedMarchSickness),
            ("CampaignWoundedPlanLeavesUnperkedRegimentAlone", CampaignWoundedPlanLeavesUnperkedRegimentAlone),
            ("CampaignWoundedPlanIgnoresNonBattleTableUnits", CampaignWoundedPlanIgnoresNonBattleTableUnits),
            ("CampaignWoundedPlanIgnoresNonCampaignGroup", CampaignWoundedPlanIgnoresNonCampaignGroup),
            ("GroupPerkFactorFailsSafeToVanillaNeutral", GroupPerkFactorFailsSafeToVanillaNeutral),
        });
    }

    private static void PerkTableFollowsVanillaUnitTypeSplit()
    {
        AssertEqual(PerkTable.Battle, VanillaPerkTables.TableFor(0), "infantry");
        AssertEqual(PerkTable.Battle, VanillaPerkTables.TableFor(4), "scouts detachment");
        AssertEqual(PerkTable.Battle, VanillaPerkTables.TableFor(13), "brigade");
        AssertEqual(PerkTable.Campaign, VanillaPerkTables.TableFor(14), "division");
        AssertEqual(PerkTable.Campaign, VanillaPerkTables.TableFor(15), "corps");
        AssertEqual(PerkTable.Campaign, VanillaPerkTables.TableFor(16), "army");
        AssertEqual(PerkTable.Fleet, VanillaPerkTables.TableFor(17), "fleet");
        AssertEqual(PerkTable.Campaign, VanillaPerkTables.TableFor(18), "army group");
    }

    // Campaign map: a corps with Ambulance Corps (campaign id 8) must not see further.
    private static void CampaignPerkEightGetsNoScoutSight()
    {
        for (int unitType = 14; unitType <= 16; unitType++)
            AssertFalse(VanillaPerkTables.ExpertScoutsSightApplies(unitType, hasPerkEight: true),
                "campaign group type " + unitType + " perk 8 is Ambulance Corps");
        AssertFalse(VanillaPerkTables.ExpertScoutsSightApplies(18, hasPerkEight: true), "army group");
    }

    private static void BattlePerkEightKeepsScoutSight()
    {
        AssertTrue(VanillaPerkTables.ExpertScoutsSightApplies(1, hasPerkEight: true), "cavalry Expert Scouts");
        AssertTrue(VanillaPerkTables.ExpertScoutsSightApplies(4, hasPerkEight: true), "detached scouts copy the perk (DetachCompany 90444)");
        AssertFalse(VanillaPerkTables.ExpertScoutsSightApplies(1, hasPerkEight: false), "no perk, no factor");
    }

    private static void FleetPerkEightGetsNoScoutSight()
    {
        AssertFalse(VanillaPerkTables.ExpertScoutsSightApplies(17, hasPerkEight: true), "fleet perk 8 is Blockade Busters");
    }

    // UnitIsHidden 101205-101214 subtracts Expert Skirmishers (3) / Expert Scouts (8)
    // concealment; campaign 3/8 are Bureau of Military Information / Ambulance Corps,
    // fleet 3/8 Balloon Barge / Blockade Busters.
    private static void BattleConcealmentPerksOnlyOnBattleTable()
    {
        AssertTrue(VanillaPerkTables.UsesBattleTable(0), "infantry");
        AssertTrue(VanillaPerkTables.UsesBattleTable(3), "skirmishers");
        AssertFalse(VanillaPerkTables.UsesBattleTable(15), "corps");
        AssertFalse(VanillaPerkTables.UsesBattleTable(17), "fleet");
    }

    // UpdateCampaignUnitStats 115113-115120: num6 *= woundedtokilledambulancecorps[...]
    // must come from the campaign group, not the regiment.
    private static void AmbulanceCorpsMortalityComesFromCampaignGroup()
    {
        float factor = VanillaPerkTables.GroupPerkFactor(AmbulanceMortality, ownHas: true, ownLevel: 2, parentHas: false, parentLevel: 0);
        AssertEqual(0.5f, factor, "Ambulance Corps III: mortality of wounded -50%");
        var plan = VanillaPerkTables.PlanCampaignWounded(0, 15, false, false, factor, 1f);
        AssertTrue(plan.Rewrite, "regiment of an Ambulance Corps corps is rewritten");
        AssertEqual(0.5f, plan.AmbulanceFactor, "ambulance factor");
        AssertEqual(1f, plan.FootCavalryFactor, "foot cavalry factor");
    }

    // RegimentMove 133418-133425 multiplies own and parent levels; keep that rule.
    private static void AmbulanceCorpsStacksWithParentArmy()
    {
        float factor = VanillaPerkTables.GroupPerkFactor(AmbulanceMortality, true, 0, true, 1);
        AssertNear(0.9f * 0.75f, factor, 1e-6f, "corps I x army II");
    }

    private static void RegimentExpertScoutsNoLongerCutsWoundedMortality()
    {
        var plan = VanillaPerkTables.PlanCampaignWounded(1, 15, unitHasIdEight: true, unitHasIdSix: false,
            ambulanceFactor: 1f, footCavalryFactor: 1f);
        AssertTrue(plan.Rewrite, "battle Expert Scouts id 8 must be hidden");
        AssertEqual(1f, plan.AmbulanceFactor, "no Ambulance Corps on the group");
    }

    private static void FootCavalryAttritionComesFromCampaignGroup()
    {
        float factor = VanillaPerkTables.GroupPerkFactor(FootCavalryAttrition, true, 1, false, 0);
        AssertEqual(0.8f, factor, "Foot Cavalry II: attrition during forced marches -20%");
        var plan = VanillaPerkTables.PlanCampaignWounded(0, 16, false, false, 1f, factor);
        AssertTrue(plan.Rewrite, "rewritten");
        AssertEqual(0.8f, plan.FootCavalryFactor, "foot cavalry factor");
    }

    private static void RegimentAceOfSpadeNoLongerCutsForcedMarchSickness()
    {
        var plan = VanillaPerkTables.PlanCampaignWounded(0, 14, unitHasIdEight: false, unitHasIdSix: true,
            ambulanceFactor: 1f, footCavalryFactor: 1f);
        AssertTrue(plan.Rewrite, "battle Ace of Spade id 6 must be hidden");
        AssertEqual(1f, plan.FootCavalryFactor, "no Foot Cavalry on the group");
    }

    private static void CampaignWoundedPlanLeavesUnperkedRegimentAlone()
    {
        var plan = VanillaPerkTables.PlanCampaignWounded(0, 15, false, false, 1f, 1f);
        AssertFalse(plan.Rewrite, "nothing to correct: vanilla runs untouched");
    }

    // The fleet branch (unittyp 17) and the group's own call return before the perk
    // reads (115068-115071); only battle-table units reach them.
    private static void CampaignWoundedPlanIgnoresNonBattleTableUnits()
    {
        AssertFalse(VanillaPerkTables.PlanCampaignWounded(17, 17, true, true, 0.5f, 0.7f).Rewrite, "fleet");
        AssertFalse(VanillaPerkTables.PlanCampaignWounded(15, 15, true, true, 0.5f, 0.7f).Rewrite, "corps");
    }

    // A group that is not on the campaign table has no Ambulance Corps / Foot Cavalry:
    // the regiment's wrong-table ids are still hidden, but no factor is applied.
    private static void CampaignWoundedPlanIgnoresNonCampaignGroup()
    {
        AssertFalse(VanillaPerkTables.GroupPerksApply(17), "fleet group");
        AssertFalse(VanillaPerkTables.GroupPerksApply(13), "brigade group");
        AssertTrue(VanillaPerkTables.GroupPerksApply(15), "corps group");
        var plan = VanillaPerkTables.PlanCampaignWounded(0, 13, true, false, 0.5f, 1f);
        AssertTrue(plan.Rewrite, "regiment id 8 still hidden");
        AssertEqual(1f, plan.AmbulanceFactor, "non-campaign group contributes nothing");
    }

    private static void GroupPerkFactorFailsSafeToVanillaNeutral()
    {
        AssertEqual(1f, VanillaPerkTables.GroupPerkFactor(null, true, 2, true, 2), "missing prefs array");
        AssertEqual(1f, VanillaPerkTables.GroupPerkFactor(AmbulanceMortality, true, 5, false, 0), "level past the array");
        AssertEqual(1f, VanillaPerkTables.GroupPerkFactor(AmbulanceMortality, true, -1, false, 0), "negative level");
        AssertEqual(1f, VanillaPerkTables.GroupPerkFactor(AmbulanceMortality, false, 2, false, 2), "no perk");
    }
}
