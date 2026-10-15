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
    //
    // Vanilla lists the unit in its offensive / defensive operation only after
    // MoveUnitTo returns (14383-14386, 13771-13775); every decision-time caller runs
    // inside the private AICampaign.UpdateUnitAI (11303). Its Postfix therefore
    // classifies and emits the recorded moves (WlCampaignDispatchRuntime) and
    // observes the player's given order for the compliance ledger
    // (PlayerComplianceRuntime). Neither Postfix changes vanilla's movement.
    internal static class WlCampaignMoveDispatchPatch
    {
        [HarmonyPatch(typeof(AICampaign), nameof(AICampaign.MoveUnitTo))]
        internal static class MoveUnitTo
        {
            [HarmonyPostfix]
            internal static void Postfix(Regiment unit, Vector3 position, bool __result)
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-campaign-dispatch.move", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-campaign-dispatch.move");
                    try
                    {
                        WlCampaignDispatchRuntime.OnMoveUnitTo(unit, position, __result);
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("wl-campaign-dispatch:move-postfix", "[Patch:WlCampaignDispatch] MoveUnitTo postfix failed: " + ex.Message);
                    }
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
