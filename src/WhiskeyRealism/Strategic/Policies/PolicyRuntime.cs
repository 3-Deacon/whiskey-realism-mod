using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Policies
{
    // Reads vanilla policy state for the need-driven selection. Runtime adapter (not in the
    // test csproj). Every read degrades to "nothing eligible" on failure with one bounded
    // warning, so the caller falls back to vanilla's own pick.
    internal static class PolicyRuntime
    {
        // Policies.IsDeactivated (210957) is private: Union 36 before 4 Mar 1861, CSA 170
        // while Virginia is Union.
        private static readonly MethodInfo IsDeactivatedMethod = AccessTools.Method(typeof(global::Policies), "IsDeactivated", new[] { typeof(Policy) });

        internal sealed class Candidates
        {
            public readonly List<PolicyCandidateFacts> Facts = new List<PolicyCandidateFacts>();
            public int UsedSlots;
            public int MaxSlots;
            // prerequisite id → same-side, unresearched dependents not blocked by an unheld grand act
            public readonly Dictionary<int, List<int>> Dependents = new Dictionary<int, List<int>>();

            public IEnumerable<int> DependentsOf(int policyId)
            {
                return Dependents.TryGetValue(policyId, out var list) ? (IEnumerable<int>)list : Array.Empty<int>();
            }
        }

        internal static Candidates ReadCandidates(int alliance)
        {
            var result = new Candidates();
            try
            {
                if (alliance < 0 || alliance > 1) return result;
                var state = AllianceState(alliance);
                var all = Policy.allpolicies;
                if (state == null || all == null) return result;
                result.UsedSlots = state.GetActivatedPolicies();
                result.MaxSlots = state.maxpolicies;
                int chapter = Policy.CurrentChapter;
                for (int i = 0; i < all.Count; i++)
                {
                    var policy = all[i];
                    if (policy == null || PolicyEffectCatalog.AllianceOf(policy.UniquePolicyID) != alliance) continue;
                    result.Facts.Add(ReadFacts(state, policy, chapter));
                    AddDependentEdges(result.Dependents, policy, alliance);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-runtime:candidates", "[PolicyRuntime] candidate read failed: " + ex.Message);
                result.Facts.Clear();
                result.Dependents.Clear();
            }
            return result;
        }

        // Live re-check at the moment of the pick (state can change between daily reviews).
        internal static bool IsEligibleNow(int alliance, int policyId)
        {
            try
            {
                var state = AllianceState(alliance);
                var policy = Policy.GetPolicyFromID(policyId);
                if (state == null || policy == null) return false;
                return PolicyEligibility.IsEligible(ReadFacts(state, policy, Policy.CurrentChapter), alliance, out _);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-runtime:eligible-now", "[PolicyRuntime] live eligibility read failed: " + ex.Message);
                return false;
            }
        }

        // Vanilla's one-at-a-time gate (CheckAIPolicyChange 211061): anything in
        // activatedpolicies below progress 1, including a player's purchase.
        internal static bool HasResearchInProgress(int alliance)
        {
            var state = AllianceState(alliance);
            return state == null || state.GetActivatedPolicies(includeactsinresearch: true, includegrandpolicies: false, researchedonly: true) > 0;
        }

        // Policies.AddResearch (209554); true when the policy is now in research.
        internal static bool TryStartResearch(int alliance, int policyId)
        {
            var state = AllianceState(alliance);
            var policy = Policy.GetPolicyFromID(policyId);
            if (state == null || policy == null) return false;
            global::Policies.AddResearch(alliance, policy);
            return state.activatedpolicies != null && state.activatedpolicies.Contains(policy);
        }

        // The personality vanilla would walk (GetAIPersonality 62716), with its emergency kind.
        internal static int[] ReadPersonalityList(int alliance, out bool emergencyCredit, out bool emergencyManpower)
        {
            emergencyCredit = false;
            emergencyManpower = false;
            try
            {
                var state = AllianceState(alliance);
                var personality = state?.GetAIPersonality(alliance);
                if (personality == null || personality.policies == null) return Array.Empty<int>();
                emergencyCredit = personality.id == GamePrefs.emergencyaipolicycredit;
                emergencyManpower = personality.id == GamePrefs.emergencyaipolicyrecruiting;
                return personality.policies.ToArray();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-runtime:personality", "[PolicyRuntime] personality read failed: " + ex.Message);
                return Array.Empty<int>();
            }
        }

        private static PolicyCandidateFacts ReadFacts(GameVars.Alliance state, Policy policy, int chapter)
        {
            return new PolicyCandidateFacts
            {
                PolicyId = policy.UniquePolicyID,
                IsGrand = Policy.grandpolicies != null && Policy.grandpolicies.Contains(policy),
                IsAct = policy.IsAct,
                ActiveOrDone = (state.activatedpolicies != null && state.activatedpolicies.Contains(policy))
                    || (state.activatedacts != null && state.activatedacts.Contains(policy))
                    || policy.researchprogress >= 1f,
                ReferedChapter = policy.ReferedChapter,
                CurrentChapter = chapter,
                HasPrePolicies = policy.HasPrePolicies(),
                Deactivated = IsDeactivated(policy),
                Blocked = policy.blocked,
                AvailableInScenario = policy.IsAvailableInScenario(),
                SlotFree = state.GetActivatedPolicies() < state.maxpolicies,
                ResearchDuration = policy.ResearchDuration
            };
        }

        // Edges prerequisite → policy for unresearched same-side policies whose prerequisite
        // chain needs no grand act the alliance lacks (Bread Basket III ← 75, Industrialization
        // III ← 76 / 176, Diplomacy IV ← 74, King Cotton III ← 175): those branches are dead
        // for look-ahead.
        private static void AddDependentEdges(Dictionary<int, List<int>> edges, Policy policy, int alliance)
        {
            if (policy.researchprogress >= 1f || policy.ResearchDuration <= 0f || policy.NeededPrePolicies == null) return;
            if (!ChainReachable(policy, 0)) return;
            foreach (var pre in policy.NeededPrePolicies)
            {
                if (pre == null || PolicyEffectCatalog.AllianceOf(pre.UniquePolicyID) != alliance) continue;
                if (!edges.TryGetValue(pre.UniquePolicyID, out var list))
                    edges[pre.UniquePolicyID] = list = new List<int>();
                if (!list.Contains(policy.UniquePolicyID)) list.Add(policy.UniquePolicyID);
            }
        }

        private static bool ChainReachable(Policy policy, int depth)
        {
            if (policy == null || depth > 8) return false;
            if (policy.NeededPrePolicies == null) return true;
            foreach (var pre in policy.NeededPrePolicies)
            {
                if (pre == null) return false;
                bool grand = pre.ResearchDuration <= 0f || (Policy.grandpolicies != null && Policy.grandpolicies.Contains(pre));
                if (grand && pre.researchprogress < 1f) return false;
                if (!grand && !ChainReachable(pre, depth + 1)) return false;
            }
            return true;
        }

        private static bool IsDeactivated(Policy policy)
        {
            if (IsDeactivatedMethod == null)
            {
                OnceLog.Warning("policy-runtime:isdeactivated-method", "[PolicyRuntime] Policies.IsDeactivated missing; candidates treated as deactivated");
                return true;
            }
            try
            {
                return (bool)IsDeactivatedMethod.Invoke(null, new object[] { policy });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-runtime:isdeactivated", "[PolicyRuntime] Policies.IsDeactivated failed: " + ex.Message);
                return true;
            }
        }

        private static GameVars.Alliance AllianceState(int alliance)
        {
            var alliances = GameVars.alliance;
            if (alliances == null || alliance < 0 || alliance >= alliances.Length) return null;
            return alliances[alliance];
        }
    }
}
