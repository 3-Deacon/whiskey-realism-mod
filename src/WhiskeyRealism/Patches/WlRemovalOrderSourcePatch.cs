using System;
using HarmonyLib;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-019 (plan 2026-10-15-wl-order-flow.md D13, N2): tells the capture Prefix which AIBattle
    // instance is inside vanilla's CheckRemovalOfOrders.
    //
    // Vanilla AIBattle.CheckRemovalOfOrders (private, decompile 6777-6811) runs on the enemy and feud
    // instances as well as the player's side; with the calling instance's macroai == 3 it calls
    // CheckCurrentOrderUpdate(group, 15, ...), which for a top-level W&L player reads "our army
    // retreats" when the enemy's army retreats. A non-skipping Prefix stamps the instance's
    // isplayeraiorfeud (1 the player's side, 0 the enemy, 2 the feud) and a Finalizer (so it runs even
    // when vanilla throws) clears it; WlOrderFlowRuntime drops a type-15 call made under a stamp other
    // than 1. Changes nothing in vanilla's own execution.
    [HarmonyPatch(typeof(AIBattle), "CheckRemovalOfOrders")]
    internal static class WlRemovalOrderSourcePatch
    {
        [HarmonyPrefix]
        internal static void Prefix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.wl-removal-source.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.wl-removal-source.prefix");
                try
                {
                    EnemyRetreatGuard.Set(__instance != null ? __instance.isplayeraiorfeud : EnemyRetreatGuard.NoStamp);
                }
                catch (Exception ex)
                {
                    EnemyRetreatGuard.Clear();
                    OnceLog.Warning("wl-order:removal-source", "[W&LOrder] stamping the removal-check caller failed: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception)
        {
            using (TelemetryPerf.Scope("tactical.patch.wl-removal-source.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.wl-removal-source.finalizer");
                EnemyRetreatGuard.Clear();
                return __exception;
            }
        }
    }
}
