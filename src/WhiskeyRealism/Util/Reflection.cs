using System;
using System.Reflection;
using HarmonyLib;

namespace WhiskeyRealism.Util
{
    internal static class Reflection
    {
        internal static T GetStaticField<T>(Type t, string name) where T : class
        {
            try
            {
                var f = AccessTools.Field(t, name);
                if (f == null) { Warn(t, name, "static field not found"); return null; }
                return f.GetValue(null) as T;
            }
            catch (Exception ex) { Warn(t, name, ex.Message); return null; }
        }

        internal static T GetField<T>(object instance, string name) where T : class
        {
            if (instance == null) return null;
            try
            {
                var f = AccessTools.Field(instance.GetType(), name);
                if (f == null) { Warn(instance.GetType(), name, "field not found"); return null; }
                return f.GetValue(instance) as T;
            }
            catch (Exception ex) { Warn(instance.GetType(), name, ex.Message); return null; }
        }

        internal static int GetIntField(object instance, string name, int fallback = 0)
        {
            if (instance == null) return fallback;
            try
            {
                var f = AccessTools.Field(instance.GetType(), name);
                if (f == null) { Warn(instance.GetType(), name, "int field not found"); return fallback; }
                return (int)f.GetValue(instance);
            }
            catch (Exception ex) { Warn(instance.GetType(), name, ex.Message); return fallback; }
        }

        internal static void SetField(object instance, string name, object value)
        {
            if (instance == null) return;
            try
            {
                var f = AccessTools.Field(instance.GetType(), name);
                if (f == null) { Warn(instance.GetType(), name, "field not found for SetField"); return; }
                f.SetValue(instance, value);
            }
            catch (Exception ex) { Warn(instance.GetType(), name, ex.Message); }
        }

        internal static MethodInfo GetMethod(Type t, string name, Type[] argTypes = null)
        {
            try
            {
                var m = (argTypes == null)
                    ? AccessTools.Method(t, name)
                    : AccessTools.Method(t, name, argTypes);
                if (m == null) Warn(t, name, "method not found");
                return m;
            }
            catch (Exception ex) { Warn(t, name, ex.Message); return null; }
        }

        private static void Warn(Type t, string name, string msg)
        {
            Plugin.Log.LogWarning("[Reflection] " + (t == null ? "<null>" : t.FullName)
                + "." + name + " — " + msg);
        }
    }
}
