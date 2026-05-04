using System.Collections.Generic;
using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Construction
{
    public enum ConstructionPosture
    {
        Infrastructure = 0,
        FieldSupply = 1,
        DefensiveWorks = 2,
        IndustrialExpansion = 3,
        EmergencyHold = 4
    }

    public enum ConstructionCandidateKind
    {
        None = 0,
        PrivateBuilding = 1,
        SupplyDepot = 2,
        Fort = 3,
        Telegraph = 4,
        Railroad = 5
    }

    public enum ConstructionSuppressionReason
    {
        None = 0,
        VanillaInvalid = 1,
        EmergencyCreditFloor = 2,
        CsaRailDoctrineCap = 3,
        DiscretionaryIndustryCreditDefense = 4,
        NoSupportingUnit = 5,
        UnsafeRear = 6
    }

    public sealed class ConstructionOptions
    {
        public float SupplyPressureThreshold = 0.35f;
        public float DefensiveThreatThreshold = 0.65f;
        public int CsaArmsStressLastYear = 1863;
        public int MinimumRatingBufferFromBondFloor = 1;
    }

    public sealed class ConstructionInput
    {
        public int AllianceId;
        public EraStage EraStage;
        public int CurrentChapter;
        public int CurrentYear = 1861;
        public FiscalPosture FiscalPosture = FiscalPosture.BalancedWar;
        public FiscalGate FiscalDefendedGate = FiscalGate.None;
        public int CurrentRating;
        public int BondFloorRating = 11;
        public bool SupplyProtection;
        public bool LogisticsExpansion;
        public bool ForceCapWarning;
        public string TopSupplyTheater = "";
        public int LowSupplyFormationCount;
        public int LowAmmoFormationCount;
        public float SupplyPressure;
        public float AmmoPressure;
        public float TransportPressure;
        public float CapitalThreat;
        public int ActiveRailroadStarts;
        public List<ConstructionCandidate> Candidates = new List<ConstructionCandidate>();
    }

    public struct ConstructionCandidate
    {
        public static readonly ConstructionCandidate None = new ConstructionCandidate
        {
            Kind = ConstructionCandidateKind.None,
            Name = "<none>",
            Reason = "none",
            VanillaValid = false,
            Score = 0f
        };

        public ConstructionCandidateKind Kind;
        public int BuildingTypeId;
        public string Name;
        public Theater Theater;
        public float SupplyPressure;
        public float AmmoPressure;
        public float TransportPressure;
        public float WoundedPressure;
        public float CapitalThreat;
        public bool VanillaValid;
        public bool SafeRear;
        public bool ArmsIndustry;
        public bool SupportsActiveArmyCorridor;
        public bool CriticalDefense;
        public float Score;
        public string Reason;
    }

    public struct ConstructionSuppression
    {
        public ConstructionCandidateKind Kind;
        public string Name;
        public ConstructionSuppressionReason Reason;
    }

    public sealed class ConstructionOutput
    {
        public ConstructionPosture Posture;
        public string TopConstructionTheater = "";
        public ConstructionCandidate TopPrivateBuilding = ConstructionCandidate.None;
        public ConstructionCandidate TopSupplyDepot = ConstructionCandidate.None;
        public ConstructionCandidate TopFort = ConstructionCandidate.None;
        public ConstructionCandidate TopTelegraph = ConstructionCandidate.None;
        public ConstructionCandidate TopRailroad = ConstructionCandidate.None;
        public ConstructionSuppression[] Suppressions = new ConstructionSuppression[0];
        public string Signature = "";
    }

    public struct ConstructionStartEvent
    {
        public int AllianceId;
        public ConstructionCandidateKind Kind;
        public int BuildingTypeId;
        public string Name;
        public Theater Theater;
        public string SiteKey;
        public int Year;
        public int Month;
        public int Day;
    }
}
