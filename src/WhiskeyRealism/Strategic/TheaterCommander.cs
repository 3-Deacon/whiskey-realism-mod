namespace WhiskeyRealism.Strategic
{
    public class TheaterCommander
    {
        public int TheaterId;
        public int OfficerCommanderId;
        public string OfficerName;
        public PersonalityVector Personality;

        public OperationalPlan ActivePlan;

        public float GetZoneRelevance(int zoneId)
        {
            if (ActivePlan?.CurrentPhase == null) return 1.0f;
            return ActivePlan.CurrentPhase.TargetAreaId == zoneId ? 1.5f : 1.0f;
        }

        public float GetForceConsolidationUrgency()
        {
            if (ActivePlan?.CurrentPhase == null) return 1.0f;
            return PersonalityVector.Clamp(0.5f * (Personality.Aggression - Personality.Caution)) + 1.0f;
        }

        public float GetDefensiveOpsThreshold()
        {
            return 1.0f - 0.3f * Personality.Caution;
        }

        public float GetPerkPreference(int perkId)
        {
            return 1.0f;
        }

        public float GetRecruitmentTheaterWeight(Theater theater, int allianceId)
        {
            return FactionProfiles.TheaterPreferenceFor(allianceId, theater);
        }

        public float GetChargeRestraint()
        {
            return PersonalityVector.Clamp(-Personality.Audacity * 0.5f) + 1.0f;
        }
    }
}
