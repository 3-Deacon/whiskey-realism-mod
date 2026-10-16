// Chain target resolution (chain-of-command spec §B, battle-core spec §4 step b).
// Pure over the snapshot's own-unit records: ParentIndex (D5 command tree) and the
// ownership/chain flags from PlayerOwnership.
//
// Vanilla anchors (decompile, build 14703111):
//   Deployment 85321-85336 detaches the player's command (permanentlydetached) and
//     re-points its descendants' groupaiobject at it; TakeOverUnitBattle detaches a
//     taken-over unit (164455).
//   SetGroupFormation builds each level with GetAttachedUnitsReg(excludedechainedunits)
//     (91988-92030, 119865), so a group order skips a detached subtree.
//   ChangeStance(…, 0) 90786-90793 clears permanentlydetached (the player's
//     stance-off button). Nothing re-asserts it, and SetWaypoint/SetGroupFormation
//     have no dlcw filter, so a group order on an ancestor would then drag the
//     player's units.
//
// Rule: a group order is issued as-is unless it would reach an exposed
// player-owned descendant (PlayerOwnership.NeedsChainFanOut: the player's attached
// command, or an attached take-over). Then the group is not ordered; its direct
// children are, skipping player-owned children and fanning out again at every
// child that itself needs it. Player-owned units are never targets. An order that
// touches an ancestor of the player's command needs the player's share delivered
// as a dispatch (§C).
//
// Cross-level claims (OrderExecutor.BeginTickClaims): a group Move also fans out
// around descendants that hold a higher-priority Move claim this tick (shielded:
// skipped like player-owned children; shieldedBelow: the units above them, which
// fan out again), because SetGroupFormation re-paths every non-detached,
// non-routed child (92076-92108).

namespace WhiskeyRealism.Tactical.Battle
{
    public struct ChainResolution
    {
        public int Count;
        public int[] TargetIndices; // caller-owned; indices into the own records
        public bool FannedOut;
        public bool PlayerShareNeedsDispatch;
        // BC13: children the fan-out left out because they are player-owned (the order never reaches them).
        public int PlayerExcluded;
    }

    public static class TacticalChainOrderPlanner
    {
        // childBuffer is the breadth-first queue of groups being fanned out; results
        // go to result.TargetIndices. Both are caller-owned and reused, so this does
        // not allocate once TargetIndices exists. Work beyond either array's
        // capacity is dropped.
        public static void ResolveTargets(UnitRecord[] own, int count, int groupIndex, int[] childBuffer, ref ChainResolution result)
        {
            ResolveTargets(own, count, groupIndex, childBuffer, ref result, null, null);
        }

        // shielded / shieldedBelow: indexed like own, null = no cross-level claims.
        public static void ResolveTargets(UnitRecord[] own, int count, int groupIndex, int[] childBuffer, ref ChainResolution result,
            bool[] shielded, bool[] shieldedBelow)
        {
            Reset(ref result, childBuffer, count);
            if (own == null) return;
            if (count > own.Length) count = own.Length;
            if (groupIndex < 0 || groupIndex >= count) return;

            UnitFlags groupFlags = own[groupIndex].Flags;
            if ((groupFlags & (UnitFlags.PlayerOwned | UnitFlags.PlayerCommand)) != 0) return;

            result.PlayerShareNeedsDispatch = (groupFlags & UnitFlags.AncestorOfPlayerCommand) != 0;
            if (!NeedsFanOut(own, count, groupIndex, shieldedBelow))
            {
                Add(ref result, groupIndex);
                return;
            }

            result.FannedOut = true;
            if (childBuffer == null || childBuffer.Length == 0) return;

            int head = 0;
            int tail = 0;
            childBuffer[tail++] = groupIndex;
            while (head < tail)
            {
                int parent = childBuffer[head++];
                for (int child = 0; child < count; child++)
                {
                    if (child == parent || own[child].ParentIndex != parent) continue;
                    if ((own[child].Flags & UnitFlags.PlayerOwned) != 0)
                    {
                        result.PlayerExcluded++;
                        continue;
                    }
                    if (shielded != null && child < shielded.Length && shielded[child]) continue;
                    if (NeedsFanOut(own, count, child, shieldedBelow))
                    {
                        if (tail < childBuffer.Length) childBuffer[tail++] = child;
                        continue;
                    }
                    Add(ref result, child);
                }
            }
        }

        private static bool NeedsFanOut(UnitRecord[] own, int count, int index, bool[] shieldedBelow)
        {
            return PlayerOwnership.NeedsChainFanOut(own, count, index)
                || (shieldedBelow != null && index < shieldedBelow.Length && shieldedBelow[index]);
        }

        private static void Reset(ref ChainResolution result, int[] childBuffer, int count)
        {
            if (result.TargetIndices == null)
                result.TargetIndices = new int[System.Math.Max(1, childBuffer != null ? childBuffer.Length : count)];
            result.Count = 0;
            result.FannedOut = false;
            result.PlayerShareNeedsDispatch = false;
            result.PlayerExcluded = 0;
        }

        private static void Add(ref ChainResolution result, int index)
        {
            if (result.Count < result.TargetIndices.Length) result.TargetIndices[result.Count++] = index;
        }
    }
}
