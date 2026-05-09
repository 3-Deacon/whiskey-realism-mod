using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Vanilla-touching wrapper for DirectChildDiscovery. Reads
    /// AIBattle.unitsused via reflection, walks Regiment.GetAttachedUnitsReg
    /// (directonly: true) to flag direct-child relationships, then delegates
    /// to the pure DirectChildDiscovery.Probe.
    /// </summary>
    public static partial class DirectChildDiscovery
    {
        private static FieldInfo _unitsusedField;
        private static FieldInfo _commandHierarchyShiftField;

        public static IReadOnlyList<DirectChildSnapshot> Snapshot(AIBattle battle)
        {
            if (battle == null) return Array.Empty<DirectChildSnapshot>();
            try
            {
                var probes = BuildProbes(battle);
                int shift = ReadCommandHierarchyShift();
                return Probe(probes, shift);
            }
            catch (Exception e)
            {
                OnceLog.Warning("o3-direct-child-discovery:exception",
                    "DirectChildDiscovery.Snapshot failed: " + e.GetType().Name + " " + e.Message);
                return Array.Empty<DirectChildSnapshot>();
            }
        }

        private static IReadOnlyList<RegimentProbe> BuildProbes(AIBattle battle)
        {
            if (_unitsusedField == null) _unitsusedField = AccessTools.Field(typeof(AIBattle), "unitsused");
            if (_unitsusedField == null) return Array.Empty<RegimentProbe>();
            var raw = _unitsusedField.GetValue(battle) as System.Collections.IList;
            if (raw == null || raw.Count == 0) return Array.Empty<RegimentProbe>();

            // First pass: walk every command-level group's GetAttachedUnitsReg(directonly: true) and
            // collect instanceIds of children flagged as direct.
            var directChildren = new HashSet<int>();
            for (int i = 0; i < raw.Count; i++)
            {
                var reg = raw[i] as Regiment;
                if (reg == null) continue;
                var regGo = ((Component)reg).gameObject;
                if (regGo == null) continue;
                if (reg.unittyp <= TacticalUnitType.MaxCombat) continue;
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

            var result = new List<RegimentProbe>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var reg = raw[i] as Regiment;
                if (reg == null) continue;
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
                    active: go.activeInHierarchy,
                    parentInstanceId: parentInstanceId,
                    isDirectChild: directChildren.Contains(instanceId)));
            }
            return result;
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
