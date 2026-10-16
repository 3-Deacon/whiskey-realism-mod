using System;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 (spec docs/superpowers/specs/2026-10-15-battle-opportunity-design.md § 6): the per-commander opportunity
    // decision. Pure. Each AI corps and division commander (the army commander for his brigades when a side has neither)
    // weighs the confirmed openings his side perceives against the assets he commands, by his own temperament, and
    // launches at most one bounded exploit on a deterministic roll. The producer owns everything around Decide: the
    // decision interval, the asset eligibility inputs, the hesitation and claim bookkeeping, the exploit entry.

    public struct OpportunityPersonality
    {
        public float Agg01, Caution01, Audacity01, CasualtyTolerance01;
        /// <summary>1 in duty, vanilla's reductionfactorcommanderoutofduty (0.5) when the commander is captured, wounded or killed.</summary>
        public float Duty;
        /// <summary>Where the vector came from (telemetry only).</summary>
        public PersonalitySource Source;
    }

    /// <summary>One direct child of the deciding node the producer found eligible, with what Decide weighs (spec § 8).</summary>
    public struct ExploitAsset
    {
        public int Id, OwnIndex, Sector;
        public float X, Z, Lateral, Strength, GroupMorale, GroupWidth, LossFraction;
        public bool Cavalry, InContact, HasFrontage, InMainEffort;
        /// <summary>Role Reserve, or a ReserveWait / FormUp / Hold task, out of contact, no live exploit: counts toward the reserve floor.</summary>
        public bool Uncommitted;
        public CommandTaskType Task;
    }

    public interface IHesitationLookup
    {
        bool IsHesitating(int commanderIndex, int openingId, double nowHours);
    }

    /// <summary>
    /// A failed roll marks the (commander, opening) pair as passed over for <see cref="OpportunityDoctrine.HesitationHours"/>; an
    /// aborted exploit marks its commander for the opening too. Per side, BattleScope; pooled and bounded.
    /// </summary>
    public sealed class HesitationStore : IHesitationLookup
    {
        private int[] _commander = new int[16], _opening = new int[16];
        private double[] _until = new double[16];
        private int _count;

        public void Mark(int commanderIndex, int openingId, double nowHours)
        {
            double until = nowHours + OpportunityDoctrine.HesitationHours;
            for (int i = 0; i < _count; i++)
            {
                if (_commander[i] != commanderIndex || _opening[i] != openingId) continue;
                _until[i] = until;
                return;
            }
            // drop what has run out before growing
            int kept = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_until[i] <= nowHours) continue;
                _commander[kept] = _commander[i];
                _opening[kept] = _opening[i];
                _until[kept] = _until[i];
                kept++;
            }
            _count = kept;
            if (_count == _commander.Length)
            {
                int size = _count * 2;
                Array.Resize(ref _commander, size);
                Array.Resize(ref _opening, size);
                Array.Resize(ref _until, size);
            }
            _commander[_count] = commanderIndex;
            _opening[_count] = openingId;
            _until[_count] = until;
            _count++;
        }

        public bool IsHesitating(int commanderIndex, int openingId, double nowHours)
        {
            for (int i = 0; i < _count; i++)
                if (_commander[i] == commanderIndex && _opening[i] == openingId && nowHours < _until[i]) return true;
            return false;
        }

        public void Clear() => _count = 0;
    }

    /// <summary>
    /// The subtrees committed this tick (requirement-commitment-ledger): the asset of every live exploit entry, then each
    /// launch decided in the same tick, so a later decision (a sibling division, a corps after its divisions) sees every
    /// earlier commitment. Rebuilt at the start of each opportunity tick. Per side, pooled.
    /// </summary>
    public sealed class CommitmentLedger
    {
        private int[] _roots = new int[8];
        private int _count;

        public void BeginTick() => _count = 0;

        public void Add(int ownIndex)
        {
            if (Contains(ownIndex)) return;
            if (_count == _roots.Length) Array.Resize(ref _roots, _count * 2);
            _roots[_count++] = ownIndex;
        }

        public ReadOnlySpan<int> Roots => new ReadOnlySpan<int>(_roots, 0, _count);

        public bool Contains(int ownIndex)
        {
            for (int i = 0; i < _count; i++) if (_roots[i] == ownIndex) return true;
            return false;
        }
    }

    /// <summary>
    /// What the producer knows about one candidate asset beyond its record: facts about its subtree and the executor
    /// state. Each flag is one eligibility rule of requirement-asset-eligibility.
    /// </summary>
    public struct AssetContext
    {
        /// <summary>In the posture producer's AI pool, or a leaf brigade with a leaf task.</summary>
        public bool DrivenByPosture;
        public bool DescendantPlayerOwned, DescendantFeud;
        /// <summary>PostureSense.ActiveMove / PendingOrder, and an executor Move within OrderExecutor.RecentOrderBattleSeconds.</summary>
        public bool ActiveMove, PendingOrder, RecentExecutorMove;
        public bool UnderManeuver, UnderRecon, UnderProbeHold, UnderArrivalHold;
        /// <summary>A live exploit on the asset, one of its ancestors or one of its descendants.</summary>
        public bool ExploitInSubtreeOrAncestor;
        public bool SafetyClaim, InCooldown;
        public bool Cavalry;
        /// <summary>The mean loss fraction of the asset's infantry and cavalry regiments (a group record carries none).</summary>
        public float LossFraction;
        /// <summary>min(1, moraletriggerforfallbackifenemyclose[3] x 1.2): vanilla's cycle-7 gate would never charge below it.</summary>
        public float CavalryMoraleBar;
    }

    public struct DecisionInput
    {
        public int NodeId, CommanderIndex, CommanderStatus, ChildCount, UncommittedChildren;
        public ExploitAsset[] Assets;
        public int AssetCount;
        public Opening[] Openings;
        public int OpeningCount;
        public BattleStance Stance;
        public BattlePhase PlanPhase;
        public int MacroAi, MainEffortSector, SideLiveExploits;
        public bool NodeHasExploit, CounterstrokeOn;
        public int BattleKey, DecisionNumber;
        public double NowHours;
        public IOpeningTerrain Terrain;
        public BattleSnapshot Snapshot;
        public IHesitationLookup Hesitation;
        /// <summary>This tick's commitments (the live exploits' assets and the launches already decided).</summary>
        public CommitmentLedger Ledger;
    }

    public struct DecisionResult
    {
        public bool Launch;
        /// <summary>A static literal: "launch", "roll-miss", the failing threshold or risk check, or "no-pair".</summary>
        public string Reason;
        public int AssetId, OpeningId;
        public float Score, Chance, Roll, Odds, RequiredOdds, Confidence, ConfidenceMin;
    }

    public static class OpportunityDoctrine
    {
        public const float AssetMoraleMin = 0.6f, AssetLossMax = 0.3f, InfantryReachMeters = 600f, CavalryReachMeters = 1200f,
            OddsRadiusMeters = OpeningDetector.LocalEnemyValueRadiusMeters, BatteryInfantryMeters = 400f,
            CavalryFlankAudacityMin = 0.6f;
        public const double DecisionIntervalHours = 0.05, OpeningMaxAgeHours = 0.5, HesitationHours = 0.1, AssetCooldownHours = 0.25;
        /// <summary>A gap must be at least this share of the asset's width, so the asset fits through (double: 0.6 x 200 is exactly 120).</summary>
        public const double GapFitShare = 0.6;
        public const int MaxConcurrentExploits = 3, MaxPairsPerDecision = 4;

        public const string Launch = "launch", RollMiss = "roll-miss", NoPair = "no-pair";
        public const string ReasonOdds = "odds", ReasonConfidence = "confidence", ReasonAge = "age", ReasonReserveFloor = "reserve-floor",
            ReasonLineIntegrity = "line-integrity", ReasonMainEffort = "main-effort", ReasonRecoveryPath = "recovery-path",
            ReasonStance = "stance", ReasonPhase = "phase", ReasonConcurrency = "concurrency";

        // ---- the formulas of § 6 -------------------------------------------------------------------------------

        /// <summary>requirement-asset-choice: the share of an opening kind each asset type is good for (§ 3, history as weights).</summary>
        public static float KindWeight(OpeningKind kind, bool cavalry, float audacity01, bool clusterShaken)
        {
            switch (kind)
            {
                case OpeningKind.Gap: return cavalry ? 0.3f : 1.0f;
                case OpeningKind.ShakenSector: return cavalry ? 1.0f : 0.8f;
                case OpeningKind.FlankInAir: return !cavalry ? 1.0f : audacity01 >= CavalryFlankAudacityMin ? 0.5f : 0f;
                case OpeningKind.UnsupportedBattery: return cavalry ? 1.0f : 0.6f;
                case OpeningKind.IsolatedCluster: return !cavalry ? 1.0f : clusterShaken ? 0.6f : 0f;
                case OpeningKind.Penetration: return cavalry ? 0f : 1.0f;
                default: return 0f;
            }
        }

        /// <summary>requirement-exploit-reach: infantry 600 m, cavalry 1200 m, x (0.75 + 0.5 x audacity01).</summary>
        public static float Reach(bool cavalry, float audacity01)
            => (cavalry ? CavalryReachMeters : InfantryReachMeters) * (0.75f + 0.5f * Clamp01(audacity01));

        /// <summary>requirement-local-odds: baseOdds(kind) x (1.2 - 0.4 x agg01).</summary>
        public static float RequiredOdds(OpeningKind kind, float agg01) => BaseOdds(kind) * (1.2f - 0.4f * Clamp01(agg01));

        private static float BaseOdds(OpeningKind kind)
        {
            switch (kind)
            {
                case OpeningKind.Gap: return 1.0f;
                case OpeningKind.ShakenSector: return 0.8f;
                case OpeningKind.FlankInAir: return 0.8f;
                case OpeningKind.UnsupportedBattery: return 0.5f;
                case OpeningKind.IsolatedCluster: return 1f / OpeningDetector.ClusterValueShare;   // vanilla's own ratio
                case OpeningKind.Penetration: return 0.8f;
                default: return 1f;
            }
        }

        /// <summary>requirement-exploit-thresholds: 0.5 + 0.3 x (caution01 - 0.5), 0.35 to 0.65.</summary>
        public static float ConfidenceMin(float caution01) => 0.5f + 0.3f * (Clamp01(caution01) - 0.5f);

        /// <summary>requirement-reserve-floor: clamp(0.30 + 0.2 x (caution01 - 0.5) - 0.1 x (casualtyTolerance01 - 0.5), 0.15, 0.45).</summary>
        public static float FloorShare(float caution01, float casualtyTolerance01)
        {
            float share = 0.30f + 0.2f * (caution01 - 0.5f) - 0.1f * (casualtyTolerance01 - 0.5f);
            return share < 0.15f ? 0.15f : share > 0.45f ? 0.45f : share;
        }

        /// <summary>The children that must stay uncommitted: ceil(n x share) for n >= 3, none for fewer (line-integrity carries the risk).</summary>
        public static int FloorCount(int children, float floorShare)
        {
            if (children <= 2) return 0;
            return (int)Math.Ceiling(children * (double)floorShare - 1e-4);
        }

        /// <summary>requirement-exploit-chance: clamp(base(kind) x (0.5 + agg01) x (0.75 + 0.5 x audacity01) x duty, 0, 0.95).</summary>
        public static float Chance(OpeningKind kind, in OpportunityPersonality p)
        {
            float chance = BaseChance(kind) * (0.5f + p.Agg01) * (0.75f + 0.5f * p.Audacity01) * p.Duty;
            return chance < 0f ? 0f : chance > 0.95f ? 0.95f : chance;
        }

        private static float BaseChance(OpeningKind kind)
        {
            switch (kind)
            {
                case OpeningKind.Gap: return 0.6f;
                case OpeningKind.ShakenSector: return 0.6f;
                case OpeningKind.UnsupportedBattery: return 0.5f;
                case OpeningKind.IsolatedCluster: return 0.5f;
                case OpeningKind.FlankInAir: return 0.4f;
                case OpeningKind.Penetration: return 0.6f;
                default: return 0f;
            }
        }

        /// <summary>The cavalry asset's group-morale bar: vanilla's cycle-7 gate (5047) tests morale above min(1, trigger x 1.2).</summary>
        public static float CavalryMoraleBar(float moraleTriggerForFallbackIfEnemyClose) => Math.Min(1f, moraleTriggerForFallbackIfEnemyClose * 1.2f);

        // ---- the deterministic roll (D-C6) ---------------------------------------------------------------------

        /// <summary>FNV-1a over the four ints (battle key, commander index, opening id, decision number), mapped to [0, 1).</summary>
        public static float Roll(int battleKey, int commanderIndex, int openingId, int decisionNumber)
        {
            uint h = 2166136261u;
            h = Mix(h, battleKey);
            h = Mix(h, commanderIndex);
            h = Mix(h, openingId);
            h = Mix(h, decisionNumber);
            return Scale(h);
        }

        // The top 24 bits are exact in a float, so Scale(0xFFFFFFFF) is 0.99999994, never 1.
        internal static float Scale(uint h) => (h >> 8) * (1f / 16777216f);

        private static uint Mix(uint h, int value)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= (uint)((value >> (8 * i)) & 0xFF);
                h *= 16777619u;
            }
            return h;
        }

        // ---- scheduling and selection ------------------------------------------------------------------------

        /// <summary>
        /// requirement-deciding-commanders: a node decides when 0.05 h have passed since its last decision; its first decision
        /// is staggered by (commanderIndex mod 4) x the side-stat cycle after it was first seen.
        /// </summary>
        public static bool Due(double nowHours, double firstSeenHours, double lastDecisionHours, int commanderIndex, double sideStatCycleHours)
        {
            if (double.IsNegativeInfinity(lastDecisionHours))
                return nowHours - firstSeenHours >= (commanderIndex & 3) * sideStatCycleHours;
            return nowHours - lastDecisionHours >= DecisionIntervalHours;
        }

        /// <summary>Deepest node first (the commander on the spot sees it first), then ascending instance id. Writes indices into <paramref name="order"/>.</summary>
        public static void DecisionOrder(int[] depth, int[] id, int count, int[] order)
        {
            for (int i = 0; i < count; i++)
            {
                int at = i;
                while (at > 0 && Before(depth, id, i, order[at - 1]))
                {
                    order[at] = order[at - 1];
                    at--;
                }
                order[at] = i;
            }
        }

        private static bool Before(int[] depth, int[] id, int a, int b)
            => depth[a] != depth[b] ? depth[a] > depth[b] : id[a] < id[b];

        /// <summary>
        /// The deciding nodes of a side: its AI corps (unittyp 16 below the top) and divisions (15); when it has neither, the
        /// army node (the top unit) for its brigades. A node the player owns, or the W&amp;L player's command, never decides; an
        /// AI division above the player's command does. Writes own indices and tree depths; returns how many.
        /// </summary>
        public static int CollectDecidingNodes(BattleSnapshot s, int[] nodes, int[] depths)
        {
            int n = 0;
            int army = -1;
            for (int i = 0; i < s.OwnCount && n < nodes.Length; i++)
            {
                ref UnitRecord r = ref s.Own[i];
                if (!IsDecidingCandidate(in r)) continue;
                if (r.ParentIndex < 0)
                {
                    if (r.UnitTyp >= TacticalUnitType.BattleGroupBrigade) army = i;
                    continue;
                }
                if (r.UnitTyp != TacticalUnitType.BattleGroupDivision && r.UnitTyp != TacticalUnitType.BattleGroupArmy) continue;
                nodes[n] = i;
                depths[n] = DepthOf(s, i);
                n++;
            }
            if (n == 0 && army >= 0)
            {
                nodes[0] = army;
                depths[0] = 0;
                n = 1;
            }
            return n;
        }

        private static bool IsDecidingCandidate(in UnitRecord r)
        {
            UnitFlags f = r.Flags;
            if ((f & UnitFlags.IsGroup) == 0 || (f & UnitFlags.Alive) == 0) return false;
            if ((f & (UnitFlags.PlayerOwned | UnitFlags.PlayerCommand | UnitFlags.Feud | UnitFlags.Routed)) != 0) return false;
            return r.UnitTyp >= TacticalUnitType.BattleGroupBrigade;
        }

        private static int DepthOf(BattleSnapshot s, int index)
        {
            int depth = 0;
            for (int p = s.Own[index].ParentIndex; p >= 0 && depth < s.OwnCount; p = s.Own[p].ParentIndex) depth++;
            return depth;
        }

        // ---- asset eligibility (requirement-asset-eligibility) -----------------------------------------------

        /// <summary>One eligibility rule per reason literal; the first failing rule is reported.</summary>
        public static bool Eligible(in UnitRecord group, in AssetContext ctx, out string reason)
        {
            UnitFlags f = group.Flags;
            if (!ctx.DrivenByPosture) return Fail("not-driven", out reason);
            if ((f & (UnitFlags.PlayerOwned | UnitFlags.AncestorOfPlayerCommand | UnitFlags.PlayerCommand)) != 0 || ctx.DescendantPlayerOwned) return Fail("player", out reason);
            if ((f & UnitFlags.Feud) != 0 || ctx.DescendantFeud) return Fail("feud", out reason);
            if ((f & (UnitFlags.Routed | UnitFlags.MarkedForRout)) != 0) return Fail("routed", out reason);
            if ((f & UnitFlags.InMelee) != 0) return Fail("in-melee", out reason);
            if (group.GroupMorale < AssetMoraleMin) return Fail("morale", out reason);
            if (ctx.LossFraction > AssetLossMax) return Fail("losses", out reason);
            if (ctx.ActiveMove) return Fail("moving", out reason);
            if (ctx.PendingOrder) return Fail("pending-order", out reason);
            if (ctx.RecentExecutorMove) return Fail("recent-order", out reason);
            if (ctx.UnderManeuver) return Fail("maneuver", out reason);
            if (ctx.UnderRecon) return Fail("recon", out reason);
            if (ctx.UnderProbeHold) return Fail("probe-hold", out reason);
            if (ctx.UnderArrivalHold) return Fail("arrival-hold", out reason);
            if (ctx.ExploitInSubtreeOrAncestor) return Fail("exploit", out reason);
            if (ctx.SafetyClaim) return Fail("safety", out reason);
            if (ctx.InCooldown) return Fail("cooldown", out reason);
            if (ctx.Cavalry && !(group.GroupMorale > ctx.CavalryMoraleBar)) return Fail("cavalry-morale", out reason);
            reason = null;
            return true;
        }

        private static bool Fail(string literal, out string reason)
        {
            reason = literal;
            return false;
        }

        // ---- the decision -------------------------------------------------------------------------------------

        private static readonly float[] PairScore = new float[MaxPairsPerDecision];
        private static readonly int[] PairOpening = new int[MaxPairsPerDecision], PairAsset = new int[MaxPairsPerDecision];
        private static readonly float[] PairOdds = new float[MaxPairsPerDecision];
        private static readonly int[] PairOpeningId = new int[MaxPairsPerDecision], PairAssetId = new int[MaxPairsPerDecision];
        private static int[] _roots = new int[16];

        /// <summary>
        /// requirement-decision-contract. Forms and scores the (opening, asset) pairs, keeps the best
        /// <see cref="MaxPairsPerDecision"/>, and takes the first that passes the odds, confidence and age thresholds and the
        /// seven risk checks in § 6 order to the roll: launch iff roll &lt; chance, else "roll-miss". With no passing pair the
        /// result carries the last failing reason, or "no-pair" when no pair was formed. Pure and allocation-free.
        /// </summary>
        public static DecisionResult Decide(in DecisionInput input, in OpportunityPersonality p)
        {
            var result = new DecisionResult { Reason = NoPair, AssetId = -1, OpeningId = -1 };
            int pairs = FormPairs(in input, in p);
            for (int k = 0; k < pairs; k++)
            {
                ref Opening o = ref input.Openings[PairOpening[k]];
                ref ExploitAsset a = ref input.Assets[PairAsset[k]];
                float odds = PairOdds[k];
                float required = RequiredOdds(o.Kind, p.Agg01);
                float confidenceMin = ConfidenceMin(p.Caution01);
                result.AssetId = a.Id;
                result.OpeningId = o.Id;
                result.Score = PairScore[k];
                result.Odds = Math.Min(odds, EnemyVisibleSector.MaxKnowableOdds);
                result.RequiredOdds = required;
                result.Confidence = o.Confidence01;
                result.ConfidenceMin = confidenceMin;

                string failure = CheckThresholds(in input, in o, odds, required, confidenceMin) ?? CheckRisks(in input, in o, in a, in p);
                if (failure != null)
                {
                    result.Reason = failure;
                    continue;
                }

                float chance = Chance(o.Kind, in p);
                float roll = Roll(input.BattleKey, input.CommanderIndex, o.Id, input.DecisionNumber);
                result.Chance = chance;
                result.Roll = roll;
                result.Launch = roll < chance;
                result.Reason = result.Launch ? Launch : RollMiss;
                return result;
            }
            return result;
        }

        // Pairs a confirmed, not-hesitated-over opening with every asset that can do something about it, scored kindWeight x
        // min(localOdds, cap) x confidence; the best MaxPairsPerDecision, best first.
        private static int FormPairs(in DecisionInput input, in OpportunityPersonality p)
        {
            int pairs = 0;
            for (int oi = 0; oi < input.OpeningCount; oi++)
            {
                ref Opening o = ref input.Openings[oi];
                if (!o.Confirmed) continue;
                if (input.Hesitation != null && input.Hesitation.IsHesitating(input.CommanderIndex, o.Id, input.NowHours)) continue;
                for (int ai = 0; ai < input.AssetCount; ai++)
                {
                    ref ExploitAsset a = ref input.Assets[ai];
                    float weight = KindWeight(o.Kind, a.Cavalry, p.Audacity01, o.Shaken);
                    if (!(weight > 0f)) continue;
                    float dx = a.X - o.AimX, dz = a.Z - o.AimZ;
                    float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (distance > Reach(a.Cavalry, p.Audacity01)) continue;
                    if (o.Kind == OpeningKind.UnsupportedBattery && !a.Cavalry && distance > BatteryInfantryMeters) continue;
                    if (o.Kind == OpeningKind.Gap && o.Width < GapFitShare * a.GroupWidth) continue;
                    if (o.Kind == OpeningKind.Penetration && a.HasFrontage) continue;
                    if (o.Kind == OpeningKind.IsolatedCluster
                        && !OpeningDetector.ClusterQualifies(o.ClusterValue, o.ClusterNearestMeters, a.Strength * a.GroupMorale, distance)) continue;

                    float odds = o.LocalEnemyValue > 0f ? a.Strength * a.GroupMorale / o.LocalEnemyValue : float.PositiveInfinity;
                    float score = weight * Math.Min(odds, EnemyVisibleSector.MaxKnowableOdds) * o.Confidence01;
                    pairs = InsertPair(pairs, score, oi, ai, odds, o.Id, a.Id);
                }
            }
            return pairs;
        }

        // Keeps the best MaxPairsPerDecision by score; a tie goes to the lower opening id, then the lower asset id.
        private static int InsertPair(int pairs, float score, int opening, int asset, float odds, int openingId, int assetId)
        {
            int at = pairs < MaxPairsPerDecision ? pairs : MaxPairsPerDecision - 1;
            if (pairs == MaxPairsPerDecision && !BetterThan(score, openingId, assetId, PairScore[at], PairOpeningId[at], PairAssetId[at])) return pairs;
            while (at > 0 && BetterThan(score, openingId, assetId, PairScore[at - 1], PairOpeningId[at - 1], PairAssetId[at - 1]))
            {
                PairScore[at] = PairScore[at - 1];
                PairOpening[at] = PairOpening[at - 1];
                PairAsset[at] = PairAsset[at - 1];
                PairOdds[at] = PairOdds[at - 1];
                PairOpeningId[at] = PairOpeningId[at - 1];
                PairAssetId[at] = PairAssetId[at - 1];
                at--;
            }
            PairScore[at] = score;
            PairOpening[at] = opening;
            PairAsset[at] = asset;
            PairOdds[at] = odds;
            PairOpeningId[at] = openingId;
            PairAssetId[at] = assetId;
            return pairs < MaxPairsPerDecision ? pairs + 1 : pairs;
        }

        private static bool BetterThan(float score, int openingId, int assetId, float otherScore, int otherOpeningId, int otherAssetId)
        {
            if (score != otherScore) return score > otherScore;
            if (openingId != otherOpeningId) return openingId < otherOpeningId;
            return assetId < otherAssetId;
        }

        private static string CheckThresholds(in DecisionInput input, in Opening o, float odds, float required, float confidenceMin)
        {
            if (odds < required) return ReasonOdds;
            if (o.Confidence01 < confidenceMin) return ReasonConfidence;
            if (input.NowHours - o.FirstSeenHours > OpeningMaxAgeHours) return ReasonAge;
            return null;
        }

        // The seven risk checks of requirement-risk-check, in order.
        private static string CheckRisks(in DecisionInput input, in Opening o, in ExploitAsset a, in OpportunityPersonality p)
        {
            // 1. reserve floor
            int floor = FloorCount(input.ChildCount, FloorShare(p.Caution01, p.CasualtyTolerance01));
            if (floor > 0 && input.UncommittedChildren - (a.Uncommitted ? 1 : 0) < floor) return ReasonReserveFloor;

            // 2. line integrity: without every committed subtree and the asset's own, the own line must show no gap at the asset
            if ((a.HasFrontage || a.InContact) && input.Snapshot != null && OwnGapWithout(in input, in a) > 0f) return ReasonLineIntegrity;

            // 3. main effort, unless the opening lies in the main-effort sector
            if (a.InMainEffort && o.Sector != input.MainEffortSector) return ReasonMainEffort;

            // 4. recovery path: the asset can come back the way it went
            if (input.Terrain == null || input.Terrain.SegmentCrossesRiver(a.X, a.Z, o.AimX, o.AimZ)) return ReasonRecoveryPath;

            // 5. stance: a Defender launches only a Penetration counterstroke, and only with the flag on
            if (input.Stance == BattleStance.Defender && !(o.Kind == OpeningKind.Penetration && input.CounterstrokeOn)) return ReasonStance;

            // 6. phase
            if (input.PlanPhase == BattlePhase.Withdraw || input.MacroAi == 3) return ReasonPhase;

            // 7. concurrency
            if (input.SideLiveExploits >= MaxConcurrentExploits || input.NodeHasExploit || o.Claimed) return ReasonConcurrency;

            return null;
        }

        private static float OwnGapWithout(in DecisionInput input, in ExploitAsset a)
        {
            ReadOnlySpan<int> committed = input.Ledger != null ? input.Ledger.Roots : ReadOnlySpan<int>.Empty;
            if (_roots.Length < committed.Length + 1) _roots = new int[Math.Max(committed.Length + 1, _roots.Length * 2)];
            int n = 0;
            for (int i = 0; i < committed.Length; i++) _roots[n++] = committed[i];
            if (input.Ledger == null || !input.Ledger.Contains(a.OwnIndex)) _roots[n++] = a.OwnIndex;
            return OpeningDetector.OwnGapAt(input.Snapshot, new ReadOnlySpan<int>(_roots, 0, n), a.Lateral);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
