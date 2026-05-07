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
        public string PhaseId;
        public string PhaseName;
        public int    TargetAreaId;
        public int    TargetObjectiveId;
        public string TargetAreaKey;
        public string TargetSectorKey;
        public float  ForceFractionRequired;
        public PhaseTransition Transition;
        public int    DeadlineMonth;
        public int    DeadlineYear;
        public OperationPosture OperationPosture;
        public bool   AllowCoordinatedAttack;
        public bool   AllowReinforcementPackage;
        public bool   AllowProbeOnly;
        public int    PhaseStartedDaySerial;
        public Phase  Fallback;
    }

    public class OperationalPlan
    {
        public int    CICFactionAllianceId;
        public int    AssignedTheaterId;
        public string OperationId;
        public string OperationName;
        public OperationTempoPreset OperationTempo;
        public OperationPosture OperationPosture;
        public int    OperationStartedDaySerial;
        public int    OperationLastDecisionDaySerial;
        public bool   PendingRetarget;
        public string PendingRetargetReason;
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
