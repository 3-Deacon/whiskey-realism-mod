using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure tree builder for the full nested command hierarchy. Given a flat probe
    /// list (one entry per registered Regiment in the battle, with parent linkage)
    /// and the era's commandhierarchyshift, builds an instanceId-keyed tree map
    /// where each node knows its parent and direct children.
    ///
    /// This generalises DirectChildDiscovery — that file only returns direct
    /// children of the army-root tier (one level deep). Here we keep the full
    /// nested tree so callers can recurse from root to leaf brigades regardless
    /// of intermediate divisions/corps.
    /// </summary>
    public static class TacticalCommandTreeProbe
    {
        public readonly struct ProbeNode
        {
            public ProbeNode(
                int instanceId,
                int unittyp,
                string name,
                bool active,
                int parentInstanceId,
                bool isDirectChild,
                float worldX,
                float worldZ,
                int strengthBucket,
                IReadOnlyList<int> childInstanceIds)
            {
                InstanceId = instanceId;
                UnitTyp = unittyp;
                Name = name ?? string.Empty;
                Active = active;
                ParentInstanceId = parentInstanceId;
                IsDirectChild = isDirectChild;
                WorldX = Sanitize(worldX);
                WorldZ = Sanitize(worldZ);
                StrengthBucket = strengthBucket < 0 ? 0 : strengthBucket;
                ChildInstanceIds = childInstanceIds ?? Array.Empty<int>();
            }

            public int InstanceId { get; }
            public int UnitTyp { get; }
            public string Name { get; }
            public bool Active { get; }
            public int ParentInstanceId { get; }
            public bool IsDirectChild { get; }
            public float WorldX { get; }
            public float WorldZ { get; }
            public int StrengthBucket { get; }
            public IReadOnlyList<int> ChildInstanceIds { get; }

            private static float Sanitize(float value)
            {
                return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
            }
        }

        public readonly struct ExtendedProbe
        {
            public ExtendedProbe(
                int instanceId,
                int unittyp,
                string name,
                bool active,
                int parentInstanceId,
                bool isDirectChild,
                float worldX,
                float worldZ,
                int strengthBucket)
            {
                InstanceId = instanceId;
                UnitTyp = unittyp;
                Name = name ?? string.Empty;
                Active = active;
                ParentInstanceId = parentInstanceId;
                IsDirectChild = isDirectChild;
                WorldX = float.IsNaN(worldX) || float.IsInfinity(worldX) ? 0f : worldX;
                WorldZ = float.IsNaN(worldZ) || float.IsInfinity(worldZ) ? 0f : worldZ;
                StrengthBucket = strengthBucket < 0 ? 0 : strengthBucket;
            }

            public int InstanceId { get; }
            public int UnitTyp { get; }
            public string Name { get; }
            public bool Active { get; }
            public int ParentInstanceId { get; }
            public bool IsDirectChild { get; }
            public float WorldX { get; }
            public float WorldZ { get; }
            public int StrengthBucket { get; }
        }

        /// <summary>
        /// Builds a tree map keyed by instanceId. Inactive probes are still
        /// included as nodes (their parent linkage may still matter for tree
        /// shape) but isolated unreachable nodes are not pruned — callers
        /// filter on Active themselves.
        /// </summary>
        public static IReadOnlyDictionary<int, ProbeNode> BuildTree(
            IReadOnlyList<ExtendedProbe> probes,
            int commandHierarchyShift)
        {
            if (probes == null || probes.Count == 0)
                return new Dictionary<int, ProbeNode>();

            // First pass: collect child instanceIds per parent.
            var childrenByParent = new Dictionary<int, List<int>>();
            for (int i = 0; i < probes.Count; i++)
            {
                var p = probes[i];
                if (p.ParentInstanceId == 0 || p.ParentInstanceId == p.InstanceId) continue;
                if (!childrenByParent.TryGetValue(p.ParentInstanceId, out var bucket))
                {
                    bucket = new List<int>();
                    childrenByParent[p.ParentInstanceId] = bucket;
                }
                bucket.Add(p.InstanceId);
            }

            // Second pass: build typed nodes with their child list embedded.
            var tree = new Dictionary<int, ProbeNode>(probes.Count);
            for (int i = 0; i < probes.Count; i++)
            {
                var p = probes[i];
                IReadOnlyList<int> children = childrenByParent.TryGetValue(p.InstanceId, out var bucket)
                    ? bucket
                    : (IReadOnlyList<int>)Array.Empty<int>();
                tree[p.InstanceId] = new ProbeNode(
                    p.InstanceId,
                    p.UnitTyp,
                    p.Name,
                    p.Active,
                    p.ParentInstanceId,
                    p.IsDirectChild,
                    p.WorldX,
                    p.WorldZ,
                    p.StrengthBucket,
                    children);
            }

            return tree;
        }

        /// <summary>
        /// Walks the tree from the given root and returns active leaf-tier nodes.
        /// A "leaf" is a node whose unittyp is at or below MaxCombat + 1 (typically
        /// brigade tier = 14). Pure: takes the tree map, returns descendant leaves.
        /// </summary>
        public static IReadOnlyList<ProbeNode> EnumerateLeaves(
            int rootInstanceId,
            IReadOnlyDictionary<int, ProbeNode> tree)
        {
            var leaves = new List<ProbeNode>();
            if (tree == null || !tree.TryGetValue(rootInstanceId, out var root)) return leaves;
            CollectLeaves(root, tree, leaves);
            return leaves;
        }

        /// <summary>
        /// Returns direct active children of the parent node, sorted by world X
        /// (left to right) so the role-cascade can assign Main/SupportMain/Refuse
        /// based on geometric position.
        /// </summary>
        public static IReadOnlyList<ProbeNode> EnumerateChildrenLeftToRight(
            int parentInstanceId,
            IReadOnlyDictionary<int, ProbeNode> tree)
        {
            if (tree == null || !tree.TryGetValue(parentInstanceId, out var parent))
                return Array.Empty<ProbeNode>();

            var children = new List<ProbeNode>(parent.ChildInstanceIds.Count);
            for (int i = 0; i < parent.ChildInstanceIds.Count; i++)
            {
                if (!tree.TryGetValue(parent.ChildInstanceIds[i], out var child)) continue;
                if (!child.Active) continue;
                children.Add(child);
            }
            children.Sort(CompareByWorldXAscending);
            return children;
        }

        private static void CollectLeaves(
            ProbeNode node,
            IReadOnlyDictionary<int, ProbeNode> tree,
            List<ProbeNode> accumulator)
        {
            if (!node.Active) return;
            // Treat unittyp <= TacticalUnitType.MaxCombat as a non-command unit (artillery,
            // skirmisher, infantry, etc.) — those are leaves in the command hierarchy.
            // Above that we treat as command-tier; recurse if it has children, else
            // consider it a leaf itself (small division with no nested brigades).
            if (node.ChildInstanceIds.Count == 0)
            {
                accumulator.Add(node);
                return;
            }

            bool hasActiveCommandChild = false;
            for (int i = 0; i < node.ChildInstanceIds.Count; i++)
            {
                if (!tree.TryGetValue(node.ChildInstanceIds[i], out var child)) continue;
                if (!child.Active) continue;
                hasActiveCommandChild = true;
                CollectLeaves(child, tree, accumulator);
            }
            // If a node has no active children, it is itself a leaf.
            if (!hasActiveCommandChild) accumulator.Add(node);
        }

        private static int CompareByWorldXAscending(ProbeNode a, ProbeNode b)
        {
            return a.WorldX.CompareTo(b.WorldX);
        }
    }
}
