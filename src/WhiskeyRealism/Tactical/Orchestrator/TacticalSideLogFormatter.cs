namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class TacticalSideLogFormatter
    {
        public static string Format(
            int allianceId,
            int playerAllianceId,
            bool aiVsAi,
            string topCommandId,
            string topCommandName,
            string topCommandLevel,
            string rootCommandId,
            string rootCommandName)
        {
            return "side=" + allianceId
                + " alliance=" + allianceId
                + " playerAlliance=" + playerAllianceId
                + " relation=" + ResolveRelation(allianceId, playerAllianceId, aiVsAi)
                + " topCommandId=" + Safe(topCommandId)
                + " topCommandName=" + Safe(topCommandName)
                + " topCommandLevel=" + Safe(topCommandLevel)
                + " rootCommandId=" + Safe(rootCommandId)
                + " rootCommandName=" + Safe(rootCommandName);
        }

        private static string ResolveRelation(int allianceId, int playerAllianceId, bool aiVsAi)
        {
            if (aiVsAi) return "ai-vs-ai";
            if (playerAllianceId < 0) return "unknown";
            return allianceId == playerAllianceId ? "player" : "opponent";
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "<unresolved>"
                : value.Trim().Replace(' ', '_');
        }
    }
}
