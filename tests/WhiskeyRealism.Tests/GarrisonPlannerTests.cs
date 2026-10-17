using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Util;

using static TestFixtures;
using static TestHarness;

// Stage G slice G8 (docs/superpowers/plans/2026-10-15-campaign-planner.md § G8): garrison manning beyond the capital, on top of D8's
// secession garrison. The two sorts of post share one role (FormationRole.Garrison), one withhold (Assignment.Withheld, read by
// CampaignWithholdView) and one ledger (GarrisonLedger); the tests pin each plan test name, the sharing, and the fog and W&L rules.
// Fixture: eleven areas (x 0..1000, relocation range 150), the capital at x 0, corps with bugle range 5 (ExecutorFixtures.Corps).
internal static class GarrisonPlannerTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("PostsOnlyOwnTownsHubsPortsDepotsForts", PostsOnlyOwnTownsHubsPortsDepotsForts),
            ("ASiteIsOnePostWhateverGuardsIt", ASiteIsOnePostWhateverGuardsIt),
            ("CapitalNeverAGarrisonPost", CapitalNeverAGarrisonPost),
            ("PostOnlyInThreatenedOrHoldSectors", PostOnlyInThreatenedOrHoldSectors),
            ("NeedSubtractsFortGarrisonDummy", NeedSubtractsFortGarrisonDummy),
            ("NeedFromBeliefsOnly", NeedFromBeliefsOnly),
            ("NeverFromMainEffortBelowRequiredRatio", NeverFromMainEffortBelowRequiredRatio),
            ("NeverTakesCapitalGuardReconOrRecover", NeverTakesCapitalGuardReconOrRecover),
            ("PrefersSmallestFormationCoveringNeed", PrefersSmallestFormationCoveringNeed),
            ("PickFormationNeverAnUnreadyOrPlayerFormation", PickFormationNeverAnUnreadyOrPlayerFormation),
            ("PostsCappedPerAlliance", PostsCappedPerAlliance),
            ("ReleasedAfterQuietDays", ReleasedAfterQuietDays),
            ("AGoneIncumbentReleasesItsPost", AGoneIncumbentReleasesItsPost),
            ("NeverPlayerCommandOrSoleMapGroupPlayer", NeverPlayerCommandOrSoleMapGroupPlayer),
            ("GarrisonWithheldFromVanillaOperations", GarrisonWithheldFromVanillaOperations),
            ("GarrisonPostsRoundTripInSidecar", GarrisonPostsRoundTripInSidecar),
            ("GarrisonAlliancesTwoToFourIgnored", AlliancesTwoToFourIgnored),
            ("SecessionAndPostsShareOneRoleOneWithholdOneLedger", SecessionAndPostsShareOneRoleOneWithholdOneLedger),
            ("ASecessionMemberInTheStateIsSeatedAtItsPostAndHolds", ASecessionMemberInTheStateIsSeatedAtItsPostAndHolds),
            ("ASecessionTownIsNeverAlsoAGarrisonPost", ASecessionTownIsNeverAlsoAGarrisonPost),
            ("AFormationHoldsOnePostAtMostAndPostsNeverShareIt", AFormationHoldsOnePostAtMostAndPostsNeverShareIt),
            ("TheIncumbentKeepsItsPostAndIsNotItsOwnExistingStrength", TheIncumbentKeepsItsPostAndIsNotItsOwnExistingStrength),
            ("ANeedNoFormationCoversIsRecordedUncovered", ANeedNoFormationCoversIsRecordedUncovered),
            ("TwoCorpsTogetherManAPostNoSingleCorpsCovers", TwoCorpsTogetherManAPostNoSingleCorpsCovers),
            ("ACombinedPostNeverTakesTheMass", ACombinedPostNeverTakesTheMass),
            ("ADepotBuilderDoesNotTakeASeatedGarrison", ADepotBuilderDoesNotTakeASeatedGarrison),
            ("AReconPickDoesNotTakeASeatedGarrison", AReconPickDoesNotTakeASeatedGarrison),
            ("ASeatedGarrisonIsWithheldByItsOwnRule", ASeatedGarrisonIsWithheldByItsOwnRule),
            ("EveryMemberOfAPostIsItsIncumbent", EveryMemberOfAPostIsItsIncumbent),
            ("ATwoFormationPostHoldsBothMembersForTenDays", ATwoFormationPostHoldsBothMembersForTenDays),
            ("APlayerFormationIsNeverAPostsIncumbent", APlayerFormationIsNeverAPostsIncumbent),
            ("AFormationAtThePostHoldsInPlaceAndOneAwayMarches", AFormationAtThePostHoldsInPlaceAndOneAwayMarches),
            ("AReserveSurplusMayBePostedButTheMassNever", AReserveSurplusMayBePostedButTheMassNever),
            ("AFormationTheTrackerGaveUpOnIsNotSeated", AFormationTheTrackerGaveUpOnIsNotSeated),
            ("TheSectorsHoldReserveDoesNotTakeAPostsIncumbentFromIt", TheSectorsHoldReserveDoesNotTakeAPostsIncumbentFromIt),
            ("TheSecessionRowIgnoresTheOrdinaryPosts", TheSecessionRowIgnoresTheOrdinaryPosts),
            ("TheLedgerRecordsMovingHoldingUncoveredAndReleased", TheLedgerRecordsMovingHoldingUncoveredAndReleased),
            ("TheGarrisonRowsCarryTheSpecifiedFields", TheGarrisonRowsCarryTheSpecifiedFields),
            ("PostsAreAMoverRoleAndTheMoverMarchesThem", PostsAreAMoverRoleAndTheMoverMarchesThem),
            ("AllocationWithPostsIsDeterministicAndLeavesThePlanAlone", AllocationWithPostsIsDeterministicAndLeavesThePlanAlone),
            ("ARealPlanThenPostsThenAllocationSeatsDistinctFormations", ARealPlanThenPostsThenAllocationSeatsDistinctFormations),
            ("EveryGarrisonUnitReadFollowsTheSideCheck", EveryGarrisonUnitReadFollowsTheSideCheck),
            ("OnlyOwnAndAlliedForts", OnlyOwnAndAlliedForts),
            ("TheRuntimeReadsItsFlagAndGatesOnlyThePostsNotTheSecession", TheRuntimeReadsItsFlagAndGatesOnlyThePostsNotTheSecession),
        });
    }

    // ---- fixtures ----

    private static CampaignSnapshot Side(int alliance = 0)
    {
        var s = ExecutorFixtures.Line(alliance, 11);
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.3f;
        return s;
    }

    private static ObjectiveRecord Town(int id, float x, int holder = 0, ObjectiveRole roles = ObjectiveRole.None, float garrison = 0f)
    {
        return new ObjectiveRecord { ObjectiveId = id, X = x, Z = 0f, HolderAlliance = holder, Available = true, Roles = roles, GarrisonStrength = garrison, Importance01 = 0.4f };
    }

    private static SupplySourceRecord Depot(int id, float x, int owner = 0, bool built = true)
    {
        return new SupplySourceRecord { Id = id, Kind = SupplySourceKind.Depot, Owner = owner, X = x, Z = 0f, Condition = 1f, Built = built };
    }

    private static FortRecord Fort(float x, float garrison = 0f)
    {
        return new FortRecord { X = x, Z = 0f, GarrisonStrength = garrison };
    }

    private static EnemyBelief Seen(float x, float strength, float confidence = 1f)
    {
        var b = ExecutorFixtures.Belief(x, 0f, strength);
        b.Confidence01 = confidence;
        return b;
    }

    // Vanilla's capital-need weighting of one army (AssignUnitToDefendCapital 11733-11740), the formula G8 reuses for a post's threat.
    private static float Weighted(float strength, float distance)
    {
        return strength / (float)Math.Pow(Math.Max(10f, distance), 0.3) * 0.5f;
    }

    private static TheaterPlan HandPlan(CampaignSnapshot s)
    {
        return new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial };
    }

    private static List<GarrisonPost> Posts(CampaignSnapshot s, TheaterPlan plan = null, IReadOnlyList<GarrisonPostRecord> retained = null)
    {
        var into = new List<GarrisonPost>();
        GarrisonPlanner.PlanPosts(s, plan ?? HandPlan(s), retained, into);
        return into;
    }

    // The runtime's order: the plan's D8 posts are in plan.Garrisons already, then the ordinary posts join them, then the allocation.
    private static Allocation Allocate(CampaignSnapshot s, TheaterPlan plan, IReadOnlyList<GarrisonPostRecord> retained = null, IReadOnlyList<GaveUpMove> gaveUp = null)
    {
        plan.Garrisons.AddRange(Posts(s, plan, retained));
        return ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null, gaveUp);
    }

    private static FormationRecord Corps(int id, float x, float strength)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.SightRadius = 50f;
        f.Morale = 0.9f;
        return f;
    }

    private static GarrisonPost PostAt(IReadOnlyList<GarrisonPost> posts, float x)
    {
        foreach (var p in posts)
            if (Math.Abs(p.X - x) < 1f) return p;
        return null;
    }

    private static GarrisonPostRecord Record(GarrisonPostKind kind, float x, string key, int quietSince = -1)
    {
        return new GarrisonPostRecord { Kind = kind, X = x, Z = 0f, AssignedUnitKeys = key == null ? new string[0] : new[] { key }, QuietSinceDay = quietSince };
    }

    private static GarrisonPostRecord Record(GarrisonPostKind kind, float x, params string[] keys)
    {
        return new GarrisonPostRecord { Kind = kind, X = x, Z = 0f, AssignedUnitKeys = keys, QuietSinceDay = -1 };
    }

    // ---- the plan's tests ----

    private static void PostsOnlyOwnTownsHubsPortsDepotsForts()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 300f));
        s.Objectives.Add(Town(41, 400f, roles: ObjectiveRole.RailHub));
        s.Objectives.Add(Town(42, 700f, roles: ObjectiveRole.Port));
        s.Objectives.Add(Town(43, 900f, holder: 1));                    // enemy-held: never a post
        s.Supply.Add(Depot(601, 600f));
        s.Supply.Add(Depot(602, 800f, owner: 1));                       // an enemy depot
        s.Supply.Add(Depot(603, 250f, built: false));                   // not built yet
        s.Forts.Add(Fort(500f));
        foreach (float x in new[] { 300f, 500f, 700f, 900f }) s.Beliefs.Add(Seen(x, 30000f));
        var posts = Posts(s);
        AssertEqual(5, posts.Count, "town, hub, fort, depot, port: " + string.Join(",", posts.Select(p => p.Kind + "@" + p.X)));
        AssertEqual(GarrisonPostKind.ObjectiveTown, PostAt(posts, 300f).Kind, "an own objective town");
        AssertEqual(GarrisonPostKind.RailHub, PostAt(posts, 400f).Kind, "an own rail hub");
        AssertEqual(GarrisonPostKind.Fort, PostAt(posts, 500f).Kind, "an own fort");
        AssertEqual(GarrisonPostKind.Depot, PostAt(posts, 600f).Kind, "an own built depot");
        AssertEqual(GarrisonPostKind.Port, PostAt(posts, 700f).Kind, "an own port");
        AssertTrue(PostAt(posts, 900f) == null && PostAt(posts, 800f) == null && PostAt(posts, 250f) == null, "no enemy town or depot and no unbuilt depot");
        foreach (var p in posts) AssertTrue(p.Need > 0f, "each stands for a need at " + p.X);
    }

    // A town, the fort beside it and a depot on the road are one place to man: one post, named for the most specific thing there, with the
    // strongest garrison dummy of them (the town's total already includes its fort's, so the two are not added).
    private static void ASiteIsOnePostWhateverGuardsIt()
    {
        float full = GarrisonPlanner.ThreatShare * Weighted(30000f, 60f);
        var s = Side();
        s.Objectives.Add(Town(40, 500f, garrison: 300f));
        s.Forts.Add(Fort(520f, garrison: 700f));
        s.Supply.Add(Depot(601, 510f));
        s.Beliefs.Add(Seen(560f, 30000f));
        var posts = Posts(s);
        AssertEqual(1, posts.Count, "one post for the three: " + string.Join(",", posts.Select(p => p.Kind + "@" + p.X)));
        AssertEqual(GarrisonPostKind.ObjectiveTown, posts[0].Kind, "named for the town, not the fort or the depot");
        AssertNear(700f, posts[0].ExistingStrength, 0.01f, "with the strongest dummy, not their sum");
        AssertNear(full - 700f, posts[0].Need, 1f, "which the need subtracts");

        var port = Side();
        port.Objectives.Add(Town(40, 500f, roles: ObjectiveRole.RailHub | ObjectiveRole.Port));
        port.Forts.Add(Fort(510f));
        port.Beliefs.Add(Seen(560f, 30000f));
        AssertEqual(GarrisonPostKind.Port, Posts(port)[0].Kind, "a port that is also a hub and a town is a port");

        var apart = Side();
        apart.Objectives.Add(Town(40, 500f));
        apart.Forts.Add(Fort(560f));                                      // 60 away: its own place
        apart.Beliefs.Add(Seen(560f, 30000f));
        AssertEqual(2, Posts(apart).Count, "a fort a bugle call and more away is a post of its own");
    }

    private static void CapitalNeverAGarrisonPost()
    {
        var s = Side();
        s.Objectives.Add(Town(1, 0f, roles: ObjectiveRole.Capital));    // the capital: the capital floor owns it
        s.Supply.Add(Depot(601, 5f));                                   // a depot inside the capital's site
        s.Objectives.Add(Town(40, 300f));
        s.Beliefs.Add(Seen(40f, 50000f));
        s.Beliefs.Add(Seen(340f, 20000f));
        var posts = Posts(s);
        AssertEqual(1, posts.Count, "only the town away from the capital");
        AssertNear(300f, posts[0].X, 0.01f, "that town");
    }

    // The plan's sector clause: a post stands where an army is believed to threaten it; the posture of the sector it lies in adds none by
    // itself (Need is beliefs only), and a manned post stays until it has been quiet for ReleaseQuietDays whatever its sector holds.
    private static void PostOnlyInThreatenedOrHoldSectors()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 300f));
        s.Objectives.Add(Town(41, 700f));
        s.Beliefs.Add(Seen(760f, 30000f));
        var plan = HandPlan(s);
        var sectors = ExecutorFixtures.Sectors(s);
        plan.Sectors.Add(new SectorPlan { SectorId = sectors.SectorAt(300f, 0f), Posture = TheaterPosture.Hold, HoldNeed = 5000f });
        plan.Sectors.Add(new SectorPlan { SectorId = sectors.SectorAt(700f, 0f), Posture = TheaterPosture.MainEffort });
        var posts = Posts(s, plan);
        AssertEqual(1, posts.Count, "one post");
        AssertNear(700f, posts[0].X, 0.01f, "the threatened town, whatever its sector's posture");
        AssertTrue(PostAt(posts, 300f) == null, "a Hold sector's town with no army near it has no post");
    }

    private static void NeedSubtractsFortGarrisonDummy()
    {
        float full = GarrisonPlanner.ThreatShare * Weighted(30000f, 60f);
        var s = Side();
        s.Objectives.Add(Town(40, 500f, garrison: 1000f));
        s.Beliefs.Add(Seen(560f, 30000f));
        var post = Posts(s)[0];
        AssertNear(full - 1000f, post.Need, 1f, "the fort dummy at the town is already there");
        AssertNear(1000f, post.ExistingStrength, 0.01f, "and is the post's existing strength");

        var covered = Side();
        covered.Objectives.Add(Town(40, 500f, garrison: full + 1f));
        covered.Beliefs.Add(Seen(560f, 30000f));
        AssertEqual(0, Posts(covered).Count, "a garrison that already covers the threat needs nobody");

        var fort = Side();
        fort.Forts.Add(Fort(500f, garrison: 700f));
        fort.Beliefs.Add(Seen(560f, 30000f));
        AssertNear(full - 700f, Posts(fort)[0].Need, 1f, "a fort post subtracts its own dummy");

        var near = Side();
        near.Objectives.Add(Town(40, 500f));
        near.Beliefs.Add(Seen(560f, 30000f));
        near.Own.Add(Corps(7, 503f, 600f));                              // an own formation within bugle range counts too
        near.Own.Add(Corps(8, 600f, 9000f));                             // one beyond it does not
        AssertNear(full - 600f, Posts(near)[0].Need, 1f, "an own formation at the post is existing strength");

        // The main effort's mass passing through does not garrison the place it passes: it will march on.
        var passing = Side();
        passing.Objectives.Add(Town(40, 500f));
        passing.Beliefs.Add(Seen(560f, 30000f));
        passing.Own.Add(Corps(9, 503f, 9000f));
        var plan = HandPlan(passing);
        plan.MassInstanceIds.Add(9);
        AssertNear(full, Posts(passing, plan)[0].Need, 1f, "a mass member standing there is not existing strength");
        AssertNear(0f, Posts(passing, plan)[0].ExistingStrength, 0.01f, "none at all");
    }

    private static void NeedFromBeliefsOnly()
    {
        var none = Side();
        none.Objectives.Add(Town(40, 500f));
        none.Objectives.Add(Town(50, 580f, holder: 1, roles: ObjectiveRole.RailHub)); // enemy ground beside it: no inference from it
        AssertEqual(0, Posts(none).Count, "no believed army, no post: the planner never reads vanilla's enemy lists");

        var off = Side();
        off.Objectives.Add(Town(40, 500f));
        off.Beliefs.Add(Seen(520f, 30000f, confidence: 0f));              // expired
        off.Beliefs.Add(Seen(651f, 30000f));                              // 151 away: outside the relocation range
        AssertEqual(0, Posts(off).Count, "an expired and a far belief threaten nothing");

        // An army afloat counts, as the capital formula counts it: it can land at a port or a coast town (at its straight distance).
        var afloat = Side();
        afloat.Objectives.Add(Town(40, 500f, roles: ObjectiveRole.Port));
        var ship = Seen(560f, 30000f);
        ship.Traits = EnemyTraits.Embarked;
        afloat.Beliefs.Add(ship);
        AssertNear(GarrisonPlanner.ThreatShare * Weighted(30000f, 60f), Posts(afloat)[0].Need, 1f, "an embarked army near a port is a threat to it");

        var seen = Side();
        seen.Objectives.Add(Town(40, 500f));
        seen.Beliefs.Add(Seen(649f, 30000f, confidence: 0.5f));
        var post = Posts(seen)[0];
        AssertNear(GarrisonPlanner.ThreatShare * Weighted(15000f, 149f), post.Need, 1f, "the confidence-weighted strength at its distance");
    }

    private static void NeverFromMainEffortBelowRequiredRatio()
    {
        // The main effort's mass is never stripped by a post, however near and large its members are (the D8 ruling).
        var s = ExecutorFixtures.Wide();
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.3f;
        s.Objectives.Add(Town(40, 400f));
        s.Beliefs.Add(Seen(100f, 18000f));                                 // the enemy at the main objective: the mass is no more than it needs
        s.Beliefs.Add(Seen(440f, 30000f));                                 // and the threat to the town
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 395f, 9000f));                                  // a mass member standing at the town
        s.Own.Add(Corps(3, 380f, 9000f));                                  // the one free formation
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2 });
        var alloc = Allocate(s, plan);
        alloc.TryGet(2, out var mass);
        AssertEqual(FormationRole.MainEffort, mass.Role, "the mass member stays the main effort");
        alloc.TryGet(3, out var posted);
        AssertEqual(ForceAllocator.ReasonGarrisonPost, posted.Reason, "the free formation holds the post");
        AssertEqual(1, alloc.Seats.Count, "one seat");

        var onlyMass = ExecutorFixtures.Wide();
        onlyMass.MinStrengthForMovement = 5000f;
        onlyMass.MinMoraleForMovement = 0.3f;
        onlyMass.Objectives.Add(Town(40, 400f));
        onlyMass.Beliefs.Add(Seen(100f, 18000f));
        onlyMass.Beliefs.Add(Seen(440f, 30000f));
        onlyMass.Own.Add(Corps(1, 200f, 20000f));
        onlyMass.Own.Add(Corps(2, 395f, 9000f));
        var plan2 = ExecutorFixtures.HandPlan(onlyMass, new[] { 1, 2 });
        var alloc2 = Allocate(onlyMass, plan2);
        AssertEqual(0, alloc2.Seats.Count, "with only mass members to hand the post goes unmanned");
        alloc2.TryGet(2, out var stillMass);
        AssertEqual(FormationRole.MainEffort, stillMass.Role, "and the mass is untouched");
    }

    private static void NeverTakesCapitalGuardReconOrRecover()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 100f));
        s.Beliefs.Add(Seen(130f, 30000f));
        s.Own.Add(Corps(1, 20f, 9000f));                                  // the capital guard
        var starving = Corps(2, 105f, 9000f);
        starving.Supply01 = 0.1f;                                         // Recover
        s.Own.Add(starving);
        var busy = Corps(3, 110f, 9000f);
        busy.Flags |= FormationFlags.InBattle;                            // busy: immobile
        s.Own.Add(busy);
        var plan = HandPlan(s);
        plan.CapitalFloorStrength = 6000f;
        var heldBuilder = Corps(4, 95f, 9000f);
        s.Own.Add(heldBuilder);
        var pinned = new List<HeldBuilder> { new HeldBuilder { InstanceId = 4, Wished = true, SectorId = 0, TargetX = 95f, TargetZ = 0f, SiteX = 95f, SiteZ = 0f } };
        plan.Garrisons.AddRange(Posts(s, plan));
        var alloc = ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null, null, pinned);
        alloc.TryGet(1, out var guard);
        alloc.TryGet(2, out var recover);
        AssertEqual(FormationRole.CapitalGuard, guard.Role, "corps 1 guards the capital");
        AssertEqual(FormationRole.Recover, recover.Role, "corps 2 recovers");
        AssertEqual(FormationRole.DepotBuilder, alloc.Assignments.First(a => a.InstanceId == 4).Role, "corps 4 builds a depot");
        AssertEqual(0, alloc.Seats.Count, "each of the four is in a role of its own: nobody is left for the post");
        foreach (var a in alloc.Assignments) AssertTrue(a.Reason != ForceAllocator.ReasonGarrisonPost, "no formation is kept for the post: " + a.InstanceId);
    }

    private static void PrefersSmallestFormationCoveringNeed()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));                                // Need about 2200
        s.Own.Add(Corps(1, 520f, 20000f));
        s.Own.Add(Corps(2, 540f, 6000f));
        s.Own.Add(Corps(3, 430f, 3000f));                                 // covers, smallest, but farther than 4
        s.Own.Add(Corps(4, 470f, 3000f));                                 // covers, smallest, nearest
        s.Own.Add(Corps(5, 480f, 1500f));                                 // does not cover
        var post = Posts(s)[0];
        AssertTrue(post.Need > 1500f && post.Need < 3000f, "the need " + post.Need + " sits between the small and the middling corps");
        var first = Pick(s, post, AllFree(s));
        AssertEqual(1, first.Count, "one formation covers it");
        AssertEqual(4, first[0], "the smallest that covers it, the nearer on a tie");
        var pool = AllFree(s);
        pool[s.IndexOfOwn(4)] = false;
        AssertEqual(3, Pick(s, post, pool)[0], "then the other");
        var far = Side();
        far.Objectives.Add(Town(40, 500f));
        far.Beliefs.Add(Seen(560f, 30000f));
        far.Own.Add(Corps(6, 800f, 6000f));                               // beyond the relocation range of the post
        AssertEqual(0, Pick(far, Posts(far)[0], AllFree(far)).Count, "a formation beyond the relocation range is not asked");
    }

    // The pool says what the allocator has left free; the pick still asks the formation itself: ready (vanilla refuses to march a readiness 0-1
    // formation onto contested ground), above the movement morale and supply floors, and one the planner may move.
    private static void PickFormationNeverAnUnreadyOrPlayerFormation()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        var unready = Corps(1, 480f, 6000f);
        unready.ReadinessStep = 1;
        s.Own.Add(unready);
        var shaken = Corps(2, 485f, 6000f);
        shaken.Morale = 0.1f;
        s.Own.Add(shaken);
        var player = Corps(3, 490f, 6000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        var post = Posts(s)[0];
        AssertEqual(0, Pick(s, post, AllFree(s)).Count, "an unready, a shaken and a player's formation: none is asked");
        s.Own.Add(Corps(5, 470f, 6000f));
        AssertEqual(5, Pick(s, post, AllFree(s))[0], "the one fit formation is");
        var met = new GarrisonPost { Kind = GarrisonPostKind.ObjectiveTown, X = 500f, Z = 0f, Need = 0f };
        AssertEqual(0, Pick(s, met, AllFree(s)).Count, "a post with no need and no incumbent asks nobody");
        AssertEqual(0, Pick(s, post, AllFree(s), have: post.Need).Count, "nor one whose need the incumbent already meets");
        AssertEqual(0, Pick(s, post, AllFree(s), slots: 0).Count, "nor one with no slot left");
    }

    // The instance ids GarrisonPlanner.PickFormations names for a post with nothing seated yet and both slots free.
    private static List<int> Pick(CampaignSnapshot s, GarrisonPost post, bool[] pool, float have = 0f, int slots = GarrisonPlanner.MaxFormationsPerPost)
    {
        var into = new List<int>();
        GarrisonPlanner.PickFormations(s, post, pool, have, slots, into);
        return into.Select(i => s.Own[i].InstanceId).ToList();
    }

    private static bool[] AllFree(CampaignSnapshot s)
    {
        var pool = new bool[s.Own.Count];
        for (int i = 0; i < pool.Length; i++) pool[i] = true;
        return pool;
    }

    private static void PostsCappedPerAlliance()
    {
        var s = Side();
        for (int k = 1; k <= 8; k++)
        {
            s.Objectives.Add(Town(40 + k, 200f * k));
            s.Beliefs.Add(Seen(200f * k, 4000f * k));                     // 200 apart: out of each other's range
        }
        var posts = Posts(s);
        AssertEqual(GarrisonPlanner.MaxPostsPerAlliance, posts.Count, "at most six posts");
        AssertTrue(PostAt(posts, 200f) == null && PostAt(posts, 400f) == null, "the two smallest needs are dropped");
        AssertTrue(PostAt(posts, 1600f) != null, "the largest is kept");
        for (int i = 1; i < posts.Count; i++) AssertTrue(posts[i - 1].Need >= posts[i].Need, "largest need first");

        // A manned post is kept ahead of a larger new need: no churn at the cap.
        var held = Posts(s, null, new[] { Record(GarrisonPostKind.ObjectiveTown, 200f, "corps9:9") });
        var withHeld = Side();
        withHeld.Own.Add(Corps(9, 200f, 6000f));
        for (int k = 1; k <= 8; k++)
        {
            withHeld.Objectives.Add(Town(40 + k, 200f * k));
            withHeld.Beliefs.Add(Seen(200f * k, 4000f * k));
        }
        var kept = Posts(withHeld, null, new[] { Record(GarrisonPostKind.ObjectiveTown, 200f, "corps9:9") });
        AssertEqual(GarrisonPlanner.MaxPostsPerAlliance, kept.Count, "still six");
        AssertTrue(PostAt(kept, 200f) != null, "the manned post keeps its place against larger new needs");
        AssertEqual(9, PostAt(kept, 200f).InstanceIds[0], "and its incumbent");
        AssertTrue(held.Count == GarrisonPlanner.MaxPostsPerAlliance, "an incumbent that is not in the snapshot holds no place");
        AssertTrue(PostAt(held, 200f) == null, "a record whose formation is gone is not kept");
    }

    private static CampaignSnapshot Day(int offset, bool threatened, int unit = 3)
    {
        var s = ExecutorFixtures.Line(0, 11, offset);
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.3f;
        s.Objectives.Add(Town(40, 500f));
        s.Own.Add(Corps(unit, 500f, 6000f));
        if (threatened) s.Beliefs.Add(Seen(560f, 30000f));
        return s;
    }

    // One review the way the runtime does it: posts from the ledger's records, the allocation, the ledger's sync.
    private static GarrisonSyncResult Review(GarrisonLedger ledger, CampaignSnapshot s, out TheaterPlan plan, out Allocation alloc)
    {
        plan = HandPlan(s);
        alloc = Allocate(s, plan, ledger.Posts);
        return ledger.Sync(plan.Garrisons, alloc, s);
    }

    private static void ReleasedAfterQuietDays()
    {
        var ledger = new GarrisonLedger();
        var seed = new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps3:3") };
        ledger.Restore(seed);
        var r0 = Review(ledger, Day(0, true), out var plan0, out _);
        AssertEqual(1, plan0.Garrisons.Count, "day 0: the army is near, the post stands");
        AssertEqual(1, r0.Manned, "manned by its incumbent");
        AssertEqual(-1, ledger.Posts[0].QuietSinceDay, "and not quiet");

        Review(ledger, Day(1, false), out var plan1, out _);
        AssertEqual(1, plan1.Garrisons.Count, "day 1: nobody near, the post is kept");
        AssertEqual(Day(1, false).DaySerial, plan1.Garrisons[0].QuietSinceDay, "quiet since today");
        AssertEqual(Day(1, false).DaySerial, ledger.Posts[0].QuietSinceDay, "the ledger remembers when the quiet began");

        Review(ledger, Day(14, false), out var plan14, out _);
        AssertEqual(1, plan14.Garrisons.Count, "day 14: thirteen quiet days, still kept");
        AssertEqual(GarrisonPlanner.ReleaseQuietDays - 1, Day(14, false).DaySerial - plan14.Garrisons[0].QuietSinceDay, "thirteen days");

        // A sighting resets the quiet.
        Review(ledger, Day(15, true), out var plan15, out _);
        AssertEqual(1, plan15.Garrisons.Count, "an army returns on day 15");
        AssertEqual(-1, ledger.Posts[0].QuietSinceDay, "the quiet is over");

        Review(ledger, Day(16, false), out _, out _);
        var released = Review(ledger, Day(16 + GarrisonPlanner.ReleaseQuietDays, false), out var planEnd, out var allocEnd);
        AssertEqual(0, planEnd.Garrisons.Count, "after the quiet fortnight the post is released");
        AssertEqual(1, released.Released, "the ledger says so");
        AssertEqual(0, ledger.Posts.Count, "and forgets it");
        allocEnd.TryGet(3, out var free);
        AssertTrue(free.Reason != ForceAllocator.ReasonGarrisonPost && free.Role == FormationRole.Hold, "the formation holds as any other (no post)");
    }

    // The verifier's probe: the incumbent holds its town and a depot task sits in its sector. SmallestIn picks the smallest formation of the
    // sector, which is the garrison; the incumbent is claimed first, so the larger corps builds the depot and the post stays manned.
    private static void ADepotBuilderDoesNotTakeASeatedGarrison()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(1, 503f, 3000f));                                // the incumbent, the smallest formation of its sector
        s.Own.Add(Corps(3, 450f, 9000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = HandPlan(s);
        int sector = sectors.SectorAt(503f, 0f);
        AssertEqual(sector, sectors.SectorAt(450f, 0f), "both stand in one sector");
        plan.DepotTasks.Add(new DepotTask { X = 520f, Z = 0f, SectorId = sector, Priority = 1f });
        var alloc = Allocate(s, plan, new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") });
        alloc.TryGet(1, out var garrison);
        alloc.TryGet(3, out var builder);
        AssertEqual(ForceAllocator.ReasonGarrisonPost, garrison.Reason, "the incumbent keeps the post");
        AssertEqual(FormationRole.DepotBuilder, builder.Role, "and the other corps builds the depot");
        AssertEqual(1, alloc.Seats.Count, "one seat, not refilled");
        AssertEqual(1, alloc.Seats[0].InstanceId, "the incumbent's");
    }

    // The same for a recon pick: a cavalry-majority incumbent is the formation recon prefers, and a seated garrison is not a scout.
    private static void AReconPickDoesNotTakeASeatedGarrison()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        var cavalry = Corps(1, 503f, 6000f);
        cavalry.CavalryShare = 0.9f;
        s.Own.Add(cavalry);
        s.Own.Add(Corps(3, 450f, 9000f));
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = HandPlan(s);
        var task = new ReconTask { Kind = ReconKind.Screen, SectorId = sectors.SectorAt(503f, 0f), FromX = 560f, FromZ = 0f, HoldX = 520f, HoldZ = 0f, Priority = 1f, Reason = "t" };
        var tasks = new List<ReconTask> { task };
        plan.Garrisons.AddRange(Posts(s, plan, new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") }));
        var alloc = ForceAllocator.Allocate(plan, s, sectors, null, tasks, new ReconMissionLedger());
        alloc.TryGet(1, out var garrison);
        AssertEqual(ForceAllocator.ReasonGarrisonPost, garrison.Reason, "the incumbent stays the garrison");
        AssertTrue(garrison.Role != FormationRole.Recon, "and is not made a scout");
        alloc.TryGet(3, out var other);
        AssertEqual(FormationRole.Recon, other.Role, "the other corps takes the recon task (a control: the task is real)");

        // Without the post the cavalry corps is the scout: the task prefers it.
        var noPost = Side();
        noPost.Objectives.Add(Town(40, 500f));
        noPost.Own.Add(cavalry);
        noPost.Own.Add(Corps(3, 450f, 9000f));
        var noPostPlan = HandPlan(noPost);
        var scout = ForceAllocator.Allocate(noPostPlan, noPost, ExecutorFixtures.Sectors(noPost), null, tasks, new ReconMissionLedger());
        scout.TryGet(1, out var cav);
        AssertEqual(FormationRole.Recon, cav.Role, "control: with no post the cavalry corps is the one recon prefers");
    }

    // Seated means withheld by the post's own rule, not by luck: with the sector's hold already met by an immobile formation, the sector's
    // withhold takes nobody, and the garrison at its post is still out of vanilla's pools.
    private static void ASeatedGarrisonIsWithheldByItsOwnRule()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(1, 503f, 6000f));                                // the incumbent, at its post
        var busy = Corps(2, 480f, 30000f);
        busy.Flags |= FormationFlags.InBattle;                           // immobile: it counts toward the sector's hold, past its target
        s.Own.Add(busy);
        var free = Corps(3, 470f, 6000f);                                // an ordinary hold the sector's withhold would leave free
        s.Own.Add(free);
        var alloc = Allocate(s, HandPlan(s), new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") });
        alloc.TryGet(3, out var ordinary);
        AssertFalse(ordinary.Withheld, "control: the sector's own withhold takes nobody (its hold is met)");
        alloc.TryGet(1, out var garrison);
        AssertEqual(FormationRole.Hold, garrison.Role, "the garrison holds at its post");
        AssertTrue(garrison.Withheld, "and is withheld by the post's rule alone");
    }

    // Every seated member is an incumbent: the post names all of them, each is claimed, and none counts as the post's existing strength.
    private static void EveryMemberOfAPostIsItsIncumbent()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(515f, 60000f));
        s.Own.Add(Corps(1, 503f, 5500f));
        s.Own.Add(Corps(2, 497f, 5500f));
        s.Own.Add(Corps(3, 440f, 5500f));
        var retained = new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1", "corps2:2") };
        var post = Posts(s, null, retained)[0];
        AssertEqual("1,2", string.Join(",", post.InstanceIds), "both members are named, in record order");
        AssertNear(0f, post.ExistingStrength, 0.01f, "neither counts as existing strength");
        AssertNear(GarrisonPlanner.ThreatShare * Weighted(60000f, 15f), post.Need, 2f, "so the need is the whole threat");
        var alloc = Allocate(s, HandPlan(s), retained);
        AssertEqual("1,2", string.Join(",", alloc.Seats.Select(x => x.InstanceId).OrderBy(x => x)), "both are claimed and no third is added");
        AssertTrue(alloc.Seats.All(x => x.AtPost), "both hold at the post");
        var ledger = new GarrisonLedger();
        var plan = HandPlan(s);
        plan.Garrisons.AddRange(Posts(s, plan, retained));
        ledger.Sync(plan.Garrisons, ForceAllocator.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null), s);
        AssertEqual("corps1:1,corps2:2", string.Join(",", ledger.Posts[0].AssignedUnitKeys), "and the ledger records both");
    }

    // The verifier's probe: a 60000 army makes a need of 6657, corps 1 and 2 (5500 each) are posted and march in, a larger corps 3 stands in
    // the same sector, and a depot task opens on days 5-6. Corps 2 stays seated and withheld every day: it is never counted as existing
    // strength, never taken as the depot builder, never re-picked.
    private static void ATwoFormationPostHoldsBothMembersForTenDays()
    {
        var ledger = new GarrisonLedger();
        for (int day = 0; day < 10; day++)
        {
            var s = ExecutorFixtures.Line(0, 11, day);
            s.MinStrengthForMovement = 5000f;
            s.MinMoraleForMovement = 0.3f;
            s.Objectives.Add(Town(40, 500f));
            s.Beliefs.Add(Seen(515f, 60000f));
            float step = Math.Min(1f, day / 2f);                           // marching on days 0-1, at the post from day 2
            s.Own.Add(Corps(1, 450f + 50f * step, 5500f));
            s.Own.Add(Corps(2, 460f + 40f * step, 5500f));
            s.Own.Add(Corps(3, 300f, 9000f));                              // in the sector, beyond the post's relocation range
            var plan = HandPlan(s);
            if (day == 5 || day == 6)
                plan.DepotTasks.Add(new DepotTask { X = 520f, Z = 0f, SectorId = ExecutorFixtures.Sectors(s).SectorAt(503f, 0f), Priority = 1f });
            var alloc = Allocate(s, plan, ledger.Posts);
            ledger.Sync(plan.Garrisons, alloc, s);
            foreach (int id in new[] { 1, 2 })
            {
                alloc.TryGet(id, out var a);
                AssertEqual(ForceAllocator.ReasonGarrisonPost, a.Reason, "day " + day + ": corps " + id + " keeps the post");
                AssertTrue(a.Withheld, "day " + day + ": corps " + id + " is withheld");
            }
            AssertEqual(2, alloc.Seats.Count, "day " + day + ": exactly its two members, no third");
            if (day >= 2)
            {
                alloc.TryGet(2, out var arrived);
                AssertEqual(FormationRole.Hold, arrived.Role, "day " + day + ": corps 2 holds at the post");
            }
            if (day == 5 || day == 6)
            {
                alloc.TryGet(3, out var builder);
                AssertEqual(FormationRole.DepotBuilder, builder.Role, "day " + day + ": the depot goes to the free corps 3");
            }
            AssertEqual(2, ledger.Posts[0].AssignedUnitKeys.Length, "day " + day + ": the ledger holds both keys");
        }
    }

    // A player's formation can never be a post's incumbent, whatever the ledger says: it would keep a post alive that nobody holds.
    private static void APlayerFormationIsNeverAPostsIncumbent()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(7, 505f, 9000f));                                // an AI corps at the post: existing strength, the need is met
        var player = Corps(1, 540f, 5000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);
        var posts = Posts(s, null, new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") });
        AssertEqual(0, posts.Count, "the player's group named by the ledger holds nothing; the AI corps already covers the threat");
        var command = Side();
        command.Objectives.Add(Town(40, 500f));
        command.Beliefs.Add(Seen(560f, 30000f));
        var own = Corps(1, 505f, 9000f);
        own.Chain = WlChainClass.PlayerCommand;
        command.Own.Add(own);
        var after = Posts(command, null, new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") });
        AssertEqual(1, after.Count, "the threat still makes a post");
        AssertEqual(0, after[0].InstanceIds.Count, "but it names no incumbent");
    }

    // A manned post lives on its incumbent: a record whose formation has gone from the map (destroyed, disbanded) keeps nothing, however
    // fresh the record, and is not planned again unless the threat itself still asks for it.
    private static void AGoneIncumbentReleasesItsPost()
    {
        var calm = Side();
        calm.Objectives.Add(Town(40, 500f));
        var ghost = new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "ghost:99") };
        AssertEqual(0, Posts(calm, null, ghost).Count, "no army near and nobody to hold it: the post is released");
        var threatened = Side();
        threatened.Objectives.Add(Town(40, 500f));
        threatened.Beliefs.Add(Seen(560f, 30000f));
        var again = Posts(threatened, null, ghost);
        AssertEqual(1, again.Count, "an army near asks for it afresh");
        AssertEqual(0, again[0].InstanceIds.Count, "with no incumbent");
        var ledger = new GarrisonLedger();
        ledger.Restore(ghost);
        var plan = HandPlan(calm);
        var result = ledger.Sync(plan.Garrisons, Allocate(calm, plan, ledger.Posts), calm);
        AssertEqual(1, result.Released, "and the ledger forgets the record");
        AssertEqual(0, ledger.Posts.Count, "nothing is kept");
    }

    private static void NeverPlayerCommandOrSoleMapGroupPlayer()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        var player = Corps(1, 500f, 20000f);
        player.Chain = WlChainClass.PlayerMapGroup;
        s.Own.Add(player);                                               // the map-group player stands at the post
        var command = Corps(2, 480f, 3000f);
        command.Chain = WlChainClass.PlayerCommand;
        s.Own.Add(command);
        var cic = Corps(3, 470f, 3000f);
        cic.Chain = WlChainClass.CicSide;
        s.Own.Add(cic);
        s.Own.Add(Corps(4, 420f, 8000f));
        var post = Posts(s)[0];
        AssertNear(GarrisonPlanner.ThreatShare * Weighted(30000f, 60f), post.Need, 1f, "the player standing there is not the garrison: the need stands");
        AssertNear(0f, post.ExistingStrength, 0.01f, "none of the three counts as existing strength");
        var alloc = Allocate(s, HandPlan(s));
        AssertEqual(1, alloc.Seats.Count, "one seat");
        AssertEqual(4, alloc.Seats[0].InstanceId, "an AI formation holds it");
        foreach (int id in new[] { 1, 2, 3 })
        {
            alloc.TryGet(id, out var a);
            AssertTrue(a.Reason != ForceAllocator.ReasonGarrisonPost && !a.Withheld, "formation " + id + " is not posted");
            AssertTrue(a.Role == FormationRole.Hold, "and keeps holding where it stands");
        }
        var carrying = Side();
        carrying.Objectives.Add(Town(40, 500f));
        carrying.Beliefs.Add(Seen(560f, 30000f));
        var carried = Corps(5, 430f, 8000f);
        carried.Chain = WlChainClass.CarryingGroup;
        carrying.Own.Add(carried);
        AssertEqual(5, Allocate(carrying, HandPlan(carrying)).Seats[0].InstanceId, "a group carrying the player below map level is an ordinary asset and may be posted");
    }

    private static void GarrisonWithheldFromVanillaOperations()
    {
        CampaignScope.ResetAll();
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Objectives.Add(Town(41, 800f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Beliefs.Add(Seen(860f, 30000f));
        s.Own.Add(Corps(1, 503f, 6000f));                                // at the first post, its incumbent
        s.Own.Add(Corps(2, 700f, 6000f));                                // away from the second
        var plan = HandPlan(s);
        plan.DaySerial = ExecutorFixtures.Day;
        var alloc = Allocate(s, plan, new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") });
        alloc.TryGet(1, out var holding);
        alloc.TryGet(2, out var marching);
        AssertEqual(FormationRole.Hold, holding.Role, "corps 1 holds at its post");
        AssertEqual(FormationRole.Garrison, marching.Role, "corps 2 marches to its");
        PlanFreshness.NoteToday(ExecutorFixtures.Day);
        CampaignPlanStore.Publish(0, plan, alloc, ExecutorFixtures.Sectors(s));
        CampaignPlanStore.PublishExecutorContext(0, new ExecutorContext { Released = true, ExecutorsAllowed = true });
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, 1), "the garrison at its post is out of vanilla's offensive pool");
        AssertTrue(CampaignWithholdView.WithheldFromOffense(0, 2), "so is the one marching to it");
        AssertTrue(CampaignWithholdView.WithheldFromDefense(0, 2, 0f, 0f), "the marching one is out of the defensive pool too");
        AssertTrue(CampaignWithholdView.WithheldFromDefense(0, 1, 900f, 0f), "and the one at its post, while the enemy vanilla asks about stands in another sector");
        CampaignScope.ResetAll();
    }

    private static void GarrisonPostsRoundTripInSidecar()
    {
        var p = new PlannerSnapshot();
        p.GarrisonPosts.Add(new GarrisonPostRecord { Kind = GarrisonPostKind.Port, X = 701.5f, Z = -12.25f, AssignedUnitKeys = new[] { "1st Corps:7", "2nd Corps:8" }, QuietSinceDay = 812 });
        p.GarrisonPosts.Add(new GarrisonPostRecord { Kind = GarrisonPostKind.Secession, X = 300f, Z = 4f, AssignedUnitKeys = new string[0], QuietSinceDay = -1 });
        var snap = CampaignLifecycleFixTests.SampleSnapshot();
        snap.Planner[0] = p;
        string json = SidecarDtoCodec.Serialize(SidecarDtoCodec.Encode(snap));
        AssertTrue(SidecarDtoCodec.TryDeserialize(json, out var dto, out var reason), "deserialize: " + reason);
        AssertTrue(SidecarDtoCodec.TryDecode(dto, out var back, out reason), "decode: " + reason);
        var posts = back.Planner[0].GarrisonPosts;
        AssertEqual(2, posts.Count, "both records survive");
        AssertEqual(GarrisonPostKind.Port, posts[0].Kind, "kind");
        AssertNear(701.5f, posts[0].X, 0.001f, "x");
        AssertNear(-12.25f, posts[0].Z, 0.001f, "z");
        AssertEqual("1st Corps:7,2nd Corps:8", string.Join(",", posts[0].AssignedUnitKeys), "every member is its unit key, never an instance id, in seat order");
        AssertEqual(812, posts[0].QuietSinceDay, "the quiet began on that day");
        AssertEqual(GarrisonPostKind.Secession, posts[1].Kind, "the secession post is in the same ledger");
        AssertEqual(0, posts[1].AssignedUnitKeys.Length, "an unmanned post keeps no key");
        AssertEqual(-1, posts[1].QuietSinceDay, "and was not quiet");
        AssertFalse(json.Contains("InstanceId"), "no Unity instance id reaches the file");

        const string old = "{\"version\":3,\"factions\":[{\"factionId\":0,\"currentEra\":\"Operational1862\",\"planner\":{\"decisionSerial\":2}}]}";
        AssertTrue(SidecarDtoCodec.TryDeserialize(old, out var oldDto, out reason), "an older v3 file without garrisonPosts: " + reason);
        AssertTrue(SidecarDtoCodec.TryDecode(oldDto, out var oldBack, out reason), "decodes: " + reason);
        AssertEqual(0, oldBack.Planner[0].GarrisonPosts.Count, "to no posts");
        const string oneKey = "{\"version\":3,\"factions\":[{\"factionId\":0,\"currentEra\":\"Operational1862\",\"planner\":{\"garrisonPosts\":[{\"kind\":\"Fort\",\"x\":5,\"z\":6,\"unitKey\":\"1st Corps:7\",\"quietSinceDay\":-1}]}}]}";
        AssertTrue(SidecarDtoCodec.TryDeserialize(oneKey, out var oneDto, out reason), "a file written with one unitKey: " + reason);
        AssertTrue(SidecarDtoCodec.TryDecode(oneDto, out var oneBack, out reason), "decodes: " + reason);
        AssertEqual("1st Corps:7", string.Join(",", oneBack.Planner[0].GarrisonPosts[0].AssignedUnitKeys), "an older file's one key loads as one member");
        AssertTrue(json.Contains("\"unitKeys\""), "the new file carries every member");

        // The ledger takes the records back and plans the incumbent by key, or by name when the commander has changed.
        var ledger = new GarrisonLedger();
        ledger.Restore(posts);
        AssertEqual(2, ledger.Posts.Count, "restored");
        var s = Side();
        s.Objectives.Add(Town(40, 701.5f));
        s.Beliefs.Add(Seen(740f, 30000f));
        var corps = Corps(7, 701.5f, 6000f);
        corps.UnitKey = "1st Corps:99";
        corps.Name = "1st Corps";
        s.Own.Add(corps);
        var planned = Posts(s, null, ledger.Posts);
        AssertEqual(1, planned.Count, "the town is a post again");
        AssertEqual(7, planned[0].InstanceIds[0], "its incumbent is found by name after a commander change");
    }

    private static void AlliancesTwoToFourIgnored()
    {
        foreach (int alliance in new[] { 2, 3, 4, -1 })
        {
            var s = Side(alliance);
            s.Objectives.Add(Town(40, 500f, holder: alliance));
            s.Beliefs.Add(Seen(560f, 30000f));
            s.Own.Add(Corps(1, 430f, 6000f));
            AssertEqual(0, Posts(s).Count, "no post for alliance " + alliance);
        }
        AssertTrue(GarrisonLedgers.For(2) == null && GarrisonLedgers.For(-1) == null, "and no ledger");
        AssertTrue(GarrisonLedgers.For(0) != null && GarrisonLedgers.For(1) != null, "alliances 0 and 1 have one");
    }

    // ---- D8 and G8 share one mechanism ----

    private const int KentuckyState = 14;

    // The Union's snapshot with a live secession state (Louisville at x 300), a second own town at x 700 under an army, and enough corps.
    private static CampaignSnapshot UnionWithBoth()
    {
        var s = Side();
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 15, StateId = KentuckyState, X = 300f, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 0.3f });
        s.Secessions.Add(new SecessionRecord
        {
            ObjectiveId = 15, StateId = KentuckyState, StateAlliance = 0, Live = SecessionRules.IsLive(0, 0.6f), HasTown = true, TownX = 300f, TownZ = 0f
        });
        s.Objectives.Add(Town(41, 700f));
        s.Own.Add(Corps(2, 240f, 8000f));                                // nearest Louisville, outside Kentucky: the D8 garrison
        s.Own.Add(Corps(6, 600f, 7000f));                                // the G8 post's garrison
        s.Own.Add(Corps(9, 100f, 9000f));
        return s;
    }

    private static TheaterPlan SecessionPlan(params FormationRecord[] extra)
    {
        // The secession post as TheaterPlanner makes it, on the same formations with nothing believed anywhere (so no sector holds a reserve).
        var calm = UnionWithBoth();
        calm.Own.AddRange(extra);
        return TheaterPlanner.Plan(calm, ExecutorFixtures.Sectors(calm), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
    }

    private static void SecessionAndPostsShareOneRoleOneWithholdOneLedger()
    {
        var s = UnionWithBoth();
        s.Beliefs.Add(Seen(740f, 30000f));
        var plan = SecessionPlan();
        AssertEqual(1, plan.Garrisons.Count, "D8 posted Louisville");
        var alloc = Allocate(s, plan);
        AssertEqual(2, plan.Garrisons.Count, "G8 added the ordinary post");
        AssertEqual(GarrisonPostKind.Secession, plan.Garrisons[0].Kind, "the secession post first");
        AssertEqual(GarrisonPostKind.ObjectiveTown, plan.Garrisons[1].Kind, "then the town");
        alloc.TryGet(2, out var secession);
        alloc.TryGet(6, out var town);
        AssertEqual(FormationRole.Garrison, secession.Role, "D8's member marches under the Garrison role");
        AssertEqual(FormationRole.Garrison, town.Role, "G8's member marches under the same role");
        AssertEqual(ForceAllocator.ReasonSecession, secession.Reason, "each states its own reason");
        AssertEqual(ForceAllocator.ReasonGarrisonPost, town.Reason, "G8's is its own string");
        AssertTrue(secession.Withheld && town.Withheld, "both are withheld through the one Assignment.Withheld flag");
        AssertNear(300f, secession.TargetX, 0.01f, "Louisville");
        AssertNear(700f, town.TargetX, 0.01f, "the town");

        var ledger = new GarrisonLedger();
        var result = ledger.Sync(plan.Garrisons, alloc, s);
        AssertEqual(2, result.Posts, "one ledger holds both posts");
        AssertEqual(2, result.Manned, "both manned");
        AssertEqual(2, ledger.Posts.Count, "two records");
        AssertEqual(GarrisonPostKind.Secession, ledger.Posts[0].Kind, "the secession one");
        AssertEqual("corps2:2", string.Join(",", ledger.Posts[0].AssignedUnitKeys), "named by the formation's unit key");
        AssertEqual("corps6:6", string.Join(",", ledger.Posts[1].AssignedUnitKeys), "likewise");
    }

    // D8's in-state member is seated at its post too (the ledger reads it as Holding), and holds in place under the one role.
    private static void ASecessionMemberInTheStateIsSeatedAtItsPostAndHolds()
    {
        var s = UnionWithBoth();
        var inside = Corps(3, 280f, 6000f);
        inside.StateId = KentuckyState;
        s.Own.Add(inside);
        var plan = SecessionPlan(inside);
        var alloc = Allocate(s, plan);
        alloc.TryGet(3, out var member);
        AssertEqual(FormationRole.Hold, member.Role, "inside the state it holds in place");
        AssertEqual(ForceAllocator.ReasonSecession, member.Reason, "for the secession");
        AssertTrue(member.Withheld, "withheld");
        var seat = alloc.Seats.First(x => x.InstanceId == 3);
        AssertTrue(seat.AtPost, "and the seat says it is there");
        AssertEqual(0, seat.PostIndex, "at the secession post");
        var ledger = new GarrisonLedger();
        ledger.Sync(plan.Garrisons, alloc, s);
        AssertEqual(GarrisonPostState.Holding, ledger.Transitions.First(t => t.Kind == GarrisonPostKind.Secession).State, "the one ledger calls it Holding");
    }

    private static void ASecessionTownIsNeverAlsoAGarrisonPost()
    {
        var s = UnionWithBoth();
        s.Beliefs.Add(Seen(340f, 30000f));                               // an army by Louisville
        var plan = SecessionPlan();
        var posts = Posts(s, plan);
        AssertTrue(PostAt(posts, 300f) == null, "Louisville is D8's post already: G8 adds none at its site");
        AssertEqual(1, plan.Garrisons.Count, "and the secession post is still the plan's");
    }

    private static void AFormationHoldsOnePostAtMostAndPostsNeverShareIt()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 400f));
        s.Objectives.Add(Town(41, 520f));                                // 120 apart: two posts, one threat
        s.Beliefs.Add(Seen(460f, 30000f));
        s.Own.Add(Corps(1, 450f, 6000f));                                // the only corps near either
        s.Own.Add(Corps(2, 600f, 9000f));
        var alloc = Allocate(s, HandPlan(s));
        AssertEqual(alloc.Seats.Select(x => x.InstanceId).Distinct().Count(), alloc.Seats.Count, "no formation is seated twice: " + string.Join(",", alloc.Seats.Select(x => x.InstanceId)));
        AssertEqual(alloc.Assignments.Count, alloc.Assignments.Select(a => a.InstanceId).Distinct().Count(), "and each has the one assignment");
        AssertEqual(2, alloc.Seats.Count, "two posts, two formations");
    }

    private static void TheIncumbentKeepsItsPostAndIsNotItsOwnExistingStrength()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(3, 500f, 6000f));                                // standing at the post: the incumbent
        s.Own.Add(Corps(4, 505f, 1500f));                                // a smaller corps that would be the pick
        var retained = new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps3:3") };
        var post = Posts(s, null, retained)[0];
        AssertNear(1500f, post.ExistingStrength, 0.01f, "only the other corps at the post is existing strength, not the incumbent");
        AssertNear(GarrisonPlanner.ThreatShare * Weighted(30000f, 60f) - 1500f, post.Need, 1f, "so the need is not met by the garrison itself");
        AssertEqual(3, post.InstanceIds[0], "the post names its incumbent");
        var alloc = Allocate(s, HandPlan(s), retained);
        AssertEqual(1, alloc.Seats.Count, "one seat");
        AssertEqual(3, alloc.Seats[0].InstanceId, "the incumbent keeps it rather than the smaller corps");
        AssertTrue(alloc.Seats[0].AtPost, "where it stands");
    }

    // A need no single formation covers is met by the nearest two together (D8's flag-alone rule), as far as they go: the shortfall is
    // recorded, never taken from the mass, and a post nobody can reach stays uncovered.
    private static void ANeedNoFormationCoversIsRecordedUncovered()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 600000f));                              // a need far above any corps
        s.Own.Add(Corps(1, 450f, 9000f));
        s.Own.Add(Corps(2, 440f, 9000f));
        s.Own.Add(Corps(3, 430f, 9000f));
        var plan = HandPlan(s);
        var alloc = Allocate(s, plan);
        float need = plan.Garrisons[0].Need;
        AssertTrue(need > 18000f, "the need " + need + " is above any two corps");
        AssertEqual(GarrisonPlanner.MaxFormationsPerPost, alloc.Seats.Count, "the nearest two are posted, no more");
        AssertTrue(alloc.Seats.All(x => x.InstanceId == 1 || x.InstanceId == 2), "corps 1 and 2 are nearest: " + string.Join(",", alloc.Seats.Select(x => x.InstanceId)));
        alloc.TryGet(3, out var third);
        AssertTrue(third.Reason != ForceAllocator.ReasonGarrisonPost, "the third is kept for no post");
        var ledger = new GarrisonLedger();
        var result = ledger.Sync(plan.Garrisons, alloc, s);
        AssertEqual(1, result.Manned, "the post is manned");
        AssertEqual(1, result.Short, "and short");
        AssertEqual(0, result.Uncovered, "so it is not uncovered");
        AssertNear(need - 18000f, ledger.Transitions[0].Short, 1f, "by what the two fall short of");

        var nobody = Side();
        nobody.Objectives.Add(Town(40, 500f));
        nobody.Beliefs.Add(Seen(560f, 600000f));
        nobody.Own.Add(Corps(1, 800f, 9000f));                           // beyond the relocation range
        var nobodyPlan = HandPlan(nobody);
        var nobodyAlloc = Allocate(nobody, nobodyPlan);
        var unreached = new GarrisonLedger().Sync(nobodyPlan.Garrisons, nobodyAlloc, nobody);
        AssertEqual(1, unreached.Uncovered, "a post no formation can reach is uncovered");
        AssertEqual(0, unreached.Manned, "and not manned");
    }

    // The verifier's case: a 60000 army fifteen from the town makes a need of about 6657; two 5500 corps nearby cover it only together.
    private static void TwoCorpsTogetherManAPostNoSingleCorpsCovers()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(515f, 60000f));
        s.Own.Add(Corps(1, 450f, 5500f));
        s.Own.Add(Corps(2, 460f, 5500f));
        s.Own.Add(Corps(3, 440f, 5500f));                                // a third, farther: not needed
        var plan = HandPlan(s);
        var alloc = Allocate(s, plan);
        float need = plan.Garrisons[0].Need;
        AssertNear(6657f, need, 3f, "the verifier's need");
        AssertEqual(2, alloc.Seats.Count, "two corps together");
        AssertEqual("1,2", string.Join(",", alloc.Seats.Select(x => x.InstanceId).OrderBy(x => x)), "the nearest two");
        AssertEqual(0, alloc.Seats.Select(x => x.PostIndex).Distinct().First(), "at the one post");
        foreach (int id in new[] { 1, 2 })
        {
            alloc.TryGet(id, out var a);
            AssertEqual(FormationRole.Garrison, a.Role, "corps " + id + " marches to the post");
            AssertTrue(a.Withheld, "withheld");
            AssertEqual(ForceAllocator.ReasonGarrisonPost, a.Reason, "for the post");
        }
        var ledger = new GarrisonLedger();
        var result = ledger.Sync(plan.Garrisons, alloc, s);
        AssertEqual(0, result.Short, "11000 meets 6657: no shortfall");
        AssertEqual("corps1+corps2", ledger.Transitions[0].Unit.Split('+').OrderBy(x => x).Aggregate((a, b) => a + "+" + b), "the ledger names both");

        // A single corps that covers is preferred to two that together would.
        var single = Side();
        single.Objectives.Add(Town(40, 500f));
        single.Beliefs.Add(Seen(515f, 60000f));
        single.Own.Add(Corps(1, 450f, 5500f));
        single.Own.Add(Corps(2, 460f, 5500f));
        single.Own.Add(Corps(4, 300f, 7000f));                           // 200 away: beyond range, so still two
        AssertEqual(2, Allocate(single, HandPlan(single)).Seats.Count, "a covering corps out of range does not count");
        single.Own[2] = Corps(4, 380f, 7000f);                           // in range
        var covered = Allocate(single, HandPlan(single));
        AssertEqual(1, covered.Seats.Count, "one corps that covers is posted alone");
        AssertEqual(4, covered.Seats[0].InstanceId, "the 7000");

        // The incumbent counts as one of the two: it keeps the post and one more is added for the rest.
        var held = Side();
        held.Objectives.Add(Town(40, 500f));
        held.Beliefs.Add(Seen(515f, 60000f));
        held.Own.Add(Corps(1, 503f, 5500f));
        held.Own.Add(Corps(2, 460f, 5500f));
        held.Own.Add(Corps(3, 440f, 5500f));
        var heldAlloc = Allocate(held, HandPlan(held), new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") });
        AssertEqual(2, heldAlloc.Seats.Count, "the incumbent and one more");
        AssertEqual(1, heldAlloc.Seats[0].InstanceId, "the incumbent first");
        AssertEqual(2, heldAlloc.Seats[1].InstanceId, "then the nearest");
    }

    // The mass is never a source for the second formation either: only the formations outside it are combined.
    private static void ACombinedPostNeverTakesTheMass()
    {
        var s = ExecutorFixtures.Wide();
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.3f;
        s.Objectives.Add(Town(40, 400f));
        s.Beliefs.Add(Seen(100f, 18000f));
        s.Beliefs.Add(Seen(415f, 60000f));
        s.Own.Add(Corps(1, 200f, 20000f));
        s.Own.Add(Corps(2, 395f, 9000f));                                // the mass, nearest the post
        s.Own.Add(Corps(3, 380f, 5500f));
        s.Own.Add(Corps(4, 370f, 5500f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2 });
        var alloc = Allocate(s, plan);
        AssertEqual("3,4", string.Join(",", alloc.Seats.Select(x => x.InstanceId).OrderBy(x => x)), "the two free corps, not the mass member beside the post");
        alloc.TryGet(2, out var mass);
        AssertEqual(FormationRole.MainEffort, mass.Role, "the mass is untouched");
    }

    private static void AFormationAtThePostHoldsInPlaceAndOneAwayMarches()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(1, 506f, 6000f));                                // within the arrival radius: the post's incumbent
        var near = Allocate(s, HandPlan(s), new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") });
        near.TryGet(1, out var holding);
        AssertEqual(FormationRole.Hold, holding.Role, "at the post it holds in place");
        AssertEqual(ForceAllocator.ReasonGarrisonPost, holding.Reason, "for the post");
        AssertTrue(holding.Withheld, "withheld");
        AssertNear(506f, holding.TargetX, 0.01f, "where it stands (steering centres its theater box on a Hold formation)");
        AssertTrue(near.Seats[0].AtPost, "the seat says it is there");

        // Not its incumbent, a formation standing at the post is existing strength: it covers the post and none is made.
        var standing = Side();
        standing.Objectives.Add(Town(40, 500f));
        standing.Beliefs.Add(Seen(560f, 30000f));
        standing.Own.Add(Corps(1, 506f, 6000f));
        AssertEqual(0, Posts(standing).Count, "the formation already there is the garrison (and is vanilla's to move until a post names it)");

        var away = Side();
        away.Objectives.Add(Town(40, 500f));
        away.Beliefs.Add(Seen(560f, 30000f));
        away.Own.Add(Corps(1, 430f, 6000f));
        var marching = Allocate(away, HandPlan(away));
        marching.TryGet(1, out var walks);
        AssertEqual(FormationRole.Garrison, walks.Role, "away from it, it marches");
        AssertNear(500f, walks.TargetX, 0.01f, "to the post");
        AssertFalse(marching.Seats[0].AtPost, "not there yet");
    }

    private static void AReserveSurplusMayBePostedButTheMassNever()
    {
        // A mass far above the required force leaves a Reserve surplus; that surplus is the Hold / EconomyOfForce / Reserve pool the plan names.
        var s = ExecutorFixtures.Wide();
        s.MinStrengthForMovement = 5000f;
        s.MinMoraleForMovement = 0.3f;
        s.Objectives.Add(Town(40, 400f));
        s.Beliefs.Add(Seen(100f, 8000f));
        s.Beliefs.Add(Seen(440f, 30000f));
        s.Own.Add(Corps(1, 150f, 20000f));
        s.Own.Add(Corps(2, 390f, 9000f));
        s.Own.Add(Corps(3, 395f, 9000f));
        var plan = ExecutorFixtures.HandPlan(s, new[] { 1, 2, 3 });
        var alloc = Allocate(s, plan);
        var roles = alloc.Assignments.ToDictionary(a => a.InstanceId, a => a);
        AssertEqual(FormationRole.MainEffort, roles[1].Role, "the mass keeps its main body");
        AssertEqual(1, alloc.Seats.Count, "the surplus the reserve split set aside holds the post");
        int posted = alloc.Seats[0].InstanceId;
        AssertTrue(posted == 2 || posted == 3, "a former reserve, not the main body: " + posted);
        AssertEqual(ForceAllocator.ReasonGarrisonPost, roles[posted].Reason, "it carries the post's reason");
    }

    private static void AFormationTheTrackerGaveUpOnIsNotSeated()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(1, 430f, 6000f));
        s.Own.Add(Corps(2, 380f, 9000f));
        var gaveUp = new[] { new GaveUpMove { InstanceId = 1, TargetX = 500f, TargetZ = 0f, ObjectiveId = -1 } };
        var alloc = Allocate(s, HandPlan(s), null, gaveUp);
        AssertEqual(1, alloc.Seats.Count, "one seat");
        AssertEqual(2, alloc.Seats[0].InstanceId, "the next candidate takes the post");
        alloc.TryGet(1, out var gave);
        AssertTrue(gave.Reason != ForceAllocator.ReasonGarrisonPost && gave.Role == FormationRole.Hold, "the formation that gave up on the post is not kept for it");
    }

    // The hold reserve takes the formations nearest the believed army, which is where a post's garrison stands. Without the reclaim the post
    // would lose its holder to the hold reserve and be re-filled from elsewhere day after day.
    private static void TheSectorsHoldReserveDoesNotTakeAPostsIncumbentFromIt()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(1, 503f, 6000f));                                // the incumbent, nearest the army
        s.Own.Add(Corps(2, 450f, 6000f));
        s.Own.Add(Corps(3, 350f, 6000f));                                // a free corps that could have been asked instead
        var plan = HandPlan(s);
        var sectors = ExecutorFixtures.Sectors(s);
        plan.Sectors.Add(new SectorPlan { SectorId = sectors.SectorAt(500f, 0f), Posture = TheaterPosture.Hold, HoldNeed = 9000f });
        var retained = new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") };
        var alloc = Allocate(s, plan, retained);
        alloc.TryGet(2, out var other);
        AssertEqual(ForceAllocator.ReasonHoldReserve, other.Reason, "the sector's hold reserve took the next corps");
        AssertEqual(1, alloc.Seats.Count, "one seat");
        AssertEqual(1, alloc.Seats[0].InstanceId, "the incumbent keeps the post although the hold reserve claimed it first");
        alloc.TryGet(1, out var holder);
        AssertEqual(FormationRole.Hold, holder.Role, "as a Hold formation of the same sector");
        AssertEqual(ForceAllocator.ReasonGarrisonPost, holder.Reason, "for the post");

        // Another sector's hold reserve is not reclaimed: the sector would be left short.
        var elsewhere = Side();
        elsewhere.Objectives.Add(Town(40, 500f));
        elsewhere.Objectives.Add(Town(41, 200f));
        elsewhere.Beliefs.Add(Seen(560f, 30000f));
        elsewhere.Beliefs.Add(Seen(160f, 30000f));
        elsewhere.Own.Add(Corps(1, 220f, 6000f));                        // the incumbent now stands in the western sector
        var plan2 = HandPlan(elsewhere);
        var sectors2 = ExecutorFixtures.Sectors(elsewhere);
        plan2.Sectors.Add(new SectorPlan { SectorId = sectors2.SectorAt(200f, 0f), Posture = TheaterPosture.Hold, HoldNeed = 9000f });
        var alloc2 = Allocate(elsewhere, plan2, new[] { Record(GarrisonPostKind.ObjectiveTown, 500f, "corps1:1") });
        alloc2.TryGet(1, out var reserved);
        AssertEqual(ForceAllocator.ReasonHoldReserve, reserved.Reason, "a hold reserve of another sector stays where it is");
    }

    private static void TheSecessionRowIgnoresTheOrdinaryPosts()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(1, 430f, 6000f));
        var plan = HandPlan(s);
        var alloc = Allocate(s, plan);
        string row = ForceAllocator.Summary(alloc, plan, PlayerCommandClass.BelowMapLevel, 1f, 0);
        AssertContains(row, " garrison=none ", "the secession field names only secession posts");
        AssertEqual("none", ForceAllocator.GarrisonText(plan), "and so does its text");
    }

    private static void TheLedgerRecordsMovingHoldingUncoveredAndReleased()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Objectives.Add(Town(41, 800f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Beliefs.Add(Seen(860f, 600000f));
        s.Own.Add(Corps(1, 430f, 6000f));
        var plan = HandPlan(s);
        var alloc = Allocate(s, plan);
        var ledger = new GarrisonLedger();
        var first = ledger.Sync(plan.Garrisons, alloc, s);
        AssertEqual(2, first.Posts, "two posts");
        AssertEqual(1, first.Manned, "one manned");
        AssertEqual(1, first.Uncovered, "one with no formation to cover it");
        var moving = ledger.Transitions.First(t => t.State == GarrisonPostState.Moving);
        AssertEqual("corps1", moving.Unit, "the formation");
        AssertEqual(GarrisonPostKind.ObjectiveTown, moving.Kind, "the kind");
        AssertEqual(GarrisonPostState.Uncovered, ledger.Transitions.First(t => t.Kind == GarrisonPostKind.ObjectiveTown && t.Unit == "none").State, "the other is uncovered");
        var again = ledger.Sync(plan.Garrisons, alloc, s);
        AssertEqual(0, ledger.Transitions.Count, "nothing changed, no transition: " + again.Posts);

        // The corps arrives.
        s.Own[0] = Corps(1, 500f, 6000f);
        var alloc2 = Allocate(s, HandPlan(s), ledger.Posts);
        var plan2 = HandPlan(s);
        plan2.Garrisons.AddRange(Posts(s, plan2, ledger.Posts));
        var alloc3 = ForceAllocator.Allocate(plan2, s, ExecutorFixtures.Sectors(s), null, null, null);
        ledger.Sync(plan2.Garrisons, alloc3, s);
        AssertEqual(GarrisonPostState.Holding, ledger.Transitions.First(t => t.Unit == "corps1").State, "arrived: holding");
        AssertTrue(alloc2.Seats.Count >= 1, "seated");

        // The army goes away and stays away: the post is released and the transition says so.
        var gone = Side();
        gone.Objectives.Add(Town(40, 500f));
        gone.Own.Add(Corps(1, 500f, 6000f));
        ledger.Sync(Array.Empty<GarrisonPost>(), new Allocation(), gone);
        AssertEqual(GarrisonPostState.Released, ledger.Transitions.First(t => t.State == GarrisonPostState.Released).State, "released");
        AssertEqual(0, ledger.Posts.Count, "and gone from the ledger");
    }

    private static void TheGarrisonRowsCarryTheSpecifiedFields()
    {
        var result = new GarrisonSyncResult { Posts = 3, Manned = 2, Released = 1, Uncovered = 1, Short = 1 };
        AssertEqual("[Garrison] alliance=0 posts=3 manned=2 released=1 uncovered=1 short=1", GarrisonRows.Summary(0, result), "the summary row");
        var t = new GarrisonTransition { Name = "Port@700,0", Kind = GarrisonPostKind.Port, Unit = "corps6", State = GarrisonPostState.Holding, Need = 2196.4f, Existing = 600.2f, Short = 12.6f };
        AssertEqual("[GarrisonPost] alliance=1 post=Port@700,0 kind=Port unit=corps6 state=Holding need=2196 existing=600 short=13", GarrisonRows.Transition(1, t), "the transition row");
        var post = new GarrisonPost { Kind = GarrisonPostKind.Fort, X = 500.4f, Z = 3.6f };
        AssertEqual("Fort@500,4", GarrisonLedger.NameOf(post), "an ordinary post is named by kind and place");
        AssertEqual("Kentucky", GarrisonLedger.NameOf(new GarrisonPost { Kind = GarrisonPostKind.Secession, StateName = "Kentucky" }), "a secession post by its state");
    }

    private static void PostsAreAMoverRoleAndTheMoverMarchesThem()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(1, 430f, 6000f));
        var plan = HandPlan(s);
        var alloc = Allocate(s, plan);
        var host = new FakeMoverHost { Own = 0 };
        var tracker = new MoveTracker();
        CampaignMover.Review(ExecutorFixtures.Inputs(s, plan, alloc, ExecutorFixtures.OpenTempo()), host, tracker, ExecutorFixtures.State());
        AssertEqual(1, host.Moves.Count, "one move issued");
        AssertEqual(1, host.Moves[0].id, "the garrison corps");
        AssertNear(500f, host.Moves[0].x, 0.01f, "to the post");

        // At its post nothing is sent: the formation holds, and steering centres its box on it.
        var atPost = Side();
        atPost.Objectives.Add(Town(40, 500f));
        atPost.Beliefs.Add(Seen(560f, 30000f));
        atPost.Own.Add(Corps(1, 503f, 6000f));
        var plan2 = HandPlan(atPost);
        var alloc2 = Allocate(atPost, plan2);
        var host2 = new FakeMoverHost { Own = 0 };
        CampaignMover.Review(ExecutorFixtures.Inputs(atPost, plan2, alloc2, ExecutorFixtures.OpenTempo()), host2, new MoveTracker(), ExecutorFixtures.State());
        AssertEqual(0, host2.Moves.Count, "no move for a formation already there");
        atPost.TheaterHalfExtentX = 100f;
        atPost.TheaterHalfExtentZ = 100f;
        var writes = new List<TheaterWrite>();
        SteeringRules.PlanTheaterWrites(atPost, alloc2, ExecutorFixtures.Sectors(atPost), new TheaterWriteLimiter(), writes);
        AssertEqual(1, writes.Count, "the steering executor writes the box");
        AssertNear(503f, writes[0].X, 0.01f, "centred on the formation at its post");
    }

    private static void AllocationWithPostsIsDeterministicAndLeavesThePlanAlone()
    {
        var s = Side();
        s.Objectives.Add(Town(40, 500f));
        s.Beliefs.Add(Seen(560f, 30000f));
        s.Own.Add(Corps(1, 430f, 6000f));
        s.Own.Add(Corps(2, 440f, 6000f));
        var plan = HandPlan(s);
        plan.Garrisons.AddRange(Posts(s, plan));
        string before = string.Join("|", plan.Garrisons.Select(g => g.Kind + ":" + string.Join(",", g.InstanceIds) + ":" + g.Need));
        var sectors = ExecutorFixtures.Sectors(s);
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        var b = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        AssertEqual(AllocationSignature(a), AllocationSignature(b), "the same plan gives the same allocation twice");
        AssertEqual(a.Seats.Count, b.Seats.Count, "and the same seats");
        AssertEqual(before, string.Join("|", plan.Garrisons.Select(g => g.Kind + ":" + string.Join(",", g.InstanceIds) + ":" + g.Need)), "the allocation does not write into the plan");
    }

    // The whole chain on a real plan: TheaterPlanner (D8 and the mass), PlanPosts, Allocate. Enough corps that nothing is short.
    private static void ARealPlanThenPostsThenAllocationSeatsDistinctFormations()
    {
        var s = UnionWithBoth();
        s.Beliefs.Add(Seen(740f, 30000f));
        for (int i = 0; i < 4; i++) s.Own.Add(Corps(20 + i, 560f + 20f * i, 7000f));
        var plan = TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, new PlannerState(), null, null, ExecutorFixtures.Speed(), out _);
        var alloc = Allocate(s, plan);
        AssertTrue(plan.Garrisons.Count >= 2, "the secession post and at least one ordinary post: " + plan.Garrisons.Count);
        var seated = alloc.Seats.Select(x => x.InstanceId).ToList();
        AssertEqual(seated.Distinct().Count(), seated.Count, "every seat is a different formation");
        foreach (var seat in alloc.Seats)
        {
            alloc.TryGet(seat.InstanceId, out var a);
            AssertTrue(a.Withheld, "a seated formation is withheld: " + seat.InstanceId);
            AssertTrue(a.Reason == ForceAllocator.ReasonSecession || a.Reason == ForceAllocator.ReasonGarrisonPost, "with a post's reason: " + a.Reason);
        }
        foreach (int id in plan.MassInstanceIds)
        {
            alloc.TryGet(id, out var m);
            AssertTrue(!seated.Contains(id) && m.Reason != ForceAllocator.ReasonGarrisonPost, "a mass member is never seated: " + id);
        }
    }

    // ---- fog and the snapshot ----

    private static void EveryGarrisonUnitReadFollowsTheSideCheck()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignSnapshotRuntime.cs");
        var reads = System.Text.RegularExpressions.Regex.Matches(src, @"\.garrisonunit\b");
        AssertEqual(1, reads.Count, "the snapshot runtime reads a fort's garrison unit in exactly one place");
        string body = BodyOf(src, "private static void ReadForts(");
        int check = body.IndexOf("SnapshotRules.SideKnowsGarrison(", StringComparison.Ordinal);
        int read = body.IndexOf(".garrisonunit", StringComparison.Ordinal);
        AssertTrue(check >= 0 && read >= 0, "that place is the fort scan, and it asks the side test");
        AssertTrue(check < read, "the side-of-fort test comes before the garrisonunit read");
        string guard = body.Substring(check, read - check);
        AssertTrue(guard.Contains("continue;"), "and a fort that fails it is skipped, not read");
        AssertEqual(1, System.Text.RegularExpressions.Regex.Matches(src, @"BattleUnits\.fort\b").Count, "and the fort list is walked only there");
        AssertTrue(body.Contains("fort.constructiontimer > 0f"), "a fort still under construction is neither a post site nor a garrison");
        AssertTrue(body.Contains("if (fort.Owner == alliance)") && body.IndexOf("s.Forts.Add(", StringComparison.Ordinal) > body.IndexOf("if (fort.Owner == alliance)", StringComparison.Ordinal),
            "only the side's own forts are post sites: an ally's garrison counts at its target but is not a place to man");
    }

    private static void OnlyOwnAndAlliedForts()
    {
        AssertTrue(SnapshotRules.SideKnowsGarrison(0, 0, false), "the side's own fort");
        AssertTrue(SnapshotRules.SideKnowsGarrison(0, 2, true), "an ally's");
        AssertFalse(SnapshotRules.SideKnowsGarrison(0, 1, false), "an enemy's: a G1 belief, never a snapshot read");
        AssertFalse(SnapshotRules.SideKnowsGarrison(0, 3, false), "a neutral's");
        AssertFalse(SnapshotRules.SideKnowsGarrison(0, -1, false), "an unowned fort");
    }

    private static void TheRuntimeReadsItsFlagAndGatesOnlyThePostsNotTheSecession()
    {
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/GarrisonRuntime.cs");
        AssertTrue(src.Contains("Plugin.EnableGarrisonManning"), "GarrisonRuntime reads Enable Garrison Manning");
        string plannerRuntime = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/CampaignPlannerRuntime.cs");
        AssertTrue(plannerRuntime.Contains("GarrisonRuntime.PlanPosts("), "the Plan job asks the runtime for the posts");
        AssertTrue(plannerRuntime.Contains("GarrisonRuntime.Sync("), "and syncs the ledger after the allocation");
        string allocator = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/ForceAllocator.cs");
        AssertFalse(allocator.Contains("Plugin."), "the allocator is pure: the flag gates the posts that enter the plan, not the allocator");
        AssertTrue(BodyOf(src, "internal static void PlanPosts(").Contains("EnableGarrisonManning"), "flag off: no ordinary post enters the plan, and D8's secession garrison is not behind it");
    }
}
