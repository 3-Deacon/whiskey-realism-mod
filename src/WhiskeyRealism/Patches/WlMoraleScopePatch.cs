using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-011 (morale). Vanilla private Regiment.UpdateMoraleCampaign(_inbattle,
    // parentcampaignunit) (DC:127816) runs per regiment from its campaign group's
    // UpdateGroupStrength (DC:120375). In a W&L career it multiplies the morale
    // rally rate by EffectMultiplier(16, 1, parentcampaignunit.dlcw_isundercommander)
    // (DC:127874) and by camp station 7 for the regiment's own flag (DC:127875-
    // 127877). For a player below the map group the parent is the AI group,
    // never flagged (DC:91203-91217), so the player's morale staff/actions do
    // nothing while camp station 7 works. The rate multiplies morale recovery
    // (`morale += ... * num3`), the right direction; no sign question.
    //
    // While vanilla works on one of the player's own regiments inside the map
    // group carrying the player's command (WlPlayerMapGroup), the non-skipping
    // Prefix sets that group's dlcw_isundercommander for the call; the Finalizer
    // clears it even if vanilla throws. AI peers in the group keep vanilla
    // values. Rollback: W&L / Enable W&L Staff Scope Fix.
    [HarmonyPatch(typeof(Regiment), "UpdateMoraleCampaign")]
    internal static class WlMoraleScopePatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance, Regiment parentcampaignunit, out Regiment __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-morale-scope.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-morale-scope.prefix");
                __state = null;
                try
                {
                    if (!DLC_WL.dlc_scenarioactive || (UnityEngine.Object)parentcampaignunit == null || (UnityEngine.Object)__instance == null) return;
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value || !Flag()) return;
                    if (!WlEconomyFixes.RaiseGroupForPlayerUnit(true, parentcampaignunit.dlcw_isundercommander,
                            __instance.dlcw_isundercommander, WlPlayerMapGroup.Is(parentcampaignunit))) return;

                    parentcampaignunit.dlcw_isundercommander = true;
                    __state = parentcampaignunit;
                    OnceLog.Info("wl-morale-scope:first",
                        "[Patch:WlMoraleScope] player's morale effect applied to the player's regiment inside group=" + parentcampaignunit.name);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-morale-scope:prefix", "[Patch:WlMoraleScope] prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(Regiment __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-morale-scope.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-morale-scope.finalizer");
                try
                {
                    if ((UnityEngine.Object)__state != null)
                        __state.dlcw_isundercommander = false;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-morale-scope:finalizer", "[Patch:WlMoraleScope] restore failed: " + ex.Message);
                }
            }
        }

        private static bool Flag()
        {
            try { return Plugin.EnableWlStaffScopeFix != null && Plugin.EnableWlStaffScopeFix.Value; }
            catch { return false; }
        }
    }
}
