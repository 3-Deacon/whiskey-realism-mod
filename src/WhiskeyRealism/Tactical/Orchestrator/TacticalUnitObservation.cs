using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-unit observation captured ONCE per <see cref="TacticalUnitObservationAggregate.Capture"/>
    /// call. All fields are immutable after capture. Alliance-aware:
    /// visibility / objective / waypoint fields are populated only when the
    /// captured unit's alliance matches the capture's <c>allianceId</c>;
    /// enemy units get cheap fields (position, strength, unittyp, routed
    /// flag) only. This matches the pre-refactor cost profile, which never
    /// invoked the visibility walk for enemy units.
    /// </summary>
    public readonly struct TacticalUnitObservation
    {
        public TacticalUnitObservation(
            int instanceId,
            int unittyp,
            int alliance,
            bool isRouted,
            float worldX,
            float worldZ,
            float strength,
            float groupOwnInRange,
            float groupAiGroup,
            bool hasCurrentSetObjective,
            int currentSetObjectiveId,
            float objectiveX,
            float objectiveZ,
            TacticalObjectiveType objectiveType,
            bool hasLastWaypoint,
            float lastWaypointX,
            float lastWaypointZ,
            float visibleEnemyStrength,
            bool hasVisibleEnemy,
            float fatigue01,
            float ammo01,
            int effectiveCommandLevel)
        {
            InstanceId = instanceId;
            Unittyp = unittyp;
            Alliance = alliance;
            IsRouted = isRouted;
            WorldX = worldX;
            WorldZ = worldZ;
            Strength = strength;
            GroupOwnInRange = groupOwnInRange;
            GroupAiGroup = groupAiGroup;
            HasCurrentSetObjective = hasCurrentSetObjective;
            CurrentSetObjectiveId = currentSetObjectiveId;
            ObjectiveX = objectiveX;
            ObjectiveZ = objectiveZ;
            ObjectiveType = objectiveType;
            HasLastWaypoint = hasLastWaypoint;
            LastWaypointX = lastWaypointX;
            LastWaypointZ = lastWaypointZ;
            VisibleEnemyStrength = visibleEnemyStrength;
            HasVisibleEnemy = hasVisibleEnemy;
            Fatigue01 = fatigue01;
            Ammo01 = ammo01;
            EffectiveCommandLevel = effectiveCommandLevel;
        }

        public int InstanceId { get; }
        public int Unittyp { get; }
        public int Alliance { get; }
        public bool IsRouted { get; }
        public float WorldX { get; }
        public float WorldZ { get; }
        public float Strength { get; }
        public float GroupOwnInRange { get; }
        public float GroupAiGroup { get; }
        public bool HasCurrentSetObjective { get; }
        public int CurrentSetObjectiveId { get; }
        public float ObjectiveX { get; }
        public float ObjectiveZ { get; }
        public TacticalObjectiveType ObjectiveType { get; }
        public bool HasLastWaypoint { get; }
        public float LastWaypointX { get; }
        public float LastWaypointZ { get; }
        public float VisibleEnemyStrength { get; }
        public bool HasVisibleEnemy { get; }
        public float Fatigue01 { get; }
        public float Ammo01 { get; }
        public int EffectiveCommandLevel { get; }
    }
}
