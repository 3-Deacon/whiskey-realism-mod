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
        }

        public void AdvancePlanAge(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return;
            _planAgeSeconds += deltaSeconds;
        }

        private bool TryPickPlan(ArmyEvidence evidence, float opposingCommanderHint, out TacticalBattlePlan nextPlan)
        {
            var ctx = new PlaybookContext(
                _commanderPersonality,
                evidence.Terrain,
                evidence.CurrentOdds,
                opposingCommanderHint,
                defaultMainEffortSector: evidence.DefaultMainEffortSector,
                jitterSeed: AllianceId * 31 + 7);
            var pb = _catalog?.Select(ctx);
            if (pb == null)
            {
                nextPlan = default;
                return false;
            }
            nextPlan = pb.Instantiate(ctx);
            return true;
        }

        public void AdvancePhase(BattlePhase next)
        {
            if (!HasPlan) return;
            if (_plan.Phase == next) return;
            _plan = _plan.WithPhase(next);
            _planAgeSeconds = 0f;
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

            _leafBrigadeMap = TacticalLeafBrigadeMap.BuildMap(tree, topAssignments, aggression01);
            _leafBrigadeMapSignature = sig;
        }

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
            // Signature: count of tree nodes + count + role-string of direct child intents.
            // Cheap to compute and dedupes the rebuild call between ticks.
            var sb = new System.Text.StringBuilder();
            sb.Append("tree=").Append(tree.Count).Append('|');
            sb.Append("intents=").Append(_directChildIntents.Count).Append('|');
            for (int i = 0; i < _directChildIntents.Count; i++)
            {
                sb.Append(_directChildIntents[i].ChildId).Append(':').Append(_directChildIntents[i].Role).Append(',');
            }
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
