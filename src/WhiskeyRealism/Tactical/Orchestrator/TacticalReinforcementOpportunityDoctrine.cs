using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// The five outcomes of the reinforcement-opportunity decision.
    /// </summary>
    public enum ReinforcementOpportunity
    {
        /// <summary>No specific opportunity — caller falls through to existing playbook selection.</summary>
        NoOpportunity = 0,
        /// <summary>Strike now before enemy concentrates — fire DefeatInDetail playbook.</summary>
        AttackNow,
        /// <summary>Hold and wait for own reinforcements to swing the balance.</summary>
        WaitAndConsolidate,
        /// <summary>Dig in and refuse decisive engagement until the situation clarifies.</summary>
        DefensiveHold,
        /// <summary>Orderly withdrawal — preserve force to fight from a better position later.</summary>
        WithdrawalToFightLater,
    }

    /// <summary>
    /// Decision outcome with diagnostic context. Pure logic — no Unity types.
    /// </summary>
    public readonly struct ReinforcementOpportunityDecision
    {
        public ReinforcementOpportunityDecision(
            ReinforcementOpportunity opportunity,
            string reason,
            float currentRatio,
            float parityHoursForEnemy,
            float parityHoursForOwn,
            float attackThreshold,
            float attackWindowHours)
        {
            Opportunity = opportunity;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
            CurrentRatio = currentRatio;
            ParityHoursForEnemy = parityHoursForEnemy;
            ParityHoursForOwn = parityHoursForOwn;
            AttackThreshold = attackThreshold;
            AttackWindowHours = attackWindowHours;
        }

        public ReinforcementOpportunity Opportunity { get; }
        public string Reason { get; }
        public float CurrentRatio { get; }
        public float ParityHoursForEnemy { get; }
        public float ParityHoursForOwn { get; }
        public float AttackThreshold { get; }
        public float AttackWindowHours { get; }
    }

    /// <summary>
    /// Pure logic that decides whether the army should attack now (defeat-
    /// in-detail before enemy reinforcements arrive), wait for own
    /// reinforcements, dig in defensively, or withdraw to fight later.
    ///
    /// Personality-modulated: aggressive commanders (Hood, Jackson, Grant
    /// with initiative ≥ 0.75) require lower force advantage to attack and
    /// accept shorter windows. Cautious commanders (McClellan, Johnston with
    /// initiative &lt; 0.30) require higher advantage and longer windows
    /// before committing.
    ///
    /// Better than SoW's AttackEnemy (campai.cpp:1030) because:
    /// - Considers reinforcement timing (SoW only looks at currently-present)
    /// - Uses actual strength (SoW counts divisions)
    /// - Personality scales decision thresholds (SoW is one-size-fits-all)
    /// - Five posture outcomes (SoW is binary attack/retreat)
    /// </summary>
    public static class TacticalReinforcementOpportunityDoctrine
    {
        // Personality-tuned attack threshold:
        //   aggression 0.0 (cautious)  → threshold ~1.56  ("need 56% advantage")
        //   aggression 0.5 (mid-band)  → threshold ~1.20  ("need 20% advantage")
        //   aggression 1.0 (aggressive)→ threshold ~1.08  ("need 8% advantage")
        public const float BaseAttackThreshold = 1.20f;
        public const float AggressionThresholdScale = 0.40f;
        public const float AggressionThresholdOffset = 1.30f;

        // Personality-tuned attack-window (minimum hours the enemy must stay
        // weak enough for the attack to complete before parity):
        //   aggression 0.0 → ~1.5h required
        //   aggression 0.5 → ~1.0h
        //   aggression 1.0 → ~0.5h
        public const float BaseAttackWindowHours = 1.0f;
        public const float AggressionWindowScale = 1.0f;
        public const float AggressionWindowOffset = 1.5f;

        // Maximum hours we'll tolerate waiting for own reinforcements before
        // giving up on Wait and falling back to Hold/Withdraw. Aggressive
        // commanders are impatient; cautious ones are patient.
        public const float BaseMaxWaitHoursForOwn = 6.0f;
        public const float AggressionWaitScale = 3.0f;  // cautious wait up to 9h, aggressive only 3h

        // Below this ratio, we're in withdrawal territory — outnumbered with
        // no near-term relief is a recipe for piecemeal destruction.
        public const float WithdrawalRatioThreshold = 0.85f;

        // Below this absolute force, attack is too risky regardless of ratio
        // (a 2-brigade vs 1-brigade matchup is not a defeat-in-detail
        // opportunity — too easy for the enemy to escape or get help).
        public const float MinAttackForceAbsolute = 2000f;

        // Confidence floor — if intel quality is below this, fall back to
        // existing behavior (don't act on bad data).
        public const float MinIntelConfidenceForAction = 0.5f;

        public static ReinforcementOpportunityDecision Decide(TacticalForceBalanceEvidence ev)
        {
            float attackThreshold = ComputeAttackThreshold(ev.CommanderAggression01);
            float attackWindowHours = ComputeAttackWindowHours(ev.CommanderAggression01);
            float maxWaitHours = ComputeMaxWaitHours(ev.CommanderAggression01);

            float currentRatio = ev.CurrentRatio;
            float parityForEnemy = ev.ParityHoursForEnemy();
            float parityForOwn = ev.ParityHoursForOwn();

            // Confidence gate: if intel is poor, never take aggressive action.
            // Better to default to NoOpportunity (fall through to standard
            // playbook) than to commit on bad data.
            if (ev.IntelConfidence01 < MinIntelConfidenceForAction)
            {
                return new ReinforcementOpportunityDecision(
                    ReinforcementOpportunity.NoOpportunity,
                    "intel-confidence-too-low",
                    currentRatio, parityForEnemy, parityForOwn,
                    attackThreshold, attackWindowHours);
            }

            // ATTACK NOW: current advantage clears threshold AND we have enough
            // force absolutely AND a reasonable window exists to complete the
            // attack before enemy parity. parityForEnemy < 999 (never) gates
            // this to scenarios where the enemy IS growing — if enemy will
            // never catch up the urgency is gone and we should just attack
            // through whichever standard playbook fits.
            if (currentRatio >= attackThreshold &&
                ev.OwnDeployedStrength >= MinAttackForceAbsolute &&
                parityForEnemy <= 24f &&            // enemy parity coming within next day
                parityForEnemy >= attackWindowHours) // but not so soon we can't finish
            {
                return new ReinforcementOpportunityDecision(
                    ReinforcementOpportunity.AttackNow,
                    "defeat-in-detail-window-open",
                    currentRatio, parityForEnemy, parityForOwn,
                    attackThreshold, attackWindowHours);
            }

            // WAIT AND CONSOLIDATE: we're not strong enough now but our own
            // reinforcements will swing the balance within maxWaitHours.
            // parityForOwn > 0 excludes the "already ahead" case (where
            // ParityHoursForOwn returns 0 as a short-circuit sentinel) —
            // already-ahead falls through to DefensiveHold instead.
            // parityForOwn < 999 excludes "never" — no point waiting if own
            // never catches up.
            if (currentRatio < attackThreshold &&
                parityForOwn > 0f && parityForOwn < 999f &&
                parityForOwn <= maxWaitHours)
            {
                return new ReinforcementOpportunityDecision(
                    ReinforcementOpportunity.WaitAndConsolidate,
                    "own-reinforcement-arriving-soon",
                    currentRatio, parityForEnemy, parityForOwn,
                    attackThreshold, attackWindowHours);
            }

            // WITHDRAW TO FIGHT LATER: we're outnumbered NOW (ratio below
            // WithdrawalRatioThreshold = 0.85) AND no own reinforcement is
            // arriving in time. Preserve the force, give up the position,
            // fight from a better one. Mirrors SoW's AttackEnemy → retreat
            // path but adds the reinforcement-timing dimension SoW lacks.
            if (currentRatio < WithdrawalRatioThreshold &&
                parityForOwn > maxWaitHours)
            {
                return new ReinforcementOpportunityDecision(
                    ReinforcementOpportunity.WithdrawalToFightLater,
                    "outnumbered-no-near-relief",
                    currentRatio, parityForEnemy, parityForOwn,
                    attackThreshold, attackWindowHours);
            }

            // DEFENSIVE HOLD: fall-through default for any case that's NOT
            // an attack opportunity, NOT a clear wait-for-relief, and NOT
            // outnumbered enough to need withdrawal. Covers the user's
            // 15k-vs-13k+7k scenario: own ahead now but future worse and
            // no own relief → hold ground, refuse decisive engagement,
            // attrit them as they arrive. The "current advantage, future
            // disadvantage" middle band.
            return new ReinforcementOpportunityDecision(
                ReinforcementOpportunity.DefensiveHold,
                "hold-and-refuse-decisive",
                currentRatio, parityForEnemy, parityForOwn,
                attackThreshold, attackWindowHours);
        }

        public static float ComputeAttackThreshold(float commanderAggression01)
        {
            float agg = Clamp01(commanderAggression01);
            // BaseAttackThreshold × (AggressionThresholdOffset - agg × AggressionThresholdScale)
            return BaseAttackThreshold * (AggressionThresholdOffset - agg * AggressionThresholdScale);
        }

        public static float ComputeAttackWindowHours(float commanderAggression01)
        {
            float agg = Clamp01(commanderAggression01);
            float window = BaseAttackWindowHours * (AggressionWindowOffset - agg * AggressionWindowScale);
            if (window < 0.25f) window = 0.25f;  // never below 15 minutes
            return window;
        }

        public static float ComputeMaxWaitHours(float commanderAggression01)
        {
            float agg = Clamp01(commanderAggression01);
            // cautious 0.0 → BaseMaxWaitHoursForOwn + AggressionWaitScale = 9h
            // aggressive 1.0 → BaseMaxWaitHoursForOwn - AggressionWaitScale*0 = 6h
            // Actually we want aggressive shorter and cautious longer:
            // base + (1-agg) * scale → 0.0→9, 0.5→7.5, 1.0→6
            return BaseMaxWaitHoursForOwn + (1f - agg) * AggressionWaitScale;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
