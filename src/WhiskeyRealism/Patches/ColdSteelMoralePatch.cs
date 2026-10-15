using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Regiment.UpdateMorale (decompile 128125) keeps an elite unit's charge
    // determination at 1 (no "the men doubt their chances" morale hit, 128630-128633) for
    // `HasPerk(7) && GetPerkLevel(7) >= 2` (Iron Discipline III, 128613) and for
    // `HasPerk(8) && GetPerkLevel(9) >= 2` (128617). A battle unit has one perk slot, so the
    // second can never be true. The level read (9), the melee-charge context and the
    // HasPerk(X) && GetPerkLevel(X) pattern of every neighbouring line make it Cold Steel III
    // (9); Expert Scouts (8) has no melee role (BUG-AI-010). The condition is mid-method, so a
    // narrow Transpiler passes the 8 through PerkIdIlHooks.MoraleColdSteel. On a pattern
    // miss: log once, vanilla. Rollback: Campaign / Fix Perk Id Mismatches.
    [HarmonyPatch(typeof(Regiment), "UpdateMorale")]
    internal static class ColdSteelMoralePatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            PatchFirstFire.Mark("tactical.patch.perk-ids.cold-steel-morale.transpiler");
            return PerkIdTranspiler.Apply("Regiment.UpdateMorale", instructions, code => PerkIdIlRewrites.RewriteUpdateMorale(
                code,
                PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.MoraleColdSteel))));
        }
    }
}
