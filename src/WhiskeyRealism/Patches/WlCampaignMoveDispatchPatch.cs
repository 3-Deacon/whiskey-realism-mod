using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #69 (chain-of-command spec §D trigger 1, §E campaign observation).
    //
    // Vanilla AICampaign.MoveUnitTo (static, decompile 14479-14540) moves a campaign
    // group (direct SetWaypoint 14514, or a queued DefensiveMovingOrder 14523 that
    // re-enters MoveUnitTo at 10048) and returns true when it did. Vanilla's campaign
    // orders reach the W&L player only when his command is its own map group (8366),
    // so a brigade/division player is carried along by his corps without a word. The
    // MoveUnitTo Postfix records a move of the player's campaign group (no write).
    // MoveUnitTo reassigns `position` to the stack position (14498) before anything
    // else, so a non-blocking Prefix hands the caller's target to the Postfix as
    // __state (the target vanilla's own order uses, e.g. 14451/14455);
    // `movearoundenemyunits` and `unit` are never reassigned.
    //
    // Vanilla lists the unit in its offensive / defensive operation only after
    // MoveUnitTo returns (14383-14386, 13771-13775); every decision-time caller runs
    // inside the private AICampaign.UpdateUnitAI (11303). Its Postfix therefore
    // classifies and emits the recorded moves (WlCampaignDispatchRuntime) and
    // observes the player's given order for the compliance ledger
    // (PlayerComplianceRuntime). Two callers cannot be told apart by list membership:
    // CheckFinalPositions (17898, a terrain correction, no order) and
    // RollUpEnemyObjectivesInZone (13883, order 6 semantics); a Prefix marks each one
    // as the move source and a Finalizer clears it. No patch here changes vanilla's
    // movement or skips vanilla.
    internal static class WlCampaignMoveDispatchPatch
    {
        [HarmonyPatch(typeof(AICampaign), nameof(AICampaign.MoveUnitTo))]
        internal static class MoveUnitTo
        {
            [HarmonyPrefix]
            internal static void Prefix(Vector3 position, out Vector3 __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-campaign-dispatch.move.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-campaign-dispatch.move.prefix");
                    __state = position;
                }
            }

            [HarmonyPostfix]
            internal static void Postfix(Regiment unit, bool movearoundenemyunits, bool __result, Vector3 __state)
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-campaign-dispatch.move", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-campaign-dispatch.move");
                    try
                    {
                        WlCampaignDispatchRuntime.OnMoveUnitTo(unit, __state, movearoundenemyunits, __result);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("wl-campaign-dispatch:move-postfix", "[Patch:WlCampaignDispatch] MoveUnitTo postfix failed: " + ex.Message);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(AICampaign), nameof(AICampaign.CheckFinalPositions))]
        internal static class FinalPositions
        {
            [HarmonyPrefix]
            internal static void Prefix()
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-campaign-dispatch.final-positions.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-campaign-dispatch.final-positions.prefix");
                    WlCampaignDispatchRuntime.EnterMoveSource(WlMoveSource.FinalPositions);
                }
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-campaign-dispatch.final-positions.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-campaign-dispatch.final-positions.finalizer");
                    WlCampaignDispatchRuntime.ExitMoveSource();
                    return __exception;
                }
            }
        }

        [HarmonyPatch(typeof(AICampaign), nameof(AICampaign.RollUpEnemyObjectivesInZone))]
        internal static class RollUp
        {
            [HarmonyPrefix]
            internal static void Prefix()
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-campaign-dispatch.roll-up.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-campaign-dispatch.roll-up.prefix");
                    WlCampaignDispatchRuntime.EnterMoveSource(WlMoveSource.RollUp);
                }
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception)
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-campaign-dispatch.roll-up.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-campaign-dispatch.roll-up.finalizer");
                    WlCampaignDispatchRuntime.ExitMoveSource();
                    return __exception;
                }
            }
        }

        [HarmonyPatch(typeof(AICampaign), "UpdateUnitAI")]
        internal static class UpdateUnitAI
        {
            [HarmonyPostfix]
            internal static void Postfix()
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-campaign-dispatch.flush", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-campaign-dispatch.flush");
                    try
                    {
                        WlCampaignDispatchRuntime.Flush();
                        WlCampaignCourierRuntime.DeliverDue();
                        WlCampaignCourierRuntime.AcknowledgeReached();
                        PlayerComplianceRuntime.ObserveCampaign();
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("wl-campaign-dispatch:flush-postfix", "[Patch:WlCampaignDispatch] UpdateUnitAI postfix failed: " + ex.Message);
                    }
                }
            }
        }
    }
}
