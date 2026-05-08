namespace WhiskeyRealism.Tactical
{
    public static class TacticalMoralePressure
    {
        public enum Result { Stable, UnderPressure, FallbackCandidate, WithdrawalCandidate, CollapseCandidate }

        public struct Input
        {
            public float CurrentMorale;
            public float BattleStartMorale;
            public bool BattleStartMoraleInitialized;
            public float FallbackThreshold;
            public int Outflanked;
            public float FriendlyRoutedNear;
            public float EnemyRoutedNear;
            public bool ReceivedFireFromClosestFar;
            public float CoverValue;
            public int CoverObject;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Result Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Result.Stable;
            if (!input.BattleStartMoraleInitialized || input.BattleStartMorale < 0f)
                return Result.Stable;

            if (input.CurrentMorale < input.FallbackThreshold)
                return Result.CollapseCandidate;

            bool fallback = input.CurrentMorale < input.FallbackThreshold * 1.2f && input.ReceivedFireFromClosestFar;
            bool noCover = input.CoverValue <= 0f || input.CoverObject == 3;
            if (fallback && input.Outflanked > 0 && noCover)
                return Result.WithdrawalCandidate;
            if (fallback)
                return Result.FallbackCandidate;

            float drop = input.BattleStartMorale - input.CurrentMorale;
            bool moralePressure = drop >= input.BattleStartMorale * 0.10f;
            if (input.Outflanked >= 1 || input.FriendlyRoutedNear > 0f || moralePressure)
                return Result.UnderPressure;

            return Result.Stable;
        }
    }
}
