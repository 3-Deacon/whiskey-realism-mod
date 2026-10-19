using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F6b-2 (D11 requirements covering-size's need, covering-members, covering-post-choice, covering-hold and
// covering-lifecycle; decision 4): the covering force itself, over F6b-1's posts and axis shares. Its need, the stored verdict, the members
// the axis sectors' hold reserves give it, how the allocator counts and places them, the defence clause and the lifecycle. Plan:
// docs/superpowers/plans/2026-10-18-strategic-foresight.md F6b-2; cases TC-COV-* in docs/loop/foresight/test-plan.md. The fixture is
// CoveringForceTests.cs's FX-002A (its header names the assumptions); the formations, strengths and positions below are invented.
internal static partial class CoveringForceTests
{
    private static (string name, Action run)[] ForceCases()
    {
        return new (string name, Action run)[]
        {
            ("NeedIsCappedAtTheAxisShares", NeedIsCappedAtTheAxisShares),
            ("NeedNeverBelowTheLiveMinimum", NeedNeverBelowTheLiveMinimum),
            ("VerdictNeedsThreeReviews", VerdictNeedsThreeReviews),
            ("TheFirstReviewAndAChangedPostKeyTakeTheRawVerdict", TheFirstReviewAndAChangedPostKeyTakeTheRawVerdict),
            ("OnePostTakesTheRawVerdict", OnePostTakesTheRawVerdict),
            ("TheFlipReviewPublishesTheStoredPost", TheFlipReviewPublishesTheStoredPost),
            ("CoveringStateRoundTripsInTheSidecar", CoveringStateRoundTripsInTheSidecar),
            ("ACommanderChangeCarriesTheCoveringKey", ACommanderChangeCarriesTheCoveringKey),
            ("NoTownWithinRangeNoCoveringForce", NoTownWithinRangeNoCoveringForce),
            ("NoAxisSectorNoCoveringForce", NoAxisSectorNoCoveringForce),
            ("CicSideHasNoCoveringForce", CicSideHasNoCoveringForce),
            ("MembersAreTheAxisHoldReservesNearestThePost", MembersAreTheAxisHoldReservesNearestThePost),
            ("NoSectorGivesMoreThanItsAxisShare", NoSectorGivesMoreThanItsAxisShare),
            ("TheBodyCrossingTheCapIsTaken", TheBodyCrossingTheCapIsTaken),
            ("MembersOrderedByTheStoredVerdictsPost", MembersOrderedByTheStoredVerdictsPost),
            ("AxisSectorsOrderTheirReserveByThePostNotTheArmy", AxisSectorsOrderTheirReserveByThePostNotTheArmy),
            ("AHomeDepartmentFormationIsCloserToThePost", AHomeDepartmentFormationIsCloserToThePost),
            ("IncumbentsKeepTheirPlace", IncumbentsKeepTheirPlace),
            ("AnIncumbentCountsInItsRecordedSectorWhereverItStands", AnIncumbentCountsInItsRecordedSectorWhereverItStands),
            ("AnArrivedMemberIsNotCountedTwice", AnArrivedMemberIsNotCountedTwice),
            ("AnUnselectedIncumbentIsACandidateWhereItStands", AnUnselectedIncumbentIsACandidateWhereItStands),
            ("AnIncumbentOffTheAxisIsNotAMember", AnIncumbentOffTheAxisIsNotAMember),
            ("AGaveUpMemberIsDroppedTheNextReview", AGaveUpMemberIsDroppedTheNextReview),
            ("HeldBuildersAndGiveUpsNotInThePool", HeldBuildersAndGiveUpsNotInThePool),
            ("AReadinessDipKeepsTheIncumbency", AReadinessDipKeepsTheIncumbency),
            ("IncumbentsReturnAfterTheEnemyCapitalPushEnds", IncumbentsReturnAfterTheEnemyCapitalPushEnds),
            ("BusyFormationsAtThePostCount", BusyFormationsAtThePostCount),
            ("TheNonMassableAtThePostCountFirst", TheNonMassableAtThePostCountFirst),
            ("MapGroupPlayerNeverTheOnlyHolder", MapGroupPlayerNeverTheOnlyHolder),
            ("PlayerCommandNeverAMemberAndCountsNothing", PlayerCommandNeverAMemberAndCountsNothing),
            ("CarryingGroupMayBeAMember", CarryingGroupMayBeAMember),
            ("MembersCountInTheirOwnSectorsHold", MembersCountInTheirOwnSectorsHold),
            ("MemberAtPostHoldsWithheldElseRedeploys", MemberAtPostHoldsWithheldElseRedeploys),
            ("RedeployingMemberInThePostSectorCountsInG4", RedeployingMemberInThePostSectorCountsInG4),
            ("G4CountsAMemberInItsRecordedSector", G4CountsAMemberInItsRecordedSector),
            ("PrefixesCountAMemberInItsRoleSector", PrefixesCountAMemberInItsRoleSector),
            ("PlanGroupsGroupsACoveringMemberWhereItStands", PlanGroupsGroupsACoveringMemberWhereItStands),
            ("AMemberThatGaveUpAtThePostIsHoldGaveUpInG4", AMemberThatGaveUpAtThePostIsHoldGaveUpInG4),
            ("CoveringNeverStripsAnotherSectorsHold", CoveringNeverStripsAnotherSectorsHold),
            ("UnionSideCoversToo", UnionSideCoversToo),
            ("ForwardVerdictOnCountedStrength", ForwardVerdictOnCountedStrength),
            ("ThePlannersVerdictNeedsThreeReviewsAndTheFlipPublishesTheStoredPost", ThePlannersVerdictNeedsThreeReviewsAndTheFlipPublishesTheStoredPost),
            ("OnePostFollowsTheRawVerdictAtOnce", OnePostFollowsTheRawVerdictAtOnce),
            ("TheStateKeepsTheMembersWithTheirAreas", TheStateKeepsTheMembersWithTheirAreas),
            ("ARenumberedSectorKeepsTheIncumbentsSector", ARenumberedSectorKeepsTheIncumbentsSector),
            ("AStagedPushNearThePostIsNotCovering", AStagedPushNearThePostIsNotCovering),
            ("MassForExcludesMembersExceptAgainstTheEnemyCapital", MassForExcludesMembersExceptAgainstTheEnemyCapital),
            ("MassForTakesMembersForAnyEnemyCapitalTarget", MassForTakesMembersForAnyEnemyCapitalTarget),
            ("EnemyCapitalPushLeavesTheCoveringStateUntouched", EnemyCapitalPushLeavesTheCoveringStateUntouched),
            ("EnemyCapitalPushMakesTheLeftBehindOrdinaryHoldReserves", EnemyCapitalPushMakesTheLeftBehindOrdinaryHoldReserves),
            ("TheLeftBehindAreRewrittenAfterTheMass", TheLeftBehindAreRewrittenAfterTheMass),
            ("LostCapitalWritesNoneKey", LostCapitalWritesNoneKey),
            ("CoveringUnbindsOnLostCapitalNoPlaceFlagOffAndEnemyCapitalPush", CoveringUnbindsOnLostCapitalNoPlaceFlagOffAndEnemyCapitalPush),
            ("ScoutDrawDepotAndPostsNeverTakeAMember", ScoutDrawDepotAndPostsNeverTakeAMember),
            ("ACoveringMemberIsTakenOffVanillasOffensiveList", ACoveringMemberIsTakenOffVanillasOffensiveList),
            ("OffenceNeverTakesAMember", OffenceNeverTakesAMember),
            ("DefenceMayTakeAMemberWhenThreatNear", DefenceMayTakeAMemberWhenThreatNear),
            ("ThreatNearNeedsAnAdvanceOnOwnSoil", ThreatNearNeedsAnAdvanceOnOwnSoil),
            ("ThreatNearCountsMarks", ThreatNearCountsMarks),
            ("CoveringClauseBeforeTheSameSectorException", CoveringClauseBeforeTheSameSectorException),
            ("CoveringClauseReadsBeliefsNotVanillasArgument", CoveringClauseReadsBeliefsNotVanillasArgument),
            ("TheCoveringRowNamesThePostTheMembersAndTheShort", TheCoveringRowNamesThePostTheMembersAndTheShort),
            ("TheCoveringRowIsRegisteredAndWrittenOnChange", TheCoveringRowIsRegisteredAndWrittenOnChange),
            ("TheReservesRowCarriesTheCoveringForceAndTheHoldReserve", TheReservesRowCarriesTheCoveringForceAndTheHoldReserve),
            ("TheFlagIsBoundAndReadByTheCoordinator", TheFlagIsBoundAndReadByTheCoordinator),
        };
    }

    // ---- the need and the verdict ----

    // D11 requirement-covering-size: CoveringNeed = min(max(HoldRatio' x Estimate(enemy capital, R), the live minimum), the sum of the axis
    // sectors' shares): the threat out of the enemy capital, capped at what the axis sectors hold against on the axis.
    private static void NeedIsCappedAtTheAxisShares()
    {
        AssertNear(5000f, CoveringForce.Need(100000f, 0.6f, 2000f, 5000f), 0.01f, "a threat of 60000 men is capped at what the axis sectors give");
        AssertNear(25560f, CoveringForce.Need(42600f, 0.6f, 2000f, 80000f), 0.5f, "under the cap the need is the hold ratio times the estimate (D11's worked example: 0.6 x 42600)");
        AssertEqual(0f, CoveringForce.Need(42600f, 0.6f, 2000f, 0f), "no axis sector, no force");
    }

    private static void NeedNeverBelowTheLiveMinimum()
    {
        AssertNear(2000f, CoveringForce.Need(100f, 0.6f, 2000f, 9000f), 0.01f, "an empty neighbourhood still wants the smallest force vanilla moves at all");
        AssertNear(1500f, CoveringForce.Need(100f, 0.6f, 2000f, 1500f), 0.01f, "unless the axis sectors give less: the cap wins over the minimum");
        AssertNear(3000f, CoveringForce.Need(100f, 0.6f, 3000f, 9000f), 0.01f, "the minimum is the scenario's live value, not a constant");
    }

    private static PlannerState VerdictState(bool forward, int days, string key)
    {
        var state = new PlannerState();
        state.RestoreCovering(null, forward, days, true, key);
        return state;
    }

    // Applies one review's verdict to the state the way the plan writes it, and returns what that review published.
    private static bool Apply(PlannerState state, bool forwardMet, string key, bool onePost)
    {
        var step = CoveringForce.Verdict(forwardMet, key, onePost, state);
        state.CoveringForward = step.Forward;
        state.CoveringVerdictDays = step.Days;
        state.CoveringVerdictKnown = step.Known;
        state.CoveringPostKey = key;
        return step.Published;
    }

    // D11 requirement-covering-post-choice: the verdict changes only after SwitchDays consecutive reviews of the other verdict.
    private static void VerdictNeedsThreeReviews()
    {
        AssertEqual(3, TheaterPlanner.SwitchDays, "the approved hysteresis is D4's");
        var state = VerdictState(forward: true, days: 0, key: "A|B");
        AssertTrue(Apply(state, false, "A|B", false), "day 1 of the other verdict: the stored forward verdict still stands");
        AssertEqual(1, state.CoveringVerdictDays, "and the count is 1");
        AssertTrue(Apply(state, false, "A|B", false), "day 2: still forward");
        AssertTrue(state.CoveringForward && state.CoveringVerdictDays == 2, "count 2");
        AssertTrue(Apply(state, true, "A|B", false), "the raw verdict agrees with the stored one again: it resets the count");
        AssertEqual(0, state.CoveringVerdictDays, "reset to 0");
        Apply(state, false, "A|B", false);
        Apply(state, false, "A|B", false);
        AssertTrue(Apply(state, false, "A|B", false), "the third review of the other verdict is published as the old verdict (the new post applies from the next review)");
        AssertFalse(state.CoveringForward, "and the stored verdict is now rear");
        AssertEqual(0, state.CoveringVerdictDays, "with the count restarted");
        AssertFalse(Apply(state, false, "A|B", false), "the next review publishes the rear post");
    }

    private static void TheFirstReviewAndAChangedPostKeyTakeTheRawVerdict()
    {
        var fresh = new PlannerState();
        var first = CoveringForce.Verdict(false, "A|B", false, fresh);
        AssertFalse(first.Published, "the first review has no stored verdict: the raw one (rear) is published");
        AssertTrue(first.Known && !first.Forward && first.Days == 0, "and stored with a count of 0");
        var held = VerdictState(forward: true, days: 2, key: "A|B");
        var changed = CoveringForce.Verdict(false, "A|C", false, held);
        AssertFalse(changed.Published, "a town changed hands (the post key changed): the raw verdict takes over at once");
        AssertTrue(changed.Days == 0 && changed.Known, "and the count restarts at 0");
        var unbound = VerdictState(forward: true, days: 2, key: "none|none");
        AssertFalse(CoveringForce.Verdict(false, "A|B", false, unbound).Published, "a post that appears after an unbound day is a key change");
    }

    // D11: when both points give the same town, or only one point gives a town, there is one post and no hysteresis: Kind is the raw
    // verdict each review, CoveringVerdictDays stays 0 and CoveringForward is written raw.
    private static void OnePostTakesTheRawVerdict()
    {
        var state = VerdictState(forward: true, days: 0, key: "A|A");
        var step = CoveringForce.Verdict(false, "A|A", true, state);
        AssertFalse(step.Published, "the raw verdict is the kind, whatever was stored");
        AssertTrue(!step.Forward && step.Days == 0 && step.Known, "written raw, with no count");
        step = CoveringForce.Verdict(true, "A|A", true, VerdictState(forward: false, days: 2, key: "A|A"));
        AssertTrue(step.Published && step.Forward && step.Days == 0, "and back the next review, at once");
    }

    private static void TheFlipReviewPublishesTheStoredPost()
    {
        var state = VerdictState(forward: false, days: 2, key: "A|B");
        var step = CoveringForce.Verdict(true, "A|B", false, state);
        AssertFalse(step.Published, "the review that flips the verdict still publishes the stored (rear) post");
        AssertTrue(step.Forward && step.Days == 0, "the stored verdict is now forward");
    }

    // ---- the state ----

    // TC-COV-SIDECAR: the members (unit key and the aiarea they stood in), the stored verdict and its post key survive a save and load; a record
    // with no key or no area is skipped, and an older file reads no verdict.
    private static void CoveringStateRoundTripsInTheSidecar()
    {
        var planner = new PlannerSnapshot();
        planner.State.RestoreCovering(new[]
        {
            new KeyValuePair<string, int>("1st Corps:7", 2), new KeyValuePair<string, int>("2nd Corps:9", 3),
            new KeyValuePair<string, int>("", 4), new KeyValuePair<string, int>("3rd Corps:11", -1)
        }, forward: true, verdictDays: 2, verdictKnown: true, postKey: "Fredericksburg@1247,-803|Fredericksburg@1247,-803");
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(SidecarDtoCodec.PlannerToDto(planner));
        var back = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>(json)).State;
        AssertEqual(2, back.CoveringKeys.Count, "a key with no name or no area is not kept");
        AssertEqual(2, back.CoveringKeys["1st Corps:7"], "a member keeps the area it stood in, not a sector id");
        AssertEqual(3, back.CoveringKeys["2nd Corps:9"], "the second member");
        AssertTrue(back.CoveringForward && back.CoveringVerdictDays == 2 && back.CoveringVerdictKnown, "the stored verdict and its count");
        AssertEqual("Fredericksburg@1247,-803|Fredericksburg@1247,-803", back.CoveringPostKey, "and the post key it was stored for");
        var older = SidecarDtoCodec.PlannerFromDto(Newtonsoft.Json.JsonConvert.DeserializeObject<PlannerStateDto>("{\"currentMainObjective\":3}")).State;
        AssertTrue(older.CoveringKeys.Count == 0 && !older.CoveringVerdictKnown && older.CoveringPostKey == null, "an older file has no covering state");
    }

    // A commander change re-keys a formation (name:commander): its place in the force moves with it, in the appointment path and in the next
    // review's observation, as a department does.
    private static void ACommanderChangeCarriesTheCoveringKey()
    {
        var state = new PlannerState();
        state.Observe(7, "1st Corps:7", Theater.East);
        state.SetCoveringKeys(new[] { new KeyValuePair<string, int>("1st Corps:7", 2) });
        state.RekeyInstance(7, "1st Corps:8");
        AssertFalse(state.CoveringKeys.ContainsKey("1st Corps:7"), "the appointment moved the key");
        AssertEqual(2, state.CoveringKeys["1st Corps:8"], "to the new one, with its area");
        var other = new PlannerState();
        other.Observe(9, "2nd Corps:9", Theater.East);
        other.SetCoveringKeys(new[] { new KeyValuePair<string, int>("2nd Corps:9", 5) });
        other.Observe(9, "2nd Corps:10", Theater.East);   // vanilla's job 24 changed the commander between two reviews
        AssertFalse(other.CoveringKeys.ContainsKey("2nd Corps:9"), "the observation carries it by instance id");
        AssertEqual(5, other.CoveringKeys["2nd Corps:10"], "to the new key");
    }
    // ---- the world ----

    // FX-002A with a Union army on the road between the capitals, the CSA's three corps at Fredericksburg and one at Manassas. The post is
    // Fredericksburg (the one town the rule gives, CoveringForceTests.cs). The corps are invented: 9000 men each at readiness 3, full supply.
    private sealed class Cover
    {
        public CampaignSnapshot S;
        public SectorModel Sectors;
        public PlannerState State = new PlannerState();
        public TheaterPlan Plan;
        public EnemyPicture Picture;
    }

    private const int CorpsA = 1, CorpsB = 2, CorpsC = 3, CorpsManassas = 4;

    private static Cover CoverWorld(Action<CampaignSnapshot> tweak = null, bool plan = true, float total = float.NaN,
                                    PlayerCommandClass player = PlayerCommandClass.NotCareer)
    {
        var w = new Cover();
        var s = EnemyPictureTests.Csa(total, player);
        AddTowns(s);
        s.Beliefs.Add(EnemyPictureTests.Belief(1300f, -700f, 30000f));
        s.Own.Add(EnemyPictureTests.Corps(CorpsA, 1250f, -800f, 9000f));
        s.Own.Add(EnemyPictureTests.Corps(CorpsB, 1240f, -790f, 9000f));
        s.Own.Add(EnemyPictureTests.Corps(CorpsC, 1262f, -822f, 9000f));
        s.Own.Add(EnemyPictureTests.Corps(CorpsManassas, 1245f, -681f, 8000f));
        for (int i = 0; i < s.Own.Count; i++)
        {
            var f = s.Own[i];
            f.AreaIndex = i == 3 ? 3 : 2;   // the area each stands in: Manassas (area 3, Washington's sector) and Fredericksburg (area 2)
            s.Own[i] = f;
        }
        tweak?.Invoke(s);
        w.S = s;
        w.Sectors = SectorModel.Build(s);
        if (plan) w.Plan = TheaterPlanner.Plan(s, w.Sectors, null, w.State, null, null, ExecutorFixtures.Speed(), out _, out w.Picture);
        return w;
    }

    private static List<int> MemberIds(TheaterPlan plan)
    {
        return plan.Claims.Where(c => c.Claimant == Claimant.Hold && string.Equals(c.Reason, ForceAllocator.ReasonCoveringForce, StringComparison.Ordinal))
            .Select(c => c.InstanceId).OrderBy(i => i).ToList();
    }

    // ---- the lifecycle: no force ----

    private static void NoTownWithinRangeNoCoveringForce()
    {
        var w = CoverWorld(s => s.Places.Clear());
        AssertEqual(CoveringKind.None, w.Plan.Covering.Kind, "no town within R of either point: no force");
        AssertEqual("no-corridor-place", w.Plan.Covering.Reason, "and the reason says so");
        AssertEqual(0, MemberIds(w.Plan).Count, "nobody is a member");
        AssertEqual("none|none", w.State.CoveringPostKey, "the unbound day writes the key none|none, so a post that appears later takes the raw verdict");
    }

    private static void NoAxisSectorNoCoveringForce()
    {
        var w = CoverWorld(s =>
        {
            s.Beliefs.Clear();
            s.Beliefs.Add(EnemyPictureTests.Belief(EnemyPictureTests.WinchesterX, EnemyPictureTests.WinchesterZ, 6653f));
        });
        AssertEqual(CoveringKind.None, w.Plan.Covering.Kind, "the only army is 254 from the axis: no sector holds against anything on it");
        AssertEqual("no-axis-sector", w.Plan.Covering.Reason, "reason no-axis-sector");
        AssertEqual("none|none", w.State.CoveringPostKey, "and the key is unbound, as for no-corridor-place");
    }

    private static void CicSideHasNoCoveringForce()
    {
        var w = CoverWorld(s =>
        {
            var f = s.Own[0];
            f.Chain = WlChainClass.CicSide;
            s.Own[0] = f;
        }, player: PlayerCommandClass.CommanderInChief);
        AssertEqual(CoveringKind.None, w.Plan.Covering.Kind, "a CIC player's own side gets an empty allocation, so no force");
        AssertEqual("cic-side", w.Plan.Covering.Reason, "reason cic-side");
        AssertEqual(0, MemberIds(w.Plan).Count, "no member");
    }

    // ---- the members ----

    // D11 requirement-covering-members: the members are the axis sectors' hold reserves, nearest the post first. Fredericksburg's sector faces the
    // army on the road (18000 needed); its two nearest corps are reserved and are the force; the third corps and the one at Manassas are not.
    private static void MembersAreTheAxisHoldReservesNearestThePost()
    {
        var w = CoverWorld();
        var cov = w.Plan.Covering;
        AssertEqual(CoveringKind.Forward, cov.Kind, "one post: the raw verdict, forward when the force reaches the need");
        AssertEqual("Fredericksburg", cov.PostName, "the post is the town the rule gives");
        AssertEqual(2, cov.InstanceIds.Count, "two corps are the force");
        AssertTrue(cov.InstanceIds.Contains(CorpsA) && cov.InstanceIds.Contains(CorpsB), "the two nearest the post: " + string.Join(",", cov.InstanceIds));
        AssertEqual(string.Join(",", cov.InstanceIds.OrderBy(i => i)), string.Join(",", MemberIds(w.Plan)), "the plan's list is the table's covering-force claims");
        AssertNear(18000f, cov.Held, 0.5f, "they hold the need");
        AssertNear(cov.Need, cov.Held, 0.5f, "which the army out of the enemy capital sets: 0.6 x 30000");
        foreach (var claim in w.Plan.Claims.Where(c => c.Claimant == Claimant.Hold && c.Reason != null))
        {
            AssertEqual(w.Sectors.SectorAt(EnemyPictureTests.FredericksburgX, EnemyPictureTests.FredericksburgZ), claim.SectorId, "a member is counted in the sector that reserved it");
            AssertNear(cov.PostX, claim.TargetX, 0.01f, "and its target is the post");
        }
    }

    // ---- the hold pass over a hand-made input ----

    // The line of ExecutorFixtures.Wide (areas x 0..600, objectives at 100, 300 and 500) as one axis: own capital at x 0, enemy capital at x 600, so
    // every formation at z 0 lies on it. Three sectors: Sector(100) holds x up to 200, Sector(300) x 300 to 400, Sector(500) the rest. The covering
    // input is made by hand (CoveringForce.Prepare is the planner's): the post at orderX, the axis shares and the need as given.
    private sealed class Hand
    {
        public CampaignSnapshot S;
        public SectorModel Sectors;
        public CoveringInput C;
        public Claim[] Claims;
        public float[] Held;
        public IReadOnlyList<HeldBuilder> Builders;
    }

    private static int SectorAtX(Hand h, float x) { return h.Sectors.SectorAt(x, 0f); }

    private static FormationRecord Corps(int id, float x, float strength, Func<FormationRecord, FormationRecord> tweak = null)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        return tweak != null ? tweak(f) : f;
    }

    private static Hand HandWorld(float orderX, float need, float[] shareBySector, params FormationRecord[] corps)
    {
        var h = new Hand { S = ExecutorFixtures.Wide() };
        h.S.HasCapital = true;
        h.S.CapitalX = 0f;
        h.S.CapitalZ = 0f;
        h.S.HasEnemyCapital = true;
        h.S.EnemyCapitalX = 600f;
        h.S.EnemyCapitalZ = 0f;
        foreach (var f in corps) h.S.Own.Add(f);
        h.Sectors = SectorModel.Build(h.S);
        int n = h.S.Own.Count;
        h.C = new CoveringInput
        {
            OrderX = orderX, OrderZ = 0f, ForwardX = orderX, ForwardZ = 0f, Radius = h.S.RelocationRange, Need = need,
            AxisShare = (float[])shareBySector.Clone(), IncumbentSector = Enumerable.Repeat(-1, n).ToArray(), IncumbentArea = Enumerable.Repeat(-1, n).ToArray(),
            Department = new Theater[n], OnePost = true, PostKey = "P|P"
        };
        return h;
    }

    // Runs the hold pass: every sector with a share has the given need, the others none.
    private static void RunHold(Hand h, Action<Claim[]> beforeHold = null, float sectorNeed = 0f)
    {
        h.Claims = ClaimTable.Fresh(h.S);
        ClaimTable.ClaimImmobile(h.S, h.Claims);
        beforeHold?.Invoke(h.Claims);
        int k = h.Sectors.Count;
        var need = new float[k];
        var nearX = new float[k];
        var nearZ = new float[k];
        for (int i = 0; i < k; i++)
        {
            need[i] = h.C.AxisShare[i] > 0f ? sectorNeed : 0f;
            h.Sectors.Centroid(i, out nearX[i], out nearZ[i]);
        }
        h.Held = new float[k];
        ReserveRules.Hold(h.S, h.Sectors, need, nearX, nearZ, h.Claims, h.C, h.Builders, h.Held);
    }

    private static float[] Shares(Hand h, params (float x, float share)[] bySector)
    {
        var shares = new float[h.Sectors.Count];
        foreach (var (x, share) in bySector) shares[SectorAtX(h, x)] = share;
        return shares;
    }

    private static List<int> MembersOf(Hand h)
    {
        return h.Claims.Where(c => CoveringForce.IsMember(c)).Select(c => c.InstanceId).OrderBy(i => i).ToList();
    }

    private static Hand TwoSectorsOfSmallBodies(float need)
    {
        // Sector(100): ids 1-3 at x 240-ish are NOT in Sector(100) ... the sectors split at x 250: ids 1-3 at 190, 180, 170 are Sector(100)'s
        // (400 men each, 20/30/40 from a post at 210); ids 4-6 at 330, 340, 350 are Sector(300)'s (120/130/140 from it).
        var probe = new Hand { S = ExecutorFixtures.Wide() };
        probe.Sectors = SectorModel.Build(probe.S);
        var shares = new float[probe.Sectors.Count];
        shares[probe.Sectors.SectorAt(100f, 0f)] = 600f;
        shares[probe.Sectors.SectorAt(300f, 0f)] = 600f;
        return HandWorld(210f, need, shares, Corps(1, 190f, 400f), Corps(2, 180f, 400f), Corps(3, 170f, 400f), Corps(4, 330f, 400f), Corps(5, 340f, 400f), Corps(6, 350f, 400f));
    }

    // D11 requirement-covering-members, Sums: each axis sector gives members while its given strength is below its share; the body that crosses
    // the share is taken (as a hold reserve takes the body that crosses its need). Two sectors with a share of 600 and bodies of 400: each lends
    // two bodies (400, then 800 crossing the 600), never the third, although the need (3000) is still unmet.
    private static void NoSectorGivesMoreThanItsAxisShare()
    {
        var h = TwoSectorsOfSmallBodies(3000f);
        RunHold(h, sectorNeed: 1000f);
        AssertEqual("1,2,4,5", string.Join(",", MembersOf(h)), "two bodies of each sector and no third one: " + string.Join(",", MembersOf(h)));
        int west = SectorAtX(h, 100f), east = SectorAtX(h, 300f);
        AssertNear(800f, h.C.Given[west], 0.01f, "the first sector lent 800");
        AssertNear(800f, h.C.Given[east], 0.01f, "and so did the second");
        AssertEqual(Claimant.Hold, h.Claims[2].Claimant, "the third body is still a hold reserve of its sector");
        AssertTrue(h.Claims[2].Reason == null, "but not a member");
    }

    private static void TheBodyCrossingTheCapIsTaken()
    {
        var h = TwoSectorsOfSmallBodies(3000f);
        RunHold(h, sectorNeed: 1000f);
        int west = SectorAtX(h, 100f);
        AssertTrue(h.C.Given[west] > h.C.AxisShare[west], "the sector lent more than its share of 600: the body that crossed it was taken (" + h.C.Given[west] + ")");
        AssertTrue(MembersOf(h).Contains(2), "that body is a member");
    }

    // Members are ordered by the stored verdict's post: the same bodies, the post at the other end, the other sector's body leads.
    private static void MembersOrderedByTheStoredVerdictsPost()
    {
        FormationRecord[] bodies = { Corps(1, 100f, 400f), Corps(2, 420f, 400f) };
        var shares = new float[3];
        var near = HandWorld(100f, 400f, shares, bodies);
        near.C.AxisShare[SectorAtX(near, 100f)] = 400f;
        near.C.AxisShare[SectorAtX(near, 420f)] = 400f;
        RunHold(near, sectorNeed: 400f);
        AssertEqual("1", string.Join(",", MembersOf(near)), "the post at x 100: the body there is the force");
        var far = HandWorld(420f, 400f, shares, bodies);
        far.C.AxisShare[SectorAtX(far, 100f)] = 400f;
        far.C.AxisShare[SectorAtX(far, 420f)] = 400f;
        RunHold(far, sectorNeed: 400f);
        AssertEqual("2", string.Join(",", MembersOf(far)), "the post at x 420: the other body is");
    }

    // D11: per axis sector the hold reserve orders its candidates by distance to the post, not to the army the sector faces. One body is
    // needed; the army (nearX 0) is nearer the body at x 100, the post (210) nearer the one at x 190.
    private static void AxisSectorsOrderTheirReserveByThePostNotTheArmy()
    {
        var h = HandWorld(210f, 400f, new float[3], Corps(1, 100f, 400f), Corps(2, 190f, 400f));
        h.C.AxisShare[SectorAtX(h, 100f)] = 400f;
        h.Claims = ClaimTable.Fresh(h.S);
        var need = new float[h.Sectors.Count];
        need[SectorAtX(h, 100f)] = 400f;
        h.Held = new float[h.Sectors.Count];
        ReserveRules.Hold(h.S, h.Sectors, need, new float[h.Sectors.Count], new float[h.Sectors.Count], h.Claims, h.C, null, h.Held);
        AssertEqual("2", string.Join(",", MembersOf(h)), "the sector reserved the body nearest the post, not the one nearest the army");
        AssertEqual(Claimant.None, h.Claims[0].Claimant, "the other was not needed");
        var shipped = HandWorld(210f, 400f, new float[3], Corps(1, 100f, 400f), Corps(2, 190f, 400f));
        shipped.Claims = ClaimTable.Fresh(shipped.S);
        shipped.Held = new float[shipped.Sectors.Count];
        ReserveRules.Hold(shipped.S, shipped.Sectors, need, new float[shipped.Sectors.Count], new float[shipped.Sectors.Count], shipped.Claims, null, null, shipped.Held);
        AssertEqual(Claimant.Hold, shipped.Claims[0].Claimant, "with no covering input the shipped order stands (the army's)");
    }

    // The allocator's home-department discount applies to the distance to the post: a formation of the post sector's theater is closer.
    private static void AHomeDepartmentFormationIsCloserToThePost()
    {
        var h = HandWorld(210f, 400f, new float[3], Corps(1, 190f, 400f), Corps(2, 230f, 400f));
        h.C.AxisShare[SectorAtX(h, 100f)] = 400f;
        h.C.PostTheater = Theater.East;
        h.C.Department[1] = Theater.East;
        RunHold(h, sectorNeed: 400f);
        AssertEqual("2", string.Join(",", MembersOf(h)), "equally far from the post, the home department's formation leads (the lower id would otherwise)");
        h.C.Department[1] = Theater.Unknown;
        RunHold(h, sectorNeed: 400f);
        AssertEqual("1", string.Join(",", MembersOf(h)), "without the discount the tie goes to the lower id");
    }

    private static Hand IncumbentWorld(float incumbentX, int incumbentSectorAtX, float need, float shareAtX = 400f, params FormationRecord[] others)
    {
        var all = new List<FormationRecord> { Corps(1, incumbentX, 400f) };
        all.AddRange(others);
        var probe = HandWorld(210f, need, new float[3], all.ToArray());
        var shares = new float[probe.Sectors.Count];
        shares[SectorAtX(probe, 100f)] = shareAtX;
        shares[SectorAtX(probe, 300f)] = shareAtX;
        var h = HandWorld(210f, need, shares, all.ToArray());
        h.C.IncumbentSector[0] = SectorAtX(h, incumbentSectorAtX);
        h.C.IncumbentArea[0] = 11;
        return h;
    }

    // D11: an incumbent still claimable is taken first, whatever is nearer the post. The incumbent stands at x 330, far from the post at 210;
    // a body of its recorded sector stands 20 from it. The incumbent is the force and counts for the sector, so the nearer body is not reserved.
    private static void IncumbentsKeepTheirPlace()
    {
        var h = IncumbentWorld(190f, 100, 400f, 400f, Corps(2, 205f, 400f));
        RunHold(h, sectorNeed: 400f);
        AssertEqual("1", string.Join(",", MembersOf(h)), "the incumbent keeps the place: " + string.Join(",", MembersOf(h)));
        AssertEqual(Claimant.None, h.Claims[1].Claimant, "and the nearer body, not needed, was not even reserved");
        h.C.IncumbentSector[0] = -1;
        RunHold(h, sectorNeed: 400f);
        AssertEqual("2", string.Join(",", MembersOf(h)), "without the record the nearer body leads");
    }

    // The incumbent stands in Sector(300) but was recorded in Sector(100): it is counted there and in no other sector, so the sector it stands
    // in must reserve another body for its own need.
    private static void AnIncumbentCountsInItsRecordedSectorWhereverItStands()
    {
        var h = IncumbentWorld(330f, 100, 400f, 400f, Corps(2, 340f, 400f));
        RunHold(h, sectorNeed: 400f);
        int west = SectorAtX(h, 100f), east = SectorAtX(h, 300f);
        AssertEqual("1", string.Join(",", MembersOf(h)), "the incumbent is the force");
        AssertEqual(west, h.Claims[0].SectorId, "counted in its recorded sector");
        AssertNear(400f, h.Held[west], 0.01f, "which holds it");
        AssertNear(400f, h.Held[east], 0.01f, "and the sector it stands in holds its own reserve instead (the other body), not the incumbent");
        AssertEqual(east, h.Claims[1].SectorId, "the other body is counted where it stands");
    }

    // A member that has arrived at the post sector is not counted there as well: the post sector's own need is met by what stands there,
    // not by the member recorded elsewhere.
    private static void AnArrivedMemberIsNotCountedTwice()
    {
        var h = IncumbentWorld(300f, 100, 400f, 400f);   // stands at the post's sector, recorded in the other
        RunHold(h, sectorNeed: 400f);
        int west = SectorAtX(h, 100f), east = SectorAtX(h, 300f);
        AssertNear(400f, h.Held[west], 0.01f, "counted once, in the recorded sector");
        AssertNear(0f, h.Held[east], 0.01f, "and not again where it stands (that sector has no body of its own: it stands short)");
    }

    // An incumbent the pick stops before (the need was met by a nearer one earlier in the order) is an ordinary candidate of the sector it
    // stands in, with no reason.
    private static void AnUnselectedIncumbentIsACandidateWhereItStands()
    {
        var all = new[] { Corps(1, 190f, 400f), Corps(2, 330f, 400f) };
        var probe = HandWorld(210f, 400f, new float[3], all);
        var shares = new float[probe.Sectors.Count];
        shares[SectorAtX(probe, 100f)] = 400f;
        shares[SectorAtX(probe, 300f)] = 400f;
        var h = HandWorld(210f, 400f, shares, all);
        h.C.IncumbentSector[0] = SectorAtX(h, 100f);
        h.C.IncumbentSector[1] = SectorAtX(h, 300f);
        h.C.IncumbentArea[0] = h.C.IncumbentArea[1] = 11;
        RunHold(h, sectorNeed: 400f);
        AssertEqual("1", string.Join(",", MembersOf(h)), "the first incumbent met the need of 400");
        AssertEqual(Claimant.Hold, h.Claims[1].Claimant, "the second is its own sector's hold reserve");
        AssertTrue(h.Claims[1].Reason == null, "an ordinary one, with no reason");
        AssertEqual(SectorAtX(h, 300f), h.Claims[1].SectorId, "counted where it stands");
    }

    private static void AnIncumbentOffTheAxisIsNotAMember()
    {
        var h = IncumbentWorld(190f, 100, 400f, 400f, Corps(2, 200f, 400f));
        var off = h.S.Own[0];
        off.Z = 400f;   // 400 from the axis, more than the relocation range of 150
        h.S.Own[0] = off;
        RunHold(h, sectorNeed: 400f);
        AssertFalse(MembersOf(h).Contains(1), "an incumbent more than R from the axis is no member");
        AssertTrue(h.Claims[0].Reason == null, "it is an ordinary candidate where it stands");
    }

    private static void AGaveUpMemberIsDroppedTheNextReview()
    {
        var h = IncumbentWorld(190f, 100, 400f, 400f, Corps(2, 200f, 400f));
        h.C.GaveUp = new[] { new GaveUpMove { InstanceId = 1, TargetX = 210f, TargetZ = 0f, ObjectiveId = -1 } };
        RunHold(h, sectorNeed: 400f);
        AssertFalse(MembersOf(h).Contains(1), "the tracker gave up on sending it to the post: the incumbent is dropped");
        AssertEqual("2", string.Join(",", MembersOf(h)), "and the next body is the force; the dropped one is not re-picked: " + string.Join(",", MembersOf(h)));
        h.C.GaveUp = new[] { new GaveUpMove { InstanceId = 1, TargetX = 999f, TargetZ = 0f, ObjectiveId = -1 } };
        RunHold(h, sectorNeed: 400f);
        AssertTrue(MembersOf(h).Contains(1), "a give-up at another target is not a give-up at the post");
    }

    private static void HeldBuildersAndGiveUpsNotInThePool()
    {
        var h = IncumbentWorld(190f, 100, 400f, 400f, Corps(2, 200f, 400f), Corps(3, 205f, 400f));
        h.Builders = new[] { new HeldBuilder { InstanceId = 1, SectorId = 0, TargetX = 190f, TargetZ = 0f, SiteX = 190f, SiteZ = 0f, Wished = false } };
        RunHold(h, sectorNeed: 400f);
        AssertFalse(MembersOf(h).Contains(1), "a depot builder on its way (an incumbent) is no member");
        AssertTrue(h.Claims[0].Claimant == Claimant.None, "and is counted nowhere: its claim stays None for the allocator's depot step");
        h.Builders = new[] { new HeldBuilder { InstanceId = 2, SectorId = 0, TargetX = 200f, TargetZ = 0f, SiteX = 200f, SiteZ = 0f, Wished = true } };
        RunHold(h, sectorNeed: 400f, beforeHold: claims => ClaimTable.ClaimPinned(h.S, claims, h.Builders));
        AssertFalse(MembersOf(h).Contains(2), "a Wished builder keeps its claim and is never a member");
        AssertEqual(Claimant.PinnedBuilder, h.Claims[1].Claimant, "still the pin's");
    }

    // An incumbent whose readiness dipped fails only MassEligible: it keeps its entry (Retained) that review, is not a member, and is taken
    // first again once it is ready.
    private static void AReadinessDipKeepsTheIncumbency()
    {
        var h = IncumbentWorld(190f, 100, 400f, 400f, Corps(2, 200f, 400f));
        var dip = h.S.Own[0];
        dip.ReadinessStep = 1;
        h.S.Own[0] = dip;
        RunHold(h, sectorNeed: 400f);
        AssertTrue(h.C.Retained.Contains(0), "the incumbent keeps its entry through the dip");
        AssertFalse(MembersOf(h).Contains(1), "but is no member while it cannot be massed");
        dip.ReadinessStep = 3;
        h.S.Own[0] = dip;
        RunHold(h, sectorNeed: 400f);
        AssertEqual("1", string.Join(",", MembersOf(h)), "ready again, it is taken first");
        AssertFalse(h.C.Retained.Contains(0), "and no longer merely retained");
    }

    // The incumbents of a push on the enemy capital are Committed or in a battle (Immobile) while it lasts; they keep their entry and return when
    // they are claimable again.
    private static void IncumbentsReturnAfterTheEnemyCapitalPushEnds()
    {
        var h = IncumbentWorld(190f, 100, 400f, 400f, Corps(2, 200f, 400f));
        RunHold(h, sectorNeed: 400f, beforeHold: claims => claims[0].Claimant = Claimant.Committed);
        AssertTrue(h.C.Retained.Contains(0), "a Committed incumbent keeps its entry");
        AssertEqual(Claimant.Committed, h.Claims[0].Claimant, "and the hold took nothing from the committed");
        var busy = h.S.Own[0];
        busy.Flags |= FormationFlags.InBattle;
        h.S.Own[0] = busy;
        RunHold(h);
        AssertTrue(h.C.Retained.Contains(0), "an incumbent in a battle keeps its entry");
        busy.Flags &= ~FormationFlags.InBattle;
        h.S.Own[0] = busy;
        RunHold(h, sectorNeed: 400f);
        AssertEqual("1", string.Join(",", MembersOf(h)), "claimable again, it returns first");
    }

    // The formations at the post that cannot be massed count toward the need, and no sector's cap. The sector's own hold (2000) is more than the
    // force's need (800) here, so four bodies are reserved and the standing corps decides how many of them the force needs.
    private static Hand PostWithOneStandingCorps(Func<FormationRecord, FormationRecord> tweak)
    {
        var h = HandWorld(210f, 800f, new float[3], Corps(1, 205f, 400f, tweak), Corps(2, 190f, 400f), Corps(3, 180f, 400f), Corps(4, 170f, 400f), Corps(5, 160f, 400f));
        h.C.AxisShare[SectorAtX(h, 100f)] = 2000f;
        return h;
    }

    private static void BusyFormationsAtThePostCount()
    {
        var h = PostWithOneStandingCorps(f => { f.Flags |= FormationFlags.InBattle; return f; });
        RunHold(h, sectorNeed: 2000f);
        AssertNear(400f, h.C.StandingNear(210f, 0f, 150f), 0.01f, "the corps in a battle at the post is counted");
        AssertEqual("2", string.Join(",", MembersOf(h)), "one body more reaches the need of 800: the standing corps counted first");
    }

    private static void TheNonMassableAtThePostCountFirst()
    {
        var h = PostWithOneStandingCorps(f => { f.ReadinessStep = 1; return f; });
        RunHold(h, sectorNeed: 2000f);
        AssertEqual(1, MembersOf(h).Count, "the unready corps at the post counts first: one member completes the need, not two");
        var none = PostWithOneStandingCorps(f => f);
        RunHold(none, sectorNeed: 2000f);
        AssertEqual(2, MembersOf(none).Count, "(a ready corps at the post is an ordinary body, and two are needed)");
        h.C.NonMassable.Clear();
        h.C.NonMassable.Add(new CoveringInput.Standing { Strength = 100f, X = 900f, Z = 0f });
        AssertNear(0f, h.C.StandingNear(210f, 0f, 150f), 0.01f, "a formation out of range counts nothing");
    }

    // The map-group player counts at the post but is never the only holder: with no member taken and he counted, the first reserve body of the
    // axis sectors is added even though the need is met.
    private static void MapGroupPlayerNeverTheOnlyHolder()
    {
        // He stands at the post (in Sector(100), which he holds alone); the AI body is the reserve of Sector(300), the other axis sector.
        var h = HandWorld(210f, 400f, new float[3], Corps(1, 205f, 5000f, f => { f.Chain = WlChainClass.PlayerMapGroup; return f; }), Corps(2, 330f, 400f));
        h.C.AxisShare[SectorAtX(h, 100f)] = 400f;
        h.C.AxisShare[SectorAtX(h, 300f)] = 400f;
        RunHold(h, sectorNeed: 400f);
        AssertTrue(h.C.StandingNear(210f, 0f, 150f) >= 400f, "he alone meets the need (counted at strength x morale)");
        AssertEqual("2", string.Join(",", MembersOf(h)), "yet an AI body is the force: " + string.Join(",", MembersOf(h)));
    }

    private static void PlayerCommandNeverAMemberAndCountsNothing()
    {
        var h = HandWorld(210f, 400f, new float[3], Corps(1, 205f, 9000f, f => { f.Chain = WlChainClass.PlayerCommand; return f; }), Corps(2, 190f, 400f));
        h.C.AxisShare[SectorAtX(h, 100f)] = 400f;
        RunHold(h, sectorNeed: 400f);
        AssertNear(0f, h.C.StandingNear(210f, 0f, 150f), 0.01f, "the player's own command counts nothing");
        AssertEqual("2", string.Join(",", MembersOf(h)), "and is never a member");
    }

    private static void CarryingGroupMayBeAMember()
    {
        var h = HandWorld(210f, 400f, new float[3], Corps(1, 205f, 400f, f => { f.Chain = WlChainClass.CarryingGroup; return f; }), Corps(2, 190f, 400f));
        h.C.AxisShare[SectorAtX(h, 100f)] = 400f;
        RunHold(h, sectorNeed: 400f);
        AssertEqual("1", string.Join(",", MembersOf(h)), "the AI group that carries the player below map level is the nearest body and may be a member");
    }

    // ---- the planner's worlds ----

    private const string FredericksburgKey = "Fredericksburg@1247,-803|Fredericksburg@1247,-803";

    private static ForceAllocator_ Alloc(Cover w) => new ForceAllocator_(w);

    // A thin view of the allocation of a world, by instance id.
    private sealed class ForceAllocator_
    {
        public readonly Allocation A;
        private readonly Cover _w;
        public ForceAllocator_(Cover w)
        {
            _w = w;
            A = ForceAllocator.Allocate(w.Plan, w.S, w.Sectors, null, null, null);
        }
        public Assignment At(int id) { A.TryGet(id, out var a); return a; }
        public float Counted(int sector) { return ForceAllocator.CountedHold(_w.S, _w.Sectors, A, sector); }
    }

    private static int FredSector(Cover w) { return w.Sectors.SectorAt(EnemyPictureTests.FredericksburgX, EnemyPictureTests.FredericksburgZ); }

    private static Claim ClaimOf(Cover w, int id) { return w.Plan.Claims[w.S.IndexOfOwn(id)]; }

    // A review of the same snapshot again (the planner state carries over, as on the next day).
    private static TheaterPlan Replan(Cover w)
    {
        w.Plan = TheaterPlanner.Plan(w.S, w.Sectors, null, w.State, null, null, ExecutorFixtures.Speed(), out _, out w.Picture);
        return w.Plan;
    }

    // ---- the allocator ----

    private static void MembersCountInTheirOwnSectorsHold()
    {
        var w = CoverWorld();
        int fred = FredSector(w);
        AssertNear(18000f, w.Plan.SectorHeld[fred], 0.5f, "the sector's hold tally includes its members");
        var alloc = Alloc(w);
        AssertNear(18000f, alloc.Counted(fred), 0.5f, "and so does the allocator's counted hold (the [Reserves] row's held)");
    }

    private static void MemberAtPostHoldsWithheldElseRedeploys()
    {
        var w = CoverWorld();
        var alloc = Alloc(w);
        var atPost = alloc.At(CorpsA);
        AssertEqual(FormationRole.Hold, atPost.Role, "corps 1 stands within arrival range of the post: it holds it");
        AssertEqual(ForceAllocator.ReasonCoveringForce, atPost.Reason, "as the covering force");
        AssertTrue(atPost.Withheld, "withheld");
        var marching = alloc.At(CorpsB);
        AssertEqual(FormationRole.Redeploy, marching.Role, "corps 2 is a little farther: it redeploys to the post");
        AssertEqual(ForceAllocator.ReasonCoveringForce, marching.Reason, "with the same reason");
        AssertTrue(marching.Withheld, "withheld from both pools");
        AssertNear(w.Plan.Covering.PostX, marching.TargetX, 0.01f, "its target is the post");
        AssertEqual(FredSector(w), marching.SectorId, "and its sector the one that reserved it");
    }

    // A Redeploy member standing in the post sector counts for that sector in the allocator (it is still a member of the sector's hold).
    private static void RedeployingMemberInThePostSectorCountsInG4()
    {
        var w = CoverWorld();
        var alloc = Alloc(w);
        AssertEqual(FormationRole.Redeploy, alloc.At(CorpsB).Role, "a member on the march");
        AssertNear(18000f, alloc.Counted(FredSector(w)), 0.5f, "counts with the one at the post: 9000 + 9000");
        AssertEqual(0, alloc.A.Shortfalls.Count(id => id == CorpsB), "and the sector is not short of it");
    }

    // An incumbent recorded in Fredericksburg's sector but standing at Manassas (Washington's sector) is counted in the first, wherever it stands.
    private static Cover IncumbentAtManassas()
    {
        var w = CoverWorld(plan: false);
        w.State.SetCoveringKeys(new[] { new KeyValuePair<string, int>("corps" + CorpsManassas + ":" + CorpsManassas, 2) });
        Replan(w);
        return w;
    }

    private static void G4CountsAMemberInItsRecordedSector()
    {
        var w = IncumbentAtManassas();
        int fred = FredSector(w);
        int washington = w.Sectors.SectorAt(1245f, -681f);
        AssertTrue(fred != washington, "the sectors differ");
        var claim = ClaimOf(w, CorpsManassas);
        AssertTrue(CoveringForce.IsMember(claim), "the incumbent is a member: " + claim.Claimant + " " + claim.Reason);
        AssertEqual(fred, claim.SectorId, "recorded in Fredericksburg's sector");
        var alloc = Alloc(w);
        var a = alloc.At(CorpsManassas);
        AssertEqual(fred, a.SectorId, "its assignment's sector is the recorded one");
        AssertEqual(FormationRole.Redeploy, a.Role, "it marches from Manassas to the post");
        float counted = alloc.Counted(fred);
        AssertNear(w.S.Own[3].Strength + 9000f + 9000f, counted, 0.5f, "Fredericksburg's sector counts it with the two corps that stand there: " + counted);
        AssertNear(0f, alloc.Counted(washington), 0.5f, "and the sector it stands in does not (it holds nothing there)");
    }

    // The prefix withheld from vanilla's offence counts a covering member in its role sector: with the member's 1000 meeting the target, the
    // sector's other Hold formation (standing there) is not withheld; it would be if the member were counted where it stands.
    private static void PrefixesCountAMemberInItsRoleSector()
    {
        var s = ExecutorFixtures.Wide();
        s.Own.Add(ExecutorFixtures.Corps(1, 450f, 1000f));   // the member: recorded in Sector(100), standing in Sector(300)
        s.Own.Add(ExecutorFixtures.Corps(2, 90f, 600f));    // an ordinary hold formation of Sector(100)
        var sectors = ExecutorFixtures.Sectors(s);
        int west = sectors.SectorAt(100f, 0f);
        var plan = new TheaterPlan { Alliance = s.Alliance, DaySerial = s.DaySerial, MinGarrisonStrength = 100f };
        plan.Sectors.Add(new SectorPlan { SectorId = west, Posture = TheaterPosture.Hold, HoldNeed = 1000f, TargetX = 100f, TargetZ = 0f });
        var claims = ClaimTable.Fresh(s);
        claims[0] = new Claim { InstanceId = 1, Claimant = Claimant.Hold, Reason = ForceAllocator.ReasonCoveringForce, SectorId = west, Share = 1000f, TargetX = 100f, TargetZ = 0f };
        claims[1] = new Claim { InstanceId = 2, Claimant = Claimant.Hold, SectorId = west, Share = 600f, TargetX = 90f, TargetZ = 0f };
        plan.Claims.AddRange(claims);
        plan.SectorHeld = new float[sectors.Count];
        var a = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        AssertTrue(a.TryGet(1, out var member) && ForceAllocator.IsCoveringMember(in member), "corps 1 is the member");
        AssertTrue(a.TryGet(2, out var other) && other.Role == FormationRole.Hold, "corps 2 holds the sector");
        AssertFalse(other.Withheld, "the member's 1000 already meets the sector's target: nothing more is withheld");
    }

    // A member marching to the post that the tracker gave up on is Hold gave-up in the allocator that review, as every role that marches; the
    // plan (the same snapshot, the same give-up list) leaves it out of the force the next review (AGaveUpMemberIsDroppedTheNextReview).
    private static void AMemberThatGaveUpAtThePostIsHoldGaveUpInG4()
    {
        var w = CoverWorld();
        var gaveUp = new[] { new GaveUpMove { InstanceId = CorpsB, TargetX = w.Plan.Covering.PostX, TargetZ = w.Plan.Covering.PostZ, ObjectiveId = -1 } };
        var a = ForceAllocator.Allocate(w.Plan, w.S, w.Sectors, null, null, null, gaveUp);
        a.TryGet(CorpsB, out var x);
        AssertEqual(FormationRole.Hold, x.Role, "it holds where it stands");
        AssertEqual(ForceAllocator.ReasonGaveUp, x.Reason, "with the give-up's reason");
        var replanned = TheaterPlanner.Plan(w.S, w.Sectors, null, w.State, null, null, ExecutorFixtures.Speed(), out _, out _, gaveUp);
        AssertFalse(replanned.Covering.InstanceIds.Contains(CorpsB), "and the next plan has dropped it from the force");
    }

    private static void PlanGroupsGroupsACoveringMemberWhereItStands()
    {
        var s = ExecutorFixtures.Wide();
        var sectors = ExecutorFixtures.Sectors(s);
        int west = sectors.SectorAt(100f, 0f), east = sectors.SectorAt(500f, 0f);
        var a = new Allocation();
        a.Assignments.Add(new Assignment { InstanceId = 1, Role = FormationRole.Hold, SectorId = west, TargetX = 500f, TargetZ = 0f, Reason = ForceAllocator.ReasonCoveringForce, Withheld = true });
        a.Assignments.Add(new Assignment { InstanceId = 2, Role = FormationRole.Hold, SectorId = east, TargetX = 520f, TargetZ = 0f, Reason = ForceAllocator.ReasonHoldReserve });
        AssertEqual(0, ArmyGroupDoctrine.PlanGroups(a).Count, "grouped by the sector that reserved it, the member is alone");
        var groups = ArmyGroupDoctrine.PlanGroups(a, 2, sectors);
        AssertEqual(1, groups.Count, "grouped by the sector it stands in (its post's), it joins the formation there");
        AssertEqual(east, groups[0].SectorId, "in that sector");
        AssertEqual("1,2", string.Join(",", groups[0].InstanceIds), "both");
    }

    // D11 requirement-covering-members: no sector gives a formation to the force that it needs for its own hold. Winchester's sector, off the axis,
    // reserves the same body with the force on and off; every sector's hold is at least what it was.
    private static void CoveringNeverStripsAnotherSectorsHold()
    {
        // The army on the road is 60000 here, so the force's need (36000) is more than the axis sectors' reserves can give (9000 + 9000 + 8000):
        // it is short, and the only other body in the field is Winchester's, which holds against the 2nd Division off the axis and is not for the taking.
        Action<CampaignSnapshot> winchester = s =>
        {
            s.Beliefs.Clear();
            s.Beliefs.Add(EnemyPictureTests.Belief(1300f, -700f, 60000f));
            s.Beliefs.Add(EnemyPictureTests.Belief(EnemyPictureTests.WinchesterX, EnemyPictureTests.WinchesterZ, 6653f));
            s.Own.Add(EnemyPictureTests.Corps(9, 1100f, -560f, 5000f));
            var f = s.Own[s.Own.Count - 1];
            f.AreaIndex = 5;
            s.Own[s.Own.Count - 1] = f;
        };
        var on = CoverWorld(winchester);
        PlannerSettings.CoveringForce = false;
        Cover off;
        try { off = CoverWorld(winchester); }
        finally { PlannerSettings.CoveringForce = true; }
        AssertEqual(CoveringKind.Rear, on.Plan.Covering.Kind, "the force exists with the flag on (short of its need, so the rear post)");
        AssertTrue(on.Plan.Covering.Held < on.Plan.Covering.Need, "and is short of its need: " + on.Plan.Covering.Held + " of " + on.Plan.Covering.Need);
        AssertFalse(CoveringForce.IsMember(ClaimOf(on, 9)), "it did not draw on Winchester's body");
        int win = on.Sectors.SectorAt(EnemyPictureTests.WinchesterX, EnemyPictureTests.WinchesterZ);
        AssertEqual(ClaimOf(off, 9).Claimant, ClaimOf(on, 9).Claimant, "Winchester's body keeps the claim it has without the force");
        for (int k = 0; k < on.Sectors.Count; k++)
            AssertTrue(on.Plan.SectorHeld[k] >= off.Plan.SectorHeld[k] - 0.5f, "sector " + k + " holds at least as much with the force: " + on.Plan.SectorHeld[k] + " against " + off.Plan.SectorHeld[k]);
        AssertTrue(on.Plan.SectorHeld[win] >= 4000f - 0.5f, "Winchester's sector holds its need");
    }

    private static void UnionSideCoversToo()
    {
        var s = Union();
        s.Beliefs.Add(EnemyPictureTests.Belief(1290f, -720f, 20000f));
        s.Areas.AddRange(EnemyPictureTests.Csa().Areas);
        s.Objectives.AddRange(EnemyPictureTests.Csa().Objectives);
        s.Own.Add(EnemyPictureTests.Corps(11, 1342f, -664f, 9000f));
        s.Own.Add(EnemyPictureTests.Corps(12, 1330f, -650f, 9000f));
        s.Own.Add(EnemyPictureTests.Corps(13, 1250f, -700f, 9000f));
        s.Own.Add(EnemyPictureTests.Corps(14, 1255f, -715f, 9000f));
        var sectors = SectorModel.Build(s);
        var state = new PlannerState();
        var plan = TheaterPlanner.Plan(s, sectors, null, state, null, null, ExecutorFixtures.Speed(), out _);
        AssertEqual("Alexandria", plan.Covering.PostName, "the Union's post is its own nearest town");
        AssertTrue(plan.Covering.Kind != CoveringKind.None, "it has a force: " + plan.Covering.Reason);
        AssertTrue(plan.Covering.InstanceIds.Count >= 1, "with members");
        AssertEqual(0, plan.Alliance, "for alliance 0");
    }

    // ---- the verdict, in the planner ----

    // Two posts: a town at each point (the forward point and the rear point lie about 12 apart on the 389 axis, so the towns are put at each).
    private static Action<CampaignSnapshot> TwoPosts => s =>
    {
        s.Places.Clear();
        s.Places.Add(new PlaceRecord { Name = "Richmond", X = EnemyPictureTests.RichmondX, Z = EnemyPictureTests.RichmondZ, Owner = 1 });
        s.Places.Add(new PlaceRecord { Name = "Washington", X = EnemyPictureTests.WashingtonX, Z = EnemyPictureTests.WashingtonZ, Owner = 0 });
        s.Places.Add(new PlaceRecord { Name = "Forward Town", X = 1305f, Z = -828f, Owner = 1 });
        s.Places.Add(new PlaceRecord { Name = "Rear Town", X = 1308f, Z = -815f, Owner = 1 });
    };

    private static void ForwardVerdictOnCountedStrength()
    {
        var strong = CoverWorld(TwoPosts);
        AssertEqual(CoveringKind.Forward, strong.Plan.Covering.Kind, "the members reach the need: the forward post");
        AssertEqual("Forward Town", strong.Plan.Covering.PostName, "which is the forward town");
        var weak = CoverWorld(s =>
        {
            TwoPosts(s);
            s.Own.RemoveAt(1);   // corps 2 gone: 9000 against a need of 18000
            s.Own.RemoveAt(2);
        });
        AssertEqual(CoveringKind.Rear, weak.Plan.Covering.Kind, "short of the need the first review's raw verdict is the rear post");
        AssertEqual("Rear Town", weak.Plan.Covering.PostName, "the rear town");
        AssertTrue(weak.Plan.Covering.Held < weak.Plan.Covering.Need, "and the force is short");
        AssertNear(1308f, ClaimOf(weak, CorpsA).TargetX, 0.01f, "the member is sent to the published (rear) post, though the pick was ordered by the forward one");
    }

    private static void ThePlannersVerdictNeedsThreeReviewsAndTheFlipPublishesTheStoredPost()
    {
        var w = CoverWorld(TwoPosts);
        AssertEqual(CoveringKind.Forward, w.Plan.Covering.Kind, "day 1 (strong): forward, stored forward");
        var full = new List<FormationRecord>(w.S.Own);
        w.S.Own.RemoveAt(1);   // corps 2 and the Manassas corps leave: the force is short from now on
        w.S.Own.RemoveAt(2);
        AssertEqual(CoveringKind.Forward, Replan(w).Covering.Kind, "day 2 (short): the stored verdict, forward, still stands");
        AssertEqual(1, w.State.CoveringVerdictDays, "the other verdict has led one day");
        AssertEqual(CoveringKind.Forward, Replan(w).Covering.Kind, "day 3: still forward");
        AssertEqual(CoveringKind.Forward, Replan(w).Covering.Kind, "day 4, the third review of the other verdict: it flips the stored verdict but publishes the old post");
        AssertFalse(w.State.CoveringForward, "the stored verdict is now rear");
        AssertEqual("Forward Town", w.Plan.Covering.PostName, "the flip review's post is the stored verdict's");
        AssertEqual(CoveringKind.Rear, Replan(w).Covering.Kind, "day 5: the rear post");
        AssertEqual("Rear Town", w.Plan.Covering.PostName, "named");
        AssertEqual(full.Count, 4, "(the roster was four corps)");
    }

    private static void OnePostFollowsTheRawVerdictAtOnce()
    {
        var w = CoverWorld();
        AssertEqual(CoveringKind.Forward, w.Plan.Covering.Kind, "strong: forward");
        w.S.Own.RemoveAt(1);
        w.S.Own.RemoveAt(2);
        AssertEqual(CoveringKind.Rear, Replan(w).Covering.Kind, "with one post the next review is the raw verdict: rear at once");
        AssertEqual(0, w.State.CoveringVerdictDays, "and no count is kept");
    }

    // The members are remembered with the area they stood in; a sector id that renumbers does not lose them.
    private static void TheStateKeepsTheMembersWithTheirAreas()
    {
        var w = CoverWorld();
        AssertEqual(2, w.State.CoveringKeys.Count, "the two members");
        AssertEqual(2, w.State.CoveringKeys["corps" + CorpsA + ":" + CorpsA], "each with the area it stands in (Fredericksburg's, 2), not a sector id");
        AssertEqual(FredericksburgKey, w.State.CoveringPostKey, "the key is the towns with their rounded positions");
        AssertTrue(w.State.CoveringVerdictKnown && w.State.CoveringForward, "and a verdict is stored");
    }

    private static void ARenumberedSectorKeepsTheIncumbentsSector()
    {
        var w = CoverWorld();
        int before = FredSector(w);
        w.S.Objectives.RemoveAll(o => o.ObjectiveId == 10);   // the Richmond objective is accomplished: the sectors are rebuilt and renumber
        w.Sectors = SectorModel.Build(w.S);
        int after = FredSector(w);
        AssertTrue(before != after, "the sector ids moved (" + before + " -> " + after + ")");
        Replan(w);
        AssertTrue(CoveringForce.IsMember(ClaimOf(w, CorpsA)), "the incumbent is still a member");
        AssertEqual(after, ClaimOf(w, CorpsA).SectorId, "of the sector its area belongs to now");
    }

    // A ready formation that stands at the post but is not a reserve body (it is in the main effort's staged mass, say) is massable, so it is not
    // counted by the non-massable count: a staged push near the post never fills the force.
    private static void AStagedPushNearThePostIsNotCovering()
    {
        var h = HandWorld(210f, 400f, new float[3], Corps(1, 205f, 9000f), Corps(2, 190f, 400f));
        h.C.AxisShare[SectorAtX(h, 300f)] = 400f;   // only Sector(300) is an axis sector: the ready corps at the post is nobody's reserve there
        RunHold(h, sectorNeed: 400f);
        AssertNear(0f, h.C.StandingNear(210f, 0f, 150f), 0.01f, "a massable formation at the post is not counted as standing there");
        AssertEqual(0, MembersOf(h).Count, "and the force has no body to be: the reserve bodies it may use are the axis sectors' own");
    }

    // ---- the lifecycle ----

    // Corps 1, 2 and 4 are strong here and the Union army is 8000 at (1230, -590), 130 from the axis, 126 from Winchester and 266 from Washington's
    // objective. With Washington's objective offered the push on the enemy capital is the main effort; without it the best target is far away.
    private static Action<CampaignSnapshot> PushWorldTweak(bool washingtonOffered)
    {
        return s =>
        {
            s.Beliefs.Clear();
            // Offered: 10000 men on the road, so the push needs the covering body besides the two small corps. Not offered: 8000 men 126 from
            // Winchester and 130 from the axis, so every army the sector faces lies within range of Winchester and its hold is released.
            s.Beliefs.Add(washingtonOffered ? EnemyPictureTests.Belief(1300f, -700f, 10000f) : EnemyPictureTests.Belief(1230f, -590f, 8000f));
            if (!washingtonOffered) s.Objectives.RemoveAll(o => o.ObjectiveId == 20);
            Action<int, float> strength = (i, v) => { var f = s.Own[i]; f.Strength = v; s.Own[i] = f; };
            strength(0, 12000f);
            strength(1, 1000f);
            strength(3, 1000f);
        };
    }

    // The members stay out of every mass: a target that is not the enemy capital cannot take the covering force's body (corps 1), though that
    // body is free of its sector's army (every army it faces lies within range of the target, so the shipped release would have let it go).
    private static void MassForExcludesMembersExceptAgainstTheEnemyCapital()
    {
        var w = CoverWorld(PushWorldTweak(washingtonOffered: false));
        AssertTrue(CoveringForce.IsMember(ClaimOf(w, CorpsA)), "corps 1 is the covering force");
        AssertFalse(w.Plan.MassInstanceIds.Contains(CorpsA), "and no mass takes it: " + string.Join(",", w.Plan.MassInstanceIds));
        AssertEqual(CoveringKind.Forward, w.Plan.Covering.Kind, "the force stands");
    }

    private static void MassForTakesMembersForAnyEnemyCapitalTarget()
    {
        var w = CoverWorld(PushWorldTweak(washingtonOffered: true));
        AssertEqual(20, w.Plan.MainObjectiveId, "the push on Washington is the main effort");
        AssertTrue(w.Plan.MassInstanceIds.Contains(CorpsA), "and its mass takes the covering force's body: " + string.Join(",", w.Plan.MassInstanceIds));
        AssertEqual(Claimant.Mass, ClaimOf(w, CorpsA).Claimant, "its claim is the mass's");
        AssertTrue(ClaimOf(w, CorpsA).Reason == null, "no longer the force's");
    }

    private static void EnemyCapitalPushLeavesTheCoveringStateUntouched()
    {
        var w = CoverWorld(PushWorldTweak(washingtonOffered: true), plan: false);
        w.State.RestoreCovering(new[] { new KeyValuePair<string, int>("corps" + CorpsA + ":" + CorpsA, 2) }, forward: true, verdictDays: 1, verdictKnown: true, postKey: FredericksburgKey);
        Replan(w);
        AssertEqual(CoveringKind.None, w.Plan.Covering.Kind, "the force is unbound for the push");
        AssertEqual(CoveringForce.ReasonEnemyCapitalPush, w.Plan.Covering.Reason, "reason main-effort-enemy-capital");
        AssertEqual(1, w.State.CoveringKeys.Count, "the incumbents survive the push");
        AssertTrue(w.State.CoveringForward && w.State.CoveringVerdictDays == 1 && w.State.CoveringVerdictKnown, "so does the verdict");
        AssertEqual(FredericksburgKey, w.State.CoveringPostKey, "and the post key");
    }

    // Corps 1 is not needed by the push here (the mass is full without it): it is left behind and is an ordinary hold reserve for the day.
    private static Cover LeftBehindWorld()
    {
        return CoverWorld(s =>
        {
            s.Beliefs.Clear();
            s.Beliefs.Add(EnemyPictureTests.Belief(1300f, -700f, 10000f));
            Action<int, float> strength = (i, v) => { var f = s.Own[i]; f.Strength = v; s.Own[i] = f; };
            strength(0, 12000f);
            strength(1, 12000f);
            strength(3, 12000f);
        });
    }

    private static void EnemyCapitalPushMakesTheLeftBehindOrdinaryHoldReserves()
    {
        var w = LeftBehindWorld();
        AssertEqual(20, w.Plan.MainObjectiveId, "the push on Washington");
        var claim = ClaimOf(w, CorpsA);
        AssertEqual(Claimant.Hold, claim.Claimant, "corps 1 was not taken by the mass");
        AssertTrue(claim.Reason == null, "it is an ordinary hold reserve now");
        var a = Alloc(w).At(CorpsA);
        AssertEqual(FormationRole.Hold, a.Role, "Hold");
        AssertEqual(ForceAllocator.ReasonHoldReserve, a.Reason, "hold-reserve, nothing is sent to an unbound post");
    }

    private static void TheLeftBehindAreRewrittenAfterTheMass()
    {
        var w = LeftBehindWorld();
        var claim = ClaimOf(w, CorpsA);
        AssertEqual(w.Sectors.SectorAt(1250f, -800f), claim.SectorId, "the rewrite counts it in the sector it stands in");
        AssertNear(12000f, claim.Share, 0.5f, "its share stays where the hold pass put it for that one review (the pass counted 12000)");
        AssertNear(1250f, claim.TargetX, 0.01f, "and its target is where it stands, not the unbound post");
    }

    private static void LostCapitalWritesNoneKey()
    {
        var w = CoverWorld(s => s.HasCapital = false);
        AssertEqual(CoveringKind.None, w.Plan.Covering.Kind, "no capital of its own, no axis");
        AssertEqual(CoveringForce.ReasonNoCapital, w.Plan.Covering.Reason, "reason no-capital");
        AssertEqual(CoveringForce.UnboundPostKey, w.State.CoveringPostKey, "the key none|none is written");
        var back = CoverWorld(plan: false);
        back.State.RestoreCovering(null, true, 2, true, CoveringForce.UnboundPostKey);
        Replan(back);
        AssertEqual(CoveringKind.Forward, back.Plan.Covering.Kind, "a post that appears after an unbound day takes the raw verdict");
        AssertEqual(0, back.State.CoveringVerdictDays, "with the count at 0");
    }

    private static void CoveringUnbindsOnLostCapitalNoPlaceFlagOffAndEnemyCapitalPush()
    {
        var reasons = new List<string>();
        reasons.Add(CoverWorld(s => s.HasCapital = false).Plan.Covering.Reason);
        reasons.Add(CoverWorld(s => s.Places.Clear()).Plan.Covering.Reason);
        PlannerSettings.CoveringForce = false;
        try { reasons.Add(CoverWorld().Plan.Covering.Reason); }
        finally { PlannerSettings.CoveringForce = true; }
        reasons.Add(CoverWorld(PushWorldTweak(washingtonOffered: true)).Plan.Covering.Reason);
        AssertEqual("no-capital,no-corridor-place,flag-off,main-effort-enemy-capital", string.Join(",", reasons), "each unbinding says why");
        var off = CoverWorld(plan: false);
        off.State.RestoreCovering(new[] { new KeyValuePair<string, int>("corps1:1", 2) }, true, 0, true, FredericksburgKey);
        PlannerSettings.CoveringForce = false;
        try { Replan(off); }
        finally { PlannerSettings.CoveringForce = true; }
        AssertEqual(1, off.State.CoveringKeys.Count, "with the flag off the keys are kept and ignored");
        AssertEqual(0, MemberIds(off.Plan).Count, "and nobody is a member");
    }

    // A member is placed before the scout draw, the depot step and the garrison posts: they take formations the table left to them. A recon
    // task for a picket, and a depot builder already on its way, find the members where they are.
    private static void ScoutDrawDepotAndPostsNeverTakeAMember()
    {
        var w = CoverWorld();
        var tasks = new List<ReconTask> { new ReconTask { Kind = ReconKind.Picket, SectorId = FredSector(w), FromX = 1250f, FromZ = -800f, HoldX = 1250f, HoldZ = -800f, Priority = 1f, Reason = "t" } };
        var a = ForceAllocator.Allocate(w.Plan, w.S, w.Sectors, null, tasks, new ReconMissionLedger(), null,
            new[] { new HeldBuilder { InstanceId = CorpsManassas, SectorId = 0, TargetX = 1245f, TargetZ = -681f, SiteX = 1245f, SiteZ = -681f, Wished = false } });
        foreach (int id in new[] { CorpsA, CorpsB })
        {
            a.TryGet(id, out var x);
            AssertEqual(ForceAllocator.ReasonCoveringForce, x.Reason, "member " + id + " keeps its role: " + x.Role + " " + x.Reason);
        }
    }

    // ---- the mover, the withhold view ----

    private static void ACoveringMemberIsTakenOffVanillasOffensiveList()
    {
        var w = CoverWorld();
        var f = w.S.Own[0];
        f.Flags |= FormationFlags.InOffensiveOps;
        w.S.Own[0] = f;
        var a = ForceAllocator.Allocate(w.Plan, w.S, w.Sectors, null, null, null);
        var host = new FakeMoverHost();
        CampaignMover.Review(ExecutorFixtures.Inputs(w.S, w.Plan, a, ExecutorFixtures.OpenTempo()), host, new MoveTracker(), ExecutorFixtures.State());
        AssertTrue(host.Listed.Contains((CorpsA, false)), "a member holding its post leaves unitsinoffensiveoperations");
    }

    private static void PublishFor(Cover w)
    {
        CampaignScope.ResetAll();
        var a = ForceAllocator.Allocate(w.Plan, w.S, w.Sectors, null, null, null);
        w.Plan.DaySerial = ExecutorFixtures.Day;
        PlanFreshness.NoteToday(ExecutorFixtures.Day);
        CampaignPlanStore.Publish(1, w.Plan, a, w.Sectors);
        CampaignPlanStore.PublishExecutorContext(1, new ExecutorContext { Released = true, ExecutorsAllowed = true });
    }

    private static Cover WorldWithAdvance(EnemyTraits traits, DetectionSource source = DetectionSource.Seen, float x = 1290f, float z = -830f)
    {
        return CoverWorld(s =>
        {
            var b = EnemyPictureTests.Belief(x, z, 2000f, 1f, traits);
            b.LastSource = source;
            s.Beliefs.Add(b);
        });
    }

    private static void OffenceNeverTakesAMember()
    {
        var w = WorldWithAdvance(EnemyTraits.OnOwnSoil);
        PublishFor(w);
        try
        {
            AssertTrue(w.Plan.Covering.ThreatNear, "an advance on own soil stands near the post");
            AssertTrue(CampaignWithholdView.WithheldFromOffense(1, CorpsA), "the member holding the post is withheld from vanilla's offence even then");
            AssertTrue(CampaignWithholdView.WithheldFromOffense(1, CorpsB), "so is the one on the march");
        }
        finally { CampaignScope.ResetAll(); }
    }

    private static void DefenceMayTakeAMemberWhenThreatNear()
    {
        var quiet = CoverWorld();
        PublishFor(quiet);
        try
        {
            AssertFalse(quiet.Plan.Covering.ThreatNear, "the army standing at its own capital does not open the clause");
            AssertTrue(CampaignWithholdView.WithheldFromDefense(1, CorpsA, 0f, 0f), "no advance: the member is withheld from vanilla's defence");
            AssertTrue(CampaignWithholdView.WithheldFromDefense(1, CorpsB, 0f, 0f), "and so is the one on the march");
        }
        finally { CampaignScope.ResetAll(); }
        var threatened = WorldWithAdvance(EnemyTraits.OnOwnSoil);
        PublishFor(threatened);
        try
        {
            AssertFalse(CampaignWithholdView.WithheldFromDefense(1, CorpsA, 0f, 0f), "an advance on own soil near the post: vanilla's defence may take the member at the post");
            AssertFalse(CampaignWithholdView.WithheldFromDefense(1, CorpsB, 0f, 0f), "and the one marching to it");
        }
        finally { CampaignScope.ResetAll(); }
    }

    private static void ThreatNearNeedsAnAdvanceOnOwnSoil()
    {
        var notOnOwnSoil = WorldWithAdvance(0);
        AssertFalse(notOnOwnSoil.Plan.Covering.ThreatNear, "an army near the post that is not on the side's own soil is no advance into its territory");
        var far = WorldWithAdvance(EnemyTraits.OnOwnSoil, x: 1100f, z: -1100f);
        AssertFalse(far.Plan.Covering.ThreatNear, "an advance on own soil more than R from the post does not open it");
        AssertTrue(WorldWithAdvance(EnemyTraits.OnOwnSoil).Plan.Covering.ThreatNear, "the same army inside R of the post does");
    }

    private static void ThreatNearCountsMarks()
    {
        var mark = WorldWithAdvance(EnemyTraits.OnOwnSoil, DetectionSource.Mark);
        AssertTrue(mark.Plan.Covering.ThreatNear, "a mark is a strength reading of any source: it counts");
    }

    // The clause is before the shipped same-sector exception: a member, a Hold of the sector the vanilla argument stands in, is still withheld
    // when there is no advance, which the exception alone would have let go.
    private static void CoveringClauseBeforeTheSameSectorException()
    {
        var w = CoverWorld();
        PublishFor(w);
        try
        {
            AssertEqual(FormationRole.Hold, w.Plan.Covering.InstanceIds.Count > 0 ? ForceAllocator.Allocate(w.Plan, w.S, w.Sectors, null, null, null).Assignments.First(x => x.InstanceId == CorpsA).Role : FormationRole.Reserve, "corps 1 is a Hold");
            AssertTrue(CampaignWithholdView.WithheldFromDefense(1, CorpsA, 1250f, -800f), "the enemy vanilla names stands in the member's own sector, yet it stays withheld");
        }
        finally { CampaignScope.ResetAll(); }
        string view = System.IO.File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Strategic/Planner/CampaignWithholdView.cs"));
        string body = BodyOf(view, "public static bool WithheldFromDefense(");
        AssertTrue(body.IndexOf("IsCoveringMember", StringComparison.Ordinal) > 0 && body.IndexOf("IsCoveringMember", StringComparison.Ordinal) < body.IndexOf("sectors.SectorAt(enemyX, enemyZ)", StringComparison.Ordinal),
            "in the source the covering clause comes before the same-sector exception");
    }

    // BUG-AI-002: the position vanilla passes is the unsorted list's, an army that is often not the one that moved. The clause must not use it.
    private static void CoveringClauseReadsBeliefsNotVanillasArgument()
    {
        var threatened = WorldWithAdvance(EnemyTraits.OnOwnSoil);
        PublishFor(threatened);
        try
        {
            AssertFalse(CampaignWithholdView.WithheldFromDefense(1, CorpsA, 9999f, 9999f), "the belief says an advance stands near the post: the member is available, whatever position vanilla passes");
        }
        finally { CampaignScope.ResetAll(); }
        var quiet = CoverWorld();
        PublishFor(quiet);
        try
        {
            AssertTrue(CampaignWithholdView.WithheldFromDefense(1, CorpsA, 1290f, -830f), "no advance in the beliefs: withheld, even when vanilla's argument stands at the post");
        }
        finally { CampaignScope.ResetAll(); }
        string view = System.IO.File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Strategic/Planner/CampaignWithholdView.cs"));
        string body = BodyOf(view, "public static bool WithheldFromDefense(");
        int at = body.IndexOf("IsCoveringMember", StringComparison.Ordinal);
        int line = body.IndexOf('\n', at);
        string clause = body.Substring(at, line - at);
        AssertFalse(clause.Contains("enemyX") || clause.Contains("enemyZ"), "the clause's line names neither coordinate of vanilla's argument: " + clause);
    }

    // ---- the telemetry ----

    private static void TheCoveringRowNamesThePostTheMembersAndTheShort()
    {
        var w = CoverWorld();
        string row = CoveringForce.Row(w.Plan.Covering, w.S, w.State);
        AssertTrue(row.StartsWith("[CoveringForce] alliance=1 post=Fredericksburg kind=forward reason=- need=18000 held=18000 members=", StringComparison.Ordinal), "the row: " + row);
        AssertContains(row, " short=0 threatNear=0 verdictDays=0", "no short, no threat, no count");
        AssertContains(row, "members=corps1:1+corps2:2 ", "the members' unit keys, the ones [CampaignMoverIssue] names");
        var none = CoverWorld(s => s.Places.Clear());
        string unbound = CoveringForce.Row(none.Plan.Covering, none.S, none.State);
        AssertContains(unbound, " post=none kind=none reason=no-corridor-place need=0 held=0 members=- short=0 ", "an unbound day names why and no post");
        var weak = CoverWorld(s => { s.Own.RemoveAt(1); s.Own.RemoveAt(2); });
        AssertContains(CoveringForce.Row(weak.Plan.Covering, weak.S, weak.State), " kind=rear ", "a short force with one post is rear");
        AssertContains(CoveringForce.Row(weak.Plan.Covering, weak.S, weak.State), " short=9000 ", "and reports what it lacks");
        var spaced = CoverWorld(s => { s.Places[IndexOfPlace(s, "Fredericksburg")] = Place("Fort Frederick Town", EnemyPictureTests.FredericksburgX, EnemyPictureTests.FredericksburgZ, 1); });
        string quoted = CoveringForce.Row(spaced.Plan.Covering, spaced.S, spaced.State);
        AssertContains(quoted, " post=\"Fort Frederick Town\" ", "a post with a space is quoted");
        AssertTrue(TelemetryLegacyParser.TryParse(row + " date=1861-06-23", TelemetryProfile.CampaignTuning, "s", out var ev), "the row parses");
        AssertEqual("Fredericksburg", ev.Fields.GetString("post"), "with the post whole");
    }

    private static void TheCoveringRowIsRegisteredAndWrittenOnChange()
    {
        var routed = TelemetryTagPolicy.Route("[CoveringForce] alliance=1 date=1861-06-23 post=none kind=none reason=flag-off need=0 held=0 members=- short=0 threatNear=0 verdictDays=0");
        AssertEqual("CoveringForce", routed.EventName, "the router knows the row");
        AssertEqual(TelemetryCategory.State, routed.Category, "State");
        AssertEqual(TelemetryLayer.Campaign, routed.Layer, "campaign layer");
        string runtime = FindSource("Strategic/Planner/CampaignPlannerRuntime.cs");
        string emit = BodyOf(runtime, "private static void EmitCovering(");
        AssertTrue(emit.Contains("CoveringForce.Row(") && emit.Contains("LastCoveringRow[a]"), "built from CoveringForce.Row and compared with the last one");
        AssertTrue(emit.Contains("ReviewCadenceCatchUp.Dated(row, plan.DaySerial)"), "dated after the compare");
        string review = BodyOf(runtime, "internal static void ReviewPlan(");
        AssertTrue(review.IndexOf("EmitAllocation(", StringComparison.Ordinal) < review.IndexOf("EmitCovering(", StringComparison.Ordinal)
            && review.IndexOf("EmitCovering(", StringComparison.Ordinal) < review.IndexOf("EmitReserves(", StringComparison.Ordinal),
            "after the allocation row, before [Reserves]");
        AssertTrue(BodyOf(runtime, "private static void ResetPlanState(").Contains("LastCoveringRow[a] = null;"), "reset with the campaign");
    }

    private static void TheReservesRowCarriesTheCoveringForceAndTheHoldReserve()
    {
        var w = CoverWorld();
        var a = ForceAllocator.Allocate(w.Plan, w.S, w.Sectors, null, null, null);
        string row = ReservesRow.Build(w.S, w.Sectors, w.Plan, a, w.State, null, null);
        AssertContains(row, " covering=18000/18000 hold=", "covering=<held>/<need> after capital=, before hold=");
        AssertContains(row, " holdReserve=", "and the hold reserve after hold=");
        var previous = ForceAllocator.Allocate(w.Plan, w.S, w.Sectors, null, null, null);
        var f = w.S.Own[1];
        f.Flags |= FormationFlags.InDefensiveOps;
        w.S.Own[1] = f;
        string after = ReservesRow.Build(w.S, w.Sectors, w.Plan, a, w.State, previous, w.Plan);
        AssertContains(after, " vanillaDefHoldOrCovering=1 ", "a covering member now in a vanilla defensive operation is counted by the covering reason (it marches: a Redeploy)");
        AssertContains(after, " vanillaDefWithheld=0 ", "and is not a violation");
    }

    private static void TheFlagIsBoundAndReadByTheCoordinator()
    {
        string plugin = System.IO.File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Plugin.cs"));
        AssertContains(plugin, "\"Campaign Planner\", \"Enable Covering Force\", true,", "bound in the planner section, default on");
        string coordinator = FindSource("Strategic/StrategicCoordinator.cs");
        AssertContains(coordinator, "PlannerSettings.CoveringForce = ConfigValue(Plugin.EnableCoveringForce, true);", "refreshed into the planner's settings before every review");
        AssertTrue(PlannerSettings.CoveringForce, "default on");
    }
}
