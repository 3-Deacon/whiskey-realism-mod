using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Bridge layer patch #13 — Postfix on MainMenu.ChangeBonus
    // (companion to AggressivenessSliderLockPatch). Locks the campaign-difficulty
    // slider to the value indicated by Plugin.Instance.LockedDifficulty
    // (default: index 3 = Hard).
    //
    // CRITICAL: ChangeBonus is shared between BattlePanel and CampaignPanel.
    // BattlePanel ranges from minstartbonusbattle..maxstartbonusbattle and is
    // a separate per-battle setting. We ONLY lock the campaign-panel use.
    [HarmonyPatch]
    internal static class DifficultySliderLockPatch
    {
        [HarmonyTargetMethod]
        internal static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("MainMenu");
            return AccessTools.Method(t, "ChangeBonus", new[] { typeof(float) });
        }

        [HarmonyPostfix]
        internal static void Postfix(object __instance)
        {
            if (Plugin.Instance == null || !Plugin.Instance.OverrideVanillaSettings.Value) return;
            try
            {
                var component = __instance as Component;
                if (component == null) return;
                if (component.gameObject.name == "BattlePanel") return;

                OnceLog.Info("settings:difficulty", "DifficultySliderLockPatch wired");

                int idx = Math.Max(0, Math.Min(4, Plugin.Instance.LockedDifficulty.Value));

                var prefsType = AccessTools.TypeByName("GamePrefs");
                var maxBonusField = AccessTools.Field(prefsType, "maxstartbonuscampaign");
                if (maxBonusField == null) return;
                float maxBonus = (float)maxBonusField.GetValue(null);
                float lockedBonus = maxBonus * (idx / 4f);

                var t = __instance.GetType();
                var bonusField = AccessTools.Field(t, "bonus");
                bonusField?.SetValue(__instance, lockedBonus);

                var selectedSideField = AccessTools.Field(t, "selectedside");
                int selectedSide = selectedSideField != null ? (int)selectedSideField.GetValue(__instance) : 0;
                int displaySlot = (selectedSide == 0) ? 1 : 0;

                var labels = new[] { "Very Easy", "Easy", "Mediocre", "Hard", "Very Hard" };
                var textField = AccessTools.Field(t, "bonustext");
                if (textField?.GetValue(__instance) is Array textArr)
                {
                    if (displaySlot >= 0 && displaySlot < textArr.Length)
                    {
                        var textObj = textArr.GetValue(displaySlot);
                        var textProp = textObj?.GetType().GetProperty("text");
                        textProp?.SetValue(textObj, "Locked:Realism", null);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[DifficultySliderLockPatch] " + ex.Message);
            }
        }
    }
}
