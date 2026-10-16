using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Tactical.Battle.BugFixes;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #124 ReserveSupportPickFixPatch (BUG-TAC-008, BC13) - Vanilla AIBattle.CheckUseOfReserves (6116) picks the endangered regiment a
    // reserve supports with Random.Range(0, list.Count - 1). UnityEngine.Random.Range(int, int) excludes its upper bound, so the last
    // endangered regiment is never picked and a two-regiment list always yields the first (BUG-LOG-011's class, in battle). The bound is
    // an inline expression, so this narrow Transpiler (ReserveSupportPickIl over the shipped RandomPickRangeIl matcher) replaces the "1"
    // of "Count - 1" with a call to UpperOffset, which returns 0 while the fix is on and 1 while it is off (vanilla): the config flag
    // is a live rollback. A site count other than one (a game update) leaves the method vanilla with one warning. #59 still denies
    // vanilla's reserve path for Reserve-role children; this changes only which endangered regiment vanilla supports. Transpilers
    // carry no TelemetryPerf scope (AGENTS.md); the hook marks first fire. Runs in every Commander Mode.
    [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
    internal static class ReserveSupportPickFixPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var vanilla = instructions.ToList();
            try
            {
                var hook = AccessTools.Method(typeof(ReserveSupportPickFixPatch), nameof(UpperOffset));
                var result = ReserveSupportPickIl.Transpile(vanilla, RandomPickRangeIl.ResolveRandomRangeInt(), hook, out int matches);
                if (ReferenceEquals(result, vanilla))
                    OnceLog.Warning("tactical-il:reserve-support-pick:pattern",
                        "[ReserveSupportPickIl] CheckUseOfReserves (6116): pattern mismatch count=" + matches + ", expected " + ReserveSupportPickIl.ExpectedSites + "; left as vanilla");
                else
                    OnceLog.Info("tactical-il:reserve-support-pick:applied",
                        "[ReserveSupportPickIl] applied sites=" + matches + " (callvirt get_Count; ldc.i4.1; sub; call Random.Range): the support pick now includes the last endangered regiment");
                return result;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-il:reserve-support-pick:transpiler", "[ReserveSupportPickIl] transpiler failed; method left as vanilla: " + ex.Message);
                return vanilla;
            }
        }

        // Called from the rewritten vanilla IL in place of the constant 1 of "Count - 1".
        public static int UpperOffset()
        {
            PatchFirstFire.Mark("tactical.patch.reserve-support-pick.hook");
            return RandomPickRangeIl.UpperOffset(Enabled());
        }

        internal static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value && Plugin.EnableVanillaReserveSupportPickFix != null
                    && Plugin.EnableVanillaReserveSupportPickFix.Value;
            }
            catch { return false; }
        }
    }
}
