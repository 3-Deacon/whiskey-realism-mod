using System;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #10 — Postfix on MainMenu.SetCampaignParameters
    // (decompile line 193675). Vanilla writes player-picked Aggressiveness +
    // Historic AI Personality into GameVars at campaign create. We override
    // Aggressiveness back to neutral 1.0 (our personality system carries that
    // dimension) and Historic to true (required for scripted succession).
    // Difficulty is intentionally left alone — it's skill-level preference.
    [HarmonyPatch]
    internal static class CampaignParametersLockPatch
    {
        [HarmonyTargetMethod]
        internal static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("MainMenu");
            return AccessTools.Method(t, "SetCampaignParameters");
        }

        [HarmonyPostfix]
        internal static void Postfix()
        {
            if (Plugin.Instance == null || !Plugin.Instance.OverrideVanillaSettings.Value) return;
            OnceLog.Info("settings:finalize", "CampaignParametersLockPatch wired");
            try
            {
                var gv = AccessTools.TypeByName("GameVars");
                if (gv == null) return;

                var aggField = AccessTools.Field(gv, "usedcampaignagressiveness");
                if (aggField != null)
                {
                    var prev = (float)aggField.GetValue(null);
                    aggField.SetValue(null, 1.0f);
                    if (Math.Abs(prev - 1.0f) > 0.001f)
                        Plugin.Log.LogInfo($"[Settings] Aggressiveness override: {prev:F2} → 1.0 (Mediocre — Whiskey Realism owns aggression via personality system)");
                }

                var histField = AccessTools.Field(gv, "usehistoricaipersonality");
                if (histField != null)
                {
                    var prev = (bool)histField.GetValue(null);
                    histField.SetValue(null, true);
                    if (!prev)
                        Plugin.Log.LogInfo("[Settings] Historic AI Personality override: false → true (required for scripted succession events)");
                }

                // Difficulty lock — index 3 (Hard) by default, configurable.
                // bonus mapping: bonus = (index/4) * GamePrefs.maxstartbonuscampaign
                // (vanilla ChangeBonus formula: steps = bonus/num2 mapped to 0..length-1).
                int idx = Math.Max(0, Math.Min(4, Plugin.Instance.LockedDifficulty.Value));
                var prefsType = AccessTools.TypeByName("GamePrefs");
                var maxBonusField = AccessTools.Field(prefsType, "maxstartbonuscampaign");
                if (maxBonusField != null)
                {
                    float maxBonus = (float)maxBonusField.GetValue(null);
                    float lockedBonus = maxBonus * (idx / 4f);

                    var bonusField = AccessTools.Field(gv, "usedcampaignbonus");
                    var bonusRunningField = AccessTools.Field(gv, "usedcampaignbonusrunning");
                    var casualtiesField = AccessTools.Field(gv, "casualtiesmodifier");
                    var casualtiesBonusField = AccessTools.Field(prefsType, "casualtiesmodifierbonus");

                    if (bonusField != null && casualtiesBonusField != null)
                    {
                        float prevBonus = (float)bonusField.GetValue(null);
                        float casualtiesBonusCoef = (float)casualtiesBonusField.GetValue(null);

                        bonusField.SetValue(null, lockedBonus);
                        bonusRunningField?.SetValue(null, lockedBonus);
                        casualtiesField?.SetValue(null, lockedBonus * casualtiesBonusCoef);

                        var labels = new[] { "Very Easy", "Easy", "Mediocre", "Hard", "Very Hard" };
                        if (Math.Abs(prevBonus - lockedBonus) > 0.001f)
                            Plugin.Log.LogInfo($"[Settings] Difficulty override: bonus {prevBonus:F2} → {lockedBonus:F2} ({labels[idx]} — historical Civil War brutality)");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[CampaignParametersLockPatch] " + ex.Message);
            }
        }
    }
}
