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

internal static class TacticalCommandTreeTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("command node operations runtime maps roles tasks and echelons", CommandNodeOperationsRuntimeMapsRolesTasksAndEchelons),
            ("command node operations runtime uses objective situation", CommandNodeOperationsRuntimeUsesObjectiveSituation),
            ("command node operations runtime builds single fallback state", CommandNodeOperationsRuntimeBuildsSingleFallbackState),
            ("arrival sync holds mid-band commander when supports are far", ArrivalSyncHoldsMidBandCommanderWhenSupportsAreFar),
            ("arrival sync releases when any support in proximity", ArrivalSyncReleasesWhenAnySupportInProximity),
            ("arrival sync bypassed for aggressive commander", ArrivalSyncBypassedForAggressiveCommander),
            ("arrival sync releases at max hold timeout", ArrivalSyncReleasesAtMaxHoldTimeout),
            ("arrival sync releases when no supports assigned", ArrivalSyncReleasesWhenNoSupportsAssigned),
            ("arrival sync sanitizes garbage inputs", ArrivalSyncSanitizesGarbageInputs),
            ("arrival sync resolve holds main as screen when supports far", ArrivalSyncResolveHoldsMainAsScreenWhenSupportsFar),
            ("arrival sync resolve releases main to attack when supports close", ArrivalSyncResolveReleasesMainToAttackWhenSupportsClose),
            ("arrival sync resolve only gates main attack role", ArrivalSyncResolveOnlyGatesMainAttackRole),
            ("arrival sync resolve passes through non-attack task", ArrivalSyncResolvePassesThroughNonAttackTask),
            ("command fallback target resolver uses visible threat without objective", CommandFallbackTargetResolverUsesVisibleThreatWithoutObjective),
            ("command formation correction sees visible march column despite line groupformation", CommandFormationCorrectionSeesVisibleMarchColumnDespiteLineGroupFormation),
            ("command formation correction computes vanilla threat facing", CommandFormationCorrectionComputesVanillaThreatFacing),
            ("command formation correction bounds repeated facing refreshes", CommandFormationCorrectionBoundsRepeatedFacingRefreshes),
            ("command formation correction overrides attack posture under flank emergency", CommandFormationCorrectionOverridesAttackPostureUnderFlankEmergency),
            ("command formation correction targets line for committed attack", CommandFormationCorrectionTargetsLineForCommittedAttack),
            ("command formation correction maps flank refusal to vanilla parameter", CommandFormationCorrectionMapsRefuseFlankParameter),
            ("formation level maps vanilla unit types", FormationLevelMapsVanillaUnitTypes),
            ("command node contracts sanitize ids and finite aggression", TestCommandNodeContractsSanitizeInputs),
            ("command tree builder creates synthetic root when no command candidates exist", TestCommandTreeBuilderSyntheticRootWhenEmpty),
            ("command tree builder preserves single root hierarchy depth", TestCommandTreeBuilderSingleRootHierarchyDepth),
            ("command tree builder preserves negative instance id parent links", TestCommandTreeBuilderPreservesNegativeInstanceIdParentLinks),
            ("command tree builder creates synthetic root for multiple top roots", TestCommandTreeBuilderSyntheticRootForMultipleTopRoots),
            ("command tree builder filters inactive routed wrong side and combat groups", TestCommandTreeBuilderFiltersInvalidGroups),
            ("command tree builder counts missing command parents", TestCommandTreeBuilderCountsMissingParents),
            ("command tree builder honors command hierarchy shift", TestCommandTreeBuilderHonorsCommandHierarchyShift),
            ("command tree distribution is deterministic", TestCommandTreeDistributionDeterministic),
            ("command intent allocator maps direct child role onto command node", TestCommandIntentAllocatorMapsDirectChildRole),
            ("command intent allocator inherits nearest ancestor role", TestCommandIntentAllocatorInheritsNearestAncestorRole),
            ("command intent allocator assigns bounded reserve for root fallback", TestCommandIntentAllocatorRootFallbackReserve),
            ("command intent resolver finds exact node by instance", TestCommandIntentResolverFindsExactNode),
            ("command intent resolver prefers game object id over component id", TestCommandIntentResolverPrefersGameObjectId),
            ("command intent resolver direct child fallback uses game object id", TestCommandIntentResolverDirectChildFallbackUsesGameObjectId),
            ("command intent resolver preserves negative instance ids", TestCommandIntentResolverPreservesNegativeInstanceIds),
            ("command intent resolver reports missing node without throwing", TestCommandIntentResolverMissingNode),
            ("role cascade single child inherits parent role", RoleCascadeSingleChildInheritsParentRole),
            ("role cascade main distributes center main flanks refuse", RoleCascadeMainDistributesCenterMainFlanksRefuse),
            ("role cascade main aggressive widens support band", RoleCascadeMainAggressiveWidensSupportBand),
            ("role cascade main cautious reserves more flanks", RoleCascadeMainCautiousReservesMoreFlanks),
            ("role cascade reserve all children reserve", RoleCascadeReserveAllChildrenReserve),
            ("role cascade fix all children fix", RoleCascadeFixAllChildrenFix),
            ("role cascade refuse right anchors outermost", RoleCascadeRefuseRightAnchorsOutermost),
            ("role cascade fallback all children fallback", RoleCascadeFallbackAllChildrenFallback),
            ("role cascade leaf task mapping is exhaustive", RoleCascadeLeafTaskMappingIsExhaustive),
            ("command tree probe builds parent links", CommandTreeProbeBuildsParentLinks),
            ("command tree probe sorts siblings left to right", CommandTreeProbeSortsSiblingsLeftToRight),
            ("leaf brigade map flat army four brigades main", LeafBrigadeMapFlatArmyFourBrigadesMain),
            ("leaf brigade map three tier cascade main into division", LeafBrigadeMapThreeTierCascadeMainIntoDivision),
            ("leaf brigade map reserve cascades to all leaves", LeafBrigadeMapReserveCascadesToAllLeaves),
            ("leaf brigade map records full cascade chain", LeafBrigadeMapRecordsFullCascadeChain),
            ("leaf brigade map empty inputs return empty map", LeafBrigadeMapEmptyInputsReturnEmptyMap),
            ("leaf brigade map exactly one main per sibling group", LeafBrigadeMapExactlyOneMainPerSiblingGroup),
            ("leaf brigade map survives parent cycle", LeafBrigadeMapSurvivesParentCycle),
            ("leaf brigade map bounded recursion depth", LeafBrigadeMapBoundedRecursionDepth),
            ("role cascade choose main anchor picks strongest near center", RoleCascadeChooseMainAnchorPicksStrongestNearCenter),
            ("role cascade envelopment anchors picks strongest in each half", RoleCascadeEnvelopmentAnchorsPicksStrongestInEachHalf),
            ("role cascade envelopment anchors falls back at one and two children", RoleCascadeEnvelopmentAnchorsFallsBackAtTinyChildCounts),
            ("role cascade main double wing five children produces two wings + pin + refused outers", RoleCascadeMainDoubleWingFiveChildren),
            ("role cascade main double wing seven children adds support adjacent to wings", RoleCascadeMainDoubleWingSevenChildren),
            ("role cascade main double wing below three children falls back to single anchor", RoleCascadeMainDoubleWingTwoChildrenFallsBackToSingleAnchor),
            ("heavy gate cycle scaler base at 1x compression", HeavyGateCycleScalerBaseAt1xCompression),
            ("heavy gate cycle scaler 5x compression stays near base", HeavyGateCycleScaler5xStaysNearBase),
            ("heavy gate cycle scaler 20x compression scales 5x", HeavyGateCycleScaler20xScales5x),
            ("heavy gate cycle scaler sanitizes garbage inputs", HeavyGateCycleScalerSanitizesGarbage),
            ("effective envelopment min ratio aggressive commander envelops at parity", EffectiveEnvelopmentMinRatioAggressiveCommanderEnvelopsAtParity),
            ("effective envelopment min ratio cautious commander requires margin", EffectiveEnvelopmentMinRatioCautiousCommanderRequiresMargin),
            ("effective envelopment min ratio mid-band uses baseline", EffectiveEnvelopmentMinRatioMidBandUsesBaseline),
            ("effective envelopment min ratio sanitizes garbage inputs", EffectiveEnvelopmentMinRatioSanitizesGarbageInputs),
            ("role cascade double wing echelon secondary gets support main", RoleCascadeDoubleWingEchelonSecondaryGetsSupportMain),
            ("role cascade double wing simultaneous secondary gets main", RoleCascadeDoubleWingSimultaneousSecondaryGetsMain),
            ("leaf brigade map stops at brigade tier not regiments", LeafBrigadeMapStopsAtBrigadeTierNotRegiments),
            ("leaf brigade map handles four tier army corps division brigade", LeafBrigadeMapHandlesFourTierArmyCorpsDivisionBrigade),
            ("tactical heavy path gate first tick always runs", TacticalHeavyPathGateFirstTickAlwaysRuns),
            ("tactical heavy path gate signature change respects floor", TacticalHeavyPathGateSignatureChangeRespectsFloor),
            ("tactical heavy path gate signature change runs after floor", TacticalHeavyPathGateSignatureChangeRunsAfterFloor),
            ("tactical heavy path gate max interval forces even on stable", TacticalHeavyPathGateMaxIntervalForcesEvenOnStable),
            ("tactical heavy path gate pending change runs after floor", TacticalHeavyPathGatePendingChangeRunsAfterFloor),
            ("tactical heavy path gate pending does not bypass floor", TacticalHeavyPathGatePendingDoesNotBypassFloor),
            ("tactical heavy path gate no run when stable under max", TacticalHeavyPathGateNoRunWhenStableUnderMax),
            ("tactical heavy path gate real-time floor throttles time compression", TacticalHeavyPathGateRealtimeFloorThrottlesTimeCompression),
        });
    }

    private static void CommandNodeOperationsRuntimeMapsRolesTasksAndEchelons()
    {
        var states = CommandNodeOperationsRuntime.Build(new[]
        {
            new CommandNodeIntent("army", "army", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 100, 0.5f, 0),
            new CommandNodeIntent("corps", "corps", DirectChildRole.SupportMain, DirectChildAxis.SectorAxis, 2, 80, 0.4f, 1),
            new CommandNodeIntent("division", "division", DirectChildRole.Fix, DirectChildAxis.Hold, 3, 60, 0.3f, 2),
            new CommandNodeIntent("brigade", "brigade", DirectChildRole.Screen, DirectChildAxis.Hold, 4, 40, 0.2f, 3),
            new CommandNodeIntent("reserve", "reserve", DirectChildRole.Reserve, DirectChildAxis.Hold, 0, 20, 0.1f, 4),
            new CommandNodeIntent("fallback", "fallback", DirectChildRole.Fallback, DirectChildAxis.Withdraw, 0, 20, 0.1f, 1),
            new CommandNodeIntent("refuse", "refuse", DirectChildRole.RefuseLeft, DirectChildAxis.Hold, 0, 20, 0.1f, 2),
            new CommandNodeIntent("unknown", "unknown", DirectChildRole.Unknown, DirectChildAxis.None, 0, 0, 0f, 2),
        }, TacticalOperationShape.FixAndFlank);

        AssertEqual(8, states.Count, "state count");
        AssertEqual(CommandEchelonKind.ArmyLike, states[0].Echelon, "army echelon");
        AssertEqual(CommandEchelonKind.CorpsLike, states[1].Echelon, "corps echelon");
        AssertEqual(CommandEchelonKind.DivisionLike, states[2].Echelon, "division echelon");
        AssertEqual(CommandEchelonKind.BrigadeLike, states[3].Echelon, "brigade echelon");
        AssertEqual(CommandNodeRole.MainEffort, states[0].Role, "main role");
        AssertEqual(CommandTaskType.AttackObjective, states[0].Task, "main task");
        AssertEqual(CommandNodeRole.SupportingAttack, states[1].Role, "support role");
        AssertEqual(CommandTaskType.SupportAttack, states[1].Task, "support task");
        AssertEqual(CommandNodeRole.FixingForce, states[2].Role, "fix role");
        AssertEqual(CommandTaskType.AdvanceToAssembly, states[2].Task, "fix task without contact");
        AssertEqual(CommandNodeRole.ScreeningForce, states[3].Role, "screen role");
        AssertEqual(CommandTaskType.Screen, states[3].Task, "screen task");
        AssertEqual(CommandNodeRole.Reserve, states[4].Role, "reserve role");
        AssertEqual(CommandNodeRole.FallbackGuard, states[5].Role, "fallback role");
        AssertEqual(CommandNodeRole.Defender, states[6].Role, "refuse role");
        AssertEqual(CommandNodeRole.Unknown, states[7].Role, "unknown role");
        AssertEqual("division", states[2].NodeId, "node id preserved");
        AssertEqual(0, CommandNodeOperationsRuntime.Build(null, TacticalOperationShape.SingleMainEffort).Count, "null input");
    }

    private static void CommandNodeOperationsRuntimeUsesObjectiveSituation()
    {
        var operation = new OperationRecord(
            TacticalOperationShape.FixAndFlank,
            TacticalOperationPhase.Committed,
            "ridge-a",
            600f);
        var objectives = new[]
        {
            new ObjectiveRecord(
                new ObjectiveObservationInput(
                    "ridge-a",
                    TacticalObjectiveType.Ridge,
                    TacticalObjectiveSource.VerifiedSceneObject,
                    new TacticalMapPoint(10f, 20f),
                    0.9f,
                    1.0f,
                    typeAnchorVerified: true),
                TacticalObjectiveStatus.Contested,
                enemyStrength: 90f,
                friendlyAssignedStrength: 100f)
        };

        var states = CommandNodeOperationsRuntime.Build(new[]
        {
            new CommandNodeIntent("fix", "fix", DirectChildRole.Fix, DirectChildAxis.Hold, 3, 60, 0.3f, 2),
            new CommandNodeIntent("defend", "defend", DirectChildRole.RefuseLeft, DirectChildAxis.Hold, 3, 50, 0.3f, 2),
        }, operation, objectives, BattleStance.Meeting);

        AssertEqual(CommandTaskType.FixEnemy, states[0].Task, "fixing force reacts to contested objective as contact");
        AssertEqual(CommandTaskType.HoldObjective, states[1].Task, "defender at contested objective holds instead of marching");
    }

    private static void CommandNodeOperationsRuntimeBuildsSingleFallbackState()
    {
        var operation = new OperationRecord(
            TacticalOperationShape.SingleMainEffort,
            TacticalOperationPhase.Committed,
            "crossroad-a",
            300f);
        var objectives = new[]
        {
            new ObjectiveRecord(
                new ObjectiveObservationInput(
                    "crossroad-a",
                    TacticalObjectiveType.RoadJunction,
                    TacticalObjectiveSource.VerifiedSceneObject,
                    new TacticalMapPoint(10f, 20f),
                    0.9f,
                    1.0f,
                    typeAnchorVerified: true),
                TacticalObjectiveStatus.WeaklyHeld,
                enemyStrength: 40f,
                friendlyAssignedStrength: 120f)
        };

        bool built = CommandNodeOperationsRuntime.TryBuildSingle(
            new CommandNodeIntent("node--27662", "node--26354", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 100, 0.6f, 3),
            operation,
            objectives,
            BattleStance.Meeting,
            out CommandNodeOperationalState state);

        AssertTrue(built, "fallback state built");
        AssertEqual("node--27662", state.NodeId, "node id");
        AssertEqual(CommandEchelonKind.BrigadeLike, state.Echelon, "brigade echelon");
        AssertEqual(CommandNodeRole.MainEffort, state.Role, "main role");
        AssertEqual(CommandTaskType.AttackObjective, state.Task, "main effort attacks weak objective");
    }

    private static void CommandFallbackTargetResolverUsesVisibleThreatWithoutObjective()
    {
        bool resolved = CommandFallbackTargetResolver.TryResolve(
            current: new TacticalMapPoint(100f, 100f),
            objective: null,
            threat: new TacticalMapPoint(100f, 0f),
            standOff: 250f,
            minDistance: 15f,
            maxDistance: 2500f,
            target: out TacticalMapPoint target,
            source: out string source);

        AssertTrue(resolved, "visible threat should resolve fallback without an objective anchor");
        AssertEqual("visible-threat", source, "source");
        AssertEqual(100f, target.X, "fallback x");
        AssertEqual(350f, target.Z, "fallback z");
    }

    private static void CommandFormationCorrectionSeesVisibleMarchColumnDespiteLineGroupFormation()
    {
        AssertTrue(
            CommandFormationCorrection.NeedsCorrection(
                actualFormation: 3,
                orderedFormation: 0,
                groupFormation: 0,
                targetFormation: 0),
            "visible march column must still be corrected even when groupformation already says line");

        AssertTrue(
            !CommandFormationCorrection.NeedsCorrection(
                actualFormation: 0,
                orderedFormation: 0,
                groupFormation: 0,
                targetFormation: 0),
            "matching visible, ordered, and group formation needs no correction");
    }

    private static void CommandFormationCorrectionComputesVanillaThreatFacing()
    {
        AssertNear(90f, CommandFormationCorrection.ThreatFacingRotationDegrees(0f, 0f, 10f, 0f), 0.001f, "east threat");
        AssertNear(0f, CommandFormationCorrection.ThreatFacingRotationDegrees(0f, 0f, 0f, 10f), 0.001f, "north threat");
        AssertNear(-90f, CommandFormationCorrection.ThreatFacingRotationDegrees(0f, 0f, -10f, 0f), 0.001f, "west threat");
    }

    private static void CommandFormationCorrectionBoundsRepeatedFacingRefreshes()
    {
        AssertTrue(!CommandFormationCorrection.NeedsFacingCorrection(4f, 0f, 15f), "small delta is already facing");
        AssertTrue(CommandFormationCorrection.NeedsFacingCorrection(40f, 0f, 15f), "large delta needs facing");
        AssertTrue(!CommandFormationCorrection.NeedsFacingCorrection(355f, 0f, 15f), "wraparound small delta is already facing");
        AssertTrue(CommandFormationCorrection.NeedsFacingCorrection(180f, 0f, 15f), "opposite facing needs correction");
    }

    private static void CommandFormationCorrectionOverridesAttackPostureUnderFlankEmergency()
    {
        AssertEqual(
            CommandTaskType.GuardFlank,
            CommandFormationCorrection.TaskForLocalFlankEmergency(
                CommandTaskType.AttackObjective,
                closeEngaged: true,
                flankRisk: true),
            "flanked attack should refuse/guard flank");

        AssertEqual(
            CommandTaskType.SupportAttack,
            CommandFormationCorrection.TaskForLocalFlankEmergency(
                CommandTaskType.SupportAttack,
                closeEngaged: false,
                flankRisk: true),
            "unengaged flank risk should not interrupt support task");

        AssertEqual(
            CommandTaskType.FallBackToLine,
            CommandFormationCorrection.TaskForLocalFlankEmergency(
                CommandTaskType.FallBackToLine,
                closeEngaged: true,
                flankRisk: true),
            "fallback task is already defensive");
    }

    private static void CommandFormationCorrectionTargetsLineForCommittedAttack()
    {
        AssertEqual(
            0,
            CommandFormationCorrection.TargetFormationForTask(CommandTaskType.AttackObjective, currentGroupFormation: 3),
            "attack objective should form line, not march column");
        AssertEqual(
            0,
            CommandFormationCorrection.TargetFormationForTask(CommandTaskType.SupportAttack, currentGroupFormation: 3),
            "supporting attack should form line, not march column");
        AssertEqual(
            2,
            CommandFormationCorrection.TargetFormationForTask(CommandTaskType.Probe, currentGroupFormation: 2),
            "probe can preserve a deployed non-column formation");
        AssertEqual(
            0,
            CommandFormationCorrection.TargetFormationForTask(CommandTaskType.Scout, currentGroupFormation: 4),
            "scout should not preserve square as the movement formation");
    }

    private static void CommandFormationCorrectionMapsRefuseFlankParameter()
    {
        AssertEqual(
            0,
            CommandFormationCorrection.RefuseFlankParameter(
                TacticalRefuseFlankIntent.Decision.RefuseLeft,
                CommandTaskType.GuardFlank,
                closeEngaged: true),
            "vanilla direction 0 refuses the left flank");
        AssertEqual(
            1,
            CommandFormationCorrection.RefuseFlankParameter(
                TacticalRefuseFlankIntent.Decision.RefuseRight,
                CommandTaskType.GuardFlank,
                closeEngaged: true),
            "vanilla direction 1 refuses the right flank");
        AssertEqual(
            -1,
            CommandFormationCorrection.RefuseFlankParameter(
                TacticalRefuseFlankIntent.Decision.RefuseLeft,
                CommandTaskType.AttackObjective,
                closeEngaged: true),
            "attack movement does not skew formation");
    }

    private static void FormationLevelMapsVanillaUnitTypes()
    {
        AssertEqual(FormationLevel.Division, FormationSnapshot.LevelFromUnitType(14));
        AssertEqual(FormationLevel.Corps, FormationSnapshot.LevelFromUnitType(15));
        AssertEqual(FormationLevel.Army, FormationSnapshot.LevelFromUnitType(16));
        AssertEqual(FormationLevel.Unknown, FormationSnapshot.LevelFromUnitType(17));
    }

    private static void TestCommandNodeContractsSanitizeInputs()
    {
        var node = new CommandNodeSnapshot(
            nodeId: "  ",
            parentNodeId: null,
            instanceId: 42,
            parentInstanceId: 0,
            allianceId: 1,
            rawUnitTyp: 15,
            commandHierarchyShift: -1,
            displayName: "  ",
            active: true,
            synthetic: false,
            depth: -3);
        AssertEqual("node-unknown", node.NodeId);
        AssertEqual(string.Empty, node.ParentNodeId);
        AssertEqual("node-unknown", node.DisplayName);
        AssertEqual(0, node.Depth);
        AssertEqual(16, node.EffectiveCommandLevel);

        var intent = new CommandNodeIntent(
            nodeId: "  ",
            sourceNodeId: "  ",
            role: DirectChildRole.Main,
            axis: DirectChildAxis.SectorAxis,
            primarySector: -2,
            supportPriority: 120,
            aggressionBias01: float.PositiveInfinity,
            depth: -1);
        AssertEqual("node-unknown", intent.NodeId);
        AssertEqual("node-unknown", intent.SourceNodeId);
        AssertEqual(0, intent.PrimarySector);
        AssertEqual(100, intent.SupportPriority);
        AssertEqual(0f, intent.AggressionBias01);
        AssertEqual(0, intent.Depth);
    }

    private static void TestCommandTreeBuilderSyntheticRootWhenEmpty()
    {
        var tree = CommandTreeBuilder.Build(Array.Empty<CommandTreeBuilder.CommandProbe>(), allianceId: 1, commandHierarchyShift: 0);

        AssertEqual("synth-root-1", tree.RootNodeId);
        AssertEqual(1, tree.Nodes.Count);
        AssertTrue(tree.Nodes[0].Synthetic, "empty command tree uses synthetic root");
        AssertEqual(14, tree.Nodes[0].RawUnitTyp);
        AssertEqual("14:1", tree.RawUnitTypDistribution);
    }

    private static void TestCommandTreeBuilderSingleRootHierarchyDepth()
    {
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "Army", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "Corps", true, false, false),
            new CommandTreeBuilder.CommandProbe(300, 200, 1, 14, "Division", true, false, false),
        }, 1, 0);

        AssertEqual("node-100", tree.RootNodeId);
        AssertEqual(3, tree.Nodes.Count);
        AssertEqual("node-100", tree.Nodes[0].NodeId);
        AssertEqual(0, tree.Nodes[0].Depth);
        AssertEqual("node-200", tree.Nodes[1].NodeId);
        AssertEqual("node-100", tree.Nodes[1].ParentNodeId);
        AssertEqual(1, tree.Nodes[1].Depth);
        AssertEqual("node-300", tree.Nodes[2].NodeId);
        AssertEqual("node-200", tree.Nodes[2].ParentNodeId);
        AssertEqual(2, tree.Nodes[2].Depth);
        AssertEqual(2, tree.MaxDepth);
    }

    private static void TestCommandTreeBuilderPreservesNegativeInstanceIdParentLinks()
    {
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(-100, 0, 1, 17, "Army", true, false, false),
            new CommandTreeBuilder.CommandProbe(-200, -100, 1, 15, "Corps", true, false, false),
            new CommandTreeBuilder.CommandProbe(-300, -200, 1, 14, "Division", true, false, false),
        }, 1, 0);

        AssertEqual("node--100", tree.RootNodeId);
        AssertEqual(3, tree.Nodes.Count);
        AssertEqual("node--200", tree.Nodes[1].NodeId);
        AssertEqual("node--100", tree.Nodes[1].ParentNodeId);
        AssertEqual("node--300", tree.Nodes[2].NodeId);
        AssertEqual("node--200", tree.Nodes[2].ParentNodeId);
        AssertEqual(0, tree.MissingParentCount);
    }

    private static void TestCommandTreeBuilderSyntheticRootForMultipleTopRoots()
    {
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "Army A", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 0, 1, 17, "Army B", true, false, false),
            new CommandTreeBuilder.CommandProbe(300, 100, 1, 15, "Corps", true, false, false),
        }, 1, 0);

        AssertEqual("synth-root-1", tree.RootNodeId);
        AssertEqual(4, tree.Nodes.Count);
        AssertTrue(tree.Nodes[0].Synthetic, "multiple roots use synthetic side root");
        AssertEqual("node-100", tree.Nodes[1].NodeId);
        AssertEqual("synth-root-1", tree.Nodes[1].ParentNodeId);
        AssertEqual("node-200", tree.Nodes[2].NodeId);
        AssertEqual("synth-root-1", tree.Nodes[2].ParentNodeId);
        AssertEqual(2, tree.MaxDepth);
    }

    private static void TestCommandTreeBuilderFiltersInvalidGroups()
    {
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "Army", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "Inactive", false, false, false),
            new CommandTreeBuilder.CommandProbe(300, 100, 1, 15, "Routed", true, true, false),
            new CommandTreeBuilder.CommandProbe(400, 100, 1, 15, "Marked", true, false, true),
            new CommandTreeBuilder.CommandProbe(500, 100, 0, 15, "Wrong Side", true, false, false),
            new CommandTreeBuilder.CommandProbe(600, 100, 1, 13, "Combat", true, false, false),
        }, 1, 0);

        AssertEqual(1, tree.Nodes.Count);
        AssertEqual("node-100", tree.Nodes[0].NodeId);
    }

    private static void TestCommandTreeBuilderCountsMissingParents()
    {
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 999, 1, 15, "Detached Corps", true, false, false),
        }, 1, 0);

        AssertEqual(1, tree.MissingParentCount);
        AssertEqual("node-100", tree.RootNodeId);
        AssertEqual(string.Empty, tree.Nodes[0].ParentNodeId);
    }

    private static void TestCommandTreeBuilderHonorsCommandHierarchyShift()
    {
        var shifted = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 12, "Below", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 0, 1, 14, "Early Command", true, false, false),
        }, 1, -1);

        var clampedHigh = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(300, 0, 1, 17, "Below Clamp", true, false, false),
            new CommandTreeBuilder.CommandProbe(400, 0, 1, 18, "Highest Command", true, false, false),
        }, 1, 99);

        AssertEqual(1, shifted.Nodes.Count);
        AssertEqual("node-200", shifted.RootNodeId);
        AssertEqual(15, shifted.Nodes[0].EffectiveCommandLevel);
        AssertEqual(1, clampedHigh.Nodes.Count);
        AssertEqual("node-400", clampedHigh.RootNodeId);
    }

    private static void TestCommandTreeDistributionDeterministic()
    {
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(300, 100, 1, 15, "C", true, false, false),
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "A", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "B", true, false, false),
            new CommandTreeBuilder.CommandProbe(400, 200, 1, 14, "D", true, false, false),
        }, 1, 0);

        AssertEqual("17:1,15:2,14:1", tree.RawUnitTypDistribution);
        AssertEqual("node-100", tree.Nodes[0].NodeId);
        AssertEqual("node-200", tree.Nodes[1].NodeId);
        AssertEqual("node-300", tree.Nodes[2].NodeId);
        AssertEqual("node-400", tree.Nodes[3].NodeId);
    }

    private static void TestCommandIntentAllocatorMapsDirectChildRole()
    {
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "Army", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "Corps", true, false, false),
        }, 1, 0);
        var intents = CommandTreeIntentAllocator.Allocate(tree, new[]
        {
            DirectIntent("child-200", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 0.75f, 0.6f),
        });

        AssertEqual(2, intents.Count);
        AssertEqual(DirectChildRole.Main, intents[1].Role);
        AssertEqual("node-200", intents[1].SourceNodeId);
        AssertEqual(75, intents[1].SupportPriority);
        AssertEqual(2, intents[1].PrimarySector);

        var syntheticMapped = CommandTreeIntentAllocator.Allocate(tree, new[]
        {
            DirectIntent("synth-army-100", DirectChildRole.Fallback, DirectChildAxis.Withdraw, 1, 0.3f, 0.2f),
        });
        AssertEqual(DirectChildRole.Fallback, syntheticMapped[0].Role);
        AssertEqual("node-100", syntheticMapped[0].SourceNodeId);
    }

    private static void TestCommandIntentAllocatorInheritsNearestAncestorRole()
    {
        var tree = CommandTreeBuilder.Build(new[]
        {
            new CommandTreeBuilder.CommandProbe(100, 0, 1, 17, "Army", true, false, false),
            new CommandTreeBuilder.CommandProbe(200, 100, 1, 15, "Corps", true, false, false),
            new CommandTreeBuilder.CommandProbe(300, 200, 1, 14, "Division", true, false, false),
        }, 1, 0);
        var intents = CommandTreeIntentAllocator.Allocate(tree, new[]
        {
            DirectIntent("child-200", DirectChildRole.Fix, DirectChildAxis.Hold, 4, 0.4f, 0.25f),
        });

        AssertEqual(DirectChildRole.Fix, intents[2].Role);
        AssertEqual("node-200", intents[2].SourceNodeId);
        AssertEqual(4, intents[2].PrimarySector);
        AssertEqual(2, intents[2].Depth);
    }

    private static void TestCommandIntentAllocatorRootFallbackReserve()
    {
        var tree = CommandTreeBuilder.Build(Array.Empty<CommandTreeBuilder.CommandProbe>(), 1, 0);
        var intents = CommandTreeIntentAllocator.Allocate(tree, Array.Empty<DirectChildIntent>());

        AssertEqual(1, intents.Count);
        AssertEqual("synth-root-1", intents[0].NodeId);
        AssertEqual(DirectChildRole.Reserve, intents[0].Role);
        AssertEqual(DirectChildAxis.Hold, intents[0].Axis);
        AssertEqual(25, intents[0].SupportPriority);
        AssertTrue(intents[0].AggressionBias01 >= 0f && intents[0].AggressionBias01 <= 1f, "fallback aggression bounded");
    }

    private static void TestCommandIntentResolverFindsExactNode()
    {
        var intents = new[]
        {
            new CommandNodeIntent("node-200", "node-200", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 75, 0.6f, 1),
        };

        var resolution = CommandIntentResolver.ResolveForInstance(200, intents);

        AssertTrue(resolution.Found, "exact node should resolve");
        AssertEqual("exact-command-node", resolution.Reason);
        AssertEqual(DirectChildRole.Main, resolution.Intent.Role);
    }

    private static void TestCommandIntentResolverPrefersGameObjectId()
    {
        var intents = new[]
        {
            new CommandNodeIntent("node-200", "node-200", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 75, 0.6f, 1),
        };

        var resolution = CommandIntentResolver.ResolveForInstance(
            componentInstanceId: 204,
            gameObjectInstanceId: 200,
            intents,
            directChildIntents: null);

        AssertTrue(resolution.Found, "game object command node should resolve when component id differs");
        AssertEqual("exact-command-node", resolution.Reason);
        AssertEqual("node-200", resolution.Intent.NodeId);
    }

    private static void TestCommandIntentResolverDirectChildFallbackUsesGameObjectId()
    {
        var resolution = CommandIntentResolver.ResolveForInstance(
            componentInstanceId: 304,
            gameObjectInstanceId: 300,
            Array.Empty<CommandNodeIntent>(),
            new[]
            {
                DirectIntent("child-300", DirectChildRole.Fix, DirectChildAxis.Hold, 4, 0.4f, 0.25f),
            });

        AssertTrue(resolution.Found, "game object direct child id should resolve when component id differs");
        AssertEqual("o3-direct-child-fallback", resolution.Reason);
        AssertEqual("node-300", resolution.Intent.NodeId);
        AssertEqual(DirectChildRole.Fix, resolution.Intent.Role);
    }

    private static void TestCommandIntentResolverPreservesNegativeInstanceIds()
    {
        var exact = CommandIntentResolver.ResolveForInstance(-200, new[]
        {
            new CommandNodeIntent("node--200", "node--200", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 75, 0.6f, 1),
        });
        var childFallback = CommandIntentResolver.ResolveForInstance(-300, Array.Empty<CommandNodeIntent>(), new[]
        {
            DirectIntent("child--300", DirectChildRole.Fix, DirectChildAxis.Hold, 4, 0.4f, 0.25f),
        });
        var synthFallback = CommandIntentResolver.ResolveForInstance(-400, Array.Empty<CommandNodeIntent>(), new[]
        {
            DirectIntent("synth-army--400", DirectChildRole.Fallback, DirectChildAxis.Withdraw, 5, 0.2f, 0.1f),
        });

        AssertTrue(exact.Found, "negative exact command node should resolve");
        AssertEqual(DirectChildRole.Main, exact.Intent.Role);
        AssertTrue(childFallback.Found, "negative child id should fall back");
        AssertEqual(DirectChildRole.Fix, childFallback.Intent.Role);
        AssertTrue(synthFallback.Found, "negative synth-army id should fall back");
        AssertEqual(DirectChildRole.Fallback, synthFallback.Intent.Role);
    }

    private static void TestCommandIntentResolverMissingNode()
    {
        var missing = CommandIntentResolver.ResolveForInstance(999, new[]
        {
            new CommandNodeIntent("node-200", "node-200", DirectChildRole.Main, DirectChildAxis.SectorAxis, 2, 75, 0.6f, 1),
        });
        var invalid = CommandIntentResolver.ResolveForInstance(0, Array.Empty<CommandNodeIntent>());

        AssertFalse(missing.Found, "missing node should not resolve");
        AssertEqual("command-node-not-found", missing.Reason);
        AssertFalse(invalid.Found, "invalid lookup should not resolve");
        AssertEqual("no-command-intent", invalid.Reason);
    }

    // ====== Role cascade tests (Task 1) ======

    private static void RoleCascadeSingleChildInheritsParentRole()
    {
        // With childCount==1 the cascade collapses: the single child inherits
        // the parent's role directly. This is the no-distribution boundary.
        var roles = new[]
        {
            DirectChildRole.Main, DirectChildRole.SupportMain, DirectChildRole.Fix,
            DirectChildRole.Screen, DirectChildRole.RefuseLeft, DirectChildRole.RefuseRight,
            DirectChildRole.Reserve, DirectChildRole.Fallback,
        };
        foreach (var parent in roles)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(parent, 0, 1, 1, 0, 0.5f);
            AssertEqual(parent, TacticalRoleCascade.DistributeChildRole(ctx),
                "single child cascades " + parent + " unchanged");
        }
    }

    private static void RoleCascadeMainDistributesCenterMainFlanksRefuse()
    {
        // 4-child cascade with default-aggression commander. center = 4/2 = 2
        // (right-biased for even N). With supportBand=1 (default aggression):
        //   idx 0 distance 2 from center  → outside band  → RefuseLeft
        //   idx 1 distance 1              → SupportMain
        //   idx 2 distance 0              → Main (anchor)
        //   idx 3 distance 1              → SupportMain (the right flank of an
        //                                                 even-N cascade is part
        //                                                 of the support band, not
        //                                                 a refused flank)
        // For symmetric 5-child cascades both flanks refuse evenly; even-N
        // bias falls on the right which is acceptable militarily.
        var results = new DirectChildRole[4];
        for (int i = 0; i < 4; i++)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(DirectChildRole.Main, i, 4, 1, 0, 0.5f);
            results[i] = TacticalRoleCascade.DistributeChildRole(ctx);
        }
        AssertEqual(DirectChildRole.RefuseLeft,  results[0], "idx 0 refuses left flank (distance 2)");
        AssertEqual(DirectChildRole.SupportMain, results[1], "idx 1 supports adjacent to center");
        AssertEqual(DirectChildRole.Main,        results[2], "idx 2 (right-bias center) is Main anchor");
        AssertEqual(DirectChildRole.SupportMain, results[3], "idx 3 supports adjacent (even-N right side)");

        // 5-child cascade (true symmetric): both ends refuse, support band on
        // both sides of true center idx 2.
        var sym = new DirectChildRole[5];
        for (int i = 0; i < 5; i++)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(DirectChildRole.Main, i, 5, 1, 0, 0.5f);
            sym[i] = TacticalRoleCascade.DistributeChildRole(ctx);
        }
        AssertEqual(DirectChildRole.RefuseLeft,  sym[0], "5-child idx 0 RefuseLeft");
        AssertEqual(DirectChildRole.SupportMain, sym[1], "5-child idx 1 SupportMain");
        AssertEqual(DirectChildRole.Main,        sym[2], "5-child idx 2 Main anchor");
        AssertEqual(DirectChildRole.SupportMain, sym[3], "5-child idx 3 SupportMain");
        AssertEqual(DirectChildRole.RefuseRight, sym[4], "5-child idx 4 RefuseRight");
    }

    private static void RoleCascadeMainAggressiveWidensSupportBand()
    {
        // Aggression >= 0.75: support band widens from 1 slot to 2 slots either
        // side of center, so idx 0 and idx 4 (outermost in 5-child group)
        // become SupportMain instead of Refuse.
        var results = new DirectChildRole[5];
        for (int i = 0; i < 5; i++)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(DirectChildRole.Main, i, 5, 1, 0, 0.85f);
            results[i] = TacticalRoleCascade.DistributeChildRole(ctx);
        }
        // Center = idx 2 → Main. Adjacent ±1 and ±2 (support band 2) → SupportMain.
        AssertEqual(DirectChildRole.SupportMain, results[0], "aggressive idx 0 widens to SupportMain");
        AssertEqual(DirectChildRole.SupportMain, results[1], "aggressive idx 1 SupportMain");
        AssertEqual(DirectChildRole.Main,        results[2], "aggressive center Main");
        AssertEqual(DirectChildRole.SupportMain, results[3], "aggressive idx 3 SupportMain");
        AssertEqual(DirectChildRole.SupportMain, results[4], "aggressive idx 4 widens to SupportMain");
    }

    private static void RoleCascadeMainCautiousReservesMoreFlanks()
    {
        // Cautious commander (aggression < 0.30) with low flank-exposure-bucket on
        // outer children should keep them in Reserve rather than committing to a
        // Refuse role. McClellan-style: hold the wings back.
        var ctx0 = new TacticalRoleCascade.CascadeContext(DirectChildRole.Main, 0, 4, 1, 0, 0.15f);
        AssertEqual(DirectChildRole.Reserve, TacticalRoleCascade.DistributeChildRole(ctx0),
            "cautious outer flank with low exposure stays Reserve");
        // Center still gets Main; cautious doesn't disable the attack itself.
        var ctx2 = new TacticalRoleCascade.CascadeContext(DirectChildRole.Main, 2, 4, 1, 0, 0.15f);
        AssertEqual(DirectChildRole.Main, TacticalRoleCascade.DistributeChildRole(ctx2),
            "cautious center still anchors Main");
    }

    private static void RoleCascadeReserveAllChildrenReserve()
    {
        for (int i = 0; i < 4; i++)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(DirectChildRole.Reserve, i, 4, 1, 0, 0.5f);
            AssertEqual(DirectChildRole.Reserve, TacticalRoleCascade.DistributeChildRole(ctx),
                "reserve cascades to all children at idx " + i);
        }
    }

    private static void RoleCascadeFixAllChildrenFix()
    {
        for (int i = 0; i < 3; i++)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(DirectChildRole.Fix, i, 3, 1, 0, 0.5f);
            AssertEqual(DirectChildRole.Fix, TacticalRoleCascade.DistributeChildRole(ctx),
                "fix cascades unchanged at idx " + i);
        }
    }

    private static void RoleCascadeRefuseRightAnchorsOutermost()
    {
        // RefuseRight: rightmost (idx == childCount-1) anchors the flank;
        // inner siblings go to Reserve in echelon behind it.
        for (int childCount = 2; childCount <= 4; childCount++)
        {
            for (int i = 0; i < childCount; i++)
            {
                var ctx = new TacticalRoleCascade.CascadeContext(DirectChildRole.RefuseRight, i, childCount, 1, 0, 0.5f);
                var role = TacticalRoleCascade.DistributeChildRole(ctx);
                var expected = (i == childCount - 1) ? DirectChildRole.RefuseRight : DirectChildRole.Reserve;
                AssertEqual(expected, role, "refuse-right child " + i + "/" + childCount);
            }
        }
    }

    private static void RoleCascadeFallbackAllChildrenFallback()
    {
        for (int i = 0; i < 3; i++)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(DirectChildRole.Fallback, i, 3, 1, 0, 0.5f);
            AssertEqual(DirectChildRole.Fallback, TacticalRoleCascade.DistributeChildRole(ctx),
                "fallback cascades unchanged at idx " + i);
        }
    }

    private static void RoleCascadeLeafTaskMappingIsExhaustive()
    {
        // Every named DirectChildRole maps to a non-None leaf task except Unknown.
        AssertEqual(CommandTaskType.AttackObjective, TacticalRoleCascade.RoleToLeafTask(DirectChildRole.Main), "Main → AttackObjective");
        AssertEqual(CommandTaskType.SupportAttack,   TacticalRoleCascade.RoleToLeafTask(DirectChildRole.SupportMain), "SupportMain → SupportAttack");
        AssertEqual(CommandTaskType.FixEnemy,        TacticalRoleCascade.RoleToLeafTask(DirectChildRole.Fix), "Fix → FixEnemy");
        AssertEqual(CommandTaskType.Screen,          TacticalRoleCascade.RoleToLeafTask(DirectChildRole.Screen), "Screen → Screen");
        AssertEqual(CommandTaskType.GuardFlank,      TacticalRoleCascade.RoleToLeafTask(DirectChildRole.RefuseLeft), "RefuseLeft → GuardFlank");
        AssertEqual(CommandTaskType.GuardFlank,      TacticalRoleCascade.RoleToLeafTask(DirectChildRole.RefuseRight), "RefuseRight → GuardFlank");
        AssertEqual(CommandTaskType.ReserveWait,     TacticalRoleCascade.RoleToLeafTask(DirectChildRole.Reserve), "Reserve → ReserveWait");
        AssertEqual(CommandTaskType.FallBackToLine,  TacticalRoleCascade.RoleToLeafTask(DirectChildRole.Fallback), "Fallback → FallBackToLine");
        AssertEqual(CommandTaskType.None,            TacticalRoleCascade.RoleToLeafTask(DirectChildRole.Unknown), "Unknown → None");
    }

    // ---- Envelopment cascade + playbook scoring (AttackNow + ratio>=1.5) ----

    private static void RoleCascadeEnvelopmentAnchorsPicksStrongestInEachHalf()
    {
        // 6 children, strengths [2, 5, 1, 3, 4, 1].
        // Left half indices [0..2]: max at idx 1 (strength 5).
        // Right half indices [3..5]: max at idx 4 (strength 4).
        var strengths = new int[] { 2, 5, 1, 3, 4, 1 };
        var (left, right) = TacticalRoleCascade.ChooseEnvelopmentAnchorIndices(strengths);
        AssertEqual(1, left, "left anchor = strongest left half");
        AssertEqual(4, right, "right anchor = strongest right half");
    }

    private static void RoleCascadeEnvelopmentAnchorsFallsBackAtTinyChildCounts()
    {
        // 1 child: no envelopment possible; (0, -1) sentinel.
        var one = TacticalRoleCascade.ChooseEnvelopmentAnchorIndices(new int[] { 3 });
        AssertEqual(0, one.leftAnchor, "single child left anchor 0");
        AssertEqual(-1, one.rightAnchor, "single child right anchor sentinel -1");

        // 2 children: each side gets one — (0, 1).
        var two = TacticalRoleCascade.ChooseEnvelopmentAnchorIndices(new int[] { 1, 1 });
        AssertEqual(0, two.leftAnchor, "two child left anchor 0");
        AssertEqual(1, two.rightAnchor, "two child right anchor 1");

        // Empty: returns (0, -1) sentinel without throwing.
        var empty = TacticalRoleCascade.ChooseEnvelopmentAnchorIndices(new int[] { });
        AssertEqual(0, empty.leftAnchor, "empty left anchor 0");
        AssertEqual(-1, empty.rightAnchor, "empty right anchor sentinel -1");
    }

    private static void RoleCascadeMainDoubleWingFiveChildren()
    {
        // 5 children, double-wing envelopment with anchors at indices 1 and 3.
        // Expected layout (envelopment template — two wings + center pin):
        //   idx 0: RefuseLeft  (outer flank refused)
        //   idx 1: Main         (left wing attack anchor)
        //   idx 2: Fix          (center pin — adjacent-band-1 logic would give Support
        //                        but at distance 1 from BOTH anchors the closer-wing rule
        //                        means SupportMain. Distance to left = 1, distance to right = 1.
        //                        → SupportMain by current implementation. Verifying below.)
        //   idx 3: Main         (right wing attack anchor)
        //   idx 4: RefuseRight  (outer flank refused)
        var results = new DirectChildRole[5];
        for (int i = 0; i < 5; i++)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(
                parentRole: DirectChildRole.Main,
                childIndex: i,
                childCount: 5,
                childStrengthBucket: 1,
                childFlankExposureBucket: 0,
                commanderAggression01: 0.5f,
                anchorIndex: 1,
                envelopmentMode: CascadeEnvelopmentMode.DoubleWing,
                secondaryAnchorIndex: 3);
            results[i] = TacticalRoleCascade.DistributeChildRole(ctx);
        }
        AssertEqual(DirectChildRole.RefuseLeft,  results[0], "5-child env idx 0 RefuseLeft");
        AssertEqual(DirectChildRole.Main,        results[1], "5-child env idx 1 Main (left wing)");
        AssertEqual(DirectChildRole.SupportMain, results[2], "5-child env idx 2 SupportMain (distance 1 from both wings)");
        AssertEqual(DirectChildRole.Main,        results[3], "5-child env idx 3 Main (right wing)");
        AssertEqual(DirectChildRole.RefuseRight, results[4], "5-child env idx 4 RefuseRight");
    }

    private static void RoleCascadeMainDoubleWingSevenChildren()
    {
        // 7 children with anchors at indices 1 and 5 (wider gap → real Fix in
        // center). Expected:
        //   idx 0: RefuseLeft
        //   idx 1: Main (left wing)
        //   idx 2: SupportMain (adjacent to left wing)
        //   idx 3: Fix (true center — distance 2+ from each wing)
        //   idx 4: SupportMain (adjacent to right wing)
        //   idx 5: Main (right wing)
        //   idx 6: RefuseRight
        var results = new DirectChildRole[7];
        for (int i = 0; i < 7; i++)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(
                parentRole: DirectChildRole.Main,
                childIndex: i,
                childCount: 7,
                childStrengthBucket: 1,
                childFlankExposureBucket: 0,
                commanderAggression01: 0.5f,
                anchorIndex: 1,
                envelopmentMode: CascadeEnvelopmentMode.DoubleWing,
                secondaryAnchorIndex: 5);
            results[i] = TacticalRoleCascade.DistributeChildRole(ctx);
        }
        AssertEqual(DirectChildRole.RefuseLeft,  results[0], "7-child env idx 0 RefuseLeft");
        AssertEqual(DirectChildRole.Main,        results[1], "7-child env idx 1 Main (left wing)");
        AssertEqual(DirectChildRole.SupportMain, results[2], "7-child env idx 2 SupportMain (left of pin)");
        AssertEqual(DirectChildRole.Fix,         results[3], "7-child env idx 3 Fix (center pin)");
        AssertEqual(DirectChildRole.SupportMain, results[4], "7-child env idx 4 SupportMain (right of pin)");
        AssertEqual(DirectChildRole.Main,        results[5], "7-child env idx 5 Main (right wing)");
        AssertEqual(DirectChildRole.RefuseRight, results[6], "7-child env idx 6 RefuseRight");
    }

    private static void RoleCascadeMainDoubleWingTwoChildrenFallsBackToSingleAnchor()
    {
        // 2 children with DoubleWing requested — sub-3 fallback path kicks in
        // (envelopment needs left wing + center + right wing, can't do with 2).
        // Falls back to standard single-anchor Main cascade.
        var ctx0 = new TacticalRoleCascade.CascadeContext(
            parentRole: DirectChildRole.Main,
            childIndex: 0,
            childCount: 2,
            childStrengthBucket: 1,
            childFlankExposureBucket: 0,
            commanderAggression01: 0.5f,
            anchorIndex: 0,
            envelopmentMode: CascadeEnvelopmentMode.DoubleWing,
            secondaryAnchorIndex: 1);
        var r0 = TacticalRoleCascade.DistributeChildRole(ctx0);
        // With anchor=0 in 2-child group, single-anchor logic: idx 0 = Main,
        // idx 1 = SupportMain (distance 1, within mid-band support).
        AssertEqual(DirectChildRole.Main, r0,
            "2-child env falls back to single-anchor; idx 0 = Main, got " + r0);

        var ctx1 = new TacticalRoleCascade.CascadeContext(
            parentRole: DirectChildRole.Main,
            childIndex: 1,
            childCount: 2,
            childStrengthBucket: 1,
            childFlankExposureBucket: 0,
            commanderAggression01: 0.5f,
            anchorIndex: 0,
            envelopmentMode: CascadeEnvelopmentMode.DoubleWing,
            secondaryAnchorIndex: 1);
        var r1 = TacticalRoleCascade.DistributeChildRole(ctx1);
        AssertEqual(DirectChildRole.SupportMain, r1,
            "2-child env falls back to single-anchor; idx 1 = SupportMain, got " + r1);
    }

    private static void HeavyGateCycleScalerBaseAt1xCompression()
    {
        // At 1x compression (battle and real time advance together), the
        // scaler returns base cycle unchanged.
        float result = TacticalHeavyPathGate.ScaleCycleForCompression(0.003f, 1.0f);
        AssertNear(0.003f, result, 1e-6f, "1x compression returns base cycle");
    }

    private static void HeavyGateCycleScaler5xStaysNearBase()
    {
        // Below 4x the realtime-floor handles throttling alone; scaler clamps
        // multiplier to 1.0 minimum until ratio exceeds 4x. At exactly 5x, the
        // multiplier kicks in: scale = 5/4 = 1.25.
        float at5x = TacticalHeavyPathGate.ScaleCycleForCompression(0.003f, 5f);
        AssertNear(0.00375f, at5x, 1e-6f, "5x compression: 1.25x base cycle");
        // At 3x (below 4x threshold), no scaling.
        float at3x = TacticalHeavyPathGate.ScaleCycleForCompression(0.003f, 3f);
        AssertNear(0.003f, at3x, 1e-6f, "3x compression: base cycle unchanged");
    }

    private static void HeavyGateCycleScaler20xScales5x()
    {
        // 20x is the cap. Scale factor = 20/4 = 5. Cycle = 5 * base.
        float result = TacticalHeavyPathGate.ScaleCycleForCompression(0.003f, 20f);
        AssertNear(0.015f, result, 1e-6f, "20x compression: 5x base cycle (54 battle-seconds)");
        // Above 20x clamps to 20x scaling — no runaway.
        float at100x = TacticalHeavyPathGate.ScaleCycleForCompression(0.003f, 100f);
        AssertNear(0.015f, at100x, 1e-6f, "100x clamps to 20x scaling");
    }

    private static void HeavyGateCycleScalerSanitizesGarbage()
    {
        // NaN/Inf/negative inputs return safe defaults.
        AssertNear(0.003f, TacticalHeavyPathGate.ScaleCycleForCompression(float.NaN, 1f), 1e-6f, "NaN base -> safe default");
        AssertNear(0.003f, TacticalHeavyPathGate.ScaleCycleForCompression(0f, 1f), 1e-6f, "zero base -> safe default");
        AssertNear(0.003f, TacticalHeavyPathGate.ScaleCycleForCompression(0.003f, float.NaN), 1e-6f, "NaN ratio -> 1x scaling");
        AssertNear(0.003f, TacticalHeavyPathGate.ScaleCycleForCompression(0.003f, -5f), 1e-6f, "negative ratio -> 1x scaling");
    }

    private static void EffectiveEnvelopmentMinRatioAggressiveCommanderEnvelopsAtParity()
    {
        // Lee/Jackson/Hood (aggression ≥ 0.7) accept envelopment at parity.
        AssertNear(ArmyOrchestrator.AggressiveEnvelopmentMinRatio,
            ArmyOrchestrator.EffectiveEnvelopmentMinRatio(0.7f),
            1e-6f, "aggressive commander at threshold uses AggressiveEnvelopmentMinRatio");
        AssertNear(ArmyOrchestrator.AggressiveEnvelopmentMinRatio,
            ArmyOrchestrator.EffectiveEnvelopmentMinRatio(0.95f),
            1e-6f, "very aggressive commander uses AggressiveEnvelopmentMinRatio");
        AssertNear(1.0f, ArmyOrchestrator.AggressiveEnvelopmentMinRatio, 1e-6f,
            "AggressiveEnvelopmentMinRatio = 1.0 (parity)");
    }

    private static void EffectiveEnvelopmentMinRatioCautiousCommanderRequiresMargin()
    {
        // McClellan/Johnston (aggression ≤ 0.3) require a clearer margin.
        AssertNear(ArmyOrchestrator.CautiousEnvelopmentMinRatio,
            ArmyOrchestrator.EffectiveEnvelopmentMinRatio(0.3f),
            1e-6f, "cautious commander at threshold uses CautiousEnvelopmentMinRatio");
        AssertNear(ArmyOrchestrator.CautiousEnvelopmentMinRatio,
            ArmyOrchestrator.EffectiveEnvelopmentMinRatio(0.05f),
            1e-6f, "very cautious commander uses CautiousEnvelopmentMinRatio");
        AssertNear(1.4f, ArmyOrchestrator.CautiousEnvelopmentMinRatio, 1e-6f,
            "CautiousEnvelopmentMinRatio = 1.4");
    }

    private static void EffectiveEnvelopmentMinRatioMidBandUsesBaseline()
    {
        // Mid-aggression commanders (0.3 < aggression < 0.7) use the 1.2 baseline.
        AssertNear(ArmyOrchestrator.EnvelopmentMinRatio,
            ArmyOrchestrator.EffectiveEnvelopmentMinRatio(0.5f),
            1e-6f, "mid-band aggression uses EnvelopmentMinRatio baseline");
        AssertNear(ArmyOrchestrator.EnvelopmentMinRatio,
            ArmyOrchestrator.EffectiveEnvelopmentMinRatio(0.69f),
            1e-6f, "just-below-aggressive threshold uses baseline");
        AssertNear(ArmyOrchestrator.EnvelopmentMinRatio,
            ArmyOrchestrator.EffectiveEnvelopmentMinRatio(0.31f),
            1e-6f, "just-above-cautious threshold uses baseline");
    }

    private static void EffectiveEnvelopmentMinRatioSanitizesGarbageInputs()
    {
        // Out-of-range / NaN must not throw and must produce a usable value.
        AssertTrue(!float.IsNaN(ArmyOrchestrator.EffectiveEnvelopmentMinRatio(float.NaN)),
            "NaN aggression returns a finite ratio");
        AssertTrue(ArmyOrchestrator.EffectiveEnvelopmentMinRatio(-5f) > 0f,
            "negative aggression clamps to ≥ 0 then returns cautious ratio");
        AssertTrue(ArmyOrchestrator.EffectiveEnvelopmentMinRatio(99f) > 0f,
            "huge aggression clamps to 1 then returns aggressive ratio");
    }

    private static void RoleCascadeDoubleWingEchelonSecondaryGetsSupportMain()
    {
        // Echelon variant of double-wing: PRIMARY anchor (AnchorIndex) gets
        // Main; SECONDARY anchor (SecondaryAnchorIndex) gets SupportMain
        // instead of Main. Models Longstreet's Day 2 sequential release —
        // primary wing attacks first, secondary follows once the first
        // engages.
        var results = new DirectChildRole[5];
        for (int i = 0; i < 5; i++)
        {
            var ctx = new TacticalRoleCascade.CascadeContext(
                parentRole: DirectChildRole.Main,
                childIndex: i,
                childCount: 5,
                childStrengthBucket: 1,
                childFlankExposureBucket: 0,
                commanderAggression01: 0.4f,  // sub-threshold; chooses echelon
                anchorIndex: 1,                // primary wing
                envelopmentMode: CascadeEnvelopmentMode.DoubleWingEchelon,
                secondaryAnchorIndex: 3);      // secondary wing
            results[i] = TacticalRoleCascade.DistributeChildRole(ctx);
        }
        AssertEqual(DirectChildRole.RefuseLeft,  results[0], "echelon idx 0 RefuseLeft");
        AssertEqual(DirectChildRole.Main,        results[1], "echelon idx 1 PRIMARY = Main");
        AssertEqual(DirectChildRole.SupportMain, results[2], "echelon idx 2 between wings = SupportMain");
        AssertEqual(DirectChildRole.SupportMain, results[3], "echelon idx 3 SECONDARY = SupportMain (not Main)");
        AssertEqual(DirectChildRole.RefuseRight, results[4], "echelon idx 4 RefuseRight");
    }

    private static void RoleCascadeDoubleWingSimultaneousSecondaryGetsMain()
    {
        // Sanity counter-check: simultaneous DoubleWing (not Echelon) still
        // gives secondary anchor Main — confirms the previous test isolates
        // the echelon-specific branch.
        var ctx = new TacticalRoleCascade.CascadeContext(
            parentRole: DirectChildRole.Main,
            childIndex: 3,
            childCount: 5,
            childStrengthBucket: 1,
            childFlankExposureBucket: 0,
            commanderAggression01: 0.5f,
            anchorIndex: 1,
            envelopmentMode: CascadeEnvelopmentMode.DoubleWing,
            secondaryAnchorIndex: 3);
        var role = TacticalRoleCascade.DistributeChildRole(ctx);
        AssertEqual(DirectChildRole.Main, role,
            "simultaneous double-wing: secondary anchor (idx 3) = Main, got " + role);
    }

    // ====== Command tree probe tests (Task 2) ======

    private static TacticalCommandTreeProbe.ExtendedProbe Probe(
        int id, int unittyp, string name, int parentId, bool isDirectChild = true, float x = 0f, float z = 0f, int strength = 1)
    {
        return new TacticalCommandTreeProbe.ExtendedProbe(id, unittyp, name, true, parentId, isDirectChild, x, z, strength);
    }

    private static void CommandTreeProbeBuildsParentLinks()
    {
        // Build a small 2-tier tree: army -100 with three brigade children -10/-20/-30.
        var probes = new[]
        {
            Probe(-100, 16, "Army_of_Northern_Virginia", 0, isDirectChild: false),
            Probe(-10, 14, "1st_Brigade", -100, x: -50f),
            Probe(-20, 14, "2nd_Brigade", -100, x: 0f),
            Probe(-30, 14, "3rd_Brigade", -100, x: 50f),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, commandHierarchyShift: 1);
        AssertEqual(4, tree.Count, "tree has 4 nodes");
        AssertTrue(tree.ContainsKey(-100), "army root present");
        AssertEqual(3, tree[-100].ChildInstanceIds.Count, "army has 3 children");
        AssertEqual(-100, tree[-10].ParentInstanceId, "brigade -10 links to army -100");
    }

    private static void CommandTreeProbeSortsSiblingsLeftToRight()
    {
        // Children should come back sorted by WorldX ascending so cascade
        // geometry-distribution sees left-to-right ordering.
        var probes = new[]
        {
            Probe(-100, 16, "army", 0, isDirectChild: false),
            Probe(-3, 14, "rightmost", -100, x: 100f),
            Probe(-1, 14, "leftmost", -100, x: -100f),
            Probe(-2, 14, "center", -100, x: 0f),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 1);
        var children = TacticalCommandTreeProbe.EnumerateChildrenLeftToRight(-100, tree);
        AssertEqual(3, children.Count, "3 children");
        AssertEqual("leftmost", children[0].Name, "leftmost first");
        AssertEqual("center",   children[1].Name, "center next");
        AssertEqual("rightmost", children[2].Name, "rightmost last");
    }

    // ====== Leaf brigade map tests (Task 3) ======

    private static void LeafBrigadeMapFlatArmyFourBrigadesMain()
    {
        // CSA-shape: army with 4 direct brigade children. Top assignment role=Main.
        // Cascade should: center idx 2 = Main, idx 1 = SupportMain, idx 0 =
        // RefuseLeft, idx 3 = RefuseRight (after geometric left-to-right sort).
        var probes = new[]
        {
            Probe(-100, 16, "army", 0, isDirectChild: false),
            Probe(-1, 14, "1st_Brigade", -100, x: -150f),
            Probe(-2, 14, "2nd_Brigade", -100, x: -50f),
            Probe(-3, 14, "3rd_Brigade", -100, x: 50f),
            Probe(-4, 14, "4th_Brigade", -100, x: 150f),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 1);
        // Top assignments are each direct child of the army (-100), all role=Main
        // — but in this Whiskey simplification each top child already has its own
        // role, so we test the leaf cascade by passing each brigade with the
        // intended role separately. For a real Main-effort-on-army scenario the
        // caller would pass exactly one top child with role=Main covering all
        // its children. Here we use the army-itself as a single top entry with
        // role=Main and verify the recursive distribution.
        var topAssignments = new[]
        {
            new TacticalLeafBrigadeMap.TopAssignment(-100, DirectChildRole.Main),
        };
        var map = TacticalLeafBrigadeMap.BuildMap(tree, topAssignments, commanderAggression01: 0.5f);

        AssertEqual(4, map.Count, "flat-army cascade produces 4 leaf assignments");
        // The 1st_Brigade (leftmost x=-150) is idx 0 after sort. center=2 →
        // idx 0 RefuseLeft, idx 1 SupportMain, idx 2 Main, idx 3 SupportMain
        // (even-N right-bias keeps idx 3 inside the support band).
        AssertEqual(DirectChildRole.RefuseLeft,  map[-1].LeafRole, "leftmost 1st_Brigade RefuseLeft");
        AssertEqual(DirectChildRole.SupportMain, map[-2].LeafRole, "2nd_Brigade SupportMain");
        AssertEqual(DirectChildRole.Main,        map[-3].LeafRole, "3rd_Brigade Main (center anchor)");
        AssertEqual(DirectChildRole.SupportMain, map[-4].LeafRole, "4th_Brigade SupportMain (within even-N support band)");
    }

    private static void LeafBrigadeMapThreeTierCascadeMainIntoDivision()
    {
        // Union-shape: corps -100 has division -200 (left) and division -300
        // (right). Each division has 2 brigades. Top assignment: division -200
        // role=Main. Cascade: -200's brigades inherit Main role at division
        // level, then distribute: center child of 2 → idx 1 = Main, idx 0 =
        // RefuseLeft (geometric).
        var probes = new[]
        {
            Probe(-100, 16, "3rd_Division_top", 0, isDirectChild: false),
            Probe(-200, 15, "5th_Division", -100, x: -100f),
            Probe(-300, 15, "2nd_Division", -100, x: 100f),
            Probe(-201, 14, "5d_left", -200, x: -150f),
            Probe(-202, 14, "5d_right", -200, x: -50f),
            Probe(-301, 14, "2d_left", -300, x: 50f),
            Probe(-302, 14, "2d_right", -300, x: 150f),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 1);
        var topAssignments = new[]
        {
            new TacticalLeafBrigadeMap.TopAssignment(-200, DirectChildRole.Main),
            new TacticalLeafBrigadeMap.TopAssignment(-300, DirectChildRole.Reserve),
        };
        var map = TacticalLeafBrigadeMap.BuildMap(tree, topAssignments, 0.5f);

        AssertEqual(4, map.Count, "3-tier cascade reaches all 4 leaves");
        // -200 (Main, 2 children): center=idx 1 → Main anchor; idx 0 → RefuseLeft.
        // childCount=2 → center = 2/2 = 1; idx 0 distance from center = 1, supportBand=1, so SupportMain.
        // Actually with 2 children the algorithm puts idx 0 in supportBand (distance=1, supportBand=1).
        // Re-verify our expectation:
        AssertTrue(map[-201].LeafRole == DirectChildRole.SupportMain || map[-201].LeafRole == DirectChildRole.RefuseLeft,
            "5d_left got Main-cascade result: " + map[-201].LeafRole);
        AssertEqual(DirectChildRole.Main,    map[-202].LeafRole, "5d_right (center idx) gets Main");
        // -300 (Reserve, 2 children): all reserve.
        AssertEqual(DirectChildRole.Reserve, map[-301].LeafRole, "2d_left Reserve");
        AssertEqual(DirectChildRole.Reserve, map[-302].LeafRole, "2d_right Reserve");
    }

    private static void LeafBrigadeMapReserveCascadesToAllLeaves()
    {
        var probes = new[]
        {
            Probe(-100, 16, "corps", 0, isDirectChild: false),
            Probe(-200, 15, "div_a", -100),
            Probe(-201, 14, "leaf_a1", -200),
            Probe(-202, 14, "leaf_a2", -200),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 1);
        var top = new[] { new TacticalLeafBrigadeMap.TopAssignment(-200, DirectChildRole.Reserve) };
        var map = TacticalLeafBrigadeMap.BuildMap(tree, top, 0.5f);
        AssertEqual(2, map.Count, "2 leaves under reserve division");
        AssertEqual(DirectChildRole.Reserve, map[-201].LeafRole, "leaf_a1 Reserve");
        AssertEqual(DirectChildRole.Reserve, map[-202].LeafRole, "leaf_a2 Reserve");
        AssertEqual(CommandTaskType.ReserveWait, map[-201].LeafTask, "leaf_a1 ReserveWait task");
    }

    private static void LeafBrigadeMapRecordsFullCascadeChain()
    {
        // 3-tier Main cascade: chain should be [Main(top), <derived at div>, <derived at leaf>].
        var probes = new[]
        {
            Probe(-100, 16, "corps", 0, isDirectChild: false),
            Probe(-200, 15, "div_main", -100, x: 0f),
            Probe(-201, 14, "leaf_center", -200, x: 0f),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 1);
        var top = new[] { new TacticalLeafBrigadeMap.TopAssignment(-200, DirectChildRole.Main) };
        var map = TacticalLeafBrigadeMap.BuildMap(tree, top, 0.5f);
        AssertEqual(1, map.Count, "1 leaf");
        // chain: top role at div (Main) -> sole child of 1, collapses to Main
        AssertEqual(2, map[-201].CascadeChain.Count, "chain captures top + leaf");
        AssertEqual(DirectChildRole.Main, map[-201].CascadeChain[0], "chain[0] is top Main");
        AssertEqual(DirectChildRole.Main, map[-201].CascadeChain[1], "chain[1] is leaf Main (collapsed single child)");
        // ParentNameChain: contains the div name + leaf name
        AssertTrue(map[-201].ParentNameChain.Count >= 2, "parent name chain has at least 2 entries");
    }

    private static void LeafBrigadeMapEmptyInputsReturnEmptyMap()
    {
        AssertEqual(0, TacticalLeafBrigadeMap.BuildMap(null, null, 0.5f).Count,
            "null inputs return empty map");
        AssertEqual(0,
            TacticalLeafBrigadeMap.BuildMap(
                new Dictionary<int, TacticalCommandTreeProbe.ProbeNode>(),
                System.Array.Empty<TacticalLeafBrigadeMap.TopAssignment>(),
                0.5f).Count,
            "empty tree+assignments yields empty map");
    }

    private static void LeafBrigadeMapExactlyOneMainPerSiblingGroup()
    {
        // Regression for the multi-self-anchor bug. Two adjacent siblings both
        // with high strength (bucket 2) under a Main parent must produce
        // exactly ONE Main leaf, not two. The cascade pre-picks the anchor at
        // the parent level so the same anchor is passed to every sibling's
        // distribution call.
        var probes = new[]
        {
            Probe(-100, 16, "corps", 0, isDirectChild: false),
            Probe(-200, 15, "div_main", -100),
            // 4 leaves under div_main, strengths {1, 2, 2, 1}: idx 1 and 2 are
            // both adjacent-to-center and both strength 2. Pre-2026-05-19 cascade
            // would have flagged both as Main. After the anchor refactor, only
            // one wins (the leftmost of the two strong, by ChooseMainAnchorIndex).
            Probe(-201, 14, "leaf_left",       -200, x: -150f, strength: 1),
            Probe(-202, 14, "leaf_strong1",    -200, x: -50f,  strength: 2),
            Probe(-203, 14, "leaf_strong2",    -200, x: 50f,   strength: 2),
            Probe(-204, 14, "leaf_right",      -200, x: 150f,  strength: 1),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 1);
        var top = new[] { new TacticalLeafBrigadeMap.TopAssignment(-200, DirectChildRole.Main) };
        var map = TacticalLeafBrigadeMap.BuildMap(tree, top, 0.5f);

        int mainCount = 0;
        foreach (var kv in map)
            if (kv.Value.LeafRole == DirectChildRole.Main) mainCount++;
        AssertEqual(1, mainCount, "exactly one Main leaf, not multi-anchor");
    }

    private static void LeafBrigadeMapSurvivesParentCycle()
    {
        // Defense-in-depth: malformed parent linkage (A's parent is B, B's
        // parent is A) must not stack-overflow. The visited set in CascadeInto
        // bails on revisited nodes.
        var cycleProbes = new[]
        {
            new TacticalCommandTreeProbe.ExtendedProbe(-100, 15, "A", true, parentInstanceId: -200, isDirectChild: false, worldX: 0f, worldZ: 0f, strengthBucket: 1),
            new TacticalCommandTreeProbe.ExtendedProbe(-200, 15, "B", true, parentInstanceId: -100, isDirectChild: true, worldX: 100f, worldZ: 0f, strengthBucket: 1),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(cycleProbes, 1);
        var top = new[] { new TacticalLeafBrigadeMap.TopAssignment(-100, DirectChildRole.Main) };
        // Should return (eventually) without exception.
        var map = TacticalLeafBrigadeMap.BuildMap(tree, top, 0.5f);
        AssertTrue(map != null, "cycle did not stack-overflow");
    }

    private static void LeafBrigadeMapBoundedRecursionDepth()
    {
        // Build a degenerate 20-deep linear chain: each node has 1 child. The
        // cascade's MaxCascadeDepth (16) should cap recursion well before the
        // chain bottoms out. No exception, no stack overflow.
        var probes = new List<TacticalCommandTreeProbe.ExtendedProbe>();
        for (int i = 0; i < 20; i++)
        {
            int id = -100 - i;
            int parentId = i == 0 ? 0 : -(100 + i - 1);
            probes.Add(new TacticalCommandTreeProbe.ExtendedProbe(
                instanceId: id, unittyp: 14, name: "node" + i, active: true,
                parentInstanceId: parentId, isDirectChild: i == 0,
                worldX: 0f, worldZ: 0f, strengthBucket: 1));
        }
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 1);
        var top = new[] { new TacticalLeafBrigadeMap.TopAssignment(-100, DirectChildRole.Reserve) };
        var map = TacticalLeafBrigadeMap.BuildMap(tree, top, 0.5f);
        AssertTrue(map != null, "deep tree did not stack-overflow");
        // Depth cap stops traversal; we don't assert exact count, just that the
        // cascade terminated.
    }

    private static void LeafBrigadeMapStopsAtBrigadeTierNotRegiments()
    {
        // Critical regression test for the regiment-leaf bug. A brigade has
        // regiments (combat units, unittyp Infantry=0, Cavalry=1, Artillery=2)
        // attached to it via Regiment.allattachedunits. The cascade must NOT
        // descend into those regiments or the leaf map would be keyed by
        // combat-unit instance IDs and the brigade-tier posture filter would
        // silently miss everything.
        var probes = new[]
        {
            Probe(-100, 15, "division", 0, isDirectChild: false),
            // A brigade with several Infantry regiments nested under it. Vanilla
            // exposes these via allattachedunits, and BuildExtendedProbesForAlliance
            // surfaces them as probe nodes too.
            Probe(-200, 14, "brigade", -100, x: 0f, strength: 2),
            new TacticalCommandTreeProbe.ExtendedProbe(-201, 0, "1st_Tennessee", true, parentInstanceId: -200, isDirectChild: false, worldX: -10f, worldZ: 0f, strengthBucket: 1),  // Infantry=0
            new TacticalCommandTreeProbe.ExtendedProbe(-202, 0, "2nd_Tennessee", true, parentInstanceId: -200, isDirectChild: false, worldX: 0f, worldZ: 0f, strengthBucket: 1),
            new TacticalCommandTreeProbe.ExtendedProbe(-203, 2, "Maney's_Battery", true, parentInstanceId: -200, isDirectChild: false, worldX: 10f, worldZ: 0f, strengthBucket: 1),  // Artillery=2
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 1);
        var top = new[] { new TacticalLeafBrigadeMap.TopAssignment(-200, DirectChildRole.Main) };
        var map = TacticalLeafBrigadeMap.BuildMap(tree, top, 0.5f);

        AssertEqual(1, map.Count, "exactly one leaf assignment — the brigade, not its regiments");
        AssertTrue(map.ContainsKey(-200), "leaf map keyed by brigade instance ID, not regiments");
        AssertFalse(map.ContainsKey(-201), "1st_Tennessee regiment not in leaf map");
        AssertFalse(map.ContainsKey(-202), "2nd_Tennessee regiment not in leaf map");
        AssertFalse(map.ContainsKey(-203), "Maney's_Battery not in leaf map");
        AssertEqual(DirectChildRole.Main, map[-200].LeafRole, "brigade inherits Main from cascade");
    }

    private static void LeafBrigadeMapHandlesFourTierArmyCorpsDivisionBrigade()
    {
        // Full 4-tier hierarchy: Army → Corps → Division → Brigade (with
        // regiments under the brigades). Verifies the cascade walks corps and
        // divisions correctly and stops at brigade tier.
        var probes = new[]
        {
            new TacticalCommandTreeProbe.ExtendedProbe(-100, 16, "Army_of_Tennessee", true, parentInstanceId: 0, isDirectChild: false, worldX: 0f, worldZ: 0f, strengthBucket: 2),  // Army=16
            new TacticalCommandTreeProbe.ExtendedProbe(-200, 15, "1st_Corps", true, parentInstanceId: -100, isDirectChild: true, worldX: -100f, worldZ: 0f, strengthBucket: 2),  // Corps slot via shift — represented as 15 here
            new TacticalCommandTreeProbe.ExtendedProbe(-300, 15, "1st_Division", true, parentInstanceId: -200, isDirectChild: false, worldX: -100f, worldZ: 0f, strengthBucket: 2),  // Division=15
            new TacticalCommandTreeProbe.ExtendedProbe(-400, 14, "1st_Brigade", true, parentInstanceId: -300, isDirectChild: false, worldX: -150f, worldZ: 0f, strengthBucket: 1),
            new TacticalCommandTreeProbe.ExtendedProbe(-401, 14, "2nd_Brigade", true, parentInstanceId: -300, isDirectChild: false, worldX: -50f, worldZ: 0f, strengthBucket: 2),
            // A regiment under each brigade for realism — must NOT appear in leaves.
            new TacticalCommandTreeProbe.ExtendedProbe(-500, 0, "1st_Bde_1st_Reg", true, parentInstanceId: -400, isDirectChild: false, worldX: -150f, worldZ: 0f, strengthBucket: 1),
            new TacticalCommandTreeProbe.ExtendedProbe(-501, 0, "2nd_Bde_1st_Reg", true, parentInstanceId: -401, isDirectChild: false, worldX: -50f, worldZ: 0f, strengthBucket: 1),
        };
        var tree = TacticalCommandTreeProbe.BuildTree(probes, 1);
        // Top assignment fires from the Corps level (typical post-shift Whiskey OOB).
        var top = new[] { new TacticalLeafBrigadeMap.TopAssignment(-200, DirectChildRole.Main) };
        var map = TacticalLeafBrigadeMap.BuildMap(tree, top, 0.5f);

        AssertEqual(2, map.Count, "exactly 2 leaf brigade assignments under the corps");
        AssertTrue(map.ContainsKey(-400), "1st_Brigade in leaf map");
        AssertTrue(map.ContainsKey(-401), "2nd_Brigade in leaf map");
        AssertFalse(map.ContainsKey(-500), "regiment under 1st_Brigade NOT in leaf map");
        AssertFalse(map.ContainsKey(-501), "regiment under 2nd_Brigade NOT in leaf map");
        AssertFalse(map.ContainsKey(-300), "intermediate division NOT in leaf map");
        AssertFalse(map.ContainsKey(-200), "corps NOT in leaf map");
        // Cascade chain should reflect the 4-tier walk: Main → ... → leaf role
        AssertTrue(map[-401].CascadeChain.Count >= 3, "cascade chain depth >= 3 (corps -> division -> brigade) but got " + map[-401].CascadeChain.Count);
    }

    private static void RoleCascadeChooseMainAnchorPicksStrongestNearCenter()
    {
        // ChooseMainAnchorIndex: default to center, but a stronger sibling
        // within nearCenterRadius pulls the anchor.
        var allEqual = new[] { 1, 1, 1, 1, 1 };
        AssertEqual(2, TacticalRoleCascade.ChooseMainAnchorIndex(allEqual), "all-equal picks center");

        var strongLeft = new[] { 1, 2, 1, 1, 1 };
        AssertEqual(1, TacticalRoleCascade.ChooseMainAnchorIndex(strongLeft), "stronger left-of-center pulls anchor");

        var strongRight = new[] { 1, 1, 1, 2, 1 };
        AssertEqual(3, TacticalRoleCascade.ChooseMainAnchorIndex(strongRight), "stronger right-of-center pulls anchor");

        var farStrong = new[] { 3, 1, 1, 1, 1 };
        // idx 0 is strongest but outside nearCenterRadius=1 from center=2 → center wins
        AssertEqual(2, TacticalRoleCascade.ChooseMainAnchorIndex(farStrong), "far-from-center strong sibling is ignored");

        var empty = System.Array.Empty<int>();
        AssertEqual(0, TacticalRoleCascade.ChooseMainAnchorIndex(empty), "empty list returns 0");
    }

    // TacticalHeavyPathGate TDD tests (Task 3). Written first (RED until gate impl correct).
    // Uses per-side scalar Input per gate pattern (DirectChildGate etc). cycleHours is both floor and max.
    // hasPending tracked by caller; gate is pure decision only.

    private static void TacticalHeavyPathGateFirstTickAlwaysRuns()
    {
        var curr = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var lastSig = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        // lastHeavy <=0 -> first tick
        var input = new TacticalHeavyPathGate.Input(curr, 1.5f, 0f, lastSig, 0.003f, false);
        var dec = TacticalHeavyPathGate.Decide(input);
        AssertTrue(dec.ShouldRun, "first tick (lastHeavy=0) must always run heavy");
        AssertEqual(TacticalHeavyPathGate.Action.Run, dec.Action, "first tick action");
    }

    private static void TacticalHeavyPathGateSignatureChangeRespectsFloor()
    {
        var curr = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var lastSig = new TacticalBattleStateSignature(100, 12000, 11500, 3, 2, false, 98765, 0, 12); // macro AI differs -> sig change
        // elapsed < cycle -> must throttle even on change
        var input = new TacticalHeavyPathGate.Input(curr, 1.502f, 1.5f, lastSig, 0.003f, false);
        var dec = TacticalHeavyPathGate.Decide(input);
        AssertFalse(dec.ShouldRun, "sig change must respect min floor (cycleHours)");
    }

    private static void TacticalHeavyPathGateSignatureChangeRunsAfterFloor()
    {
        var curr = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var lastSig = new TacticalBattleStateSignature(100, 12000, 11500, 3, 2, false, 98765, 0, 12); // differs
        // elapsed >= cycle -> run
        var input = new TacticalHeavyPathGate.Input(curr, 1.504f, 1.5f, lastSig, 0.003f, false);
        var dec = TacticalHeavyPathGate.Decide(input);
        AssertTrue(dec.ShouldRun, "sig change must run once floor (cycle) passed");
        AssertContains(dec.Reason, "signature-change", "reason for sig change execution");
    }

    private static void TacticalHeavyPathGateMaxIntervalForcesEvenOnStable()
    {
        var curr = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var lastSig = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12); // identical
        // no pending, same sig, but elapsed >= cycle -> force refresh (max interval)
        var input = new TacticalHeavyPathGate.Input(curr, 1.504f, 1.5f, lastSig, 0.003f, false);
        var dec = TacticalHeavyPathGate.Decide(input);
        AssertTrue(dec.ShouldRun, "max interval must force heavy even on identical signature (no starvation)");
        AssertEqual("max-interval-force", dec.Reason, "reason for max force");
    }

    private static void TacticalHeavyPathGatePendingChangeRunsAfterFloor()
    {
        var curr = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var lastSig = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12); // same now, but pending from earlier change
        var input = new TacticalHeavyPathGate.Input(curr, 1.504f, 1.5f, lastSig, 0.003f, true); // hasPending
        var dec = TacticalHeavyPathGate.Decide(input);
        AssertTrue(dec.ShouldRun, "pending change must execute once floor met");
        AssertContains(dec.Reason, "pending-change", "reason for pending execution");
    }

    private static void TacticalHeavyPathGatePendingDoesNotBypassFloor()
    {
        var curr = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var lastSig = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var input = new TacticalHeavyPathGate.Input(curr, 1.502f, 1.5f, lastSig, 0.003f, true); // pending but under floor
        var dec = TacticalHeavyPathGate.Decide(input);
        AssertFalse(dec.ShouldRun, "pending must still respect the cycleHours floor");
    }

    private static void TacticalHeavyPathGateNoRunWhenStableUnderMax()
    {
        var curr = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var lastSig = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var input = new TacticalHeavyPathGate.Input(curr, 1.502f, 1.5f, lastSig, 0.003f, false);
        var dec = TacticalHeavyPathGate.Decide(input);
        AssertFalse(dec.ShouldRun, "stable + under max interval must skip (cheap path)");
        AssertEqual("stable-under-max", dec.Reason, "reason for stable skip");
    }

    private static void TacticalHeavyPathGateRealtimeFloorThrottlesTimeCompression()
    {
        var curr = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);
        var lastSig = new TacticalBattleStateSignature(100, 12000, 11500, 3, 1, false, 98765, 0, 12);

        var compressed = new TacticalHeavyPathGate.Input(
            curr,
            currentBattleHours: 1.510f,
            lastHeavyHoursForSide: 1.500f,
            lastSignatureForSide: lastSig,
            cycleHours: 0.003f,
            hasPendingChangeForSide: false,
            currentRealtimeSeconds: 101f,
            lastHeavyRealtimeSecondsForSide: 100f,
            minRealtimeSeconds: 2f);
        var skipped = TacticalHeavyPathGate.Decide(compressed);

        AssertFalse(skipped.ShouldRun, "battle-time max must not bypass real-time floor under compression");
        AssertEqual("realtime-floor", skipped.Reason, "real-time floor reason");

        var afterFloor = new TacticalHeavyPathGate.Input(
            curr,
            currentBattleHours: 1.510f,
            lastHeavyHoursForSide: 1.500f,
            lastSignatureForSide: lastSig,
            cycleHours: 0.003f,
            hasPendingChangeForSide: false,
            currentRealtimeSeconds: 102.1f,
            lastHeavyRealtimeSecondsForSide: 100f,
            minRealtimeSeconds: 2f);
        var executed = TacticalHeavyPathGate.Decide(afterFloor);

        AssertTrue(executed.ShouldRun, "heavy path should run once both battle-time and real-time floors pass");
        AssertEqual("max-interval-force", executed.Reason, "max force still owns reason after real floor");
    }

    // ============================================================================
    // Brigade-arrival synchronization tests (2026-05-22). The pure predicate
    // for "should Main hold until supports arrive" used by the executor to
    // delay AttackObjective waypoint writes.
    // ============================================================================

    private static void ArrivalSyncHoldsMidBandCommanderWhenSupportsAreFar()
    {
        var supports = new[]
        {
            new TacticalMainArrivalSynchronizer.SupportMainPosition(900f),
            new TacticalMainArrivalSynchronizer.SupportMainPosition(1200f),
        };
        bool hold = TacticalMainArrivalSynchronizer.ShouldHoldMainForSupports(
            supports, commanderAggression01: 0.5f, holdSecondsElapsed: 5f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f);
        AssertTrue(hold, "mid-band commander should hold when all supports are beyond 500m proximity");
    }

    private static void ArrivalSyncReleasesWhenAnySupportInProximity()
    {
        var supports = new[]
        {
            new TacticalMainArrivalSynchronizer.SupportMainPosition(900f),
            new TacticalMainArrivalSynchronizer.SupportMainPosition(450f), // within threshold
        };
        bool hold = TacticalMainArrivalSynchronizer.ShouldHoldMainForSupports(
            supports, commanderAggression01: 0.5f, holdSecondsElapsed: 5f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f);
        AssertFalse(hold, "release when any single support is within the proximity threshold");
    }

    private static void ArrivalSyncBypassedForAggressiveCommander()
    {
        var supports = new[]
        {
            new TacticalMainArrivalSynchronizer.SupportMainPosition(2000f),
            new TacticalMainArrivalSynchronizer.SupportMainPosition(2500f),
        };
        bool hold = TacticalMainArrivalSynchronizer.ShouldHoldMainForSupports(
            supports, commanderAggression01: 0.85f, holdSecondsElapsed: 0f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f);
        AssertFalse(hold, "Lee/Jackson/Hood (≥ 0.7) never wait for supports");
    }

    private static void ArrivalSyncReleasesAtMaxHoldTimeout()
    {
        var supports = new[]
        {
            new TacticalMainArrivalSynchronizer.SupportMainPosition(2000f),
        };
        bool hold = TacticalMainArrivalSynchronizer.ShouldHoldMainForSupports(
            supports, commanderAggression01: 0.5f, holdSecondsElapsed: 31f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f);
        AssertFalse(hold, "release Main when timeout elapses even if supports never arrive");
    }

    private static void ArrivalSyncReleasesWhenNoSupportsAssigned()
    {
        var supports = Array.Empty<TacticalMainArrivalSynchronizer.SupportMainPosition>();
        bool hold = TacticalMainArrivalSynchronizer.ShouldHoldMainForSupports(
            supports, commanderAggression01: 0.5f, holdSecondsElapsed: 0f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f);
        AssertFalse(hold, "no supports → nothing to wait for, release immediately");
    }

    private static void ArrivalSyncSanitizesGarbageInputs()
    {
        var supports = new[]
        {
            new TacticalMainArrivalSynchronizer.SupportMainPosition(float.NaN),
            new TacticalMainArrivalSynchronizer.SupportMainPosition(2000f),
        };
        // Both supports effectively distant (NaN skipped, 2000m > threshold)
        bool hold = TacticalMainArrivalSynchronizer.ShouldHoldMainForSupports(
            supports, commanderAggression01: 0.5f, holdSecondsElapsed: 5f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f);
        AssertTrue(hold, "NaN distances are skipped; remaining distant supports still hold");

        // Inf elapsed time → release
        bool holdInf = TacticalMainArrivalSynchronizer.ShouldHoldMainForSupports(
            supports, commanderAggression01: 0.5f, holdSecondsElapsed: float.PositiveInfinity,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f);
        AssertFalse(holdInf, "Infinite elapsed time treated as timeout → release");
    }

    // ---- Arrival-sync task resolution (2026-05-25 / B wire-in) ----
    // The runtime substitutes Main's AttackObjective → Screen while holding for
    // supports so the brigade skirmishes forward (ai_stance=1) instead of
    // charging in alone. These guard the pure resolution that the leaf-cascade
    // patch calls.

    private static void ArrivalSyncResolveHoldsMainAsScreenWhenSupportsFar()
    {
        var supports = new[]
        {
            new TacticalMainArrivalSynchronizer.SupportMainPosition(900f),
            new TacticalMainArrivalSynchronizer.SupportMainPosition(1200f),
        };
        var task = TacticalMainArrivalSynchronizer.ResolveMainAttackTask(
            DirectChildRole.Main, CommandTaskType.AttackObjective, supports,
            commanderAggression01: 0.5f, holdSecondsElapsed: 5f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f,
            out bool holding);
        AssertTrue(holding, "Main holds when supports are far");
        AssertEqual(CommandTaskType.Screen, task,
            "holding Main downgrades AttackObjective → Screen (skirmish forward)");
    }

    private static void ArrivalSyncResolveReleasesMainToAttackWhenSupportsClose()
    {
        var supports = new[]
        {
            new TacticalMainArrivalSynchronizer.SupportMainPosition(300f),
            new TacticalMainArrivalSynchronizer.SupportMainPosition(1200f),
        };
        var task = TacticalMainArrivalSynchronizer.ResolveMainAttackTask(
            DirectChildRole.Main, CommandTaskType.AttackObjective, supports,
            commanderAggression01: 0.5f, holdSecondsElapsed: 5f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f,
            out bool holding);
        AssertFalse(holding, "a support within proximity releases the Main");
        AssertEqual(CommandTaskType.AttackObjective, task,
            "released Main keeps AttackObjective — advances with the line");
    }

    private static void ArrivalSyncResolveOnlyGatesMainAttackRole()
    {
        var supports = new[]
        {
            new TacticalMainArrivalSynchronizer.SupportMainPosition(900f),
        };
        // A SupportMain brigade on its own attack task is never held — only the
        // Main role is gated.
        var task = TacticalMainArrivalSynchronizer.ResolveMainAttackTask(
            DirectChildRole.SupportMain, CommandTaskType.AttackObjective, supports,
            commanderAggression01: 0.5f, holdSecondsElapsed: 5f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f,
            out bool holding);
        AssertFalse(holding, "non-Main role is never held by arrival sync");
        AssertEqual(CommandTaskType.AttackObjective, task,
            "SupportMain attack task passes through unchanged");
    }

    private static void ArrivalSyncResolvePassesThroughNonAttackTask()
    {
        var supports = new[]
        {
            new TacticalMainArrivalSynchronizer.SupportMainPosition(900f),
        };
        // A Main brigade already on a non-attack task (e.g. opportunity override
        // downgraded it to HoldObjective) is not touched.
        var task = TacticalMainArrivalSynchronizer.ResolveMainAttackTask(
            DirectChildRole.Main, CommandTaskType.HoldObjective, supports,
            commanderAggression01: 0.5f, holdSecondsElapsed: 5f,
            supportProximityThresholdMeters: 500f, maxHoldSeconds: 30f,
            out bool holding);
        AssertFalse(holding, "non-attack task is not gated");
        AssertEqual(CommandTaskType.HoldObjective, task,
            "Main on HoldObjective passes through unchanged");
    }
}
