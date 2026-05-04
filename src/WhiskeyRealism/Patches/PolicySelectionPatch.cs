using System;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla CheckAIPolicyChange walks the active AI personality policy list.
    // This Prefix intervenes only when fiscal or grand-strategy policy scoring
    // has a clear win, or when the vanilla first-available policy is fiscally suppressed.
    [HarmonyPatch(typeof(Policies), "CheckAIPolicyChange")]
    internal static class PolicySelectionPatch
    {
        private const float MinimumPolicyScore = 0.75f;
        private const float PolicyWinMargin = 0.35f;
        private static readonly MethodInfo IsDeactivatedMethod = AccessTools.Method(typeof(Policies), "IsDeactivated", new[] { typeof(Policy) });

        [HarmonyPrefix]
        internal static bool Prefix(int alliance)
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
                int firstVanillaPolicy = -1;
                float firstVanillaScore = 0f;
                int firstNonSuppressedPolicy = -1;
                float firstNonSuppressedScore = 0f;
                float firstNonSuppressedFiscalScore = 0f;
                float firstNonSuppressedStrategyScore = 0f;
                int bestPolicy = -1;
                float bestScore = MinimumPolicyScore;
                float bestFiscalScore = 0f;
                float bestStrategyScore = 0f;

                for (int i = 0; i < personality.policies.Count; i++)
                {
                    int policyId = personality.policies[i];
                    var policy = Policy.GetPolicyFromID(policyId);
                    if (!IsAvailable(state, policy, alliance)) continue;

                    float fiscalScore = FiscalPolicyScorer.PolicyWeight(intent, alliance, policyId);
                    float strategyScore = GrandStrategyPolicyScorer.PolicyWeight(profile, alliance, policyId);
                    float score = fiscalScore < 0f ? fiscalScore : fiscalScore + strategyScore;
                    if (firstVanillaPolicy < 0)
                    {
                        firstVanillaPolicy = policyId;
                        firstVanillaScore = score;
                    }
                    if (firstNonSuppressedPolicy < 0 && score >= 0f)
                    {
                        firstNonSuppressedPolicy = policyId;
                        firstNonSuppressedScore = score;
                        firstNonSuppressedFiscalScore = fiscalScore;
                        firstNonSuppressedStrategyScore = strategyScore;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPolicy = policyId;
                        bestFiscalScore = fiscalScore;
                        bestStrategyScore = strategyScore;
                    }
                }

                string reason = "fiscal-win";
                int selectedPolicyId = -1;
                float selectedScore = 0f;
                float selectedFiscalScore = bestFiscalScore;
                float selectedStrategyScore = bestStrategyScore;
                if (bestPolicy >= 0 && bestPolicy != firstVanillaPolicy && bestScore >= firstVanillaScore + PolicyWinMargin)
                {
                    reason = bestStrategyScore > bestFiscalScore ? "strategy-win" : "fiscal-win";
                    selectedPolicyId = bestPolicy;
                    selectedScore = bestScore;
                }
                else if (firstVanillaPolicy >= 0 && firstVanillaScore < 0f)
                {
                    reason = "suppress-force-growth";
                    if (firstNonSuppressedPolicy < 0)
                    {
                        OnceLog.Info("policy-selection", "PolicySelectionPatch wired");
                        if (Plugin.Instance.VerboseLogging.Value)
                        {
                            Plugin.Log.LogInfo(
                                $"[Patch:PolicySelection] alliance={alliance} blocked={firstVanillaPolicy} " +
                                $"posture={intent.Posture} vanillaScore={firstVanillaScore:F2} reason={reason}");
                        }
                        return false;
                    }

                    selectedPolicyId = firstNonSuppressedPolicy;
                    selectedScore = firstNonSuppressedScore;
                    selectedFiscalScore = firstNonSuppressedFiscalScore;
                    selectedStrategyScore = firstNonSuppressedStrategyScore;
                }
                else
                {
                    return true;
                }

                var selected = Policy.GetPolicyFromID(selectedPolicyId);
                if (selected == null) return true;

                OnceLog.Info("policy-selection", "PolicySelectionPatch wired");
                Policies.AddResearch(alliance, selected);
                Plugin.Log.LogInfo(
                    $"[Patch:PolicySelection] alliance={alliance} policy={selectedPolicyId} " +
                    $"posture={intent.Posture} vanilla={firstVanillaPolicy} " +
                    $"vanillaScore={firstVanillaScore:F2} selectedScore={selectedScore:F2} " +
                    $"fiscalScore={selectedFiscalScore:F2} strategyScore={selectedStrategyScore:F2} reason={reason}");
                return false;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-selection:prefix", "[Patch:PolicySelection] prefix failed: " + ex.Message);
                return true;
            }
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
