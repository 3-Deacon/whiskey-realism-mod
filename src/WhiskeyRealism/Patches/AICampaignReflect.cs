using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace WhiskeyRealism.Patches
{
    // Shared reflection helper for patches that need to resolve an aifaction
    // index into an alliance ID. AICampaign.aifaction is a private static
    // List<AIFaction> and AIFaction is a private inner class — both
    // reflection-only.
    internal static class AICampaignReflect
    {
        private static Type _aicType;
        private static FieldInfo _aifactionField;
        private static readonly Dictionary<Type, FieldInfo> _allianceIdFields =
            new Dictionary<Type, FieldInfo>();

        internal static int GetAllianceId(int aifactionIndex)
        {
            try
            {
                var list = GetAifactionList();
                if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return -1;
                var faction = list[aifactionIndex];
                if (faction == null) return -1;
                var allianceField = GetAllianceIdField(faction.GetType());
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
                var list = GetAifactionList();
                if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return null;
                return list[aifactionIndex];
            }
            catch { return null; }
        }

        private static IList GetAifactionList()
        {
            if (_aicType == null) _aicType = AccessTools.TypeByName("AICampaign");
            if (_aicType == null) return null;
            if (_aifactionField == null) _aifactionField = AccessTools.Field(_aicType, "aifaction");
            return _aifactionField?.GetValue(null) as IList;
        }

        private static FieldInfo GetAllianceIdField(Type factionType)
        {
            if (factionType == null) return null;
            if (_allianceIdFields.TryGetValue(factionType, out var field)) return field;
            field = AccessTools.Field(factionType, "allianceid");
            _allianceIdFields[factionType] = field;
            return field;
        }
    }
}
