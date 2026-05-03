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
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[CampaignParametersLockPatch] " + ex.Message);
            }
        }
    }
}
