using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Perf slice B1: the recon planner with the hoisted scout list, the precomputed
// closest-visible distance and the per-tick target cap equals the shipped planner
// (BattleReconReference, 742d7e4) whenever no list exceeds the cap, with and without a
// built slice-pressure row; past the cap it still finds the shipped pick, ranks later.
internal static class BattleReconSearchTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("recon search equals the shipped planner on random fields", SearchEqualsShippedOnRandomFields),
            ("recon search equals the shipped planner with closest-visible rows built", SearchEqualsShippedWithPressureRows),
            ("recon search past the cap resumes at the next ranks", SearchPastCapResumes),
            ("recon search with no idle scout touches no target", SearchWithoutScoutReturnsAtOnce),
            ("recon search wrapped window still picks the nearest feasible rank", WrappedWindowPicksNearest),
            ("recon closest-visible rows are not read once an observation was added after them", StaleRowsAreNotRead),
            ("recon failed search allocates nothing and is bounded", FailedSearchAllocatesNothing),
            ("recon search past 16 tied targets ranks by distance then source index", TiedTargetsRankBySourceIndex),
        });
    }

    private static ReconSettings Settings() =>
        new ReconSettings(true, cavalrySight: 250f, infantrySight: 200f, standOff: 150f, interruptMeters: 100f, minMorale: 0.5f);

    private sealed class Sink : IIntentSink
    {
        public readonly BattleOrderIntent[] Items = new BattleOrderIntent[64];
        public int Count;
        public void Submit(in BattleOrderIntent intent) { if (Count < Items.Length) Items[Count++] = intent; }
    }

    private sealed class Field
    {
        public int Seed;
        public int Brigades, Remembered, Visible, Entries;
        public bool HadContact, StaleBand, Objective, AxisTarget;
        public float Confidence;
    }

    private static BattleSnapshot Build(Field f, double hours, bool pressure)
    {
        var rng = new Random(f.Seed);
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, (long)(hours * 1000), hours);
        s.SetSliceCount(20);
        int id = 1;
        var parents = new System.Collections.Generic.List<(int child, int parent)>();
        for (int b = 0; b < f.Brigades; b++)
        {
            int brigade = id++;
            float bx = (float)(rng.NextDouble() * 2000.0 - 1000.0);
            float bz = -1000f + (float)(rng.NextDouble() * 200.0);
            s.AddOwn(new UnitRecord
            {
                InstanceId = brigade, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = bx, Z = bz, AiStance = 1, AiStanceOrdered = 1,
                Strength = 1200, GroupStrengthAiGroup = 1200, Morale = 0.9f,
                RegimentPaths = rng.NextDouble() < 0.15 ? 1 : 0,
                Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup | (rng.NextDouble() < 0.1 ? UnitFlags.PlayerOwned : UnitFlags.None),
            });
            int regiments = 1 + rng.Next(4);
            for (int r = 0; r < regiments; r++)
            {
                int reg = id++;
                bool cavalry = rng.NextDouble() < 0.7;
                s.AddOwn(new UnitRecord
                {
                    InstanceId = reg, UnitTyp = cavalry ? TacticalUnitType.Cavalry : TacticalUnitType.Infantry,
                    X = bx + (float)(rng.NextDouble() * 160.0 - 80.0), Z = bz + (float)(rng.NextDouble() * 60.0 - 30.0),
                    Strength = 200 + rng.Next(600), Morale = rng.NextDouble() < 0.1 ? 0.3f : 0.9f,
                    RegimentPaths = rng.NextDouble() < 0.1 ? 1 : 0,
                    Flags = UnitFlags.Alive | UnitFlags.Active | (cavalry ? UnitFlags.IsCavalry : UnitFlags.IsInfantry),
                });
                parents.Add((reg, brigade));
            }
        }
        foreach (var (child, parent) in parents)
            s.Own[s.IndexOfOwn(child)].ParentIndex = s.IndexOfOwn(parent);
        s.IndexCommandTree(null);
        if (f.AxisTarget) s.SetFrontAxis(TacticalFrontAxis.FromCentroids(0f, -1000f, 0f, 0f).WithOwnExtent(-1000f, 1000f));
        int enemy = 5000;
        for (int k = 0; k < f.Visible; k++)
            s.AddObservation(new EnemyObservation
            {
                InstanceId = enemy++, UnitTyp = TacticalUnitType.Infantry, State = ObservationState.Visible, Confidence01 = 1f,
                X = (float)(rng.NextDouble() * 3000.0 - 1500.0), Z = -900f + (float)(rng.NextDouble() * 1400.0),
                Strength = rng.Next(1500), IsReinforcement = rng.NextDouble() < 0.1, LastSeenHours = hours,
            });
        for (int k = 0; k < f.Remembered; k++)
            s.AddObservation(new EnemyObservation
            {
                InstanceId = enemy++, UnitTyp = TacticalUnitType.Infantry, State = ObservationState.Remembered, Confidence01 = 0.4f,
                X = (float)(rng.NextDouble() * 3000.0 - 1500.0), Z = -400f + (float)(rng.NextDouble() * 1200.0),
                Strength = rng.Next(1500), IsReinforcement = rng.NextDouble() < 0.05, LastSeenHours = hours - 0.5,
            });
        if (pressure) SlicePressureBuilder.Build(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f });
        return s;
    }

    private static ReconPicture Picture(Field f, BattleSnapshot s, double hours)
    {
        var rng = new Random(f.Seed * 31 + 7);
        var p = new ReconPicture();
        p.BeginTick(s.OwnCount);
        p.IntelConfidence = f.Confidence;
        p.HadContact = f.HadContact;
        p.Phase = BattlePhase.MainEffort;
        p.FrameHeading = 0f;
        p.HasStaleBand = f.StaleBand;
        p.StaleBandLateral = (float)(rng.NextDouble() * 1000.0 - 500.0);
        p.StaleBandRecency = 0.2f;
        p.HasObjective = f.Objective;
        p.ObjectiveX = (float)(rng.NextDouble() * 1000.0 - 500.0);
        p.ObjectiveZ = 800f;
        for (int k = 0; k < f.Entries; k++)
            p.AddEntry((float)(rng.NextDouble() * 3000.0 - 1500.0), -200f + (float)(rng.NextDouble() * 1500.0),
                rng.NextDouble() < 0.3 ? hours - 0.2 : -1.0);
        for (int i = 0; i < s.OwnCount; i++)
            if (rng.NextDouble() < 0.1) p.PostureMoved[i] = true;
        return p;
    }

    private static void Compare(Field f, bool pressure, string label)
    {
        var oldMemory = new ReconMemory();
        var newMemory = new ReconMemory();
        for (int tick = 0; tick < 4; tick++)
        {
            double hours = 3.0 + tick * 0.4;
            BattleSnapshot s = Build(f, hours, pressure);
            var oldSink = new Sink();
            var newSink = new Sink();
            ReconTickResult a = BattleReconReference.Plan(s, Settings(), Picture(f, s, hours), oldMemory, new OrderExecutor(), oldSink);
            ReconTickResult b = BattleRecon.Plan(s, Settings(), Picture(f, s, hours), newMemory, new OrderExecutor(), newSink);
            string at = label + " seed " + f.Seed + " tick " + tick;
            AssertEqual(a.Events, b.Events, at + ": events");
            AssertEqual(a.StartedKind, b.StartedKind, at + ": started kind");
            AssertEqual(a.EndReason, b.EndReason, at + ": end reason");
            AssertEqual(a.Intents, b.Intents, at + ": intents");
            AssertEqual(oldSink.Count, newSink.Count, at + ": submitted");
            for (int i = 0; i < oldSink.Count; i++)
            {
                AssertEqual(oldSink.Items[i].GroupId, newSink.Items[i].GroupId, at + ": intent group " + i);
                AssertEqual(oldSink.Items[i].Channel, newSink.Items[i].Channel, at + ": intent channel " + i);
                AssertEqual(oldSink.Items[i].TargetX, newSink.Items[i].TargetX, at + ": intent x " + i);
                AssertEqual(oldSink.Items[i].TargetZ, newSink.Items[i].TargetZ, at + ": intent z " + i);
                AssertEqual(oldSink.Items[i].Reason, newSink.Items[i].Reason, at + ": intent reason " + i);
            }
            AssertEqual(oldMemory.Leg, newMemory.Leg, at + ": leg");
            AssertEqual(oldMemory.ScoutId, newMemory.ScoutId, at + ": scout");
            AssertEqual(oldMemory.Kind, newMemory.Kind, at + ": kind");
            AssertEqual(oldMemory.TargetX, newMemory.TargetX, at + ": target x");
            AssertEqual(oldMemory.TargetZ, newMemory.TargetZ, at + ": target z");
        }
    }

    private static Field RandomField(Random rng, int seed, int maxListLength)
    {
        return new Field
        {
            Seed = seed,
            Brigades = 1 + rng.Next(6),
            Visible = rng.Next(40),
            Remembered = rng.Next(maxListLength + 1),
            Entries = rng.Next(maxListLength + 1),
            HadContact = rng.NextDouble() < 0.6,
            StaleBand = rng.NextDouble() < 0.5,
            Objective = rng.NextDouble() < 0.7,
            AxisTarget = rng.NextDouble() < 0.5,
            Confidence = rng.NextDouble() < 0.8 ? 0.1f : 0.6f,
        };
    }

    private static void SearchEqualsShippedOnRandomFields()
    {
        var rng = new Random(20261015);
        int started = 0;
        for (int seed = 1; seed <= 400; seed++)
        {
            Field f = RandomField(rng, seed, ReconSettings.MaxTargetsPerTick);
            Compare(f, pressure: false, "scan");
            var probe = new ReconMemory();
            BattleSnapshot s = Build(f, 3.0, false);
            if ((BattleReconReference.Plan(s, Settings(), Picture(f, s, 3.0), probe, new OrderExecutor(), new Sink()).Events & ReconEvents.MissionStarted) != 0)
                started++;
        }
        AssertTrue(started > 40, "fixtures that start a mission: " + started);
    }

    private static void SearchEqualsShippedWithPressureRows()
    {
        var rng = new Random(77);
        for (int seed = 1; seed <= 400; seed++)
            Compare(RandomField(rng, seed, ReconSettings.MaxTargetsPerTick), pressure: true, "pressure");
    }

    // Many remembered enemies, only the farthest reachable without passing a superior
    // visible enemy: the shipped planner picks it at once; the capped one tries the
    // nearest window first and reaches it in later ticks, picking the same target.
    private static void SearchPastCapResumes()
    {
        double hours = 3.0;
        BattleSnapshot s = PastCapField(hours);
        var p = PastCapPicture(s);
        var shipped = new ReconMemory();
        var shippedResult = BattleReconReference.Plan(s, Settings(), p, shipped, new OrderExecutor(), new Sink());
        AssertTrue((shippedResult.Events & ReconEvents.MissionStarted) != 0, "the shipped planner starts on the far target");

        var capped = new ReconMemory();
        int ticks = 0;
        ReconTickResult r = default;
        while (ticks < 10)
        {
            ticks++;
            s = PastCapField(hours);
            r = BattleRecon.Plan(s, Settings(), PastCapPicture(s), capped, new OrderExecutor(), new Sink());
            if ((r.Events & ReconEvents.MissionStarted) != 0) break;
        }
        AssertTrue((r.Events & ReconEvents.MissionStarted) != 0, "the capped planner starts too");
        AssertEqual(3, ticks, "20 failing ranks before it: two failed windows of 8, found in the third");
        AssertEqual(shipped.ScoutId, capped.ScoutId, "same scout");
        AssertEqual(shipped.TargetX, capped.TargetX, "same stand-off x");
        AssertEqual(shipped.TargetZ, capped.TargetZ, "same stand-off z");
        AssertEqual(0, capped.RememberedCursor, "window reset after a start");
    }

    private static BattleSnapshot PastCapField(double hours)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, hours);
        s.SetSliceCount(20);
        s.AddOwn(new UnitRecord { InstanceId = 1, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = 0f, Z = -1000f, AiStance = 1, AiStanceOrdered = 1, Strength = 1200, GroupStrengthAiGroup = 1200, Morale = 0.9f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup });
        s.AddOwn(new UnitRecord { InstanceId = 2, UnitTyp = TacticalUnitType.Cavalry, X = 0f, Z = -1000f, Strength = 400, Morale = 0.9f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsCavalry });
        s.Own[1].ParentIndex = 0;
        s.IndexCommandTree(null);
        // A superior visible enemy sits on the stand-off point of every near target
        // (x = 0 line), none near the far target at x = 1500.
        s.AddObservation(new EnemyObservation { InstanceId = 900, State = ObservationState.Visible, Confidence01 = 1f, X = 0f, Z = -500f, Strength = 5000f });
        for (int k = 0; k < 20; k++)
            s.AddObservation(new EnemyObservation { InstanceId = 1000 + k, State = ObservationState.Remembered, Confidence01 = 0.5f, X = 0f, Z = -300f + k * 20f, Strength = 300f });
        s.AddObservation(new EnemyObservation { InstanceId = 2000, State = ObservationState.Remembered, Confidence01 = 0.5f, X = 1500f, Z = 400f, Strength = 300f });
        return s;
    }

    // Past 16 targets Array.Sort is no longer an insertion sort, so equal distances keep
    // the (distance, source index) rank only through SortTargets' tie pass (verify round 1,
    // half B): 24 remembered enemies on 4-fold symmetric points, shuffled.
    private static void TiedTargetsRankBySourceIndex()
    {
        var rng = new Random(5);
        int differ = 0;
        for (int trial = 0; trial < 200; trial++)
        {
            var s = new BattleSnapshot();
            s.BeginBuild(1, 0, 0, 1, 3.0);
            s.SetSliceCount(20);
            s.AddOwn(new UnitRecord { InstanceId = 1, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = 0f, Z = 0f, AiStance = 1, AiStanceOrdered = 1, Strength = 1200, GroupStrengthAiGroup = 1200, Morale = 0.9f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup });
            s.AddOwn(new UnitRecord { InstanceId = 2, UnitTyp = TacticalUnitType.Cavalry, X = 0f, Z = 0f, Strength = 400, Morale = 0.9f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsCavalry });
            s.Own[1].ParentIndex = 0;
            s.IndexCommandTree(null);
            var pts = new System.Collections.Generic.List<(float, float)>();
            foreach (float d in new[] { 400f, 400f, 500f, 600f, 700f, 800f })
                foreach (var (sx, sz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
                    pts.Add((sx * d, sz * d));
            for (int i = pts.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = pts[i]; pts[i] = pts[j]; pts[j] = t; }
            for (int k = 0; k < pts.Count; k++)
                s.AddObservation(new EnemyObservation { InstanceId = 1000 + k, State = ObservationState.Remembered, Confidence01 = 0.5f, X = pts[k].Item1, Z = pts[k].Item2, Strength = 300f });
            var shipped = new ReconMemory();
            BattleReconReference.Plan(s, Settings(), PastCapPicture(s), shipped, new OrderExecutor(), new Sink());
            var capped = new ReconMemory();
            BattleRecon.Plan(s, Settings(), PastCapPicture(s), capped, new OrderExecutor(), new Sink());
            if (shipped.TargetX != capped.TargetX || shipped.TargetZ != capped.TargetZ) differ++;
        }
        AssertEqual(0, differ, "trials where the capped pick differs from the shipped pick");
    }

    private static ReconPicture PastCapPicture(BattleSnapshot s)
    {
        var p = new ReconPicture();
        p.BeginTick(s.OwnCount);
        p.IntelConfidence = 0.1f;
        p.HadContact = true;
        p.Phase = BattlePhase.MainEffort;
        p.FrameHeading = 0f;
        return p;
    }

    private static void SearchWithoutScoutReturnsAtOnce()
    {
        var rng = new Random(5);
        for (int seed = 1; seed <= 50; seed++)
        {
            Field f = RandomField(rng, seed, 40);
            f.Brigades = 0;
            Compare(f, pressure: true, "no scout");
        }
    }

    private static void FailedSearchAllocatesNothing()
    {
        BattleSnapshot s = PastCapField(3.0);
        var p = PastCapPicture(s);
        // Never feasible: the far target moves under the superior enemy too.
        s.Observations[s.ObservationCount - 1].X = 0f;
        var m = new ReconMemory();
        var sink = new Sink();
        var executor = new OrderExecutor();
        for (int i = 0; i < 4; i++) BattleRecon.Plan(s, Settings(), p, m, executor, sink);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) BattleRecon.Plan(s, Settings(), p, m, executor, sink);
        AssertEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "failed searches allocated bytes");
        AssertEqual(0, sink.Count, "nothing submitted");
    }

    // After a failed window the cursor sits at rank 8 of 12; next tick every target is
    // feasible, the window wraps (8..11, 0..3) and the pick must still be rank 0, the
    // shipped planner's pick.
    private static void WrappedWindowPicksNearest()
    {
        BattleSnapshot blocked = WrapField(blocked: true);
        var m = new ReconMemory();
        var r = BattleRecon.Plan(blocked, Settings(), PastCapPicture(blocked), m, new OrderExecutor(), new Sink());
        AssertTrue((r.Events & ReconEvents.MissionStarted) == 0, "blocked: no start");
        AssertEqual(8, m.RememberedCursor, "window advanced");

        BattleSnapshot open = WrapField(blocked: false);
        r = BattleRecon.Plan(open, Settings(), PastCapPicture(open), m, new OrderExecutor(), new Sink());
        var shipped = new ReconMemory();
        BattleReconReference.Plan(open, Settings(), PastCapPicture(open), shipped, new OrderExecutor(), new Sink());
        AssertTrue((r.Events & ReconEvents.MissionStarted) != 0, "open: start");
        AssertEqual(shipped.TargetX, m.TargetX, "nearest feasible rank x");
        AssertEqual(shipped.TargetZ, m.TargetZ, "nearest feasible rank z");
    }

    private static BattleSnapshot WrapField(bool blocked)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 3.0);
        s.SetSliceCount(20);
        s.AddOwn(new UnitRecord { InstanceId = 1, UnitTyp = TacticalUnitType.BattleGroupBrigade, X = 0f, Z = -1000f, AiStance = 1, AiStanceOrdered = 1, Strength = 1200, GroupStrengthAiGroup = 1200, Morale = 0.9f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsGroup });
        s.AddOwn(new UnitRecord { InstanceId = 2, UnitTyp = TacticalUnitType.Cavalry, X = 0f, Z = -1000f, Strength = 400, Morale = 0.9f, Flags = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsCavalry });
        s.Own[1].ParentIndex = 0;
        s.IndexCommandTree(null);
        if (blocked) s.AddObservation(new EnemyObservation { InstanceId = 900, State = ObservationState.Visible, Confidence01 = 1f, X = 0f, Z = -500f, Strength = 5000f });
        for (int k = 0; k < 12; k++)
            s.AddObservation(new EnemyObservation { InstanceId = 1000 + k, State = ObservationState.Remembered, Confidence01 = 0.5f, X = 0f, Z = -300f + k * 40f, Strength = 300f });
        return s;
    }

    private static void StaleRowsAreNotRead()
    {
        var rng = new Random(11);
        for (int seed = 1; seed <= 100; seed++)
        {
            Field f = RandomField(rng, seed, ReconSettings.MaxTargetsPerTick);
            BattleSnapshot s = Build(f, 3.0, pressure: true);
            // A visible enemy beside every own unit, added after the rows were built.
            for (int i = 0; i < s.OwnCount; i++)
                s.AddObservation(new EnemyObservation { InstanceId = 90000 + i, State = ObservationState.Visible, Confidence01 = 1f, X = s.Own[i].X + 30f, Z = s.Own[i].Z, Strength = 10f });
            var a = BattleReconReference.Plan(s, Settings(), Picture(f, s, 3.0), new ReconMemory(), new OrderExecutor(), new Sink());
            var b = BattleRecon.Plan(s, Settings(), Picture(f, s, 3.0), new ReconMemory(), new OrderExecutor(), new Sink());
            AssertEqual(a.Events, b.Events, "seed " + seed + ": events");
            AssertEqual(a.StartedKind, b.StartedKind, "seed " + seed + ": started kind");
        }
    }
}
