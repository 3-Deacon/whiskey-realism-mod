using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;

// BC14W task 6: the stored products under the works hold (spec § 7A requirement-works-hold, "The stored-product guard"): the command states guarded in place,
// the doctrine orders guarded into the army's own array (the ledger's list stays as built), the leaf map copy, and the fallback state.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksOrderTests() => new (string, Action)[]
    {
        ("bc14w WORKS-ORDER the store guards the copied states, builds the orders, then guards them into the army's own array; the ledger's list stays unguarded", WOrder),
        ("bc14w WORKS-LEAF-MAP the guarded leaf map is reused while the leaf signature and the hold hash stand, and rebuilt when either changes", WLeafMap),
        ("bc14w WORKS-EXPLOIT an exploit that is not a breach and aims at held works is built as a hold with its followers; a breach and one aimed off the works are not", WExploit),
        ("bc14w WORKS-FALLBACK a state built for a group with no stored state is guarded with the side's context, and TryResolveState calls it", WFallback),
    };

    // A lookup that answers the works hold from a script (the ledger's role, without an evaluation).
    sealed class HoldLookup : IFeasibilityLookup
    {
        public bool Hold;
        public string Reason = "hopeless-assault";
        public int Hash = 1;
        public readonly HashSet<string> Faces = new HashSet<string>(StringComparer.Ordinal);

        public BattlePlanId SelectedPlan => BattlePlanId.Unknown;
        public string BrokenAssumption => null;
        public int ThreatCount => 0;
        public int LookRequests(Span<float> xs, Span<float> zs) => 0;
        public double EvaluatedHours => 0.0;
        public bool HasEvaluation => false;
        public bool TryPredictedArrival(string childId, out double arrivalHours, out double marchHours) { arrivalHours = marchHours = 0.0; return false; }
        public bool TryThreat(int index, out CrossingThreat threat, float[] ownShiftByChild, out int childCount) { threat = default(CrossingThreat); childCount = 0; return false; }
        public string ThreatChildId(int column) => null;
        public void CaptureSelection(BattlePlanId plan, bool mirrored) { }
        public bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ, out float attackViaX, out float attackViaZ)
        {
            releaseHours = timeBudgetHours = 0.0;
            viaX = viaZ = attackViaX = attackViaZ = float.NaN;
            return false;
        }
        public bool TryViaRegions(string childId, out int viaFarRegion, out int attackViaFarRegion) { viaFarRegion = attackViaFarRegion = -1; return false; }
        public bool TryGet(BattlePlanId plan, out CandidateResult result) { result = default(CandidateResult); return false; }
        public bool HasFocal;
        public FocalPoint Focal;
        public bool TryFocalPoint(out FocalPoint focal) { focal = Focal; return HasFocal; }
        public bool WorksHold(out string reason) { reason = Hold ? Reason : null; return Hold; }
        public bool AssaultsWorks(string childId) => Faces.Contains(childId);
        public bool FacesWorks(string childId) => Faces.Contains(childId);
        public int WorksHoldHash => Hash;
        public IWorksQuery HeldWorks => null;
    }

    // An army with two corps (child-2, child-3), each with one brigade (node-10 under node-2, node-20 under node-3).
    static ArmyOrchestrator TwoCorpsArmy(HoldLookup lookup)
    {
        var army = new ArmyOrchestrator(0, new TacticalPlaybookCatalog(), default(PersonalityVector));
        army.SetPlanForTesting(new TacticalBattlePlan(BattlePlanId.DoubleEnvelopment, BattlePhase.MainEffort, 2, new int[0], new int[0], 1.2f, 0f, 0));
        army.SetFeasibility(lookup);
        army.RegisterDirectChildren(new[]
        {
            new DirectChildSnapshot("child-2", "army", 15, 0, "corps-a", true),
            new DirectChildSnapshot("child-3", "army", 15, 0, "corps-b", true),
        });
        army.RegisterCommandTree(new CommandTreeSnapshot(0, "node-1", new[]
        {
            new CommandNodeSnapshot("node-1", "", 1, 0, 0, 16, 0, "army", true, false, 0),
            new CommandNodeSnapshot("node-2", "node-1", 2, 1, 0, 15, 0, "corps-a", true, false, 1),
            new CommandNodeSnapshot("node-3", "node-1", 3, 1, 0, 15, 0, "corps-b", true, false, 1),
            new CommandNodeSnapshot("node-10", "node-2", 10, 2, 0, 14, 0, "brigade-a", true, false, 2),
            new CommandNodeSnapshot("node-20", "node-3", 20, 3, 0, 14, 0, "brigade-b", true, false, 2),
        }, 2, 0, string.Empty));
        return army;
    }

    static CommandNodeOperationalState[] FourStates() => new[]
    {
        new CommandNodeOperationalState("node-2", CommandEchelonKind.CorpsLike, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, CommandTaskState.Planning),
        new CommandNodeOperationalState("node-10", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, CommandTaskState.Planning),
        new CommandNodeOperationalState("node-3", CommandEchelonKind.CorpsLike, CommandNodeRole.FixingForce, CommandTaskType.FixEnemy, CommandTaskState.Planning),
        new CommandNodeOperationalState("node-20", CommandEchelonKind.BrigadeLike, CommandNodeRole.FixingForce, CommandTaskType.FixEnemy, CommandTaskState.Planning),
    };

    static TacticalOperationsLedgerRuntime ActiveLedger()
    {
        var ledger = new TacticalOperationsLedgerRuntime();
        ledger.SetRuntimeClock(0f);
        ledger.Replace(TacticalCommanderMode.Active,
            new[] { ObjectiveRecordFor("ridge-a", enemyStrength: 1000f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"),
            new ForceAvailabilitySnapshot(3000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        return ledger;
    }

    static CommandDoctrineOrder OrderFor(IReadOnlyList<CommandDoctrineOrder> orders, string nodeId)
    {
        for (int i = 0; i < orders.Count; i++) if (orders[i].NodeId == nodeId) return orders[i];
        throw new Exception("no order for " + nodeId);
    }

    static CommandNodeOperationalState StateFor(IReadOnlyList<CommandNodeOperationalState> states, string nodeId)
    {
        for (int i = 0; i < states.Count; i++) if (states[i].NodeId == nodeId) return states[i];
        throw new Exception("no state for " + nodeId);
    }

    static void WOrder()
    {
        var lookup = new HoldLookup { Hold = true };
        lookup.Faces.Add("child-2");
        ArmyOrchestrator army = TwoCorpsArmy(lookup);
        TacticalOperationsLedgerRuntime ledger = ActiveLedger();
        army.UpdateOperationsLedger(ledger, FourStates());

        // The states of the works-facing child (its node and its brigade) are guarded; the other child's are not.
        foreach (string node in new[] { "node-2", "node-10" })
        {
            CommandNodeOperationalState s = StateFor(army.CurrentCommandOperations, node);
            AssertEqual(CommandTaskType.HoldObjective, s.Task, node + " state is a hold");
            AssertEqual(WorksHoldGuard.Reason, s.Reason, node + " state names the guard");
            AssertEqual(CommandTaskType.AttackObjective, s.PreGuardTask, node + " state remembers the attack");
        }
        foreach (string node in new[] { "node-3", "node-20" })
        {
            CommandNodeOperationalState s = StateFor(army.CurrentCommandOperations, node);
            AssertEqual(CommandTaskType.FixEnemy, s.Task, node + " state is untouched");
            AssertTrue(s.Reason == null, node + " has no reason");
        }

        // The orders are built from the nodes' roles, then guarded into the army's own array; the ledger's own list stays as built.
        IReadOnlyList<CommandDoctrineOrder> mine = army.CurrentDoctrineOrders;
        AssertFalse(ReferenceEquals(mine, ledger.CurrentDoctrineOrders), "CurrentDoctrineOrders is the army's array, not the ledger's list");
        AssertEqual(ledger.CurrentDoctrineOrders.Count, mine.Count, "the same orders");
        foreach (string node in new[] { "node-2", "node-10" })
        {
            CommandDoctrineOrder own = OrderFor(mine, node), built = OrderFor(ledger.CurrentDoctrineOrders, node);
            AssertTrue(built.Task != CommandTaskType.HoldObjective || built.Reason != WorksHoldGuard.Reason, node + ": the ledger's list stays unguarded");
            AssertEqual(CommandTaskType.HoldObjective, own.Task, node + " order is a hold");
            AssertEqual(WorksHoldGuard.Reason, own.Reason, node + " order names the guard");
            AssertEqual(built.Task, own.PreGuardTask, node + " order remembers the task it was built with");
            AssertNear(built.PrimaryTarget.X, own.PrimaryTarget.X, 1e-4f, node + " keeps its target");
        }
        foreach (string node in new[] { "node-3", "node-20" })
            AssertEqual(OrderFor(ledger.CurrentDoctrineOrders, node).Task, OrderFor(mine, node).Task, node + " order is untouched");

        // BC11 holds a node: it is in the maneuver table, so the guard leaves it.
        var maneuverOrder = new ManeuverOrder { Phase = ManeuverPhase.March, Task = CommandTaskType.AttackObjective, TargetX = 5f, TargetZ = 6f, Heading = -1f, Reason = "march" };
        ArmyOrchestrator maneuvering = TwoCorpsArmy(lookup);
        maneuvering.SetManeuvers(new[] { new ChildManeuver("child-2", maneuverOrder, 1f, 0f) });
        maneuvering.UpdateOperationsLedger(ActiveLedger(), FourStates());
        AssertEqual(CommandTaskType.AttackObjective, StateFor(maneuvering.CurrentCommandOperations, "node-2").Task, "a maneuvering child's state passes the guard");
        AssertTrue(!(OrderFor(maneuvering.CurrentDoctrineOrders, "node-2").Reason == WorksHoldGuard.Reason), "and so does its order");

        // With the hold off, nothing is guarded and CurrentDoctrineOrders is the ledger's list as before.
        var off = new HoldLookup { Hold = false };
        off.Faces.Add("child-2");
        ArmyOrchestrator quiet = TwoCorpsArmy(off);
        TacticalOperationsLedgerRuntime quietLedger = ActiveLedger();
        quiet.UpdateOperationsLedger(quietLedger, FourStates());
        AssertEqual(CommandTaskType.AttackObjective, StateFor(quiet.CurrentCommandOperations, "node-2").Task, "no hold, no guard");
        AssertTrue(ReferenceEquals(quiet.CurrentDoctrineOrders, quietLedger.CurrentDoctrineOrders), "and the orders are the ledger's own");

        // With the flag off the lookup's hold is not asked.
        bool saved = WhiskeyRealism.Plugin.EnemyWorksDoctrine;
        try
        {
            WhiskeyRealism.Plugin.EnemyWorksDoctrine = false;
            ArmyOrchestrator flagOff = TwoCorpsArmy(lookup);
            flagOff.UpdateOperationsLedger(ActiveLedger(), FourStates());
            AssertEqual(CommandTaskType.AttackObjective, StateFor(flagOff.CurrentCommandOperations, "node-2").Task, "with Enable Enemy Works Doctrine off nothing is guarded");
        }
        finally { WhiskeyRealism.Plugin.EnemyWorksDoctrine = saved; }
    }

    static TacticalCommandTreeProbe.ExtendedProbe Probe(int id, int typ, string name, int parent, float x, float z)
        => new TacticalCommandTreeProbe.ExtendedProbe(id, typ, name, true, parent, parent == 1, x, z, 2);

    static void WLeafMap()
    {
        var lookup = new HoldLookup { Hold = true };
        lookup.Faces.Add("child-2");
        ArmyOrchestrator army = TwoCorpsArmy(lookup);
        army.ObserveDirectChildEvidenceWithZones(new[] { new DirectChildEvidence(3, 0, false, 2, 0, 0.9f), new DirectChildEvidence(3, 0, false, 2, 0, 0.9f) }, null, null);
        var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>
        {
            Probe(1, 16, "army", 0, 0f, 0f),
            Probe(2, 15, "corps-a", 1, -200f, 0f),
            Probe(3, 15, "corps-b", 1, 200f, 0f),
            Probe(10, 14, "brigade-a", 2, -200f, 0f),
            Probe(20, 14, "brigade-b", 3, 200f, 0f),
        };
        army.UpdateLeafBrigadeMap(TacticalCommandTreeProbe.BuildTree(probes, 0));

        // Whatever tasks the cascade gave, the leaf of the works-facing child that attacks is held and the other's is not.
        var raw = new Dictionary<int, TacticalLeafBrigadeMap.LeafAssignment>();
        lookup.Hold = false;
        foreach (var pair in army.CurrentLeafBrigadeMap) raw[pair.Key] = pair.Value;
        AssertTrue(raw.Count >= 2, "the cascade made the two leaves, got " + raw.Count);
        lookup.Hold = true;
        IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> guarded = army.CurrentLeafBrigadeMap;
        bool aAttacks = raw[10].LeafTask == CommandTaskType.AttackObjective || raw[10].LeafTask == CommandTaskType.SupportAttack || raw[10].LeafTask == CommandTaskType.FixEnemy;
        AssertEqual(aAttacks, guarded[10].WorksHeld, "the works-facing child's leaf is held exactly when it was attacking");
        if (aAttacks) AssertEqual(CommandTaskType.HoldObjective, guarded[10].LeafTask, "as a hold");
        AssertFalse(guarded[20].WorksHeld, "the other child's leaf is not held");
        AssertEqual(raw[20].LeafTask, guarded[20].LeafTask, "and keeps its task");
        AssertEqual(raw[10].LeafRole, guarded[10].LeafRole, "the role stays");
        AssertEqual(raw[10].TopLevelChildIndex, guarded[10].TopLevelChildIndex, "the top-level index stays");

        // The copy is reused while nothing changes.
        AssertTrue(ReferenceEquals(guarded, army.CurrentLeafBrigadeMap), "the same copy is returned");
        // A hold hash that changes rebuilds it (the set of children that face works moved).
        lookup.Faces.Add("child-3");
        lookup.Hash = 2;
        IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> rebuilt = army.CurrentLeafBrigadeMap;
        bool bAttacks = raw[20].LeafTask == CommandTaskType.AttackObjective || raw[20].LeafTask == CommandTaskType.SupportAttack || raw[20].LeafTask == CommandTaskType.FixEnemy;
        AssertEqual(bAttacks, rebuilt[20].WorksHeld, "after the hash changed the second child's leaf is held too when it attacks");
        // A new leaf signature rebuilds it too.
        probes[4] = Probe(20, 14, "brigade-b", 3, 260f, 40f);
        army.UpdateLeafBrigadeMap(TacticalCommandTreeProbe.BuildTree(probes, 0));
        AssertFalse(ReferenceEquals(rebuilt, army.CurrentLeafBrigadeMap) && army.CurrentLeafBrigadeMap[20].WorksHeld != bAttacks, "a changed tree is guarded afresh");
        // With the hold off the raw map is returned.
        lookup.Hold = false;
        AssertFalse(army.CurrentLeafBrigadeMap[10].WorksHeld, "with the hold off no leaf is held");
    }

    sealed class AimQuery : IWorksQuery
    {
        public float WorksFromZ = 500f;
        public bool OnHeldWorks(float x, float z) => z >= WorksFromZ;
        public bool InWorksBand(float x, float z) => z >= WorksFromZ - 100f;
    }

    static ExploitEntry ExploitAt(OpeningKind kind, float aimZ) => new ExploitEntry
    {
        Id = 1, AssetId = 50, OpeningId = 9, Kind = kind, Phase = ExploitPhase.Advance, AimX = 10f, AimZ = aimZ, Heading = 0f,
    };

    static void WExploit()
    {
        ExploitTable table = ExploitTable.ForAlliance(0);
        try
        {
            table.Clear(ExploitEnd.StandDown);
            var aim = new AimQuery();
            table.Add(ExploitAt(OpeningKind.IsolatedCluster, 600f));
            table.SetSubtree(1, new[] { 51, 52 }, new int[0]);

            // No view: the exploit is built as BC13 builds it.
            AssertTrue(table.TryOverride(50, out CommandDoctrineOrder plain, out CommandNodeOperationalState plainState), "the asset has an order");
            AssertEqual(CommandTaskType.AttackObjective, plain.Task, "an attack");
            AssertEqual(CommandTaskType.AttackObjective, plainState.Task, "and a state to match");

            // The hold is on and the aim lies on held works: the asset and its followers are built as holds.
            table.SetWorksView(true, aim);
            table.TryOverride(50, out CommandDoctrineOrder held, out CommandNodeOperationalState heldState);
            AssertEqual(CommandTaskType.HoldObjective, held.Task, "the asset's order is a hold");
            AssertEqual(WorksHoldGuard.Reason, held.Reason, "named for the guard");
            AssertEqual(CommandTaskType.AttackObjective, held.PreGuardTask, "remembering the attack");
            AssertEqual(ExploitOverrideKind.Exploit, ExploitPosture.KindOf(in held), "it is still an exploit order, not a follower");
            AssertTrue(WorksHoldGuard.Holds(heldState.Task, heldState.Reason), "and its state is the guard's");
            table.TryOverride(51, out CommandDoctrineOrder follower, out _);
            AssertEqual(CommandTaskType.HoldObjective, follower.Task, "the follower's order is a hold");
            AssertEqual(ExploitOverrideKind.Follow, ExploitPosture.KindOf(in follower), "and it still reads as a follower, so the pipeline submits it no move");

            // A breach passes, with its followers.
            table.Clear(ExploitEnd.StandDown);
            table.Add(ExploitAt(OpeningKind.Gap, 600f));
            table.SetSubtree(1, new[] { 51 }, new int[0]);
            table.TryOverride(50, out CommandDoctrineOrder gap, out _);
            AssertEqual(CommandTaskType.AttackObjective, gap.Task, "a Gap exploit passes");
            table.TryOverride(51, out CommandDoctrineOrder gapFollower, out _);
            AssertEqual(CommandTaskType.AttackObjective, gapFollower.Task, "so does its follower");

            // One aimed off the works passes; so does any exploit with the hold off or no works.
            table.Clear(ExploitEnd.StandDown);
            table.Add(ExploitAt(OpeningKind.IsolatedCluster, 300f));
            table.TryOverride(50, out CommandDoctrineOrder off, out _);
            AssertEqual(CommandTaskType.AttackObjective, off.Task, "an exploit aimed off the works passes");
            table.Clear(ExploitEnd.StandDown);
            table.Add(ExploitAt(OpeningKind.IsolatedCluster, 600f));
            table.SetWorksView(false, aim);
            table.TryOverride(50, out CommandDoctrineOrder quiet, out _);
            AssertEqual(CommandTaskType.AttackObjective, quiet.Task, "with the hold off it passes");
            table.SetWorksView(true, null);
            table.TryOverride(50, out CommandDoctrineOrder none, out _);
            AssertEqual(CommandTaskType.AttackObjective, none.Task, "with no held works it passes");
        }
        finally
        {
            table.SetWorksView(false, null);
            table.Clear(ExploitEnd.StandDown);
        }
    }

    static void WFallback()
    {
        var lookup = new HoldLookup { Hold = true };
        lookup.Faces.Add("child-2");
        ArmyOrchestrator army = TwoCorpsArmy(lookup);
        var built = new CommandNodeOperationalState("node-10", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, CommandTaskState.Planning);
        CommandNodeOperationalState guarded = army.GuardFallback(in built);
        AssertEqual(CommandTaskType.HoldObjective, guarded.Task, "a fallback state of a works-facing child's brigade is a hold");
        AssertEqual(WorksHoldGuard.Reason, guarded.Reason, "with the guard's reason");
        var other = new CommandNodeOperationalState("node-20", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, CommandTaskState.Planning);
        AssertEqual(CommandTaskType.AttackObjective, army.GuardFallback(in other).Task, "another child's is not");
        lookup.Hold = false;
        AssertEqual(CommandTaskType.AttackObjective, army.GuardFallback(in built).Task, "with no hold it is not");

        // TryResolveState calls it on the fallback path (the runtime file is not in the harness).
        string producer = System.IO.File.ReadAllText(TestFixtures.FindRepoPath("src/WhiskeyRealism/Tactical/Battle/Producers/PostureIntentProducer.cs"));
        int resolve = producer.IndexOf("internal static bool TryResolveState(", StringComparison.Ordinal);
        int single = producer.IndexOf("CommandNodeOperationsRuntime.TryBuildSingle(", resolve, StringComparison.Ordinal);
        int guard = producer.IndexOf("army.GuardFallback(", resolve, StringComparison.Ordinal);
        AssertTrue(resolve >= 0 && single > resolve && guard > single, "TryResolveState guards the state TryBuildSingle built");
    }
}
