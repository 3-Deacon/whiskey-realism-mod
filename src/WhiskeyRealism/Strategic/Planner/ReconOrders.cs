namespace WhiskeyRealism.Strategic.Planner
{
    // The G5d recon executor's pure decisions (plan § G5d; built with G1b's pure half).
    //
    // CavalryOrderFor: the BattleUnits.ChangeCampaignOrders code to send (90720-90770: 120 + n sets
    // cavalryorders n, 121 Scouting, 120 none; it returns at once when the order is already set,
    // 90738, so a repeat is free), or -1 for no call. Only a cavalry-majority Screen on its mission
    // gets Scouting (vanilla's AI never sets it; its effects are vanilla's: "Scouting" status 119923,
    // intel -> readiness 120424-120426, scouting perk experience 115368-115371, later arrival as a
    // battle reinforcement 67742-67744; sight does not widen). A recalled or cleared screen gets 120.
    // Pickets, advance guards and probes get no call. Never on a group in the W&L player's chain:
    // the order is applied by PerformActionForGroupForAllUnitsOnLevel(group, -1, "SetCavalryOrders")
    // (90758), which reaches the group and every descendant three levels down (92572-92620), the
    // player's own units included, and the scouting term tests the group's own cavalryorders
    // (120424-120426), so it cannot be set on the non-player regiments alone. A carrying-group
    // screen moves and views without the order.
    //
    // MayAct: the executor's live re-check before any move or order (stage F's chain class re-read
    // with ChainClassOf): never the player's own command or the CIC player's side; a map-group
    // player is addressed by W&L dispatch only (WlChainRules.OrderOnly).
    internal static class ReconOrders
    {
        internal const int ScoutingOrder = 121;
        internal const int NoCavalryOrder = 120;

        internal static int CavalryOrderFor(ReconKind kind, ReconState state, float cavalryShare, WlChainClass chain)
        {
            if (chain != WlChainClass.None || kind != ReconKind.Screen) return -1;
            if (state == ReconState.Recalled || state == ReconState.Cleared) return NoCavalryOrder;
            return cavalryShare >= ReconPlanner.MinCavalryShare ? ScoutingOrder : -1;
        }

        // The cavalry mode n of a code CavalryOrderFor returned (ChangeCampaignOrders: 120 + n, 90720-90770): 0 none, 1 Scouting.
        // What the executor compares with Regiment.cavalryordersordered.
        internal static int ModeOf(int orderCode)
        {
            return orderCode - NoCavalryOrder;
        }

        internal static bool MayAct(WlChainClass chain)
        {
            return chain != WlChainClass.PlayerCommand && chain != WlChainClass.CicSide;
        }
    }
}
