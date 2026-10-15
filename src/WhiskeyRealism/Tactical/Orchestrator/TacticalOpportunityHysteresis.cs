namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Anti-thrash smoother for TacticalReinforcementOpportunityDoctrine
    // decisions (2026-05-21). Ports Scourge of War's anti-oscillation pattern
    // from `sdk/SowAiInf/unitai.cpp:929-1032` (MoraleAI cascade) +
    // `sdk/SowAiInf/offai.cpp:816` (courier cooldown).
    //
    // Problem observed in smoke: TacticalReinforcementOpportunityDoctrine
    // produces deterministic decisions from instantaneous force-ratio
    // evidence. When ratio hovers near a category threshold, raw evidence
    // flips between cycles (e.g. WithdrawalToFightLater ↔ DefensiveHold
    // every 500ms-1s observed at 21:51:51-21:52:00). Each flip propagates
    // through:
    //   - Brigade-posture path → conflicting waypoints
    //   - Leaf-cascade override → conflicting tasks
    //   - ReplanTrigger.OpportunityChange → unnecessary replans
    //   - Write gates → cooldown cascade → brigade stuck mid-action
    //
    // Scourge's pattern: state transitions are time-rate-limited (courier
    // cooldown 15min, help cooldown 30min, target re-acquisition timer).
    // Same idea here at battle scale: once we COMMIT to an opportunity
    // category, hold it for a minimum span of BATTLE time before allowing a
    // transition. Brigades get stable orders, vanilla AI executes them.
    //
    // Pure scorer: no Unity API touches. Caller passes the raw doctrine
    // output + the prior decision + battle-seconds-since-last-change.
    // Returns the smoothed decision.
    public static class TacticalOpportunityHysteresis
    {
        // BATTLE seconds a decision must be held before a transition is
        // allowed (10 battle-minutes). The caller measures elapsed time on
        // TacticalDoctrineTime, so the hold means the same amount of
        // battle at 1x and 50x compression (the old 6 wall-clock seconds were
        // 36 battle-seconds at 1x and 5 battle-minutes at 50x).
        //
        // Single source of the default: the "Opportunity Hysteresis Min Hold"
        // config entry is in battle MINUTES and should bind its default to
        // DefaultMinHoldBattleMinutes; the doctrine works in seconds.
        public const float DefaultMinHoldBattleMinutes = 10f;
        public const float DefaultMinHoldBattleSeconds = DefaultMinHoldBattleMinutes * 60f;

        // Decisive-transition shortcut: even within MinHoldSeconds, allow a
        // transition if the new decision is a strong upgrade in danger
        // direction (e.g. DefensiveHold → WithdrawalToFightLater means
        // things got materially worse). Scourge's MoraleAI uses analogous
        // tier-priority — ROUTED takes immediate precedence over BROKEN
        // regardless of timing. Without this, a brigade would hold
        // DefensiveHold for 6s even when situation collapses to "should
        // be retreating right now."
        public const bool AllowImmediateEscalationToWithdrawal = true;

        public readonly struct SmoothResult
        {
            public SmoothResult(
                ReinforcementOpportunityDecision decision,
                bool changed,
                string smoothingReason)
            {
                Decision = decision;
                Changed = changed;
                SmoothingReason = string.IsNullOrEmpty(smoothingReason) ? "default" : smoothingReason;
            }

            public ReinforcementOpportunityDecision Decision { get; }
            // Whether the smoother accepted a transition this cycle (true =
            // category changed) or held the prior decision (false).
            public bool Changed { get; }
            // Diagnostic — what rule fired. Useful for telemetry showing why
            // the smoother behaved a particular way.
            public string SmoothingReason { get; }
        }

        // Smooths the raw doctrine output against the prior decision +
        // hold-time elapsed.
        //
        // raw = output of TacticalReinforcementOpportunityDoctrine.Decide()
        // prev = the decision we ACTIVELY held going into this cycle (may
        //        differ from `raw` if doctrine just flipped)
        // secondsSinceLastChange = how long `prev.Opportunity` has been
        //        held (resets on every accepted transition)
        // minHoldSeconds = override for DefaultMinHoldBattleSeconds (caller's
        //        config-bound tunable); both elapsed and hold in battle seconds
        public static SmoothResult Smooth(
            ReinforcementOpportunityDecision raw,
            ReinforcementOpportunityDecision prev,
            float secondsSinceLastChange,
            float minHoldSeconds)
        {
            if (minHoldSeconds < 0f) minHoldSeconds = DefaultMinHoldBattleSeconds;

            // Same category — no smoothing needed.
            if (raw.Opportunity == prev.Opportunity)
            {
                return new SmoothResult(raw, changed: false, smoothingReason: "same-category");
            }

            // Decisive escalation shortcut: situation got materially worse
            // and we should react immediately, not after hold expires.
            if (AllowImmediateEscalationToWithdrawal && IsEscalationToWithdrawal(prev, raw))
            {
                return new SmoothResult(raw, changed: true, smoothingReason: "escalation-to-withdrawal");
            }

            // Within hold window — keep the prior decision regardless of
            // what raw says. This is the primary anti-thrash mechanism.
            if (secondsSinceLastChange < minHoldSeconds)
            {
                return new SmoothResult(prev, changed: false, smoothingReason: "hold-time-active");
            }

            // Hold expired — accept the new decision.
            return new SmoothResult(raw, changed: true, smoothingReason: "transition-after-hold");
        }

        // Returns true when transitioning from `prev` to `raw` represents
        // a "situation got worse" escalation that should bypass hold time.
        // WithdrawalToFightLater is the most dangerous category (we're
        // outnumbered with no relief); arriving at it from anything else
        // signals the doctrine has determined urgent withdrawal is needed.
        private static bool IsEscalationToWithdrawal(
            ReinforcementOpportunityDecision prev,
            ReinforcementOpportunityDecision raw)
        {
            return raw.Opportunity == ReinforcementOpportunity.WithdrawalToFightLater
                && prev.Opportunity != ReinforcementOpportunity.WithdrawalToFightLater;
        }
    }
}
