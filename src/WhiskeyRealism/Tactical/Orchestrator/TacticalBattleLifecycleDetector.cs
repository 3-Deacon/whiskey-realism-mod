namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum BattleLifecycleEvent
    {
        None = 0,
        BattleStart = 1,
        BattleEnd = 2,
    }

    /// <summary>
    /// Pure helper that detects battle start/end transitions from per-tick "units in battle" counts.
    ///
    /// BattleStart fires on the first tick where units &gt; 0 after a tick where units == 0 (or initial state).
    /// BattleEnd fires after two consecutive units == 0 ticks following any units &gt; 0 tick.
    ///
    /// Two-tick hysteresis prevents single-tick flapping from spurious empty observations (e.g.
    /// when CheckGlobalAIStrategyPostfix fires twice per frame — once per AI side — the counter
    /// still needs two distinct calls with zero units to confirm end, halving wall-clock latency
    /// to ~1 frame but remaining robust against single-tick dropout).
    ///
    /// Independent of Unity / vanilla state — safe for harness testing.
    /// </summary>
    public sealed class TacticalBattleLifecycleDetector
    {
        private bool inBattle;
        private int consecutiveZeroTicks;

        public BattleLifecycleEvent Observe(int unitsInBattleThisTick)
        {
            if (unitsInBattleThisTick > 0)
            {
                consecutiveZeroTicks = 0;
                if (!inBattle)
                {
                    inBattle = true;
                    return BattleLifecycleEvent.BattleStart;
                }
                return BattleLifecycleEvent.None;
            }

            // unitsInBattleThisTick == 0
            if (!inBattle) return BattleLifecycleEvent.None;
            consecutiveZeroTicks++;
            if (consecutiveZeroTicks >= 2)
            {
                inBattle = false;
                consecutiveZeroTicks = 0;
                return BattleLifecycleEvent.BattleEnd;
            }
            return BattleLifecycleEvent.None;
        }
    }
}
