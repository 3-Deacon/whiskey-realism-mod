using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Read-only view onto a captured set of <see cref="TacticalUnitObservation"/>.
    /// Consumed by the aggregate-based variants of the six ObjectiveRecord
    /// sub-builders. Implementations: <see cref="TacticalUnitObservationAggregate"/>
    /// for runtime, harness stubs for tests.
    /// </summary>
    public interface IObservationSource
    {
        /// <summary>
        /// Total number of units captured (allied + enemy combined).
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Alliance the aggregate was captured for. Visibility / objective /
        /// waypoint fields are populated only for units where
        /// <c>Alliance == CapturedForAlliance</c>.
        /// </summary>
        int CapturedForAlliance { get; }

        /// <summary>
        /// All captured units in <see cref="BattleUnits.completeunitlist"/>
        /// iteration order. Index is stable within a single capture.
        /// </summary>
        IReadOnlyList<TacticalUnitObservation> AllUnits { get; }

        /// <summary>
        /// Indices into <see cref="AllUnits"/> for units where
        /// <c>Alliance == CapturedForAlliance</c>. Empty when no own-side units captured.
        /// </summary>
        IReadOnlyList<int> AlliedIndices { get; }

        /// <summary>
        /// Indices into <see cref="AllUnits"/> for units where
        /// <c>Alliance != CapturedForAlliance</c>. Empty when no enemy units captured.
        /// </summary>
        IReadOnlyList<int> EnemyIndices { get; }
    }
}
