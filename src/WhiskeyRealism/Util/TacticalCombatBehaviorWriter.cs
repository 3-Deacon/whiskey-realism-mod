using System;
using UnityEngine;

namespace WhiskeyRealism.Util
{
    // Scope B alignment (2026-05-22). Canonical helper for issuing vanilla
    // ChangeCombatBehavior writes. Mirrors TacticalStanceWriter +
    // TacticalFormationWriter patterns for consistency.
    //
    // Vanilla combat-behavior values per Regiment.combatbehaviorordered
    // (decompile inspection of fire-discipline state machine):
    //   0 = aggressive (charge-eligible, high fire rate)
    //   1 = fire at will (default, balanced)
    //   2 = defensive (preferred for stand-and-hold)
    //   3 = hold fire (no fire until enemy in close range)
    //   ...
    //   7 = bombardment-conserve (artillery — reduces ammo expenditure)
    //   8 = cancel-bombardment (artillery — halts active bombardment)
    //   9 = full bombardment (artillery)
    //
    // Vanilla's ChangeStance internally sets a default combat behavior per
    // stance (e.g., stance=1/screen → behavior=2/defensive). Whiskey's
    // TacticalFireControlDoctrine may override for specific tactical reasons
    // (low ammo, exposed lateral, friendly in fire line). This helper
    // unifies those override writes.
    public static class TacticalCombatBehaviorWriter
    {
        public const int BehaviorAggressive = 0;
        public const int BehaviorFireAtWill = 1;
        public const int BehaviorDefensive = 2;
        public const int BehaviorHoldFire = 3;
        public const int BehaviorBombardmentConserve = 7;
        public const int BehaviorCancelBombardment = 8;
        public const int BehaviorFullBombardment = 9;

        // Sets combatbehaviorordered for the group/regiment. Returns true if
        // write fired (i.e. behavior changed).
        public static bool EnsureBehavior(
            BattleUnits bunits,
            Regiment unit,
            int targetBehavior,
            string contextTag = null)
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
                if (!string.IsNullOrEmpty(contextTag))
                {
                    OnceLog.Info(
                        "tactical-combatbehavior-write:" + contextTag + ":" + targetBehavior,
                        "[TacticalCombatBehaviorWrite] " + contextTag + " behavior="
                        + targetBehavior + " (was " + unit.combatbehaviorordered + ")");
                }
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
