using System;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC15 decision 2(a): BattlePerception.Update latches every observation change (an entry appears, turns Visible, turns Remembered) on the memory
// entry, and logs the position of an entry that leaves memory while it still blocked a group; the latch is cleared by the tier pass
// (ClearChangeLatch), never by a build, so a change seen between two arbiter ticks is not lost.
internal static partial class BattleLodTests
{
    private static (string, Action)[] LatchTests()
    {
        return new (string, Action)[]
        {
            ("bc15 LATCH a new sighting latches its observation", NewSightingLatches),
            ("bc15 LATCH a steady visible entry does not latch", SteadyVisibleDoesNotLatch),
            ("bc15 LATCH an entry turning Visible again latches", ReappearanceLatches),
            ("bc15 LATCH an entry turning Remembered latches", RememberedTransitionLatches),
            ("bc15 LATCH the latch survives later builds until the tier pass clears it", LatchSurvivesBuilds),
            ("bc15 LATCH a new entry in a freed slot is latched", NewEntryStartsLatchedWhateverTheSlotHeld),
            ("bc15 LATCH a visible enemy seen routing leaves a drop at its position", RoutedDropIsLogged),
            ("bc15 LATCH a remembered entry that expires leaves no drop", ExpiryLeavesNoDrop),
            ("bc15 LATCH the drop log overflows into a flag, not an allocation", DropLogOverflows),
            ("bc15 LATCH Publish copies the drops into the snapshot and a new build starts empty", PublishCopiesDrops),
            ("bc15 LATCH Update and ClearChangeLatch allocate nothing after warm-up", LatchAllocatesNothingAfterWarmup),
        };
    }

    private sealed class LatchProbe
    {
        internal PerceptionProbeResult Result = PerceptionProbeResult.Create(16);

        internal LatchProbe Clear() { Result.Count = 0; return this; }

        internal LatchProbe Add(int id, bool visible, float x, float z, bool routed = false)
        {
            int i = Result.Count++;
            Result.EnemyIds[i] = id;
            Result.Visible[i] = visible;
            Result.X[i] = x;
            Result.Z[i] = z;
            Result.Strength[i] = 500f;
            Result.UnitTyp[i] = 0;
            Result.Routed[i] = routed;
            Result.IsReinforcement[i] = false;
            return this;
        }
    }

    private sealed class LatchMemory
    {
        internal readonly EnemyObservation[] Items = new EnemyObservation[32];
        internal int Count;
        internal readonly ObservationDropLog Drops = new ObservationDropLog();

        internal void Tick(LatchProbe probe, double nowHours) => BattlePerception.Update(Items, ref Count, in probe.Result, nowHours, Drops);

        internal void Consume() => BattlePerception.ClearChangeLatch(Items, Count, Drops);

        internal int IndexOf(int id)
        {
            for (int i = 0; i < Count; i++) if (Items[i].InstanceId == id) return i;
            return -1;
        }
    }

    private const double LatchMinute = 1.0 / 60.0;

    private static void NewSightingLatches()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0);
        AssertTrue(m.Items[m.IndexOf(7)].ChangedSinceLastTier, "a new entry is latched");
    }

    private static void SteadyVisibleDoesNotLatch()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0);
        m.Consume();
        m.Tick(p.Clear().Add(7, true, 110f, 210f), 10.0 + LatchMinute);
        AssertFalse(m.Items[m.IndexOf(7)].ChangedSinceLastTier, "a visible entry that stays visible (it only moves) is no change");
    }

    private static void ReappearanceLatches()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0);
        m.Tick(p.Clear().Add(7, false, 100f, 200f), 10.0 + LatchMinute);
        m.Tick(p.Clear().Add(7, false, 100f, 200f), 10.0 + 2 * LatchMinute);
        AssertEqual(ObservationState.Remembered, m.Items[m.IndexOf(7)].State, "precondition: remembered");
        m.Consume();
        m.Tick(p.Clear().Add(7, true, 120f, 220f), 10.0 + 3 * LatchMinute);
        AssertEqual(ObservationState.Visible, m.Items[m.IndexOf(7)].State, "visible again");
        AssertTrue(m.Items[m.IndexOf(7)].ChangedSinceLastTier, "Remembered -> Visible is latched");
    }

    private static void RememberedTransitionLatches()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0);
        m.Consume();
        m.Tick(p.Clear().Add(7, false, 100f, 200f), 10.0 + LatchMinute);
        AssertFalse(m.Items[m.IndexOf(7)].ChangedSinceLastTier, "one hidden tick is still Visible: no change");
        m.Tick(p.Clear().Add(7, false, 100f, 200f), 10.0 + 2 * LatchMinute);
        AssertEqual(ObservationState.Remembered, m.Items[m.IndexOf(7)].State, "precondition: remembered");
        AssertTrue(m.Items[m.IndexOf(7)].ChangedSinceLastTier, "Visible -> Remembered is latched");
        m.Consume();
        m.Tick(p.Clear().Add(7, false, 100f, 200f), 10.0 + 3 * LatchMinute);
        AssertFalse(m.Items[m.IndexOf(7)].ChangedSinceLastTier, "a remembered entry that only decays is no change");
    }

    // The mutation this pins: clearing the latch at the start of every Update (per build) instead of in ClearChangeLatch (per tier pass).
    private static void LatchSurvivesBuilds()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0);
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0 + LatchMinute);   // a second build before any arbiter tick (deployment, a gated-off build)
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0 + 2 * LatchMinute);
        AssertTrue(m.Items[m.IndexOf(7)].ChangedSinceLastTier, "the sighting of build 1 is still latched at build 3");
        m.Consume();
        AssertFalse(m.Items[m.IndexOf(7)].ChangedSinceLastTier, "the tier pass cleared it");
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0 + 3 * LatchMinute);
        AssertFalse(m.Items[m.IndexOf(7)].ChangedSinceLastTier, "and a steady entry does not set it again");
    }

    private static void NewEntryStartsLatchedWhateverTheSlotHeld()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        m.Tick(p.Clear().Add(7, true, 100f, 200f).Add(8, true, 300f, 300f), 10.0);
        m.Consume();
        // Enemy 7 routs (leaves memory) and a new enemy takes the freed slot; its slot held a consumed (false) latch before.
        m.Tick(p.Clear().Add(7, true, 100f, 200f, routed: true).Add(9, true, 500f, 500f), 10.0 + LatchMinute);
        AssertEqual(-1, m.IndexOf(7), "routed enemy left");
        AssertTrue(m.Items[m.IndexOf(9)].ChangedSinceLastTier, "the new entry is latched");
    }

    private static void RoutedDropIsLogged()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0);
        m.Consume();
        m.Tick(p.Clear().Add(7, true, 150f, 250f, routed: true), 10.0 + LatchMinute);
        AssertEqual(1, m.Drops.Count, "one drop");
        AssertNear(100f, m.Drops.X[0], 0.001f, "at its last known x");
        AssertNear(200f, m.Drops.Z[0], 0.001f, "at its last known z");
        m.Consume();
        AssertEqual(0, m.Drops.Count, "the tier pass clears the log");
        AssertFalse(m.Drops.Overflow, "and its overflow flag");
    }

    private static void ExpiryLeavesNoDrop()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0);
        m.Consume();
        m.Tick(p.Clear().Add(7, false, 100f, 200f), 10.0 + LatchMinute);
        m.Tick(p.Clear().Add(7, false, 100f, 200f), 10.0 + 2 * LatchMinute);
        m.Tick(p.Clear(), 10.0 + 91 * LatchMinute);
        AssertEqual(-1, m.IndexOf(7), "expired after 90 minutes");
        AssertEqual(0, m.Drops.Count, "a memory below the blocking confidence blocked nothing, so its expiry is no change");
    }

    private static void DropLogOverflows()
    {
        var log = new ObservationDropLog();
        for (int i = 0; i < ObservationDropLog.MaxEntries; i++) log.Add(i, i);
        AssertEqual(ObservationDropLog.MaxEntries, log.Count, "full");
        AssertFalse(log.Overflow, "not yet overflowed");
        log.Add(1f, 1f);
        AssertTrue(log.Overflow, "one more sets the flag");
        AssertEqual(ObservationDropLog.MaxEntries, log.Count, "and stores nothing");
        log.Clear();
        AssertEqual(0, log.Count, "cleared");
        AssertFalse(log.Overflow, "overflow cleared");
    }

    private static void PublishCopiesDrops()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        m.Tick(p.Clear().Add(7, true, 100f, 200f), 10.0);
        m.Consume();
        m.Tick(p.Clear().Add(7, true, 100f, 200f, routed: true), 10.0 + LatchMinute);

        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 10.0);
        BattlePerception.Publish(s, PerceptionMode.RangeOnly, PerceptionFallbackReason.None, m.Items, m.Count, in p.Result, m.Drops);
        AssertEqual(1, s.DroppedObservationCount, "the snapshot carries the drop");
        AssertNear(100f, s.DroppedObservationX(0), 0.001f, "x");
        AssertNear(200f, s.DroppedObservationZ(0), 0.001f, "z");
        AssertFalse(s.DroppedObservationsOverflowed, "no overflow");

        s.BeginBuild(1, 0, 0, 2, 10.1);
        AssertEqual(0, s.DroppedObservationCount, "a new build starts empty");
        AssertFalse(s.DroppedObservationsOverflowed, "and clears the overflow flag");
    }

    private static void LatchAllocatesNothingAfterWarmup()
    {
        var m = new LatchMemory();
        var p = new LatchProbe();
        for (int round = 0; round < 3; round++)
        {
            m.Tick(p.Clear().Add(7, true, 100f, 200f).Add(8, true, 130f, 200f), 10.0 + round * LatchMinute);
            m.Consume();
        }
        long before = TestFixtures.BeginAllocationWindow();
        for (int round = 3; round < 30; round++)
        {
            m.Tick(p.Clear().Add(7, round % 3 != 0, 100f, 200f).Add(8, true, 130f, 200f), 10.0 + round * LatchMinute);
            m.Consume();
        }
        AssertEqual(0L, TestFixtures.AllocatedSince(before), "bytes allocated by 27 updates and consumes");
    }
}
