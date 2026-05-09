using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;

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
        private bool _hasObservedEvidence;

        public IReadOnlyList<DirectChildIntent> CurrentDirectChildIntents => _directChildIntents;

        public void RegisterDirectChildren(IReadOnlyList<DirectChildSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                _directChildSnapshots = Array.Empty<DirectChildSnapshot>();
                _directChildEvidenceCache = Array.Empty<DirectChildEvidence>();
                _directChildIntents = Array.Empty<DirectChildIntent>();
                _hasObservedEvidence = false;
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

        /// <summary>Test-only: directly install a plan without going through a playbook.</summary>
        internal void SetPlanForTesting(TacticalBattlePlan plan)
        {
            _plan = plan;
            HasPlan = true;
            _planAgeSeconds = 0f;
            _historyGlobalOdds = 1f;
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
