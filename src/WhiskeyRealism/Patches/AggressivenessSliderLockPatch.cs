using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #11 — Postfix on MainMenu.SwitchAIMode
    // (decompile line ~193739). Visually grey-out the campaign-aggressiveness
    // slider by snapping `aimode` back to 1.0 and overwriting the displayed
    // label to "Locked by Whiskey Realism" each time the user clicks an arrow.
    //
    // CRITICAL: SwitchAIMode is shared between BattlePanel ("AI Mode" — historic
    // vs dynamic per-battle) and CampaignPanel ("Aggressiveness" 5-step). We
    // ONLY lock the campaign-panel use; battle-panel toggle is per-scenario
    // scripting and unrelated to our strategic brain.
    [HarmonyPatch]
    internal static class AggressivenessSliderLockPatch
    {
        [HarmonyTargetMethod]
        internal static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("MainMenu");
            return AccessTools.Method(t, "SwitchAIMode", new[] { typeof(float) });
        }

        [HarmonyPostfix]
        internal static void Postfix(object __instance)
        {
            if (Plugin.Instance == null || !Plugin.Instance.OverrideVanillaSettings.Value) return;
            try
            {
                var component = __instance as Component;
                if (component == null) return;

                // Don't touch the battle-panel branch — it's a different setting.
                if (component.gameObject.name == "BattlePanel") return;

                OnceLog.Info("settings:slider", "AggressivenessSliderLockPatch wired");

                var t = __instance.GetType();
                var aimodeField = AccessTools.Field(t, "aimode");
                aimodeField?.SetValue(__instance, 1.0f);

                // Compute the side index the same way vanilla does.
                var selectedSideField = AccessTools.Field(t, "selectedside");
                int selectedSide = selectedSideField != null ? (int)selectedSideField.GetValue(__instance) : 0;
                int displaySlot = (selectedSide == 0) ? 1 : 0;

                var textField = AccessTools.Field(t, "aimodetext");
                if (textField?.GetValue(__instance) is Array textArr)
                {
                    if (displaySlot >= 0 && displaySlot < textArr.Length)
                    {
                        var textObj = textArr.GetValue(displaySlot);
                        var textProp = textObj?.GetType().GetProperty("text");
                        textProp?.SetValue(textObj, "Aggressiveness: Locked by Whiskey Realism", null);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[AggressivenessSliderLockPatch] " + ex.Message);
            }
        }
    }
}
