using System;
using UnityEngine;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // The one emitter of W&L battle dispatches (battle-core D9, chain-of-command
    // spec §C). #62 PlayerSubordinateOrderPatch (the player's own order) and the
    // battle-core executor (the player's share of an order given to an ancestor)
    // both call Emit, so they share one dedupe and never double-pop the career panel.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   AIBattle.CheckCurrentOrderUpdate 8233-8667, battle path
    //   (calledfromcampaign: false): returns for a commanding-officer player (8329);
    //   relays type 11 via unitlinkedto (8333-8336); passes only orders to the
    //   player's parent chain with the same groupaiobject (8337); remaps the issuer
    //   to the parent's commander (8341-8360); widens the zone to the parent group
    //   and dlcwl_minimumobjectivezone (8398-8428); dedupes against the active order
    //   (8643: same type for type > 0, within 110 m for type 0; 12 never replaces an
    //   attack; 15 terminal; 13 sticky); bumps givenorderssession (8651) except for a
    //   type-11 refresh (flag4); shows the panel (8653-8664).
    // Vanilla already dedupes, more strictly: 8643 rejects any repeat of the same
    // type > 0 whatever the distance. This emitter's 110 m record skips the call
    // only while the player still holds the order the last dispatch produced
    // (PlayerOrderDedupeState.IsDuplicateDispatch). A call vanilla rejects costs its
    // tooltip lookups and a terrain sample, not a Debug.Log (only on accept, 8666).
    // Callers: #62 (bounded by its signature check and 120-tick throttle) and the
    // order executor's ancestor dispatch (Move winners only, behind the move dedupe,
    // the 180 battle-second recent-order cooldown and at most one retry).
    internal static class TacticalPlayerDispatchRuntime
    {
        internal static readonly PlayerOrderDedupeState Dedupe = new PlayerOrderDedupeState();
        internal const float DispatchDedupeMeters = 110f;

        // The zone sizes #62 always passed; vanilla widens them to the parent group.
        private const float ZoneWidth = 100f;
        private const float ZoneDepth = 50f;

        static TacticalPlayerDispatchRuntime()
        {
            BattleScope.Register("tactical-player-dispatch", () => Dedupe.ClearForBattleBoundary(string.Empty));
        }

        // Returns true when vanilla issued a new given order (session advanced).
        // A type-11 refresh of an active reserve order updates in place and
        // returns false, as does a deduped or refused dispatch.
        internal static bool Emit(Regiment ancestorOrPlayerCommand, int vanillaType, float x, float z, float rotation, string destinationName)
        {
            using (TelemetryPerf.Scope("tactical.dispatch.emit", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if ((UnityEngine.Object)ancestorOrPlayerCommand == null || vanillaType < 0
                        || !TacticalWriteGuard.ModeAllowsWrites()) return false;

                    int before = DLC_WL.GivenOrders.givenorderssession;
                    if (Dedupe.IsDuplicateDispatch(vanillaType, x, z, CurrentGivenOrder(before), DispatchDedupeMeters)) return false;

                    AIBattle.CheckCurrentOrderUpdate(
                        ancestorOrPlayerCommand,
                        vanillaType,
                        new Vector3(x, 0f, z),
                        destinationName ?? string.Empty,
                        rotation,
                        ZoneWidth,
                        ZoneDepth);

                    int after = DLC_WL.GivenOrders.givenorderssession;
                    Dedupe.RecordDispatch(vanillaType, x, z, after);
                    return after != before;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-dispatch:emit", "[TacticalPlayerDispatch] CheckCurrentOrderUpdate failed: " + ex.GetType().Name + " " + ex.Message);
                    return false;
                }
            }
        }

        private static GivenOrderView CurrentGivenOrder(int session)
        {
            DLC_WL.GivenOrders order = DLC_WL.givenorder;
            if (order == null) return new GivenOrderView(false, -1, 0f, 0f, session);
            return new GivenOrderView(true, order.type, order.position.x, order.position.z, session);
        }
    }
}
