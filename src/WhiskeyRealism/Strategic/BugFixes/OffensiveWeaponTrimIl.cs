using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-LOG-010. AICampaign.CheckOffensiveMovements counts each unit it adds with the
    // blended weapon factor w' = 1 * (1 - influence) + influence * w (decompile 14239,
    // 14274; also CalculateZoneRecruitingProbs 13137), but the loop that trims surplus
    // units writes w *= influence * w + (1 - influence) * 1 (14294), so a removed unit
    // is credited back with w * w' instead of w'. The factor is a local, so the fix is a
    // narrow IL rewrite: a hook call is inserted after the trim's left-operand load,
    // returning 1 (fix on: w = 1 * w') or w (fix off: vanilla).
    //
    // The matched IL (one site in CheckOffensiveMovements):
    //   call   AICampaign::GetWeaponStrengthFactor(Regiment)
    //   stloc  w
    //   ldloc  w                       <- hook inserted after this
    //   ldsfld GamePrefs::influenceweaponsonoffensives
    //   ldloc  w
    //   mul
    //   ldc.r4 1
    //   ldsfld GamePrefs::influenceweaponsonoffensives
    //   sub
    //   ldc.r4 1
    //   mul
    //   add
    //   mul
    //   stloc  w
    internal static class OffensiveWeaponTrimIl
    {
        internal const int ExpectedSites = 1;

        internal static float Blend(float weaponFactor, float influence)
        {
            return 1f * (1f - influence) + influence * weaponFactor;
        }

        internal static float LeftOperand(float weaponFactor, bool fixEnabled)
        {
            return fixEnabled ? 1f : weaponFactor;
        }

        internal static MethodInfo ResolveWeaponStrengthFactor()
        {
            var aiCampaign = AccessTools.TypeByName("AICampaign");
            var regiment = AccessTools.TypeByName("Regiment");
            if (aiCampaign == null || regiment == null) return null;
            return AccessTools.Method(aiCampaign, "GetWeaponStrengthFactor", new[] { regiment });
        }

        internal static FieldInfo ResolveInfluenceField()
        {
            var prefs = AccessTools.TypeByName("GamePrefs");
            return prefs == null ? null : AccessTools.Field(prefs, "influenceweaponsonoffensives");
        }

        // The transpiler's whole decision: the rewritten copy when exactly ExpectedSites
        // match, otherwise the vanilla list itself (same instance).
        internal static List<CodeInstruction> Transpile(List<CodeInstruction> vanilla, MethodInfo weaponStrengthFactor,
            FieldInfo influence, MethodInfo leftOperandHook, out int matches)
        {
            matches = 0;
            if (vanilla == null) return vanilla;
            var code = new List<CodeInstruction>(vanilla.Count);
            foreach (var ci in vanilla) code.Add(ci.Clone());
            matches = Rewrite(code, weaponStrengthFactor, influence, leftOperandHook);
            return matches == ExpectedSites ? code : vanilla;
        }

        // Returns the number of matching sites. The code is changed only when exactly
        // ExpectedSites match; any other count leaves it untouched (fail safe).
        internal static int Rewrite(List<CodeInstruction> code, MethodInfo weaponStrengthFactor, FieldInfo influence, MethodInfo leftOperandHook)
        {
            if (code == null || weaponStrengthFactor == null || influence == null || leftOperandHook == null) return 0;
            var sites = new List<int>();
            for (int i = 0; i + 13 < code.Count; i++)
            {
                if (IsSite(code, i, weaponStrengthFactor, influence)) sites.Add(i);
            }
            if (sites.Count != ExpectedSites) return sites.Count;
            code.Insert(sites[0] + 3, new CodeInstruction(OpCodes.Call, leftOperandHook));
            return sites.Count;
        }

        private static bool IsSite(List<CodeInstruction> c, int i, MethodInfo weaponStrengthFactor, FieldInfo influence)
        {
            if (!c[i].Calls(weaponStrengthFactor)) return false;
            if (!c[i + 1].IsStloc()) return false;
            int local = LocalIndex(c[i + 1]);
            if (local < 0) return false;
            return IsLdloc(c[i + 2], local)
                && c[i + 3].LoadsField(influence)
                && IsLdloc(c[i + 4], local)
                && c[i + 5].opcode == OpCodes.Mul
                && IsOne(c[i + 6])
                && c[i + 7].LoadsField(influence)
                && c[i + 8].opcode == OpCodes.Sub
                && IsOne(c[i + 9])
                && c[i + 10].opcode == OpCodes.Mul
                && c[i + 11].opcode == OpCodes.Add
                && c[i + 12].opcode == OpCodes.Mul
                && c[i + 13].IsStloc()
                && LocalIndex(c[i + 13]) == local;
        }

        private static bool IsLdloc(CodeInstruction ci, int local)
        {
            return ci.IsLdloc() && LocalIndex(ci) == local;
        }

        private static bool IsOne(CodeInstruction ci)
        {
            return ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f && f == 1f;
        }

        private static int LocalIndex(CodeInstruction ci)
        {
            var op = ci.opcode;
            if (op == OpCodes.Ldloc_0 || op == OpCodes.Stloc_0) return 0;
            if (op == OpCodes.Ldloc_1 || op == OpCodes.Stloc_1) return 1;
            if (op == OpCodes.Ldloc_2 || op == OpCodes.Stloc_2) return 2;
            if (op == OpCodes.Ldloc_3 || op == OpCodes.Stloc_3) return 3;
            switch (ci.operand)
            {
                case LocalVariableInfo local: return local.LocalIndex;
                case byte b: return b;
                case short s: return s;
                case int n: return n;
                default: return -1;
            }
        }
    }
}
