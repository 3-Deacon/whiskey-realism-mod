using UnityEngine;

namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // Reads the move entries vanilla keeps in a superior's order queue, for the move gate of the W&L order
    // flow (WlOrderFlow.ReleaseOnMove). Runtime only; excluded from the test csproj. Reads only: nothing
    // here queues, edits or deletes an entry.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   BattleUnits.SetWaypoint 91461-91504: the order is queued on the ordered unit's scene parent
    //     (`gameObject = reg.transform.parent.gameObject`; a parent named "Units" has no Regiment, and the
    //     unit queues on itself, 91494-91504). transform.parent is therefore the question here, the queue
    //     owner vanilla itself keys on, and not the command tree (the snapshot's parent map answers that).
    //   AddToOrderQueue 124917: an entry is {advisedunit, ordertype, processingtime}; ordertype 0 is a path
    //     append and 1 a new path; a unit with a move already in flight gets no second entry (SetWaypoint
    //     91485, ordertypeactive).
    //   OrderQueue constructor (109640): processingtime is never before the entry's creation, and lastupdate is
    //     the creation stamp (ProcessOrders rewrites it only to a later time, 125331). A replacement entry
    //     (124929-124931) skips the 0.02 h standard time (124966), so the stamp, not the processing time,
    //     identifies the entry an order rides.
    //   ProcessOrders 125173-125425: an entry is carried out and deleted when every courier line has run out; a
    //     lost line clears the untransmitted waypoints (125282) and the entry goes one or two passes later, so
    //     a lost move is told from a carried-out one by the unit's path once the entry is gone (WlMoveState).
    //   SetOrderStatus 125484-125508: carrying it out transmits all the paths the unit then holds.
    internal static class WlMoveQueue
    {
        // The unit's pending move entry with the latest processing time: its processing time (which entry is
        // carried out last), its creation stamp and ordertype (which entry the order rides,
        // WlMoveState.Carries).
        internal static bool TryPending(Regiment unit, out float processingTime, out float stamp, out int orderType)
        {
            processingTime = 0f;
            stamp = 0f;
            orderType = 0;
            Regiment.OrderQueue entry = Latest(unit);
            if (entry == null) return false;
            processingTime = entry.processingtime;
            stamp = entry.lastupdate;
            orderType = entry.ordertype;
            return true;
        }

        // Where the carrier unit's move stands: queued, queued with its courier gone, or no longer queued,
        // when the unit's path end, position and contact say whether it was carried out or lost (WlMoveState),
        // and `basis` says which of them carried it.
        // Only an entry that WlMoveState.Carries counts, so an older move of the same unit is another order's.
        internal static MoveState StateOf(Regiment unit, in WlMoveCarrier carrier, out MoveBasis basis)
        {
            basis = MoveBasis.None;
            var queue = OwnerQueue(unit);
            GameObject moved = unit.gameObject;
            bool queued = false;
            bool lost = false;
            for (int i = 0; i < queue.Count; i++)
            {
                Regiment.OrderQueue entry = queue[i];
                if (entry == null || (UnityEngine.Object)entry.advisedunit != (UnityEngine.Object)moved) continue;
                if (!WlMoveState.Carries(entry.ordertype, entry.lastupdate, in carrier)) continue;
                queued = true;
                lost |= CourierLost(entry, moved);
            }
            if (queued) return WlMoveState.Classify(true, lost, false);
            Vector3 at = unit.transform.position;
            float distance = FlatDistance(at.x, at.z, carrier.X, carrier.Z);
            // The held path's end: the unit's last set waypoint (SetGroupFormation stamps it on every group move,
            // 92067; RegimentSetPath on a regiment, 131254). Read only while a path is held.
            Vector3 last = unit.lastsetwaypointposition;
            float pathEnd = FlatDistance(last.x, last.z, carrier.X, carrier.Z);
            basis = WlMoveState.BasisAfterGone(unit.regimentpaths > 0, pathEnd, distance, unit.combatstatussubordinates > 0f, carrier.SlackMeters);
            return WlMoveState.Classify(false, false, basis != MoveBasis.None);
        }

        private static float FlatDistance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static Regiment.OrderQueue Latest(Regiment unit)
        {
            var queue = OwnerQueue(unit);
            GameObject moved = unit.gameObject;
            Regiment.OrderQueue latest = null;
            for (int i = 0; i < queue.Count; i++)
            {
                Regiment.OrderQueue entry = queue[i];
                if (entry == null || entry.ordertype > 1 || (UnityEngine.Object)entry.advisedunit != (UnityEngine.Object)moved) continue;
                if (latest == null || entry.processingtime > latest.processingtime) latest = entry;
            }
            return latest;
        }

        // SetWaypoint 91461: the unit's scene parent owns its queue; under the "Units" root the unit does. The
        // queue is never null (`orderqueue` is initialised where it is declared, 111866), and both callers hold
        // a live unit (the executor's issued target; the host's resolved carrier), so there is no "no queue" case.
        private static System.Collections.Generic.List<Regiment.OrderQueue> OwnerQueue(Regiment unit)
        {
            Transform parent = unit.transform.parent;
            Regiment owner = parent != null ? parent.GetComponent<Regiment>() : null;
            if ((UnityEngine.Object)owner == null) owner = unit;
            return owner.orderqueue;
        }

        // The entry's own line to the moved unit is an active mounted courier line (type 1) whose courier is
        // gone: vanilla's lost-order test (125282-125286, the line must still be active). Secondary lines to
        // the unit's regiments are not asked.
        private static bool CourierLost(Regiment.OrderQueue entry, GameObject moved)
        {
            var lines = entry.courierline;
            if (lines == null) return false;
            for (int k = 0; k < lines.Count; k++)
            {
                Regiment.OrderQueue.CourierLine line = lines[k];
                if (line != null && line.type == 1 && line.lineactive && (UnityEngine.Object)line.targetunit == (UnityEngine.Object)moved
                    && (UnityEngine.Object)line.courierref == null) return true;
            }
            return false;
        }
    }
}
