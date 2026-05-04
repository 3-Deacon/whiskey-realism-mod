namespace WhiskeyRealism.Strategic
{
    internal static class DifficultyPersonalityModifier
    {
        internal const int HistoricalHardDifficultyIndex = 3;
        internal const float HistoricalHardCasualtyToleranceBonus = 0.10f;

        internal static PersonalityVector ForLockedHistoricalDifficulty(
            bool overrideVanillaSettings,
            int lockedDifficultyIndex)
        {
            if (!overrideVanillaSettings)
                return default(PersonalityVector);

            if (lockedDifficultyIndex != HistoricalHardDifficultyIndex)
                return default(PersonalityVector);

            return new PersonalityVector(
                agg: 0f,
                caut: 0f,
                aud: 0f,
                cas: HistoricalHardCasualtyToleranceBonus,
                pol: 0f);
        }
    }
}
