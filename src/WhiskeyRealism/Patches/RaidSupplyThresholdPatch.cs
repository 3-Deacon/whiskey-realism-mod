using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-001. Vanilla AICampaign.RaidForce.SearchObjective (decompile 9130-9185) runs
    // for every raid force each frame (UpdateRaidForces 9021-9046, from BattleUnits.Update
    // 79303 while debug_raidgroupsactive, true at 64459). It returns at once unless
    // GetGroupSupplyState(raidgroup) >= GamePrefs.minimumsupplyoffensivetargets (9135), but
    // GetGroupSupplyState (14063-14066) halves both of its 0..1 terms and never exceeds 0.5,
    // while the threshold is 0.75 (campaignprefs 1411), so a raised raid force never
    // searches for a target and stays in state 2 for good.
    //
    // For the duration of the call the Prefix puts the threshold on GetGroupSupplyState's
    // own scale (x 0.5) through a ValueSwap; the Finalizer restores it, even if vanilla
    // throws. Nothing else in SearchObjective reads the pref (its other reader, 14019, is in
    // UpdateMicroMovementInOffensive). Raid groups under the W&L player's command keep
    // vanilla's gate (RaidSupplyThreshold.ShouldRescale). The other GetGroupSupplyState
    // consumers (depot trigger 14715, fort builders 9724 / 16381, raid abort 9092) are
    // unchanged; see docs/bug-fixes/vanilla-campaign-logistics.md.
    // Rollback: Campaign Bug Fixes / Enable Raid Supply Threshold Fix.
    [HarmonyPatch(typeof(AICampaign.RaidForce), "SearchObjective")]
    internal static class RaidSupplyThresholdPatch
    {
        private static readonly ValueSwap<float> Threshold = new ValueSwap<float>(
            () => GamePrefs.minimumsupplyoffensivetargets,
            value => GamePrefs.minimumsupplyoffensivetargets = value);

        [HarmonyPrefix]
        internal static void Prefix(AICampaign.RaidForce __instance, ref int __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.raid-supply-threshold.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.raid-supply-threshold.prefix");
                __state = -1;
                try
                {
                    bool hasGroup = __instance != null && (UnityEngine.Object)__instance.raidgroup != null;
                    if (!RaidSupplyThreshold.ShouldRescale(Enabled(), hasGroup, DLC_WL.dlc_scenarioactive, hasGroup && __instance.raidgroup.dlcw_isundercommander)) return;
                    __state = __instance.currentstate;
                    Threshold.Apply(RaidSupplyThreshold.ThresholdOnGroupSupplyScale(GamePrefs.minimumsupplyoffensivetargets));
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("raid-supply-threshold:prefix", "[Patch:RaidSupply] prefix failed (vanilla threshold kept): " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(AICampaign.RaidForce __instance, int __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.raid-supply-threshold.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.raid-supply-threshold.finalizer");
                try
                {
                    string failure = Threshold.Restore();
                    if (failure != null)
                        OnceLog.Warning("raid-supply-threshold:restore", "[Patch:RaidSupply] restoring the threshold failed: " + failure);
                    if (__instance != null && __state == 2 && __instance.currentstate == 3)
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
