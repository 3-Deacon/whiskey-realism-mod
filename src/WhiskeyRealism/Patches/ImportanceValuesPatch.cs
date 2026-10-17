using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #2 - Postfix on AIArea.CalculateMostValueableAIZones (decompile 10964, rewritten by stage G, G5a).
    // Vanilla, per source area (`this`), picks the highest-scoring candidate area that passes its distance, supply and value
    // gates (10984) and stores it in mostvalueableaiareaclose[aifaction]; it also stores the best friendly area with no enemy
    // in mostvaluealbeaiareaclosedistanceonly[aifaction] (10990-11008). CheckOffensiveMovements reads the first as the
    // offensive target when the source area has enemy contact and the second when it has none (14243-14248, 14204-14227).
    //
    // The campaign planner steers them (spec C7): when `this` area lies in the MainEffort sector of the faction's alliance and the main
    // effort is not staged (SteeringRules.ImportanceSteers on the published plan; the Support sectors steer nothing), BOTH pointers are set to the area containing the sector's
    // target - but only when that area passes vanilla's own candidate gates from `this` (the distance and
    // ownpoints <= supplycapacity checks and the value clause of 10984, CampaignMilitaryGates.ShouldRedirectToPlanTarget;
    // vanilla's live points / distancepoints / importancevalues are its own gate inputs here, not planner inputs, so the
    // fog-honest rule is untouched). Everywhere else vanilla's selection stands, so local counterattacks keep working
    // and nothing is pulled cross-map. Vanilla's supply gate (ownpoints <= supplycapacity, 10983) relies on the zone
    // supply-capacity fix of fix/campaign-logistics-bugs (G5a builds on it). Reads the published plan only, and only once the tempo
    // roll has released the decision (CampaignWithholdView.TrySteering: before release vanilla's own pick stands, so no formation
    // is pointed at the target ahead of the main effort) (a stale plan, a
    // pre-war scenario and an alliance other than 0 and 1 steer nothing). Rollback: Enable Campaign Planner Executors.
    //
    // History: v0.2.0 targeted AICampaign.UpdateImportanceValues (wrong shape: parameterless, chunked, writes only
    // importancevaluestemp); v0.2.1 moved to the consumer side; the phased-plan CIC target and its objective-id area lookup
    // are gone with G5a. Name kept (catalog ordinal #2) for stable cross-references.
    [HarmonyPatch]
    internal static class ImportanceValuesPatch
    {
        static ImportanceValuesPatch()
        {
            CampaignScope.Register("importance-values", ResetForNewCampaign);
        }

        private static bool _closestAreaDistanceResolved;
        private static Func<AICampaign.AIArea, AICampaign.AIArea, float> _closestAreaDistance;
        // The sector target's area, resolved once per published plan (vanilla calls this Postfix once per AI area per job-4
        // pass, 11408-11414, and every steered sector shares the main target). A plan is a fresh object each review.
        private static TheaterPlan _targetPlan;
        private static float _targetX, _targetZ;
        private static AICampaign.AIArea _targetArea;
        // The verbose row is emitted only when the alliance's main objective changes.
        private static readonly Dictionary<int, int> LastLoggedTarget = new Dictionary<int, int>();

        internal static void ResetForNewCampaign()
        {
            _targetPlan = null;
            _targetArea = null;
            LastLoggedTarget.Clear();
        }

        [HarmonyTargetMethod]
        internal static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AICampaign.AIArea), "CalculateMostValueableAIZones", new[] { typeof(int) });
        }

        [HarmonyPostfix]
        internal static void Postfix(int aifaction, AICampaign.AIArea __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.importance-values", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.importance-values");
                OnceLog.Info("importance", "ImportanceValuesPatch wired (Postfix on CalculateMostValueableAIZones)");
                try
                {
                    if (__instance == null || !CampaignPlannerRuntime.ExecutorsEnabled()) return;
                    int alliance = PlannerFactionMap.AllianceOf(aifaction);
                    if (alliance < 0 || alliance > 1) return;
                    if (!CampaignWithholdView.TrySteering(alliance, out var plan, out var sectors)) return;

                    var picks = __instance.mostvalueableaiareaclose;
                    if (picks == null || aifaction < 0 || aifaction >= picks.Length) return;
                    int sourceIndex = AICampaign.aiarea != null ? AICampaign.aiarea.IndexOf(__instance) : -1;
                    if (!SteeringRules.ImportanceSteers(plan, sectors, sourceIndex, out float targetX, out float targetZ)) return;

                    var targetArea = TargetAreaOf(plan, targetX, targetZ);
                    if (targetArea == null || !TargetPassesVanillaGates(__instance, targetArea, aifaction)) return;

                    picks[aifaction] = targetArea;
                    var distanceOnly = __instance.mostvaluealbeaiareaclosedistanceonly;
                    if (distanceOnly != null && aifaction < distanceOnly.Length) distanceOnly[aifaction] = targetArea;
                    if (Plugin.Instance.VerboseLogging.Value) EmitPlanImportanceTarget(alliance, plan.MainObjectiveId);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("importance:postfix", "[Patch:Importance] " + ex.Message);
                }
            }
        }

        // The area holding the target point: vanilla's own colour-map lookup (the one InitializeAIAreas fills the area list
        // with, 11265-11270), cached for the published plan it was asked for.
        private static AICampaign.AIArea TargetAreaOf(TheaterPlan plan, float x, float z)
        {
            if (ReferenceEquals(plan, _targetPlan) && x == _targetX && z == _targetZ) return _targetArea;
            AICampaign.AIArea area = null;
            var map = AICampaign.aiareas;
            if ((UnityEngine.Object)map != null)
            {
                Color color = map.GetColorOnPos(new Vector3(x, 0f, z), -1f);
                if (color.a > 0f) area = AICampaign.AIArea.GetAIArea(color);
            }
            _targetPlan = plan;
            _targetX = x;
            _targetZ = z;
            _targetArea = area;
            return area;
        }

        private static bool TargetPassesVanillaGates(AICampaign.AIArea source, AICampaign.AIArea target, int aifaction)
        {
            if (ReferenceEquals(source, target)) return false;
            if (!InRange(target.ownpoints, aifaction) || !InRange(target.points, aifaction) || // vanilla's own 10984 gate
                !InRange(target.distancepoints, aifaction) || !InRange(target.importancevalues, aifaction) || // vanilla's own 10984 gate
                !InRange(source.distancepoints, aifaction) || !InRange(source.importancevalues, aifaction)) // vanilla's own 10984 gate
                return false;

            // averageposition is only meaningful once vanilla has accumulated area samples (avgposcount, 11290-11299).
            if (source.avgposcount <= 0 || target.avgposcount <= 0) return false;

            float distance = ClosestAreaDistance(source, target);
            if (float.IsNaN(distance)) return false;

            bool valueClause = target.points[aifaction] >= 0f || // vanilla's own 10984 gate
                target.distancepoints[aifaction] > source.distancepoints[aifaction] || // vanilla's own 10984 gate
                target.importancevalues[aifaction] > source.importancevalues[aifaction];

            return CampaignMilitaryGates.ShouldRedirectToPlanTarget(
                false,
                true,
                distance,
                GamePrefs.aimaximumdistancetosearchforunitrelocations * GamePrefs.rangefactoroffensivetargets,
                target.ownpoints[aifaction],
                target.supplycapacity,
                valueClause);
        }

        private static bool InRange(float[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length;
        }

        // Vanilla private static AICampaign.GetClosestAreaDistance(AIArea, AIArea) (decompile 14863); the (AIArea, Vector3)
        // overload also exists. Bound once to a typed delegate - no object[] or boxing per call.
        private static float ClosestAreaDistance(AICampaign.AIArea source, AICampaign.AIArea target)
        {
            if (!_closestAreaDistanceResolved)
            {
                _closestAreaDistanceResolved = true;
                var method = AccessTools.Method(
                    typeof(AICampaign),
                    "GetClosestAreaDistance",
                    new[] { typeof(AICampaign.AIArea), typeof(AICampaign.AIArea) });
                if (method != null)
                    _closestAreaDistance = (Func<AICampaign.AIArea, AICampaign.AIArea, float>)Delegate.CreateDelegate(
                        typeof(Func<AICampaign.AIArea, AICampaign.AIArea, float>), method);
                if (_closestAreaDistance == null)
                    OnceLog.Warning("importance:closest-area-distance",
                        "[Patch:Importance] AICampaign.GetClosestAreaDistance(AIArea, AIArea) not found; plan-target redirect disabled");
            }
            if (_closestAreaDistance == null) return float.NaN;
            return _closestAreaDistance(source, target);
        }

        private static void EmitPlanImportanceTarget(int allianceId, int objectiveId)
        {
            if (LastLoggedTarget.TryGetValue(allianceId, out int last) && last == objectiveId) return;
            LastLoggedTarget[allianceId] = objectiveId;
            string signature = "alliance=" + allianceId +
                "|action=importance-target" +
                "|objective=" + objectiveId;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "Plan", TelemetrySeverity.Info, ev => ev
                .WithAlliance(allianceId)
                .WithDecision("importance-target", "active-plan-target", signature)
                .WithField("objective", objectiveId));
        }
    }
}
