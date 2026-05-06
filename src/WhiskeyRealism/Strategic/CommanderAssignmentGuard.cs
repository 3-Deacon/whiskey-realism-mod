namespace WhiskeyRealism.Strategic
{
    internal static class CommanderAssignmentGuard
    {
        internal static bool ShouldClearPreviousCommand(
            bool priorCommandExists,
            bool priorIsAssignedTarget,
            int priorCommandCommanderId,
            int assignedCommanderId)
        {
            return priorCommandExists
                && !priorIsAssignedTarget
                && assignedCommanderId >= 0
                && priorCommandCommanderId == assignedCommanderId;
        }
    }
}
