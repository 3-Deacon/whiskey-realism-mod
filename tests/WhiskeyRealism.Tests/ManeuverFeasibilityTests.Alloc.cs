using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static BattleMovementHonestyTests;
using static ManeuverFeasibilityFixtures;
using static CommanderOpportunityTests;

// BC14 task 16: requirement-zero-alloc. After a warm-up the heavy path allocates nothing: a full evaluation, an evaluation in slices, the
// monitor's step and a crossing release. Measured with TestFixtures.BeginAllocationWindow / AllocatedSince (AllocationMeasurementTests).
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] AllocTests() => new (string, Action)[]
    {
        ("bc14 FEAS-ALLOC a warmed evaluation (21 candidates, 6 bodies, a 64-link graph), its slices, the monitor step and a crossing release allocate nothing", FeasAlloc),
    };

    // A river across the front with `crossings` fords along it: one link per ford.
    static FeasField ManyFords(int crossings)
    {
        var fords = new (float lateral, float depth, int id)[crossings];
        for (int i = 0; i < crossings; i++) fords[i] = (-1100f + 36f * i, 250f, Ford);
        var f = new FeasField(BandWithCrossings(240f, 260f, fords));
        f.AddCorps(-300f, 0f, 12000f);
        f.AddCorps(0f, 0f, 11000f);
        f.AddCorps(300f, 0f, 12000f);
        f.AddCorps(0f, -300f, 6000f, deployedReserve: true);
        f.AddEnemy(-400f, 556f, 11000f);
        f.AddEnemy(0f, 556f, 11500f);
        f.AddEnemy(400f, 556f, 11000f);
        f.EnemyHalfExtent = 450f;
        return f;
    }

    static void FeasAlloc()
    {
        FeasField f = ManyFords(60);
        AssertTrue(f.Regions.LinkCount >= 40, "a wide graph: " + f.Regions.LinkCount + " links");
        f.Legs = StraightLegs(f, AllPlans);
        var ledger = new FeasibilityLedger();
        // Warm-up: a full evaluation, the selection, a second evaluation on the selected side, a sliced run and a monitor step.
        FeasibilityInput input = f.Build(AllPlans);
        ManeuverFeasibility.Evaluate(in input, ledger);
        ledger.CaptureSelection(BattlePlanId.ManeuverAndFix, false);
        ManeuverFeasibility.Evaluate(in input, ledger);
        EvaluateSelectedFully(in input, ledger);
        ledger.Assess(null, f.Now);
        long ticks = 0;
        Func<long> clock = () => ticks++;          // one delegate for the run: the runtime passes Stopwatch.GetTimestamp
        while (!ManeuverFeasibility.EvaluateSlice(in input, ledger.Job, ledger, ticks + 1, clock)) { }

        long mark = BeginAllocationWindow();
        ManeuverFeasibility.Evaluate(in input, ledger);
        long full = AllocatedSince(mark);
        AssertEqual(0L, full, "one full evaluation allocated " + full + " bytes");

        ticks = 0;
        mark = BeginAllocationWindow();
        int slices = 1;
        while (!ManeuverFeasibility.EvaluateSlice(in input, ledger.Job, ledger, ticks + 1, clock)) slices++;
        long sliced = AllocatedSince(mark);
        AssertTrue(slices > 1, "the run took more than one slice (" + slices + ")");
        AssertEqual(0L, sliced, "an evaluation in " + slices + " slices allocated " + sliced + " bytes");

        mark = BeginAllocationWindow();
        EvaluateSelectedFully(in input, ledger);
        ledger.Assess(null, f.Now);
        long monitor = AllocatedSince(mark);
        AssertEqual(0L, monitor, "one monitor step allocated " + monitor + " bytes");

        // The work was real: every candidate side was evaluated over a wide crossing graph.
        int sides = 0;
        for (int i = 0; i < AllPlans.Length; i++) sides += ledger.Job.SidesEvaluated[i];
        AssertEqual(21, sides, "21 candidate sides (18 plans, three of them on both sides)");
        AssertTrue(ledger.Job.Graph.Searches > 100, "path searches ran: " + ledger.Job.Graph.Searches);
        AssertTrue(ledger.Job.Graph.Regions.RoutedCount >= 40, "over a graph of " + ledger.Job.Graph.Regions.RoutedCount + " routed links");

        // A Defender's run too: the enemy bodies, the counted crossings, the threats of the selected candidate.
        FeasField d = ManyFords(60);
        d.Stance = BattleStance.Defender;
        d.AddEnemy(1000f, 420f, 5000f);
        d.Legs = StraightLegs(d, DefensivePlans);
        var defender = new FeasibilityLedger();
        FeasibilityInput defence = d.Build(DefensivePlans);
        ManeuverFeasibility.Evaluate(in defence, defender);
        defender.CaptureSelection(BattlePlanId.PreparedDefense, false);
        ManeuverFeasibility.Evaluate(in defence, defender);
        AssertTrue(defender.ThreatCount > 0, "the Defender run counts crossings: " + defender.ThreatCount);
        mark = BeginAllocationWindow();
        ManeuverFeasibility.Evaluate(in defence, defender);
        long defended = AllocatedSince(mark);
        AssertEqual(0L, defended, "a Defender's evaluation allocated " + defended + " bytes");

        // More than MaxEnemyBodies (16) clusters are perceived: the per-cluster sums are pooled too, so the merge down allocates nothing at any count.
        foreach (int enemies in new[] { 24, 60 })
        {
            FeasField crowd = ManyFords(60);
            crowd.Stance = BattleStance.Defender;
            for (int e = 0; e < enemies; e++) crowd.AddEnemy(-1100f + 200f * (e % 12), 420f + 200f * (e / 12), 800f);   // 200 apart: one cluster each
            crowd.Legs = StraightLegs(crowd, DefensivePlans);
            var crowded = new FeasibilityLedger();
            FeasibilityInput crowdInput = crowd.Build(DefensivePlans);
            ManeuverFeasibility.Evaluate(in crowdInput, crowded);
            ManeuverFeasibility.Evaluate(in crowdInput, crowded);
            AssertTrue(crowded.Job.Defender.BodyCount == ManeuverFeasibility.MaxEnemyBodies, enemies + " samples fill all " + ManeuverFeasibility.MaxEnemyBodies + " bodies after the merge, got " + crowded.Job.Defender.BodyCount);
            mark = BeginAllocationWindow();
            ManeuverFeasibility.Evaluate(in crowdInput, crowded);
            long merged = AllocatedSince(mark);
            AssertEqual(0L, merged, "a Defender's evaluation of " + enemies + " perceived samples allocated " + merged + " bytes");
        }

        // A crossing release on pooled arrays: the soonest eligible reserve guards the crossing the enemy reaches first.
        CrossingThreat threat = new CrossingThreat { Link = 3, X = 100f, Z = 200f, Left = true, Sector = 0, GuardX = 10f, GuardZ = 20f, GuardHeading = 90f, EnemyAtCrossing = 0.4f, EnemyAtFlank = 0.7f, CandidateOwnShift = 9f };
        var threats = new[] { threat };
        var shifts = new[] { 9f, 0.2f, 0.9f };
        var state = new ReserveReleaseState();
        var context = new ReserveReleaseContext(0.5f, false, state, null, 10.0, 0.5f, 0.5f, 0.5f, false, threats, 1, shifts, 3);
        DirectChildSnapshot[] snapshots = ResChildren(3);
        var roles = new DirectChildRole[3];
        var axis = new int[3];
        var guards = new GuardPoint[3];
        Array.Clear(guards, 0, guards.Length);
        // Warm-up, then the measured release with the binding cleared (the pooled table is reused).
        roles[0] = DirectChildRole.Main; roles[1] = DirectChildRole.Reserve; roles[2] = DirectChildRole.Reserve;
        DirectChildAllocator.ReleaseReserveToCrossings(roles, axis, guards, context, snapshots);
        AssertEqual(DirectChildRole.RefuseLeft, roles[1], "the release is real: the soonest reserve guards");
        state.UnbindCrossing("c1");
        roles[0] = DirectChildRole.Main; roles[1] = DirectChildRole.Reserve; roles[2] = DirectChildRole.Reserve;
        mark = BeginAllocationWindow();
        DirectChildAllocator.ReleaseReserveToCrossings(roles, axis, guards, context, snapshots);
        long released = AllocatedSince(mark);
        AssertEqual(DirectChildRole.RefuseLeft, roles[1], "the measured release released");
        AssertEqual(0L, released, "a crossing release allocated " + released + " bytes");
    }
}
