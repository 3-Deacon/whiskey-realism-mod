using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Policies;
using static TestHarness;

// Chapter fixes (docs/superpowers/plans/2026-10-15-chapters.md): the late-war era stage follows
// vanilla's chapter 3 (Policy.CheckForChapterUpdate 211663-211667), not the 1864 calendar.
internal static class ChapterFixTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("chapter late war starts at chapter 3 only", LateWarStartsAtChapterThree),
            ("chapter era is not late war in 1864 before the election chapter", EraNotLateWarIn1864BeforeChapterThree),
            ("chapter era stays out of late war in 1865 when the election was lost", EraStaysOutOfLateWarWhenElectionLost),
            ("chapter era reaches late war at chapter 3", EraReachesLateWarAtChapterThree),
            ("chapter era keeps calendar stages before the election", EraKeepsCalendarStagesBeforeElection),
            ("chapter era unknown chapter is never late war", EraUnknownChapterIsNeverLateWar),
            ("chapter era w&l 1864 start waits for chapter 3", EraWlStartIn1864WaitsForChapterThree),
            ("chapter era check transition advances only on chapter 3", CheckTransitionAdvancesOnlyOnChapterThree),
            ("chapter era reset to date takes the chapter", ResetToDateTakesTheChapter),
            ("chapter era stale calendar late war is pulled back when the chapter is known", StaleCalendarLateWarPulledBack),
            ("chapter era stale late war keeps chapter 3 and unknown chapter", StaleLateWarKeepsChapterThreeAndUnknown),
            ("chapter era stale late war leaves other stages alone", StaleLateWarLeavesOtherStagesAlone),
            ("chapter project window uses the calendar year for a project dated 1864", ProjectDatedYearWindowUsesTheCalendar),
            ("chapter project window flags a project dated 1864 before 1864", ProjectDatedBeforeItsYearIsOutOfWindow),
            ("chapter project selection patch reads the campaign year once and hands it to the facts", ProjectPatchPassesTheCampaignYear),
            ("chapter election countdown counts days to the 9 Nov 1864 verdict", ElectionCountsDaysToTheVerdict),
            ("chapter election countdown ends after the verdict day and for bad dates", ElectionCountdownEndsAfterVerdictAndForBadDates),
            ("chapter election boost grows as the deadline nears", ElectionBoostGrowsTowardTheDeadline),
            ("chapter election boost needs a morale shortfall and an open ramp", ElectionBoostNeedsShortfallAndOpenRamp),
            ("chapter election boost ignores non-finite input", ElectionBoostIgnoresNonFiniteInput),
            ("chapter election pressure raises the union morale need only", ElectionRaisesTheUnionMoraleNeedOnly),
            ("chapter election pressure stays in unit range and off without a verdict day", ElectionNeedStaysInRangeAndOffWithoutVerdict),
            ("chapter election pressure reaches the policy score of a morale policy", ElectionReachesThePolicyScore),
            ("chapter election runtime reads objective 26 and threads the review date", ElectionRuntimeWiring),
            ("chapter wait treats only a negative chapter as unknown", ChapterKnownOnlyAfterVanillaUpdate),
            ("chapter wait gates the cadence hook before it consumes a notify", HookWaitsForVanillaChapterBeforeNotifying),
            ("chapter wait is bounded: it ends at a frame limit if the chapter never arrives", ChapterWaitIsBounded),
            ("chapter wait replaces whiskey's own chapter update call", WhiskeyNoLongerRunsTheChapterUpdate),
            ("chapter militia act one is the scripted war start while the chapter is pre-war", ScriptedWarStartIsPreWarOnly),
            ("chapter militia act one leaves vanilla's fallback walk with or without a need vector", WarStartLeavesTheFallbackWalk),
            ("chapter militia act one hold follows the selection flag and never an emergency", WarStartHoldFollowsTheFlag),
            ("chapter militia act one pick filter and fallback hold share one predicate", WarStartPredicateIsShared),
            ("chapter fiscal and construction inputs carry no chapter nobody reads", FiscalAndConstructionInputsCarryNoChapter),
            ("chapter late operation is available May to October 1864 at chapter two", LateOperationAvailableInSummer1864),
            ("chapter late operation survives a lost election", LateOperationSurvivesALostElection),
            ("chapter late operation is not available before May 1864", LateOperationNotBeforeMay1864),
            ("chapter late operation needs the decisive era or later", LateOperationNeedsDecisiveEraOrLater),
            ("chapter every other operation keeps its single era so its match set is unchanged", OtherOperationsKeepTheirSingleEra),
            ("chapter grand strategy profile is decisive before chapter three", GrandStrategyStaysDecisiveBeforeChapterThree),
            ("chapter era callers pass the live chapter and migrate a seeded sidecar era once", CoordinatorPassesTheChapterToTheEra),
        });
    }

    private static void LateWarStartsAtChapterThree()
    {
        AssertEqual(3, EraStageManager.LateWarChapter, "vanilla chapter 3 is Lincoln re-elected");
        AssertFalse(EraStageManager.IsLateWar(2), "chapter 2");
        AssertTrue(EraStageManager.IsLateWar(3), "chapter 3");
        AssertFalse(EraStageManager.IsLateWar(-1), "unknown chapter");
        AssertFalse(EraStageManager.IsLateWar(0), "pre-war");
    }

    private static void EraNotLateWarIn1864BeforeChapterThree()
    {
        AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(1, 1864, false, 2), "1 Jan 1864 is no longer total war");
        AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(8, 1864, false, 2), "Atlanta summer, still chapter 2");
    }

    private static void EraStaysOutOfLateWarWhenElectionLost()
    {
        // Lincoln loses: the chapter never reaches 3 (objective 26 unmet, 179393), the campaign ends 4 Mar 1865.
        AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(1, 1865, false, 2), "Jan 1865");
        AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(3, 1865, false, 2), "Mar 1865");
    }

    private static void EraReachesLateWarAtChapterThree()
    {
        AssertEqual(EraStage.TotalWar1864, EraStageManager.ForDate(11, 1864, false, 3), "9 Nov 1864");
        AssertEqual(EraStage.TotalWar1864, EraStageManager.ForDate(2, 1865, false, 3), "1865");
    }

    private static void EraKeepsCalendarStagesBeforeElection()
    {
        AssertEqual(EraStage.Amateur1861, EraStageManager.ForDate(4, 1861, false, 0), "pre-war");
        AssertEqual(EraStage.Amateur1861, EraStageManager.ForDate(12, 1861, false, 1), "1861");
        AssertEqual(EraStage.Operational1862, EraStageManager.ForDate(1, 1862, false, 1), "1862 chapter 1");
        AssertEqual(EraStage.Operational1862, EraStageManager.ForDate(12, 1862, false, 2), "1862 chapter 2");
        AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(1, 1863, false, 2), "1863");
        AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(3, 1863, true, 2), "Vicksburg early keeps 1863");
        AssertEqual(EraStage.Operational1862, EraStageManager.ForDate(3, 1862, true, 2), "Vicksburg pull only applies in 1863");
    }

    private static void EraUnknownChapterIsNeverLateWar()
    {
        AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(6, 1864, false, -1), "chapter -1");
    }

    private static void EraWlStartIn1864WaitsForChapterThree()
    {
        // W&L forces chapter 1 on every call (211671) and then applies the same 2 and 3 rules.
        AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(6, 1864, false, 1), "forced chapter 1 in 1864");
        AssertEqual(EraStage.TotalWar1864, EraStageManager.ForDate(12, 1864, false, 3), "chapter 3 reachable in W&L");
    }

    private static void CheckTransitionAdvancesOnlyOnChapterThree()
    {
        var era = new EraStageManager { Stage = EraStage.Decisive1863 };
        era.CheckTransition(6, 1864, false, 2);
        AssertEqual(EraStage.Decisive1863, era.Stage, "1864, chapter 2");
        era.CheckTransition(11, 1864, false, 3);
        AssertEqual(EraStage.TotalWar1864, era.Stage, "chapter 3");
        era.CheckTransition(11, 1864, false, 2);
        AssertEqual(EraStage.TotalWar1864, era.Stage, "forward only: a later lower chapter reading does not regress it");
    }

    private static void ResetToDateTakesTheChapter()
    {
        var era = new EraStageManager { Stage = EraStage.Amateur1861 };
        era.ResetToDate(3, 1865, 3);
        AssertEqual(EraStage.TotalWar1864, era.Stage, "late war save");
        era.ResetToDate(6, 1864, 2);
        AssertEqual(EraStage.Decisive1863, era.Stage, "mid 1864 save");
    }

    private static void StaleCalendarLateWarPulledBack()
    {
        // A sidecar written before this fix holds TotalWar1864 from 1 Jan 1864 (calendar rule).
        var era = new EraStageManager { Stage = EraStage.TotalWar1864 };
        AssertTrue(era.ResetStaleLateWar(6, 1864, 2), "pulled back");
        AssertEqual(EraStage.Decisive1863, era.Stage, "chapter 2 in June 1864");
    }

    private static void StaleLateWarKeepsChapterThreeAndUnknown()
    {
        var late = new EraStageManager { Stage = EraStage.TotalWar1864 };
        AssertFalse(late.ResetStaleLateWar(12, 1864, 3), "chapter 3 is genuine");
        AssertEqual(EraStage.TotalWar1864, late.Stage, "kept");
        var unknown = new EraStageManager { Stage = EraStage.TotalWar1864 };
        AssertFalse(unknown.ResetStaleLateWar(6, 1864, -1), "unknown chapter: wait");
        AssertEqual(EraStage.TotalWar1864, unknown.Stage, "kept");
    }

    private static void StaleLateWarLeavesOtherStagesAlone()
    {
        var era = new EraStageManager { Stage = EraStage.Operational1862 };
        AssertFalse(era.ResetStaleLateWar(6, 1864, 2), "not late war");
        AssertEqual(EraStage.Operational1862, era.Stage, "unchanged");
    }

    private static string Flat(string path) =>
        System.Text.RegularExpressions.Regex.Replace(TestFixtures.ReadRepoFile(path), @"\s+", " ");

    private static string MethodText(string source, string startSignature, string endSignature)
    {
        int a = source.IndexOf(startSignature, StringComparison.Ordinal);
        int b = source.IndexOf(endSignature, a + 1, StringComparison.Ordinal);
        AssertTrue(a >= 0 && b > a, "method text " + startSignature);
        return source.Substring(a, b - a);
    }

    private static void CoordinatorPassesTheChapterToTheEra()
    {
        string src = Flat("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        AssertContains(src, ".ResetToDate(gameMonth, gameYear, SafePolicyChapter());", "fresh state derives from date and chapter");
        AssertContains(src, "cicEra.CheckTransition(month, year, cicWs.VicksburgFallen, SafePolicyChapter());", "player CIC alliance");
        AssertContains(src, "era.CheckTransition(month, year, ws.VicksburgFallen, SafePolicyChapter());", "AI alliance");

        // A loaded save restores LastSeenMonth (ApplySnapshot), so `firstCall` (LastSeenMonth < 0) is
        // false on every save written after its first review: the sidecar migration must not hang on it.
        string notify = MethodText(src, "public void NotifyDateAdvanced(", "private bool _forcedAllSuccession;");
        AssertContains(notify, "if (_eraMigrationPending) { _eraMigrationPending = false;", "one-shot consumed on the first notify after a load");
        AssertContains(notify, "_work.Eras[a]?.ResetStaleLateWar(gameMonth, gameYear, SafePolicyChapter());", "stale calendar late war pulled back");
        AssertTrue(notify.IndexOf("_eraMigrationPending", StringComparison.Ordinal) < notify.IndexOf("bool firstCall", StringComparison.Ordinal),
            "the migration is decided before, and independently of, firstCall");
        AssertTrue(notify.IndexOf("InitializeFromGameState();", StringComparison.Ordinal) < notify.IndexOf("_eraMigrationPending", StringComparison.Ordinal),
            "after the drain and the initialisation");
        AssertTrue(notify.IndexOf("_eraMigrationPending", StringComparison.Ordinal) < notify.IndexOf("OnMonthlyTick(", StringComparison.Ordinal),
            "before the review reads the era");
        AssertContains(MethodText(src, "private void ApplySnapshot(", "PublishStrategicState();"), "if (snap.Eras[alliance].HasValue) { _erasSeededFromSave = true; _eraMigrationPending = true; }", "a sidecar era arms the migration");
        AssertContains(MethodText(src, "public void ResetAll(", "private CIC BuildCICForAlliance"), "_eraMigrationPending = false;", "a reset disarms it");
    }

    private static WhiskeyRealism.Strategic.Projects.ProjectDoctrineDecision SelectWithVanilla1864Project(int campaignYear)
    {
        var signals = new WhiskeyRealism.Strategic.Projects.ProjectDoctrineSignals
        {
            Alliance = 0,
            Era = EraStage.Decisive1863,
            FiscalPosture = WhiskeyRealism.Strategic.Fiscal.FiscalPosture.BalancedWar,
            NavalDeficit = 1f
        };
        var candidates = new[] { new ProjectCandidateInput { ProjectId = 31, SubsidyType = 4, VanillaWeight = 0.2f } };
        return WhiskeyRealism.Strategic.Projects.ProjectDoctrineScorer.Select(
            GrandStrategyRegistry.Resolve(0, EraStage.Decisive1863),
            signals, subsidyType: 4, vanillaProjectId: 35, vanillaWeight: 1f, candidates: candidates, fiscalWeight: null,
            runtimeFacts: id => new WhiskeyRealism.Strategic.Projects.ProjectRuntimeFacts
            {
                ProjectId = id,
                SubsidyLane = 4,
                Cost = 1000f,
                DateFromKnown = id == 35,
                DateFromYear = id == 35 ? 1864 : 0,
                DateFromMonth = 1,
                DateFromDay = 1,
                CampaignYear = campaignYear
            },
            fundingAvailable: 0f, netFundingPerDay: 0f, constructionCurrentlyWins: false);
    }

    private static void ProjectDatedYearWindowUsesTheCalendar()
    {
        // June 1864, chapter 2 (era Decisive1863): vanilla's own 1864 pick is available, not out of window.
        var decision = SelectWithVanilla1864Project(1864);
        AssertTrue(decision.Reason != "out-of-window-vanilla", "a project dated 1864 is in window in 1864 whatever the era");
    }

    private static void ProjectDatedBeforeItsYearIsOutOfWindow()
    {
        var decision = SelectWithVanilla1864Project(1863);
        AssertEqual("out-of-window-vanilla", decision.Reason, "a project dated 1864 is out of window in 1863");
    }

    private static void ProjectPatchPassesTheCampaignYear()
    {
        string src = Flat("src/WhiskeyRealism/Patches/ProjectSelectionPatch.cs");
        AssertContains(src, "int campaignYear = ReadCampaignYear();", "read once per selection");
        AssertContains(src, "projectId => BuildRuntimeFacts(projectId, subsidyType, alliance, campaignYear),", "handed to every facts read");
        AssertContains(src, "CampaignYear = campaignYear", "stored on the facts");
    }

    // ---- election pressure (objective 26, 179386-179418) ----

    private static void ElectionCountsDaysToTheVerdict()
    {
        // Vanilla measures morale while the date is before 9 Nov 1864 + 1 day (179393-179394), so
        // 9 Nov is the last measured day.
        AssertEqual(0, ElectionMorale.DaysToVerdict(9, 11, 1864), "verdict day");
        AssertEqual(8, ElectionMorale.DaysToVerdict(1, 11, 1864), "1 Nov");
        AssertEqual(161, ElectionMorale.DaysToVerdict(1, 6, 1864), "1 Jun");
    }

    private static void ElectionCountdownEndsAfterVerdictAndForBadDates()
    {
        AssertEqual(ElectionMorale.NoVerdictAhead, ElectionMorale.DaysToVerdict(10, 11, 1864), "day after the verdict");
        AssertEqual(ElectionMorale.NoVerdictAhead, ElectionMorale.DaysToVerdict(1, 1, 1865), "1865");
        AssertEqual(ElectionMorale.NoVerdictAhead, ElectionMorale.DaysToVerdict(0, 0, 0), "vanilla cannot say");
        AssertEqual(ElectionMorale.NoVerdictAhead, ElectionMorale.DaysToVerdict(31, 2, 1864), "no 31 Feb");
        AssertEqual(ElectionMorale.NoVerdictAhead, ElectionMorale.DaysToVerdict(1, 13, 1864), "month 13");
        AssertEqual(ElectionMorale.NoVerdictAhead, ElectionMorale.DaysToVerdict(1, 6, 10000), "year out of DateTime range");
        AssertTrue(ElectionMorale.DaysToVerdict(29, 2, 1864) > 0, "1864 is a leap year");
    }

    private static void ElectionBoostGrowsTowardTheDeadline()
    {
        AssertNear(0.2f, ElectionMorale.Boost(0.45f, 0.5f, 0), 1e-5f, "verdict day, 0.45 against 0.50 (+0.05 margin)");
        AssertNear(0.1f, ElectionMorale.Boost(0.45f, 0.5f, ElectionMorale.RampDays / 2), 1e-5f, "half the ramp left");
        float far = ElectionMorale.Boost(0.45f, 0.5f, 100);
        float near = ElectionMorale.Boost(0.45f, 0.5f, 10);
        AssertTrue(near > far && far > 0f, "closer is stronger");
        AssertEqual(ElectionMorale.RampDays, 150, "the ramp is Jun-Nov");
        AssertNear(0.05f, ElectionMorale.SafetyMargin, 1e-6f, "margin pin");
    }

    private static void ElectionBoostNeedsShortfallAndOpenRamp()
    {
        AssertNear(0f, ElectionMorale.Boost(0.45f, 0.5f, ElectionMorale.RampDays), 1e-6f, "ramp opens 150 days out");
        AssertNear(0f, ElectionMorale.Boost(0.45f, 0.5f, ElectionMorale.RampDays + 40), 1e-6f, "before the ramp");
        AssertNear(0f, ElectionMorale.Boost(0.45f, 0.5f, ElectionMorale.NoVerdictAhead), 1e-6f, "after the verdict");
        AssertNear(0f, ElectionMorale.Boost(0.55f, 0.5f, 0), 1e-6f, "at threshold plus margin there is no shortfall");
        AssertNear(0f, ElectionMorale.Boost(0.9f, 0.5f, 0), 1e-6f, "comfortable");
        AssertTrue(ElectionMorale.Boost(0.5f, 0.5f, 0) > 0f, "just meeting the threshold still leaves the margin to cover");
        AssertNear(1f, ElectionMorale.Boost(-1f, 0.5f, 0), 1e-6f, "capped at 1");
    }

    private static void ElectionBoostIgnoresNonFiniteInput()
    {
        AssertNear(0f, ElectionMorale.Boost(float.NaN, 0.5f, 0), 1e-6f, "nan morale");
        AssertNear(0f, ElectionMorale.Boost(0.4f, float.NaN, 0), 1e-6f, "nan threshold");
        AssertNear(0f, ElectionMorale.Boost(0.4f, 0f, 0), 1e-6f, "zero threshold");
        AssertNear(0f, ElectionMorale.Boost(float.NegativeInfinity, float.PositiveInfinity, 0), 1e-6f, "infinite input");
    }

    private static NeedSnapshot ElectionSnapshot(int alliance, float morale, int daysToVerdict)
    {
        return new NeedSnapshot
        {
            Alliance = alliance,
            NationalMorale = morale,
            ElectionMoraleThreshold = 0.5f,
            ElectionDaysToVerdict = daysToVerdict
        };
    }

    private static void ElectionRaisesTheUnionMoraleNeedOnly()
    {
        AssertNear(0.55f, CampaignNeedBuilder.Raw(ElectionSnapshot(0, 0.45f, ElectionMorale.NoVerdictAhead))[NeedKind.Morale], 1e-5f, "no election pending: 1 - morale");
        AssertNear(0.75f, CampaignNeedBuilder.Raw(ElectionSnapshot(0, 0.45f, 0))[NeedKind.Morale], 1e-5f, "Union on the verdict day");
        AssertNear(0.55f, CampaignNeedBuilder.Raw(ElectionSnapshot(1, 0.45f, 0))[NeedKind.Morale], 1e-5f, "the CSA's morale need is not the election's");
        AssertNear(0.1f, CampaignNeedBuilder.Raw(ElectionSnapshot(0, 0.9f, 0))[NeedKind.Morale], 1e-5f, "comfortable morale: no boost");
    }

    private static void ElectionNeedStaysInRangeAndOffWithoutVerdict()
    {
        AssertNear(1f, CampaignNeedBuilder.Raw(ElectionSnapshot(0, 0.05f, 0))[NeedKind.Morale], 1e-6f, "clamped to 1");
        var plain = new NeedSnapshot { Alliance = 0, NationalMorale = 0.45f };
        AssertEqual(ElectionMorale.NoVerdictAhead, plain.ElectionDaysToVerdict, "a snapshot starts with no election");
        AssertNear(0.55f, CampaignNeedBuilder.Raw(plain)[NeedKind.Morale], 1e-5f, "default snapshot");
    }

    private static void ElectionReachesThePolicyScore()
    {
        // Policy 44 (Abolition of Slavery) meets a Morale need (PolicyEffectCatalog); the plan
        // consumes the same NeedVector, so the election reaches the pick with no new wiring.
        var facts = new WhiskeyRealism.Strategic.Policies.PolicyCandidateFacts { PolicyId = 44, IsAct = true, ResearchDuration = 0.1f };
        var profile = GrandStrategyRegistry.Resolve(0, EraStage.Decisive1863);
        var calm = WhiskeyRealism.Strategic.Policies.PolicyNeedScorer.Score(
            facts, CampaignNeedBuilder.Raw(ElectionSnapshot(0, 0.45f, ElectionMorale.NoVerdictAhead)), new int[0], profile, 0, 0, id => new int[0]);
        var pressed = WhiskeyRealism.Strategic.Policies.PolicyNeedScorer.Score(
            facts, CampaignNeedBuilder.Raw(ElectionSnapshot(0, 0.45f, 0)), new int[0], profile, 0, 0, id => new int[0]);
        AssertTrue(pressed.Need > calm.Need, "the election raises what a morale policy is worth");
    }

    private static void ElectionRuntimeWiring()
    {
        string runtime = Flat("src/WhiskeyRealism/Strategic/CampaignNeedRuntime.cs");
        AssertContains(runtime, "CampaignObjective.GetObjectiveDeactivated(ElectionMorale.ObjectiveId)", "vanilla decides whether objective 26 is live");
        AssertContains(runtime, "s.ElectionDaysToVerdict = ElectionMorale.DaysToVerdict(day, month, year);", "countdown from the review's date");
        AssertContains(runtime, "s.ElectionMoraleThreshold = GamePrefs.lincolnmaintainmoraleabove;", "vanilla's own threshold");
        string coordinator = Flat("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        AssertContains(coordinator, "CampaignNeedRuntime.Read(alliance, era, day, month, year,", "the review date reaches the read");
    }

    // ---- waiting for vanilla's chapter update instead of calling it ----

    private static void ChapterKnownOnlyAfterVanillaUpdate()
    {
        // Policy.CurrentChapter is -1 until CheckForChapterUpdate runs (reset on frame 1 at 29857,
        // first call on frame 50 at 30146-30149); that method sets it to 0 or more unconditionally (211611).
        AssertFalse(ReviewCadenceCatchUp.ChapterKnown(-1), "frames 1-49 of a new campaign");
        AssertTrue(ReviewCadenceCatchUp.ChapterKnown(0), "pre-war chapter");
        AssertTrue(ReviewCadenceCatchUp.ChapterKnown(1), "chapter 1 (W&L is always 1)");
        AssertTrue(ReviewCadenceCatchUp.ChapterKnown(3), "chapter 3");
    }

    private static void ChapterWaitIsBounded()
    {
        // Vanilla's frame-50 call (30146-30149; CampaignController.Awake zeroes the frame on every campaign
        // scene, 29726, so a new campaign, a load and a battle return all reach it) and BattleUnits.Update's
        // retries (79323) set the chapter to 0 or more first thing (211611). The wait still never outlives
        // ChapterWaitLastFrame, so a campaign where that never happened is not left without a review.
        AssertTrue(ReviewCadenceCatchUp.ChapterWaitLastFrame > 50, "the limit is after vanilla's frame-50 call");
        AssertFalse(ReviewCadenceCatchUp.ChapterWaitOver(-1, 49), "before vanilla's call");
        AssertFalse(ReviewCadenceCatchUp.ChapterWaitOver(-1, ReviewCadenceCatchUp.ChapterWaitLastFrame), "still within the limit");
        AssertTrue(ReviewCadenceCatchUp.ChapterWaitOver(-1, ReviewCadenceCatchUp.ChapterWaitLastFrame + 1), "past the limit the review proceeds with an unknown chapter");
        AssertTrue(ReviewCadenceCatchUp.ChapterWaitOver(0, 10), "a known chapter ends the wait at any frame");
        AssertTrue(ReviewCadenceCatchUp.ChapterWaitOver(2, 0), "known chapter, frame 0");
    }

    private static void HookWaitsForVanillaChapterBeforeNotifying()
    {
        string hook = Flat("src/WhiskeyRealism/Patches/MonthlyTickHookPatch.cs");
        AssertContains(hook, "ReviewCadenceCatchUp.ChapterWaitOver(Policy.CurrentChapter, GameVars.frame)", "the hook reads vanilla's chapter");
        int live = hook.IndexOf("CampaignIsLive(SceneManagement.currentoperation)", StringComparison.Ordinal);
        int wait = hook.IndexOf("ReviewCadenceCatchUp.ChapterWaitOver(Policy.CurrentChapter, GameVars.frame)", StringComparison.Ordinal);
        int gate = hook.IndexOf("_startupGate.ShouldNotify(dateChanged, runtimeReady)", StringComparison.Ordinal);
        int pump = hook.IndexOf("PumpDailyReview()", StringComparison.Ordinal);
        AssertTrue(live >= 0 && live < wait, "campaign live first");
        AssertTrue(wait < gate, "the wait comes before a notify can be consumed");
        AssertTrue(wait < pump, "and before any pump");
        AssertContains(hook, "OnceLog.Info(\"strategic-cadence:chapter-wait\"", "one bounded line");
        AssertContains(hook, "OnceLog.Warning(\"strategic-cadence:chapter-wait-expired\"", "the expiry is logged once");
    }

    private static void WhiskeyNoLongerRunsTheChapterUpdate()
    {
        // Calling CheckForChapterUpdate from the monthly tick could do the -1 to N transition before
        // vanilla's frame-50 call (newspaper, chapter video, policy 36, pre-war armies) and a bare catch
        // could swallow a half-finished one (Policy.newspaper is assigned in Policy.Initialize, 211276).
        string coordinator = Flat("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        AssertFalse(coordinator.Contains("CheckForChapterUpdate\")"), "no reflection call into vanilla's chapter update");
        AssertFalse(coordinator.Contains("ForceChapterUpdate"), "the helper is gone");
    }

    // ---- Militia Act I (policy 36) before the war ----

    // Union personalities 4 (Militaristic) and 7 (Abolitionist) list 36 (Campaigns/001/A/aipersonalities.dat).
    private static readonly int[] UnionListWith36 = { 22, 35, 36, 41, 33 };

    private static void ScriptedWarStartIsPreWarOnly()
    {
        AssertTrue(PolicyEligibility.IsScriptedWarStart(36, -1), "unknown chapter");
        AssertTrue(PolicyEligibility.IsScriptedWarStart(36, 0), "pre-war chapter 0");
        AssertFalse(PolicyEligibility.IsScriptedWarStart(36, 1), "war started");
        AssertFalse(PolicyEligibility.IsScriptedWarStart(136, 0), "CSA Militia Act I is not scripted");
        AssertFalse(PolicyEligibility.IsScriptedWarStart(37, 0), "Militia Act II");
        AssertTrue(PolicyEligibility.HoldsScriptedWarStart(0), "chapter 0");
        AssertFalse(PolicyEligibility.HoldsScriptedWarStart(2), "chapter 2");
    }

    private static void WarStartLeavesTheFallbackWalk()
    {
        var calm = new NeedVector();
        var list = new System.Collections.Generic.List<int>(UnionListWith36);
        var held = PolicyListOverride.HeldIdsToRemove(calm, list, 0);
        AssertTrue(held.Contains(36), "pre-war: vanilla's gate (ReferedChapter 0 <= 0) would pick it, Whiskey's pick filter will not");
        AssertTrue(held.Contains(33), "the need-held irreversible act still leaves");
        AssertEqual(36, held[0], "list order: 36 comes before 33 in the personality list");
        AssertSequence(new[] { 36 }, PolicyListOverride.HeldIdsToRemove(null, list, 0), "no need vector yet: the war start is still held");
        AssertSequence(new int[0], PolicyListOverride.HeldIdsToRemove(null, list, 1), "after the war starts nothing is held without a need vector");
        AssertFalse(PolicyListOverride.HeldIdsToRemove(calm, list, 1).Contains(36), "chapter 1: 36 is a normal act");
        AssertEqual(0, PolicyListOverride.HeldIdsToRemove(null, null, 0).Count, "null list");
    }

    private static void AssertSequence(int[] expected, System.Collections.Generic.IList<int> actual, string label)
    {
        AssertEqual(expected.Length, actual.Count, label + " count");
        for (int i = 0; i < expected.Length; i++)
            AssertEqual(expected[i], actual[i], label + " [" + i + "]");
    }

    private static PolicyCallFacts WarFacts(bool warStartHeld, bool need = false, bool selection = true, bool emergency = false, bool research = false)
    {
        return new PolicyCallFacts
        {
            Emergency = emergency,
            ResearchInProgress = research,
            SelectionEnabled = selection,
            HasNeed = need,
            WarStartHeld = warStartHeld
        };
    }

    private static void WarStartHoldFollowsTheFlag()
    {
        Func<int> noPick = () => -1;
        Func<int, bool> neverStarts = id => false;
        AssertEqual(PolicyCallAction.HoldFallback, PolicyListOverride.Decide(WarFacts(true), noPick, neverStarts), "pre-war, no need vector: hold the war start");
        AssertEqual(PolicyCallAction.Vanilla, PolicyListOverride.Decide(WarFacts(false), noPick, neverStarts), "after the war starts and no need vector: vanilla");
        AssertEqual(PolicyCallAction.HoldFallback, PolicyListOverride.Decide(WarFacts(false, need: true), noPick, neverStarts), "need vector: hold as before");
        AssertEqual(PolicyCallAction.Vanilla, PolicyListOverride.Decide(WarFacts(true, selection: false), noPick, neverStarts), "selection off is the rollback to vanilla");
        AssertEqual(PolicyCallAction.Vanilla, PolicyListOverride.Decide(WarFacts(true, emergency: true), noPick, neverStarts), "an emergency never holds");
        AssertEqual(PolicyCallAction.Vanilla, PolicyListOverride.Decide(WarFacts(true, research: true), noPick, neverStarts), "research in progress: vanilla returns at 211061");
        AssertEqual(PolicyCallAction.Pick, PolicyListOverride.Decide(WarFacts(true), () => 22, id => true), "a started pick skips the hold");
    }

    private static void WarStartPredicateIsShared()
    {
        string eligibility = Flat("src/WhiskeyRealism/Strategic/Policies/PolicyEligibility.cs");
        AssertContains(eligibility, "if (IsScriptedWarStart(f.PolicyId, f.CurrentChapter))", "the pick filter uses the predicate");
        string overrides = Flat("src/WhiskeyRealism/Strategic/Policies/PolicyListOverride.cs");
        AssertContains(overrides, "PolicyEligibility.IsScriptedWarStart(id, chapter)", "the fallback hold uses the same predicate");
        string patch = Flat("src/WhiskeyRealism/Patches/PolicySelectionPatch.cs");
        AssertContains(patch, "WarStartHeld = PolicyEligibility.HoldsScriptedWarStart(Policy.CurrentChapter)", "the call facts carry the chapter");
        AssertContains(patch, "PolicyListOverride.HeldIdsToRemove(need, personality.policies, Policy.CurrentChapter)", "the hold removes the war start");
    }

    // ---- dead fields ----

    private static void FiscalAndConstructionInputsCarryNoChapter()
    {
        // FiscalInput.CurrentChapter and ConstructionInput.CurrentChapter were written every review and
        // read by nothing (vanilla's chapter-0 ship and POW-camp gates already hold: 12012, 15155).
        AssertTrue(typeof(WhiskeyRealism.Strategic.Fiscal.FiscalInput).GetField("CurrentChapter") == null, "FiscalInput.CurrentChapter is gone");
        AssertTrue(typeof(WhiskeyRealism.Strategic.Construction.ConstructionInput).GetField("CurrentChapter") == null, "ConstructionInput.CurrentChapter is gone");
        AssertFalse(Flat("src/WhiskeyRealism/Strategic/Fiscal/FiscalRuntime.cs").Contains("SafePolicyChapter"), "fiscal runtime no longer reads the chapter");
        AssertFalse(Flat("src/WhiskeyRealism/Strategic/Construction/ConstructionRuntime.cs").Contains("SafePolicyChapter"), "construction runtime no longer reads the chapter");
    }

    // ---- historical operations are calendar priors; the era stage follows the chapter ----

    private static HistoricalOperationMatch ResolveUnionEast(EraStage era, int chapter, int month, int year)
    {
        AssertTrue(ObjectiveCatalog.TryResolve(3, out var objective), "objective 3");
        var candidate = new HistoricalOperationCandidate { ObjectiveId = 3, Objective = objective, ObjectiveScore = 10f };
        return HistoricalOperationCatalog.Resolve(0, era, chapter, month, year, candidate, null, default(PersonalityVector), null, null);
    }

    private static void LateOperationAvailableInSummer1864()
    {
        // Grant and Sherman opened their campaigns in May 1864; the election (chapter 3) comes in November.
        for (int month = 5; month <= 10; month++)
        {
            var match = ResolveUnionEast(EraStage.Decisive1863, 2, month, 1864);
            AssertEqual(HistoricalOperationMatchKind.Matched, match.Kind, "1864-" + month);
            AssertEqual("union-late-pressure", match.Profile.OperationId, "1864-" + month);
            AssertEqual("matched:chapter-drift", match.Reason, "chapter 2 is before the profile's chapters 3-4, allowed by AllowDateDrift");
        }
        var election = ResolveUnionEast(EraStage.TotalWar1864, 3, 11, 1864);
        AssertEqual("union-late-pressure", election.Profile.OperationId, "after the election");
        AssertEqual("matched", election.Reason, "chapter 3 is in window");
    }

    private static void LateOperationSurvivesALostElection()
    {
        // Chapter stays 2 and the era Decisive1863 (EraStageManager.ForDate); the campaign runs to 4 Mar 1865.
        AssertEqual(EraStage.Decisive1863, EraStageManager.ForDate(1, 1865, false, 2), "era after a lost election");
        foreach (int month in new[] { 11, 12 })
            AssertEqual("union-late-pressure", ResolveUnionEast(EraStage.Decisive1863, 2, month, 1864).Profile.OperationId, "1864-" + month);
        foreach (int month in new[] { 1, 2, 3 })
            AssertEqual("union-late-pressure", ResolveUnionEast(EraStage.Decisive1863, 2, month, 1865).Profile.OperationId, "1865-" + month);
    }

    private static void LateOperationNotBeforeMay1864()
    {
        foreach (int month in new[] { 1, 2, 3, 4 })
            AssertEqual(HistoricalOperationMatchKind.NoProfile, ResolveUnionEast(EraStage.Decisive1863, 2, month, 1864).Kind, "1864-" + month);
        AssertEqual(HistoricalOperationMatchKind.NoProfile, ResolveUnionEast(EraStage.Decisive1863, 2, 12, 1863).Kind, "Dec 1863");
        AssertEqual(HistoricalOperationMatchKind.NoProfile, ResolveUnionEast(EraStage.Decisive1863, 2, 6, 1865 + 1).Kind, "after the profile's window");
        AssertTrue(HistoricalOperationCatalog.TryGetById("union-late-pressure", out var profile), "profile");
        AssertEqual(5, profile.StartMonth, "starts in May");
        AssertEqual(1864, profile.StartYear, "of 1864");
    }

    private static void LateOperationNeedsDecisiveEraOrLater()
    {
        AssertEqual(HistoricalOperationMatchKind.NoProfile, ResolveUnionEast(EraStage.Amateur1861, 2, 6, 1864).Kind, "Amateur");
        AssertEqual(HistoricalOperationMatchKind.NoProfile, ResolveUnionEast(EraStage.Operational1862, 2, 6, 1864).Kind, "Operational");
        AssertTrue(HistoricalOperationCatalog.TryGetById("union-late-pressure", out var profile), "profile");
        AssertEqual(EraStage.Decisive1863, profile.Era, "lowest era");
        AssertEqual(EraStage.TotalWar1864, profile.MaxEra, "highest era");
        AssertEqual(OperationChapterPolicy.AllowDateDrift, profile.ChapterPolicy, "chapter policy unchanged");
    }

    private static void OtherOperationsKeepTheirSingleEra()
    {
        // Matching is `profile.Era <= era <= profile.MaxEra`. With MaxEra == Era that is the old
        // `era == profile.Era`, so these profiles match exactly as before for every era and date.
        foreach (string id in new[] { "union-east-pressure", "csa-valley-disruption", "union-coastal-pressure", "union-western-pressure", "csa-western-depth" })
        {
            AssertTrue(HistoricalOperationCatalog.TryGetById(id, out var profile), id);
            AssertEqual(profile.Era, profile.MaxEra, id + " single era");
        }
    }

    private static void GrandStrategyStaysDecisiveBeforeChapterThree()
    {
        var beforeElection = GrandStrategyRegistry.Resolve(0, EraStageManager.ForDate(8, 1864, false, 2));
        AssertEqual(EraStage.Decisive1863, beforeElection.EraStage, "August 1864, chapter 2");
        var lost = GrandStrategyRegistry.Resolve(0, EraStageManager.ForDate(2, 1865, false, 2));
        AssertEqual(EraStage.Decisive1863, lost.EraStage, "lost election");
        var reelected = GrandStrategyRegistry.Resolve(0, EraStageManager.ForDate(11, 1864, false, 3));
        AssertEqual(EraStage.TotalWar1864, reelected.EraStage, "chapter 3");
    }
}
