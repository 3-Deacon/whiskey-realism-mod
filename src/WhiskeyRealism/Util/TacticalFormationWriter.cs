using System;
using UnityEngine;

namespace WhiskeyRealism.Util
{
    // Scope B alignment (2026-05-22). Canonical helper for issuing vanilla
    // SetGroupFormation writes. Mirrors TacticalStanceWriter's pattern for
    // consistency across the codebase.
    //
    // Vanilla SetGroupFormation has 14 parameters; this helper exposes the
    // 3 most-used variants:
    //   EnsureFormation(group, formation)
    //     basic — set formation if different, no rotation/target override
    //   EnsureFormationAndFacing(group, formation, manualFinalRotation)
    //     formation + facing rotation
    //   EnsureFormationFlankAware(group, formation, refuseFlank)
    //     formation + flank-refuse parameter (for guard-flank tasks)
    //
    // All variants:
    //   - Skip if formation already correct (avoids redundant W&L queue entries)
    //   - Single point for telemetry
    //   - Fail-open on exception
    public static class TacticalFormationWriter
    {
        // Vanilla formation values per Regiment.formationordered:
        //   0 = line
        //   1 = column
        //   2 = square
        //   3 = skirmish (mass detach)
        //   4 = wedge (cavalry)
        public const int FormationLine = 0;
        public const int FormationColumn = 1;
        public const int FormationSquare = 2;
        public const int FormationSkirmish = 3;
        public const int FormationWedge = 4;

        // Basic formation write. Returns true if write fired.
        public static bool EnsureFormation(
            BattleUnits bunits,
            Regiment group,
            int targetFormation,
            string contextTag = null)
        {
            try
            {
                if (bunits == null || group == null) return false;
                if (targetFormation < 0 || targetFormation > 4) return false;
                if (group.formationordered == targetFormation) return false;
                bunits.SetGroupFormation(
                    group,
                    newformation: targetFormation,
                    manualfinalrotation: -1f,
                    targetpos: default(Vector3),
                    immediateplacement: false,
                    newpath: true,
                    modifylastwaypoint: false,
                    newstate: 2,
                    refuseflank: -1);
                if (!string.IsNullOrEmpty(contextTag))
                {
                    OnceLog.Info(
                        "tactical-formation-write:" + contextTag + ":" + targetFormation,
                        "[TacticalFormationWrite] " + contextTag + " formation="
                        + targetFormation + " (was " + group.formationordered + ")");
                }
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-formation-write:exception",
                    "[TacticalFormationWrite] EnsureFormation threw: "
                    + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        // Formation + facing rotation. Common for "wheel to face X" decisions.
        public static bool EnsureFormationAndFacing(
            BattleUnits bunits,
            Regiment group,
            int targetFormation,
            float manualFinalRotationDegrees,
            string contextTag = null)
        {
            try
            {
                if (bunits == null || group == null) return false;
                if (targetFormation < 0 || targetFormation > 4) return false;
                // Don't skip if only the facing changed but formation matches —
                // facing-only writes are still meaningful for re-orientation.
                bunits.SetGroupFormation(
                    group,
                    newformation: targetFormation,
                    manualfinalrotation: manualFinalRotationDegrees,
                    targetpos: default(Vector3),
                    immediateplacement: false,
                    newpath: true,
                    modifylastwaypoint: false,
                    newstate: 2,
                    refuseflank: -1);
                if (!string.IsNullOrEmpty(contextTag))
                {
                    OnceLog.Info(
                        "tactical-formation-write:" + contextTag + ":" + targetFormation + ":" + manualFinalRotationDegrees.ToString("0"),
                        "[TacticalFormationWrite] " + contextTag + " formation="
                        + targetFormation + " facing=" + manualFinalRotationDegrees.ToString("0"));
                }
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-formation-write:exception",
                    "[TacticalFormationWrite] EnsureFormationAndFacing threw: "
                    + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        // Formation + flank-refuse parameter. Used by guard-flank tasks.
        public static bool EnsureFormationFlankAware(
            BattleUnits bunits,
            Regiment group,
            int targetFormation,
            int refuseFlank,
            string contextTag = null)
        {
            try
            {
                if (bunits == null || group == null) return false;
                if (targetFormation < 0 || targetFormation > 4) return false;
                // We can't cheaply check the existing refuseflank value (vanilla
                // doesn't expose a stable accessor across game updates) — write
                // unconditionally when formation OR caller wants flank update.
                // The refuseFlank parameter difference is rare enough that the
                // small over-write rate is acceptable.
                if (group.formationordered == targetFormation && refuseFlank < 0) return false;
                bunits.SetGroupFormation(
                    group,
                    newformation: targetFormation,
                    manualfinalrotation: -1f,
                    targetpos: default(Vector3),
                    immediateplacement: false,
                    newpath: true,
                    modifylastwaypoint: false,
                    newstate: 2,
                    refuseflank: refuseFlank);
                if (!string.IsNullOrEmpty(contextTag))
                {
                    OnceLog.Info(
                        "tactical-formation-write:" + contextTag + ":refuse:" + refuseFlank,
                        "[TacticalFormationWrite] " + contextTag + " formation="
                        + targetFormation + " refuseFlank=" + refuseFlank);
                }
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-formation-write:exception",
                    "[TacticalFormationWrite] EnsureFormationFlankAware threw: "
                    + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }
    }
}
