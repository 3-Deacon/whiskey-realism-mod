using System;
using System.Collections.Generic;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

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
        private float _operationAssignedStrength;
        private TacticalCommanderMode _commanderMode = TacticalCommanderMode.Off;

        // The assigned points of this side's nodes, held while the line they were planned on has not really moved (DoctrineTargetHold).
        private readonly DoctrineTargetHold _targetHold = new DoctrineTargetHold();

        // The enemy line the side last saw, kept through a count without a glimpse of it (EnemyLineMemory).
        private readonly EnemyLineMemory _enemyLineMemory = new EnemyLineMemory();

        // The operation the director decided at the last count (the reference of the TacticalOperationChange row).
        private OperationRecord _lastDecidedOperation = NoOpOperation();

        // The doctrine second the committed force first stood below the abort line, -1 while it does not.
        private float _collapseSinceSeconds = -1f;

        public TacticalCommanderMode CommanderMode => _commanderMode;
        public bool RunsLedger => TacticalCommanderModePolicy.RunsLedger(_commanderMode);
        public IReadOnlyList<ObjectiveRecord> CurrentObjectives => _currentObjectives;
        public StrategicBattleIntentSnapshot CurrentStrategicBattleIntent => _currentStrategicBattleIntent;
        public OperationRecord CurrentOperation => _currentOperation;

        /// <summary>BC14O: the main-effort objective the last doctrine store picked (<see cref="ObjectivePick.Has"/> false with the doctrine off or nothing feasible).</summary>
        internal ObjectivePick CurrentObjectivePick { get; private set; }
        public BattlefieldPictureSnapshot CurrentBattlefieldPicture => _currentBattlefieldPicture;
        public IReadOnlyList<CommandDoctrineOrder> CurrentDoctrineOrders => _currentDoctrineOrders;

        /// <summary>Where the side's command nodes stand and what it knows of the rivers; null until the coordinator sets it (nodes are then unplaced and planned nowhere).</summary>
        internal ICommandGround Ground { get; private set; }

        internal void SetGround(ICommandGround ground)
        {
            Ground = ground;
        }

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
                _operationAssignedStrength = 0f;
                _enemyLineMemory.Clear();
                _collapseSinceSeconds = -1f;
                _lastDecidedOperation = NoOpOperation();
                StoreDoctrine(new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>()), Array.Empty<CommandDoctrineOrder>());
                return;
            }

            // Use snapshot objectives if the caller provided a HasData snapshot (atomic with command tree / evidence at heavy build time).
            // Degrades safely for default/Empty (null guard + HasData false).
            var effectiveObjectives = (snapshot.Objectives != null && snapshot.HasData)
                ? snapshot.Objectives
                : objectives;
            _currentObjectives = CopyObjectives(_enemyLineMemory.Apply(effectiveObjectives, _currentTimeSeconds));
            _currentStrategicBattleIntent = strategicBattleIntent;
            _currentForce = force;
            _currentPersonality = personality;
            StoreDoctrine(new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>()), Array.Empty<CommandDoctrineOrder>());

            // A Forming operation on the enemy line the side perceives is held for its own commit window (the director sets it to selection time + 900 s).
            // It used to reset to Planning on the first objective here, so it lasted one update, and the enemy line exists only while the side sees the
            // enemy: one tick without a glimpse and a side in contact "had not met the enemy" again (smoke 2026-10-17: Forming, Scouting, Scouting,
            // Scouting, SoftAbort in 3 minutes). A map objective does not vanish with the fog, so a Forming operation on one is reselected as before
            // (it may retarget to a live objective before it commits), a Scouting operation is selected afresh every update, and past its window the
            // held operation is too.
            bool formingHeld = _currentOperation.Phase == TacticalOperationPhase.Forming && _currentTimeSeconds < _currentOperation.MinimumCommitSeconds &&
                !string.IsNullOrEmpty(_currentOperation.PrimaryObjectiveId) &&
                _currentOperation.PrimaryObjectiveId.StartsWith("enemy-line-", StringComparison.Ordinal);
            if (!IsCommittedOrPostCommitOperation(_currentOperation.Phase) && !formingHeld)
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

        /// <summary>
        /// Live strength of the nodes tasked to attack/support the current operation,
        /// measured by the coordinator after the previous ledger tick (T14). 0 = none.
        /// </summary>
        internal void SetOperationAssignedStrength(float strength)
        {
            _operationAssignedStrength = SanitizeNonNegative(strength);
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

        // stance: the side's battle stance (ArmyEvidenceBuilder.ReadBattleStance, cached
        // by the army from its force-balance evidence); a Defender is never sent probing.
        // reverseSlope: the army's plan is ReverseSlopeDefense, so a crest line is held 80 m behind the crest (BC12).
        // feasibility / allianceId (BC14O): the side's ledger, whose objective table and pressure the director picks the main-effort objective with, and
        // the alliance whose latch holds it (null / -1: the shipped director).
        internal void StoreDoctrineForCommandStates(CommandNodeOperationalState[] commandStates, BattleStance stance,
            ManeuverNodeTable maneuvers = null, bool reverseSlope = false, IFeasibilityLookup feasibility = null, int allianceId = -1)
        {
            if (!RunsLedger)
            {
                _targetHold.Clear();
                StoreDoctrine(new BattlefieldPictureSnapshot(Array.Empty<BattlefieldObjectiveEstimate>()), Array.Empty<CommandDoctrineOrder>());
                return;
            }

            var picture = BuildBattlefieldPicture(_currentObjectives);
            ObjectiveLatch latch = feasibility != null && Plugin.ObjectiveDoctrineEnabled() ? ObjectiveLatch.ForAlliance(allianceId) : null;
            TacticalOperationDirectorInput input = TacticalOperationDirectorInput.ForTest(
                _currentOperation,
                _currentTimeSeconds,
                _currentForce.AvailableStrength,
                _currentForce.ReserveFraction,
                Clamp01((_currentPersonality.Aggression + 1f) * 0.5f),
                Clamp01((_currentPersonality.Caution + 1f) * 0.5f),
                picture.Objectives).WithOperationAssignedStrength(_operationAssignedStrength).WithStance(stance)
                .WithObjectiveDoctrine(latch != null ? feasibility : null, latch?.Id);
            var decision = TacticalOperationDirector.Decide(input.WithOddsCollapseSeconds(TrackOddsCollapse(input)));

            // The row compares with what the director decided last, not with _currentOperation: the update resets an operation that is not held to
            // Planning before every decision, which would make every count a change.
            EmitOperationChange(allianceId, _lastDecidedOperation, decision, input);
            _lastDecidedOperation = decision.Operation;
            _currentOperation = decision.Operation;
            CurrentObjectivePick = decision.Pick;
            // BC14O: the latch follows the pick; nothing feasible (or an objective gone from the map) clears it, and the next pick starts with no margin.
            if (latch != null)
            {
                if (decision.Pick.Has) latch.Set(decision.Pick.Objective.ObjectiveId, decision.Pick.Objective.X, decision.Pick.Objective.Z);
                else latch.Clear();
            }
            CommandDoctrineOrder[] orders;
            // BC12: the doctrine build plans the terrain line (TacticalTerrainLinePlanner) once per assignment.
            using (TelemetryPerf.Scope("tactical.terrain-line", TelemetryLayer.Tactical, TelemetryCategory.Performance, 1.0))
            {
                orders = CommandDoctrineAssignment.Build(
                    commandStates ?? Array.Empty<CommandNodeOperationalState>(),
                    _currentOperation,
                    picture,
                    _currentForce.AvailableStrength,
                    _currentTimeSeconds,
                    _currentForce.ReserveFraction,
                    stance,
                    maneuvers,
                    reverseSlope,
                    Ground?.Crossings,
                    Ground != null ? Ground.HomeBank(commandStates) : -1,
                    _targetHold);
            }
            NoteFirstTerrainLine(orders);
            StoreDoctrine(picture, orders);
        }

        // One row per change of the operation (its phase, shape or primary objective), with what the director decided it on: the reason, and the forces and
        // the enemy it weighed. Rows that were silent about this left a smoke unable to tell a lost contact from a collapsed force (2026-10-18).
        private static void EmitOperationChange(int side, OperationRecord before, TacticalOperationDirectorDecision decision, TacticalOperationDirectorInput input)
        {
            OperationRecord after = decision.Operation;
            if (after.Phase == before.Phase && after.Shape == before.Shape && string.Equals(after.PrimaryObjectiveId, before.PrimaryObjectiveId, StringComparison.Ordinal))
                return;
            try
            {
                float enemy = 0f;
                for (int i = 0; i < input.Objectives.Length; i++)
                {
                    if (!string.Equals(input.Objectives[i].ObjectiveId, after.PrimaryObjectiveId, StringComparison.Ordinal)) continue;
                    enemy = input.Objectives[i].EnemyStrength;
                    break;
                }
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalOperationChange", TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision("TacticalOperationChange", after.Phase.ToString(), decision.Reason)
                        .WithField("side", side)
                        .WithField("fromPhase", before.Phase.ToString())
                        .WithField("toPhase", after.Phase.ToString())
                        .WithField("fromShape", before.Shape.ToString())
                        .WithField("toShape", after.Shape.ToString())
                        .WithField("fromPrimary", before.PrimaryObjectiveId)
                        .WithField("toPrimary", after.PrimaryObjectiveId)
                        .WithField("ownStrength", input.OwnStrength)
                        .WithField("assignedStrength", input.OperationAssignedStrength)
                        .WithField("enemyStrength", enemy)
                        .WithField("collapseSeconds", input.OddsCollapseSeconds)
                        .WithField("holdUntil", after.MinimumCommitSeconds)
                        .WithField("seconds", input.CurrentTimeSeconds));
            }
            catch { /* telemetry never changes a decision */ }
        }

        // How long the committed force has stood below the abort line, on the doctrine clock: the director aborts on a collapse that lasts, not on one count.
        private float TrackOddsCollapse(TacticalOperationDirectorInput input)
        {
            if (!TacticalOperationDirector.OddsCollapsed(input))
            {
                _collapseSinceSeconds = -1f;
                return 0f;
            }
            // A clock that ran backwards (a reload to an earlier save) starts the collapse again.
            if (_collapseSinceSeconds < 0f || _currentTimeSeconds < _collapseSinceSeconds) _collapseSinceSeconds = _currentTimeSeconds;
            return _currentTimeSeconds - _collapseSinceSeconds;
        }

        // First-fire marker for BC12's terrain lines (smoke item 2.31): the first order whose line reason names a terrain line.
        private static void NoteFirstTerrainLine(CommandDoctrineOrder[] orders)
        {
            if (orders == null) return;
            for (int i = 0; i < orders.Length; i++)
            {
                string reason = orders[i].Reason;
                int at = reason.IndexOf("terrain-line:", StringComparison.Ordinal);
                if (at < 0) continue;
                OnceLog.Info("tactical-battle-core:terrain-line", "[BattleTerrain] first terrain-line anchor: " + reason.Substring(at));
                return;
            }
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
                    terrainStrength: observation.TerrainStrength,
                    approachDifficulty: observation.ApproachDifficulty,
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
