using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public sealed class DefenseThreat
    {
        public DefenseThreatSourceKind SourceKind;
        public string Signature;
        public DefensePosture Posture;
        public ThreatScale Scale;
        public string AssetName;
        public AssetStrategicRole AssetRole;
        public float X;
        public float Z;
        public float EnemyStrength;
        public float DesiredStrength;
        public CandidateTier ResponseRadius = CandidateTier.Local;
        public string EscalationReason;
    }

    public sealed class DefenseCandidate
    {
        public int UnitInstanceId;
        public string UnitKey;
        public string UnitName;
        public string SectorKey;
        public float X;
        public float Z;
        public float ActiveStrength;
        public float Morale;
        public float ReadinessStep;
        public Theater Theater;
        public CandidateTier Tier;
        public bool InOffensiveOperation;
        // Whiskey may not commit this group (W&L player chain, WlChainRules.MayMove false).
        public bool PlayerControlled;
        // Counts toward coverage at (HoldX, HoldZ), its last waypoint, without being a package
        // member: the W&L map-group player (WlChainRules.CountsTowardCoverage). Implies PlayerControlled.
        public bool HoldsPosition;
        public float HoldX;
        public float HoldZ;
        public bool CriticalFront;
        public bool HasFormationDirective;
        public bool DefensiveAllowed = true;
        public bool TransferDonorAllowed = true;
        public bool DirectMovementAllowed = true;
        public float DistanceToThreat;
        public float Score;
        public float EffectiveStrength;
    }

    public sealed class DefenseSuppression
    {
        public int UnitInstanceId;
        public string Reason;
    }

    public sealed class DefenseResponse
    {
        public DefenseThreat Threat;
        public List<DefenseCandidate> SelectedPackage = new List<DefenseCandidate>();
        public List<DefenseSuppression> Suppressed = new List<DefenseSuppression>();
        public bool Adequate;
        public bool Understrength;
        // Strength x morale of position-holding groups credited to this threat or asset;
        // the package only makes up the rest.
        public float HeldStrength;
        public string TelemetrySignature;
    }

    public sealed class DefenseIntentLedgerOutput
    {
        public int AllianceId;
        public List<DefenseResponse> Responses = new List<DefenseResponse>();
        public string Signature;
    }
}
