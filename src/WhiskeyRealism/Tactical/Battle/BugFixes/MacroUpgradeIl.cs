using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WhiskeyRealism.Tactical.Battle.BugFixes
{
    // BC13 (spec § 7, requirement-fix-macro-upgrade). Vanilla AIBattle.CheckGlobalAIStrategy (6432-6434) upgrades "attack" to "assault"
    // (no flanking possible and enough force balance) by writing the PENDING value, "if (newmacroaitoset == 1) newmacroaitoset = 0",
    // while the tail of the method (6438-6446) compares and stores this cycle's local decision num4. The upgrade therefore lands only
    // when the pending value already was attack, and the tail does not even store it. The fix rewrites the 7-instruction block
    //
    //   ldarg.0; ldfld newmacroaitoset; ldc.i4.1; bne.un(.s) L; ldarg.0; ldc.i4.0; stfld newmacroaitoset        (L is the next instruction)
    //
    // into   ldarg.0; ldloc num4; call Hook(AIBattle, int); stloc num4   - the first ldarg.0 stays (so its labels and blocks stay).
    // num4's local is the stloc right after the only "ldfld newmacroaitoset" that is followed by a stloc (6393). Anything else (no
    // anchor, two anchors, a label or exception block inside the block) leaves vanilla's own list untouched.
    internal static class MacroUpgradeIl
    {
        internal const string Method = "CheckGlobalAIStrategy";
        internal const int ExpectedSites = 1;
        private const int BlockLength = 7;

        /// <summary>Fix on: this cycle's decision attack (1) becomes assault (0); the field is left alone.</summary>
        internal static int UpgradedDecision(int num4)
        {
            return num4 == 1 ? 0 : num4;
        }

        /// <summary>Fix off (vanilla's own write): a pending attack becomes assault.</summary>
        internal static int VanillaPending(int pending)
        {
            return pending == 1 ? 0 : pending;
        }

        /// <summary>The list Harmony should emit: the rewritten copy when exactly one block matches, else vanilla's own instance.</summary>
        internal static List<CodeInstruction> Transpile(List<CodeInstruction> vanilla, FieldInfo pendingField, MethodInfo hook, out int matches)
        {
            matches = 0;
            if (vanilla == null) return vanilla;
            var code = new List<CodeInstruction>(vanilla.Count);
            foreach (var ci in vanilla) code.Add(new CodeInstruction(ci));
            matches = Rewrite(code, pendingField, hook, ExpectedSites);
            return matches == ExpectedSites ? code : vanilla;
        }

        // Returns the number of matching blocks; the code is changed only when exactly `expected` match (fail safe).
        internal static int Rewrite(List<CodeInstruction> code, FieldInfo pendingField, MethodInfo hook, int expected)
        {
            if (code == null || pendingField == null || hook == null) return 0;
            if (!TryFindLocalStore(code, pendingField, out int storeAt)) return 0;
            var blocks = new List<int>();
            for (int i = 0; i + BlockLength < code.Count; i++)
            {
                if (IsBlock(code, i, pendingField)) blocks.Add(i);
            }
            if (blocks.Count != expected) return blocks.Count;
            foreach (int at in blocks)
            {
                for (int k = at + 1; k < at + BlockLength; k++)
                    if (code[k].labels.Count != 0 || code[k].blocks.Count != 0) return blocks.Count - 1;
            }
            var store = code[storeAt];
            for (int n = blocks.Count - 1; n >= 0; n--)
            {
                int at = blocks[n];
                var load = LoadFor(store);
                if (load == null) return 0;
                code.RemoveRange(at + 1, BlockLength - 1);
                code.InsertRange(at + 1, new[]
                {
                    load,
                    new CodeInstruction(OpCodes.Call, hook),
                    new CodeInstruction(store),
                });
            }
            return blocks.Count;
        }

        // num4's store: the stloc right after the one ldfld of the pending field that is followed by a stloc. Exactly one, else no anchor.
        private static bool TryFindLocalStore(List<CodeInstruction> code, FieldInfo field, out int storeAt)
        {
            storeAt = -1;
            int found = 0;
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ldfld && ReferenceEquals(code[i].operand, field) && IsStloc(code[i + 1]))
                {
                    storeAt = i + 1;
                    found++;
                }
            }
            return found == 1;
        }

        private static bool IsBlock(List<CodeInstruction> code, int i, FieldInfo field)
        {
            if (code[i].opcode != OpCodes.Ldarg_0) return false;
            if (code[i + 1].opcode != OpCodes.Ldfld || !ReferenceEquals(code[i + 1].operand, field)) return false;
            if (code[i + 2].opcode != OpCodes.Ldc_I4_1) return false;
            var branch = code[i + 3];
            if (branch.opcode != OpCodes.Bne_Un && branch.opcode != OpCodes.Bne_Un_S) return false;
            if (code[i + 4].opcode != OpCodes.Ldarg_0 || code[i + 5].opcode != OpCodes.Ldc_I4_0) return false;
            if (code[i + 6].opcode != OpCodes.Stfld || !ReferenceEquals(code[i + 6].operand, field)) return false;
            return branch.operand is Label target && code[i + BlockLength].labels.Contains(target);
        }

        private static bool IsStloc(CodeInstruction ci)
        {
            return ci.opcode == OpCodes.Stloc || ci.opcode == OpCodes.Stloc_S || ci.opcode == OpCodes.Stloc_0
                || ci.opcode == OpCodes.Stloc_1 || ci.opcode == OpCodes.Stloc_2 || ci.opcode == OpCodes.Stloc_3;
        }

        // The matching load for a store, carrying the same local operand.
        private static CodeInstruction LoadFor(CodeInstruction store)
        {
            var op = store.opcode;
            if (op == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
            if (op == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
            if (op == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
            if (op == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
            if (op == OpCodes.Stloc_S) return new CodeInstruction(OpCodes.Ldloc_S, store.operand);
            if (op == OpCodes.Stloc) return new CodeInstruction(OpCodes.Ldloc, store.operand);
            return null;
        }
    }
}
