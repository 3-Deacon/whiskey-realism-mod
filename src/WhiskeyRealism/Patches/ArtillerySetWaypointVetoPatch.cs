using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Artillery point-blank waypoint veto. Postfix on
    // BattleUnits.SetWaypoint(Regiment, ...) (decompile 91232).
    //
    // When vanilla AI orders an AI battery to a destination within 60 m of a
    // visible enemy (vanilla can push batteries forward through micro movement,
    // e.g. the useorderdelay SetWaypoint at 4146), the patch cancels that order
    // and pulls the battery back the way vanilla's own CheckArtyFallback does.
    //
    // Vanilla anchors / rules (BR-C2):
    //   - showmovementoptions defaults true for player UI orders; every vanilla
    //     AI call site passes false (3139, 3643, 4146, 4768, 4957, 6235, 6959).
    //     A true value is a player order and is never touched.
    //   - TacticalWriteGuard.CanWrite: AI-controlled battery, Commander Mode
    //     Active, W&L player chain protected.
    //   - distance is from the nearest VISIBLE enemy to the TARGET position.
    //   - the vanilla order is cancelled through Regiment.DeleteOrderQueue
    //     (vanilla's own cancellation: ResetOrderState +
    //     ClearAllNonTransmittedWaypoints) when it was queued with order delay
    //     (SetWaypoint 91455-91487 queues it on the parent, or on the unit).
    //   - limber with BattleUnits.MountUnits(reg, 1) (formation 3, 90872); the
    //     old SetFormationParam(0, ..., 1) UNLIMBERED (135440-135452).
    //   - no stance write: ChangeStance on a battery sets permanentlydetached
    //     (90790) and, for the player alliance outside ai_vs_ai, hands it to
    //     aibattle[1] (90798); vanilla artillery AI then skips it forever.
    //   - retreat along unitrange.retreatangle by artilleryfallbackenemyclosedist
    //     (CheckArtyFallback 3525) with an immediate, non-UI SetWaypoint.
    // Telemetry is throttled (static key, 5 s real time).
    [HarmonyPatch(typeof(BattleUnits), "SetWaypoint", new[]
    {
        typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool), typeof(float),
        typeof(bool), typeof(bool), typeof(float), typeof(int), typeof(bool),
        typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
    })]
    internal static class ArtillerySetWaypointVetoPatch
    {
        public const float PointBlankMeters = 60f;
        private const float DefaultRetreatMeters = 200f;
        private const float TelemetryThrottleSeconds = 5f;

        private static readonly Dictionary<string, float> _lastTelemetryAt = new Dictionary<string, float>();

        // Our own retreat SetWaypoint re-enters this Postfix.
        [ThreadStatic]
        private static bool _inVetoWrite;

        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance, Regiment reg, Vector3 targetpos, bool showmovementoptions)
        {
            using (TelemetryPerf.Scope("tactical.patch.artillery-waypoint-veto",
                TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (_inVetoWrite) return;
                try
                {
                    if ((UnityEngine.Object)reg == null || reg.unittyp != 2 || showmovementoptions) return;
                    if (Plugin.EnableArtilleryWaypointVeto == null || !Plugin.EnableArtilleryWaypointVeto.Value) return;
                    if (__instance == null) return;

                    bool canWrite = TacticalWriteGuard.CanWrite(reg);
                    float nearestSq = canWrite ? NearestVisibleEnemyDistanceSq(reg, targetpos) : float.MaxValue;
                    if (!BattleWriterPolicy.ShouldVetoArtilleryWaypoint(
                            reg.unittyp,
                            reg.guns,
                            reg.isrouted || reg.markedforrout,
                            reg.permanentlydetached,
                            showmovementoptions,
                            canWrite,
                            nearestSq,
                            PointBlankMeters))
                        return;

                    int cancelled = CancelQueuedMoveOrders(reg);
                    if (reg.mounted != 1) __instance.MountUnits(reg, 1);

                    Vector3 retreatTarget = RetreatTarget(reg, out float retreatAngle);
                    _inVetoWrite = true;
                    try
                    {
                        __instance.SetWaypoint(
                            reg,
                            retreatTarget,
                            newpath: true,
                            doublequick: false,
                            manualfinalrotation: retreatAngle + 180f,
                            modifylastwaypoint: false,
                            useorderdelay: false,
                            timetomove: -1f,
                            direction: -1,
                            showmovementoptions: false,
                            ignorebattlemonuments: false,
                            groupmoveonly: false,
                            ignoredisabledships: false,
                            checkforreadiness: true,
                            clearinterruptionpaths: true);
                    }
                    finally { _inVetoWrite = false; }
                    reg.SetMovementMode(4);

                    EmitVetoTelemetry(reg, targetpos, (float)Math.Sqrt(nearestSq), retreatTarget, cancelled);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-artillery-veto:exception",
                        "[TacticalArtilleryWaypointVeto] Postfix threw: "
                        + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static float NearestVisibleEnemyDistanceSq(Regiment reg, Vector3 target)
        {
            float best = float.MaxValue;
            if (reg.unitrange == null) return best;
            Accumulate(reg.unitrange.enemyinfirerangereg, target, ref best);
            Accumulate(reg.unitrange.enemyinrangereg, target, ref best);
            return best;
        }

        private static void Accumulate(IList<Regiment> enemies, Vector3 target, ref float best)
        {
            if (enemies == null) return;
            for (int i = 0; i < enemies.Count; i++)
            {
                Regiment enemy = enemies[i];
                if ((UnityEngine.Object)enemy == null || enemy.isrouted || enemy.permanentlydetached) continue;
                Vector3 p = ((Component)enemy).transform.position;
                float dx = p.x - target.x;
                float dz = p.z - target.z;
                float sq = dx * dx + dz * dz;
                if (sq < best) best = sq;
            }
        }

        // SetWaypoint queues a delayed move on the parent group (or the unit
        // when its parent is the "Units" root) as ordertype 0/1 (91455-91487).
        private static int CancelQueuedMoveOrders(Regiment reg)
        {
            int cancelled = 0;
            cancelled += CancelQueuedMoveOrders(reg, reg);
            Transform parent = ((Component)reg).transform.parent;
            Regiment parentRegiment = (UnityEngine.Object)parent != null ? parent.GetComponent<Regiment>() : null;
            if ((UnityEngine.Object)parentRegiment != null && (UnityEngine.Object)parentRegiment != (UnityEngine.Object)reg)
                cancelled += CancelQueuedMoveOrders(parentRegiment, reg);
            return cancelled;
        }

        private static int CancelQueuedMoveOrders(Regiment queueOwner, Regiment advised)
        {
            if (queueOwner.orderqueue == null) return 0;
            GameObject advisedObject = ((Component)advised).gameObject;
            int cancelled = 0;
            for (int i = queueOwner.orderqueue.Count - 1; i >= 0; i--)
            {
                var order = queueOwner.orderqueue[i];
                if (order == null || order.ordertype > 1) continue;
                if ((UnityEngine.Object)order.advisedunit != (UnityEngine.Object)advisedObject) continue;
                queueOwner.DeleteOrderQueue(i);
                cancelled++;
            }

            return cancelled;
        }

        private static Vector3 RetreatTarget(Regiment reg, out float retreatAngle)
        {
            Vector3 own = ((Component)reg).transform.position;
            retreatAngle = reg.unitrange != null
                ? reg.unitrange.retreatangle
                : ((Component)reg).transform.eulerAngles.y + 180f;
            float distance = GamePrefs.artilleryfallbackenemyclosedist > 0f
                ? GamePrefs.artilleryfallbackenemyclosedist
                : DefaultRetreatMeters;
            BattleWriterPolicy.AnglePointXZ(own.x, own.z, retreatAngle, distance, out float x, out float z);
            return TacticalTerrainProbe.WithTerrainHeight(new Vector3(x, own.y, z));
        }

        private static void EmitVetoTelemetry(Regiment reg, Vector3 vetoedTarget, float enemyDist, Vector3 retreatTarget, int cancelledOrders)
        {
            try
            {
                if (!TacticalTelemetry.ShouldEmit(_lastTelemetryAt, "artillery-veto", "point-blank-destination",
                        Time.realtimeSinceStartup, TelemetryThrottleSeconds, false))
                    return;

                string unit = TacticalCurrentOrderSignature.Safe(reg.name);
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Gate,
                    "TacticalArtilleryWaypointVeto",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithUnit(unit)
                        .WithDecision("Vetoed", "point-blank-destination", "TacticalArtilleryWaypointVeto")
                        .WithField("vetoedTargetX", vetoedTarget.x)
                        .WithField("vetoedTargetZ", vetoedTarget.z)
                        .WithField("retreatTargetX", retreatTarget.x)
                        .WithField("retreatTargetZ", retreatTarget.z)
                        .WithField("cancelledQueuedOrders", cancelledOrders)
                        .WithField("enemyDistanceFromVetoedTarget", enemyDist));
            }
            catch { /* never throw out of Postfix */ }
        }
    }
}
