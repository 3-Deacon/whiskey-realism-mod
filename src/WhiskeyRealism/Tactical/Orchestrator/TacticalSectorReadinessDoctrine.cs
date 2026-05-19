namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure logic that answers "should I push the attack in this sector NOW
    /// with the troops I have, OR hold for fresh ones?"
    ///
    /// Historical anchor: commanders rarely committed the same brigades to
    /// two consecutive pushes — fatigued, low-ammo units were rotated out
    /// or held in reserve while fresh formations made the next attack
    /// (Longstreet's choice not to renew the Day 2 attack on Day 3 at Gettysburg,
    /// Grant's rotation pattern at the Wilderness, Lee's careful staging on
    /// Day 2). The decision is: do I have effective force in this sector?
    /// If not, are reinforcements arriving in time to make it work? If still
    /// no — does my commander press anyway (Hood) or preserve the force
    /// (Johnston / McClellan)?
    ///
    /// Composes with <see cref="TacticalPhaseProgressionDoctrine"/>: when
    /// progression would advance Probe → MainEffort, readiness must also
    /// be PushReady (or PushDegraded for aggressive commanders). Hold
    /// outcomes block the phase advance — battle stays in Probe.
    /// </summary>
    public static class TacticalSectorReadinessDoctrine
    {
        public enum Result
        {
            // Fresh troops available; sector effective-force-ratio meets the
            // commander's attack threshold. Phase progression can commit.
            PushReady = 0,
            // No fresh force AND no reinforcements coming. Aggressive
            // commander presses anyway, accepting the casualty trade.
            PushDegraded = 1,
            // Effective force insufficient now BUT reinforcements arriving
            // within the hold window AND would tip the balance. Stay in
            // Probe, let the relief land first.
            HoldForReinforcements = 2,
            // Effective force insufficient, no relief, cautious commander
            // would rather preserve the force than spend it on a marginal
            // attack. Falls back to Probe / Consolidate.
            HoldFatigued = 3,
        }

        // Base attack ratio (effective_own / enemy) below which the attack is
        // not viable. Aggressive commanders accept thinner margins.
        public const float BaseAttackRatio = 1.20f;
        // Aggression scale: high aggression lowers the threshold, low
        // aggression raises it.
        public const float AggressionRatioScale = 0.50f;
        public const float AggressionRatioOffset = 1.35f;

        // Reinforcement window (hours): if relief arrives within this many
        // game-hours AND would push effective force above the attack ratio,
        // prefer HoldForReinforcements over PushDegraded.
        public const float ReinforcementWaitHoursMid = 4f;
        public const float ReinforcementWaitHoursScale = 4f;
        public const float ReinforcementWaitHoursOffset = 6f;

        // Below this aggression threshold, "no relief + no fresh" becomes
        // HoldFatigued (preserve); above it becomes PushDegraded (press).
        public const float PressVsHoldAggressionThreshold = 0.55f;

        // Minimum effective-force fraction (effective / raw own) below which
        // the force is considered too degraded to push regardless of ratio.
        // Scale-invariant — works for a 500-man cavalry detachment AND a
        // 60,000-man field army (GTCW battles span both extremes). Replaces
        // the prior MinViablePushEffectiveStrength=1500 absolute floor which
        // permanently blocked PushReady for small-force engagements.
        //
        // 0.25 = army at 25% combat effectiveness. Below that, fatigue+ammo+
        // morale stack has collapsed too far to push effectively. Examples:
        //   fresh+full-ammo+full-morale  ->  1.0 (always pushes)
        //   0.4 fatigue, 0.7 ammo, 0.8 morale  ->  0.336 (push-eligible)
        //   0.6 fatigue, 0.5 ammo, 0.7 morale  ->  0.14 (HoldFatigued)
        public const float MinEffectiveForceFraction = 0.25f;

        public readonly struct Input
        {
            public Input(
                float ownRawStrength,
                float avgFatigue01,
                float avgAmmo01,
                float avgMorale01,
                float enemyStrength,
                float reinforcementHours,
                float reinforcementStrength,
                float commanderAggression01)
            {
                OwnRawStrength = SanitizeNonNeg(ownRawStrength);
                AvgFatigue01 = Clamp01(avgFatigue01);
                AvgAmmo01 = Clamp01(avgAmmo01);
                AvgMorale01 = Clamp01(avgMorale01);
                EnemyStrength = SanitizeNonNeg(enemyStrength);
                ReinforcementHours = SanitizeNonNeg(reinforcementHours);
                ReinforcementStrength = SanitizeNonNeg(reinforcementStrength);
                CommanderAggression01 = Clamp01(commanderAggression01);
            }

            public float OwnRawStrength { get; }
            public float AvgFatigue01 { get; }
            public float AvgAmmo01 { get; }
            public float AvgMorale01 { get; }
            public float EnemyStrength { get; }
            public float ReinforcementHours { get; }
            public float ReinforcementStrength { get; }
            public float CommanderAggression01 { get; }

            /// <summary>
            /// Effective force = raw strength × (1 - fatigue) × ammo × morale.
            /// A 5000-strong brigade at 50% fatigue, 80% ammo, 90% morale =
            /// 5000 × 0.5 × 0.8 × 0.9 = 1800 effective. SoW's fear/charge
            /// calculations similarly downgrade tired/low-ammo troops.
            /// </summary>
            public float OwnEffectiveStrength =>
                OwnRawStrength * (1f - AvgFatigue01) * AvgAmmo01 * AvgMorale01;

            /// <summary>
            /// Effective strength if reinforcements have landed. Assumes
            /// arriving troops are fresh (fatigue=0, ammo=1, morale=1) —
            /// they just got here. Used to test "would relief make the attack
            /// work."
            /// </summary>
            public float OwnEffectiveStrengthAfterReinforcement =>
                OwnEffectiveStrength + ReinforcementStrength;
        }

        public readonly struct Decision
        {
            public Decision(Result result, string reason, float effectiveRatio, float requiredRatio)
            {
                Result = result;
                Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
                EffectiveRatio = effectiveRatio;
                RequiredRatio = requiredRatio;
            }

            public Result Result { get; }
            public string Reason { get; }
            public float EffectiveRatio { get; }
            public float RequiredRatio { get; }
        }

        public static Decision Decide(Input input)
        {
            float requiredRatio = ComputeRequiredRatio(input.CommanderAggression01);
            float reinforcementWindow = ComputeReinforcementWaitHours(input.CommanderAggression01);

            // Guard: army is at <25% combat effectiveness (fatigue+ammo+morale
            // stack collapsed). Pushing in this state squanders the force.
            // Scale-invariant — independent of headcount. Replaces the prior
            // 1500-man absolute floor which spuriously blocked small armies.
            float forceHealth = input.OwnRawStrength > 0f
                ? input.OwnEffectiveStrength / input.OwnRawStrength
                : 0f;
            if (forceHealth < MinEffectiveForceFraction && input.OwnRawStrength > 0f)
            {
                return new Decision(
                    Result.HoldFatigued,
                    "force-health-below-min-fraction",
                    input.EnemyStrength > 0f ? input.OwnEffectiveStrength / input.EnemyStrength : 0f,
                    requiredRatio);
            }

            float currentRatio = input.EnemyStrength > 0f
                ? input.OwnEffectiveStrength / input.EnemyStrength
                : 10f; // sentinel: enemy gone, push freely

            // PushReady: effective force already covers the threshold.
            if (currentRatio >= requiredRatio)
            {
                return new Decision(Result.PushReady, "effective-force-sufficient",
                    currentRatio, requiredRatio);
            }

            // Not ready now. Check reinforcement window.
            bool reliefInWindow =
                input.ReinforcementHours > 0f &&
                input.ReinforcementHours <= reinforcementWindow &&
                input.ReinforcementStrength > 0f;
            if (reliefInWindow)
            {
                float futureRatio = input.EnemyStrength > 0f
                    ? input.OwnEffectiveStrengthAfterReinforcement / input.EnemyStrength
                    : 10f;
                if (futureRatio >= requiredRatio)
                {
                    return new Decision(Result.HoldForReinforcements,
                        "fresh-relief-in-window-will-tip-balance",
                        currentRatio, requiredRatio);
                }
                // Relief in window but still won't tip — fall through to
                // press/hold decision; relief doesn't change the calculus.
            }

            // No useful relief. Aggressive commanders press; cautious hold.
            if (input.CommanderAggression01 >= PressVsHoldAggressionThreshold)
            {
                return new Decision(Result.PushDegraded,
                    "no-fresh-no-relief-aggressive-presses",
                    currentRatio, requiredRatio);
            }

            return new Decision(Result.HoldFatigued,
                "no-fresh-no-relief-cautious-holds",
                currentRatio, requiredRatio);
        }

        /// <summary>
        /// Attack ratio scaled by commander aggression. Aggression=1.0 →
        /// ratio 0.85 (will attack while outnumbered if effective force is
        /// quality). Aggression=0.0 → ratio 1.35 (needs material advantage).
        /// Aggression=0.5 → ratio 1.10 (slight edge required).
        /// </summary>
        public static float ComputeRequiredRatio(float aggression01)
        {
            float a = Clamp01(aggression01);
            // BaseAttackRatio * (AggressionRatioOffset - a * AggressionRatioScale)
            // = 1.20 * (1.35 - a*0.50) → at a=0 → 1.620, at a=1 → 1.020
            // adjust: target 1.35 at a=0, 0.85 at a=1
            // Linear in a: ratio(a) = 1.35 - 0.50*a
            return BaseAttackRatio * (AggressionRatioOffset - a * AggressionRatioScale) / BaseAttackRatio;
        }

        /// <summary>
        /// Reinforcement wait window scaled by aggression. Aggressive
        /// commanders won't wait long (2h); cautious commanders will wait
        /// up to 6h for the right hand. Mirrors
        /// <see cref="TacticalReinforcementOpportunityDoctrine.ComputeMaxWaitHours"/>
        /// shape but per-sector instead of global.
        /// </summary>
        public static float ComputeReinforcementWaitHours(float aggression01)
        {
            float a = Clamp01(aggression01);
            // ReinforcementWaitHoursOffset(6) - a * ReinforcementWaitHoursScale(4)
            // at a=0 → 6, at a=1 → 2, at a=0.5 → 4
            return ReinforcementWaitHoursOffset - a * ReinforcementWaitHoursScale;
        }

        private static float SanitizeNonNeg(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return 0f;
            return v;
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
