using System;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // The one emitter of W&L battle dispatches (battle-core D9, chain-of-command spec section C).
    // #62 PlayerSubordinateOrderPatch (the player's own order) and the battle-core executor (the
    // player's share of an order given to an ancestor) both call Emit.
    //
    // Emit no longer calls AIBattle.CheckCurrentOrderUpdate itself: the order goes onto the W&L order
    // flow's courier queue (WlOrderFlowRuntime.EnqueueWhiskey) and is delivered to vanilla after the
    // courier delay from the issuing headquarters, so Whiskey's orders and vanilla's arrive the same
    // way and are subject to the same re-admit policy. The queue's cadence (one order per issuer,
    // type and origin per 0.25 h) and en-route test (same type within 110 m already riding) replace the
    // old 110 m last-dispatch record. Vanilla's gates and dedupe (decompile 8251-8369, 8640-8646) still
    // run at delivery. With both order-flow switches off the call goes to vanilla at once, as before.
    internal static class TacticalPlayerDispatchRuntime
    {
        internal static readonly PlayerOrderDedupeState Dedupe = new PlayerOrderDedupeState();

        static TacticalPlayerDispatchRuntime()
        {
            BattleScope.Register("tactical-player-dispatch", () => Dedupe.ClearForBattleBoundary(string.Empty));
        }

        // True when the order went onto a courier (or, with the flow off, vanilla showed a new order).
        // Deduped, refused and dropped orders return false.
        internal static bool Emit(Regiment ancestorOrPlayerCommand, int vanillaType, float x, float z, float rotation, string destinationName, OrderOrigin origin)
        {
            using (TelemetryPerf.Scope("tactical.dispatch.emit", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if ((UnityEngine.Object)ancestorOrPlayerCommand == null || vanillaType < 0
                        || !TacticalWriteGuard.ModeAllowsWrites()) return false;
                    return WlOrderFlowRuntime.EnqueueWhiskey(ancestorOrPlayerCommand, vanillaType, x, z, rotation, destinationName, origin);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-dispatch:emit", "[TacticalPlayerDispatch] order flow enqueue failed: " + ex.GetType().Name + " " + ex.Message);
                    return false;
                }
            }
        }
    }
}
