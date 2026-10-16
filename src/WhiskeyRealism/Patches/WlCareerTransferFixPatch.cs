using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-013 (stage G, spec C1, decided yes 2026-10-15). In a W&L career vanilla's AI transfer of
    // units never happens. AICampaign.CheckTransferOfUnits (17232) queues the unit in
    // unitstobetransfered and calls BattleUnits.MoveUnit(unit, below) with the default
    // skipdlccheck = false (17328); MoveUnit returns at its first guard unless BOTH units are
    // player-owned (`(!component2.dlcw_isundercommander | !component.dlcw_isundercommander) &&
    // DLC_WL.dlc_scenarioactive && !skipdlccheck`, 93099), so no AI faction's transfer moves anything.
    //
    // The fix is argument-only and no vanilla call is skipped. This scope patch marks the span of one
    // CheckTransferOfUnits call; the MoveUnit patch below then sets skipdlccheck = true for the calls
    // made inside that span, and only when neither unit is player-owned (WlTransferFixDecision), so a
    // unit the player owns keeps vanilla's refusal and is never re-parented by Whiskey. Everything else
    // in MoveUnit (type gates, chapter-0 garrison guard, SetTransfer) is vanilla's own.
    // Rollback: W&L / Enable W&L Career Transfer Fix.
    [HarmonyPatch(typeof(AICampaign), "CheckTransferOfUnits")]
    internal static class WlCareerTransferScopePatch
    {
        [ThreadStatic] private static int _depth;

        // True while vanilla is inside CheckTransferOfUnits with the fix on.
        internal static bool Inside => _depth > 0;

        [HarmonyPrefix]
        internal static void Prefix(out bool __state)
        {
            __state = false;
            // Outside a W&L career there is nothing to scope: one static read (perf L3).
            if (!DLC_WL.dlc_scenarioactive) return;
            using (TelemetryPerf.Scope("campaign.patch.wl-career-transfer.scope", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-career-transfer.scope");
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (Plugin.EnableWlCareerTransferFix == null || !Plugin.EnableWlCareerTransferFix.Value) return;
                _depth++;
                __state = true;
            }
        }

        // Also runs when vanilla throws; only a call that opened the scope closes it.
        [HarmonyFinalizer]
        internal static void Finalizer(bool __state)
        {
            if (!__state) return;
            using (TelemetryPerf.Scope("campaign.patch.wl-career-transfer.scope.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-career-transfer.scope.finalizer");
                if (_depth > 0) _depth--;
            }
        }
    }

    [HarmonyPatch(typeof(BattleUnits), "MoveUnit")]
    internal static class WlCareerTransferMoveUnitPatch
    {
        // The first-fire row is formatted once per campaign, not per transfer.
        private static bool _firstLogged;

        static WlCareerTransferMoveUnitPatch()
        {
            CampaignScope.Register("wl-career-transfer-first-log", () => _firstLogged = false);
        }

        [HarmonyPrefix]
        internal static void Prefix(GameObject unittomove, GameObject unittomovebelow, ref bool skipdlccheck)
        {
            // MoveUnit is not hot, but nothing here is needed outside the transfer scope.
            if (!WlCareerTransferScopePatch.Inside) return;
            using (TelemetryPerf.Scope("campaign.patch.wl-career-transfer.move-unit", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.wl-career-transfer.move-unit");
                try
                {
                    if ((UnityEngine.Object)unittomove == null || (UnityEngine.Object)unittomovebelow == null) return;
                    var source = unittomove.GetComponent<Regiment>();
                    var destination = unittomovebelow.GetComponent<Regiment>();
                    if ((UnityEngine.Object)source == null || (UnityEngine.Object)destination == null) return;
                    if (!WlTransferFixDecision.Allow(true, DLC_WL.dlc_scenarioactive, source.dlcw_isundercommander, destination.dlcw_isundercommander)) return;

                    skipdlccheck = true;
                    if (!_firstLogged)
                    {
                        _firstLogged = true;
                        OnceLog.Info("campaign-planner:wl-transfer-fix-first",
                            "[WlTransferFix] career transfer allowed unit=" + source.name + " to=" + destination.name);
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:wl-transfer-fix", "[Patch:WlCareerTransfer] move-unit prefix failed: " + ex.Message);
                }
            }
        }
    }
}
