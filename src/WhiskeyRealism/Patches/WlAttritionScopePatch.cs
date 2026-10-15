using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-005. Vanilla Regiment.UpdateCampaignUnitStats(groupunit, state)
    // (DC:114965) runs for every regiment of every campaign group (DC:120260,
    // 120409). In a W&L career it multiplies the sickness target and the desertion
    // rate by DLC_WL.EffectMultiplier(22) with the default isplayerunit = true
    // (DC:115149-115151, 115196-115198), so the player's attrition effect (action
    // 32 Camp Rules x0.5, staff Provost Marshal-General) lowers attrition for both
    // armies. Medical care, training and readiness in the same method pass
    // groupunit.dlcw_isundercommander (DC:115107, 115243, 115276), and the
    // attrition tooltip is scoped per unit (DC:173552).
    //
    // This non-skipping Prefix sets cumulativeeffectsrunning[22] to 1 (vanilla's
    // own "not the player's unit" value, DC:44635-44641) while vanilla runs for a
    // regiment whose campaign group the player does not own; the Finalizer puts
    // the value back even if vanilla throws. Rollback: W&L / Enable W&L Attrition
    // Scope Fix.
    [HarmonyPatch(typeof(Regiment), "UpdateCampaignUnitStats")]
    internal static class WlAttritionScopePatch
    {
        internal struct Snapshot
        {
            public float[] Array;
            public float Saved;
            public bool Active;
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment groupunit, out Snapshot __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-attrition-scope.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-attrition-scope.prefix");
                __state = default;
                try
                {
                    if (!DLC_WL.dlc_scenarioactive || (UnityEngine.Object)groupunit == null) return;
                    if (!WlEconomyFixes.NeutralizeAttritionBonus(true, groupunit.dlcw_isundercommander)) return;
                    if (!Enabled()) return;

                    float[] running = WlEffectArrays.Running();
                    if (!WlEffectArrays.TryGet(running, WlEconomyFixes.EffectAttrition, out float value) || value == 1f) return;

                    running[WlEconomyFixes.EffectAttrition] = 1f;
                    __state = new Snapshot { Array = running, Saved = value, Active = true };
                    OnceLog.Info("wl-attrition-scope:first",
                        "[Patch:WlAttritionScope] player attrition effect x" + value.ToString("0.###") + " withheld from a group the player does not own");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-attrition-scope:prefix", "[Patch:WlAttritionScope] prefix failed: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(Snapshot __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-attrition-scope.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-attrition-scope.finalizer");
                if (!__state.Active) return;
                try
                {
                    float[] running = WlEffectArrays.Running();
                    bool same = ReferenceEquals(running, __state.Array);
                    if (WlEconomyFixes.ShouldRestore(same, same ? running[WlEconomyFixes.EffectAttrition] : float.NaN, 1f))
                        running[WlEconomyFixes.EffectAttrition] = __state.Saved;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-attrition-scope:finalizer", "[Patch:WlAttritionScope] restore failed: " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableWlAttritionScopeFix != null && Plugin.EnableWlAttritionScopeFix.Value;
            }
            catch { return false; }
        }
    }
}
