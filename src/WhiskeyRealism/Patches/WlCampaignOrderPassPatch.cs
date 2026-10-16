using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #69 (chain-of-command spec §D, §E; amended 2026-10-16: no order for a carried player).
    //
    // Vanilla AICampaign.UpdateUnitAI (private, decompile 11303) is the campaign AI's per-pass
    // entry; every decision-time caller of the campaign movers and every vanilla campaign order
    // runs inside it. The Postfix is the campaign half of the W&L order flow's heartbeat:
    //   - WlCampaignCourierRuntime.ClearStaleCarriedOrder empties the campaign order a carried player
    //     still holds from before the fix (none is ever given to him now);
    //   - WlCampaignCourierRuntime.DeliverDue gives every due courier order (a vanilla campaign
    //     call the capture Prefix queued, or Whiskey's bridge order for a map-group player) to
    //     the player's panel;
    //   - WlCampaignCourierRuntime.AcknowledgeReached tells the player a reached order is done;
    //   - PlayerComplianceRuntime.ObserveCampaign samples the order the player was given.
    // A player whose command is not its own map group (a brigade or division carried by a corps)
    // is sent no campaign order by vanilla (AIBattle.CheckCurrentOrderUpdate gate 8366) and none
    // by Whiskey: the pass never reacts to a move of his carrying group. No patch here changes
    // vanilla's movement or skips vanilla.
    internal static class WlCampaignOrderPassPatch
    {
        [HarmonyPatch(typeof(AICampaign), "UpdateUnitAI")]
        internal static class UpdateUnitAI
        {
            [HarmonyPostfix]
            internal static void Postfix()
            {
                using (TelemetryPerf.Scope("campaign.patch.wl-campaign-order-pass", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.wl-campaign-order-pass");
                    try
                    {
                        WlCampaignCourierRuntime.ClearStaleCarriedOrder();
                        WlCampaignCourierRuntime.DeliverDue();
                        WlCampaignCourierRuntime.AcknowledgeReached();
                        PlayerComplianceRuntime.ObserveCampaign();
                    }
                    catch (Exception ex)
                    {
                        OnceLog.Warning("wl-campaign-order-pass:postfix", "[Patch:WlCampaignOrderPass] UpdateUnitAI postfix failed: " + ex.Message);
                    }
                }
            }
        }
    }
}
