namespace WhiskeyRealism.Tactical.Operations
{
    public enum TacticalCommanderMode
    {
        Off = 0,
        MonitorOnly = 1,
        Active = 2,
    }

    public static class TacticalCommanderModePolicy
    {
        public static TacticalCommanderMode Parse(string raw, TacticalCommanderMode fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "off":
                    return TacticalCommanderMode.Off;
                case "monitoronly":
                case "monitor-only":
                case "monitor only":
                    return TacticalCommanderMode.MonitorOnly;
                case "active":
                    return TacticalCommanderMode.Active;
                default:
                    return fallback;
            }
        }

        public static bool RunsLedger(TacticalCommanderMode mode) =>
            mode == TacticalCommanderMode.MonitorOnly || mode == TacticalCommanderMode.Active;

        public static bool AllowsWrites(TacticalCommanderMode mode) =>
            mode == TacticalCommanderMode.Active;
    }
}
