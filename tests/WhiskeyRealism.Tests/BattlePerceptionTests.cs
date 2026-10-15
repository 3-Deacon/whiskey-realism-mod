using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Util;
using static TestHarness;

internal static class BattlePerceptionTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("battle-perception mode falls back when fow off", ModeFallsBackWhenFowOff),
            ("battle-perception mode falls back with hq in the saddle", ModeFallsBackWithHqInTheSaddle),
            ("battle-perception mode falls back before grid painted", ModeFallsBackBeforeGridPainted),
            ("battle-perception mode own side uses within-fog", ModeOwnSideUsesWithinFog),
            ("battle-perception config off forces range-only", ConfigOffForcesRangeOnly),
            ("battle-perception mode log keys reset per battle", ModeLogKeysResetPerBattle),
            ("battle-perception visible becomes remembered after two hidden ticks", VisibleBecomesRememberedAfterTwoHiddenTicks),
            ("battle-perception hidden for one tick stays visible", HiddenForOneTickStaysVisible),
            ("battle-perception remembered keeps last visible position", RememberedKeepsLastVisiblePosition),
            ("battle-perception remembered confidence halves at 20 minutes", RememberedConfidenceHalvesAt20Minutes),
            ("battle-perception remembered dropped at 90 minutes", RememberedDroppedAt90Minutes),
            ("battle-perception reappearing enemy resets to visible", ReappearingEnemyResetsToVisible),
            ("battle-perception never-seen hidden enemy is unknown", NeverSeenHiddenEnemyIsUnknown),
            ("battle-perception visible routed enemy is dropped", VisibleRoutedEnemyIsDropped),
            ("battle-perception range-only reach is vanilla list range", RangeOnlyReachIsVanillaListRange),
            ("battle-perception range-only routed enemy in reach is dropped", RangeOnlyRoutedEnemyInReachIsDropped),
            ("battle-perception reinforcement not remembered once entry hidden", ReinforcementNotRememberedOnceEntryHidden),
            ("battle-perception slice pressure counts only visible plus discounted remembered", SlicePressureCountsOnlyVisiblePlusDiscountedRemembered),
            ("battle-perception slice binning matches vanilla angles", SliceBinningMatchesVanillaAngles),
            ("battle-perception flank pressure left right rear", FlankPressureLeftRightRear),
            ("battle-perception closest visible enemy ignores hidden", ClosestVisibleEnemyIgnoresHidden),
            ("battle-perception snapshot strength and centroid from observations", SnapshotStrengthAndCentroidFromObservations),
            ("battle-perception reinforcement counts only with visible entry", ReinforcementCountsOnlyWithVisibleEntry),
            ("battle-perception update allocates nothing after warmup", PerceptionUpdateAllocatesNothingAfterWarmup),
        });
    }

    // ---------------------------------------------------------------- mode decision

    private static PerceptionMode Decide(bool configOn = true, bool useFow = true, bool hq = false, bool firstRun = true,
        double seconds = 60.0, bool playerSide = false)
    {
        return BattlePerception.DecideMode(configOn, useFow, hq, firstRun, seconds, playerSide, out _);
    }

    private static PerceptionFallbackReason Reason(bool configOn = true, bool useFow = true, bool hq = false, bool firstRun = true,
        double seconds = 60.0, bool playerSide = false)
    {
        BattlePerception.DecideMode(configOn, useFow, hq, firstRun, seconds, playerSide, out PerceptionFallbackReason reason);
        return reason;
    }

    private static void ModeFallsBackWhenFowOff()
    {
        AssertEqual(PerceptionMode.FogGrid, Decide(), "all inputs healthy -> fog grid");
        AssertEqual(PerceptionFallbackReason.None, Reason(), "no fallback reason");
        AssertEqual(PerceptionMode.RangeOnly, Decide(useFow: false), "usefow off -> range only");
        AssertEqual(PerceptionFallbackReason.FogDisabled, Reason(useFow: false), "usefow off reason");
        AssertEqual(PerceptionMode.RangeOnly, Decide(useFow: false, playerSide: true), "usefow off beats the player-side mode");
    }

    private static void ModeFallsBackWithHqInTheSaddle()
    {
        AssertEqual(PerceptionMode.RangeOnly, Decide(hq: true), "hq in the saddle blanks fows[1] (101312)");
        AssertEqual(PerceptionFallbackReason.HqInTheSaddle, Reason(hq: true), "hq reason");
    }

    private static void ModeFallsBackBeforeGridPainted()
    {
        AssertEqual(PerceptionMode.RangeOnly, Decide(firstRun: false), "firstrunfinished false");
        AssertEqual(PerceptionFallbackReason.GridNotPainted, Reason(firstRun: false), "first-run reason");
        AssertEqual(PerceptionMode.RangeOnly, Decide(seconds: BattlePerception.FogStartupSeconds - 0.1), "inside startup window");
        AssertEqual(PerceptionFallbackReason.GridNotPainted, Reason(seconds: 1.0), "startup reason");
        AssertEqual(PerceptionMode.FogGrid, Decide(seconds: BattlePerception.FogStartupSeconds), "startup window ends at 5 s");
        AssertEqual(5f, BattlePerception.FogStartupSeconds, "FogStartupSeconds pinned");
    }

    private static void ModeOwnSideUsesWithinFog()
    {
        AssertEqual(PerceptionMode.OwnSideWithinFog, Decide(playerSide: true), "player-alliance AI side");
        AssertEqual(PerceptionFallbackReason.None, Reason(playerSide: true), "no fallback for within-fog");
        AssertEqual(PerceptionMode.OwnSideWithinFog, Decide(playerSide: true, hq: true),
            "iswithinfog already carries the hq-in-the-saddle rule (127211-127225)");
        AssertEqual(PerceptionMode.OwnSideWithinFog, Decide(playerSide: true, firstRun: false, seconds: 0.0),
            "fows[0] is cleared at init, no startup transient");
    }

    private static void ConfigOffForcesRangeOnly()
    {
        AssertEqual(PerceptionMode.RangeOnly, Decide(configOn: false), "config off");
        AssertEqual(PerceptionFallbackReason.ConfigOff, Reason(configOn: false), "config off reason");
        AssertEqual(PerceptionFallbackReason.ConfigOff, Reason(configOn: false, useFow: false, playerSide: true),
            "config off outranks every other reason");
    }

    private static void ModeLogKeysResetPerBattle()
    {
        OnceLog.Reset();
        var modes = (PerceptionMode[])Enum.GetValues(typeof(PerceptionMode));
        for (int i = 0; i < modes.Length; i++)
        {
            string key = BattlePerception.ModeLogKey(modes[i]);
            AssertTrue(key.StartsWith(OnceLog.TacticalKeyPrefix, StringComparison.Ordinal), "tactical prefix: " + key);
            AssertTrue(key.EndsWith(modes[i].ToString(), StringComparison.Ordinal), "names the mode: " + key);
            for (int j = 0; j < i; j++)
                AssertFalse(key == BattlePerception.ModeLogKey(modes[j]), "distinct per mode: " + key);
        }
        string fog = BattlePerception.ModeLogKey(PerceptionMode.FogGrid);
        OnceLog.Info(fog, "[BattlePerception] test");
        int fired = OnceLog.FiredCount;
        OnceLog.ResetTactical();
        AssertEqual(fired - 1, OnceLog.FiredCount, "battle end releases the mode key");
        OnceLog.Reset();
    }

    // ---------------------------------------------------------------- memory

    private sealed class Probe
    {
        internal PerceptionProbeResult Result;

        internal Probe(int capacity)
        {
            Result = PerceptionProbeResult.Create(capacity);
        }

        internal Probe Clear()
        {
            Result.Count = 0;
            return this;
        }

        internal Probe Add(int id, bool visible, float x, float z, float strength = 500f, bool routed = false,
            bool reinforcement = false, float arrivalHours = 0f)
        {
            int i = Result.Count++;
            Result.EnemyIds[i] = id;
            Result.Visible[i] = visible;
            Result.X[i] = x;
            Result.Z[i] = z;
            Result.Strength[i] = strength;
            Result.UnitTyp[i] = 1;
            Result.Routed[i] = routed;
            Result.IsReinforcement[i] = reinforcement;
            Result.ArrivalHours[i] = arrivalHours;
            return this;
        }
    }

    private sealed class Memory
    {
        internal readonly EnemyObservation[] Items = new EnemyObservation[64];
        internal int Count;

        internal void Tick(Probe probe, double nowHours)
        {
            BattlePerception.Update(Items, ref Count, in probe.Result, nowHours);
        }

        internal int IndexOf(int id)
        {
            for (int i = 0; i < Count; i++)
                if (Items[i].InstanceId == id) return i;
            return -1;
        }
    }

    private const double Minute = 1.0 / 60.0;

    private static void VisibleBecomesRememberedAfterTwoHiddenTicks()
    {
        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(7, true, 100f, -200f), 10.0);
        AssertEqual(ObservationState.Visible, memory.Items[memory.IndexOf(7)].State, "tick 1 visible");
        memory.Tick(probe.Clear().Add(7, false, 140f, -260f), 10.0 + Minute);
        AssertEqual(ObservationState.Visible, memory.Items[memory.IndexOf(7)].State, "tick 2 hidden once: still visible");
        memory.Tick(probe.Clear().Add(7, false, 150f, -270f), 10.0 + 2 * Minute);
        int i = memory.IndexOf(7);
        AssertEqual(ObservationState.Remembered, memory.Items[i].State, "tick 3 hidden twice: remembered");
        AssertEqual(BattlePerception.HideConfirmTicks, memory.Items[i].HiddenTicks, "hidden tick count");
        AssertEqual(2, BattlePerception.HideConfirmTicks, "HideConfirmTicks pinned");
    }

    private static void HiddenForOneTickStaysVisible()
    {
        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(7, true, 100f, -200f), 10.0);
        memory.Tick(probe.Clear().Add(7, false, 100f, -200f), 10.0 + Minute);
        memory.Tick(probe.Clear().Add(7, true, 105f, -205f), 10.0 + 2 * Minute);
        int i = memory.IndexOf(7);
        AssertEqual(ObservationState.Visible, memory.Items[i].State, "flicker stays visible");
        AssertEqual(0, memory.Items[i].HiddenTicks, "hidden ticks reset");
        AssertNear(1f, memory.Items[i].Confidence01, 0.0001f, "full confidence");
    }

    private static void RememberedKeepsLastVisiblePosition()
    {
        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(7, true, 100f, -200f, strength: 800f), 10.0);
        memory.Tick(probe.Clear().Add(7, false, 300f, -400f, strength: 50f), 10.0 + Minute);
        memory.Tick(probe.Clear().Add(7, false, 500f, -600f, strength: 10f), 10.0 + 2 * Minute);
        int i = memory.IndexOf(7);
        AssertEqual(ObservationState.Remembered, memory.Items[i].State, "remembered");
        AssertNear(100f, memory.Items[i].X, 0.001f, "x is the last visible x, not the true one");
        AssertNear(-200f, memory.Items[i].Z, 0.001f, "z is the last visible z");
        AssertNear(800f, memory.Items[i].Strength, 0.001f, "strength is the last visible strength");
        AssertTrue(Math.Abs(memory.Items[i].LastSeenHours - 10.0) < 1e-9, "last seen stays at the last visible tick");
    }

    private static void RememberedConfidenceHalvesAt20Minutes()
    {
        AssertEqual(20f, BattlePerception.RememberedHalfLifeMinutes, "half-life pinned");
        AssertNear(1f, BattlePerception.Confidence(0.0), 0.0001f, "fresh");
        AssertNear(0.5f, BattlePerception.Confidence(20 * Minute), 0.0001f, "20 minutes");
        AssertNear(0.25f, BattlePerception.Confidence(40 * Minute), 0.0001f, "40 minutes");
        AssertNear(0.5f * 0.5f * 0.5f * 0.5f, BattlePerception.Confidence(80 * Minute), 0.0001f, "80 minutes above the floor");
        AssertEqual(BattlePerception.RememberedWeightFloor, BattlePerception.Confidence(88 * Minute), "floor until the cap");
        AssertEqual(0f, BattlePerception.Confidence(BattlePerception.RememberedMaxMinutes * Minute), "zero at the cap");

        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(7, true, 0f, -10f), 10.0);
        memory.Tick(probe.Clear(), 10.0 + Minute);
        memory.Tick(probe.Clear(), 10.0 + 20 * Minute);
        int i = memory.IndexOf(7);
        AssertEqual(ObservationState.Remembered, memory.Items[i].State, "absent from the probe counts as hidden");
        AssertNear(0.5f, memory.Items[i].Confidence01, 0.0001f, "confidence decays on the doctrine clock");
    }

    private static void RememberedDroppedAt90Minutes()
    {
        AssertEqual(90f, BattlePerception.RememberedMaxMinutes, "max minutes pinned");
        AssertEqual(0.05f, BattlePerception.RememberedWeightFloor, "weight floor pinned");

        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(7, true, 0f, -10f), 10.0);
        memory.Tick(probe.Clear(), 10.0 + Minute);
        memory.Tick(probe.Clear(), 10.0 + 2 * Minute);
        memory.Tick(probe.Clear(), 10.0 + 89 * Minute);
        int i = memory.IndexOf(7);
        AssertTrue(i >= 0, "kept at 89 minutes: the floor does not drop it early");
        AssertNear(BattlePerception.RememberedWeightFloor, memory.Items[i].Confidence01, 0.0001f,
            "0.5^(89/20) = 0.046 held at the floor");
        memory.Tick(probe.Clear(), 10.0 + 90 * Minute);
        AssertEqual(-1, memory.IndexOf(7), "dropped at 90 minutes");
        AssertEqual(0, memory.Count, "memory empty");
    }

    private static void ReappearingEnemyResetsToVisible()
    {
        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(7, true, 100f, -200f), 10.0);
        memory.Tick(probe.Clear(), 10.0 + Minute);
        memory.Tick(probe.Clear(), 10.0 + 30 * Minute);
        AssertEqual(ObservationState.Remembered, memory.Items[memory.IndexOf(7)].State, "remembered first");
        memory.Tick(probe.Clear().Add(7, true, 420f, -380f, strength: 650f), 10.0 + 31 * Minute);
        int i = memory.IndexOf(7);
        AssertEqual(ObservationState.Visible, memory.Items[i].State, "visible again");
        AssertNear(420f, memory.Items[i].X, 0.001f, "position refreshed");
        AssertNear(650f, memory.Items[i].Strength, 0.001f, "strength refreshed");
        AssertNear(1f, memory.Items[i].Confidence01, 0.0001f, "confidence reset");
        AssertEqual(0, memory.Items[i].HiddenTicks, "hidden ticks reset");
        AssertTrue(Math.Abs(memory.Items[i].LastSeenHours - (10.0 + 31 * Minute)) < 1e-9, "last seen refreshed");
        AssertEqual(1, memory.Count, "no duplicate entry");
    }

    private static void NeverSeenHiddenEnemyIsUnknown()
    {
        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(7, false, 100f, -200f).Add(8, true, 50f, -50f), 10.0);
        AssertEqual(-1, memory.IndexOf(7), "hidden and never seen: not in memory");
        AssertTrue(memory.IndexOf(8) >= 0, "visible enemy recorded");
        AssertEqual(1, memory.Count, "one entry");
    }

    private static void VisibleRoutedEnemyIsDropped()
    {
        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(7, true, 100f, -200f).Add(8, true, 0f, -20f), 10.0);
        memory.Tick(probe.Clear().Add(7, true, 120f, -220f, routed: true).Add(8, false, 0f, -20f, routed: true), 10.0 + Minute);
        AssertEqual(-1, memory.IndexOf(7), "seen routing: out of the fight");
        AssertTrue(memory.IndexOf(8) >= 0, "routed out of sight: still an unknown, kept");
        memory.Tick(probe.Clear().Add(9, true, 0f, -20f, routed: true), 10.0 + 2 * Minute);
        AssertEqual(-1, memory.IndexOf(9), "a routed enemy is never added");
    }

    private static void RangeOnlyReachIsVanillaListRange()
    {
        AssertEqual(250f, BattlePerception.RangeOnlyReach(250f, 150f), "line of sight beyond the cancel distance");
        AssertEqual(150f, BattlePerception.RangeOnlyReach(20f, 150f), "cancel distance floors a short sight");
    }

    // Vanilla's range lists drop routed enemies (122644), so range-only perception sees a
    // routing enemy only through the same reach test the probe applies to it.
    private static void RangeOnlyRoutedEnemyInReachIsDropped()
    {
        float[] ownX = { 0f, 1000f };
        float[] ownZ = { -500f, -500f };
        float[] reach = { BattlePerception.RangeOnlyReach(200f, 150f), BattlePerception.RangeOnlyReach(20f, 150f) };
        bool near = BattlePerception.WithinAnyReach(ownX, ownZ, reach, 2, 0f, -320f);
        bool nearSecond = BattlePerception.WithinAnyReach(ownX, ownZ, reach, 2, 1000f, -360f);
        bool far = BattlePerception.WithinAnyReach(ownX, ownZ, reach, 2, 500f, -900f);
        AssertTrue(near, "180 m from a 200 m unit");
        AssertTrue(nearSecond, "140 m from a unit floored at 150 m");
        AssertFalse(far, "out of every reach");
        AssertFalse(BattlePerception.WithinAnyReach(ownX, ownZ, reach, 0, 0f, -500f), "no own units");

        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(7, true, 0f, -320f).Add(8, true, 500f, -900f), 10.0);
        memory.Tick(probe.Clear().Add(7, near, 0f, -320f, routed: true).Add(8, far, 500f, -900f, routed: true), 10.0 + Minute);
        AssertEqual(-1, memory.IndexOf(7), "routing within reach: seen routing, dropped");
        AssertTrue(memory.IndexOf(8) >= 0, "routing out of reach: unknown, kept");
    }

    private static void ReinforcementNotRememberedOnceEntryHidden()
    {
        var memory = new Memory();
        var probe = new Probe(8);
        memory.Tick(probe.Clear().Add(70, true, 900f, -50f, strength: 3000f, reinforcement: true, arrivalHours: 2f), 10.0);
        int i = memory.IndexOf(70);
        AssertTrue(i >= 0 && memory.Items[i].IsReinforcement, "visible entry point recorded as reinforcement");
        memory.Tick(probe.Clear().Add(70, false, 900f, -50f, strength: 3000f, reinforcement: true, arrivalHours: 1.9f), 10.0 + Minute);
        AssertEqual(-1, memory.IndexOf(70), "entry point out of sight: the column is not remembered");
    }

    // ---------------------------------------------------------------- snapshot outputs

    // Vanilla battleprefs.txt: aidefensiveslices 20, aidefensivemaxrange 250;
    // unitprefs.txt: flankpositionshift 150.
    private const int VanillaSlices = 20;

    private static SliceGeometry VanillaGeometry()
    {
        return new SliceGeometry { MaxRangeMeters = 250f, FlankShiftMeters = 150f };
    }

    // What BattleSnapshotBuilder does before perception runs: begin, slice count, roster,
    // command-tree index (which clears the per-unit perception arrays).
    private static void Rebuild(BattleSnapshot s, UnitRecord[] own, int ownCount, double nowHours)
    {
        s.BeginBuild(1, 0, 0, 1, nowHours);
        s.SetSliceCount(VanillaSlices);
        for (int i = 0; i < ownCount; i++) s.AddOwn(in own[i]);
        s.IndexCommandTree(null);
    }

    private static UnitRecord OwnAt(int id, float x, float z, float facing = 0f)
    {
        return new UnitRecord { InstanceId = id, X = x, Z = z, RotationY = facing, ParentIndex = -1 };
    }

    private static void Perceive(BattleSnapshot s, Memory memory, Probe probe, UnitRecord[] own, int ownCount, double nowHours,
        PerceptionMode mode = PerceptionMode.FogGrid)
    {
        Rebuild(s, own, ownCount, nowHours);
        memory.Tick(probe, nowHours);
        BattlePerception.Publish(s, mode, PerceptionFallbackReason.None, memory.Items, memory.Count, in probe.Result);
        SlicePressureBuilder.Build(s, VanillaGeometry());
    }

    private static float SliceSum(BattleSnapshot s, int ownIndex)
    {
        float total = 0f;
        for (int k = 0; k < s.SliceCount; k++) total += s.SlicePressure(ownIndex, k);
        return total;
    }

    private static void SlicePressureCountsOnlyVisiblePlusDiscountedRemembered()
    {
        AssertEqual(0.5f, SlicePressureBuilder.RememberedPressureWeight, "remembered weight pinned");
        var s = new BattleSnapshot();
        var memory = new Memory();
        var probe = new Probe(8);
        var own = new[] { OwnAt(1, 0f, -500f) };
        // 7 seen then hidden for 20 minutes (confidence 0.5); 8 visible; 9 never seen.
        memory.Tick(probe.Clear().Add(7, true, 0f, -400f, strength: 400f), 10.0);
        memory.Tick(probe.Clear().Add(7, false, 0f, -400f, strength: 400f), 10.0 + Minute);
        Perceive(s, memory, probe.Clear()
            .Add(7, false, 0f, -400f, strength: 400f)
            .Add(8, true, 0f, -300f, strength: 1000f)
            .Add(9, false, 0f, -350f, strength: 5000f), own, 1, 10.0 + 20 * Minute);
        AssertNear(1000f + 0.5f * 0.5f * 400f, SliceSum(s, 0), 0.01f, "visible + 0.5 x confidence x remembered; hidden never-seen excluded");
        AssertNear(1000f + 100f, s.SlicePressure(0, 0), 0.01f, "both due north land in slice 0");
        AssertEqual(0f, s.SlicePressure(0, 99), "out-of-range slice reads 0");
        AssertEqual(0f, s.SlicePressure(5, 0), "out-of-range own index reads 0");
    }

    // Literal port of UpdateUnitRangeFast 122717-122732: bearing = GetAngle(own, enemy) + 180,
    // normalized to [0, 360), bin = floor(bearing / aidefensiveslices).
    private static int VanillaSlice(float ox, float oz, float ex, float ez, int slices)
    {
        float num7 = (float)(Math.Atan2(ex - ox, ez - oz) / Math.PI * 180.0) - 180f + 180f;
        for (; num7 < 0f; num7 += 360f) { }
        while (num7 >= 360f) num7 -= 360f;
        return Math.Min(360, Math.Max(0, (int)Math.Floor(num7 / slices)));
    }

    private static void SliceBinningMatchesVanillaAngles()
    {
        float[] bearings = { 3f, 47f, 90f + 7f, 133f, 181f, 229f, 271f, 302f, 333f, 357f };
        var s = new BattleSnapshot();
        var memory = new Memory();
        var probe = new Probe(16).Clear();
        var own = new[] { OwnAt(1, 1000f, -1000f) };
        for (int i = 0; i < bearings.Length; i++)
        {
            double rad = bearings[i] * Math.PI / 180.0;
            probe.Add(100 + i, true, 1000f + (float)(Math.Sin(rad) * 200.0), -1000f + (float)(Math.Cos(rad) * 200.0), strength: 1f + i);
        }
        probe.Add(200, true, 1000f, -1000f + 260f, strength: 999f); // beyond aidefensivemaxrange
        Perceive(s, memory, probe, own, 1, 10.0);

        AssertEqual(20, s.SliceCount, "slice count is aidefensiveslices");
        var expected = new float[VanillaSlices];
        for (int i = 0; i < bearings.Length; i++)
        {
            int k = VanillaSlice(1000f, -1000f, probe.Result.X[i], probe.Result.Z[i], VanillaSlices);
            AssertEqual(k, SlicePressureBuilder.SliceIndex(SlicePressureBuilder.Bearing(
                probe.Result.X[i] - 1000f, probe.Result.Z[i] + 1000f), VanillaSlices), "slice index at " + bearings[i]);
            expected[k] += 1f + i;
        }
        for (int k = 0; k < VanillaSlices; k++)
            AssertNear(expected[k], s.SlicePressure(0, k), 0.001f, "slice " + k);
        AssertEqual(0f, s.SlicePressure(0, 18), "slot 18 never filled (20-degree bins)");
        AssertEqual(0f, s.SlicePressure(0, 19), "slot 19 never filled");
        AssertEqual(4, SlicePressureBuilder.SliceIndex(90f, 20), "east is bin 4 (90 / 20)");
        AssertEqual(9, SlicePressureBuilder.SliceIndex(180f, 20), "south is bin 9");
        AssertEqual(9, SlicePressureBuilder.SliceIndex(359f, 10), "vanilla would overrun a 10-slot array; clamped");
    }

    private static void FlankPressureLeftRightRear()
    {
        var s = new BattleSnapshot();
        var memory = new Memory();
        var probe = new Probe(8);
        // Facing east (90): left is north (+z), right is south (-z), rear is west (-x).
        var own = new[] { OwnAt(1, 1000f, -1000f, facing: 90f) };
        Perceive(s, memory, probe.Clear()
            .Add(7, true, 1000f, -1000f + 160f, strength: 100f)
            .Add(8, true, 1000f, -1000f - 160f, strength: 200f)
            .Add(9, true, 1000f - 170f, -1000f, strength: 300f)
            .Add(10, true, 1000f + 290f, -1000f, strength: 400f), own, 1, 10.0);
        AssertNear(100f, s.FlankPressure(0, 0), 0.01f, "left flank");
        AssertNear(200f, s.FlankPressure(0, 1), 0.01f, "right flank");
        AssertNear(300f, s.FlankPressure(0, 2), 0.01f, "rear");
        AssertEqual(0f, s.FlankPressure(0, 3), "unknown flank reads 0");
    }

    private static void ClosestVisibleEnemyIgnoresHidden()
    {
        var s = new BattleSnapshot();
        var memory = new Memory();
        var probe = new Probe(8);
        var own = new[] { OwnAt(1, 0f, -500f), OwnAt(2, 2000f, -500f) };
        memory.Tick(probe.Clear().Add(7, true, 0f, -480f), 10.0);
        memory.Tick(probe.Clear().Add(7, false, 0f, -480f), 10.0 + Minute);
        Perceive(s, memory, probe.Clear()
            .Add(7, false, 0f, -480f)
            .Add(8, true, 0f, -700f)
            .Add(9, false, 0f, -505f)
            .Add(70, true, 0f, -510f, reinforcement: true, arrivalHours: 1f), own, 2, 10.0 + 2 * Minute);
        int k = s.ClosestVisibleEnemy(0, out float distance);
        AssertTrue(k >= 0, "found one");
        AssertEqual(8, s.Observations[k].InstanceId, "remembered, never-seen and reinforcement rows skipped");
        AssertNear(200f, distance, 0.01f, "true XZ distance");
        int far = s.ClosestVisibleEnemy(1, out float farDistance);
        AssertEqual(8, s.Observations[far].InstanceId, "second unit sees the same enemy");
        AssertNear((float)Math.Sqrt(2000.0 * 2000.0 + 200.0 * 200.0), farDistance, 0.05f, "no range cap on closest");
        AssertEqual(-1, s.ClosestVisibleEnemy(7, out _), "unknown own index");
        AssertTrue(s.IsVisible(8), "8 visible");
        AssertFalse(s.IsVisible(7), "7 remembered, not visible");
        AssertFalse(s.IsVisible(9), "9 unknown");
    }

    private static void SnapshotStrengthAndCentroidFromObservations()
    {
        var s = new BattleSnapshot();
        var memory = new Memory();
        var probe = new Probe(8);
        var own = new[] { OwnAt(1, 0f, -500f) };
        memory.Tick(probe.Clear().Add(7, true, 0f, -100f, strength: 400f), 10.0);
        memory.Tick(probe.Clear(), 10.0 + Minute);
        Perceive(s, memory, probe.Clear()
            .Add(8, true, 100f, -200f, strength: 300f)
            .Add(9, true, 400f, -200f, strength: 100f), own, 1, 10.0 + 20 * Minute, PerceptionMode.OwnSideWithinFog);
        AssertEqual(PerceptionMode.OwnSideWithinFog, s.Perception, "mode stored");
        AssertEqual(PerceptionFallbackReason.None, s.PerceptionFallback, "reason stored");
        AssertEqual(3, s.ObservationCount, "two visible plus one remembered");
        AssertNear(400f, s.VisibleEnemyStrength, 0.01f, "visible strength");
        AssertNear(200f, s.RememberedEnemyStrength, 0.01f, "remembered strength is confidence-weighted");
        AssertNear(175f, s.VisibleEnemyCentroidX, 0.01f, "strength-weighted centroid x");
        AssertNear(-200f, s.VisibleEnemyCentroidZ, 0.01f, "centroid z");
    }

    private static void ReinforcementCountsOnlyWithVisibleEntry()
    {
        var s = new BattleSnapshot();
        var memory = new Memory();
        var probe = new Probe(8);
        var own = new[] { OwnAt(1, 0f, -500f) };
        Perceive(s, memory, probe.Clear()
            .Add(70, true, 900f, -50f, strength: 3000f, reinforcement: true, arrivalHours: 2.5f)
            .Add(71, false, 50f, -50f, strength: 9000f, reinforcement: true, arrivalHours: 0.5f)
            .Add(72, true, 950f, -60f, strength: 1000f, reinforcement: true, arrivalHours: 4f)
            .Add(8, true, 0f, -400f, strength: 600f), own, 1, 10.0);
        AssertNear(4000f, s.VisibleReinforcementStrength, 0.01f, "only columns at visible entry points");
        AssertNear(2.5f, s.NearestVisibleReinforcementHours, 0.0001f, "nearest visible arrival");
        AssertNear(600f, s.VisibleEnemyStrength, 0.01f, "reinforcements are not on-map strength");
        AssertNear(600f, SliceSum(s, 0), 0.01f, "reinforcements add no slice pressure");

        Perceive(s, memory, probe.Clear().Add(71, false, 50f, -50f, strength: 9000f, reinforcement: true, arrivalHours: 0.4f),
            own, 1, 10.0 + Minute);
        AssertEqual(0f, s.VisibleReinforcementStrength, "nothing visible");
        AssertEqual(float.MaxValue, s.NearestVisibleReinforcementHours, "no visible arrival keeps BeginBuild's sentinel");
    }

    private static void PerceptionUpdateAllocatesNothingAfterWarmup()
    {
        const int enemies = 300;
        const int owns = 300;
        var s = new BattleSnapshot();
        var memory = new EnemyObservation[enemies * 2];
        int memoryCount = 0;
        var probe = PerceptionProbeResult.Create(enemies);
        var own = new UnitRecord[owns];
        for (int i = 0; i < owns; i++) own[i] = OwnAt(1000 + i, (i % 30) * 40f, -2000f + (i / 30) * 40f, facing: (i * 37) % 360);
        var geometry = VanillaGeometry();

        void Tick(int tick)
        {
            probe.Count = enemies;
            for (int i = 0; i < enemies; i++)
            {
                probe.EnemyIds[i] = 5000 + i;
                probe.Visible[i] = ((i + tick) % 3) != 0;
                probe.X[i] = (i % 30) * 40f + tick;
                probe.Z[i] = -1700f + (i / 30) * 40f;
                probe.Strength[i] = 300f + i;
                probe.UnitTyp[i] = 1;
                probe.Routed[i] = false;
                probe.IsReinforcement[i] = i >= enemies - 5;
                probe.ArrivalHours[i] = 1f;
            }
            double now = 10.0 + tick * Minute;
            Rebuild(s, own, owns, now);
            BattlePerception.Update(memory, ref memoryCount, in probe, now);
            BattlePerception.Publish(s, PerceptionMode.FogGrid, PerceptionFallbackReason.None, memory, memoryCount, in probe);
            SlicePressureBuilder.Build(s, geometry);
        }

        Tick(0);
        Tick(1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 2; t < 6; t++) Tick(t);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertTrue(s.ObservationCount > 0, "observations filled");
        AssertEqual(0L, allocated, "perception tick allocated bytes after warm-up");
    }
}
