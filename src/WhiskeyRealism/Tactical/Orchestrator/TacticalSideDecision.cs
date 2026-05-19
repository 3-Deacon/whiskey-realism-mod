using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-side immutable decision produced by the worker thread.
    /// All fields are immutable after construction. Consumed by main-thread
    /// patches via <c>TacticalDecisionSnapshot.Current</c>.
    ///
    /// Declared <c>internal</c> because it exposes <see cref="CommandTreeSnapshot"/>,
    /// which is itself internal (see <c>CommandNodeContracts.cs</c>). Same
    /// accessibility precedent as <see cref="TacticalBattleRuntimeSnapshot"/>.
    /// </summary>
    internal readonly struct TacticalSideDecision
    {
        public TacticalSideDecision(
            int allianceId,
            TacticalBattlePlan currentPlan,
            TacticalIntentModel currentIntent,
            CommandTreeSnapshot commandTree,
            StrategicBattleIntentSnapshot strategicIntent,
            ForceAvailabilitySnapshot forceAvailability,
            PersonalityVector commanderPersonality,
            ReplanTrigger lastReplanTrigger,
            bool hasPlan)
        {
            AllianceId = allianceId;
            CurrentPlan = currentPlan;
            CurrentIntent = currentIntent;
            CommandTree = commandTree ?? CommandTreeSnapshot.Empty;
            StrategicIntent = strategicIntent;
            ForceAvailability = forceAvailability;
            CommanderPersonality = commanderPersonality;
            LastReplanTrigger = lastReplanTrigger;
            HasPlan = hasPlan;
        }

        public int AllianceId { get; }
        public TacticalBattlePlan CurrentPlan { get; }
        public TacticalIntentModel CurrentIntent { get; }
        public CommandTreeSnapshot CommandTree { get; }
        public StrategicBattleIntentSnapshot StrategicIntent { get; }
        public ForceAvailabilitySnapshot ForceAvailability { get; }
        public PersonalityVector CommanderPersonality { get; }
        public ReplanTrigger LastReplanTrigger { get; }
        public bool HasPlan { get; }

        public static TacticalSideDecision Empty => new TacticalSideDecision(
            allianceId: -1,
            currentPlan: default,
            currentIntent: default,
            commandTree: CommandTreeSnapshot.Empty,
            strategicIntent: StrategicBattleIntentSnapshot.Empty,
            forceAvailability: new ForceAvailabilitySnapshot(0f, 0f),
            commanderPersonality: default,
            lastReplanTrigger: ReplanTrigger.None,
            hasPlan: false);
    }
}
