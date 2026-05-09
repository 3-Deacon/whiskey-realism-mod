namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum ReplanTrigger
    {
        None = 0,
        PhaseDeadline,
        MainEffortSectorLoss,
        EnemyIntentShift,
        ForceImbalanceShift,
        CasualtyThreshold,
        ReserveExhaustion,
        ReinforcementArrival,
    }

    public readonly struct ReplanTriggerInput
    {
        public ReplanTriggerInput(
            float planAgeSeconds,
            BattlePhase currentPhase,
            float mainEffortOwnStrength,
            float mainEffortHistoryOwnStrength,
            float globalOddsCurrent,
            float globalOddsHistory,
            float armyMoraleCurrent,
            float armyMoraleFloor,
            float reservesCommittedFraction,
            float reinforcementsArrivingDelta,
            float enemyMainEffortShiftConfidenceWeighted)
        {
            PlanAgeSeconds = planAgeSeconds;
            CurrentPhase = currentPhase;
            MainEffortOwnStrength = mainEffortOwnStrength;
            MainEffortHistoryOwnStrength = mainEffortHistoryOwnStrength;
            GlobalOddsCurrent = globalOddsCurrent;
            GlobalOddsHistory = globalOddsHistory;
            ArmyMoraleCurrent = armyMoraleCurrent;
            ArmyMoraleFloor = armyMoraleFloor;
            ReservesCommittedFraction = reservesCommittedFraction;
            ReinforcementsArrivingDelta = reinforcementsArrivingDelta;
            EnemyMainEffortShiftConfidenceWeighted = enemyMainEffortShiftConfidenceWeighted;
        }

        public float PlanAgeSeconds { get; }
        public BattlePhase CurrentPhase { get; }
        public float MainEffortOwnStrength { get; }
        public float MainEffortHistoryOwnStrength { get; }
        public float GlobalOddsCurrent { get; }
        public float GlobalOddsHistory { get; }
        public float ArmyMoraleCurrent { get; }
        public float ArmyMoraleFloor { get; }
        public float ReservesCommittedFraction { get; }
        public float ReinforcementsArrivingDelta { get; }
        public float EnemyMainEffortShiftConfidenceWeighted { get; }
    }

    public static class ArmyReplanTriggers
    {
        // Per umbrella §"Replan triggers". Thresholds are seed values; future
        // tuning may move them into config flags.
        public const float PhaseBudgetSeconds = 180f;
        public const float MainEffortLossFraction = 0.5f;
        public const float OddsLowHysteresis = 0.7f;
        public const float OddsHighHysteresis = 1.4f;
        public const float ReservesAlmostSpent = 0.85f;
        public const float EnemyShiftConfidenceFloor = 0.5f;

        /// <summary>
        /// Evaluates the 7 replan triggers in priority order — phase deadline
        /// first (hard battlefield clock), intent shift last (soft inference).
        /// Returns ReplanTrigger.None if nothing fires.
        /// </summary>
        public static ReplanTrigger Evaluate(ReplanTriggerInput i)
        {
            if (i.PlanAgeSeconds >= PhaseBudgetSeconds) return ReplanTrigger.PhaseDeadline;

            if (i.MainEffortHistoryOwnStrength > 0f &&
                i.MainEffortOwnStrength / i.MainEffortHistoryOwnStrength <= MainEffortLossFraction)
                return ReplanTrigger.MainEffortSectorLoss;

            if (i.GlobalOddsCurrent <= OddsLowHysteresis && i.GlobalOddsHistory > OddsLowHysteresis)
                return ReplanTrigger.ForceImbalanceShift;
            if (i.GlobalOddsCurrent >= OddsHighHysteresis && i.GlobalOddsHistory < OddsHighHysteresis)
                return ReplanTrigger.ForceImbalanceShift;

            if (i.ArmyMoraleCurrent < i.ArmyMoraleFloor) return ReplanTrigger.CasualtyThreshold;
            if (i.ReservesCommittedFraction >= ReservesAlmostSpent) return ReplanTrigger.ReserveExhaustion;
            if (i.ReinforcementsArrivingDelta > 1f) return ReplanTrigger.ReinforcementArrival;
            if (i.EnemyMainEffortShiftConfidenceWeighted >= EnemyShiftConfidenceFloor) return ReplanTrigger.EnemyIntentShift;

            return ReplanTrigger.None;
        }
    }
}
