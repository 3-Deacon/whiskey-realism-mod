using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
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
        public ArmyEvidence(float currentOdds, TerrainKind terrain, int defaultMainEffortSector)
        {
            CurrentOdds = currentOdds;
            Terrain = terrain;
            DefaultMainEffortSector = defaultMainEffortSector;
        }

        public float CurrentOdds { get; }
        public TerrainKind Terrain { get; }
        public int DefaultMainEffortSector { get; }
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

        private ReplanBaseline _replanBaseline = ReplanBaseline.Initial;
        public ReplanBaseline LastReplanBaseline => _replanBaseline;

        /// <summary>Recorded by the tick cycle right after a replan.</summary>
        public void RecordReplanBaseline(ReplanBaseline baseline) => _replanBaseline = baseline;

        /// <summary>
        /// AIBattle.macroai derived from current plan + phase + commander aggression.
        /// Returns -1 (dynamic) when no plan picked yet — signals the rewired #44
        /// patch to leave vanilla's macroai alone.
        /// </summary>
        public int CurrentMacroAi
        {
            get
            {
                if (!HasPlan) return -1;
                switch (_plan.Phase)
                {
                    case BattlePhase.Probe:        return _commanderPersonality.Aggression > 0.3f ? 1 : -1;
                    case BattlePhase.MainEffort:   return _commanderPersonality.Aggression > 0.0f ? 1 : 0;
                    case BattlePhase.Exploit:      return 0;
                    case BattlePhase.Consolidate:  return 2;
                    case BattlePhase.Withdraw:     return 3;
                    default:                       return -1;
                }
            }
        }

        public void PickInitialPlan(ArmyEvidence evidence)
        {
            if (TryPickPlan(evidence, opposingCommanderHint: 0f, out var nextPlan))
            {
                _plan = nextPlan;
                HasPlan = true;
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
        /// The tick cycle passes doctrine seconds (TacticalDoctrineClock).
        /// </summary>
        public void AdvancePlanAge(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return;
            _planAgeSeconds += deltaSeconds;
            _phaseAgeSeconds += deltaSeconds;
        }

        private bool TryPickPlan(ArmyEvidence evidence, float opposingCommanderHint, out TacticalBattlePlan nextPlan)
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
            BattleStance stance = TacticalStanceReconciler.ResolveEffectiveStance(
                _currentForceBalanceStance, _currentReinforcementOpportunity.Opportunity);

            var ctx = new PlaybookContext(
                _commanderPersonality,
                evidence.Terrain,
                evidence.CurrentOdds,
                opposingCommanderHint,
                defaultMainEffortSector: evidence.DefaultMainEffortSector,
                jitterSeed: AllianceId * 31 + 7,
                envelopmentPressure: envelopmentPressure,
                allianceId: AllianceId,
                cavalryFraction: cavalryFraction,
                infantryFraction: infantryFraction,
                artilleryFraction: artilleryFraction,
                delayingDefensePressure: delayingDefensePressure,
                defensivePressure: defensivePressure,
                stance: stance);
            var pb = _catalog?.Select(ctx);
            if (pb == null)
            {
                nextPlan = default;
                return false;
            }
            nextPlan = pb.Instantiate(ctx);

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
            if (!HasPlan) return false;
            if (decisiveSectorId < 0) return false;
            if (_plan.MainEffortSector == decisiveSectorId) return false;
            if (_plan.Phase == BattlePhase.Consolidate ||
                _plan.Phase == BattlePhase.Withdraw)
                return false;
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
            TacticalSectorReadinessDoctrine.Result mainEffortReadiness = TacticalSectorReadinessDoctrine.Result.PushReady)
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
                mainEffortReadiness: mainEffortReadiness);

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

        public void Replan(ArmyEvidence evidence, TacticalIntentModel enemyIntent)
        {
            if (!TryPickPlan(evidence, OpposingCommanderHintFromIntent(enemyIntent), out var nextPlan))
            {
                return;
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
                    supportPriority01: 0f, aggressionBias01: (_commanderPersonality.Aggression + 1f) * 0.5f,
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
            _directChildIntents = DirectChildAllocator.StabilizeMain(
                DirectChildAllocator.AllocateWithZones(
                    _plan, _commanderPersonality, _directChildSnapshots, _directChildEvidenceCache,
                    perChildEnemyIntent, zoneAssignments, new ReserveReleaseContext(_lastGlobalOdds)),
                _mainHysteresis);
            _commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);
            EmitDirectChildIntentTelemetry();
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

        public IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> CurrentLeafBrigadeMap
            => _leafBrigadeMap;

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
        /// (<see cref="ArmyTickCycle.MaybeReplan(ArmyOrchestrator, float, ArmyEvidence, EnemyVisibleState, float, float, float, float, int, float, float, float, float, float)"/>).
        /// </summary>
        public void ObserveFrontGeometry(EnemyVisibleState enemyVisible)
        {
            if (enemyVisible.FrontAxis.IsValid) _frontAxis = enemyVisible.FrontAxis;
            if (enemyVisible.InstanceFlankThreat != null) _flankThreat = enemyVisible.InstanceFlankThreat;
        }

        public TacticalLeafBrigadeMap.LeafAssignment? GetLeafBrigadeAssignment(int instanceId)
        {
            if (_leafBrigadeMap == null) return null;
            return _leafBrigadeMap.TryGetValue(instanceId, out var assignment) ? assignment : (TacticalLeafBrigadeMap.LeafAssignment?)null;
        }

        /// <summary>
        /// Refresh the leaf brigade map. Caller passes the full nested tree built
        /// from extended probes; we use the current DirectChildIntent role
        /// assignments as the top-tier seeds and cascade roles down to leaves
        /// via TacticalLeafBrigadeMap.BuildMap. Caches by signature so repeated
        /// calls with no changes are O(1).
        /// </summary>
        public void UpdateLeafBrigadeMap(IReadOnlyDictionary<int, TacticalCommandTreeProbe.ProbeNode> tree)
        {
            if (tree == null)
            {
                _leafBrigadeMap = new Dictionary<int, TacticalLeafBrigadeMap.LeafAssignment>();
                _leafBrigadeMapSignature = string.Empty;
                return;
            }

            string sig = ComputeLeafMapSignature(tree);
            if (sig == _leafBrigadeMapSignature && _leafBrigadeMap.Count > 0) return;

            var topAssignments = new List<TacticalLeafBrigadeMap.TopAssignment>(_directChildIntents.Count);
            for (int i = 0; i < _directChildIntents.Count; i++)
            {
                int instanceId = TacticalBattleCoordinator.ParseInstanceIdFromChildId(_directChildIntents[i].ChildId);
                if (instanceId == 0) continue;
                topAssignments.Add(new TacticalLeafBrigadeMap.TopAssignment(instanceId, _directChildIntents[i].Role));
            }

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

            _leafBrigadeMap = TacticalLeafBrigadeMap.BuildMap(
                tree, topAssignments, aggression01, envelopmentMode, _frontAxis, _flankThreat);
            _leafBrigadeMapSignature = sig;
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
            UpdateReinforcementOpportunity(evidence, (float)TacticalDoctrineClock.NowBattleSeconds);
        }

        /// <summary>
        /// Clock-injected overload. The hysteresis hold is measured on doctrine
        /// time (TacticalDoctrineClock), so the same span of battle
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

            ledger.StoreDoctrineForCommandStates(copiedCommandOperations);
            _currentOperation = ledger.CurrentOperation;
            _currentDoctrineOrders = ledger.CurrentDoctrineOrders;
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
