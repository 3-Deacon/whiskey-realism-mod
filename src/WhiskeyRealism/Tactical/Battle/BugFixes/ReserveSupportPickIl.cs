using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;

namespace WhiskeyRealism.Tactical.Battle.BugFixes
{
    // BC13 (spec § 7, requirement-fix-reserve-support-pick, BUG-TAC-008). Vanilla AIBattle.CheckUseOfReserves (6116) picks the
    // endangered regiment a reserve supports with Random.Range(0, list.Count - 1): the upper bound of the int overload is
    // exclusive, so the last endangered regiment is never chosen (with two, always the first). This is BUG-LOG-011's class in
    // battle, and the same shipped matcher serves it: RandomPickRangeIl.Rewrite replaces the "1" of "Count - 1" with a hook
    // call that returns 0 (fix on) or 1 (fix off: vanilla). The method's float Random.Range(-1f, 1f) does not match. The
    // rewrite is made on a copy built with new CodeInstruction(ci) (labels and exception blocks kept); any other site count
    // leaves vanilla's own list untouched.
    internal static class ReserveSupportPickIl
    {
        internal const string Method = "CheckUseOfReserves";
        internal const int ExpectedSites = 1;

        /// <summary>The list Harmony should emit: the rewritten copy when exactly one site matches, else vanilla's own instance.</summary>
        internal static List<CodeInstruction> Transpile(List<CodeInstruction> vanilla, MethodInfo randomRangeInt, MethodInfo upperOffsetHook, out int matches)
        {
            matches = 0;
            if (vanilla == null) return vanilla;
            var code = new List<CodeInstruction>(vanilla.Count);
            foreach (var ci in vanilla) code.Add(new CodeInstruction(ci));
            matches = RandomPickRangeIl.Rewrite(code, randomRangeInt, upperOffsetHook, ExpectedSites);
            return matches == ExpectedSites ? code : vanilla;
        }
    }
}
