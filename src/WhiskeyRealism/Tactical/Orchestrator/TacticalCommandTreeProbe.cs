using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;

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
        /// A "leaf" for the role cascade is a brigade-tier node (unittyp ==
        /// BattleGroupBrigade = 14), NOT a regiment-tier combat unit below it.
        /// Brigades are the SoW-equivalent of `eRankBrig` — the tier orders are
        /// issued to; the regiments below them (Infantry/Cavalry/Artillery/
        /// Skirmisher unittyps 0-3) inherit orders from their brigade via
        /// vanilla.
        ///
        /// If a non-brigade node has no active brigade-or-above descendants we
        /// also consider it a leaf, so degenerate hierarchies (a lone corps with
        /// no brigades) still produce assignments.
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
        /// based on geometric position. Filters out combat-unit children
        /// (unittyp &lt; BattleGroupBrigade) — those are regiments inside
        /// brigades and don't participate in the command-tier cascade.
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
                // Skip non-command children (regiments, officers, etc.) so the
                // cascade only walks brigade-and-above tiers.
                if (child.UnitTyp < TacticalUnitType.BattleGroupBrigade) continue;
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

            // Stop at brigade tier. The cascade issues orders to brigades; their
            // regiment children inherit via vanilla. Without this, the cascade
            // would recurse through Regiment → Infantry/Cavalry/Artillery and
            // assign roles to the wrong tier — the leaf map would then be
            // keyed by combat-unit instance IDs, and the brigade-tier filter
            // in TryApplyNestedLeafBrigades would never find a match.
            if (node.UnitTyp == TacticalUnitType.BattleGroupBrigade)
            {
                accumulator.Add(node);
                return;
            }

            // Combat units (Infantry/Cavalry/Artillery/Skirmisher/Officer) are
            // not part of the command-tier cascade; skip entirely. The brigade
            // containing them is the leaf.
            if (node.UnitTyp < TacticalUnitType.BattleGroupBrigade) return;

            // Above brigade tier (Division/Corps/Army): recurse into brigade-
            // tier-or-above children.
            bool hasActiveCommandChild = false;
            for (int i = 0; i < node.ChildInstanceIds.Count; i++)
            {
                if (!tree.TryGetValue(node.ChildInstanceIds[i], out var child)) continue;
                if (!child.Active) continue;
                if (child.UnitTyp < TacticalUnitType.BattleGroupBrigade) continue;
                hasActiveCommandChild = true;
                CollectLeaves(child, tree, accumulator);
            }

            // If a Division/Corps/Army has no active brigade descendants, treat
            // it as a leaf so degenerate hierarchies still produce one assignment
            // (better than silently dropping the whole branch).
            if (!hasActiveCommandChild) accumulator.Add(node);
        }

        private static int CompareByWorldXAscending(ProbeNode a, ProbeNode b)
        {
            return a.WorldX.CompareTo(b.WorldX);
        }
    }
}
