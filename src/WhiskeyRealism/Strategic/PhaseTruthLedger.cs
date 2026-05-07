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
        public float TargetSectorOwnStrength;
        public float TargetSectorEnemyStrength;
        public float RequiredForce;
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

    public static class PhaseTruthLedger
    {
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

            if (input.RequiredForce > 0f && input.TargetSectorOwnStrength < input.RequiredForce)
            {
                output.Verdict = PhaseTruthVerdict.ForceBelowThreshold;
                output.RecommendedAction = PhaseTruthAction.Recover;
                output.Reason = "force-below-threshold";
                return ApplyOperationRules(input, output);
            }

            bool deadlinePassed =
                input.CurrentYear > phase.DeadlineYear ||
                (input.CurrentYear == phase.DeadlineYear && input.CurrentMonth > phase.DeadlineMonth);

            if (deadlinePassed)
            {
                output.Verdict = PhaseTruthVerdict.DeadlineExpired;
                bool hasNextPhase = input.Plan.CurrentPhaseIndex + 1 < input.Plan.Phases.Count;
                output.RecommendedAction = hasNextPhase ? PhaseTruthAction.Advance : PhaseTruthAction.Replan;
                output.Reason = "deadline-expired";
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
