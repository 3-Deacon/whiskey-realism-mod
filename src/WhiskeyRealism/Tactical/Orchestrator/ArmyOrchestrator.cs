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
    /// Army echelon orchestrator. Owns the army CO's TacticalBattlePlan, exposes
    /// CurrentMacroAi for the rewired BattleMacroStrategyPatch to read, emits
    /// ArmyIntent down to corps each tick.
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
        }

        public void AdvancePlanAge(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return;
            _planAgeSeconds += deltaSeconds;
            // Legacy compat: callers that don't supply a separate battle-time
            // delta get phase age advanced at real-time too. The preferred
            // pattern for compression-aware code is to call AdvancePhaseAge
            // explicitly with a battle-time delta and AdvancePlanAge with a
            // real-time delta — see ArmyTickCycle.MaybeReplan.
            _phaseAgeSeconds += deltaSeconds;
        }

        /// <summary>
        /// Adds <paramref name="battleDeltaSeconds"/> to phase age in battle-
        /// time. Use this when the runtime tracks GameVars.currenttimefromstart
        /// for compression-aware phase budgets (2x/5x/20x time compression
        /// makes real-time and battle-time diverge — phase budgets should run
        /// on battle-time, replan rate limits on real-time).
        ///
        /// Callers should NOT also pass the same delta to AdvancePlanAge for
        /// the phase-age component — that would double-count. The new pattern
        /// is: AdvancePhaseAge(battleDelta) FIRST to set phase age, then a
        /// real-time-only plan age update (or AdvancePlanAge for legacy).
        /// </summary>
        public void AdvancePhaseAge(float battleDeltaSeconds)
        {
            if (battleDeltaSeconds <= 0f ||
                float.IsNaN(battleDeltaSeconds) || float.IsInfinity(battleDeltaSeconds))
                return;
            _phaseAgeSeconds += battleDeltaSeconds;
        }

        /// <summary>
        /// Real-time-only plan age advance. Use this with AdvancePhaseAge
        /// to keep the two timers semantically separate: replan rate limits
        /// run on real-time (wallclock decision cadence), phase budgets run
        /// on battle-time (commander decision pace).
        /// </summary>
        public void AdvancePlanAgeRealtimeOnly(float realDeltaSeconds)
        {
            if (realDeltaSeconds <= 0f ||
                float.IsNaN(realDeltaSeconds) || float.IsInfinity(realDeltaSeconds))
                return;
            _planAgeSeconds += realDeltaSeconds;
        }

        private bool TryPickPlan(ArmyEvidence evidence, float opposingCommanderHint, out TacticalBattlePlan nextPlan)
        {
            // Envelopment pressure: replan-time read of the latest reinforcement
            // opportunity decision. AttackNow + ratio>=1.5 biases the catalog
            // toward Lee/Jackson/Hooker/Sherman envelopment playbooks. Mirrors
            // SoW's grand-tactics play selection (offai.cpp:1144-1168) but uses
            // a doctrine flag instead of a pre-battle table.
            bool envelopmentPressure =
                _currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.AttackNow &&
                _currentReinforcementOpportunity.CurrentRatio >= EnvelopmentMinRatio;
            var ctx = new PlaybookContext(
                _commanderPersonality,
                evidence.Terrain,
                evidence.CurrentOdds,
                opposingCommanderHint,
                defaultMainEffortSector: evidence.DefaultMainEffortSector,
                jitterSeed: AllianceId * 31 + 7,
                envelopmentPressure: envelopmentPressure,
                allianceId: AllianceId);
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
        /// Legacy overload without hysteresis. Used by call sites that
        /// haven't been updated to pass odds yet. Should be removed once
        /// the runtime fully migrates to the hysteresis-aware path.
        /// </summary>
        public bool ConsiderMainEffortShift(int decisiveSectorId)
            => ConsiderMainEffortShift(decisiveSectorId, decisiveSectorOdds: float.MaxValue, currentMainEffortOdds: 0f);

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

        public ArmyIntent EmitArmyIntent()
        {
            return new ArmyIntent(
                _plan.PlanId,
                _plan.Phase,
                _plan.MainEffortSector,
                _plan.FixingSectors,
                _plan.ScreeningSectors,
                _plan.ReserveCommitTriggerOdds,
                aggressionBias01: (_commanderPersonality.Aggression + 1f) * 0.5f,
                directChildIntents: _directChildIntents);
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

            _plan = nextPlan;
            HasPlan = true;
            _currentIntentModel = enemyIntent;
            _historyGlobalOdds = evidence.CurrentOdds;
            _planAgeSeconds = 0f;
            _phaseAgeSeconds = 0f;
            // Plan changed → MainEffortSector/FixingSectors/ScreeningSectors may have shifted,
            // which would change DirectChildAllocator outputs. Invalidate the direct-child
            // evidence cache so the next signature-equal observe reallocates against the new
            // plan instead of returning stale pre-replan intents. (AdvancePhase does not need
            // this — the allocator does not read BattlePhase.)
            _hasObservedEvidence = false;
        }

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
        private bool _hasObservedEvidence;

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
                _hasObservedEvidence = false;
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
            _hasObservedEvidence = false;
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

        public void ObserveDirectChildEvidence(IReadOnlyList<DirectChildEvidence> evidence)
        {
            if (!HasPlan) return;
            if (evidence == null || evidence.Count != _directChildSnapshots.Length) return;

            if (_hasObservedEvidence && SignatureEqual(evidence, _directChildEvidenceCache))
            {
                return;
            }

            for (int i = 0; i < evidence.Count; i++) _directChildEvidenceCache[i] = evidence[i];
            _hasObservedEvidence = true;

            _directChildIntents = DirectChildAllocator.Allocate(
                _plan, _commanderPersonality, _directChildSnapshots, _directChildEvidenceCache);
            _commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);
        }

        public void ObserveDirectChildEvidenceWithIntent(IReadOnlyList<DirectChildEvidence> evidence, IReadOnlyList<TacticalIntentModel> perChildEnemyIntent)
        {
            if (!HasPlan) return;
            if (evidence == null || evidence.Count != _directChildSnapshots.Length) return;
            // Force allocation regardless of signature when explicit per-child intent is supplied,
            // since enemy intent (which is not part of DirectChildEvidence.SignatureEquals) can change.
            for (int i = 0; i < evidence.Count; i++) _directChildEvidenceCache[i] = evidence[i];
            _hasObservedEvidence = true;
            _directChildIntents = DirectChildAllocator.AllocateWithChildIntent(
                _plan, _commanderPersonality, _directChildSnapshots, _directChildEvidenceCache, perChildEnemyIntent);
            _commandNodeIntents = CommandTreeIntentAllocator.Allocate(_commandTree, _directChildIntents);
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
            if (_currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.AttackNow &&
                _currentReinforcementOpportunity.CurrentRatio >= EnvelopmentMinRatio)
            {
                envelopmentMode = aggression01 >= EnvelopmentSimultaneousAggressionThreshold
                    ? CascadeEnvelopmentMode.DoubleWing
                    : CascadeEnvelopmentMode.DoubleWingEchelon;
            }

            _leafBrigadeMap = TacticalLeafBrigadeMap.BuildMap(tree, topAssignments, aggression01, envelopmentMode);
            _leafBrigadeMapSignature = sig;
        }

        /// <summary>
        /// Minimum CurrentRatio for the cascade to spread Main+SupportMain
        /// across two lateral wings. Below this, single-axis Main is safer —
        /// thin advantages get diluted across two axes and lose locally.
        /// </summary>
        public const float EnvelopmentMinRatio = 1.5f;

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
        /// just caches its output so consumers don't re-run it per tick.
        /// </summary>
        public void UpdateReinforcementOpportunity(TacticalForceBalanceEvidence evidence)
        {
            _currentReinforcementOpportunity = TacticalReinforcementOpportunityDoctrine.Decide(evidence);
        }

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
            bool envelopActive =
                _currentReinforcementOpportunity.Opportunity == ReinforcementOpportunity.AttackNow &&
                _currentReinforcementOpportunity.CurrentRatio >= EnvelopmentMinRatio;
            // Encode the mode (1 = simultaneous, 2 = echelon, 0 = off) so the
            // cache invalidates when commander aggression flips us across the
            // threshold mid-battle — e.g., a wounded aggressive commander
            // dropping into echelon shouldn't keep the old simultaneous layout.
            int envCode = 0;
            if (envelopActive)
            {
                float aggression01 = (_commanderPersonality.Aggression + 1f) * 0.5f;
                if (aggression01 < 0f) aggression01 = 0f;
                if (aggression01 > 1f) aggression01 = 1f;
                envCode = aggression01 >= EnvelopmentSimultaneousAggressionThreshold ? 1 : 2;
            }
            sb.Append("|env=").Append(envCode);
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
        }

        private static CommandNodeOperationalState[] CopyCommandOperations(
            IReadOnlyList<CommandNodeOperationalState> commandOperations)
        {
            if (commandOperations == null || commandOperations.Count == 0) return Array.Empty<CommandNodeOperationalState>();

            var copy = new CommandNodeOperationalState[commandOperations.Count];
            for (int i = 0; i < commandOperations.Count; i++) copy[i] = commandOperations[i];
            return copy;
        }

        private static bool SignatureEqual(IReadOnlyList<DirectChildEvidence> a, DirectChildEvidence[] b)
        {
            if (a.Count != b.Length) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].SignatureEquals(b[i])) return false;
            }
            return true;
        }
    }
}
