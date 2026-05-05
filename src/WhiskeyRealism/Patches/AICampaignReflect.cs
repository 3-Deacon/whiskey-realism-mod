using HarmonyLib;

namespace WhiskeyRealism.Patches
{
    // Shared reflection helper for patches that need to resolve an aifaction
    // index into an alliance ID. AICampaign.aifaction is a private static
    // List<AIFaction> and AIFaction is a private inner class — both
    // reflection-only.
    internal static class AICampaignReflect
    {
        internal static int GetAllianceId(int aifactionIndex)
        {
            try
            {
                var aicType = AccessTools.TypeByName("AICampaign");
                if (aicType == null) return -1;
                var listField = AccessTools.Field(aicType, "aifaction");
                var list = listField?.GetValue(null) as System.Collections.IList;
                if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return -1;
                var faction = list[aifactionIndex];
                var allianceField = AccessTools.Field(faction.GetType(), "allianceid");
                return allianceField != null ? (int)allianceField.GetValue(faction) : -1;
            }
            catch { return -1; }
        }

        // Returns the raw AIFaction object at the given index, or null on failure.
        // AIFaction is a private inner class; callers must use reflection to read its fields.
        internal static object GetFaction(int aifactionIndex)
        {
            try
            {
                var aicType = AccessTools.TypeByName("AICampaign");
                if (aicType == null) return null;
                var listField = AccessTools.Field(aicType, "aifaction");
                var list = listField?.GetValue(null) as System.Collections.IList;
                if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return null;
                return list[aifactionIndex];
            }
            catch { return null; }
        }
    }
}
