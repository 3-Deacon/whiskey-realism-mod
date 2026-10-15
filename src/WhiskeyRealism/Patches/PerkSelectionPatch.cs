using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.CheckPerkSelection (decompile 11871-11891, job 14) calls
    // Regiment.ChoosePerk(Random.Range(0, Length/3 - 1)) for every own unit and fleet;
    // ChoosePerk (123381) fills the first open slot unless the perk is already owned. The int
    // Random.Range excludes its upper bound, so the last id (campaign 17 River Expedition,
    // fleet 10 Port Defender) is never drawn; the battle pick (AIBattle.CheckPerkSelection
    // 5429) draws array[Random.Range(0, array.Length)], every id (BUG-AI-011).
    // This Prefix chooses a role-aware perk with a weighted-random tiebreak among near-equal
    // scores for alliances 0/1 when Whiskey runs that alliance, then still delegates
    // assignment to Regiment.ChoosePerk. Where Whiskey does not steer (a W&L
    // commander-in-chief's own alliance, Europe, no coordinator yet) it replays vanilla's
    // random pick with vanilla's eligibility and the full id range, and skips vanilla; the
    // whole method is these two loops, so the replay is the method with the bound fixed.
    // Candidate window: every id with Campaign / Fix AI Perk Pick Range on, vanilla's
    // window (last id never drawn) with it off; with it off the unsteered case runs vanilla.
    [HarmonyPatch(typeof(AICampaign), "CheckPerkSelection")]
    internal static class PerkSelectionPatch
    {
        private static readonly HashSet<string> _loggedChoices = new HashSet<string>();
        private static readonly Dictionary<string, System.Reflection.FieldInfo> _factionFields =
            new Dictionary<string, System.Reflection.FieldInfo>();

        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-selection", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.perk-selection");
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return true;

                var faction = GetFaction(_aifaction);
                if (faction == null) return true;

                int alliance = AICampaignReflect.GetAllianceId(_aifaction);
                if (StrategicCoordinator.Instance == null || alliance < 0 || alliance >= 2
                    || StrategicCoordinator.IsPlayerCICOf(alliance, GameVars.playeralliance))
                    return !(FixPickRange() && ReplayVanillaPick(faction));

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
            } // end using TelemetryPerf.Scope
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
            int selected = PerkSelectionScorer.SelectArmyPerk(alliance, meta.Theater, role, personality, available, UnityEngine.Random.value);
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
            int selected = PerkSelectionScorer.SelectFleetPerk(alliance, role, available, UnityEngine.Random.value);
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

            int limit = PerkSelectionScorer.CandidateLimit(perkCount, FixPickRange());
            for (int perkId = 0; perkId < limit; perkId++)
                if (!existing.Contains(perkId))
                    available.Add(perkId);

            return available;
        }

        internal static bool FixPickRange()
        {
            try { return Plugin.FixAiPerkPickRange != null && Plugin.FixAiPerkPickRange.Value; }
            catch { return false; }
        }

        // Vanilla's two loops (11877-11890) with the full id range: same units, same W&L
        // eligibility (a unit under the W&L player's command is skipped), uniform random id,
        // Regiment.ChoosePerk decides as in vanilla. Returns false (run vanilla) when vanilla
        // would return early or the faction's lists cannot be read.
        private static bool ReplayVanillaPick(object faction)
        {
            if (AICampaign.aiarea == null) return false; // vanilla returns before picking (11873)
            var ownUnits = ReadList(faction, "ownunits");
            var ownFleets = ReadList(faction, "ownfleets");
            if (ownUnits == null || ownFleets == null) return false;

            if (PerkRow.TooltipsHeadlinesEachLevelGroup != null)
            {
                int bound = PerkSelectionScorer.RandomPickUpperBound(PerkRow.TooltipsHeadlinesEachLevelGroup.Length / PerkRow.LevelsPerPerk, true);
                for (int i = 0; i < ownUnits.Count; i++)
                {
                    var unit = ownUnits[i] as Regiment;
                    if ((UnityEngine.Object)unit == null) continue;
                    if (unit.dlcw_isundercommander && DLC_WL.dlc_scenarioactive) continue;
                    unit.ChoosePerk(UnityEngine.Random.Range(0, bound));
                }
            }
            if (PerkRow.TooltipsHeadlinesEachLevelFleets != null)
            {
                int bound = PerkSelectionScorer.RandomPickUpperBound(PerkRow.TooltipsHeadlinesEachLevelFleets.Length / PerkRow.LevelsPerPerk, true);
                for (int j = 0; j < ownFleets.Count; j++)
                {
                    var fleet = ownFleets[j] as Regiment;
                    if ((UnityEngine.Object)fleet == null) continue;
                    fleet.ChoosePerk(UnityEngine.Random.Range(0, bound));
                }
            }
            OnceLog.Info("perks:replay", "[Patch:Perks] unsteered alliance: vanilla random perk pick replayed over every perk id");
            return true;
        }

        // AIFaction is a single private type; field lookups are cached by name.
        private static IList ReadList(object obj, string fieldName)
        {
            try
            {
                if (!_factionFields.TryGetValue(fieldName, out var field))
                {
                    field = AccessTools.Field(obj.GetType(), fieldName);
                    _factionFields[fieldName] = field;
                }
                return field?.GetValue(obj) as IList;
            }
            catch { return null; }
        }

        private static object GetFaction(int aifactionIndex)
        {
            return AICampaignReflect.GetFaction(aifactionIndex);
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
            TelemetryRouter.LegacyInfo(
                $"[FormationDirective] source=PerkSelection alliance={alliance} kind={kind} role={role} perk={perk}",
                TelemetryLayer.Campaign);
        }
    }
}
