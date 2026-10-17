using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;

// BC14W task 6: the works hold on the stored orders, states and leaves (spec § 7A requirement-works-hold; § 13.2 WORKS-GUARD, READER-DOCTRINE-COPY-W,
// READER-LEAF-COPY). The runtime halves (the pipeline, the stance writer, the coordinator) are pinned in ManeuverFeasibilityTests.Works.Stance.cs.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksGuardTests() => new (string, Action)[]
    {
        ("bc14w READER-DOCTRINE-COPY-W an order carrying a via, a formation and a preparation point keeps all three through every copy", WDoctrineCopy),
        ("bc14w READER-LEAF-COPY the guard's leaf copy keeps every other field and sets WorksHeld", WLeafCopy),
        ("bc14w WORKS-GUARD attack, support and fix tasks of a node facing works become holds, with the reason and the task they replaced", WGuardRules),
        ("bc14w WORKS-GUARD-EXEMPT a node BC11 holds, a breach exploit, a child that does not face works and the holding tasks pass", WGuardExempt),
        ("bc14w WORKS-GUARD-LANE a guarded former fix with its lane outside the works band is planned to it, every other guarded order holds in place", WGuardLane),
        ("bc14w WORKS-GUARD-STRENGTH the operation's assigned strength counts a guarded state by the task it replaced", WGuardStrength),
        ("bc14w WORKS-GUARD-OVERRIDE an attack-now opportunity leaves a guarded hold a hold", WGuardOverride),
    };

    static CommandDoctrineOrder OrderOf(CommandTaskType task, string reason = "unit-test")
        => CommandDoctrineOrder.Create("node-9", CommandNodeRole.MainEffort, task, "obj-1", DoctrineTargetPoint.From(100f, 200f),
            DoctrineTargetPoint.From(110f, 210f), DoctrineTargetPoint.From(120f, 220f), DoctrineAllowedIdleReason.None, 5f, 6f, 0.7f, reason);

    static CommandNodeOperationalState StateOf(CommandTaskType task)
        => new CommandNodeOperationalState("node-9", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, task, CommandTaskState.Committed, 1f, 2f, 90f);

    static WorksGuardContext Facing() => new WorksGuardContext { WorksHold = true, FacesWorks = true };

    static void WDoctrineCopy()
    {
        CommandDoctrineOrder none = OrderOf(CommandTaskType.AttackObjective);
        AssertFalse(none.HasFormation, "Create gives no formation");
        AssertEqual(-1, none.Formation, "the formation reads -1");
        AssertFalse(none.HasPreparation, "no preparation point");
        AssertTrue(float.IsNaN(none.PreparationX) && float.IsNaN(none.PreparationZ), "NaN");
        AssertEqual(CommandTaskType.None, none.PreGuardTask, "no guarded task");
        CommandDoctrineOrder empty = default(CommandDoctrineOrder);
        AssertFalse(empty.HasFormation || empty.HasPreparation || empty.HasVia, "the no-order value carries none");

        CommandDoctrineOrder full = OrderOf(CommandTaskType.AttackObjective).WithVia(7f, 8f).WithFormation(4).WithPreparation(300f, 400f);
        AssertTrue(full.HasVia && full.HasFormation && full.HasPreparation, "the three are set");
        AssertEqual(4, full.Formation, "formation 4");
        AssertNear(300f, full.PreparationX, 1e-4f, "preparation x");
        AssertNear(400f, full.PreparationZ, 1e-4f, "preparation z");
        AssertNear(7f, full.ViaX, 1e-4f, "via x survives WithFormation and WithPreparation");

        void Keeps(CommandDoctrineOrder copy, string how)
        {
            AssertTrue(copy.HasVia && copy.ViaX == 7f && copy.ViaZ == 8f, how + " keeps the via");
            AssertTrue(copy.HasFormation && copy.Formation == 4, how + " keeps the formation");
            AssertTrue(copy.HasPreparation && copy.PreparationX == 300f && copy.PreparationZ == 400f, how + " keeps the preparation point");
        }
        Keeps(full.WithSop(TacticalSopDecision.ForAssignedTask(CommandNodeRole.MainEffort, CommandTaskType.AttackObjective)), "WithSop");
        Keeps(full.WithTask(CommandTaskType.SupportAttack, "retask"), "WithTask");
        Keeps(full.WithTask(CommandTaskType.HoldObjective, WorksHoldGuard.Reason), "WithTask (the guard's rewrite)");
        Keeps(full.WithManeuver(DoctrineTargetPoint.From(1f, 2f), DoctrineAllowedIdleReason.FormingUp, 45f, true, "maneuver"), "WithManeuver");
        Keeps(full.WithExploit(3), "WithExploit");
        Keeps(full.WithWorksHold(CommandTaskType.AttackObjective), "WithWorksHold");

        CommandDoctrineOrder formation = OrderOf(CommandTaskType.FixEnemy).WithExploit(5).WithFormation(2);
        AssertEqual(5, formation.ExploitId, "WithFormation keeps the exploit id");
        AssertEqual(CommandTaskType.FixEnemy, formation.Task, "and the task");
        AssertNear(100f, formation.PrimaryTarget.X, 1e-4f, "and the targets");
        CommandDoctrineOrder prep = OrderOf(CommandTaskType.FixEnemy).WithPreparation(1f, 2f);
        AssertEqual("node-9", prep.NodeId, "WithPreparation keeps the node");
        AssertFalse(prep.HasFormation, "and adds no formation");
    }

    static void WLeafCopy()
    {
        var leaf = new TacticalLeafBrigadeMap.LeafAssignment(42, "1st Brigade", DirectChildRole.Main, CommandTaskType.AttackObjective,
            new[] { DirectChildRole.Main, DirectChildRole.SupportMain }, new[] { "Corps", "Division" }, 3);
        AssertFalse(leaf.WorksHeld, "an unguarded leaf is not held");
        AssertTrue(WorksHoldGuard.ApplyLeaf(in leaf, Facing(), out TacticalLeafBrigadeMap.LeafAssignment guarded), "the guard rewrites an attack leaf");
        AssertEqual(CommandTaskType.HoldObjective, guarded.LeafTask, "to a hold");
        AssertTrue(guarded.WorksHeld, "and marks it");
        AssertEqual(42, guarded.InstanceId, "instance");
        AssertEqual("1st Brigade", guarded.DisplayName, "name");
        AssertEqual(DirectChildRole.Main, guarded.LeafRole, "role");
        AssertEqual(3, guarded.TopLevelChildIndex, "top-level child index");
        AssertEqual("Main->SupportMain", guarded.CascadeChainString, "cascade chain");
        AssertEqual(2, guarded.ParentNameChain.Count, "parent names");
        AssertEqual("Division", guarded.ParentNameChain[1], "parent name 2");

        var held = new TacticalLeafBrigadeMap.LeafAssignment(43, "2nd", DirectChildRole.Screen, CommandTaskType.Probe, null, null, 0);
        AssertFalse(WorksHoldGuard.ApplyLeaf(in held, Facing(), out TacticalLeafBrigadeMap.LeafAssignment same), "a probe leaf passes");
        AssertFalse(same.WorksHeld, "unmarked");
        AssertEqual(CommandTaskType.Probe, same.LeafTask, "unchanged");
    }

    static void WGuardRules()
    {
        foreach (CommandTaskType task in new[] { CommandTaskType.AttackObjective, CommandTaskType.SupportAttack, CommandTaskType.FixEnemy })
        {
            CommandDoctrineOrder order = OrderOf(task).WithVia(1f, 2f);
            AssertTrue(WorksHoldGuard.Apply(in order, Facing(), out CommandDoctrineOrder o), task + " order is guarded");
            AssertEqual(CommandTaskType.HoldObjective, o.Task, task + " becomes a hold");
            AssertEqual(WorksHoldGuard.Reason, o.Reason, "with the guard's reason");
            AssertEqual(task, o.PreGuardTask, "and remembers the task");
            AssertTrue(WorksHoldGuard.Holds(o.Task, o.Reason), "Holds sees it");
            AssertNear(100f, o.PrimaryTarget.X, 1e-4f, "the targets stay (a former fix goes to its lane)");
            AssertEqual(CommandNodeRole.MainEffort, o.Role, "the role stays");
            AssertTrue(o.HasVia, "the via stays");
            AssertEqual(TacticalSopDecision.ForAssignedTask(CommandNodeRole.MainEffort, CommandTaskType.HoldObjective).Authority, o.Sop.Authority, "the SOP is the hold's");

            CommandNodeOperationalState state = StateOf(task);
            AssertTrue(WorksHoldGuard.ApplyState(in state, Facing(), out CommandNodeOperationalState s), task + " state is guarded");
            AssertEqual(CommandTaskType.HoldObjective, s.Task, task + " state becomes a hold");
            AssertEqual(WorksHoldGuard.Reason, s.Reason, "with the reason");
            AssertEqual(task, s.PreGuardTask, "and the task it replaced");
            AssertEqual(CommandTaskState.Committed, s.TaskState, "the task state stays");
            AssertNear(1f, s.X, 1e-4f, "position stays");
            AssertTrue(WorksHoldGuard.Holds(s.Task, s.Reason), "Holds sees the state");
        }
        AssertFalse(WorksHoldGuard.Holds(CommandTaskType.HoldObjective, "defender-hold"), "a Defender's hold is not the guard's");
        AssertFalse(WorksHoldGuard.Holds(CommandTaskType.AttackObjective, WorksHoldGuard.Reason), "nor an attack carrying the reason");
    }

    static void WGuardExempt()
    {
        CommandDoctrineOrder attack = OrderOf(CommandTaskType.AttackObjective);
        CommandNodeOperationalState state = StateOf(CommandTaskType.AttackObjective);
        // The hold is off, the child does not face works, BC11 holds the node.
        WorksGuardContext off = Facing(); off.WorksHold = false;
        WorksGuardContext away = Facing(); away.FacesWorks = false;
        WorksGuardContext table = Facing(); table.InManeuverTable = true;
        foreach (WorksGuardContext c in new[] { off, away, table })
        {
            AssertFalse(WorksHoldGuard.Apply(in attack, c, out CommandDoctrineOrder o), "an order passes");
            AssertEqual(CommandTaskType.AttackObjective, o.Task, "unchanged");
            AssertFalse(WorksHoldGuard.ApplyState(in state, c, out CommandNodeOperationalState s), "a state passes");
            AssertEqual(CommandTaskType.AttackObjective, s.Task, "unchanged");
        }
        // Probe, holding tasks and the other tasks pass.
        foreach (CommandTaskType task in new[] { CommandTaskType.Probe, CommandTaskType.Scout, CommandTaskType.Screen, CommandTaskType.HoldObjective, CommandTaskType.ReserveWait,
            CommandTaskType.HoldChoke, CommandTaskType.GuardFlank, CommandTaskType.FallBackToLine, CommandTaskType.Delay, CommandTaskType.Consolidate, CommandTaskType.Counterstroke })
        {
            CommandDoctrineOrder order = OrderOf(task);
            AssertFalse(WorksHoldGuard.Apply(in order, Facing(), out CommandDoctrineOrder o), task + " passes");
            AssertEqual(task, o.Task, task + " unchanged");
        }
        // Exploits: a breach (Gap, Penetration) passes with its followers; any other kind is guarded only when its aim lies on held works,
        // whether or not the asset's child faces works.
        var breach = new WorksGuardContext { WorksHold = true, IsExploit = true, ExploitKind = OpeningKind.Gap, ExploitAimOnWorks = true };
        AssertFalse(WorksHoldGuard.Apply(in attack, breach, out _), "a Gap exploit passes");
        breach.ExploitKind = OpeningKind.Penetration;
        AssertFalse(WorksHoldGuard.Apply(in attack, breach, out _), "a Penetration exploit passes");
        var isolated = new WorksGuardContext { WorksHold = true, IsExploit = true, ExploitKind = OpeningKind.IsolatedCluster, ExploitAimOnWorks = true };
        AssertTrue(WorksHoldGuard.Apply(in attack, isolated, out CommandDoctrineOrder held), "an IsolatedCluster exploit aimed on held works is guarded");
        AssertEqual(CommandTaskType.HoldObjective, held.Task, "to a hold");
        isolated.ExploitAimOnWorks = false;
        AssertFalse(WorksHoldGuard.Apply(in attack, isolated, out _), "one aimed off the works passes");
        isolated.ExploitAimOnWorks = true;
        isolated.WorksHold = false;
        AssertFalse(WorksHoldGuard.Apply(in attack, isolated, out _), "with no hold it passes");
    }

    sealed class BandQuery : IWorksQuery
    {
        public float BandFromZ = 500f;
        public bool OnHeldWorks(float x, float z) => false;
        public bool InWorksBand(float x, float z) => z >= BandFromZ;
    }

    static void WGuardLane()
    {
        var physical = new CommandPhysicalState(false, false, false, 0, false, 0);
        var eligibility = new WriteEligibilitySnapshot(modeAllowsWrites: true, playerProtected: false, routed: false, orderPending: false, recentOrder: false);
        var works = new BandQuery();

        // The fix lane (z 200) is outside the band (z >= 500): the guarded fix goes to its lane and holds there.
        CommandDoctrineOrder fix = OrderOf(CommandTaskType.FixEnemy);
        WorksHoldGuard.Apply(in fix, Facing(), out CommandDoctrineOrder guardedFix);
        AssertTrue(WorksHoldGuard.LaneOpen(in guardedFix, works), "the lane is open");
        PostureExecutionDecision lane = CommandPostureExecutor.Decide(guardedFix, physical, eligibility, 10f, holdLane: true);
        AssertEqual(PostureExecutionAction.SetFormationAndWaypoint, lane.Action, "formation and waypoint");
        AssertEqual(PostureExecutionTarget.DoctrinePrimaryTarget, lane.Target, "to the lane");

        // Without the lane flag the same order holds where it stands: a formation and no waypoint.
        PostureExecutionDecision stay = CommandPostureExecutor.Decide(guardedFix, physical, eligibility, 10f, holdLane: false);
        AssertEqual(PostureExecutionAction.SetFormation, stay.Action, "a formation only");

        // A lane inside the band is no lane.
        works.BandFromZ = 150f;
        AssertFalse(WorksHoldGuard.LaneOpen(in guardedFix, works), "the target is in the band");
        // A former attack has no lane whatever the band, and a lane needs a query.
        works.BandFromZ = 500f;
        CommandDoctrineOrder attack = OrderOf(CommandTaskType.AttackObjective);
        WorksHoldGuard.Apply(in attack, Facing(), out CommandDoctrineOrder guardedAttack);
        AssertFalse(WorksHoldGuard.LaneOpen(in guardedAttack, works), "a former attack holds in place");
        AssertEqual(PostureExecutionAction.SetFormation, CommandPostureExecutor.Decide(guardedAttack, physical, eligibility, 10f, holdLane: true).Action,
            "even if asked for a lane");
        AssertFalse(WorksHoldGuard.LaneOpen(in guardedFix, null), "no query, no lane");
        AssertFalse(WorksHoldGuard.LaneOpen(in fix, works), "an unguarded fix is not a guarded lane");
    }

    static void WGuardStrength()
    {
        AssertTrue(TacticalBattleCoordinator.OperationStrength.CountsAsAssigned(CommandTaskType.AttackObjective, CommandTaskType.None), "an attack counts");
        AssertTrue(TacticalBattleCoordinator.OperationStrength.CountsAsAssigned(CommandTaskType.SupportAttack, CommandTaskType.None), "a support attack counts");
        AssertTrue(TacticalBattleCoordinator.OperationStrength.CountsAsAssigned(CommandTaskType.HoldObjective, CommandTaskType.AttackObjective), "a guarded attack still counts");
        AssertTrue(TacticalBattleCoordinator.OperationStrength.CountsAsAssigned(CommandTaskType.HoldObjective, CommandTaskType.SupportAttack), "a guarded support still counts");
        AssertFalse(TacticalBattleCoordinator.OperationStrength.CountsAsAssigned(CommandTaskType.HoldObjective, CommandTaskType.FixEnemy), "a former fix never did");
        AssertFalse(TacticalBattleCoordinator.OperationStrength.CountsAsAssigned(CommandTaskType.HoldObjective, CommandTaskType.None), "a plain hold does not");
        AssertFalse(TacticalBattleCoordinator.OperationStrength.CountsAsAssigned(CommandTaskType.FixEnemy, CommandTaskType.None), "nor a fix");
    }

    static void WGuardOverride()
    {
        // The shipped overload is the unguarded one.
        CommandTaskType plain = TacticalOpportunityTaskOverride.Override(CommandTaskType.HoldObjective, DirectChildRole.Main, ReinforcementOpportunity.AttackNow, out string reason);
        AssertEqual(CommandTaskType.AttackObjective, plain, "an attack-now opportunity turns a hold into an attack");
        AssertEqual("attack-now", reason, "with its reason");
        CommandTaskType held = TacticalOpportunityTaskOverride.Override(CommandTaskType.HoldObjective, DirectChildRole.Main, ReinforcementOpportunity.AttackNow,
            worksHold: true, out string heldReason);
        AssertEqual(CommandTaskType.HoldObjective, held, "a guarded hold stays a hold");
        AssertTrue(heldReason == null, "with no reason");
        CommandTaskType consolidate = TacticalOpportunityTaskOverride.Override(CommandTaskType.Consolidate, DirectChildRole.SupportMain, ReinforcementOpportunity.AttackNow,
            worksHold: true, out _);
        AssertEqual(CommandTaskType.Consolidate, consolidate, "so does a guarded consolidation");
        // A withdrawal is not an attack: it still applies to a held node.
        CommandTaskType withdrawal = TacticalOpportunityTaskOverride.Override(CommandTaskType.HoldObjective, DirectChildRole.Main, ReinforcementOpportunity.WithdrawalToFightLater,
            worksHold: true, out _);
        AssertEqual(CommandTaskType.FallBackToLine, withdrawal, "a withdrawal still reaches it");
    }
}
