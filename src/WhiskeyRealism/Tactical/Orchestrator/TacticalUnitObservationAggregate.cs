using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pooled <see cref="IObservationSource"/> implementation. Reused
    /// across Build calls — instance is per-battle (cleared on battle
    /// end via <see cref="ClearForBattleEnd"/>); steady-state per-Build
    /// allocations are zero after the underlying lists grow into their
    /// working size.
    ///
    /// The runtime-only <see cref="Capture"/> entry (which touches
    /// <c>BattleUnits.completeunitlist</c>) lives in
    /// <c>TacticalUnitObservationAggregate.Runtime.cs</c>; harness tests
    /// build aggregates via <see cref="LoadForTest"/>.
    /// </summary>
    public sealed partial class TacticalUnitObservationAggregate : IObservationSource
    {
        private static readonly TacticalUnitObservationAggregate _shared = new TacticalUnitObservationAggregate();

        private readonly List<TacticalUnitObservation> _units = new List<TacticalUnitObservation>(512);
        private readonly List<int> _alliedIndices = new List<int>(64);
        private readonly List<int> _enemyIndices = new List<int>(64);
        private int _capturedForAlliance = -1;

        public static TacticalUnitObservationAggregate Shared
        {
            get { return _shared; }
        }

        public int Count
        {
            get { return _units.Count; }
        }

        public int CapturedForAlliance
        {
            get { return _capturedForAlliance; }
        }

        public IReadOnlyList<TacticalUnitObservation> AllUnits
        {
            get { return _units; }
        }

        public IReadOnlyList<int> AlliedIndices
        {
            get { return _alliedIndices; }
        }

        public IReadOnlyList<int> EnemyIndices
        {
            get { return _enemyIndices; }
        }

        /// <summary>
        /// Clears the aggregate. Called from
        /// <c>TacticalBattleCoordinator.ResetRuntimeTickState</c> on
        /// battle end so the next battle's first capture starts clean.
        /// </summary>
        public void ClearForBattleEnd()
        {
            _units.Clear();
            _alliedIndices.Clear();
            _enemyIndices.Clear();
            _capturedForAlliance = -1;
        }

        /// <summary>
        /// Harness-only loader. Replaces the captured contents with the
        /// supplied observations and rebuilds index views from the
        /// supplied alliance id. Not called from runtime code.
        /// </summary>
        public void LoadForTest(int allianceId, IReadOnlyList<TacticalUnitObservation> units)
        {
            _units.Clear();
            _alliedIndices.Clear();
            _enemyIndices.Clear();
            _capturedForAlliance = allianceId;
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                _units.Add(units[i]);
                if (units[i].Alliance == allianceId) _alliedIndices.Add(i);
                else _enemyIndices.Add(i);
            }
        }
    }
}
