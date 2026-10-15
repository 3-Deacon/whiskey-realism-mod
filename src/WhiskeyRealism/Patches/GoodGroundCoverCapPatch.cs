using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-WL-015. Regiment.CheckCoyCovers (138343-138390) sums each block's cover value and,
    // while W&L incident 18 "A Good Ground" is active for the player's command
    // (IsIncidentActive(18) && dlcw_isundercommander, 138373-138376), multiplies it by
    // dlcwl_incidentgoodgroundcovermultiplier (dlcwl_config.dat 233-234 = 2.0) with no ceiling.
    // Behind parapets that is 1.0 or more, and SufferCasualties multiplies ranged losses by
    // clamp01(1 - covervalue) (126510): no losses at all for the whole battle. The incident text
    // promises "an upper hand" (incidents.dat 228). Integrator decision 2026-10-15: cap the
    // doubled cover at 0.9 (TerrainBugFixes.CapGoodGroundCover; never below the undoubled
    // cover). The Postfix repeats vanilla's own early-return and incident tests, then rewrites
    // covervalue and covervaluetemp (vanilla copies one to the other at the end). Battle only;
    // affects only the W&L player's command, as vanilla's incident does. Rollback: W&L / Fix
    // Good Ground Cover Cap.
    [HarmonyPatch(typeof(Regiment), "CheckCoyCovers")]
    internal static class GoodGroundCoverCapPatch
    {
        private const int GoodGroundIncident = 18; // incidents.dat 218 "A Good Ground"

        // The proof line is formatted once per battle, not per cover update.
        private static bool _logged;

        static GoodGroundCoverCapPatch()
        {
            BattleScope.Register("good-ground-cover-cap-log", () => _logged = false);
        }

        internal static bool Enabled()
        {
            try { return Plugin.FixGoodGroundCoverCap != null && Plugin.FixGoodGroundCoverCap.Value; }
            catch { return false; }
        }

        [HarmonyPostfix]
        internal static void Postfix(Regiment __instance)
        {
            // Every company-cover update of every battle unit: return before the scope unless
            // the W&L incident can have applied.
            if (!DLC_WL.dlc_scenarioactive || !__instance.dlcw_isundercommander) return;
            using (TelemetryPerf.Scope("tactical.patch.good-ground-cover-cap", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.good-ground-cover-cap");
                try
                {
                    if (!Enabled()) return;
                    if (__instance.unittyp > 13 || (UnityEngine.Object)__instance.buildingassignment != null || __instance.GetLastTransmittedPath() >= 0) return;
                    if (!DLC_WL.IsIncidentActive(GoodGroundIncident)) return;
                    float doubled = __instance.covervalue;
                    float capped = TerrainBugFixes.CapGoodGroundCover(doubled, GamePrefs.dlcwl_incidentgoodgroundcovermultiplier, TerrainBugFixes.GoodGroundCoverCap);
                    if (capped == doubled) return;
                    __instance.covervalue = capped;
                    __instance.covervaluetemp = capped;
                    if (!_logged)
                    {
                        _logged = true;
                        OnceLog.Info("good-ground:cap", "[Patch:GoodGround] A Good Ground cover " + doubled.ToString("0.00") + " capped at " + capped.ToString("0.00"));
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("good-ground:postfix", "[Patch:GoodGround] CheckCoyCovers postfix failed, vanilla cover kept: " + ex.Message);
                }
            }
        }
    }
}
