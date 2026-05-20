using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-unit observation captured ONCE per <see cref="TacticalUnitObservationAggregate.Capture"/>
    /// call. All fields are immutable after capture. Alliance-aware:
    /// visibility / objective / waypoint fields are populated only when the
    /// captured unit's alliance matches the capture's <c>allianceId</c>;
    /// enemy units get cheap fields (position, strength, unittyp, routed
    /// flag, permanently-detached flag) only. This matches the pre-refactor
    /// cost profile, which never invoked the visibility walk for enemy units.
    ///
    /// Own-side-only fields: <see cref="HasCurrentSetObjective"/>,
    /// <see cref="HasLastWaypoint"/>, <see cref="HasVisibleEnemy"/>,
    /// <see cref="HasVisibleEnemyPosition"/>, <see cref="VisibleEnemyStrength"/>,
    /// <see cref="VisibleEnemyX"/>, <see cref="VisibleEnemyZ"/>,
    /// <see cref="VisibleEnemyUnittyp"/>, <see cref="VisibleEnemyName"/>,
    /// <see cref="VisibleEnemyRouted"/>, <see cref="VisibleEnemyPermanentlyDetached"/>,
    /// <see cref="Fatigue01"/>, <see cref="Ammo01"/>,
    /// <see cref="GroupEnemiesInRange"/>, <see cref="GroupAngleEnemyStrength"/>,
    /// <see cref="GroupFlankRisk"/>, <see cref="GroupStrongPoint"/>,
    /// <see cref="GroupVanillaStanceOrdered"/>.
    /// </summary>
    public readonly struct TacticalUnitObservation
    {
        public TacticalUnitObservation(
            int instanceId,
            int unittyp,
            int alliance,
            bool isRouted,
            bool permanentlyDetached,
            float worldX,
            float worldZ,
            float strength,
            float groupOwnInRange,
            float groupAiGroup,
            bool hasCurrentSetObjective,
            string objectiveName,
            float objectiveX,
            float objectiveZ,
            TacticalObjectiveType objectiveType,
            bool hasLastWaypoint,
            float lastWaypointX,
            float lastWaypointZ,
            float visibleEnemyStrength,
            bool hasVisibleEnemy,
            bool hasVisibleEnemyPosition,
            float visibleEnemyX,
            float visibleEnemyZ,
            float fatigue01,
            float ammo01,
            int effectiveCommandLevel,
            int visibleEnemyUnittyp = -1,
            string visibleEnemyName = null,
            bool visibleEnemyRouted = false,
            bool visibleEnemyPermanentlyDetached = false,
            float groupEnemiesInRange = 0f,
            float groupAngleEnemyStrength = 0f,
            bool groupFlankRisk = false,
            bool groupStrongPoint = false,
            int groupVanillaStanceOrdered = -1)
        {
            InstanceId = instanceId;
            Unittyp = unittyp;
            Alliance = alliance;
            IsRouted = isRouted;
            PermanentlyDetached = permanentlyDetached;
            WorldX = worldX;
            WorldZ = worldZ;
            Strength = strength;
            GroupOwnInRange = groupOwnInRange;
            GroupAiGroup = groupAiGroup;
            HasCurrentSetObjective = hasCurrentSetObjective;
            ObjectiveName = objectiveName ?? string.Empty;
            ObjectiveX = objectiveX;
            ObjectiveZ = objectiveZ;
            ObjectiveType = objectiveType;
            HasLastWaypoint = hasLastWaypoint;
            LastWaypointX = lastWaypointX;
            LastWaypointZ = lastWaypointZ;
            VisibleEnemyStrength = visibleEnemyStrength;
            HasVisibleEnemy = hasVisibleEnemy;
            HasVisibleEnemyPosition = hasVisibleEnemyPosition;
            VisibleEnemyX = visibleEnemyX;
            VisibleEnemyZ = visibleEnemyZ;
            Fatigue01 = fatigue01;
            Ammo01 = ammo01;
            EffectiveCommandLevel = effectiveCommandLevel;
            VisibleEnemyUnittyp = visibleEnemyUnittyp;
            VisibleEnemyName = visibleEnemyName ?? string.Empty;
            VisibleEnemyRouted = visibleEnemyRouted;
            VisibleEnemyPermanentlyDetached = visibleEnemyPermanentlyDetached;
            GroupEnemiesInRange = groupEnemiesInRange;
            GroupAngleEnemyStrength = groupAngleEnemyStrength;
            GroupFlankRisk = groupFlankRisk;
            GroupStrongPoint = groupStrongPoint;
            GroupVanillaStanceOrdered = groupVanillaStanceOrdered;
        }

        public int InstanceId { get; }
        public int Unittyp { get; }
        public int Alliance { get; }
        public bool IsRouted { get; }
        public bool PermanentlyDetached { get; }
        public float WorldX { get; }
        public float WorldZ { get; }
        public float Strength { get; }
        public float GroupOwnInRange { get; }
        public float GroupAiGroup { get; }
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
        public TacticalObjectiveType ObjectiveType { get; }
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
        public float Fatigue01 { get; }
        public float Ammo01 { get; }
        public int EffectiveCommandLevel { get; }

        // ---- Gap-fill Chunk 1: per-group sector fields (own-side only) ----
        // Captured so the worker thread's PopulateGroupDecisions can rebuild
        // TacticalGroupContactInput without touching live vanilla Regiment state.
        // All default to neutral values (0 / false / -1 / string.Empty) for
        // enemy observations and for own-side units where the source read failed.

        /// <summary>
        /// Closest-visible-enemy <c>Regiment.unittyp</c> (own-side only).
        /// Defaults to -1 when no visible enemy or read failed. Used by
        /// <see cref="TacticalGroupSectorEstimator.IsVisibleLineContact"/> to
        /// gate the line-contact confidence bump.
        /// </summary>
        public int VisibleEnemyUnittyp { get; }
        /// <summary>
        /// Closest-visible-enemy GameObject name (own-side only). Used by
        /// <see cref="TacticalGroupSectorEstimator.IsVisibleLineContact"/> to
        /// reject skirmisher contact (name contains "skirm"). Empty when no
        /// visible enemy or read failed.
        /// </summary>
        public string VisibleEnemyName { get; }
        /// <summary>Closest-visible-enemy routed flag (own-side only).</summary>
        public bool VisibleEnemyRouted { get; }
        /// <summary>Closest-visible-enemy permanently-detached flag (own-side only).</summary>
        public bool VisibleEnemyPermanentlyDetached { get; }
        /// <summary>
        /// <c>Regiment.groupenemiesinrange</c> (own-side only). Defaults to 0f
        /// for enemy observations.
        /// </summary>
        public float GroupEnemiesInRange { get; }
        /// <summary>
        /// Sum of <c>Regiment.unitrange.enemystrengthwithinangle[]</c>
        /// (own-side only). Precomputed at capture time so the worker doesn't
        /// have to touch the array. Defaults to 0f for enemy observations.
        /// </summary>
        public float GroupAngleEnemyStrength { get; }
        /// <summary>
        /// Precomputed flank-risk flag (own-side only). Mirrors
        /// <c>BattleGroupStancePatch.BuildGroupSector</c>:
        /// <c>flanksthreated &gt; 0f || outflanked &gt; 0</c>.
        /// </summary>
        public bool GroupFlankRisk { get; }
        /// <summary>
        /// Precomputed strong-point flag (own-side only). Mirrors
        /// <c>BattleGroupStancePatch.BuildGroupSector</c>:
        /// <c>covervalue &gt; 0.5f || fortinrange</c>.
        /// </summary>
        public bool GroupStrongPoint { get; }
        /// <summary>
        /// <c>Regiment.ai_stanceordered</c> at capture time (own-side only).
        /// Defaults to -1 for enemy observations or read failure. Consumed by
        /// the worker as <c>vanillaStance</c> input to
        /// <see cref="TacticalDoctrineScorer.DecideGroupStance"/>.
        /// </summary>
        public int GroupVanillaStanceOrdered { get; }
    }
}
