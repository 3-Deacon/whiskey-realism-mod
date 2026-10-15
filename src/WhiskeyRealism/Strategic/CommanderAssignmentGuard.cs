namespace WhiskeyRealism.Strategic
{
    internal enum PreviousCommandAction { None, ReplaceViaVanilla }

    internal static class CommanderAssignmentGuard
    {
        // A commander moved by AssignCommando (decompile 60343) leaves his previous
        // unit still pointing at him. Refill it the way vanilla refills any vacated
        // command - BattleUnits.ReplaceCommanderOfUnit (81389), as
        // ArmyGroup.AppointCommander does (75575) - instead of orphaning it with
        // commander = -1. ReplaceCommanderOfUnit re-enters AssignCommando, so a
        // replacement already running is left alone.
        internal static PreviousCommandAction ResolvePreviousCommandAction(bool staleReference, bool replacementInProgress)
        {
            if (!staleReference || replacementInProgress) return PreviousCommandAction.None;
            return PreviousCommandAction.ReplaceViaVanilla;
        }

        internal static bool ShouldClearPreviousCommand(
            bool priorCommandExists,
            bool priorIsAssignedTarget,
            int priorCommandCommanderId,
            int assignedCommanderId,
            bool vanillaReplacementWillReadPriorCommander = false)
        {
            return priorCommandExists
                && !priorIsAssignedTarget
                && assignedCommanderId >= 0
                && priorCommandCommanderId == assignedCommanderId
                && !vanillaReplacementWillReadPriorCommander;
        }
    }
}
