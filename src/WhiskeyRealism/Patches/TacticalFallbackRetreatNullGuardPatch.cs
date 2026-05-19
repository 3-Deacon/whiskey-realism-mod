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

        // BUG-TAC-015: AIBattle.CheckOutOfFireRange (decompile line 4497-4537)
        // guards on unitrange.closestenemyunitfarreg (Regiment) at line 4518
        // but then dereferences unitrange.closestenemyunitfar (GameObject)
        // at lines 4529 and 4532. These are SEPARATE fields on UnitRange
        // (109492 GameObject closestenemyunitfar; 109496 Regiment
        // closestenemyunitfarregreg) — multiple vanilla call sites (36099,
        // 36268, 93370, 93430, 116148, 122925) check the GameObject for null
        // independently, so it can legitimately be null while the Regiment
        // ref is set (unit destroyed mid-fallback). NRE here crashes the
        // per-side microai tick. Suppress with same shape as Retreats and
        // Fallbacks — these are vanilla null-deref hazards, not Whiskey bugs.
        [HarmonyPatch(typeof(AIBattle), "CheckOutOfFireRange")]
        internal static class CheckOutOfFireRange
        {
            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                return Handle("CheckOutOfFireRange", __exception);
            }
        }
    }
}
