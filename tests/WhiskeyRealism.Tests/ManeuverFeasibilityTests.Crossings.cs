using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;
using static BattleMovementHonestyTests;

// BC14 task 5: the crossing watch (spec § 13.1 S1 XING-*). Map: BandGrid's 400-unit square with a river band on x in [190, 210) and
// crossings listed on the band; an enemy regiment stands east of the crossing at (200, -200) and faces west (heading 270) when it
// covers it. Bearings are the world's: 0 is +z, 90 is +x.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] CrossingTests() => new (string, Action)[]
    {
        ("bc14 XING-HELD an enemy regiment within 1.5 x fire range facing the crossing holds it; at 50 degrees it does not", XingHeld),
        ("bc14 XING-WATCHED an infantry regiment within its reach, not facing, watches; beyond it does not; a battery never holds", XingWatched),
        ("bc14 XING-CLEAR a probe within 0.25 h makes a crossing clear; a stale probe does not", XingClear),
        ("bc14 XING-REMEMBERED a remembered regiment at 0.4 confidence neither holds nor watches", XingRemembered),
        ("bc14 XING-SHARED-RULE BuildPoints and the crossing watch agree on rule (a) for 50 random fixtures", XingSharedRule),
        ("bc14 XING-PROBE-CAP 40 crossings are probed 16 a tick, oldest first", XingProbeCap),
    };

    static readonly float[] ShippedReach = ReachTable(200f, 200f, 400f);

    // Reach by unit type: infantry, cavalry, artillery, then the rest at the infantry reach.
    static float[] ReachTable(float infantry, float cavalry, float artillery)
    {
        var reach = new float[14];
        for (int i = 0; i < reach.Length; i++) reach[i] = infantry;
        reach[TacticalUnitType.Cavalry] = cavalry;
        reach[TacticalUnitType.Artillery] = artillery;
        return reach;
    }

    sealed class FnGroundProbe : IPerceivedGround
    {
        public Func<float, float, bool> Fn = (x, z) => false;
        public readonly List<(float x, float z)> Calls = new List<(float, float)>();
        public bool IsPerceived(float x, float z) { Calls.Add((x, z)); return Fn(x, z); }
    }

    static EnemyObservation Enemy(float x, float z, float heading, float fireRange, int unitTyp = TacticalUnitType.Infantry,
        float strength = 500f, ObservationState state = ObservationState.Visible, float confidence = 1f, bool reinforcement = false)
        => new EnemyObservation
        {
            InstanceId = 5000 + (int)(x * 7 + z), UnitTyp = unitTyp, X = x, Z = z, Heading = heading, FireRange = fireRange, Strength = strength,
            State = state, Confidence01 = confidence, IsReinforcement = reinforcement, Morale = 0.8f,
        };

    static BattleSnapshot SnapshotOf(double hours, params EnemyObservation[] observations)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(7, 0, 0, 1, hours);
        s.IndexCommandTree(null);
        for (int i = 0; i < observations.Length; i++)
        {
            EnemyObservation o = observations[i];
            s.AddObservation(in o);
        }
        return s;
    }

    // A river band with one listed crossing at its centre (200, -200), regions built.
    static RiverRegions OneCrossing(out BattleTerrainGrid grid)
    {
        const int size = 400;
        grid = BattleMovementHonestyTests.GridOf(size, size, (x, zi) => x >= 190 && x < 210 ? Ford : Grass, false, new[] { 200f }, new[] { -200f }, new[] { Ford });
        return BuildRegions(grid, size);
    }

    static CrossingState Watch(BattleSnapshot s, RiverRegions r, BattleTerrainGrid g, double now, Func<float, float, bool> perceived = null, float[] reach = null)
    {
        var watch = new CrossingWatch();
        watch.Classify(s, r, g, new FnGroundProbe { Fn = perceived ?? ((x, z) => false) }, reach ?? ShippedReach, now);
        return watch.StateOf(0);
    }

    static void XingHeld()
    {
        RiverRegions r = OneCrossing(out BattleTerrainGrid g);
        AssertEqual(1, r.LinkCount, "one link");
        // 60 units east of the crossing, facing west, fire range 100 (reach 150).
        AssertEqual(CrossingState.Held, Watch(SnapshotOf(10.0, Enemy(260f, -200f, 270f, 100f)), r, g, 10.0), "facing it within 1.5 x fire range");
        AssertEqual(CrossingState.Held, Watch(SnapshotOf(10.0, Enemy(260f, -200f, 315f, 100f)), r, g, 10.0), "exactly 45 degrees off still holds");
        AssertEqual(CrossingState.Watched, Watch(SnapshotOf(10.0, Enemy(260f, -200f, 320f, 100f)), r, g, 10.0), "50 degrees off: not held, but within its reach: watched");
        AssertEqual(CrossingState.Watched, Watch(SnapshotOf(10.0, Enemy(260f, -200f, 270f, 30f)), r, g, 10.0), "beyond 1.5 x a short fire range: watched, not held");
        AssertEqual(CrossingState.Unknown, Watch(SnapshotOf(10.0, Enemy(260f, -200f, 270f, 100f, reinforcement: true)), r, g, 10.0), "a reinforcement column neither holds nor watches");

        // The held strength: the perceived strength within 300 units, Visible in full and Remembered times its confidence.
        var watch = new CrossingWatch();
        BattleSnapshot s = SnapshotOf(10.0,
            Enemy(260f, -200f, 270f, 100f, strength: 800f),
            Enemy(300f, -150f, 270f, 100f, strength: 600f, state: ObservationState.Remembered, confidence: 0.6f),
            Enemy(200f, -20f, 270f, 100f, strength: 5000f));        // 180 units off in z, inside 300? 180 < 300: counts
        watch.Classify(s, r, g, new FnGroundProbe(), ShippedReach, 10.0);
        AssertEqual(CrossingState.Held, watch.StateOf(0), "held");
        AssertNear(800f + 600f * 0.6f + 5000f, watch.HeldStrength(0), 0.01f, "held strength within 300 units");
        BattleSnapshot far = SnapshotOf(10.0, Enemy(260f, -200f, 270f, 100f, strength: 800f), Enemy(200f, 130f, 270f, 100f, strength: 5000f));
        watch.Classify(far, r, g, new FnGroundProbe(), ShippedReach, 10.0);
        AssertNear(800f, watch.HeldStrength(0), 0.01f, "a regiment 330 units off adds nothing");
        // A map that is not ready answers Unknown for everything.
        watch.Classify(s, new RiverRegions(), g, new FnGroundProbe(), ShippedReach, 10.0);
        AssertEqual(0, watch.LinkCount, "no regions, no links");
        AssertEqual(CrossingState.Unknown, watch.StateOf(0), "unknown");
    }

    static void XingWatched()
    {
        RiverRegions r = OneCrossing(out BattleTerrainGrid g);
        // An infantry regiment 180 units north of the crossing facing north, away from it: shipped infantry reach is 200.
        AssertEqual(CrossingState.Watched, Watch(SnapshotOf(10.0, Enemy(200f, -20f, 0f, 100f)), r, g, 10.0), "180 units, not facing: watched");
        AssertEqual(CrossingState.Unknown, Watch(SnapshotOf(10.0, Enemy(200f, 50f, 0f, 100f)), r, g, 10.0), "250 units: not watched (reach 200)");
        AssertEqual(CrossingState.Watched, Watch(SnapshotOf(10.0, Enemy(200f, -20f, 180f, 100f)), r, g, 10.0), "180 units, facing it, but beyond 1.5 x 100: watched, not held");
        // A battery 350 units off, facing the crossing, with a long range: artillery is never a holder, but it watches within its reach.
        EnemyObservation battery = Enemy(200f, 150f, 180f, 500f, unitTyp: TacticalUnitType.Artillery);
        AssertEqual(CrossingState.Watched, Watch(SnapshotOf(10.0, battery), r, g, 10.0), "a battery within its reach watches and never holds");
        AssertEqual(CrossingState.Unknown, Watch(SnapshotOf(10.0, battery), r, g, 10.0, reach: ReachTable(200f, 200f, 300f)), "beyond a shorter artillery reach: not watched");
    }

    static void XingClear()
    {
        RiverRegions r = OneCrossing(out BattleTerrainGrid g);
        BattleSnapshot empty = SnapshotOf(10.0);
        var watch = new CrossingWatch();
        watch.Classify(empty, r, g, new FnGroundProbe { Fn = (x, z) => true }, ShippedReach, 10.0);
        AssertEqual(CrossingState.Clear, watch.StateOf(0), "a probe that sees the point makes it clear");
        AssertNear(10.0f, (float)watch.LastPerceivedHours(0), 1e-6f, "the probe is stamped with the doctrine hour");
        watch.Classify(empty, r, g, new FnGroundProbe(), ShippedReach, 10.1);
        AssertEqual(CrossingState.Clear, watch.StateOf(0), "probed 0.1 h ago: still clear");
        watch.Classify(empty, r, g, new FnGroundProbe(), ShippedReach, 10.25);
        AssertEqual(CrossingState.Clear, watch.StateOf(0), "probed exactly 0.25 h ago: still clear");
        watch.Classify(empty, r, g, new FnGroundProbe(), ShippedReach, 10.3);
        AssertEqual(CrossingState.Unknown, watch.StateOf(0), "probed 0.3 h ago: a stale probe is not clear");
        // A Held crossing is Held whatever the probe says; a perceived point does not hide a covering regiment.
        var holder = SnapshotOf(10.0, Enemy(260f, -200f, 270f, 100f));
        watch.Classify(holder, r, g, new FnGroundProbe { Fn = (x, z) => true }, ShippedReach, 10.0);
        AssertEqual(CrossingState.Held, watch.StateOf(0), "held beats clear");
        AssertEqual(1, watch.CountBy(CrossingState.Held), "CountBy");
        AssertEqual(0, watch.CountBy(CrossingState.Clear), "CountBy clear");
    }

    static void XingRemembered()
    {
        RiverRegions r = OneCrossing(out BattleTerrainGrid g);
        AssertEqual(CrossingState.Unknown, Watch(SnapshotOf(10.0, Enemy(260f, -200f, 270f, 100f, state: ObservationState.Remembered, confidence: 0.4f)), r, g, 10.0),
            "a remembered regiment at 0.4 confidence neither holds nor watches");
        AssertEqual(CrossingState.Held, Watch(SnapshotOf(10.0, Enemy(260f, -200f, 270f, 100f, state: ObservationState.Remembered, confidence: 0.5f)), r, g, 10.0),
            "control: at 0.5 it holds");
    }

    static void XingSharedRule()
    {
        const int size = 400;
        const int crossings = 12;
        var cx = new float[crossings];
        var cz = new float[crossings];
        var cid = new int[crossings];
        for (int i = 0; i < crossings; i++) { cx[i] = 200f; cz[i] = -380f + i * 30f; cid[i] = i % 2 == 0 ? Ford : Bridge; }
        BattleTerrainGrid g = BattleMovementHonestyTests.GridOf(size, size, (x, zi) => x >= 190 && x < 210 ? Ford : Grass, false, cx, cz, cid);
        RiverRegions r = BuildRegions(g, size);
        AssertEqual(crossings, r.LinkCount, "every listed crossing links the banks");
        uint state = 0xC0FFEE11u;
        float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state & 0xFFFFFFu) / (float)0x1000000; }
        int heldTotal = 0, notHeldTotal = 0;
        for (int fixture = 0; fixture < 50; fixture++)
        {
            int n = 1 + (int)(Next() * 6f);
            var obs = new EnemyObservation[n];
            for (int i = 0; i < n; i++)
            {
                obs[i] = Enemy(120f + Next() * 160f, -400f + Next() * 400f, Next() * 360f, 40f + Next() * 160f,
                    unitTyp: (int)(Next() * 4f), state: Next() < 0.7f ? ObservationState.Visible : ObservationState.Remembered,
                    confidence: Next(), reinforcement: Next() < 0.1f);
                obs[i].InstanceId = 6000 + fixture * 10 + i;
            }
            BattleSnapshot s = SnapshotOf(10.0, obs);
            var xs = new float[64];
            var zs = new float[64];
            int count = CoveredCrossings.BuildPoints(s, g, xs, zs, out int crossingPoints);
            var watch = new CrossingWatch();
            watch.Classify(s, r, g, new FnGroundProbe(), ShippedReach, 10.0);
            for (int link = 0; link < r.LinkCount; link++)
            {
                r.LinkCrossing(link, out float lx, out float lz, out _, out _);
                bool listed = false;
                for (int p = 0; p < crossingPoints; p++) if (xs[p] == lx && zs[p] == lz) listed = true;
                AssertEqual(listed, watch.StateOf(link) == CrossingState.Held, "fixture " + fixture + " link " + link + ": BuildPoints and the watch agree on rule (a)");
                if (listed) heldTotal++; else notHeldTotal++;
            }
        }
        AssertTrue(heldTotal > 20 && notHeldTotal > 20, "the fixtures exercise both outcomes (" + heldTotal + "/" + notHeldTotal + ")");
    }

    static void XingProbeCap()
    {
        const int size = 400;
        const int crossings = 40;
        var cx = new float[crossings];
        var cz = new float[crossings];
        var cid = new int[crossings];
        for (int i = 0; i < crossings; i++) { cx[i] = 200f; cz[i] = -390f + i * 9f; cid[i] = Ford; }
        BattleTerrainGrid g = BattleMovementHonestyTests.GridOf(size, size, (x, zi) => x >= 190 && x < 210 ? Ford : Grass, false, cx, cz, cid);
        RiverRegions r = BuildRegions(g, size);
        AssertEqual(crossings, r.LinkCount, "40 links");
        var watch = new CrossingWatch();
        BattleSnapshot empty = SnapshotOf(10.0);

        var first = new FnGroundProbe();
        watch.Classify(empty, r, g, first, ShippedReach, 10.0);
        AssertEqual(CrossingWatch.MaxCrossingProbesPerTick, first.Calls.Count, "16 probes in the first tick");
        AssertEqual(16, watch.ProbesLastTick, "ProbesLastTick");
        for (int i = 0; i < 16; i++) AssertNear(cz[i], first.Calls[i].z, 1e-3f, "the never-probed links go in index order: " + i);

        var second = new FnGroundProbe();
        watch.Classify(empty, r, g, second, ShippedReach, 10.05);
        AssertEqual(16, second.Calls.Count, "16 probes in the second tick");
        for (int i = 0; i < 16; i++) AssertNear(cz[16 + i], second.Calls[i].z, 1e-3f, "the second tick takes the next sixteen: " + i);

        var third = new FnGroundProbe();
        watch.Classify(empty, r, g, third, ShippedReach, 10.10);
        AssertEqual(16, third.Calls.Count, "16 probes in the third tick");
        for (int i = 0; i < 8; i++) AssertNear(cz[32 + i], third.Calls[i].z, 1e-3f, "the last never-probed eight first: " + i);
        for (int i = 8; i < 16; i++) AssertNear(cz[i - 8], third.Calls[i].z, 1e-3f, "then the oldest probed again: " + i);
    }
}
