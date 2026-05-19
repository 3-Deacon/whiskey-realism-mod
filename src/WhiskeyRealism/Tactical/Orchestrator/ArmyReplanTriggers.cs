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
        // Offensive triggers (added 2026-05-19). Defensive triggers above
        // fire when something goes wrong; these fire when opportunity emerges
        // — historical battles iterated probe -> commit -> exploit, not a
        // single fixed plan.
        BreakthroughOpportunity,        // A non-main-effort sector's odds suddenly
                                        // exceed the current main effort's by a
                                        // material margin -> shift attack axis.
        MainEffortLocalBreakthrough,    // The current main-effort sector's local
                                        // odds spike sharply -> escalate to Exploit
                                        // via replan (picks a more aggressive
                                        // playbook for the new sector posture).
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
            float enemyMainEffortShiftConfidenceWeighted,
            float mainEffortLocalOdds = 1f,
            float mainEffortLocalOddsHistory = 1f,
            float bestNonMainEffortSectorOdds = 0f,
            float currentMainEffortOdds = 0f)
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
            MainEffortLocalOdds = mainEffortLocalOdds;
            MainEffortLocalOddsHistory = mainEffortLocalOddsHistory;
            BestNonMainEffortSectorOdds = bestNonMainEffortSectorOdds;
            CurrentMainEffortOdds = currentMainEffortOdds;
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

        // Offensive-trigger inputs (added 2026-05-19 for probe-then-exploit
        // doctrine). Default to neutral values (1.0 odds) so existing call
        // sites compile without modification — the offensive triggers simply
        // won't fire if the runtime hasn't populated these yet.
        public float MainEffortLocalOdds { get; }          // current sector odds
        public float MainEffortLocalOddsHistory { get; }   // sector odds at last replan / phase
        public float BestNonMainEffortSectorOdds { get; }  // strongest off-axis sector odds
        public float CurrentMainEffortOdds { get; }        // for the off-axis comparison
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

        // Mirrors ArmyEvidenceBuilder.UnknownReserveCommitFraction. When the
        // evidence builder cannot determine a real reserve fraction (no chains,
        // no reservegroups, alliance lookup failed) it returns this sentinel.
        // The replan trigger must treat the sentinel as "unknown" rather than
        // letting it drive ReserveExhaustion.
        public const float UnknownReserveCommitSentinel = 0.35f;
        private const float SentinelTolerance = 0.0001f;

        // Offensive trigger thresholds. LocalBreakthroughOddsRatio = how much
        // the main-effort sector's odds must rise (current vs history) to
        // count as a breakthrough — mirrors
        // TacticalPhaseProgressionDoctrine.ExploitTriggerOddsRatio so the two
        // doctrines fire on the same evidence. BreakthroughOpportunityMargin =
        // how much higher a non-main-effort sector's odds must be vs the
        // current main-effort odds to justify shifting axis (defensive bias —
        // don't thrash sectors on a marginal advantage).
        public const float LocalBreakthroughOddsRatio = 1.35f;
        public const float LocalBreakthroughAbsoluteOdds = 1.8f;
        public const float BreakthroughOpportunityMargin = 0.5f;
        public const float OffensiveTriggerMinReservesCommitted = 0.3f;

        /// <summary>
        /// Evaluates the 9 replan triggers in priority order. Defensive
        /// triggers (phase deadline, sector loss, force imbalance, morale,
        /// reserve exhaustion) fire first because they signal the existing
        /// plan is failing. Offensive triggers (local breakthrough, sector
        /// shift opportunity) fire after defensive so we don't get pulled
        /// into a "breakthrough" while morale collapses elsewhere. Returns
        /// <see cref="ReplanTrigger.None"/> if nothing fires.
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
            if (IsReserveCommitFractionKnown(i.ReservesCommittedFraction) &&
                i.ReservesCommittedFraction >= ReservesAlmostSpent)
                return ReplanTrigger.ReserveExhaustion;
            if (i.ReinforcementsArrivingDelta > 1f) return ReplanTrigger.ReinforcementArrival;
            if (i.EnemyMainEffortShiftConfidenceWeighted >= EnemyShiftConfidenceFloor) return ReplanTrigger.EnemyIntentShift;

            // ---- Offensive triggers (probe-then-exploit doctrine) ----
            //
            // Only fire in offensive phases (Probe / MainEffort). Once we're
            // consolidating or withdrawing, opportunity-driven replans would
            // be premature — let the defensive plan run its course.
            if (i.CurrentPhase == BattlePhase.Probe || i.CurrentPhase == BattlePhase.MainEffort)
            {
                // MainEffortLocalBreakthrough: the current main effort sector's
                // local odds have spiked vs history (the probe broke through)
                // AND reserves are at least partially committed (we have force
                // to exploit). Fires a replan so the catalog can swap to an
                // exploitation-friendly playbook.
                bool localSpike =
                    (i.MainEffortLocalOddsHistory > 0.01f &&
                     i.MainEffortLocalOdds / i.MainEffortLocalOddsHistory >= LocalBreakthroughOddsRatio) ||
                    i.MainEffortLocalOdds >= LocalBreakthroughAbsoluteOdds;
                if (localSpike &&
                    IsReserveCommitFractionKnown(i.ReservesCommittedFraction) &&
                    i.ReservesCommittedFraction >= OffensiveTriggerMinReservesCommitted)
                    return ReplanTrigger.MainEffortLocalBreakthrough;

                // BreakthroughOpportunity: a non-main-effort sector's odds now
                // exceed the main-effort sector's by a material margin. Fires
                // a replan so the catalog re-picks a playbook (which may
                // re-target the better sector). The MainEffortSector shift
                // itself is handled separately by
                // ArmyOrchestrator.ConsiderMainEffortShift; this trigger
                // ensures the replan loop also re-scores playbooks for the new
                // axis posture.
                if (i.BestNonMainEffortSectorOdds > 0f &&
                    i.CurrentMainEffortOdds > 0f &&
                    i.BestNonMainEffortSectorOdds - i.CurrentMainEffortOdds >= BreakthroughOpportunityMargin)
                    return ReplanTrigger.BreakthroughOpportunity;
            }

            return ReplanTrigger.None;
        }

        private static bool IsReserveCommitFractionKnown(float fraction)
        {
            float diff = fraction - UnknownReserveCommitSentinel;
            if (diff < 0f) diff = -diff;
            return diff > SentinelTolerance;
        }
    }
}
