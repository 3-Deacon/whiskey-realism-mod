namespace WhiskeyRealism.Tactical
{
    public enum TacticalWlGuardAction
    {
        ChargeInitiation = 0,
        ChargeCancellation = 1,
        FeudMovement = 2
    }

    public readonly struct TacticalWlGuardDecision
    {
        public TacticalWlGuardDecision(bool allow, string reason)
        {
            Allow = allow;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public bool Allow { get; }
        public string Reason { get; }
    }

    public static class TacticalWlActionGuard
    {
        public static TacticalWlGuardDecision Decide(
            bool configEnabled,
            bool dlcScenarioActive,
            TacticalWlGuardAction action,
            bool unitUnderCommander,
            bool groupUnderCommander,
            bool attachedUnitUnderCommander)
        {
            if (!configEnabled) return new TacticalWlGuardDecision(true, "config-disabled");
            if (!dlcScenarioActive) return new TacticalWlGuardDecision(true, "wl-inactive");
            if (action == TacticalWlGuardAction.ChargeCancellation)
                return new TacticalWlGuardDecision(true, "preserve-cancellation");

            if (unitUnderCommander) return new TacticalWlGuardDecision(false, "player-subordinate");
            if (groupUnderCommander) return new TacticalWlGuardDecision(false, "player-subordinate-group");
            if (attachedUnitUnderCommander) return new TacticalWlGuardDecision(false, "player-subordinate-attached");

            return new TacticalWlGuardDecision(true, "ai-chain");
        }
    }
}
