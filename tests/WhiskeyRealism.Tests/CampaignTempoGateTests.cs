using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G5a, chapters decisions 5 and 8 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G5a, the
// chapter tempo gate and the pre-war gate): vanilla's tempo roll as pure arithmetic, the tempo clock, the first-move
// gate and its release predicate (TempoReleasedDecision == DecisionSerial), the offense-only withhold of the mass
// and of a deferred decision's Support, the map-group player's Support order, and the 001 pre-war idle.
internal static class CampaignTempoGateTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("TempoProbabilityMovedVerbatimFromTheOffensivePackageGate", TempoProbabilityMovedVerbatimFromTheOffensivePackageGate),
            ("ChapterAggressionIsVanillasArray", ChapterAggressionIsVanillasArray),
            ("ForcedInvasionIsVanillas14303", ForcedInvasionIsVanillas14303),
            ("DebugPermanentMovementReleases", DebugPermanentMovementReleases),
            ("RollMatchesVanillaPerPackageRule", RollMatchesVanillaPerPackageRule),
            ("IdleTermAddedOncePerRoll", IdleTermAddedOncePerRoll),
            ("ReleaseRateMatchesFormula", ReleaseRateMatchesFormula),
            ("SeededRollReproducible", SeededRollReproducible),
            ("FirstRollOfNewDecisionUsesOneReviewGap", FirstRollOfNewDecisionUsesOneReviewGap),
            ("GapCappedAfterPauseOrPreWar", GapCappedAfterPauseOrPreWar),
            ("NoStoredDayUsesOneReviewGap", NoStoredDayUsesOneReviewGap),
            ("LastFullReviewDayOnlyOnExecutorsAllowedFullReview", LastFullReviewDayOnlyOnExecutorsAllowedFullReview),
            ("NoRollMainEffortReleasesDecision", NoRollMainEffortReleasesDecision),
            ("GateOnlyFirstMoveOfADecision", GateOnlyFirstMoveOfADecision),
            ("OwnTerritoryMoveNeverGated", OwnTerritoryMoveNeverGated),
            ("NonMainRolesNeverGated", NonMainRolesNeverGated),
            ("DeferredMoveLeavesPlanPublished", DeferredMoveLeavesPlanPublished),
            ("SupportWaitsForMainEffortLaunchIntoEnemyGround", SupportWaitsForMainEffortLaunchIntoEnemyGround),
            ("SupportRepositionsFreelyInOwnGround", SupportRepositionsFreelyInOwnGround),
            ("PlayerSupportOrderFollowsAiSupportRule", PlayerSupportOrderFollowsAiSupportRule),
            ("EmptyDispatchableMassIsNotKept", EmptyDispatchableMassIsNotKept),
            ("DefendingMassMemberIsBusyAtNextReview", DefendingMassMemberIsBusyAtNextReview),
            ("DeferredMassWithheldFromVanillaOffense", DeferredMassWithheldFromVanillaOffense),
            ("DeferredMassAvailableForVanillaDefense", DeferredMassAvailableForVanillaDefense),
            ("DeferredSupportWithheldFromVanillaOffenseUntilRelease", DeferredSupportWithheldFromVanillaOffenseUntilRelease),
            ("WlCareerMultipliesTwiceWithPlayerInMass", WlCareerMultipliesTwiceWithPlayerInMass),
            ("ReleasedDecisionSurvivesRestore", ReleasedDecisionSurvivesRestore),
            ("TempoRowsAreBoundedToTwoPerDecision", TempoRowsAreBoundedToTwoPerDecision),
            ("PlannerReadsNoCalendarEra", PlannerReadsNoCalendarEra),
            ("TempoGateMovedOutOfThePackageLedger", TempoGateMovedOutOfThePackageLedger),
            ("PlannerIdlesPreWar001", PlannerIdlesPreWar001),
            ("PreWarGateOnlyScenario001", PreWarGateOnlyScenario001),
            ("PreWarWithholdsNothing", PreWarWithholdsNothing),
            ("PaceNotTooQuietPreWar", PaceNotTooQuietPreWar),
            ("ReviewExecutePreWarReturnPinned", ReviewExecutePreWarReturnPinned),
        });
    }

    private static readonly float[] Union001 = { 0.00f, 0.75f, 1.50f, 2.00f };   // Config/campaignprefs.txt:1417-1420
    private static readonly float[] Union002 = { 0.00f, 1.15f, 1.75f, 2.25f };   // Campaigns/002/A/campaignprefs.txt:1417-1420

    // ---- vanilla's roll as arithmetic ----

    // The five TempoProbability cases that lived in CampaignMilitaryFixTests (14336-14350).
    private static void TempoProbabilityMovedVerbatimFromTheOffensivePackageGate()
    {
        float tval = CampaignTempoGate.TempoProbability(0.5f, 0.02f, 1f, 1f, 1f, 0f, false, 1.5f, false);
        AssertTrue(Math.Abs(0.1f - tval) < 1e-5f, "chapter x timediff x 10, got " + tval);
        float winter = CampaignTempoGate.TempoProbability(0.5f, 0.02f, 1f, 1f, 0.2f, 0f, false, 1.5f, false);
        AssertTrue(Math.Abs(0.02f - winter) < 1e-5f, "winter modifier scales the roll");
        float idle = CampaignTempoGate.TempoProbability(0f, 0.02f, 1f, 1f, 1f, 0.5f, false, 1.5f, false);
        AssertTrue(Math.Abs(0.125f - idle) < 1e-5f, "idle time adds clamp01(idle^3)");
        float wl = CampaignTempoGate.TempoProbability(0.5f, 0.02f, 1f, 1f, 1f, 0f, true, 1.5f, false);
        AssertTrue(Math.Abs(0.15f - wl) < 1e-5f, "W&L scenario multiplies once (14344)");
        float wlPlayer = CampaignTempoGate.TempoProbability(0.5f, 0.02f, 1f, 1f, 1f, 0f, true, 1.5f, true);
        AssertTrue(Math.Abs(0.225f - wlPlayer) < 1e-5f, "player's group in the package multiplies again (14346)");
    }

    private static void ChapterAggressionIsVanillasArray()
    {
        for (int chapter = 0; chapter < 4; chapter++)
        {
            AssertEqual(Union001[chapter], CampaignTempoGate.ChapterAggression(Union001, chapter), "001 chapter " + chapter);
            AssertEqual(Union002[chapter], CampaignTempoGate.ChapterAggression(Union002, chapter), "002 chapter " + chapter);
        }
        AssertEqual(0f, CampaignTempoGate.ChapterAggression(Union001, -1), "chapter -1 (before vanilla's frame-50 update, 211208) is 0");
        AssertEqual(0f, CampaignTempoGate.ChapterAggression(Union001, 4), "past the array is 0");
        AssertEqual(0f, CampaignTempoGate.ChapterAggression(null, 1), "no prefs loaded");
    }

    private static void ForcedInvasionIsVanillas14303()
    {
        AssertTrue(CampaignTempoGate.ForcedInvasion("001", false, 0, 1861.6f), "001, Union, objective 6 open, past 1861.513");
        AssertFalse(CampaignTempoGate.ForcedInvasion("002", false, 0, 1861.6f), "scenario 001 only");
        AssertFalse(CampaignTempoGate.ForcedInvasion("001", false, 1, 1861.6f), "the Union (alliance 0) only");
        AssertFalse(CampaignTempoGate.ForcedInvasion("001", true, 0, 1861.6f), "objective 6 open");
        AssertFalse(CampaignTempoGate.ForcedInvasion("001", false, 0, 1861.513f), "strictly past 1861.513 (14303: > )");
        AssertTrue(CampaignTempoGate.ForcedInvasion("001", false, 0, 1861.514f), "just past it");
        AssertFalse(CampaignTempoGate.ForcedInvasion(null, false, 0, 1861.6f), "no scenario");
    }

    // 14350: vanilla returns when `num8 > num9 && !debug_permanentaimovement && !flag`.
    private static void DebugPermanentMovementReleases()
    {
        AssertFalse(CampaignTempoGate.Releases(0.9f, 0.1f, false), "a roll over tval waits");
        AssertTrue(CampaignTempoGate.Releases(0.9f, 0.1f, true), "...unless the forced flag or debug_permanentaimovement is set (the caller passes their OR)");
        AssertTrue(CampaignTempoGate.Releases(0.1f, 0.1f, false), "a roll equal to tval goes (the test is `roll > tval` to wait)");
        var inputs = TempoCase(chapter: 0f, timeDiffDays: 1, idle: 0f);
        inputs.Forced = true;
        inputs.BelievedEnemyAtTarget = 100f;
        inputs.TargetInAnotherAllianceState = true;
        var verdict = CampaignTempoGate.Evaluate(in inputs);
        AssertTrue(verdict.NeedsRoll && verdict.Released && verdict.Forced, "with the debug flag set any roll releases");
        inputs.Forced = false;
        AssertFalse(CampaignTempoGate.Evaluate(in inputs).Released, "...and without it, a zero probability does not");
        string coordinator = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignMoverRuntime.cs");
        AssertTrue(coordinator.Contains("GameVars.debug_permanentaimovement"), "the runtime reads vanilla's debug flag");
    }

    // 14316-14350 step by step over the same package list.
    private static float VanillaNum9(float[] aggression, int chapter, float timediff, float factionAggression, float campaignAggression, float winter,
                                     IList<float> timeOfNotMoving, float date, bool wl, float wlMultiplier, bool playerInList)
    {
        float num7 = 0f;
        for (int j = 0; j < timeOfNotMoving.Count; j++) num7 += timeOfNotMoving[j];
        num7 /= Math.Max(1f, (float)timeOfNotMoving.Count);
        num7 = date - num7;
        float num9 = aggression[chapter] * timediff * 10f * factionAggression * campaignAggression * winter;
        num9 += Math.Max(0f, Math.Min(1f, num7 * num7 * num7));
        if (wl)
        {
            num9 *= wlMultiplier;
            if (playerInList) num9 *= wlMultiplier;
        }
        return num9;
    }

    private static void RollMatchesVanillaPerPackageRule()
    {
        var rolls = new[] { 0f, 0.001f, 0.02f, 0.05f, 0.1f, 0.25f, 0.5f, 0.75f, 0.9999f };
        int cases = 0;
        foreach (var package in new[] { new[] { 1861.6f }, new[] { 1861.1f, 1861.55f }, new[] { 1860.9f, 1861.2f, 1861.5f }, new[] { 1861.0f, 1861.0f, 1861.6f, 1861.59f } })
        foreach (int chapter in new[] { 1, 2, 3 })
        foreach (float faction in new[] { 1.0f, 0.5f })
        foreach (float winter in new[] { 1f, 0.2f })
        foreach (bool wl in new[] { false, true })
        foreach (bool player in new[] { false, true })
        {
            if (player && !wl) continue;
            const float date = 1861.6f, timediff = 1f / 365.25f, campaign = 1.2f, wlMult = 1.3f;
            float expected = VanillaNum9(Union001, chapter, timediff, faction, campaign, winter, package, date, wl, wlMult, player);
            float idleYears = CampaignTempoGate.IdleYears(date, package);
            float actual = CampaignTempoGate.TempoProbability(CampaignTempoGate.ChapterAggression(Union001, chapter), timediff, faction, campaign, winter, idleYears, wl, wlMult, player);
            AssertNear(expected, actual, 1e-5f, "probability, package of " + package.Length + " chapter " + chapter + " faction " + faction + " winter " + winter + " wl " + wl + " player " + player);
            foreach (float roll in rolls.Concat(new[] { expected }))
            {
                // 14350: `if (num8 > num9 && !debug_permanentaimovement && !flag) return;`
                AssertEqual(!(roll > expected), CampaignTempoGate.Releases(roll, actual, false), "release at roll " + roll);
                AssertEqual(true, CampaignTempoGate.Releases(roll, actual, true), "debug or forced flag at roll " + roll);
            }
            cases++;
        }
        AssertTrue(cases >= 100, "a real grid: " + cases);
        AssertEqual(0f, CampaignTempoGate.IdleYears(1861.6f, new float[0]) - 1861.6f, "an empty list reads as vanilla's mean 0 (14323-14326): the idle term is the whole date, clamped by the caller");
    }

    private static void IdleTermAddedOncePerRoll()
    {
        const float chapter = 0.75f;
        float noGap = CampaignTempoGate.TempoProbability(chapter, 0f, 1f, 1f, 1f, 0.5f, false, 1f, false);
        AssertNear(0.125f, noGap, 1e-6f, "with timediff 0 the probability is clamp01(idle^3)");
        float wl = CampaignTempoGate.TempoProbability(chapter, 0f, 1f, 1f, 1f, 0.5f, true, 1.5f, false);
        AssertNear(0.125f * 1.5f, wl, 1e-6f, "...times the W&L factor");
        float one = CampaignTempoGate.TempoProbability(chapter, 0.01f, 1f, 1f, 1f, 0.5f, false, 1f, false);
        float two = CampaignTempoGate.TempoProbability(chapter, 0.02f, 1f, 1f, 1f, 0.5f, false, 1f, false);
        AssertNear(chapter * 0.01f * 10f, one - noGap, 1e-6f, "the chapter term alone scales with timediff");
        AssertNear(2f * (one - noGap), two - noGap, 1e-6f, "doubling timediff changes only the chapter term");
        AssertNear(1f, CampaignTempoGate.TempoProbability(0f, 0f, 1f, 1f, 1f, 5f, false, 1f, false), 1e-6f, "the idle term is clamped to 1");
        // One review's roll: Evaluate adds the idle term once.
        var inputs = TempoCase(chapter: 0f, timeDiffDays: 1, idle: 0.5f);
        inputs.TargetInAnotherAllianceState = true;
        inputs.BelievedEnemyAtTarget = 1000f;
        AssertNear(0.125f, CampaignTempoGate.Evaluate(in inputs).Probability, 1e-6f, "once per roll, however many formations the mass has");
    }

    private static void ReleaseRateMatchesFormula()
    {
        const int n = 200000;
        int day0 = ExecutorFixtures.Day;
        foreach (var c in new[] { (agg: 0.75f, faction: 1.0f, label: "001 Union ch1"), (agg: 1.50f, faction: 1.0f, label: "001 Union ch2"),
                                  (agg: 2.00f, faction: 1.0f, label: "001 Union ch3"), (agg: 0.75f, faction: 0.5f, label: "001 CSA ch1") })
        {
            var inputs = TempoCase(c.agg, 1, 0f);
            inputs.FactionAggression = c.faction;
            inputs.CampaignSeed = 1861L;
            inputs.TargetInAnotherAllianceState = true;
            inputs.BelievedEnemyAtTarget = 1000f;
            int released = 0;
            float p = 0f;
            for (int i = 0; i < n; i++)
            {
                inputs.TodayDay = day0 + i;
                inputs.LastFullReviewDay = inputs.TodayDay - 1;
                inputs.DecisionSerial = 1 + (i % 50);
                var verdict = CampaignTempoGate.Evaluate(in inputs);
                p = verdict.Probability;
                if (verdict.Released) released++;
            }
            double rate = (double)released / n;
            double band = 4.0 * Math.Sqrt(p * (1.0 - p) / n);
            AssertNear(c.agg * c.faction * 10f / 365.25f, p, 1e-6f, c.label + ": the formula");
            AssertTrue(Math.Abs(rate - p) <= band, c.label + ": release frequency " + rate.ToString("0.00000") + " vs p " + p.ToString("0.00000") + " within " + band.ToString("0.00000"));
        }
    }

    private static void SeededRollReproducible()
    {
        float a = CampaignTempoGate.Roll01(1861L, 0, 3, 5000);
        AssertEqual(a, CampaignTempoGate.Roll01(1861L, 0, 3, 5000), "same inputs, same roll (a reload re-rolls the same dice)");
        AssertTrue(a >= 0f && a < 1f, "in [0, 1)");
        AssertTrue(a != CampaignTempoGate.Roll01(1862L, 0, 3, 5000), "another campaign seed");
        AssertTrue(a != CampaignTempoGate.Roll01(1861L, 1, 3, 5000), "the other alliance");
        AssertTrue(a != CampaignTempoGate.Roll01(1861L, 0, 4, 5000), "another decision");
        AssertTrue(a != CampaignTempoGate.Roll01(1861L, 0, 3, 5001), "another day");
        float lo = 1f, hi = 0f, sum = 0f;
        for (int i = 0; i < 5000; i++)
        {
            float r = CampaignTempoGate.Roll01(1861L, 0, 1, 1000 + i);
            lo = Math.Min(lo, r);
            hi = Math.Max(hi, r);
            sum += r;
        }
        AssertTrue(lo >= 0f && hi < 1f && lo < 0.01f && hi > 0.99f, "spans the unit interval");
        AssertNear(0.5f, sum / 5000f, 0.02f, "centred");
    }

    // ---- the tempo clock ----

    private static void FirstRollOfNewDecisionUsesOneReviewGap()
    {
        // The previous Full review was yesterday; the last release was 60 days ago. The gap is yesterday's, not 60 days.
        int today = ExecutorFixtures.Day;
        AssertNear(1f / 365.25f, CampaignTempoGate.TimeDiffYears(today, today - 1), 1e-9f, "since the previous Full review: one day");
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 20000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        var state = ExecutorFixtures.State();
        state.TempoReleasedDecision = 1;       // the previous decision was released long ago
        state.DecisionSerial = 2;              // a new decision
        state.LastFullReviewDay = today - 1;
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.ClosedTempo()), new FakeMoverHost(), new MoveTracker(), state);
        AssertNear(1f, tally.Tempo.GapDays, 1e-6f, "the roll of a new decision uses one review's worth, not the days since the last release");
        AssertTrue(tally.Tempo.NeedsRoll, "a believed enemy at an enemy-held target needs the roll");
    }

    private static void GapCappedAfterPauseOrPreWar()
    {
        int today = ExecutorFixtures.Day;
        float one = 1f / 365.25f;
        AssertNear(one, CampaignTempoGate.TimeDiffYears(today, -1), 1e-9f, "no executors-allowed review before (001 pre-war idle, or none recorded): one review's worth");
        AssertNear(one, CampaignTempoGate.TimeDiffYears(today, today - 30), 1e-9f, "after a 30-day W&L pause: capped, vanilla would carry the whole gap (11327)");
        AssertNear(one, CampaignTempoGate.TimeDiffYears(today, today - 400), 1e-9f, "after a long stand-down");
        AssertNear(one, CampaignTempoGate.TimeDiffYears(today, today - 1), 1e-9f, "the normal daily gap");
        AssertNear(0f, CampaignTempoGate.TimeDiffYears(today, today), 1e-9f, "a second review of the same day adds no time");
        AssertNear(one, CampaignTempoGate.TimeDiffYears(today, today + 5), 1e-9f, "a clock that went back (a load) restarts at one review's worth");
        AssertEqual(1, CampaignTempoGate.ReviewGapDays, "ReviewGapDays");
    }

    private static void NoStoredDayUsesOneReviewGap()
    {
        var state = new PlannerState();
        state.Restore(2, 7, -1, 0, 3, 1861L);   // a v1/v2 sidecar: no stored day, no stored release
        AssertEqual(-1, state.LastFullReviewDay, "no stored day");
        AssertEqual(-1, state.TempoReleasedDecision, "no stored release: -1 never equals a DecisionSerial");
        AssertNear(1f / 365.25f, CampaignTempoGate.TimeDiffYears(ExecutorFixtures.Day, state.LastFullReviewDay), 1e-9f, "a reload from it rolls with one review's worth");
        AssertEqual(-1, new PlannerState().TempoReleasedDecision, "a fresh state: DecisionSerial starts at 0 and is 1 at the first decision");
    }

    private static void LastFullReviewDayOnlyOnExecutorsAllowedFullReview()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 9000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        var state = ExecutorFixtures.State();
        AssertEqual(-1, state.LastFullReviewDay, "none yet");
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.OpenTempo()), new FakeMoverHost(), new MoveTracker(), state);
        AssertEqual(s.DaySerial, state.LastFullReviewDay, "a review that ran the executors records its day, whether or not a roll happened");

        // The only writes: PlannerState (declaration and Restore) and the mover pass; the runtime reaches the pass only after the
        // pause, stand-down and pre-war returns (ReviewExecutePreWarReturnPinned).
        int writes = 0;
        foreach (string file in new[] { "CampaignMover.cs", "CampaignMoverRuntime.cs", "CampaignSteeringRuntime.cs", "CampaignPlannerRuntime.cs", "PlannerState.cs", "TheaterPlanner.cs" })
            foreach (Match m in Regex.Matches(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/" + file), @"^\s*(?:public int |\w+\.)?LastFullReviewDay\s*=[^=]", RegexOptions.Multiline))
                writes++;
        AssertEqual(3, writes, "PlannerState: the initializer and Restore; CampaignMover: the one assignment");
    }

    // ---- the first-move gate ----

    // A fixture where the main objective (x 100) lies in alliance 1's ground and a believed army stands there, so the roll applies.
    private sealed class Gate
    {
        public CampaignSnapshot S;
        public TheaterPlan Plan;
        public Allocation Alloc;
        public PlannerState State = ExecutorFixtures.State();
        public MoveTracker Tracker = new MoveTracker();
        public FakeMoverHost Host = new FakeMoverHost();
    }

    private static Gate GateWith(bool believedEnemy, bool supportInEnemyGround = true, bool playerSupport = false, int dayOffset = 0)
    {
        var g = new Gate();
        var s = g.S = ExecutorFixtures.Wide(dayOffset);
        // 6000 + 5000 against a believed 5000: the required force is 5500, so no part of the mass is Reserve (kept at 1.3 x 5500).
        s.Own.Add(ExecutorFixtures.Corps(1, 350f, 6000f));           // MainEffort
        s.Own.Add(ExecutorFixtures.Corps(2, 360f, 5000f));           // MainEffort
        s.Own.Add(ExecutorFixtures.Corps(3, 330f, 9000f));           // Support
        if (playerSupport)
        {
            var player = ExecutorFixtures.Corps(4, 335f, 6000f);
            player.Chain = WlChainClass.PlayerMapGroup;
            s.Own.Add(player);
        }
        if (believedEnemy) s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
        var plan = g.Plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2 });
        var sectors = ExecutorFixtures.Sectors(s);
        int support = sectors.SectorAt(330f, 0f);
        float supportX = supportInEnemyGround ? 100f : 300f;
        plan.Sectors.Add(new SectorPlan { SectorId = support, Posture = TheaterPosture.Support, TargetObjectiveId = 98, TargetX = supportX, TargetZ = 0f });
        g.Alloc = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        return g;
    }

    private static MoverTally Run(Gate g, TempoWorld tempo)
    {
        return CampaignMover.Review(ExecutorFixtures.Inputs(g.S, g.Plan, g.Alloc, tempo), g.Host, g.Tracker, g.State);
    }

    private static void NoRollMainEffortReleasesDecision()
    {
        var g = GateWith(believedEnemy: false);
        var tally = Run(g, ExecutorFixtures.ClosedTempo());
        AssertFalse(tally.Tempo.NeedsRoll, "no believed strength at the objective: vanilla's roll condition (14336, num2 > 0) fails");
        AssertEqual(g.State.DecisionSerial, g.State.TempoReleasedDecision, "released before the first MainEffort dispatch");
        AssertEqual(3, g.Host.Moves.Count, "the main effort and its Support go out on that review");
        AssertTrue(g.Host.Moves.Exists(m => m.id == 3), "Support into enemy ground is not held: the decision is already released");

        // A later review on which the advancing mass has revealed an enemy does not roll the rest of the mass.
        var later = GateWith(believedEnemy: true, dayOffset: 1);
        later.State = g.State;
        var busy = later.S.Own[1];
        busy.Flags |= FormationFlags.InBattle;               // corps 2 was busy yesterday
        later.S.Own[1] = busy;
        Run(later, ExecutorFixtures.ClosedTempo());
        AssertTrue(later.Host.Moves.Exists(m => m.id == 1), "the dispatchable mass member goes out without a roll");
        later = GateWith(believedEnemy: true, dayOffset: 2);
        later.State = g.State;
        var tallyLater = Run(later, ExecutorFixtures.ClosedTempo());
        AssertTrue(later.Host.Moves.Exists(m => m.id == 2), "a member that was busy and is free now is dispatched without a roll: the decision stays released");
        AssertFalse(tallyLater.Tempo.NeedsRoll, "no roll was evaluated: the decision was already released");
    }

    private static void GateOnlyFirstMoveOfADecision()
    {
        var closed = GateWith(believedEnemy: true);
        var first = Run(closed, ExecutorFixtures.ClosedTempo());
        AssertTrue(first.Tempo.NeedsRoll && !first.Tempo.Released, "the roll applies and fails");
        AssertEqual(-1, closed.State.TempoReleasedDecision, "not released");
        AssertEqual(0, closed.Host.Moves.Count, "no MainEffort formation moves, and no Support into enemy ground");
        AssertTrue(first.Deferred >= 3, "every held move is counted as deferred: " + first.Deferred);

        var open = GateWith(believedEnemy: true, dayOffset: 1);
        open.State = closed.State;
        open.Tracker = closed.Tracker;
        var release = Run(open, ExecutorFixtures.OpenTempo());
        AssertTrue(release.Tempo.Released, "a later roll passes");
        AssertEqual(open.State.DecisionSerial, open.State.TempoReleasedDecision, "the release is recorded");
        AssertEqual(3, open.Host.Moves.Count, "the whole mass and the Support go out");

        var again = GateWith(believedEnemy: true, dayOffset: 2);
        again.State = open.State;
        var tail = Run(again, ExecutorFixtures.ClosedTempo());
        AssertFalse(tail.Tempo.NeedsRoll, "no second roll for the same decision");
        AssertEqual(0, tail.Deferred, "nothing is held any more");
    }

    private static void OwnTerritoryMoveNeverGated()
    {
        var g = GateWith(believedEnemy: true);
        g.Host.State = (x, z) => 0;                           // the whole map is the Union's ground: nothing to attack
        var tally = Run(g, ExecutorFixtures.ClosedTempo());
        AssertFalse(tally.Tempo.NeedsRoll, "the target is not in a state of another alliance (14335)");
        AssertEqual(g.State.DecisionSerial, g.State.TempoReleasedDecision, "released without a roll");
        AssertEqual(3, g.Host.Moves.Count, "and every move goes out");
        AssertEqual(0, tally.Deferred, "nothing deferred");
    }

    private static void NonMainRolesNeverGated()
    {
        var s = ExecutorFixtures.Wide();
        for (int id = 1; id <= 4; id++) s.Own.Add(ExecutorFixtures.Corps(id, 340f + id * 5f, 9000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(ExecutorFixtures.Assign(1, FormationRole.MainEffort, plan.MainSectorId, 100f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(2, FormationRole.CapitalGuard, 0, 0f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(3, FormationRole.Recover, 0, 90f, 0f));
        a.Assignments.Add(ExecutorFixtures.Assign(4, FormationRole.DepotBuilder, 0, 80f, 0f));
        var state = ExecutorFixtures.State();
        var host = new FakeMoverHost();
        var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, a, ExecutorFixtures.ClosedTempo()), host, new MoveTracker(), state);
        AssertFalse(tally.Tempo.Released, "the main effort is deferred");
        AssertEqual("2,3,4", string.Join(",", host.Moves.ConvertAll(m => m.id)), "CapitalGuard, Recover and DepotBuilder move whatever the ground and the tempo");
        AssertEqual(1, tally.Deferred, "only the main effort waits");
    }

    private static void DeferredMoveLeavesPlanPublished()
    {
        var g = GateWith(believedEnemy: true);
        string before = AllocationSignature(g.Alloc) + "|" + g.Plan.MainObjectiveId + "|" + g.Plan.MassInstanceIds.Count + "|" + g.Plan.Sectors.Count;
        Run(g, ExecutorFixtures.ClosedTempo());
        string after = AllocationSignature(g.Alloc) + "|" + g.Plan.MainObjectiveId + "|" + g.Plan.MassInstanceIds.Count + "|" + g.Plan.Sectors.Count;
        AssertEqual(before, after, "a deferred decision changes neither the plan nor the allocation: it stays published and is retried next review");
        AssertEqual(1, g.State.DecisionSerial, "and the decision serial");
    }

    private static void SupportWaitsForMainEffortLaunchIntoEnemyGround()
    {
        var held = GateWith(believedEnemy: true);
        var tally = Run(held, ExecutorFixtures.ClosedTempo());
        AssertFalse(held.Host.Moves.Exists(m => m.id == 3), "Support into enemy-held ground waits for the launch");
        AssertTrue(tally.Deferred >= 3, "main effort and Support deferred");
        var next = GateWith(believedEnemy: true, dayOffset: 1);
        next.State = held.State;
        Run(next, ExecutorFixtures.OpenTempo());
        AssertTrue(next.Host.Moves.Exists(m => m.id == 3), "it goes out on the release review");

        var noRoll = GateWith(believedEnemy: false);
        Run(noRoll, ExecutorFixtures.ClosedTempo());
        AssertTrue(noRoll.Host.Moves.Exists(m => m.id == 3), "a decision whose main effort needs no roll is released before its first MainEffort dispatch: its Support goes out on that review");

        var neutral = GateWith(believedEnemy: true);
        neutral.Host.State = (x, z) => x <= 150f ? 2 : 0;       // alliance 2 holds the target's state
        neutral.Host.Own = 0;
        Run(neutral, ExecutorFixtures.ClosedTempo());
        AssertTrue(neutral.Host.Moves.Exists(m => m.id == 3), "...and ground whose owner is not at war with us is not enemy-held (63127): Support is not held");
    }

    private static void SupportRepositionsFreelyInOwnGround()
    {
        var g = GateWith(believedEnemy: true, supportInEnemyGround: false);
        var tally = Run(g, ExecutorFixtures.ClosedTempo());
        AssertFalse(g.State.TempoReleasedDecision == g.State.DecisionSerial, "the main effort is still deferred");
        AssertEqual("3", string.Join(",", g.Host.Moves.ConvertAll(m => m.id)), "Support in own ground goes out while the decision is deferred, the main effort does not");
        AssertEqual(2, tally.Deferred, "the two MainEffort formations wait");
    }

    private static void PlayerSupportOrderFollowsAiSupportRule()
    {
        // In enemy ground: both wait; in own ground: both go out; on release: both go out.
        var held = GateWith(believedEnemy: true, playerSupport: true);
        AssertEqual(FormationRole.Support, held.Alloc.TryGet(4, out var p) ? p.Role : FormationRole.Hold, "the real allocator gave the map-group player a Support assignment");
        Run(held, ExecutorFixtures.ClosedTempo());
        AssertFalse(held.Host.Moves.Exists(m => m.id == 3), "the AI Support formation waits");
        AssertEqual(0, held.Host.Orders.Count, "...and so does the map-group player's order");

        var own = GateWith(believedEnemy: true, supportInEnemyGround: false, playerSupport: true);
        Run(own, ExecutorFixtures.ClosedTempo());
        AssertTrue(own.Host.Moves.Exists(m => m.id == 3), "the AI Support formation moves in own ground");
        AssertEqual(1, own.Host.Orders.Count, "...and the player's Support order goes out on exactly the same review");

        var released = GateWith(believedEnemy: true, playerSupport: true, dayOffset: 1);
        released.State = held.State;
        Run(released, ExecutorFixtures.OpenTempo());
        AssertTrue(released.Host.Moves.Exists(m => m.id == 3), "the AI Support moves on the release review");
        AssertEqual(1, released.Host.Orders.Count, "and so does the player's order");
        AssertFalse(released.Host.Moves.Exists(m => m.id == 4), "never as a move");
    }

    // ---- the plan side: the mass, busy formations ----

    private static void EmptyDispatchableMassIsNotKept()
    {
        var state = new PlannerState { CampaignSeed = 1861L };
        var day1 = ExecutorFixtures.Wide();
        day1.Own.Add(ExecutorFixtures.Corps(1, 200f, 20000f));
        day1.Own.Add(ExecutorFixtures.Corps(2, 350f, 15000f));
        var plan1 = ExecutorFixtures.RealPlan(day1, state);
        AssertEqual(98, plan1.MainObjectiveId, "day 1: a main effort");
        AssertEqual(2, plan1.MassInstanceIds.Count, "over a mass of two");
        int decision = state.DecisionSerial;

        var day2 = ExecutorFixtures.Wide(1);
        foreach (var f in new[] { ExecutorFixtures.Corps(1, 200f, 20000f), ExecutorFixtures.Corps(2, 350f, 15000f) })
        {
            var busy = f;
            busy.Flags |= FormationFlags.InDefensiveOps;     // vanilla's defence took both
            day2.Own.Add(busy);
        }
        var plan2 = ExecutorFixtures.RealPlan(day2, state);
        AssertEqual(-1, plan2.MainObjectiveId, "no dispatchable mass: the main effort is not viable and is not kept");
        AssertEqual(-1, state.CurrentMainObjective, "the state drops it");
        AssertEqual(0, plan2.MassInstanceIds.Count, "no mass");
        foreach (var sp in plan2.Sectors)
            AssertTrue(sp.Posture != TheaterPosture.MainEffort && sp.Posture != TheaterPosture.Support && sp.TargetObjectiveId < 0, "no sector follows a main target: nothing is held back for it");
        var alloc2 = ForceAllocator.Allocate(plan2, day2, ExecutorFixtures.Sectors(day2), null, null, null);
        foreach (var a in alloc2.Assignments)
            AssertTrue(a.Role == FormationRole.Hold && a.Reason == ForceAllocator.ReasonBusy, "both formations hold, busy");
        AssertEqual(decision, state.DecisionSerial, "(the decision serial moves on only at the next decision)");
        CampaignScope.ResetAll();
        PlanFreshness.NoteToday(day2.DaySerial);
        CampaignPlanStore.Publish(0, plan2, alloc2, ExecutorFixtures.Sectors(day2));
        CampaignPlanStore.PublishExecutorContext(0, new ExecutorContext { DecisionSerial = decision, ReleasedDecision = -1, ExecutorsAllowed = true, MapGroupInstanceId = -1 });
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, 1), "so no Support stays withheld for it, and vanilla's own pool is vanilla's");
        CampaignScope.ResetAll();
    }

    private static void DefendingMassMemberIsBusyAtNextReview()
    {
        var state = new PlannerState { CampaignSeed = 1861L };
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 200f, 20000f));
        s.Own.Add(ExecutorFixtures.Corps(2, 250f, 15000f));
        s.Own.Add(ExecutorFixtures.Corps(3, 300f, 12000f));
        var first = ExecutorFixtures.RealPlan(s, state);
        AssertEqual(3, first.MassInstanceIds.Count, "all three massed on the first review");

        var next = ExecutorFixtures.Wide(1);
        var defending = ExecutorFixtures.Corps(1, 200f, 20000f);
        defending.Flags |= FormationFlags.InDefensiveOps;        // vanilla's CheckForDefensiveOperations took it (13716-13717)
        next.Own.Add(defending);
        var attacking = ExecutorFixtures.Corps(2, 250f, 15000f);
        attacking.Flags |= FormationFlags.InOffensiveOps;        // the mover's own dispatch
        next.Own.Add(attacking);
        next.Own.Add(ExecutorFixtures.Corps(3, 300f, 12000f));
        var plan = ExecutorFixtures.RealPlan(next, state);
        AssertFalse(plan.MassInstanceIds.Contains(1), "a defending member leaves the mass");
        AssertTrue(plan.MassInstanceIds.Contains(2) && plan.MassInstanceIds.Contains(3), "a member in the mover's own offensive list stays in it");
        var alloc = ForceAllocator.Allocate(plan, next, ExecutorFixtures.Sectors(next), null, null, null);
        alloc.TryGet(1, out var d);
        AssertEqual(FormationRole.Hold, d.Role, "the defending member holds");
        AssertEqual(ForceAllocator.ReasonBusy, d.Reason, "with reason busy, until vanilla ends the defence (13782-13848)");
        alloc.TryGet(2, out var o);
        AssertEqual(FormationRole.MainEffort, o.Role, "the offensive-list member is still MainEffort");
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(next, plan, alloc, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), state);
        AssertFalse(host.Moves.Exists(m => m.id == 1), "the mover dispatches nothing to the defender");
        AssertEqual((uint)8, (uint)FormationFlags.InDefensiveOps, "InDefensiveOps is in TheaterPlanner.Busy");
        AssertTrue((TheaterPlanner.Busy & FormationFlags.InDefensiveOps) != 0, "the Busy mask");
        AssertTrue((TheaterPlanner.Busy & FormationFlags.InOffensiveOps) == 0, "and not InOffensiveOps: that would freeze the mass after its first dispatch");
    }

    // ---- what vanilla's own offense and defence may take ----

    private static void DeferredMassWithheldFromVanillaOffense()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Main), "every MainEffort mass member is withheld while the decision is deferred");
        ExecutorFixtures.Release(0);
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Main), "and after the release (an undispatched member is launched only by the mover)");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.FreeHold), "a Hold formation above its sector's need is not");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.Support), "nor Support once the decision is released");
        CampaignPlanStore.PublishExecutorContext(0, new ExecutorContext { DecisionSerial = 1, ReleasedDecision = -1, ExecutorsAllowed = false, MapGroupInstanceId = 3 });
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.Main), "with the pre-war gate nothing is");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.Reserve), "not even a Reserve formation");
        CampaignScope.ResetAll();
    }

    private static void DeferredMassAvailableForVanillaDefense()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        foreach (float ex in new[] { 10f, 100f, 330f, 560f })
        {
            AssertFalse(CampaignWithholdView.WithheldFromDefense(0, w.Main, ex, 0f), "a deferred mass member stays available to vanilla's defence, enemy at x " + ex);
            AssertFalse(CampaignWithholdView.WithheldFromDefense(0, w.Support, ex, 0f), "a deferred Support formation too");
        }
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Main) && CampaignWithholdView.WithheldFromOffense(0, w.Support), "while offense withholds both");
        CampaignScope.ResetAll();
    }

    private static void DeferredSupportWithheldFromVanillaOffenseUntilRelease()
    {
        var w = ExecutorFixtures.PublishedWorld(out _);
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, w.Support), "an AI Support formation of a deferred decision is withheld from offense");
        AssertFalse(CampaignWithholdView.WithheldFromDefense(0, w.Support, 100f, 0f), "never from defense");
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.MapGroupSupport), "the map-group player's Support formation is never withheld");
        ExecutorFixtures.Release(0);
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, w.Support), "on the release review the clause drops");
        CampaignScope.ResetAll();

        // A decision whose main effort needs no roll is released at its first dispatch and withholds no Support from then on.
        var g = GateWith(believedEnemy: false);
        Run(g, ExecutorFixtures.ClosedTempo());
        var alloc = g.Alloc;
        PlanFreshness.NoteToday(g.S.DaySerial);
        CampaignPlanStore.Publish(0, g.Plan, alloc, ExecutorFixtures.Sectors(g.S));
        CampaignPlanStore.PublishExecutorContext(0, ExecutorContext.From(g.State, true, -1));
        AssertFalse(CampaignWithholdView.WithheldFromOffense(0, 3), "released at the first dispatch: Support is vanilla's");
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, 1), "while the mass stays the mover's");
        CampaignScope.ResetAll();
    }

    private static void WlCareerMultipliesTwiceWithPlayerInMass()
    {
        var carrying = GateWith(believedEnemy: true);
        var c = carrying.S.Own[0];
        c.Chain = WlChainClass.CarryingGroup;
        carrying.S.Own[0] = c;
        var inputs = ExecutorFixtures.Inputs(carrying.S, carrying.Plan, carrying.Alloc, ExecutorFixtures.ClosedTempo());
        inputs.Tempo.WlActive = true;
        inputs.Tempo.WlAggressiveness = 1.5f;
        inputs.Tempo.ChapterAggression = 0.75f;
        var tally = CampaignMover.Review(inputs, new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State());
        float baseP = 0.75f / 365.25f * 10f;
        AssertNear(baseP * 1.5f * 1.5f, tally.Tempo.Probability, 1e-5f, "a carrying group in the mass: multiplied twice (14341-14347)");

        var plain = GateWith(believedEnemy: true);
        var inputs2 = ExecutorFixtures.Inputs(plain.S, plain.Plan, plain.Alloc, ExecutorFixtures.ClosedTempo());
        inputs2.Tempo.WlActive = true;
        inputs2.Tempo.WlAggressiveness = 1.5f;
        inputs2.Tempo.ChapterAggression = 0.75f;
        var tally2 = CampaignMover.Review(inputs2, new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State());
        AssertNear(baseP * 1.5f, tally2.Tempo.Probability, 1e-5f, "an AI mass: once");

        var off = GateWith(believedEnemy: true);
        var inputs3 = ExecutorFixtures.Inputs(off.S, off.Plan, off.Alloc, ExecutorFixtures.ClosedTempo());
        inputs3.Tempo.ChapterAggression = 0.75f;
        AssertNear(baseP, CampaignMover.Review(inputs3, new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State()).Tempo.Probability, 1e-5f, "outside a W&L scenario: not at all");
    }

    private static void ReleasedDecisionSurvivesRestore()
    {
        var state = new PlannerState();
        state.Restore(2, 98, -1, 0, 3, 1861L, belowOddsDays: 0, capitalThreatLatched: false, tempoReleasedDecision: 3, lastFullReviewDay: 4321);
        AssertEqual(3, state.TempoReleasedDecision, "the release is restored");
        AssertEqual(4321, state.LastFullReviewDay, "and the tempo clock's reference");
        var g = GateWith(believedEnemy: true);
        g.State = state;
        var tally = Run(g, ExecutorFixtures.ClosedTempo());
        AssertFalse(tally.Tempo.NeedsRoll, "a restored, released decision is not rolled again");
        AssertEqual(3, g.Host.Moves.Count, "and everything goes out");
        var fresh = new PlannerState();
        fresh.Restore(2, 98, -1, 0, 3, 1861L, tempoReleasedDecision: 2);
        AssertTrue(fresh.TempoReleasedDecision != fresh.DecisionSerial, "an older decision's release does not release the current one");
    }

    private static void TempoRowsAreBoundedToTwoPerDecision()
    {
        var g = GateWith(believedEnemy: true);
        var first = Run(g, ExecutorFixtures.ClosedTempo());
        AssertTrue(first.TempoFirstRow && !first.TempoReleaseRow, "the first review of a decision logs its row");
        AssertTrue(first.TempoRow(0).StartsWith("[CampaignTempo] alliance=0 main=98 decision=1 chapter=1 gap="), first.TempoRow(0));
        AssertContains(first.TempoRow(0), "released=false forced=false", "the row text");
        var again = GateWith(believedEnemy: true, dayOffset: 1);
        again.State = g.State;
        var second = Run(again, ExecutorFixtures.ClosedTempo());
        AssertFalse(second.TempoFirstRow || second.TempoReleaseRow, "a deferred decision logs nothing on later reviews");
        var release = GateWith(believedEnemy: true, dayOffset: 2);
        release.State = g.State;
        var third = Run(release, ExecutorFixtures.OpenTempo());
        AssertTrue(third.TempoReleaseRow && !third.TempoFirstRow, "the release logs its row");
        AssertContains(third.TempoRow(0), "released=true", "the release row");
        var after = GateWith(believedEnemy: true, dayOffset: 3);
        after.State = g.State;
        var fourth = Run(after, ExecutorFixtures.ClosedTempo());
        AssertFalse(fourth.TempoFirstRow || fourth.TempoReleaseRow, "and nothing after");

        var quick = GateWith(believedEnemy: false);
        var q = Run(quick, ExecutorFixtures.ClosedTempo());
        AssertTrue(q.TempoFirstRow && q.TempoReleaseRow, "a no-roll release is one row that is both");
        AssertEqual("", new MoverTally().Row(0), "no count, no [CampaignMover] row");
        var row = first.Row(0);
        AssertTrue(row.StartsWith("[CampaignMover] alliance=0 issued=0 confirmed=0 failed=0 retried=0 gaveUp=0 queued=0 skippedPlayer=0"), row);
    }

    private static void PlannerReadsNoCalendarEra()
    {
        string dir = "src/WhiskeyRealism/Strategic/Planner";
        foreach (string file in System.IO.Directory.GetFiles(FindRepoPath(dir), "*.cs"))
        {
            string src = System.IO.File.ReadAllText(file);
            AssertFalse(src.Contains("EraStage"), System.IO.Path.GetFileName(file) + " reads no EraStage: the late war reaches the planner only through DirectorPosture (decision 7)");
            AssertFalse(Regex.IsMatch(src, @"Year\s*[<>]=?\s*18[0-9]{2}"), System.IO.Path.GetFileName(file) + " tests no calendar year");
        }
    }

    private static void TempoGateMovedOutOfThePackageLedger()
    {
        string ledger = ReadRepoFile("src/WhiskeyRealism/Strategic/CoordinatedOperationPackageLedger.cs");
        AssertFalse(ledger.Contains("TempoProbability"), "TempoProbability left CoordinatedOffensiveVanillaGates (it is CampaignTempoGate's now)");
        AssertTrue(ledger.Contains("TempoPasses"), "TempoPasses stays until G6 deletes the file with #38");
        string patch = ReadRepoFile("src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs");
        AssertTrue(patch.Contains("float tval = CampaignTempoGate.TempoProbability("), "#38 rolls with the planner's copy until G6 deletes it");
        AssertFalse(patch.Contains("CoordinatedOffensiveVanillaGates.TempoProbability"), "no second copy");
    }

    // ---- the pre-war gate ----

    private static void PlannerIdlesPreWar001()
    {
        AssertFalse(PlannerChapterRules.ExecutorsAllowed("001", -1), "chapter -1 (before vanilla's frame-50 update) is pre-war");
        AssertFalse(PlannerChapterRules.ExecutorsAllowed("001", 0), "chapter 0: SetWaypoint refuses every move (91366)");
        AssertTrue(PlannerChapterRules.ExecutorsAllowed("001", 1), "chapter 1 runs");
        AssertTrue(PlannerChapterRules.ExecutorsAllowed("001", 3), "chapter 3 runs");
        // Appointments run in all three: the gate is applied after them (ReviewExecutePreWarReturnPinned) and
        // CommanderAppointmentRuntime itself tests no chapter.
        AssertFalse(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CommanderAppointmentRuntime.cs").Contains("PlannerChapterRules"), "appointments are not gated");
        AssertFalse(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CommanderAppointmentRuntime.cs").Contains("CurrentChapter"), "...by any chapter test");
    }

    private static void PreWarGateOnlyScenario001()
    {
        AssertTrue(PlannerChapterRules.ExecutorsAllowed("002", 1), "002 chapter 1 runs");
        AssertTrue(PlannerChapterRules.ExecutorsAllowed("002", 0), "002 is forced to chapter >= 1 by vanilla (211671); the gate does not apply to it at all");
        AssertTrue(PlannerChapterRules.ExecutorsAllowed("002", -1), "...even at chapter -1");
        AssertTrue(PlannerChapterRules.ExecutorsAllowed(null, 0), "no scenario");
        AssertTrue(PlannerChapterRules.ExecutorsAllowed("0010", 0), "an exact match on 001");
        AssertEqual("001", PlannerChapterRules.PreWarScenario, "the scenario");
    }

    private static void PreWarWithholdsNothing()
    {
        var w = ExecutorFixtures.PublishedWorld(out var tracker);
        tracker.RecordIssued(w.FreeHold, 300f, 0f, 100f, 0f, ExecutorFixtures.Day, queued: false);
        CampaignPlanStore.PublishExecutorContext(0, new ExecutorContext { DecisionSerial = 1, ReleasedDecision = -1, ExecutorsAllowed = false, MapGroupInstanceId = -1 });
        foreach (int id in new[] { w.Main, w.Support, w.Reserve, w.CapitalGuard, w.FreeHold, w.HoldAtNeed })
        {
            AssertFalse(CampaignWithholdView.WithheldFromOffense(0, id), "pre-war: formation " + id + " is vanilla's");
            AssertFalse(CampaignWithholdView.WithheldFromDefense(0, id, 100f, 0f), "pre-war: nor from defense: " + id);
        }
        AssertFalse(CampaignWithholdView.TrySteering(0, out _, out _), "and no importance redirect");
        CampaignScope.ResetAll();
    }

    private static void PaceNotTooQuietPreWar()
    {
        CampaignPaceInput Input(int chapter, bool winter) => new CampaignPaceInput
        {
            AllianceId = 0, Year = 1861, Month = 3, PolicyChapter = chapter, OwnNationalMorale = 95f, BreakMoraleTrigger = 30f, MinNationalMoraleSurrender = 18f,
            BattlesIn14Days = 0, MajorBattlesIn14Days = 0, CapitalDangerStreakDays = 0, DaysSinceFrontSignatureChange = 30, IsWinter = winter
        };
        AssertTrue(CampaignPaceLedger.Build(Input(0, false)).Pace != CampaignPace.TooQuiet, "no battles are possible before 15 Apr 1861 (211642-211656): chapter 0 is not 'too quiet'");
        AssertTrue(CampaignPaceLedger.Build(Input(-1, false)).Pace != CampaignPace.TooQuiet, "nor chapter -1");
        AssertEqual(CampaignPace.TooQuiet, CampaignPaceLedger.Build(Input(1, false)).Pace, "chapter 1 outside winter is still too quiet (the rule stays)");
        AssertTrue(CampaignPaceLedger.Build(Input(1, true)).Pace != CampaignPace.TooQuiet, "chapter 1 winter stays suppressed");
        AssertEqual(CampaignPace.TooQuiet, CampaignPaceLedger.Build(Input(2, true)).Pace, "chapter 2 winter is not");
    }

    private static void ReviewExecutePreWarReturnPinned()
    {
        string body = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs"), "internal static void ReviewExecute(");
        int appointments = body.IndexOf("CommanderAppointmentRuntime.Execute(alliance, Snapshots[alliance]);", StringComparison.Ordinal);
        int gate = body.IndexOf("if (!PlannerChapterRules.ExecutorsAllowed(GamePrefs.leveltoload, Policy.CurrentChapter))", StringComparison.Ordinal);
        int firstExecutor = body.IndexOf("CampaignSteeringRuntime.Execute(", StringComparison.Ordinal);
        AssertTrue(appointments >= 0 && gate > appointments && firstExecutor > gate, "the gate: after the appointment step and before the first move, order or steering executor");
        string gateBlock = body.Substring(gate, firstExecutor - gate);
        AssertTrue(gateBlock.Contains("return;"), "the gate returns");
        AssertTrue(gateBlock.Contains("\"campaign-planner:pre-war-idle\""), "with the static OnceLog key");
        AssertTrue(gateBlock.Contains("[CampaignPlanner] scenario 001 pre-war (chapter "), "and the line");
        string rules = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/PlannerChapterRules.cs");
        AssertTrue(HasLine(rules, "return !(scenario == PreWarScenario && chapter <= 0);"), "the rule, whole line");
        string pace = ReadRepoFile("src/WhiskeyRealism/Strategic/CampaignPaceLedger.cs");
        AssertTrue(HasLine(pace, "bool quietSuppressed = input.PolicyChapter <= 0 || (input.PolicyChapter <= 1 && input.IsWinter);"), "the pace rule, whole line");
    }

    // ---- helpers ----

    // Inputs for a roll over a mass that moved today (idle 0), one review's gap.
    private static TempoInputs TempoCase(float chapter, int timeDiffDays, float idle)
    {
        int today = ExecutorFixtures.Day;
        return new TempoInputs
        {
            ChapterAggression = chapter, FactionAggression = 1f, CampaignAggression = 1f, WinterModifier = 1f, WlAggressiveness = 1f,
            IdleYears = idle, TodayDay = today, LastFullReviewDay = today - timeDiffDays, CampaignSeed = 1861L, Alliance = 0, DecisionSerial = 1
        };
    }
}
