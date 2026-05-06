using WhiskeyRealism.Strategic.Fiscal;

namespace WhiskeyRealism.Strategic.Projects
{
    public sealed class ProjectDoctrineSignalInput
    {
        public int Alliance;
        public EraStage Era;
        public FiscalPosture FiscalPosture = FiscalPosture.BalancedWar;
        public float OwnAverageRifles;
        public float EnemyBestAverageRifles;
        public float OwnAverageGuns;
        public float EnemyBestAverageGuns;
        public float OwnTotalTonnage;
        public float EnemyTotalTonnage;
        public float BlockadeRatio = 0.5f;
        public float PortViabilityInput = 0.5f;
        public float ManpowerStressInput;
        public float SupplyPressure;
        public float TransportPressure;
        public float IndustryGapInput;
        public float AgricultureFoodStressInput;
        public float CivilOrderRiskInput;
        public float RecognitionProbability;
        public float OffensiveTempoInput;
        public float StrengthRatio = 1f;
    }

    public sealed class ProjectDoctrineSignals
    {
        public int Alliance;
        public EraStage Era;
        public FiscalPosture FiscalPosture;
        public float WeaponDeficit;
        public float ArtilleryDeficit;
        public float NavalDeficit;
        public float BlockadePressure;
        public float PortViability;
        public float CreditStress;
        public float ManpowerStress;
        public float LogisticsTempoNeed;
        public float IndustryGap;
        public float AgricultureFoodStress;
        public float CivilOrderRisk;
        public float RecognitionWindow;
        public float OffensiveTempoNeed;
        public float LateWarCollapseRisk;
    }

    public sealed class ProjectRuntimeFacts
    {
        public int ProjectId;
        public int SubsidyLane;
        public int DateFromYear;
        public int DateFromMonth;
        public int DateFromDay;
        public float Cost;
        public bool DateFromKnown;
    }

    public sealed class ProjectLaneIntent
    {
        public int Alliance;
        public int SubsidyLane;
        public int QueuedProjectId;
        public float FundingAvailable;
        public float FundingNeeded;
        public float NetFundingPerDay;
        public float TimeToFundEstimateDays;
        public bool ConstructionCurrentlyWins;
        public bool CriticalDoctrineProject;
    }

    public sealed class ProjectDoctrineScore
    {
        public int ProjectId;
        public float VanillaWeight;
        public float ProfileWeight;
        public float FiscalWeight;
        public float DoctrineWeight;
        public float Total;
        public string Reason;
        public bool Suppressed;
        public bool OutOfWindow;
    }

    public sealed class ProjectDoctrineDecision
    {
        public bool ShouldReplace;
        public int ProjectId;
        public float BestScore;
        public float VanillaScore;
        public string Reason;
        public ProjectLaneIntent LaneIntent;
    }
}
