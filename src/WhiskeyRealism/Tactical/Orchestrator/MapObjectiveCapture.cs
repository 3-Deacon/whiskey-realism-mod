using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // One battlefield objective (vanilla Objectives, BattleUnits.objectives 78910) as read
    // by TacticalStableSceneCache: id and position are immutable for the battle; owner
    // (written on capture, 105746), points (zeroed when a Touch or timed Hold objective resolves,
    // 105726-105735) and the active flags are read live at every capture.
    // owner == allianceId decides friendlyOwned at consumption, so one capture serves
    // both sides.
    //
    // Two liveness flags, by design: IsActive (isActiveAndEnabled) is the shipped gate on the director's
    // observation list and stays so; LiveInHierarchy (gameObject.activeInHierarchy) is the test vanilla
    // itself applies to an objective (CheckIfObjectivesTaken 7377), and BC14O's value and objective table use it.
    internal readonly struct MapObjectiveCapture
    {
        public string ObjectiveId { get; }
        public TacticalMapPoint Point { get; }
        public int Owner { get; }
        public bool IsActive { get; }

        /// <summary>Objectives.points, read live: public, the same for both sides.</summary>
        public int Points { get; }

        /// <summary>gameObject.activeInHierarchy, read live. A capture built without it is as live as <see cref="IsActive"/> says.</summary>
        public bool LiveInHierarchy { get; }

        public MapObjectiveCapture(string objectiveId, TacticalMapPoint point, int owner, bool isActive, int points = 0, bool? liveInHierarchy = null)
        {
            ObjectiveId = objectiveId ?? string.Empty;
            Point = point;
            Owner = owner;
            IsActive = isActive;
            Points = points;
            LiveInHierarchy = liveInHierarchy ?? isActive;
        }
    }
}
