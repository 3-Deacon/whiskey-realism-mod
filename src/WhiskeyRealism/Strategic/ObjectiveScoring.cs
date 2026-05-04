namespace WhiskeyRealism.Strategic
{
    public static class ObjectiveScoring
    {
        public static float Score(int allianceId, PersonalityVector p, GrandStrategyProfile strategy, ObjectiveMetadata meta)
        {
            float theaterPref = FactionProfiles.TheaterPreferenceFor(allianceId, meta.Theater);
            float foreignWeight = FactionProfiles.ForeignRecognitionWeightFor(allianceId);
            float forceRatioTerm = 0.5f;
            float distanceTerm = 0f;
            float strategyTerm = meta.StrategyWeight(strategy);

            return theaterPref
                 + meta.SupplyReachWeight * 1.0f
                 + meta.ForeignRecognitionWeight * foreignWeight
                 + meta.AttritionWeight * p.CasualtyTolerance
                 + forceRatioTerm * (1f - p.Caution)
                 - distanceTerm * (1f - p.Audacity)
                 + strategyTerm * 0.75f;
        }
    }
}
