using System;
using WhiskeyRealism.Tactical;

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
        VanillaAnglePointMatchesGetAngleTerrainPos();
        VanillaAnglePointXYMatchesGetAngleTerrainPosXY();
        B8MapsWithdrawalDecisionsToVanillaFallbackShapes();
        B8SkipsUnitsVanillaWouldNotFallBack();
        ReserveGateMirrorsVanillaEndangeredFilter();
        ReserveGateSnapshotsOnlyPossibleSupportUnits();
        ReserveGateRollbackOnlyWhenStopRegimentIsReversible();
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
        bool endBattle = false)
    {
        return BattleWriterPolicy.ResolveMacroAiWrite(new MacroAiWriteInput(
            modeAllowsWrites,
            feud,
            aiStrategy,
            scriptedSide,
            endBattle,
            current,
            ledger,
            doctrine));
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
}
