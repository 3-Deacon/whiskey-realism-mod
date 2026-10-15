using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.PickCampaignObjective(int _aifaction) (decompile 17769): returns for
    // alliance >= 2, clears followedcampaignobjective once IDAccomplished, and when it is < 0
    // picks a uniformly random entry from CampaignObjective.GetAvailableObjectives(alliance).
    // followedcampaignobjective feeds UpdateImportanceValues (14922/14961) and recruiting
    // (12337) through the objective's towns/IIPs.
    //
    // Prefix (existing Prefix-skip, narrowed by R5): when the CIC has an active plan phase of
    // a non-defensive operation the mod writes that target and skips the random pick.
    // Defensive-posture operations leave the pick to vanilla. With no plan (no historical
    // profile and no resolvable generic target) vanilla's pick runs; if the current
    // followedcampaignobjective is one the mod wrote, it is cleared first so vanilla
    // actually re-picks instead of keeping the stale mod target.
    [HarmonyPatch(typeof(AICampaign), "PickCampaignObjective")]
    internal static class PickCampaignObjectivePatch
    {
        private static FieldInfo _aifactionField;
        private static FieldInfo _allianceIdField;
        private static FieldInfo _followedField;
        // aifaction index -> objective id the mod last wrote (cleared on campaign reset).
        private static readonly Dictionary<int, int> LastWrittenByMod = new Dictionary<int, int>();

        internal static void ResetForNewCampaign()
        {
            LastWrittenByMod.Clear();
        }

        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.pick-campaign-objective", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                OnceLog.Info("pickcampobj", "PickCampaignObjectivePatch wired");
                try
                {
                    var coordinator = StrategicCoordinator.Instance;
                    if (coordinator == null) return true;

                    var faction = ResolveFaction(_aifaction);
                    if (faction == null) return true;
                    int allianceId = Convert.ToInt32(_allianceIdField.GetValue(faction));
                    if (allianceId < 0 || allianceId >= coordinator.CICs.Length) return true;

                    int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                    if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return true;

                    var cic = coordinator.CICs[allianceId];
                    var phase = cic?.ActivePlan?.CurrentPhase;
                    int planTarget = phase != null ? phase.TargetObjectiveId : -1;
                    int current = Convert.ToInt32(_followedField.GetValue(faction));
                    int lastWritten = LastWrittenByMod.TryGetValue(_aifaction, out int w) ? w : -1;

                    // Defensive-posture operations never write the objective: in vanilla it is an
                    // offensive importance weight (UpdateImportanceValues 14922/14961).
                    bool steers = phase != null && HistoricalOperationCatalog.SteersCampaignObjective(cic.ActivePlan.OperationId);
                    var steer = CampaignObjectiveSteering.Decide(steers, planTarget, current, lastWritten);
                    if (steer.WriteFollowed != CampaignObjectiveSteering.NoWrite)
                    {
                        _followedField.SetValue(faction, steer.WriteFollowed);
                        if (steer.WriteFollowed >= 0) LastWrittenByMod[_aifaction] = steer.WriteFollowed;
                        else LastWrittenByMod.Remove(_aifaction);
                    }

                    if (steer.RunVanilla)
                    {
                        if (cic != null) EmitVanillaPick(allianceId, phase == null ? "no-plan" : (steers ? "invalid-phase" : "defensive-operation"));
                        return true;
                    }

                    if (Plugin.Instance != null && Plugin.Instance.VerboseLogging.Value)
                        EmitPlanObjective(allianceId, planTarget, cic.ActivePlan.OperationId, phase.PhaseId);
                    return false;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("pickcampobj:failed", "[Patch:PickCampObj] " + ex.Message);
                    return true;
                }
            }
        }

        private static object ResolveFaction(int aifactionIndex)
        {
            if (_aifactionField == null) _aifactionField = AccessTools.Field(typeof(AICampaign), "aifaction");
            var list = _aifactionField?.GetValue(null) as IList;
            if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return null;
            var faction = list[aifactionIndex];
            if (faction == null) return null;
            var type = faction.GetType();
            if (_allianceIdField == null || _allianceIdField.DeclaringType != type)
            {
                _allianceIdField = AccessTools.Field(type, "allianceid");
                _followedField = AccessTools.Field(type, "followedcampaignobjective");
            }
            if (_allianceIdField == null || _followedField == null)
            {
                OnceLog.Warning("pickcampobj:fields", "[Patch:PickCampObj] AIFaction.allianceid/followedcampaignobjective not found");
                return null;
            }
            return faction;
        }

        private static void EmitVanillaPick(int allianceId, string reason)
        {
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "HistoricalOperation", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithDecision("vanilla-pick", reason, "alliance=" + allianceId + "|action=vanilla-pick|reason=" + reason)
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
    }
}
