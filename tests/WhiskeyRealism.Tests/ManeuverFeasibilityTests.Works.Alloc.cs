using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;
using static WorksFixtures;

// BC14W task 10, requirement-zero-alloc's BC14W case (spec FEAS-ALLOC-W): after warm-up the works picture (32 segments, more than 256 focal points), the
// stored-product guard pass over the states and the army's built orders, and the contest's per-group tick with its advancesOnWorks count allocate nothing.
// Green first: the code was written in tasks 3 and 6; the plant "a List allocated per guard pass" is the red control.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksAllocTests() => new (string, Action)[]
    {
        ("bc14w FEAS-ALLOC-W a warmed works picture (32 segments, 256 focal points), the stored-product guard pass and the contest's counts allocate nothing", WAlloc),
    };

    static void WAlloc()
    {
        // ---- the picture: 32 held segments of ten holders each, 50 apart (one profile point apiece: 320 focal points offered, 256 kept)
        var holders = new List<EnemyObservation>();
        for (int segment = 0; segment < 32; segment++)
            for (int k = 0; k < 10; k++)
                holders.Add(Sample(segment * 700f + k * 50f, 600f, 500f));
        BattleSnapshot snapshot = Snapshot(holders.ToArray());
        Bodies(snapshot, out float[] bodyX, out float[] bodyZ, out int bodyCount);
        var picture = new WorksPicture();
        picture.BuildEnemy(snapshot, Axis(), bodyX, bodyZ, bodyCount, null, null);
        AssertEqual(32, picture.SegmentCount, "32 segments");
        AssertEqual(WorksRules.MaxFocalPoints, picture.FocalCount, "256 focal points kept");
        AssertTrue(picture.Dropped > 0, "and the rest counted as dropped: " + picture.Dropped);
        picture.BuildEnemy(snapshot, Axis(), bodyX, bodyZ, bodyCount, null, null);
        long mark = BeginAllocationWindow();
        picture.BuildEnemy(snapshot, Axis(), bodyX, bodyZ, bodyCount, null, null);
        long built = AllocatedSince(mark);
        AssertEqual(0L, built, "a warmed works picture allocated " + built + " bytes");
        mark = BeginAllocationWindow();
        bool onWorks = picture.OnHeldWorks(X(100f), Z(600f)) | picture.InWorksBand(X(100f), Z(500f)) | picture.TryFocal(out _);
        long queried = AllocatedSince(mark);
        AssertTrue(onWorks, "the queries answered");
        AssertEqual(0L, queried, "the picture's queries allocated " + queried + " bytes");

        // ---- the guard pass: the states guarded in place and the built orders guarded into the army's own array
        var lookup = new HoldLookup { Hold = true };
        lookup.Faces.Add("child-2");
        ArmyOrchestrator army = TwoCorpsArmy(lookup);
        TacticalOperationsLedgerRuntime ledger = ActiveLedger();
        army.UpdateOperationsLedger(ledger, FourStates());
        IReadOnlyList<CommandDoctrineOrder> orders = ledger.CurrentDoctrineOrders;
        CommandNodeOperationalState[] states = FourStates();
        army.GuardStates(states);
        army.FinishOrders(orders, true);
        states = FourStates();
        mark = BeginAllocationWindow();
        army.GuardStates(states);
        IReadOnlyList<CommandDoctrineOrder> guarded = army.FinishOrders(orders, true);
        long pass = AllocatedSince(mark);
        AssertEqual(CommandTaskType.HoldObjective, states[0].Task, "the pass guarded the works-facing child's state");
        AssertTrue(army.WorksGuarded >= 2, "and counted what it rewrote: " + army.WorksGuarded);
        AssertEqual(orders.Count, guarded.Count, "the orders were rebuilt into the army's array");
        AssertEqual(0L, pass, "a guard pass over " + states.Length + " states and " + orders.Count + " orders allocated " + pass + " bytes");

        // ---- the contest's tick and the counts: 24 held groups, half of them advancing into the band by an executor move, half assaulting
        MoveContestState contest = MoveContestState.ForAlliance(0);
        MoveContestState.ForAlliance(1).TakeCounts(out _, out _, out _, out _);
        contest.TakeCounts(out _, out _, out _, out _);
        long tick = 0;
        void Pass()
        {
            tick++;
            for (int g = 0; g < 24; g++)
            {
                bool advancing = (tick & 1) == 0;
                var observation = new ContestObservation
                {
                    Held = true, X = 100f + g, Z = 100f, HasPath = advancing, WaypointX = 100f + g, WaypointZ = advancing ? 450f : 100f,
                    HasLastIssued = advancing, LastIssuedX = 100f + g, LastIssuedZ = 450f, Stance = g % 2 == 0 ? 4 : 2, Works = Strip,
                };
                contest.Evaluate(1000 + g, tick, in observation, out _, out _);
            }
        }
        for (int warm = 0; warm < 4; warm++) Pass();
        contest.TakeCounts(out _, out _, out _, out _);
        mark = BeginAllocationWindow();
        for (int measured = 0; measured < 8; measured++) Pass();
        long ticked = AllocatedSince(mark);
        contest.TakeCounts(out int chain, out int onset, out int advances, out int entries);
        AssertTrue(advances > 0, "the advancesOnWorks count ran: " + advances + " (chain " + chain + ", onset " + onset + ", entries " + entries + ")");
        AssertEqual(0L, ticked, "eight contest ticks over 24 held groups allocated " + ticked + " bytes");
        // Leave the process-wide state as the next test finds it.
        for (int g = 0; g < 24; g++)
        {
            var released = new ContestObservation { Held = false };
            contest.Evaluate(1000 + g, tick + 1, in released, out _, out _);
        }
    }
}
