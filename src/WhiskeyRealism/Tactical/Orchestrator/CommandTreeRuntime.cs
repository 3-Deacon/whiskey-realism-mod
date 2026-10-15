using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Command-tree adapter over the side's current <see cref="BattleSnapshot"/>. The
    /// parent links come from the snapshot's single command-parent map
    /// (BattleSnapshotAssembler, plan D5); all tree construction lives in the pure
    /// CommandTreeBuilder. No vanilla reads of its own.
    /// </summary>
    internal static class CommandTreeRuntime
    {
        static CommandTreeRuntime()
        {
            BattleScope.Register("command-tree", InvalidateAll);
        }

        private static FieldInfo _commandHierarchyShiftField;

        // CommandTreeBuilder.Build allocates the node tree, so its result is cached per
        // alliance against a roster signature derived from the snapshot:
        //   - structural (the snapshot's parent-map generation, own unit count, id hash) or
        //     shift change: rebuild;
        //   - live change only (active / routed counts of command-level units): re-read
        //     those flags for the cached probes from the snapshot and re-run the builder.
        // Battle end: BattleScope -> InvalidateAll(). Alliance 2 (Europe) has no snapshot.
        private static readonly CommandTreeSnapshot[] _cachedSnapshots =
        {
            CommandTreeSnapshot.Empty,
            CommandTreeSnapshot.Empty
        };
        private static readonly int[] _cachedShifts = { int.MinValue, int.MinValue };
        private static readonly bool[] _cacheValid = { false, false };
        private static readonly TacticalRosterSignature[] _cachedRosters = new TacticalRosterSignature[2];
        private static readonly CommandTreeBuilder.CommandProbe[][] _cachedProbes = new CommandTreeBuilder.CommandProbe[2][];

        // Diagnostics, read by TacticalCacheValidator.
        private static readonly long[] _cacheHits = { 0, 0 };
        private static readonly long[] _cacheMisses = { 0, 0 };

        public static CommandTreeSnapshot Snapshot(int allianceId)
        {
            return Snapshot(allianceId, ReadCommandHierarchyShift());
        }

        public static CommandTreeSnapshot Snapshot(int allianceId, int commandHierarchyShift)
        {
            try
            {
                if (!BattleSnapshotBuilder.TryGetCurrentOrWarn(allianceId, out BattleSnapshot battle))
                    return CommandTreeSnapshot.Empty;

                var liveRoster = RosterSignatureOf(battle, commandHierarchyShift);
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
                    && TryRefreshLiveFlags(allianceId, battle, commandHierarchyShift, liveRoster, out CommandTreeSnapshot refreshed))
                {
                    return refreshed;
                }

                var probes = new List<CommandTreeBuilder.CommandProbe>(battle.OwnCount);
                BattleSnapshotAssembler.ToCommandProbes(battle, BattleSnapshotBuilder.NamesFor(allianceId), commandHierarchyShift, probes);
                var snapshot = CommandTreeBuilder.Build(probes, allianceId, commandHierarchyShift) ?? CommandTreeSnapshot.Empty;

                _cachedProbes[allianceId] = probes.ToArray();
                _cachedSnapshots[allianceId] = snapshot;
                _cachedShifts[allianceId] = commandHierarchyShift;
                _cachedRosters[allianceId] = liveRoster;
                _cacheValid[allianceId] = true;
                _cacheMisses[allianceId]++;
                OnceLog.Info(
                    "tactical-command-tree-cache:populated",
                    "[TacticalCommandTreeCache] populated alliance=" + allianceId
                    + " shift=" + commandHierarchyShift
                    + " — reads served from cache until the roster signature changes");
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

        // Same units and parent map as the cached build; only command-level active/routed
        // counts changed. Re-read those flags for the cached probes and re-run the builder.
        private static bool TryRefreshLiveFlags(int allianceId, BattleSnapshot battle, int shift, TacticalRosterSignature liveRoster, out CommandTreeSnapshot snapshot)
        {
            snapshot = null;
            var probes = _cachedProbes[allianceId];
            if (probes == null) return false;
            int commandMin = BattleSnapshotAssembler.ClampShiftedMin(shift);
            var refreshedProbes = new CommandTreeBuilder.CommandProbe[probes.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                int index = battle.IndexOfOwn(probes[i].InstanceId);
                if (index < 0) return false;
                refreshedProbes[i] = BattleSnapshotAssembler.ToCommandProbe(battle, index, probes[i].DisplayName, commandMin);
            }

            snapshot = CommandTreeBuilder.Build(refreshedProbes, allianceId, shift) ?? CommandTreeSnapshot.Empty;
            _cachedProbes[allianceId] = refreshedProbes;
            _cachedSnapshots[allianceId] = snapshot;
            _cachedRosters[allianceId] = liveRoster;
            _cacheHits[allianceId]++;
            return true;
        }

        // Invalidate one alliance's cache; the next Snapshot() rebuilds.
        public static void Invalidate(int allianceId)
        {
            if (allianceId < 0 || allianceId >= _cachedSnapshots.Length) return;
            bool wasValid = _cacheValid[allianceId];
            _cacheValid[allianceId] = false;
            _cachedSnapshots[allianceId] = CommandTreeSnapshot.Empty;
            _cachedShifts[allianceId] = int.MinValue;
            _cachedRosters[allianceId] = default(TacticalRosterSignature);
            _cachedProbes[allianceId] = null;
            if (wasValid)
            {
                OnceLog.Info(
                    "tactical-command-tree-cache:invalidated",
                    "[TacticalCommandTreeCache] invalidated alliance=" + allianceId
                    + " — next Snapshot() will rebuild");
            }
        }

        // Invalidates all alliance caches at battle end (BattleScope); counters reset.
        public static void InvalidateAll()
        {
            for (int i = 0; i < _cachedSnapshots.Length; i++)
            {
                _cacheValid[i] = false;
                _cachedSnapshots[i] = CommandTreeSnapshot.Empty;
                _cachedShifts[i] = int.MinValue;
                _cachedRosters[i] = default(TacticalRosterSignature);
                _cachedProbes[i] = null;
                _cacheHits[i] = 0;
                _cacheMisses[i] = 0;
            }
        }

        // Signature of the side's current snapshot (default when none was built yet).
        internal static TacticalRosterSignature ReadRosterSignature(int allianceId)
        {
            return BattleSnapshotBuilder.TryGetCurrent(allianceId, out BattleSnapshot battle)
                ? RosterSignatureOf(battle, ReadCommandHierarchyShift())
                : default(TacticalRosterSignature);
        }

        /// <summary>
        /// Structural part: the snapshot's parent-map generation (bumps whenever the
        /// assembler re-read allattachedunits), own unit count and an order-independent id
        /// hash. Live part: active / routed counts over command-level units, the only ones
        /// CommandTreeBuilder keeps. markedforrout never applies to a group (Rout returns
        /// for unittyp > 13, 123547), so its count stays 0.
        /// </summary>
        private static TacticalRosterSignature RosterSignatureOf(BattleSnapshot battle, int commandHierarchyShift)
        {
            int commandMin = BattleSnapshotAssembler.ClampShiftedMin(commandHierarchyShift);
            int active = 0, routed = 0, idHash = 0;
            for (int i = 0; i < battle.OwnCount; i++)
            {
                ref UnitRecord u = ref battle.Own[i];
                unchecked { idHash += u.InstanceId * 16777619; }
                if (u.UnitTyp < commandMin) continue;
                if ((u.Flags & UnitFlags.Routed) != 0) routed++;
                if ((u.Flags & UnitFlags.Active) != 0) active++;
            }
            return new TacticalRosterSignature(battle.TreeGeneration, battle.OwnCount, active, routed, 0, idHash);
        }

        internal static bool TryGetCachedRosterSignature(int allianceId, out TacticalRosterSignature roster)
        {
            roster = default(TacticalRosterSignature);
            if (allianceId < 0 || allianceId >= _cachedRosters.Length || !_cacheValid[allianceId]) return false;
            roster = _cachedRosters[allianceId];
            return true;
        }

        public static long GetCacheHits(int allianceId)
        {
            return (allianceId >= 0 && allianceId < _cacheHits.Length) ? _cacheHits[allianceId] : 0;
        }

        public static long GetCacheMisses(int allianceId)
        {
            return (allianceId >= 0 && allianceId < _cacheMisses.Length) ? _cacheMisses[allianceId] : 0;
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
    }
}
