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

        // Stage B perf cache (2026-05-21). The command tree structure (parent-child
        // relationships between brigades/divisions/corps + raw unittyp + alliance) is
        // STABLE between roster mutation events. BattleUnits.completeunitlist itself is
        // immutable during a battle (vanilla only mutates it at battle-init line 79980
        // per the decompile audit), and Regiment.alliance/unittyp don't change once a
        // unit is on the field. So we build the tree ONCE per (allianceId, shift) and
        // reuse on every heavy-gate fire. Saves ~8ms per side per fire — the largest
        // single per-cycle main-thread cost remaining after Stage A.
        //
        // Invalidation surface:
        //   - Battle end: TacticalBattleCoordinator.OnBattleEnd → Invalidate()
        //   - Shift change: detected on every Snapshot() call; rebuild on mismatch
        //   - Manual: Invalidate(allianceId) exposed for future roster-event hooks
        //     (Stage C — when a regiment dies or reinforcements arrive)
        //
        // Indexed by allianceId 0/1; index 2+ (Europe) is intentionally not cached
        // because the orchestrator has no role for alliance 2 anyway.
        private static readonly CommandTreeSnapshot[] _cachedSnapshots =
        {
            CommandTreeSnapshot.Empty,
            CommandTreeSnapshot.Empty
        };
        private static readonly int[] _cachedShifts = { int.MinValue, int.MinValue };
        private static readonly bool[] _cacheValid = { false, false };

        // Exposed counters for diagnostics — populated cheaply on hit/miss; emitted
        // periodically via TacticalCommandTreeCache events. Per-side so the dashboard
        // can show which side's cache is being effective.
        private static readonly long[] _cacheHits = { 0, 0 };
        private static readonly long[] _cacheMisses = { 0, 0 };

        public static CommandTreeSnapshot Snapshot(int allianceId)
        {
            return Snapshot(allianceId, ReadCommandHierarchyShift());
        }

        public static CommandTreeSnapshot Snapshot(int allianceId, int commandHierarchyShift)
        {
            // Cache check: only alliance 0/1 are cached. Skip cache for invalid alliance ids.
            if (allianceId >= 0 && allianceId < _cachedSnapshots.Length)
            {
                if (_cacheValid[allianceId] && _cachedShifts[allianceId] == commandHierarchyShift)
                {
                    _cacheHits[allianceId]++;
                    return _cachedSnapshots[allianceId];
                }
            }

            try
            {
                var probes = BuildProbes(allianceId, commandHierarchyShift);
                var snapshot = CommandTreeBuilder.Build(probes, allianceId, commandHierarchyShift);

                if (allianceId >= 0 && allianceId < _cachedSnapshots.Length)
                {
                    _cachedSnapshots[allianceId] = snapshot ?? CommandTreeSnapshot.Empty;
                    _cachedShifts[allianceId] = commandHierarchyShift;
                    _cacheValid[allianceId] = true;
                    _cacheMisses[allianceId]++;
                    OnceLog.Info(
                        "tactical-command-tree-cache:populated:" + allianceId,
                        "[TacticalCommandTreeCache] populated alliance=" + allianceId
                        + " shift=" + commandHierarchyShift
                        + " — subsequent reads served from cache");
                }
                return snapshot;
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-command-tree:snapshot-failed:" + allianceId,
                    "[TacticalCommandTree] snapshot failed side=" + allianceId
                    + ": " + e.GetType().Name + " " + e.Message);
                return CommandTreeSnapshot.Empty;
            }
        }

        // Invalidate one alliance's cache. Called when a roster event (death,
        // reinforcement arrival) might have changed the tree structure. Stage C
        // will wire automatic invalidation; for now this is exposed so external
        // hooks can call it manually.
        public static void Invalidate(int allianceId)
        {
            if (allianceId < 0 || allianceId >= _cachedSnapshots.Length) return;
            bool wasValid = _cacheValid[allianceId];
            _cacheValid[allianceId] = false;
            _cachedSnapshots[allianceId] = CommandTreeSnapshot.Empty;
            _cachedShifts[allianceId] = int.MinValue;
            if (wasValid)
            {
                OnceLog.Info(
                    "tactical-command-tree-cache:invalidated:" + allianceId,
                    "[TacticalCommandTreeCache] invalidated alliance=" + allianceId
                    + " — next Snapshot() will rebuild");
            }
        }

        // Invalidates all alliance caches. Called from TacticalBattleCoordinator.OnBattleEnd
        // so the next battle starts with a clean state. Counters reset for the next session.
        public static void InvalidateAll()
        {
            for (int i = 0; i < _cachedSnapshots.Length; i++)
            {
                _cacheValid[i] = false;
                _cachedSnapshots[i] = CommandTreeSnapshot.Empty;
                _cachedShifts[i] = int.MinValue;
                _cacheHits[i] = 0;
                _cacheMisses[i] = 0;
            }
        }

        // Diagnostic accessor — periodic dashboard emission can summarize hit/miss
        // rates without exposing internal arrays.
        public static long GetCacheHits(int allianceId)
        {
            return (allianceId >= 0 && allianceId < _cacheHits.Length) ? _cacheHits[allianceId] : 0;
        }

        public static long GetCacheMisses(int allianceId)
        {
            return (allianceId >= 0 && allianceId < _cacheMisses.Length) ? _cacheMisses[allianceId] : 0;
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
                    active: IsOperationallyPresent(reg, go, commandHierarchyShift),
                    routed: reg.isrouted,
                    markedForRout: reg.markedforrout));
            }

            return probes;
        }

        private static bool IsOperationallyPresent(Regiment reg, GameObject go, int commandHierarchyShift)
        {
            if (reg == null || go == null) return false;
            if (go.activeInHierarchy) return true;

            int effectiveCommandMin = ClampShiftedMin(commandHierarchyShift);
            if (reg.unittyp < effectiveCommandMin) return false;
            if (reg.isrouted || reg.markedforrout) return false;

            return true;
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
