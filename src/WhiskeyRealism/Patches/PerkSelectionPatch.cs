using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.CheckPerkSelection randomly assigns campaign army and
    // fleet perks. This Prefix mirrors vanilla eligibility, chooses a
    // role-aware perk, then still delegates assignment to Regiment.ChoosePerk.
    [HarmonyPatch(typeof(AICampaign), "CheckPerkSelection")]
    internal static class PerkSelectionPatch
    {
        private static readonly HashSet<string> _loggedChoices = new HashSet<string>();

        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return true;
                if (StrategicCoordinator.Instance == null) return true;

                var faction = GetFaction(_aifaction);
                if (faction == null) return true;

                int alliance = AICampaignReflect.GetAllianceId(_aifaction);
                if (alliance < 0 || alliance >= 2) return true;
                if (StrategicCoordinator.IsPlayerCICOf(alliance, GameVars.playeralliance)) return true;

                bool handled = false;
                OnceLog.Info("perks", "PerkSelectionPatch wired (campaign army/fleet perk steering)");

                var ownUnits = ReadList(faction, "ownunits");
                if (ownUnits != null && PerkRow.TooltipsHeadlinesEachLevelGroup != null)
                {
                    int perkCount = PerkRow.TooltipsHeadlinesEachLevelGroup.Length / PerkRow.LevelsPerPerk;
                    for (int i = 0; i < ownUnits.Count; i++)
                    {
                        var unit = ownUnits[i] as Regiment;
                        if (unit == null) continue;
                        if (DLC_WL.dlc_scenarioactive && unit.dlcw_isundercommander) continue;

                        handled = true;
                        ChooseArmyPerk(unit, alliance, perkCount);
                    }
                }

                var ownFleets = ReadList(faction, "ownfleets");
                if (ownFleets != null && PerkRow.TooltipsHeadlinesEachLevelFleets != null)
                {
                    int perkCount = PerkRow.TooltipsHeadlinesEachLevelFleets.Length / PerkRow.LevelsPerPerk;
                    for (int i = 0; i < ownFleets.Count; i++)
                    {
                        var fleet = ownFleets[i] as Regiment;
                        if (fleet == null) continue;

                        handled = true;
                        ChooseFleetPerk(fleet, alliance, perkCount);
                    }
                }

                return !handled;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("perks:prefix", "[Patch:Perks] prefix failed: " + ex.Message);
                return true;
            }
        }

        private static void ChooseArmyPerk(Regiment unit, int alliance, int perkCount)
        {
            var available = AvailablePerks(unit, perkCount);
            if (available.Count == 0) return;

            var cic = StrategicCoordinator.Instance.CICs[alliance];
            var meta = CurrentObjectiveMetadata(cic);
            var era = StrategicCoordinator.Instance.Eras[alliance];
            var personality = cic != null && era != null
                ? cic.Effective(era)
                : default(PersonalityVector);
            var role = ResolveArmyRole(alliance, unit, meta);
            int selected = PerkSelectionScorer.SelectArmyPerk(alliance, meta.Theater, role, personality, available);
            if (selected < 0) return;

            unit.ChoosePerk(selected);
            LogChoice("army", alliance, role.ToString(), selected);
        }

        private static void ChooseFleetPerk(Regiment fleet, int alliance, int perkCount)
        {
            var available = AvailablePerks(fleet, perkCount);
            if (available.Count == 0) return;

            var cic = StrategicCoordinator.Instance.CICs[alliance];
            var meta = CurrentObjectiveMetadata(cic);
            var role = ResolveFleetRole(alliance, meta);
            int selected = PerkSelectionScorer.SelectFleetPerk(alliance, role, available);
            if (selected < 0) return;

            fleet.ChoosePerk(selected);
            LogChoice("fleet", alliance, role.ToString(), selected);
        }

        private static ObjectiveMetadata CurrentObjectiveMetadata(CIC cic)
        {
            int objectiveId = cic?.ActivePlan?.CurrentPhase?.TargetObjectiveId ?? -1;
            if (ObjectiveCatalog.TryResolve(objectiveId, out var meta))
                return meta;

            return ObjectiveMetadata.DefaultDerived(Theater.Unknown, 0f, 0f);
        }

        private static ArmyPerkRole ResolveArmyRole(int alliance, Regiment unit, ObjectiveMetadata meta)
        {
            var directive = CurrentDirective(alliance, unit);
            if (directive != null)
            {
                switch (directive.Directive)
                {
                    case FormationDirective.RaidSupport:
                        return ArmyPerkRole.Raid;
                    case FormationDirective.Recover:
                        return ArmyPerkRole.Recovery;
                    case FormationDirective.Guard:
                    case FormationDirective.Hold:
                    case FormationDirective.CoordinateHold:
                        return ArmyPerkRole.CapitalDefense;
                    case FormationDirective.Probe:
                    case FormationDirective.Mass:
                    case FormationDirective.Counterstroke:
                    case FormationDirective.Reinforce:
                    case FormationDirective.CoordinateMass:
                        return ArmyPerkRole.Maneuver;
                }
            }

            if (meta.Category == Category.RiverControl || meta.Theater == Theater.River) return ArmyPerkRole.River;
            if (meta.Category == Category.CapitalThreat) return alliance == 1 ? ArmyPerkRole.CapitalDefense : ArmyPerkRole.Siege;
            if (meta.Category == Category.RailroadCut || meta.Category == Category.SupplyHub) return ArmyPerkRole.Maneuver;
            if (meta.Category == Category.ForeignRecognition) return ArmyPerkRole.Raid;
            return ArmyPerkRole.General;
        }

        private static FleetPerkRole ResolveFleetRole(int alliance, ObjectiveMetadata meta)
        {
            if (meta.Category == Category.RiverControl || meta.Theater == Theater.River)
                return FleetPerkRole.River;

            if (alliance == 0)
            {
                if (meta.Theater == Theater.Coast) return FleetPerkRole.Blockade;
                return FleetPerkRole.Blockade;
            }

            if (meta.Theater == Theater.Coast || meta.Category == Category.CapitalThreat)
                return FleetPerkRole.PortDefense;

            return FleetPerkRole.Raid;
        }

        private static FormationDirectiveAssignment CurrentDirective(int alliance, Regiment unit)
        {
            try
            {
                var ledgers = StrategicCoordinator.Instance?.FormationDirectives;
                if (ledgers == null || alliance < 0 || alliance >= ledgers.Length || ledgers[alliance] == null)
                    return null;

                return ledgers[alliance].GetAssignment(UnitKey(unit));
            }
            catch { return null; }
        }

        private static List<int> AvailablePerks(Regiment unit, int perkCount)
        {
            var available = new List<int>();
            if (unit == null || unit.perks == null || perkCount <= 0) return available;

            bool hasOpenSlot = false;
            var existing = new HashSet<int>();
            for (int i = 0; i < unit.perks.Count; i++)
            {
                var perk = unit.perks[i];
                if (perk == null) continue;
                if (perk.perk >= 0) existing.Add(perk.perk);
                if (perk.perk < 0 && perk.perkexp >= 1f) hasOpenSlot = true;
            }

            if (!hasOpenSlot) return available;

            for (int perkId = 0; perkId < perkCount; perkId++)
                if (!existing.Contains(perkId))
                    available.Add(perkId);

            return available;
        }

        private static IList ReadList(object obj, string fieldName)
        {
            try { return AccessTools.Field(obj.GetType(), fieldName)?.GetValue(obj) as IList; }
            catch { return null; }
        }

        private static object GetFaction(int aifactionIndex)
        {
            var list = AccessTools.Field(typeof(AICampaign), "aifaction")?.GetValue(null) as IList;
            if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return null;
            return list[aifactionIndex];
        }

        private static string UnitKey(Regiment unit)
        {
            if (unit == null) return "<null>";
            return unit.name + ":" + unit.commander.ToString();
        }

        private static void LogChoice(string kind, int alliance, string role, int perk)
        {
            string signature = $"{kind}:{alliance}:{role}:{perk}";
            if (!_loggedChoices.Add(signature)) return;
            Plugin.Log.LogInfo($"[Patch:Perks] alliance={alliance} kind={kind} role={role} perk={perk}");
        }
    }
}
