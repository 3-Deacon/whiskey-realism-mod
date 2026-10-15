namespace WhiskeyRealism.Tactical.Battle
{
    // BattleUnits.SetWaypoint (91232-91640) returns without a word on several
    // paths; the executor checks the battle-relevant ones before calling it, so a
    // skipped order is reported with its reason instead of reading as Failed:
    //   91300  showmovementoptions && !CheckDLCWLControl — the executor always
    //          passes showmovementoptions: false, so this exit never applies;
    //   91331  garrisonreference != null (RollingText "no movement for garrisons");
    //   91338-91360  campaign readiness — IsCampaign only, never in battle;
    //   91362  onretreat || isrouted.
    // A unit that is not alive or not active (inactive reinforcement, removed) is
    // skipped on every channel. Garrisons are skipped only on the channels that
    // re-path the group (Move; Facing and Formation via SetGroupFormation, 91822).
    public static class SilentExitPrecheck
    {
        public static SkipReason Check(in UnitRecord group, OrderChannel channel)
        {
            UnitFlags flags = group.Flags;
            if ((flags & UnitFlags.Alive) == 0 || (flags & UnitFlags.Active) == 0) return SkipReason.Inactive;
            if ((flags & UnitFlags.Routed) != 0) return SkipReason.Routed;
            if ((flags & UnitFlags.OnRetreat) != 0) return SkipReason.OnRetreat;
            if ((flags & UnitFlags.Garrisoned) != 0 && RePathsGroup(channel)) return SkipReason.Garrison;
            return SkipReason.None;
        }

        private static bool RePathsGroup(OrderChannel channel)
        {
            return channel == OrderChannel.Move || channel == OrderChannel.Facing || channel == OrderChannel.Formation;
        }
    }
}
