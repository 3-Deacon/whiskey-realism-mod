using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // DEFERRED to v0.2.1 — vanilla AICampaign.UpdateImportanceValues() is parameterless,
    // returns bool, and is a chunked per-IIP/cbuild/town processor that writes to
    // `importancevaluestemp` (not the final `importancevalues`). Postfixing it would
    // fire repeatedly per tick, double-counting our bias. Wrong target.
    //
    // Right target for v0.2.1: Prefix on AIArea.CalculateMostValueableAIZones(int aifaction)
    // that pre-biases aiarea[i].importancevalues[aifaction] for plan-target zones, since
    // that's the method that READS the final array to make the zone-pick decision.
    //
    // Class kept (without [HarmonyPatch] attribute, so PatchAll skips it) so the v0.2.1
    // redesign has a stable file to land in.
    internal static class ImportanceValuesPatch
    {
        [Obsolete("Disabled in v0.2.0 — wrong vanilla target. Redesign in v0.2.1.")]
        internal static void Postfix(int _aifaction)
        {
            OnceLog.Info("importance", "ImportanceValuesPatch wired");
            try
            {
                if (StrategicCoordinator.Instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                if (cic == null || cic.Theaters.Count == 0) return;

                var areaListField = AccessTools.Field(typeof(AICampaign), "aiarea");
                var areaList = areaListField?.GetValue(null) as System.Collections.IList;
                if (areaList == null) return;

                for (int areaId = 0; areaId < areaList.Count; areaId++)
                {
                    var area = areaList[areaId];
                    if (area == null) continue;

                    var theater = cic.Theaters[0];
                    float multiplier = theater.GetZoneRelevance(areaId);
                    if (Math.Abs(multiplier - 1.0f) < 0.001f) continue;

                    var importanceField = AccessTools.Field(area.GetType(), "importancevalues");
                    if (importanceField == null) continue;
                    var importanceArr = importanceField.GetValue(area) as float[];
                    if (importanceArr == null) continue;
                    if (allianceId < 0 || allianceId >= importanceArr.Length) continue;
                    importanceArr[allianceId] *= multiplier;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Patch:Importance] " + ex.Message);
            }
        }
    }

    // Shared reflection helper used by patches that take int _aifaction.
    internal static class AICampaignReflect
    {
        internal static int GetAllianceId(int aifactionIndex)
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
    }
}
