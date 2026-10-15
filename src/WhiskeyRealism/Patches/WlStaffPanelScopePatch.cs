using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-012. Vanilla CampaignArmyPanel.UpdateStats(reg, ...) (DC:173470)
    // builds the selected group's tooltips. In a W&L career it lists "Staff
    // influences" from EffectMultiplier(9/10/11/14) (DC:173699-173702) and "Staff
    // effects on morale / fighting spirit" from EffectMultiplier(16/17)
    // (DC:173742-173747) without an ownership argument, so an AI or enemy group
    // shows the player's bonuses, while the attrition and readiness lines beside
    // them pass reg.dlcw_isundercommander (DC:173552, 173637). The method only
    // writes UI text (its other calls, UpdateStatusIcons/UpdateVessel/
    // UpdateBranch, read no effects).
    //
    // For a group that is neither the player's nor the map group carrying the
    // player's command (WlPlayerMapGroup), the non-skipping Prefix sets those six
    // running effects to 1 for the call - the value EffectMultiplier returns for
    // a unit that is not the player's - so vanilla omits the lines. The state
    // is stored in __state before the write (Patches/WlEffectSwaps
    // EffectSlotSet); the Finalizer restores unconditionally every slot it
    // wrote and nothing rewrote. The UI method is not re-entrant, so one slot
    // set is reused (no allocation per call). Display only. Rollback:
    // W&L / Enable W&L Staff Panel Scope Fix.
    [HarmonyPatch(typeof(CampaignArmyPanel), "UpdateStats")]
    internal static class WlStaffPanelScopePatch
    {
        private static readonly EffectSlotSet Slots = new EffectSlotSet(WlEconomyFixes.PanelStaffEffects);

        [HarmonyPrefix]
        internal static void Prefix(Regiment reg, out EffectSlotSet __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-staff-panel-scope.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-staff-panel-scope.prefix");
                __state = null;
                try
                {
                    if (!DLC_WL.dlc_scenarioactive || (UnityEngine.Object)reg == null) return;
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value || !Flag()) return;
                    if (!WlEconomyFixes.HidePlayerStaffEffectsOnPanel(true, reg.dlcw_isundercommander, WlPlayerMapGroup.Is(reg))) return;

                    __state = Slots;
                    __state.Prepare(WlEffectArrays.Running(), 1f);
                    __state.Commit();
                    if (__state.Active)
                        OnceLog.Info("wl-staff-panel-scope:first", "[Patch:WlStaffPanelScope] player's staff effects hidden on a group panel that is not the player's");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-staff-panel-scope:prefix", "[Patch:WlStaffPanelScope] prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(EffectSlotSet __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-staff-panel-scope.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-staff-panel-scope.finalizer");
                try
                {
                    __state?.Restore(WlEffectArrays.Running());
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-staff-panel-scope:finalizer", "[Patch:WlStaffPanelScope] restore failed: " + ex.Message);
                }
            }
        }

        private static bool Flag()
        {
            try { return Plugin.EnableWlStaffPanelScopeFix != null && Plugin.EnableWlStaffPanelScopeFix.Value; }
            catch { return false; }
        }
    }
}
