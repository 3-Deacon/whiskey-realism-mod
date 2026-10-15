using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// The one battle clock every tactical doctrine timer reads: phase budgets,
    /// main-effort and exploit windows, plan deadline, replan cooldown,
    /// opportunity hysteresis, arrival hold and contact-ledger decay.
    ///
    /// Raw source is <c>GameVars.currenttimefromstart</c> (game hours;
    /// <c>uniStormSystem.Hour + battlepasseddays * 24</c>, SetTimeVariables
    /// decompile 93545). Vanilla runs the night as an end-of-day cycle at game
    /// speed 1000 (CheckTimeIssues 86368-86490, <c>eodcycle</c> 1..5), so the
    /// raw clock jumps ~9 h overnight while nothing is fought. The runtime that
    /// watches <c>eodcycle</c> reports that span through
    /// <see cref="AddPausedSpan"/>; doctrine time is raw time minus every
    /// paused span, so no budget, deadline or decay advances across the night.
    /// </summary>
    public static class TacticalDoctrineClock
    {
        private static double _pausedTotalSeconds;

        /// <summary>Battle seconds excluded from doctrine time so far this battle.</summary>
        public static double PausedTotalSeconds => _pausedTotalSeconds;

        /// <summary>Doctrine time in battle seconds (raw clock minus paused spans).</summary>
        public static double NowBattleSeconds => Effective(RawBattleSeconds(), _pausedTotalSeconds);

        /// <summary>Doctrine time in battle hours.</summary>
        public static double NowBattleHours => NowBattleSeconds / 3600.0;

        internal static double Effective(double rawBattleSeconds, double pausedTotalSeconds)
        {
            if (!Finite(rawBattleSeconds) || rawBattleSeconds <= 0.0) return 0.0;
            double paused = Finite(pausedTotalSeconds) && pausedTotalSeconds > 0.0 ? pausedTotalSeconds : 0.0;
            return Math.Max(0.0, rawBattleSeconds - paused);
        }

        /// <summary>Exclude a span of raw battle seconds (the night cycle) from doctrine time.</summary>
        public static void AddPausedSpan(double seconds)
        {
            if (!Finite(seconds) || seconds <= 0.0) return;
            _pausedTotalSeconds += seconds;
        }

        public static void ResetForBattleEnd()
        {
            _pausedTotalSeconds = 0.0;
        }

        private static double RawBattleSeconds()
        {
            try
            {
                double hours = GameVars.currenttimefromstart;
                return Finite(hours) && hours > 0.0 ? hours * 3600.0 : 0.0;
            }
            catch { return 0.0; }
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
