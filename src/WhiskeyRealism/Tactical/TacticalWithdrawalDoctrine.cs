namespace WhiskeyRealism.Tactical
{
    public static class TacticalWithdrawalDoctrine
    {
        public enum Decision { HoldLine, Stabilize, Screen, RearGuard, FullRetreat }

        public struct Input
        {
            public TacticalMoralePressure.Result MoralePressure;
            public bool RearPressureFlag;
            public TacticalFatigueState.Result Fatigue;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Decision Score(in Input input)
        {
            // W&L gate: safe default is HoldLine (no write).
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Decision.HoldLine;

            // Base ladder from morale pressure.
            Decision baseDecision;
            switch (input.MoralePressure)
            {
                case TacticalMoralePressure.Result.Stable:
                    baseDecision = Decision.HoldLine;
                    break;
                case TacticalMoralePressure.Result.UnderPressure:
                    baseDecision = Decision.Stabilize;
                    break;
                case TacticalMoralePressure.Result.FallbackCandidate:
                    baseDecision = Decision.Screen;
                    break;
                case TacticalMoralePressure.Result.WithdrawalCandidate:
                    baseDecision = Decision.RearGuard;
                    break;
                case TacticalMoralePressure.Result.CollapseCandidate:
                    baseDecision = Decision.FullRetreat;
                    break;
                default:
                    baseDecision = Decision.HoldLine;
                    break;
            }

            // Rear pressure + tired/spent/exhausted bumps the ladder one step.
            bool tiredOrWorse = input.Fatigue == TacticalFatigueState.Result.Spent
                || input.Fatigue == TacticalFatigueState.Result.Exhausted;
            if (input.RearPressureFlag && tiredOrWorse)
            {
                baseDecision = BumpUpOne(baseDecision);
            }

            return baseDecision;
        }

        private static Decision BumpUpOne(Decision d)
        {
            switch (d)
            {
                case Decision.HoldLine: return Decision.Stabilize;
                case Decision.Stabilize: return Decision.Screen;
                case Decision.Screen: return Decision.RearGuard;
                case Decision.RearGuard: return Decision.FullRetreat;
                case Decision.FullRetreat: return Decision.FullRetreat;
                default: return d;
            }
        }
    }
}
