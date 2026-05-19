using HarmonyLib;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.MicroAICheckForRetreats(Regiment) at decompile line 4817
    // writes retreat paths/modes for units past the morale fallback threshold.
    // Postfix observer only.
    [HarmonyPatch(typeof(AIBattle), "MicroAICheckForRetreats")]
    public static class B8MicroAICheckForRetreatsObserverPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Regiment aigroup)
        {
            using (TelemetryPerf.Scope("tactical.patch.b8-retreats-observer", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    OnceLog.Info("b8-microai-check-retreats", "");
                }
                catch (System.Exception ex)
                {
                    OnceLog.Warning("b8-microai-check-retreats-error",
                        "[B8] MicroAICheckForRetreats observer error: " + ex.Message);
                }
            }
        }
    }
}
