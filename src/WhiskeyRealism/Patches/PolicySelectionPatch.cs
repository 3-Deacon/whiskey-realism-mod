using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Policies.CheckAIPolicyChange (decompile 211017-211086) researches the first
    // available AI-personality policy. This Prefix intervenes only when fiscal or
    // grand-strategy scoring has a clear win, or swaps a fiscally suppressed first policy
    // for the first non-suppressed one. If every available policy is suppressed it lets
    // vanilla run (FiscalPolicySelector) instead of stalling research.
    [HarmonyPatch(typeof(Policies), "CheckAIPolicyChange")]
    internal static class PolicySelectionPatch
    {
        private static readonly MethodInfo IsDeactivatedMethod = AccessTools.Method(typeof(Policies), "IsDeactivated", new[] { typeof(Policy) });

        [HarmonyPrefix]
        internal static bool Prefix(int alliance)
        {
            using (TelemetryPerf.Scope("campaign.patch.policy-selection", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return true;
                if (StrategicCoordinator.Instance == null) return true;
                if (alliance < 0 || alliance >= 2) return true;
                if (GameVars.frame <= 50) return true;
                if (GameVars.alliance == null || alliance >= GameVars.alliance.Length) return true;
                if (alliance == GameVars.playeralliance && !GameVars.automanagepolicies && !GameVars.ai_vs_ai) return true;

                var state = GameVars.alliance[alliance];
                if (state == null) return true;

                var intent = StrategicCoordinator.Instance.FiscalIntents != null && alliance < StrategicCoordinator.Instance.FiscalIntents.Length
                    ? StrategicCoordinator.Instance.FiscalIntents[alliance]
                    : null;
                if (intent == null) return true;

                var personality = state.GetAIPersonality(alliance);
                if (personality == null || personality.policies == null) return true;
                if (personality.id == GamePrefs.emergencyaipolicycredit || personality.id == GamePrefs.emergencyaipolicyrecruiting) return true;
                if (state.GetActivatedPolicies(includeactsinresearch: true, includegrandpolicies: false, researchedonly: true) > 0) return true;

                var profile = ResolveProfile(alliance);
                var ids = new List<int>();
                var scores = new List<float>();
                var fiscalScores = new List<float>();
                var strategyScores = new List<float>();

                for (int i = 0; i < personality.policies.Count; i++)
                {
                    int policyId = personality.policies[i];
                    var policy = Policy.GetPolicyFromID(policyId);
                    if (!IsAvailable(state, policy, alliance)) continue;

                    float fiscalScore = FiscalPolicyScorer.PolicyWeight(intent, alliance, policyId);
                    float strategyScore = GrandStrategyPolicyScorer.PolicyWeight(profile, alliance, policyId);
                    ids.Add(policyId);
                    scores.Add(fiscalScore < 0f ? fiscalScore : fiscalScore + strategyScore);
                    fiscalScores.Add(fiscalScore);
                    strategyScores.Add(strategyScore);
                }

                // Index -1 => vanilla researches its first available policy. The selector
                // never blocks research when every available policy is penalised.
                var decision = FiscalPolicySelector.Select(ids, scores);
                if (decision.Index < 0) return true;

                int pick = decision.Index;
                int firstVanillaPolicy = ids[0];
                float firstVanillaScore = scores[0];
                int selectedPolicyId = ids[pick];
                float selectedScore = scores[pick];
                float selectedFiscalScore = fiscalScores[pick];
                float selectedStrategyScore = strategyScores[pick];
                string reason = decision.Reason == "score-win"
                    ? (selectedStrategyScore > selectedFiscalScore ? "strategy-win" : "fiscal-win")
                    : decision.Reason;

                var selected = Policy.GetPolicyFromID(selectedPolicyId);
                if (selected == null) return true;

                OnceLog.Info("policy-selection", "PolicySelectionPatch wired");
                Policies.AddResearch(alliance, selected);
                EmitFiscalIntentPolicy(
                    alliance,
                    "select-policy",
                    intent,
                    firstVanillaPolicy,
                    selectedPolicyId,
                    firstVanillaScore,
                    selectedScore,
                    selectedFiscalScore,
                    selectedStrategyScore,
                    reason);
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-selection:prefix", "[Patch:PolicySelection] prefix failed: " + ex.Message);
                return true;
            }
            } // end using TelemetryPerf.Scope
        }

        private static GrandStrategyProfile ResolveProfile(int alliance)
        {
            var coordinator = StrategicCoordinator.Instance;
            EraStage stage = EraStage.Amateur1861;
            if (coordinator != null
                && coordinator.Eras != null
                && alliance >= 0
                && alliance < coordinator.Eras.Length
                && coordinator.Eras[alliance] != null)
            {
                stage = coordinator.Eras[alliance].Stage;
            }

            return GrandStrategyRegistry.Resolve(alliance, stage);
        }

        private static void EmitFiscalIntentPolicy(
            int alliance,
            string decision,
            FiscalOutput intent,
            int vanillaPolicyId,
            int selectedPolicyId,
            float vanillaScore,
            float selectedScore,
            float fiscalScore,
            float strategyScore,
            string reason)
        {
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "-" : reason;
            string signature = "source=PolicySelection|alliance=" + alliance +
                "|decision=" + decision +
                "|vanilla=" + vanillaPolicyId +
                "|selected=" + selectedPolicyId +
                "|reason=" + safeReason;
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "FiscalIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithDecision(decision, safeReason, signature)
                .WithField("source", "PolicySelection")
                .WithField("posture", intent != null ? intent.Posture.ToString() : "-")
                .WithField("vanillaPolicy", vanillaPolicyId)
                .WithField("selectedPolicy", selectedPolicyId)
                .WithField("vanillaScore", vanillaScore)
                .WithField("selectedScore", selectedScore)
                .WithField("fiscalScore", fiscalScore)
                .WithField("strategyScore", strategyScore));
        }

        private static bool IsAvailable(GameVars.Alliance state, Policy policy, int alliance)
        {
            try
            {
                if (state == null || policy == null) return false;
                if (state.activatedpolicies == null || state.activatedacts == null) return false;
                if (state.activatedpolicies.Contains(policy)) return false;
                if (state.activatedacts.Contains(policy)) return false;
                if (policy.ReferedChapter > Policy.CurrentChapter) return false;
                if (!policy.HasPrePolicies()) return false;
                if (IsPolicyDeactivated(policy)) return false;
                if (policy.blocked) return false;
                if (!policy.IsAvailableInScenario()) return false;
                if (!(state.GetActivatedPolicies(includeactsinresearch: true, includegrandpolicies: false, researchedonly: true) < 1 || policy.IsAct)) return false;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-selection:available", "[Patch:PolicySelection] policy availability check failed: " + ex.Message);
                return false;
            }
        }

        private static bool IsPolicyDeactivated(Policy policy)
        {
            if (IsDeactivatedMethod == null)
            {
                OnceLog.Warning("policy-selection:isdeactivated-method", "[Patch:PolicySelection] Policies.IsDeactivated missing; fiscal policy steering disabled for candidate");
                return true;
            }

            try
            {
                return (bool)IsDeactivatedMethod.Invoke(null, new object[] { policy });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-selection:isdeactivated", "[Patch:PolicySelection] Policies.IsDeactivated failed: " + ex.Message);
                return true;
            }
        }
    }
}
