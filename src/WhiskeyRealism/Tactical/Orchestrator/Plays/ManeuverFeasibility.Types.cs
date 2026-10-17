using System;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    // BC14's value types (spec § 8). All pure; the arrays they point into are the ledger's pooled buffers.

    /// <summary>The BC13 exploit table as the feasibility evaluation sees it (a seam, so the evaluation stays pure).</summary>
    public interface IExploitLookup
    {
        /// <summary>The instance is the asset of a live exploit, or a descendant of it: not offered to a play's layout.</summary>
        bool IsExploiting(int instanceId);

        /// <summary>The instance is an ancestor of such an asset: it stays in the layout, at half strength (the asset is away from its slot).</summary>
        bool HasExploitingDescendant(int instanceId);
    }

    /// <summary>
    /// One body of a candidate play (requirement-bodies): an enveloping child moving on its own route, or the main body, every other
    /// assigned child together. The strike and risk terms are filled in the order the evaluation computes them.
    /// </summary>
    public struct BodyEstimate
    {
        public int Body, ChildCount;
        /// <summary>Where the body strikes: the enemy line end on its side (a DeepRaid: its outflank point), or for the main body the play's enemy anchor.</summary>
        public float StrikeX, StrikeZ;
        /// <summary>The enveloping child's id; for the main body its highest-priority fixing member's. Matches bodies across evaluations.</summary>
        public string ChildId;
        public bool Envelops, Main;
        public PlayRectKind Kind;
        public float Strength, Morale, TargetX, TargetZ;
        public float RouteCost;
        public bool RouteEstimated, RouteConfirmed;
        /// <summary>A main-body strike leg has no path (reason <c>no-strike-crossing</c>): its member can still hold its anchor.</summary>
        public bool NoStrikePath;
        /// <summary>Link indexes of the first crossing of the march leg and of the inward leg, -1 none.</summary>
        public int MarchVia, AttackVia;
        public CrossingState MarchCrossing, AttackCrossing;
        public float InwardSurplusHours;
        /// <summary>The main body's strike legs' weighted state value.</summary>
        public float StrikeContested;
        public float CourierHours, MarchHours, StrikeHours, DesiredStrikeHours, ReleaseHours, OffsetHours;
        /// <summary>The strength this body pins at its strike (an enveloping body's, the main body's fixing members' without its reserve) and the defence ratio there.</summary>
        public float PinStrength, DefenseRatio;
        /// <summary>
        /// The detail risk's working terms (requirement-detail-risk): the enemy's shift hours to this body (tau), the own support time (sigma) and the
        /// exposure window (W), the enemy near the strike point (L), the pinned enemy, the enemy against this body (E), the own ratio and the isolated odds.
        /// </summary>
        public float EnemyShiftHours, SupportHours, ExposureWindowHours, LocalEnemy, Pin, EnemyAgainst, OwnRatio, IsolatedOdds;
        /// <summary>The terms of the penalty.</summary>
        public float Daylight, Detail, Contested, Approach, Exposure, Turn, Penalty;
        /// <summary>The reason literal when this body's penalty is largest (set with the verdict).</summary>
        public string Reason;
        /// <summary>
        /// BC14W (requirement-works-pricing): the body's strike point lies on held works. <see cref="AssaultsWorks"/> when the body attacks them
        /// (an enveloping body, a single-body plan's main body, a focal assault's assault body): it is held to <see cref="BarUsed"/>, three times the
        /// commander's bar. A fixing main body whose strike point lies on works holds there and assaults nothing (requirement-turn-first).
        /// </summary>
        public bool StrikeOnWorks, AssaultsWorks;
        /// <summary>The bar the detail term was measured against (the commander's, or three times it for a works assault).</summary>
        public float BarUsed;
        /// <summary>A holding fix's enemy outside works (requirement-turn-first): the enemy against it counts only that; 0 for every other body.</summary>
        public float EnemyOpen;
    }

    /// <summary>One candidate play with its evaluated side and verdict; its bodies are <c>BodyStart..BodyStart+BodyCount</c> in the ledger's body array.</summary>
    public struct CandidateResult
    {
        public BattlePlanId Plan;
        public bool Mirrored;
        public FeasibilityVerdict Verdict;
        public float Penalty;
        /// <summary>A static literal: the verdict's reason, or the name of the largest penalty term.</summary>
        public string Reason;
        public int BodyStart, BodyCount;
        /// <summary>Into the ledger's pooled <see cref="MemberEstimate"/> array: one entry per assigned child.</summary>
        public int MemberStart, MemberCount;
        /// <summary>Into the ledger's pooled <see cref="RouteCrossing"/> array: the crossings of all the members' legs (a member's own are <see cref="MemberEstimate.CrossingStart"/> into this range).</summary>
        public int CrossingStart, CrossingCount;
    }

    /// <summary>
    /// One crossing on a member's route, recorded with the evaluation (requirement-assumption-monitor): the link, the region the route
    /// continues in after it (a child standing there has passed the crossing) and whether the crossing was Held when the route was priced
    /// (a crossing carried knowingly is no broken assumption).
    /// </summary>
    public struct RouteCrossing
    {
        public int Link, FarRegion;
        public bool HeldAtEvaluation;
    }

    /// <summary>
    /// One crossing a Defender's flank can be turned at (requirement-defender-exposure), as the evaluation of the selected candidate found it:
    /// where it is, which bank is the side's own, when the enemy gets there and onward to the flank, how soon the side's reserves can, and the
    /// place a released reserve guards it from. The allocator compares and writes the guard point; it holds no grid and no snapshot.
    /// </summary>
    public struct CrossingThreat
    {
        public int Link;
        public float X, Z;
        /// <summary>The crossing is on the left (the lower-lateral side) of the own line.</summary>
        public bool Left;
        /// <summary>The front-axis band the crossing's lateral falls in (the released child's sector override).</summary>
        public int Sector;
        public int OwnBankRegion;
        /// <summary>Where a reserve released to this crossing stands (on own-bank land behind the crossing) and the heading it faces: toward the crossing.</summary>
        public float GuardX, GuardZ, GuardHeading;
        /// <summary>The earliest arrival at the crossing over the enemy bodies not yet in the own bank's region (hours from the evaluation), and onward to the candidate's flank end.</summary>
        public float EnemyAtCrossing, EnemyAtFlank;
        /// <summary>The fastest of the candidate's reserve sources to reach the crossing.</summary>
        public float CandidateOwnShift;
        /// <summary>The earliest enemy body already stands in the own bank's region: contact, answered by BC13's crisis release, not a crossing threat.</summary>
        public bool EnemyAcross;
    }

    /// <summary>
    /// BC14W (requirement-defender-works-reserve): where a Defender's reserve stations behind its most threatened focal point of its own works: the focal
    /// point, the station <see cref="ManeuverFeasibility"/> picked <see cref="WorksRules.ReserveStationMeters"/> behind it along the own front axis (on own
    /// land), the heading that faces it, and the hours the earliest enemy body needs to reach the focal point. <see cref="Has"/> false is none.
    /// </summary>
    public struct StationPoint
    {
        public bool Has;
        public float FocalX, FocalZ, X, Z, Heading, ArrivalHours;
    }

    /// <summary>What the army, the allocator and BC11's runtime read of the published ledger.</summary>
    public interface IFeasibilityLookup
    {
        bool TryGet(BattlePlanId plan, out CandidateResult result);

        /// <summary>The doctrine hour the published evaluation started at; meaningful once <see cref="HasEvaluation"/>.</summary>
        double EvaluatedHours { get; }

        /// <summary>An evaluation has been published.</summary>
        bool HasEvaluation { get; }

        /// <summary>The plan the ledger's per-child table is for (the selected plan, captured after every pick); Unknown before the first.</summary>
        BattlePlanId SelectedPlan { get; }

        /// <summary>
        /// The selected plan's release for one direct child (requirement-synchronized-release): the absolute doctrine hour its Hold ends, the time
        /// budget its march gets, and the march via and attack via (NaN: none). False for a child the plan gives no release (a fix in contact, a
        /// deployed reserve, a child with no entry).
        /// </summary>
        bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ,
            out float attackViaX, out float attackViaZ);

        /// <summary>
        /// An enveloping child's predicted arrival at its turn point (absolute doctrine hours: its strike S* less DeployHours) and its predicted
        /// march hours, as the table last held them (BC11 latches them at the March for the TacticalEnvelopment row). False for any other child.
        /// </summary>
        bool TryPredictedArrival(string childId, out double arrivalHours, out double marchHours);

        /// <summary>The regions a child's vias lead into (-1: no via): once the child stands in one, the via is behind it.</summary>
        bool TryViaRegions(string childId, out int viaFarRegion, out int attackViaFarRegion);

        /// <summary>
        /// The army selected (or kept) a plan on a side: the ledger keeps its identity, snapshots the selected candidate's evaluation for the
        /// assumption monitor, and builds the per-child table BC11 reads. Called after every pick and every kept plan.
        /// </summary>
        void CaptureSelection(BattlePlanId plan, bool mirrored);

        /// <summary>The assumption the monitor found broken (a static literal), or null; cleared by the next <see cref="CaptureSelection"/>.</summary>
        string BrokenAssumption { get; }

        /// <summary>The crossings a Defender's flank can be turned at, for the selected candidate (earliest enemy arrival first); 0 for an Attacker or with no plan selected.</summary>
        int ThreatCount { get; }

        /// <summary>
        /// The i-th threat, with the time each direct child (in the evaluation's child order, <paramref name="childCount"/> of them) needs to
        /// reach it (courier plus march; NaN for a child with player-owned strength) written to <paramref name="ownShiftByChild"/>.
        /// </summary>
        bool TryThreat(int index, out CrossingThreat threat, float[] ownShiftByChild, out int childCount);

        /// <summary>The direct child id of column <paramref name="column"/> of the matrix <see cref="TryThreat"/> writes; null outside it. The consumer maps the columns to its own children by id.</summary>
        string ThreatChildId(int column);

        /// <summary>
        /// Up to <see cref="ManeuverFeasibility.MaxLookRequests"/> look points (world x and z) at link crossings the side does not know
        /// (requirement-crossing-look), most pressing first; the count written. A request ends when the crossing turns Clear, Watched or Held.
        /// </summary>
        int LookRequests(Span<float> xs, Span<float> zs);

        /// <summary>BC14W: the focal point of the enemy works the published evaluation found (the point FocalAssault faces); false with none.</summary>
        bool TryFocalPoint(out FocalPoint focal);

        /// <summary>BC14W: the works hold of the selected plan (<c>hopeless-assault</c> or <c>turn-not-struck</c>); false, reason null, with none.</summary>
        bool WorksHold(out string reason);

        /// <summary>BC14W: the child is a member of a works-assaulting body of the selected plan.</summary>
        bool AssaultsWorks(string childId);

        /// <summary>BC14W: the child assaults works or, as a holding fix, faces them (requirement-turn-first).</summary>
        bool FacesWorks(string childId);

        /// <summary>BC14W: a hash of the works hold, its reason and the set of children that face works; a change forces a store of the stored orders.</summary>
        int WorksHoldHash { get; }

        /// <summary>BC14W: the army commander's willingness to assault works as of the last evaluation (<see cref="WorksRules.Willingness"/>); -1 with the flag off or before one.</summary>
        float Willingness { get; }

        /// <summary>BC14W: the held works of the latest built evaluation (the exploit guard, the probe nomination and the lane check ask it); null with none.</summary>
        IWorksQuery HeldWorks { get; }

        /// <summary>
        /// BC14W (requirement-defender-works-reserve): the station behind the Defender's most threatened own focal point, with each direct child's time to it
        /// (courier plus march; NaN for a child holding player-owned strength) written to <paramref name="ownShiftByChild"/> in the column order of
        /// <see cref="StationChildId"/>. False with none.
        /// </summary>
        bool TryStation(out StationPoint station, float[] ownShiftByChild, out int childCount);

        /// <summary>BC14W: the child id of a column of the station's time vector; null past the last.</summary>
        string StationChildId(int column);

        /// <summary>BC14O: the pressure of the victory clock at the published evaluation (U and H are the selection's urgency and hold bias).</summary>
        ObjectivePressure Pressure { get; }

        /// <summary>BC14O: the child holds player-owned strength at the published evaluation (it is never drawn to screen a secondary objective).</summary>
        bool HoldsPlayerOwned(string childId);

        /// <summary>BC14O: the rows of the objective table.</summary>
        int ObjectiveCount { get; }

        /// <summary>BC14O: the i-th row of the objective table; false past the last.</summary>
        bool TryObjective(int index, out ObjectiveEstimate estimate);
    }

    /// <summary>
    /// One evaluation's inputs. The runtime builds it from the side's snapshot, the contact ledger, the intent inference and the
    /// personality table; a test builds it from fixtures. Arrays are the caller's and are only read.
    /// </summary>
    public struct FeasibilityInput
    {
        /// <summary>The monitor's one-candidate run: it builds no look list (the ledger's published one is the full evaluation's).</summary>
        public bool SkipLooks;
        public BattleSnapshot Snapshot;
        public TacticalFrontAxis Axis;
        public BattleStance Stance;
        public BattleTerrainGrid Grid;
        public TerrainPicture Picture;
        public RiverRegions Regions;
        public CrossingWatch Crossings;
        public MarchRules Rules;
        /// <summary>The army commander's temperament (BC13's table entry, or the roster vector with the personality flag off).</summary>
        public OpportunityPersonality Army;
        /// <summary>The contact ledger's EstimatedEnemyStrength and Confidence(now).</summary>
        public float EnemyEstimate, Confidence;
        public TacticalIntentModel EnemyIntent;
        public double NowHours, DaylightHours;
        public BattlePlanId CurrentPlan;
        public bool HasPlan;
        public IExploitLookup Exploits;
        public IConfirmedLegs Legs;
        /// <summary>The side's direct children ("child-&lt;id&gt;") and, per child, whether BC10 placed it as a deployment reserve.</summary>
        public string[] ChildIds;
        public int ChildCount;
        public bool[] DeployedReserve;
        /// <summary>The children bound to guard a crossing at the previous allocation (BoundGuardCount of them): their regiments are left out of the crossing cover test.</summary>
        public string[] BoundGuardChildIds;
        public int BoundGuardCount;
        /// <summary>Per child (same order): BC11 has it past its Hold, marching, turning or attacking: its time to strike carries no courier term.</summary>
        public bool[] ChildMoving;
        /// <summary>
        /// The plans the catalog's precondition and stance filters leave for the side's current effective stance, best catalog score (without the
        /// feasibility term) first: the first <see cref="ManeuverFeasibility.ConfirmCandidates"/> are the ones whose routes are confirmed and looked at.
        /// </summary>
        public BattlePlanId[] Candidates;
        public int CandidateCount;
        /// <summary>The crest line across the front (ReverseSlopeDefense's anchor); <c>Has</c> false when there is none.</summary>
        public TerrainLine Crest;
        /// <summary>The reach (RangeOnlyReach) of a regiment of each unit type, indexed by <c>UnitTyp</c>: where a perceived regiment would see a column.</summary>
        public float[] ReachByUnitType;
        /// <summary>BC10's assumed enemy point, used as the enemy mass when the side perceives no enemy at all.</summary>
        public bool HasAssumedEnemy;
        public float AssumedEnemyX, AssumedEnemyZ;
        public LegCostCache Cache;
        /// <summary>BC14W: the works points the side has perceived (the runtime's ObstacleLine pieces); null reads the works samples alone.</summary>
        public IFortificationPoints Works;
        /// <summary>BC14O: the live objectives (gameObject.activeInHierarchy), at most <see cref="ManeuverFeasibility.MaxObjectives"/>; the runtime's pooled array.</summary>
        public ObjectiveFact[] Objectives;
        public int ObjectiveCount;
        /// <summary>BC14O: the victory clock; <see cref="HasClock"/> false means no pressure (U = H = 0).</summary>
        public VictoryClock Clock;
        public bool HasClock;
        /// <summary>BC14O: each due designer chain's next objective (the capture's id), de-duplicated: the historical prior.</summary>
        public string[] DueChainObjectiveIds;
        public int DueChainCount;
        /// <summary>BC14O: the children that had a screen target at the previous allocation (their strength screens a secondary objective, not the main effort's).</summary>
        public string[] ScreenChildIds;
        public int ScreenChildCount;
    }
}
