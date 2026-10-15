namespace WhiskeyRealism.Strategic
{
    internal enum PerkTable
    {
        Battle,
        Campaign,
        Fleet
    }

    // Vanilla keeps three perk-id tables and picks one by unit type (PerkRow.GetPerksShown
    // 208802, PerkRow.UpdatePerkButton 208939): unittyp <= 13 (regiments, batteries,
    // detachments, brigades) uses PerkBattleTooltips.txt, unittyp 17 (fleet) uses
    // PerkFleetsTooltips.txt, every other type (division, corps, army, army group; on the
    // campaign map and as a battle HQ, import_units 88579-88632) uses
    // PerkTooltipsArmyCampaign.txt. Regiment.HasPerk (132765) compares raw ids, so a call site
    // written for one table misreads the other two. Pure: no Unity or vanilla types.
    internal static class VanillaPerkTables
    {
        internal const int LastBattleTableUnitType = 13;
        internal const int FleetUnitType = 17;

        // PerkBattleTooltips.txt.
        internal const int BattleExpertSkirmishers = 3;
        internal const int BattleAceOfSpade = 6;
        internal const int BattleExpertScouts = 8;

        // PerkTooltipsArmyCampaign.txt.
        internal const int CampaignFootCavalry = 6;
        internal const int CampaignAmbulanceCorps = 8;

        internal static PerkTable TableFor(int unitType)
        {
            if (unitType <= LastBattleTableUnitType) return PerkTable.Battle;
            return unitType == FleetUnitType ? PerkTable.Fleet : PerkTable.Campaign;
        }

        // FogOfWar.UpdateViewRange (101298-101301) multiplies line of sight by
        // perk_expertscoutsvisibilityrangefactor whenever HasPerk(8). Only battle id 8 is
        // Expert Scouts; campaign 8 is Ambulance Corps and fleet 8 Blockade Busters.
        internal static bool ExpertScoutsSightApplies(int unitType, bool hasPerkEight)
        {
            return hasPerkEight && TableFor(unitType) == PerkTable.Battle;
        }

        // FogOfWar.UnitIsHidden (101205-101214) subtracts Expert Skirmishers (3) and Expert
        // Scouts (8) concealment from any unit it is given. Its regref argument is used for
        // nothing else, and no campaign or fleet perk promises concealment.
        internal static bool BattleConcealmentPerksApply(int unitType)
        {
            return TableFor(unitType) == PerkTable.Battle;
        }

        // Ambulance Corps and Foot Cavalry belong to the campaign group that owns the
        // regiment (UpdateGroupStrength 120409 passes it as groupunit).
        internal static bool GroupPerksApply(int groupUnitType)
        {
            return TableFor(groupUnitType) == PerkTable.Campaign;
        }

        // A campaign group's own level times its parent army's level, the rule vanilla uses
        // wherever it reads a campaign perk (HasPerk + HasPerkParent, e.g. RegimentMove
        // 133418-133425). Missing arrays and out-of-range levels count as no effect.
        internal static float GroupPerkFactor(float[] factors, bool ownHas, int ownLevel, bool parentHas, int parentLevel)
        {
            float factor = 1f;
            if (ownHas) factor *= FactorAt(factors, ownLevel);
            if (parentHas) factor *= FactorAt(factors, parentLevel);
            return factor;
        }

        // Regiment.UpdateCampaignUnitStats on a regiment (unittyp <= 13; the fleet branch and
        // groups return first, 115068-115071) reads the regiment's own ids: HasPerk(8) at
        // 115113 (battle 8 = Expert Scouts) scales wounded mortality and HasPerk(6) at 115155
        // (battle 6 = Ace of Spade) scales forced-march sickness. HasPerkParent needs
        // unittyp 15 (132750), so for a regiment it is always false and the group's Ambulance
        // Corps / Foot Cavalry never apply. The plan replaces those two reads with the group's
        // factors; it rewrites only when there is something to correct.
        internal static CampaignWoundedPerkPlan PlanCampaignWounded(
            int unitType,
            int groupUnitType,
            bool unitHasIdEight,
            bool unitHasIdSix,
            float ambulanceFactor,
            float footCavalryFactor)
        {
            if (TableFor(unitType) != PerkTable.Battle) return CampaignWoundedPerkPlan.Vanilla;

            bool groupApplies = GroupPerksApply(groupUnitType);
            float ambulance = groupApplies ? ambulanceFactor : 1f;
            float footCavalry = groupApplies ? footCavalryFactor : 1f;
            bool rewrite = unitHasIdEight || unitHasIdSix || ambulance != 1f || footCavalry != 1f;
            return rewrite ? new CampaignWoundedPerkPlan(true, ambulance, footCavalry) : CampaignWoundedPerkPlan.Vanilla;
        }

        private static float FactorAt(float[] factors, int level)
        {
            if (factors == null || level < 0 || level >= factors.Length) return 1f;
            return factors[level];
        }
    }

    internal readonly struct CampaignWoundedPerkPlan
    {
        internal static readonly CampaignWoundedPerkPlan Vanilla = new CampaignWoundedPerkPlan(false, 1f, 1f);

        internal CampaignWoundedPerkPlan(bool rewrite, float ambulanceFactor, float footCavalryFactor)
        {
            Rewrite = rewrite;
            AmbulanceFactor = ambulanceFactor;
            FootCavalryFactor = footCavalryFactor;
        }

        internal bool Rewrite { get; }

        // Multiplier on wounded mortality (woundedtokilledambulancecorps); 1 = none.
        internal float AmbulanceFactor { get; }

        // Multiplier on forced-march sickness (attritionfootcavalry); 1 = none.
        internal float FootCavalryFactor { get; }
    }
}
