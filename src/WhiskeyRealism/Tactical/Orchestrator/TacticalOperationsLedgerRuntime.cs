using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal sealed class TacticalOperationsLedgerRuntime
    {
        // ============================================================
        // URGENT RECOVERY SAFETY BOUNDARY (Task 8)
        // ============================================================
        // Update/Replace accept optional snapshot and apply HasData guard (Task 7) so that when coordinator
        // provides a fresh gated snapshot, the ledger (and thus doctrine orders + command node states)
        // consume the atomic snapshot data. When !HasData or default: degrade to the passed objectives array
        // (which may be empty in Off mode or stale cases).
        //
        // This ledger state is read by urgent recovery (#61 patch) for CommandNodeOperationalState + doctrine
        // orders. The patch pairs it with live vanilla reads (pathinterrupted, groupsubordinatesmoving, local
        // contacts, formation state, positions, recent orders) for physical decisions and local fallback.
        // No heavy build is ever triggered from ledger consumers or the patch.
        //
        // StoreDoctrine currently writes empty (placeholder); future slices will populate from snapshot picture
        // when available.
        //
        // Authoritative: plan Task 7/8 + runtime safety (degrade on !HasData).
        // ============================================================

        private IReadOnlyList<ObjectiveRecord> _currentObjectives = Array.Empty<ObjectiveRecord>();
        private StrategicBattleIntentSnapshot _currentStrategicBattleIntent = StrategicBattleIntentSnapshot.Empty;
        private OperationRecord _currentOperation = NoOpOperation();
        private ForceAvailabilitySnapshot _currentForce = new ForceAvailabilitySnapshot(0f, 0f);
        private PersonalityVector _currentPersonality;
        private BattlefieldPictureSnapshot _currentBattlefieldPicture = new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>());
        private readonly List<CommandDoctrineOrder> _currentDoctrineOrders = new List<CommandDoctrineOrder>();
        private float _currentTimeSeconds;
        private TacticalCommanderMode _commanderMode = TacticalCommanderMode.Off;

        public TacticalCommanderMode CommanderMode => _commanderMode;
        public bool RunsLedger => TacticalCommanderModePolicy.RunsLedger(_commanderMode);
        public IReadOnlyList<ObjectiveRecord> CurrentObjectives => _currentObjectives;
        public StrategicBattleIntentSnapshot CurrentStrategicBattleIntent => _currentStrategicBattleIntent;
        public OperationRecord CurrentOperation => _currentOperation;
        public BattlefieldPictureSnapshot CurrentBattlefieldPicture => _currentBattlefieldPicture;
        public IReadOnlyList<CommandDoctrineOrder> CurrentDoctrineOrders => _currentDoctrineOrders;

        internal void Update(
            TacticalCommanderMode mode,
            IReadOnlyList<ObjectiveRecord> objectives,
            StrategicBattleIntentSnapshot strategicBattleIntent,
            ForceAvailabilitySnapshot force,
            PersonalityVector personality,
            TacticalBattleRuntimeSnapshot snapshot = default)
        {
            // Task 7: forward snapshot (optional) so ledger uses snapshot objectives when HasData.
            // This ensures doctrine assignment (CommandDoctrineAssignment.Build via picture from _currentObjectives)
            // and downstream consumers operate on snapshot data when the heavy atomic unit is provided.
            var effectiveObjectives = (snapshot.Objectives != null && snapshot.HasData)
                ? snapshot.Objectives
                : objectives;
            Replace(mode, effectiveObjectives, strategicBattleIntent, force, personality);
        }

        internal void Replace(
            TacticalCommanderMode mode,
            IReadOnlyList<ObjectiveRecord> objectives,
            StrategicBattleIntentSnapshot strategicBattleIntent,
            ForceAvailabilitySnapshot force,
            PersonalityVector personality,
            TacticalBattleRuntimeSnapshot snapshot = default)
        {
            _commanderMode = mode;
            if (!RunsLedger)
            {
                _currentObjectives = Array.Empty<ObjectiveRecord>();
                _currentStrategicBattleIntent = StrategicBattleIntentSnapshot.Empty;
                _currentOperation = NoOpOperation();
                _currentForce = new ForceAvailabilitySnapshot(0f, 0f);
                _currentPersonality = default(PersonalityVector);
                StoreDoctrine(new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>()), Array.Empty<CommandDoctrineOrder>());
                return;
            }

            // Use snapshot objectives if the caller provided a HasData snapshot (atomic with command tree / evidence at heavy build time).
            // Degrades safely for default/Empty (null guard + HasData false).
            var effectiveObjectives = (snapshot.Objectives != null && snapshot.HasData)
                ? snapshot.Objectives
                : objectives;
            _currentObjectives = CopyObjectives(effectiveObjectives);
            _currentStrategicBattleIntent = strategicBattleIntent;
            _currentForce = force;
            _currentPersonality = personality;
            StoreDoctrine(new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>()), Array.Empty<CommandDoctrineOrder>());

            if (!IsCommittedOrPostCommitOperation(_currentOperation.Phase))
            {
                var first = _currentObjectives.Count > 0 ? _currentObjectives[0] : default(ObjectiveRecord);
                var second = _currentObjectives.Count > 1 ? _currentObjectives[1] : default(ObjectiveRecord);
                var shape = TacticalOperationSelectionModel.Select(first, second, force, personality);
                _currentOperation = new OperationRecord(
                    shape,
                    TacticalOperationPhase.Planning,
                    first.Observation.ObjectiveId,
                    minimumCommitSeconds: 0f);
            }
        }

        internal void SetRuntimeClock(float nowSeconds)
        {
            _currentTimeSeconds = SanitizeNonNegative(nowSeconds);
        }

        public void StoreDoctrine(BattlefieldPictureSnapshot picture, CommandDoctrineOrder[] orders)
        {
            _currentBattlefieldPicture = new BattlefieldPictureSnapshot(CopyBattlefieldObjectives(picture.Objectives));
            _currentDoctrineOrders.Clear();
            if (orders == null || orders.Length == 0) return;
            for (int i = 0; i < orders.Length; i++)
            {
                _currentDoctrineOrders.Add(orders[i]);
            }
        }

        internal void StoreDoctrineForCommandStates(CommandNodeOperationalState[] commandStates)
        {
            if (!RunsLedger)
            {
                StoreDoctrine(new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>()), Array.Empty<CommandDoctrineOrder>());
                return;
            }

            var picture = BuildBattlefieldPicture(_currentObjectives);
            var decision = TacticalOperationDirector.Decide(TacticalOperationDirectorInput.ForTest(
                _currentOperation,
                _currentTimeSeconds,
                _currentForce.AvailableStrength,
                _currentForce.ReserveFraction,
                Clamp01((_currentPersonality.Aggression + 1f) * 0.5f),
                Clamp01((_currentPersonality.Caution + 1f) * 0.5f),
                picture.Objectives));

            _currentOperation = decision.Operation;
            StoreDoctrine(
                picture,
                CommandDoctrineAssignment.Build(
                    commandStates ?? Array.Empty<CommandNodeOperationalState>(),
                    _currentOperation,
                    picture,
                    _currentForce.AvailableStrength,
                    _currentTimeSeconds,
                    _currentForce.ReserveFraction));
        }

        private static IReadOnlyList<ObjectiveRecord> CopyObjectives(IReadOnlyList<ObjectiveRecord> objectives)
        {
            if (objectives == null || objectives.Count == 0) return Array.Empty<ObjectiveRecord>();

            var copy = new ObjectiveRecord[objectives.Count];
            for (int i = 0; i < objectives.Count; i++) copy[i] = objectives[i];
            return copy;
        }

        private static BattlefieldPictureSnapshot BuildBattlefieldPicture(IReadOnlyList<ObjectiveRecord> objectives)
        {
            if (objectives == null || objectives.Count == 0)
            {
                return new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>());
            }

            var estimates = new BattlefieldObjectiveEstimate[objectives.Count];
            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveRecord objective = objectives[i];
                var observation = objective.Observation;
                estimates[i] = new BattlefieldObjectiveEstimate(
                    observation.ObjectiveId,
                    observation.Type,
                    objective.EnemyStrength,
                    observation.SourceConfidence,
                    MainLineExposed(objective),
                    observation.Value,
                    observation.Location.X,
                    observation.Location.Z,
                    terrainStrength: 0f,
                    approachDifficulty: 0f,
                    observation.ApproachAvenue);
            }

            return new BattlefieldPictureSnapshot(estimates);
        }

        private static bool MainLineExposed(ObjectiveRecord objective)
        {
            return objective.EnemyStrength > 0f &&
                (objective.Status == TacticalObjectiveStatus.WeaklyHeld ||
                 objective.Status == TacticalObjectiveStatus.StronglyHeld ||
                 objective.Status == TacticalObjectiveStatus.Contested);
        }

        private static BattlefieldObjectiveEstimate[] CopyBattlefieldObjectives(BattlefieldObjectiveEstimate[] objectives)
        {
            if (objectives == null || objectives.Length == 0) return Array.Empty<BattlefieldObjectiveEstimate>();
            var copy = new BattlefieldObjectiveEstimate[objectives.Length];
            Array.Copy(objectives, copy, objectives.Length);
            return copy;
        }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool IsCommittedOrPostCommitOperation(TacticalOperationPhase phase)
        {
            return phase == TacticalOperationPhase.Committed ||
                phase == TacticalOperationPhase.Exploiting ||
                phase == TacticalOperationPhase.Consolidating ||
                phase == TacticalOperationPhase.SoftAbort;
        }

        private static OperationRecord NoOpOperation() =>
            new OperationRecord(
                TacticalOperationShape.SingleMainEffort,
                TacticalOperationPhase.Planning,
                "objective-unknown",
                minimumCommitSeconds: 0f);
    }
}
