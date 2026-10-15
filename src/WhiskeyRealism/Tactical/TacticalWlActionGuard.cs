namespace WhiskeyRealism.Tactical
{
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
            bool unitUnderCommander,
            bool groupUnderCommander,
            bool attachedUnitUnderCommander)
        {
            if (!configEnabled) return new TacticalWlGuardDecision(true, "config-disabled");
            if (!dlcScenarioActive) return new TacticalWlGuardDecision(true, "wl-inactive");
            if (unitUnderCommander) return new TacticalWlGuardDecision(false, "player-subordinate");
            if (groupUnderCommander) return new TacticalWlGuardDecision(false, "player-subordinate-group");
            if (attachedUnitUnderCommander) return new TacticalWlGuardDecision(false, "player-subordinate-attached");

            return new TacticalWlGuardDecision(true, "ai-chain");
        }
    }
}
