using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic.Planner;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-INTEL-001 (foresight F4b, patch #134). Vanilla SpottedEnemy.UpdateMarks (decompile 142773, private, one call per frame
    // for one registered enemy group) draws the chance of a new spotting mark on the W&L player's map from
    //     num3 = (intel x spottingfactor[1] + (1 - intel) x spottingfactor[0]) x Random(1 - v, 1 + v)      (142819)
    // where spottingfactor is the per-unit-type concealment array (002/A: -50, -60) and the pref meant is timefactorspottings (loaded
    // 55804-55806, never read: 0.1 and 0.006 game years). num3 is negative, 142820's max(0.001, num3) makes it a thousandth of a game year,
    // and a hidden group is marked every time it moves 50 map units.
    //
    // A non-skipping Prefix writes timefactorspottings into spottingfactor[0..1] for the length of the call and the Finalizer puts vanilla's
    // values back, also if vanilla throws, so vanilla's body runs unchanged. The swap is safe: spottingfactor is read only at 101203 (the
    // per-type concealment inside the unit-typed UnitIsHidden, which the one-argument UnitIsHidden that UpdateMarks and AddMark call skips,
    // unittype -1) and at 142819. The AI side's mark ledger takes the same pair through MarkRules.TimingPair, so both sides mark at one rate.
    // Gated on Fix Spotting Mark Timing and GameVars.usefow only: it changes the player's own map whether or not the planner runs.
    // Rollback: Campaign Planner / Fix Spotting Mark Timing. No Prefix-skip, no Transpiler.
    [HarmonyPatch]
    internal static class SpottingMarkTimingFixPatch
    {
        private static bool _logged;

        internal struct TimingSwap
        {
            public bool Active;
            public float Saved0, Saved1;
        }

        [HarmonyTargetMethod]
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(SpottedEnemy), "UpdateMarks", new[] { typeof(int) });
        }

        [HarmonyPrefix]
        internal static void Prefix(out TimingSwap __state)
        {
            __state = default(TimingSwap);
            using (TelemetryPerf.Scope("campaign.patch.spotting-mark-timing.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.spotting-mark-timing.prefix");
                try
                {
                    if (!Enabled() || !GameVars.usefow) return;
                    var spotting = GamePrefs.spottingfactor;
                    if (spotting == null || spotting.Length < 2) return;
                    if (!MarkRules.TimingPair(true, spotting, GamePrefs.timefactorspottings, out float s0, out float s1))
                    {
                        OnceLog.Warning("spotting-mark-timing:pair", "[Patch:SpottingMarkTiming] timefactorspottings is not usable; vanilla's mark timing left as is");
                        return;
                    }
                    __state = new TimingSwap { Active = true, Saved0 = spotting[0], Saved1 = spotting[1] };
                    spotting[0] = s0;
                    spotting[1] = s1;
                    if (!_logged)
                    {
                        _logged = true;   // the line is built once, not on each of the frames this runs
                        OnceLog.Info("spotting-mark-timing:swap", "[Patch:SpottingMarkTiming] UpdateMarks: spottingfactor " + __state.Saved0 + "," + __state.Saved1 + " -> timefactorspottings " + s0 + "," + s1 + " (BUG-INTEL-001)");
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("spotting-mark-timing:prefix", "[Patch:SpottingMarkTiming] prefix failed, vanilla left as is: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(TimingSwap __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.spotting-mark-timing.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.spotting-mark-timing.finalizer");
                if (!__state.Active) return;
                var spotting = GamePrefs.spottingfactor;
                if (spotting == null || spotting.Length < 2) return;
                spotting[0] = __state.Saved0;
                spotting[1] = __state.Saved1;
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.FixSpottingMarkTiming != null && Plugin.FixSpottingMarkTiming.Value;
            }
            catch { return false; }
        }
    }
}
