using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.Update (decompile 11159-11202) runs floor(sqrt(gamespeed))
    // private UpdateUnitAI passes per frame (11195-11199; 4 at 20x, 7 at 50x, still 1 at
    // speed 0), then UpdateCompanyFoundationList + CBuilding.WorkDownConstructionWishes.
    // Each UpdateUnitAI pass advances one CheckOffensiveMovements unit (11319-11337) and
    // one job of the per-faction job table (11372-11629).
    //
    // At >= 20x this Prefix replaces the pass loop with a backlog scheduler: it owes
    // vanilla's passes every frame and pays them with smoothing (guaranteed passes, frame
    // budget, forced catch-up beyond MaxBacklogFrames), so job-cycle progression never
    // falls behind vanilla. Once nothing is owed it runs the Fast Forward AI catch-up
    // extras itself (FastForwardAiCatchUpPatch stands down while the governor is on).
    // Paused post-initialization updates are skipped.
    [HarmonyPatch(typeof(AICampaign), "Update")]
    internal static class CampaignAiUpdateGovernorPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(AICampaign __instance)
        {
            using (TelemetryPerf.Scope("campaign.patch.campaign-ai-governor", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.campaign-ai-governor");
                try
                {
                    if (__instance == null) return true;
                    var plugin = Plugin.Instance;
                    if (plugin == null || !plugin.Enabled.Value) return true;
                    if (GameVars.debug_turnoffcampaignai) return false;

                    bool initialized = Initialized(__instance);
                    if (initialized && FastForwardAiScheduler.ShouldSkipCampaignAiUpdate(GameVars.gamepaused, GameVars.gamespeed))
                    {
                        OnceLog.Info("campaign-ai-paused", "[Patch:CampaignAIGovernor] paused AICampaign.Update guard active");
                        return false;
                    }

                    if (!plugin.CampaignAiGovernorEnabled.Value || GameVars.gamespeed < 20f || GameVars.debug_showcampaignaimapvalues)
                    {
                        _owedPasses = 0f;
                        return true;
                    }

                    if (!initialized)
                    {
                        var initialize = ResolveInitializeAi();
                        if (initialize == null) return true;
                        initialize.Invoke(__instance, null);
                        return false;
                    }

                    var updateUnitAi = ResolveUpdateUnitAi();
                    var updateCompanyFoundationList = ResolveUpdateCompanyFoundationList();
                    var workDownConstructionWishes = ResolveWorkDownConstructionWishes();
                    if (updateUnitAi == null || updateCompanyFoundationList == null || workDownConstructionWishes == null)
                        return true;

                    OnceLog.Info("campaign-ai-governor", "CampaignAiUpdateGovernorPatch wired (bounded high-speed AICampaign.Update wrapper)");

                    float gameSpeed = GameVars.gamespeed;
                    int desiredPasses = FastForwardAiScheduler.VanillaPasses(gameSpeed);
                    var options = BuildOptions(plugin);
                    var watch = Stopwatch.StartNew();
                    int executedPasses = 0;
                    _owedPasses = FastForwardAiScheduler.AccrueOwedPasses(_owedPasses, gameSpeed);

                    while (FastForwardAiScheduler.ShouldRunGovernedPass(
                               executedPasses,
                               (float)watch.Elapsed.TotalMilliseconds,
                               _owedPasses,
                               gameSpeed,
                               options))
                    {
                        _owedPasses -= 1f;
                        executedPasses++;
                        updateUnitAi.Invoke(__instance, null);
                    }

                    int extraPasses = 0;
                    int unityFrame = Time.frameCount;
                    var catchUp = BuildCatchUpOptions(plugin);
                    if (FastForwardAiScheduler.ShouldThrottleAfterFrame((float)watch.Elapsed.TotalMilliseconds, 0f, catchUp))
                        _catchUpCooldownUntilFrame = FastForwardAiScheduler.CooldownUntilFrame(unityFrame, catchUp);
                    var catchUpWatch = Stopwatch.StartNew();
                    while (FastForwardAiScheduler.ShouldRunCatchUpPass(
                               extraPasses,
                               (float)watch.Elapsed.TotalMilliseconds,
                               (float)catchUpWatch.Elapsed.TotalMilliseconds,
                               _owedPasses,
                               gameSpeed,
                               options,
                               catchUp,
                               unityFrame,
                               _catchUpCooldownUntilFrame))
                    {
                        long passStart = Stopwatch.GetTimestamp();
                        extraPasses++;
                        updateUnitAi.Invoke(__instance, null);
                        float passMs = (float)((Stopwatch.GetTimestamp() - passStart) * 1000d / Stopwatch.Frequency);
                        if (FastForwardAiScheduler.ShouldThrottleAfterFrame(0f, passMs, catchUp))
                            _catchUpCooldownUntilFrame = FastForwardAiScheduler.CooldownUntilFrame(unityFrame, catchUp);
                    }

                    updateCompanyFoundationList.Invoke(__instance, null);
                    workDownConstructionWishes.Invoke(null, null);
                    watch.Stop();

                    LogSample(gameSpeed, desiredPasses, executedPasses, extraPasses, options, watch.Elapsed.TotalMilliseconds);
                    return false;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-ai-governor:prefix", "[Patch:CampaignAIGovernor] prefix failed: " + ex.Message);
                    _owedPasses = 0f;
                    return true;
                }
            }
        }

        // Vanilla passes owed but not yet run (bounded by MaxBacklogFrames x vanilla).
        private static float _owedPasses;
        private static int _catchUpCooldownUntilFrame;
        private static FieldInfo _initializedField;
        private static MethodInfo _initializeAiMethod;
        private static MethodInfo _updateUnitAiMethod;
        private static MethodInfo _updateCompanyFoundationListMethod;
        private static MethodInfo _workDownConstructionWishesMethod;
        private static readonly FastForwardAiLogGate _logGate = new FastForwardAiLogGate();

        private static CampaignAiGovernorOptions BuildOptions(Plugin plugin)
        {
            return new CampaignAiGovernorOptions
            {
                Enabled = plugin.CampaignAiGovernorEnabled.Value,
                MaxPassesAt20x = Math.Max(1, plugin.CampaignAiGovernorMaxPasses20x.Value),
                MaxPassesAt50x = Math.Max(1, plugin.CampaignAiGovernorMaxPasses50x.Value),
                FrameBudgetMs = Math.Max(0.1f, plugin.CampaignAiGovernorFrameBudgetMs.Value)
            };
        }

        // Fast Forward AI catch-up runs inside the governor (only when no vanilla work is
        // owed), so extras never displace vanilla's own job progression.
        private static FastForwardAiOptions BuildCatchUpOptions(Plugin plugin)
        {
            return new FastForwardAiOptions
            {
                Enabled = plugin.FastForwardAiCatchUp.Value,
                FrameBudgetMs = Math.Max(0f, plugin.FastForwardAiFrameBudgetMs.Value),
                MaxExtraPassesAt20x = plugin.FastForwardAi20xExtraPasses.Value,
                MaxExtraPassesAt50x = plugin.FastForwardAi50xExtraPasses.Value,
                SlowFrameThresholdMs = Math.Max(0.1f, plugin.FastForwardAiSlowFrameThresholdMs.Value),
                SlowFrameCooldownFrames = Math.Max(0, plugin.FastForwardAiSlowFrameCooldownFrames.Value)
            };
        }

        private static bool Initialized(AICampaign instance)
        {
            if (_initializedField == null)
                _initializedField = AccessTools.Field(typeof(AICampaign), "initialized");
            if (_initializedField == null)
            {
                OnceLog.Warning("campaign-ai-governor:initialized-field", "[Patch:CampaignAIGovernor] missing AICampaign.initialized");
                return true;
            }
            return Convert.ToBoolean(_initializedField.GetValue(instance));
        }

        private static MethodInfo ResolveInitializeAi()
        {
            if (_initializeAiMethod != null) return _initializeAiMethod;
            _initializeAiMethod = AccessTools.Method(typeof(AICampaign), "InitializeAI");
            if (_initializeAiMethod == null)
                OnceLog.Warning("campaign-ai-governor:init-method", "[Patch:CampaignAIGovernor] missing AICampaign.InitializeAI");
            return _initializeAiMethod;
        }

        private static MethodInfo ResolveUpdateUnitAi()
        {
            if (_updateUnitAiMethod != null) return _updateUnitAiMethod;
            _updateUnitAiMethod = AccessTools.Method(typeof(AICampaign), "UpdateUnitAI");
            if (_updateUnitAiMethod == null)
                OnceLog.Warning("campaign-ai-governor:update-method", "[Patch:CampaignAIGovernor] missing AICampaign.UpdateUnitAI");
            return _updateUnitAiMethod;
        }

        private static MethodInfo ResolveUpdateCompanyFoundationList()
        {
            if (_updateCompanyFoundationListMethod != null) return _updateCompanyFoundationListMethod;
            _updateCompanyFoundationListMethod = AccessTools.Method(typeof(AICampaign), "UpdateCompanyFoundationList");
            if (_updateCompanyFoundationListMethod == null)
                OnceLog.Warning("campaign-ai-governor:company-foundation-method", "[Patch:CampaignAIGovernor] missing AICampaign.UpdateCompanyFoundationList");
            return _updateCompanyFoundationListMethod;
        }

        private static MethodInfo ResolveWorkDownConstructionWishes()
        {
            if (_workDownConstructionWishesMethod != null) return _workDownConstructionWishesMethod;
            _workDownConstructionWishesMethod = AccessTools.Method(typeof(CBuilding), "WorkDownConstructionWishes");
            if (_workDownConstructionWishesMethod == null)
                OnceLog.Warning("campaign-ai-governor:construction-wishes-method", "[Patch:CampaignAIGovernor] missing CBuilding.WorkDownConstructionWishes");
            return _workDownConstructionWishesMethod;
        }

        private static void LogSample(
            float gameSpeed,
            int vanillaPasses,
            int executedPasses,
            int extraPasses,
            CampaignAiGovernorOptions options,
            double elapsedMs)
        {
            bool behind = _owedPasses >= 1f;
            bool budget = elapsedMs >= options.FrameBudgetMs;
            if (!behind && !budget) return;
            string signature = ((int)Math.Round(gameSpeed)) + "x:" + vanillaPasses + ":" + (behind ? "behind" : "budget");
            if (!_logGate.ShouldLog(signature)) return;
            TelemetryRouter.LegacyInfo(
                $"[DailyOps:Perf] source=CampaignAIGovernor speed={gameSpeed:F0} vanilla={vanillaPasses} " +
                $"executed={executedPasses} extra={extraPasses} owed={_owedPasses:F1} budgetMs={options.FrameBudgetMs:F2} " +
                $"elapsedMs={elapsedMs:F2} limit={(behind ? "backlog" : "budget")}",
                TelemetryLayer.Campaign);
        }
    }
}
