namespace WhiskeyRealism.Tactical.Battle
{
    // The arrival re-path decision of #105 (ReinforcementArrivalGuardPatch), pure.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   CheckReinforcementArrivals 87554-87604 -> SetGroupArrival 87606-87663: each queued
    //     unit of a reinforcement group becomes active in turn, and every arrival calls
    //     SetInitialPathForEntryPoint(group, entry, ruleoutmanuallymoved: true) (87662).
    //   SetInitialPathForEntryPoint 87967-88003: SetGroupFormation(group, formation 1,
    //     entry trooptargetposition +-20 m) re-paths every non-detached, non-routed child
    //     (92076-92108). The only shield is ruleoutmanuallymoved, which detaches members
    //     with alreadymanuallymoved for the call (87991-88003); that flag is written only
    //     by the player UI (SetManuallyMoved 138854-138859).
    //
    // Select names the arrived members of an arriving group that a Whiskey order is
    // walking: the member, or a command ancestor strictly below the arriving group, holds
    // a live executor Move (last Move outcome Issued or Moved), and the member still has a
    // path (RegimentPaths > 0), so the entry re-path would overwrite our order. Step A
    // only sizes that overwrite (TacticalArrivalRepath rows); step B would set vanilla's
    // alreadymanuallymoved on exactly these members, so the decision is shared.
    //
    // Never selected: the arriving group itself (vanilla re-paths it unconditionally), a
    // player-owned or feuding unit (the executor never orders one), a routed, inactive or
    // permanently detached member (vanilla's call skips those anyway, 92076-92108).
    //
    // An executor Move to a member always postdates that member's arrival: units are
    // inactive until SetUnitActiveState (87626), and the snapshot lists only active ones.
    public static class ArrivalShield
    {
        /// <summary>
        /// Writes the own indices of the ordered members into <paramref name="into"/> (up to
        /// its length) and returns how many were written.
        /// </summary>
        public static int Select(IBattleUnitView s, int arrivingGroupIndex, OrderExecutor executor, int[] into)
        {
            if (s == null || executor == null || into == null || into.Length == 0) return 0;
            if (arrivingGroupIndex < 0 || arrivingGroupIndex >= s.OwnCount) return 0;
            UnitRecord[] own = s.Own;
            int count = 0;
            for (int m = 0; m < s.OwnCount && count < into.Length; m++)
            {
                if (m == arrivingGroupIndex || !s.IsAncestorOf(arrivingGroupIndex, m)) continue;
                if (!Eligible(own[m]) || own[m].RegimentPaths <= 0) continue;
                if (!HoldsLiveMove(s, executor, arrivingGroupIndex, m)) continue;
                into[count++] = m;
            }
            return count;
        }

        private static bool Eligible(in UnitRecord unit)
        {
            UnitFlags f = unit.Flags;
            if ((f & (UnitFlags.Alive | UnitFlags.Active)) != (UnitFlags.Alive | UnitFlags.Active)) return false;
            return (f & (UnitFlags.Routed | UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.PermanentlyDetached)) == 0;
        }

        // The member itself, or any command ancestor between it and the arriving group
        // (exclusive), holds a live executor Move.
        private static bool HoldsLiveMove(IBattleUnitView s, OrderExecutor executor, int arrivingGroupIndex, int member)
        {
            UnitRecord[] own = s.Own;
            int current = member;
            for (int hops = 0; current >= 0 && current < s.OwnCount && current != arrivingGroupIndex && hops < s.OwnCount; hops++)
            {
                if (executor.TryGetLastOutcome(own[current].InstanceId, OrderChannel.Move, out OrderOutcome outcome) &&
                    (outcome.Kind == OrderOutcomeKind.Issued || outcome.Kind == OrderOutcomeKind.Moved))
                    return true;
                current = own[current].ParentIndex;
            }
            return false;
        }
    }
}
