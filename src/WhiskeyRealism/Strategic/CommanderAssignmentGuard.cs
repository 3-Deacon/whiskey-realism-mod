namespace WhiskeyRealism.Strategic
{
    internal enum PreviousCommandAction { None, ReplaceViaVanilla }

    internal enum ReplacementAppointment { Defer, AppointDirect, ReseatThenAppoint }

    internal static class CommanderAssignmentGuard
    {
        // A commander moved by AssignCommando (decompile 60343) leaves his previous
        // unit still pointing at him. Refill it the way vanilla refills any vacated
        // command - BattleUnits.ReplaceCommanderOfUnit (81389), as
        // ArmyGroup.AppointCommander does (75575) - instead of orphaning it with
        // commander = -1. ReplaceCommanderOfUnit re-enters AssignCommando, so a
        // replacement already running is left alone.
        // vanillaPickable = Commander.CanBePickedForAIReplacement(alliance, true) (60748).
        // It rejects any sitting army-group commander, but vanilla seats historical
        // figures on auto-created groups early (CreateNewArmyGroup 75653). Such a
        // commander may move only if he is otherwise free (IsAvailable, not serving
        // under the player, not staff, not CIC) AND his old group can be reseated
        // from vanilla's pool (GetAvailableArmyGroupCommanders 75546) -
        // AppointCommander (75559) never clears the old group's commanderid.
        // CommanderAppointmentRuntime's live check (stage G2 succession reseat).
        internal static ReplacementAppointment ResolveReplacementAppointment(bool vanillaPickable, bool leadsOtherArmyGroup, bool freeApartFromArmyGroup, bool successorAvailable)
        {
            if (vanillaPickable) return ReplacementAppointment.AppointDirect;
            if (leadsOtherArmyGroup && freeApartFromArmyGroup && successorAvailable)
                return ReplacementAppointment.ReseatThenAppoint;
            return ReplacementAppointment.Defer;
        }

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
