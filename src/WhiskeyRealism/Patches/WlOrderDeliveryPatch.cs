using System;
using HarmonyLib;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // W&L order flow, delivery (plan 2026-10-15-wl-order-flow.md D1, D8, D8a).
    //
    // Vanilla AIBattle.UpdateDLCPlayerOrders (private, decompile 6747-6763) runs on tactical micro
    // task 28, once per 30 frames per AIBattle: it checks one assigned group's orders for removal
    // (13/14/15, CheckRemovalOfOrders) and refreshes reserve orders. On the player's side that is the
    // cadence at which this Postfix (Priority.Low, after #62's Postfix has queued its order) delivers
    // the couriers that have arrived: each is given to vanilla's own CheckCurrentOrderUpdate, with the
    // order slot emptied first when the re-admit policy admits an order vanilla would drop, and put
    // back if vanilla refuses it. Runs on the player-side AIBattle only (isplayeraiorfeud == 1); the
    // enemy and feud instances never deliver. Never throws.
    [HarmonyPatch(typeof(AIBattle), "UpdateDLCPlayerOrders")]
    internal static class WlOrderDeliveryPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.wl-order-delivery", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.wl-order-delivery");
                try
                {
                    WlOrderFlowRuntime.OnDeliveryPass(__instance);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("wl-order:delivery-failed", "[W&LOrder] delivery pass failed: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }
    }
}
