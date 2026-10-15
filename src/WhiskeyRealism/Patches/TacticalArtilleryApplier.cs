using System;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Shared artillery write helpers (2026-05-21). Extracted into a separate
    // class so both `B7CheckAIBombardmentPatch` (vanilla stance 3/4 path)
    // and `BattleGroupStancePatch.ApplyArtilleryAudit` (all-stance path)
    // emit the same vanilla writes for each TacticalArtilleryMicroAction.
    //
    // Vanilla write surface per action (mirrors B7's existing implementation):
    //   Limber           → unit.ChangeRegimentFormation(SetFormationParam(0, 1, 1))
    //                       (sets mounted=1 = limbered)
    //   Unlimber         → unit.ChangeRegimentFormation(SetFormationParam(0, 1, 0))
    //                       + rotate toward closest enemy
    //   Retreat          → SetWaypoint backward along (own − enemy) vector
    //                       at GamePrefs.artilleryfallbackenemyclosedist meters
    //                       (default 180m fallback)
    //   WheelToBestThreat → rotate toward closest enemy in priority quadrant
    //   ConserveAmmo     → ChangeCombatBehavior to value 7
    //                       (cancels combatbehaviorordered==9 bombardment)
    //   Fire             → no-op (vanilla handles)
    //   Preserve         → no-op
    //
    // The vanilla `mounted` field on Regiment is the limber state:
    //   mounted == 0 → unlimbered (firing)
    //   mounted == 1 → limbered (mobile)
    //
    // Fail-safe: each apply path is wrapped in try/catch so a bad write
    // doesn't break the Postfix. OnceLog tracks per-action telemetry without
    // spamming.
    internal static class TacticalArtilleryApplier
    {
        // Returns true when the action resulted in a write attempt (telemetry
        // hook); false on no-op cases (Fire/Preserve) or already-correct state.
        internal static bool ApplyMicroDecision(
            BattleUnits bunits,
            Regiment unit,
            int currentCombatBehaviorOrdered,
            TacticalArtilleryMicroDecision decision,
            string contextLogPrefix)
        {
            try
            {
                if (bunits == null || unit == null) return false;
                switch (decision.Action)
                {
                    case TacticalArtilleryMicroAction.Limber:
                        if (unit.mounted != 1)
                        {
                            unit.ChangeRegimentFormation(GameVars.SetFormationParam(0, manualset: true, 1));
                            OnceLog.Info(contextLogPrefix + "-artillery-limber",
                                "[" + contextLogPrefix + "] artillery limber applied (unit was unlimbered).");
                            return true;
                        }
                        return false;
                    case TacticalArtilleryMicroAction.Unlimber:
                        if (unit.mounted != 0)
                        {
                            unit.ChangeRegimentFormation(GameVars.SetFormationParam(0, manualset: true, 0));
                            TryRotateTowardClosestEnemy(unit);
                            OnceLog.Info(contextLogPrefix + "-artillery-unlimber",
                                "[" + contextLogPrefix + "] artillery unlimber applied + rotated to closest enemy.");
                            return true;
                        }
                        return false;
                    case TacticalArtilleryMicroAction.Retreat:
                        return ApplyArtilleryRetreat(bunits, unit, contextLogPrefix);
                    case TacticalArtilleryMicroAction.WheelToBestThreat:
                        if (TryRotateTowardClosestEnemy(unit))
                        {
                            OnceLog.Info(contextLogPrefix + "-artillery-wheel",
                                "[" + contextLogPrefix + "] artillery wheel applied to closest enemy.");
                            return true;
                        }
                        return false;
                    case TacticalArtilleryMicroAction.ConserveAmmo:
                        if (currentCombatBehaviorOrdered == 9)
                        {
                            bunits.ChangeCombatBehavior(((Component)unit).gameObject, 7);
                            unit.bombardstarttime = 0f;
                            OnceLog.Info(contextLogPrefix + "-artillery-conserve",
                                "[" + contextLogPrefix + "] artillery conserve-ammo applied (cancelled bombardment).");
                            return true;
                        }
                        return false;
                    default:
                        return false;  // Fire / Preserve / no-op
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(contextLogPrefix + "-artillery-apply-error",
                    "[" + contextLogPrefix + "] artillery micro decision apply failed: "
                    + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        // Issues a fallback waypoint along the (own − enemy) vector, with
        // limber state forced to 1 (limber up before retreating) and movement
        // mode 4 (running / double-quick). Mirrors B7's ApplyArtilleryRetreat
        // exactly so behavior is consistent across patch paths.
        private static bool ApplyArtilleryRetreat(BattleUnits bunits, Regiment unit, string contextLogPrefix)
        {
            try
            {
                Regiment enemy = TacticalFogOfWarContact.ClosestVisibleEnemy(unit);
                if (enemy == null) return false;

                Vector3 own = ((Component)unit).transform.position;
                Vector3 enemyPos = ((Component)enemy).transform.position;
                Vector3 away = own - enemyPos;
                away.y = 0f;
                if (away.sqrMagnitude < 1f) return false;
                away.Normalize();

                float distance = GamePrefs.artilleryfallbackenemyclosedist > 0f
                    ? GamePrefs.artilleryfallbackenemyclosedist
                    : 180f;
                Vector3 target = own + away * distance;
                // Scope B refactor (2026-05-22): defensive stance BEFORE
                // limber+waypoint. Vanilla micro-AI treats retreating artillery
                // correctly only when ai_stanceordered = 2 (defend).
                WhiskeyRealism.Util.TacticalStanceWriter.EnsureStance(
                    bunits, unit, 2, contextTag: "artillery-applier-retreat:" + contextLogPrefix);
                unit.ChangeRegimentFormation(GameVars.SetFormationParam(0, manualset: true, 1));
                bunits.SetWaypoint(
                    unit,
                    target,
                    newpath: true,
                    doublequick: false,
                    manualfinalrotation: -1f,
                    modifylastwaypoint: false,
                    useorderdelay: true,
                    timetomove: -1f,
                    direction: -1,
                    showmovementoptions: false,
                    ignorebattlemonuments: false,
                    groupmoveonly: false,
                    ignoredisabledships: false,
                    checkforreadiness: true,
                    clearinterruptionpaths: true);
                unit.SetMovementMode(4);
                OnceLog.Info(contextLogPrefix + "-artillery-retreat",
                    "[" + contextLogPrefix + "] artillery retreat applied (limber + fallback waypoint "
                    + distance.ToString("0") + "m away from enemy).");
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(contextLogPrefix + "-artillery-retreat-error",
                    "[" + contextLogPrefix + "] artillery retreat apply failed: "
                    + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static bool TryRotateTowardClosestEnemy(Regiment unit)
        {
            try
            {
                Regiment enemy = TacticalFogOfWarContact.ClosestVisibleEnemy(unit);
                if (enemy == null) return false;
                Vector3 own = ((Component)unit).transform.position;
                Vector3 enemyPos = ((Component)enemy).transform.position;
                Vector3 toEnemy = enemyPos - own;
                toEnemy.y = 0f;
                if (toEnemy.sqrMagnitude < 1f) return false;
                float yawDegrees = Mathf.Atan2(toEnemy.x, toEnemy.z) * Mathf.Rad2Deg;
                ((Component)unit).transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
                return true;
            }
            catch { return false; }
        }

        // Resolves the artillery's limber state from vanilla's `mounted` field.
        // Used by both B7 and the all-stance patch.
        internal static TacticalArtilleryLimberState LimberStateFor(Regiment unit)
        {
            try
            {
                if (unit == null) return TacticalArtilleryLimberState.Unknown;
                return unit.mounted == 1
                    ? TacticalArtilleryLimberState.Limbered
                    : TacticalArtilleryLimberState.Unlimbered;
            }
            catch
            {
                return TacticalArtilleryLimberState.Unknown;
            }
        }
    }
}
