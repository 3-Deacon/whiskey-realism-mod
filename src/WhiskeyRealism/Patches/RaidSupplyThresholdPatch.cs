using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-001. Vanilla AICampaign.RaidForce.SearchObjective (decompile 9130-9185) runs
    // for every raid force each AI frame (UpdateRaidForces 9021-9046, from BattleUnits.Update 79303). It returns at once
    // unless GetGroupSupplyState(raidgroup) >= GamePrefs.minimumsupplyoffensivetargets
    // (9135), but GetGroupSupplyState (14063-14066) halves both of its 0..1 terms and
    // never exceeds 0.5, while the threshold is 0.75 (campaignprefs 1411), so a raised
    // raid force never searches for a target and stays in state 2 for good.
    //
    // For the duration of the call the Prefix puts the threshold on GetGroupSupplyState's
    // own scale (x 0.5, RaidSupplyThreshold), so the gate means max(avg supply,
    // supplystate) >= 0.75, as configured; the Finalizer restores the pref even if
    // vanilla throws. Nothing else in SearchObjective reads the pref. Raid groups under
    // the W&L player's command are left at vanilla (they never set out on their own).
    // The other GetGroupSupplyState consumers (depot trigger 14715, fort builders 9724 /
    // 16381, raid abort 9092) are unchanged; see docs/bug-fixes/vanilla-campaign-logistics.md.
    // Rollback: Campaign Bug Fixes / Enable Raid Supply Threshold Fix.
    [HarmonyPatch(typeof(AICampaign.RaidForce), "SearchObjective")]
    internal static class RaidSupplyThresholdPatch
    {
        internal struct Swap
        {
            internal bool Active;
            internal float Original;
            internal int StateBefore;
        }

        [HarmonyPrefix]
        internal static void Prefix(AICampaign.RaidForce __instance, ref Swap __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.raid-supply-threshold.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.raid-supply-threshold.prefix");
                __state = default(Swap);
                try
                {
                    if (!Enabled() || __instance == null || (UnityEngine.Object)__instance.raidgroup == null) return;
                    if (DLC_WL.dlc_scenarioactive && __instance.raidgroup.dlcw_isundercommander) return;

                    __state.Active = true;
                    __state.Original = GamePrefs.minimumsupplyoffensivetargets;
                    __state.StateBefore = __instance.currentstate;
                    GamePrefs.minimumsupplyoffensivetargets = RaidSupplyThreshold.ThresholdOnGroupSupplyScale(__state.Original);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("raid-supply-threshold:prefix", "[Patch:RaidSupply] prefix failed (vanilla threshold kept): " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(AICampaign.RaidForce __instance, Swap __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.raid-supply-threshold.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.raid-supply-threshold.finalizer");
                if (!__state.Active) return;
                try
                {
                    GamePrefs.minimumsupplyoffensivetargets = __state.Original;
                    if (__instance != null && __state.StateBefore == 2 && __instance.currentstate == 3)
                        OnceLog.Info("raid-supply-threshold:target", "[Patch:RaidSupply] a raid force passed the rescaled supply gate and picked a target");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("raid-supply-threshold:finalizer", "[Patch:RaidSupply] finalizer failed: " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableRaidSupplyThresholdFix != null
                    && Plugin.EnableRaidSupplyThresholdFix.Value;
            }
            catch { return false; }
        }
    }
}
