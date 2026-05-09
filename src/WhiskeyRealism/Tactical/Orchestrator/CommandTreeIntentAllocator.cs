using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class CommandTreeIntentAllocator
    {
        public static IReadOnlyList<CommandNodeIntent> Allocate(
            CommandTreeSnapshot tree,
            IReadOnlyList<DirectChildIntent> directChildIntents)
        {
            if (tree == null || !tree.HasNodes)
            {
                return Array.Empty<CommandNodeIntent>();
            }

            var o3ByNodeId = new Dictionary<string, DirectChildIntent>(StringComparer.Ordinal);
            if (directChildIntents != null)
            {
                for (var i = 0; i < directChildIntents.Count; i++)
                {
                    var directIntent = directChildIntents[i];
                    var nodeId = DirectChildToNodeId(directIntent.ChildId);
                    if (!string.IsNullOrEmpty(nodeId))
                    {
                        o3ByNodeId[nodeId] = directIntent;
                    }
                }
            }

            var parentByNode = BuildParentMap(tree.Nodes);
            var allocated = new Dictionary<string, CommandNodeIntent>(StringComparer.Ordinal);
            var result = new List<CommandNodeIntent>(tree.Nodes.Count);

            for (var i = 0; i < tree.Nodes.Count; i++)
            {
                var node = tree.Nodes[i];
                var intent = AllocateForNode(node, o3ByNodeId, parentByNode, allocated);
                allocated[node.NodeId] = intent;
                result.Add(intent);
            }

            return result;
        }

        private static CommandNodeIntent AllocateForNode(
            CommandNodeSnapshot node,
            Dictionary<string, DirectChildIntent> o3ByNodeId,
            Dictionary<string, string> parentByNode,
            Dictionary<string, CommandNodeIntent> allocated)
        {
            if (o3ByNodeId.TryGetValue(node.NodeId, out var directIntent))
            {
                return new CommandNodeIntent(
                    node.NodeId,
                    node.NodeId,
                    directIntent.Role,
                    directIntent.Axis,
                    directIntent.PrimarySector,
                    (int)Math.Round(directIntent.SupportPriority01 * 100f),
                    directIntent.AggressionBias01,
                    node.Depth);
            }

            var parentNodeId = node.ParentNodeId;
            while (!string.IsNullOrEmpty(parentNodeId))
            {
                if (allocated.TryGetValue(parentNodeId, out var parentIntent))
                {
                    return new CommandNodeIntent(
                        node.NodeId,
                        parentIntent.SourceNodeId,
                        parentIntent.Role,
                        parentIntent.Axis,
                        parentIntent.PrimarySector,
                        parentIntent.SupportPriority,
                        parentIntent.AggressionBias01,
                        node.Depth);
                }

                parentByNode.TryGetValue(parentNodeId, out parentNodeId);
            }

            return new CommandNodeIntent(
                node.NodeId,
                node.NodeId,
                DirectChildRole.Reserve,
                DirectChildAxis.Hold,
                primarySector: 0,
                supportPriority: 25,
                aggressionBias01: 0.5f,
                node.Depth);
        }

        private static Dictionary<string, string> BuildParentMap(IReadOnlyList<CommandNodeSnapshot> nodes)
        {
            var parentByNode = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < nodes.Count; i++)
            {
                parentByNode[nodes[i].NodeId] = nodes[i].ParentNodeId;
            }

            return parentByNode;
        }

        private static string DirectChildToNodeId(string childId)
        {
            if (string.IsNullOrWhiteSpace(childId))
            {
                return string.Empty;
            }

            const string childPrefix = "child-";
            if (childId.StartsWith(childPrefix, StringComparison.Ordinal))
            {
                return "node-" + childId.Substring(childPrefix.Length);
            }

            const string syntheticArmyPrefix = "synth-army-";
            if (childId.StartsWith(syntheticArmyPrefix, StringComparison.Ordinal))
            {
                return "node-" + childId.Substring(syntheticArmyPrefix.Length);
            }

            return string.Empty;
        }
    }
}
