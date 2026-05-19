using System;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

internal static class ObjectiveRecordsFromAggregateTests
{
    public static void Run()
    {
        VisibleEnemyLineComputesWeightedCentroidOfEnemyPositions();
        VisibleEnemyLineReturnsFalseWhenNoOwnUnitHasVisibleEnemy();
        MovementAnchorComputesWeightedCentroidOfWaypoints();
        MovementAnchorReturnsFalseWhenAllOwnHaveNoWaypoint();
        ApproachAvenueObservationsExcludeRoutedEnemies();
    }

    private static void LogPass(string message)
    {
        Console.WriteLine("PASS " + message);
    }

    private static TacticalUnitObservation MakeUnit(
        int instanceId,
        int alliance,
        float worldX = 0f,
        float worldZ = 0f,
        float strength = 100f,
        bool hasVisibleEnemy = false,
        float visibleEnemyStrength = 0f,
        bool hasVisibleEnemyPosition = false,
        float visibleEnemyX = 0f,
        float visibleEnemyZ = 0f,
        bool hasLastWaypoint = false,
        float lastWaypointX = 0f,
        float lastWaypointZ = 0f,
        bool isRouted = false,
        bool permanentlyDetached = false)
    {
        return new TacticalUnitObservation(
            instanceId: instanceId,
            unittyp: 0,
            alliance: alliance,
            isRouted: isRouted,
            permanentlyDetached: permanentlyDetached,
            worldX: worldX,
            worldZ: worldZ,
            strength: strength,
            groupOwnInRange: strength,
            groupAiGroup: strength,
            hasCurrentSetObjective: false,
            objectiveName: string.Empty,
            objectiveX: 0f,
            objectiveZ: 0f,
            objectiveType: TacticalObjectiveType.UnknownVanillaObjective,
            hasLastWaypoint: hasLastWaypoint,
            lastWaypointX: lastWaypointX,
            lastWaypointZ: lastWaypointZ,
            visibleEnemyStrength: visibleEnemyStrength,
            hasVisibleEnemy: hasVisibleEnemy,
            hasVisibleEnemyPosition: hasVisibleEnemyPosition,
            visibleEnemyX: visibleEnemyX,
            visibleEnemyZ: visibleEnemyZ,
            fatigue01: 0.2f,
            ammo01: 0.9f,
            effectiveCommandLevel: 0);
    }

    private static void VisibleEnemyLineComputesWeightedCentroidOfEnemyPositions()
    {
        var agg = new TacticalUnitObservationAggregate();
        // Own unit A: visible enemy at (100, 0) with enemy strength 100 — contributes weight 100
        // Own unit B: visible enemy at (200, 0) with enemy strength 200 — contributes weight 200
        // Own unit C: visible enemy at (300, 0) with enemy strength 300 — contributes weight 300
        // Weighted centroid X: (100*100 + 200*200 + 300*300) / (100+200+300) = 140000/600 ≈ 233.333
        // friendlyStrength = sum of all own unit strengths = 100 + 100 + 100 = 300
        // enemyStrength = sum of per-unit visible enemy strengths = 100 + 200 + 300 = 600
        agg.LoadForTest(allianceId: 0, units: new[]
        {
            MakeUnit(1, 0, strength: 100f, hasVisibleEnemy: true, visibleEnemyStrength: 100f,
                hasVisibleEnemyPosition: true, visibleEnemyX: 100f, visibleEnemyZ: 0f),
            MakeUnit(2, 0, strength: 100f, hasVisibleEnemy: true, visibleEnemyStrength: 200f,
                hasVisibleEnemyPosition: true, visibleEnemyX: 200f, visibleEnemyZ: 0f),
            MakeUnit(3, 0, strength: 100f, hasVisibleEnemy: true, visibleEnemyStrength: 300f,
                hasVisibleEnemyPosition: true, visibleEnemyX: 300f, visibleEnemyZ: 0f),
        });
        bool ok = TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(
            agg, 0, out var point, out float enemyStrength, out float friendlyStrength);
        TestHarness.AssertTrue(ok, "visible enemy line ok");
        TestHarness.AssertTrue(Math.Abs(point.X - 233.3333f) < 0.1f, "visible enemy line centroid X");
        TestHarness.AssertEqual(0f, point.Z, "visible enemy line centroid Z");
        TestHarness.AssertEqual(600f, enemyStrength, "visible enemy line enemy strength sum");
        TestHarness.AssertEqual(300f, friendlyStrength, "visible enemy line friendly strength sum");
        LogPass("visible enemy line computes weighted centroid of enemy positions");
    }

    private static void VisibleEnemyLineReturnsFalseWhenNoOwnUnitHasVisibleEnemy()
    {
        var agg = new TacticalUnitObservationAggregate();
        agg.LoadForTest(0, new[]
        {
            MakeUnit(1, 0, hasVisibleEnemy: false),
            MakeUnit(2, 0, hasVisibleEnemy: false),
            MakeUnit(3, 1, worldX: 50f, worldZ: 50f, strength: 100f),
        });
        bool ok = TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(
            agg, 0, out _, out _, out _);
        TestHarness.AssertFalse(ok, "visible enemy line returns false when no own unit has visible enemy");
        LogPass("visible enemy line returns false when no own unit has visible enemy");
    }

    private static void MovementAnchorComputesWeightedCentroidOfWaypoints()
    {
        var agg = new TacticalUnitObservationAggregate();
        // Own unit A: strength 100, waypoint at (50, 50) — weight 100
        // Own unit B: strength 200, waypoint at (100, 100) — weight 200
        // Weighted centroid: (50*100 + 100*200) / (100+200) = 25000/300 ≈ 83.333
        // friendlyStrength = 100 + 200 = 300
        agg.LoadForTest(0, new[]
        {
            MakeUnit(1, 0, strength: 100f, hasLastWaypoint: true, lastWaypointX: 50f, lastWaypointZ: 50f),
            MakeUnit(2, 0, strength: 200f, hasLastWaypoint: true, lastWaypointX: 100f, lastWaypointZ: 100f),
        });
        bool ok = TacticalVisionRuntimeAdapter.TryMovementAnchorLineFromAggregate_ForTest(
            agg, 0, out var point, out float friendlyStrength);
        TestHarness.AssertTrue(ok, "movement anchor ok");
        TestHarness.AssertTrue(Math.Abs(point.X - 83.3333f) < 0.1f, "movement anchor centroid X");
        TestHarness.AssertTrue(Math.Abs(point.Z - 83.3333f) < 0.1f, "movement anchor centroid Z");
        TestHarness.AssertEqual(300f, friendlyStrength, "movement anchor friendly strength sum");
        LogPass("movement anchor computes weighted centroid of waypoints");
    }

    private static void MovementAnchorReturnsFalseWhenAllOwnHaveNoWaypoint()
    {
        var agg = new TacticalUnitObservationAggregate();
        agg.LoadForTest(0, new[]
        {
            MakeUnit(1, 0),
            MakeUnit(2, 0),
            MakeUnit(3, 1, worldX: 50f, worldZ: 50f),
        });
        bool ok = TacticalVisionRuntimeAdapter.TryMovementAnchorLineFromAggregate_ForTest(
            agg, 0, out _, out _);
        TestHarness.AssertFalse(ok, "movement anchor returns false when all own have no waypoint");
        LogPass("movement anchor returns false when all own have no waypoint");
    }

    private static void ApproachAvenueObservationsExcludeRoutedEnemies()
    {
        var agg = new TacticalUnitObservationAggregate();
        // Enemy unit 1: non-routed — should produce an observation
        // Enemy unit 2: routed — should be excluded
        // Own unit: should not produce enemy-side observations
        agg.LoadForTest(0, new[]
        {
            MakeUnit(1, 1, worldX: 100f, worldZ: 100f, strength: 100f, isRouted: false),
            MakeUnit(2, 1, worldX: 200f, worldZ: 200f, strength: 200f, isRouted: true),
            MakeUnit(3, 0, worldX: 50f,  worldZ: 50f,  strength: 50f),
        });
        var observations = TacticalVisionRuntimeAdapter.BuildApproachAvenueObservationsFromAggregate_ForTest(agg, 0);
        TestHarness.AssertEqual(1, observations.Length, "approach avenue observations exclude routed enemies count");
        LogPass("approach avenue observations exclude routed enemies");
    }
}
