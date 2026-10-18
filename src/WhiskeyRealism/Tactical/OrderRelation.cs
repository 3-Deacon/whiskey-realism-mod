namespace WhiskeyRealism.Tactical
{
    /// <summary>
    /// The relation label of a queued or couriered order in the tactical observer rows. The holder of an order queue is the ordered unit's scene
    /// parent, not the issuer: <c>SetWaypoint</c> and the stop, stance and formation orders all queue on <c>reg.transform.parent</c> (decompile 91490,
    /// 91131), and <c>DetachCompany</c> parents a skirmisher company to the regiment's own parent, the brigade (90440-90500). So a W&amp;L player's
    /// own click on his regiment or company reads "source = the AI brigade". The old label for that case, "ai-to-player-subordinate", named an
    /// issuer the queue cannot show (smoke 2026-10-18: the player's stop on his company read as an AI brigade stop, BUG-WL-024).
    /// </summary>
    public static class OrderRelation
    {
        /// <summary>An order for a player-owned unit sitting in a queue that an AI group holds: whoever issued it, the holder did not.</summary>
        public const string AiQueueToPlayerUnit = "ai-queue-to-player-unit";

        public const string PlayerChain = "player-chain";
        public const string PlayerToAi = "player-to-ai";
        public const string AiChain = "ai-chain";

        public static string Classify(bool holderUnderCommander, bool targetUnderCommander)
        {
            if (!holderUnderCommander && targetUnderCommander) return AiQueueToPlayerUnit;
            if (holderUnderCommander && targetUnderCommander) return PlayerChain;
            if (holderUnderCommander) return PlayerToAi;
            return AiChain;
        }
    }
}
