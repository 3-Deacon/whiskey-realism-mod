using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestFixtures;
using static TestHarness;

// The smoke of 2026-10-16 (docs/superpowers/specs/2026-10-14-campaign-planner-design.md § Smoke amendments 2026-10-16): the planner
// marched an unscouted mass at infinite odds. Fixtures are built from the smoke's numbers (the Union at 1861-06-02: 15 formations,
// 3 busy, 10 massed at 67669, objective 31 twice and 29, odds=inf/0.82 on every candidate, no belief near any target).
internal static class ScoutFirstTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("LowReadinessFormationsAreNeverMassed", LowReadinessFormationsAreNeverMassed),
            ("GaveUpFlipChainNeedsTheMassToBeRefusedFirst", GaveUpFlipChainNeedsTheMassToBeRefusedFirst),
            ("UnseenTargetIsUnscoutedAndItsOddsAreNotInfinite", UnseenTargetIsUnscoutedAndItsOddsAreNotInfinite),
            ("FreshBeliefNearTheTargetScoutsIt", FreshBeliefNearTheTargetScoutsIt),
            ("StaleBeliefDoesNotScoutAndIsRemembered", StaleBeliefDoesNotScoutAndIsRemembered),
            ("AnOwnViewerCoveringTheTargetScoutsIt", AnOwnViewerCoveringTheTargetScoutsIt),
            ("AfloatBeliefDoesNotScoutALandTarget", AfloatBeliefDoesNotScoutALandTarget),
            ("UnscoutedMainEffortIsStagedAtTheForwardMostMassMember", UnscoutedMainEffortIsStagedAtTheForwardMostMassMember),
            ("ScoutingTheMainObjectiveIsNotASwitch", ScoutingTheMainObjectiveIsNotASwitch),
            ("PlanRowNamesScoutedAndStaged", PlanRowNamesScoutedAndStaged),
            ("HomeDepartmentIsTheSectorsTheaterAtFirstReviewAndStaysUntilATransfer", HomeDepartmentIsTheSectorsTheaterAtFirstReviewAndStaysUntilATransfer),
            ("ACommanderChangeCarriesTheDepartmentOver", ACommanderChangeCarriesTheDepartmentOver),
            ("TheMainEffortDrawsFromItsOwnDepartmentFirst", TheMainEffortDrawsFromItsOwnDepartmentFirst),
            ("AFormationTwentyFiveMarchDaysAwayIsNeverPulledIn", AFormationTwentyFiveMarchDaysAwayIsNeverPulledIn),
            ("TheLosingDepartmentsHoldBlocksAMove", TheLosingDepartmentsHoldBlocksAMove),
            ("OneCrossDepartmentMovePerDecisionAndItChangesTheDepartment", OneCrossDepartmentMovePerDecisionAndItChangesTheDepartment),
            ("AnArmysCorpsGoTogether", AnArmysCorpsGoTogether),
            ("DepartmentsAndTheReinforcementSurviveTheSidecar", DepartmentsAndTheReinforcementSurviveTheSidecar),
            ("PlanRowNamesDepartmentsAndTheCrossDepartmentMove", PlanRowNamesDepartmentsAndTheCrossDepartmentMove),
            ("NoReadyFormationMeansNoScoutsAndNoMarch", NoReadyFormationMeansNoScoutsAndNoMarch),
            ("AnInfantryAdvanceGuardAndProbeScoutAnUnscoutedObjective", AnInfantryAdvanceGuardAndProbeScoutAnUnscoutedObjective),
            ("ADetachmentNeedsNoOffensiveStrengthMinimumButAScreenDoes", ADetachmentNeedsNoOffensiveStrengthMinimumButAScreenDoes),
            ("ACavalryScreenGoesOutWhenACavalryFormationExists", ACavalryScreenGoesOutWhenACavalryFormationExists),
            ("TheProbeStandsDownWhenTheObjectiveIsScouted", TheProbeStandsDownWhenTheObjectiveIsScouted),
            ("AScoutIsRecalledOnContactAndOnLowSupply", AScoutIsRecalledOnContactAndOnLowSupply),
            ("ReconRowCountsTheScoutPool", ReconRowCountsTheScoutPool),
            ("TheSmokeShapeNoLongerMassesEverything", TheSmokeShapeNoLongerMassesEverything),
            ("ASectorFacingEnemyGroundKeepsAHoldWithNothingBelieved", ASectorFacingEnemyGroundKeepsAHoldWithNothingBelieved),
            ("TheMassMarginScalesWithTheCommandersAttackBias", TheMassMarginScalesWithTheCommandersAttackBias),
            ("TheTraceNamesScoutingAndThePresumption", TheTraceNamesScoutingAndThePresumption),
            ("AllocationRowShowsTheMassAgainstItsNeed", AllocationRowShowsTheMassAgainstItsNeed),
            ("ABelievedEnemyStillRaisesTheHoldAbovePresumption", ABelievedEnemyStillRaisesTheHoldAbovePresumption),
            ("AScoutedDecisionStaysScoutedWhenTheBeliefDecays", AScoutedDecisionStaysScoutedWhenTheBeliefDecays),
            ("TheScoutedLatchSurvivesTheSidecarAndAnOldFileHasNone", TheScoutedLatchSurvivesTheSidecarAndAnOldFileHasNone),
            ("TheMainEffortTakesNoReinforcementWhenItsOwnDepartmentSuffices", TheMainEffortTakesNoReinforcementWhenItsOwnDepartmentSuffices),
            ("TheStartLineIsTheNativeEvenWhenTheReinforcementIsNearer", TheStartLineIsTheNativeEvenWhenTheReinforcementIsNearer),
            ("AProbeNeedsNoOffensiveStrengthMinimumEither", AProbeNeedsNoOffensiveStrengthMinimumEither),
            ("DepartmentsOfFormationsThatNoLongerExistArePruned", DepartmentsOfFormationsThatNoLongerExistArePruned),
            ("AnAppointmentCarriesTheDepartmentToTheNewKey", AnAppointmentCarriesTheDepartmentToTheNewKey),
            ("PlanPlusAllocateAllocationIsBoundedPerReview", PlanPlusAllocateAllocationIsBoundedPerReview),
        });
    }

    // ---- fixtures ----

    // Eleven areas (x 0..1000, relocation range 150), the own capital at x 0, own IIPs every 100 to x 800, enemy objectives 31 and 28
    // both at x 800 (the smoke's candidates were two targets of one objective in one sector), and `count` corps from x 100 on, 40 apart, 6700+ strong, ready, 0.9 morale.
    // x <= 600 is the West theater and x > 600 the East theater (TheaterClassifier.FromPosition), so a corps at x 700 is native to
    // the targets and one at x 300 is foreign.
    private static CampaignSnapshot Union(int count = 12, int readiness = 3)
    {
        var s = ExecutorFixtures.Line(0, 11);
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.65f;
        for (int i = 1; i <= 8; i++) s.Supply.Add(ExecutorFixtures.Iip(600 + i, 100f * i, 0));
        s.Objectives.Add(Objective(31, 800f, 0.8f));
        s.Objectives.Add(Objective(28, 800f, 0.1f));
        for (int i = 0; i < count; i++) s.Own.Add(Corps(i + 1, 100f + 40f * i, 6700f + 100f * i, readiness));
        return s;
    }

    private static ObjectiveRecord Objective(int id, float x, float importance)
    {
        return new ObjectiveRecord { ObjectiveId = id, X = x, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = importance };
    }

    private static FormationRecord Corps(int id, float x, float strength, int readiness = 3)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.SightRadius = 50f;
        f.Morale = 0.9f;
        f.ReadinessStep = readiness;
        return f;
    }

    private static TheaterPlan Plan(CampaignSnapshot s, PlannerState state = null, IReadOnlyList<GaveUpMove> gaveUp = null)
    {
        return TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, state ?? new PlannerState(), null, null, ExecutorFixtures.Speed(), out _, gaveUp);
    }

    // ---- D5 / D4 root cause: readiness ----

    // SetWaypoint refuses a readiness step 0/1 formation's march onto enemy ground without a word (91336-91355) and vanilla's own
    // offensive gate asks step >= 2 (14091): such a formation is neither massed nor allocated a march.
    private static void LowReadinessFormationsAreNeverMassed()
    {
        var s = Union(12, readiness: 1);
        var plan = Plan(s);
        AssertEqual(0, plan.MassInstanceIds.Count, "no formation of readiness step 1 is massed");
        AssertEqual(-1, plan.MainObjectiveId, "no force that can march: no main effort");

        var mixed = Union(12, readiness: 3);
        for (int i = 0; i < 6; i++)
        {
            var f = mixed.Own[i];
            f.ReadinessStep = i % 2;
            mixed.Own[i] = f;
        }
        var mixedPlan = Plan(mixed);
        for (int i = 0; i < 6; i++)
            AssertFalse(mixedPlan.MassInstanceIds.Contains(mixed.Own[i].InstanceId), "formation " + mixed.Own[i].InstanceId + " (step " + mixed.Own[i].ReadinessStep + ") is not massed");
        AssertTrue(mixedPlan.MassInstanceIds.Count > 0, "the ready ones are");
    }

    // The smoke's chain: formations the tracker gave up on for objective 31 left its mass, so objective 28 (whose mass still counted
    // them) won; they gave up there too and the main effort ended at -1. The tracker's per-objective exclusion is by design (a refusal
    // follows the target's state); what started the chain was sending formations that vanilla silently refuses.
    private static void GaveUpFlipChainNeedsTheMassToBeRefusedFirst()
    {
        var s = Union(8, readiness: 3);
        var state = new PlannerState();
        var first = Plan(s, state);
        AssertEqual(31, first.MainObjectiveId, "day 1: objective 31");

        var gaveUp31 = new List<GaveUpMove>();
        for (int i = 0; i < s.Own.Count; i++) gaveUp31.Add(new GaveUpMove { InstanceId = s.Own[i].InstanceId, TargetX = 800f, TargetZ = 0f, ObjectiveId = 31 });
        var second = Plan(s, state, gaveUp31);
        AssertEqual(28, second.MainObjectiveId, "every formation gave up on 31: the other objective takes over (P8, by design)");

        var gaveUpBoth = new List<GaveUpMove>(gaveUp31);
        for (int i = 0; i < s.Own.Count; i++) gaveUpBoth.Add(new GaveUpMove { InstanceId = s.Own[i].InstanceId, TargetX = 800f, TargetZ = 0f, ObjectiveId = 28 });
        var third = Plan(s, state, gaveUpBoth);
        AssertEqual(-1, third.MainObjectiveId, "and on 28 too: no main effort (the smoke's -1)");
    }

    // ---- D1: scout first ----

    private static TheaterPlanner.Candidate CandidateOf(CampaignSnapshot s, int objectiveId, int occurrence = 0)
    {
        var inputs = new TheaterPlanner.Inputs { S = s, Sectors = ExecutorFixtures.Sectors(s), Speed = ExecutorFixtures.Speed() };
        var list = new List<TheaterPlanner.Candidate>();
        TheaterPlanner.Evaluate(inputs, list);
        foreach (var c in list)
            if (c.ObjectiveId == objectiveId && occurrence-- == 0) return c;
        throw new Exception("no candidate " + objectiveId);
    }

    private static EnemyBelief Seen(float x, float strength, float confidence = 1f)
    {
        var b = ExecutorFixtures.Belief(x, 0f, strength);
        b.Confidence01 = confidence;
        return b;
    }

    // The smoke's rows: odds=inf/0.82 with e=0.00 for every candidate and no belief near any target. An unseen enemy is unknown, not none.
    private static void UnseenTargetIsUnscoutedAndItsOddsAreNotInfinite()
    {
        var s = Union();
        var c = CandidateOf(s, 31);
        AssertFalse(c.Scouted, "no belief and no viewer near the target: unscouted");
        // 5000 x (1 + 0.8): the importance of the enemy-held ground within the relocation range (objective 31's own 0.8).
        AssertNear(9000f, c.Presumed, 0.5f, "presumed threat = MinGarrisonStrength x (1 + importance)");
        AssertNear(9000f, c.Enemy, 0.5f, "the odds are judged against the presumption");
        AssertTrue(c.Ratio < float.MaxValue, "the odds are finite");
        AssertNear(c.Mass / 9000f, c.Ratio, 0.01f, "capacity over the presumed enemy");
        AssertTrue(c.Eligible, "an unscouted main effort may still be chosen (D1): 87000 against 9000");

        // The presumption does not make a weak force eligible.
        var weak = Union(1);
        var wc = CandidateOf(weak, 31);
        AssertFalse(wc.Eligible, "6700 against 9000 x 1.1 required is not enough");
    }

    private static void FreshBeliefNearTheTargetScoutsIt()
    {
        var s = Union();
        s.Beliefs.Add(Seen(850f, 3000f));
        var c = CandidateOf(s, 31);
        AssertTrue(c.Scouted, "a fresh land belief within the relocation range scouts the target");
        AssertEqual(0f, c.Presumed, "nothing presumed once scouted");
        AssertNear(3000f, c.Enemy, 0.5f, "the believed enemy");
        AssertTrue(c.Eligible, "87000 against 3000");

        var far = Union();
        far.Beliefs.Add(Seen(300f, 3000f));
        AssertFalse(CandidateOf(far, 31).Scouted, "a belief 500 away speaks for nothing near the target");
    }

    private static void StaleBeliefDoesNotScoutAndIsRemembered()
    {
        var s = Union();
        s.Beliefs.Add(Seen(850f, 20000f, 0.3f));
        var c = CandidateOf(s, 31);
        AssertFalse(c.Scouted, "confidence 0.3 is below the freshness bar (ContactEvidenceLedger.MinEscalationConfidence 0.5)");
        AssertNear(20000f, c.Presumed, 0.5f, "the aged-out sighting is remembered at its last-seen strength");
        AssertNear(20000f, c.Enemy, 0.5f, "and the odds are judged on it, not on the 6000 its confidence weights it to");

        var small = Union();
        small.Beliefs.Add(Seen(850f, 2000f, 0.3f));
        AssertNear(9000f, CandidateOf(small, 31).Presumed, 0.5f, "a small old sighting does not lower the floor");
    }

    private static void AnOwnViewerCoveringTheTargetScoutsIt()
    {
        var s = Union();
        var viewer = Corps(99, 780f, 6700f);   // 20 from the target, sight 50
        s.Own.Add(viewer);
        var c = CandidateOf(s, 31);
        AssertTrue(c.Scouted, "an own viewer's sight covers the target: what it sees, it sees");
        AssertEqual(0f, c.Presumed, "the empty target is a reading, not a presumption");
        AssertTrue(c.Ratio >= float.MaxValue, "seen empty: unbounded odds");

        var blind = Union();
        var shortSighted = Corps(99, 780f, 6700f);
        shortSighted.SightRadius = 10f;
        blind.Own.Add(shortSighted);
        AssertFalse(CandidateOf(blind, 31).Scouted, "sight 10 does not reach 20");
    }

    private static void AfloatBeliefDoesNotScoutALandTarget()
    {
        var s = Union();
        var b = Seen(850f, 3000f);
        b.Traits = EnemyTraits.Embarked;
        s.Beliefs.Add(b);
        AssertFalse(CandidateOf(s, 31).Scouted, "an army afloat says nothing about the land target (EstablishCampaignBattle 80632-80635)");
    }

    // D1: the mass assembles at a start line and does not march: the plan says so and names the line.
    private static void UnscoutedMainEffortIsStagedAtTheForwardMostMassMember()
    {
        var s = Union();
        var plan = Plan(s);
        AssertEqual(31, plan.MainObjectiveId, "objective 31 is the main effort");
        AssertFalse(plan.MainScouted, "unscouted");
        AssertTrue(plan.MainStaged, "so staged");
        AssertNear(9000f, plan.MainEnemyStrength, 0.5f, "the presumed enemy is what the odds were judged on");
        float forward = 0f;
        foreach (int id in plan.MassInstanceIds)
        {
            var f = s.Own[s.IndexOfOwn(id)];
            if (f.X > forward) forward = f.X;
        }
        AssertNear(forward, plan.StartLineX, 0.01f, "the start line is the forward-most mass member's position");
        AssertTrue(plan.StartLineX < 800f, "behind the objective");
        var main = plan.SectorById(plan.MainSectorId);
        AssertNear(800f, main.TargetX, 0.5f, "the sector's target stays the objective (recon and steering read it)");

        var scouted = Union();
        scouted.Beliefs.Add(Seen(850f, 3000f));
        var marching = Plan(scouted);
        AssertTrue(marching.MainScouted, "scouted");
        AssertFalse(marching.MainStaged, "so it marches");
    }

    // D4: the staged -> scouted step is neither a decision nor a switch: same objective, same serial, reason hold.
    private static void ScoutingTheMainObjectiveIsNotASwitch()
    {
        var s = Union();
        var state = new PlannerState();
        var day1 = Plan(s, state);
        AssertEqual(31, day1.MainObjectiveId, "day 1");
        AssertTrue(day1.MainStaged, "staged");
        int serial = state.DecisionSerial;

        s.Beliefs.Add(Seen(850f, 3000f));
        var day2 = Plan(s, state);
        AssertEqual(31, day2.MainObjectiveId, "day 2: the same main effort");
        AssertFalse(day2.MainStaged, "no longer staged");
        AssertEqual(serial, state.DecisionSerial, "no new decision");
        AssertEqual("hold", day2.SwitchReason, "not a switch");
        AssertEqual(0, state.BelowOddsDays, "and no odds debt");
    }

    private static void PlanRowNamesScoutedAndStaged()
    {
        var staged = TheaterPlanner.Summary(Plan(Union()));
        AssertContains(staged, "scouted=no staged=yes", "the row of an unscouted main effort");
        var s = Union();
        s.Beliefs.Add(Seen(850f, 3000f));
        AssertContains(TheaterPlanner.Summary(Plan(s)), "scouted=yes staged=no", "and of a scouted one");
        AssertContains(TheaterPlanner.Summary(Plan(Union(0))), "scouted=none staged=no", "and with no main effort");
    }

    // ---- D6 detail: home departments ----

    // Two fronts: objective 30 at x 300 (the West sector, areas 0-6) and 31 at x 900 (the East sector, areas 7-10), own IIPs every
    // 100 to x 900 so everything is in supply. A formation at x <= 650 stands in the West sector, x >= 700 in the East one.
    private static CampaignSnapshot Fronts(params FormationRecord[] own)
    {
        var s = ExecutorFixtures.Line(0, 11);
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.65f;
        for (int i = 1; i <= 9; i++) s.Supply.Add(ExecutorFixtures.Iip(600 + i, 100f * i, 0));
        s.Objectives.Add(Objective(30, 300f, 0.2f));
        s.Objectives.Add(Objective(31, 900f, 0.9f));
        s.Beliefs.Add(Seen(900f, 8000f));    // scouts objective 31: need 8000 x 1.1 x 1.25 = 11000
        foreach (var f in own) s.Own.Add(f);
        return s;
    }

    private static int Index(CampaignSnapshot s, int id) { return s.IndexOfOwn(id); }

    private static void HomeDepartmentIsTheSectorsTheaterAtFirstReviewAndStaysUntilATransfer()
    {
        var west = Corps(1, 300f, 9000f);
        var east = Corps(2, 800f, 9000f);
        var s = Fronts(west, east);
        var state = new PlannerState();
        Plan(s, state);
        AssertEqual(Theater.West, state.DepartmentOf(west.UnitKey), "corps 1 stands in the West sector at the first review");
        AssertEqual(Theater.East, state.DepartmentOf(east.UnitKey), "corps 2 in the East sector");

        // Corps 1 later stands in the East sector: its department is still West (a department is not where a formation happens to be).
        var later = Fronts(Corps(1, 800f, 9000f), east);
        Plan(later, state);
        AssertEqual(Theater.West, state.DepartmentOf(west.UnitKey), "kept until a deliberate transfer");

        // A formation first seen late takes the sector it stands in then.
        var third = Corps(3, 300f, 9000f);
        var joined = Fronts(Corps(1, 800f, 9000f), east, third);
        Plan(joined, state);
        AssertEqual(Theater.West, state.DepartmentOf(third.UnitKey), "first seen at x 300");
    }

    private static void ACommanderChangeCarriesTheDepartmentOver()
    {
        var state = new PlannerState();
        AssertEqual(Theater.West, state.Observe(7, "1st Corps:11", Theater.West), "assigned at the first look");
        AssertEqual(Theater.West, state.Observe(7, "1st Corps:99", Theater.East), "the appointment re-keyed the formation: same instance, same department");
        AssertEqual(Theater.Unknown, state.DepartmentOf("1st Corps:11"), "the old key is gone");
        AssertEqual(Theater.West, state.DepartmentOf("1st Corps:99"), "the new key has it");
        AssertEqual(Theater.East, state.Observe(8, "2nd Corps:12", Theater.East), "another formation takes its own standing");
    }

    // Three West corps and a small East one; the push is at the East objective. Old behaviour: every West corp the hold did not keep
    // was within 30 march-days and joined. Now: the East department first, and one West corps at most, as the reinforcement.
    private static void TheMainEffortDrawsFromItsOwnDepartmentFirst()
    {
        var s = Fronts(Corps(1, 800f, 8000f), Corps(2, 480f, 20000f), Corps(3, 500f, 20000f), Corps(4, 520f, 20000f));
        var state = new PlannerState();
        var plan = Plan(s, state);
        AssertEqual(31, plan.MainObjectiveId, "the push is at the East objective");
        int west = 0;
        foreach (int id in plan.MassInstanceIds)
            if (id != 1) west++;
        AssertEqual(1, west, "exactly one West corps joins: the reinforcement");
        AssertTrue(plan.MassInstanceIds.Contains(1), "the East corps is the core of the mass");
        AssertTrue(plan.ReinforcementInstanceId >= 2, "and the West corps is named the reinforcement: " + plan.ReinforcementInstanceId);
        AssertTrue(plan.MassInstanceIds.Contains(plan.ReinforcementInstanceId), "in the mass");
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        alloc.TryGet(plan.ReinforcementInstanceId, out var support);
        AssertEqual(FormationRole.Support, support.Role, "as the push's Support");
        int mainEffort = 0;
        foreach (var a in alloc.Assignments) if (a.Role == FormationRole.MainEffort) mainEffort++;
        AssertEqual(1, mainEffort, "the mass has one MainEffort formation (the East corps) and one Support");
    }

    private static void AFormationTwentyFiveMarchDaysAwayIsNeverPulledIn()
    {
        // Road speed in the fixture is 82 units a day: x -1100 is 2000 from the objective, 24 march-days. The old 30-day horizon pulled it
        // into any push. (The far corps is a West-department formation: it is the natural mass of the West objective, 17 days off.)
        var far = Corps(2, -1100f, 30000f);
        var guard = Corps(3, 300f, 7000f);
        guard.Flags |= FormationFlags.InBattle;       // a busy West guard holds the West (counted, never massed): the far corps could leave it
        var s = Fronts(Corps(1, 800f, 8000f), far, guard);
        var east = CandidateOf(s, 31);
        AssertNear(8000f, east.Mass, 0.5f, "the East push can concentrate its own 8000, not the 30000 24 march-days away");
        var plan = Plan(s, new PlannerState());
        AssertFalse(plan.MainObjectiveId == 31 && plan.MassInstanceIds.Contains(2), "never in the East push");
        AssertEqual(-1, plan.ReinforcementInstanceId, "not even as the one reinforcement (ReinforcementMaxDays " + TheaterPlanner.ReinforcementMaxDays + ")");
    }

    // The West department holds objective 30's front (a 5000 floor). Corps 5 is West but stands in the East sector: it could be pulled in,
    // unless the West then falls below its hold.
    private static void TheLosingDepartmentsHoldBlocksAMove()
    {
        foreach (float westHold in new[] { 4000f, 6000f })
        {
            var guard = Corps(2, 300f, westHold);
            var strayer = Corps(5, 800f, 20000f);
            var s = Fronts(Corps(1, 850f, 8000f), guard, strayer);
            var state = new PlannerState();
            state.RestoreDepartments(new[] { new KeyValuePair<string, Theater>(strayer.UnitKey, Theater.West), new KeyValuePair<string, Theater>(guard.UnitKey, Theater.West) });
            var plan = Plan(s, state);
            // Drawing the stray 20000 would leave the West its guard alone: allowed only when that still meets the 5000 floor.
            if (westHold < 5000f)
            {
                AssertFalse(plan.MassInstanceIds.Contains(5), "the West is left 4000 against a 5000 hold: corps 5 stays");
                AssertTrue(plan.ReinforcementInstanceId != 5, "and is not the reinforcement");
            }
            else
            {
                AssertTrue(plan.MassInstanceIds.Contains(5), "the West keeps 6000 against 5000: corps 5 may be drawn");
                AssertEqual(5, plan.ReinforcementInstanceId, "as the reinforcement");
            }
        }
    }

    private static void OneCrossDepartmentMovePerDecisionAndItChangesTheDepartment()
    {
        var w2 = Corps(2, 480f, 20000f);
        var w3 = Corps(3, 500f, 20000f);
        var w4 = Corps(4, 520f, 20000f);
        var s = Fronts(Corps(1, 800f, 8000f), w2, w3, w4);
        var state = new PlannerState();
        var plan = Plan(s, state);
        int pick = plan.ReinforcementInstanceId;
        AssertTrue(pick >= 2, "a reinforcement was drawn");
        var pickedKey = s.Own[Index(s, pick)].UnitKey;
        AssertEqual(Theater.East, state.DepartmentOf(pickedKey), "the move changed its home department");
        AssertEqual(Theater.West, plan.ReinforcementFrom, "it left the West");
        AssertEqual(Theater.East, plan.ReinforcementTo, "for the East");
        AssertEqual(pickedKey, state.ReinforcementKey, "the decision remembers it");
        int west = 0;
        foreach (var f in s.Own) if (f.InstanceId >= 2 && state.DepartmentOf(f.UnitKey) == Theater.West) west++;
        AssertEqual(2, west, "the other two stay West");

        // The next days of the same decision: the same one, and no second move.
        for (int day = 0; day < 3; day++)
        {
            var again = Plan(s, state);
            AssertEqual(pick, again.ReinforcementInstanceId, "the same reinforcement on day " + day);
            int drawn = 0;
            foreach (int id in again.MassInstanceIds) if (id >= 2) drawn++;
            AssertEqual(1, drawn, "never a second West corps in the same decision");
            // The move is announced once, on the review that makes it: later days name the corps and its new department only.
            string row = TheaterPlanner.Summary(again);
            AssertTrue(row.EndsWith("xdept=" + again.ReinforcementName + ":East", StringComparison.Ordinal), "the later row names the corps and where it is now, with no from>to: " + row);
        }

        // A new decision picks afresh.
        state.DecisionSerial++;
        var next = Plan(s, state);
        // The transfer made the picked corps East: in the new decision it is a native of the East, so the East's own 8000 + 20000 meet
        // the 11000 need and no West corps is drawn at all (the lock belongs to the old decision).
        AssertEqual(-1, next.ReinforcementInstanceId, "the new decision's own department suffices: no reinforcement");
        AssertTrue(next.MassInstanceIds.Contains(pick), "and the corps transferred last decision is part of its own department's mass");
        foreach (int id in next.MassInstanceIds) AssertTrue(id == 1 || id == pick, "nothing else is drawn: " + id);
    }

    // Two corps of one army (parent 90) and a lone corps: the push needs 11000. Taken corps by corps nearest first the army was split
    // (corps 3 went, its sibling corps 2 stayed); now the army goes whole or not at all.
    private static void AnArmysCorpsGoTogether()
    {
        var lone = Corps(1, 800f, 6000f);
        var army1 = Corps(2, 700f, 6000f);
        var army2 = Corps(3, 760f, 6000f);
        army1.ParentInstanceId = 90;
        army2.ParentInstanceId = 90;
        var s = Fronts(lone, army1, army2);
        var plan = Plan(s, new PlannerState());
        AssertEqual(31, plan.MainObjectiveId, "the East objective");
        AssertTrue(plan.MassInstanceIds.Contains(1), "the nearest formation");
        AssertTrue(plan.MassInstanceIds.Contains(2) && plan.MassInstanceIds.Contains(3), "and the army, both corps: " + string.Join(",", plan.MassInstanceIds));

        // Not the army's: lone corps first, then the army whole even if the lone corps alone would not do.
        var solo = Fronts(Corps(1, 800f, 12000f), army1, army2);
        var soloPlan = Plan(solo, new PlannerState());
        AssertEqual("1", string.Join(",", soloPlan.MassInstanceIds), "a lone corps that is enough leaves the army where it is");
    }

    private static void DepartmentsAndTheReinforcementSurviveTheSidecar()
    {
        var state = new PlannerState();
        state.Observe(1, "1st Corps:11", Theater.West);
        state.Observe(2, "2nd Corps:12", Theater.East);
        state.Transfer("1st Corps:11", Theater.East);
        state.Restore(31, -1, 0, 4, 77L, reinforcementKey: "1st Corps:11", reinforcementDecision: 4);
        var snapshot = new PlannerSnapshot { State = state };
        var dto = SidecarDtoCodec.PlannerToDto(snapshot);
        AssertEqual(2, dto.Departments.Count, "both departments written");
        AssertEqual("1st Corps:11", dto.ReinforcementKey, "the reinforcement written");
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(dto);
        var back = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>(json));
        AssertEqual(Theater.East, back.State.DepartmentOf("1st Corps:11"), "the transferred formation's department");
        AssertEqual(Theater.East, back.State.DepartmentOf("2nd Corps:12"), "the other's");
        AssertEqual("1st Corps:11", back.State.ReinforcementKey, "the reinforcement key");
        AssertEqual(4, back.State.ReinforcementDecision, "and its decision");

        // A file written before the fields restores with no department and no reinforcement.
        var old = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>("{\"currentMainObjective\":31}"));
        AssertEqual(0, old.State.Departments.Count, "no departments in an old file");
        AssertEqual(-1, old.State.ReinforcementDecision, "no reinforcement");
        // A department name that is not a Theater is skipped.
        var bad = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>("{\"departments\":[{\"unitKey\":\"a:1\",\"department\":\"Mars\"},{\"unitKey\":\"b:2\",\"department\":\"West\"}]}"));
        AssertEqual(1, bad.State.Departments.Count, "the unknown name is dropped");
    }

    private static void PlanRowNamesDepartmentsAndTheCrossDepartmentMove()
    {
        var s = Fronts(Corps(1, 800f, 8000f), Corps(2, 480f, 20000f), Corps(3, 300f, 6000f));
        var plan = Plan(s, new PlannerState());
        string row = TheaterPlanner.Summary(plan);
        AssertContains(row, "depts=East:1,West:2", "the home departments");
        AssertContains(row, "xdept=corps2:West>East", "the cross-department move, with its direction on the day it is made");
        var none = TheaterPlanner.Summary(Plan(Fronts(Corps(1, 800f, 30000f)), new PlannerState()));
        AssertContains(none, "xdept=none", "no move");
    }

    // ---- D7: real scouts ----

    private struct ReconDay
    {
        public TheaterPlan Plan;
        public List<ReconTask> Tasks;
        public Allocation Allocation;
        public ReconMissionLedger Ledger;
    }

    // The G1b sequence the runtime runs each review: plan, recon tasks, allocation, mission sync (ReconStep's own order).
    private static ReconDay Review(CampaignSnapshot s, PlannerState state, ReconMissionLedger ledger)
    {
        var sectors = ExecutorFixtures.Sectors(s);
        var day = new ReconDay { Ledger = ledger, Tasks = new List<ReconTask>() };
        ledger.OnReview(s, null);
        day.Plan = TheaterPlanner.Plan(s, sectors, null, state, null, null, ExecutorFixtures.Speed(), out _);
        ReconStep.Plan(s, sectors, day.Plan, new List<BattleHistoryRecord>(), 0, ledger, day.Tasks);
        day.Allocation = ForceAllocator.Allocate(day.Plan, s, sectors, null, day.Tasks, ledger);
        ledger.Sync(s, day.Tasks, day.Allocation);
        return day;
    }

    private static int Missions(ReconDay day, ReconKind kind)
    {
        int n = 0;
        foreach (var m in day.Ledger.Missions)
            if (m.Kind == kind && ReconMissionLedger.IsActive(m.State)) n++;
        return n;
    }

    // The smoke: unwatchedApproaches=3 and every mission count 0. Every candidate scout was a formation the allocator had already given
    // another role, or one vanilla's SetWaypoint refuses to march (readiness step <= 1): a day with nobody ready has tasks to plan (none are
    // even planned: no formation could take one) and nobody to send, and the mass is empty with it.
    private static void NoReadyFormationMeansNoScoutsAndNoMarch()
    {
        var s = Union(12, readiness: 1);
        var day = Review(s, new PlannerState(), new ReconMissionLedger());
        AssertEqual(0, day.Plan.MassInstanceIds.Count, "nobody to march");
        AssertEqual(0, day.Tasks.Count, "nobody could take a reconnaissance task, so none is planned");
        AssertEqual(0, day.Ledger.Missions.Count, "no mission");
        AssertEqual(0, ReconPlanner.ScoutPool(s), "the pool is empty");
        AssertTrue(ReconStep.CountsOf(s, day.Ledger, day.Tasks).Row(0).EndsWith("pool=0 free=0 mass=0"), "and the [Recon] row says so");
    }

    // 1861: no cavalry-majority group. The staged main effort still gets scouts: an infantry advance guard and a probe drawn from its
    // own mass, a screen on the main axis, whole existing campaign groups (vanilla has no AI detach API).
    private static void AnInfantryAdvanceGuardAndProbeScoutAnUnscoutedObjective()
    {
        var s = Union(12, readiness: 3);
        var state = new PlannerState();
        var day = Review(s, state, new ReconMissionLedger());
        AssertTrue(day.Plan.MainStaged, "staged: the objective is unscouted");
        foreach (var f in s.Own) AssertTrue(f.CavalryShare < ReconPlanner.MinCavalryShare, "no cavalry in this army");
        AssertEqual(1, Missions(day, ReconKind.Probe), "a probe goes toward the objective");
        AssertEqual(1, Missions(day, ReconKind.AdvanceGuard), "and an advance guard one sight radius ahead of the front");
        AssertTrue(Missions(day, ReconKind.Screen) >= 1, "and a screen on the unwatched approach");
        foreach (var m in day.Ledger.Missions)
        {
            int i = s.IndexOfOwn(m.InstanceId);
            AssertTrue(i >= 0 && s.Own[i].Strength <= 0.25f * 12 * 7300f || m.Kind == ReconKind.Screen, "a detachment of the main body: at most a quarter of it");
        }
        // The probe's post is the standoff short of the objective: its sight (50) covers the target (800) from 40 short.
        foreach (var m in day.Ledger.Missions)
            if (m.Kind == ReconKind.Probe) AssertNear(760f, m.HoldX, 1f, "the probe holds 0.8 x sight short of the objective");
    }

    private static void ADetachmentNeedsNoOffensiveStrengthMinimumButAScreenDoes()
    {
        var s = Union(0);
        var small = Corps(1, 700f, 2500f);                    // under aiminimumstrengthformovement (5000)
        var big = Corps(2, 500f, 30000f);
        s.Own.Add(small);
        s.Own.Add(big);
        s.Beliefs.Clear();
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        var guard = new ReconTask { Kind = ReconKind.AdvanceGuard, SectorId = plan.MainSectorId, FromX = 500f, FromZ = 0f, HoldX = 600f, HoldZ = 0f, Priority = 1f, Reason = "t" };
        var screen = new ReconTask { Kind = ReconKind.Screen, SectorId = plan.MainSectorId, FromX = 500f, FromZ = 0f, HoldX = 600f, HoldZ = 0f, Priority = 1f, Reason = "t" };
        var taken = new List<int>();
        int guardPick = ReconPlanner.PickFormation(s, sectors, plan, null, guard, taken, true);
        int screenPick = ReconPlanner.PickFormation(s, sectors, plan, null, screen, taken, true);
        AssertEqual(1, guardPick, "the small division leads the advance (a detachment of the main body)");
        AssertEqual(2, screenPick, "a screen is held to vanilla's minimum strength");
    }

    private static void ACavalryScreenGoesOutWhenACavalryFormationExists()
    {
        var s = Union(12, readiness: 3);
        var horse = Corps(40, 560f, 6000f);
        horse.CavalryShare = 0.8f;
        s.Own.Add(horse);
        var day = Review(s, new PlannerState(), new ReconMissionLedger());
        bool cavalryScreens = false;
        foreach (var m in day.Ledger.Missions)
            if (m.Kind == ReconKind.Screen && m.InstanceId == 40) cavalryScreens = true;
        AssertTrue(cavalryScreens, "the cavalry formation takes a screen");
        AssertEqual(ReconOrders.ScoutingOrder, ReconOrders.CavalryOrderFor(ReconKind.Screen, ReconState.Assigned, horse.CavalryShare, WlChainClass.None), "with vanilla's scouting order 121 (the executor sends it)");
        foreach (var m in day.Ledger.Missions)
            if (m.Kind != ReconKind.Screen) AssertTrue(m.InstanceId != 40, "the cavalry is not spent on a detachment's task");
    }

    private static void TheProbeStandsDownWhenTheObjectiveIsScouted()
    {
        var s = Union(12, readiness: 3);
        var state = new PlannerState();
        var ledger = new ReconMissionLedger();
        var day1 = Review(s, state, ledger);
        AssertEqual(1, Missions(day1, ReconKind.Probe), "day 1: a probe");
        // Day 2: its sight and a fresh belief answer the question.
        s.Beliefs.Add(Seen(850f, 3000f));
        var day2 = Review(s, state, ledger);
        AssertTrue(day2.Plan.MainScouted, "the objective is scouted");
        AssertEqual(0, Missions(day2, ReconKind.Probe), "the probe's question is answered: its mission stands down");
        foreach (var task in day2.Tasks) AssertTrue(task.Kind != ReconKind.Probe, "and no probe task is planned");
    }

    private static void AScoutIsRecalledOnContactAndOnLowSupply()
    {
        var s = Union(12, readiness: 3);
        var state = new PlannerState();
        var ledger = new ReconMissionLedger();
        var day1 = Review(s, state, ledger);
        int guardId = -1;
        foreach (var m in ledger.Missions) if (m.Kind == ReconKind.AdvanceGuard) guardId = m.InstanceId;
        AssertTrue(guardId > 0, "an advance guard is out");
        // Low supply: raw Supply01 < 0.5 recalls it (vanilla's raid abort on the halved metric, 9092).
        var thin = s.Own[s.IndexOfOwn(guardId)];
        thin.Supply01 = 0.3f;
        s.Own[s.IndexOfOwn(guardId)] = thin;
        ledger.OnReview(s, day1.Allocation);
        bool recalled = false;
        foreach (var m in ledger.Missions) if (m.InstanceId == guardId && m.State == ReconState.Recalled) recalled = true;
        AssertTrue(recalled, "low supply recalls the advance guard");
    }

    private static void ReconRowCountsTheScoutPool()
    {
        var s = Union(12, readiness: 3);
        var day = Review(s, new PlannerState(), new ReconMissionLedger());
        AssertEqual(12, ReconPlanner.ScoutPool(s), "every ready, unbusy, orderable formation");
        AssertContains(ReconStep.CountsOf(s, day.Ledger, day.Tasks, day.Allocation).Row(0), " pool=12 free=", "the [Recon] row");
        var mixed = Union(12, readiness: 3);
        var busy = mixed.Own[0];
        busy.Flags |= FormationFlags.InBattle;
        mixed.Own[0] = busy;
        var slow = mixed.Own[1];
        slow.ReadinessStep = 1;
        mixed.Own[1] = slow;
        AssertEqual(10, ReconPlanner.ScoutPool(mixed), "a busy and an unready formation are not in it");
    }

    // ---- the smoke's shape, D2 and D3 ----

    // The smoke: 15 formations (3 busy), 10 massed at 67669 against nothing seen, a Hold and three EconomyOfForce sectors. Now: the
    // presumed 9000 needs 12375, the mass is the two nearest corps, the rest keep their fronts, and the push is staged.
    private static void TheSmokeShapeNoLongerMassesEverything()
    {
        var s = Union(15, readiness: 3);
        for (int i = 0; i < 3; i++)
        {
            var busy = s.Own[i];
            busy.Flags |= FormationFlags.InBattle;
            s.Own[i] = busy;
        }
        var plan = Plan(s);
        AssertEqual(31, plan.MainObjectiveId, "objective 31 as in the smoke");
        AssertNear(12375f, plan.MainNeedStrength, 1f, "the need: 9000 x 1.1 x 1.25");
        AssertTrue(plan.MainCapacityStrength > 60000f, "the capacity is what the old mass was: " + plan.MainCapacityStrength);
        AssertTrue(plan.MassInstanceIds.Count <= 3, "the mass is the force it needs, not ten of fifteen: " + plan.MassInstanceIds.Count);
        AssertTrue(plan.MassStrength >= plan.MainNeedStrength, "and covers the need");
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        int main = 0, hold = 0;
        foreach (var a in alloc.Assignments)
        {
            if (a.Role == FormationRole.MainEffort) main++;
            if (a.Role == FormationRole.Hold) hold++;
        }
        AssertEqual(plan.MassInstanceIds.Count, main, "MainEffort is exactly the mass");
        AssertTrue(hold >= 12 - 3, "everything else keeps its front's role (and the three busy hold in place): " + hold);
        string row = ForceAllocator.Summary(alloc, plan, PlayerCommandClass.BelowMapLevel, 1f, 0);
        AssertContains(row, "staged=yes", "the allocation row says the push is staged");
    }

    // D2: a sector facing enemy-held ground keeps a hold of max(HoldRatio x believed, MinGarrison, presumed) with nothing believed there.
    private static void ASectorFacingEnemyGroundKeepsAHoldWithNothingBelieved()
    {
        var guard = Corps(2, 300f, 7000f);
        var s = Fronts(Corps(1, 800f, 30000f), guard);
        s.Beliefs.Clear();                        // nothing believed anywhere: both objectives unscouted
        var plan = Plan(s);
        var sectors = ExecutorFixtures.Sectors(s);
        int west = sectors.SectorAt(300f, 0f);
        float need = 0f;
        foreach (var sp in plan.Sectors) if (sp.SectorId == west) need = sp.HoldNeed;
        // 5000 x (1 + 0.2, objective 30's importance): the presumption, above the 5000 floor.
        AssertNear(6000f, need, 0.5f, "the West sector faces objective 30: presumed 6000");
        var alloc = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        alloc.TryGet(2, out var g);
        AssertEqual(FormationRole.Hold, g.Role, "the guard holds");
        AssertEqual(ForceAllocator.ReasonHoldReserve, g.Reason, "as the sector's hold reserve, though nothing is believed there");
        AssertFalse(plan.MassInstanceIds.Contains(2), "and is not in any push");

        // A sector that faces no enemy-held ground (no enemy objective within its reach) owes nothing.
        var quiet = Union(0);
        quiet.Objectives.Clear();
        quiet.Objectives.Add(new ObjectiveRecord { ObjectiveId = 90, X = 300f, Z = 0f, HolderAlliance = 0, Available = false });
        quiet.Own.Add(Corps(1, 300f, 9000f));
        var quietPlan = Plan(quiet);
        foreach (var sp in quietPlan.Sectors) AssertEqual(0f, sp.HoldNeed, "no enemy ground, no hold need (sector " + sp.SectorId + ")");
    }

    private static void ABelievedEnemyStillRaisesTheHoldAbovePresumption()
    {
        var guard = Corps(2, 300f, 20000f);
        var s = Fronts(Corps(1, 800f, 30000f), guard);
        s.Beliefs.Add(Seen(320f, 40000f));         // a fresh sighting in the West: 0.6 x 40000 = 24000
        var plan = Plan(s);
        float need = 0f;
        foreach (var sp in plan.Sectors) if (sp.SectorId == ExecutorFixtures.Sectors(s).SectorAt(300f, 0f)) need = sp.HoldNeed;
        AssertNear(24000f, need, 1f, "HoldRatio x believed beats the floor and the presumption");

        // A fresh, small sighting: the floor (nothing is presumed once the sector is scouted, but a front is never held by less).
        var small = Fronts(Corps(1, 800f, 30000f), Corps(2, 300f, 20000f));
        small.Beliefs.Add(Seen(320f, 1000f));
        float floor = 0f;
        foreach (var sp in Plan(small).Sectors) if (sp.SectorId == ExecutorFixtures.Sectors(small).SectorAt(300f, 0f)) floor = sp.HoldNeed;
        AssertNear(ForceAllocator.MinGarrisonStrength, floor, 0.5f, "0.6 x 1000 is under the 5000 floor: the floor, not the presumption (6000)");
    }

    private static void TheMassMarginScalesWithTheCommandersAttackBias()
    {
        AssertNear(10000f * 1.1f * 1.25f, TheaterPlanner.NeedFor(10000f, 1.1f, 0f), 1f, "a neutral commander: 25%");
        AssertNear(10000f * 1.1f * 1.125f, TheaterPlanner.NeedFor(10000f, 1.1f, 1f), 1f, "a bold one: 12.5%");
        AssertNear(10000f * 1.1f * 1.375f, TheaterPlanner.NeedFor(10000f, 1.1f, -1f), 1f, "a cautious one: 37.5%");
        AssertNear(10000f * 1.1f * 1.125f, TheaterPlanner.NeedFor(10000f, 1.1f, 5f), 1f, "the bias is clamped to [-1, 1]");
        AssertNear(ForceAllocator.MinGarrisonStrength, TheaterPlanner.NeedFor(0f, 1.1f, 0f), 0.01f, "never below the smallest force the AI moves");
        AssertNear(ForceAllocator.MinGarrisonStrength, TheaterPlanner.NeedFor(1000f, 1.1f, 0f), 0.01f, "a small enemy still needs the floor");
    }

    private static void TheTraceNamesScoutingAndThePresumption()
    {
        var s = Union();
        TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out string trace);
        AssertContains(trace, "unscouted est=9000 pres=9000", "an unseen target: the presumption is the estimate");
        var seen = Union();
        seen.Beliefs.Add(Seen(850f, 3000f));
        TheaterPlanner.Plan(seen, ExecutorFixtures.Sectors(seen), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out string traceSeen);
        AssertContains(traceSeen, " scouted est=3000 pres=0", "a sighting: the believed enemy, nothing presumed");
        AssertFalse(trace.Contains("odds=inf"), "no candidate reads 'inf' odds against an unseen enemy");
    }

    private static void AllocationRowShowsTheMassAgainstItsNeed()
    {
        var s = Union();
        var plan = Plan(s);
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
        string row = ForceAllocator.Summary(alloc, plan, PlayerCommandClass.BelowMapLevel, 1f, 0);
        AssertContains(row, " need=12375 ", "the need");
        AssertContains(row, " cap=", "the capacity");
        AssertContains(row, " mass=", "and the mass it sized");
    }

    // ---- verify round 1 ----

    // F1: the verifier's day-8 decay. Day 1: the target is scouted and the march goes; day 8: the belief has decayed to 0.45 (under
    // the 0.5 that counts as fresh). The decision is the same, so the march is not pulled back to a start line.
    private static void AScoutedDecisionStaysScoutedWhenTheBeliefDecays()
    {
        var s = Union(12, readiness: 3);
        s.Beliefs.Add(Seen(850f, 3000f));
        var state = new PlannerState();
        var day1 = Plan(s, state);
        AssertEqual(31, day1.MainObjectiveId, "objective 31");
        AssertTrue(day1.MainScouted && !day1.MainStaged && !day1.MainScoutLatched, "day 1: scouted on a fresh belief, not staged, not merely latched");
        int serial = state.DecisionSerial;

        var decayed = Union(12, readiness: 3);
        decayed.Beliefs.Add(Seen(850f, 3000f, 0.45f));
        var day8 = Plan(decayed, state);
        AssertEqual(31, day8.MainObjectiveId, "day 8: the same main effort");
        AssertEqual(serial, state.DecisionSerial, "the same decision");
        AssertFalse(CandidateOf(decayed, 31).Scouted, "the candidate's own picture is no longer fresh (its estimate stays honest)");
        AssertTrue(day8.MainScoutLatched, "but the decision stays scouted");
        AssertFalse(day8.MainStaged, "so the released march is not re-staged");
        AssertContains(TheaterPlanner.Summary(day8), "scouted=latched staged=no", "the row says so");
        var alloc = ForceAllocator.Allocate(day8, decayed, ExecutorFixtures.Sectors(decayed), null, null, null);
        foreach (int id in day8.MassInstanceIds)
        {
            alloc.TryGet(id, out var a);
            AssertNear(800f, a.TargetX, 0.5f, "corps " + id + " still marches on the objective, not on a start line");
        }

        // A new decision starts unscouted: the same decayed picture is staged, whether the state is new or the decision serial moved on.
        var fresh = Plan(decayed, new PlannerState());
        AssertTrue(fresh.MainStaged && !fresh.MainScoutLatched, "no latch carries over to a fresh state");
        state.DecisionSerial++;
        var later = Plan(decayed, state);
        AssertTrue(later.MainStaged && !later.MainScoutLatched, "nor to the next decision of the same state");
    }

    private static void TheScoutedLatchSurvivesTheSidecarAndAnOldFileHasNone()
    {
        var state = new PlannerState();
        state.Restore(31, -1, 0, 4, 77L, scoutedDecision: 4);
        var dto = SidecarDtoCodec.PlannerToDto(new PlannerSnapshot { State = state });
        AssertEqual(4, dto.ScoutedDecision, "written");
        var back = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>(Newtonsoft.Json.JsonConvert.SerializeObject(dto)));
        AssertEqual(4, back.State.ScoutedDecision, "restored with its decision");
        var old = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>("{\"currentMainObjective\":31}"));
        AssertEqual(-1, old.State.ScoutedDecision, "a file written before the field has no latch");
    }

    // V09: the reinforcement is a last resort. The East's own 20000 meets the 11000 need, so the nearby West corps stays home.
    private static void TheMainEffortTakesNoReinforcementWhenItsOwnDepartmentSuffices()
    {
        var s = Fronts(Corps(1, 800f, 20000f), Corps(2, 560f, 20000f));
        var plan = Plan(s, new PlannerState());
        AssertEqual(31, plan.MainObjectiveId, "the push is at the East objective");
        AssertEqual(-1, plan.ReinforcementInstanceId, "no cross-department reinforcement");
        AssertFalse(plan.MassInstanceIds.Contains(2), "the West corps is not in the mass");
        AssertTrue(plan.MassInstanceIds.Contains(1), "the East corps is");
    }

    // V33: the staged start line is where the native mass stands, even when the one reinforcement is nearer the target. The East corps
    // stands off the road to the north (z 400, farther from the objective than the West reinforcement on the road at x 590).
    private static void TheStartLineIsTheNativeEvenWhenTheReinforcementIsNearer()
    {
        var native = Corps(1, 700f, 8000f);
        native.Z = 400f;
        native.WaypointZ = 400f;
        var s = Fronts(native, Corps(2, 590f, 20000f));
        s.Beliefs.Clear();                      // unscouted: staged
        s.Objectives[s.Objectives.FindIndex(o => o.ObjectiveId == 30)] = new ObjectiveRecord { ObjectiveId = 30, X = 300f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 0.2f };   // our own: the West has no push of its own
        var plan = Plan(s, new PlannerState());
        AssertEqual(31, plan.MainObjectiveId, "the push is at the East objective");
        AssertEqual(2, plan.ReinforcementInstanceId, "the West corps is the reinforcement (the native 8000 is short of the need) mass=[" + string.Join(",", plan.MassInstanceIds) + "] depts=" + plan.DepartmentCounts);
        AssertTrue(plan.MainStaged, "unscouted: staged");
        AssertNear(700f, plan.StartLineX, 0.5f, "the line is the native's position x");
        AssertNear(400f, plan.StartLineZ, 0.5f, "and z, not the nearer reinforcement's");
    }

    // V34: a probe is a detachment too: a division under vanilla's 5000-man offensive minimum may go out as one.
    private static void AProbeNeedsNoOffensiveStrengthMinimumEither()
    {
        var s = Union(0);
        s.Own.Add(Corps(1, 700f, 2500f));          // under aiminimumstrengthformovement
        s.Own.Add(Corps(2, 500f, 30000f));
        s.Beliefs.Clear();
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = TheaterPlanner.Plan(s, sectors, null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        var probe = new ReconTask { Kind = ReconKind.Probe, SectorId = plan.MainSectorId, FromX = 700f, FromZ = 0f, HoldX = 760f, HoldZ = 0f, Priority = 1f, Reason = "probe" };
        var taken = new List<int>();
        AssertEqual(1, ReconPlanner.PickFormation(s, sectors, plan, null, probe, taken, true), "the small division is the probe");
    }

    // A department is dropped only after its key has been missing for PruneAfterMissedReviews reviews in a row: a formation missing from one
    // snapshot (inactive, invisible for a moment) keeps its home, and a failed read (an empty snapshot) counts nothing.
    private static void DepartmentsOfFormationsThatNoLongerExistArePruned()
    {
        AssertEqual(10, PlannerState.PruneAfterMissedReviews, "ten reviews in a row: the threshold is pinned, not read back");
        int n = PlannerState.PruneAfterMissedReviews;
        var state = new PlannerState();
        state.BeginObservation();
        state.Observe(1, "1st Corps:11", Theater.West);
        state.Observe(2, "2nd Corps:12", Theater.East);
        AssertEqual(0, state.PruneUnobserved(), "both were seen");
        for (int day = 1; day < n; day++)
        {
            state.BeginObservation();
            state.Observe(1, "1st Corps:11", Theater.West);   // corps 2 is not in these snapshots
            AssertEqual(0, state.PruneUnobserved(), "missing for " + day + " review(s): kept");
        }
        // Back for a review: the count starts over.
        state.BeginObservation();
        state.Observe(1, "1st Corps:11", Theater.West);
        state.Observe(2, "2nd Corps:12", Theater.East);
        AssertEqual(0, state.PruneUnobserved(), "seen again");
        for (int day = 1; day <= n; day++)
        {
            state.BeginObservation();
            state.Observe(1, "1st Corps:11", Theater.West);
            int dropped = state.PruneUnobserved();
            AssertEqual(day == n ? 1 : 0, dropped, "missing for " + day + " reviews in a row");
        }
        AssertEqual(Theater.Unknown, state.DepartmentOf("2nd Corps:12"), "gone after the run");
        AssertEqual(Theater.West, state.DepartmentOf("1st Corps:11"), "the survivor keeps its own");

        // Through the planner: the inactive case (absent for fewer than N reviews) keeps the department, and a read failure (an empty
        // snapshot, however many times) neither prunes nor counts.
        var planned = new PlannerState();
        Plan(Fronts(Corps(1, 800f, 8000f), Corps(2, 300f, 7000f)), planned);
        AssertEqual(2, planned.Departments.Count, "two formations observed");
        for (int day = 0; day < n - 1; day++) Plan(Fronts(Corps(1, 800f, 8000f)), planned);
        AssertEqual(2, planned.Departments.Count, "inactive for " + (n - 1) + " reviews: kept");
        for (int day = 0; day < 3 * n; day++) Plan(Fronts(), planned);
        AssertEqual(2, planned.Departments.Count, "a snapshot with no formations (a failed read) prunes and counts nothing");
        Plan(Fronts(Corps(1, 800f, 8000f), Corps(2, 300f, 7000f)), planned);
        for (int day = 0; day < n; day++) Plan(Fronts(Corps(1, 800f, 8000f)), planned);
        AssertEqual(1, planned.Departments.Count, "gone for " + n + " reviews in a row: pruned");
    }

    // Concern 3: an appointment changes a commander and so the unit key (name:commander); the department follows at once, so a save
    // written before the next review keeps it.
    private static void AnAppointmentCarriesTheDepartmentToTheNewKey()
    {
        RekeyAtSaveFollowsAnyCommanderChange();
        var state = new PlannerState();
        state.Observe(7, "1st Corps:11", Theater.West);
        state.RekeyInstance(7, "1st Corps:42");
        AssertEqual(Theater.West, state.DepartmentOf("1st Corps:42"), "the department follows the commander change");
        AssertEqual(Theater.Unknown, state.DepartmentOf("1st Corps:11"), "and the old key is gone");
        state.RekeyInstance(99, "x:1");           // an instance the planner never saw: nothing happens
        AssertEqual(Theater.Unknown, state.DepartmentOf("x:1"), "unknown instance is ignored");
        string exec = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CommanderAppointmentRuntime.cs");
        AssertContains(exec, "CampaignPlannerRuntime.RekeyDepartment(alliance, target.GetInstanceID()", "the executor re-keys the target the moment the appointment takes");
        AssertContains(exec, "CampaignPlannerRuntime.RekeyDepartment(alliance, old.GetInstanceID()", "and the appointee's former command, whose commander changed too");
    }

    // The review allocates (lists, the candidate set, the plan and allocation objects) once per alliance per daily review; it is not a
    // per-frame path. Bounded here so a regression to per-formation or per-candidate garbage shows: measured about 13 KB for 15 formations
    // and 3 candidates (the plan, the allocation, the candidate and posture lists); the bound is about 3.5 times that. It is not allocation-free
    // on purpose: the plan and the allocation are new published objects each day.
    private static void PlanPlusAllocateAllocationIsBoundedPerReview()
    {
        var s = Union(15, readiness: 3);
        var sectors = ExecutorFixtures.Sectors(s);
        var state = new PlannerState();
        TheaterPlanner.Plan(s, sectors, null, state, null, null, ExecutorFixtures.Speed(), out _);   // warm: JIT, the state's dictionaries
        long before = TestFixtures.BeginAllocationWindow();
        var plan = TheaterPlanner.Plan(s, sectors, null, state, null, null, ExecutorFixtures.Speed(), out _);
        var alloc = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        long bytes = TestFixtures.AllocatedSince(before);
        AssertTrue(alloc.Assignments.Count == 15, "all fifteen are assigned");
        AssertTrue(bytes < AllocationBound, "Plan + Allocate for 15 formations allocated " + bytes + " bytes (bound " + AllocationBound + ")");
    }

    private const long AllocationBound = 48000;

    // Landing round, ruling 6: just before the sidecar is written every known formation is re-keyed from its live name and commander, so a
    // commander changed between the review and the save (vanilla's job 24, a death) is saved under the key the next load computes.
    private static void RekeyAtSaveFollowsAnyCommanderChange()
    {
        var state = new PlannerState();
        state.Observe(7, "1st Corps:11", Theater.West);
        state.Observe(8, "2nd Corps:12", Theater.East);
        state.Restore(31, -1, 0, 3, 5L, reinforcementKey: "1st Corps:11", reinforcementDecision: 3);
        state.RekeyFromLive(new[]
        {
            new KeyValuePair<int, string>(7, "1st Corps:99"),     // its commander changed after the last review
            new KeyValuePair<int, string>(8, "2nd Corps:12"),     // unchanged
            new KeyValuePair<int, string>(500, "unseen:1")        // a unit the planner never observed
        });
        var dto = SidecarDtoCodec.PlannerToDto(new PlannerSnapshot { State = state });
        var keys = new List<string>();
        foreach (var d in dto.Departments) keys.Add(d.UnitKey + "=" + d.Department);
        AssertContains(string.Join(",", keys), "1st Corps:99=West", "saved under the new key");
        AssertContains(string.Join(",", keys), "2nd Corps:12=East", "the unchanged one as it was");
        AssertFalse(string.Join(",", keys).Contains("1st Corps:11"), "and not under the old one");
        AssertEqual("1st Corps:99", dto.ReinforcementKey, "the decision's reinforcement follows its formation");
        string runtime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        AssertContains(BodyOf(runtime, "internal static PlannerSnapshot Export("), "RekeyDepartmentsFromLive(alliance);", "the export re-keys before it writes");
        AssertContains(BodyOf(runtime, "private static void RekeyDepartmentsFromLive("), "SnapshotRules.UnitKey(((UnityEngine.Object)reg).name, reg.commander)", "from the live name and commander");
    }
}
