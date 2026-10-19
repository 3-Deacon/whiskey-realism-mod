using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// F8, the pool: who may be given to a short department. Row 10's pool is the formations claimed None that are massable, no depot builder, no
// pledge, no retained post incumbent, no scout on an active mission, standing outside the department with no give-up on its sectors; the
// pick among them is D6's selection (soonest within ten days, the lower instance id on a tie), and only a formation whose donor department
// keeps its hold without it. The world is the standard one (DepartmentReinforcementTests.cs): corps 1 holds the West, corps 2 is the East's
// reserve, corps 3 (x 820) and corps 4 (x 840) the donors.
internal static partial class DepartmentReinforcementTests
{
    private static (string name, Action run)[] PoolCases()
    {
        return new (string name, Action run)[]
        {
            ("ThePickUsesD6sSelection", ThePickUsesD6sSelection),
            ("NoDonorBeyondTenDays", NoDonorBeyondTenDays),
            ("ADonorIsFilteredByWhereItStands", ADonorIsFilteredByWhereItStands),
            ("AFormationAlreadyInTheReceiverIsNoDonor", AFormationAlreadyInTheReceiverIsNoDonor),
            ("TheDonorsHoldStaysMet", TheDonorsHoldStaysMet),
            ("AGiveUpInTheReceiverIsNoDonor", AGiveUpInTheReceiverIsNoDonor),
            ("NeverAReserveMassCoveringGarrisonOrBuilder", NeverAReserveMassCoveringGarrisonOrBuilder),
            ("NeverThePlayersCommandOrMapGroup", NeverThePlayersCommandOrMapGroup),
            ("AnUnreadyOrStarvedFormationIsNoDonor", AnUnreadyOrStarvedFormationIsNoDonor),
            ("AGarrisonIncumbentIsNeverADonor", AGarrisonIncumbentIsNeverADonor),
            ("AReleasedPostsHolderMayDonate", AReleasedPostsHolderMayDonate),
            ("AReconMissionIsNeverADonor", AReconMissionIsNeverADonor),
            ("ASecessionPledgeIsNeverADonor", ASecessionPledgeIsNeverADonor),
            ("AnEndedPushsCorpsMayDonate", AnEndedPushsCorpsMayDonate),
        };
    }

    private static int Picked(TheaterPlan plan)
    {
        var moving = plan.Claims.Where(c => c.Claimant == Claimant.DepartmentMove).Select(c => c.InstanceId).ToList();
        return moving.Count == 1 ? moving[0] : moving.Count == 0 ? -1 : -2;
    }

    // D6's own selection, one function for both paths: the soonest within ten days, the lower instance id on a tie; the department path calls
    // the function the main effort's reinforcement calls.
    private static void ThePickUsesD6sSelection()
    {
        AssertEqual(3, Picked(Replan(Standard(), new PlannerState())), "the nearer of the two spare corps, 6.3 days off against 6.5");
        var tie = World(10000f, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(5, 830f, 4000f), Corps(4, 830f, 4000f));
        AssertEqual(4, Picked(Replan(tie, new PlannerState())), "two corps the same days off: the lower instance id");
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/DepartmentReinforcement.cs");
        AssertContains(BodyOf(src, "private static int PickFor("), "TheaterPlanner.ReinforcementPick(", "the department path calls D6's function");
        string planner = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs");
        AssertContains(BodyOf(planner, "internal static float MassFor("), "ReinforcementPick(c, tx, tz, c.ForeignScratch, LockedReinforcement(c), out _)", "and so does the main effort's reinforcement, with its decision lock");
        AssertContains(BodyOf(planner, "internal static int ReinforcementPick("), "MarchTime.EstimateDays(c.Speed, f, tx, tz, distance, s, null, c.OwnIips)", "both estimate the march with the rail leg credited");
    }

    // A corps that needs more than ten days is no donor, however near the others are in men.
    private static void NoDonorBeyondTenDays()
    {
        Func<FormationRecord, FormationRecord> slow = f => { f.GroupSpeed = 0.5f; return f; };
        var s = World(10000f, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), slow(Corps(3, 820f, 4000f)), slow(Corps(4, 840f, 4000f)));
        var plan = Replan(s, new PlannerState());
        AssertEqual(-1, Picked(plan), "twelve days or more: " + Describe(plan));
        AssertEqual("nodonor", Status(plan, Theater.West), "the West reads nodonor");
    }

    // The department a formation leaves is the one it stands in. Corps 2 is the West's by home and the only unclaimed corps (the East's need of
    // 7,200 keeps 3 and 4 as its hold): by home the West would be left with 1,500 against 6,000 and refuse it, by where it stands the East
    // keeps 8,000 and may give it.
    private static void ADonorIsFilteredByWhereItStands()
    {
        var s = WorldWith(10000f, 12000f, false, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f));
        var state = new PlannerState();
        state.Observe(2, s.Own[s.IndexOfOwn(2)].UnitKey, Theater.East);
        state.Transfer(s.Own[s.IndexOfOwn(2)].UnitKey, Theater.West);
        var plan = Replan(s, state);
        AssertEqual(2, Picked(plan), "the East gives corps 2: " + Describe(plan) + " " + DepartmentReinforcement.ShortText(plan));
    }

    // A corps already in the receiver is no donor: the West has a second sector facing nothing, with an unclaimed corps in it, which would be the
    // soonest of all.
    private static void AFormationAlreadyInTheReceiverIsNoDonor()
    {
        var s = World(20000f, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f), Corps(6, 500f, 5000f));
        s.Areas.Add(new AreaRecord { Index = 41, X = 500f, Z = -400f, EdgeX = new[] { 500f }, EdgeZ = new[] { -400f }, Importance = 1f, SupplyCapacity = 100f });
        var sectors = ExecutorFixtures.Sectors(s);
        var f6 = s.Own[s.IndexOfOwn(6)];
        f6.X = f6.WaypointX = 500f;
        f6.Z = f6.WaypointZ = -400f;
        s.Own[s.IndexOfOwn(6)] = f6;
        sectors = ExecutorFixtures.Sectors(s);
        AssertEqual(Theater.West, sectors.TheaterOf(sectors.SectorAt(500f, -400f)), "(fixture: the second West sector)");
        var plan = Replan(s, new PlannerState());
        AssertEqual(Claimant.None, ClaimOf(plan, 6).Claimant, "(fixture: corps 6 is unclaimed: its sector faces nothing)");
        AssertEqual("donor", Status(plan, Theater.West), "the West is short and given a formation: " + DepartmentReinforcement.ShortText(plan));
        AssertEqual(3, Picked(plan), "an East corps, not the unclaimed West one beside it: " + Describe(plan));
        // The pool itself, asked of the pass's inputs: the donor test would also refuse a corps of the short department (it is short, so taking any
        // man leaves it shorter), and the clause stands on its own.
        var c = InputsOf(s);
        AssertFalse(DepartmentReinforcement.InPool(c, s.IndexOfOwn(6), Theater.West), "corps 6 stands in the West: not in its pool");
        AssertTrue(DepartmentReinforcement.InPool(c, s.IndexOfOwn(3), Theater.West), "corps 3 stands in the East: in it");
    }

    // The donor test, at the pick: East 9,000 stands against a need of 7,200. Corps 3 is nearer, but taking it would leave 5,000; corps 4
    // (1,000) leaves 8,000, so it is the one that may go.
    private static void TheDonorsHoldStaysMet()
    {
        var s = WorldWith(10000f, 12000f, false, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 1000f));
        var c = InputsOf(s);
        AssertNear(7200f, TheaterPlanner.DepartmentNeed(c, Theater.East), 1f, "(fixture: the East needs 7,200)");
        var pool = new List<int> { s.IndexOfOwn(3), s.IndexOfOwn(4) };
        int pick = TheaterPlanner.ReinforcementPick(c, WestX, 0f, pool, null, out float days);
        AssertEqual(s.IndexOfOwn(4), pick, "corps 3 is nearer but the East would fall under its hold without it");
        AssertTrue(days > 0f, "and the days are reported");
        var only = new List<int> { s.IndexOfOwn(3) };
        AssertEqual(-1, TheaterPlanner.ReinforcementPick(c, WestX, 0f, only, null, out _), "with only corps 3 to ask, nobody is picked");
    }

    private static void AGiveUpInTheReceiverIsNoDonor()
    {
        var gaveUp = new[] { new GaveUpMove { InstanceId = 3, TargetX = WestX, TargetZ = 0f, ObjectiveId = -1 } };
        var plan = Replan(Standard(), new PlannerState(), gaveUp);
        AssertEqual(4, Picked(plan), "corps 3 gave up on the West: corps 4 goes instead: " + Describe(plan));
        var elsewhere = new[] { new GaveUpMove { InstanceId = 3, TargetX = 900f, TargetZ = 0f, ObjectiveId = -1 } };
        AssertEqual(3, Picked(Replan(Standard(), new PlannerState(), elsewhere)), "a give-up in the East is no bar to the West");
    }

    // The reserves are claimed before the pick (the pool is claimed None), and a depot builder or a capital guard is never given away.
    private static void NeverAReserveMassCoveringGarrisonOrBuilder()
    {
        var standard = Replan(Standard(), new PlannerState());
        AssertTrue(standard.Claims.Where(c => c.Claimant == Claimant.Hold).All(c => c.Claimant != Claimant.DepartmentMove) && Picked(standard) == 3,
            "the hold reserves stay where they are; only an unclaimed corps goes: " + Describe(standard));
        var held = new[]
        {
            new HeldBuilder { InstanceId = 3, SectorId = 1, TargetX = 820f, TargetZ = 0f, SiteX = 820f, SiteZ = 0f, Wished = false },
            new HeldBuilder { InstanceId = 4, SectorId = 1, TargetX = 840f, TargetZ = 0f, SiteX = 840f, SiteZ = 0f, Wished = false }
        };
        var builders = Replan(Standard(), new PlannerState(), held: held);
        AssertEqual(-1, Picked(builders), "a Marching depot builder is the depot's: " + Describe(builders));
        var pinned = new[] { new HeldBuilder { InstanceId = 3, SectorId = 1, TargetX = 820f, TargetZ = 0f, SiteX = 820f, SiteZ = 0f, Wished = true } };
        AssertEqual(4, Picked(Replan(Standard(), new PlannerState(), held: pinned)), "nor a Wished one (corps 4 goes)");
        var s = Standard();
        s.HasCapital = true;
        s.CapitalX = 840f;
        s.CapitalZ = 0f;
        var capital = Replan(s, new PlannerState());
        AssertTrue(capital.Claims.Any(c => c.Claimant == Claimant.Capital), "(fixture: a capital guard is claimed)");
        AssertTrue(capital.Claims.Where(c => c.Claimant == Claimant.Capital).All(c => c.InstanceId != Picked(capital)), "and it is not the one given away");
        AssertEqual(0, Replan(WorldWith(10000f, 0f, true, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f)), new PlannerState())
            .Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "the mass of a push is never given away");
    }

    private static void NeverThePlayersCommandOrMapGroup()
    {
        foreach (var chain in new[] { WlChainClass.PlayerCommand, WlChainClass.PlayerMapGroup })
        {
            var s = Standard();
            foreach (int id in new[] { 2, 3, 4 })
            {
                var f = s.Own[s.IndexOfOwn(id)];
                f.Chain = chain;
                s.Own[s.IndexOfOwn(id)] = f;
            }
            var plan = Replan(s, new PlannerState());
            AssertEqual(-1, Picked(plan), chain + ": the planner may not move them: " + Describe(plan));
        }
    }

    // The pool is the massable: a corps at readiness step 1, or below the movement morale floor, cannot be sent (vanilla refuses its march
    // onto enemy ground, and a short morale makes it recover); another one goes.
    private static void AnUnreadyOrStarvedFormationIsNoDonor()
    {
        var unready = Standard();
        unready.Own[unready.IndexOfOwn(3)] = Corps(3, 820f, 4000f, readiness: 1);
        var step1 = Replan(unready, new PlannerState());
        AssertTrue(Picked(step1) > 0 && Picked(step1) != 3, "corps 3 is at readiness step 1: another corps goes: " + Describe(step1));
        // The pool itself, with nothing claimed: the readiness rule stands on its own, not on whatever the hold pass made of the corps.
        var c = InputsOf(unready);
        AssertFalse(DepartmentReinforcement.InPool(c, unready.IndexOfOwn(3), Theater.West), "an unready corps is not in the pool");
        AssertTrue(DepartmentReinforcement.InPool(c, unready.IndexOfOwn(4), Theater.West), "(control: a ready one is)");
        var shaken = Standard();
        var f = shaken.Own[shaken.IndexOfOwn(3)];
        f.Morale = 0.1f;
        shaken.Own[shaken.IndexOfOwn(3)] = f;
        var low = Replan(shaken, new PlannerState());
        AssertTrue(Picked(low) > 0 && Picked(low) != 3, "corps 3 is below the morale floor (it recovers): another corps goes: " + Describe(low));
    }

    // A post's retained incumbent and an active scout are another owner's: the plan receives their ids and leaves them out of the pool.
    private static void AGarrisonIncumbentIsNeverADonor()
    {
        AssertEqual(4, Picked(Replan(Standard(), new PlannerState(), garrisonIncumbents: new[] { 3 })), "corps 3 holds a garrison post: corps 4 goes");
        AssertEqual(-1, Picked(Replan(Standard(), new PlannerState(), garrisonIncumbents: new[] { 3, 4 })), "with both held, nobody");
        // The ledger's side: RetainedIncumbents names the members of a record that is threatened or has not been quiet a fortnight.
        var s = Standard();
        var record = new GarrisonPostRecord { Kind = GarrisonPostKind.ObjectiveTown, X = 820f, Z = 0f, AssignedUnitKeys = new[] { "corps3:3" }, QuietSinceDay = s.DaySerial - 3 };
        var into = new List<int>();
        GarrisonPlanner.RetainedIncumbents(s, new[] { record }, true, into);
        AssertEqual("3", string.Join(",", into), "a record quiet three days still holds its formation");
        into.Clear();
        GarrisonPlanner.RetainedIncumbents(s, new[] { record }, false, into);
        AssertEqual(0, into.Count, "Enable Garrison Manning off retains nobody (the ledger releases its posts)");
        record.Kind = GarrisonPostKind.Secession;
        GarrisonPlanner.RetainedIncumbents(s, new[] { record }, true, into);
        AssertEqual(0, into.Count, "a D8 secession record is the plan's own pledge, not read here");
    }

    private static void AReleasedPostsHolderMayDonate()
    {
        var s = Standard();
        var record = new GarrisonPostRecord { Kind = GarrisonPostKind.ObjectiveTown, X = 820f, Z = 0f, AssignedUnitKeys = new[] { "corps3:3" }, QuietSinceDay = s.DaySerial - GarrisonPlanner.ReleaseQuietDays };
        var retained = new List<int>();
        GarrisonPlanner.RetainedIncumbents(s, new[] { record }, true, retained);
        AssertEqual(0, retained.Count, "a post quiet for the whole fortnight is released: nobody is retained");
        AssertEqual(3, Picked(Replan(s, new PlannerState(), garrisonIncumbents: retained)), "so its holder may be given away");
        var beliefs = Standard();
        beliefs.Beliefs.Add(ExecutorFixtures.Belief(830f, 0f, 9000f));
        var threatened = new List<int>();
        GarrisonPlanner.RetainedIncumbents(beliefs, new[] { record }, true, threatened);
        AssertEqual("3", string.Join(",", threatened), "while a belief threatens the post it is held, whatever the quiet days");
    }

    private static void AReconMissionIsNeverADonor()
    {
        AssertEqual(4, Picked(Replan(Standard(), new PlannerState(), reconIncumbents: new[] { 3 })), "a scout on a mission stays");
        var s = Standard();
        var ledger = new ReconMissionLedger();
        Func<int, int, ReconState, ReconMission> mission = (id, instance, state) => new ReconMission
        {
            MissionId = id, Kind = ReconKind.Screen, InstanceId = instance, UnitKey = "corps" + instance + ":" + instance, HoldX = 350f, HoldZ = 0f, SectorId = 0,
            State = state, IssuedDay = s.DaySerial - 1, Reason = "belief", ObservedEnemyStrength = -1f, ObservedObjectiveId = -1, ClearedDay = -1
        };
        ledger.Restore(new[] { mission(1, 3, ReconState.Holding), mission(2, 4, ReconState.Cleared), mission(3, 2, ReconState.Moving), mission(4, 1, ReconState.Recalled) });
        var active = new List<int>();
        ledger.CollectActiveInstances(active);
        AssertEqual("2,3", string.Join(",", active.OrderBy(i => i)), "the formations on an Assigned, Moving or Holding mission");
    }

    // D8's secession pledge: a Union formation inside the live state is pledged to the garrison, and is not given to a department.
    private static void ASecessionPledgeIsNeverADonor()
    {
        const int state = 14;
        var s = Standard();
        s.Secessions.Add(new SecessionRecord { ObjectiveId = 15, StateId = state, StateAlliance = 0, Live = SecessionRules.IsLive(0, 0.6f), HasTown = true, TownX = 820f, TownZ = 0f });
        var f3 = s.Own[s.IndexOfOwn(3)];
        f3.StateId = state;
        s.Own[s.IndexOfOwn(3)] = f3;
        var plan = Replan(s, new PlannerState());
        AssertTrue(plan.Garrisons.Count > 0 && plan.Garrisons[0].InstanceIds.Contains(3), "(fixture: corps 3 is the secession garrison's) " + string.Join(",", plan.Garrisons.SelectMany(g => g.InstanceIds)));
        AssertEqual(4, Picked(plan), "so corps 4 is the one given: " + Describe(plan));
    }

    // requirement-decision-end: a push's corps waits one review (committed-wait) and is unclaimed from the next, when a short department may
    // be given it.
    private static void AnEndedPushsCorpsMayDonate()
    {
        var state = new PlannerState();
        Func<bool, CampaignSnapshot> day = ended => WorldWith(10000f, 0f, !ended, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f));
        var first = Replan(day(false), state);
        AssertEqual(EastObjective, first.MainObjectiveId, "the push goes");
        var dispatched = CommitmentTests.Moves(EastObjective, first.MassInstanceIds.ToArray());
        var end = Replan(day(true), state, dispatched: dispatched);
        AssertEqual(-1, end.MainObjectiveId, "the push ends (the objective is no longer offered): " + end.SwitchReason);
        AssertEqual(0, end.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "the review it ends picks nobody: its corps wait where they stand (" + Describe(end) + ")");
        var after = Replan(day(true), state);
        AssertEqual(1, after.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "the next review places one of them where the West needs it: " + Describe(after) + " " + DepartmentReinforcement.ShortText(after) + " main=" + after.MainObjectiveId);
    }
}
