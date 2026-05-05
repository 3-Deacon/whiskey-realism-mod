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
        public string AreaKey;
        public string SectorKey;
        public float X;
        public float Z;
        public float ActiveStrength;
        public float Morale;
        public float ReadinessStep;
        public Theater Theater;
        public CandidateTier Tier;
        public bool InOffensiveOperation;
        public bool PlayerControlled;
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
        public string TelemetrySignature;
    }

    public sealed class DefenseIntentLedgerOutput
    {
        public int AllianceId;
        public List<DefenseResponse> Responses = new List<DefenseResponse>();
        public string Signature;
    }
}
