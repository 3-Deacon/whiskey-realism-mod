using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using static TestHarness;

// BC10 decision 2: covered crossings from perceived enemies only (vanilla
// UpdateBlockedCrossings 7917-7973 reads completeunitlist with no fog).
internal static partial class BattleMovementHonestyTests
{
    private static EnemyObservation Obs(int id, float x, float z, float heading, float fireRange, ObservationState state = ObservationState.Visible,
        float confidence = 1f, int unitTyp = TacticalUnitType.Infantry, float coverValue = 0f, int coverObject = 0, bool standing = true)
    {
        return new EnemyObservation
        {
            InstanceId = id, UnitTyp = unitTyp, X = x, Z = z, Strength = 500f, State = state, Confidence01 = confidence,
            Heading = heading, FireRange = fireRange, CoverValue = coverValue, CoverObject = coverObject, Standing = standing,
        };
    }

    private static BattleSnapshot SnapshotWith(params EnemyObservation[] observations)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        s.IndexCommandTree(null);
        foreach (EnemyObservation o in observations) s.AddObservation(in o);
        return s;
    }

    private static BattleTerrainGrid CrossingGrid(params (float x, float z, int id)[] crossings)
    {
        var xs = new float[crossings.Length];
        var zs = new float[crossings.Length];
        var ids = new int[crossings.Length];
        for (int i = 0; i < crossings.Length; i++) { xs[i] = crossings[i].x; zs[i] = crossings[i].z; ids[i] = crossings[i].id; }
        return GridOf(1000, 1000, (x, z) => Grass, crossingX: xs, crossingZ: zs, crossingId: ids);
    }

    // A crossing 40 m ahead (+z) of an enemy that faces +z (heading 0) and fires 30 m:
    // 1.5 x 30 = 45 >= 40 and the bearing to it is within 45 degrees of the facing.
    private static void CoveredCrossingFromVisibleFacingEnemyWithin1_5FireRange()
    {
        BattleTerrainGrid grid = CrossingGrid((100f, -100f, Bridge));
        var xs = new float[8];
        var zs = new float[8];
        BattleSnapshot s = SnapshotWith(Obs(1, 100f, -140f, 0f, 30f));
        AssertEqual(1, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "a visible enemy covers the bridge");
        AssertNear(100f, xs[0], 0.001f, "x");
        AssertNear(-100f, zs[0], 0.001f, "z");

        s = SnapshotWith(Obs(1, 100f, -150f, 0f, 30f));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "50 m is past 1.5 x fire range (45 m)");
        s = SnapshotWith(Obs(1, 100f, -140f, 180f, 30f));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "facing away from the crossing (180 degrees off)");
        s = SnapshotWith(Obs(1, 100f, -140f, 45f, 30f));
        AssertEqual(1, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "45 degrees off the facing still covers");
        s = SnapshotWith(Obs(1, 100f, -140f, 46f, 30f));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "46 degrees off does not");
        s = SnapshotWith(Obs(1, 100f, -140f, 315f, 30f));
        AssertEqual(1, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "the arc wraps around 360 (bearing 0, facing 315)");
    }

    private static void CoveredCrossingIgnoresHiddenEnemy()
    {
        BattleTerrainGrid grid = CrossingGrid((100f, -100f, Bridge));
        var xs = new float[8];
        var zs = new float[8];
        // No observation at all: whatever stands in the fog is not perceived (the snapshot's
        // EnemyRecords are true state and are never read here).
        BattleSnapshot s = SnapshotWith();
        s.AddEnemy(new UnitRecord { InstanceId = 1, UnitTyp = 0, X = 100f, Z = -140f, RotationY = 0f, FireRange = 30f, Flags = UnitFlags.Alive | UnitFlags.Active });
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "a hidden enemy covers nothing");
    }

    private static void CoveredCrossingIgnoresArtillery()
    {
        BattleTerrainGrid grid = CrossingGrid((100f, -100f, Bridge));
        var xs = new float[8];
        var zs = new float[8];
        BattleSnapshot s = SnapshotWith(Obs(1, 100f, -140f, 0f, 30f, unitTyp: TacticalUnitType.Artillery));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "artillery (unittyp 2) is skipped, 7948");
        s = SnapshotWith(Obs(1, 100f, -140f, 0f, 30f, unitTyp: 14));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "a group (unittyp > 13) is skipped");
        s = SnapshotWith(Obs(1, 100f, -140f, 0f, 30f, unitTyp: TacticalUnitType.Cavalry));
        AssertEqual(1, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "cavalry covers");
        var column = Obs(2, 100f, -140f, 0f, 30f);
        column.IsReinforcement = true;
        s = SnapshotWith(column);
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "an unarrived reinforcement column covers nothing");
    }

    private static void CoveredCrossingRememberedOnlyAboveHalfConfidence()
    {
        BattleTerrainGrid grid = CrossingGrid((100f, -100f, Bridge));
        var xs = new float[8];
        var zs = new float[8];
        BattleSnapshot s = SnapshotWith(Obs(1, 100f, -140f, 0f, 30f, ObservationState.Remembered, confidence: 0.5f));
        AssertEqual(1, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "seen within the last 20 battle-minutes (confidence 0.5)");
        s = SnapshotWith(Obs(1, 100f, -140f, 0f, 30f, ObservationState.Remembered, confidence: 0.49f));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "an older memory no longer covers");
    }

    // 7957-7966: a standing enemy (no paths, not routed) in real cover (covervalue > 0.15, not
    // forest) covers the point one fire range ahead of it.
    private static void CoverPointAheadOfStandingEnemyInCover()
    {
        BattleTerrainGrid grid = CrossingGrid();
        var xs = new float[8];
        var zs = new float[8];
        BattleSnapshot s = SnapshotWith(Obs(1, 100f, -300f, 90f, 100f, coverValue: 0.3f, coverObject: 1));
        AssertEqual(1, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "a fence-covered enemy facing east");
        AssertNear(200f, xs[0], 0.01f, "one fire range east (GetAngleTerrainPos: x + cos(angle - 90) * range)");
        AssertNear(-300f, zs[0], 0.01f, "same z");

        s = SnapshotWith(Obs(1, 100f, -300f, 0f, 100f, coverValue: 0.3f, coverObject: 1));
        AssertEqual(1, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "facing +z");
        AssertNear(100f, xs[0], 0.01f, "x unchanged");
        AssertNear(-200f, zs[0], 0.01f, "z - sin(-90) * range = z + range");

        // The point is clamped to the map like vanilla's (x in [0, size - 1], z in [-size + 1, 0]).
        s = SnapshotWith(Obs(1, 950f, -300f, 90f, 100f, coverValue: 0.3f, coverObject: 1));
        AssertEqual(1, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "near the east edge");
        AssertNear(999f, xs[0], 0.01f, "clamped to size - 1");

        // Two enemies on the same point list it once (vanilla's Contains).
        s = SnapshotWith(Obs(1, 100f, -300f, 90f, 100f, coverValue: 0.3f, coverObject: 1), Obs(2, 100f, -300f, 90f, 100f, coverValue: 0.3f, coverObject: 1));
        AssertEqual(1, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "the same point once");
    }

    private static void CoverPointNotForForestCoverOrMovingEnemy()
    {
        BattleTerrainGrid grid = CrossingGrid();
        var xs = new float[8];
        var zs = new float[8];
        BattleSnapshot s = SnapshotWith(Obs(1, 100f, -300f, 90f, 100f, coverValue: 0.3f, coverObject: 3));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "forest cover (3) is concealment, not a fire position");
        s = SnapshotWith(Obs(1, 100f, -300f, 90f, 100f, coverValue: 0.3f, coverObject: 1, standing: false));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "a moving enemy holds no point");
        s = SnapshotWith(Obs(1, 100f, -300f, 90f, 100f, coverValue: 0.15f, coverObject: 1));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "covervalue must exceed 0.15");
        s = SnapshotWith(Obs(1, 100f, -300f, 90f, 100f, coverValue: 0.3f, coverObject: 1, unitTyp: TacticalUnitType.Artillery));
        AssertEqual(0, CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), "artillery is skipped on both rules");
    }

    private static void CoveredCrossingsAllocateNothingAfterWarmup()
    {
        BattleTerrainGrid grid = CrossingGrid((100f, -100f, Bridge), (300f, -100f, Ford), (500f, -100f, Bridge));
        var obs = new EnemyObservation[300];
        for (int i = 0; i < obs.Length; i++)
            obs[i] = Obs(i + 1, 80f + i * 2f, -140f, 0f, 30f, coverValue: 0.3f, coverObject: 1);
        BattleSnapshot s = SnapshotWith(obs);
        var xs = new float[64];
        var zs = new float[64];
        int n = 0;
        AssertNoAllocation(() => n = CoveredCrossings.BuildPoints(s, grid, xs, zs, out _), 20, "no allocation after warm-up");
        AssertTrue(n > 0 && n <= 64, "points found, bounded by the output arrays");
    }

    // Visible rows carry their detail into memory (last seen while Visible); a row that is
    // hidden this tick keeps the last seen values.
    private static void PerceptionMemoryKeepsTheRowDetailWhileVisible()
    {
        var memory = new EnemyObservation[4];
        int count = 0;
        var probe = PerceptionProbeResult.Create(2);
        probe.Count = 1;
        probe.EnemyIds[0] = 7; probe.Visible[0] = true; probe.X[0] = 10f; probe.Z[0] = -20f; probe.Strength[0] = 400f; probe.UnitTyp[0] = 0;
        probe.Heading[0] = 135f; probe.FireRange[0] = 90f; probe.CoverValue[0] = 0.4f; probe.CoverObject[0] = 2; probe.Standing[0] = true;
        BattlePerception.Update(memory, ref count, in probe, 1.0);
        AssertEqual(1, count, "one entry");
        AssertNear(135f, memory[0].Heading, 0.001f, "heading");
        AssertNear(90f, memory[0].FireRange, 0.001f, "fire range");
        AssertNear(0.4f, memory[0].CoverValue, 0.001f, "cover value");
        AssertEqual(2, memory[0].CoverObject, "cover object");
        AssertTrue(memory[0].Standing, "standing");

        probe.Visible[0] = false;
        probe.Heading[0] = 0f; probe.FireRange[0] = 0f; probe.Standing[0] = false;
        BattlePerception.Update(memory, ref count, in probe, 1.001);
        AssertNear(135f, memory[0].Heading, 0.001f, "a hidden tick keeps the last seen heading");
        AssertNear(90f, memory[0].FireRange, 0.001f, "and fire range");
        AssertTrue(memory[0].Standing, "and the standing flag");
    }

    // Vanilla's AIBattle.blockedcrossings is built from every enemy regiment on the map (7917-7973);
    // nothing under Tactical/ reads it: the executor hands BlockedMovingOrder the perceived list.
    private static void VanillaBlockedCrossingsListIsNeverRead()
    {
        string root = TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical");
        var readers = new System.Collections.Generic.List<string>();
        foreach (string path in System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
            if (StripComments(System.IO.File.ReadAllText(path)).Contains("blockedcrossings")) readers.Add(System.IO.Path.GetFileName(path));
        AssertEqual("", string.Join(",", readers), "files that read vanilla's blockedcrossings");
        string exec = Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs");
        AssertTrue(exec.Contains("CoveredCrossings.BuildPointsGrowing("), "the executor builds the perceived list");
    }

    // 300 standing enemies in cover each add a distinct fire point: the output buffers grow instead of
    // truncating the list, up to the caller's ceiling, and say so when the ceiling cuts it.
    private static void CoveredPointBuffersGrowInsteadOfTruncating()
    {
        BattleTerrainGrid grid = CrossingGrid((100f, -100f, Bridge));
        var obs = new EnemyObservation[300];
        for (int i = 0; i < obs.Length; i++)
            obs[i] = Obs(i + 1, 80f + i * 2f, -140f, 0f, 30f, coverValue: 0.3f, coverObject: 1);
        BattleSnapshot s = SnapshotWith(obs);
        var xs = new float[64];
        var zs = new float[64];
        int n = CoveredCrossings.BuildPointsGrowing(s, grid, ref xs, ref zs, 1024, out _, out bool truncated);
        AssertTrue(n > 64, "more than the starting 64 points were kept: " + n);
        AssertFalse(truncated, "under the ceiling nothing is cut");
        AssertTrue(xs.Length >= n && zs.Length >= n, "the buffers grew");

        var small = new float[8];
        var smallZ = new float[8];
        n = CoveredCrossings.BuildPointsGrowing(s, grid, ref small, ref smallZ, 16, out _, out truncated);
        AssertEqual(16, n, "the ceiling bounds the list");
        AssertTrue(truncated, "and the cut is reported");
        string exec = Code("src/WhiskeyRealism/Tactical/Battle/OrderExecutorRuntime.cs");
        AssertTrue(exec.Contains("CoveredCrossings.BuildPointsGrowing(") && exec.Contains("covered-crossings-truncated"), "the runtime grows the buffer and logs a cut once");
    }
}
