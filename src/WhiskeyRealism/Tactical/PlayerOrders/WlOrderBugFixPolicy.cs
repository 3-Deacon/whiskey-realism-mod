namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // BUG-WL-019 (plan 2026-10-15-wl-order-flow.md D13, N2). Vanilla's AIBattle.CheckRemovalOfOrders
    // (private, decompile 6777-6811) runs on every AIBattle whose groups are assigned (UpdateDLCPlayerOrders,
    // 6747), the enemy's (isplayeraiorfeud 0) and the feud instance's (2) included. Its first test, the
    // group's alliance against the player's (6788), passes for the player's own command, and with the
    // instance's macroai == 3 it calls CheckCurrentOrderUpdate(group, 15, ...) (6794-6798); for a top-level
    // player the gate at 8337 passes, so he is told "our army retreats" when the ENEMY army retreats.
    // The patch stamps the calling instance for the duration of the call; a type-15 call under a stamp
    // that is not the player's side (1) is the enemy's retreat sent by mistake and is dropped.
    internal static class EnemyRetreatGuard
    {
        public const int NoStamp = -1;
        private const int PlayerSide = 1;
        private const int Retreat = 15;

        // AIBattle.isplayeraiorfeud of the instance inside CheckRemovalOfOrders, or NoStamp outside it.
        public static int Stamp { get; private set; } = NoStamp;

        public static void Set(int isPlayerAiOrFeud)
        {
            Stamp = isPlayerAiOrFeud;
        }

        public static void Clear()
        {
            Stamp = NoStamp;
        }

        public static bool IsEnemySideRetreat(int type, int removalStamp)
        {
            return type == Retreat && removalStamp != NoStamp && removalStamp != PlayerSide;
        }
    }

    // BUG-WL-020 (D14). DLC_WL.LaunchIncident (decompile 46272) hides the paper-map fog grid for the Lost
    // Orders incidents 60, 61 and 64 (46530) before it asks whether the incident applies (46545); a failed
    // check returns false with the grid still hidden, and Incident.End's re-show (40677-40680) never runs.
    // Entering the paper map re-shows the grid (29015) and leaving hides it (29024), so the grid is wrong
    // only while the paper map is open at the roll. RelaunchIncidents (45752-45764) re-applies an active
    // incident with skipinfos: true, hides the grid on purpose and returns false (46550-46552): that grid
    // stays hidden.
    internal static class LostOrdersFow
    {
        public static bool ShouldRestore(int incidentId, bool useFow, bool launchedDirectly, bool skipInfos, bool launched, bool paperMapOpen)
        {
            bool lostOrders = incidentId == 60 || incidentId == 61 || incidentId == 64;
            return lostOrders && useFow && launchedDirectly && !skipInfos && !launched && paperMapOpen;
        }
    }

    // BUG-WL-022 (D18, N3). GivenOrders.commanderid is the commander of the unit that issued the order, read
    // when the order was made (8341-8360); a commander killed, relieved or swapped since then leaves the
    // panel showing his portrait. The order group's current commander is shown instead.
    internal static class StaleIssuer
    {
        // issuerCommandOwnerId: the commander that currently commands the issuer's command, -1 when it
        // has none. The issuer is stale when he is not on the roster or no longer commands it himself.
        public static bool TryReplacement(int issuerId, bool issuerExists, int issuerCommandOwnerId,
            int groupCommanderId, int chosenId, out int replacement)
        {
            replacement = issuerId;
            bool stale = !issuerExists || issuerCommandOwnerId != issuerId;
            if (!stale) return false;
            if (groupCommanderId < 0 || groupCommanderId == issuerId || groupCommanderId == chosenId) return false;
            replacement = groupCommanderId;
            return true;
        }
    }
}
