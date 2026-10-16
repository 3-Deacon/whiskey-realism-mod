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
    // Emit and EmitP62 no longer call AIBattle.CheckCurrentOrderUpdate itself: the order goes onto the W&L order
    // flow's courier queue (WlOrderFlowRuntime.EnqueueWhiskey) and is delivered to vanilla after the
    // courier delay from the issuing headquarters, so Whiskey's orders and vanilla's arrive the same
    // way and are subject to the same re-admit policy. The queue's cadence (one order per issuer,
    // type and origin per 0.25 h) and en-route test (same type within 110 m already riding) replace the
    // old 110 m last-dispatch record. Vanilla's gates and dedupe (decompile 8251-8369, 8640-8646) still
    // run at delivery. With both order-flow switches off the call goes to vanilla at once, as before.
    // #62's accept/attempt bookkeeping follows the delivery (OnP62Outcome), not the enqueue.
    internal static class TacticalPlayerDispatchRuntime
    {
        internal static readonly PlayerOrderDedupeState Dedupe = new PlayerOrderDedupeState();
        // #62's view of the game (the active order, its provenance, the candidate it composes); the
        // delivery callback below uses the same adapter to read what vanilla stored.
        internal static readonly PlayerOrderRuntimeAdapter Adapter = new PlayerOrderRuntimeAdapter(Dedupe);

        static TacticalPlayerDispatchRuntime()
        {
            BattleScope.Register("tactical-player-dispatch", () => Dedupe.ClearForBattleBoundary(string.Empty));
        }

        // #62's order for the player: `unit` is the player's parent unit (never his own command, which
        // vanilla's gate at 8337 refuses for a subordinate). True when it went onto a courier.
        internal static bool EmitP62(Regiment unit, in PlayerOrderCandidate candidate, string destinationName)
        {
            using (TelemetryPerf.Scope("tactical.dispatch.emit-p62", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if ((UnityEngine.Object)unit == null || !candidate.HasCandidate || !TacticalWriteGuard.ModeAllowsWrites()) return false;
                    return WlOrderFlowRuntime.EnqueueP62(unit, in candidate, destinationName);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-dispatch:emit-p62", "[TacticalPlayerDispatch] #62 enqueue failed: " + ex.GetType().Name + " " + ex.Message);
                    return false;
                }
            }
        }

        // The delivery callback for a #62 entry (WlOrderFlow): vanilla accepted it, refused or dropped it,
        // or it is gone for a structural reason.
        internal static void OnP62Outcome(AIBattle battle, in PlayerOrderCandidate candidate, P62Outcome outcome, double nowHours)
        {
            try
            {
                if (!candidate.HasCandidate) return;
                switch (outcome)
                {
                    case P62Outcome.Accepted:
                        PlayerOrderActiveSnapshot after = Adapter.Snapshot(battle, forceVanillaProvenance: false).ActiveOrder;
                        Dedupe.RecordAccepted(Adapter.WithSession(candidate, after.GivenOrderSession), Adapter.WithAcceptedOrder(candidate, after), nowHours);
                        break;
                    case P62Outcome.Attempt:
                        Dedupe.RecordAttempt(candidate, nowHours);
                        break;
                    case P62Outcome.ClearPending:
                        Dedupe.ClearPending(candidate.UnitKey);
                        break;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-dispatch:p62-outcome", "[TacticalPlayerDispatch] #62 bookkeeping failed: " + ex.GetType().Name + " " + ex.Message);
            }
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
