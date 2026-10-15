using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WhiskeyRealism.Patches
{
    // IL edits and list logic for the two vanilla allocation trims (no [HarmonyPatch];
    // compiled into the test harness, which runs the edits on the real game IL). Each
    // Transpile* hands back the edited copy only when exactly one site matches; any other
    // count returns the vanilla list itself (same instance), so the caller leaves the
    // method as vanilla. Edits keep labels: a call is swapped in place for a static hook
    // with the same stack input.
    internal static class AllocationTrimIl
    {
        internal const int ExpectedSites = 1;

        // Vanilla's dead literal in BattleUnits.IsUnitUnderCommander (DC:91186).
        internal const string DeadNameLiteral = "1st Delaware Sharpshooters";

        internal static FieldInfo ResolveCompleteUnitList()
        {
            return AccessTools.Field(typeof(BattleUnits), "completeunitlist");
        }

        internal static MethodInfo ResolveToListOfRegiment()
        {
            return AccessTools.Method(typeof(Enumerable), nameof(Enumerable.ToList)).MakeGenericMethod(typeof(Regiment));
        }

        internal static MethodInfo ResolveObjectNameGetter()
        {
            return AccessTools.PropertyGetter(typeof(UnityEngine.Object), nameof(UnityEngine.Object.name));
        }

        internal static MethodInfo ResolveStringEquality()
        {
            return AccessTools.Method(typeof(string), "op_Equality", new[] { typeof(string), typeof(string) });
        }

        // The hook's list logic. reuse: Clear + AddRange into the one reused list, which
        // copies the same elements in the same order as ToList. Otherwise, or with no
        // reused list or a null source, Enumerable.ToList itself: a fresh list, and vanilla's
        // ArgumentNullException for a null source.
        internal static List<T> CopyList<T>(List<T> reused, IEnumerable<T> source, bool reuse)
        {
            if (!reuse || reused == null || source == null) return Enumerable.ToList(source);
            reused.Clear();
            reused.AddRange(source);
            return reused;
        }

        // AICampaign.UpdateUnitAI (DC:11310): `ldsfld BattleUnits::completeunitlist;
        // call Enumerable::ToList<Regiment>; stloc allunits`. The call becomes a call to the
        // hook, which takes the same IEnumerable<Regiment> and returns a List<Regiment>.
        internal static List<CodeInstruction> TranspileUnitListCopy(List<CodeInstruction> vanilla, FieldInfo completeUnitList,
            MethodInfo toList, MethodInfo hook, out int matches)
        {
            matches = 0;
            if (vanilla == null) return vanilla;
            var code = Clone(vanilla);
            matches = RewriteUnitListCopy(code, completeUnitList, toList, hook);
            return matches == ExpectedSites ? code : vanilla;
        }

        // Returns the number of sites; edits only when exactly ExpectedSites match.
        internal static int RewriteUnitListCopy(List<CodeInstruction> code, FieldInfo completeUnitList, MethodInfo toList, MethodInfo hook)
        {
            if (code == null || completeUnitList == null || toList == null || hook == null) return 0;
            var sites = new List<int>();
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (code[i].LoadsField(completeUnitList) && code[i + 1].Calls(toList) && code[i + 2].IsStloc())
                    sites.Add(i + 1);
            }
            if (sites.Count != ExpectedSites) return sites.Count;
            code[sites[0]].opcode = OpCodes.Call;
            code[sites[0]].operand = hook;
            return sites.Count;
        }

        // BattleUnits.IsUnitUnderCommander (DC:91186): `callvirt Object::get_name;
        // ldstr "1st Delaware Sharpshooters"; call string::op_Equality; pop`, the result
        // discarded. The get_name call becomes a call to the hook, which takes the unit
        // already on the stack and returns nothing; the three instructions after it are
        // removed. A site whose removed instructions carry a label or an exception block is
        // not matched.
        internal static List<CodeInstruction> TranspileNameRead(List<CodeInstruction> vanilla, MethodInfo nameGetter,
            MethodInfo stringEquality, MethodInfo hook, out int matches)
        {
            matches = 0;
            if (vanilla == null) return vanilla;
            var code = Clone(vanilla);
            matches = RewriteNameRead(code, nameGetter, stringEquality, hook);
            return matches == ExpectedSites ? code : vanilla;
        }

        internal static int RewriteNameRead(List<CodeInstruction> code, MethodInfo nameGetter, MethodInfo stringEquality, MethodInfo hook)
        {
            if (code == null || nameGetter == null || stringEquality == null || hook == null) return 0;
            var sites = new List<int>();
            for (int i = 0; i + 3 < code.Count; i++)
            {
                if (code[i].Calls(nameGetter)
                    && code[i + 1].opcode == OpCodes.Ldstr && code[i + 1].operand as string == DeadNameLiteral
                    && code[i + 2].Calls(stringEquality)
                    && code[i + 3].opcode == OpCodes.Pop
                    && Unmarked(code[i + 1]) && Unmarked(code[i + 2]) && Unmarked(code[i + 3]))
                    sites.Add(i);
            }
            if (sites.Count != ExpectedSites) return sites.Count;
            int at = sites[0];
            code[at].opcode = OpCodes.Call;
            code[at].operand = hook;
            code.RemoveRange(at + 1, 3);
            return sites.Count;
        }

        private static bool Unmarked(CodeInstruction ci)
        {
            return ci.labels.Count == 0 && ci.blocks.Count == 0;
        }

        // The copy constructor keeps labels and exception blocks; CodeInstruction.Clone()
        // drops both, which would leave every branch of the method without its target.
        private static List<CodeInstruction> Clone(List<CodeInstruction> vanilla)
        {
            var code = new List<CodeInstruction>(vanilla.Count);
            foreach (var ci in vanilla) code.Add(new CodeInstruction(ci));
            return code;
        }
    }
}
