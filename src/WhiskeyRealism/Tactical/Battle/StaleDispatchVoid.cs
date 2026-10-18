using WhiskeyRealism.Tactical.PlayerOrders;

namespace WhiskeyRealism.Tactical.Battle
{
    // Which queued orders to the W&L player a written group Move makes stale (pure; the runtime owns the instance and calls it from
    // OrderExecutorRuntime.Execute before the move's own order is queued).
    //
    // A dispatch or a vanilla order armed with its called unit (CourierEntry.Pursuit) is keyed on the group it names. Vanilla restamps a
    // group's last waypoint only for the group a Move is issued to (SetGroupFormation, 92067) and for regiments (RegimentSetPath, 131250,
    // unittyp <= 13); a sub-group the parent re-paths keeps its old last waypoint. So a brigade order to A still reads "path ends at A"
    // after the division sends the brigade to B, and delivers (smoke review: a Maintenance division move voided nothing and left A on the panel).
    //
    // Rule: on every written group Move, Maintenance moves included, the queued executor orders of every NON-DETACHED descendant group
    // (the snapshot's parent map, never transform.parent) are voided when the descendant's new target differs from the queued one by more
    // than 110 m, and so are the vanilla orders armed with such a descendant. The new target is the one the executor gives a child
    // (OrderExecutor.OffsetTarget: it keeps its offset from the ordered group). A detached subtree, a player-owned unit and a routed unit
    // are not re-pathed by a group order (SetGroupFormation skips them, 119865, 92076-92108) and are untouched. The ordered group's own
    // queued executor orders are voided too, except for a Maintenance move (a recovery's target can be a fresh assembly point and the move
    // is no new order). A Maintenance move still sends no dispatch of its own (OrderExecutor.PlanCore step 9).
    internal sealed class StaleDispatchVoid
    {
        private int[] _queue = new int[64];
        private int[] _index = new int[64];
        private float[] _x = new float[64];
        private float[] _z = new float[64];

        // `issued` are the units the Move reached (the ordered group, or the children of a fan-out) with their targets.
        // Returns how many queued orders were voided (each reported through the queue's sink).
        public int Apply(WlCourierQueue queue, UnitRecord[] own, int count, int groupIndex, bool maintenance, float groupX, float groupZ,
            IssuedTarget[] issued, int issuedCount, float sizeX, float sizeZ, string reason)
        {
            if (queue == null || own == null || queue.Count == 0 || groupIndex < 0 || groupIndex >= count || groupIndex >= own.Length) return 0;
            int groupId = own[groupIndex].InstanceId;
            int voided = 0;
            if (!maintenance) voided += queue.DiscardSupersededIntents(groupId, groupX, groupZ, reason, includeArmedVanilla: false);
            for (int k = 0; k < issuedCount && queue.Count > 0; k++)
            {
                IssuedTarget moved = issued[k];
                // A fan-out child is a descendant that was moved: its orders go whatever kind of Move this is.
                if (moved.InstanceId != groupId)
                    voided += queue.DiscardSupersededIntents(moved.InstanceId, moved.X, moved.Z, reason, includeArmedVanilla: true);
                int root = IndexOf(own, count, moved.InstanceId);
                if (root < 0) continue;
                int found = Collect(own, count, root, moved.X, moved.Z, sizeX, sizeZ);
                for (int j = 0; j < found; j++)
                    voided += queue.DiscardSupersededIntents(own[_index[j]].InstanceId, _x[j], _z[j], reason, includeArmedVanilla: true);
            }
            return voided;
        }

        // The non-detached descendant groups of `root` with the target each gets when `root` goes to (rootX, rootZ). Results in
        // _index/_x/_z; returns the count. Breadth first, bounded by the buffers (grown to the record count).
        internal int Collect(UnitRecord[] own, int count, int root, float rootX, float rootZ, float sizeX, float sizeZ)
        {
            if (count > own.Length) count = own.Length;
            if (_queue.Length < count)
            {
                _queue = new int[count];
                _index = new int[count];
                _x = new float[count];
                _z = new float[count];
            }
            int found = 0, head = 0, tail = 0;
            _queue[tail++] = root;
            while (head < tail)
            {
                int parent = _queue[head++];
                for (int child = 0; child < count; child++)
                {
                    if (child == parent || own[child].ParentIndex != parent) continue;
                    UnitFlags flags = own[child].Flags;
                    if ((flags & (UnitFlags.PermanentlyDetached | UnitFlags.PlayerOwned | UnitFlags.PlayerCommand | UnitFlags.Routed)) != 0) continue;
                    if ((flags & UnitFlags.IsGroup) == 0) continue;
                    OrderExecutor.OffsetTarget(own[root], own[child], rootX, rootZ, sizeX, sizeZ, out float cx, out float cz);
                    _index[found] = child;
                    _x[found] = cx;
                    _z[found] = cz;
                    found++;
                    if (tail < _queue.Length) _queue[tail++] = child;
                }
            }
            return found;
        }

        private static int IndexOf(UnitRecord[] own, int count, int instanceId)
        {
            for (int i = 0; i < count && i < own.Length; i++)
                if (own[i].InstanceId == instanceId) return i;
            return -1;
        }
    }
}
