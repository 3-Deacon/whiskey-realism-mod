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
    /// A position the ledger computes for a node (a slot, an assembly point, a reserve post, a flank guard, a fallback) stays on a bank: a
    /// target in another land region is pulled back along the line from an anchor on that bank to the target, in eighths, to the farthest
    /// point still on the bank (the anchor itself at worst); like the LeafReservePlanner's slot clamp. Open river and crossing blocks belong
    /// to no region, so a point on them is never kept. Whose bank: <see cref="Keep"/> the node's own, where it stands; a Defender's is the
    /// side's (<see cref="SideBank"/>), so a node that has already crossed is sent back (smoke 2026-10-17: the posts of a node already
    /// across were pulled to the far bank and the Defender stayed there).
    /// </summary>
    public static class OwnBank
    {
        private const int Steps = 8;

        /// <summary>The target kept on the bank the node stands on; unchanged when the picture cannot judge.</summary>
        public static DoctrineTargetPoint Keep(ICrossingPicture picture, in CommandNodeOperationalState node, DoctrineTargetPoint target)
        {
            if (picture == null || !picture.Ready || !node.HasPosition || !target.HasValue) return target;
            int home = picture.RegionAt(node.X, node.Z);
            return home < 0 ? target : PullBack(picture, home, node.X, node.Z, target);   // on a crossing: no bank to keep
        }

        /// <summary>
        /// The land region most of the placed nodes stand in (the first of them on a tie); -1 with no picture, no placed node or none on land.
        /// </summary>
        public static int SideBank(ICrossingPicture picture, CommandNodeOperationalState[] nodes)
        {
            if (picture == null || !picture.Ready || nodes == null) return -1;
            int best = -1, bestCount = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!nodes[i].HasPosition || nodes[i].Role == CommandNodeRole.Unknown) continue;
                int region = picture.RegionAt(nodes[i].X, nodes[i].Z);
                if (region < 0) continue;
                int count = 0;
                for (int k = 0; k < nodes.Length; k++)
                {
                    if (!nodes[k].HasPosition || nodes[k].Role == CommandNodeRole.Unknown) continue;
                    if (picture.RegionAt(nodes[k].X, nodes[k].Z) == region) count++;
                }

                if (count > bestCount) { best = region; bestCount = count; }
            }

            return best;
        }

        /// <summary>
        /// The target kept on the side's bank. A node that stands on it is the anchor of the pull-back; a node that has crossed is not, so the
        /// anchor is the placed node on the bank nearest the target, and the crossed node's post is back on the bank it left.
        /// </summary>
        public static DoctrineTargetPoint KeepOnSideBank(ICrossingPicture picture, int bank, CommandNodeOperationalState[] nodes,
            in CommandNodeOperationalState node, DoctrineTargetPoint target)
        {
            if (picture == null || !picture.Ready || bank < 0 || !target.HasValue) return target;
            if (picture.RegionAt(target.X, target.Z) == bank) return target;
            if (node.HasPosition && picture.RegionAt(node.X, node.Z) == bank) return PullBack(picture, bank, node.X, node.Z, target);

            int nearest = -1;
            float nearestSquared = float.MaxValue;
            for (int i = 0; nodes != null && i < nodes.Length; i++)
            {
                if (!nodes[i].HasPosition || nodes[i].Role == CommandNodeRole.Unknown || picture.RegionAt(nodes[i].X, nodes[i].Z) != bank) continue;
                float dx = nodes[i].X - target.X, dz = nodes[i].Z - target.Z;
                float squared = dx * dx + dz * dz;
                if (squared < nearestSquared) { nearest = i; nearestSquared = squared; }
            }

            return nearest < 0 ? target : PullBack(picture, bank, nodes[nearest].X, nodes[nearest].Z, target);
        }

        private static DoctrineTargetPoint PullBack(ICrossingPicture picture, int bank, float anchorX, float anchorZ, DoctrineTargetPoint target)
        {
            if (picture.RegionAt(target.X, target.Z) == bank) return target;
            for (int k = Steps - 1; k >= 1; k--)
            {
                float f = k / (float)Steps;
                float x = anchorX + (target.X - anchorX) * f;
                float z = anchorZ + (target.Z - anchorZ) * f;
                if (picture.RegionAt(x, z) == bank) return DoctrineTargetPoint.From(x, z);
            }

            return DoctrineTargetPoint.From(anchorX, anchorZ);
        }
    }
}
