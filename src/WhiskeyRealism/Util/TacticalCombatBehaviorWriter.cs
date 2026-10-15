using System;
using UnityEngine;

namespace WhiskeyRealism.Util
{
    // Canonical ChangeCombatBehavior write: TacticalWriteGuard, skip when
    // already ordered, bounded telemetry. Vanilla's own micro AI sets combat
    // behavior per stance cycle 6 (UpdateMicroAI 5617-5638) the same way.
    public static class TacticalCombatBehaviorWriter
    {
        // Sets combatbehaviorordered for the group/regiment. Returns true if
        // write fired (i.e. behavior changed).
        public static bool EnsureBehavior(
            BattleUnits bunits,
            Regiment unit,
            int targetBehavior)
        {
            try
            {
                if (bunits == null || unit == null) return false;
                if (!WhiskeyRealism.Tactical.TacticalWriteGuard.CanWrite(unit)) return false;
                if (targetBehavior < 0) return false;
                if (unit.combatbehaviorordered == targetBehavior) return false;
                var gameObject = ((Component)unit).gameObject;
                if (gameObject == null) return false;
                bunits.ChangeCombatBehavior(gameObject, targetBehavior);
                OnceLog.Info(
                    "tactical-combatbehavior-write:" + targetBehavior,
                    "[TacticalCombatBehaviorWrite] first write behavior=" + targetBehavior);
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-combatbehavior-write:exception",
                    "[TacticalCombatBehaviorWrite] EnsureBehavior threw: "
                    + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }
    }
}
