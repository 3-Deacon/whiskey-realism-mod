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
        Recover,
        Fallback,
        Replan
    }

    public sealed class PhaseTruthInput
    {
        public OperationalPlan Plan;
        public bool TargetAccomplished;
        public bool ObjectiveAvailable;
        public bool TargetPositionResolves = true;
        public bool TargetEngagedRecently;
        public float TargetSectorOwnStrength;
        public float RequiredForce;
        public int CurrentMonth;
        public int CurrentYear;
    }

    public sealed class PhaseTruthOutput
    {
        public PhaseTruthVerdict Verdict;
        public PhaseTruthAction RecommendedAction;
        public string Reason;
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

            if (input.TargetAccomplished)
            {
                output.Verdict = PhaseTruthVerdict.TargetAccomplished;
                output.RecommendedAction = PhaseTruthAction.Advance;
                output.Reason = "target-accomplished";
                return output;
            }

            if (!input.ObjectiveAvailable || !input.TargetPositionResolves)
            {
                output.Verdict = input.ObjectiveAvailable
                    ? PhaseTruthVerdict.MissingTargetPosition
                    : PhaseTruthVerdict.ObjectiveUnavailable;
                output.RecommendedAction = PhaseTruthAction.Replan;
                output.Reason = output.Verdict.ToString();
                return output;
            }

            if (input.RequiredForce > 0f && input.TargetSectorOwnStrength < input.RequiredForce)
            {
                output.Verdict = PhaseTruthVerdict.ForceBelowThreshold;
                output.RecommendedAction = PhaseTruthAction.Recover;
                output.Reason = "force-below-threshold";
                return output;
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
                return output;
            }

            if (input.TargetEngagedRecently)
            {
                output.Verdict = PhaseTruthVerdict.TargetEngaged;
                output.RecommendedAction = PhaseTruthAction.Continue;
                output.Reason = "target-engaged-let-contact-decide";
                return output;
            }

            output.Verdict = PhaseTruthVerdict.Valid;
            output.RecommendedAction = PhaseTruthAction.Continue;
            output.Reason = "phase-valid";
            return output;
        }
    }
}
