using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// BC14W task 6: the works hold's stance and the readers that must not undo the guard (spec § 7A requirement-works-hold, "One stance source" and "No
// consumer undoes it"; § 13.2 WORKS-STANCE). The pipeline, the executor, the writer and the coordinator are runtime files (not in the harness): their
// calls are pinned in the source, and the pure rules they call are exercised here.
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksStanceTests() => new (string, Action)[]
    {
        ("bc14w WORKS-STANCE-RULES a works-hold stance takes vanilla's attack stances, yields to its weak-group screen and the army retreat, and a renewal coexists", WStanceRules),
        ("bc14w WORKS-STANCE-INTENT the one stance of a guarded group is the mapper's hold stance, flagged, at the posture executor's priority", WStanceIntent),
        ("bc14w WORKS-STANCE-45 #45 submits no stance for a guarded order or state and keeps only the charge demotion", WStance45),
        ("bc14w WORKS-PIN-MAPPER a pin lowers an attack stance to defend and leaves a screen, a hold and every other stance as the mapper has them", WPinMapper),
        ("bc14w WORKS-PIN-INTENT a pinned fix gets one flagged stance-2 intent keeping its task; a task with no attack stance gets none", WPinIntent),
        ("bc14w WORKS-PIN-WIRED the posture pipeline and both #45 steps ask the army for the pin, and the node lookup is gated by the doctrine flag", WPinWired),
        ("bc14w WORKS-STANCE-PINS the pipeline submits the stance after every rewrite and before the decision, and no later reader undoes the guard", WStancePins),
    };

    static StanceWriteInput StanceInput(int ordered, int vanilla, bool worksHold, int macro = 1, StanceHoldRecord record = default(StanceHoldRecord),
        StanceWriteSource source = StanceWriteSource.Executor, float now = 1f, int target = 2)
        => new StanceWriteInput(true, target, ordered, macro, now, record, source, vanilla, now, worksHold);

    static void WStanceRules()
    {
        // Against vanilla's assault (ordered 4, per-group stance 4): refused today, applied for the works hold.
        StanceWriteDecision plain = TacticalStanceOwnership.Decide(StanceInput(ordered: 4, vanilla: 4, worksHold: false));
        AssertEqual(StanceWriteAction.Refuse, plain.Action, "without the flag a vanilla assault is never replaced");
        AssertEqual("vanilla-assault", plain.Reason, "for that reason");
        StanceWriteDecision held = TacticalStanceOwnership.Decide(StanceInput(ordered: 4, vanilla: 4, worksHold: true));
        AssertEqual(StanceWriteAction.Write, held.Action, "the works hold replaces it");
        // Against vanilla's attack stance 3 the hold is written too.
        AssertEqual(StanceWriteAction.Write, TacticalStanceOwnership.Decide(StanceInput(ordered: 3, vanilla: 3, worksHold: true)).Action, "and stance 3");
        // The yield step is not reached for a flagged intent at all: even a stance the escalation yield would swallow (vanilla's assault against an attack
        // target) is written for the works hold, and still yielded to without it.
        AssertEqual(StanceWriteAction.Write, TacticalStanceOwnership.Decide(StanceInput(ordered: 4, vanilla: 4, worksHold: true, target: 3)).Action,
            "a flagged intent skips the escalation yield");
        AssertEqual(StanceWriteAction.Yield, TacticalStanceOwnership.Decide(StanceInput(ordered: 4, vanilla: 4, worksHold: false, target: 3)).Action,
            "control: the same intent unflagged is yielded");
        // Vanilla's weak-group screen still wins: the group is handed back to vanilla.
        StanceWriteDecision screen = TacticalStanceOwnership.Decide(StanceInput(ordered: 3, vanilla: 1, worksHold: true));
        AssertEqual(StanceWriteAction.Yield, screen.Action, "vanilla's weak-group screen is yielded to");
        AssertEqual("vanilla-weak-group", screen.Reason, "as a weak group");
        // The army retreat still wins.
        AssertEqual(StanceWriteAction.Yield, TacticalStanceOwnership.Decide(StanceInput(ordered: 4, vanilla: 4, worksHold: true, macro: 3)).Action, "the army retreat is yielded to");
        // A group the hold already owns is reasserted over a vanilla assault, not refused.
        var record = new StanceHoldRecord(2, 0.95f, StanceWriteSource.Executor);
        StanceWriteDecision reassert = TacticalStanceOwnership.Decide(StanceInput(ordered: 4, vanilla: 4, worksHold: true, record: record));
        AssertEqual(StanceWriteAction.Write, reassert.Action, "an owned hold is reasserted over vanilla's assault");

        // RenewWhileOrdered coexists: the renewal re-runs the group's own record (stance 2) while it carries an order and the stance is still the
        // ordered one; it never meets the assault refusal, so it needs no flag. A vanilla overwrite to 4 ends the renewal and the next tick's flagged
        // intent takes the group back.
        var executorHold = new StanceHoldRecord(2, 1f, StanceWriteSource.Executor);
        AssertTrue(TacticalStanceOwnership.RenewsWhileOrdered(executorHold, currentOrderedStance: 2, carryingOrder: true), "a held stance 2 is renewed while the group carries an order");
        AssertFalse(TacticalStanceOwnership.RenewsWhileOrdered(executorHold, currentOrderedStance: 4, carryingOrder: true), "vanilla's overwrite ends the renewal");
        StanceWriteDecision renew = TacticalStanceOwnership.Decide(StanceInput(ordered: 2, vanilla: 4, worksHold: false, record: executorHold, now: 1.1f));
        AssertTrue(renew.Action == StanceWriteAction.Claim, "the renewal is a claim of the stance already ordered, got " + renew.Action + " " + renew.Reason);
    }

    sealed class CaptureSink : IIntentSink
    {
        public readonly List<BattleOrderIntent> Intents = new List<BattleOrderIntent>();
        public void Submit(in BattleOrderIntent intent) => Intents.Add(intent);
    }

    static void WStanceIntent()
    {
        var sink = new CaptureSink();
        int n = PostureIntentPlan.SubmitWorksHoldStance(sink, 77, IntentPriority.Executor, IntentSource.PostureExecutor, 0);
        AssertEqual(1, n, "one intent");
        BattleOrderIntent i = sink.Intents[0];
        AssertEqual(OrderChannel.Stance, i.Channel, "on the stance channel");
        AssertEqual(TacticalCommandTaskToStanceMapper.Map(CommandTaskType.HoldObjective), i.Value, "the mapper's hold stance");
        AssertEqual(2, i.Value, "which is 2");
        AssertTrue((i.Flags & IntentFlags.WorksHold) != 0, "flagged");
        AssertEqual(IntentPriority.Executor, i.Priority, "at the posture executor's priority");
        AssertEqual(IntentSource.PostureExecutor, i.Source, "from the posture executor");
        AssertEqual(77, i.GroupId, "for the group");
        AssertEqual(WorksHoldGuard.Reason, i.Reason, "with the guard's reason");
        AssertEqual(0, PostureIntentPlan.SubmitWorksHoldStance(sink, 0, IntentPriority.Executor, IntentSource.PostureExecutor, 0), "no group, no intent");

        // The request's own stance is not submitted again once the hold's has gone out.
        var request = new PostureIntentRequest
        {
            GroupId = 77, Action = PostureExecutionAction.SetFormationAndWaypoint, Task = CommandTaskType.HoldObjective, HasTarget = true, TargetX = 1f, TargetZ = 2f,
            TargetFormation = -1, Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Reason = "posture-executor", StanceSubmitted = true,
        };
        var second = new CaptureSink();
        PostureIntentPlan.Submit(second, in request);
        foreach (BattleOrderIntent o in second.Intents) AssertTrue(o.Channel != OrderChannel.Stance, "no second stance intent");
        request.StanceSubmitted = false;
        var third = new CaptureSink();
        PostureIntentPlan.Submit(third, in request);
        bool anyStance = false;
        foreach (BattleOrderIntent o in third.Intents) anyStance |= o.Channel == OrderChannel.Stance;
        AssertTrue(anyStance, "control: without the flag the request submits its own");
    }

    static void WStance45()
    {
        CommandDoctrineOrder guarded = OrderOf(CommandTaskType.AttackObjective);
        WorksHoldGuard.Apply(in guarded, new WorksGuardContext { WorksHold = true, FacesWorks = true }, out guarded);
        DoctrineStanceDecision decision = DoctrineConsumerDecisions.DecideStance(guarded, enemyMainLineExposed: true, localOdds: 5f);
        AssertEqual(DoctrineConsumerAction.Deny, decision.Action, "#45's doctrine decision for a guarded order is Deny");
        AssertEqual(DoctrineConsumerDecisions.WorksHoldReason, decision.Reason, "named for the guard");
        DoctrineStanceDecision plainHold = DoctrineConsumerDecisions.DecideStance(OrderOf(CommandTaskType.HoldObjective), true, 5f);
        AssertEqual(DoctrineConsumerAction.Observe, plainHold.Action, "a plain hold has no doctrine opinion");
        DoctrineStanceDecision attack = DoctrineConsumerDecisions.DecideStance(OrderOf(CommandTaskType.AttackObjective), true, 5f);
        AssertEqual(DoctrineConsumerAction.Allow, attack.Action, "control: an unguarded attack at 5 to 1 is still allowed");

        // The cached entry: a Skip, not an Apply; only the charge demotion survives.
        TacticalResolvedGroupDecision onVanillaAssault = TacticalResolvedGroupDecision.ForWorksHold(4, chargeDenialOn: true, orchestratorArmyOn: true, fromDoctrine: true);
        AssertEqual(TacticalDoctrineDecisionKind.Skip, onVanillaAssault.Kind, "no stance is applied");
        AssertEqual(ChargeOverrideAction.DemoteToHold, onVanillaAssault.ChargeAction, "a vanilla assault is demoted");
        AssertEqual(TacticalChargeDenial.DoctrineDemoteStance, onVanillaAssault.FinalStance, "to the hold");
        AssertTrue(onVanillaAssault.IsChargeOverride && onVanillaAssault.FromDoctrine, "from the doctrine step");
        TacticalResolvedGroupDecision ledgerStep = TacticalResolvedGroupDecision.ForWorksHold(3, chargeDenialOn: true, orchestratorArmyOn: true, fromDoctrine: false);
        AssertEqual(TacticalDoctrineDecisionKind.Skip, ledgerStep.Kind, "the ledger step applies no stance either");
        AssertTrue(ledgerStep.FromLedger && !ledgerStep.IsChargeOverride, "from the ledger, no assault at cache time");
        AssertEqual(ChargeOverrideAction.DemoteToHold, ledgerStep.ChargeAction, "but a later vanilla escalation is still demoted");
        TacticalResolvedGroupDecision denialOff = TacticalResolvedGroupDecision.ForWorksHold(4, chargeDenialOn: false, orchestratorArmyOn: true, fromDoctrine: true);
        AssertEqual(ChargeOverrideAction.PreserveVanilla, denialOff.ChargeAction, "with charge denial off #45 leaves vanilla's assault alone");
        AssertEqual(TacticalDoctrineDecisionKind.Skip, denialOff.Kind, "and still writes no stance");
    }

    static void WStancePins()
    {
        string producer = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs");
        string run = FlankResponseTests.Between(producer, "internal static bool Run(", "private static bool CanBypassStaleQueuedOrder");
        string flat = FlankResponseTests.Flat(run);
        int guard = flat.IndexOf("PostureStanceGuard.Apply(stance, ref state, hasOrder, ref order);", StringComparison.Ordinal);
        int stance = flat.IndexOf("PostureIntentPlan.SubmitWorksHoldStance(", StringComparison.Ordinal);
        int decide = flat.IndexOf("CommandPostureExecutor.Decide(order, physical, eligibility", StringComparison.Ordinal);
        int cavalry = flat.IndexOf("TryCavalryFollow(", StringComparison.Ordinal);
        AssertTrue(guard > 0 && stance > guard, "the works-hold stance follows the Defender stance guard, after every rewrite");
        AssertTrue(decide > stance, "and precedes the posture decision, whatever it returns");
        AssertTrue(flat.Contains("worksHeld && recordTick ? PostureIntentPlan.SubmitWorksHoldStance("), "only the posture producer's own pass submits it");
        FlankResponseTests.AssertPinned(run, "int worksStance = worksHeld && recordTick", "and its count is the one every later exit reads");
        FlankResponseTests.AssertPinned(run, "bool worksHeld = WorksHoldGuard.Holds(state.Task, state.Reason) || (hasOrder && WorksHoldGuard.Holds(order.Task, order.Reason));",
            "a group is held when its state or its order carries the guard");
        AssertTrue(cavalry > 0 && flat.Substring(Math.Max(0, cavalry - 140), 140).Contains("!WorksHoldGuard.Holds(state.Task, state.Reason)"), "the cavalry follow leaves a guarded group alone");
        FlankResponseTests.AssertPinned(run, "TacticalOpportunityTaskOverride.Override(state.Task, DirectChildRole.Main, opportunity, WorksHoldGuard.Holds(state.Task, state.Reason), out overrideReason)",
            "the opportunity override is told of the hold (the node path and the no-order path)");
        FlankResponseTests.AssertPinned(run, "CommandPostureExecutor.Decide(order, physical, eligibility, (float)s.DoctrineHours, worksLane)", "a former fix with an open lane is planned to it");
        FlankResponseTests.AssertPinned(run, "WorksHoldGuard.LaneOpen(in order, army?.Feasibility?.HeldWorks)", "against the ledger's held works");
        FlankResponseTests.AssertPinned(run, "StanceSubmitted = worksStance > 0", "the request does not submit a second stance");
        // A stance is no order that moves or re-lays the group: the three early exits after it, and the follower's, report no submission, so the group's
        // facing pulse and fire control still run (Groups[i].Submitted, which the pulse skips, is true only for a group the pipeline ordered).
        AssertFalse(flat.Contains("return worksStance > 0;"), "no early exit counts a stance as a submission");
        FlankResponseTests.AssertPinned(run, "decision, worksStance, decision.Reason); return false;", "the no-write exit");
        FlankResponseTests.AssertPinned(run, "decision, worksStance, \"nav-budget-deferred\"); return false;", "the nav-budget exit");
        FlankResponseTests.AssertPinned(run, "decision, worksStance, \"road-route-deferred\"); return false;", "the road-route exit");
        FlankResponseTests.AssertPinned(run, "int submitted = worksStance + PostureIntentPlan.Submit(sink, request);", "the final count adds the stance");
        FlankResponseTests.AssertPinned(run, "return submitted > worksStance;", "and reports a submission only past the stance");
        FlankResponseTests.AssertPinned(producer, "TacticalOpportunityTaskOverride.Override(leaf.LeafTask, leaf.LeafRole, opportunity, leaf.WorksHeld, out string overrideReason)",
            "the leaf path is told of the hold");
        string play = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PlayIntentProducer.cs");
        FlankResponseTests.AssertPinned(play, "if (WorksHoldGuard.Holds(child.State.Task, child.State.Reason)) continue;", "the division play skips a guarded group");
        string executor = FlankResponseTests.SourceText("Tactical", "Battle", "OrderExecutorRuntime.cs");
        FlankResponseTests.AssertPinned(executor, "TacticalStanceWriter.EnsureStance(bunits, unit, w.Value, w.Reason, w.Source, (w.Flags & IntentFlags.WorksHold) != 0)", "the flag crosses the executor");
        string writer = FlankResponseTests.SourceText("Util", "TacticalStanceWriter.cs");
        FlankResponseTests.AssertPinned(writer, "worksHold: worksHold);", "and reaches the ownership decision");
        string coordinator = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs");
        FlankResponseTests.AssertPinned(coordinator, "string.Equals(dReason, DoctrineConsumerDecisions.WorksHoldReason, StringComparison.Ordinal)", "#45's doctrine step recognises the guard's Deny");
        FlankResponseTests.AssertPinned(coordinator, "if (WorksHoldGuard.Holds(state.Task, state.Reason))", "and its ledger step the guarded state");
        FlankResponseTests.AssertPinned(coordinator, "OperationStrength.CountsAsAssigned(states[i].Task, states[i].PreGuardTask)", "the operation strength counts a guarded state by its pre-guard task");
        string orchestrator = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleOrchestrator.cs");
        int planner = orchestrator.IndexOf("TacticalNestedDivisionPlayPlanner", StringComparison.Ordinal);
        int ledger = orchestrator.IndexOf("Army.UpdateOperationsLedger(", StringComparison.Ordinal);
        AssertTrue(ledger > 0 && (planner < 0 || planner < ledger), "the nested division play planner runs before the guarded store, so it never sees a guarded state");
    }

    static void WPinMapper()
    {
        foreach (CommandTaskType task in Enum.GetValues(typeof(CommandTaskType)))
        {
            int plain = TacticalCommandTaskToStanceMapper.Map(task);
            int pinned = TacticalCommandTaskToStanceMapper.Map(task, worksPin: true);
            AssertEqual(plain, TacticalCommandTaskToStanceMapper.Map(task, worksPin: false), task + ": without a pin the mapper is unchanged");
            if (plain == TacticalCommandTaskToStanceMapper.StanceAttack)
            {
                AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend, pinned, task + ": an attack stance is lowered to defend");
                AssertTrue(TacticalCommandTaskToStanceMapper.WorksPinHolds(task, true), task + ": the pin holds it");
                AssertFalse(TacticalCommandTaskToStanceMapper.WorksPinHolds(task, false), task + ": no pin, no hold");
            }
            else
            {
                AssertEqual(plain, pinned, task + ": a stance that is not attack is left as it is");
                AssertFalse(TacticalCommandTaskToStanceMapper.WorksPinHolds(task, true), task + ": nothing to hold");
            }
        }
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceAttack, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FixEnemy), "control: a fix is an attack stance");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceScreen, TacticalCommandTaskToStanceMapper.Map(CommandTaskType.Screen, worksPin: true), "a screen keeps its screen");
        AssertTrue(TacticalCommandTaskToStanceMapper.WorksPinHoldsStance(TacticalCommandTaskToStanceMapper.StanceAttack, true), "the #45 scorer's attack stance is held");
        AssertFalse(TacticalCommandTaskToStanceMapper.WorksPinHoldsStance(TacticalCommandTaskToStanceMapper.StanceDefend, true), "its defend is not");
        AssertFalse(TacticalCommandTaskToStanceMapper.WorksPinHoldsStance(-1, true), "nor its no-opinion");
    }

    static void WPinIntent()
    {
        var sink = new CaptureSink();
        AssertEqual(1, PostureIntentPlan.SubmitWorksPinStance(sink, 88, CommandTaskType.FixEnemy, IntentPriority.Executor, IntentSource.PostureExecutor, 5), "one intent");
        BattleOrderIntent i = sink.Intents[0];
        AssertEqual(OrderChannel.Stance, i.Channel, "on the stance channel");
        AssertEqual(2, i.Value, "stance 2");
        AssertEqual(CommandTaskType.FixEnemy, i.Task, "the task stays the fix's");
        AssertTrue((i.Flags & IntentFlags.WorksHold) != 0, "flagged, so vanilla's attack stances are taken");
        AssertEqual(PostureIntentPlan.WorksPinReason, i.Reason, "named for the pin");
        AssertEqual(88, i.GroupId, "for the group");
        AssertEqual(5, i.ExploitId, "carrying the exploit id");
        foreach (CommandTaskType task in new[] { CommandTaskType.Screen, CommandTaskType.Probe, CommandTaskType.HoldObjective, CommandTaskType.None })
            AssertEqual(0, PostureIntentPlan.SubmitWorksPinStance(sink, 88, task, IntentPriority.Executor, IntentSource.PostureExecutor, 0), task + " has no attack stance to lower");
        AssertEqual(0, PostureIntentPlan.SubmitWorksPinStance(sink, 0, CommandTaskType.FixEnemy, IntentPriority.Executor, IntentSource.PostureExecutor, 0), "no group, no intent");
        AssertEqual(1, sink.Intents.Count, "nothing else was submitted");
    }

    static void WPinWired()
    {
        string producer = FlankResponseTests.Flat(FlankResponseTests.Between(FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "PostureIntentProducer.cs"),
            "internal static bool Run(", "private static bool CanBypassStaleQueuedOrder"));
        int hold = producer.IndexOf("PostureIntentPlan.SubmitWorksHoldStance(", StringComparison.Ordinal);
        int pin = producer.IndexOf("army.PinsWorksNode(state.NodeId)", StringComparison.Ordinal);
        int decide = producer.IndexOf("CommandPostureExecutor.Decide(order, physical, eligibility", StringComparison.Ordinal);
        AssertTrue(hold > 0 && pin > hold && decide > pin, "the pin is asked after the hold's stance and before the posture decision");
        AssertTrue(producer.Contains("if (worksStance == 0 && recordTick && !exploit && army != null && army.PinsWorksNode(state.NodeId)) worksStance = PostureIntentPlan.SubmitWorksPinStance("),
            "only for a group the hold did not guard, on the producer's own pass; an exploit asset was made a hold before, and has the hold's stance");

        string runtime = FlankResponseTests.Flat(FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalBattleCoordinatorRuntime.cs"));
        AssertTrue(runtime.Contains("doctrineAction == DoctrineConsumerAction.Allow && side.Army != null && side.Army.PinsWorksNode(doctrineNodeId)) { cache[instanceId] = TacticalResolvedGroupDecision.ForWorksHold("),
            "#45's doctrine Allow of a pinned fix is a works-hold skip");
        AssertTrue(runtime.Contains("TacticalCommandTaskToStanceMapper.WorksPinHoldsStance(lStance, side.Army != null && side.Army.PinsWorksNode(state.NodeId))) { cache[instanceId] = TacticalResolvedGroupDecision.ForWorksHold("),
            "and so is its ledger step's attack stance");

        string army = FlankResponseTests.Flat(FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs"));
        AssertTrue(army.Contains("internal bool PinsWorksNode(string nodeId) { if (!Plugin.EnemyWorksDoctrineEnabled() || _feasibility == null) return false; int child = ChildIndexOfNode(nodeId); return child >= 0 && _feasibility.PinsWorks(_directChildIntents[child].ChildId); }"),
            "the node lookup is gated by the flag and reads the ledger's per-child pin");
    }
}
