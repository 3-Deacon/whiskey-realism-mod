using System;

namespace WhiskeyRealism.Tactical
{
    // Pure arithmetic for two battle-start stat corrections (plan
    // docs/superpowers/plans/2026-10-15-wl-economy-bugs.md). Not orders: they
    // restore the values vanilla's own formulas meant to produce.
    internal static class BattleStatFixes
    {
        // BUG-TAC-018. BattleUnits.CalculateAllUnitsOfAlliance averages the AI
        // side's entrenchment over the player's group count (DC:87399:
        // enemyentrenchmentlevel /= max(1, num)); readiness beside it uses the AI
        // count num2 (87397). Same floor as vanilla.
        internal static float SideAverage(float sum, int count)
        {
            return sum / Math.Max(1f, count);
        }

        // Vanilla's enemy-side predicate at DC:87384-87389 (the caller has
        // already required a Regiment with unittyp > 13 on the group object).
        internal static bool CountsForEnemyAverage(float reinforcementDelay, bool isPlayerAlliance, bool isBattleDataUnit)
        {
            return reinforcementDelay <= 0f && !isPlayerAlliance && isBattleDataUnit;
        }

        // BUG-TAC-019. AssignConditionToSubordinates clamps every active unit of a
        // group by that group's per-kind value (DC:85465-85467), then clamps the
        // group's HQ again with the men value for horses and guns
        // (DC:85470-85471). The intended HQ value: its own value before the call,
        // clamped by every group whose sub-unit pass reached it (enclosingClamp,
        // +inf when none) and by its own group's per-kind value.
        internal static float HqCondition(float before, float enclosingClamp, float ownClamp)
        {
            return Math.Min(before, Math.Min(enclosingClamp, ownClamp));
        }
    }
}
