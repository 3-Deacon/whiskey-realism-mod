using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G slice G3 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G3): the historical
// prior table, the theater planner (live score, prior, hysteresis, seeded choice, postures,
// capital floor), the depot tasks, river lines, the march-time estimate and the runtime pins.
internal static class TheaterPlannerTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("ReachFalseBeyondSourcesYieldsDepotTask", ReachFalseBeyondSourcesYieldsDepotTask),
            ("DepotTasksSkipSiteNearAnExistingDepot", DepotTasksSkipSiteNearAnExistingDepot),
            ("PriorInsideWindowIsFull", PriorInsideWindowIsFull),
            ("PriorDecaysOutsideWindow", PriorDecaysOutsideWindow),
            ("PriorTableRows", PriorTableRows),
            ("LiveMarginAbovePriorMaxWins", LiveMarginAbovePriorMaxWins),
            ("PriorBreaksNearTie", PriorBreaksNearTie),
            ("HysteresisHoldsBelowMargin", HysteresisHoldsBelowMargin),
            ("HysteresisSwitchesAfterThreeDays", HysteresisSwitchesAfterThreeDays),
            ("CapitalThreatSwitchesImmediately", CapitalThreatSwitchesImmediately),
            ("AccomplishedObjectiveSwitchesImmediately", AccomplishedObjectiveSwitchesImmediately),
            ("DecisiveBattleNearTargetSwitchesImmediately", DecisiveBattleNearTargetSwitchesImmediately),
            ("SeededChoiceReproducible", SeededChoiceReproducible),
            ("RandomOnlyWithinEpsilon", RandomOnlyWithinEpsilon),
            ("BoldCommanderAcceptsWorseOdds", BoldCommanderAcceptsWorseOdds),
            ("TempoMassModifierRaisesRequiredRatio", TempoMassModifierRaisesRequiredRatio),
            ("NullTempoIsNeutral", NullTempoIsNeutral),
            ("CampaignSeedIsFnv1aOfScenarioAndStartDate", CampaignSeedIsFnv1aOfScenarioAndStartDate),
            ("UnseenEnemyDoesNotLowerBalance", UnseenEnemyDoesNotLowerBalance),
            ("EmptySnapshotProducesNoMainEffort", EmptySnapshotProducesNoMainEffort),
            ("AllianceTwoReturnsEmptyPlan", AllianceTwoReturnsEmptyPlan),
            ("RecoverUsesRawSupplyNotVanillaCap", RecoverUsesRawSupplyNotVanillaCap),
            ("PostureAssignmentCoversEverySectorWithOwnFormations", PostureAssignmentCoversEverySectorWithOwnFormations),
            ("ScreenOnlyBelowHoldNeedParityIsHold", ScreenOnlyBelowHoldNeedParityIsHold),
            ("CapitalFloorIsVanillaWeightingOnBeliefs", CapitalFloorIsVanillaWeightingOnBeliefs),
            ("RiverLineDetourViaNearestCrossing", RiverLineDetourViaNearestCrossing),
            ("UnblockedLineKeepsStraightDistance", UnblockedLineKeepsStraightDistance),
            ("NoCrossingNeverRemovesThreat", NoCrossingNeverRemovesThreat),
            ("ThreatBehindRiverWeighsLessNotZero", ThreatBehindRiverWeighsLessNotZero),
            ("AdvanceAcrossRiverCostsTheDetour", AdvanceAcrossRiverCostsTheDetour),
            ("HoldNeedFollowsLongerThreatDistance", HoldNeedFollowsLongerThreatDistance),
            ("MarchTimeUsesTheP6SpeedChain", MarchTimeUsesTheP6SpeedChain),
            ("MarchTimeCreditsRiverCorridor", MarchTimeCreditsRiverCorridor),
            ("MarchTimeCreditsRailOnlyWithTrainCapacity", MarchTimeCreditsRailOnlyWithTrainCapacity),
            ("AfloatEnemyIsThreatNotHoldableContact", AfloatEnemyIsThreatNotHoldableContact),
            ("PlannerWeightsAndThresholdsPinned", PlannerWeightsAndThresholdsPinned),
            ("PlanSummaryNamesTheDecision", PlanSummaryNamesTheDecision),
            ("BattleInboxDeliversToBothSides", BattleInboxDeliversToBothSides),
            ("PlannerStateRestoreRoundTrips", PlannerStateRestoreRoundTrips),
            ("TerrainProbeMirrorsVanillaEngagementLine", TerrainProbeMirrorsVanillaEngagementLine),
            ("PlannerPlanStepPinned", PlannerPlanStepPinned),
            ("BattleForwardFollowsHistoryAdd", BattleForwardFollowsHistoryAdd),
            ("StaleBattlesAfterAPauseAreDropped", StaleBattlesAfterAPauseAreDropped),
            ("OddsDeadbandHoldsTheMainEffort", OddsDeadbandHoldsTheMainEffort),
            ("OddsDropBelowDeadbandOrAfterSwitchDays", OddsDropBelowDeadbandOrAfterSwitchDays),
            ("StandingCapitalThreatDoesNotFlap", StandingCapitalThreatDoesNotFlap),
            ("CapitalLatchRearmsBelowRelease", CapitalLatchRearmsBelowRelease),
            ("TriggerKeepsCurrentWithinEpsilon", TriggerKeepsCurrentWithinEpsilon),
            ("EligibilityCountsMassableStrength", EligibilityCountsMassableStrength),
            ("MassExcludesHoldsBusyAndMapGroupPlayer", MassExcludesHoldsBusyAndMapGroupPlayer),
            ("MassOnlyWithinMarchHorizon", MassOnlyWithinMarchHorizon),
            ("RiverDiscountIsOrderIndependent", RiverDiscountIsOrderIndependent),
            ("ReviewTerrainRecordsEachUnknownOnce", ReviewTerrainRecordsEachUnknownOnce),
            ("ReviewTerrainProbesInFixedOrder", ReviewTerrainProbesInFixedOrder),
            ("DrainEnumeratesExactlyTheAreaPairSet", DrainEnumeratesExactlyTheAreaPairSet),
            ("DrainRespectsTheFrameBudget", DrainRespectsTheFrameBudget),
            ("DrainCompletesThenReviewAppliesRiverTerms", DrainCompletesThenReviewAppliesRiverTerms),
            ("DrainAppendsNewCommandRangesOnly", DrainAppendsNewCommandRangesOnly),
            ("TerrainDrainWiringPinned", TerrainDrainWiringPinned),
            ("DrainMemoBelongsToTheMap", DrainMemoBelongsToTheMap),
            ("SegmentsStayWithinTheFrameBudget", SegmentsStayWithinTheFrameBudget),
            ("SegmentStartIsVanillasWalk", SegmentStartIsVanillasWalk),
            ("MassExcludesTheCapitalReserve", MassExcludesTheCapitalReserve),
            ("BusyFormationNeverTheCandidateForce", BusyFormationNeverTheCandidateForce),
            ("CandidateForceIsTheMainBody", CandidateForceIsTheMainBody),
            ("SeedFoldsDecisionSerialAndAlliance", SeedFoldsDecisionSerialAndAlliance),
            ("EqualChallengersBreakTiesOnLowerObjectiveId", EqualChallengersBreakTiesOnLowerObjectiveId),
            ("UnoccupiedThreatenedSectorIsScreen", UnoccupiedThreatenedSectorIsScreen),
            ("LowMoraleSectorRecovers", LowMoraleSectorRecovers),
            ("SupportFollowsTheMainAxis", SupportFollowsTheMainAxis),
            ("ExposureLowersTheScore", ExposureLowersTheScore),
            ("SnapshotReadsGroupSpeed", SnapshotReadsGroupSpeed),
        });
    }

    // 1861-06-15: inside union-east-pressure (alliance 0, objective 3, 1861-04..12, tier 3).
    private static readonly int Day = ReviewCadenceCatchUp.DaySerial(15, 6, 1861);

    // ---- depot tasks (spec component 5) ----

    private static void ReachFalseBeyondSourcesYieldsDepotTask()
    {
        // Own IIPs at x 100 and 300 supply a corps (command range 40) out to x 340; the target at
        // 400 is beyond: the plan keeps the objective, stops the advance at 340 and lays depots.
        var s = Line();
        s.Objectives.Add(Target(99, 400f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        var plan = PlanOnce(s);

        AssertEqual(99, plan.MainObjectiveId, "out-of-reach objective stays the main effort");
        var main = SectorOf(plan, TheaterPosture.MainEffort);
        AssertNear(340f, main.TargetX, 0.5f, "advance stops at the farthest in-reach point");
        AssertEqual(2, plan.DepotTasks.Count, "two depot tasks along the axis");
        AssertNear(340f, plan.DepotTasks[0].X, 0.5f, "first depot at the supply frontier");
        AssertNear(380f, plan.DepotTasks[1].X, 0.5f, "second depot one reach range on");
        AssertTrue(plan.DepotTasks[0].Priority > plan.DepotTasks[1].Priority, "the nearer depot first");
        AssertEqual(main.SectorId, plan.DepotTasks[0].SectorId, "depot sector from the sector model");

        // With an own IIP at the target it is in reach: no depot task, the target itself.
        var supplied = Line();
        supplied.Objectives.Add(Target(99, 400f, 1f));
        supplied.Own.Add(Corps(1, 200f, 20000f));
        supplied.Supply.Add(Iip(504, 400f, 0));
        var inReach = PlanOnce(supplied);
        AssertEqual(0, inReach.DepotTasks.Count, "in reach: no depot task");
        AssertNear(400f, SectorOf(inReach, TheaterPosture.MainEffort).TargetX, 0.01f, "in reach: the target itself");
    }

    private static void DepotTasksSkipSiteNearAnExistingDepot()
    {
        var s = Line();
        s.Objectives.Add(Target(99, 400f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Supply.Add(new SupplySourceRecord { Id = 700, Kind = SupplySourceKind.Depot, Owner = 0, X = 320f, Z = 0f, Built = false });
        var plan = PlanOnce(s);
        AssertEqual(1, plan.DepotTasks.Count, "the frontier site already has a depot coming");
        AssertNear(380f, plan.DepotTasks[0].X, 0.5f, "only the next site");
    }

    // ---- historical prior (P12) ----

    private static void PriorInsideWindowIsFull()
    {
        AssertNear(1f, HistoricalPriorTable.PriorFor(0, 3, Day, out string id), 0.0001f, "tier-3 entry inside its window");
        AssertEqual("union-east-pressure", id, "prior id");
        AssertNear(2f / 3f, HistoricalPriorTable.PriorFor(1, 32, Day, out _), 0.0001f, "tier 2 (catalog priority 15)");
        int coastal = ReviewCadenceCatchUp.DaySerial(15, 6, 1862);
        AssertNear(1f / 3f, HistoricalPriorTable.PriorFor(0, 37, coastal, out _), 0.0001f, "tier 1 (catalog priority 20)");
        AssertEqual(0f, HistoricalPriorTable.PriorFor(1, 3, Day, out string none), "another alliance's objective has no prior");
        AssertEqual(null, none, "no prior id");
    }

    private static void PriorDecaysOutsideWindow()
    {
        HistoricalPriorTable.Entry east = default(HistoricalPriorTable.Entry);
        foreach (var e in HistoricalPriorTable.Entries)
            if (e.Id == "union-east-pressure") east = e;
        double month = HistoricalPriorTable.MeanMonthDays;
        bool calendar = ReviewCadenceCatchUp.DaySerial(1, 1, 1862) - ReviewCadenceCatchUp.DaySerial(1, 1, 1861) == 365;
        AssertEqual(calendar ? 30.4375 : 31.0, month, "the serial's mean month: 1461 / 48 calendar days, or the legacy 31");
        AssertNear(0.5f, HistoricalPriorTable.Weight(east, east.EndSerial + (int)Math.Round(3 * month)), 0.005f, "three months after the window");
        AssertNear(0.25f, HistoricalPriorTable.Weight(east, east.EndSerial + (int)Math.Round(6 * month)), 0.005f, "six months after");
        AssertNear(0.5f, HistoricalPriorTable.Weight(east, east.StartSerial - (int)Math.Round(3 * month)), 0.005f, "three months before");
        AssertNear((float)Math.Pow(0.5, 40 / month / 3.0), HistoricalPriorTable.Weight(east, east.EndSerial + 40), 0.0001f, "0.5^(days / month / 3)");
        AssertNear(1f, HistoricalPriorTable.Weight(east, east.EndSerial), 0.0001f, "the window's last day is inside");
        AssertTrue(HistoricalPriorTable.Weight(east, east.EndSerial + 1) < 1f, "the next day is outside");
        // A window ending in a 30-day month ends on the 30th, whatever the serial.
        var june = new HistoricalPriorTable.Entry { StartSerial = east.StartSerial, EndSerial = ReviewCadenceCatchUp.DaySerial(1, 7, 1861) - 1, Priority = 3 };
        AssertTrue(HistoricalPriorTable.Weight(june, ReviewCadenceCatchUp.DaySerial(30, 6, 1861)) == 1f, "30 June inside");
        AssertTrue(HistoricalPriorTable.Weight(june, ReviewCadenceCatchUp.DaySerial(1, 7, 1861)) < 1f, "1 July outside");
    }

    // The six rows as the retired historical operation catalog had them (the catalog went in G6): id, alliance, objectives,
    // first and last month of the window, and the tier its catalog priority (10 / 15 / 20) maps to.
    private static void PriorTableRows()
    {
        var rows = new (string Id, int Alliance, int Objective, int StartMonth, int StartYear, int EndMonth, int EndYear, int Tier)[]
        {
            ("union-east-pressure", 0, 3, 4, 1861, 12, 1861, 3),
            ("csa-valley-disruption", 1, 32, 4, 1861, 12, 1861, 2),
            ("union-coastal-pressure", 0, 37, 1, 1862, 12, 1862, 1),
            ("union-western-pressure", 0, 29, 1, 1862, 12, 1862, 3),
            ("csa-western-depth", 1, 30, 1, 1862, 12, 1862, 3),
            ("union-late-pressure", 0, 3, 5, 1864, 12, 1865, 3)
        };
        AssertEqual(rows.Length, HistoricalPriorTable.Entries.Count, "six catalog profiles");
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            var e = HistoricalPriorTable.Entries[i];
            AssertEqual(r.Id, e.Id, "row " + i + " id");
            AssertEqual(r.Alliance, e.Alliance, r.Id + " alliance");
            AssertEqual(r.Objective.ToString(), string.Join(",", e.ObjectiveIds), r.Id + " objectives");
            AssertEqual(ReviewCadenceCatchUp.DaySerial(1, r.StartMonth, r.StartYear), e.StartSerial, r.Id + " window start");
            AssertEqual(ReviewCadenceCatchUp.DaySerial(1, r.EndMonth + 1, r.EndYear) - 1, e.EndSerial, r.Id + " window end: the end month's last day");
            AssertEqual(r.Tier, e.Priority, r.Id + " tier");
        }
        AssertEqual(3, HistoricalPriorTable.TierOf(10), "catalog 10 is the top tier");
        AssertEqual(2, HistoricalPriorTable.TierOf(15), "catalog 15");
        AssertEqual(1, HistoricalPriorTable.TierOf(20), "catalog 20");
    }

    // ---- live score vs prior (P3) ----

    private static void LiveMarginAbovePriorMaxWins()
    {
        // Objective 3 carries a full prior (+0.08); objective 99 leads on live score by 0.135
        // (value 0.30 x 0.5 x 0.9): any live margin above PriorMaxContribution wins.
        var s = Line();
        s.Objectives.Add(Target(3, 100f, 0.1f));
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(8, 150f, 20000f));   // the garrison (D2)
        var plan = PlanOnce(s);
        AssertEqual(99, plan.MainObjectiveId, "live margin above the prior's maximum beats the prior");

        foreach (var e in HistoricalPriorTable.Entries)
            foreach (int day in new[] { e.StartSerial - 400, e.StartSerial, e.EndSerial, e.EndSerial + 400 })
                AssertTrue(TheaterPlanner.PriorLambda * HistoricalPriorTable.Weight(e, day) <= TheaterPlanner.PriorMaxContribution + 1e-6f,
                    "prior contribution bounded for " + e.Id);
    }

    private static void PriorBreaksNearTie()
    {
        var s = Line();
        s.Objectives.Add(Target(3, 100f, 0.5f));
        s.Objectives.Add(Target(99, 300f, 0.5f));
        s.Own.Add(Corps(1, 200f, 20000f));
        var plan = PlanOnce(s);
        AssertEqual(3, plan.MainObjectiveId, "the prior breaks a live tie");
        AssertEqual("union-east-pressure", plan.PriorId, "prior id carried into the plan");
    }

    // ---- hysteresis (P4) ----

    private static void HysteresisHoldsBelowMargin()
    {
        // Current 98; 99 better by 0.09 (< SwitchMargin): held on every review.
        var s = TwoTargets(0.5f, 1.1f);
        var state = StateOn(98);
        for (int day = 0; day < 5; day++)
        {
            var plan = Plan(s, state);
            AssertEqual(98, plan.MainObjectiveId, "below margin holds, review " + day);
            AssertEqual("hold", plan.SwitchReason, "hold reason");
        }

        // 99 better by 0.15 (> SwitchMargin): still held on the first two reviews.
        var wide = TwoTargets(0f, 1f);
        var counting = StateOn(98);
        for (int day = 0; day < TheaterPlanner.SwitchDays - 1; day++)
            AssertEqual(98, Plan(wide, counting).MainObjectiveId, "above margin but fewer than SwitchDays, review " + day);
        AssertEqual(99, counting.ChallengerObjective, "challenger counted");
        AssertEqual(TheaterPlanner.SwitchDays - 1, counting.ChallengerDays, "challenger days");
    }

    private static void HysteresisSwitchesAfterThreeDays()
    {
        var s = TwoTargets(0f, 1f);
        var state = StateOn(98);
        TheaterPlan plan = null;
        for (int day = 0; day < TheaterPlanner.SwitchDays; day++) plan = Plan(s, state);
        AssertEqual(99, plan.MainObjectiveId, "switch on the third review above margin");
        AssertEqual("margin", plan.SwitchReason, "switch reason");
        AssertEqual(1, state.DecisionSerial, "decision serial advanced once");
        AssertEqual(-1, state.ChallengerObjective, "challenger reset after the switch");

        // A challenger that drops back below the margin restarts the count.
        var flip = StateOn(98);
        Plan(TwoTargets(0f, 1f), flip);
        Plan(TwoTargets(0.5f, 0.6f), flip);
        AssertEqual(0, flip.ChallengerDays, "count reset below margin");
    }

    private static void CapitalThreatSwitchesImmediately()
    {
        // Current 99 (x 300) leads 98 (x 100) by 0.015. A believed army 100 from the own capital
        // (x 0) with no own force near it: threat 1, and 98's closeness to the capital (0.67) adds
        // 0.067: 98 now leads by 0.05, below SwitchMargin, but the trigger switches at once. A second
        // corps beside the first: the capital reserve (nearest the capital) takes one of them.
        var s = TwoTargets(0.5f, 0.6f);
        s.Own.Add(Corps(2, 200f, 20000f));
        s.Beliefs.Add(Belief(-100f, 0f, 10000f, 1f));
        var state = StateOn(99);
        var plan = Plan(s, state);
        AssertEqual(98, plan.MainObjectiveId, "capital threat switches on the first review");
        AssertEqual("capital-threat", plan.SwitchReason, "trigger reason");

        var calm = TwoTargets(0.5f, 0.6f);
        calm.Own.Add(Corps(2, 0f, 20000f)); // the capital is held: threat 0.06
        var calmState = StateOn(99);
        AssertEqual(99, Plan(calm, calmState).MainObjectiveId, "no trigger without a capital threat");
    }

    private static void AccomplishedObjectiveSwitchesImmediately()
    {
        var s = TwoTargets(0f, 1f);
        var done = s.Objectives[1];
        done.Accomplished = true;
        done.Available = false;
        s.Objectives[1] = done;
        var state = StateOn(99);
        var plan = Plan(s, state);
        AssertEqual(98, plan.MainObjectiveId, "accomplished objective is replaced at once");
        AssertEqual("accomplished", plan.SwitchReason, "trigger reason");
    }

    private static void DecisiveBattleNearTargetSwitchesImmediately()
    {
        var decisive = new BattleHistoryRecord { LandOrSea = 0, BattleResultType = 1, AllianceWon = 1, Alliance = new[] { 0, 1 }, PositionX = 300f, PositionZ = 0f };
        var state = StateOn(99);
        var plan = TheaterPlanner.Plan(TwoTargets(0.5f, 0.1f), Sectors(TwoTargets(0.5f, 0.1f)), null, state,
            new[] { decisive }, null, Speed(), out _);
        AssertEqual(98, plan.MainObjectiveId, "decisive battle at the target switches at once");
        AssertEqual("battle", plan.SwitchReason, "trigger reason");

        var minor = new BattleHistoryRecord { LandOrSea = 0, BattleResultType = 2, AllianceWon = 1, PositionX = 300f, PositionZ = 0f };
        var held = StateOn(99);
        var kept = TheaterPlanner.Plan(TwoTargets(0.5f, 0.1f), Sectors(TwoTargets(0.5f, 0.1f)), null, held,
            new[] { minor }, null, Speed(), out _);
        AssertEqual(99, kept.MainObjectiveId, "a minor battle is history only");
    }

    // ---- seeded randomness (P5) ----

    private static void SeededChoiceReproducible()
    {
        var s = TwoTargets(0.5f, 0.5f);
        var reversed = Line();
        reversed.Objectives.Add(s.Objectives[1]);
        reversed.Objectives.Add(s.Objectives[0]);
        reversed.Own.Add(Corps(1, 200f, 20000f));
        reversed.Own.Add(Corps(8, 150f, 20000f));
        var seen = new HashSet<int>();
        for (long seed = 1; seed <= 20; seed++)
        {
            var a = new PlannerState { CampaignSeed = seed };
            var b = new PlannerState { CampaignSeed = seed };
            var c = new PlannerState { CampaignSeed = seed };
            int choice = Plan(s, a).MainObjectiveId;
            AssertEqual(choice, Plan(s, b).MainObjectiveId, "same seed, same choice " + seed);
            AssertEqual(choice, Plan(reversed, c).MainObjectiveId, "same choice whatever order vanilla lists the objectives in, seed " + seed);
            seen.Add(choice);
        }
        AssertTrue(seen.Contains(98) && seen.Contains(99), "the seeds draw both");
    }

    private static void RandomOnlyWithinEpsilon()
    {
        // 98 and 99 tie; 97 (beside 99, less important) is 0.06 below them: just outside Epsilon,
        // where the softmax alone would still draw it about one time in forty.
        var s = TwoTargets(0.5f, 0.5f);
        s.Objectives.Add(Target(97, 300f, 0.1f));
        var seen = new HashSet<int>();
        for (long seed = 1; seed <= 400; seed++)
            seen.Add(Plan(s, new PlannerState { CampaignSeed = seed }).MainObjectiveId);
        AssertTrue(seen.Contains(98) && seen.Contains(99), "both near-equal candidates are drawn");
        AssertFalse(seen.Contains(97), "a candidate outside Epsilon is never drawn");
    }

    // ---- personality and tempo (spec component 3, C17) ----

    private static void BoldCommanderAcceptsWorseOdds()
    {
        // Own 20000 vs a believed 20000 at the target: odds 1.0, below vanilla's 1.1 dominance.
        var s = Line();
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Beliefs.Add(Belief(300f, 0f, 20000f, 1f));
        AssertEqual(-1, PlanOnce(s).MainObjectiveId, "a neutral commander does not attack at even odds");

        Func<int, CommanderProfile> bold = id => new CommanderProfile { RequiredOddsShift = -0.25f };
        var plan = TheaterPlanner.Plan(s, Sectors(s), bold, new PlannerState(), null, null, Speed(), out _);
        AssertEqual(99, plan.MainObjectiveId, "a bold commander attacks at worse odds");
        AssertNear(0.85f, plan.MainRequiredRatio, 0.0001f, "required ratio shifted by the profile");
    }

    private static void TempoMassModifierRaisesRequiredRatio()
    {
        var neutral = PlanOnce(OneTarget(null));
        var massed = PlanOnce(OneTarget(new DirectorPosture { MassRatioModifier = 0.1f }));
        AssertNear(1.1f, neutral.MainRequiredRatio, 0.0001f, "aidominanceforoffensiveoperations");
        AssertNear(1.21f, massed.MainRequiredRatio, 0.0001f, "x (1 + MassRatioModifier)");
    }

    private static void NullTempoIsNeutral()
    {
        var none = PlanOnce(OneTarget(null));
        var zero = PlanOnce(OneTarget(new DirectorPosture()));
        AssertEqual(TheaterPlanner.Summary(zero), TheaterPlanner.Summary(none), "null tempo = zero modifiers");
        AssertNear(zero.MainRequiredRatio, none.MainRequiredRatio, 0f, "same ratio");
    }

    private static void CampaignSeedIsFnv1aOfScenarioAndStartDate()
    {
        AssertEqual(0xcbf29ce484222325UL, PlannerState.Fnv1a64(""), "FNV-1a 64 empty");
        AssertEqual(0xaf63dc4c8601ec8cUL, PlannerState.Fnv1a64("a"), "FNV-1a 64 'a'");
        AssertEqual(0x85944171f73967e8UL, PlannerState.Fnv1a64("foobar"), "FNV-1a 64 'foobar'");
        long seed = PlannerState.ComputeCampaignSeed("Campaign/Scenario002", 1861, 4, 12);
        AssertEqual(unchecked((long)PlannerState.Fnv1a64("Campaign/Scenario002|1861-04-12")), seed, "scenario|yyyy-mm-dd");
        AssertTrue(seed != PlannerState.ComputeCampaignSeed("Campaign/Scenario002", 1861, 4, 13), "the date is part of the seed");
    }

    // ---- fog (spec component 2) ----

    private static void UnseenEnemyDoesNotLowerBalance()
    {
        float clear = BalanceAt(Line(), null, 99);
        var expired = Line();
        expired.Beliefs.Add(Belief(300f, 0f, 50000f, 0f));
        AssertNear(clear, BalanceAt(expired, null, 99), 0f, "an expired (unseen) belief changes nothing");
        var seen = Line();
        seen.Beliefs.Add(Belief(300f, 0f, 20000f, 0.5f));
        float half = BalanceAt(seen, null, 99);
        AssertNear(20000f / 30000f, half, 0.0001f, "a live belief counts at its confidence");
    }

    private static void EmptySnapshotProducesNoMainEffort()
    {
        var s = Fresh(0);
        var state = new PlannerState();
        var plan = Plan(s, state);
        AssertEqual(-1, plan.MainObjectiveId, "no objective");
        AssertEqual(-1, plan.MainSectorId, "no sector");
        AssertEqual(0, plan.Sectors.Count, "no sector plans");
        AssertEqual(0, plan.DepotTasks.Count, "no depot tasks");
        AssertEqual("none", plan.SwitchReason, "reason");
        AssertEqual(0, state.DecisionSerial, "no decision");
    }

    private static void AllianceTwoReturnsEmptyPlan()
    {
        var s = Line(2);
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        var state = new PlannerState();
        var plan = Plan(s, state);
        AssertEqual(2, plan.Alliance, "alliance kept");
        AssertEqual(-1, plan.MainObjectiveId, "Europe gets no plan");
        AssertEqual(0, plan.Sectors.Count, "no sectors");
        AssertEqual(-1, state.CurrentMainObjective, "state untouched");
    }

    // ---- postures ----

    private static void RecoverUsesRawSupplyNotVanillaCap()
    {
        AssertTrue(PostureOfFarSector(0.45f) != TheaterPosture.Recover, "raw 0.45 (vanilla 0.225) is not Recover");
        AssertEqual(TheaterPosture.Recover, PostureOfFarSector(0.35f), "raw 0.35 is Recover");
    }

    private static void PostureAssignmentCoversEverySectorWithOwnFormations()
    {
        var s = Wide();
        s.Own.Add(Corps(2, 350f, 5000f));
        s.Own.Add(Corps(3, 550f, 8000f));
        s.Beliefs.Add(Belief(600f, 0f, 10000f, 1f));
        var plan = PlanOnce(s);
        var sectors = Sectors(s);
        AssertEqual(98, plan.MainObjectiveId, "main effort at x 100");
        var covered = new HashSet<int>();
        foreach (var sp in plan.Sectors)
        {
            AssertTrue(covered.Add(sp.SectorId), "one plan per sector");
            AssertEqual(sectors.TheaterOf(sp.SectorId), sp.Theater, "theater from the sector model");
        }
        foreach (var f in s.Own)
            AssertTrue(covered.Contains(sectors.SectorAt(f.X, f.Z)), "sector with own formations has a posture: " + f.Name);
        AssertEqual(TheaterPosture.MainEffort, PostureOf(plan, sectors.SectorAt(100f, 0f)), "main sector");
        AssertEqual(TheaterPosture.Hold, PostureOf(plan, sectors.SectorAt(550f, 0f)), "threatened sector that can hold");
    }

    private static void ScreenOnlyBelowHoldNeedParityIsHold()
    {
        AssertEqual(TheaterPosture.Hold, PostureAt550(10000f, 10000f, null), "own equal to the believed enemy holds");
        AssertEqual(TheaterPosture.Hold, PostureAt550(6000f, 10000f, null), "own at the hold need (0.6) holds");
        AssertEqual(TheaterPosture.Screen, PostureAt550(5000f, 10000f, null), "own below the hold need screens");
    }

    private static void CapitalFloorIsVanillaWeightingOnBeliefs()
    {
        var s = TwoTargets(0.5f, 0.6f);
        s.Beliefs.Add(Belief(-100f, 0f, 10000f, 0.5f));
        var plan = Plan(s, new PlannerState());
        float expected = 0.5f * 10000f / (float)Math.Pow(100.0, 0.3) * 0.5f;
        AssertNear(expected, plan.CapitalFloorStrength, 0.01f, "11712-11741 weighting on the confidence-weighted belief");

        var far = TwoTargets(0.5f, 0.6f);
        far.Beliefs.Add(Belief(-200f, 0f, 10000f, 1f));
        AssertNear(0f, Plan(far, new PlannerState()).CapitalFloorStrength, 0f, "beyond relocation range: no floor");
    }

    // ---- river lines (G0 landing B) ----

    private static void RiverLineDetourViaNearestCrossing()
    {
        var river = new RiverAtX(50f);
        var crossings = new List<CrossingRecord> { new CrossingRecord { X = 50f, Z = 80f }, new CrossingRecord { X = 50f, Z = 10f } };
        float d = RiverLines.EffectiveDistance(0f, 0f, 100f, 0f, river, crossings);
        AssertNear(2f * (float)Math.Sqrt(50 * 50 + 10 * 10), d, 0.01f, "the nearest crossing's detour");
    }

    private static void UnblockedLineKeepsStraightDistance()
    {
        var river = new RiverAtX(50f);
        var crossings = new List<CrossingRecord> { new CrossingRecord { X = 50f, Z = 80f } };
        AssertNear(40f, RiverLines.EffectiveDistance(0f, 0f, 40f, 0f, river, crossings), 0.0001f, "clear line: straight");
        AssertNear(100f, RiverLines.EffectiveDistance(0f, 0f, 100f, 0f, null, crossings), 0.0001f, "no probe: straight");
    }

    private static void NoCrossingNeverRemovesThreat()
    {
        var river = new RiverAtX(50f);
        AssertNear(100f, RiverLines.EffectiveDistance(0f, 0f, 100f, 0f, river, new List<CrossingRecord>()), 0.0001f, "no crossing: straight");

        var s = Line();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Beliefs.Add(Belief(420f, 0f, 10000f, 1f));
        float open = BalanceAt(s, null, 99);
        float blockedNoCrossing = BalanceAt(s, new RiverAtX(360f), 99);
        AssertNear(open, blockedNoCrossing, 0f, "a river with no crossing keeps the full threat");
    }

    private static void ThreatBehindRiverWeighsLessNotZero()
    {
        var s = Line();
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Beliefs.Add(Belief(420f, 0f, 10000f, 1f));
        float open = BalanceAt(s, null, 99);
        s.Crossings.Add(new CrossingRecord { X = 360f, Z = 400f });
        float behindRiver = BalanceAt(s, new RiverAtX(360f), 99);
        var none = Line();
        none.Own.Add(Corps(1, 200f, 20000f));
        float noEnemy = BalanceAt(none, null, 99);
        AssertNear(20000f / 30000f, open, 0.0001f, "open ground: full weight");
        AssertTrue(behindRiver > open, "behind a river the threat weighs less");
        AssertTrue(behindRiver < noEnemy, "but not zero");
    }

    private static void AdvanceAcrossRiverCostsTheDetour()
    {
        var s = Line();
        s.Own.Add(Corps(1, 200f, 20000f));
        float open = Candidate(s, null, 99).March;
        s.Crossings.Add(new CrossingRecord { X = 250f, Z = 200f });
        float across = Candidate(s, new RiverAtX(250f), 99).March;
        AssertTrue(across < open, "the advance pays the detour to the crossing");
        float detourDays = 2f * (float)Math.Sqrt(50 * 50 + 200 * 200) / RoadPerDay();
        AssertNear(1f - detourDays / TheaterPlanner.MarchHorizonDays, across, 0.001f, "march time over the detour");
    }

    private static void HoldNeedFollowsLongerThreatDistance()
    {
        // Own 5000 vs a believed 10000 at x 640: open ground needs 6000 -> Screen; behind a river at
        // x 575 crossed only at (575, 300) the threat weighs 200 / 608 -> need ~1970 -> Hold.
        AssertEqual(TheaterPosture.Screen, PostureAt550(5000f, 10000f, null, 640f), "open ground");
        AssertEqual(TheaterPosture.Hold, PostureAt550(5000f, 10000f, new RiverAtX(575f), 640f, new CrossingRecord { X = 575f, Z = 300f }),
            "the river lowers the hold need");
    }

    // ---- march time (P6 chain, G0 landing B) ----

    private static void MarchTimeUsesTheP6SpeedChain()
    {
        var f = Corps(1, 0f, 20000f);
        f.GroupSpeed = 0.5f;
        f.UseRails = f.UseRivers = false;
        var m = Speed();
        float perDay = 2f * 0.5f * 1000f * 30f / 2f / 365.25f;
        AssertNear(perDay, MarchTime.SpeedPerDay(m, 15, 0.5f, m.RoadCost), 0.0001f, "unitbasicspeed x groupspeed x basicunitspeed x factor / road cost / 365.25");
        float days = MarchTime.EstimateDays(m, f, 400f, 0f, 400f, null, null);
        AssertNear(400f / perDay, days, 0.001f, "days over the road rate");

        f.GroupSpeed = 1f;
        AssertNear(days / 2f, MarchTime.EstimateDays(m, f, 400f, 0f, 400f, null, null), 0.001f, "groupspeed");
        f.GroupSpeed = 0.5f;
        var faster = m;
        faster.CampaignSpeedFactor = 60f;
        AssertNear(days / 2f, MarchTime.EstimateDays(faster, f, 400f, 0f, 400f, null, null), 0.001f, "generalcampaignunitspeedfactor");
        var basic = m;
        basic.BasicUnitSpeed = 2000f;
        AssertNear(days / 2f, MarchTime.EstimateDays(basic, f, 400f, 0f, 400f, null, null), 0.001f, "basicunitspeed");
        var muddy = m;
        muddy.RoadCost = 4f;
        AssertNear(days * 2f, MarchTime.EstimateDays(muddy, f, 400f, 0f, 400f, null, null), 0.001f, "terrainspeccost[7]");
        f.UnitTyp = 14;
        var division = m;
        division.MountedBaseSpeed = (float[])m.MountedBaseSpeed.Clone();
        division.MountedBaseSpeed[14] = 4f;
        AssertNear(days / 2f, MarchTime.EstimateDays(division, f, 400f, 0f, 400f, null, null), 0.001f, "unitbasicspeed[t, 1] of the formation's type");
        AssertEqual(float.MaxValue, MarchTime.EstimateDays(default(MarchSpeedInputs), f, 400f, 0f, 400f, null, null), "no speed inputs: unknown");
    }

    private static void MarchTimeCreditsRiverCorridor()
    {
        var f = Corps(1, 0f, 20000f);
        f.UseRails = false;
        var m = Speed();
        float road = 400f / MarchTime.SpeedPerDay(m, 15, 1f, m.RoadCost);
        var everywhere = new RiverAtX(0f) { RiverEverywhere = true };
        AssertNear(road / 2f, MarchTime.EstimateDays(m, f, 400f, 0f, 400f, null, everywhere), 0.001f, "the whole way at the cost-1 river rate");
        AssertNear(road, MarchTime.EstimateDays(m, f, 400f, 0f, 400f, null, new RiverAtX(1000f)), 0.001f, "no river near: road");
        f.UseRivers = false;
        AssertNear(road, MarchTime.EstimateDays(m, f, 400f, 0f, 400f, null, everywhere), 0.001f, "a unit without userivers keeps the road");
    }

    private static void MarchTimeCreditsRailOnlyWithTrainCapacity()
    {
        var s = Line();
        s.Supply.Add(Iip(601, 0f, 0));
        s.Supply.Add(Iip(602, 400f, 0));
        s.Rails.Add(new RailLineRecord { Index = 0, Progress = 1f, AX = 0f, AZ = 0f, BX = 400f, BZ = 0f, AIipId = 601, BIipId = 602 });
        var f = Corps(1, 10f, 20000f);
        f.UseRivers = false;
        var m = Speed();
        float road = 390f / MarchTime.SpeedPerDay(m, 15, 1f, m.RoadCost);
        float rail = 390f / (MarchTime.SpeedPerDay(m, 15, 1f, m.RailCost) * m.RailroadSpeedAdj);

        m.TrainCapacityFree = 10f; // need 0.001 x 20000 / 1 / 4 x 1 = 5
        AssertTrue(MarchTime.TrainCovers(m, 20000f), "capacity covers the corps");
        AssertNear(rail, MarchTime.EstimateDays(m, f, 400f, 0f, 390f, s, null), 0.001f, "the line carries the corps");

        var short_ = m;
        short_.TrainCapacityFree = 2f;
        AssertNear(road, MarchTime.EstimateDays(short_, f, 400f, 0f, 390f, s, null), 0.001f, "no train capacity: road");
        var noRails = f;
        noRails.UseRails = false;
        AssertNear(road, MarchTime.EstimateDays(m, noRails, 400f, 0f, 390f, s, null), 0.001f, "a unit without userails keeps the road");

        var unfinished = Line();
        unfinished.Supply.Add(Iip(601, 0f, 0));
        unfinished.Supply.Add(Iip(602, 400f, 0));
        unfinished.Rails.Add(new RailLineRecord { Index = 0, Progress = 0.5f, AX = 0f, AZ = 0f, BX = 400f, BZ = 0f, AIipId = 601, BIipId = 602 });
        AssertNear(road, MarchTime.EstimateDays(m, f, 400f, 0f, 390f, unfinished, null), 0.001f, "an unfinished line carries nobody");

        var enemyEnd = Line();
        enemyEnd.Supply.Add(Iip(601, 0f, 0));
        enemyEnd.Supply.Add(Iip(602, 400f, 1));
        enemyEnd.Rails.Add(new RailLineRecord { Index = 0, Progress = 1f, AX = 0f, AZ = 0f, BX = 400f, BZ = 0f, AIipId = 601, BIipId = 602 });
        AssertNear(road, MarchTime.EstimateDays(m, f, 400f, 0f, 390f, enemyEnd, null), 0.001f, "a line into enemy hands is not own");
    }

    // ---- afloat enemies (G0 landing B) ----

    private static void AfloatEnemyIsThreatNotHoldableContact()
    {
        var afloat = Belief(600f, 0f, 10000f, 1f);
        afloat.Traits = EnemyTraits.Embarked;
        var s = Wide();
        s.Own.Add(Corps(3, 550f, 5000f));
        s.Beliefs.Add(afloat);
        var plan = PlanOnce(s);
        int farSector = Sectors(s).SectorAt(550f, 0f);
        // The sector faces enemy-held ground (objective 97), so it holds its D2 floor: the 10000 afloat army adds nothing to the need.
        AssertNear(ForceAllocator.MinGarrisonStrength, HoldNeedOf(plan, farSector), 0.5f, "an afloat army is no holdable contact: the hold need is the floor");

        var nearTarget = Line();
        nearTarget.Own.Add(Corps(1, 200f, 20000f));
        var boat = Belief(300f, 0f, 20000f, 1f);
        boat.Traits = EnemyTraits.Embarked;
        nearTarget.Beliefs.Add(boat);
        AssertNear(1f, BalanceAt(nearTarget, null, 99), 0f, "not in the land balance at the objective");

        var capital = TwoTargets(0.5f, 0.6f);
        var boatAtCapital = Belief(-100f, 0f, 10000f, 1f);
        boatAtCapital.Traits = EnemyTraits.Embarked;
        capital.Beliefs.Add(boatAtCapital);
        AssertTrue(Plan(capital, new PlannerState()).CapitalFloorStrength > 0f, "but a threat to the capital");

        var flank = Line();
        flank.Own.Add(Corps(1, 0f, 20000f));
        var boatOnFlank = Belief(150f, 50f, 20000f, 1f);
        boatOnFlank.Traits = EnemyTraits.Embarked;
        flank.Beliefs.Add(boatOnFlank);
        AssertTrue(Candidate(flank, null, 99).Exposure > 0f, "and an exposure on the axis");
    }

    // ---- pins ----

    private static void PlannerWeightsAndThresholdsPinned()
    {
        float sum = TheaterPlanner.ValueWeight + TheaterPlanner.BalanceWeight + TheaterPlanner.ReachWeight
            + TheaterPlanner.DistanceWeight + TheaterPlanner.ExposureWeight + TheaterPlanner.CapitalThreatWeight;
        AssertNear(1f, sum, 0.0001f, "live weights sum to 1 (P3)");
        AssertNear(0.30f, TheaterPlanner.ValueWeight, 0f, "value");
        AssertNear(0.25f, TheaterPlanner.BalanceWeight, 0f, "balance");
        AssertNear(0.15f, TheaterPlanner.ReachWeight, 0f, "reach");
        AssertNear(0.10f, TheaterPlanner.DistanceWeight, 0f, "distance");
        AssertNear(0.10f, TheaterPlanner.ExposureWeight, 0f, "exposure");
        AssertNear(0.10f, TheaterPlanner.CapitalThreatWeight, 0f, "capital");
        AssertNear(0.08f, TheaterPlanner.PriorLambda, 0f, "lambda");
        AssertNear(0.12f, TheaterPlanner.SwitchMargin, 0f, "margin");
        AssertEqual(3, TheaterPlanner.SwitchDays, "days");
        AssertNear(0.04f, TheaterPlanner.Epsilon, 0f, "epsilon");
        AssertNear(0.02f, TheaterPlanner.Temperature, 0f, "temperature");
        AssertNear(0.6f, TheaterPlanner.CapitalThreatTrigger, 0f, "capital trigger");
        AssertNear(0.6f, TheaterPlanner.HoldRatio, 0f, "hold ratio");
        AssertNear(0.4f, TheaterPlanner.RecoverSupplyBelow, 0f, "recover on raw supply");
    }

    private static void PlanSummaryNamesTheDecision()
    {
        var plan = PlanOnce(OneTarget(null));
        string line = TheaterPlanner.Summary(plan);
        AssertContains(line, "[CampaignPlan] alliance=0 main=", "tagged row");
        AssertContains(line, "/99 ratio=1.10", "main objective and ratio");
        AssertContains(line, "sectors=main:1,", "posture counts");
        AssertContains(line, "switch=initial prior=none depots=0", "decision fields");
    }

    private static void BattleInboxDeliversToBothSides()
    {
        var inbox = new BattleInbox();
        var record = Battle(1, new[] { 0, 1 }, 15, 6, 1861);
        inbox.Add(record);
        var taken = new List<BattleHistoryRecord>();
        inbox.Take(0, Day, taken);
        AssertEqual(1, taken.Count, "alliance 0 sees the battle");
        inbox.Take(0, Day, taken);
        AssertEqual(0, taken.Count, "taken once");
        inbox.Take(1, Day, taken);
        AssertEqual(1, taken.Count, "alliance 1 still sees it");
        inbox.Add(record);
        inbox.Take(2, Day, taken);
        AssertEqual(0, taken.Count, "no queue beyond alliance 1");
        for (int i = 0; i < BattleInbox.MaxPending + 5; i++) inbox.Add(record);
        inbox.Take(1, Day, taken);
        AssertEqual(BattleInbox.MaxPending, taken.Count, "queue bounded");
        inbox.Clear();
        inbox.Take(0, Day, taken);
        AssertEqual(0, taken.Count, "cleared");

        // CSA vs a European power: the Union did not fight it and never judges it.
        inbox.Add(Battle(2, new[] { 1, 2 }, 15, 6, 1861));
        inbox.Take(0, Day, taken);
        AssertEqual(0, taken.Count, "a battle without alliance 0 is not alliance 0's");
        inbox.Take(1, Day, taken);
        AssertEqual(1, taken.Count, "the CSA fought it");
    }

    // G3 verify F7: after a W&L pause the queue is not judged wholesale against today's main effort.
    private static void StaleBattlesAfterAPauseAreDropped()
    {
        var inbox = new BattleInbox();
        inbox.Add(Battle(1, new[] { 0, 1 }, 1, 5, 1861));   // 45 days before Day
        inbox.Add(Battle(1, new[] { 0, 1 }, 8, 6, 1861));   // exactly MaxBattleAgeDays before
        inbox.Add(Battle(1, new[] { 0, 1 }, 14, 6, 1861));
        var taken = new List<BattleHistoryRecord>();
        inbox.Take(0, Day, taken);
        AssertEqual(2, taken.Count, "only battles within MaxBattleAgeDays are judged");
        inbox.Take(0, Day, taken);
        AssertEqual(0, taken.Count, "the stale one is dropped, not kept for later");

        var state = StateOn(99);
        var plan = TheaterPlanner.Plan(TwoTargets(0.5f, 0.1f), Sectors(TwoTargets(0.5f, 0.1f)), null, state,
            Queued(Battle(1, new[] { 0, 1 }, 1, 5, 1861, 300f)), null, Speed(), out _);
        AssertEqual(99, plan.MainObjectiveId, "a stale decisive battle at the target triggers nothing");
    }

    private static void PlannerStateRestoreRoundTrips()
    {
        var state = new PlannerState();
        state.Restore(31, 32, 2, 7, 123456789L, 2, true);
        AssertEqual(31, state.CurrentMainObjective, "objective");
        AssertEqual(32, state.ChallengerObjective, "challenger");
        AssertEqual(2, state.ChallengerDays, "days");
        AssertEqual(7, state.DecisionSerial, "serial");
        AssertEqual(123456789L, state.CampaignSeed, "seed");
        AssertEqual(2, state.BelowOddsDays, "odds days");
        AssertTrue(state.CapitalThreatLatched, "capital latch");
    }

    // The runtime probe replicates CheckIfUnitIsWithinModifiedRange (80511-80517) and is bounded.
    private static void TerrainProbeMirrorsVanillaEngagementLine()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TerrainLineRuntime.cs");
        string probe = MethodBody(src, "public bool ProbeLine(");
        AssertContains(probe, "Tools.GetAngle(start, end) + 180f", "vanilla's angle convention");
        AssertContains(probe, ".CountTerrainLine(from, angle, steps, GameVars.terrainrestrictions_combat) < steps", "vanilla's < distance test, per segment");
        AssertContains(probe, "LineSegments.AlongLine(start.x, start.z, angle, walked, out float px, out float pz);", "a segment starts where vanilla's walk is after the steps walked");
        AssertContains(probe, "Remember(LineMemo, key, blocked);", "memoized for the map (static terrain grid)");
        // P01: over budget the question stays unknown (no probe, no memo write, return false).
        AssertContains(probe, "if (OverTime()) return Refused();", "CPU budget refusal");
        AssertContains(probe, "if (steps <= 0f) return Refused();", "step budget refusal");
        AssertContains(probe, "_millisPerStep, !ProbedThisReview(), MinSegmentSteps);", "the budget's first probe walks at least a minimum segment");
        AssertContains(MethodBody(src, "private static bool ProbedThisReview("), "return _lineSteps > 0f || _riverProbes > 0;", "first probe of the budget");
        // A line walked only partly keeps its offset and stays unknown.
        AssertContains(probe, "if (!blocked && walked + steps < distance)", "unfinished line");
        AssertContains(probe, "Partials[key] = walked + steps;\n                return false;", "offset kept, answer unknown");
        AssertTrue(probe.IndexOf("return Refused();", StringComparison.Ordinal) < probe.IndexOf("Remember(LineMemo,", StringComparison.Ordinal), "refused before anything is remembered");
        AssertTrue(probe.IndexOf("return false;", StringComparison.Ordinal) < probe.IndexOf("Remember(LineMemo,", StringComparison.Ordinal), "an unfinished line is not remembered");
        string refused = MethodBody(src, "private static bool Refused(");
        AssertContains(refused, "\"campaign-planner:terrain-probe-cap\"", "cap logged once with a static key");
        AssertContains(refused, "return false;", "a refused probe answers unknown");
        AssertContains(MethodBody(src, "public bool TryLine("), "LineMemo.TryGetValue(", "a memo lookup is free");
        string river = MethodBody(src, "public bool ProbeRiver(");
        AssertContains(river, "if (_riverProbes >= MaxRiverProbesPerReview || OverTime()) return Refused();", "river budget refusal");
        AssertContains(river, "BattlefieldSetup.GetCurrentTerrainOnPos(", "terrain lookup 26727");
        AssertContains(river, "Remember(RiverMemo,", "memoized");
        // P17: every review starts with a fresh budget.
        AssertContains(MethodBody(src, "internal static TerrainLineRuntime ForReview("), "NewBudget(MaxProbeMillisPerReview);", "each review starts a 1 ms budget");
        string review = MethodBody(src, "private static void NewBudget(");
        AssertContains(review, "_budgetMillis = millis;", "the budget's CPU time");
        AssertContains(review, "_lineSteps = 0f;", "line budget reset per review");
        AssertContains(review, "_riverProbes = 0;", "river budget reset per review");
        AssertContains(review, "_probeMillis = 0.0;", "CPU budget reset per review");
        AssertContains(MethodBody(src, "private static bool OverTime("), "_probeMillis > _budgetMillis", "only probing time is charged");
        AssertContains(probe, "_lineSteps, MaxLineStepsPerReview, _probeMillis, _budgetMillis,", "segments sized to the current budget");
        AssertContains(src, "internal const double MaxDrainMillisPerFrame = 0.9;", "drain frames keep a margin under 1 ms");
        AssertContains(src, "CampaignScope.Register(\"campaign-planner-terrain-probe\"", "per-campaign reset");
        // M2b: a CampaignScope reset keeps the map's memo; only a new map identity clears it.
        string reset = MethodBody(src, "private static void Reset(");
        // F3 (integration verify): the one exception is a load onto another scenario, dropped at the load.
        string guarded = "if (Drain.DropForScenario(SceneManagement.currentcampaignscenario)) ClearMap();";
        AssertContains(reset, guarded, "a load onto another scenario drops the old map's queue and memo");
        string rest = reset.Replace(guarded, "");
        AssertFalse(rest.Contains("LineMemo") || rest.Contains("RiverMemo") || rest.Contains("Partials") || rest.Contains("Drain.") || rest.Contains("ClearMap"), "otherwise the reset keeps the memo, partial lines and drain");
        string prepare = MethodBody(src, "internal static void PrepareDrain(");
        AssertContains(prepare, "TerrainMemoDrain.MapIdentity(SceneManagement.currentcampaignscenario, snapshot.Areas)", "map identity: scenario and area geometry");
        AssertContains(prepare, "if (mapChanged) ClearMap();", "cleared on a new map only");
        string clear = MethodBody(src, "private static void ClearMap(");
        AssertContains(clear, "LineMemo.Clear();", "line memo");
        AssertContains(clear, "RiverMemo.Clear();", "river memo");
        AssertContains(clear, "Partials.Clear();", "partial lines");
    }

    // The plan step: snapshot -> (bookkeeping return) -> plan -> publish; G2's sector profiles, no seam left.
    private static void PlannerPlanStepPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string review = MethodBody(src, "internal static void ReviewPlan(");
        int build = review.IndexOf("CampaignSnapshotRuntime.Build(", StringComparison.Ordinal);
        int bookkeeping = review.IndexOf("mode == PlannerReviewMode.Bookkeeping) return;", StringComparison.Ordinal);
        int plan = review.IndexOf("TheaterPlanner.Plan(", StringComparison.Ordinal);
        int publish = review.IndexOf("CampaignPlanStore.Publish(", StringComparison.Ordinal);
        AssertTrue(build >= 0 && bookkeeping > build && plan > bookkeeping && publish > plan, "build, bookkeeping return, plan, publish in order");
        AssertContains(review, "plan = TheaterPlanner.Plan(snapshot, sectors, id => CommanderProfiles.ForSector(snapshot.Commanders, id), state, PendingBattles,", "the plan step's inputs: G2's sector commander profiles");
        AssertContains(review, "CampaignPlanStore.Publish(alliance, plan,", "the planner's plan is what is published");
        AssertContains(review, "EmitPlan(plan, trace,", "[CampaignPlan] row from the published plan");
        AssertContains(review, "Battles.Take(alliance, snapshot.DaySerial,", "each review takes only its own, recent battles");
        int seams = src.Split(new[] { "// G2 SEAM" }, StringSplitOptions.None).Length - 1;
        AssertEqual(0, seams, "the G2 seam is gone");
        string forward = MethodBody(src, "internal static void OnBattleRecorded(");
        AssertContains(forward, "Battles.Add(record)", "battles queued for both sides");
    }

    private static void BattleForwardFollowsHistoryAdd()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        string record = MethodBody(src, "internal void RecordBattleOutcome(");
        int add = record.IndexOf("BattleHistory.Add(record);", StringComparison.Ordinal);
        int forward = record.IndexOf("CampaignPlannerRuntime.OnBattleRecorded(record);", StringComparison.Ordinal);
        AssertTrue(add >= 0 && forward > add, "the planner hears a battle right after BattleHistory.Add");
    }


    // ---- G3 verify r1 (F1-F5) ----

    // F1 / D1: odds hovering at RequiredRatio (1.111 / 1.087 against 1.1) never drop the main effort.
    private static void OddsDeadbandHoldsTheMainEffort()
    {
        var state = new PlannerState { CampaignSeed = 1234L };
        for (int day = 0; day < 6; day++)
        {
            var s = Line();
            s.Supply.Add(Iip(504, 400f, 0));
            s.Objectives.Add(Target(99, 300f, 1f));
            s.Own.Add(Corps(1, 200f, 20000f));
            s.Beliefs.Add(Belief(300f, 0f, day % 2 == 0 ? 18000f : 18400f, 1f));
            var plan = Plan(s, state);
            AssertEqual(99, plan.MainObjectiveId, "main effort kept, review " + day);
            AssertEqual(day == 0 ? "initial" : "hold", plan.SwitchReason, "no re-decision, review " + day);
            AssertEqual(1, state.DecisionSerial, "one decision only, review " + day);
        }
    }

    private static TheaterPlan OddsReview(PlannerState state, float enemyAt99)
    {
        var s = Line();
        s.Objectives.Add(Target(98, 100f, 0f));
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Beliefs.Add(Belief(300f, 0f, enemyAt99, 1f));
        return Plan(s, state);
    }

    private static void OddsDropBelowDeadbandOrAfterSwitchDays()
    {
        var sharp = new PlannerState { CampaignSeed = 7L };
        AssertEqual(99, OddsReview(sharp, 18000f).MainObjectiveId, "99 first");
        var dropped = OddsReview(sharp, 21000f); // 0.95 < 1.1 - 0.10
        AssertEqual(98, dropped.MainObjectiveId, "below the deadband: dropped at once");
        AssertEqual("odds", dropped.SwitchReason, "reason odds");

        var slow = new PlannerState { CampaignSeed = 7L };
        OddsReview(slow, 18000f);
        for (int day = 1; day < TheaterPlanner.SwitchDays; day++)
        {
            var kept = OddsReview(slow, 18400f); // 1.087: inside the deadband
            AssertEqual(99, kept.MainObjectiveId, "inside the deadband, review " + day);
            AssertEqual(day, slow.BelowOddsDays, "below-odds days counted");
        }
        var third = OddsReview(slow, 18400f);
        AssertEqual(98, third.MainObjectiveId, "dropped after SwitchDays below the required odds");
        AssertEqual("odds", third.SwitchReason, "reason odds");
        AssertEqual(0, slow.BelowOddsDays, "count reset with the new main effort");
    }

    // F2 / D2: under a standing capital threat the scores alternate by 0.053; one switch at onset.
    private static void StandingCapitalThreatDoesNotFlap()
    {
        var state = StateOn(99);
        int changes = 0, last = 99;
        for (int day = 0; day < 6; day++)
        {
            bool even = day % 2 == 0;
            var s = TwoTargets(even ? 0.9f : 0.1f, even ? 0.1f : 0.9f);
            s.Own.Add(Corps(2, 200f, 20000f));
            s.Beliefs.Add(Belief(-100f, 0f, 10000f, 1f));
            var plan = Plan(s, state);
            if (plan.MainObjectiveId != last) changes++;
            last = plan.MainObjectiveId;
            AssertEqual(day == 0 ? "capital-threat" : "hold", plan.SwitchReason, "trigger on onset only, review " + day);
        }
        AssertEqual(1, changes, "one switch in six reviews");
        AssertEqual(1, state.DecisionSerial, "one decision");
        AssertTrue(state.CapitalThreatLatched, "latched while the threat stands");
    }

    private static void CapitalLatchRearmsBelowRelease()
    {
        var state = StateOn(99);
        var threatened = TwoTargets(0.5f, 0.6f);
        threatened.Own.Add(Corps(2, 200f, 20000f));
        threatened.Beliefs.Add(Belief(-100f, 0f, 10000f, 1f));
        AssertEqual("capital-threat", Plan(threatened, state).SwitchReason, "onset");
        state.Restore(99, -1, 0, state.DecisionSerial, state.CampaignSeed, 0, state.CapitalThreatLatched);
        AssertEqual("hold", Plan(threatened, state).SwitchReason, "standing threat: no trigger");
        float floor = 10000f / (float)Math.Pow(100.0, 0.3) * 0.5f; // vanilla weighting (11735): 1256
        var easing = TwoTargets(0.5f, 0.6f);
        easing.Own.Add(Corps(2, 200f, 20000f));
        easing.Own.Add(Corps(3, 0f, floor / 0.5f)); // threat 0.5: below the trigger, above the release
        easing.Beliefs.Add(Belief(-100f, 0f, 10000f, 1f));
        Plan(easing, state);
        AssertTrue(state.CapitalThreatLatched, "0.5 keeps the latch: released only below 0.4");

        var calm = TwoTargets(0.5f, 0.6f);
        calm.Own.Add(Corps(2, 0f, 20000f)); // capital held: threat 0.06 < CapitalThreatRelease
        calm.Beliefs.Add(Belief(-100f, 0f, 10000f, 1f));
        Plan(calm, state);
        AssertFalse(state.CapitalThreatLatched, "released below CapitalThreatRelease");
        AssertEqual("capital-threat", Plan(threatened, state).SwitchReason, "a new onset triggers again");
    }

    // P02: a trigger keeps the current main effort while it is within Epsilon of the best (98 leads
    // by 0.029 at the capital-threat onset); no seed may switch it.
    private static void TriggerKeepsCurrentWithinEpsilon()
    {
        for (long seed = 1; seed <= 30; seed++)
        {
            var s = TwoTargets(0.5f, 0.75f);
            s.Own.Add(Corps(2, 200f, 20000f));
            s.Beliefs.Add(Belief(-100f, 0f, 10000f, 1f));
            var state = new PlannerState();
            state.Restore(99, -1, 0, 0, seed);
            var plan = Plan(s, state);
            AssertEqual(99, plan.MainObjectiveId, "current kept within Epsilon, seed " + seed);
            AssertEqual(0, state.DecisionSerial, "no decision, seed " + seed);
        }
    }

    // F4 / D3: two corps of 25000 together reach 1.25 x a believed 40000; G4 masses exactly them.
    private static void EligibilityCountsMassableStrength()
    {
        var s = Line();
        s.Supply.Add(Iip(504, 400f, 0));
        s.Objectives.Add(Target(99, 400f, 1f));
        s.Own.Add(Corps(1, 200f, 25000f));
        s.Own.Add(Corps(2, 100f, 25000f));
        var hungry = Corps(3, 300f, 25000f);
        hungry.Supply01 = 0.35f; // raw Supply01 below RecoverSupplyBelow: Recover, never massed
        s.Own.Add(hungry);
        s.Beliefs.Add(Belief(400f, 0f, 40000f, 1f));
        var plan = PlanOnce(s);
        AssertEqual(99, plan.MainObjectiveId, "massed odds 1.25 >= 1.10");
        AssertNear(50000f, plan.MassStrength, 0.01f, "mass strength published");
        AssertEqual("1,2", string.Join(",", plan.MassInstanceIds), "the massable set G4 assigns MainEffort to");
    }

    // Seven areas: 98 at x 100 (sector 0), 97 at x 500 (sector 2). Sector 2 faces a believed 10000 at
    // x 600 and holds it with corps 4; a map-group player corps and a corps in battle never mass.
    private static void MassExcludesHoldsBusyAndMapGroupPlayer()
    {
        var s = Line(0, 7);
        s.Supply.Add(Iip(505, 500f, 0));
        s.Objectives.Add(Target(98, 100f, 1f));
        s.Objectives.Add(Target(97, 500f, 0f));
        s.Own.Add(Corps(1, 200f, 20000f));
        var player = Corps(2, 150f, 20000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        var fighting = Corps(3, 200f, 20000f);
        fighting.Flags |= FormationFlags.InBattle;
        s.Own.Add(fighting);
        s.Own.Add(Corps(4, 550f, 8000f));
        s.Beliefs.Add(Belief(600f, 0f, 10000f, 1f));
        AssertNear(20000f, Candidate(s, null, 98).Mass, 0.01f, "sector 2's hold, the player's map group and the busy corps stay out");
        AssertNear(28000f, Candidate(s, null, 97).Mass, 0.01f, "sector 2's hold masses for sector 2's own target");
        var plan = PlanOnce(s);
        AssertEqual(98, plan.MainObjectiveId, "main effort 98");
        AssertEqual("1", string.Join(",", plan.MassInstanceIds), "only corps 1 is massed");
        AssertNear(6000f, HoldNeedOf(plan, Sectors(s).SectorAt(550f, 0f)), 0.01f, "HoldNeed published (0.6 x 10000)");
    }

    private static void MassOnlyWithinMarchHorizon()
    {
        var s = Line();
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        var far = Corps(2, 3300f, 20000f); // 3000 / 82.1 a day = 36.5 days > 30
        far.UseRails = false;
        s.Own.Add(far);
        AssertNear(20000f, Candidate(s, null, 99).Mass, 0.01f, "beyond MarchHorizonDays: not massable");
    }

    // F3 / D4: two mirror candidates whose defenders sit behind a river crossed only far away. With a
    // per-review probe budget the plan is the same whatever order the objectives come in, no
    // candidate gets a river discount the other lacks, and once the memo is complete both do.
    private static void RiverDiscountIsOrderIndependent()
    {
        var forward = new MemoRiverAtZ(60f, 1);
        var reversed = new MemoRiverAtZ(60f, 1);
        var stateA = new PlannerState { CampaignSeed = 1L };
        var stateB = new PlannerState { CampaignSeed = 1L };
        bool complete = false;
        for (int review = 0; review < 8 && !complete; review++)
        {
            forward.NewReview();
            reversed.NewReview();
            var a = TheaterPlanner.Plan(Mirror(false), Sectors(Mirror(false)), null, stateA, null, forward, Speed(), out string traceA);
            var b = TheaterPlanner.Plan(Mirror(true), Sectors(Mirror(true)), null, stateB, null, reversed, Speed(), out string traceB);
            AssertEqual(traceA, traceB, "same plan in both orders, review " + review);
            AssertEqual(a.MainObjectiveId, b.MainObjectiveId, "same main effort, review " + review);
            AssertEqual(forward.Known(), reversed.Known(), "the same lines probed first, review " + review);
            complete = traceA.EndsWith("terrain=complete");
            if (!complete) AssertEqual(-1, a.MainObjectiveId, "no partial discount: both at even odds, review " + review);
        }
        AssertTrue(complete, "the memo completes within a few reviews");
        var final = TheaterPlanner.Plan(Mirror(false), Sectors(Mirror(false)), null, new PlannerState { CampaignSeed = 1L }, null, forward, Speed(), out _);
        AssertTrue(final.MainObjectiveId == 98 || final.MainObjectiveId == 99, "with the memo complete both are discounted and eligible");
    }

    // The trace's unknown count is the number of distinct questions: a line asked from two points
    // of the same areas, or twice, is one question, and so is a river point.
    private static void ReviewTerrainRecordsEachUnknownOnce()
    {
        var areas = new List<AreaRecord>
        {
            new AreaRecord { Index = 0, X = 0f, Z = 0f, EdgeX = new[] { 0f }, EdgeZ = new[] { 0f } },
            new AreaRecord { Index = 1, X = 200f, Z = 0f, EdgeX = new[] { 200f }, EdgeZ = new[] { 0f } },
        };
        var source = new MemoRiverAtZ(60f, 0);
        var view = new ReviewTerrain(source, areas);
        view.Blocks(0f, 0f, 200f, 0f);
        view.Blocks(10f, 5f, 190f, -5f);
        view.Blocks(200f, 0f, 0f, 0f);
        AssertEqual(1, view.Unknown, "one line question per area pair");
        source.Budget = 1;
        source.NewReview();
        AssertEqual(1, view.ProbeUnknown(), "probed once");
        AssertTrue(source.TryLine(0f, 0f, 200f, 0f, out _), "known after the probe");
    }

    // Unknown questions are probed capital first, then candidates by ascending objective id, not in
    // the order they were asked.
    private static void ReviewTerrainProbesInFixedOrder()
    {
        var areas = new List<AreaRecord>
        {
            new AreaRecord { Index = 0, X = 0f, Z = 0f, EdgeX = new[] { 0f }, EdgeZ = new[] { 0f } },
            new AreaRecord { Index = 1, X = 200f, Z = 0f, EdgeX = new[] { 200f }, EdgeZ = new[] { 0f } },
            new AreaRecord { Index = 2, X = 0f, Z = 200f, EdgeX = new[] { 0f }, EdgeZ = new[] { 200f } },
        };
        var source = new MemoRiverAtZ(60f, 1);
        for (int review = 0; review < 3; review++)
        {
            source.NewReview();
            var view = new ReviewTerrain(source, areas);
            view.Mark(ReviewTerrain.GroupCandidate, 99, 200f, 0f);
            view.Blocks(0f, 0f, 200f, 0f);
            view.Mark(ReviewTerrain.GroupCandidate, 98, 0f, 200f);
            view.Blocks(0f, 0f, 0f, 200f);
            view.Mark(ReviewTerrain.GroupCapital, 0, 200f, 0f);
            view.Blocks(200f, 0f, 0f, 200f);
            view.ProbeUnknown();
        }
        AssertTrue(source.TryLine(200f, 0f, 0f, 200f, out _), "review 1: the capital's line");
        AssertTrue(source.TryLine(0f, 0f, 0f, 200f, out _), "review 2: objective 98's line");
        AssertTrue(source.TryLine(0f, 0f, 200f, 0f, out _), "review 3: objective 99's line");
        var fresh = new MemoRiverAtZ(60f, 1);
        fresh.NewReview();
        var first = new ReviewTerrain(fresh, areas);
        first.Mark(ReviewTerrain.GroupCandidate, 99, 200f, 0f);
        first.Blocks(0f, 0f, 200f, 0f);
        first.Mark(ReviewTerrain.GroupCandidate, 98, 0f, 200f);
        first.Blocks(0f, 0f, 0f, 200f);
        first.ProbeUnknown();
        AssertTrue(fresh.TryLine(0f, 0f, 0f, 200f, out _) && !fresh.TryLine(0f, 0f, 200f, 0f, out _), "98 before 99 although 99 asked first");
    }

    // The drain's set is every area-centre pair within MaxProbedLine and every area at every own
    // command range, probed in order: lines by area position, then river points by range.
    private static void DrainEnumeratesExactlyTheAreaPairSet()
    {
        var s = Line(0, 9); // areas at x 0..800, 100 apart: pairs up to 6 apart are within 600
        s.Own.Add(Corps(1, 100f, 1000f));
        s.Own.Add(Corps(2, 200f, 1000f));
        var third = Corps(3, 300f, 1000f);
        third.CommandRange = 60f;
        s.Own.Add(third);
        var expected = new List<string>();
        for (int a = 0; a < 9; a++)
            for (int b = a + 1; b < 9; b++)
                if (b - a <= 6) expected.Add("L" + a * 100f + "," + b * 100f);
        foreach (int range in new[] { 40, 60 })
            for (int a = 0; a < 9; a++) expected.Add("R" + a * 100f + "," + range);
        var drain = new TerrainMemoDrain();
        AssertTrue(drain.Prepare(MapA, s.Areas, s.Own, 1000, out _), "queued");
        AssertEqual(33, drain.Lines, "33 of the 36 pairs are within 600");
        AssertEqual(18, drain.Rivers, "9 areas x 2 command ranges");
        var source = new RecordingSource(int.MaxValue);
        source.NewFrame();
        AssertEqual(expected.Count, drain.Drain(source), "every question probed once");
        AssertEqual(string.Join(";", expected), string.Join(";", source.Probed), "exactly the set, in the fixed order");
        AssertEqual(0, drain.Remaining, "complete");
        AssertTrue(!drain.Prepare(MapA, s.Areas, s.Own, 1000, out _), "the same map and ranges queue nothing more");
        var tooMany = new TerrainMemoDrain();
        AssertTrue(!tooMany.Prepare(MapA, s.Areas, s.Own, 50, out _) && tooMany.Oversize && tooMany.Remaining == 0, "a set larger than the memo is not drained");
    }

    private static void DrainRespectsTheFrameBudget()
    {
        var s = Line(0, 9);
        s.Own.Add(Corps(1, 100f, 1000f));
        var drain = new TerrainMemoDrain();
        drain.Prepare(MapA, s.Areas, s.Own, 1000, out _);
        int total = drain.Remaining; // 33 lines + 9 river points
        var source = new RecordingSource(5);
        source.NewFrame();
        source.ProbeLine(0f, 0f, 100f, 0f); // a review already memoised one line
        int frames = 0, probed = 1;
        while (drain.Remaining > 0 && frames < 100)
        {
            source.NewFrame();
            int n = drain.Drain(source);
            AssertTrue(n <= 5, "at most the frame's budget, frame " + frames);
            probed += n;
            frames++;
        }
        AssertEqual(total, probed, "every question probed once; the memoised one skipped");
        AssertEqual((total - 1 + 4) / 5, frames, "completes in ceil((n - 1) / 5) frames");
    }

    // F3 with the drain: until the memo holds the set the reviews read clear; once drained the
    // review applies river terms, and a belief moving to other areas asks nothing new.
    private static void DrainCompletesThenReviewAppliesRiverTerms()
    {
        var source = new MemoRiverAtZ(60f, 0);
        var drain = new TerrainMemoDrain();
        var s = Mirror(false);
        drain.Prepare(MapA, s.Areas, s.Own, 1000, out _);
        source.NewReview();
        var before = TheaterPlanner.Plan(s, Sectors(s), null, new PlannerState { CampaignSeed = 1L }, null, source, Speed(), out string traceBefore);
        AssertTrue(traceBefore.Contains("terrain=filling"), "no budget: filling");
        AssertEqual(-1, before.MainObjectiveId, "no river term yet");
        int frames = 0;
        while (drain.Remaining > 0 && frames < 200)
        {
            source.Budget = 2;
            source.NewReview();
            drain.Drain(source);
            frames++;
        }
        AssertEqual(0, drain.Remaining, "drained");
        source.Budget = 0;
        source.NewReview();
        var after = TheaterPlanner.Plan(s, Sectors(s), null, new PlannerState { CampaignSeed = 1L }, null, source, Speed(), out string traceAfter);
        AssertTrue(traceAfter.EndsWith("terrain=complete"), "complete with no review budget");
        AssertTrue(after.MainObjectiveId == 98 || after.MainObjectiveId == 99, "river terms applied: eligible");
        var moved = Mirror(false);
        moved.Beliefs.Clear();
        moved.Beliefs.Add(Belief(400f, 0f, 20000f, 1f));
        moved.Beliefs.Add(Belief(300f, 140f, 20000f, 1f));
        TheaterPlanner.Plan(moved, Sectors(moved), null, new PlannerState { CampaignSeed = 1L }, null, source, Speed(), out string traceMoved);
        AssertTrue(traceMoved.EndsWith("terrain=complete"), "a moved belief asks no new question");
    }

    private static void DrainAppendsNewCommandRangesOnly()
    {
        var s = Line(0, 4);
        s.Own.Add(Corps(1, 100f, 1000f));
        var drain = new TerrainMemoDrain();
        drain.Prepare(MapA, s.Areas, s.Own, 1000, out _);
        var source = new RecordingSource(int.MaxValue);
        source.NewFrame();
        drain.Drain(source);
        var wide = Corps(2, 200f, 1000f);
        wide.CommandRange = 80f;
        s.Own.Add(wide);
        AssertTrue(drain.Prepare(MapA, s.Areas, s.Own, 1000, out _), "a new command range is queued");
        AssertEqual(4, drain.Remaining, "only its four river points");
        AssertEqual(8, drain.Rivers, "two ranges x four areas");
    }

    private const long MapA = 1L;

    // M2b: the set and the memo belong to the campaign map: preparing the same map again (after a
    // sidecar load or a battle) re-queues nothing; another scenario or area geometry starts over.
    private static void DrainMemoBelongsToTheMap()
    {
        var s = Line(0, 4);
        s.Own.Add(Corps(1, 100f, 1000f));
        long map = TerrainMemoDrain.MapIdentity("1861", s.Areas);
        AssertEqual(map, TerrainMemoDrain.MapIdentity("1861", Line(0, 4).Areas), "same scenario and areas: same map");
        AssertTrue(map != TerrainMemoDrain.MapIdentity("1862", s.Areas), "another scenario");
        var moved = Line(0, 4);
        var area = moved.Areas[2];
        area.X += 50f;
        moved.Areas[2] = area;
        AssertTrue(map != TerrainMemoDrain.MapIdentity("1861", moved.Areas), "another area geometry");
        AssertTrue(map != TerrainMemoDrain.MapIdentity("1861", Line(0, 5).Areas), "another area count");
        var drain = new TerrainMemoDrain();
        drain.Prepare(map, s.Areas, s.Own, 1000, out bool first);
        AssertTrue(first, "the first map is a change");
        var source = new RecordingSource(int.MaxValue);
        source.NewFrame();
        drain.Drain(source);
        AssertTrue(!drain.Prepare(map, s.Areas, s.Own, 1000, out bool again) && !again && drain.Remaining == 0, "same map after a reset: kept, nothing re-queued");
        AssertTrue(drain.Prepare(TerrainMemoDrain.MapIdentity("1862", s.Areas), s.Areas, s.Own, 1000, out bool other) && other, "another map starts over");
        AssertEqual(drain.Lines + drain.Rivers, drain.Remaining, "the whole set re-queued");
    }

    // M2a: a long line is walked in segments that keep each frame within its budget; the budget's
    // first segment is at least MinSegmentSteps however slow a step is measured.
    private static void SegmentsStayWithinTheFrameBudget()
    {
        const double millisPerStep = 0.003, frameMillis = 1.0;
        float distance = 600f, walked = 0f;
        int frames = 0;
        while (walked < distance && frames < 10)
        {
            float steps = LineSegments.Steps(distance - walked, 0f, 1500f, 0.0, frameMillis, millisPerStep, true, 32f);
            AssertTrue(steps * millisPerStep <= frameMillis + 1e-9, "frame " + frames + " within 1 ms: " + steps + " steps");
            walked += steps;
            frames++;
        }
        AssertEqual(600f, walked, "the whole line walked");
        AssertEqual(2, frames, "333 + 267 steps");
        AssertEqual(32f, LineSegments.Steps(600f, 0f, 1500f, 0.0, 1.0, 0.1, true, 32f), "first segment never below the minimum");
        AssertEqual(0f, LineSegments.Steps(600f, 0f, 1500f, 0.9, 1.0, 0.1, false, 32f), "after the first, nothing past the budget");
        AssertEqual(100f, LineSegments.Steps(600f, 1400f, 1500f, 0.0, 1.0, 0.001, false, 32f), "the step budget");
        AssertEqual(5.5f, LineSegments.Steps(5.5f, 0f, 1500f, 0.0, 1.0, 0.003, true, 32f), "the line's remainder");
    }

    // A segment starts where vanilla's one-step walk (GetAngleTerrainPosStatic, 26909-26912) is
    // after the steps walked, in the same cell.
    private static void SegmentStartIsVanillasWalk()
    {
        float sx = 1234.4f, sz = -567.8f, ex = 1500.2f, ez = -790.6f;
        float angle = (float)(Math.Atan2(ex - sx, ez - sz) / Math.PI * 180.0 - 180.0) + 180f; // Tools.GetAngle + 180
        float x = sx, z = sz;
        for (int k = 1; k <= 300; k++)
        {
            float r = (angle - 90f) * (float)Math.PI / 180f;
            x += (float)Math.Cos(r);
            z -= (float)Math.Sin(r);
            if (k % 50 != 0) continue;
            LineSegments.AlongLine(sx, sz, angle, k, out float px, out float pz);
            AssertTrue(Math.Abs(px - x) < 0.01f && Math.Abs(pz - z) < 0.01f, "step " + k + " same point");
        }
        LineSegments.AlongLine(sx, sz, angle, RiverLines.Distance(sx, sz, ex, ez), out float fx, out float fz);
        AssertTrue(Math.Abs(fx - ex) < 0.05f && Math.Abs(fz - ez) < 0.05f, "the walk heads to the end point");
    }

    // M1 / N08: the capital reserve never masses. Capital at x 0 facing a believed 10000 at x -100
    // (floor 1256); the 2000 guard at the capital is the reserve, so target 99 masses corps 1 only.
    private static void MassExcludesTheCapitalReserve()
    {
        var s = Line();
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 250f, 20000f));
        s.Own.Add(Corps(3, 0f, 2000f));
        s.Beliefs.Add(Belief(-100f, 0f, 10000f, 1f));
        var c = Candidate(s, null, 99);
        AssertNear(20000f, c.Mass, 0.01f, "the capital guard is not massable");
    }

    // The drain runs from the daily review's frame pump on the campaign map, is prepared by each review and reset
    // per campaign.
    private static void TerrainDrainWiringPinned()
    {
        // A client of the daily review's frame pump (fix/perf-hot-paths scheduler): it drains on a
        // campaign frame with no review day in flight, never from Plugin.Update.
        string plugin = ReadRepoFile("src/WhiskeyRealism/Plugin.cs");
        AssertFalse(MethodBody(plugin, "private void Update(").Contains("DrainTick"), "not a bare Plugin.Update tick");
        string coordinator = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        string pump = System.Text.RegularExpressions.Regex.Replace(MethodBody(coordinator, "internal void PumpDailyReview("), @"\s+", " ");
        AssertContains(pump, "if (!_review.InFlight) { if (_reviewFrames.MayDrain(Time.frameCount)) TerrainLineRuntime.DrainTick(); return; }", "drained by the review's frame pump between review days, on a frame without review work (F4)");
        AssertEqual(1, coordinator.Split(new[] { "TerrainLineRuntime.DrainTick();" }, StringSplitOptions.None).Length - 1, "one drain call site");
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string review = MethodBody(runtime, "internal static void ReviewPlan(");
        int prepare = review.IndexOf("TerrainLineRuntime.PrepareDrain(snapshot);", StringComparison.Ordinal);
        AssertTrue(prepare > review.IndexOf("CampaignSnapshotRuntime.Build(", StringComparison.Ordinal) && prepare < review.IndexOf("TheaterPlanner.Plan(", StringComparison.Ordinal), "prepared from each review's snapshot, before the plan");
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TerrainLineRuntime.cs");
        string tick = MethodBody(src, "internal static void DrainTick(");
        AssertContains(tick, "if (Drain.Remaining == 0) return;", "nothing to do costs nothing");
        AssertContains(tick, "TelemetryPerf.Scope(\"campaign.planner.terrain-drain\"", "perf scope");
        AssertContains(tick, "if ((UnityEngine.Object)ResolveBattlefieldSetup() == null) return;", "campaign map only");
        AssertContains(tick, "NewBudget(MaxDrainMillisPerFrame);", "a fresh 0.9 ms budget each frame");
        AssertContains(tick, "Drain.Drain(Instance);", "drains through the budgeted source");
        AssertContains(tick, "catch (Exception ex)", "never throws");
        AssertContains(MethodBody(src, "private static BattlefieldSetup ResolveBattlefieldSetup("), "!_bunits.IsCampaign", "a battle map's terrain is never probed");
        AssertContains(MethodBody(src, "internal static void PrepareDrain("), "Drain.Prepare(map, snapshot.Areas, snapshot.Own, MaxMemoEntries, out bool mapChanged)", "per map, bounded by the memo");
    }

    private static CampaignSnapshot Mirror(bool reversed)
    {
        var s = Line();
        s.HasCapital = false;
        var a = Target(98, 100f, 0.5f);
        var b = Target(99, 300f, 0.5f);
        if (reversed) { s.Objectives.Add(b); s.Objectives.Add(a); }
        else { s.Objectives.Add(a); s.Objectives.Add(b); }
        s.Own.Add(Corps(1, 200f, 20000f));
        // The defenders stand in areas of their own (terrain questions are asked between area centres).
        s.Areas.Add(new AreaRecord { Index = 20, X = 100f, Z = 140f, EdgeX = new[] { 100f }, EdgeZ = new[] { 140f }, Importance = 1f, SupplyCapacity = 100f });
        s.Areas.Add(new AreaRecord { Index = 21, X = 300f, Z = 140f, EdgeX = new[] { 300f }, EdgeZ = new[] { 140f }, Importance = 1f, SupplyCapacity = 100f });
        s.Beliefs.Add(Belief(100f, 140f, 20000f, 1f));
        s.Beliefs.Add(Belief(300f, 140f, 20000f, 1f));
        s.Crossings.Add(new CrossingRecord { X = 200f, Z = 1000f });
        return s;
    }

    // F5 / P25: a busy formation is never the candidate force, whatever keeps it busy.
    private static void BusyFormationNeverTheCandidateForce()
    {
        foreach (var busy in new[] { FormationFlags.InBattle, FormationFlags.OnRetreat, FormationFlags.Embarked, FormationFlags.VanillaDepotBuilder,
                                     FormationFlags.FortBuilder, FormationFlags.InTransfer, FormationFlags.RaidForce, FormationFlags.SeaInvasion })
        {
            var s = Line();
            s.Objectives.Add(Target(99, 300f, 1f));
            var near = Corps(1, 250f, 30000f);
            near.Flags |= busy;
            s.Own.Add(near);
            s.Own.Add(Corps(2, 0f, 15000f));
            var c = Candidate(s, null, 99);
            AssertEqual(1, c.Force, "the free corps is the force, not the one " + busy);
            AssertNear(15000f, c.Mass, 0.01f, busy + " formation never masses");
        }
    }

    // P24: the force is the nearest formation of at least MainBodyShare of the largest, not the nearest.
    private static void CandidateForceIsTheMainBody()
    {
        var s = Line();
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 290f, 5000f));
        AssertEqual(0, Candidate(s, null, 99).Force, "the main body, not the nearer small corps");
    }

    // P08 / P09: the draw's seed folds the decision serial and the alliance.
    private static void SeedFoldsDecisionSerialAndAlliance()
    {
        var bySerial = new HashSet<int>();
        for (int serial = 0; serial < 64; serial++)
        {
            var state = new PlannerState();
            state.Restore(-1, -1, 0, serial, 99L);
            bySerial.Add(Plan(TwoTargets(0.5f, 0.5f), state).MainObjectiveId);
        }
        AssertTrue(bySerial.Contains(98) && bySerial.Contains(99), "the decision serial changes the draw");

        bool differs = false;
        for (long seed = 1; seed <= 64 && !differs; seed++)
        {
            int zero = Plan(TwoTargets(0.5f, 0.5f), new PlannerState { CampaignSeed = seed }).MainObjectiveId;
            int one = Plan(AllianceOneTwoTargets(), new PlannerState { CampaignSeed = seed }).MainObjectiveId;
            differs = zero != one;
        }
        AssertTrue(differs, "the alliance changes the draw");
    }

    private static CampaignSnapshot AllianceOneTwoTargets()
    {
        var s = Line(1);
        var a = Target(98, 100f, 0.5f);
        var b = Target(99, 300f, 0.5f);
        a.HolderAlliance = b.HolderAlliance = 0;
        s.Objectives.Add(a);
        s.Objectives.Add(b);
        s.Own.Add(Corps(1, 200f, 20000f));
        return s;
    }

    // P10: two equal challengers above the margin: the lower objective id is counted, in either order.
    private static void EqualChallengersBreakTiesOnLowerObjectiveId()
    {
        foreach (bool reversed in new[] { false, true })
        {
            var s = Line();
            var low = Target(97, 0f, 0f);
            var a = Target(98, 100f, 0.5f);
            var b = Target(99, 300f, 0.5f);
            if (reversed) { s.Objectives.Add(b); s.Objectives.Add(a); s.Objectives.Add(low); }
            else { s.Objectives.Add(low); s.Objectives.Add(a); s.Objectives.Add(b); }
            s.Own.Add(Corps(1, 200f, 20000f));
            var state = StateOn(97);
            Plan(s, state);
            AssertEqual(98, state.ChallengerObjective, "lower id first, reversed=" + reversed);
        }
    }

    // P11: a sector with a believed army and no own formation is a Screen sector.
    private static void UnoccupiedThreatenedSectorIsScreen()
    {
        var s = Wide();
        s.Beliefs.Add(Belief(350f, 0f, 10000f, 1f));
        var plan = PlanOnce(s);
        AssertEqual(TheaterPosture.Screen, PostureOf(plan, Sectors(s).SectorAt(350f, 0f)), "unoccupied threatened sector screens");
    }

    // P13: average morale below aiminimummoraleformovement is Recover, supply full.
    private static void LowMoraleSectorRecovers()
    {
        AssertEqual(TheaterPosture.Recover, PostureOfFarSector(1f, 0.2f), "morale 0.2 < 0.3");
    }

    // P20: an unthreatened sector within relocation range of the main axis supports it.
    private static void SupportFollowsTheMainAxis()
    {
        // The far sector {300, 400} (centroid 350, 150 from the axis 100-200) holds an own objective (96) and faces no enemy-held
        // ground within its reach: Support. (A sector that does face enemy ground is Hold or Screen, D2.)
        var s = Line();
        s.Objectives.Add(Target(98, 100f, 1f));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 96, X = 300f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 0f });
        s.Own.Add(Corps(1, 200f, 20000f));   // the sector of the objective is released into the push (its own hold is the target's)
        s.Own.Add(Corps(2, 400f, 5000f));
        var plan = PlanOnce(s);
        AssertEqual(98, plan.MainObjectiveId, "main effort at x 100");
        AssertEqual(TheaterPosture.Support, PostureOf(plan, Sectors(s).SectorAt(400f, 0f)), "centroid 150 from the axis");
    }

    // P14: an army on the axis flank lowers the candidate's score.
    private static void ExposureLowersTheScore()
    {
        var open = Line();
        open.Own.Add(Corps(1, 0f, 20000f));
        float clear = Candidate(open, null, 99).Score;
        var flank = Line();
        flank.Own.Add(Corps(1, 0f, 20000f));
        var boat = Belief(150f, 50f, 20000f, 1f);
        boat.Traits = EnemyTraits.Embarked;
        flank.Beliefs.Add(boat);
        AssertTrue(Candidate(flank, null, 99).Score < clear, "exposure costs score");
    }

    // P05b: the march-time GroupSpeed is the formation's own (Regiment.groupspeed, 120438-120440).
    private static void SnapshotReadsGroupSpeed()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSnapshotRuntime.cs");
        AssertContains(src, "GroupSpeed = reg.groupspeed", "read from the regiment");
    }

    private static float HoldNeedOf(TheaterPlan plan, int sectorId)
    {
        foreach (var sp in plan.Sectors)
            if (sp.SectorId == sectorId) return sp.HoldNeed;
        throw new Exception("no plan for sector " + sectorId);
    }

    // ---- fixtures ----

    // A north-south river at x, every answer known (as a complete memo).
    private sealed class RiverAtX : ITerrainLineProbe, ITerrainLineSource
    {
        private readonly float _x;
        public bool RiverEverywhere;
        public RiverAtX(float x) { _x = x; }
        public bool Blocks(float ax, float az, float bx, float bz) => (ax - _x) * (bx - _x) < 0f;
        public bool RiverNear(float x, float z, float range) => RiverEverywhere || Math.Abs(x - _x) <= range;
        public bool TryLine(float ax, float az, float bx, float bz, out bool blocked) { blocked = Blocks(ax, az, bx, bz); return true; }
        public bool TryRiver(float x, float z, float range, out bool river) { river = RiverNear(x, z, range); return true; }
        public bool ProbeLine(float ax, float az, float bx, float bz) => true;
        public bool ProbeRiver(float x, float z, float range) => true;
    }

    // An east-west river at z behind a memo that starts empty and fills only by probing within a
    // per-review budget of questions (TerrainLineRuntime's shape).
    private sealed class MemoRiverAtZ : ITerrainLineSource
    {
        private readonly float _z;
        private readonly Dictionary<string, bool> _memo = new Dictionary<string, bool>();
        public int Budget, Left;
        public MemoRiverAtZ(float z, int budget) { _z = z; Budget = budget; }
        public void NewReview() => Left = Budget;
        public string Known()
        {
            var keys = new List<string>(_memo.Keys);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(";", keys);
        }
        private static string Key(float a, float b, float c, float d) => a + "," + b + "," + c + "," + d;
        public bool TryLine(float ax, float az, float bx, float bz, out bool blocked) => _memo.TryGetValue(Key(ax, az, bx, bz), out blocked);
        public bool TryRiver(float x, float z, float range, out bool river) { river = false; return true; }
        public bool ProbeLine(float ax, float az, float bx, float bz)
        {
            if (Left <= 0) return false;
            Left--;
            _memo[Key(ax, az, bx, bz)] = (az - _z) * (bz - _z) < 0f;
            return true;
        }
        public bool ProbeRiver(float x, float z, float range) => true;
    }

    // Memoises every probe (keys by exact coordinates), records the probe order, and refuses
    // probes past a per-frame budget.
    private sealed class RecordingSource : ITerrainLineSource
    {
        private readonly HashSet<string> _known = new HashSet<string>();
        private readonly int _budget;
        private int _left;
        public readonly List<string> Probed = new List<string>();
        public RecordingSource(int budget) { _budget = budget; }
        public void NewFrame() => _left = _budget;
        private static string Line(float ax, float bx) => "L" + ax + "," + bx;
        private static string River(float x, float range) => "R" + x + "," + range;
        public bool TryLine(float ax, float az, float bx, float bz, out bool blocked) { blocked = false; return _known.Contains(Line(ax, bx)); }
        public bool TryRiver(float x, float z, float range, out bool river) { river = false; return _known.Contains(River(x, range)); }
        public bool ProbeLine(float ax, float az, float bx, float bz) => Probe(Line(ax, bx));
        public bool ProbeRiver(float x, float z, float range) => Probe(River(x, range));
        private bool Probe(string key)
        {
            if (_left <= 0) return false;
            _left--;
            _known.Add(key);
            Probed.Add(key);
            return true;
        }
    }

    private static BattleHistoryRecord Battle(int won, int[] sides, int day, int month, int year, float x = 0f)
    {
        return new BattleHistoryRecord { LandOrSea = 0, BattleResultType = 1, AllianceWon = won, Alliance = sides, Day = day, Month = month, Year = year, PositionX = x };
    }

    private static List<BattleHistoryRecord> Queued(BattleHistoryRecord record)
    {
        var inbox = new BattleInbox();
        inbox.Add(record);
        var list = new List<BattleHistoryRecord>();
        inbox.Take(0, Day, list);
        return list;
    }

    private static CampaignSnapshot Fresh(int alliance)
    {
        var s = new CampaignSnapshot();
        s.Reset(alliance, alliance, Day, PlayerCommandClass.NotCareer, -1, null);
        return s;
    }

    // Areas every 100 on x from 0 (relocation range 150: each touches its neighbours), own capital at
    // x 0, own IIPs at x 100 and 300.
    private static CampaignSnapshot Line(int alliance = 0, int areas = 5, DirectorPosture tempo = null)
    {
        var s = new CampaignSnapshot();
        s.Reset(alliance, alliance, Day, PlayerCommandClass.NotCareer, -1, tempo);
        s.RelocationRange = 150f;
        s.OffensiveDominance = 1.1f;
        s.MinStrengthForMovement = 1000f;
        s.MinMoraleForMovement = 0.3f;
        s.HasCapital = true;
        for (int i = 0; i < areas; i++)
            s.Areas.Add(new AreaRecord { Index = 10 + i, X = i * 100f, Z = 0f, EdgeX = new[] { i * 100f }, EdgeZ = new[] { 0f }, Importance = 1f, SupplyCapacity = 100f });
        s.Supply.Add(Iip(501, 100f, alliance));
        s.Supply.Add(Iip(503, 300f, alliance));
        return s;
    }

    // Objectives 98 (x 100) and 99 (x 300), a corps between them at x 200.
    private static CampaignSnapshot TwoTargets(float importance98, float importance99)
    {
        var s = Line();
        s.Objectives.Add(Target(98, 100f, importance98));
        s.Objectives.Add(Target(99, 300f, importance99));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(8, 150f, 20000f));   // the garrison: a sector facing enemy-held ground keeps a hold of its own (D2), the nearer corps
        return s;
    }

    private static CampaignSnapshot OneTarget(DirectorPosture tempo)
    {
        var s = Line(0, 5, tempo);
        s.Objectives.Add(Target(99, 300f, 1f));
        s.Own.Add(Corps(1, 200f, 20000f));
        return s;
    }

    // Seven areas (x 0..600), objectives at 100 / 300 / 500: sectors {0,100,200}, {300,400}, {500,600}.
    private static CampaignSnapshot Wide()
    {
        var s = Line(0, 7);
        s.Supply.Add(Iip(505, 500f, 0));
        s.Objectives.Add(Target(98, 100f, 1f));
        s.Objectives.Add(Target(99, 300f, 0f));
        s.Objectives.Add(Target(97, 500f, 0f));
        s.Own.Add(Corps(1, 200f, 20000f));
        return s;
    }

    private static ObjectiveRecord Target(int id, float x, float importance)
    {
        return new ObjectiveRecord { ObjectiveId = id, X = x, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importance };
    }

    private static FormationRecord Corps(int id, float x, float strength)
    {
        return new FormationRecord
        {
            InstanceId = id, UnitKey = "corps" + id + ":" + id, UnitTyp = 15, X = x, Z = 0f, WaypointX = x, WaypointZ = 0f,
            Strength = strength, Morale = 1f, Supply01 = 1f, ReadinessStep = 3, CommandRange = 40f, BugleRange = 5f, CommanderId = id,
            AreaIndex = -1, Flags = FormationFlags.FightingForce, Name = "corps" + id, ParentInstanceId = -1,
            UseRails = true, UseRivers = true, GroupSpeed = 1f
        };
    }

    private static SupplySourceRecord Iip(int id, float x, int owner)
    {
        return new SupplySourceRecord { Id = id, Kind = SupplySourceKind.Iip, Owner = owner, X = x, Z = 0f, Condition = 1f, Built = true };
    }

    private static EnemyBelief Belief(float x, float z, float strength, float confidence)
    {
        return new EnemyBelief { InstanceId = -1, UnitKey = "enemy:1", UnitTyp = 15, X = x, Z = z, Strength = strength, Confidence01 = confidence, LastSeenDay = Day };
    }

    // unitbasicspeed[t, 1] = 2 for groups, basicunitspeed 1000 (unitprefs 853-854), factor 30
    // (campaignprefs 215-216), costs walk 4 / river 1 / road 2 / rail 1.
    private static MarchSpeedInputs Speed()
    {
        var mounted = new float[19];
        for (int t = 14; t <= 16; t++) mounted[t] = 2f;
        return new MarchSpeedInputs
        {
            MountedBaseSpeed = mounted, BasicUnitSpeed = 1000f, CampaignSpeedFactor = 30f,
            WalkCost = 4f, RiverCost = 1f, RoadCost = 2f, RailCost = 1f,
            RailroadSpeedAdj = 1f, TrainCapacityFree = 0f, TrainCapacityPerMan = 0.001f
        };
    }

    private static float RoadPerDay()
    {
        var m = Speed();
        return MarchTime.SpeedPerDay(m, 15, 1f, m.RoadCost);
    }

    private static SectorModel Sectors(CampaignSnapshot s) => SectorModel.Build(s);

    private static TheaterPlan PlanOnce(CampaignSnapshot s) => Plan(s, new PlannerState());

    private static TheaterPlan Plan(CampaignSnapshot s, PlannerState state)
    {
        return TheaterPlanner.Plan(s, Sectors(s), null, state, null, null, Speed(), out _);
    }

    private static PlannerState StateOn(int objective)
    {
        var state = new PlannerState();
        state.Restore(objective, -1, 0, 0, 1234L);
        return state;
    }

    private static TheaterPlanner.Candidate Candidate(CampaignSnapshot s, ITerrainLineProbe probe, int objectiveId)
    {
        if (s.Objectives.Count == 0) s.Objectives.Add(Target(objectiveId, 300f, 1f));
        var inputs = new TheaterPlanner.Inputs { S = s, Sectors = Sectors(s), Probe = probe, Speed = Speed() };
        var list = new List<TheaterPlanner.Candidate>();
        TheaterPlanner.Evaluate(inputs, list);
        foreach (var c in list)
            if (c.ObjectiveId == objectiveId) return c;
        throw new Exception("no candidate " + objectiveId);
    }

    private static float BalanceAt(CampaignSnapshot s, ITerrainLineProbe probe, int objectiveId)
    {
        if (s.Own.Count == 0) s.Own.Add(Corps(1, 200f, 20000f));
        return Candidate(s, probe, objectiveId).Balance;
    }

    private static SectorPlan SectorOf(TheaterPlan plan, TheaterPosture posture)
    {
        foreach (var sp in plan.Sectors)
            if (sp.Posture == posture) return sp;
        throw new Exception("no sector with posture " + posture);
    }

    private static TheaterPosture PostureOf(TheaterPlan plan, int sectorId)
    {
        foreach (var sp in plan.Sectors)
            if (sp.SectorId == sectorId) return sp.Posture;
        throw new Exception("no plan for sector " + sectorId);
    }

    // The far sector {300, 400} holds a formation at x 400 with the given raw supply; the main
    // effort is objective 98 at x 100.
    private static TheaterPosture PostureOfFarSector(float supply01, float morale = 1f)
    {
        var s = TwoTargets(1f, 0f);
        var tired = Corps(2, 400f, 5000f);
        tired.Supply01 = supply01;
        tired.Morale = morale;
        s.Own.Add(tired);
        var plan = PlanOnce(s);
        AssertEqual(98, plan.MainObjectiveId, "main effort at x 100");
        return PostureOf(plan, Sectors(s).SectorAt(400f, 0f));
    }

    // The sector {500, 600} (centroid 550) with own strength at x 550 and a believed land army.
    private static TheaterPosture PostureAt550(float own, float enemy, ITerrainLineSource probe, float enemyX = 600f, params CrossingRecord[] crossings)
    {
        var s = Wide();
        s.Own.Add(Corps(3, 550f, own));
        s.Beliefs.Add(Belief(enemyX, 0f, enemy, 1f));
        s.Crossings.AddRange(crossings);
        var plan = TheaterPlanner.Plan(s, Sectors(s), null, new PlannerState(), null, probe, Speed(), out _);
        return PostureOf(plan, Sectors(s).SectorAt(550f, 0f));
    }

    private static string MethodBody(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(start >= 0, "method present: " + signature);
        int open = src.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        throw new Exception("unbalanced body: " + signature);
    }
}
