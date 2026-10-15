using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-BAT-002. Autocalc.FightUnit (decompile 21050) sets the casualty weapon factor
    // num5 (0 by default, 21084, multiplied into the casualties at 21169) only when
    // `unit0.weapon > 0` (21085 line units, 21089 artillery). Weapon id 0 is the 6-pounder
    // Field Gun (Config/weapons.txt record 0), every shipped campaign's standard artillery
    // weapon (nations.dat standardweapons), so a battery still carrying it inflicts no
    // casualties in autoresolved land battles, raids and skirmishes. The sibling
    // EnvolvedUnits.GetFortWeaponStrength tests `weapon >= 0` (18482), and -1 is vanilla's
    // "no weapon" value everywhere else (81576, 93203, 93220), so the intent is `>= 0`.
    //
    // Matched IL per site: ldarg.1; ldfld Regiment::weapon; ldc.i4.0; ble(.s) <skip>.
    // The ldc.i4.0 is replaced by a hook call returning -1 (fix on: weapon > -1) or 0
    // (fix off: vanilla), same stack shape, labels kept.
    internal static class AutoresolveWeaponGateIl
    {
        internal const int ExpectedSites = 2;

        // The transpiler's whole decision: the rewritten copy when exactly ExpectedSites
        // match, otherwise the vanilla list itself (same instance).
        internal static List<CodeInstruction> Transpile(List<CodeInstruction> vanilla, FieldInfo weaponField,
            MethodInfo floorHook, out int matches)
        {
            matches = 0;
            if (vanilla == null) return vanilla;
            var code = new List<CodeInstruction>(vanilla.Count);
            foreach (var ci in vanilla) code.Add(ci.Clone());
            matches = Rewrite(code, weaponField, floorHook);
            return matches == ExpectedSites ? code : vanilla;
        }

        // The lowest weapon id that does NOT count as armed: -1 with the fix, 0 in vanilla.
        internal static int WeaponFloor(bool fixEnabled)
        {
            return fixEnabled ? -1 : 0;
        }

        internal static FieldInfo ResolveWeaponField()
        {
            return AccessTools.Field(typeof(Regiment), "weapon");
        }

        // Returns the number of matching sites; the code changes only when it is ExpectedSites.
        internal static int Rewrite(List<CodeInstruction> code, FieldInfo weaponField, MethodInfo floorHook)
        {
            if (code == null || weaponField == null || floorHook == null) return 0;
            var sites = Sites(code, weaponField);
            if (sites.Count != ExpectedSites) return sites.Count;
            foreach (int at in sites)
            {
                code[at].opcode = OpCodes.Call;
                code[at].operand = floorHook;
            }
            return sites.Count;
        }

        // Index of the `ldc.i4.0` of every `ldarg.1; ldfld weapon; ldc.i4.0; ble` run.
        internal static List<int> Sites(List<CodeInstruction> code, FieldInfo weaponField)
        {
            var sites = new List<int>();
            for (int i = 1; i + 2 < code.Count; i++)
            {
                if (code[i - 1].opcode == OpCodes.Ldarg_1
                    && code[i].LoadsField(weaponField)
                    && code[i + 1].LoadsConstant(0)
                    && (code[i + 2].opcode == OpCodes.Ble || code[i + 2].opcode == OpCodes.Ble_S))
                    sites.Add(i + 1);
            }
            return sites;
        }
    }
}
