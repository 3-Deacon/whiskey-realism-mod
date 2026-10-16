using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G5c (docs/superpowers/plans/2026-10-15-campaign-planner.md § G5c): construction steering. The depot task ledger
// and its builder hold, the railroad line choice against a replica of vanilla's loop, the rail line scorer, the fort site
// preference, the construction steering board and the construction ledger's depot wiring, with the runtime pins at the end.
internal static class ConstructionSteeringTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("RailProbabilityIsVanillas16058", RailProbabilityIsVanillas16058),
            ("RailSteeringPreservesSuccessCount", RailSteeringPreservesSuccessCount),
            ("RailSuccessOnIneligibleLineStartsNothing", RailSuccessOnIneligibleLineStartsNothing),
            ("RailEachLineStartedAtMostOncePerCall", RailEachLineStartedAtMostOncePerCall),
            ("RailSteeringStartsTheHighestScoredEligibleLine", RailSteeringStartsTheHighestScoredEligibleLine),
            ("RailBestLineStartedBeforeItsOwnRollStillCountsItsSuccess", RailBestLineStartedBeforeItsOwnRollStillCountsItsSuccess),
            ("RailTiesKeepTheRolledLineThenTheLowestIndex", RailTiesKeepTheRolledLineThenTheLowestIndex),
            ("RailTakenRolledLineFallsBackToTheLowestIndexAmongTies", RailTakenRolledLineFallsBackToTheLowestIndexAmongTies),
            ("RailRolledLinesPairWithStartsInOrder", RailRolledLinesPairWithStartsInOrder),
            ("RailRollEqualToTheChanceSucceedsAsInVanilla", RailRollEqualToTheChanceSucceedsAsInVanilla),
            ("RailSteeringStandsDownForPlayerAndEurope", RailSteeringStandsDownForPlayerAndEurope),
            ("RailSteeringStandsDownWhileJobMachinePaused", RailSteeringStandsDownWhileJobMachinePaused),
            ("RailScorePrefersMainAxis", RailScorePrefersMainAxis),
            ("RailScoreUsesEndpointsNotAverage", RailScoreUsesEndpointsNotAverage),
            ("RailScoreRewardsAnOwnSupplyEndOnTheAxis", RailScoreRewardsAnOwnSupplyEndOnTheAxis),
            ("RailScoreRewardsAThreatenedSector", RailScoreRewardsAThreatenedSector),
            ("RailScoreRewardsSupplyReachTowardTheTarget", RailScoreRewardsSupplyReachTowardTheTarget),
            ("RailScoreWithoutAMainEffortKeepsTheSupplyAndThreatTerms", RailScoreWithoutAMainEffortKeepsTheSupplyAndThreatTerms),
            ("RailScoreStaysInUnitRangeAndHandlesAMissingLine", RailScoreStaysInUnitRangeAndHandlesAMissingLine),
            ("DepotWishOnlyOnArrivalWithinBugleRange", DepotWishOnlyOnArrivalWithinBugleRange),
            ("DepotWishOncePerTask", DepotWishOncePerTask),
            ("DepotBuilderHeldUntilBuilt", DepotBuilderHeldUntilBuilt),
            ("DepotAbandonedAfterTimeout", DepotAbandonedAfterTimeout),
            ("DepotTimeoutRunsFromTheWishOnceWished", DepotTimeoutRunsFromTheWishOnceWished),
            ("OneActiveDepotTaskPerAlliance", OneActiveDepotTaskPerAlliance),
            ("DepotBuilderWithheldWhileHeld", DepotBuilderWithheldWhileHeld),
            ("DepotTasksKeyedByBuilderUnitKey", DepotTasksKeyedByBuilderUnitKey),
            ("DepotSiteFollowsThePlanWhileMarchingAndDropsWithIt", DepotSiteFollowsThePlanWhileMarchingAndDropsWithIt),
            ("DepotRetryWaitsAfterAbandon", DepotRetryWaitsAfterAbandon),
            ("DepotBuilderNeverStartsOnThePlayersCommand", DepotBuilderNeverStartsOnThePlayersCommand),
            ("FinishedDepotRecordsAreBounded", FinishedDepotRecordsAreBounded),
            ("DepotLedgersArePerAllianceAndPerCampaign", DepotLedgersArePerAllianceAndPerCampaign),
            ("DepotTransitionsNameTheirReasons", DepotTransitionsNameTheirReasons),
            ("DepotBuiltWhenABuiltDepotStandsNearTheSite", DepotBuiltWhenABuiltDepotStandsNearTheSite),
            ("DepotRebindNeverSeatsAPlayerFormation", DepotRebindNeverSeatsAPlayerFormation),
            ("DepotBuilderHeldButNotAllocatedIsWithheld", DepotBuilderHeldButNotAllocatedIsWithheld),
            ("DepotCooldownIsADayDifferenceThatSurvivesARestore", DepotCooldownIsADayDifferenceThatSurvivesARestore),
            ("DepotWishMomentIsHereAdoptAheadOrWait", DepotWishMomentIsHereAdoptAheadOrWait),
            ("DepotAheadNeedsAnEarlierMoveTowardTheSite", DepotAheadNeedsAnEarlierMoveTowardTheSite),
            ("MoveTrackerNamesAMarchTowardASite", MoveTrackerNamesAMarchTowardASite),
            ("DepotWishLostWhenNoOwnDepotStandsAtTheSite", DepotWishLostWhenNoOwnDepotStandsAtTheSite),
            ("DepotAdoptNeedsAnOwnUnbuiltDepotAtTheSite", DepotAdoptNeedsAnOwnUnbuiltDepotAtTheSite),
            ("DepotIsLookedForWhereTheWishWasPlaced", DepotIsLookedForWhereTheWishWasPlaced),
            ("PinnedBuilderListsOnlyWishedTasksOfMovableFormations", PinnedBuilderListsOnlyWishedTasksOfMovableFormations),
            ("WishedBuilderKeepsItsRoleWhateverThePlanDoes", WishedBuilderKeepsItsRoleWhateverThePlanDoes),
            ("AheadBuilderStaysOnItsMarchToTheSite", AheadBuilderStaysOnItsMarchToTheSite),
            ("PinnedBuilderIsNeverTheMapGroupPlayerAndIsNoReserve", PinnedBuilderIsNeverTheMapGroupPlayerAndIsNoReserve),
            ("AllocatorAndPlannerWiringPinsTheWishedBuilders", AllocatorAndPlannerWiringPinsTheWishedBuilders),
            ("AdoptNeedsTheDepotWithinTheBuildersBugleRange", AdoptNeedsTheDepotWithinTheBuildersBugleRange),
            ("APendingPlacementIsNotALostWish", APendingPlacementIsNotALostWish),
            ("DepotHoldReachesTheFortJobOnlyUnderAFreshPlan", DepotHoldReachesTheFortJobOnlyUnderAFreshPlan),
            ("DepotLedgerAbandonsOnlyWithADay", DepotLedgerAbandonsOnlyWithADay),
            ("DepotFlagOffReleasesEveryActiveTask", DepotFlagOffReleasesEveryActiveTask),
            ("DepotCandidateCreatedFromPlanDepotTask", DepotCandidateCreatedFromPlanDepotTask),
            ("DepotTaskStartFollowsTheLedgersVerdict", DepotTaskStartFollowsTheLedgersVerdict),
            ("TopSupplyDepotIsTheNextTaskToStart", TopSupplyDepotIsTheNextTaskToStart),
            ("FortPreferenceOnlyWhenAxisSiteEligible", FortPreferenceOnlyWhenAxisSiteEligible),
            ("FortPreferenceKeepsVanillaSitesOtherwise", FortPreferenceKeepsVanillaSitesOtherwise),
            ("TopFortPreferredWhenEligible", TopFortPreferredWhenEligible),
            ("BoardPrefersFortSitesOnTheAxisOrInAThreatenedSector", BoardPrefersFortSitesOnTheAxisOrInAThreatenedSector),
            ("FortRankIsTheLedgersCandidateScorePlusTheTopBonus", FortRankIsTheLedgersCandidateScorePlusTheTopBonus),
            ("RailScoreMultipliesTheLedgersCandidateScore", RailScoreMultipliesTheLedgersCandidateScore),
            ("CsaRailCapExcludesLineFromSteering", CsaRailCapExcludesLineFromSteering),
            ("TopRailroadBreaksScoreTies", TopRailroadBreaksScoreTies),
            ("LedgerKeepsEveryScoredRailAndFortCandidateWithItsIndex", LedgerKeepsEveryScoredRailAndFortCandidateWithItsIndex),
            ("BoardIsPerAllianceFreshAndPerCampaign", BoardIsPerAllianceFreshAndPerCampaign),
            ("DepotSteeringRuntimeStatementsPinned", DepotSteeringRuntimeStatementsPinned),
            ("DepotSteeringRuntimeIssuesNoUnitOrder", DepotSteeringRuntimeIssuesNoUnitOrder),
            ("ConstructionRuntimeFeedsTheLedgerPlanDepotsAndSiteIndexes", ConstructionRuntimeFeedsTheLedgerPlanDepotsAndSiteIndexes),
            ("DepotExecutorRidesTheSteeringExecutorBehindTheGates", DepotExecutorRidesTheSteeringExecutorBehindTheGates),
            ("RailroadPatchStatementsPinned", RailroadPatchStatementsPinned),
            ("RailroadPatchDrawsBeforeItStartsAndNeverSkipsVanillaBeforeTheDraws", RailroadPatchDrawsBeforeItStartsAndNeverSkipsVanillaBeforeTheDraws),
            ("FortPatchStatementsPinned", FortPatchStatementsPinned),
            ("ConstructionSteeringFlagsAreBoundAndRead", ConstructionSteeringFlagsAreBoundAndRead),
            ("DepotExecutorFaultCannotIdleTheMover", DepotExecutorFaultCannotIdleTheMover),
            ("HeldDepotBuilderIsNotSentToBuildAFort", HeldDepotBuilderIsNotSentToBuildAFort),
            ("ConstructionSteeringFilesHoldNoUnityTypesOrEraReads", ConstructionSteeringFilesHoldNoUnityTypesOrEraReads),
        });
    }

    // ---- Railroad line choice against a replica of vanilla's loop ----------------------------------------------------------

    // One predefined line as vanilla holds it: constructionprogress, AllianceOwnsAllIIPs, scriptref.IsPermitted.
    private struct VanillaLine
    {
        public float Progress;
        public bool OwnsAllIips;
        public bool Permitted;
    }

    // Railroad.StartConstruction(alliance, checkonly: true), 77818-77831.
    private static bool VanillaCanStart(in VanillaLine l)
    {
        return l.Progress < 1f && l.OwnsAllIips && l.Permitted;
    }

    // UpdateRailroadConstruction's loop, 16067-16073: one roll per line in line order, the roll tested before the progress,
    // then StartConstruction (which refuses what it refuses). Returns the number of lines it started.
    private static int VanillaStarts(VanillaLine[] lines, float[] rolls, float perCall)
    {
        var progress = new float[lines.Length];
        for (int j = 0; j < lines.Length; j++) progress[j] = lines[j].Progress;
        int started = 0;
        for (int j = 0; j < lines.Length; j++)
        {
            var line = lines[j];
            line.Progress = progress[j];
            if (rolls[j] <= perCall && progress[j] <= 0f && VanillaCanStart(line))
            {
                progress[j] = 0.0001f;
                started++;
            }
        }
        return started;
    }

    // Twelve lines: normal, unowned IIPs, not permitted, under construction, complete, normal, repeated.
    private static VanillaLine[] MixedLines()
    {
        var lines = new VanillaLine[12];
        for (int j = 0; j < lines.Length; j++)
        {
            lines[j] = new VanillaLine { Progress = 0f, OwnsAllIips = true, Permitted = true };
            switch (j % 6)
            {
                case 1: lines[j].OwnsAllIips = false; break;
                case 2: lines[j].Permitted = false; break;
                case 3: lines[j].Progress = 0.4f; break;
                case 4: lines[j].Progress = 1f; break;
            }
        }
        return lines;
    }

    private static bool[] EligibleOf(VanillaLine[] lines)
    {
        var eligible = new bool[lines.Length];
        for (int j = 0; j < lines.Length; j++) eligible[j] = RailLineSteering.Eligible(lines[j].Progress, VanillaCanStart(lines[j]));
        return eligible;
    }

    private static float[] Rolls(int seed, int n)
    {
        var rng = new Random(seed);
        var rolls = new float[n];
        for (int j = 0; j < n; j++) rolls[j] = (float)rng.NextDouble();
        return rolls;
    }

    private static float[] Scores(int seed, int n)
    {
        var rng = new Random(seed * 31 + 7);
        var scores = new float[n];
        for (int j = 0; j < n; j++) scores[j] = (float)rng.NextDouble();
        return scores;
    }

    // 16058-16069: the base probability plus one per-industry term for each of the four policies the alliance has, times the
    // elapsed time; the roll succeeds when Random.Range(0, 1) <= that.
    private static void RailProbabilityIsVanillas16058()
    {
        AssertNear(0.12f, RailLineSteering.ProbabilityPerCall(0.12f, 0.05f, 0, 1f), 1e-6f, "no industry policy: the base alone");
        AssertNear(0.12f + 3 * 0.05f, RailLineSteering.ProbabilityPerCall(0.12f, 0.05f, 3, 1f), 1e-6f, "three policies add three terms");
        AssertNear((0.12f + 4 * 0.05f) * 0.25f, RailLineSteering.ProbabilityPerCall(0.12f, 0.05f, 4, 0.25f), 1e-6f, "scaled by the elapsed time");
        AssertNear(0f, RailLineSteering.ProbabilityPerCall(0.12f, 0.05f, 2, 0f), 1e-6f, "no time elapsed: no chance");
        AssertNear(0.12f + 4 * 0.05f, RailLineSteering.ProbabilityPerCall(0.12f, 0.05f, 9, 1f), 1e-6f, "only four industry policies exist (7..10 / 107..110)");
        AssertNear(0.12f, RailLineSteering.ProbabilityPerCall(0.12f, 0.05f, -2, 1f), 1e-6f, "a negative count adds nothing");
    }

    private static void RailSteeringPreservesSuccessCount()
    {
        var lines = MixedLines();
        var eligible = EligibleOf(lines);
        int eligibleCount = 0;
        for (int j = 0; j < eligible.Length; j++) if (eligible[j]) eligibleCount++;
        AssertEqual(4, eligibleCount, "fixture: the unowned, the not permitted, the under-construction and the complete lines are all ineligible");
        long totalVanilla = 0, totalSteered = 0;
        foreach (float p in new[] { 0.05f, 0.2f, 0.5f, 0.9f })
        {
            for (int seed = 0; seed < 300; seed++)
            {
                var rolls = Rolls(seed, lines.Length);
                int vanilla = VanillaStarts(lines, rolls, p);
                var started = RailLineSteering.Choose(eligible, rolls, p, Scores(seed, lines.Length));
                AssertEqual(vanilla, started.Length, "p=" + p + " seed=" + seed + ": the number of starts equals vanilla's for the same draws");
                totalVanilla += vanilla;
                totalSteered += started.Length;
            }
        }
        AssertTrue(totalVanilla > 500, "fixture: the seeds produce successes (" + totalVanilla + ")");
        AssertEqual(totalVanilla, totalSteered, "and the totals agree");
    }

    private static void RailSuccessOnIneligibleLineStartsNothing()
    {
        var lines = MixedLines();
        var eligible = EligibleOf(lines);
        var rolls = new float[lines.Length];
        for (int j = 0; j < rolls.Length; j++) rolls[j] = eligible[j] ? 1f : 0f; // only the ineligible lines roll a success
        var scores = new float[lines.Length];
        for (int j = 0; j < scores.Length; j++) scores[j] = j;
        AssertEqual(0, VanillaStarts(lines, rolls, 0.5f), "vanilla starts nothing for a success on a line it cannot start");
        AssertEqual(0, RailLineSteering.Choose(eligible, rolls, 0.5f, scores).Length, "a success on an ineligible line starts nothing, as in vanilla");
    }

    private static void RailEachLineStartedAtMostOncePerCall()
    {
        var lines = MixedLines();
        var eligible = EligibleOf(lines);
        var rolls = new float[lines.Length]; // every line rolls a success
        for (int seed = 0; seed < 100; seed++)
        {
            var started = RailLineSteering.Choose(eligible, rolls, 0.5f, Scores(seed, lines.Length));
            var seen = new HashSet<int>();
            for (int k = 0; k < started.Length; k++)
            {
                AssertTrue(eligible[started[k]], "seed " + seed + ": only a line vanilla could start is started");
                AssertTrue(seen.Add(started[k]), "seed " + seed + ": line " + started[k] + " is started once");
            }
            AssertEqual(VanillaStarts(lines, rolls, 0.5f), started.Length, "seed " + seed + ": a success on every line starts one line per eligible line (" + started.Length + ")");
        }
    }

    private static void RailSteeringStartsTheHighestScoredEligibleLine()
    {
        var eligible = new[] { true, true, true, false, true };
        var rolls = new[] { 0f, 1f, 1f, 1f, 1f }; // only line 0 succeeds
        var scores = new[] { 0.1f, 0.2f, 0.3f, 9f, 0.7f }; // line 3 scores highest but vanilla could not start it
        var started = RailLineSteering.Choose(eligible, rolls, 0.5f, scores);
        AssertEqual(1, started.Length, "one success, one start");
        AssertEqual(4, started[0], "the highest-scored line vanilla could start, not the line that rolled");
    }

    // The invariant the count rests on: line 2 is started by line 0's success, then line 2's own roll succeeds; the second
    // success must start one more line, never line 2 again, and a third eligible line always remains.
    private static void RailBestLineStartedBeforeItsOwnRollStillCountsItsSuccess()
    {
        var eligible = new[] { true, true, true };
        var rolls = new[] { 0f, 1f, 0f };
        var scores = new[] { 0.1f, 0.2f, 0.9f };
        var started = RailLineSteering.Choose(eligible, rolls, 0.5f, scores);
        AssertEqual(2, started.Length, "two successes, two starts");
        AssertEqual(2, started[0], "the first success starts the best line");
        AssertEqual(1, started[1], "the second success: line 2 is taken, so the best of what remains");
    }

    // When the rolled line is already taken, the best of the rest is the highest score and, among equal scores, the lowest index.
    private static void RailTakenRolledLineFallsBackToTheLowestIndexAmongTies()
    {
        var eligible = new[] { true, true, true, true };
        var scores = new[] { 0.1f, 0.5f, 0.5f, 0.9f };
        var started = RailLineSteering.Choose(eligible, new[] { 0f, 1f, 1f, 0f }, 0.5f, scores);
        AssertEqual(2, started.Length, "two successes, two starts");
        AssertEqual(3, started[0], "the first success starts the strictly better line 3");
        AssertEqual(1, started[1], "line 3 is taken; lines 1 and 2 tie: the lowest index");
    }

    private static void RailTiesKeepTheRolledLineThenTheLowestIndex()
    {
        var eligible = new[] { true, true, true };
        var scores = new[] { 0.5f, 0.5f, 0.5f };
        var onTwo = RailLineSteering.Choose(eligible, new[] { 1f, 1f, 0f }, 0.5f, scores);
        AssertEqual(2, onTwo[0], "a tie with the rolled line keeps the rolled line (steering needs a strictly better one)");
        var taken = RailLineSteering.Choose(eligible, new[] { 0f, 0f, 1f }, 0.5f, new[] { 0.5f, 0.5f, 0.9f });
        AssertEqual(2, taken[0], "the first success starts the strictly better line");
        AssertEqual(1, taken[1], "the second success is on line 1; with line 2 taken and 0 and 1 tied it starts the rolled line, not line 0");
        var lowest = RailLineSteering.Choose(eligible, new[] { 0f, 0f, 0f }, 0.5f, new[] { 0.5f, 0.5f, 0.5f });
        AssertEqual(3, lowest.Length, "three successes, three starts");
        AssertEqual(0, lowest[0], "equal scores: the lowest index first");
    }

    // ---- Rail line scorer ----------------------------------------------------------------------------------------------------

    // Alliance 0, areas x 0..600, own supply IIPs 501@100, 503@300 and 505@500 (z 0), the mass (corps 1) at x 500 and the main
    // effort at (100, 0): the main axis runs from (500, 0) to (100, 0); the relocation range is 150.
    private static CampaignSnapshot RailWorld(out TheaterPlan plan, out SectorModel sectors)
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 500f, 20000f));
        sectors = ExecutorFixtures.Sectors(s);
        plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        return s;
    }

    private static RailLineRecord Line(int index, float ax, float az, int aId, float bx, float bz, int bId)
    {
        return new RailLineRecord { Index = index, Startable = true, AX = ax, AZ = az, AIipId = aId, BX = bx, BZ = bz, BIipId = bId };
    }

    private static void RailScorePrefersMainAxis()
    {
        var s = RailWorld(out var plan, out var sectors);
        var onAxis = Line(0, 500f, 0f, 505, 150f, 0f, 910);
        var offAxis = Line(1, 500f, 0f, 505, 500f, 200f, 911);
        float a = RailLineScorer.Score(onAxis, s, plan, sectors), b = RailLineScorer.Score(offAxis, s, plan, sectors);
        AssertTrue(a > b, "two lines from the same own source: the one running along the main axis scores higher (" + a + " vs " + b + ")");
        // The axis term alone (no own supply, no reach gain, no threat): both ends near the axis beat one end near it.
        var bothEnds = Line(2, 300f, 0f, 930, 250f, 0f, 931);
        var oneEnd = Line(3, 300f, 0f, 932, 300f, 200f, 933);
        AssertTrue(RailLineScorer.Score(bothEnds, s, plan, sectors) > RailLineScorer.Score(oneEnd, s, plan, sectors) + 0.05f,
            "the closeness of the ends is averaged: a line along the axis beats a line that touches it");
    }

    // Two lines with one average point (200, 0): the one that links own supply to the axis wins; an average-point scorer could
    // not tell them apart.
    private static void RailScoreUsesEndpointsNotAverage()
    {
        var s = RailWorld(out var plan, out var sectors);
        var links = Line(0, 300f, 0f, 503, 100f, 0f, 912);
        var scattered = Line(1, 200f, 200f, 913, 200f, -200f, 914);
        AssertNear(200f, (links.AX + links.BX) / 2f, 1e-4f, "fixture: the average x");
        AssertNear(0f, (links.AZ + links.BZ) / 2f, 1e-4f, "fixture: the average z");
        AssertNear((links.AX + links.BX) / 2f, (scattered.AX + scattered.BX) / 2f, 1e-4f, "fixture: one average point (x)");
        AssertNear((links.AZ + links.BZ) / 2f, (scattered.AZ + scattered.BZ) / 2f, 1e-4f, "fixture: one average point (z)");
        float a = RailLineScorer.Score(links, s, plan, sectors), b = RailLineScorer.Score(scattered, s, plan, sectors);
        AssertTrue(a > b + 0.3f, "the line linking own supply to the axis wins clearly (" + a + " vs " + b + ")");
    }

    private static void RailScoreRewardsAnOwnSupplyEndOnTheAxis()
    {
        var s = RailWorld(out var plan, out var sectors);
        var ownEnd = Line(0, 300f, 0f, 503, 250f, 0f, 915);
        var strangers = Line(1, 300f, 0f, 916, 250f, 0f, 917);
        AssertTrue(RailLineScorer.Score(ownEnd, s, plan, sectors) > RailLineScorer.Score(strangers, s, plan, sectors), "the same geometry scores higher with an own supply source at one end");
        float baseline = RailLineScorer.Score(ownEnd, s, plan, sectors);
        var enemyIip = Line(2, 300f, 0f, 503, 250f, 0f, 918);
        s.Supply[1] = new SupplySourceRecord { Id = 503, Kind = SupplySourceKind.Iip, Owner = 1, X = 300f, Z = 0f, Built = true };
        AssertTrue(RailLineScorer.Score(enemyIip, s, plan, sectors) < baseline, "an IIP the other side owns is not our supply source");
        s.Supply[1] = new SupplySourceRecord { Id = 503, Kind = SupplySourceKind.Iip, Owner = 0, X = 300f, Z = 0f, Built = true, Invisible = true };
        AssertTrue(RailLineScorer.Score(enemyIip, s, plan, sectors) < baseline, "nor is an invisible IIP (LogisticsGate.Supplies' rule)");
    }

    private static void RailScoreRewardsAThreatenedSector()
    {
        var s = RailWorld(out var plan, out var sectors);
        int east = sectors.SectorAt(550f, 0f);
        plan.Sectors.Add(new SectorPlan { SectorId = east, Posture = TheaterPosture.Hold, TargetObjectiveId = -1, TargetX = 550f, TargetZ = 0f, HoldNeed = 9000f });
        var inThreat = Line(0, 600f, 300f, 920, 580f, 300f, 921);
        var quiet = Line(1, 0f, 300f, 922, 20f, 300f, 923);
        AssertTrue(sectors.SectorAt(600f, 300f) == east, "fixture: the first line stands in the threatened sector");
        AssertTrue(RailLineScorer.Score(inThreat, s, plan, sectors) > RailLineScorer.Score(quiet, s, plan, sectors), "an end in a Hold sector scores");
        plan.Sectors[plan.Sectors.Count - 1].Posture = TheaterPosture.Screen;
        AssertTrue(RailLineScorer.Score(inThreat, s, plan, sectors) > RailLineScorer.Score(quiet, s, plan, sectors), "and a Screen sector");
        var secondEndOnly = Line(2, 0f, 300f, 924, 600f, 300f, 925);
        AssertTrue(RailLineScorer.Score(secondEndOnly, s, plan, sectors) > RailLineScorer.Score(quiet, s, plan, sectors), "either end in the threatened sector scores, not the first alone");
        plan.Sectors[plan.Sectors.Count - 1].Posture = TheaterPosture.EconomyOfForce;
        AssertNear(RailLineScorer.Score(quiet, s, plan, sectors), RailLineScorer.Score(inThreat, s, plan, sectors), 1e-4f, "a sector that is neither threatened nor on the axis adds nothing");
    }

    private static void RailScoreRewardsSupplyReachTowardTheTarget()
    {
        var s = RailWorld(out var plan, out var sectors);
        var toward = Line(0, 500f, 0f, 505, 200f, 0f, 930);
        var away = Line(1, 500f, 0f, 505, 590f, 0f, 931);
        AssertTrue(RailLineScorer.Score(toward, s, plan, sectors) > RailLineScorer.Score(away, s, plan, sectors), "a line that carries supply from own source toward the main target scores above one that points away");
        // The reach term alone: an own source far off the axis (axis closeness 0 at both ends) and one end nearer the target or not.
        s.Supply.Add(ExecutorFixtures.Iip(507, 600f, 0));
        s.Supply[s.Supply.Count - 1] = new SupplySourceRecord { Id = 507, Kind = SupplySourceKind.Iip, Owner = 0, X = 600f, Z = 400f, Built = true };
        var nearer = Line(2, 600f, 400f, 507, 300f, 400f, 934);
        var farther = Line(3, 600f, 400f, 507, 700f, 400f, 935);
        float n = RailLineScorer.Score(nearer, s, plan, sectors), f = RailLineScorer.Score(farther, s, plan, sectors);
        AssertTrue(n > f + 0.03f, "with nothing else to tell them apart, the line whose far end is nearer the target scores higher (" + n + " vs " + f + ")");
        AssertNear(f, RailLineScorer.OwnSupplyWeight * 0.5f, 1e-4f, "a line pointing away gains no reach: its score is the own-supply share alone");
    }

    private static void RailScoreWithoutAMainEffortKeepsTheSupplyAndThreatTerms()
    {
        var s = RailWorld(out _, out var sectors);
        var none = new TheaterPlan { Alliance = 0, DaySerial = s.DaySerial };
        var own = Line(0, 300f, 0f, 503, 250f, 0f, 940);
        var strangers = Line(1, 300f, 0f, 941, 250f, 0f, 942);
        AssertTrue(RailLineScorer.Score(own, s, none, sectors) > RailLineScorer.Score(strangers, s, none, sectors), "no main effort: the own-supply end still scores");
        AssertNear(0f, RailLineScorer.Score(strangers, s, none, sectors), 1e-6f, "no main effort, no own supply, no threat: nothing to prefer");
        AssertTrue(RailLineScorer.Score(own, s, null, sectors) > 0f, "no plan at all is the same as no main effort");
        AssertTrue(RailLineScorer.Score(own, s, none, null) > 0f, "and no sector model drops only the sector terms");
    }

    private static void RailScoreStaysInUnitRangeAndHandlesAMissingLine()
    {
        var s = RailWorld(out var plan, out var sectors);
        for (int k = 0; k < 60; k++)
        {
            var rng = new Random(k);
            var line = Line(k, (float)rng.NextDouble() * 700f - 50f, (float)rng.NextDouble() * 400f - 200f, rng.Next(500, 507),
                (float)rng.NextDouble() * 700f - 50f, (float)rng.NextDouble() * 400f - 200f, rng.Next(500, 507));
            float score = RailLineScorer.Score(line, s, plan, sectors);
            AssertTrue(score >= 0f && score <= 1f, "every score is normalized to [0, 1] (" + score + ")");
        }
        var empty = Line(0, 0f, 0f, -1, 0f, 0f, -1);
        AssertNear(0f, RailLineScorer.Score(empty, s, plan, sectors), 1e-6f, "a line with no IIPs scores nothing");
        AssertNear(0f, RailLineScorer.Score(Line(1, 0f, 0f, 503, 0f, 0f, 503), null, plan, sectors), 1e-6f, "no snapshot, no score");
    }

    // ---- Depot task ledger ---------------------------------------------------------------------------------------------------

    // Alliance 0; corps 1 and 2 are the two DepotBuilders the allocation picked for the plan's two depot tasks (sites x 250 and
    // x 400); corps 3 is an idle formation. Bugle range 5 (ExecutorFixtures.Corps).
    private static CampaignSnapshot DepotWorld(out TheaterPlan plan, out Allocation allocation, int dayOffset = 0)
    {
        var s = ExecutorFixtures.Wide(dayOffset);
        s.Own.Add(ExecutorFixtures.Corps(1, 300f, 9000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 320f, 9000f));
        s.Own.Add(ExecutorFixtures.Corps(3, 100f, 9000f));
        plan = new TheaterPlan { Alliance = 0, DaySerial = s.DaySerial };
        plan.DepotTasks.Add(new DepotTask { X = 250f, Z = 0f, SectorId = 3, Priority = 1f });
        plan.DepotTasks.Add(new DepotTask { X = 400f, Z = 0f, SectorId = 4, Priority = 0.5f });
        allocation = new Allocation { Alliance = 0 };
        allocation.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.DepotBuilder, 3, 250f, 0f));
        allocation.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.DepotBuilder, 4, 400f, 0f));
        return s;
    }

    private static int ActiveDepotTasks(DepotTaskLedger ledger)
    {
        int n = 0;
        for (int i = 0; i < ledger.Tasks.Count; i++)
            if (ledger.Tasks[i].State == DepotTaskState.Marching || ledger.Tasks[i].State == DepotTaskState.Wished) n++;
        return n;
    }

    private static void DepotWishOnlyOnArrivalWithinBugleRange()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        AssertEqual(1, ledger.Tasks.Count, "the first plan task starts");
        AssertEqual(DepotTaskState.Marching, ledger.Tasks[0].State, "marching");
        AssertFalse(ledger.ShouldWish(0, 300f, 0f, 5f), "50 from the site, bugle range 5: still marching");
        AssertFalse(ledger.ShouldWish(0, 256f, 0f, 5f), "6 from the site: just outside the bugle range");
        AssertTrue(ledger.ShouldWish(0, 255f, 0f, 5f), "5 from the site: on arrival within the bugle range");
        AssertTrue(ledger.ShouldWish(0, 250f, 3f, 5f), "and from any side of it");
        AssertFalse(ledger.ShouldWish(1, 250f, 0f, 5f), "no such task");
        AssertFalse(ledger.ShouldWish(-1, 250f, 0f, 5f), "no such task (negative)");
    }

    private static void DepotWishOncePerTask()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        AssertTrue(ledger.ShouldWish(0, 250f, 0f, 5f), "before the wish");
        ledger.RecordWished(0, s.DaySerial, 250f, 0f);
        AssertEqual(DepotTaskState.Wished, ledger.Tasks[0].State, "wished");
        AssertFalse(ledger.ShouldWish(0, 250f, 0f, 5f), "a wished task is never wished again");
        ledger.RecordWished(0, s.DaySerial + 1, 250f, 0f);
        AssertEqual(s.DaySerial, ledger.Tasks[0].StartedDay, "a second RecordWished changes nothing");
    }

    private static void DepotBuilderHeldUntilBuilt()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        AssertTrue(ledger.HoldsBuilder(1), "the builder is held from the start of the march");
        AssertFalse(ledger.HoldsBuilder(2), "the other DepotBuilder has no task yet");
        AssertFalse(ledger.HoldsBuilder(3), "nor an idle formation");
        AssertFalse(ledger.HoldsBuilder(-1), "nor a missing one");
        ledger.RecordWished(0, s.DaySerial + 3, 250f, 0f);
        AssertTrue(ledger.HoldsBuilder(1), "and through the whole wished period, while the depot rises");
        ledger.RecordBuilt(0, s.DaySerial);
        AssertEqual(DepotTaskState.Built, ledger.Tasks[0].State, "built");
        AssertFalse(ledger.HoldsBuilder(1), "the builder is released when the depot stands");
    }

    private static void DepotAbandonedAfterTimeout()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        var later = DepotWorld(out var plan2, out var a2, DepotTaskLedger.TimeoutDays - 1);
        ledger.Sync(plan2, a2, later, null);
        AssertEqual(DepotTaskState.Marching, ledger.Tasks[0].State, "one day short of the timeout it still marches");
        var timedOut = DepotWorld(out var plan3, out var a3, DepotTaskLedger.TimeoutDays);
        ledger.Sync(plan3, a3, timedOut, null);
        AssertEqual(DepotTaskState.Abandoned, ledger.Tasks[0].State, "TimeoutDays after the start the task is abandoned");
        AssertFalse(ledger.HoldsBuilder(1), "and the builder is released");
        AssertEqual(2, ledger.Tasks.Count, "the abandoned site is not restarted at once (it cools down); the other task takes the freed slot");
        AssertEqual(DepotTaskState.Marching, ledger.Tasks[1].State, "the second task marches");
        AssertNear(400f, ledger.Tasks[1].X, 1e-4f, "...it is the one at the other site");
    }

    // StartedDay is the day the current state began: the construction gets its own TimeoutDays after the wish, however long the march took.
    private static void DepotTimeoutRunsFromTheWishOnceWished()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        int wishDay = s.DaySerial + 15;
        ledger.RecordWished(0, wishDay, 250f, 0f);
        var mid = DepotWorld(out var p2, out var a2, 30);
        ledger.Sync(p2, a2, mid, null);
        AssertEqual(DepotTaskState.Wished, ledger.Tasks[0].State, "30 days after the start, 15 after the wish: still building");
        var end = DepotWorld(out var p3, out var a3, 15 + DepotTaskLedger.TimeoutDays);
        ledger.Sync(p3, a3, end, null);
        AssertEqual(DepotTaskState.Abandoned, ledger.Tasks[0].State, "TimeoutDays after the wish: abandoned");
    }

    private static void OneActiveDepotTaskPerAlliance()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        ledger.Sync(plan, a, s, null);
        AssertEqual(1, ActiveDepotTasks(ledger), "two tasks and two builders, one task active");
        AssertTrue(ledger.HoldsBuilder(1) && !ledger.HoldsBuilder(2), "the first (highest-priority) task's builder only");
        ledger.RecordWished(0, s.DaySerial, 250f, 0f);
        ledger.Sync(plan, a, s, null);
        AssertEqual(1, ActiveDepotTasks(ledger), "still one while it is wished");
        ledger.RecordBuilt(0, s.DaySerial);
        var next = DepotWorld(out var plan2, out var a2, 5);
        ledger.Sync(plan2, a2, next, null);
        AssertEqual(1, ActiveDepotTasks(ledger), "when the first is built the second may start");
        AssertTrue(ledger.HoldsBuilder(1) || ledger.HoldsBuilder(2), "...and the builder of an active task is held");
    }

    private static void DepotBuilderWithheldWhileHeld()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        var ledger = DepotTaskLedgers.For(0);
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.FreeHold), "fixture: a Hold formation above its need is vanilla's");
        AssertFalse(CampaignWithholdView.WithheldFromDefense(0, w.FreeHold, 100f, 0f), "...in defence too");
        ledger.Restore(new[]
        {
            new DepotTaskRecord { X = 250f, Z = 0f, SectorId = 1, BuilderInstanceId = -1, BuilderUnitKey = "k", State = DepotTaskState.Wished, StartedDay = ExecutorFixtures.Day }
        });
        // Restore leaves the instance id for the next Sync to bind; hold it the way Sync does.
        var s = ExecutorFixtures.Wide();
        var freeHold = ExecutorFixtures.Corps(w.FreeHold, 550f, 9000f);
        freeHold.UnitKey = "k";
        s.Own.Add(freeHold);
        var plan = new TheaterPlan { Alliance = 0, DaySerial = s.DaySerial };
        ledger.Sync(plan, new Allocation { Alliance = 0 }, s, null);
        AssertTrue(ledger.HoldsBuilder(w.FreeHold), "fixture: the ledger holds the formation");
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.FreeHold), "a builder held by an active depot task is out of vanilla's offensive pool");
        AssertTrue(CampaignWithholdView.WithheldFromDefense(0, w.FreeHold, 100f, 0f), "and out of its defensive pool, whatever the allocation says");
        ledger.RecordBuilt(0, s.DaySerial);
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.FreeHold), "released when the depot stands");
        AssertFalse(CampaignWithholdView.WithheldFromDefense(0, w.FreeHold, 100f, 0f), "...in defence too");
        CampaignScope.ResetAll();
    }

    private static void DepotTasksKeyedByBuilderUnitKey()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        AssertEqual("corps1:1", ledger.Tasks[0].BuilderUnitKey, "the persisted identity is the unit key (name:commander)");
        var saved = new List<DepotTaskRecord>(ledger.Tasks);
        var reloaded = new DepotTaskLedger();
        reloaded.Restore(saved);
        AssertEqual(-1, reloaded.Tasks[0].BuilderInstanceId, "an instance id is session-local: not restored");
        AssertFalse(reloaded.HoldsBuilder(1), "nothing is held until the builder is found again");
        // A new session: the same formation has a new instance id.
        var s2 = ExecutorFixtures.Wide();
        var again = ExecutorFixtures.Corps(77, 300f, 9000f);
        again.UnitKey = "corps1:1";
        s2.Own.Add(again);
        var plan2 = new TheaterPlan { Alliance = 0, DaySerial = s2.DaySerial };
        plan2.DepotTasks.Add(plan.DepotTasks[0]);
        var a2 = new Allocation { Alliance = 0 };
        a2.Assignments.Add(ExecutorFixtures.Assign(77, FormationRole.DepotBuilder, 3, 250f, 0f));
        reloaded.Sync(plan2, a2, s2, null);
        AssertEqual(77, reloaded.Tasks[0].BuilderInstanceId, "rebound to this session's instance by unit key");
        AssertTrue(reloaded.HoldsBuilder(77), "and held again");
        // The formation is gone for good: the task is abandoned, not held forever.
        var s3 = ExecutorFixtures.Wide();
        var gone = new DepotTaskLedger();
        gone.Restore(saved);
        gone.Sync(plan2, new Allocation { Alliance = 0 }, s3, null);
        AssertEqual(DepotTaskState.Abandoned, gone.Tasks[0].State, "a builder the snapshot no longer has abandons the task");
    }

    private static void DepotSiteFollowsThePlanWhileMarchingAndDropsWithIt()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        // The frontier moved 20: the site and the allocation's target moved with it.
        plan.DepotTasks[0].X = 270f;
        a.Assignments[0] = ExecutorFixtures.Assign(1, FormationRole.DepotBuilder, 3, 270f, 0f);
        ledger.Sync(plan, a, s, null);
        AssertNear(270f, ledger.Tasks[0].X, 1e-4f, "a marching task follows the plan's site within the match radius");
        AssertEqual(DepotTaskState.Marching, ledger.Tasks[0].State, "still marching");
        // The plan no longer wants it.
        plan.DepotTasks.Clear();
        ledger.Sync(plan, a, s, null);
        AssertEqual(DepotTaskState.Abandoned, ledger.Tasks[0].State, "no plan task near the site: the task is dropped");
        AssertFalse(ledger.HoldsBuilder(1), "and the builder released");
        // A wished task stays whatever the plan says: the wish exists in vanilla.
        var s2 = DepotWorld(out var plan2, out var a2, 40);
        var wished = new DepotTaskLedger();
        wished.Sync(plan2, a2, s2, null);
        wished.RecordWished(0, s2.DaySerial, 250f, 0f);
        plan2.DepotTasks.Clear();
        wished.Sync(plan2, a2, s2, null);
        AssertEqual(DepotTaskState.Wished, wished.Tasks[0].State, "a wished task does not depend on the plan");
    }

    private static void DepotRetryWaitsAfterAbandon()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        ledger.Abandon(0, "wish-failed", s.DaySerial);
        ledger.Sync(plan, a, s, null);
        AssertEqual(DepotTaskState.Abandoned, ledger.Tasks[0].State, "the abandoned record stays");
        AssertTrue(ledger.Tasks.Count >= 1 && !ledger.HoldsBuilder(1), "the same site is not restarted the same day");
        var soon = DepotWorld(out var p2, out var a2, DepotTaskLedger.RetryAfterAbandonDays - 1);
        ledger.Sync(p2, a2, soon, null);
        bool siteRestarted = false;
        for (int i = 0; i < ledger.Tasks.Count; i++)
            if (ledger.Tasks[i].State == DepotTaskState.Marching && Math.Abs(ledger.Tasks[i].X - 250f) < 1f) siteRestarted = true;
        AssertFalse(siteRestarted, "still cooling down one day before the retry window ends");
        var after = DepotWorld(out var p3, out var a3, DepotTaskLedger.RetryAfterAbandonDays);
        ledger.Sync(p3, a3, after, null);
        siteRestarted = false;
        for (int i = 0; i < ledger.Tasks.Count; i++)
            if (ledger.Tasks[i].State == DepotTaskState.Marching && Math.Abs(ledger.Tasks[i].X - 250f) < 1f) siteRestarted = true;
        AssertTrue(siteRestarted, "after the window the site may start again");
    }

    private static void DepotBuilderNeverStartsOnThePlayersCommand()
    {
        var s = DepotWorld(out var plan, out var a);
        var player = s.Own[s.IndexOfOwn(1)];
        player.Chain = WlChainClass.PlayerCommand;
        s.Own[s.IndexOfOwn(1)] = player;
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        AssertFalse(ledger.HoldsBuilder(1), "a builder the planner may not move never gets a task");
        AssertTrue(ledger.HoldsBuilder(2), "the other task, whose builder the planner may move, is the one that starts");
        AssertEqual(1, ActiveDepotTasks(ledger), "one active task");
        // And a task already on a formation that turns out to be the player's is dropped.
        var s2 = DepotWorld(out var plan2, out var a2);
        var ledger2 = new DepotTaskLedger();
        ledger2.Sync(plan2, a2, s2, null);
        AssertTrue(ledger2.HoldsBuilder(1), "fixture: the first builder holds the task");
        var p2 = s2.Own[s2.IndexOfOwn(1)];
        p2.Chain = WlChainClass.PlayerMapGroup;
        s2.Own[s2.IndexOfOwn(1)] = p2;
        ledger2.Sync(plan2, a2, s2, null);
        AssertFalse(ledger2.HoldsBuilder(1), "the task is abandoned when its formation is the player's");
        AssertEqual(DepotTaskState.Abandoned, ledger2.Tasks[0].State, "abandoned");
    }

    private static void FinishedDepotRecordsAreBounded()
    {
        var ledger = new DepotTaskLedger();
        for (int i = 0; i < DepotTaskLedger.MaxFinishedRecords + 5; i++)
        {
            var s = DepotWorld(out var plan, out var a, i * 100);
            plan.DepotTasks.RemoveAt(1);
            plan.DepotTasks[0].X = 250f + i * 100f;
            a.Assignments.RemoveAt(1);
            a.Assignments[0] = ExecutorFixtures.Assign(1, FormationRole.DepotBuilder, 3, 250f + i * 100f, 0f);
            ledger.Sync(plan, a, s, null);
            ledger.RecordBuilt(ledger.Tasks.Count - 1, s.DaySerial);
        }
        var last = DepotWorld(out var p, out var al, 5000);
        ledger.Sync(p, al, last, null);
        int finished = 0;
        for (int i = 0; i < ledger.Tasks.Count; i++)
            if (ledger.Tasks[i].State == DepotTaskState.Built || ledger.Tasks[i].State == DepotTaskState.Abandoned) finished++;
        AssertTrue(finished <= DepotTaskLedger.MaxFinishedRecords, "finished records are kept for the retry cooldown only and are bounded (" + finished + ")");
    }

    private static void DepotLedgersArePerAllianceAndPerCampaign()
    {
        CampaignScope.ResetAll();
        var zero = DepotTaskLedgers.For(0);
        AssertTrue(zero != null && DepotTaskLedgers.For(1) != null, "alliances 0 and 1 have ledgers");
        AssertTrue(!ReferenceEquals(zero, DepotTaskLedgers.For(1)), "each its own");
        AssertTrue(DepotTaskLedgers.For(2) == null && DepotTaskLedgers.For(-1) == null, "alliances 0 and 1 only");
        var s = DepotWorld(out var plan, out var a);
        zero.Sync(plan, a, s, null);
        AssertEqual(1, zero.Tasks.Count, "fixture: a task");
        CampaignScope.ResetAll();
        AssertEqual(0, DepotTaskLedgers.For(0).Tasks.Count, "a new campaign starts with no task (CampaignScope)");
    }

    private static void DepotTransitionsNameTheirReasons()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        ledger.RecordWished(0, s.DaySerial, 250f, 0f);
        ledger.RecordBuilt(0, s.DaySerial);
        var t = ledger.Transitions;
        AssertEqual(3, t.Count, "start, wish, built");
        AssertEqual("task-started", t[0].Reason, "the start");
        AssertEqual(DepotTaskState.Marching, t[0].State, "marching");
        AssertEqual("wish-placed", t[1].Reason, "the wish");
        AssertEqual(DepotTaskState.Wished, t[1].State, "wished");
        AssertEqual("built", t[2].Reason, "the depot stands");
        AssertEqual("corps1:1", t[2].BuilderUnitKey, "the builder's unit key rides on the row");
        ledger.ClearTransitions();
        AssertEqual(0, ledger.Transitions.Count, "drained");
        var again = new DepotTaskLedger();
        again.Sync(plan, a, s, null);
        again.Abandon(0, "builder-refused", s.DaySerial);
        AssertEqual("builder-refused", again.Transitions[again.Transitions.Count - 1].Reason, "a caller's reason is kept");
    }

    private static void DepotBuiltWhenABuiltDepotStandsNearTheSite()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        var task = ledger.Tasks[0];
        AssertFalse(DepotTaskLedger.SiteBuilt(s, task, 30f), "no depot: not built");
        s.Supply.Add(new SupplySourceRecord { Id = 9001, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 20f, Built = false, Condition = 0.1f });
        AssertFalse(DepotTaskLedger.SiteBuilt(s, task, 30f), "a depot under construction (constructiontimer > 0) is not built yet");
        s.Supply[s.Supply.Count - 1] = new SupplySourceRecord { Id = 9001, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 20f, Built = true, Condition = 1f };
        AssertTrue(DepotTaskLedger.SiteBuilt(s, task, 30f), "a finished depot within the placement radius of the wish");
        AssertFalse(DepotTaskLedger.SiteBuilt(s, task, 15f), "...but not one outside it (the wish places a building within buildingsitemaxradius)");
        s.Supply[s.Supply.Count - 1] = new SupplySourceRecord { Id = 9002, Kind = SupplySourceKind.Iip, Owner = 0, X = 250f, Z = 0f, Built = true };
        AssertFalse(DepotTaskLedger.SiteBuilt(s, task, 30f), "an IIP is not a depot");
        AssertFalse(DepotTaskLedger.SiteBuilt(null, task, 30f) || DepotTaskLedger.SiteBuilt(s, task, 0f), "no snapshot / no radius: not built");
    }

    private static void DepotFlagOffReleasesEveryActiveTask()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        ledger.RecordWished(0, s.DaySerial, 250f, 0f);
        AssertTrue(ledger.HoldsBuilder(1), "fixture: a held builder");
        ledger.AbandonActive("flag-off", s.DaySerial);
        AssertFalse(ledger.HoldsBuilder(1), "no builder stays held once the steering is off");
        AssertEqual(DepotTaskState.Abandoned, ledger.Tasks[0].State, "abandoned");
        AssertEqual("flag-off", ledger.Transitions[ledger.Transitions.Count - 1].Reason, "with the reason");
        ledger.AbandonActive("flag-off", s.DaySerial);
        AssertEqual(1, ledger.Tasks.Count, "idempotent");
    }

    // FollowPlan's rebind: the allocation may name another formation for the task, but never one the planner may not move.
    private static void DepotRebindNeverSeatsAPlayerFormation()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        AssertEqual(1, ledger.Tasks[0].BuilderInstanceId, "fixture: builder 1");
        var player = s.Own[s.IndexOfOwn(3)];
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own[s.IndexOfOwn(3)] = player;
        a.Assignments[0] = ExecutorFixtures.Assign(3, FormationRole.DepotBuilder, 3, 250f, 0f);
        ledger.Sync(plan, a, s, null);
        AssertEqual(1, ledger.Tasks[0].BuilderInstanceId, "the allocation now names the map-group player's formation: the task keeps its builder");
        a.Assignments[0] = ExecutorFixtures.Assign(2, FormationRole.DepotBuilder, 3, 250f, 0f);
        ledger.Sync(plan, a, s, null);
        AssertEqual(2, ledger.Tasks[0].BuilderInstanceId, "a movable formation the allocation names does take the task over");
    }

    // The hold is checked before the allocation lookup: a builder the allocation no longer knows (it is Busy, not a role) is still held.
    private static void DepotBuilderHeldButNotAllocatedIsWithheld()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        var s = ExecutorFixtures.Wide();
        var stranger = ExecutorFixtures.Corps(4242, 300f, 9000f);
        stranger.UnitKey = "stranger:1";
        s.Own.Add(stranger);
        var ledger = DepotTaskLedgers.For(0);
        ledger.Restore(new[]
        {
            new DepotTaskRecord { X = 300f, Z = 0f, SectorId = 1, BuilderInstanceId = -1, BuilderUnitKey = "stranger:1", State = DepotTaskState.Wished, StartedDay = ExecutorFixtures.Day, EndedDay = -1 }
        });
        ledger.Sync(new TheaterPlan { Alliance = 0, DaySerial = s.DaySerial }, new Allocation { Alliance = 0 }, s, null);
        AssertTrue(ledger.HoldsBuilder(4242), "fixture: held");
        Assignment none;
        AssertFalse(CampaignPlanStore.TryGetAllocation(0, out var published) && published.TryGet(4242, out none), "fixture: the published allocation does not know the formation");
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, 4242), "held and unallocated: withheld from vanilla's offensive pool");
        AssertTrue(CampaignWithholdView.WithheldFromDefense(0, 4242, 100f, 0f), "...and its defensive pool");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, 4243), "a formation nobody holds or allocates is vanilla's");
        CampaignScope.ResetAll();
    }

    // F2: the abandon day rides in the record, so the cooldown is a day difference that a save and a reload keep.
    private static void DepotCooldownIsADayDifferenceThatSurvivesARestore()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        AssertEqual(-1, ledger.Tasks[0].EndedDay, "an active task has no end day");
        ledger.Abandon(0, "wish-failed", s.DaySerial);
        AssertEqual(s.DaySerial, ledger.Tasks[0].EndedDay, "an abandoned task carries the day it ended");
        var saved = new List<DepotTaskRecord>(ledger.Tasks);
        var reloaded = new DepotTaskLedger();
        reloaded.Restore(saved);
        AssertEqual(s.DaySerial, reloaded.Tasks[0].EndedDay, "...through Restore");
        var soon = DepotWorld(out var p2, out var a2, DepotTaskLedger.RetryAfterAbandonDays - 1);
        p2.DepotTasks.RemoveAt(1); // only the abandoned site is in the plan, so no other task takes the slot
        a2.Assignments.RemoveAt(1);
        reloaded.Sync(p2, a2, soon, null);
        bool restarted = false;
        for (int i = 0; i < reloaded.Tasks.Count; i++)
            if (reloaded.Tasks[i].State == DepotTaskState.Marching && Math.Abs(reloaded.Tasks[i].X - 250f) < 1f) restarted = true;
        AssertFalse(restarted, "after a reload the site still cools down");
        var after = DepotWorld(out var p3, out var a3, DepotTaskLedger.RetryAfterAbandonDays);
        p3.DepotTasks.RemoveAt(1);
        a3.Assignments.RemoveAt(1);
        reloaded.Sync(p3, a3, after, null);
        for (int i = 0; i < reloaded.Tasks.Count; i++)
            if (reloaded.Tasks[i].State == DepotTaskState.Marching && Math.Abs(reloaded.Tasks[i].X - 250f) < 1f) restarted = true;
        AssertTrue(restarted, "and may start again once the window has passed");
        var built = new DepotTaskLedger();
        var s4 = DepotWorld(out var p4, out var a4);
        built.Sync(p4, a4, s4, null);
        built.RecordBuilt(0, s4.DaySerial + 4);
        AssertEqual(s4.DaySerial + 4, built.Tasks[0].EndedDay, "a built task carries its day too");
    }

    private static FormationRecord DepotBuilderAt(float x, float z, FormationFlags flags)
    {
        var f = ExecutorFixtures.Corps(1, x, 9000f);
        f.Z = z;
        f.Flags |= flags;
        return f;
    }

    private static DepotWishFacts Facts(int day, bool march = false, bool depotAtSite = false)
    {
        return new DepotWishFacts { DaySerial = day, MarchIssuedEarlier = march, OwnUnbuiltDepotAtSite = depotAtSite };
    }

    // F6: when the wish is placed.
    private static void DepotWishMomentIsHereAdoptAheadOrWait()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        float reach = 10f;
        int later = s.DaySerial + 1;
        AssertEqual(DepotWishMoment.Here, ledger.WishMoment(0, DepotBuilderAt(255f, 0f, FormationFlags.None), reach, 9f, Facts(later)), "within reach of the site: wish at its position");
        AssertEqual(DepotWishMoment.Adopt, ledger.WishMoment(0, DepotBuilderAt(255f, 0f, FormationFlags.VanillaDepotBuilder), reach, 9f, Facts(later, depotAtSite: true)), "...and vanilla's job already raises a depot for it at the site: adopt that one");
        AssertEqual(DepotWishMoment.Ahead, ledger.WishMoment(0, DepotBuilderAt(300f, 0f, FormationFlags.None), reach, DepotTaskLedger.AheadDays, Facts(later, march: true)), "marching there on an earlier review's move and arriving within a review day: wish now, at the site");
        AssertEqual(DepotWishMoment.Wait, ledger.WishMoment(0, DepotBuilderAt(300f, 0f, FormationFlags.None), reach, DepotTaskLedger.AheadDays + 0.01f, Facts(later, march: true)), "arriving later: wait");
        AssertEqual(DepotWishMoment.Wait, ledger.WishMoment(0, DepotBuilderAt(300f, 0f, FormationFlags.VanillaDepotBuilder), reach, 9f, Facts(later, depotAtSite: true)), "a builder far from the site next to some other depot is not adopted");
        AssertEqual(DepotWishMoment.Wait, ledger.WishMoment(0, DepotBuilderAt(300f, 0f, FormationFlags.None), reach, float.MaxValue, Facts(later, march: true)), "no speed estimate: wait");
        AssertEqual(DepotWishMoment.Wait, ledger.WishMoment(1, DepotBuilderAt(250f, 0f, FormationFlags.None), reach, 0f, Facts(later)), "no such task");
        ledger.RecordWished(0, s.DaySerial, 250f, 0f, "vanilla-depot-adopted");
        AssertEqual("vanilla-depot-adopted", ledger.Transitions[ledger.Transitions.Count - 1].Reason, "the transition names how the task became Wished");
        AssertEqual(DepotWishMoment.Wait, ledger.WishMoment(0, DepotBuilderAt(250f, 0f, FormationFlags.None), reach, 0f, Facts(later)), "a wished task is not wished again, whatever the builder does");
    }

    // F6a: an arrival estimate is a lower bound, and the wish is paid at once (97506, 96211-96257), so Ahead needs a builder that is
    // on its way: a move toward this site issued at an earlier review, on a task that did not start today.
    private static void DepotAheadNeedsAnEarlierMoveTowardTheSite()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        var far = DepotBuilderAt(300f, 0f, FormationFlags.None);
        int day = s.DaySerial;
        AssertEqual(day, ledger.Tasks[0].StartedDay, "fixture: the task started today");
        AssertEqual(DepotWishMoment.Wait, ledger.WishMoment(0, far, 10f, 0.5f, Facts(day, march: true)), "the review the task starts: no Ahead, whatever the tracker says");
        AssertEqual(DepotWishMoment.Wait, ledger.WishMoment(0, far, 10f, 0.5f, Facts(day + 1, march: false)), "a later review but no move toward the site on the tracker: no Ahead on the estimate alone");
        AssertEqual(DepotWishMoment.Ahead, ledger.WishMoment(0, far, 10f, 0.5f, Facts(day + 1, march: true)), "a later review, a move on its way, arriving within a day: Ahead");
        AssertEqual(DepotWishMoment.Wait, ledger.WishMoment(0, far, 10f, DepotTaskLedger.AheadDays + 0.5f, Facts(day + 1, march: true)), "...but not when it arrives later than a review day");
        AssertEqual(DepotWishMoment.Here, ledger.WishMoment(0, DepotBuilderAt(255f, 0f, FormationFlags.None), 10f, 0f, Facts(day)), "arrived on the start day: Here needs no move (it stands there)");
        string runtime = Src("Strategic/Planner/DepotSteeringRuntime.cs");
        string watch = BodyOf(runtime, "private static void PlaceWishesAndWatchBuilds(");
        AssertTrue(HasLine(watch, "MarchIssuedEarlier = MoveTrackers.For(alliance).MarchingToward(builder.InstanceId, task.X, task.Z, DepotTaskLedger.SiteMatchRadius, snapshot.DaySerial)"), "the runtime reads the mover's tracker for a move toward the task's site");
    }

    private static void MoveTrackerNamesAMarchTowardASite()
    {
        var t = new MoveTracker();
        int day = ExecutorFixtures.Day;
        AssertFalse(t.MarchingToward(1, 250f, 0f, 25f, day), "no record: not marching");
        t.RecordIssued(1, 0f, 0f, 250f, 0f, day, false);
        AssertFalse(t.MarchingToward(1, 250f, 0f, 25f, day), "issued at this review: not an earlier one");
        AssertTrue(t.MarchingToward(1, 250f, 0f, 25f, day + 1), "issued at an earlier review and not yet judged");
        AssertTrue(t.MarchingToward(1, 270f, 0f, 25f, day + 1), "a site that moved a little with the frontier is the same site");
        AssertFalse(t.MarchingToward(1, 400f, 0f, 25f, day + 1), "a move toward some other place is not this march");
        AssertEqual(MoveOutcome.Confirmed, t.Judge(1, 100f, 0f, 250f, 0f, day + 1), "fixture: the move is judged underway");
        AssertTrue(t.MarchingToward(1, 250f, 0f, 25f, day + 2), "a confirmed (underway) move still counts");
        var queued = new MoveTracker();
        queued.RecordIssued(2, 0f, 0f, 250f, 0f, day, true);
        AssertFalse(queued.MarchingToward(2, 250f, 0f, 25f, day + 1), "a queued move has not started");
        var failed = new MoveTracker();
        failed.RecordIssued(3, 0f, 0f, 250f, 0f, day, false);
        AssertEqual(MoveOutcome.Failed, failed.Judge(3, 0f, 0f, 0f, 0f, day + 1), "fixture: the move did not take");
        AssertFalse(failed.MarchingToward(3, 250f, 0f, 25f, day + 2), "a failed move is not a march");
    }

    // F6a: a Wished task whose depot never rose, or was removed, is released at the next review.
    private static void DepotWishLostWhenNoOwnDepotStandsAtTheSite()
    {
        float radius = 50f;
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        ledger.RecordWished(0, s.DaySerial, 250f, 0f);
        ledger.WatchWish(0, s, radius);
        AssertEqual(DepotTaskState.Wished, ledger.Tasks[0].State, "the wish's own day: the placeholder is not yet on the ground, so nothing is judged");
        var next = DepotWorld(out _, out _, 1);
        ledger.WatchWish(0, next, radius);
        AssertEqual(DepotTaskState.Abandoned, ledger.Tasks[0].State, "a later review with no own depot near the site: lost");
        AssertEqual("wish-lost", ledger.Transitions[ledger.Transitions.Count - 1].Reason, "named wish-lost");
        AssertEqual(next.DaySerial, ledger.Tasks[0].EndedDay, "with the day it ended");
        AssertFalse(ledger.HoldsBuilder(1), "and the builder is released at once");

        var kept = new DepotTaskLedger();
        var s2 = DepotWorld(out var p2, out var a2);
        kept.Sync(p2, a2, s2, null);
        kept.RecordWished(0, s2.DaySerial, 250f, 0f);
        var n2 = DepotWorld(out _, out _, 1);
        n2.Supply.Add(new SupplySourceRecord { Id = 9100, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 30f, Built = false, Condition = 0.2f });
        kept.WatchWish(0, n2, radius);
        AssertEqual(DepotTaskState.Wished, kept.Tasks[0].State, "an own depot still rising at the site keeps the task");
        n2.Supply[n2.Supply.Count - 1] = new SupplySourceRecord { Id = 9100, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 30f, Built = true, Condition = 1f };
        kept.WatchWish(0, n2, radius);
        AssertEqual(DepotTaskState.Built, kept.Tasks[0].State, "a standing one finishes it");
        AssertEqual(n2.DaySerial, kept.Tasks[0].EndedDay, "...on the day it was seen standing");

        foreach (var other in new[]
        {
            new SupplySourceRecord { Id = 9101, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 80f, Built = false },     // an own depot, but not at this site
            new SupplySourceRecord { Id = 9102, Kind = SupplySourceKind.Depot, Owner = 1, X = 250f, Z = 10f, Built = false },     // not ours
            new SupplySourceRecord { Id = 9104, Kind = SupplySourceKind.Iip, Owner = 0, X = 250f, Z = 10f, Built = true }            // an IIP is not a depot
        })
        {
            var l = new DepotTaskLedger();
            var s3 = DepotWorld(out var p3, out var a3);
            l.Sync(p3, a3, s3, null);
            l.RecordWished(0, s3.DaySerial, 250f, 0f);
            var n3 = DepotWorld(out _, out _, 1);
            n3.Supply.Add(other);
            l.WatchWish(0, n3, radius);
            AssertEqual(DepotTaskState.Abandoned, l.Tasks[0].State, "does not keep the task: supply source " + other.Id);
        }
        string runtime = Src("Strategic/Planner/DepotSteeringRuntime.cs");
        string watch = BodyOf(runtime, "private static void PlaceWishesAndWatchBuilds(");
        AssertTrue(HasLine(watch, "ledger.WatchWish(t, snapshot, builtRadius);"), "the runtime watches every wished task each review");
    }

    // F6b: the VanillaDepotBuilder flag means an own-started depot stands within the builder's command range (50-100 for a corps or
    // army), which need not be this site's; adopt only a depot at the site.
    private static void DepotAdoptNeedsAnOwnUnbuiltDepotAtTheSite()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        var flagged = DepotBuilderAt(250f, 0f, FormationFlags.VanillaDepotBuilder);
        AssertEqual(DepotWishMoment.Here, ledger.WishMoment(0, flagged, 10f, 0f, Facts(s.DaySerial, depotAtSite: false)), "flagged, but the depot it is raising is not at the site: its own wish");
        AssertEqual(DepotWishMoment.Adopt, ledger.WishMoment(0, flagged, 10f, 0f, Facts(s.DaySerial, depotAtSite: true)), "flagged and a depot rising at the site: adopted");
        AssertEqual(DepotWishMoment.Here, ledger.WishMoment(0, DepotBuilderAt(250f, 0f, FormationFlags.None), 10f, 0f, Facts(s.DaySerial, depotAtSite: true)), "not flagged: vanilla's job does not own that depot, wish");

        float builtRadius = 50f;
        var task = ledger.Tasks[0];
        var world = DepotWorld(out _, out _);
        world.Supply.Add(new SupplySourceRecord { Id = 9200, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 80f, Built = false });
        AssertFalse(DepotTaskLedger.SiteHasOwnDepot(world, task, builtRadius, true), "an own unbuilt depot 80 away (inside a corps' command range of 100, outside the placement radius of 50) is not at the site");
        world.Supply.Add(new SupplySourceRecord { Id = 9201, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 40f, Built = false });
        AssertTrue(DepotTaskLedger.SiteHasOwnDepot(world, task, builtRadius, true), "one 40 away is");
        AssertTrue(DepotTaskLedger.NearestOwnDepot(world, task, builtRadius, true, 0f, 0f, float.PositiveInfinity, out float nx, out float nz) && nx == 250f && nz == 40f, "and its position is the one adopted");
        world.Supply.Add(new SupplySourceRecord { Id = 9204, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 20f, Built = false });
        AssertTrue(DepotTaskLedger.NearestOwnDepot(world, task, builtRadius, true, 0f, 0f, float.PositiveInfinity, out nx, out nz) && nx == 250f && nz == 20f, "with two, the nearer to the site");
        world.Supply.Add(new SupplySourceRecord { Id = 9205, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 45f, Built = false });
        AssertTrue(DepotTaskLedger.NearestOwnDepot(world, task, builtRadius, true, 0f, 0f, float.PositiveInfinity, out nx, out nz) && nx == 250f && nz == 20f, "a farther one listed later does not displace it");
        ledger.RecordWished(0, s.DaySerial, nx, nz, "vanilla-depot-adopted");
        AssertTrue(ledger.Tasks[0].WishX == 250f && ledger.Tasks[0].WishZ == 20f, "an adopted task is looked for around the adopted depot");
        var builtOnly = DepotWorld(out _, out _);
        builtOnly.Supply.Add(new SupplySourceRecord { Id = 9202, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 5f, Built = true });
        AssertFalse(DepotTaskLedger.SiteHasOwnDepot(builtOnly, task, builtRadius, true), "a standing depot is not one to adopt (the unbuilt filter)");
        AssertTrue(DepotTaskLedger.SiteHasOwnDepot(builtOnly, task, builtRadius, false), "...but is one that keeps a wish");
        var ally = DepotWorld(out _, out _);
        ally.Supply.Add(new SupplySourceRecord { Id = 9203, Kind = SupplySourceKind.Depot, Owner = 1, X = 250f, Z = 5f, Built = false });
        AssertFalse(DepotTaskLedger.SiteHasOwnDepot(ally, task, builtRadius, true), "another alliance's depot is not ours");
        AssertFalse(DepotTaskLedger.SiteHasOwnDepot(null, task, builtRadius, true) || DepotTaskLedger.SiteHasOwnDepot(world, task, 0f, true), "no snapshot / no radius: none");
        string watch = BodyOf(Src("Strategic/Planner/DepotSteeringRuntime.cs"), "private static void PlaceWishesAndWatchBuilds(");
        AssertTrue(HasLine(watch, "facts.OwnUnbuiltDepotAtSite = DepotTaskLedger.NearestOwnDepot(snapshot, task, builtRadius, true, builder.X, builder.Z, builder.BugleRange, out facts.AdoptX, out facts.AdoptZ);"), "the runtime reads the snapshot for the nearest own unbuilt depot within the placement radius of the site and the builder's bugle range, and keeps its position");
    }

    // The wish is placed at the builder, which can be up to its bugle range from the site, and the depot rises within
    // buildingsitemaxradius of the wish (97557-97585): Built and wish-lost look around the wish position, not the site.
    private static void DepotIsLookedForWhereTheWishWasPlaced()
    {
        float radius = 50f;
        // A corps builder wishing from 70 away: the depot rises 30 from the wish, 100 from the site.
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        ledger.RecordWished(0, s.DaySerial, 250f, 70f);
        AssertEqual(70f, ledger.Tasks[0].WishZ, "the record keeps where the wish was placed");
        var rising = DepotWorld(out _, out _, 1);
        rising.Supply.Add(new SupplySourceRecord { Id = 9300, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 100f, Built = false });
        ledger.WatchWish(0, rising, radius);
        AssertEqual(DepotTaskState.Wished, ledger.Tasks[0].State, "a depot rising outside the radius of the site but inside it of the wish keeps the task (not wish-lost)");
        var built = DepotWorld(out _, out _, 2);
        built.Supply.Add(new SupplySourceRecord { Id = 9300, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 100f, Built = true });
        ledger.WatchWish(0, built, radius);
        AssertEqual(DepotTaskState.Built, ledger.Tasks[0].State, "...and once it stands the task reads Built");
        AssertEqual("built", ledger.Transitions[ledger.Transitions.Count - 1].Reason, "not wish-lost");

        // The depot at the wish position vanishes: lost, and a depot near the site alone does not keep a wish placed elsewhere.
        var lost = new DepotTaskLedger();
        var s2 = DepotWorld(out var p2, out var a2);
        lost.Sync(p2, a2, s2, null);
        lost.RecordWished(0, s2.DaySerial, 250f, 70f);
        var gone = DepotWorld(out _, out _, 1);
        lost.WatchWish(0, gone, radius);
        AssertEqual(DepotTaskState.Abandoned, lost.Tasks[0].State, "no depot at the wish position: lost");
        AssertEqual("wish-lost", lost.Transitions[lost.Transitions.Count - 1].Reason, "named wish-lost");

        var elsewhere = new DepotTaskLedger();
        var s3 = DepotWorld(out var p3, out var a3);
        elsewhere.Sync(p3, a3, s3, null);
        elsewhere.RecordWished(0, s3.DaySerial, 250f, 70f);
        var nearSite = DepotWorld(out _, out _, 1);
        nearSite.Supply.Add(new SupplySourceRecord { Id = 9301, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 5f, Built = true });
        elsewhere.WatchWish(0, nearSite, radius);
        AssertEqual(DepotTaskState.Abandoned, elsewhere.Tasks[0].State, "a standing depot 5 from the site is 65 from the wish: it is not this wish's depot, so the wish is lost");
    }

    // ---- verify r3 F7: the allocator never re-tasks the builder of a Wished task ----------------------------------------------

    private static FormationRecord BuilderAtWish(int id, float x, float z, float waypointX, float waypointZ)
    {
        var f = ExecutorFixtures.Corps(id, x, 9000f);
        f.Z = z;
        f.WaypointX = waypointX;
        f.WaypointZ = waypointZ;
        return f;
    }

    private static void PinnedBuilderListsOnlyWishedTasksOfMovableFormations()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        var pinned = new List<PinnedBuilder>();
        ledger.Sync(plan, a, s, null);
        ledger.CollectPinnedBuilders(pinned);
        AssertEqual(0, pinned.Count, "a Marching task's builder follows the allocation: not pinned");
        ledger.RecordWished(0, s.DaySerial, 250f, 70f);
        ledger.CollectPinnedBuilders(pinned);
        AssertEqual(1, pinned.Count, "a Wished task's builder is pinned");
        AssertTrue(pinned[0].InstanceId == 1 && pinned[0].TargetX == 250f && pinned[0].TargetZ == 70f && pinned[0].SectorId == 3, "...at the wish position, in the task's sector");
        ledger.Abandon(0, "timeout", s.DaySerial + 1);
        ledger.CollectPinnedBuilders(pinned);
        AssertEqual(0, pinned.Count, "released when the task ends");
        var built = new DepotTaskLedger();
        var s2 = DepotWorld(out var p2, out var a2);
        built.Sync(p2, a2, s2, null);
        built.RecordWished(0, s2.DaySerial, 250f, 0f);
        built.RecordBuilt(0, s2.DaySerial + 1);
        built.CollectPinnedBuilders(pinned);
        AssertEqual(0, pinned.Count, "and when the depot stands");
        // A builder the planner may not move never keeps a task, so it is never listed.
        var player = new DepotTaskLedger();
        var s3 = DepotWorld(out var p3, out var a3);
        player.Sync(p3, a3, s3, null);
        player.RecordWished(0, s3.DaySerial, 250f, 0f);
        var own = s3.Own[s3.IndexOfOwn(1)];
        own.Chain = WlChainClass.PlayerMapGroup;
        s3.Own[s3.IndexOfOwn(1)] = own;
        player.Sync(p3, a3, s3, null);
        player.CollectPinnedBuilders(pinned);
        AssertEqual(0, pinned.Count, "the W&L map-group player's formation is dropped from the task, so never pinned");
    }

    // The pin, through the real allocator and the real mover review.
    private static void WishedBuilderKeepsItsRoleWhateverThePlanDoes()
    {
        var s = ExecutorFixtures.Wide(1);
        s.Own.Add(BuilderAtWish(1, 250f, 70f, 250f, 70f));       // standing at its wish, 70 from the site
        s.Own.Add(ExecutorFixtures.Corps(2, 300f, 15000f));      // the mass
        var plan = ExecutorFixtures.HandPlan(s, new[] { 2 });    // no DepotTasks: the plan dropped the task
        var sectors = ExecutorFixtures.Sectors(s);
        int sector = sectors.SectorAt(250f, 70f);
        var pin = new[] { new PinnedBuilder { InstanceId = 1, SectorId = sector, TargetX = 250f, TargetZ = 70f } };

        var free = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        AssertTrue(free.TryGet(1, out var loose) && loose.Role != FormationRole.DepotBuilder, "control: with no pin the dropped task leaves the builder to the allocation (" + loose.Role + ")");
        var hostFree = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, free, ExecutorFixtures.OpenTempo()), hostFree, new MoveTracker(), ExecutorFixtures.State());
        AssertTrue(hostFree.Moves.Exists(m => m.id == 1), "control: and the mover marches it off its wish");

        var pinned = ForceAllocator.Allocate(plan, s, sectors, null, null, null, null, pin);
        AssertTrue(pinned.TryGet(1, out var held), "the pinned builder is allocated");
        AssertEqual(FormationRole.DepotBuilder, held.Role, "it keeps the DepotBuilder role although the plan dropped the task");
        AssertTrue(held.TargetX == 250f && held.TargetZ == 70f && held.Withheld, "at the wish position, withheld");
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, pinned, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertFalse(host.Moves.Exists(m => m.id == 1), "the mover sends it no move");

        // A smaller formation entering the sector, and a plan task in the same sector: the builder is not re-picked away.
        var s2 = ExecutorFixtures.Wide(1);
        s2.Own.Add(BuilderAtWish(1, 230f, 70f, 230f, 70f));
        var small = ExecutorFixtures.Corps(3, 235f, 2000f);
        small.Z = 70f;
        s2.Own.Add(small);
        s2.Own.Add(ExecutorFixtures.Corps(2, 300f, 15000f));
        var plan2 = ExecutorFixtures.HandPlan(s2, new[] { 2 });
        int sector2 = ExecutorFixtures.Sectors(s2).SectorAt(230f, 70f);
        plan2.DepotTasks.Add(new DepotTask { X = 235f, Z = 70f, SectorId = sector2, Priority = 1f });
        var pin2 = new[] { new PinnedBuilder { InstanceId = 1, SectorId = sector2, TargetX = 230f, TargetZ = 70f } };
        var a2 = ForceAllocator.Allocate(plan2, s2, ExecutorFixtures.Sectors(s2), null, null, null, null, pin2);
        AssertTrue(a2.TryGet(1, out var still) && still.Role == FormationRole.DepotBuilder && still.TargetX == 230f && still.TargetZ == 70f, "a smaller formation in the sector does not take its role or move its target");
        AssertTrue(a2.TryGet(3, out var other) && other.Role == FormationRole.DepotBuilder, "the plan's own task gets the smaller formation (" + other.Role + ")");
    }

    private static void AheadBuilderStaysOnItsMarchToTheSite()
    {
        var s = ExecutorFixtures.Wide(1);
        s.Own.Add(BuilderAtWish(1, 300f, 0f, 250f, 0f));         // still marching: 50 out, its last waypoint is the site
        s.Own.Add(ExecutorFixtures.Corps(2, 330f, 15000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 2 });
        var sectors = ExecutorFixtures.Sectors(s);
        var pin = new[] { new PinnedBuilder { InstanceId = 1, SectorId = sectors.SectorAt(250f, 0f), TargetX = 250f, TargetZ = 0f } };
        var tracker = new MoveTracker();
        tracker.RecordIssued(1, 400f, 0f, 250f, 0f, s.DaySerial - 1, false);

        var pinned = ForceAllocator.Allocate(plan, s, sectors, null, null, null, null, pin);
        AssertTrue(pinned.TryGet(1, out var held) && held.Role == FormationRole.DepotBuilder && held.TargetX == 250f && held.TargetZ == 0f, "an Ahead wish's builder keeps the role, its target the site it is marching to");
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, pinned, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        AssertFalse(host.Moves.Exists(m => m.id == 1), "the mover issues no new move: the march stands");
        AssertTrue(tracker.MarchingToward(1, 250f, 0f, 25f, s.DaySerial + 1), "and the tracker still has it marching to the site");

        var free = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        AssertTrue(free.TryGet(1, out var loose) && loose.Role != FormationRole.DepotBuilder, "control: with no pin the allocation re-tasks it (" + loose.Role + ")");
    }

    private static void PinnedBuilderIsNeverTheMapGroupPlayerAndIsNoReserve()
    {
        // A pin that names a formation the planner may not move is ignored.
        var s = ExecutorFixtures.Wide(1);
        s.Own.Add(ExecutorFixtures.Corps(2, 300f, 15000f));
        var player = ExecutorFixtures.Corps(1, 550f, 6000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        var plan = ExecutorFixtures.HandPlan(s, new[] { 2 });
        var sectors = ExecutorFixtures.Sectors(s);
        var pinPlayer = new[] { new PinnedBuilder { InstanceId = 1, SectorId = sectors.SectorAt(550f, 0f), TargetX = 550f, TargetZ = 0f } };
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null, null, pinPlayer);
        AssertTrue(a.TryGet(1, out var x) && x.Role != FormationRole.DepotBuilder, "the map-group player is never made a builder by a pin");
        var cic = ExecutorFixtures.Wide(1);
        var own = ExecutorFixtures.Corps(1, 300f, 9000f);
        own.Chain = WlChainClass.PlayerCommand;
        cic.Own.Add(own);
        var plan2 = ExecutorFixtures.HandPlan(cic, new int[0]);
        var a2 = ForceAllocator.Allocate(plan2, cic, ExecutorFixtures.Sectors(cic), null, null, null, null, new[] { new PinnedBuilder { InstanceId = 1, SectorId = 0, TargetX = 300f, TargetZ = 0f } });
        AssertTrue(a2.TryGet(1, out var y) && y.Role != FormationRole.DepotBuilder, "nor the player's own command");
        AssertEqual(s.Own.Count, ForceAllocator.Allocate(plan, s, sectors, null, null, null, null, new[] { new PinnedBuilder { InstanceId = 99, SectorId = 0, TargetX = 0f, TargetZ = 0f } }).Assignments.Count, "a pin naming no formation changes nothing");

        // A pinned builder is no capital reserve: the guard is raised from the others.
        var c = ExecutorFixtures.Wide(1);
        c.Own.Add(BuilderAtWish(1, 20f, 70f, 20f, 70f));
        c.Own.Add(ExecutorFixtures.Corps(4, 100f, 9000f));
        c.Own.Add(ExecutorFixtures.Corps(2, 300f, 15000f));
        var cp = ExecutorFixtures.HandPlan(c, new[] { 2 });
        cp.CapitalFloorStrength = 5000f;
        var csectors = ExecutorFixtures.Sectors(c);
        var cpin = new[] { new PinnedBuilder { InstanceId = 1, SectorId = csectors.SectorAt(20f, 70f), TargetX = 20f, TargetZ = 70f } };
        var unpinned = ForceAllocator.Allocate(cp, c, csectors, null, null, null);
        AssertTrue(unpinned.TryGet(1, out var u) && u.Role == FormationRole.CapitalGuard, "control: unpinned, the formation nearest the capital is the capital guard");
        var guarded = ForceAllocator.Allocate(cp, c, csectors, null, null, null, null, cpin);
        AssertTrue(guarded.TryGet(1, out var b) && b.Role == FormationRole.DepotBuilder, "the pinned builder nearest the capital stays the builder");
        AssertTrue(guarded.TryGet(4, out var g) && g.Role == FormationRole.CapitalGuard, "and the next formation is the capital guard: the floor is met without it");
    }

    private static void AllocatorAndPlannerWiringPinsTheWishedBuilders()
    {
        string planner = Src("Strategic/Planner/CampaignPlannerRuntime.cs");
        AssertTrue(HasLine(planner, "DepotTaskLedgers.For(alliance)?.CollectPinnedBuilders(pinnedBuilders);"), "the review reads the ledger's pins");
        AssertTrue(HasLine(planner, "allocation = ForceAllocator.Allocate(plan, snapshot, sectors, HomeTheaterPrior.ForAlliance(alliance), ReconTasks[alliance], Recon[alliance], gaveUp, pinnedBuilders);"), "and hands them to the allocator");
        string allocator = Src("Strategic/Planner/ForceAllocator.cs");
        string run = BodyOf(allocator, "internal void Run(Allocation allocation)");
        AssertTrue(run.IndexOf("AssignPinnedBuilders()", StringComparison.Ordinal) < run.IndexOf("AssignImmobile()", StringComparison.Ordinal), "the pinned builders are handed out first");
        AssertTrue(HasLine(run, "ReserveRules.Capital(_s, _plan.CapitalFloorStrength, capitalReserved, pinnedBuilder);"), "the capital reserve does not count them");
        string pin = BodyOf(allocator, "private bool[] AssignPinnedBuilders()");
        AssertTrue(HasLine(pin, "if (i < 0 || _done[i] || !WlChainRules.MayMove(_s.Own[i].Chain)) continue;"), "only a formation the planner may move");
        AssertTrue(HasLine(pin, "Assign(i, FormationRole.DepotBuilder, _pinned[k].SectorId, _pinned[k].TargetX, _pinned[k].TargetZ, ReasonDepotTask, true);"), "as DepotBuilder at the pinned position, withheld");
        string mover = Src("Strategic/Planner/CampaignMover.cs");
        AssertFalse(mover.Contains("DepotTask") || mover.Contains("PinnedBuilder"), "the mover reads no depot state: the allocator's assignment is the one rule");
    }

    // F8: a depot rises only while an own unit stands within that unit's bugle range of it (96626-96655).
    private static void AdoptNeedsTheDepotWithinTheBuildersBugleRange()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        var task = ledger.Tasks[0];
        var world = DepotWorld(out _, out _);
        world.Supply.Add(new SupplySourceRecord { Id = 9400, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 40f, Built = false });
        AssertFalse(DepotTaskLedger.NearestOwnDepot(world, task, 50f, true, 250f, 0f, 5f, out _, out _), "a depot 40 from the builder, bugle range 5: not one it can raise");
        AssertTrue(DepotTaskLedger.NearestOwnDepot(world, task, 50f, true, 250f, 0f, 50f, out float x, out float z) && x == 250f && z == 40f, "bugle range 50: it is");
        world.Supply.Add(new SupplySourceRecord { Id = 9401, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 20f, Built = false });
        AssertTrue(DepotTaskLedger.NearestOwnDepot(world, task, 50f, true, 250f, 0f, 30f, out x, out z) && z == 20f, "of two, the one in range");
        AssertFalse(DepotTaskLedger.NearestOwnDepot(world, task, 50f, true, 250f, 0f, 20f, out _, out _), "the range is strict (96626: distance < buglerange)");
        AssertEqual(DepotWishMoment.Here, ledger.WishMoment(0, DepotBuilderAt(250f, 0f, FormationFlags.VanillaDepotBuilder), 10f, 0f, Facts(s.DaySerial, depotAtSite: false)), "a flagged builder with no depot in its range gets its own wish");
    }

    // F9: the wish is a placeholder at -20000 until vanilla's one shared queue reaches it (97538-97591).
    private static void APendingPlacementIsNotALostWish()
    {
        float radius = 50f;
        var ledger = new DepotTaskLedger();
        var s = DepotWorld(out var plan, out var a);
        ledger.Sync(plan, a, s, null);
        ledger.RecordWished(0, s.DaySerial, 250f, 0f);
        var next = DepotWorld(out _, out _, 1);
        next.Supply.Add(new SupplySourceRecord { Id = 9500, Kind = SupplySourceKind.Depot, Owner = 0, X = -20000f, Z = -20000f, Built = false });
        ledger.WatchWish(0, next, radius);
        AssertEqual(DepotTaskState.Wished, ledger.Tasks[0].State, "an own unbuilt depot at the placeholder is a placement waiting its turn: not lost");
        var days = DepotWorld(out _, out _, 5);
        days.Supply.Add(new SupplySourceRecord { Id = 9500, Kind = SupplySourceKind.Depot, Owner = 0, X = -20000f, Z = -20000f, Built = false });
        ledger.WatchWish(0, days, radius);
        AssertEqual(DepotTaskState.Wished, ledger.Tasks[0].State, "...and for as long as it is queued");
        var timeout = DepotWorld(out var p3, out var a3, DepotTaskLedger.TimeoutDays);
        timeout.Supply.Add(new SupplySourceRecord { Id = 9500, Kind = SupplySourceKind.Depot, Owner = 0, X = -20000f, Z = -20000f, Built = false });
        ledger.Sync(p3, a3, timeout, null);
        AssertEqual(DepotTaskState.Abandoned, ledger.Tasks[0].State, "the wait is bounded by TimeoutDays");
        bool timedOut = false;
        for (int k = 0; k < ledger.Transitions.Count; k++) timedOut |= ledger.Transitions[k].Reason == "timeout";
        AssertTrue(timedOut, "named timeout");

        foreach (var other in new[]
        {
            new SupplySourceRecord { Id = 9501, Kind = SupplySourceKind.Depot, Owner = 1, X = -20000f, Z = -20000f, Built = false },   // another alliance's
            new SupplySourceRecord { Id = 9502, Kind = SupplySourceKind.Depot, Owner = 0, X = -20000f, Z = -20000f, Built = true },    // not a placeholder
            new SupplySourceRecord { Id = 9503, Kind = SupplySourceKind.Depot, Owner = 0, X = 250f, Z = 400f, Built = false },         // a rising depot far from the wish
            new SupplySourceRecord { Id = 9505, Kind = SupplySourceKind.Depot, Owner = 0, X = -20000f, Z = 300f, Built = false },     // out past the threshold in x only
            new SupplySourceRecord { Id = 9504, Kind = SupplySourceKind.Iip, Owner = 0, X = -20000f, Z = -20000f, Built = false }      // not a depot
        })
        {
            var l = new DepotTaskLedger();
            var s4 = DepotWorld(out var p4, out var a4);
            l.Sync(p4, a4, s4, null);
            l.RecordWished(0, s4.DaySerial, 250f, 0f);
            var n4 = DepotWorld(out _, out _, 1);
            n4.Supply.Add(other);
            l.WatchWish(0, n4, radius);
            AssertEqual(DepotTaskState.Abandoned, l.Tasks[0].State, "does not count as a pending placement: supply source " + other.Id);
        }
    }

    // F6c: the fort job's hold is the withhold view's hold, under its plan-freshness and executors-allowed test.
    private static void DepotHoldReachesTheFortJobOnlyUnderAFreshPlan()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        var ledger = DepotTaskLedgers.For(0);
        ledger.Restore(new[]
        {
            new DepotTaskRecord { X = 300f, Z = 0f, SectorId = 1, BuilderInstanceId = -1, BuilderUnitKey = "stranger:1", State = DepotTaskState.Wished, StartedDay = ExecutorFixtures.Day, EndedDay = -1 }
        });
        var snapshot = ExecutorFixtures.Wide();
        var stranger = ExecutorFixtures.Corps(4242, 300f, 9000f);
        stranger.UnitKey = "stranger:1";
        snapshot.Own.Add(stranger);
        ledger.Sync(new TheaterPlan { Alliance = 0, DaySerial = snapshot.DaySerial }, new Allocation { Alliance = 0 }, snapshot, null);
        AssertTrue(ledger.HoldsBuilder(4242), "fixture: the ledger holds the formation");
        AssertTrue(CampaignWithholdView.HoldsDepotBuilder(0, 4242), "a fresh plan, executors allowed: the builder is held");
        AssertFalse(CampaignWithholdView.HoldsDepotBuilder(0, 4243), "someone else is not");
        AssertFalse(CampaignWithholdView.HoldsDepotBuilder(2, 4242) || CampaignWithholdView.HoldsDepotBuilder(-1, 4242), "an alliance with no planner (Europe) holds nothing");
        PlanFreshness.NoteToday(ExecutorFixtures.Day + PlanFreshness.MaxAgeDays);
        AssertTrue(CampaignWithholdView.HoldsDepotBuilder(0, 4242), "two days old: still read, as the other clauses");
        PlanFreshness.NoteToday(ExecutorFixtures.Day + PlanFreshness.MaxAgeDays + 1);
        AssertFalse(CampaignWithholdView.HoldsDepotBuilder(0, 4242), "a stale plan holds nothing from the fort job");
        AssertEqual(CampaignWithholdView.WithheldFromOffense(0, 4242), CampaignWithholdView.HoldsDepotBuilder(0, 4242), "...exactly as it holds nothing from offense");
        PlanFreshness.NoteToday(ExecutorFixtures.Day);
        CampaignPlanStore.TryGetExecutorContext(0, out var ctx);
        ctx.ExecutorsAllowed = false;
        CampaignPlanStore.PublishExecutorContext(0, ctx);
        AssertFalse(CampaignWithholdView.HoldsDepotBuilder(0, 4242), "the pre-war gate (executors not allowed) holds nothing");
        CampaignScope.ResetAll();
        AssertFalse(CampaignWithholdView.HoldsDepotBuilder(0, 4242), "no plan published: nothing");

        string view = Src("Strategic/Planner/CampaignWithholdView.cs");
        AssertTrue(HasLine(BodyOf(view, "internal static bool HoldsDepotBuilder("), "return Read(alliance, out _, out _, out _) && HeldBuilder(alliance, instanceId);"), "the fort job's hold goes through the one picture test (Read)");
        AssertEqual(2, CountOccurrences(view, "if (HeldBuilder(alliance, instanceId)) return true;"), "offense and defense hold through the same helper");
        AssertEqual(1, CountOccurrences(view, "DepotTaskLedgers.For(alliance).HoldsBuilder(instanceId)"), "one ledger test in the view, no copy");
        AssertTrue(HasLine(BodyOf(Src("Patches/FortConstructionGovernorPatch.cs"), "internal static void Prefix("), "bool heldBuilder = holdBuilders && CampaignWithholdView.HoldsDepotBuilder(alliance, unit.GetInstanceID());"), "the fort patch asks the view, not the ledger");
        AssertFalse(Src("Patches/FortConstructionGovernorPatch.cs").Contains("DepotTaskLedgers"), "and keeps no copy of the test");
    }

    // F3: every abandon carries its day.
    private static void DepotLedgerAbandonsOnlyWithADay()
    {
        string src = Src("Strategic/Planner/DepotTaskLedger.cs");
        AssertFalse(src.Contains("public void Abandon(int taskIndex, string reason)"), "no abandon without a day: the cooldown is a day difference");
        AssertFalse(src.Contains("_endedDay"), "the end day is the record's, not a parallel list");
        AssertFalse(src.Contains("public bool ShouldWish(int taskIndex, float builderX, float builderZ, float bugleRange, int daySerial)"), "ShouldWish reads no day");
    }

    // ---- The construction ledger's depot wiring ------------------------------------------------------------------------------

    private static ConstructionInput DepotInput(TheaterPlan plan, FiscalPosture posture, Func<float, float, bool> ownIip, int rating = 5, int bondFloor = 11)
    {
        var input = new ConstructionInput { AllianceId = 0, FiscalPosture = posture, CurrentRating = rating, BondFloorRating = bondFloor, SupplyPressure = 0.5f, TransportPressure = 0.4f };
        DepotCandidates.Add(input, plan, (x, z) => Theater.Unknown, ownIip);
        return input;
    }

    private static void DepotCandidateCreatedFromPlanDepotTask()
    {
        var s = DepotWorld(out var plan, out _);
        var input = DepotInput(plan, FiscalPosture.BalancedWar, (x, z) => true);
        AssertEqual(2, input.Candidates.Count, "one SupplyDepot candidate per plan depot task");
        for (int i = 0; i < 2; i++)
        {
            AssertEqual(ConstructionCandidateKind.SupplyDepot, input.Candidates[i].Kind, "the ledger's own depot kind");
            AssertEqual(DepotTaskLedger.CandidateName(i), input.Candidates[i].Name, "named by the plan task index");
            AssertTrue(input.Candidates[i].VanillaValid && input.Candidates[i].SupportsActiveArmyCorridor, "valid, on the army's corridor");
        }
        AssertTrue(input.Candidates[0].SupplyPressure > input.Candidates[1].SupplyPressure, "the priority scales the supply pressure: the nearer site ranks first");
        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
        AssertEqual(ConstructionCandidateKind.SupplyDepot, output.TopSupplyDepot.Kind, "the ledger's TopSupplyDepot is no longer dead");
        AssertEqual(DepotTaskLedger.CandidateName(0), output.TopSupplyDepot.Name, "it is the highest-priority task");
        AssertEqual(0, DepotInputCount(null, FiscalPosture.BalancedWar), "no plan, no candidate");
        s.Reset(0, 0, s.DaySerial, PlayerCommandClass.NotCareer, -1, true, null);
    }

    private static int DepotInputCount(TheaterPlan plan, FiscalPosture posture)
    {
        var input = new ConstructionInput { AllianceId = 0, FiscalPosture = posture };
        DepotCandidates.Add(input, plan, null, null);
        return input.Candidates.Count;
    }

    // The ledger lets a depot through every fiscal posture, emergency included (IsEmergencyAllowed), and has no depot-specific
    // fiscal suppression; a depot is suppressed only when it is not valid for vanilla (no own IIP to call, VanillaInvalid).
    // A task whose candidate is suppressed never starts.
    private static void DepotTaskStartFollowsTheLedgersVerdict()
    {
        foreach (var posture in new[] { FiscalPosture.Expansion, FiscalPosture.BalancedWar, FiscalPosture.CreditDefense, FiscalPosture.EmergencySolvency })
        {
            var s = DepotWorld(out var plan, out var a);
            var output = ConstructionIntentLedger.Compute(DepotInput(plan, posture, (x, z) => true), new ConstructionOptions());
            AssertEqual(0, output.Suppressions.Length, posture + ": a valid depot is not suppressed");
            var ledger = new DepotTaskLedger();
            ledger.Sync(plan, a, s, output);
            AssertEqual(1, ActiveDepotTasks(ledger), posture + ": the task starts");
        }
        var nearFloor = ConstructionIntentLedger.Compute(DepotInput(DepotWorldPlan(), FiscalPosture.BalancedWar, (x, z) => true, rating: 10, bondFloor: 11), new ConstructionOptions());
        AssertEqual(ConstructionPosture.EmergencyHold, nearFloor.Posture, "fixture: next to the bond floor");
        AssertEqual(0, nearFloor.Suppressions.Length, "...the existing emergency allowance still lets the depot through EmergencyHold");
        AssertEqual(DepotTaskLedger.CandidateName(0), nearFloor.TopSupplyDepot.Name, "...and ranks it");

        var s2 = DepotWorld(out var plan2, out var a2);
        var invalid = ConstructionIntentLedger.Compute(DepotInput(plan2, FiscalPosture.BalancedWar, (x, z) => false), new ConstructionOptions());
        AssertEqual(2, invalid.Suppressions.Length, "no own IIP to call: both candidates are VanillaInvalid");
        var blocked = new DepotTaskLedger();
        blocked.Sync(plan2, a2, s2, invalid);
        AssertEqual(0, blocked.Tasks.Count, "a suppressed task never starts");
        var onlyFirstBad = ConstructionIntentLedger.Compute(DepotInput(plan2, FiscalPosture.BalancedWar, (x, z) => x > 300f), new ConstructionOptions());
        var second = new DepotTaskLedger();
        second.Sync(plan2, a2, s2, onlyFirstBad);
        AssertEqual(1, second.Tasks.Count, "the other task starts");
        AssertNear(400f, second.Tasks[0].X, 1e-4f, "the unsuppressed one");
        var none = new DepotTaskLedger();
        none.Sync(plan2, a2, s2, null);
        AssertEqual(1, none.Tasks.Count, "no ledger output yet (the construction pass is weekly): nothing is suppressed");
    }

    private static TheaterPlan DepotWorldPlan()
    {
        DepotWorld(out var plan, out _);
        return plan;
    }

    private static void TopSupplyDepotIsTheNextTaskToStart()
    {
        var s = DepotWorld(out var plan, out var a);
        var output = ConstructionIntentLedger.Compute(DepotInput(plan, FiscalPosture.BalancedWar, (x, z) => true), new ConstructionOptions());
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, output);
        AssertNear(250f, ledger.Tasks[0].X, 1e-4f, "the ledger's top task starts first");
        // The ledger ranks the second task above the first: it is the one to start.
        var flipped = new ConstructionOutput { TopSupplyDepot = new ConstructionCandidate { Kind = ConstructionCandidateKind.SupplyDepot, Name = DepotTaskLedger.CandidateName(1), VanillaValid = true } };
        var second = new DepotTaskLedger();
        second.Sync(plan, a, s, flipped);
        AssertNear(400f, second.Tasks[0].X, 1e-4f, "TopSupplyDepot names the next task to start");
        // Its builder is not available: fall back to the first task that can start.
        var noBuilder = new Allocation { Alliance = 0 };
        noBuilder.Assignments.Add(a.Assignments[0]);
        var fallback = new DepotTaskLedger();
        fallback.Sync(plan, noBuilder, s, flipped);
        AssertNear(250f, fallback.Tasks[0].X, 1e-4f, "a top task with no builder yields to the first one that has one");
        var unknown = new ConstructionOutput { TopSupplyDepot = new ConstructionCandidate { Kind = ConstructionCandidateKind.SupplyDepot, Name = "depot-task-xyz", VanillaValid = true } };
        var odd = new DepotTaskLedger();
        odd.Sync(plan, a, s, unknown);
        AssertNear(250f, odd.Tasks[0].X, 1e-4f, "a name the ledger cannot read is ignored");
    }

    private static void RailRolledLinesPairWithStartsInOrder()
    {
        var eligible = new[] { true, false, true, true, false, true };
        var rolls = new[] { 0f, 0f, 1f, 0f, 0f, 0f };
        var rolled = RailLineSteering.RolledLines(eligible, rolls, 0.5f);
        AssertEqual(3, rolled.Length, "a roll that succeeds on an eligible line counts; a success on an ineligible line and a failed roll do not");
        AssertEqual(0, rolled[0], "line order");
        AssertEqual(3, rolled[1], "line order");
        AssertEqual(5, rolled[2], "line order");
        AssertEqual(rolled.Length, RailLineSteering.Choose(eligible, rolls, 0.5f, new float[6]).Length, "Choose starts exactly one line per rolled line");
        AssertEqual(0, RailLineSteering.RolledLines(new bool[0], new float[0], 0.5f).Length, "no lines, no rolls");
    }

    // 16069: Random.Range(0f, 1f) <= num * timediff.
    private static void RailRollEqualToTheChanceSucceedsAsInVanilla()
    {
        var one = new[] { true };
        var lines = new[] { new VanillaLine { OwnsAllIips = true, Permitted = true } };
        AssertEqual(1, VanillaStarts(lines, new[] { 0.3f }, 0.3f), "fixture: vanilla starts the line when the roll equals the chance");
        AssertEqual(1, RailLineSteering.Choose(one, new[] { 0.3f }, 0.3f, new float[1]).Length, "a roll equal to the chance succeeds (<=)");
        AssertEqual(0, VanillaStarts(lines, new[] { 0.3001f }, 0.3f), "fixture: and not above it");
        AssertEqual(0, RailLineSteering.Choose(one, new[] { 0.3001f }, 0.3f, new float[1]).Length, "a roll above the chance fails");
    }

    // ---- Railroad steering gate ----------------------------------------------------------------------------------------------

    private static void RailSteeringStandsDownForPlayerAndEurope()
    {
        AssertTrue(RailLineSteering.ShouldSteer(true, 0, 0, true, false, false, true), "an AI alliance under ai_vs_ai steers (the player's own side included)");
        AssertTrue(RailLineSteering.ShouldSteer(true, 1, 0, false, false, false, true), "the AI alliance in an ordinary game steers");
        AssertFalse(RailLineSteering.ShouldSteer(true, 0, 0, false, false, false, true), "the player's alliance outside ai_vs_ai: vanilla's own guard returns (16054)");
        AssertFalse(RailLineSteering.ShouldSteer(true, 2, 0, false, false, false, true), "alliance 2 (Europe): vanilla's guard returns");
        AssertFalse(RailLineSteering.ShouldSteer(true, 3, 0, true, false, false, true), "alliances of 2 and above never steer");
        AssertFalse(RailLineSteering.ShouldSteer(true, -1, 0, true, false, false, true), "a negative alliance never steers");
        AssertFalse(RailLineSteering.ShouldSteer(true, 0, 0, true, true, false, true), "the W&L commander-in-chief player's alliance under ai_vs_ai: the planner stands down");
        AssertFalse(RailLineSteering.ShouldSteer(false, 1, 0, false, false, false, true), "flag off (or executors off): vanilla's loop");
        AssertFalse(RailLineSteering.ShouldSteer(true, 1, 0, false, false, false, false), "no fresh scores (no plan, stale plan, pre-war): vanilla's loop");
    }

    private static void RailSteeringStandsDownWhileJobMachinePaused()
    {
        AssertFalse(RailLineSteering.ShouldSteer(true, 1, 0, false, false, true, true), "while vanilla's job machine is paused (11368) vanilla keeps rolling railroads (81741) and its own roll runs");
        AssertTrue(RailLineSteering.ShouldSteer(true, 1, 0, false, false, false, true), "...and steering resumes when the job machine runs");
    }

    // ---- Fort site preference and the construction steering board -------------------------------------------------------------

    private static void FortPreferenceOnlyWhenAxisSiteEligible()
    {
        var preferred = new[] { false, true, false };
        AssertEqual(1, FortSitePreference.Choose(preferred, new[] { true, true, true }, new float[3]), "an eligible axis site: it is the one left visible");
        AssertEqual(-1, FortSitePreference.Choose(preferred, new[] { true, false, true }, new float[3]), "the axis site is not one vanilla would take for this unit: the list stays");
    }

    private static void FortPreferenceKeepsVanillaSitesOtherwise()
    {
        AssertEqual(-1, FortSitePreference.Choose(new[] { false, false }, new[] { true, true }, new float[2]), "no preferred site: vanilla's list order decides");
        AssertEqual(-1, FortSitePreference.Choose(new bool[0], new bool[0], new float[0]), "no sites at all");
        AssertEqual(-1, FortSitePreference.Choose(new[] { true }, new[] { false }, new float[1]), "a preferred site that is not eligible is not chosen");
    }

    private static void TopFortPreferredWhenEligible()
    {
        var preferred = new[] { true, true, true };
        var rank = new[] { 0.5f, 0.5f + ConstructionSteeringBoard.TopBonus, 0.4f };
        AssertEqual(1, FortSitePreference.Choose(preferred, new[] { true, true, true }, rank), "the ledger's top fort is the preferred site when it is eligible");
        AssertEqual(0, FortSitePreference.Choose(preferred, new[] { true, false, true }, rank), "when the top is not eligible the next best preferred site");
        AssertEqual(0, FortSitePreference.Choose(preferred, new[] { true, true, true }, new[] { 0.5f, 0.5f, 0.5f }), "equal ranks: the lowest index, as vanilla's own list walk would take it");
    }

    private static void BoardPrefersFortSitesOnTheAxisOrInAThreatenedSector()
    {
        var s = RailWorld(out var plan, out var sectors);
        int east = sectors.SectorAt(550f, 0f);
        plan.Sectors.Add(new SectorPlan { SectorId = east, Posture = TheaterPosture.Hold, TargetObjectiveId = -1, TargetX = 550f, TargetZ = 0f, HoldNeed = 9000f });
        var fortX = new[] { 300f, 300f, 600f, 0f };
        var fortZ = new[] { 40f, 300f, 300f, 400f };
        var board = ConstructionSteeringBoard.Build(s, plan, sectors, null, fortX, fortZ, 60f);
        AssertTrue(board.FortPreferred[0], "40 from the axis, range 60: on the axis");
        AssertFalse(board.FortPreferred[1], "300 from the axis, in a quiet sector: neither");
        AssertTrue(sectors.SectorAt(600f, 300f) == east, "fixture: site 2 stands in the Hold sector");
        AssertTrue(board.FortPreferred[2], "in the Hold sector, 300 off the axis");
        AssertFalse(board.FortPreferred[3], "far from both");
        var noPlan = ConstructionSteeringBoard.Build(s, new TheaterPlan { Alliance = 0, DaySerial = s.DaySerial }, sectors, null, fortX, fortZ, 60f);
        for (int i = 0; i < 4; i++) AssertFalse(noPlan.FortPreferred[i], "no main effort and no threatened sector: nothing is preferred (" + i + ")");
        AssertEqual(0, ConstructionSteeringBoard.Build(s, plan, sectors, null, null, null, 60f).FortPreferred.Length, "no site list, no preference");
    }

    private static void FortRankIsTheLedgersCandidateScorePlusTheTopBonus()
    {
        var s = RailWorld(out var plan, out var sectors);
        var input = new ConstructionInput { AllianceId = 0 };
        for (int i = 0; i < 3; i++)
            input.Candidates.Add(new ConstructionCandidate { Kind = ConstructionCandidateKind.Fort, SiteIndex = i, Name = "fort-site-" + i, VanillaValid = true, CapitalThreat = i == 1 ? 0.8f : 0.1f });
        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
        AssertEqual(1, output.TopFort.SiteIndex, "fixture: the ledger's top fort site is 1");
        float score0 = 0f, score1 = 0f;
        foreach (var c in output.SiteCandidates) { if (c.SiteIndex == 0) score0 = c.Score; if (c.SiteIndex == 1) score1 = c.Score; }
        var board = ConstructionSteeringBoard.Build(s, plan, sectors, output, new[] { 300f, 300f, 300f }, new[] { 0f, 0f, 0f }, 60f);
        AssertNear(score0, board.FortRank[0], 1e-6f, "a site's rank is the ledger's score for it");
        AssertNear(score1 + ConstructionSteeringBoard.TopBonus, board.FortRank[1], 1e-6f, "the top fort carries the tie-break bonus");
        AssertTrue(board.FortRank[1] > board.FortRank[2], "and ranks above the others");
        var plain = ConstructionSteeringBoard.Build(s, plan, sectors, null, new[] { 300f }, new[] { 0f }, 60f);
        AssertEqual(0f, plain.FortRank[0], "no ledger output: no rank");
    }

    // A construction output whose Railroad candidates carry their line index: the candidate scores come from the ledger itself.
    private static ConstructionOutput RailOutput(int allianceId, int activeRailStarts, params int[] lines)
    {
        var input = new ConstructionInput { AllianceId = allianceId, ActiveRailroadStarts = activeRailStarts, SupplyPressure = 0.2f };
        for (int k = 0; k < lines.Length; k++)
            input.Candidates.Add(new ConstructionCandidate
            {
                Kind = ConstructionCandidateKind.Railroad, SiteIndex = lines[k], Name = "railroad-" + lines[k], VanillaValid = true,
                SupportsActiveArmyCorridor = true, SupplyPressure = 0.2f * (k + 1)
            });
        return ConstructionIntentLedger.Compute(input, new ConstructionOptions());
    }

    private static void RailScoreMultipliesTheLedgersCandidateScore()
    {
        var s = RailWorld(out var plan, out var sectors);
        s.Rails.Add(Line(0, 500f, 0f, 505, 150f, 0f, 950));
        s.Rails.Add(Line(1, 500f, 0f, 505, 150f, 0f, 951)); // the same geometry as line 0
        var output = RailOutput(0, 0, 0, 1);
        AssertTrue(output.SiteCandidates.Length == 2, "fixture: both candidates are kept");
        float c0 = 0f, c1 = 0f;
        foreach (var c in output.SiteCandidates) { if (c.SiteIndex == 0) c0 = c.Score; else c1 = c.Score; }
        AssertTrue(c1 > c0, "fixture: the ledger scores line 1 higher");
        var plain = ConstructionSteeringBoard.Build(s, plan, sectors, null, null, null, 0f);
        AssertNear(plain.RailScore[0], plain.RailScore[1], 1e-6f, "without a ledger the lines tie");
        var board = ConstructionSteeringBoard.Build(s, plan, sectors, output, null, null, 0f);
        float geometry = RailLineScorer.Score(s.Rails[0], s, plan, sectors);
        AssertNear(geometry * c0 + (output.TopRailroad.SiteIndex == 0 ? ConstructionSteeringBoard.TopBonus : 0f), board.RailScore[0], 1e-5f, "the geometry times the ledger's candidate score for that line");
        AssertTrue(board.RailScore[1] > board.RailScore[0], "the line the ledger scores higher scores higher");
    }

    private static void CsaRailCapExcludesLineFromSteering()
    {
        var s = RailWorld(out var plan, out var sectors);
        s.Rails.Add(Line(0, 500f, 0f, 505, 150f, 0f, 950));
        s.Rails.Add(Line(1, 500f, 0f, 505, 150f, 0f, 951));
        var csaCapped = RailOutput(1, 1, 0, 1);
        AssertEqual(2, csaCapped.Suppressions.Length, "fixture: alliance 1 with a line already under construction: every rail candidate is suppressed (CsaRailDoctrineCap)");
        AssertEqual(1, csaCapped.Suppressions[0].SiteIndex + csaCapped.Suppressions[1].SiteIndex, "the suppressions carry the line index");
        var board = ConstructionSteeringBoard.Build(s, plan, sectors, csaCapped, null, null, 0f);
        AssertEqual(ConstructionSteeringBoard.ExcludedRailScore, board.RailScore[0], "a suppressed line is excluded from the preference");
        AssertEqual(ConstructionSteeringBoard.ExcludedRailScore, board.RailScore[1], "...every one of them");
        // Excluded lines sort last but still start when they are all that vanilla could start: the count of starts is vanilla's.
        var started = RailLineSteering.Choose(new[] { true, true }, new[] { 1f, 0f }, 0.5f, board.RailScore);
        AssertEqual(1, started.Length, "the rolled line starts, as in vanilla");
        AssertEqual(1, started[0], "all excluded and tied: the rolled line itself");
        var mixed = RailOutput(1, 0, 0);
        mixed.Suppressions = new[] { new ConstructionSuppression { Kind = ConstructionCandidateKind.Railroad, SiteIndex = 1, Reason = ConstructionSuppressionReason.CsaRailDoctrineCap } };
        var half = ConstructionSteeringBoard.Build(s, plan, sectors, mixed, null, null, 0f);
        AssertTrue(half.RailScore[0] > half.RailScore[1], "an allowed line beats an excluded one");
        var steered = RailLineSteering.Choose(new[] { true, true }, new[] { 1f, 0f }, 0.5f, half.RailScore);
        AssertEqual(0, steered[0], "the success on the excluded line starts the allowed one instead");
    }

    private static void TopRailroadBreaksScoreTies()
    {
        var s = RailWorld(out var plan, out var sectors);
        for (int i = 0; i < 3; i++) s.Rails.Add(Line(i, 0f, 300f, 960 + i, 20f, 300f, 970 + i)); // three identical lines far from everything: geometry 0
        var output = new ConstructionOutput { TopRailroad = new ConstructionCandidate { Kind = ConstructionCandidateKind.Railroad, SiteIndex = 2, Score = 1f } };
        var board = ConstructionSteeringBoard.Build(s, plan, sectors, output, null, null, 0f);
        AssertEqual(ConstructionSteeringBoard.TopBonus, board.RailScore[2], "the ledger's top line gets the tie-break bonus");
        AssertEqual(0f, board.RailScore[0], "the others stay level");
        var started = RailLineSteering.Choose(new[] { true, true, true }, new[] { 0f, 1f, 1f }, 0.5f, board.RailScore);
        AssertEqual(2, started[0], "a success on line 0 starts the ledger's top line when everything else ties");
    }

    private static void LedgerKeepsEveryScoredRailAndFortCandidateWithItsIndex()
    {
        var input = new ConstructionInput { AllianceId = 0 };
        for (int i = 0; i < 3; i++)
        {
            input.Candidates.Add(new ConstructionCandidate { Kind = ConstructionCandidateKind.Railroad, SiteIndex = 10 + i, Name = "r" + i, VanillaValid = true, SupplyPressure = i * 0.1f });
            input.Candidates.Add(new ConstructionCandidate { Kind = ConstructionCandidateKind.Fort, SiteIndex = 20 + i, Name = "f" + i, VanillaValid = true, CapitalThreat = i * 0.1f });
        }
        input.Candidates.Add(new ConstructionCandidate { Kind = ConstructionCandidateKind.Railroad, SiteIndex = 99, Name = "bad", VanillaValid = false });
        input.Candidates.Add(new ConstructionCandidate { Kind = ConstructionCandidateKind.PrivateBuilding, BuildingTypeId = 4, Name = "mill", VanillaValid = true });
        var output = ConstructionIntentLedger.Compute(input, new ConstructionOptions());
        AssertEqual(6, output.SiteCandidates.Length, "three rails and three forts; not the invalid line, not the private building");
        AssertEqual(12, output.TopRailroad.SiteIndex, "the top rail keeps its index");
        AssertEqual(22, output.TopFort.SiteIndex, "the top fort keeps its index");
        AssertEqual(1, output.Suppressions.Length, "the invalid line is suppressed");
        AssertEqual(99, output.Suppressions[0].SiteIndex, "...with its index");
        AssertEqual(0, ConstructionIntentLedger.Compute(null, null).SiteCandidates.Length, "an empty input has none");
    }

    private static void BoardIsPerAllianceFreshAndPerCampaign()
    {
        CampaignScope.ResetAll();
        var s = RailWorld(out var plan, out var sectors);
        s.Rails.Add(Line(0, 500f, 0f, 505, 150f, 0f, 950));
        var board = ConstructionSteeringBoard.Build(s, plan, sectors, null, null, null, 0f);
        AssertEqual(s.DaySerial, board.DaySerial, "the board carries the review day");
        AssertFalse(ConstructionSteeringBoard.TryRead(0, out _), "nothing published yet");
        ConstructionSteeringBoard.Publish(0, board);
        PlanFreshness.NoteToday(s.DaySerial);
        AssertTrue(ConstructionSteeringBoard.TryRead(0, out var read) && ReferenceEquals(read, board), "the published board is read whole");
        AssertFalse(ConstructionSteeringBoard.TryRead(1, out _), "per alliance");
        AssertFalse(ConstructionSteeringBoard.TryRead(2, out _) || ConstructionSteeringBoard.TryRead(-1, out _), "alliances 0 and 1 only");
        var other = ConstructionSteeringBoard.Build(s, plan, sectors, null, null, null, 0f);
        ConstructionSteeringBoard.Publish(2, other);
        ConstructionSteeringBoard.Publish(-1, other);
        AssertFalse(ConstructionSteeringBoard.TryRead(2, out _), "publishing for alliance 2 stores nothing");
        AssertTrue(ConstructionSteeringBoard.TryRead(0, out var still) && ReferenceEquals(still, board), "...and does not replace alliance 0's board");
        AssertFalse(ConstructionSteeringBoard.TryRead(1, out _), "...nor alliance 1's");
        PlanFreshness.NoteToday(s.DaySerial + PlanFreshness.MaxAgeDays);
        AssertTrue(ConstructionSteeringBoard.TryRead(0, out _), "two days old: still read");
        PlanFreshness.NoteToday(s.DaySerial + PlanFreshness.MaxAgeDays + 1);
        AssertFalse(ConstructionSteeringBoard.TryRead(0, out _), "three days old: ignored, as a stale plan is");
        PlanFreshness.NoteToday(s.DaySerial);
        CampaignScope.ResetAll();
        AssertFalse(ConstructionSteeringBoard.TryRead(0, out _), "a new campaign starts with none");
    }

    // ---- Runtime pins ---------------------------------------------------------------------------------------------------------

    private static string Src(string path)
    {
        return ReadRepoFile("src/WhiskeyRealism/" + path);
    }

    private static void DepotSteeringRuntimeStatementsPinned()
    {
        string src = Src("Strategic/Planner/DepotSteeringRuntime.cs");
        string execute = BodyOf(src, "private static void Run(");
        AssertTrue(HasLine(execute, "PublishBoard(alliance, snapshot, plan);"), "the rail and fort board is built every review");
        AssertTrue(HasLine(execute, "ledger.AbandonActive(\"flag-off\", snapshot.DaySerial);"), "flag off releases every held builder");
        AssertTrue(HasLine(execute, "ledger.Sync(plan, allocation, snapshot, EvaluateDepots(alliance));"), "the ledger syncs with today's plan, allocation, snapshot and the construction ledger's verdict");
        AssertTrue(execute.IndexOf("ledger.AbandonActive(", StringComparison.Ordinal) < execute.IndexOf("ledger.Sync(", StringComparison.Ordinal), "the flag decides before any task starts");
        string watch = BodyOf(src, "private static void PlaceWishesAndWatchBuilds(");
        AssertTrue(HasLine(watch, "float builtRadius = GamePrefs.buildingsitemaxradius + CampaignMover.ArrivalRadius;"), "the finished depot is looked for within the wish's placement radius (97557-97585) of a builder that may stand an arrival radius off the site");
        AssertTrue(HasLine(watch, "ledger.WatchWish(t, snapshot, builtRadius);"), "built (and wish-lost) are read from the snapshot's depots, never from the placeholder");
        AssertTrue(HasLine(watch, "float reach = Math.Max(builder.BugleRange, CampaignMover.ArrivalRadius);"), "arrived: within the builder's bugle range, or the mover's arrival radius, of the site");
        AssertTrue(HasLine(watch, "float days = MarchTime.EstimateDays(speed, builder, task.X, task.Z, Distance(builder.X, builder.Z, task.X, task.Z), snapshot, null);"), "the builder's march time to the site, the planner's own estimate");
        AssertTrue(HasLine(watch, "switch (ledger.WishMoment(t, builder, reach, days, facts))"), "the ledger decides when (pure, tested)");
        AssertTrue(HasLine(watch, "case DepotWishMoment.Here: Wish(alliance, snapshot, ledger, t, task, builder.X, builder.Z, builder.InstanceId); break;"), "arrived: the wish at the builder's position (14770)");
        AssertTrue(HasLine(watch, "case DepotWishMoment.Ahead: Wish(alliance, snapshot, ledger, t, task, task.X, task.Z, builder.InstanceId); break;"), "arriving before the next review: the wish at the site, while the builder still marches");
        AssertTrue(HasLine(watch, "case DepotWishMoment.Adopt: ledger.RecordWished(t, snapshot.DaySerial, facts.AdoptX, facts.AdoptZ, \"vanilla-depot-adopted\"); break;"), "a depot vanilla's own job is already raising for the builder is the task's depot: no second wish");
        string wish = BodyOf(src, "private static void Wish(");
        AssertTrue(HasLine(wish, "IIP iip = ConstructionRuntime.NearestOwnIip(alliance, task.X, task.Z);"), "the IIP the wish calls");
        AssertTrue(HasLine(wish, "if ((UnityEngine.Object)iip == null) return;"), "no IIP: wait, record nothing");
        AssertTrue(HasLine(wish, "CBuilding building = CBuilding.AddConstructionWish(CBuilding.id_supplydepot, new Vector3(x, y, z), iip, alliance);"), "vanilla's own wish (97479) at the position the moment names");
        AssertTrue(HasLine(wish, "ledger.Abandon(taskIndex, \"wish-refused\", snapshot.DaySerial);"), "a null building (the IIP, the company-founding cap) abandons the task");
        AssertTrue(HasLine(wish, "ledger.RecordWished(taskIndex, snapshot.DaySerial, x, z);"), "a real building records the wish");
        AssertTrue(wish.IndexOf("AddConstructionWish(", StringComparison.Ordinal) < wish.IndexOf("RecordWished(", StringComparison.Ordinal), "wished only after the wish exists");
        AssertEqual(1, CountOccurrences(src, "= CBuilding.AddConstructionWish("), "one wish call in the executor");
        AssertTrue(src.Contains("TelemetryPerf.Scope(\"campaign.planner.construction\", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0)"), "the executor's scope");
        string emit = BodyOf(src, "private static void EmitTransitions(");
        AssertTrue(emit.Contains("\"[DepotTask] alliance=\" + alliance + \" site=\""), "the [DepotTask] row names the alliance and the site");
        AssertTrue(emit.Contains("\" state=\" + t.State + \" reason=\" + t.Reason"), "...the state and the reason");
        AssertTrue(HasLine(emit, "ledger.ClearTransitions();"), "drained once logged");
    }

    private static int CountOccurrences(string text, string needle)
    {
        int n = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    // The depot builder is moved by G5a's mover; this executor only wishes. It must give no unit an order of any kind.
    private static void DepotSteeringRuntimeIssuesNoUnitOrder()
    {
        string src = Src("Strategic/Planner/DepotSteeringRuntime.cs");
        foreach (var banned in new[] { "MoveUnitTo(", "SetWaypoint(", "ChangeCampaignOrders", "CheckCurrentOrderUpdate", "TryIssue(", "unitsinoffensiveoperations", "unitsindefensiveoperations", "theaterposition", "followedcampaignobjective" })
            AssertFalse(src.Contains(banned), "DepotSteeringRuntime does not touch " + banned + ": it places a construction wish and nothing else");
    }

    private static void ConstructionRuntimeFeedsTheLedgerPlanDepotsAndSiteIndexes()
    {
        string src = Src("Strategic/Construction/ConstructionRuntime.cs");
        string build = BodyOf(src, "public static ConstructionInput BuildInput(");
        AssertTrue(HasLine(build, "AddDepotTaskCandidates(input);"), "the weekly pass wires the ledger's dead SupplyDepot kind to the plan's depot tasks");
        string depotInput = BodyOf(src, "internal static ConstructionInput BuildDepotInput(");
        AssertTrue(HasLine(depotInput, "AddDepotTaskCandidates(input);"), "the executor's own evaluation does too");
        AssertFalse(depotInput.Contains("AddPrivateBuildingCandidates") || depotInput.Contains("AddRailroadCandidates") || depotInput.Contains("AddFortSiteObservationCandidates"), "and repeats no other scan");
        string depots = BodyOf(src, "private static void AddDepotTaskCandidates(");
        AssertTrue(HasLine(depots, "if (!CampaignPlanStore.TryGetPlan(input.AllianceId, out var plan)) return;"), "the published plan, through the store's accessor");
        AssertTrue(depots.Contains("(x, z) => NearestOwnIip(alliance, x, z) != null"), "valid for vanilla when an own IIP exists to call");
        string iip = BodyOf(src, "internal static IIP NearestOwnIip(");
        AssertTrue(HasLine(iip, "if ((UnityEngine.Object)iip == null || iip.allianceowner != alliance || iip.IIPType == IIP.id_invisible) continue;"), "an own, visible IIP");
        AssertTrue(HasLine(iip, "if ((UnityEngine.Object)iip.currentlyunderconstruction != null) continue;"), "not one with a building under construction (97481-97484)");
        AssertTrue(HasLine(BodyOf(src, "private static void AddRailroadCandidates("), "SiteIndex = i,"), "a rail candidate carries its line index");
        AssertTrue(HasLine(BodyOf(src, "private static void AddFortSiteObservationCandidates("), "SiteIndex = i,"), "a fort candidate carries its site index");
    }

    private static void DepotExecutorRidesTheSteeringExecutorBehindTheGates()
    {
        string steering = BodyOf(Src("Strategic/Planner/CampaignSteeringRuntime.cs"), "internal static void Execute(");
        AssertTrue(HasLine(steering, "DepotSteeringRuntime.Execute(alliance, snapshot);"), "the construction executor is called from the steering executor");
        string review = BodyOf(Src("Strategic/Planner/CampaignPlannerRuntime.cs"), "internal static void ReviewExecute(");
        int gate = review.IndexOf("PlannerChapterRules.ExecutorsAllowed(", StringComparison.Ordinal);
        int steer = review.IndexOf("CampaignSteeringRuntime.Execute(", StringComparison.Ordinal);
        AssertTrue(gate >= 0 && steer > gate, "the steering executor, and with it the construction executor, runs behind the pre-war gate");
        int todays = review.IndexOf("PlanFreshness.IsToday(todaysPlan.DaySerial)", StringComparison.Ordinal);
        AssertTrue(todays > gate && todays < steer, "...and on today's plan only");
        AssertFalse(review.Contains("DepotSteeringRuntime"), "ReviewExecute's own executor list is G5d's file and is not edited by G5c");
    }

    private static void RailroadPatchStatementsPinned()
    {
        string src = Src("Patches/RailroadLineSteeringPatch.cs");
        AssertTrue(HasLine(src, "[HarmonyPatch(typeof(AICampaign), \"UpdateRailroadConstruction\")]"), "vanilla's UpdateRailroadConstruction (16052)");
        AssertTrue(HasLine(src, "internal static bool Prefix(int alliance, float timediff)"), "the Prefix takes vanilla's two arguments");
        AssertTrue(HasLine(src, "using (TelemetryPerf.Scope(\"campaign.patch.railroad-steering\", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))"), "scope");
        AssertTrue(HasLine(src, "PatchFirstFire.Mark(\"campaign.patch.railroad-steering\");"), "first-fire id");
        string prepare = BodyOf(src, "private static bool TryPrepare(");
        AssertTrue(HasLine(prepare, "bool enabled = Plugin.EnableRailroadSteering != null && Plugin.EnableRailroadSteering.Value && CampaignPlannerRuntime.ExecutorsEnabled();"), "the flag and the executors flag");
        AssertTrue(HasLine(prepare, "bool cicPlayerAlliance = DLC_WL.dlc_scenarioactive && alliance == GameVars.playeralliance && DLC_WL.IsCommanderInChief();"), "the W&L commander-in-chief's alliance");
        AssertTrue(prepare.Contains("GameVars.playeralliance, GameVars.ai_vs_ai, cicPlayerAlliance,"), "vanilla's 16054 inputs reach the rule");
        AssertTrue(prepare.Contains("StrategicCoordinator.WlVanillaJobMachinePaused()"), "the live job-machine pause (11368)");
        AssertTrue(HasLine(prepare, "bool fresh = ConstructionSteeringBoard.TryRead(alliance, out var board);"), "the fresh board");
        AssertTrue(HasLine(prepare, "eligible[j] = RailLineSteering.Eligible(line.constructionprogress, line.StartConstruction(alliance, checkonly: true));"), "eligibility is vanilla's (progress, then StartConstruction checkonly 77818-77831)");
        AssertTrue(HasLine(prepare, "score[j] = j < board.RailScore.Length ? board.RailScore[j] : 0f;"), "the line's steering score is the board's (a line past the board scores 0, which ties and leaves vanilla's rolled line)");
        AssertTrue(prepare.Contains("GamePrefs.probrailroadconstructionperyear, GamePrefs.probrailroadconstructionperyearperindustr,"), "vanilla's chance (16058-16066)");
        AssertTrue(HasLine(prepare, "IndustryPoliciesHeld(alliance), timediff);"), "...with the alliance's industry policies and the elapsed time");
        AssertTrue(HasLine(BodyOf(src, "private static int IndustryPoliciesHeld("), "int first = alliance != 0 ? 107 : 7;"), "policies 7..10 / 107..110 (16059)");
        AssertTrue(BodyOf(src, "private static int IndustryPoliciesHeld(").Contains("Policy.GetStatusByPolicyID(first + i)"), "...asked of vanilla's Policy");
        AssertTrue(src.Contains("if (!TryPrepare(alliance, timediff, out eligible, out score, out perCall, out lines)) return true;"), "any reason not to steer: vanilla runs");
    }

    private static void RailroadPatchDrawsBeforeItStartsAndNeverSkipsVanillaBeforeTheDraws()
    {
        string src = Src("Patches/RailroadLineSteeringPatch.cs");
        string roll = BodyOf(src, "private static void Roll(");
        AssertTrue(HasLine(roll, "for (int j = 0; j < lines; j++) rolls[j] = UnityEngine.Random.Range(0f, 1f);"), "one UnityEngine.Random.Range(0f, 1f) per line in line order, as 16069 (vanilla's stream is consumed as before)");
        AssertFalse(src.Contains("System.Random") || src.Contains("new Random("), "never a private generator: it would desync vanilla's stream");
        AssertTrue(roll.IndexOf("Random.Range(", StringComparison.Ordinal) < roll.IndexOf("StartConstruction(alliance)", StringComparison.Ordinal), "every draw before the first start");
        AssertTrue(HasLine(roll, "BattleUnits.railroad[started[k]].StartConstruction(alliance);"), "a start is vanilla's own StartConstruction on a line the choice named");
        AssertTrue(HasLine(roll, "var started = RailLineSteering.Choose(eligible, rolls, perCall, score);"), "the decision is the pure one");
        string prefix = BodyOf(src, "internal static bool Prefix(");
        int prepareFailure = prefix.IndexOf("could not prepare (vanilla's roll runs)", StringComparison.Ordinal);
        int rollCall = prefix.IndexOf("Roll(alliance, eligible, score, perCall, lines);", StringComparison.Ordinal);
        AssertTrue(prepareFailure >= 0 && rollCall > prepareFailure, "a failure before the draws lets vanilla run; the roll comes after");
        AssertTrue(HasLine(prefix, "return false;"), "from the first draw on vanilla's loop is skipped, even when the roll fails");
        string prepare = BodyOf(src, "private static bool TryPrepare(");
        AssertFalse(prepare.Contains("Random"), "nothing is drawn while preparing");
        AssertFalse(HasLine(prepare, "line.StartConstruction(alliance);"), "nothing is started while preparing (checkonly only)");
    }

    private static void FortPatchStatementsPinned()
    {
        string src = Src("Patches/FortConstructionGovernorPatch.cs");
        string prefix = BodyOf(src, "internal static void Prefix(");
        AssertTrue(HasLine(prefix, "bool governor = Enabled();"), "the saturation governor keeps its own flag");
        AssertTrue(HasLine(prefix, "bool steering = SteeringEnabled();"), "fort steering has its own");
        AssertTrue(HasLine(prefix, "if (!governor && !steering && !holdBuilders) return;"), "each of the three runs without the others");
        AssertTrue(HasLine(prefix, "inRange[i] = Tools.GetXZDistance(unitPosition, site) <= range;"), "vanilla's own range test (16389)");
        AssertTrue(HasLine(prefix, "if (!inRange[i]) filtered.Add(original[i]);"), "out-of-range sites stay in vanilla's list");
        AssertTrue(HasLine(prefix, "int preferredSite = steering ? PreferredSite(alliance, faction, unit, original, inRange, allowed) : -1;"), "the preference is asked only with steering on");
        AssertTrue(HasLine(prefix, "else if (preferredSite >= 0 && i != preferredSite) suppressed++;"), "with a preferred site, every other in-range site is hidden for the call");
        string pref = BodyOf(src, "private static int PreferredSite(");
        AssertTrue(HasLine(pref, "if (!ConstructionSteeringBoard.TryRead(alliance, out var board)) return -1;"), "a fresh board only");
        AssertTrue(HasLine(pref, "if (!inRange[i] || !allowed[i] || i >= board.FortPreferred.Length || !board.FortPreferred[i]) continue;"), "preferred, in range, allowed by the governor");
        AssertTrue(HasLine(pref, "int chosen = FortSitePreference.Choose(preferred, eligible, board.FortRank);"), "the decision is the pure one");
        string take = BodyOf(src, "private static bool VanillaWouldTake(");
        AssertTrue(HasLine(take, "float factor = capitalDefender ? 0.6f : 1f;"), "16386");
        AssertTrue(HasLine(take, "if (capitalDefender && Tools.GetXZDistance(((Component)unit).transform.position, capitalPosition) > Tools.GetXZDistance(site, capitalPosition))"), "a capital defender takes only a site no nearer the capital than itself (16385: num <= xZDistance || !flag)");
        AssertTrue(HasLine(take, "return bunits.GetClosestBuilding(site, CBuilding.id_fort, -1, 0f, GamePrefs.maxdistancetootherfort * factor) == null"), "no fort within maxdistancetootherfort (16389)");
        AssertTrue(HasLine(take, "&& bfs.CheckIfPositionIsCloseToFrontline(site, alliance, GamePrefs.maxdistancefrontlinefortconstruction)"), "...close to the frontline (16389)");
        AssertTrue(HasLine(take, "&& AICampaign.FortConstructionOrder.IsUnitAbleToConstructFort(unit, site);"), "...the unit able (16389)");
        AssertTrue(src.Contains("[HarmonyFinalizer]"), "the restore still runs in a Finalizer");
        AssertEqual(1, CountOccurrences(src, "FortSitesField()?.SetValue(null, original)"), "one restore path");
    }

    private static void ConstructionSteeringFlagsAreBoundAndRead()
    {
        string plugin = Src("Plugin.cs");
        foreach (var name in new[] { "Enable Supply Depot Steering", "Enable Fort Steering", "Enable Railroad Steering" })
            AssertTrue(plugin.Contains("\"Campaign Planner\", \"" + name + "\", true,"), name + " is bound, default on, in the planner's section");
        AssertTrue(Src("Strategic/Planner/DepotSteeringRuntime.cs").Contains("FlagOn(Plugin.EnableSupplyDepotSteering)"), "the depot executor reads its flag");
        AssertTrue(Src("Patches/FortConstructionGovernorPatch.cs").Contains("Plugin.EnableFortSteering.Value"), "the fort patch reads its flag");
        AssertTrue(Src("Patches/RailroadLineSteeringPatch.cs").Contains("Plugin.EnableRailroadSteering.Value"), "the rail patch reads its flag");
    }

    private static void ConstructionSteeringFilesHoldNoUnityTypesOrEraReads()
    {
        foreach (var file in new[] { "DepotTaskLedger", "RailLineScorer", "RailLineSteering", "ConstructionSteeringBoard" })
        {
            string src = Src("Strategic/Planner/" + file + ".cs");
            AssertFalse(src.Contains("using UnityEngine") || src.Contains("UnityEngine."), file + ".cs is pure: no Unity type");
            AssertFalse(src.Contains("EraStage"), file + ".cs reads no era stage (plan decision 7)");
        }
    }

    // F4: a fault in the construction executor is its own; the mover and G5a's steering still run in the same review.
    private static void DepotExecutorFaultCannotIdleTheMover()
    {
        string src = Src("Strategic/Planner/DepotSteeringRuntime.cs");
        string execute = BodyOf(src, "internal static void Execute(");
        AssertTrue(HasLine(execute, "Run(alliance, snapshot);"), "the work is in Run");
        AssertTrue(execute.Contains("catch (Exception ex)"), "...inside its own try/catch");
        AssertTrue(execute.Contains("OnceLog.Warning(\"campaign-planner:construction-executor\""), "...with a bounded (static-key) warning");
        AssertTrue(execute.IndexOf("try", StringComparison.Ordinal) < execute.IndexOf("Run(alliance, snapshot);", StringComparison.Ordinal), "the call is inside the try");
        string steering = BodyOf(Src("Strategic/Planner/CampaignSteeringRuntime.cs"), "internal static void Execute(");
        AssertTrue(HasLine(steering, "DepotSteeringRuntime.Execute(alliance, snapshot);"), "and the caller stays a plain call");
    }

    // F6: vanilla's fort job (21) does not read the availability Postfixes; a held depot builder is not sent to build a fort.
    private static void HeldDepotBuilderIsNotSentToBuildAFort()
    {
        string src = Src("Patches/FortConstructionGovernorPatch.cs");
        string prefix = BodyOf(src, "internal static void Prefix(");
        AssertTrue(HasLine(prefix, "bool holdBuilders = DepotHoldEnabled();"), "the hold has its own gate (the depot flag and the planner executors)");
        AssertTrue(HasLine(prefix, "bool heldBuilder = holdBuilders && CampaignWithholdView.HoldsDepotBuilder(alliance, unit.GetInstanceID());"), "the visiting unit is asked of the withhold view (the ledger's hold under its freshness and executors test)");
        AssertTrue(HasLine(prefix, "allowed[i] = !heldBuilder;"), "a held builder is offered no in-range site");
        AssertTrue(HasLine(prefix, "allowed[i] = !heldBuilder && decision.Allow;"), "...whatever the governor says");
        AssertTrue(HasLine(prefix, "if (!inRange[i]) filtered.Add(original[i]);"), "out-of-range sites stay: vanilla rejects them itself");
        string gate = BodyOf(src, "private static bool DepotHoldEnabled(");
        AssertTrue(gate.Contains("Plugin.EnableSupplyDepotSteering.Value") && gate.Contains("CampaignPlannerRuntime.ExecutorsEnabled()"), "the hold is part of depot steering and of the executors");
    }
}
