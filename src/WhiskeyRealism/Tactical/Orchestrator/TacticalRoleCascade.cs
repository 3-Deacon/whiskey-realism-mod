using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure logic that distributes a parent's role to one of its children based on
    /// the child's index, sibling count, geometric position from parent center,
    /// strength bucket, flank-exposure bucket, and the commander's aggression.
    ///
    /// Hierarchy-depth-agnostic: callers recursively apply this to descend an
    /// arbitrarily deep command tree, propagating roles from the top tier down
    /// to leaf brigades. SoW's offai.cpp dispatch only handles its rigid 5-tier
    /// hierarchy; this function does not care how deep the tree is.
    ///
    /// Rules summary:
    /// - Main on a parent: center child = Main (attack anchor), adjacent = SupportMain,
    ///   outer = RefuseLeft/Right by position. High-aggression commanders widen the
    ///   Main+SupportMain band so more brigades attack.
    /// - SupportMain on a parent: strongest = SupportMain, others = Reserve. Aggressive
    ///   commanders keep more brigades on SupportMain duty.
    /// - Fix: all children Fix (continue fixing the enemy in this sector).
    /// - Screen: center = Screen, outer = Probe (forward scouts).
    /// - RefuseLeft/Right: outermost flank child stays on the flank role; inner
    ///   children become Reserve (echelon depth).
    /// - Reserve / Fallback / Unknown: cascade unchanged.
    /// </summary>
    public static class TacticalRoleCascade
    {
        public readonly struct CascadeContext
        {
            public CascadeContext(
                DirectChildRole parentRole,
                int childIndex,
                int childCount,
                int childStrengthBucket,
                int childFlankExposureBucket,
                float commanderAggression01,
                int anchorIndex = -1)
            {
                ParentRole = parentRole;
                ChildIndex = childIndex < 0 ? 0 : childIndex;
                ChildCount = childCount < 1 ? 1 : childCount;
                ChildStrengthBucket = childStrengthBucket < 0 ? 0 : childStrengthBucket;
                ChildFlankExposureBucket = childFlankExposureBucket < 0 ? 0 : childFlankExposureBucket;
                CommanderAggression01 = Clamp01(commanderAggression01);
                // anchorIndex = -1 means "use geometric center" (childCount / 2).
                // Callers that have global sibling-strength data should pre-compute
                // the anchor once and pass it here so every sibling sees the same
                // anchor — preventing multi-self-anchor allocations.
                AnchorIndex = anchorIndex;
            }

            public DirectChildRole ParentRole { get; }
            public int ChildIndex { get; }
            public int ChildCount { get; }
            public int ChildStrengthBucket { get; }
            public int ChildFlankExposureBucket { get; }
            public float CommanderAggression01 { get; }
            public int AnchorIndex { get; }

            public int ResolvedAnchorIndex
            {
                get
                {
                    if (AnchorIndex >= 0 && AnchorIndex < ChildCount) return AnchorIndex;
                    return ChildCount / 2;
                }
            }

            private static float Clamp01(float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return 0.5f;
                if (value < 0f) return 0f;
                if (value > 1f) return 1f;
                return value;
            }
        }

        /// <summary>
        /// Caller-side helper: pre-pick the Main anchor index given each sibling's
        /// strength bucket. Picks the center child by default; if a sibling with
        /// higher strength sits within `nearCenterRadius` of center, the anchor
        /// shifts there. Always returns a single index — no ambiguity between
        /// per-child views of who's the anchor.
        /// </summary>
        public static int ChooseMainAnchorIndex(IReadOnlyList<int> siblingStrengthBuckets, int nearCenterRadius = 1)
        {
            if (siblingStrengthBuckets == null || siblingStrengthBuckets.Count <= 0) return 0;
            int count = siblingStrengthBuckets.Count;
            int center = count / 2;
            int bestIndex = center;
            int bestStrength = (center >= 0 && center < count) ? siblingStrengthBuckets[center] : -1;
            for (int i = 0; i < count; i++)
            {
                int dist = i - center; if (dist < 0) dist = -dist;
                if (dist > nearCenterRadius) continue;
                if (siblingStrengthBuckets[i] > bestStrength)
                {
                    bestStrength = siblingStrengthBuckets[i];
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        public static DirectChildRole DistributeChildRole(CascadeContext ctx)
        {
            // Sentinel cases short-circuit to the same role with no distribution.
            if (ctx.ChildCount <= 1)
                return CollapseSingleChild(ctx.ParentRole);

            switch (ctx.ParentRole)
            {
                case DirectChildRole.Main:
                    return DistributeMain(ctx);
                case DirectChildRole.SupportMain:
                    return DistributeSupportMain(ctx);
                case DirectChildRole.Fix:
                    return DirectChildRole.Fix;
                case DirectChildRole.Screen:
                    return DistributeScreen(ctx);
                case DirectChildRole.RefuseLeft:
                    return DistributeRefuseLeft(ctx);
                case DirectChildRole.RefuseRight:
                    return DistributeRefuseRight(ctx);
                case DirectChildRole.Reserve:
                    return DirectChildRole.Reserve;
                case DirectChildRole.Fallback:
                    return DirectChildRole.Fallback;
                case DirectChildRole.Unknown:
                default:
                    return DirectChildRole.Unknown;
            }
        }

        /// <summary>
        /// Maps a leaf brigade role to its CommandTaskType. This is the cascade's
        /// task contract — what the posture executor will actually try to execute
        /// at the leaf brigade tier.
        /// </summary>
        public static CommandTaskType RoleToLeafTask(DirectChildRole role)
        {
            switch (role)
            {
                case DirectChildRole.Main:        return CommandTaskType.AttackObjective;
                case DirectChildRole.SupportMain: return CommandTaskType.SupportAttack;
                case DirectChildRole.Fix:         return CommandTaskType.FixEnemy;
                case DirectChildRole.Screen:      return CommandTaskType.Screen;
                case DirectChildRole.RefuseLeft:  return CommandTaskType.GuardFlank;
                case DirectChildRole.RefuseRight: return CommandTaskType.GuardFlank;
                case DirectChildRole.Reserve:     return CommandTaskType.ReserveWait;
                case DirectChildRole.Fallback:    return CommandTaskType.FallBackToLine;
                case DirectChildRole.Unknown:
                default:                          return CommandTaskType.None;
            }
        }

        // ---- Per-role distribution helpers ----

        private static DirectChildRole DistributeMain(CascadeContext ctx)
        {
            // The Main effort cascades down with one anchor child taking Main,
            // adjacent siblings supporting, and outer flanks refusing.
            //
            // High-aggression commanders widen the attack band: aggression >= 0.75 = 2 slots
            // each side of anchor; < 0.30 (cautious) narrows the support band to 1 slot
            // and outer children fall back to Reserve instead of Refuse.
            //
            // The anchor is supplied by the caller via ctx.AnchorIndex (or defaults to
            // geometric center). This avoids the multi-self-anchor bug where two
            // strong adjacent children both classified themselves as the anchor.
            int anchorIndex = ctx.ResolvedAnchorIndex;
            int supportBand = ctx.CommanderAggression01 >= 0.75f ? 2 : 1;
            bool cautious = ctx.CommanderAggression01 < 0.30f;

            if (ctx.ChildIndex == anchorIndex)
                return DirectChildRole.Main;

            int distanceFromAnchor = ctx.ChildIndex - anchorIndex;
            if (distanceFromAnchor < 0) distanceFromAnchor = -distanceFromAnchor;

            if (distanceFromAnchor <= supportBand)
            {
                if (cautious && ctx.ChildFlankExposureBucket >= 2)
                    return DirectChildRole.Reserve;
                return DirectChildRole.SupportMain;
            }

            // Outside the support band: refuse outer flanks. Use index position
            // relative to anchor for left vs right.
            if (cautious && ctx.ChildFlankExposureBucket < 2)
                return DirectChildRole.Reserve;
            return ctx.ChildIndex < anchorIndex
                ? DirectChildRole.RefuseLeft
                : DirectChildRole.RefuseRight;
        }

        private static DirectChildRole DistributeSupportMain(CascadeContext ctx)
        {
            // SupportMain children continue the attack but with fewer attack roles
            // and more reserves. Single anchor — caller-supplied via ctx.AnchorIndex
            // — continues SupportMain; aggressive commanders allow one adjacent
            // sibling to also be SupportMain.
            int anchorIndex = ctx.ResolvedAnchorIndex;
            int supportBand = ctx.CommanderAggression01 >= 0.75f ? 1 : 0;

            if (ctx.ChildIndex == anchorIndex) return DirectChildRole.SupportMain;
            int distance = ctx.ChildIndex - anchorIndex;
            if (distance < 0) distance = -distance;
            if (distance <= supportBand) return DirectChildRole.SupportMain;
            return DirectChildRole.Reserve;
        }

        private static DirectChildRole DistributeScreen(CascadeContext ctx)
        {
            // Screen role: center child keeps Screen, outer children push forward
            // as Probe (forward scouts). Aggressive commanders push more probes;
            // cautious commanders keep more in Screen.
            int center = ctx.ChildCount / 2;
            int distanceFromCenter = ctx.ChildIndex - center;
            if (distanceFromCenter < 0) distanceFromCenter = -distanceFromCenter;

            // Strongest sibling probes forward (it can survive contact best);
            // weaker siblings stay in screen line.
            if (ctx.CommanderAggression01 >= 0.60f && ctx.ChildStrengthBucket >= 1)
                return DirectChildRole.Screen;  // re-map Probe → Screen for compatibility with leaf tasks

            return distanceFromCenter == 0 ? DirectChildRole.Screen : DirectChildRole.Screen;
        }

        private static DirectChildRole DistributeRefuseLeft(CascadeContext ctx)
        {
            // Refuse-left echelon: leftmost (lowest index) anchors the flank;
            // inner siblings stay Reserve behind the refused line.
            if (ctx.ChildIndex == 0) return DirectChildRole.RefuseLeft;
            return DirectChildRole.Reserve;
        }

        private static DirectChildRole DistributeRefuseRight(CascadeContext ctx)
        {
            // Refuse-right echelon: rightmost (highest index) anchors the flank;
            // inner siblings stay Reserve behind the refused line.
            if (ctx.ChildIndex == ctx.ChildCount - 1) return DirectChildRole.RefuseRight;
            return DirectChildRole.Reserve;
        }

        private static DirectChildRole CollapseSingleChild(DirectChildRole parentRole)
        {
            // Single-child cascade: child inherits the parent role directly. No
            // distribution to do; the parent's role IS the child's role.
            return parentRole;
        }

    }
}
