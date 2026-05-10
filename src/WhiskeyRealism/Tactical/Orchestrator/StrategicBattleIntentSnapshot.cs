namespace WhiskeyRealism.Tactical.Orchestrator
{
    public readonly struct StrategicBattleIntentSnapshot
    {
        public static readonly StrategicBattleIntentSnapshot Empty = new StrategicBattleIntentSnapshot(0f, 0f, string.Empty, string.Empty);

        public StrategicBattleIntentSnapshot(
            float casualtyPressure,
            float timePressure,
            string theaterIntent,
            string campaignIntent)
        {
            CasualtyPressure = Clamp01(casualtyPressure);
            TimePressure = Clamp01(timePressure);
            TheaterIntent = string.IsNullOrWhiteSpace(theaterIntent) ? string.Empty : theaterIntent.Trim();
            CampaignIntent = string.IsNullOrWhiteSpace(campaignIntent) ? string.Empty : campaignIntent.Trim();
        }

        public float CasualtyPressure { get; }
        public float TimePressure { get; }
        public string TheaterIntent { get; }
        public string CampaignIntent { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
