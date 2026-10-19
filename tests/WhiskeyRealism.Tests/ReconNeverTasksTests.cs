using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;

using static TestHarness;

// The smoke of 2026-10-17 (docs/loop notes build-recon-never-tasks): the Union's [Recon] row read pool=2..7 unwatchedApproaches=3
// screens=probes=advanceGuards=0 on every review, its mass stayed staged ("scouted=no staged=yes") and its mover deferred one
// formation on all 22 reviews. The fixture below is that shape with vanilla's real prefs (aiminimumstrengthformovement 2000,
// morale 0.65, relocation range 200, dominance 0.65 from Config/campaignprefs.txt): two enemy objectives (30 West at 300, 31 East
// at 900), one ready body in no sector that needs a hold (it is in vanilla's capital-defence list, so it cannot scout), unready
// and busy formations that already cover the West and East needs, and one ready 3000-man formation that the East hold reserve
// keeps: the only formation that could scout is the one the hold reserve claimed first.
internal static class ReconNeverTasksTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("EveryUnfilledTaskCarriesAReason", EveryUnfilledTaskCarriesAReason),
            ("ARefusedTaskKeepsAReasonOfItsOwn", ARefusedTaskKeepsAReasonOfItsOwn),
            ("ClaimedNamesAMultiWordRoleInKebabCase", ClaimedNamesAMultiWordRoleInKebabCase),
            ("ARecalledFormationIsNeitherFreeNorUnsuited", ARecalledFormationIsNeitherFreeNorUnsuited),
            ("AMissRowNamesTheRoleTheMoveWasSentFor", AMissRowNamesTheRoleTheMoveWasSentFor),
            ("ANameWithASpaceSurvivesTheParser", ANameWithASpaceSurvivesTheParser),
            ("EveryPlannerRowThatCarriesANameQuotesIt", EveryPlannerRowThatCarriesANameQuotesIt),
            ("NoneAbleNamesTheFormationsThatAreTooWeak", NoneAbleNamesTheFormationsThatAreTooWeak),
            ("FreeIsCountedAtTheDetachmentStandard", FreeIsCountedAtTheDetachmentStandard),
            ("ClaimedNamesTheRoleThatHoldsTheAbleFormations", ClaimedNamesTheRoleThatHoldsTheAbleFormations),
            ("ARefusedTaskIsToldAsTheRefusalAlone", ARefusedTaskIsToldAsTheRefusalAlone),
            ("GaveUpNamesTheTaskItsFormationGaveUpOn", GaveUpNamesTheTaskItsFormationGaveUpOn),
            ("FreeIsThePoolLessWhatARoleHadClaimed", FreeIsThePoolLessWhatARoleHadClaimed),
            ("AMissedMoveNamesItsFormationAndTarget", AMissedMoveNamesItsFormationAndTarget),
            ("TheSmokeShapeHasNothingFreeToScoutWith", TheSmokeShapeHasNothingFreeToScoutWith),
            ("AStagedMainEffortGetsOneScoutFromTheHoldReserve", AStagedMainEffortGetsOneScoutFromTheHoldReserve),
            ("TheHoldReserveIsDrawnOnceNotPerTask", TheHoldReserveIsDrawnOnceNotPerTask),
            ("ANonMainTaskNeverDrawsTheHoldReserve", ANonMainTaskNeverDrawsTheHoldReserve),
            ("AScoutedMainEffortDrawsNothing", AScoutedMainEffortDrawsNothing),
            ("TheLastMassBodyIsNeverSpentOnScouting", TheLastMassBodyIsNeverSpentOnScouting),
            ("TheDrawnScoutKeepsItsPostTheNextReview", TheDrawnScoutKeepsItsPostTheNextReview),
            ("AGaveUpFormationIsNotDrawnForTheTaskItGaveUpOn", AGaveUpFormationIsNotDrawnForTheTaskItGaveUpOn),
            ("TheDrawServesTheMainObjectivesTasksNotThePostureOrBeliefTasks", TheDrawServesTheMainObjectivesTasksNotThePostureOrBeliefTasks),
            ("ASecessionGarrisonIsNeverDrawnAsAScout", ASecessionGarrisonIsNeverDrawnAsAScout),
            ("TheRefusalIsClearedWhenAFreeFormationFillsALaterMainTask", TheRefusalIsClearedWhenAFreeFormationFillsALaterMainTask),
            ("TheDrawNeverOpensAShortfallAgainstABelievedArmy", TheDrawNeverOpensAShortfallAgainstABelievedArmy),
            ("ABelievedSectorThatStillCoversItsNeedGivesAScout", ABelievedSectorThatStillCoversItsNeedGivesAScout),
            ("ADrawThatLeavesExactlyTheNeedIsAllowed", ADrawThatLeavesExactlyTheNeedIsAllowed),
            ("AMarchingCoveringMemberStillHoldsItsSectorForTheDraw", AMarchingCoveringMemberStillHoldsItsSectorForTheDraw),
            ("APresumptionOnlySectorGivesAScoutAndNamesItsCarrier", APresumptionOnlySectorGivesAScoutAndNamesItsCarrier),
            ("OneStandingScoutPerMainEffortAcrossReviews", OneStandingScoutPerMainEffortAcrossReviews),
            ("AScoutAlreadyOutForTheMainObjectiveBlocksAFurtherDraw", AScoutAlreadyOutForTheMainObjectiveBlocksAFurtherDraw),
            ("AScoutOutForAnotherTaskDoesNotBlockTheDraw", AScoutOutForAnotherTaskDoesNotBlockTheDraw),
            ("AClearedMissionDoesNotBlockTheDraw", AClearedMissionDoesNotBlockTheDraw),
            ("AChallengerObjectiveIsComparedWithTheCurrentObjectivesBestRecord", AChallengerObjectiveIsComparedWithTheCurrentObjectivesBestRecord),
            ("ATriggerKeepsTheKeptRecordWhileTheObjectiveIsStillBest", ATriggerKeepsTheKeptRecordWhileTheObjectiveIsStillBest),
            ("AnUnreadyHolderCountsTowardTheSectorsCoverBeforeADraw", AnUnreadyHolderCountsTowardTheSectorsCoverBeforeADraw),
            ("ACapitalGuardCountsTowardTheSectorsCoverBeforeADraw", ACapitalGuardCountsTowardTheSectorsCoverBeforeADraw),
            ("AnUnreadyHolderTakenAsDepotBuilderIsNoLongerCounted", AnUnreadyHolderTakenAsDepotBuilderIsNoLongerCounted),
            ("AnUnreadyHolderPledgedToAGarrisonOutsideItsStateIsNoLongerCounted", AnUnreadyHolderPledgedToAGarrisonOutsideItsStateIsNoLongerCounted),
            ("AnOrdinaryGarrisonPostTakesNoUnreadyHolder", AnOrdinaryGarrisonPostTakesNoUnreadyHolder),
            ("AReservedFormationPinnedAsABuilderIsNoLongerCounted", AReservedFormationPinnedAsABuilderIsNoLongerCounted),
            ("ACapitalGuardBoundForAnotherSectorCoversNothingWhereItStands", ACapitalGuardBoundForAnotherSectorCoversNothingWhereItStands),
            ("ACapitalSectorHeldByItsGuardListsNoShortfall", ACapitalSectorHeldByItsGuardListsNoShortfall),
            ("ABelievedTermNearTheNeedButUnderItIsStillPresumptionBacked", ABelievedTermNearTheNeedButUnderItIsStillPresumptionBacked),
            ("TheCarrierIsTheNearestImmobileHolder", TheCarrierIsTheNearestImmobileHolder),
            ("ASectorWithAStaleBeliefAndAPresumptionIsPresumptionBacked", ASectorWithAStaleBeliefAndAPresumptionIsPresumptionBacked),
            ("ScoutsMainObjectiveNamesTheMainTasksOnly", ScoutsMainObjectiveNamesTheMainTasksOnly),
            ("TheUnfilledCountMatchesTasksByKindPointAndReason", TheUnfilledCountMatchesTasksByKindPointAndReason),
            ("TheReconRowCountsTasksAndUnfilled", TheReconRowCountsTasksAndUnfilled),
            ("AScoutDrawnFromTheReserveReturnsWhenTheObjectiveIsScouted", AScoutDrawnFromTheReserveReturnsWhenTheObjectiveIsScouted),
            ("ARetargetWithinTheObjectiveIsNotScoutedByTheOldTarget", ARetargetWithinTheObjectiveIsNotScoutedByTheOldTarget),
            ("RecordsWhoseScoresAlternateStayOnOneRecord", RecordsWhoseScoresAlternateStayOnOneRecord),
            ("ARecordChangesAfterTheMarginHoldsForThreeReviews", ARecordChangesAfterTheMarginHoldsForThreeReviews),
            ("AnInterruptedLeadRestartsTheRecordWindow", AnInterruptedLeadRestartsTheRecordWindow),
            ("AnInvalidKeptRecordIsLeftAtOnce", AnInvalidKeptRecordIsLeftAtOnce),
            ("DifferentChallengersDoNotAddUp", DifferentChallengersDoNotAddUp),
            ("RecordsThatDifferOnlyInZAreDistinctRecords", RecordsThatDifferOnlyInZAreDistinctRecords),
            ("ALeadWithinTheMarginBreaksTheRecordWindow", ALeadWithinTheMarginBreaksTheRecordWindow),
            ("TheKeptRecordHasTheOddsDeadband", TheKeptRecordHasTheOddsDeadband),
            ("TheScoutedLatchFollowsTheKeptRecord", TheScoutedLatchFollowsTheKeptRecord),
            ("TheKeptRecordAndItsChallengerSurviveTheSidecar", TheKeptRecordAndItsChallengerSurviveTheSidecar),
            ("TheScoutedTargetSurvivesTheSidecarAndAnOldFileHasNone", TheScoutedTargetSurvivesTheSidecarAndAnOldFileHasNone),
            ("TheMoverRowNamesAStagedGatheringThatWaits", TheMoverRowNamesAStagedGatheringThatWaits),
            ("AZeroCountedFailureIsNotAFailureRow", AZeroCountedFailureIsNotAFailureRow),
        });
    }

    // ---- smoke 2026-10-18: tasks went unfilled with why=- and moves gave up without a name ----

    // A day built from the real planner, with the tasks narrowed, the gave-up moves or the ledger given, and the plan amended before the allocation.
    private static Day Allocated(CampaignSnapshot s, Func<List<ReconTask>, List<ReconTask>> narrow = null, List<GaveUpMove> gaveUp = null,
                                 ReconMissionLedger ledger = null, Action<TheaterPlan> amend = null, IReadOnlyList<HeldBuilder> held = null)
    {
        ledger = ledger ?? new ReconMissionLedger();
        var sectors = ExecutorFixtures.Sectors(s);
        var day = new Day { Ledger = ledger, S = s };
        ledger.OnReview(s, null);
        day.Plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, null, held);   // the table is the plan's: the held builders go to both
        amend?.Invoke(day.Plan);
        var all = new List<ReconTask>();
        ReconStep.Plan(s, sectors, day.Plan, new List<BattleHistoryRecord>(), 0, ledger, all);
        day.Tasks = narrow != null ? narrow(all) : all;
        day.Allocation = TestClaims.Allocate(day.Plan, s, sectors, null, day.Tasks, ledger, gaveUp, held);
        ledger.Sync(s, day.Tasks, day.Allocation);
        return day;
    }

    private static List<ReconTask> MainTasks(List<ReconTask> all)
    {
        return all.FindAll(task => ReconPlanner.ScoutsMainObjective(task));
    }

    private static CampaignSnapshot EastWithAWeakFormation()
    {
        var s = SmokeUnion();
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 12000f));
        s.Own.Add(Corps(40, 150f, 1500f));      // too weak for a screen, fine for an advance guard (a quarter of the 9563 body)
        return s;
    }

    // One ready formation, claimed by the role the world gives it, and a screen task nobody else can take.
    private static List<ReconTask> OneScreen(float x)
    {
        return new List<ReconTask> { new ReconTask { Kind = ReconKind.Screen, SectorId = 0, FromX = x, FromZ = 0f, HoldX = x, HoldZ = 0f, Priority = 5f, Reason = "belief" } };
    }

    // Every recon task with no scout says why: the row's why= is never empty while unfilled is not, in every world the file builds.
    private static void EveryUnfilledTaskCarriesAReason()
    {
        var worlds = new List<(string name, Day day)>
        {
            ("smoke shape, one reserve", Review(SmokeUnion())),
            ("no reserve, the body is the pool", Review(SmokeUnion(bodyFlags: 0, withReserve: false))),
            ("a believed army of 12000", EastWithArmy(12000f)),
            ("a believed army of 16000", EastWithArmy(16000f)),
            ("an unready holder", Review(EastWithAnUnreadyHolder())),
            ("a weak formation, every task", Allocated(EastWithAWeakFormation())),
            ("a weak formation, the main objective's tasks", Allocated(EastWithAWeakFormation(), MainTasks)),
            ("no weak formation, the main objective's tasks", Allocated(SmokeUnionWithAnArmy(), MainTasks)),
            ("a depot task taking the ready formation", DepotTaskKeepingTheReadyFormation()),
        };
        foreach (var (name, day) in worlds)
        {
            var counts = ReconStep.CountsOf(day.S, day.Ledger, day.Tasks, day.Allocation);
            AssertTrue(counts.Unfilled == 0 || (counts.Why != null && counts.Why.Length > 0), name + ": unfilled tasks and the row says why: " + counts.Row(0));
            AssertTrue(counts.Free + counts.Mass <= counts.Pool, name + ": the formations still free are a part of the pool: " + counts.Row(0));
        }
        AssertTrue(ReconStep.CountsOf(worlds[0].day.S, worlds[0].day.Ledger, worlds[0].day.Tasks, worlds[0].day.Allocation).Unfilled > 0, "the invariant is not vacuous: tasks stay unfilled");
        AssertTrue(ReconStep.CountsOf(worlds[5].day.S, worlds[5].day.Ledger, worlds[5].day.Tasks, worlds[5].day.Allocation).Unfilled > 0, "nor with the weak formation");
    }

    private static CampaignSnapshot SmokeUnionWithAnArmy()
    {
        var s = SmokeUnion();
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 12000f));
        return s;
    }

    // F9: a depot builder is new only from a sector's surplus, and the one ready formation is no surplus; the task's own builder, kept as its
    // incumbent (a Marching task, exempt from the surplus test), is what takes the ready formation.
    private static Day DepotTaskKeepingTheReadyFormation()
    {
        var s = OneReadyFormation();
        int sector = ExecutorFixtures.Sectors(s).SectorAt(330f, 0f);
        var incumbent = new[] { new HeldBuilder { InstanceId = 1, SectorId = sector, Wished = false, SiteX = 340f, SiteZ = 0f, TargetX = 340f, TargetZ = 0f } };
        return Allocated(s, all => OneScreen(300f), held: incumbent,
                         amend: plan => plan.DepotTasks.Add(new DepotTask { X = 340f, Z = 0f, SectorId = sector, Priority = 1f }));
    }

    private static CampaignSnapshot OneReadyFormation()
    {
        var s = Base();
        s.Own.Add(CapitalGuard());
        s.Own.Add(Corps(1, 330f, 4000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 12000f));
        return s;
    }

    // Verify round 4's probe: the draw is refused for the main-axis screen, then the weak formation fills the advance guard. The refused task
    // is told as covered, not left without a reason (tasks=2 unfilled=1 why=-).
    private static void ARefusedTaskKeepsAReasonOfItsOwn()
    {
        var control = Allocated(SmokeUnionWithAnArmy(), MainTasks);
        var controlCounts = ReconStep.CountsOf(control.S, control.Ledger, control.Tasks, control.Allocation);
        AssertEqual(ForceAllocator.ReasonScoutAgainstThreat, controlCounts.Why, "control, no weak formation: the refusal alone: " + controlCounts.Row(0));
        var day = Allocated(EastWithAWeakFormation(), MainTasks);
        var counts = ReconStep.CountsOf(day.S, day.Ledger, day.Tasks, day.Allocation);
        AssertEqual(2, counts.Tasks, "two tasks see the main objective: " + counts.Row(0));
        AssertEqual(1, counts.Unfilled, "the weak formation filled one: " + counts.Row(0));
        AssertEqual(ForceAllocator.ReasonUnfilledMainCovered, counts.Why, "the other, refused its draw, says the main objective is covered: " + counts.Row(0));
        AssertContains(counts.Row(0), " why=main-covered ", "in the row");
    }

    // The role a claimed formation holds is written in kebab case, as the hold reserve's own reason is: a two-word role is not run together.
    private static void ClaimedNamesAMultiWordRoleInKebabCase()
    {
        var depot = DepotTaskKeepingTheReadyFormation();
        AssertEqual(FormationRole.DepotBuilder, Of(depot.Allocation, 1).Role, "the only ready formation is the depot task's builder (its kept incumbent)");
        AssertEqual(ForceAllocator.ReasonUnfilledClaimedPrefix + "depot-builder", depot.Allocation.ReconUnfilledWhy, "and the screen says so");

        var g = Base();
        g.CapitalX = 760f;
        g.CapitalZ = 0f;
        g.Own.Add(Corps(31, 755f, 6000f));
        g.Beliefs.Add(ExecutorFixtures.Belief(900f, 0f, 12000f));
        var guard = Allocated(g, all => OneScreen(800f));
        AssertEqual(FormationRole.CapitalGuard, Of(guard.Allocation, 31).Role, "the only ready formation guards the capital");
        AssertEqual(ForceAllocator.ReasonUnfilledClaimedPrefix + "capital-guard", guard.Allocation.ReconUnfilledWhy, "and the screen says so");
    }

    // A formation on its way back to the main body is skipped by the pick: it is not a free scout, and the task says why it has none.
    private static void ARecalledFormationIsNeitherFreeNorUnsuited()
    {
        var s = OneReadyFormation();
        var f = s.Own[s.IndexOfOwn(1)];
        var ledger = new ReconMissionLedger();
        ledger.Restore(new[]
        {
            new ReconMission
            {
                MissionId = 1, Kind = ReconKind.Screen, InstanceId = 1, UnitKey = f.UnitKey, HoldX = 350f, HoldZ = 0f, SectorId = 0,
                State = ReconState.Recalled, IssuedDay = s.DaySerial - 1, Reason = "belief", ObservedEnemyStrength = -1f, ObservedObjectiveId = -1, ClearedDay = -1
            }
        });
        var day = Allocated(s, all => OneScreen(300f), ledger: ledger);
        var counts = ReconStep.CountsOf(day.S, day.Ledger, day.Tasks, day.Allocation);
        AssertEqual(1, counts.Pool, "the pool counts it: " + counts.Row(0));
        AssertEqual(0, counts.Free + counts.Mass, "but it is not free: " + counts.Row(0));
        AssertEqual(ForceAllocator.ReasonUnfilledRecalling, counts.Why, "the task says its only able formation is on its way back: " + counts.Row(0));
    }

    // A miss row names the role the move was sent for, from the move record: the allocation of the review that judges it may have given the
    // formation another role.
    private static void AMissRowNamesTheRoleTheMoveWasSentFor()
    {
        var host = new FakeMoverHost();
        var tracker = new MoveTracker();
        var state = ExecutorFixtures.State();
        Func<int, int[], MoverTally> day = (offset, mass) =>
        {
            var s = ExecutorFixtures.Wide(offset);
            s.Own.Add(ExecutorFixtures.Corps(1, 350f, 6000f));
            s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
            var plan = ExecutorFixtures.HandPlan(s, mass);
            var alloc = TestClaims.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
            return CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), host, tracker, state);
        };
        AssertEqual(1, day(0, new[] { 1 }).Issued, "sent as the main effort");
        var second = day(1, new int[0]);          // the mass is gone: today's allocation holds the formation
        AssertEqual(1, second.Failed, "the move missed");
        AssertContains(second.MissRows[0], " role=MainEffort ", "and the row says what it was sent for, not what it is today: " + second.MissRows[0]);
    }

    // A name goes into a row quoted wherever the planner writes one (the parser ends an unquoted value at a space): the sweep reads every
    // planner source for a name field set from anything but TelemetryLegacyParser.Quote. from= is also a coordinate in the miss row.
    private static void EveryPlannerRowThatCarriesANameQuotesIt()
    {
        var offenders = new List<string>();
        // from=fromX/fromZ are numbers, and [EnemyPicture]'s displaced= is a count of anchors (plan F3's smoke row), not a name.
        var rx = new System.Text.RegularExpressions.Regex("\" ?(unit|commander|displaced|post|builder|reseated|to|from)=\" \\+ (?!TelemetryLegacyParser\\.Quote\\()(?!fromX)(?!p\\.DisplacedCount)");
        int rows = 0;
        foreach (string path in System.IO.Directory.GetFiles(TestFixtures.FindRepoPath("src/WhiskeyRealism/Strategic/Planner"), "*.cs"))
        {
            string text = System.IO.File.ReadAllText(path);
            rows += System.Text.RegularExpressions.Regex.Matches(text, "\" ?(unit|commander|displaced|post|builder|reseated|to|from)=\" \\+ ").Count;
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(text)) offenders.Add(System.IO.Path.GetFileName(path) + ": " + m.Value);
        }
        AssertTrue(rows >= 10, "the sweep sees the name fields (" + rows + ")");
        AssertEqual(0, offenders.Count, "unquoted name fields: " + string.Join("; ", offenders));
    }

    // A name with a space is written quoted, so the parser keeps all of it (smoke 2026-10-18: "1st Maine Infantry" read "1st").
    private static void ANameWithASpaceSurvivesTheParser()
    {
        AssertEqual("10th", TelemetryLegacyParser.Quote("10th"), "a plain value is left as it is");
        AssertEqual("\"10th Indiana:5\"", TelemetryLegacyParser.Quote("10th Indiana:5"), "a name with a space is quoted");
        AssertEqual("-", TelemetryLegacyParser.Quote(null), "nothing is a dash");
        AssertEqual("\"a \\\"b\\\" c\\\\d\"", TelemetryLegacyParser.Quote("a \"b\" c\\d"), "quotes and backslashes are escaped");
        foreach (string name in new[] { "10th Indiana:5", "a \"b\" c\\d", "1st Maine Infantry", "plain" })
        {
            AssertTrue(TelemetryLegacyParser.TryParse("[CampaignMoverMiss] alliance=0 unit=" + TelemetryLegacyParser.Quote(name) + " role=MainEffort outcome=failed",
                TelemetryProfile.CampaignTuning, "s", out var ev), "parsed: " + name);
            AssertEqual(name, ev.Unit, "the unit survives: " + name);
            AssertEqual("MainEffort", ev.Fields.GetString("role"), "and the field after it is its own");
        }
        var host = new FakeMoverHost();
        var tracker = new MoveTracker();
        var state = ExecutorFixtures.State();
        for (int offset = 0; offset < 2; offset++)
        {
            var s = ExecutorFixtures.Wide(offset);
            var named = ExecutorFixtures.Corps(1, 350f, 6000f);
            named.UnitKey = "10th Indiana:5";
            s.Own.Add(named);
            s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
            var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
            var alloc = TestClaims.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
            var tally = CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), host, tracker, state);
            if (offset == 0) continue;
            AssertEqual(1, tally.MissRows.Count, "a miss");
            AssertTrue(TelemetryLegacyParser.TryParse(tally.MissRows[0], TelemetryProfile.CampaignTuning, "s", out var miss), "the miss row parses");
            AssertEqual("10th Indiana:5", miss.Unit, "the unit of the miss row is whole");
        }
        var row = GarrisonRows.Transition(1, new GarrisonTransition { Name = "Fort Monroe", Kind = GarrisonPostKind.Fort, Unit = "10th Indiana", State = GarrisonPostState.Holding, Need = 1f, Existing = 0f, Short = 0f });
        AssertTrue(TelemetryLegacyParser.TryParse(row, TelemetryProfile.CampaignTuning, "s", out var post), "the garrison post row parses");
        AssertEqual("10th Indiana", post.Unit, "its unit is whole");
        AssertEqual("Fort Monroe", post.Fields.GetString("post"), "and its post");
        var plan2 = new TheaterPlan { Alliance = 0, MainObjectiveId = -1, ReinforcementInstanceId = 7, ReinforcementName = "10th Indiana", ReinforcementFrom = Theater.East, ReinforcementTo = Theater.West };
        AssertTrue(TelemetryLegacyParser.TryParse(TheaterPlanner.Summary(plan2), TelemetryProfile.CampaignTuning, "s", out var planRow), "the plan row parses");
        AssertEqual("10th Indiana:East>West", planRow.Fields.GetString("xdept"), "the reinforcement of the plan row is whole");
    }

    // Each reason is the cause it names, one world each. A union of ready formations too weak for a screen (2000 is vanilla's minimum for an
    // offensive operation; a detachment needs only a body): the pool is not empty and nothing can take a screen.
    private static void NoneAbleNamesTheFormationsThatAreTooWeak()
    {
        var s = Base();
        s.Own.Add(Corps(1, 100f, 1500f));
        s.Own.Add(Corps(2, 150f, 1500f));
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 12000f));
        var day = Review(s);
        var counts = ReconStep.CountsOf(day.S, day.Ledger, day.Tasks, day.Allocation);
        AssertTrue(counts.Tasks > 0 && counts.Unfilled == counts.Tasks, "tasks exist and none is filled: " + counts.Row(0));
        AssertTrue(counts.Pool > 0, "the pool is not empty: " + counts.Row(0));
        AssertContains(counts.Row(0), " why=" + ForceAllocator.ReasonUnfilledNoneAble + " ", "too weak for a screen is the reason: " + counts.Row(0));
    }

    // free= is read at the detachment standard (a body is enough), as the pool is: two ready 1500-man formations nothing has claimed are free
    // although no screen can take them, and the tasks that need a screen say none-able.
    private static void FreeIsCountedAtTheDetachmentStandard()
    {
        var s = Base();
        s.Own.Add(Corps(1, 600f, 1500f));
        s.Own.Add(Corps(2, 610f, 1500f));
        var plan = ExecutorFixtures.HandPlan(s, new int[0]);       // no mass and no sector that needs a hold: nothing claims them
        var tasks = OneScreen(600f);
        tasks.Add(new ReconTask { Kind = ReconKind.Screen, SectorId = 0, FromX = 620f, FromZ = 0f, HoldX = 620f, HoldZ = 0f, Priority = 4f, Reason = "belief" });
        var ledger = new ReconMissionLedger();
        var alloc = TestClaims.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, tasks, ledger);
        ledger.Sync(s, tasks, alloc);
        var counts = ReconStep.CountsOf(s, ledger, tasks, alloc);
        AssertEqual(2, counts.Pool, "both are the pool: " + counts.Row(0));
        AssertEqual(2, counts.Free, "and nothing claimed them: " + counts.Row(0));
        AssertEqual(0, counts.Mass, "neither is the mass's: " + counts.Row(0));
        AssertEqual(2, counts.Unfilled, "no screen can take a body: " + counts.Row(0));
        AssertEqual(ForceAllocator.ReasonUnfilledNoneAble, counts.Why, "so the tasks say none-able: " + counts.Row(0));
    }

    private static void ClaimedNamesTheRoleThatHoldsTheAbleFormations()
    {
        var day = EastWithArmy(12000f);
        AssertContains(day.Allocation.ReconUnfilledWhy, ForceAllocator.ReasonUnfilledClaimedPrefix + ForceAllocator.ReasonHoldReserve,
            "the one formation that could go is the East's hold reserve, which only the main objective's tasks may draw: " + day.Allocation.ReconUnfilledWhy);
    }

    // A task refused its draw is told as the refusal alone, not also as a claim: the main objective's tasks only, every one refused.
    private static void ARefusedTaskIsToldAsTheRefusalAlone()
    {
        var s = SmokeUnion();
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 12000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        var all = new List<ReconTask>();
        ReconStep.Plan(s, sectors, plan, new List<BattleHistoryRecord>(), 0, new ReconMissionLedger(), all);
        var main = new List<ReconTask>();
        foreach (var task in all) if (ReconPlanner.ScoutsMainObjective(task)) main.Add(task);
        AssertTrue(main.Count >= 1, "a task sees the main objective");
        var alloc = TestClaims.Allocate(plan, s, sectors, null, main, new ReconMissionLedger());
        AssertEqual(ForceAllocator.ReasonScoutAgainstThreat, alloc.ReconUnfilledWhy, "the refusal alone");
    }

    private static void GaveUpNamesTheTaskItsFormationGaveUpOn()
    {
        var s = SmokeUnion();
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        var tasks = new List<ReconTask>();
        ReconStep.Plan(s, sectors, plan, new List<BattleHistoryRecord>(), 0, new ReconMissionLedger(), tasks);
        var gaveUp = new List<GaveUpMove>();
        foreach (var task in tasks) gaveUp.Add(new GaveUpMove { InstanceId = 2, TargetX = task.HoldX, TargetZ = task.HoldZ, ObjectiveId = -1 });
        var alloc = TestClaims.Allocate(plan, s, sectors, null, tasks, new ReconMissionLedger(), gaveUp);
        AssertContains(alloc.ReconUnfilledWhy, ForceAllocator.ReasonUnfilledGaveUp, "the only formation that could go gave up on every post: " + alloc.ReconUnfilledWhy);
    }

    // The pool is the snapshot's count before any role; free= is what is left when the recon step begins. Here every ready formation is the
    // East's hold reserve or the main effort's, so the pool is not empty and nothing is free.
    private static void FreeIsThePoolLessWhatARoleHadClaimed()
    {
        var day = EastWithArmy(12000f);
        var counts = ReconStep.CountsOf(day.S, day.Ledger, day.Tasks, day.Allocation);
        AssertTrue(counts.Pool >= 1, "the pool is not empty: " + counts.Row(0));
        AssertEqual(0, counts.Free, "but a role claimed it: " + counts.Row(0));
        // A quiet map: nothing is claimed, so every ready formation is free or the main effort's mass, and the two are told apart.
        var open = Base();
        open.Own.Add(CapitalGuard());
        for (int i = 1; i <= 4; i++) open.Own.Add(Corps(i, 100f + 20f * i, 4000f));
        var calm = Review(open);
        var calmCounts = ReconStep.CountsOf(calm.S, calm.Ledger, calm.Tasks, calm.Allocation);
        AssertEqual(4, calmCounts.Pool, "a quiet map: all four are the pool: " + calmCounts.Row(0));
        AssertEqual(4, calmCounts.Free + calmCounts.Mass, "each is free or the mass's: " + calmCounts.Row(0));
        AssertTrue(calmCounts.Free > 0, "some are outside the mass: " + calmCounts.Row(0));
        AssertEqual(calm.Plan.MassInstanceIds.Count, calmCounts.Mass, "and the mass's bodies are counted apart: " + calmCounts.Row(0));
        AssertTrue(calmCounts.Mass > 0, "there is a mass: " + calmCounts.Row(0));
    }

    // A move judged a miss names the formation, its role and where it was sent, so a give-up is no longer a count alone.
    private static void AMissedMoveNamesItsFormationAndTarget()
    {
        var host = new FakeMoverHost();
        var tracker = new MoveTracker();
        var state = ExecutorFixtures.State();
        Func<int, MoverTally> day = offset =>
        {
            var s = ExecutorFixtures.Wide(offset);
            s.Own.Add(ExecutorFixtures.Corps(1, 350f, 6000f));
            s.Own.Add(ExecutorFixtures.Corps(2, 360f, 5000f));
            s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
            var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2 });
            var alloc = TestClaims.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
            return CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), host, tracker, state);
        };
        var first = day(0);
        AssertEqual(2, first.Issued, "both bodies are sent");
        AssertEqual(0, first.MissRows.Count, "and nothing has been judged yet");
        var second = day(1);          // the fake host never moves a formation: its waypoint stays where it was
        AssertEqual(2, second.Failed, "both missed");
        AssertEqual(2, second.MissRows.Count, "and each miss has its row");
        AssertContains(second.MissRows[0], "[CampaignMoverMiss] alliance=0 unit=", "the row names the unit");
        AssertContains(second.MissRows[0], " role=MainEffort outcome=failed ", "its role and the outcome");
        AssertContains(second.MissRows[0], " retries=0 ", "the first miss");
        var missRoute = TelemetryTagPolicy.Route(second.MissRows[0]);
        AssertEqual("CampaignMoverMiss", missRoute.EventName, "the router knows the row");
        AssertEqual(TelemetryCategory.Failure, missRoute.Category, "and files a failed move as a failure");
        var third = day(2);
        AssertEqual(2, third.GaveUp, "the retry missed too");
        AssertEqual(2, third.MissRows.Count, "two rows again");
        AssertContains(third.MissRows[1], " outcome=gaveUp ", "now given up");
        AssertContains(third.MissRows[1], " retries=1 ", "after the retry");
        AssertContains(third.MissRows[1], " target=", "with the place it was sent to");
        AssertEqual(TelemetryCategory.Write, TelemetryTagPolicy.Route(third.MissRows[1]).Category, "a give-up is the tally's gaveUp, a Write row");
    }

    // ---- fixtures ----

    private static FormationRecord Corps(int id, float x, float strength, int readiness = 3, FormationFlags extra = 0)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.SightRadius = 30f;
        f.Morale = 0.9f;
        f.ReadinessStep = readiness;
        f.Flags |= extra;
        return f;
    }

    private static CampaignSnapshot Base()
    {
        var s = ExecutorFixtures.Line(0, 11);
        s.MinStrengthForMovement = 2000f;
        s.MinMoraleForMovement = 0.65f;
        s.RelocationRange = 200f;
        s.OffensiveDominance = 0.65f;
        for (int i = 1; i <= 8; i++) s.Supply.Add(ExecutorFixtures.Iip(600 + i, 100f * i, 0));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 30, X = 300f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.2f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 31, X = 900f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.8f });
        return s;
    }

    // D11: the capital's floor is never under the minimum strength, so every world keeps a guard at the capital. It stands in vanilla's
    // capital-defence list, as the smoke's body does: CanMove refuses it as a scout, so it adds nothing to the scout pool.
    private static FormationRecord CapitalGuard()
    {
        return Corps(900, 0f, 5600f, 3, FormationFlags.CapitalDefense);
    }

    // Body 1: ready, 9563 strong, in vanilla's capital-defence list (CanMove refuses it as a scout; the mass takes it). Unready 9, 10 and
    // the busy 15 cover the West's need (6000); the busy 13, 14 cover two thirds of the East's (9000) and the ready 3000-man
    // formation 2 is the East hold reserve that completes it.
    // eastBusy is each busy East holder's strength. The smoke's East need was the unscouted floor 5000 x (1 + 0.8) = 9000, which two busy 3000s
    // and the ready 3000 completed. The floor is now the scenario's minimum (foresight F5: 2000 here, so 3600), and two busy 3000s would
    // cover it alone: the presumption-backed shapes use 1200 each (two thirds of 3600, the same share as before), and the shapes that set
    // the East need by a believed army, whose need does not depend on the floor, pass 3000 (two tests do).
    private static CampaignSnapshot SmokeUnion(FormationFlags bodyFlags = FormationFlags.CapitalDefense, bool withReserve = true, bool secondEastBusy = true,
                                               float eastBusy = 1200f)
    {
        var s = Base();
        s.Own.Add(CapitalGuard());
        s.Own.Add(Corps(1, 100f, 9563f, 3, bodyFlags));
        if (withReserve) s.Own.Add(Corps(2, 760f, 3000f));
        s.Own.Add(Corps(9, 200f, 4000f, 1));
        s.Own.Add(Corps(10, 200f + 20f, 4000f, 1));
        s.Own.Add(Corps(13, 790f, eastBusy, 3, FormationFlags.InBattle));
        if (secondEastBusy) s.Own.Add(Corps(14, 800f, eastBusy, 3, FormationFlags.InBattle));
        s.Own.Add(Corps(15, 150f, 5000f, 3, FormationFlags.InBattle));
        return s;
    }

    private struct Day
    {
        public TheaterPlan Plan;
        public List<ReconTask> Tasks;
        public Allocation Allocation;
        public ReconMissionLedger Ledger;
        public CampaignSnapshot S;
    }

    private static Day Review(CampaignSnapshot s, PlannerState state = null, ReconMissionLedger ledger = null, Action<TheaterPlan> amend = null,
                              IReadOnlyList<HeldBuilder> pinned = null)
    {
        ledger = ledger ?? new ReconMissionLedger();
        var sectors = ExecutorFixtures.Sectors(s);
        var day = new Day { Ledger = ledger, Tasks = new List<ReconTask>(), S = s };
        ledger.OnReview(s, null);
        day.Plan = TheaterPlanner.Plan(s, sectors, null, state ?? new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, null, pinned);
        amend?.Invoke(day.Plan);
        ReconStep.Plan(s, sectors, day.Plan, new List<BattleHistoryRecord>(), 0, ledger, day.Tasks);
        day.Allocation = TestClaims.Allocate(day.Plan, s, sectors, null, day.Tasks, ledger, null, pinned);
        ledger.Sync(s, day.Tasks, day.Allocation);
        return day;
    }

    // Whether the draw from the hold reserve was refused for a believed threat: the [Recon] row's why= names it (among the other reasons).
    private static bool Refused(Allocation a)
    {
        return a.ReconUnfilledWhy != null && Array.IndexOf(a.ReconUnfilledWhy.Split(','), ForceAllocator.ReasonScoutAgainstThreat) >= 0;
    }

    private static int Roles(Allocation a, FormationRole role)
    {
        int n = 0;
        foreach (var x in a.Assignments) if (x.Role == role) n++;
        return n;
    }

    // The Recon assignments that see the main objective (main-axis, probe, advance guard); a posture or approach screen is another scout.
    private static int MainScouts(Allocation a)
    {
        int n = 0;
        foreach (var x in a.Assignments)
            if (x.Role == FormationRole.Recon && (x.Reason == ReconPlanner.ReasonMainAxis || x.Reason == "probe" || x.Reason == "advance-guard")) n++;
        return n;
    }

    // The allocation as "id=Role/reason", for an assertion's label.
    private static string Describe(Allocation a)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var x in a.Assignments) sb.Append(x.InstanceId).Append('=').Append(x.Role).Append('/').Append(x.Reason).Append(' ');
        return sb.ToString();
    }

    private static Assignment Of(Allocation a, int id)
    {
        a.TryGet(id, out var x);
        return x;
    }

    // ---- the cause ----

    // The cause, pinned as the smoke saw it: tasks are planned (three, the main axis among them), the pool is not empty, the main
    // effort is staged, and nobody is sent, because the one formation that could go is the East hold reserve (already given its
    // role when the recon pick runs) and the body is in vanilla's capital-defence list. Without the draw this stays 0 screens.
    private static void TheSmokeShapeHasNothingFreeToScoutWith()
    {
        var s = SmokeUnion(withReserve: true);
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        AssertEqual(30, plan.MainObjectiveId, "the push is at the West objective");
        AssertTrue(plan.MainStaged, "unscouted: staged");
        AssertEqual(1, plan.MassInstanceIds.Count, "the mass is the one body");
        var tasks = new List<ReconTask>();
        ReconStep.Plan(s, sectors, plan, new List<BattleHistoryRecord>(), 0, new ReconMissionLedger(), tasks);
        AssertTrue(tasks.Count >= 3, "tasks are planned: " + tasks.Count);
        bool mainAxis = false;
        foreach (var t in tasks) if (t.Reason == ReconPlanner.ReasonMainAxis) mainAxis = true;
        AssertTrue(mainAxis, "the main axis is unwatched");
        AssertEqual(1, ReconPlanner.ScoutPool(s, null), "the pool counts the one ready formation that is not in a vanilla list");
        var held = TestClaims.Allocate(plan, s, sectors, null, null, null);
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(held, 2).Reason, "and the East hold reserve has it");
        AssertEqual(FormationRole.MainEffort, Of(held, 1).Role, "the body is the main effort");
    }

    // The fix: a staged main effort that has no free scout takes the smallest ready formation its hold reserve keeps, once.
    private static void AStagedMainEffortGetsOneScoutFromTheHoldReserve()
    {
        var day = Review(SmokeUnion());
        AssertTrue(day.Plan.MainStaged, "staged");
        var scout = Of(day.Allocation, 2);
        AssertEqual(FormationRole.Recon, scout.Role, "the reserve formation scouts");
        AssertTrue(scout.Reason == ReconPlanner.ReasonMainAxis || scout.Reason == "probe" || scout.Reason == "advance-guard", "for the main objective: " + scout.Reason);
        AssertEqual(1, Roles(day.Allocation, FormationRole.Recon), "and only one is drawn");
        AssertEqual(FormationRole.MainEffort, Of(day.Allocation, 1).Role, "the main effort keeps its body");
        AssertEqual(1, day.Ledger.Missions.Count, "one mission");
        AssertTrue(ReconMissionLedger.IsActive(day.Ledger.Missions[0].State), "and it is active");
        AssertTrue(scout.Withheld, "withheld from vanilla like any scout");
    }

    private static void TheHoldReserveIsDrawnOnceNotPerTask()
    {
        // The East's need is the scenario's floor, 3600 (2000 x 1.8; foresight F5). The one busy formation holds 100 of it, so the ready 2 (3000)
        // and the small 3 and 4 (400 each) are all hold reserve: 3900 against 3600.
        var s = SmokeUnion(secondEastBusy: false, eastBusy: 100f);
        s.Own.Add(Corps(3, 770f, 400f));
        s.Own.Add(Corps(4, 780f, 400f));
        var held = TestClaims.Allocate(ExecutorFixtures.RealPlan(s, new PlannerState()), s, ExecutorFixtures.Sectors(s), null, null, null);
        int reserveBefore = 0;
        foreach (var a in held.Assignments) if (a.Reason == ForceAllocator.ReasonHoldReserve) reserveBefore++;
        AssertEqual(3, reserveBefore, "the East keeps three ready formations");
        var east = Review(s);
        int reserve = 0;
        foreach (var a in east.Allocation.Assignments) if (a.Reason == ForceAllocator.ReasonHoldReserve) reserve++;
        AssertTrue(east.Tasks.Count >= 3, "there were several tasks: " + east.Tasks.Count);
        AssertEqual(1, Roles(east.Allocation, FormationRole.Recon), "one scout from the reserve however many main-objective tasks there are");
        AssertEqual(2, reserve, "the rest of the reserve keeps holding");
    }

    // The approach tasks (capital-approach, front-approach, belief, picket) are not a staged push's eyes: they never cost a sector its hold.
    private static void ANonMainTaskNeverDrawsTheHoldReserve()
    {
        var s = SmokeUnion();
        // The main objective is seen (a fresh belief at 30) but another army is believed somewhere no viewer covers.
        var near = ExecutorFixtures.Belief(310f, 0f, 1000f);
        s.Beliefs.Add(near);
        var day = Review(s);
        AssertTrue(day.Plan.MainScouted, "the main objective is scouted");
        AssertTrue(day.Tasks.Count > 0, "approach tasks still exist");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "none of them takes the hold reserve");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day.Allocation, 2).Reason, "which keeps holding");
    }

    private static void AScoutedMainEffortDrawsNothing()
    {
        var s = SmokeUnion();
        s.Beliefs.Add(ExecutorFixtures.Belief(310f, 0f, 1000f));
        var state = new PlannerState();
        var first = Review(s, state);
        var again = Review(s, state, first.Ledger);
        AssertFalse(again.Plan.MainStaged, "scouted, so not staged");
        AssertEqual(0, Roles(again.Allocation, FormationRole.Recon), "no draw on the second review either");
    }

    // The other half of the starvation: with no reserve to draw on and a body that is not in a vanilla list, the body itself was the only
    // formation the pick could return, and the main effort lost all of it to its own screen.
    private static void TheLastMassBodyIsNeverSpentOnScouting()
    {
        var s = SmokeUnion(bodyFlags: 0, withReserve: false);
        var day = Review(s);
        AssertEqual(1, day.Plan.MassInstanceIds.Count, "one body");
        AssertEqual(FormationRole.MainEffort, Of(day.Allocation, 1).Role, "it stays the main effort");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "and nobody scouts: there is nobody to send");
        AssertTrue(day.Tasks.Count > 0, "though the tasks exist");
    }

    // Reviews are daily and the reserve is rebuilt each time: the formation drawn yesterday is the incumbent today (no churn between
    // reserve formations, the mission is not cleared and re-opened).
    private static void TheDrawnScoutKeepsItsPostTheNextReview()
    {
        var s = SmokeUnion();
        var state = new PlannerState();
        var day1 = Review(s, state);
        int first = -1;
        foreach (var a in day1.Allocation.Assignments) if (a.Role == FormationRole.Recon) first = a.InstanceId;
        AssertTrue(first > 0, "a scout is drawn");
        int mission = day1.Ledger.Missions[0].MissionId;
        var s2 = SmokeUnion();
        var day2 = Review(s2, state, day1.Ledger);
        AssertEqual(FormationRole.Recon, Of(day2.Allocation, first).Role, "the same formation scouts again");
        AssertEqual(1, Roles(day2.Allocation, FormationRole.Recon), "and still only one");
        AssertEqual(mission, day2.Ledger.Missions[0].MissionId, "the mission was kept, not re-opened");
    }

    // The tracker gave up on sending the reserve formation to this task's post (two misses): the next candidate is tried, and nobody here.
    private static void AGaveUpFormationIsNotDrawnForTheTaskItGaveUpOn()
    {
        var s = SmokeUnion();
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        var tasks = new List<ReconTask>();
        ReconStep.Plan(s, sectors, plan, new List<BattleHistoryRecord>(), 0, new ReconMissionLedger(), tasks);
        var gaveUp = new List<GaveUpMove>();
        foreach (var task in tasks) gaveUp.Add(new GaveUpMove { InstanceId = 2, TargetX = task.HoldX, TargetZ = task.HoldZ, ObjectiveId = -1 });
        var alloc = TestClaims.Allocate(plan, s, sectors, null, tasks, new ReconMissionLedger(), gaveUp);
        AssertEqual(0, Roles(alloc, FormationRole.Recon), "the formation that gave up on every post is not drawn for any of them");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(alloc, 2).Reason, "it keeps holding");
    }

    // The reason is told only while the push has no eyes. A draw refused for the first main task is no reason once a free formation fills
    // a later one: the free 30 gave up on every other task, so the first main task is refused its draw and 30 takes the last.
    private static void TheRefusalIsClearedWhenAFreeFormationFillsALaterMainTask()
    {
        var s = SmokeUnion();
        s.Own.Add(Corps(30, 40f, 3000f));   // behind the body: the nearest formation with the scenario's minimum is the mass (foresight F5)
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 12000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        var tasks = new List<ReconTask>();
        ReconStep.Plan(s, sectors, plan, new List<BattleHistoryRecord>(), 0, new ReconMissionLedger(), tasks);
        var main = new List<ReconTask>();
        foreach (var task in tasks) if (ReconPlanner.ScoutsMainObjective(task)) main.Add(task);
        AssertTrue(main.Count >= 2, "two tasks see the main objective: " + main.Count);
        // 30 gave up on every task but the last main one, so the first main task is refused its draw and the last is filled by the pick.
        ReconTask last = main[main.Count - 1];
        var gaveUp = new List<GaveUpMove>();
        foreach (var task in tasks) if (task.HoldX != last.HoldX || task.HoldZ != last.HoldZ)
            gaveUp.Add(new GaveUpMove { InstanceId = 30, TargetX = task.HoldX, TargetZ = task.HoldZ, ObjectiveId = -1 });
        var alloc = TestClaims.Allocate(plan, s, sectors, null, tasks, new ReconMissionLedger(), gaveUp);
        AssertEqual(FormationRole.Recon, Of(alloc, 30).Role, "the free formation scouts for a later main task");
        AssertEqual(last.Reason, Of(alloc, 30).Reason, "the last main task");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(alloc, 2).Reason, "the East reserve was refused for the first");
        AssertFalse(Refused(alloc), "and the push has its eyes: no reason is told");
        // A free formation that fills a task that does not see the main objective leaves the push without eyes: the
        // reason stands.
        var other = new List<GaveUpMove>();
        foreach (var task in tasks) if (ReconPlanner.ScoutsMainObjective(task))
            other.Add(new GaveUpMove { InstanceId = 30, TargetX = task.HoldX, TargetZ = task.HoldZ, ObjectiveId = -1 });
        var approach = TestClaims.Allocate(plan, s, sectors, null, tasks, new ReconMissionLedger(), other);
        // The nearest formation with the scenario's minimum is now the mass, so the scout stands behind the body and its first free task is the
        // sector's screen posture (it was the capital approach): either way a task that does not see the main objective.
        AssertEqual(FormationRole.Recon, Of(approach, 30).Role, "30 watches a task");
        AssertEqual("screen-posture", Of(approach, 30).Reason, "the sector's screen posture, the task a scout behind the body takes once the main objective's tasks are given up on");
        AssertFalse(ReconPlanner.ScoutsMainObjective(ReconKind.Screen, Of(approach, 30).Reason), "that does not see the main objective: " + Of(approach, 30).Reason);
        AssertTrue(Refused(approach), "and the push still has no eyes: the reason stands");
        // Control: with the free formation gone the refusal stands.
        var bare = SmokeUnion();
        bare.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 12000f));
        AssertTrue(Refused(Review(bare).Allocation), "control: nobody scouts, the refusal is the reason");
    }

    // The draw is for the push's own eyes. A higher-priority task of another kind (a screen posture, a belief) comes first in the list and
    // must not take the one formation the reserve can spare.
    private static void TheDrawServesTheMainObjectivesTasksNotThePostureOrBeliefTasks()
    {
        var s = SmokeUnion();
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        AssertTrue(plan.MainStaged, "staged");
        var tasks = new List<ReconTask>
        {
            new ReconTask { Kind = ReconKind.Screen, SectorId = plan.MainSectorId, FromX = 100f, FromZ = 0f, HoldX = 200f, HoldZ = 0f, Priority = 5f, Reason = "screen-posture" },
            new ReconTask { Kind = ReconKind.Screen, SectorId = plan.MainSectorId, FromX = 100f, FromZ = 0f, HoldX = 210f, HoldZ = 0f, Priority = 2.5f, Reason = "belief" },
            new ReconTask { Kind = ReconKind.Probe, SectorId = plan.MainSectorId, FromX = 100f, FromZ = 0f, HoldX = 250f, HoldZ = 0f, Priority = 4.2f, Reason = "probe" },
        };
        var alloc = TestClaims.Allocate(plan, s, sectors, null, tasks, new ReconMissionLedger());
        AssertEqual(FormationRole.Recon, Of(alloc, 2).Role, "the reserve formation scouts");
        AssertEqual("probe", Of(alloc, 2).Reason, "for the probe, not for the posture or the belief that precede it");
    }

    // A Union garrison the plan pledged inside a live secession state (Kentucky, D8). The hold reserve claims before AssignGarrisons, so the same
    // formation carries hold-reserve (and the plan's pledge): the draw must not take it (verify round 1, blocker).
    private static void ASecessionGarrisonIsNeverDrawnAsAScout()
    {
        var s = SmokeUnion(withReserve: false);
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 15, StateId = 14, X = 500f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 0.3f });
        s.Secessions.Add(new SecessionRecord { ObjectiveId = 15, StateId = 14, StateAlliance = 0, Live = true, HasTown = true, TownX = 500f, TownZ = 0f });
        var garrison = Corps(20, 480f, 2500f);
        garrison.StateId = 14;
        s.Own.Add(garrison);
        // The body is nearer the West objective than the garrison and big enough alone: the garrison is not in the mass.
        var body = s.Own[s.IndexOfOwn(1)];
        body.X = 345f;
        body.WaypointX = 345f;
        body.Strength = 12000f;
        s.Own[s.IndexOfOwn(1)] = body;
        var day = Review(s);
        AssertTrue(day.Plan.MainStaged, "staged");
        AssertEqual(1, day.Plan.Garrisons.Count, "one post: Kentucky");
        AssertTrue(day.Plan.Garrisons[0].InstanceIds.Contains(20), "the plan pledged the formation");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(TestClaims.Allocate(day.Plan, s, ExecutorFixtures.Sectors(s), null, null, null), 20).Reason, "though the hold reserve claimed it first");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "and it is not the push's scout");
        AssertTrue(ReconPlanner.ScoutPool(s, null) >= 1, "though the pool counts it");
    }

    private static void ScoutsMainObjectiveNamesTheMainTasksOnly()
    {
        foreach (string reason in new[] { ReconPlanner.ReasonMainAxis })
            AssertTrue(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.Screen, Reason = reason }), "the main-axis screen");
        AssertTrue(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.Probe, Reason = "probe" }), "the probe");
        AssertTrue(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.AdvanceGuard, Reason = "advance-guard" }), "the advance guard");
        foreach (string reason in new[] { ReconPlanner.ReasonCapitalApproach, ReconPlanner.ReasonFrontApproach, "belief", "screen-posture" })
            AssertFalse(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.Screen, Reason = reason }), reason + " is not the push's eyes");
        AssertFalse(ReconPlanner.ScoutsMainObjective(new ReconTask { Kind = ReconKind.Picket, Reason = ReconPlanner.ReasonMainAxis }), "a picket is never one");
    }

    // The row's unfilled count pairs tasks with missions as the ledger does: kind, hold point and reason.
    private static void TheUnfilledCountMatchesTasksByKindPointAndReason()
    {
        var s = Base();
        s.Own.Add(Corps(1, 100f, 6000f));
        var ledger = new ReconMissionLedger();
        var a = new Allocation { Alliance = 0 };
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.Recon, SectorId = 0, TargetX = 200f, TargetZ = 5f, Reason = "belief" });
        var tasks = new List<ReconTask>
        {
            new ReconTask { Kind = ReconKind.Screen, SectorId = 0, HoldX = 200f, HoldZ = 5f, Reason = "belief" },
            new ReconTask { Kind = ReconKind.Screen, SectorId = 0, HoldX = 300f, HoldZ = 5f, Reason = "belief" },   // another point
            new ReconTask { Kind = ReconKind.Screen, SectorId = 0, HoldX = 200f, HoldZ = 9f, Reason = "belief" },   // another point (z)
            new ReconTask { Kind = ReconKind.Screen, SectorId = 0, HoldX = 200f, HoldZ = 5f, Reason = ReconPlanner.ReasonMainAxis },   // another reason
            new ReconTask { Kind = ReconKind.Probe, SectorId = 0, HoldX = 200f, HoldZ = 5f, Reason = "belief" },     // another kind
        };
        ledger.Sync(s, tasks, a);
        var counts = ReconStep.CountsOf(s, ledger, tasks);
        AssertEqual(5, counts.Tasks, "five tasks");
        AssertEqual(4, counts.Unfilled, "one has its mission");
        var cleared = ledger.Missions[0];
        cleared.State = ReconState.Cleared;       // same kind, point and reason, but over: it fills nothing
        ledger.Restore(new[] { cleared });
        AssertEqual("belief", ledger.Missions[0].Reason, "the cleared mission still carries the task's reason");
        AssertEqual(5, ReconStep.CountsOf(s, ledger, tasks).Unfilled, "a cleared mission fills nothing");
    }

    private static void TheReconRowCountsTasksAndUnfilled()
    {
        // The body is the pool's only member and the last of the main effort: tasks exist, nobody can be sent.
        var none = Review(SmokeUnion(bodyFlags: 0, withReserve: false));
        AssertTrue(none.Tasks.Count >= 3, "tasks exist: " + none.Tasks.Count);
        var rowNone = ReconStep.CountsOf(none.S, none.Ledger, none.Tasks, none.Allocation).Row(0);
        AssertContains(rowNone, " tasks=" + none.Tasks.Count + " unfilled=" + none.Tasks.Count + " ", "nobody was sent: every task is unfilled, " + rowNone);
        AssertContains(rowNone, " why=mass-kept,unsuited pool=1 free=0 mass=1", "though the pool is not empty, and the row says what stopped each: the screen would take the main effort's last body, the advance guard is the body itself: " + rowNone);

        var drawn = Review(SmokeUnion());
        var row = ReconStep.CountsOf(drawn.S, drawn.Ledger, drawn.Tasks, drawn.Allocation).Row(0);
        AssertContains(row, " tasks=" + drawn.Tasks.Count + " unfilled=" + (drawn.Tasks.Count - 1) + " ", "one task has its scout: " + row);
        AssertContains(row, " listed=2 why=claimed-recon pool=1 free=0 mass=0", "the capital-defence body and the capital guard (also on vanilla's list) are named, the one formation the pick can take went to the first task and the others say so: " + row);
        int guardOnly = ReconPlanner.ListedCount(SmokeUnion(0));
        AssertEqual(1, guardOnly, "an unlisted body is not listed: only the capital guard is");
        AssertEqual(guardOnly + 1, ReconPlanner.ListedCount(SmokeUnion(FormationFlags.InOffensiveOps)), "an offensive-operations entry is listed too");
        var busyListed = SmokeUnion(0);
        var inBattle = busyListed.Own[busyListed.IndexOfOwn(13)];
        inBattle.Flags |= FormationFlags.CapitalDefense;
        busyListed.Own[busyListed.IndexOfOwn(13)] = inBattle;
        AssertEqual(guardOnly, ReconPlanner.ListedCount(busyListed), "a busy formation is not massable, so its list entry says nothing about the mass");
    }

    // The probe/screen is a question, not a post: answered by sight or a fresh belief, the task is gone and the formation goes back to
    // the reserve the next review.
    private static void AScoutDrawnFromTheReserveReturnsWhenTheObjectiveIsScouted()
    {
        var state = new PlannerState();
        var day1 = Review(SmokeUnion(), state);
        AssertEqual(FormationRole.Recon, Of(day1.Allocation, 2).Role, "day 1: drawn");
        var s2 = SmokeUnion();
        s2.Beliefs.Add(ExecutorFixtures.Belief(310f, 0f, 1000f));
        var day2 = Review(s2, state, day1.Ledger);
        AssertTrue(day2.Plan.MainScouted, "day 2: the objective is scouted");
        AssertEqual(FormationRole.Hold, Of(day2.Allocation, 2).Role, "the formation is back in its sector's hold");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day2.Allocation, 2).Reason, "as its hold reserve");
    }

    // ---- Q3: the retarget within one objective ----

    // The smoke: main West/29 scouted=yes at 18:17:02, East/29 scouted=latched at 18:17:32 with the trace reading "unscouted est=9947".
    // An objective has several target records (29@3, 29@2, 29@6) and the decision follows the objective's id, so the best record of the
    // same objective is chosen each review with switch=hold; the scouting latch then belonged to the decision, not to the ground that was
    // scouted, and a different record, unseen, marched as if it were scouted.
    private static CampaignSnapshot TwoRecords(float beliefAtFirst, float beliefAtSecond)
    {
        var s = Base();
        s.Objectives.Clear();
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 400f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.6f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 700f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.6f });
        s.Own.Add(Corps(900, 0f, 30000f, 3, FormationFlags.CapitalDefense));   // the capital guard, sized for the floor the 300000 army in reach of the capital raises
        for (int i = 0; i < 8; i++) s.Own.Add(Corps(i + 1, 450f + 30f * i, 7000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(410f, 0f, beliefAtFirst));
        if (beliefAtSecond > 0f) s.Beliefs.Add(ExecutorFixtures.Belief(710f, 0f, beliefAtSecond));
        return s;
    }

    private static void ARetargetWithinTheObjectiveIsNotScoutedByTheOldTarget()
    {
        var state = new PlannerState();
        var day1 = Review(TwoRecords(1000f, 300000f), state);   // the second record is held by an army far too strong for the mass
        AssertEqual(40, day1.Plan.MainObjectiveId, "objective 40");
        AssertNear(400f, day1.Plan.MainObjectiveX, 0.5f, "the first record, seen");
        AssertTrue(day1.Plan.MainScouted && !day1.Plan.MainStaged, "day 1: scouted, not staged");
        int serial = state.DecisionSerial;

        // Day 2: the first record is now held by far more than the mass can beat; the same objective's other record, never seen, is the best.
        var day2 = Review(TwoRecords(300000f, 0f), state);
        AssertEqual(40, day2.Plan.MainObjectiveId, "the same objective");
        AssertNear(700f, day2.Plan.MainObjectiveX, 0.5f, "now its other record");
        AssertEqual(serial, state.DecisionSerial, "the same decision (the objective did not change)");
        AssertEqual("hold", day2.Plan.SwitchReason, "switch=hold, as in the smoke");
        AssertFalse(day2.Plan.MainScouted, "but nobody has seen this ground");
        AssertFalse(day2.Plan.MainScoutLatched, "and the old record's picture is not carried over to it");
        AssertTrue(day2.Plan.MainStaged, "so the mass gathers and scouts first");
    }

    // ---- hysteresis between the records of one objective ----

    // Objective 40 with two records, 400 (the West one) and 700; the importance of each sets its value term (0.30 x 0.5 x importance), so
    // swapping the two importances swaps which record leads by more than SwitchMargin (0.12). Eight corps stand between them, in supply.
    private static CampaignSnapshot Records(float importanceFirst, float importanceSecond, float beliefAtFirst = 0f)
    {
        var s = Base();
        s.Objectives.Clear();
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 400f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importanceFirst });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 700f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importanceSecond });
        // Re-pinned for the capital-floor pump hotfix (2026-10-19): the capital reserve no longer pulls the nearest corps (x 450, 7000) from beyond
        // the relocation range (200) to top the guard up, so the guard stands at the strength both made (5600 + 7000) and the corps is not here.
        var guard = CapitalGuard();
        guard.Strength = 12600f;
        s.Own.Add(guard);
        for (int i = 1; i < 8; i++) s.Own.Add(Corps(i + 1, 450f + 30f * i, 7000f));
        if (beliefAtFirst > 0f) s.Beliefs.Add(ExecutorFixtures.Belief(410f, 0f, beliefAtFirst));
        return s;
    }

    private const float Lean = 0.95f, Slight = 0.05f;   // the record that leads, the one that trails

    private static TheaterPlan PlanOf(CampaignSnapshot s, PlannerState state)
    {
        return TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, state, null, null, ExecutorFixtures.Speed(), out _);
    }

    private static float ScoreOf(CampaignSnapshot s, float x)
    {
        var inputs = new TheaterPlanner.Inputs { S = s, Sectors = ExecutorFixtures.Sectors(s), Speed = ExecutorFixtures.Speed() };
        var list = new List<TheaterPlanner.Candidate>();
        TheaterPlanner.Evaluate(inputs, list);
        foreach (var c in list) if (Math.Abs(c.X - x) < 1f) return c.Score;
        throw new Exception("no record at " + x);
    }

    private static void RecordsWhoseScoresAlternateStayOnOneRecord()
    {
        var state = new PlannerState();
        var day1 = PlanOf(Records(Lean, Slight), state);
        float first = day1.MainObjectiveX;
        AssertNear(400f, first, 0.5f, "day 1: the record that leads");
        int serial = state.DecisionSerial;
        for (int day = 2; day <= 9; day++)
        {
            bool swapped = day % 2 == 0;
            var s = swapped ? Records(Slight, Lean) : Records(Lean, Slight);
            if (swapped)
                AssertTrue(ScoreOf(s, 700f) - ScoreOf(s, 400f) > TheaterPlanner.SwitchMargin, "day " + day + ": the other record leads by more than the margin");
            var plan = PlanOf(s, state);
            AssertNear(first, plan.MainObjectiveX, 0.5f, "day " + day + ": the main effort stays on its record");
            AssertEqual(40, plan.MainObjectiveId, "the same objective");
            AssertEqual(serial, state.DecisionSerial, "and the same decision");
            AssertEqual("hold", plan.SwitchReason, "switch=hold");
        }
    }

    // The objective switch's own rule: the margin on SwitchDays consecutive reviews.
    private static void ARecordChangesAfterTheMarginHoldsForThreeReviews()
    {
        var state = new PlannerState();
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "day 1");
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "day 2: one review of lead");
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "day 3: two");
        int serial = state.DecisionSerial;
        var day4 = PlanOf(Records(Slight, Lean), state);
        AssertNear(700f, day4.MainObjectiveX, 0.5f, "day 4: the third consecutive review of lead moves it");
        AssertEqual(serial, state.DecisionSerial, "within the decision (the objective did not change)");
        AssertEqual(0, state.RecordChallengerDays, "the window is spent with the move");
        AssertNear(700f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "and the new record is kept in its turn (day 5: the old one leads once)");
    }

    private static void AnInterruptedLeadRestartsTheRecordWindow()
    {
        var state = new PlannerState();
        PlanOf(Records(Lean, Slight), state);
        PlanOf(Records(Slight, Lean), state);       // 1
        PlanOf(Records(Slight, Lean), state);       // 2
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "a review where the kept record leads again");
        PlanOf(Records(Slight, Lean), state);       // 1 again
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "two in a row after the interruption: still the kept record");
        AssertNear(700f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "the third in a row moves it");
    }

    // Invalid at once: the kept record is held by an army the mass cannot beat (no longer eligible), or is gone (accomplished or unavailable).
    private static void AnInvalidKeptRecordIsLeftAtOnce()
    {
        var state = new PlannerState();
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "day 1");
        int serial = state.DecisionSerial;
        var beaten = Records(Lean, Slight, beliefAtFirst: 300000f);
        var day2 = PlanOf(beaten, state);
        AssertNear(700f, day2.MainObjectiveX, 0.5f, "day 2: the kept record is below its odds, so the other is taken the same review");
        AssertEqual(serial, state.DecisionSerial, "same objective, same decision");

        var state2 = new PlannerState();
        PlanOf(Records(Lean, Slight), state2);
        var gone = Records(Lean, Slight);
        var rec = gone.Objectives[0];
        rec.Accomplished = true;
        gone.Objectives[0] = rec;
        AssertNear(700f, PlanOf(gone, state2).MainObjectiveX, 0.5f, "an accomplished record is lost: the other at once");
        var state3 = new PlannerState();
        PlanOf(Records(Lean, Slight), state3);
        var unavailable = Records(Lean, Slight);
        rec = unavailable.Objectives[0];
        rec.Available = false;
        unavailable.Objectives[0] = rec;
        AssertNear(700f, PlanOf(unavailable, state3).MainObjectiveX, 0.5f, "an unavailable one too");
    }

    // Three records: the kept one is beaten by the margin every review, but by B and C in turn: no record has three reviews in a row.
    private static CampaignSnapshot ThreeRecords(float a, float b, float c)
    {
        var s = Records(a, b);
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 600f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = c });
        return s;
    }

    private static void DifferentChallengersDoNotAddUp()
    {
        var state = new PlannerState();
        AssertNear(400f, PlanOf(ThreeRecords(Lean, Slight, Slight), state).MainObjectiveX, 0.5f, "day 1: the first record");
        for (int day = 2; day <= 8; day++)
        {
            var s = day % 2 == 0 ? ThreeRecords(Slight, Lean, 0.9f) : ThreeRecords(Slight, 0.9f, Lean);
            var plan = PlanOf(s, state);
            AssertNear(400f, plan.MainObjectiveX, 0.5f, "day " + day + ": two records beat it in turn, neither for three reviews");
        }
    }

    // Two records at one x, 10 apart in z: the record is its position, both axes.
    private static CampaignSnapshot SideBySide(float importanceA, float importanceB)
    {
        var s = Records(importanceA, 0.05f);
        s.Objectives.Clear();
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 400f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importanceA });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, X = 400f, Z = 10f, HolderAlliance = 1, Available = true, Importance01 = importanceB });
        return s;
    }

    private static void RecordsThatDifferOnlyInZAreDistinctRecords()
    {
        var state = new PlannerState();
        AssertNear(0f, PlanOf(SideBySide(1f, 0f), state).MainObjectiveZ, 0.5f, "day 1: the record on the line");
        PlanOf(SideBySide(0f, 1f), state);
        PlanOf(SideBySide(0f, 1f), state);
        AssertNear(10f, PlanOf(SideBySide(0f, 1f), state).MainObjectiveZ, 0.5f, "day 4: the other record after three reviews");
        AssertNear(10f, PlanOf(SideBySide(1f, 0f), state).MainObjectiveZ, 0.5f, "day 5: the first leads once, the kept one stays");
    }

    // ---- the draw and a believed threat (verify round 1, major) ----

    // A believed army at x 1000 faces the East (obj 31 at 900). Its hold need is HoldRatio x the army, belief-driven once it passes the floor.
    private static Day EastWithArmy(float armyStrength)
    {
        var s = SmokeUnion();
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, armyStrength));
        return Review(s);
    }

    private static void TheDrawNeverOpensAShortfallAgainstABelievedArmy()
    {
        foreach (float army in new[] { 12000f, 16000f })
        {
            var day = EastWithArmy(army);
            AssertTrue(day.Plan.MainStaged, army + ": the West push is staged");
            float need = 0f, believed = 0f;
            foreach (var sp in day.Plan.Sectors) if (sp.HoldNeed > need) { need = sp.HoldNeed; believed = sp.BelievedHold; }
            AssertTrue(believed > TheaterPlanner.MinStrengthFallback && believed >= need - 0.01f, army + ": the East need is the army's: " + need + " / " + believed);
            AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), army + ": nothing is drawn from the East reserve");
            AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day.Allocation, 2).Reason, army + ": it keeps holding");
            AssertTrue(Refused(day.Allocation), army + ": and the refusal is named");
            AssertContains(ReconStep.CountsOf(day.S, day.Ledger, day.Tasks, day.Allocation).Row(0), " why=believed-threat,claimed-hold-reserve ", army + ": in the [Recon] row");
            AssertFalse(ReconStep.CountsOf(day.S, day.Ledger, day.Tasks, day.Allocation).Equals(ReconStep.CountsOf(day.S, day.Ledger, day.Tasks)), army + ": a row that differs only by why= is a changed row");
        }
    }

    // Belief-driven, but the sector's hold less the drawn formation still meets its target: the draw may go. East need 5400 (an army of 9000), a
    // busy 3000 holds, the reserve is the 1000-man 5 (nearest the army) and the 3000-man 2: drawing the smallest leaves 6000 >= 5400.
    private static void ABelievedSectorThatStillCoversItsNeedGivesAScout()
    {
        var s = SmokeUnion(secondEastBusy: false, eastBusy: 3000f);
        s.Own.Add(Corps(5, 800f, 1000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 9000f));
        var held = TestClaims.Allocate(ExecutorFixtures.RealPlan(s, new PlannerState()), s, ExecutorFixtures.Sectors(s), null, null, null);
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(held, 5).Reason, "5 is hold reserve");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(held, 2).Reason, "so is 2");
        var day = Review(s);
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 5).Role, "the 1000-man formation scouts: the East keeps 6000 against 5400");
        AssertEqual(FormationRole.Hold, Of(day.Allocation, 2).Role, "and 2 keeps holding");
        AssertFalse(Refused(day.Allocation), "nothing was refused");
    }

    // The boundary: the hold less the drawn formation equals the need exactly (an army of 10000: need 6000; a busy 3000, the 1000 and the 3000
    // hold 7000, and 6000 remain). Equal meets the target, so the draw goes.
    private static void ADrawThatLeavesExactlyTheNeedIsAllowed()
    {
        var s = SmokeUnion(secondEastBusy: false, eastBusy: 3000f);
        s.Own.Add(Corps(5, 800f, 1000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 10000f));
        var day = Review(s);
        int east = ExecutorFixtures.Sectors(s).SectorAt(800f, 0f);
        float need = 0f;
        foreach (var sp in day.Plan.Sectors) if (sp.SectorId == east) need = sp.HoldNeed;
        AssertNear(6000f, need, 0.5f, "the East need");
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 5).Role, "the 1000 scouts: 6000 remain against 6000");
        AssertFalse(Refused(day.Allocation), "nothing refused");
    }

    // Foresight F6b-2 gap 32 (plant P25): a covering member still on the march to its post (Redeploy, reason covering-force) holds its recorded
    // sector, so the scout draw does not read it as leaving. The world is the one above, with the 3000-man hold reserve 2 turned into a covering
    // member of the East that is far from its post: the East still holds 7000 against 5400 (a believed army of 9000), so the 1000-man reserve 5 may
    // scout. If HoldLeaving counted the marching member as leaving, 7000 - 3000 - 1000 = 3000 < 5400 would refuse the draw.
    private static void AMarchingCoveringMemberStillHoldsItsSectorForTheDraw()
    {
        var s = SmokeUnion(secondEastBusy: false, eastBusy: 3000f);
        s.Own.Add(Corps(5, 800f, 1000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 9000f));
        var day = Review(s, null, null, plan =>
        {
            for (int k = 0; k < plan.Claims.Count; k++)
            {
                if (plan.Claims[k].InstanceId != 2) continue;
                var c = plan.Claims[k];
                AssertEqual(Claimant.Hold, c.Claimant, "2 is a hold reserve before it is made a covering member");
                c.Reason = ForceAllocator.ReasonCoveringForce;
                c.TargetX = 300f;          // the post, far west of the formation at 760
                c.TargetZ = 0f;
                plan.Claims[k] = c;
            }
        });
        var member = Of(day.Allocation, 2);
        AssertEqual(FormationRole.Redeploy, member.Role, "2 marches to its post: " + Describe(day.Allocation));
        AssertEqual(ForceAllocator.ReasonCoveringForce, member.Reason, "as a covering member");
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 5).Role, "the marching member still holds the East, so 5 scouts: " + Describe(day.Allocation));
        AssertFalse(Refused(day.Allocation), "and nothing was refused");
    }

    // The presumption alone sets the East need (nothing believed): the scout may go, and the sector's loss is a listed shortfall whose carrier is
    // its immobile holder, so G5b sees it although the one mobile hold was drawn away.
    private static void APresumptionOnlySectorGivesAScoutAndNamesItsCarrier()
    {
        var day = Review(SmokeUnion());
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 2).Role, "drawn");
        AssertFalse(Refused(day.Allocation), "nothing refused");
        AssertTrue(day.Allocation.Shortfalls.Contains(13) || day.Allocation.Shortfalls.Contains(14), "the East is short and names a busy holder: " + string.Join(",", day.Allocation.Shortfalls));
        AssertEqual(1, day.Allocation.Shortfalls.Count, "only the East");
        // A sector with no immobile holder either has no one to name.
        var s = SmokeUnion(secondEastBusy: false);
        s.Own.RemoveAt(s.IndexOfOwn(13));
        s.Own.Add(Corps(3, 770f, 600f));
        s.Own.Add(Corps(4, 780f, 600f));
        var bare = Review(s);
        AssertEqual(1, Roles(bare.Allocation, FormationRole.Recon), "one drawn from a sector held by reserve alone: " + Describe(bare.Allocation));
        foreach (int id in bare.Allocation.Shortfalls) AssertTrue(id != 13 && id != 14, "no immobile formation exists to carry it");
    }

    // The cap is per main effort, not per review: the scout out yesterday is today's incumbent, and no second one is opened beside it.
    private static void OneStandingScoutPerMainEffortAcrossReviews()
    {
        Func<CampaignSnapshot> world = () =>
        {
            var w = SmokeUnion(secondEastBusy: false);
            w.Own.Add(Corps(3, 770f, 600f));
            w.Own.Add(Corps(4, 780f, 600f));
            return w;
        };
        var state = new PlannerState();
        var ledger = new ReconMissionLedger();
        var day = Review(world(), state, ledger);
        AssertEqual(1, MainScouts(day.Allocation), "day 1: one");
        for (int k = 2; k <= 5; k++)
        {
            var s2 = world();
            foreach (var a in day.Allocation.Assignments)
            {
                if (a.Role != FormationRole.Recon) continue;
                int i = s2.IndexOfOwn(a.InstanceId);
                var f = s2.Own[i];
                f.X = 600f; f.Z = 0f; f.WaypointX = 600f; f.WaypointZ = 0f;      // it is on the road west, out of the East's reserve and not yet at its post
                s2.Own[i] = f;
            }
            day = Review(s2, state, ledger);
            AssertTrue(MainScouts(day.Allocation) <= 1, "day " + k + ": never a second scout for the main objective: " + MainScouts(day.Allocation));
        }
    }

    // A scout of the main objective already stands from an earlier review (a free formation on an advance-guard mission): the reserve is not
    // drawn on for a second pair of eyes, whatever task is still open.
    private static void AScoutAlreadyOutForTheMainObjectiveBlocksAFurtherDraw()
    {
        var s = SmokeUnion();
        // Farther from the West objective than the body: the push is sized by the scenario's minimum now (2000), so the nearest formation that
        // has it is the mass, and a free scout stands behind the body.
        var free = Corps(30, 40f, 3000f);
        s.Own.Add(free);
        var ledger = new ReconMissionLedger();
        ledger.Restore(new[]
        {
            new ReconMission
            {
                MissionId = 1, Kind = ReconKind.AdvanceGuard, InstanceId = 30, UnitKey = free.UnitKey, HoldX = 350f, HoldZ = 0f, SectorId = 0,
                State = ReconState.Moving, IssuedDay = s.DaySerial - 1, Reason = "advance-guard", ObservedEnemyStrength = -1f, ObservedObjectiveId = -1, ClearedDay = -1
            }
        });
        var day = Review(s, new PlannerState(), ledger);
        AssertTrue(day.Plan.MainStaged, "staged");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day.Allocation, 2).Reason, "the East reserve is not drawn on");
        AssertEqual(1, MainScouts(day.Allocation), "one scout for the main objective: the one already out: " + Describe(day.Allocation) + " mass=" + string.Join(",", day.Plan.MassInstanceIds) + " need=" + day.Plan.MainNeedStrength + " enemy=" + day.Plan.MainEnemyStrength + " main=" + day.Plan.MainObjectiveId);
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 30).Role, "that one");
    }

    // A mission that has finished (Cleared, or Recalled) is not a scout out: it does not stand in for the push's eyes. The free 30 is not ready,
    // so the regular pick cannot take it; the standing count decides whether the East reserve may be drawn.
    private static void AClearedMissionDoesNotBlockTheDraw()
    {
        foreach (ReconState finished in new[] { ReconState.Cleared, ReconState.Recalled })
        {
            var s = SmokeUnion();
            var free = Corps(30, 365f, 3000f, 1);
            s.Own.Add(free);
            var ledger = new ReconMissionLedger();
            ledger.Restore(new[]
            {
                new ReconMission
                {
                    MissionId = 1, Kind = ReconKind.AdvanceGuard, InstanceId = 30, UnitKey = free.UnitKey, HoldX = 350f, HoldZ = 0f, SectorId = 0,
                    State = finished, IssuedDay = s.DaySerial - 2, Reason = "advance-guard", ObservedEnemyStrength = -1f, ObservedObjectiveId = -1,
                    ClearedDay = finished == ReconState.Cleared ? s.DaySerial - 1 : -1
                }
            });
            var day = Review(s, new PlannerState(), ledger);
            AssertTrue(day.Plan.MainStaged, finished + ": staged");
            AssertEqual(FormationRole.Recon, Of(day.Allocation, 2).Role, finished + ": the reserve is drawn for the push's eyes");
        }
        // Control: the same fixture with the mission still active blocks it (AScoutAlreadyOutForTheMainObjectiveBlocksAFurtherDraw).
        var c = SmokeUnion();
        var held = Corps(30, 365f, 3000f, 1);
        c.Own.Add(held);
        var activeLedger = new ReconMissionLedger();
        activeLedger.Restore(new[]
        {
            new ReconMission
            {
                MissionId = 1, Kind = ReconKind.AdvanceGuard, InstanceId = 30, UnitKey = held.UnitKey, HoldX = 350f, HoldZ = 0f, SectorId = 0,
                State = ReconState.Moving, IssuedDay = c.DaySerial - 1, Reason = "advance-guard", ObservedEnemyStrength = -1f, ObservedObjectiveId = -1, ClearedDay = -1
            }
        });
        var blocked = Review(c, new PlannerState(), activeLedger);
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(blocked.Allocation, 2).Reason, "control: an active mission leaves the reserve alone");
    }

    // The approach screens watch other ground; they are not the push's eyes, so one standing does not stand in for them.
    private static void AScoutOutForAnotherTaskDoesNotBlockTheDraw()
    {
        var s = SmokeUnion();
        var free = Corps(30, 365f, 3000f, 1);   // not ready: it cannot be picked for a task today, its mission still stands
        s.Own.Add(free);
        var ledger = new ReconMissionLedger();
        ledger.Restore(new[]
        {
            new ReconMission
            {
                MissionId = 1, Kind = ReconKind.Screen, InstanceId = 30, UnitKey = free.UnitKey, HoldX = 350f, HoldZ = 0f, SectorId = 0,
                State = ReconState.Moving, IssuedDay = s.DaySerial - 1, Reason = ReconPlanner.ReasonCapitalApproach, ObservedEnemyStrength = -1f, ObservedObjectiveId = -1, ClearedDay = -1
            }
        });
        var day = Review(s, new PlannerState(), ledger);
        AssertTrue(day.Plan.MainStaged, "staged");
        AssertEqual(1, MainScouts(day.Allocation), "the push still gets its eyes from the reserve");
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 2).Role, "the reserve formation");
    }

    // Three candidates, pinned: the kept record trails the objective's best record; a challenger objective is compared with the BEST record of
    // the current objective (its value), not with the record the main effort happens to be on, so the record window does not weaken the objective.
    private static void AChallengerObjectiveIsComparedWithTheCurrentObjectivesBestRecord()
    {
        // Objective 40: records 400 (kept) and 700; objective 41: one record at 690. Find an importance for 41 that beats 40's best record by
        // less than the margin and the kept record by more.
        Func<float, float, float, CampaignSnapshot> world = (a, b, other) =>
        {
            var s = Records(a, b);
            s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 41, X = 698f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = other });
            return s;
        };
        float chosen = -1f, second = -1f;
        for (float second2 = 0.3f; second2 <= 1f && chosen < 0f; second2 += 0.1f)
            for (float imp = 0f; imp <= 1f && chosen < 0f; imp += 0.05f)
            {
                var s = world(Slight, second2, imp);
                float best40 = Math.Max(ScoreOf(s, 400f), ScoreOf(s, 700f));
                float kept40 = ScoreOf(s, 400f);
                float c = ScoreOf(s, 698f);
                if (c - best40 > 0.005f && c - best40 < TheaterPlanner.SwitchMargin - 0.01f && c - kept40 > TheaterPlanner.SwitchMargin + 0.01f) { chosen = imp; second = second2; }
            }
        AssertTrue(chosen >= 0f, "a challenger inside the margin of the best record and outside it of the kept one exists");
        var state = new PlannerState();
        AssertNear(400f, PlanOf(world(Lean, Slight, 0f), state).MainObjectiveX, 0.5f, "day 1: objective 40 on its 400 record");
        AssertEqual(40, state.CurrentMainObjective, "objective 40");
        PlanOf(world(Slight, second, chosen), state);
        PlanOf(world(Slight, second, chosen), state);
        AssertEqual(0, state.ChallengerDays, "objective 41 never counts as a challenger: it does not beat 40's best record by the margin");
        AssertEqual(40, state.CurrentMainObjective, "and the main effort is still objective 40");
        AssertNear(400f, state.CurrentTargetX, 0.5f, "on its kept record, which trails the best one");
    }

    // A standing trigger (a capital threat) never re-draws among near equals while the current objective is within Epsilon of the best: the
    // objective's value is its best record's, and the main effort stays on the record it holds.
    private static void ATriggerKeepsTheKeptRecordWhileTheObjectiveIsStillBest()
    {
        Func<float, float, float, bool, CampaignSnapshot> world = (a, b, other, armyAtCapital) =>
        {
            var s = Records(a, b);
            s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 41, X = 698f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = other });
            if (armyAtCapital) s.Beliefs.Add(ExecutorFixtures.Belief(50f, 0f, 120000f));
            return s;
        };
        float second = -1f, chosen = -1f;
        for (float b2 = 0.3f; b2 <= 1f && chosen < 0f; b2 += 0.1f)
            for (float imp = 0f; imp <= 1f && chosen < 0f; imp += 0.05f)
            {
                var s = world(Slight, b2, imp, true);
                float best40 = Math.Max(ScoreOf(s, 400f), ScoreOf(s, 700f));
                float c = ScoreOf(s, 698f);
                if (c - best40 > 0.002f && c - best40 < TheaterPlanner.Epsilon - 0.01f && best40 - ScoreOf(s, 400f) > TheaterPlanner.Epsilon + 0.01f) { second = b2; chosen = imp; }
            }
        AssertTrue(chosen >= 0f, "a rival inside Epsilon of the objective's best record, with the kept record outside it, exists");
        var state = new PlannerState();
        AssertNear(400f, PlanOf(world(Lean, Slight, 0f, false), state).MainObjectiveX, 0.5f, "day 1: objective 40, record 400");
        int serial = state.DecisionSerial;
        var day2 = PlanOf(world(Slight, second, chosen, true), state);
        AssertEqual(40, day2.MainObjectiveId, "day 2, under a capital threat: still objective 40");
        AssertNear(400f, day2.MainObjectiveX, 0.5f, "on the record it holds");
        AssertEqual(serial, state.DecisionSerial, "the same decision");
    }

    // ---- rule (b) is judged on the measure the reserve was sized on (verify round 2, major) ----

    // The sum of a sector's Hold roles at the end of the pass (the unready and the busy are Hold; a capital guard is not, and counts separately).
    private static float HeldAtEnd(Day d, int sector, bool withCapitalGuards)
    {
        var sectors = ExecutorFixtures.Sectors(d.S);
        float sum = 0f;
        foreach (var a in d.Allocation.Assignments)
        {
            var f = d.S.Own[d.S.IndexOfOwn(a.InstanceId)];
            if (ReserveRules.SectorOf(sectors, f) != sector) continue;
            if ((a.Role == FormationRole.Hold && a.SectorId == sector) || (withCapitalGuards && a.Role == FormationRole.CapitalGuard)) sum += Math.Max(0f, f.Strength);
        }
        return sum;
    }

    // The East faces a believed 12000 (need 7200, belief-driven). It holds with an unready 4000 (counted by ReserveRules.Hold, assigned Hold only
    // by AssignRemainder after the recon pick), the ready 3000 nearest the army and a ready 5000 behind it: without the 3000 the East holds 9000.
    private static CampaignSnapshot EastWithAnUnreadyHolder()
    {
        var s = Base();
        s.Own.Add(CapitalGuard());
        s.Own.Add(Corps(1, 100f, 9563f, 3, FormationFlags.CapitalDefense));
        s.Own.Add(Corps(9, 200f, 4000f, 1));
        s.Own.Add(Corps(10, 220f, 4000f, 1));
        s.Own.Add(Corps(15, 150f, 5000f, 3, FormationFlags.InBattle));
        s.Own.Add(Corps(30, 750f, 4000f, 1));          // unready East holder
        s.Own.Add(Corps(2, 800f, 3000f));              // ready, nearest the believed army
        s.Own.Add(Corps(3, 760f, 5000f));              // ready, behind it
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 12000f));
        return s;
    }

    private static void AnUnreadyHolderCountsTowardTheSectorsCoverBeforeADraw()
    {
        var s = EastWithAnUnreadyHolder();
        var day = Review(s);
        int east = ExecutorFixtures.Sectors(s).SectorAt(800f, 0f);
        float need = 0f, believed = 0f;
        foreach (var sp in day.Plan.Sectors) if (sp.SectorId == east) { need = sp.HoldNeed; believed = sp.BelievedHold; }
        AssertNear(7200f, need, 1f, "the East need is HoldRatio x 12000");
        AssertTrue(believed >= need - 0.01f, "and belief-driven");
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 2).Role, "the 3000 is drawn");
        AssertFalse(Refused(day.Allocation), "nothing refused");
        AssertTrue(HeldAtEnd(day, east, false) >= need, "and the East still holds " + HeldAtEnd(day, east, false) + " against " + need);
    }

    // What the held sum counts must still hold the sector when the draw is tested. The unready 30 is counted (ReserveRules.Hold), and it is
    // also the East's marching depot builder (F9: an unready formation is never a NEW pick, but the task's own builder keeps its pick through a
    // readiness dip), so the depot task there keeps it as the builder before the recon pick: it leaves the hold.
    // Without it the East keeps 8000 against 7200 and the 3000 cannot go.
    private static void AnUnreadyHolderTakenAsDepotBuilderIsNoLongerCounted()
    {
        var s = EastWithAnUnreadyHolder();
        int east = ExecutorFixtures.Sectors(s).SectorAt(800f, 0f);
        var marching = new[] { new HeldBuilder { InstanceId = 30, SectorId = east, Wished = false, SiteX = 770f, SiteZ = 0f, TargetX = 770f, TargetZ = 0f } };
        var day = Review(s, null, null, plan => plan.DepotTasks.Add(new DepotTask { X = 770f, Z = 0f, SectorId = east, Priority = 1f }), marching);
        AssertEqual(FormationRole.DepotBuilder, Of(day.Allocation, 30).Role, "the unready formation is the depot task's kept builder");
        float need = 0f;
        foreach (var sp in day.Plan.Sectors) if (sp.SectorId == east) need = sp.HoldNeed;
        AssertNear(7200f, need, 1f, "the East need (belief-driven)");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "the 3000 is not drawn: the East holds 8000 without the builder");
        AssertTrue(Refused(day.Allocation), "and the refusal is named");
        AssertTrue(HeldAtEnd(day, east, false) >= need, "the East holds " + HeldAtEnd(day, east, false) + " against " + need);
    }

    // A secession garrison on the march (a pledged formation outside the state) leaves its sector the same way.
    private static void AnUnreadyHolderPledgedToAGarrisonOutsideItsStateIsNoLongerCounted()
    {
        var s = EastWithAnUnreadyHolder();
        int east = ExecutorFixtures.Sectors(s).SectorAt(800f, 0f);
        var day = Review(s, null, null, plan => plan.Garrisons.Add(new GarrisonPost
        {
            Kind = GarrisonPostKind.Secession, StateId = 99, X = 300f, Z = 0f, Need = 3000f, InstanceIds = new List<int> { 30 }
        }));
        AssertEqual(FormationRole.Garrison, Of(day.Allocation, 30).Role, "the pledged formation marches to the post");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "the 3000 is not drawn: the East holds 8000 without it");
        AssertTrue(Refused(day.Allocation), "and the refusal is named");
        AssertTrue(HeldAtEnd(day, east, false) >= 7200f, "the East holds " + HeldAtEnd(day, east, false));
    }

    // An ordinary garrison post cannot take it: GarrisonPlanner seats only a massable formation (Seatable), so the unready holder stays in the
    // East whatever a post near it asks, and the draw is tested on the held sum as it is.
    private static void AnOrdinaryGarrisonPostTakesNoUnreadyHolder()
    {
        var s = EastWithAnUnreadyHolder();
        int east = ExecutorFixtures.Sectors(s).SectorAt(800f, 0f);
        var day = Review(s, null, null, plan => plan.Garrisons.Add(new GarrisonPost
        {
            Kind = GarrisonPostKind.Fort, X = 700f, Z = 0f, Need = 3000f
        }));
        AssertEqual(FormationRole.Hold, Of(day.Allocation, 30).Role, "the unready formation is not seated at the post");
        AssertEqual(0, day.Allocation.Seats.Count, "the post is unmanned");
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 2).Role, "the 3000 is drawn: the East still holds 9000");
        AssertFalse(Refused(day.Allocation), "nothing refused");
        AssertTrue(HeldAtEnd(day, east, false) >= 7200f, "the East holds " + HeldAtEnd(day, east, false));
    }

    // A reserved formation that a Wished depot task pins as its builder is counted in the reserve (ReserveRules.Hold does not know the pin)
    // and leaves it before the draw: the share it put into the sum goes with it. The East faces 14000 (need 8400); the unready 3000 and the
    // reserve 2 (pinned, 1000), 3 (1000) and 5 (5000) make 10000. Drawing the 1000 would leave 9000 on the counted sum but 8000 in fact.
    private static void AReservedFormationPinnedAsABuilderIsNoLongerCounted()
    {
        var s = Base();
        s.Own.Add(CapitalGuard());
        s.Own.Add(Corps(1, 100f, 9563f, 3, FormationFlags.CapitalDefense));
        s.Own.Add(Corps(9, 200f, 4000f, 1));
        s.Own.Add(Corps(10, 220f, 4000f, 1));
        s.Own.Add(Corps(15, 150f, 5000f, 3, FormationFlags.InBattle));
        s.Own.Add(Corps(30, 750f, 3000f, 1));
        s.Own.Add(Corps(2, 820f, 1000f));
        s.Own.Add(Corps(3, 810f, 1000f));
        s.Own.Add(Corps(5, 770f, 5000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(1000f, 0f, 14000f));
        int east = ExecutorFixtures.Sectors(s).SectorAt(800f, 0f);
        var pinned = new List<HeldBuilder> { new HeldBuilder { InstanceId = 2, Wished = true, SectorId = east, TargetX = 830f, TargetZ = 0f, SiteX = 830f, SiteZ = 0f } };
        var day = Review(s, null, null, null, pinned);
        float need = 0f;
        foreach (var sp in day.Plan.Sectors) if (sp.SectorId == east) need = sp.HoldNeed;
        AssertNear(8400f, need, 1f, "the East need (belief-driven)");
        AssertEqual(FormationRole.DepotBuilder, Of(day.Allocation, 2).Role, "2 builds the pinned depot");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day.Allocation, 3).Reason, "3 is in the reserve");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "the 1000 is not drawn: the East would hold 8000 against 8400");
        AssertTrue(Refused(day.Allocation), "and the refusal is named");
    }

    // The capital reserve (the formations nearest the capital until its floor) holds the sector when the capital stands in it; ReserveRules.Hold
    // counts it, so the draw does too.
    private static void ACapitalGuardCountsTowardTheSectorsCoverBeforeADraw()
    {
        var s = Base();
        s.CapitalX = 760f;
        s.CapitalZ = 0f;
        s.Own.Add(Corps(1, 100f, 9563f, 3, FormationFlags.CapitalDefense));
        s.Own.Add(Corps(9, 200f, 4000f, 1));
        s.Own.Add(Corps(10, 220f, 4000f, 1));
        s.Own.Add(Corps(15, 150f, 5000f, 3, FormationFlags.InBattle));
        s.Own.Add(Corps(31, 755f, 7000f));             // nearest the capital: the capital reserve (D11: the minimum plus the weighted threat, met by it alone)
        s.Own.Add(Corps(2, 800f, 1000f));              // ready, nearest the believed army
        s.Own.Add(Corps(3, 790f, 5000f));              // ready, behind it
        s.Beliefs.Add(ExecutorFixtures.Belief(900f, 0f, 12000f));
        var day = Review(s);
        int east = ExecutorFixtures.Sectors(s).SectorAt(800f, 0f);
        AssertEqual(FormationRole.CapitalGuard, Of(day.Allocation, 31).Role, "31 is the capital guard");
        float need = 0f;
        foreach (var sp in day.Plan.Sectors) if (sp.SectorId == east) need = sp.HoldNeed;
        AssertNear(7200f, need, 1f, "the East need");
        // D11's floor (the minimum plus the weighted threat) needs a guard of 7000 here; it stands in the East and counts toward the East's
        // cover, so the 5000 is drawn and the 1000 stays in the East reserve.
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day.Allocation, 2).Reason, "2 stays in the East reserve: the guard's 7000 and its 1000 hold 8000 against 7200");
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 3).Role, "the 5000 is drawn: the East still holds its need without it");
        AssertFalse(Refused(day.Allocation), "nothing refused");
        AssertTrue(HeldAtEnd(day, east, true) >= need, "the East holds " + HeldAtEnd(day, east, true) + " with its guard, against " + need);
    }

    // A capital guard standing in the East while the capital lies in another sector marches there: it holds nothing in the East, so neither
    // the reserve's size nor the draw's cover counts it (verify r3 probe: it was counted, the East held 6500 against 7200 after the draw).
    private static CampaignSnapshot EastWithAGuardBoundElsewhere()
    {
        var s = Base();
        s.CapitalX = 560f;
        s.CapitalZ = 0f;
        s.Own.Add(Corps(1, 100f, 9563f, 3, FormationFlags.CapitalDefense));
        s.Own.Add(Corps(9, 200f, 4000f, 1));
        s.Own.Add(Corps(10, 220f, 4000f, 1));
        s.Own.Add(Corps(15, 150f, 5000f, 3, FormationFlags.InBattle));
        s.Own.Add(Corps(31, 760f, 8000f));             // the capital reserve: nearest the capital that has anything, standing in the East (met by it alone)
        s.Own.Add(Corps(2, 820f, 1000f));
        s.Own.Add(Corps(3, 830f, 6500f));
        s.Beliefs.Add(ExecutorFixtures.Belief(710f, 0f, 12000f));
        return s;
    }

    private static void ACapitalGuardBoundForAnotherSectorCoversNothingWhereItStands()
    {
        var s = EastWithAGuardBoundElsewhere();
        var day = Review(s);
        var sectors = ExecutorFixtures.Sectors(s);
        int east = sectors.SectorAt(820f, 0f);
        AssertTrue(sectors.SectorAt(560f, 0f) != east, "the capital lies in another sector than the East");
        AssertEqual(FormationRole.CapitalGuard, Of(day.Allocation, 31).Role, "31 is the capital guard");
        float need = 0f;
        foreach (var sp in day.Plan.Sectors) if (sp.SectorId == east) need = sp.HoldNeed;
        AssertNear(7200f, need, 1f, "the East need (belief-driven)");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day.Allocation, 2).Reason, "the reserve is sized without the guard: 2 is in it");
        AssertEqual(ForceAllocator.ReasonHoldReserve, Of(day.Allocation, 3).Reason, "and 3");
        AssertEqual(0, Roles(day.Allocation, FormationRole.Recon), "no scout is drawn: the East holds 7500 and would hold 6500 without the 1000");
        AssertTrue(Refused(day.Allocation), "and the refusal is named");
        AssertTrue(HeldAtEnd(day, east, false) >= need, "the East holds " + HeldAtEnd(day, east, false) + " against " + need);
        AssertEqual(0, day.Allocation.Shortfalls.Count, "no shortfall against the believed army");
    }

    // The capital reserve guards its sector when the capital stands in it: the allocation lists the sector as short only if it is short with the
    // guard counted, as ReserveRules.Hold counts it. The East holds 11000 (the guard's 6000, the hold 5000) against 7200 (verify r4 probe).
    private static void ACapitalSectorHeldByItsGuardListsNoShortfall()
    {
        var s = Base();
        s.CapitalX = 760f;
        s.CapitalZ = 0f;
        s.Own.Add(Corps(1, 100f, 9563f, 3, FormationFlags.CapitalDefense));
        s.Own.Add(Corps(9, 200f, 4000f, 1));
        s.Own.Add(Corps(10, 220f, 4000f, 1));
        s.Own.Add(Corps(15, 150f, 5000f, 3, FormationFlags.InBattle));
        s.Own.Add(Corps(31, 755f, 7000f));
        s.Own.Add(Corps(3, 790f, 5000f));
        s.Beliefs.Add(ExecutorFixtures.Belief(900f, 0f, 12000f));
        var day = Review(s);
        int east = ExecutorFixtures.Sectors(s).SectorAt(800f, 0f);
        AssertEqual(FormationRole.CapitalGuard, Of(day.Allocation, 31).Role, "31 is the capital guard");
        AssertEqual(FormationRole.Hold, Of(day.Allocation, 3).Role, "3 holds the East");
        AssertTrue(HeldAtEnd(day, east, true) >= 11000f, "the East holds " + HeldAtEnd(day, east, true) + " with its guard");
        AssertFalse(day.Allocation.Shortfalls.Contains(3), "so 3 is not listed as the carrier of a shortfall: " + string.Join(",", day.Allocation.Shortfalls));
        // A guard that stands elsewhere (the capital in the West) counts nothing in the East, which is then short.
        var away = Base();
        away.CapitalX = 560f;
        away.CapitalZ = 0f;
        away.Own.Add(Corps(1, 100f, 9563f, 3, FormationFlags.CapitalDefense));
        away.Own.Add(Corps(15, 150f, 5000f, 3, FormationFlags.InBattle));
        away.Own.Add(Corps(31, 760f, 8000f));
        away.Own.Add(Corps(3, 790f, 5000f));
        away.Beliefs.Add(ExecutorFixtures.Belief(710f, 0f, 12000f));          // near the capital, so it keeps a floor and a guard
        var other = Review(away);
        AssertTrue(other.Allocation.Shortfalls.Contains(3), "control: a guard bound for another sector covers nothing here, the East is short: " + string.Join(",", other.Allocation.Shortfalls));
        // ... and it is not yet in the capital's sector either: the West (the busy 5000 against 6000) is short until the guard arrives.
        AssertTrue(other.Allocation.Shortfalls.Contains(15), "nor does it cover the capital's sector from where it stands: " + string.Join(",", other.Allocation.Shortfalls));
    }

    // A stale belief (confidence 0.3) leaves a presumption: D11's East need is 0.6 x (the live 6000 + the faded 14000 placed there) = 12000,
    // the faded men held against at the same ratio as the seen ones, not the believed term alone (0.6 x 20000 x 0.3 = 3600). The need is
    // presumption-backed, so the scout goes, and the shortfall names the nearest immobile holder.
    private static CampaignSnapshot EastWithAStaleArmy(float busyAX, float busyBX, float confidence = 0.3f)
    {
        var s = SmokeUnion();
        var a = s.Own[s.IndexOfOwn(13)]; a.X = busyAX; a.WaypointX = busyAX; s.Own[s.IndexOfOwn(13)] = a;
        var b = s.Own[s.IndexOfOwn(14)]; b.X = busyBX; b.WaypointX = busyBX; s.Own[s.IndexOfOwn(14)] = b;
        var stale = ExecutorFixtures.Belief(1000f, 0f, 20000f);
        stale.Confidence01 = confidence;
        s.Beliefs.Add(stale);
        return s;
    }

    private static void ASectorWithAStaleBeliefAndAPresumptionIsPresumptionBacked()
    {
        var day = Review(EastWithAStaleArmy(790f, 800f));
        int east = ExecutorFixtures.Sectors(day.S).SectorAt(800f, 0f);
        float need = 0f, believed = 0f;
        foreach (var sp in day.Plan.Sectors) if (sp.SectorId == east) { need = sp.HoldNeed; believed = sp.BelievedHold; }
        AssertNear(12000f, need, 1f, "0.6 x (the live 6000 + the faded 14000): the remembered army sets the need");
        AssertTrue(believed > 0f && believed < need, "a believed force exists but does not drive it: " + believed);
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 2).Role, "so the East gives its scout though it cannot cover 12000 without it");
        AssertFalse(Refused(day.Allocation), "nothing refused");
        // A fresher belief (confidence 0.45: 0.6 x 20000 x 0.45 = 5400) puts a believed term above the floor, still under half the need that the
        // faded men share (D11): the sector is still presumption-backed, not belief-driven.
        var fresher = Review(EastWithAStaleArmy(790f, 800f, 0.45f));
        float need2 = 0f, believed2 = 0f;
        foreach (var sp in fresher.Plan.Sectors) if (sp.SectorId == east) { need2 = sp.HoldNeed; believed2 = sp.BelievedHold; }
        AssertNear(12000f, need2, 1f, "the live and the faded men still sum to the same need");
        AssertTrue(believed2 > TheaterPlanner.MinStrengthFallback && believed2 < need2 - 0.01f, "a believed term above the floor but under the need: " + believed2);
        AssertEqual(FormationRole.Recon, Of(fresher.Allocation, 2).Role, "and the East still gives its scout");
        AssertFalse(Refused(fresher.Allocation), "nothing refused");
    }

    // The threshold between the two causes of a need: the believed term close to the half of the need that the live men make (three remembered
    // armies of 20000 at confidence 0.49: the believed term is 0.6 x 29400 = 17640 against a need of 0.6 x (29400 live + 30600 faded) = 36000,
    // 0.49 of it) is still not what sets the need, so the scout still goes. Only a believed term of at least the rest of the need (D11: the live
    // men being at least the faded ones) makes the sector belief-driven.
    private static void ABelievedTermNearTheNeedButUnderItIsStillPresumptionBacked()
    {
        var s = SmokeUnion();
        foreach (float x in new[] { 1000f, 1004f, 1008f })
        {
            var stale = ExecutorFixtures.Belief(x, 0f, 20000f);
            stale.Confidence01 = 0.49f;
            s.Beliefs.Add(stale);
        }
        var day = Review(s);
        int east = ExecutorFixtures.Sectors(s).SectorAt(800f, 0f);
        float need = 0f, believed = 0f;
        foreach (var sp in day.Plan.Sectors) if (sp.SectorId == east) { need = sp.HoldNeed; believed = sp.BelievedHold; }
        AssertNear(36000f, need, 1f, "the live and the faded men set the need");
        AssertTrue(believed > 0.45f * need && believed < 0.5f * need - 0.01f, "the believed term is near half the need and under it: " + believed + " / " + need);
        AssertEqual(FormationRole.Recon, Of(day.Allocation, 2).Role, "the East still gives its scout");
        AssertFalse(Refused(day.Allocation), "nothing refused");
    }

    // The carrier of a short sector is its nearest immobile holder to the army it faces: 14 (x 800) beside the belief at 1000, and 13 when
    // the two busy formations swap places.
    private static void TheCarrierIsTheNearestImmobileHolder()
    {
        var near14 = Review(EastWithAStaleArmy(790f, 800f));
        AssertTrue(near14.Allocation.Shortfalls.Contains(14) && !near14.Allocation.Shortfalls.Contains(13), "x 800 is nearer 1000: " + string.Join(",", near14.Allocation.Shortfalls));
        var near13 = Review(EastWithAStaleArmy(810f, 790f));
        AssertTrue(near13.Allocation.Shortfalls.Contains(13) && !near13.Allocation.Shortfalls.Contains(14), "and with the places swapped it is 13: " + string.Join(",", near13.Allocation.Shortfalls));
    }

    // A record that leads, but by no more than the margin, is not a challenger: it breaks the run of reviews.
    private static void ALeadWithinTheMarginBreaksTheRecordWindow()
    {
        float close = -1f;
        for (float imp = 0.05f; imp <= 1f && close < 0f; imp += 0.05f)
        {
            var s = Records(0.5f, imp);
            float lead = ScoreOf(s, 700f) - ScoreOf(s, 400f);
            if (lead > 0.005f && lead < TheaterPlanner.SwitchMargin - 0.02f) close = imp;
        }
        AssertTrue(close > 0f, "a lead inside the margin exists for the fixture");
        var state = new PlannerState();
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "day 1");
        PlanOf(Records(Slight, Lean), state);          // 1
        PlanOf(Records(Slight, Lean), state);          // 2
        AssertNear(400f, PlanOf(Records(0.5f, close), state).MainObjectiveX, 0.5f, "a lead inside the margin: stays, and the run is broken");
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "the run starts again at one");
        AssertNear(400f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "two");
        AssertNear(700f, PlanOf(Records(Slight, Lean), state).MainObjectiveX, 0.5f, "three");
    }

    // The kept record, not whichever record scores best today, carries the odds deadband (F1): within OddsDeadband below its required ratio it
    // stays eligible for fewer than SwitchDays reviews, so a better-scoring record that has led for one review does not take the target.
    private static void TheKeptRecordHasTheOddsDeadband()
    {
        var state = new PlannerState();
        AssertNear(400f, PlanOf(Records(Lean, Slight), state).MainObjectiveX, 0.5f, "day 1");
        var s = Records(Slight, Lean, beliefAtFirst: 46000f);   // 28000 against 46000: 0.61 against a required 0.65, inside the deadband
        var plan = PlanOf(s, state);
        AssertNear(400f, plan.MainObjectiveX, 0.5f, "day 2: the kept record is retained by the deadband");
        AssertEqual(1, state.BelowOddsDays, "and the odds debt is counted");
    }

    // The latch follows the record kept: while the main effort stays on the scouted record the alternation of the other does not unscout it.
    private static void TheScoutedLatchFollowsTheKeptRecord()
    {
        var state = new PlannerState();
        var day1 = PlanOf(Records(Lean, Slight, beliefAtFirst: 800f), state);
        AssertNear(400f, day1.MainObjectiveX, 0.5f, "the scouted record");
        AssertTrue(day1.MainScouted && !day1.MainStaged, "scouted, not staged");
        for (int day = 2; day <= 5; day++)
        {
            var plan = PlanOf(day % 2 == 0 ? Records(Slight, Lean, 800f) : Records(Lean, Slight, 800f), state);
            AssertNear(400f, plan.MainObjectiveX, 0.5f, "day " + day + ": kept");
            AssertTrue(plan.MainScouted && !plan.MainStaged, "day " + day + ": and still scouted");
        }
        // Moving to the other record starts unscouted (it has not been seen).
        PlanOf(Records(Slight, Lean, 800f), state);
        PlanOf(Records(Slight, Lean, 800f), state);
        var moved = PlanOf(Records(Slight, Lean, 800f), state);
        AssertNear(700f, moved.MainObjectiveX, 0.5f, "after the window");
        AssertTrue(moved.MainStaged && !moved.MainScoutLatched, "the new record is staged until it is scouted");
    }

    private static void TheKeptRecordAndItsChallengerSurviveTheSidecar()
    {
        var state = new PlannerState();
        state.Restore(40, -1, 0, 3, 77L, currentTargetX: 400.5f, currentTargetZ: -2f, recordChallengerX: 700f, recordChallengerZ: 0f, recordChallengerDays: 2);
        var dto = SidecarDtoCodec.PlannerToDto(new PlannerSnapshot { State = state });
        var back = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>(Newtonsoft.Json.JsonConvert.SerializeObject(dto)));
        AssertNear(400.5f, back.State.CurrentTargetX, 0.001f, "kept x");
        AssertNear(-2f, back.State.CurrentTargetZ, 0.001f, "kept z");
        AssertNear(700f, back.State.RecordChallengerX, 0.001f, "challenger x");
        AssertNear(0f, back.State.RecordChallengerZ, 0.001f, "challenger z");
        AssertEqual(2, back.State.RecordChallengerDays, "and its reviews");
        var old = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>("{\"currentMainObjective\":40}"));
        AssertTrue(float.IsNaN(old.State.CurrentTargetX) && old.State.RecordChallengerDays == 0, "an older file keeps no record: the best one is taken");
    }

    private static void TheScoutedTargetSurvivesTheSidecarAndAnOldFileHasNone()
    {
        var state = new PlannerState();
        state.Restore(31, -1, 0, 4, 77L, scoutedDecision: 4, scoutedTargetX: 812.5f, scoutedTargetZ: -40f);
        var dto = SidecarDtoCodec.PlannerToDto(new PlannerSnapshot { State = state });
        var back = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>(Newtonsoft.Json.JsonConvert.SerializeObject(dto)));
        AssertNear(812.5f, back.State.ScoutedTargetX, 0.001f, "x restored");
        AssertNear(-40f, back.State.ScoutedTargetZ, 0.001f, "z restored");
        var old = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>("{\"currentMainObjective\":31,\"scoutedDecision\":4}"));
        AssertEqual(4, old.State.ScoutedDecision, "an older file's latch still reads");
        AssertTrue(float.IsNaN(old.State.ScoutedTargetX), "without a target: the latch speaks for the decision's target as before");
    }

    // ---- Q2: what the deferred rows are ----

    private static void TheMoverRowNamesAStagedGatheringThatWaits()
    {
        var g = new MoverGate();
        var tally = g.RunStagedInEnemyGround();
        AssertEqual(1, tally.Deferred, "the main effort waits");
        AssertEqual(1, tally.DeferredStaged, "and the row says it is a staged gathering, not the chapter roll");
        AssertContains(tally.Row(0), "deferredStaged=1", "in the row the smoke reads");
        AssertFalse(tally.Tempo.NeedsRoll, "no roll was evaluated (the roll is the march's)");

        var marching = g.RunScoutedClosedTempo();
        AssertEqual(1, marching.Deferred, "a scouted march waits for the roll");
        AssertEqual(0, marching.DeferredStaged, "which is not a staged gathering");
        AssertContains(marching.Row(0), "deferredStaged=0", "the row tells them apart");
    }

    private sealed class MoverGate
    {
        private static Allocation AllocOf(CampaignSnapshot s, TheaterPlan plan)
        {
            return TestClaims.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        }

        private (CampaignSnapshot s, TheaterPlan plan) Fixture(bool staged, float startX)
        {
            var s = ExecutorFixtures.Wide();
            s.Own.Add(ExecutorFixtures.Corps(1, 340f, 20000f));
            s.Beliefs.Add(ExecutorFixtures.Belief(100f, 0f, 5000f));
            var plan = ExecutorFixtures.HandPlan(s, new[] { 1 });
            plan.MainStaged = staged;
            plan.StartLineX = startX;
            plan.StartLineZ = 0f;
            return (s, plan);
        }

        internal MoverTally RunStagedInEnemyGround()
        {
            var (s, plan) = Fixture(true, 120f);   // x <= 150 is alliance 1's ground in the fake host
            return CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, AllocOf(s, plan), ExecutorFixtures.ClosedTempo()), new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State());
        }

        internal MoverTally RunScoutedClosedTempo()
        {
            var (s, plan) = Fixture(false, 0f);
            return CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, AllocOf(s, plan), ExecutorFixtures.ClosedTempo()), new FakeMoverHost(), new MoveTracker(), ExecutorFixtures.State());
        }
    }

    // ---- Q4: the row's category ----

    // The smoke's failures.jsonl held 26 rows, all [CampaignMover] with failed=0: the router read the counter's name as an event.
    private static void AZeroCountedFailureIsNotAFailureRow()
    {
        const string quiet = "[CampaignMover] alliance=0 issued=0 confirmed=0 failed=0 retried=0 gaveUp=0 queued=0 skippedPlayer=0 skippedBusy=0 deferred=1";
        var route = TelemetryTagPolicy.Route(quiet);
        AssertEqual(TelemetryCategory.Write, route.Category, "a mover row with nothing failed is a Write row");
        AssertEqual(TelemetrySeverity.Info, route.Severity, "at Info");
        AssertEqual(TelemetryCategory.Write, TelemetryTagPolicy.Route("[ReconMission] alliance=1 issued=2 retried=0 failed=0 queued=0 ordered=1 skippedPlayer=0 skippedBusy=0").Category, "and so is a recon summary");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 issued=0 confirmed=0 failed=2 retried=0 gaveUp=0 queued=0 skippedPlayer=0 skippedBusy=0 deferred=0").Category, "a failed count above zero is still a failure");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=10").Category, "ten is not zero");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=0.5").Category, "nor is 0.5");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=05").Category, "nor is 05");
        AssertEqual(TelemetryCategory.Write, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=0").Category, "a zero at the end of the line is a zero");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 failed=0 exception while moving").Category, "a zero counter does not hide a real exception word");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 move failed for corps 4").Category, "a failed word with no value is an event");
        AssertEqual(TelemetryCategory.Failure, TelemetryTagPolicy.Route("[CampaignMover] alliance=0 error=0 failed=1").Category, "a later non-zero failed still counts");
    }
}
