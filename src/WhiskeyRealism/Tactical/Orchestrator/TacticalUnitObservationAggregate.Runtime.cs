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
        private static FieldInfo _commandHierarchyShiftField;
        private static FieldInfo _fatigueField;
        private static FieldInfo _ammoField;
        private static int _captureFailureLogged;  // OnceLog gate

        /// <summary>
        /// Single-walk capture. Walks <c>BattleUnits.completeunitlist</c>
        /// once, populating allied-specific fields (visibility, objective,
        /// waypoint, fatigue/ammo) only when the candidate's
        /// <c>reg.alliance == allianceId</c>. Enemy units get cheap
        /// fields (position, strength, unittyp, routed flag,
        /// permanently-detached flag) only — matching the pre-refactor
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
                int shift = ReadCommandHierarchyShift();
                var raw = BattleUnits.completeunitlist as IList;
                if (raw == null) return this;
                for (int i = 0; i < raw.Count; i++)
                {
                    var reg = raw[i] as Regiment;
                    if (reg == null) continue;
                    bool isOwn = reg.alliance == allianceId;
                    var obs = CaptureUnit(reg, isOwn, shift);
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

        private TacticalUnitObservation CaptureUnit(Regiment reg, bool isOwn, int commandHierarchyShift)
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

            int unittyp = SafeUnittyp(reg);
            int alliance = SafeAlliance(reg);
            bool isRouted = SafeIsRouted(reg);
            bool permanentlyDetached = SafePermanentlyDetached(reg);
            // Strength = legacy SafeStrength(reg) — groupstrengthaigroup with strength fallback.
            // Matches the field the legacy main loop / TryVisibleEnemyLine / TryMovementAnchorLine pass into
            // friendlyStrengths. Use the promoted helper so semantics stay identical.
            float strength = 0f;
            try { strength = TacticalVisionRuntimeAdapter.SafeStrength(reg); }
            catch { strength = 0f; }
            float groupOwnInRange = SafeOwnInRange(reg);
            float groupAiGroup = SafeAiGroup(reg);

            bool hasObj = false;
            int objId = 0;
            float objX = 0f, objZ = 0f;
            var objType = TacticalObjectiveType.UnknownVanillaObjective;
            bool hasLastWaypoint = false;
            float lastWaypointX = 0f, lastWaypointZ = 0f;
            // VisibleEnemyStrength = legacy EstimateVisibleEnemyStrength(own) — strength of single
            // closest visible enemy (NOT the sum from TacticalFogOfWarContact.VisibleEnemyStrength).
            // Use the promoted helper to preserve parity.
            float visibleEnemyStrength = 0f;
            bool hasVisibleEnemy = false;
            float fatigue01 = 0.2f;
            float ammo01 = 0.9f;

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
                            objId = TacticalVisionRuntimeAdapter.SafeObjectiveIdHash(obj);
                            objX = pt.X;
                            objZ = pt.Z;
                            objType = TacticalObjectiveType.UnknownVanillaObjective;
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

                // Visibility (own-side only — never call for enemy units)
                try
                {
                    visibleEnemyStrength = TacticalVisionRuntimeAdapter.EstimateVisibleEnemyStrength(reg);
                    hasVisibleEnemy = TacticalFogOfWarContact.HasVisibleEnemy(reg);
                }
                catch { }

                fatigue01 = ClampUnit(SafeFloat(reg, ref _fatigueField, "fatigue"), 0.2f);
                ammo01 = ClampUnit(SafeFloat(reg, ref _ammoField, "ammo"), 0.9f);
            }

            int effective = unittyp - commandHierarchyShift;
            return new TacticalUnitObservation(
                instanceId: instanceId,
                unittyp: unittyp,
                alliance: alliance,
                isRouted: isRouted,
                permanentlyDetached: permanentlyDetached,
                worldX: worldX,
                worldZ: worldZ,
                strength: strength,
                groupOwnInRange: groupOwnInRange,
                groupAiGroup: groupAiGroup,
                hasCurrentSetObjective: hasObj,
                currentSetObjectiveId: objId,
                objectiveX: objX,
                objectiveZ: objZ,
                objectiveType: objType,
                hasLastWaypoint: hasLastWaypoint,
                lastWaypointX: lastWaypointX,
                lastWaypointZ: lastWaypointZ,
                visibleEnemyStrength: visibleEnemyStrength,
                hasVisibleEnemy: hasVisibleEnemy,
                fatigue01: fatigue01,
                ammo01: ammo01,
                effectiveCommandLevel: effective);
        }

        private static int SafeUnittyp(Regiment reg) { try { return reg.unittyp; } catch { return 0; } }
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
        private static float SafeOwnInRange(Regiment reg) { try { return reg.groupowninrange; } catch { return 0f; } }
        private static float SafeAiGroup(Regiment reg) { try { return reg.groupstrengthaigroup; } catch { return 0f; } }

        private static float SafeFloat(Regiment reg, ref FieldInfo cache, string fieldName)
        {
            try
            {
                if (cache == null) cache = AccessTools.Field(typeof(Regiment), fieldName);
                if (cache == null) return 0f;
                var v = cache.GetValue(reg);
                return v is float f ? f : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static float ClampUnit(float v, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static int ReadCommandHierarchyShift()
        {
            try
            {
                if (_commandHierarchyShiftField == null)
                    _commandHierarchyShiftField = AccessTools.Field(typeof(GamePrefs), "commandhierarchyshift");
                if (_commandHierarchyShiftField == null) return 0;
                var v = _commandHierarchyShiftField.GetValue(null);
                if (v is int shift) return shift;
                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
