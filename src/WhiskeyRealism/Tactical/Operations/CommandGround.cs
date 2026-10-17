using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Operations
{
    /// <summary>
    /// The ground the operations ledger plans on (smoke 2026-10-17): where each command node stands now, and the side's picture of the map's
    /// river crossings. The ledger builds its command-node states from intents alone, so without a ground every node was planned from the map
    /// origin; the coordinator's runtime half (CommandGroundRuntime) answers from the side's battle snapshot.
    /// </summary>
    public interface ICommandGround
    {
        /// <summary>The node's position and facing (degrees); false when the node is not in the side's snapshot.</summary>
        bool TryPlace(string nodeId, out float x, out float z, out float facingDegrees);

        /// <summary>The side's crossing picture; null when the map's regions are not built (no river is then known).</summary>
        ICrossingPicture Crossings { get; }
    }

    /// <summary>
    /// A position the ledger computes for a node (a slot, an assembly point, a reserve post, a flank guard, a fallback) stays on the node's own
    /// bank (smoke 2026-10-17: reserves and assembly points were laid across the river). A target in another land region is pulled back along
    /// the line to the node, in eighths, to the farthest point still in the node's region (the node's own position at worst); like the
    /// LeafReservePlanner's slot clamp. Open river and crossing blocks belong to no region, so a point on them is never kept.
    /// </summary>
    public static class OwnBank
    {
        private const int Steps = 8;

        public static DoctrineTargetPoint Keep(ICrossingPicture picture, in CommandNodeOperationalState node, DoctrineTargetPoint target)
        {
            if (picture == null || !picture.Ready || !node.HasPosition || !target.HasValue) return target;
            int home = picture.RegionAt(node.X, node.Z);
            if (home < 0) return target;                                   // the node stands on a crossing: no bank to keep
            if (picture.RegionAt(target.X, target.Z) == home) return target;

            for (int k = Steps - 1; k >= 1; k--)
            {
                float f = k / (float)Steps;
                float x = node.X + (target.X - node.X) * f;
                float z = node.Z + (target.Z - node.Z) * f;
                if (picture.RegionAt(x, z) == home) return DoctrineTargetPoint.From(x, z);
            }

            return DoctrineTargetPoint.From(node.X, node.Z);
        }
    }
}
