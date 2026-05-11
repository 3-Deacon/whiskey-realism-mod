using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Vanilla-touching command-tree snapshot adapter. Reads BattleUnits and
    /// Regiment hierarchy state only; all tree construction lives in the pure
    /// CommandTreeBuilder.
    /// </summary>
    internal static class CommandTreeRuntime
    {
        private static FieldInfo _commandHierarchyShiftField;

        public static CommandTreeSnapshot Snapshot(int allianceId)
        {
            return Snapshot(allianceId, ReadCommandHierarchyShift());
        }

        public static CommandTreeSnapshot Snapshot(int allianceId, int commandHierarchyShift)
        {
            try
            {
                var probes = BuildProbes(allianceId, commandHierarchyShift);
                return CommandTreeBuilder.Build(probes, allianceId, commandHierarchyShift);
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-command-tree:snapshot-failed:" + allianceId,
                    "[TacticalCommandTree] snapshot failed side=" + allianceId
                    + ": " + e.GetType().Name + " " + e.Message);
                return CommandTreeSnapshot.Empty;
            }
        }

        private static IReadOnlyList<CommandTreeBuilder.CommandProbe> BuildProbes(int allianceId, int commandHierarchyShift)
        {
            var units = BattleUnits.completeunitlist as System.Collections.IList;
            if (units == null || units.Count == 0)
            {
                return Array.Empty<CommandTreeBuilder.CommandProbe>();
            }

            var attachedParentByChild = BuildAttachedParentMap(units, allianceId, commandHierarchyShift);
            var probes = new List<CommandTreeBuilder.CommandProbe>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                var reg = units[i] as Regiment;
                if (reg == null || reg.alliance != allianceId)
                {
                    continue;
                }

                var go = ((Component)reg).gameObject;
                if (go == null)
                {
                    continue;
                }

                int instanceId = go.GetInstanceID();
                int parentInstanceId;
                if (!attachedParentByChild.TryGetValue(instanceId, out parentInstanceId))
                {
                    parentInstanceId = ResolveParentInstanceId(reg);
                }

                probes.Add(new CommandTreeBuilder.CommandProbe(
                    instanceId: instanceId,
                    parentInstanceId: parentInstanceId,
                    allianceId: reg.alliance,
                    rawUnitTyp: reg.unittyp,
                    displayName: ((UnityEngine.Object)go).name,
                    active: go.activeInHierarchy,
                    routed: reg.isrouted,
                    markedForRout: reg.markedforrout));
            }

            return probes;
        }

        private static Dictionary<int, int> BuildAttachedParentMap(System.Collections.IList units, int allianceId, int commandHierarchyShift)
        {
            int effectiveCommandMin = ClampShiftedMin(commandHierarchyShift);
            var parentByChild = new Dictionary<int, int>();
            for (int i = 0; i < units.Count; i++)
            {
                var parent = units[i] as Regiment;
                if (parent == null || parent.alliance != allianceId)
                {
                    continue;
                }

                var parentGo = ((Component)parent).gameObject;
                if (parentGo == null || parent.unittyp < effectiveCommandMin)
                {
                    continue;
                }

                Regiment[] children;
                try
                {
                    children = parent.GetAttachedUnitsReg(
                        excludedechainedunits: true,
                        excludeskirmishers: true,
                        searchonlytype: -1,
                        directonly: true,
                        includenonactiveunits: false,
                        includebasicgarrisons: false,
                        undersamecampaignunitonly: false,
                        sortbytype: false);
                }
                catch
                {
                    children = null;
                }

                if (children == null)
                {
                    continue;
                }

                int parentInstanceId = parentGo.GetInstanceID();
                for (int c = 0; c < children.Length; c++)
                {
                    var child = children[c];
                    if (child == null || child.alliance != allianceId)
                    {
                        continue;
                    }

                    var childGo = ((Component)child).gameObject;
                    if (childGo == null)
                    {
                        continue;
                    }

                    parentByChild[childGo.GetInstanceID()] = parentInstanceId;
                }
            }

            return parentByChild;
        }

        private static int ResolveParentInstanceId(Regiment reg)
        {
            try
            {
                if (reg.parentregiment != null)
                {
                    var parentReg = reg.parentregiment.GetComponent<Regiment>();
                    if (parentReg != null)
                    {
                        return ((Component)parentReg).gameObject.GetInstanceID();
                    }
                }

                var go = ((Component)reg).gameObject;
                var parentTransform = go != null && go.transform != null ? go.transform.parent : null;
                if (parentTransform == null)
                {
                    return 0;
                }

                var transformParentReg = parentTransform.GetComponent<Regiment>();
                if (transformParentReg == null)
                {
                    return 0;
                }

                return ((Component)transformParentReg).gameObject.GetInstanceID();
            }
            catch
            {
                return 0;
            }
        }

        private static int ReadCommandHierarchyShift()
        {
            try
            {
                if (_commandHierarchyShiftField == null)
                {
                    _commandHierarchyShiftField = AccessTools.Field(typeof(GamePrefs), "commandhierarchyshift");
                }

                if (_commandHierarchyShiftField == null)
                {
                    return 0;
                }

                var value = _commandHierarchyShiftField.GetValue(null);
                return value is int shift ? shift : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static int ClampShiftedMin(int shift)
        {
            int min = TacticalUnitType.MaxCombat + 1 + shift;
            if (min < 1) return 1;
            if (min > 18) return 18;
            return min;
        }
    }
}
