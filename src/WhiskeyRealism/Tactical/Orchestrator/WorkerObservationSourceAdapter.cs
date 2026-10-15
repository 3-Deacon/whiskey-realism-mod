using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Thin IObservationSource adapter wrapping a per-side TacticalUnitObservation[] from
    // TacticalOrchestratorWorkerInput. Pure data — built off-main on the worker thread.
    //
    // The input pipeline (chunk-1, 2026-05-19) already captures one observation array
    // per side, populated on main via TacticalUnitObservationAggregate.Shared.Capture
    // immediately after each side's heavy snapshot build. The arrays carry observations
    // for BOTH sides' regiments (the aggregate captures the full completeunitlist with
    // alliance metadata per unit); only the AlliedIndices / EnemyIndices partition
    // differs by which alliance we filter for.
    //
    // We build this adapter once per side per worker cycle (cheap O(N) partition on
    // immutable observation array). The pure ObjectiveRecord builder reads
    // AlliedIndices / EnemyIndices for its main loops.
    internal sealed class WorkerObservationSourceAdapter : IObservationSource
    {
        public int Count { get; }
        public int CapturedForAlliance { get; }
        public IReadOnlyList<TacticalUnitObservation> AllUnits { get; }
        public IReadOnlyList<int> AlliedIndices { get; }
        public IReadOnlyList<int> EnemyIndices { get; }

        private WorkerObservationSourceAdapter(
            IReadOnlyList<TacticalUnitObservation> allUnits,
            IReadOnlyList<int> alliedIndices,
            IReadOnlyList<int> enemyIndices,
            int capturedForAlliance)
        {
            AllUnits = allUnits ?? Array.Empty<TacticalUnitObservation>();
            AlliedIndices = alliedIndices ?? Array.Empty<int>();
            EnemyIndices = enemyIndices ?? Array.Empty<int>();
            Count = AllUnits.Count;
            CapturedForAlliance = capturedForAlliance;
        }

        // Builds an adapter for the given side index (0 or 1) using the captured
        // observations on that side. Returns null when no observations were captured
        // (suppressed side / first-tick race) so the caller can short-circuit.
        public static WorkerObservationSourceAdapter Build(TacticalOrchestratorWorkerInput input, int sideIdx)
        {
            if (input == null) return null;

            IReadOnlyList<TacticalUnitObservation> all = sideIdx == 0
                ? input.Side0Observations
                : input.Side1Observations;
            int allianceId = sideIdx == 0
                ? input.Side0AllianceId
                : input.Side1AllianceId;

            if (all == null || all.Count == 0) return null;

            var alliedIndices = new List<int>(all.Count);
            var enemyIndices = new List<int>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                var obs = all[i];
                if (obs.Alliance == allianceId) alliedIndices.Add(i);
                else if (obs.Alliance >= 0) enemyIndices.Add(i);
            }

            return new WorkerObservationSourceAdapter(
                all,
                alliedIndices,
                enemyIndices,
                allianceId);
        }
    }
}
