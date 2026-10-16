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
        plan.Sectors[plan.Sectors.Count - 1].Posture = TheaterPosture.EconomyOfForce;
        AssertNear(RailLineScorer.Score(quiet, s, plan, sectors), RailLineScorer.Score(inThreat, s, plan, sectors), 1e-4f, "a sector that is neither threatened nor on the axis adds nothing");
    }

    private static void RailScoreRewardsSupplyReachTowardTheTarget()
    {
        var s = RailWorld(out var plan, out var sectors);
        var toward = Line(0, 500f, 0f, 505, 200f, 0f, 930);
        var away = Line(1, 500f, 0f, 505, 590f, 0f, 931);
        AssertTrue(RailLineScorer.Score(toward, s, plan, sectors) > RailLineScorer.Score(away, s, plan, sectors), "a line that carries supply from own source toward the main target scores above one that points away");
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
        AssertFalse(ledger.ShouldWish(0, 300f, 0f, 5f, s.DaySerial), "50 from the site, bugle range 5: still marching");
        AssertFalse(ledger.ShouldWish(0, 256f, 0f, 5f, s.DaySerial), "6 from the site: just outside the bugle range");
        AssertTrue(ledger.ShouldWish(0, 255f, 0f, 5f, s.DaySerial), "5 from the site: on arrival within the bugle range");
        AssertTrue(ledger.ShouldWish(0, 250f, 3f, 5f, s.DaySerial), "and from any side of it");
        AssertFalse(ledger.ShouldWish(1, 250f, 0f, 5f, s.DaySerial), "no such task");
        AssertFalse(ledger.ShouldWish(-1, 250f, 0f, 5f, s.DaySerial), "no such task (negative)");
    }

    private static void DepotWishOncePerTask()
    {
        var s = DepotWorld(out var plan, out var a);
        var ledger = new DepotTaskLedger();
        ledger.Sync(plan, a, s, null);
        AssertTrue(ledger.ShouldWish(0, 250f, 0f, 5f, s.DaySerial), "before the wish");
        ledger.RecordWished(0, s.DaySerial);
        AssertEqual(DepotTaskState.Wished, ledger.Tasks[0].State, "wished");
        AssertFalse(ledger.ShouldWish(0, 250f, 0f, 5f, s.DaySerial), "a wished task is never wished again");
        ledger.RecordWished(0, s.DaySerial + 1);
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
        ledger.RecordWished(0, s.DaySerial + 3);
        AssertTrue(ledger.HoldsBuilder(1), "and through the whole wished period, while the depot rises");
        ledger.RecordBuilt(0);
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
        ledger.RecordWished(0, wishDay);
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
        ledger.RecordWished(0, s.DaySerial);
        ledger.Sync(plan, a, s, null);
        AssertEqual(1, ActiveDepotTasks(ledger), "still one while it is wished");
        ledger.RecordBuilt(0);
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
        ledger.RecordBuilt(0);
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
        wished.RecordWished(0, s2.DaySerial);
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
            ledger.RecordBuilt(ledger.Tasks.Count - 1);
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
        ledger.RecordWished(0, s.DaySerial);
        ledger.RecordBuilt(0);
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
        again.Abandon(0, "builder-refused");
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
        ledger.RecordWished(0, s.DaySerial);
        AssertTrue(ledger.HoldsBuilder(1), "fixture: a held builder");
        ledger.AbandonActive("flag-off", s.DaySerial);
        AssertFalse(ledger.HoldsBuilder(1), "no builder stays held once the steering is off");
        AssertEqual(DepotTaskState.Abandoned, ledger.Tasks[0].State, "abandoned");
        AssertEqual("flag-off", ledger.Transitions[ledger.Transitions.Count - 1].Reason, "with the reason");
        ledger.AbandonActive("flag-off", s.DaySerial);
        AssertEqual(1, ledger.Tasks.Count, "idempotent");
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
        var fortX = new[] { 300f, 300f, 560f, 0f };
        var fortZ = new[] { 40f, 300f, 0f, 400f };
        var board = ConstructionSteeringBoard.Build(s, plan, sectors, null, fortX, fortZ, 60f);
        AssertTrue(board.FortPreferred[0], "40 from the axis, range 60: on the axis");
        AssertFalse(board.FortPreferred[1], "300 from the axis, in a quiet sector: neither");
        AssertTrue(board.FortPreferred[2], "in the Hold sector, off the axis");
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
        ConstructionSteeringBoard.Publish(2, board);
        AssertFalse(ConstructionSteeringBoard.TryRead(2, out _), "publishing for alliance 2 stores nothing");
        PlanFreshness.NoteToday(s.DaySerial + PlanFreshness.MaxAgeDays);
        AssertTrue(ConstructionSteeringBoard.TryRead(0, out _), "two days old: still read");
        PlanFreshness.NoteToday(s.DaySerial + PlanFreshness.MaxAgeDays + 1);
        AssertFalse(ConstructionSteeringBoard.TryRead(0, out _), "three days old: ignored, as a stale plan is");
        PlanFreshness.NoteToday(s.DaySerial);
        CampaignScope.ResetAll();
        AssertFalse(ConstructionSteeringBoard.TryRead(0, out _), "a new campaign starts with none");
    }

    private static string Src(string path)
    {
        return ReadRepoFile("src/WhiskeyRealism/" + path);
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
}
