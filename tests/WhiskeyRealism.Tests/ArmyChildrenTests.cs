using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17 (session 20261017-001611-436): the orchestrator saw each army as one
// synthetic child, so no brigade got a role. Cause: vanilla marks the groups the AI engine
// drives permanentlydetached (AddUnitToPool 85386, the AI-level pass 85255-85290, and any
// stance order via ChangeStance 90786), and DirectChildDiscovery copied the
// excludedechainedunits filter of GetAttachedUnitsReg, which drops exactly those groups
// from their army. The fixtures below are the evidence's hierarchies.
internal static class ArmyChildrenTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("army children union department finds its detached division", UnionDepartmentFindsItsDetachedDivision),
            ("army children csa two armies find all their detached brigades", CsaTwoArmiesFindAllTheirDetachedBrigades),
            ("army children stance order detaching a brigade keeps it a child", StanceOrderDetachingABrigadeKeepsItAChild),
            ("army children routed and skirmisher companies stay out", RoutedAndSkirmisherCompaniesStayOut),
            ("army children player owned detached command stays out", PlayerOwnedDetachedCommandStaysOut),
        });
    }

    // Union side 0: Department(16) -> 1st_Division(15) -> four brigades(14) -> one regiment
    // each. Vanilla's AI-level pass (maxunitlevelforallocationtoai = 15) detaches the division
    // and the brigade-level groups under it are driven through it.
    private static Hierarchy Union(UnitFlags groupFlags)
    {
        var h = new Hierarchy();
        h.Add(1, 16, UnitFlags.None, 10, 11, 12, 13, 14, 20, 21, 22, 23);
        h.Add(10, 15, groupFlags, 11, 12, 13, 14, 20, 21, 22, 23);
        for (int b = 0; b < 4; b++) h.Add(11 + b, 14, groupFlags, 20 + b);
        for (int b = 0; b < 4; b++) h.Add(20 + b, TacticalUnitType.Infantry, UnitFlags.None);
        h.Assemble();
        return h;
    }

    private static void UnionDepartmentFindsItsDetachedDivision()
    {
        var h = Union(UnitFlags.PermanentlyDetached);
        foreach (int shift in new[] { 0, -1 })
        {
            var children = h.Discover(shift);
            AssertEqual(1, children.Count, "one direct child of the department, shift " + shift);
            AssertEqual("child-10", children[0].ChildId, "the division is the child, not the army itself");
            AssertEqual("army-1", children[0].ParentArmyId, "parent is the department");
        }
    }

    // CSA side 1: Army_of_the_Potomac(16) and Army_of_Shenandoah(16) hold brigades(14) directly.
    private static void CsaTwoArmiesFindAllTheirDetachedBrigades()
    {
        // Lists are nested or disjoint, like GetComponentsInChildren.
        var h = new Hierarchy();
        h.Add(100, 16, UnitFlags.None, 110, 111, 112, 120, 121, 122);
        h.Add(200, 16, UnitFlags.None, 210, 211, 220, 221);
        for (int b = 0; b < 3; b++) h.Add(110 + b, 14, UnitFlags.PermanentlyDetached, 120 + b);
        for (int b = 0; b < 3; b++) h.Add(120 + b, TacticalUnitType.Infantry, UnitFlags.None);
        for (int b = 0; b < 2; b++) h.Add(210 + b, 14, UnitFlags.PermanentlyDetached, 220 + b);
        for (int b = 0; b < 2; b++) h.Add(220 + b, TacticalUnitType.Infantry, UnitFlags.None);
        h.Assemble();
        var children = h.Discover(-1);
        AssertEqual(5, children.Count, "three brigades of one army and two of the other");
        AssertEqual(3, children.Count(c => c.ParentArmyId == "army-100"), "first army's brigades");
        AssertEqual(2, children.Count(c => c.ParentArmyId == "army-200"), "second army's brigades");
        AssertTrue(children.All(c => c.ChildId.StartsWith("child-")), "no synthetic army child");
    }

    // ChangeStance with a stance above 0 sets permanentlydetached on the ordered unit
    // (90786-90791), so Whiskey's own stance order must not remove a brigade from the roster.
    private static void StanceOrderDetachingABrigadeKeepsItAChild()
    {
        var h = Union(UnitFlags.None);
        AssertEqual("child-10", h.Discover(-1)[0].ChildId, "before any stance order");
        h.SetFlag(10, UnitFlags.PermanentlyDetached);
        h.SetFlag(12, UnitFlags.PermanentlyDetached);
        h.Assemble();
        AssertEqual("child-10", h.Discover(-1)[0].ChildId, "after a stance order detached the division");

        // Same with brigades directly under the army.
        var b = new Hierarchy();
        b.Add(1, 16, UnitFlags.None, 11, 12, 20, 21);
        b.Add(11, 14, UnitFlags.None, 20);
        b.Add(12, 14, UnitFlags.None, 21);
        b.Add(20, TacticalUnitType.Infantry, UnitFlags.None);
        b.Add(21, TacticalUnitType.Infantry, UnitFlags.None);
        b.Assemble();
        AssertEqual(2, b.Discover(-1).Count, "two brigades");
        b.SetFlag(11, UnitFlags.PermanentlyDetached);
        b.Assemble();
        var after = b.Discover(-1);
        AssertEqual(2, after.Count, "the detached brigade is still the army's child");
        AssertTrue(after.All(c => c.ChildId.StartsWith("child-")), "no synthetic child");
    }

    private static void RoutedAndSkirmisherCompaniesStayOut()
    {
        var b = new Hierarchy();
        b.Add(1, 16, UnitFlags.None, 11, 12, 20, 21);
        b.Add(11, 14, UnitFlags.Routed, 20);
        b.Add(12, 14, UnitFlags.PermanentlyDetached, 21);
        b.Add(20, TacticalUnitType.Infantry, UnitFlags.None);
        b.Add(21, TacticalUnitType.Infantry, UnitFlags.None);
        b.Assemble();
        var children = b.Discover(-1);
        AssertEqual(1, children.Count, "the routed brigade is not a child");
        AssertEqual("child-12", children[0].ChildId, "the detached, unrouted brigade is");
    }

    private static void PlayerOwnedDetachedCommandStaysOut()
    {
        var b = new Hierarchy();
        b.Add(1, 16, UnitFlags.None, 11, 12, 20, 21);
        b.Add(11, 14, UnitFlags.PermanentlyDetached | UnitFlags.PlayerOwned, 20);
        b.Add(12, 14, UnitFlags.PermanentlyDetached, 21);
        b.Add(20, TacticalUnitType.Infantry, UnitFlags.PlayerOwned);
        b.Add(21, TacticalUnitType.Infantry, UnitFlags.None);
        b.Assemble();
        var children = b.Discover(-1, excludePlayerOwned: true);
        AssertEqual(1, children.Count, "the W&L player's detached command is nobody's AI child");
        AssertEqual("child-12", children[0].ChildId, "the AI brigade is");
    }

    private sealed class Hierarchy
    {
        private readonly List<UnitRecord> _records = new List<UnitRecord>();
        private readonly Dictionary<int, int[]> _attached = new Dictionary<int, int[]>();
        private readonly BattleSnapshot _snapshot = new BattleSnapshot();
        private readonly BattleSnapshotScratch _scratch = new BattleSnapshotScratch();

        internal void Add(int id, int unittyp, UnitFlags extra, params int[] descendants)
        {
            var flags = UnitFlags.Active | UnitFlags.Alive | extra;
            if (unittyp > TacticalUnitType.MaxCombat) flags |= UnitFlags.IsGroup;
            _records.Add(new UnitRecord { InstanceId = id, UnitTyp = unittyp, ParentIndex = -1, Flags = flags, Strength = 500 });
            var list = new int[descendants.Length + 1];
            list[0] = id; // GetComponentsInChildren(true) includes the component's own object
            Array.Copy(descendants, 0, list, 1, descendants.Length);
            _attached[id] = list;
        }

        internal void SetFlag(int id, UnitFlags flag)
        {
            int i = _records.FindIndex(r => r.InstanceId == id);
            var r = _records[i];
            r.Flags |= flag;
            _records[i] = r;
        }

        internal void Assemble()
        {
            _snapshot.BeginBuild(77, 0, 0, 1, 1.0);
            _scratch.Reset();
            foreach (var record in _records) { var r = record; _snapshot.AddOwn(in r); }
            var ids = new int[_records.Count][];
            var counts = new int[_records.Count];
            int lengthSum = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                ids[i] = _attached[_records[i].InstanceId];
                counts[i] = ids[i].Length;
                if ((_records[i].Flags & UnitFlags.IsGroup) != 0) lengthSum += counts[i];
            }
            _scratch.AttachedLengthSum = lengthSum;
            BattleSnapshotAssembler.AssembleCommandTree(_snapshot.Own, _snapshot.OwnCount, ids, counts, _scratch);
            _snapshot.IndexCommandTree(_scratch);
        }

        internal IReadOnlyList<DirectChildSnapshot> Discover(int shift, bool excludePlayerOwned = false)
        {
            var probes = new List<DirectChildDiscovery.RegimentProbe>();
            BattleSnapshotAssembler.ToRegimentProbes(_snapshot, null, shift, excludePlayerOwned, probes);
            return DirectChildDiscovery.Probe(probes, shift);
        }
    }
}
