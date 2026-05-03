using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    [HarmonyPatch(typeof(AICampaign), "UpdateImportanceValues")]
    internal static class ImportanceValuesPatch
    {
        [HarmonyPostfix]
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
