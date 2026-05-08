namespace WhiskeyRealism.Tactical
{
    public static class TacticalGateHelpers
    {
        public static bool PassesWlOwnership(int aiFeudStance, int isPlayerAiOrFeud)
            => aiFeudStance == -1 || isPlayerAiOrFeud == 2;

        public static bool IsValidAllianceIndex(int allianceId, int factionLength)
            => allianceId >= 0 && allianceId < factionLength;
    }
}
