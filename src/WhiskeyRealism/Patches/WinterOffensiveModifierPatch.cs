using System;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-004. Vanilla AICampaign.CheckOffensiveMovements (decompile 14166-14459)
    // multiplies the timediff term of its offensive roll num9 (14339) by num5, set in
    // winter months to GamePrefs.winterdefensiveoperationsmodifier (14310-14312); the idle
    // term added after it (14340, min(1, idle^3)) is not scaled. The offensive one,
    // winteroffensiveoperationsmodifier ("Probability modifier for offensive operations in
    // winter", campaignprefs 1546, 0.05), is loaded (56381) and never read; the defensive
    // one (1548, 0.2) belongs to CheckForDefensiveOperations (13565-13567).
    //
    // The roll's inputs are locals, so for the duration of the call the Prefix stores the
    // offensive modifier in the defensive pref through a ValueSwap (the method's only read
    // of it, 14312; no callee reads it: its other reader is CheckForDefensiveOperations)
    // and the Finalizer restores it, even if vanilla throws. #38's mirror of the roll
    // (CoordinatedOffensiveOperationsPatch.WinterModifier) reads the same helper.
    // Rollback: Campaign Bug Fixes / Enable Winter Offensive Modifier Fix.
    [HarmonyPatch(typeof(AICampaign), "CheckOffensiveMovements")]
    internal static class WinterOffensiveModifierPatch
    {
        private static readonly ValueSwap<float> DefensivePref = new ValueSwap<float>(
            () => GamePrefs.winterdefensiveoperationsmodifier,
            value => GamePrefs.winterdefensiveoperationsmodifier = value);

        [HarmonyPrefix]
        internal static void Prefix()
        {
            using (TelemetryPerf.Scope("campaign.patch.winter-offensive-modifier.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.winter-offensive-modifier.prefix");
                try
                {
                    if (!Enabled()) return;
                    float roll = WinterOperationsModifier.OffensiveRollModifier(true, true, GamePrefs.winteroffensiveoperationsmodifier, GamePrefs.winterdefensiveoperationsmodifier);
                    if (DefensivePref.Apply(roll))
                        OnceLog.Info("winter-offensive-modifier:active", "[Patch:WinterOffensive] vanilla's winter offensive roll reads winteroffensiveoperationsmodifier");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("winter-offensive-modifier:prefix", "[Patch:WinterOffensive] prefix failed (vanilla modifier kept): " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            using (TelemetryPerf.Scope("campaign.patch.winter-offensive-modifier.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.winter-offensive-modifier.finalizer");
                try
                {
                    string failure = DefensivePref.Restore();
                    if (failure != null)
                        OnceLog.Warning("winter-offensive-modifier:restore", "[Patch:WinterOffensive] restoring the defensive modifier failed: " + failure);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("winter-offensive-modifier:finalizer", "[Patch:WinterOffensive] finalizer failed: " + ex.Message);
                }
            }
        }

        internal static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableWinterOffensiveModifierFix != null
                    && Plugin.EnableWinterOffensiveModifierFix.Value;
            }
            catch { return false; }
        }
    }
}
