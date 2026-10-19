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
            ("AMassThatMarchesIntoTheStateIsNotPinnedThere", AMassThatMarchesIntoTheStateIsNotPinnedThere),
            ("AFreeFormationInsideIsPledgedAndTheMassMemberIsNot", AFreeFormationInsideIsPledgedAndTheMassMemberIsNot),
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
            ("ANobodyPresentStatePostsOneFormationNotSeveral", ANobodyPresentStatePostsOneFormationNotSeveral),
            ("TheGarrisonNeverTakesTheMassAndRecordsWhatItCouldNotMeet", TheGarrisonNeverTakesTheMassAndRecordsWhatItCouldNotMeet),
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
        s.Own.Add(Corps(GuardId, 0f, 9000f));   // the capital guard: D11's floor is never under the minimum strength, so the capital always keeps one
        return s;
    }

    private const int GuardId = 900;

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
        return TestClaims.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null);
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
        AssertNear(TheaterPlanner.MinStrengthFallback, small.Garrisons[0].Need, 0.5f, "no enemy believed: the floor");
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

    // The enemy target stands at x 330. A formation inside the state that is the best mass member is the mass (and counts as presence); it
    // is not pledged. A garrison posted from outside is never part of the mass either.
    private static void AGarrisonIsNeverPartOfTheMass()
    {
        var s = Side(0, 0, 0.6f);
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 31, X = 330f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.8f });
        s.Beliefs.Add(Seen(330f, 3000f));
        s.Own.Add(Corps(1, 305f, 9000f, KentuckyState));
        for (int i = 1; i < 6; i++) s.Own.Add(Corps(i + 1, 40f * i, 9000f));
        var plan = Plan(s);
        AssertEqual(31, plan.MainObjectiveId, "a main effort exists");
        AssertTrue(plan.MassInstanceIds.Contains(1), "the formation standing in Kentucky is the nearest to the target, so it is the mass");
        AssertTrue(plan.Garrisons[0].InState, "and its presence counts");
        AssertFalse(plan.Garrisons[0].InstanceIds.Contains(1), "but it is not pledged");

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

    // Landing r2: the main effort's mass is never stopped by a secession garrison. The verifier's two-day probe: on day 1 corps 3 is the
    // mass and stands outside Kentucky; on day 2 its march has taken it into Kentucky. It stays the mass, the main effort stays, and it is
    // not pledged or pinned there.
    private static void AMassThatMarchesIntoTheStateIsNotPinnedThere()
    {
        // Corps 3 is the mass; corps 1 is the free corps; `corps1State` puts corps 1 inside Kentucky (day 3), `corps3State` puts corps 3 there.
        CampaignSnapshot Day(int corps3State, int corps1State = -1)
        {
            var s = Side(0, 0, 0.6f);
            s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 31, X = 600f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.8f });
            s.Beliefs.Add(Seen(600f, 3000f));
            s.Own.Add(Corps(1, corps1State >= 0 ? 290f : 100f, 9000f, corps1State));
            s.Own.Add(Corps(3, corps3State >= 0 ? 280f : 200f, 9000f, corps3State));
            return s;
        }
        var state = new PlannerState();
        var day1 = Plan(Day(-1), state);
        AssertEqual(31, day1.MainObjectiveId, "day 1: a main effort");
        AssertTrue(day1.MassInstanceIds.Contains(3), "day 1: corps 3 is the mass: " + string.Join(",", day1.MassInstanceIds));
        AssertEqual(1, day1.Garrisons[0].InstanceIds.Count, "day 1: nobody inside, so one formation is posted");

        var s2 = Day(KentuckyState);
        var day2 = Plan(s2, state);
        AssertEqual(31, day2.MainObjectiveId, "day 2: the main effort stays");
        AssertTrue(day2.MassInstanceIds.Contains(3), "day 2: corps 3 is still the mass");
        var post = day2.Garrisons[0];
        AssertTrue(post.InState, "its presence in Kentucky counts for the secession");
        AssertFalse(post.InstanceIds.Contains(3), "but it is not pledged");
        AssertTrue(post.MassPresence, "the row names it");
        // The mass will march on: one free corps is posted now, through the ordinary garrison path, never from the mass.
        AssertEqual(1, post.InstanceIds.Count, "one replacement is posted while the mass is still inside: " + string.Join(",", post.InstanceIds));
        AssertEqual(1, post.InstanceIds[0], "the free corps");
        AssertTrue(post.Marching, "marching in");
        var alloc = Allocate(s2, day2);
        alloc.TryGet(3, out var a);
        AssertTrue(a.Role == FormationRole.MainEffort || a.Role == FormationRole.Support, "corps 3 keeps marching with the push (" + a.Role + "; from Kentucky's West it is the one reinforcement)");
        AssertTrue(a.Reason != ForceAllocator.ReasonSecession && !a.Withheld, "not withheld for the secession");
        alloc.TryGet(1, out var replacement);
        AssertEqual(FormationRole.Garrison, replacement.Role, "the replacement marches to the town");
        AssertNear(300f, replacement.TargetX, 0.01f, "to Louisville");
        AssertContains(ForceAllocator.Summary(alloc, day2, PlayerCommandClass.BelowMapLevel, 1f, 0), " garrison=corps1@Kentucky:in+moving/", "the allocation row");

        // Day 3: the mass has moved on and the posted corps stands in Kentucky: it holds, and nothing extra is posted.
        var day3 = Plan(Day(-1, KentuckyState), state);
        var held = day3.Garrisons[0];
        AssertTrue(held.InState, "day 3: the posted corps is inside");
        AssertEqual(1, held.InstanceIds.Count, "exactly one formation: " + string.Join(",", held.InstanceIds));
        AssertEqual(1, held.InstanceIds[0], "the corps that was posted");
        AssertFalse(held.Marching, "nothing is marching in");
        AssertFalse(held.MassPresence, "and the mass is no longer the presence");
        var alloc3 = Allocate(Day(-1, KentuckyState), day3);
        alloc3.TryGet(1, out var holding);
        AssertEqual(FormationRole.Hold, holding.Role, "the posted corps holds");
        AssertEqual(ForceAllocator.ReasonSecession, holding.Reason, "for the secession");

        // The replacement is capped at one, whatever the need: valuable enemy ground by the town makes the need 7500 (D11's floor, 5000 x (1 + 0.5)),
        // which a 6000-man corps does not meet, and three such free corps are available; still only one is posted.
        var presumed = Day(KentuckyState);
        presumed.Objectives.Add(new ObjectiveRecord { ObjectiveId = 50, X = 380f, Z = 0f, HolderAlliance = 1, Available = false, Importance01 = 0.5f });
        for (int i = 0; i < 3; i++) presumed.Own.Add(Corps(5 + i, 130f + 20f * i, 6000f));
        var presumedPlan = Plan(presumed, new PlannerState());
        var presumedPost = presumedPlan.Garrisons[0];
        AssertTrue(presumedPost.MassPresence, "the mass is the only presence: mass=" + string.Join(",", presumedPlan.MassInstanceIds) + " pledged=" + string.Join(",", presumedPost.InstanceIds));
        AssertTrue(presumedPost.Need > 6000f, "the need (" + presumedPost.Need + ") is more than one of those corps");
        AssertEqual(1, presumedPost.InstanceIds.Count, "yet the replacement is one: " + string.Join(",", presumedPost.InstanceIds));

        // A garrison already blocking (the flag) needs no replacement when the mass is the only field presence.
        var flagged = Day(KentuckyState);
        var rec = flagged.Secessions[0];
        rec.OwnTroops = true;
        flagged.Secessions[0] = rec;
        AssertFalse(Plan(flagged, new PlannerState()).Garrisons[0].Marching, "the flag says a garrison stands: no replacement");
    }

    // A mass member and a free formation both inside: the free one is pledged, the mass member is not.
    private static void AFreeFormationInsideIsPledgedAndTheMassMemberIsNot()
    {
        var s = Side(0, 0, 0.6f);
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 31, X = 600f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.8f });
        s.Beliefs.Add(Seen(600f, 3000f));
        s.Own.Add(Corps(3, 330f, 9000f, KentuckyState));    // nearest the target: the mass
        s.Own.Add(Corps(4, 290f, 9000f, KentuckyState));    // free, inside
        var plan = Plan(s);
        AssertEqual(31, plan.MainObjectiveId, "a main effort");
        AssertTrue(plan.MassInstanceIds.Contains(3) && !plan.MassInstanceIds.Contains(4), "corps 3 is the mass, corps 4 is not: " + string.Join(",", plan.MassInstanceIds));
        var post = plan.Garrisons[0];
        AssertEqual(1, post.InstanceIds.Count, "one pledged");
        AssertEqual(4, post.InstanceIds[0], "the free formation");
        var alloc = Allocate(s, plan);
        alloc.TryGet(4, out var free);
        AssertEqual(FormationRole.Hold, free.Role, "it holds where it stands");
        AssertEqual(ForceAllocator.ReasonSecession, free.Reason, "for the secession");
        AssertTrue(free.Withheld, "withheld");
        alloc.TryGet(3, out var mass);
        AssertTrue(mass.Role == FormationRole.MainEffort || mass.Role == FormationRole.Support, "the mass member marches with the push (" + mass.Role + ")");
        AssertTrue(mass.Reason != ForceAllocator.ReasonSecession, "and is not pinned");
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

    // A flag-alone district: the Union is present by the flag, the town garrison's own strength is `garrison`, a 30000 army is believed at
    // the town (HoldRatio 0.6 x 30000 = 18000). West corps stand at x 240 and below (the town's sector), East corps at x 700 and above.
    private static CampaignSnapshot FlagAlone(float garrison, float enemy, params FormationRecord[] corps)
    {
        var f = Side(0, 0, 0.6f);
        var rec = f.Secessions[0];
        rec.OwnTroops = true;
        rec.GarrisonStrength = garrison;
        f.Secessions[0] = rec;
        // An own-held objective in the East gives the sector model its East sector, so East corps are not the West sector's hold reserve.
        f.Objectives.Add(new ObjectiveRecord { ObjectiveId = 40, StateId = 5, X = 900f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 0.1f });
        if (enemy > 0f) f.Beliefs.Add(Seen(320f, enemy));
        foreach (var c in corps) f.Own.Add(c);
        return f;
    }

    // Landing check, F1: with nobody present (no flag, nobody inside) one formation is posted, whatever the believed threat.
    private static void ANobodyPresentStatePostsOneFormationNotSeveral()
    {
        var s = Side(0, 0, 0.6f);
        s.Beliefs.Add(Seen(320f, 30000f));                 // an army of 30000 at the town
        s.Own.Add(Corps(1, 240f, 9000f));
        s.Own.Add(Corps(2, 220f, 9000f));
        s.Own.Add(Corps(3, 700f, 9000f));
        s.Own.Add(Corps(4, 720f, 9000f));
        s.Own.Add(Corps(5, 740f, 9000f));
        var plan = Plan(s);
        AssertFalse(plan.Garrisons[0].InState, "nobody is present");
        AssertEqual(1, plan.Garrisons[0].InstanceIds.Count, "one formation is posted, not enough of them to meet the whole need: " + string.Join(",", plan.Garrisons[0].InstanceIds));
        AssertTrue(plan.Garrisons[0].Need > 9000f, "though the need (" + plan.Garrisons[0].Need + ") is more than one corps");
    }

    // Landing round, ruling 7, with the landing check's rulings: the flag says held, not safe. The need is the believed threat's hold less
    // the hold already kept against that army and the garrison's own strength.
    private static void ADistrictHeldOnlyByTheFlagStillGetsTheBelievedThreatsHold()
    {
        // Foresight F6a (integrator decision on phase 1 gap 18): what holds the town's sector is credited once, the capital guard standing in it
        // included (it is the table's Capital claim counted in that sector), so the world is sized for it. The town's sector holds the army with
        // one West corps (9000) and the capital guard (9000) of 27000; the garrison is 4000: 5000 is still needed; the nearest free corps goes.
        var threatened = FlagAlone(4000f, 45000f, Corps(1, 240f, 9000f), Corps(2, 700f, 9000f), Corps(3, 720f, 9000f), Corps(4, 740f, 9000f));
        var plan = Plan(threatened);
        var post = plan.Garrisons[0];
        AssertTrue(post.InState, "the Union is present by the flag");
        AssertNear(5000f, post.Need, 1f, "27000 - 18000 held against the same army (the corps and the capital guard) - 4000 garrison: " + post.Need);
        AssertEqual(1, post.InstanceIds.Count, "one corps of 9000 meets 5000: " + string.Join(",", post.InstanceIds));
        AssertEqual(2, post.InstanceIds[0], "the nearest free formation");
        AssertTrue(post.Marching, "it marches to the town");
        var alloc = Allocate(threatened, plan);
        alloc.TryGet(2, out var first);
        AssertEqual(FormationRole.Garrison, first.Role, "through the garrison role (MayMove, the mover)");
        AssertNear(300f, first.TargetX, 0.01f, "to Louisville");
        AssertContains(ForceAllocator.Summary(alloc, plan, PlayerCommandClass.BelowMapLevel, 1f, 0), " garrison=corps2@Kentucky:in+moving/5000 ", "the row says so");

        // A need that takes two: an army of 70000 (42000), one West corps (9000) and the capital guard (14000) holding the town's sector, the
        // garrison's 4000: 15000 -> two corps of 9000.
        var big = FlagAlone(4000f, 70000f, Corps(1, 240f, 9000f), Corps(2, 700f, 9000f), Corps(3, 720f, 9000f), Corps(4, 740f, 9000f));
        big.Own[big.IndexOfOwn(GuardId)] = Corps(GuardId, 0f, 14000f);   // the capital's floor (the minimum plus this army's weight) is met by its guard, so corps 1 stays the town's hold
        var bigPlan = Plan(big);
        AssertNear(15000f, bigPlan.Garrisons[0].Need, 1f, "42000 - 23000 - 4000: " + bigPlan.Garrisons[0].Need);
        AssertEqual(2, bigPlan.Garrisons[0].InstanceIds.Count, "two corps are needed: " + string.Join(",", bigPlan.Garrisons[0].InstanceIds));
        AssertEqual(2, bigPlan.Garrisons[0].InstanceIds[0], "the nearest first");
        AssertEqual(3, bigPlan.Garrisons[0].InstanceIds[1], "then the next");
        AssertTrue(bigPlan.Garrisons[0].Shortfall <= 0f, "and nothing is short");

        // A hold kept against a different army is not subtracted: an East army of 30000 (18000) keeps corps 2 and 3 in the East; the town's
        // hold is still only corps 1 and the capital guard (27000 against the town's army of 45000), so 5000 is needed and the free corps 4 goes.
        var other = FlagAlone(4000f, 45000f, Corps(1, 240f, 9000f), Corps(2, 700f, 9000f), Corps(3, 720f, 9000f), Corps(4, 760f, 9000f));
        other.Beliefs.Add(Seen(900f, 30000f));
        var otherPlan = Plan(other);
        AssertNear(5000f, otherPlan.Garrisons[0].Need, 1f, "the East hold faces another army and is not counted against this one: " + otherPlan.Garrisons[0].Need);

        // The hold already kept covers the army: nothing is needed (the same army is not counted twice).
        var covered = FlagAlone(4000f, 30000f, Corps(1, 240f, 9000f), Corps(2, 220f, 9000f), Corps(3, 700f, 9000f), Corps(4, 720f, 9000f));
        covered.Own[covered.IndexOfOwn(GuardId)] = Corps(GuardId, 0f, 8800f);   // meets the capital's floor but leaves the town's sector short, so both corps are its hold reserve (a guard in the sector counts toward its hold first)
        var coveredPlan = Plan(covered);
        AssertNear(0f, coveredPlan.Garrisons[0].Need, 1f, "two corps hold 18000 against the army: the garrison needs nothing more");
        AssertEqual(0, coveredPlan.Garrisons[0].InstanceIds.Count, "nobody is posted");

        // A garrison strong enough for the threat: nothing is posted.
        var strong = FlagAlone(18000f, 30000f, Corps(1, 700f, 9000f), Corps(2, 720f, 9000f));
        AssertEqual(0, Plan(strong).Garrisons[0].InstanceIds.Count, "a garrison that meets the hold needs nobody");

        // No enemy believed near: the flag is enough, nothing is posted (no floor, no presumption).
        var quiet = FlagAlone(4000f, 0f, Corps(1, 700f, 9000f), Corps(2, 720f, 9000f));
        var quietPlan = Plan(quiet);
        AssertNear(0f, quietPlan.Garrisons[0].Need, 0.01f, "no threat, no need");
        AssertEqual(0, quietPlan.Garrisons[0].InstanceIds.Count, "nothing posted");

        // An enemy far from the town (outside the relocation range) is not near: fog-honest, beliefs only.
        var far = FlagAlone(4000f, 0f, Corps(1, 700f, 9000f), Corps(2, 720f, 9000f));
        far.Beliefs.Add(Seen(900f, 30000f));
        AssertEqual(0, Plan(far).Garrisons[0].InstanceIds.Count, "a belief far away posts nothing");

        // A player's group is never one of the posted.
        var map = Corps(2, 700f, 9000f);
        map.Chain = WlChainClass.PlayerMapGroup;
        var player = FlagAlone(4000f, 45000f, Corps(1, 240f, 9000f), map, Corps(3, 720f, 9000f), Corps(4, 740f, 9000f));   // sized as the first case: the guard and corps 1 hold 18000 of 27000
        var playerPlan = Plan(player);
        AssertFalse(playerPlan.Garrisons[0].InstanceIds.Contains(2), "the map-group player is not posted");
        AssertEqual(3, playerPlan.Garrisons[0].InstanceIds[0], "the next free formation is");
    }

    // Landing check, structural: the garrison is chosen after the mass and never takes a formation marching with or released to it; it
    // takes the free formations nearest the town first; a need only the mass could meet is posted as far as the free formations go and the
    // rest is recorded as the post's shortfall.
    private static void TheGarrisonNeverTakesTheMassAndRecordsWhatItCouldNotMeet()
    {
        // A main effort at x 650 (an army of 3000 believed there, in the East): corps 2, standing at 700, is both the nearest East corps to the
        // town and the nearest to that target, so it is the mass; it is not a hold reserve (a busy corps holds the East). Corps 1 holds the
        // town's sector (a hold reserve, never the garrison's).
        CampaignSnapshot World(params FormationRecord[] corps)
        {
            var w = FlagAlone(4000f, 60000f, corps);   // 0.6 x 60000 = 36000 at the town
            var busy = Corps(9, 660f, 6000f);          // a corps in battle in the East holds the East's own 5000 floor, so no East corps is its hold reserve
            busy.Flags |= FormationFlags.InBattle;
            w.Own.Add(busy);
            w.Objectives.Add(new ObjectiveRecord { ObjectiveId = 31, StateId = 5, X = 650f, Z = 0f, HolderAlliance = 1, Available = true, Importance01 = 0.9f });
            w.Beliefs.Add(Seen(650f, 3000f));
            return w;
        }

        var s = World(Corps(1, 240f, 9000f), Corps(2, 700f, 9000f), Corps(3, 720f, 9000f), Corps(4, 740f, 9000f), Corps(5, 760f, 9000f));
        var plan = Plan(s);
        AssertEqual(31, plan.MainObjectiveId, "a main effort exists: " + plan.MainObjectiveId);
        AssertTrue(plan.MassInstanceIds.Contains(2), "corps 2 marches with the mass: " + string.Join(",", plan.MassInstanceIds));
        var post = plan.Garrisons[0];
        AssertTrue(post.InstanceIds.Count > 0, "free formations exist, so some are posted: need " + post.Need);
        foreach (int id in post.InstanceIds)
            AssertFalse(plan.MassInstanceIds.Contains(id), "formation " + id + " is posted but marches with the mass");
        AssertEqual(3, post.InstanceIds[0], "the nearest free formation is first (corps 2, nearer, is the mass)");
        for (int k = 1; k < post.InstanceIds.Count; k++)
            AssertTrue(DistanceToTown(s, post.InstanceIds[k - 1]) <= DistanceToTown(s, post.InstanceIds[k]), "nearest first");

        // Only the mass is free: nothing is taken from it; the shortfall is the whole need.
        var tight = World(Corps(1, 240f, 9000f), Corps(2, 700f, 9000f));
        var tightPlan = Plan(tight);
        AssertTrue(tightPlan.MassInstanceIds.Contains(2), "corps 2 is the mass");
        AssertEqual(0, tightPlan.Garrisons[0].InstanceIds.Count, "and the garrison does not take it");
        AssertTrue(tightPlan.Garrisons[0].Shortfall > 0f, "the shortfall is recorded: " + tightPlan.Garrisons[0].Shortfall);
        AssertNear(tightPlan.Garrisons[0].Need, tightPlan.Garrisons[0].Shortfall, 1f, "all of it");
        var row = ForceAllocator.Summary(Allocate(tight, tightPlan), tightPlan, PlayerCommandClass.BelowMapLevel, 1f, 0);
        AssertContains(row, "!short", "the allocation row logs the shortfall");

        // Part of the need met by the free formations: the shortfall is what remains.
        var partial = World(Corps(1, 240f, 9000f), Corps(2, 700f, 9000f), Corps(3, 720f, 9000f));
        var partialPlan = Plan(partial);
        AssertEqual(1, partialPlan.Garrisons[0].InstanceIds.Count, "the one free formation is posted");
        AssertEqual(3, partialPlan.Garrisons[0].InstanceIds[0], "corps 3, not the mass");
        AssertNear(partialPlan.Garrisons[0].Need - 9000f, partialPlan.Garrisons[0].Shortfall, 1f, "and the shortfall is the rest");
    }

    private static float DistanceToTown(CampaignSnapshot s, int instanceId)
    {
        var f = s.Own[s.IndexOfOwn(instanceId)];
        float dx = f.X - 300f, dz = f.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
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
