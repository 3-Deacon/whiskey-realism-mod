namespace WhiskeyRealism.Tactical
{
    public static class TacticalSupportScreen
    {
        public enum Result { Screened, Shaken, Unsupported, Unknown }

        public struct Input
        {
            public float ProtectedUnitMorale;
            public float MoraleFallbackThreshold;
            public float BattleStartMorale;
            public float EnemyDistance;
            public float DangerRadius;
            public int ScreenUnitCount;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Result Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Result.Unknown;
            if (input.BattleStartMorale < 0f)
                return Result.Unknown;

            bool enemyClose = input.EnemyDistance <= input.DangerRadius;
            bool screenPresent = input.ScreenUnitCount > 0;
            bool moraleSteady = input.ProtectedUnitMorale >= input.MoraleFallbackThreshold;

            if (screenPresent && moraleSteady) return Result.Screened;
            if (screenPresent && !moraleSteady) return Result.Shaken;
            if (enemyClose && !screenPresent) return Result.Unsupported;
            return Result.Screened;
        }
    }
}
