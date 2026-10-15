using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla BattleUnits.CalculateCommanderFame (decompile 83890), after a battle, multiplies a
    // corps/army commander's fame by fameadjustmentembeddedreporters when
    // CheckForGroupPerk(reg, 14) finds Embedded Reporters (83930-83933). That search starts at
    // the parent (83866-83884), so a unit's own Embedded Reporters never count, and the level
    // is read from reg, not from the holder (BUG-AI-010). The reads sit inside the fame loop,
    // so a narrow Transpiler swaps the two calls for PerkIdIlHooks: the unit itself first, then
    // vanilla's parent search, with the holder's level. On a pattern miss: log once, vanilla.
    // Rollback: Campaign / Fix Perk Effect Scope.
    [HarmonyPatch(typeof(BattleUnits), nameof(BattleUnits.CalculateCommanderFame))]
    internal static class CommanderFameReportersPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            PatchFirstFire.Mark("tactical.patch.perk-ids.commander-fame.transpiler");
            return PerkIdTranspiler.Apply("BattleUnits.CalculateCommanderFame", instructions, code => PerkIdIlRewrites.RewriteCommanderFame(
                code,
                PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.FameReportersHolder)),
                PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.FameReportersLevel))));
        }
    }
}
