namespace WhiskeyRealism.Strategic
{
    internal static class FleetPatrolResetGuard
    {
        internal static bool ShouldResetAiPatrolToIdle(
            bool isPlayerFleet,
            int unitType,
            int fleetOrders,
            int regimentPaths,
            float distanceToStart,
            float completionRadius,
            bool isRouted,
            bool onRetreat,
            bool inBattle,
            bool withinRotationProcess)
        {
            return !isPlayerFleet
                && unitType == 17
                && fleetOrders == 2
                && regimentPaths <= 0
                && distanceToStart <= completionRadius
                && !isRouted
                && !onRetreat
                && !inBattle
                && !withinRotationProcess;
        }
    }
}
