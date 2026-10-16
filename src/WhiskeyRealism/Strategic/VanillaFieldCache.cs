using System;
using System.Reflection;
using HarmonyLib;

namespace WhiskeyRealism.Strategic
{
    // Cached FieldInfo lookups keyed by (declaring runtime type, field name) for
    // vanilla objects read via reflection - AICampaign.AIFaction and its nested
    // types, AICampaign, Regiment fields read from untyped lists - so per-call
    // campaign code does not re-run AccessTools.Field. A missing field caches as
    // null (callers handle null).
    internal static class VanillaFieldCache
    {
        private static readonly System.Collections.Generic.Dictionary<(Type, string), FieldInfo> _fields =
            new System.Collections.Generic.Dictionary<(Type, string), FieldInfo>();

        internal static FieldInfo Field(Type owner, string name)
        {
            if (owner == null || name == null) return null;
            var key = (owner, name);
            if (_fields.TryGetValue(key, out var field)) return field;
            field = AccessTools.Field(owner, name);
            _fields[key] = field;
            return field;
        }

        internal static object Get(object instance, string name)
        {
            return instance == null ? null : Field(instance.GetType(), name)?.GetValue(instance);
        }

        internal static System.Collections.IList List(object instance, string name)
        {
            return Get(instance, name) as System.Collections.IList;
        }
    }
}
