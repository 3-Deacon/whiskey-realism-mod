namespace WhiskeyRealism.Tactical
{
    public static class TacticalChargeViability
    {
        public enum Result { Refuse, Allow, Encourage }

        public struct Input
        {
            public float ChargeScore;
            public float ScoreThreshold;
            public float TargetMorale;
            public float TargetMoraleThreshold;
            public int TargetUnitTyp;
            public float DistanceToTarget;
            public float MaxChargeRadius;
            public float TimeSinceLastCharge;
            public float ChargeCooldown;
            public float VolleyDwellRemaining;
            public int TargetOutflanked;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Result Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Result.Refuse;
            if (input.TimeSinceLastCharge < input.ChargeCooldown) return Result.Refuse;
            if (input.DistanceToTarget > input.MaxChargeRadius) return Result.Refuse;
            if (input.VolleyDwellRemaining > 0f) return Result.Refuse;
            if (input.ChargeScore < input.ScoreThreshold) return Result.Refuse;

            bool moralePass = input.TargetUnitTyp == 2
                || input.TargetMorale < input.TargetMoraleThreshold;
            if (!moralePass) return Result.Refuse;

            float margin = input.ChargeScore / input.ScoreThreshold;
            bool wideMargin = margin >= 1.25f;
            bool soft = input.TargetOutflanked >= 3 || input.TargetMorale < input.TargetMoraleThreshold * 0.5f;
            if (wideMargin && soft) return Result.Encourage;
            return Result.Allow;
        }
    }
}
