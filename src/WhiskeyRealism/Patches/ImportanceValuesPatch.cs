using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #2 — Postfix on AIArea.CalculateMostValueableAIZones
    // (decompile line 10964). After vanilla picks the most valuable next-area
    // for this faction, override the choice to point at our active plan's
    // current-phase target zone if one exists.
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
        [HarmonyTargetMethod]
        internal static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("AIArea");
            return AccessTools.Method(t, "CalculateMostValueableAIZones", new[] { typeof(int) });
        }

        [HarmonyPostfix]
        internal static void Postfix(int aifaction, object __instance)
        {
            OnceLog.Info("importance", "ImportanceValuesPatch wired (Postfix on CalculateMostValueableAIZones)");
            try
            {
                if (StrategicCoordinator.Instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(aifaction);
                if (allianceId < 0) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                if (allianceId >= StrategicCoordinator.Instance.CICs.Length) return;
                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                var phase = cic?.ActivePlan?.CurrentPhase;
                if (phase == null || phase.TargetObjectiveId < 0) return;

                var targetArea = ResolveTargetAIArea(phase.TargetObjectiveId);
                if (targetArea == null) return;

                // Override __instance.mostvalueableaiareaclose[aifaction] to point at the plan target.
                var f = AccessTools.Field(__instance.GetType(), "mostvalueableaiareaclose");
                if (f?.GetValue(__instance) is Array arr && aifaction >= 0 && aifaction < arr.Length)
                {
                    arr.SetValue(targetArea, aifaction);
                    if (Plugin.Instance.VerboseLogging.Value)
                        Plugin.Log.LogInfo($"[Patch:Importance] alliance={allianceId} biased to plan-target obj={phase.TargetObjectiveId}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:Importance] " + ex.Message);
            }
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

                var getColorOnPos = AccessTools.Method(aiareas.GetType(), "GetColorOnPos", new[] { typeof(Vector3) });
                if (getColorOnPos == null) return null;
                var color = getColorOnPos.Invoke(aiareas, new object[] { pos.Value });
                if (color == null) return null;

                var aiAreaType = AccessTools.TypeByName("AIArea");
                var getAIArea = AccessTools.Method(aiAreaType, "GetAIArea", new[] { typeof(Color) });
                if (getAIArea == null) return null;
                return getAIArea.Invoke(null, new object[] { color });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:Importance] ResolveTargetAIArea failed: " + ex.Message);
                return null;
            }
        }
    }
}
