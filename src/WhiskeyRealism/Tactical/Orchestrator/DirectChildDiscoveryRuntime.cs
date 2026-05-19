using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Vanilla-touching wrapper for DirectChildDiscovery. Walks
    /// BattleUnits.completeunitlist filtered by allianceId so each side discovers
    /// its own command groups regardless of which AIBattle.OnBattleStart fired
    /// (vanilla creates one AIBattle per side; the coordinator only bootstraps
    /// from one of them, so AIBattle.unitsused is the wrong list for the other
    /// side). Calls Regiment.GetAttachedUnitsReg(directonly: true) to flag
    /// direct-child relationships, then delegates to the pure DirectChildDiscovery.Probe.
    /// </summary>
    public static partial class DirectChildDiscovery
    {
        private static FieldInfo _commandHierarchyShiftField;

        public static IReadOnlyList<DirectChildSnapshot> Snapshot(AIBattle battle)
        {
            // Backwards-compatible overload — when caller doesn't supply an alliance,
            // fall back to the AIBattle's own sideofai mapping.
            if (battle == null) return Array.Empty<DirectChildSnapshot>();
            try
            {
                int allianceId = ResolveAllianceFromBattle(battle);
                return Snapshot(allianceId);
            }
            catch (Exception e)
            {
                OnceLog.Warning("o3-direct-child-discovery:exception",
                    "DirectChildDiscovery.Snapshot(battle) failed: " + e.GetType().Name + " " + e.Message);
                return Array.Empty<DirectChildSnapshot>();
            }
        }

        public static IReadOnlyList<DirectChildSnapshot> Snapshot(int allianceId)
        {
            try
            {
                int shift = ReadCommandHierarchyShift();
                var probes = BuildProbesForAlliance(allianceId, shift);
                return Probe(probes, shift);
            }
            catch (Exception e)
            {
                OnceLog.Warning("o3-direct-child-discovery:exception",
                    "DirectChildDiscovery.Snapshot(allianceId) failed: " + e.GetType().Name + " " + e.Message);
                return Array.Empty<DirectChildSnapshot>();
            }
        }

        private static IReadOnlyList<RegimentProbe> BuildProbesForAlliance(int allianceId, int commandHierarchyShift)
        {
            var units = BattleUnits.completeunitlist as System.Collections.IList;
            if (units == null || units.Count == 0) return Array.Empty<RegimentProbe>();
            int effectiveCommandMin = ClampShiftedMin(commandHierarchyShift);

            // First pass: walk every command-level group on this alliance and collect
            // instanceIds of regiments flagged as direct children via vanilla's
            // GetAttachedUnitsReg(directonly: true).
            var directChildren = new HashSet<int>();
            for (int i = 0; i < units.Count; i++)
            {
                var reg = units[i] as Regiment;
                if (reg == null) continue;
                if (reg.alliance != allianceId) continue;
                var regGo = ((Component)reg).gameObject;
                if (regGo == null) continue;
                if (reg.unittyp < effectiveCommandMin) continue;
                Regiment[] kids;
                try { kids = reg.GetAttachedUnitsReg(true, true, -1, true, false, false, false, false); }
                catch { kids = null; }
                if (kids == null) continue;
                for (int k = 0; k < kids.Length; k++)
                {
                    var kid = kids[k];
                    if (kid == null) continue;
                    var kidGo = ((Component)kid).gameObject;
                    if (kidGo == null) continue;
                    directChildren.Add(kidGo.GetInstanceID());
                }
            }

            var result = new List<RegimentProbe>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                var reg = units[i] as Regiment;
                if (reg == null) continue;
                if (reg.alliance != allianceId) continue;
                var go = ((Component)reg).gameObject;
                if (go == null) continue;
                int instanceId = go.GetInstanceID();
                var parentTransform = go.transform != null ? go.transform.parent : null;
                int parentInstanceId = 0;
                if (parentTransform != null)
                {
                    var parentReg = parentTransform.GetComponent<Regiment>();
                    if (parentReg != null) parentInstanceId = ((Component)parentReg).gameObject.GetInstanceID();
                }
                result.Add(new RegimentProbe(
                    instanceId: instanceId,
                    unittyp: reg.unittyp,
                    name: ((UnityEngine.Object)go).name,
                    active: IsOperationallyPresent(reg, go, commandHierarchyShift),
                    parentInstanceId: parentInstanceId,
                    isDirectChild: directChildren.Contains(instanceId)));
            }
            return result;
        }

        /// <summary>
        /// Same alliance walk as BuildProbesForAlliance, but emits richer probes
        /// (world X/Z, strength bucket) for TacticalCommandTreeProbe. Used by
        /// the role-cascade leaf-brigade map.
        /// </summary>
        public static IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> BuildExtendedProbesForAlliance(int allianceId)
        {
            try
            {
                int shift = ReadCommandHierarchyShift();
                var units = BattleUnits.completeunitlist as System.Collections.IList;
                if (units == null || units.Count == 0)
                    return Array.Empty<TacticalCommandTreeProbe.ExtendedProbe>();
                int effectiveCommandMin = ClampShiftedMin(shift);

                var directChildren = new HashSet<int>();
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null || reg.alliance != allianceId) continue;
                    var regGo = ((Component)reg).gameObject;
                    if (regGo == null) continue;
                    if (reg.unittyp < effectiveCommandMin) continue;
                    Regiment[] kids;
                    try { kids = reg.GetAttachedUnitsReg(true, true, -1, true, false, false, false, false); }
                    catch { kids = null; }
                    if (kids == null) continue;
                    for (int k = 0; k < kids.Length; k++)
                    {
                        var kid = kids[k];
                        if (kid == null) continue;
                        var kidGo = ((Component)kid).gameObject;
                        if (kidGo == null) continue;
                        directChildren.Add(kidGo.GetInstanceID());
                    }
                }

                var result = new List<TacticalCommandTreeProbe.ExtendedProbe>(units.Count);
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i] as Regiment;
                    if (reg == null || reg.alliance != allianceId) continue;
                    var go = ((Component)reg).gameObject;
                    if (go == null) continue;
                    int instanceId = go.GetInstanceID();
                    var parentTransform = go.transform != null ? go.transform.parent : null;
                    int parentInstanceId = 0;
                    if (parentTransform != null)
                    {
                        var parentReg = parentTransform.GetComponent<Regiment>();
                        if (parentReg != null) parentInstanceId = ((Component)parentReg).gameObject.GetInstanceID();
                    }
                    float worldX = 0f, worldZ = 0f;
                    try
                    {
                        var pos = go.transform != null ? go.transform.position : default(Vector3);
                        worldX = pos.x; worldZ = pos.z;
                    }
                    catch { }
                    int strengthBucket = SafeStrengthBucket(reg);
                    result.Add(new TacticalCommandTreeProbe.ExtendedProbe(
                        instanceId: instanceId,
                        unittyp: reg.unittyp,
                        name: ((UnityEngine.Object)go).name,
                        active: IsOperationallyPresent(reg, go, shift),
                        parentInstanceId: parentInstanceId,
                        isDirectChild: directChildren.Contains(instanceId),
                        worldX: worldX,
                        worldZ: worldZ,
                        strengthBucket: strengthBucket));
                }
                return result;
            }
            catch (Exception e)
            {
                OnceLog.Warning("o3-extended-probes:exception",
                    "DirectChildDiscovery.BuildExtendedProbesForAlliance failed: "
                    + e.GetType().Name + " " + e.Message);
                return Array.Empty<TacticalCommandTreeProbe.ExtendedProbe>();
            }
        }

        private static int SafeStrengthBucket(Regiment reg)
        {
            try
            {
                // Approximate strength bucket: 0 (weak), 1 (normal), 2 (strong).
                // Uses groupstrengthaigroup if available, else groupowninrange.
                float strength = 0f;
                try { strength = Math.Max(reg.groupowninrange, reg.groupstrengthaigroup); }
                catch { }
                if (strength >= 3000f) return 2;
                if (strength >= 1000f) return 1;
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsOperationallyPresent(Regiment reg, GameObject go, int commandHierarchyShift)
        {
            if (reg == null || go == null) return false;
            if (go.activeInHierarchy) return true;

            int effectiveCommandMin = ClampShiftedMin(commandHierarchyShift);
            if (reg.unittyp < effectiveCommandMin) return false;
            try
            {
                if (reg.isrouted || reg.markedforrout) return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;

        private static int ResolveAllianceFromBattle(AIBattle battle)
        {
            try
            {
                if (_sideOfAiField == null) _sideOfAiField = AccessTools.Field(typeof(AIBattle), "sideofai");
                if (_bunitsField == null) _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits");
                if (_sideOfAiField == null || _bunitsField == null) return -1;
                int side = (_sideOfAiField.GetValue(battle) is int s) ? s : -1;
                if (side != 0 && side != 1) return -1;
                var bunits = _bunitsField.GetValue(battle) as BattleUnits;
                if (bunits == null || bunits.alliance == null || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch
            {
                return -1;
            }
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
