using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// BC14O (requirement-main-effort-objective): the objective the army's main effort is on, per alliance, so a rival must beat it by
    /// <see cref="ObjectiveScoring.ObjectiveSwitchMargin"/> to take it. The id is the director's own string and the point is where the objective
    /// stands, for the front-axis band the plan's main effort follows. The director sets it with each pick; the army clears it when a new plan is
    /// picked and on a new battle day. Per battle (<see cref="BattleScope"/>).
    /// </summary>
    public sealed class ObjectiveLatch
    {
        private static readonly ObjectiveLatch[] ByAlliance = { new ObjectiveLatch(), new ObjectiveLatch() };

        static ObjectiveLatch()
        {
            BattleScope.Register("objective-latch", ResetAll);
        }

        private static void ResetAll()
        {
            for (int i = 0; i < ByAlliance.Length; i++) ByAlliance[i].Clear();
        }

        /// <summary>The alliance's latch; null outside 0 and 1.</summary>
        public static ObjectiveLatch ForAlliance(int alliance) => alliance >= 0 && alliance < ByAlliance.Length ? ByAlliance[alliance] : null;

        /// <summary>The main-effort objective's id, null when none is latched.</summary>
        public string Id { get; private set; }

        public float X { get; private set; }

        public float Z { get; private set; }

        public bool Has => Id != null;

        public void Set(string id, float x, float z)
        {
            Id = id;
            X = x;
            Z = z;
        }

        public void Clear()
        {
            Id = null;
            X = 0f;
            Z = 0f;
        }
    }
}
