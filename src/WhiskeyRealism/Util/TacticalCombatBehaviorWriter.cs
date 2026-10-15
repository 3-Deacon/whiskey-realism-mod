using System;
using UnityEngine;

namespace WhiskeyRealism.Util
{
    // Canonical ChangeCombatBehavior write and the CombatBehavior channel
    // executor of the battle core: TacticalWriteGuard, skip when already
    // ordered, bounded telemetry. Vanilla's own micro AI sets combat behavior
    // per stance cycle 6 (UpdateMicroAI 5617-5638) the same way.
    //
    // Per-regiment writes (IntentFlags.PerRegiment, D10): cycle 6 re-orders a
    // group whose combatbehaviorordered != 2 back to its stance default, and
    // ChangeCombatBehavior (90944-90966) cascades SetCombatBehaviorOrdered to
    // every unit under the target. A per-regiment intent (charge 3, fire
    // control) therefore only ever targets a regiment (unittyp <= 13), never
    // a group.
    public static class TacticalCombatBehaviorWriter
    {
        // Sets combatbehaviorordered for the group/regiment. Returns true if
        // write fired (i.e. behavior changed).
        public static bool EnsureBehavior(
            BattleUnits bunits,
            Regiment unit,
            int targetBehavior,
            bool perRegiment = false)
        {
            try
            {
                if (bunits == null || unit == null) return false;
                if (perRegiment && unit.unittyp > WhiskeyRealism.Tactical.TacticalUnitType.MaxCombat)
                {
                    OnceLog.Info("tactical-combatbehavior-write:per-regiment-group",
                        "[TacticalCombatBehaviorWrite] refused a per-regiment behavior on a group (first occurrence)");
                    return false;
                }
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
