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
        DiscretionaryIndustryCreditDefense = 4
    }

    public sealed class ConstructionOptions
    {
        public float SupplyPressureThreshold = 0.35f;
        public float LowSupplyFormationShare = 0.33f;
        public float DefensiveThreatThreshold = 0.65f;
        public int CsaArmsStressLastYear = 1863;
        public int MinimumRatingBufferFromBondFloor = 1;
    }

    public sealed class ConstructionInput
    {
        public int AllianceId;
        public EraStage EraStage;
        public int CurrentYear = 1861;
        public FiscalPosture FiscalPosture = FiscalPosture.BalancedWar;
        public FiscalGate FiscalDefendedGate = FiscalGate.None;
        public int CurrentRating;
        public int BondFloorRating = 11;
        public bool SupplyProtection;
        public bool LogisticsExpansion;
        public bool ForceCapWarning;
        public string TopSupplyTheater = "";
        public string TopSupplyAreaKey = "";
        public List<ConstructionSectorThreat> SectorThreats = new List<ConstructionSectorThreat>();
        public int LowSupplyFormationCount;
        public int LowAmmoFormationCount;
        public int FormationCount;
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
        public const int MissingBuildingTypeId = -1;

        public ConstructionCandidateKind Kind;
        public int BuildingTypeId;
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

    // Which vanilla CBuilding.Place calls ConstructionObserverPatch counts as a
    // construction start. Place (96163) registers a newly created building in
    // BattleUnits.cbuildings; a construction wish first places an off-map
    // placeholder (AddConstructionWish 97479, position -20000). A building
    // already gone from cbuildings when the observer runs was removed by an
    // earlier Place postfix (TelegraphPlacementPatch rejecting a station,
    // RemoveBuilding 98334) and never became a construction.
    public static class ConstructionStartFilter
    {
        public static bool CountsBuildingStart(bool newlyCreated, int owner, bool wishPlaceholder, bool stillRegistered)
        {
            return newlyCreated && owner >= 0 && owner < 2 && !wishPlaceholder && stillRegistered;
        }
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

namespace WhiskeyRealism.Strategic.Construction
{
    // Fiscal supply priority is a Theater name (FrontSector.Theater); formation
    // pressure is an ArmyAreaClassifier key. Compare each with its own kind.
    public static class ConstructionSupplyCorridor
    {
        public static bool Matches(string topTheater, string topAreaKey, Theater siteTheater, string siteAreaKey)
        {
            if (!string.IsNullOrEmpty(topAreaKey) && !string.IsNullOrEmpty(siteAreaKey) &&
                string.Equals(topAreaKey, siteAreaKey, System.StringComparison.OrdinalIgnoreCase))
                return true;
            return !string.IsNullOrEmpty(topTheater) &&
                siteTheater != Theater.Unknown &&
                string.Equals(topTheater, siteTheater.ToString(), System.StringComparison.OrdinalIgnoreCase);
        }
    }

    public struct ConstructionSectorThreat
    {
        public Theater Theater;
        public float Threat;
    }

    public static class ConstructionThreat
    {
        // Threat of the sector(s) covering a site's theater; quiet theaters stay 0.
        public static float ForTheater(System.Collections.Generic.IEnumerable<ConstructionSectorThreat> sectors, Theater theater)
        {
            float peak = 0f;
            if (sectors == null || theater == Theater.Unknown) return peak;
            foreach (var sector in sectors)
            {
                if (sector.Theater == theater && sector.Threat > peak)
                    peak = sector.Threat;
            }
            return peak;
        }

        // Fills per-sector threats and the alliance peak from the daily front ledger.
        // FrontSectorRuntime marks East/River critical permanently and ChoosePosture
        // defaults to Hold, so criticality alone is no signal: threat is the critical
        // sector's importance scaled by how far its own:enemy ratio sits below the
        // ledger's own hold ratio (0 at/above MinimumHoldRatio, -> importance as own -> 0).
        public static void Apply(ConstructionInput input, FrontSectorLedger front)
        {
            if (input == null) return;
            input.SectorThreats.Clear();
            if (front != null)
            {
                foreach (var sector in front.Sectors)
                {
                    float threat = SectorThreat(sector);
                    if (threat > 0f)
                        input.SectorThreats.Add(new ConstructionSectorThreat { Theater = sector.Theater, Threat = threat });
                }
            }
            input.CapitalThreat = Peak(input.SectorThreats);
        }

        public static float SectorThreat(FrontSector sector)
        {
            if (sector == null || !sector.IsCritical || sector.MinimumHoldRatio <= 0f) return 0f;
            // Every theater sector exists even when empty; own/max(1, enemy) is 0 there,
            // which would read as a total deficit. No enemy present means no threat.
            if (sector.EnemyStrength <= 0f) return 0f;
            float deficit = 1f - sector.StrengthRatio / sector.MinimumHoldRatio;
            if (deficit <= 0f) return 0f;
            if (deficit > 1f) deficit = 1f;
            return sector.StrategicImportance * deficit;
        }

        // Alliance-level capital threat is the most threatened sector, not an average
        // diluted by every quiet theater.
        public static float Peak(System.Collections.Generic.IEnumerable<ConstructionSectorThreat> sectors)
        {
            float peak = 0f;
            if (sectors == null) return peak;
            foreach (var sector in sectors)
            {
                if (sector.Threat > peak)
                    peak = sector.Threat;
            }
            return peak;
        }
    }
}
