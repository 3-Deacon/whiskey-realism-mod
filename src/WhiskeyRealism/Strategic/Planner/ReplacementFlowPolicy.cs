using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The fallback of the replacement flow (spec C8): when the allocation names no shortfall formation
    // to feed, vanilla's CheckTransferOfUnits (17232) can still be pointed at the main target when the
    // force there is short of what the plan requires against the army believed there. Vanilla's own
    // deficit read its enemy from the omniscient recruitment heatmap; this reads the planner's beliefs,
    // so nothing here knows an army the side has not seen. Pure.
    internal static class ReplacementFlowPolicy
    {
        // A shortfall under a brigade's worth is not worth a transfer; the old plan-target steer used
        // the same 2500 floor for "a token presence".
        public const float MinimumShortfall = 2500f;

        // required less present: the plan's required ratio over the believed enemy at the main target,
        // less the own strength that counts toward coverage within `radius` of it (each formation by its
        // hold point). Positive means the main target is under-supplied.
        internal static float PlanShortfall(TheaterPlan plan, float believedEnemyAtTarget, IReadOnlyList<FormationRecord> own,
                                            float targetX, float targetZ, float radius)
        {
            if (plan == null || !(believedEnemyAtTarget > 0f)) return 0f;
            float required = Math.Max(0f, plan.MainRequiredRatio) * believedEnemyAtTarget;
            float present = 0f;
            float radiusSq = radius * radius;
            if (own != null)
            {
                for (int i = 0; i < own.Count; i++)
                {
                    if (!WlChainRules.CountsTowardCoverage(own[i].Chain)) continue;
                    ReserveRules.HoldPoint(own[i], out float hx, out float hz);
                    if (ReserveRules.DistanceSq(hx, hz, targetX, targetZ) > radiusSq) continue;
                    present += ReserveRules.HoldStrength(own[i]);
                }
            }
            return required - present;
        }

        // The main effort's published gather point (SectorPlan.TargetX/Z of the main sector), false for a
        // plan with no main effort.
        internal static bool TryMainTarget(TheaterPlan plan, out float targetX, out float targetZ)
        {
            targetX = targetZ = 0f;
            var sector = plan?.SectorById(plan.MainSectorId);
            if (sector == null) return false;
            targetX = sector.TargetX;
            targetZ = sector.TargetZ;
            return true;
        }

        // Steer only for a plan with a main effort, an army believed at its target and a shortfall of at
        // least MinimumShortfall. The point is the main target (TryMainTarget).
        internal static bool ShouldSteer(TheaterPlan plan, float believedEnemyAtTarget, float planShortfall, out float targetX, out float targetZ)
        {
            targetX = targetZ = 0f;
            if (!(believedEnemyAtTarget > 0f) || !(planShortfall >= MinimumShortfall)) return false;
            return TryMainTarget(plan, out targetX, out targetZ);
        }
    }
}
