using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Evidence the orchestrator reads at decision time. Built by the runtime
    /// partial of TacticalBattleCoordinator from existing battle ledgers
    /// (TacticalSectorLedger, TacticalOddsDoctrine, etc.) and passed into
    /// PickInitialPlan. Test-friendly — no Unity types, no vanilla types.
    /// </summary>
    public readonly struct ArmyEvidence
    {
        public ArmyEvidence(float currentOdds, TerrainKind terrain, int defaultMainEffortSector, TerrainLine crestLine = default(TerrainLine))
        {
            CurrentOdds = currentOdds;
            Terrain = terrain;
            DefaultMainEffortSector = defaultMainEffortSector;
            CrestLine = crestLine;
        }

        public float CurrentOdds { get; }
        public TerrainKind Terrain { get; }
        public int DefaultMainEffortSector { get; }

        /// <summary>BC12: the best crest across the front from the terrain picture (Has = false when none, or no picture).</summary>
        public TerrainLine CrestLine { get; }
    }

    /// <summary>
    /// What the army looked like at its last replan (or plan creation). Replan
    /// triggers compare against it so they fire on a change, not on a level
    /// (T7). Main-effort band values are seeded lazily from the first reading
    /// of the current main-effort band.
    /// </summary>
    public readonly struct ReplanBaseline
    {
        public ReplanBaseline(
            float morale,
            float reservesCommitted,
            float reinforcementShare,
            InferredIntent enemyIntent,
            int mainEffortSector,
            float mainEffortOwnStrength,
            float mainEffortOdds)
        {
            Morale = morale;
            ReservesCommitted = reservesCommitted;
            ReinforcementShare = reinforcementShare;
            EnemyIntent = enemyIntent;
            MainEffortSector = mainEffortSector;
            MainEffortOwnStrength = mainEffortOwnStrength;
            MainEffortOdds = mainEffortOdds;
        }

        public static ReplanBaseline Initial =>
            new ReplanBaseline(1f, 0f, 0f, InferredIntent.Unknown, -1, 0f, 0f);

        public float Morale { get; }
        public float ReservesCommitted { get; }
        public float ReinforcementShare { get; }
        public InferredIntent EnemyIntent { get; }
        public int MainEffortSector { get; }
        public float MainEffortOwnStrength { get; }
        public float MainEffortOdds { get; }

        public bool HasMainEffortSnapshot(int sector) => MainEffortSector == sector && MainEffortOwnStrength > 0f;

        public ReplanBaseline WithMainEffort(int sector, float ownStrength, float odds) =>
            new ReplanBaseline(Morale, ReservesCommitted, ReinforcementShare, EnemyIntent, sector, ownStrength, odds);
    }

    /// <summary>
    /// Army echelon orchestrator. Owns the army CO's TacticalBattlePlan, exposes
    /// CurrentMacroAi for the rewired BattleMacroStrategyPatch to read, and
    /// allocates direct-child (corps) roles each tick.
    ///
    /// AIBattle.macroai values: -1 dynamic / 0 assault / 1 attack / 2 defend / 3 retreat.
    /// </summary>
    public sealed class ArmyOrchestrator : EchelonOrchestrator
    {
        private readonly TacticalPlaybookCatalog _catalog;
        private readonly PersonalityVector _commanderPersonality;
        private TacticalBattlePlan _plan;
        private float _planAgeSeconds;
        private float _historyGlobalOdds;
        private TacticalIntentModel _currentIntentModel;
        private Plays.IFeasibilityLookup _feasibility;
        private readonly SelectionReport _selection = new SelectionReport();

        public ArmyOrchestrator(int allianceId, TacticalPlaybookCatalog catalog, PersonalityVector commanderPersonality)
            : base(EchelonKind.Army, allianceId)
        {
            _catalog = catalog;
            _commanderPersonality = commanderPersonality;
            _planAgeSeconds = 0f;
            _phaseAgeSeconds = 0f;
            _historyGlobalOdds = 1f;
            _currentIntentModel = UnknownIntentModel();
            HasPlan = false;
        }

        public bool HasPlan { get; private set; }
        public TacticalBattlePlan CurrentPlan => _plan;
        public PersonalityVector CommanderPersonality => _commanderPersonality;
        public float PlanAgeSeconds => _planAgeSeconds;
        public float HistoryGlobalOdds => _historyGlobalOdds;
        public TacticalIntentModel CurrentIntentModel => _currentIntentModel;

        /// <summary>BC14: the side's feasibility ledger (null with the flag off), read by the pick and by BC11's maneuvers.</summary>
        public Plays.IFeasibilityLookup Feasibility => _feasibility;

        /// <summary>BC14: the side's feasibility ledger the next pick reads (null: the shipped selection, unchanged).</summary>
        public void SetFeasibility(Plays.IFeasibilityLookup lookup) => _feasibility = lookup;


        private ReplanBaseline _replanBaseline = ReplanBaseline.Initial;
        public ReplanBaseline LastReplanBaseline => _replanBaseline;

        /// <summary>Recorded by the tick cycle right after a replan.</summary>
        public void RecordReplanBaseline(ReplanBaseline baseline) => _replanBaseline = baseline;

        public void PickInitialPlan(ArmyEvidence evidence)
        {
            if (TryPickPlan(evidence, opposingCommanderHint: 0f, hysteresis: false, "initial", out var nextPlan))
            {
                _plan = nextPlan;
                HasPlan = true;
                _feasibility?.CaptureSelection(_plan.PlanId, _plan.Mirrored);
            }
            else
            {
                HasPlan = false;
            }
            _historyGlobalOdds = evidence.CurrentOdds;
            _planAgeSeconds = 0f;
            _phaseAgeSeconds = 0f;
            _replanBaseline = ReplanBaseline.Initial;
        }

        /// <summary>
        /// Advances plan age (time since the last replan) and phase age (time
        /// since the last phase transition) by the same doctrine-clock delta.
        /// The tick cycle passes doctrine seconds (TacticalDoctrineTime).
        /// </summary>
        public void AdvancePlanAge(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return;
            _planAgeSeconds += deltaSeconds;
            _phaseAgeSeconds += deltaSeconds;
        }

        /// <summary>
        /// The playbooks that reach scoring for this evidence and the side's current effective stance (precondition, stance and crest gates):
        /// the candidates BC14's feasibility evaluation times before a pick. Returns how many were written.
        /// </summary>
        public int EligiblePlans(ArmyEvidence evidence, BattlePlanId[] into)
        {
            if (_catalog == null) return 0;
            // Best catalog score first, without the feasibility term: the order the confirm queue and the look requests read.
            PlaybookContext ctx = BuildPickContext(evidence, 0f, false, withFeasibility: false);
            _catalog.Select(ctx, _rankScratch);
            int n = 0;
            for (int i = 0; i < _rankScratch.Count && n < into.Length; i++)
                if (!_rankScratch[i].Skipped) into[n++] = _rankScratch[i].Plan;
            return n;
        }

        private readonly SelectionReport _rankScratch = new SelectionReport();

        /// <summary>The battle stance the catalog filters by: vanilla's battle type, demoted to Defender when the force-aware doctrine wants no offence.</summary>
        public BattleStance EffectiveStance
            => TacticalStanceReconciler.ResolveEffectiveStance(_currentForceBalanceStance, _currentReinforcementOpportunity.Opportunity);

        private bool TryPickPlan(ArmyEvidence evidence, float opposingCommanderHint, bool hysteresis, string trigger, out TacticalBattlePlan nextPlan)
        {
            PlaybookContext ctx = BuildPickContext(evidence, opposingCommanderHint, hysteresis);
            bool envelopmentPressure = ctx.EnvelopmentPressure;
            var pb = _catalog?.Select(ctx, _selection);
            if (pb == null)
            {
                nextPlan = default;
                return false;
            }
            nextPlan = pb.Instantiate(ctx);
            // The side the feasibility evaluation scored the winner on goes out with the plan (requirement-selected-side).
            if (_feasibility != null && _feasibility.TryGet(pb.Id, out Plays.CandidateResult scored)) nextPlan = nextPlan.WithMirrored(scored.Mirrored);
            double now = TacticalDoctrineTime.NowBattleHours;
            double ledgerAge = _feasibility != null && _feasibility.HasEvaluation ? now - _feasibility.EvaluatedHours : double.PositiveInfinity;
            bool kept = hysteresis && HasPlan && pb.Id == _plan.PlanId;
            Plays.FeasibilityTelemetry.PlanSelection(AllianceId, trigger, kept, ctx.Stance, ctx.CurrentOdds, _selection, pb.Id, nextPlan.Mirrored, ledgerAge, now, _feasibility != null ? _feasibility.Willingness : -1f);

            // Envelopment-active tightening: commit reserves sooner when the
            // doctrine signaled AttackNow + clear advantage. Caps to the
            // EnvelopmentReserveCommitOddsCap floor so a playbook that already
            // commits earlier than the cap (e.g., GenericDesperate=0.9) is not
            // worsened. Sympathetic to SoW's "all engaged" posture under clear
            // advantage (offai.cpp:947) without going to full commit.
            if (envelopmentPressure && nextPlan.ReserveCommitTriggerOdds > EnvelopmentReserveCommitOddsCap)
            {
                nextPlan = nextPlan.WithReserveCommitTriggerOdds(EnvelopmentReserveCommitOddsCap);
            }
            return true;
        }

        private PlaybookContext BuildPickContext(ArmyEvidence evidence, float opposingCommanderHint, bool hysteresis, bool withFeasibility = true)
        {
            // Envelopment pressure: replan-time read of the latest reinforcement
            // opportunity decision. AttackNow + personality-aware ratio threshold
            // biases the catalog toward Lee/Jackson/Hooker/Sherman envelopment
            // playbooks. Mirrors SoW's grand-tactics play selection
            // (offai.cpp:1144-1168) but uses a doctrine flag instead of a
            // pre-battle table. Aggressive commanders envelop at parity;
            // cautious commanders require a clearer margin (1.4×).
            float envelopAggression01 = (_commanderPersonality.Aggression + 1f) * 0.5f;
            if (envelopAggression01 < 0f) envelopAggression01 = 0f;
            if (envelopAggression01 > 1f) envelopAggression01 = 1f;
            float envelopMinRatio = EffectiveEnvelopmentMinRatio(envelopAggression01);
            bool envelopmentPressure =
                _currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.AttackNow &&
                _currentReinforcementOpportunity.CurrentRatio >= envelopMinRatio;
            // Force-composition probe (2026-05-21 precondition gate). Walks
            // BattleUnits.completeunitlist filtered to this alliance, computes
            // strength fractions per branch (infantry/artillery/cavalry). Used
            // by TacticalPlaybook.MatchesPreconditions to hard-filter
            // cavalry-specific playbooks (Buford, Forrest, etc.) when the
            // force lacks cavalry. Defaults to 1f (permissive) if the probe
            // can't complete — preserves pre-gate behavior on degradation.
            //
            // Branch resolution: vanilla unittyp values (see decompile
            // line 5072: cavalry-mount-up checks unittyp == 1 || 4; line 4080:
            // infantry filter is unittyp == 0 || 1; artillery is unittyp == 2).
            // Cavalry is identified as unittyp == 4 (the unambiguous case);
            // unittyp == 1 is mounted-infantry hybrid which doesn't trigger
            // Buford-style plays.
            float cavalryFraction = 1f, infantryFraction = 1f, artilleryFraction = 1f;
            try
            {
                var units = BattleUnits.completeunitlist as System.Collections.IList;
                if (units != null && units.Count > 0)
                {
                    float total = 0f, inf = 0f, cav = 0f, art = 0f;
                    for (int i = 0; i < units.Count; i++)
                    {
                        var r = units[i] as Regiment;
                        if (r == null) continue;
                        if (r.alliance != AllianceId) continue;
                        if (r.isrouted || r.markedforrout || r.permanentlydetached) continue;
                        float strength = 0f;
                        try { strength = r.groupstrengthdirect; } catch { strength = 1f; }
                        if (strength <= 0f) strength = 1f;
                        total += strength;
                        if (r.unittyp == 0 || r.unittyp == 1) inf += strength;
                        else if (r.unittyp == 2) art += strength;
                        else if (r.unittyp == 4) cav += strength;
                    }
                    if (total > 0f)
                    {
                        cavalryFraction = cav / total;
                        infantryFraction = inf / total;
                        artilleryFraction = art / total;
                    }
                }
            }
            catch { /* preserve defaults (permissive) */ }

            // DelayingDefense pressure: biases catalog toward delay+skirmish
            // archetypes when the doctrine says we're outnumbered + no near
            // parity but still defensible (added 2026-05-23 / I).
            bool delayingDefensePressure =
                _currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.DelayingDefense;

            // Defensive pressure (2026-05-23 / K): broader signal — fires
            // for ANY defensive doctrine outcome (DelayingDefense,
            // DefensiveHold, WaitAndConsolidate). Stronger catalog weight
            // (0.35 vs 0.2) so the playbook MUST agree with the doctrine
            // when doctrine wants defense.
            bool defensivePressure =
                _currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.DelayingDefense ||
                _currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.DefensiveHold ||
                _currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.WaitAndConsolidate;

            // Battle stance (2026-05-23 / L): pulled from the latest force-balance
            // evidence (which read vanilla's GetBattleType). Hard-filters the
            // catalog so a defender CANNOT pick an offensive playbook and an
            // attacker CANNOT pick a pure-delay playbook.
            //
            // Force-aware demotion (2026-05-25 / A): GetBattleType only knows
            // "is our mission to take ground?" — it does NOT know the force
            // ratio. An outnumbered side that is nominally the objective-
            // attacker was being FORCED into an offensive playbook
            // (FrontalShockAssault) because the hard filter removed every
            // defensive archetype before the DelayingDefensePressure /
            // DefensivePressure biases could apply. When the force-aware
            // doctrine wants a non-offensive posture, demote Attacker/Meeting →
            // Defender so the outnumbered side delays/defends instead of
            // launching a doomed assault. See TacticalStanceReconciler.
            BattleStance stance = EffectiveStance;

            var ctx = new PlaybookContext(
                _commanderPersonality,
                evidence.Terrain,
                evidence.CurrentOdds,
                opposingCommanderHint,
                defaultMainEffortSector: TryObjectiveSector(out int objectiveSector) ? objectiveSector : evidence.DefaultMainEffortSector,
                jitterSeed: AllianceId * 31 + 7,
                envelopmentPressure: envelopmentPressure,
                allianceId: AllianceId,
                cavalryFraction: cavalryFraction,
                infantryFraction: infantryFraction,
                artilleryFraction: artilleryFraction,
                delayingDefensePressure: delayingDefensePressure,
                defensivePressure: defensivePressure,
                stance: stance,
                hasCrestLine: evidence.CrestLine.Has,
                feasibility: withFeasibility ? _feasibility : null,
                hysteresisPlan: hysteresis && HasPlan ? _plan.PlanId : BattlePlanId.Unknown,
                hysteresisMargin: Plays.ManeuverFeasibility.ReplanScoreMargin,
                focalSector: FocalSectorOf(_feasibility),
                hasFocalPoint: _feasibility != null && _feasibility.TryFocalPoint(out _));
            return ctx;
        }

        // BC14W: the front-axis band the ledger's focal point stands in; -1 with no focal point or no axis.
        private int FocalSectorOf(Plays.IFeasibilityLookup feasibility)
        {
            if (feasibility == null || !_frontAxis.IsValid || !feasibility.TryFocalPoint(out WhiskeyRealism.Tactical.Battle.FocalPoint focal)) return -1;
            return _frontAxis.SectorOf(focal.X, focal.Z);
        }

        /// <summary>
        /// Reserve-commit-trigger-odds cap when envelopment pressure is active.
        /// At this odds level, reserves release into the wings to keep attack
        /// pressure up. A defensive playbook (Longstreet=1.5, McClellan=1.6)
        /// caps here; aggressive playbooks (Hood=1.0, Jackson=1.0, Desperate=0.9)
        /// keep their playbook default.
        /// </summary>
        public const float EnvelopmentReserveCommitOddsCap = 1.1f;

        // Phase-local age: time since the last phase transition. Distinct
        // from _planAgeSeconds (time since last Replan). Decoupled in
        // 2026-05-19 so phase progression can reset the phase budget without
        // also resetting the PhaseDeadline-replan timer (each phase has its
        // own budget; the overall plan still has an overarching deadline).
        private float _phaseAgeSeconds = 0f;
        public float PhaseAgeSeconds => _phaseAgeSeconds;

        public void AdvancePhase(BattlePhase next)
        {
            if (!HasPlan) return;
            if (_plan.Phase == next) return;
            _plan = _plan.WithPhase(next);
            _phaseAgeSeconds = 0f;
            // Note: _planAgeSeconds is NOT reset — that timer measures time
            // since last Replan, which is independent of phase transitions.
        }

        /// <summary>
        /// Sector-driven main-effort shift. Runtime passes in the decisive
        /// sector id from <see cref="WhiskeyRealism.Tactical.TacticalSectorLedger.Evaluate"/>;
        /// if it differs from the current plan's MainEffortSector AND we're
        /// in a phase where shifting makes sense (Probe or MainEffort), the
        /// plan is updated. Defensive phases (Consolidate, Withdraw) keep
        /// their established sector so the army doesn't change direction
        /// while breaking contact.
        ///
        /// Returns true iff the main effort shifted. Caller should rebuild
        /// the leaf-brigade map (signature includes MainEffortSector so this
        /// is automatic on the next UpdateLeafBrigadeMap call).
        /// </summary>
        /// <summary>
        /// Hysteresis margin for main effort shift: candidate must be at
        /// least this much better than current. Prevents thrash under 20x
        /// time compression where sector odds bounce in real-time terms
        /// (the underlying battle simulation runs faster than human review
        /// can confirm). 0.25 = candidate must be 25% better in odds.
        /// Mirrors ArmyReplanTriggers.BreakthroughOpportunityMargin in spirit
        /// (defensive bias against overreacting to marginal swings).
        /// </summary>
        public const float MainEffortShiftMarginRatio = 0.25f;

        /// <summary>
        /// Considers shifting the main effort sector to the candidate
        /// decisive sector. Caller passes the candidate sector AND its odds
        /// AND the current main effort sector's odds; the shift only fires
        /// if the candidate is materially better (margin = 25% of current).
        /// Hysteresis prevents flicker at high time compression.
        /// </summary>
        public bool ConsiderMainEffortShift(int decisiveSectorId, float decisiveSectorOdds, float currentMainEffortOdds)
        {
            if (!CanMoveMainEffortTo(decisiveSectorId)) return false;
            // Hysteresis: only shift if candidate is materially better.
            // If currentMainEffortOdds <= 0 (no contact in current sector),
            // any positive candidate wins — there's nothing to lose.
            if (currentMainEffortOdds > 0f)
            {
                float requiredOdds = currentMainEffortOdds * (1f + MainEffortShiftMarginRatio);
                if (decisiveSectorOdds < requiredOdds) return false;
            }
            _plan = _plan.WithMainEffortSector(decisiveSectorId);
            return true;
        }

        // The main effort may move to the band: there is a plan, the band exists and is not the current one, the plan is not a FocalAssault (its main
        // effort is the focal point's band, set at its pick) and not in Consolidate or Withdraw. Shared by the odds-based shift and the objective's.
        private bool CanMoveMainEffortTo(int sector)
        {
            if (!HasPlan || sector < 0) return false;
            if (_plan.PlanId == BattlePlanId.FocalAssault) return false;
            if (_plan.MainEffortSector == sector) return false;
            return _plan.Phase != BattlePhase.Consolidate && _plan.Phase != BattlePhase.Withdraw;
        }

        /// <summary>
        /// BC14O: the plan's main effort follows the main-effort objective (requirement-main-effort-objective). Refused in Consolidate and Withdraw and
        /// for a FocalAssault plan, as the odds-based shift is; no hysteresis of its own, because the objective's latch carries the margin.
        /// </summary>
        public bool SetObjectiveMainEffort(int sector)
        {
            if (!CanMoveMainEffortTo(sector)) return false;
            _plan = _plan.WithMainEffortSector(sector);
            return true;
        }

        /// <summary>
        /// BC14O: the front-axis band of the latched main-effort objective, false when the doctrine is off, nothing is latched or the army has no axis.
        /// </summary>
        internal bool TryObjectiveSector(out int sector)
        {
            sector = -1;
            if (_feasibility == null || !_frontAxis.IsValid || !Plugin.ObjectiveDoctrineEnabled()) return false;
            ObjectiveLatch latch = ObjectiveLatch.ForAlliance(AllianceId);
            if (latch == null || !latch.Has) return false;
            sector = _frontAxis.SectorOf(latch.X, latch.Z);
            return sector >= 0;
        }

        /// <summary>
        /// Per-tick phase progression evaluator. Runtime calls this after
        /// updating evidence; the doctrine decides whether to advance
        /// (Probe -> MainEffort -> Exploit -> Consolidate -> Withdraw)
        /// based on plan age, global/main-effort odds, morale, and reserves.
        /// Returns the decision so the runtime can emit telemetry; the
        /// orchestrator also applies the recommended advance internally.
        /// </summary>
        public TacticalPhaseProgressionDoctrine.Decision EvaluateAndAdvancePhase(
            float globalOddsCurrent,
            float mainEffortOddsCurrent,
            float mainEffortOddsHistory,
            float armyMoraleCurrent,
            float armyMoraleFloor,
            float reservesCommittedFraction,
            TacticalSectorReadinessDoctrine.Result mainEffortReadiness = TacticalSectorReadinessDoctrine.Result.PushReady,
            float intelConfidence01 = 1f)
        {
            if (!HasPlan)
                return new TacticalPhaseProgressionDoctrine.Decision(BattlePhase.Probe, "no-plan");

            float aggression01 = (_commanderPersonality.Aggression + 1f) * 0.5f;
            if (aggression01 < 0f) aggression01 = 0f;
            if (aggression01 > 1f) aggression01 = 1f;

            var input = new TacticalPhaseProgressionDoctrine.Input(
                currentPhase: _plan.Phase,
                planAgeSeconds: _phaseAgeSeconds,
                globalOddsCurrent: globalOddsCurrent,
                globalOddsHistory: _historyGlobalOdds,
                mainEffortOddsCurrent: mainEffortOddsCurrent,
                mainEffortOddsHistory: mainEffortOddsHistory,
                armyMoraleCurrent: armyMoraleCurrent,
                armyMoraleFloor: armyMoraleFloor,
                reservesCommittedFraction: reservesCommittedFraction,
                commanderAggression01: aggression01,
                mainEffortReadiness: mainEffortReadiness,
                intelConfidence01: intelConfidence01,
                worksHold: WorksHoldOn);

            var decision = TacticalPhaseProgressionDoctrine.Decide(input);
            if (decision.NextPhase != _plan.Phase)
            {
                AdvancePhase(decision.NextPhase);
            }
            return decision;
        }

        public ReplanTrigger CheckReplanTriggers(ReplanTriggerInput input) => ArmyReplanTriggers.Evaluate(input);

        public void ObserveIntent(TacticalIntentModel enemyIntent)
        {
            _currentIntentModel = enemyIntent;
        }

        public void Replan(ArmyEvidence evidence)
        {
            Replan(evidence, UnknownIntentModel());
        }

        public void Replan(ArmyEvidence evidence, TacticalIntentModel enemyIntent) => Replan(evidence, enemyIntent, hysteresis: false);

        public void Replan(ArmyEvidence evidence, TacticalIntentModel enemyIntent, string trigger) => Replan(evidence, enemyIntent, hysteresis: false, trigger);

        /// <summary>
        /// Re-picks the plan. With <paramref name="hysteresis"/> (BC14's broken maneuver assumption) the catalog keeps the current plan unless
        /// it is gone, impossible or beaten by <see cref="Plays.ManeuverFeasibility.ReplanScoreMargin"/>; a kept plan keeps its side, phase and
        /// age and only refreshes the baselines the edge triggers compare with. Returns true when the plan was kept.
        /// </summary>
        public bool Replan(ArmyEvidence evidence, TacticalIntentModel enemyIntent, bool hysteresis, string trigger = null)
        {
            if (!TryPickPlan(evidence, OpposingCommanderHintFromIntent(enemyIntent), hysteresis, trigger ?? "replan", out var nextPlan))
            {
                return false;
            }

            if (hysteresis && HasPlan && nextPlan.PlanId == _plan.PlanId)
            {
                _currentIntentModel = enemyIntent;
                _historyGlobalOdds = evidence.CurrentOdds;
                _lastReplanOpportunity = _currentReinforcementOpportunity.Opportunity;
                _feasibility?.CaptureSelection(_plan.PlanId, _plan.Mirrored);
                return true;
            }

            // A replan re-picks the playbook; it does not throw away where the
            // battle is. Every playbook instantiates in Probe at the default
            // sector, so without this a defensive replan reset Withdraw/
            // Consolidate to Probe and the main effort to the default band
            // (Withdraw -> Probe -> Withdraw loop, review T7).
            bool hadPlan = HasPlan;
            if (hadPlan)
            {
                nextPlan = nextPlan.WithPhase(_plan.Phase).WithMainEffortSector(_plan.MainEffortSector);
            }
            _plan = nextPlan;
            HasPlan = true;
            // BC14O: a new plan starts the main-effort objective's hysteresis over.
            ObjectiveLatch.ForAlliance(AllianceId)?.Clear();
            // BC13: a re-picked plan starts the staggered release over (released reserves revert as the allocator reruns).
            ReserveRelease.Clear();
            _armyReleaseHysteresis.Clear();
            _leafRelease.Clear();
            _currentIntentModel = enemyIntent;
            _historyGlobalOdds = evidence.CurrentOdds;
            _planAgeSeconds = 0f;
            if (!hadPlan) _phaseAgeSeconds = 0f;
            // Opportunity-change trigger (2026-05-21). Snapshot the opportunity at
            // each replan so ArmyReplanTriggers.Evaluate can detect transitions
            // without firing the trigger every cycle. Next cycle's input compares
            // CurrentOpportunity to LastReplanOpportunity; they match until the
            // doctrine returns a different category, at which point the trigger
            // fires and ArmyTickCycle invokes Replan again (this snapshot is
            // updated, breaking the loop).
            _lastReplanOpportunity = _currentReinforcementOpportunity.Opportunity;
            _feasibility?.CaptureSelection(_plan.PlanId, _plan.Mirrored);
            return false;
        }

        // Opportunity-change trigger state (2026-05-21). Snapshotted in Replan()
        // and read by ReplanTriggerInput so the trigger fires once per category
        // transition, not every cycle while the opportunity sits in the same
        // category.
        private ReinforcementOpportunity _lastReplanOpportunity = ReinforcementOpportunity.NoOpportunity;
        public ReinforcementOpportunity LastReplanOpportunity => _lastReplanOpportunity;

        private static TacticalIntentModel UnknownIntentModel() =>
            new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, null);

        private static float OpposingCommanderHintFromIntent(TacticalIntentModel m)
        {
            float baseBias;
            switch (m.PrimaryIntent)
            {
                case InferredIntent.Defend:
                case InferredIntent.Refuse:
                case InferredIntent.Withdraw:
                    baseBias = 0.6f;
                    break;
                case InferredIntent.Attack:
                    baseBias = 0.2f;
                    break;
                case InferredIntent.Probe:
                    baseBias = 0.4f;
                    break;
                case InferredIntent.Unknown:
                default:
                    baseBias = 0f;
                    break;
            }

            return baseBias * m.Confidence01;
        }

        private DirectChildSnapshot[] _directChildSnapshots = Array.Empty<DirectChildSnapshot>();
        private DirectChildEvidence[] _directChildEvidenceCache = Array.Empty<DirectChildEvidence>();
        private IReadOnlyList<DirectChildIntent> _directChildIntents = Array.Empty<DirectChildIntent>();
        private CommandTreeSnapshot _commandTree = CommandTreeSnapshot.Empty;
        private IReadOnlyList<CommandNodeIntent> _commandNodeIntents = Array.Empty<CommandNodeIntent>();
        private TacticalCommanderMode _commanderMode = TacticalCommanderMode.Off;
        private IReadOnlyList<CommandNodeOperationalState> _currentCommandOperations = Array.Empty<CommandNodeOperationalState>();
        private IReadOnlyList<CommandDoctrineOrder> _currentDoctrineOrders = Array.Empty<CommandDoctrineOrder>();
        private OperationRecord _currentOperation = new OperationRecord(
            TacticalOperationShape.SingleMainEffort,
            TacticalOperationPhase.Planning,
            "objective-unknown",
            0f);
        private StrategicBattleIntentSnapshot _currentStrategicBattleIntent = StrategicBattleIntentSnapshot.Empty;

        public IReadOnlyList<DirectChildIntent> CurrentDirectChildIntents => _directChildIntents;
        public TacticalCommanderMode CommanderMode => _commanderMode;
        public IReadOnlyList<CommandNodeOperationalState> CurrentCommandOperations => _currentCommandOperations;
        public IReadOnlyList<CommandDoctrineOrder> CurrentDoctrineOrders => _currentDoctrineOrders;
        public OperationRecord CurrentOperation => _currentOperation;
        public StrategicBattleIntentSnapshot CurrentStrategicBattleIntent => _currentStrategicBattleIntent;
        internal CommandTreeSnapshot CurrentCommandTree => _commandTree;
        internal IReadOnlyList<CommandNodeIntent> CurrentCommandNodeIntents => _commandNodeIntents;

        public void RegisterDirectChildren(IReadOnlyList<DirectChildSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                _directChildSnapshots = Array.Empty<DirectChildSnapshot>();
                _directChildEvidenceCache = Array.Empty<DirectChildEvidence>();
                _directChildIntents = Array.Empty<DirectChildIntent>();
                _commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);
                return;
            }

            _directChildSnapshots = new DirectChildSnapshot[snapshots.Count];
            for (int i = 0; i < snapshots.Count; i++) _directChildSnapshots[i] = snapshots[i];
            _directChildEvidenceCache = new DirectChildEvidence[snapshots.Count];
            // Initial intent list mirrors snapshot count with Unknown roles so callers
            // can iterate before any evidence has arrived.
            var initial = new DirectChildIntent[snapshots.Count];
            var unknownEnemy = new TacticalIntentModel(InferredIntent.Unknown, -1, 0f, 0f, Array.Empty<EvidenceTag>());
            for (int i = 0; i < snapshots.Count; i++)
            {
                var s = snapshots[i];
                initial[i] = new DirectChildIntent(
                    s.ChildId, s.RawUnitTyp, s.EffectiveCommandLevel, s.DisplayName,
                    primarySector: 0, role: DirectChildRole.Unknown,
                    axis: DirectChildAxis.None, axisSector: 0,
                    supportPriority01: 0f,
                    enemyIntent: unknownEnemy);
            }
            _directChildIntents = initial;
            _commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);
        }

        public bool RegisterDirectChildrenIfChanged(IReadOnlyList<DirectChildSnapshot> snapshots)
        {
            if (SnapshotsEqual(_directChildSnapshots, snapshots)) return false;
            RegisterDirectChildren(snapshots);
            return true;
        }

        private static bool SnapshotsEqual(DirectChildSnapshot[] current, IReadOnlyList<DirectChildSnapshot> next)
        {
            int currentCount = current == null ? 0 : current.Length;
            int nextCount = next == null ? 0 : next.Count;
            if (currentCount != nextCount) return false;
            for (int i = 0; i < currentCount; i++)
            {
                if (!SnapshotEqual(current[i], next[i])) return false;
            }

            return true;
        }

        private static bool SnapshotEqual(DirectChildSnapshot a, DirectChildSnapshot b)
        {
            return string.Equals(a.ChildId, b.ChildId, StringComparison.Ordinal) &&
                string.Equals(a.ParentArmyId, b.ParentArmyId, StringComparison.Ordinal) &&
                a.RawUnitTyp == b.RawUnitTyp &&
                a.CommandHierarchyShift == b.CommandHierarchyShift &&
                string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal) &&
                a.Active == b.Active;
        }

        internal void RegisterCommandTree(CommandTreeSnapshot tree)
        {
            _commandTree = tree ?? CommandTreeSnapshot.Empty;
            _commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);
        }

        internal CommandIntentResolution ResolveCommandIntentForGroup(int regimentInstanceId)
        {
            return CommandIntentResolver.ResolveForInstance(regimentInstanceId, _commandNodeIntents, _directChildIntents);
        }

        internal CommandIntentResolution ResolveCommandIntentForGroup(int regimentInstanceId, int gameObjectInstanceId)
        {
            return CommandIntentResolver.ResolveForInstance(
                regimentInstanceId,
                gameObjectInstanceId,
                _commandNodeIntents,
                _directChildIntents);
        }

        /// <summary>
        /// Zone-aware observation (Stage 2 / G geometric play layer). When
        /// <paramref name="zoneAssignments"/> is non-empty, the allocator
        /// gives those children roles based on rect kind instead of evidence.
        /// </summary>
        public void ObserveDirectChildEvidenceWithZones(
            IReadOnlyList<DirectChildEvidence> evidence,
            IReadOnlyList<TacticalIntentModel> perChildEnemyIntent,
            IReadOnlyList<WhiskeyRealism.Tactical.Orchestrator.Plays.CorpsAssignment> zoneAssignments)
        {
            if (!HasPlan) return;
            if (evidence == null || evidence.Count != _directChildSnapshots.Length) return;
            // Allocates every call: main-role hysteresis counts consecutive
            // cycles, and the reserve-release context (global odds) and
            // per-child enemy intent are not part of the evidence signature.
            for (int i = 0; i < evidence.Count; i++) _directChildEvidenceCache[i] = evidence[i];
            IReadOnlyList<DirectChildIntent> previousIntents = _directChildIntents;
            _directChildIntents = DirectChildAllocator.StabilizeMain(
                DirectChildAllocator.AllocateWithZones(
                    _plan, _commanderPersonality, _directChildSnapshots, _directChildEvidenceCache,
                    perChildEnemyIntent, zoneAssignments, BuildReserveReleaseContext()),
                _mainHysteresis);
            ScreenSecondaryObjectives();
            _commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);
            EmitFlankReserveReleaseTelemetry(previousIntents, _directChildIntents);
            EmitStaggeredReleaseTelemetry();
            EmitDirectChildIntentTelemetry();
        }

        // ---- BC14O economy of force ----

        private readonly SecondaryObjective[] _secondaries = new SecondaryObjective[ObjectiveScoring.MaxSecondaryScreens];
        private bool[] _playerOwnedChildren = Array.Empty<bool>();

        /// <summary>BC14O: the secondary objectives the last allocation drew a screen for (the TacticalMainEffort row's <c>secondaryScreens</c>).</summary>
        internal int SecondaryScreens
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _directChildIntents.Count; i++) if (_directChildIntents[i].HasScreenTarget) n++;
                return n;
            }
        }

        // The ledger's secondary objectives (every feasible enemy-held or threatened objective but the main effort, best score first) each draw a
        // Screen-role child the play already holds, at most MaxSecondaryScreens of them.
        private void ScreenSecondaryObjectives()
        {
            if (_feasibility == null || !HasPlan || !_frontAxis.IsValid || !Plugin.ObjectiveDoctrineEnabled()) return;
            ObjectiveLatch latch = ObjectiveLatch.ForAlliance(AllianceId);
            int count = ObjectiveScoring.Secondaries(_feasibility, latch?.Id, _secondaries);
            if (count == 0) return;
            if (_playerOwnedChildren.Length < _directChildIntents.Count) _playerOwnedChildren = new bool[_directChildIntents.Count];
            for (int i = 0; i < _directChildIntents.Count; i++) _playerOwnedChildren[i] = _feasibility.HoldsPlayerOwned(_directChildIntents[i].ChildId);
            _directChildIntents = DirectChildAllocator.ScreenSecondaryObjectives(
                _directChildIntents, _secondaries, count, _frontAxis, _plan.PlanId, _playerOwnedChildren);
        }

        // ---- BC13 staggered reserve release ----

        /// <summary>The alliance's reserve-release memory: the crisis releases (the exploit lifecycle reads them), the stagger state and the crossing bindings. One instance per alliance, reset with the battle.</summary>
        internal ReserveReleaseState ReserveRelease => ReserveReleaseState.ForAlliance(AllianceId);

        // The allocator's release context: the flank release flag as before, and with Enable Staggered Reserve Release the state, the
        // children with a live exploit (never released) and the army commander's temperament (the interval and the floor share).
        //
        // BC14: with Enable Maneuver Feasibility the context carries the state, the exploit flags and the commander's temperament whether or not
        // the stagger is on (the crossing release needs the state's bindings and the floor share), and the Defender's crossing threats from the
        // ledger. The threat buffers are pooled here: grow-only, filled in place each allocation.
        private readonly Plays.CrossingThreat[] _threatBuffer = new Plays.CrossingThreat[Plays.ManeuverFeasibility.MaxThreatCrossings];
        private float[] _threatShiftBuffer = new float[Plays.ManeuverFeasibility.MaxThreatCrossings * Plays.ManeuverFeasibility.MaxChildren];
        private readonly float[] _threatRow = new float[Plays.ManeuverFeasibility.MaxChildren];
        // The threats the last allocation was handed: the release row names the one a guard answers.
        private int _lastThreatCount;
        // BC14W: the Defender's works station and each allocation child's time to it, in the allocation's child order.
        private float[] _stationShiftBuffer = new float[Plays.ManeuverFeasibility.MaxChildren];
        private readonly float[] _stationRow = new float[Plays.ManeuverFeasibility.MaxChildren];

        // The army-level release and return damped as the leaf one is (verify r2: the crisis release is stateless and flipped with the picture).
        private readonly ReleaseHysteresis _armyReleaseHysteresis = new ReleaseHysteresis();

        /// <summary>The doctrine clock the releases and their hysteresis read (a test installs its own).</summary>
        internal Func<double> HoursSource = () => TacticalDoctrineTime.NowBattleHours;

        private ReserveReleaseContext BuildReserveReleaseContext()
        {
            bool flank = Plugin.FlankResponseEnabled();
            bool staggered = Plugin.StaggeredReserveReleaseEnabled();
            bool feasible = Plugin.ManeuverFeasibilityEnabled();
            if (!staggered && !feasible) return new ReserveReleaseContext(_lastGlobalOdds, flank);
            var exploits = WhiskeyRealism.Tactical.Battle.ExploitTable.ForAlliance(AllianceId);
            var exploiting = new bool[_directChildSnapshots.Length];
            for (int i = 0; i < exploiting.Length; i++)
            {
                int instanceId = ChildInstanceId(_directChildSnapshots[i].ChildId);
                exploiting[i] = instanceId != 0 && exploits.IsExploiting(instanceId);
            }
            int threats = 0, threatChildren = 0;
            _lastThreatCount = 0;
            if (feasible && _feasibility != null)
                threats = FillThreatMatrix(_feasibility, _directChildSnapshots, _threatBuffer, _threatShiftBuffer, _threatRow, out threatChildren);
            _lastThreatCount = threats;
            Plays.StationPoint station = default(Plays.StationPoint);
            int stationChildren = 0;
            if (feasible && _feasibility != null && Plugin.EnemyWorksDoctrineEnabled())
                FillStation(_feasibility, _directChildSnapshots, _stationShiftBuffer, _stationRow, out station, out stationChildren);
            return new ReserveReleaseContext(
                _lastGlobalOdds, flank, ReserveRelease, exploiting, HoursSource(),
                WhiskeyRealism.Tactical.Battle.CommanderPersonalityDerivation.To01(_commanderPersonality.Aggression),
                WhiskeyRealism.Tactical.Battle.CommanderPersonalityDerivation.To01(_commanderPersonality.Caution),
                WhiskeyRealism.Tactical.Battle.CommanderPersonalityDerivation.To01(_commanderPersonality.CasualtyTolerance),
                staggered, _threatBuffer, threats, _threatShiftBuffer, threatChildren, _armyReleaseHysteresis, station, _stationShiftBuffer, stationChildren);
        }

        /// <summary>
        /// The Defender's works station and each allocation child's time to it from the ledger into the pooled buffer, by child id as the threat matrix
        /// is: a child the ledger did not time (another child list, one that arrived since) gets NaN, which the allocator reads as never stationed.
        /// With no station, or more children than the ledger's cap, the station is none.
        /// </summary>
        internal static void FillStation(Plays.IFeasibilityLookup ledger, IReadOnlyList<DirectChildSnapshot> snapshots, float[] shiftBuffer, float[] row,
            out Plays.StationPoint station, out int allocationChildren)
        {
            allocationChildren = snapshots.Count;
            if (allocationChildren > Plays.ManeuverFeasibility.MaxChildren || !ledger.TryStation(out station, row, out int children) || !station.Has)
            {
                station = default(Plays.StationPoint);
                allocationChildren = 0;
                return;
            }
            for (int c = 0; c < allocationChildren; c++)
            {
                int column = -1;
                for (int k = 0; k < children; k++)
                    if (string.Equals(ledger.StationChildId(k), snapshots[c].ChildId, StringComparison.Ordinal)) { column = k; break; }
                shiftBuffer[c] = column >= 0 ? row[column] : float.NaN;
            }
        }

        /// <summary>
        /// The Defender's threats and the time matrix (threat-major, one column per allocation child) from the ledger into the pooled buffers.
        /// The ledger's matrix is keyed by child id: each of the allocation's children takes the column of its own id, and a child the ledger did
        /// not time (another child list, a child that arrived since) gets NaN, which the allocator reads as never releasable or bound. Returns the
        /// threat count (0 when the allocation has more children than the ledger's cap).
        /// </summary>
        internal static int FillThreatMatrix(Plays.IFeasibilityLookup ledger, IReadOnlyList<DirectChildSnapshot> snapshots, Plays.CrossingThreat[] threatBuffer,
            float[] shiftBuffer, float[] row, out int allocationChildren)
        {
            allocationChildren = snapshots.Count;
            if (allocationChildren > Plays.ManeuverFeasibility.MaxChildren) { allocationChildren = 0; return 0; }
            int threats = 0;
            int count = Math.Min(ledger.ThreatCount, threatBuffer.Length);
            for (int t = 0; t < count; t++)
            {
                if (!ledger.TryThreat(t, out threatBuffer[threats], row, out int children)) continue;
                for (int c = 0; c < allocationChildren; c++)
                {
                    int column = -1;
                    for (int k = 0; k < children; k++)
                        if (string.Equals(ledger.ThreatChildId(k), snapshots[c].ChildId, StringComparison.Ordinal)) { column = k; break; }
                    shiftBuffer[threats * allocationChildren + c] = column >= 0 ? row[column] : float.NaN;
                }
                threats++;
            }
            return threats;
        }

        // Child ids read "child-" + the instance id.
        internal static int ChildInstanceId(string childId) => DirectChildIds.InstanceId(childId);

        // One TacticalReserveRelease row per stagger or crisis release, throttled per child and reason (a crisis release is recomputed with every
        // allocation and used to write a row each tick). The table is battle-scoped.
        private static readonly System.Collections.Generic.Dictionary<string, float> _lastReleaseRowAt =
            new System.Collections.Generic.Dictionary<string, float>();
        private const float ReleaseRowSeconds = 30f;

        private static bool ReleaseRowDue(int side, string childId, string reason)
            => WhiskeyRealism.Tactical.TacticalTelemetry.ShouldEmit(_lastReleaseRowAt, "tactical-reserve-release:" + side + ":" + childId, reason,
                System.Environment.TickCount * 0.001f, ReleaseRowSeconds, false);

        private void EmitStaggeredReleaseTelemetry()
        {
            try
            {
                var outcomes = ReserveRelease.Outcomes;
                int released = ReserveRelease.ReleasedCount;
                for (int i = 0; i < outcomes.Count; i++)
                {
                    ReserveReleaseState.Outcome o = outcomes[i];
                    double now = TacticalDoctrineTime.NowBattleHours;
                    string reason = o.Crisis ? "crisis" : "stagger";
                    if (!ReleaseRowDue(AllianceId, o.ChildId, reason)) continue;
                    WhiskeyRealism.Telemetry.TelemetryRouter.Emit(
                        WhiskeyRealism.Telemetry.TelemetryLayer.Tactical,
                        WhiskeyRealism.Telemetry.TelemetryCategory.Decision,
                        "TacticalReserveRelease",
                        WhiskeyRealism.Telemetry.TelemetrySeverity.Info,
                        ev => ev
                            .WithDecision("TacticalReserveRelease", reason, "side=" + AllianceId + "|child=" + o.ChildId)
                            .WithField("side", AllianceId)
                            .WithField("child", o.ChildId)
                            .WithField("reason", reason)
                            .WithField("held", o.Held)
                            .WithField("floor", o.Floor)
                            .WithField("floorBreach", o.FloorBreach ? "crisis" : "none")
                            .WithField("released", released)
                            .WithField("nowHours", now));
                }
            }
            catch (System.Exception ex)
            {
                try { Plugin.Log?.LogWarning("[TacticalReserveRelease] stagger emit failed: " + ex.Message); } catch { }
            }
        }

        // BC11 decision 3: one TacticalReserveRelease row when a Reserve child becomes the guard of a threatened
        // wing (a state change, so it is bounded by the role flips themselves).
        private void EmitFlankReserveReleaseTelemetry(IReadOnlyList<DirectChildIntent> previous, IReadOnlyList<DirectChildIntent> current)
        {
            if (previous == null || current == null) return;
            try
            {
                for (int i = 0; i < current.Count; i++)
                {
                    DirectChildRole role = current[i].Role;
                    if (role != DirectChildRole.RefuseLeft && role != DirectChildRole.RefuseRight) continue;
                    int before = IndexOfChild(previous, current[i].ChildId);
                    if (before < 0 || previous[before].Role != DirectChildRole.Reserve) continue;
                    DirectChildIntent released = current[i];
                    // A reserve sent to guard a crossing carries a guard point; BC11's wing guard does not.
                    string reason = released.HasGuardPoint ? "crossing-threat" : "flank-threat";
                    // The crossing release names the crossing, the guard's place and the two times it was decided on (requirement-telemetry).
                    int crossing = -1;
                    double ownShift = 0.0, enemyArrival = 0.0;
                    int held = 0, floor = 0;
                    if (released.HasGuardPoint && ReserveRelease.TryCrossingOf(released.ChildId, out CrossingBinding binding))
                    {
                        crossing = binding.Link;
                        ownShift = binding.OwnShiftAtRelease;
                        held = binding.Held;
                        floor = binding.Floor;
                        for (int t = 0; t < _lastThreatCount; t++)
                            if (_threatBuffer[t].Link == binding.Link) enemyArrival = _threatBuffer[t].EnemyAcross ? 0.0 : _threatBuffer[t].EnemyAtCrossing;
                    }
                    float guardX = released.Guard.X, guardZ = released.Guard.Z, guardHeading = released.Guard.Heading;
                    WhiskeyRealism.Telemetry.TelemetryRouter.Emit(
                        WhiskeyRealism.Telemetry.TelemetryLayer.Tactical,
                        WhiskeyRealism.Telemetry.TelemetryCategory.Decision,
                        "TacticalReserveRelease",
                        WhiskeyRealism.Telemetry.TelemetrySeverity.Info,
                        ev => ev
                            .WithDecision("TacticalReserveRelease", reason, "side=" + AllianceId + "|child=" + released.ChildId + "|role=" + released.Role)
                            .WithField("side", AllianceId)
                            .WithField("child", released.ChildId)
                            .WithField("role", released.Role.ToString())
                            .WithField("wingSector", released.AxisSector)
                            .WithField("reason", reason)
                            .WithField("crossing", crossing)
                            .WithField("guardX", guardX)
                            .WithField("guardZ", guardZ)
                            .WithField("guardHeading", guardHeading)
                            .WithField("ownShiftHours", ownShift)
                            .WithField("enemyArrivalHours", enemyArrival)
                            .WithField("held", held)
                            .WithField("floor", floor)
                            .WithField("reverted", false));
                }
                // A guard that stood last allocation and does not now: the threat stopped (or the binding ended), the child is a reserve again.
                for (int i = 0; i < current.Count; i++)
                {
                    int before = IndexOfChild(previous, current[i].ChildId);
                    if (before < 0 || !previous[before].HasGuardPoint || current[i].HasGuardPoint) continue;
                    string childId = current[i].ChildId;
                    // BC14W: a guard point on a Reserve child was the works station, not a crossing guard.
                    string revertedReason = previous[before].Role == DirectChildRole.Reserve ? "works-station" : "crossing-threat";
                    WhiskeyRealism.Telemetry.TelemetryRouter.Emit(
                        WhiskeyRealism.Telemetry.TelemetryLayer.Tactical,
                        WhiskeyRealism.Telemetry.TelemetryCategory.Decision,
                        "TacticalReserveRelease",
                        WhiskeyRealism.Telemetry.TelemetrySeverity.Info,
                        ev => ev
                            .WithDecision("TacticalReserveRelease", revertedReason, "side=" + AllianceId + "|child=" + childId + "|reverted")
                            .WithField("side", AllianceId)
                            .WithField("child", childId)
                            .WithField("reason", revertedReason)
                            .WithField("reverted", true));
                }
                // BC14W: a Reserve child that has just been stationed behind a focal point of the Defender's works (a state change, so bounded).
                for (int i = 0; i < current.Count; i++)
                {
                    if (current[i].Role != DirectChildRole.Reserve || !current[i].HasGuardPoint) continue;
                    int before = IndexOfChild(previous, current[i].ChildId);
                    if (before >= 0 && previous[before].Role == DirectChildRole.Reserve && previous[before].HasGuardPoint) continue;
                    DirectChildIntent stationed = current[i];
                    StationBinding station = ReserveRelease.Station;
                    WhiskeyRealism.Telemetry.TelemetryRouter.Emit(
                        WhiskeyRealism.Telemetry.TelemetryLayer.Tactical,
                        WhiskeyRealism.Telemetry.TelemetryCategory.Decision,
                        "TacticalReserveRelease",
                        WhiskeyRealism.Telemetry.TelemetrySeverity.Info,
                        ev => ev
                            .WithDecision("TacticalReserveRelease", "works-station", "side=" + AllianceId + "|child=" + stationed.ChildId)
                            .WithField("side", AllianceId)
                            .WithField("child", stationed.ChildId)
                            .WithField("reason", "works-station")
                            .WithField("focalX", station.FocalX)
                            .WithField("focalZ", station.FocalZ)
                            .WithField("guardX", stationed.Guard.X)
                            .WithField("guardZ", stationed.Guard.Z)
                            .WithField("guardHeading", stationed.Guard.Heading)
                            .WithField("enemyArrivalHours", station.ArrivalAtChoice)
                            .WithField("reverted", false));
                }
            }
            catch (System.Exception ex)
            {
                try { Plugin.Log?.LogWarning("[TacticalReserveRelease] emit failed: " + ex.Message); } catch { }
            }
        }

        private static int IndexOfChild(IReadOnlyList<DirectChildIntent> intents, string childId)
        {
            for (int i = 0; i < intents.Count; i++)
                if (string.Equals(intents[i].ChildId, childId, System.StringComparison.Ordinal)) return i;
            return -1;
        }

        // ---- Role-distribution telemetry (2026-05-22 — D from "Fix it all correctly") ----
        //
        // Emits a single TacticalDirectChildIntent telemetry event whenever the
        // role distribution changes. Bounded by a per-side signature dedup +
        // minimum interval so it doesn't flood the log. This gives us visibility
        // into Main/SupportMain/Refuse/Reserve/Fallback distribution per side
        // that was previously missing — the role-budget allocator could be
        // diagnosed in code only, never in smoke.
        private string _lastDirectChildIntentTelemetrySig = string.Empty;
        private static readonly System.Collections.Generic.Dictionary<string, float> _lastDirectChildIntentTelemetryAt =
            new System.Collections.Generic.Dictionary<string, float>();
        private const float DirectChildIntentTelemetrySeconds = 5f;

        private void EmitDirectChildIntentTelemetry()
        {
            if (_directChildIntents == null || _directChildIntents.Count == 0) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                int mainCount = 0;
                int supportMainCount = 0;
                int refuseLeftCount = 0;
                int refuseRightCount = 0;
                int fixCount = 0;
                int screenCount = 0;
                int reserveCount = 0;
                int fallbackCount = 0;
                int unknownCount = 0;
                for (int i = 0; i < _directChildIntents.Count; i++)
                {
                    var intent = _directChildIntents[i];
                    if (i > 0) sb.Append(',');
                    sb.Append(intent.ChildId ?? "?").Append('=').Append(intent.Role).Append(':').Append(intent.PrimarySector);
                    switch (intent.Role)
                    {
                        case DirectChildRole.Main:        mainCount++; break;
                        case DirectChildRole.SupportMain: supportMainCount++; break;
                        case DirectChildRole.Fix:         fixCount++; break;
                        case DirectChildRole.Screen:      screenCount++; break;
                        case DirectChildRole.RefuseLeft:  refuseLeftCount++; break;
                        case DirectChildRole.RefuseRight: refuseRightCount++; break;
                        case DirectChildRole.Reserve:     reserveCount++; break;
                        case DirectChildRole.Fallback:    fallbackCount++; break;
                        case DirectChildRole.Unknown:     unknownCount++; break;
                    }
                }
                string roleSummary = "main=" + mainCount
                    + " sup=" + supportMainCount
                    + " fix=" + fixCount
                    + " scrn=" + screenCount
                    + " rL=" + refuseLeftCount
                    + " rR=" + refuseRightCount
                    + " rsrv=" + reserveCount
                    + " fbck=" + fallbackCount
                    + " unk=" + unknownCount;
                string sig = "side=" + AllianceId + "|" + roleSummary;
                if (string.Equals(sig, _lastDirectChildIntentTelemetrySig, System.StringComparison.Ordinal))
                    return;
                _lastDirectChildIntentTelemetrySig = sig;

                float nowSeconds = System.Environment.TickCount * 0.001f;
                string key = "tactical-direct-child-intent:" + AllianceId;
                if (!WhiskeyRealism.Tactical.TacticalTelemetry.ShouldEmit(
                        _lastDirectChildIntentTelemetryAt, key, sig, nowSeconds,
                        DirectChildIntentTelemetrySeconds, false))
                    return;

                string detail = sb.ToString();
                string opportunity = _currentReinforcementOpportunity.Opportunity.ToString();
                float currentRatio = _currentReinforcementOpportunity.CurrentRatio;
                int mainEffortSector = HasPlan ? _plan.MainEffortSector : -1;
                string planId = HasPlan ? _plan.PlanId.ToString() : "<none>";
                string phase = HasPlan ? _plan.Phase.ToString() : "<none>";
                WhiskeyRealism.Telemetry.TelemetryRouter.Emit(
                    WhiskeyRealism.Telemetry.TelemetryLayer.Tactical,
                    WhiskeyRealism.Telemetry.TelemetryCategory.Decision,
                    "TacticalDirectChildIntent",
                    WhiskeyRealism.Telemetry.TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision("TacticalDirectChildIntent", "summary", sig)
                        .WithField("side", AllianceId)
                        .WithField("plan", planId)
                        .WithField("phase", phase)
                        .WithField("mainEffortSector", mainEffortSector)
                        .WithField("opportunity", opportunity)
                        .WithField("currentRatio", currentRatio)
                        .WithField("mainCount", mainCount)
                        .WithField("supportMainCount", supportMainCount)
                        .WithField("fixCount", fixCount)
                        .WithField("screenCount", screenCount)
                        .WithField("refuseLeftCount", refuseLeftCount)
                        .WithField("refuseRightCount", refuseRightCount)
                        .WithField("reserveCount", reserveCount)
                        .WithField("fallbackCount", fallbackCount)
                        .WithField("unknownCount", unknownCount)
                        .WithField("brigades", detail));
            }
            catch (System.Exception ex)
            {
                try { Plugin.Log?.LogWarning("[TacticalDirectChildIntent] emit failed: " + ex.Message); } catch { }
            }
        }

        // One row per change of the Defender's reserve split (bounded by the signature and an interval): the numbers each holding
        // command had, what it holds back and why, so thin numbers are on the trace and not a silent absence.
        private string _lastDefenderReserveSig = string.Empty;
        private static readonly System.Collections.Generic.Dictionary<string, float> _lastDefenderReserveAt =
            new System.Collections.Generic.Dictionary<string, float>();
        private const float DefenderReserveTelemetrySeconds = 5f;

        // The throttle keys carry the trace signature, so the table grows with the battle: it starts empty with the next one.
        static ArmyOrchestrator()
        {
            WhiskeyRealism.Util.BattleScope.Register("army-orchestrator-defender-reserve-throttle", () => { _lastDefenderReserveAt.Clear(); _lastReleaseRowAt.Clear(); });
        }

        private void EmitDefenderReserveTelemetry()
        {
            if (_defenderReserveReport.Count == 0) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                int held = 0, thin = 0;
                for (int i = 0; i < _defenderReserveReport.Count; i++)
                {
                    var r = _defenderReserveReport[i];
                    if (i > 0) sb.Append(',');
                    sb.Append(r.ParentName).Append('#').Append(r.ParentInstanceId).Append('=').Append(r.Held).Append('/').Append(r.Children).Append(':').Append(r.Reason);
                    held += r.Held;
                    if (r.Reason != DefenderReserveRecord.Reserved && r.Reason != DefenderReserveRecord.AlreadyHeld) thin++;
                }
                string sig = "side=" + AllianceId + "|" + sb;
                if (string.Equals(sig, _lastDefenderReserveSig, System.StringComparison.Ordinal)) return;
                _lastDefenderReserveSig = sig;
                if (!WhiskeyRealism.Tactical.TacticalTelemetry.ShouldEmit(_lastDefenderReserveAt, "tactical-defender-reserve:" + AllianceId, sig,
                        System.Environment.TickCount * 0.001f, DefenderReserveTelemetrySeconds, false))
                    return;
                string detail = sb.ToString();
                int commands = _defenderReserveReport.Count;
                WhiskeyRealism.Telemetry.TelemetryRouter.Emit(
                    WhiskeyRealism.Telemetry.TelemetryLayer.Tactical,
                    WhiskeyRealism.Telemetry.TelemetryCategory.Decision,
                    "TacticalDefenderReserve",
                    WhiskeyRealism.Telemetry.TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision("TacticalDefenderReserve", thin == 0 ? "reserved" : "thin", sig)
                        .WithField("side", AllianceId)
                        .WithField("commands", commands)
                        .WithField("held", held)
                        .WithField("thinCommands", thin)
                        .WithField("detail", detail));
            }
            catch (System.Exception ex)
            {
                try { Plugin.Log?.LogWarning("[TacticalDefenderReserve] emit failed: " + ex.Message); } catch { }
            }
        }

        public DirectChildRole GetDirectChildRole(string childId)
        {
            if (string.IsNullOrEmpty(childId)) return DirectChildRole.Unknown;
            for (int i = 0; i < _directChildIntents.Count; i++)
            {
                if (_directChildIntents[i].ChildId == childId) return _directChildIntents[i].Role;
            }
            return DirectChildRole.Unknown;
        }

        public DirectChildIntent? GetDirectChildIntent(string childId)
        {
            if (string.IsNullOrEmpty(childId)) return null;
            for (int i = 0; i < _directChildIntents.Count; i++)
            {
                if (_directChildIntents[i].ChildId == childId) return _directChildIntents[i];
            }
            return null;
        }

        // ---- Leaf brigade map (depth-agnostic role cascade) ----
        //
        // Maintained by the runtime via UpdateLeafBrigadeMap(tree) per Postfix
        // call when the tree shape or intent allocation changes. Provides
        // per-leaf-brigade role/task assignments for the posture executor to
        // iterate brigades nested inside divisions (the Union-AI-doesn't-move
        // case that motivated the cascade).

        private IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> _leafBrigadeMap =
            new Dictionary<int, TacticalLeafBrigadeMap.LeafAssignment>();
        private string _leafBrigadeMapSignature = string.Empty;

        /// <summary>
        /// The leaf map the posture producers read. While the works hold is on, a cached copy whose entries under a child that faces works are
        /// guarded (<see cref="TacticalLeafBrigadeMap.LeafAssignment.WorksHeld"/>); it is rebuilt when the leaf map, the hold hash or the maneuvers change.
        /// </summary>
        public IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> CurrentLeafBrigadeMap
            => WorksHoldOn ? GuardedLeafMap() : _leafBrigadeMap;

        private string[] _leafTopChildIds = Array.Empty<string>();
        private readonly Dictionary<int, TacticalLeafBrigadeMap.LeafAssignment> _guardedLeafMap = new Dictionary<int, TacticalLeafBrigadeMap.LeafAssignment>();
        private IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> _guardedLeafSource;
        private int _guardedLeafHoldHash, _guardedLeafManeuverHash;

        private IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> GuardedLeafMap()
        {
            int hash = _feasibility.WorksHoldHash;
            if (ReferenceEquals(_guardedLeafSource, _leafBrigadeMap) && _guardedLeafHoldHash == hash && _guardedLeafManeuverHash == _maneuverHash)
                return _guardedLeafMap;
            _guardedLeafMap.Clear();
            _guardedLeaves = 0;
            foreach (KeyValuePair<int, TacticalLeafBrigadeMap.LeafAssignment> pair in _leafBrigadeMap)
            {
                TacticalLeafBrigadeMap.LeafAssignment leaf = pair.Value;
                var context = new WorksGuardContext { WorksHold = true };
                int top = leaf.TopLevelChildIndex;
                if (top >= 0 && top < _leafTopChildIds.Length)
                {
                    context.FacesWorks = _feasibility.FacesWorks(_leafTopChildIds[top]);
                    context.InManeuverTable = _worksManeuverNodes.TryGet(
                        WhiskeyRealism.Tactical.Orchestrator.Plays.EnvelopmentManeuver.NodeIdOfChild(_leafTopChildIds[top]), out _);
                }
                if (WorksHoldGuard.ApplyLeaf(in leaf, in context, out TacticalLeafBrigadeMap.LeafAssignment guarded)) _guardedLeaves++;
                _guardedLeafMap[pair.Key] = guarded;
            }
            _guardedLeafSource = _leafBrigadeMap;
            _guardedLeafHoldHash = hash;
            _guardedLeafManeuverHash = _maneuverHash;
            return _guardedLeafMap;
        }

        // The Defender's reserve trace of the latest leaf map: one record per holding command that split among
        // two or more subordinates (what it had, what it holds back, why). Empty for an army that is not the Defender.
        private readonly List<DefenderReserveRecord> _defenderReserveReport = new List<DefenderReserveRecord>();
        public IReadOnlyList<DefenderReserveRecord> CurrentDefenderReserveReport => _defenderReserveReport;

        // Front geometry from the latest evidence (EnemyVisibleState). The leaf
        // cascade orders siblings across this axis and reads flank exposure
        // from it; UpdateLeafBrigadeMap is called by the #61 patch without an
        // axis, so the orchestrator keeps the one the tick cycle observed.
        private TacticalFrontAxis _frontAxis = TacticalFrontAxis.None;

        // Reserve release / main-role hysteresis inputs (T15/T16), refreshed
        // by the tick cycle.
        private float _lastGlobalOdds = 1f;
        private readonly MainRoleHysteresisState _mainHysteresis = new MainRoleHysteresisState();

        /// <summary>Army-wide odds from the latest evidence; read by reserve release.</summary>
        public void ObserveGlobalOdds(float globalOdds)
        {
            if (!float.IsNaN(globalOdds) && !float.IsInfinity(globalOdds) && globalOdds >= 0f) _lastGlobalOdds = globalOdds;
        }
        private IReadOnlyDictionary<int, float> _flankThreat = new Dictionary<int, float>();

        /// <summary>
        /// Records the front axis and per-group flank threat (mean outflanked weight) the
        /// evidence was banded on. Called from the orchestrator tick cycle
        /// (<see cref="ArmyTickCycle.MaybeReplan(ArmyOrchestrator, float, ArmyEvidence, EnemyVisibleState, float, float, float, float, int, float, float, float, float, float, double)"/>).
        /// </summary>
        public void ObserveFrontGeometry(EnemyVisibleState enemyVisible)
        {
            if (enemyVisible.FrontAxis.IsValid) _frontAxis = enemyVisible.FrontAxis;
            if (enemyVisible.InstanceFlankThreat != null) _flankThreat = enemyVisible.InstanceFlankThreat;
            // The perceived picture the leaf reserve release reads (sectors, strengths seen, fire seen): nothing the side has not perceived.
            _enemyVisible = enemyVisible;
            _hasEnemyVisible = true;
        }

        private EnemyVisibleState _enemyVisible;
        private bool _hasEnemyVisible;

        // The Defender's reserves inside holding commands, and the ones the release has sent out (verify F4).
        private readonly LeafReserveRelease _leafRelease = new LeafReserveRelease();
        internal LeafReserveRelease LeafRelease => _leafRelease;

        // Runs before the leaf map's cache check: the release depends on the doctrine clock and the perceived sectors, not on the tree.
        private void RefreshLeafRelease(float aggression01, WhiskeyRealism.Tactical.Battle.BattleSnapshot live)
        {
            if (_currentForceBalanceStance != BattleStance.Defender || !HasPlan || !_hasEnemyVisible)
            {
                _leafRelease.Clear();
                return;
            }
            _leafRelease.Compute(
                _defenderReserveReport, _plan, _frontAxis, _enemyVisible, _lastGlobalOdds, HoursSource(), aggression01,
                WhiskeyRealism.Tactical.Battle.CommanderPersonalityDerivation.To01(_commanderPersonality.Caution),
                WhiskeyRealism.Tactical.Battle.CommanderPersonalityDerivation.To01(_commanderPersonality.CasualtyTolerance),
                Plugin.FlankResponseEnabled(), Plugin.StaggeredReserveReleaseEnabled(), live);
            EmitLeafReleaseTelemetry();
        }

        // One TacticalReserveRelease row (tier "leaf") for each reserve a holding command newly sent out: bounded by the releases themselves.
        private void EmitLeafReleaseTelemetry()
        {
            var fresh = _leafRelease.NewReleases;
            if (fresh.Count == 0) return;
            try
            {
                double now = TacticalDoctrineTime.NowBattleHours;
                for (int i = 0; i < fresh.Count; i++)
                {
                    LeafRelease r = fresh[i];
                    if (!ReleaseRowDue(AllianceId, "child-" + r.ChildInstanceId, r.Reason + ":leaf")) continue;
                    WhiskeyRealism.Telemetry.TelemetryRouter.Emit(
                        WhiskeyRealism.Telemetry.TelemetryLayer.Tactical,
                        WhiskeyRealism.Telemetry.TelemetryCategory.Decision,
                        "TacticalReserveRelease",
                        WhiskeyRealism.Telemetry.TelemetrySeverity.Info,
                        ev => ev
                            .WithDecision("TacticalReserveRelease", r.Reason, "side=" + AllianceId + "|child=" + r.ChildInstanceId + "|tier=leaf")
                            .WithField("side", AllianceId)
                            .WithField("child", "child-" + r.ChildInstanceId)
                            .WithField("reason", r.Reason)
                            .WithField("tier", "leaf")
                            .WithField("parent", r.ParentInstanceId)
                            .WithField("role", r.Role.ToString())
                            .WithField("sector", r.Sector)
                            .WithField("held", r.Held)
                            .WithField("floor", r.Floor)
                            .WithField("released", _leafRelease.Released.Count)
                            .WithField("nowHours", now));
                }
            }
            catch (System.Exception ex)
            {
                try { Plugin.Log?.LogWarning("[TacticalReserveRelease] leaf emit failed: " + ex.Message); } catch { }
            }
        }

        /// <summary>
        /// Refresh the leaf brigade map. Caller passes the full nested tree built
        /// from extended probes; we use the current DirectChildIntent role
        /// assignments as the top-tier seeds and cascade roles down to leaves
        /// via TacticalLeafBrigadeMap.BuildMap. Caches by signature so repeated
        /// calls with no changes are O(1).
        /// </summary>
        public void UpdateLeafBrigadeMap(IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree, WhiskeyRealism.Tactical.Battle.BattleSnapshot live = null)
        {
            if (tree == null)
            {
                _leafBrigadeMap = new Dictionary<int, TacticalLeafBrigadeMap.LeafAssignment>();
                _leafBrigadeMapSignature = string.Empty;
                return;
            }

            float releaseAggression01 = (_commanderPersonality.Aggression + 1f) * 0.5f;
            if (releaseAggression01 < 0f) releaseAggression01 = 0f;
            if (releaseAggression01 > 1f) releaseAggression01 = 1f;
            RefreshLeafRelease(releaseAggression01, live);

            string sig = ComputeLeafMapSignature(tree);
            if (sig == _leafBrigadeMapSignature && _leafBrigadeMap.Count > 0) return;

            var topAssignments = new List<TacticalLeafBrigadeMap.TopAssignment>(_directChildIntents.Count);
            var topChildIds = new List<string>(_directChildIntents.Count);
            for (int i = 0; i < _directChildIntents.Count; i++)
            {
                int instanceId = TacticalBattleCoordinator.ParseInstanceIdFromChildId(_directChildIntents[i].ChildId);
                if (instanceId == 0) continue;
                topAssignments.Add(new TacticalLeafBrigadeMap.TopAssignment(instanceId, _directChildIntents[i].Role));
                topChildIds.Add(_directChildIntents[i].ChildId);
            }
            // BC14W: LeafAssignment.TopLevelChildIndex indexes the top assignments, which skip a child with no instance id.
            _leafTopChildIds = topChildIds.ToArray();

            float aggression01 = (_commanderPersonality.Aggression + 1f) * 0.5f;
            if (aggression01 < 0f) aggression01 = 0f;
            if (aggression01 > 1f) aggression01 = 1f;

            // Envelopment mode is gated on a live AttackNow doctrine decision
            // AND a clear (>=1.5x) standing advantage. Below that threshold the
            // legacy single-axis cascade is preferred — diluting force across
            // two wings on a thin margin loses locally. Matches SoW pattern of
            // selecting multi-rect army plays only when force structure
            // supports detaching a wing (offai.cpp:1115-1320).
            //
            // When envelopment fires, the choice between simultaneous (both
            // wings as Main / AttackObjective) vs echelon (primary wing as
            // Main, secondary as SupportMain / SupportAttack — Longstreet Day
            // 2 sequential template) is driven by commander aggression:
            //   aggression >= EnvelopmentSimultaneousAggressionThreshold (0.6)
            //     -> DoubleWing (Lee/Jackson — committed, simultaneous)
            //   below threshold -> DoubleWingEchelon (Longstreet/Bragg/Hooker
            //     and methodical commanders — probe with one wing, follow with
            //     the second once the first finds purchase).
            CascadeEnvelopmentMode envelopmentMode = CascadeEnvelopmentMode.None;
            float effectiveEnvelopMinRatio = EffectiveEnvelopmentMinRatio(aggression01);
            if (_currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.AttackNow &&
                _currentReinforcementOpportunity.CurrentRatio >= effectiveEnvelopMinRatio)
            {
                envelopmentMode = aggression01 >= EnvelopmentSimultaneousAggressionThreshold
                    ? CascadeEnvelopmentMode.DoubleWing
                    : CascadeEnvelopmentMode.DoubleWingEchelon;
            }

            _defenderReserveReport.Clear();
            _leafBrigadeMap = TacticalLeafBrigadeMap.BuildMap(
                tree, topAssignments, aggression01, envelopmentMode, _frontAxis, _flankThreat,
                defenderReserve: _currentForceBalanceStance == BattleStance.Defender, _defenderReserveReport, _leafRelease.Released);
            _leafBrigadeMapSignature = sig;
            EmitDefenderReserveTelemetry();
        }

        /// <summary>
        /// Baseline minimum CurrentRatio for the cascade to spread Main+SupportMain
        /// across two lateral wings. Below this, single-axis Main is safer —
        /// thin advantages get diluted across two axes and lose locally.
        ///
        /// (2026-05-22 tuned 1.5 → 1.2.) Historical envelopments commonly fired
        /// at far less than 1.5× force ratio (Jackson at Chancellorsville
        /// flanked at ~0.5×; Lee at Second Manassas at parity). Use
        /// <see cref="EffectiveEnvelopmentMinRatio"/> for the personality-aware
        /// threshold per commander aggression.
        /// </summary>
        public const float EnvelopmentMinRatio = 1.2f;

        /// <summary>
        /// Aggressive commanders (Lee/Jackson/Hood, aggression ≥ 0.7) accept
        /// envelopment at parity or slightly outnumbered — the historical
        /// precedent for offensive envelopment under disadvantage.
        /// </summary>
        public const float AggressiveEnvelopmentMinRatio = 1.0f;

        /// <summary>
        /// Cautious commanders (McClellan/Johnston, aggression ≤ 0.3) require
        /// a clearer margin before committing to multi-axis attack.
        /// </summary>
        public const float CautiousEnvelopmentMinRatio = 1.4f;

        /// <summary>
        /// Aggression threshold above which the aggressive envelopment ratio applies.
        /// </summary>
        public const float AggressiveEnvelopmentAggressionThreshold = 0.7f;

        /// <summary>
        /// Aggression threshold below which the cautious envelopment ratio applies.
        /// </summary>
        public const float CautiousEnvelopmentAggressionThreshold = 0.3f;

        /// <summary>
        /// Returns the effective envelopment minimum CurrentRatio for a commander
        /// with the given normalized aggression. Aggressive commanders accept
        /// envelopment at parity; cautious commanders require a clearer margin;
        /// mid-band uses the baseline.
        /// </summary>
        public static float EffectiveEnvelopmentMinRatio(float aggression01)
        {
            if (float.IsNaN(aggression01) || float.IsInfinity(aggression01)) return EnvelopmentMinRatio;
            if (aggression01 < 0f) aggression01 = 0f;
            if (aggression01 > 1f) aggression01 = 1f;
            if (aggression01 >= AggressiveEnvelopmentAggressionThreshold) return AggressiveEnvelopmentMinRatio;
            if (aggression01 <= CautiousEnvelopmentAggressionThreshold) return CautiousEnvelopmentMinRatio;
            return EnvelopmentMinRatio;
        }

        /// <summary>
        /// Commander aggression threshold above which envelopment uses
        /// simultaneous DoubleWing instead of sequential DoubleWingEchelon.
        /// Lee/Jackson/Hood (>= 0.6) attack both wings simultaneously;
        /// Longstreet/Bragg/methodical commanders (< 0.6) commit primary
        /// wing first, secondary wing follows as SupportMain.
        /// </summary>
        public const float EnvelopmentSimultaneousAggressionThreshold = 0.6f;

        // ---- Reinforcement opportunity doctrine ----
        //
        // Latest force-balance evaluation + decision. Refreshed by the runtime
        // each tick via UpdateReinforcementOpportunity. Consumers read via the
        // CurrentReinforcementOpportunity property; default is NoOpportunity so
        // existing behavior is preserved when the doctrine is disabled or the
        // evidence build fails.

        private ReinforcementOpportunityDecision _currentReinforcementOpportunity = new ReinforcementOpportunityDecision(
            ReinforcementOpportunity.NoOpportunity, "not-evaluated", 1f, 999f, 999f, 1f, 1f);

        public ReinforcementOpportunityDecision CurrentReinforcementOpportunity => _currentReinforcementOpportunity;

        /// <summary>
        /// Live commander initiative on [0, 1], refreshed by the runtime from
        /// GameVars.commander[id].GetCommanderInitiative() via the
        /// bunits.GetCommandingOfficerFromSide pattern. Defaults to 0.5
        /// (mid-band) until first refresh so the doctrine doesn't act on
        /// stale or empty data. Distinct from the PersonalityVector — the
        /// vector is the static historical-figure baseline (Hood = aggressive,
        /// McClellan = cautious), while LiveCommanderInitiative01 reads the
        /// vanilla per-commander field that may differ from the vector
        /// (e.g., wounded/fatigued commander initiative).
        /// </summary>
        public float LiveCommanderInitiative01 { get; private set; } = 0.5f;

        /// <summary>
        /// Update the cached live initiative reading. Caller is responsible
        /// for the GameVars.commander read + NaN/range guarding; this just
        /// caches the validated value for the doctrine to consume.
        /// </summary>
        public void UpdateLiveCommanderInitiative(float initiative01)
        {
            if (float.IsNaN(initiative01) || float.IsInfinity(initiative01)) return;
            if (initiative01 < 0f) initiative01 = 0f;
            if (initiative01 > 1f) initiative01 = 1f;
            LiveCommanderInitiative01 = initiative01;
        }

        /// <summary>
        /// Refresh the reinforcement-opportunity decision. Runtime passes in a
        /// freshly-built TacticalForceBalanceEvidence (typically from
        /// ArmyEvidenceBuilder.BuildForceBalance). The doctrine is pure; this
        /// caches its output so consumers don't re-run it per tick, and
        /// passes the raw decision through TacticalOpportunityHysteresis to
        /// prevent thrash when ratio inputs hover near category thresholds
        /// (observed in smoke as WithdrawalToFightLater ↔ DefensiveHold
        /// flipping every ~500ms — cascading into stuck-brigade behavior).
        /// </summary>
        public void UpdateReinforcementOpportunity(TacticalForceBalanceEvidence evidence)
        {
            UpdateReinforcementOpportunity(evidence, (float)TacticalDoctrineTime.NowBattleSeconds);
        }

        /// <summary>
        /// Clock-injected overload. The hysteresis hold is measured on doctrine
        /// time (TacticalDoctrineTime), so the same span of battle
        /// passes at every time compression. The "Opportunity Hysteresis Min
        /// Hold Seconds" config value is read as battle MINUTES.
        /// </summary>
        internal void UpdateReinforcementOpportunity(TacticalForceBalanceEvidence evidence, float nowBattleSeconds)
        {
            var raw = TacticalReinforcementOpportunityDoctrine.Decide(evidence);
            float sinceLastChange = _hasOpportunityChange
                ? nowBattleSeconds - _opportunityLastChangedBattleSeconds
                : float.MaxValue;
            // Battle clock ran backwards (reload): treat as long ago.
            if (sinceLastChange < 0f) sinceLastChange = float.MaxValue;

            // Config value is battle MINUTES (default bound to
            // TacticalOpportunityHysteresis.DefaultMinHoldBattleMinutes).
            float minHold = Plugin.OpportunityHysteresisMinHoldSeconds != null
                ? Plugin.OpportunityHysteresisMinHoldSeconds.Value * 60f
                : TacticalOpportunityHysteresis.DefaultMinHoldBattleSeconds;
            var smoothed = TacticalOpportunityHysteresis.Smooth(
                raw, _currentReinforcementOpportunity, sinceLastChange, minHold);
            if (smoothed.Changed || !_hasOpportunityChange)
            {
                _opportunityLastChangedBattleSeconds = nowBattleSeconds;
                _hasOpportunityChange = true;
            }
            _currentReinforcementOpportunity = smoothed.Decision;
            _currentForceBalanceStance = evidence.Stance;
        }

        // ---- Cached stance from latest force-balance evidence (2026-05-23 / L) ----
        // Read by TryPickPlan to hard-filter the playbook catalog by stance.
        // Updated by UpdateReinforcementOpportunity each tick from vanilla's
        // GetBattleType signal.
        private BattleStance _currentForceBalanceStance = BattleStance.Meeting;

        /// <summary>The side's battle stance (vanilla GetBattleType via ArmyEvidenceBuilder.ReadBattleStance).</summary>
        public BattleStance CurrentBattleStance => _currentForceBalanceStance;

        /// <summary>
        /// BC9: refreshed on every coordinator tick by ArmyPostureInputsRuntime.RefreshPickInputs (BC14: before every plan pick),
        /// independent of the reinforcement-opportunity doctrine flag, so the director,
        /// the doctrine assignment and the posture guard see a Defender as one.
        /// </summary>
        internal void UpdateBattleStance(BattleStance stance) { _currentForceBalanceStance = stance; }

        // Battle time (seconds) of the most recent accepted opportunity-category
        // change. Read by TacticalOpportunityHysteresis.Smooth to enforce the
        // minimum hold.
        private float _opportunityLastChangedBattleSeconds;
        private bool _hasOpportunityChange;

        private string ComputeLeafMapSignature(IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree)
        {
            // Signature: tree-size + role-string of direct child intents +
            // envelopment-relevant doctrine state. Envelopment is a function
            // of the AttackNow + ratio>=1.5 gate, so the signature must change
            // when those flip — otherwise the cached map keeps yesterday's
            // single-axis layout after the doctrine swings to AttackNow.
            var sb = new System.Text.StringBuilder();
            sb.Append("tree=").Append(tree.Count).Append('|');
            sb.Append("intents=").Append(_directChildIntents.Count).Append('|');
            for (int i = 0; i < _directChildIntents.Count; i++)
            {
                sb.Append(_directChildIntents[i].ChildId).Append(':').Append(_directChildIntents[i].Role).Append(',');
            }
            // MainEffortSector is part of the cascade input because role
            // distribution and offensive geometry depend on it. If the runtime
            // shifts the main effort to a different sector via
            // ConsiderMainEffortShift, the cache must invalidate so cascade
            // rebuilds with new positional priorities.
            sb.Append("|me=").Append(HasPlan ? _plan.MainEffortSector : -1);
            // The Defender keeps a reserve inside a holding command (TacticalLeafBrigadeMap), so a stance change rebuilds the map.
            sb.Append("|st=").Append((int)_currentForceBalanceStance);
            // The reserves the release has sent out change the roles under them.
            sb.Append("|rel=");
            foreach (var released in _leafRelease.Released) sb.Append(released.Key).Append(':').Append((int)released.Value).Append(',');
            sb.Append("|ph=").Append(HasPlan ? (int)_plan.Phase : -1);
            float sigAggression01 = (_commanderPersonality.Aggression + 1f) * 0.5f;
            if (sigAggression01 < 0f) sigAggression01 = 0f;
            if (sigAggression01 > 1f) sigAggression01 = 1f;
            float sigEnvelopMinRatio = EffectiveEnvelopmentMinRatio(sigAggression01);
            bool envelopActive =
                _currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.AttackNow &&
                _currentReinforcementOpportunity.CurrentRatio >= sigEnvelopMinRatio;
            // Encode the mode (1 = simultaneous, 2 = echelon, 0 = off) so the
            // cache invalidates when commander aggression flips us across the
            // threshold mid-battle — e.g., a wounded aggressive commander
            // dropping into echelon shouldn't keep the old simultaneous layout.
            int envCode = 0;
            if (envelopActive)
            {
                envCode = sigAggression01 >= EnvelopmentSimultaneousAggressionThreshold ? 1 : 2;
            }
            sb.Append("|env=").Append(envCode);
            // Front geometry: rebuild when the front rotates (15-degree buckets,
            // already stabilized against jitter by ArmyEvidenceBuilder) or the
            // vanilla flank-threat picture changes.
            sb.Append("|hd=").Append(_frontAxis.HeadingBucket(15));
            int threatHash = 0;
            if (_flankThreat != null)
            {
                foreach (var kv in _flankThreat) threatHash ^= kv.Key * 31 + (int)(kv.Value * 100f);
            }
            sb.Append("|ft=").Append(threatHash);
            return sb.ToString();
        }

        public void ApplyTacticalCommanderMode(TacticalCommanderMode mode)
        {
            _commanderMode = mode;
            if (TacticalCommanderModePolicy.RunsLedger(mode)) return;

            _currentOperation = new OperationRecord(
                TacticalOperationShape.SingleMainEffort,
                TacticalOperationPhase.Planning,
                "objective-unknown",
                0f);
            _currentStrategicBattleIntent = StrategicBattleIntentSnapshot.Empty;
            _currentCommandOperations = Array.Empty<CommandNodeOperationalState>();
            _currentDoctrineOrders = Array.Empty<CommandDoctrineOrder>();
        }

        // BC11 decision 4: the side's maneuver orders (EnvelopmentManeuver), set by the coordinator each direct-child cycle.
        private IReadOnlyList<WhiskeyRealism.Tactical.Orchestrator.Plays.ChildManeuver> _maneuvers =
            Array.Empty<WhiskeyRealism.Tactical.Orchestrator.Plays.ChildManeuver>();
        private int _maneuverHash;

        /// <summary>A hash of the maneuver orders; the ledger gate ticks when it changes (a phase change must reach the doctrine orders).</summary>
        internal int ManeuverHash => _maneuverHash;

        /// <summary>A hash of the guard points of the latest allocation (crossing guards and the works station); the ledger gate ticks when it changes.</summary>
        internal int GuardHash => DirectChildAllocator.GuardHash(_directChildIntents);

        internal void SetManeuvers(IReadOnlyList<WhiskeyRealism.Tactical.Orchestrator.Plays.ChildManeuver> maneuvers)
        {
            _maneuvers = maneuvers ?? Array.Empty<WhiskeyRealism.Tactical.Orchestrator.Plays.ChildManeuver>();
            _maneuverHash = WhiskeyRealism.Tactical.Orchestrator.Plays.EnvelopmentManeuver.Hash(_maneuvers);
        }

        internal void UpdateOperationsLedger(
            TacticalOperationsLedgerRuntime ledger,
            IReadOnlyList<CommandNodeOperationalState> commandOperations)
        {
            if (ledger == null)
            {
                ApplyTacticalCommanderMode(TacticalCommanderMode.Off);
                return;
            }

            _commanderMode = ledger.CommanderMode;
            _currentStrategicBattleIntent = ledger.CurrentStrategicBattleIntent;
            CommandNodeOperationalState[] copiedCommandOperations = CopyCommandOperations(commandOperations);
            _currentCommandOperations = copiedCommandOperations;

            if (!ledger.RunsLedger)
            {
                ApplyTacticalCommanderMode(ledger.CommanderMode);
                return;
            }

            // BC14W (requirement-works-hold): while the works hold is on, the command states are guarded first, in place; the orders built from them
            // (which take their task from each node's role, not from the state) are then guarded into the army's own array. The ledger's own list
            // stays as it built it. Both stored products are guarded in this one store, before any reader of the tick.
            ManeuverNodeTable maneuverNodes =
                WhiskeyRealism.Tactical.Orchestrator.Plays.EnvelopmentManeuver.ExpandToNodes(_maneuvers, _commandNodeIntents, _directChildIntents);
            _worksManeuverNodes = maneuverNodes;
            bool worksHold = WorksHoldOn;
            _guardedStates = 0;
            _guardedOrderCount = 0;
            if (worksHold) GuardStates(copiedCommandOperations);
            ledger.StoreDoctrineForCommandStates(copiedCommandOperations, _currentForceBalanceStance, maneuverNodes,
                feasibility: _feasibility, allianceId: AllianceId, reverseSlope: HasPlan && _plan.PlanId == BattlePlanId.ReverseSlopeDefense);
            _currentOperation = ledger.CurrentOperation;
            _currentDoctrineOrders = FinishOrders(ledger.CurrentDoctrineOrders, worksHold);
        }

        // ---- BC14W: the works hold on the stored products (requirement-works-hold) ----

        private ManeuverNodeTable _worksManeuverNodes = new ManeuverNodeTable();
        private int _guardedStates, _guardedOrderCount, _guardedLeaves;

        /// <summary>The stored states, built orders and leaf entries the works hold guard rewrote at the last store (the TacticalWorksHold row's <c>guarded</c>).</summary>
        internal int WorksGuarded => _guardedStates + _guardedOrderCount + _guardedLeaves;
        private readonly List<CommandDoctrineOrder> _guardedOrders = new List<CommandDoctrineOrder>(64);
        private string[] _childNodeIds = Array.Empty<string>();

        /// <summary>The ledger's works hold is on and Enable Enemy Works Doctrine is: the stored orders, states and leaf entries of the children that face works are guarded.</summary>
        internal bool WorksHoldOn => Plugin.EnemyWorksDoctrineEnabled() && _feasibility != null && _feasibility.WorksHold(out _);

        // The node id of each direct child ("node-" + the child's instance id), refreshed when the children change. The child ids the table was made from are
        // kept beside it: comparing them allocates nothing, where making each node id again to compare would allocate a string per child per call.
        private string[] _childNodeSources = Array.Empty<string>();

        private string[] ChildNodeIds()
        {
            int n = _directChildIntents.Count;
            bool same = _childNodeSources.Length == n;
            for (int i = 0; same && i < n; i++)
                same = string.Equals(_childNodeSources[i], _directChildIntents[i].ChildId, StringComparison.Ordinal);
            if (same) return _childNodeIds;
            _childNodeIds = new string[n];
            _childNodeSources = new string[n];
            for (int i = 0; i < n; i++)
            {
                _childNodeSources[i] = _directChildIntents[i].ChildId;
                _childNodeIds[i] = WhiskeyRealism.Tactical.Orchestrator.Plays.EnvelopmentManeuver.NodeIdOfChild(_directChildIntents[i].ChildId);
            }
            return _childNodeIds;
        }

        // The top-level child a command node belongs to (CommandNodeIntent.SourceNodeId is the child's own node), or -1.
        private int ChildIndexOfNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return -1;
            string source = nodeId;
            for (int i = 0; i < _commandNodeIntents.Count; i++)
            {
                if (!string.Equals(_commandNodeIntents[i].NodeId, nodeId, StringComparison.Ordinal)) continue;
                source = _commandNodeIntents[i].SourceNodeId;
                break;
            }
            string[] ids = ChildNodeIds();
            for (int i = 0; i < ids.Length; i++)
                if (string.Equals(ids[i], source, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>What the guard needs to know about a command node: whether the ledger has its child assaulting or facing works and whether BC11 holds the node.</summary>
        internal WorksGuardContext WorksContextOfNode(string nodeId)
        {
            int child = ChildIndexOfNode(nodeId);
            return new WorksGuardContext
            {
                WorksHold = true,
                FacesWorks = child >= 0 && _feasibility != null && _feasibility.FacesWorks(_directChildIntents[child].ChildId),
                InManeuverTable = _worksManeuverNodes.TryGet(nodeId, out _),
            };
        }

        internal void GuardStates(CommandNodeOperationalState[] states)
        {
            for (int i = 0; i < states.Length; i++)
                if (WorksHoldGuard.ApplyState(in states[i], WorksContextOfNode(states[i].NodeId), out CommandNodeOperationalState guarded))
                {
                    states[i] = guarded;
                    _guardedStates++;
                }
        }

        // The ledger's built orders, finished into the army's own array when the works hold guards them or the focal assault prepares its bombardment; the
        // ledger's own list is returned as it is when neither applies.
        internal IReadOnlyList<CommandDoctrineOrder> FinishOrders(IReadOnlyList<CommandDoctrineOrder> orders, bool worksHold)
        {
            bool prepare = PreparationPoint(out float preparationX, out float preparationZ);
            if (!worksHold && !prepare) return orders;
            _guardedOrders.Clear();
            for (int i = 0; i < orders.Count; i++)
            {
                CommandDoctrineOrder order = orders[i];
                if (worksHold && WorksHoldGuard.Apply(in order, WorksContextOfNode(order.NodeId), out order)) _guardedOrderCount++;
                if (prepare && ChildIndexOfNode(order.NodeId) >= 0) order = order.WithPreparation(preparationX, preparationZ);
                _guardedOrders.Add(order);
            }
            return _guardedOrders;
        }

        /// <summary>
        /// BC14W (requirement-focal-execution): the point the focal assault's batteries bombard before the column goes in. The plan's focal point from its
        /// activation until the column strikes (its Attack, or its abandonment), and never while the works hold keeps the column: no preparation for an
        /// assault that is not going in. Every node of the plan's children carries it, so a battery's parent group order holds it wherever the guns stand.
        /// </summary>
        internal bool PreparationPoint(out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (!Plugin.EnemyWorksDoctrineEnabled() || !HasPlan || _plan.PlanId != BattlePlanId.FocalAssault || _feasibility == null) return false;
            if (!_feasibility.TryFocalPoint(out WhiskeyRealism.Tactical.Battle.FocalPoint focal)) return false;
            if (_feasibility.WorksHold(out string reason) && string.Equals(reason, WhiskeyRealism.Tactical.Orchestrator.Plays.FeasibilityLedger.ReasonHopelessAssault, StringComparison.Ordinal))
                return false;
            for (int m = 0; m < _maneuvers.Count; m++)
            {
                if (_maneuvers[m].Kind != WhiskeyRealism.Tactical.Orchestrator.Plays.PlayRectKind.AssaultColumn) continue;
                WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverPhase phase = _maneuvers[m].Order.Phase;
                if (phase == WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverPhase.Attack || phase == WhiskeyRealism.Tactical.Orchestrator.Plays.ManeuverPhase.Abandoned)
                    return false;
            }
            x = focal.X;
            z = focal.Z;
            return true;
        }

        /// <summary>
        /// A command state the fallback builder made for a group the ledger has no state for (PostureLedger.TryResolveState's TryBuildSingle), guarded
        /// with this side's context: the node is the group's own, found through the allocated command intents.
        /// </summary>
        internal CommandNodeOperationalState GuardFallback(in CommandNodeOperationalState state)
        {
            if (!WorksHoldOn) return state;
            WorksHoldGuard.ApplyState(in state, WorksContextOfNode(state.NodeId), out CommandNodeOperationalState guarded);
            return guarded;
        }

        /// <summary>Test-only: directly install a plan without going through a playbook.</summary>
        internal void SetPlanForTesting(TacticalBattlePlan plan)
        {
            _plan = plan;
            HasPlan = true;
            _planAgeSeconds = 0f;
            _phaseAgeSeconds = 0f;
            _historyGlobalOdds = 1f;
            _replanBaseline = ReplanBaseline.Initial;
        }

        private static CommandNodeOperationalState[] CopyCommandOperations(
            IReadOnlyList<CommandNodeOperationalState> commandOperations)
        {
            if (commandOperations == null || commandOperations.Count == 0) return Array.Empty<CommandNodeOperationalState>();

            var copy = new CommandNodeOperationalState[commandOperations.Count];
            for (int i = 0; i < commandOperations.Count; i++) copy[i] = commandOperations[i];
            return copy;
        }
    }
}
