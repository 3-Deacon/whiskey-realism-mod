using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// The one battle clock every tactical doctrine timer reads: phase budgets,
    /// main-effort and exploit windows, plan deadline, replan cooldown,
    /// opportunity hysteresis, arrival hold and contact-ledger decay.
    ///
    /// Source today is <c>GameVars.currenttimefromstart</c> (game hours;
    /// <c>uniStormSystem.Hour + battlepasseddays * 24</c>, SetTimeVariables
    /// decompile 93545). That raw clock jumps ~9 h across vanilla's night
    /// end-of-day cycle (CheckTimeIssues 86368-86490, <c>eodcycle</c> 1..5);
    /// the night freeze belongs to the battle coordinator, and this seam is
    /// where its doctrine-hours getter plugs in so no doctrine consumer reads
    /// the raw clock itself.
    /// </summary>
    public static class TacticalDoctrineClock
    {
        /// <summary>Doctrine time in battle seconds.</summary>
        public static double NowBattleSeconds
        {
            get
            {
                try
                {
                    double hours = GameVars.currenttimefromstart;
                    return !double.IsNaN(hours) && !double.IsInfinity(hours) && hours > 0.0 ? hours * 3600.0 : 0.0;
                }
                catch { return 0.0; }
            }
        }

        /// <summary>Doctrine time in battle hours.</summary>
        public static double NowBattleHours => NowBattleSeconds / 3600.0;
    }
}
