using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Autocalc (campaign autoresolve) reads battle perk ids 1 and 2 swapped and the
    // River Expedition factor on the wrong campaign id (BUG-AI-006, BUG-AI-007):
    // - FightUnit (21050) gives sharpshooterperkinfluenceautocalc on unit0.HasPerk(2) /
    //   GetPerkLevel(2) (21080-21082; battle 2 is Zouave, Sharpshooters 1), spares unit1 the
    //   high-fatigue morale hit on !HasPerk(1) (21247; in battle that is Zouave 2, UpdateMorale
    //   128659), and applies fightingstrengthembarkedriverexp on topUnit.HasPerk(10) (21155;
    //   10 is Sappers and Miners, campaignprefs.txt 1363 names River Expedition, 17).
    // - UpdateFatigue (21345) applies perk_zouaves_fatiguefactor on HasPerk(1)/GetPerkLevel(1)
    //   (21368-21370; Regiment.UpdateFatigue 132665 uses 2).
    // The reads sit inside methods that also read the right ids on the same units, so no
    // Prefix swap can reach them. Narrow Transpilers pass each wrong constant through a
    // PerkIdIlHooks remap (vanilla id when the flag is off); on a pattern miss they log once
    // and leave vanilla. Rollback: Campaign / Fix Perk Id Mismatches.
    internal static class AutocalcPerkIdsPatch
    {
        [HarmonyPatch(typeof(Autocalc), "FightUnit")]
        internal static class FightUnitIds
        {
            [HarmonyTranspiler]
            internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.autocalc.fight-unit.transpiler");
                return PerkIdTranspiler.Apply("Autocalc.FightUnit", instructions, code => PerkIdIlRewrites.RewriteFightUnit(
                    code,
                    PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.AutocalcSharpshooters)),
                    PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.AutocalcZouave)),
                    PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.AutocalcRiverExpedition))));
            }
        }

        [HarmonyPatch(typeof(Autocalc), "UpdateFatigue")]
        internal static class FatigueIds
        {
            [HarmonyTranspiler]
            internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.autocalc.fatigue.transpiler");
                return PerkIdTranspiler.Apply("Autocalc.UpdateFatigue", instructions, code => PerkIdIlRewrites.RewriteAutocalcFatigue(
                    code,
                    PerkIdTranspiler.Hook(nameof(PerkIdIlHooks.AutocalcZouave))));
            }
        }
    }
}
