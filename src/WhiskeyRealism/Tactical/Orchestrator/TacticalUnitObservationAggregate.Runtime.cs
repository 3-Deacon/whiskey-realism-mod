using System;
using System.Collections;
using System.Collections.Generic;
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

        // Active-set scoping: rotating-bucket refresh for inactive own-side units.
        // After each Capture, _bucketCounter advances so the next call walks the
        // next 1/8th of the inactive remainder. At 20× compression and ~10s
        // heavy-gate intervals, each inactive own-side unit refreshes roughly
        // every 80 seconds of game time — plenty fresh for reserves.
        private const int InactiveBucketCount = 8;
        private static int _bucketCounter;

        // Scratch hashset for active-set membership lookup. Held as a field so
        // we don't allocate on every Capture; cleared at the top of each call.
        private readonly HashSet<int> _activeSetIds = new HashSet<int>();

        /// <summary>
        /// Legacy entry — resolves the per-side AIBattle via static lookup
        /// (BattleUnits singleton) and forwards to the AIBattle-aware overload.
        /// </summary>
        public IObservationSource Capture(int allianceId)
        {
            return Capture(allianceId, ResolveBattleUnitsSingleton());
        }

        /// <summary>
        /// Active-set-scoped capture. Replaces the O(N) walk of
        /// <c>BattleUnits.completeunitlist</c> with two-tier iteration:
        ///   (1) Walk <c>AIBattle.unitsused</c> for the side that matches
        ///       <paramref name="allianceId"/>. This is vanilla's own active
        ///       set per cycle — units actively giving/receiving AI orders.
        ///       These get a fresh observation every Capture.
        ///   (2) Walk a rotating 1/<see cref="InactiveBucketCount"/> slice of
        ///       <c>BattleUnits.completeunitlist</c> for own-side units NOT in
        ///       the active set. These reserves / inactive units get refreshed
        ///       on a slower cadence so a steady-state inactive brigade is
        ///       observed roughly every <see cref="InactiveBucketCount"/>
        ///       heavy-gate fires.
        ///
        /// Enemy units are no longer captured — <see cref="EnemyIndices"/> has
        /// zero runtime consumers (all per-own-unit enemy state is captured
        /// inline via <c>ClosestVisibleEnemy(reg)</c>). The aggregate-path's
        /// own-side observations still carry <c>VisibleEnemy*</c> fields, so
        /// downstream consumers (<see cref="TacticalVisionRuntimeAdapter"/>,
        /// <see cref="TacticalOrchestratorWorkerAnalysis"/>) see no change.
        ///
        /// Fallback: when <c>unitsused</c> is null/empty (e.g. deployment or
        /// a degenerate state), the implementation falls through to the legacy
        /// single-walk of <c>completeunitlist</c> for own-side units so we
        /// never publish an empty aggregate when units exist.
        /// </summary>
        public IObservationSource Capture(int allianceId, BattleUnits bunits)
        {
            _units.Clear();
            _alliedIndices.Clear();
            _enemyIndices.Clear();
            _activeSetIds.Clear();
            _capturedForAlliance = allianceId;
            try
            {
                int shift = ReadCommandHierarchyShift();
                var unitsUsed = ResolveUnitsUsedForAlliance(bunits, allianceId);
                var complete = BattleUnits.completeunitlist as IList;

                if (unitsUsed != null && unitsUsed.Count > 0)
                {
                    // (1) Active set — fresh every Capture.
                    for (int i = 0; i < unitsUsed.Count; i++)
                    {
                        var reg = unitsUsed[i] as Regiment;
                        if (reg == null) continue;
                        // unitsused for a given AIBattle is alliance-scoped by
                        // construction; double-check defensively.
                        if (reg.alliance != allianceId) continue;
                        int instanceId = SafeInstanceId(reg);
                        if (instanceId != 0)
                        {
                            if (!_activeSetIds.Add(instanceId)) continue;  // duplicate guard
                        }
                        var obs = CaptureUnit(reg, isOwn: true, commandHierarchyShift: shift);
                        _units.Add(obs);
                        _alliedIndices.Add(_units.Count - 1);
                    }

                    // (2) Rotating bucket — refresh 1/Nth of inactive own-side
                    // remainder so reserves still get periodic observations.
                    if (complete != null && complete.Count > 0)
                    {
                        int bucketIndex = (int)((uint)_bucketCounter % InactiveBucketCount);
                        for (int i = bucketIndex; i < complete.Count; i += InactiveBucketCount)
                        {
                            var reg = complete[i] as Regiment;
                            if (reg == null) continue;
                            if (reg.alliance != allianceId) continue;
                            int instanceId = SafeInstanceId(reg);
                            // Already captured above? Skip.
                            if (instanceId != 0 && _activeSetIds.Contains(instanceId)) continue;
                            var obs = CaptureUnit(reg, isOwn: true, commandHierarchyShift: shift);
                            _units.Add(obs);
                            _alliedIndices.Add(_units.Count - 1);
                            if (instanceId != 0) _activeSetIds.Add(instanceId);
                        }
                        // Advance the bucket pointer with wrap-around. uint cast
                        // makes the modulo work cleanly even after long sessions.
                        _bucketCounter = unchecked(_bucketCounter + 1);
                    }
                }
                else
                {
                    // Fallback: unitsused unavailable (deployment edge case, or
                    // reflection couldn't resolve the per-side AIBattle). Walk
                    // completeunitlist once for own-side units — matches the
                    // pre-refactor behavior so we never publish an empty
                    // aggregate when units exist.
                    if (complete == null) return this;
                    for (int i = 0; i < complete.Count; i++)
                    {
                        var reg = complete[i] as Regiment;
                        if (reg == null) continue;
                        if (reg.alliance != allianceId) continue;
                        var obs = CaptureUnit(reg, isOwn: true, commandHierarchyShift: shift);
                        _units.Add(obs);
                        _alliedIndices.Add(_units.Count - 1);
                    }
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

        // ---- Active-set resolution ----

        private static FieldInfo _aibattleArrayField;
        private static FieldInfo _allianceArrayField;
        private static FieldInfo _unitsUsedField;

        /// <summary>
        /// Resolves <c>BattleUnits.aibattle[side].unitsused</c> for the side
        /// whose <c>bunits.alliance[side] == allianceId</c>. Returns null on
        /// any reflection / null-traversal failure so Capture falls back to
        /// the legacy completeunitlist walk.
        /// </summary>
        private static IList ResolveUnitsUsedForAlliance(BattleUnits bunits, int allianceId)
        {
            try
            {
                if (bunits == null) return null;
                if (_aibattleArrayField == null)
                    _aibattleArrayField = AccessTools.Field(typeof(BattleUnits), "aibattle");
                if (_allianceArrayField == null)
                    _allianceArrayField = AccessTools.Field(typeof(BattleUnits), "alliance");
                if (_aibattleArrayField == null || _allianceArrayField == null) return null;

                var alliance = _allianceArrayField.GetValue(bunits) as int[];
                var aibattleArray = _aibattleArrayField.GetValue(bunits) as Array;
                if (alliance == null || aibattleArray == null) return null;

                int side = -1;
                for (int s = 0; s < alliance.Length && s < aibattleArray.Length; s++)
                {
                    if (alliance[s] == allianceId) { side = s; break; }
                }
                if (side < 0) return null;

                var aib = aibattleArray.GetValue(side) as AIBattle;
                if (aib == null) return null;

                if (_unitsUsedField == null)
                    _unitsUsedField = AccessTools.Field(typeof(AIBattle), "unitsused");
                if (_unitsUsedField == null) return null;
                return _unitsUsedField.GetValue(aib) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static BattleUnits _cachedBattleUnits;

        private static FieldInfo _aibattleBunitsField;

        /// <summary>
        /// BattleUnits is a singleton component on the GameController
        /// GameObject (decompile line 79142 / 188815). Reach it through
        /// the static completeunitlist's owning regiment as a side path
        /// (Regiment.aiassignedref → AIBattle.bunits), or via
        /// UnityEngine.Object.FindObjectOfType as the fallback. Cached
        /// because GameController persists for the battle lifetime.
        /// </summary>
        private static BattleUnits ResolveBattleUnitsSingleton()
        {
            try
            {
                if (_cachedBattleUnits != null) return _cachedBattleUnits;
                // Cheapest: walk the first non-null regiment's aiassignedref
                // (decompile line 110778: public AIBattle aiassignedref on
                // Regiment) and read its .bunits. Many regiments don't carry
                // an aiassignedref (only those AI-assigned to a battle do), so
                // we may have to walk a few entries before finding one.
                var list = BattleUnits.completeunitlist;
                if (list != null)
                {
                    if (_aibattleBunitsField == null)
                        _aibattleBunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                    if (_aibattleBunitsField != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var reg = list[i];
                            if (reg == null) continue;
                            try
                            {
                                var aibRef = reg.aiassignedref;
                                if (aibRef == null) continue;
                                var bunits = _aibattleBunitsField.GetValue(aibRef) as BattleUnits;
                                if (bunits != null) { _cachedBattleUnits = bunits; return bunits; }
                            }
                            catch { }
                        }
                    }
                }
                // Fallback — slower but reliable.
                var found = UnityEngine.Object.FindObjectOfType<BattleUnits>();
                if (found != null) _cachedBattleUnits = found;
                return found;
            }
            catch
            {
                return null;
            }
        }

        private static int SafeInstanceId(Regiment reg)
        {
            try
            {
                if (reg == null) return 0;
                var go = ((Component)reg).gameObject;
                return go != null ? go.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Clears the cached BattleUnits singleton. Called from
        /// <c>TacticalBattleCoordinator.ResetRuntimeTickState</c> on battle
        /// end so the next battle resolves a fresh BattleUnits instance.
        /// </summary>
        internal static void ResetBattleUnitsCache()
        {
            _cachedBattleUnits = null;
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
