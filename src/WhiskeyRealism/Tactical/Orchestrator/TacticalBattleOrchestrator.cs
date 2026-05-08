using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Per-side root container instantiated by the coordinator for each active battle alliance.
    /// In O0 the Echelons list is always empty — Army/Corps/etc. concretes ship in O1+.
    /// Tick cascades to child echelons (none in O0) after incrementing its own TickCount.
    /// </summary>
    public sealed class TacticalBattleOrchestrator
    {
        public TacticalBattleOrchestrator(int allianceId, TacticalCommanderRoster roster)
        {
            AllianceId = allianceId;
            Roster = roster;
            Echelons = new List<EchelonOrchestrator>();
        }

        public int AllianceId { get; }
        public TacticalCommanderRoster Roster { get; }
        public List<EchelonOrchestrator> Echelons { get; }
        public int TickCount { get; private set; }

        public void Tick()
        {
            TickCount++;
            for (int i = 0; i < Echelons.Count; i++)
            {
                Echelons[i]?.Tick();
            }
        }

        public void PropagateIntent()
        {
            for (int i = 0; i < Echelons.Count; i++)
            {
                Echelons[i]?.PropagateIntent();
            }
        }
    }
}
