namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure logic that recommends the next BattlePhase given the current
    /// battle state. The runtime calls this per-tick (or per-replan) and
    /// advances the orchestrator's phase whenever the recommendation differs
    /// from the current phase.
    ///
    /// Historically a battle was not one decisive blow — it cycled through
    /// probe (find a weak point), main effort (commit force there), exploit
    /// (push through a breakthrough), consolidate (hold the gain or recover),
    /// and withdraw (preserve force when the day is lost). Whiskey's plans
    /// already encode all five phases via BattlePhase, but until 2026-05-19
    /// no code ever called <see cref="ArmyOrchestrator.AdvancePhase"/> at
    /// runtime — battles sat in Probe forever. This doctrine fixes that.
    ///
    /// The recommendation is driven by:
    /// - plan age (probe and main-effort each have an aggression-scaled budget),
    /// - global force odds (favorable -> commit; reverted -> consolidate/withdraw),
    /// - main-effort sector odds spike vs history (-> exploit when probe breaks),
    /// - reserves committed fraction (commit-deep + favorable -> exploit;
    ///   commit-deep + reverted -> consolidate),
    /// - army morale floor (overrides all -> withdraw when breached).
    /// </summary>
    public static class TacticalPhaseProgressionDoctrine
    {
        // Phase budgets in seconds. Mid commander (aggression01=0.5) baselines.
        // Aggressive commanders shorten Probe (commit faster) and lengthen
        // MainEffort (sustain push). Cautious commanders do the inverse.
        public const float ProbeBudgetMidSeconds = 60f;
        public const float MainEffortBudgetMidSeconds = 120f;
        public const float ExploitBudgetMidSeconds = 90f;

        // Odds thresholds. ProbeCommitOdds: global odds above which probe phase
        // is judged "found a working axis, commit." Strictly above 1.0 so that
        // exact parity (1.0 odds — stalemate) keeps probing rather than
        // committing. ExploitTriggerOddsRatio: ratio of current main-effort
        // odds to history that signals a local breakthrough big enough to
        // exploit. ConsolidateOddsFloor: global odds below which the attacking
        // force should pause and consolidate.
        public const float ProbeCommitOddsFloor = 1.05f;

        // Consolidate exit threshold for "odds recovered, resume attack."
        // Matches the legacy 1.4 hysteresis used elsewhere.
        public const float ResumeFromConsolidateOddsFloor = 1.4f;
        public const float ExploitTriggerOddsRatio = 1.35f;
        public const float ExploitTriggerAbsoluteOdds = 1.8f;
        public const float ConsolidateOddsFloor = 0.9f;

        // Reserve commit fractions. DeepCommitFraction: at this much reserves
        // committed, the army is past the point of probing — must either
        // exploit (if odds favor) or consolidate (if odds reverted).
        public const float DeepCommitFraction = 0.65f;
        public const float ExhaustedFraction = 0.9f;

        // Same sentinel that ArmyEvidenceBuilder uses to signal "unknown
        // reserve commit." We must not let an unknown sentinel drive phase
        // progression — better to keep probing than to commit on bad data.
        public const float UnknownReserveCommitSentinel = 0.35f;
        private const float SentinelTolerance = 0.0001f;

        public readonly struct Input
        {
            public Input(
                BattlePhase currentPhase,
                float planAgeSeconds,
                float globalOddsCurrent,
                float globalOddsHistory,
                float mainEffortOddsCurrent,
                float mainEffortOddsHistory,
                float armyMoraleCurrent,
                float armyMoraleFloor,
                float reservesCommittedFraction,
                float commanderAggression01,
                TacticalSectorReadinessDoctrine.Result mainEffortReadiness = TacticalSectorReadinessDoctrine.Result.PushReady)
            {
                CurrentPhase = currentPhase;
                PlanAgeSeconds = planAgeSeconds < 0f ? 0f : planAgeSeconds;
                GlobalOddsCurrent = SanitizePositive(globalOddsCurrent, 1f);
                GlobalOddsHistory = SanitizePositive(globalOddsHistory, 1f);
                MainEffortOddsCurrent = SanitizePositive(mainEffortOddsCurrent, 1f);
                MainEffortOddsHistory = SanitizePositive(mainEffortOddsHistory, 1f);
                ArmyMoraleCurrent = SanitizePositive(armyMoraleCurrent, 1f);
                ArmyMoraleFloor = SanitizePositive(armyMoraleFloor, 0f);
                ReservesCommittedFraction = reservesCommittedFraction;
                CommanderAggression01 = Clamp01(commanderAggression01);
                MainEffortReadiness = mainEffortReadiness;
            }

            public BattlePhase CurrentPhase { get; }
            public float PlanAgeSeconds { get; }
            public float GlobalOddsCurrent { get; }
            public float GlobalOddsHistory { get; }
            public float MainEffortOddsCurrent { get; }
            public float MainEffortOddsHistory { get; }
            public float ArmyMoraleCurrent { get; }
            public float ArmyMoraleFloor { get; }
            public float ReservesCommittedFraction { get; }
            public float CommanderAggression01 { get; }
            /// <summary>
            /// Result of <see cref="TacticalSectorReadinessDoctrine.Decide"/>
            /// for the main effort sector. Defaults to PushReady so callers
            /// that haven't wired readiness yet get current behavior. When
            /// HoldForReinforcements or HoldFatigued, the Probe -> MainEffort
            /// transition is blocked even with favorable odds.
            /// </summary>
            public TacticalSectorReadinessDoctrine.Result MainEffortReadiness { get; }

            public bool IsReservesCommitKnown
            {
                get
                {
                    float diff = ReservesCommittedFraction - UnknownReserveCommitSentinel;
                    if (diff < 0f) diff = -diff;
                    return diff > SentinelTolerance;
                }
            }
        }

        public readonly struct Decision
        {
            public Decision(BattlePhase nextPhase, string reason)
            {
                NextPhase = nextPhase;
                Reason = string.IsNullOrEmpty(reason) ? "no-change" : reason;
            }

            public BattlePhase NextPhase { get; }
            public string Reason { get; }
        }

        /// <summary>
        /// Decide the next phase. Returns a decision where
        /// <see cref="Decision.NextPhase"/> equals <paramref name="input"/>.
        /// <see cref="Input.CurrentPhase"/> when no change is recommended.
        /// </summary>
        public static Decision Decide(Input input)
        {
            // Morale-floor override: withdraw from any phase except already
            // withdrawing. Overrides every other rule because if morale
            // breaks, the army is going home regardless of plan.
            if (input.ArmyMoraleCurrent < input.ArmyMoraleFloor &&
                input.CurrentPhase != BattlePhase.Withdraw)
                return new Decision(BattlePhase.Withdraw, "morale-floor-breached");

            switch (input.CurrentPhase)
            {
                case BattlePhase.Probe:
                    return DecideFromProbe(input);
                case BattlePhase.MainEffort:
                    return DecideFromMainEffort(input);
                case BattlePhase.Exploit:
                    return DecideFromExploit(input);
                case BattlePhase.Consolidate:
                    return DecideFromConsolidate(input);
                case BattlePhase.Withdraw:
                default:
                    // Withdraw is absorbing — never advance back to attack.
                    return new Decision(BattlePhase.Withdraw, "withdraw-absorbing");
            }
        }

        private static Decision DecideFromProbe(Input input)
        {
            // Probe completes when EITHER:
            //   (a) Global odds clear ProbeCommitOddsFloor AND main effort odds
            //       are favorable AND sector readiness permits the push.
            //   (b) Plan age exceeds the aggression-scaled probe budget ->
            //       time's up. If global odds bad -> Consolidate. If readiness
            //       says HoldForReinforcements -> still keep probing (waiting
            //       for relief beats pushing fatigued troops). Otherwise
            //       commit to MainEffort.
            //
            // The readiness gate is what implements "commander has fresh
            // troops or stages them": HoldForReinforcements keeps the army
            // probing until relief lands, HoldFatigued falls back to
            // Consolidate when the budget runs out (cautious + no relief
            // means preserve force), PushDegraded permits commit (aggressive
            // commander accepts the casualty trade).
            float probeBudget = ProbeBudgetForAggression(input.CommanderAggression01);
            bool readinessAllowsPush =
                input.MainEffortReadiness == TacticalSectorReadinessDoctrine.Result.PushReady ||
                input.MainEffortReadiness == TacticalSectorReadinessDoctrine.Result.PushDegraded;

            if (input.GlobalOddsCurrent >= ProbeCommitOddsFloor &&
                input.MainEffortOddsCurrent >= ProbeCommitOddsFloor &&
                readinessAllowsPush)
                return new Decision(BattlePhase.MainEffort, "probe-found-purchase");

            // Odds favor commit but readiness blocks — stay in Probe.
            // Reason string distinguishes the two hold cases for telemetry.
            if (input.GlobalOddsCurrent >= ProbeCommitOddsFloor &&
                input.MainEffortOddsCurrent >= ProbeCommitOddsFloor &&
                !readinessAllowsPush)
                return new Decision(BattlePhase.Probe,
                    input.MainEffortReadiness == TacticalSectorReadinessDoctrine.Result.HoldForReinforcements
                        ? "probe-holds-for-fresh-relief"
                        : "probe-holds-fatigued-force");

            if (input.PlanAgeSeconds >= probeBudget)
            {
                if (input.GlobalOddsCurrent < ConsolidateOddsFloor)
                    return new Decision(BattlePhase.Consolidate, "probe-budget-and-odds-poor");
                // Budget elapsed: respect readiness — Hold outcomes prefer
                // Consolidate (force preservation) over a degraded push the
                // commander didn't authorize.
                if (input.MainEffortReadiness == TacticalSectorReadinessDoctrine.Result.HoldForReinforcements)
                    return new Decision(BattlePhase.Probe, "probe-budget-but-relief-imminent");
                if (input.MainEffortReadiness == TacticalSectorReadinessDoctrine.Result.HoldFatigued)
                    return new Decision(BattlePhase.Consolidate, "probe-budget-with-fatigued-force");
                return new Decision(BattlePhase.MainEffort, "probe-budget-elapsed");
            }

            return new Decision(BattlePhase.Probe, "probe-continuing");
        }

        private static Decision DecideFromMainEffort(Input input)
        {
            float mainEffortBudget = MainEffortBudgetForAggression(input.CommanderAggression01);

            // Exploit gate: local main-effort sector odds spike vs history OR
            // absolute spike past the trigger. This is "probe broke through —
            // funnel reserves into the gap." Mirrors historical Lee at
            // Chancellorsville Day 2 funneling Jackson's wing into the gap
            // after the morning probe found the Union right exposed.
            bool localBreakthrough =
                (input.MainEffortOddsHistory > 0.01f &&
                 input.MainEffortOddsCurrent / input.MainEffortOddsHistory >= ExploitTriggerOddsRatio) ||
                input.MainEffortOddsCurrent >= ExploitTriggerAbsoluteOdds;
            if (localBreakthrough &&
                input.IsReservesCommitKnown && input.ReservesCommittedFraction >= 0.4f)
            {
                // Exploit also gated on readiness — pushing through a
                // breakthrough with fatigued troops squanders the
                // opportunity. HoldForReinforcements stays in MainEffort
                // (don't exploit until relief lands); HoldFatigued stays in
                // MainEffort but the cautious commander won't escalate.
                // PushReady / PushDegraded both permit Exploit.
                if (input.MainEffortReadiness == TacticalSectorReadinessDoctrine.Result.PushReady ||
                    input.MainEffortReadiness == TacticalSectorReadinessDoctrine.Result.PushDegraded)
                    return new Decision(BattlePhase.Exploit, "local-breakthrough-detected");
                return new Decision(BattlePhase.MainEffort,
                    input.MainEffortReadiness == TacticalSectorReadinessDoctrine.Result.HoldForReinforcements
                        ? "breakthrough-detected-but-fresh-relief-incoming"
                        : "breakthrough-detected-but-force-fatigued");
            }

            // Consolidate gate: budget elapsed OR odds reverted with deep
            // commit (we're in too far to keep pushing; lock down gains).
            if (input.PlanAgeSeconds >= mainEffortBudget)
                return new Decision(BattlePhase.Consolidate, "main-effort-budget-elapsed");

            if (input.GlobalOddsCurrent < ConsolidateOddsFloor &&
                input.IsReservesCommitKnown && input.ReservesCommittedFraction >= DeepCommitFraction)
                return new Decision(BattlePhase.Consolidate, "odds-reverted-and-deep-commit");

            return new Decision(BattlePhase.MainEffort, "main-effort-continuing");
        }

        private static Decision DecideFromExploit(Input input)
        {
            // Exploit ends when reserves exhausted OR odds revert. Exploit
            // is the high-tempo phase; it can't last long because reserves
            // run out and the enemy reorganizes.
            if (input.IsReservesCommitKnown && input.ReservesCommittedFraction >= ExhaustedFraction)
                return new Decision(BattlePhase.Consolidate, "reserves-exhausted-after-exploit");

            if (input.GlobalOddsCurrent < ConsolidateOddsFloor)
                return new Decision(BattlePhase.Consolidate, "odds-reverted-during-exploit");

            if (input.PlanAgeSeconds >= ExploitBudgetMidSeconds)
                return new Decision(BattlePhase.Consolidate, "exploit-budget-elapsed");

            return new Decision(BattlePhase.Exploit, "exploit-continuing");
        }

        private static Decision DecideFromConsolidate(Input input)
        {
            // Consolidate is normally an absorbing-ish phase that ends with
            // either resumed attack (if odds swing back high enough) or
            // withdraw on morale loss. Exit back to MainEffort needs a
            // strong odds reversal AND enough reserves to mean it.
            if (input.GlobalOddsCurrent >= 1.4f &&
                input.IsReservesCommitKnown && input.ReservesCommittedFraction < 0.6f)
                return new Decision(BattlePhase.MainEffort, "odds-recovered-resume-attack");

            return new Decision(BattlePhase.Consolidate, "consolidate-continuing");
        }

        /// <summary>
        /// Probe budget scaled by commander aggression. Aggressive commanders
        /// (>0.75) commit at 30s; cautious commanders (&lt;0.25) probe for 120s.
        /// </summary>
        public static float ProbeBudgetForAggression(float aggression01)
        {
            float a = Clamp01(aggression01);
            // Linear: 0.0 -> 120, 0.5 -> 60, 1.0 -> 30
            return 120f - a * 90f;
        }

        /// <summary>
        /// Main effort budget scaled by commander aggression. Aggressive
        /// commanders sustain attack longer (180s); cautious commanders pull
        /// back into consolidate sooner (60s).
        /// </summary>
        public static float MainEffortBudgetForAggression(float aggression01)
        {
            float a = Clamp01(aggression01);
            // Linear: 0.0 -> 60, 0.5 -> 120, 1.0 -> 180
            return 60f + a * 120f;
        }

        private static float SanitizePositive(float v, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return fallback;
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
