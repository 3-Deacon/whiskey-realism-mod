using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalNestedDivisionPlayPlanner
    {
        public static IReadOnlyList<CommandNodeOperationalState> Apply(
            CommandTreeSnapshot tree,
            IReadOnlyList<CommandNodeOperationalState> commandStates)
        {
            if (commandStates == null || commandStates.Count == 0) return Array.Empty<CommandNodeOperationalState>();
            if (tree == null || !tree.HasNodes) return Copy(commandStates);

            var statesByNode = new Dictionary<string, CommandNodeOperationalState>(StringComparer.Ordinal);
            for (int i = 0; i < commandStates.Count; i++)
            {
                statesByNode[commandStates[i].NodeId] = commandStates[i];
            }

            CommandNodeSnapshot parent;
            var children = FindSingleNestedCommand(tree, statesByNode, commandStates.Count, out parent);
            if (children.Count < 3) return Copy(commandStates);

            var nested = new CommandNodeOperationalState[children.Count + 1];
            for (int i = 0; i < children.Count; i++)
            {
                CommandNodeOperationalState existing;
                statesByNode.TryGetValue(children[i].NodeId, out existing);
                CommandNodeRole role = NestedRoleFor(i, children.Count);
                nested[i] = new CommandNodeOperationalState(
                    children[i].NodeId,
                    CommandEchelonKind.BrigadeLike,
                    role,
                    NestedTaskFor(role, existing.Task),
                    existing.TaskState,
                    existing.X,
                    existing.Z,
                    existing.FacingDegrees);
            }

            CommandNodeOperationalState parentState;
            statesByNode.TryGetValue(parent.NodeId, out parentState);
            nested[children.Count] = new CommandNodeOperationalState(
                parent.NodeId,
                parentState.Echelon,
                CommandNodeRole.Reserve,
                CommandTaskType.ReserveWait,
                parentState.TaskState,
                parentState.X,
                parentState.Z,
                parentState.FacingDegrees);

            return nested;
        }

        private static List<CommandNodeSnapshot> FindSingleNestedCommand(
            CommandTreeSnapshot tree,
            Dictionary<string, CommandNodeOperationalState> statesByNode,
            int stateCount,
            out CommandNodeSnapshot parent)
        {
            parent = default(CommandNodeSnapshot);
            var bestChildren = new List<CommandNodeSnapshot>();
            if (tree.Nodes == null || tree.Nodes.Count == 0) return bestChildren;

            for (int i = 0; i < tree.Nodes.Count; i++)
            {
                var candidate = tree.Nodes[i];
                if (candidate.Synthetic || !candidate.Active) continue;
                if (!statesByNode.ContainsKey(candidate.NodeId)) continue;

                var children = DirectCommandChildren(tree.Nodes, candidate.NodeId, statesByNode);
                if (children.Count < 3) continue;
                if (stateCount > children.Count + 1) continue;
                if (children.Count > bestChildren.Count)
                {
                    parent = candidate;
                    bestChildren = children;
                }
            }

            return bestChildren;
        }

        private static List<CommandNodeSnapshot> DirectCommandChildren(
            IReadOnlyList<CommandNodeSnapshot> nodes,
            string parentNodeId,
            Dictionary<string, CommandNodeOperationalState> statesByNode)
        {
            var children = new List<CommandNodeSnapshot>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node.Synthetic || !node.Active) continue;
                if (!string.Equals(node.ParentNodeId, parentNodeId, StringComparison.Ordinal)) continue;
                if (!statesByNode.ContainsKey(node.NodeId)) continue;
                children.Add(node);
            }

            return children;
        }

        private static CommandNodeRole NestedRoleFor(int index, int count)
        {
            if (count <= 1) return CommandNodeRole.MainEffort;
            if (index == 0 || index == count - 1) return CommandNodeRole.FlankMarch;
            int reserveIndex = ReserveSlotIndex(count);
            if (index == reserveIndex) return CommandNodeRole.Reserve;
            int mainIndex = MainSlotIndex(count, reserveIndex);
            if (index == mainIndex) return CommandNodeRole.MainEffort;
            return index < mainIndex
                ? CommandNodeRole.SupportingAttack
                : CommandNodeRole.FixingForce;
        }

        private static int ReserveSlotIndex(int count)
        {
            return count >= 4 ? count - 2 : -1;
        }

        private static int MainSlotIndex(int count, int reserveIndex)
        {
            if (count <= 2) return Math.Max(0, count - 1);
            double center = (count - 1) * 0.5d;
            int best = 1;
            double bestDistance = double.MaxValue;
            for (int i = 1; i < count - 1; i++)
            {
                if (i == reserveIndex) continue;
                double distance = Math.Abs(i - center);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static CommandTaskType NestedTaskFor(CommandNodeRole role, CommandTaskType fallback)
        {
            switch (role)
            {
                case CommandNodeRole.FlankMarch:
                    return CommandTaskType.FormUp;
                case CommandNodeRole.Reserve:
                    return CommandTaskType.ReserveWait;
                case CommandNodeRole.MainEffort:
                case CommandNodeRole.SupportingAttack:
                case CommandNodeRole.FixingForce:
                    return CommandTaskType.FormUp;
                default:
                    return fallback == CommandTaskType.None ? CommandTaskType.FormUp : fallback;
            }
        }

        private static IReadOnlyList<CommandNodeOperationalState> Copy(IReadOnlyList<CommandNodeOperationalState> source)
        {
            var copy = new CommandNodeOperationalState[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }
    }
}
