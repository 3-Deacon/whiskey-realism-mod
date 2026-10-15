using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-AI-014. unitprefs.txt 1595-1596 "Increase factor of engineering points if commander
    // fits to branch of arms (engineers)" = 1.5 (factorengineerpointsbranchofarms, 53852) is
    // added for an engineer regiment whose commander has historicalbranch == 4, both in
    // BattleUnits.CalculateAllUnitsOfAlliance (87443, the battle deployment pool) and in
    // Autocalc.CalculateEngineeringPoints (22578, autoresolve). Branch 4 is navy
    // (branchtext 65091, fleet commanders 59162); engineer is 3 (W&L attribute 19 Engineer 42526,
    // 61 commanders in commanders.txt such as Lee, McClellan, Meade, Beauregard). Neither method
    // reads historicalbranch anywhere else. The test is an inline constant, so for the call a
    // Prefix writes every navy officer's branch as -4 and every engineer's as 4 (vanilla's own
    // status and unit conditions still decide who gets it), two FieldMasks (LogisticsSwaps);
    // the Finalizer restores engineers, then navy, each only where the mask value is still
    // there, even if vanilla throws. Both methods run a few times per battle / autoresolve day.
    // Rollback: Campaign / Fix Engineer Branch Bonus.
    internal static class EngineerBranchBonusPatch
    {
        internal static bool Enabled()
        {
            try { return Plugin.FixEngineerBranchBonus != null && Plugin.FixEngineerBranchBonus.Value; }
            catch { return false; }
        }

        internal static EngineerBranchMasks<GameVars.Commander> NewMasks()
        {
            return new EngineerBranchMasks<GameVars.Commander>(c => c.historicalbranch, (c, v) => c.historicalbranch = v);
        }

        private static void LogRestore(string failure, string key)
        {
            if (failure != null) OnceLog.Warning(key, "[Patch:EngineerBranch] branch restore failed: " + failure);
        }

        [HarmonyPatch(typeof(BattleUnits), nameof(BattleUnits.CalculateAllUnitsOfAlliance))]
        internal static class BattlePool
        {
            private static readonly EngineerBranchMasks<GameVars.Commander> Swap = NewMasks();

            [HarmonyPrefix]
            internal static void Prefix()
            {
                using (TelemetryPerf.Scope("tactical.patch.engineer-branch.battle.prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("tactical.patch.engineer-branch.battle.prefix");
                    try
                    {
                        LogRestore(Swap.Restore(), "engineer-branch:battle:restore");
                        if (!Enabled()) return;
                        if (Swap.Apply(GameVars.commander) > 0)
                            OnceLog.Info("engineer-branch:battle", "[Patch:EngineerBranch] battle engineering points: branch bonus tests the engineer branch");
                    }
                    catch (Exception ex)
                    {
                        LogRestore(Swap.Restore(), "engineer-branch:battle:restore");
                        OnceLog.Warning("engineer-branch:battle:prefix", "[Patch:EngineerBranch] CalculateAllUnitsOfAlliance prefix failed, vanilla left as is: " + ex.Message);
                    }
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer()
            {
                using (TelemetryPerf.Scope("tactical.patch.engineer-branch.battle.finalizer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("tactical.patch.engineer-branch.battle.finalizer");
                    if (Swap.Active) LogRestore(Swap.Restore(), "engineer-branch:battle:restore");
                }
            }
        }

        [HarmonyPatch(typeof(Autocalc), "CalculateEngineeringPoints")]
        internal static class Autoresolve
        {
            private static readonly EngineerBranchMasks<GameVars.Commander> Swap = NewMasks();

            [HarmonyPrefix]
            internal static void Prefix()
            {
                using (TelemetryPerf.Scope("campaign.patch.engineer-branch.autocalc.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.engineer-branch.autocalc.prefix");
                    try
                    {
                        LogRestore(Swap.Restore(), "engineer-branch:autocalc:restore");
                        if (!Enabled()) return;
                        if (Swap.Apply(GameVars.commander) > 0)
                            OnceLog.Info("engineer-branch:autocalc", "[Patch:EngineerBranch] autoresolve engineering points: branch bonus tests the engineer branch");
                    }
                    catch (Exception ex)
                    {
                        LogRestore(Swap.Restore(), "engineer-branch:autocalc:restore");
                        OnceLog.Warning("engineer-branch:autocalc:prefix", "[Patch:EngineerBranch] CalculateEngineeringPoints prefix failed, vanilla left as is: " + ex.Message);
                    }
                }
            }

            [HarmonyFinalizer]
            internal static void Finalizer()
            {
                using (TelemetryPerf.Scope("campaign.patch.engineer-branch.autocalc.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                {
                    PatchFirstFire.Mark("campaign.patch.engineer-branch.autocalc.finalizer");
                    if (Swap.Active) LogRestore(Swap.Restore(), "engineer-branch:autocalc:restore");
                }
            }
        }
    }
}
