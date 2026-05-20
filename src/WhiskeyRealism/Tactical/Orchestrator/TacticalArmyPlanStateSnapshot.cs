namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Immutable read-only snapshot of <see cref="ArmyOrchestrator"/> plan-state,
    /// captured on the main thread and handed to the worker. Worker reads only;
    /// never mutates. Filled by
    /// <c>TacticalBattleCoordinatorRuntime.CaptureWorkerInputOnMain</c> and
    /// consumed by <c>TacticalOrchestratorWorkerAnalysis.ProcessSide</c> to
    /// populate <see cref="TacticalSideDecision.HasPlan"/> and
    /// <see cref="TacticalSideDecision.CurrentPlan"/>.
    ///
    /// Gap-fill chunk 3 (2026-05-19): added so the worker can publish plan
    /// state into its decisions instead of leaving them at defaults. Pure
    /// plain-old-data struct; no Unity / vanilla / reflection dependencies.
    /// netstandard2.1 — no nullable annotations, no init-only setters.
    /// </summary>
    internal readonly struct TacticalArmyPlanStateSnapshot
    {
        public TacticalArmyPlanStateSnapshot(
            bool hasPlan,
            TacticalBattlePlan currentPlan,
            float planAgeSeconds,
            float phaseAgeSeconds,
            float historyGlobalOdds,
            int mainEffortSector)
        {
            HasPlan = hasPlan;
            CurrentPlan = currentPlan;
            PlanAgeSeconds = planAgeSeconds;
            PhaseAgeSeconds = phaseAgeSeconds;
            HistoryGlobalOdds = historyGlobalOdds;
            MainEffortSector = mainEffortSector;
        }

        public bool HasPlan { get; }
        public TacticalBattlePlan CurrentPlan { get; }
        public float PlanAgeSeconds { get; }
        public float PhaseAgeSeconds { get; }
        public float HistoryGlobalOdds { get; }

        /// <summary>
        /// Plan-fielded main effort sector. -1 sentinel when
        /// <see cref="HasPlan"/> is false. Use this field directly rather than
        /// <c>CurrentPlan.MainEffortSector</c> to keep the sentinel semantics
        /// clean when no plan is active.
        /// </summary>
        public int MainEffortSector { get; }

        public static TacticalArmyPlanStateSnapshot Empty => new TacticalArmyPlanStateSnapshot(
            hasPlan: false,
            currentPlan: default(TacticalBattlePlan),
            planAgeSeconds: 0f,
            phaseAgeSeconds: 0f,
            historyGlobalOdds: 0f,
            mainEffortSector: -1);
    }
}
