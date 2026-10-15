using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Outcomes of the reinforcement-opportunity decision.
    /// </summary>
    public enum ReinforcementOpportunity
    {
        /// <summary>No specific opportunity — caller falls through to existing playbook selection.</summary>
        NoOpportunity = 0,
        /// <summary>Strike now before enemy concentrates — fire envelopment playbook.</summary>
        AttackNow,
        /// <summary>Hold and wait for own reinforcements to swing the balance.</summary>
        WaitAndConsolidate,
        /// <summary>Dig in and refuse decisive engagement until the situation clarifies.</summary>
        DefensiveHold,
        /// <summary>
        /// Outnumbered with no near relief, but still defensible — fight Fabian:
        /// forward screen, delay lines, refuse decisive engagement, force the
        /// enemy to attack you on your terms while buying time. Historically
        /// the dominant CW pattern when outnumbered (Johnston Atlanta campaign,
        /// Lee in 1864). Distinct from <see cref="DefensiveHold"/> in that we
        /// expect no parity relief and accept a long delaying action; distinct
        /// from <see cref="WithdrawalToFightLater"/> in that we still hold ground.
        /// </summary>
        DelayingDefense,
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
    /// Pure logic that decides whether the army should attack now (clear
    /// advantage over the enemy it has had in range), wait for own
    /// reinforcements, dig in defensively, or withdraw to fight later.
    ///
    /// Personality-modulated: aggressive commanders (Hood, Jackson, Grant
    /// with initiative ≥ 0.75) require lower force advantage to attack and
    /// accept shorter windows. Cautious commanders (McClellan, Johnston with
    /// initiative &lt; 0.30) require higher advantage and longer windows
    /// before committing.
    ///
    /// Better than SoW's AttackEnemy (campai.cpp:1030) because:
    /// - Considers own reinforcement timing (SoW only looks at currently-present)
    /// - Uses actual strength (SoW counts divisions)
    /// - Personality scales decision thresholds (SoW is one-size-fits-all)
    /// - Five posture outcomes (SoW is binary attack/retreat)
    /// </summary>
    public static class TacticalReinforcementOpportunityDoctrine
    {
        // Personality-tuned attack threshold
        //   BaseAttackThreshold * (AggressionThresholdOffset - aggression * AggressionThresholdScale):
        //   aggression 0.0 (cautious)  → 1.56  ("need 56% advantage")
        //   aggression 0.5 (mid-band)  → 1.32  ("need 32% advantage")
        //   aggression 1.0 (aggressive)→ 1.08  ("need 8% advantage")
        // Capped at parity for Meeting/Attacker stances — see
        // EffectiveAttackThreshold.
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
        // giving up on Wait and falling back to DelayingDefense/Withdraw.
        // Tuned to CW timing (2026-05-23): historical commanders waited
        // 1-2 days for reinforcements; the prior 6-9h cap was way too tight
        // and forced premature withdrawal. Cautious commanders are most
        // patient (McClellan would wait a week); aggressive ones short-circuit
        // sooner (Jackson would attack outnumbered rather than wait).
        public const float BaseMaxWaitHoursForOwn = 18.0f;
        public const float AggressionWaitScale = 10.0f;  // cautious wait up to 28h, aggressive 18h

        // Base withdrawal threshold — personality-modulated by
        // ComputeWithdrawalRatioThreshold (2026-05-23). The prior FLAT 0.85
        // forced withdrawal at any 17%+ disadvantage regardless of commander
        // temperament, which contradicts CW history (most outnumbered armies
        // FOUGHT defensively rather than retreated). Aggressive commanders
        // (Jackson, Hood) refuse to withdraw until severely outnumbered (~5:1);
        // cautious commanders (McClellan) withdraw earlier (~2:1).
        public const float BaseWithdrawalRatioThreshold = 0.55f;
        public const float WithdrawalAggressionScale = 0.30f;

        // ---- Casualty escalation (2026-05-23 / J) ----
        //
        // When DelayingDefense is the active outcome, the doctrine re-evaluates
        // each tick. If casualties or routs cross personality-modulated
        // thresholds, the outcome ESCALATES to WithdrawalToFightLater — the
        // user's "should not let it become a decisive victory" rule.
        //
        //   aggressive (1.0)  → casualty 0.15, rout 0.30
        //   mid       (0.5)   → casualty 0.10, rout 0.22
        //   cautious  (0.0)   → casualty 0.05, rout 0.15
        public const float BaseCasualtyEscalationThreshold = 0.05f;
        public const float CasualtyAggressionScale = 0.10f;
        public const float BaseRoutEscalationThreshold = 0.15f;
        public const float RoutAggressionScale = 0.15f;

        // If a side has been in DelayingDefense for > this many hours AND
        // still outnumbered AND parity never comes, escalate to withdrawal.
        // Represents the "we've bought enough time" judgment — at some point
        // the delay window expires regardless of casualties.
        public const float ProlongedDelayHoursThreshold = 6.0f;

        // Intel gate: below this confidence the doctrine has no opinion
        // (NoOpportunity) and vanilla's own pipeline decides. This is the only
        // gate that can block AttackNow regardless of ratio; everything else
        // is dimensionless (ratios, fractions, hours) and scale-invariant.
        //
        // Confidence comes from TacticalEnemyContactLedger: observation
        // coverage (recency of the ground in front of the line, softened by
        // the share of enemy-side entry points watched) times contact
        // recency. It says how well the side has looked, never how big the
        // enemy is, so a small enemy force that is fully observed is a real
        // opportunity at any battle size (1,500 to 60,000+ a side, per
        // 2026-05-19 user direction: no absolute headcount thresholds).
        //
        // 0.35: a fully observed front with no enemy-side entry point watched
        // reads 0.5, and with one of five coverage bands unobserved as well
        // 0.4; both act. A picket seen under low coverage (~0.1) does not.
        // The probe's commit gate (TacticalPhaseProgressionDoctrine) uses the
        // same constant.
        public const float MinIntelConfidenceForAction = 0.35f;

        public static ReinforcementOpportunityDecision Decide(TacticalForceBalanceEvidence ev)
        {
            float attackThreshold = EffectiveAttackThreshold(ev.CommanderAggression01, ev.Stance);
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

            // ATTACK NOW: the ratio clears the threshold AND either
            //   (a) the known enemy is below us and will not catch up
            //       (parityForEnemy >= 999 sentinel) — SoW's AttackEnemy
            //       attacks on ratio alone here; no reason to sit on an
            //       advantage and wait for it to erode — or
            //   (b) Meeting/Attacker at parity: vanilla attacks (6400-6408)
            //       as long as the known enemy does not outnumber what we
            //       will have when the attack lands.
            // There is no defeat-in-detail window: the side cannot see the
            // enemy's arrival schedule; enemy reinforcements count once they
            // are in range (contact ledger).
            // Scale-invariant: only the ratio matters. Whether you have a 500-
            // man cavalry detachment or a 60,000-man field army, the question
            // is "do I have a clear advantage right now."
            // Stance gate (2026-05-23 / L): defenders cannot output AttackNow
            // regardless of force balance. Vanilla's GetBattleType makes this
            // decision once at battle start from objective-points-held. The
            // doctrine respects that — Scourge of War offai.cpp:1153-1159 does
            // the same. If we're the defender, the worst-case favorable force
            // balance still produces DefensiveHold (we hold what we have).
            bool stanceAllowsAttack = ev.Stance != BattleStance.Defender;

            bool ratioAndForceOk = currentRatio >= attackThreshold;
            bool enemyNotGrowing = parityForEnemy >= 999f;
            bool parityHoldsThroughWindow = ev.Stance != BattleStance.Defender &&
                ev.EnemyDeployedStrength <= ev.OwnStrengthAtHour(attackWindowHours);
            if (stanceAllowsAttack && ratioAndForceOk && (enemyNotGrowing || parityHoldsThroughWindow))
            {
                string reason = enemyNotGrowing ? "advantage-and-enemy-static" : "parity-holds-through-attack-window";
                return new ReinforcementOpportunityDecision(
                    ReinforcementOpportunity.AttackNow,
                    reason,
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

            // WITHDRAW TO FIGHT LATER: outnumbered SEVERELY (ratio below
            // personality-modulated WithdrawalRatioThreshold) AND no own
            // reinforcement arriving in time. Preserve the force, give up
            // the position, fight from a better one.
            //
            // The personality scaling matters: Jackson at 0.44 ratio would
            // delay-and-skirmish (DelayingDefense below), not withdraw.
            // McClellan at the same ratio WOULD withdraw — same evidence,
            // different doctrine output. This matches CW history.
            float withdrawalThreshold = ComputeWithdrawalRatioThreshold(ev.CommanderAggression01);
            if (currentRatio < withdrawalThreshold &&
                parityForOwn > maxWaitHours)
            {
                return new ReinforcementOpportunityDecision(
                    ReinforcementOpportunity.WithdrawalToFightLater,
                    "outnumbered-no-near-relief",
                    currentRatio, parityForEnemy, parityForOwn,
                    attackThreshold, attackWindowHours);
            }

            // DELAYING DEFENSE: outnumbered but NOT enough to require
            // withdrawal, AND no near reinforcement to swing the balance.
            // The historical CW pattern: forward screen, delay lines,
            // refuse decisive engagement, force the enemy to attack you on
            // your terms. Johnston Atlanta campaign, Lee 1864.
            //
            // ESCALATION (2026-05-23 / J): DelayingDefense is NOT static.
            // If casualties mount or routs cascade while delaying, the
            // doctrine escalates to WithdrawalToFightLater. The escalation
            // thresholds are personality-modulated — aggressive commanders
            // tolerate higher losses before bailing; cautious commanders
            // pull back sooner.
            //
            // Fires when: ratio < attackThreshold (can't attack) AND
            //   either no parity coming OR parity too far out for Wait
            //   AND ratio >= withdrawalThreshold (not severely outnumbered)
            // Caller routes to DelayingDefense playbook archetype +
            // macroai = 2 (vanilla forms line).
            if (currentRatio < attackThreshold &&
                (parityForOwn >= 999f || parityForOwn > maxWaitHours))
            {
                // Check escalation triggers.
                float casualtyThreshold = ComputeCasualtyEscalationThreshold(ev.CommanderAggression01);
                float routEscalationThreshold = ComputeRoutEscalationThreshold(ev.CommanderAggression01);
                bool casualtyEscalation = ev.OwnCasualtyRatio01 >= casualtyThreshold;
                bool routEscalation = ev.OwnRoutedFraction01 >= routEscalationThreshold;
                bool prolongedEngagement = ev.BattleHoursElapsed >= ProlongedDelayHoursThreshold
                    && currentRatio < 0.50f
                    && parityForOwn >= 999f;

                if (casualtyEscalation || routEscalation || prolongedEngagement)
                {
                    string reason;
                    if (casualtyEscalation) reason = "delay-escalation-casualties";
                    else if (routEscalation) reason = "delay-escalation-routs";
                    else reason = "delay-escalation-prolonged";
                    return new ReinforcementOpportunityDecision(
                        ReinforcementOpportunity.WithdrawalToFightLater,
                        reason,
                        currentRatio, parityForEnemy, parityForOwn,
                        attackThreshold, attackWindowHours);
                }

                return new ReinforcementOpportunityDecision(
                    ReinforcementOpportunity.DelayingDefense,
                    "outnumbered-defensible-no-near-parity",
                    currentRatio, parityForEnemy, parityForOwn,
                    attackThreshold, attackWindowHours);
            }

            // DEFENSIVE HOLD: fall-through default for any case that's NOT
            // an attack opportunity, NOT a clear wait-for-relief, NOT
            // outnumbered enough to delay, and NOT outnumbered enough to
            // withdraw. Covers the "current advantage, future disadvantage"
            // middle band: own ahead now but future worse and no own relief →
            // hold ground, refuse decisive engagement, attrit them as they
            // arrive.
            return new ReinforcementOpportunityDecision(
                ReinforcementOpportunity.DefensiveHold,
                "hold-and-refuse-decisive",
                currentRatio, parityForEnemy, parityForOwn,
                attackThreshold, attackWindowHours);
        }

        /// <summary>
        /// Personality-modulated withdrawal threshold. Aggressive commanders
        /// almost never withdraw until severely outnumbered; cautious
        /// commanders withdraw earlier. Formula:
        ///   threshold = BaseWithdrawalRatioThreshold - aggression01 * WithdrawalAggressionScale
        ///   aggressive (1.0) → 0.25  (won't withdraw until 4:1 odds)
        ///   mid       (0.5) → 0.40  (withdraws at ~2.5:1 odds)
        ///   cautious  (0.0) → 0.55  (withdraws at ~2:1 odds)
        /// Clamped to [0.15, 0.70] to keep extreme personalities sane.
        /// </summary>
        public static float ComputeWithdrawalRatioThreshold(float aggression01)
        {
            float agg = Clamp01(aggression01);
            float t = BaseWithdrawalRatioThreshold - agg * WithdrawalAggressionScale;
            if (t < 0.15f) t = 0.15f;
            if (t > 0.70f) t = 0.70f;
            return t;
        }

        /// <summary>
        /// Personality-modulated casualty ratio above which a side delaying
        /// defensively escalates to withdrawal. Aggressive commanders tolerate
        /// higher losses; cautious commanders pull back sooner.
        ///   aggressive (1.0) → 0.15  (15% casualties before withdraw)
        ///   mid       (0.5) → 0.10
        ///   cautious  (0.0) → 0.05
        /// </summary>
        public static float ComputeCasualtyEscalationThreshold(float aggression01)
        {
            float agg = Clamp01(aggression01);
            return BaseCasualtyEscalationThreshold + agg * CasualtyAggressionScale;
        }

        /// <summary>
        /// Personality-modulated routed-brigade fraction above which a side
        /// delaying defensively escalates to withdrawal. Routs are a sharper
        /// signal than casualties — even moderate routs imply morale collapse.
        ///   aggressive (1.0) → 0.30
        ///   mid       (0.5) → 0.22
        ///   cautious  (0.0) → 0.15
        /// </summary>
        public static float ComputeRoutEscalationThreshold(float aggression01)
        {
            float agg = Clamp01(aggression01);
            return BaseRoutEscalationThreshold + agg * RoutAggressionScale;
        }

        public static float ComputeAttackThreshold(float commanderAggression01)
        {
            float agg = Clamp01(commanderAggression01);
            // BaseAttackThreshold × (AggressionThresholdOffset - agg × AggressionThresholdScale)
            return BaseAttackThreshold * (AggressionThresholdOffset - agg * AggressionThresholdScale);
        }

        /// <summary>
        /// Attack threshold after the battle-type rule. Vanilla
        /// CheckGlobalAIStrategy (6400-6408) attacks in a meeting engagement
        /// (GetBattleType == 0) regardless of force balance, and attacks
        /// whenever forcebalance ≥ forcebalancetriggerattack / (1 + initiative
        /// × 0.7) = 0.80 / 1.35 ≈ 0.59 at mid initiative (battleprefs) — below
        /// parity. Requiring 1.08-1.56x for every stance left both sides of a
        /// near-even meeting in DefensiveHold (BR-H1). Meeting/Attacker cap at
        /// parity; a Defender keeps the personality threshold (vanilla's
        /// defender only assaults at overwhelming balance, 6408).
        /// </summary>
        public static float EffectiveAttackThreshold(float commanderAggression01, BattleStance stance)
        {
            float t = ComputeAttackThreshold(commanderAggression01);
            if (stance != BattleStance.Defender && t > 1f) t = 1f;
            return t;
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
            // base + (1 - agg) * scale: cautious 0.0 → 28 h, mid 0.5 → 23 h,
            // aggressive 1.0 → 18 h.
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
