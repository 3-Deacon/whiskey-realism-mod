namespace WhiskeyRealism.Tactical
{
    public static class TacticalGateHelpers
    {
        public static bool PassesWlOwnership(int aiFeudStance, int isPlayerAiOrFeud)
            => aiFeudStance == -1 || isPlayerAiOrFeud == 2;
    }
}
