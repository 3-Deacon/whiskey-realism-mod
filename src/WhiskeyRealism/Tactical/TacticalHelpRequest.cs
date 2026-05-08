namespace WhiskeyRealism.Tactical
{
    public static class TacticalHelpRequest
    {
        public enum Decision
        {
            NoRequest,
            RequestReserveScreen,
            RequestLineRelief,
            RequestArtillerySupport,
            RequestMainEffortShift,
        }

        public struct Input
        {
            public float SectorPressureRatio;
            public int OutflankedTierMax;
            public bool ArtilleryCounterBatteryNeeded;
            public bool MainEffortStalled;
            public int AiFeudStance;
            public int IsPlayerAiOrFeud;
        }

        public static Decision Score(in Input input)
        {
            if (!TacticalGateHelpers.PassesWlOwnership(input.AiFeudStance, input.IsPlayerAiOrFeud))
                return Decision.NoRequest;

            if (input.OutflankedTierMax >= 3)
                return Decision.RequestReserveScreen;
            if (input.SectorPressureRatio >= 1.25f)
                return Decision.RequestLineRelief;
            if (input.ArtilleryCounterBatteryNeeded)
                return Decision.RequestArtillerySupport;
            if (input.MainEffortStalled)
                return Decision.RequestMainEffortShift;
            return Decision.NoRequest;
        }
    }
}
