using System;
using WhiskeyRealism.Strategic;

using static TestHarness;

internal static class CicFactionAiGuardTests
{
    public static void Run()
    {
        TestHarness.RunAll(new (string name, Action run)[]
        {
            ("cic faction guard arms only for another faction of a CIC career", ArmsOnlyForOtherFaction),
            ("cic faction guard never arms for the player's own faction", NeverArmsForOwnFaction),
            ("cic faction guard rollback leaves vanilla's guard", RollbackLeavesVanillaGuard),
            ("cic faction guard consumes only the default-argument call", ConsumesOnlyDefaultArgumentCall),
            ("cic faction guard reports whether vanilla's guard still blocks", ReportsVanillaGuardBlocks),
        });
    }

    private static void ArmsOnlyForOtherFaction()
    {
        AssertTrue(CicFactionAiGuard.ShouldArm(true, wlActive: true, playerIsCic: true, cicAlliance: 0, factionAlliance: 1), "enemy of a Union CIC");
        AssertFalse(CicFactionAiGuard.ShouldArm(true, wlActive: false, playerIsCic: true, cicAlliance: 0, factionAlliance: 1), "no career");
        AssertFalse(CicFactionAiGuard.ShouldArm(true, wlActive: true, playerIsCic: false, cicAlliance: 0, factionAlliance: 1), "player not CIC");
        AssertFalse(CicFactionAiGuard.ShouldArm(true, wlActive: true, playerIsCic: true, cicAlliance: 0, factionAlliance: -1), "unknown faction");
        AssertTrue(CicFactionAiGuard.ShouldArm(true, wlActive: true, playerIsCic: true, cicAlliance: 1, factionAlliance: 2), "Europe is another faction too");
    }

    private static void NeverArmsForOwnFaction()
    {
        AssertFalse(CicFactionAiGuard.ShouldArm(true, wlActive: true, playerIsCic: true, cicAlliance: 1, factionAlliance: 1), "player's own faction");
    }

    private static void RollbackLeavesVanillaGuard()
    {
        AssertFalse(CicFactionAiGuard.ShouldArm(false, wlActive: true, playerIsCic: true, cicAlliance: 0, factionAlliance: 1), "flag off");
    }

    private static void ConsumesOnlyDefaultArgumentCall()
    {
        AssertTrue(CicFactionAiGuard.ConsumeAsNotCic(armed: true, manualCommander: -1), "the guard's call");
        AssertFalse(CicFactionAiGuard.ConsumeAsNotCic(armed: true, manualCommander: 12), "a question about another commander");
        AssertFalse(CicFactionAiGuard.ConsumeAsNotCic(armed: false, manualCommander: -1), "not armed");
    }

    private static void ReportsVanillaGuardBlocks()
    {
        AssertFalse(CicFactionAiGuard.VanillaGuardBlocks(true, wlActive: true, playerIsCic: true, cicAlliance: 0, factionAlliance: 1), "guard lets the enemy run");
        AssertTrue(CicFactionAiGuard.VanillaGuardBlocks(true, wlActive: true, playerIsCic: true, cicAlliance: 0, factionAlliance: 0), "own faction still blocked");
        AssertTrue(CicFactionAiGuard.VanillaGuardBlocks(false, wlActive: true, playerIsCic: true, cicAlliance: 0, factionAlliance: 1), "rollback: vanilla blocks every faction");
        AssertFalse(CicFactionAiGuard.VanillaGuardBlocks(true, wlActive: true, playerIsCic: false, cicAlliance: 0, factionAlliance: 0), "no CIC career");
        AssertFalse(CicFactionAiGuard.VanillaGuardBlocks(false, wlActive: false, playerIsCic: true, cicAlliance: 0, factionAlliance: 0), "no career");
    }
}
