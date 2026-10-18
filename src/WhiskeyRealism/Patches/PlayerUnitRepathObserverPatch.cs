using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-024, record only: who re-paths or re-moves a W&L player-owned unit, and from which vanilla routine.
    //
    // Smoke 2026-10-18: a CSA player's detached skirmisher company (Kyle's_Detachment) walked backwards, toward his regiment, and ignored his orders.
    // Vanilla's unit-level skirmisher routines act on every company with no W&L test: CheckFallbackDueToEnemyClose (128924, falls back from an enemy
    // within 25 m, movementmode 2, a path along the enemy-to-parent line), CheckNewTargetsOfSkirmishers (128847, relinks to the parent), UpdateUnitLink
    // (124394, follows the link position) and CheckForReattachmentOrder (129520). They call Regiment.RegimentSetPath directly, never
    // BattleUnits.SetWaypoint, so TacticalObserverPatch.ObservePathShape (showmovementoptions only) cannot see them. This patch can.
    //
    //  * RegimentSetPath and SetMovementMode Postfixes write one TacticalPlayerUnitRepath row for a player-owned unit (dlcw_isundercommander, a
    //    battle unit, in a W&L career only: outside one the flag is set on the whole player side): kind=path or kind=mode, the routine on the
    //    stack, the target, the mode and path count before and after, the link, the stance, the distance to the closest enemy and to the parent.
    //  * A marker around each suspect routine (Prefix enters, Finalizer leaves; RepathRoutineMarker, one static, no StackTrace) names the caller. The
    //    player's own click is a marker around BattleUnits.SetWaypoint with showmovementoptions true (the only UI callers, 166134, 166165). A call
    //    with no marker reads "unattributed": the brigade-level AIBattle code, the player's own queued order (ProcessOrders then
    //    RebuildAllManualWaypoints, 125344, 125463: it runs after the click, not inside it) or a caller not named here.
    //  * A routine that changed unitlinkedto or reattachmentorder with no path set (CheckNewTargetsOfSkirmishers' relink) writes kind=routine.
    // Every Prefix and Postfix is non-skipping and writes nothing into the battle: the only state touched is this file's own static marker. Rows are
    // bounded by RepathRowBudget. Gated by Enable Tactical Observer (the rollback valve).
    internal static class PlayerUnitRepathObserverPatch
    {
        internal const string EventName = "TacticalPlayerUnitRepath";

        private static readonly RepathRowBudget Budget = new RepathRowBudget();

        internal struct RoutineState
        {
            public bool Entered;
            public RepathMarker Previous;
            public int LinkedId;
            public bool Reattach;
        }

        internal struct PathState
        {
            public bool Tracked;
            public int Paths;
            public int Mode;
        }

        static PlayerUnitRepathObserverPatch()
        {
            BattleScope.Register("player-unit-repath-budget", Budget.Reset);
        }

        // ---- the player's own order ---------------------------------------------------------------------------------------
        //
        // Every entry that fires for each regiment tests Enabled and Tracked before it opens a scope, as PerfHotPathTests pins for the W&L hooks: an AI
        // unit's call costs two field reads and no scope.

        [HarmonyPatch(typeof(BattleUnits), "SetWaypoint", new[]
        {
            typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool), typeof(float),
            typeof(bool), typeof(bool), typeof(float), typeof(int), typeof(bool),
            typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
        })]
        [HarmonyPrefix]
        internal static void SetWaypointPrefix(bool showmovementoptions, out RoutineState __state)
        {
            __state = default(RoutineState);
            if (!showmovementoptions || !Enabled()) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.set-waypoint.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.set-waypoint.prefix");
                __state.Previous = RepathRoutineMarker.Enter(RepathRoutine.PlayerOrder, RepathRoutineMarker.AnyUnit);
                __state.Entered = true;
            }
        }

        [HarmonyPatch(typeof(BattleUnits), "SetWaypoint", new[]
        {
            typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool), typeof(float),
            typeof(bool), typeof(bool), typeof(float), typeof(int), typeof(bool),
            typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
        })]
        [HarmonyFinalizer]
        internal static void SetWaypointFinalizer(RoutineState __state)
        {
            if (!__state.Entered) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.set-waypoint.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.set-waypoint.finalizer");
                RepathRoutineMarker.Leave(__state.Previous);
            }
        }

        // ---- the suspect vanilla routines ---------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Regiment), "CheckFallbackDueToEnemyClose")]
        [HarmonyPrefix]
        internal static void FallbackPrefix(Regiment __instance, out RoutineState __state)
        {
            __state = default(RoutineState);
            if (!Enabled() || !Tracked(__instance)) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.fallback.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.fallback.prefix");
                Enter(__instance, RepathRoutine.Fallback, ref __state);
            }
        }

        [HarmonyPatch(typeof(Regiment), "CheckFallbackDueToEnemyClose")]
        [HarmonyFinalizer]
        internal static void FallbackFinalizer(Regiment __instance, RoutineState __state)
        {
            if (!__state.Entered) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.fallback.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.fallback.finalizer");
                Leave(__instance, RepathRoutine.Fallback, __state);
            }
        }

        [HarmonyPatch(typeof(Regiment), "CheckNewTargetsOfSkirmishers")]
        [HarmonyPrefix]
        internal static void TargetsPrefix(Regiment __instance, out RoutineState __state)
        {
            __state = default(RoutineState);
            if (!Enabled() || !Tracked(__instance)) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.skirmisher-target.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.skirmisher-target.prefix");
                Enter(__instance, RepathRoutine.SkirmisherTarget, ref __state);
            }
        }

        [HarmonyPatch(typeof(Regiment), "CheckNewTargetsOfSkirmishers")]
        [HarmonyFinalizer]
        internal static void TargetsFinalizer(Regiment __instance, RoutineState __state)
        {
            if (!__state.Entered) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.skirmisher-target.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.skirmisher-target.finalizer");
                Leave(__instance, RepathRoutine.SkirmisherTarget, __state);
            }
        }

        [HarmonyPatch(typeof(Regiment), "UpdateUnitLink")]
        [HarmonyPrefix]
        internal static void LinkPrefix(Regiment __instance, out RoutineState __state)
        {
            __state = default(RoutineState);
            if (!Enabled() || !Tracked(__instance)) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.unit-link.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.unit-link.prefix");
                Enter(__instance, RepathRoutine.UnitLink, ref __state);
            }
        }

        [HarmonyPatch(typeof(Regiment), "UpdateUnitLink")]
        [HarmonyFinalizer]
        internal static void LinkFinalizer(Regiment __instance, RoutineState __state)
        {
            if (!__state.Entered) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.unit-link.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.unit-link.finalizer");
                Leave(__instance, RepathRoutine.UnitLink, __state);
            }
        }

        [HarmonyPatch(typeof(Regiment), "CheckForReattachmentOrder")]
        [HarmonyPrefix]
        internal static void ReattachPrefix(Regiment __instance, out RoutineState __state)
        {
            __state = default(RoutineState);
            if (!Enabled() || !Tracked(__instance)) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.reattach.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.reattach.prefix");
                Enter(__instance, RepathRoutine.Reattach, ref __state);
            }
        }

        [HarmonyPatch(typeof(Regiment), "CheckForReattachmentOrder")]
        [HarmonyFinalizer]
        internal static void ReattachFinalizer(Regiment __instance, RoutineState __state)
        {
            if (!__state.Entered) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.reattach.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.reattach.finalizer");
                Leave(__instance, RepathRoutine.Reattach, __state);
            }
        }

        [HarmonyPatch(typeof(Regiment), "CheckEnemyContact")]
        [HarmonyPrefix]
        internal static void ContactPrefix(Regiment __instance, out RoutineState __state)
        {
            __state = default(RoutineState);
            if (!Enabled() || !Tracked(__instance)) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.contact-interruption.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.contact-interruption.prefix");
                Enter(__instance, RepathRoutine.ContactInterruption, ref __state);
            }
        }

        [HarmonyPatch(typeof(Regiment), "CheckEnemyContact")]
        [HarmonyFinalizer]
        internal static void ContactFinalizer(Regiment __instance, RoutineState __state)
        {
            if (!__state.Entered) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.contact-interruption.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.contact-interruption.finalizer");
                Leave(__instance, RepathRoutine.ContactInterruption, __state);
            }
        }

        private static void Enter(Regiment unit, RepathRoutine routine, ref RoutineState state)
        {
            try
            {
                state.Previous = RepathRoutineMarker.Enter(routine, unit.GetInstanceID());
                state.Entered = true;
                state.LinkedId = LinkedId(unit);
                state.Reattach = unit.reattachmentorder;
            }
            catch (Exception ex)
            {
                Warn(ex);
            }
        }

        private static void Leave(Regiment unit, RepathRoutine routine, RoutineState state)
        {
            RepathRoutineMarker.Leave(state.Previous);
            try
            {
                int linked = LinkedId(unit);
                bool reattach = unit.reattachmentorder;
                if (linked == state.LinkedId && reattach == state.Reattach) return;
                EmitRoutine(unit, routine, state.LinkedId, linked, state.Reattach, reattach);
            }
            catch (Exception ex)
            {
                Warn(ex);
            }
        }

        // ---- RegimentSetPath and SetMovementMode ----------------------------------------------------------------------

        [HarmonyPatch(typeof(Regiment), "RegimentSetPath")]
        [HarmonyPrefix]
        internal static void SetPathPrefix(Regiment __instance, out PathState __state)
        {
            __state = default(PathState);
            if (!Enabled() || !Tracked(__instance)) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.set-path.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.set-path.prefix");
                try
                {
                    __state.Tracked = true;
                    __state.Paths = __instance.regimentpaths;
                    __state.Mode = __instance.movementmode;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                }
            }
        }

        [HarmonyPatch(typeof(Regiment), "RegimentSetPath")]
        [HarmonyPostfix]
        internal static void SetPathPostfix(Regiment __instance, Vector3 target, bool newpath, bool modifylastwaypoint, int state, bool uimovement, PathState __state)
        {
            if (!__state.Tracked) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.set-path", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.set-path");
                try
                {
                    int id = __instance.GetInstanceID();
                    RepathRoutine routine = RepathRoutineMarker.For(id);
                    if (!Budget.TryTake(id, routine, RepathKind.Path, Time.realtimeSinceStartup, out int suppressed, out int dropped)) return;
                    EmitPath(__instance, routine, target, newpath, modifylastwaypoint, state, uimovement, __state, suppressed, dropped);
                }
                catch (Exception ex)
                {
                    Warn(ex);
                }
            }
        }

        [HarmonyPatch(typeof(Regiment), "SetMovementMode")]
        [HarmonyPrefix]
        internal static void SetModePrefix(Regiment __instance, out PathState __state)
        {
            __state = default(PathState);
            if (!Enabled() || !Tracked(__instance)) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.set-mode.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.set-mode.prefix");
                try
                {
                    __state.Tracked = true;
                    __state.Mode = __instance.movementmode;
                }
                catch (Exception ex)
                {
                    Warn(ex);
                }
            }
        }

        [HarmonyPatch(typeof(Regiment), "SetMovementMode")]
        [HarmonyPostfix]
        internal static void SetModePostfix(Regiment __instance, int newmode, PathState __state)
        {
            if (!__state.Tracked) return;
            using (TelemetryPerf.Scope("tactical.patch.player-unit-repath.set-mode", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.player-unit-repath.set-mode");
                try
                {
                    int after = __instance.movementmode;
                    if (after == __state.Mode) return;
                    int id = __instance.GetInstanceID();
                    RepathRoutine routine = RepathRoutineMarker.For(id);
                    if (!Budget.TryTake(id, routine, RepathKind.Mode, Time.realtimeSinceStartup, out int suppressed, out int dropped)) return;
                    EmitMode(__instance, routine, __state.Mode, newmode, after, suppressed, dropped);
                }
                catch (Exception ex)
                {
                    Warn(ex);
                }
            }
        }

        // ---- rows ---------------------------------------------------------------------------------------------------------

        private static void EmitPath(Regiment unit, RepathRoutine routine, Vector3 target, bool newPath, bool modifyLast, int state, bool uiMove,
            PathState before, int suppressed, int dropped)
        {
            string routineName = RepathRoutineMarker.Name(routine);
            string name = UnitLabel(unit);
            int side = unit.alliance, unittyp = unit.unittyp, pathsAfter = unit.regimentpaths, modeAfter = unit.movementmode;
            bool detached = unit.parentregiment != null, reattach = unit.reattachmentorder;
            int stance = unit.ai_stance, behavior = unit.combatbehavior, linked = LinkedId(unit);
            double enemyM = Math.Round(EnemyDistance(unit), 1), parentM = Math.Round(ParentDistance(unit), 1);
            double toX = Math.Round(target.x, 1), toZ = Math.Round(target.z, 1);
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, EventName, TelemetrySeverity.Info,
                ev => ev
                    .WithSide(side)
                    .WithUnit(name)
                    .WithField("kind", "path")
                    .WithField("routine", routineName)
                    .WithField("unittyp", unittyp)
                    .WithField("detached", detached)
                    .WithField("toX", toX)
                    .WithField("toZ", toZ)
                    .WithField("newPath", newPath)
                    .WithField("modifyLast", modifyLast)
                    .WithField("state", state)
                    .WithField("uiMove", uiMove)
                    .WithField("paths", before.Paths + "->" + pathsAfter)
                    .WithField("mode", before.Mode + "->" + modeAfter)
                    .WithField("linkedTo", linked)
                    .WithField("reattach", reattach)
                    .WithField("stance", stance)
                    .WithField("behavior", behavior)
                    .WithField("enemyM", enemyM)
                    .WithField("parentM", parentM)
                    .WithField("suppressed", suppressed)
                    .WithField("dropped", dropped));
        }

        private static void EmitMode(Regiment unit, RepathRoutine routine, int from, int requested, int to, int suppressed, int dropped)
        {
            string routineName = RepathRoutineMarker.Name(routine);
            string name = UnitLabel(unit);
            int side = unit.alliance, unittyp = unit.unittyp, paths = unit.regimentpaths, stance = unit.ai_stance;
            bool detached = unit.parentregiment != null;
            double enemyM = Math.Round(EnemyDistance(unit), 1), parentM = Math.Round(ParentDistance(unit), 1);
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, EventName, TelemetrySeverity.Info,
                ev => ev
                    .WithSide(side)
                    .WithUnit(name)
                    .WithField("kind", "mode")
                    .WithField("routine", routineName)
                    .WithField("unittyp", unittyp)
                    .WithField("detached", detached)
                    .WithField("mode", from + "->" + to)
                    .WithField("requested", requested)
                    .WithField("paths", paths)
                    .WithField("stance", stance)
                    .WithField("enemyM", enemyM)
                    .WithField("parentM", parentM)
                    .WithField("suppressed", suppressed)
                    .WithField("dropped", dropped));
        }

        private static void EmitRoutine(Regiment unit, RepathRoutine routine, int linkedFrom, int linkedTo, bool reattachFrom, bool reattachTo)
        {
            int id = unit.GetInstanceID();
            if (!Budget.TryTake(id, routine, RepathKind.Routine, Time.realtimeSinceStartup, out int suppressed, out int dropped)) return;
            string routineName = RepathRoutineMarker.Name(routine);
            string name = UnitLabel(unit);
            int side = unit.alliance, unittyp = unit.unittyp, stance = unit.ai_stance, mode = unit.movementmode;
            bool detached = unit.parentregiment != null;
            double enemyM = Math.Round(EnemyDistance(unit), 1), parentM = Math.Round(ParentDistance(unit), 1);
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, EventName, TelemetrySeverity.Info,
                ev => ev
                    .WithSide(side)
                    .WithUnit(name)
                    .WithField("kind", "routine")
                    .WithField("routine", routineName)
                    .WithField("unittyp", unittyp)
                    .WithField("detached", detached)
                    .WithField("linkedTo", linkedFrom + "->" + linkedTo)
                    .WithField("reattach", reattachFrom + "->" + reattachTo)
                    .WithField("mode", mode)
                    .WithField("stance", stance)
                    .WithField("enemyM", enemyM)
                    .WithField("parentM", parentM)
                    .WithField("suppressed", suppressed)
                    .WithField("dropped", dropped));
        }

        // ---- reads --------------------------------------------------------------------------------------------------------

        private static bool Enabled()
        {
            return Plugin.Instance != null && Plugin.Instance.Enabled.Value && Plugin.Instance.EnableTacticalObserver.Value;
        }

        // A battle unit the W&L player owns (dlcw_isundercommander, the flag DetachCompany copies to a company, 90477), in a W&L career only: outside
        // one every player-side unit has the flag set (113065, 91170-91176) and CheckForReattachmentOrder runs every frame (112052), so without the
        // DLC_WL.dlc_scenarioactive test the whole player side would pay the scope pair on each frame.
        private static bool Tracked(Regiment unit)
        {
            return DLC_WL.dlc_scenarioactive && (UnityEngine.Object)unit != null && !unit.calledfromcampaign && unit.dlcw_isundercommander;
        }

        private static int LinkedId(Regiment unit)
        {
            GameObject linked = unit.unitlinkedto;
            return (UnityEngine.Object)linked != null ? linked.GetInstanceID() : 0;
        }

        // XZ distance to the closest enemy regiment the unit's range list holds; -1 when none.
        private static double EnemyDistance(Regiment unit)
        {
            Regiment enemy = unit.unitrange != null ? unit.unitrange.closestenemyunitfarreg : null;
            return (UnityEngine.Object)enemy != null ? Xz(unit.transform.position, enemy.transform.position) : -1.0;
        }

        private static double ParentDistance(Regiment unit)
        {
            GameObject parent = unit.parentregiment;
            return (UnityEngine.Object)parent != null ? Xz(unit.transform.position, parent.transform.position) : -1.0;
        }

        private static double Xz(Vector3 a, Vector3 b)
        {
            double dx = a.x - b.x, dz = a.z - b.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static string UnitLabel(Regiment unit)
        {
            return unit.name + "#" + unit.GetInstanceID();
        }

        private static void Warn(Exception ex)
        {
            OnceLog.Warning("tactical-battle-core:player-unit-repath-failed",
                "[PlayerUnitRepath] a call was not observed: " + ex.GetType().Name + " " + ex.Message);
        }
    }
}
