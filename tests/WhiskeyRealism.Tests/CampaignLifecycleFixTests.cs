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

        // R16 — director clamp per alliance + AI-controlled alliance gate
        Check("c1 r16 two alliances reviewed in the same instant both publish", R16TwoAlliancesSameInstantBothPublish);
        Check("c1 r16 clamp dedupes same alliance same game day", R16ClampDedupesSameAllianceSameDay);
        Check("c1 r16 player alliance outside w&l gets no cic", R16PlayerAllianceOutsideWlGetsNoCic);
        Check("c1 r16 w&l ai-vs-ai keeps player alliance cic unless player is cic", R16WlAiVsAiKeepsPlayerAllianceCic);
        Check("c1 r16 aifaction list is ground truth once ready", R16AiFactionListIsGroundTruth);

        // R5 — catalog vs Config/campaignobjectives.dat
        Check("c1 r5 fixture parses vanilla campaignobjectives layout", R5FixtureParsesVanillaLayout);
        Check("c1 r5 every profile target exists for its alliance chapter and date", R5EveryProfileTargetExistsForAllianceChapterAndDate);
        Check("c1 r5 allow list matches phase targets", R5AllowListMatchesPhaseTargets);
        Check("c1 r5 every phase target has a resolvable position", R5EveryPhaseTargetHasResolvablePosition);
        Check("c1 r5 no profile targets capture washington", R5NoProfileTargetsWashington);
        Check("c1 r5 no catalog phase with a defensive posture steers", R5DefensiveCatalogPhasesDoNotSteer);
        Check("c1 r5 router defensive override stops steering and releases", R5RouterDefensiveOverrideReleases);
        Check("c1 r5 returning to an offensive posture steers again", R5OffensivePostureSteersAgain);
        Check("c1 r5 retired csa defense profiles are gone", R5RetiredCsaDefenseProfilesAreGone);
        Check("c1 r5 resolve rejects profile whose phase targets are unavailable", R5ResolveRejectsUnavailablePhaseTargets);
        Check("c1 r5 generic fallback only targets objectives with towns or IIPs", R5GenericFallbackRequiresVanillaGround);
        Check("c1 r5 generic fallback skips abstract objectives like capture washington", R5GenericFallbackSkipsAbstractObjectives);
        Check("c1 r5 scenario 001 csa gets no plan and vanilla picks", R5Scenario001CsaGetsNoPlan);
        Check("c1 r5 generic fallback only uses objectives vanilla would pick", R5GenericFallbackUsesVanillaPickableSet);
        Check("c1 r5 objective steering lets vanilla pick without a plan", R5ObjectiveSteeringLetsVanillaPickWithoutPlan);

        // PER — sidecar persistence
        Check("c1 per sidecar round trip preserves plan history and new state", PerSidecarRoundTripPreservesState);
        Check("c1 per missing fields decode without throwing or fabricating posture", PerMissingFieldsDecodeSafely);
        Check("c1 per corrupt json is rejected", PerCorruptJsonIsRejected);
        Check("c1 per newer sidecar version is rejected", PerNewerVersionIsRejected);
        Check("c1 per forced succession ids are not persisted", PerForcedSuccessionIdsAreNotPersisted);
        Check("c1 per mod-written campaign objective survives reload", PerModWrittenObjectiveSurvivesReload);
        Check("c1 per old sidecar infers mod-written objective from the plan target", PerOldSidecarInfersModWrittenObjective);
        Check("c1 per runtime counters survive a battle round trip", PerRuntimeCountersSurviveRoundTrip);
        Check("c1 per atomic write keeps bak and leaves no temp", PerAtomicWriteKeepsBakAndNoTemp);
        Check("c1 per replace fallback survives original already moved to bak", PerReplaceFallbackSurvivesMissingOriginal);
        Check("c1 per load falls back to bak when primary is corrupt", PerLoadFallsBackToBak);
        Check("c1 per load falls back to tmp when primary and bak are missing", PerLoadFallsBackToTmp);
        Check("c1 per load reports no sidecar when nothing is usable", PerLoadReportsNothingUsable);

        // R15/H3 — reset between campaigns
        Check("c1 r15 new campaign scene without load resets", R15NewCampaignSceneWithoutLoadResets);
        Check("c1 r15 load before first update keeps loaded state", R15LoadBeforeFirstUpdateKeepsLoadedState);
        Check("c1 r15 same campaign scene never resets twice", R15SameSceneNeverResetsTwice);
        Check("c1 r15 reset reason reflects the scene operation", R15ResetReasonReflectsOperation);
        Check("c1 r15 era derives from date in both directions", R15EraDerivesFromDateBothDirections);
        Check("c1 r15 era transition stays monotonic within a campaign", R15EraTransitionStaysMonotonic);

        // C-Probe
        Check("c1 probe withdraw needs confirmation day", ProbeWithdrawNeedsConfirmationDay);
        Check("c1 probe new probe blocked during reprobe cooldown", ProbeNewProbeBlockedDuringCooldown);
        Check("c1 probe state resets when plan target changes", ProbeStateResetsWhenTargetChanges);
        Check("c1 probe stop mirrors vanilla operation-ended guard", ProbeStopMirrorsVanillaGuard);

        // C-HardWired
        Check("c1 hardwired capital threat mirrors vanilla defend-capital score", HardWiredCapitalThreatMirrorsVanilla);
        Check("c1 hardwired capital streak advances once per day and counts skipped days", HardWiredCapitalStreakAdvancesPerDay);
        Check("c1 hardwired days since front change", HardWiredDaysSinceFrontChange);
        Check("c1 hardwired context uses live local signals", HardWiredContextUsesLiveLocalSignals);
        Check("c1 hardwired profile near-target radius narrows vanilla radius", HardWiredProfileRadiusNarrowsVanilla);

        // Cadence
        Check("c1 cadence skipped weekly gate detected across fast-forward", CadenceSkippedWeeklyGateDetected);
        Check("c1 cadence skipped weekly gate across month rollover", CadenceSkippedWeeklyGateAcrossMonth);
        Check("c1 cadence consecutive days skip nothing", CadenceConsecutiveDaysSkipNothing);
        Check("c1 cadence skipped alternating day forces both alliances", CadenceSkippedAlternatingDayForcesBothAlliances);
        Check("c1 cadence review only runs once vanilla marks the campaign live", CadenceReviewOnlyWhenCampaignLive);
        Check("c1 per stale sidecar plan off the current catalog is replanned", PerStaleSidecarPlanIsReplanned);
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
            TargetLocalOwnStrength = own,
            TargetLocalEnemyStrength = enemy,
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
    // R16
    // ------------------------------------------------------------------

    private static void R16TwoAlliancesSameInstantBothPublish()
    {
        var clamp = new DirectorPublishClamp();
        int day = Serial(3, 6, 1862);
        TestHarness.AssertTrue(clamp.TryPublish(0, day), "alliance 0 publishes");
        TestHarness.AssertTrue(clamp.TryPublish(1, day), "alliance 1 publishes in the same instant");
    }

    private static void R16ClampDedupesSameAllianceSameDay()
    {
        var clamp = new DirectorPublishClamp();
        int day = Serial(3, 6, 1862);
        TestHarness.AssertTrue(clamp.TryPublish(1, day), "first publish");
        TestHarness.AssertFalse(clamp.TryPublish(1, day), "second publish same game day suppressed");
        TestHarness.AssertTrue(clamp.TryPublish(1, day + 1), "next game day publishes");
        TestHarness.AssertFalse(clamp.TryPublish(2, day), "out-of-range alliance rejected");
    }

    private static void R16PlayerAllianceOutsideWlGetsNoCic()
    {
        TestHarness.AssertFalse(CicEligibility.ShouldRunCic(0, factionListReady: false, hasAiFaction: false, playerAlliance: 0, aiVsAi: false, playerIsWlCic: false), "player alliance, no ai_vs_ai");
        TestHarness.AssertTrue(CicEligibility.ShouldRunCic(1, factionListReady: false, hasAiFaction: false, playerAlliance: 0, aiVsAi: false, playerIsWlCic: false), "enemy alliance runs");
    }

    private static void R16WlAiVsAiKeepsPlayerAllianceCic()
    {
        TestHarness.AssertTrue(CicEligibility.ShouldRunCic(0, factionListReady: false, hasAiFaction: false, playerAlliance: 0, aiVsAi: true, playerIsWlCic: false), "w&l subordinate: vanilla AI runs player alliance");
        TestHarness.AssertFalse(CicEligibility.ShouldRunCic(0, factionListReady: true, hasAiFaction: true, playerAlliance: 0, aiVsAi: true, playerIsWlCic: true), "w&l player is CIC");
    }

    private static void R16AiFactionListIsGroundTruth()
    {
        TestHarness.AssertFalse(CicEligibility.ShouldRunCic(1, factionListReady: true, hasAiFaction: false, playerAlliance: 0, aiVsAi: false, playerIsWlCic: false), "no AIFaction => no CIC");
        TestHarness.AssertTrue(CicEligibility.ShouldRunCic(0, factionListReady: true, hasAiFaction: true, playerAlliance: 0, aiVsAi: true, playerIsWlCic: false), "AIFaction present => CIC");
    }

    // ------------------------------------------------------------------
    // R5
    // ------------------------------------------------------------------

    private static void R5FixtureParsesVanillaLayout()
    {
        var rows = CampaignObjectivesDat.Parse(CampaignObjectivesDat.Fixture);
        TestHarness.AssertEqual(38, rows.Count, "objective count");
        var o4 = rows[4];
        TestHarness.AssertEqual(4, o4.Id, "id 4");
        TestHarness.AssertEqual(1, o4.Alliance, "objective 4 (Capture Washington) is a CSA objective");
        var o31 = rows[31];
        TestHarness.AssertEqual(0, o31.Alliance, "objective 31 (Shenandoah Valley) is a Union objective");
        TestHarness.AssertTrue(o31.Scenarios.Contains("002") && !o31.Scenarios.Contains("001"), "31 is W&L-only");
        TestHarness.AssertEqual(1862, rows[37].ValidFromYear, "37 valid from 1862");
        TestHarness.AssertTrue(rows[36].Prerequisites.Contains(29), "36 requires 29");
    }

    private static IEnumerable<HistoricalOperationProfile> AllProfiles()
    {
        // The catalog has no runtime need to enumerate profiles; read the private table so the
        // .dat validation covers every profile, including ones added later.
        var field = typeof(HistoricalOperationCatalog).GetField("Profiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        TestHarness.AssertTrue(field != null, "HistoricalOperationCatalog.Profiles table");
        foreach (var profile in (HistoricalOperationProfile[])field.GetValue(null))
            yield return profile;
    }

    private static void R5EveryProfileTargetExistsForAllianceChapterAndDate()
    {
        var rows = CampaignObjectivesDat.Parse(CampaignObjectivesDat.Fixture);
        var byId = new Dictionary<int, CampaignObjectivesDat.Row>();
        foreach (var r in rows) byId[r.Id] = r;

        foreach (var profile in AllProfiles())
        {
            var ids = new List<int>(profile.ObjectiveAllowList);
            foreach (var phase in profile.Phases) ids.Add(phase.TargetObjectiveId);
            foreach (int id in ids)
            {
                string label = profile.OperationId + " objective " + id;
                TestHarness.AssertTrue(byId.TryGetValue(id, out var row), label + " exists in campaignobjectives.dat");
                // Vanilla GetAvailableObjectives (decompile 178827) only returns objectives whose
                // ObjectiveAlliance == the asking alliance, so a CIC can only ever pursue its own.
                TestHarness.AssertEqual(profile.AllianceId, row.Alliance, label + " alliance");
                TestHarness.AssertTrue(row.Scenarios.Count > 0, label + " has a scenario");
                bool chapterOverlap = row.Chapters.Count == 0;
                foreach (int ch in row.Chapters)
                    if (ch >= profile.MinChapter && ch <= profile.MaxChapter) chapterOverlap = true;
                TestHarness.AssertTrue(chapterOverlap, label + " chapter window overlaps [" + profile.MinChapter + ".." + profile.MaxChapter + "]");
                int profileStart = profile.StartYear * 12 + profile.StartMonth;
                int profileEnd = profile.EndYear * 12 + profile.EndMonth;
                int validFrom = row.ValidFromYear * 12 + row.ValidFromMonth;
                int validTo = row.ValidToYear * 12 + row.ValidToMonth;
                TestHarness.AssertTrue(profileStart <= validTo && validFrom <= profileEnd, label + " date window overlaps objective validity (IsDeactivated 178920)");
                TestHarness.AssertTrue(row.Prerequisites.Count == 0, label + " has no precampaign prerequisite (IsDeactivated deactivates until accomplished)");
            }
        }
    }

    private static void R5AllowListMatchesPhaseTargets()
    {
        foreach (var profile in AllProfiles())
        {
            var targets = new HashSet<int>();
            foreach (var phase in profile.Phases) targets.Add(phase.TargetObjectiveId);
            TestHarness.AssertTrue(targets.Contains(profile.PrimaryObjectiveId), profile.OperationId + " primary is a phase target");
            foreach (int id in profile.ObjectiveAllowList)
                TestHarness.AssertTrue(targets.Contains(id), profile.OperationId + " allow-listed " + id + " is pursued by a phase");
        }
    }

    private static void R5EveryPhaseTargetHasResolvablePosition()
    {
        foreach (var profile in AllProfiles())
            foreach (var phase in profile.Phases)
                TestHarness.AssertTrue(ObjectiveCatalog.TryResolvePosition(phase.TargetObjectiveId, out _, out _),
                    profile.OperationId + " target " + phase.TargetObjectiveId + " has a catalog position");
    }

    private static void R5NoProfileTargetsWashington()
    {
        foreach (var profile in AllProfiles())
            foreach (var phase in profile.Phases)
                TestHarness.AssertTrue(phase.TargetObjectiveId != 4, profile.OperationId + " must not target objective 4 (Capture Washington)");
    }

    private static void R5DefensiveCatalogPhasesDoNotSteer()
    {
        bool sawDefensive = false, sawOffensive = false;
        foreach (var profile in AllProfiles())
        {
            foreach (var template in profile.Phases)
            {
                var posture = template.Posture == OperationPosture.Inherit ? profile.Posture : template.Posture;
                bool defensive = HistoricalOperationCatalog.IsDefensivePosture(posture);
                sawDefensive |= defensive;
                sawOffensive |= !defensive;
                var phase = new Phase { PhaseId = template.PhaseId, TargetObjectiveId = template.TargetObjectiveId, OperationPosture = posture };
                TestHarness.AssertEqual(!defensive, CampaignObjectiveSteering.PhaseSteers(phase), profile.OperationId + "/" + template.PhaseId + " steering");
            }
        }
        TestHarness.AssertTrue(sawDefensive && sawOffensive, "catalog exercises both kinds of phase");
        TestHarness.AssertFalse(CampaignObjectiveSteering.PhaseSteers(null), "no phase");
        TestHarness.AssertFalse(CampaignObjectiveSteering.PhaseSteers(new Phase { TargetObjectiveId = -1, OperationPosture = OperationPosture.ConcentratedAttack }), "no target");
    }

    private static OperationalPlan EastPressurePlan()
    {
        TestHarness.AssertTrue(HistoricalOperationCatalog.TryGetById("union-east-pressure", out var profile), "profile");
        var template = profile.Phases[0];
        var plan = new OperationalPlan { OperationId = profile.OperationId, OperationPosture = profile.Posture, PlanDeadlineMonth = 12, PlanDeadlineYear = 1861 };
        plan.Phases.Add(new Phase
        {
            PhaseId = template.PhaseId,
            TargetObjectiveId = template.TargetObjectiveId,
            Transition = template.Transition,
            OperationPosture = template.Posture,
            AllowCoordinatedAttack = template.AllowCoordinatedAttack,
            AllowReinforcementPackage = template.AllowReinforcementPackage,
            AllowProbeOnly = template.AllowProbeOnly,
            DeadlineMonth = 12,
            DeadlineYear = 1861
        });
        return plan;
    }

    private static void R5RouterDefensiveOverrideReleases()
    {
        foreach (var action in new[] { PhaseTruthAction.ScreenAndDelay, PhaseTruthAction.Recover, PhaseTruthAction.Pause })
        {
            var plan = EastPressurePlan();
            TestHarness.AssertTrue(CampaignObjectiveSteering.PhaseSteers(plan.CurrentPhase), "offensive phase steers");
            CicReviewRouter.Route(plan, Truth(action), Serial(1, 7, 1861), 7, 1861);
            bool steers = CampaignObjectiveSteering.PhaseSteers(plan.CurrentPhase);
            TestHarness.AssertFalse(steers, action + " override stops steering");
            var steer = CampaignObjectiveSteering.Decide(steers, plan.CurrentPhase.TargetObjectiveId, currentFollowed: 3, lastWrittenByMod: 3);
            TestHarness.AssertTrue(steer.RunVanilla, action + ": vanilla picks");
            TestHarness.AssertEqual(-1, steer.WriteFollowed, action + ": mod-written objective released (vanilla re-picks at 17780)");
        }
    }

    private static void R5OffensivePostureSteersAgain()
    {
        var plan = EastPressurePlan();
        CicReviewRouter.Route(plan, Truth(PhaseTruthAction.Recover), Serial(1, 7, 1861), 7, 1861);
        CicReviewRouter.Route(plan, Truth(PhaseTruthAction.Continue), Serial(2, 7, 1861), 7, 1861);
        bool steers = CampaignObjectiveSteering.PhaseSteers(plan.CurrentPhase);
        TestHarness.AssertTrue(steers, "template posture restored => steers");
        var steer = CampaignObjectiveSteering.Decide(steers, plan.CurrentPhase.TargetObjectiveId, currentFollowed: 12, lastWrittenByMod: -1);
        TestHarness.AssertFalse(steer.RunVanilla, "mod steers again");
        TestHarness.AssertEqual(3, steer.WriteFollowed, "plan target written");
    }

    private static void R5RetiredCsaDefenseProfilesAreGone()
    {
        // No CSA objective with towns represents ground the CSA defends (campaignobjectives.dat);
        // both ops only pulled forces at the offensive B&O objective 32.
        TestHarness.AssertFalse(HistoricalOperationCatalog.TryGetById("csa-capital-defense", out _), "csa-capital-defense retired");
        TestHarness.AssertFalse(HistoricalOperationCatalog.TryGetById("csa-protraction-defense", out _), "csa-protraction-defense retired");
    }

    private static void R5ResolveRejectsUnavailablePhaseTargets()
    {
        TestHarness.AssertTrue(ObjectiveCatalog.TryResolve(3, out var objective), "objective 3");
        var candidate = new HistoricalOperationCandidate { ObjectiveId = 3, Objective = objective, ObjectiveScore = 10f };
        var rejected = HistoricalOperationCatalog.Resolve(0, EraStage.Amateur1861, 1, 7, 1861, candidate, null, default(PersonalityVector), null, null,
            availableObjectiveIds: new HashSet<int> { 37 });
        TestHarness.AssertEqual(HistoricalOperationMatchKind.NoProfile, rejected.Kind, "phase targets unavailable");
        var accepted = HistoricalOperationCatalog.Resolve(0, EraStage.Amateur1861, 1, 7, 1861, candidate, null, default(PersonalityVector), null, null,
            availableObjectiveIds: new HashSet<int> { 3 });
        TestHarness.AssertEqual(HistoricalOperationMatchKind.Matched, accepted.Kind, "phase targets available");
    }

    // Mirrors the CampaignObjective fields the CIC reads: UniqueObjectiveID and the
    // town/IIP `objectives` list (empty for abstract objectives 0-28 in campaignobjectives.dat).
    internal sealed class FakeObjective
    {
        public int UniqueObjectiveID;
        public List<object> objectives = new List<object>();
        public FakeObjective(int id, int towns = 0)
        {
            UniqueObjectiveID = id;
            for (int i = 0; i < towns; i++) objectives.Add(new object());
        }
    }

    private static OperationalPlan ReplanWith(int alliance, EraStage stage, int month, int year, params FakeObjective[] objectives)
    {
        return ReplanWith(alliance, stage, month, year, null, objectives);
    }

    // vanillaPickable: ids GetAvailableObjectives(alliance, false, mintownobjectives: 1) returns
    // (vanilla's own PickCampaignObjective call); null = same as the available list.
    private static OperationalPlan ReplanWith(int alliance, EraStage stage, int month, int year, int[] vanillaPickable, params FakeObjective[] objectives)
    {
        var savedProvider = CIC.AvailableObjectivesProvider;
        var savedPickable = CIC.VanillaPickableObjectivesProvider;
        var savedPosition = CIC.TargetPositionResolves;
        var savedDoctrine = CicSettings.HistoricalOperationDoctrine;
        try
        {
            var list = new List<object>();
            list.AddRange(objectives);
            CIC.AvailableObjectivesProvider = _ => list;
            var pickable = new List<object>();
            foreach (var o in objectives)
                if (vanillaPickable == null || Array.IndexOf(vanillaPickable, o.UniqueObjectiveID) >= 0) pickable.Add(o);
            CIC.VanillaPickableObjectivesProvider = _ => pickable;
            CIC.TargetPositionResolves = id => ObjectiveCatalog.TryResolvePosition(id, out _, out _);
            CicSettings.HistoricalOperationDoctrine = true;
            var cic = new CIC { AllianceId = alliance, OfficerName = "Test" };
            var era = new EraStageManager { Stage = stage };
            cic.Replan(era, month, year, Serial(1, month, year), 2, null, null);
            return cic.ActivePlan;
        }
        finally
        {
            CIC.AvailableObjectivesProvider = savedProvider;
            CIC.VanillaPickableObjectivesProvider = savedPickable;
            CIC.TargetPositionResolves = savedPosition;
            CicSettings.HistoricalOperationDoctrine = savedDoctrine;
        }
    }

    private static void R5GenericFallbackRequiresVanillaGround()
    {
        // CSA in 1863 has no historical profile. Only an objective with towns/IIPs can carry
        // vanilla's followedcampaignobjective weight (UpdateImportanceValues 14922/14961).
        var plan = ReplanWith(1, EraStage.Decisive1863, 6, 1863, new FakeObjective(25), new FakeObjective(4), new FakeObjective(30, towns: 4));
        TestHarness.AssertTrue(plan != null, "generic fallback plan built");
        TestHarness.AssertTrue(string.IsNullOrEmpty(plan.OperationId), "fallback is the legacy generic plan");
        TestHarness.AssertEqual(30, plan.CurrentPhase.TargetObjectiveId, "fallback targets the objective with ground");
    }

    private static void R5GenericFallbackSkipsAbstractObjectives()
    {
        // Objective 4 (Capture Washington) has a mod catalog position but no towns: it must
        // not become a permanent generic offensive.
        var plan = ReplanWith(1, EraStage.Decisive1863, 6, 1863, new FakeObjective(25), new FakeObjective(4));
        TestHarness.AssertTrue(plan == null, "no plan => PickCampaignObjective lets vanilla choose");
    }

    private static void R5GenericFallbackUsesVanillaPickableSet()
    {
        // 30 has towns but none enemy-held any more, so vanilla's mintownobjectives=1 call
        // (PickCampaignObjective 17782, GetAvailableObjectives 178827) would not offer it.
        var plan = ReplanWith(1, EraStage.Decisive1863, 6, 1863, new int[0], new FakeObjective(30, towns: 4));
        TestHarness.AssertTrue(plan == null, "objective vanilla would not pick is not a fallback target");
    }

    private static void R5Scenario001CsaGetsNoPlan()
    {
        // Grand campaign (001): every CSA objective is abstract (no towns) and no CSA profile
        // matches, so the CSA keeps vanilla's own objective picking in every era.
        foreach (var era in new[] { EraStage.Amateur1861, EraStage.Operational1862, EraStage.Decisive1863, EraStage.TotalWar1864 })
        {
            int year = 1861 + (int)era;
            var plan = ReplanWith(1, era, 7, year,
                new FakeObjective(1), new FakeObjective(4), new FakeObjective(8), new FakeObjective(12),
                new FakeObjective(13), new FakeObjective(15), new FakeObjective(16), new FakeObjective(19),
                new FakeObjective(21), new FakeObjective(25), new FakeObjective(28));
            TestHarness.AssertTrue(plan == null, "001 CSA " + era + " gets no plan");
        }
    }

    private static void R5ObjectiveSteeringLetsVanillaPickWithoutPlan()
    {
        var steer = CampaignObjectiveSteering.Decide(hasPlanTarget: true, planTargetObjectiveId: 32, currentFollowed: 4, lastWrittenByMod: -1);
        TestHarness.AssertFalse(steer.RunVanilla, "plan target steers");
        TestHarness.AssertEqual(32, steer.WriteFollowed, "writes plan target");

        var released = CampaignObjectiveSteering.Decide(hasPlanTarget: false, planTargetObjectiveId: -1, currentFollowed: 32, lastWrittenByMod: 32);
        TestHarness.AssertTrue(released.RunVanilla, "no plan => vanilla PickCampaignObjective runs");
        TestHarness.AssertEqual(-1, released.WriteFollowed, "stale mod-written objective is cleared so vanilla re-picks (17780)");

        var vanillaOwned = CampaignObjectiveSteering.Decide(hasPlanTarget: false, planTargetObjectiveId: -1, currentFollowed: 12, lastWrittenByMod: 32);
        TestHarness.AssertTrue(vanillaOwned.RunVanilla, "vanilla runs");
        TestHarness.AssertEqual(CampaignObjectiveSteering.NoWrite, vanillaOwned.WriteFollowed, "vanilla's own pick is left alone");
    }

    // ------------------------------------------------------------------
    // PER
    // ------------------------------------------------------------------

    private static SidecarSnapshot SampleSnapshot()
    {
        var snap = SidecarSnapshot.Empty();
        var plan = TwoPhasePlan(0.6f);
        plan.OperationId = "union-east-pressure";
        plan.CurrentPhaseIndex = 1;
        plan.Phases[1].PhaseStartedDaySerial = Serial(4, 2, 1862);
        snap.Cics[0] = new CIC { AllianceId = 0, OfficerName = "Lincoln", OfficerPersonality = new PersonalityVector(0.3f, 0.1f, 0.1f, 0.4f, 0.7f), ActivePlan = plan };
        snap.Eras[0] = EraStage.Operational1862;
        snap.Eras[1] = EraStage.Operational1862;
        snap.DirectorMemories[0] = new DirectorMemory
        {
            LastPosture = new DirectorPosture { Pace = CampaignPace.TooQuiet, Intent = StrategicIntent.Probe, Risk = CollapseRisk.Elevated, TheaterPriority = Theater.East },
            CapitalDangerStreakDays = 3
        };
        snap.RecentReplans[0] = new[] { Serial(1, 1, 1862), Serial(1, 2, 1862) };
        snap.ProbeStates[1] = new OperationalProbeState { ProbeId = "p-1", UnitKey = "Army of X:12", TargetAreaKey = "a-3", SourceSectorKey = "East", StartedDaySerial = Serial(2, 3, 1862), LastObservedEnemyStrength = 9000f, LastObservedFriendlyStrength = 4000f };
        snap.FiscalMemories[1] = new WhiskeyRealism.Strategic.Fiscal.FiscalStateMemory { PreviousPosture = WhiskeyRealism.Strategic.Fiscal.FiscalPosture.CreditDefense, StableTicksAboveEmergency = 4, EmergencyResidue = true };
        snap.MinorOfficers[17] = new PersonalityVector(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
        snap.FiredEvents.Add(1);
        snap.AppliedEvents.Add(1);
        snap.Battles.Add(Battle(1, 760f, 60f));
        snap.LastSeenMonth = 3;
        snap.LastSeenYear = 1862;
        return snap;
    }

    private static void PerSidecarRoundTripPreservesState()
    {
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(SampleSnapshot()));
        TestHarness.AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "deserialize: " + reason);
        TestHarness.AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out reason), "decode: " + reason);

        TestHarness.AssertEqual("union-east-pressure", back.Cics[0].ActivePlan.OperationId, "operation id");
        TestHarness.AssertEqual(1, back.Cics[0].ActivePlan.CurrentPhaseIndex, "phase index");
        TestHarness.AssertEqual(Serial(4, 2, 1862), back.Cics[0].ActivePlan.Phases[1].PhaseStartedDaySerial, "phase start");
        TestHarness.AssertTrue(back.Cics[1] == null, "alliance without CIC stays null");
        TestHarness.AssertEqual(EraStage.Operational1862, back.Eras[1].Value, "era kept for alliance without CIC");
        TestHarness.AssertEqual(CampaignPace.TooQuiet, back.DirectorMemories[0].LastPosture.Pace, "posture pace");
        TestHarness.AssertEqual(3, back.DirectorMemories[0].CapitalDangerStreakDays, "capital streak");
        TestHarness.AssertEqual(2, back.RecentReplans[0].Length, "replans");
        TestHarness.AssertEqual("Army of X:12", back.ProbeStates[1].UnitKey, "probe unit");
        TestHarness.AssertEqual(Serial(2, 3, 1862), back.ProbeStates[1].StartedDaySerial, "probe start");
        TestHarness.AssertEqual(4, back.FiscalMemories[1].StableTicksAboveEmergency, "fiscal stable ticks");
        TestHarness.AssertTrue(back.FiscalMemories[1].EmergencyResidue, "fiscal residue");
        TestHarness.AssertTrue(back.MinorOfficers.ContainsKey(17), "minor officer");
        TestHarness.AssertTrue(back.FiredEvents.Contains(1) && back.AppliedEvents.Contains(1), "succession");
        TestHarness.AssertEqual(1, back.Battles.Count, "battle history");
        TestHarness.AssertEqual(3, back.LastSeenMonth, "last seen month restored");
        TestHarness.AssertEqual(1862, back.LastSeenYear, "last seen year restored");
    }

    private static SidecarSnapshot RoundTrip(SidecarSnapshot snap)
    {
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
        TestHarness.AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "deserialize: " + reason);
        TestHarness.AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out reason), "decode: " + reason);
        return back;
    }

    private static void PerModWrittenObjectiveSurvivesReload()
    {
        var snap = SampleSnapshot();
        snap.ModWrittenObjective[0] = 29;
        var back = RoundTrip(snap);
        TestHarness.AssertEqual(29, back.ModWrittenObjective[0], "mod-written objective restored");
        TestHarness.AssertEqual(-1, back.ModWrittenObjective[1], "none written for alliance 1");
        // After reload, a now-defensive/planless faction still clears the stale id it wrote.
        var steer = CampaignObjectiveSteering.Decide(false, -1, currentFollowed: 29, lastWrittenByMod: back.ModWrittenObjective[0].Value);
        TestHarness.AssertEqual(-1, steer.WriteFollowed, "stale mod objective cleared after reload");
    }

    private static void PerOldSidecarInfersModWrittenObjective()
    {
        const string json = "{\"version\":2,\"factions\":[{\"factionId\":0,\"currentEra\":\"Amateur1861\",\"cic\":{\"officerName\":\"Lincoln\"}}]}";
        TestHarness.AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "deserialize: " + reason);
        TestHarness.AssertTrue(SidecarDtoCodec.TryDecode(dto, out var snap, out reason), "decode: " + reason);
        TestHarness.AssertFalse(snap.ModWrittenObjective[0].HasValue, "field absent => unknown, not -1");

        TestHarness.AssertEqual(3, CampaignObjectiveSteering.InferModWritten(null, currentFollowed: 3, planTargetObjectiveId: 3), "followed == plan target => the old mod wrote it");
        TestHarness.AssertEqual(-1, CampaignObjectiveSteering.InferModWritten(null, currentFollowed: 12, planTargetObjectiveId: 3), "vanilla's own pick");
        TestHarness.AssertEqual(-1, CampaignObjectiveSteering.InferModWritten(null, currentFollowed: -1, planTargetObjectiveId: -1), "no plan, nothing followed");
        TestHarness.AssertEqual(29, CampaignObjectiveSteering.InferModWritten(29, currentFollowed: 3, planTargetObjectiveId: 3), "persisted value wins");
    }

    private static void PerRuntimeCountersSurviveRoundTrip()
    {
        int day = Serial(1, 6, 1862);
        var snap = SampleSnapshot();
        snap.FrontChangedDay[1] = day - 40;
        snap.CapitalStreakDay[0] = day - 1;
        var hysteresis = new ProbeHysteresis();
        hysteresis.Filter(OperationalProbeDecision.Withdraw, true, "a-1", day);
        hysteresis.Filter(OperationalProbeDecision.Withdraw, true, "a-1", day + 1);
        snap.ProbeHysteresis[0] = hysteresis.Snapshot();

        var back = RoundTrip(snap);
        TestHarness.AssertEqual(day - 40, back.FrontChangedDay[1], "front-change stamp");
        TestHarness.AssertEqual(-1, back.FrontChangedDay[0], "unset stamp stays -1");
        TestHarness.AssertEqual(day - 1, back.CapitalStreakDay[0], "capital streak day");
        var restored = ProbeHysteresis.FromSnapshot(back.ProbeHysteresis[0]);
        TestHarness.AssertEqual(OperationalProbeDecision.None, restored.Filter(OperationalProbeDecision.Probe, false, "a-1", day + 2), "reprobe cooldown survives reload");

        var pending = new ProbeHysteresis();
        pending.Filter(OperationalProbeDecision.Withdraw, true, "a-2", day);
        var pendingBack = ProbeHysteresis.FromSnapshot(RoundTripHysteresis(pending.Snapshot()));
        TestHarness.AssertEqual(OperationalProbeDecision.Withdraw, pendingBack.Filter(OperationalProbeDecision.Withdraw, true, "a-2", day + 1), "withdraw confirmation streak survives reload");
    }

    private static ProbeHysteresisDto RoundTripHysteresis(ProbeHysteresisDto dto)
    {
        var snap = SampleSnapshot();
        snap.ProbeHysteresis[1] = dto;
        return RoundTrip(snap).ProbeHysteresis[1];
    }

    private static void PerMissingFieldsDecodeSafely()
    {
        const string json = "{\"version\":1,\"factions\":[{\"factionId\":0,\"currentEra\":\"Operational1862\",\"cic\":{\"officerName\":\"Lincoln\"},\"directorMemory\":{\"lastFullRefresh\":5}}],\"succession\":null,\"minorOfficerProfiles\":[{\"commanderId\":5}],\"battleHistory\":null}";
        TestHarness.AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "deserialize: " + reason);
        TestHarness.AssertTrue(SidecarDtoCodec.TryDecode(dto, out var snap, out reason), "decode: " + reason);
        TestHarness.AssertEqual("Lincoln", snap.Cics[0].OfficerName, "cic restored");
        TestHarness.AssertTrue(snap.Cics[0].ActivePlan == null, "no plan");
        TestHarness.AssertTrue(snap.DirectorMemories[0].LastPosture == null, "missing pace/intent/risk must not fabricate an all-zero posture");
        TestHarness.AssertEqual(0, snap.FiredEvents.Count, "null succession tolerated");
        TestHarness.AssertEqual(0, snap.MinorOfficers.Count, "minor officer without personality skipped");
        TestHarness.AssertEqual(-1, snap.LastSeenMonth, "v1 sidecar has no last-seen month");
    }

    private static void PerCorruptJsonIsRejected()
    {
        TestHarness.AssertFalse(SidecarDtoCodec.TryDeserialize("{\"version\":1,\"factions\":[{", out _, out _), "truncated json");
        TestHarness.AssertFalse(SidecarDtoCodec.TryDeserialize("", out _, out _), "empty file");
    }

    private static void PerNewerVersionIsRejected()
    {
        TestHarness.AssertFalse(SidecarDtoCodec.TryDeserialize("{\"version\":99}", out _, out var reason), "future version");
        TestHarness.AssertTrue(reason.Contains("version"), "reason names the version: " + reason);
    }

    private static void PerForcedSuccessionIdsAreNotPersisted()
    {
        var snap = SampleSnapshot();
        snap.FiredEvents.Add(5);
        snap.ForcedEvents.Add(5);
        var dto = SidecarDtoCodec.Encode(snap);
        TestHarness.AssertTrue(dto.Succession.FiredEvents.Contains(1), "naturally fired id persisted");
        TestHarness.AssertFalse(dto.Succession.FiredEvents.Contains(5), "test-mode forced id not persisted");
    }

    private static void WithTempDir(Action<string> body)
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wr-c1-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try { body(System.IO.Path.Combine(dir, "whiskeyrealism.json")); }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    private static string ValidSidecarJson(int lastSeenMonth)
    {
        var snap = SampleSnapshot();
        snap.LastSeenMonth = lastSeenMonth;
        return SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
    }

    private static void PerReplaceFallbackSurvivesMissingOriginal()
    {
        WithTempDir(path =>
        {
            // Win32 ReplaceFile can fail after it already renamed the original to .bak.
            System.IO.File.WriteAllText(path + ".bak", "old");
            System.IO.File.WriteAllText(path + ".tmp", "new");
            SidecarDtoCodec.ReplaceByCopy(path + ".tmp", path, path + ".bak");
            TestHarness.AssertEqual("new", System.IO.File.ReadAllText(path), "new content in place");
            TestHarness.AssertEqual("old", System.IO.File.ReadAllText(path + ".bak"), "backup untouched");
            TestHarness.AssertFalse(System.IO.File.Exists(path + ".tmp"), "temp removed");
        });
    }

    private static void PerLoadFallsBackToBak()
    {
        WithTempDir(path =>
        {
            System.IO.File.WriteAllText(path, "{\"version\":2,\"factions\":[{");
            System.IO.File.WriteAllText(path + ".bak", ValidSidecarJson(4));
            TestHarness.AssertTrue(SidecarDtoCodec.TryLoadWithFallback(path, out var snap, out var source, out var reason), "loads: " + reason);
            TestHarness.AssertEqual(path + ".bak", source, "source");
            TestHarness.AssertEqual(4, snap.LastSeenMonth, "bak content");
        });
    }

    private static void PerLoadFallsBackToTmp()
    {
        WithTempDir(path =>
        {
            System.IO.File.WriteAllText(path + ".tmp", ValidSidecarJson(5));
            TestHarness.AssertTrue(SidecarDtoCodec.TryLoadWithFallback(path, out var snap, out var source, out var reason), "loads: " + reason);
            TestHarness.AssertEqual(path + ".tmp", source, "source");
            TestHarness.AssertEqual(5, snap.LastSeenMonth, "tmp content");
        });
    }

    private static void PerLoadReportsNothingUsable()
    {
        WithTempDir(path =>
        {
            TestHarness.AssertFalse(SidecarDtoCodec.TryLoadWithFallback(path, out _, out _, out var reason), "nothing on disk");
            TestHarness.AssertEqual("no-sidecar", reason, "reason");
            System.IO.File.WriteAllText(path, "garbage");
            TestHarness.AssertFalse(SidecarDtoCodec.TryLoadWithFallback(path, out _, out _, out reason), "only a corrupt primary");
            TestHarness.AssertTrue(reason != "no-sidecar", "rejection reason kept: " + reason);
        });
    }

    private static void PerAtomicWriteKeepsBakAndNoTemp()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wr-c1-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            string path = System.IO.Path.Combine(dir, "whiskeyrealism.json");
            SidecarDtoCodec.WriteAtomic(path, "first");
            TestHarness.AssertEqual("first", System.IO.File.ReadAllText(path), "first write");
            SidecarDtoCodec.WriteAtomic(path, "second");
            TestHarness.AssertEqual("second", System.IO.File.ReadAllText(path), "second write");
            TestHarness.AssertEqual("first", System.IO.File.ReadAllText(path + ".bak"), "previous kept as .bak");
            TestHarness.AssertFalse(System.IO.File.Exists(path + ".tmp"), "no temp left behind");
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    // ------------------------------------------------------------------
    // R15
    // ------------------------------------------------------------------

    private static void R15NewCampaignSceneWithoutLoadResets()
    {
        var tracker = new CampaignEpochTracker();
        var sceneA = new object();
        var sceneB = new object();
        TestHarness.AssertTrue(tracker.ShouldResetOnUpdate(sceneA), "first campaign scene resets");
        TestHarness.AssertTrue(tracker.ShouldResetOnUpdate(sceneB), "new campaign scene (no AICampaign.Load) resets");
    }

    private static void R15LoadBeforeFirstUpdateKeepsLoadedState()
    {
        var tracker = new CampaignEpochTracker();
        var sceneA = new object();
        tracker.ShouldResetOnUpdate(sceneA);
        var sceneB = new object();
        tracker.NoteLoad(sceneB);
        TestHarness.AssertFalse(tracker.ShouldResetOnUpdate(sceneB), "load already reset + applied for this scene");

        var sceneC = new object();
        tracker.NoteLoad(null);
        TestHarness.AssertFalse(tracker.ShouldResetOnUpdate(sceneC), "load with unknown instance adopts the next scene");
        var sceneD = new object();
        TestHarness.AssertTrue(tracker.ShouldResetOnUpdate(sceneD), "pending marker consumed once");
    }

    private static void R15SameSceneNeverResetsTwice()
    {
        var tracker = new CampaignEpochTracker();
        var scene = new object();
        TestHarness.AssertTrue(tracker.ShouldResetOnUpdate(scene), "first sight");
        TestHarness.AssertFalse(tracker.ShouldResetOnUpdate(scene), "same scene");
        TestHarness.AssertFalse(tracker.ShouldResetOnUpdate(null), "no instance");
    }

    private static void R15ResetReasonReflectsOperation()
    {
        TestHarness.AssertEqual("battle-return-scene", CampaignEpochTracker.ResetReason(5), "op 5");
        TestHarness.AssertEqual("save-load-scene", CampaignEpochTracker.ResetReason(6), "op 6");
        TestHarness.AssertEqual("battle-scene", CampaignEpochTracker.ResetReason(3), "op 3");
        TestHarness.AssertEqual("battle-scene", CampaignEpochTracker.ResetReason(8), "op 8");
        TestHarness.AssertEqual("new-campaign-scene", CampaignEpochTracker.ResetReason(0), "op 0");
        TestHarness.AssertEqual("new-campaign-scene", CampaignEpochTracker.ResetReason(2), "op 2");
    }

    private static void R15EraDerivesFromDateBothDirections()
    {
        TestHarness.AssertEqual(EraStage.TotalWar1864, EraStageManager.ForDate(6, 1864, false, false), "1864");
        TestHarness.AssertEqual(EraStage.Amateur1861, EraStageManager.ForDate(6, 1861, false, false), "back to 1861");
        TestHarness.AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(3, 1863, true, false), "Vicksburg early keeps 1863");
        TestHarness.AssertEqual(EraStage.TotalWar1864, EraStageManager.ForDate(3, 1864, false, true), "Atlanta early");
        var era = new EraStageManager { Stage = EraStage.TotalWar1864 };
        era.ResetToDate(6, 1861);
        TestHarness.AssertEqual(EraStage.Amateur1861, era.Stage, "reset re-derives the era backwards");
    }

    private static void R15EraTransitionStaysMonotonic()
    {
        var era = new EraStageManager { Stage = EraStage.Decisive1863 };
        era.CheckTransition(6, 1862, false, false);
        TestHarness.AssertEqual(EraStage.Decisive1863, era.Stage, "CheckTransition never regresses");
        era.CheckTransition(1, 1864, false, false);
        TestHarness.AssertEqual(EraStage.TotalWar1864, era.Stage, "advances");
    }

    // ------------------------------------------------------------------
    // C-Probe
    // ------------------------------------------------------------------

    private static void ProbeWithdrawNeedsConfirmationDay()
    {
        var h = new ProbeHysteresis();
        int day = Serial(1, 6, 1862);
        TestHarness.AssertEqual(OperationalProbeDecision.Pause, h.Filter(OperationalProbeDecision.Withdraw, true, "a-1", day), "first withdraw holds");
        TestHarness.AssertEqual(OperationalProbeDecision.Probe, h.Filter(OperationalProbeDecision.Probe, true, "a-1", day + 1), "probe resets the streak");
        TestHarness.AssertEqual(OperationalProbeDecision.Pause, h.Filter(OperationalProbeDecision.Withdraw, true, "a-1", day + 2), "streak restarted");
        TestHarness.AssertEqual(OperationalProbeDecision.Withdraw, h.Filter(OperationalProbeDecision.Withdraw, true, "a-1", day + 3), "second consecutive withdraw executes");
    }

    private static void ProbeNewProbeBlockedDuringCooldown()
    {
        var h = new ProbeHysteresis();
        int day = Serial(1, 6, 1862);
        h.Filter(OperationalProbeDecision.Withdraw, true, "a-1", day);
        h.Filter(OperationalProbeDecision.Withdraw, true, "a-1", day + 1);
        TestHarness.AssertEqual(OperationalProbeDecision.None, h.Filter(OperationalProbeDecision.Probe, false, "a-1", day + 2), "no immediate re-probe of the same target");
        TestHarness.AssertEqual(OperationalProbeDecision.Probe, h.Filter(OperationalProbeDecision.Probe, false, "a-2", day + 2), "other target allowed");
        TestHarness.AssertEqual(OperationalProbeDecision.Probe, h.Filter(OperationalProbeDecision.Probe, false, "a-1", day + 1 + ProbeHysteresis.ReprobeCooldownDays), "cooldown expires");
    }

    private static void ProbeStateResetsWhenTargetChanges()
    {
        TestHarness.AssertTrue(ProbeHysteresis.KeepPreviousState("a-1", "a-1"), "same target keeps state");
        TestHarness.AssertFalse(ProbeHysteresis.KeepPreviousState("a-1", "a-9"), "target changed resets state");
        TestHarness.AssertFalse(ProbeHysteresis.KeepPreviousState("a-1", null), "no target resets state");
    }

    private static void ProbeStopMirrorsVanillaGuard()
    {
        TestHarness.AssertTrue(ProbeHysteresis.ShouldStopUnit(inOffensive: true, inBattle: false, wlUnderCommander: false, wlScenarioActive: true), "AI unit stops");
        TestHarness.AssertFalse(ProbeHysteresis.ShouldStopUnit(inOffensive: true, inBattle: false, wlUnderCommander: true, wlScenarioActive: true), "W&L under-commander unit is not stopped (vanilla 13840)");
        TestHarness.AssertTrue(ProbeHysteresis.ShouldStopUnit(inOffensive: true, inBattle: false, wlUnderCommander: true, wlScenarioActive: false), "flag ignored outside W&L");
        TestHarness.AssertFalse(ProbeHysteresis.ShouldStopUnit(inOffensive: true, inBattle: true, wlUnderCommander: false, wlScenarioActive: false), "never stop a unit in battle");
        TestHarness.AssertFalse(ProbeHysteresis.ShouldStopUnit(inOffensive: false, inBattle: false, wlUnderCommander: false, wlScenarioActive: false), "already released");
    }

    // ------------------------------------------------------------------
    // C-HardWired
    // ------------------------------------------------------------------

    private static void HardWiredCapitalThreatMirrorsVanilla()
    {
        var enemies = new List<CapitalForceSample> { new CapitalForceSample { Distance = 100f, Strength = 20000f, Morale = 0.8f } };
        var own = new List<CapitalForceSample> { new CapitalForceSample { Distance = 50f, Strength = 30000f, Morale = 1f } };
        var none = new List<CapitalForceSample>();
        TestHarness.AssertTrue(CampaignResponsivenessSignals.EnemyThreatensCapital(enemies, none, 275f, 1500f), "uncovered enemy near capital threatens");
        TestHarness.AssertFalse(CampaignResponsivenessSignals.EnemyThreatensCapital(enemies, own, 275f, 1500f), "covered by stronger own forces closer than the enemy");
        var far = new List<CapitalForceSample> { new CapitalForceSample { Distance = 400f, Strength = 50000f, Morale = 1f } };
        TestHarness.AssertFalse(CampaignResponsivenessSignals.EnemyThreatensCapital(far, none, 275f, 1500f), "enemy outside vanilla relocation radius is no threat");
    }

    private static void HardWiredCapitalStreakAdvancesPerDay()
    {
        int day = Serial(1, 6, 1862);
        int streak = CampaignResponsivenessSignals.NextCapitalStreak(0, true, -1, day, out int last);
        TestHarness.AssertEqual(1, streak, "first threatened day");
        streak = CampaignResponsivenessSignals.NextCapitalStreak(streak, true, last, day, out last);
        TestHarness.AssertEqual(1, streak, "same day counted once");
        streak = CampaignResponsivenessSignals.NextCapitalStreak(streak, true, last, day + 3, out last);
        TestHarness.AssertEqual(4, streak, "skipped days count");
        streak = CampaignResponsivenessSignals.NextCapitalStreak(streak, false, last, day + 4, out last);
        TestHarness.AssertEqual(0, streak, "threat cleared");
    }

    private static void HardWiredDaysSinceFrontChange()
    {
        int day = Serial(1, 6, 1862);
        TestHarness.AssertEqual(0, CampaignResponsivenessSignals.DaysSince(-1, day), "never stamped");
        TestHarness.AssertEqual(61, CampaignResponsivenessSignals.DaysSince(day, day + 61), "61 days static");
    }

    private static void HardWiredContextUsesLiveLocalSignals()
    {
        var signals = new HistoricalOperationLocalSignals
        {
            HasTarget = true,
            TargetX = 760f,
            TargetZ = 60f,
            TargetTheater = Theater.East,
            LocalOwnStrength = 12000f,
            LocalEnemyStrength = 6000f,
            EnemyThreatensCapital = true
        };
        var context = HistoricalOperationContextBuilder.Build(0, Serial(1, 6, 1862), 3, null, null, null, null, null, null, signals);
        TestHarness.AssertTrue(context.TargetPositionResolves, "position from live signals");
        TestHarness.AssertEqual(12000f, context.TargetLocalOwnStrength, "local own");
        TestHarness.AssertEqual(6000f, context.TargetLocalEnemyStrength, "local enemy");
        TestHarness.AssertTrue(Math.Abs(context.TargetLocalRatio - 2f) < 0.001f, "local ratio");
        TestHarness.AssertTrue(context.EnemyThreatensCapitalCorridor, "capital threat wired");
    }

    private static void HardWiredProfileRadiusNarrowsVanilla()
    {
        var narrow = new HistoricalOperationProfile { OperationId = "x", NearTargetRadius = 275f };
        TestHarness.AssertEqual(275f, HistoricalOperationContextBuilder.NearTargetRadius(narrow, 500f), "profile radius caps a wider vanilla radius");
        TestHarness.AssertEqual(200f, HistoricalOperationContextBuilder.NearTargetRadius(narrow, 200f), "vanilla radius wins when narrower");
        TestHarness.AssertEqual(300f, HistoricalOperationContextBuilder.NearTargetRadius(null, 300f), "generic plans use vanilla");
        var unset = new HistoricalOperationProfile { OperationId = "y", NearTargetRadius = 0f };
        TestHarness.AssertEqual(300f, HistoricalOperationContextBuilder.NearTargetRadius(unset, 300f), "unset profile radius uses vanilla");
        TestHarness.AssertEqual(1f, HistoricalOperationContextBuilder.NearTargetRadius(null, 0f), "never below 1");
    }

    // ------------------------------------------------------------------
    // Cadence
    // ------------------------------------------------------------------

    private static void CadenceSkippedWeeklyGateDetected()
    {
        TestHarness.AssertTrue(ReviewCadenceCatchUp.SkippedWeeklyGate(Serial(5, 6, 1862), Serial(9, 6, 1862)), "day 7 skipped by a 4-day jump");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Serial(8, 6, 1862), Serial(13, 6, 1862)), "no multiple of 7 between 8 and 13");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Serial(5, 6, 1862), Serial(7, 6, 1862)), "landing on day 7 is the normal gate");
    }

    private static void CadenceSkippedWeeklyGateAcrossMonth()
    {
        TestHarness.AssertTrue(ReviewCadenceCatchUp.SkippedWeeklyGate(Serial(27, 5, 1862), Serial(2, 6, 1862)), "day 28 of May skipped");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Serial(29, 5, 1862), Serial(2, 6, 1862)), "29..1 has no weekly day");
    }

    private static void CadenceConsecutiveDaysSkipNothing()
    {
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(Serial(6, 6, 1862), Serial(7, 6, 1862)), "consecutive");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedAlternatingGate(Serial(6, 6, 1862), Serial(7, 6, 1862)), "consecutive");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.SkippedWeeklyGate(-1, Serial(9, 6, 1862)), "first review");
    }

    private static void CadenceSkippedAlternatingDayForcesBothAlliances()
    {
        TestHarness.AssertTrue(ReviewCadenceCatchUp.SkippedAlternatingGate(Serial(6, 6, 1862), Serial(8, 6, 1862)), "a whole day skipped");
    }

    private static void CadenceReviewOnlyWhenCampaignLive()
    {
        TestHarness.AssertTrue(ReviewCadenceCatchUp.CampaignIsLive(2), "currentoperation 2 = campaign running");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.CampaignIsLive(5), "returning from battle, save not applied yet");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.CampaignIsLive(6), "loading a save");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.CampaignIsLive(0), "new campaign before frame 30");
        TestHarness.AssertFalse(ReviewCadenceCatchUp.CampaignIsLive(4), "launching a battle");
    }

    private static void PerStaleSidecarPlanIsReplanned()
    {
        var stale = new OperationalPlanDto { OperationId = "csa-valley-disruption", CurrentPhaseIndex = 0, PlanDeadlineMonth = 12, PlanDeadlineYear = 1861 };
        stale.Phases.Add(new PhaseDto { PhaseId = "develop-contact", TargetObjectiveId = 31, Transition = "TargetEngaged", DeadlineMonth = 12, DeadlineYear = 1861 });
        TestHarness.AssertTrue(SidecarDtoCodec.PlanFromDto(stale, 1).IsDirty, "plan targeting objective 31 under the old catalog must replan");

        var retired = new OperationalPlanDto { OperationId = "csa-capital-defense", CurrentPhaseIndex = 0, PlanDeadlineMonth = 12, PlanDeadlineYear = 1861 };
        retired.Phases.Add(new PhaseDto { PhaseId = "screen-and-delay", TargetObjectiveId = 4, Transition = "TargetEngaged", DeadlineMonth = 12, DeadlineYear = 1861 });
        TestHarness.AssertTrue(SidecarDtoCodec.PlanFromDto(retired, 1).IsDirty, "plan of a retired operation must replan");

        var current = new OperationalPlanDto { OperationId = "csa-valley-disruption", CurrentPhaseIndex = 0, PlanDeadlineMonth = 12, PlanDeadlineYear = 1861 };
        current.Phases.Add(new PhaseDto { PhaseId = "develop-contact", TargetObjectiveId = 32, Transition = "TargetEngaged", DeadlineMonth = 12, DeadlineYear = 1861 });
        TestHarness.AssertFalse(SidecarDtoCodec.PlanFromDto(current, 1).IsDirty, "plan matching the catalog is kept");
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

// Mirrors vanilla CampaignObjective.InitializeObjectives (decompile 178635-178680): a count
// line, then 20 lines per objective: id, name, tooltip headline, tooltip, chapters
// ("001;002;" -> Substring(j*4,3)), alliance, scenarios, valid-from, valid-to, town/IIP
// objects, precampaign ids, prestige, 5 headlines, picture id, picture headline, content.
internal static class CampaignObjectivesDat
{
    internal sealed class Row
    {
        public int Id;
        public string Name;
        public List<int> Chapters = new List<int>();
        public int Alliance;
        public List<string> Scenarios = new List<string>();
        public int ValidFromMonth, ValidFromYear, ValidToMonth, ValidToYear;
        public List<int> Prerequisites = new List<int>();
    }

    internal static List<Row> Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        int count = int.Parse(lines[0]);
        var rows = new List<Row>();
        int i = 1;
        for (int k = 0; k < count; k++, i += 20)
        {
            var row = new Row { Id = int.Parse(lines[i]), Name = lines[i + 1], Alliance = int.Parse(lines[i + 5]) };
            string chapters = lines[i + 4];
            for (int j = 0; j < chapters.Length / 4; j++)
                row.Chapters.Add(int.Parse(chapters.Substring(j * 4, 3)));
            foreach (var part in lines[i + 6].Split(';'))
                if (part.Length > 0) row.Scenarios.Add(part);
            ParseDate(lines[i + 7], out row.ValidFromMonth, out row.ValidFromYear);
            ParseDate(lines[i + 8], out row.ValidToMonth, out row.ValidToYear);
            foreach (var part in lines[i + 10].Split(';'))
                if (part.Length > 0) row.Prerequisites.Add(int.Parse(part));
            rows.Add(row);
        }
        return rows;
    }

    private static void ParseDate(string text, out int month, out int year)
    {
        var parts = text.Split('.');
        month = int.Parse(parts[1]);
        year = int.Parse(parts[2]);
    }

    // Config/campaignobjectives.dat (game build 14703111), prose lines replaced by "-".
    internal const string Fixture = @"38
0
Break Confederate Morale
-
-
000;001;002;003;
0
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
0
-
-
1
Break Union Morale
-
-
000;001;002;003;
1
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
3
-
-
2
Capture Montgomery, AL
-
-
000;001;002;003;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
30
-
-
3
Capture Richmond, VA
-
-
000;001;002;003;
0
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
30
-
-
4
Capture Washington, D.C.
-
-
000;001;002;003;
1
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
30
-
-
5
Pre-emptive Action
-
-
000;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
508
-
-
6
Invade the Confederacy
-
-
001;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
537
-
-
7
Win a Major Battle
-
-
001;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
31
-
-
8
Win a Major Battle
-
-
001;
1
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
31
-
-
9
Maintain Support Above 60
-
-
001;
0
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
20
-
-
10
Union Support Below 60
-
-
001;
1
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
32
-
-
11
Capture 10 Forts
-
-
001;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
6
-
-
12
Capture 3 Cities
-
-
001;002;
1
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
538
-
-
13
Invade the North
-
-
001;
1
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
539
-
-
14
Capture Western Virginia
-
-
001;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
540
-
-
15
Capture Kentucky
-
-
001;002;003;
1
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
26
-
-
16
Capture Missouri
-
-
001;002;003;
1
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
26
-
-
17
Cut Confederacy in Half
-
-
001;002;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
34
-
-
18
Win 5 Consecutive Land Battles
-
-
001;002;003;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
0
-
-
19
Win 5 Consecutive Land Battles
-
-
001;002;003;
1
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
3
-
-
20
Win 5 Consecutive Naval Battles
-
-
001;002;003;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
9
-
-
21
Win 5 Consecutive Naval Battles
-
-
001;002;003;
1
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
9
-
-
22
Defeat the British
-
-
001;002;003;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
0
-
-
23
Defeat the French
-
-
001;002;003;
0
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
11
-
-
24
Gain a Major Victory
-
-
001;002;003;
0
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
0
-
-
25
Gain a Major Victory
-
-
001;002;003;
1
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
3
-
-
26
Maintain Morale Above 50
-
-
002;
0
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
25
-
-
27
Union Morale below 50
-
-
002;
1
001;002;
01.01.1850
01.01.1870


0
-
-
-
-
-
541
-
-
28
Inflict 50,000 casualties
-
-
003;
1
001;
01.01.1850
01.01.1870


0
-
-
-
-
-
33
-
-
29
West Virginia
-
-
001;002;003;
0
002;
01.01.1850
01.01.1870
Grafton;Charleston;Wheeling;Parkersburg;Town_lewisburg;Town_beverly;Town_Romney;

500
-
-
-
-
-
540
-
-
30
West Virginia
-
-
001;002;003;
1
002;
01.01.1850
01.01.1870
Charleston;Grafton;Town_lewisburg;Town_Romney;

1000
-
-
-
-
-
538
-
-
31
Shenandoah Valley
-
-
001;002;003;
0
002;
01.01.1850
01.01.1870
Winchester;Town_martinsburg;Town_strasburg;Town_newmarket;

1000
-
-
-
-
-
537
-
-
32
Baltimore & Ohio Lines
-
-
001;002;003;
1
002;
01.01.1850
01.01.1870
Cumberland;Hagerstown;Town_BerkeleySpr;Town_martinsburg;Town_harpersfrry;

1000
-
-
-
-
-
535
-
-
33
Maryland Campaign
-
-
001;002;003;
1
002;
01.01.1862
01.01.1870
Hagerstown;Frederick;
032;
1000
-
-
-
-
-
538
-
-
34
Pennsylvania Campaign
-
-
001;002;003;
1
002;
01.01.1863
01.01.1870
Hagerstown;Harrisburg;Town_Chambersburg;Town_Carlisle;Town_Gettysburg;Town_York;
032;
2500
-
-
-
-
-
538
-
-
35
Coastal Operations
-
-
001;002;
0
002;
01.01.1862
01.01.1870
NewBern;Plymouth;

1000
-
-
-
-
-
537
-
-
36
Salt Wars
-
-
002;003;
0
002;
01.01.1862
01.01.1870
Town_saltville;
029;
500
-
-
-
-
-
537
-
-
37
Invasion of Norfolk
-
-
001;002;003;
0
002;
01.01.1862
01.01.1870
Norfolk;Town_portsmouth;Town_suffolk;

1000
-
-
-
-
-
67
-
-";
}
