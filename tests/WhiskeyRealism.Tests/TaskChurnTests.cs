using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-18 (main a6b49fcf, DLL 11c2eae4): the Union's divisions swapped FixEnemy, ReserveWait, AttackObjective, FallBackToLine and FormUp every
// 30 to 60 s of real time, in lockstep with the operation's phase (Forming, Committed, SoftAbort, Scouting), and the 4th Brigade carried a path 1.1 km to the
// vanilla objective with no enemy in sight. The evidence the operation is decided on was a one-tick sample of what is visible.
internal static class TaskChurnTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("task churn: the visible enemy line counts an enemy once however many own units see it", EnemyLineCountsAnEnemyOnce),
        });
    }

    private static void EnemyLineCountsAnEnemyOnce()
    {
        // Three own units, each with the same enemy regiment (300 men) as its closest visible enemy: the old sum was 900.
        var snapshot = BattlePerceptionTests.ConsumerSnapshot(10.0,
            new[] { BattlePerceptionTests.OwnUnit(1, 100f, -50f, aiGroupStrength: 100), BattlePerceptionTests.OwnUnit(2, 110f, -50f, aiGroupStrength: 100),
                BattlePerceptionTests.OwnUnit(3, 120f, -50f, aiGroupStrength: 100) },
            BattlePerceptionTests.Seen(11, 110f, 0f, 300f));
        bool ok = TacticalVisionRuntimeAdapter.TryVisibleEnemyLineFromAggregate_ForTest(
            snapshot, 0, out TacticalMapPoint point, out float enemyStrength, out _);
        AssertTrue(ok, "the line is seen");
        AssertEqual(300f, enemyStrength, "one enemy regiment of 300 is 300, not once per own unit that sees it");
        AssertNear(110f, point.X, 0.01f, "and it stands where the enemy stands");
    }
}
