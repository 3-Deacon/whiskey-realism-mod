using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G slice G0 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G0): the planner
// contracts, the snapshot, the pure primitives (sight radius, beliefs query, viewer coverage,
// sectors, logistics reach), the W&L player classifier and pause predicates, and the plan store.
internal static class CampaignSnapshotTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("CampaignSightIgnoresPerkEightAndSixteen", CampaignSightIgnoresPerkEightAndSixteen),
            ("SightRadiusFlagOffKeepsPerkEightFactor", SightRadiusFlagOffKeepsPerkEightFactor),
            ("ClassifierNotCareerWhenScenarioInactive", ClassifierNotCareerWhenScenarioInactive),
            ("ClassifierCicBeatsEverything", ClassifierCicBeatsEverything),
            ("ClassifierNoCommandOnHierarchyMinusOne", ClassifierNoCommandOnHierarchyMinusOne),
            ("ClassifierArmyGroupCommanderWithoutCommandIsNotPaused", ClassifierArmyGroupCommanderWithoutCommandIsNotPaused),
            ("ClassifierMapGroupWhenCommandIsItsOwnCampaignGroup", ClassifierMapGroupWhenCommandIsItsOwnCampaignGroup),
            ("ClassifierBelowMapLevelForBrigadeInCorps", ClassifierBelowMapLevelForBrigadeInCorps),
            ("StandsDownOnlyForCicPlayersOwnAlliance", StandsDownOnlyForCicPlayersOwnAlliance),
            ("AllAlliancesExecutorsPauseWithVanillaJobMachine", AllAlliancesExecutorsPauseWithVanillaJobMachine),
            ("CareerNotStartedOnlyWithoutChosenCommander", CareerNotStartedOnlyWithoutChosenCommander),
            ("VanillaJobMachinePausedMatches11368", VanillaJobMachinePausedMatches11368),
            ("IndexOfOwnMinusOneForUnknown", IndexOfOwnMinusOneForUnknown),
            ("IndexOfKeyFindsByUnitKey", IndexOfKeyFindsByUnitKey),
            ("SnapshotListsResetOnReuse", SnapshotListsResetOnReuse),
            ("AllocationTryGetFindsAssignment", AllocationTryGetFindsAssignment),
            ("PlanStorePublishReplacesAndResetClears", PlanStorePublishReplacesAndResetClears),
            ("PlanStoreReaderOutsideReviewSeesCommittedPlan", PlanStoreReaderOutsideReviewSeesCommittedPlan),
            ("ViewerCoverageUsesEachViewersOwnRadius", ViewerCoverageUsesEachViewersOwnRadius),
            ("CoversFalseWithoutViewerInRadius", CoversFalseWithoutViewerInRadius),
            ("StrengthWithinIsConfidenceWeighted", StrengthWithinIsConfidenceWeighted),
            ("NearestIgnoresExpired", NearestIgnoresExpired),
            ("SectorsDeterministicAcrossRebuilds", SectorsDeterministicAcrossRebuilds),
            ("SectorTheaterFromCentroid", SectorTheaterFromCentroid),
            ("SectorAdjacencyUsesZoneEdges", SectorAdjacencyUsesZoneEdges),
            ("ReachTrueWithOwnIipInCommandRange", ReachTrueWithOwnIipInCommandRange),
            ("EnemyOwnedIipDoesNotSupply", EnemyOwnedIipDoesNotSupply),
            ("ReachNeedsBuiltDepotAndZoneStackingRoom", ReachNeedsBuiltDepotAndZoneStackingRoom),
            ("ClampToReachPullsBackToLastInReachPoint", ClampToReachPullsBackToLastInReachPoint),
            ("ClampToReachFalseWhenNothingInReach", ClampToReachFalseWhenNothingInReach),
            ("AreaRecordCarriesNoEnemyAggregate", AreaRecordCarriesNoEnemyAggregate),
            ("SupplyMetricIsRawNotVanillaHalved", SupplyMetricIsRawNotVanillaHalved),
            ("EmbarkedMatchesVanillaAfloatTest", EmbarkedMatchesVanillaAfloatTest),
            ("FightingForceMirrorsVanillaWithoutRecompute", FightingForceMirrorsVanillaWithoutRecompute),
            ("CavalryShareFromDirectStrengthPerType", CavalryShareFromDirectStrengthPerType),
            ("ObjectiveRolesFromVanillaFacts", ObjectiveRolesFromVanillaFacts),
            ("ContractEnumValuesPinned", ContractEnumValuesPinned),
            ("ContractRecordsCarryCensusFields", ContractRecordsCarryCensusFields),
            ("ImportanceMirrorsVanillaTerms", ImportanceMirrorsVanillaTerms),
            ("SectorAdjacencyCacheFollowsGeometry", SectorAdjacencyCacheFollowsGeometry),
            ("CoordinatorPauseGuardsOrderIssuingSteps", CoordinatorPauseGuardsOrderIssuingSteps),
            ("CoordinatorPauseGatesAreTheVanillaPredicates", CoordinatorPauseGatesAreTheVanillaPredicates),
            ("PlannerReviewGatesPinned", PlannerReviewGatesPinned),
            ("StaticSourceKeyRebuildsOnNewListOrCount", StaticSourceKeyRebuildsOnNewListOrCount),
            ("RailGeometryMatchesDirectScan", RailGeometryMatchesDirectScan),
            ("RailGeometryMemoFollowsRadiusAndRebuild", RailGeometryMemoFollowsRadiusAndRebuild),
            ("SnapshotStaticFactsReadOncePerCampaign", SnapshotStaticFactsReadOncePerCampaign),
        });
    }

    // ---- SightRadiusFormula (vanilla FogOfWar.UpdateViewRange 101296-101309) ----

    private static void CampaignSightIgnoresPerkEightAndSixteen()
    {
        // Campaign viewers are top units (unittyp 14-16, 18): with the perk-id fix no perk
        // changes their sight. Perk 16 (campaign Expert Scouts) has no term in vanilla at all.
        foreach (int unitTyp in new[] { 14, 15, 16, 18 })
        {
            float withEight = SightRadiusFormula.Campaign(unitTyp, 50f, true, 1.5f, 0.2f, 0.5f, false, 0.5f, 1f, true);
            float without = SightRadiusFormula.Campaign(unitTyp, 50f, false, 1.5f, 0.2f, 0.5f, false, 0.5f, 1f, true);
            AssertNear(45f, withEight, 0.001f, "campaign sight ignores perk 8, unittyp " + unitTyp);
            AssertNear(without, withEight, 0.001f, "perk 8 changes nothing, unittyp " + unitTyp);
        }

        // Weather, lie-down and W&L incident 63 apply as in 101302-101309.
        AssertNear(45f * 0.5f, SightRadiusFormula.Campaign(15, 50f, false, 1.5f, 0.2f, 0.5f, true, 0.5f, 1f, true), 0.001f, "lie-down factor");
        AssertNear(45f * 2f, SightRadiusFormula.Campaign(15, 50f, false, 1.5f, 0.2f, 0.5f, false, 0.5f, 2f, true), 0.001f, "incident 63 factor");
        // A battle-table unit (unittyp <= 13) with Expert Scouts keeps the factor.
        AssertNear(67.5f, SightRadiusFormula.Campaign(13, 50f, true, 1.5f, 0.2f, 0.5f, false, 0.5f, 1f, true), 0.001f, "battle unit keeps expert scouts");
        AssertNear(0f, SightRadiusFormula.Campaign(15, 50f, false, 1f, 1f, 2f, false, 0.5f, 1f, true), 0.001f, "radius never negative");
    }

    private static void SightRadiusFlagOffKeepsPerkEightFactor()
    {
        // Fix Perk Id Mismatches off: vanilla's unfixed radius, the factor on any HasPerk(8).
        AssertNear(67.5f, SightRadiusFormula.Campaign(15, 50f, true, 1.5f, 0.2f, 0.5f, false, 0.5f, 1f, false), 0.001f, "flag off corps with id 8");
        AssertNear(45f, SightRadiusFormula.Campaign(15, 50f, false, 1.5f, 0.2f, 0.5f, false, 0.5f, 1f, false), 0.001f, "flag off without id 8");
    }

    // ---- PlayerCommandClassifier ----

    private static void ClassifierNotCareerWhenScenarioInactive()
    {
        AssertEqual(PlayerCommandClass.NotCareer, PlayerCommandClassifier.Classify(false, 5, false, 15, true, true), "not a career");
        AssertEqual(PlayerCommandClass.NotCareer, PlayerCommandClassifier.Classify(false, -1, false, -1, false, false), "not a career, no commander");
        AssertFalse(PlayerCommandClassifier.ExecutorsPaused(PlayerCommandClass.NotCareer), "no pause outside a career");
    }

    private static void ClassifierCicBeatsEverything()
    {
        AssertEqual(PlayerCommandClass.CommanderInChief, PlayerCommandClassifier.Classify(true, 5, true, 18, false, false), "CIC without command");
        AssertEqual(PlayerCommandClass.CommanderInChief, PlayerCommandClassifier.Classify(true, 5, true, 18, true, true), "CIC with command");
        AssertEqual(PlayerCommandClass.CommanderInChief, PlayerCommandClassifier.Classify(true, 5, true, -1, false, false), "CIC with hierarchy -1");
    }

    private static void ClassifierNoCommandOnHierarchyMinusOne()
    {
        AssertEqual(PlayerCommandClass.NoCommand, PlayerCommandClassifier.Classify(true, 5, false, -1, false, false), "between commands");
        AssertEqual(PlayerCommandClass.NoCommand, PlayerCommandClassifier.Classify(true, -1, false, -1, false, false), "no chosen commander");
        AssertTrue(PlayerCommandClassifier.ExecutorsPaused(PlayerCommandClass.NoCommand), "no command pauses");
    }

    private static void ClassifierArmyGroupCommanderWithoutCommandIsNotPaused()
    {
        var c = PlayerCommandClassifier.Classify(true, 5, false, 18, false, false);
        AssertEqual(PlayerCommandClass.ArmyGroupCommander, c, "army group commander without currentcommand");
        AssertFalse(PlayerCommandClassifier.ExecutorsPaused(c), "army group commander does not pause");
        AssertFalse(PlayerCommandClassifier.PlannerStandsDown(c, 0, 0), "army group commander does not stand down");
    }

    private static void ClassifierMapGroupWhenCommandIsItsOwnCampaignGroup()
    {
        // A top-unit division (hierarchy 14) whose campaign group is itself (8271 flag2).
        AssertEqual(PlayerCommandClass.MapGroup, PlayerCommandClassifier.Classify(true, 5, false, 14, true, true), "top-unit division");
        AssertEqual(PlayerCommandClass.MapGroup, PlayerCommandClassifier.Classify(true, 5, false, 15, true, true), "corps");
        AssertEqual(PlayerCommandClass.MapGroup, PlayerCommandClassifier.Classify(true, 5, false, 18, true, true), "army-group commander with a map-group command");
    }

    private static void ClassifierBelowMapLevelForBrigadeInCorps()
    {
        AssertEqual(PlayerCommandClass.BelowMapLevel, PlayerCommandClassifier.Classify(true, 5, false, 13, true, false), "brigade in a corps");
        AssertEqual(PlayerCommandClass.BelowMapLevel, PlayerCommandClassifier.Classify(true, 5, false, 14, true, false), "non-top division");
    }

    private static void StandsDownOnlyForCicPlayersOwnAlliance()
    {
        AssertTrue(PlayerCommandClassifier.PlannerStandsDown(PlayerCommandClass.CommanderInChief, 1, 1), "CIC own alliance");
        AssertFalse(PlayerCommandClassifier.PlannerStandsDown(PlayerCommandClass.CommanderInChief, 1, 0), "CIC enemy alliance");
        foreach (PlayerCommandClass c in Enum.GetValues(typeof(PlayerCommandClass)))
        {
            if (c == PlayerCommandClass.CommanderInChief) continue;
            AssertFalse(PlayerCommandClassifier.PlannerStandsDown(c, 1, 1), "no stand-down for " + c);
        }
        AssertEqual(PlannerReviewMode.StandDown, PlayerCommandClassifier.ReviewMode(PlayerCommandClass.CommanderInChief, 1, 1), "CIC review mode own alliance");
        AssertEqual(PlannerReviewMode.Full, PlayerCommandClassifier.ReviewMode(PlayerCommandClass.CommanderInChief, 1, 0), "CIC review mode enemy alliance");
    }

    private static void AllAlliancesExecutorsPauseWithVanillaJobMachine()
    {
        // Vanilla's return at 11368 freezes every faction's job machine: the executors of both
        // alliances pause, and the review keeps its bookkeeping (snapshot, beliefs, aging).
        var paused = PlayerCommandClassifier.Classify(true, 5, false, -1, false, false);
        for (int alliance = 0; alliance < 2; alliance++)
        {
            AssertEqual(PlannerReviewMode.Bookkeeping, PlayerCommandClassifier.ReviewMode(paused, 0, alliance), "bookkeeping alliance " + alliance);
            AssertEqual(PlannerReviewMode.Bookkeeping, PlayerCommandClassifier.ReviewMode(paused, 1, alliance), "bookkeeping (player 1) alliance " + alliance);
        }
        AssertTrue(PlayerCommandClassifier.ExecutorsPaused(paused), "executors paused");

        var running = PlayerCommandClassifier.Classify(true, 5, false, 15, true, true);
        for (int alliance = 0; alliance < 2; alliance++)
            AssertEqual(PlannerReviewMode.Full, PlayerCommandClassifier.ReviewMode(running, 0, alliance), "full alliance " + alliance);
        AssertFalse(PlayerCommandClassifier.ExecutorsPaused(running), "executors run");
    }

    // ---- WlCareerStartGate (spec C5) ----

    private static void CareerNotStartedOnlyWithoutChosenCommander()
    {
        AssertTrue(WlCareerStartGate.CareerNotStarted(true, -1), "career, no commander chosen");
        AssertFalse(WlCareerStartGate.CareerNotStarted(true, 7), "career, commander chosen (even between commands)");
        AssertFalse(WlCareerStartGate.CareerNotStarted(false, -1), "not a career");
    }

    private static void VanillaJobMachinePausedMatches11368()
    {
        // 11368: dlc_scenarioactive && GetPlayerCommandHierarchy() == -1 && !IsCommanderInChief().
        AssertTrue(WlCareerStartGate.VanillaJobMachinePaused(true, -1, false), "between commands");
        AssertFalse(WlCareerStartGate.VanillaJobMachinePaused(true, -1, true), "CIC never pauses");
        AssertFalse(WlCareerStartGate.VanillaJobMachinePaused(true, 18, false), "army-group player without currentcommand (hierarchy 18)");
        AssertFalse(WlCareerStartGate.VanillaJobMachinePaused(true, 15, false), "corps player");
        AssertFalse(WlCareerStartGate.VanillaJobMachinePaused(false, -1, false), "not a career");

        // The classifier's pause and the coordinator's predicate agree on every vanilla state.
        foreach (bool active in new[] { false, true })
        foreach (bool cic in new[] { false, true })
        foreach (int hierarchy in new[] { -1, 10, 13, 14, 15, 16, 18 })
        foreach (bool hasCommand in new[] { false, true })
        {
            var c = PlayerCommandClassifier.Classify(active, 5, cic, hierarchy, hasCommand, false);
            AssertEqual(WlCareerStartGate.VanillaJobMachinePaused(active, hierarchy, cic), PlayerCommandClassifier.ExecutorsPaused(c),
                "pause agreement active=" + active + " cic=" + cic + " h=" + hierarchy + " cmd=" + hasCommand);
        }
    }

    // ---- CampaignSnapshot ----

    private static void IndexOfOwnMinusOneForUnknown()
    {
        var s = NewSnapshot(0);
        s.Own.Add(Formation(101, "1st Corps:12", 0f, 0f, 10000f));
        s.Own.Add(Formation(102, "2nd Corps:13", 10f, 0f, 9000f));
        AssertEqual(1, s.IndexOfOwn(102), "known instance");
        AssertEqual(-1, s.IndexOfOwn(999), "unknown instance");
    }

    private static void IndexOfKeyFindsByUnitKey()
    {
        var s = NewSnapshot(0);
        s.Own.Add(Formation(101, "1st Corps:12", 0f, 0f, 10000f));
        s.Own.Add(Formation(102, "2nd Corps:13", 10f, 0f, 9000f));
        AssertEqual(1, s.IndexOfKey("2nd Corps:13"), "known key");
        AssertEqual(-1, s.IndexOfKey("2nd Corps:99"), "unknown key");
        AssertEqual(-1, s.IndexOfKey(null), "null key");
        AssertEqual("1st Corps:12", SnapshotRules.UnitKey("1st Corps", 12), "unit key convention name:commander");
    }

    private static void SnapshotListsResetOnReuse()
    {
        var s = NewSnapshot(1);
        s.Own.Add(Formation(101, "a:1", 0f, 0f, 1f));
        s.Areas.Add(Area(0, 0f, 0f, 5f));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 3 });
        s.Supply.Add(new SupplySourceRecord { Id = 4 });
        s.Rails.Add(new RailLineRecord { Index = 5 });
        s.Commanders.Add(new CommanderRecord { Id = 6 });
        s.RankBands.Add(new RankBand { UnitTyp = 15 });
        s.Crossings.Add(new CrossingRecord { X = 1f, Z = 2f });
        s.Detections.Add(new EnemyDetection { InstanceId = 7 });
        s.Beliefs.Add(new EnemyBelief { InstanceId = 8 });
        s.CapitalX = 5f;
        s.HasCapital = true;
        s.RelocationRange = 100f;
        var own = s.Own;

        var tempo = new DirectorPosture();
        s.Reset(0, 2, 77, PlayerCommandClass.MapGroup, 55, tempo);
        AssertEqual(0, s.Alliance, "alliance");
        AssertEqual(2, s.FactionIndex, "faction index");
        AssertEqual(77, s.DaySerial, "day serial");
        AssertEqual(PlayerCommandClass.MapGroup, s.Player, "player class");
        AssertEqual(55, s.PlayerCommandInstanceId, "player command");
        AssertTrue(ReferenceEquals(tempo, s.Tempo), "tempo");
        AssertTrue(ReferenceEquals(own, s.Own), "lists are pooled");
        AssertEqual(0, s.Own.Count + s.Areas.Count + s.Objectives.Count + s.Supply.Count + s.Rails.Count
            + s.Commanders.Count + s.RankBands.Count + s.Crossings.Count + s.Detections.Count + s.Beliefs.Count, "every list cleared");
        AssertEqual(0f, s.CapitalX, "capital x cleared");
        AssertFalse(s.HasCapital, "capital flag cleared");
        AssertEqual(0f, s.RelocationRange, "scalars cleared");
    }

    private static void AllocationTryGetFindsAssignment()
    {
        var a = new Allocation { Alliance = 1 };
        a.Assignments.Add(new Assignment { InstanceId = 11, Role = FormationRole.Hold });
        a.Assignments.Add(new Assignment { InstanceId = 12, Role = FormationRole.MainEffort, Withheld = false });
        AssertTrue(a.TryGet(12, out var found), "found");
        AssertEqual(FormationRole.MainEffort, found.Role, "found role");
        AssertFalse(a.TryGet(13, out _), "absent");
    }

    private static void PlanStorePublishReplacesAndResetClears()
    {
        var first = new TheaterPlan { Alliance = 0, DaySerial = 10 };
        var second = new TheaterPlan { Alliance = 0, DaySerial = 11 };
        var sectors = SectorModel.Build(NewSnapshot(0));
        CampaignPlanStore.Publish(0, first, new Allocation { Alliance = 0 }, sectors);
        CampaignPlanStore.Publish(0, second, new Allocation { Alliance = 0 }, sectors);
        AssertTrue(CampaignPlanStore.TryGetPlan(0, out var plan), "plan published");
        AssertEqual(11, plan.DaySerial, "second publish replaces the first");
        AssertTrue(CampaignPlanStore.TryGetAllocation(0, out _), "allocation published");
        AssertTrue(CampaignPlanStore.TryGetSectors(0, out var got) && ReferenceEquals(sectors, got), "sectors published");
        AssertFalse(CampaignPlanStore.TryGetPlan(1, out _), "other alliance empty");
        AssertFalse(CampaignPlanStore.TryGetPlan(2, out _), "Europe never published");
        CampaignPlanStore.Publish(2, first, new Allocation(), sectors);
        AssertFalse(CampaignPlanStore.TryGetPlan(2, out _), "publish for alliance 2 ignored");

        AssertTrue(new List<string>(CampaignScope.RegisteredNames).Contains("campaign-planner-store"), "store registers its campaign reset");
        CampaignScope.ResetAll();
        AssertFalse(CampaignPlanStore.TryGetPlan(0, out _), "reset clears plan");
        AssertFalse(CampaignPlanStore.TryGetAllocation(0, out _), "reset clears allocation");
        AssertFalse(CampaignPlanStore.TryGetSectors(0, out _), "reset clears sectors");
    }

    // Integrator decision after G3 verify r2: a patch outside a review job reads the plan of the
    // last committed day; the planner's own jobs read the working plan.
    private static void PlanStoreReaderOutsideReviewSeesCommittedPlan()
    {
        bool inJob = false;
        CampaignScope.ResetAll();
        DailyReviewView.Attach(() => inJob);
        try
        {
            var sectors = SectorModel.Build(NewSnapshot(0));
            var monday = new TheaterPlan { Alliance = 0, DaySerial = 10 };
            var tuesday = new TheaterPlan { Alliance = 0, DaySerial = 11 };
            inJob = true;
            CampaignPlanStore.Publish(0, monday, new Allocation { Alliance = 0 }, sectors);
            inJob = false;
            AssertFalse(CampaignPlanStore.TryGetPlan(0, out _), "nothing committed yet");
            CampaignPlanStore.PreparePublish();
            CampaignPlanStore.SwapPublished();
            AssertTrue(CampaignPlanStore.TryGetPlan(0, out var seen) && seen.DaySerial == 10, "Monday committed");

            inJob = true;
            CampaignPlanStore.Publish(0, tuesday, new Allocation { Alliance = 0 }, sectors);
            AssertTrue(CampaignPlanStore.TryGetPlan(0, out var working) && working.DaySerial == 11, "inside a job: the working plan");
            inJob = false;
            AssertTrue(CampaignPlanStore.TryGetPlan(0, out var mid) && mid.DaySerial == 10, "between slices: Monday's plan");
            AssertTrue(CampaignPlanStore.TryGetSectors(0, out var midSectors) && ReferenceEquals(sectors, midSectors), "and Monday's sectors");
            CampaignPlanStore.PreparePublish();
            CampaignPlanStore.SwapPublished();
            AssertTrue(CampaignPlanStore.TryGetPlan(0, out var after) && after.DaySerial == 11, "after the commit: Tuesday");
            long before = TestFixtures.BeginAllocationWindow();
            for (int i = 0; i < 1000; i++)
            {
                CampaignPlanStore.PreparePublish();
                CampaignPlanStore.SwapPublished();
                CampaignPlanStore.TryGetPlan(0, out _);
            }
            AssertEqual(0L, TestFixtures.AllocatedSince(before), "plan commit and published read bytes");
        }
        finally
        {
            DailyReviewView.Attach(null);
            CampaignScope.ResetAll();
        }
    }

    // ---- ViewerCoverage (spec C14) ----

    private static void ViewerCoverageUsesEachViewersOwnRadius()
    {
        float[] x = { 0f, 100f };
        float[] z = { 0f, 0f };
        float[] r = { 10f, 50f };
        AssertTrue(ViewerCoverage.Covers(60f, 0f, x, z, r, 2), "second viewer's wide radius covers 40 away");
        AssertFalse(ViewerCoverage.Covers(15f, 0f, x, z, r, 2), "first viewer's radius 10 does not reach 15");
        AssertTrue(ViewerCoverage.Covers(8f, 0f, x, z, r, 2), "first viewer covers 8");
        AssertFalse(ViewerCoverage.Covers(60f, 0f, x, z, r, 1), "count limits the viewers");
    }

    private static void CoversFalseWithoutViewerInRadius()
    {
        AssertFalse(ViewerCoverage.Covers(0f, 0f, new float[0], new float[0], new float[0], 0), "no viewers");
        AssertFalse(ViewerCoverage.Covers(0f, 0f, null, null, null, 3), "null arrays");
        AssertFalse(ViewerCoverage.Covers(500f, 500f, new[] { 0f }, new[] { 0f }, new[] { 50f }, 1), "viewer out of range");
        AssertFalse(ViewerCoverage.Covers(0f, 0f, new[] { 0f }, new[] { 0f }, new[] { 0f }, 1), "zero radius sees nothing");
    }

    // ---- EnemyBeliefQuery ----

    private static void StrengthWithinIsConfidenceWeighted()
    {
        var beliefs = new List<EnemyBelief>
        {
            Belief(0f, 0f, 1000f, 1f),
            Belief(30f, 0f, 2000f, 0.5f),
            Belief(300f, 0f, 9000f, 1f),
        };
        AssertNear(2000f, EnemyBeliefQuery.StrengthWithin(beliefs, 0f, 0f, 50f), 0.01f, "confidence-weighted sum within radius");
        AssertNear(0f, EnemyBeliefQuery.StrengthWithin(null, 0f, 0f, 50f), 0.01f, "null list");
    }

    private static void NearestIgnoresExpired()
    {
        var beliefs = new List<EnemyBelief>
        {
            Belief(5f, 0f, 1000f, 0f),
            Belief(40f, 0f, 1000f, 0.3f),
            Belief(90f, 0f, 1000f, 1f),
        };
        AssertEqual(1, EnemyBeliefQuery.Nearest(beliefs, 0f, 0f, out float d), "expired nearest skipped");
        AssertNear(40f, d, 0.01f, "distance to nearest live belief");
        var expired = new List<EnemyBelief> { Belief(5f, 0f, 1000f, 0f) };
        AssertEqual(-1, EnemyBeliefQuery.Nearest(expired, 0f, 0f, out float none), "only expired");
        AssertTrue(none == float.MaxValue, "no distance when none");
    }

    // ---- SectorModel (P11) ----

    private static void SectorsDeterministicAcrossRebuilds()
    {
        var s = ChainSnapshot();
        var a = SectorModel.Build(s);
        var b = SectorModel.Build(s);
        AssertEqual(a.Count, b.Count, "same count");
        for (int i = 0; i < s.Areas.Count; i++)
            AssertEqual(a.SectorOfArea(s.Areas[i].Index), b.SectorOfArea(s.Areas[i].Index), "same sector for area " + i);

        // The order in which vanilla lists objectives does not change the sectors.
        var reversed = ChainSnapshot();
        reversed.Objectives.Reverse();
        var c = SectorModel.Build(reversed);
        for (int i = 0; i < s.Areas.Count; i++)
            AssertEqual(a.SectorOfArea(s.Areas[i].Index), c.SectorOfArea(s.Areas[i].Index), "objective order irrelevant, area " + i);

        // Two seeds on a chain of five adjacent areas split it at the middle; the tie goes to the lower sector.
        AssertEqual(2, a.Count, "two seeded sectors");
        AssertEqual(a.SectorOfArea(10), a.SectorOfArea(11), "area 11 joins seed 10");
        AssertEqual(a.SectorOfArea(14), a.SectorOfArea(13), "area 13 joins seed 14");
        AssertEqual(a.SectorOfArea(10), a.SectorOfArea(12), "equidistant area 12 joins the lower sector");
        AssertEqual(-1, a.SectorOfArea(99), "unknown area");
        AssertEqual(a.SectorOfArea(13), a.SectorAt(31f, 1f), "SectorAt uses the closest area");
    }

    private static void SectorTheaterFromCentroid()
    {
        var s = NewSnapshot(0);
        s.RelocationRange = 30f;
        s.Areas.Add(Area(0, -400f, 0f, 5f));
        s.Areas.Add(Area(1, -380f, 0f, 5f));
        s.Areas.Add(Area(2, 400f, -300f, 5f));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 1, X = -400f, Z = 0f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 2, X = 400f, Z = -300f });
        var m = SectorModel.Build(s);
        for (int sector = 0; sector < m.Count; sector++)
        {
            m.Centroid(sector, out float x, out float z);
            AssertEqual(TheaterClassifier.FromPosition(x, z), m.TheaterOf(sector), "theater of centroid, sector " + sector);
        }
        m.Centroid(m.SectorOfArea(0), out float cx, out _);
        AssertNear(-390f, cx, 0.01f, "centroid is the mean of member area centres");
        AssertEqual(Theater.TransMiss, m.TheaterOf(m.SectorOfArea(0)), "west of -200 is Trans-Mississippi");
        AssertEqual(Theater.Unknown, m.TheaterOf(99), "unknown sector");
    }

    private static void SectorAdjacencyUsesZoneEdges()
    {
        // Area centres 200 apart, relocation range 30: only their zone edges (25 apart) make
        // them neighbours (GetClosestAreaDistance(AIArea, AIArea), 14863-14900).
        var s = NewSnapshot(0);
        s.RelocationRange = 30f;
        s.Areas.Add(AreaWithEdges(0, 0f, 0f, new[] { -50f, 90f, 0f, 0f }, new[] { 0f, 0f, -50f, 50f }));
        s.Areas.Add(AreaWithEdges(1, 200f, 0f, new[] { 115f, 300f, 200f, 200f }, new[] { 0f, 0f, -50f, 50f }));
        s.Areas.Add(AreaWithEdges(2, 900f, 0f, new[] { 850f, 950f, 900f, 900f }, new[] { 0f, 0f, -50f, 50f }));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 1, X = 0f, Z = 0f });
        var m = SectorModel.Build(s);
        AssertEqual(m.SectorOfArea(0), m.SectorOfArea(1), "edge-adjacent area joins the seed's sector");
        AssertTrue(m.SectorOfArea(2) != m.SectorOfArea(0), "an area beyond the range gets its own sector");
        AssertEqual(2, m.Count, "seeded sector plus the unreached area's own sector");
        AssertNear(25f, AreaGeometry.AreaDistance(s.Areas[0], s.Areas[1]), 0.01f, "edge-to-edge distance");
    }

    // ---- LogisticsGate (spec component 5, 114272-114306) ----

    private static void ReachTrueWithOwnIipInCommandRange()
    {
        var s = NewSnapshot(0);
        s.Supply.Add(new SupplySourceRecord { Id = 7, Kind = SupplySourceKind.Iip, Owner = 0, X = 30f, Z = 0f, Condition = 1f, Built = true });
        var r = LogisticsGate.Evaluate(s, 0f, 0f, 40f, 0f);
        AssertTrue(r.InReach, "own IIP 30 away, command range 40");
        AssertEqual(7, r.NearestSourceId, "nearest source");
        AssertNear(30f, r.NearestSourceDistance, 0.01f, "distance");
        AssertNear(0.25f, r.Slack01, 0.001f, "slack = 1 - 30/40");
        AssertFalse(LogisticsGate.Evaluate(s, 0f, 0f, 25f, 0f).InReach, "out of a 25 range");
        AssertTrue(LogisticsGate.Evaluate(s, 0f, 0f, 25f, 0.25f).InReach, "administration slack widens the range to 31.25");
        s.Supply[0] = new SupplySourceRecord { Id = 7, Kind = SupplySourceKind.Iip, Owner = 0, X = 30f, Z = 0f, Invisible = true };
        AssertFalse(LogisticsGate.Evaluate(s, 0f, 0f, 40f, 0f).InReach, "invisible IIP does not supply");
    }

    private static void EnemyOwnedIipDoesNotSupply()
    {
        var s = NewSnapshot(0);
        s.Supply.Add(new SupplySourceRecord { Id = 7, Kind = SupplySourceKind.Iip, Owner = 1, X = 10f, Z = 0f, Condition = 1f, Built = true });
        var r = LogisticsGate.Evaluate(s, 0f, 0f, 40f, 0f);
        AssertFalse(r.InReach, "enemy IIP");
        AssertEqual(-1, r.NearestSourceId, "no source");
        AssertEqual(0f, r.Slack01, "no slack");
    }

    private static void ReachNeedsBuiltDepotAndZoneStackingRoom()
    {
        var s = NewSnapshot(0);
        s.Supply.Add(new SupplySourceRecord { Id = 8, Kind = SupplySourceKind.Depot, Owner = 0, X = 10f, Z = 0f, Condition = 1f, Built = false });
        AssertFalse(LogisticsGate.Evaluate(s, 0f, 0f, 40f, 0f).InReach, "depot under construction");
        s.Supply[0] = new SupplySourceRecord { Id = 8, Kind = SupplySourceKind.Depot, Owner = 0, X = 10f, Z = 0f, Condition = 0.5f, Built = true };
        AssertFalse(LogisticsGate.Evaluate(s, 0f, 0f, 40f, 0f).InReach, "damaged depot (condition < 0.75)");
        s.Supply[0] = new SupplySourceRecord { Id = 8, Kind = SupplySourceKind.Depot, Owner = 0, X = 10f, Z = 0f, Condition = 0.9f, Built = true };
        AssertTrue(LogisticsGate.Evaluate(s, 0f, 0f, 40f, 0f).InReach, "built depot");

        // Vanilla's zone stacking cap (ownpoints <= supplycapacity, 10941-10962, 10983).
        var full = Area(0, 0f, 0f, 5f);
        full.OwnPoints = 50f;
        full.SupplyCapacity = 20f;
        s.Areas.Add(full);
        AssertFalse(LogisticsGate.Evaluate(s, 0f, 0f, 40f, 0f).InReach, "target zone over its stacking cap");
    }

    private static void ClampToReachPullsBackToLastInReachPoint()
    {
        var s = NewSnapshot(0);
        s.Supply.Add(new SupplySourceRecord { Id = 1, Kind = SupplySourceKind.Iip, Owner = 0, X = 0f, Z = 0f, Condition = 1f, Built = true });
        float tx = 200f, tz = 0f;
        AssertTrue(LogisticsGate.ClampToReach(s, 0f, 0f, ref tx, ref tz, 40f, 0f), "clamped");
        AssertTrue(tx <= 40.001f && tx >= 30f, "hold point pulled back to the edge of reach, got " + tx);
        AssertEqual(0f, tz, "stays on the axis");
        AssertTrue(LogisticsGate.Evaluate(s, tx, tz, 40f, 0f).InReach, "clamped point is in reach");

        float ix = 20f, iz = 0f;
        AssertTrue(LogisticsGate.ClampToReach(s, 0f, 0f, ref ix, ref iz, 40f, 0f), "in reach already");
        AssertEqual(20f, ix, "unchanged when in reach");
    }

    private static void ClampToReachFalseWhenNothingInReach()
    {
        var s = NewSnapshot(0);
        s.Supply.Add(new SupplySourceRecord { Id = 1, Kind = SupplySourceKind.Iip, Owner = 1, X = 0f, Z = 0f, Built = true });
        float tx = 200f, tz = 0f;
        AssertFalse(LogisticsGate.ClampToReach(s, 0f, 0f, ref tx, ref tz, 40f, 0f), "no own source anywhere");
        AssertEqual(200f, tx, "target untouched");
    }

    // ---- Snapshot source pins and pure rules ----

    private static void AreaRecordCarriesNoEnemyAggregate()
    {
        // UpdateAreaPoints (10517-10609) builds these from every enemy at its destination.
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSnapshotRuntime.cs");
        foreach (string pattern in new[] { @"\bpoints\b", @"\bdistancepoints\b", @"\benemyavgcampaignunits\b", @"\benemycampaignunitsstrength\w*", @"\bavgenemytroopmoraleinzone\b", @"\benemyunits\b" })
            AssertFalse(Regex.IsMatch(src, pattern), "snapshot runtime reads no enemy aggregate: " + pattern);
        AssertTrue(Regex.IsMatch(src, @"\bownpoints\b"), "the own-points read stays");
    }

    private static void SupplyMetricIsRawNotVanillaHalved()
    {
        float[] group = { 0.45f, 0.45f, 0.45f, 0.45f };
        float raw = SnapshotRules.RawSupply01(group, 0.3f);
        AssertNear(0.45f, raw, 0.0001f, "raw supply");
        // Vanilla GetGroupSupplyState (14063-14066) halves both terms.
        float vanilla = Math.Max((group[0] + group[1] + group[2] + group[3]) / 4f * 0.5f, 0.3f * 0.5f);
        AssertNear(0.225f, vanilla, 0.0001f, "vanilla halved metric");
        AssertNear(vanilla * 2f, raw, 0.0001f, "raw = 2 x vanilla");
        AssertNear(0.8f, SnapshotRules.RawSupply01(group, 0.8f), 0.0001f, "the unit's own supply wins when higher");
        AssertNear(0.6f, SnapshotRules.RawSupply01(null, 0.6f), 0.0001f, "missing group array");
        AssertNear(1f, SnapshotRules.RawSupply01(new[] { 3f, 3f, 3f, 3f }, 0f), 0.0001f, "clamped to 1");
    }

    private static void EmbarkedMatchesVanillaAfloatTest()
    {
        AssertTrue(SnapshotRules.IsEmbarked(0.5f, 0), "embarking (116348-116360)");
        AssertTrue(SnapshotRules.IsEmbarked(0f, 1), "afloat at sea");
        AssertTrue(SnapshotRules.IsEmbarked(0f, 2), "afloat on a river");
        AssertFalse(SnapshotRules.IsEmbarked(0f, 0), "ashore");
    }

    private static void FightingForceMirrorsVanillaWithoutRecompute()
    {
        // UnitIsFightingForce 16867-16890, without its UpdateGroupStrength call.
        AssertTrue(SnapshotRules.IsFightingForce(15, false, 0f, 1000f), "corps");
        AssertFalse(SnapshotRules.IsFightingForce(14, false, 50000f, 1000f), "division");
        AssertFalse(SnapshotRules.IsFightingForce(17, false, 50000f, 1000f), "fleet");
        AssertFalse(SnapshotRules.IsFightingForce(18, false, 50000f, 1000f), "army group");
        AssertTrue(SnapshotRules.IsFightingForce(16, false, 0f, 1000f), "army without direct corps");
        AssertTrue(SnapshotRules.IsFightingForce(16, true, 5000f, 1000f), "army with corps and its own strength");
        AssertFalse(SnapshotRules.IsFightingForce(16, true, 500f, 1000f), "army HQ shell over its corps");
    }

    private static void CavalryShareFromDirectStrengthPerType()
    {
        AssertNear(0.25f, SnapshotRules.CavalryShare(new[] { 600, 200, 0, 0, 0 }), 0.0001f, "cavalry is type 1");
        AssertEqual(0f, SnapshotRules.CavalryShare(new[] { 0, 0 }), "empty group");
        AssertEqual(0f, SnapshotRules.CavalryShare(new[] { 5 }), "no cavalry slot");
        AssertEqual(0f, SnapshotRules.CavalryShare(null), "missing array");
    }

    private static void ObjectiveRolesFromVanillaFacts()
    {
        AssertEqual(ObjectiveRole.Capital, SnapshotRules.ObjectiveRoles(true, false, 0, false), "capital town");
        AssertEqual(ObjectiveRole.Port, SnapshotRules.ObjectiveRoles(false, true, 0, false), "harbour IIP");
        AssertEqual(ObjectiveRole.RailHub, SnapshotRules.ObjectiveRoles(false, false, 2, false), "two lines meet");
        AssertEqual(ObjectiveRole.None, SnapshotRules.ObjectiveRoles(false, false, 1, false), "one line is no hub");
        AssertEqual(ObjectiveRole.Industry, SnapshotRules.ObjectiveRoles(false, false, 0, true), "industry");
        AssertEqual(ObjectiveRole.Capital | ObjectiveRole.RailHub | ObjectiveRole.Industry, SnapshotRules.ObjectiveRoles(true, false, 3, true), "combined");
    }

    // Pins the persisted / logged enum values (sidecar v3 and telemetry rows read them).
    private static void ContractEnumValuesPinned()
    {
        AssertEqual(0, (int)PlayerCommandClass.NotCareer, "NotCareer");
        AssertEqual(1, (int)PlayerCommandClass.NoCommand, "NoCommand");
        AssertEqual(2, (int)PlayerCommandClass.CommanderInChief, "CommanderInChief");
        AssertEqual(3, (int)PlayerCommandClass.MapGroup, "MapGroup");
        AssertEqual(4, (int)PlayerCommandClass.BelowMapLevel, "BelowMapLevel");
        AssertEqual(5, (int)PlayerCommandClass.ArmyGroupCommander, "ArmyGroupCommander");

        AssertEqual(0u, (uint)default(FormationFlags), "None");
        AssertEqual(1u, (uint)FormationFlags.InBattle, "InBattle");
        AssertEqual(2u, (uint)FormationFlags.OnRetreat, "OnRetreat");
        AssertEqual(4u, (uint)FormationFlags.InOffensiveOps, "InOffensiveOps");
        AssertEqual(8u, (uint)FormationFlags.InDefensiveOps, "InDefensiveOps");
        AssertEqual(16u, (uint)FormationFlags.CapitalDefense, "CapitalDefense");
        AssertEqual(32u, (uint)FormationFlags.VanillaDepotBuilder, "VanillaDepotBuilder");
        AssertEqual(64u, (uint)FormationFlags.FortBuilder, "FortBuilder");
        AssertEqual(128u, (uint)FormationFlags.SeaInvasion, "SeaInvasion");
        AssertEqual(256u, (uint)FormationFlags.RaidForce, "RaidForce");
        AssertEqual(512u, (uint)FormationFlags.InTransfer, "InTransfer");
        AssertEqual(1024u, (uint)FormationFlags.Embarked, "Embarked");
        AssertEqual(2048u, (uint)FormationFlags.FightingForce, "FightingForce");
        AssertEqual(4096u, (uint)FormationFlags.HasPath, "HasPath");
        AssertEqual(8192u, (uint)FormationFlags.MoveQueued, "MoveQueued");

        AssertEqual(0, (int)ObjectiveRole.None, "role None");
        AssertEqual(1, (int)ObjectiveRole.Capital, "role Capital");
        AssertEqual(2, (int)ObjectiveRole.RailHub, "role RailHub");
        AssertEqual(4, (int)ObjectiveRole.Port, "role Port");
        AssertEqual(8, (int)ObjectiveRole.Industry, "role Industry");
        AssertEqual(0, (int)SupplySourceKind.Iip, "Iip");
        AssertEqual(1, (int)SupplySourceKind.Depot, "Depot");

        AssertEqual(0, (int)DetectionSource.Seen, "Seen");
        AssertEqual(2, (int)DetectionSource.Contact, "Contact");
        AssertEqual(4, (int)DetectionSource.ObjectiveGarrison, "ObjectiveGarrison");
        AssertEqual(0, (int)default(EnemyTraits), "traits None");
        AssertEqual(1, (int)EnemyTraits.Embarked, "traits Embarked");
        AssertEqual(2, (int)EnemyTraits.CavalryMajority, "CavalryMajority");
        AssertEqual(4, (int)EnemyTraits.OnOwnSoil, "OnOwnSoil");
        AssertEqual(8, (int)EnemyTraits.NearOwnCoast, "NearOwnCoast");

        AssertEqual(0, (int)TheaterPosture.Hold, "posture Hold");
        AssertEqual(1, (int)TheaterPosture.MainEffort, "posture MainEffort");
        AssertEqual(2, (int)TheaterPosture.Support, "posture Support");
        AssertEqual(3, (int)TheaterPosture.EconomyOfForce, "EconomyOfForce");
        AssertEqual(4, (int)TheaterPosture.Screen, "posture Screen");
        AssertEqual(5, (int)TheaterPosture.Recover, "posture Recover");
        AssertEqual(0, (int)FormationRole.Hold, "role Hold");
        AssertEqual(1, (int)FormationRole.MainEffort, "role MainEffort");
        AssertEqual(2, (int)FormationRole.Support, "role Support");
        AssertEqual(3, (int)FormationRole.Reserve, "Reserve");
        AssertEqual(4, (int)FormationRole.CapitalGuard, "CapitalGuard");
        AssertEqual(5, (int)FormationRole.DepotBuilder, "DepotBuilder");
        AssertEqual(6, (int)FormationRole.Recover, "role Recover");
        AssertEqual(7, (int)FormationRole.Recon, "Recon");
        AssertEqual(0, (int)ReconKind.Screen, "kind Screen");
        AssertEqual(1, (int)ReconKind.Picket, "Picket");
        AssertEqual(2, (int)ReconKind.AdvanceGuard, "AdvanceGuard");
        AssertEqual(3, (int)ReconKind.Probe, "Probe");
        AssertEqual(0, (int)PlannerReviewMode.Full, "review Full");
        AssertEqual(1, (int)PlannerReviewMode.Bookkeeping, "review Bookkeeping");
        AssertEqual(2, (int)PlannerReviewMode.StandDown, "review StandDown");
    }

    // The G0 contract members that later slices read (plan § G7 read census). G7's
    // PlannerReadCensusTests replaces this with the parsed census; until then this keeps
    // every field's shape compiled and round-tripping.
    private static void ContractRecordsCarryCensusFields()
    {
        var detection = new EnemyDetection { InstanceId = 1, UnitKey = "Army of Tennessee:40", UnitTyp = 16, X = 1f, Z = 2f, Strength = 30000f, Source = DetectionSource.Seen, Traits = EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil };
        AssertEqual("Army of Tennessee:40", detection.UnitKey, "detection key");
        AssertEqual(EnemyTraits.CavalryMajority | EnemyTraits.OnOwnSoil, detection.Traits, "detection traits");

        var belief = new EnemyBelief { InstanceId = 1, UnitKey = "k:1", UnitTyp = 15, X = 3f, Z = 4f, Strength = 9000f, LastSeenDay = 700, Confidence01 = 0.5f, UncertaintyRadius = 12f, LastSource = DetectionSource.Contact, Traits = EnemyTraits.NearOwnCoast };
        AssertEqual(700, belief.LastSeenDay, "belief last seen");
        AssertEqual(12f, belief.UncertaintyRadius, "belief radius");
        AssertEqual(DetectionSource.Contact, belief.LastSource, "belief source");

        var commander = new CommanderRecord { Id = 3, Rank = 8, Initiative = 0.6f, Leadership = 0.5f, Administration = 0.4f, Average = 0.55f, Available = true, Navy = false, Dummy = false, RefreshCommand = false, ServingUnderPlayer = false, IsPlayer = false, CommandInstanceId = 101, CommandUnitTyp = 16, Name = "R. E. Lee" };
        AssertEqual(0.55f, commander.Average, "commander average");
        AssertEqual(16, commander.CommandUnitTyp, "commander command type");

        var profile = new CommanderProfile { RequiredOddsShift = -0.05f, AttackBias = 0.35f, SupplySlack = 0.1f };
        AssertEqual(-0.05f, profile.RequiredOddsShift, "profile odds shift");
        AssertEqual(0.35f, profile.AttackBias, "profile attack bias");
        AssertEqual(0.1f, profile.SupplySlack, "profile supply slack");

        var task = new ReconTask { Kind = ReconKind.Picket, SectorId = 2, FromX = 1f, FromZ = 2f, HoldX = 3f, HoldZ = 4f, Priority = 0.7f, Reason = "unwatched-approach" };
        AssertEqual(0.7f, task.Priority, "recon priority");
        AssertEqual(4f, task.HoldZ, "recon hold");

        var plan = new TheaterPlan { Alliance = 1, DaySerial = 9, MainSectorId = 2, MainObjectiveId = 31, MainRequiredRatio = 1.2f, CapitalFloorStrength = 15000f, SwitchReason = "capital-threat", PriorId = "overland-1864" };
        plan.Sectors.Add(new SectorPlan { SectorId = 2, Theater = Theater.East, Posture = TheaterPosture.MainEffort, TargetObjectiveId = 31, TargetX = 5f, TargetZ = 6f, RequiredRatio = 1.2f });
        plan.DepotTasks.Add(new DepotTask { X = 1f, Z = 1f, SectorId = 2, Priority = 0.5f });
        AssertEqual(TheaterPosture.MainEffort, plan.Sectors[0].Posture, "sector posture");
        AssertEqual(1.2f, plan.Sectors[0].RequiredRatio, "sector ratio");
        AssertEqual(15000f, plan.CapitalFloorStrength, "capital floor");
        AssertEqual("overland-1864", plan.PriorId, "prior id");
        AssertEqual(-1, new TheaterPlan().MainSectorId, "no main sector by default");

        var allocation = new Allocation { Alliance = 1 };
        allocation.Assignments.Add(new Assignment { InstanceId = 5, Role = FormationRole.Recon, SectorId = 2, TargetX = 7f, TargetZ = 8f, Reason = "screen", Withheld = true });
        allocation.Shortfalls.Add(6);
        allocation.Surpluses.Add(7);
        AssertTrue(allocation.TryGet(5, out var assignment) && assignment.Withheld, "assignment fields");
        AssertEqual(6, allocation.Shortfalls[0], "shortfall");
        AssertEqual(7, allocation.Surpluses[0], "surplus");

        var rail = new RailLineRecord { Index = 2, Progress = 0f, AX = 1f, AZ = 2f, BX = 3f, BZ = 4f, AIipId = 11, BIipId = 12 };
        AssertEqual(12, rail.BIipId, "rail endpoint iip");
        var crossing = new CrossingRecord { X = 5f, Z = 6f };
        AssertEqual(6f, crossing.Z, "crossing");
        var objective = new ObjectiveRecord { ObjectiveId = 4, HolderAlliance = 1, Available = true, Accomplished = false, Roles = ObjectiveRole.Port, Importance01 = 0.4f };
        var band = new RankBand { UnitTyp = 15, MinRank = 7, MaxRank = 8 };
        AssertEqual(8, band.MaxRank, "rank band");

        var formation = Formation(9, "x:1", 0f, 0f, 1f);
        formation.Flags = FormationFlags.Embarked | FormationFlags.MoveQueued;
        formation.Chain = WlChainClass.CarryingGroup;
        formation.ParentInstanceId = -1;
        formation.UseRails = true;
        formation.UseRivers = false;
        AssertTrue(formation.UseRails && !formation.UseRivers, "transport modes");
        AssertTrue((formation.Flags & FormationFlags.MoveQueued) != 0, "formation flags");
        AssertEqual(WlChainClass.CarryingGroup, formation.Chain, "formation chain class");
    }

    // ---- G0 landing (verify-g0 F1-F4) ----

    // UpdateImportanceValues 14905-14995 for one faction, on fixture prefs.
    private static void ImportanceMirrorsVanillaTerms()
    {
        const float pop = 0.01f, transport = 0.2f, nonWar = 0.35f, objective = 5f, capital = 20f;
        AssertNear(10f, SnapshotRules.TargetImportance(true, 1000f, 0f, true, false, false, pop, transport, nonWar, objective, capital), 0.001f, "enemy town: population term x 1");
        AssertNear(3.5f, SnapshotRules.TargetImportance(true, 1000f, 0f, false, false, false, pop, transport, nonWar, objective, capital), 0.001f, "own or non-war town: x 0.35");
        AssertNear(8.5f, SnapshotRules.TargetImportance(true, 1000f, 0f, false, true, false, pop, transport, nonWar, objective, capital), 0.001f, "followed objective adds its bonus unscaled");
        AssertNear(23.5f, SnapshotRules.TargetImportance(true, 1000f, 0f, false, false, true, pop, transport, nonWar, objective, capital), 0.001f, "capital bonus only at its own index, unscaled");
        AssertNear(10f, SnapshotRules.TargetImportance(false, 1000f, 50f, true, false, true, pop, transport, nonWar, objective, capital), 0.001f, "IIP: transport term, never the capital bonus");
        AssertNear(3.5f, SnapshotRules.TargetImportance(false, 0f, 50f, false, false, false, pop, transport, nonWar, objective, capital), 0.001f, "own IIP x 0.35");
    }

    private static void SectorAdjacencyCacheFollowsGeometry()
    {
        // Same area count, different geometry: the cached matrix must not leak across.
        var near = NewSnapshot(0);
        near.RelocationRange = 15f;
        near.Areas.Add(Area(0, 0f, 0f, 1f));
        near.Areas.Add(Area(1, 10f, 0f, 1f));
        near.Objectives.Add(new ObjectiveRecord { ObjectiveId = 1, X = 0f, Z = 0f });
        var far = NewSnapshot(0);
        far.RelocationRange = 15f;
        far.Areas.Add(Area(0, 0f, 0f, 1f));
        far.Areas.Add(Area(1, 100f, 0f, 1f));
        far.Objectives.Add(new ObjectiveRecord { ObjectiveId = 1, X = 0f, Z = 0f });

        AssertEqual(1, SectorModel.Build(near).Count, "adjacent pair is one sector");
        AssertEqual(2, SectorModel.Build(far).Count, "moved area is its own sector");
        AssertEqual(1, SectorModel.Build(near).Count, "back to the first geometry");

        var a = near.Areas.ToArray();
        AssertEqual(SectorModel.AdjacencyKey(a, 15f), SectorModel.AdjacencyKey(near.Areas.ToArray(), 15f), "same geometry, same key");
        AssertTrue(SectorModel.AdjacencyKey(a, 15f) != SectorModel.AdjacencyKey(a, 16f), "range changes the key");
        a[1].EdgeX = new[] { 10f, 11f, 10f, 10f };
        AssertTrue(SectorModel.AdjacencyKey(a, 15f) != SectorModel.AdjacencyKey(near.Areas.ToArray(), 15f), "an edge changes the key");
    }

    // Spec C5 / plan P1: while vanilla's job machine is paused (11368), every order-issuing step of
    // the coordinator's review is unreachable (BattleWriteGateTests style source pin).
    // Sliced review (perf merge): the pause is captured with the day's inputs (ReviewDay
    // .JobMachinePaused) and each order-issuing job tests it.
    private static void CoordinatorPauseGuardsOrderIssuingSteps()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        string head = MethodBody(src, "private void RunHeadReviewJob(");
        foreach (string call in new[] { "Succession.CheckEvents(", "SwapOfficer(" })
            AssertGuardedByPause(head, call, "!d.JobMachinePaused");
        foreach (string call in new[] { "UpdateCapitalThreat(", "PublishDirectorPosture(" })
            AssertFalse(IsGuardedByPause(head, head.IndexOf(call, StringComparison.Ordinal), "!d.JobMachinePaused"), call + " keeps running during the pause");

        string job = MethodBody(src, "private void RunReviewJob(");
        AssertContains(job, "bool paused = d.JobMachinePaused;", "the day's pause");
        // Each order-issuing case returns or skips while paused before its call.
        foreach (var (kind, guard, call) in new[]
        {
            ("Front", "if (paused) break;", "UpdateFrontLedger("),
            ("Defense", "if (!paused)", "UpdateDefenseIntent("),
            ("Execute", "if (ready && !paused)", "CampaignPlannerRuntime.ReviewExecute("),
            ("Formation", "if (!ready || paused) break;", "UpdateFormationDirectiveLedger("),
        })
        {
            string body = CaseBody(job, kind);
            int g = body.IndexOf(guard, StringComparison.Ordinal);
            int c = body.IndexOf(call, StringComparison.Ordinal);
            AssertTrue(g >= 0 && c > g, kind + " job: " + call + " behind " + guard);
        }
        // Bookkeeping that must keep running is not behind the pause.
        foreach (var (kind, call) in new[] { ("Plan", "CampaignPlannerRuntime.ReviewPlan("), ("Fiscal", "UpdateFiscalIntent("), ("Construction", "UpdateConstructionIntent(") })
        {
            string body = CaseBody(job, kind);
            AssertTrue(body.Contains(call) && !body.Contains("paused) break;") && !body.Contains("!paused"), call + " keeps running during the pause");
        }
        AssertContains(job, "UpdateConstructionIntent(alliance, a.Era.Stage, d.LogHeartbeat, paused)", "construction receives the pause");

        // The telegraph start is an executor write (AddConstructionWish): guarded inside construction.
        string construction = MethodBody(src, "private void UpdateConstructionIntentCore(");
        AssertGuardedByPause(construction, "TelegraphConstructionRuntime.TryStartTelegraph(", "!jobMachinePaused");
    }

    private static string CaseBody(string job, string kind)
    {
        string label = "case DailyReviewJobKind." + kind + ":";
        int start = job.IndexOf(label, StringComparison.Ordinal);
        AssertTrue(start >= 0, "case present: " + kind);
        int end = job.IndexOf("case DailyReviewJobKind.", start + label.Length, StringComparison.Ordinal);
        return job.Substring(start, (end < 0 ? job.Length : end) - start);
    }

    private static void CoordinatorPauseGatesAreTheVanillaPredicates()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/StrategicCoordinator.cs");
        string review = MethodBody(src, "private bool BeginReviewDay(");
        AssertContains(review, "if (WlCareerNotStarted())", "the whole review defers only before a commander is chosen");
        AssertFalse(review.Contains("WlCareerStartPending()"), "the review no longer defers between commands");
        AssertContains(review, "bool jobMachinePaused = WlVanillaJobMachinePaused();", "pause read once per review");
        AssertContains(review, "JobMachinePaused = jobMachinePaused,", "captured with the day's inputs");
        AssertTrue(review.IndexOf("if (WlCareerNotStarted())", StringComparison.Ordinal) < review.IndexOf("_review.Begin(", StringComparison.Ordinal), "early return before the day begins");

        string paused = MethodBody(src, "public static bool WlVanillaJobMachinePaused(");
        AssertContains(paused, "WlCareerStartGate.VanillaJobMachinePaused(active, DLC_WL.GetPlayerCommandHierarchy(), DLC_WL.IsCommanderInChief())", "11368 predicate with its CIC term");
        string notStarted = MethodBody(src, "public static bool WlCareerNotStarted(");
        AssertContains(notStarted, "WlCareerStartGate.CareerNotStarted(DLC_WL.dlc_scenarioactive, DLC_WL.dlc_chosencommander)", "career start predicate");

        string hook = ReadRepoFile("src/WhiskeyRealism/Patches/MonthlyTickHookPatch.cs");
        AssertContains(hook, "!StrategicCoordinator.WlCareerNotStarted()", "the cadence notifies between commands");
        AssertFalse(hook.Contains("WlCareerStartPending"), "the cadence no longer waits for a command");
    }

    private static void PlannerReviewGatesPinned()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        string plan = MethodBody(src, "internal static void ReviewPlan(");
        AssertContains(plan, "!FlagOn(Plugin.EnableCampaignPlanner)", "review gated on its flag");
        AssertContains(plan, "mode == PlannerReviewMode.StandDown) return;", "CIC stand-down");
        int bookkeeping = plan.IndexOf("mode == PlannerReviewMode.Bookkeeping) return;", StringComparison.Ordinal);
        int publish = plan.IndexOf("CampaignPlanStore.Publish(", StringComparison.Ordinal);
        AssertTrue(bookkeeping >= 0 && publish > bookkeeping, "bookkeeping returns before anything is published");

        string execute = MethodBody(src, "internal static void ReviewExecute(");
        AssertContains(execute, "if (!ExecutorsEnabled()) return;", "executors gated on both flags (G5a: one helper, also read by the patches)");
        AssertContains(MethodBody(src, "internal static bool ExecutorsEnabled("), "FlagOn(Plugin.EnableCampaignPlanner) && FlagOn(Plugin.EnableCampaignPlannerExecutors)", "...which is both flags");
        AssertContains(execute, "!= PlannerReviewMode.Full) return;", "executors run only in a full review");
    }

    private static void AssertGuardedByPause(string body, string call, string guard)
    {
        int at = body.IndexOf(call, StringComparison.Ordinal);
        AssertTrue(at >= 0, "call present: " + call);
        for (; at >= 0; at = body.IndexOf(call, at + call.Length, StringComparison.Ordinal))
            AssertTrue(IsGuardedByPause(body, at, guard), call + " is reachable while vanilla's job machine is paused");
    }

    // True when an enclosing block, or a brace-less if directly above, tests the guard.
    private static bool IsGuardedByPause(string body, int at, string guard)
    {
        int lineStart = body.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        string before = body.Substring(0, lineStart).TrimEnd();
        int prevLineStart = before.LastIndexOf('\n') + 1;
        string previous = before.Substring(prevLineStart).Trim();
        if (previous.StartsWith("if (") && !previous.EndsWith(";") && !previous.EndsWith("{") && previous.Contains(guard))
            return true;

        int depth = 0;
        for (int i = at - 1; i >= 0; i--)
        {
            char c = body[i];
            if (c == '}') depth++;
            else if (c == '{')
            {
                if (depth > 0) { depth--; continue; }
                int headerEnd = body.LastIndexOf('\n', Math.Max(0, i - 1));
                int headerStart = headerEnd > 0 ? body.LastIndexOf('\n', headerEnd - 1) + 1 : 0;
                string header = body.Substring(headerStart, Math.Max(0, headerEnd - headerStart)).Trim();
                if (header.StartsWith("if (") && header.Contains(guard)) return true;
            }
        }
        return false;
    }

    // From the signature to its matching closing brace.
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

    // ---- fixtures ----

    private static CampaignSnapshot NewSnapshot(int alliance)
    {
        var s = new CampaignSnapshot();
        s.Reset(alliance, alliance, 100, PlayerCommandClass.NotCareer, -1, null);
        return s;
    }

    private static FormationRecord Formation(int id, string key, float x, float z, float strength)
    {
        return new FormationRecord { InstanceId = id, UnitKey = key, UnitTyp = 15, X = x, Z = z, Strength = strength, Name = key, ParentInstanceId = -1, AreaIndex = -1 };
    }

    private static EnemyBelief Belief(float x, float z, float strength, float confidence)
    {
        return new EnemyBelief { InstanceId = -1, UnitKey = "b", UnitTyp = 15, X = x, Z = z, Strength = strength, Confidence01 = confidence };
    }

    // An area whose zone edges are its centre: distance between two such areas is centre to centre.
    private static AreaRecord Area(int index, float x, float z, float importance)
    {
        return new AreaRecord { Index = index, X = x, Z = z, EdgeX = new[] { x, x, x, x }, EdgeZ = new[] { z, z, z, z }, Importance = importance, OwnPoints = 0f, SupplyCapacity = 100f };
    }

    private static AreaRecord AreaWithEdges(int index, float x, float z, float[] edgeX, float[] edgeZ)
    {
        return new AreaRecord { Index = index, X = x, Z = z, EdgeX = edgeX, EdgeZ = edgeZ, Importance = 1f, SupplyCapacity = 100f };
    }

    // Five areas on a line 10 apart (relocation range 12: each is adjacent to its neighbours),
    // objectives in the first and last.
    private static CampaignSnapshot ChainSnapshot()
    {
        var s = NewSnapshot(0);
        s.RelocationRange = 12f;
        for (int i = 0; i < 5; i++)
            s.Areas.Add(Area(10 + i, i * 10f, 0f, 1f));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 1, X = 0f, Z = 0f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 2, X = 40f, Z = 0f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 3, X = 40f, Z = 1f, Accomplished = true });
        return s;
    }

    // ---- per-campaign static map facts (perf review P1): cached once, equal to the direct scan ----

    private static void StaticSourceKeyRebuildsOnNewListOrCount()
    {
        var key = new StaticSourceKey();
        var list = new List<int> { 1, 2, 3 };
        AssertFalse(key.Matches(list, list.Count), "an unset key never matches");
        key.Set(list, list.Count);
        AssertTrue(key.Matches(list, list.Count), "same list and count");
        AssertFalse(key.Matches(list, 4), "same list, another count (an element added)");
        AssertFalse(key.Matches(new List<int> { 1, 2, 3 }, 3), "a new list of the same size (a reload)");
        AssertFalse(key.Matches(null, 3), "a torn-down list");
        key.Clear();
        AssertFalse(key.Matches(list, list.Count), "cleared on campaign reset");
    }

    private static void RailGeometryMatchesDirectScan()
    {
        foreach (int seed in new[] { 1, 7, 42, 1861, 1865 })
        {
            var random = new Random(seed);
            var lines = RandomLines(random, 40);
            var rails = BuildRails(lines);

            float radius = 5f + (float)random.NextDouble() * 40f;
            for (int t = 0; t < 60; t++)
            {
                float x = (float)random.NextDouble() * 1000f, z = (float)random.NextDouble() * 1000f;
                int expected = LinesNearDirect(lines, x, z, radius);
                AssertEqual(expected, rails.LinesNearTown(1000 + t, x, z, radius), "town " + t + " lines near, seed " + seed);
                AssertEqual(expected, rails.LinesNearTown(1000 + t, x, z, radius), "town " + t + " memo hit, seed " + seed);
            }
            for (int id = -1; id < 260; id++)
                AssertEqual(LinesOfIipDirect(lines, id), rails.LinesOfIip(id), "lines of iip " + id + ", seed " + seed);
            for (int i = 0; i < lines.Count; i++)
            {
                var expected = new RailLineRecord { Index = i, AIipId = -1, BIipId = -1 };
                EndpointsDirect(lines[i], ref expected);
                var cached = new RailLineRecord { Index = i, AIipId = -1, BIipId = -1 };
                rails.CopyEndpoints(i, ref cached);
                AssertEqual(expected.AIipId, cached.AIipId, "line " + i + " A id, seed " + seed);
                AssertEqual(expected.BIipId, cached.BIipId, "line " + i + " B id, seed " + seed);
                AssertEqual(expected.AX, cached.AX, "line " + i + " AX");
                AssertEqual(expected.AZ, cached.AZ, "line " + i + " AZ");
                AssertEqual(expected.BX, cached.BX, "line " + i + " BX");
                AssertEqual(expected.BZ, cached.BZ, "line " + i + " BZ");
            }
        }
    }

    private static void RailGeometryMemoFollowsRadiusAndRebuild()
    {
        // One line with an IIP 10 from the town: in at radius 12, out at radius 8.
        var rails = new RailGeometry();
        rails.BeginLine();
        rails.AddIip(1, 10f, 0f);
        rails.EndLine();
        AssertEqual(1, rails.LinesNearTown(7, 0f, 0f, 12f), "inside the radius");
        AssertEqual(0, rails.LinesNearTown(7, 0f, 0f, 8f), "a new radius is not served from the memo");
        AssertEqual(0, rails.LinesNearTown(7, 0f, 0f, 0f), "no radius, no line");
        AssertEqual(1, rails.LinesNearTown(7, 0f, 0f, 12f), "back to the first radius: memo holds town 7 at 12");

        // A reload at the same radius: the same town id must not keep the old map's count.
        rails.Clear();
        AssertEqual(0, rails.LinesNearTown(7, 0f, 0f, 12f), "no lines after clear");
        var gone = new RailLineRecord { AIipId = -1, BIipId = -1 };
        rails.CopyEndpoints(0, ref gone);
        AssertEqual(-1, gone.AIipId, "no line 0 after clear");
        for (int i = 0; i < 2; i++)
        {
            rails.BeginLine();
            rails.AddIip(10 + i, 0f, 5f);
            rails.EndLine();
        }
        AssertEqual(2, rails.LinesNearTown(7, 0f, 0f, 12f), "rebuilt geometry, fresh count");
        AssertEqual(1, rails.LinesOfIip(10), "rebuilt iip index");
        AssertEqual(0, rails.LinesOfIip(1), "old iip index dropped");

        // An empty line (vanilla line without IIPs) keeps its index and has no endpoints.
        rails.BeginLine();
        rails.EndLine();
        var record = new RailLineRecord { AIipId = -1, BIipId = -1 };
        rails.CopyEndpoints(2, ref record);
        AssertEqual(-1, record.AIipId, "empty line, no A");
        AssertEqual(-1, record.BIipId, "empty line, no B");
    }

    // The snapshot runtime reads the static map once per campaign and only the dynamic facts daily.
    private static void SnapshotStaticFactsReadOncePerCampaign()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSnapshotRuntime.cs");

        string reset = MethodBody(src, "private static void ResetCaches(");
        foreach (string clear in new[] { "Rails.Clear()", "RailSource.Clear()", "AreaShapes.Clear()", "AreaSource.Clear()",
                                         "CrossingCache.Clear()", "CrossingSource.Clear()", "PositionCache.Clear()" })
            AssertContains(reset, clear, "campaign reset clears " + clear);

        string build = MethodBody(src, "internal static CampaignSnapshot Build(");
        AssertContains(build, "TelemetryPerf.Scope(\"campaign.planner.snapshot\", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0)", "snapshot timed in its own scope");
        AssertContains(build, "return BuildCore(", "the scope wraps the whole build");

        string areas = MethodBody(src, "private static void ReadAreas(");
        AssertContains(areas, "AreaSource.Matches(", "areas rebuild their shapes only on a new area list");
        AssertFalse(areas.Contains("zoneedges"), "edges are not read daily");
        string shapes = MethodBody(src, "private static void ReadAreaShapes(");
        foreach (string daily in new[] { "importancevalues", "ownpoints", "supplycapacity" })
        {
            AssertContains(areas, "area." + daily, "area " + daily + " read every review");
            AssertFalse(shapes.Contains(daily), "area " + daily + " is not frozen in the shape cache");
        }

        // Supply: ownership, condition and construction are read live for every source, with no
        // cache in front of the record (only the static position goes through PositionOf).
        string supply = MethodBody(src, "private static void ReadSupply(");
        foreach (string live in new[] { "Owner = iip.allianceowner", "Condition = iip.condition", "Owner = depot.Owner",
                                        "Condition = depot.Condition", "Built = depot.constructiontimer <= 0f" })
            AssertContains(supply, live, "supply reads live: " + live);
        AssertFalse(supply.Contains("TryGetValue") || supply.Contains("Cache") || supply.Contains("Source."), "no cached supply record");
        int iipLoop = supply.IndexOf("for (int i = 0; i < iips.Count; i++)", StringComparison.Ordinal);
        AssertTrue(iipLoop >= 0, "supply walks every IIP");
        AssertFalse(supply.Substring(0, iipLoop).Contains("return"), "no early return before the live IIP reads");

        string objectives = MethodBody(src, "private static void ReadObjectives(");
        // G3c moved the town target's record into TownRecord (shared with the mapped ground).
        string townRecord = MethodBody(src, "private static ObjectiveRecord TownRecord(");
        AssertContains(townRecord, "Rails.LinesNearTown(", "town rail lines come from the cached geometry");
        AssertContains(objectives, "TownRecord(record, town,", "a listed town target is built by TownRecord");
        AssertContains(objectives, "Rails.LinesOfIip(", "iip rail lines come from the cached geometry");
        AssertFalse(objectives.Contains("IIPsForTransportCapacityUpdate") || townRecord.Contains("IIPsForTransportCapacityUpdate"), "no per-review walk of the rail lines");

        string rails = MethodBody(src, "private static void ReadRails(");
        AssertContains(rails, "EnsureRailGeometry()", "rails rebuild their geometry only on a new railroad list");
        AssertContains(rails, "Progress = line.constructionprogress", "construction progress read every review");
        AssertContains(rails, "Rails.CopyEndpoints(", "endpoints come from the cache");
        AssertFalse(rails.Contains("IIPsForTransportCapacityUpdate"), "no per-review endpoint search");

        string ensure = MethodBody(src, "private static void EnsureRailGeometry(");
        AssertContains(ensure, "RailSource.Matches(", "rail geometry keyed on the railroad list");
        int cleared = ensure.IndexOf("Rails.Clear()", StringComparison.Ordinal);
        AssertTrue(cleared >= 0 && cleared < ensure.IndexOf("Rails.BeginLine()", StringComparison.Ordinal), "the old geometry is cleared before a rebuild");

        string crossings = MethodBody(src, "private static void ReadCrossings(");
        AssertContains(crossings, "CrossingSource.Matches(", "crossings copied once per crossing list");

        // Every transform read is the position cache's miss path or a moving unit's climate cell.
        // The cache is reachable only through the Town, IIP and CBuilding overloads.
        string position = MethodBody(src, "private static Vector3 CachedStaticPosition(");
        AssertEqual(1, Count(position, "transform.position"), "one read on a cache miss");
        foreach (string typed in new[] { "PositionOf(Town town) => CachedStaticPosition(town);", "PositionOf(IIP iip) => CachedStaticPosition(iip);",
                                         "PositionOf(CBuilding building) => CachedStaticPosition(building);" })
            AssertContains(src, typed, "typed position overload");
        AssertEqual(4, Count(src, "CachedStaticPosition("), "only the three overloads reach the cache");
        int climate = 0;
        foreach (string line in src.Split('\n'))
            if (line.Contains("transform.position") && line.Contains("GetClimateData(")) climate++;
        AssertEqual(1, climate, "the climate cell under a moving unit is read live");
        AssertEqual(2, Count(src, "transform.position"), "no other transform read in the snapshot");
    }

    private static int Count(string text, string token)
    {
        int n = 0;
        for (int at = text.IndexOf(token, StringComparison.Ordinal); at >= 0; at = text.IndexOf(token, at + token.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    // A railroad line as vanilla holds it: IIPsForTransportCapacityUpdate, nulls already dropped
    // (ids and positions in array order). Lines share junction IIPs; some lines are empty.
    private sealed class FixtureLine
    {
        internal int[] Ids;
        internal float[] X, Z;
    }

    private static List<FixtureLine> RandomLines(Random random, int count)
    {
        var lines = new List<FixtureLine>();
        for (int i = 0; i < count; i++)
        {
            int n = i % 9 == 0 ? 0 : 1 + random.Next(25);
            var line = new FixtureLine { Ids = new int[n], X = new float[n], Z = new float[n] };
            for (int k = 0; k < n; k++)
            {
                line.Ids[k] = random.Next(250);
                line.X[k] = (float)Math.Round(random.NextDouble() * 1000.0, 1);
                line.Z[k] = (float)Math.Round(random.NextDouble() * 1000.0, 1);
            }
            lines.Add(line);
        }
        return lines;
    }

    private static RailGeometry BuildRails(List<FixtureLine> lines)
    {
        var rails = new RailGeometry();
        foreach (var line in lines)
        {
            rails.BeginLine();
            for (int k = 0; k < line.Ids.Length; k++)
                rails.AddIip(line.Ids[k], line.X[k], line.Z[k]);
            rails.EndLine();
        }
        return rails;
    }

    // The G0 runtime's per-review scans, kept here as the uncached reference.
    private static int LinesNearDirect(List<FixtureLine> lines, float x, float z, float radius)
    {
        if (radius <= 0f) return 0;
        float radiusSq = radius * radius;
        int count = 0;
        foreach (var line in lines)
        {
            for (int k = 0; k < line.Ids.Length; k++)
            {
                float dx = line.X[k] - x, dz = line.Z[k] - z;
                if (dx * dx + dz * dz > radiusSq) continue;
                count++;
                break;
            }
        }
        return count;
    }

    private static int LinesOfIipDirect(List<FixtureLine> lines, int id)
    {
        int count = 0;
        foreach (var line in lines)
            for (int k = 0; k < line.Ids.Length; k++)
                if (line.Ids[k] == id) count++;
        return count;
    }

    private static void EndpointsDirect(FixtureLine line, ref RailLineRecord record)
    {
        float best = -1f;
        for (int a = 0; a < line.Ids.Length; a++)
        {
            for (int b = a; b < line.Ids.Length; b++)
            {
                float dx = line.X[a] - line.X[b], dz = line.Z[a] - line.Z[b];
                float d = dx * dx + dz * dz;
                if (d <= best) continue;
                best = d;
                record.AX = line.X[a];
                record.AZ = line.Z[a];
                record.BX = line.X[b];
                record.BZ = line.Z[b];
                record.AIipId = line.Ids[a];
                record.BIipId = line.Ids[b];
            }
        }
    }
}
