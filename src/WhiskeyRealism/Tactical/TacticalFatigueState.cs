namespace WhiskeyRealism.Tactical
{
    public static class TacticalFatigueState
    {
        public enum Result { Fresh, Tiring, Spent, Exhausted }

        public static Result Score(float fatigue)
        {
            if (fatigue < 0.25f) return Result.Fresh;
            if (fatigue < 0.55f) return Result.Tiring;
            if (fatigue < 0.80f) return Result.Spent;
            return Result.Exhausted;
        }
    }
}
