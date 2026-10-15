using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Policies;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Policies.CheckAIPolicyChange (decompile 211017-211098), run per alliance from
    // CampaignController.UpdateAllianceData (81736): skips alliance > 1 and the player
    // alliance unless automanagepolicies || ai_vs_ai (a W&L career forces ai_vs_ai, so it
    // runs for the career player's alliance at every rank). On an emergency personality
    // (GetAIPersonality 62716) it fires a newspaper event and removes every in-progress
    // policy not on the emergency list (211065-211074); otherwise, when nothing is in
    // research, it researches the first available policy of the personality list.
    //
    // This patch:
    //  - Emergency, player's alliance: appends the alliance's in-progress policy ids to the
    //    emergency list for the call, so vanilla's removal spares research the player chose
    //    (a W&L prestige purchase is otherwise lost, with no refund and no way for the career
    //    player to cancel it, 211368). Master switch only. Vanilla then waits for that research.
    //  - Emergency, AI alliance: vanilla.
    //  - Otherwise, when Enable Need-Driven Policy Selection is on and nothing is in research,
    //    for an alliance the daily review plans (every AI-run alliance and a W&L career
    //    player's alliance at any rank, CIC included; never a non-career player alliance):
    //    researches the first ranked policy that still passes the live filters
    //    (PolicyEligibility) and skips vanilla. With nothing eligible, vanilla's fallback walk
    //    (211076-211090) runs with the irreversible acts held below their need gate
    //    (PolicyNeedScorer.IsHeld under the review's need vector) taken out of the personality
    //    list, so neither Whiskey nor vanilla starts one. No need vector yet: vanilla.
    //
    // Both list edits snapshot vanilla's personality list first and publish the handle
    // through __state before touching it; the Finalizer puts the exact snapshot back (contents
    // and order), even when vanilla throws. GetAIPersonality (62716) returns the stored
    // personality (GetPersonalityByID), the same object vanilla re-reads at 211025.
    [HarmonyPatch(typeof(Policies), "CheckAIPolicyChange")]
    internal static class PolicySelectionPatch
    {
        // Vanilla's personality list and its contents before this call's edit.
        internal sealed class ListSnapshot
        {
            internal List<int> List;
            internal int[] Original;
        }

        [HarmonyPrefix]
        internal static bool Prefix(int alliance, out ListSnapshot __state)
        {
            __state = null;
            using (TelemetryPerf.Scope("campaign.patch.policy-selection.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.policy-selection.prefix");
                try
                {
                    if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return true;
                    if (alliance < 0 || alliance > 1) return true;
                    var alliances = GameVars.alliance;
                    if (alliances == null || alliance >= alliances.Length || alliances[alliance] == null) return true;
                    int playerAlliance = GameVars.playeralliance;
                    if (alliance == playerAlliance && !GameVars.automanagepolicies && !GameVars.ai_vs_ai) return true;

                    var state = alliances[alliance];
                    var personality = state.GetAIPersonality(alliance);
                    if (personality == null || personality.policies == null) return true;

                    if (personality.id == GamePrefs.emergencyaipolicycredit || personality.id == GamePrefs.emergencyaipolicyrecruiting)
                    {
                        if (alliance == playerAlliance)
                            ProtectPlayerResearch(alliance, state, personality, out __state);
                        return true;
                    }

                    // Vanilla returns at 211061 without walking the list while anything is in research.
                    if (PolicyRuntime.HasResearchInProgress(alliance)) return true;
                    if (TrySelectNeedDriven(alliance)) return false;
                    HoldIrreversibleActs(alliance, personality, out __state);
                    return true;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("policy-selection:prefix", "[Patch:PolicySelection] prefix failed: " + ex.Message);
                    return true;
                }
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, ListSnapshot __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.policy-selection.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.policy-selection.finalizer");
                try
                {
                    if (__state != null) PolicyListOverride.Restore(__state.List, __state.Original);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("policy-selection:restore", "[Patch:PolicySelection] personality list restore failed: " + ex.Message);
                }
                return __exception;
            }
        }

        // The restore handle is published through the out parameter (Harmony's __state)
        // before the vanilla list is touched, so the Finalizer restores it even when anything
        // after the edit throws.
        private static void ProtectPlayerResearch(int alliance, GameVars.Alliance state, GameVars.Alliance.AIPersonality personality, out ListSnapshot snapshot)
        {
            snapshot = null;
            var research = state.activatedpolicies;
            if (research == null || research.Count == 0) return;
            var ids = new int[research.Count];
            var progress = new float[research.Count];
            for (int i = 0; i < research.Count; i++)
            {
                ids[i] = research[i] != null ? research[i].UniquePolicyID : -1;
                progress[i] = research[i] != null ? research[i].researchprogress : 1f;
            }
            var protectedIds = PolicyListOverride.ProtectedIds(ids, progress, personality.policies);
            if (protectedIds.Count == 0) return;

            snapshot = new ListSnapshot { List = personality.policies, Original = personality.policies.ToArray() };
            personality.policies.AddRange(protectedIds);

            try
            {
                OnceLog.Info("policy-selection:emergency-protect",
                    "PolicySelectionPatch kept the player's research through vanilla's emergency cancel alliance=" + alliance + " protected=" + protectedIds.Count);
                string joined = string.Join(",", protectedIds);
                string signature = "source=PolicySelection|alliance=" + alliance + "|protected=" + joined + "|reason=emergency-protect";
                TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "FiscalIntent", TelemetrySeverity.Info, ev => ev
                    .WithAlliance(alliance)
                    .WithDecision("emergency-protect", "player-research", signature)
                    .WithField("source", "PolicySelection")
                    .WithField("personality", personality.id)
                    .WithField("protected", protectedIds.Count)
                    .WithField("protectedIds", joined));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-selection:emergency-telemetry", "[Patch:PolicySelection] emergency-protect telemetry failed: " + ex.Message);
            }
        }

        // Non-emergency fallback, nothing in research and no Whiskey pick: takes the held
        // irreversible acts out of the list vanilla is about to walk. Same publish-first
        // handle as ProtectPlayerResearch.
        private static void HoldIrreversibleActs(int alliance, GameVars.Alliance.AIPersonality personality, out ListSnapshot snapshot)
        {
            snapshot = null;
            var coordinator = StrategicCoordinator.Instance;
            NeedVector need = coordinator != null && coordinator.Needs != null && alliance < coordinator.Needs.Length ? coordinator.Needs[alliance] : null;
            var held = PolicyListOverride.HeldIdsToRemove(Plugin.Instance.EnableNeedDrivenPolicySelection.Value, emergency: false, need, personality.policies);
            if (held.Count == 0) return;

            snapshot = new ListSnapshot { List = personality.policies, Original = personality.policies.ToArray() };
            PolicyListOverride.Remove(personality.policies, held);

            try
            {
                string joined = string.Join(",", held);
                OnceLog.Info("policy-selection:hold-fallback",
                    "PolicySelectionPatch held irreversible acts out of vanilla's fallback walk alliance=" + alliance + " held=" + joined);
                string signature = "source=PolicySelection|alliance=" + alliance + "|held=" + joined + "|reason=hold-fallback";
                TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "FiscalIntent", TelemetrySeverity.Info, ev => ev
                    .WithAlliance(alliance)
                    .WithDecision("hold-irreversible", "vanilla-fallback", signature)
                    .WithField("source", "PolicySelection")
                    .WithField("personality", personality.id)
                    .WithField("held", held.Count)
                    .WithField("heldIds", joined));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("policy-selection:hold-telemetry", "[Patch:PolicySelection] hold-fallback telemetry failed: " + ex.Message);
            }
        }

        // True when Whiskey started research (vanilla is skipped). The caller checked that
        // nothing is in research.
        private static bool TrySelectNeedDriven(int alliance)
        {
            if (!Plugin.Instance.EnableNeedDrivenPolicySelection.Value) return false;
            var coordinator = StrategicCoordinator.Instance;
            if (coordinator == null || coordinator.PolicyPlans == null || alliance >= coordinator.PolicyPlans.Length) return false;

            var plan = coordinator.PolicyPlans[alliance];
            if (plan == null || plan.Ranked.Count == 0) return false;
            int pick = plan.NextEligible(id => PolicyRuntime.IsEligibleNow(alliance, id));
            if (pick < 0) return false;
            if (!PolicyRuntime.TryStartResearch(alliance, pick))
            {
                OnceLog.Warning("policy-selection:start-failed", "[Patch:PolicySelection] AddResearch did not start policy " + pick + "; vanilla picks");
                return false;
            }

            OnceLog.Info("policy-selection:wired", "PolicySelectionPatch need-driven pick alliance=" + alliance + " policy=" + pick);
            EmitSelection(alliance, pick, plan);
            return true;
        }

        private static void EmitSelection(int alliance, int pick, PolicyPlanLedger plan)
        {
            string reason = pick == plan.CurrentPick ? "ledger-pick" : "next-eligible";
            string signature = "source=PolicySelection|alliance=" + alliance + "|pick=" + pick + "|reason=" + reason;
            PolicyScore selected = default(PolicyScore);
            for (int i = 0; i < plan.Ranked.Count; i++)
                if (plan.Ranked[i].PolicyId == pick) { selected = plan.Ranked[i]; break; }
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Decision, "FiscalIntent", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithDecision("select-policy", reason, signature)
                .WithField("source", "PolicySelection")
                .WithField("selectedPolicy", pick)
                .WithField("score", selected.Total)
                .WithField("need", selected.Need)
                .WithField("history", selected.Personality)
                .WithField("profile", selected.Profile)
                .WithField("lookAhead", selected.LookAhead)
                .WithField("cost", selected.SideCost + selected.SlotPressure + selected.Duration)
                .WithField("top", plan.DescribeTop(3)));
        }
    }
}
