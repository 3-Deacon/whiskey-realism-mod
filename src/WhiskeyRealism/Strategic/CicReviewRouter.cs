namespace WhiskeyRealism.Strategic
{
    // Pure routing logic: given a PhaseTruthOutput, decide what ReviewPlanWithTruth should do
    // to the plan. No BepInEx / Unity / reflection dependencies — safe to compile in tests.
    //
    // CIC.ReviewPlanWithTruth delegates here; callers that don't need CIC can call RouteAction
    // directly (e.g. the test harness).
    internal static class CicReviewRouter
    {
        // Returns true when the plan is still valid (Continue → ReviewPlan baseline, or Advance
        // succeeds). Returns false when the plan needs replanning (dirty or no next phase).
        internal static bool RouteAction(
            OperationalPlan plan,
            PhaseTruthOutput truth,
            int currentMonth,
            int currentYear)
        {
            if (plan == null) return false;
            if (truth == null) return ReviewPlanBaseline(plan, currentMonth, currentYear);

            switch (truth.RecommendedAction)
            {
                case PhaseTruthAction.Advance:
                    return AdvancePhase(plan);

                case PhaseTruthAction.Complete:
                    plan.IsDirty = true;
                    return false;

                case PhaseTruthAction.Pause:
                    ApplyOperationPosture(plan, OperationPosture.ScreenAndDelay, allowAttack: false, allowReinforce: true, probeOnly: true);
                    return true;
                case PhaseTruthAction.Exploit:
                    ApplyOperationPosture(plan, OperationPosture.ExploitBreakthrough, allowAttack: true, allowReinforce: true, probeOnly: false);
                    return true;
                case PhaseTruthAction.Counterstroke:
                    ApplyOperationPosture(plan, OperationPosture.Counterstroke, allowAttack: true, allowReinforce: true, probeOnly: false);
                    return true;
                case PhaseTruthAction.ScreenAndDelay:
                    ApplyOperationPosture(plan, OperationPosture.ScreenAndDelay, allowAttack: false, allowReinforce: true, probeOnly: true);
                    return true;
                case PhaseTruthAction.Recover:
                    ApplyOperationPosture(plan, OperationPosture.Recover, allowAttack: false, allowReinforce: true, probeOnly: true);
                    return true;

                case PhaseTruthAction.Abort:
                    plan.IsDirty = true;
                    return false;

                case PhaseTruthAction.Pivot:
                    plan.PendingRetarget = true;
                    plan.PendingRetargetReason = truth.AlternateOperationId;
                    plan.IsDirty = true;
                    return false;
                case PhaseTruthAction.Replan:
                    plan.IsDirty = true;
                    return false;

                case PhaseTruthAction.Continue:
                default:
                    return ReviewPlanBaseline(plan, currentMonth, currentYear);
            }
        }

        // Mirrors the deadline/phase logic of CIC.ReviewPlan, operating on the plan directly.
        // Only called for the Continue/default branch so CIC.ReviewPlanWithTruth can fall through
        // to the same deadline checks without duplicating them here.
        // NOTE: Callers that go through CIC.ReviewPlanWithTruth will use ReviewPlan() on the CIC
        // instance rather than this helper, so this is here only for the test harness path.
        internal static bool ReviewPlanBaseline(OperationalPlan plan, int currentMonth, int currentYear)
        {
            if (plan.IsDirty) return false;

            if (currentYear > plan.PlanDeadlineYear ||
               (currentYear == plan.PlanDeadlineYear && currentMonth > plan.PlanDeadlineMonth))
                return false;

            var p = plan.CurrentPhase;
            if (p != null && (currentYear > p.DeadlineYear ||
                             (currentYear == p.DeadlineYear && currentMonth > p.DeadlineMonth)))
            {
                return AdvancePhase(plan);
            }

            return true;
        }

        // Advances the plan's current phase index. If no next phase exists, marks the plan
        // dirty and returns false (signals replan). Does NOT log — no Plugin dependency.
        internal static bool AdvancePhase(OperationalPlan plan)
        {
            if (plan == null) return false;
            if (plan.CurrentPhaseIndex + 1 < plan.Phases.Count)
            {
                plan.CurrentPhaseIndex++;
                return true;
            }
            plan.IsDirty = true;
            return false;
        }

        private static void ApplyOperationPosture(
            OperationalPlan plan,
            OperationPosture posture,
            bool allowAttack,
            bool allowReinforce,
            bool probeOnly)
        {
            if (plan == null) return;
            plan.OperationPosture = posture;
            plan.PendingRetarget = false;
            var phase = plan.CurrentPhase;
            if (phase == null) return;
            phase.OperationPosture = posture;
            phase.AllowCoordinatedAttack = allowAttack;
            phase.AllowReinforcementPackage = allowReinforce;
            phase.AllowProbeOnly = probeOnly;
        }
    }
}
