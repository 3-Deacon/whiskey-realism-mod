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
        // Candidates within this score margin of the best are drawn weighted-random, so a
        // faction's armies do not all take the same perk sequence.
        internal const float NearTieMargin = 0.35f;

        // A formation directive's army perk role (PerkSelectionPatch.ResolveArmyRole); false = the
        // directive names none and the objective's role decides. Screen = on a campaign-planner recon
        // mission (G1b). The fallback Hold ("default-hold") keeps the perk path of the fallback it
        // replaced ("default-screen", which had no case here): only an explicit Hold takes the
        // capital-defence perks (integrator decision, G1b verify r1 F2).
        internal static bool TryDirectiveRole(FormationDirective directive, string reason, out ArmyPerkRole role)
        {
            role = ArmyPerkRole.General;
            switch (directive)
            {
                case FormationDirective.Screen:
                    role = ArmyPerkRole.Scouting;
                    return true;
                case FormationDirective.Recover:
                    role = ArmyPerkRole.Recovery;
                    return true;
                case FormationDirective.Hold:
                    if (FormationDirectiveLedger.IsDefaultHold(reason)) return false;
                    role = ArmyPerkRole.CapitalDefense;
                    return true;
                case FormationDirective.Guard:
                case FormationDirective.CoordinateHold:
                    role = ArmyPerkRole.CapitalDefense;
                    return true;
                case FormationDirective.Probe:
                case FormationDirective.Mass:
                case FormationDirective.Counterstroke:
                case FormationDirective.Reinforce:
                case FormationDirective.CoordinateMass:
                    role = ArmyPerkRole.Maneuver;
                    return true;
                default:
                    return false;
            }
        }

        // Vanilla AICampaign.CheckPerkSelection (decompile 11881/11888) draws
        // Random.Range(0, Length / LevelsPerPerk - 1): the int overload excludes its upper
        // bound, so the AI never receives the last perk id (campaign 17 River Expedition,
        // fleet 10 Port Defender). The battle pick (AIBattle.CheckPerkSelection 5429) uses
        // array[Random.Range(0, array.Length)], every id, which is the intent (BUG-AI-011).
        // Returns the exclusive upper bound of the candidate ids: every id with the fix on,
        // vanilla's window with it off (Random.Range(0, 0) yields 0, hence a single-perk
        // table keeps id 0).
        internal static int CandidateLimit(int perkCount, bool includeLastId)
        {
            if (perkCount <= 0) return 0;
            if (includeLastId) return perkCount;
            return perkCount == 1 ? 1 : perkCount - 1;
        }

        // Upper bound passed to Random.Range(0, bound) when Whiskey replays vanilla's random
        // pick (the units Whiskey does not steer): perkCount with the fix on, vanilla's
        // perkCount - 1 with it off.
        internal static int RandomPickUpperBound(int perkCount, bool includeLastId)
        {
            if (perkCount <= 0) return 0;
            return includeLastId ? perkCount : perkCount - 1;
        }

        // roll in [0, 1): 0 = always the best-scoring perk.
        internal static int SelectArmyPerk(
            int allianceId,
            Theater theater,
            ArmyPerkRole role,
            PersonalityVector personality,
            IEnumerable<int> availablePerks,
            float roll = 0f)
        {
            return SelectWeighted(availablePerks, perk => ArmyScore(allianceId, theater, role, personality, perk), roll);
        }

        internal static int SelectFleetPerk(
            int allianceId,
            FleetPerkRole role,
            IEnumerable<int> availablePerks,
            float roll = 0f)
        {
            return SelectWeighted(availablePerks, perk => FleetScore(allianceId, role, perk), roll);
        }

        private static int SelectWeighted(IEnumerable<int> availablePerks, Func<int, float> scorer, float roll)
        {
            if (availablePerks == null) return -1;

            var scored = new List<KeyValuePair<int, float>>();
            float best = float.MinValue;
            foreach (int perk in availablePerks)
            {
                float score = scorer(perk);
                scored.Add(new KeyValuePair<int, float>(perk, score));
                if (score > best) best = score;
            }
            if (scored.Count == 0) return -1;

            float floor = best - NearTieMargin;
            var near = scored.FindAll(kv => kv.Value > floor);
            near.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));

            float total = 0f;
            foreach (var kv in near) total += kv.Value - floor;
            if (float.IsNaN(roll) || roll < 0f) roll = 0f;
            if (roll >= 1f) roll = 0.9999f;

            float target = roll * total;
            float cumulative = 0f;
            foreach (var kv in near)
            {
                cumulative += kv.Value - floor;
                if (target < cumulative) return kv.Key;
            }
            return near[near.Count - 1].Key;
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
                case 7: break;                 // Engineers & Mechanics: AI construction never reads it (BUG-AI-012)
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

            // Role bonuses go only to perks whose code effect serves the role (BUG-AI-012):
            // Siege: 10 siege assault (Autocalc.FightUnit 21100, assault stance 0 only),
            //   0 siege firepower (UpdateSiegeStrength 18340), 15 night siege/skirmish (21130,
            //   either side), 17 siege assault (21108).
            // River: 17 river speed (RegimentMove 133484), 9 creek crossing and supply speed.
            // Recovery: 8 wounded speed/mortality, 9 supply speed (UpdateIIPSupplyLink 114146),
            //   5 supplies carried (114165).
            // CapitalDefense: a fort holder defends (CalculateStance 22449 -> stance 2), so
            //   Sappers never fires; 11 defensive skirmish (21119) and enemy slow-down, 15 night
            //   siege, 8, 1.
            switch (role)
            {
                case ArmyPerkRole.Siege:
                    if (perk == 10) score += 4.0f;
                    if (perk == 0) score += 3.2f;
                    if (perk == 15) score += 2.4f;
                    if (perk == 17) score += 1.5f;
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
                    if (perk == 9) score += 2.2f;
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
                    break;
                case ArmyPerkRole.CapitalDefense:
                    if (perk == 11) score += 3.0f;
                    if (perk == 15) score += 2.4f;
                    if (perk == 8) score += 2.0f;
                    if (perk == 1) score += 1.4f;
                    break;
            }

            score += Math.Max(0f, personality.Audacity) * ((perk == 5 || perk == 6 || perk == 12 || perk == 13) ? 0.8f : 0f);
            score += Math.Max(0f, personality.Caution) * ((perk == 8 || perk == 11 || perk == 1) ? 0.6f : 0f);

            if (theater == Theater.River && (perk == 17 || perk == 9)) score += 1.2f;
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
