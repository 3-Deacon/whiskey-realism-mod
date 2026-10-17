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
        // Opportunity-change trigger (added 2026-05-21). The army's
        // ReinforcementOpportunityDoctrine has transitioned to a materially-
        // different category (e.g. DefensiveHold → WithdrawalToFightLater).
        // Without this trigger, opportunity decisions were computed every
        // cycle but never drove the orchestrator's plan to replan — brigades
        // kept executing their initial plan's roles even when the army's
        // analysis said "withdraw" or "attack now."
        OpportunityChange,
        // BC14 (appended last): the assumption monitor found a broken assumption of the selected plan's maneuvers
        // (ReplanTriggerInput.AssumptionReason names which). Lowest priority: every concrete trigger above replans first.
        ManeuverAssumptionBroken,
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
            float ownReinforcementsArriving,
            float enemyMainEffortShiftConfidenceWeighted,
            float mainEffortLocalOdds = 1f,
            float mainEffortLocalOddsHistory = 1f,
            float bestNonMainEffortSectorOdds = 0f,
            float currentMainEffortOdds = 0f,
            ReinforcementOpportunity currentOpportunity = ReinforcementOpportunity.NoOpportunity,
            ReinforcementOpportunity lastReplanOpportunity = ReinforcementOpportunity.NoOpportunity,
            float armyMoraleAtLastReplan = 1f,
            float reservesCommittedAtLastReplan = 0f,
            float ownStrength = 0f,
            float reinforcementShareAtLastReplan = 0f,
            InferredIntent? currentEnemyIntent = null,
            InferredIntent enemyIntentAtLastReplan = InferredIntent.Unknown,
            string assumptionReason = null,
            bool flankCrisis = false)
        {
            FlankCrisis = flankCrisis;
            AssumptionReason = assumptionReason;
            PlanAgeSeconds = planAgeSeconds;
            CurrentPhase = currentPhase;
            MainEffortOwnStrength = mainEffortOwnStrength;
            MainEffortHistoryOwnStrength = mainEffortHistoryOwnStrength;
            GlobalOddsCurrent = globalOddsCurrent;
            GlobalOddsHistory = globalOddsHistory;
            ArmyMoraleCurrent = armyMoraleCurrent;
            ArmyMoraleFloor = armyMoraleFloor;
            ReservesCommittedFraction = reservesCommittedFraction;
            OwnReinforcementsArriving = ownReinforcementsArriving;
            EnemyMainEffortShiftConfidenceWeighted = enemyMainEffortShiftConfidenceWeighted;
            MainEffortLocalOdds = mainEffortLocalOdds;
            MainEffortLocalOddsHistory = mainEffortLocalOddsHistory;
            BestNonMainEffortSectorOdds = bestNonMainEffortSectorOdds;
            CurrentMainEffortOdds = currentMainEffortOdds;
            CurrentOpportunity = currentOpportunity;
            LastReplanOpportunity = lastReplanOpportunity;
            ArmyMoraleAtLastReplan = armyMoraleAtLastReplan;
            ReservesCommittedAtLastReplan = reservesCommittedAtLastReplan;
            OwnStrength = ownStrength;
            ReinforcementShareAtLastReplan = reinforcementShareAtLastReplan;
            CurrentEnemyIntent = currentEnemyIntent;
            EnemyIntentAtLastReplan = enemyIntentAtLastReplan;
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
        /// <summary>
        /// Own reinforcement strength due inside vanilla's retreat-decision window
        /// (ArmyEvidenceBuilder.OwnReinforcementsArriving: own scheduled arrivals,
        /// 84764-84767). Own side only: the enemy's schedule is not known; enemy
        /// arrivals count once seen, through the contact ledger.
        /// </summary>
        public float OwnReinforcementsArriving { get; }
        public float EnemyMainEffortShiftConfidenceWeighted { get; }

        // Offensive-trigger inputs (added 2026-05-19 for probe-then-exploit
        // doctrine). Default to neutral values (1.0 odds) so existing call
        // sites compile without modification — the offensive triggers simply
        // won't fire if the runtime hasn't populated these yet.
        public float MainEffortLocalOdds { get; }          // current sector odds
        public float MainEffortLocalOddsHistory { get; }   // sector odds at last replan / phase
        public float BestNonMainEffortSectorOdds { get; }  // strongest off-axis sector odds
        public float CurrentMainEffortOdds { get; }        // for the off-axis comparison

        // Opportunity-change trigger inputs (added 2026-05-21). CurrentOpportunity is
        // the latest decision from TacticalReinforcementOpportunityDoctrine;
        // LastReplanOpportunity is what it was at the last successful replan (or at
        // plan creation). Differing values fire ReplanTrigger.OpportunityChange.
        public ReinforcementOpportunity CurrentOpportunity { get; }
        public ReinforcementOpportunity LastReplanOpportunity { get; }

        // Snapshot at the last replan (T7, 2026-10-14). Triggers fire when a
        // value CROSSES its threshold since the last replan, not every cycle
        // the state persists; the defaults describe a healthy baseline so a
        // caller without a snapshot still sees the first crossing.
        public float ArmyMoraleAtLastReplan { get; }
        public float ReservesCommittedAtLastReplan { get; }
        /// <summary>Own strength; own arriving reinforcements are judged as a share of it.</summary>
        public float OwnStrength { get; }
        public float ReinforcementShareAtLastReplan { get; }
        /// <summary>Inferred enemy intent now; null = not supplied (level behaviour).</summary>
        public InferredIntent? CurrentEnemyIntent { get; }
        public InferredIntent EnemyIntentAtLastReplan { get; }
        /// <summary>BC14: the assumption the maneuver monitor found broken (a static literal), or null.</summary>
        public string AssumptionReason { get; }

        /// <summary>
        /// An own group is outflanked severely (vanilla's mean outflanked weight at or past <see cref="TacticalFrontAxis.FlankThreatSevere"/>: both
        /// flanks, or the rear): the enemy is on the flank or rear, which makes any trigger that fires a crisis (<see cref="ArmyReplanTriggers.IsCrisis"/>).
        /// </summary>
        public bool FlankCrisis { get; }

        /// <summary>Own arriving reinforcements as a share of own strength.</summary>
        public float ReinforcementShare =>
            OwnReinforcementsArriving / (OwnStrength > 1f ? OwnStrength : 1f);
    }

    public static class ArmyReplanTriggers
    {
        // Per umbrella §"Replan triggers". Thresholds are seed values; future
        // tuning may move them into config flags.
        /// <summary>
        /// Plan reassessment deadline in BATTLE seconds since the last replan
        /// (3 battle hours). Was 180 real seconds, which reset plans every three
        /// wall-clock minutes regardless of time compression.
        /// </summary>
        public const float PlanDeadlineBattleSeconds = 3f * 3600f;

        /// <summary>
        /// A side holds the plan it adopted for this long (BATTLE seconds on the doctrine clock: one game hour) before a trigger that is not a crisis
        /// may re-pick it. Smoke 2026-10-17: 20 replans in 4.6 doctrine hours, one per "Min Replan Seconds" (600 s), because the cooldown was the only
        /// brake. A written constant, not a config default: the deployed cfg already holds the cooldown and overrides a bumped default.
        /// </summary>
        public const float PlanCommitmentBattleSeconds = 3600f;
        public const float MainEffortLossFraction = 0.5f;
        public const float OddsLowHysteresis = 0.7f;
        public const float OddsHighHysteresis = 1.4f;
        public const float ReservesAlmostSpent = 0.85f;
        public const float EnemyShiftConfidenceFloor = 0.5f;

        /// <summary>
        /// Net reinforcements (own minus visible enemy, 24 h) worth this share
        /// of own strength change the situation. Vanilla sums head counts into
        /// reinforcementarrivalswithin24hrs (84767), so the old "&gt; 1" fired on
        /// a single man.
        /// </summary>
        public const float ReinforcementShareThreshold = 0.15f;

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
            if (i.PlanAgeSeconds >= PlanDeadlineBattleSeconds) return ReplanTrigger.PhaseDeadline;

            if (i.MainEffortHistoryOwnStrength > 0f &&
                i.MainEffortOwnStrength / i.MainEffortHistoryOwnStrength <= MainEffortLossFraction)
                return ReplanTrigger.MainEffortSectorLoss;

            // The crises come first (IsCrisis): the fall of the odds and the break of morale. A rising-odds shift is no crisis, and a trigger that fires every
            // tick while its condition holds must not shadow a morale collapse behind it inside the plan commitment hold.
            if (i.GlobalOddsCurrent <= OddsLowHysteresis && i.GlobalOddsHistory > OddsLowHysteresis)
                return ReplanTrigger.ForceImbalanceShift;

            if (i.ArmyMoraleCurrent < i.ArmyMoraleFloor && i.ArmyMoraleAtLastReplan >= i.ArmyMoraleFloor)
                return ReplanTrigger.CasualtyThreshold;

            if (i.GlobalOddsCurrent >= OddsHighHysteresis && i.GlobalOddsHistory < OddsHighHysteresis)
                return ReplanTrigger.ForceImbalanceShift;

            if (IsReserveCommitFractionKnown(i.ReservesCommittedFraction) &&
                i.ReservesCommittedFraction >= ReservesAlmostSpent &&
                !(IsReserveCommitFractionKnown(i.ReservesCommittedAtLastReplan) && i.ReservesCommittedAtLastReplan >= ReservesAlmostSpent))
                return ReplanTrigger.ReserveExhaustion;
            if (i.ReinforcementShare >= ReinforcementShareThreshold &&
                i.ReinforcementShareAtLastReplan < ReinforcementShareThreshold)
                return ReplanTrigger.ReinforcementArrival;
            if (i.EnemyMainEffortShiftConfidenceWeighted >= EnemyShiftConfidenceFloor &&
                (!i.CurrentEnemyIntent.HasValue || i.CurrentEnemyIntent.Value != i.EnemyIntentAtLastReplan))
                return ReplanTrigger.EnemyIntentShift;

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

            // Opportunity-change trigger (2026-05-21). Lowest priority — defensive
            // and offensive triggers above handle their concrete state changes
            // first. This catches "the situation has shifted enough that the
            // doctrine returned a different opportunity category" cases that the
            // specific triggers miss. ArmyTickCycle's minReplanSeconds cooldown
            // prevents thrashing when the opportunity oscillates.
            if (i.CurrentOpportunity != i.LastReplanOpportunity)
            {
                return ReplanTrigger.OpportunityChange;
            }

            // BC14: a broken maneuver assumption comes last, so any concrete trigger above pre-empts it (and its replan reads the ledger anyway).
            if (i.AssumptionReason != null) return ReplanTrigger.ManeuverAssumptionBroken;

            return ReplanTrigger.None;
        }

        /// <summary>
        /// Whether <paramref name="trigger"/> is a crisis, which may re-pick the plan inside <see cref="PlanCommitmentBattleSeconds"/>: the army has
        /// lost its main effort (<see cref="ReplanTrigger.MainEffortSectorLoss"/>, which the tick cycle raises on men lost, not men moved), its morale has
        /// broken, its odds have fallen below the low line, a maneuver assumption is broken (that trigger has its own hysteresis) or the plan's
        /// deadline has passed. Everything else (a reinforcement, an intent shift, a rising opportunity or odds, spent reserves) is a change in the
        /// situation a commander digests at the next review, unless an own group is outflanked severely (<see cref="ReplanTriggerInput.FlankCrisis"/>).
        /// </summary>
        public static bool IsCrisis(ReplanTrigger trigger, in ReplanTriggerInput input)
        {
            switch (trigger)
            {
                case ReplanTrigger.None:
                    return false;
                case ReplanTrigger.PhaseDeadline:
                case ReplanTrigger.MainEffortSectorLoss:
                case ReplanTrigger.CasualtyThreshold:
                case ReplanTrigger.ManeuverAssumptionBroken:
                    return true;
                case ReplanTrigger.ForceImbalanceShift:
                    return input.FlankCrisis || input.GlobalOddsCurrent <= OddsLowHysteresis;
                default:
                    return input.FlankCrisis;
            }
        }

        private static bool IsReserveCommitFractionKnown(float fraction)
        {
            float diff = fraction - UnknownReserveCommitSentinel;
            if (diff < 0f) diff = -diff;
            return diff > SentinelTolerance;
        }
    }
}
