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

        public ArmyOrchestrator(int allianceId, TacticalPlaybookCatalog catalog, PersonalityVector commanderPersonality)
            : base(EchelonKind.Army, allianceId)
        {
            _catalog = catalog;
            _commanderPersonality = commanderPersonality;
            HasPlan = false;
        }

        public bool HasPlan { get; private set; }
        public TacticalBattlePlan CurrentPlan => _plan;
        public PersonalityVector CommanderPersonality => _commanderPersonality;

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
            var ctx = new PlaybookContext(
                _commanderPersonality,
                evidence.Terrain,
                evidence.CurrentOdds,
                opposingCommanderHint: 0f,
                defaultMainEffortSector: evidence.DefaultMainEffortSector,
                jitterSeed: AllianceId * 31 + 7);
            var pb = _catalog?.Select(ctx);
            if (pb == null)
            {
                HasPlan = false;
                return;
            }
            _plan = pb.Instantiate(ctx);
            HasPlan = true;
        }

        public void AdvancePhase(BattlePhase next)
        {
            if (!HasPlan) return;
            _plan = _plan.WithPhase(next);
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
                aggressionBias01: (_commanderPersonality.Aggression + 1f) * 0.5f);
        }
    }
}
