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
    // 3/4 multiplies cumulativeeffectsrunning (actions DC:44753-44773). The
    // actions 28 "Logistics Corps" ("Increases supply efficiency and medical care
    // in your unit") and 50 "Not Yet Ready" ("improve your supply situation")
    // carry (9, type 1), and 27 "Engineer Corps" ("Increases your engineering
    // points") carries (23, type 1) (actions.dat). Effects 9 and 23 are only read
    // as running multipliers (DC:114348, 173699; 87433), never as once values,
    // so the paid-for bonus never applies. No companion, staff, item or injury
    // feeds once[9] or once[23].
    //
    // This Postfix folds once[id] into running[id] as x(1 + once) for ids 9 and
    // 23. The method rebuilds both arrays from scratch on every call
    // (DC:44671-44675), so the fold never compounds. Effect 9 is folded only while
    // Enable W&L Supply Efficiency Sign Fix is on: without it vanilla applies
    // effect 9 the wrong way round (BUG-WL-008). The (22, type 1) qualities of
    // actions 40/41/49 are left vanilla: their texts never mention attrition and
    // "+" would raise it (Camp Rules lowers it with type 4). Rollback: W&L /
    // Enable W&L Action Once-Effects Fix.
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
                        if (id == WlEconomyFixes.EffectSupplyEfficiency && !SignFixOn()) continue;
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
