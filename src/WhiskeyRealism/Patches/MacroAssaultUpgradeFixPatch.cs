using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using WhiskeyRealism.Tactical.Battle.BugFixes;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #122 MacroAssaultUpgradeFixPatch (BUG-TAC-028, BC13) - Vanilla AIBattle.CheckGlobalAIStrategy (6432-6434) upgrades a macro
    // "attack" to "assault" (no flanking possible, enough force balance) with "if (newmacroaitoset == 1) newmacroaitoset = 0": it writes
    // the PENDING value while the method's tail (6438-6446) compares and stores this cycle's local decision num4, so the upgrade lands
    // only when the pending value already was attack and the tail never stores it. This narrow Transpiler (MacroUpgradeIl) replaces
    // that statement with "num4 = Hook(this, num4)": with the fix on the hook turns an attack decision into assault and leaves the
    // field alone, so the tail stores and times the upgrade like any other macro decision; with it off the hook performs vanilla's own
    // write and gives num4 back unchanged, so the config flag is a live rollback. #44 (BattleMacroStrategyPatch) is a Prefix/Postfix
    // pair on the same method and is unaffected. A block count other than one (a game update) leaves the method vanilla with one
    // warning. Transpilers carry no TelemetryPerf scope (AGENTS.md); the hook marks first fire. Runs in every Commander Mode.
    [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
    internal static class MacroAssaultUpgradeFixPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var vanilla = instructions.ToList();
            try
            {
                var hook = AccessTools.Method(typeof(MacroAssaultUpgradeFixPatch), nameof(Hook));
                var result = MacroUpgradeIl.Transpile(vanilla, AccessTools.Field(typeof(AIBattle), "newmacroaitoset"), hook, out int matches);
                if (ReferenceEquals(result, vanilla))
                    OnceLog.Warning("tactical-il:macro-upgrade:pattern",
                        "[MacroUpgradeIl] CheckGlobalAIStrategy (6432): pattern mismatch count=" + matches + ", expected " + MacroUpgradeIl.ExpectedSites + "; left as vanilla");
                else
                    OnceLog.Info("tactical-il:macro-upgrade:applied",
                        "[MacroUpgradeIl] applied blocks=" + matches + " (newmacroaitoset == 1 write replaced by num4 = Hook): the assault upgrade now reaches the stored decision");
                return result;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-il:macro-upgrade:transpiler", "[MacroUpgradeIl] transpiler failed; method left as vanilla: " + ex.Message);
                return vanilla;
            }
        }

        // Called from the rewritten vanilla IL in place of "if (newmacroaitoset == 1) newmacroaitoset = 0"; the result is stored in num4.
        public static int Hook(AIBattle battle, int num4)
        {
            PatchFirstFire.Mark("tactical.patch.macro-assault-upgrade.hook");
            try
            {
                if (Enabled()) return MacroUpgradeIl.UpgradedDecision(num4);
                if (battle != null) battle.newmacroaitoset = MacroUpgradeIl.VanillaPending(battle.newmacroaitoset);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-il:macro-upgrade:hook", "[MacroUpgradeIl] hook failed; vanilla value kept: " + ex.Message);
            }
            return num4;
        }

        internal static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value && Plugin.EnableVanillaMacroAssaultUpgradeFix != null
                    && Plugin.EnableVanillaMacroAssaultUpgradeFix.Value;
            }
            catch { return false; }
        }
    }
}
