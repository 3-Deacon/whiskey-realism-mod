using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-011. UnityEngine.Random.Range(int, int) excludes its upper bound, but eight
    // AICampaign candidate picks pass list.Count - 1, so the last candidate is never
    // chosen and a two-candidate list always yields the first: the port a new fleet is
    // raised at (RaiseFleet 12146), the fleet a new ship joins (GrabFleet 12241, weighted
    // by free slots), the ship type to build (GrabShipType 12302), the division and the
    // corps a recruit joins (GrabExistingGroup 12793, 12801), the IIP a recruiting zone
    // uses (GetClosestIIPPositionOfZone 12928), the army a new corps joins
    // (RaiseNewCampaignGroup 13040) and the frontline town a raid force is raised at
    // (CheckCreationOfRaidForces 17611). The same class picks with Range(0, Count)
    // elsewhere (RaidForce 9070, 9174).
    //
    // The bound is an inline expression, so this narrow Transpiler (RandomPickRangeIl)
    // replaces the "1" of "Count - 1" with a call to UpperOffset, which returns 0 while
    // the fix is on and 1 while it is off (vanilla), so the config flag is a live
    // rollback. A method whose site count differs from the expected one (a game update)
    // is left as vanilla with one warning. Transpilers carry no TelemetryPerf scope
    // (AGENTS.md); the hook marks first fire so a smoke log shows the rewritten IL ran. Rollback: Campaign Bug Fixes / Enable AI Random Candidate Pick Fix.
    [HarmonyPatch]
    public static class RandomCandidatePickPatch
    {
        internal static bool Prepare()
        {
            return TargetMethods().Any();
        }

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var site in RandomPickRangeIl.Sites)
            {
                var method = AccessTools.Method(typeof(AICampaign), site.Method);
                if (method != null) yield return method;
                else OnceLog.Warning("random-candidate-pick:missing", "[Patch:RandomPick] an AICampaign pick method is missing in this game build; it stays vanilla");
            }
        }

        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var vanilla = instructions.ToList();
            try
            {
                var hook = AccessTools.Method(typeof(RandomCandidatePickPatch), nameof(UpperOffset));
                var result = RandomPickRangeIl.Transpile(vanilla, original != null ? original.Name : string.Empty,
                    RandomPickRangeIl.ResolveRandomRangeInt(), hook, out var site, out int matches);
                if (site == null) return vanilla;
                var s = site.Value;
                if (ReferenceEquals(result, vanilla))
                    OnceLog.Warning(s.PatternKey, "[Patch:RandomPick] " + s.Method + " (" + s.Anchor + "): found " + matches + " Count - 1 picks, expected " + s.Expected + "; left as vanilla");
                else
                    OnceLog.Info(s.AppliedKey, "[Patch:RandomPick] " + s.Method + " candidate pick now includes the last candidate");
                return result;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("random-candidate-pick:transpiler", "[Patch:RandomPick] transpiler failed; method left as vanilla: " + ex.Message);
                return vanilla;
            }
        }

        // Called from the rewritten vanilla IL in place of the constant 1 of "Count - 1".
        public static int UpperOffset()
        {
            PatchFirstFire.Mark("campaign.patch.random-candidate-pick.hook");
            return RandomPickRangeIl.UpperOffset(Enabled());
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableAiRandomCandidatePickFix != null
                    && Plugin.EnableAiRandomCandidatePickFix.Value;
            }
            catch { return false; }
        }
    }
}
