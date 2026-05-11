using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;

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
            TickCount++;
            for (int i = 0; i < Echelons.Count; i++) Echelons[i]?.Tick();
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
            PersonalityVector personality)
        {
            if (Army == null) return;

            OperationsLedger.Update(mode, objectives, strategicBattleIntent, force, personality);
            var commandOperations = CommandNodeOperationsRuntime.Build(
                Army.CurrentCommandNodeIntents,
                OperationsLedger.CurrentOperation,
                OperationsLedger.CurrentObjectives);
            Army.UpdateOperationsLedger(OperationsLedger, commandOperations ?? Array.Empty<CommandNodeOperationalState>());
        }
    }
}
