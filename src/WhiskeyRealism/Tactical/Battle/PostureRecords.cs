using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // What the posture producer decided for one group this tick; the facing
    // pulse and fire control read it (they ran only on a group #61 left alone),
    // and the recon producer reads Submitted as "the posture moved this group".
    internal struct GroupPosture
    {
        public bool Has;
        public CommandNodeOperationalState State;
        public bool CloseEngaged;
        public bool FlankRisk;
        public bool Submitted;
        public bool FacingSubmitted;
        // The pipeline put the group on a BC10 road route or is waiting for its column this tick; its regiments march in column on purpose.
        public bool RouteColumn;
    }

    // The posture row of a leaf brigade (a brigade nested under an AI pool group, led by the leaf map and not by the ledger). Unit churn, 2026-10-17: the
    // leaf path never filled the row, so FacingPulseIntentProducer and FireControlIntentProducer, which read only rows with Has set, skipped every leaf
    // brigade: all eleven Union brigades of the smoke had no contact facing, no formation correction and no fire control (Confederate pool brigades had 317
    // Brigade and 105 Regiment pulse rows, the Union side none). Pure.
    internal static class LeafPosture
    {
        // The node id per brigade, built once: Record runs for every leaf brigade on every posture tick, so it must not concatenate a string each time.
        private static readonly Dictionary<int, string> NodeIds = new Dictionary<int, string>();

        static LeafPosture()
        {
            WhiskeyRealism.Util.BattleScope.Register("leaf-posture-node-ids", NodeIds.Clear);
        }

        internal static string NodeId(int groupId)
        {
            if (!NodeIds.TryGetValue(groupId, out string id))
            {
                id = "leaf-" + groupId;
                NodeIds[groupId] = id;
            }
            return id;
        }

        public static GroupPosture Record(DirectChildRole role, CommandTaskType task, int groupId, bool closeEngaged, bool flankRisk)
        {
            return new GroupPosture
            {
                Has = true,
                State = new CommandNodeOperationalState(NodeId(groupId), CommandEchelonKind.BrigadeLike, CommandNodeOperationsRuntime.MapRole(role), task, CommandTaskState.Committed),
                CloseEngaged = closeEngaged,
                FlankRisk = flankRisk,
            };
        }
    }

    // BC15: the posture each group's last evaluation recorded, kept for the ticks the group is not due (a Distant group's producers do not run, and
    // the per-tick table is cleared). Pure.
    //
    // A carried group answers FireControl, FacingPulse and Recon with exactly what its last full-rate evaluation answered, Submitted and
    // FacingSubmitted included: at full rate a group whose posture resubmits Stance and Move every tick (a hold task with an order) is "left
    // alone" by neither, and zeroing the flags on a carried tick would send fire control and the scout test into a group that full rate never
    // touches. Remember therefore runs after the facing pulse (which sets FacingSubmitted), not after the posture producer alone.
    internal sealed class PostureRecordCache
    {
        private readonly Dictionary<int, GroupPosture> _last = new Dictionary<int, GroupPosture>(128);

        public int Count => _last.Count;

        /// <summary>The group is not due this tick: its table row becomes the record of its last evaluation, flags included. False when it has none.</summary>
        public bool Carry(GroupPosture[] groups, int ownIndex, int groupId)
        {
            if (!_last.TryGetValue(groupId, out GroupPosture posture)) return false;
            groups[ownIndex] = posture;
            return true;
        }

        /// <summary>Records the group's table row as the answer of its evaluation (forgets the group when the row says it has none).</summary>
        public void Remember(GroupPosture[] groups, int ownIndex, int groupId)
        {
            if (groups[ownIndex].Has) _last[groupId] = groups[ownIndex];
            else _last.Remove(groupId);
        }

        public void Clear()
        {
            _last.Clear();
        }
    }
}
