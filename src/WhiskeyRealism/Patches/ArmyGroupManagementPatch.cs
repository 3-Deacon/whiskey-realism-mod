using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla CheckArmyGroupManagement (decompile 17705) forms ArmyGroups from
    // nearby top units that already have theaterposition. This Postfix
    // preserves vanilla's pass, then nudges historically related top formations
    // into the same ArmyGroup using vanilla ArmyGroup APIs.
    //
    // 2026-10-14 (C-ArmyGroup): repeats vanilla's early-return guard (army
    // groups disabled / reform not researched / W&L player is CIC); never
    // attaches to or re-commands the W&L player's group and respects
    // CanAddFurtherUnits like GetArmyGroupInRange (75631, 75770); picks
    // commanders only through Commander.CanBePickedForAIReplacement (60748)
    // with exact historical-name matching; skips W&L player-commanded units
    // like CheckCombinationOfUnits (17141).
    [HarmonyPatch(typeof(AICampaign), "CheckArmyGroupManagement")]
    internal static class ArmyGroupManagementPatch
    {
        private static readonly HashSet<string> LogOnceKeys = new HashSet<string>();

        [HarmonyPostfix]
        internal static void Postfix(AICampaign __instance, int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.army-group-management", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                OnceLog.Info("armygroup", "ArmyGroupManagementPatch wired (historical army-group steering)");

                try
                {
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                    if (StrategicCoordinator.Instance == null) return;

                    int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                    if (allianceId < 0 || allianceId >= StrategicCoordinator.Instance.ArmyAreas.Length) return;

                    int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                    if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;
                    if (StrategicCoordinator.WlCareerStartPending()) return;
                    if (!VanillaAllowsArmyGroups(allianceId)) return;

                    var ledger = StrategicCoordinator.Instance.ArmyAreas[allianceId];
                    if (ledger == null) return;

                    var plans = ArmyGroupDoctrine.PlanGroups(ledger);
                    if (plans.Count == 0) return;

                    var faction = GetFaction(_aifaction);
                    var ownUnits = AccessTools.Field(faction?.GetType(), "ownunits")?.GetValue(faction) as IList;
                    if (ownUnits == null)
                    {
                        OnceLog.Warning(
                            "armygroup:no-ownunits:" + allianceId,
                            $"[Patch:ArmyGroup] skipped: ownunits unavailable for alliance={allianceId}");
                        return;
                    }

                    for (int i = 0; i < plans.Count; i++)
                        ApplyPlan(__instance, allianceId, plans[i], ownUnits);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("armygroup:postfix", "[Patch:ArmyGroup] postfix failed: " + ex.Message);
                }
            }
        }

        private static void ApplyPlan(AICampaign campaign, int allianceId, ArmyGroupPlan plan, IList ownUnits)
        {
            var units = ResolveUnits(plan, ownUnits, allianceId);
            if (units.Count < 2) return;

            ArmyGroup group = ExistingGroupFor(units);
            if ((UnityEngine.Object)(object)group == (UnityEngine.Object)null)
            {
                group = FindGroupInArea(allianceId, plan.AreaKey, units);
            }

            int attached = 0;
            if ((UnityEngine.Object)(object)group != (UnityEngine.Object)null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    if ((UnityEngine.Object)(object)units[i].assignedarmygroup != (UnityEngine.Object)null) continue;
                    if (!group.CanAddFurtherUnits()) break;
                    group.AddUnit(units[i], calledfromload: false, renamecorps: true);
                    attached++;
                }

                if (attached > 0)
                {
                    EmitCampaignInfoOnce(
                        $"armygroup:{allianceId}:{plan.AreaKey}:attach:{UnitSignature(units)}",
                        $"[ArmyArea] alliance={allianceId} area={plan.AreaKey} action=armygroup-attach units={attached}",
                        TelemetryLayer.Campaign);
                }

                AppointPreferredCommander(allianceId, plan.AreaKey, group, units);
                return;
            }

            var unassigned = new List<Regiment>();
            for (int i = 0; i < units.Count; i++)
                if ((UnityEngine.Object)(object)units[i].assignedarmygroup == (UnityEngine.Object)null)
                    unassigned.Add(units[i]);

            if (unassigned.Count < 2) return;
            if (!HasCorpsOrArmy(unassigned)) return;

            var prefab = AccessTools.Field(typeof(AICampaign), "ArmyGroupPrefab")?.GetValue(campaign) as GameObject;
            if (prefab == null)
            {
                OnceLog.Warning("armygroup:prefab", "[Patch:ArmyGroup] AICampaign.ArmyGroupPrefab missing; cannot create historical army group");
                return;
            }

            group = ArmyGroup.CreateNewArmyGroup(prefab, "", -1, unassigned);
            if ((UnityEngine.Object)(object)group == (UnityEngine.Object)null) return;

            EmitCampaignInfoOnce(
                $"armygroup:{allianceId}:{plan.AreaKey}:create:{UnitSignature(unassigned)}",
                $"[ArmyArea] alliance={allianceId} area={plan.AreaKey} action=armygroup-create units={unassigned.Count}",
                TelemetryLayer.Campaign);

            AppointPreferredCommander(allianceId, plan.AreaKey, group, units);
        }

        private static List<Regiment> ResolveUnits(ArmyGroupPlan plan, IList ownUnits, int allianceId)
        {
            var result = new List<Regiment>();
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (!IsEligibleTopUnit(unit)) continue;
                string unitKey = UnitKey(unit);
                if (!plan.UnitKeys.Contains(unitKey)) continue;
                if (unit.unittyp == 14 && !FormationDirectiveRuntime.AllowsArmyGroupAttachment(allianceId, unitKey)) continue;
                result.Add(unit);
            }
            return result;
        }

        private static bool IsEligibleTopUnit(Regiment unit)
        {
            if ((UnityEngine.Object)(object)unit == (UnityEngine.Object)null) return false;
            if ((UnityEngine.Object)(object)unit.garrisonreference != (UnityEngine.Object)null) return false;
            if (!unit.istopunit) return false;
            if (unit.unittyp < 14 || unit.unittyp > 16) return false;
            if (unit.groupstrengthdirect <= 1000f) return false;
            if (unit.inbattle || unit.onretreat) return false;
            // vanilla CheckCombinationOfUnits: (!dlcw_isundercommander | !dlc_scenarioactive)
            if (unit.dlcw_isundercommander && DLC_WL.dlc_scenarioactive) return false;
            return true;
        }

        private static bool VanillaAllowsArmyGroups(int allianceId)
        {
            try
            {
                if (GameVars.alliance == null || allianceId < 0 || allianceId >= GameVars.alliance.Length) return false;
                return ArmyGroupDoctrine.ArmyGroupsAllowed(
                    GameVars.allowarmygroups,
                    GameVars.alliance[allianceId].AllowsArmyGroups(),
                    DLC_WL.dlc_scenarioactive,
                    DLC_WL.IsCommanderInChief());
            }
            catch (Exception ex)
            {
                OnceLog.Warning("armygroup:vanilla-guard", "[Patch:ArmyGroup] vanilla army-group guard read failed: " + ex.Message);
                return false;
            }
        }

        private static bool AcceptsUnits(ArmyGroup group)
        {
            if ((UnityEngine.Object)(object)group == (UnityEngine.Object)null) return false;
            return ArmyGroupDoctrine.ArmyGroupAcceptsUnits(
                DLC_WL.dlc_scenarioactive,
                group.GetCommanderID(),
                DLC_WL.dlc_chosencommander,
                group.CanAddFurtherUnits());
        }

        private static bool HasCorpsOrArmy(List<Regiment> units)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].unittyp >= 15 && units[i].unittyp <= 16)
                    return true;
            return false;
        }

        private static ArmyGroup ExistingGroupFor(List<Regiment> units)
        {
            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i].assignedarmygroup;
                if ((UnityEngine.Object)(object)group != (UnityEngine.Object)null && AcceptsUnits(group))
                    return group;
            }
            return null;
        }

        private static ArmyGroup FindGroupInArea(int allianceId, string areaKey, List<Regiment> units)
        {
            Vector3 anchor;
            if (!ArmyAreaRuntime.TryGetAnchor(areaKey, out anchor))
                anchor = AveragePosition(units);

            float range = ReadGamePrefsFloat("aiarmygroupmaxtheaterrange", 650f);
            return ArmyGroup.GetArmyGroupInRange(allianceId, anchor, range);
        }

        private static Vector3 AveragePosition(List<Regiment> units)
        {
            Vector3 total = default(Vector3);
            int count = 0;
            for (int i = 0; i < units.Count; i++)
            {
                total += ((Component)units[i]).transform.position;
                count++;
            }
            return count > 0 ? total / count : default(Vector3);
        }

        private static void AppointPreferredCommander(int allianceId, string areaKey, ArmyGroup group, List<Regiment> units)
        {
            var preference = ArmyGroupDoctrine.ResolveCommanderPreference(allianceId, areaKey);
            if (preference.PreferredLastNames.Count == 0) return;

            int currentId = group.GetCommanderID();
            if (DLC_WL.dlc_scenarioactive && currentId == DLC_WL.dlc_chosencommander) return;
            if (CommanderMatches(allianceId, currentId, preference.PreferredLastNames)) return;

            int commanderId = FindPreferredCommander(allianceId, preference.PreferredLastNames, units);
            if (commanderId < 0 || commanderId == currentId) return;

            ArmyGroup.AppointCommander(group, commanderId, usefeudmechanism: false, resetcoordinationtime: true, ignoreprestige: true);
            TelemetryRouter.LegacyInfo(
                $"[ArmyArea] alliance={allianceId} area={areaKey} action=armygroup-appoint commander={CommanderName(commanderId)}",
                TelemetryLayer.Campaign);
        }

        private static int FindPreferredCommander(int allianceId, List<string> preferredLastNames, List<Regiment> units)
        {
            var commanders = GameVars.commander;
            if (commanders == null) return -1;

            for (int p = 0; p < preferredLastNames.Count; p++)
            {
                string preferred = preferredLastNames[p];
                for (int i = 0; i < commanders.Count; i++)
                {
                    var commander = commanders[i];
                    if (commander == null || commander.alliance != allianceId) continue;
                    if (!ArmyGroupDoctrine.CommanderNameMatches(allianceId, preferred, commander.combinedname)) continue;

                    bool pickable;
                    try { pickable = commander.CanBePickedForAIReplacement(allianceId, includeofficerswithcommand: true); }
                    catch { pickable = false; }

                    if (ArmyGroupDoctrine.IsEligibleArmyGroupCommander(
                            pickable,
                            IsPlayerCommander(i),
                            CommanderCommandsOneOf(commander, units) || CommanderHasNoCommand(commander)))
                        return i;
                }
            }
            return -1;
        }

        private static bool CommanderMatches(int allianceId, int commanderId, List<string> preferredLastNames)
        {
            var commanders = GameVars.commander;
            if (commanders == null || commanderId < 0 || commanderId >= commanders.Count) return false;
            string name = commanders[commanderId]?.combinedname;
            for (int i = 0; i < preferredLastNames.Count; i++)
                if (ArmyGroupDoctrine.CommanderNameMatches(allianceId, preferredLastNames[i], name))
                    return true;
            return false;
        }

        private static bool CommanderCommandsOneOf(object commander, List<Regiment> units)
        {
            var currentCommand = AccessTools.Field(commander.GetType(), "currentcommand")?.GetValue(commander) as Regiment;
            if ((UnityEngine.Object)(object)currentCommand == (UnityEngine.Object)null) return false;
            for (int i = 0; i < units.Count; i++)
                if ((UnityEngine.Object)(object)currentCommand == (UnityEngine.Object)(object)units[i])
                    return true;
            return false;
        }

        private static bool CommanderHasNoCommand(object commander)
        {
            var currentCommand = AccessTools.Field(commander.GetType(), "currentcommand")?.GetValue(commander) as Regiment;
            return (UnityEngine.Object)(object)currentCommand == (UnityEngine.Object)null;
        }

        private static bool IsPlayerCommander(int commanderId)
        {
            return DLC_WL.dlc_scenarioactive && commanderId == DLC_WL.dlc_chosencommander;
        }

        private static object GetFaction(int aifactionIndex)
        {
            var list = AccessTools.Field(typeof(AICampaign), "aifaction")?.GetValue(null) as IList;
            if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return null;
            return list[aifactionIndex];
        }

        private static string UnitKey(Regiment unit)
        {
            return ((UnityEngine.Object)unit).name + ":" + unit.commander.ToString();
        }

        private static string UnitSignature(List<Regiment> units)
        {
            var names = new List<string>();
            for (int i = 0; i < units.Count; i++)
                names.Add(((UnityEngine.Object)units[i]).name);
            names.Sort(StringComparer.Ordinal);
            return string.Join("+", names);
        }

        private static string CommanderName(int commanderId)
        {
            var commanders = AccessTools.Field(typeof(GameVars), "commander")?.GetValue(null) as IList;
            if (commanders == null || commanderId < 0 || commanderId >= commanders.Count) return "<unknown>";
            return ReadString(commanders[commanderId], "combinedname") ?? "<unknown>";
        }

        private static string ReadString(object target, string fieldName)
        {
            try
            {
                var field = AccessTools.Field(target.GetType(), fieldName);
                return field?.GetValue(target) as string;
            }
            catch { return null; }
        }

        private static float ReadGamePrefsFloat(string fieldName, float fallback)
        {
            try
            {
                var field = AccessTools.Field(typeof(GamePrefs), fieldName);
                return field != null ? Convert.ToSingle(field.GetValue(null)) : fallback;
            }
            catch { return fallback; }
        }

        private static void EmitCampaignInfoOnce(string key, string line, TelemetryLayer layer)
        {
            if (!LogOnceKeys.Add(key ?? string.Empty)) return;
            TelemetryRouter.LegacyInfo(line, layer);
        }
    }
}
