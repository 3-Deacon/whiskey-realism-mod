using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla CheckArmyGroupManagement (decompile 17705) forms ArmyGroups from
    // nearby top units that already have theaterposition. This Postfix
    // preserves vanilla's pass, then nudges the top formations the campaign
    // planner allocated to one sector into the same ArmyGroup using vanilla
    // ArmyGroup APIs. It seats no commander: appointments are the planner's
    // appointment executor's (CommanderAppointmentRuntime).
    //
    // 2026-10-14 (C-ArmyGroup): repeats vanilla's early-return guard (army
    // groups disabled / reform not researched / the CIC test, applied as #70
    // applies it: only to the W&L CIC player's own alliance); never
    // attaches to the W&L player's group (GetArmyGroupInRange 75631);
    // CanAddFurtherUnits (75770) gates attaching; skips W&L player-commanded
    // units like CheckCombinationOfUnits (17141).
    [HarmonyPatch(typeof(AICampaign), "CheckArmyGroupManagement")]
    internal static class ArmyGroupManagementPatch
    {
        private static readonly HashSet<string> LogOnceKeys = new HashSet<string>();

        [HarmonyPostfix]
        internal static void Postfix(AICampaign __instance, int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.army-group-management", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.army-group-management");
                OnceLog.Info("armygroup", "ArmyGroupManagementPatch wired (historical army-group steering)");

                try
                {
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                    if (StrategicCoordinator.Instance == null) return;

                    // The planner's executors write the grouping: with them off vanilla's pass stands alone.
                    if (!CampaignPlannerRuntime.ExecutorsEnabled()) return;

                    int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                    if (allianceId < 0 || allianceId > 1) return;

                    int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                    if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;
                    if (StrategicCoordinator.WlCareerStartPending()) return;
                    if (!VanillaAllowsArmyGroups(allianceId)) return;

                    if (!CampaignPlanStore.TryGetPlan(allianceId, out var published) || !PlanFreshness.IsFresh(published.DaySerial)) return;
                    if (!CampaignPlanStore.TryGetAllocation(allianceId, out var allocation)) return;
                    CampaignPlanStore.TryGetSectors(allianceId, out var sectors);

                    var plans = ArmyGroupDoctrine.PlanGroups(allocation);
                    if (plans.Count == 0) return;

                    var faction = AICampaignReflect.GetFaction(_aifaction);
                    var ownUnits = VanillaFieldCache.List(faction, "ownunits");
                    if (ownUnits == null)
                    {
                        OnceLog.Warning(
                            "armygroup:no-ownunits:" + allianceId,
                            $"[Patch:ArmyGroup] skipped: ownunits unavailable for alliance={allianceId}");
                        return;
                    }

                    for (int i = 0; i < plans.Count; i++)
                        ApplyPlan(__instance, allianceId, plans[i], ownUnits, sectors);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("armygroup:postfix", "[Patch:ArmyGroup] postfix failed: " + ex.Message);
                }
            }
        }

        private static void ApplyPlan(AICampaign campaign, int allianceId, ArmyGroupPlan plan, IList ownUnits, SectorModel sectors)
        {
            var units = ResolveUnits(plan, ownUnits, allianceId);
            if (units.Count < 2) return;

            ArmyGroup own = OwnGroupFor(units);
            ArmyGroup area = (UnityEngine.Object)(object)own == (UnityEngine.Object)null
                ? FindGroupInArea(allianceId, sectors, plan.SectorId, units)
                : null;
            var action = ArmyGroupDoctrine.ResolveArmyGroupPlanAction(
                (UnityEngine.Object)(object)own != (UnityEngine.Object)null,
                (UnityEngine.Object)(object)own != (UnityEngine.Object)null && own.CanAddFurtherUnits(),
                (UnityEngine.Object)(object)area != (UnityEngine.Object)null);

            if (action != ArmyGroupPlanAction.CreateNew)
            {
                ArmyGroup group = action == ArmyGroupPlanAction.AttachToArea ? area : own;
                int attached = 0;
                if (action == ArmyGroupPlanAction.AttachToOwn || action == ArmyGroupPlanAction.AttachToArea)
                {
                    for (int i = 0; i < units.Count; i++)
                    {
                        if ((UnityEngine.Object)(object)units[i].assignedarmygroup != (UnityEngine.Object)null) continue;
                        if (!group.CanAddFurtherUnits()) break;
                        group.AddUnit(units[i], calledfromload: false, renamecorps: true);
                        attached++;
                    }
                }

                if (attached > 0)
                {
                    EmitCampaignInfoOnce(
                        $"armygroup:{allianceId}:{plan.SectorId}:attach:{UnitSignature(units)}",
                        $"[ArmyGroup] alliance={allianceId} sector={plan.SectorId} action=armygroup-attach units={attached}",
                        TelemetryLayer.Campaign);
                }
                return;
            }

            var unassigned = new List<Regiment>();
            for (int i = 0; i < units.Count; i++)
                if ((UnityEngine.Object)(object)units[i].assignedarmygroup == (UnityEngine.Object)null)
                    unassigned.Add(units[i]);

            if (unassigned.Count < 2) return;
            if (!HasCorpsOrArmy(unassigned)) return;

            var prefab = campaign.ArmyGroupPrefab;
            if (prefab == null)
            {
                OnceLog.Warning("armygroup:prefab", "[Patch:ArmyGroup] AICampaign.ArmyGroupPrefab missing; cannot create historical army group");
                return;
            }

            var created = ArmyGroup.CreateNewArmyGroup(prefab, "", -1, unassigned);
            if ((UnityEngine.Object)(object)created == (UnityEngine.Object)null) return;

            EmitCampaignInfoOnce(
                $"armygroup:{allianceId}:{plan.SectorId}:create:{UnitSignature(unassigned)}",
                $"[ArmyGroup] alliance={allianceId} sector={plan.SectorId} action=armygroup-create units={unassigned.Count}",
                TelemetryLayer.Campaign);
        }

        private static List<Regiment> ResolveUnits(ArmyGroupPlan plan, IList ownUnits, int allianceId)
        {
            var result = new List<Regiment>();
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i] as Regiment;
                if (!IsEligibleTopUnit(unit)) continue;
                if (!plan.InstanceIds.Contains(((UnityEngine.Object)unit).GetInstanceID())) continue;
                if (unit.unittyp == 14 && !FormationDirectiveRuntime.AllowsArmyGroupAttachment(allianceId, UnitKey(unit))) continue;
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
                bool wlActive = DLC_WL.dlc_scenarioactive;
                int chosen = DLC_WL.dlc_chosencommander;
                bool playerIsCic = wlActive && DLC_WL.IsCommanderInChief();
                int cicAlliance = playerIsCic && chosen >= 0 && chosen < GameVars.commander.Count
                    ? GameVars.commander[chosen].alliance
                    : -1;
                bool guardEnabled = Plugin.EnableCicFactionAiBugGuard != null && Plugin.EnableCicFactionAiBugGuard.Value;
                return ArmyGroupDoctrine.ArmyGroupsAllowed(
                    GameVars.allowarmygroups,
                    GameVars.alliance[allianceId].AllowsArmyGroups(),
                    CicFactionAiGuard.VanillaGuardBlocks(guardEnabled, wlActive, playerIsCic, cicAlliance, allianceId));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("armygroup:vanilla-guard", "[Patch:ArmyGroup] vanilla army-group guard read failed: " + ex.Message);
                return false;
            }
        }

        private static bool IsPlayerGroup(ArmyGroup group)
        {
            return ArmyGroupDoctrine.IsPlayerArmyGroup(
                DLC_WL.dlc_scenarioactive,
                group.GetCommanderID(),
                DLC_WL.dlc_chosencommander);
        }

        private static bool HasCorpsOrArmy(List<Regiment> units)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].unittyp >= 15 && units[i].unittyp <= 16)
                    return true;
            return false;
        }

        // A non-player army group that already holds one of the plan's units.
        private static ArmyGroup OwnGroupFor(List<Regiment> units)
        {
            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i].assignedarmygroup;
                if ((UnityEngine.Object)(object)group != (UnityEngine.Object)null && !IsPlayerGroup(group))
                    return group;
            }
            return null;
        }

        // The army group in range of the sector's centre (the formations' mean position when the sector has none).
        private static ArmyGroup FindGroupInArea(int allianceId, SectorModel sectors, int sectorId, List<Regiment> units)
        {
            Vector3 anchor = AveragePosition(units);
            if (sectors != null && sectorId >= 0 && sectorId < sectors.Count)
            {
                sectors.Centroid(sectorId, out float x, out float z);
                anchor = new Vector3(x, 0f, z);
            }

            float range = GamePrefs.aiarmygroupmaxtheaterrange;
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

        private static void EmitCampaignInfoOnce(string key, string line, TelemetryLayer layer)
        {
            if (!LogOnceKeys.Add(key ?? string.Empty)) return;
            TelemetryRouter.LegacyInfo(line, layer);
        }
    }
}
