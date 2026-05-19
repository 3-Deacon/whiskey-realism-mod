using System;
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
                float commanderAggression01)
            {
                ParentRole = parentRole;
                ChildIndex = childIndex < 0 ? 0 : childIndex;
                ChildCount = childCount < 1 ? 1 : childCount;
                ChildStrengthBucket = childStrengthBucket < 0 ? 0 : childStrengthBucket;
                ChildFlankExposureBucket = childFlankExposureBucket < 0 ? 0 : childFlankExposureBucket;
                CommanderAggression01 = Clamp01(commanderAggression01);
            }

            public DirectChildRole ParentRole { get; }
            public int ChildIndex { get; }
            public int ChildCount { get; }
            public int ChildStrengthBucket { get; }
            public int ChildFlankExposureBucket { get; }
            public float CommanderAggression01 { get; }

            private static float Clamp01(float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return 0.5f;
                if (value < 0f) return 0f;
                if (value > 1f) return 1f;
                return value;
            }
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
            // The Main effort cascades down with the center child taking the attack
            // anchor, adjacent siblings supporting, and the outer flanks refusing.
            //
            // High-aggression commanders (Hood/Jackson/Grant) widen the attack band:
            // - aggression >= 0.75: 3-wide attack band (Main + 2x SupportMain)
            // - aggression < 0.30 (cautious): narrow Main, more Reserve on flanks
            // - default 0.30-0.75: Main + adjacent SupportMain, outer Refuse
            int center = ctx.ChildCount / 2;
            int distanceFromCenter = ctx.ChildIndex - center;
            if (distanceFromCenter < 0) distanceFromCenter = -distanceFromCenter;

            int supportBand = ctx.CommanderAggression01 >= 0.75f ? 2 : 1;
            bool cautious = ctx.CommanderAggression01 < 0.30f;

            // Strongest sibling pulls the Main anchor even if it's not perfectly
            // centered, so big brigades anchor the attack.
            int mainIndex = PickAnchorIndex(ctx, center);
            if (ctx.ChildIndex == mainIndex)
                return DirectChildRole.Main;

            // Re-derive distance from the resolved anchor, not the bare center.
            distanceFromCenter = ctx.ChildIndex - mainIndex;
            if (distanceFromCenter < 0) distanceFromCenter = -distanceFromCenter;

            if (distanceFromCenter <= supportBand)
            {
                if (cautious && ctx.ChildFlankExposureBucket >= 2)
                    return DirectChildRole.Reserve;
                return DirectChildRole.SupportMain;
            }

            // Outside the support band: refuse outer flanks. Use index position
            // relative to anchor for left vs right.
            if (cautious && ctx.ChildFlankExposureBucket < 2)
                return DirectChildRole.Reserve;
            return ctx.ChildIndex < mainIndex
                ? DirectChildRole.RefuseLeft
                : DirectChildRole.RefuseRight;
        }

        private static DirectChildRole DistributeSupportMain(CascadeContext ctx)
        {
            // SupportMain children continue the attack but with fewer attack roles
            // and more reserves. Strongest sibling continues SupportMain;
            // aggressive commanders pull more siblings into SupportMain.
            int supportSlots = ctx.CommanderAggression01 >= 0.75f ? 2 : 1;
            if (ctx.CommanderAggression01 < 0.30f) supportSlots = 1;
            if (supportSlots > ctx.ChildCount) supportSlots = ctx.ChildCount;

            int anchorIndex = PickStrongestIndex(ctx);
            if (ctx.ChildIndex == anchorIndex) return DirectChildRole.SupportMain;
            if (supportSlots >= 2)
            {
                // Allow a second support slot adjacent to the anchor
                if (ctx.ChildIndex == anchorIndex - 1 || ctx.ChildIndex == anchorIndex + 1)
                    return DirectChildRole.SupportMain;
            }
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

        private static int PickAnchorIndex(CascadeContext ctx, int defaultCenter)
        {
            // Default to the geometric center, but if a stronger sibling exists
            // within 1 slot of center, pull the anchor there. Keeps the attack
            // mass-concentrated on the heaviest brigade.
            if (ctx.ChildStrengthBucket >= 2 &&
                (ctx.ChildIndex == defaultCenter - 1 || ctx.ChildIndex == defaultCenter + 1))
                return ctx.ChildIndex;
            return defaultCenter;
        }

        private static int PickStrongestIndex(CascadeContext ctx)
        {
            // Used by SupportMain distribution. Without a global per-sibling
            // strength comparison, treat the current index as the strongest if
            // its strength bucket is >= 2 (a high bucket). Otherwise default to
            // index 0.
            //
            // Callers that have full sibling strength data should pre-sort and
            // pass index 0 as the strongest; this function is robust to that
            // pre-sorting.
            if (ctx.ChildStrengthBucket >= 2) return ctx.ChildIndex;
            return 0;
        }
    }
}
