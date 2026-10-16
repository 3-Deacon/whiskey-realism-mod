using System;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// Perf slice F2: the uniform-grid SlicePressureBuilder must equal the full scan it
// replaced (SlicePressureReference, the shipped c3f35b1 code) bit for bit: every slice,
// every flank value, the closest visible observation index and its distance.
internal static class SlicePressureBuilderTests
{
    public static void Run()
    {
        RunAll(new (string name, Action run)[]
        {
            ("slice pressure grid equals full scan on random battlefields", GridEqualsFullScanOnRandomBattlefields),
            ("slice pressure grid equals full scan on edge cases", GridEqualsFullScanOnEdgeCases),
            ("slice pressure grid equals full scan for units far outside the enemy field", GridEqualsFullScanFarOutside),
            ("slice pressure grid keeps the lowest index on closest-distance ties", GridKeepsLowestIndexOnTies),
            ("slice pressure build allocates nothing once warm", BuildAllocatesNothingOnceWarm),
            ("slice pressure grid equals full scan at ulp-level cell boundaries", GridEqualsFullScanAtCellBoundaries),
            ("slice pressure rows are current only until the snapshot changes", PressureCurrentClearedOnChange),
        });
    }

    private static readonly SliceGeometry Vanilla = new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f };

    private static void PressureCurrentClearedOnChange()
    {
        var s = Snapshot(20);
        AddOwn(s, 1, 0f, 0f, 0f);
        s.IndexCommandTree(null);
        AssertFalse(s.PressureCurrent, "not built");
        SlicePressureBuilder.Build(s, Vanilla);
        AssertTrue(s.PressureCurrent, "built");
        AddOwn(s, 2, 10f, 0f, 0f);
        AssertFalse(s.PressureCurrent, "a unit added after the build");
        s.IndexCommandTree(null);
        SlicePressureBuilder.Build(s, Vanilla);
        AssertTrue(s.PressureCurrent, "rebuilt");
        s.AddObservation(new EnemyObservation { InstanceId = 7, X = 0f, Z = 100f, Strength = 1f, State = ObservationState.Visible });
        AssertFalse(s.PressureCurrent, "an observation added after the build");
        SlicePressureBuilder.Build(s, Vanilla);
        s.IndexCommandTree(null);
        AssertFalse(s.PressureCurrent, "command tree re-indexed");
        SlicePressureBuilder.Build(s, Vanilla);
        s.BeginBuild(1, 0, 0, 2, 1.0);
        AssertFalse(s.PressureCurrent, "next build");
    }

    // The cell padding's job (verify round 1, half B): a unit a few ulps below a cell
    // boundary (for the unpadded and the padded cell size) with observations exactly on
    // the far side of a flank circle or on the range ring, nudged by ulps. Without the
    // padding the 3x3 block misses an in-reach observation in the next cell.
    private static void GridEqualsFullScanAtCellBoundaries()
    {
        var rng = new Random(99);
        var g = new SliceGeometry { MaxRangeMeters = 300f, FlankShiftMeters = 150f };
        float reach = 300f;
        float cellPadded = reach * 1.01f + 1f;
        int mismatches = 0, cases = 0;
        string first = null;
        foreach (float cell in new[] { reach, cellPadded })
        for (int iter = 0; iter < 20000; iter++)
        {
            float baseX = (float)(rng.Next(1, 40) * 1000);
            var s = Snapshot(20);
            int k = rng.Next(1, 6);
            float ux = baseX + k * cell;
            for (int n = rng.Next(0, 6); n > 0; n--) ux = MathF.BitDecrement(ux);
            float facing = rng.NextDouble() < 0.5 ? (float)(rng.NextDouble() * 0.01 - 0.005) : 180f + (float)(rng.NextDouble() * 0.01 - 0.005);
            s.AddOwn(new UnitRecord { InstanceId = 1, X = ux, Z = 5000f, RotationY = facing, ParentIndex = -1 });
            s.IndexCommandTree(null);
            // The anchor observation fixes the grid's minimum x at baseX.
            s.AddObservation(new EnemyObservation { InstanceId = 1, X = baseX, Z = 5000f - 50000f, Strength = 1f, State = ObservationState.Visible });
            for (int j = 0; j < 12; j++)
            {
                double ang = (facing + (rng.Next(2) == 0 ? 90.0 : -90.0) - 90.0) * Math.PI / 180.0;
                float px = ux + (float)(Math.Cos(ang) * 150.0), pz = 5000f - (float)(Math.Sin(ang) * 150.0);
                float ox = px + (px >= ux ? 150f : -150f), oz = pz;
                if (j % 3 == 0) { ox = ux + (rng.Next(2) == 0 ? 300f : -300f); oz = 5000f; }
                int nudge = rng.Next(-3, 4);
                for (; nudge > 0; nudge--) ox = MathF.BitIncrement(ox);
                for (; nudge < 0; nudge++) ox = MathF.BitDecrement(ox);
                s.AddObservation(new EnemyObservation { InstanceId = 10 + j, X = ox, Z = oz, Strength = 1f + j, State = ObservationState.Remembered, Confidence01 = 1f });
            }
            cases++;
            var exp = SlicePressureReference.Build(s, g);
            SlicePressureBuilder.Build(s, g);
            bool ok = true;
            for (int q = 0; q < 20 && ok; q++) ok = BitConverter.SingleToInt32Bits(exp[0].Slices[q]) == BitConverter.SingleToInt32Bits(s.SlicePressure(0, q));
            for (int f = 0; f < 3 && ok; f++) ok = BitConverter.SingleToInt32Bits(exp[0].Flanks[f]) == BitConverter.SingleToInt32Bits(s.FlankPressure(0, f));
            if (!ok)
            {
                mismatches++;
                if (first == null) first = "cell " + cell + " ux=" + ux + " base=" + baseX + " facing=" + facing;
            }
        }
        AssertEqual(0, mismatches, "boundary mismatches of " + cases + " (first: " + first + ")");
    }

    private static void GridEqualsFullScanOnRandomBattlefields()
    {
        var rng = new Random(20261015);
        int cases = 0;
        foreach (int own in new[] { 0, 1, 7, 120, 400 })
        foreach (int enemies in new[] { 0, 1, 3, 50, 300, 900 })
        {
            for (int layout = 0; layout < 3; layout++)
            {
                var s = Snapshot(20);
                float spread = layout == 0 ? 400f : layout == 1 ? 3000f : 12000f;
                for (int i = 0; i < own; i++)
                    AddOwn(s, i + 1, (float)(rng.NextDouble() * spread) - spread / 2f, (float)(rng.NextDouble() * spread) - spread / 2f - 300f, (float)(rng.NextDouble() * 360.0));
                s.IndexCommandTree(null);
                for (int k = 0; k < enemies; k++)
                    AddObservation(s, rng, k, spread);
                Compare(s, Vanilla, "random own=" + own + " obs=" + enemies + " layout=" + layout);
                Compare(s, new SliceGeometry { MaxRangeMeters = 1000f, FlankShiftMeters = 150f }, "random wide range " + own + "/" + enemies);
                Compare(s, new SliceGeometry { MaxRangeMeters = 60f, FlankShiftMeters = 400f }, "random wide flank " + own + "/" + enemies);
                cases += 3;
            }
        }
        AssertTrue(cases > 200, "cases run: " + cases);
    }

    private static void GridEqualsFullScanOnEdgeCases()
    {
        var rng = new Random(7);
        foreach (int slices in new[] { 0, 1, 20, 36, 400 })
        {
            var s = Snapshot(slices);
            AddOwn(s, 1, 0f, 0f, 0f);
            AddOwn(s, 2, float.NaN, 10f, 0f);                 // non-finite own: full scan
            AddOwn(s, 3, 100f, float.PositiveInfinity, 90f);
            AddOwn(s, 4, 250f, 0f, 45f);                      // exactly at slice range
            AddOwn(s, 5, 300f, 300f, float.NaN);              // NaN facing
            AddOwn(s, 6, -1e6f, 1e6f, 10f);                   // far away
            s.IndexCommandTree(null);
            s.AddObservation(Obs(1, 250f, 0f, 100f, ObservationState.Visible));          // range boundary from own 1
            s.AddObservation(Obs(2, 0f, 0f, 50f, ObservationState.Visible));            // on top of own 1
            s.AddObservation(Obs(3, float.NaN, 5f, 70f, ObservationState.Visible));     // never counted
            s.AddObservation(Obs(4, 10f, float.NegativeInfinity, 70f, ObservationState.Visible));
            s.AddObservation(Obs(5, 120f, 40f, 400f, ObservationState.Remembered, confidence: 0.3f));
            s.AddObservation(Obs(6, 130f, 40f, 900f, ObservationState.Visible, reinforcement: true));
            s.AddObservation(Obs(7, -150f, 0f, float.NaN, ObservationState.Visible));   // NaN strength propagates as in the scan
            s.AddObservation(Obs(8, 0f, -150f, 80f, ObservationState.Remembered, confidence: 1f));
            for (int k = 0; k < 40; k++) AddObservation(s, rng, 100 + k, 800f);
            Compare(s, Vanilla, "edge slices=" + slices);
            Compare(s, new SliceGeometry { MaxRangeMeters = -250f, FlankShiftMeters = -150f }, "negative geometry slices=" + slices);
            Compare(s, new SliceGeometry { MaxRangeMeters = 0f, FlankShiftMeters = 0f }, "zero geometry slices=" + slices);
            Compare(s, new SliceGeometry { MaxRangeMeters = float.NaN, FlankShiftMeters = 150f }, "NaN range slices=" + slices);
            Compare(s, new SliceGeometry { MaxRangeMeters = 1e20f, FlankShiftMeters = 150f }, "overflowing range slices=" + slices);
            Compare(s, new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = float.PositiveInfinity }, "infinite flank slices=" + slices);
        }

        var onlyReinforcements = Snapshot(20);
        AddOwn(onlyReinforcements, 1, 0f, 0f, 0f);
        onlyReinforcements.IndexCommandTree(null);
        onlyReinforcements.AddObservation(Obs(1, 10f, 10f, 500f, ObservationState.Visible, reinforcement: true));
        Compare(onlyReinforcements, Vanilla, "only reinforcement observations");

        var wideField = Snapshot(20);
        AddOwn(wideField, 1, 0f, 0f, 0f);
        AddOwn(wideField, 2, 4e6f, -4e6f, 0f);
        wideField.IndexCommandTree(null);
        wideField.AddObservation(Obs(1, -5e6f, -5e6f, 10f, ObservationState.Visible));
        wideField.AddObservation(Obs(2, 5e6f, 5e6f, 10f, ObservationState.Visible));
        wideField.AddObservation(Obs(3, 120f, 0f, 10f, ObservationState.Visible));
        Compare(wideField, Vanilla, "field wider than the cell cap");
    }

    private static void GridEqualsFullScanFarOutside()
    {
        var rng = new Random(99);
        var s = Snapshot(20);
        for (int i = 0; i < 30; i++)
            AddOwn(s, i + 1, 20000f + i * 37f, -15000f + i * 11f, i * 12f);
        s.IndexCommandTree(null);
        for (int k = 0; k < 200; k++) AddObservation(s, rng, k, 2000f);
        Compare(s, Vanilla, "own units far outside the observed field");
    }

    private static void GridKeepsLowestIndexOnTies()
    {
        var s = Snapshot(20);
        AddOwn(s, 1, 0f, 0f, 0f);
        s.IndexCommandTree(null);
        // Equal distances: inside the block, in a ring, and across both.
        s.AddObservation(Obs(1, 2000f, 0f, 10f, ObservationState.Visible));
        s.AddObservation(Obs(2, 0f, 2000f, 10f, ObservationState.Visible));
        s.AddObservation(Obs(3, -2000f, 0f, 10f, ObservationState.Visible));
        s.AddObservation(Obs(4, 0f, -2000f, 10f, ObservationState.Remembered));
        Compare(s, Vanilla, "ties in rings");
        AssertEqual(0, s.ClosestVisibleEnemy(0, out _), "lowest index wins a tie");

        var t = Snapshot(20);
        AddOwn(t, 1, 0f, 0f, 0f);
        t.IndexCommandTree(null);
        t.AddObservation(Obs(1, 2000f, 0f, 10f, ObservationState.Visible));
        t.AddObservation(Obs(2, 100f, 0f, 10f, ObservationState.Visible));
        t.AddObservation(Obs(3, 0f, 100f, 10f, ObservationState.Visible));
        Compare(t, Vanilla, "ties in block");
        AssertEqual(1, t.ClosestVisibleEnemy(0, out _), "lowest index of the nearest pair");
    }

    private static void BuildAllocatesNothingOnceWarm()
    {
        var rng = new Random(3);
        var s = Snapshot(20);
        for (int i = 0; i < 700; i++)
            AddOwn(s, i + 1, (float)(rng.NextDouble() * 1200.0) - 600f, -125f - (float)(rng.NextDouble() * 200.0), 0f);
        s.IndexCommandTree(null);
        for (int k = 0; k < 700; k++)
            s.AddObservation(Obs(k, (float)(rng.NextDouble() * 1200.0) - 600f, 125f + (float)(rng.NextDouble() * 200.0), 300f, ObservationState.Visible));
        SlicePressureBuilder.Build(s, Vanilla);
        long before = TestFixtures.BeginAllocationWindow();
        for (int i = 0; i < 5; i++) SlicePressureBuilder.Build(s, Vanilla);
        AssertEqual(0L, TestFixtures.AllocatedSince(before), "slice pressure build allocated bytes");
    }

    // ---- fixtures

    private static BattleSnapshot Snapshot(int slices)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.SetSliceCount(slices);
        return s;
    }

    private static void AddOwn(BattleSnapshot s, int id, float x, float z, float facing)
    {
        s.AddOwn(new UnitRecord { InstanceId = id, X = x, Z = z, RotationY = facing, ParentIndex = -1 });
    }

    private static void AddObservation(BattleSnapshot s, Random rng, int id, float spread)
    {
        double roll = rng.NextDouble();
        var state = roll < 0.6 ? ObservationState.Visible : ObservationState.Remembered;
        // A few exact duplicates of earlier positions (ties).
        float x = (float)(rng.NextDouble() * spread) - spread / 2f;
        float z = (float)(rng.NextDouble() * spread) - spread / 2f + 300f;
        if (s.ObservationCount > 0 && rng.NextDouble() < 0.05)
        {
            x = s.Observations[s.ObservationCount - 1].X;
            z = s.Observations[s.ObservationCount - 1].Z;
        }
        s.AddObservation(Obs(id, x, z, (float)(rng.NextDouble() * 1500.0), state,
            confidence: (float)rng.NextDouble(), reinforcement: rng.NextDouble() < 0.05));
    }

    private static EnemyObservation Obs(int id, float x, float z, float strength, ObservationState state, float confidence = 1f, bool reinforcement = false)
    {
        return new EnemyObservation { InstanceId = id, X = x, Z = z, Strength = strength, State = state, Confidence01 = confidence, IsReinforcement = reinforcement };
    }

    private static void Compare(BattleSnapshot s, SliceGeometry g, string label)
    {
        int slices = Math.Min(s.SliceCount, 360);
        SlicePressureReference.Result[] expected = SlicePressureReference.Build(s, g);
        SlicePressureBuilder.Build(s, g);
        for (int o = 0; o < s.OwnCount; o++)
        {
            for (int k = 0; k < slices; k++)
                AssertTrue(Same(expected[o].Slices[k], s.SlicePressure(o, k)), label + ": own " + o + " slice " + k + " expected " + expected[o].Slices[k] + " got " + s.SlicePressure(o, k));
            for (int f = 0; f < 3; f++)
                AssertTrue(Same(expected[o].Flanks[f], s.FlankPressure(o, f)), label + ": own " + o + " flank " + f + " expected " + expected[o].Flanks[f] + " got " + s.FlankPressure(o, f));
            int closest = s.ClosestVisibleEnemy(o, out float distance);
            AssertEqual(expected[o].Closest, closest, label + ": own " + o + " closest");
            AssertTrue(Same(expected[o].Distance, distance), label + ": own " + o + " distance");
        }
    }

    private static bool Same(float a, float b)
    {
        return BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b) || (float.IsNaN(a) && float.IsNaN(b));
    }
}

// The full scan SlicePressureBuilder.Build shipped at c3f35b1 (O(own x observations)),
// kept as the reference the grid is checked against. Not a test class.
internal static class SlicePressureReference
{
    internal struct Result
    {
        public float[] Slices;
        public float[] Flanks;
        public int Closest;
        public float Distance;
    }

    internal static Result[] Build(BattleSnapshot s, in SliceGeometry g)
    {
        int slices = Math.Min(s.SliceCount, 360);
        float maxRangeSq = g.MaxRangeMeters * g.MaxRangeMeters;
        float flankRadiusSq = g.FlankShiftMeters * g.FlankShiftMeters;
        UnitRecord[] own = s.Own;
        EnemyObservation[] obs = s.Observations;
        int obsCount = s.ObservationCount;
        var results = new Result[s.OwnCount];

        for (int o = 0; o < s.OwnCount; o++)
        {
            float ox = own[o].X;
            float oz = own[o].Z;
            float facing = own[o].RotationY;
            AnglePos(ox, oz, facing - 90f, g.FlankShiftMeters, out float leftX, out float leftZ);
            AnglePos(ox, oz, facing + 90f, g.FlankShiftMeters, out float rightX, out float rightZ);
            AnglePos(ox, oz, facing + 180f, g.FlankShiftMeters, out float rearX, out float rearZ);

            var slice = new float[Math.Max(slices, 1)];
            float left = 0f, right = 0f, rear = 0f;
            int closest = -1;
            float closestSq = float.MaxValue;

            for (int k = 0; k < obsCount; k++)
            {
                if (obs[k].IsReinforcement) continue;
                bool visible = obs[k].State == ObservationState.Visible;
                float weighted = obs[k].Strength * (visible ? 1f : SlicePressureBuilder.RememberedPressureWeight * obs[k].Confidence01);
                float dx = obs[k].X - ox;
                float dz = obs[k].Z - oz;
                float distSq = dx * dx + dz * dz;

                if (visible && distSq < closestSq)
                {
                    closestSq = distSq;
                    closest = k;
                }
                if (slices > 0 && distSq <= maxRangeSq) slice[SlicePressureBuilder.SliceIndex(SlicePressureBuilder.Bearing(dx, dz), slices)] += weighted;
                if (WithinSq(obs[k].X, obs[k].Z, leftX, leftZ, flankRadiusSq)) left += weighted;
                if (WithinSq(obs[k].X, obs[k].Z, rightX, rightZ, flankRadiusSq)) right += weighted;
                if (WithinSq(obs[k].X, obs[k].Z, rearX, rearZ, flankRadiusSq)) rear += weighted;
            }

            results[o] = new Result
            {
                Slices = slice,
                Flanks = new[] { left, right, rear },
                Closest = closest,
                Distance = closest >= 0 ? (float)Math.Sqrt(closestSq) : 0f,
            };
        }
        return results;
    }

    private static void AnglePos(float x, float z, float angleDegrees, float distance, out float px, out float pz)
    {
        double radians = (angleDegrees - 90f) * (Math.PI / 180.0);
        px = x + (float)(Math.Cos(radians) * distance);
        pz = z - (float)(Math.Sin(radians) * distance);
    }

    private static bool WithinSq(float x, float z, float cx, float cz, float radiusSq)
    {
        float dx = x - cx;
        float dz = z - cz;
        return dx * dx + dz * dz <= radiusSq;
    }
}
