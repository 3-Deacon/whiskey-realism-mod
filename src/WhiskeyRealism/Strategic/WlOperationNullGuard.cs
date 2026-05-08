namespace WhiskeyRealism.Strategic
{
    public static class WlOperationNullGuard
    {
        public static bool ShouldFinishMissingOperation(bool modEnabled, bool operationExists, bool usedTopGroupExists)
        {
            return modEnabled && operationExists && !usedTopGroupExists;
        }
    }
}
