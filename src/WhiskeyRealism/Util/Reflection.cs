using System;
using System.Reflection;
using HarmonyLib;

namespace WhiskeyRealism.Util
{
    internal static class Reflection
    {
        private static readonly object Gate = new object();
        private static readonly System.Collections.Generic.Dictionary<string, FieldInfo> FieldCache =
            new System.Collections.Generic.Dictionary<string, FieldInfo>(StringComparer.Ordinal);

        // Reads a (possibly private) instance field by name. FieldInfo lookups are cached per
        // type+name (null results too), and a missing/failed field warns once per type+name.
        internal static T GetField<T>(object instance, string name) where T : class
        {
            if (instance == null) return null;
            Type type = instance.GetType();
            try
            {
                var f = CachedField(type, name);
                if (f == null) { Warn(type, name, "field not found"); return null; }
                return f.GetValue(instance) as T;
            }
            catch (Exception ex) { Warn(type, name, ex.Message); return null; }
        }

        private static FieldInfo CachedField(Type type, string name)
        {
            string key = type.FullName + "." + name;
            lock (Gate)
            {
                FieldInfo field;
                if (FieldCache.TryGetValue(key, out field))
                    return field;
                field = AccessTools.Field(type, name);
                FieldCache[key] = field;
                return field;
            }
        }

        // Key is type + field name: both come from code literals at the call sites, so the
        // key set is bounded (no instance data).
        private static void Warn(Type t, string name, string msg)
        {
            string member = (t == null ? "<null>" : t.FullName) + "." + name;
            OnceLog.Warning("reflection:" + member, "[Reflection] " + member + " — " + msg);
        }
    }

    // Shared decisions for the vanilla null-guard Finalizers (WS P, H-finalizer). A guard
    // may swallow an exception only when (a) its type is one the guarded vanilla body is
    // known to raise and (b) no frame between the throw site and the patched method belongs
    // to this mod — a fault from a Whiskey Realism Prefix/Postfix (or from vanilla code a mod
    // patch called) must surface instead of being hidden as a "vanilla" NRE.
    internal static class VanillaFaultSite
    {
        private static readonly Assembly ModAssembly = typeof(VanillaFaultSite).Assembly;

        internal static bool IsModFault(Exception exception)
        {
            return HasFrameFrom(exception, ModAssembly);
        }

        internal static bool HasFrameFrom(Exception exception, Assembly assembly)
        {
            if (exception == null || assembly == null)
                return false;

            try
            {
                var frames = new System.Diagnostics.StackTrace(exception, false).GetFrames();
                if (frames == null)
                    return false;

                for (int i = 0; i < frames.Length; i++)
                {
                    try
                    {
                        MethodBase method = frames[i] != null ? frames[i].GetMethod() : null;
                        Type declaring = method != null ? method.DeclaringType : null;
                        if (declaring != null && declaring.Assembly == assembly)
                            return true;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        // Null-before-use faults: a C# null deref, or Unity's MissingReferenceException for a
        // destroyed component (e.g. usedtopgroup.transform).
        internal static bool IsNullOrMissingReference(Exception exception)
        {
            return exception is NullReferenceException
                || exception is UnityEngine.MissingReferenceException;
        }

        // Economy.UpdateFilterMaps (decompile 31852) indexes towns/smalltowns/iips/cbuildings
        // by its run-through cursors and dereferences map components and
        // BattlefieldSetup.productionmapsymbols: null/destroyed items or a cursor past a list
        // that shrank are the faults the initialization guard exists for.
        internal static bool IsFilterMapInitializationFault(Exception exception)
        {
            return IsNullOrMissingReference(exception)
                || exception is IndexOutOfRangeException
                || exception is ArgumentOutOfRangeException;
        }

        // Diary.UpdateEvents cycle 4 (decompile ~183037): unittyp <= 13 reads supplystate;
        // otherwise vanilla skips a null groupsupplystate and reads index 2 of a non-null one.
        internal static bool DiarySupplyCycleSafe(int unitType, float[] groupSupplyState)
        {
            if (unitType <= 13)
                return true;
            return groupSupplyState == null || groupSupplyState.Length > 2;
        }
    }
}
