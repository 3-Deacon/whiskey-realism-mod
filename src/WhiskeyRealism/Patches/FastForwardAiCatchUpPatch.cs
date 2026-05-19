using System;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.Update scales campaign AI work as sqrt(gamespeed):
    // 20x runs four UpdateUnitAI passes per frame and 50x runs seven. This
    // Postfix adds a small, budgeted catch-up allowance at high campaign speeds
    // so strategic jobs are less starved during fast-forward without trying to
    // preserve full 1x AI density.
    [HarmonyPatch(typeof(AICampaign), "Update")]
    internal static class FastForwardAiCatchUpPatch
    {
        [HarmonyPrefix]
        internal static void Prefix()
        {
            using (TelemetryPerf.Scope("campaign.patch.fast-forward-ai-catchup.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                _updateStartTicks = Stopwatch.GetTimestamp();
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(AICampaign __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.fast-forward-ai-catchup.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
            try
            {
                if (__instance == null) return;
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (Plugin.Instance.CampaignAiGovernorEnabled.Value) return;
                if (GameVars.debug_turnoffcampaignai || GameVars.tutorialactive || GameVars.gamepaused) return;

                float gameSpeed = GameVars.gamespeed;
                var options = BuildOptions();
                int maxExtra = FastForwardAiScheduler.MaxExtraPasses(gameSpeed, options);
                if (maxExtra <= 0) return;
                if (!RuntimeReady()) return;

                var updateUnitAi = ResolveUpdateUnitAi();
                if (updateUnitAi == null) return;

                OnceLog.Info("fast-forward-ai", "FastForwardAiCatchUpPatch wired (default-on bounded campaign AI catch-up)");

                int frame = Time.frameCount;
                int vanillaPasses = FastForwardAiScheduler.VanillaPasses(gameSpeed);
                double vanillaElapsedMs = ElapsedMsSince(_updateStartTicks);
                if (FastForwardAiScheduler.InCooldown(frame, _cooldownUntilFrame))
                {
                    LogPerfSample(gameSpeed, "cooldown", vanillaPasses, 0, maxExtra, vanillaElapsedMs, 0d, 0d, frame);
                    return;
                }

                if (FastForwardAiScheduler.ShouldThrottleAfterFrame((float)vanillaElapsedMs, 0f, options))
                {
                    _cooldownUntilFrame = FastForwardAiScheduler.CooldownUntilFrame(frame, options);
                    LogPerfSample(gameSpeed, "vanilla-slow", vanillaPasses, 0, maxExtra, vanillaElapsedMs, 0d, 0d, frame);
                    return;
                }

                var sw = Stopwatch.StartNew();
                int extra = 0;
                double slowestExtraMs = 0d;
                while (FastForwardAiScheduler.ShouldRunExtraPass(extra, (float)sw.Elapsed.TotalMilliseconds, gameSpeed, options))
                {
                    long passStart = Stopwatch.GetTimestamp();
                    updateUnitAi.Invoke(__instance, null);
                    double passMs = ElapsedMsSince(passStart);
                    if (passMs > slowestExtraMs) slowestExtraMs = passMs;
                    extra++;
                    if (FastForwardAiScheduler.ShouldThrottleAfterFrame(0f, (float)passMs, options))
                    {
                        _cooldownUntilFrame = FastForwardAiScheduler.CooldownUntilFrame(frame, options);
                        break;
                    }
                }

                if (extra > 0)
                {
                    bool budgetExhausted = extra < maxExtra && sw.Elapsed.TotalMilliseconds >= options.FrameBudgetMs;
                    string signature = FastForwardAiScheduler.LogSignature(gameSpeed, vanillaPasses, extra, maxExtra, budgetExhausted);
                    if (_logGate.ShouldLog(signature))
                    {
                        TelemetryRouter.LegacyInfo(
                            $"[DailyOps:Perf] source=FastForwardAI speed={gameSpeed:F0} vanilla={vanillaPasses} " +
                            $"extra={extra}/{maxExtra} vanillaMs={vanillaElapsedMs:F2} " +
                            $"extraMs={sw.Elapsed.TotalMilliseconds:F2} slowestExtraMs={slowestExtraMs:F2} " +
                            $"limit={(budgetExhausted ? "budget" : "cap")}",
                            TelemetryLayer.Campaign);
                    }

                    if (FastForwardAiScheduler.InCooldown(frame, _cooldownUntilFrame))
                        LogPerfSample(gameSpeed, "extra-slow", vanillaPasses, extra, maxExtra, vanillaElapsedMs, sw.Elapsed.TotalMilliseconds, slowestExtraMs, frame);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fast-forward-ai:postfix", "[Patch:FastForwardAI] postfix failed: " + ex.Message);
            }
            } // end using TelemetryPerf.Scope
        }

        private static System.Reflection.MethodInfo _updateUnitAiMethod;
        private static System.Reflection.FieldInfo _aifactionField;
        private static readonly FastForwardAiLogGate _logGate = new FastForwardAiLogGate();
        private static long _updateStartTicks;
        private static int _cooldownUntilFrame;
        private static int _lastPerfLogFrame20x = -1000000;
        private static int _lastPerfLogFrame50x = -1000000;

        private static FastForwardAiOptions BuildOptions()
        {
            return new FastForwardAiOptions
            {
                Enabled = Plugin.Instance.FastForwardAiCatchUp.Value && !Plugin.Instance.CampaignAiGovernorEnabled.Value,
                FrameBudgetMs = Math.Max(0f, Plugin.Instance.FastForwardAiFrameBudgetMs.Value),
                MaxExtraPassesAt20x = Plugin.Instance.FastForwardAi20xExtraPasses.Value,
                MaxExtraPassesAt50x = Plugin.Instance.FastForwardAi50xExtraPasses.Value,
                SlowFrameThresholdMs = Math.Max(0.1f, Plugin.Instance.FastForwardAiSlowFrameThresholdMs.Value),
                SlowFrameCooldownFrames = Math.Max(0, Plugin.Instance.FastForwardAiSlowFrameCooldownFrames.Value)
            };
        }

        private static System.Reflection.MethodInfo ResolveUpdateUnitAi()
        {
            if (_updateUnitAiMethod != null) return _updateUnitAiMethod;
            _updateUnitAiMethod = AccessTools.Method(typeof(AICampaign), "UpdateUnitAI");
            if (_updateUnitAiMethod == null)
                OnceLog.Warning("fast-forward-ai:method", "[Patch:FastForwardAI] missing AICampaign.UpdateUnitAI");
            return _updateUnitAiMethod;
        }

        private static bool RuntimeReady()
        {
            try
            {
                if (BattleUnits.completeunitlist == null) return false;
                if (_aifactionField == null) _aifactionField = AccessTools.Field(typeof(AICampaign), "aifaction");
                var list = _aifactionField?.GetValue(null) as System.Collections.IList;
                return list != null && list.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static double ElapsedMsSince(long startTicks)
        {
            if (startTicks <= 0L) return 0d;
            long elapsed = Stopwatch.GetTimestamp() - startTicks;
            return elapsed * 1000d / Stopwatch.Frequency;
        }

        private static void LogPerfSample(
            float gameSpeed,
            string reason,
            int vanillaPasses,
            int extra,
            int maxExtra,
            double vanillaMs,
            double extraMs,
            double slowestExtraMs,
            int frame)
        {
            if (!ShouldLogPerf(gameSpeed, frame)) return;
            TelemetryRouter.LegacyInfo(
                $"[DailyOps:Perf] source=FastForwardAI speed={gameSpeed:F0} reason={reason} " +
                $"vanilla={vanillaPasses} extra={extra}/{maxExtra} " +
                $"vanillaMs={vanillaMs:F2} extraMs={extraMs:F2} slowestExtraMs={slowestExtraMs:F2} " +
                $"cooldownUntilFrame={_cooldownUntilFrame}",
                TelemetryLayer.Campaign);
        }

        private static bool ShouldLogPerf(float gameSpeed, int frame)
        {
            const int intervalFrames = 300;
            if (gameSpeed >= 50f)
            {
                if (frame - _lastPerfLogFrame50x < intervalFrames) return false;
                _lastPerfLogFrame50x = frame;
                return true;
            }

            if (frame - _lastPerfLogFrame20x < intervalFrames) return false;
            _lastPerfLogFrame20x = frame;
            return true;
        }
    }
}
