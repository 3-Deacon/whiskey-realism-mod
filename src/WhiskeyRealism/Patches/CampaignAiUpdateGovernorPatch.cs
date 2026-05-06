using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.Update computes floor(sqrt(GameVars.gamespeed)) and
    // runs that many private UpdateUnitAI passes in one Unity frame. At 20x/50x
    // this is 4/7 full campaign-AI passes, and still at least one pass when
    // gamespeed is 0. This Prefix preserves vanilla initialization and
    // construction side effects while capping high-speed UpdateUnitAI passes
    // behind config and skipping post-initialization paused updates.
    [HarmonyPatch(typeof(AICampaign), "Update")]
    internal static class CampaignAiUpdateGovernorPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(AICampaign __instance)
        {
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

                if (!plugin.CampaignAiGovernorEnabled.Value) return true;
                if (GameVars.gamespeed < 20f) return true;
                if (GameVars.debug_showcampaignaimapvalues) return true;

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

                while (FastForwardAiScheduler.ShouldRunGovernedPass(
                           executedPasses,
                           (float)watch.Elapsed.TotalMilliseconds,
                           gameSpeed,
                           options))
                {
                    updateUnitAi.Invoke(__instance, null);
                    executedPasses++;
                }

                updateCompanyFoundationList.Invoke(__instance, null);
                workDownConstructionWishes.Invoke(null, null);
                watch.Stop();

                LogSample(gameSpeed, desiredPasses, executedPasses, options, watch.Elapsed.TotalMilliseconds);
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-ai-governor:prefix", "[Patch:CampaignAIGovernor] prefix failed: " + ex.Message);
                return true;
            }
        }

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
            int desiredPasses,
            int executedPasses,
            CampaignAiGovernorOptions options,
            double elapsedMs)
        {
            bool capped = executedPasses < desiredPasses;
            bool budget = elapsedMs >= options.FrameBudgetMs;
            string signature = ((int)Math.Round(gameSpeed)) + "x:" + desiredPasses + ":" + executedPasses + ":" + (budget ? "budget" : "cap");
            if (!capped && !budget) return;
            if (!_logGate.ShouldLog(signature)) return;
            Plugin.Log.LogInfo(
                $"[Patch:CampaignAIGovernor] speed={gameSpeed:F0} vanilla={desiredPasses} " +
                $"executed={executedPasses} budgetMs={options.FrameBudgetMs:F2} elapsedMs={elapsedMs:F2} " +
                $"limit={(budget ? "budget" : "cap")}");
        }
    }
}
