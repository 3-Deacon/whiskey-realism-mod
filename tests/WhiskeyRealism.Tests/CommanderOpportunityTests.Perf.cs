using System;
using System.Collections.Generic;
using System.Diagnostics;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Util;
using static TestHarness;

// BC13 task 13, the performance proof (spec § 11 requirement-zero-alloc and requirement-frame-budget, § 13 S7): the whole opportunity
// tick (detection, decisions, lifecycle) allocates nothing after warm-up, and a timing smoke over 1,000 ticks plus the full 128-point
// probe budget stays far inside a frame. The binding frame-budget measurement is in-game smoke item 9; these are the harness half.
internal static partial class CommanderOpportunityTests
{
    private static (string, Action)[] PerfTests()
    {
        return new (string, Action)[]
        {
            ("bc13 OPP-ALLOC detection over 400 ticks allocates nothing after warm-up", AllocDetection),
            ("bc13 OPP-ALLOC decisions for 12 commanders and the lifecycle of live exploits allocate nothing after warm-up", AllocDecisionsAndLifecycle),
            ("bc13 OPP-TIME 1,000 ticks and the full 128-point probe budget fit a frame", TimeTicksAndBudget),
        };
    }

    private sealed class CountingSink : IOpportunitySink
    {
        public int Launches, Declines, Phases, Ends, Summaries;
        public void Launched(in ExploitEntry entry, in DecisionResult result, in OpportunityPersonality personality, int live) => Launches++;
        public void Declined(int nodeId, int commanderIndex, in DecisionResult result, in OpportunityPersonality personality, int live) => Declines++;
        public void PhaseChanged(in ExploitEntry entry, ExploitPhase from, int live) => Phases++;
        public void Ended(in ExploitEntry entry, double nowHours, bool leftSector, int live) => Ends++;
        public void OpeningSummary(OpportunityTick tick, ExploitTable table, double nowHours) => Summaries++;
    }

    // `divisions` divisions of five brigades, each with its own commander index, a line of enemy pairs with a gap in front of every
    // one; snapshots for `ticks` consecutive ticks are built up front so the measured loop allocates only what the tick itself does.
    private sealed class PerfRig
    {
        public readonly OpportunityTick Tick = new OpportunityTick();
        public readonly ExploitTable Table = ExploitTable.ForAlliance(0);
        public readonly FakeWorld World = new FakeWorld();
        public readonly CountingSink Sink = new CountingSink();
        public readonly IPerceivedGround Ground = new AllPerceived();
        public readonly IOpeningTerrain Terrain = new AnchoredTerrain();
        public OpeningRules Rules = OpeningRules.Shipped;
        public readonly BattleSnapshot[] Snapshots;

        public PerfRig(int divisions, int ticks, int extraEnemies)
        {
            var fx = new DetFixture();
            int army = fx.OwnGroup();
            Patch(fx, army, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupArmy; });
            for (int d = 0; d < divisions; d++)
            {
                float baseLateral = d * 1400f;
                int div = fx.OwnGroup(army);
                int commander = TickFx.ReliableCommander + d;
                Patch(fx, div, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupDivision; r.Commander = commander; r.GroupMorale = 0.8f; });
                for (int i = 0; i < 5; i++)
                {
                    float lateral = baseLateral - 250f + 250f * i;
                    int brigade = fx.OwnGroup(div);
                    Patch(fx, brigade, (ref UnitRecord r) => { r.UnitTyp = TacticalUnitType.BattleGroupBrigade; r.Commander = 1000 + d * 10 + i; r.GroupMorale = 0.8f; r.X = lateral; r.Z = 0f; r.GroupWidth = 120f; });
                    int regiment = fx.OwnInf(lateral, 0f, 120f, brigade);
                    Patch(fx, regiment, (ref UnitRecord r) => { r.Strength = 2000; });
                    World.Roles[TickFx.IdOf(brigade)] = i < 2
                        ? (CommandNodeRole.MainEffort, CommandTaskType.AttackObjective)
                        : (CommandNodeRole.Reserve, CommandTaskType.ReserveWait);
                }
                fx.Inf(baseLateral, 300f);
                fx.Inf(baseLateral + 300f, 300f);
            }
            for (int e = 0; e < extraEnemies; e++) fx.Inf(-700f - 140f * (e % 20), 600f + 120f * (e / 20));
            Snapshots = new BattleSnapshot[ticks];
            double now = 10.0;
            for (int t = 0; t < ticks; t++)
            {
                now += 0.03125;
                Snapshots[t] = fx.Build(now);
            }
        }

        private static void Patch(DetFixture fx, int index, TickFx.PatchRecord edit)
        {
            UnitRecord r = fx.Own[index];
            edit(ref r);
            fx.Own[index] = r;
        }

        public void Run(int tickIndex)
        {
            Tick.Run(Snapshots[tickIndex], World, Ground, Terrain, in Rules, Table, Sink);
        }
    }

    private static long Allocated(PerfRig rig, int from, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++) rig.Run(from + i);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void AllocDetection()
    {
        BattleScope.ResetAll();
        // Detection alone: a one-division army, 30 enemies, a stand-by commander (no exploit is ever launched: the personality is
        // timid), so every measured tick is the detector's full pass over the snapshot.
        var rig = new PerfRig(1, 460, 30);
        rig.World.Persona = new OpportunityPersonality { Agg01 = 0f, Caution01 = 1f, Audacity01 = 0f, CasualtyTolerance01 = 0f, Duty = 0f, Source = PersonalitySource.Registry };
        for (int i = 0; i < 60; i++) rig.Run(i);
        AssertTrue(rig.Tick.Detector.Count >= 1, "the detector holds openings while it is measured");
        long bytes = Allocated(rig, 60, 400);
        AssertEqual(0L, bytes, "detection over 400 ticks allocated bytes");
    }

    private static void AllocDecisionsAndLifecycle()
    {
        BattleScope.ResetAll();
        var rig = new PerfRig(12, 560, 0);
        int warm = 80;
        int liveMax = 0;
        for (int i = 0; i < warm; i++)
        {
            rig.Run(i);
            if (rig.Table.Count > liveMax) liveMax = rig.Table.Count;
        }
        AssertTrue(rig.Sink.Launches >= 3, "twelve commanders launch several exploits during warm-up (" + rig.Sink.Launches + ")");
        AssertTrue(liveMax >= 3, "at least three exploits were live at once (" + liveMax + ")");
        long bytes = Allocated(rig, warm, 480);
        AssertEqual(0L, bytes, "decisions for 12 commanders and the lifecycle of live exploits allocated bytes");
        AssertTrue(rig.Sink.Declines + rig.Sink.Launches + rig.Sink.Phases + rig.Sink.Ends >= 6, "the sink saw the decisions that were measured");
    }

    private static void TimeTicksAndBudget()
    {
        BattleScope.ResetAll();
        var rig = new PerfRig(12, 1060, 30);
        for (int i = 0; i < 60; i++) rig.Run(i);
        var watch = Stopwatch.StartNew();
        for (int i = 60; i < 1060; i++) rig.Run(i);
        watch.Stop();
        double meanMicros = watch.Elapsed.TotalMilliseconds * 1000.0 / 1000.0;
        Console.WriteLine("INFO opportunity tick: 1,000 ticks over 12 divisions took " + watch.Elapsed.TotalMilliseconds.ToString("F1") + " ms (" + meanMicros.ToString("F1") + " us a tick)");
        // Informational: the binding measurement is in-game smoke item 9. The bound is two orders above what a tick costs here and still
        // proves nothing quadratic crept in.
        AssertTrue(meanMicros < 2000.0, "a tick costs under 2 ms on average (" + meanMicros.ToString("F1") + " us)");

        // The full probe budget: ten candidate lattices behind each other, 128 IsPerceived calls in one tick through a fixture grid.
        var ground = new FnGround((x, z) => true);
        var pool = new PerceivedGroundLattice[10];
        for (int i = 0; i < pool.Length; i++)
        {
            pool[i] = new PerceivedGroundLattice();
            ProbeBox box = BoxOf(9, 7, 1000f * i);
            pool[i].Begin(i, OpeningKind.Gap, in box, Epoch(200), false, order: i + 1);
            pool[i].Claimed = true;
        }
        var stats = default(LatticeTickStats);
        var one = Stopwatch.StartNew();
        PerceivedGroundLattice.Serve(pool, pool.Length, ground, OpeningDetector.MaxProbePointsPerTick, 1, ref stats);
        one.Stop();
        AssertTrue(ground.Calls <= OpeningDetector.MaxProbePointsPerTick, "never more than the budget of IsPerceived calls (" + ground.Calls + ")");
        AssertTrue(ground.Calls > 0, "and the budget was used");
        Console.WriteLine("INFO probe budget: " + ground.Calls + " IsPerceived calls in one tick took " + (one.Elapsed.TotalMilliseconds * 1000.0).ToString("F0") + " us");
    }
}
