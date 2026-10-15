using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-unit observation captured ONCE per <see cref="TacticalUnitObservationAggregate.Capture"/>
    /// call. All fields are immutable after capture. Alliance-aware:
    /// visibility / objective / waypoint fields are populated only when the
    /// captured unit's alliance matches the capture's <c>allianceId</c>;
    /// enemy units get cheap fields (strength, routed flag,
    /// permanently-detached flag) only. This matches the pre-refactor
    /// cost profile, which never invoked the visibility walk for enemy units.
    ///
    /// Own-side-only fields: <see cref="HasCurrentSetObjective"/>,
    /// <see cref="HasLastWaypoint"/>, <see cref="HasVisibleEnemy"/>,
    /// <see cref="HasVisibleEnemyPosition"/>, <see cref="VisibleEnemyStrength"/>,
    /// <see cref="VisibleEnemyX"/>, <see cref="VisibleEnemyZ"/>.
    /// </summary>
    public readonly struct TacticalUnitObservation
    {
        public TacticalUnitObservation(
            int instanceId,
            int alliance,
            bool isRouted,
            bool permanentlyDetached,
            float strength,
            bool hasCurrentSetObjective,
            string objectiveName,
            float objectiveX,
            float objectiveZ,
            bool hasLastWaypoint,
            float lastWaypointX,
            float lastWaypointZ,
            float visibleEnemyStrength,
            bool hasVisibleEnemy,
            bool hasVisibleEnemyPosition,
            float visibleEnemyX,
            float visibleEnemyZ)
        {
            InstanceId = instanceId;
            Alliance = alliance;
            IsRouted = isRouted;
            PermanentlyDetached = permanentlyDetached;
            Strength = strength;
            HasCurrentSetObjective = hasCurrentSetObjective;
            ObjectiveName = objectiveName ?? string.Empty;
            ObjectiveX = objectiveX;
            ObjectiveZ = objectiveZ;
            HasLastWaypoint = hasLastWaypoint;
            LastWaypointX = lastWaypointX;
            LastWaypointZ = lastWaypointZ;
            VisibleEnemyStrength = visibleEnemyStrength;
            HasVisibleEnemy = hasVisibleEnemy;
            HasVisibleEnemyPosition = hasVisibleEnemyPosition;
            VisibleEnemyX = visibleEnemyX;
            VisibleEnemyZ = visibleEnemyZ;
        }

        public int InstanceId { get; }
        public int Alliance { get; }
        public bool IsRouted { get; }
        public bool PermanentlyDetached { get; }
        public float Strength { get; }
        public bool HasCurrentSetObjective { get; }
        /// <summary>
        /// Raw objective name string captured via reflection on <c>objectivename</c>.
        /// Empty string when the objective is nameless or unreadable. Use
        /// <see cref="HasCurrentSetObjective"/> as the presence flag — this field
        /// is empty for both nameless objectives and no-objective cases.
        /// Task 8's BuildObjectiveRecordsFromAggregate reconstructs the legacy id
        /// formula (<c>string.IsNullOrWhiteSpace(name) ? "objective-" + observations.Count : name</c>)
        /// at consumption time to produce bit-identical id streams.
        /// </summary>
        public string ObjectiveName { get; }
        public float ObjectiveX { get; }
        public float ObjectiveZ { get; }
        public bool HasLastWaypoint { get; }
        public float LastWaypointX { get; }
        public float LastWaypointZ { get; }
        public float VisibleEnemyStrength { get; }
        public bool HasVisibleEnemy { get; }
        /// <summary>
        /// <c>true</c> when <see cref="HasVisibleEnemy"/> is true AND the
        /// closest visible enemy's world position was successfully read and is
        /// not at the map origin. Separate from <see cref="HasVisibleEnemy"/> to
        /// handle the edge case where the enemy regiment was found but its
        /// transform was destroyed or returned a zero-origin position.
        /// </summary>
        public bool HasVisibleEnemyPosition { get; }
        /// <summary>World X position of the closest visible enemy regiment (own-side only).</summary>
        public float VisibleEnemyX { get; }
        /// <summary>World Z position of the closest visible enemy regiment (own-side only).</summary>
        public float VisibleEnemyZ { get; }
    }
}
