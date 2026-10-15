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

        internal Phase Clone()
        {
            var copy = (Phase)MemberwiseClone();
            copy.Fallback = Fallback?.Clone();
            return copy;
        }
    }

    public class OperationalPlan
    {
        public int    CICFactionAllianceId;
        public int    AssignedTheaterId;
        public string OperationId;
        public string OperationName;
        // Generic (non-historical) plans keep the default tempo; saved by name.
        public OperationTempoPreset OperationTempo = OperationTempoPreset.Deliberate;
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

        internal OperationalPlan Clone()
        {
            var copy = (OperationalPlan)MemberwiseClone();
            copy.Phases = new List<Phase>(Phases != null ? Phases.Count : 0);
            if (Phases != null)
                for (int i = 0; i < Phases.Count; i++)
                    copy.Phases.Add(Phases[i]?.Clone());
            return copy;
        }
    }
}
