using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime half of the battle snapshot (BC1). One pass over BattleUnits.completeunitlist
    // per build fills the pooled BattleSnapshot: own units of the alliance into Own, every
    // other unit into EnemyRecords (true state, for BC2 perception only). Runs on the main
    // thread inside the coordinator tick (TacticalObserverPatch on CheckGlobalAIStrategy,
    // UpdateAITasks 5866), so every live movement field it captures (regimentpaths,
    // lastsetwaypointposition, formation, rotation, position) is from the same frame the
    // doctrine and the arbiter (AdjustGroupFormations 5870) act in.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   - BattleUnits.completeunitlist (78920), replaced by UpdateUnitList (79974).
    //   - Regiment.GetPosition (140398): transform position cached per GameVars.frame.
    //   - Regiment.allattachedunits (110988), filled by UpdateAllAttachedUnits
    //     (129572-129615) with GetComponentsInChildren(true) — self included. Its Length
    //     is summed every build (roster signature); the ids are read only when the roster
    //     signature changes (BattleSnapshotAssembler, plan D5).
    //   - Regiment.unitrange.enemyinrangereg (109450), assigned from the unsorted temp list
    //     at the end of UpdateUnitRangeFast (122840); [0] is range-only, not nearest.
    //   - Regiment.ammo (111498) through ArmyEvidenceBuilder.NormalizeAmmo01 (T1 rule).
    //   - groupowninrange (110894) / groupstrengthaigroup (110844), written by
    //     UpdateGroupStrength (120297, 120559-120564): the command-tree strength bucket.
    //   - SetWaypoint's garrison exit (91331): Garrisoned = garrisonreference != null.
    //   - W&L ownership inputs: dlcw_isundercommander (110360, set by
    //     BattleUnits.IsUnitUnderCommander 91168-91222), DLC_WL.IsUnitTakenOver (45840),
    //     ai_feudstance (111248), permanentlydetached (111222; deployment detach
    //     85318-85336 sets it on the player's command and, outside ai_vs_ai, on every
    //     player-alliance unit). PlayerOwnership.Compute turns them into PlayerOwned /
    //     Feud per unit, and ApplyChainFlags marks the player's command and its
    //     ancestors once the parent map is assembled.
    //   - Vanilla's own ancestry test for the player's command is Transform.IsChildOf
    //     (CheckCurrentOrderUpdate 8272, IsUnitUnderCommander 91203); the parent map is
    //     compared against it for the player chain only and disagreements are counted.
    internal static class BattleSnapshotBuilder
    {
        static BattleSnapshotBuilder()
        {
            BattleScope.Register("battle-snapshot", ResetForBattleEnd);
        }

        private const int Sides = 2;

        private static readonly BattleSnapshot[] _current = new BattleSnapshot[Sides];
        private static readonly BattleSnapshotScratch[] _scratch = { new BattleSnapshotScratch(), new BattleSnapshotScratch() };
        private static readonly TacticalFrontAxis[] _lastFrontAxis = new TacticalFrontAxis[Sides];
        private static readonly int[] _lastPlayerCommandId = new int[Sides];

        // Live units aligned with the last built snapshot's Own / EnemyRecords.
        private static readonly Regiment[][] _ownUnits = { new Regiment[BattleSnapshot.InitialCapacity], new Regiment[BattleSnapshot.InitialCapacity] };
        private static readonly Regiment[][] _enemyUnits = { new Regiment[BattleSnapshot.InitialCapacity], new Regiment[BattleSnapshot.InitialCapacity] };
        private static readonly int[][][] _attachedIds = { new int[BattleSnapshot.InitialCapacity][], new int[BattleSnapshot.InitialCapacity][] };
        private static readonly int[][] _attachedCounts = { new int[BattleSnapshot.InitialCapacity], new int[BattleSnapshot.InitialCapacity] };

        private static FieldInfo _bunitsField;
        private static bool _bunitsFieldResolved;

        internal static BattleSnapshot Build(AIBattle battle, int alliance, BattleSnapshot reuse, long tickId, double doctrineHours)
        {
            using (TelemetryPerf.Scope("tactical.snapshot.build", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                var snapshot = reuse ?? new BattleSnapshot();
                bool slot = alliance >= 0 && alliance < Sides;
                try
                {
                    if (!slot) snapshot.BeginBuild(0, alliance, -1, tickId, doctrineHours);
                    else BuildInto(battle, alliance, snapshot, tickId, doctrineHours);
                }
                catch (Exception e)
                {
                    snapshot.BeginBuild(0, alliance, -1, tickId, doctrineHours);
                    OnceLog.Warning("tactical-battle-core:snapshot-degraded",
                        "[BattleSnapshot] build failed, empty snapshot: " + e.GetType().Name + " " + e.Message);
                }
                if (slot) _current[alliance] = snapshot;
                return snapshot;
            }
        }

        // Last snapshot built for the alliance this battle. BattleCore.TryGetSnapshot (BC4)
        // reads this slot; nothing else keeps a second registry.
        internal static bool TryGetCurrent(int alliance, out BattleSnapshot snapshot)
        {
            snapshot = alliance >= 0 && alliance < Sides ? _current[alliance] : null;
            return snapshot != null;
        }

        // For readers that need the snapshot this tick (command tree, direct children):
        // a miss means nothing built a snapshot for the side, which is a wiring gap, so it
        // is warned once instead of being papered over with a second build.
        internal static bool TryGetCurrentOrWarn(int alliance, out BattleSnapshot snapshot)
        {
            if (TryGetCurrent(alliance, out snapshot)) return true;
            if (alliance >= 0 && alliance < Sides)
                OnceLog.Warning("tactical-battle-core:snapshot-missing",
                    "[BattleSnapshot] no snapshot built for a side; command tree and direct children stay empty");
            return false;
        }

        // Unit names of the alliance's roster, refreshed only when the roster changes.
        internal static IReadOnlyDictionary<int, string> NamesFor(int alliance)
        {
            return alliance >= 0 && alliance < Sides ? _scratch[alliance].NameById : null;
        }

        // The live unit behind a record of the alliance's current snapshot (index into
        // Own, or into EnemyRecords when enemy is true); null when out of range.
        internal static Regiment UnitAt(BattleSnapshot snapshot, int index, bool enemy)
        {
            if (snapshot == null) return null;
            int alliance = snapshot.Alliance;
            if (alliance < 0 || alliance >= Sides || !ReferenceEquals(_current[alliance], snapshot)) return null;
            int count = enemy ? snapshot.EnemyRecordCount : snapshot.OwnCount;
            var units = enemy ? _enemyUnits[alliance] : _ownUnits[alliance];
            return index >= 0 && index < count && index < units.Length ? units[index] : null;
        }

        private static void BuildInto(AIBattle battle, int alliance, BattleSnapshot snapshot, long tickId, double doctrineHours)
        {
            var scratch = _scratch[alliance];
            var bunits = ResolveBattleUnits(battle);
            var units = BattleUnits.completeunitlist;
            if (bunits == null || units == null)
            {
                snapshot.BeginBuild(0, alliance, -1, tickId, doctrineHours);
                snapshot.IndexCommandTree(scratch);
                OnceLog.Warning("tactical-battle-core:snapshot-degraded",
                    "[BattleSnapshot] no BattleUnits or completeunitlist; empty snapshot");
                return;
            }

            snapshot.BeginBuild(bunits.GetInstanceID(), alliance, SideOf(bunits, alliance), tickId, doctrineHours);
            snapshot.SetSliceCount(GamePrefs.aidefensiveslices);
            scratch.SourceListIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(units);

            // One ownership context per build; the same one TacticalWriteGuard's Regiment
            // overloads read, so snapshot flags and direct-writer checks cannot disagree.
            PlayerOwnershipInput ownership = TacticalWriteGuard.ReadOwnershipInput();

            var ownUnits = _ownUnits[alliance];
            var enemyUnits = _enemyUnits[alliance];
            int attachedLengthSum = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var reg = units[i];
                if (reg == null) continue;
                var go = reg.gameObject;
                if (go == null) continue;
                var record = Capture(reg, go, in ownership);
                if (reg.alliance == alliance)
                {
                    if (snapshot.OwnCount == ownUnits.Length) _ownUnits[alliance] = ownUnits = Grow(ownUnits);
                    ownUnits[snapshot.OwnCount] = reg;
                    snapshot.AddOwn(in record);
                    if ((record.Flags & UnitFlags.IsGroup) != 0)
                    {
                        var attached = reg.allattachedunits;
                        if (attached != null) attachedLengthSum += attached.Length;
                    }
                }
                else
                {
                    if (snapshot.EnemyRecordCount == enemyUnits.Length) _enemyUnits[alliance] = enemyUnits = Grow(enemyUnits);
                    enemyUnits[snapshot.EnemyRecordCount] = reg;
                    snapshot.AddEnemy(in record);
                }
            }

            int playerCommandId = ownership.WlActive ? PlayerCommandId(alliance) : 0;
            if (playerCommandId != 0) snapshot.SetPlayerCommand(snapshot.IndexOfOwn(playerCommandId));

            scratch.AttachedLengthSum = attachedLengthSum;
            int count = snapshot.OwnCount;
            if (BattleSnapshotAssembler.RosterChanged(snapshot.Own, count, scratch))
                RefreshAttachedAndNames(alliance, snapshot, scratch);
            int generation = scratch.TreeGeneration;
            BattleSnapshotAssembler.AssembleCommandTree(snapshot.Own, count, _attachedIds[alliance], _attachedCounts[alliance], scratch);
            // Chain flags walk ParentIndex, so they follow the tree just assembled.
            PlayerOwnership.ApplyChainFlags(snapshot, in ownership);
            if (generation != scratch.TreeGeneration || playerCommandId != _lastPlayerCommandId[alliance])
            {
                _lastPlayerCommandId[alliance] = playerCommandId;
                scratch.AncestryMismatchCount = CountAncestryMismatches(snapshot, ownUnits);
                // "tactical" key prefix: OnceLog.ResetTactical releases it at battle end, so
                // the line fires once per battle.
                if (scratch.AncestryMismatchCount > 0)
                    OnceLog.Info("tactical-battle-core:ancestry-mismatch",
                        "[BattleSnapshot] parent map and Transform.IsChildOf disagree on the player's chain: alliance="
                        + alliance + " groups=" + scratch.AncestryMismatchCount);
            }
            snapshot.SetAncestryMismatchCount(scratch.AncestryMismatchCount);
            snapshot.IndexCommandTree(scratch);

            RefitFrontAxis(snapshot);
        }

        // Fits the snapshot's front axis against the alliance's last fitted axis (the
        // Stabilize reference) and records it as the new reference. Build calls it before
        // perception, so the axis keeps the last known heading; once BC2 has set the
        // visible-enemy summary for the tick, call it again so the heading follows what
        // is seen and the next tick stabilizes against that.
        internal static void RefitFrontAxis(BattleSnapshot snapshot)
        {
            int alliance = snapshot != null ? snapshot.Alliance : -1;
            if (alliance < 0 || alliance >= Sides) return;
            var axis = BattleSnapshotAssembler.FitFrontAxis(snapshot, _lastFrontAxis[alliance], GamePrefs.commandhierarchyshift);
            _lastFrontAxis[alliance] = axis;
            snapshot.SetFrontAxis(axis);
        }

        private static UnitRecord Capture(Regiment reg, GameObject go, in PlayerOwnershipInput ownership)
        {
            var record = new UnitRecord();
            record.InstanceId = go.GetInstanceID();
            record.UnitTyp = reg.unittyp;
            record.Alliance = reg.alliance;
            record.Commander = reg.commander;
            Vector3 p = reg.GetPosition();
            record.X = p.x;
            record.Y = p.y;
            record.Z = p.z;
            record.RotationY = go.transform.eulerAngles.y;
            record.Strength = reg.strength;
            record.GroupStrengthDirect = reg.groupstrengthdirect;
            record.GroupOwnInRange = reg.groupowninrange;
            record.GroupStrengthAiGroup = reg.groupstrengthaigroup;
            record.Morale = reg.morale;
            record.BattleStartMorale = reg.battlestartmorale;
            record.Ammo01 = ArmyEvidenceBuilder.NormalizeAmmo01(reg.ammo, reg.unittyp, 0.9f);
            record.Fatigue = reg.fatigue;
            record.Experience = reg.experience;
            record.RegimentPaths = reg.regimentpaths;
            record.QueuedOrders = reg.orderqueue != null ? reg.orderqueue.Count : 0;
            record.MovementMode = reg.movementmode;
            record.AiStance = reg.ai_stance;
            record.AiStanceOrdered = reg.ai_stanceordered;
            record.Formation = reg.formation;
            record.CombatBehaviorOrdered = reg.combatbehaviorordered;
            record.LastWaypointX = reg.lastsetwaypointposition.x;
            record.LastWaypointZ = reg.lastsetwaypointposition.z;
            record.ParentIndex = -1;
            record.FirstEnemyInRangeId = FirstEnemyInRangeId(reg);

            bool group = reg.unittyp > TacticalUnitType.MaxCombat;
            var flags = UnitFlags.None;
            if (go.activeInHierarchy) flags |= UnitFlags.Active;
            if (group || reg.strength > 0) flags |= UnitFlags.Alive; // a group lives while it exists
            if (reg.isrouted) flags |= UnitFlags.Routed;
            if (reg.markedforrout) flags |= UnitFlags.MarkedForRout;
            if (reg.onretreat) flags |= UnitFlags.OnRetreat;
            if (reg.inmelee > 0f) flags |= UnitFlags.InMelee;
            if (reg.liedown) flags |= UnitFlags.LiedDown;
            if (reg.permanentlydetached) flags |= UnitFlags.PermanentlyDetached;
            if (reg.garrisonreference != null) flags |= UnitFlags.Garrisoned;
            if (group) flags |= UnitFlags.IsGroup;
            else if (reg.unittyp == TacticalUnitType.Infantry) flags |= UnitFlags.IsInfantry;
            else if (reg.unittyp == TacticalUnitType.Cavalry) flags |= UnitFlags.IsCavalry;
            else if (reg.unittyp == TacticalUnitType.Artillery) flags |= UnitFlags.IsArtillery;
            flags |= TacticalWriteGuard.OwnershipFlags(reg, in ownership);
            record.Flags = flags;
            return record;
        }

        private static int FirstEnemyInRangeId(Regiment reg)
        {
            var range = reg.unitrange;
            var list = range != null ? range.enemyinrangereg : null;
            if (list == null || list.Count == 0) return 0;
            var first = list[0];
            if (first == null) return 0;
            var go = first.gameObject;
            return go != null ? go.GetInstanceID() : 0;
        }

        // Re-reads allattachedunits ids of every own group, and names of own units not seen
        // before. Only on a roster change: Unity's name getter allocates.
        private static void RefreshAttachedAndNames(int alliance, BattleSnapshot snapshot, BattleSnapshotScratch scratch)
        {
            int count = snapshot.OwnCount;
            if (_attachedIds[alliance].Length < count)
            {
                Array.Resize(ref _attachedIds[alliance], Math.Max(count, _attachedIds[alliance].Length * 2));
                Array.Resize(ref _attachedCounts[alliance], _attachedIds[alliance].Length);
            }
            var ids = _attachedIds[alliance];
            var counts = _attachedCounts[alliance];
            var units = _ownUnits[alliance];
            var names = scratch.NameById;
            for (int i = 0; i < count; i++)
            {
                counts[i] = 0;
                var reg = units[i];
                if (reg == null) continue;
                int id = snapshot.Own[i].InstanceId;
                if (!names.ContainsKey(id)) names[id] = reg.gameObject.name;
                if ((snapshot.Own[i].Flags & UnitFlags.IsGroup) == 0) continue;
                var attached = reg.allattachedunits;
                if (attached == null) continue;
                if (ids[i] == null || ids[i].Length < attached.Length) ids[i] = new int[Math.Max(attached.Length, 8)];
                int n = 0;
                for (int k = 0; k < attached.Length; k++)
                {
                    var unit = attached[k];
                    if (unit == null) continue;
                    var go = unit.gameObject;
                    if (go == null) continue;
                    ids[i][n++] = go.GetInstanceID();
                }
                counts[i] = n;
            }
        }

        private static int CountAncestryMismatches(BattleSnapshot snapshot, Regiment[] ownUnits)
        {
            int player = snapshot.PlayerCommandIndex;
            if (player < 0) return 0;
            var playerTransform = ownUnits[player] != null ? ownUnits[player].transform : null;
            if (playerTransform == null) return 0;
            int mismatches = 0;
            for (int g = 0; g < snapshot.OwnCount; g++)
            {
                if (g == player || (snapshot.Own[g].Flags & UnitFlags.IsGroup) == 0) continue;
                var group = ownUnits[g];
                if (group == null) continue;
                bool vanilla = playerTransform.IsChildOf(group.transform);
                if (vanilla != snapshot.IsAncestorOf(g, player)) mismatches++;
            }
            return mismatches;
        }

        private static int PlayerCommandId(int alliance)
        {
            int chosen = DLC_WL.dlc_chosencommander;
            var commanders = GameVars.commander;
            if (chosen < 0 || commanders == null || chosen >= commanders.Count || commanders[chosen] == null) return 0;
            var command = commanders[chosen].currentcommand;
            if (command == null || command.alliance != alliance) return 0;
            var go = command.gameObject;
            return go != null ? go.GetInstanceID() : 0;
        }

        private static int SideOf(BattleUnits bunits, int alliance)
        {
            var alliances = bunits.alliance;
            if (alliances == null) return -1;
            for (int s = 0; s < alliances.Length; s++)
                if (alliances[s] == alliance) return s;
            return -1;
        }

        // AIBattle.bunits is a private static field (decompile 3306), resolved once.
        private static BattleUnits ResolveBattleUnits(AIBattle battle)
        {
            if (!_bunitsFieldResolved)
            {
                _bunitsFieldResolved = true;
                try { _bunitsField = AccessTools.Field(typeof(AIBattle), "bunits"); }
                catch { _bunitsField = null; }
                if (_bunitsField == null)
                    OnceLog.Warning("tactical-battle-core:bunits-field", "[BattleSnapshot] AIBattle.bunits not found; snapshots stay empty");
            }
            return _bunitsField != null ? _bunitsField.GetValue(battle) as BattleUnits : null;
        }

        private static Regiment[] Grow(Regiment[] units)
        {
            Array.Resize(ref units, units.Length * 2);
            return units;
        }

        private static void ResetForBattleEnd()
        {
            for (int a = 0; a < Sides; a++)
            {
                _current[a] = null;
                _scratch[a].Reset();
                _lastFrontAxis[a] = TacticalFrontAxis.None;
                _lastPlayerCommandId[a] = 0;
                Array.Clear(_ownUnits[a], 0, _ownUnits[a].Length);
                Array.Clear(_enemyUnits[a], 0, _enemyUnits[a].Length);
            }
        }
    }
}
