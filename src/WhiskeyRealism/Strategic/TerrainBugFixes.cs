using System;

namespace WhiskeyRealism.Strategic
{
    // Pure decisions for the campaign terrain and engineer bug fixes (BUG-AI-013, BUG-AI-014,
    // BUG-AI-016, BUG-TAC-021, BUG-WL-015). Anchors are lines of the build 14703111 decompile.
    internal static class TerrainBugFixes
    {
        // NavMesh area "Small Rivers" (creek), campaignprefs.txt 711-714.
        internal const int CreekArea = 3;
        internal const int CampaignPontoonTrain = 9;

        // Pontoon Train tooltip, PerkTooltipsArmyCampaign.txt 114/118/122: "Movement speed
        // across small rivers +25%/+50%/+75%". The campaignprefs.txt 2540-2545 values
        // (0.75/0.5/0.25) are labelled "Movement cost crossing creeks" and are what AddPath
        // multiplies the creek path cost by (130419-130431); no code applies a speed.
        private static readonly float[] PontoonCreekSpeed = { 1.25f, 1.5f, 1.75f };

        internal static int PontoonLevels => PontoonCreekSpeed.Length;

        internal static float PontoonCreekSpeedFactor(int level)
        {
            if (level < 0) return 1f;
            return PontoonCreekSpeed[Math.Min(level, PontoonCreekSpeed.Length - 1)];
        }

        // The pontoon level that applies, by AddPath's own precedence (130421-130427): the
        // parent's level replaces the unit's own; -1 when neither holds the perk.
        internal static int PontoonLevel(bool ownHas, int ownLevel, bool parentHas, int parentLevel)
        {
            if (parentHas) return Math.Max(0, parentLevel);
            if (ownHas) return Math.Max(0, ownLevel);
            return -1;
        }

        // Vanilla's HasPerkParent / GetPerkLevelParent answer only for a campaign corps asking
        // its army (132698, 132750).
        internal static bool ReadsParentPerk(bool calledFromCampaign, int unitTyp)
        {
            return calledFromCampaign && unitTyp == 15;
        }

        // Whether the pontoon fix speaks for this unit: a campaign land group on the campaign
        // table (division, corps, army, army group). Fleets use the fleet table (9 = Battery
        // Runner) and battle-table units' 9 is Cold Steel.
        internal static bool PontoonApplies(bool fixOn, bool calledFromCampaign, int unitTyp)
        {
            return fixOn && calledFromCampaign && VanillaPerkTables.TableFor(unitTyp) == PerkTable.Campaign;
        }

        // Multiplier on Regiment.GetTerrainSpeedAdjustment's result (1/terrainspeccost[3] on a
        // creek, 132888-132900): the tooltip's speed factor on a creek cell, 1 elsewhere.
        internal static float PontoonCreekSpeedMultiplier(bool fixOn, bool calledFromCampaign, int unitTyp, int currentTerrain, int level)
        {
            if (currentTerrain != CreekArea || level < 0 || !PontoonApplies(fixOn, calledFromCampaign, unitTyp)) return 1f;
            return PontoonCreekSpeedFactor(level);
        }

        // The creek path-cost factors that keep AddPath's route choice in time units: vanilla
        // prices every terrain as terrainspeccost and moves at 1/terrainspeccost (55843,
        // 132900), so a creek crossed at speed x factor must cost 1/factor. Filled per level.
        internal static void FillPontoonPathCost(float[] target)
        {
            if (target == null) return;
            for (int i = 0; i < target.Length; i++) target[i] = 1f / PontoonCreekSpeedFactor(i);
        }

        // ---- BUG-AI-014: engineer branch of arms --------------------------------------------
        // branchtext {"infantry","cavalry","artillery","engineer","navy"} (65091); W&L attribute
        // 19 Engineer sets 3 (42526), fleet commanders get 4 (59162). The engineering-point
        // bonus "if commander fits to branch of arms (engineers)" (unitprefs.txt 1595) tests
        // historicalbranch == 4 at 87443 and 22578.
        internal const int BranchEngineer = 3;
        internal const int BranchNavy = 4;

        // For the call vanilla's "== 4" test must mean "engineer": engineers are written as 4
        // and navy officers as this value, which no read in either method matches.
        internal const int NavyMaskedBranch = -4;

        // ---- BUG-WL-015: incident 18 "A Good Ground" ----------------------------------------
        // Integrator decision 2026-10-15: the doubled cover never exceeds 0.9, so ranged losses
        // (126510: x clamp01(1 - covervalue)) stay at 10% or more; below that the incident keeps
        // its full doubling. The cap never takes a unit below its cover without the incident.
        internal const float GoodGroundCoverCap = 0.9f;

        // Who gets the cap: exactly the units vanilla doubled (CheckCoyCovers 138350 early
        // return: a pending path, a building assignment or unittyp > 13; 138373: incident 18
        // active and the unit under the W&L player's command), with the fix on.
        internal static bool GoodGroundCapApplies(bool fixOn, bool underPlayerCommand, int unitTyp,
            bool hasBuildingAssignment, int lastTransmittedPath, bool incidentActive)
        {
            return fixOn && underPlayerCommand && incidentActive && unitTyp <= 13
                && !hasBuildingAssignment && lastTransmittedPath < 0;
        }

        internal static float CapGoodGroundCover(float doubled, float multiplier, float cap)
        {
            if (float.IsNaN(doubled) || float.IsInfinity(doubled) || !(multiplier > 1f)) return doubled;
            float undoubled = doubled / multiplier;
            return Math.Min(doubled, Math.Max(cap, undoubled));
        }

        // ---- BUG-AI-016: autoresolve entrenchment protection ------------------------------
        // Autocalc.FightUnit (21050) sets num2 = the side of unit1 (the unit fired at): 1 when
        // unit0 is on side 0 (alliance[0]), else 0 (21061-21070). When that side defends
        // (usedstance[num2] == 2) incoming fire is scaled by 1 - engineeringpoints[1] / 100 x
        // militaryexperience of unit1's alliance (21075-21077): the literal 1 instead of num2,
        // so a defending side 0 is protected by side 1's (its attacker's) entrenchments.
        internal const int VanillaEntrenchmentReadSide = 1;

        internal static int DefendingSide(bool firingUnitOnSide0)
        {
            return firingUnitOnSide0 ? 1 : 0;
        }

        internal static bool EntrenchmentReadNeedsFix(bool fixOn, int defendingSide)
        {
            return fixOn && defendingSide != VanillaEntrenchmentReadSide;
        }

        // ---- BUG-TAC-021: the player's entrenchment term in the battle engineering pool -----
        // BattleUnits.ResetEngineeringPoints (87465): the player's pool adds
        // entrenchmentlevel x totalstrength x k x entrenchmentlevel (87476), the AI's adds
        // totalstrength x k x enemyentrenchmentlevel (87480); both are then scaled by
        // 1 + (effect - 1) x level (87477, 87481). Entrenchment is at most the alliance's
        // military experience (115450-115453), so the square shrinks the player's term.
        // Returns what the linear term adds on top of vanilla's squared one, after the scale.
        internal static float LinearEntrenchmentDelta(float level, float strength, float influence, float effect)
        {
            float delta = (level - level * level) * strength * influence * (1f + (effect - 1f) * level);
            return float.IsNaN(delta) || float.IsInfinity(delta) ? 0f : delta;
        }
    }
}
