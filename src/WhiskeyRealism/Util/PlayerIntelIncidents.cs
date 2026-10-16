namespace WhiskeyRealism.Util
{
    // The W&L player-intelligence incidents that make FogOfWar.UnitIsHidden answer "visible" for every position
    // (101183-101186): 60 Lost Orders (battle), 61 and 64 Scouting Ahead (campaign). IsIncidentActive tests
    // Incident.launchedtime >= 0 (46251-46263). Vanilla asks an AI side's grid about the player's units only through Whiskey's
    // fog-grid probes (it computes iswithinfog for non-player units only, 127212-127230), so the incident is the player's
    // intelligence and never the AI side's: both probes (the battle perception's and the campaign's enemy detection) mark the
    // incidents not launched for their calls and restore them. One home for the numbers and the rule; each probe owns its own
    // mask flow (BattlePerceptionProbe.MaskPlayerIntelIncidents, DetectionRules.RunMasked).
    internal static class PlayerIntelIncidents
    {
        internal static readonly int[] Ids = { 60, 61, 64 };
        internal const float NotLaunched = -1f;

        internal static bool Launched(float launchedTime)
        {
            return launchedTime >= 0f;
        }

        // The mask applies only in a W&L scenario and only when the probe asks an AI side's fog grid (the player's own side
        // reads vanilla's verdict, which the incident is meant to open).
        internal static bool Masks(bool asksAnAiSidesGrid, bool dlcScenarioActive)
        {
            return dlcScenarioActive && asksAnAiSidesGrid;
        }
    }
}
