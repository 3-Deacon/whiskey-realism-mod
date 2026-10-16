using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Recruit routing (stage G, G5b, spec C8). Vanilla's ZoneRecruiting raises each new regiment at a
    // weak heatmap cell and attaches it to a division near that cell: AICampaign.GrabExistingGroup
    // (12739; called from ZoneRecruiting at 12558 with the cell's world position) searches the top groups
    // within aimaximumdistancetosearchforunitrelocations of that position, drops the player's units
    // (RemovePlayerUnits 12755/12790/12797) and may start a division in a corps (12796-12801); the new
    // regiment then travels to its group by abstract SetTransfer (139051-139075). Nothing routes a recruit
    // to the army that needs it.
    //
    // This patch changes one argument: while vanilla is inside ZoneRecruiting
    // (RecruitmentPatch.InZoneRecruiting), the search position becomes the hold point of the nearest
    // formation the live allocation reports short, within MaxRouteFactor x the relocation range of the
    // cell (ReinforcementRouting.TryRouteRecruit). Vanilla's own division search, player-unit removal and
    // travel then run unchanged. No skip, no write: a Prefix argument edit (Harmony's ref parameter), the
    // same class of edit as the planner's other steering. A player formation can be a shortfall's position
    // marker (G4); the hold point is all it contributes and no recruit is attached to it (vanilla drops
    // player units from its own candidates). Rollback: Campaign Planner / Enable Campaign Planner Executors.
    [HarmonyPatch(typeof(AICampaign), "GrabExistingGroup")]
    internal static class RecruitRoutingPatch
    {
        private static readonly System.Collections.Generic.List<FormationRecord> Own = new System.Collections.Generic.List<FormationRecord>();

        [HarmonyPrefix]
        internal static void Prefix(ref Vector3 position)
        {
            // GrabExistingGroup also runs for CheckTransferOfUnits (17319): only the recruiting call is steered.
            if (!RecruitmentPatch.InZoneRecruiting) return;
            using (TelemetryPerf.Scope("campaign.patch.recruit-routing", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.recruit-routing");
                try
                {
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                    if (!FlagOn(Plugin.EnableCampaignPlanner) || !FlagOn(Plugin.EnableCampaignPlannerExecutors)) return;
                    if (StrategicCoordinator.Instance == null) return;

                    int aifaction = RecruitmentPatch.ActiveAifaction;
                    int alliance = AICampaignReflect.GetAllianceId(aifaction);
                    if (alliance < 0 || alliance > 1) return;
                    if (StrategicCoordinator.IsPlayerCICOf(alliance, StrategicCoordinator.ResolvePlayerAlliance())) return;
                    if (!CampaignPlanStore.TryGetAllocation(alliance, out var allocation)) return;

                    var faction = AICampaignReflect.GetFaction(aifaction);
                    var ownUnits = faction == null ? null : VanillaFieldCache.List(faction, "ownunits");
                    if (ownUnits == null) return;

                    LiveFormationReader.Read(ownUnits, alliance, false, Own);
                    if (!ReinforcementRouting.TryRouteRecruit(allocation, Own, GamePrefs.aimaximumdistancetosearchforunitrelocations,
                            position.x, position.z, out float routeX, out float routeZ, out int toInstanceId)) return;

                    string to = toInstanceId >= 0 ? LiveFormationReader.NameOf(ownUnits, toInstanceId) : "position-marker";
                    string from = ReplacementFlowRow.Cell(position.x, position.z);
                    position = new Vector3(routeX, position.y, routeZ);
                    ReplacementFlowRow.Emit(alliance, ReplacementFlowRow.KindRecruit, to, from);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("recruit-routing:prefix", "[Patch:RecruitRouting] prefix failed: " + ex.Message);
                }
            }
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
