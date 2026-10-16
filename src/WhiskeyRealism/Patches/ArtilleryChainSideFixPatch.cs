using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Tactical.Battle.BugFixes;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #123 ArtilleryChainSideFixPatch (BUG-TAC-029, BC13) - Vanilla AIBattle.AssignReserves (7092) sends every artillery group of an
    // objective to the chain end Random.Range(1, 2) picks, and the int overload excludes its upper bound, so it is always 1 (the left
    // end, linkedposition 3): no artillery ever joins the right. This narrow Transpiler (ArtillerySideIl) replaces the "2" of
    // "Random.Range(1, 2)" with a call to UpperBound, which returns 3 while the fix is on (1 or 2 at random) and 2 while it is off
    // (vanilla), so the config flag is a live rollback. A site count other than one (a game update) leaves the method vanilla
    // with one warning. Transpilers carry no TelemetryPerf scope (AGENTS.md); the hook marks first fire. Runs in every Commander Mode.
    [HarmonyPatch(typeof(AIBattle), "AssignReserves")]
    internal static class ArtilleryChainSideFixPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var vanilla = instructions.ToList();
            try
            {
                var hook = AccessTools.Method(typeof(ArtilleryChainSideFixPatch), nameof(UpperBound));
                var result = ArtillerySideIl.Transpile(vanilla, RandomPickRangeIl.ResolveRandomRangeInt(), hook, out int matches);
                if (ReferenceEquals(result, vanilla))
                    OnceLog.Warning("tactical-il:artillery-side:pattern",
                        "[ArtillerySideIl] AssignReserves (7092): pattern mismatch count=" + matches + ", expected " + ArtillerySideIl.ExpectedSites + "; left as vanilla");
                else
                    OnceLog.Info("tactical-il:artillery-side:applied",
                        "[ArtillerySideIl] applied sites=" + matches + " (ldc.i4.1; ldc.i4.2; call Random.Range): artillery groups now join either chain end");
                return result;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-il:artillery-side:transpiler", "[ArtillerySideIl] transpiler failed; method left as vanilla: " + ex.Message);
                return vanilla;
            }
        }

        // Called from the rewritten vanilla IL in place of the constant 2 of "Random.Range(1, 2)".
        public static int UpperBound()
        {
            PatchFirstFire.Mark("tactical.patch.artillery-side.hook");
            return ArtillerySideIl.UpperBound(Enabled());
        }

        internal static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value && Plugin.EnableVanillaArtilleryChainSideFix != null
                    && Plugin.EnableVanillaArtilleryChainSideFix.Value;
            }
            catch { return false; }
        }
    }
}
