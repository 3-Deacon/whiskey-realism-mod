using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-AI-018. Vanilla AICampaign.GetWeaponStrengthFactor(Regiment) (16195) returns the
    // strength-weighted mean weapon factor of a group's armed basic units as num / num2 with
    // no zero guard (16211). A group whose armed units have no men left, or that has none
    // (an HQ with no basic units; allattachedunits holds the HQ itself, 129574), returns
    // 0/0 = NaN, which poisons the strength-weighted area means that sum it (13136-13143)
    // and makes CheckOffensiveMovements' outgunned tests false (14257, 14283, 14299). This
    // Postfix returns 0 for such a group (no armed men, no weapon strength), the value the
    // empty numerator gives. Rollback: Campaign Bug Fixes / Enable Group Weapon Strength Guard.
    [HarmonyPatch(typeof(AICampaign), nameof(AICampaign.GetWeaponStrengthFactor), new[] { typeof(Regiment) })]
    internal static class GroupWeaponStrengthGuardPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(ref float __result)
        {
            using (TelemetryPerf.Scope("campaign.patch.group-weapon-strength-guard", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.group-weapon-strength-guard");
                float guarded = ArmsRules.GroupWeaponFactor(__result);
                if (guarded == __result || !Enabled()) return;
                __result = guarded;
                OnceLog.Info("group-weapon-strength-guard", "[Patch:GroupWeaponStrength] a group with no armed men got weapon factor 0 instead of NaN");
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableGroupWeaponStrengthGuard != null
                    && Plugin.EnableGroupWeaponStrengthGuard.Value;
            }
            catch { return false; }
        }
    }
}
