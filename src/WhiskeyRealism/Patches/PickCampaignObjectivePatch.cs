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
            using (TelemetryPerf.Scope("campaign.patch.pick-campaign-objective", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
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
                        EmitHistoricalOperationSkip(allianceId, "no-historical-operation-plan");
                        return false;
                    }
                    return true;
                }

                var phase = cic.ActivePlan.CurrentPhase;
                if (phase == null || phase.TargetObjectiveId < 0)
                {
                    if (HistoricalDoctrineEnabled())
                    {
                        EmitHistoricalOperationSkip(allianceId, "invalid-historical-operation-phase");
                        return false;
                    }
                    return true;
                }

                SetFollowedCampaignObjective(_aifaction, phase.TargetObjectiveId);
                if (Plugin.Instance.VerboseLogging.Value)
                    EmitPlanObjective(allianceId, phase.TargetObjectiveId, cic.ActivePlan.OperationId, phase.PhaseId);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:PickCampObj] " + ex.Message);
                return true;
            }
            } // end using TelemetryPerf.Scope
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

        private static void EmitHistoricalOperationSkip(int allianceId, string reason)
        {
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "HistoricalOperation", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithDecision("skip-vanilla-random", reason, "alliance=" + allianceId + "|action=skip-vanilla-random|reason=" + reason)
                .WithField("reason", reason));
        }

        private static void EmitPlanObjective(int allianceId, int objectiveId, string operationId, string phaseId)
        {
            string safeOperation = string.IsNullOrWhiteSpace(operationId) ? "-" : operationId;
            string safePhase = string.IsNullOrWhiteSpace(phaseId) ? "-" : phaseId;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "Plan", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithPhase(safePhase)
                .WithDecision("pick-campaign-objective", "active-plan", "alliance=" + allianceId + "|objective=" + objectiveId + "|operation=" + safeOperation + "|phase=" + safePhase)
                .WithField("objective", objectiveId)
                .WithField("operation", safeOperation));
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
