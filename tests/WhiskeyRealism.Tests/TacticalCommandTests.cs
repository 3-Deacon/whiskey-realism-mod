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
using CommandWaypointWritePolicy = WhiskeyRealism.Tactical.Battle.CommandWaypointWritePolicy;

internal static class TacticalCommandTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("tactical commander mode active allows writes", TacticalCommanderModeActiveAllowsWrites),
            ("tactical commander mode monitor runs ledger without writes", TacticalCommanderModeMonitorRunsNoWrites),
            ("tactical commander mode parses spacing and fallback", TacticalCommanderModeParsesSpacingAndFallback),
            ("tactical commander mode active emits ledger telemetry", TacticalCommanderModeActiveEmitsLedgerTelemetry),
            ("tactical commander active remains configured default", TacticalCommanderActiveRemainsConfiguredDefault),
            ("tactical operations parallel requires per objective advantage", TacticalOperationsParallelRequiresPerObjectiveAdvantage),
            ("tactical operations strong and weak selects fix and flank", TacticalOperationsStrongWeakSelectsFixAndFlank),
            ("tactical operations unknown strength does not look weak", TacticalOperationsUnknownStrengthDoesNotLookWeak),
            ("tactical operations ledger runtime active selects operation", TacticalOperationsLedgerRuntimeActiveSelectsOperation),
            ("tactical operations ledger runtime off does not run ledger", TacticalOperationsLedgerRuntimeOffDoesNotRunLedger),
            ("operations ledger stores picture and doctrine orders", OperationsLedgerStoresPictureAndDoctrineOrders),
            ("operations ledger preserves committed operation across ticks", OperationsLedgerPreservesCommittedOperationAcrossTicks),
            ("operations ledger retargets stale forming operation", OperationsLedgerRetargetsStaleFormingOperation),
            ("tactical operations telemetry formats bounded monitor rows", TacticalOperationsTelemetryFormatsBoundedMonitorRows),
            ("tactical operations telemetry throttle helpers bound monitor loop", TacticalOperationsTelemetryThrottleHelpersBoundMonitorLoop),
            ("tactical command posture monitor-only suppresses active task writes", TacticalCommandPostureMonitorOnlySuppressesActiveTaskWrites),
            ("tactical command posture active mode does not report monitor-only", TacticalCommandPostureActiveModeDoesNotReportMonitorOnly),
            ("tactical command posture eligibility precedence", TacticalCommandPostureEligibilityPrecedence),
            ("tactical command posture physical protection fails closed", TacticalCommandPosturePhysicalProtectionFailsClosed),
            ("tactical command posture interrupted illegal idle recovery", TacticalCommandPostureInterruptedIllegalIdleRecovery),
            ("tactical command posture no-write gates after eligibility", TacticalCommandPostureNoWriteGatesAfterEligibility),
            ("tactical command posture close engagement limits movement writes", TacticalCommandPostureCloseEngagementLimitsMovementWrites),
            ("tactical command posture reserve wait distinguishes reserve area", TacticalCommandPostureReserveWaitDistinguishesReserveArea),
            ("tactical command posture maps task families", TacticalCommandPostureMapsTaskFamilies),
            ("posture executor uses doctrine attack target", PostureExecutorUsesDoctrineAttackTarget),
            ("posture executor uses doctrine assembly target", PostureExecutorUsesDoctrineAssemblyTarget),
            ("posture executor moves scout probe and screen to doctrine target", PostureExecutorMovesScoutProbeAndScreenToDoctrineTarget),
            ("posture executor probe scout screen require objective progress", PostureExecutorProbeScoutScreenRequireObjectiveProgress),
            ("posture executor close screen breaks off to doctrine fallback", PostureExecutorCloseScreenBreaksOffToDoctrineFallback),
            ("posture executor moves reserve to doctrine rally target", PostureExecutorMovesReserveToDoctrineRallyTarget),
            ("posture executor clears interrupted inactive order", PostureExecutorClearsInterruptedInactiveOrder),
            ("posture executor fallback doctrine overrides interrupted stale path", PostureExecutorFallbackDoctrineOverridesInterruptedStalePath),
            ("posture executor preserves legal reserve idle", PostureExecutorPreservesLegalReserveIdle),
            ("posture executor retries unmaterialized recent attack order", PostureExecutorRetriesUnmaterializedRecentAttackOrder),
            ("posture executor battle waypoints bypass delayed order queue", PostureExecutorBattleWaypointsBypassDelayedOrderQueue),
            ("posture executor scout and probe are objective movement tasks", PostureExecutorScoutAndProbeAreObjectiveMovementTasks),
            ("posture executor retries stalled scout and probe despite recent order", PostureExecutorRetriesStalledScoutAndProbeDespiteRecentOrder),
            ("posture executor long attack uses vanilla blocked movement order", PostureExecutorLongAttackUsesVanillaBlockedMovementOrder),
            ("posture executor group movement flags count as active move", PostureExecutorGroupMovementFlagsCountAsActiveMove),
            ("posture executor duplicate waypoint policy suppresses same target", PostureExecutorDuplicateWaypointPolicySuppressesSameTarget),
            ("tactical command monitor reserve idle valid", TacticalCommandMonitorReserveIdleValid),
            ("tactical command monitor path interrupted idle illegal", TacticalCommandMonitorPathInterruptedIdleIllegal),
            ("tactical command monitor interrupted hold is illegal", TacticalCommandMonitorInterruptedHoldIsIllegal),
            ("tactical command monitor player protected no-write", TacticalCommandMonitorPlayerProtectedNoWrite),
            ("tactical command task planner main effort attack vs defensive hold", TacticalCommandTaskPlannerMainEffortAttackVsDefensiveHold),
            ("tactical command task planner maps role table", TacticalCommandTaskPlannerMapsRoleTable),
            ("tactical command node state sanitizes blank node id", TacticalCommandNodeStateSanitizesBlankNodeId),
            ("tactical order outside bugle range is delayed", TacticalOrderOutsideBugleRangeIsDelayed),
            ("tactical order short bugle process time is delivered", TacticalOrderShortBugleProcessTimeIsDelivered),
            ("tactical order delivered transmitted path differs while delayed", TacticalOrderDeliveredTransmittedPathDiffersWhileDelayed),
            ("tactical order stale delayed order downgrades on material contact change", TacticalOrderStaleDelayedOrderDowngradesOnContactChange),
            ("tactical order high initiative reduces delay pressure without instant delivery", TacticalOrderHighInitiativeReducesDelayPressureWithoutInstant),
            ("tactical order settlement allows idle stance retask", TacticalOrderSettlementAllowsIdleStanceRetask),
            ("tactical order settlement blocks queued stance retask", TacticalOrderSettlementBlocksQueuedStanceRetask),
            ("tactical order settlement allows stale queued posture bypass", TacticalOrderSettlementAllowsStaleQueuedPostureBypass),
            ("tactical order settlement blocks delivered pending stance retask", TacticalOrderSettlementBlocksDeliveredPendingStanceRetask),
            ("tactical order settlement allows stalled interrupted pending retask", TacticalOrderSettlementAllowsStalledInterruptedPendingRetask),
            ("tactical order settlement allows stalled interrupted pending retask with stale path", TacticalOrderSettlementAllowsStalledInterruptedPendingRetaskWithStalePath),
            ("tactical order settlement stalled interrupted does not block posture write", TacticalOrderSettlementStalledInterruptedDoesNotBlockPostureWrite),
            ("tactical order settlement stalled pending without path does not block posture write", TacticalOrderSettlementStalledPendingWithoutPathDoesNotBlockPostureWrite),
            ("tactical order settlement blocks unknown order state", TacticalOrderSettlementBlocksUnknownOrderState),
            ("tactical command army and corps intent does not retask regiments directly", TacticalCommandArmyCorpsDoesNotRetaskRegimentsDirectly),
            ("tactical command maps vanilla battle unit tiers", TacticalCommandMapsVanillaBattleUnitTiers),
            ("tactical command division mission maps to brigade actions", TacticalCommandDivisionMissionMapsToBrigadeActions),
            ("tactical commander roster falls back to faction defaults for unknown", TacticalCommanderRosterFallsBackToFactionDefaultsForUnknown),
            ("tactical commander roster partitions by side", TacticalCommanderRosterPartitionsBySide),
            ("tactical commander roster rank tier bias increases caution for corps", TacticalCommanderRosterRankTierBiasIncreasesCautionForCorps),
            ("reinforcement opportunity 15k own vs 13k enemy mid commander holds as defender", ReinforcementOpportunity15kVs13kMidHoldsAsDefender),
            ("reinforcement opportunity 15k own vs 13k enemy aggressive attacks now", ReinforcementOpportunity15kVs13kAggressiveAttacks),
            ("reinforcement opportunity 15k own vs 13k enemy cautious holds as defender", ReinforcementOpportunity15kVs13kCautiousHoldsAsDefender),
            ("reinforcement opportunity 10k vs 15k cautious delays defensively", ReinforcementOpportunity10kVs15kCautiousDelaysDefensively),
            ("reinforcement opportunity severely outnumbered withdraws", ReinforcementOpportunitySeverelyOutnumberedWithdraws),
            ("reinforcement opportunity aggressive commander refuses withdraw at moderate disadvantage", ReinforcementOpportunityAggressiveCommanderRefusesToWithdrawAtModerateDisadvantage),
            ("reinforcement opportunity smoke case routes to delaying defense", ReinforcementOpportunitySmokeCaseRoutesToDelayingDefense),
            ("reinforcement opportunity 15k own vs 10k enemy attacks on the advantage", ReinforcementOpportunity15kVs10kAttacksOnAdvantage),
            ("reinforcement opportunity 12k own +5k in 6h vs 13k enemy waits to consolidate", ReinforcementOpportunity12kPlus5kIn6hWaitsToConsolidate),
            ("reinforcement opportunity 13k vs 13k own relief too far defensive hold", ReinforcementOpportunity13kVs13kOwnReliefTooFarDefensiveHold),
            ("reinforcement opportunity intel confidence floor falls through to no opportunity", ReinforcementOpportunityIntelConfidenceFloorFallsThrough),
            ("reinforcement opportunity small cavalry brigade can attack at favorable ratio", ReinforcementOpportunitySmallCavalryCanAttack),
            ("reinforcement opportunity picket clash below 500 men does not attack", ReinforcementOpportunityPicketClashDoesNotAttack),
            ("reinforcement opportunity standing advantage + static enemy attacks now", ReinforcementOpportunityStandingAdvantageStaticEnemyAttacksNow),
            ("reinforcement opportunity standing advantage + static enemy below-min-force holds", ReinforcementOpportunityStandingAdvantageStaticEnemyBelowMinForceHolds),
            ("reinforcement opportunity cautious commander + standing advantage does not attack", ReinforcementOpportunityCautiousStandingAdvantageDoesNotAttack),
            ("reinforcement opportunity empty arrivals never reach parity", ReinforcementOpportunityEmptyArrivalsNeverReachParity),
            ("reinforcement opportunity zero enemy does not divide by zero", ReinforcementOpportunityZeroEnemyDoesNotDivideByZero),
            ("reinforcement opportunity attack threshold scales with aggression", ReinforcementOpportunityAttackThresholdScalesWithAggression),
            ("reinforcement opportunity attack window scales with aggression", ReinforcementOpportunityAttackWindowScalesWithAggression),
            ("reinforcement opportunity wait hours scales with aggression", ReinforcementOpportunityWaitHoursScalesWithAggression),
            ("reinforcement opportunity strength at hour accumulates arrivals", ReinforcementOpportunityStrengthAtHourAccumulatesArrivals),
            ("reinforcement opportunity parity hours for own short circuits when already ahead", ReinforcementOpportunityParityHoursForOwnShortCircuitsWhenAlreadyAhead),
        });
    }

    private static void TacticalOperationsParallelRequiresPerObjectiveAdvantage()
    {
        var first = ObjectiveRecordFor("ridge-a", enemyStrength: 70f, friendlyAssignedStrength: 100f);
        var second = ObjectiveRecordFor("ridge-b", enemyStrength: 60f, friendlyAssignedStrength: 100f);
        var personality = new PersonalityVector(0.4f, 0f, 0f, 0f, 0f);

        var selected = TacticalOperationSelectionModel.Select(
            first,
            second,
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            personality);

        AssertEqual(TacticalOperationShape.ParallelObjectives, selected, "parallel selection");

        var lowReserve = TacticalOperationSelectionModel.Select(
            first,
            second,
            new ForceAvailabilitySnapshot(8000f, 0.05f),
            personality);
        AssertTrue(lowReserve != TacticalOperationShape.ParallelObjectives, "low reserve blocks parallel");

        var secondStrong = TacticalOperationSelectionModel.Select(
            first,
            ObjectiveRecordFor("ridge-b", enemyStrength: 90f, friendlyAssignedStrength: 100f),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            personality);
        AssertTrue(secondStrong != TacticalOperationShape.ParallelObjectives, "per-objective disadvantage blocks parallel");
    }

    private static void TacticalOperationsStrongWeakSelectsFixAndFlank()
    {
        var first = ObjectiveRecordFor("enemy-line", enemyStrength: 140f, friendlyAssignedStrength: 100f);
        var second = ObjectiveRecordFor("bridge", enemyStrength: 50f, friendlyAssignedStrength: 100f);

        var selected = TacticalOperationSelectionModel.Select(
            first,
            second,
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));

        AssertEqual(TacticalOperationShape.FixAndFlank, selected, "strong weak selection");
    }

    private static void TacticalOperationsUnknownStrengthDoesNotLookWeak()
    {
        var personality = new PersonalityVector(0.4f, 0f, 0f, 0f, 0f);

        var defaultSelection = TacticalOperationSelectionModel.Select(
            default(ObjectiveRecord),
            default(ObjectiveRecord),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            personality);

        AssertTrue(defaultSelection != TacticalOperationShape.ParallelObjectives, "default objectives do not drive parallel");
        AssertTrue(defaultSelection != TacticalOperationShape.FixAndFlank, "default objectives do not drive fix and flank");

        var corruptFirstWeakSecond = TacticalOperationSelectionModel.Select(
            ObjectiveRecordFor("bad-a", enemyStrength: float.PositiveInfinity, friendlyAssignedStrength: 100f),
            ObjectiveRecordFor("ridge-b", enemyStrength: 50f, friendlyAssignedStrength: 100f),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            personality);

        AssertTrue(corruptFirstWeakSecond != TacticalOperationShape.FixAndFlank, "corrupt first objective does not drive fix and flank");

        var nonfiniteEnemy = TacticalOperationSelectionModel.Select(
            ObjectiveRecordFor("bad-a", enemyStrength: float.PositiveInfinity, friendlyAssignedStrength: 100f),
            ObjectiveRecordFor("bad-b", enemyStrength: float.NaN, friendlyAssignedStrength: 100f),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            personality);

        AssertTrue(nonfiniteEnemy != TacticalOperationShape.ParallelObjectives, "nonfinite enemy strength does not drive parallel");
    }

    private static void TacticalOperationsLedgerRuntimeActiveSelectsOperation()
    {
        var runtime = new TacticalOperationsLedgerRuntime();
        var snapshot = new StrategicBattleIntentSnapshot(0.4f, 0.6f, "hold-theater", "campaign-push");
        var objectives = new[]
        {
            ObjectiveRecordFor("enemy-line", enemyStrength: 140f, friendlyAssignedStrength: 100f),
            ObjectiveRecordFor("bridge", enemyStrength: 50f, friendlyAssignedStrength: 100f),
        };

        runtime.Replace(
            TacticalCommanderMode.Active,
            objectives,
            snapshot,
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));

        AssertTrue(runtime.RunsLedger, "active runs ledger");
        AssertEqual(TacticalCommanderMode.Active, runtime.CommanderMode, "mode");
        AssertEqual(TacticalOperationShape.FixAndFlank, runtime.CurrentOperation.Shape, "operation shape");
        AssertEqual("enemy-line", runtime.CurrentOperation.PrimaryObjectiveId, "primary objective");
        AssertEqual("campaign-push", runtime.CurrentStrategicBattleIntent.CampaignIntent, "strategic snapshot");
        AssertEqual(2, runtime.CurrentObjectives.Count, "objectives stored");
    }

    private static void TacticalOperationsLedgerRuntimeOffDoesNotRunLedger()
    {
        var runtime = new TacticalOperationsLedgerRuntime();

        runtime.Replace(
            TacticalCommanderMode.Off,
            new[] { ObjectiveRecordFor("ridge-a", enemyStrength: 70f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
            new StrategicBattleIntentSnapshot(0.4f, 0.6f, "hold", "push"),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));

        AssertFalse(runtime.RunsLedger, "off does not run ledger");
        AssertEqual(TacticalCommanderMode.Off, runtime.CommanderMode, "mode");
        AssertEqual(TacticalOperationShape.SingleMainEffort, runtime.CurrentOperation.Shape, "safe noop shape");
        AssertEqual("objective-unknown", runtime.CurrentOperation.PrimaryObjectiveId, "safe noop objective");
        AssertEqual(0, runtime.CurrentObjectives.Count, "objectives cleared");
        AssertEqual(string.Empty, runtime.CurrentStrategicBattleIntent.CampaignIntent, "strategic snapshot cleared");
    }

    private static void OperationsLedgerStoresPictureAndDoctrineOrders()
    {
        TacticalOperationsLedgerRuntime ledger = new TacticalOperationsLedgerRuntime();
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 300f, 0.9f, true, 0.8f, 100f, 200f, 0.2f, 0.2f)
        });
        CommandDoctrineOrder[] orders =
        {
            CommandDoctrineOrder.Create("node-1", CommandNodeRole.MainEffort, CommandTaskType.AttackObjective, "ridge-a", DoctrineTargetPoint.From(100f, 200f), DoctrineTargetPoint.None, DoctrineTargetPoint.None, DoctrineAllowedIdleReason.None, 900f, 100f, 0.9f, "test")
        };

        ledger.StoreDoctrine(picture, orders);

        AssertEqual(1, ledger.CurrentBattlefieldPicture.Objectives.Length, "picture objective count");
        AssertEqual(1, ledger.CurrentDoctrineOrders.Count, "doctrine order count");
        AssertEqual("node-1", ledger.CurrentDoctrineOrders[0].NodeId, "node id");
    }

    private static void OperationsLedgerPreservesCommittedOperationAcrossTicks()
    {
        var ledger = new TacticalOperationsLedgerRuntime();
        var commandStates = new[]
        {
            CommandNodeOperationalState.Create("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f)
        };

        ledger.SetRuntimeClock(0f);
        ledger.Replace(
            TacticalCommanderMode.Active,
            new[] { ObjectiveRecordFor("ridge-a", enemyStrength: 1000f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"),
            new ForceAvailabilitySnapshot(3000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        ledger.StoreDoctrineForCommandStates(commandStates);

        AssertEqual(TacticalOperationPhase.Committed, ledger.CurrentOperation.Phase, "initial committed phase");
        AssertEqual("ridge-a", ledger.CurrentOperation.PrimaryObjectiveId, "initial primary");

        ledger.SetRuntimeClock(100f);
        ledger.Replace(
            TacticalCommanderMode.Active,
            new[] { ObjectiveRecordFor("ridge-b", enemyStrength: 100f, friendlyAssignedStrength: 100f) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"),
            new ForceAvailabilitySnapshot(3000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        ledger.StoreDoctrineForCommandStates(commandStates);

        AssertEqual(TacticalOperationPhase.Committed, ledger.CurrentOperation.Phase, "committed phase preserved");
        AssertEqual("ridge-a", ledger.CurrentOperation.PrimaryObjectiveId, "committed primary preserved");
        AssertEqual(1, ledger.CurrentDoctrineOrders.Count, "doctrine regenerated from preserved operation");
    }

    private static void OperationsLedgerRetargetsStaleFormingOperation()
    {
        var ledger = new TacticalOperationsLedgerRuntime();
        var commandStates = new[]
        {
            CommandNodeOperationalState.Create("node-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f)
        };

        ledger.SetRuntimeClock(0f);
        ledger.Replace(
            TacticalCommanderMode.Active,
            new[] { ObjectiveRecordFor("ridge-a", enemyStrength: 5000f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.StronglyHeld) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"),
            new ForceAvailabilitySnapshot(3000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        ledger.StoreDoctrineForCommandStates(commandStates);

        AssertEqual(TacticalOperationPhase.Forming, ledger.CurrentOperation.Phase, "initial forming phase");
        AssertEqual("ridge-a", ledger.CurrentOperation.PrimaryObjectiveId, "initial primary");

        ledger.SetRuntimeClock(100f);
        ledger.Replace(
            TacticalCommanderMode.Active,
            new[] { ObjectiveRecordFor("ridge-b", enemyStrength: 1000f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"),
            new ForceAvailabilitySnapshot(3000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));
        ledger.StoreDoctrineForCommandStates(commandStates);

        AssertEqual(TacticalOperationPhase.Committed, ledger.CurrentOperation.Phase, "forming may retarget before commit");
        AssertEqual("ridge-b", ledger.CurrentOperation.PrimaryObjectiveId, "forming retargeted to live objective");
    }

    private static void TacticalOperationsTelemetryFormatsBoundedMonitorRows()
    {
        var operation = new OperationRecord(
            TacticalOperationShape.FixAndFlank,
            TacticalOperationPhase.Forming,
            "ridge A/left",
            12.25f);
        var strategic = new StrategicBattleIntentSnapshot(
            0.25f,
            0.5f,
            "Attack Left",
            "Plan Alpha",
            allianceId: 1,
            campaignObjectiveId: "Obj X",
            theaterPriority: 0.75f,
            casualtyTolerance: -0.25f,
            preserveForceBias: 0.40f);
        var state = new CommandNodeOperationalState(
            "node A/1",
            CommandEchelonKind.DivisionLike,
            CommandNodeRole.MainEffort,
            CommandTaskType.AttackObjective,
            CommandTaskState.Planning);

        string ledger = TacticalOperationsTelemetry.OpsLedger(
            1,
            TacticalCommanderMode.MonitorOnly,
            operation,
            strategic,
            commandCount: 3);
        string assignment = TacticalOperationsTelemetry.CommandAssignment(1, state, operation);
        string summary = TacticalOperationsTelemetry.PostureSummary(
            1,
            validIdle: 2,
            illegalIdle: 1,
            recoveringStuck: 1,
            activeAttacks: 2,
            reservesWaiting: 1);

        AssertContains(ledger, "[TacticalOpsLedger]", "ledger prefix");
        AssertContains(ledger, "primary=ridge_A/left", "ledger objective token");
        AssertContains(ledger, "campaign=Plan_Alpha", "ledger campaign token");
        AssertContains(assignment, "[TacticalCommandAssignment]", "assignment prefix");
        AssertContains(assignment, "node=node_A/1", "assignment node token");
        AssertContains(assignment, "objective=ridge_A/left", "assignment objective token");
        AssertContains(summary, "[TacticalPostureSummary]", "summary prefix");
        AssertContains(summary, "illegalIdle=1", "summary illegal count");
    }

    private static void TacticalOperationsTelemetryThrottleHelpersBoundMonitorLoop()
    {
        var emittedAt = new Dictionary<string, float>();
        AssertTrue(TacticalOperationsTelemetry.ShouldEmitInterval(emittedAt, "summary:1", 10f, 15f, verbose: false), "first interval emits");
        AssertFalse(TacticalOperationsTelemetry.ShouldEmitInterval(emittedAt, "summary:1", 20f, 15f, verbose: false), "interval suppresses early repeat");
        AssertTrue(TacticalOperationsTelemetry.ShouldEmitInterval(emittedAt, "summary:1", 25f, 15f, verbose: false), "interval emits after window");
        AssertTrue(TacticalOperationsTelemetry.ShouldEmitInterval(emittedAt, "summary:1", 26f, 15f, verbose: true), "verbose interval emits");

        var emittedSignatures = new Dictionary<string, string>();
        var pendingSignatures = new Dictionary<string, string>();
        var detailEmittedAt = new Dictionary<string, float>();
        AssertTrue(TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(
            emittedSignatures, pendingSignatures, detailEmittedAt, "node:1", "sig-a", 0f, 30f, verbose: false),
            "first detail emits");
        AssertFalse(TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(
            emittedSignatures, pendingSignatures, detailEmittedAt, "node:1", "sig-b", 10f, 30f, verbose: false),
            "changed detail suppressed before interval");
        AssertTrue(TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(
            emittedSignatures, pendingSignatures, detailEmittedAt, "node:1", "sig-b", 30f, 30f, verbose: false),
            "pending changed detail emits after interval");
        AssertFalse(TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(
            emittedSignatures, pendingSignatures, detailEmittedAt, "node:1", "sig-b", 60f, 30f, verbose: false),
            "unchanged detail stays suppressed");
    }

    private static void TacticalCommandPostureMonitorOnlySuppressesActiveTaskWrites()
    {
        var decision = CommandPostureExecutor.Decide(
            CommandState(CommandTaskType.AttackObjective),
            PhysicalState(),
            new WriteEligibilitySnapshot(
                modeAllowsWrites: false,
                playerProtected: false,
                routed: false,
                orderPending: false,
                recentOrder: false));

        AssertPostureDecision(PostureExecutionAction.NoWrite, "mode-monitor-only", decision);
    }

    private static void TacticalCommandPostureActiveModeDoesNotReportMonitorOnly()
    {
        var decision = CommandPostureExecutor.Decide(
            CommandState(CommandTaskType.AttackObjective),
            PhysicalState(),
            new WriteEligibilitySnapshot(
                modeAllowsWrites: TacticalCommanderModePolicy.AllowsWrites(TacticalCommanderMode.Active),
                playerProtected: false,
                routed: false,
                orderPending: false,
                recentOrder: false));

        AssertTrue(decision.Action != PostureExecutionAction.NoWrite, "active mode can write posture");
        AssertTrue(decision.Reason != "mode-monitor-only", "active mode does not report monitor-only");
    }

    private static void TacticalCommandPostureEligibilityPrecedence()
    {
        AssertPostureDecision(
            PostureExecutionAction.NoWrite,
            "player-protected",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                PhysicalState(),
                new WriteEligibilitySnapshot(true, playerProtected: true, routed: true, orderPending: true, recentOrder: true)));

        AssertPostureDecision(
            PostureExecutionAction.NoWrite,
            "routed",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                PhysicalState(),
                new WriteEligibilitySnapshot(true, playerProtected: false, routed: true, orderPending: true, recentOrder: true)));

        AssertPostureDecision(
            PostureExecutionAction.NoWrite,
            "order-pending",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                PhysicalState(),
                new WriteEligibilitySnapshot(true, playerProtected: false, routed: false, orderPending: true, recentOrder: true)));

        AssertPostureDecision(
            PostureExecutionAction.NoWrite,
            "recent-order",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                PhysicalState(),
                new WriteEligibilitySnapshot(true, playerProtected: false, routed: false, orderPending: false, recentOrder: true)));
    }

    private static void TacticalCommandPosturePhysicalProtectionFailsClosed()
    {
        AssertPostureDecision(
            PostureExecutionAction.NoWrite,
            "player-protected",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                new CommandPhysicalState(
                    routed: false,
                    playerProtected: true,
                    pathInterrupted: false,
                    paths: 1,
                    activeMove: false,
                    formation: 1),
                EligibilityAllowsWrites()));

        AssertPostureDecision(
            PostureExecutionAction.NoWrite,
            "routed",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                new CommandPhysicalState(
                    routed: true,
                    playerProtected: false,
                    pathInterrupted: false,
                    paths: 1,
                    activeMove: false,
                    formation: 1),
                EligibilityAllowsWrites()));
    }

    private static void TacticalCommandPostureInterruptedIllegalIdleRecovery()
    {
        var decision = CommandPostureExecutor.Decide(
            CommandState(CommandTaskType.AttackObjective),
            new CommandPhysicalState(
                routed: false,
                playerProtected: false,
                pathInterrupted: true,
                paths: 0,
                activeMove: false,
                formation: 1),
            EligibilityAllowsWrites());

        AssertPostureDecision(PostureExecutionAction.RecoverInterruptedOrder, "illegal-idle-path-interrupted", decision);
    }

    private static void TacticalCommandPostureNoWriteGatesAfterEligibility()
    {
        AssertPostureDecision(
            PostureExecutionAction.NoWrite,
            "movement-in-progress",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                new CommandPhysicalState(
                    routed: false,
                    playerProtected: false,
                    pathInterrupted: false,
                    paths: 2,
                    activeMove: true,
                    formation: 1),
                EligibilityAllowsWrites()));

        AssertPostureDecision(
            PostureExecutionAction.NoWrite,
            "already-correct",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                PhysicalState(),
                new WriteEligibilitySnapshot(
                    modeAllowsWrites: true,
                    playerProtected: false,
                    routed: false,
                    orderPending: false,
                    recentOrder: false,
                    alreadyDoingCorrectTask: true)));

        AssertPostureDecision(
            PostureExecutionAction.NoWrite,
            "missing-ledger-assignment",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.None),
                PhysicalState(),
                EligibilityAllowsWrites()));
    }

    private static void TacticalCommandPostureReserveWaitDistinguishesReserveArea()
    {
        AssertPostureDecision(
            PostureExecutionAction.SetWaypoint,
            "reserve-area",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.ReserveWait),
                PhysicalState(),
                EligibilityAllowsWrites()));
        AssertPostureTarget(PostureExecutionTarget.ReserveArea, false, CommandPostureExecutor.Decide(
            CommandState(CommandTaskType.ReserveWait),
            PhysicalState(),
            EligibilityAllowsWrites()));

        AssertPostureDecision(
            PostureExecutionAction.SetFormation,
            "reserve-hold",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.ReserveWait),
                PhysicalState(),
                new WriteEligibilitySnapshot(
                    modeAllowsWrites: true,
                    playerProtected: false,
                    routed: false,
                    orderPending: false,
                    recentOrder: false,
                    atAssignedLocation: true)));
    }

    private static void TacticalCommandPostureCloseEngagementLimitsMovementWrites()
    {
        AssertPostureDecision(
            PostureExecutionAction.SetFormation,
            "close-engaged-attack-objective",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                PhysicalState(),
                new WriteEligibilitySnapshot(
                    modeAllowsWrites: true,
                    playerProtected: false,
                    routed: false,
                    orderPending: false,
                    recentOrder: false,
                    closeEngaged: true)));
        AssertPostureTarget(
            PostureExecutionTarget.CurrentPosition,
            false,
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                PhysicalState(),
                new WriteEligibilitySnapshot(
                    modeAllowsWrites: true,
                    playerProtected: false,
                    routed: false,
                    orderPending: false,
                    recentOrder: false,
                    closeEngaged: true)));

        AssertPostureDecision(
            PostureExecutionAction.SetFormation,
            "close-engaged-form-up",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.FormUp),
                PhysicalState(),
                new WriteEligibilitySnapshot(
                    modeAllowsWrites: true,
                    playerProtected: false,
                    routed: false,
                    orderPending: false,
                    recentOrder: false,
                    closeEngaged: true)));

        AssertPostureDecision(
            PostureExecutionAction.SetFormation,
            "close-engaged-attack-objective",
            CommandPostureExecutor.Decide(
                CommandState(CommandTaskType.AttackObjective),
                new CommandPhysicalState(
                    routed: false,
                    playerProtected: false,
                    pathInterrupted: true,
                    paths: 0,
                    activeMove: false,
                    formation: 1),
                new WriteEligibilitySnapshot(
                    modeAllowsWrites: true,
                    playerProtected: false,
                    routed: false,
                    orderPending: false,
                    recentOrder: false,
                    closeEngaged: true)));

        var closeFallback = CommandPostureExecutor.Decide(
            CommandState(CommandTaskType.FallBackToLine),
            new CommandPhysicalState(
                routed: false,
                playerProtected: false,
                pathInterrupted: true,
                paths: 1,
                activeMove: false,
                formation: 0),
            new WriteEligibilitySnapshot(
                modeAllowsWrites: true,
                playerProtected: false,
                routed: false,
                orderPending: false,
                recentOrder: false,
                closeEngaged: true));
        AssertPostureDecision(
            PostureExecutionAction.SetFormationAndWaypoint,
            "close-engaged-fallback-line",
            closeFallback);
        AssertPostureTarget(PostureExecutionTarget.FallbackLine, false, closeFallback);
    }

    private static void TacticalCommandPostureMapsTaskFamilies()
    {
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "form-up", DecidePosture(CommandTaskType.FormUp));
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "advance-to-assembly", DecidePosture(CommandTaskType.AdvanceToAssembly));
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "attack-objective", DecidePosture(CommandTaskType.AttackObjective));
        AssertPostureTarget(PostureExecutionTarget.ObjectiveApproach, false, DecidePosture(CommandTaskType.AttackObjective));

        AssertPostureDecision(PostureExecutionAction.SetFormation, "hold-objective", DecidePosture(CommandTaskType.HoldObjective));
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "fix-enemy", DecidePosture(CommandTaskType.FixEnemy));
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "screen", DecidePosture(CommandTaskType.Screen));
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "probe", DecidePosture(CommandTaskType.Probe));
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "support-attack", DecidePosture(CommandTaskType.SupportAttack));
        AssertPostureDecision(PostureExecutionAction.SetFormation, "guard-flank", DecidePosture(CommandTaskType.GuardFlank));
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "scout", DecidePosture(CommandTaskType.Scout));
        AssertPostureDecision(PostureExecutionAction.SetFormation, "hold-choke", DecidePosture(CommandTaskType.HoldChoke));
        AssertPostureDecision(PostureExecutionAction.SetFormation, "delay", DecidePosture(CommandTaskType.Delay));
        AssertPostureDecision(PostureExecutionAction.SetFormation, "consolidate", DecidePosture(CommandTaskType.Consolidate));

        AssertPostureDecision(PostureExecutionAction.FallbackToLine, "fallback-line", DecidePosture(CommandTaskType.FallBackToLine));
        AssertPostureTarget(PostureExecutionTarget.FallbackLine, false, DecidePosture(CommandTaskType.FallBackToLine));
        AssertPostureDecision(PostureExecutionAction.ReleaseReserve, "release-reserve", DecidePosture(CommandTaskType.ReleaseReserve));
        AssertPostureTarget(PostureExecutionTarget.ReleasePoint, false, DecidePosture(CommandTaskType.ReleaseReserve));
        AssertPostureDecision(PostureExecutionAction.RecoverInterruptedOrder, "recover-stuck-order", DecidePosture(CommandTaskType.RecoverStuckOrder));
        AssertPostureTarget(PostureExecutionTarget.RecoveryPath, true, DecidePosture(CommandTaskType.RecoverStuckOrder));
        AssertPostureDecision(PostureExecutionAction.NoWrite, "missing-ledger-assignment", DecidePosture(CommandTaskType.None));
    }

    private static void PostureExecutorUsesDoctrineAttackTarget()
    {
        var decision = CommandPostureExecutor.Decide(
            DoctrineOrder(CommandTaskType.AttackObjective, primary: DoctrineTargetPoint.From(125f, 250f)),
            PhysicalState(),
            nowSeconds: 100f);

        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "attack-objective", decision);
        AssertPostureTarget(PostureExecutionTarget.DoctrinePrimaryTarget, false, decision);
    }

    private static void PostureExecutorUsesDoctrineAssemblyTarget()
    {
        var formUp = CommandPostureExecutor.Decide(
            DoctrineOrder(CommandTaskType.FormUp, primary: DoctrineTargetPoint.From(220f, 360f)),
            PhysicalState(),
            nowSeconds: 100f);
        var advance = CommandPostureExecutor.Decide(
            DoctrineOrder(CommandTaskType.AdvanceToAssembly, primary: DoctrineTargetPoint.From(260f, 390f)),
            PhysicalState(),
            nowSeconds: 100f);

        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "form-up", formUp);
        AssertPostureTarget(PostureExecutionTarget.DoctrinePrimaryTarget, false, formUp);
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "advance-to-assembly", advance);
        AssertPostureTarget(PostureExecutionTarget.DoctrinePrimaryTarget, false, advance);
    }

    private static void PostureExecutorMovesScoutProbeAndScreenToDoctrineTarget()
    {
        var scout = CommandPostureExecutor.Decide(
            DoctrineOrder(CommandTaskType.Scout, primary: DoctrineTargetPoint.From(125f, 250f)),
            PhysicalState(),
            nowSeconds: 100f);
        var probe = CommandPostureExecutor.Decide(
            DoctrineOrder(CommandTaskType.Probe, primary: DoctrineTargetPoint.From(140f, 260f)),
            PhysicalState(),
            nowSeconds: 100f);
        var screen = CommandPostureExecutor.Decide(
            DoctrineOrder(CommandTaskType.Screen, primary: DoctrineTargetPoint.From(160f, 280f)),
            PhysicalState(),
            nowSeconds: 100f);

        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "scout", scout);
        AssertPostureTarget(PostureExecutionTarget.DoctrinePrimaryTarget, false, scout);
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "probe", probe);
        AssertPostureTarget(PostureExecutionTarget.DoctrinePrimaryTarget, false, probe);
        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "screen", screen);
        AssertPostureTarget(PostureExecutionTarget.DoctrinePrimaryTarget, false, screen);
    }

    private static void PostureExecutorProbeScoutScreenRequireObjectiveProgress()
    {
        AssertFalse(CommandPostureExecutor.IdleCanSatisfyTask(CommandTaskType.Scout), "scout idle must still move to objective");
        AssertFalse(CommandPostureExecutor.IdleCanSatisfyTask(CommandTaskType.Probe), "probe idle must still move to objective");
        AssertFalse(CommandPostureExecutor.IdleCanSatisfyTask(CommandTaskType.Screen), "screen idle must still move to objective");
        AssertFalse(CommandPostureExecutor.IdleCanSatisfyTask(CommandTaskType.FixEnemy), "fix idle must still move to objective");
        AssertTrue(CommandPostureExecutor.IdleCanSatisfyTask(CommandTaskType.ReserveWait), "reserve idle is legal");
        AssertTrue(CommandPostureExecutor.IdleCanSatisfyTask(CommandTaskType.HoldObjective), "hold objective idle is legal");
    }

    private static void PostureExecutorCloseScreenBreaksOffToDoctrineFallback()
    {
        var decision = CommandPostureExecutor.Decide(
            DoctrineOrder(
                CommandTaskType.Screen,
                primary: DoctrineTargetPoint.From(125f, 250f),
                fallback: DoctrineTargetPoint.From(80f, 200f),
                role: CommandNodeRole.ScreeningForce),
            PhysicalState(),
            new WriteEligibilitySnapshot(
                modeAllowsWrites: true,
                playerProtected: false,
                routed: false,
                orderPending: false,
                recentOrder: false,
                closeEngaged: true),
            nowSeconds: 100f);

        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "close-engaged-screen-breakoff", decision);
        AssertPostureTarget(PostureExecutionTarget.DoctrineFallbackTarget, false, decision);
    }

    private static void PostureExecutorMovesReserveToDoctrineRallyTarget()
    {
        var decision = CommandPostureExecutor.Decide(
            DoctrineOrder(
                CommandTaskType.ReserveWait,
                primary: DoctrineTargetPoint.From(700f, 50f),
                role: CommandNodeRole.Reserve,
                allowedIdle: DoctrineAllowedIdleReason.HeldReserve),
            PhysicalState(),
            new WriteEligibilitySnapshot(
                modeAllowsWrites: true,
                playerProtected: false,
                routed: false,
                orderPending: false,
                recentOrder: false,
                atAssignedLocation: false),
            nowSeconds: 100f);

        AssertPostureDecision(PostureExecutionAction.SetWaypoint, "reserve-area", decision);
        AssertPostureTarget(PostureExecutionTarget.DoctrinePrimaryTarget, false, decision);
    }

    private static void PostureExecutorClearsInterruptedInactiveOrder()
    {
        var decision = CommandPostureExecutor.Decide(
            DoctrineOrder(CommandTaskType.SupportAttack, primary: DoctrineTargetPoint.From(125f, 250f)),
            new CommandPhysicalState(
                routed: false,
                playerProtected: false,
                pathInterrupted: true,
                paths: 2,
                activeMove: false,
                formation: 1),
            nowSeconds: 100f);

        AssertPostureDecision(PostureExecutionAction.RecoverInterruptedOrder, "interrupted-inactive", decision);
        AssertPostureTarget(PostureExecutionTarget.RecoveryPath, true, decision);
    }

    private static void PostureExecutorFallbackDoctrineOverridesInterruptedStalePath()
    {
        var decision = CommandPostureExecutor.Decide(
            DoctrineOrder(
                CommandTaskType.FallBackToLine,
                fallback: DoctrineTargetPoint.From(90f, 120f)),
            new CommandPhysicalState(
                routed: false,
                playerProtected: false,
                pathInterrupted: true,
                paths: 2,
                activeMove: false,
                formation: 3),
            nowSeconds: 100f);

        AssertPostureDecision(PostureExecutionAction.FallbackToLine, "fallback-line", decision);
        AssertPostureTarget(PostureExecutionTarget.DoctrineFallbackTarget, true, decision);
    }

    private static void PostureExecutorPreservesLegalReserveIdle()
    {
        var decision = CommandPostureExecutor.Decide(
            DoctrineOrder(
                CommandTaskType.ReserveWait,
                allowedIdle: DoctrineAllowedIdleReason.HeldReserve),
            new CommandPhysicalState(
                routed: false,
                playerProtected: false,
                pathInterrupted: true,
                paths: 0,
                activeMove: false,
                formation: 1),
            nowSeconds: 100f);

        AssertPostureDecision(PostureExecutionAction.NoWrite, "legal-idle", decision);
        AssertPostureTarget(PostureExecutionTarget.None, false, decision);
    }

    private static void PostureExecutorRetriesUnmaterializedRecentAttackOrder()
    {
        var decision = CommandPostureExecutor.Decide(
            DoctrineOrder(CommandTaskType.AttackObjective, primary: DoctrineTargetPoint.From(125f, 250f)),
            new CommandPhysicalState(
                routed: false,
                playerProtected: false,
                pathInterrupted: false,
                paths: 0,
                activeMove: false,
                formation: 3),
            new WriteEligibilitySnapshot(
                modeAllowsWrites: true,
                playerProtected: false,
                routed: false,
                orderPending: false,
                recentOrder: true),
            nowSeconds: 100f);

        AssertPostureDecision(PostureExecutionAction.SetFormationAndWaypoint, "attack-objective", decision);
        AssertPostureTarget(PostureExecutionTarget.DoctrinePrimaryTarget, false, decision);
    }

    private static void PostureExecutorBattleWaypointsBypassDelayedOrderQueue()
    {
        // T4 B-61clock: AI group moves use the courier queue like vanilla
        // (UpdateMovingTargets 6959); only an in-contact withdrawal skips it.
        AssertTrue(
            CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.AttackObjective, battleActive: true),
            "battle attack movement goes through the courier queue");
        AssertFalse(
            CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.FallBackToLine, battleActive: true, inContactWithdrawal: true),
            "in-contact fallback is immediate like CheckIfOutflanked");
        AssertTrue(
            CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.AttackObjective, battleActive: false),
            "non-battle callers should keep vanilla order-delay semantics");
    }

    private static void PostureExecutorScoutAndProbeAreObjectiveMovementTasks()
    {
        AssertTrue(
            CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.Scout, battleActive: true),
            "scout toward objective goes through the courier queue");
        AssertTrue(
            CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(CommandTaskType.Probe, battleActive: true),
            "probe toward objective goes through the courier queue");
    }

    private static void PostureExecutorRetriesStalledScoutAndProbeDespiteRecentOrder()
    {
        var stalled = new CommandPhysicalState(
            routed: false,
            playerProtected: false,
            pathInterrupted: false,
            paths: 0,
            activeMove: false,
            formation: 3);
        var moving = new CommandPhysicalState(
            routed: false,
            playerProtected: false,
            pathInterrupted: false,
            paths: 1,
            activeMove: true,
            formation: 3);

        AssertFalse(
            CommandPostureExecutor.ShouldBlockForRecentOrder(CommandTaskType.Scout, stalled, recentOrder: true),
            "stalled scout needs a retry if the order did not materialize");
        AssertFalse(
            CommandPostureExecutor.ShouldBlockForRecentOrder(CommandTaskType.Probe, stalled, recentOrder: true),
            "stalled probe needs a retry if the order did not materialize");
        AssertTrue(
            CommandPostureExecutor.ShouldBlockForRecentOrder(CommandTaskType.Scout, moving, recentOrder: true),
            "moving scout should not be reissued");
        AssertTrue(
            CommandPostureExecutor.ShouldBlockForRecentOrder(CommandTaskType.Probe, moving, recentOrder: true),
            "moving probe should not be reissued");
    }

    private static void PostureExecutorLongAttackUsesVanillaBlockedMovementOrder()
    {
        // Whether the side has blocked-crossing data is the executor runtime's
        // check (AIBattle.blockedcrossings, null on a map without crossings, 7919).
        AssertTrue(
            CommandWaypointWritePolicy.ShouldUseBlockedMovingOrderForExecutorWaypoint(CommandTaskType.AttackObjective, distance: 900f),
            "long battle attack moves should mirror vanilla objective movement through BlockedMovingOrder");

        AssertFalse(
            CommandWaypointWritePolicy.ShouldUseBlockedMovingOrderForExecutorWaypoint(CommandTaskType.FallBackToLine, distance: 900f),
            "fallback should not use the objective blocked-move queue");

        AssertFalse(
            CommandWaypointWritePolicy.ShouldUseBlockedMovingOrderForExecutorWaypoint(CommandTaskType.AttackObjective, distance: float.NaN),
            "no distance, no blocked-move queue");
    }

    private static void PostureExecutorGroupMovementFlagsCountAsActiveMove()
    {
        AssertFalse(
            CommandWaypointWritePolicy.IsExecutorMovementActive(
                pathInterrupted: false,
                regimentPaths: 0,
                movementMode: 0,
                groupSubordinatesMoving: 1f,
                groupSubordinatesMovingNonAi: 0f,
                hasLastWaypoint: true,
                distanceToLastWaypoint: 500f,
                minWaypointDistance: 15f),
            "stale vanilla group movement flags without any active path should not block objective reissue");

        AssertTrue(
            CommandWaypointWritePolicy.IsExecutorMovementActive(
                pathInterrupted: false,
                regimentPaths: 1,
                movementMode: 0,
                groupSubordinatesMoving: 1f,
                groupSubordinatesMovingNonAi: 0f,
                hasLastWaypoint: false,
                distanceToLastWaypoint: 0f,
                minWaypointDistance: 15f),
            "vanilla group movement flags with a real group or subordinate path mean the command is moving");

        AssertFalse(
            CommandWaypointWritePolicy.IsExecutorMovementActive(
                pathInterrupted: true,
                regimentPaths: 2,
                movementMode: 3,
                groupSubordinatesMoving: 1f,
                groupSubordinatesMovingNonAi: 1f,
                hasLastWaypoint: true,
                distanceToLastWaypoint: 500f,
                minWaypointDistance: 15f),
            "interrupted movement should not be treated as active progress");
    }

    private static void PostureExecutorDuplicateWaypointPolicySuppressesSameTarget()
    {
        AssertTrue(
            CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint(
                currentWaypointX: 1880.5f,
                currentWaypointZ: -708.5f,
                targetX: 1880.9f,
                targetZ: -708.2f,
                tolerance: 5f,
                pathInterrupted: false),
            "same assembly target should not enqueue another waypoint");

        // Microshift fix (2026-05-21): interrupted paths with NEAR-same target should
        // still dedup. Previously this returned false unconditionally, which caused
        // the unit "microshift" pattern at high compression — every path-interrupt
        // cycle re-issued SetWaypoint to a ~0.5m-different target and vanilla
        // recomputed the path, briefly reversing the unit. The new behavior only
        // bypasses dedup when the new target has truly relocated (>InterruptedReissueDistance).
        AssertTrue(
            CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint(
                currentWaypointX: 1880.5f,
                currentWaypointZ: -708.5f,
                targetX: 1880.9f,
                targetZ: -708.2f,
                tolerance: 5f,
                pathInterrupted: true),
            "interrupted + same target should NOT trigger a wasteful re-write (microshift fix)");

        // Confirm the new behavior — interrupted + genuinely different target DOES
        // still bypass dedup. 200m delta is well past the 60m InterruptedReissueDistance.
        AssertFalse(
            CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint(
                currentWaypointX: 1880.5f,
                currentWaypointZ: -708.5f,
                targetX: 2080.5f,
                targetZ: -708.5f,
                tolerance: 5f,
                pathInterrupted: true),
            "interrupted + genuinely-relocated target should still allow re-write");

        AssertFalse(
            CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint(
                currentWaypointX: 1880.5f,
                currentWaypointZ: -708.5f,
                targetX: 1880.9f,
                targetZ: -708.2f,
                tolerance: 5f,
                pathInterrupted: false,
                regimentPaths: 0,
                activeMove: false,
                currentX: 650.5f,
                currentZ: -1347.5f),
            "stale duplicate waypoint with no active path should be reissued");
    }

    private static PostureExecutionDecision DecidePosture(CommandTaskType task)
    {
        return CommandPostureExecutor.Decide(CommandState(task), PhysicalState(), EligibilityAllowsWrites());
    }

    private static CommandPhysicalState PhysicalState()
    {
        return new CommandPhysicalState(
            routed: false,
            playerProtected: false,
            pathInterrupted: false,
            paths: 1,
            activeMove: false,
            formation: 1);
    }

    private static WriteEligibilitySnapshot EligibilityAllowsWrites()
    {
        return new WriteEligibilitySnapshot(
            modeAllowsWrites: true,
            playerProtected: false,
            routed: false,
            orderPending: false,
            recentOrder: false);
    }

    private static void AssertPostureDecision(
        PostureExecutionAction action,
        string reason,
        PostureExecutionDecision decision)
    {
        AssertEqual(action, decision.Action, "posture action");
        AssertEqual(reason, decision.Reason, "posture reason");
    }

    private static void AssertPostureTarget(
        PostureExecutionTarget target,
        bool clearInterruptedPaths,
        PostureExecutionDecision decision)
    {
        AssertEqual(target, decision.Target, "posture target");
        AssertEqual(clearInterruptedPaths, decision.ClearInterruptedPaths, "posture clear interrupted paths");
    }

    private static void TacticalCommandMonitorReserveIdleValid()
    {
        var state = new CommandNodeOperationalState(
            "reserve-1",
            CommandEchelonKind.DivisionLike,
            CommandNodeRole.Reserve,
            CommandTaskType.ReserveWait,
            CommandTaskState.Planning);
        var physical = new CommandPhysicalState(
            routed: false,
            playerProtected: false,
            pathInterrupted: true,
            paths: 0,
            activeMove: false,
            formation: 0);

        AssertEqual(TacticalIdleClassification.ValidIdle, TacticalCommandMonitor.ClassifyIdle(state, physical), "reserve wait idle");
    }

    private static void TacticalCommandMonitorPathInterruptedIdleIllegal()
    {
        var state = new CommandNodeOperationalState(
            "main-1",
            CommandEchelonKind.DivisionLike,
            CommandNodeRole.MainEffort,
            CommandTaskType.AttackObjective,
            CommandTaskState.Committed);
        var physical = new CommandPhysicalState(
            routed: false,
            playerProtected: false,
            pathInterrupted: true,
            paths: -3,
            activeMove: false,
            formation: 1);

        AssertEqual(0, physical.Paths, "negative paths sanitize");
        AssertEqual(TacticalIdleClassification.IllegalIdle, TacticalCommandMonitor.ClassifyIdle(state, physical), "interrupted idle");
    }

    private static void TacticalCommandMonitorInterruptedHoldIsIllegal()
    {
        var state = new CommandNodeOperationalState(
            "hold-1",
            CommandEchelonKind.BrigadeLike,
            CommandNodeRole.MainEffort,
            CommandTaskType.HoldObjective,
            CommandTaskState.Committed);
        var physical = new CommandPhysicalState(
            routed: false,
            playerProtected: false,
            pathInterrupted: true,
            paths: 0,
            activeMove: false,
            formation: 3);

        AssertEqual(TacticalIdleClassification.IllegalIdle, TacticalCommandMonitor.ClassifyIdle(state, physical), "interrupted hold needs recovery");
        AssertPostureDecision(
            PostureExecutionAction.RecoverInterruptedOrder,
            "illegal-idle-path-interrupted",
            CommandPostureExecutor.Decide(state, physical, EligibilityAllowsWrites()));
    }

    private static void TacticalCommandMonitorPlayerProtectedNoWrite()
    {
        var state = new CommandNodeOperationalState(
            "player-child",
            CommandEchelonKind.BrigadeLike,
            CommandNodeRole.MainEffort,
            CommandTaskType.AttackObjective,
            CommandTaskState.Committed);
        var physical = new CommandPhysicalState(
            routed: false,
            playerProtected: true,
            pathInterrupted: true,
            paths: 0,
            activeMove: false,
            formation: 2);

        AssertEqual(TacticalIdleClassification.ProtectedNoWrite, TacticalCommandMonitor.ClassifyIdle(state, physical), "player protected");
    }

    private static void TacticalCommandTaskPlannerMainEffortAttackVsDefensiveHold()
    {
        AssertEqual(
            CommandTaskType.AttackObjective,
            CommandNodeTaskPlanner.PlanTask(CommandNodeRole.MainEffort, TacticalOperationShape.SingleMainEffort, contact: false, atObjective: false),
            "offensive main effort");
        AssertEqual(
            CommandTaskType.HoldObjective,
            CommandNodeTaskPlanner.PlanTask(CommandNodeRole.MainEffort, TacticalOperationShape.DefensiveNetwork, contact: true, atObjective: true),
            "defensive main effort");
    }

    private static void TacticalCommandTaskPlannerMapsRoleTable()
    {
        AssertEqual(CommandTaskType.ReserveWait, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.Reserve, TacticalOperationShape.SingleMainEffort, contact: false, atObjective: false), "reserve");
        AssertEqual(CommandTaskType.AdvanceToAssembly, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.Defender, TacticalOperationShape.SingleMainEffort, contact: false, atObjective: false), "defender advance");
        AssertEqual(CommandTaskType.HoldObjective, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.Defender, TacticalOperationShape.SingleMainEffort, contact: false, atObjective: true), "defender hold");
        AssertEqual(CommandTaskType.FallBackToLine, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.FallbackGuard, TacticalOperationShape.DelayAndFallback, contact: false, atObjective: false), "fallback guard");
        AssertEqual(CommandTaskType.AdvanceToAssembly, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.FixingForce, TacticalOperationShape.FixAndFlank, contact: false, atObjective: false), "fixing advance");
        AssertEqual(CommandTaskType.FixEnemy, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.FixingForce, TacticalOperationShape.FixAndFlank, contact: true, atObjective: false), "fixing contact");
        AssertEqual(CommandTaskType.Screen, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.ScreeningForce, TacticalOperationShape.SingleMainEffort, contact: false, atObjective: false), "screen");
        AssertEqual(CommandTaskType.Probe, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.Probe, TacticalOperationShape.SingleMainEffort, contact: false, atObjective: false), "probe");
        AssertEqual(CommandTaskType.SupportAttack, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.SupportingAttack, TacticalOperationShape.ParallelObjectives, contact: true, atObjective: false), "support");
        AssertEqual(CommandTaskType.GuardFlank, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.FlankMarch, TacticalOperationShape.FixAndFlank, contact: false, atObjective: false), "flank march");
        AssertEqual(CommandTaskType.FormUp, CommandNodeTaskPlanner.PlanTask(CommandNodeRole.Unknown, TacticalOperationShape.SingleMainEffort, contact: false, atObjective: false), "unknown");
    }

    private static void TacticalCommandNodeStateSanitizesBlankNodeId()
    {
        var state = new CommandNodeOperationalState(
            " ",
            CommandEchelonKind.CorpsLike,
            CommandNodeRole.SupportingAttack,
            CommandTaskType.SupportAttack,
            CommandTaskState.Planning);

        AssertEqual("node-unknown", state.NodeId, "blank node id");
        AssertEqual(CommandEchelonKind.CorpsLike, state.Echelon, "echelon");
        AssertEqual(CommandNodeRole.SupportingAttack, state.Role, "role");
        AssertEqual(CommandTaskType.SupportAttack, state.Task, "task");
        AssertEqual(CommandTaskState.Planning, state.TaskState, "task state");
    }

    private static void TacticalCommanderModeActiveAllowsWrites()
    {
        var mode = TacticalCommanderModePolicy.Parse("Active", TacticalCommanderMode.MonitorOnly);

        AssertEqual(TacticalCommanderMode.Active, mode, "mode");
        AssertTrue(TacticalCommanderModePolicy.RunsLedger(mode), "ledger");
        AssertTrue(TacticalCommanderModePolicy.AllowsWrites(mode), "writes");
    }

    private static void TacticalCommanderModeMonitorRunsNoWrites()
    {
        var mode = TacticalCommanderModePolicy.Parse("MonitorOnly", TacticalCommanderMode.Active);

        AssertEqual(TacticalCommanderMode.MonitorOnly, mode, "mode");
        AssertTrue(TacticalCommanderModePolicy.RunsLedger(mode), "ledger");
        AssertFalse(TacticalCommanderModePolicy.AllowsWrites(mode), "writes");
    }

    private static void TacticalCommanderModeParsesSpacingAndFallback()
    {
        AssertEqual(
            TacticalCommanderMode.MonitorOnly,
            TacticalCommanderModePolicy.Parse(" monitor only ", TacticalCommanderMode.Off),
            "monitor only spacing");
        AssertEqual(
            TacticalCommanderMode.MonitorOnly,
            TacticalCommanderModePolicy.Parse("monitor-only", TacticalCommanderMode.Off),
            "monitor hyphen");
        AssertEqual(
            TacticalCommanderMode.Active,
            TacticalCommanderModePolicy.Parse("unknown", TacticalCommanderMode.Active),
            "unknown fallback");
        AssertEqual(
            TacticalCommanderMode.Off,
            TacticalCommanderModePolicy.Parse(" ", TacticalCommanderMode.Off),
            "blank fallback");
        AssertFalse(TacticalCommanderModePolicy.RunsLedger(TacticalCommanderMode.Off), "off ledger");
        AssertFalse(TacticalCommanderModePolicy.AllowsWrites(TacticalCommanderMode.Off), "off writes");
    }

    private static void TacticalCommanderModeActiveEmitsLedgerTelemetry()
    {
        AssertTrue(TacticalCommanderModePolicy.EmitsLedgerTelemetry(TacticalCommanderMode.Active), "active telemetry");
        AssertTrue(TacticalCommanderModePolicy.EmitsLedgerTelemetry(TacticalCommanderMode.MonitorOnly), "monitor telemetry");
        AssertFalse(TacticalCommanderModePolicy.EmitsLedgerTelemetry(TacticalCommanderMode.Off), "off telemetry");
    }

    private static void TacticalCommanderActiveRemainsConfiguredDefault()
    {
        var configuredDefault = TacticalCommanderModePolicy.Parse("Active", TacticalCommanderMode.Off);

        AssertEqual(TacticalCommanderMode.Active, configuredDefault, "configured default");
        AssertTrue(TacticalCommanderModePolicy.RunsLedger(configuredDefault), "default runs ledger");
        AssertTrue(TacticalCommanderModePolicy.AllowsWrites(configuredDefault), "default allows writes");
        AssertEqual(TacticalCommanderMode.Off, TacticalCommanderModePolicy.Parse("Off", configuredDefault), "off still available");
        AssertEqual(TacticalCommanderMode.MonitorOnly, TacticalCommanderModePolicy.Parse("MonitorOnly", configuredDefault), "monitor-only still available");
    }

    private static void TacticalOrderOutsideBugleRangeIsDelayed()
    {
        var decision = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
            orderDelayEnabled: true,
            queueProcessing: true,
            queueDelayHours: 0.20f,
            delivery: TacticalOrderDelivery.Courier,
            deliveryProcessHours: 9999999f,
            courierMissing: false,
            orderState: 1,
            intendedPathId: 4,
            transmittedPathId: 4,
            contactChangedMaterially: false,
            commanderInitiative01: 0.50f));

        AssertEqual(TacticalOrderFrictionState.Courier, decision.State, "state");
        AssertTrue(decision.IsDelayed, "courier order should be delayed");
        AssertTrue(decision.DelayPressure > 0.10f, "delay pressure should exceed .10");
    }

    private static void TacticalOrderShortBugleProcessTimeIsDelivered()
    {
        var decision = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
            orderDelayEnabled: true,
            queueProcessing: false,
            queueDelayHours: 0f,
            delivery: TacticalOrderDelivery.Bugle,
            deliveryProcessHours: 0.02f,
            courierMissing: false,
            orderState: 0,
            intendedPathId: 4,
            transmittedPathId: 4,
            contactChangedMaterially: false,
            commanderInitiative01: 0.50f));

        AssertEqual(TacticalOrderFrictionState.Immediate, decision.State, "state");
        AssertTrue(decision.IsDelivered, "short bugle process time should be treated as delivered");
        AssertFalse(decision.IsDelayed, "short bugle process time should not create delay friction");
    }

    private static void TacticalOrderDeliveredTransmittedPathDiffersWhileDelayed()
    {
        var decision = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
            orderDelayEnabled: true,
            queueProcessing: true,
            queueDelayHours: 0.20f,
            delivery: TacticalOrderDelivery.Courier,
            deliveryProcessHours: 0f,
            courierMissing: false,
            orderState: 1,
            intendedPathId: 5,
            transmittedPathId: 2,
            contactChangedMaterially: false,
            commanderInitiative01: 0.50f));

        AssertEqual(TacticalOrderFrictionState.Pending, decision.State, "state");
        AssertTrue(decision.TransmittedPathDiffers, "transmitted path should differ");
        AssertTrue(!decision.IsDelivered, "pending order should not be delivered");
    }

    private static void TacticalOrderStaleDelayedOrderDowngradesOnContactChange()
    {
        var decision = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
            orderDelayEnabled: true,
            queueProcessing: true,
            queueDelayHours: 0.20f,
            delivery: TacticalOrderDelivery.Courier,
            deliveryProcessHours: 9999999f,
            courierMissing: false,
            orderState: 1,
            intendedPathId: 4,
            transmittedPathId: 1,
            contactChangedMaterially: true,
            commanderInitiative01: 0.50f));

        AssertEqual(TacticalOrderFrictionState.Stale, decision.State, "state");
        AssertTrue(decision.IsDelayed, "stale order should remain delayed");
    }

    private static void TacticalOrderHighInitiativeReducesDelayPressureWithoutInstant()
    {
        var low = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
            orderDelayEnabled: true,
            queueProcessing: true,
            queueDelayHours: 0.20f,
            delivery: TacticalOrderDelivery.Courier,
            deliveryProcessHours: 9999999f,
            courierMissing: false,
            orderState: 1,
            intendedPathId: 4,
            transmittedPathId: 4,
            contactChangedMaterially: false,
            commanderInitiative01: 0.10f));
        var high = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
            orderDelayEnabled: true,
            queueProcessing: true,
            queueDelayHours: 0.20f,
            delivery: TacticalOrderDelivery.Courier,
            deliveryProcessHours: 9999999f,
            courierMissing: false,
            orderState: 1,
            intendedPathId: 4,
            transmittedPathId: 4,
            contactChangedMaterially: false,
            commanderInitiative01: 0.90f));

        AssertTrue(high.DelayPressure < low.DelayPressure, "high initiative should reduce delay pressure");
        AssertEqual(TacticalOrderFrictionState.Courier, high.State, "state");
        AssertTrue(!high.IsDelivered, "high initiative should not make courier order instant");
    }

    private static void TacticalOrderSettlementAllowsIdleStanceRetask()
    {
        var decision = TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 0,
            OrderState = 0,
            RegimentPaths = 0,
            PathInterrupted = false,
            MovementMode = 0
        });

        AssertTrue(decision.AllowChange, "idle group should allow stance retask");
        AssertEqual("settled", decision.Reason, "reason");
    }

    private static void TacticalOrderSettlementBlocksQueuedStanceRetask()
    {
        var decision = TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 2,
            OrderState = 0,
            RegimentPaths = 0,
            PathInterrupted = false,
            MovementMode = 0
        });

        AssertFalse(decision.AllowChange, "queued vanilla orders must block stance retask");
        AssertEqual("queued-order", decision.Reason, "reason");
    }

    private static void TacticalOrderSettlementAllowsStaleQueuedPostureBypass()
    {
        var input = new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 2,
            OrderState = 1,
            RegimentPaths = 1,
            PathInterrupted = false,
            MovementMode = 0,
            ActiveMove = false,
            AllowStaleQueuedBypass = true
        };

        var decision = TacticalOrderSettlementGate.Evaluate(input);

        AssertTrue(decision.AllowChange, "stale queued non-moving posture order should allow command recovery");
        AssertEqual("stalled-queued-order", decision.Reason, "reason");
        AssertFalse(
            TacticalOrderSettlementGate.HasBlockingPendingOrder(input),
            "stale queued non-moving order should not block posture writes when bypass is explicit");
    }

    private static void TacticalOrderSettlementBlocksDeliveredPendingStanceRetask()
    {
        var decision = TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 0,
            OrderState = 2,
            RegimentPaths = 0,
            PathInterrupted = false,
            MovementMode = 0
        });

        AssertFalse(decision.AllowChange, "delivered but unapplied orderstate must block stance retask");
        AssertEqual("pending-orderstate", decision.Reason, "reason");
    }

    private static void TacticalOrderSettlementAllowsStalledInterruptedPendingRetask()
    {
        var decision = TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 0,
            OrderState = 1,
            RegimentPaths = 0,
            PathInterrupted = true,
            MovementMode = 0,
            ActiveMove = false
        });

        AssertTrue(decision.AllowChange, "stalled interrupted pending order should allow recovery retask");
        AssertEqual("stalled-interrupted-order", decision.Reason, "reason");
    }

    private static void TacticalOrderSettlementAllowsStalledInterruptedPendingRetaskWithStalePath()
    {
        var input = new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 0,
            OrderState = 1,
            RegimentPaths = 2,
            PathInterrupted = true,
            MovementMode = 0,
            ActiveMove = false
        };

        var decision = TacticalOrderSettlementGate.Evaluate(input);

        AssertTrue(decision.AllowChange, "stalled interrupted order with stale paths should allow recovery retask");
        AssertEqual("stalled-interrupted-order", decision.Reason, "reason");
        AssertFalse(
            TacticalOrderSettlementGate.HasBlockingPendingOrder(input),
            "stalled interrupted order with stale paths should not block posture writes");
    }

    private static void TacticalOrderSettlementStalledInterruptedDoesNotBlockPostureWrite()
    {
        bool stalledBlocks = TacticalOrderSettlementGate.HasBlockingPendingOrder(new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 0,
            OrderState = 1,
            RegimentPaths = 0,
            PathInterrupted = true,
            MovementMode = 0,
            ActiveMove = false
        });

        bool queuedBlocks = TacticalOrderSettlementGate.HasBlockingPendingOrder(new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 1,
            OrderState = 0,
            RegimentPaths = 0,
            PathInterrupted = false,
            MovementMode = 0,
            ActiveMove = false
        });

        AssertFalse(stalledBlocks, "stalled interrupted orderstate should not block recovery posture write");
        AssertTrue(queuedBlocks, "queued courier order should still block posture write");
    }

    private static void TacticalOrderSettlementStalledPendingWithoutPathDoesNotBlockPostureWrite()
    {
        var input = new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 0,
            OrderState = 1,
            RegimentPaths = 0,
            PathInterrupted = false,
            MovementMode = 0,
            ActiveMove = false
        };

        var decision = TacticalOrderSettlementGate.Evaluate(input);

        AssertTrue(decision.AllowChange, "pending order with no path and no active movement should allow recovery retask");
        AssertEqual("stalled-pending-order", decision.Reason, "reason");
        AssertFalse(
            TacticalOrderSettlementGate.HasBlockingPendingOrder(input),
            "stalled pending order with no active path should not block posture writes");
    }

    private static void TacticalOrderSettlementBlocksUnknownOrderState()
    {
        var decision = TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
        {
            OrderQueueCount = 0,
            OrderState = -1,
            RegimentPaths = 0,
            PathInterrupted = false,
            MovementMode = 0
        });

        AssertFalse(decision.AllowChange, "unknown vanilla order state must fail closed");
        AssertEqual("unknown-orderstate", decision.Reason, "reason");
    }

    private static void TacticalCommandArmyCorpsDoesNotRetaskRegimentsDirectly()
    {
        var army = TacticalCommanderProfile.FromVanillaShape(
            stableId: 100,
            displayName: "Army of the Potomac",
            unitType: 16,
            isTopUnit: true,
            underPlayerCommander: true,
            parentId: -1,
            alliance: 0,
            side: 0,
            initiative01: 0.50f);
        var regiment = TacticalCommanderProfile.FromVanillaShape(
            stableId: 101,
            displayName: "20th Maine",
            unitType: 0,
            isTopUnit: false,
            underPlayerCommander: true,
            parentId: 15,
            alliance: 0,
            side: 0,
            initiative01: 0.50f);

        var decision = TacticalCommandLedger.DecideOrderScope(army, regiment);

        AssertEqual(TacticalOrderScope.BlockDirectRegimentRetask, decision.Scope, "scope");
        AssertEqual("army-corps-intent-must-flow-through-subcommand", decision.Reason, "reason");

        var corps = TacticalCommanderProfile.FromVanillaShape(
            stableId: 102,
            displayName: "First Corps",
            unitType: 16,
            isTopUnit: false,
            underPlayerCommander: true,
            parentId: 100,
            alliance: 0,
            side: 0,
            initiative01: 0.50f);

        var corpsDecision = TacticalCommandLedger.DecideOrderScope(corps, regiment);

        AssertEqual(TacticalOrderScope.BlockDirectRegimentRetask, corpsDecision.Scope, "corps scope");
        AssertEqual("army-corps-intent-must-flow-through-subcommand", corpsDecision.Reason, "corps reason");
    }

    private static void TacticalCommandMapsVanillaBattleUnitTiers()
    {
        AssertEqual(TacticalCommandTier.Regiment, TacticalCommanderProfile.TierFromUnitType(13, false), "regiment");
        AssertEqual(TacticalCommandTier.Brigade, TacticalCommanderProfile.TierFromUnitType(14, false), "brigade");
        AssertEqual(TacticalCommandTier.Division, TacticalCommanderProfile.TierFromUnitType(15, false), "division");
        AssertEqual(TacticalCommandTier.Army, TacticalCommanderProfile.TierFromUnitType(16, true), "army");
        AssertEqual(TacticalCommandTier.Corps, TacticalCommanderProfile.TierFromUnitType(16, false), "corps");
    }

    private static void TacticalCommandDivisionMissionMapsToBrigadeActions()
    {
        var division = TacticalCommanderProfile.FromVanillaShape(
            stableId: 200,
            displayName: "First Division",
            unitType: 15,
            isTopUnit: false,
            underPlayerCommander: false,
            parentId: 10,
            alliance: 1,
            side: 1,
            initiative01: 0.50f);
        var brigade = TacticalCommanderProfile.FromVanillaShape(
            stableId: 201,
            displayName: "First Brigade",
            unitType: 14,
            isTopUnit: false,
            underPlayerCommander: false,
            parentId: 200,
            alliance: 1,
            side: 1,
            initiative01: 0.50f);

        var decision = TacticalCommandLedger.DecideOrderScope(division, brigade);

        AssertEqual(TacticalOrderScope.SubcommandAction, decision.Scope, "scope");
        AssertEqual("division-to-brigade", decision.Reason, "reason");
    }

    // ---- TacticalCommanderRoster tests ----

    private static void TacticalCommanderRosterFallsBackToFactionDefaultsForUnknown()
    {
        var roster = TacticalCommanderRoster.BuildFromSynthetic(new[]
        {
            new SyntheticCommanderInput("Some Brigadier", EchelonKind.Brigade, 0)
        });

        var entry = roster.GetByName("Some Brigadier");
        AssertTrue(entry != null, "entry should be present");
        AssertFalse(entry.MatchedHistoricalRegistry, "synthetic entry should not be matched historical");

        // Brigade bias: Aggression += 0.05; other fields unchanged from faction default
        var factionDefault = FactionProfiles.For(0);
        float expectedAgg = PersonalityVector.Clamp(factionDefault.Aggression + 0.05f);
        AssertNear(expectedAgg, entry.PersonalityVector.Aggression, 0.001f, "brigade bias applied to aggression");
        AssertNear(factionDefault.Caution, entry.PersonalityVector.Caution, 0.001f, "caution unchanged for brigade");
        AssertNear(factionDefault.Audacity, entry.PersonalityVector.Audacity, 0.001f, "audacity unchanged for brigade");
        AssertNear(factionDefault.CasualtyTolerance, entry.PersonalityVector.CasualtyTolerance, 0.001f, "casualty tolerance unchanged for brigade");
        AssertNear(factionDefault.PoliticalResponsiveness, entry.PersonalityVector.PoliticalResponsiveness, 0.001f, "political responsiveness unchanged for brigade");
    }

    private static void TacticalCommanderRosterPartitionsBySide()
    {
        var roster = TacticalCommanderRoster.BuildFromSynthetic(new[]
        {
            new SyntheticCommanderInput("A", EchelonKind.Army, 0),
            new SyntheticCommanderInput("B", EchelonKind.Army, 1)
        });

        AssertEqual(2, roster.Count, "total roster count");
        AssertEqual(1, roster.GetSide(0).Count, "side 0 count");
        AssertEqual(1, roster.GetSide(1).Count, "side 1 count");
    }

    private static void TacticalCommanderRosterRankTierBiasIncreasesCautionForCorps()
    {
        var roster = TacticalCommanderRoster.BuildFromSynthetic(new[]
        {
            new SyntheticCommanderInput("X", EchelonKind.Corps, 0)
        });

        var entry = roster.GetByName("X");
        AssertTrue(entry != null, "corps entry should be present");

        var factionDefault = FactionProfiles.For(0);
        AssertTrue(entry.PersonalityVector.Caution > factionDefault.Caution,
            "corps bias should increase Caution above faction default");
    }

    private static void ReinforcementOpportunity15kVs13kMidHoldsAsDefender()
    {
        // 15K own vs 13K known enemy, mid commander. The enemy's schedule is
        // not an input (enemy reinforcements count once in range).
        // CurrentRatio = 1.15 (below mid-band threshold ~1.32)
        // OwnParity = 0 (already ahead) → fails WaitAndConsolidate's > 0 check
        // CurrentRatio above WithdrawalRatioThreshold (0.85) → not Withdraw
        // → DefensiveHold as Defender.
        // BR-H1 (2026-10-14): only a Defender holds here. In a meeting
        // engagement vanilla attacks regardless of force balance
        // (CheckGlobalAIStrategy 6400-6408), so Meeting/Attacker cap the attack
        // threshold at parity and attack on the advantage.
        var ev = MakeForceBalance(15000f, 13000f,
            aggression: 0.5f, stance: BattleStance.Defender);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.DefensiveHold, d.Opportunity,
            "mid defender holds a thin advantage, got reason=" + d.Reason);
        var meeting = TacticalReinforcementOpportunityDoctrine.Decide(MakeForceBalance(15000f, 13000f, aggression: 0.5f));
        AssertEqual(ReinforcementOpportunity.AttackNow, meeting.Opportunity, "meeting engagement attacks on the advantage");
    }

    private static void ReinforcementOpportunity15kVs13kAggressiveAttacks()
    {
        // Same numbers, aggression=0.85 (Hood-style)
        // AttackThreshold drops to ~1.20 * (1.30 - 0.85*0.40) = ~1.15
        // CurrentRatio 1.15 now meets threshold, known enemy below us
        // Decision: AttackNow (advantage-and-enemy-static)
        var ev = MakeForceBalance(15000f, 13000f,
            aggression: 0.85f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.AttackNow, d.Opportunity,
            "aggressive commander attacks a thin advantage, got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunity15kVs13kCautiousHoldsAsDefender()
    {
        // BR-H1: holding at these odds is Defender behaviour; Meeting/Attacker
        // attack at parity like vanilla (CheckGlobalAIStrategy 6400-6408).
        // Same numbers, aggression=0.10 (McClellan-style)
        // AttackThreshold rises to ~1.20 * (1.30 - 0.10*0.40) = ~1.51
        // CurrentRatio 1.15 way below — won't attack
        // CurrentRatio above WithdrawalRatioThreshold (0.85) → not Withdraw
        // → DefensiveHold (cautious commander never attacks the unequal odds AND
        //   doesn't withdraw from a position where own > enemy currently)
        var ev = MakeForceBalance(15000f, 13000f,
            aggression: 0.10f, stance: BattleStance.Defender);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.DefensiveHold, d.Opportunity,
            "cautious commander holds defensively (not attack, not withdraw with own ahead now), got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunity10kVs15kCautiousDelaysDefensively()
    {
        // 10K vs 15K = ratio 0.67. With 2026-05-23 personality-modulated
        // withdrawal threshold, cautious commander (aggression 0.10) threshold
        // is ~0.52. Ratio 0.67 > 0.52 → does NOT withdraw, instead routes to
        // DelayingDefense (outnumbered but defensible, no near parity).
        // Historical CW pattern: Johnston Atlanta-campaign-style Fabian delay.
        var ev = MakeForceBalance(10000f, 15000f,
            aggression: 0.10f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.DelayingDefense, d.Opportunity,
            "moderate disadvantage + no near relief → DelayingDefense (not Withdraw), got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunitySeverelyOutnumberedWithdraws()
    {
        // 5K vs 15K = ratio 0.33. Cautious commander threshold 0.52,
        // ratio 0.33 < 0.52 → WithdrawalToFightLater fires (severe overmatch).
        var ev = MakeForceBalance(5000f, 15000f,
            aggression: 0.10f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.WithdrawalToFightLater, d.Opportunity,
            "severely outnumbered + no relief → Withdraw, got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunityAggressiveCommanderRefusesToWithdrawAtModerateDisadvantage()
    {
        // 6K vs 15K = 0.40 ratio. Aggressive commander (0.95) threshold:
        // 0.55 - 0.95*0.30 = 0.27. Ratio 0.40 > 0.27 → does NOT withdraw.
        // Jackson at Chancellorsville pattern: hold ground even at 2.5:1 odds.
        var ev = MakeForceBalance(6000f, 15000f,
            aggression: 0.95f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.DelayingDefense, d.Opportunity,
            "aggressive commander refuses withdrawal at 2.5:1 odds, got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunitySmokeCaseRoutesToDelayingDefense()
    {
        // 2026-05-23 user-observed smoke scenario, post-fix:
        // CSA at ratio 0.44 (e.g., 5500 vs 12500), reinforcements coming
        // at 19h but parityForOwn=999 (reinforcements don't reach parity).
        // Aggression 0.65 (mid-aggressive). Pre-I doctrine: WithdrawalToFightLater.
        // Post-I doctrine: should route to DelayingDefense — historically correct
        // for an outnumbered army with terrain holding action and reinforcements
        // hours out.
        var ev = MakeForceBalance(5500f, 12500f,
            ownArrivals: new[] { new ReinforcementArrival(19f, 4000f) },  // not enough to reach parity
            aggression: 0.65f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.DelayingDefense, d.Opportunity,
            "user-observed smoke case (CSA 0.44 ratio, 19h relief, never parity) → DelayingDefense, got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunity15kVs10kAttacksOnAdvantage()
    {
        // 15K vs 10K known enemy, mid commander
        // CurrentRatio = 1.5 (above threshold 1.20)
        // Decision: AttackNow (advantage-and-enemy-static)
        var ev = MakeForceBalance(15000f, 10000f,
            aggression: 0.5f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.AttackNow, d.Opportunity,
            "clear advantage → AttackNow, got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunity12kPlus5kIn6hWaitsToConsolidate()
    {
        // 12K own +5K in 6h vs 13K known enemy
        // CurrentRatio = 0.92 (below threshold)
        // OwnParity = 6h (own 17K vs enemy 13K = 1.31)
        // EnemyParity = never (own > enemy after own arrival)
        // Mid-band MaxWaitHours = 7.5h, ParityForOwn 6h fits
        // Decision: WaitAndConsolidate
        var ev = MakeForceBalance(12000f, 13000f,
            ownArrivals: new[] { new ReinforcementArrival(6f, 5000f) },
            aggression: 0.5f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.WaitAndConsolidate, d.Opportunity,
            "own reinforcement arriving in time → WaitAndConsolidate, got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunity13kVs13kOwnReliefTooFarDefensiveHold()
    {
        // BR-H1: holding at these odds is Defender behaviour; Meeting/Attacker
        // attack at parity like vanilla (CheckGlobalAIStrategy 6400-6408).
        // 13K vs 13K known enemy, own +3K in 12h
        // CurrentRatio = 1.0 (below threshold), OwnParity 0 (already level)
        // Decision: DefensiveHold as Defender
        var ev = MakeForceBalance(13000f, 13000f,
            ownArrivals: new[] { new ReinforcementArrival(12f, 3000f) },
            aggression: 0.5f, stance: BattleStance.Defender);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        // CurrentRatio 1.0 is below WithdrawalRatioThreshold(0.85)? No, 1.0 >= 0.85.
        // So withdrawal is NOT triggered. Hold is the right call.
        AssertEqual(ReinforcementOpportunity.DefensiveHold, d.Opportunity,
            "neither side has clear leverage, hold and refuse decisive, got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunityIntelConfidenceFloorFallsThrough()
    {
        // Same scenario as attack-window-open but with intel confidence 0.3
        // (below the 0.35 floor for action) → falls through to NoOpportunity
        var ev = MakeForceBalance(15000f, 10000f,
            aggression: 0.5f, intel: 0.3f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.NoOpportunity, d.Opportunity,
            "poor intel falls through to existing playbook, got reason=" + d.Reason);
    }

    private static void ReinforcementOpportunitySmallCavalryCanAttack()
    {
        // 1500 own vs 800 enemy = ratio 1.875, well above mid-aggression
        // attack threshold ~1.10. Per 2026-05-19 user clarification: GTCW
        // battles dynamically span 1,500 to 60,000+ per side, so the doctrine
        // must NOT spuriously block small-force engagements — a Buford cavalry
        // delaying force or a Forrest raid is a real battle decision worth
        // doctrine. AttackNow fires because (enemy not growing, force adequate
        // for ratio).
        var ev = MakeForceBalance(1500f, 800f,
            aggression: 0.5f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.AttackNow, d.Opportunity,
            "small force at favorable ratio → AttackNow; got " + d.Opportunity + " reason=" + d.Reason);
    }

    private static void ReinforcementOpportunityPicketClashDoesNotAttack()
    {
        // 400 own vs 200 enemy at 2x ratio. Per the 2026-05-19 scale-
        // invariance fix, doctrine has NO absolute headcount floor — only
        // IntelConfidence and ratio gate the decision. A 400-man detachment
        // with full intel and 2x ratio SHOULD AttackNow (Forrest's smaller
        // raids did exactly this). The previous "picket-clash floor" was
        // wrong for the dynamic campaign battle scale (1,500-60,000+).
        //
        // This test now verifies the OPPOSITE: small engagements WITH good
        // intel DO act on the doctrine. Use the intel-confidence test to
        // verify "low intel suppresses regardless of size."
        var ev = MakeForceBalance(400f, 200f, aggression: 0.5f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.AttackNow, d.Opportunity,
            "small force at favorable ratio + good intel → AttackNow (no absolute floor); got " + d.Opportunity + " reason=" + d.Reason);
    }

    private static void ReinforcementOpportunityEmptyArrivalsNeverReachParity()
    {
        // Own AHEAD, no arrivals: enemy never reaches parity (999).
        var aheadNoArrivals = MakeForceBalance(12000f, 10000f);
        AssertTrue(aheadNoArrivals.ParityHoursForEnemy() >= 999f,
            "own ahead + no arrivals → enemy parity never; got " + aheadNoArrivals.ParityHoursForEnemy());
        AssertEqual(0f, aheadNoArrivals.ParityHoursForOwn(),
            "own already ahead → own parity is 0");

        // Equal NOW, no arrivals: enemy is already at parity (>=) at hour 0.
        var equalNoArrivals = MakeForceBalance(10000f, 10000f);
        AssertEqual(0f, equalNoArrivals.ParityHoursForEnemy(),
            "equal at hour 0 → enemy parity is 0 (already there)");

        // Own BEHIND, no arrivals: own never catches up (999).
        var behindNoArrivals = MakeForceBalance(8000f, 10000f);
        AssertTrue(behindNoArrivals.ParityHoursForOwn() >= 999f,
            "own behind + no arrivals → own parity never; got " + behindNoArrivals.ParityHoursForOwn());
    }

    private static void ReinforcementOpportunityStandingAdvantageStaticEnemyAttacksNow()
    {
        // Live-log regression (2026-05-19 session p175884):
        //   CSA had ratio=2.29, enemyParityHrs=never, ownParityHrs=0 — should AttackNow.
        //   Previous logic gated AttackNow on parityForEnemy <= 24h ("defeat-in-detail
        //   urgency"), so a 2.29× advantage with no enemy reinforcement fell through
        //   to DefensiveHold. SoW's AttackEnemy would attack on ratio alone here.
        // 18500 own vs 8100 enemy, no arrivals on either side, mid commander
        // CurrentRatio = 2.28
        // AttackThreshold mid = 1.32, OwnDeployed >= 2000 ✓
        // parityForEnemy = 999 (never) → enemyNotGrowing branch fires
        // Decision: AttackNow, reason "advantage-and-enemy-static"
        var ev = MakeForceBalance(18500f, 8100f, aggression: 0.5f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.AttackNow, d.Opportunity,
            "standing advantage + static enemy must AttackNow (SoW-aligned), got reason=" + d.Reason);
        AssertEqual("advantage-and-enemy-static", d.Reason,
            "reason should identify the static-enemy branch, got " + d.Reason);
    }

    private static void ReinforcementOpportunityStandingAdvantageStaticEnemyBelowMinForceHolds()
    {
        // Per the 2026-05-19 scale-invariance fix: doctrine has NO absolute
        // headcount floor. A 300-vs-100 engagement at 3x ratio with full
        // intel SHOULD AttackNow regardless of total headcount. Test now
        // verifies that small-scale engagements still get doctrine.
        var ev = MakeForceBalance(300f, 100f, aggression: 0.5f);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.AttackNow, d.Opportunity,
            "3x ratio at any scale → AttackNow (scale-invariant doctrine); got " + d.Opportunity + " reason=" + d.Reason);
    }

    private static void ReinforcementOpportunityCautiousStandingAdvantageDoesNotAttack()
    {
        // BR-H1: holding at these odds is Defender behaviour; Meeting/Attacker
        // attack at parity like vanilla (CheckGlobalAIStrategy 6400-6408).
        // 15K vs 13K, no enemy growth, cautious commander (0.10).
        // AttackThreshold @ 0.10 = 1.20 * (1.30 - 0.10*0.40) = 1.512
        // CurrentRatio 1.154 < 1.512 → ratio check fails even though enemy static.
        // Decision: NOT AttackNow (cautious commander needs bigger margin).
        var ev = MakeForceBalance(15000f, 13000f, aggression: 0.10f, stance: BattleStance.Defender);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertTrue(d.Opportunity != ReinforcementOpportunity.AttackNow,
            "cautious commander honors elevated attack threshold despite static enemy, got " + d.Opportunity + " reason=" + d.Reason);
    }

    private static void ReinforcementOpportunityZeroEnemyDoesNotDivideByZero()
    {
        // Edge case: own 5000, enemy 0. CurrentRatio sentinel of 10.0 (own > 0 and enemy=0).
        var ev = MakeForceBalance(5000f, 0f);
        AssertEqual(10f, ev.CurrentRatio, "zero enemy + nonzero own → sentinel 10");
        // Both zero: ratio is 1.0 (no advantage either way)
        var zz = MakeForceBalance(0f, 0f);
        AssertEqual(1f, zz.CurrentRatio, "zero/zero → 1.0");
    }

    private static void ReinforcementOpportunityAttackThresholdScalesWithAggression()
    {
        // Cautious (0.1) high threshold, mid (0.5) baseline, aggressive (0.9) low
        float cautious = TacticalReinforcementOpportunityDoctrine.ComputeAttackThreshold(0.1f);
        float mid = TacticalReinforcementOpportunityDoctrine.ComputeAttackThreshold(0.5f);
        float aggressive = TacticalReinforcementOpportunityDoctrine.ComputeAttackThreshold(0.9f);
        AssertTrue(cautious > mid && mid > aggressive,
            "threshold decreases with aggression: cautious=" + cautious + " mid=" + mid + " aggressive=" + aggressive);
        // Mid-band should be very close to BaseAttackThreshold * (1.30 - 0.20) = 1.32 (not exactly base)
        // Actually 1.20 * (1.30 - 0.5*0.40) = 1.20 * 1.10 = 1.32
        AssertNear(1.32f, mid, 0.001f, "mid-band threshold ~1.32");
    }

    private static void ReinforcementOpportunityAttackWindowScalesWithAggression()
    {
        float cautious = TacticalReinforcementOpportunityDoctrine.ComputeAttackWindowHours(0.1f);
        float aggressive = TacticalReinforcementOpportunityDoctrine.ComputeAttackWindowHours(0.9f);
        AssertTrue(cautious > aggressive, "cautious needs longer window than aggressive");
        // Floor at 0.25h
        AssertTrue(aggressive >= 0.25f, "window floored at 15 min");
    }

    private static void ReinforcementOpportunityWaitHoursScalesWithAggression()
    {
        // 2026-05-23: raised to realistic CW timing.
        //   base 18h + (1 - aggression) * 10 → cautious 28h, aggressive 18h.
        float cautious = TacticalReinforcementOpportunityDoctrine.ComputeMaxWaitHours(0.0f);
        float aggressive = TacticalReinforcementOpportunityDoctrine.ComputeMaxWaitHours(1.0f);
        AssertTrue(cautious > aggressive, "cautious waits longer than aggressive");
        AssertNear(28f, cautious, 0.001f, "cautious max wait ~28h");
        AssertNear(18f, aggressive, 0.001f, "aggressive max wait ~18h");
    }

    private static void ReinforcementOpportunityStrengthAtHourAccumulatesArrivals()
    {
        var ev = MakeForceBalance(10000f, 5000f,
            ownArrivals: new[] { new ReinforcementArrival(2f, 1500f), new ReinforcementArrival(6f, 1000f) });
        AssertEqual(10000f, ev.OwnStrengthAtHour(1f), "no arrivals yet at hour 1");
        AssertEqual(11500f, ev.OwnStrengthAtHour(3f), "first arrival counted at hour 3");
        AssertEqual(12500f, ev.OwnStrengthAtHour(8f), "both arrivals counted at hour 8");
    }

    private static void ReinforcementOpportunityParityHoursForOwnShortCircuitsWhenAlreadyAhead()
    {
        var ahead = MakeForceBalance(15000f, 10000f);
        AssertEqual(0f, ahead.ParityHoursForOwn(), "already ahead → ParityHoursForOwn is 0");
    }
}
