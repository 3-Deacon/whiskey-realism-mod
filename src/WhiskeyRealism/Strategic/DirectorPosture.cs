namespace WhiskeyRealism.Strategic
{
    public enum CampaignPace
    {
        Stable,
        TooQuiet,
        Overheated,
        TooFastCollapse,
        Stalemated,
        LateWarPressure
    }

    public enum StrategicIntent
    {
        Probe,
        Concentrate,
        Preserve,
        Delay,
        Exploit,
        Recover
    }

    public enum CollapseRisk
    {
        Low,
        Elevated,
        Critical
    }

    public sealed class DirectorPosture
    {
        public int AllianceId;
        public CampaignPace Pace;
        public StrategicIntent Intent;
        public CollapseRisk Risk;
        public Theater TheaterPriority = Theater.Unknown;
        public string Reason;
        public string SourceSignature;
        public bool Stale;

        // Threshold modifiers — applied on top of OperationalTempoDoctrine output.
        // Each is bounded to ±50% of the personality delta on the same field.
        public float MinimumProbeDaysModifier;
        public float MaximumProbeStrengthFractionModifier;
        public float EscalateFriendlyRatioModifier;
        public float EnemyReactionMultiplierModifier;
        public float WithdrawFriendlyRatioModifier;
    }
}
