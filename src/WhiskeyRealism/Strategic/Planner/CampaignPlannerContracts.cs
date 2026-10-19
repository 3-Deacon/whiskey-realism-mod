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
        // The unit's rail and river pathing modes (Regiment.userails/userivers, 111466-111470; AddPath
        // 130435-130454). Non-player alliances get rails and rivers at init (112862-112867); the
        // player's alliance, including its AI-owned groups under ai_vs_ai, keeps its stored values
        // (field default false). G5a raises them to vanilla's AI default before a planner move.
        public bool UseRails, UseRivers;
        // Regiment.groupspeed: the slowest member's unitbasicspeed[t, 1] (UpdateGroupStrength
        // 120438-120440), starting at unitbasicspeed[15, 1] (120179); RegimentMove multiplies a
        // campaign group's speed by it (133991). G3's march-time estimate.
        public float GroupSpeed;
        // Regiment.timeofnotmoving (110614): the campaign date (GameVars.GetCampaignFloatingDate, years)
        // the group last moved. G5a's tempo roll takes the mass's idle time from it (vanilla 14321-14327).
        public float TimeOfNotMoving;
        // The state the formation stands in (BattlefieldSetup.GetStateOfField(position), the test vanilla's alliancetroops uses, 81654-81660),
        // -1 unknown. Stored plus one so an unset record reads -1.
        private int _state;
        public int StateId { get => _state - 1; set => _state = value + 1; }
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
        // The strength of the side's own and allied forts' garrison units that guard this target (G8: the fort's closest town, else its
        // parent or closest IIP), 0 for none. Own and allied forts only: an enemy fort's garrison reaches the side as a G1 belief
        // (spec C14/C25), never as a snapshot read.
        public float GarrisonStrength;
        // The state the target lies in (GetStateOfField), -1 unknown; stored plus one so an unset record reads -1.
        private int _state;
        public int StateId { get => _state - 1; set => _state = value + 1; }
    }

    // One town of the campaign map (BattleUnits.towns: a Town, never one of the small towns BattleUnits.smalltowns keeps apart): its name, position
    // and owner alliance (Town.Owner, the public map fact the covering corridor's posts are chosen from, foresight F6b-1). Listed in
    // BattleUnits.towns order; the place index is the index into CampaignSnapshot.Places.
    public struct PlaceRecord
    {
        public string Name;
        public float X, Z;
        public int Owner;
    }

    // A state the CSA can win by secession (Kentucky, Missouri: SecessionRules). Read live from vanilla each review, never cached.
    public struct SecessionRecord
    {
        public int ObjectiveId;     // 15 Kentucky, 16 Missouri
        public int StateId;
        public int StateAlliance;   // GameVars.nation[state].alliance
        public bool Live;           // SecessionRules.IsLive(owner, GameVars.nation[state].support[1]): vanilla's IsDeactivated is false
        public float GarrisonStrength; // Union only: strength of the side's own garrison units standing in the state (the flag's likely source)
        public bool OwnTroops;      // Union only: GameVars.nation[state].alliancetroops[0], vanilla's own "a Union group stands here" flag (garrisons included)
        public bool HasTown;
        public float TownX, TownZ;  // the objective's town (Louisville, St.Louis), from its objective record
    }

    // One of the side's own forts (G8; BattleUnits.fort entries with Owner == the alliance): a place a garrison post can stand at, and
    // the strength of the garrison dummy that guards it. An ally's fort is not a record (its garrison still adds to the target it guards).
    public struct FortRecord
    {
        public float X, Z;
        public float GarrisonStrength; // the fort's garrisonunit groupstrengthdirect, 0 with none
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
        public float Condition;
        public bool Built;
        public bool Invisible;
    }

    public struct RailLineRecord
    {
        public int Index;
        public float Progress;
        // The line's two farthest-apart IIPs (ConstructionSymbol.IIPsForTransportCapacityUpdate,
        // 188134): their positions and instance ids (= SupplySourceRecord.Id of the same IIP),
        // so G5c can score a line by what it connects.
        public float AX, AZ, BX, BZ;
        public int AIipId, BIipId; // -1 none
    }

    // A ford (terrain 5) or bridge (6) cell of the campaign terrain grid
    // (BattlefieldSetup.rivercrossingpositions, built by ReadInTerrainSpecs 26628-26638, cells
    // merged within 20). G3's river-line term routes a threat or an advance through the nearest one.
    public struct CrossingRecord
    {
        public float X, Z;
    }

    // How a sighting came about, as far as a decision branches on it: Seen is any fog, own-side or fog-off
    // sighting (the three provenances that used to be told apart were only ever assigned, never branched on;
    // sidecars written with the old values 1 and 3 load as Seen, see DetectionSources.FromSaved), Contact is
    // vanilla's campaign contact (counted in [EnemyBeliefs]), ObjectiveGarrison a fort or town garrison
    // sighting (keeps its radius), Mark a vanilla spotting mark (foresight F4, decision 2: where a hidden group was last shown, within a
    // MarkRadius of the truth; it counts in every strength sum and never scouts, lands a group, raises a raid or counts as contact).
    // The numbers are persisted: 2 and 4 stay as they were, Mark is 5.
    public enum DetectionSource
    {
        Seen = 0,
        Contact = 2,
        ObjectiveGarrison = 4,
        Mark = 5
    }

    public static class DetectionSources
    {
        // A saved DetectionSource: 2, 4 and 5 as written; 0, 1 and 3 (FogGrid, OwnSideWithinFog, NoFog) and anything unknown are Seen.
        public static DetectionSource FromSaved(int saved)
        {
            if (saved == (int)DetectionSource.Contact) return DetectionSource.Contact;
            if (saved == (int)DetectionSource.ObjectiveGarrison) return DetectionSource.ObjectiveGarrison;
            if (saved == (int)DetectionSource.Mark) return DetectionSource.Mark;
            return DetectionSource.Seen;
        }
    }

    [System.Flags]
    public enum EnemyTraits
    {
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
        public float X, Z, Strength;
        public DetectionSource Source;
        public EnemyTraits Traits;
        // G9: the strength-weighted WSF of the seen group's armed units, per weapon lane (infantry and cavalry rifles, guns), 0 when
        // none was seen in the lane. What a sighting shows of the enemy's arms, never read from an alliance's averageweaponstrength*.
        public float RifleQuality, GunQuality;
        // Foresight F4: a Mark detection's calendar day (MarkRules.MarkDay, at most today) and its radius (MarkRules.MarkRadius); 0 otherwise.
        public int MarkDay;
        public float MarkRadius;
    }

    public struct EnemyBelief
    {
        public int InstanceId;
        public string UnitKey;
        public int UnitTyp;
        public float X, Z, Strength;
        public int LastSeenDay;
        public float Confidence01, UncertaintyRadius;
        public DetectionSource LastSource;
        public EnemyTraits Traits;
        // The last review day the group was seen Embarked, -1 never (G1): a group seen ashore on
        // own soil within 3 days of being seen afloat is a landing (sea-invasion inference, C25).
        public int LastEmbarkedDay;
        // G9: the lane qualities of the last sighting that showed the lane (EnemyDetection.RifleQuality/GunQuality); carried unchanged
        // while the group is unseen, 0 until a sighting shows it.
        public float RifleQuality, GunQuality;
        // Foresight F4: the radius a mark belief starts from (MarkRadius), 0 for a sighting or a contact; the uncertainty radius is this plus
        // the drift of the days since the mark. Persisted (baseRadius).
        public float BaseRadius;
    }

    public struct CommanderRecord
    {
        public int Id;
        public int Rank;
        public float Initiative, Leadership, Administration;
        public float Average; // GetAverageAttributeValue (59175)
        public bool Available, Navy, Dummy, RefreshCommand, ServingUnderPlayer, IsPlayer;
        // Vanilla's AI appointment pool, CanBePickedForAIReplacement(alliance,
        // includeofficerswithcommand: true) (60748-60779): available, not serving under the W&L
        // player, not on a staff, not an army-group commander, not the alliance CIC (G2).
        public bool Pickable;
        // The same tests without the army-group one (main's C-CmdrReplace FreeApartFromArmyGroup):
        // IsAvailable, not serving under the player, not on a staff, not the CIC. G2's succession
        // reseat path for a historical figure vanilla seated on an auto-created army group (75653).
        public bool FreeApartFromArmyGroup;
        public int CommandInstanceId;
        public int CommandUnitTyp;
        public string Name;
    }

    // One vanilla ArmyGroup of the snapshot's alliance (BattleUnits.armygroups, 75007; its
    // commander is ArmyGroup.GetCommanderID, 75541, and has no currentcommand). Position and
    // strength are the members' mean position and summed groupstrengthdirect. G2's appointment
    // targets (ArmyGroup.AppointCommander, 75559).
    public struct ArmyGroupRecord
    {
        public int InstanceId;
        public int CommanderId;
        public float X, Z;
        public float Strength;
        public int[] MemberInstanceIds; // attachedunits (top formations)
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
        Recon = 7,
        // A formation sent to stand at a garrison post until it is there; once there it is Hold in place, withheld. One role for both
        // sorts of post: D8's secession garrison (a live objective 15/16 the CSA could win; reason secession-garrison) and G8's posts
        // beyond the capital (an objective town, rail hub, port, depot or fort; reason garrison-post).
        Garrison = 8,
        // A formation the mover sends to a hold point of its own side (foresight F6a, D11 requirement-redeploy-role): a covering post
        // (reason covering-force) or a short department's sector (department-reinforcement). Within the bugle range of the point it is an
        // ordinary Hold at the next review.
        Redeploy = 9
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
        // (HoldRatio + Tempo.MinimumHoldRatioModifier) x the sector's river-weighted believed land
        // threat (TheaterPlanner.SectorThreat): G4's hold need is this value, never recomputed (F9).
        public float HoldNeed;
        // The believed term of that need alone: (HoldRatio + modifier) x the believed land threat. The need is belief-driven when this is the
        // larger term (above the garrison floor and not below HoldNeed); otherwise the floor or the presumption of an unscouted front sets it.
        // ForceAllocator reads it to decide whether a scout may be drawn from the sector's hold reserve.
        public float BelievedHold;
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
        // The main candidate's target (ObjectiveRecord.X/Z): the objective's position for readers outside
        // the planner (the front ledger's target theater, the formation directives' target area). Not the
        // clamped frontier point of SectorPlan.TargetX/Z.
        public float MainObjectiveX, MainObjectiveZ;
        public float MainRequiredRatio;
        public List<SectorPlan> Sectors = new List<SectorPlan>();
        public float CapitalFloorStrength;
        // Foresight F5 (D11 requirement-live-min-strength): the smallest strength the AI moves a formation at, the scenario's
        // aiminimumstrengthformovement (002/A 2000, Config 5000) read by TheaterPlanner.Plan, the 5000 fallback when that read is not positive
        // or not finite and, with Enable Strategic Foresight Picture off, always. Every floor and hold target derives from it; a plan a test
        // builds by hand keeps the fallback.
        public float MinGarrisonStrength = TheaterPlanner.MinStrengthFallback;
        // Foresight F3 (D11 requirement-capital-window and -floor): the numbers behind the capital floor, for the [CapitalThreat] row and
        // the [CapitalFloor] row. CapitalReach is the race, max(RelocationRange, CapitalReliefDays x CapitalCorpsPerDay); CapitalWeighted
        // vanilla's weight (11727) over the picture's elements inside it; CapitalThreat01 the 0.6 trigger's input (baseline excluded).
        public float CapitalReach, CapitalWeighted, CapitalThreat01, CapitalReliefDays, CapitalCorpsPerDay;
        public List<DepotTask> DepotTasks = new List<DepotTask>();
        public string SwitchReason;
        public string PriorId;
        // The massable set the main effort's odds were judged on (TheaterPlanner.MassFor): own movable
        // committed formations within MarchHorizonDays of the main target, net of the capital floor
        // and of every other sector's hold. G4 assigns MainEffort to exactly these instance ids.
        public List<int> MassInstanceIds = new List<int>();
        public float MassStrength;
        // The believed land enemy G3 judged the main objective's odds against (confidence-weighted, river-
        // weighted, within the relocation range of the objective itself, not of the clamped frontier point
        // in SectorPlan.TargetX/Z): G4's reserve split keeps the odds it judged.
        public float MainEnemyStrength;
        // Scout first (smoke amendments 2026-10-16, D1): whether the main objective is scouted, i.e. a fresh land belief lies within
        // the relocation range of it or an own formation's sight covers it. Unscouted, MainEnemyStrength is the presumed threat
        // (TheaterPlanner.PresumedAt), not a reading, and the main effort is Staged: its mass assembles at (StartLineX, StartLineZ),
        // the forward-most mass member's position, and does not march on the objective until the picture is scouted and the odds hold.
        public bool MainScouted;
        public bool MainStaged;
        public float StartLineX, StartLineZ;
        // Push only what it needs (D3): the force the main objective needs (MainEnemyStrength x MainRequiredRatio x (1 + margin)),
        // and what the planner could concentrate for it (the capacity the odds were judged on). MassStrength is the selected mass.
        public float MainNeedStrength, MainCapacityStrength;
        public bool MainScoutLatched;   // scouted earlier in this decision, no fresh picture now (PlannerState.ScoutedDecision)
        // D6: the one formation from another theater that joins the mass (instance id), -1 none. It is the Support formation of the push.
        public int ReinforcementInstanceId = -1;
        // The reinforcement's name and cross-department move (telemetry): From is the department it left on the day of the transfer
        // (Unknown on later days), To the main objective's department.
        public string ReinforcementName;
        // Telemetry: the count of top formations per home department ("East:9,West:5"), empty with none.
        public string DepartmentCounts = "";
        public Theater ReinforcementFrom, ReinforcementTo;

        // D8: the secession garrisons of this review (Union side: one post per live secession state it owns).
        public List<GarrisonPost> Garrisons = new List<GarrisonPost>();

        // Foresight F6a (D11 requirement-claims): who owns each own formation at the end of the review, one Claim per formation in snapshot
        // order (ClaimTable), and per sector the hold the reserve was sized on (SectorHeld: ReserveRules.Hold's sum, D9's measure). The
        // allocator assigns roles from the table and never re-runs the capital or hold reserve.
        public List<Claim> Claims = new List<Claim>();
        public float[] SectorHeld = new float[0];

        // Foresight F6b-2 (D11 requirement-covering-lifecycle): the covering force between the two capitals, rebuilt each review and never null
        // after Plan. Kind None carries the Reason it is unbound.
        public CoveringPlan Covering = new CoveringPlan();

        // Foresight F7 (D11 requirement-commitment): the instance ids whose commitment to the last mass ended this review, in snapshot order: the
        // members the last mass had that the review's mass did not keep (the committed-wait formations of a push that ended, and the members a
        // continuing push dropped). The mover takes each that is still in vanilla's offensive list off it, unless it is a MainEffort or Support
        // again; [Reserves] endedListed= and endedMarching= read the previous review's.
        public List<int> CommitmentEnded = new List<int>();

        // Foresight F8 (D11 requirement-department-reinforcement): the departments whose hold is short (HoldStrength standing in them under their
        // need), the shortest first, each with the outcome of this review's pick (Status); and the moves of this review (picked, arrived, gave
        // up), which the runtime writes as [DepartmentReinforcement] rows. Both empty with Enable Department Reinforcement off.
        public List<DepartmentShortfall> DepartmentShort = new List<DepartmentShortfall>();
        public List<DepartmentTransition> DepartmentTransitions = new List<DepartmentTransition>();

        // The plan's sector with this id, null for none (-1 included).
        public SectorPlan SectorById(int sectorId)
        {
            if (sectorId < 0) return null;
            for (int i = 0; i < Sectors.Count; i++)
                if (Sectors[i] != null && Sectors[i].SectorId == sectorId) return Sectors[i];
            return null;
        }
    }

    // A department whose hold is short (D11 requirement-department-reinforcement): the hold strength standing in it against what it needs,
    // and what this review did about it: donor (it was given a formation), nodonor (the first ranked unlocked department, and no formation
    // passed the pool), locked (a move is under way toward it), untried (ranked after the one tried) or push (the review has a main effort).
    public struct DepartmentShortfall
    {
        public Theater Theater;
        public float Need, Have;
        public string Status;
    }

    // One step of a department move in a review: a formation picked for a short department (the target and the march days estimated at the
    // pick), arrived in it (its home department changed) or gave up on it (the tracker gave up on the march).
    public struct DepartmentTransition
    {
        public int InstanceId;
        public string Name;
        public Theater From, To;
        public float TargetX, TargetZ;
        public float Days;          // MarchTime.EstimateDays at the pick, -1 on the other steps
        public string State;        // DepartmentReinforcement.StatePicked, StateArrived, StateGaveUp or StateDropped
    }

    // Where the covering force stands (D11 requirement-covering-post-choice): the forward post, at the relocation range from the enemy
    // capital, or the rear post, at the relocation range from the own capital. With one post the kind is the raw verdict of the review.
    public enum CoveringKind
    {
        None = 0,
        Forward = 1,
        Rear = 2
    }

    // The covering force of one review (foresight F6b-2): the post it stands at, the men the axis sectors' hold reserves give it against the
    // men it needs, and the defence flag the withhold view reads. Published with the plan; the allocator reads the members from the ownership
    // table (reason covering-force), the withhold view reads ThreatNear and the telemetry row everything.
    public sealed class CoveringPlan
    {
        public string PostName;
        public float PostX, PostZ;
        public CoveringKind Kind;
        // Radius is the relocation range the post and the threat test are measured in. Held is the counted strength (the members plus the
        // formations that stand at the post and cannot be massed); Need the strength the force is sized to.
        public float Need, Held, Radius;
        // A live land belief of the side's own, of any source, lies on own soil within Radius of the post at plan time (the case vanilla's
        // own defence answers): the covering members are then available to it.
        public bool ThreatNear;
        public List<int> InstanceIds = new List<int>();
        // Why Kind is None: no-capital, no-corridor-place, no-axis-sector, flag-off, cic-side, main-effort-enemy-capital. Null when posted.
        public string Reason;
    }

    // What a garrison post guards. Secession is D8's (a state the CSA could win); the others are G8's posts beyond the capital.
    public enum GarrisonPostKind
    {
        ObjectiveTown = 0,
        RailHub = 1,
        Port = 2,
        Depot = 3,
        Fort = 4,
        Secession = 5
    }

    // One garrison post. D8's (Kind Secession): the post is a secession state's objective town, the formations that hold it and whether one
    // already stands in the state. G8's (any other Kind): a place away from the capital, the believed threat it needs covering
    // (Need) and the strength already there (ExistingStrength); the allocator seats one formation at it. Both sorts ride TheaterPlan.Garrisons,
    // are seated by ForceAllocator under the one Garrison role and recorded by the one GarrisonLedger.
    public sealed class GarrisonPost
    {
        public GarrisonPostKind Kind = GarrisonPostKind.Secession;
        public float ExistingStrength;             // G8: the fort garrison dummy and the own formations at the post, net of its own incumbent
        public int QuietSinceDay = -1;             // G8: the review day from which no live land belief has lain within the relocation range of the post, -1 while one does
        public string StateName;
        public int StateId;
        public float X, Z;
        public bool Marching;                      // a member outside the state is on its way to the town
        public bool MassPresence;                  // the only own formations inside the state are the main effort's mass, which is not pinned
        public bool PlayerPresence;                // the only own formations inside the state are a player's or a CIC side's: present, but not members
        public bool InState;                       // the Union is present in the state: an own formation stands in it, or vanilla's alliancetroops[0] is set
        public float Need;                         // the D2 hold need at the post (telemetry: the [Allocation] garrison= term)
        public float Shortfall;                    // the part of a flag-alone need only the main effort's mass could have met (never taken from it)
        public List<int> InstanceIds = new List<int>(); // the garrison: in-state formations nearest the post up to the need, else the one posted
        public string Name;                        // the first member's name (telemetry)
    }

    public struct CommanderProfile
    {
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

    // Who owns an own formation at the end of a review, in the order the rows of ClaimTable take it (D11 requirement-claims). None is
    // unclaimed: the later steps of the allocator place it.
    public enum Claimant
    {
        None = 0,
        PinnedBuilder = 1,   // the builder of a Wished depot task
        Immobile = 2,        // busy, non-combat, or the player's own command
        Recover = 3,         // leaving for supply
        Committed = 4,       // a dispatched member of the main effort waiting out the end of its push
        DepartmentMove = 5,  // on its way to a short department
        Capital = 6,         // the capital reserve
        Hold = 7,            // a sector's hold reserve, or a covering member
        Mass = 8             // the main effort's selected mass
    }

    // One own formation's owner. SectorId and Share are where the hold pass counted the formation and what it added there (-1 and 0 when
    // nowhere): the numbers HoldLeaving and the scout draw read. TargetX/Z is where the owner's role sends it; Reason names the owner's
    // kind of hold (covering-force) where the role cannot tell.
    public struct Claim
    {
        public int InstanceId;
        public Claimant Claimant;
        public int SectorId;
        public float Share;
        public float TargetX, TargetZ;
        public string Reason;
    }

    public struct Assignment
    {
        public int InstanceId;
        public FormationRole Role;
        public int SectorId;
        public float TargetX, TargetZ;
        public string Reason;
        // Kept out of vanilla's offensive pool: Reserve, CapitalGuard, DepotBuilder, Recon,
        // Recover, and Hold at sector need (G4).
        public bool Withheld;
    }

    // A formation the allocator seated at a garrison post (either sort): which post of TheaterPlan.Garrisons, which formation, and whether
    // it already stands at the post (then it holds in place) or is marching to it. GarrisonLedger.Sync reads these.
    public struct GarrisonSeat
    {
        public int PostIndex;
        public int InstanceId;
        public bool AtPost;
    }

    public sealed class Allocation
    {
        public int Alliance;
        public List<Assignment> Assignments = new List<Assignment>();
        public List<GarrisonSeat> Seats = new List<GarrisonSeat>();
        public List<int> Shortfalls = new List<int>(); // instance ids, worst first
        public List<int> Surpluses = new List<int>();
        // Why each recon task with no scout has none (smoke 2026-10-18: tasks=6 unfilled=6 why=- on every review): the distinct reasons in the
        // order of the tasks (ForceAllocator.ReasonUnfilled*), "believed-threat" first when it applies, null when every task has a scout.
        // The [Recon] row's why=.
        public string ReconUnfilledWhy;
        // The formations a detachment-standard scout pick could still take when the recon step began: ready, unbusy and claimed by no role
        // before it. The [Recon] row's free= (pool= is the snapshot's count before any role).
        public int ReconFree;
        // The main effort's own bodies a scout pick could still take at that point (ready, unclaimed, in the mass). The row's mass=; not in free=.
        public int ReconMass;

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
