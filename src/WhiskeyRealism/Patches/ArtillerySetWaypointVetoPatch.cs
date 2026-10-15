using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Artillery point-blank waypoint veto (2026-05-22). Postfix on
    // BattleUnits.SetWaypoint(Regiment, ...) that detects ARTILLERY being
    // sent to a destination within 60m of any visible enemy, and immediately
    // overwrites the waypoint with a retreat backward.
    //
    // Background (per docs/scourge-of-war-ai-anchors.md "2026-05-22 artillery
    // panic audit"):
    //   - Vanilla GT can autonomously advance artillery via various code paths
    //     (objective-driven movement, post-firing repositioning) that result
    //     in batteries ending up at point-blank range from enemy infantry.
    //   - Whiskey's TacticalArtilleryMicroDoctrine.Decide has a 60m point-blank
    //     panic retreat, but it runs only inside ApplyArtilleryAudit which
    //     fires once per BGS Apply (1.5s cooldown). Between cycles, vanilla
    //     can push artillery into the danger zone.
    //   - This Postfix catches the violation at the moment the waypoint is
    //     written and immediately issues a counter-retreat-order, closing
    //     the timing gap.
    //
    // SAFETY DESIGN:
    //   1. **Postfix, not Prefix**: vanilla's write still happens; we react
    //      AFTER to overwrite. Less risky than Prefix-blocking per
    //      AGENTS.md hygiene rules.
    //   2. **Filtered hot path**: bails immediately for non-artillery
    //      (cheapest possible check before any geometry work). Postfix
    //      fires for every SetWaypoint call; per-call cost must be near-zero
    //      for non-artillery.
    //   3. **Recursion guard**: thread-static `_inVetoWrite` flag prevents
    //      our own counter-retreat-write from triggering this Postfix again.
    //   4. **Player-protected**: never overrides W&L player commands
    //      (dlcw_isundercommander).
    //   5. **Default-on (Plugin.EnableArtilleryWaypointVeto)** per the
    //      2026-05-18 default-on policy for behavioral writes. Config flag
    //      provides rollback.
    //
    // Per AGENTS.md: this is a Postfix not a Prefix-block — vanilla's intent
    // is preserved, we just react after. The veto is a corrective write
    // through the canonical vanilla `bunits.SetWaypoint` pipeline.
    [HarmonyPatch(typeof(BattleUnits), "SetWaypoint", new[]
    {
        typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool), typeof(float),
        typeof(bool), typeof(bool), typeof(float), typeof(int), typeof(bool),
        typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
    })]
    internal static class ArtillerySetWaypointVetoPatch
    {
        // Point-blank distance: enemy within this many meters of the proposed
        // waypoint = veto + retreat. Matches the doctrine's point-blank floor
        // (TacticalScourgeInspiredDoctrine.Decide at the 60m branch).
        public const float PointBlankMeters = 60f;

        // Retreat distance: how far to push back along the (enemy → artillery)
        // vector. 200m matches vanilla's GamePrefs.artilleryfallbackenemyclosedist
        // typical value, giving artillery enough space to limber + reform.
        public const float RetreatDistanceMeters = 200f;

        // Recursion guard. Our own retreat-write triggers this Postfix again;
        // skip the recursive call to avoid infinite ping-pong. Per-thread
        // (Postfix runs on main, but defensive coding for any threading future).
        [ThreadStatic]
        private static bool _inVetoWrite;

        [HarmonyPostfix]
        internal static void Postfix(BattleUnits __instance, Regiment reg, Vector3 targetpos)
        {
            using (TelemetryPerf.Scope("tactical.patch.artillery-waypoint-veto",
                TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (_inVetoWrite) return;  // recursion guard
                try
                {
                    if (Plugin.EnableArtilleryWaypointVeto == null
                        || !Plugin.EnableArtilleryWaypointVeto.Value) return;
                    if (__instance == null || reg == null) return;

                    // Cheap filter — bail immediately for non-artillery so the
                    // Postfix per-call cost stays sub-microsecond for infantry
                    // / cavalry / commandable groups.
                    if (reg.unittyp != 2) return;
                    if (reg.guns <= 0) return;
                    if (reg.isrouted || reg.markedforrout || reg.permanentlydetached) return;

                    // Player protection — never override W&L commands.
                    try { if (reg.dlcw_isundercommander) return; }
                    catch { return; }

                    // Find closest visible enemy. If no enemy visible, the
                    // waypoint is presumably safe (artillery deploying without
                    // contact).
                    Regiment closestEnemy = null;
                    try { closestEnemy = TacticalFogOfWarContact.ClosestVisibleEnemy(reg); }
                    catch { closestEnemy = null; }
                    if (closestEnemy == null) return;
                    if (closestEnemy.isrouted || closestEnemy.permanentlydetached) return;

                    // Check distance from PROPOSED WAYPOINT to enemy (not from
                    // artillery's current position). The veto fires when the
                    // destination would put the artillery at point-blank,
                    // catching vanilla "advance to position X" orders before
                    // they materialize the bad outcome.
                    Vector3 enemyPos = closestEnemy.transform.position;
                    float dx = enemyPos.x - targetpos.x;
                    float dz = enemyPos.z - targetpos.z;
                    float distSq = dx * dx + dz * dz;
                    if (distSq > PointBlankMeters * PointBlankMeters) return;

                    // Veto: compute retreat target (push back along enemy →
                    // artillery direction) and issue an overriding waypoint.
                    float dist = (float)Math.Sqrt(distSq);
                    if (dist < 0.1f)
                    {
                        // Degenerate case: target IS the enemy. Push back
                        // along (enemy → current artillery position) instead.
                        var artPos = reg.transform.position;
                        dx = artPos.x - enemyPos.x;
                        dz = artPos.z - enemyPos.z;
                        float fbSq = dx * dx + dz * dz;
                        if (fbSq < 0.01f) return;  // can't determine direction
                        dist = (float)Math.Sqrt(fbSq);
                    }
                    float invDist = 1f / dist;
                    Vector3 retreatTarget = new Vector3(
                        targetpos.x - dx * invDist * RetreatDistanceMeters * -1f,
                        targetpos.y,
                        targetpos.z - dz * invDist * RetreatDistanceMeters * -1f);
                    // The above moves AWAY from enemy: (artillery - enemy)
                    // direction times retreat distance, added to the proposed
                    // target. Simplification: (enemy → me) unit-vector * 200m.
                    // Recompute cleanly:
                    var artPos2 = reg.transform.position;
                    float awayX = artPos2.x - enemyPos.x;
                    float awayZ = artPos2.z - enemyPos.z;
                    float awayLen = (float)Math.Sqrt(awayX * awayX + awayZ * awayZ);
                    if (awayLen < 0.1f) return;
                    awayX /= awayLen;
                    awayZ /= awayLen;
                    retreatTarget = new Vector3(
                        artPos2.x + awayX * RetreatDistanceMeters,
                        artPos2.y,
                        artPos2.z + awayZ * RetreatDistanceMeters);

                    // Set defensive stance + force limber before retreating.
                    TacticalStanceWriter.EnsureStance(__instance, reg, 2,
                        contextTag: "artillery-waypoint-veto:" + SafeName(reg));
                    try { reg.ChangeRegimentFormation(GameVars.SetFormationParam(0, manualset: true, 1)); }
                    catch { }

                    // Issue the retreat waypoint with recursion guard.
                    _inVetoWrite = true;
                    try
                    {
                        __instance.SetWaypoint(
                            reg,
                            retreatTarget,
                            newpath: true,
                            doublequick: true,
                            manualfinalrotation: -1f,
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

                    EmitVetoTelemetry(reg, targetpos, dist, retreatTarget, closestEnemy);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-artillery-veto:exception",
                        "[TacticalArtilleryWaypointVeto] Postfix threw: "
                        + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static void EmitVetoTelemetry(Regiment reg, Vector3 vetoedTarget, float enemyDist, Vector3 retreatTarget, Regiment enemy)
        {
            try
            {
                int instanceId;
                try { instanceId = reg.GetInstanceID(); } catch { instanceId = 0; }
                string unit = SafeName(reg);
                string enemyName = "<noname>";
                try { enemyName = ((UnityEngine.Object)((Component)enemy).gameObject).name ?? "<noname>"; } catch { }
                string sig = "TacticalArtilleryWaypointVeto|unit=" + unit
                    + "|id=" + instanceId
                    + "|enemy=" + enemyName
                    + "|enemyDist=" + enemyDist.ToString("0");
                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Gate,
                    "TacticalArtilleryWaypointVeto",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithUnit(unit)
                        .WithDecision("Vetoed", "point-blank-destination", sig)
                        .WithField("instanceId", instanceId)
                        .WithField("vetoedTargetX", vetoedTarget.x)
                        .WithField("vetoedTargetZ", vetoedTarget.z)
                        .WithField("retreatTargetX", retreatTarget.x)
                        .WithField("retreatTargetZ", retreatTarget.z)
                        .WithField("enemyName", enemyName)
                        .WithField("enemyDistanceFromVetoedTarget", enemyDist));
            }
            catch { /* never throw out of Postfix */ }
        }

        private static string SafeName(Regiment reg)
        {
            try { return ((UnityEngine.Object)((Component)reg).gameObject).name ?? "<noname>"; }
            catch { return "<error>"; }
        }
    }
}
