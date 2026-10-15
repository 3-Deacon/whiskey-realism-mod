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
