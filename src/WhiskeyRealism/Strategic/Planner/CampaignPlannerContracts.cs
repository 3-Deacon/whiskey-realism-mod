using System.Collections.Generic;

// Stage G planner contracts (docs/superpowers/plans/2026-10-15-campaign-planner.md § G0).
// Pure: no Unity or vanilla types. Positions are campaign-map X/Z. Every member is read by a
// G0 primitive or by a later slice named in the plan's read census (§ G7); a member with no
// reader is deleted, not kept for later.
namespace WhiskeyRealism.Strategic.Planner
{
    // The W&L player's echelon as the planner treats it (spec C4/C5; vanilla
    // GetPlayerCommandHierarchy 44397-44427, IsCommanderInChief 47475, map-group test 8271).
    public enum PlayerCommandClass
    {
        NotCareer = 0,
        NoCommand = 1,
        CommanderInChief = 2,
        MapGroup = 3,
        BelowMapLevel = 4,
        ArmyGroupCommander = 5
    }

    // What one daily review may do for one alliance (PlayerCommandClassifier.ReviewMode).
    public enum PlannerReviewMode
    {
        Full = 0,         // plan, publish, execute
        Bookkeeping = 1,  // vanilla's job machine is paused (11368): snapshot, beliefs, aging only
        StandDown = 2     // the W&L CIC player's own alliance: nothing
    }

    [System.Flags]
    public enum FormationFlags : uint
    {
        None = 0,
        InBattle = 1,
        OnRetreat = 2,
        InOffensiveOps = 4,
        InDefensiveOps = 8,
        CapitalDefense = 16,
        VanillaDepotBuilder = 32,
        FortBuilder = 64,
        SeaInvasion = 128,
        RaidForce = 256,
        InTransfer = 512,
        // embarkmentstarttime > 0 || currentmodeltype == 1 || currentmodeltype == 2
        // (116348-116360, 116383-116417; vanilla's own afloat test 80724).
        Embarked = 1024,
        FightingForce = 2048,
        HasPath = 4096,
        MoveQueued = 8192
    }

    public struct FormationRecord
    {
        public int InstanceId;      // in-session lookup only, never persisted
        public string UnitKey;      // name + ":" + commander, the sidecar convention; persisted identity
        public int UnitTyp;
        public float X, Z, WaypointX, WaypointZ;
        public float Strength;      // groupstrengthdirect
        public float Morale;
        // RAW 0..1: max(avg(groupsupplystate[0..3]), supplystate) = vanilla GetGroupSupplyState x 2
        // (14063-14066 halves both terms, so vanilla's value never exceeds 0.5).
        public float Supply01;
        public int ReadinessStep;
        public float CommandRange;
        public float BugleRange;
        public int CommanderId;
        public bool HasTheater;
        public float TheaterX, TheaterZ;
        public int AreaIndex;       // vanilla aiarea index of the formation's closest area, -1 none
        public FormationFlags Flags;
        public string Name;
        // Stage F's chain class (WlStrategicOrderBridge.ChainClassOf); None outside a W&L career
        // and on the enemy alliance. Internal: WlChainClass is internal.
        internal WlChainClass Chain;
        public float CavalryShare;
        public float SightRadius;   // SightRadiusFormula.Campaign, the radius that paints the fog grid
        // Smallest containing own top group by allattachedunits (the battle core's D5 rule), -1 root.
        public int ParentInstanceId;
    }

    // No enemy aggregate (spec C25): points, distancepoints, enemycampaignunitsstrength* and
    // enemyavgcampaignunits are built from every enemy at its destination (10535-10594).
    public struct AreaRecord
    {
        public int Index;            // vanilla aiarea index
        public float X, Z;           // averageposition
        public float[] EdgeX, EdgeZ; // zoneedges
        public float Importance;     // importancevalues[faction]
        public float OwnPoints, SupplyCapacity;
    }

    [System.Flags]
    public enum ObjectiveRole
    {
        None = 0,
        Capital = 1,
        RailHub = 2,
        Port = 4,
        Industry = 8
    }

    // One record per target (Town or IIP) of every active campaign objective of either alliance.
    public struct ObjectiveRecord
    {
        public int ObjectiveId;        // UniqueObjectiveID
        public float X, Z;             // the target's live position
        public int HolderAlliance;
        public bool Available;         // the objective is in GetAvailableObjectives(snapshot alliance)
        public bool Accomplished;      // IDAccomplished
        public ObjectiveRole Roles;
        public float Importance01;
        public float GarrisonStrength; // fort garrison groupstrengthdirect at the target, 0 for none
    }

    public enum SupplySourceKind
    {
        Iip = 0,
        Depot = 1
    }

    public struct SupplySourceRecord
    {
        public int Id;
        public SupplySourceKind Kind;
        public int Owner;
        public float X, Z;
        public float TransportCapacity, Condition;
        public bool Built;
        public bool Invisible;
    }

    public struct RailLineRecord
    {
        public int Index;
        public bool Startable;          // StartConstruction(alliance, checkonly: true)
        public bool UnderConstruction;
        public float Progress;
        public float AX, AZ, BX, BZ;
    }

    public enum DetectionSource
    {
        FogGrid = 0,
        OwnSideWithinFog = 1,
        Contact = 2,
        NoFog = 3,
        ObjectiveGarrison = 4
    }

    [System.Flags]
    public enum EnemyTraits
    {
        None = 0,
        Embarked = 1,
        CavalryMajority = 2,
        OnOwnSoil = 4,
        NearOwnCoast = 8
    }

    // Only what a sighting shows; never a destination, plan state or target.
    public struct EnemyDetection
    {
        public int InstanceId;
        public string UnitKey;
        public int UnitTyp;
        public float X, Z, Strength, Guns;
        public DetectionSource Source;
        public EnemyTraits Traits;
    }

    public struct EnemyBelief
    {
        public int InstanceId;
        public string UnitKey;
        public int UnitTyp;
        public float X, Z, Strength, Guns;
        public int LastSeenDay;
        public float Confidence01, UncertaintyRadius;
        public DetectionSource LastSource;
        public EnemyTraits Traits;
    }

    public struct CommanderRecord
    {
        public int Id;
        public int Rank;
        public float Initiative, Leadership, Administration;
        public float Average; // GetAverageAttributeValue (59175)
        public bool Available, Navy, Dummy, RefreshCommand, ServingUnderPlayer, IsPlayer;
        public int CommandInstanceId;
        public int CommandUnitTyp;
        public string Name;
    }

    public enum TheaterPosture
    {
        Hold = 0,
        MainEffort = 1,
        Support = 2,
        EconomyOfForce = 3,
        Screen = 4,
        Recover = 5
    }

    public enum FormationRole
    {
        Hold = 0,
        MainEffort = 1,
        Support = 2,
        Reserve = 3,
        CapitalGuard = 4,
        DepotBuilder = 5,
        Recover = 6,
        Recon = 7
    }

    public enum ReconKind
    {
        Screen = 0,
        Picket = 1,
        AdvanceGuard = 2,
        Probe = 3
    }

    public struct ReconTask
    {
        public ReconKind Kind;
        public int SectorId;
        public float FromX, FromZ, HoldX, HoldZ;
        public float Priority;
        public string Reason;
    }

    public sealed class SectorPlan
    {
        public int SectorId;
        public Theater Theater;
        public TheaterPosture Posture;
        public int TargetObjectiveId;
        public float TargetX, TargetZ;
        public float RequiredRatio;
    }

    public sealed class DepotTask
    {
        public float X, Z;
        public int SectorId;
        public float Priority;
    }

    public sealed class TheaterPlan
    {
        public int Alliance;
        public int DaySerial;
        public int MainSectorId = -1;
        public int MainObjectiveId = -1;
        public float MainRequiredRatio;
        public List<SectorPlan> Sectors = new List<SectorPlan>();
        public float CapitalFloorStrength;
        public List<DepotTask> DepotTasks = new List<DepotTask>();
        public string SwitchReason;
        public string PriorId;
    }

    public struct CommanderProfile
    {
        public int CommanderId;
        public float RequiredOddsShift; // (initiative - 0.5) * -0.5: bold commanders accept worse odds
        public float AttackBias;        // initiative - leadership * 0.5, clamped [-1, 1]
        public float SupplySlack;       // administration * 0.25 of commandrange
    }

    // GetMin/MaxRankForUnittyp (67003-67058) per unittyp for this alliance.
    public struct RankBand
    {
        public int UnitTyp;
        public int MinRank, MaxRank;
    }

    public struct Assignment
    {
        public int InstanceId;
        public FormationRole Role;
        public int SectorId;
        public float TargetX, TargetZ;
        public string Reason;
        public int ReconMissionId; // -1 none
        // Kept out of vanilla's offensive pool: Reserve, CapitalGuard, DepotBuilder, Recon,
        // Recover, and Hold at sector need (G4).
        public bool Withheld;
    }

    public sealed class Allocation
    {
        public int Alliance;
        public List<Assignment> Assignments = new List<Assignment>();
        public List<int> Shortfalls = new List<int>(); // instance ids, worst first
        public List<int> Surpluses = new List<int>();

        public bool TryGet(int instanceId, out Assignment assignment)
        {
            for (int i = 0; i < Assignments.Count; i++)
            {
                if (Assignments[i].InstanceId != instanceId) continue;
                assignment = Assignments[i];
                return true;
            }
            assignment = default(Assignment);
            return false;
        }
    }
}
