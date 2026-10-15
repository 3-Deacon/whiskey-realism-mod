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
}
