using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC1 (docs/superpowers/plans/2026-10-15-battle-core.md): pooled BattleSnapshot, the D5
// command-parent map (smallest containing group over vanilla allattachedunits, which
// UpdateAllAttachedUnits 129574 fills with GetComponentsInChildren(true), self included),
// roster-signature gating, front-axis stabilization and the probe converters that
// DirectChildDiscovery and CommandTreeRuntime now read.
internal static class BattleSnapshotTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("battle snapshot command tree parent is smallest containing group", CommandTreeParentIsSmallestContainingGroup),
            ("battle snapshot command tree root has no parent", CommandTreeRootHasNoParent),
            ("battle snapshot direct children match parent map", DirectChildrenMatchesParentMap),
            ("battle snapshot is-ancestor-of walks parent chain", IsAncestorOfWalksParentChain),
            ("battle snapshot index of own returns -1 for unknown", IndexOfOwnReturnsMinusOneForUnknown),
            ("battle snapshot assembler rebuilds tree only on roster change", AssemblerRebuildsTreeOnlyOnRosterChange),
            ("battle snapshot assembly allocates nothing after warm-up", SnapshotAssemblyAllocatesNothingAfterWarmup),
            ("battle snapshot front axis stabilized across ticks", FrontAxisStabilizedAcrossTicks),
            ("battle snapshot direct-child and command-tree probes agree on parents", DirectChildAndCommandTreeAgreeOnParents),
        });
    }

    // Ids of the small fixture army.
    private const int Army = 1000, Div1 = 1100, Div2 = 1200, Brig1 = 1110, Brig2 = 1120, Brig3 = 1210;
    private const int R1 = 1, R2 = 2, R3 = 3, R4 = 4, Loose = 9;

    // Army(16) -> Div1(15), Div2(15); Div1 -> Brig1(14), Brig2(14); Div2 -> Brig3(14);
    // Brig1 -> R1, R2; Brig2 -> R3; Brig3 -> R4. Loose belongs to no group. Records are in
    // a shuffled order so the parent rule cannot lean on completeunitlist order.
    private sealed class Fixture
    {
        internal readonly BattleSnapshot Snapshot = new BattleSnapshot();
        internal readonly BattleSnapshotScratch Scratch = new BattleSnapshotScratch();
        internal readonly List<UnitRecord> Records = new List<UnitRecord>();
        internal readonly Dictionary<int, int[]> Attached = new Dictionary<int, int[]>();
        internal int[][] AttachedIds;
        internal int[] AttachedCounts;

        internal void Add(int id, int unittyp, float x, float z, int strength, UnitFlags extra = UnitFlags.None, params int[] descendants)
        {
            var flags = UnitFlags.Active | extra;
            if (unittyp > TacticalUnitType.MaxCombat) flags |= UnitFlags.IsGroup | UnitFlags.Alive;
            else if (strength > 0) flags |= UnitFlags.Alive;
            Records.Add(new UnitRecord
            {
                InstanceId = id, UnitTyp = unittyp, Alliance = 0, X = x, Z = z,
                Strength = strength, GroupStrengthDirect = strength, ParentIndex = -1, Flags = flags,
            });
            var self = new int[descendants.Length + 1];
            self[0] = id; // GetComponentsInChildren(true) includes the component's own object
            Array.Copy(descendants, 0, self, 1, descendants.Length);
            Attached[id] = self;
        }

        internal void Load(long tickId = 1)
        {
            Snapshot.BeginBuild(battleKey: 77, alliance: 0, side: 0, tickId: tickId, doctrineHours: 1.0);
            for (int i = 0; i < Records.Count; i++)
            {
                var r = Records[i];
                Snapshot.AddOwn(in r);
            }
            if (AttachedIds == null || AttachedIds.Length < Records.Count)
            {
                AttachedIds = new int[Records.Count][];
                AttachedCounts = new int[Records.Count];
            }
            for (int i = 0; i < Records.Count; i++)
            {
                var ids = Attached[Records[i].InstanceId];
                AttachedIds[i] = ids;
                AttachedCounts[i] = ids.Length;
            }
        }

        internal void Assemble()
        {
            BattleSnapshotAssembler.AssembleCommandTree(Snapshot.Own, Snapshot.OwnCount, AttachedIds, AttachedCounts, Scratch);
            Snapshot.IndexCommandTree(Scratch);
        }

        internal int Parent(int id)
        {
            int i = Snapshot.IndexOfOwn(id);
            int p = Snapshot.Own[i].ParentIndex;
            return p < 0 ? 0 : Snapshot.Own[p].InstanceId;
        }
    }

    private static Fixture SmallArmy()
    {
        var f = new Fixture();
        f.Add(R3, TacticalUnitType.Artillery, 30f, 0f, 120);
        f.Add(Brig1, TacticalUnitType.BattleGroupBrigade, 10f, 0f, 1200, UnitFlags.None, R1, R2);
        f.Add(Army, TacticalUnitType.BattleGroupArmy, 0f, -50f, 4000, UnitFlags.None, Div1, Div2, Brig1, Brig2, Brig3, R1, R2, R3, R4);
        f.Add(R1, TacticalUnitType.Infantry, 5f, 0f, 500);
        f.Add(Div2, TacticalUnitType.BattleGroupDivision, 60f, -20f, 900, UnitFlags.None, Brig3, R4);
        f.Add(Loose, TacticalUnitType.Infantry, -40f, 0f, 300);
        f.Add(Brig3, TacticalUnitType.BattleGroupBrigade, 60f, 0f, 900, UnitFlags.None, R4);
        f.Add(R4, TacticalUnitType.Cavalry, 65f, 0f, 400);
        f.Add(Div1, TacticalUnitType.BattleGroupDivision, 15f, -20f, 1800, UnitFlags.None, Brig1, Brig2, R1, R2, R3);
        f.Add(Brig2, TacticalUnitType.BattleGroupBrigade, 30f, 0f, 600, UnitFlags.None, R3);
        f.Add(R2, TacticalUnitType.Infantry, 15f, 0f, 500);
        f.Load();
        f.Assemble();
        return f;
    }

    private static void CommandTreeParentIsSmallestContainingGroup()
    {
        var f = SmallArmy();
        AssertEqual(Brig1, f.Parent(R1), "R1 -> Brig1 (Div1 and Army also contain it)");
        AssertEqual(Brig1, f.Parent(R2), "R2 -> Brig1");
        AssertEqual(Brig2, f.Parent(R3), "R3 -> Brig2");
        AssertEqual(Brig3, f.Parent(R4), "R4 -> Brig3");
        AssertEqual(Div1, f.Parent(Brig1), "Brig1 -> Div1");
        AssertEqual(Div1, f.Parent(Brig2), "Brig2 -> Div1");
        AssertEqual(Div2, f.Parent(Brig3), "Brig3 -> Div2");
        AssertEqual(Army, f.Parent(Div1), "Div1 -> Army");
        AssertEqual(Army, f.Parent(Div2), "Div2 -> Army");
    }

    private static void CommandTreeRootHasNoParent()
    {
        var f = SmallArmy();
        AssertEqual(-1, f.Snapshot.Own[f.Snapshot.IndexOfOwn(Army)].ParentIndex, "army root (self is in its own allattachedunits)");
        AssertEqual(-1, f.Snapshot.Own[f.Snapshot.IndexOfOwn(Loose)].ParentIndex, "unit in no group");
    }

    private static void DirectChildrenMatchesParentMap()
    {
        var f = SmallArmy();
        var s = f.Snapshot;
        var buffer = new int[s.OwnCount];
        for (int i = 0; i < s.OwnCount; i++)
        {
            int n = s.DirectChildren(i, buffer);
            int expected = 0;
            for (int j = 0; j < s.OwnCount; j++)
                if (s.Own[j].ParentIndex == i) expected++;
            AssertEqual(expected, n, "child count of " + s.Own[i].InstanceId);
            for (int k = 0; k < n; k++)
                AssertEqual(i, s.Own[buffer[k]].ParentIndex, "listed child's parent is " + s.Own[i].InstanceId);
        }
        int army = s.DirectChildren(s.IndexOfOwn(Army), buffer);
        AssertEqual(2, army, "army has two divisions");
        AssertEqual(0, s.DirectChildren(s.IndexOfOwn(R1), buffer), "a regiment has no children");
        AssertEqual(0, s.DirectChildren(-1, buffer), "out-of-range index");
        AssertEqual(1, s.DirectChildren(s.IndexOfOwn(Army), new int[1]), "capped at the buffer length");
    }

    private static void IsAncestorOfWalksParentChain()
    {
        var s = SmallArmy().Snapshot;
        int army = s.IndexOfOwn(Army), div1 = s.IndexOfOwn(Div1), div2 = s.IndexOfOwn(Div2), r4 = s.IndexOfOwn(R4);
        AssertTrue(s.IsAncestorOf(army, r4), "army is an ancestor of R4");
        AssertTrue(s.IsAncestorOf(div2, r4), "Div2 is an ancestor of R4");
        AssertFalse(s.IsAncestorOf(div1, r4), "Div1 is not an ancestor of R4");
        AssertFalse(s.IsAncestorOf(r4, army), "descendant is not an ancestor");
        AssertFalse(s.IsAncestorOf(army, army), "a unit is not its own ancestor");
        AssertFalse(s.IsAncestorOf(-1, r4), "out-of-range ancestor");
    }

    private static void IndexOfOwnReturnsMinusOneForUnknown()
    {
        var s = SmallArmy().Snapshot;
        AssertEqual(-1, s.IndexOfOwn(424242), "unknown id");
        AssertEqual(-1, s.IndexOfOwn(0), "zero id");
        int i = s.IndexOfOwn(Brig3);
        AssertEqual(Brig3, s.Own[i].InstanceId, "known id resolves to its record");
    }

    private static void AssemblerRebuildsTreeOnlyOnRosterChange()
    {
        var f = SmallArmy();
        // Same roster, stale/empty attached lists: the cached parent map must be reused.
        f.Load(tickId: 2);
        Array.Clear(f.AttachedCounts, 0, f.AttachedCounts.Length);
        f.Assemble();
        AssertEqual(Brig1, f.Parent(R1), "same roster keeps the cached parent");
        AssertEqual(Army, f.Parent(Div2), "same roster keeps the cached parent (group)");

        // Routed count changes the roster signature: rebuild from the (empty) lists.
        int r2 = f.Records.FindIndex(r => r.InstanceId == R2);
        var routed = f.Records[r2];
        routed.Flags |= UnitFlags.Routed;
        f.Records[r2] = routed;
        f.Load(tickId: 3);
        Array.Clear(f.AttachedCounts, 0, f.AttachedCounts.Length);
        f.Assemble();
        AssertEqual(0, f.Parent(R1), "routed count change rebuilds the map");

        // Count change rebuilds too, now with real lists again.
        f.Records.RemoveAt(f.Records.FindIndex(r => r.InstanceId == Loose));
        f.Load(tickId: 4);
        f.Assemble();
        AssertEqual(Brig1, f.Parent(R1), "count change rebuilds the map");

        // Sum of instance ids: same count, same routed count, different unit.
        int r4 = f.Records.FindIndex(r => r.InstanceId == R4);
        var swapped = f.Records[r4];
        swapped.InstanceId = 5;
        f.Records[r4] = swapped;
        f.Attached[5] = new[] { 5 };
        f.Attached[Brig3] = new[] { Brig3, 5 };
        f.Attached[Div2] = new[] { Div2, Brig3, 5 };
        f.Attached[Army] = new[] { Army, Div1, Div2, Brig1, Brig2, Brig3, R1, R2, R3, 5 };
        f.Load(tickId: 5);
        f.Assemble();
        AssertEqual(Brig3, f.Parent(5), "id-sum change rebuilds the map");
    }

    // 300 units: 1 army, 4 divisions, 20 brigades, 275 regiments.
    private static Fixture LargeArmy()
    {
        var f = new Fixture();
        const int army = 100000;
        var divs = new List<int>();
        var armyDesc = new List<int>();
        int reg = 1;
        for (int d = 0; d < 4; d++)
        {
            int div = 200000 + d;
            var divDesc = new List<int>();
            for (int b = 0; b < 5; b++)
            {
                int brig = 300000 + d * 10 + b;
                var brigDesc = new List<int>();
                int regiments = (d == 3 && b == 4) ? 28 : 13;
                for (int r = 0; r < regiments; r++)
                {
                    f.Add(reg, r % 7 == 0 ? TacticalUnitType.Artillery : TacticalUnitType.Infantry,
                        d * 400f + b * 60f + r, 100f + r, 400);
                    brigDesc.Add(reg);
                    reg++;
                }
                f.Add(brig, TacticalUnitType.BattleGroupBrigade, d * 400f + b * 60f, 90f, 5000, UnitFlags.None, brigDesc.ToArray());
                divDesc.Add(brig);
                divDesc.AddRange(brigDesc);
            }
            f.Add(div, TacticalUnitType.BattleGroupDivision, d * 400f + 120f, 60f, 25000, UnitFlags.None, divDesc.ToArray());
            divs.Add(div);
            armyDesc.Add(div);
            armyDesc.AddRange(divDesc);
        }
        f.Add(army, TacticalUnitType.BattleGroupArmy, 800f, 0f, 100000, UnitFlags.None, armyDesc.ToArray());
        AssertEqual(300, f.Records.Count, "fixture size");
        return f;
    }

    private static void SnapshotAssemblyAllocatesNothingAfterWarmup()
    {
        var f = LargeArmy();
        var previous = TacticalFrontAxis.None;
        f.Snapshot.SetEnemySummary(visibleStrength: 5000f, rememberedStrength: 0f, visibleCentroidX: 800f, visibleCentroidZ: 2000f);

        // Warm-up: a rebuild and a reuse pass grow every pooled buffer.
        f.Load(1); f.Assemble(); previous = BattleSnapshotAssembler.FitFrontAxis(f.Snapshot, previous);
        f.Load(2); f.Assemble(); previous = BattleSnapshotAssembler.FitFrontAxis(f.Snapshot, previous);

        long before = GC.GetAllocatedBytesForCurrentThread();
        f.Load(3); f.Assemble(); previous = BattleSnapshotAssembler.FitFrontAxis(f.Snapshot, previous);
        long reuse = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertEqual(0L, reuse, "second assembly (same roster) allocated bytes");

        // A roster change (one regiment routs) rebuilds the tree from pooled buffers.
        var r = f.Records[0];
        r.Flags |= UnitFlags.Routed;
        f.Records[0] = r;
        before = GC.GetAllocatedBytesForCurrentThread();
        f.Load(4); f.Assemble(); previous = BattleSnapshotAssembler.FitFrontAxis(f.Snapshot, previous);
        long rebuild = GC.GetAllocatedBytesForCurrentThread() - before;
        AssertEqual(0L, rebuild, "roster-change rebuild allocated bytes");
        AssertTrue(previous.IsValid, "front axis fitted");
        AssertEqual(f.Snapshot.IndexOfOwn(100000), f.Snapshot.Own[f.Snapshot.IndexOfOwn(200000)].ParentIndex, "large tree parent");
    }

    private static void FrontAxisStabilizedAcrossTicks()
    {
        AssertEqual(TacticalFrontAxis.DefaultMinHeadingChangeDegrees, BattleSnapshot.FrontAxisMinHeadingChangeDegrees, "reuses the existing stabilizer threshold");
        AssertEqual(512, BattleSnapshot.InitialCapacity, "initial pooled capacity");

        var f = SmallArmy();
        var s = f.Snapshot;
        // Own combat centroid sits near (x≈23, z=0); enemy due north (+Z).
        s.SetEnemySummary(5000f, 0f, 23f, 1000f);
        var axis1 = BattleSnapshotAssembler.FitFrontAxis(s, TacticalFrontAxis.None);
        AssertTrue(axis1.IsValid, "axis fitted toward the visible enemy");
        AssertNear(0f, axis1.HeadingDegrees, 1f, "heading north");

        // Enemy centroid swings ~5 degrees: below the 10-degree threshold, heading kept.
        f.Load(2); f.Assemble();
        s.SetEnemySummary(5000f, 0f, 23f + 1000f * (float)Math.Tan(5.0 * Math.PI / 180.0), 1000f);
        var axis2 = BattleSnapshotAssembler.FitFrontAxis(s, axis1);
        AssertNear(axis1.HeadingDegrees, axis2.HeadingDegrees, 0.01f, "small swing keeps heading");

        // ~25 degree swing: the new heading is taken.
        f.Load(3); f.Assemble();
        s.SetEnemySummary(5000f, 0f, 23f + 1000f * (float)Math.Tan(25.0 * Math.PI / 180.0), 1000f);
        var axis3 = BattleSnapshotAssembler.FitFrontAxis(s, axis2);
        AssertNear(25f, axis3.HeadingDegrees, 1.5f, "large swing re-heads");

        // Nothing visible: keep facing the last way.
        f.Load(4); f.Assemble();
        s.SetEnemySummary(0f, 0f, 0f, 0f);
        var axis4 = BattleSnapshotAssembler.FitFrontAxis(s, axis3);
        AssertNear(axis3.HeadingDegrees, axis4.HeadingDegrees, 0.01f, "no visible enemy keeps heading");
        AssertTrue(axis4.BandCount == TacticalFrontAxis.DefaultBandCount, "bands spread over own extent");
    }

    private static void DirectChildAndCommandTreeAgreeOnParents()
    {
        var f = SmallArmy();
        var s = f.Snapshot;
        var names = new Dictionary<int, string>();
        for (int i = 0; i < s.OwnCount; i++) names[s.Own[i].InstanceId] = "unit-" + s.Own[i].InstanceId;

        var regimentProbes = new List<DirectChildDiscovery.RegimentProbe>();
        var commandProbes = new List<CommandTreeBuilder.CommandProbe>();
        var extendedProbes = new List<TacticalCommandTreeProbe.ExtendedProbe>();
        BattleSnapshotAssembler.ToRegimentProbes(s, names, 0, excludePlayerOwned: false, regimentProbes);
        BattleSnapshotAssembler.ToCommandProbes(s, names, 0, commandProbes);
        BattleSnapshotAssembler.ToExtendedProbes(s, names, 0, extendedProbes);

        AssertEqual(s.OwnCount, regimentProbes.Count, "one regiment probe per own unit");
        AssertEqual(s.OwnCount, commandProbes.Count, "one command probe per own unit");
        AssertEqual(s.OwnCount, extendedProbes.Count, "one extended probe per own unit");
        for (int i = 0; i < s.OwnCount; i++)
        {
            int expected = f.Parent(s.Own[i].InstanceId);
            AssertEqual(expected, regimentProbes[i].ParentInstanceId, "direct-child parent of " + s.Own[i].InstanceId);
            AssertEqual(expected, commandProbes[i].ParentInstanceId, "command-tree parent of " + s.Own[i].InstanceId);
            AssertEqual(expected, extendedProbes[i].ParentInstanceId, "extended parent of " + s.Own[i].InstanceId);
            AssertEqual("unit-" + s.Own[i].InstanceId, commandProbes[i].DisplayName, "names come from the roster cache");
        }

        // GetAttachedUnitsReg(directonly, includenonactiveunits:false, excludedechainedunits,
        // excludeskirmishers) exclusions (119849-119873) carry over to IsDirectChild.
        int iDiv1 = s.IndexOfOwn(Div1);
        AssertTrue(regimentProbes[iDiv1].IsDirectChild, "Div1 is a direct child of the army");
        AssertFalse(regimentProbes[s.IndexOfOwn(Army)].IsDirectChild, "root is nobody's direct child");
        AssertFalse(regimentProbes[s.IndexOfOwn(Loose)].IsDirectChild, "unit in no group");

        var detached = s.Own[iDiv1];
        detached.Flags |= UnitFlags.PermanentlyDetached;
        f.Records[f.Records.FindIndex(r => r.InstanceId == Div1)] = detached;
        int iR3 = f.Records.FindIndex(r => r.InstanceId == R3);
        var skirmisher = f.Records[iR3];
        skirmisher.UnitTyp = TacticalUnitType.Skirmisher;
        f.Records[iR3] = skirmisher;
        int iBrig3 = f.Records.FindIndex(r => r.InstanceId == Brig3);
        var playerOwned = f.Records[iBrig3];
        playerOwned.Flags |= UnitFlags.PlayerOwned;
        f.Records[iBrig3] = playerOwned;
        f.Load(2); f.Assemble();
        BattleSnapshotAssembler.ToRegimentProbes(s, names, 0, excludePlayerOwned: true, regimentProbes);
        int div1Probe = regimentProbes.FindIndex(p => p.InstanceId == Div1);
        AssertFalse(regimentProbes[div1Probe].IsDirectChild, "permanently detached is not a direct child");
        AssertEqual(Army, regimentProbes[div1Probe].ParentInstanceId, "detached keeps its parent link");
        AssertFalse(regimentProbes.Find(p => p.InstanceId == R3).IsDirectChild, "skirmisher is not a direct child");
        AssertEqual(-1, regimentProbes.FindIndex(p => p.InstanceId == Brig3), "W&L player-owned excluded from discovery");
        AssertEqual(s.OwnCount - 1, regimentProbes.Count, "only the player-owned unit dropped");
    }
}
