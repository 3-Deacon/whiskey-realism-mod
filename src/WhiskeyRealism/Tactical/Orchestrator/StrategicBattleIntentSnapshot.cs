using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public readonly struct StrategicBattleIntentSnapshot
    {
        public static readonly StrategicBattleIntentSnapshot Empty = new StrategicBattleIntentSnapshot(0f, 0f, string.Empty, string.Empty);

        public StrategicBattleIntentSnapshot(
            float casualtyPressure,
            float timePressure,
            string theaterIntent,
            string campaignIntent,
            int allianceId = -1,
            string campaignObjectiveId = "",
            float theaterPriority = 0f,
            float casualtyTolerance = 0f,
            float preserveForceBias = 0f,
            PersonalityVector commanderPersonality = default(PersonalityVector))
        {
            CasualtyPressure = Clamp01(casualtyPressure);
            TimePressure = Clamp01(timePressure);
            TheaterIntent = string.IsNullOrWhiteSpace(theaterIntent) ? string.Empty : theaterIntent.Trim();
            CampaignIntent = string.IsNullOrWhiteSpace(campaignIntent) ? string.Empty : campaignIntent.Trim();
            AllianceId = allianceId >= 0 && allianceId <= 1 ? allianceId : -1;
            CampaignObjectiveId = string.IsNullOrWhiteSpace(campaignObjectiveId) ? string.Empty : campaignObjectiveId.Trim();
            TheaterPriority = Clamp01(theaterPriority);
            CasualtyTolerance = PersonalityVector.Clamp(SanitizeSigned(casualtyTolerance));
            PreserveForceBias = Clamp01(preserveForceBias);
            CommanderPersonality = SanitizePersonality(commanderPersonality);
        }

        public float CasualtyPressure { get; }
        public float TimePressure { get; }
        public string TheaterIntent { get; }
        public string CampaignIntent { get; }
        public int AllianceId { get; }
        public string CampaignObjectiveId { get; }
        public float TheaterPriority { get; }
        public float CasualtyTolerance { get; }
        public float PreserveForceBias { get; }
        public PersonalityVector CommanderPersonality { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static float SanitizeSigned(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static PersonalityVector SanitizePersonality(PersonalityVector value)
        {
            return new PersonalityVector(
                PersonalityVector.Clamp(SanitizeSigned(value.Aggression)),
                PersonalityVector.Clamp(SanitizeSigned(value.Caution)),
                PersonalityVector.Clamp(SanitizeSigned(value.Audacity)),
                PersonalityVector.Clamp(SanitizeSigned(value.CasualtyTolerance)),
                PersonalityVector.Clamp(SanitizeSigned(value.PoliticalResponsiveness)));
        }
    }
}
