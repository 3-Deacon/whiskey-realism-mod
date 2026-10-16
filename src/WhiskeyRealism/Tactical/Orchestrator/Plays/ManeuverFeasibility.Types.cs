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
    }

    /// <summary>What the army, the allocator and BC11's runtime read of the published ledger.</summary>
    public interface IFeasibilityLookup
    {
        bool TryGet(BattlePlanId plan, out CandidateResult result);

        /// <summary>The plan the ledger's per-child table is for (the selected plan, captured after every pick); Unknown before the first.</summary>
        BattlePlanId SelectedPlan { get; }

        /// <summary>
        /// The selected plan's release for one direct child (requirement-synchronized-release): the absolute doctrine hour its Hold ends, the time
        /// budget its march gets, and the march via and attack via (NaN: none). False for a child the plan gives no release (a fix in contact, a
        /// deployed reserve, a child with no entry).
        /// </summary>
        bool TryPlannedRelease(string childId, out double releaseHours, out double timeBudgetHours, out float viaX, out float viaZ,
            out float attackViaX, out float attackViaZ);

        /// <summary>The regions a child's vias lead into (-1: no via): once the child stands in one, the via is behind it.</summary>
        bool TryViaRegions(string childId, out int viaFarRegion, out int attackViaFarRegion);
    }

    /// <summary>
    /// One evaluation's inputs. The runtime builds it from the side's snapshot, the contact ledger, the intent inference and the
    /// personality table; a test builds it from fixtures. Arrays are the caller's and are only read.
    /// </summary>
    public struct FeasibilityInput
    {
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
        /// <summary>The plans the catalog's precondition and stance filters leave for the side's current effective stance.</summary>
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
    }
}
