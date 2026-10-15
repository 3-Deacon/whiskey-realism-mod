// Chain target resolution (chain-of-command spec §B, battle-core spec §4 step b).
// Pure over the snapshot's own-unit records: ParentIndex (D5 command tree) and the
// ownership/chain flags from PlayerOwnership.
//
// Vanilla anchors (decompile, build 14703111):
//   Deployment 85321-85336 detaches the player's command (permanentlydetached) and
//     re-points its descendants' groupaiobject at it.
//   GetAttachedUnitsReg(excludedechainedunits) 119865 drops detached children, so a
//     group order on an ancestor skips the detached player subtree.
//   ChangeStance(…, 0) 90786-90793 clears permanentlydetached (the player's
//     stance-off button). Nothing re-asserts it, and SetWaypoint/SetGroupFormation
//     have no dlcw filter, so a group order on an ancestor would then drag the
//     player's units.
//
// Rule: a group that is not an ancestor of the player's command is its own target.
// An ancestor with the player's command detached is ordered as a group. An ancestor
// with the player's command attached is not ordered as a group; its direct
// children are ordered instead, skipping the child that is or contains the player's
// command, and the same rule repeats down to the player's direct parent.
// Player-owned units are never targets. Any order that touches an ancestor needs
// the player's share delivered as a dispatch (§C).

namespace WhiskeyRealism.Tactical.Battle
{
    public struct ChainResolution
    {
        public int Count;
        public int[] TargetIndices; // caller-owned; indices into the own records
        public bool FannedOut;
        public bool PlayerShareNeedsDispatch;
    }

    public static class TacticalChainOrderPlanner
    {
        public static void ResolveTargets(BattleSnapshot s, int groupIndex, int[] childBuffer, ref ChainResolution result)
        {
            if (s == null)
            {
                Reset(ref result, null, 0);
                return;
            }
            ResolveTargets(s.Own, s.OwnCount, groupIndex, childBuffer, ref result);
        }

        // childBuffer holds one level's direct children while descending; results go
        // to result.TargetIndices. Both are caller-owned and reused, so this does not
        // allocate once TargetIndices exists. Targets beyond either array's capacity
        // are dropped.
        public static void ResolveTargets(UnitRecord[] own, int count, int groupIndex, int[] childBuffer, ref ChainResolution result)
        {
            Reset(ref result, childBuffer, count);
            if (own == null) return;
            if (count > own.Length) count = own.Length;
            if (groupIndex < 0 || groupIndex >= count) return;

            UnitFlags groupFlags = own[groupIndex].Flags;
            if ((groupFlags & (UnitFlags.PlayerOwned | UnitFlags.PlayerCommand)) != 0) return;

            bool ancestor = (groupFlags & UnitFlags.AncestorOfPlayerCommand) != 0;
            result.PlayerShareNeedsDispatch = ancestor;
            if (!PlayerOwnership.NeedsChainFanOut(groupFlags))
            {
                Add(ref result, groupIndex);
                return;
            }

            result.FannedOut = true;
            if (childBuffer == null) return;

            // Exactly one child per level contains the player's command, so the
            // descent is a single path from the group to the player's direct parent.
            int level = groupIndex;
            for (int depth = 0; level >= 0 && depth < count; depth++)
            {
                int children = DirectChildren(own, count, level, childBuffer);
                int next = -1;
                for (int i = 0; i < children; i++)
                {
                    int child = childBuffer[i];
                    UnitFlags flags = own[child].Flags;
                    if ((flags & (UnitFlags.PlayerOwned | UnitFlags.PlayerCommand)) != 0) continue;
                    if ((flags & UnitFlags.AncestorOfPlayerCommand) != 0) { next = child; continue; }
                    Add(ref result, child);
                }
                level = next;
            }
        }

        private static int DirectChildren(UnitRecord[] own, int count, int parent, int[] buffer)
        {
            int n = 0;
            for (int i = 0; i < count && n < buffer.Length; i++)
                if (own[i].ParentIndex == parent && i != parent) buffer[n++] = i;
            return n;
        }

        private static void Reset(ref ChainResolution result, int[] childBuffer, int count)
        {
            if (result.TargetIndices == null)
                result.TargetIndices = new int[System.Math.Max(1, childBuffer != null ? childBuffer.Length : count)];
            result.Count = 0;
            result.FannedOut = false;
            result.PlayerShareNeedsDispatch = false;
        }

        private static void Add(ref ChainResolution result, int index)
        {
            if (result.Count < result.TargetIndices.Length) result.TargetIndices[result.Count++] = index;
        }
    }
}
