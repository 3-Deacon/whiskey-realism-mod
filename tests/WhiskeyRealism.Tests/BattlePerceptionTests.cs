using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
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
            ("battle-perception painted grid latch is per side", PaintedGridLatchIsPerSide),
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
            // BC2b: the T1 consumers read the snapshot.
            ("battle-perception snapshot is the observation source", SnapshotIsTheObservationSource),
            ("battle-perception objective records from snapshot match aggregate fixture", ObjectiveRecordsFromSnapshotMatchAggregateFixture),
            ("battle-perception evidence sector strength from observations not vanilla slices", EvidenceSectorStrengthFromObservationsNotVanillaSlices),
            ("battle-perception enemy visible state excludes hidden enemies", EnemyVisibleStateExcludesHiddenEnemies),
            ("battle-perception contact ledger fed from snapshot observations", ContactLedgerFedFromSnapshotObservations),
            ("battle-perception own averages from snapshot own", OwnAveragesFromSnapshotOwn),
            ("battle-perception own reinforcements use vanilla retreat window", OwnReinforcementsUseVanillaRetreatWindow),
            ("battle-perception group flank threat from snapshot pressure", GroupFlankThreatFromSnapshotPressure),
            ("battle-perception unseen enemy avenues are filtered", UnseenEnemyAvenuesAreFiltered),
            ("battle-perception signature inputs from snapshot", SignatureInputsFromSnapshot),
            ("battle-perception group pressure helpers read the snapshot", GroupPressureHelpersReadTheSnapshot),
            ("battle-perception evidence front axis is the snapshot axis", EvidenceFrontAxisIsTheSnapshotAxis),
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

    // W&L / ai_vs_ai: both sides perceive, each through its own grid (fows[0] / fows[1]).
    private static void PaintedGridLatchIsPerSide()
    {
        var latch = new PaintedGridLatch(2);
        object grid0 = new object(), grid1 = new object();
        AssertFalse(latch.IsPainted(0, grid0), "nothing latched yet");
        latch.MarkPainted(0, grid0);
        latch.MarkPainted(1, grid1);
        for (int tick = 0; tick < 3; tick++)
        {
            AssertTrue(latch.IsPainted(0, grid0), "side 0 stays latched while side 1 runs");
            AssertTrue(latch.IsPainted(1, grid1), "side 1 stays latched while side 0 runs");
        }
        AssertFalse(latch.IsPainted(0, grid1), "another side's grid is not side 0's");
        AssertFalse(latch.IsPainted(1, new object()), "a new grid object needs its own read");
        AssertFalse(latch.IsPainted(0, null), "no grid");
        AssertFalse(latch.IsPainted(2, grid0), "Europe has no slot");
        latch.MarkPainted(2, grid0);
        latch.Reset();
        AssertFalse(latch.IsPainted(0, grid0), "battle end clears side 0");
        AssertFalse(latch.IsPainted(1, grid1), "battle end clears side 1");
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

    // ---------------------------------------------------------------- BC2b consumers

    // A snapshot as the coordinator hands it to the T1 consumers: own roster, observations
    // as perception published them, slice / flank / closest-visible rows built.
    private static BattleSnapshot ConsumerSnapshot(double nowHours, UnitRecord[] own, params EnemyObservation[] observations)
    {
        var s = new BattleSnapshot();
        Rebuild(s, own, own.Length, nowHours);
        for (int i = 0; i < observations.Length; i++) s.AddObservation(in observations[i]);
        SlicePressureBuilder.Build(s, VanillaGeometry());
        return s;
    }

    private static EnemyObservation Seen(int id, float x, float z, float strength, float confidence = 1f,
        bool remembered = false, bool reinforcement = false)
    {
        return new EnemyObservation
        {
            InstanceId = id,
            UnitTyp = 0,
            X = x,
            Z = z,
            Strength = strength,
            State = remembered ? ObservationState.Remembered : ObservationState.Visible,
            Confidence01 = remembered ? confidence : 1f,
            IsReinforcement = reinforcement,
        };
    }

    private static UnitRecord OwnUnit(int id, float x, float z, int aiGroupStrength = 0, int unitTyp = 0, int strength = 500,
        UnitFlags flags = UnitFlags.Active | UnitFlags.Alive, float waypointX = 0f, float waypointZ = 0f, float facing = 0f)
    {
        return new UnitRecord
        {
            InstanceId = id, X = x, Z = z, RotationY = facing, UnitTyp = unitTyp, Strength = strength,
            GroupStrengthAiGroup = aiGroupStrength, Flags = flags, LastWaypointX = waypointX, LastWaypointZ = waypointZ,
            ParentIndex = -1,
        };
    }

    // Ports TacticalUnitObservationAggregateTests: the snapshot replaced the pooled aggregate as
    // the IObservationSource, and a rebuild into the same instance drops the previous capture.
    private static void SnapshotIsTheObservationSource()
    {
        var s = ConsumerSnapshot(10.0, new[] { OwnUnit(1, 0f, -500f), OwnUnit(3, 100f, -500f) }, Seen(2, 0f, -300f, 400f));
        IObservationSource source = s;
        AssertEqual(2, source.OwnCount, "own units");
        AssertEqual(1, source.ObservationCount, "enemy observations");
        AssertEqual(1, source.Own[0].InstanceId, "own order kept");
        AssertEqual(2, source.Observations[0].InstanceId, "observation kept");
        AssertEqual(1, source.IndexOfOwn(3), "own index by instance id");
        AssertEqual(-1, source.IndexOfOwn(2), "an enemy is never an own index");
        AssertEqual(0, source.ClosestVisibleEnemy(0, out float distance), "closest visible enemy");
        AssertNear(200f, distance, 0.01f, "closest distance");

        Rebuild(s, new[] { OwnUnit(4, 0f, -500f) }, 1, 10.1);
        AssertEqual(1, source.OwnCount, "rebuild replaces the roster");
        AssertEqual(0, source.ObservationCount, "rebuild clears the observations");
        AssertEqual(-1, source.IndexOfOwn(1), "previous capture's ids are gone");

        Rebuild(s, Array.Empty<UnitRecord>(), 0, 10.2);
        AssertEqual(0, source.OwnCount, "empty build: no own");
        AssertEqual(-1, source.ClosestVisibleEnemy(0, out _), "empty build: no closest");
    }

    // Ports ObjectiveRecordsFromAggregateTests onto a BattleSnapshot source.
    private static void ObjectiveRecordsFromSnapshotMatchAggregateFixture()
    {
        // Each own unit's closest visible enemy: 11 (100), 12 (200), 13 (300). Weighted
        // centroid X = (100*100 + 200*200 + 300*300) / 600 = 233.33; friendly is the sum of
        // own groupstrengthaigroup (the shipped SafeStrength field).
        var line = ConsumerSnapshot(10.0,
            new[] { OwnUnit(1, 100f, -50f, aiGroupStrength: 100), OwnUnit(2, 200f, -50f, aiGroupStrength: 100),
                OwnUnit(3, 300f, -50f, aiGroupStrength: 100) },
            Seen(11, 100f, 0f, 100f), Seen(12, 200f, 0f, 200f), Seen(13, 300f, 0f, 300f));
        bool ok = TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(
            line, 0, out TacticalMapPoint point, out float enemyStrength, out float friendlyStrength);
        AssertTrue(ok, "visible enemy line ok");
        AssertNear(233.3333f, point.X, 0.1f, "visible enemy line centroid X");
        AssertEqual(0f, point.Z, "visible enemy line centroid Z");
        AssertEqual(600f, enemyStrength, "visible enemy line enemy strength sum");
        AssertEqual(300f, friendlyStrength, "visible enemy line friendly strength sum");

        var routed = ConsumerSnapshot(10.0,
            new[] { OwnUnit(1, 100f, -50f, aiGroupStrength: 100, flags: UnitFlags.Active | UnitFlags.Routed),
                OwnUnit(2, 200f, -50f, aiGroupStrength: 100, flags: UnitFlags.Active | UnitFlags.PermanentlyDetached) },
            Seen(11, 100f, 0f, 100f));
        AssertFalse(TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(routed, 0, out _, out _, out _),
            "routed and permanently detached own units contribute nothing");

        var unseen = ConsumerSnapshot(10.0, new[] { OwnUnit(1, 0f, -500f), OwnUnit(2, 50f, -500f) },
            Seen(11, 0f, -300f, 500f, confidence: 0.5f, remembered: true));
        AssertFalse(TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(unseen, 0, out _, out _, out _),
            "visible enemy line returns false when no own unit has a visible enemy");

        // Strength-weighted centroid of last waypoints: (50*100 + 100*200) / 300 = 83.33.
        var anchor = ConsumerSnapshot(10.0,
            new[] { OwnUnit(1, 0f, -500f, aiGroupStrength: 100, waypointX: 50f, waypointZ: 50f),
                OwnUnit(2, 0f, -500f, aiGroupStrength: 200, waypointX: 100f, waypointZ: 100f) });
        ok = TacticalVisionRuntimeAdapter.TryMovementAnchorLineFromAggregate_ForTest(anchor, 0, out point, out friendlyStrength);
        AssertTrue(ok, "movement anchor ok");
        AssertNear(83.3333f, point.X, 0.1f, "movement anchor centroid X");
        AssertNear(83.3333f, point.Z, 0.1f, "movement anchor centroid Z");
        AssertEqual(300f, friendlyStrength, "movement anchor friendly strength sum");

        var noWaypoint = ConsumerSnapshot(10.0,
            new[] { OwnUnit(1, 0f, -500f), OwnUnit(2, 300f, -500f, waypointX: 310f, waypointZ: -510f) });
        AssertFalse(TacticalVisionRuntimeAdapter.TryMovementAnchorLineFromAggregate_ForTest(noWaypoint, 0, out _, out _),
            "no waypoint, or one within 25 m of the unit: no movement anchor");
    }

    private static void EvidenceSectorStrengthFromObservationsNotVanillaSlices()
    {
        // 21 visible (500, engaged), 22 remembered at confidence 0.5 (400 -> 200), 23 a
        // reinforcement column at a visible entry point (off-map, not sector strength).
        var s = ConsumerSnapshot(10.0, new[] { OwnUnit(1, 0f, -500f) },
            Seen(21, 0f, -300f, 500f), Seen(22, 40f, -320f, 400f, confidence: 0.5f, remembered: true),
            Seen(23, 0f, -310f, 3000f, reinforcement: true));
        var visible = new List<FrontUnitSample>();
        var weighted = new List<FrontUnitSample>();
        var known = new List<FrontUnitSample>();
        ArmyEvidenceBuilder.CollectEnemySamples(s, new HashSet<int> { 21 }, visible, weighted, known);

        float total = 0f;
        for (int i = 0; i < weighted.Count; i++) total += weighted[i].Strength;
        AssertNear(700f, total, 0.01f, "sector strength: visible + confidence-weighted remembered");
        var axis = TacticalFrontAxis.FromCentroids(0f, -500f, 0f, -300f).WithOwnExtent(-100f, 100f);
        var sectors = EnemyVisibleSectorBuilder.BuildSectors(axis, new[] { new FrontUnitSample(1, 0f, -500f, 500f, false) }, weighted);
        float sectorEnemy = 0f;
        for (int i = 0; i < sectors.Length; i++) sectorEnemy += sectors[i].EnemyStrength;
        AssertNear(700f, sectorEnemy, 0.01f, "sectors are built from the observations");
        AssertEqual(2, known.Count, "known positions: visible and remembered, no reinforcement column");
    }

    private static void EnemyVisibleStateExcludesHiddenEnemies()
    {
        var s = ConsumerSnapshot(10.0, new[] { OwnUnit(1, 0f, -500f) },
            Seen(21, 0f, -300f, 500f), Seen(22, 40f, -320f, 400f, confidence: 0.5f, remembered: true),
            Seen(23, 0f, -310f, 3000f, reinforcement: true));
        var visible = new List<FrontUnitSample>();
        var weighted = new List<FrontUnitSample>();
        var known = new List<FrontUnitSample>();
        ArmyEvidenceBuilder.CollectEnemySamples(s, new HashSet<int> { 21, 22 }, visible, weighted, known);
        AssertEqual(1, visible.Count, "only the visible enemy reaches the axis and the contact picture");
        AssertEqual(21, visible[0].InstanceId, "visible enemy id");
        AssertTrue(visible[0].RecentFire, "in an own fire-range list: engaged");
        AssertEqual(500f, visible[0].Strength, "visible strength at full weight");

        ArmyEvidenceBuilder.CollectEnemySamples(s, new HashSet<int>(), visible, weighted, known);
        AssertEqual(1, visible.Count, "buffers are refilled, not appended");
        AssertFalse(visible[0].RecentFire, "not in a fire-range list: seen, not engaged");
    }

    private static void ContactLedgerFedFromSnapshotObservations()
    {
        var ledger = new TacticalEnemyContactLedger();
        var seen = ConsumerSnapshot(10.0, new[] { OwnUnit(1, 0f, -500f) },
            Seen(21, 0f, -300f, 500f), Seen(22, 40f, -320f, 400f, confidence: 0.5f, remembered: true),
            Seen(23, 0f, -310f, 3000f, reinforcement: true));
        ledger.Observe(seen, engagedEnemyStrength: 0f);
        AssertNear(700f, ledger.EstimatedEnemyStrength, 0.01f, "visible plus confidence-weighted remembered, no reinforcement");
        AssertTrue(ledger.HadContact, "a visible enemy is contact");

        // Half an hour later only a fading memory: the estimate decays on the doctrine clock
        // (snapshot.DoctrineHours) and the contact counts as broken.
        var hidden = ConsumerSnapshot(10.5, new[] { OwnUnit(1, 0f, -500f) },
            Seen(22, 40f, -320f, 400f, confidence: 0.25f, remembered: true));
        ledger.Observe(hidden, engagedEnemyStrength: 0f);
        AssertNear(700f * (1f - TacticalEnemyContactLedger.LargestSeenDecayPerHour * 0.5f), ledger.EstimatedEnemyStrength, 0.01f,
            "largest-seen decays by the snapshot's doctrine hours");
        AssertTrue(ledger.ContactBroken(10.5f, hidden.VisibleEnemyStrength), "remembered only: contact broken");

        var none = new TacticalEnemyContactLedger();
        none.Observe(ConsumerSnapshot(10.0, new[] { OwnUnit(1, 0f, -500f) },
            Seen(22, 40f, -320f, 400f, confidence: 0.5f, remembered: true)), engagedEnemyStrength: 0f);
        AssertFalse(none.HadContact, "a remembered enemy alone is no new contact");
        AssertNear(200f, none.EstimatedEnemyStrength, 0.01f, "but it is known strength");
    }

    private static void OwnAveragesFromSnapshotOwn()
    {
        var own = new[]
        {
            OwnUnit(1, 0f, 0f), OwnUnit(2, 0f, 0f), OwnUnit(3, 0f, 0f),
            OwnUnit(4, 0f, 0f, unitTyp: 16),                                      // a group: not sampled
            OwnUnit(5, 0f, 0f, flags: UnitFlags.Active | UnitFlags.Routed),        // routed: not sampled
            OwnUnit(6, 0f, 0f, flags: UnitFlags.Alive),                            // not on the field
            OwnUnit(7, 0f, 0f, strength: 0),                                       // no men
        };
        own[0].Fatigue = 0.2f; own[0].Ammo01 = 1f; own[0].Morale = 40f; own[0].BattleStartMorale = 80f;
        own[1].Fatigue = 0.4f; own[1].Ammo01 = 0.5f; own[1].Morale = 60f; own[1].BattleStartMorale = 80f;
        own[2].Fatigue = 2f; own[2].Ammo01 = 0f;                                   // fatigue clamped, no morale baseline
        for (int i = 3; i < own.Length; i++) { own[i].Fatigue = 0.9f; own[i].Ammo01 = 0f; own[i].Morale = 1f; own[i].BattleStartMorale = 90f; }
        ArmyEvidenceBuilder.OwnAverages(own, own.Length, out float fatigue, out float ammo, out float morale);
        AssertNear((0.2f + 0.4f + 1f) / 3f, fatigue, 0.0001f, "mean fatigue over alive combat leaves");
        AssertNear(0.5f, ammo, 0.0001f, "mean ammo over alive combat leaves");
        AssertNear(100f / 160f, morale, 0.0001f, "morale relative to battle start");

        ArmyEvidenceBuilder.OwnAverages(own, 0, out fatigue, out ammo, out morale);
        AssertEqual(0.2f, fatigue, "nothing sampled: fatigue default");
        AssertEqual(0.9f, ammo, "nothing sampled: ammo default");
        AssertEqual(1f, morale, "nothing sampled: morale default");
    }

    // Vanilla sums own reinforcements due in (0, reinforcementmaxtimeforairetreatdecisions)
    // hours (84764-84767); the side knows its own schedule.
    private static void OwnReinforcementsUseVanillaRetreatWindow()
    {
        var arrivals = new[]
        {
            new ReinforcementArrival(0.5f, 1000f), new ReinforcementArrival(3f, 2000f),
            new ReinforcementArrival(0f, 500f), new ReinforcementArrival(30f, 9000f),
        };
        AssertEqual(3000f, ArmyEvidenceBuilder.ReinforcementsArrivingWithin(arrivals, 24f), "due inside the window");
        AssertEqual(0f, ArmyEvidenceBuilder.ReinforcementsArrivingWithin(Array.Empty<ReinforcementArrival>(), 24f), "none scheduled");
        ArmyEvidenceBuilder.NearestReinforcement(arrivals, out float hours, out float strength);
        AssertEqual(0f, hours, "nearest arrival hours");
        AssertEqual(500f, strength, "nearest arrival strength");
        ArmyEvidenceBuilder.NearestReinforcement(Array.Empty<ReinforcementArrival>(), out hours, out strength);
        AssertEqual(0f, hours, "none: 0 hours");
        AssertEqual(0f, strength, "none: 0 strength");
    }

    // Vanilla's outflanked state comes from range-list enemies at the unit's sides and rear
    // (UpdateUnitRangeFast 122990-123015, set 123151-123175); the group threat is now the
    // same weight over the snapshot's perceived flank pressure.
    private static void GroupFlankThreatFromSnapshotPressure()
    {
        var own = new[]
        {
            OwnUnit(100, 1000f, -1100f, unitTyp: 16, flags: UnitFlags.Active | UnitFlags.Alive | UnitFlags.IsGroup),
            OwnUnit(1, 1000f, -1000f, facing: 90f),
            OwnUnit(2, 5000f, -1000f, facing: 90f),
            OwnUnit(3, 1000f, -1000f, facing: 90f, flags: UnitFlags.Active | UnitFlags.Routed),
        };
        var s = new BattleSnapshot();
        Rebuild(s, own, own.Length, 10.0);
        for (int i = 1; i < own.Length; i++) s.Own[i].ParentIndex = 0;
        s.IndexCommandTree(null);
        var enemy = Seen(9, 1000f, -840f, 100f); // 160 m north of unit 1: its left flank point
        s.AddObservation(in enemy);
        SlicePressureBuilder.Build(s, VanillaGeometry());
        AssertTrue(s.FlankPressure(1, 0) > 0f, "fixture: unit 1 pressed on the left");

        var mean = new float[own.Length];
        var count = new int[own.Length];
        ArmyEvidenceBuilder.MeanFlankThreatByGroup(s, mean, count);
        AssertNear(0.5f, mean[0], 0.0001f, "mean vanilla weight over the group's unrouted combat leaves (1 and 0)");
        AssertEqual(2, count[0], "routed leaf not sampled");
        AssertEqual(0f, mean[1], "a leaf carries no group threat");

        AssertEqual(1, ArmyEvidenceBuilder.OutflankedState(1f, 0f, 0f), "one side");
        AssertEqual(3, ArmyEvidenceBuilder.OutflankedState(1f, 1f, 0f), "both sides");
        AssertEqual(7, ArmyEvidenceBuilder.OutflankedState(1f, 1f, 1f), "surrounded");
        AssertEqual(4f, ArmyEvidenceBuilder.VanillaOutflankedWeight(ArmyEvidenceBuilder.OutflankedState(1f, 1f, 1f)), "surrounded weight");
    }

    private static void UnseenEnemyAvenuesAreFiltered()
    {
        var s = ConsumerSnapshot(10.0, new[] { OwnUnit(1, 0f, -500f) },
            Seen(70, 900f, -50f, 3000f, reinforcement: true));
        TacticalApproachAvenueObservation Avenue(TacticalApproachAvenueSource source, int owner, float x, float z)
            => new TacticalApproachAvenueObservation(source, owner, x, z, x + 100f, z - 100f, 0.8f, false, false, "test");

        AssertTrue(TacticalVisionRuntimeAdapter.IsAvenueKnown(Avenue(TacticalApproachAvenueSource.ScheduledArrival, 0, 50f, -50f), 0, s),
            "own arrivals: the side knows its own schedule");
        AssertTrue(TacticalVisionRuntimeAdapter.IsAvenueKnown(Avenue(TacticalApproachAvenueSource.EntryPoint, 1, 50f, -50f), 0, s),
            "entry point ownership is map knowledge");
        AssertTrue(TacticalVisionRuntimeAdapter.IsAvenueKnown(Avenue(TacticalApproachAvenueSource.ScheduledArrival, 1, 900.5f, -50f), 0, s),
            "enemy column at an entry point perception sees");
        AssertFalse(TacticalVisionRuntimeAdapter.IsAvenueKnown(Avenue(TacticalApproachAvenueSource.ScheduledArrival, 1, 50f, -50f), 0, s),
            "enemy column at an unseen entry point");
        AssertFalse(TacticalVisionRuntimeAdapter.IsAvenueKnown(Avenue(TacticalApproachAvenueSource.DeploymentGroup, 1, 900f, -50f), 0, s),
            "enemy deployment-group objectives are never perceived");
        AssertTrue(TacticalVisionRuntimeAdapter.IsAvenueKnown(Avenue(TacticalApproachAvenueSource.DeploymentGroup, 0, 900f, -50f), 0, s),
            "own deployment groups");
    }

    private static void SignatureInputsFromSnapshot()
    {
        AssertEqual(0, ArmyEvidenceBuilder.KnownEnemyStrengthBucket(0f), "nothing seen");
        AssertEqual(0, ArmyEvidenceBuilder.KnownEnemyStrengthBucket(ArmyEvidenceBuilder.KnownEnemyStrengthBucketMen - 1f), "inside the first bucket");
        AssertEqual(3, ArmyEvidenceBuilder.KnownEnemyStrengthBucket(ArmyEvidenceBuilder.KnownEnemyStrengthBucketMen * 3.5f), "bucketed, not exact");
        AssertEqual(0, ArmyEvidenceBuilder.KnownEnemyStrengthBucket(float.NaN), "NaN reads as nothing");

        var own = new[]
        {
            OwnUnit(1, 0f, -500f), OwnUnit(2, 0f, -500f), OwnUnit(3, 0f, -500f),
            OwnUnit(4, 0f, -500f, flags: UnitFlags.Active | UnitFlags.InMelee),
        };
        own[0].FirstEnemyInRangeId = 21; // visible
        own[1].FirstEnemyInRangeId = 22; // remembered: not contact
        own[2].FirstEnemyInRangeId = 0;
        var s = ConsumerSnapshot(10.0, own, Seen(21, 0f, -300f, 500f), Seen(22, 0f, -320f, 400f, confidence: 0.5f, remembered: true));
        AssertEqual(1, ArmyEvidenceBuilder.ContactBucket(s), "two contacts (visible range-list enemy, melee) -> bucket 1");
        AssertEqual(0, ArmyEvidenceBuilder.ContactBucket(ConsumerSnapshot(10.0, new[] { OwnUnit(1, 0f, -500f) })), "no contact");
    }

    private static void GroupPressureHelpersReadTheSnapshot()
    {
        var s = ConsumerSnapshot(10.0, new[] { OwnUnit(1, 0f, -500f), OwnUnit(2, 5000f, -500f) },
            Seen(21, 0f, -300f, 500f), Seen(22, 0f, -320f, 400f, confidence: 0.5f, remembered: true));
        AssertNear(500f + 0.5f * 0.5f * 400f, ArmyEvidenceBuilder.SlicePressureTotal(s, 0), 0.01f, "sum of the unit's slices");
        AssertEqual(0f, ArmyEvidenceBuilder.SlicePressureTotal(s, 1), "nothing within range of the far unit");
        AssertEqual(0f, ArmyEvidenceBuilder.SlicePressureTotal(s, 7), "unknown own index");
        AssertEqual(0f, ArmyEvidenceBuilder.SlicePressureTotal(null, 0), "no snapshot");
        AssertFalse(ArmyEvidenceBuilder.HasFlankPressure(s, 1), "far unit: no flank pressure");
        AssertFalse(ArmyEvidenceBuilder.HasFlankPressure(null, 0), "no snapshot: no flank pressure");
    }

    // Evidence bands on the snapshot's front axis (BC1 fit, refit on what is seen by BC2a);
    // only before any enemy has given the snapshot a target does it face the nearest open
    // objective, as the shipped evidence axis did.
    private static void EvidenceFrontAxisIsTheSnapshotAxis()
    {
        var own = new[] { OwnUnit(1, -200f, -1000f, strength: 600), OwnUnit(2, 200f, -1000f, strength: 600) };
        var seen = ConsumerSnapshot(10.0, own, Seen(21, 0f, -500f, 800f));
        seen.SetEnemySummary(800f, 0f, 0f, -500f); // what BattlePerception.Publish sets for 21
        BattleSnapshotTestAxis(seen);
        AssertTrue(seen.FrontAxis.TargetDistance > 0f, "fixture: the snapshot axis faces the visible enemy");
        var ownSample = new List<FrontUnitSample> { new FrontUnitSample(1, -200f, -1000f, 600f, false), new FrontUnitSample(2, 200f, -1000f, 600f, false) };
        var visible = new List<FrontUnitSample> { new FrontUnitSample(21, 0f, -500f, 800f, false) };

        TacticalFrontAxis axis = ArmyEvidenceBuilder.EvidenceFrontAxis(seen.FrontAxis, TacticalFrontAxis.None,
            hasOpenObjective: true, objectiveX: 5000f, objectiveZ: -1000f, ownSample, visible);
        AssertNear(seen.FrontAxis.HeadingDegrees, axis.HeadingDegrees, 0.001f, "a snapshot axis with a target is used as is");
        AssertNear(seen.FrontAxis.LateralMin, axis.LateralMin, 0.001f, "its bands too");
        AssertTrue(axis.HasEnemyExtent, "enemy extent kept");

        // Nothing ever seen: the snapshot axis is degenerate (forward +z, no target distance).
        var blind = ConsumerSnapshot(10.0, own);
        BattleSnapshotTestAxis(blind);
        AssertTrue(blind.FrontAxis.IsValid && blind.FrontAxis.TargetDistance <= 0f, "fixture: degenerate snapshot axis");
        axis = ArmyEvidenceBuilder.EvidenceFrontAxis(blind.FrontAxis, TacticalFrontAxis.None,
            hasOpenObjective: true, objectiveX: 2000f, objectiveZ: -1000f, ownSample, Array.Empty<FrontUnitSample>());
        AssertNear(90f, axis.HeadingDegrees, 0.01f, "faces the nearest open objective (due east)");
        AssertNear(blind.FrontAxis.OriginX, axis.OriginX, 0.001f, "from the snapshot's own centroid");
        AssertEqual(TacticalFrontAxis.DefaultBandCount, axis.BandCount, "banded over the own sample");
        AssertNear(2000f, axis.TargetDistance, 0.01f, "objective distance");

        axis = ArmyEvidenceBuilder.EvidenceFrontAxis(blind.FrontAxis, TacticalFrontAxis.None,
            hasOpenObjective: false, objectiveX: 0f, objectiveZ: 0f, ownSample, Array.Empty<FrontUnitSample>());
        AssertNear(blind.FrontAxis.HeadingDegrees, axis.HeadingDegrees, 0.001f, "no open objective: the snapshot axis");

        AssertFalse(ArmyEvidenceBuilder.EvidenceFrontAxis(TacticalFrontAxis.None, TacticalFrontAxis.None,
            hasOpenObjective: true, objectiveX: 2000f, objectiveZ: -1000f, ownSample, visible).IsValid,
            "no own sample (invalid snapshot axis): no axis");
    }

    // What BattleSnapshotBuilder.RefitFrontAxis does after perception: fit the axis on the
    // snapshot's own sample and visible observations.
    private static void BattleSnapshotTestAxis(BattleSnapshot s)
    {
        s.SetFrontAxis(BattleSnapshotAssembler.FitFrontAxis(s, TacticalFrontAxis.None, 0));
    }
}
