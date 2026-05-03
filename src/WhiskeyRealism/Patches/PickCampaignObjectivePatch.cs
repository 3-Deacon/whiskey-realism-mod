using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    [HarmonyPatch(typeof(AICampaign), "PickCampaignObjective")]
    internal static class PickCampaignObjectivePatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            OnceLog.Info("pickcampobj", "PickCampaignObjectivePatch wired");
            try
            {
                if (StrategicCoordinator.Instance == null) return true;

                int allianceId = ResolveAllianceId(_aifaction);
                if (allianceId < 0) return true;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return true;

                if (allianceId < 0 || allianceId >= StrategicCoordinator.Instance.CICs.Length) return true;
                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                if (cic == null || cic.ActivePlan == null) return true;

                var phase = cic.ActivePlan.CurrentPhase;
                if (phase == null) return true;
                if (phase.TargetObjectiveId < 0) return true;

                SetFollowedCampaignObjective(_aifaction, phase.TargetObjectiveId);
                if (Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[Patch:PickCampObj] alliance={allianceId} obj={phase.TargetObjectiveId} (plan-driven)");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:PickCampObj] " + ex.Message);
                return true;
            }
        }

        private static int ResolveAllianceId(int aifactionIndex)
        {
            try
            {
                var listField = AccessTools.Field(typeof(AICampaign), "aifaction");
                var list = listField?.GetValue(null) as System.Collections.IList;
                if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return -1;
                var faction = list[aifactionIndex];
                var allianceField = AccessTools.Field(faction.GetType(), "allianceid");
                return allianceField != null ? (int)allianceField.GetValue(faction) : -1;
            }
            catch { return -1; }
        }

        private static void SetFollowedCampaignObjective(int aifactionIndex, int objectiveId)
        {
            try
            {
                var listField = AccessTools.Field(typeof(AICampaign), "aifaction");
                var list = listField?.GetValue(null) as System.Collections.IList;
                if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return;
                var faction = list[aifactionIndex];
                var f = AccessTools.Field(faction.GetType(), "followedcampaignobjective");
                f?.SetValue(faction, objectiveId);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[Patch:PickCampObj] write failed: " + ex.Message); }
        }
    }
}
