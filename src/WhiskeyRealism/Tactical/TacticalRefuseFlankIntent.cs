namespace WhiskeyRealism.Tactical
{
    public static class TacticalRefuseFlankIntent
    {
        public enum Decision { NoRefuse, RefuseLeft, RefuseRight }
        public enum Posture { Offensive, Defensive }

        public struct Input
        {
            public float LeftFlankStrength;
            public float RightFlankStrength;
            public Posture SectorPosture;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Decision Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Decision.NoRefuse;
            if (input.SectorPosture != Posture.Defensive) return Decision.NoRefuse;

            const float threatRatio = 2f;
            if (input.LeftFlankStrength > input.RightFlankStrength * threatRatio)
                return Decision.RefuseLeft;
            if (input.RightFlankStrength > input.LeftFlankStrength * threatRatio)
                return Decision.RefuseRight;
            return Decision.NoRefuse;
        }
    }
}
