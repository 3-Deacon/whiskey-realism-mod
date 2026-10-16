namespace WhiskeyRealism.Strategic.Planner
{
    // Where the held main objective sits, for the battle-replan gate (P4).
    internal sealed class BattleReplanQuery
    {
        public bool HasTarget;
        public float TargetX;
        public float TargetZ;
        public Theater TargetTheater = Theater.Unknown;
        public Theater BattleTheater = Theater.Unknown;
        public float Radius;
    }

    // P4's decisive-battle trigger, read by TheaterPlanner.Plan.
    internal static class PlanTriggers
    {
        // P4: a finished land battle only invalidates a plan when it was decisive
        // (vanilla battleresulttype 1 "glorious victory" / 3 "disaster"; decompile
        // FinishLandBattle 21874-21886 maps endbattle 0/8 -> 1/3 for AI-vs-AI battles too,
        // so magnitude does not depend on the player's alliance) AND it was fought in the
        // target's theater or within the near-target radius. Everything else is history only.
        internal static bool BattleInvalidatesPlan(BattleHistoryRecord record, BattleReplanQuery query)
        {
            if (record == null || query == null || !query.HasTarget) return false;
            if (!record.IsLandBattle || !record.IsMajorResult) return false;

            if (query.TargetTheater != Theater.Unknown && query.BattleTheater == query.TargetTheater)
                return true;

            float dx = record.PositionX - query.TargetX;
            float dz = record.PositionZ - query.TargetZ;
            return query.Radius > 0f && dx * dx + dz * dz <= query.Radius * query.Radius;
        }
    }
}
