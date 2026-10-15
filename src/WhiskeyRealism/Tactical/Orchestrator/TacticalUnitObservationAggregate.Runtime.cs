using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime portion of <see cref="TacticalUnitObservationAggregate"/>.
    /// Holds the Unity-touching <see cref="Capture"/> entry plus the
    /// reflection caches used by <see cref="CaptureUnit"/>. Excluded from
    /// the harness test compile (tests use <see cref="LoadForTest"/>).
    /// </summary>
    public sealed partial class TacticalUnitObservationAggregate
    {
        private static int _captureFailureLogged;  // OnceLog gate

        /// <summary>
        /// Single-walk capture. Walks <c>BattleUnits.completeunitlist</c>
        /// once, populating allied-specific fields (visibility, objective,
        /// waypoint) only when the candidate's
        /// <c>reg.alliance == allianceId</c>. Enemy units get cheap
        /// fields (strength, routed flag, permanently-detached flag) only — matching the pre-refactor
        /// cost profile where visibility logic was gated by
        /// <c>IsUsableOwnUnit</c>.
        /// </summary>
        public IObservationSource Capture(int allianceId)
        {
            _units.Clear();
            _alliedIndices.Clear();
            _enemyIndices.Clear();
            _capturedForAlliance = allianceId;
            try
            {
                var raw = BattleUnits.completeunitlist as IList;
                if (raw == null) return this;
                for (int i = 0; i < raw.Count; i++)
                {
                    var reg = raw[i] as Regiment;
                    if (reg == null) continue;
                    bool isOwn = reg.alliance == allianceId;
                    var obs = CaptureUnit(reg, isOwn);
                    _units.Add(obs);
                    int idx = _units.Count - 1;
                    if (isOwn) _alliedIndices.Add(idx);
                    else _enemyIndices.Add(idx);
                }
            }
            catch (Exception e)
            {
                if (_captureFailureLogged == 0)
                {
                    _captureFailureLogged = 1;
                    Plugin.Log.LogWarning("[TacticalUnitObservationAggregate.Capture] degraded: "
                        + e.GetType().Name + " " + e.Message);
                }
            }
            return this;
        }

        private TacticalUnitObservation CaptureUnit(Regiment reg, bool isOwn)
        {
            int instanceId = 0;
            float worldX = 0f, worldZ = 0f;
            try
            {
                var go = ((Component)reg).gameObject;
                if (go != null)
                {
                    instanceId = go.GetInstanceID();
                    if (go.transform != null)
                    {
                        var p = go.transform.position;
                        worldX = p.x;
                        worldZ = p.z;
                    }
                }
            }
            catch { }

            int alliance = SafeAlliance(reg);
            bool isRouted = SafeIsRouted(reg);
            bool permanentlyDetached = SafePermanentlyDetached(reg);
            // Strength = legacy SafeStrength(reg) — groupstrengthaigroup with strength fallback.
            // Matches the field the legacy main loop / TryVisibleEnemyLine / TryMovementAnchorLine pass into
            // friendlyStrengths. Use the promoted helper so semantics stay identical.
            float strength = 0f;
            try { strength = TacticalVisionRuntimeAdapter.SafeStrength(reg); }
            catch { strength = 0f; }

            bool hasObj = false;
            string objectiveName = string.Empty;
            float objX = 0f, objZ = 0f;
            bool hasLastWaypoint = false;
            float lastWaypointX = 0f, lastWaypointZ = 0f;
            // VisibleEnemyStrength = legacy EstimateVisibleEnemyStrength(own) — strength of single
            // closest visible enemy (NOT the sum from TacticalFogOfWarContact.VisibleEnemyStrength).
            // Use the promoted helper to preserve parity.
            float visibleEnemyStrength = 0f;
            bool hasVisibleEnemy = false;
            bool hasVisibleEnemyPosition = false;
            float visibleEnemyX = 0f, visibleEnemyZ = 0f;

            if (isOwn)
            {
                // Current-set objective (own-side only)
                try
                {
                    var obj = TacticalVisionRuntimeAdapter.SafeCurrentSetObjective(reg);
                    if (obj != null)
                    {
                        var pt = TacticalVisionRuntimeAdapter.SafeObjectivePoint(obj);
                        if (TacticalVisionRuntimeAdapter.IsUsableMapPoint(pt))
                        {
                            hasObj = true;
                            objectiveName = TacticalVisionRuntimeAdapter.SafeObjectiveName(obj);
                            objX = pt.X;
                            objZ = pt.Z;
                        }
                    }
                }
                catch { }

                // Last waypoint (TryMovementAnchorLine fallback) — reject within 25m
                // of current position to match TryLastWaypointPoint behavior at
                // TacticalVisionRuntimeAdapter.cs:1063. Use the helper's IsDefaultVector
                // for the "unset" check so the threshold/behavior matches exactly.
                try
                {
                    Vector3 wp = reg.lastsetwaypointposition;
                    if (!TacticalVisionRuntimeAdapter.IsDefaultVector(wp))
                    {
                        float dx = worldX - wp.x;
                        float dz = worldZ - wp.z;
                        if ((dx * dx) + (dz * dz) >= 625f)
                        {
                            hasLastWaypoint = true;
                            lastWaypointX = wp.x;
                            lastWaypointZ = wp.z;
                        }
                    }
                }
                catch { }

                // Visibility (own-side only — never call for enemy units).
                // Call ClosestVisibleEnemy(reg) once to obtain both strength and
                // position, eliminating the redundant HasVisibleEnemy range walk.
                // Strength uses SafeStrength(enemy); position uses transform.position
                // wrapped in a separate try so a destroyed component doesn't poison
                // the strength read. HasVisibleEnemyPosition uses IsDefaultVector's
                // XZ threshold (< 0.01f absolute) to reject zero-origin positions.
                try
                {
                    var enemy = TacticalFogOfWarContact.ClosestVisibleEnemy(reg);
                    if (enemy != null)
                    {
                        hasVisibleEnemy = true;
                        try { visibleEnemyStrength = TacticalVisionRuntimeAdapter.SafeStrength(enemy); }
                        catch { visibleEnemyStrength = 0f; }
                        try
                        {
                            var ego = ((Component)enemy).gameObject;
                            if (ego != null && ego.transform != null)
                            {
                                var p = ego.transform.position;
                                if (!float.IsNaN(p.x) && !float.IsNaN(p.z) &&
                                    !float.IsInfinity(p.x) && !float.IsInfinity(p.z) &&
                                    !(Math.Abs(p.x) < 0.01f && Math.Abs(p.z) < 0.01f))
                                {
                                    hasVisibleEnemyPosition = true;
                                    visibleEnemyX = p.x;
                                    visibleEnemyZ = p.z;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return new TacticalUnitObservation(
                instanceId: instanceId,
                alliance: alliance,
                isRouted: isRouted,
                permanentlyDetached: permanentlyDetached,
                strength: strength,
                hasCurrentSetObjective: hasObj,
                objectiveName: objectiveName,
                objectiveX: objX,
                objectiveZ: objZ,
                hasLastWaypoint: hasLastWaypoint,
                lastWaypointX: lastWaypointX,
                lastWaypointZ: lastWaypointZ,
                visibleEnemyStrength: visibleEnemyStrength,
                hasVisibleEnemy: hasVisibleEnemy,
                hasVisibleEnemyPosition: hasVisibleEnemyPosition,
                visibleEnemyX: visibleEnemyX,
                visibleEnemyZ: visibleEnemyZ);
        }

        private static int SafeAlliance(Regiment reg) { try { return reg.alliance; } catch { return -1; } }
        private static bool SafeIsRouted(Regiment reg)
        {
            try { return reg.isrouted || reg.markedforrout; }
            catch { return false; }
        }
        private static bool SafePermanentlyDetached(Regiment reg)
        {
            // permanentlydetached is a public bool field on Regiment — use direct access.
            try { return reg.permanentlydetached; }
            catch { return false; }
        }
    }
}
