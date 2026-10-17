using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Util;

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

        /// <summary>
        /// The side's home bank (<see cref="HomeBank"/>): the land region it deployed on, fixed for the battle; -1 when the picture is not
        /// ready or nothing settles it. <paramref name="placedNodes"/> are the states the ledger is planning, which settle it at the first tick.
        /// </summary>
        int HomeBank(CommandNodeOperationalState[] placedNodes);
    }

    /// <summary>
    /// A side's home bank is fixed for the battle (verify round 3: a head-count of where the force stands flipped once most of it had crossed,
    /// and the nodes still at home were sent forward over the river). It is the bank of the side's own deployment zone (the zone's point nearest
    /// the centre of the placed nodes), else the bank that centre stands on (the placed node nearest it when the centre is on a river), settled
    /// once at the first ledger tick that can and recorded for the alliance; the battle's end resets it (BattleScope). It never moves with the force.
    /// </summary>
    public static class HomeBank
    {
        private const int Unset = -2;
        private static readonly int[] Recorded = { Unset, Unset };

        static HomeBank()
        {
            BattleScope.Register("home-bank", () => { Recorded[0] = Unset; Recorded[1] = Unset; });
        }

        /// <summary>The alliance's home bank: the recorded one, else settled now from the zone and the nodes (and recorded); -1 when it cannot be settled yet.</summary>
        public static int Settle(int alliance, ICrossingPicture picture, IOwnDeploymentZone zone, CommandNodeOperationalState[] nodes)
        {
            bool recordable = alliance >= 0 && alliance < Recorded.Length;
            if (recordable && Recorded[alliance] != Unset) return Recorded[alliance];
            int home = Resolve(picture, zone, nodes);
            if (home >= 0 && recordable) Recorded[alliance] = home;
            return home;
        }

        private static int Resolve(ICrossingPicture picture, IOwnDeploymentZone zone, CommandNodeOperationalState[] nodes)
        {
            if (picture == null || !picture.Ready || nodes == null) return -1;
            double sumX = 0d, sumZ = 0d;
            int placed = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!nodes[i].HasPosition || nodes[i].Role == CommandNodeRole.Unknown) continue;
                sumX += nodes[i].X;
                sumZ += nodes[i].Z;
                placed++;
            }

            if (placed == 0) return -1;
            float cx = (float)(sumX / placed), cz = (float)(sumZ / placed);
            float zx = cx, zz = cz;
            if (zone != null && zone.TryClamp(cx, cz, ref zx, ref zz))
            {
                int zoneRegion = picture.RegionAt(zx, zz);
                if (zoneRegion >= 0) return zoneRegion;
            }

            int centre = picture.RegionAt(cx, cz);
            if (centre >= 0) return centre;
            int best = -1;
            float bestSquared = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!nodes[i].HasPosition || nodes[i].Role == CommandNodeRole.Unknown) continue;
                int region = picture.RegionAt(nodes[i].X, nodes[i].Z);
                if (region < 0) continue;
                float dx = nodes[i].X - cx, dz = nodes[i].Z - cz;
                if (dx * dx + dz * dz < bestSquared) { best = region; bestSquared = dx * dx + dz * dz; }
            }

            return best;
        }
    }

    /// <summary>
    /// A position the ledger computes for a node (a slot, an assembly point, a reserve post, a flank guard, a fallback) stays on a bank: a
    /// target in another land region is pulled back along the line from an anchor on that bank to the target, in eighths, to the farthest
    /// point still on the bank (the anchor itself at worst); like the LeafReservePlanner's slot clamp. Open river and crossing blocks belong
    /// to no region, so a point on them is never kept. Whose bank: <see cref="Keep"/> the node's own, where it stands; a Defender's post and a
    /// fall back are the side's home bank (<see cref="HomeBank"/>, <see cref="KeepFallBack"/>), so a node that has already crossed is sent back (smoke 2026-10-17: the posts of a node already
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
        /// The target kept on a bank (the home bank). A node that stands on it is the anchor of the pull-back; a node that has crossed is not, so the
        /// anchor is the placed node on the bank nearest the target, and the crossed node's post is back on the bank it left.
        /// </summary>
        public static DoctrineTargetPoint KeepOnBank(ICrossingPicture picture, int bank, CommandNodeOperationalState[] nodes,
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

        /// <summary>
        /// A fall-back or withdrawal target (every stance): the target as it would be without the clamp, unless it lies off the home bank, then
        /// pulled onto it, and never forward of the node relative to the enemy: a clamp that would move it forward (toward the enemy point) is
        /// dropped for the unclamped target, so a fall back never leads over the river toward the enemy.
        /// </summary>
        public static DoctrineTargetPoint KeepFallBack(ICrossingPicture picture, int home, CommandNodeOperationalState[] nodes,
            in CommandNodeOperationalState node, DoctrineTargetPoint target, float enemyX, float enemyZ)
        {
            DoctrineTargetPoint kept = KeepOnBank(picture, home, nodes, node, target);
            if (!kept.HasValue || !target.HasValue || !node.HasPosition) return kept;
            if (kept.X == target.X && kept.Z == target.Z) return target;
            float ex = enemyX - node.X, ez = enemyZ - node.Z;
            float length = (float)System.Math.Sqrt(ex * ex + ez * ez);
            if (length < 1f) return kept;
            float forwardKept = ((kept.X - node.X) * ex + (kept.Z - node.Z) * ez) / length;
            float forwardTarget = ((target.X - node.X) * ex + (target.Z - node.Z) * ez) / length;
            return forwardKept > forwardTarget ? target : kept;
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
