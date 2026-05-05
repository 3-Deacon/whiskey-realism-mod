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

        // FrontLedger transfer/hold modifiers — applied on top of FrontLedgerOptions defaults.
        // Bounded to [-0.05, +0.10].
        public float MinimumHoldRatioModifier;
        public float ConcessionRatioModifier;

        // FormationDirective modifiers — applied on top of FormationDirectiveOptions defaults.
        // RecoverFloorModifier bounded to [-0.05, +0.10]; MassRatioModifier bounded to [-0.10, +0.10].
        public float RecoverFloorModifier;
        public float MassRatioModifier;

        // Fiscal/construction bias modifiers — applied as multiplier adjustments in scorer call paths.
        // SupplyConstructionBias: additive; bounded to [-0.20, +0.40].
        // LogisticsBias: additive; bounded to [-0.20, +0.40].
        // ExpansionDamper: multiplicative damper; bounded to [0, +0.50].
        public float SupplyConstructionBias;
        public float LogisticsBias;
        public float ExpansionDamper;

        // Defense budget modifiers — applied in DefenseIntentRuntime and DefensiveOpsPatch.
        // GuardBudgetFractionModifier: additive on the 0.10f default; bounded to [-0.05, +0.05].
        // CapitalDefenseBudgetModifier: additive on the base capital fraction; bounded to [-0.05, +0.10].
        public float GuardBudgetFractionModifier;
        public float CapitalDefenseBudgetModifier;
    }
}
