using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // One battlefield objective (vanilla Objectives, BattleUnits.objectives 78910) as read
    // by TacticalStableSceneCache: id and position are immutable for the battle; owner
    // (written on capture, 105746) and active state are read live at every capture.
    // owner == allianceId decides friendlyOwned at consumption, so one capture serves
    // both sides.
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
}
