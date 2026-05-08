using HarmonyLib;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.CheckLineFallbacks(Regiment) at decompile line 5118 writes
    // fallback paths/movement modes for line units in morale danger. This Postfix
    // observes only - no writes - and emits a first-fire marker so smoke can verify
    // the patch loaded.
    [HarmonyPatch(typeof(AIBattle), "CheckLineFallbacks")]
    public static class B8CheckLineFallbacksObserverPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Regiment aigroup)
        {
            try
            {
                OnceLog.Info("b8-check-line-fallbacks", "");
            }
            catch (System.Exception ex)
            {
                OnceLog.Warning("b8-check-line-fallbacks-error",
                    "[B8] CheckLineFallbacks observer error: " + ex.Message);
            }
        }
    }
}
