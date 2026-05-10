using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public sealed class TacticalOperationsLedgerRuntime
    {
        private IReadOnlyList<ObjectiveRecord> _currentObjectives = Array.Empty<ObjectiveRecord>();
        private StrategicBattleIntentSnapshot _currentStrategicBattleIntent = StrategicBattleIntentSnapshot.Empty;
        private OperationRecord _currentOperation = NoOpOperation();
        private TacticalCommanderMode _commanderMode = TacticalCommanderMode.Off;

        public TacticalCommanderMode CommanderMode => _commanderMode;
        public bool RunsLedger => TacticalCommanderModePolicy.RunsLedger(_commanderMode);
        public IReadOnlyList<ObjectiveRecord> CurrentObjectives => _currentObjectives;
        public StrategicBattleIntentSnapshot CurrentStrategicBattleIntent => _currentStrategicBattleIntent;
        public OperationRecord CurrentOperation => _currentOperation;

        public void Update(
            TacticalCommanderMode mode,
            IReadOnlyList<ObjectiveRecord> objectives,
            StrategicBattleIntentSnapshot strategicBattleIntent,
            ForceAvailabilitySnapshot force,
            PersonalityVector personality)
        {
            Replace(mode, objectives, strategicBattleIntent, force, personality);
        }

        public void Replace(
            TacticalCommanderMode mode,
            IReadOnlyList<ObjectiveRecord> objectives,
            StrategicBattleIntentSnapshot strategicBattleIntent,
            ForceAvailabilitySnapshot force,
            PersonalityVector personality)
        {
            _commanderMode = mode;
            if (!RunsLedger)
            {
                _currentObjectives = Array.Empty<ObjectiveRecord>();
                _currentStrategicBattleIntent = StrategicBattleIntentSnapshot.Empty;
                _currentOperation = NoOpOperation();
                return;
            }

            _currentObjectives = CopyObjectives(objectives);
            _currentStrategicBattleIntent = strategicBattleIntent;

            var first = _currentObjectives.Count > 0 ? _currentObjectives[0] : default(ObjectiveRecord);
            var second = _currentObjectives.Count > 1 ? _currentObjectives[1] : default(ObjectiveRecord);
            var shape = TacticalOperationSelectionModel.Select(first, second, force, personality);
            _currentOperation = new OperationRecord(
                shape,
                TacticalOperationPhase.Planning,
                first.Observation.ObjectiveId,
                minimumCommitSeconds: 0f);
        }

        private static IReadOnlyList<ObjectiveRecord> CopyObjectives(IReadOnlyList<ObjectiveRecord> objectives)
        {
            if (objectives == null || objectives.Count == 0) return Array.Empty<ObjectiveRecord>();

            var copy = new ObjectiveRecord[objectives.Count];
            for (int i = 0; i < objectives.Count; i++) copy[i] = objectives[i];
            return copy;
        }

        private static OperationRecord NoOpOperation() =>
            new OperationRecord(
                TacticalOperationShape.SingleMainEffort,
                TacticalOperationPhase.Planning,
                "objective-unknown",
                minimumCommitSeconds: 0f);
    }
}
