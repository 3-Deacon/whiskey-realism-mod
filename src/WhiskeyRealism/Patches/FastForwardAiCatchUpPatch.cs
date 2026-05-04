using System;
using System.Diagnostics;
using HarmonyLib;
using WhiskeyRealism.Strategic;
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
        [HarmonyPostfix]
        internal static void Postfix(AICampaign __instance)
        {
            try
            {
                if (__instance == null) return;
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (GameVars.debug_turnoffcampaignai || GameVars.tutorialactive || GameVars.gamepaused) return;

                float gameSpeed = GameVars.gamespeed;
                var options = BuildOptions();
                int maxExtra = FastForwardAiScheduler.MaxExtraPasses(gameSpeed, options);
                if (maxExtra <= 0) return;
                if (!RuntimeReady()) return;

                var updateUnitAi = ResolveUpdateUnitAi();
                if (updateUnitAi == null) return;

                OnceLog.Info("fast-forward-ai", "FastForwardAiCatchUpPatch wired (default-on bounded campaign AI catch-up)");

                var sw = Stopwatch.StartNew();
                int extra = 0;
                while (FastForwardAiScheduler.ShouldRunExtraPass(extra, (float)sw.Elapsed.TotalMilliseconds, gameSpeed, options))
                {
                    updateUnitAi.Invoke(__instance, null);
                    extra++;
                }

                if (extra > 0)
                {
                    int vanillaPasses = FastForwardAiScheduler.VanillaPasses(gameSpeed);
                    bool budgetExhausted = extra < maxExtra && sw.Elapsed.TotalMilliseconds >= options.FrameBudgetMs;
                    string signature = FastForwardAiScheduler.LogSignature(gameSpeed, vanillaPasses, extra, maxExtra, budgetExhausted);
                    if (_logGate.ShouldLog(signature))
                    {
                        Plugin.Log.LogInfo(
                            $"[Patch:FastForwardAI] speed={gameSpeed:F0} vanilla={vanillaPasses} " +
                            $"extra={extra}/{maxExtra} elapsedMs={sw.Elapsed.TotalMilliseconds:F2} " +
                            $"limit={(budgetExhausted ? "budget" : "cap")}");
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("fast-forward-ai:postfix", "[Patch:FastForwardAI] postfix failed: " + ex.Message);
            }
        }

        private static System.Reflection.MethodInfo _updateUnitAiMethod;
        private static System.Reflection.FieldInfo _aifactionField;
        private static readonly FastForwardAiLogGate _logGate = new FastForwardAiLogGate();

        private static FastForwardAiOptions BuildOptions()
        {
            return new FastForwardAiOptions
            {
                Enabled = Plugin.Instance.FastForwardAiCatchUp.Value,
                FrameBudgetMs = Math.Max(0f, Plugin.Instance.FastForwardAiFrameBudgetMs.Value),
                MaxExtraPassesAt20x = Plugin.Instance.FastForwardAi20xExtraPasses.Value,
                MaxExtraPassesAt50x = Plugin.Instance.FastForwardAi50xExtraPasses.Value
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
    }
}
