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
            ("PriorTableMatchesCatalog", PriorTableMatchesCatalog),
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
        AssertNear(0.5f, HistoricalPriorTable.Weight(east, east.EndSerial + 93), 0.0001f, "three months after the window");
        AssertNear(0.25f, HistoricalPriorTable.Weight(east, east.EndSerial + 186), 0.0001f, "six months after");
        AssertNear(0.5f, HistoricalPriorTable.Weight(east, east.StartSerial - 93), 0.0001f, "three months before");
        AssertNear(1f, HistoricalPriorTable.Weight(east, east.EndSerial), 0.0001f, "the window's last day is inside");
    }

    private static void PriorTableMatchesCatalog()
    {
        AssertEqual(6, HistoricalPriorTable.Entries.Count, "six catalog profiles");
        foreach (var e in HistoricalPriorTable.Entries)
        {
            AssertTrue(HistoricalOperationCatalog.TryGetById(e.Id, out var p), "catalog has " + e.Id);
            AssertEqual(p.AllianceId, e.Alliance, e.Id + " alliance");
            AssertEqual(string.Join(",", p.ObjectiveAllowList), string.Join(",", e.ObjectiveIds), e.Id + " objectives");
            AssertEqual(ReviewCadenceCatchUp.DaySerial(1, p.StartMonth, p.StartYear), e.StartSerial, e.Id + " window start");
            AssertEqual(ReviewCadenceCatchUp.DaySerial(31, p.EndMonth, p.EndYear), e.EndSerial, e.Id + " window end");
            AssertEqual(HistoricalPriorTable.TierOf(p.Priority), e.Priority, e.Id + " tier");
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
        // 0.067: 98 now leads by 0.05, below SwitchMargin, but the trigger switches at once.
        var s = TwoTargets(0.5f, 0.6f);
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
        var decisive = new BattleHistoryRecord { LandOrSea = 0, BattleResultType = 1, AllianceWon = 1, PositionX = 300f, PositionZ = 0f };
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
        for (long seed = 1; seed <= 20; seed++)
        {
            var a = new PlannerState { CampaignSeed = seed };
            var b = new PlannerState { CampaignSeed = seed };
            AssertEqual(Plan(s, a).MainObjectiveId, Plan(s, b).MainObjectiveId, "same seed, same choice " + seed);
        }
    }

    private static void RandomOnlyWithinEpsilon()
    {
        // 98 and 99 tie; 97 (x 0, no value, out of reach) is far below the best.
        var s = TwoTargets(0.5f, 0.5f);
        s.Objectives.Add(Target(97, 0f, 0f));
        var seen = new HashSet<int>();
        for (long seed = 1; seed <= 200; seed++)
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

        Func<int, CommanderProfile> bold = id => new CommanderProfile { CommanderId = id, RequiredOddsShift = -0.25f };
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
        var posture = PostureOf(plan, Sectors(s).SectorAt(550f, 0f));
        AssertTrue(posture != TheaterPosture.Hold && posture != TheaterPosture.Screen, "an afloat army is no holdable contact");

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
        var record = new BattleHistoryRecord { LandOrSea = 0, BattleResultType = 1 };
        inbox.Add(record);
        var taken = new List<BattleHistoryRecord>();
        inbox.Take(0, taken);
        AssertEqual(1, taken.Count, "alliance 0 sees the battle");
        inbox.Take(0, taken);
        AssertEqual(0, taken.Count, "taken once");
        inbox.Take(1, taken);
        AssertEqual(1, taken.Count, "alliance 1 still sees it");
        inbox.Add(record);
        inbox.Take(2, taken);
        AssertEqual(0, taken.Count, "no queue beyond alliance 1");
        for (int i = 0; i < BattleInbox.MaxPending + 5; i++) inbox.Add(record);
        inbox.Take(1, taken);
        AssertEqual(BattleInbox.MaxPending, taken.Count, "queue bounded");
        inbox.Clear();
        inbox.Take(0, taken);
        AssertEqual(0, taken.Count, "cleared");
    }

    private static void PlannerStateRestoreRoundTrips()
    {
        var state = new PlannerState();
        state.Restore(4, 31, 32, 2, 7, 123456789L);
        AssertEqual(4, state.CurrentMainSector, "sector");
        AssertEqual(31, state.CurrentMainObjective, "objective");
        AssertEqual(32, state.ChallengerObjective, "challenger");
        AssertEqual(2, state.ChallengerDays, "days");
        AssertEqual(7, state.DecisionSerial, "serial");
        AssertEqual(123456789L, state.CampaignSeed, "seed");
    }

    // The runtime probe replicates CheckIfUnitIsWithinModifiedRange (80511-80517) and is bounded.
    private static void TerrainProbeMirrorsVanillaEngagementLine()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TerrainLineRuntime.cs");
        string blocks = MethodBody(src, "public bool Blocks(");
        AssertContains(blocks, "Tools.GetAngle(start, end) + 180f", "vanilla's angle convention");
        AssertContains(blocks, ".CountTerrainLine(start, angle, distance, GameVars.terrainrestrictions_combat) < distance", "vanilla's < distance test");
        AssertContains(blocks, "LineMemo", "memoized for the campaign (static terrain grid)");
        AssertContains(blocks, "MaxLineStepsPerReview", "step budget per review");
        AssertContains(blocks, "\"campaign-planner:terrain-probe-cap\"", "cap logged once with a static key");
        string river = MethodBody(src, "public bool RiverNear(");
        AssertContains(river, "BattlefieldSetup.GetCurrentTerrainOnPos(", "terrain lookup 26727");
        AssertContains(river, "RiverMemo", "memoized");
        AssertContains(src, "CampaignScope.Register(\"campaign-planner-terrain-probe\"", "per-campaign reset");
    }

    // The plan step: snapshot -> (bookkeeping return) -> plan -> publish; one G2 seam line.
    private static void PlannerPlanStepPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string review = MethodBody(src, "internal static void ReviewPlan(");
        int build = review.IndexOf("CampaignSnapshotRuntime.Build(", StringComparison.Ordinal);
        int bookkeeping = review.IndexOf("mode == PlannerReviewMode.Bookkeeping) return;", StringComparison.Ordinal);
        int plan = review.IndexOf("TheaterPlanner.Plan(", StringComparison.Ordinal);
        int publish = review.IndexOf("CampaignPlanStore.Publish(", StringComparison.Ordinal);
        AssertTrue(build >= 0 && bookkeeping > build && plan > bookkeeping && publish > plan, "build, bookkeeping return, plan, publish in order");
        AssertContains(review, "Battles.Take(alliance,", "each review takes only its own battles");
        int seams = src.Split(new[] { "// G2 SEAM" }, StringSplitOptions.None).Length - 1;
        AssertEqual(1, seams, "exactly one G2 seam line");
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

    // ---- fixtures ----

    private sealed class RiverAtX : ITerrainLineProbe
    {
        private readonly float _x;
        public bool RiverEverywhere;
        public RiverAtX(float x) { _x = x; }
        public bool Blocks(float ax, float az, float bx, float bz) => (ax - _x) * (bx - _x) < 0f;
        public bool RiverNear(float x, float z, float range) => RiverEverywhere || Math.Abs(x - _x) <= range;
    }

    private static CampaignSnapshot Fresh(int alliance)
    {
        var s = new CampaignSnapshot();
        s.Reset(alliance, alliance, Day, PlayerCommandClass.NotCareer, -1, true, null);
        return s;
    }

    // Areas every 100 on x from 0 (relocation range 150: each touches its neighbours), own capital at
    // x 0, own IIPs at x 100 and 300.
    private static CampaignSnapshot Line(int alliance = 0, int areas = 5, DirectorPosture tempo = null)
    {
        var s = new CampaignSnapshot();
        s.Reset(alliance, alliance, Day, PlayerCommandClass.NotCareer, -1, true, tempo);
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
        return new SupplySourceRecord { Id = id, Kind = SupplySourceKind.Iip, Owner = owner, X = x, Z = 0f, TransportCapacity = 10f, Condition = 1f, Built = true };
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
        state.Restore(0, objective, -1, 0, 0, 1234L);
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
    private static TheaterPosture PostureOfFarSector(float supply01)
    {
        var s = TwoTargets(1f, 0f);
        var tired = Corps(2, 400f, 5000f);
        tired.Supply01 = supply01;
        s.Own.Add(tired);
        var plan = PlanOnce(s);
        AssertEqual(98, plan.MainObjectiveId, "main effort at x 100");
        return PostureOf(plan, Sectors(s).SectorAt(400f, 0f));
    }

    // The sector {500, 600} (centroid 550) with own strength at x 550 and a believed land army.
    private static TheaterPosture PostureAt550(float own, float enemy, ITerrainLineProbe probe, float enemyX = 600f, params CrossingRecord[] crossings)
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
