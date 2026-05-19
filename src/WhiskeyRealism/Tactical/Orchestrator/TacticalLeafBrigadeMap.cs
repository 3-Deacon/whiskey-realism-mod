using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure recursive role cascade: given the full nested command tree (built by
    /// TacticalCommandTreeProbe.BuildTree), a set of top-tier role assignments
    /// (typically from DirectChildAllocator), and the commander's aggression
    /// vector, produce a flat map of (leaf brigade instanceId → LeafBrigadeAssignment).
    ///
    /// Each assignment includes the full cascade chain (parent role at each
    /// tier, top to leaf) so telemetry can show why a specific brigade got
    /// its task.
    ///
    /// This is the depth-agnostic improvement over SoW's rigid per-tier Think
    /// dispatch: the same algorithm walks 1-tier (army → brigades direct),
    /// 2-tier (army → divisions → brigades), or N-tier hierarchies. Sibling
    /// distribution is geometry-aware (left-to-right by world X) and
    /// personality-modulated (high-aggression commanders widen the attack band).
    /// </summary>
    public static class TacticalLeafBrigadeMap
    {
        public readonly struct LeafAssignment
        {
            public LeafAssignment(
                int instanceId,
                string displayName,
                DirectChildRole leafRole,
                CommandTaskType leafTask,
                IReadOnlyList<DirectChildRole> cascadeChain,
                IReadOnlyList<string> parentNameChain,
                int topLevelChildIndex)
            {
                InstanceId = instanceId;
                DisplayName = displayName ?? string.Empty;
                LeafRole = leafRole;
                LeafTask = leafTask;
                CascadeChain = cascadeChain ?? Array.Empty<DirectChildRole>();
                ParentNameChain = parentNameChain ?? Array.Empty<string>();
                TopLevelChildIndex = topLevelChildIndex;
            }

            public int InstanceId { get; }
            public string DisplayName { get; }
            public DirectChildRole LeafRole { get; }
            public CommandTaskType LeafTask { get; }
            public IReadOnlyList<DirectChildRole> CascadeChain { get; }
            public IReadOnlyList<string> ParentNameChain { get; }
            public int TopLevelChildIndex { get; }

            public string CascadeChainString
            {
                get
                {
                    if (CascadeChain.Count == 0) return string.Empty;
                    var parts = new string[CascadeChain.Count];
                    for (int i = 0; i < CascadeChain.Count; i++) parts[i] = CascadeChain[i].ToString();
                    return string.Join("->", parts);
                }
            }
        }

        public readonly struct TopAssignment
        {
            public TopAssignment(int childInstanceId, DirectChildRole role)
            {
                ChildInstanceId = childInstanceId;
                Role = role;
            }
            public int ChildInstanceId { get; }
            public DirectChildRole Role { get; }
        }

        /// <summary>
        /// Builds the leaf assignment map. Walks each top-tier child via the tree,
        /// cascades the top role down to its descendant leaves, and returns a
        /// dictionary keyed by leaf brigade instanceId.
        /// </summary>
        public static IReadOnlyDictionary<int, LeafAssignment> BuildMap(
            IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree,
            IReadOnlyList<TopAssignment> topLevelAssignments,
            float commanderAggression01)
        {
            var result = new Dictionary<int, LeafAssignment>();
            if (tree == null || topLevelAssignments == null) return result;

            for (int i = 0; i < topLevelAssignments.Count; i++)
            {
                var top = topLevelAssignments[i];
                if (!tree.TryGetValue(top.ChildInstanceId, out var topNode)) continue;
                CascadeInto(
                    topNode,
                    top.Role,
                    new List<DirectChildRole> { top.Role },
                    new List<string> { topNode.Name },
                    i,
                    tree,
                    commanderAggression01,
                    result);
            }

            return result;
        }

        private static void CascadeInto(
            TacticalCommandTreeProbe.ProbeNode node,
            DirectChildRole nodeRole,
            List<DirectChildRole> chainSoFar,
            List<string> parentNamesSoFar,
            int topLevelChildIndex,
            IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree,
            float commanderAggression01,
            Dictionary<int, LeafAssignment> output)
        {
            if (!node.Active) return;

            var children = TacticalCommandTreeProbe.EnumerateChildrenLeftToRight(node.InstanceId, tree);
            if (children.Count == 0)
            {
                // Leaf brigade reached. Record its assignment.
                output[node.InstanceId] = new LeafAssignment(
                    node.InstanceId,
                    node.Name,
                    nodeRole,
                    TacticalRoleCascade.RoleToLeafTask(nodeRole),
                    chainSoFar.ToArray(),
                    parentNamesSoFar.ToArray(),
                    topLevelChildIndex);
                return;
            }

            // Non-leaf: distribute the parent role to each child via the cascade
            // and recurse into each child.
            for (int idx = 0; idx < children.Count; idx++)
            {
                var child = children[idx];
                var ctx = new TacticalRoleCascade.CascadeContext(
                    parentRole: nodeRole,
                    childIndex: idx,
                    childCount: children.Count,
                    childStrengthBucket: child.StrengthBucket,
                    childFlankExposureBucket: 0,
                    commanderAggression01: commanderAggression01);
                var derived = TacticalRoleCascade.DistributeChildRole(ctx);

                var nextChain = new List<DirectChildRole>(chainSoFar.Count + 1);
                nextChain.AddRange(chainSoFar);
                nextChain.Add(derived);

                var nextNames = new List<string>(parentNamesSoFar.Count + 1);
                nextNames.AddRange(parentNamesSoFar);
                nextNames.Add(child.Name);

                CascadeInto(
                    child,
                    derived,
                    nextChain,
                    nextNames,
                    topLevelChildIndex,
                    tree,
                    commanderAggression01,
                    output);
            }
        }
    }
}
