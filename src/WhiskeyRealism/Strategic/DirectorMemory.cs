using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class DirectorMemory
    {
        public DirectorPosture LastPosture;
        public int LastFullRefreshDay = -1;
        public int CapitalDangerStreakDays;
        public int DaysSinceLastBattle;
        public List<string> RecentEventSummaries = new List<string>();
        public string LastSourceSignature;
    }
}
