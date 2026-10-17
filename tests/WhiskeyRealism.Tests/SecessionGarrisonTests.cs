using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestFixtures;
using static TestHarness;

// Smoke amendments 2026-10-16, D8: vanilla's secession rules for Kentucky (state 14, objective 15) and Missouri (state 22, objective 16).
// IsDeactivated (178966-178973): off while the state is the CSA's or the CSA's support there is below 0.45. CheckIfAccomplished
// (179259-179288): done when the CSA owns Louisville / St.Louis and nation.alliancetroops[0] is false (no Union campaign group in the
// state). The Union keeps a garrison inside the state while it is live; the CSA values Louisville / St.Louis and the clearing of the
// state higher (a weight, never a must). Both read the live snapshot each review.
internal static class SecessionGarrisonTests
{
    private const int KentuckyState = 14;
    private const int KentuckyObjective = 15;

    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("SecessionIsLiveUnderVanillasIsDeactivatedRule", SecessionIsLiveUnderVanillasIsDeactivatedRule),
            ("UnionKeepsAGarrisonInsideALiveSecessionState", UnionKeepsAGarrisonInsideALiveSecessionState),
            ("UnionPostsTheNearestReadyFormationWhenNoneStandsInTheState", UnionPostsTheNearestReadyFormationWhenNoneStandsInTheState),
            ("TheGarrisonIsSizedByTheHoldNeedAtTheTown", TheGarrisonIsSizedByTheHoldNeedAtTheTown),
            ("AGarrisonIsNeverPartOfTheMass", AGarrisonIsNeverPartOfTheMass),
            ("TheInStateGarrisonStaysWithheldWhenAnotherHoldMeetsTheSectorsTarget", TheInStateGarrisonStaysWithheldWhenAnotherHoldMeetsTheSectorsTarget),
            ("NoGarrisonBelowTheSupportFloorOrOnceTheStateHasFlipped", NoGarrisonBelowTheSupportFloorOrOnceTheStateHasFlipped),
            ("TheGarrisonIsReadEachReviewNotCached", TheGarrisonIsReadEachReviewNotCached),
            ("OnlyTheStatesOwnerGarrisonsIt", OnlyTheStatesOwnerGarrisonsIt),
            ("CsaValuesLouisvilleAndTheClearingOfTheState", CsaValuesLouisvilleAndTheClearingOfTheState),
            ("CsaBonusIsGoneWhenDeactivatedOrFlippedAndNeverOnTheUnionSide", CsaBonusIsGoneWhenDeactivatedOrFlippedAndNeverOnTheUnionSide),
            ("CsaSecessionBonusIsAWeightNotAMust", CsaSecessionBonusIsAWeightNotAMust),
            ("AllocationRowNamesTheSecessionGarrison", AllocationRowNamesTheSecessionGarrison),
            ("TheGarrisonMarchesThroughTheMoverAsAGarrisonRole", TheGarrisonMarchesThroughTheMoverAsAGarrisonRole),
            ("TheSnapshotReadsTheSecessionStatesFromVanillaEachReview", TheSnapshotReadsTheSecessionStatesFromVanillaEachReview),
            ("AGarrisonAlreadyBlocksSecessionSoNoFieldFormationIsPosted", AGarrisonAlreadyBlocksSecessionSoNoFieldFormationIsPosted),
            ("TheGarrisonNeverPostsOrHoldsAPlayerOrCicFormation", TheGarrisonNeverPostsOrHoldsAPlayerOrCicFormation),
            ("ADistrictHeldOnlyByTheFlagStillGetsTheBelievedThreatsHold", ADistrictHeldOnlyByTheFlagStillGetsTheBelievedThreatsHold),
            ("ThePlayerPresenceRowIsNotCalledATownGarrison", ThePlayerPresenceRowIsNotCalledATownGarrison),
        });
    }

    // ---- fixtures ----

    // Eleven areas (x 0..1000, relocation range 150), the capital at x 0, IIPs every 100, the town (Louisville, objective 15) at x 300 with
    // `holder` as its holder, enemy objective 31 at x 800 for the Union (the CSA side's target), and the secession record.
    private static CampaignSnapshot Side(int alliance, int stateAlliance, float csaSupport)
    {
        var s = ExecutorFixtures.Line(alliance, 11);
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.65f;
        for (int i = 1; i <= 9; i++) s.Supply.Add(ExecutorFixtures.Iip(600 + i, 100f * i, alliance));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = KentuckyObjective, StateId = KentuckyState, X = 300f, Z = 0f, HolderAlliance = stateAlliance, Available = alliance == 1, Importance01 = 0.3f });
        s.Secessions.Add(new SecessionRecord
        {
            ObjectiveId = KentuckyObjective, StateId = KentuckyState, StateAlliance = stateAlliance,
            Live = SecessionRules.IsLive(stateAlliance, csaSupport), HasTown = true, TownX = 300f, TownZ = 0f
        });
        return s;
    }

    private static FormationRecord Corps(int id, float x, float strength, int state = -1)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.SightRadius = 50f;
        f.Morale = 0.9f;
        f.StateId = state;
        return f;
    }

    private static EnemyBelief Seen(float x, float strength)
    {
        return ExecutorFixtures.Belief(x, 0f, strength);
    }

    private static TheaterPlan Plan(CampaignSnapshot s, PlannerState state = null)
    {
        return TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, state ?? new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
    }

    private static Allocation Allocate(CampaignSnapshot s, TheaterPlan plan)
    {
        return ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
    }

    private static TheaterPlanner.Candidate CandidateOf(CampaignSnapshot s, int objectiveId)
    {
        var inputs = new TheaterPlanner.Inputs { S = s, Sectors = ExecutorFixtures.Sectors(s), Speed = ExecutorFixtures.Speed() };
        var list = new List<TheaterPlanner.Candidate>();
        TheaterPlanner.Evaluate(inputs, list);
        foreach (var c in list)
            if (c.ObjectiveId == objectiveId) return c;
        throw new Exception("no candidate " + objectiveId);
    }

    // ---- the rule ----

    private static void SecessionIsLiveUnderVanillasIsDeactivatedRule()
    {
        AssertTrue(SecessionRules.IsLive(0, 0.45f), "the Union's state at exactly 0.45 CSA support: live (the test is support < 0.45)");
        AssertTrue(SecessionRules.IsLive(0, 0.9f), "and above it");
        AssertFalse(SecessionRules.IsLive(0, 0.449f), "below 0.45: deactivated");
        AssertFalse(SecessionRules.IsLive(1, 0.9f), "already the CSA's: deactivated whatever the support");
        AssertTrue(SecessionRules.IsLive(2, 0.6f), "a neutral state is not the CSA's, so still live");
        AssertEqual("Kentucky", SecessionRules.NameOf(14), "state 14");
        AssertEqual("Missouri", SecessionRules.NameOf(22), "state 22");
    }

    // ---- the Union ----

    private static void UnionKeepsAGarrisonInsideALiveSecessionState()
    {
        var s = Side(0, 0, 0.6f);
        s.Own.Add(Corps(1, 280f, 6000f, KentuckyState));    // in Kentucky, 20 from Louisville
        s.Own.Add(Corps(2, 100f, 9000f));
        s.Own.Add(Corps(3, 150f, 9000f));
        var plan = Plan(s);
        AssertEqual(1, plan.Garrisons.Count, "one post: Louisville");
        var post = plan.Garrisons[0];
        AssertEqual("Kentucky", post.StateName, "named");
        AssertTrue(post.InState, "a formation already stands in the state");
        AssertEqual(1, post.InstanceIds.Count, "the one in the state, which meets the floor need");
        AssertEqual(1, post.InstanceIds[0], "formation 1");
        AssertNear(300f, post.X, 0.01f, "the post is the objective's town");

        var alloc = Allocate(s, plan);
        AssertTrue(alloc.TryGet(1, out var a), "assigned");
        AssertEqual(FormationRole.Hold, a.Role, "it holds where it stands");
        AssertEqual(ForceAllocator.ReasonSecession, a.Reason, "for the secession");
        AssertTrue(a.Withheld, "withheld from vanilla's pools: it must stay in the state");
        AssertNear(280f, a.TargetX, 0.01f, "in place");
    }

    private static void UnionPostsTheNearestReadyFormationWhenNoneStandsInTheState()
    {
        var s = Side(0, 0, 0.6f);
        s.Own.Add(Corps(1, 100f, 9000f));
        s.Own.Add(Corps(2, 240f, 8000f));    // nearest to Louisville, outside the state
        s.Own.Add(Corps(3, 500f, 9000f));
        var plan = Plan(s);
        AssertEqual(1, plan.Garrisons.Count, "a post");
        AssertFalse(plan.Garrisons[0].InState, "nobody in the state yet");
        AssertEqual(1, plan.Garrisons[0].InstanceIds.Count, "one formation is posted");
        AssertEqual(2, plan.Garrisons[0].InstanceIds[0], "the nearest ready one");
        var alloc = Allocate(s, plan);
        alloc.TryGet(2, out var a);
        AssertEqual(FormationRole.Garrison, a.Role, "it marches to the town");
        AssertNear(300f, a.TargetX, 0.01f, "the target is Louisville");
        AssertEqual(ForceAllocator.ReasonSecession, a.Reason, "named");
        AssertTrue(a.Withheld, "and withheld");

        // An unready or busy formation is never the one posted.
        var low = Side(0, 0, 0.6f);
        var unready = Corps(2, 240f, 8000f);
        unready.ReadinessStep = 1;
        low.Own.Add(Corps(1, 100f, 9000f));
        low.Own.Add(unready);
        AssertEqual(1, Plan(low).Garrisons[0].InstanceIds[0], "an unready formation is skipped for the next nearest");
    }

    private static void TheGarrisonIsSizedByTheHoldNeedAtTheTown()
    {
        // Nothing believed near the town: the floor (5000); the first formation in the state meets it.
        var quiet = Side(0, 0, 0.6f);
        quiet.Own.Add(Corps(1, 290f, 6000f, KentuckyState));
        quiet.Own.Add(Corps(2, 310f, 7000f, KentuckyState));
        quiet.Own.Add(Corps(3, 330f, 8000f, KentuckyState));
        var small = Plan(quiet);
        AssertNear(ForceAllocator.MinGarrisonStrength, small.Garrisons[0].Need, 0.5f, "no enemy believed: the floor");
        AssertEqual(1, small.Garrisons[0].InstanceIds.Count, "one formation is a small garrison");

        // A fresh sighting of 30000 at the town: HoldRatio x 30000 = 18000, nearest first until it is met.
        var threatened = Side(0, 0, 0.6f);
        threatened.Own.Add(Corps(1, 290f, 6000f, KentuckyState));
        threatened.Own.Add(Corps(2, 310f, 7000f, KentuckyState));
        threatened.Own.Add(Corps(3, 330f, 8000f, KentuckyState));
        threatened.Own.Add(Corps(4, 350f, 9000f, KentuckyState));
        threatened.Beliefs.Add(Seen(320f, 30000f));
        var plan = Plan(threatened);
        AssertNear(18000f, plan.Garrisons[0].Need, 1f, "HoldRatio x the believed enemy");
        AssertEqual(3, plan.Garrisons[0].InstanceIds.Count, "6000 + 7000 + 8000 meets 18000; the fourth stays free");
        AssertFalse(plan.Garrisons[0].InstanceIds.Contains(4), "the farthest is not needed");
        AssertEqual(1, plan.Garrisons[0].InstanceIds[0], "nearest first");
    }

    // The enemy target stands at x 330, so the formations at Louisville (x 300) are the nearest to the push: without the exclusion the
    // garrison would be the first formation massed.
    private static void AGarrisonIsNeverPartOfTheMass()
    {
        var s = Side(0, 0, 0.6f);
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 31, X = 330f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.8f });
        s.Beliefs.Add(Seen(330f, 3000f));
        s.Own.Add(Corps(1, 305f, 9000f, KentuckyState));
        for (int i = 1; i < 6; i++) s.Own.Add(Corps(i + 1, 40f * i, 9000f));
        var plan = Plan(s);
        AssertEqual(31, plan.MainObjectiveId, "a main effort exists");
        AssertTrue(plan.MassInstanceIds.Count > 0, "with a mass");
        AssertFalse(plan.MassInstanceIds.Contains(1), "the formation holding Kentucky is not massed, though it is the nearest to the target");

        var outside = Side(0, 0, 0.6f);
        outside.Objectives.Add(new ObjectiveRecord { ObjectiveId = 31, X = 330f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.8f });
        outside.Beliefs.Add(Seen(330f, 3000f));
        outside.Own.Add(Corps(1, 250f, 9000f));
        for (int i = 1; i < 6; i++) outside.Own.Add(Corps(i + 1, 40f * i, 9000f));
        var outsidePlan = Plan(outside);
        AssertEqual(1, outsidePlan.Garrisons[0].InstanceIds.Count, "one is posted");
        int posted = outsidePlan.Garrisons[0].InstanceIds[0];
        AssertEqual(31, outsidePlan.MainObjectiveId, "the push still goes");
        AssertFalse(outsidePlan.MassInstanceIds.Contains(posted), "and the posted formation (" + posted + ") is not in the mass either");
    }

    // The sector's own withhold keeps the formations nearest its threat until its target is met; the garrison is withheld on its own account,
    // so a hold nearer the sector's centre meeting the 5000 target first does not free it to be taken for an offensive or a defence.
    private static void TheInStateGarrisonStaysWithheldWhenAnotherHoldMeetsTheSectorsTarget()
    {
        var s = Side(0, 0, 0.6f);
        s.Own.Add(Corps(1, 250f, 9000f, KentuckyState));    // the garrison: in Kentucky
        s.Own.Add(Corps(2, 300f, 9000f));                    // another hold in the same sector, outside the state
        s.Own.Add(Corps(3, 350f, 9000f));
        var plan = Plan(s);
        AssertEqual(1, plan.Garrisons[0].InstanceIds[0], "the one inside Kentucky is the garrison");
        var alloc = Allocate(s, plan);
        int withheld = 0;
        foreach (var a in alloc.Assignments)
            if (a.Role == FormationRole.Hold && a.Withheld) withheld++;
        alloc.TryGet(1, out var garrison);
        AssertTrue(garrison.Withheld, "the garrison is withheld");
        AssertTrue(withheld >= 1, "withheld holds: " + withheld);
    }

    private static void NoGarrisonBelowTheSupportFloorOrOnceTheStateHasFlipped()
    {
        foreach (var (stateAlliance, support, label) in new[] { (0, 0.44f, "support below 0.45"), (1, 0.9f, "already flipped to the CSA"), (1, 0.3f, "flipped and low") })
        {
            var s = Side(0, stateAlliance, support);
            s.Own.Add(Corps(1, 280f, 9000f, KentuckyState));
            s.Own.Add(Corps(2, 100f, 9000f));
            var plan = Plan(s);
            AssertEqual(0, plan.Garrisons.Count, "no post: " + label);
            var alloc = Allocate(s, plan);
            alloc.TryGet(1, out var a);
            AssertTrue(a.Reason != ForceAllocator.ReasonSecession, "and no formation is kept for it: " + label);
        }
        var live = Side(0, 0, 0.45f);
        live.Own.Add(Corps(1, 280f, 9000f, KentuckyState));
        AssertEqual(1, Plan(live).Garrisons.Count, "exactly 0.45 is still live");
    }

    // The record is rebuilt from vanilla each review: the next review sees today's support and owner, never yesterday's.
    private static void TheGarrisonIsReadEachReviewNotCached()
    {
        var state = new PlannerState();
        var s = Side(0, 0, 0.6f);
        s.Own.Add(Corps(1, 280f, 9000f, KentuckyState));
        AssertEqual(1, Plan(s, state).Garrisons.Count, "review 1: live");

        var fallen = Side(0, 0, 0.3f);
        fallen.Own.Add(Corps(1, 280f, 9000f, KentuckyState));
        AssertEqual(0, Plan(fallen, state).Garrisons.Count, "review 2: support fell, the garrison is released the same day");

        var again = Side(0, 0, 0.7f);
        again.Own.Add(Corps(1, 280f, 9000f, KentuckyState));
        AssertEqual(1, Plan(again, state).Garrisons.Count, "review 3: support recovered, the garrison is back");

        var flipped = Side(0, 1, 0.7f);
        flipped.Own.Add(Corps(1, 280f, 9000f, KentuckyState));
        AssertEqual(0, Plan(flipped, state).Garrisons.Count, "review 4: the state flipped");
    }

    private static void OnlyTheStatesOwnerGarrisonsIt()
    {
        // A CSA snapshot never garrisons a state the Union owns, however live the record.
        var s = Side(1, 0, 0.8f);
        s.Own.Add(Corps(1, 280f, 9000f, KentuckyState));
        AssertEqual(0, Plan(s).Garrisons.Count, "the CSA does not garrison a Union state");
    }

    // ---- the CSA ----

    private static CampaignSnapshot CsaCandidates(int stateAlliance, float support, int ownerOfOtherTargetState = KentuckyState)
    {
        var s = Side(1, stateAlliance, support);
        s.Objectives.Clear();
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = KentuckyObjective, StateId = KentuckyState, X = 800f, Z = 0f, HolderAlliance = 0, Available = true, Importance01 = 0.3f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 33, StateId = ownerOfOtherTargetState, X = 800f, Z = 0f, HolderAlliance = 0, Available = true, Importance01 = 0.3f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 28, StateId = 3, X = 800f, Z = 0f, HolderAlliance = 0, Available = true, Importance01 = 0.3f });
        var rec = s.Secessions[0];
        rec.TownX = 800f;
        s.Secessions[0] = rec;
        return s;
    }

    private static void CsaValuesLouisvilleAndTheClearingOfTheState()
    {
        var s = CsaCandidates(0, 0.6f);
        float plain = CandidateOf(s, 28).Value;
        AssertNear(TheaterPlanner.SecessionValueBonus, CandidateOf(s, KentuckyObjective).Value - plain, 0.001f, "Louisville itself");
        AssertNear(TheaterPlanner.SecessionStateBonus, CandidateOf(s, 33).Value - plain, 0.001f, "another target in the same live state: clearing the Union out of it");
        AssertTrue(TheaterPlanner.SecessionValueBonus > TheaterPlanner.SecessionStateBonus, "the town counts for more than its neighbours");
    }

    private static void CsaBonusIsGoneWhenDeactivatedOrFlippedAndNeverOnTheUnionSide()
    {
        foreach (var (stateAlliance, support, label) in new[] { (0, 0.44f, "support below 0.45"), (1, 0.9f, "already the CSA's") })
        {
            var s = CsaCandidates(stateAlliance, support);
            float plain = CandidateOf(s, 28).Value;
            AssertNear(0f, CandidateOf(s, KentuckyObjective).Value - plain, 0.001f, "no bonus on the town: " + label);
            AssertNear(0f, CandidateOf(s, 33).Value - plain, 0.001f, "no bonus on the state's other targets: " + label);
        }
        // The Union side values nothing extra for its own state's objective: it garrisons it.
        var union = CsaCandidates(0, 0.6f);
        var asUnion = ExecutorFixtures.Line(0, 11);
        asUnion.MinStrengthForMovement = 5000f;
        asUnion.Objectives.Add(new ObjectiveRecord { ObjectiveId = KentuckyObjective, StateId = KentuckyState, X = 800f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.3f });
        asUnion.Objectives.Add(new ObjectiveRecord { ObjectiveId = 28, StateId = 3, X = 800f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.3f });
        asUnion.Secessions.Add(union.Secessions[0]);
        AssertNear(0f, CandidateOf(asUnion, KentuckyObjective).Value - CandidateOf(asUnion, 28).Value, 0.001f, "the Union's candidate is unchanged");
    }

    // The bonus is a weight (0.25 of a 0.30-weight term): a Louisville the CSA cannot take, against a believed army several times its
    // strength, does not become the main effort while an ordinary target is within reach.
    private static void CsaSecessionBonusIsAWeightNotAMust()
    {
        var s = CsaCandidates(0, 0.9f);
        for (int i = 0; i < 4; i++) s.Own.Add(Corps(i + 1, 600f + 40f * i, 9000f));
        s.Beliefs.Add(Seen(800f, 200000f));                 // an army at Louisville, which is also where 33 and 28 stand
        var ordinary = s.Objectives.FindIndex(o => o.ObjectiveId == 28);
        var other = s.Objectives[ordinary];
        other.X = 420f;                                     // a target well away from that army, near our corps
        s.Objectives[ordinary] = other;
        var plan = Plan(s);
        AssertTrue(plan.MainObjectiveId != KentuckyObjective, "Louisville against 200000 is not attacked with 36000: main=" + plan.MainObjectiveId);
        var louisville = CandidateOf(s, KentuckyObjective);
        AssertTrue(louisville.Need > 100000f, "and it still needs the force the enemy there demands: " + louisville.Need);
    }

    // ---- telemetry and the mover ----

    private static void AllocationRowNamesTheSecessionGarrison()
    {
        var s = Side(0, 0, 0.6f);
        s.Own.Add(Corps(1, 280f, 9000f, KentuckyState));
        s.Own.Add(Corps(2, 100f, 9000f));
        var plan = Plan(s);
        string row = ForceAllocator.Summary(Allocate(s, plan), plan, PlayerCommandClass.BelowMapLevel, 1f, 0);
        AssertContains(row, " garrison=corps1@Kentucky:in/5000 ", "the unit and its state, standing in it");

        var moving = Side(0, 0, 0.6f);
        moving.Own.Add(Corps(2, 100f, 9000f));
        var movingPlan = Plan(moving);
        AssertContains(ForceAllocator.Summary(Allocate(moving, movingPlan), movingPlan, PlayerCommandClass.BelowMapLevel, 1f, 0), " garrison=corps2@Kentucky:moving/5000 ", "or on its way");

        var none = Side(0, 0, 0.3f);
        none.Own.Add(Corps(2, 100f, 9000f));
        var nonePlan = Plan(none);
        AssertContains(ForceAllocator.Summary(Allocate(none, nonePlan), nonePlan, PlayerCommandClass.BelowMapLevel, 1f, 0), " garrison=none ", "none while the objective is not live");
    }

    private static void TheGarrisonMarchesThroughTheMoverAsAGarrisonRole()
    {
        AssertTrue(CampaignMover.MovesRole(FormationRole.Garrison), "the mover sends a Garrison formation (one owner per formation)");
    }

    // The runtime is not compiled into the harness: pin the live reads (vanilla's owner and CSA support, never a cached or constant value).
    private static void TheSnapshotReadsTheSecessionStatesFromVanillaEachReview()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSnapshotRuntime.cs");
        AssertTrue(HasLine(src, "float support = nation.support != null && nation.support.Length > SecessionRules.CsaAlliance ? nation.support[SecessionRules.CsaAlliance] : 0f;"),
            "the CSA's support in the state (nation.support[1], 178966-178973)");
        AssertTrue(HasLine(src, "Live = SecessionRules.IsLive(nation.alliance, support),"), "live is vanilla's IsDeactivated rule over today's owner and support");
        AssertTrue(src.Contains("GarrisonStrength = s.Alliance == 0 && GarrisonStrengthByState.TryGetValue(rule.StateId, out float garrison) ? garrison : 0f,"),
            "the town garrison's own strength is read for the Union only, from the side's own garrison units");
        AssertTrue(HasLine(src, "ReadSecessions(s);"), "read on every snapshot build, after the objectives it takes the town from");
        AssertTrue(HasLine(src, "OwnTroops = s.Alliance == 0 && nation.alliancetroops != null && nation.alliancetroops.Length > 0 && nation.alliancetroops[0]"),
            "the Union reads vanilla's presence flag (garrisons included); the CSA side never does");
        string body = BodyOf(src, "private static void ReadSecessions(");
        AssertFalse(body.Contains("static readonly") || body.Contains("Cache"), "nothing is kept between reviews");
    }

    // F2: vanilla's alliancetroops[0] is set by any visible Union group in the state, a garrison included (81654-81660, 82940-82943).
    // With the flag set and no field formation inside, the secession is already blocked: nobody is posted, nothing marches.
    private static void AGarrisonAlreadyBlocksSecessionSoNoFieldFormationIsPosted()
    {
        var s = Side(0, 0, 0.6f);
        var rec = s.Secessions[0];
        rec.OwnTroops = true;
        s.Secessions[0] = rec;
        s.Own.Add(Corps(1, 240f, 9000f));      // the nearest field formation: formerly posted for nothing
        s.Own.Add(Corps(2, 100f, 9000f));
        var plan = Plan(s);
        AssertEqual(1, plan.Garrisons.Count, "the post stands");
        AssertTrue(plan.Garrisons[0].InState, "the Union is present in the state (a town garrison)");
        AssertEqual(0, plan.Garrisons[0].InstanceIds.Count, "so no field formation is posted");
        var alloc = Allocate(s, plan);
        foreach (var a in alloc.Assignments)
        {
            AssertTrue(a.Role != FormationRole.Garrison, "no formation marches to Louisville: " + a.InstanceId);
            AssertTrue(a.Reason != ForceAllocator.ReasonSecession, "and none is kept for it: " + a.InstanceId);
        }
        AssertContains(ForceAllocator.Summary(alloc, plan, PlayerCommandClass.BelowMapLevel, 1f, 0), " garrison=town-garrison@Kentucky:in/0 ", "the row says a garrison already blocks it and nothing is needed");

        // Without the flag (the garrison left, or the half-day alliance-data pass has not run), the same position posts the nearest formation.
        var flagless = Side(0, 0, 0.6f);
        flagless.Own.Add(Corps(1, 240f, 9000f));
        flagless.Own.Add(Corps(2, 100f, 9000f));
        AssertEqual(1, Plan(flagless).Garrisons[0].InstanceIds[0], "no flag: the nearest ready formation is posted");

        // A field formation inside the state is still pledged when the flag is set (the flag lags the groups by one pass).
        var both = Side(0, 0, 0.6f);
        var bothRec = both.Secessions[0];
        bothRec.OwnTroops = true;
        both.Secessions[0] = bothRec;
        both.Own.Add(Corps(1, 280f, 9000f, KentuckyState));
        AssertEqual(1, Plan(both).Garrisons[0].InstanceIds.Count, "the one inside is still held in place");
    }

    // W&L: the garrison never posts, holds or reserves a formation the planner may not move. A player's formation inside the state
    // still counts as the Union's presence (vanilla's flag counts every Union group) but is no member of the garrison.
    private static void TheGarrisonNeverPostsOrHoldsAPlayerOrCicFormation()
    {
        foreach (var chain in new[] { WlChainClass.PlayerCommand, WlChainClass.PlayerMapGroup, WlChainClass.CicSide })
        {
            // Outside the state, nearest to the town: the AI formation farther away is posted instead.
            var outside = Side(0, 0, 0.6f);
            var player = Corps(1, 290f, 9000f);
            player.Chain = chain;
            outside.Own.Add(player);
            outside.Own.Add(Corps(2, 200f, 9000f));
            var plan = Plan(outside);
            AssertEqual(1, plan.Garrisons.Count, chain + ": a post");
            AssertFalse(plan.Garrisons[0].InstanceIds.Contains(1), chain + ": never posted");
            var alloc = Allocate(outside, plan);
            if (alloc.TryGet(1, out var a))
                AssertTrue(a.Role != FormationRole.Garrison && a.Reason != ForceAllocator.ReasonSecession, chain + ": no garrison role or reason");

            // Inside the state: present, but not a member, so no one is ordered to hold or march for it.
            var inside = Side(0, 0, 0.6f);
            var held = Corps(1, 290f, 9000f, KentuckyState);
            held.Chain = chain;
            inside.Own.Add(held);
            inside.Own.Add(Corps(2, 200f, 9000f));
            var insidePlan = Plan(inside);
            AssertTrue(insidePlan.Garrisons[0].InState, chain + ": the player's group in the state is the Union's presence");
            AssertFalse(insidePlan.Garrisons[0].InstanceIds.Contains(1), chain + ": not a garrison member");
            AssertFalse(insidePlan.Garrisons[0].InstanceIds.Contains(2), chain + ": and the AI formation is not posted for nothing");
            var insideAlloc = Allocate(inside, insidePlan);
            foreach (var x in insideAlloc.Assignments)
                AssertTrue(x.Reason != ForceAllocator.ReasonSecession, chain + ": no formation is kept for the secession (" + x.InstanceId + ")");
        }
    }

    // Landing round, ruling 7: the flag says the state is held, not that it is safe. With an enemy believed near the town the need is the
    // believed threat's hold less the garrison's own strength; with none near, nothing is posted.
    private static void ADistrictHeldOnlyByTheFlagStillGetsTheBelievedThreatsHold()
    {
        CampaignSnapshot Flagged(float garrisonStrength)
        {
            var f = Side(0, 0, 0.6f);
            var rec = f.Secessions[0];
            rec.OwnTroops = true;
            rec.GarrisonStrength = garrisonStrength;
            f.Secessions[0] = rec;
            f.Own.Add(Corps(1, 240f, 9000f));      // the believed army's own sector holds these two back (the hold reserve)
            f.Own.Add(Corps(2, 220f, 9000f));
            f.Own.Add(Corps(3, 190f, 9000f));      // the nearest formations free to march
            f.Own.Add(Corps(4, 170f, 9000f));
            f.Own.Add(Corps(5, 150f, 9000f));
            return f;
        }

        // An army of 30000 believed at the town: HoldRatio 0.6 x 30000 = 18000, less the garrison's 4000 = 14000: two corps march.
        var threatened = Flagged(4000f);
        threatened.Beliefs.Add(Seen(320f, 30000f));
        var plan = Plan(threatened);
        AssertTrue(plan.Garrisons[0].InState, "the Union is present by the flag");
        AssertNear(14000f, plan.Garrisons[0].Need, 1f, "the hold less the garrison's own strength");
        AssertEqual(2, plan.Garrisons[0].InstanceIds.Count, "9000 + 9000 meets 14000: " + string.Join(",", plan.Garrisons[0].InstanceIds));
        AssertEqual(3, plan.Garrisons[0].InstanceIds[0], "nearest free formation first");
        AssertEqual(4, plan.Garrisons[0].InstanceIds[1], "then the next");
        AssertTrue(plan.Garrisons[0].Marching, "they march to the town");
        var alloc = Allocate(threatened, plan);
        alloc.TryGet(3, out var first);
        AssertEqual(FormationRole.Garrison, first.Role, "through the garrison role (MayMove, the mover)");
        AssertNear(300f, first.TargetX, 0.01f, "to Louisville");
        alloc.TryGet(5, out var spare);
        AssertTrue(spare.Reason != ForceAllocator.ReasonSecession, "the third corps is not needed");
        AssertContains(ForceAllocator.Summary(alloc, plan, PlayerCommandClass.BelowMapLevel, 1f, 0), " garrison=corps3@Kentucky:in+moving/14000+1 ", "the row says so");

        // A garrison strong enough for the threat: nothing is posted.
        var strong = Flagged(18000f);
        strong.Beliefs.Add(Seen(320f, 30000f));
        AssertEqual(0, Plan(strong).Garrisons[0].InstanceIds.Count, "a garrison that meets the hold needs nobody");

        // No enemy believed near: the flag is enough, nothing is posted (no floor, no presumption).
        var quiet = Flagged(4000f);
        var quietPlan = Plan(quiet);
        AssertNear(0f, quietPlan.Garrisons[0].Need, 0.01f, "no threat, no need");
        AssertEqual(0, quietPlan.Garrisons[0].InstanceIds.Count, "nothing posted");

        // An enemy far from the town (outside the relocation range) is not near: fog-honest, beliefs only.
        var far = Flagged(4000f);
        far.Beliefs.Add(Seen(900f, 30000f));
        AssertEqual(0, Plan(far).Garrisons[0].InstanceIds.Count, "a belief far away posts nothing");

        // A player's group is never one of the posted.
        var player = Flagged(4000f);
        player.Beliefs.Add(Seen(320f, 30000f));
        var near = player.Own[2];                         // corps 3, the first that would march
        near.Chain = WlChainClass.PlayerMapGroup;
        player.Own[2] = near;
        var playerPlan = Plan(player);
        AssertFalse(playerPlan.Garrisons[0].InstanceIds.Contains(3), "the map-group player is not posted");
        AssertEqual(2, playerPlan.Garrisons[0].InstanceIds.Count, "the next two free formations are");
    }

    // Presence by a player's group alone is not a town garrison.
    private static void ThePlayerPresenceRowIsNotCalledATownGarrison()
    {
        var s = Side(0, 0, 0.6f);
        var held = Corps(1, 290f, 9000f, KentuckyState);
        held.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(held);
        var plan = Plan(s);
        AssertTrue(plan.Garrisons[0].PlayerPresence, "only a player's group stands in the state");
        string row = ForceAllocator.Summary(Allocate(s, plan), plan, PlayerCommandClass.BelowMapLevel, 1f, 0);
        AssertContains(row, " garrison=player-presence@Kentucky:in/", "named for what it is");
        AssertFalse(row.Contains("town-garrison"), "and not a town garrison");
    }
}
