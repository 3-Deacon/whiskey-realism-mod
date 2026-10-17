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
            ("army children union defender line holds with a flank guard and covers the loose division", UnionDefenderLineHoldsWithAFlankGuard),
            ("army children a holding division keeps floor(n/3) brigades in reserve, rearmost interior first", AHoldingDivisionKeepsAThirdInReserve),
            ("army children a holding division with fewer than three brigades says the numbers are thin", AHoldingDivisionWithTwoBrigadesSaysThin),
            ("army children an exposed end of a holding division draws the nearest reserve to guard the flank", AnExposedEndDrawsTheNearestReserve),
            ("army children a Fix command keeps its own reserve", AFixParentKeepsItsOwnReserve),
            ("army children the anchor of a holding command is never the one held back", TheAnchorIsNeverHeldBack),
            ("army children an attacking division keeps no cascade reserve", AnAttackingDivisionKeepsNoCascadeReserve),
            ("army children the orchestrator's defender leaf map keeps a reserve and rebuilds on a stance change", TheOrchestratorDefenderLeafMapKeepsAReserve),
            ("army children a division in no army is a direct child beside the army's divisions", ALooseDivisionIsADirectChildBesideTheArmysDivisions),
            ("army children a side of two armies and a loose corps covers every top command", ASideOfTwoArmiesAndALooseCorpsCoversEveryTopCommand),
            ("army children only a command-level top with no parent group is a loose child", OnlyACommandLevelTopWithNoParentGroupIsALooseChild),
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

    // The command tree roots every top command under one synthetic side root (CommandTreeBuilder),
    // so the side's direct children cover every top command: an army's children, and a command-level
    // unit below the army tier that sits in no army (the Union's second division, noParent=2 in the
    // probe health row) as a child itself, a peer of the army's divisions.
    private static void ALooseDivisionIsADirectChildBesideTheArmysDivisions()
    {
        var h = UnionWithLooseDivision();
        var children = h.Discover(-1);
        AssertEqual(2, children.Count, "the army's division and the loose division");
        AssertEqual("child-10", children[0].ChildId, "the army's division first");
        AssertEqual("child-30", children[1].ChildId, "the loose division is itself a child");
        AssertEqual(15, children[1].RawUnitTyp, "a division, not its brigades");
        AssertTrue(children.All(c => !c.ChildId.StartsWith("synth-")), "no synthetic child");
        AssertEqual("army-1", children[1].ParentArmyId, "attached to the side's army for the log label");
    }

    private static Hierarchy UnionWithLooseDivision()
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
        return h;
    }

    // CSA side 1: Potomac and Shenandoah (16) hold brigades; a corps (15) sits in neither.
    private static void ASideOfTwoArmiesAndALooseCorpsCoversEveryTopCommand()
    {
        var h = new Hierarchy();
        h.Add(100, 16, UnitFlags.None, 110, 111, 120, 121);
        h.Add(200, 16, UnitFlags.None, 210, 220);
        h.Add(300, 15, UnitFlags.PermanentlyDetached, 310, 311, 320, 321);
        h.Add(110, 14, UnitFlags.PermanentlyDetached, 120);
        h.Add(111, 14, UnitFlags.PermanentlyDetached, 121);
        h.Add(210, 14, UnitFlags.PermanentlyDetached, 220);
        h.Add(310, 14, UnitFlags.PermanentlyDetached, 320);
        h.Add(311, 14, UnitFlags.PermanentlyDetached, 321);
        foreach (int id in new[] { 120, 121, 220, 320, 321 }) h.Add(id, TacticalUnitType.Infantry, UnitFlags.None);
        h.Assemble();
        var children = h.Discover(-1);
        var ids = children.Select(c => c.ChildId).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        AssertEqual("child-110,child-111,child-210,child-300", string.Join(",", ids), "both armies' brigades and the loose corps");
        AssertEqual("army-100", children.Single(c => c.ChildId == "child-110").ParentArmyId, "first army's brigade");
        AssertEqual("army-200", children.Single(c => c.ChildId == "child-210").ParentArmyId, "second army's brigade");
    }

    private static void OnlyACommandLevelTopWithNoParentGroupIsALooseChild()
    {
        var probes = new[]
        {
            new DirectChildDiscovery.RegimentProbe(1, 16, "Army", true, 0, false),
            new DirectChildDiscovery.RegimentProbe(10, 15, "Corps", true, 1, true),
            new DirectChildDiscovery.RegimentProbe(30, 15, "Loose corps", true, 0, false),
            new DirectChildDiscovery.RegimentProbe(31, 14, "Brigade of loose corps", true, 30, true),
            new DirectChildDiscovery.RegimentProbe(40, 15, "Unit under an unknown parent", true, 999, false),
            new DirectChildDiscovery.RegimentProbe(50, 15, "Not on the field", false, 0, false),
            new DirectChildDiscovery.RegimentProbe(60, 12, "Regiment below the window", true, 0, false),
        };
        var children = DirectChildDiscovery.Probe(probes, commandHierarchyShift: 0);
        AssertEqual("child-10,child-30", string.Join(",", children.Select(c => c.ChildId)), "the army's corps and the loose corps only");
    }

    // The Union side's real shape (tree row 16:1,15:2,14:6): Department(16) -> 1st_Division(15) -> four
    // brigades, plus a loose division (15) with two brigades. As the Defender the two divisions are the
    // side's two children; their six brigades all get a leaf assignment, and a Defender's tasks hold or
    // guard (the stance guard turns attack, support and fix tasks into HoldObjective).
    private static void UnionDefenderLineHoldsWithAFlankGuard()
    {
        var h = UnionWithLooseDivision();
        var children = h.Discover(-1);
        AssertEqual(2, children.Count, "the army's division and the loose division");
        var army = new ArmyOrchestrator(0, BuiltInPlaybooks.SeedCatalog(), default(WhiskeyRealism.Strategic.PersonalityVector));
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.PreparedDefense, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7));
        army.UpdateBattleStance(BattleStance.Defender);
        army.RegisterDirectChildren(children);
        army.ObserveGlobalOdds(0.7f);
        army.ObserveDirectChildEvidenceWithZones(new[]
        {
            new DirectChildEvidence(2, 2, true, 0, 0, 0.9f),
            new DirectChildEvidence(1, 2, false, 4, 0, 0.9f),
        }, null, null);
        h.Cascade(army);
        var map = army.CurrentLeafBrigadeMap;
        AssertEqual(6, map.Count, "the six brigades of both divisions");
        AssertTrue(map.ContainsKey(31) && map.ContainsKey(32), "the loose division's brigades are commanded");
        var tasks = map.Values.Select(a => PostureStanceGuard.Task(a.LeafTask, BattleStance.Defender)).ToList();
        AssertTrue(tasks.All(t => t == CommandTaskType.HoldObjective || t == CommandTaskType.GuardFlank || t == CommandTaskType.ReserveWait),
            "a Defender's brigades hold, guard a flank or wait: " + string.Join(",", tasks));
    }

    // ---- the Defender's reserve inside a holding division (user, 2026-10-17: of 6 brigades 4 on the line and
    // 1-2 in reserve; when the numbers do not allow it the trace says so). The rule is the deployment
    // doctrine's own: floor(N / 3) of N >= 3 (DeploymentDoctrine.ProposeReserve), the farthest from the
    // enemy first, never the anchor of the line. ----

    // One division of n brigades in a row across the front (facing +Z), the later ones farther back.
    private static (IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree, int divisionId) DivisionOf(int n)
    {
        var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>
        {
            new TacticalCommandTreeProbe.ExtendedProbe(10, 15, "Division", true, 0, true, 0f, 0f, 1),
        };
        for (int b = 0; b < n; b++)
            probes.Add(new TacticalCommandTreeProbe.ExtendedProbe(100 + b, 14, "Brigade" + b, true, 10, true, 100f * b, -10f * b, 1));
        return (TacticalCommandTreeProbe.BuildTree(probes, -1), 10);
    }

    private static TacticalFrontAxis FacingPlusZ() => TacticalFrontAxis.FromCentroids(0f, 0f, 0f, 1000f).WithOwnExtent(0f, 500f, 5);

    private static IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> HoldingMap(int n, bool defender, List<DefenderReserveRecord> report, float aggression01 = 0.5f,
        DirectChildRole parentRole = DirectChildRole.Main)
    {
        var (tree, id) = DivisionOf(n);
        return TacticalLeafBrigadeMap.BuildMap(tree, new[] { new TacticalLeafBrigadeMap.TopAssignment(id, parentRole) }, aggression01,
            CascadeEnvelopmentMode.None, FacingPlusZ(), null, defender, report);
    }

    private static void AHoldingDivisionKeepsAThirdInReserve()
    {
        foreach (var (n, reserves) in new[] { (3, 1), (4, 1), (5, 1), (6, 2), (9, 3) })
        {
            var report = new List<DefenderReserveRecord>();
            var map = HoldingMap(n, defender: true, report);
            var reserved = map.Values.Where(a => a.LeafRole == DirectChildRole.Reserve).Select(a => a.InstanceId).ToList();
            AssertEqual(reserves, reserved.Count, n + " brigades keep " + reserves + " in reserve: " + string.Join(",", map.Values.OrderBy(a => a.InstanceId).Select(a => a.InstanceId + ":" + a.LeafRole)));
            AssertEqual(n, map.Count, "every brigade keeps an assignment");
            AssertTrue(map.Values.Count(a => a.LeafRole == DirectChildRole.Main) == 1, "exactly one anchor of the line");
            AssertTrue(map.Values.All(a => a.LeafRole != DirectChildRole.Reserve || a.LeafTask == CommandTaskType.ReserveWait), "a reserve waits");
            AssertEqual(1, report.Count, "one trace record for the division");
            AssertEqual(n, report[0].Children, "the record names the numbers");
            AssertEqual(reserves, report[0].Held, "and what it holds back");
            AssertEqual("reserved", report[0].Reason, "and why");
        }
        // An aggressive commander widens the support band to two, so with five brigades every one is SupportMain or the anchor
        // and the two ends are candidates too; the interior ones still go first (the ends hold the line's wings).
        var five = HoldingMap(5, defender: true, null, aggression01: 0.9f);
        AssertEqual(1, five.Values.Count(a => a.LeafRole == DirectChildRole.Reserve), "five brigades keep one back");
        AssertTrue(five[100].LeafRole != DirectChildRole.Reserve && five[104].LeafRole != DirectChildRole.Reserve,
            "even when the ends are candidates the interior goes first: " + string.Join(",", five.Values.OrderBy(a => a.InstanceId).Select(a => a.InstanceId + ":" + a.LeafRole)));
        // Rearmost first: with six brigades the interior brigades farthest back (the highest index here) go to reserve.
        var six = HoldingMap(6, defender: true, null);
        AssertEqual(DirectChildRole.Reserve, six[104].LeafRole, "the rearmost interior brigade is in reserve");
        AssertTrue(six[100].LeafRole != DirectChildRole.Reserve && six[105].LeafRole != DirectChildRole.Reserve, "the wings keep the line's ends");
    }

    // The reserve is taken inside every holding command, not only a Main one: a Fix parent (the cascade gives its
    // children Fix / Refuse roles) keeps floor(N/3) back too (verify V12).
    private static void AFixParentKeepsItsOwnReserve()
    {
        var report = new List<DefenderReserveRecord>();
        var map = HoldingMap(6, defender: true, report, parentRole: DirectChildRole.Fix);
        AssertEqual(2, map.Values.Count(a => a.LeafRole == DirectChildRole.Reserve), "a Fix division of six keeps two back: "
            + string.Join(",", map.Values.OrderBy(a => a.InstanceId).Select(a => a.InstanceId + ":" + a.LeafRole)));
        AssertEqual(6, map.Count, "every brigade keeps an assignment");
        AssertEqual("reserved", report[0].Reason, "and the trace says why");
        var attacker = HoldingMap(6, defender: false, null, parentRole: DirectChildRole.Fix);
        AssertEqual(0, attacker.Values.Count(a => a.LeafRole == DirectChildRole.Reserve), "an attacker's Fix division holds none back");
    }

    // The anchor is the command's own main effort: with a SupportMain parent, three brigades and a bold commander the
    // anchor must not be the one held back (verify F6: anchor 101 landed in Reserve).
    private static void TheAnchorIsNeverHeldBack()
    {
        foreach (var parentRole in new[] { DirectChildRole.Main, DirectChildRole.SupportMain })
            foreach (float aggression in new[] { 0.1f, 0.5f, 0.9f })
                foreach (int n in new[] { 3, 4, 5, 6, 9 })
                {
                    var map = HoldingMap(n, defender: true, null, aggression01: aggression, parentRole: parentRole);
                    // The anchor is the one child the cascade names for the parent's own role at the narrowest band (Main under a Main
                    // parent, the lone SupportMain under a SupportMain one); the attacker's map shows it.
                    var narrow = HoldingMap(n, defender: false, null, aggression01: 0.1f, parentRole: parentRole);
                    var anchors = narrow.Values.Where(a => a.LeafRole == parentRole).Select(a => a.InstanceId).ToList();
                    AssertEqual(1, anchors.Count, "the cascade names one anchor for " + n + " brigades under " + parentRole);
                    AssertTrue(map[anchors[0]].LeafRole != DirectChildRole.Reserve,
                        "anchor " + anchors[0] + " is not held back (n=" + n + ", aggression " + aggression + ", parent " + parentRole + "): "
                        + string.Join(",", map.Values.OrderBy(a => a.InstanceId).Select(a => a.InstanceId + ":" + a.LeafRole)));
                }
    }

    // A flank column perceived on the right end of the line (the cascade's flank-threat input, vanilla's 0-4 scale):
    // the nearest reserve goes to guard that end instead of sitting behind the line.
    private static void AnExposedEndDrawsTheNearestReserve()
    {
        var (tree, id) = DivisionOf(6);
        var calm = new List<DefenderReserveRecord>();
        var tops = new[] { new TacticalLeafBrigadeMap.TopAssignment(id, DirectChildRole.Main) };
        var held = TacticalLeafBrigadeMap.BuildMap(tree, tops, 0.5f, CascadeEnvelopmentMode.None, FacingPlusZ(), null, true, calm);
        AssertEqual(2, held.Values.Count(a => a.LeafRole == DirectChildRole.Reserve), "calm: two held");
        var threat = new Dictionary<int, float> { { 105, 4f } };
        var exposed = new List<DefenderReserveRecord>();
        var map = TacticalLeafBrigadeMap.BuildMap(tree, tops, 0.5f, CascadeEnvelopmentMode.None, FacingPlusZ(), threat, true, exposed);
        AssertEqual(1, map.Values.Count(a => a.LeafRole == DirectChildRole.Reserve), "one reserve left behind the line");
        AssertEqual(DirectChildRole.RefuseRight, map[105].LeafRole, "the right end guards its flank");
        AssertTrue(map.Values.Count(a => a.LeafRole == DirectChildRole.RefuseRight) >= 2, "a reserve went to the right: " + string.Join(",", map.Values.OrderBy(a => a.InstanceId).Select(a => a.InstanceId + ":" + a.LeafRole)));
        AssertEqual(DefenderReserveRecord.ReleasedToFlank, exposed[0].Reason, "the trace says it was released to the flank");
        AssertEqual(1, exposed[0].Held, "and what is still held");
    }

    private static void AHoldingDivisionWithTwoBrigadesSaysThin()
    {
        var report = new List<DefenderReserveRecord>();
        var map = HoldingMap(2, defender: true, report);
        AssertEqual(0, map.Values.Count(a => a.LeafRole == DirectChildRole.Reserve), "two brigades keep no reserve");
        AssertEqual(1, report.Count, "the trace says so");
        AssertEqual("thin-numbers", report[0].Reason, "thin numbers");
        AssertEqual(2, report[0].Children, "of two brigades");
        AssertEqual(0, report[0].Held, "none held");
    }

    private static void AnAttackingDivisionKeepsNoCascadeReserve()
    {
        var report = new List<DefenderReserveRecord>();
        var map = HoldingMap(6, defender: false, report);
        AssertEqual(0, map.Values.Count(a => a.LeafRole == DirectChildRole.Reserve), "the attacker's cascade is unchanged");
        AssertEqual(0, report.Count, "and writes no reserve record");
    }

    private static void TheOrchestratorDefenderLeafMapKeepsAReserve()
    {
        var h = UnionWithLooseDivision();
        var children = h.Discover(-1);
        var army = new ArmyOrchestrator(0, BuiltInPlaybooks.SeedCatalog(), default(WhiskeyRealism.Strategic.PersonalityVector));
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.PreparedDefense, BattlePhase.MainEffort, 0, null, null, 1.4f, 0f, 7));
        army.RegisterDirectChildren(children);
        army.ObserveGlobalOdds(0.7f);
        army.ObserveDirectChildEvidenceWithZones(new[]
        {
            new DirectChildEvidence(2, 2, true, 0, 0, 0.9f),
            new DirectChildEvidence(1, 2, false, 4, 0, 0.9f),
        }, null, null);
        h.Cascade(army);
        AssertEqual(0, army.CurrentLeafBrigadeMap.Values.Count(a => a.LeafRole == DirectChildRole.Reserve && a.InstanceId >= 11 && a.InstanceId <= 14),
            "as a Meeting-engagement army the cascade keeps no reserve inside the four-brigade division");
        army.UpdateBattleStance(BattleStance.Defender);
        h.Cascade(army);
        var map = army.CurrentLeafBrigadeMap;
        int reserved = map.Values.Count(a => a.LeafRole == DirectChildRole.Reserve && a.InstanceId >= 11 && a.InstanceId <= 14);
        AssertEqual(1, reserved, "the Defender's four-brigade division keeps one brigade in reserve");
        AssertTrue(army.CurrentDefenderReserveReport.Count >= 1 && army.CurrentDefenderReserveReport.Any(r => r.Children == 4 && r.Held == 1),
            "the orchestrator exposes the trace");
        AssertTrue(army.CurrentDefenderReserveReport.Any(r => r.Children == 2 && r.Reason == "thin-numbers"),
            "and says the loose division of two is too thin to hold one back");
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
