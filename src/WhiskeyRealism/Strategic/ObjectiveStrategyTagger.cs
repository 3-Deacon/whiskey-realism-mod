namespace WhiskeyRealism.Strategic
{
    public static class ObjectiveStrategyTagger
    {
        public static ObjectiveMetadata ApplyDefaultTags(ObjectiveMetadata meta)
        {
            switch (meta.Theater)
            {
                case Theater.Coast:
                    meta = meta.WithTag(StrategyTag.Blockade)
                               .WithTag(StrategyTag.PortAccess);
                    break;
                case Theater.River:
                    meta = meta.WithTag(StrategyTag.RiverControl)
                               .WithTag(StrategyTag.DefensiveDepth);
                    break;
                case Theater.East:
                    meta = meta.WithTag(StrategyTag.CapitalThreat)
                               .WithTag(StrategyTag.CapitalDefense);
                    break;
                case Theater.West:
                    meta = meta.WithTag(StrategyTag.RailHub)
                               .WithTag(StrategyTag.DefensiveDepth);
                    break;
                case Theater.TransMiss:
                    meta = meta.WithTag(StrategyTag.DefensiveDepth);
                    break;
            }

            switch (meta.Category)
            {
                case Category.RiverControl:
                    meta = meta.WithTag(StrategyTag.RiverControl);
                    break;
                case Category.RailroadCut:
                case Category.SupplyHub:
                    meta = meta.WithTag(StrategyTag.RailHub);
                    break;
                case Category.CapitalThreat:
                    meta = meta.WithTag(StrategyTag.CapitalThreat);
                    break;
                case Category.ForeignRecognition:
                    meta = meta.WithTag(StrategyTag.ForeignRecognition);
                    break;
            }

            return meta;
        }
    }
}
