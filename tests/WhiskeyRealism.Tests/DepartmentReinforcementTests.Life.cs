using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// F8, the lock and the lifecycle: a department is locked while the department-move record holds a formation toward it (read from the record,
// never from the decision counter, and kept in the sidecar so a load does not free it); a formation's home department changes only when it
// arrives; an interrupted pick leaves it the donor's and the next review decides afresh; and the allocator reads the same table the plan made.
internal static partial class DepartmentReinforcementTests
{
    private static (string name, Action run)[] LifeCases()
    {
        return new (string name, Action run)[]
        {
            ("ADepartmentIsLockedWhileAReinforcementMovesTowardIt", ADepartmentIsLockedWhileAReinforcementMovesTowardIt),
            ("TheLockReleasesOnArrivalOrARoleChange", TheLockReleasesOnArrivalOrARoleChange),
            ("AQuietPeriodLocksNothingByDecision", AQuietPeriodLocksNothingByDecision),
            ("D6KeepsItsDecisionLock", D6KeepsItsDecisionLock),
            ("TheLockIsHeldThroughABattleOnTheMarch", TheLockIsHeldThroughABattleOnTheMarch),
            ("ALoadKeepsTheLock", ALoadKeepsTheLock),
            ("DepartmentMovesRoundTripInTheSidecar", DepartmentMovesRoundTripInTheSidecar),
            ("TheHomeDepartmentChangesOnlyOnArrival", TheHomeDepartmentChangesOnlyOnArrival),
            ("AnInterruptedPickStaysTheDonors", AnInterruptedPickStaysTheDonors),
            ("TheNextReviewReDecides", TheNextReviewReDecides),
            ("TheTargetIsTheNeediestSectorRecomputedEachReview", TheTargetIsTheNeediestSectorRecomputedEachReview),
            ("AGiveUpFallsBackToHold", AGiveUpFallsBackToHold),
            ("RowFiveReadsTheDepartmentMoves", RowFiveReadsTheDepartmentMoves),
            ("RowFiveClaimsNothingWithoutTheFacts", RowFiveClaimsNothingWithoutTheFacts),
            ("AReceiverWithNoFacingSectorEndsTheMove", AReceiverWithNoFacingSectorEndsTheMove),
            ("TheNewHomeAppliesFromTheNextReview", TheNewHomeAppliesFromTheNextReview),
            ("AMoveContinuesThroughAPush", AMoveContinuesThroughAPush),
            ("AReviewThatSawNoFormationLeavesTheRecordAlone", AReviewThatSawNoFormationLeavesTheRecordAlone),
            ("ACommanderChangeCarriesTheMove", ACommanderChangeCarriesTheMove),
            ("DepartmentMoveClaimAgreesInG3AndG4", DepartmentMoveClaimAgreesInG3AndG4),
        };
    }

    // A real plan over the standard world with a department move in it, for the ownership registry (OwnershipTableTests.PlanInputs).
    internal static TheaterPlan PlanInputWorld(out CampaignSnapshot s, out SectorModel sectors)
    {
        s = Standard();
        sectors = ExecutorFixtures.Sectors(s);
        return Replan(s, new PlannerState());
    }

    private static CampaignSnapshot Moved(CampaignSnapshot s, int id, float x)
    {
        s.Own[s.IndexOfOwn(id)] = Corps(id, x, s.Own[s.IndexOfOwn(id)].Strength);
        return s;
    }

    // The department is locked from the pick on, however many reviews the march takes, and nobody else is picked for it meanwhile.
    private static void ADepartmentIsLockedWhileAReinforcementMovesTowardIt()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        for (int day = 0; day < 3; day++)
        {
            var plan = Replan(Standard(), state);
            AssertEqual("locked", Status(plan, Theater.West), "day " + day + ": the West stays locked");
            AssertEqual("3", string.Join(",", plan.Claims.Where(c => c.Claimant == Claimant.DepartmentMove).Select(c => c.InstanceId)), "day " + day + ": corps 3 alone is under way, though corps 4 is spare");
        }
        AssertEqual(1, Replan(Standard(), new PlannerState()).Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "(control: with no record the same world picks)");
    }

    // The lock is the record as the review finds it. On the arrival review the department still reads locked and the record empties; the next
    // review may pick again. A role change (Recover) empties the record the same way.
    private static void TheLockReleasesOnArrivalOrARoleChange()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        var arrival = Replan(Moved(Standard(), 3, 290f), state);
        AssertEqual("locked", Status(arrival, Theater.West), "the arrival review reads the lock it found");
        AssertEqual(0, state.DepartmentMoves.Count, "and empties the record");
        var next = Replan(Moved(Standard(), 3, 290f), state);
        AssertEqual("donor", Status(next, Theater.West), "the next review picks again (the West holds 5,500 of 6,000): " + DepartmentReinforcement.ShortText(next));

        var other = new PlannerState();
        Replan(Standard(), other);
        var starved = Standard();
        var f = starved.Own[starved.IndexOfOwn(3)];
        f.Supply01 = 0.1f;
        starved.Own[starved.IndexOfOwn(3)] = f;
        var recovering = Replan(starved, other);
        AssertEqual(Claimant.Recover, ClaimOf(recovering, 3).Claimant, "(fixture: corps 3 is recovering)");
        AssertEqual(0, other.DepartmentMoves.Count, "a role change takes it out of the record");
        AssertEqual("donor", Status(Replan(Standard(), other), Theater.West), "and the review after is free to pick");
    }

    // No decision counter: a new decision does not free the lock, and a record-less state is never locked whatever its counter says.
    private static void AQuietPeriodLocksNothingByDecision()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        state.DecisionSerial += 5;
        AssertEqual("locked", Status(Replan(Standard(), state), Theater.West), "a new decision leaves the lock where the record puts it");
        var fresh = new PlannerState { DecisionSerial = 9, ReinforcementKey = "corps3:3", ReinforcementDecision = 9 };
        AssertEqual("donor", Status(Replan(Standard(), fresh), Theater.West), "and D6's lock fields lock no department");
    }

    // D6 keeps its own decision lock and the two do not touch: a quiet pick leaves ReinforcementKey alone, and a main effort's reinforcement
    // leaves the department-move record alone. The West holds 28,000 against 6,000 here and the East's corps is too small for the push alone.
    private static void D6KeepsItsDecisionLock()
    {
        var quiet = new PlannerState();
        Replan(Standard(), quiet);
        AssertTrue(quiet.ReinforcementKey == null && quiet.ReinforcementDecision == -1, "a department pick takes no decision lock");
        var s = WorldWith(10000f, 0f, true, Corps(1, WestX, 20000f), Corps(5, 250f, 8000f), Corps(2, 800f, 3000f));
        var state = new PlannerState();
        var plan = Replan(s, state);
        AssertEqual(EastObjective, plan.MainObjectiveId, "(fixture: the East push) " + Describe(plan));
        AssertTrue(plan.ReinforcementInstanceId >= 0, "D6 draws a West corps as the push's one reinforcement: " + Describe(plan));
        AssertEqual(s.Own[s.IndexOfOwn(plan.ReinforcementInstanceId)].UnitKey, state.ReinforcementKey, "and locks it for the decision");
        AssertEqual(0, state.DepartmentMoves.Count, "without touching the department-move record");
        var again = Replan(s, state);
        AssertEqual(plan.ReinforcementInstanceId, again.ReinforcementInstanceId, "the next review of the decision keeps the same one");
    }

    // A battle on the march leaves the formation busy, hence claimed Immobile, but not out of the record: the lock holds, and the march resumes.
    private static void TheLockIsHeldThroughABattleOnTheMarch()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        var fighting = Standard();
        var f = fighting.Own[fighting.IndexOfOwn(3)];
        f.Flags |= FormationFlags.InBattle;
        fighting.Own[fighting.IndexOfOwn(3)] = f;
        var plan = Replan(fighting, state);
        AssertEqual(Claimant.Immobile, ClaimOf(plan, 3).Claimant, "(fixture: corps 3 is busy)");
        AssertEqual(Theater.West, state.DepartmentMoveOf("corps3:3"), "it stays in the record");
        AssertEqual("locked", Status(plan, Theater.West), "so the West stays locked");
        AssertEqual(0, plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "and nobody else is picked while it fights");
        var resumed = Replan(Standard(), state);
        AssertEqual(Claimant.DepartmentMove, ClaimOf(resumed, 3).Claimant, "the review after the battle claims it again");
    }

    // The record is saved by unit key; the formation's instance id is new after a load, and the lock survives it.
    private static void ALoadKeepsTheLock()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        var back = SidecarDtoCodec.PlannerFromDto(JsonConvert.DeserializeObject<PlannerStateDto>(JsonConvert.SerializeObject(SidecarDtoCodec.PlannerToDto(new PlannerSnapshot { State = state }))));
        var loaded = Standard();
        var f = loaded.Own[loaded.IndexOfOwn(3)];
        f.InstanceId = 33;
        loaded.Own[loaded.IndexOfOwn(3)] = f;
        var plan = Replan(loaded, back.State);
        AssertEqual(Claimant.DepartmentMove, ClaimOf(plan, 33).Claimant, "the loaded formation is claimed again by its unit key");
        AssertEqual("locked", Status(plan, Theater.West), "the West is still locked");
        AssertEqual(1, plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "and nobody else is picked");
    }

    private static void DepartmentMovesRoundTripInTheSidecar()
    {
        var state = new PlannerState();
        state.SetDepartmentMoves(new[] { new KeyValuePair<string, Theater>("b:2", Theater.East), new KeyValuePair<string, Theater>("a:1", Theater.West) });
        var dto = SidecarDtoCodec.PlannerToDto(new PlannerSnapshot { State = state });
        AssertEqual("a:1,b:2", string.Join(",", dto.DepartmentMoves.Select(m => m.UnitKey)), "written sorted by unit key, so the same state gives the same file");
        string json = JsonConvert.SerializeObject(dto);
        AssertContains(json, "\"departmentMoves\"", "under the additive field's name");
        var back = SidecarDtoCodec.PlannerFromDto(JsonConvert.DeserializeObject<PlannerStateDto>(json));
        AssertEqual(Theater.West, back.State.DepartmentMoveOf("a:1"), "the receiver of one");
        AssertEqual(Theater.East, back.State.DepartmentMoveOf("b:2"), "and of the other");
        var bad = JsonConvert.DeserializeObject<PlannerStateDto>("{\"departmentMoves\":[{\"unitKey\":\"c:3\",\"theater\":\"Atlantis\"},{\"unitKey\":\"\",\"theater\":\"West\"},{\"unitKey\":\"d:4\",\"theater\":\"Unknown\"}]}");
        AssertEqual(0, SidecarDtoCodec.PlannerFromDto(bad).State.DepartmentMoves.Count, "a record with an unknown theater name, no key or the Unknown theater is skipped");
        AssertEqual(0, SidecarDtoCodec.PlannerFromDto(JsonConvert.DeserializeObject<PlannerStateDto>("{}")).State.DepartmentMoves.Count, "an older file has none");
    }

    // The donor's department stays the formation's home while it marches, and the receiver's becomes its home on arrival only.
    private static void TheHomeDepartmentChangesOnlyOnArrival()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        AssertEqual(Theater.East, state.DepartmentOf("corps3:3"), "the pick transfers nothing");
        Replan(Moved(Standard(), 3, 700f), state);
        AssertEqual(Theater.East, state.DepartmentOf("corps3:3"), "nor does a march that has not arrived (x 700 is still the East sector's)");
        Replan(Moved(Standard(), 3, 290f), state);
        AssertEqual(Theater.West, state.DepartmentOf("corps3:3"), "arrival does");
    }

    // An interrupted pick (a role change) leaves the formation the donor's: its home is the East still, and it is no longer in the record.
    private static void AnInterruptedPickStaysTheDonors()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        var starved = Standard();
        var f = starved.Own[starved.IndexOfOwn(3)];
        f.Supply01 = 0.1f;
        starved.Own[starved.IndexOfOwn(3)] = f;
        var plan = Replan(starved, state);
        AssertEqual(Theater.East, state.DepartmentOf("corps3:3"), "still the East's");
        AssertEqual(0, state.DepartmentMoves.Count, "out of the record");
        AssertEqual(0, plan.DepartmentTransitions.Count, "and no Arrived row");
    }

    private static void TheNextReviewReDecides()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        var starved = Standard();
        var f = starved.Own[starved.IndexOfOwn(3)];
        f.Supply01 = 0.1f;
        starved.Own[starved.IndexOfOwn(3)] = f;
        Replan(starved, state);
        var next = Replan(starved, state);
        AssertEqual(1, next.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "from scratch, one is picked: " + Describe(next));
        AssertEqual(4, next.Claims.First(c => c.Claimant == Claimant.DepartmentMove).InstanceId, "the one the recovering corps left standing: corps 4");
    }

    // The West has a second facing sector (an enemy-held objective at x 600, z -650). The target is whichever sector needs the most that
    // review, so the same move re-aims when the beliefs change.
    private static void TheTargetIsTheNeediestSectorRecomputedEachReview()
    {
        Func<float, float, CampaignSnapshot> day = (a, b) =>
        {
            var s = World(0f, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f));
            s.Areas.Add(new AreaRecord { Index = 50, X = 600f, Z = -650f, EdgeX = new[] { 600f }, EdgeZ = new[] { -650f }, Importance = 1f, SupplyCapacity = 100f });
            s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 33, X = 600f, Z = -650f, HolderAlliance = 1, Available = false, Importance01 = 0f });
            s.Beliefs.Add(ExecutorFixtures.Belief(WestX, 0f, a));
            s.Beliefs.Add(ExecutorFixtures.Belief(600f, -650f, b));
            return s;
        };
        var state = new PlannerState();
        var first = Replan(day(10000f, 3000f), state);
        var c1 = ClaimOf(first, 3);
        AssertEqual(Claimant.DepartmentMove, c1.Claimant, "(fixture: corps 3 is picked) " + Describe(first));
        AssertTrue(Math.Abs(c1.TargetX - WestX) < 0.5f && Math.Abs(c1.TargetZ) < 0.5f, "review 1: the sector at x 300 needs 4,500 more, the other 1,800: the target is " + c1.TargetX + "," + c1.TargetZ);
        var second = Replan(day(3000f, 12000f), state);
        var c2 = ClaimOf(second, 3);
        AssertEqual(Claimant.DepartmentMove, c2.Claimant, "review 2: still under way");
        AssertTrue(Math.Abs(c2.TargetX - 600f) < 0.5f && Math.Abs(c2.TargetZ + 650f) < 0.5f, "now the other sector needs the most (7,200 held by nobody): the target is " + c2.TargetX + "," + c2.TargetZ);
    }

    // The tracker gave up on the march: row 5 does not claim it, the record is dropped and a GaveUp row is written; the allocator's table makes
    // it an ordinary Hold.
    private static void AGiveUpFallsBackToHold()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        var gaveUp = new[] { new GaveUpMove { InstanceId = 3, TargetX = WestX, TargetZ = 0f, ObjectiveId = -1 } };
        var s = Standard();
        var plan = Replan(s, state, gaveUp);
        AssertTrue(ClaimOf(plan, 3).Claimant != Claimant.DepartmentMove, "no longer a department move: " + Describe(plan));
        AssertEqual(0, state.DepartmentMoves.Count, "out of the record");
        AssertTrue(plan.DepartmentTransitions.Any(t => t.InstanceId == 3 && t.State == "GaveUp"), "a GaveUp row is written");
        var allocation = TestClaims.Allocate(plan, s, ExecutorFixtures.Sectors(s), null, null, null, gaveUp);
        allocation.TryGet(3, out var a);
        AssertTrue(a.Role != FormationRole.Redeploy, "and the allocator does not send it: " + a.Role);
    }

    // Row 5 claims the formations of the record that are still claimable, whatever the review picked.
    private static void RowFiveReadsTheDepartmentMoves()
    {
        var state = new PlannerState();
        state.SetDepartmentMoves(new[] { new KeyValuePair<string, Theater>("corps4:4", Theater.West) });
        var plan = Replan(Standard(), state);
        var claim = ClaimOf(plan, 4);
        AssertEqual(Claimant.DepartmentMove, claim.Claimant, "corps 4 is in the record: " + Describe(plan));
        AssertEqual("locked", Status(plan, Theater.West), "the West is locked by it");
        AssertEqual(1, plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "and nothing else is picked");
        AssertTrue(Math.Abs(claim.TargetX - WestX) < 0.5f, "bound for the West's neediest sector");
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/DepartmentReinforcement.cs");
        AssertContains(BodyOf(src, "public void Produce("), "state.DepartmentMoveOf(f.UnitKey)", "the source reads the record");
    }

    // A hand-built table (no sector facts) is a world no review made: the row claims nothing, so the harness's hand-made plans are unchanged.
    private static void RowFiveClaimsNothingWithoutTheFacts()
    {
        var s = Standard();
        var sectors = ExecutorFixtures.Sectors(s);
        var state = new PlannerState();
        state.SetDepartmentMoves(new[] { new KeyValuePair<string, Theater>("corps4:4", Theater.West) });
        var plan = TestClaims.For(ExecutorFixtures.HandPlan(s, new int[0]), s, sectors, null, null, state);
        AssertEqual(0, plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "no facts, no claim");
    }

    // The receiver faces nothing any more (its enemy objective is ours and the army is gone): the move ends where it stands, no arrival.
    private static void AReceiverWithNoFacingSectorEndsTheMove()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        var quiet = World(0f, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f));
        quiet.Objectives[0] = new ObjectiveRecord { ObjectiveId = WestObjective, X = WestX, Z = 0f, HolderAlliance = 0, Available = false, Importance01 = 0f };
        var plan = Replan(quiet, state);
        AssertTrue(ClaimOf(plan, 3).Claimant != Claimant.DepartmentMove, "the move is not claimed: " + Describe(plan));
        AssertEqual(0, state.DepartmentMoves.Count, "and leaves the record");
        AssertEqual(Theater.East, state.DepartmentOf("corps3:3"), "without a transfer");
        AssertEqual(0, plan.DepartmentTransitions.Count, "or a row");
    }

    // The review of the arrival still counts the formation in its old department (c.Department is not refreshed); the next counts it in the new.
    private static void TheNewHomeAppliesFromTheNextReview()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        var arrival = Replan(Moved(Standard(), 3, 290f), state);
        AssertEqual("East:3,West:1", arrival.DepartmentCounts, "the arrival review counts the old homes");
        var next = Replan(Moved(Standard(), 3, 290f), state);
        AssertEqual("East:2,West:2", next.DepartmentCounts, "the next counts the new one");
    }

    // A move under way continues through a push; row 8 never takes it, and D6's own reinforcement serves the push.
    private static void AMoveContinuesThroughAPush()
    {
        var state = new PlannerState();
        state.SetDepartmentMoves(new[] { new KeyValuePair<string, Theater>("corps3:3", Theater.West) });
        var plan = Replan(WorldWith(10000f, 0f, true, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f)), state);
        AssertEqual(EastObjective, plan.MainObjectiveId, "(fixture: a push) " + Describe(plan));
        AssertEqual(Claimant.DepartmentMove, ClaimOf(plan, 3).Claimant, "corps 3 keeps marching to the West");
        AssertFalse(plan.MassInstanceIds.Contains(3), "and is in no mass");
        AssertEqual(Theater.West, state.DepartmentMoveOf("corps3:3"), "the record holds it");
        AssertEqual("push", Status(plan, Theater.West), "the West reads push");
    }

    // A review that read no formation of ours (the units are not loaded yet after a load) must not free the lock.
    private static void AReviewThatSawNoFormationLeavesTheRecordAlone()
    {
        var state = new PlannerState();
        state.SetDepartmentMoves(new[] { new KeyValuePair<string, Theater>("corps3:3", Theater.West) });
        Replan(World(0f), state);
        AssertEqual(Theater.West, state.DepartmentMoveOf("corps3:3"), "the record is kept");
    }

    private static void ACommanderChangeCarriesTheMove()
    {
        var state = new PlannerState();
        state.SetDepartmentMoves(new[] { new KeyValuePair<string, Theater>("1st Corps:11", Theater.West) });
        state.Observe(7, "1st Corps:11", Theater.East);
        state.RekeyInstance(7, "1st Corps:99");
        AssertEqual(Theater.Unknown, state.DepartmentMoveOf("1st Corps:11"), "the old key is gone");
        AssertEqual(Theater.West, state.DepartmentMoveOf("1st Corps:99"), "the appointment moved the move to the new key");
        var other = new PlannerState();
        other.SetDepartmentMoves(new[] { new KeyValuePair<string, Theater>("2nd Corps:12", Theater.East) });
        other.Observe(8, "2nd Corps:12", Theater.East);
        other.Observe(8, "2nd Corps:55", Theater.East);
        AssertEqual(Theater.East, other.DepartmentMoveOf("2nd Corps:55"), "an observation of the new key carries it by instance id");
    }

    // The table the plan made is the table the allocator reads: the claim is a Redeploy of reason department-reinforcement in the receiver's
    // sector, bound for the claim's target, withheld from neither pool by the allocator (the withhold view takes the offensive pool alone).
    private static void DepartmentMoveClaimAgreesInG3AndG4()
    {
        var s = Standard();
        var sectors = ExecutorFixtures.Sectors(s);
        var plan = Replan(s, new PlannerState());
        var claim = ClaimOf(plan, 3);
        var allocation = ForceAllocator.Allocate(plan, s, sectors, null, null, null);
        AssertTrue(allocation.TryGet(3, out var a), "the allocator assigns it");
        AssertEqual(FormationRole.Redeploy, a.Role, "a Redeploy");
        AssertEqual(ForceAllocator.ReasonDepartmentReinforcement, a.Reason, "of reason department-reinforcement");
        AssertEqual(sectors.SectorAt(claim.TargetX, claim.TargetZ), a.SectorId, "in the receiver's sector");
        AssertTrue(Math.Abs(a.TargetX - claim.TargetX) < 0.01f && Math.Abs(a.TargetZ - claim.TargetZ) < 0.01f, "bound for the claim's target");
        AssertFalse(a.Withheld, "withheld from neither pool by the allocator");
        AssertEqual(1, allocation.Assignments.Count(x => x.Role == FormationRole.Redeploy), "and it is the only one");
        AssertContains(ForceAllocator.Summary(allocation, plan, PlayerCommandClass.NotCareer, 1f, 0), " redeploy=1 ", "the [Allocation] row counts it");
    }
}
