namespace WhiskeyRealism.Strategic
{
    internal enum CicRouteOutcome
    {
        // Plan stays active for this review.
        KeepPlan,
        // Plan is dirty; caller rebuilds it via CIC.Replan.
        Replan,
        // Operation aborted; caller drops the plan before replanning.
        AbandonPlan
    }

    internal struct CampaignObjectiveSteer
    {
        public bool RunVanilla;
        public int WriteFollowed;
    }

    // R5(d): what PickCampaignObjectivePatch does with AIFaction.followedcampaignobjective.
    // With a plan target the mod writes it and skips vanilla's random pick. Without one,
    // vanilla AICampaign.PickCampaignObjective (decompile 17769) runs; it only re-picks when
    // followedcampaignobjective < 0 (17780), so a stale id the MOD wrote is cleared first.
    // An id vanilla picked itself is left alone.
    internal static class CampaignObjectiveSteering
    {
        internal const int NoWrite = int.MinValue;

        // Steering follows the LIVE phase posture (after CicReviewRouter overrides such as
        // Recover / ScreenAndDelay / Pause), for catalog and generic plans alike. In vanilla
        // followedcampaignobjective is an offensive importance weight on the objective's
        // towns/IIPs (UpdateImportanceValues 14922/14961), so a phase in a defensive posture
        // releases it and vanilla's own pick (17780-17785) is the defensive-phase behaviour.
        internal static bool PhaseSteers(Phase livePhase)
        {
            return livePhase != null &&
                livePhase.TargetObjectiveId >= 0 &&
                !HistoricalOperationCatalog.IsDefensivePosture(livePhase.OperationPosture);
        }

        // Sidecars written before modWrittenObjective existed: if the faction still follows the
        // loaded plan's current phase target, that value was written by the mod.
        internal static int InferModWritten(int? persisted, int currentFollowed, int planTargetObjectiveId)
        {
            if (persisted.HasValue) return persisted.Value;
            return planTargetObjectiveId >= 0 && currentFollowed == planTargetObjectiveId ? planTargetObjectiveId : -1;
        }

        internal static CampaignObjectiveSteer Decide(bool hasPlanTarget, int planTargetObjectiveId, int currentFollowed, int lastWrittenByMod)
        {
            if (hasPlanTarget && planTargetObjectiveId >= 0)
                return new CampaignObjectiveSteer { RunVanilla = false, WriteFollowed = planTargetObjectiveId };

            bool modOwned = lastWrittenByMod >= 0 && currentFollowed == lastWrittenByMod;
            return new CampaignObjectiveSteer { RunVanilla = true, WriteFollowed = modOwned ? -1 : NoWrite };
        }
    }

    // Single implementation of "what does a PhaseTruthOutput do to the active plan".
    // CIC.ReviewPlanWithTruth delegates here (it only adds the abandon → null and the
    // phase-advanced log line), so the console harness exercises the exact runtime path.
    // No BepInEx / Unity / reflection dependencies.
    internal static class CicReviewRouter
    {
        internal static CicRouteOutcome Route(
            OperationalPlan plan,
            PhaseTruthOutput truth,
            int daySerial,
            int currentMonth,
            int currentYear)
        {
            if (plan == null) return CicRouteOutcome.Replan;
            if (truth == null) return ToOutcome(ReviewPlanBaseline(plan, daySerial, currentMonth, currentYear));

            switch (truth.RecommendedAction)
            {
                case PhaseTruthAction.Advance:
                    return ToOutcome(AdvancePhase(plan, daySerial));

                case PhaseTruthAction.Complete:
                    plan.IsDirty = true;
                    return CicRouteOutcome.Replan;

                case PhaseTruthAction.Abort:
                    plan.IsDirty = true;
                    return CicRouteOutcome.AbandonPlan;

                case PhaseTruthAction.Pivot:
                    plan.PendingRetarget = true;
                    plan.PendingRetargetReason = truth.AlternateOperationId;
                    plan.IsDirty = true;
                    return CicRouteOutcome.Replan;

                case PhaseTruthAction.Replan:
                    plan.PendingRetarget = false;
                    plan.PendingRetargetReason = null;
                    plan.IsDirty = true;
                    return CicRouteOutcome.Replan;

                case PhaseTruthAction.Pause:
                case PhaseTruthAction.ScreenAndDelay:
                    MarkOperationDecision(plan, truth, daySerial, OperationPosture.ScreenAndDelay, allowAttack: false, allowReinforce: true, probeOnly: true);
                    return ToOutcome(ReviewPlanBaseline(plan, daySerial, currentMonth, currentYear));
                case PhaseTruthAction.Exploit:
                    MarkOperationDecision(plan, truth, daySerial, OperationPosture.ExploitBreakthrough, allowAttack: true, allowReinforce: true, probeOnly: false);
                    return ToOutcome(ReviewPlanBaseline(plan, daySerial, currentMonth, currentYear));
                case PhaseTruthAction.Counterstroke:
                    MarkOperationDecision(plan, truth, daySerial, OperationPosture.Counterstroke, allowAttack: true, allowReinforce: true, probeOnly: false);
                    return ToOutcome(ReviewPlanBaseline(plan, daySerial, currentMonth, currentYear));
                case PhaseTruthAction.Recover:
                    MarkOperationDecision(plan, truth, daySerial, OperationPosture.Recover, allowAttack: false, allowReinforce: true, probeOnly: true);
                    return ToOutcome(ReviewPlanBaseline(plan, daySerial, currentMonth, currentYear));

                case PhaseTruthAction.Continue:
                default:
                    // Posture overrides (Recover, ScreenAndDelay, ...) are level conditions:
                    // once the truth is back to Continue the phase returns to its template.
                    RestorePhaseTemplate(plan);
                    return ToOutcome(ReviewPlanBaseline(plan, daySerial, currentMonth, currentYear));
            }
        }

        // Re-derives the current phase's posture/permission flags from its source template:
        // the HistoricalOperationCatalog profile phase (matched by PhaseId) for historical
        // operations, or GenericPhaseFlags for legacy generic plans (no OperationId).
        internal static void RestorePhaseTemplate(OperationalPlan plan)
        {
            var phase = plan?.CurrentPhase;
            if (phase == null) return;

            if (string.IsNullOrEmpty(plan.OperationId))
            {
                GenericPhaseFlags(phase.Transition, out bool attack, out bool reinforce, out bool probeOnly);
                plan.OperationPosture = OperationPosture.ProbeAndDevelop;
                phase.OperationPosture = OperationPosture.ProbeAndDevelop;
                phase.AllowCoordinatedAttack = attack;
                phase.AllowReinforcementPackage = reinforce;
                phase.AllowProbeOnly = probeOnly;
                return;
            }

            if (!HistoricalOperationCatalog.TryGetById(plan.OperationId, out var profile) || profile.Phases == null)
                return;
            for (int i = 0; i < profile.Phases.Length; i++)
            {
                var template = profile.Phases[i];
                if (template == null || !string.Equals(template.PhaseId, phase.PhaseId, System.StringComparison.Ordinal))
                    continue;
                plan.OperationPosture = profile.Posture;
                phase.OperationPosture = template.Posture == OperationPosture.Inherit ? profile.Posture : template.Posture;
                phase.AllowCoordinatedAttack = template.AllowCoordinatedAttack;
                phase.AllowReinforcementPackage = template.AllowReinforcementPackage;
                phase.AllowProbeOnly = template.AllowProbeOnly;
                return;
            }
        }

        // Permission flags of a legacy generic phase (CIC.BuildLegacyGenericPlan): a
        // TargetEngaged lead-in phase is probe-only; every other phase may attack.
        internal static void GenericPhaseFlags(PhaseTransition transition, out bool allowAttack, out bool allowReinforce, out bool probeOnly)
        {
            bool leadIn = transition == PhaseTransition.TargetEngaged;
            allowAttack = !leadIn;
            allowReinforce = true;
            probeOnly = leadIn;
        }

        // Plan/phase deadline checks. Every keep-plan path runs through here so a posture
        // override (Recover, ScreenAndDelay, ...) can never freeze the deadlines.
        internal static bool ReviewPlanBaseline(OperationalPlan plan, int daySerial, int currentMonth, int currentYear)
        {
            if (plan == null || plan.IsDirty) return false;

            if (currentYear > plan.PlanDeadlineYear ||
               (currentYear == plan.PlanDeadlineYear && currentMonth > plan.PlanDeadlineMonth))
                return false;

            var p = plan.CurrentPhase;
            if (p != null && (currentYear > p.DeadlineYear ||
                             (currentYear == p.DeadlineYear && currentMonth > p.DeadlineMonth)))
            {
                return AdvancePhase(plan, daySerial);
            }

            return true;
        }

        // Advances the plan's current phase index and stamps the new phase's start day.
        // If no next phase exists, marks the plan dirty and returns false (signals replan).
        internal static bool AdvancePhase(OperationalPlan plan, int daySerial)
        {
            if (plan == null) return false;
            if (plan.CurrentPhaseIndex + 1 < plan.Phases.Count)
            {
                plan.CurrentPhaseIndex++;
                var phase = plan.CurrentPhase;
                if (phase != null) phase.PhaseStartedDaySerial = daySerial;
                return true;
            }
            plan.IsDirty = true;
            return false;
        }

        private static void MarkOperationDecision(
            OperationalPlan plan,
            PhaseTruthOutput truth,
            int daySerial,
            OperationPosture posture,
            bool allowAttack,
            bool allowReinforce,
            bool probeOnly)
        {
            plan.OperationPosture = posture;
            plan.OperationLastDecisionDaySerial = daySerial;
            plan.PendingRetarget = false;
            plan.PendingRetargetReason = truth?.Reason;
            var phase = plan.CurrentPhase;
            if (phase == null) return;
            phase.OperationPosture = posture;
            phase.AllowCoordinatedAttack = allowAttack;
            phase.AllowReinforcementPackage = allowReinforce;
            phase.AllowProbeOnly = probeOnly;
        }

        private static CicRouteOutcome ToOutcome(bool keep)
        {
            return keep ? CicRouteOutcome.KeepPlan : CicRouteOutcome.Replan;
        }
    }
}
