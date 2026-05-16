using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-side root container instantiated by the coordinator for each active battle alliance.
    /// O0 shipped with empty Echelons; O1 attaches a single ArmyOrchestrator via AttachArmy.
    /// Future phases attach Corps / Division / Brigade echelons as children of Army.
    /// </summary>
    public sealed class TacticalBattleOrchestrator
    {
        public TacticalBattleOrchestrator(int allianceId, TacticalCommanderRoster roster)
        {
            AllianceId = allianceId;
            Roster = roster;
            Echelons = new List<EchelonOrchestrator>();
            OperationsLedger = new TacticalOperationsLedgerRuntime();
        }

        public int AllianceId { get; }
        public TacticalCommanderRoster Roster { get; }
        public List<EchelonOrchestrator> Echelons { get; }
        public int TickCount { get; private set; }
        public ArmyOrchestrator Army { get; private set; }
        internal TacticalOperationsLedgerRuntime OperationsLedger { get; }

        /// <summary>
        /// Attach the per-side ArmyOrchestrator. No-op if <paramref name="army"/> is null.
        /// Idempotent: re-attaching the same instance does not duplicate it in <see cref="Echelons"/>.
        /// </summary>
        public void AttachArmy(ArmyOrchestrator army)
        {
            if (army == null) return;
            Army = army;
            if (!Echelons.Contains(army)) Echelons.Add(army);
        }

        public void Tick()
        {
            using (TelemetryPerf.Scope("tactical.orchestrator-side-tick", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                TickCount++;
                for (int i = 0; i < Echelons.Count; i++) Echelons[i]?.Tick();
            }
        }

        public void PropagateIntent()
        {
            for (int i = 0; i < Echelons.Count; i++) Echelons[i]?.PropagateIntent();
        }

        internal void TickOperationsLedger(
            TacticalCommanderMode mode,
            IReadOnlyList<ObjectiveRecord> objectives,
            StrategicBattleIntentSnapshot strategicBattleIntent,
            ForceAvailabilitySnapshot force,
            PersonalityVector personality,
            TacticalBattleRuntimeSnapshot snapshot = default)
        {
            if (Army == null) return;

            // Task 7: accept snapshot (optional for compat; default/Empty degrades to prior paths per plan).
            // When provided and HasData (from heavy gated build in coordinator), use its Objectives (for ledger/director/doctrine)
            // and CommandTree (for nested division play) so CommandNodeOperationsRuntime.Build, TacticalNestedDivisionPlayPlanner,
            // and CommandDoctrineAssignment (via ledger picture) all consume the atomic snapshot data instead of independently rebuilt values.
            // Safe guards prevent NRE on default(struct) zeroed refs (Objectives/CommandTree may be null only for default param).
            IReadOnlyList<ObjectiveRecord> effectiveObjectives = (snapshot.Objectives != null && snapshot.HasData)
                ? snapshot.Objectives
                : (objectives ?? Array.Empty<ObjectiveRecord>());

            CommandTreeSnapshot effectiveCommandTree = (snapshot.CommandTree != null && snapshot.CommandTree.HasNodes)
                ? snapshot.CommandTree
                : Army.CurrentCommandTree;

            OperationsLedger.Update(mode, effectiveObjectives, strategicBattleIntent, force, personality, snapshot);
            IReadOnlyList<CommandNodeOperationalState> commandOperations;
            using (TelemetryPerf.Scope("tactical.command-assignment", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                commandOperations = CommandNodeOperationsRuntime.Build(
                    Army.CurrentCommandNodeIntents,
                    OperationsLedger.CurrentOperation,
                    OperationsLedger.CurrentObjectives);
                commandOperations = TacticalNestedDivisionPlayPlanner.Apply(
                    effectiveCommandTree,
                    commandOperations);
            }
            Army.UpdateOperationsLedger(OperationsLedger, commandOperations ?? Array.Empty<CommandNodeOperationalState>());
        }
    }
}
