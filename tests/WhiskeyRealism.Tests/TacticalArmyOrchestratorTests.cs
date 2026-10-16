using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WhiskeyRealism;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.PlayerOrders;
using WhiskeyRealism.Util;

using static TestFixtures;
using static TestHarness;

internal static class TacticalArmyOrchestratorTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("critical understrength sector holds", CriticalUnderstrengthSectorHolds),
            ("noncritical understrength sector is economy of force", NoncriticalUnderstrengthSectorEconomyOfForce),
            ("army orchestrator update operations ledger replaces snapshots", ArmyOrchestratorUpdateOperationsLedgerReplacesSnapshots),
            ("army orchestrator clears doctrine orders when off", ArmyOrchestratorClearsDoctrineOrdersWhenOff),
            ("opportunity hysteresis holds same category", OpportunityHysteresisHoldsSameCategory),
            ("opportunity hysteresis blocks transition within hold window", OpportunityHysteresisBlocksWithinHoldWindow),
            ("opportunity hysteresis allows transition after hold expires", OpportunityHysteresisAllowsAfterHold),
            ("opportunity hysteresis escalates to withdrawal immediately", OpportunityHysteresisEscalatesToWithdrawal),
            ("opportunity hysteresis accepts first decision", OpportunityHysteresisAcceptsFirstDecision),
            ("army masses for plan target when hierarchy exists", ArmyMassesForPlanTargetWhenHierarchyExists),
            ("army intent inference unknown when no visible sectors", ArmyIntentInferenceUnknownWhenNoVisibleSectors),
            ("army intent inference concentration in one sector implies attack", ArmyIntentInferenceConcentrationInOneSectorImpliesAttack),
            ("army intent inference single sector strong contact stays finite", ArmyIntentInferenceSingleSectorStrongContactStaysFinite),
            ("army intent inference unconcentrated reserves uncommitted implies probe", ArmyIntentInferenceUnconcentratedReservesUncommittedImpliesProbe),
            ("army intent inference contact broken implies withdraw", ArmyIntentInferenceContactBrokenImpliesWithdraw),
            ("army intent inference receiving fire implies defend", ArmyIntentInferenceReceivingFireImpliesDefend),
            ("army intent inference confidence floor below threshold", ArmyIntentInferenceConfidenceFloorBelowThreshold),
            ("army intent inference for frontage filters by sector", ArmyIntentInferenceForFrontageFiltersBySector),
            ("army intent inference for frontage empty mask returns unknown", ArmyIntentInferenceForFrontageEmptyMaskReturnsUnknown),
            ("army orchestrator new has no plan until picked", ArmyOrchestratorNewHasNoPlanUntilPicked),
            ("army orchestrator pick initial plan with lee personality assigns lee envelopment", ArmyOrchestratorPickInitialPlanWithLeePersonalityAssignsLeeEnvelopment),
            ("army orchestrator records history on initial plan", ArmyOrchestratorRecordsHistoryOnInitialPlan),
            ("army orchestrator tick advances age without replanning", ArmyOrchestratorTickAdvancesAgeWithoutReplanning),
            ("army orchestrator replan with intent resets age and updates history", ArmyOrchestratorReplanWithIntentResetsAgeAndUpdatesHistory),
            ("army orchestrator replan without intent leaves intent unknown", ArmyOrchestratorReplanWithoutIntentLeavesIntentUnknown),
            ("army orchestrator failed replan preserves active state", ArmyOrchestratorFailedReplanPreservesActiveState),
            ("army orchestrator register direct children stores snapshots", ArmyOrchestratorRegisterDirectChildrenStoresSnapshots),
            ("army orchestrator observe evidence allocates roles", ArmyOrchestratorObserveEvidenceAllocatesRoles),
            ("army orchestrator get direct child role unknown when unregistered", ArmyOrchestratorGetDirectChildRoleUnknownWhenUnregistered),
            ("army orchestrator refreshes expanded direct child snapshots", ArmyOrchestratorRefreshesExpandedDirectChildSnapshots),
            ("army orchestrator returns role for synth army child id", ArmyOrchestratorReturnsRoleForSynthArmyChildId),
            ("army orchestrator registers command tree snapshot", TestArmyOrchestratorRegistersCommandTree),
            ("army orchestrator preserves O3 direct child role after command tree allocation", TestArmyOrchestratorPreservesDirectChildRoleWithCommandTree),
            ("army orchestrator resolves command node intent after direct child evidence", TestArmyOrchestratorResolvesCommandNodeIntent),
            ("army orchestrator command resolver falls back to O3 direct child intent", TestArmyOrchestratorCommandResolverFallsBackToDirectChildIntent),
            ("army replan triggers phase deadline fires when age exceeds phase budget", ArmyReplanTriggersPhaseDeadlineFiresWhenAgeExceedsPhaseBudget),
            ("army replan triggers main effort sector loss fires below threshold", ArmyReplanTriggersMainEffortSectorLossFiresBelowThreshold),
            ("army replan triggers force imbalance shift fires when odds cross hysteresis", ArmyReplanTriggersForceImbalanceShiftFiresWhenOddsCrossHysteresis),
            ("army replan triggers casualty threshold fires when morale below floor", ArmyReplanTriggersCasualtyThresholdFiresWhenMoraleBelowFloor),
            ("army replan triggers reserve exhaustion fires at 85 percent committed", ArmyReplanTriggersReserveExhaustionFiresAt85PercentCommitted),
            ("army replan triggers reinforcement arrival fires on nonzero delta", ArmyReplanTriggersReinforcementArrivalFiresOnNonzeroDelta),
            ("army replan triggers enemy intent shift fires when confidence weighted exceeds floor", ArmyReplanTriggersEnemyIntentShiftFiresWhenConfidenceWeightedExceedsFloor),
            ("army replan triggers none when all conditions normal", ArmyReplanTriggersNoneWhenAllConditionsNormal),
            ("army replan triggers ignore reserve exhaustion when fraction is unknown sentinel", ArmyReplanTriggersIgnoreReserveExhaustionWhenFractionIsUnknownSentinel),
            ("army replan triggers still fire reserve exhaustion above 0.85 known fraction", ArmyReplanTriggersStillFireReserveExhaustionAbove85PercentKnownFraction),
            ("army replan triggers at sentinel boundary do not trip reserve exhaustion", ArmyReplanTriggersAtSentinelBoundaryDoNotTripReserveExhaustion),
            ("army orchestrator envelopment min ratio constant is 1.2 baseline", ArmyOrchestratorEnvelopmentMinRatioConstantIs12),
            ("army orchestrator envelopment reserve commit cap is 1.1", ArmyOrchestratorEnvelopmentReserveCommitCapIs11),
            ("army orchestrator envelopment simultaneous aggression threshold is 0.6", ArmyOrchestratorEnvelopmentSimultaneousAggressionThresholdIs06),
            // Slice A: phase progression doctrine
            ("phase progression probe holds when odds neutral and budget unspent", PhaseProgressionProbeHoldsWhenNeutral),
            ("phase progression probe commits to main effort when odds favorable", PhaseProgressionProbeCommitsWhenFavorable),
            ("phase progression probe budget elapses with poor odds yields consolidate", PhaseProgressionProbeBudgetElapsesPoorOddsConsolidate),
            ("phase progression main effort exploits on local breakthrough", PhaseProgressionMainEffortExploitsOnLocalBreakthrough),
            ("phase progression main effort consolidates on odds revert deep commit", PhaseProgressionMainEffortConsolidatesOnRevert),
            ("phase progression morale floor breach withdraws from any phase", PhaseProgressionMoraleFloorWithdraws),
            ("phase progression withdraw is absorbing", PhaseProgressionWithdrawIsAbsorbing),
            ("phase progression aggressive commander shortens probe budget", PhaseProgressionAggressiveShortensProbeBudget),
            ("phase progression cautious commander lengthens probe budget", PhaseProgressionCautiousLengthensProbeBudget),
            ("phase progression exploit ends on reserves exhausted", PhaseProgressionExploitEndsOnReservesExhausted),
            ("army orchestrator evaluate and advance updates plan phase", ArmyOrchestratorEvaluateAndAdvanceUpdatesPlanPhase),
            // Slice B: sector-driven main-effort shift
            ("army orchestrator consider main effort shift swaps sector in probe phase", ArmyOrchestratorConsiderMainEffortShiftSwapsInProbe),
            ("army orchestrator consider main effort shift blocked in withdraw", ArmyOrchestratorConsiderMainEffortShiftBlockedInWithdraw),
            ("army orchestrator consider main effort shift noop on same sector", ArmyOrchestratorConsiderMainEffortShiftNoopOnSame),
            ("battle plan with main effort sector replaces field", BattlePlanWithMainEffortSectorReplacesField),
            // Slice C: offensive replan triggers
            ("replan triggers fire main effort local breakthrough on odds spike", ReplanTriggersFireMainEffortLocalBreakthroughOnSpike),
            ("replan triggers fire breakthrough opportunity on off axis sector", ReplanTriggersFireBreakthroughOpportunityOnOffAxis),
            ("replan triggers offensive triggers do not fire in consolidate phase", ReplanTriggersOffensiveDoNotFireInConsolidate),
            ("replan triggers defensive triggers still take precedence", ReplanTriggersDefensiveTakePrecedence),
            // Sector readiness doctrine (fresh-troops decision)
            ("sector readiness push ready when effective force covers ratio", SectorReadinessPushReadyWhenEffectiveCoversRatio),
            ("sector readiness hold for reinforcements when relief tips balance", SectorReadinessHoldForReinforcementsWhenReliefTips),
            ("sector readiness push degraded when aggressive and no relief", SectorReadinessPushDegradedAggressiveNoRelief),
            ("sector readiness hold fatigued when cautious and no relief", SectorReadinessHoldFatiguedCautiousNoRelief),
            ("sector readiness fatigue ammo morale collapse effective strength", SectorReadinessFatigueAmmoMoraleCollapseEffective),
            ("sector readiness degraded force health always holds", SectorReadinessDegradedForceHealthAlwaysHolds),
            ("sector readiness is scale invariant across small and large armies", SectorReadinessIsScaleInvariant),
            ("sector readiness reinforcement window scales with aggression", SectorReadinessReinforcementWindowScalesWithAggression),
            ("phase progression probe blocks commit when hold for reinforcements", PhaseProgressionProbeBlocksCommitOnHoldForReinforcements),
            ("phase progression probe blocks commit when hold fatigued", PhaseProgressionProbeBlocksCommitOnHoldFatigued),
            ("phase progression probe push degraded still commits", PhaseProgressionProbePushDegradedStillCommits),
            ("phase progression exploit blocks on hold for reinforcements", PhaseProgressionExploitBlocksOnHoldForReinforcements),
            // Full wiring: ArmyTickCycle.MaybeReplan readiness path
            ("army tick cycle fresh army advances phase to main effort on favorable odds", ArmyTickCycleFreshAdvancesOnFavorableOdds),
            ("army tick cycle exhausted army with relief holds in probe", ArmyTickCycleExhaustedWithReliefHoldsInProbe),
            ("army tick cycle exhausted aggressive commander still commits", ArmyTickCycleExhaustedAggressiveStillCommits),
            ("army tick cycle exhausted cautious commander holds fatigued", ArmyTickCycleExhaustedCautiousHoldsFatigued),
            ("army tick cycle legacy overload preserves prior behavior", ArmyTickCycleLegacyOverloadPreservesPriorBehavior),
            ("army tick cycle wires consider main effort shift from enemy visible state", ArmyTickCycleWiresConsiderMainEffortShift),
            ("army tick cycle wires offensive trigger inputs from enemy visible state", ArmyTickCycleWiresOffensiveTriggerInputs),
            // Time compression handling
            ("consider main effort shift respects hysteresis margin", ConsiderMainEffortShiftRespectsHysteresisMargin),
            ("consider main effort shift legacy overload always shifts", ConsiderMainEffortShiftLegacyOverloadAlwaysShifts),
            ("army tick cycle 20x compression burns probe budget in battle time", ArmyTickCycle20xCompressionBurnsProbeInBattleTime),
            ("army orchestrator live commander initiative defaults mid band", ArmyOrchestratorLiveCommanderInitiativeDefaultsMidBand),
            ("army orchestrator live commander initiative clamps to range", ArmyOrchestratorLiveCommanderInitiativeClampsToRange),
            ("army orchestrator live commander initiative rejects nan and infinity", ArmyOrchestratorLiveCommanderInitiativeRejectsNaNAndInfinity),
            ("army tick cycle no trigger when all conditions normal", ArmyTickCycleNoTriggerWhenAllConditionsNormal),
            ("army tick cycle phase deadline fires", ArmyTickCyclePhaseDeadlineFires),
            ("army tick cycle rate limits replan within min replan seconds", ArmyTickCycleRateLimitsReplanWithinMinReplanSeconds),
            ("army tick cycle rate limit is per alliance clock", ArmyTickCycleRateLimitIsPerAllianceClock),
            ("army tick cycle reset clears battle lifetime rate limit", ArmyTickCycleResetClearsBattleLifetimeRateLimit),
            ("army tick cycle updates observed intent without replan", ArmyTickCycleUpdatesObservedIntentWithoutReplan),
            ("army tick cycle enemy intent shift fires when confident enemy attacks", ArmyTickCycleEnemyIntentShiftFiresWhenConfidentEnemyAttacks),
            ("army tick cycle no replan if orchestrator has no plan", ArmyTickCycleNoReplanIfOrchestratorHasNoPlan),
        });
    }

    private static void CriticalUnderstrengthSectorHolds()
    {
        var ledger = BuildLedger();
        AssertEqual(FrontPosture.Hold, ledger.GetSector("Richmond").Posture);
    }

    private static void ArmyOrchestratorUpdateOperationsLedgerReplacesSnapshots()
    {
        var army = NewArmyOrchestratorWithPlan();
        var first = new TacticalOperationsLedgerRuntime();
        first.Replace(
            TacticalCommanderMode.MonitorOnly,
            new[] { ObjectiveRecordFor("ridge-a", enemyStrength: 70f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "first", "first-campaign"),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));

        army.UpdateOperationsLedger(first, new[]
        {
            new CommandNodeOperationalState(
                "node-a",
                CommandEchelonKind.CorpsLike,
                CommandNodeRole.MainEffort,
                CommandTaskType.AttackObjective,
                CommandTaskState.Planning),
        });

        AssertEqual(TacticalCommanderMode.MonitorOnly, army.CommanderMode, "mode");
        AssertEqual("ridge-a", army.CurrentOperation.PrimaryObjectiveId, "operation");
        AssertEqual(1, army.CurrentCommandOperations.Count, "command operation count");
        AssertEqual("first-campaign", army.CurrentStrategicBattleIntent.CampaignIntent, "strategic intent");

        var second = new TacticalOperationsLedgerRuntime();
        second.Replace(
            TacticalCommanderMode.Off,
            Array.Empty<ObjectiveRecord>(),
            StrategicBattleIntentSnapshot.Empty,
            new ForceAvailabilitySnapshot(0f, 0f),
            new PersonalityVector(0f, 0f, 0f, 0f, 0f));

        army.UpdateOperationsLedger(second, Array.Empty<CommandNodeOperationalState>());

        AssertEqual(TacticalCommanderMode.Off, army.CommanderMode, "replaced mode");
        AssertEqual(0, army.CurrentCommandOperations.Count, "replaced command operations");
        AssertEqual("objective-unknown", army.CurrentOperation.PrimaryObjectiveId, "replaced operation");
        AssertEqual(string.Empty, army.CurrentStrategicBattleIntent.CampaignIntent, "replaced strategic intent");
    }

    private static void ArmyOrchestratorClearsDoctrineOrdersWhenOff()
    {
        ArmyOrchestrator army = NewArmyOrchestratorWithPlan();
        var ledger = new TacticalOperationsLedgerRuntime();
        ledger.Replace(
            TacticalCommanderMode.Active,
            new[] { ObjectiveRecordFor("ridge-a", enemyStrength: 70f, friendlyAssignedStrength: 100f) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        army.UpdateOperationsLedger(ledger, new[]
        {
            new CommandNodeOperationalState(
                "node-a",
                CommandEchelonKind.CorpsLike,
                CommandNodeRole.MainEffort,
                CommandTaskType.AttackObjective,
                CommandTaskState.Planning),
        });

        AssertEqual(1, army.CurrentDoctrineOrders.Count, "orders populated");

        army.ApplyTacticalCommanderMode(TacticalCommanderMode.Off);

        AssertEqual(0, army.CurrentDoctrineOrders.Count, "orders cleared");
    }

    private static void NoncriticalUnderstrengthSectorEconomyOfForce()
    {
        var ledger = BuildLedger();
        AssertEqual(FrontPosture.EconomyOfForce, ledger.GetSector("Coast").Posture);
    }

    private static void ArmyMassesForPlanTargetWhenHierarchyExists()
    {
        var snap = Snapshot("army", 0, 16, 50000f, 30000f, FormationLevel.Army, FrontPosture.Exploit);
        snap.GrandArmyStructureAvailable = true;
        snap.IsPlanTargetArea = true;

        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.TotalWar1864, "VirginiaCapitalCorridor");
        var assignment = ledger.GetAssignment("army");

        AssertEqual(FormationDirective.Mass, assignment.Directive);
        AssertEqual(true, assignment.OffensiveAllowed);
    }

    private static void ArmyIntentInferenceUnknownWhenNoVisibleSectors()
    {
        var enemy = new EnemyVisibleState(System.Array.Empty<EnemyVisibleSector>(), 0f, false, false);

        var model = ArmyIntentInference.Build(enemy);

        AssertEqual(InferredIntent.Unknown, model.PrimaryIntent, "no sectors infers Unknown");
        AssertNear(0f, model.Confidence01, 1e-5f, "no sectors confidence");
        AssertEqual(0, model.SupportingEvidence.Length, "no sectors has no evidence");
    }

    private static void ArmyIntentInferenceConcentrationInOneSectorImpliesAttack()
    {
        var enemy = new EnemyVisibleState(
            new[]
            {
                new EnemyVisibleSector(0, 5000f, 8500f, false),
                new EnemyVisibleSector(1, 5000f, 1200f, false),
                new EnemyVisibleSector(2, 5000f, 1300f, false)
            },
            0.7f,
            true,
            false);

        var model = ArmyIntentInference.Build(enemy);

        AssertEqual(InferredIntent.Attack, model.PrimaryIntent, "concentration + committed reserve infers Attack");
        AssertEqual(0, model.InferredMainEffort, "sector 0 is max enemy strength");
        AssertTrue(model.Confidence01 >= 0.5f, "attack confidence >= 0.5");
        AssertTrue(System.Array.IndexOf(model.SupportingEvidence, EvidenceTag.SectorConcentration) >= 0, "attack evidence includes concentration");
        AssertTrue(System.Array.IndexOf(model.SupportingEvidence, EvidenceTag.ReserveCommitted) >= 0, "attack evidence includes reserve committed");
        AssertTrue(System.Array.IndexOf(model.SupportingEvidence, EvidenceTag.ContactSpotted) >= 0, "attack evidence includes contact spotted");
    }

    private static void ArmyIntentInferenceSingleSectorStrongContactStaysFinite()
    {
        var enemy = new EnemyVisibleState(
            new[] { new EnemyVisibleSector(0, 5000f, 7000f, false) },
            0.7f,
            true,
            false);

        var model = ArmyIntentInference.Build(enemy);

        AssertEqual(InferredIntent.Attack, model.PrimaryIntent, "single sector strong contact infers Attack");
        AssertEqual(0, model.InferredMainEffort, "single sector is main effort");
        AssertTrue(model.Confidence01 >= ArmyIntentInference.ConfidenceFloor, "single sector confidence reaches floor");
        AssertTrue(System.Array.IndexOf(model.SupportingEvidence, EvidenceTag.ReserveCommitted) >= 0, "single sector evidence includes reserve committed");
        AssertTrue(System.Array.IndexOf(model.SupportingEvidence, EvidenceTag.ContactSpotted) >= 0, "single sector evidence includes contact spotted");
    }

    private static void ArmyIntentInferenceUnconcentratedReservesUncommittedImpliesProbe()
    {
        var enemy = new EnemyVisibleState(
            new[]
            {
                new EnemyVisibleSector(0, 5000f, 2000f, false),
                new EnemyVisibleSector(1, 5000f, 2200f, false),
                new EnemyVisibleSector(2, 5000f, 2100f, false)
            },
            0.1f,
            true,
            false);

        var model = ArmyIntentInference.Build(enemy);

        AssertEqual(InferredIntent.Probe, model.PrimaryIntent, "unconcentrated + uncommitted reserve infers Probe");
        AssertTrue(System.Array.IndexOf(model.SupportingEvidence, EvidenceTag.ReserveUncommitted) >= 0, "probe evidence includes reserve uncommitted");
    }

    private static void ArmyIntentInferenceContactBrokenImpliesWithdraw()
    {
        var enemy = new EnemyVisibleState(
            new[] { new EnemyVisibleSector(0, 5000f, 1000f, false) },
            0f,
            false,
            true);

        var model = ArmyIntentInference.Build(enemy);

        AssertEqual(InferredIntent.Withdraw, model.PrimaryIntent, "broken contact with low visible enemy infers Withdraw");
        AssertTrue(model.Confidence01 >= ArmyIntentInference.ConfidenceFloor, "hard withdraw signal reaches confidence floor");
        AssertTrue(System.Array.IndexOf(model.SupportingEvidence, EvidenceTag.ContactBroken) >= 0, "withdraw evidence includes contact broken");
    }

    private static void ArmyIntentInferenceReceivingFireImpliesDefend()
    {
        var enemy = new EnemyVisibleState(
            new[]
            {
                new EnemyVisibleSector(0, 4000f, 6000f, true),
                new EnemyVisibleSector(1, 4000f, 6500f, true)
            },
            0.6f,
            true,
            false);

        var model = ArmyIntentInference.Build(enemy);

        AssertEqual(InferredIntent.Defend, model.PrimaryIntent, "recent fire in multiple sectors with committed reserve infers Defend");
        AssertTrue(System.Array.IndexOf(model.SupportingEvidence, EvidenceTag.ReceivingFire) >= 0, "defend evidence includes receiving fire");
    }

    private static void ArmyIntentInferenceConfidenceFloorBelowThreshold()
    {
        var enemy = new EnemyVisibleState(
            new[] { new EnemyVisibleSector(0, 5000f, 100f, false) },
            0f,
            false,
            false);

        var model = ArmyIntentInference.Build(enemy);

        AssertTrue(model.Confidence01 < 0.3f, "low signal confidence remains below floor");
        AssertEqual(InferredIntent.Unknown, model.PrimaryIntent, "below confidence floor infers Unknown");
    }

    private static void ArmyIntentInferenceForFrontageFiltersBySector()
    {
        var enemy = new EnemyVisibleState(
            sectors: new[]
            {
                new EnemyVisibleSector(0, 1000f,  500f, false),
                new EnemyVisibleSector(2, 2000f, 4000f, true),  // child sector — strong enemy + recent fire
                new EnemyVisibleSector(4, 1000f,  500f, false),
            },
            enemyReserveCommitFraction: 0.5f,
            anyContactSpotted: true,
            anyContactBroken: false);

        var intent = ArmyIntentInference.BuildForFrontage(primarySector: 2, enemy);
        AssertTrue(intent.PrimaryIntent != InferredIntent.Unknown,
            "frontage-filtered single-sector enemy should yield non-Unknown when fire and reserve evidence present");
        AssertEqual(2, intent.InferredMainEffort);
    }

    private static void ArmyIntentInferenceForFrontageEmptyMaskReturnsUnknown()
    {
        var enemy = new EnemyVisibleState(
            sectors: new[] { new EnemyVisibleSector(0, 100f, 100f, false) },
            enemyReserveCommitFraction: 0f,
            anyContactSpotted: false,
            anyContactBroken: false);
        var intent = ArmyIntentInference.BuildForFrontage(primarySector: 99, enemy);
        AssertEqual(InferredIntent.Unknown, intent.PrimaryIntent);
    }

    private static void ArmyOrchestratorNewHasNoPlanUntilPicked()
    {
        var orch = new ArmyOrchestrator(allianceId: 0, catalog: SeedCatalog.AllHistoricalAndGeneric(), commanderPersonality: default);
        AssertFalse(orch.HasPlan, "new orchestrator has no plan");
    }

    private static void ArmyOrchestratorPickInitialPlanWithLeePersonalityAssignsLeeEnvelopment()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(currentOdds: 1.1f, terrain: TerrainKind.Wooded, defaultMainEffortSector: 0));
        AssertTrue(orch.HasPlan, "plan picked");
        AssertEqual(BattlePlanId.DoubleEnvelopment, orch.CurrentPlan.PlanId, "Lee personality + wooded + 1.1 odds picks lee-envelopment");
        AssertEqual(BattlePhase.Probe, orch.CurrentPlan.Phase, "initial phase is Probe");
    }

    private static void ArmyOrchestratorRecordsHistoryOnInitialPlan()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);

        orch.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Wooded, 0));

        AssertNear(1.4f, orch.HistoryGlobalOdds, 1e-5f, "initial plan records global odds history");
        AssertNear(0f, orch.PlanAgeSeconds, 1e-5f, "initial plan resets plan age");
    }

    private static void ArmyOrchestratorTickAdvancesAgeWithoutReplanning()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Wooded, 0));

        orch.AdvancePlanAge(15f);
        orch.AdvancePlanAge(20f);

        AssertNear(35f, orch.PlanAgeSeconds, 1e-5f, "positive ticks accumulate plan age");
    }

    private static void ArmyOrchestratorReplanWithIntentResetsAgeAndUpdatesHistory()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Wooded, 0));
        orch.AdvancePlanAge(60f);
        var enemyIntent = new TacticalIntentModel(InferredIntent.Defend, 1, 0.7f, 0f, null);

        orch.Replan(new ArmyEvidence(0.8f, TerrainKind.Open, 1), enemyIntent);

        AssertNear(0f, orch.PlanAgeSeconds, 1e-5f, "replan resets plan age");
        AssertNear(0.8f, orch.HistoryGlobalOdds, 1e-5f, "replan updates global odds history");
        AssertEqual(InferredIntent.Defend, orch.CurrentIntentModel.PrimaryIntent, "current intent model stores last consumed intent");
    }

    private static void ArmyOrchestratorReplanWithoutIntentLeavesIntentUnknown()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Wooded, 0));

        orch.Replan(new ArmyEvidence(1.0f, TerrainKind.Wooded, 0));

        AssertEqual(InferredIntent.Unknown, orch.CurrentIntentModel.PrimaryIntent, "legacy replan uses unknown intent");
    }

    private static void ArmyOrchestratorFailedReplanPreservesActiveState()
    {
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var catalog = SeedCatalog.AllHistoricalAndGeneric();
        var orch = new ArmyOrchestrator(1, catalog, lee);
        orch.PickInitialPlan(new ArmyEvidence(1.4f, TerrainKind.Wooded, 0));
        orch.AdvancePlanAge(45f);
        var oldPlanId = orch.CurrentPlan.PlanId;

        var field = typeof(TacticalPlaybookCatalog).GetField("_playbooks", BindingFlags.NonPublic | BindingFlags.Instance);
        var list = (List<TacticalPlaybook>)field.GetValue(catalog);
        list.Clear();

        orch.Replan(
            new ArmyEvidence(0.7f, TerrainKind.Open, 2),
            new TacticalIntentModel(InferredIntent.Attack, 2, 0.9f, 0f, null));

        AssertTrue(orch.HasPlan, "failed replan preserves active plan flag");
        AssertEqual(oldPlanId, orch.CurrentPlan.PlanId, "failed replan preserves active plan");
        AssertNear(45f, orch.PlanAgeSeconds, 1e-5f, "failed replan preserves age");
        AssertNear(1.4f, orch.HistoryGlobalOdds, 1e-5f, "failed replan preserves history odds");
        AssertEqual(InferredIntent.Unknown, orch.CurrentIntentModel.PrimaryIntent, "failed replan preserves previous intent");
    }

    private static void ArmyOrchestratorRegisterDirectChildrenStoresSnapshots()
    {
        var orch = NewArmyOrchestratorWithPlan();
        orch.RegisterDirectChildren(new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "First", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Second", true),
        });
        AssertEqual(2, orch.CurrentDirectChildIntents.Count);
        AssertEqual("c0", orch.CurrentDirectChildIntents[0].ChildId);
        AssertEqual(DirectChildRole.Unknown, orch.CurrentDirectChildIntents[0].Role); // no evidence yet
    }

    private static void ArmyOrchestratorObserveEvidenceAllocatesRoles()
    {
        var orch = NewArmyOrchestratorWithPlan(mainSector: 2);
        orch.RegisterDirectChildren(new[]
        {
            new DirectChildSnapshot("c0", "a", 15, 0, "First", true),
            new DirectChildSnapshot("c1", "a", 15, 0, "Second", true),
        });
        orch.ObserveDirectChildEvidenceWithZones(new[]
        {
            new DirectChildEvidence(1, 1, false, 0, 0, 0.3f),
            new DirectChildEvidence(3, 1, true,  2, 0, 0.7f),
        }, null, null);
        AssertEqual(DirectChildRole.Main, orch.GetDirectChildRole("c1"));
    }

    private static void ArmyOrchestratorGetDirectChildRoleUnknownWhenUnregistered()
    {
        var orch = NewArmyOrchestratorWithPlan();
        AssertEqual(DirectChildRole.Unknown, orch.GetDirectChildRole("never-registered"));
    }

    private static void ArmyOrchestratorRefreshesExpandedDirectChildSnapshots()
    {
        var orch = NewArmyOrchestratorWithPlan(mainSector: 2);
        bool first = orch.RegisterDirectChildrenIfChanged(new[]
        {
            new DirectChildSnapshot("child-100", "army-1", 15, 0, "First", true),
        });
        AssertTrue(first, "initial registration changes snapshot set");

        bool same = orch.RegisterDirectChildrenIfChanged(new[]
        {
            new DirectChildSnapshot("child-100", "army-1", 15, 0, "First", true),
        });
        AssertFalse(same, "identical registration is ignored");

        bool expanded = orch.RegisterDirectChildrenIfChanged(new[]
        {
            new DirectChildSnapshot("child-100", "army-1", 15, 0, "First", true),
            new DirectChildSnapshot("child-200", "army-1", 15, 0, "Second", true),
        });
        AssertTrue(expanded, "expanded command tree refreshes direct child registration");
        AssertEqual(2, orch.CurrentDirectChildIntents.Count);
        AssertEqual("child-200", orch.CurrentDirectChildIntents[1].ChildId);
    }

    private static void ArmyOrchestratorReturnsRoleForSynthArmyChildId()
    {
        // When DirectChildDiscovery synthesizes an army-root snapshot (zero qualifying
        // direct children), its ChildId is "synth-army-{instanceId}". The orchestrator
        // must return the assigned role for that exact id so #42's fallback lookup engages.
        var orch = NewArmyOrchestratorWithPlan(mainSector: 2);
        orch.RegisterDirectChildren(new[]
        {
            new DirectChildSnapshot("synth-army-12345", "army-12345", 16, 0, "ArmyA", true),
        });
        orch.ObserveDirectChildEvidenceWithZones(new[]
        {
            new DirectChildEvidence(3, 1, true, 2, 0, 0.7f),
        }, null, null);
        AssertEqual(DirectChildRole.Main, orch.GetDirectChildRole("synth-army-12345"));
        AssertEqual(DirectChildRole.Unknown, orch.GetDirectChildRole("child-12345"),
            "orchestrator must distinguish synth-army-{id} from child-{id} by exact match");
    }

    private static void TestArmyOrchestratorRegistersCommandTree()
    {
        var army = NewArmyOrchestratorWithPlan();
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "Army", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "Corps", true, false, false),
        }, 1, 0);

        army.RegisterCommandTree(tree);

        AssertEqual("node-100", army.CurrentCommandTree.RootNodeId);
        AssertEqual(2, army.CurrentCommandTree.Nodes.Count);
        AssertEqual(2, army.CurrentCommandNodeIntents.Count);
    }

    private static void TestArmyOrchestratorPreservesDirectChildRoleWithCommandTree()
    {
        var army = NewArmyOrchestratorWithPlan();
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "Army", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "Corps", true, false, false),
        }, 1, 0);

        army.RegisterDirectChildren(new[]
        {
            new DirectChildSnapshot("child-200", "army-100", 15, 0, "Corps", true),
        });
        army.RegisterCommandTree(tree);
        army.ObserveDirectChildEvidenceWithZones(new[]
        {
            new DirectChildEvidence(3, 1, true, 2, 0, 0.7f),
        }, new[]
        {
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>()),
        }, null);

        AssertEqual(DirectChildRole.Main, army.GetDirectChildRole("child-200"), "O3 direct child role should remain authoritative");
        var resolution = army.ResolveCommandIntentForGroup(200);
        AssertTrue(resolution.Found, "command resolver should find node-200");
        AssertEqual(DirectChildRole.Main, resolution.Intent.Role, "command node should mirror direct child role");
    }

    private static void TestArmyOrchestratorResolvesCommandNodeIntent()
    {
        var army = NewArmyOrchestratorWithPlan();
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "Army", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "Corps", true, false, false),
            new CommandTreeBuilder.CommandProbe(300, 200, 1, 14, "Division", true, false, false),
        }, 1, 0);

        army.RegisterDirectChildren(new[]
        {
            new DirectChildSnapshot("child-200", "army-100", 15, 0, "Corps", true),
        });
        army.RegisterCommandTree(tree);
        army.ObserveDirectChildEvidenceWithZones(new[]
        {
            new DirectChildEvidence(3, 1, true, 2, 0, 0.7f),
        }, null, null);

        var parent = army.ResolveCommandIntentForGroup(200);
        var child = army.ResolveCommandIntentForGroup(300);
        AssertTrue(parent.Found, "parent command node resolves");
        AssertTrue(child.Found, "deeper command node resolves");
        AssertEqual(DirectChildRole.Main, parent.Intent.Role);
        AssertEqual(DirectChildRole.Main, child.Intent.Role);
        AssertEqual("node-200", child.Intent.SourceNodeId);
    }

    private static void TestArmyOrchestratorCommandResolverFallsBackToDirectChildIntent()
    {
        var army = NewArmyOrchestratorWithPlan();
        army.RegisterDirectChildren(new[]
        {
            new DirectChildSnapshot("synth-army-200", "army-200", 15, 0, "Detached Corps", true),
        });
        army.ObserveDirectChildEvidenceWithZones(new[]
        {
            new DirectChildEvidence(1, 3, true, 4, 0, 0.8f),
        }, new[]
        {
            new TacticalIntentModel(InferredIntent.Attack, 4, 0.8f, 0.5f, Array.Empty<EvidenceTag>()),
        }, null);

        var resolution = army.ResolveCommandIntentForGroup(200);

        AssertTrue(resolution.Found, "missing command tree should fall back to O3 direct child intent");
        AssertEqual("o3-direct-child-fallback", resolution.Reason);
        AssertEqual(army.GetDirectChildRole("synth-army-200"), resolution.Intent.Role);
        AssertEqual(4, resolution.Intent.PrimarySector);
    }

    private static void ArmyReplanTriggersPhaseDeadlineFiresWhenAgeExceedsPhaseBudget()
    {
        var input = new ReplanTriggerInput(
            planAgeSeconds: ArmyReplanTriggers.PlanDeadlineBattleSeconds + 20f, currentPhase: BattlePhase.Probe,
            mainEffortOwnStrength: 5000f, mainEffortHistoryOwnStrength: 5000f,
            globalOddsCurrent: 1.0f, globalOddsHistory: 1.0f,
            armyMoraleCurrent: 1.0f, armyMoraleFloor: 0.4f,
            reservesCommittedFraction: 0.5f, ownReinforcementsArriving: 0f,
            enemyMainEffortShiftConfidenceWeighted: 0f);
        AssertEqual(ReplanTrigger.PhaseDeadline, ArmyReplanTriggers.Evaluate(input), "age past the battle-time plan deadline fires PhaseDeadline");
    }

    private static void ArmyReplanTriggersMainEffortSectorLossFiresBelowThreshold()
    {
        var input = new ReplanTriggerInput(
            planAgeSeconds: 30f, currentPhase: BattlePhase.MainEffort,
            mainEffortOwnStrength: 1500f, mainEffortHistoryOwnStrength: 5000f,  // 30% of historic
            globalOddsCurrent: 1.0f, globalOddsHistory: 1.0f,
            armyMoraleCurrent: 1.0f, armyMoraleFloor: 0.4f,
            reservesCommittedFraction: 0.5f, ownReinforcementsArriving: 0f,
            enemyMainEffortShiftConfidenceWeighted: 0f);
        AssertEqual(ReplanTrigger.MainEffortSectorLoss, ArmyReplanTriggers.Evaluate(input), "main-effort below 50% fires MainEffortSectorLoss");
    }

    private static void ArmyReplanTriggersForceImbalanceShiftFiresWhenOddsCrossHysteresis()
    {
        var below = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000f, 5000f, 0.65f, 1.5f, 1f, 0.4f, 0.5f, 0f, 0f);
        var above = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000f, 5000f, 1.5f, 1.0f, 1f, 0.4f, 0.5f, 0f, 0f);
        AssertEqual(ReplanTrigger.ForceImbalanceShift, ArmyReplanTriggers.Evaluate(below), "odds cross 0.7 downward fires ForceImbalanceShift");
        AssertEqual(ReplanTrigger.ForceImbalanceShift, ArmyReplanTriggers.Evaluate(above), "odds cross 1.4 upward fires ForceImbalanceShift");
    }

    private static void ArmyReplanTriggersCasualtyThresholdFiresWhenMoraleBelowFloor()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000f, 5000f, 1.0f, 1.0f, armyMoraleCurrent: 0.3f, armyMoraleFloor: 0.4f, 0.5f, 0f, 0f);
        AssertEqual(ReplanTrigger.CasualtyThreshold, ArmyReplanTriggers.Evaluate(input), "morale below floor fires CasualtyThreshold");
    }

    private static void ArmyReplanTriggersReserveExhaustionFiresAt85PercentCommitted()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000f, 5000f, 1.0f, 1.0f, 1f, 0.4f, reservesCommittedFraction: 0.9f, 0f, 0f);
        AssertEqual(ReplanTrigger.ReserveExhaustion, ArmyReplanTriggers.Evaluate(input), "reserves >=85% fires ReserveExhaustion");
    }

    private static void ArmyReplanTriggersReinforcementArrivalFiresOnNonzeroDelta()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000f, 5000f, 1.0f, 1.0f, 1f, 0.4f, 0.5f, ownReinforcementsArriving: 2500f, 0f);
        AssertEqual(ReplanTrigger.ReinforcementArrival, ArmyReplanTriggers.Evaluate(input), "reinforcements arrival fires ReinforcementArrival");
    }

    private static void ArmyReplanTriggersEnemyIntentShiftFiresWhenConfidenceWeightedExceedsFloor()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000f, 5000f, 1.0f, 1.0f, 1f, 0.4f, 0.5f, 0f, enemyMainEffortShiftConfidenceWeighted: 0.55f);
        AssertEqual(ReplanTrigger.EnemyIntentShift, ArmyReplanTriggers.Evaluate(input), "enemy shift >=0.5 fires EnemyIntentShift");
    }

    private static void ArmyReplanTriggersNoneWhenAllConditionsNormal()
    {
        var input = new ReplanTriggerInput(30f, BattlePhase.MainEffort, 5000f, 5000f, 1.0f, 1.0f, 1f, 0.4f, 0.5f, 0f, 0f);
        AssertEqual(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(input), "no trigger fires when conditions normal");
    }

    private static void ArmyReplanTriggersIgnoreReserveExhaustionWhenFractionIsUnknownSentinel()
    {
        // 0.35 is the UnknownReserveCommitSentinel from ArmyEvidenceBuilder/ArmyReplanTriggers.
        // It must NOT trip ReserveExhaustion. Pre-fix: ArmyEvidenceBuilder returned 1.0
        // when chains existed but had no reservegroups, which tripped this trigger every
        // cycle and produced the replan thrash observed in the 695be770... runtime.
        var input = new ReplanTriggerInput(
            planAgeSeconds: 10f, currentPhase: BattlePhase.Probe,
            mainEffortOwnStrength: 1000f, mainEffortHistoryOwnStrength: 1000f,
            globalOddsCurrent: 1.0f, globalOddsHistory: 1.0f,
            armyMoraleCurrent: 0.9f, armyMoraleFloor: 0.4f,
            reservesCommittedFraction: 0.35f,
            ownReinforcementsArriving: 0f,
            enemyMainEffortShiftConfidenceWeighted: 0.0f);
        AssertEqual(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(input),
            "0.35 unknown sentinel must not trip ReserveExhaustion");
    }

    private static void ArmyReplanTriggersStillFireReserveExhaustionAbove85PercentKnownFraction()
    {
        // Regression: a genuinely high committed fraction (not the sentinel) still fires.
        var input = new ReplanTriggerInput(
            planAgeSeconds: 10f, currentPhase: BattlePhase.Probe,
            mainEffortOwnStrength: 1000f, mainEffortHistoryOwnStrength: 1000f,
            globalOddsCurrent: 1.0f, globalOddsHistory: 1.0f,
            armyMoraleCurrent: 0.9f, armyMoraleFloor: 0.4f,
            reservesCommittedFraction: 0.90f,
            ownReinforcementsArriving: 0f,
            enemyMainEffortShiftConfidenceWeighted: 0.0f);
        AssertEqual(ReplanTrigger.ReserveExhaustion, ArmyReplanTriggers.Evaluate(input),
            "known fraction >= 0.85 still fires ReserveExhaustion");
    }

    private static void ArmyOrchestratorEnvelopmentMinRatioConstantIs12()
    {
        // Baseline lowered 1.5 → 1.2 on 2026-05-22 — historical envelopments
        // commonly fired well below 1.5×. Use EffectiveEnvelopmentMinRatio for
        // the personality-aware threshold per commander aggression.
        AssertNear(1.2f, ArmyOrchestrator.EnvelopmentMinRatio, 1e-6f,
            "EnvelopmentMinRatio = 1.2 baseline");
    }

    private static void ArmyOrchestratorEnvelopmentReserveCommitCapIs11()
    {
        // The reserve-commit-trigger-odds cap when envelopment is active is
        // 1.1, tight enough to make defensive playbooks (Longstreet=1.5,
        // McClellan=1.6) commit reserves sooner under attack pressure, but
        // not so tight that it overrides already-aggressive playbooks
        // (Hood=1.0, Jackson=1.0, GenericDesperate=0.9 keep their defaults).
        AssertNear(1.1f, ArmyOrchestrator.EnvelopmentReserveCommitOddsCap, 1e-6f,
            "EnvelopmentReserveCommitOddsCap = 1.1");
    }

    private static void ArmyOrchestratorEnvelopmentSimultaneousAggressionThresholdIs06()
    {
        AssertNear(0.6f, ArmyOrchestrator.EnvelopmentSimultaneousAggressionThreshold, 1e-6f,
            "EnvelopmentSimultaneousAggressionThreshold = 0.6");
    }

    // ---- Slice A: phase progression doctrine ----

    private static TacticalPhaseProgressionDoctrine.Input MakePhaseInput(
        BattlePhase phase,
        float planAgeSeconds = 0f,
        float globalOddsCurrent = 1f,
        float globalOddsHistory = 1f,
        float mainEffortOddsCurrent = 1f,
        float mainEffortOddsHistory = 1f,
        float armyMoraleCurrent = 1f,
        float armyMoraleFloor = 0.2f,
        float reservesCommittedFraction = 0.2f,
        float commanderAggression01 = 0.5f) =>
        new TacticalPhaseProgressionDoctrine.Input(
            phase, planAgeSeconds, globalOddsCurrent, globalOddsHistory,
            mainEffortOddsCurrent, mainEffortOddsHistory,
            armyMoraleCurrent, armyMoraleFloor,
            reservesCommittedFraction, commanderAggression01);

    private static void PhaseProgressionProbeHoldsWhenNeutral()
    {
        var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
            BattlePhase.Probe, planAgeSeconds: 10f,
            globalOddsCurrent: 0.95f, mainEffortOddsCurrent: 0.95f));
        AssertEqual(BattlePhase.Probe, d.NextPhase,
            "probe holds when neither budget elapsed nor favorable odds; got " + d.Reason);
    }

    private static void PhaseProgressionProbeCommitsWhenFavorable()
    {
        // Global AND main-effort odds clear the 1.0 floor → commit to main effort
        var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
            BattlePhase.Probe, planAgeSeconds: 5f,
            globalOddsCurrent: 1.3f, mainEffortOddsCurrent: 1.2f));
        AssertEqual(BattlePhase.MainEffort, d.NextPhase,
            "probe commits to main effort when odds favorable; got " + d.Reason);
    }

    private static void PhaseProgressionProbeBudgetElapsesPoorOddsConsolidate()
    {
        // Budget elapsed, global odds below 0.9 → consolidate (don't commit
        // to a bad attack just because the clock ran out)
        var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
            BattlePhase.Probe, planAgeSeconds: TacticalPhaseProgressionDoctrine.ProbeBudgetForAggression(0.5f) + 60f,
            globalOddsCurrent: 0.7f, mainEffortOddsCurrent: 0.7f,
            commanderAggression01: 0.5f));
        AssertEqual(BattlePhase.Consolidate, d.NextPhase,
            "probe budget elapsed + poor odds → consolidate; got " + d.Reason);
    }

    private static void PhaseProgressionMainEffortExploitsOnLocalBreakthrough()
    {
        // Main effort odds spike from 1.0 history → 1.5 current (ratio 1.5 > 1.35)
        // AND reserves committed >= 0.4 → exploit
        var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
            BattlePhase.MainEffort, planAgeSeconds: 30f,
            mainEffortOddsCurrent: 1.5f, mainEffortOddsHistory: 1.0f,
            reservesCommittedFraction: 0.5f));
        AssertEqual(BattlePhase.Exploit, d.NextPhase,
            "main effort exploits on local breakthrough; got " + d.Reason);
    }

    private static void PhaseProgressionMainEffortConsolidatesOnRevert()
    {
        // Global odds drop to 0.8 (below 0.9 floor) AND deep commit (0.7 reserves)
        // → consolidate to lock down gains rather than push further
        var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
            BattlePhase.MainEffort, planAgeSeconds: 30f,
            globalOddsCurrent: 0.8f,
            reservesCommittedFraction: 0.7f));
        AssertEqual(BattlePhase.Consolidate, d.NextPhase,
            "main effort consolidates on odds revert + deep commit; got " + d.Reason);
    }

    private static void PhaseProgressionMoraleFloorWithdraws()
    {
        // Morale below floor overrides any other rule from any phase
        foreach (var phase in new[] { BattlePhase.Probe, BattlePhase.MainEffort,
                                       BattlePhase.Exploit, BattlePhase.Consolidate })
        {
            var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
                phase, armyMoraleCurrent: 0.1f, armyMoraleFloor: 0.2f));
            AssertEqual(BattlePhase.Withdraw, d.NextPhase,
                "morale floor breach withdraws from " + phase + "; got " + d.Reason);
        }
    }

    private static void PhaseProgressionWithdrawIsAbsorbing()
    {
        // Once withdrawing, even high odds don't pull us back to attack
        var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
            BattlePhase.Withdraw, globalOddsCurrent: 2.0f, armyMoraleCurrent: 1.0f));
        AssertEqual(BattlePhase.Withdraw, d.NextPhase,
            "withdraw is absorbing; got " + d.Reason);
    }

    private static void PhaseProgressionAggressiveShortensProbeBudget()
    {
        // Aggressive commander (aggression01=1.0) -> 20 battle-minute probe
        // budget. At 30 battle-minutes (past budget) with neutral odds, commit.
        var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
            BattlePhase.Probe, planAgeSeconds: 30f * 60f,
            globalOddsCurrent: 0.95f, mainEffortOddsCurrent: 0.95f,
            commanderAggression01: 1.0f));
        AssertEqual(BattlePhase.MainEffort, d.NextPhase,
            "aggressive 20-minute probe budget commits at 30 minutes; got " + d.Reason);
        AssertNear(20f * 60f, TacticalPhaseProgressionDoctrine.ProbeBudgetForAggression(1.0f), 0.01f,
            "aggression=1.0 -> 20 battle-minute probe budget");
    }

    private static void PhaseProgressionCautiousLengthensProbeBudget()
    {
        // Cautious commander (aggression01=0.0) -> 60 battle-minute budget.
        // At 30 battle-minutes with neutral odds, still probing.
        var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
            BattlePhase.Probe, planAgeSeconds: 30f * 60f,
            globalOddsCurrent: 0.95f, mainEffortOddsCurrent: 0.95f,
            commanderAggression01: 0.0f));
        AssertEqual(BattlePhase.Probe, d.NextPhase,
            "cautious 60-minute probe budget still probing at 30 minutes; got " + d.Reason);
        AssertNear(60f * 60f, TacticalPhaseProgressionDoctrine.ProbeBudgetForAggression(0.0f), 0.01f,
            "aggression=0.0 -> 60 battle-minute probe budget");
    }

    private static void PhaseProgressionExploitEndsOnReservesExhausted()
    {
        // Reserves >= 0.9 → exit exploit to consolidate (force spent)
        var d = TacticalPhaseProgressionDoctrine.Decide(MakePhaseInput(
            BattlePhase.Exploit, planAgeSeconds: 30f,
            reservesCommittedFraction: 0.95f));
        AssertEqual(BattlePhase.Consolidate, d.NextPhase,
            "exploit ends on reserves exhausted; got " + d.Reason);
    }

    private static void ArmyOrchestratorEvaluateAndAdvanceUpdatesPlanPhase()
    {
        // End-to-end: orchestrator with Lee plan in Probe phase → favorable
        // odds → EvaluateAndAdvancePhase should swap to MainEffort
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(1.1f, TerrainKind.Wooded, 0));
        AssertEqual(BattlePhase.Probe, orch.CurrentPlan.Phase, "plan starts in Probe");

        var decision = orch.EvaluateAndAdvancePhase(
            globalOddsCurrent: 1.4f,
            mainEffortOddsCurrent: 1.3f,
            mainEffortOddsHistory: 1.1f,
            armyMoraleCurrent: 1.0f,
            armyMoraleFloor: 0.2f,
            reservesCommittedFraction: 0.2f);
        AssertEqual(BattlePhase.MainEffort, decision.NextPhase, "doctrine recommends MainEffort");
        AssertEqual(BattlePhase.MainEffort, orch.CurrentPlan.Phase,
            "orchestrator's plan advanced to MainEffort");
        AssertNear(0f, orch.PlanAgeSeconds, 1e-5f, "phase advance resets plan age");
    }

    // ---- Slice B: sector-driven main-effort shift ----

    private static void ArmyOrchestratorConsiderMainEffortShiftSwapsInProbe()
    {
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(1.1f, TerrainKind.Wooded, defaultMainEffortSector: 0));
        AssertEqual(0, orch.CurrentPlan.MainEffortSector, "plan starts on sector 0");

        bool shifted = orch.ConsiderMainEffortShift(2, float.MaxValue, 0f);
        AssertTrue(shifted, "shift returns true when sector changes");
        AssertEqual(2, orch.CurrentPlan.MainEffortSector,
            "main effort shifted to sector 2");
    }

    private static void ArmyOrchestratorConsiderMainEffortShiftBlockedInWithdraw()
    {
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(1.1f, TerrainKind.Wooded, defaultMainEffortSector: 0));
        orch.AdvancePhase(BattlePhase.Withdraw);

        bool shifted = orch.ConsiderMainEffortShift(2, float.MaxValue, 0f);
        AssertTrue(!shifted, "withdraw phase blocks main-effort shifts");
        AssertEqual(0, orch.CurrentPlan.MainEffortSector,
            "main effort unchanged during withdraw");
    }

    private static void ArmyOrchestratorConsiderMainEffortShiftNoopOnSame()
    {
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(1.1f, TerrainKind.Wooded, defaultMainEffortSector: 1));

        bool shifted = orch.ConsiderMainEffortShift(1, float.MaxValue, 0f);
        AssertTrue(!shifted, "no-op when decisive sector equals current");
    }

    private static void BattlePlanWithMainEffortSectorReplacesField()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment, BattlePhase.Probe,
            mainEffortSector: 0,
            fixingSectors: new[] { 1 }, screeningSectors: new[] { 2 },
            reserveCommitTriggerOdds: 1.2f,
            ageSeconds: 10f, jitterSeed: 42);
        var moved = plan.WithMainEffortSector(3);
        AssertEqual(3, moved.MainEffortSector, "main effort sector replaced");
        AssertEqual(BattlePhase.Probe, moved.Phase, "phase preserved");
        AssertEqual(BattlePlanId.DoubleEnvelopment, moved.PlanId, "plan id preserved");
        AssertNear(10f, moved.AgeSeconds, 1e-5f, "age preserved");
        AssertNear(1.2f, moved.ReserveCommitTriggerOdds, 1e-5f, "trigger odds preserved");
    }

    // ---- Slice C: offensive replan triggers ----

    private static ReplanTriggerInput MakeReplanInput(
        BattlePhase phase = BattlePhase.MainEffort,
        float planAgeSeconds = 30f,
        float mainEffortOwn = 1000f, float mainEffortHistory = 1000f,
        float globalOddsCurrent = 1.0f, float globalOddsHistory = 1.0f,
        float morale = 1.0f, float moraleFloor = 0.2f,
        float reserves = 0.3f,
        float reinforcements = 0f,
        float enemyShift = 0f,
        float mainEffortLocalOdds = 1f, float mainEffortLocalOddsHistory = 1f,
        float bestNonMainEffortSectorOdds = 0f, float currentMainEffortOdds = 0f) =>
        new ReplanTriggerInput(
            planAgeSeconds, phase, mainEffortOwn, mainEffortHistory,
            globalOddsCurrent, globalOddsHistory, morale, moraleFloor,
            reserves, reinforcements, enemyShift,
            mainEffortLocalOdds, mainEffortLocalOddsHistory,
            bestNonMainEffortSectorOdds, currentMainEffortOdds);

    private static void ReplanTriggersFireMainEffortLocalBreakthroughOnSpike()
    {
        // Main effort sector odds spike 1.0 -> 1.5 (ratio 1.5 > threshold 1.35)
        // AND reserves committed >= 0.3 (offensive trigger min) → MainEffortLocalBreakthrough
        var input = MakeReplanInput(
            phase: BattlePhase.MainEffort,
            reserves: 0.4f,
            mainEffortLocalOdds: 1.5f,
            mainEffortLocalOddsHistory: 1.0f);
        var trigger = ArmyReplanTriggers.Evaluate(input);
        AssertEqual(ReplanTrigger.MainEffortLocalBreakthrough, trigger,
            "local odds spike fires breakthrough trigger");
    }

    private static void ReplanTriggersFireBreakthroughOpportunityOnOffAxis()
    {
        // Off-axis sector odds 2.0 vs current main effort 1.0 (margin 1.0 > threshold 0.5)
        // in MainEffort phase → BreakthroughOpportunity
        var input = MakeReplanInput(
            phase: BattlePhase.MainEffort,
            reserves: 0.4f,
            bestNonMainEffortSectorOdds: 2.0f,
            currentMainEffortOdds: 1.0f);
        var trigger = ArmyReplanTriggers.Evaluate(input);
        AssertEqual(ReplanTrigger.BreakthroughOpportunity, trigger,
            "off-axis odds gap fires opportunity trigger");
    }

    private static void ReplanTriggersOffensiveDoNotFireInConsolidate()
    {
        // Same spike inputs in Consolidate phase — offensive triggers must NOT fire
        var input = MakeReplanInput(
            phase: BattlePhase.Consolidate,
            reserves: 0.4f,
            mainEffortLocalOdds: 1.5f,
            mainEffortLocalOddsHistory: 1.0f,
            bestNonMainEffortSectorOdds: 2.0f,
            currentMainEffortOdds: 1.0f);
        var trigger = ArmyReplanTriggers.Evaluate(input);
        AssertTrue(trigger != ReplanTrigger.MainEffortLocalBreakthrough &&
                   trigger != ReplanTrigger.BreakthroughOpportunity,
            "offensive triggers blocked in consolidate; got " + trigger);
    }

    private static void ReplanTriggersDefensiveTakePrecedence()
    {
        // Morale floor breach AND offensive opportunity present — defensive
        // trigger (CasualtyThreshold) must fire first
        var input = MakeReplanInput(
            phase: BattlePhase.MainEffort,
            morale: 0.1f, moraleFloor: 0.2f,
            reserves: 0.4f,
            mainEffortLocalOdds: 1.5f,
            mainEffortLocalOddsHistory: 1.0f);
        var trigger = ArmyReplanTriggers.Evaluate(input);
        AssertEqual(ReplanTrigger.CasualtyThreshold, trigger,
            "morale floor wins over offensive triggers");
    }

    // ---- Sector readiness (fresh-troops decision) ----

    private static TacticalSectorReadinessDoctrine.Input MakeReadiness(
        float own = 8000f,
        float fatigue = 0.2f,
        float ammo = 0.9f,
        float morale = 0.9f,
        float enemy = 5000f,
        float reinforcementHours = 0f,
        float reinforcementStrength = 0f,
        float aggression = 0.5f) =>
        new TacticalSectorReadinessDoctrine.Input(
            own, fatigue, ammo, morale, enemy,
            reinforcementHours, reinforcementStrength, aggression);

    private static void SectorReadinessPushReadyWhenEffectiveCoversRatio()
    {
        // 8000 own at fatigue=0.2, ammo=0.9, morale=0.9 → effective ~5184
        // vs enemy=3500 → ratio ~1.48, mid commander threshold ~1.10 → PushReady
        var d = TacticalSectorReadinessDoctrine.Decide(MakeReadiness(
            own: 8000f, fatigue: 0.2f, ammo: 0.9f, morale: 0.9f,
            enemy: 3500f, aggression: 0.5f));
        AssertEqual(TacticalSectorReadinessDoctrine.Result.PushReady, d.Result,
            "fresh force covers ratio → PushReady, got reason=" + d.Reason);
    }

    private static void SectorReadinessHoldForReinforcementsWhenReliefTips()
    {
        // 8000 own at fatigue=0.6 (tired), ammo=0.5, morale=0.7 → effective
        // 8000*0.4*0.5*0.7 = 1120 — way below MinViable 1500 — actually returns
        // HoldFatigued via the min-viable guard. Use larger own to test the
        // proper HoldForReinforcements path.
        //
        // 12000 own at fatigue=0.6, ammo=0.6, morale=0.7 → effective ~2016
        // vs enemy=4000 → ratio 0.50 (below mid threshold ~1.10)
        // Reinforcements 3500 strong arriving in 2h → after relief: 5516
        // future ratio = 5516/4000 = 1.38 — clears threshold → HoldForReinforcements
        var d = TacticalSectorReadinessDoctrine.Decide(MakeReadiness(
            own: 12000f, fatigue: 0.5f, ammo: 0.7f, morale: 0.8f,
            enemy: 8500f,
            reinforcementHours: 2f, reinforcementStrength: 6000f,
            aggression: 0.5f));
        AssertEqual(TacticalSectorReadinessDoctrine.Result.HoldForReinforcements, d.Result,
            "tired force + relief that tips balance → HoldForReinforcements, got reason=" + d.Reason);
    }

    private static void SectorReadinessPushDegradedAggressiveNoRelief()
    {
        // Same tired-force scenario but NO relief AND aggressive commander
        // (0.85 aggression, above PressVsHoldAggressionThreshold 0.55) →
        // PushDegraded (presses anyway)
        var d = TacticalSectorReadinessDoctrine.Decide(MakeReadiness(
            own: 12000f, fatigue: 0.5f, ammo: 0.7f, morale: 0.8f,
            enemy: 8500f,
            reinforcementHours: 0f, reinforcementStrength: 0f,
            aggression: 0.85f));
        AssertEqual(TacticalSectorReadinessDoctrine.Result.PushDegraded, d.Result,
            "aggressive + no relief presses anyway → PushDegraded, got reason=" + d.Reason);
    }

    private static void SectorReadinessHoldFatiguedCautiousNoRelief()
    {
        // Same tired-force scenario, NO relief, cautious commander (0.15
        // aggression) → HoldFatigued (preserve force)
        var d = TacticalSectorReadinessDoctrine.Decide(MakeReadiness(
            own: 12000f, fatigue: 0.5f, ammo: 0.7f, morale: 0.8f,
            enemy: 8500f,
            reinforcementHours: 0f, reinforcementStrength: 0f,
            aggression: 0.15f));
        AssertEqual(TacticalSectorReadinessDoctrine.Result.HoldFatigued, d.Result,
            "cautious + no relief holds → HoldFatigued, got reason=" + d.Reason);
    }

    private static void SectorReadinessFatigueAmmoMoraleCollapseEffective()
    {
        // Effective force calc: 10000 * (1 - 0.5) * 0.5 * 0.5 = 1250 effective
        // vs nominal 10000 — a 5x degradation from fatigue/ammo/morale stack.
        var input = MakeReadiness(
            own: 10000f, fatigue: 0.5f, ammo: 0.5f, morale: 0.5f, enemy: 5000f);
        AssertNear(1250f, input.OwnEffectiveStrength, 0.5f,
            "effective force degraded by fatigue×ammo×morale stack; got " + input.OwnEffectiveStrength);
    }

    private static void SectorReadinessDegradedForceHealthAlwaysHolds()
    {
        // Force at <25% effectiveness regardless of HEADCOUNT must HoldFatigued.
        // 5000 own at fatigue 0.75, ammo 0.4, morale 0.5 → effective
        // 5000*0.25*0.4*0.5 = 250 → forceHealth = 0.05 (well below 0.25).
        // Must HoldFatigued even with maxed aggression — the army is too
        // degraded to push effectively.
        var d = TacticalSectorReadinessDoctrine.Decide(MakeReadiness(
            own: 5000f, fatigue: 0.75f, ammo: 0.4f, morale: 0.5f,
            enemy: 100f, aggression: 1.0f));
        AssertEqual(TacticalSectorReadinessDoctrine.Result.HoldFatigued, d.Result,
            "degraded force health always holds even with maxed aggression; got " + d.Reason);
    }

    private static void SectorReadinessIsScaleInvariant()
    {
        // Fresh army at any size should reach PushReady with favorable ratio.
        // Tests the user's 2026-05-19 concern: GTCW campaigns dynamically
        // produce 1,500 to 60,000-soldier armies; the doctrine must work
        // across the full range, not just the mid-band.
        //
        // Three armies: 1500 (small skirmish), 8000 (corps), 50000 (Gettysburg-
        // scale). All fresh (fatigue 0.1, ammo 0.95, morale 1.0). All vs a
        // 60% enemy (ratio 1.67). All should PushReady — scale invariance
        // means the doctrine doesn't gate on headcount, only on ratio + state.
        foreach (var size in new float[] { 1500f, 8000f, 50000f })
        {
            var d = TacticalSectorReadinessDoctrine.Decide(MakeReadiness(
                own: size, fatigue: 0.1f, ammo: 0.95f, morale: 1.0f,
                enemy: size * 0.6f, aggression: 0.5f));
            AssertEqual(TacticalSectorReadinessDoctrine.Result.PushReady, d.Result,
                "size=" + size + " fresh + 1.67x ratio → PushReady; got " + d.Reason);
        }
    }

    private static void SectorReadinessReinforcementWindowScalesWithAggression()
    {
        // Aggressive commanders won't wait long; cautious will wait up to 6h.
        float aggressive = TacticalSectorReadinessDoctrine.ComputeReinforcementWaitHours(1.0f);
        float mid = TacticalSectorReadinessDoctrine.ComputeReinforcementWaitHours(0.5f);
        float cautious = TacticalSectorReadinessDoctrine.ComputeReinforcementWaitHours(0.0f);
        AssertNear(2f, aggressive, 0.001f, "aggressive wait ~2h");
        AssertNear(4f, mid, 0.001f, "mid wait ~4h");
        AssertNear(6f, cautious, 0.001f, "cautious wait ~6h");
        AssertTrue(cautious > aggressive, "cautious waits longer");
    }

    private static void PhaseProgressionProbeBlocksCommitOnHoldForReinforcements()
    {
        // Favorable odds (global 1.3, main effort 1.2) BUT readiness says
        // HoldForReinforcements → stay in Probe (don't commit to a push
        // with fatigued troops when relief is imminent)
        var input = new TacticalPhaseProgressionDoctrine.Input(
            BattlePhase.Probe,
            planAgeSeconds: 30f,
            globalOddsCurrent: 1.3f,
            globalOddsHistory: 1.1f,
            mainEffortOddsCurrent: 1.2f,
            mainEffortOddsHistory: 1.0f,
            armyMoraleCurrent: 1.0f,
            armyMoraleFloor: 0.2f,
            reservesCommittedFraction: 0.2f,
            commanderAggression01: 0.5f,
            mainEffortReadiness: TacticalSectorReadinessDoctrine.Result.HoldForReinforcements);
        var d = TacticalPhaseProgressionDoctrine.Decide(input);
        AssertEqual(BattlePhase.Probe, d.NextPhase,
            "HoldForReinforcements blocks commit despite favorable odds; got reason=" + d.Reason);
    }

    private static void PhaseProgressionProbeBlocksCommitOnHoldFatigued()
    {
        // Same scenario but readiness HoldFatigued → still blocks commit
        var input = new TacticalPhaseProgressionDoctrine.Input(
            BattlePhase.Probe,
            planAgeSeconds: 30f,
            globalOddsCurrent: 1.3f,
            globalOddsHistory: 1.1f,
            mainEffortOddsCurrent: 1.2f,
            mainEffortOddsHistory: 1.0f,
            armyMoraleCurrent: 1.0f,
            armyMoraleFloor: 0.2f,
            reservesCommittedFraction: 0.2f,
            commanderAggression01: 0.2f,
            mainEffortReadiness: TacticalSectorReadinessDoctrine.Result.HoldFatigued);
        var d = TacticalPhaseProgressionDoctrine.Decide(input);
        AssertEqual(BattlePhase.Probe, d.NextPhase,
            "HoldFatigued blocks commit even with favorable odds; got reason=" + d.Reason);
    }

    private static void PhaseProgressionProbePushDegradedStillCommits()
    {
        // PushDegraded permits commit (aggressive commander accepts the risk)
        var input = new TacticalPhaseProgressionDoctrine.Input(
            BattlePhase.Probe,
            planAgeSeconds: 30f,
            globalOddsCurrent: 1.3f,
            globalOddsHistory: 1.1f,
            mainEffortOddsCurrent: 1.2f,
            mainEffortOddsHistory: 1.0f,
            armyMoraleCurrent: 1.0f,
            armyMoraleFloor: 0.2f,
            reservesCommittedFraction: 0.2f,
            commanderAggression01: 0.85f,
            mainEffortReadiness: TacticalSectorReadinessDoctrine.Result.PushDegraded);
        var d = TacticalPhaseProgressionDoctrine.Decide(input);
        AssertEqual(BattlePhase.MainEffort, d.NextPhase,
            "PushDegraded permits aggressive commit; got reason=" + d.Reason);
    }

    private static void PhaseProgressionExploitBlocksOnHoldForReinforcements()
    {
        // Local breakthrough detected AND reserves committed BUT readiness
        // says HoldForReinforcements → stay in MainEffort, don't escalate
        // to Exploit until relief lands
        var input = new TacticalPhaseProgressionDoctrine.Input(
            BattlePhase.MainEffort,
            planAgeSeconds: 30f,
            globalOddsCurrent: 1.4f,
            globalOddsHistory: 1.2f,
            mainEffortOddsCurrent: 1.8f,
            mainEffortOddsHistory: 1.0f,
            armyMoraleCurrent: 1.0f,
            armyMoraleFloor: 0.2f,
            reservesCommittedFraction: 0.6f,
            commanderAggression01: 0.5f,
            mainEffortReadiness: TacticalSectorReadinessDoctrine.Result.HoldForReinforcements);
        var d = TacticalPhaseProgressionDoctrine.Decide(input);
        AssertEqual(BattlePhase.MainEffort, d.NextPhase,
            "Exploit blocked by HoldForReinforcements; got reason=" + d.Reason);
    }

    // ---- Full wiring: ArmyTickCycle readiness path ----

    private static void ArmyTickCycleFreshAdvancesOnFavorableOdds()
    {
        // Fresh army (fatigue 0.1, ammo 0.95) AND favorable LOCAL main-effort
        // sector odds (1.5x) → readiness PushReady AND mainEffortOdds clears
        // ProbeCommitOddsFloor → phase advances Probe → MainEffort. The
        // EnemyVisibleState carries sector 0 with own>enemy because the
        // doctrine now reads per-sector odds — if you have 1.3x globally
        // but stalemate in the main effort sector, doctrine correctly keeps
        // probing.
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var evidence = new ArmyEvidence(1.3f, TerrainKind.Open, 0);
        orch.PickInitialPlan(evidence);
        AssertEqual(BattlePhase.Probe, orch.CurrentPlan.Phase, "initial Probe");

        // anyContactSpotted=false keeps ArmyIntentInference.Confidence below
        // EnemyShiftConfidenceFloor (0.5) so no EnemyIntentShift replan fires
        // and we observe pure phase progression.
        var visible = new EnemyVisibleState(
            new[] { new EnemyVisibleSector(0, 6000f, 4000f, false) },  // 1.5x local, no recent fire
            0.5f, anyContactSpotted: false, anyContactBroken: false);
        ArmyTickCycle.MaybeReplan(
            orch, deltaSeconds: 10f, evidence, visible,
            ownMainEffortStrength: 8000f, ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.2f, ownReinforcementsArriving: 0f,
            minReplanSeconds: 60,
            ownAvgFatigue01: 0.1f, ownAvgAmmo01: 0.95f,
            nearestReinforcementHours: 0f, nearestReinforcementStrength: 0f);

        AssertEqual(BattlePhase.MainEffort, orch.CurrentPlan.Phase,
            "fresh army + favorable local sector odds → MainEffort");
    }

    /// <summary>
    /// Helper: EnemyVisibleState that does NOT trigger ArmyIntentInference's
    /// confidence-weighted EnemyIntentShift replan. Used by phase-progression
    /// tests so we observe the phase transition without it being immediately
    /// reset by a replan-to-fresh-Probe-plan side effect.
    /// </summary>
    private static EnemyVisibleState QuietEnemyVisibleState(int sectorId, float ownStrength, float enemyStrength)
    {
        return new EnemyVisibleState(
            new[] { new EnemyVisibleSector(sectorId, ownStrength, enemyStrength, false) },
            0.5f, anyContactSpotted: false, anyContactBroken: false);
    }

    private static void ArmyTickCycleExhaustedWithReliefHoldsInProbe()
    {
        // Tired army (own 20000, fatigue 0.7, ammo 0.5, morale 0.7 → effective
        // ~2100) facing enemy ~15400 at odds 1.3 — current ratio 0.14, way
        // below mid-aggression requiredRatio ~1.10. BUT 15000-strong relief
        // arrives in 2h (within 4h mid window) → future effective 17100 vs
        // enemy 15400 = ratio 1.11 > 1.10 → readiness HoldForReinforcements
        // → phase stays in Probe even though global odds favor commit.
        ArmyTickCycle.Reset();
        var midCommander = new PersonalityVector(0.0f, 0.0f, 0.0f, 0.5f, 0.4f);  // mid
        var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), midCommander);
        var evidence = new ArmyEvidence(1.3f, TerrainKind.Open, 0);
        orch.PickInitialPlan(evidence);

        ArmyTickCycle.MaybeReplan(
            orch, deltaSeconds: 10f, evidence,
            QuietEnemyVisibleState(0, 8000f, 5000f),
            ownMainEffortStrength: 20000f, ownArmyMorale: 0.7f,
            ownReservesCommittedFraction: 0.2f, ownReinforcementsArriving: 0f,
            minReplanSeconds: 60,
            ownAvgFatigue01: 0.7f, ownAvgAmmo01: 0.5f,
            nearestReinforcementHours: 2f, nearestReinforcementStrength: 15000f);

        AssertEqual(BattlePhase.Probe, orch.CurrentPlan.Phase,
            "tired army with imminent decisive relief stays in Probe");
    }

    private static void ArmyTickCycleExhaustedAggressiveStillCommits()
    {
        // Moderately degraded army (fatigue 0.4, ammo 0.7, morale 0.8 →
        // forceHealth ~0.34 — above the 0.25 min-viable fraction so the
        // aggression-based press/hold branch is reached) AND aggressive
        // commander (Hood-tier, aggression 0.95) AND NO relief → readiness
        // PushDegraded → commits to MainEffort.
        ArmyTickCycle.Reset();
        var hood = new PersonalityVector(0.9f, -0.8f, 0.7f, 0.5f, 0.0f);  // aggressive
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), hood);
        var evidence = new ArmyEvidence(1.3f, TerrainKind.Open, 0);
        orch.PickInitialPlan(evidence);

        ArmyTickCycle.MaybeReplan(
            orch, deltaSeconds: 10f, evidence,
            QuietEnemyVisibleState(0, 6000f, 4000f),  // 1.5x local
            ownMainEffortStrength: 18000f, ownArmyMorale: 0.8f,
            ownReservesCommittedFraction: 0.2f, ownReinforcementsArriving: 0f,
            minReplanSeconds: 60,
            ownAvgFatigue01: 0.4f, ownAvgAmmo01: 0.7f,
            nearestReinforcementHours: 0f, nearestReinforcementStrength: 0f);

        AssertEqual(BattlePhase.MainEffort, orch.CurrentPlan.Phase,
            "aggressive commander commits with moderately-degraded force (no relief)");
    }

    private static void ArmyTickCycleExhaustedCautiousHoldsFatigued()
    {
        // Moderately degraded army (same shape as aggressive test — moderate
        // fatigue/ammo/morale that lands above the 0.25 force-health floor)
        // but cautious commander (McClellan-tier, aggression ~0.2) AND NO
        // relief → readiness HoldFatigued (cautious below
        // PressVsHoldAggressionThreshold 0.55) → stays in Probe.
        ArmyTickCycle.Reset();
        var mcc = new PersonalityVector(-0.6f, 0.8f, -0.7f, 0.7f, 0.4f);  // cautious
        var orch = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), mcc);
        var evidence = new ArmyEvidence(1.3f, TerrainKind.Open, 0);
        orch.PickInitialPlan(evidence);

        ArmyTickCycle.MaybeReplan(
            orch, deltaSeconds: 10f, evidence,
            QuietEnemyVisibleState(0, 6000f, 4000f),  // 1.5x local
            ownMainEffortStrength: 18000f, ownArmyMorale: 0.8f,
            ownReservesCommittedFraction: 0.2f, ownReinforcementsArriving: 0f,
            minReplanSeconds: 60,
            ownAvgFatigue01: 0.4f, ownAvgAmmo01: 0.7f,
            nearestReinforcementHours: 0f, nearestReinforcementStrength: 0f);

        AssertEqual(BattlePhase.Probe, orch.CurrentPlan.Phase,
            "cautious commander holds moderately-tired force (preserve)");
    }

    private static void ArmyTickCycleLegacyOverloadPreservesPriorBehavior()
    {
        // Legacy overload (no readiness fields passed) defaults to neutral
        // values → PushReady → fresh-army-favorable-odds behavior. Confirms
        // existing call sites that haven't been updated still get the
        // pre-readiness commit behavior.
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var evidence = new ArmyEvidence(1.3f, TerrainKind.Open, 0);
        orch.PickInitialPlan(evidence);

        // 9-arg overload (no readiness params)
        ArmyTickCycle.MaybeReplan(
            orch, deltaSeconds: 10f, evidence,
            QuietEnemyVisibleState(0, 6000f, 4000f),  // 1.5x local, no intent shift
            ownMainEffortStrength: 8000f, ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.2f, ownReinforcementsArriving: 0f,
            minReplanSeconds: 60);

        AssertEqual(BattlePhase.MainEffort, orch.CurrentPlan.Phase,
            "legacy overload defaults to PushReady; commits as before");
    }

    private static void ArmyTickCycleWiresConsiderMainEffortShift()
    {
        // EnemyVisibleState with sector 2 having clear best odds → MaybeReplan
        // must call ConsiderMainEffortShift, swapping plan.MainEffortSector
        // from 0 to 2. Confirms the tick driver is actually wiring the
        // sector-shift slice end-to-end.
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var evidence = new ArmyEvidence(1.3f, TerrainKind.Open, defaultMainEffortSector: 0);
        orch.PickInitialPlan(evidence);
        AssertEqual(0, orch.CurrentPlan.MainEffortSector, "starts on sector 0");

        // anyContactSpotted=false keeps EnemyIntentShift quiet so we observe
        // the sector shift without it being undone by a replan-to-default-
        // sector side effect.
        var sectors = new EnemyVisibleSector[]
        {
            new EnemyVisibleSector(sectorId: 0, ownStrength: 4000f, enemyStrength: 4000f, recentFire: false),
            new EnemyVisibleSector(sectorId: 1, ownStrength: 3000f, enemyStrength: 5000f, recentFire: false),
            new EnemyVisibleSector(sectorId: 2, ownStrength: 5000f, enemyStrength: 2000f, recentFire: false), // decisive
        };
        var visible = new EnemyVisibleState(sectors, 0.4f, anyContactSpotted: false, anyContactBroken: false);

        ArmyTickCycle.MaybeReplan(
            orch, deltaSeconds: 5f, evidence, visible,
            ownMainEffortStrength: 12000f, ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.2f, ownReinforcementsArriving: 0f,
            minReplanSeconds: 60,
            ownAvgFatigue01: 0.2f, ownAvgAmmo01: 0.9f,
            nearestReinforcementHours: 0f, nearestReinforcementStrength: 0f);

        AssertEqual(2, orch.CurrentPlan.MainEffortSector,
            "main effort shifted to decisive sector 2");
    }

    private static void ConsiderMainEffortShiftRespectsHysteresisMargin()
    {
        // Hysteresis prevents thrash at 20x compression. Candidate sector
        // must beat current by 25% in odds to actually shift. At ratio
        // odds 1.0 → 1.20, candidate is only 20% better → no shift.
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(1.0f, TerrainKind.Open, defaultMainEffortSector: 0));

        // Marginal candidate (20% better) — must NOT shift.
        bool shiftedMarginal = orch.ConsiderMainEffortShift(
            decisiveSectorId: 2, decisiveSectorOdds: 1.20f, currentMainEffortOdds: 1.0f);
        AssertTrue(!shiftedMarginal, "20% margin below 25% hysteresis → no shift");
        AssertEqual(0, orch.CurrentPlan.MainEffortSector, "sector unchanged at marginal candidate");

        // Material candidate (50% better) — must shift.
        bool shiftedMaterial = orch.ConsiderMainEffortShift(
            decisiveSectorId: 2, decisiveSectorOdds: 1.50f, currentMainEffortOdds: 1.0f);
        AssertTrue(shiftedMaterial, "50% margin clears hysteresis → shift");
        AssertEqual(2, orch.CurrentPlan.MainEffortSector, "sector shifted to material candidate");
    }

    private static void ConsiderMainEffortShiftLegacyOverloadAlwaysShifts()
    {
        // Legacy single-arg overload defaults currentMainEffortOdds=0 (no
        // contact in current sector) → any positive candidate odds shifts.
        // Preserves pre-hysteresis behavior for call sites not yet updated.
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        orch.PickInitialPlan(new ArmyEvidence(1.0f, TerrainKind.Open, defaultMainEffortSector: 0));

        bool shifted = orch.ConsiderMainEffortShift(3, float.MaxValue, 0f);
        AssertTrue(shifted, "legacy overload shifts on any positive decisive sector");
        AssertEqual(3, orch.CurrentPlan.MainEffortSector, "shifted to sector 3");
    }

    private static void ArmyTickCycle20xCompressionBurnsProbeInBattleTime()
    {
        // 20x compression: 1 wallclock second = 20 battle seconds. With a
        // 30-second probe budget (aggressive commander), 1.5 wallclock
        // seconds (= 30 battle seconds) should elapse the budget. Before
        // the battle-time fix, 30 wallclock seconds would be needed — at
        // 20x that's 600 battle seconds (10 battle minutes) of probing
        // before commit. Now budget tracks battle pace correctly.
        //
        // Test passes a small deltaSeconds (1.5s real-time, 30s battle-time)
        // with cautious-odds + push-degraded to NOT trigger the favorable-
        // odds commit path. Then verify phase age has advanced 30s and
        // matches the battle delta, not the real delta.
        ArmyTickCycle.Reset();
        var hood = new PersonalityVector(0.9f, -0.8f, 0.7f, 0.5f, 0.0f);  // aggressive
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), hood);
        orch.PickInitialPlan(new ArmyEvidence(1.0f, TerrainKind.Open, 0));

        var visible = QuietEnemyVisibleState(0, 4000f, 4000f);  // parity, no commit

        ArmyTickCycle.MaybeReplan(
            orch, deltaSeconds: 1.5f, new ArmyEvidence(1.0f, TerrainKind.Open, 0), visible,
            ownMainEffortStrength: 10000f, ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.2f, ownReinforcementsArriving: 0f,
            minReplanSeconds: 600,
            ownAvgFatigue01: 0.2f, ownAvgAmmo01: 0.9f,
            nearestReinforcementHours: 0f, nearestReinforcementStrength: 0f,
            battleDeltaSeconds: 30f);  // 20x compression: 1.5 real = 30 battle

        // T8 (2026-10-14): plan age (the plan deadline) is battle time too.
        AssertNear(30f, orch.PlanAgeSeconds, 1e-5f, "plan age accrues battle-time (30s)");
        AssertNear(30f, orch.PhaseAgeSeconds, 1e-5f, "phase age accrues battle-time (30s)");
    }

    private static void ArmyTickCycleWiresOffensiveTriggerInputs()
    {
        // Local breakthrough scenario: history was 1.1 odds (initial replan
        // baseline), current main effort sector now shows 2.5x — ratio
        // 2.5/1.1 = 2.27 well above LocalBreakthroughOddsRatio (1.35). With
        // reserves committed >= 0.3 in MainEffort phase, must fire
        // MainEffortLocalBreakthrough.
        //
        // Only one sector in EnemyVisibleState, so the post-shift main-effort
        // sector matches the high-odds sector — no off-axis competitor to
        // override. anyContactSpotted=false suppresses EnemyIntentShift
        // replan so the offensive trigger is the one that fires.
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.7f, -0.3f, 0.6f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var evidence = new ArmyEvidence(1.1f, TerrainKind.Open, defaultMainEffortSector: 0);
        orch.PickInitialPlan(evidence);
        orch.AdvancePhase(BattlePhase.MainEffort);  // skip Probe so offensive triggers eligible

        var visible = QuietEnemyVisibleState(0, 5000f, 2000f);  // 2.5x local at main effort

        ArmyTickCycle.MaybeReplan(
            orch, deltaSeconds: 5f, evidence, visible,
            ownMainEffortStrength: 12000f, ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.5f,  // above exploit-gate min 0.4
            ownReinforcementsArriving: 0f,
            minReplanSeconds: 0,
            ownAvgFatigue01: 0.2f, ownAvgAmmo01: 0.9f,
            nearestReinforcementHours: 0f, nearestReinforcementStrength: 0f);

        // The wired offensive-evidence path now drives phase progression to
        // Exploit directly (more elegant than firing a replan trigger). Both
        // outcomes consume the same per-sector odds from EnemyVisibleState —
        // the assertion verifies the wiring works either way.
        AssertEqual(BattlePhase.Exploit, orch.CurrentPlan.Phase,
            "main effort sector odds spike drives phase advance to Exploit");
    }

    private static void ArmyOrchestratorLiveCommanderInitiativeDefaultsMidBand()
    {
        var orch = new ArmyOrchestrator(0,
            SeedCatalog.AllHistoricalAndGeneric(),
            new PersonalityVector(0.7f, -0.5f, 0.6f, 0.4f, -0.2f));  // Lee
        AssertEqual(0.5f, orch.LiveCommanderInitiative01,
            "live initiative defaults mid-band 0.5 until refresh");
    }

    private static void ArmyOrchestratorLiveCommanderInitiativeClampsToRange()
    {
        var orch = new ArmyOrchestrator(0,
            SeedCatalog.AllHistoricalAndGeneric(),
            new PersonalityVector(0.7f, -0.5f, 0.6f, 0.4f, -0.2f));
        orch.UpdateLiveCommanderInitiative(0.85f);
        AssertEqual(0.85f, orch.LiveCommanderInitiative01, "in-range value passes through");
        orch.UpdateLiveCommanderInitiative(-0.3f);
        AssertEqual(0f, orch.LiveCommanderInitiative01, "negative clamps to 0");
        orch.UpdateLiveCommanderInitiative(1.5f);
        AssertEqual(1f, orch.LiveCommanderInitiative01, "above 1 clamps to 1");
    }

    private static void ArmyOrchestratorLiveCommanderInitiativeRejectsNaNAndInfinity()
    {
        var orch = new ArmyOrchestrator(0,
            SeedCatalog.AllHistoricalAndGeneric(),
            new PersonalityVector(0.7f, -0.5f, 0.6f, 0.4f, -0.2f));
        orch.UpdateLiveCommanderInitiative(0.6f);  // set baseline
        orch.UpdateLiveCommanderInitiative(float.NaN);
        AssertEqual(0.6f, orch.LiveCommanderInitiative01, "NaN does not overwrite valid value");
        orch.UpdateLiveCommanderInitiative(float.PositiveInfinity);
        AssertEqual(0.6f, orch.LiveCommanderInitiative01, "Inf does not overwrite valid value");
        orch.UpdateLiveCommanderInitiative(float.NegativeInfinity);
        AssertEqual(0.6f, orch.LiveCommanderInitiative01, "-Inf does not overwrite valid value");
    }

    private static void ArmyReplanTriggersAtSentinelBoundaryDoNotTripReserveExhaustion()
    {
        // Boundary: exactly at the sentinel value still treated as unknown.
        // Values close-but-not-equal (e.g., 0.34 or 0.36) are treated as known
        // and won't trip because they're below 0.85 anyway.
        var atSentinel = new ReplanTriggerInput(
            planAgeSeconds: 10f, currentPhase: BattlePhase.Probe,
            mainEffortOwnStrength: 1000f, mainEffortHistoryOwnStrength: 1000f,
            globalOddsCurrent: 1.0f, globalOddsHistory: 1.0f,
            armyMoraleCurrent: 0.9f, armyMoraleFloor: 0.4f,
            reservesCommittedFraction: 0.35f,
            ownReinforcementsArriving: 0f,
            enemyMainEffortShiftConfidenceWeighted: 0.0f);
        AssertEqual(ReplanTrigger.None, ArmyReplanTriggers.Evaluate(atSentinel),
            "at sentinel boundary: no trigger");
    }

    private static void ArmyTickCycleNoTriggerWhenAllConditionsNormal()
    {
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        orch.PickInitialPlan(ownEvidence);

        var trigger = ArmyTickCycle.MaybeReplan(
            orch,
            deltaSeconds: 5f,
            ownEvidence,
            NormalEnemyVisibleState(),
            ownMainEffortStrength: 5000f,
            ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.5f,
            ownReinforcementsArriving: 0f,
            minReplanSeconds: 60);

        AssertEqual(ReplanTrigger.None, trigger, "normal conditions do not replan");
        AssertNear(5f, orch.PlanAgeSeconds, 1e-5f, "tick advances plan age");
    }

    private static void ArmyTickCyclePhaseDeadlineFires()
    {
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        orch.PickInitialPlan(ownEvidence);
        orch.AdvancePlanAge(ArmyReplanTriggers.PlanDeadlineBattleSeconds + 10f);

        var trigger = ArmyTickCycle.MaybeReplan(
            orch,
            deltaSeconds: 5f,
            ownEvidence,
            NormalEnemyVisibleState(),
            ownMainEffortStrength: 5000f,
            ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.5f,
            ownReinforcementsArriving: 0f,
            minReplanSeconds: 60);

        AssertEqual(ReplanTrigger.PhaseDeadline, trigger, "age beyond budget replans on phase deadline");
        AssertNear(0f, orch.PlanAgeSeconds, 1e-5f, "replan resets plan age");
    }

    private static void ArmyTickCycleRateLimitsReplanWithinMinReplanSeconds()
    {
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        orch.PickInitialPlan(ownEvidence);
        orch.AdvancePlanAge(ArmyReplanTriggers.PlanDeadlineBattleSeconds + 10f);

        var first = ArmyTickCycle.MaybeReplan(
            orch,
            deltaSeconds: 5f,
            ownEvidence,
            NormalEnemyVisibleState(),
            ownMainEffortStrength: 5000f,
            ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.5f,
            ownReinforcementsArriving: 0f,
            minReplanSeconds: 60);
        orch.AdvancePlanAge(ArmyReplanTriggers.PlanDeadlineBattleSeconds + 10f);
        var second = ArmyTickCycle.MaybeReplan(
            orch,
            deltaSeconds: 5f,
            ownEvidence,
            NormalEnemyVisibleState(),
            ownMainEffortStrength: 5000f,
            ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.5f,
            ownReinforcementsArriving: 0f,
            minReplanSeconds: 60);

        AssertEqual(ReplanTrigger.PhaseDeadline, first, "first over-budget tick replans");
        AssertEqual(ReplanTrigger.None, second, "second over-budget tick is rate-limited");
    }

    private static void ArmyTickCycleRateLimitIsPerAllianceClock()
    {
        ArmyTickCycle.Reset();
        // Test verifies the rate-limit CLOCK is per-alliance — needs TWO
        // distinct allianceIds. The "lee" personality vector is just a
        // fixture; with faction-affinity bias, Union side (alliance 0) gets
        // a penalty selecting CSA-historical Lee playbook, but that doesn't
        // affect the rate-limit behavior under test.
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var union = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var csa = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        union.PickInitialPlan(ownEvidence);
        csa.PickInitialPlan(ownEvidence);
        union.AdvancePlanAge(ArmyReplanTriggers.PlanDeadlineBattleSeconds + 10f);
        csa.AdvancePlanAge(ArmyReplanTriggers.PlanDeadlineBattleSeconds + 10f);

        var unionFirst = ArmyTickCycle.MaybeReplan(union, 5f, ownEvidence, NormalEnemyVisibleState(), 5000f, 1.0f, 0.5f, 0f, 60);
        var csaFirst = ArmyTickCycle.MaybeReplan(csa, 5f, ownEvidence, NormalEnemyVisibleState(), 5000f, 1.0f, 0.5f, 0f, 60);
        union.AdvancePlanAge(ArmyReplanTriggers.PlanDeadlineBattleSeconds + 10f);
        var unionSecond = ArmyTickCycle.MaybeReplan(union, 5f, ownEvidence, NormalEnemyVisibleState(), 5000f, 1.0f, 0.5f, 0f, 60);

        AssertEqual(ReplanTrigger.PhaseDeadline, unionFirst, "union first over-budget tick replans");
        AssertEqual(ReplanTrigger.PhaseDeadline, csaFirst, "csa first over-budget tick replans");
        AssertEqual(ReplanTrigger.None, unionSecond, "csa tick does not advance union rate-limit clock");
    }

    private static void ArmyTickCycleResetClearsBattleLifetimeRateLimit()
    {
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        var firstBattle = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        firstBattle.PickInitialPlan(ownEvidence);
        firstBattle.AdvancePlanAge(ArmyReplanTriggers.PlanDeadlineBattleSeconds + 10f);
        var first = ArmyTickCycle.MaybeReplan(firstBattle, 5f, ownEvidence, NormalEnemyVisibleState(), 5000f, 1.0f, 0.5f, 0f, 60);

        ArmyTickCycle.Reset();

        var secondBattle = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        secondBattle.PickInitialPlan(ownEvidence);
        secondBattle.AdvancePlanAge(ArmyReplanTriggers.PlanDeadlineBattleSeconds + 10f);
        var second = ArmyTickCycle.MaybeReplan(secondBattle, 5f, ownEvidence, NormalEnemyVisibleState(), 5000f, 1.0f, 0.5f, 0f, 60);

        AssertEqual(ReplanTrigger.PhaseDeadline, first, "first battle over-budget tick replans");
        AssertEqual(ReplanTrigger.PhaseDeadline, second, "reset clears rate-limit state for next battle");
    }

    private static void ArmyTickCycleUpdatesObservedIntentWithoutReplan()
    {
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        orch.PickInitialPlan(ownEvidence);

        var trigger = ArmyTickCycle.MaybeReplan(
            orch,
            deltaSeconds: 5f,
            ownEvidence,
            NormalEnemyVisibleState(),
            ownMainEffortStrength: 5000f,
            ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.5f,
            ownReinforcementsArriving: 0f,
            minReplanSeconds: 60);

        AssertEqual(ReplanTrigger.None, trigger, "normal tick does not replan");
        AssertEqual(InferredIntent.Probe, orch.CurrentIntentModel.PrimaryIntent, "normal tick still stores observed intent");
    }

    private static void ArmyTickCycleEnemyIntentShiftFiresWhenConfidentEnemyAttacks()
    {
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);
        var ownEvidence = new ArmyEvidence(1.0f, TerrainKind.Open, 0);
        orch.PickInitialPlan(ownEvidence);
        orch.AdvancePlanAge(70f);
        var enemy = new EnemyVisibleState(
            new[]
            {
                new EnemyVisibleSector(0, 5000f, 9000f, true),
                new EnemyVisibleSector(1, 5000f, 1500f, false),
                new EnemyVisibleSector(2, 5000f, 1500f, false)
            },
            enemyReserveCommitFraction: 0.8f,
            anyContactSpotted: true,
            anyContactBroken: false);

        var trigger = ArmyTickCycle.MaybeReplan(
            orch,
            deltaSeconds: 5f,
            ownEvidence,
            enemy,
            ownMainEffortStrength: 5000f,
            ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.5f,
            ownReinforcementsArriving: 0f,
            minReplanSeconds: 60);

        AssertEqual(ReplanTrigger.EnemyIntentShift, trigger, "confident enemy attack signal replans");
    }

    private static void ArmyTickCycleNoReplanIfOrchestratorHasNoPlan()
    {
        ArmyTickCycle.Reset();
        var lee = new PersonalityVector(0.8f, -0.4f, 0.7f, 0.5f, 0.4f);
        var orch = new ArmyOrchestrator(1, SeedCatalog.AllHistoricalAndGeneric(), lee);

        var trigger = ArmyTickCycle.MaybeReplan(
            orch,
            deltaSeconds: 5f,
            new ArmyEvidence(1.0f, TerrainKind.Open, 0),
            NormalEnemyVisibleState(),
            ownMainEffortStrength: 5000f,
            ownArmyMorale: 1.0f,
            ownReservesCommittedFraction: 0.5f,
            ownReinforcementsArriving: 0f,
            minReplanSeconds: 60);

        AssertEqual(ReplanTrigger.None, trigger, "no active plan cannot replan");
    }

    private static EnemyVisibleState NormalEnemyVisibleState()
    {
        return new EnemyVisibleState(
            new[] { new EnemyVisibleSector(0, 5000f, 5000f, false) },
            enemyReserveCommitFraction: 0.5f,
            anyContactSpotted: true,
            anyContactBroken: false);
    }

    // ============================================================================
    // Regimental intelligence tests (2026-05-21). Cover the doctrine's 6 priority
    // branches + threat computer's quadrant assignment + isolation detection.
    // ============================================================================

    // ============================================================================
    // Friendly-path blocking gate tests (2026-05-21). Verify Scourge's 3-layer
    // gate logic produces the right block/clear results for common scenarios.
    // ============================================================================

    // ============================================================================
    // Opportunity hysteresis tests (2026-05-21). Verify the smoother holds
    // decisions for the configured min-hold window and accepts escalations
    // to Withdrawal immediately.
    // ============================================================================

    private static ReinforcementOpportunityDecision MakeOpportunityDecision(ReinforcementOpportunity opp, string reason)
    {
        return new ReinforcementOpportunityDecision(opp, reason, 1f, 999f, 999f, 1.2f, 1.0f);
    }

    private static void OpportunityHysteresisHoldsSameCategory()
    {
        var prev = MakeOpportunityDecision(ReinforcementOpportunity.DefensiveHold, "hold");
        var raw = MakeOpportunityDecision(ReinforcementOpportunity.DefensiveHold, "hold");
        var result = TacticalOpportunityHysteresis.Smooth(raw, prev, 100f, 6f);
        AssertFalse(result.Changed, "same category never changes");
        AssertEqual("same-category", result.SmoothingReason, "smoothingReason matches");
    }

    private static void OpportunityHysteresisBlocksWithinHoldWindow()
    {
        var prev = MakeOpportunityDecision(ReinforcementOpportunity.DefensiveHold, "hold");
        var raw = MakeOpportunityDecision(ReinforcementOpportunity.AttackNow, "attack");
        var result = TacticalOpportunityHysteresis.Smooth(raw, prev, 3f, 6f);
        AssertFalse(result.Changed, "within hold window = no transition");
        AssertEqual(ReinforcementOpportunity.DefensiveHold, result.Decision.Opportunity, "holds prior decision");
        AssertEqual("hold-time-active", result.SmoothingReason, "reason matches");
    }

    private static void OpportunityHysteresisAllowsAfterHold()
    {
        var prev = MakeOpportunityDecision(ReinforcementOpportunity.DefensiveHold, "hold");
        var raw = MakeOpportunityDecision(ReinforcementOpportunity.AttackNow, "attack");
        var result = TacticalOpportunityHysteresis.Smooth(raw, prev, 10f, 6f);
        AssertTrue(result.Changed, "after hold expires, transition is allowed");
        AssertEqual(ReinforcementOpportunity.AttackNow, result.Decision.Opportunity, "accepts new decision");
    }

    private static void OpportunityHysteresisEscalatesToWithdrawal()
    {
        var prev = MakeOpportunityDecision(ReinforcementOpportunity.DefensiveHold, "hold");
        var raw = MakeOpportunityDecision(ReinforcementOpportunity.WithdrawalToFightLater, "outnumbered");
        var result = TacticalOpportunityHysteresis.Smooth(raw, prev, 1f, 6f);
        AssertTrue(result.Changed, "escalation to withdrawal bypasses hold");
        AssertEqual(ReinforcementOpportunity.WithdrawalToFightLater, result.Decision.Opportunity, "accepts withdrawal");
        AssertEqual("escalation-to-withdrawal", result.SmoothingReason, "reason matches");
    }

    // ============================================================================
    // Regimental positioner tests (2026-05-22, Scope C). Verifies the
    // per-regiment slot computation produces sensible left/right symmetric
    // positions perpendicular to the brigade-→-target axis.
    // ============================================================================

    private static void OpportunityHysteresisAcceptsFirstDecision()
    {
        var prev = MakeOpportunityDecision(ReinforcementOpportunity.NoOpportunity, "init");
        var raw = MakeOpportunityDecision(ReinforcementOpportunity.AttackNow, "attack");
        var result = TacticalOpportunityHysteresis.Smooth(raw, prev, float.MaxValue, 6f);
        AssertTrue(result.Changed, "first decision accepted");
        AssertEqual(ReinforcementOpportunity.AttackNow, result.Decision.Opportunity, "accepts AttackNow");
    }
}
