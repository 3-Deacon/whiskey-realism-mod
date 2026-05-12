namespace WhiskeyRealism.Tactical
{
    public static class TacticalOrderSettlementGate
    {
        public struct Input
        {
            public int OrderQueueCount;
            public int OrderState;
            public int RegimentPaths;
            public bool PathInterrupted;
            public int MovementMode;
            public bool ActiveMove;
        }

        public readonly struct Decision
        {
            public Decision(bool allowChange, string reason)
            {
                AllowChange = allowChange;
                Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
            }

            public bool AllowChange { get; }
            public string Reason { get; }
        }

        public static Decision Evaluate(in Input input)
        {
            if (input.OrderQueueCount > 0)
                return new Decision(false, "queued-order");

            if (input.OrderState < 0)
                return new Decision(false, "unknown-orderstate");

            if (input.OrderState == 1 &&
                input.PathInterrupted &&
                input.MovementMode <= 0 &&
                !input.ActiveMove)
                return new Decision(true, "stalled-interrupted-order");

            if (input.OrderState == 1 &&
                input.RegimentPaths <= 0 &&
                input.MovementMode <= 0 &&
                !input.ActiveMove)
                return new Decision(true, "stalled-pending-order");

            if (input.OrderState > 0)
                return new Decision(false, "pending-orderstate");

            if (input.RegimentPaths > 0 && input.PathInterrupted)
                return new Decision(false, "path-interrupted");

            if (input.RegimentPaths > 0 && input.MovementMode == 3)
                return new Decision(false, "active-movement");

            return new Decision(true, "settled");
        }

        public static bool HasBlockingPendingOrder(in Input input)
        {
            if (input.OrderQueueCount > 0) return true;
            if (input.OrderState < 0) return true;
            if (input.OrderState <= 0) return false;

            return !Evaluate(input).AllowChange;
        }
    }
}
