using System;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;

// WS T3 — battle writer & gate patch fixes. Every test pins a pure decision
// extracted from a Harmony patch to the vanilla behavior it must respect.
internal static class BattleWriterFixTests
{
    public static void Run()
    {
        MacroAiNeverWritesRetreat();
        MacroAiDefersToScriptedOverrides();
        MacroAiSkipsFeudInstanceAndMonitorModes();
        MacroAiSkipsVanillaRetreatAndEndOfBattle();
        MacroAiPrefersLedgerOverDoctrineOpinion();
        MacroAiSkipsWhenAlreadyAligned();
        MacroAiEarlyGateMatchesResolver();
        MacroAiOnWlPlayerSideOnlyWritesGenuineLedgerChange();
        VanillaAnglePointMatchesGetAngleTerrainPos();
        VanillaAnglePointXYMatchesGetAngleTerrainPosXY();
        B8MapsWithdrawalDecisionsToVanillaFallbackShapes();
        B8SkipsUnitsVanillaWouldNotFallBack();
        ReserveGateMirrorsVanillaEndangeredFilter();
        ReserveGateSnapshotsOnlyPossibleSupportUnits();
        ReserveGateRollbackOnlyWhenStopRegimentIsReversible();
        PathfinderNeverRemovesAddedPaths();
        PathfinderEndsRetryLoopOnlyWithinTolerance();
        VetoSkipsPlayerUiOrdersAndNonAiUnits();
        VetoMeasuresEnemyDistanceToTarget();
        ArtilleryMicroNeverInterruptsMovement();
        ArtilleryMicroMirrorsVanillaUnlimberRule();
        ArtilleryMicroMapsActionsToVanillaWrites();
        CounterBatteryReleasedWhenTargetGone();
        ChargeGateUsesVanillaPerformAiActionRule();
        ChargeGateAllowsCounterChargeAgainstChargingEnemy();
        FeudGateRestoresVanillaDistanceCap();
        GatesFailOpenOutsideActiveMode();
        ObjectiveChainCycleGateMatchesVanilla();
        ObjectiveChainMovementGateMatchesVanilla();
        DeploymentSkipsVanillaFortifiedPlacement();
        DeploymentSkipsFleetsAndMissingZones();
    }

    private static void Pass(string name)
    {
        Console.WriteLine("PASS " + name);
    }

    private static int Macro(
        int ledger = -1,
        int doctrine = -1,
        int current = -1,
        bool modeAllowsWrites = true,
        bool feud = false,
        bool aiStrategy = false,
        bool scriptedSide = false,
        bool endBattle = false,
        bool wlPlayerSide = false,
        bool wlPlayerIsCic = false,
        int pending = -1)
    {
        return BattleWriterPolicy.ResolveMacroAiWrite(new MacroAiWriteInput(
            modeAllowsWrites,
            feud,
            aiStrategy,
            scriptedSide,
            endBattle,
            current,
            ledger,
            doctrine,
            wlPlayerSide,
            wlPlayerIsCic,
            pending));
    }

    // W&L: aibattle[1] (player side) runs the macro AI; its macroai drives
    // dispatches to the player (2 -> type-12 defend orders, CheckGlobalAIStrategy
    // 6447-6466). Only a planned-operation opinion that differs from both the
    // current and vanilla's pending newmacroaitoset is written there, and
    // nothing when the player is CIC (every unit on the side is player-owned).
    private static void MacroAiOnWlPlayerSideOnlyWritesGenuineLedgerChange()
    {
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(doctrine: 2, current: 1, wlPlayerSide: true), "doctrine fallback ignored");
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 2, current: 1, pending: 2, wlPlayerSide: true), "vanilla already heading there");
        TestHarness.AssertEqual(1, Macro(ledger: 1, current: 2, pending: 2, wlPlayerSide: true), "genuine ledger change");
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 1, current: 2, wlPlayerSide: true, wlPlayerIsCic: true), "player CIC");
        TestHarness.AssertEqual(2, Macro(doctrine: 2, current: 1, pending: 2), "AI side keeps the doctrine fallback");
        Pass("battle writers: macroai on the W&L player side only writes a genuine ledger change");
    }

    // CheckGlobalAIStrategy 6355/6362: startoffallback is stamped only on
    // vanilla's own transition to 3; a foreign 3 ends the battle next cycle.
    private static void MacroAiNeverWritesRetreat()
    {
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 3), "ledger 3 is no opinion");
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(doctrine: 3), "doctrine 3 is no opinion");
        TestHarness.AssertEqual(2, Macro(ledger: 3, doctrine: 2), "ledger 3 falls through to doctrine 2");
        Pass("battle writers: macroai never writes retreat 3");
    }

    // 6378 aistrategy >= 0 and 6383 sideinformation.macroai >= 0 are scripted overrides.
    private static void MacroAiDefersToScriptedOverrides()
    {
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 1, aiStrategy: true), "aistrategy override");
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 1, scriptedSide: true), "scripted side macro");
        Pass("battle writers: macroai defers to scripted aistrategy and side macro");
    }

    private static void MacroAiSkipsFeudInstanceAndMonitorModes()
    {
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 1, feud: true), "feud instance");
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 1, modeAllowsWrites: false), "mode monitor/off");
        Pass("battle writers: macroai skips feud instance and non-Active mode");
    }

    // 6362/6374: once vanilla is at 3 (or the battle is ending) nothing else may run.
    private static void MacroAiSkipsVanillaRetreatAndEndOfBattle()
    {
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 1, current: 3), "vanilla retreat");
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 1, endBattle: true), "end of battle");
        Pass("battle writers: macroai skips vanilla retreat and end of battle");
    }

    private static void MacroAiPrefersLedgerOverDoctrineOpinion()
    {
        TestHarness.AssertEqual(1, Macro(ledger: 1, doctrine: 2), "ledger wins");
        TestHarness.AssertEqual(2, Macro(ledger: -1, doctrine: 2), "doctrine fallback");
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(), "no opinion leaves vanilla");
        Pass("battle writers: macroai prefers ledger then doctrine opinion");
    }

    private static void MacroAiSkipsWhenAlreadyAligned()
    {
        TestHarness.AssertEqual(BattleWriterPolicy.NoMacroWrite, Macro(ledger: 2, current: 2), "aligned");
        TestHarness.AssertEqual(0, Macro(ledger: 0, current: 2), "changed");
        Pass("battle writers: macroai skips writes when vanilla already aligned");
    }

    private static void MacroAiEarlyGateMatchesResolver()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.MacroAiMayHaveOpinion(true, false, false, false, false, 1), "open gate");
        TestHarness.AssertFalse(BattleWriterPolicy.MacroAiMayHaveOpinion(false, false, false, false, false, 1), "mode");
        TestHarness.AssertFalse(BattleWriterPolicy.MacroAiMayHaveOpinion(true, true, false, false, false, 1), "feud");
        TestHarness.AssertFalse(BattleWriterPolicy.MacroAiMayHaveOpinion(true, false, true, false, false, 1), "aistrategy");
        TestHarness.AssertFalse(BattleWriterPolicy.MacroAiMayHaveOpinion(true, false, false, true, false, 1), "scripted side");
        TestHarness.AssertFalse(BattleWriterPolicy.MacroAiMayHaveOpinion(true, false, false, false, true, 1), "end battle");
        TestHarness.AssertFalse(BattleWriterPolicy.MacroAiMayHaveOpinion(true, false, false, false, false, 3), "vanilla retreat");
        Pass("battle writers: macroai early gate matches resolver gates");
    }

    private static TacticalWithdrawalDoctrine.FallbackUnitState FallbackUnit(
        int unitTyp = 0,
        bool routed = false,
        bool markedForRout = false,
        bool detached = false,
        bool onRetreat = false,
        int paths = 0,
        int movementMode = 0,
        bool chargeTarget = false,
        float inMelee = 0f)
    {
        return new TacticalWithdrawalDoctrine.FallbackUnitState(
            unitTyp, routed, markedForRout, detached, onRetreat, paths, movementMode, chargeTarget, inMelee);
    }

    // R1: never SetWithdrawal in battle (116116 sets onretreat; SetWaypoint
    // returns early on onretreat at 91361). Use vanilla's own tactical
    // fallbacks: CheckLineFallbacks 5118 (mode 2) and MicroAICheckForRetreats
    // 4825 (retreat distance).
    private static void B8MapsWithdrawalDecisionsToVanillaFallbackShapes()
    {
        var unit = FallbackUnit();
        TestHarness.AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.Fallback,
            TacticalWithdrawalDoctrine.PlanTacticalFallback(TacticalWithdrawalDoctrine.Decision.RearGuard, unit), "rear guard -> fallback");
        TestHarness.AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.Retreat,
            TacticalWithdrawalDoctrine.PlanTacticalFallback(TacticalWithdrawalDoctrine.Decision.FullRetreat, unit), "full retreat -> retreat");
        TestHarness.AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            TacticalWithdrawalDoctrine.PlanTacticalFallback(TacticalWithdrawalDoctrine.Decision.Screen, unit), "screen -> none");
        TestHarness.AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.None,
            TacticalWithdrawalDoctrine.PlanTacticalFallback(TacticalWithdrawalDoctrine.Decision.HoldLine, unit), "hold -> none");
        Pass("battle writers: B8 maps withdrawal decisions to vanilla fallback shapes");
    }

    // CheckLineFallbacks 5132: infantry/cavalry, movementmode 0, not routed,
    // no charge target, no paths, not in melee.
    private static void B8SkipsUnitsVanillaWouldNotFallBack()
    {
        var d = TacticalWithdrawalDoctrine.Decision.FullRetreat;
        var none = TacticalWithdrawalDoctrine.FallbackKind.None;
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(unitTyp: 2)), "artillery");
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(routed: true)), "routed");
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(markedForRout: true)), "marked for rout");
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(detached: true)), "detached");
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(onRetreat: true)), "on retreat");
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(paths: 1)), "has paths");
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(movementMode: 4)), "retreating");
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(movementMode: 2)), "falling back");
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(chargeTarget: true)), "charging");
        TestHarness.AssertEqual(none, TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(inMelee: 0.5f)), "in melee");
        TestHarness.AssertEqual(TacticalWithdrawalDoctrine.FallbackKind.Retreat,
            TacticalWithdrawalDoctrine.PlanTacticalFallback(d, FallbackUnit(unitTyp: 1)), "cavalry eligible");
        Pass("battle writers: B8 skips units vanilla would not fall back");
    }

    // CheckUseOfReserves 6093-6113: vanilla commits only when an attached
    // unit (unittyp <= 2, no paths, not routed) is outflanked on a side its
    // own flank unit doesn't cover; otherwise it returns before any write.
    private static void ReserveGateMirrorsVanillaEndangeredFilter()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.IsEndangeredForReserveSupport(0, 0, false, 1, 0), "left outflank");
        TestHarness.AssertTrue(BattleWriterPolicy.IsEndangeredForReserveSupport(2, 0, false, 6, 1), "right outflank (6)");
        TestHarness.AssertFalse(BattleWriterPolicy.IsEndangeredForReserveSupport(0, 0, false, 1, 1), "left covered by own flank");
        TestHarness.AssertFalse(BattleWriterPolicy.IsEndangeredForReserveSupport(0, 0, false, 2, 2), "right covered by own flank");
        TestHarness.AssertFalse(BattleWriterPolicy.IsEndangeredForReserveSupport(0, 0, false, 3, 0), "outflanked code 3 not handled by vanilla");
        TestHarness.AssertFalse(BattleWriterPolicy.IsEndangeredForReserveSupport(14, 0, false, 1, 0), "group unit");
        TestHarness.AssertFalse(BattleWriterPolicy.IsEndangeredForReserveSupport(0, 1, false, 1, 0), "moving");
        TestHarness.AssertFalse(BattleWriterPolicy.IsEndangeredForReserveSupport(0, 0, true, 1, 0), "routed");
        TestHarness.AssertFalse(BattleWriterPolicy.IsEndangeredForReserveSupport(0, 0, false, 0, 0), "not outflanked");
        Pass("battle writers: #59 mirrors vanilla endangered-unit filter");
    }

    // CheckUseOfReserves 6121: only unittyp <= 1, idle, not routed, not
    // outflanked, not engaged units can be sent as support.
    private static void ReserveGateSnapshotsOnlyPossibleSupportUnits()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.IsReserveSupportCandidate(0, 0, false, 0, false), "idle infantry");
        TestHarness.AssertTrue(BattleWriterPolicy.IsReserveSupportCandidate(1, 0, false, -1, false), "idle cavalry");
        TestHarness.AssertFalse(BattleWriterPolicy.IsReserveSupportCandidate(2, 0, false, 0, false), "artillery");
        TestHarness.AssertFalse(BattleWriterPolicy.IsReserveSupportCandidate(0, 2, false, 0, false), "moving");
        TestHarness.AssertFalse(BattleWriterPolicy.IsReserveSupportCandidate(0, 0, true, 0, false), "routed");
        TestHarness.AssertFalse(BattleWriterPolicy.IsReserveSupportCandidate(0, 0, false, 1, false), "outflanked");
        TestHarness.AssertFalse(BattleWriterPolicy.IsReserveSupportCandidate(0, 0, false, 0, true), "engaged");
        Pass("battle writers: #59 snapshots only possible support units");
    }

    // RegimentSetPath(newpath) calls StopRegiment(manualstop) (130836), which
    // drops pontoon/obstacle construction and re-forms out of march column via
    // ChangeRegimentFormation; a field snapshot cannot undo those.
    private static void ReserveGateRollbackOnlyWhenStopRegimentIsReversible()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.ReserveRollbackIsSafe(false, false, 0, 0), "plain unit");
        TestHarness.AssertFalse(BattleWriterPolicy.ReserveRollbackIsSafe(true, false, 0, 0), "pontoon builder");
        TestHarness.AssertFalse(BattleWriterPolicy.ReserveRollbackIsSafe(false, true, 0, 0), "obstacle builder");
        TestHarness.AssertFalse(BattleWriterPolicy.ReserveRollbackIsSafe(false, false, 2, 0), "formation change pending");
        Pass("battle writers: #59 rolls back only when StopRegiment side effects are reversible");
    }

    private static ObjectiveChainMovementProbe Chain(
        bool hasCenter = true,
        int objectives = 1,
        bool firstObjective = true,
        float timeToMove = 5f,
        float nowHours = 10f,
        int centerTyp = 14,
        int centerPaths = 0,
        float subsMovingNotFar = 0f,
        int groupPositionAttached = 3,
        bool pathInterrupted = false,
        bool blockedOrderRunning = false,
        bool orderActive = false,
        float engagedNonAri = 0f,
        bool assaultMove = false,
        bool ableToFight = true)
    {
        return new ObjectiveChainMovementProbe(
            hasCenter, objectives, firstObjective, timeToMove, nowHours, centerTyp, centerPaths,
            subsMovingNotFar, groupPositionAttached, pathInterrupted, blockedOrderRunning,
            orderActive, engagedNonAri, assaultMove, ableToFight);
    }

    // UpdateMovingTargets 6872: state < 4 || macroai == 3 || macroai == -1 returns.
    private static void ObjectiveChainCycleGateMatchesVanilla()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.ObjectiveChainCycleActive(5, 1), "active");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainCycleActive(3, 1), "state < 4");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainCycleActive(5, 3), "retreat");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainCycleActive(5, -1), "unset macro");
        Pass("battle writers: #46 cycle gate matches UpdateMovingTargets");
    }

    // UpdateMovingTargets 6877-6936: the per-chain continue conditions that
    // precede the movement branch and CheckCurrentOrderUpdate.
    private static void ObjectiveChainMovementGateMatchesVanilla()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain()), "ready group");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(hasCenter: false)), "no center");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(objectives: 0)), "no objectives");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(timeToMove: 11f)), "not yet time");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(subsMovingNotFar: 0.5f)), "group moving");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(centerTyp: 0, centerPaths: 1)), "regiment moving");
        TestHarness.AssertTrue(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(centerTyp: 0, subsMovingNotFar: 0.9f)), "regiment ignores group moving flag");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(groupPositionAttached: 0)), "empty group");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(pathInterrupted: true)), "path interrupted");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(blockedOrderRunning: true)), "blocked order");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(orderActive: true)), "order active");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(engagedNonAri: 0.1f)), "engaged");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(assaultMove: true)), "assault move");
        TestHarness.AssertFalse(BattleWriterPolicy.ObjectiveChainWouldReachMovement(Chain(ableToFight: false)), "unable to fight");
        Pass("battle writers: #46 movement gate matches UpdateMovingTargets");
    }

    private static void AssertNear(float expected, float actual, string label)
    {
        TestHarness.AssertTrue(Math.Abs(expected - actual) < 0.001f, label + ": expected " + expected + " got " + actual);
    }

    // BattlefieldSetup.GetAngleTerrainPosStatic 26891: angle -= 90;
    // x += cos(a) * d; z -= sin(a) * d.
    private static void VanillaAnglePointMatchesGetAngleTerrainPos()
    {
        BattleWriterPolicy.AnglePointXZ(100f, -200f, 90f, 50f, out float x, out float z);
        AssertNear(150f, x, "angle 90 x");
        AssertNear(-200f, z, "angle 90 z");
        BattleWriterPolicy.AnglePointXZ(100f, -200f, 0f, 50f, out x, out z);
        AssertNear(100f, x, "angle 0 x");
        AssertNear(-150f, z, "angle 0 z");
        Pass("battle writers: angle point matches vanilla GetAngleTerrainPos");
    }

    // BattlefieldSetup.GetAngleTerrainPosXYStatic 26937: the distancez term
    // points opposite to GetAngleTerrainPos, so SetGroupFormation's HQ
    // (targetpos + (0, depth + back), 92044) sits behind the line front.
    private static void VanillaAnglePointXYMatchesGetAngleTerrainPosXY()
    {
        BattleWriterPolicy.AnglePointXYXZ(100f, -200f, 0f, 0f, 30f, out float x, out float z);
        AssertNear(100f, x, "xy angle 0 x");
        AssertNear(-230f, z, "xy angle 0 z (behind)");
        BattleWriterPolicy.AnglePointXYXZ(100f, -200f, 0f, 10f, 0f, out x, out z);
        AssertNear(90f, x, "xy lateral x (positive distancex is to the left)");
        AssertNear(-200f, z, "xy lateral z");
        BattleWriterPolicy.DeploymentLineFrontFromHq(100f, -230f, 0f, 20f, 10f, out x, out z);
        AssertNear(100f, x, "front x");
        AssertNear(-200f, z, "front z = hq moved forward by depth + back");
        Pass("battle writers: angle XY point matches vanilla GetAngleTerrainPosXY");
    }

    // Regiment.AddPath 130511: partial/invalid paths are accepted only once
    // iterations >= 15 (deliberate); on endpoint mismatch the segment is kept
    // and RegimentSetPath (131167-131178) chains from its last corner.
    private static void PathfinderNeverRemovesAddedPaths()
    {
        var partial = BattleWriterPolicy.DecideAddPath(pathAdded: true, cornerCount: 8, vanillaResult: 0, finalDistance: 80f, tolerance: 5f);
        TestHarness.AssertFalse(partial.OverrideResult, "far endpoint keeps vanilla result");
        TestHarness.AssertEqual("vanilla-chain", partial.Reason, "far endpoint reason");
        var none = BattleWriterPolicy.DecideAddPath(pathAdded: false, cornerCount: 0, vanillaResult: 0, finalDistance: 0f, tolerance: 5f);
        TestHarness.AssertFalse(none.OverrideResult, "no added path");
        var accepted = BattleWriterPolicy.DecideAddPath(pathAdded: true, cornerCount: 8, vanillaResult: 1, finalDistance: 0f, tolerance: 5f);
        TestHarness.AssertFalse(accepted.OverrideResult, "vanilla accepted");
        Pass("battle writers: #53 never removes added path segments");
    }

    // Exact float endpoint equality (130517) never matches a navmesh-snapped
    // target, so vanilla burns all 20 RegimentSetPath iterations; a close
    // endpoint ends the loop.
    private static void PathfinderEndsRetryLoopOnlyWithinTolerance()
    {
        var near = BattleWriterPolicy.DecideAddPath(pathAdded: true, cornerCount: 8, vanillaResult: 0, finalDistance: 1.25f, tolerance: 5f);
        TestHarness.AssertTrue(near.OverrideResult, "near endpoint overrides");
        TestHarness.AssertEqual(1, near.Result, "near endpoint result");
        var noCorners = BattleWriterPolicy.DecideAddPath(pathAdded: true, cornerCount: 0, vanillaResult: 0, finalDistance: 0f, tolerance: 5f);
        TestHarness.AssertFalse(noCorners.OverrideResult, "no corners never overrides");
        Pass("battle writers: #53 ends the retry loop only within endpoint tolerance");
    }

    private static bool Veto(
        int unitTyp = 2,
        int guns = 4,
        bool routed = false,
        bool detached = false,
        bool showMovementOptions = false,
        bool canWrite = true,
        float nearestEnemyToTargetSq = 30f * 30f)
    {
        return BattleWriterPolicy.ShouldVetoArtilleryWaypoint(
            unitTyp, guns, routed, detached, showMovementOptions, canWrite, nearestEnemyToTargetSq, 60f);
    }

    // BR-C2: SetWaypoint's showmovementoptions defaults true for UI orders;
    // every vanilla AI call site passes false (e.g. 3139, 4146, 6235, 6959).
    private static void VetoSkipsPlayerUiOrdersAndNonAiUnits()
    {
        TestHarness.AssertTrue(Veto(), "AI battery ordered to point blank");
        TestHarness.AssertFalse(Veto(showMovementOptions: true), "player UI order");
        TestHarness.AssertFalse(Veto(canWrite: false), "guard denies (player/W&L/mode)");
        TestHarness.AssertFalse(Veto(unitTyp: 0), "infantry");
        TestHarness.AssertFalse(Veto(guns: 0), "no guns");
        TestHarness.AssertFalse(Veto(routed: true), "routed");
        TestHarness.AssertFalse(Veto(detached: true), "detached");
        Pass("battle writers: veto skips player UI orders and non-AI units");
    }

    private static void VetoMeasuresEnemyDistanceToTarget()
    {
        TestHarness.AssertFalse(Veto(nearestEnemyToTargetSq: 61f * 61f), "target beyond point blank");
        TestHarness.AssertFalse(Veto(nearestEnemyToTargetSq: float.MaxValue), "no visible enemy");
        TestHarness.AssertTrue(Veto(nearestEnemyToTargetSq: 59f * 59f), "target inside point blank");
        Pass("battle writers: veto measures nearest visible enemy to the target");
    }

    private static ArtilleryMicroWrite Micro(
        TacticalArtilleryMicroAction action,
        int mounted = 0,
        int paths = 0,
        int movementMode = 0,
        int combatBehaviorOrdered = 7,
        bool enemyInFireRange = true)
    {
        return BattleWriterPolicy.PlanArtilleryMicroWrite(action, mounted, paths, movementMode, combatBehaviorOrdered, enemyInFireRange);
    }

    // BR-H3/B7 thrash: RotateRegiment and RegimentSetPath(newpath) both run
    // StopRegiment (130165, 130836); vanilla's artillery micro (3482, 3513)
    // only acts on idle batteries.
    private static void ArtilleryMicroNeverInterruptsMovement()
    {
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.Unlimber, mounted: 1, paths: 2), "unlimber while moving");
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.Retreat, mounted: 1, paths: 1), "retreat while moving");
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.Retreat, mounted: 1, movementMode: 4), "retreat while retreating");
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.WheelToBestThreat, paths: 1), "wheel while moving");
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.Limber, paths: 1), "limber while moving");
        Pass("battle writers: artillery micro never interrupts battery movement");
    }

    // UnlimberArtilleryAIMicro 3482: mounted == 1, no paths, enemy within fire range.
    private static void ArtilleryMicroMirrorsVanillaUnlimberRule()
    {
        TestHarness.AssertEqual(ArtilleryMicroWrite.UnlimberAndFace, Micro(TacticalArtilleryMicroAction.Unlimber, mounted: 1), "unlimber in range");
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.Unlimber, mounted: 1, enemyInFireRange: false), "no enemy in fire range");
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.Unlimber, mounted: 0), "already unlimbered");
        Pass("battle writers: artillery unlimber mirrors vanilla rule");
    }

    private static void ArtilleryMicroMapsActionsToVanillaWrites()
    {
        TestHarness.AssertEqual(ArtilleryMicroWrite.Limber, Micro(TacticalArtilleryMicroAction.Limber, mounted: 0), "limber");
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.Limber, mounted: 1), "already limbered");
        TestHarness.AssertEqual(ArtilleryMicroWrite.Retreat, Micro(TacticalArtilleryMicroAction.Retreat, mounted: 1), "retreat");
        TestHarness.AssertEqual(ArtilleryMicroWrite.Face, Micro(TacticalArtilleryMicroAction.WheelToBestThreat), "wheel");
        TestHarness.AssertEqual(ArtilleryMicroWrite.CeaseBombard, Micro(TacticalArtilleryMicroAction.ConserveAmmo, combatBehaviorOrdered: 9), "conserve cancels bombard");
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.ConserveAmmo, combatBehaviorOrdered: 7), "conserve no bombard");
        TestHarness.AssertEqual(ArtilleryMicroWrite.None, Micro(TacticalArtilleryMicroAction.Fire), "fire is vanilla");
        Pass("battle writers: artillery micro maps actions to vanilla writes");
    }

    // CheckCounterBatteryFire 3834 skips combatbehaviorordered == 8, so its own
    // 8 -> 7 release branch is unreachable; CheckAIBombardment 3890 skips 8 too.
    private static void CounterBatteryReleasedWhenTargetGone()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.ShouldReleaseCounterBattery(8, false), "no enemy artillery in fire range");
        TestHarness.AssertFalse(BattleWriterPolicy.ShouldReleaseCounterBattery(8, true), "enemy artillery still in range");
        TestHarness.AssertFalse(BattleWriterPolicy.ShouldReleaseCounterBattery(7, false), "not counter-battery");
        Pass("battle writers: counter-battery released when the target is gone");
    }

    // AIBattle.PerformAIActionDLCWL 5101: outside W&L always; a unit of a group
    // whose ai_stance >= 1 (player delegated to the AI) is allowed even when
    // under the player's command; otherwise dlcw_isundercommander blocks.
    private static void ChargeGateUsesVanillaPerformAiActionRule()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.WlAllowsUnitAction(false, 0, false, true), "W&L inactive");
        TestHarness.AssertTrue(BattleWriterPolicy.WlAllowsUnitAction(true, 4, true, true), "delegated assault stance");
        TestHarness.AssertFalse(BattleWriterPolicy.WlAllowsUnitAction(true, 0, true, true), "player-held stance");
        TestHarness.AssertFalse(BattleWriterPolicy.WlAllowsUnitAction(true, 4, false, true), "other group");
        TestHarness.AssertTrue(BattleWriterPolicy.WlAllowsUnitAction(true, 0, false, false), "AI unit");
        Pass("battle writers: #41 W&L rule mirrors vanilla PerformAIActionDLCWL");
    }

    private static void ChargeGateAllowsCounterChargeAgainstChargingEnemy()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.IsCounterCharge(true, 3, false, 0f), "enemy charging (3)");
        TestHarness.AssertTrue(BattleWriterPolicy.IsCounterCharge(true, 6, false, 0f), "enemy bayonet charge (6)");
        TestHarness.AssertTrue(BattleWriterPolicy.IsCounterCharge(false, 0, false, 0.4f), "already in melee");
        TestHarness.AssertFalse(BattleWriterPolicy.IsCounterCharge(true, 3, true, 0f), "routed enemy is a pursuit, not a counter");
        TestHarness.AssertFalse(BattleWriterPolicy.IsCounterCharge(true, 1, false, 0f), "enemy advancing");
        TestHarness.AssertFalse(BattleWriterPolicy.IsCounterCharge(false, 3, false, 0f), "no visible enemy");
        Pass("battle writers: #41 allows counter-charges against a charging enemy");
    }

    // CheckForFeudGroupActions 4952: GetClosestEnemyUnit(neededdistancefeudgroupmovement).
    private static void FeudGateRestoresVanillaDistanceCap()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.FeudMoveTargetAllowed(true, 300f, 400f), "inside cap");
        TestHarness.AssertFalse(BattleWriterPolicy.FeudMoveTargetAllowed(true, 900f, 400f), "beyond cap");
        TestHarness.AssertFalse(BattleWriterPolicy.FeudMoveTargetAllowed(false, 0f, 400f), "no enemy");
        Pass("battle writers: #42 restores vanilla feud distance cap");
    }

    // R14: Commander Mode Off/MonitorOnly means no Whiskey writes and no
    // Whiskey denials - gates hand the call back to vanilla.
    private static void GatesFailOpenOutsideActiveMode()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.GateMayIntervene(true, true), "active + enabled");
        TestHarness.AssertFalse(BattleWriterPolicy.GateMayIntervene(false, true), "monitor/off");
        TestHarness.AssertFalse(BattleWriterPolicy.GateMayIntervene(true, false), "flag off");
        Pass("battle writers: gates fail open to vanilla outside Active mode");
    }

    // DoPlacementAIUnitsWithinDeploymentzoneNew 85684-85751: num3 == 1
    // (defender battle type 2, or attacker type 1 with groupmorale <=
    // minimummoraleforaistances[1]) with >= 3 eligible sub-units places the
    // group on fortification lines with ignoredeplyomentzone: true.
    private static void DeploymentSkipsVanillaFortifiedPlacement()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.WouldTakeFortifiedDeploymentPath(2, 0.9f, 0.4f, 3), "defender");
        TestHarness.AssertTrue(BattleWriterPolicy.WouldTakeFortifiedDeploymentPath(1, 0.3f, 0.4f, 5), "shaken attacker");
        TestHarness.AssertFalse(BattleWriterPolicy.WouldTakeFortifiedDeploymentPath(1, 0.9f, 0.4f, 5), "attacker");
        TestHarness.AssertFalse(BattleWriterPolicy.WouldTakeFortifiedDeploymentPath(2, 0.9f, 0.4f, 2), "too few units -> num3 = 2");
        TestHarness.AssertFalse(BattleWriterPolicy.WouldTakeFortifiedDeploymentPath(0, 0.9f, 0.4f, 5), "meeting");
        Pass("battle writers: #60 leaves vanilla fortified-line placement alone");
    }

    private static void DeploymentSkipsFleetsAndMissingZones()
    {
        TestHarness.AssertTrue(BattleWriterPolicy.DeploymentCorrectionAllowed(14, 4, false), "brigade with zone");
        TestHarness.AssertFalse(BattleWriterPolicy.DeploymentCorrectionAllowed(17, 4, false), "fleet");
        TestHarness.AssertFalse(BattleWriterPolicy.DeploymentCorrectionAllowed(14, 1, false), "frontlinevector.Count < 2");
        TestHarness.AssertFalse(BattleWriterPolicy.DeploymentCorrectionAllowed(14, 4, true), "fortified path");
        Pass("battle writers: #60 skips fleets, missing zones and fortified groups");
    }
}
