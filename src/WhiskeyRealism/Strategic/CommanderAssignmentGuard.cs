using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    internal readonly struct ArmyGroupRoleCandidate
    {
        internal readonly int Index;
        internal readonly Theater Theater;
        internal readonly string AreaKey;
        internal readonly float Strength;
        internal readonly bool IsPlayerGroup;

        internal ArmyGroupRoleCandidate(int index, Theater theater, string areaKey, float strength, bool isPlayerGroup)
        {
            Index = index;
            Theater = theater;
            AreaKey = areaKey;
            Strength = strength;
            IsPlayerGroup = isPlayerGroup;
        }
    }

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

        // SuccessionScheduler.Event.ReplacedRole values: ANV, AoP, Western,
        // Valley, GeneralInChief. GeneralInChief is GameVars.alliance[a]
        // .commanderinchief (no public setter beyond scenario init), not an
        // ArmyGroup command, so no army-group swap resolves for it.
        internal static bool RoleIsArmyGroupCommand(string role)
        {
            switch (role)
            {
                case "ANV":
                case "AoP":
                case "Western":
                case "Valley":
                    return true;
                default:
                    return false;
            }
        }

        // Resolve the army group a scripted succession displaces by role and
        // theater instead of "first army-group commander by index". The W&L
        // player's own group is never a target. Main eastern armies prefer a
        // non-Valley group; Valley requires a Shenandoah group; Western takes the
        // largest western group. Returns the candidate Index or -1 (defer).
        internal static int ResolveRoleTargetGroup(string role, IList<ArmyGroupRoleCandidate> groups)
        {
            if (!RoleIsArmyGroupCommand(role) || groups == null) return -1;

            switch (role)
            {
                case "ANV":
                case "AoP":
                {
                    int main = Largest(groups, g => g.Theater == Theater.East && g.AreaKey != ValleyArea);
                    return main >= 0 ? main : Largest(groups, g => g.Theater == Theater.East);
                }
                case "Valley":
                    return Largest(groups, g => g.Theater == Theater.East && g.AreaKey == ValleyArea);
                case "Western":
                    return Largest(groups, g => g.Theater == Theater.West);
                default:
                    return -1;
            }
        }

        private const string ValleyArea = "ShenandoahValley";

        private static int Largest(IList<ArmyGroupRoleCandidate> groups, System.Func<ArmyGroupRoleCandidate, bool> filter)
        {
            int best = -1;
            float bestStrength = float.MinValue;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g.IsPlayerGroup || !filter(g)) continue;
                if (g.Strength > bestStrength)
                {
                    bestStrength = g.Strength;
                    best = g.Index;
                }
            }
            return best;
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
