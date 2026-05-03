using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public enum PhaseTransition
    {
        TargetTaken,
        TargetEngaged,
        DeadlineExpired,
        ForceBelowThreshold
    }

    public class Phase
    {
        public int    TargetAreaId;
        public int    TargetObjectiveId;
        public float  ForceFractionRequired;
        public PhaseTransition Transition;
        public int    DeadlineMonth;
        public int    DeadlineYear;
        public Phase  Fallback;
    }

    public class OperationalPlan
    {
        public int    CICFactionAllianceId;
        public int    AssignedTheaterId;
        public List<Phase> Phases = new List<Phase>();
        public int    CurrentPhaseIndex;
        public int    PlanDeadlineMonth;
        public int    PlanDeadlineYear;
        public string Rationale;
        public bool   IsDirty;

        public Phase CurrentPhase
            => (CurrentPhaseIndex >= 0 && CurrentPhaseIndex < Phases.Count) ? Phases[CurrentPhaseIndex] : null;
    }
}
