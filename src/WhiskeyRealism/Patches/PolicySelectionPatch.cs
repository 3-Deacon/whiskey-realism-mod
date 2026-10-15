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
    //    emergency list for the call and removes them in the Finalizer, so vanilla's removal
    //    spares research the player chose (a W&L prestige purchase is otherwise lost, with
    //    no refund and no way for the career player to cancel it, 211368). Snapshot/restore
    //    of a vanilla list, master switch only. Vanilla then waits for that research.
    //  - Emergency, AI alliance: vanilla.
    //  - Otherwise, when Enable Need-Driven Policy Selection is on, the daily review has a
    //    ranking for the alliance (every AI-run alliance and a W&L player's alliance at any rank,
    //    CIC included; never a non-career player alliance) and nothing is in research: researches
    //    the first ranked policy that still passes the live filters (PolicyEligibility) and skips
    //    vanilla. Irreversible acts below their need gate never rank. Nothing eligible / no
    //    ranking: vanilla.
    [HarmonyPatch(typeof(Policies), "CheckAIPolicyChange")]
    internal static class PolicySelectionPatch
    {
        internal sealed class ProtectedTail
        {
            internal List<int> List;
            internal int OriginalCount;
        }

        [HarmonyPrefix]
        internal static bool Prefix(int alliance, out ProtectedTail __state)
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

                    return !TrySelectNeedDriven(alliance);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("policy-selection:prefix", "[Patch:PolicySelection] prefix failed: " + ex.Message);
                    return true;
                }
            }
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Exception __exception, ProtectedTail __state)
        {
            using (TelemetryPerf.Scope("campaign.patch.policy-selection.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.policy-selection.finalizer");
                try
                {
                    if (__state != null) PolicyEmergencyProtection.Restore(__state.List, __state.OriginalCount);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("policy-selection:restore", "[Patch:PolicySelection] emergency list restore failed: " + ex.Message);
                }
                return __exception;
            }
        }

        // The restore handle is published through the out parameter (Harmony's __state)
        // before the vanilla list is touched, so the Finalizer restores it even when anything
        // after the append throws.
        private static void ProtectPlayerResearch(int alliance, GameVars.Alliance state, GameVars.Alliance.AIPersonality personality, out ProtectedTail tail)
        {
            tail = null;
            var research = state.activatedpolicies;
            if (research == null || research.Count == 0) return;
            var ids = new int[research.Count];
            var progress = new float[research.Count];
            for (int i = 0; i < research.Count; i++)
            {
                ids[i] = research[i] != null ? research[i].UniquePolicyID : -1;
                progress[i] = research[i] != null ? research[i].researchprogress : 1f;
            }
            var protectedIds = PolicyEmergencyProtection.ProtectedIds(ids, progress, personality.policies);
            if (protectedIds.Count == 0) return;

            tail = new ProtectedTail { List = personality.policies, OriginalCount = personality.policies.Count };
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

        // True when Whiskey started research (vanilla is skipped).
        private static bool TrySelectNeedDriven(int alliance)
        {
            if (!Plugin.Instance.EnableNeedDrivenPolicySelection.Value) return false;
            var coordinator = StrategicCoordinator.Instance;
            if (coordinator == null || coordinator.PolicyPlans == null || alliance >= coordinator.PolicyPlans.Length) return false;
            if (PolicyRuntime.HasResearchInProgress(alliance)) return false;

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
