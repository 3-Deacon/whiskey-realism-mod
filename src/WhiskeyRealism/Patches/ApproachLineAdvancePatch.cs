using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // ApproachLineAdvancePatch (provisional #129, ordinal assigned at merge) - a skipping Prefix on Regiment.CheckFormationChange, approved by the
    // user 2026-10-17 ("Yes, allow it (Recommended)": a patch that skips the game's re-column step only for AI regiments Whiskey has ordered to advance or
    // hold in line near the enemy, never the player's units, never road marches far from the enemy, with its own on/off switch).
    //
    // Vanilla CheckFormationChange (127625-127682), run from the regular cycle (112277, while changetomarchcolumnactive) and on a manual stop (132370):
    //   - 127662-127668: a regiment with a path whose last corner is further than mindistanceformarchcolumn (60 m, unitprefs 881) and no enemy within
    //     distancetoenemyforinterruption (100 m, unitprefs 989) takes the march column (SetDistanceMarchColumn 127702);
    //   - 127669-127674: near the path's end it goes back to its prior formation;
    //   - 127677-127682: with an enemy inside the 100 m (movementmode 0 or 3) a column regiment deploys to line, facing lastwaypointrotation[0].
    // A brigade Whiskey advances or holds in line near the enemy was columned again a moment after each deployment and met the enemy in column.
    //
    // This Prefix returns false (vanilla's method does not run for this call) only when ApproachLine.Skips holds: the flag is on, Commander Mode is Active,
    // TacticalWriteGuard.CanWrite passes (an AI regiment, not player-owned, not feuding, no exposed player descendant), the regiment is infantry and is not in
    // the march column (a column regiment keeps vanilla's deploy, 127677, and Whiskey's own ColumnDeployment), and the approach table marks it: the
    // facing-pulse producer marks the regiments of a group whose recorded intent is a line advance or hold (not a road route or column wait), with the
    // group's closest perceived enemy inside ApproachLine.AssaultRangeMeters. In every other case the method runs unchanged; any failure runs it too.
    // The skip keeps the regiment's formation; facing comes from the facing pulse and the deployment, not from the walking direction. Reads the table only.
    [HarmonyPatch(typeof(Regiment), "CheckFormationChange")]
    internal static class ApproachLineAdvancePatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(Regiment __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.approach-line-advance", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.approach-line-advance");
                try
                {
                    // The cheap tests first: this runs for every regiment on every regular cycle.
                    if (Plugin.EnableApproachLineAdvance == null || !Plugin.EnableApproachLineAdvance.Value) return true;
                    if ((UnityEngine.Object)__instance == null || __instance.unittyp != TacticalUnitType.Infantry) return true;
                    int formation = __instance.formation;
                    if (formation == ColumnDeployment.RegimentMarchColumn) return true;
                    bool marked = ApproachLineTable.Shared.Contains(__instance.alliance, ((Component)__instance).gameObject.GetInstanceID(), TacticalDoctrineTime.NowBattleHours);
                    if (!marked) return true;
                    bool skip = ApproachLine.Skips(true, TacticalWriteGuard.ModeAllowsWrites(), TacticalWriteGuard.CanWrite(__instance), __instance.unittyp, formation, marked);
                    if (skip) OnceLog.Info("tactical-approach-line:first-skip", "[ApproachLine] first re-column skipped: an AI regiment advancing or holding in line inside the assault range");
                    return !skip;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                    return true;
                }
            }
        }

        private static void Warn(Exception ex)
        {
            OnceLog.Warning("tactical-approach-line:failed", "[ApproachLine] Prefix threw, vanilla runs: " + ex.GetType().Name + " " + ex.Message);
        }
    }
}
