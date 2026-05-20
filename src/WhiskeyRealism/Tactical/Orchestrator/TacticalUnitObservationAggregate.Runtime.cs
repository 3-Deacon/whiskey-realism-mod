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
        private static FieldInfo _groupEnemiesInRangeField;
        private static FieldInfo _flanksThreatedField;
        private static FieldInfo _outflankedField;
        private static FieldInfo _coverValueField;
        private static FieldInfo _fortInRangeField;
        private static FieldInfo _stanceOrderedField;
        private static FieldInfo _unitRangeField;
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
            string objectiveName = string.Empty;
            float objX = 0f, objZ = 0f;
            var objType = TacticalObjectiveType.UnknownVanillaObjective;
            bool hasLastWaypoint = false;
            float lastWaypointX = 0f, lastWaypointZ = 0f;
            // VisibleEnemyStrength = legacy EstimateVisibleEnemyStrength(own) — strength of single
            // closest visible enemy (NOT the sum from TacticalFogOfWarContact.VisibleEnemyStrength).
            // Use the promoted helper to preserve parity.
            float visibleEnemyStrength = 0f;
            bool hasVisibleEnemy = false;
            bool hasVisibleEnemyPosition = false;
            float visibleEnemyX = 0f, visibleEnemyZ = 0f;
            int visibleEnemyUnittyp = -1;
            string visibleEnemyName = string.Empty;
            bool visibleEnemyRouted = false;
            bool visibleEnemyPermanentlyDetached = false;
            float fatigue01 = 0.2f;
            float ammo01 = 0.9f;
            // Gap-fill chunk 1: per-group sector fields (own-side only).
            float groupEnemiesInRange = 0f;
            float groupAngleEnemyStrength = 0f;
            bool groupFlankRisk = false;
            bool groupStrongPoint = false;
            int groupVanillaStanceOrdered = -1;

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
                        // Gap-fill chunk 1: closest-enemy metadata for
                        // TacticalGroupSectorEstimator.IsVisibleLineContact.
                        // unittyp gates formed-line detection; name carries the
                        // "skirm" substring that rejects skirmisher contact;
                        // routed/permanentlydetached also gate line contact.
                        try { visibleEnemyUnittyp = enemy.unittyp; }
                        catch { visibleEnemyUnittyp = -1; }
                        try { visibleEnemyRouted = enemy.isrouted || enemy.markedforrout; }
                        catch { visibleEnemyRouted = false; }
                        try { visibleEnemyPermanentlyDetached = enemy.permanentlydetached; }
                        catch { visibleEnemyPermanentlyDetached = false; }
                        try
                        {
                            var ego = ((Component)enemy).gameObject;
                            if (ego != null)
                            {
                                try
                                {
                                    string nm = ego.name;
                                    visibleEnemyName = nm ?? string.Empty;
                                }
                                catch { visibleEnemyName = string.Empty; }
                                if (ego.transform != null)
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
                        }
                        catch { }
                    }
                }
                catch { }

                fatigue01 = ClampUnit(SafeFloat(reg, ref _fatigueField, "fatigue"), 0.2f);
                ammo01 = ClampUnit(SafeFloat(reg, ref _ammoField, "ammo"), 0.9f);

                // Gap-fill chunk 1: per-group sector fields (own-side only).
                // BuildGroupSector reads: groupenemiesinrange, unitrange.enemystrengthwithinangle[],
                // flanksthreated, outflanked, covervalue, fortinrange, ai_stanceordered.
                // FlankRisk / StrongPoint are precomputed here to keep the worker
                // pure (avoid passing raw float/int signals it would have to re-threshold).
                groupEnemiesInRange = SafeFloat(reg, ref _groupEnemiesInRangeField, "groupenemiesinrange");
                if (float.IsNaN(groupEnemiesInRange) || float.IsInfinity(groupEnemiesInRange) || groupEnemiesInRange < 0f)
                    groupEnemiesInRange = 0f;
                groupAngleEnemyStrength = SafeAngleEnemyStrength(reg);
                float flanksThreated = SafeFloat(reg, ref _flanksThreatedField, "flanksthreated");
                int outflanked = SafeInt(reg, ref _outflankedField, "outflanked");
                groupFlankRisk = flanksThreated > 0f || outflanked > 0;
                float coverValue = SafeFloat(reg, ref _coverValueField, "covervalue");
                bool fortInRange = SafeBool(reg, ref _fortInRangeField, "fortinrange");
                groupStrongPoint = coverValue > 0.5f || fortInRange;
                groupVanillaStanceOrdered = SafeInt(reg, ref _stanceOrderedField, "ai_stanceordered", -1);
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
                objectiveName: objectiveName,
                objectiveX: objX,
                objectiveZ: objZ,
                objectiveType: objType,
                hasLastWaypoint: hasLastWaypoint,
                lastWaypointX: lastWaypointX,
                lastWaypointZ: lastWaypointZ,
                visibleEnemyStrength: visibleEnemyStrength,
                hasVisibleEnemy: hasVisibleEnemy,
                hasVisibleEnemyPosition: hasVisibleEnemyPosition,
                visibleEnemyX: visibleEnemyX,
                visibleEnemyZ: visibleEnemyZ,
                fatigue01: fatigue01,
                ammo01: ammo01,
                effectiveCommandLevel: effective,
                visibleEnemyUnittyp: visibleEnemyUnittyp,
                visibleEnemyName: visibleEnemyName,
                visibleEnemyRouted: visibleEnemyRouted,
                visibleEnemyPermanentlyDetached: visibleEnemyPermanentlyDetached,
                groupEnemiesInRange: groupEnemiesInRange,
                groupAngleEnemyStrength: groupAngleEnemyStrength,
                groupFlankRisk: groupFlankRisk,
                groupStrongPoint: groupStrongPoint,
                groupVanillaStanceOrdered: groupVanillaStanceOrdered);
        }

        /// <summary>
        /// Mirrors BattleGroupStancePatch.SumEnemyStrengthWithinAngle. Walks
        /// the per-angle slice and sums non-negative entries. Gated on
        /// TacticalFogOfWarContact.HasVisibleEnemy to match the patch's
        /// short-circuit. Wraps everything in try/catch with bounded warn so
        /// reflection/Unity failure degrades to 0f rather than throwing.
        /// </summary>
        private static float SafeAngleEnemyStrength(Regiment reg)
        {
            try
            {
                if (reg == null) return 0f;
                if (_unitRangeField == null)
                    _unitRangeField = AccessTools.Field(typeof(Regiment), "unitrange");
                // unitrange is a public field on Regiment; if reflection fails
                // we still try direct property access via reg.unitrange below.
                var unitrange = _unitRangeField != null ? _unitRangeField.GetValue(reg) : reg.unitrange;
                if (unitrange == null) return 0f;
                var slice = reg.unitrange != null ? reg.unitrange.enemystrengthwithinangle : null;
                if (slice == null) return 0f;
                if (!TacticalFogOfWarContact.HasVisibleEnemy(reg)) return 0f;
                float total = 0f;
                for (int i = 0; i < slice.Length; i++)
                {
                    float v = slice[i];
                    if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) continue;
                    total += v;
                }
                return total;
            }
            catch
            {
                return 0f;
            }
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

        private static int SafeInt(Regiment reg, ref FieldInfo cache, string fieldName, int fallback = 0)
        {
            try
            {
                if (cache == null) cache = AccessTools.Field(typeof(Regiment), fieldName);
                if (cache == null) return fallback;
                var v = cache.GetValue(reg);
                if (v is int i) return i;
                if (v == null) return fallback;
                return Convert.ToInt32(v);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool SafeBool(Regiment reg, ref FieldInfo cache, string fieldName)
        {
            try
            {
                if (cache == null) cache = AccessTools.Field(typeof(Regiment), fieldName);
                if (cache == null) return false;
                var v = cache.GetValue(reg);
                return v is bool b && b;
            }
            catch
            {
                return false;
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
