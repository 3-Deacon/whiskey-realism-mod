using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    internal class SuccessionScheduler
    {
        internal class Event
        {
            public int    Id;
            public string Name;
            public int    EarliestYear;
            public int    EarliestMonth;
            public int    AllianceId;
            public string ReplacementName;
            public string ReplacedRole;
            public Func<WarStateView, bool> WarStateGate;
            public bool   Fired;
        }

        internal class WarStateView
        {
            public int  CurrentMonth;
            public int  CurrentYear;
            public bool ANVHasLostMajorBattle;
            public bool JohnstonWoundedOrDisabled;
            public bool BragsCommandRatingLow;
            public bool AoPHasFailedNOffensives;
            public bool BurnsidesFirstDefeatPassed;
            public bool LeeInvadingPennsylvania;
            public bool WesternMajorDefeatPassed;
            public bool VicksburgFallen;
            public bool ChattanoogaFallen;
            public bool AtlantaThreatened;
            public bool DavisPatienceExhausted;
            public bool ValleyOpsNeeded;
            public bool WarClearlyLost;
        }

        private readonly List<Event> _events = new List<Event>();

        internal HashSet<int> FiredEventIds = new HashSet<int>();

        internal SuccessionScheduler()
        {
            _events.Add(new Event { Id =  1, Name = "Lee → ANV command",         EarliestYear = 1862, EarliestMonth =  5, AllianceId = 1, ReplacementName = "lee",       ReplacedRole = "ANV",            WarStateGate = w => w.JohnstonWoundedOrDisabled || w.ANVHasLostMajorBattle });
            _events.Add(new Event { Id =  2, Name = "Bragg → Western theater",   EarliestYear = 1862, EarliestMonth =  6, AllianceId = 1, ReplacementName = "bragg",     ReplacedRole = "Western",        WarStateGate = w => w.BragsCommandRatingLow });
            _events.Add(new Event { Id =  3, Name = "McClellan removed",         EarliestYear = 1862, EarliestMonth = 11, AllianceId = 0, ReplacementName = "burnside",  ReplacedRole = "AoP",            WarStateGate = w => w.AoPHasFailedNOffensives });
            _events.Add(new Event { Id =  4, Name = "Burnside → Hooker",         EarliestYear = 1863, EarliestMonth =  1, AllianceId = 0, ReplacementName = "hooker",    ReplacedRole = "AoP",            WarStateGate = w => w.BurnsidesFirstDefeatPassed });
            _events.Add(new Event { Id =  5, Name = "Hooker → Meade",            EarliestYear = 1863, EarliestMonth =  6, AllianceId = 0, ReplacementName = "meade",     ReplacedRole = "AoP",            WarStateGate = w => w.LeeInvadingPennsylvania });
            _events.Add(new Event { Id =  6, Name = "Bragg removed",             EarliestYear = 1863, EarliestMonth = 11, AllianceId = 1, ReplacementName = "johnston",  ReplacedRole = "Western",        WarStateGate = w => w.WesternMajorDefeatPassed });
            _events.Add(new Event { Id =  7, Name = "Joe Johnston → Western",    EarliestYear = 1863, EarliestMonth = 12, AllianceId = 1, ReplacementName = "johnston",  ReplacedRole = "Western",        WarStateGate = w => true });
            _events.Add(new Event { Id =  8, Name = "Grant → General-in-Chief",  EarliestYear = 1864, EarliestMonth =  3, AllianceId = 0, ReplacementName = "grant",     ReplacedRole = "GeneralInChief", WarStateGate = w => w.VicksburgFallen && w.ChattanoogaFallen });
            _events.Add(new Event { Id =  9, Name = "Sherman → Western",         EarliestYear = 1864, EarliestMonth =  3, AllianceId = 0, ReplacementName = "sherman",   ReplacedRole = "Western",        WarStateGate = w => w.VicksburgFallen && w.ChattanoogaFallen });
            _events.Add(new Event { Id = 10, Name = "Hood replaces Johnston",    EarliestYear = 1864, EarliestMonth =  7, AllianceId = 1, ReplacementName = "hood",      ReplacedRole = "Western",        WarStateGate = w => w.AtlantaThreatened && w.DavisPatienceExhausted });
            _events.Add(new Event { Id = 11, Name = "Sheridan → Shenandoah",     EarliestYear = 1864, EarliestMonth =  8, AllianceId = 0, ReplacementName = "sheridan",  ReplacedRole = "Valley",         WarStateGate = w => w.ValleyOpsNeeded });
            _events.Add(new Event { Id = 12, Name = "Lee → General-in-Chief CSA", EarliestYear = 1865, EarliestMonth =  2, AllianceId = 1, ReplacementName = "lee",       ReplacedRole = "GeneralInChief", WarStateGate = w => w.WarClearlyLost });
        }

        internal List<Event> CheckEvents(WarStateView w)
        {
            var fired = new List<Event>();
            foreach (var e in _events)
            {
                if (FiredEventIds.Contains(e.Id)) continue;

                bool dateOk = (w.CurrentYear > e.EarliestYear) ||
                              (w.CurrentYear == e.EarliestYear && w.CurrentMonth >= e.EarliestMonth);
                bool warStateOk = e.WarStateGate(w);

                if (Plugin.Instance.SuccessionTrace.Value)
                    Plugin.Log.LogInfo($"[Succession:{e.Id}] {e.Name} dateOk={dateOk} warStateOk={warStateOk}");

                if (dateOk && warStateOk)
                {
                    FiredEventIds.Add(e.Id);
                    fired.Add(e);
                }
            }
            return fired;
        }
    }
}
