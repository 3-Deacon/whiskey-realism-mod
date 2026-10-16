using System;
using System.IO;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using static TestHarness;
using static TestFixtures;

// BC10 (docs/superpowers/plans/2026-10-15-battle-core.md § BC10): deployment and
// movement honesty. Red first: this file does not compile before the BC10 types exist.
// Sections: arrival re-path sizing (#112 step A), terrain grid, covered crossings,
// perceived outflanking, path probe scores, road routing, fog-honest deployment,
// executor, avenues, write gate.
internal static partial class BattleMovementHonestyTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            // arrival re-path sizing (#112 step A)
            ("bc10 ShieldOnlyLiveExecutorMoves", ShieldOnlyLiveExecutorMoves),
            ("bc10 ShieldNeverPlayerOwnedOrFeud", ShieldNeverPlayerOwnedOrFeud),
            ("bc10 ShieldSkipsTopGroup", ShieldSkipsTopGroup),
            ("bc10 ShieldCoversMembersUnderAnOrderedSubGroup", ShieldCoversMembersUnderAnOrderedSubGroup),
            // terrain grid
            ("bc10 GridIndexesLikeGetCurrentTerrainOnPos", GridIndexesLikeGetCurrentTerrainOnPos),
            ("bc10 GridSampleLineCountsForestShare", GridSampleLineCountsForestShare),
            ("bc10 GridRiverImpassableUnlessFrozen", GridRiverImpassableUnlessFrozen),
            ("bc10 GridSpeedCostFallsBackToGrassForUnknownIds", GridSpeedCostFallsBackToGrassForUnknownIds),
            ("bc10 GridCrossingsAreCopiedAndBounded", GridCrossingsAreCopiedAndBounded),
            // covered crossings
            ("bc10 CoveredCrossingFromVisibleFacingEnemyWithin1_5FireRange", CoveredCrossingFromVisibleFacingEnemyWithin1_5FireRange),
            ("bc10 CoveredCrossingIgnoresHiddenEnemy", CoveredCrossingIgnoresHiddenEnemy),
            ("bc10 CoveredCrossingIgnoresArtillery", CoveredCrossingIgnoresArtillery),
            ("bc10 CoveredCrossingRememberedOnlyAboveHalfConfidence", CoveredCrossingRememberedOnlyAboveHalfConfidence),
            ("bc10 CoverPointAheadOfStandingEnemyInCover", CoverPointAheadOfStandingEnemyInCover),
            ("bc10 CoverPointNotForForestCoverOrMovingEnemy", CoverPointNotForForestCoverOrMovingEnemy),
            ("bc10 CoveredCrossingsAllocateNothingAfterWarmup", CoveredCrossingsAllocateNothingAfterWarmup),
            ("bc10 PerceptionMemoryKeepsTheRowDetailWhileVisible", PerceptionMemoryKeepsTheRowDetailWhileVisible),
            ("bc10 VanillaBlockedCrossingsListIsNeverRead", VanillaBlockedCrossingsListIsNeverRead),
            ("bc10 CoveredPointBuffersGrowInsteadOfTruncating", CoveredPointBuffersGrowInsteadOfTruncating),
            // perceived outflanking
            ("bc10 FlankCodesMatchVanillaSectors", FlankCodesMatchVanillaSectors),
            ("bc10 FlankFriendlyCloserClearsSector", FlankFriendlyCloserClearsSector),
            ("bc10 FlankFallbackModeReversesFacing", FlankFallbackModeReversesFacing),
            ("bc10 FlankIgnoresSkirmisherScoutAndMelee", FlankIgnoresSkirmisherScoutAndMelee),
            ("bc10 PerceivedFlankingReadsOnlyNeighbourCells", PerceivedFlankingReadsOnlyNeighbourCells),
            ("bc10 OwnOutflankedFromVisibleOnly", OwnOutflankedFromVisibleOnly),
            ("bc10 TargetFlankedByOwnRegimentsScreenedByVisibleFriends", TargetFlankedByOwnRegimentsScreenedByVisibleFriends),
            ("bc10 ApplyOverwritesTheRawVanillaCode", ApplyOverwritesTheRawVanillaCode),
            ("bc10 ChargeBarsOnPerceivedOutflankNotRaw", ChargeBarsOnPerceivedOutflankNotRaw),
            ("bc10 PerceivedFlankingAllocatesNothingAfterWarmup", PerceivedFlankingAllocatesNothingAfterWarmup),
            // path probe
            ("bc10 RoadShareCountsRoadAndBridgeSamples", RoadShareCountsRoadAndBridgeSamples),
            ("bc10 BridgePathIsNotRiverRisk", BridgePathIsNotRiverRisk),
            ("bc10 FordPathIsChokeNotRisk", FordPathIsChokeNotRisk),
            ("bc10 FenceOnPathIsRisk", FenceOnPathIsRisk),
            ("bc10 FrozenRiverIsNotRisk", FrozenRiverIsNotRisk),
            ("bc10 DeadGroundNeedsAThreat", DeadGroundNeedsAThreat),
            ("bc10 PathProbeUsesAQueryFilterNotTheGlobalAreaCosts", PathProbeUsesAQueryFilterNotTheGlobalAreaCosts),
            // road march routing
            ("bc10 RouteNotPlannedBelow400m", RouteNotPlannedBelow400m),
            ("bc10 RouteNotPlannedBelowWoodedShare", RouteNotPlannedBelowWoodedShare),
            ("bc10 RouteTakesRoadAroundForest", RouteTakesRoadAroundForest),
            ("bc10 RouteRejectedWhenGainBelow20Percent", RouteRejectedWhenGainBelow20Percent),
            ("bc10 RouteNeverViaWithinLast250m", RouteNeverViaWithinLast250m),
            ("bc10 RouteRiverImpassableUsesBridge", RouteRiverImpassableUsesBridge),
            ("bc10 RouteCorridorShrinksToFit6000CellsElseNotPlanned", RouteCorridorShrinksToFit6000CellsElseNotPlanned),
            ("bc10 RoutePlanAllocatesNothingAfterWarmup", RoutePlanAllocatesNothingAfterWarmup),
            ("bc10 RoutedMoveCarriesColumnFormationAndLegHeadings", RoutedMoveCarriesColumnFormationAndLegHeadings),
            ("bc10 RouteWaitsForColumnThenGoesUnrouted", RouteWaitsForColumnThenGoesUnrouted),
            ("bc10 ExecutorValidatesViaPoints", ExecutorValidatesViaPoints),
            ("bc10 DedupeComparesFinalTargetOnly", DedupeComparesFinalTargetOnly),
            ("bc10 CoveredCrossingMoveTakesNoVia", CoveredCrossingMoveTakesNoVia),
            ("bc10 RoutingIsWiredIntoThePostureAndExecutorRuntime", RoutingIsWiredIntoThePostureAndExecutorRuntime),
            ("bc10 RouteCacheIsPrunedDuringABattle", RouteCacheIsPrunedDuringABattle),
            // fog-honest deployment and reserve echelon
            ("bc10 Day1RangeOnlyIgnoresVisibleUsesEntryPoint", Day1RangeOnlyIgnoresVisibleUsesEntryPoint),
            ("bc10 FogGridAssumedEnemyVisibleThenEntryPointThenCentre", FogGridAssumedEnemyVisibleThenEntryPointThenCentre),
            ("bc10 Day1PicksObjectiveNearestAssumedEnemyNotTrueEnemy", Day1PicksObjectiveNearestAssumedEnemyNotTrueEnemy),
            ("bc10 FogGridObjectiveFilterUsesVisibleOnly", FogGridObjectiveFilterUsesVisibleOnly),
            ("bc10 RangeOnlyObjectiveFilterUsesEntryPointsOnly", RangeOnlyObjectiveFilterUsesEntryPointsOnly),
            ("bc10 LaterDaysPickNearestToGroup", LaterDaysPickNearestToGroup),
            ("bc10 KeepsVanillaPlacementWhenSameObjectiveAndFacing", KeepsVanillaPlacementWhenSameObjectiveAndFacing),
            ("bc10 DeploymentFramesRoundTripAtAnyFacing", DeploymentFramesRoundTripAtAnyFacing),
            ("bc10 SpacingCountsEveryGroupPlacedInTheCall", SpacingCountsEveryGroupPlacedInTheCall),
            ("bc10 LockedGroupRuleCoversWlParentAndPlayerFlags", LockedGroupRuleCoversWlParentAndPlayerFlags),
            ("bc10 LockedAndKeptGroupsLateInOrderAreStillRespected", LockedAndKeptGroupsLateInOrderAreStillRespected),
            ("bc10 DirectChildOfWalksToTheArmysChild", DirectChildOfWalksToTheArmysChild),
            ("bc10 LockedGroupIsNeverReplacedAndHoldsItsObjective", LockedGroupIsNeverReplacedAndHoldsItsObjective),
            ("bc10 DeploymentWritesOnlyThroughTheExecutor", DeploymentWritesOnlyThroughTheExecutor),
            ("bc10 DeploymentPatchIsPrefixAndPostfixWithScopes", DeploymentPatchIsPrefixAndPostfixWithScopes),
            ("bc10 DeployedReserveIsReadFromTheExecutorOutcome", DeployedReserveIsReadFromTheExecutorOutcome),
            // avenue flags
            ("bc10 RoadAnchoredNeedsRoadSamples", RoadAnchoredNeedsRoadSamples),
            ("bc10 CrossingAnchoredNeedsFordOrBridge", CrossingAnchoredNeedsFordOrBridge),
            ("bc10 AvenueFlagsComeFromTheGrid", AvenueFlagsComeFromTheGrid),
            ("bc10 ReserveIsFloorThirdOfEligibleChildren", ReserveIsFloorThirdOfEligibleChildren),
            ("bc10 NoReserveBelowThreeChildren", NoReserveBelowThreeChildren),
            ("bc10 ReserveNeverPlayerOwnedOrCavalryOnly", ReserveNeverPlayerOwnedOrCavalryOnly),
            ("bc10 ReserveDepth425mClampedToZoneElseKept", ReserveDepth425mClampedToZoneElseKept),
            ("bc10 ReserveClampRevertsChildUnder150m", ReserveClampRevertsChildUnder150m),
            ("bc10 ReserveChildGroupsKeepRelativeSpacing", ReserveChildGroupsKeepRelativeSpacing),
            ("bc10 DeployedReserveFromExecutorOutcomeSource", DeployedReserveFromExecutorOutcomeSource),
            ("bc10 TerrainCorrectedReserveKeepsItsReserveSource", TerrainCorrectedReserveKeepsItsReserveSource),
            ("bc10 DeployedReserveClearsWhenAnotherSourceMovesTheChild", DeployedReserveClearsWhenAnotherSourceMovesTheChild),
            ("bc10 ReleasedDeployedReserveDoesNotFlipBack", ReleasedDeployedReserveDoesNotFlipBack),
            ("bc10 DeployedReserveOutranksZoneRolesButNotTheFallbackCheck", DeployedReserveOutranksZoneRolesButNotTheFallbackCheck),
            ("bc10 DeployedReserveEvidenceGivesReserveRoleFirst", DeployedReserveEvidenceGivesReserveRoleFirst),
            ("bc10 DeployedReserveReleasedByCrisis", DeployedReserveReleasedByCrisis),
            ("bc10 DeployedReserveFlowsThroughTheEvidenceBuilder", DeployedReserveFlowsThroughTheEvidenceBuilder),
            ("bc10 ImmediatePlacementIssuesSetGroupFormationOnceAndPlacedOutcome", ImmediatePlacementIssuesSetGroupFormationOnceAndPlacedOutcome),
            ("bc10 ImmediatePlacementAcceptedBesideSamePassPlacement", ImmediatePlacementAcceptedBesideSamePassPlacement),
            ("bc10 ImmediatePlacementRefusesPlayerOwned", ImmediatePlacementRefusesPlayerOwned),
        });
    }

    // ------------------------------------------------------------------ fixtures

    // Zero allocation after warm-up, read without runtime noise: a real allocation (every call, or every
    // few calls) dirties most of eight windows of `calls` invocations, while the occasional 1-2 KB
    // bookkeeping allocation seen on the calling thread dirties one or two. Passes when at least six of
    // the eight windows allocate nothing.
    private static void AssertNoAllocation(Action work, int calls, string label)
    {
        const int windows = 8, cleanRequired = 6;
        for (int k = 0; k < 30; k++) work();
        int clean = 0;
        long worst = 0L;
        for (int window = 0; window < windows; window++)
        {
            long before = TestFixtures.BeginAllocationWindow();
            for (int k = 0; k < calls; k++) work();
            long bytes = TestFixtures.AllocatedSince(before);
            if (bytes == 0L) clean++;
            else if (bytes > worst) worst = bytes;
        }
        AssertTrue(clean >= cleanRequired, label + ": " + clean + " of " + windows + " windows were clean, worst window " + worst + " bytes");
    }

    private const int Top = 100, SubGroup = 110, Mem1 = 1, Mem2 = 2, Mem3 = 3, Outside = 9;

    // Top(100) -> SubGroup(110) -> Mem2, Mem3;  Top -> Mem1;  Outside belongs to no group.
    // Every member is a live regiment with a path (it is marching).
    private static BattleSnapshot ArrivalFixture(out int[] index)
    {
        var s = new BattleSnapshot();
        s.BeginBuild(1, 0, 0, 1, 1.0);
        int[] ids = { Top, SubGroup, Mem1, Mem2, Mem3, Outside };
        int[] parents = { -1, 0, 0, 1, 1, -1 };
        UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
        for (int i = 0; i < ids.Length; i++)
        {
            bool group = ids[i] == Top || ids[i] == SubGroup;
            s.AddOwn(new UnitRecord
            {
                InstanceId = ids[i], UnitTyp = group ? TacticalUnitType.BattleGroupBrigade : TacticalUnitType.Infantry,
                RegimentPaths = 1, X = 100f * i, Z = -100f,
                Flags = live | (group ? UnitFlags.IsGroup : UnitFlags.IsInfantry),
            });
        }
        for (int i = 0; i < ids.Length; i++) s.Own[i].ParentIndex = parents[i];
        s.IndexCommandTree(null);
        index = new int[ids.Length];
        for (int i = 0; i < ids.Length; i++) index[i] = s.IndexOfOwn(ids[i]);
        return s;
    }

    private static readonly IssuedTarget[] _issued = new IssuedTarget[1];

    // A Move the executor issued to the unit (and recorded as Issued).
    private static void Ordered(OrderExecutor executor, int unitId, double now = 1.0)
    {
        _issued[0] = new IssuedTarget { InstanceId = unitId, X = 500f, Z = -500f };
        var intent = new BattleOrderIntent
        {
            GroupId = unitId, Channel = OrderChannel.Move, TargetX = 500f, TargetZ = -500f, Heading = -1f,
            Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Task = CommandTaskType.AttackObjective, Reason = "test",
        };
        executor.RecordIssued(intent, now, _issued, 1, null, 0, 0, 1);
    }

    // ------------------------------------------------------------------ arrival re-path sizing (#112 step A)

    // ArrivalShield.Select names the arrived members of an arriving group that a Whiskey
    // order is walking (the ones vanilla's entry re-path, SetInitialPathForEntryPoint
    // 87662, would overwrite): the member or a command ancestor below the arriving group
    // holds a live executor Move and the member still has a path.
    private static void ShieldOnlyLiveExecutorMoves()
    {
        BattleSnapshot s = ArrivalFixture(out int[] ix);
        var executor = new OrderExecutor();
        var into = new int[8];

        AssertEqual(0, ArrivalShield.Select(s, ix[0], executor, into), "no executor move: nothing ordered");

        Ordered(executor, Mem1);
        int n = ArrivalShield.Select(s, ix[0], executor, into);
        AssertEqual(1, n, "one ordered member");
        AssertEqual(ix[2], into[0], "member 1");

        // The order has finished (no path left): vanilla's re-path overwrites nothing of ours.
        s.Own[ix[2]].RegimentPaths = 0;
        AssertEqual(0, ArrivalShield.Select(s, ix[0], executor, into), "a finished move is not live");
        s.Own[ix[2]].RegimentPaths = 1;

        // A failed outcome is not a live order.
        _issued[0] = new IssuedTarget { InstanceId = Mem1, X = 500f, Z = -500f };
        var ex2 = new OrderExecutor();
        var intent = new BattleOrderIntent
        {
            GroupId = Mem1, Channel = OrderChannel.Move, TargetX = 500f, TargetZ = -500f, Heading = -1f,
            Priority = IntentPriority.Executor, Source = IntentSource.PostureExecutor, Task = CommandTaskType.AttackObjective, Reason = "test",
        };
        ex2.RecordIssued(intent, 1.0, _issued, 1, null, 0, 0, 0); // paths did not rise
        var outcomes = new OrderOutcome[4];
        s.Own[ix[2]].RegimentPaths = 0; // nothing moves: the next observation reads Failed
        ex2.ObserveOutcomes(s, 1.001, outcomes, out int count);
        s.Own[ix[2]].RegimentPaths = 1;
        AssertTrue(count > 0, "the observation produced outcomes");
        AssertEqual(0, ArrivalShield.Select(s, ix[0], ex2, into), "a Failed or GaveUp Move is no live order");
    }

    private static void ShieldNeverPlayerOwnedOrFeud()
    {
        BattleSnapshot s = ArrivalFixture(out int[] ix);
        var executor = new OrderExecutor();
        var into = new int[8];
        Ordered(executor, Mem1);
        Ordered(executor, Mem2);
        Ordered(executor, Mem3);
        s.Own[ix[2]].Flags |= UnitFlags.PlayerOwned;
        s.Own[ix[3]].Flags |= UnitFlags.Feud;
        int n = ArrivalShield.Select(s, ix[0], executor, into);
        AssertEqual(1, n, "only the AI, non-feuding member remains");
        AssertEqual(ix[4], into[0], "member 3");
    }

    private static void ShieldSkipsTopGroup()
    {
        BattleSnapshot s = ArrivalFixture(out int[] ix);
        var executor = new OrderExecutor();
        var into = new int[8];
        Ordered(executor, Top);
        AssertEqual(0, ArrivalShield.Select(s, ix[0], executor, into), "vanilla re-paths the top group itself unconditionally: never selected");
        Ordered(executor, Outside);
        AssertEqual(0, ArrivalShield.Select(s, ix[0], executor, into), "a unit outside the arriving group is not a member");
    }

    private static void ShieldCoversMembersUnderAnOrderedSubGroup()
    {
        BattleSnapshot s = ArrivalFixture(out int[] ix);
        var executor = new OrderExecutor();
        var into = new int[8];
        Ordered(executor, SubGroup);
        int n = ArrivalShield.Select(s, ix[0], executor, into);
        AssertEqual(3, n, "the ordered sub-group and both regiments under it");
        AssertTrue(Array.IndexOf(into, ix[1], 0, n) >= 0 && Array.IndexOf(into, ix[3], 0, n) >= 0 && Array.IndexOf(into, ix[4], 0, n) >= 0,
            "sub-group, member 2 and member 3");
        AssertTrue(Array.IndexOf(into, ix[2], 0, n) < 0, "member 1 hangs off the top group, not the ordered sub-group");
        AssertEqual(1, ArrivalShield.Select(s, ix[0], executor, new int[1]), "the output buffer bounds the count");
    }
}
