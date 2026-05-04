using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    internal enum ArmyPerkRole
    {
        General,
        Siege,
        Raid,
        Maneuver,
        Recovery,
        Scouting,
        River,
        CapitalDefense
    }

    internal enum FleetPerkRole
    {
        General,
        Blockade,
        Raid,
        River,
        PortDefense,
        Amphibious
    }

    internal static class PerkSelectionScorer
    {
        internal static int SelectArmyPerk(
            int allianceId,
            Theater theater,
            ArmyPerkRole role,
            PersonalityVector personality,
            IEnumerable<int> availablePerks)
        {
            return SelectBest(availablePerks, perk => ArmyScore(allianceId, theater, role, personality, perk));
        }

        internal static int SelectFleetPerk(
            int allianceId,
            FleetPerkRole role,
            IEnumerable<int> availablePerks)
        {
            return SelectBest(availablePerks, perk => FleetScore(allianceId, role, perk));
        }

        private static int SelectBest(IEnumerable<int> availablePerks, Func<int, float> scorer)
        {
            if (availablePerks == null) return -1;

            int best = -1;
            float bestScore = float.MinValue;
            foreach (int perk in availablePerks)
            {
                float score = scorer(perk);
                if (score > bestScore || (Math.Abs(score - bestScore) < 0.0001f && perk < best))
                {
                    best = perk;
                    bestScore = score;
                }
            }

            return best;
        }

        private static float ArmyScore(
            int allianceId,
            Theater theater,
            ArmyPerkRole role,
            PersonalityVector personality,
            int perk)
        {
            float score = 1f;

            switch (perk)
            {
                case 0: score += 1.2f; break;  // Siege Train
                case 1: score += 0.7f; break;  // Field Telegraph
                case 2: score += 0.8f; break;  // Balloon Corps
                case 3: score += 1.0f; break;  // Bureau of Military Information
                case 4: score += 0.9f; break;  // Skilled Cartographers
                case 5: score += 1.0f; break;  // Flying Column
                case 6: score += 0.9f; break;  // Foot Cavalry
                case 7: score += 0.9f; break;  // Engineers & Mechanics
                case 8: score += 0.9f; break;  // Ambulance Corps
                case 9: score += 0.8f; break;  // Pontoon Train
                case 10: score += 1.1f; break; // Sappers and Miners
                case 11: score += 0.4f; break; // Land Torpedoes
                case 12: score += allianceId == 1 ? 1.0f : 0.4f; break; // Partisan Brigades
                case 13: score += allianceId == 1 ? 0.8f : 0.3f; break; // Bushwhackers
                case 14: score += 0.2f; break; // Embedded Reporters
                case 15: score += 0.5f; break; // Limelights
                case 16: score += 0.9f; break; // Expert Scouts
                case 17: score += 0.7f; break; // River Expedition
            }

            switch (role)
            {
                case ArmyPerkRole.Siege:
                    if (perk == 10) score += 4.0f;
                    if (perk == 0) score += 3.2f;
                    if (perk == 9) score += 2.4f;
                    if (perk == 7) score += 1.5f;
                    break;
                case ArmyPerkRole.Raid:
                    if (perk == 12) score += 4.0f;
                    if (perk == 13) score += 3.2f;
                    if (perk == 5) score += 2.0f;
                    if (perk == 6) score += 1.8f;
                    break;
                case ArmyPerkRole.Maneuver:
                    if (perk == 5) score += 3.0f;
                    if (perk == 6) score += 2.6f;
                    if (perk == 4) score += 1.6f;
                    if (perk == 1) score += 1.2f;
                    break;
                case ArmyPerkRole.Recovery:
                    if (perk == 8) score += 3.0f;
                    if (perk == 7) score += 2.2f;
                    if (perk == 5) score += 1.0f;
                    break;
                case ArmyPerkRole.Scouting:
                    if (perk == 3) score += 3.0f;
                    if (perk == 16) score += 2.6f;
                    if (perk == 2) score += 2.0f;
                    if (perk == 4) score += 1.8f;
                    break;
                case ArmyPerkRole.River:
                    if (perk == 17) score += 3.4f;
                    if (perk == 9) score += 3.0f;
                    if (perk == 10) score += 1.8f;
                    if (perk == 3) score += 1.2f;
                    break;
                case ArmyPerkRole.CapitalDefense:
                    if (perk == 11) score += 3.0f;
                    if (perk == 10) score += 2.4f;
                    if (perk == 8) score += 2.0f;
                    if (perk == 1) score += 1.4f;
                    break;
            }

            score += Math.Max(0f, personality.Audacity) * ((perk == 5 || perk == 6 || perk == 12 || perk == 13) ? 0.8f : 0f);
            score += Math.Max(0f, personality.Caution) * ((perk == 8 || perk == 11 || perk == 1) ? 0.6f : 0f);

            if (theater == Theater.River && (perk == 17 || perk == 9 || perk == 10)) score += 1.2f;
            if (theater == Theater.Coast && (perk == 0 || perk == 10 || perk == 15)) score += 0.8f;
            if (theater == Theater.East && (perk == 1 || perk == 3 || perk == 8)) score += 0.5f;

            return score;
        }

        private static float FleetScore(int allianceId, FleetPerkRole role, int perk)
        {
            float score = 1f;

            switch (perk)
            {
                case 0: score += allianceId == 1 ? 1.2f : 0.5f; break; // Torpedoes
                case 1: score += 1.0f; break; // Mortar Boats
                case 2: score += 0.9f; break; // Supply Colliers
                case 3: score += 0.8f; break; // Balloon Barge
                case 4: score += 0.6f; break; // Limelights
                case 5: score += allianceId == 0 ? 1.4f : 0.5f; break; // Blockading Squadron
                case 6: score += allianceId == 1 ? 1.5f : 0.3f; break; // Sea Raiders
                case 7: score += allianceId == 0 ? 1.2f : 0.7f; break; // Amphibious Attack
                case 8: score += allianceId == 1 ? 1.4f : 0.4f; break; // Blockade Busters
                case 9: score += allianceId == 1 ? 1.1f : 0.5f; break; // Battery Runner
                case 10: score += 1.0f; break; // Port Defender
            }

            switch (role)
            {
                case FleetPerkRole.Blockade:
                    if (perk == 5) score += 4.0f;
                    if (perk == 2) score += 1.8f;
                    if (perk == 7) score += 1.4f;
                    break;
                case FleetPerkRole.Raid:
                    if (perk == 6) score += 4.0f;
                    if (perk == 8) score += 3.0f;
                    if (perk == 9) score += 2.0f;
                    if (perk == 0) score += 1.4f;
                    break;
                case FleetPerkRole.River:
                    if (perk == 1) score += 3.4f;
                    if (perk == 3) score += 2.4f;
                    if (perk == 7) score += 1.6f;
                    break;
                case FleetPerkRole.PortDefense:
                    if (perk == 10) score += 3.4f;
                    if (perk == 0) score += 2.4f;
                    if (perk == 4) score += 1.8f;
                    break;
                case FleetPerkRole.Amphibious:
                    if (perk == 7) score += 3.4f;
                    if (perk == 1) score += 2.0f;
                    if (perk == 3) score += 1.5f;
                    break;
            }

            return score;
        }
    }
}
