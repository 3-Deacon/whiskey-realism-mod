using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla UpdateFinancialAI issues/redeems bonds, nudges one random tax lane,
    // and raises/cuts one random subsidy lane. This Postfix keeps vanilla bond
    // behavior intact, then applies bounded corrections from FiscalIntent.
    [HarmonyPatch(typeof(AICampaign), "UpdateFinancialAI")]
    internal static class FinancialAIPatch
    {
        private static readonly FinancialAiLogGate _logGate = new FinancialAiLogGate();

        [HarmonyPostfix]
        internal static void Postfix(int alliance)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;
                if (alliance < 0 || alliance >= 2) return;
                if (GameVars.frame <= 50) return;
                if (GameVars.gamepaused) return;
                if (alliance == GameVars.playeralliance && !GameVars.automanagefinances && !GameVars.ai_vs_ai) return;
                if (GameVars.alliance == null || alliance >= GameVars.alliance.Length) return;

                var intent = StrategicCoordinator.Instance.FiscalIntents[alliance];
                if (intent == null) return;
                var state = GameVars.alliance[alliance];
                if (state == null || state.tax == null || state.subsidies == null) return;
                var aiPersonality = state.GetAIPersonality(alliance);
                if (aiPersonality == null) return;

                OnceLog.Info("financial-ai", "FinancialAIPatch wired");

                int moves = 0;
                moves += MoveTaxTowardTarget(alliance, state.tax, intent, 1);
                if (moves < 2) moves += MoveTaxTowardTarget(alliance, state.tax, intent, 2);
                if (moves < 2) moves += MoveSubsidyTowardTarget(alliance, state.subsidies, intent, aiPersonality.subsidyfocus, 3);
                if (moves < 2) moves += MoveSubsidyTowardTarget(alliance, state.subsidies, intent, aiPersonality.subsidyfocus, 4);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("financial-ai:postfix", "[Patch:FinancialAI] postfix failed: " + ex.Message);
            }
        }

        private static int MoveTaxTowardTarget(int alliance, float[] tax, FiscalOutput intent, int lane)
        {
            if (lane < 0 || lane >= tax.Length || lane >= intent.TargetTaxMin.Length || lane >= intent.TargetTaxMax.Length) return 0;
            float old = tax[lane];
            float targetMin = UnityEngine.Mathf.Clamp(intent.TargetTaxMin[lane], 0f, 1f);
            float targetMax = UnityEngine.Mathf.Clamp(intent.TargetTaxMax[lane], 0f, 1f);
            float step = GamePrefs.taxstepsai;

            if (old < targetMin)
                tax[lane] = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.Min(targetMin, old + step), 0f, 1f);
            else if (old > targetMax)
                tax[lane] = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.Max(targetMax, old - step), 0f, 1f);
            else
                return 0;

            if (NearlyEqual(old, tax[lane])) return 0;
            LogCorrection(alliance, "tax", lane, old, tax[lane], intent.Posture);
            return 1;
        }

        private static int MoveSubsidyTowardTarget(int alliance, float[] subsidies, FiscalOutput intent, float[] subsidyFocus, int lane)
        {
            if (lane < 0 || lane >= subsidies.Length || lane >= intent.TargetSubsidyMin.Length || lane >= intent.TargetSubsidyMax.Length) return 0;
            float old = subsidies[lane];
            float targetMin = intent.TargetSubsidyMin[lane];
            float targetMax = intent.TargetSubsidyMax[lane];
            float step = GamePrefs.taxstepsai;
            float? focusCap = null;

            if (subsidyFocus != null && lane < subsidyFocus.Length)
            {
                focusCap = subsidyFocus[lane];
                targetMax = UnityEngine.Mathf.Min(targetMax, focusCap.Value);
            }

            if (focusCap.HasValue && old > focusCap.Value)
                subsidies[lane] = focusCap.Value;
            else if (old < targetMin)
                subsidies[lane] = UnityEngine.Mathf.Min(targetMax, UnityEngine.Mathf.Min(targetMin, old + step));
            else if (old > targetMax)
                subsidies[lane] = UnityEngine.Mathf.Max(targetMax, old - step);
            else
                return 0;

            if (NearlyEqual(old, subsidies[lane])) return 0;
            LogCorrection(alliance, "subsidy", lane, old, subsidies[lane], intent.Posture);
            return 1;
        }

        private static void LogCorrection(int alliance, string laneType, int lane, float oldValue, float newValue, FiscalPosture posture)
        {
            string signature = FinancialAiLogGate.Signature(alliance, laneType, lane, oldValue, newValue, posture);
            if (!_logGate.ShouldLog(signature)) return;

            string label = laneType == "tax" ? "taxLane" : "subsidyLane";
            Plugin.Log.LogInfo(
                $"[Patch:FinancialAI] alliance={alliance} {label}={lane} old={oldValue:F2} new={newValue:F2} posture={posture}");
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) < 0.0001f;
        }
    }
}
