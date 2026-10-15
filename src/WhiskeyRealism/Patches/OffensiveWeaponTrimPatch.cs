using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-010. Vanilla AICampaign.CheckOffensiveMovements (decompile 14166-14459)
    // gathers units for an offensive and counts each with the blended weapon factor
    // w' = 1 * (1 - influenceweaponsonoffensives) + influenceweaponsonoffensives * w
    // (14239, 14274; CalculateZoneRecruitingProbs uses the same blend, 13137). The loop
    // that then drops surplus units (14286-14297) writes
    // w *= influenceweaponsonoffensives * w + (1 - influenceweaponsonoffensives) * 1
    // (14294), crediting a dropped unit back with w * w' instead of w', so the trim
    // stops early or late depending on weapon quality.
    //
    // The factor is a local inside the loop, so no Prefix/Postfix can reach it. This
    // narrow Transpiler (OffensiveWeaponTrimIl) inserts one call to TrimLeftOperand after
    // the trim's left-operand load; the hook returns 1 while the fix is on (w = 1 * w')
    // and w while it is off (vanilla), so the config flag is a live rollback. If the IL
    // pattern is not found exactly once (a game update), the method is left as vanilla
    // and one warning is logged. Transpilers carry no TelemetryPerf scope (AGENTS.md); the
    // hook marks first fire so a smoke log shows the rewritten IL ran.
    // Rollback: Campaign Bug Fixes / Enable Offensive Weapon Trim Fix.
    [HarmonyPatch(typeof(AICampaign), "CheckOffensiveMovements")]
    public static class OffensiveWeaponTrimPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var vanilla = instructions.ToList();
            try
            {
                var hook = AccessTools.Method(typeof(OffensiveWeaponTrimPatch), nameof(TrimLeftOperand));
                var result = OffensiveWeaponTrimIl.Transpile(vanilla, OffensiveWeaponTrimIl.ResolveWeaponStrengthFactor(),
                    OffensiveWeaponTrimIl.ResolveInfluenceField(), hook, out int matches);
                if (ReferenceEquals(result, vanilla))
                    OnceLog.Warning("offensive-weapon-trim:pattern", "[Patch:OffensiveWeaponTrim] trim IL pattern found " + matches + " times (expected 1); CheckOffensiveMovements left as vanilla");
                else
                    OnceLog.Info("offensive-weapon-trim:applied", "[Patch:OffensiveWeaponTrim] CheckOffensiveMovements trim loop now uses the blended weapon factor");
                return result;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("offensive-weapon-trim:transpiler", "[Patch:OffensiveWeaponTrim] transpiler failed; CheckOffensiveMovements left as vanilla: " + ex.Message);
                return vanilla;
            }
        }

        // Called from the rewritten vanilla IL once per dropped unit.
        public static float TrimLeftOperand(float weaponFactor)
        {
            PatchFirstFire.Mark("campaign.patch.offensive-weapon-trim.hook");
            return OffensiveWeaponTrimIl.LeftOperand(weaponFactor, Enabled());
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableOffensiveWeaponTrimFix != null
                    && Plugin.EnableOffensiveWeaponTrimFix.Value;
            }
            catch { return false; }
        }
    }
}
