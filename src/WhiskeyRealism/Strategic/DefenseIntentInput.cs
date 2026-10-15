using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public enum DefenseThreatSourceKind
    {
        SeaInvasion = 1,
        RaidForce = 2,
        AssetProximity = 3
    }

    public sealed class DefenseThreatSource
    {
        public DefenseThreatSourceKind Kind;
        // The believed enemy group behind a SeaInvasion or RaidForce threat (EnemyBelief.UnitKey).
        public string EnemyUnitKey;
        public string AssetName;
        public CampaignMapAssetKind AssetKind;
        public AssetStrategicRole AssetRole;
        public float X;
        public float Z;
        public float EnemyStrength;
        public int[] EnemyInstanceIds;
        public bool LandedSignal;
        // The sea-invasion belief that raised this threat last review expired (Recovered).
        public bool BeliefExpired;
    }

    public sealed class DefenseIntentInput
    {
        public int AllianceId;
        public bool PlayerIsCIC;
        public PersonalityVector CICPersonality;
        public List<DefenseThreatSource> Threats = new List<DefenseThreatSource>();
        public List<DefenseCandidate> Candidates = new List<DefenseCandidate>();
        public DefenseCooldownTable Cooldown = new DefenseCooldownTable();
        public int CooldownDays = 4;
        public float GuardBudgetFraction = 0.10f;
        public float CapitalDefenseBudgetFraction = 0.18f;
        public float TotalAllianceEffectiveStrength;
        public FrontSectorLedger FrontLedger;
        public FormationDirectiveLedger FormationDirectives;
        public List<CampaignMapAsset> GuardCandidateAssets = new List<CampaignMapAsset>();
    }
}
