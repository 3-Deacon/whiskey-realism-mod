using System;
using WhiskeyRealism.Strategic;
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

    private static void CoordinatorPassesTheChapterToTheEra()
    {
        string src = Flat("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        AssertContains(src, ".ResetToDate(gameMonth, gameYear, SafePolicyChapter());", "fresh state derives from date and chapter");
        AssertContains(src, "cicEra.CheckTransition(month, year, cicWs.VicksburgFallen, SafePolicyChapter());", "player CIC alliance");
        AssertContains(src, "era.CheckTransition(month, year, ws.VicksburgFallen, SafePolicyChapter());", "AI alliance");
        AssertContains(src, "else if (firstCall) { // Sidecar migration", "a seeded era is migrated on the first call only");
        AssertContains(src, "_work.Eras[a]?.ResetStaleLateWar(gameMonth, gameYear, SafePolicyChapter());", "stale calendar late war pulled back");
    }

    // The stage is vanilla's chapter now; a project's date from is a calendar date (datefrom[alliance]).
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
}
