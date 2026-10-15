using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// One side's perceived battle as the objective-record and evidence sub-builders read it
    /// (plan D11). The implementation is the per-tick <see cref="BattleSnapshot"/>: own units
    /// are the side's true roster, enemies exist only as the snapshot's visible and remembered
    /// observations (vanilla AI-side fog, BC2a). Harness tests build snapshots directly.
    /// </summary>
    public interface IObservationSource
    {
        /// <summary>Own units, valid on [0, <see cref="OwnCount"/>).</summary>
        int OwnCount { get; }
        UnitRecord[] Own { get; }

        /// <summary>Visible and remembered enemies, valid on [0, <see cref="ObservationCount"/>).</summary>
        int ObservationCount { get; }
        EnemyObservation[] Observations { get; }

        /// <summary>Own index of a GameObject instance id, or -1.</summary>
        int IndexOfOwn(int instanceId);

        /// <summary>Observation index of the own unit's closest visible enemy (true XZ distance), or -1.</summary>
        int ClosestVisibleEnemy(int ownIndex, out float distance);
    }
}
