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
        // The belief that raised this sea-invasion or raid threat last review no longer qualifies
        // (LapsedThreatTracker): reported once more, as Recovered.
        public bool SightingLapsed;
    }

    public sealed class DefenseIntentInput
    {
        public bool PlayerIsCIC;
        public PersonalityVector CICPersonality;
        public List<DefenseThreatSource> Threats = new List<DefenseThreatSource>();
        public List<DefenseCandidate> Candidates = new List<DefenseCandidate>();
        public DefenseCooldownTable Cooldown = new DefenseCooldownTable();
        public int CooldownDays = 4;
        public float GuardBudgetFraction = 0.10f;
        public float CapitalDefenseBudgetFraction = 0.18f;
        public float TotalAllianceEffectiveStrength;
        // A position-holding candidate covers the one threat or asset nearest its hold position,
        // within this distance: vanilla's maxdistancedefensiveoperations ("already there" 11799,
        // reach to a threat 13615). Vanilla's capital need (11733-11740) counts own groups by last
        // waypoint at strength x morale, within the nearest enemy's distance instead. 0 disables
        // position cover.
        public float HoldCoverRadius;
        public FrontSectorLedger FrontLedger;
        public FormationDirectiveLedger FormationDirectives;
        public List<CampaignMapAsset> GuardCandidateAssets = new List<CampaignMapAsset>();
    }
}
