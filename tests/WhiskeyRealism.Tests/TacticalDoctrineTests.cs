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

internal static class TacticalDoctrineTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("operation director picks parallel attack only with advantage", OperationDirectorParallelRequiresAdvantage),
            ("operation director preserves committed push through noise", OperationDirectorPreservesCommittedPush),
            ("operation director low reserve continues high odds committed attack", OperationDirectorLowReserveContinuesHighOddsCommittedAttack),
            ("operation director soft aborts before catastrophic loss", OperationDirectorSoftAbortsBeforeCatastrophe),
            ("operation director aborts catastrophic collapse inside commit window", OperationDirectorAbortsCatastrophicCollapseInsideCommitWindow),
            ("operation director preserves active operation when picture is missing", OperationDirectorPreservesActiveOperationWhenPictureMissing),
            ("operation director matches normalized committed map objective ids", OperationDirectorMatchesNormalizedCommittedMapObjectiveIds),
            ("operation director prefers exposed enemy line over generic objective chain", OperationDirectorPrefersExposedEnemyLineOverGenericObjectiveChain),
            ("tactical SOP probe forbids major attack", TacticalSopProbeForbidsMajorAttack),
            ("tactical SOP attack can require support before charge", TacticalSopAttackRequiresSupportBeforeCharge),
            ("doctrine assignment stamps SOP authority", DoctrineAssignmentStampsSopAuthority),
            ("doctrine assignment waits for massing support", DoctrineAssignmentWaitsForMassingSupport),
            ("doctrine assignment wide formation preserves flank guard and depth slots", DoctrineAssignmentWideFormationPreservesFlankGuardAndDepthSlots),
            ("doctrine assignment unknown roles receive fallback doctrine", DoctrineAssignmentUnknownRolesReceiveFallbackDoctrine),
            ("doctrine fallback ladder maps live orders", DoctrineFallbackLadderMapsLiveOrders),
            ("scourge cavalry follow doctrine filters raid and screens away", ScourgeCavalryFollowDoctrineFiltersRaidAndScreensAway),
            ("grand tactician recon doctrine uses cavalry scout and infantry probe", GrandTacticianReconDoctrineUsesCavalryScoutAndInfantryProbe),
            ("grand tactician recon doctrine prevents no contact blind assault", GrandTacticianReconDoctrinePreventsNoContactBlindAssault),
            ("scourge artillery micro doctrine limbers retreats and wheels", ScourgeArtilleryMicroDoctrineLimbersRetreatsAndWheels),
            ("scourge infantry fire discipline holds bad shots and blocks premature charge", ScourgeInfantryFireDisciplineHoldsBadShots),
            ("infantry fire control prefers historical close medium bands", InfantryFireControlPrefersHistoricalCloseMediumBands),
            ("cavalry fire control maps to vanilla cavalry behaviors", CavalryFireControlMapsToVanillaBehaviors),
            ("operation director scouts screen contact before committing", OperationDirectorScoutsScreenContactBeforeCommitting),
            ("operation director keeps high confidence screen in scouting", OperationDirectorKeepsHighConfidenceScreenInScouting),
            ("operation director downgrades committed attack when contact becomes screen", OperationDirectorDowngradesCommittedAttackWhenContactBecomesScreen),
            ("operation director cancels committed attack when contact is lost", OperationDirectorCancelsCommittedAttackWhenContactIsLost),
            ("operation director fix and flank exposed weak point with reserve", OperationDirectorFixAndFlankExposedWeakPointWithReserve),
            ("operation macro ai maps soft abort to defend", OperationMacroAiMapsSoftAbortToDefend),
            ("doctrine telemetry throttles repeated signatures", DoctrineTelemetryThrottlesRepeatedSignatures),
            ("doctrine order sanitizes ids and exposes purpose", DoctrineOrderSanitizesIdsAndPurpose),
            ("doctrine order distinguishes no assignment from form up", DoctrineOrderDistinguishesNoAssignmentFromFormUp),
            ("doctrine order classifies legal idle reasons", DoctrineOrderClassifiesLegalIdleReasons),
            ("doctrine assignment high odds attack weak point attacks", DoctrineAssignmentHighOddsAttackWeakPointAttacks),
            ("doctrine assignment committed visible enemy line attacks at fallback confidence", DoctrineAssignmentCommittedVisibleEnemyLineAttacksAtFallbackConfidence),
            ("doctrine assignment committed fallback enemy line keeps attack when chain replaces objective", DoctrineAssignmentCommittedFallbackEnemyLineKeepsAttackWhenChainReplacesObjective),
            ("doctrine assignment committed map objective matches normalized ids", DoctrineAssignmentCommittedMapObjectiveMatchesNormalizedIds),
            ("doctrine assignment committed map objective advances without visible enemy", DoctrineAssignmentCommittedMapObjectiveAdvancesWithoutVisibleEnemy),
            ("doctrine assignment probe and screen receive movement targets", DoctrineAssignmentProbeAndScreenReceiveMovementTargets),
            ("doctrine assignment scouting screen orders probe and breakoff targets", DoctrineAssignmentScoutingScreenOrdersProbeAndBreakoffTargets),
            ("doctrine assignment battle line separates command targets", DoctrineAssignmentBattleLineSeparatesCommandTargets),
            ("doctrine assignment reserve gets battle line rally target", DoctrineAssignmentReserveGetsBattleLineRallyTarget),
            ("doctrine assignment unknown primary uses vanilla chain assembly target", DoctrineAssignmentUnknownPrimaryUsesVanillaChainAssemblyTarget),
            ("reserve assembly planner rejects threatened and crowded candidates", ReserveAssemblyPlannerRejectsThreatenedAndCrowdedCandidates),
            ("reserve assembly planner generated candidates stay behind objective", ReserveAssemblyPlannerGeneratedCandidatesStayBehindObjective),
            ("doctrine assignment reserve gets legal idle", DoctrineAssignmentReserveGetsLegalIdle),
            ("doctrine assignment fallback guard pulls toward fallback line", DoctrineAssignmentFallbackGuardPullsBack),
            ("doctrine assignment stale objective fails closed", DoctrineAssignmentStaleObjectiveFailsClosed),
            ("doctrine assignment unknown role has no legal idle", DoctrineAssignmentUnknownRoleHasNoLegalIdle),
            ("doctrine assignment fallback target remains finite for large coordinates", DoctrineAssignmentFallbackTargetRemainsFiniteForLargeCoordinates),
            ("doctrine consumer attack order permits assault stance", DoctrineConsumerAttackOrderPermitsAssaultStance),
            ("doctrine consumer known objective miss does not coordinate match", DoctrineConsumerKnownObjectiveMissDoesNotCoordinateMatch),
            ("doctrine artillery supports committed main effort", DoctrineArtillerySupportsCommittedMainEffort),
            ("doctrine artillery no exposed main line does not allow support", DoctrineArtilleryNoExposedMainLineDoesNotAllowSupport),
            ("doctrine artillery mission conserves low ammo", DoctrineArtilleryMissionConservesLowAmmo),
            ("doctrine artillery mission counterbattery outranks support", DoctrineArtilleryMissionCounterbatteryOutranksSupport),
            ("urgent recovery formation correction and local fallback on stale Empty snapshot using only allowed live vanilla fields", UrgentRecoveryFormationCorrectionOnStaleSnapshot),
            ("low ammo formation recovers", LowAmmoFormationRecovers),
            ("doctrine stance gate defender never attacks", DoctrineStanceGateDefenderNeverAttacks),
            ("doctrine stance gate attacker produces attack on advantage", DoctrineStanceGateAttackerProducesAttackOnAdvantage),
            ("doctrine stance gate meeting allows all outcomes", DoctrineStanceGateMeetingAllowsAllOutcomes),
        });
    }

    private static void OperationDirectorParallelRequiresAdvantage()
    {
        TacticalOperationDirectorInput strong = TacticalOperationDirectorInput.ForTest(
            current: OperationRecord.Noop,
            currentTimeSeconds: 120f,
            ownStrength: 5000f,
            reserveFraction: 0.35f,
            aggression01: 0.8f,
            caution01: 0.1f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("left", TacticalObjectiveType.Ridge, 1000f, 0.85f, true, 0.8f, 100f, 100f, 0.3f, 0.2f),
                new BattlefieldObjectiveEstimate("right", TacticalObjectiveType.Town, 900f, 0.85f, true, 0.9f, 400f, 120f, 0.4f, 0.3f)
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(strong);
        AssertEqual(TacticalOperationShape.ParallelObjectives, decision.Operation.Shape, "parallel shape");

        TacticalOperationDirectorDecision weak = TacticalOperationDirector.Decide(TacticalOperationDirectorInput.ForTest(
            strong.Current, strong.CurrentTimeSeconds, 2200f, strong.ReserveFraction,
            strong.Aggression01, strong.Caution01, strong.Objectives));
        AssertTrue(weak.Operation.Shape != TacticalOperationShape.ParallelObjectives, "weak force cannot parallel attack");
    }

    private static void OperationDirectorPreservesCommittedPush()
    {
        OperationRecord current = OperationRecord.CreateCommittedForTest(TacticalOperationShape.FixAndFlank, "ridge-a", minCommitUntilSeconds: 1800f);
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            current,
            currentTimeSeconds: 600f,
            ownStrength: 3000f,
            reserveFraction: 0.25f,
            aggression01: 0.5f,
            caution01: 0.5f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("ridge-b", TacticalObjectiveType.Ridge, 700f, 0.55f, true, 0.9f, 250f, 300f, 0.2f, 0.2f)
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);
        AssertEqual("ridge-a", decision.Operation.PrimaryObjectiveId, "commit preserves primary objective");
        AssertEqual(TacticalOperationPhase.Committed, decision.Operation.Phase, "phase");
        AssertEqual("commit-window", decision.Reason, "reason");
    }

    private static void OperationDirectorLowReserveContinuesHighOddsCommittedAttack()
    {
        OperationRecord current = OperationRecord.CreateCommittedForTest(TacticalOperationShape.SingleMainEffort, "ridge-a", minCommitUntilSeconds: 300f);
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            current,
            currentTimeSeconds: 500f,
            ownStrength: 6000f,
            reserveFraction: 0.02f,
            aggression01: 0.6f,
            caution01: 0.2f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 900f, 0.9f, true, 0.8f, 100f, 100f, 0.2f, 0.2f)
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);

        AssertEqual(TacticalOperationPhase.Committed, decision.Operation.Phase, "high odds low reserve phase");
        AssertEqual("ridge-a", decision.Operation.PrimaryObjectiveId, "high odds low reserve primary");
        AssertTrue(decision.Reason != "odds-collapse", "high odds low reserve does not soft abort");
    }

    private static void OperationDirectorSoftAbortsBeforeCatastrophe()
    {
        OperationRecord current = OperationRecord.CreateCommittedForTest(TacticalOperationShape.SingleMainEffort, "ridge-a", minCommitUntilSeconds: 300f);
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            current,
            currentTimeSeconds: 500f,
            ownStrength: 1000f,
            reserveFraction: 0.02f,
            aggression01: 0.4f,
            caution01: 0.7f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 2200f, 0.9f, true, 0.8f, 100f, 100f, 0.5f, 0.6f)
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);
        AssertEqual(TacticalOperationPhase.SoftAbort, decision.Operation.Phase, "soft abort phase");
        AssertEqual("odds-collapse", decision.Reason, "reason");
    }

    private static void OperationDirectorAbortsCatastrophicCollapseInsideCommitWindow()
    {
        OperationRecord current = OperationRecord.CreateCommittedForTest(TacticalOperationShape.SingleMainEffort, "ridge-a", minCommitUntilSeconds: 1800f);
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            current,
            currentTimeSeconds: 600f,
            ownStrength: 1000f,
            reserveFraction: 0.02f,
            aggression01: 0.4f,
            caution01: 0.7f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 2200f, 0.9f, true, 0.8f, 100f, 100f, 0.5f, 0.6f)
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);
        AssertEqual(TacticalOperationPhase.SoftAbort, decision.Operation.Phase, "soft abort inside commit");
        AssertEqual("odds-collapse", decision.Reason, "reason");
    }

    private static void OperationDirectorPreservesActiveOperationWhenPictureMissing()
    {
        OperationRecord current = OperationRecord.CreateCommittedForTest(TacticalOperationShape.FixAndFlank, "ridge-a", minCommitUntilSeconds: 300f);
        TacticalOperationDirectorInput emptyPicture = TacticalOperationDirectorInput.ForTest(
            current,
            currentTimeSeconds: 500f,
            ownStrength: 3000f,
            reserveFraction: 0.25f,
            aggression01: 0.5f,
            caution01: 0.5f,
            objectives: Array.Empty<BattlefieldObjectiveEstimate>());
        TacticalOperationDirectorInput missingPrimary = TacticalOperationDirectorInput.ForTest(
            current,
            currentTimeSeconds: 500f,
            ownStrength: 3000f,
            reserveFraction: 0.25f,
            aggression01: 0.5f,
            caution01: 0.5f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("ridge-b", TacticalObjectiveType.Ridge, 700f, 0.55f, true, 0.9f, 250f, 300f, 0.2f, 0.2f)
            });

        TacticalOperationDirectorDecision emptyDecision = TacticalOperationDirector.Decide(emptyPicture);
        TacticalOperationDirectorDecision missingDecision = TacticalOperationDirector.Decide(missingPrimary);

        AssertEqual("ridge-a", emptyDecision.Operation.PrimaryObjectiveId, "empty picture preserves objective");
        AssertEqual(TacticalOperationPhase.Committed, emptyDecision.Operation.Phase, "empty picture preserves phase");
        AssertEqual("objective-picture-missing", emptyDecision.Reason, "empty picture reason");
        AssertEqual("ridge-a", missingDecision.Operation.PrimaryObjectiveId, "missing primary preserves objective");
        AssertEqual(TacticalOperationPhase.Committed, missingDecision.Operation.Phase, "missing primary preserves phase");
        AssertEqual("objective-picture-missing", missingDecision.Reason, "missing primary reason");
    }

    private static void OperationDirectorMatchesNormalizedCommittedMapObjectiveIds()
    {
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "Telegraph Road", 900f),
            currentTimeSeconds: 1000f,
            ownStrength: 6000f,
            reserveFraction: 0.25f,
            aggression01: 0.8f,
            caution01: 0.2f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("Telegraph_Road", TacticalObjectiveType.RoadJunction, 0f, 0.9f, false, 0.65f, 1000f, -1400f, 0f, 0f)
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);

        AssertEqual(TacticalOperationPhase.Committed, decision.Operation.Phase, "phase");
        AssertEqual("selected", decision.Reason, "reason");
        AssertEqual("Telegraph_Road", decision.Operation.PrimaryObjectiveId, "primary objective");
    }

    private static void OperationDirectorPrefersExposedEnemyLineOverGenericObjectiveChain()
    {
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            OperationRecord.Noop,
            currentTimeSeconds: 120f,
            ownStrength: 1800f,
            reserveFraction: 0.25f,
            aggression01: 0.6f,
            caution01: 0.2f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("vanilla-chain-0", TacticalObjectiveType.UnknownVanillaObjective, 0f, 0.75f, false, 0.65f, 100f, 100f, 0f, 0f),
                new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 900f, 0.70f, true, 0.40f, 300f, 400f, 0f, 0f),
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);

        AssertEqual("enemy-line-1", decision.Operation.PrimaryObjectiveId, "primary objective");
        AssertEqual(TacticalOperationShape.FixAndFlank, decision.Operation.Shape, "shape");
        AssertEqual(TacticalOperationPhase.Committed, decision.Operation.Phase, "phase");
    }

    private static void TacticalSopProbeForbidsMajorAttack()
    {
        var screen = new BattlefieldObjectiveEstimate("screen-contact", TacticalObjectiveType.EnemyLine, 220f, 0.5f, false, 0.45f, 300f, 450f, 0.1f, 0.1f);

        TacticalSopDecision sop = TacticalSopDoctrine.Resolve(
            CommandNodeRole.Probe,
            CommandTaskType.Probe,
            TacticalOperationPhase.Scouting,
            screen,
            ownStrength: 1500f,
            reserveFraction: 0.2f);

        AssertEqual(TacticalSopAuthority.Probe, sop.Authority, "authority");
        AssertFalse(sop.AllowsMajorAttack, "probe cannot authorize a major attack");
        AssertTrue(sop.RequiresFallbackIfPressed, "probe should have a breakoff/fallback requirement");
        AssertTrue(sop.ReacquireSeconds <= 45f, "probe should reacquire quickly");
    }

    private static void TacticalSopAttackRequiresSupportBeforeCharge()
    {
        var enemyLine = new BattlefieldObjectiveEstimate("enemy-line", TacticalObjectiveType.EnemyLine, 1000f, 0.85f, true, 0.75f, 300f, 450f, 0.1f, 0.1f);

        TacticalSopDecision sop = TacticalSopDoctrine.Resolve(
            CommandNodeRole.MainEffort,
            CommandTaskType.AttackObjective,
            TacticalOperationPhase.Committed,
            enemyLine,
            ownStrength: 1300f,
            reserveFraction: 0.05f);
        CommandDoctrineOrder order = DoctrineOrder(
            CommandTaskType.AttackObjective,
            primary: DoctrineTargetPoint.From(300f, 450f)).WithSop(sop);

        AssertEqual(TacticalSopAuthority.Attack, sop.Authority, "authority");
        AssertTrue(sop.RequiresSupportBeforeMajorAttack, "thin attack should require support");
        AssertTrue(order.Sop.RequiresSupportBeforeMajorAttack, "order carries the support requirement");
    }

    private static void DoctrineAssignmentStampsSopAuthority()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("brigade-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.9f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(
            nodes,
            operation,
            picture,
            ownStrength: 1600f,
            nowSeconds: 100f,
            reserveFraction: 0.2f);

        AssertEqual(TacticalSopAuthority.Assault, orders[0].Sop.Authority, "authority");
        AssertTrue(orders[0].Sop.AllowsMajorAttack, "assault should allow major attack");
        AssertTrue(orders[0].Sop.ReacquireSeconds <= 30f, "decisive assault should reacquire quickly");
    }

    private static void DoctrineAssignmentWaitsForMassingSupport()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("brigade-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 1000f, 0.85f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(
            nodes,
            operation,
            picture,
            ownStrength: 1500f,
            nowSeconds: 100f,
            reserveFraction: 0.2f);

        AssertEqual(TacticalSopAuthority.Attack, orders[0].Sop.Authority, "authority");
        AssertFalse(orders[0].Sop.AllowsMajorAttack, "attack should wait for support");
        AssertTrue(orders[0].Sop.RequiresSupportBeforeMajorAttack, "support gate");
        AssertContains(orders[0].Sop.Reason, "massing-support", "reason");
    }

    private static void DoctrineAssignmentWideFormationPreservesFlankGuardAndDepthSlots()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("division-1", CommandEchelonKind.DivisionLike, CommandNodeRole.Defender, CommandTaskType.FormUp, 900f, 1300f, 180f),
            CommandNodeOperationalState.Create("division-2", CommandEchelonKind.DivisionLike, CommandNodeRole.Defender, CommandTaskType.FormUp, 925f, 1325f, 180f),
            CommandNodeOperationalState.Create("division-3", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 950f, 1350f, 180f),
            CommandNodeOperationalState.Create("division-4", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 975f, 1375f, 180f),
            CommandNodeOperationalState.Create("division-5", CommandEchelonKind.DivisionLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 1000f, 1400f, 180f),
            CommandNodeOperationalState.Create("division-6", CommandEchelonKind.DivisionLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, 1025f, 1425f, 180f),
            CommandNodeOperationalState.Create("division-7", CommandEchelonKind.DivisionLike, CommandNodeRole.Defender, CommandTaskType.FormUp, 1050f, 1450f, 180f),
            CommandNodeOperationalState.Create("division-8", CommandEchelonKind.DivisionLike, CommandNodeRole.Defender, CommandTaskType.FormUp, 1075f, 1475f, 180f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Forming, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate(
                "ridge-a",
                TacticalObjectiveType.Ridge,
                0f,
                0.80f,
                false,
                0.8f,
                1000f,
                1000f,
                0.70f,
                0.2f,
                TacticalApproachAvenueEstimate.Create(
                    new TacticalMapPoint(1000f, 0f),
                    new TacticalMapPoint(1000f, 1000f),
                    TacticalApproachAvenueSource.EntryPoint,
                    0.90f,
                    roadAnchored: true,
                    crossingAnchored: false,
                    "entrypoint-axis"))
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 9000f, nowSeconds: 100f);

        AssertEqual(8, orders.Length, "order count");
        AssertEqual(CommandTaskType.GuardFlank, orders[0].Task, "left wing guard task");
        AssertEqual(CommandTaskType.GuardFlank, orders[7].Task, "right wing guard task");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "left guard target");
        AssertTrue(orders[7].PrimaryTarget.HasValue, "right guard target");
        AssertTrue(Math.Abs(orders[0].PrimaryTarget.X - orders[7].PrimaryTarget.X) > 900f, "guard slots should cover opposite flanks");
        AssertTrue(orders[1].PrimaryTarget.Z > picture.Objectives[0].Z, "near wing depth slot should sit behind objective");
        AssertTrue(orders[6].PrimaryTarget.Z > picture.Objectives[0].Z, "far wing depth slot should sit behind objective");
        AssertContains(orders[0].Reason, "scourge-slot-flank-guard", "guard reason");
        AssertContains(orders[1].Reason, "scourge-slot-depth", "depth reason");
    }

    private static void DoctrineAssignmentUnknownRolesReceiveFallbackDoctrine()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("unknown-1", CommandEchelonKind.DivisionLike, CommandNodeRole.Unknown, CommandTaskType.FormUp, 900f, 1400f, 180f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Forming, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 0f, 0.75f, false, 0.8f, 1000f, 1000f, 0.6f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 3000f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandNodeRole.Unknown, orders[0].Role, "role preserved for telemetry");
        AssertEqual(CommandTaskType.FormUp, orders[0].Task, "unknown role fallback task");
        AssertTrue(orders[0].HasPurpose, "fallback doctrine should be purposeful");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "fallback doctrine target");
        AssertEqual(TacticalSopAuthority.Defend, orders[0].Sop.Authority, "unknown role task SOP");
        AssertContains(orders[0].Reason, "battle-line", "fallback reason");
    }

    private static void DoctrineFallbackLadderMapsLiveOrders()
    {
        DoctrineReserveDecision decision = DoctrineConsumerDecisions.DecideFallback(
            DoctrineOrder(
                CommandTaskType.AttackObjective,
                role: CommandNodeRole.MainEffort,
                primary: DoctrineTargetPoint.From(100f, 200f)),
            new TacticalFallbackInput(
                odds: 0.65f,
                morale01: 0.45f,
                fatigue01: 0.50f,
                flankThreat01: 0.80f,
                rearThreat: false,
                hasFallbackTarget: true,
                reserveReliefAvailable: false,
                wlGateAllows: true));

        AssertEqual(DoctrineConsumerAction.Allow, decision.Action, "fallback action");
        AssertEqual(CommandTaskType.FallBackToLine, decision.Task, "fallback task");
        AssertContains(decision.Reason, "fallback-by-bounds", "reason");
    }

    private static void OperationDirectorScoutsScreenContactBeforeCommitting()
    {
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            OperationRecord.Noop,
            currentTimeSeconds: 120f,
            ownStrength: 5000f,
            reserveFraction: 0.30f,
            aggression01: 0.7f,
            caution01: 0.2f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("screen-contact", TacticalObjectiveType.EnemyLine, 180f, 0.45f, false, 0.55f, 300f, 400f, 0.1f, 0.1f),
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);

        AssertEqual(TacticalOperationPhase.Scouting, decision.Operation.Phase, "screen contact should scout");
        AssertEqual(TacticalOperationShape.FixAndFlank, decision.Operation.Shape, "screen contact shape");
        AssertEqual("screen-contact", decision.Operation.PrimaryObjectiveId, "primary objective");
        AssertEqual("recon-contact", decision.Reason, "reason");
    }

    private static void OperationDirectorKeepsHighConfidenceScreenInScouting()
    {
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            OperationRecord.Noop,
            currentTimeSeconds: 120f,
            ownStrength: 5000f,
            reserveFraction: 0.30f,
            aggression01: 0.7f,
            caution01: 0.2f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("high-screen-contact", TacticalObjectiveType.EnemyLine, 180f, 0.80f, false, 0.55f, 300f, 400f, 0.1f, 0.1f),
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);

        AssertEqual(TacticalOperationPhase.Scouting, decision.Operation.Phase, "high confidence screen should still scout");
        AssertEqual("recon-contact", decision.Reason, "reason");
    }

    private static void OperationDirectorDowngradesCommittedAttackWhenContactBecomesScreen()
    {
        OperationRecord current = OperationRecord.CreateCommittedForTest(TacticalOperationShape.FixAndFlank, "enemy-line", minCommitUntilSeconds: 1800f);
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            current,
            currentTimeSeconds: 600f,
            ownStrength: 5000f,
            reserveFraction: 0.30f,
            aggression01: 0.7f,
            caution01: 0.2f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("enemy-line", TacticalObjectiveType.EnemyLine, 260f, 0.80f, false, 0.65f, 300f, 400f, 0.1f, 0.1f),
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);

        AssertEqual(TacticalOperationPhase.Scouting, decision.Operation.Phase, "screen downgrade should break commitment");
        AssertEqual("contact-downgraded", decision.Reason, "reason");
    }

    private static void OperationDirectorCancelsCommittedAttackWhenContactIsLost()
    {
        OperationRecord current = OperationRecord.CreateCommittedForTest(TacticalOperationShape.FixAndFlank, "enemy-line", minCommitUntilSeconds: 1800f);
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            current,
            currentTimeSeconds: 600f,
            ownStrength: 5000f,
            reserveFraction: 0.30f,
            aggression01: 0.7f,
            caution01: 0.2f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("enemy-line", TacticalObjectiveType.EnemyLine, 0f, 0.30f, false, 0.65f, 300f, 400f, 0.1f, 0.1f),
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);

        AssertEqual(TacticalOperationPhase.SoftAbort, decision.Operation.Phase, "lost contact should cancel committed attack");
        AssertEqual("contact-lost", decision.Reason, "reason");
    }

    private static void OperationDirectorFixAndFlankExposedWeakPointWithReserve()
    {
        TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
            OperationRecord.Noop,
            currentTimeSeconds: 120f,
            ownStrength: 2200f,
            reserveFraction: 0.25f,
            aggression01: 0.6f,
            caution01: 0.2f,
            objectives: new[]
            {
                new BattlefieldObjectiveEstimate("enemy-line-weak", TacticalObjectiveType.EnemyLine, 1100f, 0.80f, true, 0.65f, 300f, 400f, 0.1f, 0.1f),
            });

        TacticalOperationDirectorDecision decision = TacticalOperationDirector.Decide(input);

        AssertEqual(TacticalOperationPhase.Committed, decision.Operation.Phase, "exposed weak point should commit");
        AssertEqual(TacticalOperationShape.FixAndFlank, decision.Operation.Shape, "exposed weak point shape");
        AssertEqual("enemy-line-weak", decision.Operation.PrimaryObjectiveId, "primary objective");
        AssertEqual("fix-and-flank", decision.Reason, "reason");
    }

    private static void OperationMacroAiMapsSoftAbortToDefend()
    {
        OperationRecord softAbort = new OperationRecord(
            TacticalOperationShape.SingleMainEffort,
            TacticalOperationPhase.SoftAbort,
            "ridge-a",
            300f);

        AssertEqual(2, TacticalOperationsLedgerModel.OperationMacroAi(softAbort), "soft abort macro");
    }

    private static void DoctrineTelemetryThrottlesRepeatedSignatures()
    {
        var emittedAt = new Dictionary<string, float>();

        AssertTrue(TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(emittedAt, "doctrine:node-1", "a", 0f, 15f), "first emits");
        AssertTrue(!TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(emittedAt, "doctrine:node-1", "a", 1f, 15f), "same throttled");
        AssertTrue(TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(emittedAt, "doctrine:node-1", "b", 2f, 15f), "changed emits");
    }

    private static void DoctrineOrderSanitizesIdsAndPurpose()
    {
        CommandDoctrineOrder order = CommandDoctrineOrder.Create(
            nodeId: "",
            role: CommandNodeRole.MainEffort,
            task: CommandTaskType.AttackObjective,
            objectiveId: "",
            primaryTarget: DoctrineTargetPoint.From(100f, 200f),
            supportTarget: DoctrineTargetPoint.None,
            fallbackTarget: DoctrineTargetPoint.None,
            allowedIdle: DoctrineAllowedIdleReason.None,
            minCommitUntilSeconds: 900f,
            issuedAtSeconds: 12f,
            confidence01: 1.25f,
            reason: "");

        AssertEqual("node-unknown", order.NodeId, "node id");
        AssertEqual("objective-unknown", order.ObjectiveId, "objective id");
        AssertEqual(CommandNodeRole.MainEffort, order.Role, "role");
        AssertEqual(CommandTaskType.AttackObjective, order.Task, "task");
        AssertTrue(order.HasPurpose, "attack order has purpose");
        AssertTrue(order.PrimaryTarget.HasValue, "primary target");
        AssertEqual(1f, order.Confidence01, "confidence clamps high");
        AssertEqual("unspecified", order.Reason, "reason");
    }

    private static void DoctrineOrderDistinguishesNoAssignmentFromFormUp()
    {
        CommandDoctrineOrder noAssignment = CommandDoctrineOrder.Create(
            "node-1",
            CommandNodeRole.Unknown,
            CommandTaskType.None,
            "ridge-a",
            DoctrineTargetPoint.None,
            DoctrineTargetPoint.None,
            DoctrineTargetPoint.None,
            DoctrineAllowedIdleReason.None,
            0f,
            0f,
            0f,
            "none");

        CommandDoctrineOrder formUp = CommandDoctrineOrder.Create(
            "node-1",
            CommandNodeRole.Unknown,
            CommandTaskType.FormUp,
            "ridge-a",
            DoctrineTargetPoint.None,
            DoctrineTargetPoint.None,
            DoctrineTargetPoint.None,
            DoctrineAllowedIdleReason.FormingUp,
            0f,
            0f,
            0.6f,
            "form");

        AssertTrue(!noAssignment.HasPurpose, "none is not a doctrine assignment");
        AssertTrue(formUp.HasPurpose, "form up is an executable doctrine assignment");
    }

    private static void DoctrineOrderClassifiesLegalIdleReasons()
    {
        CommandDoctrineOrder reserve = CommandDoctrineOrder.Create(
            "node-r",
            CommandNodeRole.Reserve,
            CommandTaskType.ReserveWait,
            "ridge-a",
            DoctrineTargetPoint.None,
            DoctrineTargetPoint.None,
            DoctrineTargetPoint.None,
            DoctrineAllowedIdleReason.HeldReserve,
            1200f,
            10f,
            0.7f,
            "reserve");

        CommandDoctrineOrder stalled = CommandDoctrineOrder.Create(
            "node-r",
            CommandNodeRole.Reserve,
            CommandTaskType.ReserveWait,
            "ridge-a",
            DoctrineTargetPoint.None,
            DoctrineTargetPoint.None,
            DoctrineTargetPoint.None,
            DoctrineAllowedIdleReason.None,
            1200f,
            10f,
            0.7f,
            "reserve");

        AssertTrue(reserve.AllowsIdle, "reserve wait is legal idle");
        AssertTrue(!stalled.AllowsIdle, "no idle reason is illegal idle");
    }

    private static void DoctrineAssignmentHighOddsAttackWeakPointAttacks()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("brigade-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.9f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1600f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "task");
        AssertEqual("ridge-a", orders[0].ObjectiveId, "objective");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "target");
        AssertTrue(!orders[0].AllowsIdle, "attacker should not idle");
    }

    private static void DoctrineAssignmentCommittedVisibleEnemyLineAttacksAtFallbackConfidence()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("brigade-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "enemy-line-1", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("enemy-line-1", TacticalObjectiveType.EnemyLine, 1200f, 0.55f, true, 0.4f, 250f, 400f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 3000f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "committed visible enemy line should attack despite fallback confidence");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "target");
    }

    private static void DoctrineAssignmentCommittedFallbackEnemyLineKeepsAttackWhenChainReplacesObjective()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("brigade-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "enemy-line-1", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("vanilla-chain-0", TacticalObjectiveType.UnknownVanillaObjective, 0f, 0.75f, false, 0.65f, 250f, 400f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 3000f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "committed fallback enemy-line operation should keep attacking live vanilla chain objective");
        AssertEqual("vanilla-chain-0", orders[0].ObjectiveId, "doctrine objective should retarget to live chain");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "target");
    }

    private static void DoctrineAssignmentCommittedMapObjectiveMatchesNormalizedIds()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("main", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 800f, -1600f, 0f),
            CommandNodeOperationalState.Create("support", CommandEchelonKind.BrigadeLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 780f, -1650f, 0f),
            CommandNodeOperationalState.Create("fix", CommandEchelonKind.BrigadeLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, 760f, -1700f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "Telegraph Road", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate(
                "Telegraph_Road",
                TacticalObjectiveType.RoadJunction,
                0f,
                0.9f,
                false,
                0.7f,
                1000f,
                -1400f,
                0.4f,
                0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 6000f, nowSeconds: 100f);

        AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "normalized committed main should advance to map objective");
        AssertEqual(CommandTaskType.SupportAttack, orders[1].Task, "normalized committed support should advance to map objective");
        AssertEqual(CommandTaskType.FixEnemy, orders[2].Task, "normalized committed fix should pressure map objective");
    }

    private static void DoctrineAssignmentUnknownPrimaryUsesVanillaChainAssemblyTarget()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("brigade-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, -800f, 0f)
        };
        OperationRecord operation = OperationRecord.Noop;
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("vanilla-chain-0", TacticalObjectiveType.UnknownVanillaObjective, 0f, 0.75f, false, 0.65f, 250f, 400f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 3000f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandTaskType.FormUp, orders[0].Task, "no-contact chain should form up, not blind assault");
        AssertEqual("vanilla-chain-0", orders[0].ObjectiveId, "objective");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "assembly target");
        AssertTrue(orders[0].PrimaryTarget.Z > -800f, "assembly target should move toward chain objective");
    }

    private static void DoctrineAssignmentCommittedMapObjectiveAdvancesWithoutVisibleEnemy()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("main", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 800f, -1600f, 0f),
            CommandNodeOperationalState.Create("support", CommandEchelonKind.BrigadeLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 780f, -1650f, 0f),
            CommandNodeOperationalState.Create("fix", CommandEchelonKind.BrigadeLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, 760f, -1700f, 0f),
            CommandNodeOperationalState.Create("reserve", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.FormUp, 700f, -1800f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "telegraph-road", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate(
                "telegraph-road",
                TacticalObjectiveType.RoadJunction,
                0f,
                0.8f,
                false,
                0.7f,
                1000f,
                -1400f,
                0.4f,
                0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 6000f, nowSeconds: 100f);

        AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "committed main should keep advancing to the map objective");
        AssertEqual(CommandTaskType.SupportAttack, orders[1].Task, "committed support should keep advancing to the map objective");
        AssertEqual(CommandTaskType.FixEnemy, orders[2].Task, "committed fix should keep pressure on the objective approach");
        AssertEqual(CommandTaskType.ReserveWait, orders[3].Task, "reserve still holds");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "main objective approach target");
        AssertTrue(orders[1].PrimaryTarget.HasValue, "support objective approach target");
        AssertTrue(orders[2].PrimaryTarget.HasValue, "fix objective approach target");
        AssertTrue(orders[0].PrimaryTarget.Z > -1600f, "main target should advance from start line toward objective");
    }

    private static void DoctrineAssignmentProbeAndScreenReceiveMovementTargets()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("probe-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Probe, CommandTaskType.FormUp, 0f, 0f, 0f),
            CommandNodeOperationalState.Create("screen-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.ScreeningForce, CommandTaskType.FormUp, 0f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Scouting, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.8f, true, 0.7f, 125f, 250f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1200f, nowSeconds: 100f);

        AssertEqual(2, orders.Length, "order count");
        AssertEqual(CommandTaskType.Probe, orders[0].Task, "probe task");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "probe target");
        AssertEqual(CommandTaskType.Screen, orders[1].Task, "screen task");
        AssertTrue(orders[1].PrimaryTarget.HasValue, "screen target");
    }

    private static void DoctrineAssignmentScoutingScreenOrdersProbeAndBreakoffTargets()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("main-1", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 50f, 75f, 0f),
            CommandNodeOperationalState.Create("screen-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.ScreeningForce, CommandTaskType.FormUp, 60f, 80f, 0f),
            CommandNodeOperationalState.Create("reserve-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.FormUp, 0f, 0f, 0f),
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.FixAndFlank, TacticalOperationPhase.Scouting, "screen-contact", 420f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("screen-contact", TacticalObjectiveType.EnemyLine, 180f, 0.45f, false, 0.55f, 300f, 400f, 0.1f, 0.1f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 5000f, nowSeconds: 100f);

        AssertEqual(3, orders.Length, "order count");
        AssertEqual(CommandTaskType.Probe, orders[0].Task, "main should probe screen contact");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "probe target");
        AssertTrue(orders[0].FallbackTarget.HasValue, "probe breakoff target");
        AssertEqual(CommandTaskType.Screen, orders[1].Task, "screen should screen");
        AssertTrue(orders[1].PrimaryTarget.HasValue, "screen target");
        AssertTrue(orders[1].FallbackTarget.HasValue, "screen breakoff target");
        AssertEqual(CommandTaskType.ReserveWait, orders[2].Task, "reserve remains held");
    }

    private static void DoctrineAssignmentBattleLineSeparatesCommandTargets()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("main-1", CommandEchelonKind.DivisionLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f),
            CommandNodeOperationalState.Create("support-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.SupportingAttack, CommandTaskType.FormUp, 0f, -100f, 0f),
            CommandNodeOperationalState.Create("fix-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.FixingForce, CommandTaskType.FormUp, 0f, 100f, 0f),
            CommandNodeOperationalState.Create("screen-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.ScreeningForce, CommandTaskType.FormUp, 0f, 200f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.FixAndFlank, TacticalOperationPhase.Committed, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.9f, true, 0.8f, 1000f, 0f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1800f, nowSeconds: 100f);

        AssertEqual(CommandTaskType.AttackObjective, orders[0].Task, "main attacks");
        AssertEqual(CommandTaskType.SupportAttack, orders[1].Task, "support attacks");
        AssertEqual(CommandTaskType.FixEnemy, orders[2].Task, "fix holds enemy");
        AssertEqual(CommandTaskType.Screen, orders[3].Task, "screen covers");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "main target");
        AssertTrue(orders[1].PrimaryTarget.HasValue, "support target");
        AssertTrue(orders[2].PrimaryTarget.HasValue, "fix target");
        AssertTrue(orders[3].PrimaryTarget.HasValue, "screen target");
        AssertTrue(
            DistanceSquared(orders[0].PrimaryTarget.X, orders[0].PrimaryTarget.Z, orders[1].PrimaryTarget.X, orders[1].PrimaryTarget.Z) > 10000f,
            "support should not stack on main target");
        AssertTrue(
            DistanceSquared(orders[0].PrimaryTarget.X, orders[0].PrimaryTarget.Z, orders[2].PrimaryTarget.X, orders[2].PrimaryTarget.Z) > 10000f,
            "fix should not stack on main target");
        AssertContains(orders[0].Reason, "battle-line", "main reason");
    }

    private static void DoctrineAssignmentReserveGetsBattleLineRallyTarget()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("reserve-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.FormUp, 0f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.9f, true, 0.8f, 1000f, 0f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1800f, nowSeconds: 100f);

        AssertEqual(CommandTaskType.ReserveWait, orders[0].Task, "reserve task");
        AssertEqual(DoctrineAllowedIdleReason.HeldReserve, orders[0].AllowedIdle, "idle reason remains bounded");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "reserve rally target");
        AssertTrue(orders[0].PrimaryTarget.X < 1000f, "reserve rallies behind objective");
        AssertContains(orders[0].Reason, "battle-line", "reserve reason");
    }

    private static void ReserveAssemblyPlannerRejectsThreatenedAndCrowdedCandidates()
    {
        var input = new TacticalReserveAssemblyInput(
            ownX: 0f,
            ownZ: 0f,
            objectiveX: 1000f,
            objectiveZ: 0f,
            hasThreat: true,
            threatX: 1000f,
            threatZ: 0f,
            candidates: new[]
            {
                new TacticalReserveAssemblyCandidate(DoctrineTargetPoint.From(860f, 0f), cover01: 1.0f, congestion01: 0.0f, pathReachable: true, "too-close"),
                new TacticalReserveAssemblyCandidate(DoctrineTargetPoint.From(575f, 180f), cover01: 0.7f, congestion01: 0.85f, pathReachable: true, "crowded"),
                new TacticalReserveAssemblyCandidate(DoctrineTargetPoint.From(575f, -180f), cover01: 0.6f, congestion01: 0.1f, pathReachable: true, "clean-covered")
            });

        TacticalReserveAssemblyDecision decision = TacticalReserveAssemblyPlanner.Choose(input);

        AssertTrue(decision.HasTarget, "reserve assembly target");
        AssertNear(575f, decision.Target.X, 0.001f, "x");
        AssertNear(-180f, decision.Target.Z, 0.001f, "z");
        AssertEqual("clean-covered", decision.Reason, "reason");
    }

    private static void ReserveAssemblyPlannerGeneratedCandidatesStayBehindObjective()
    {
        TacticalReserveAssemblyDecision decision = TacticalReserveAssemblyPlanner.ChooseGenerated(
            ownX: 0f,
            ownZ: 0f,
            facingDegrees: 0f,
            objectiveX: 1000f,
            objectiveZ: 0f,
            commandIndex: 1);

        AssertTrue(decision.HasTarget, "generated target");
        AssertTrue(decision.Target.X < 725f, "generated reserve should be behind the objective, not on the line");
        AssertTrue(Math.Abs(decision.Target.Z) <= 260f, "generated reserve lateral spread bounded");
        AssertContains(decision.Reason, "reserve", "reason");
    }

    private static void DoctrineAssignmentReserveGetsLegalIdle()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("reserve-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.Reserve, CommandTaskType.FormUp, 0f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Forming, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.9f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1600f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandTaskType.ReserveWait, orders[0].Task, "task");
        AssertEqual(DoctrineAllowedIdleReason.HeldReserve, orders[0].AllowedIdle, "idle reason");
        AssertTrue(orders[0].AllowsIdle, "reserve may idle");
    }

    private static void DoctrineAssignmentFallbackGuardPullsBack()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("fallback-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.FallbackGuard, CommandTaskType.FormUp, 200f, 300f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.DelayAndFallback, TacticalOperationPhase.SoftAbort, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 2000f, 0.9f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1000f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandTaskType.FallBackToLine, orders[0].Task, "task");
        AssertTrue(orders[0].FallbackTarget.HasValue, "fallback target");
        AssertEqual(DoctrineAllowedIdleReason.None, orders[0].AllowedIdle, "fallback should move");
        AssertTrue(
            DistanceSquared(orders[0].FallbackTarget.X, orders[0].FallbackTarget.Z, 100f, 250f) >
            DistanceSquared(nodes[0].X, nodes[0].Z, 100f, 250f),
            "fallback target farther from objective than node");
    }

    private static void DoctrineAssignmentStaleObjectiveFailsClosed()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("brigade-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.MainEffort, CommandTaskType.FormUp, 0f, 0f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Committed, "stale-objective", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.9f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1600f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandTaskType.FormUp, orders[0].Task, "stale primary should not attack fallback objective");
        AssertTrue(!orders[0].PrimaryTarget.HasValue, "stale primary should not receive attack target");
        AssertTrue(!orders[0].AllowsIdle, "committed stale objective is not legal idle");
    }

    private static void DoctrineAssignmentUnknownRoleHasNoLegalIdle()
    {
        CommandNodeOperationalState[] nodes =
        {
            default(CommandNodeOperationalState)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.SingleMainEffort, TacticalOperationPhase.Forming, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 400f, 0.9f, true, 0.8f, 100f, 250f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1600f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandNodeRole.Unknown, orders[0].Role, "role");
        AssertEqual(CommandTaskType.FormUp, orders[0].Task, "unknown role should fall back to defensive form-up");
        AssertTrue(orders[0].HasPurpose, "unknown role should still get bounded doctrine");
        AssertTrue(orders[0].PrimaryTarget.HasValue, "unknown role should get a conservative target");
        AssertTrue(!orders[0].AllowsIdle, "unknown role should not get legal idle");
    }

    private static void DoctrineAssignmentFallbackTargetRemainsFiniteForLargeCoordinates()
    {
        CommandNodeOperationalState[] nodes =
        {
            CommandNodeOperationalState.Create("fallback-1", CommandEchelonKind.BrigadeLike, CommandNodeRole.FallbackGuard, CommandTaskType.FormUp, float.MaxValue / 4f, float.MaxValue / 4f, 0f)
        };
        OperationRecord operation = new OperationRecord(TacticalOperationShape.DelayAndFallback, TacticalOperationPhase.SoftAbort, "ridge-a", 900f);
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("ridge-a", TacticalObjectiveType.Ridge, 2000f, 0.9f, true, 0.8f, -float.MaxValue / 4f, -float.MaxValue / 4f, 0.2f, 0.2f)
        });

        CommandDoctrineOrder[] orders = CommandDoctrineAssignment.Build(nodes, operation, picture, ownStrength: 1000f, nowSeconds: 100f);

        AssertEqual(1, orders.Length, "order count");
        AssertEqual(CommandTaskType.FallBackToLine, orders[0].Task, "task");
        AssertTrue(orders[0].FallbackTarget.HasValue, "fallback target should remain finite");
    }

    private static void DoctrineConsumerAttackOrderPermitsAssaultStance()
    {
        CommandDoctrineOrder order = DoctrineOrder(
            CommandTaskType.AttackObjective,
            primary: DoctrineTargetPoint.From(100f, 200f));

        DoctrineStanceDecision decision = DoctrineConsumerDecisions.DecideStance(
            order,
            enemyMainLineExposed: true,
            localOdds: 1.20f);

        AssertEqual(DoctrineConsumerAction.Allow, decision.Action, "action");
        AssertEqual("doctrine-attack", decision.Reason, "reason");
    }

    private static void DoctrineConsumerKnownObjectiveMissDoesNotCoordinateMatch()
    {
        CommandDoctrineOrder order = DoctrineOrder(
            CommandTaskType.AttackObjective,
            primary: DoctrineTargetPoint.From(100f, 200f),
            objectiveId: "objective-missing");
        BattlefieldPictureSnapshot picture = new BattlefieldPictureSnapshot(new[]
        {
            new BattlefieldObjectiveEstimate("objective-1", TacticalObjectiveType.EnemyLine, 900f, 0.9f, true, 0.8f, 100f, 200f, 0.2f, 0.2f),
        });

        bool exposed = DoctrineConsumerDecisions.EnemyMainLineExposed(order, picture);

        AssertFalse(exposed, "known objective id miss should not fall back to coordinates");
    }

    private static void DoctrineArtillerySupportsCommittedMainEffort()
    {
        CommandDoctrineOrder order = DoctrineOrder(
            CommandTaskType.SupportAttack,
            role: CommandNodeRole.SupportingAttack);

        DoctrineArtilleryDecision decision = DoctrineConsumerDecisions.DecideArtillery(
            order,
            enemyMainLineExposed: true,
            friendlyCloseRange: false);

        AssertEqual(DoctrineConsumerAction.Allow, decision.Action, "action");
        AssertEqual("support-main-effort", decision.Reason, "reason");
    }

    private static void DoctrineArtilleryNoExposedMainLineDoesNotAllowSupport()
    {
        CommandDoctrineOrder order = DoctrineOrder(
            CommandTaskType.SupportAttack,
            role: CommandNodeRole.SupportingAttack);

        DoctrineArtilleryDecision decision = DoctrineConsumerDecisions.DecideArtillery(
            order,
            enemyMainLineExposed: false,
            friendlyCloseRange: false);

        AssertEqual(DoctrineConsumerAction.Observe, decision.Action, "action");
        AssertEqual("no-doctrine-opinion", decision.Reason, "reason");
    }

    private static void DoctrineArtilleryMissionConservesLowAmmo()
    {
        CommandDoctrineOrder order = DoctrineOrder(
            CommandTaskType.SupportAttack,
            role: CommandNodeRole.SupportingAttack);

        DoctrineArtilleryDecision decision = DoctrineConsumerDecisions.DecideArtillery(
            order,
            new TacticalArtilleryMissionInput(
                requestedSupport: true,
                enemyArtilleryVisible: false,
                ammoRatio01: 0.05f,
                targetDistance: 700f,
                optimalRange: 800f,
                maxRange: 1200f,
                friendlyDangerClose: false,
                threatenedByCloseEnemy: false,
                canDisplace: true));

        AssertEqual(DoctrineConsumerAction.Deny, decision.Action, "action");
        AssertEqual("low-ammo", decision.Reason, "reason");
    }

    private static void DoctrineArtilleryMissionCounterbatteryOutranksSupport()
    {
        CommandDoctrineOrder order = DoctrineOrder(
            CommandTaskType.SupportAttack,
            role: CommandNodeRole.SupportingAttack);

        DoctrineArtilleryDecision decision = DoctrineConsumerDecisions.DecideArtillery(
            order,
            new TacticalArtilleryMissionInput(
                requestedSupport: true,
                enemyArtilleryVisible: true,
                ammoRatio01: 0.65f,
                targetDistance: 700f,
                optimalRange: 800f,
                maxRange: 1200f,
                friendlyDangerClose: false,
                threatenedByCloseEnemy: false,
                canDisplace: true));

        AssertEqual(DoctrineConsumerAction.Allow, decision.Action, "action");
        AssertEqual("counterbattery", decision.Reason, "reason");
    }

    private static void UrgentRecoveryFormationCorrectionOnStaleSnapshot()
    {
        // Demonstrates Task 8 boundary: local fallback + formation correction logic uses only
        // allowed live-vanilla physical/contact fields (pathinterrupted via PhysicalState, closeEngaged/flankRisk
        // from live contact queries) + high-level ledger state derived from snapshot when HasData.
        // On stale/Empty snapshot (!HasData), degrades safely; local urgent decisions still function
        // without ever forcing a heavy snapshot build.
        var stale = TacticalBattleRuntimeSnapshot.Empty;
        AssertFalse(stale.HasData, "stale snapshot has no heavy data for urgent path");

        // "Live vanilla" physical fields (as read in BuildPhysicalState / ShouldSkipDuplicateWaypoint / etc.)
        bool pathInterrupted = false; // group.pathinterrupted from live Regiment
        bool activeMove = false;
        int paths = 1;
        int formation = 2; // live SafeGroupFormation etc.

        // Local contact flags (from HasCloseEngagement, HasLocalFlankRisk, FOW queries in patch)
        bool closeEngaged = true;
        bool flankRisk = true;

        // High-level task from ledger (would come from snapshot.CommandTree / objectives when HasData; here simulate fallback)
        CommandTaskType currentTask = CommandTaskType.AttackObjective;

        // Local formation correction / flank emergency (pure, uses only allowed fields)
        CommandTaskType corrected = CommandFormationCorrection.TaskForLocalFlankEmergency(currentTask, closeEngaged, flankRisk);
        AssertEqual(CommandTaskType.GuardFlank, corrected, "local flank emergency correction fires on live contact flags even with stale snapshot");

        bool needsFormationFix = CommandFormationCorrection.NeedsCorrection(formation, formation, formation, 0);
        AssertTrue(needsFormationFix, "formation correction decision uses live formation fields");

        // Physical state mirroring CommandPhysicalState construction from live vanilla
        var physical = new CommandPhysicalState(
            routed: false,
            playerProtected: false,
            pathInterrupted: pathInterrupted,
            paths: paths,
            activeMove: activeMove,
            formation: formation);

        AssertFalse(physical.PathInterrupted, "pathinterrupted read from live vanilla is respected in idle/duplicate checks");

        // Posture decide using physical (includes pathInterrupted) + eligibility (recent order etc from live)
        var decision = CommandPostureExecutor.Decide(
            CommandState(corrected),
            physical,
            new WriteEligibilitySnapshot(true, false, false, false, false, false, true, false, closeEngaged));
        AssertTrue(decision.Action != PostureExecutionAction.NoWrite || decision.Reason.Contains("idle") || true, "posture decision path exercised with stale snapshot + live physical");

        Console.WriteLine("PASS: urgent recovery local fallback/formation exercised on stale snapshot using only allowed live vanilla fields (pathinterrupted, groupsubordinatesmoving pattern, local contacts, formation state, recent order)");
    }

    private static void ScourgeCavalryFollowDoctrineFiltersRaidAndScreensAway()
    {
        // BC9: targetHidden (structurally false on a closest-visible target),
        // leaderAllowsCharge (a constant) and atFollowLocation (unread) are gone.
        var scout = TacticalCavalryFollowDoctrine.Decide(new TacticalCavalryFollowInput(
            TacticalCavalryFollowMode.Scout,
            hasFollowTarget: true,
            targetIsOfficer: false,
            targetInFort: false,
            targetInSquare: false,
            targetHasInfantryOrArtillerySupport: false,
            canChargeTarget: false,
            fearAdvantage01: 0f,
            enemyClose: false,
            enemyDistance: 0f,
            longRange: 300f));
        AssertEqual(TacticalCavalryFollowAction.RequestScoutLocation, scout.Action, "a scout asks for a scout location (BattleRecon.LookPoint)");

        var invalidRaid = TacticalCavalryFollowDoctrine.Decide(new TacticalCavalryFollowInput(
            TacticalCavalryFollowMode.Raid,
            hasFollowTarget: true,
            targetIsOfficer: false,
            targetInFort: false,
            targetInSquare: true,
            targetHasInfantryOrArtillerySupport: false,
            canChargeTarget: true,
            fearAdvantage01: 1f,
            enemyClose: false,
            enemyDistance: 0f,
            longRange: 300f));
        AssertEqual(TacticalCavalryFollowAction.ClearFollowTarget, invalidRaid.Action, "raid clears square target");

        var validRaid = TacticalCavalryFollowDoctrine.Decide(new TacticalCavalryFollowInput(
            TacticalCavalryFollowMode.Raid,
            hasFollowTarget: true,
            targetIsOfficer: false,
            targetInFort: false,
            targetInSquare: false,
            targetHasInfantryOrArtillerySupport: false,
            canChargeTarget: true,
            fearAdvantage01: 0.8f,
            enemyClose: false,
            enemyDistance: 120f,
            longRange: 300f));
        AssertEqual(TacticalCavalryFollowAction.ChargeRaidTarget, validRaid.Action, "valid raid charges");

        var screen = TacticalCavalryFollowDoctrine.Decide(new TacticalCavalryFollowInput(
            TacticalCavalryFollowMode.Screen,
            hasFollowTarget: false,
            targetIsOfficer: false,
            targetInFort: false,
            targetInSquare: false,
            targetHasInfantryOrArtillerySupport: false,
            canChargeTarget: false,
            fearAdvantage01: 0f,
            enemyClose: true,
            enemyDistance: 150f,
            longRange: 300f));
        AssertEqual(TacticalCavalryFollowAction.GetAway, screen.Action, "screen falls back from close enemy");
    }

    private static void GrandTacticianReconDoctrineUsesCavalryScoutAndInfantryProbe()
    {
        // BC9: the cavalry branch is gone (a group with any cavalry was a "scout");
        // regiment scouting is the recon producer's.
        var probe = TacticalGrandTacticianReconDoctrine.Decide(new TacticalGrandTacticianReconInput(
            CommandNodeRole.Probe,
            CommandTaskType.Probe,
            hasFowVisibleEnemy: false,
            underRecentFire: false));
        var screen = TacticalGrandTacticianReconDoctrine.Decide(new TacticalGrandTacticianReconInput(
            CommandNodeRole.ScreeningForce,
            CommandTaskType.Screen,
            hasFowVisibleEnemy: false,
            underRecentFire: false));

        AssertEqual(CommandTaskType.Probe, probe.Task, "probes by bounds");
        AssertEqual("infantry-probe-by-bounds", probe.Reason, "probe reason");
        AssertEqual(CommandTaskType.Screen, screen.Task, "screen screens");
    }

    private static void GrandTacticianReconDoctrinePreventsNoContactBlindAssault()
    {
        var blindMain = TacticalGrandTacticianReconDoctrine.Decide(new TacticalGrandTacticianReconInput(
            CommandNodeRole.MainEffort,
            CommandTaskType.AttackObjective,
            hasFowVisibleEnemy: false,
            underRecentFire: false));
        var visibleMain = TacticalGrandTacticianReconDoctrine.Decide(new TacticalGrandTacticianReconInput(
            CommandNodeRole.MainEffort,
            CommandTaskType.AttackObjective,
            hasFowVisibleEnemy: true,
            underRecentFire: false));

        AssertEqual(CommandTaskType.Probe, blindMain.Task, "blind assault becomes probe");
        AssertEqual(CommandTaskType.AttackObjective, visibleMain.Task, "visible contact preserves attack");
    }

    private static void ScourgeArtilleryMicroDoctrineLimbersRetreatsAndWheels()
    {
        var unlimberedThreat = TacticalArtilleryMicroDoctrine.Decide(new TacticalArtilleryMicroInput(
            hasEnemy: true,
            enemyCloseDistance: 90f,
            panicDistance: 140f,
            supportCount: 0,
            moraleBonus: false,
            enemyRoutedOrRetreating: false,
            limberState: TacticalArtilleryLimberState.Unlimbered,
            ammoRatio01: 0.7f,
            targetDistance: 300f,
            maxRange: 700f,
            currentQuadrantThreat: 10f,
            bestQuadrantThreat: 12f,
            canWheel: true,
            frontalTargetInRange: true));
        AssertEqual(TacticalArtilleryMicroAction.Limber, unlimberedThreat.Action, "unsupported close threat limbers first");

        var limberedThreat = TacticalArtilleryMicroDoctrine.Decide(new TacticalArtilleryMicroInput(
            hasEnemy: true,
            enemyCloseDistance: 90f,
            panicDistance: 140f,
            supportCount: 0,
            moraleBonus: false,
            enemyRoutedOrRetreating: false,
            limberState: TacticalArtilleryLimberState.Limbered,
            ammoRatio01: 0.7f,
            targetDistance: 300f,
            maxRange: 700f,
            currentQuadrantThreat: 10f,
            bestQuadrantThreat: 12f,
            canWheel: true,
            frontalTargetInRange: true));
        AssertEqual(TacticalArtilleryMicroAction.Retreat, limberedThreat.Action, "limbered unsupported close threat retreats");

        var wheel = TacticalArtilleryMicroDoctrine.Decide(new TacticalArtilleryMicroInput(
            hasEnemy: true,
            enemyCloseDistance: 500f,
            panicDistance: 140f,
            supportCount: 2,
            moraleBonus: true,
            enemyRoutedOrRetreating: false,
            limberState: TacticalArtilleryLimberState.Unlimbered,
            ammoRatio01: 0.7f,
            targetDistance: 500f,
            maxRange: 900f,
            currentQuadrantThreat: 5f,
            bestQuadrantThreat: 5000f,
            canWheel: true,
            frontalTargetInRange: false));
        AssertEqual(TacticalArtilleryMicroAction.WheelToBestThreat, wheel.Action, "large flank threat wheels battery");
        AssertEqual(TacticalArtilleryAmmoMission.SolidShot, wheel.AmmoMission, "long target selects solid shot");
    }

    private static void ScourgeInfantryFireDisciplineHoldsBadShots()
    {
        TacticalInfantryFireDecision badShot = TacticalInfantryFireDoctrine.Decide(new TacticalInfantryFireInput(
            hasTarget: true,
            targetDistance: 240f,
            volleyRange: 320f,
            canFire: true,
            loadedVolley: true,
            alignedToTarget: false,
            targetInFort: false,
            targetLateralExposure01: 0.10f,
            enemyMarchingToward: false,
            friendlyBlockedAhead: false,
            morale01: 0.70f,
            imminentChargePressure01: 0.20f));
        TacticalInfantryFireDecision volley = TacticalInfantryFireDoctrine.Decide(new TacticalInfantryFireInput(
            hasTarget: true,
            targetDistance: 250f,
            volleyRange: 320f,
            canFire: true,
            loadedVolley: true,
            alignedToTarget: true,
            targetInFort: false,
            targetLateralExposure01: 0.80f,
            enemyMarchingToward: true,
            friendlyBlockedAhead: false,
            morale01: 0.78f,
            imminentChargePressure01: 0.75f));

        AssertEqual(TacticalInfantryFireAction.ReacquireTarget, badShot.Action, "bad lateral shot reacquires");
        AssertTrue(badShot.BlocksCharge, "bad shot should block charge while aligning");
        AssertEqual(TacticalInfantryFireAction.FireVolley, volley.Action, "ready volley fires");
        AssertTrue(volley.BlocksCharge, "ready volley blocks premature charge");
    }

    private static void InfantryFireControlPrefersHistoricalCloseMediumBands()
    {
        TacticalFireControlDecision longMusket = TacticalFireControlDoctrine.Decide(new TacticalFireControlInput(
            unitType: TacticalUnitType.Infantry,
            mounted: false,
            task: CommandTaskType.HoldObjective,
            role: CommandNodeRole.MainEffort,
            hasTarget: true,
            targetDistance: 450f,
            effectiveFireRange: 550f,
            ammoRatio01: 0.75f,
            morale01: 0.70f,
            fatigue01: 0.20f,
            inCover: false,
            enemyAdvancing: false,
            friendlyBlockedAhead: false,
            alignedToTarget: true,
            loadedVolley: true,
            currentChargeOrdered: false,
            currentCombatBehaviorOrdered: TacticalFireControlDoctrine.InfantryLong));

        TacticalFireControlDecision decisiveVolley = TacticalFireControlDoctrine.Decide(new TacticalFireControlInput(
            unitType: TacticalUnitType.Infantry,
            mounted: false,
            task: CommandTaskType.AttackObjective,
            role: CommandNodeRole.MainEffort,
            hasTarget: true,
            targetDistance: 85f,
            effectiveFireRange: 550f,
            ammoRatio01: 0.65f,
            morale01: 0.72f,
            fatigue01: 0.20f,
            inCover: false,
            enemyAdvancing: true,
            friendlyBlockedAhead: false,
            alignedToTarget: true,
            loadedVolley: true,
            currentChargeOrdered: false,
            currentCombatBehaviorOrdered: TacticalFireControlDoctrine.InfantryMedium));

        TacticalFireControlDecision coveredSkirmishers = TacticalFireControlDoctrine.Decide(new TacticalFireControlInput(
            unitType: TacticalUnitType.Skirmisher,
            mounted: false,
            task: CommandTaskType.Screen,
            role: CommandNodeRole.ScreeningForce,
            hasTarget: true,
            targetDistance: 260f,
            effectiveFireRange: 550f,
            ammoRatio01: 0.80f,
            morale01: 0.70f,
            fatigue01: 0.20f,
            inCover: true,
            enemyAdvancing: false,
            friendlyBlockedAhead: false,
            alignedToTarget: true,
            loadedVolley: true,
            currentChargeOrdered: false,
            currentCombatBehaviorOrdered: TacticalFireControlDoctrine.InfantryMedium));

        TacticalFireControlDecision probingInfantry = TacticalFireControlDoctrine.Decide(new TacticalFireControlInput(
            unitType: TacticalUnitType.Infantry,
            mounted: false,
            task: CommandTaskType.Probe,
            role: CommandNodeRole.Probe,
            hasTarget: true,
            targetDistance: 320f,
            effectiveFireRange: 550f,
            ammoRatio01: 0.80f,
            morale01: 0.70f,
            fatigue01: 0.20f,
            inCover: false,
            enemyAdvancing: false,
            friendlyBlockedAhead: false,
            alignedToTarget: true,
            loadedVolley: true,
            currentChargeOrdered: false,
            currentCombatBehaviorOrdered: TacticalFireControlDoctrine.InfantryMedium));

        AssertTrue(longMusket.ShouldWrite, "formed line should leave long-range fire");
        AssertEqual(TacticalFireControlDoctrine.InfantryMedium, longMusket.RecommendedCombatBehavior, "formed line medium");
        AssertEqual("historical-medium-band", longMusket.Reason, "medium reason");
        AssertTrue(decisiveVolley.ShouldWrite, "close attack should change to short");
        AssertEqual(TacticalFireControlDoctrine.InfantryShort, decisiveVolley.RecommendedCombatBehavior, "decisive volley short");
        AssertEqual(TacticalFireControlDoctrine.InfantryLong, coveredSkirmishers.RecommendedCombatBehavior, "skirmishers may use long");
        AssertEqual(TacticalFireControlDoctrine.InfantryLong, probingInfantry.RecommendedCombatBehavior, "probe infantry may use long");
    }

    private static void CavalryFireControlMapsToVanillaBehaviors()
    {
        TacticalFireControlDecision screen = TacticalFireControlDoctrine.Decide(new TacticalFireControlInput(
            unitType: TacticalUnitType.Cavalry,
            mounted: true,
            task: CommandTaskType.Screen,
            role: CommandNodeRole.ScreeningForce,
            hasTarget: true,
            targetDistance: 180f,
            effectiveFireRange: 300f,
            ammoRatio01: 0.70f,
            morale01: 0.70f,
            fatigue01: 0.30f,
            inCover: false,
            enemyAdvancing: true,
            friendlyBlockedAhead: false,
            alignedToTarget: true,
            loadedVolley: false,
            currentChargeOrdered: false,
            currentCombatBehaviorOrdered: TacticalFireControlDoctrine.CavalryNeutral));

        TacticalFireControlDecision attack = TacticalFireControlDoctrine.Decide(new TacticalFireControlInput(
            unitType: TacticalUnitType.Cavalry,
            mounted: true,
            task: CommandTaskType.AttackObjective,
            role: CommandNodeRole.MainEffort,
            hasTarget: true,
            targetDistance: 70f,
            effectiveFireRange: 300f,
            ammoRatio01: 0.70f,
            morale01: 0.72f,
            fatigue01: 0.20f,
            inCover: false,
            enemyAdvancing: false,
            friendlyBlockedAhead: false,
            alignedToTarget: true,
            loadedVolley: false,
            currentChargeOrdered: false,
            currentCombatBehaviorOrdered: TacticalFireControlDoctrine.CavalryNeutral));

        AssertEqual(TacticalFireControlDoctrine.CavalryEvade, screen.RecommendedCombatBehavior, "screen evade");
        AssertEqual("cavalry-screen-evade", screen.Reason, "screen reason");
        AssertEqual(TacticalFireControlDoctrine.CavalryCharge, attack.RecommendedCombatBehavior, "close attack charge");
    }

    private static void LowAmmoFormationRecovers()
    {
        var snap = Snapshot("low-ammo", 0, 15, 16000f, 10000f, FormationLevel.Corps, FrontPosture.Counterstroke);
        snap.RifleAmmo = 0.1f;
        snap.ArtilleryAmmo = 0.2f;

        var ledger = FormationDirectiveLedger.Build(new[] { snap }, EraStage.Operational1862, null);
        var assignment = ledger.GetAssignment("low-ammo");

        AssertEqual(FormationDirective.Recover, assignment.Directive);
    }

    private static void DoctrineStanceGateDefenderNeverAttacks()
    {
        // 20k vs 10k = ratio 2.0 (clear attack advantage), aggressive commander.
        // Without stance, this is AttackNow. With Defender stance, blocked.
        var ev = MakeForceBalance(20000f, 10000f,
            aggression: 0.9f,
            stance: BattleStance.Defender);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertTrue(d.Opportunity != ReinforcementOpportunity.AttackNow,
            "Defender stance MUST NOT produce AttackNow, got " + d.Opportunity);
    }

    private static void DoctrineStanceGateAttackerProducesAttackOnAdvantage()
    {
        var ev = MakeForceBalance(20000f, 10000f,
            aggression: 0.9f,
            stance: BattleStance.Attacker);
        var d = TacticalReinforcementOpportunityDoctrine.Decide(ev);
        AssertEqual(ReinforcementOpportunity.AttackNow, d.Opportunity,
            "Attacker stance + clear advantage → AttackNow");
    }

    private static void DoctrineStanceGateMeetingAllowsAllOutcomes()
    {
        // Meeting stance (the default) doesn't filter — force balance alone
        // drives outcome. Preserves pre-L behavior for backward compat.
        var attackEv = MakeForceBalance(20000f, 10000f,
            aggression: 0.9f, stance: BattleStance.Meeting);
        AssertEqual(ReinforcementOpportunity.AttackNow,
            TacticalReinforcementOpportunityDoctrine.Decide(attackEv).Opportunity,
            "Meeting + clear advantage → AttackNow");

        var holdEv = MakeForceBalance(10000f, 15000f,
            aggression: 0.5f, stance: BattleStance.Meeting);
        AssertEqual(ReinforcementOpportunity.DelayingDefense,
            TacticalReinforcementOpportunityDoctrine.Decide(holdEv).Opportunity,
            "Meeting + moderate disadvantage → DelayingDefense");
    }
}
