using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

using static TestHarness;
using static TestFixtures;

// Stage G strategic foresight, slice F8 (D11 requirement-department-reinforcement; decision 5): with no push, the shortest department gets one
// formation that stands outside it, chosen with D6's own selection, under a lock read from the formations in a department move (kept in
// PlannerState and the sidecar). Plan: docs/superpowers/plans/2026-10-18-strategic-foresight.md F8; the decisions are docs/loop/foresight/
// decisions.md (phase 2, rounds 2-4 and the pass 12b decisions). This file holds the world, the ranking and the pick; DepartmentReinforcementTests
// .Pool.cs the donor test and the pool; DepartmentReinforcementTests.Life.cs the lock, the lifecycle, the sidecar and the allocator's half.
//
// The world is Line(0, 11): areas every 100 from x 0 to 1000, relocation range 150, so the objectives at x 300 and x 900 seed two sectors. The
// West sector (areas 0-6, centroid x 300) classifies West; the East sector (areas 7-10, centroid x 850) East (TheaterClassifier). Both
// objectives are the enemy's and unavailable, so the review has no candidate and no main effort: a quiet review. A belief of 10,000 at x 300
// makes the West need 6,000; the East faces enemy ground and needs the minimum strength (2,000). No capital, so there is no capital reserve.
internal static partial class DepartmentReinforcementTests
{
    public static void Run()
    {
        var all = new List<(string name, Action run)>();
        all.AddRange(PickCases());
        all.AddRange(PoolCases());
        all.AddRange(LifeCases());
        TestHarness.RunAll(all.ToArray());
    }

    // ---- fixtures ----

    internal const int WestObjective = 30, EastObjective = 31;
    internal const float WestX = 300f;

    internal static FormationRecord Corps(int id, float x, float strength, int readiness = 3)
    {
        var f = ExecutorFixtures.Corps(id, x, strength);
        f.ReadinessStep = readiness;
        f.Morale = 0.9f;
        return f;
    }

    // The quiet two-department world. `threat` is the believed army at the West objective (0: none); the formations are given in order.
    internal static CampaignSnapshot World(float threat, params FormationRecord[] own)
    {
        return WorldWith(threat, 0f, false, own);
    }

    // The same, with a believed army of `eastThreat` at the East objective (0: none) and, with `eastOpen`, that objective offered as a target
    // (the review then has a push to judge).
    internal static CampaignSnapshot WorldWith(float threat, float eastThreat, bool eastOpen, params FormationRecord[] own)
    {
        var s = ExecutorFixtures.Line(0, 11);
        s.MinStrengthForMovement = 2000f;
        s.MinMoraleForMovement = 0.3f;
        s.HasCapital = false;
        for (int i = 1; i <= 9; i++) s.Supply.Add(ExecutorFixtures.Iip(600 + i, 100f * i, 0));
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = WestObjective, X = WestX, Z = 0f, HolderAlliance = 1, Available = false, Importance01 = 0f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = EastObjective, X = 900f, Z = 0f, HolderAlliance = 1, Available = eastOpen, Importance01 = eastOpen ? 1f : 0f });
        if (threat > 0f) s.Beliefs.Add(ExecutorFixtures.Belief(WestX, 0f, threat));
        if (eastThreat > 0f) s.Beliefs.Add(ExecutorFixtures.Belief(900f, 0f, eastThreat));
        foreach (var f in own) s.Own.Add(f);
        return s;
    }

    // The standard roster: a small West corps and three East corps. The East's hold reserve is the corps nearest the army the sector faces,
    // which is the only belief on the map, the one at x 300: corps 2 (x 800). Corps 3 (x 820) is then the soonest donor, corps 4 (x 840) the next.
    internal static CampaignSnapshot Standard(float threat = 10000f)
    {
        return World(threat, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f));
    }

    internal static TheaterPlan Replan(CampaignSnapshot s, PlannerState state, IReadOnlyList<GaveUpMove> gaveUp = null, IReadOnlyList<HeldBuilder> held = null,
                                       IReadOnlyCollection<int> garrisonIncumbents = null, IReadOnlyCollection<int> reconIncumbents = null,
                                       IReadOnlyList<DispatchedMove> dispatched = null)
    {
        return TheaterPlanner.Plan(s, ExecutorFixtures.Sectors(s), null, state, null, null, ExecutorFixtures.Speed(), out _, out EnemyPicture _, gaveUp, held,
            null, dispatched, garrisonIncumbents, reconIncumbents);
    }

    internal static Claim ClaimOf(TheaterPlan plan, int instanceId)
    {
        return plan.Claims.First(c => c.InstanceId == instanceId);
    }

    internal static string Describe(TheaterPlan plan)
    {
        return string.Join(" ", plan.Claims.Select(c => c.InstanceId + "=" + c.Claimant + (c.Reason != null ? "/" + c.Reason : "")));
    }

    internal static DepartmentShortfall ShortOf(TheaterPlan plan, Theater department)
    {
        foreach (DepartmentShortfall d in plan.DepartmentShort)
            if (d.Theater == department) return d;
        throw new Exception("no " + department + " in deptShort: " + string.Join(",", plan.DepartmentShort.Select(d => d.Theater + ":" + d.Status)));
    }

    internal static string Status(TheaterPlan plan, Theater department)
    {
        return ShortOf(plan, department).Status;
    }

    internal static T WithFlag<T>(bool on, Func<T> run)
    {
        bool before = PlannerSettings.DepartmentReinforcement;
        PlannerSettings.DepartmentReinforcement = on;
        try { return run(); }
        finally { PlannerSettings.DepartmentReinforcement = before; }
    }

    // The world's first line: the two departments exist, as the sectors classify them.
    private static void AssertTwoDepartments(CampaignSnapshot s)
    {
        var sectors = ExecutorFixtures.Sectors(s);
        AssertEqual(2, sectors.Count, "the world has two sectors");
        AssertEqual(Theater.West, sectors.TheaterOf(sectors.SectorAt(WestX, 0f)), "the sector of x 300 is West");
        AssertEqual(Theater.East, sectors.TheaterOf(sectors.SectorAt(900f, 0f)), "the sector of x 900 is East");
    }

    // The pass's inputs over a world, for the pure functions: departments assigned, the sector facts built, a table with nobody claimed that
    // `claim` may write into.
    internal static TheaterPlanner.Inputs InputsOf(CampaignSnapshot s, Action<Claim[]> claim = null, PlannerState state = null)
    {
        state = state ?? new PlannerState();
        var c = new TheaterPlanner.Inputs { S = s, Sectors = ExecutorFixtures.Sectors(s), Speed = ExecutorFixtures.Speed(), State = state };
        TheaterPlanner.AssignDepartments(c, state);
        c.Facts = TheaterPlanner.BuildSectorFacts(c);
        c.OwnIips = MarchTime.OwnIips(s);
        c.Claims = ClaimTable.Fresh(s);
        claim?.Invoke(c.Claims);
        return c;
    }

    // ---- the pick ----

    private static (string name, Action run)[] PickCases()
    {
        return new (string name, Action run)[]
        {
            ("TheWorldHasTwoDepartments", () => AssertTwoDepartments(Standard())),
            ("TheShortestDepartmentGetsOneFormation", TheShortestDepartmentGetsOneFormation),
            ("TheLockHoldsTheMoveAcrossThreeReviews", TheLockHoldsTheMoveAcrossThreeReviews),
            ("OnlyTheShortestIsTried", OnlyTheShortestIsTried),
            ("AtMostOnePickPerReview", AtMostOnePickPerReview),
            ("NoPickDuringAPush", NoPickDuringAPush),
            ("AStalledPushDrawsNoDonor", AStalledPushDrawsNoDonor),
            ("WorksWithNoMainEffort", WorksWithNoMainEffort),
            ("FlagOffClearsTheDepartmentMoves", FlagOffClearsTheDepartmentMoves),
            ("OneHaveForRankDonorAndRow", OneHaveForRankDonorAndRow),
            ("HaveCountsWhereFormationsStand", HaveCountsWhereFormationsStand),
            ("HaveExcludesTheMass", HaveExcludesTheMass),
            ("ADepartingFormationCountsInNoHave", ADepartingFormationCountsInNoHave),
            ("TwoReceiversNeverStripOneDonor", TwoReceiversNeverStripOneDonor),
            ("D6sDepartmentHoldMetUsesDepartmentHave", D6sDepartmentHoldMetUsesDepartmentHave),
            ("D6TestsTheDepartmentTheReinforcementStandsIn", D6TestsTheDepartmentTheReinforcementStandsIn),
            ("DepartmentShortStatusesAfterThePick", DepartmentShortStatusesAfterThePick),
            ("NodonorMeansNoFormationPassesThePool", NodonorMeansNoFormationPassesThePool),
            ("DaysIsTheEstimateAtThePick", DaysIsTheEstimateAtThePick),
            ("TheRowsCarryTheMoveAndTheShortfall", TheRowsCarryTheMoveAndTheShortfall),
            ("RowTenRunsBeforeTheTableIsCopiedAndTheStateIsWrittenAfterDecide", RowTenRunsBeforeTheTableIsCopiedAndTheStateIsWrittenAfterDecide),
        };
    }

    // The tracer: one real Plan over the quiet world. The West holds 1,500 against 6,000; the East holds 12,000 against 2,000. The soonest
    // East corps that the East's own hold does not keep and can spare (corps 3, x 820, 6.3 march-days off) is claimed DepartmentMove, bound for
    // the West's neediest facing sector, and recorded in the plan state.
    private static void TheShortestDepartmentGetsOneFormation()
    {
        var s = Standard();
        var state = new PlannerState();
        var plan = Replan(s, state);
        AssertEqual(-1, plan.MainObjectiveId, "a quiet review: " + Describe(plan));
        var claim = ClaimOf(plan, 3);
        AssertEqual(Claimant.DepartmentMove, claim.Claimant, "the soonest donor is owned by row 10: " + Describe(plan));
        AssertNear(WestX, claim.TargetX, 0.5f, "bound for the West sector's centre");
        AssertNear(0f, claim.TargetZ, 0.5f, "on the line");
        AssertEqual(Theater.West, state.DepartmentMoveOf("corps3:3"), "recorded in the plan state, by unit key");
        AssertEqual(1, state.DepartmentMoves.Count, "one formation");
        AssertEqual(Theater.East, state.DepartmentOf("corps3:3"), "its home department is still the East");
        var west = ShortOf(plan, Theater.West);
        AssertEqual("donor", west.Status, "the West was given a formation");
        AssertTrue(west.Have < west.Need, "short: " + west.Have + " of " + west.Need);
        AssertEqual(1, plan.DepartmentTransitions.Count, "one transition");
        var t = plan.DepartmentTransitions[0];
        AssertTrue(t.InstanceId == 3 && t.State == "Picked" && t.From == Theater.East && t.To == Theater.West, "Picked East to West: " + t.State);
        AssertNear(WestX, t.TargetX, 0.5f, "with the target");
        AssertTrue(t.Days > 0f && t.Days <= TheaterPlanner.ReinforcementMaxDays, "and the days of the estimate: " + t.Days);
        int moving = plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove);
        AssertEqual(1, moving, "at most one pick a review");
    }

    // Three reviews on one state: the pick; the next review with the formation still in the East (row 5 re-claims it, the West reads locked and
    // the East's have no longer counts it); the review it stands in the West (it arrives: its home department changes and the record empties).
    private static void TheLockHoldsTheMoveAcrossThreeReviews()
    {
        var state = new PlannerState();
        var first = Replan(Standard(), state);
        AssertEqual(Claimant.DepartmentMove, ClaimOf(first, 3).Claimant, "review 1 picks corps 3");

        var second = Replan(Standard(), state);
        AssertEqual(Claimant.DepartmentMove, ClaimOf(second, 3).Claimant, "review 2: row 5 claims it again: " + Describe(second));
        AssertEqual("locked", Status(second, Theater.West), "the West is locked while the move is under way");
        AssertEqual(1, second.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "and nobody else is picked");
        AssertEqual(0, second.DepartmentTransitions.Count, "no transition on a review that only continues it");

        var arrived = Standard();
        arrived.Own[2] = Corps(3, 290f, 4000f);
        var third = Replan(arrived, state);
        AssertEqual(Theater.West, state.DepartmentOf("corps3:3"), "review 3: it stands in the West sector, so its home department changes");
        AssertEqual(0, state.DepartmentMoves.Count, "and the record is empty");
        AssertTrue(third.DepartmentTransitions.Any(t => t.InstanceId == 3 && t.State == "Arrived" && t.To == Theater.West), "Arrived is reported");
        var arrival = third.DepartmentTransitions.First(t => t.State == "Arrived");
        AssertEqual(Theater.East, arrival.From, "from the department it left");
        AssertEqual(-1f, arrival.Days, "with no march days: they are the pick's alone");
        AssertContains(DepartmentReinforcement.Row(arrival, 0), " days=- state=Arrived", "and the row says so");
        AssertEqual("locked", Status(third, Theater.West), "the arrival review still reads the lock as the review found it; the next review may pick");
    }

    // The third department, TransMiss (x < -200): an enemy-held objective at x -400 seeds a sector of its own, 400 from the West's last area.
    private static CampaignSnapshot WithTransMiss(CampaignSnapshot s)
    {
        s.Areas.Add(new AreaRecord { Index = 40, X = -400f, Z = 0f, EdgeX = new[] { -400f }, EdgeZ = new[] { 0f }, Importance = 1f, SupplyCapacity = 100f });
        s.Objectives.Add(new ObjectiveRecord { ObjectiveId = 32, X = -400f, Z = 0f, HolderAlliance = 1, Available = false, Importance01 = 0f });
        return s;
    }

    // The shortest department is the one tried, and only that one: TransMiss (100 held of 2,000) ranks before the West (1,500 of 6,000), no East
    // corps is within ten march-days of it (1,220 off, 15 days), so it reads nodonor and the West, whom a donor could serve, is untried.
    private static void OnlyTheShortestIsTried()
    {
        var s = WithTransMiss(Standard());
        s.Own.Add(Corps(5, -400f, 100f));
        AssertEqual(Theater.TransMiss, ExecutorFixtures.Sectors(s).TheaterOf(ExecutorFixtures.Sectors(s).SectorAt(-400f, 0f)), "(fixture: x -400 is TransMiss)");
        var plan = Replan(s, new PlannerState());
        AssertEqual(Theater.TransMiss, plan.DepartmentShort[0].Theater, "TransMiss is the shortest: " + DepartmentReinforcement.ShortText(plan));
        AssertEqual("nodonor", plan.DepartmentShort[0].Status, "it is tried and no formation passes the pool");
        AssertEqual("untried", Status(plan, Theater.West), "the West is not tried after it: there is no fall-through");
        AssertEqual(0, plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "and nobody is picked: " + Describe(plan));
    }

    // Two donors would serve the West; one is picked.
    private static void AtMostOnePickPerReview()
    {
        var plan = Replan(Standard(), new PlannerState());
        AssertEqual(1, plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "corps 3 and corps 4 are both donors, and one is picked: " + Describe(plan));
        AssertEqual(1, plan.DepartmentTransitions.Count(t => t.State == "Picked"), "one Picked row");
    }

    // A push: the East objective is offered and the East's corps can take it. The review has a main effort, so every short department reads push
    // and no formation is picked.
    private static void NoPickDuringAPush()
    {
        var plan = Replan(WorldWith(10000f, 0f, true, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f)), new PlannerState());
        AssertEqual(EastObjective, plan.MainObjectiveId, "the review has a main effort at the East objective: " + Describe(plan));
        AssertEqual("push", Status(plan, Theater.West), "the West reads push");
        AssertEqual(0, plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "and nobody is picked during a push: " + Describe(plan));
        AssertEqual(0, plan.DepartmentTransitions.Count, "no transition");
    }

    // A push kept through the odds deadband is still a push: a second review with the odds just under the required ratio keeps the main effort
    // (BelowOddsDays 1 of SwitchDays), and a short West draws no donor from it.
    private static void AStalledPushDrawsNoDonor()
    {
        var state = new PlannerState();
        Func<float, CampaignSnapshot> day = enemy => WorldWith(10000f, enemy, true, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f));
        var first = Replan(day(0f), state);
        AssertEqual(EastObjective, first.MainObjectiveId, "day 1 pushes the East objective");
        var second = Replan(day(11500f), state);
        AssertEqual(EastObjective, second.MainObjectiveId, "day 2 keeps it: " + second.SwitchReason + " below=" + state.BelowOddsDays);
        AssertEqual(1, state.BelowOddsDays, "through the odds deadband (the push is stalled, not ended)");
        AssertEqual("push", Status(second, Theater.West), "so the West still reads push");
        AssertEqual(0, second.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "and draws no donor: " + Describe(second));
    }

    // Main=none: the pick works with no main effort, and the plan row says so.
    private static void WorksWithNoMainEffort()
    {
        var plan = Replan(Standard(), new PlannerState());
        AssertEqual("none", TheaterPlanner.MainLabel(plan), "main=none");
        AssertEqual(1, plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "and one formation is picked");
    }

    // Enable Department Reinforcement off: no pick, no row 5, no deptShort, and the record is cleared with the formations back to the holds.
    private static void FlagOffClearsTheDepartmentMoves()
    {
        var state = new PlannerState();
        Replan(Standard(), state);
        AssertEqual(1, state.DepartmentMoves.Count, "fixture: a move is under way");
        var off = WithFlag(false, () => Replan(Standard(), state));
        AssertEqual(0, state.DepartmentMoves.Count, "off clears the record");
        AssertEqual(0, off.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "claims nobody");
        AssertEqual(0, off.DepartmentShort.Count, "publishes no shortfall");
        AssertEqual(0, off.DepartmentTransitions.Count, "and no transition");
        AssertEqual(Claimant.Hold, ClaimOf(off, 3).Claimant == Claimant.Hold ? Claimant.Hold : ClaimOf(off, 3).Claimant == Claimant.None ? Claimant.Hold : ClaimOf(off, 3).Claimant,
            "the formation falls to the holds (or nobody) where it stands: " + Describe(off));
        var again = Replan(Standard(), state);
        AssertEqual(1, again.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "back on, the next review decides afresh");
    }

    // ---- have ----

    private static void OneHaveForRankDonorAndRow()
    {
        var s = Standard();
        var state = new PlannerState();
        var plan = Replan(s, state);
        var sectors = ExecutorFixtures.Sectors(s);
        var table = plan.Claims.ToArray();
        AssertNear(DepartmentReinforcement.Have(s, sectors, table, Theater.West), ShortOf(plan, Theater.West).Have, 0.01f, "the ranking's have is the one function");
        AssertNear(1500f, ShortOf(plan, Theater.West).Have, 0.01f, "(1,500 stand in the West)");
        var c = InputsOf(s, claims => { for (int i = 0; i < claims.Length; i++) claims[i] = table[i]; }, state);
        int idx = s.IndexOfOwn(4);
        bool met = TheaterPlanner.DepartmentHoldMet(c, Theater.East, idx);
        AssertEqual(DepartmentReinforcement.Have(s, sectors, table, Theater.East, idx) >= TheaterPlanner.DepartmentNeed(c, Theater.East), met, "the donor test is the same have less the formation");
        AssertContains(DepartmentReinforcement.ShortText(plan), "West:6000/1500:donor", "the row prints it");
        // One definition: nothing else sums the strength standing in a department.
        string src = ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs");
        AssertContains(BodyOf(src, "internal static bool DepartmentHoldMet("), "DepartmentReinforcement.Have(", "D6's test reads the one have");
    }

    // A formation's department for the have is where it stands: a West-home corps standing in the East is the East's.
    private static void HaveCountsWhereFormationsStand()
    {
        var s = Standard();
        var state = new PlannerState();
        state.Observe(2, s.Own[1].UnitKey, Theater.East);
        state.Transfer(s.Own[1].UnitKey, Theater.West);
        var c = InputsOf(s, null, state);
        AssertEqual(Theater.West, state.DepartmentOf(s.Own[1].UnitKey), "(fixture: corps 2's home is West)");
        AssertNear(12000f, DepartmentReinforcement.Have(s, c.Sectors, c.Claims, Theater.East), 0.01f, "the East's have counts the three corps that stand there");
        AssertNear(1500f, DepartmentReinforcement.Have(s, c.Sectors, c.Claims, Theater.West), 0.01f, "the West's only the one");
    }

    private static void HaveExcludesTheMass()
    {
        var s = Standard();
        var c = InputsOf(s, claims =>
        {
            claims[1].Claimant = Claimant.Mass;
            claims[2].Claimant = Claimant.Committed;   // a push that ended: its corps waits where it stands, and counts there
            claims[3].Claimant = Claimant.Capital;
        });
        AssertNear(8000f, DepartmentReinforcement.Have(s, c.Sectors, c.Claims, Theater.East), 0.01f, "the mass leaves the have; a committed-wait corps and a capital guard stay");
    }

    // A formation leaving for another department or for supply is in no have from the first review it is claimed so, while it still stands.
    private static void ADepartingFormationCountsInNoHave()
    {
        var s = Standard();
        var c = InputsOf(s, claims =>
        {
            claims[2].Claimant = Claimant.DepartmentMove;
            claims[3].Claimant = Claimant.Recover;
        });
        AssertNear(4000f, DepartmentReinforcement.Have(s, c.Sectors, c.Claims, Theater.East), 0.01f, "only corps 2 is left in the East's have");
        var none = InputsOf(s);
        AssertNear(12000f, DepartmentReinforcement.Have(s, none.Sectors, none.Claims, Theater.East), 0.01f, "(control: unclaimed, all three count)");
    }

    // East 12,000 believed at its objective: its need is 7,200. Corps 3 leaves for the West; while it still marches inside the East a second
    // receiver's donor test reads the East without it: 8,000 less another 4,000 is under 7,200.
    private static void TwoReceiversNeverStripOneDonor()
    {
        var s = WorldWith(10000f, 12000f, false, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f));
        var idle = InputsOf(s);
        AssertNear(7200f, TheaterPlanner.DepartmentNeed(idle, Theater.East), 1f, "(fixture: the East needs 7,200)");
        AssertTrue(TheaterPlanner.DepartmentHoldMet(idle, Theater.East, s.IndexOfOwn(4)), "alone, the East can spare a corps");
        var departing = InputsOf(s, claims => claims[2].Claimant = Claimant.DepartmentMove);
        AssertFalse(TheaterPlanner.DepartmentHoldMet(departing, Theater.East, s.IndexOfOwn(4)), "with corps 3 already on its way out, it cannot spare a second");
        // And through the planner: the next review keeps the West locked and takes nobody else.
        var state = new PlannerState();
        var first = Replan(s, state);
        AssertEqual(1, first.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "the first review picks one");
        var second = Replan(s, state);
        AssertEqual(1, second.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "the second takes nobody else: " + Describe(second));
    }

    private static void D6sDepartmentHoldMetUsesDepartmentHave()
    {
        var s = WorldWith(10000f, 12000f, false, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f));
        var plain = InputsOf(s);
        AssertTrue(TheaterPlanner.DepartmentHoldMet(plain, Theater.East, s.IndexOfOwn(2)), "12,000 less 4,000 meets 7,200");
        var moving = InputsOf(s, claims => { claims[2].Claimant = Claimant.DepartmentMove; claims[3].Claimant = Claimant.Mass; });
        AssertFalse(TheaterPlanner.DepartmentHoldMet(moving, Theater.East, s.IndexOfOwn(2)), "D6 sees the have that leaves out a departing and a massed corps: 4,000 less 4,000 does not");
    }

    // D6's reinforcement asks the donor test of the department the candidate STANDS in (the shipped call asked its home). Corps 2 is the West's by
    // home, which holds 20,000 beside it, and stands in the East: the East's own hold decides.
    private static void D6TestsTheDepartmentTheReinforcementStandsIn()
    {
        foreach (var (rest, expected) in new[] { (4000f, true), (2000f, false) })
        {
            var s = WorldWith(10000f, 12000f, true, Corps(1, WestX, 20000f), Corps(2, 800f, 4000f), Corps(3, 820f, rest), Corps(4, 840f, rest));
            var state = new PlannerState();
            state.Observe(2, s.Own[s.IndexOfOwn(2)].UnitKey, Theater.East);
            state.Transfer(s.Own[s.IndexOfOwn(2)].UnitKey, Theater.West);
            var c = InputsOf(s, null, state);
            AssertNear(7200f, TheaterPlanner.DepartmentNeed(c, Theater.East), 1f, "(fixture: the East needs 7,200)");
            int pick = TheaterPlanner.ReinforcementPick(c, 900f, 0f, new List<int> { s.IndexOfOwn(2) }, null, out _);
            AssertEqual(expected ? s.IndexOfOwn(2) : -1, pick, "the East keeps " + (4000f + 2 * rest) + " without corps 2 against 7,200, whatever the West holds beside it");
        }
    }

    // The order inside Plan (the pipeline pin of F10b names it whole): row 10 after the garrison's pledges and before the table is copied into the
    // plan (a claim is a struct: one written after the copy never reaches the published table); the department state after Decide, beside the
    // covering state.
    private static void RowTenRunsBeforeTheTableIsCopiedAndTheStateIsWrittenAfterDecide()
    {
        string body = BodyOf(ReadRepoFile("src/WhiskeyRealism/Strategic/Planner/TheaterPlanner.cs"), "IReadOnlyCollection<int> garrisonIncumbents = null, IReadOnlyCollection<int> reconIncumbents = null)\n        {\n            var plan = new TheaterPlan");
        AssertInOrder(body, "TheaterPlanner.Plan", "int main = Decide(", "PostFromOutside(inputs, plan);", "DepartmentReinforcement.Reinforce(inputs, plan, out int departmentPick, out Theater departmentTo);",
            "plan.Claims.AddRange(inputs.Claims);", "CoveringForce.WriteState(inputs, enemyCapitalPush, state);", "DepartmentReinforcement.WriteState(inputs, plan, departmentPick, departmentTo);", "AssignPostures(");
    }

    // Every anchor is one statement of the source, once, each after the one before (the pipeline pins' rule, CampaignExecutorTests.AssertInOrder).
    private static void AssertInOrder(string body, string label, params string[] anchors)
    {
        int previous = -1;
        foreach (string anchor in anchors)
        {
            int at = body.IndexOf(anchor, StringComparison.Ordinal);
            AssertTrue(at >= 0, label + " has the step: " + anchor);
            AssertEqual(at, body.LastIndexOf(anchor, StringComparison.Ordinal), label + " has the step once: " + anchor);
            AssertTrue(at > previous, label + " runs this step after the one before it: " + anchor);
            previous = at;
        }
    }

    // ---- statuses, the empty pool, the days ----

    private static void DepartmentShortStatusesAfterThePick()
    {
        var donor = Replan(Standard(), new PlannerState());
        AssertEqual("donor", Status(donor, Theater.West), "the department that got a formation");
        var state = new PlannerState();
        Replan(Standard(), state);
        AssertEqual("locked", Status(Replan(Standard(), state), Theater.West), "a department a move is under way toward");
        AssertEqual("push", Status(Replan(WorldWith(10000f, 0f, true, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f), Corps(4, 840f, 4000f)), new PlannerState()), Theater.West), "a review with a main effort");
        var s = WithTransMiss(Standard());
        s.Own.Add(Corps(5, -400f, 100f));
        var many = Replan(s, new PlannerState());
        AssertEqual("nodonor", Status(many, Theater.TransMiss), "the first ranked unlocked department, no formation in the pool");
        AssertEqual("untried", Status(many, Theater.West), "a department ranked after the one tried");
        AssertEqual(Theater.TransMiss, many.DepartmentShort[0].Theater, "the shortest is first");
    }

    // Only the East has men to give, and its own hold needs them: no formation passes the pool.
    private static void NodonorMeansNoFormationPassesThePool()
    {
        var s = WorldWith(10000f, 15000f, false, Corps(1, WestX, 1500f), Corps(2, 800f, 4000f), Corps(3, 820f, 4000f));
        var plan = Replan(s, new PlannerState());
        AssertEqual("nodonor", Status(plan, Theater.West), "the East needs 9,000 and has 8,000: " + DepartmentReinforcement.ShortText(plan));
        AssertEqual(0, plan.Claims.Count(c => c.Claimant == Claimant.DepartmentMove), "nobody is picked");
        AssertEqual(0, plan.DepartmentTransitions.Count, "and no row is written");
    }

    // days= is MarchTime's estimate for the pick to the target, rail credited as D6 credits it.
    private static void DaysIsTheEstimateAtThePick()
    {
        var s = Standard();
        var plan = Replan(s, new PlannerState());
        var t = plan.DepartmentTransitions[0];
        var f = s.Own[s.IndexOfOwn(t.InstanceId)];
        float expected = MarchTime.EstimateDays(ExecutorFixtures.Speed(), f, t.TargetX, t.TargetZ, RiverLines.Distance(f.X, f.Z, t.TargetX, t.TargetZ), s, null, MarchTime.OwnIips(s));
        AssertNear(expected, t.Days, 0.001f, "the days of the estimate");
        AssertTrue(t.Days > 5f && t.Days < 8f, "(about 6.3 march-days at 82 a day: " + t.Days + ")");
    }

    // The rows: [DepartmentReinforcement] names the unit, the departments, the target and the days; [Reserves] gains deptShort=.
    private static void TheRowsCarryTheMoveAndTheShortfall()
    {
        var s = Standard();
        var plan = Replan(s, new PlannerState());
        string row = DepartmentReinforcement.Row(plan.DepartmentTransitions[0], 0);
        AssertTrue(row.StartsWith("[DepartmentReinforcement] alliance=0 unit=", StringComparison.Ordinal), "the tag and the alliance: " + row);
        AssertContains(row, " from=East to=West target=300,0 days=", "the departments and the target");
        AssertTrue(row.EndsWith(" state=Picked", StringComparison.Ordinal), "and the step: " + row);
        AssertEqual("DepartmentReinforcement", WhiskeyRealism.Telemetry.TelemetryTagPolicy.Route(row).EventName, "the router files it");
        AssertEqual(WhiskeyRealism.Telemetry.TelemetryCategory.Write, WhiskeyRealism.Telemetry.TelemetryTagPolicy.Route(row).Category, "as a Write");
        var sectors = ExecutorFixtures.Sectors(s);
        var allocation = TestClaims.Allocate(plan, s, sectors, null, null, null);
        string reserves = ReservesRow.Build(s, sectors, plan, allocation, new PlannerState(), null, null);
        AssertContains(reserves, " deptShort=West:6000/1500:donor ", "the [Reserves] row: " + reserves);
        var held = World(0f, Corps(1, WestX, 9000f), Corps(2, 850f, 9000f));
        var quiet = Replan(held, new PlannerState());
        string none = ReservesRow.Build(held, ExecutorFixtures.Sectors(held), quiet, TestClaims.Allocate(quiet, held, ExecutorFixtures.Sectors(held), null, null, null), new PlannerState(), null, null);
        AssertContains(none, " deptShort=none ", "no department short reads none: " + none);
    }


}
