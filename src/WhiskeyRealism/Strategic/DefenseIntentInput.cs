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
        public int InvasionForceInstanceId;
        public string SpotName;
        public string SourcePortName;
        public int RaidGroupInstanceId;
        public int RaidCurrentState;
        public string AssetName;
        public CampaignMapAssetKind AssetKind;
        public AssetStrategicRole AssetRole;
        public float X;
        public float Z;
        public float EnemyStrength;
        public int[] EnemyInstanceIds;
        public bool LandedSignal;
        public bool VanillaCollapsed;
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
        public float TotalAllianceEffectiveStrength;
        public List<CampaignMapAsset> GuardCandidateAssets = new List<CampaignMapAsset>();
    }
}
