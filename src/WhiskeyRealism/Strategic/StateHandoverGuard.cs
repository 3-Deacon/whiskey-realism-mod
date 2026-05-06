namespace WhiskeyRealism.Strategic
{
    internal static class StateHandoverGuard
    {
        internal const float FriendlySupportThreshold = 0.65f;
        internal const float OpposingSupportCeiling = 0.35f;

        internal static bool AllowsHandover(
            int newAlliance,
            float unionSupport,
            float csaSupport,
            float friendlyThreshold = FriendlySupportThreshold,
            float opposingCeiling = OpposingSupportCeiling)
        {
            if (newAlliance == 0)
                return unionSupport >= friendlyThreshold && csaSupport <= opposingCeiling;
            if (newAlliance == 1)
                return csaSupport >= friendlyThreshold && unionSupport <= opposingCeiling;
            return false;
        }
    }
}
