using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;

// WS C1 — campaign plan lifecycle, catalog, persistence regression tests.
internal static class CampaignLifecycleFixTests
{
    public static void Run()
    {
        Check("c1 cic runtime review abort clears plan and matches router", CicRuntimeReviewAbortClearsPlanAndMatchesRouter);
        Check("c1 cic runtime review replan clears pending retarget", CicRuntimeReviewReplanClearsPendingRetarget);
        Check("c1 cic runtime review posture stamps decision day", CicRuntimeReviewPostureStampsDecisionDay);
    }

    private static void Check(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    // ------------------------------------------------------------------
    // Shared builders
    // ------------------------------------------------------------------

    internal static OperationalPlan SinglePhasePlan(int deadlineMonth = 12, int deadlineYear = 1862)
    {
        var plan = new OperationalPlan
        {
            CurrentPhaseIndex = 0,
            PlanDeadlineMonth = 12,
            PlanDeadlineYear = 1865
        };
        plan.Phases.Add(new Phase
        {
            PhaseId = "p0",
            TargetObjectiveId = 3,
            ForceFractionRequired = 0.5f,
            Transition = PhaseTransition.TargetTaken,
            DeadlineMonth = deadlineMonth,
            DeadlineYear = deadlineYear,
            OperationPosture = OperationPosture.ProbeAndDevelop,
            AllowCoordinatedAttack = true,
            AllowReinforcementPackage = true
        });
        return plan;
    }

    private static PhaseTruthOutput Truth(PhaseTruthAction action, PhaseTruthVerdict verdict = PhaseTruthVerdict.Valid, string reason = "test")
    {
        return new PhaseTruthOutput { RecommendedAction = action, Verdict = verdict, Reason = reason };
    }

    // ------------------------------------------------------------------
    // CIC router drift: CIC.ReviewPlanWithTruth delegates to CicReviewRouter
    // ------------------------------------------------------------------

    private static void CicRuntimeReviewAbortClearsPlanAndMatchesRouter()
    {
        var cic = new CIC { AllianceId = 0, ActivePlan = SinglePhasePlan() };
        bool keep = cic.ReviewPlanWithTruth(1, 6, 1862, Truth(PhaseTruthAction.Abort));
        TestHarness.AssertFalse(keep, "abort must not keep the plan");
        TestHarness.AssertTrue(cic.ActivePlan == null, "abort clears the CIC plan");

        var plan = SinglePhasePlan();
        var outcome = CicReviewRouter.Route(plan, Truth(PhaseTruthAction.Abort), 1862 * 372 + 6 * 31 + 1, 6, 1862);
        TestHarness.AssertEqual(CicRouteOutcome.AbandonPlan, outcome, "router abort outcome");
    }

    private static void CicRuntimeReviewReplanClearsPendingRetarget()
    {
        var plan = SinglePhasePlan();
        plan.PendingRetarget = true;
        plan.PendingRetargetReason = "old";
        var outcome = CicReviewRouter.Route(plan, Truth(PhaseTruthAction.Replan), 1862 * 372 + 6 * 31 + 1, 6, 1862);
        TestHarness.AssertEqual(CicRouteOutcome.Replan, outcome, "replan outcome");
        TestHarness.AssertTrue(plan.IsDirty, "replan marks dirty");
        TestHarness.AssertFalse(plan.PendingRetarget, "replan clears pending retarget");
        TestHarness.AssertTrue(plan.PendingRetargetReason == null, "replan clears retarget reason");
    }

    private static void CicRuntimeReviewPostureStampsDecisionDay()
    {
        var cic = new CIC { AllianceId = 0, ActivePlan = SinglePhasePlan() };
        bool keep = cic.ReviewPlanWithTruth(14, 6, 1862, Truth(PhaseTruthAction.ScreenAndDelay, reason: "empty-target"));
        TestHarness.AssertTrue(keep, "posture action keeps plan");
        TestHarness.AssertEqual(1862 * 372 + 6 * 31 + 14, cic.ActivePlan.OperationLastDecisionDaySerial, "decision day serial");
        TestHarness.AssertEqual(OperationPosture.ScreenAndDelay, cic.ActivePlan.CurrentPhase.OperationPosture, "phase posture");
        TestHarness.AssertEqual("empty-target", cic.ActivePlan.PendingRetargetReason, "decision reason recorded");
    }
}
