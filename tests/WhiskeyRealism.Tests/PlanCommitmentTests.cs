using System;
using WhiskeyRealism.Tactical.Orchestrator;
using static TestHarness;

// Smoke 2026-10-17 (battle replan thrash): 20 replans in 4.6 doctrine hours, one every Min Replan Seconds (600 s = 0.167 h), because the
// cooldown was the only brake. A side commits to a plan for PlanCommitmentBattleSeconds; inside it only a crisis replans; a replan
// outside a crisis must beat the current plan by the score margin; a re-pick that lands on the plan already held changes nothing.
// MainEffortSectorLoss means men lost, not men who walked into the next band. docs/tactical-orchestrator.md.
internal static class PlanCommitmentTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("plan commitment holds a non-crisis trigger inside the commitment time", NonCrisisTriggerIsHeldInsideTheCommitment),
            ("plan commitment names the hold distinctly from the committed wings", TheHoldHasItsOwnSuppressionLiteral),
            ("plan commitment lets a non-crisis trigger through once the commitment has passed, with the score margin", NonCrisisTriggerAfterTheCommitmentKeepsThePlan),
            ("plan commitment a review outside a crisis keeps a plan its challenger beats by less than the margin, a crisis does not", TheMarginAppliesOutsideACrisisOnly),
            ("plan commitment lets a morale collapse replan inside the commitment", CrisisReplansInsideTheCommitment),
            ("plan commitment lets any trigger through while a group is outflanked severely", FlankCrisisUnlocksAnyTrigger),
            ("plan commitment classifies the crisis triggers", CrisisClassification),
            ("plan commitment a crisis re-pick that lands on the held plan keeps the release state", SamePlanRepickWipesNothing),
            ("plan commitment the cooldown is still the floor for a crisis", CrisisStillWaitsForTheCooldown),
            ("plan commitment a band relocation is not a main effort loss", RelocationIsNotLoss),
            ("plan commitment a band that lost men while the army lost men is a main effort loss", RealLossIsLoss),
            ("plan commitment a baseline taken with the main band empty does not read as a loss when the band fills", EmptyBandBaselineIsNotTheArmyTotal),
        });
    }

    private static readonly EnemyVisibleState Front = new EnemyVisibleState(
        new[] { new EnemyVisibleSector(2, 6000f, 6000f, false) }, 0.5f, true, false);

    private static ArmyOrchestrator Orch()
    {
        ArmyTickCycle.Reset();
        ReserveReleaseState.ResetAll();
        var orch = TacticalEvidenceFixTests.OrchestratorWithPlan(BattlePhase.MainEffort, mainSector: 2, aggression: 0.5f);
        // Adopt the plan the catalog itself picks for this evidence, so a later re-pick can land on it.
        orch.Replan(new ArmyEvidence(1f, TerrainKind.Open, 2));
        orch.RecordReplanBaseline(ReplanBaseline.Initial);
        return orch;
    }

    private static ReplanOutcome Step(ArmyOrchestrator orch, float battleSeconds, float morale = 1f, float reserves = 0.5f, float total = 6000f,
        EnemyVisibleState? enemy = null, int cooldown = 60)
        => ArmyTickCycle.MaybeReplan(orch, 0f, new ArmyEvidence(1f, TerrainKind.Open, 2), enemy ?? Front, total, morale, reserves, 0f, cooldown,
            0.2f, 0.9f, 0f, 0f, battleSeconds, double.NaN, null);

    private static void NonCrisisTriggerIsHeldInsideTheCommitment()
    {
        var orch = Orch();
        ReplanOutcome held = Step(orch, 600f, reserves: 0.9f);
        AssertEqual(ReplanTrigger.ReserveExhaustion, held.Trigger, "the trigger still fires");
        AssertFalse(held.Replanned, "but the plan is not re-picked inside the commitment");
        AssertFalse(held.Kept, "and it is not a kept replan either");
        AssertTrue(orch.PlanAgeSeconds >= 600f, "plan age keeps running: the hold did not reset it");
    }

    private static void TheHoldHasItsOwnSuppressionLiteral()
    {
        var orch = Orch();
        ReplanOutcome held = Step(orch, 600f, reserves: 0.9f);
        AssertEqual(ReplanOutcome.SuppressedByPlanHold, held.Suppression, "the time hold names itself");
        AssertTrue(ReplanOutcome.SuppressedByPlanHold != ReplanOutcome.SuppressedByCommitment, "and is not the committed wings of the maneuver monitor");
    }

    private static void NonCrisisTriggerAfterTheCommitmentKeepsThePlan()
    {
        var orch = Orch();
        BattlePlanId held = orch.CurrentPlan.PlanId;
        ReplanOutcome after = Step(orch, ArmyReplanTriggers.PlanCommitmentBattleSeconds + 1f, reserves: 0.9f);
        AssertEqual(ReplanTrigger.ReserveExhaustion, after.Trigger, "the trigger fires after the commitment");
        AssertTrue(after.Replanned || after.Kept, "and is acted on");
        AssertTrue(after.Kept, "the plan is held unless a challenger beats it by the margin");
        AssertEqual(held, orch.CurrentPlan.PlanId, "same plan");
        AssertNear(0.9f, orch.LastReplanBaseline.ReservesCommitted, 0.001f, "a kept replan refreshes the baseline so the edge does not refire");
    }

    private static void TheMarginAppliesOutsideACrisisOnly()
    {
        var ev = new ArmyEvidence(1f, TerrainKind.Open, 2);
        var unknown = new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null);
        // The catalog's own pick, and a plan the margin keeps although the pick is another one.
        var probe = Orch();
        probe.Replan(ev, unknown, hysteresis: false, "probe");
        BattlePlanId top = probe.CurrentPlan.PlanId;
        var into = new BattlePlanId[24];
        int n = probe.EligiblePlans(ev, into);
        BattlePlanId marginal = BattlePlanId.Unknown;
        for (int i = 0; i < n && marginal == BattlePlanId.Unknown; i++)
        {
            if (into[i] == top) continue;
            var o = Orch();
            o.SetPlanForTesting(new TacticalBattlePlan(into[i], BattlePhase.MainEffort, 2, null, null, 1.2f, 0f, 7));
            if (o.Replan(ev, unknown, hysteresis: true, "probe")) marginal = into[i];
        }
        AssertTrue(marginal != BattlePlanId.Unknown, "the fixture has a plan within the margin of the pick");

        var review = Orch();
        review.SetPlanForTesting(new TacticalBattlePlan(marginal, BattlePhase.MainEffort, 2, null, null, 1.2f, 0f, 7));
        ReplanOutcome after = Step(review, ArmyReplanTriggers.PlanCommitmentBattleSeconds + 1f, reserves: 0.9f);
        AssertEqual(ReplanTrigger.ReserveExhaustion, after.Trigger, "a review, not a crisis");
        AssertTrue(after.Kept, "kept");
        AssertEqual(marginal, review.CurrentPlan.PlanId, "the plan within the margin stays");

        var crisis = Orch();
        crisis.SetPlanForTesting(new TacticalBattlePlan(marginal, BattlePhase.MainEffort, 2, null, null, 1.2f, 0f, 7));
        ReplanOutcome c = Step(crisis, 600f, morale: 0.3f);
        AssertEqual(ReplanTrigger.CasualtyThreshold, c.Trigger, "a crisis");
        AssertEqual(top, crisis.CurrentPlan.PlanId, "re-picks without the margin");
    }

    private static void CrisisReplansInsideTheCommitment()
    {
        var orch = Orch();
        ReplanOutcome crisis = Step(orch, 600f, morale: 0.3f);
        AssertEqual(ReplanTrigger.CasualtyThreshold, crisis.Trigger, "morale collapse");
        AssertTrue(crisis.Replanned || crisis.Kept, "is acted on at once");
    }

    private static void FlankCrisisUnlocksAnyTrigger()
    {
        var orch = Orch();
        var outflanked = new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 6000f, 6000f, false) }, 0.5f, true, false,
            instanceFlankThreat: new System.Collections.Generic.Dictionary<int, float> { { 7, TacticalFrontAxis.FlankThreatSevere } });
        ReplanOutcome o = Step(orch, 600f, reserves: 0.9f, enemy: outflanked);
        AssertEqual(ReplanTrigger.ReserveExhaustion, o.Trigger, "the same non-crisis trigger");
        AssertTrue(o.Replanned || o.Kept, "an enemy on the flank or rear lifts the hold");
        var noticed = new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 6000f, 6000f, false) }, 0.5f, true, false,
            instanceFlankThreat: new System.Collections.Generic.Dictionary<int, float> { { 7, TacticalFrontAxis.FlankThreatSevere - 0.1f } });
        var orch2 = Orch();
        ReplanOutcome quiet = Step(orch2, 600f, reserves: 0.9f, enemy: noticed);
        AssertFalse(quiet.Replanned || quiet.Kept, "a flank threat below severe does not");
    }

    private static ReplanTriggerInput Input(float oddsNow = 1f, float oddsThen = 1f, bool flankCrisis = false)
        => new ReplanTriggerInput(0f, BattlePhase.MainEffort, 6000f, 6000f, oddsNow, oddsThen, 1f, 0.4f, 0.5f, 0f, 0f, flankCrisis: flankCrisis);

    private static void CrisisClassification()
    {
        foreach (ReplanTrigger t in new[] { ReplanTrigger.PhaseDeadline, ReplanTrigger.MainEffortSectorLoss, ReplanTrigger.CasualtyThreshold, ReplanTrigger.ManeuverAssumptionBroken })
            AssertTrue(ArmyReplanTriggers.IsCrisis(t, Input()), t + " is a crisis");
        foreach (ReplanTrigger t in new[] { ReplanTrigger.EnemyIntentShift, ReplanTrigger.ReserveExhaustion, ReplanTrigger.ReinforcementArrival,
            ReplanTrigger.BreakthroughOpportunity, ReplanTrigger.MainEffortLocalBreakthrough, ReplanTrigger.OpportunityChange })
            AssertFalse(ArmyReplanTriggers.IsCrisis(t, Input()), t + " is not a crisis on its own");
        AssertTrue(ArmyReplanTriggers.IsCrisis(ReplanTrigger.ForceImbalanceShift, Input(oddsNow: 0.6f, oddsThen: 1f)), "odds falling below the low line is a crisis");
        AssertFalse(ArmyReplanTriggers.IsCrisis(ReplanTrigger.ForceImbalanceShift, Input(oddsNow: 5.5f, oddsThen: 1f)), "odds rising past the high line is an opportunity, not a crisis");
        AssertTrue(ArmyReplanTriggers.IsCrisis(ReplanTrigger.OpportunityChange, Input(flankCrisis: true)), "an outflanked line makes any trigger a crisis");
        AssertFalse(ArmyReplanTriggers.IsCrisis(ReplanTrigger.None, Input(flankCrisis: true)), "None is nothing");
    }

    private static void SamePlanRepickWipesNothing()
    {
        var orch = Orch();
        BattlePlanId held = orch.CurrentPlan.PlanId;
        ReserveReleaseState.ForAlliance(0).InitialReserves = 3;
        ReplanOutcome crisis = Step(orch, 600f, morale: 0.3f);
        AssertTrue(crisis.Replanned || crisis.Kept, "the crisis re-picked");
        AssertEqual(held, orch.CurrentPlan.PlanId, "and landed on the plan already held");
        AssertTrue(crisis.Kept, "which is a kept plan, not a new one");
        AssertEqual(3, ReserveReleaseState.ForAlliance(0).InitialReserves, "so the staggered release state was not cleared");
    }

    private static void CrisisStillWaitsForTheCooldown()
    {
        var orch = Orch();
        ReplanOutcome first = Step(orch, 600f, morale: 0.3f, cooldown: 600);
        AssertTrue(first.Replanned || first.Kept, "first crisis acts");
        // 100 s later the main band has lost two thirds of its men: a second crisis, inside the 600 s floor.
        ReplanOutcome second = Step(orch, 100f, morale: 0.3f, enemy: Band(2000f), total: 2600f, cooldown: 600);
        AssertEqual(ReplanTrigger.MainEffortSectorLoss, second.Trigger, "the second crisis fires");
        AssertFalse(second.Replanned || second.Kept, "but Min Replan Seconds still applies to a crisis");
    }

    private static EnemyVisibleState Band(float own) => new EnemyVisibleState(new[] { new EnemyVisibleSector(2, own, 5000f, false) }, 0.5f, true, false);

    private static void RelocationIsNotLoss()
    {
        var orch = Orch();
        Step(orch, 600f, enemy: Band(6000f), total: 6000f);
        // The wing marched into the next band: the band is a third of what it was, the army lost nobody.
        ReplanOutcome o = Step(orch, 600f, enemy: Band(2000f), total: 6000f);
        AssertTrue(o.Trigger != ReplanTrigger.MainEffortSectorLoss, "units that walked out of the band are not casualties: " + o.Trigger);
    }

    private static void RealLossIsLoss()
    {
        var orch = Orch();
        Step(orch, 600f, enemy: Band(6000f), total: 6000f);
        ReplanOutcome o = Step(orch, 600f, enemy: Band(2000f), total: 2600f);
        AssertEqual(ReplanTrigger.MainEffortSectorLoss, o.Trigger, "the band lost two thirds and the army lost 3400 men");
        AssertTrue(o.Replanned || o.Kept, "and that is a crisis: it is acted on inside the commitment");
    }

    private static void EmptyBandBaselineIsNotTheArmyTotal()
    {
        var orch = Orch();
        // A replan while the main band holds nobody: the old code stored the whole army (6000) as that band's baseline.
        var emptyBand = new EnemyVisibleState(new[] { new EnemyVisibleSector(1, 6000f, 5000f, false) }, 0.5f, true, false);
        ReplanOutcome c = Step(orch, 600f, morale: 0.3f, enemy: emptyBand, total: 6000f);
        AssertTrue(c.Replanned || c.Kept, "a replan happened with the main band empty");
        Step(orch, 600f, morale: 1f, enemy: emptyBand, total: 6000f);
        // The band fills with a corps of 2000: that is where the army stands, not a loss.
        // (The army has lost most of its men elsewhere meanwhile, which would pass the old baseline's loss test: 6000 "in the band" down to 2000.)
        ReplanOutcome filled = Step(orch, 4000f, enemy: new EnemyVisibleState(new[] { new EnemyVisibleSector(2, 2000f, 5000f, false) }, 0.5f, true, false), total: 2400f);
        AssertTrue(filled.Trigger != ReplanTrigger.MainEffortSectorLoss, "the first sighting of a band is a baseline, not a loss: " + filled.Trigger);
    }
}

// The runtime halves (excluded from the test project) are pinned to the pure pieces they hand their memory to.
internal static class ReplanThrashWiringTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("replan thrash wiring: the executor hands its last assignment to the next one for the same plan and side", ExecutorKeepsRects),
            ("replan thrash wiring: the army holds roles through the role hold and keys them on plan, side, phase and band", ArmyHoldsRoles),
            ("replan thrash wiring: the ledger hands its target hold to the doctrine build", LedgerHoldsTargets),
            ("replan thrash wiring: the coordinator reports a look that started no mission", CoordinatorReportsLookIdle),
        });
    }

    private static void ExecutorKeepsRects()
    {
        string body = FlankResponseTests.SourceText("Tactical", "Orchestrator", "Plays", "PlayExecutorRuntime.cs");
        FlankResponseTests.AssertPinned(body, "_lastComputePlan[allianceId] == planId && _lastComputeMirrored[allianceId] == mirrored ? cached : null;", "only the same plan and side");
        FlankResponseTests.AssertPinned(body, "PlayCorpsAssignment.Assign(worldRects, corpsPositions, objectives, held);", "and the memory reaches the assignment");
    }

    private static void ArmyHoldsRoles()
    {
        string body = FlankResponseTests.SourceText("Tactical", "Orchestrator", "ArmyOrchestrator.cs");
        FlankResponseTests.AssertPinned(body, "_directChildIntents = _roleHold.Apply(_directChildIntents, RoleHoldPlanKey(), HoursSource());", "the allocation passes through the hold on the doctrine clock");
        FlankResponseTests.AssertPinned(body, "=> unchecked((((int)_plan.PlanId * 31 + (_plan.Mirrored ? 1 : 0)) * 31 + (int)_plan.Phase) * 31 + _plan.MainEffortSector);", "keyed on plan, side, phase and band");
    }

    private static void LedgerHoldsTargets()
    {
        string body = FlankResponseTests.SourceText("Tactical", "Orchestrator", "TacticalOperationsLedgerRuntime.cs");
        FlankResponseTests.AssertPinned(body, "reverseSlope, _targetHold);", "the doctrine build gets the ledger's hold");
        FlankResponseTests.AssertPinned(body, "_targetHold.Clear();", "and a ledger that does not run forgets it");
    }

    private static void CoordinatorReportsLookIdle()
    {
        string body = FlankResponseTests.SourceText("Tactical", "Battle", "Producers", "ReconIntentProducer.cs");
        FlankResponseTests.AssertPinned(body, "if ((r.Events & ReconEvents.LookIdle) != 0)", "the producer reports the idle look");
        FlankResponseTests.AssertPinned(body, "Row(s, alliance, ReconEvents.LookIdle, r.LookIdleReason ?? \"idle\", 0, 0f, 0f);", "with its reason");
    }
}
