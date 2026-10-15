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

internal static class TacticalBattleTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("tactical battle line planner uses frontage terrain and artillery line", TacticalBattleLinePlannerUsesFrontageTerrainAndArtilleryLine),
            ("tactical battle line planner spreads forming commands across frontage", TacticalBattleLinePlannerSpreadsFormingCommandsAcrossFrontage),
            ("tactical battle line planner uses predicted avenue axis", TacticalBattleLinePlannerUsesPredictedAvenueAxis),
            ("tactical battle line planner defends corridor with reserve and artillery depth", TacticalBattleLinePlannerDefendsCorridorWithReserveAndArtilleryDepth),
            ("tactical battle line planner keeps crossing corridor orders on defensive bank", TacticalBattleLinePlannerKeepsCrossingCorridorOrdersOnDefensiveBank),
            ("tactical battle orchestrator forwards operations ledger update", TacticalBattleOrchestratorForwardsOperationsLedgerUpdate),
            ("tactical battle coordinator side gate blocks player side unless ai vs ai", TacticalBattleCoordinatorSideGateBlocksPlayerSideUnlessAiVsAi),
            ("stance reconciler demotes attacker on defensive doctrine", StanceReconcilerDemotesAttackerOnDefensiveDoctrine),
            ("stance reconciler demotes meeting on defensive doctrine", StanceReconcilerDemotesMeetingOnDefensiveDoctrine),
            ("stance reconciler preserves attacker on attacknow", StanceReconcilerPreservesAttackerOnAttackNow),
            ("stance reconciler preserves attacker on no opportunity", StanceReconcilerPreservesAttackerOnNoOpportunity),
            ("stance reconciler leaves defender unchanged", StanceReconcilerLeavesDefenderUnchanged),
            ("stance reconciler demotes attacker on withdrawal", StanceReconcilerDemotesAttackerOnWithdrawal),
            ("macroai attack-now flanks at clear advantage", MacroAiAttackNowFlankAtClearAdvantage),
            ("macroai attack-now frontal at marginal ratio", MacroAiAttackNowFrontalAtMarginalRatio),
            ("macroai defensive-hold maps to defend", MacroAiDefensiveHoldMapsToDefend),
            ("macroai wait-and-consolidate maps to defend", MacroAiWaitAndConsolidateMapsToDefend),
            ("macroai withdrawal maps to retreat", MacroAiWithdrawalMapsToRetreat),
            ("macroai no-opportunity returns null", MacroAiNoOpportunityReturnsNull),
            ("macroai aggressive commander envelops at parity", MacroAiAggressiveCommanderEnvelopsAtParity),
            ("macroai cautious commander requires clear margin to flank", MacroAiCautiousCommanderRequiresClearMarginToFlank),
            ("stance mapper screen tasks → stance 1", StanceMapperScreenTasksMapToStance1),
            ("stance mapper attack tasks → stance 3", StanceMapperAttackTasksMapToStance3),
            ("stance mapper defensive tasks → stance 2", StanceMapperDefensiveTasksMapToStance2),
            ("stance mapper none → stance 0", StanceMapperNoneTaskMapsToStance0),
            ("collapse risk thresholds bound to break morale trigger", CollapseRiskThresholdsBoundToBreakMoraleTrigger),
            ("tactical battle orchestrator owns alliance and roster", TacticalBattleOrchestratorOwnsAllianceAndRoster),
            ("tactical battle orchestrator attach army exposes army", TacticalBattleOrchestratorAttachArmyExposesArmy),
            ("tactical battle orchestrator attach army idempotent", TacticalBattleOrchestratorAttachArmyIdempotent),
            ("tactical battle plan records id phase main effort and age", TacticalBattlePlanRecordsIdPhaseMainEffortAndAge),
            ("tactical battle plan with phase advances and resets age", TacticalBattlePlanWithPhaseAdvancesAndResetsAge),
            ("tactical battle plan sanitizes NaN and Infinity floats", TacticalBattlePlanSanitizesNanAndInfinityFloats),
            ("tactical battle coordinator starts inactive", TacticalBattleCoordinatorStartsInactive),
            ("tactical battle coordinator activates on battle start with synthetic inputs", TacticalBattleCoordinatorActivatesOnBattleStartWithSyntheticInputs),
            ("tactical battle coordinator suppresses player cic side", TacticalBattleCoordinatorSuppressesPlayerCicSide),
            ("tactical battle coordinator on battle end for test clears state", TacticalBattleCoordinatorOnBattleEndForTestClearsState),
            ("tactical battle coordinator double start is no-op", TacticalBattleCoordinatorDoubleStartIsNoOp),
            ("tactical battle lifecycle detector returns none when no units across ticks", TacticalBattleLifecycleDetectorReturnsNoneWhenNoUnitsAcrossTicks),
            ("tactical battle lifecycle detector returns battle start on first units tick", TacticalBattleLifecycleDetectorReturnsBattleStartOnFirstUnitsTick),
            ("tactical battle lifecycle detector requires two consecutive zero ticks for battle end", TacticalBattleLifecycleDetectorRequiresTwoConsecutiveZeroTicksForBattleEnd),
            ("tactical battle lifecycle detector ignores transient zero tick between units ticks", TacticalBattleLifecycleDetectorIgnoresTransientZeroTickBetweenUnitsTicks),
            ("tactical battle lifecycle detector does not fire double start on subsequent units ticks", TacticalBattleLifecycleDetectorDoesNotFireDoubleStartOnSubsequentUnitsTicks),
            ("tactical battle lifecycle detector restarts battle after end", TacticalBattleLifecycleDetectorRestartsBattleAfterEnd),
            ("enemy visible state records sector and contact fields", EnemyVisibleStateRecordsSectorAndContactFields),
            ("enemy visible state clamps and coerces null sectors", EnemyVisibleStateClampsAndCoercesNullSectors),
            ("compute withdrawal ratio threshold personality scale", ComputeWithdrawalRatioThresholdPersonalityScale),
            ("compute casualty escalation threshold personality scale", ComputeCasualtyEscalationThresholdPersonalityScale),
            ("tactical battle state signature equals identical", TacticalBattleStateSignatureEqualsIdentical),
            ("tactical battle state signature differs on macro ai", TacticalBattleStateSignatureDiffersOnMacroAI),
            ("tactical battle state signature differs on objective hash", TacticalBattleStateSignatureDiffersOnObjectiveHash),
            ("tactical battle state signature differs on active unit count", TacticalBattleStateSignatureDiffersOnActiveUnitCount),
            ("tactical battle state signature equals ignores hour bucket", TacticalBattleStateSignatureEqualsIgnoresHourBucket),
            ("tactical battle runtime snapshot empty is valid", TacticalBattleRuntimeSnapshotEmptyIsValid),
            ("tactical battle runtime snapshot construction sanitizes and stores", TacticalBattleRuntimeSnapshotConstructionSanitizesAndStores),
            ("tactical battle runtime snapshot copies objectives defensively", TacticalBattleRuntimeSnapshotCopiesObjectivesDefensively),
            ("tactical battle runtime snapshot copies direct child snapshots defensively", TacticalBattleRuntimeSnapshotCopiesDirectChildSnapshotsDefensively),
            ("tactical battle runtime snapshot has data reflects contents", TacticalBattleRuntimeSnapshotHasDataReflectsContents),
        });
    }

    private static void TacticalBattleLinePlannerUsesFrontageTerrainAndArtilleryLine()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("left", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, -500f, 0f, 0f),
            CommandNodeOperationalState.Create("center", CommandEchelonKind.BrigadeLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 0f, 0f, 0f),
            CommandNodeOperationalState.Create("right", CommandEchelonKind.BrigadeLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, 500f, 0f, 0f)
        };
        var objective = new BattlefieldObjectiveEstimate("ridge", TacticalObjectiveType.Ridge, 900f, 0.9f, true, 0.8f, 0f, 1000f, 0.85f, 0.2f);
        TacticalBattlefrontSnapshot frontage = TacticalBattlefrontGeometry.Build(nodes, objective);

        TacticalBattleLinePlan support = TacticalBattleLinePlanner.PlanNode(
            nodes[1],
            CommandTaskType.SupportAttack,
            objective,
            objectiveMatched: true,
            commandIndex: 1,
            frontage,
            nodeCount: nodes.Length);
        TacticalBattleLinePlan fix = TacticalBattleLinePlanner.PlanNode(
            nodes[2],
            CommandTaskType.FixEnemy,
            objective,
            objectiveMatched: true,
            commandIndex: 2,
            frontage,
            nodeCount: nodes.Length);

        AssertTrue(support.LeftFrontageTarget.HasValue, "left frontage");
        AssertTrue(support.RightFrontageTarget.HasValue, "right frontage");
        AssertTrue(support.ArtilleryLineTarget.HasValue, "artillery line");
        AssertTrue(Math.Abs(support.PrimaryTarget.X) > 150f, "support should use a real frontage lane");
        AssertTrue(Math.Abs(fix.PrimaryTarget.X) > 150f, "fix should use a real frontage lane");
        AssertTrue(Math.Sign(support.PrimaryTarget.X) != Math.Sign(fix.PrimaryTarget.X), "support and fix should anchor opposite flanks");
        AssertTrue(support.EchelonDepth > 100f, "support echelon depth");
        AssertTrue(support.ArtilleryLineTarget.Z < support.PrimaryTarget.Z, "artillery line should sit behind the assault frontage");
        AssertContains(support.Reason, "terrain-anchor", "ridge terrain reason");
        AssertContains(support.Reason, "frontage", "frontage reason");
    }

    private static void TacticalBattleLinePlannerSpreadsFormingCommandsAcrossFrontage()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("division-1", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, -30f, 0f, 0f),
            CommandNodeOperationalState.Create("division-2", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, -10f, 0f, 0f),
            CommandNodeOperationalState.Create("division-3", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 10f, 0f, 0f),
            CommandNodeOperationalState.Create("division-4", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 30f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Forming, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 500f, 0.7f, false, 0.8f, 1000f, 0f, 0.7f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 900f, nowSeconds: 100f);

        AssertEqual(4, orders.Length, "order count");
        for (int i = 0; i < orders.Length; i++)
        {
            AssertEqual(CommandTaskType.FormUp, orders[i].Task, "forming task " + i);
            AssertTrue(orders[i].PrimaryTarget.HasValue, "forming target " + i);
        }

        AssertTrue(
            DistanceSquared(orders[0].PrimaryTarget.X, orders[0].PrimaryTarget.Z, orders[1].PrimaryTarget.X, orders[1].PrimaryTarget.Z) > 6400f,
            "first two forming commands should not share an assembly lane");
        AssertTrue(
            DistanceSquared(orders[1].PrimaryTarget.X, orders[1].PrimaryTarget.Z, orders[2].PrimaryTarget.X, orders[2].PrimaryTarget.Z) > 6400f,
            "middle forming commands should not share an assembly lane");
        AssertTrue(
            DistanceSquared(orders[2].PrimaryTarget.X, orders[2].PrimaryTarget.Z, orders[3].PrimaryTarget.X, orders[3].PrimaryTarget.Z) > 6400f,
            "last two forming commands should not share an assembly lane");
        AssertTrue(
            DistanceSquared(orders[0].PrimaryTarget.X, orders[0].PrimaryTarget.Z, orders[2].PrimaryTarget.X, orders[2].PrimaryTarget.Z) > 6400f,
            "forming commands should not repeat the left assembly lane");
        AssertTrue(
            DistanceSquared(orders[1].PrimaryTarget.X, orders[1].PrimaryTarget.Z, orders[3].PrimaryTarget.X, orders[3].PrimaryTarget.Z) > 6400f,
            "forming commands should not repeat the right assembly lane");
    }

    private static void TacticalBattleLinePlannerUsesPredictedAvenueAxis()
    {
        var objective = new BattlefieldObjectiveEstimate(
            "ridge-a",
            TacticalObjectiveType.Ridge,
            900f,
            0.9f,
            true,
            0.8f,
            1000f,
            1000f,
            0.6f,
            0.2f,
            TacticalApproachAvenueEstimate.Create(
                new TacticalMapPoint(1000f, 0f),
                new TacticalMapPoint(1000f, 1000f),
                TacticalApproachAvenueSource.EntryPoint,
                0.90f,
                roadAnchored: true,
                crossingAnchored: false,
                "entrypoint-axis"));
        CommandNodeOperationalState node = CommandNodeOperationalState.Create(
            "main",
            CommandEchelonKind.DivisionLike,
            CommandNodeRole.MainEffort,
            CommandTaskType.FormUp,
            0f,
            1000f,
            90f);

        TacticalBattleLinePlan plan = TacticalBattleLinePlanner.PlanNode(
            node,
            CommandTaskType.AttackObjective,
            objective,
            objectiveMatched: true,
            commandIndex: 0,
            default(TacticalBattlefrontSnapshot),
            nodeCount: 1);

        AssertTrue(plan.PrimaryTarget.HasValue, "target");
        AssertNear(1000f, plan.PrimaryTarget.X, 0.001f, "avenue target x");
        AssertTrue(plan.PrimaryTarget.Z < objective.Z, "line should form toward predicted enemy route");
        AssertTrue(plan.FallbackTarget.HasValue, "fallback");
        AssertTrue(plan.FallbackTarget.Z > objective.Z, "fallback should sit behind objective away from predicted route");
        AssertContains(plan.Reason, "approach-avenue", "reason");
    }

    private static void TacticalBattleLinePlannerDefendsCorridorWithReserveAndArtilleryDepth()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("main", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 900f, 1300f, 180f),
            CommandNodeOperationalState.Create("support", CommandEchelonKind.DivisionLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 950f, 1320f, 180f),
            CommandNodeOperationalState.Create("fix", CommandEchelonKind.DivisionLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, 1025f, 1340f, 180f),
            CommandNodeOperationalState.Create("reserve", CommandEchelonKind.DivisionLike, CommandNodeRole.Reserve, CommandTaskType.FormUp, 1100f, 1360f, 180f)
        };
        var objective = new BattlefieldObjectiveEstimate(
            "stafford-road",
            TacticalObjectiveType.VictoryPoint,
            0f,
            0.82f,
            false,
            0.8f,
            1000f,
            1000f,
            0.25f,
            0.4f,
            TacticalApproachAvenueEstimate.Create(
                new TacticalMapPoint(1000f, 0f),
                new TacticalMapPoint(1000f, 1000f),
                TacticalApproachAvenueSource.ScheduledArrival,
                0.92f,
                roadAnchored: true,
                crossingAnchored: true,
                "road-between-rivers"));
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "stafford-road", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[] { objective });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 5000f, nowSeconds: 100f, reserveFraction: 0.25f);

        CommandDoctrineOrder main = orders[0];
        CommandDoctrineOrder reserve = orders[3];

        AssertEqual(CommandTaskType.AttackObjective, main.Task, "main task");
        AssertTrue(main.PrimaryTarget.HasValue, "main target");
        AssertTrue(main.PrimaryTarget.Z < objective.Z - 250f, "main line should block the approach before the objective");
        AssertTrue(reserve.PrimaryTarget.HasValue, "reserve target");
        AssertTrue(reserve.PrimaryTarget.Z > main.PrimaryTarget.Z, "reserve should sit behind the defended corridor");
        AssertContains(main.Reason, "approach-intercept", "main reason");
        AssertContains(main.Reason, "objective-lane", "choke width reason");
    }

    private static void TacticalBattleLinePlannerKeepsCrossingCorridorOrdersOnDefensiveBank()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("main", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 900f, 1300f, 180f),
            CommandNodeOperationalState.Create("support", CommandEchelonKind.DivisionLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 950f, 1320f, 180f),
            CommandNodeOperationalState.Create("fix", CommandEchelonKind.DivisionLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, 1025f, 1340f, 180f),
            CommandNodeOperationalState.Create("flank", CommandEchelonKind.BrigadeLike, CommandNodeRole.FlankMarch, CommandTaskType.FormUp, 1100f, 1360f, 180f),
            CommandNodeOperationalState.Create("forming", CommandEchelonKind.BrigadeLike, CommandNodeRole.Unknown, CommandTaskType.FormUp, 1125f, 1380f, 180f)
        };
        var objective = new BattlefieldObjectiveEstimate(
            "river-road",
            TacticalObjectiveType.VictoryPoint,
            0f,
            0.82f,
            false,
            0.8f,
            1000f,
            1000f,
            0.25f,
            0.4f,
            TacticalApproachAvenueEstimate.Create(
                new TacticalMapPoint(1000f, 0f),
                new TacticalMapPoint(1000f, 1000f),
                TacticalApproachAvenueSource.ScheduledArrival,
                0.92f,
                roadAnchored: true,
                crossingAnchored: true,
                "river-road-crossing"));
        TacticalDefensiveLineAnchor anchor = TacticalDefensiveLineAnchorPlanner.Plan(
            objective,
            TacticalBattlefrontGeometry.Build(nodes, objective),
            nodes.Length);
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "river-road", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[] { objective });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 5000f, nowSeconds: 100f, reserveFraction: 0.20f);

        AssertTrue(anchor.HasAnchor, "anchor");
        AssertTrue(anchor.Center.HasValue, "anchor center");
        for (int i = 0; i < orders.Length; i++)
        {
            AssertTrue(orders[i].PrimaryTarget.HasValue, "target " + i.ToString(CultureInfo.InvariantCulture));
            AssertTrue(
                orders[i].PrimaryTarget.Z > anchor.Center.Z,
                "target should stay on defensive bank " + orders[i].Task.ToString());
            AssertTrue(
                orders[i].PrimaryTarget.Z < objective.Z,
                "target should still defend before the road objective " + orders[i].Task.ToString());
            AssertContains(orders[i].Reason, "defensive-side-corridor", "reason");
        }
    }

    private static void TacticalBattleOrchestratorForwardsOperationsLedgerUpdate()
    {
        var side = new TacticalBattleOrchestrator(1, TacticalCommanderRoster.BuildFromSynthetic(Array.Empty<SyntheticCommanderInput>()));
        var army = NewArmyOrchestratorWithPlan();
        side.AttachArmy(army);
        var firstLedger = side.OperationsLedger;

        side.TickOperationsLedger(
            TacticalCommanderMode.Active,
            new[] { ObjectiveRecordFor("ridge-a", enemyStrength: 70f, friendlyAssignedStrength: 100f, status: TacticalObjectiveStatus.WeaklyHeld) },
            new StrategicBattleIntentSnapshot(0.2f, 0.3f, "theater", "campaign"),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));

        AssertEqual(TacticalCommanderMode.Active, army.CommanderMode, "forwarded mode");
        AssertEqual("ridge-a", army.CurrentOperation.PrimaryObjectiveId, "forwarded operation");
        AssertTrue(ReferenceEquals(firstLedger, side.OperationsLedger), "per-side ledger persists");

        side.TickOperationsLedger(
            TacticalCommanderMode.MonitorOnly,
            new[] { ObjectiveRecordFor("ridge-b", enemyStrength: 40f, friendlyAssignedStrength: 100f) },
            new StrategicBattleIntentSnapshot(0.1f, 0.2f, "theater", "second-campaign"),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));

        AssertTrue(ReferenceEquals(firstLedger, side.OperationsLedger), "per-side ledger reused");
        AssertEqual(TacticalCommanderMode.MonitorOnly, army.CommanderMode, "second mode");
        AssertEqual("ridge-a", army.CurrentOperation.PrimaryObjectiveId, "committed operation preserved");

        side.TickOperationsLedger(
            TacticalCommanderMode.Off,
            new[] { ObjectiveRecordFor("stale", enemyStrength: 10f, friendlyAssignedStrength: 10f) },
            new StrategicBattleIntentSnapshot(0.9f, 0.9f, "stale", "stale"),
            new ForceAvailabilitySnapshot(8000f, 0.30f),
            new PersonalityVector(0.4f, 0f, 0f, 0f, 0f));

        AssertTrue(ReferenceEquals(firstLedger, side.OperationsLedger), "off mode reuses ledger");
        AssertEqual(TacticalCommanderMode.Off, army.CommanderMode, "off mode clears mode");
        AssertEqual("objective-unknown", army.CurrentOperation.PrimaryObjectiveId, "off mode clears operation");
        AssertEqual(0, army.CurrentCommandOperations.Count, "off mode clears command operations");
    }

    private static void TacticalBattleCoordinatorSideGateBlocksPlayerSideUnlessAiVsAi()
    {
        AssertFalse(TacticalBattleCoordinator.ShouldRunTacticalCommanderForSide(0, 0, aiVsAi: false), "player side blocked");
        AssertTrue(TacticalBattleCoordinator.ShouldRunTacticalCommanderForSide(0, 0, aiVsAi: true), "ai vs ai allows player side");
        AssertTrue(TacticalBattleCoordinator.ShouldRunTacticalCommanderForSide(1, 0, aiVsAi: false), "opposing side allowed");
        AssertFalse(TacticalBattleCoordinator.ShouldRunTacticalCommanderForSide(2, 0, aiVsAi: true), "invalid alliance blocked");
        AssertFalse(TacticalBattleCoordinator.ShouldRunTacticalCommanderForSide(1, -1, aiVsAi: false), "unknown player alliance fails closed");
    }

    private static void CollapseRiskThresholdsBoundToBreakMoraleTrigger()
    {
        AssertEqual(CollapseRisk.Critical, CampaignPaceLedger.RiskFor(ownMorale: 30f * 1.10f, breakMoraleTrigger: 30f, minSurrender: 18f));
        AssertEqual(CollapseRisk.Elevated, CampaignPaceLedger.RiskFor(ownMorale: 30f * 1.40f, breakMoraleTrigger: 30f, minSurrender: 18f));
        AssertEqual(CollapseRisk.Low,      CampaignPaceLedger.RiskFor(ownMorale: 30f * 2.50f, breakMoraleTrigger: 30f, minSurrender: 18f));
    }

    // ---- TacticalBattleOrchestrator tests ----

    private static void TacticalBattleOrchestratorOwnsAllianceAndRoster()
    {
        var roster = TacticalCommanderRoster.BuildFromSynthetic(new[]
        {
            new SyntheticCommanderInput("Lee", EchelonKind.Army, 1)
        });
        var orch = new TacticalBattleOrchestrator(allianceId: 1, roster);
        AssertEqual(1, orch.AllianceId, "alliance id");
        AssertTrue(ReferenceEquals(roster, orch.Roster), "roster is same reference");
    }

    private static void TacticalBattleOrchestratorAttachArmyExposesArmy()
    {
        var roster = new TacticalCommanderRoster();
        var side = new TacticalBattleOrchestrator(allianceId: 0, roster);
        var army = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), default);
        side.AttachArmy(army);
        AssertTrue(side.Army == army, "Army property exposes attached army");
    }

    private static void TacticalBattleOrchestratorAttachArmyIdempotent()
    {
        var roster = new TacticalCommanderRoster();
        var side = new TacticalBattleOrchestrator(allianceId: 0, roster);
        var army = new ArmyOrchestrator(0, SeedCatalog.AllHistoricalAndGeneric(), default);
        side.AttachArmy(army);
        side.AttachArmy(army);
        side.AttachArmy(null);
        AssertTrue(side.Army == army, "null AttachArmy keeps the attached army");
    }

    // ---- TacticalBattleCoordinator tests ----

    private static void TacticalBattleCoordinatorStartsInactive()
    {
        TacticalBattleCoordinator.ResetForTest();
        AssertTrue(!TacticalBattleCoordinator.IsActive, "coordinator should start inactive");
        AssertTrue(TacticalBattleCoordinator.GetSideOrchestrator(0) == null, "side0 should be null before start");
        AssertTrue(TacticalBattleCoordinator.GetSideOrchestrator(1) == null, "side1 should be null before start");
    }

    private static void TacticalBattleCoordinatorActivatesOnBattleStartWithSyntheticInputs()
    {
        TacticalBattleCoordinator.ResetForTest();
        var inputs = new[]
        {
            new SyntheticCommanderInput("Lee", EchelonKind.Army, 1),
            new SyntheticCommanderInput("McClellan", EchelonKind.Army, 0)
        };
        // playerCicAllianceId = -1 means no player CIC: both sides should be active
        TacticalBattleCoordinator.OnBattleStartForTest(-1, inputs);
        AssertTrue(TacticalBattleCoordinator.IsActive, "should be active after start");
        AssertTrue(TacticalBattleCoordinator.GetSideOrchestrator(0) != null, "side0 should be non-null");
        AssertTrue(TacticalBattleCoordinator.GetSideOrchestrator(1) != null, "side1 should be non-null");
    }

    private static void TacticalBattleCoordinatorSuppressesPlayerCicSide()
    {
        TacticalBattleCoordinator.ResetForTest();
        var inputs = new[]
        {
            new SyntheticCommanderInput("Lee", EchelonKind.Army, 1),
            new SyntheticCommanderInput("McClellan", EchelonKind.Army, 0)
        };
        // playerCicAllianceId = 0: side0 is player-controlled, should be suppressed
        TacticalBattleCoordinator.OnBattleStartForTest(0, inputs);
        AssertTrue(TacticalBattleCoordinator.IsActive, "should be active even with one side suppressed");
        AssertTrue(TacticalBattleCoordinator.GetSideOrchestrator(0) == null, "side0 (player CIC) should be suppressed");
        AssertTrue(TacticalBattleCoordinator.GetSideOrchestrator(1) != null, "side1 (AI) should be active");
    }

    private static void TacticalBattleCoordinatorOnBattleEndForTestClearsState()
    {
        TacticalBattleCoordinator.ResetForTest();
        TacticalBattleCoordinator.OnBattleStartForTest(-1, new SyntheticCommanderInput[0]);
        AssertTrue(TacticalBattleCoordinator.IsActive, "should be active after start");
        TacticalBattleCoordinator.ResetForTest();
        AssertTrue(!TacticalBattleCoordinator.IsActive, "should be inactive after end");
        AssertTrue(TacticalBattleCoordinator.GetSideOrchestrator(0) == null, "side0 should be null after end");
        AssertTrue(TacticalBattleCoordinator.GetSideOrchestrator(1) == null, "side1 should be null after end");
    }

    private static void TacticalBattleCoordinatorDoubleStartIsNoOp()
    {
        TacticalBattleCoordinator.ResetForTest();
        TacticalBattleCoordinator.OnBattleStartForTest(-1, new SyntheticCommanderInput[0]);
        var originalSide1 = TacticalBattleCoordinator.GetSideOrchestrator(1);
        // Second start with different player side should be rejected (already active)
        TacticalBattleCoordinator.OnBattleStartForTest(1, new SyntheticCommanderInput[0]);
        AssertTrue(ReferenceEquals(originalSide1, TacticalBattleCoordinator.GetSideOrchestrator(1)),
            "side1 reference should be unchanged on double start");
    }

    // ---- TacticalBattleLifecycleDetector tests ----

    private static void TacticalBattleLifecycleDetectorReturnsNoneWhenNoUnitsAcrossTicks()
    {
        var detector = new TacticalBattleLifecycleDetector();
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(0), "first zero tick should be None");
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(0), "second zero tick should be None");
    }

    private static void TacticalBattleLifecycleDetectorReturnsBattleStartOnFirstUnitsTick()
    {
        var detector = new TacticalBattleLifecycleDetector();
        detector.Observe(0);
        var ev = detector.Observe(5);
        AssertEqual(BattleLifecycleEvent.BattleStart, ev, "first units tick after zero should fire BattleStart");
    }

    private static void TacticalBattleLifecycleDetectorRequiresTwoConsecutiveZeroTicksForBattleEnd()
    {
        var detector = new TacticalBattleLifecycleDetector();
        // arm the detector: start a battle
        detector.Observe(0);
        detector.Observe(3); // BattleStart
        // first zero tick: not enough for BattleEnd
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(0), "first zero after units should be None");
        // second consecutive zero tick: now fires BattleEnd
        AssertEqual(BattleLifecycleEvent.BattleEnd, detector.Observe(0), "second consecutive zero should fire BattleEnd");
    }

    private static void TacticalBattleLifecycleDetectorIgnoresTransientZeroTickBetweenUnitsTicks()
    {
        var detector = new TacticalBattleLifecycleDetector();
        // arm: start battle
        detector.Observe(0);
        AssertEqual(BattleLifecycleEvent.BattleStart, detector.Observe(3), "initial BattleStart");
        // transient zero: counter increments to 1
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(0), "transient zero should be None");
        // back to units: counter resets to 0
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(2), "return to units after transient zero is None");
        // zero again: counter at 1 — not enough
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(0), "first zero (counter=1) after reset is None");
        // second consecutive zero: counter at 2 — BattleEnd
        AssertEqual(BattleLifecycleEvent.BattleEnd, detector.Observe(0), "second consecutive zero fires BattleEnd");
    }

    private static void TacticalBattleLifecycleDetectorDoesNotFireDoubleStartOnSubsequentUnitsTicks()
    {
        var detector = new TacticalBattleLifecycleDetector();
        detector.Observe(0);
        AssertEqual(BattleLifecycleEvent.BattleStart, detector.Observe(3), "first units tick fires BattleStart");
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(5), "subsequent units tick should be None");
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(2), "third units tick should also be None");
    }

    private static void TacticalBattleLifecycleDetectorRestartsBattleAfterEnd()
    {
        var detector = new TacticalBattleLifecycleDetector();
        // prime the detector
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(0), "priming zero should be None");
        // first battle begins
        AssertEqual(BattleLifecycleEvent.BattleStart, detector.Observe(3), "first BattleStart");
        // teardown: two consecutive zero ticks end the first battle
        AssertEqual(BattleLifecycleEvent.None, detector.Observe(0), "first zero of teardown should be None");
        AssertEqual(BattleLifecycleEvent.BattleEnd, detector.Observe(0), "second consecutive zero fires BattleEnd");
        // second battle begins — verifies reset path: inBattle=false, consecutiveZeroTicks=0
        AssertEqual(BattleLifecycleEvent.BattleStart, detector.Observe(5), "second BattleStart after reset");
    }

    // ---- TacticalBattlePlan tests (O1.1) ----

    private static void TacticalBattlePlanRecordsIdPhaseMainEffortAndAge()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.DoubleEnvelopment,
            BattlePhase.Probe,
            mainEffortSector: 3,
            fixingSectors: new[] { 0, 1 },
            screeningSectors: new[] { 4 },
            reserveCommitTriggerOdds: 1.4f,
            ageSeconds: 0f,
            jitterSeed: 17);
        AssertEqual(BattlePlanId.DoubleEnvelopment, plan.PlanId, "plan id");
        AssertEqual(BattlePhase.Probe, plan.Phase, "phase");
        AssertEqual(3, plan.MainEffortSector, "main effort sector");
        AssertEqual(2, plan.FixingSectors.Length, "fixing sectors length");
        AssertEqual(0, plan.FixingSectors[0], "fixing sector 0");
        AssertEqual(1, plan.FixingSectors[1], "fixing sector 1");
        AssertEqual(1, plan.ScreeningSectors.Length, "screening sectors length");
        AssertEqual(4, plan.ScreeningSectors[0], "screening sector 0");
        AssertNear(1.4f, plan.ReserveCommitTriggerOdds, 1e-5f, "reserve trigger");
        AssertNear(0f, plan.AgeSeconds, 1e-5f, "age");
        AssertEqual(17, plan.JitterSeed, "jitter seed");
    }

    private static void TacticalBattlePlanWithPhaseAdvancesAndResetsAge()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.GenericMethodical,
            BattlePhase.Probe,
            mainEffortSector: 0,
            fixingSectors: null,
            screeningSectors: null,
            reserveCommitTriggerOdds: 1.2f,
            ageSeconds: 12.5f,
            jitterSeed: 1).WithPhase(BattlePhase.MainEffort);
        AssertEqual(BattlePhase.MainEffort, plan.Phase, "phase advanced");
        AssertNear(0f, plan.AgeSeconds, 1e-5f, "age reset");
    }

    private static void TacticalBattlePlanSanitizesNanAndInfinityFloats()
    {
        var plan = new TacticalBattlePlan(
            BattlePlanId.GenericMethodical,
            BattlePhase.Probe,
            mainEffortSector: 0,
            fixingSectors: null,
            screeningSectors: null,
            reserveCommitTriggerOdds: float.NaN,
            ageSeconds: float.PositiveInfinity,
            jitterSeed: 0);
        AssertNear(0f, plan.ReserveCommitTriggerOdds, 1e-5f, "NaN reserveOdds sanitized to 0");
        AssertNear(0f, plan.AgeSeconds, 1e-5f, "Infinity ageSeconds sanitized then clamped to 0");
        AssertEqual(0, plan.FixingSectors.Length, "null fixingSectors coalesced to empty");
        AssertEqual(0, plan.ScreeningSectors.Length, "null screeningSectors coalesced to empty");
    }

    private static void EnemyVisibleStateRecordsSectorAndContactFields()
    {
        var sectors = new[]
        {
            new EnemyVisibleSector(sectorId: 0, ownStrength: 5000f, enemyStrength: 7500f, recentFire: true),
            new EnemyVisibleSector(sectorId: 1, ownStrength: 3000f, enemyStrength: 1500f, recentFire: false)
        };
        var state = new EnemyVisibleState(
            sectors,
            enemyReserveCommitFraction: 0.4f,
            anyContactSpotted: true,
            anyContactBroken: false);

        AssertEqual(2, state.Sectors.Length, "sector count");
        AssertEqual(0, state.Sectors[0].SectorId, "sector 0 id");
        AssertNear(7500f, state.Sectors[0].EnemyStrength, 1e-5f, "sector 0 enemy strength");
        AssertTrue(state.Sectors[0].RecentFire, "sector 0 recent fire");
        AssertNear(0.4f, state.EnemyReserveCommitFraction, 1e-5f, "enemy reserve commit fraction");
        AssertTrue(state.AnyContactSpotted, "any contact spotted");
        AssertFalse(state.AnyContactBroken, "any contact broken");
    }

    private static void EnemyVisibleStateClampsAndCoercesNullSectors()
    {
        var state = new EnemyVisibleState(
            sectors: null,
            enemyReserveCommitFraction: 1.5f,
            anyContactSpotted: false,
            anyContactBroken: false);

        AssertEqual(0, state.Sectors.Length, "null sectors become empty");
        AssertNear(1.0f, state.EnemyReserveCommitFraction, 1e-5f, "reserve fraction clamps to 1");
    }

    private static void ComputeCasualtyEscalationThresholdPersonalityScale()
    {
        AssertNear(0.05f, TacticalReinforcementOpportunityDoctrine.ComputeCasualtyEscalationThreshold(0.0f),
            1e-6f, "cautious casualty threshold 5%");
        AssertNear(0.10f, TacticalReinforcementOpportunityDoctrine.ComputeCasualtyEscalationThreshold(0.5f),
            1e-6f, "mid casualty threshold 10%");
        AssertNear(0.15f, TacticalReinforcementOpportunityDoctrine.ComputeCasualtyEscalationThreshold(1.0f),
            1e-6f, "aggressive casualty threshold 15%");
    }

    private static void ComputeWithdrawalRatioThresholdPersonalityScale()
    {
        // Spot-check the personality-modulated withdrawal threshold:
        //   aggressive (1.0) → 0.25 (won't withdraw until ~4:1 odds)
        //   mid       (0.5) → 0.40 (withdraws at ~2.5:1 odds)
        //   cautious  (0.0) → 0.55 (withdraws at ~2:1 odds)
        AssertNear(0.25f, TacticalReinforcementOpportunityDoctrine.ComputeWithdrawalRatioThreshold(1.0f),
            1e-6f, "aggressive threshold");
        AssertNear(0.40f, TacticalReinforcementOpportunityDoctrine.ComputeWithdrawalRatioThreshold(0.5f),
            1e-6f, "mid threshold");
        AssertNear(0.55f, TacticalReinforcementOpportunityDoctrine.ComputeWithdrawalRatioThreshold(0.0f),
            1e-6f, "cautious threshold");
    }

    private static void TacticalBattleStateSignatureEqualsIdentical()
    {
        var a = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var b = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        AssertTrue(a.SignatureEquals(b), "identical signatures must be equal");
    }

    private static void TacticalBattleStateSignatureDiffersOnMacroAI()
    {
        var a = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var b = new TacticalBattleStateSignature(100, 12000, 11500, 3, 2, false, 98765, 0, 12); // macro AI side1 differs
        AssertTrue(!a.SignatureEquals(b), "different MacroAI must cause signature differ");
    }

    private static void TacticalBattleStateSignatureDiffersOnObjectiveHash()
    {
        var a = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var b = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 54321, 0, 12); // objective hash differs
        AssertTrue(!a.SignatureEquals(b), "different MajorObjectiveAnchorHash must cause signature differ");
    }

    private static void TacticalBattleStateSignatureDiffersOnActiveUnitCount()
    {
        var a = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var b = new TacticalBattleStateSignature(101, 12000, 11500, 3, 1, false, 98765, 0, 12); // active count differs
        AssertTrue(!a.SignatureEquals(b), "different ActiveUnitCount must cause signature differ");
    }

    private static void TacticalBattleStateSignatureEqualsIgnoresHourBucket()
    {
        var a = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var b = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 13); // only bucket differs
        AssertTrue(a.SignatureEquals(b), "hour bucket change alone must not affect signature equality (time gate separate)");
    }

    // TacticalBattleRuntimeSnapshot TDD tests (Task 4). Written first (RED: stub has no members).
    // Full pure DTO impl will make them PASS. Tests cover Empty, construction sanitization,
    // defensive list copies (per pure DTO contract), and HasData helper.

    private static void TacticalBattleRuntimeSnapshotEmptyIsValid()
    {
        var snap = TacticalBattleRuntimeSnapshot.Empty;
        AssertTrue(snap.SignatureAtBuild.SignatureEquals(default(TacticalBattleStateSignature)), "empty has default signature");
        AssertEqual(0f, snap.BuildBattleHours, "empty build hours is 0");
        AssertEqual(0, snap.Objectives.Count, "empty has no objectives");
        AssertFalse(snap.HasData, "empty has no data");
    }

    private static void TacticalBattleRuntimeSnapshotConstructionSanitizesAndStores()
    {
        var sig = new TacticalBattleStateSignature(50, 8000, 7500, 1, 2, true, 12345, 0, 5);
        var ev = new ArmyEvidence(1.2f, TerrainKind.Open, 3);
        var vis = new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0.4f, true, false);
        var objs = new ObjectiveRecord[0];
        var tree = CommandTreeSnapshot.Empty;
        var children = Array.Empty<DirectChildSnapshot>();

        var snap = new TacticalBattleRuntimeSnapshot(sig, -1.5f, ev, vis, -10f, 1.5f, -0.2f, 500f, objs, tree, children);
        AssertTrue(snap.SignatureAtBuild.SignatureEquals(sig), "stores signature");
        AssertEqual(0f, snap.BuildBattleHours, "negative hours sanitized to 0");
        AssertEqual(0f, snap.OwnMainEffortStrength, "negative main effort sanitized");
        AssertEqual(1f, snap.OwnArmyMorale, " >1 morale clamped");
        AssertEqual(0f, snap.OwnReservesCommittedFraction, "negative reserves sanitized");
        AssertEqual(500f, snap.OwnReinforcementsArriving, "positive delta stored");
    }

    private static void TacticalBattleRuntimeSnapshotCopiesObjectivesDefensively()
    {
        var list = new List<ObjectiveRecord>();
        // Note: ObjectiveRecord ctor requires valid Observation; use default for test (sanitized inside)
        var snap = new TacticalBattleRuntimeSnapshot(default(TacticalBattleStateSignature), 10f, default(ArmyEvidence),
            new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false),
            100f, 0.9f, 0.1f, 0f, list, CommandTreeSnapshot.Empty, Array.Empty<DirectChildSnapshot>());
        // mutate original
        // (list is empty, but test demonstrates copy contract even if non-empty in real use)
        AssertEqual(0, snap.Objectives.Count, "stores copy of list");
        // If list had items, mutation after ctor would not affect snap.Objectives (defensive)
    }

    private static void TacticalBattleRuntimeSnapshotCopiesDirectChildSnapshotsDefensively()
    {
        var children = new DirectChildSnapshot[0];
        var snap = new TacticalBattleRuntimeSnapshot(default(TacticalBattleStateSignature), 2f, default(ArmyEvidence),
            new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false),
            50f, 0.8f, 0.2f, 0f, Array.Empty<ObjectiveRecord>(), CommandTreeSnapshot.Empty, children);
        AssertEqual(0, snap.DirectChildSnapshots.Count, "stores copy of direct child list");
    }

    private static void TacticalBattleRuntimeSnapshotHasDataReflectsContents()
    {
        var snapEmpty = TacticalBattleRuntimeSnapshot.Empty;
        AssertFalse(snapEmpty.HasData, "empty reports no data");

        // minimal with objectives count >0 would set HasData true (ctor accepts)
        var snapWithObj = new TacticalBattleRuntimeSnapshot(default(TacticalBattleStateSignature), 1f, default(ArmyEvidence),
            new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false),
            10f, 0.5f, 0.1f, 0f, new[] { default(ObjectiveRecord) }, CommandTreeSnapshot.Empty, Array.Empty<DirectChildSnapshot>());
        AssertTrue(snapWithObj.HasData, "snapshot with objectives has data");
    }

    // ============================================================================
    // Stance mapper tests (2026-05-22, Scope B).
    // ============================================================================

    private static void StanceMapperScreenTasksMapToStance1()
    {
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceScreen,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.Screen), "Screen → 1");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceScreen,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.Probe), "Probe → 1");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceScreen,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.Scout), "Scout → 1");
    }

    private static void StanceMapperAttackTasksMapToStance3()
    {
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceAttack,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.AttackObjective), "AttackObjective → 3");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceAttack,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.SupportAttack), "SupportAttack → 3");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceAttack,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FixEnemy), "FixEnemy → 3");
    }

    private static void StanceMapperDefensiveTasksMapToStance2()
    {
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.HoldObjective), "HoldObjective → 2");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.FallBackToLine), "FallBackToLine → 2");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.GuardFlank), "GuardFlank → 2");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.Consolidate), "Consolidate → 2");
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceDefend,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.ReserveWait), "ReserveWait → 2");
    }

    private static void StanceMapperNoneTaskMapsToStance0()
    {
        AssertEqual(TacticalCommandTaskToStanceMapper.StanceNone,
            TacticalCommandTaskToStanceMapper.Map(CommandTaskType.None), "None → 0");
    }

    // ============================================================================
    // Vanilla macroai mapping tests (Stage 1 / F). Pure mapping logic between
    // Whiskey's doctrine opportunity and vanilla's per-side macroai field.
    // ============================================================================

    private static void MacroAiAttackNowFlankAtClearAdvantage()
    {
        // Aggressive commander (aggression 0.85 → threshold 1.0). Ratio 1.5
        // clearly exceeds → flank-attack.
        int? result = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.AttackNow, currentRatio: 1.5f, commanderAggression01: 0.85f);
        AssertTrue(result.HasValue, "AttackNow must return a macroai value");
        AssertEqual(TacticalVanillaMacroAiMapping.MacroAiFlankAttack, result.Value,
            "AttackNow at clear-advantage ratio + aggressive commander → flank attack (0)");
    }

    private static void MacroAiAttackNowFrontalAtMarginalRatio()
    {
        // Mid-band commander (aggression 0.5 → threshold 1.2 baseline).
        // Ratio 1.10 < 1.2 → frontal attack, not flank.
        int? result = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.AttackNow, currentRatio: 1.10f, commanderAggression01: 0.5f);
        AssertTrue(result.HasValue, "AttackNow must return a macroai value");
        AssertEqual(TacticalVanillaMacroAiMapping.MacroAiFrontalAttack, result.Value,
            "AttackNow below envelopment threshold → frontal attack (1)");
    }

    private static void MacroAiDefensiveHoldMapsToDefend()
    {
        // BR-H1 (2026-10-14): DefensiveHold is the doctrine's fall-through —
        // no opinion, vanilla's CheckGlobalAIStrategy decides.
        int? result = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.DefensiveHold, currentRatio: 0.95f, commanderAggression01: 0.5f);
        AssertTrue(!result.HasValue, "DefensiveHold returns null (vanilla decides)");
    }

    private static void MacroAiWaitAndConsolidateMapsToDefend()
    {
        // While we wait for reinforcements, we still want the line formed —
        // map to defend, not retreat.
        int? result = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.WaitAndConsolidate, currentRatio: 0.95f, commanderAggression01: 0.5f);
        AssertTrue(result.HasValue, "WaitAndConsolidate must return a macroai value");
        AssertEqual(TacticalVanillaMacroAiMapping.MacroAiDefend, result.Value,
            "WaitAndConsolidate → defensive posture (macroai = 2)");
    }

    private static void MacroAiWithdrawalMapsToRetreat()
    {
        int? result = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.WithdrawalToFightLater, currentRatio: 0.60f, commanderAggression01: 0.5f);
        AssertTrue(result.HasValue, "WithdrawalToFightLater must return a macroai value");
        AssertEqual(TacticalVanillaMacroAiMapping.MacroAiDefend, result.Value,
            "WithdrawalToFightLater → defend (2); macroai 3 stays vanilla's (startoffallback 6355/6362)");
    }

    private static void MacroAiNoOpportunityReturnsNull()
    {
        // NoOpportunity / pre-doctrine state must NOT overwrite vanilla.
        int? result = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.NoOpportunity, currentRatio: 1.0f, commanderAggression01: 0.5f);
        AssertTrue(!result.HasValue,
            "NoOpportunity must return null so vanilla's own hysteresis-driven decision runs");
    }

    private static void MacroAiAggressiveCommanderEnvelopsAtParity()
    {
        // Cross-check: at aggression 0.7 (Hood/Jackson), envelopment threshold
        // drops to 1.0× per ArmyOrchestrator.EffectiveEnvelopmentMinRatio.
        // Ratio 1.0 at aggression 0.7 → flank attack.
        int? result = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.AttackNow, currentRatio: 1.0f, commanderAggression01: 0.7f);
        AssertTrue(result.HasValue, "must return a value");
        AssertEqual(TacticalVanillaMacroAiMapping.MacroAiFlankAttack, result.Value,
            "Aggressive commander envelops at parity (matches EffectiveEnvelopmentMinRatio)");
    }

    private static void MacroAiCautiousCommanderRequiresClearMarginToFlank()
    {
        // Cautious commander (aggression 0.2): threshold 1.4. Ratio 1.3 → frontal,
        // not flank.
        int? result = TacticalVanillaMacroAiMapping.MapOpportunityToMacroAi(
            ReinforcementOpportunity.AttackNow, currentRatio: 1.3f, commanderAggression01: 0.2f);
        AssertTrue(result.HasValue, "must return a value");
        AssertEqual(TacticalVanillaMacroAiMapping.MacroAiFrontalAttack, result.Value,
            "Cautious commander requires clear-margin (1.4×) before enveloping");
    }

    // ---- Stance reconciliation (2026-05-25 / A) ----
    // The force-aware doctrine demotes an objective-attacker to Defender for
    // playbook selection so an outnumbered side can't be forced into an
    // offensive playbook by GetBattleType alone.

    private static void StanceReconcilerDemotesAttackerOnDefensiveDoctrine()
    {
        AssertEqual(BattleStance.Defender,
            TacticalStanceReconciler.ResolveEffectiveStance(
                BattleStance.Attacker, ReinforcementOpportunity.DelayingDefense),
            "Attacker + DelayingDefense → demote to Defender");
        AssertEqual(BattleStance.Attacker,
            TacticalStanceReconciler.ResolveEffectiveStance(
                BattleStance.Attacker, ReinforcementOpportunity.DefensiveHold),
            "Attacker + DefensiveHold (no opinion, BR-H1) → stays Attacker");
        AssertEqual(BattleStance.Defender,
            TacticalStanceReconciler.ResolveEffectiveStance(
                BattleStance.Attacker, ReinforcementOpportunity.WaitAndConsolidate),
            "Attacker + WaitAndConsolidate → demote to Defender");
    }

    private static void StanceReconcilerDemotesMeetingOnDefensiveDoctrine()
    {
        AssertEqual(BattleStance.Defender,
            TacticalStanceReconciler.ResolveEffectiveStance(
                BattleStance.Meeting, ReinforcementOpportunity.DelayingDefense),
            "Meeting + DelayingDefense → demote to Defender");
    }

    private static void StanceReconcilerPreservesAttackerOnAttackNow()
    {
        AssertEqual(BattleStance.Attacker,
            TacticalStanceReconciler.ResolveEffectiveStance(
                BattleStance.Attacker, ReinforcementOpportunity.AttackNow),
            "Attacker + AttackNow → stays Attacker (offensive playbooks allowed)");
    }

    private static void StanceReconcilerPreservesAttackerOnNoOpportunity()
    {
        AssertEqual(BattleStance.Attacker,
            TacticalStanceReconciler.ResolveEffectiveStance(
                BattleStance.Attacker, ReinforcementOpportunity.NoOpportunity),
            "Attacker + NoOpportunity → stays Attacker (neutral outcome, no demotion)");
    }

    private static void StanceReconcilerLeavesDefenderUnchanged()
    {
        AssertEqual(BattleStance.Defender,
            TacticalStanceReconciler.ResolveEffectiveStance(
                BattleStance.Defender, ReinforcementOpportunity.AttackNow),
            "Defender stays Defender even on AttackNow (no offensive promotion)");
        AssertEqual(BattleStance.Defender,
            TacticalStanceReconciler.ResolveEffectiveStance(
                BattleStance.Defender, ReinforcementOpportunity.DelayingDefense),
            "Defender + DelayingDefense → still Defender");
    }

    private static void StanceReconcilerDemotesAttackerOnWithdrawal()
    {
        AssertEqual(BattleStance.Defender,
            TacticalStanceReconciler.ResolveEffectiveStance(
                BattleStance.Attacker, ReinforcementOpportunity.WithdrawalToFightLater),
            "Attacker + WithdrawalToFightLater → demote to Defender (don't attack while withdrawing)");
    }
}
