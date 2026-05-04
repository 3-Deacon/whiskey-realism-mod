namespace WhiskeyRealism.Strategic.Fiscal
{
    public enum FiscalPosture
    {
        Expansion = 0,
        BalancedWar = 1,
        CreditDefense = 2,
        EmergencySolvency = 3
    }

    public enum FiscalGate
    {
        None = 0,
        EmergencyPolicy = 1,
        Recruitment = 2,
        Construction = 3,
        WeaponPurchases = 4,
        BondFloor = 5
    }

    public enum FiscalSuppression
    {
        None = 0,
        DiscretionaryIndustry = 1,
        NewForceGrowth = 2,
        VanityNaval = 3,
        TariffConflict = 4,
        LowValueSubsidy = 5
    }

    public sealed class FiscalOptions
    {
        public float VanillaStep = 0.05f;
        public int CreditDefenseEntryBuffer = 1;
        public int CreditDefenseExitBuffer = 2;
        public int EmergencyExitWeeks = 2;
        public float MinimumSupplyProtection = 0.35f;
    }

    public sealed class FiscalStateMemory
    {
        public FiscalPosture PreviousPosture = FiscalPosture.BalancedWar;
        public int StableWeeksAboveEmergency;
        public bool EmergencyResidue;
    }

    public sealed class FiscalInput
    {
        public int AllianceId;
        public EraStage EraStage;
        public int CurrentChapter;
        public FiscalStateMemory Memory = new FiscalStateMemory();
        public int CurrentRating;
        public int RatingNotches;
        public int EmergencyPolicyFailureRating;
        public int RecruitmentFailureRating;
        public int ConstructionFailureRating;
        public int WeaponFailureRating;
        public float Treasury;
        public float Debt;
        public float AnnualBalance;
        public float InterestCost;
        public float ArmyUpkeep;
        public float NavyUpkeep;
        public float RecruitmentCost;
        public float SupplyDepotPurchases;
        public float[] Taxes = new float[5];
        public float[] TaxCaps = new float[5];
        public float[] Subsidies = new float[8];
        public float[] SubsidyFocus = new float[8];
        public bool HasWarBonds;
        public bool HasBankAct;
        public bool HasFreeTrade;
        public bool HasKingCotton;
        public bool HasOrganizedBlockadeRunning;
        public float InterventionProbability;
        public float SupplyPressure;
        public float AmmoPressure;
        public float TransportPressure;
        public int LowSupplyFormationCount;
        public int LowAmmoFormationCount;
        public string TopSupplyTheater;
    }

    public sealed class FiscalOutput
    {
        public FiscalPosture Posture;
        public FiscalGate DefendedGate;
        public int MinimumAcceptableRating;
        public bool SupplyProtection;
        public bool LogisticsExpansion;
        public bool ForceCapWarning;
        public string TheaterSupplyPriority;
        public float[] TargetTaxMin = new float[5];
        public float[] TargetTaxMax = new float[5];
        public float[] TargetSubsidyMin = new float[8];
        public float[] TargetSubsidyMax = new float[8];
        public FiscalSuppression[] Suppressions = new FiscalSuppression[0];
        public string Signature;
    }
}
