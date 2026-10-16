using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // W&L order flow, capture (plan 2026-10-15-wl-order-flow.md D1-D3).
    //
    // Vanilla AIBattle.CheckCurrentOrderUpdate (static, decompile 8233-8667) is the one place every
    // order bound for the W&L player's panel passes: the AI's objective, support, assault and reserve
    // callers, CheckRemovalOfOrders' 13/14/15, the W&L dispatchers. It gates the call (8251-8369),
    // dedupes it against the active order (8640-8646), writes DLC_WL.givenorder, bumps the order
    // session and shows the panel (8648-8664) at once.
    //
    // This skipping Prefix (user-approved: "all orders by courier", the order-lock fix) takes each
    // new order that would reach the panel into the courier queue instead, or drops it when the
    // re-admit policy says it never should arrive; WlOrderDeliveryPatch calls vanilla for it once the
    // courier has arrived. Vanilla's 13 and 14 (the player's own contact state) and a type-11 refresh
    // pass straight through; a call made by the delivery transaction itself is never captured. Any
    // exception passes the call to vanilla. Priority.Last: the observer Prefix (TacticalObserverPatch)
    // sees the call first; its Postfix skips a call this patch took.
    [HarmonyPatch(typeof(AIBattle), "CheckCurrentOrderUpdate")]
    internal static class WlOrderCapturePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        internal static bool Prefix(Regiment unit, int type, Vector3 position, string destinationname, float rotation, float width, float depth, bool calledfromcampaign)
        {
            using (TelemetryPerf.Scope("tactical.patch.wl-order-capture", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.wl-order-capture");
                return WlOrderFlowRuntime.Capture(unit, type, position, destinationname, rotation, width, depth, calledfromcampaign);
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            using (TelemetryPerf.Scope("tactical.patch.wl-order-capture.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.wl-order-capture.finalizer");
                WlOrderFlowRuntime.ClearCapturedFlag();
            }
        }
    }
}
