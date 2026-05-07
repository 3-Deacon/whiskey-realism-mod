using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalSectorSource
    {
        None = 0,
        ObjectiveChain = 1,
        AngleSlice = 2
    }

    public enum TacticalObservedEvent
    {
        Macro = 0,
        Group = 1,
        Charge = 2,
        Feud = 3,
        Sector = 4,
        Order = 5,
        Reserve = 6,
        Artillery = 7,
        Fallback = 8,
        PlayerOrder = 9,
        Command = 10,
        Odds = 11
    }

    public sealed class TacticalBattleContext
    {
        public int Side { get; set; }
        public int Alliance { get; set; }
        public int MacroAi { get; set; }
        public int GroupCount { get; set; }
        public int ChargingCount { get; set; }
        public int FeudGroupCount { get; set; }
        public int ReserveGroupCount { get; set; }
        public int ArtilleryGroupCount { get; set; }
        public int FallbackCount { get; set; }
        public int RetreatingCount { get; set; }
        public int VisibleEnemyCount { get; set; }
        public int ObjectiveChainCount { get; set; }
        public TacticalSectorSource SectorSource { get; set; }
        public string SectorSignature { get; set; }
        public string OrderSignature { get; set; }
        public string CommandSignature { get; set; }
        public string OddsSignature { get; set; }
        public string OddsSummary { get; set; }
        public float ForceBalance { get; set; }
        public float ReinforcementsWithin24Hours { get; set; }
        public int DecisiveSectorId { get; set; }
        public float CurrentGlobalOdds { get; set; }
        public float ProjectedGlobalOdds { get; set; }

        public static TacticalBattleContext Empty()
        {
            return new TacticalBattleContext
            {
                Side = -1,
                Alliance = -1,
                MacroAi = -99,
                SectorSource = TacticalSectorSource.None,
                SectorSignature = "",
                OrderSignature = "",
                CommandSignature = "",
                OddsSignature = "",
                OddsSummary = "",
                DecisiveSectorId = -1
            };
        }
    }

    public sealed class TacticalObserverSnapshot
    {
        public int GroupCount { get; set; }
        public int ChargingCount { get; set; }
        public int FeudGroupCount { get; set; }
        public int ReserveGroupCount { get; set; }
        public int ArtilleryGroupCount { get; set; }
        public int FallbackCount { get; set; }
        public int RetreatingCount { get; set; }
        public string Signature { get; set; }

        public static TacticalObserverSnapshot Empty()
        {
            return new TacticalObserverSnapshot { Signature = "" };
        }
    }
}
