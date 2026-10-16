using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WhiskeyRealism.Tactical.Battle.BugFixes
{
    // BC13 (spec § 7, requirement-fix-artillery-side). Vanilla AIBattle.AssignReserves (7092) picks the end of the line an artillery
    // group joins with Random.Range(1, 2): an int range with an exclusive maximum, so it is always 1 and every artillery group joins
    // the left chain end (AssignReserveToOperationalGroup direction 1). The intent is a left (1) or a right (2) end. The fix is a
    // narrow IL rewrite under the BUG-LOG-011 rules (RandomPickRangeIl): the "ldc.i4.2" upper bound of the one int Random.Range
    // preceded by "ldc.i4.1" becomes a hook call that returns 3 (fix on: the int range 1..2) or 2 (fix off: vanilla). The method's
    // other Random.Range is the float overload (the 50 % roll) and does not match.
    //
    // Matched IL: ldc.i4.1; ldc.i4.2; call UnityEngine.Random::Range(int32, int32)   (exactly one site)
    internal static class ArtillerySideIl
    {
        internal const string Method = "AssignReserves";
        internal const int ExpectedSites = 1;

        internal static int UpperBound(bool fixEnabled)
        {
            return fixEnabled ? 3 : 2;
        }

        /// <summary>The list Harmony should emit: the rewritten copy when exactly one site matches, else vanilla's own instance.</summary>
        internal static List<CodeInstruction> Transpile(List<CodeInstruction> vanilla, MethodInfo randomRangeInt, MethodInfo upperBoundHook, out int matches)
        {
            matches = 0;
            if (vanilla == null) return vanilla;
            var code = new List<CodeInstruction>(vanilla.Count);
            foreach (var ci in vanilla) code.Add(new CodeInstruction(ci));
            matches = Rewrite(code, randomRangeInt, upperBoundHook, ExpectedSites);
            return matches == ExpectedSites ? code : vanilla;
        }

        // Returns the number of matching sites; the code is changed only when exactly `expected` match (fail safe). The ldc.i4.2 is
        // edited in place, so its labels and exception blocks stay where they are.
        internal static int Rewrite(List<CodeInstruction> code, MethodInfo randomRangeInt, MethodInfo upperBoundHook, int expected)
        {
            if (code == null || randomRangeInt == null || upperBoundHook == null) return 0;
            var sites = new List<int>();
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ldc_I4_1 && code[i + 1].opcode == OpCodes.Ldc_I4_2 && code[i + 2].Calls(randomRangeInt))
                    sites.Add(i + 1);
            }
            if (sites.Count != expected) return sites.Count;
            foreach (int at in sites)
            {
                code[at].opcode = OpCodes.Call;
                code[at].operand = upperBoundHook;
            }
            return sites.Count;
        }
    }
}
