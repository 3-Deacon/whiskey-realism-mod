using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class CommandTreeBuilder
    {
        internal readonly struct CommandProbe
        {
            public CommandProbe(
                int instanceId,
                int parentInstanceId,
                int allianceId,
                int rawUnitTyp,
                string displayName,
                bool active,
                bool routed,
                bool markedForRout)
            {
                InstanceId = instanceId;
                ParentInstanceId = parentInstanceId;
                AllianceId = allianceId;
                RawUnitTyp = rawUnitTyp;
                DisplayName = displayName ?? string.Empty;
                Active = active;
                Routed = routed;
                MarkedForRout = markedForRout;
            }

            public int InstanceId { get; }
            public int ParentInstanceId { get; }
            public int AllianceId { get; }
            public int RawUnitTyp { get; }
            public string DisplayName { get; }
            public bool Active { get; }
            public bool Routed { get; }
            public bool MarkedForRout { get; }
        }

        public static CommandTreeSnapshot Build(
            IReadOnlyList<CommandProbe> probes,
            int allianceId,
            int commandHierarchyShift)
        {
            var threshold = ClampShiftedMin(commandHierarchyShift);
            var candidates = FilterCandidates(probes, allianceId, threshold);
            if (candidates.Count == 0)
            {
                var synthetic = SyntheticRoot(allianceId, threshold, commandHierarchyShift);
                return new CommandTreeSnapshot(
                    allianceId,
                    synthetic.NodeId,
                    new[] { synthetic },
                    0,
                    0,
                    BuildDistribution(new[] { synthetic }));
            }

            var byInstance = new Dictionary<int, CommandProbe>();
            foreach (var candidate in candidates)
            {
                byInstance[candidate.InstanceId] = candidate;
            }

            var childrenByParent = new Dictionary<int, List<CommandProbe>>();
            var topRoots = new List<CommandProbe>();
            var missingParents = 0;

            foreach (var candidate in candidates)
            {
                if (candidate.ParentInstanceId == 0 || !byInstance.TryGetValue(candidate.ParentInstanceId, out var parent))
                {
                    if (candidate.ParentInstanceId != 0)
                    {
                        missingParents++;
                    }

                    topRoots.Add(candidate);
                    continue;
                }

                if (parent.RawUnitTyp <= candidate.RawUnitTyp)
                {
                    topRoots.Add(candidate);
                    continue;
                }

                if (!childrenByParent.TryGetValue(parent.InstanceId, out var children))
                {
                    children = new List<CommandProbe>();
                    childrenByParent[parent.InstanceId] = children;
                }

                children.Add(candidate);
            }

            Sort(topRoots);
            foreach (var children in childrenByParent.Values)
            {
                Sort(children);
            }

            return BuildSnapshot(allianceId, commandHierarchyShift, threshold, topRoots, childrenByParent, missingParents);
        }

        private static List<CommandProbe> FilterCandidates(IReadOnlyList<CommandProbe> probes, int allianceId, int threshold)
        {
            var candidates = new List<CommandProbe>();
            if (probes == null)
            {
                return candidates;
            }

            for (var i = 0; i < probes.Count; i++)
            {
                var probe = probes[i];
                if (probe.InstanceId == 0) continue;
                if (probe.AllianceId != allianceId) continue;
                if (!probe.Active || probe.Routed || probe.MarkedForRout) continue;
                if (probe.RawUnitTyp < threshold) continue;
                candidates.Add(probe);
            }

            Sort(candidates);
            return candidates;
        }

        private static CommandTreeSnapshot BuildSnapshot(
            int allianceId,
            int commandHierarchyShift,
            int threshold,
            List<CommandProbe> topRoots,
            Dictionary<int, List<CommandProbe>> childrenByParent,
            int missingParents)
        {
            var nodes = new List<CommandNodeSnapshot>();
            var queue = new Queue<(CommandProbe probe, string parentNodeId, int depth)>();
            string rootNodeId;

            if (topRoots.Count == 1)
            {
                rootNodeId = NodeId(topRoots[0].InstanceId);
                queue.Enqueue((topRoots[0], string.Empty, 0));
            }
            else
            {
                var synthetic = SyntheticRoot(allianceId, threshold, commandHierarchyShift);
                rootNodeId = synthetic.NodeId;
                nodes.Add(synthetic);
                for (var i = 0; i < topRoots.Count; i++)
                {
                    queue.Enqueue((topRoots[i], synthetic.NodeId, 1));
                }
            }

            var maxDepth = 0;
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                var node = ToNode(item.probe, item.parentNodeId, commandHierarchyShift, item.depth);
                nodes.Add(node);
                if (node.Depth > maxDepth) maxDepth = node.Depth;

                if (!childrenByParent.TryGetValue(item.probe.InstanceId, out var children))
                {
                    continue;
                }

                for (var i = 0; i < children.Count; i++)
                {
                    queue.Enqueue((children[i], node.NodeId, node.Depth + 1));
                }
            }

            return new CommandTreeSnapshot(
                allianceId,
                rootNodeId,
                nodes,
                maxDepth,
                missingParents,
                BuildDistribution(nodes));
        }

        private static CommandNodeSnapshot ToNode(CommandProbe probe, string parentNodeId, int commandHierarchyShift, int depth)
        {
            return new CommandNodeSnapshot(
                NodeId(probe.InstanceId),
                parentNodeId,
                probe.InstanceId,
                probe.ParentInstanceId,
                probe.AllianceId,
                probe.RawUnitTyp,
                commandHierarchyShift,
                probe.DisplayName,
                probe.Active,
                synthetic: false,
                depth);
        }

        private static CommandNodeSnapshot SyntheticRoot(int allianceId, int threshold, int commandHierarchyShift)
        {
            return new CommandNodeSnapshot(
                $"synth-root-{allianceId}",
                string.Empty,
                0,
                0,
                allianceId,
                threshold,
                commandHierarchyShift,
                $"Synthetic side root {allianceId}",
                true,
                true,
                0);
        }

        private static string BuildDistribution(IReadOnlyList<CommandNodeSnapshot> nodes)
        {
            var counts = new SortedDictionary<int, int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            for (var i = 0; i < nodes.Count; i++)
            {
                var raw = nodes[i].RawUnitTyp;
                counts[raw] = counts.TryGetValue(raw, out var count) ? count + 1 : 1;
            }

            var parts = new List<string>(counts.Count);
            foreach (var pair in counts)
            {
                parts.Add(pair.Key + ":" + pair.Value);
            }

            return string.Join(",", parts);
        }

        private static int ClampShiftedMin(int commandHierarchyShift)
        {
            var shifted = TacticalUnitType.MaxCombat + 1 + commandHierarchyShift;
            if (shifted < 1) return 1;
            if (shifted > 18) return 18;
            return shifted;
        }

        private static void Sort(List<CommandProbe> probes)
        {
            probes.Sort((a, b) =>
            {
                var byTyp = b.RawUnitTyp.CompareTo(a.RawUnitTyp);
                return byTyp != 0 ? byTyp : a.InstanceId.CompareTo(b.InstanceId);
            });
        }

        private static string NodeId(int instanceId) => "node-" + instanceId;
    }
}
