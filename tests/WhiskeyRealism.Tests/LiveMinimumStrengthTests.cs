using System;
using System.IO;
using System.Linq;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F5 (D11 requirement-live-min-strength; decision 6): the planner's garrison minimum was a 5000 constant
// that claimed to mirror vanilla's aiminimumstrengthformovement, which is 2000 in 002/A (Config 5000). Its one definition site is now
// TheaterPlan.MinGarrisonStrength, set by TheaterPlanner.Plan from the snapshot's read, with MinStrengthFallback (5000) when the read is not
// positive or not finite and, with Enable Strategic Foresight Picture off, whatever the read says (the rollback never leaves the live
// floor under D2's old presumption). Every reader derives from the plan. Plan: docs/superpowers/plans/2026-10-18-strategic-foresight.md F5;
// cases TC-MIN-* in docs/loop/foresight/test-plan.md.
internal static class LiveMinimumStrengthTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("PlanPublishesTheScenariosMinimum", PlanPublishesTheScenariosMinimum),
            ("FallbackOnlyWhenTheReadIsNotPositive", FallbackOnlyWhenTheReadIsNotPositive),
            ("AHandBuiltPlanKeepsTheFallback", AHandBuiltPlanKeepsTheFallback),
            ("PictureOffKeepsTheOldFloor", PictureOffKeepsTheOldFloor),
            ("NeedForTakesTheMinimumAsAParameter", NeedForTakesTheMinimumAsAParameter),
            ("EveryFloorDerivesFromThePlan", EveryFloorDerivesFromThePlan),
            ("HoldTargetIsTheLiveMinimumInAQuietSector", HoldTargetIsTheLiveMinimumInAQuietSector),
            ("ReinforcementRoutingReadsThePlansMinimum", ReinforcementRoutingReadsThePlansMinimum),
            ("ReconAndReservesReadThePlansMinimum", ReconAndReservesReadThePlansMinimum),
            ("CanMoveTakesThePlansMinimum", CanMoveTakesThePlansMinimum),
            ("PictureOffRecon5000IsNamed", PictureOffRecon5000IsNamed),
            ("TheWarningKeysOnTheRawRead", TheWarningKeysOnTheRawRead),
            ("TheFirstReviewRowNamesTheMinimum", TheFirstReviewRowNamesTheMinimum),
        });
    }

    // ---- the plan's minimum ----

    private static CampaignSnapshot World(float minStrengthForMovement)
    {
        var s = EnemyPictureTests.Csa(100000f);
        s.MinStrengthForMovement = minStrengthForMovement;
        s.Own.Add(ExecutorFixtures.Corps(1, EnemyPictureTests.RichmondX, 12000f));
        return s;
    }

    private static TheaterPlan PlanOf(CampaignSnapshot s)
    {
        return TheaterPlanner.Plan(s, SectorModel.Build(s), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
    }

    private static void PlanPublishesTheScenariosMinimum()
    {
        AssertEqual(2000f, PlanOf(World(2000f)).MinGarrisonStrength, "002/A's aiminimumstrengthformovement");
        AssertEqual(5000f, PlanOf(World(5000f)).MinGarrisonStrength, "Config's");
        AssertEqual(1000f, PlanOf(World(1000f)).MinGarrisonStrength, "a scenario that sets 1000 gets 1000 (the earlier floor was 5000 whatever the scenario said)");
    }

    private static void FallbackOnlyWhenTheReadIsNotPositive()
    {
        AssertEqual(TheaterPlanner.MinStrengthFallback, PlanOf(World(0f)).MinGarrisonStrength, "0");
        AssertEqual(TheaterPlanner.MinStrengthFallback, PlanOf(World(-3f)).MinGarrisonStrength, "negative");
        AssertEqual(TheaterPlanner.MinStrengthFallback, PlanOf(World(float.NaN)).MinGarrisonStrength, "NaN");
        AssertEqual(TheaterPlanner.MinStrengthFallback, PlanOf(World(float.PositiveInfinity)).MinGarrisonStrength, "infinity");
        AssertEqual(1f, PlanOf(World(1f)).MinGarrisonStrength, "any positive finite read stands");
        AssertEqual(5000f, TheaterPlanner.MinStrengthFallback, "the fallback is Config/campaignprefs.txt 1397");
    }

    private static void AHandBuiltPlanKeepsTheFallback()
    {
        AssertEqual(TheaterPlanner.MinStrengthFallback, new TheaterPlan().MinGarrisonStrength, "a plan a test builds by hand keeps the old floor");
        AssertEqual(TheaterPlanner.MinStrengthFallback, new TheaterPlanner.Inputs().MinStrength, "and so do the planner's inputs");
    }

    // With Enable Strategic Foresight Picture off the plan's minimum is the 5000 fallback, whatever vanilla's minimum: rolling the picture
    // back alone never leaves the live 2000 floor under D2's old presumption (decision 6).
    private static void PictureOffKeepsTheOldFloor()
    {
        bool before = PlannerSettings.ForesightPicture;
        PlannerSettings.ForesightPicture = false;
        try
        {
            AssertEqual(TheaterPlanner.MinStrengthFallback, PlanOf(World(2000f)).MinGarrisonStrength, "the picture off: 5000, though the scenario's minimum is 2000");
        }
        finally { PlannerSettings.ForesightPicture = before; }
        AssertEqual(2000f, PlanOf(World(2000f)).MinGarrisonStrength, "control: on, the live minimum");
    }

    private static void NeedForTakesTheMinimumAsAParameter()
    {
        AssertEqual(2000f, TheaterPlanner.NeedFor(500f, 1.5f, 0f, 2000f), "the smallest force the AI moves at all is the scenario's");
        AssertEqual(5000f, TheaterPlanner.NeedFor(500f, 1.5f, 0f, 5000f), "and the fallback's when that is the minimum");
        AssertTrue(TheaterPlanner.NeedFor(40000f, 1.5f, 0f, 2000f) > 60000f, "a large enemy still sets the need");
    }

    // ---- one definition site ----

    // The only 5000f under Strategic/Planner/ that means "the minimum" is the fallback's declaration; MarkRules' band table is vanilla's
    // approximation limits (80226-80255), not a minimum.
    private static void EveryFloorDerivesFromThePlan()
    {
        string root = RepoPlannerDirectory();
        foreach (string path in Directory.GetFiles(root, "*.cs"))
        {
            string name = Path.GetFileName(path);
            string code = string.Join("\n", File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            if (name == "MarkRules.cs") continue;
            if (name == "TheaterPlanner.cs")
            {
                AssertEqual(1, code.Split(new[] { "5000f" }, StringSplitOptions.None).Length - 1, "TheaterPlanner holds the fallback's declaration and nothing else at 5000");
                AssertContains(code, "internal const float MinStrengthFallback = 5000f;", "the declaration");
                continue;
            }
            AssertFalse(code.Contains("5000f"), name + " names no 5000 minimum");
        }
        AssertFalse(CodeOnly(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ForceAllocator.cs")).Contains("MinGarrisonStrength ="), "the allocator's const is gone");
        foreach (string path in Directory.GetFiles(root, "*.cs"))
            AssertFalse(CodeOnly(File.ReadAllText(path)).Contains("ForceAllocator.MinGarrisonStrength"), Path.GetFileName(path) + " reads no allocator constant");
    }

    private static string RepoPlannerDirectory()
    {
        return Path.GetDirectoryName(FindRepoPath("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs"));
    }

    private static string CodeOnly(string src)
    {
        return string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    // ---- the readers ----

    // Three 3000 divisions in a quiet sector keep max(0, the plan's minimum): at 2000 the first reaches it, so the other two are released.
    private static void HoldTargetIsTheLiveMinimumInAQuietSector()
    {
        TheaterPlan Run(float minimum, out Allocation allocation)
        {
            var s = ExecutorFixtures.Wide();
            s.Own.Add(ExecutorFixtures.Corps(1, 520f, 3000f));
            s.Own.Add(ExecutorFixtures.Corps(2, 550f, 3000f));
            s.Own.Add(ExecutorFixtures.Corps(3, 580f, 3000f));
            var plan = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial };
            plan.MinGarrisonStrength = minimum;
            allocation = TestClaims.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
            return plan;
        }
        Run(2000f, out var live);
        AssertTrue(live.TryGet(2, out var center) && center.Withheld, "the formation on the centroid is withheld first");
        AssertTrue(live.TryGet(1, out var second) && !second.Withheld, "and at the live 2000 it alone meets the minimum: the second is released");
        AssertTrue(live.TryGet(3, out var third) && !third.Withheld, "so is the third");
        Run(5000f, out var old);
        AssertTrue(old.TryGet(1, out var oldSecond) && oldSecond.Withheld, "control: at 5000 the second is needed too (3000 + 3000)");
        AssertTrue(old.TryGet(3, out var oldThird) && !oldThird.Withheld, "and the third is beyond it");
    }

    private static void ReinforcementRoutingReadsThePlansMinimum()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 520f, 1500f));
        var sectors = ExecutorFixtures.Sectors(s);
        int sector = sectors.SectorAt(520f, 0f);
        var allocation = new Allocation { Alliance = 0 };
        allocation.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.Hold, sector, 520f, 0f));
        var plan = new TheaterPlan { Alliance = 0, DaySerial = s.DaySerial };
        plan.MinGarrisonStrength = 2000f;
        AssertNear(500f, ReinforcementRouting.ShortfallStrength(plan, allocation, s.Own, 1), 0.5f, "a quiet sector is short of the plan's 2000 by what it lacks");
        plan.MinGarrisonStrength = 5000f;
        AssertNear(3500f, ReinforcementRouting.ShortfallStrength(plan, allocation, s.Own, 1), 0.5f, "control: of 5000");
    }

    private static void ReconAndReservesReadThePlansMinimum()
    {
        string reserves = CodeOnly(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReservesRow.cs"));
        AssertContains(reserves, "plan.MinGarrisonStrength", "the [Reserves] row's quiet sectors floor at the plan's minimum");
        string recon = CodeOnly(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ReconPlanner.cs"));
        AssertContains(recon, "return plan != null ? plan.MinGarrisonStrength : TheaterPlanner.MinStrengthFallback;", "the callers that may hold no plan (the scout pool, the task ranges) use the plan's value or the fallback");
        AssertFalse(recon.Contains("s.MinStrengthForMovement"), "the recon planner reads no raw scenario value");
        string allocator = CodeOnly(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ForceAllocator.cs"));
        AssertContains(allocator, "return Math.Max(_need[sector], _plan.MinGarrisonStrength);", "the hold target reads the plan");
        AssertEqual(2, allocator.Split(new[] { "_believed[k] > _plan.MinGarrisonStrength" }, StringSplitOptions.None).Length - 1, "and so does the scout test, on both branches of the flag");
        AssertEqual(2, allocator.Split(new[] { "_plan.MinGarrisonStrength)" }, StringSplitOptions.None).Length - 1 - 1, "and the two recon movement tests (the target above ends the same way)");
        string snapshot = CodeOnly(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSnapshotRuntime.cs"));
        AssertContains(snapshot, "FlagsOf(reg, unitTyp, hasDirectCorps, s.MinStrengthForMovement)", "FlagsOf keeps vanilla's raw read (its UnitIsFightingForce mirror)");
    }

    private static void CanMoveTakesThePlansMinimum()
    {
        var s = ExecutorFixtures.Wide();
        var f = ExecutorFixtures.Corps(1, 520f, 3000f);
        f.Morale = 1f;
        s.Own.Add(f);
        s.MinMoraleForMovement = 0.3f;
        AssertTrue(ReconPlanner.CouldScout(s, f, ReconKind.Screen, 2000f), "3000 men meet a minimum of 2000");
        AssertFalse(ReconPlanner.CouldScout(s, f, ReconKind.Screen, 5000f), "and not 5000");
        AssertTrue(ReconPlanner.CouldScout(s, f, ReconKind.AdvanceGuard, 5000f), "a detachment needs only a body, whatever the minimum");
    }

    private static void PictureOffRecon5000IsNamed()
    {
        bool before = PlannerSettings.ForesightPicture;
        PlannerSettings.ForesightPicture = false;
        try
        {
            var s = World(2000f);
            var f = ExecutorFixtures.Corps(2, EnemyPictureTests.RichmondX, 3000f);
            f.Morale = 1f;
            s.Own.Add(f);
            var plan = PlanOf(s);
            AssertFalse(ReconPlanner.CouldScout(s, f, ReconKind.Screen, plan.MinGarrisonStrength),
                "the picture off, recon's minimum is the 5000 fallback too: a 3000 division does not screen though vanilla's own minimum is 2000 (named in the flag's rollback)");
        }
        finally { PlannerSettings.ForesightPicture = before; }
    }

    private static void TheWarningKeysOnTheRawRead()
    {
        string review = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs"), "internal static void ReviewPlan(");
        int warn = review.IndexOf("OnceLog.Warning(\"campaign-planner:min-strength-fallback\"", StringComparison.Ordinal);
        AssertTrue(warn > 0, "the review warns when the fallback applies");
        string guard = review.Substring(Math.Max(0, warn - 220), 220);
        AssertContains(guard, "snapshot.MinStrengthForMovement", "keyed on the raw read");
        AssertFalse(guard.Contains("ForesightPicture"), "whatever the picture flag says");
    }

    private static void TheFirstReviewRowNamesTheMinimum()
    {
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string first = BodyOf(runtime, "private static void LogFirstReview(");
        AssertContains(first, "\" minStrength=\" + plan.MinGarrisonStrength", "[CampaignPlanner] first review prints the plan's minimum");
        AssertContains(BodyOf(runtime, "internal static void ReviewPlan("), "LogFirstReview(snapshot, sectors, plan);", "from the review, with the plan");
    }
}
