namespace WhiskeyRealism.Tactical.PlayerOrders
{
    // What a CheckCurrentOrderUpdate call looks like to vanilla's gates. WlOrderFlowRuntime reads
    // each fact (the player's command, parent unit and campaign group are cached once per frame);
    // the cheap facts are read first, so a call from the enemy side never reaches the chain tests.
    internal struct CaptureFacts
    {
        // 8251
        public bool WlActive;
        public bool HasCurrentCommand;
        // 8259
        public bool EodCycle;
        public bool UnitMissing;            // null unit, or no transform parent
        public bool UnitIsPlayerAlliance;
        // the call
        public bool CampaignCall;
        public int Type;
        // 8329, 8366
        public bool PlayerIsBattleCo;
        public bool PlayerIsCic;
        // 8337, 8366: vanilla's flag (the player's parent unit is a child of the call's unit; true
        // when there is no parent unit) and flag2 (the player's command is its campaign group)
        public bool ParentIsUnderUnit;
        public bool CampaignGroupIsOwnCommand;
        public bool SameGroupAiObject;      // unit.groupaiobject == parentUnit.groupaiobject
        public bool UnitIsUnderCommander;   // unit.dlcw_isundercommander
        public bool UnitIsPlayerCommand;    // unit == the player's current command
        // 8362: unit is the player's command or below it (type 2 only)
        public bool UnitInPlayerSubtree;
    }

    // Which calls can reach the W&L player's panel: the returns before vanilla's dedupe,
    // AIBattle.CheckCurrentOrderUpdate decompile 8251-8369 (build 14703111). Pure; the capture
    // Prefix passes every call this rejects straight to vanilla, which returns on it as well.
    internal static class WlOrderCaptureGate
    {
        // 8251 and 8259: the gates that need no chain test, cheapest first.
        public static bool CheapReject(in CaptureFacts f)
        {
            return !f.WlActive || !f.HasCurrentCommand || f.EodCycle || f.UnitMissing || !f.UnitIsPlayerAlliance;
        }

        public static bool ReachesDedupe(in CaptureFacts f)
        {
            if (CheapReject(in f)) return false;

            if (f.CampaignCall)
            {
                // 8366
                return !(f.PlayerIsCic
                    || !f.CampaignGroupIsOwnCommand
                    || (!f.ParentIsUnderUnit && f.CampaignGroupIsOwnCommand)
                    || (f.ParentIsUnderUnit && f.CampaignGroupIsOwnCommand && !f.UnitIsUnderCommander));
            }

            // 8329
            if (f.PlayerIsBattleCo) return false;
            // 8337
            if ((!f.CampaignGroupIsOwnCommand && (!f.ParentIsUnderUnit || !f.SameGroupAiObject))
                || (!f.UnitIsUnderCommander && !f.UnitIsPlayerCommand && !f.ParentIsUnderUnit)) return false;
            // 8362-8365
            if (f.Type == 2 && (f.UnitInPlayerSubtree || f.UnitIsPlayerCommand)) return false;
            return true;
        }
    }
}
