using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Immutable per-side capture of all Unity-bound data BuildObjectiveRecordsFromAggregate
    // reads from the scene graph / vanilla statics. The main thread populates this at
    // heavy-gate fire (Unity API + reflection cost stays on main, where it is required);
    // the worker thread consumes it via BuildObjectiveRecordsFromContext to produce the
    // final ObjectiveRecord[] without touching any Unity API. All fields are pure data.
    //
    // Composition:
    //   - MapObjectives: from UnityEngine.Object.FindObjectsOfType<Objectives>() —
    //     stable scene objects, cached across cycles by ObjectiveRecordsCaptureCache.
    //   - ObjectiveChains: from AIBattle.objectivechain — mutable per-cycle (AI moves
    //     units between chains), so not cacheable; reflected fresh each capture.
    //   - ApproachAvenues: composed from cached EntryPoint snapshot, fresh
    //     BattleUnits.scheduledarrival reflection (mutable, small list), and cached
    //     deployment-group snapshot (stable). The avenue capture itself is the same
    //     pure-data array shape consumers already expect.
    internal sealed class ObjectiveRecordsCaptureContext
    {
        public IReadOnlyList<MapObjectiveCapture> MapObjectives { get; }
        public IReadOnlyList<ObjectiveChainCapture> ObjectiveChains { get; }
        // Array shape preserved because ApplyApproachAvenues consumes an array directly.
        // Avoid materializing per-call by keeping the wire form aligned to the consumer.
        public TacticalApproachAvenueObservation[] ApproachAvenues { get; }
        public int AllianceId { get; }

        public ObjectiveRecordsCaptureContext(
            IReadOnlyList<MapObjectiveCapture> mapObjectives,
            IReadOnlyList<ObjectiveChainCapture> objectiveChains,
            TacticalApproachAvenueObservation[] approachAvenues,
            int allianceId)
        {
            MapObjectives = mapObjectives ?? Array.Empty<MapObjectiveCapture>();
            ObjectiveChains = objectiveChains ?? Array.Empty<ObjectiveChainCapture>();
            ApproachAvenues = approachAvenues ?? Array.Empty<TacticalApproachAvenueObservation>();
            AllianceId = allianceId;
        }

        public static ObjectiveRecordsCaptureContext Empty { get; } = new ObjectiveRecordsCaptureContext(
            mapObjectives: Array.Empty<MapObjectiveCapture>(),
            objectiveChains: Array.Empty<ObjectiveChainCapture>(),
            approachAvenues: Array.Empty<TacticalApproachAvenueObservation>(),
            allianceId: -1);
    }

    // Map-objective capture: stable scene object extracted via FindObjectsOfType<Objectives>().
    // owner == allianceId determines friendlyOwned at consumption time (so one capture
    // serves both sides). The runtime cache key is (x, z, name) — the underlying scene
    // objects don't change ids across cycles.
    internal readonly struct MapObjectiveCapture
    {
        public string ObjectiveId { get; }
        public TacticalMapPoint Point { get; }
        public int Owner { get; }
        public bool IsActive { get; }

        public MapObjectiveCapture(string objectiveId, TacticalMapPoint point, int owner, bool isActive)
        {
            ObjectiveId = objectiveId ?? string.Empty;
            Point = point;
            Owner = owner;
            IsActive = isActive;
        }
    }

    // Objective-chain capture: from AIBattle.objectivechain[i]. The chain contents are
    // mutable per cycle (AI assigns units between chains), so this list is reflected
    // fresh each capture. Inner objectives within a chain are themselves objects with
    // a position; we extract just the fields the builder reads.
    internal readonly struct ObjectiveChainCapture
    {
        public int UsedStrategyId { get; }
        public IReadOnlyList<ObjectiveChainObjective> Objectives { get; }
        public float EnemyStrength { get; }
        public float FriendlyStrength { get; }

        public ObjectiveChainCapture(
            int usedStrategyId,
            IReadOnlyList<ObjectiveChainObjective> objectives,
            float enemyStrength,
            float friendlyStrength)
        {
            UsedStrategyId = usedStrategyId;
            Objectives = objectives ?? Array.Empty<ObjectiveChainObjective>();
            EnemyStrength = enemyStrength;
            FriendlyStrength = friendlyStrength;
        }
    }

    // Inner objective inside a chain — just position + id.
    internal readonly struct ObjectiveChainObjective
    {
        public string ObjectiveId { get; }
        public TacticalMapPoint Point { get; }

        public ObjectiveChainObjective(string objectiveId, TacticalMapPoint point)
        {
            ObjectiveId = objectiveId ?? string.Empty;
            Point = point;
        }
    }
}
