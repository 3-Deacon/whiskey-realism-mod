using System;
using System.Collections.Generic;
using System.Linq;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Operations;
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
            ("army children a group not yet on the field is not a child", AGroupNotYetOnTheFieldIsNotAChild),
            ("army children skirmisher and scout companies are not children", SkirmisherAndScoutCompaniesAreNotChildren),
            ("army children player owned detached command stays out", PlayerOwnedDetachedCommandStaysOut),
            ("army children w and l depth keeps the player's ai brigade, division and army", WlDepthKeepsThePlayersAiBrigadeDivisionAndArmy),
            ("army children vanilla depth finds corps under the army", VanillaDepthFindsCorpsUnderTheArmy),
            ("army children union division child cascades a role to every brigade", UnionDivisionChildCascadesARoleToEveryBrigade),
            ("army children csa brigade children each own a leaf assignment", CsaBrigadeChildrenEachOwnALeafAssignment),
            ("army children union defender line holds with a flank guard and leaves the detached reserve division to the tree default", UnionDefenderLineHoldsWithAFlankGuard),
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

    // includenonactiveunits:false (119857): a reinforcement group that has not arrived (its object is
    // inactive) is not on the field, so it holds no role until it is; a detached one is the same.
    private static void AGroupNotYetOnTheFieldIsNotAChild()
    {
        var b = new Hierarchy();
        b.Add(1, 16, UnitFlags.None, 11, 12, 20, 21);
        b.Add(11, 14, UnitFlags.PermanentlyDetached, 20);
        b.Add(12, 14, UnitFlags.PermanentlyDetached, 21);
        b.Add(20, TacticalUnitType.Infantry, UnitFlags.None);
        b.Add(21, TacticalUnitType.Infantry, UnitFlags.None);
        b.ClearFlag(12, UnitFlags.Active);
        b.Assemble();
        var children = b.Discover(-1);
        AssertEqual(1, children.Count, "the inactive brigade is not offered");
        AssertEqual("child-11", children[0].ChildId, "the one on the field is");
    }

    private static void SkirmisherAndScoutCompaniesAreNotChildren()
    {
        var b = new Hierarchy();
        b.Add(1, 16, UnitFlags.None, 11, 12, 20, 21);
        b.Add(11, 14, UnitFlags.None, 20);
        b.Add(20, TacticalUnitType.Skirmisher, UnitFlags.None);
        b.Add(21, TacticalUnitType.Scout, UnitFlags.None);
        b.Add(12, 14, UnitFlags.None);
        b.Assemble();
        var probes = new List<DirectChildDiscovery.RegimentProbe>();
        BattleSnapshotAssembler.ToRegimentProbes(b.Snapshot, null, -1, false, probes);
        AssertFalse(probes.Find(p => p.InstanceId == 20).IsDirectChild, "a skirmisher company is not a direct child");
        AssertFalse(probes.Find(p => p.InstanceId == 21).IsDirectChild, "a scout company is not a direct child");
        AssertTrue(probes.Find(p => p.InstanceId == 11).IsDirectChild, "its brigade is");
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

    // Whiskey and Lemons (commandhierarchyshift -1, ClampShiftedMin 13): Army(16) -> Division(15)
    // -> Brigade(14) -> regiments (leaf unittyp 0). The player commands one regiment inside
    // brigade 1st_Brigade; the brigade, its division and the army above are AI
    // (AncestorOfPlayerCommand, never PlayerOwned) and keep their place in the roster; only the
    // player's own subtree leaves it. The sibling brigade and the player's brigade-mates stay.
    private static void WlDepthKeepsThePlayersAiBrigadeDivisionAndArmy()
    {
        const UnitFlags aiAncestor = UnitFlags.AncestorOfPlayerCommand | UnitFlags.PermanentlyDetached;
        var h = new Hierarchy();
        h.Add(1, 16, aiAncestor, 10, 20, 21, 22, 30, 31);
        h.Add(10, 15, aiAncestor, 20, 21, 22, 30);
        h.Add(20, 14, aiAncestor, 21, 22);          // the player's brigade
        h.Add(30, 14, UnitFlags.PermanentlyDetached, 31); // a sibling brigade
        h.Add(21, TacticalUnitType.Infantry, UnitFlags.PlayerOwned | UnitFlags.PlayerCommand);
        h.Add(22, TacticalUnitType.Infantry, UnitFlags.None);
        h.Add(31, TacticalUnitType.Infantry, UnitFlags.None);
        h.Assemble();
        var children = h.Discover(-1, excludePlayerOwned: true);
        AssertEqual(1, children.Count, "the division is the army's one child");
        AssertEqual("child-10", children[0].ChildId, "an AI ancestor of the player's regiment is not dropped");
        AssertEqual("army-1", children[0].ParentArmyId, "the army above the player's chain is still the root");

        // The same army with brigades directly under it (the CSA side): the player's brigade
        // is a child although the player sits inside it.
        var csa = new Hierarchy();
        csa.Add(1, 16, UnitFlags.AncestorOfPlayerCommand, 20, 30, 21, 22, 31);
        csa.Add(20, 14, UnitFlags.AncestorOfPlayerCommand | UnitFlags.PermanentlyDetached, 21, 22);
        csa.Add(30, 14, UnitFlags.PermanentlyDetached, 31);
        csa.Add(21, TacticalUnitType.Infantry, UnitFlags.PlayerOwned | UnitFlags.PlayerCommand);
        csa.Add(22, TacticalUnitType.Infantry, UnitFlags.None);
        csa.Add(31, TacticalUnitType.Infantry, UnitFlags.None);
        csa.Assemble();
        var csaChildren = csa.Discover(-1, excludePlayerOwned: true);
        AssertEqual(2, csaChildren.Count, "both brigades are children");
        AssertTrue(csaChildren.Any(c => c.ChildId == "child-20"), "the brigade the player sits in stays a child");
        AssertTrue(csaChildren.All(c => c.ChildId.StartsWith("child-")), "no synthetic army child");
    }

    // Default game (commandhierarchyshift 0, ClampShiftedMin 14): Army(16) -> Corps(15) ->
    // Division(14) -> Brigade(13, the combat block) -> regiments. The army's children are
    // the corps; the 13-type brigades are below the command window.
    private static void VanillaDepthFindsCorpsUnderTheArmy()
    {
        var h = new Hierarchy();
        h.Add(1, 16, UnitFlags.None, 10, 11, 20, 21, 30, 31, 40, 41);
        h.Add(10, 15, UnitFlags.PermanentlyDetached, 20, 21, 30, 31);
        h.Add(11, 15, UnitFlags.PermanentlyDetached, 40, 41);
        h.Add(20, 14, UnitFlags.None, 30);
        h.Add(21, 14, UnitFlags.None, 31);
        h.Add(40, 14, UnitFlags.None, 41);
        h.Add(30, 13, UnitFlags.None);
        h.Add(31, 13, UnitFlags.None);
        h.Add(41, 13, UnitFlags.None);
        h.Assemble();
        var children = h.Discover(0);
        AssertEqual(2, children.Count, "two corps");
        AssertTrue(children.All(c => c.ChildId == "child-10" || c.ChildId == "child-11"), "the corps are the children");
    }

    // Discovery -> registration -> allocation -> leaf map, on the Union evidence: the division is
    // the one direct child and its four brigades each get a leaf role (smoke: the army was the
    // one child and "synth-army--221568=Reserve:4" parked the whole army).
    private static void UnionDivisionChildCascadesARoleToEveryBrigade()
    {
        var h = Union(UnitFlags.PermanentlyDetached);
        for (int b = 0; b < 4; b++) h.SetPosition(11 + b, 100f * b, 0f);
        h.Assemble();
        var army = ArmyWithPlan(h.Discover(-1));
        h.Cascade(army);
        var map = army.CurrentLeafBrigadeMap;
        AssertEqual(4, map.Count, "one leaf assignment per brigade");
        for (int b = 0; b < 4; b++)
            AssertTrue(map.ContainsKey(11 + b), "brigade " + (11 + b) + " has an assignment");
        AssertTrue(map.Values.Select(a => a.LeafRole).Distinct().Count() > 1, "the brigades are not all one role: " + string.Join(",", map.Values.Select(a => a.LeafRole)));
        AssertTrue(map.Values.All(a => a.CascadeChain.Count >= 2), "the division's role cascades down to each brigade");
    }

    private static void CsaBrigadeChildrenEachOwnALeafAssignment()
    {
        var h = new Hierarchy();
        h.Add(100, 16, UnitFlags.None, 110, 111, 112, 120, 121, 122);
        h.Add(200, 16, UnitFlags.None, 210, 211, 220, 221);
        for (int b = 0; b < 3; b++) h.Add(110 + b, 14, UnitFlags.PermanentlyDetached, 120 + b);
        for (int b = 0; b < 3; b++) h.Add(120 + b, TacticalUnitType.Infantry, UnitFlags.None);
        for (int b = 0; b < 2; b++) h.Add(210 + b, 14, UnitFlags.PermanentlyDetached, 220 + b);
        for (int b = 0; b < 2; b++) h.Add(220 + b, TacticalUnitType.Infantry, UnitFlags.None);
        int[] brigades = { 110, 111, 112, 210, 211 };
        for (int i = 0; i < brigades.Length; i++) h.SetPosition(brigades[i], 80f * i, 0f);
        h.Assemble();
        var army = ArmyWithPlan(h.Discover(-1));
        h.Cascade(army);
        var map = army.CurrentLeafBrigadeMap;
        AssertEqual(5, map.Count, "every brigade of both armies has a leaf assignment");
        foreach (int id in brigades) AssertTrue(map.ContainsKey(id), "brigade " + id + " is commanded");
        AssertTrue(map.Values.Any(a => a.LeafRole == DirectChildRole.Main), "one brigade carries the main effort");
        AssertTrue(map.Values.Select(a => a.LeafRole).Distinct().Count() > 1, "roles differ by brigade, not one role for the army");
    }

    // The Union side's real shape (tree row 16:1,15:2,14:6): Department(16) -> 1st_Division(15) -> four
    // brigades, plus a second division (15) with two brigades that sits in no army (noParent=2 in the
    // probe health row). As the Defender: the army's one child is the first division, its brigades all
    // hold or guard (the stance guard turns attack, support and fix tasks into HoldObjective), the
    // left wing brigade guards the flank, and the second division is not a child: it gets the command
    // tree's default (Reserve/Hold, the log's node--225274), not a role from the allocator.
    // Pinned as delivered today; a reserve brigade inside a holding division is a later slice (BC16).
    private static void UnionDefenderLineHoldsWithAFlankGuard()
    {
        var h = new Hierarchy();
        h.Add(1, 16, UnitFlags.PermanentlyDetached, 10, 11, 12, 13, 14, 20, 21, 22, 23);
        h.Add(10, 15, UnitFlags.PermanentlyDetached, 11, 12, 13, 14, 20, 21, 22, 23);
        for (int b = 0; b < 4; b++) h.Add(11 + b, 14, UnitFlags.PermanentlyDetached, 20 + b);
        for (int b = 0; b < 4; b++) h.Add(20 + b, TacticalUnitType.Infantry, UnitFlags.None);
        h.Add(30, 15, UnitFlags.PermanentlyDetached, 31, 32, 40, 41);
        h.Add(31, 14, UnitFlags.PermanentlyDetached, 40);
        h.Add(32, 14, UnitFlags.PermanentlyDetached, 41);
        h.Add(40, TacticalUnitType.Infantry, UnitFlags.None);
        h.Add(41, TacticalUnitType.Infantry, UnitFlags.None);
        for (int b = 0; b < 4; b++) h.SetPosition(11 + b, 100f * b, 0f);
        h.SetPosition(31, 900f, 0f);
        h.SetPosition(32, 1000f, 0f);
        h.Assemble();
        var children = h.Discover(-1);
        AssertEqual(1, children.Count, "the army's one child is the first division");
        AssertEqual("child-10", children[0].ChildId, "1st_Division");

        var army = new ArmyOrchestrator(0, BuiltInPlaybooks.SeedCatalog(), default(WhiskeyRealism.Strategic.PersonalityVector));
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.PreparedDefense, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7));
        army.UpdateBattleStance(BattleStance.Defender);
        army.RegisterDirectChildren(children);
        army.ObserveGlobalOdds(0.7f);
        army.ObserveDirectChildEvidenceWithZones(new[] { new DirectChildEvidence(2, 2, true, 0, 0, 0.9f) }, null, null);
        h.Cascade(army);
        var map = army.CurrentLeafBrigadeMap;
        AssertEqual(4, map.Count, "the four brigades of the first division, not the second division's");
        AssertFalse(map.ContainsKey(31) || map.ContainsKey(32), "the division in no army is left to the tree default");
        var tasks = map.Values.Select(a => PostureStanceGuard.Task(a.LeafTask, BattleStance.Defender)).ToList();
        AssertTrue(tasks.All(t => t == CommandTaskType.HoldObjective || t == CommandTaskType.GuardFlank || t == CommandTaskType.ReserveWait),
            "a Defender's brigades hold, guard a flank or wait: " + string.Join(",", tasks));
        AssertTrue(tasks.Contains(CommandTaskType.GuardFlank), "one brigade guards the open flank");
    }

    private static ArmyOrchestrator ArmyWithPlan(IReadOnlyList<DirectChildSnapshot> children)
    {
        var army = new ArmyOrchestrator(0, BuiltInPlaybooks.SeedCatalog(), default(WhiskeyRealism.Strategic.PersonalityVector));
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.GenericMethodical, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7));
        army.RegisterDirectChildren(children);
        army.ObserveGlobalOdds(1.2f);
        var evidence = new DirectChildEvidence[children.Count];
        for (int i = 0; i < evidence.Length; i++) evidence[i] = new DirectChildEvidence(2 + (i % 2), 1, false, i % 3, 0, 0.9f);
        army.ObserveDirectChildEvidenceWithZones(evidence, null, null);
        return army;
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

        internal void SetPosition(int id, float x, float z)
        {
            int i = _records.FindIndex(r => r.InstanceId == id);
            var r = _records[i];
            r.X = x;
            r.Z = z;
            _records[i] = r;
        }

        internal void Cascade(ArmyOrchestrator army)
        {
            var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>();
            BattleSnapshotAssembler.ToExtendedProbes(_snapshot, null, -1, probes);
            army.UpdateLeafBrigadeMap(TacticalCommandTreeProbe.BuildTree(probes, -1));
        }

        internal BattleSnapshot Snapshot => _snapshot;

        internal void ClearFlag(int id, UnitFlags flag)
        {
            int i = _records.FindIndex(r => r.InstanceId == id);
            var r = _records[i];
            r.Flags &= ~flag;
            _records[i] = r;
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
