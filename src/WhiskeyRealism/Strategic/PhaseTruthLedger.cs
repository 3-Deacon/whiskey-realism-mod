using System;

namespace WhiskeyRealism.Strategic
{
    public enum PhaseTruthVerdict
    {
        Valid,
        TargetAccomplished,
        ObjectiveUnavailable,
        TargetEngaged,
        ForceBelowThreshold,
        DeadlineExpired,
        MissingTargetPosition
    }

    public enum PhaseTruthAction
    {
        Continue,
        Advance,
        Complete,
        Recover,
        Pause,
        Pivot,
        Abort,
        Exploit,
        Counterstroke,
        ScreenAndDelay,
        Replan
    }

    public sealed class PhaseTruthInput
    {
        public OperationalPlan Plan;
        public HistoricalOperationProfile OperationProfile;
        public HistoricalOperationContext OperationContext;
        public int AllianceId;
        public int DaySerial;
        public bool TargetAccomplished;
        public bool ObjectiveAvailable;
        public bool TargetPositionResolves = true;
        public bool TargetEngagedRecently;
        // Day serial of the latest land battle near the target (-1 = none in the window).
        public int LastTargetEngagementDaySerial = -1;
        // Own / enemy campaign strength within the near-target radius around the phase target
        // (vanilla GamePrefs.aimaximumdistancetosearchforunitrelocations), not theater totals.
        public float TargetLocalOwnStrength;
        public float TargetLocalEnemyStrength;
        public int CurrentMonth;
        public int CurrentYear;
    }

    public sealed class PhaseTruthOutput
    {
        public PhaseTruthVerdict Verdict;
        public PhaseTruthAction RecommendedAction;
        public string Reason;
        public string OperationId;
        public string RuleId;
        public string AlternateOperationId;
    }

    // Evaluation order (R3): availability -> accomplished -> DEADLINE -> phase transition
    // -> force -> engaged -> valid. The deadline is checked before the force gate so an
    // under-strength operation still advances/replans when its time runs out instead of
    // sitting in Recover forever.
    //
    // Force requirement: Phase.ForceFractionRequired is read as a commitment share and
    // mapped to a LOCAL own:enemy ratio, required = 0.5 + f (0.35 -> 0.85:1, 0.65 ->
    // 1.15:1, 0.8 -> 1.3:1). The previous f x (own+enemy) over a whole theater was an
    // own:enemy ratio of f/(1-f) (0.8 => 4:1 across the East) that never cleared.
    //
    // Phase.Transition is evaluated: TargetEngaged phases complete on an engagement that
    // happened after the phase started; ForceBelowThreshold (hold) phases are not
    // force-gated and complete once the force requirement is met; TargetTaken phases
    // complete on accomplishment; DeadlineExpired phases only on their deadline.
    public static class PhaseTruthLedger
    {
        public static float RequiredLocalRatio(float forceFractionRequired)
        {
            if (forceFractionRequired <= 0f || float.IsNaN(forceFractionRequired)) return 0f;
            return Math.Max(0.5f, Math.Min(2.0f, 0.5f + forceFractionRequired));
        }

        public static bool IsForceShort(float forceFractionRequired, float localOwn, float localEnemy)
        {
            float required = RequiredLocalRatio(forceFractionRequired);
            if (required <= 0f) return false;
            if (localEnemy <= 0f) return false;
            return Math.Max(0f, localOwn) < required * localEnemy;
        }

        public static PhaseTruthOutput Evaluate(PhaseTruthInput input)
        {
            var output = new PhaseTruthOutput();
            if (input?.Plan?.CurrentPhase == null)
            {
                output.Verdict = PhaseTruthVerdict.MissingTargetPosition;
                output.RecommendedAction = PhaseTruthAction.Replan;
                output.Reason = "no-active-phase";
                return output;
            }

            var phase = input.Plan.CurrentPhase;

            if (!input.ObjectiveAvailable || !input.TargetPositionResolves)
            {
                output.Verdict = input.ObjectiveAvailable
                    ? PhaseTruthVerdict.MissingTargetPosition
                    : PhaseTruthVerdict.ObjectiveUnavailable;
                output.RecommendedAction = PhaseTruthAction.Replan;
                output.Reason = output.Verdict.ToString();
                return ApplyOperationRules(input, output);
            }

            if (input.TargetAccomplished)
            {
                output.Verdict = PhaseTruthVerdict.TargetAccomplished;
                output.RecommendedAction = PhaseTruthAction.Advance;
                output.Reason = "target-accomplished";
                return ApplyOperationRules(input, output);
            }

            bool deadlinePassed =
                input.CurrentYear > phase.DeadlineYear ||
                (input.CurrentYear == phase.DeadlineYear && input.CurrentMonth > phase.DeadlineMonth);

            if (deadlinePassed)
            {
                // Authoritative: dynamic posture rules must not mask an expired deadline.
                output.Verdict = PhaseTruthVerdict.DeadlineExpired;
                bool hasNextPhase = input.Plan.CurrentPhaseIndex + 1 < input.Plan.Phases.Count;
                output.RecommendedAction = hasNextPhase ? PhaseTruthAction.Advance : PhaseTruthAction.Replan;
                output.Reason = "deadline-expired";
                if (input.OperationProfile != null) output.OperationId = input.OperationProfile.OperationId;
                return output;
            }

            bool forceShort = IsForceShort(
                phase.ForceFractionRequired,
                input.TargetLocalOwnStrength,
                input.TargetLocalEnemyStrength);

            if (phase.Transition == PhaseTransition.TargetEngaged &&
                input.LastTargetEngagementDaySerial >= 0 &&
                input.LastTargetEngagementDaySerial > phase.PhaseStartedDaySerial)
            {
                output.Verdict = PhaseTruthVerdict.TargetEngaged;
                output.RecommendedAction = PhaseTruthAction.Advance;
                output.Reason = "transition-target-engaged";
                return ApplyOperationRules(input, output);
            }

            if (phase.Transition == PhaseTransition.ForceBelowThreshold)
            {
                // Hold phase: being short is the phase's job (reinforce and hold), not a
                // reason to Recover. Completes once the force requirement is met.
                bool ready = !forceShort && input.TargetLocalEnemyStrength > 0f;
                output.Verdict = PhaseTruthVerdict.Valid;
                output.RecommendedAction = ready ? PhaseTruthAction.Advance : PhaseTruthAction.Continue;
                output.Reason = ready ? "transition-force-ready" : "hold-building-force";
                return ApplyOperationRules(input, output);
            }

            if (forceShort)
            {
                output.Verdict = PhaseTruthVerdict.ForceBelowThreshold;
                output.RecommendedAction = PhaseTruthAction.Recover;
                output.Reason = "force-below-threshold";
                return ApplyOperationRules(input, output);
            }

            if (input.TargetEngagedRecently)
            {
                output.Verdict = PhaseTruthVerdict.TargetEngaged;
                output.RecommendedAction = PhaseTruthAction.Continue;
                output.Reason = "target-engaged-let-contact-decide";
                return ApplyOperationRules(input, output);
            }

            output.Verdict = PhaseTruthVerdict.Valid;
            output.RecommendedAction = PhaseTruthAction.Continue;
            output.Reason = "phase-valid";
            return ApplyOperationRules(input, output);
        }

        private static PhaseTruthOutput ApplyOperationRules(PhaseTruthInput input, PhaseTruthOutput output)
        {
            if (input?.OperationProfile == null) return output;
            return OperationDynamicRuleEvaluator.Evaluate(
                output,
                input.OperationProfile,
                input.OperationContext,
                input.AllianceId,
                input.DaySerial);
        }
    }
}
