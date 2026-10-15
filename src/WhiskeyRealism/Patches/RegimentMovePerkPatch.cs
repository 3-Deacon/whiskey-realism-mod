using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Regiment.RegimentMove (decompile 133063), every unit every frame:
    // - multiplies speed by forcedmarchspeedfootcavalry on HasPerk(6) / HasPerkParent(6)
    //   (133518-133524) for every campaign unit, fleets (6 = Sea Raiders) included, and on
    //   every move, though Foot Cavalry's tooltip and campaignprefs.txt 2402 give "forced march
    //   speed" (vanilla's forced march is doublequick | running: sickness 115153, XP 134116);
    // - grants Flying Column / Foot Cavalry XP (5/6) for marching to fleets too (134113-134119);
    // - plays the Zouave running sound on HasPerk(2) (133734, 133743) for battle HQs, whose 2 is
    //   Balloon Corps (BUG-AI-008, BUG-AI-009).
    // All three are locals or calls mid-method after the doublequick/running state is set
    // (133497-133516), so no Prefix can reach them and a Prefix on this per-frame method would
    // cost every unit every frame. A narrow Transpiler swaps those six calls for PerkIdIlHooks
    // with the same stack shape; the hooks run only where vanilla already made that call (the
    // campaign speed/XP path and the battle sound path), cost one flag read and a unit-type
    // test, and are timed under campaign.patch.perk-ids.regiment-move.* /
    // tactical.patch.perk-ids.regiment-move.zouave-sound. On a pattern miss: log once, vanilla.
    // Rollback: Campaign / Fix Perk Id Mismatches (ids), Campaign / Fix Perk Effect Scope
    // (forced-march gate).
    [HarmonyPatch(typeof(Regiment), nameof(Regiment.RegimentMove))]
    internal static class RegimentMovePerkPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            PatchFirstFire.Mark("campaign.patch.perk-ids.regiment-move.transpiler");
            return PerkIdTranspiler.Apply("Regiment.RegimentMove", instructions, code => PerkIdIlRewrites.RewriteRegimentMove(
                code,
                PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.FootCavalrySpeedHas)),
                PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.FootCavalrySpeedHasParent)),
                PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.MarchPerkExp)),
                PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.BattleTableHasPerk))));
        }
    }
}
