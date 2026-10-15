using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    // One side's view of the battle for one doctrine tick (BC1, plan
    // docs/superpowers/plans/2026-10-15-battle-core.md, spec §1). Pure: no Unity or
    // vanilla types. BattleSnapshotBuilder (runtime) fills it from one pass over
    // BattleUnits.completeunitlist; BattleSnapshotAssembler derives the command tree and
    // the front axis; BC2 perception fills the observation/pressure members.
    //
    // Every array is pooled and grow-only, so a rebuild into the same instance allocates
    // nothing once the roster fits. Consumers read [0, Count) and treat the instance as
    // immutable for the tick.
    public sealed partial class BattleSnapshot
    {
        public const int InitialCapacity = 512;

        // TacticalFrontAxis.Stabilize threshold the evidence builder already uses.
        public const float FrontAxisMinHeadingChangeDegrees = TacticalFrontAxis.DefaultMinHeadingChangeDegrees;

        private const int FlankCount = 3; // 0 left, 1 right, 2 rear

        private UnitRecord[] _own = new UnitRecord[InitialCapacity];
        private UnitRecord[] _enemy = new UnitRecord[InitialCapacity];
        private EnemyObservation[] _observations = new EnemyObservation[InitialCapacity];
        private readonly Dictionary<int, int> _ownIndexById = new Dictionary<int, int>(InitialCapacity);
        private readonly HashSet<int> _visibleEnemyIds = new HashSet<int>();

        // Command tree as compressed child lists: children of own index i are
        // _childList[_childStart[i] .. _childStart[i + 1]).
        private int[] _childStart = new int[InitialCapacity + 1];
        private int[] _childList = new int[InitialCapacity];

        // Per own unit, filled by BC2 perception.
        private float[] _slicePressure = new float[0];
        private float[] _flankPressure = new float[InitialCapacity * FlankCount];
        private int[] _closestVisible = new int[InitialCapacity];
        private float[] _closestVisibleDistance = new float[InitialCapacity];

        public int BattleKey { get; private set; }
        public int Alliance { get; private set; }
        public int Side { get; private set; }
        public long TickId { get; private set; }
        public double DoctrineHours { get; private set; }

        public int OwnCount { get; private set; }
        public UnitRecord[] Own => _own;

        // True enemy state: read only by BC2 perception, never by doctrine.
        public int EnemyRecordCount { get; private set; }
        public UnitRecord[] EnemyRecords => _enemy;

        public int ObservationCount { get; private set; }
        public EnemyObservation[] Observations => _observations;

        public PerceptionMode Perception { get; private set; }
        public PerceptionFallbackReason PerceptionFallback { get; private set; }

        // Own index of the W&L player's current command; -1 when it is not on this side.
        public int PlayerCommandIndex { get; private set; }

        public TacticalFrontAxis FrontAxis { get; private set; }
        public int SliceCount { get; private set; }

        public float VisibleEnemyStrength { get; private set; }
        public float RememberedEnemyStrength { get; private set; } // confidence-weighted
        public float VisibleEnemyCentroidX { get; private set; }
        public float VisibleEnemyCentroidZ { get; private set; }
        public float VisibleReinforcementStrength { get; private set; }
        public float NearestVisibleReinforcementHours { get; private set; }

        // D5 telemetry: groups where the parent map and vanilla Transform.IsChildOf
        // disagree about being an ancestor of the player's command.
        public int AncestryMismatchCount { get; private set; }

        // Increments whenever the assembler rebuilt the parent map this snapshot uses.
        internal int TreeGeneration { get; private set; }

        public int IndexOfOwn(int instanceId)
        {
            return instanceId != 0 && _ownIndexById.TryGetValue(instanceId, out int index) ? index : -1;
        }

        // Writes the direct children of ownIndex into buffer; returns how many were
        // written (capped at buffer.Length). No allocation.
        public int DirectChildren(int ownIndex, int[] buffer)
        {
            if (buffer == null || ownIndex < 0 || ownIndex >= OwnCount) return 0;
            int start = _childStart[ownIndex];
            int count = _childStart[ownIndex + 1] - start;
            if (count > buffer.Length) count = buffer.Length;
            Array.Copy(_childList, start, buffer, 0, count);
            return count;
        }

        public bool IsAncestorOf(int ancestorIndex, int descendantIndex)
        {
            if (ancestorIndex < 0 || ancestorIndex >= OwnCount) return false;
            if (descendantIndex < 0 || descendantIndex >= OwnCount) return false;
            int current = _own[descendantIndex].ParentIndex;
            // Bounded by the roster size so a malformed map cannot loop.
            for (int steps = 0; current >= 0 && current < OwnCount && steps < OwnCount; steps++)
            {
                if (current == ancestorIndex) return true;
                current = _own[current].ParentIndex;
            }
            return false;
        }

        public float SlicePressure(int ownIndex, int slice)
        {
            if (ownIndex < 0 || ownIndex >= OwnCount || slice < 0 || slice >= SliceCount) return 0f;
            return _slicePressure[ownIndex * SliceCount + slice];
        }

        public float FlankPressure(int ownIndex, int flank)
        {
            if (ownIndex < 0 || ownIndex >= OwnCount || flank < 0 || flank >= FlankCount) return 0f;
            return _flankPressure[ownIndex * FlankCount + flank];
        }

        // Observation index of the closest visible enemy, or -1.
        public int ClosestVisibleEnemy(int ownIndex, out float distance)
        {
            distance = 0f;
            if (ownIndex < 0 || ownIndex >= OwnCount) return -1;
            int observation = _closestVisible[ownIndex];
            if (observation >= 0) distance = _closestVisibleDistance[ownIndex];
            return observation;
        }

        public bool IsVisible(int enemyInstanceId)
        {
            return _visibleEnemyIds.Contains(enemyInstanceId);
        }

        // ---- builder / assembler side --------------------------------------------------

        internal void BeginBuild(int battleKey, int alliance, int side, long tickId, double doctrineHours)
        {
            BattleKey = battleKey;
            Alliance = alliance;
            Side = side;
            TickId = tickId;
            DoctrineHours = doctrineHours;
            OwnCount = 0;
            EnemyRecordCount = 0;
            ObservationCount = 0;
            _ownIndexById.Clear();
            _visibleEnemyIds.Clear();
            PlayerCommandIndex = -1;
            AncestryMismatchCount = 0;
            FrontAxis = TacticalFrontAxis.None;
            Perception = PerceptionMode.RangeOnly;
            PerceptionFallback = PerceptionFallbackReason.None;
            VisibleEnemyStrength = 0f;
            RememberedEnemyStrength = 0f;
            VisibleEnemyCentroidX = 0f;
            VisibleEnemyCentroidZ = 0f;
            VisibleReinforcementStrength = 0f;
            NearestVisibleReinforcementHours = 0f;
        }

        internal void AddOwn(in UnitRecord record)
        {
            if (OwnCount == _own.Length) Grow(ref _own);
            _own[OwnCount] = record;
            _own[OwnCount].ParentIndex = -1;
            if (record.InstanceId != 0) _ownIndexById[record.InstanceId] = OwnCount;
            OwnCount++;
        }

        internal void AddEnemy(in UnitRecord record)
        {
            if (EnemyRecordCount == _enemy.Length) Grow(ref _enemy);
            _enemy[EnemyRecordCount] = record;
            _enemy[EnemyRecordCount].ParentIndex = -1;
            EnemyRecordCount++;
        }

        internal void SetPlayerCommand(int ownIndex)
        {
            if (ownIndex < 0 || ownIndex >= OwnCount) return;
            PlayerCommandIndex = ownIndex;
            _own[ownIndex].Flags |= UnitFlags.PlayerCommand;
        }

        internal void SetAncestryMismatchCount(int count)
        {
            AncestryMismatchCount = count < 0 ? 0 : count;
        }

        internal void SetFrontAxis(TacticalFrontAxis axis)
        {
            FrontAxis = axis;
        }

        // Builds the child lists from ParentIndex (set by the assembler) and resets the
        // per-unit perception arrays to "nothing seen". O(OwnCount), no allocation once
        // the arrays fit.
        internal void IndexCommandTree(BattleSnapshotScratch scratch)
        {
            int n = OwnCount;
            TreeGeneration = scratch != null ? scratch.TreeGeneration : 0;
            if (_childStart.Length < n + 1) _childStart = new int[Math.Max(n + 1, _childStart.Length * 2)];
            if (_childList.Length < n) _childList = new int[Math.Max(n, _childList.Length * 2)];
            Array.Clear(_childStart, 0, n + 1);
            for (int i = 0; i < n; i++)
            {
                int p = _own[i].ParentIndex;
                if (p >= 0 && p < n) _childStart[p + 1]++;
            }
            for (int i = 0; i < n; i++) _childStart[i + 1] += _childStart[i];
            // _childStart[p] is the next write slot while filling; shift back afterwards.
            for (int i = 0; i < n; i++)
            {
                int p = _own[i].ParentIndex;
                if (p >= 0 && p < n) _childList[_childStart[p]++] = i;
            }
            for (int i = n; i > 0; i--) _childStart[i] = _childStart[i - 1];
            _childStart[0] = 0;

            if (_closestVisible.Length < n)
            {
                int size = Math.Max(n, _closestVisible.Length * 2);
                _closestVisible = new int[size];
                _closestVisibleDistance = new float[size];
            }
            for (int i = 0; i < n; i++) _closestVisible[i] = -1;
            if (_flankPressure.Length < n * FlankCount) _flankPressure = new float[Math.Max(n * FlankCount, _flankPressure.Length * 2)];
            Array.Clear(_flankPressure, 0, n * FlankCount);
            if (_slicePressure.Length < n * SliceCount) _slicePressure = new float[Math.Max(n * SliceCount, _slicePressure.Length * 2)];
            Array.Clear(_slicePressure, 0, n * SliceCount);
        }

        // ---- BC2 perception side --------------------------------------------------------

        // Vanilla's slice array length (GamePrefs.aidefensiveslices, 122723). Set before
        // IndexCommandTree so the per-unit slice array is sized for the tick.
        internal void SetSliceCount(int sliceCount)
        {
            SliceCount = sliceCount < 0 ? 0 : sliceCount;
        }

        internal void SetPerception(PerceptionMode mode, PerceptionFallbackReason reason)
        {
            Perception = mode;
            PerceptionFallback = reason;
        }

        internal void AddObservation(in EnemyObservation observation)
        {
            if (ObservationCount == _observations.Length) Grow(ref _observations);
            _observations[ObservationCount++] = observation;
            if (observation.State == ObservationState.Visible) _visibleEnemyIds.Add(observation.InstanceId);
        }

        internal void SetEnemySummary(float visibleStrength, float rememberedStrength, float visibleCentroidX, float visibleCentroidZ)
        {
            VisibleEnemyStrength = visibleStrength;
            RememberedEnemyStrength = rememberedStrength;
            VisibleEnemyCentroidX = visibleCentroidX;
            VisibleEnemyCentroidZ = visibleCentroidZ;
        }

        internal void SetReinforcementSummary(float visibleStrength, float nearestHours)
        {
            VisibleReinforcementStrength = visibleStrength;
            NearestVisibleReinforcementHours = nearestHours;
        }

        internal void SetSlicePressure(int ownIndex, int slice, float value)
        {
            if (ownIndex < 0 || ownIndex >= OwnCount || slice < 0 || slice >= SliceCount) return;
            _slicePressure[ownIndex * SliceCount + slice] = value;
        }

        internal void SetFlankPressure(int ownIndex, int flank, float value)
        {
            if (ownIndex < 0 || ownIndex >= OwnCount || flank < 0 || flank >= FlankCount) return;
            _flankPressure[ownIndex * FlankCount + flank] = value;
        }

        internal void SetClosestVisibleEnemy(int ownIndex, int observationIndex, float distance)
        {
            if (ownIndex < 0 || ownIndex >= OwnCount) return;
            _closestVisible[ownIndex] = observationIndex;
            _closestVisibleDistance[ownIndex] = distance;
        }

        private static void Grow<T>(ref T[] array)
        {
            Array.Resize(ref array, Math.Max(InitialCapacity, array.Length * 2));
        }
    }
}
