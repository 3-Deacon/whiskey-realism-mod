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
    // 3/4 multiplies cumulativeeffectsrunning (actions DC:44753-44773). A sweep
    // of every quality type of every action, item, companion, injury, staff and
    // career-event record against every EffectMultiplier reader finds paid
    // actions whose bonus lands in the array vanilla never reads for that
    // effect (actions.dat; WlEconomyFixes.FoldsOnceEffect / FoldsRunningEffect):
    // type 1/2 on running-only effects 9 supply efficiency (28 Logistics Corps,
    // 50 Not Yet Ready), 12/13 order delays (29 Signal Corps), 17 fighting
    // spirit (31 High Spirits), 19 contract renewal (36/37 Recruitment
    // Campaign, 46 Camp Pleasures, 64 Uncle Sam, 65 Irish Legion), 23
    // engineering (27 Engineer Corps), 27 staff efficiency (30 Staff Standards,
    // 66 Examine Details); and type 3 on the once-only effect 4 incident chance
    // (20 Gamble, DC:46139). No companion, staff, item, injury or career event
    // feeds these slots.
    //
    // This Postfix folds once[id] into running[id] as x(1 + once), and the
    // running deviation of effect 4 into once[4] (WlEconomyFixes.FoldEffects).
    // The method rebuilds both arrays from scratch on every call (DC:44671-44675), so the
    // fold never compounds. Effect 9 is folded only while Enable W&L Supply
    // Efficiency Sign Fix is on: without it vanilla applies effect 9 the wrong
    // way round (BUG-WL-008). Left vanilla, with the evidence in
    // docs/bug-fixes/vanilla-wl-career.md: 22 (actions 40/41/49), 0 (item,
    // injury and career-event qualities whose texts promise other things), 3
    // (companion type-0 data).
    // Rollback: W&L / Enable W&L Action Once-Effects Fix.
    [HarmonyPatch(typeof(DLC_WL), "UpdateCumulativeEffects")]
    internal static class WlActionOnceEffectsPatch
    {
        // The first-fold message is formatted once per campaign, not per call.
        private static bool _foldLogged;

        static WlActionOnceEffectsPatch()
        {
            CampaignScope.Register("wl-action-once-effects-log", () => _foldLogged = false);
        }

        [HarmonyPostfix]
        internal static void Postfix()
        {
            using (TelemetryPerf.Scope("campaign.patch.wl-action-once-effects", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-action-once-effects");
                try
                {
                    if (!DLC_WL.dlc_scenarioactive || !Enabled()) return;
                    int folded = WlEconomyFixes.FoldEffects(WlEffectArrays.Running(), WlEffectArrays.Once(), SignFixOn());
                    if (folded > 0 && !_foldLogged)
                    {
                        _foldLogged = true;
                        OnceLog.Info("wl-action-once-effects:first",
                            "[Patch:WlActionEffects] action bonuses folded into the effect vanilla reads: slots=" + folded);
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
