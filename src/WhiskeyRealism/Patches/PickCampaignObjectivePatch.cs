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
    // Prefix (existing Prefix-skip, narrowed by R5): when the CIC's current phase is in an
    // offensive posture (live, after router overrides) the mod writes that target and skips
    // the random pick. Defensive phases leave the pick to vanilla. With no plan (no historical
    // profile and no resolvable generic target) vanilla's pick runs; if the current
    // followedcampaignobjective is one the mod wrote, it is cleared first so vanilla
    // actually re-picks instead of keeping the stale mod target.
    [HarmonyPatch(typeof(AICampaign), "PickCampaignObjective")]
    internal static class PickCampaignObjectivePatch
    {
        static PickCampaignObjectivePatch()
        {
            CampaignScope.Register("pick-objective", ResetForNewCampaign);
        }

        private static FieldInfo _aifactionField;
        private static FieldInfo _allianceIdField;
        private static FieldInfo _followedField;
        // Per alliance: objective id the mod last wrote. Patch-local bookkeeping of its own
        // writes; the coordinator snapshots/restores it with the sidecar so a stale mod-written
        // objective is still recognized after a reload.
        private static readonly int[] LastWrittenByMod = { -1, -1 };
        // The verbose plan-objective row repeats every AI cycle while the plan steers;
        // emit it only when the alliance's objective/operation/phase signature changes.
        private static readonly Dictionary<int, string> LastLoggedPlanObjective = new Dictionary<int, string>();

        internal static void ResetForNewCampaign()
        {
            LastWrittenByMod[0] = -1;
            LastWrittenByMod[1] = -1;
            LastLoggedPlanObjective.Clear();
        }

        internal static int[] SnapshotWrittenByMod()
        {
            return new[] { LastWrittenByMod[0], LastWrittenByMod[1] };
        }

        internal static void RestoreWrittenByMod(int[] written)
        {
            for (int a = 0; a < LastWrittenByMod.Length; a++)
                LastWrittenByMod[a] = written != null && a < written.Length ? written[a] : -1;
        }

        [HarmonyPrefix]
        internal static bool Prefix(int _aifaction)
        {
            using (TelemetryPerf.Scope("campaign.patch.pick-campaign-objective", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.pick-campaign-objective");
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
                    int lastWritten = LastWrittenByMod[allianceId];

                    // Steer only while the LIVE phase posture is offensive (router overrides
                    // included); a defensive phase releases the objective to vanilla.
                    bool steers = CampaignObjectiveSteering.PhaseSteers(phase);
                    var steer = CampaignObjectiveSteering.Decide(steers, planTarget, current, lastWritten);
                    if (steer.WriteFollowed != CampaignObjectiveSteering.NoWrite)
                    {
                        _followedField.SetValue(faction, steer.WriteFollowed);
                        LastWrittenByMod[allianceId] = steer.WriteFollowed;
                    }

                    if (steer.RunVanilla)
                    {
                        if (cic != null) EmitVanillaPick(allianceId, phase == null ? "no-plan" : (phase.TargetObjectiveId < 0 ? "invalid-phase" : "defensive-phase"));
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
            string signature = "alliance=" + allianceId + "|objective=" + objectiveId + "|operation=" + safeOperation + "|phase=" + safePhase;
            if (LastLoggedPlanObjective.TryGetValue(allianceId, out string last) && last == signature) return;
            LastLoggedPlanObjective[allianceId] = signature;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "Plan", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithPhase(safePhase)
                .WithDecision("pick-campaign-objective", "active-plan", signature)
                .WithField("objective", objectiveId)
                .WithField("operation", safeOperation));
        }
    }
}
