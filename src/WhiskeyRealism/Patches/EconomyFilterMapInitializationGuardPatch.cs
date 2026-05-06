using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla BattleUnits.CampaignDataRuns spins in
    // while (!Economy.UpdateFilterMaps(initialization:true)) with no iteration
    // cap. This guard does not replace CampaignDataRuns; it makes the
    // initialization UpdateFilterMaps surface return complete after repeated
    // no-progress false results or an initialization-only exception.
    [HarmonyPatch(typeof(Economy), "UpdateFilterMaps")]
    internal static class EconomyFilterMapInitializationGuardPatch
    {
        private static readonly CampaignFilterMapInitializationGuard Guard = new CampaignFilterMapInitializationGuard();

        [HarmonyPrefix]
        internal static void Prefix(ref CampaignFilterMapState __state)
        {
            __state = CaptureState();
        }

        [HarmonyPostfix]
        internal static void Postfix(bool initialization, ref bool __result, CampaignFilterMapState __state)
        {
            try
            {
                if (!Enabled()) return;
                var decision = Guard.Observe(initialization, __result, __state, CaptureState());
                if (!decision.ForceComplete) return;

                __result = true;
                OnceLog.Warning(
                    "filter-map-init:" + decision.Reason,
                    "[Patch:FilterMapInit] forced initialization complete after repeated no-progress UpdateFilterMaps result");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("filter-map-init:postfix", "[Patch:FilterMapInit] postfix failed: " + ex.Message);
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(
            Exception __exception,
            bool initialization,
            ref bool __result,
            CampaignFilterMapState __state)
        {
            try
            {
                if (__exception == null || !Enabled()) return __exception;
                var decision = Guard.ObserveException(initialization, __exception, __state);
                if (!decision.ForceComplete) return __exception;

                __result = true;
                OnceLog.Warning(
                    "filter-map-init:" + decision.Reason,
                    "[Patch:FilterMapInit] suppressed initialization UpdateFilterMaps exception and ended bounded startup loop: "
                        + __exception.Message);
                return null;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("filter-map-init:finalizer", "[Patch:FilterMapInit] finalizer failed: " + ex.Message);
                return __exception;
            }
        }

        private static CampaignFilterMapState CaptureState()
        {
            return new CampaignFilterMapState(
                Economy.workforcetownrunthrough,
                Economy.workforceiiprunthrough,
                Economy.workforcecorprunthrough,
                BattleUnits.towns != null ? BattleUnits.towns.Count : -1,
                BattleUnits.iips != null ? BattleUnits.iips.Count : -1,
                BattleUnits.cbuildings != null ? BattleUnits.cbuildings.Count : -1);
        }

        private static bool Enabled()
        {
            try { return Plugin.Instance != null && Plugin.Instance.Enabled.Value; }
            catch { return false; }
        }
    }
}
