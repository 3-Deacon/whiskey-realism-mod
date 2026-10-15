using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #105. Vanilla SceneManagement.SaveAll (decompile 36708) writes the units (SaveAllUnits),
    // objects, commanders and campaign data, with AICampaign.Save (36770, whose Postfix writes
    // the Whiskey sidecar) near the end. It runs for the save before a campaign battle
    // (PrepareLaunchingBattleFromCampaign, 36702), autosaves (66948) and manual saves (216812).
    // This non-skipping Prefix finishes a time-sliced daily strategic review that is still in
    // flight before vanilla saves anything, so the unit orders its remaining jobs issue are in
    // the saved units and the sidecar holds the completed day. No-op when no review is running.
    [HarmonyPatch(typeof(SceneManagement), "SaveAll")]
    internal static class DailyReviewSaveDrainPatch
    {
        [HarmonyPrefix]
        internal static void Prefix()
        {
            using (TelemetryPerf.Scope("campaign.patch.daily-review-save-drain", TelemetryLayer.Campaign, TelemetryCategory.Performance, 5.0))
            {
                PatchFirstFire.Mark("campaign.patch.daily-review-save-drain");
                try
                {
                    StrategicCoordinator.Instance?.DrainDailyReview();
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("daily-review-save-drain", "[DailyReviewSaveDrainPatch] drain before save failed: " + ex.Message);
                }
            }
        }
    }
}
