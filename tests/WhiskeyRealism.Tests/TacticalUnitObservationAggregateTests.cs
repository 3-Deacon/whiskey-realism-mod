using System;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

internal static class TacticalUnitObservationAggregateTests
    {
        public static void Run()
        {
            LoadForTestPartitionsAlliedAndEnemyIndices();
            LoadForTestClearsPriorCapture();
            ClearForBattleEndResetsAlliance();
            EmptyLoadProducesEmptyViews();
        }

        private static void LogPass(string message)
        {
            Console.WriteLine("PASS " + message);
        }

        private static TacticalUnitObservation MakeUnit(int instanceId, int alliance, float worldX = 0f, float worldZ = 0f)
        {
            return new TacticalUnitObservation(
                instanceId: instanceId,
                unittyp: 0,
                alliance: alliance,
                isRouted: false,
                worldX: worldX,
                worldZ: worldZ,
                strength: 100f,
                groupOwnInRange: 100f,
                groupAiGroup: 100f,
                hasCurrentSetObjective: false,
                currentSetObjectiveId: 0,
                objectiveX: 0f,
                objectiveZ: 0f,
                objectiveType: TacticalObjectiveType.UnknownVanillaObjective,
                hasLastWaypoint: false,
                lastWaypointX: 0f,
                lastWaypointZ: 0f,
                visibleEnemyStrength: 0f,
                hasVisibleEnemy: false,
                fatigue01: 0.2f,
                ammo01: 0.9f,
                effectiveCommandLevel: 0);
        }

        private static void LoadForTestPartitionsAlliedAndEnemyIndices()
        {
            var agg = new TacticalUnitObservationAggregate();
            var units = new[]
            {
                MakeUnit(1, alliance: 0),
                MakeUnit(2, alliance: 1),
                MakeUnit(3, alliance: 0),
                MakeUnit(4, alliance: 1)
            };
            agg.LoadForTest(allianceId: 0, units: units);
            TestHarness.AssertEqual(4, agg.Count, "count");
            TestHarness.AssertEqual(0, agg.CapturedForAlliance, "capturedForAlliance");
            TestHarness.AssertEqual(2, agg.AlliedIndices.Count, "alliedCount");
            TestHarness.AssertEqual(2, agg.EnemyIndices.Count, "enemyCount");
            TestHarness.AssertEqual(1, agg.AllUnits[agg.AlliedIndices[0]].InstanceId, "alliedFirst");
            TestHarness.AssertEqual(2, agg.AllUnits[agg.EnemyIndices[0]].InstanceId, "enemyFirst");
            LogPass("tactical unit observation aggregate load partitions allied and enemy indices");
        }

        private static void LoadForTestClearsPriorCapture()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, new[] { MakeUnit(1, 0), MakeUnit(2, 1) });
            agg.LoadForTest(1, new[] { MakeUnit(3, 1) });
            TestHarness.AssertEqual(1, agg.Count, "count");
            TestHarness.AssertEqual(1, agg.CapturedForAlliance, "alliance");
            TestHarness.AssertEqual(1, agg.AlliedIndices.Count, "alliedCount");
            TestHarness.AssertEqual(0, agg.EnemyIndices.Count, "enemyCount");
            LogPass("tactical unit observation aggregate load clears prior capture");
        }

        private static void ClearForBattleEndResetsAlliance()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, new[] { MakeUnit(1, 0) });
            agg.ClearForBattleEnd();
            TestHarness.AssertEqual(0, agg.Count, "count");
            TestHarness.AssertEqual(-1, agg.CapturedForAlliance, "alliance");
            TestHarness.AssertEqual(0, agg.AlliedIndices.Count, "alliedCount");
            TestHarness.AssertEqual(0, agg.EnemyIndices.Count, "enemyCount");
            LogPass("tactical unit observation aggregate clear for battle end resets alliance");
        }

        private static void EmptyLoadProducesEmptyViews()
        {
            var agg = new TacticalUnitObservationAggregate();
            agg.LoadForTest(0, Array.Empty<TacticalUnitObservation>());
            TestHarness.AssertEqual(0, agg.Count, "count");
            TestHarness.AssertEqual(0, agg.AlliedIndices.Count, "alliedCount");
            TestHarness.AssertEqual(0, agg.EnemyIndices.Count, "enemyCount");
            LogPass("tactical unit observation aggregate empty load produces empty views");
        }
    }
