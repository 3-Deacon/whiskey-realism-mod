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

        // R3 — Recover deadlock
        Check("c1 r3 deadline expiry advances while force short", R3DeadlineExpiryAdvancesWhileForceShort);
        Check("c1 r3 deadline expiry on last phase replans while force short", R3DeadlineExpiryOnLastPhaseReplansWhileForceShort);
        Check("c1 r3 force short before deadline recovers", R3ForceShortBeforeDeadlineRecovers);
        Check("c1 r3 required ratio is local commitment share not theater f/(1-f)", R3RequiredRatioIsLocalCommitmentShare);
        Check("c1 r3 no local enemy is never force short", R3NoLocalEnemyIsNeverForceShort);
        Check("c1 r3 recover posture still advances on expired phase deadline", R3RecoverPostureStillAdvancesOnExpiredDeadline);
        Check("c1 r3 recover clears to template flags when ratio recovers", R3RecoverClearsToTemplateFlagsWhenRatioRecovers);
        Check("c1 r3 recover clears to generic flags for legacy plans", R3RecoverClearsToGenericFlagsForLegacyPlans);
        Check("c1 r3 target-engaged transition advances on engagement after phase start", R3TargetEngagedTransitionAdvancesOnNewEngagement);
        Check("c1 r3 target-engaged transition ignores engagement before phase start", R3TargetEngagedTransitionIgnoresOldEngagement);
        Check("c1 r3 hold phase is not force gated and advances when force ready", R3HoldPhaseAdvancesWhenForceReady);

        // R4 — battle resets
        Check("c1 r4 minor out-of-theater battle does not dirty plan", R4MinorOutOfTheaterBattleDoesNotDirtyPlan);
        Check("c1 r4 minor battle at target does not dirty plan", R4MinorBattleAtTargetDoesNotDirtyPlan);
        Check("c1 r4 major battle elsewhere does not dirty plan", R4MajorBattleElsewhereDoesNotDirtyPlan);
        Check("c1 r4 major battle in target theater dirties plan", R4MajorBattleInTargetTheaterDirtiesPlan);
        Check("c1 r4 major battle near target dirties plan", R4MajorBattleNearTargetDirtiesPlan);
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

    private static int Serial(int day, int month, int year) => year * 372 + month * 31 + day;

    private static PhaseTruthInput TruthInput(OperationalPlan plan, int month, int year, float own, float enemy)
    {
        return new PhaseTruthInput
        {
            Plan = plan,
            AllianceId = 0,
            DaySerial = Serial(1, month, year),
            ObjectiveAvailable = true,
            TargetPositionResolves = true,
            TargetSectorOwnStrength = own,
            TargetSectorEnemyStrength = enemy,
            LastTargetEngagementDaySerial = -1,
            CurrentMonth = month,
            CurrentYear = year
        };
    }

    private static OperationalPlan TwoPhasePlan(float fraction)
    {
        var plan = SinglePhasePlan(deadlineMonth: 3, deadlineYear: 1862);
        plan.Phases[0].ForceFractionRequired = fraction;
        plan.Phases.Add(new Phase
        {
            PhaseId = "p1",
            TargetObjectiveId = 3,
            ForceFractionRequired = fraction,
            Transition = PhaseTransition.TargetTaken,
            DeadlineMonth = 9,
            DeadlineYear = 1862
        });
        return plan;
    }

    // ------------------------------------------------------------------
    // R3
    // ------------------------------------------------------------------

    private static void R3DeadlineExpiryAdvancesWhileForceShort()
    {
        var plan = TwoPhasePlan(0.8f);
        var output = PhaseTruthLedger.Evaluate(TruthInput(plan, 5, 1862, own: 1000f, enemy: 10000f));
        TestHarness.AssertEqual(PhaseTruthVerdict.DeadlineExpired, output.Verdict, "deadline evaluated before force");
        TestHarness.AssertEqual(PhaseTruthAction.Advance, output.RecommendedAction, "deadline advances");
    }

    private static void R3DeadlineExpiryOnLastPhaseReplansWhileForceShort()
    {
        var plan = SinglePhasePlan(deadlineMonth: 3, deadlineYear: 1862);
        plan.Phases[0].ForceFractionRequired = 0.8f;
        var output = PhaseTruthLedger.Evaluate(TruthInput(plan, 5, 1862, own: 1000f, enemy: 10000f));
        TestHarness.AssertEqual(PhaseTruthAction.Replan, output.RecommendedAction, "expired last phase replans");
    }

    private static void R3ForceShortBeforeDeadlineRecovers()
    {
        var plan = TwoPhasePlan(0.8f);
        var output = PhaseTruthLedger.Evaluate(TruthInput(plan, 2, 1862, own: 1000f, enemy: 10000f));
        TestHarness.AssertEqual(PhaseTruthVerdict.ForceBelowThreshold, output.Verdict, "force short verdict");
        TestHarness.AssertEqual(PhaseTruthAction.Recover, output.RecommendedAction, "force short recovers");
    }

    private static void R3RequiredRatioIsLocalCommitmentShare()
    {
        // 0.8 used to mean own >= 0.8 x (own+enemy) theater-wide = 4:1. Now: own >= (0.5+f) x local enemy.
        TestHarness.AssertTrue(Math.Abs(PhaseTruthLedger.RequiredLocalRatio(0.8f) - 1.3f) < 0.001f, "0.8 -> 1.3:1");
        var plan = TwoPhasePlan(0.8f);
        var ok = PhaseTruthLedger.Evaluate(TruthInput(plan, 2, 1862, own: 13000f, enemy: 10000f));
        TestHarness.AssertEqual(PhaseTruthAction.Continue, ok.RecommendedAction, "1.3:1 local satisfies a 0.8 phase");
        var shortOutput = PhaseTruthLedger.Evaluate(TruthInput(plan, 2, 1862, own: 12000f, enemy: 10000f));
        TestHarness.AssertEqual(PhaseTruthAction.Recover, shortOutput.RecommendedAction, "1.2:1 is short for a 0.8 phase");
    }

    private static void R3NoLocalEnemyIsNeverForceShort()
    {
        var plan = TwoPhasePlan(0.8f);
        var output = PhaseTruthLedger.Evaluate(TruthInput(plan, 2, 1862, own: 0f, enemy: 0f));
        TestHarness.AssertEqual(PhaseTruthAction.Continue, output.RecommendedAction, "empty target is not force short");
    }

    private static void R3RecoverPostureStillAdvancesOnExpiredDeadline()
    {
        var plan = TwoPhasePlan(0.8f);
        var outcome = CicReviewRouter.Route(plan, Truth(PhaseTruthAction.Recover, PhaseTruthVerdict.ForceBelowThreshold), Serial(1, 5, 1862), 5, 1862);
        TestHarness.AssertEqual(CicRouteOutcome.KeepPlan, outcome, "plan kept");
        TestHarness.AssertEqual(1, plan.CurrentPhaseIndex, "expired phase advanced despite Recover posture");
        TestHarness.AssertEqual(Serial(1, 5, 1862), plan.CurrentPhase.PhaseStartedDaySerial, "new phase start stamped");
    }

    private static void R3RecoverClearsToTemplateFlagsWhenRatioRecovers()
    {
        TestHarness.AssertTrue(HistoricalOperationCatalog.TryGetById("union-east-pressure", out var profile), "profile");
        var template = profile.Phases[1];
        var plan = new OperationalPlan
        {
            OperationId = profile.OperationId,
            OperationPosture = profile.Posture,
            CurrentPhaseIndex = 1,
            PlanDeadlineMonth = 12,
            PlanDeadlineYear = 1861
        };
        plan.Phases.Add(new Phase { PhaseId = profile.Phases[0].PhaseId, TargetObjectiveId = 3, DeadlineMonth = 12, DeadlineYear = 1861 });
        plan.Phases.Add(new Phase { PhaseId = template.PhaseId, TargetObjectiveId = 3, DeadlineMonth = 12, DeadlineYear = 1861 });

        CicReviewRouter.Route(plan, Truth(PhaseTruthAction.Recover, PhaseTruthVerdict.ForceBelowThreshold), Serial(1, 6, 1861), 6, 1861);
        TestHarness.AssertEqual(OperationPosture.Recover, plan.CurrentPhase.OperationPosture, "recover applied");
        TestHarness.AssertFalse(plan.CurrentPhase.AllowCoordinatedAttack, "recover blocks attack");

        CicReviewRouter.Route(plan, Truth(PhaseTruthAction.Continue), Serial(2, 6, 1861), 6, 1861);
        TestHarness.AssertEqual(template.Posture, plan.CurrentPhase.OperationPosture, "template posture restored");
        TestHarness.AssertEqual(template.AllowCoordinatedAttack, plan.CurrentPhase.AllowCoordinatedAttack, "template attack flag restored");
        TestHarness.AssertEqual(template.AllowReinforcementPackage, plan.CurrentPhase.AllowReinforcementPackage, "template reinforce flag restored");
        TestHarness.AssertEqual(template.AllowProbeOnly, plan.CurrentPhase.AllowProbeOnly, "template probe flag restored");
        TestHarness.AssertEqual(profile.Posture, plan.OperationPosture, "plan posture restored");
    }

    private static void R3RecoverClearsToGenericFlagsForLegacyPlans()
    {
        var plan = SinglePhasePlan();
        CicReviewRouter.Route(plan, Truth(PhaseTruthAction.Recover, PhaseTruthVerdict.ForceBelowThreshold), Serial(1, 6, 1862), 6, 1862);
        CicReviewRouter.Route(plan, Truth(PhaseTruthAction.Continue), Serial(2, 6, 1862), 6, 1862);
        TestHarness.AssertEqual(OperationPosture.ProbeAndDevelop, plan.CurrentPhase.OperationPosture, "generic posture restored");
        TestHarness.AssertTrue(plan.CurrentPhase.AllowCoordinatedAttack, "TargetTaken generic phase may attack again");
        TestHarness.AssertFalse(plan.CurrentPhase.AllowProbeOnly, "TargetTaken generic phase is not probe-only");
    }

    private static OperationalPlan EngagedTransitionPlan(int phaseStart)
    {
        var plan = TwoPhasePlan(0.35f);
        plan.Phases[0].Transition = PhaseTransition.TargetEngaged;
        plan.Phases[0].DeadlineMonth = 12;
        plan.Phases[0].PhaseStartedDaySerial = phaseStart;
        plan.Phases[1].DeadlineMonth = 12;
        return plan;
    }

    private static void R3TargetEngagedTransitionAdvancesOnNewEngagement()
    {
        var plan = EngagedTransitionPlan(Serial(1, 5, 1862));
        var input = TruthInput(plan, 6, 1862, own: 10000f, enemy: 10000f);
        input.TargetEngagedRecently = true;
        input.LastTargetEngagementDaySerial = Serial(10, 5, 1862);
        var output = PhaseTruthLedger.Evaluate(input);
        TestHarness.AssertEqual(PhaseTruthAction.Advance, output.RecommendedAction, "engagement after phase start completes a TargetEngaged phase");
    }

    private static void R3TargetEngagedTransitionIgnoresOldEngagement()
    {
        var plan = EngagedTransitionPlan(Serial(10, 5, 1862));
        var input = TruthInput(plan, 6, 1862, own: 10000f, enemy: 10000f);
        input.TargetEngagedRecently = true;
        input.LastTargetEngagementDaySerial = Serial(10, 5, 1862);
        var output = PhaseTruthLedger.Evaluate(input);
        TestHarness.AssertEqual(PhaseTruthAction.Continue, output.RecommendedAction, "same engagement must not cascade into the next phase");
    }

    private static void R3HoldPhaseAdvancesWhenForceReady()
    {
        var plan = TwoPhasePlan(0.65f);
        plan.Phases[0].Transition = PhaseTransition.ForceBelowThreshold;
        var shortOutput = PhaseTruthLedger.Evaluate(TruthInput(plan, 2, 1862, own: 5000f, enemy: 10000f));
        TestHarness.AssertEqual(PhaseTruthAction.Continue, shortOutput.RecommendedAction, "hold phase keeps holding while short");
        var ready = PhaseTruthLedger.Evaluate(TruthInput(plan, 2, 1862, own: 12000f, enemy: 10000f));
        TestHarness.AssertEqual(PhaseTruthAction.Advance, ready.RecommendedAction, "hold phase advances once force requirement met");
    }

    // ------------------------------------------------------------------
    // R4
    // ------------------------------------------------------------------

    private static BattleHistoryRecord Battle(int resultType, float x, float z)
    {
        return new BattleHistoryRecord
        {
            BattleName = "Battle of Test",
            Day = 1, Month = 6, Year = 1862,
            LandOrSea = 0,
            AllianceWon = 0,
            BattleResultType = resultType,
            PositionX = x,
            PositionZ = z,
            Alliance = new[] { 0, 1 }
        };
    }

    private static BattleReplanQuery TargetQuery()
    {
        return new BattleReplanQuery
        {
            HasTarget = true,
            TargetX = 760f,
            TargetZ = 60f,
            TargetTheater = Theater.East,
            Radius = 275f
        };
    }

    private static void R4MinorOutOfTheaterBattleDoesNotDirtyPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.West;
        TestHarness.AssertFalse(CicReviewRouter.BattleInvalidatesPlan(Battle(0, 100f, -300f), q), "minor western battle");
    }

    private static void R4MinorBattleAtTargetDoesNotDirtyPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.East;
        TestHarness.AssertFalse(CicReviewRouter.BattleInvalidatesPlan(Battle(2, 760f, 60f), q), "minor battle never resets an operation");
    }

    private static void R4MajorBattleElsewhereDoesNotDirtyPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.West;
        TestHarness.AssertFalse(CicReviewRouter.BattleInvalidatesPlan(Battle(1, 100f, -300f), q), "major battle in another theater");
    }

    private static void R4MajorBattleInTargetTheaterDirtiesPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.East;
        TestHarness.AssertTrue(CicReviewRouter.BattleInvalidatesPlan(Battle(3, 600f, 400f), q), "major battle in target theater");
    }

    private static void R4MajorBattleNearTargetDirtiesPlan()
    {
        var q = TargetQuery();
        q.BattleTheater = Theater.Unknown;
        TestHarness.AssertTrue(CicReviewRouter.BattleInvalidatesPlan(Battle(1, 800f, 100f), q), "major battle within radius");
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
