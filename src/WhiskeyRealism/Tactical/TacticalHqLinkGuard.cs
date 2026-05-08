namespace WhiskeyRealism.Tactical
{
    public static class TacticalHqLinkGuard
    {
        public static bool ShouldClearAutoGroupLink(
            bool modEnabled,
            bool newlyLinked,
            bool sourceIsGroupUnit,
            bool targetExists,
            bool sameHierarchy,
            bool sameNonRootParent,
            bool sameAiGroup)
        {
            if (!modEnabled) return false;
            if (!newlyLinked) return false;
            if (!sourceIsGroupUnit) return false;
            if (!targetExists) return false;
            if (sameHierarchy) return false;
            if (sameNonRootParent) return false;
            if (sameAiGroup) return false;

            return true;
        }
    }
}
