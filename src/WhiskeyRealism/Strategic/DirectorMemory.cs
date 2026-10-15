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

        // Copy for StrategicLedgerSet.PublishFrom. A stored DirectorPosture is never mutated
        // (StrategicResilienceDirector builds a new one each day), so it is shared.
        public DirectorMemory Clone()
        {
            var copy = (DirectorMemory)MemberwiseClone();
            copy.RecentEventSummaries = RecentEventSummaries != null ? new List<string>(RecentEventSummaries) : null;
            return copy;
        }
    }
}
