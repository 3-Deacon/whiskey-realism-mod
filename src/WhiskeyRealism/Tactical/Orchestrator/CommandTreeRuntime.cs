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

        // Stage B perf cache (2026-05-21), corrected 2026-10-14 (BR-H5). The tree
        // structure (command parent links, raw unittyp, alliance) only changes on roster
        // events, but completeunitlist is NOT immutable mid-battle: UpdateUnitList
        // (79974) replaces it from CreateUnitDefault (89670), DetachUnits (90239) and
        // ReattachCompany (90609), and each probe's Active/Routed/MarkedForRout flag is
        // live vanilla state (routs, reinforcement arrivals). Every Snapshot() therefore
        // reads a cheap roster signature (one pass over completeunitlist, plain field
        // reads; no names, no GetAttachedUnitsReg):
        //   - structural change (list identity, unit count, id set; incl. a new battle's
        //     new list) or shift change: full rebuild (hierarchy walk + names);
        //   - live-flag change only (active / routed / marked counts of command-level
        //     units, the only ones CommandTreeBuilder keeps): re-read those flags for the
        //     cached probes and re-run the pure builder — no hierarchy walk, no names.
        // A cache hit thus always reflects the live routed/active state.
        //
        // Invalidation surface:
        //   - Structural roster change / shift change: rebuild
        //   - Live-flag change: refresh
        //   - Battle end: TacticalBattleCoordinator.OnBattleEnd -> InvalidateAll()
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
        private static readonly TacticalRosterSignature[] _cachedRosters = new TacticalRosterSignature[2];
        private static readonly CommandTreeBuilder.CommandProbe[][] _cachedProbes = new CommandTreeBuilder.CommandProbe[2][];
        private static readonly Regiment[][] _cachedProbeUnits = new Regiment[2][];

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
            bool cacheable = allianceId >= 0 && allianceId < _cachedSnapshots.Length;
            TacticalRosterSignature liveRoster = default(TacticalRosterSignature);
            if (cacheable)
            {
                liveRoster = ReadRosterSignature(allianceId, commandHierarchyShift);
                var refresh = TacticalCacheSignatures.ClassifyCommandTreeRefresh(
                    _cacheValid[allianceId],
                    _cachedShifts[allianceId],
                    commandHierarchyShift,
                    _cachedRosters[allianceId],
                    liveRoster);
                if (refresh == CommandTreeRefresh.Hit)
                {
                    _cacheHits[allianceId]++;
                    return _cachedSnapshots[allianceId];
                }
                if (refresh == CommandTreeRefresh.RefreshLiveFlags
                    && TryRefreshLiveFlags(allianceId, commandHierarchyShift, liveRoster, out CommandTreeSnapshot refreshed))
                {
                    return refreshed;
                }
            }

            try
            {
                var units = new List<Regiment>();
                var probes = BuildProbes(allianceId, commandHierarchyShift, units);
                var snapshot = CommandTreeBuilder.Build(probes, allianceId, commandHierarchyShift);

                if (cacheable)
                {
                    _cachedProbes[allianceId] = ToArray(probes);
                    _cachedProbeUnits[allianceId] = units.ToArray();
                    _cachedSnapshots[allianceId] = snapshot ?? CommandTreeSnapshot.Empty;
                    _cachedShifts[allianceId] = commandHierarchyShift;
                    _cachedRosters[allianceId] = liveRoster;
                    _cacheValid[allianceId] = true;
                    _cacheMisses[allianceId]++;
                    OnceLog.Info(
                        "tactical-command-tree-cache:populated",
                        "[TacticalCommandTreeCache] populated alliance=" + allianceId
                        + " shift=" + commandHierarchyShift
                        + " — reads served from cache until the roster signature changes");
                }
                return snapshot;
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-command-tree:snapshot-failed",
                    "[TacticalCommandTree] snapshot failed side=" + allianceId
                    + ": " + e.GetType().Name + " " + e.Message);
                return CommandTreeSnapshot.Empty;
            }
        }

        // Same units as the cached build; only active/routed/marked changed. Re-read those
        // flags for the cached probes and re-run the pure builder.
        private static bool TryRefreshLiveFlags(int allianceId, int shift, TacticalRosterSignature liveRoster, out CommandTreeSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                var probes = _cachedProbes[allianceId];
                var units = _cachedProbeUnits[allianceId];
                if (probes == null || units == null || probes.Length != units.Length) return false;
                var refreshedProbes = new CommandTreeBuilder.CommandProbe[probes.Length];
                for (int i = 0; i < probes.Length; i++)
                {
                    var p = probes[i];
                    var reg = units[i];
                    if (reg == null) return false;
                    refreshedProbes[i] = new CommandTreeBuilder.CommandProbe(
                        p.InstanceId,
                        p.ParentInstanceId,
                        p.AllianceId,
                        p.RawUnitTyp,
                        p.DisplayName,
                        IsOperationallyPresent(reg, reg.gameObject, shift),
                        reg.isrouted,
                        reg.markedforrout);
                }

                snapshot = CommandTreeBuilder.Build(refreshedProbes, allianceId, shift) ?? CommandTreeSnapshot.Empty;
                _cachedProbes[allianceId] = refreshedProbes;
                _cachedSnapshots[allianceId] = snapshot;
                _cachedRosters[allianceId] = liveRoster;
                _cacheHits[allianceId]++;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static CommandTreeBuilder.CommandProbe[] ToArray(IReadOnlyList<CommandTreeBuilder.CommandProbe> probes)
        {
            var array = new CommandTreeBuilder.CommandProbe[probes.Count];
            for (int i = 0; i < probes.Count; i++) array[i] = probes[i];
            return array;
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
            _cachedRosters[allianceId] = default(TacticalRosterSignature);
            _cachedProbes[allianceId] = null;
            _cachedProbeUnits[allianceId] = null;
            if (wasValid)
            {
                OnceLog.Info(
                    "tactical-command-tree-cache:invalidated",
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
                _cachedRosters[i] = default(TacticalRosterSignature);
                _cachedProbes[i] = null;
                _cachedProbeUnits[i] = null;
                _cacheHits[i] = 0;
                _cacheMisses[i] = 0;
            }
        }

        internal static TacticalRosterSignature ReadRosterSignature(int allianceId)
        {
            return ReadRosterSignature(allianceId, ReadCommandHierarchyShift());
        }

        /// <summary>
        /// One pass over completeunitlist for the alliance: list identity, unit count and
        /// an order-independent id hash (structural), plus activeInHierarchy / isrouted /
        /// markedforrout counts over command-level units only (live; leaf routs do not
        /// change the command tree). Plain field reads only.
        /// </summary>
        private static TacticalRosterSignature ReadRosterSignature(int allianceId, int commandHierarchyShift)
        {
            try
            {
                var units = BattleUnits.completeunitlist;
                if (units == null) return default(TacticalRosterSignature);
                int commandMin = ClampShiftedMin(commandHierarchyShift);
                int count = 0, active = 0, routed = 0, marked = 0, idHash = 0;
                for (int i = 0; i < units.Count; i++)
                {
                    var reg = units[i];
                    if (reg == null || reg.alliance != allianceId) continue;
                    count++;
                    unchecked { idHash += reg.GetInstanceID() * 16777619; }
                    if (reg.unittyp < commandMin) continue;
                    if (reg.isrouted) routed++;
                    if (reg.markedforrout) marked++;
                    if (reg.gameObject.activeInHierarchy) active++;
                }
                return new TacticalRosterSignature(
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(units),
                    count, active, routed, marked, idHash);
            }
            catch
            {
                return default(TacticalRosterSignature);
            }
        }

        internal static bool TryGetCachedRosterSignature(int allianceId, out TacticalRosterSignature roster)
        {
            roster = default(TacticalRosterSignature);
            if (allianceId < 0 || allianceId >= _cachedRosters.Length || !_cacheValid[allianceId]) return false;
            roster = _cachedRosters[allianceId];
            return true;
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

        private static IReadOnlyList<CommandTreeBuilder.CommandProbe> BuildProbes(int allianceId, int commandHierarchyShift, List<Regiment> probeUnits)
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

                probeUnits.Add(reg);
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
