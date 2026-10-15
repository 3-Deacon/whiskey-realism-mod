using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Patches
{
    // IL edits for the perk-id transpilers (no [HarmonyPatch]; compiled into the test harness,
    // which runs them on the real game IL). Each Rewrite* finds every site it expects, by the
    // `ldc.i4 <id>; call(virt) <method>` pair plus an anchor where the pair repeats, and edits
    // only when every count matches. On any miss it returns false and leaves the list as it
    // was, so the caller hands vanilla's IL back unchanged. Edits keep labels: a constant is
    // passed through a remap call inserted after the ldc, a call is swapped in place for a
    // static hook with the same stack shape (instance first).
    internal static class PerkIdIlRewrites
    {
        internal static readonly MethodInfo HasPerk = AccessTools.Method(typeof(Regiment), nameof(Regiment.HasPerk));
        internal static readonly MethodInfo HasPerkParent = AccessTools.Method(typeof(Regiment), nameof(Regiment.HasPerkParent));
        internal static readonly MethodInfo GetPerkLevel = AccessTools.Method(typeof(Regiment), nameof(Regiment.GetPerkLevel));
        internal static readonly MethodInfo IncreasePerkExp = AccessTools.Method(typeof(Regiment), nameof(Regiment.IncreasePerkExp));
        internal static readonly MethodInfo CheckForGroupPerk = AccessTools.Method(typeof(BattleUnits), nameof(BattleUnits.CheckForGroupPerk));

        // Autocalc.FightUnit (21050): Sharpshooter bonus on unit0 id 2 (IL `ldarg.1; ldc.i4.2`,
        // 21080-21082), fatigue-morale immunity on unit1 id 1 (21247), embarked River
        // Expedition factor on topUnit id 10 (21155; the HasPerk(10) followed by
        // fightingstrengthembarkedriverexp, not the siege one at 21100).
        internal static bool RewriteFightUnit(List<CodeInstruction> code, MethodInfo sharpshootersId, MethodInfo zouaveId, MethodInfo riverExpeditionId)
        {
            var sharpHas = Sites(code, HasPerk, VanillaPerkTables.BattleZouave);
            var sharpLevel = Sites(code, GetPerkLevel, VanillaPerkTables.BattleZouave);
            var zouaveHas = Sites(code, HasPerk, VanillaPerkTables.BattleSharpshooters);
            var embarked = Sites(code, HasPerk, VanillaPerkTables.CampaignSappersAndMiners)
                .FindAll(i => LoadsFieldWithin(code, i, "fightingstrengthembarkedriverexp", 6));
            if (sharpHas.Count != 1 || sharpLevel.Count != 1 || zouaveHas.Count != 1 || embarked.Count != 1) return false;

            var edits = new List<(int, MethodInfo)>
            {
                (sharpHas[0], sharpshootersId), (sharpLevel[0], sharpshootersId),
                (zouaveHas[0], zouaveId), (embarked[0], riverExpeditionId)
            };
            InsertRemaps(code, edits);
            return true;
        }

        // Autocalc.UpdateFatigue (21345): perk_zouaves_fatiguefactor on id 1 (21368-21370).
        internal static bool RewriteAutocalcFatigue(List<CodeInstruction> code, MethodInfo zouaveId)
        {
            var has = Sites(code, HasPerk, VanillaPerkTables.BattleSharpshooters);
            var level = Sites(code, GetPerkLevel, VanillaPerkTables.BattleSharpshooters);
            if (has.Count != 1 || level.Count != 1) return false;
            InsertRemaps(code, new List<(int, MethodInfo)> { (has[0], zouaveId), (level[0], zouaveId) });
            return true;
        }

        // Regiment.UpdateMorale (128125): `HasPerk(8) && GetPerkLevel(9) >= 2` (128617), the
        // only HasPerk(8) in the method, followed by the GetPerkLevel(9).
        internal static bool RewriteUpdateMorale(List<CodeInstruction> code, MethodInfo coldSteelId)
        {
            var has = Sites(code, HasPerk, VanillaPerkTables.BattleExpertScouts)
                .FindAll(i => CallsWithin(code, i, GetPerkLevel, VanillaPerkTables.BattleColdSteel, 4));
            if (has.Count != 1 || Sites(code, HasPerk, VanillaPerkTables.BattleExpertScouts).Count != 1) return false;
            InsertRemaps(code, new List<(int, MethodInfo)> { (has[0], coldSteelId) });
            return true;
        }

        // Regiment.RegimentMove (133063): Foot Cavalry speed HasPerk(6)/HasPerkParent(6)
        // (133518-133524), Flying Column / Foot Cavalry XP IncreasePerkExp(…, 5/6)
        // (134113-134119), and the Zouave running sound HasPerk(2) (133734, 133743; not the
        // fleet Supply Colliers HasPerk(2) at 133405, which loads speedadjustmentsupplycolliers).
        internal static bool RewriteRegimentMove(
            List<CodeInstruction> code,
            MethodInfo footCavalryHas,
            MethodInfo footCavalryHasParent,
            MethodInfo marchExp,
            MethodInfo battleTableHas)
        {
            var foot = Sites(code, HasPerk, VanillaPerkTables.CampaignFootCavalry);
            var footParent = Sites(code, HasPerkParent, VanillaPerkTables.CampaignFootCavalry);
            var expFlying = Sites(code, IncreasePerkExp, VanillaPerkTables.CampaignFlyingColumn);
            var expFoot = Sites(code, IncreasePerkExp, VanillaPerkTables.CampaignFootCavalry);
            var allZouave = Sites(code, HasPerk, VanillaPerkTables.BattleZouave);
            var zouave = allZouave.FindAll(i => !LoadsFieldWithin(code, i, "speedadjustmentsupplycolliers", 6));
            if (foot.Count != 1 || footParent.Count != 1 || expFlying.Count != 1 || expFoot.Count != 1
                || allZouave.Count != 3 || zouave.Count != 2)
                return false;

            ReplaceCall(code, foot[0], footCavalryHas);
            ReplaceCall(code, footParent[0], footCavalryHasParent);
            ReplaceCall(code, expFlying[0], marchExp);
            ReplaceCall(code, expFoot[0], marchExp);
            ReplaceCall(code, zouave[0], battleTableHas);
            ReplaceCall(code, zouave[1], battleTableHas);
            return true;
        }

        // BattleUnits.CalculateCommanderFame (83890): CheckForGroupPerk(reg, 14) (83930) and
        // fameadjustmentembeddedreporters[reg.GetPerkLevel(14)] (83932).
        internal static bool RewriteCommanderFame(List<CodeInstruction> code, MethodInfo findHolder, MethodInfo holderLevel)
        {
            var find = Sites(code, CheckForGroupPerk, VanillaPerkTables.CampaignEmbeddedReporters);
            var level = Sites(code, GetPerkLevel, VanillaPerkTables.CampaignEmbeddedReporters);
            if (find.Count != 1 || level.Count != 1) return false;
            ReplaceCall(code, find[0], findHolder);
            ReplaceCall(code, level[0], holderLevel);
            return true;
        }

        // Index of every call to `method` whose argument just before it is the constant `id`.
        internal static List<int> Sites(List<CodeInstruction> code, MethodInfo method, int id)
        {
            var sites = new List<int>();
            if (method == null) return sites;
            for (int i = 1; i < code.Count; i++)
            {
                if (!code[i].Calls(method)) continue;
                if (code[i - 1].LoadsConstant(id)) sites.Add(i);
            }
            return sites;
        }

        private static bool LoadsFieldWithin(List<CodeInstruction> code, int from, string fieldName, int window)
        {
            for (int i = from + 1; i < code.Count && i <= from + window; i++)
                if (code[i].operand is FieldInfo field && field.Name == fieldName) return true;
            return false;
        }

        private static bool CallsWithin(List<CodeInstruction> code, int from, MethodInfo method, int id, int window)
        {
            for (int i = from + 1; i < code.Count && i <= from + window; i++)
                if (code[i].Calls(method) && code[i - 1].LoadsConstant(id)) return true;
            return false;
        }

        // Inserts `call remap(int) -> int` between each site's constant and its call, highest
        // index first so earlier indices stay valid.
        private static void InsertRemaps(List<CodeInstruction> code, List<(int call, MethodInfo remap)> edits)
        {
            edits.Sort((a, b) => b.call.CompareTo(a.call));
            foreach (var (call, remap) in edits)
                code.Insert(call, new CodeInstruction(OpCodes.Call, remap));
        }

        private static void ReplaceCall(List<CodeInstruction> code, int index, MethodInfo hook)
        {
            code[index].opcode = OpCodes.Call;
            code[index].operand = hook;
        }
    }
}
