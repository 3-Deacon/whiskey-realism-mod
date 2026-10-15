namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// The one battle clock every tactical doctrine timer reads: phase budgets,
    /// main-effort and exploit windows, plan deadline, replan cooldown,
    /// opportunity hysteresis, arrival hold, artillery hold and contact-ledger
    /// decay. It is <see cref="TacticalBattleCoordinator.DoctrineBattleHours"/>
    /// in seconds: GameVars.currenttimefromstart (game hours, SetTimeVariables
    /// 93545) followed forward while fighting and frozen across vanilla's
    /// night end-of-day cycle (CheckTimeIssues 86368-86490, eodcycle != 0),
    /// where the raw clock jumps ~9 h.
    /// </summary>
    public static class TacticalDoctrineTime
    {
        /// <summary>Doctrine time in battle seconds.</summary>
        public static double NowBattleSeconds => TacticalBattleCoordinator.DoctrineBattleHours * 3600.0;

        /// <summary>Doctrine time in battle hours.</summary>
        public static double NowBattleHours => TacticalBattleCoordinator.DoctrineBattleHours;
    }
}
