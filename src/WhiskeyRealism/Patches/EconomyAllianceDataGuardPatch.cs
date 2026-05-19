using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla UpdateEconomyAllianceData aggregates IIP, corporate-building,
    // and town economy data for the current filter-map iterators. A null entry
    // can throw before UpdateFilterMaps advances those iterators, causing the
    // same Player.log NRE every frame. This Finalizer suppresses only that NRE.
    [HarmonyPatch(typeof(Economy), "UpdateEconomyAllianceData")]
    internal static class EconomyAllianceDataGuardPatch
    {
        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception)
        {
            using (TelemetryPerf.Scope("campaign.patch.economy-alliance-data-guard", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return __exception;
                    if (!EconomyAllianceDataGuard.ShouldSuppress(__exception)) return __exception;

                    OnceLog.Warning(
                        "economy-alliance-data:nre",
                        "[Patch:EconomyAllianceData] suppressed vanilla NullReferenceException in UpdateEconomyAllianceData; UpdateFilterMaps will advance iterator "
                            + IteratorState());
                    return null;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "economy-alliance-data:guard-failed",
                        "[Patch:EconomyAllianceData] finalizer guard failed: " + ex.Message);
                    return __exception;
                }
            }
        }

        private static string IteratorState()
        {
            return EconomyAllianceDataGuard.FormatIteratorState(
                Economy.workforceiiprunthrough,
                BattleUnits.iips != null ? BattleUnits.iips.Count : -1,
                Economy.workforcecorprunthrough,
                BattleUnits.cbuildings != null ? BattleUnits.cbuildings.Count : -1,
                Economy.workforcetownrunthrough,
                BattleUnits.towns != null ? BattleUnits.towns.Count : -1);
        }
    }
}
