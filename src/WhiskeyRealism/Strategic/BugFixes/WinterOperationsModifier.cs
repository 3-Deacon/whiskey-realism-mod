namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-LOG-004. AICampaign.CheckOffensiveMovements scales its offensive roll by
    // GamePrefs.winterdefensiveoperationsmodifier in winter months (decompile
    // 14310-14312). winteroffensiveoperationsmodifier ("Probability modifier for
    // offensive operations in winter", campaignprefs 1546, 0.05) is loaded (56381) and
    // never read; the defensive one (1548, 0.2) is meant for CheckForDefensiveOperations
    // (13565-13567).
    internal static class WinterOperationsModifier
    {
        // Vanilla's winter test (14310, 13565): month <= winterquartersmonthuntil or
        // month >= winterquartersmonthfrom.
        internal static bool IsWinterMonth(int month, int untilMonth, int fromMonth)
        {
            return month <= untilMonth || month >= fromMonth;
        }

        internal static float OffensiveRollModifier(bool winter, bool fixEnabled, float offensiveModifier, float defensiveModifier)
        {
            if (!winter) return 1f;
            return fixEnabled ? offensiveModifier : defensiveModifier;
        }
    }
}
