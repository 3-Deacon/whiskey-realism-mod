using System;
using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle fallback/retreat loops dereference allattachedunits slots
    // without null guards. Suppress only those NullReferenceException cases.
    internal static class TacticalFallbackRetreatNullGuardPatch
    {
        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalFallbackRetreatNullGuard.Value;
        }

        private static Exception Handle(string methodName, Exception exception)
        {
            if (!Enabled() || exception == null) return exception;
            if (!TacticalBattlefieldBugDiagnostics.ShouldSuppressFallbackRetreatException(methodName, exception))
                return exception;

            OnceLog.Warning(
                "tactical-fallback-retreat-null:" + methodName,
                "[Patch:TacticalFallbackRetreatNullGuard] suppressed NullReferenceException in " + methodName +
                "; vanilla likely had a null allattachedunits slot.");
            return null;
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForRetreats")]
        internal static class Retreats
        {
            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                return Handle("MicroAICheckForRetreats", __exception);
            }
        }

        [HarmonyPatch(typeof(AIBattle), "CheckLineFallbacks")]
        internal static class Fallbacks
        {
            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                return Handle("CheckLineFallbacks", __exception);
            }
        }
    }
}
