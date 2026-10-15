using System;
using WhiskeyRealism.Tactical;

using static TestHarness;

// Battle-start stat fixes (BUG-TAC-018 entrenchment divisor, BUG-TAC-019 HQ
// horse/gun condition); plan 2026-10-15-wl-economy-bugs.
internal static class BattleStatFixTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("battle entrenchment average divides by its own side's count", EntrenchmentAverageUsesOwnCount),
            ("battle entrenchment eligibility mirrors vanilla's enemy predicate", EntrenchmentEligibilityMirrorsVanilla),
            ("battle hq condition takes the per-kind clamp, not the men clamp", HqConditionTakesPerKindClamp),
            ("battle hq condition keeps every enclosing group's clamp", HqConditionKeepsEnclosingClamps),
        });
    }

    private static void EntrenchmentAverageUsesOwnCount()
    {
        // Vanilla: enemyentrenchmentlevel /= max(1, num) with num the player's
        // group count (DC:87399); the enemy count num2 is right beside it (87397).
        AssertNear(0.5f, BattleStatFixes.SideAverage(sum: 1.5f, count: 3), 1e-6f, "three AI groups");
        AssertNear(0.4f, BattleStatFixes.SideAverage(sum: 0.4f, count: 0), 1e-6f, "count floored at 1 as vanilla");
    }

    private static void EntrenchmentEligibilityMirrorsVanilla()
    {
        // DC:87384-87389: on the field now, not the player's alliance, a battledata group.
        AssertTrue(BattleStatFixes.CountsForEnemyAverage(reinforcementDelay: 0f, isPlayerAlliance: false, isBattleDataUnit: true), "AI start group");
        AssertFalse(BattleStatFixes.CountsForEnemyAverage(reinforcementDelay: 2f, isPlayerAlliance: false, isBattleDataUnit: true), "reinforcement not yet arrived");
        AssertFalse(BattleStatFixes.CountsForEnemyAverage(reinforcementDelay: 0f, isPlayerAlliance: true, isBattleDataUnit: true), "player's side");
        AssertFalse(BattleStatFixes.CountsForEnemyAverage(reinforcementDelay: 0f, isPlayerAlliance: false, isBattleDataUnit: false), "sub-group without its own battledata");
    }

    private static void HqConditionTakesPerKindClamp()
    {
        // AssignConditionToSubordinates sets the HQ's horses and guns from array[0]
        // (men) at DC:85470-85471; its sub-units use array[1]/array[2] (85466-85467).
        AssertNear(0.9f, BattleStatFixes.HqCondition(before: 1f, enclosingClamp: 1f, ownClamp: 0.9f), 1e-6f, "own horse clamp");
        AssertNear(0.7f, BattleStatFixes.HqCondition(before: 0.7f, enclosingClamp: 1f, ownClamp: 0.9f), 1e-6f, "already lower stays");
    }

    private static void HqConditionKeepsEnclosingClamps()
    {
        AssertNear(0.6f, BattleStatFixes.HqCondition(before: 1f, enclosingClamp: 0.6f, ownClamp: 0.9f), 1e-6f, "a parent group's clamp still applies");
        AssertNear(1f, BattleStatFixes.HqCondition(before: 1f, enclosingClamp: float.PositiveInfinity, ownClamp: 1f), 1e-6f, "no enclosing group");
    }
}
