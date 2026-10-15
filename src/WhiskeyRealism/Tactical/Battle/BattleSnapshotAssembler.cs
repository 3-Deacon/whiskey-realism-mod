using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    // Pure derivations over a BattleSnapshot (BC1): the single command-parent map, the
    // front axis, and the probe lists DirectChildDiscovery / CommandTreeRuntime build from.
    //
    // Command parent rule (plan D5). Vanilla anchors, decompile build 14703111:
    //   - Regiment.UpdateAllAttachedUnits (129572-129615) sets allattachedunits =
    //     GetComponentsInChildren<Regiment>(true): every descendant, inactive ones
    //     included, AND the group itself. A leaf's list is just [self].
    //   - GetAttachedUnitsReg(directonly) (119840-119880) treats a unit as a direct child
    //     when its transform.parent is the group's transform, after its filters:
    //     permanentlydetached (excludedechainedunits), unittyp 3/4 (excludeskirmishers),
    //     !activeInHierarchy | isrouted (includenonactiveunits:false), isgarrisonbasicunit.
    //   - Groups are unittyp > 13 (vanilla's own cut, 129589).
    // The direct parent of a unit is therefore the smallest group (fewest allattachedunits)
    // whose list contains it, self excluded. Groups are visited smallest first and the
    // first claim wins, so nothing is allocated and no transform is read.
    internal static class BattleSnapshotAssembler
    {
        // Sets UnitRecord.ParentIndex for own[0..count). attachedIds[i] holds the
        // gameObject instance ids of own[i].allattachedunits (attachedCounts[i] of them);
        // they are read only when the roster signature changed. With an unchanged roster
        // the cached parent ids are re-resolved to this tick's indices.
        internal static void AssembleCommandTree(UnitRecord[] own, int count, int[][] attachedIds, int[] attachedCounts, BattleSnapshotScratch scratch)
        {
            if (own == null || scratch == null) return;
            if (count > own.Length) count = own.Length;

            var indexById = scratch.IndexById;
            indexById.Clear();
            for (int i = 0; i < count; i++)
            {
                own[i].ParentIndex = -1;
                if (own[i].InstanceId != 0) indexById[own[i].InstanceId] = i;
            }

            var roster = BattleRosterSignature.Of(own, count, scratch.SourceListIdentity);
            if (scratch.HasTree && roster.Equals(scratch.Roster))
            {
                var parentIdById = scratch.ParentIdById;
                for (int i = 0; i < count; i++)
                {
                    if (parentIdById.TryGetValue(own[i].InstanceId, out int parentId)
                        && indexById.TryGetValue(parentId, out int parentIndex))
                        own[i].ParentIndex = parentIndex;
                }
                return;
            }

            RebuildParents(own, count, attachedIds, attachedCounts, scratch);
            scratch.Roster = roster;
            scratch.HasTree = true;
            scratch.TreeGeneration++;
        }

        // True when the roster differs from the one the cached parent map was built for,
        // i.e. the runtime must re-read allattachedunits (and names) this tick.
        internal static bool RosterChanged(UnitRecord[] own, int count, BattleSnapshotScratch scratch)
        {
            if (scratch == null || !scratch.HasTree) return true;
            return !BattleRosterSignature.Of(own, count, scratch.SourceListIdentity).Equals(scratch.Roster);
        }

        private static void RebuildParents(UnitRecord[] own, int count, int[][] attachedIds, int[] attachedCounts, BattleSnapshotScratch scratch)
        {
            // Candidate parents: groups, ordered by list length ascending, ties by index.
            int groups = 0;
            if (scratch.GroupOrder.Length < count) scratch.GroupOrder = new int[Math.Max(count, scratch.GroupOrder.Length * 2)];
            var order = scratch.GroupOrder;
            for (int i = 0; i < count; i++)
            {
                if ((own[i].Flags & UnitFlags.IsGroup) == 0) continue;
                if (AttachedCount(attachedIds, attachedCounts, i) <= 1) continue; // only itself
                int length = AttachedCount(attachedIds, attachedCounts, i);
                int slot = groups++;
                while (slot > 0)
                {
                    int prev = order[slot - 1];
                    if (AttachedCount(attachedIds, attachedCounts, prev) <= length) break;
                    order[slot] = prev;
                    slot--;
                }
                order[slot] = i;
            }

            var indexById = scratch.IndexById;
            for (int g = 0; g < groups; g++)
            {
                int group = order[g];
                int[] ids = attachedIds[group];
                int n = AttachedCount(attachedIds, attachedCounts, group);
                int selfId = own[group].InstanceId;
                for (int k = 0; k < n; k++)
                {
                    int childId = ids[k];
                    if (childId == 0 || childId == selfId) continue;
                    if (!indexById.TryGetValue(childId, out int child) || child == group) continue;
                    if (own[child].ParentIndex < 0) own[child].ParentIndex = group;
                }
            }

            var parentIdById = scratch.ParentIdById;
            parentIdById.Clear();
            for (int i = 0; i < count; i++)
            {
                int p = own[i].ParentIndex;
                if (p >= 0 && own[i].InstanceId != 0) parentIdById[own[i].InstanceId] = own[p].InstanceId;
            }
        }

        private static int AttachedCount(int[][] attachedIds, int[] attachedCounts, int i)
        {
            if (attachedIds == null || attachedCounts == null || i >= attachedIds.Length || i >= attachedCounts.Length) return 0;
            int[] ids = attachedIds[i];
            if (ids == null) return 0;
            int n = attachedCounts[i];
            return n < 0 ? 0 : Math.Min(n, ids.Length);
        }

        // Own-centroid -> visible-enemy-centroid axis, stabilized against the previous
        // tick (TacticalFrontAxis.Stabilize, FrontAxisMinHeadingChangeDegrees), with lateral
        // bands over the own combat line. Mirrors ArmyEvidenceBuilder.BareAxis/FitAxis
        // (own combat leaves weighted by max(1, strength); groups when no leaf qualifies;
        // nothing visible keeps facing the last way), minus the objective fallback, which
        // needs map data the snapshot does not carry. Must run after perception has set
        // the visible-enemy summary for the tick.
        internal static TacticalFrontAxis FitFrontAxis(BattleSnapshot s, TacticalFrontAxis previous)
        {
            if (s == null) return previous;
            bool leaves = HasCombatLeaf(s);
            double sx = 0, sz = 0, w = 0;
            for (int i = 0; i < s.OwnCount; i++)
            {
                ref UnitRecord u = ref s.Own[i];
                if (!InFrontSample(in u, leaves)) continue;
                double weight = Math.Max(1f, leaves ? u.Strength : u.GroupStrengthDirect);
                sx += u.X * weight;
                sz += u.Z * weight;
                w += weight;
            }
            if (w <= 0) return TacticalFrontAxis.None;
            float ox = (float)(sx / w), oz = (float)(sz / w);

            TacticalFrontAxis next;
            if (s.VisibleEnemyStrength > 0f)
                next = TacticalFrontAxis.FromCentroids(ox, oz, s.VisibleEnemyCentroidX, s.VisibleEnemyCentroidZ);
            else if (previous.IsValid)
                next = TacticalFrontAxis.FromCentroids(ox, oz, ox + previous.ForwardX * previous.TargetDistance,
                    oz + previous.ForwardZ * previous.TargetDistance);
            else
                next = TacticalFrontAxis.FromCentroids(ox, oz, ox, oz);
            var bare = TacticalFrontAxis.Stabilize(previous, next, BattleSnapshot.FrontAxisMinHeadingChangeDegrees);
            if (!bare.IsValid) return bare;

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < s.OwnCount; i++)
            {
                ref UnitRecord u = ref s.Own[i];
                if (!InFrontSample(in u, leaves)) continue;
                float lateral = bare.LateralOf(u.X, u.Z);
                if (lateral < min) min = lateral;
                if (lateral > max) max = lateral;
            }
            var axis = min <= max ? bare.WithOwnExtent(min, max) : bare.WithOwnExtent(0f, 0f);

            float emin = float.MaxValue, emax = float.MinValue;
            for (int i = 0; i < s.ObservationCount; i++)
            {
                ref EnemyObservation o = ref s.Observations[i];
                if (o.State != ObservationState.Visible) continue;
                float lateral = bare.LateralOf(o.X, o.Z);
                if (lateral < emin) emin = lateral;
                if (lateral > emax) emax = lateral;
            }
            return emin <= emax ? axis.WithEnemyExtent(emin, emax) : axis;
        }

        private static bool HasCombatLeaf(BattleSnapshot s)
        {
            for (int i = 0; i < s.OwnCount; i++)
                if (InFrontSample(in s.Own[i], leaves: true)) return true;
            return false;
        }

        private static bool InFrontSample(in UnitRecord u, bool leaves)
        {
            if ((u.Flags & UnitFlags.Routed) != 0) return false;
            if (leaves)
            {
                if ((u.Flags & UnitFlags.IsGroup) != 0) return false;
                if (u.UnitTyp > TacticalUnitType.MaxCombat || u.UnitTyp == TacticalUnitType.Excluded) return false;
                return u.Strength > 0 && (u.Flags & UnitFlags.Active) != 0;
            }
            return (u.Flags & UnitFlags.IsGroup) != 0 && (u.Flags & UnitFlags.PlayerOwned) == 0;
        }

        // ---- probe converters (one parent map for every tree consumer) ------------------

        // DirectChildDiscovery input. excludePlayerOwned drops the W&L player's units
        // (no role is ever allocated to them).
        internal static void ToRegimentProbes(BattleSnapshot s, IReadOnlyDictionary<int, string> names, int commandHierarchyShift,
            bool excludePlayerOwned, List<DirectChildDiscovery.RegimentProbe> into)
        {
            into.Clear();
            if (s == null) return;
            int commandMin = ClampShiftedMin(commandHierarchyShift);
            for (int i = 0; i < s.OwnCount; i++)
            {
                ref UnitRecord u = ref s.Own[i];
                if (excludePlayerOwned && (u.Flags & UnitFlags.PlayerOwned) != 0) continue;
                into.Add(new DirectChildDiscovery.RegimentProbe(
                    instanceId: u.InstanceId,
                    unittyp: u.UnitTyp,
                    name: NameOf(names, u.InstanceId),
                    active: IsOperationallyPresent(in u, commandMin),
                    parentInstanceId: ParentId(s, i),
                    isDirectChild: IsDirectChild(s, i, commandMin)));
            }
        }

        // TacticalCommandTreeProbe input (role cascade / leaf-brigade map).
        internal static void ToExtendedProbes(BattleSnapshot s, IReadOnlyDictionary<int, string> names, int commandHierarchyShift,
            List<TacticalCommandTreeProbe.ExtendedProbe> into)
        {
            into.Clear();
            if (s == null) return;
            int commandMin = ClampShiftedMin(commandHierarchyShift);
            for (int i = 0; i < s.OwnCount; i++)
            {
                ref UnitRecord u = ref s.Own[i];
                into.Add(new TacticalCommandTreeProbe.ExtendedProbe(
                    instanceId: u.InstanceId,
                    unittyp: u.UnitTyp,
                    name: NameOf(names, u.InstanceId),
                    active: IsOperationallyPresent(in u, commandMin),
                    parentInstanceId: ParentId(s, i),
                    isDirectChild: IsDirectChild(s, i, commandMin),
                    worldX: u.X,
                    worldZ: u.Z,
                    strengthBucket: StrengthBucket(in u)));
            }
        }

        // CommandTreeBuilder input. markedforrout is passed false: vanilla sets it only on
        // cavalry leaves and clears it before routing (Rout 123547-123595), and groups
        // return before it (unittyp > 13), so it never applies to a command-level node.
        internal static void ToCommandProbes(BattleSnapshot s, IReadOnlyDictionary<int, string> names, int commandHierarchyShift,
            List<CommandTreeBuilder.CommandProbe> into)
        {
            into.Clear();
            if (s == null) return;
            int commandMin = ClampShiftedMin(commandHierarchyShift);
            for (int i = 0; i < s.OwnCount; i++)
            {
                ref UnitRecord u = ref s.Own[i];
                into.Add(ToCommandProbe(s, i, NameOf(names, u.InstanceId), commandMin));
            }
        }

        internal static CommandTreeBuilder.CommandProbe ToCommandProbe(BattleSnapshot s, int ownIndex, string name, int commandMin)
        {
            ref UnitRecord u = ref s.Own[ownIndex];
            return new CommandTreeBuilder.CommandProbe(
                instanceId: u.InstanceId,
                parentInstanceId: ParentId(s, ownIndex),
                allianceId: u.Alliance,
                rawUnitTyp: u.UnitTyp,
                displayName: name,
                active: IsOperationallyPresent(in u, commandMin),
                routed: (u.Flags & UnitFlags.Routed) != 0,
                markedForRout: false);
        }

        // Present on the field: active, or a command-level group that has not routed
        // (vanilla deactivates group objects that are still in command).
        internal static bool IsOperationallyPresent(in UnitRecord u, int commandMin)
        {
            if ((u.Flags & UnitFlags.Active) != 0) return true;
            return u.UnitTyp >= commandMin && (u.Flags & UnitFlags.Routed) == 0;
        }

        // GetAttachedUnitsReg(excludedechainedunits:true, excludeskirmishers:true,
        // directonly:true, includenonactiveunits:false) from a command-level parent.
        // includebasicgarrisons:false is not reproduced: isgarrisonbasicunit marks
        // garrison regiments (82094), which never pass the command-level filter that
        // DirectChildDiscovery applies to direct children.
        private static bool IsDirectChild(BattleSnapshot s, int i, int commandMin)
        {
            ref UnitRecord u = ref s.Own[i];
            int p = u.ParentIndex;
            if (p < 0 || s.Own[p].UnitTyp < commandMin) return false;
            const UnitFlags excluded = UnitFlags.PermanentlyDetached | UnitFlags.Routed;
            if ((u.Flags & excluded) != 0 || (u.Flags & UnitFlags.Active) == 0) return false;
            return u.UnitTyp != TacticalUnitType.Skirmisher && u.UnitTyp != TacticalUnitType.Officer;
        }

        private static int ParentId(BattleSnapshot s, int i)
        {
            int p = s.Own[i].ParentIndex;
            return p >= 0 && p < s.OwnCount ? s.Own[p].InstanceId : 0;
        }

        // 0 weak / 1 normal / 2 strong, the thresholds of the former runtime bucket.
        // Groups read groupstrengthdirect (UpdateGroupStrength 120301), leaves their own
        // strength; groupowninrange / groupstrengthaigroup are no longer read.
        private static int StrengthBucket(in UnitRecord u)
        {
            float strength = Math.Max(u.GroupStrengthDirect, u.Strength);
            if (strength >= 3000f) return 2;
            if (strength >= 1000f) return 1;
            return 0;
        }

        private static string NameOf(IReadOnlyDictionary<int, string> names, int instanceId)
        {
            return names != null && names.TryGetValue(instanceId, out string name) ? name : string.Empty;
        }

        internal static int ClampShiftedMin(int shift)
        {
            int min = TacticalUnitType.MaxCombat + 1 + shift;
            if (min < 1) return 1;
            if (min > 18) return 18;
            return min;
        }
    }

    // Roster identity the parent map is cached against: own unit count, routed count,
    // sum of instance ids, and the identity of the vanilla list it was read from
    // (BattleUnits.UpdateUnitList 79974 replaces completeunitlist on DetachUnits 90239 /
    // ReattachCompany 90609 / CreateUnitDefault 89670, which can re-parent units without
    // changing the id set).
    internal readonly struct BattleRosterSignature : IEquatable<BattleRosterSignature>
    {
        private BattleRosterSignature(int count, int routedCount, long idSum, int listIdentity)
        {
            Count = count;
            RoutedCount = routedCount;
            IdSum = idSum;
            ListIdentity = listIdentity;
        }

        internal int Count { get; }
        internal int RoutedCount { get; }
        internal long IdSum { get; }
        internal int ListIdentity { get; }

        internal static BattleRosterSignature Of(UnitRecord[] own, int count, int listIdentity)
        {
            int routed = 0;
            long idSum = 0;
            for (int i = 0; i < count; i++)
            {
                idSum += own[i].InstanceId;
                if ((own[i].Flags & UnitFlags.Routed) != 0) routed++;
            }
            return new BattleRosterSignature(count, routed, idSum, listIdentity);
        }

        public bool Equals(BattleRosterSignature other)
        {
            return Count == other.Count && RoutedCount == other.RoutedCount
                && IdSum == other.IdSum && ListIdentity == other.ListIdentity;
        }

        public override bool Equals(object obj) => obj is BattleRosterSignature other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return ((Count * 397) ^ RoutedCount) * 397 ^ IdSum.GetHashCode() ^ ListIdentity; }
        }
    }

    // Per-alliance pooled state behind the assembler: the cached parent map, its roster
    // signature, and the name cache the runtime fills on roster change (GameObject.name
    // allocates, so it is never read per tick).
    internal sealed class BattleSnapshotScratch
    {
        internal int SourceListIdentity;
        internal BattleRosterSignature Roster;
        internal bool HasTree;
        internal int TreeGeneration;
        internal int AncestryMismatchCount;
        internal int[] GroupOrder = new int[BattleSnapshot.InitialCapacity];
        internal readonly Dictionary<int, int> IndexById = new Dictionary<int, int>(BattleSnapshot.InitialCapacity);
        internal readonly Dictionary<int, int> ParentIdById = new Dictionary<int, int>(BattleSnapshot.InitialCapacity);
        internal readonly Dictionary<int, string> NameById = new Dictionary<int, string>(BattleSnapshot.InitialCapacity);

        internal void Reset()
        {
            SourceListIdentity = 0;
            Roster = default(BattleRosterSignature);
            HasTree = false;
            AncestryMismatchCount = 0;
            IndexById.Clear();
            ParentIdById.Clear();
            NameById.Clear();
        }
    }
}
