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
        internal const int BattleEngineering = 0;
        internal const int BattleSharpshooters = 1;
        internal const int BattleZouave = 2;
        internal const int BattleExpertSkirmishers = 3;
        internal const int BattleAceOfSpade = 6;
        internal const int BattleExpertScouts = 8;
        internal const int BattleColdSteel = 9;
        internal const int BattleStormers = 11;
        internal const int BattleLightningBrigade = 13;
        internal const int BattleSapperGunners = 18;

        // PerkTooltipsArmyCampaign.txt.
        internal const int CampaignFlyingColumn = 5;
        internal const int CampaignFootCavalry = 6;
        internal const int CampaignAmbulanceCorps = 8;
        internal const int CampaignSappersAndMiners = 10;
        internal const int CampaignEmbeddedReporters = 14;
        internal const int CampaignRiverExpedition = 17;

        // PerkFleetsTooltips.txt.
        internal const int FleetSeaRaiders = 6;

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

        // A call site written for battle ids (FogOfWar.UnitIsHidden concealment 101205-101214,
        // CheckAbilityToRun 113255-113262, Regiment.UpdateFatigue 132665, GetCoverValueFromID
        // 138518-138526, Initialize/ChoosePerk hasengineers 113162/123411, the Zouave running
        // sound 133734-133743) is right only for a unit on the battle table; a battle HQ carries
        // campaign ids there.
        internal static bool UsesBattleTable(int unitType)
        {
            return TableFor(unitType) == PerkTable.Battle;
        }

        // RegimentMove (133518-133524) multiplies speed by forcedmarchspeedfootcavalry on
        // HasPerk(6) / HasPerkParent(6) for every campaign unit on every move. Foot Cavalry is
        // campaign 6 (fleet 6 is Sea Raiders), and its tooltip and campaignprefs.txt 2402 say
        // "forced march speed": vanilla's forced march is doublequick | running (sickness
        // 115153, Foot Cavalry XP 134116). fixIds applies the table rule, fixScope the
        // forced-march gate; with both off this is vanilla.
        internal static bool FootCavalrySpeedApplies(int unitType, bool forcedMarch, bool fixIds, bool fixScope)
        {
            if (fixIds && TableFor(unitType) != PerkTable.Campaign) return false;
            if (fixScope && !forcedMarch) return false;
            return true;
        }

        // RegimentMove grants Flying Column (5) XP for any campaign move and Foot Cavalry (6) XP
        // on a forced march (134113-134119). On a fleet 5/6 are Blockading Squadron / Sea
        // Raiders, which level up by blockading (PerkFleetsTooltips.txt).
        internal static bool MarchPerkExpApplies(int unitType)
        {
            return TableFor(unitType) == PerkTable.Campaign;
        }

        // Regiment.UpdateBlockades (113790, 113813) lets HasPerk(6) open trade nodes as raid
        // targets and grants sea-raider XP (113821). That is fleet 6, Sea Raiders ("Level up by
        // blockading trade nodes"); for a land unit 6 is Foot Cavalry / Ace of Spade.
        internal static bool SeaRaidersApplies(int unitType)
        {
            return TableFor(unitType) == PerkTable.Fleet;
        }

        // BattleUnits.CalculateCommanderFame (83930-83933) boosts a corps/army commander's
        // fame when CheckForGroupPerk(reg, 14) finds Embedded Reporters, but that search starts
        // at the parent (83866-83884; the unit itself is checked only at the top of its
        // hierarchy, where GrabNextParentUnit returns it, 86886-86907) and the level is read
        // from reg. The tooltip ("allowing
        // newspaper reporters to follow the unit") makes the unit itself the first holder, and
        // the factor is that holder's level. fixScope off = vanilla.
        internal static PerkHolder FameReportersHolder(bool selfHas, bool ancestorHas, bool fixScope)
        {
            if (fixScope && selfHas) return PerkHolder.Self;
            if (ancestorHas) return fixScope ? PerkHolder.Ancestor : PerkHolder.AncestorWithOwnLevel;
            return PerkHolder.None;
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
            bool fixIds,
            int unitType,
            int groupUnitType,
            bool unitHasIdEight,
            bool unitHasIdSix,
            float ambulanceFactor,
            float footCavalryFactor)
        {
            if (!fixIds || TableFor(unitType) != PerkTable.Battle) return CampaignWoundedPerkPlan.Vanilla;

            bool groupApplies = GroupPerksApply(groupUnitType);
            float ambulance = groupApplies ? ambulanceFactor : 1f;
            float footCavalry = groupApplies ? footCavalryFactor : 1f;
            bool rewrite = unitHasIdEight || unitHasIdSix || ambulance != 1f || footCavalry != 1f;
            return rewrite ? new CampaignWoundedPerkPlan(true, ambulance, footCavalry) : CampaignWoundedPerkPlan.Vanilla;
        }

        // Runtime decisions, one per patch or hook. Each takes its flag, so with the flag off
        // the answer is vanilla's (keep the id / grant / count / hide nothing).

        // Transpiler remaps (Autocalc 1/2/17, UpdateMorale 9).
        internal static int RemapId(bool fixIds, int vanillaId, int fixedId)
        {
            return fixIds ? fixedId : vanillaId;
        }

        // FogPerkSightPatch: hide id 8 from a viewer whose 8 is not Expert Scouts.
        internal static bool HideExpertScoutsSight(bool fixIds, int unitType, bool hasPerkEight)
        {
            return fixIds && hasPerkEight && !ExpertScoutsSightApplies(unitType, hasPerkEight);
        }

        // FogPerkConcealmentPatch: drop regref for a non-battle unit holding 3 or 8.
        internal static bool DropBattleConcealment(bool fixIds, int unitType, bool holdsThreeOrEight)
        {
            return fixIds && holdsThreeOrEight && !UsesBattleTable(unitType);
        }

        // RegimentMove marching XP hook: grant unless the ids fix is on and the unit is no
        // campaign group.
        internal static bool GrantMarchPerkExp(bool fixIds, int unitType)
        {
            return !fixIds || MarchPerkExpApplies(unitType);
        }

        // RegimentMove Zouave running-sound hook.
        internal static bool BattlePerkCounts(bool fixIds, int unitType)
        {
            return !fixIds || UsesBattleTable(unitType);
        }

        // SeaRaidersBlockadePatch: hide a non-fleet unit's perks when it holds 6.
        internal static bool HideSeaRaiders(bool fixIds, int unitType, bool holdsSix)
        {
            return fixIds && holdsSix && !SeaRaidersApplies(unitType);
        }

        // BattleHqPerkPatch swaps (UpdateFatigue 2, GetCoverValueFromID 6/13/18): hide a
        // non-battle unit's perks when it holds one of the battle ids read there.
        internal static bool HideBattleIdsOnHq(bool fixIds, int unitType, bool holdsReadId)
        {
            return fixIds && holdsReadId && !UsesBattleTable(unitType);
        }

        // BattleHqPerkPatch postfixes (isabletorun 2/11, hasengineers 0): reset a flag that only a
        // battle id read on a non-battle unit set, in battle (vanilla's reads need
        // !calledfromcampaign), when vanilla's value without the perk is false.
        internal static bool ResetHqPerkFlag(bool fixIds, bool calledFromCampaign, int unitType, bool flagSet, bool holdsReadId, bool valueWithoutPerk)
        {
            return fixIds && !calledFromCampaign && flagSet && holdsReadId && !valueWithoutPerk && !UsesBattleTable(unitType);
        }

        private static float FactorAt(float[] factors, int level)
        {
            if (factors == null || level < 0 || level >= factors.Length) return 1f;
            return factors[level];
        }
    }

    internal enum PerkHolder
    {
        None,
        Self,
        Ancestor,
        // Vanilla: an ancestor holds the perk but the level is read from the unit itself.
        AncestorWithOwnLevel
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
