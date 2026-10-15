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
    // Bridge layer patch #2 — Postfix on AIArea.CalculateMostValueableAIZones
    // (decompile line 10964). Vanilla, per source area (`this`), picks the
    // highest-scoring candidate area that passes its distance, supply and value
    // gates (10984) and stores it in mostvalueableaiareaclose[aifaction];
    // CheckOffensiveMovements (14166, 14227) reads it as the offensive target
    // when the source area has enemy contact.
    //
    // 2026-10-14 (R7): the patch used to overwrite that slot for EVERY source
    // area with the plan target, bypassing vanilla's gates — local
    // counterattacks on other fronts vanished and units could be sent across
    // the map. Now the plan target replaces vanilla's pick only when it passes
    // vanilla's own candidate gates from this source area and the source area
    // is in the plan target's theater (CampaignMilitaryGates
    // .ShouldRedirectToPlanTarget). Everywhere else vanilla's selection stands.
    //
    // History: v0.2.0 originally targeted AICampaign.UpdateImportanceValues but
    // that method is parameterless, returns bool, and is a chunked per-IIP
    // processor that writes only to importancevaluestemp — wrong shape. v0.2.1
    // moves to the consumer side: override the picker's output directly. Name
    // kept (catalog ordinal #2) for stable cross-references; method target
    // changed.
    [HarmonyPatch]
    internal static class ImportanceValuesPatch
    {
        private static bool _closestAreaDistanceResolved;
        private static Func<AICampaign.AIArea, AICampaign.AIArea, float> _closestAreaDistance;
        private static int _cachedObjectiveId = int.MinValue;
        private static object _cachedAreaList;
        private static AICampaign.AIArea _cachedTargetArea;
        // Vanilla rewrites the pick on every AI-area pass, so the redirect repeats per
        // area per cycle; the verbose row is emitted only when the alliance's target changes.
        private static readonly Dictionary<int, int> LastLoggedTarget = new Dictionary<int, int>();

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
            OnceLog.Info("importance", "ImportanceValuesPatch wired (Postfix on CalculateMostValueableAIZones)");
            try
            {
                if (StrategicCoordinator.Instance == null || __instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(aifaction);
                if (allianceId < 0) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                if (allianceId >= StrategicCoordinator.Instance.CICs.Length) return;
                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                var phase = cic?.ActivePlan?.CurrentPhase;
                if (phase == null || phase.TargetObjectiveId < 0) return;

                var picks = __instance.mostvalueableaiareaclose;
                if (picks == null || aifaction < 0 || aifaction >= picks.Length) return;

                var targetArea = ResolveTargetAIAreaCached(phase.TargetObjectiveId);
                if (targetArea == null) return;
                if (ReferenceEquals(picks[aifaction], targetArea)) return;
                if (!TargetPassesVanillaGates(__instance, targetArea, aifaction)) return;

                picks[aifaction] = targetArea;
                if (Plugin.Instance.VerboseLogging.Value)
                    EmitPlanImportanceTarget(allianceId, phase.TargetObjectiveId);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("importance:postfix", "[Patch:Importance] " + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        private static bool TargetPassesVanillaGates(AICampaign.AIArea source, AICampaign.AIArea target, int aifaction)
        {
            if (!InRange(target.ownpoints, aifaction) || !InRange(target.points, aifaction) ||
                !InRange(target.distancepoints, aifaction) || !InRange(target.importancevalues, aifaction) ||
                !InRange(source.distancepoints, aifaction) || !InRange(source.importancevalues, aifaction))
                return false;

            // averageposition is only meaningful once vanilla has accumulated area
            // samples (avgposcount, 11290-11299); (0,0) would classify as West.
            if (source.avgposcount <= 0 || target.avgposcount <= 0) return false;

            float distance = ClosestAreaDistance(source, target);
            if (float.IsNaN(distance)) return false;

            bool valueClause = target.points[aifaction] >= 0f ||
                target.distancepoints[aifaction] > source.distancepoints[aifaction] ||
                target.importancevalues[aifaction] > source.importancevalues[aifaction];

            return CampaignMilitaryGates.ShouldRedirectToPlanTarget(
                ReferenceEquals(source, target),
                TheaterClassifier.FromPosition(source.averageposition.x, source.averageposition.z),
                TheaterClassifier.FromPosition(target.averageposition.x, target.averageposition.z),
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

        // Vanilla private static AICampaign.GetClosestAreaDistance(AIArea, AIArea)
        // (decompile 14863); the (AIArea, Vector3) overload also exists. Bound
        // once to a typed delegate - no object[] or boxing per call.
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

        private static AICampaign.AIArea ResolveTargetAIAreaCached(int objectiveId)
        {
            var areaList = AICampaign.aiarea;
            if (objectiveId == _cachedObjectiveId && ReferenceEquals(areaList, _cachedAreaList) && _cachedTargetArea != null)
                return _cachedTargetArea;
            _cachedObjectiveId = objectiveId;
            _cachedAreaList = areaList;
            _cachedTargetArea = ResolveTargetAIArea(objectiveId) as AICampaign.AIArea;
            return _cachedTargetArea;
        }

        // Resolve a CampaignObjective UniqueObjectiveID → AIArea by:
        //   1. Walk CampaignObjective.allcampaignobjectives, find matching ID.
        //   2. From the objective's `objectives` (List<object> of Town/IIP refs), grab the first.
        //   3. Read its world position (Component.transform.position).
        //   4. Use vanilla's lookup chain: AICampaign.aiareas (FilterMap) → GetColorOnPos → AIArea.GetAIArea.
        private static object ResolveTargetAIArea(int objectiveId)
        {
            try
            {
                // Step 1: find the CampaignObjective.
                var coType = AccessTools.TypeByName("CampaignObjective");
                if (coType == null) return null;
                var allObjsField = AccessTools.Field(coType, "allcampaignobjectives");
                var allObjs = allObjsField?.GetValue(null) as IList;
                if (allObjs == null) return null;

                object matchingObj = null;
                var idField = AccessTools.Field(coType, "UniqueObjectiveID");
                foreach (var obj in allObjs)
                {
                    if (obj == null) continue;
                    if (idField != null && (int)idField.GetValue(obj) == objectiveId)
                    {
                        matchingObj = obj;
                        break;
                    }
                }
                if (matchingObj == null) return null;

                // Step 2-3: get first target's world position.
                var targetsList = AccessTools.Field(coType, "objectives")?.GetValue(matchingObj) as IList;
                if (targetsList == null || targetsList.Count == 0) return null;
                Vector3? pos = null;
                foreach (var target in targetsList)
                {
                    if (target == null) continue;
                    if (target is Component c) { pos = c.transform.position; break; }
                }
                if (pos == null) return null;

                // Step 4: position → color → AIArea via vanilla helpers.
                var aicType = AccessTools.TypeByName("AICampaign");
                var aiareasField = AccessTools.Field(aicType, "aiareas");
                var aiareas = aiareasField?.GetValue(null);
                if (aiareas == null) return null;

                // Vanilla: public Color GetColorOnPos(Vector3 position, float overridealpha = -1f)
                // (decompile line 33585). Two args; pass -1f to use the default.
                var getColorOnPos = AccessTools.Method(aiareas.GetType(), "GetColorOnPos", new[] { typeof(Vector3), typeof(float) });
                if (getColorOnPos == null) return null;
                var color = getColorOnPos.Invoke(aiareas, new object[] { pos.Value, -1f });
                if (color == null) return null;

                var aiAreaType = AccessTools.TypeByName("AIArea");
                var getAIArea = AccessTools.Method(aiAreaType, "GetAIArea", new[] { typeof(Color) });
                if (getAIArea == null) return null;
                return getAIArea.Invoke(null, new object[] { color });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("importance:resolve-target", "[Patch:Importance] ResolveTargetAIArea failed: " + ex.Message);
                return null;
            }
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
