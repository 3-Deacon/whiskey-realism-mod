using System;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Shared artillery micro writes for B7CheckAIBombardmentPatch and
    // BattleGroupStancePatch.ApplyArtilleryAudit (BR-H3). The pure
    // BattleWriterPolicy.PlanArtilleryMicroWrite decides; this class only
    // performs the vanilla call for the chosen write.
    //
    // Vanilla write surface per write (decompile build 14703111):
    //   Limber          -> BattleUnits.MountUnits(unit, 1): formation 3 +
    //                      mounted 1 (90872). SetFormationParam(0, ..., 1) is an
    //                      UNLIMBER for artillery (ChangeRegimentFormation
    //                      135440-135452) and is never used.
    //   UnlimberAndFace -> MountUnits(unit, 0) (90880) then RotateRegiment toward
    //                      the closest enemy in fire range, as
    //                      UnlimberArtilleryAIMicro does (3482-3495).
    //   Retreat         -> AIBattle.CheckArtyFallback shape (3499-3539):
    //                      RegimentSetPath to GetAngleTerrainPos(pos,
    //                      unitrange.retreatangle, artilleryfallbackenemyclosedist),
    //                      skipcover, then SetMovementMode(4); limbered first.
    //   Face            -> RotateRegiment(Tools.GetAngle(own, enemy) + 180)
    //                      (never transform.rotation).
    //   CeaseBombard    -> ChangeCombatBehavior(7) + bombardstarttime = 0 (3904).
    // No stance is ever written: batteries are attached sub-units, and
    // ChangeStance (90772) would set permanentlydetached, after which vanilla
    // artillery AI skips them for good (3789, 3834, 3890, 3928; flag saved 73608).
    // Every write is behind TacticalWriteGuard.CanWrite(unit).
    internal static class TacticalArtilleryApplier
    {
        // Returns true when a vanilla write was issued.
        internal static bool ApplyMicroDecision(
            BattleUnits bunits,
            Regiment unit,
            int currentCombatBehaviorOrdered,
            TacticalArtilleryMicroDecision decision,
            string contextLogPrefix)
        {
            try
            {
                if (bunits == null || (UnityEngine.Object)unit == null) return false;

                GameObject enemyInFireRange = null;
                if (decision.Action == TacticalArtilleryMicroAction.Unlimber)
                    enemyInFireRange = unit.GetClosestEnemyUnit(unit.firerange);

                ArtilleryMicroWrite write = BattleWriterPolicy.PlanArtilleryMicroWrite(
                    decision.Action,
                    unit.mounted,
                    unit.regimentpaths,
                    unit.movementmode,
                    currentCombatBehaviorOrdered,
                    (UnityEngine.Object)enemyInFireRange != null);
                if (write == ArtilleryMicroWrite.None) return false;
                if (!TacticalWriteGuard.CanWrite(unit)) return false;

                switch (write)
                {
                    case ArtilleryMicroWrite.Limber:
                        bunits.MountUnits(unit, 1);
                        OnceLog.Info("artillery-applier-limber",
                            "[TacticalArtilleryApplier] artillery limber applied via MountUnits (formation 3).");
                        return true;
                    case ArtilleryMicroWrite.UnlimberAndFace:
                        bunits.MountUnits(unit, 0);
                        FaceToward(unit, enemyInFireRange);
                        OnceLog.Info("artillery-applier-unlimber",
                            "[TacticalArtilleryApplier] artillery unlimber applied + rotated to closest enemy in fire range.");
                        return true;
                    case ArtilleryMicroWrite.Retreat:
                        return ApplyArtilleryRetreat(bunits, unit);
                    case ArtilleryMicroWrite.Face:
                        Regiment visibleEnemy = TacticalFogOfWarContact.ClosestVisibleEnemy(unit);
                        return (UnityEngine.Object)visibleEnemy != null && FaceToward(unit, ((Component)visibleEnemy).gameObject);
                    case ArtilleryMicroWrite.CeaseBombard:
                        bunits.ChangeCombatBehavior(((Component)unit).gameObject, 7);
                        unit.bombardstarttime = 0f;
                        OnceLog.Info("artillery-applier-conserve",
                            "[TacticalArtilleryApplier] artillery conserve-ammo applied (cancelled bombardment).");
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("artillery-applier-error",
                    "[TacticalArtilleryApplier] artillery micro decision apply failed (" + contextLogPrefix + "): "
                    + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static bool ApplyArtilleryRetreat(BattleUnits bunits, Regiment unit)
        {
            if (unit.unitrange == null) return false;

            float distance = GamePrefs.artilleryfallbackenemyclosedist > 0f
                ? GamePrefs.artilleryfallbackenemyclosedist
                : 180f;
            float retreatAngle = unit.unitrange.retreatangle;
            Vector3 own = ((Component)unit).transform.position;
            if (!TacticalTerrainProbe.TryVanillaAnglePoint(own, retreatAngle, distance, out Vector3 target)) return false;

            if (unit.mounted != 1) bunits.MountUnits(unit, 1);
            unit.RegimentSetPath(target, newpath: true, skipcover: true, retreatAngle + 180f, modifylastwaypoint: false, 2, -1f, null, autoadvancefallback: true, uimovement: false);
            unit.SetMovementMode(4);
            OnceLog.Info("artillery-applier-retreat",
                "[TacticalArtilleryApplier] artillery retreat applied (limber + CheckArtyFallback-shaped path "
                + distance.ToString("0") + "m along retreat angle).");
            return true;
        }

        private static bool FaceToward(Regiment unit, GameObject enemy)
        {
            if ((UnityEngine.Object)enemy == null) return false;
            float rotation = Tools.GetAngle(((Component)unit).transform.position, enemy.transform.position) + 180f;
            if (float.IsNaN(rotation) || float.IsInfinity(rotation)) return false;
            unit.RotateRegiment(rotation);
            return true;
        }

        // Resolves the artillery's limber state from vanilla's `mounted` field.
        // Used by both B7 and the all-stance patch.
        internal static TacticalArtilleryLimberState LimberStateFor(Regiment unit)
        {
            try
            {
                if ((UnityEngine.Object)unit == null) return TacticalArtilleryLimberState.Unknown;
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
