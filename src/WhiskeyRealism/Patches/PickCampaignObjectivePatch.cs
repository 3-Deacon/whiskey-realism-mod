using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
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
                if (cic == null) return true;
                if (cic.ActivePlan == null)
                {
                    if (HistoricalDoctrineEnabled())
                    {
                        TelemetryRouter.LegacyInfo(
                            $"[HistoricalOperation] alliance={allianceId} action=skip-vanilla-random reason=no-historical-operation-plan",
                            TelemetryLayer.Campaign);
                        return false;
                    }
                    return true;
                }

                var phase = cic.ActivePlan.CurrentPhase;
                if (phase == null || phase.TargetObjectiveId < 0)
                {
                    if (HistoricalDoctrineEnabled())
                    {
                        TelemetryRouter.LegacyInfo(
                            $"[HistoricalOperation] alliance={allianceId} action=skip-vanilla-random reason=invalid-historical-operation-phase",
                            TelemetryLayer.Campaign);
                        return false;
                    }
                    return true;
                }

                SetFollowedCampaignObjective(_aifaction, phase.TargetObjectiveId);
                if (Plugin.Instance.VerboseLogging.Value)
                    TelemetryRouter.LegacyInfo($"[Plan] alliance={allianceId} action=pick-campaign-objective objective={phase.TargetObjectiveId}", TelemetryLayer.Campaign);
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

        private static bool HistoricalDoctrineEnabled()
        {
            return Plugin.Instance == null ||
                Plugin.Instance.EnableHistoricalOperationDoctrine == null ||
                Plugin.Instance.EnableHistoricalOperationDoctrine.Value;
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
