using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-009. Vanilla DLC_WL.UpdateCumulativeEffects (DC:44662) resets both
    // effect arrays and rebuilds them from companions, staff, actions, items,
    // injuries and events; quality type 1/2 adds to cumulativeeffectsonce, type
    // 3/4 multiplies cumulativeeffectsrunning (actions DC:44753-44773). Several
    // actions carry type-1 qualities on effects that are only ever read as
    // running multipliers, so the bonus their text promises never applies
    // (actions.dat; readers in WlEconomyFixes.FoldsOnceEffect): 9 supply
    // efficiency (28 Logistics Corps, 50 Not Yet Ready), 17 fighting spirit (31
    // High Spirits), 19 contract renewal (36/37 Recruitment Campaign, 46 Camp
    // Pleasures, 64 Uncle Sam, 65 Irish Legion), 23 engineering (27 Engineer
    // Corps), 27 staff efficiency (30 Staff Standards, 66 Examine Details). No
    // companion, staff, item, injury or career event feeds once[] for these ids.
    //
    // This Postfix folds once[id] into running[id] as x(1 + once). The method
    // rebuilds both arrays from scratch on every call (DC:44671-44675), so the
    // fold never compounds. Effect 9 is folded only while Enable W&L Supply
    // Efficiency Sign Fix is on: without it vanilla applies effect 9 the wrong
    // way round (BUG-WL-008). Left vanilla, with the evidence in
    // docs/bug-fixes/vanilla-wl-career.md: 22 (actions 40/41/49), 0 (item
    // qualities whose texts promise other things), 3 (companion type-0 data).
    // Rollback: W&L / Enable W&L Action Once-Effects Fix.
    [HarmonyPatch(typeof(DLC_WL), "UpdateCumulativeEffects")]
    internal static class WlActionOnceEffectsPatch
    {
        [HarmonyPostfix]
        internal static void Postfix()
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-action-once-effects", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-action-once-effects");
                try
                {
                    if (!DLC_WL.dlc_scenarioactive || !Enabled()) return;
                    float[] running = WlEffectArrays.Running();
                    float[] once = WlEffectArrays.Once();
                    if (running == null || once == null) return;

                    int count = Math.Min(running.Length, once.Length);
                    for (int id = 0; id < count; id++)
                    {
                        if (!WlEconomyFixes.FoldsOnceEffect(id)) continue;
                        if (WlEconomyFixes.OnceFoldNeedsSignFix(id) && !SignFixOn()) continue;
                        float add = once[id];
                        if (add == 0f) continue;

                        running[id] = WlEconomyFixes.FoldOnceIntoRunning(running[id], add);
                        OnceLog.Info("wl-action-once-effects:first",
                            "[Patch:WlActionEffects] action once-bonus folded into the running effect: id=" + id + " once=" + add.ToString("0.###")
                            + " running=" + running[id].ToString("0.###"));
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-action-once-effects:postfix", "[Patch:WlActionEffects] postfix failed: " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableWlActionOnceEffectsFix != null && Plugin.EnableWlActionOnceEffectsFix.Value;
            }
            catch { return false; }
        }

        private static bool SignFixOn()
        {
            try { return Plugin.EnableWlSupplyEfficiencySignFix != null && Plugin.EnableWlSupplyEfficiencySignFix.Value; }
            catch { return false; }
        }
    }
}
