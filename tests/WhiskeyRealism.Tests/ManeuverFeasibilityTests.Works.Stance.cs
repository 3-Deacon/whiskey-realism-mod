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
        ("bc14w WORKS-STANCE-PINS the pipeline submits the stance after every rewrite and before the decision, and no later reader undoes the guard", WStancePins),
    };

    static StanceWriteInput StanceInput(int ordered, int vanilla, bool worksHold, int macro = 1, StanceHoldRecord record = default(StanceHoldRecord),
        StanceWriteSource source = StanceWriteSource.Executor, float now = 1f)
        => new StanceWriteInput(true, 2, ordered, macro, now, record, source, vanilla, now, worksHold);

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
        AssertTrue(cavalry > 0 && flat.Substring(Math.Max(0, cavalry - 140), 140).Contains("!WorksHoldGuard.Holds(state.Task, state.Reason)"), "the cavalry follow leaves a guarded group alone");
        FlankResponseTests.AssertPinned(run, "TacticalOpportunityTaskOverride.Override(state.Task, DirectChildRole.Main, opportunity, WorksHoldGuard.Holds(state.Task, state.Reason), out overrideReason)",
            "the opportunity override is told of the hold (the node path and the no-order path)");
        FlankResponseTests.AssertPinned(run, "CommandPostureExecutor.Decide(order, physical, eligibility, (float)s.DoctrineHours, worksLane)", "a former fix with an open lane is planned to it");
        FlankResponseTests.AssertPinned(run, "WorksHoldGuard.LaneOpen(in order, army?.Feasibility?.HeldWorks)", "against the ledger's held works");
        FlankResponseTests.AssertPinned(run, "StanceSubmitted = worksStance > 0", "the request does not submit a second stance");
        int returns = 0;
        for (int at = flat.IndexOf("return worksStance > 0;", StringComparison.Ordinal); at >= 0; at = flat.IndexOf("return worksStance > 0;", at + 1, StringComparison.Ordinal)) returns++;
        AssertEqual(3, returns, "the three early exits after the stance count it as a submission");
        FlankResponseTests.AssertPinned(run, "int submitted = worksStance + PostureIntentPlan.Submit(sink, request);", "and so does the final count");
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
}
