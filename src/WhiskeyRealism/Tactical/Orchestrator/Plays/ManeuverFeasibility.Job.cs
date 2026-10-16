using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>What the evaluation reads of one direct child, taken once per evaluation (requirement-hypothetical-layout).</summary>
    internal struct ChildFact
    {
        public string Id;
        public int OwnIndex, InstanceId;
        public float X, Z;
        /// <summary>The subtree's strength without its player-owned regiments (requirement-player-share), halved while an exploit asset is away from its slot.</summary>
        public float Strength;
        /// <summary>The strength-weighted mean morale of the non-player regiments.</summary>
        public float Morale;
        public float MeanFatigue, BaseSpeed;
        /// <summary>The child is offered to the hypothetical layout.</summary>
        public bool Offered;
        public bool InContact, DeployedReserve;
        /// <summary>BC11 has the child past its Hold: it is marching, turning or attacking, and its order delay was paid when it was released.</summary>
        public bool Moving;
        /// <summary>The child holds player-owned strength: it is never released to a crossing and its time to one is not offered.</summary>
        public bool HasPlayerOwned;
        /// <summary>The child is bound to guard a crossing (requirement-defender-crossing-release): its regiments are left out of the crossing cover test.</summary>
        public bool BoundGuard;
        /// <summary>The hours an order from the army HQ takes to reach the child and everything below it, whether or not it is engaged.</summary>
        public float CourierHours;
    }

    /// <summary>One candidate side's working set: its result, bodies and members (pooled, reused by every candidate).</summary>
    internal sealed class CandidateScratch
    {
        public CandidateResult Result;
        public readonly BodyEstimate[] Bodies = new BodyEstimate[ManeuverFeasibility.MaxBodies];
        public readonly MemberEstimate[] Members = new MemberEstimate[FeasibilityLedger.MaxMembersPerCandidate];
        public int BodyCount, MemberCount;
        /// <summary>A Defender candidate's turning exposure (requirement-defender-exposure), the worst crossing's.</summary>
        public float Turn;
        /// <summary>The crossings on every member's march and inward legs (<see cref="MemberEstimate.CrossingStart"/> indexes it).</summary>
        public readonly RouteCrossing[] Crossings = new RouteCrossing[FeasibilityLedger.MaxCrossingsPerCandidate];
        public int CrossingCount;
        /// <summary>The route screen's outcomes for the verdict (requirement-route-screen).</summary>
        public bool NoCrossing, RiverOnLeg, NoRoute, WingUnfilled, RouteUnknown;

        public void Reset(BattlePlanId plan, bool mirrored)
        {
            Result = new CandidateResult { Plan = plan, Mirrored = mirrored };
            BodyCount = 0;
            MemberCount = 0;
            Turn = 0f;
            CrossingCount = 0;
            NoCrossing = false;
            RiverOnLeg = false;
            NoRoute = false;
            WingUnfilled = false;
            RouteUnknown = false;
        }
    }

    /// <summary>
    /// The resumable feasibility evaluation of one side (spec § 8): <see cref="Begin"/> takes what every candidate shares (the own
    /// frame, the children and the crossing graph's configuration), then each <see cref="Step"/> evaluates whole candidates until its
    /// deadline. Results go to the ledger's back buffer and publish whole when the last candidate is done.
    /// </summary>
    public sealed class FeasibilityJob
    {
        public bool Running { get; private set; }

        public int NextCandidate { get; private set; }

        internal FeasibilityInput Input;
        /// <summary>The ledger the running evaluation publishes into (the selected candidate's crossing threats go to its back buffer).</summary>
        internal FeasibilityLedger Target;
        /// <summary>A Defender's enemy bodies and counted crossings, prepared once per evaluation (requirement-defender-exposure).</summary>
        internal readonly DefenderPicture Defender = new DefenderPicture();
        private bool[] _ownExcluded = new bool[256];
        internal readonly CrossingGraphView Graph = new CrossingGraphView();
        internal readonly EnemyPicture Enemy = new EnemyPicture();
        private readonly LegCostCache _ownCache = new LegCostCache();

        internal readonly ChildFact[] Children = new ChildFact[ManeuverFeasibility.MaxChildren];
        internal int ChildCount, OfferedCount;
        internal readonly List<CorpsPosition> Offered = new List<CorpsPosition>(ManeuverFeasibility.MaxChildren);

        // The own frame the executor's ComputeZoneAssignments lays a play out in.
        internal float ArmyX, ArmyZ, EnemyX, EnemyZ, Frontage;
        /// <summary>The perceived enemy strength on each half of its extent (a DeepRaid's outflank side).</summary>
        internal float PressureLeft, PressureRight;
        internal bool FrameValid;
        private readonly List<float> _xs = new List<float>(256);
        private readonly List<float> _zs = new List<float>(256);

        internal readonly BattlePlanId[] Plans = new BattlePlanId[FeasibilityLedger.MaxCandidates];
        internal int PlanCount;

        /// <summary>How many sides each candidate of the last evaluation was evaluated on (CANDIDATE-CENTRED), by candidate index.</summary>
        internal readonly int[] SidesEvaluated = new int[FeasibilityLedger.MaxCandidates];

        /// <summary>The most path searches one candidate side needed in this evaluation (requirement-frame-budget's counted bound).</summary>
        internal int MaxSearchesPerSide;

        internal readonly CandidateScratch SideA = new CandidateScratch();
        internal readonly CandidateScratch SideB = new CandidateScratch();

        // One exact-length rect array per plan: AssignInto takes the rects as an IReadOnlyList, and an array of the template's length is one.
        private readonly WorldPlayRect[][] _rects = new WorldPlayRect[ManeuverFeasibility.PlanSlots][];
        internal readonly CorpsAssignment[] Assignments = new CorpsAssignment[ManeuverFeasibility.MaxRects];
        internal static readonly ObjectivePosition[] NoObjectives = new ObjectivePosition[0];

        private int[] _slotOfOwn = new int[256];
        private float[] _strengthAll = new float[ManeuverFeasibility.MaxChildren];
        private float[] _strengthPlayer = new float[ManeuverFeasibility.MaxChildren];
        private float[] _moraleSum = new float[ManeuverFeasibility.MaxChildren];
        private float[] _fatigueSum = new float[ManeuverFeasibility.MaxChildren];
        private int[] _regimentCount = new int[ManeuverFeasibility.MaxChildren];
        private bool[] _hasInfantry = new bool[ManeuverFeasibility.MaxChildren];
        private bool[] _hasCavalry = new bool[ManeuverFeasibility.MaxChildren];
        private bool[] _hasArtillery = new bool[ManeuverFeasibility.MaxChildren];

        internal WorldPlayRect[] RectsFor(BattlePlanId plan, int rectCount)
        {
            int slot = (int)plan;
            WorldPlayRect[] array = _rects[slot];
            if (array == null || array.Length != rectCount) _rects[slot] = array = new WorldPlayRect[rectCount];
            return array;
        }

        internal int SlotOfChild(string childId)
        {
            for (int i = 0; i < ChildCount; i++)
                if (ReferenceEquals(Children[i].Id, childId)) return i;
            for (int i = 0; i < ChildCount; i++)
                if (string.Equals(Children[i].Id, childId, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Drops a running evaluation (a forced evaluation starts over).</summary>
        internal void Abort() => Running = false;

        /// <summary>Starts an evaluation: copies the input, prepares the shared facts and clears the ledger's back buffer.</summary>
        internal void Begin(in FeasibilityInput input, FeasibilityLedger ledger)
        {
            Input = input;
            Target = ledger;
            PlanCount = Math.Min(Math.Max(input.CandidateCount, 0), FeasibilityLedger.MaxCandidates);
            for (int i = 0; i < PlanCount; i++) Plans[i] = input.Candidates[i];
            Array.Clear(SidesEvaluated, 0, SidesEvaluated.Length);
            MaxSearchesPerSide = 0;
            NextCandidate = 0;

            LegCostCache cache = input.Cache ?? _ownCache;
            cache.Clear();
            Graph.Grid = input.Grid;
            Graph.Regions = input.Regions;
            Graph.Crossings = input.Crossings;
            Graph.Cache = cache;
            Graph.Legs = input.Legs;
            Graph.Rules = input.Rules;
            Graph.OwnSnapshot = input.Snapshot;
            Graph.ResetOwnCovered();
            Graph.LegsSampled = 0;
            Graph.Searches = 0;

            PrepareFrame();
            Enemy.Build(Input.Snapshot, Input.EnemyEstimate, Input.Confidence, Input.Army.Caution01, Input.HasAssumedEnemy, Input.AssumedEnemyX,
                Input.AssumedEnemyZ, EnemyX, EnemyZ);
            PrepareChildren();
            ManeuverFeasibility.PrepareDefender(this);
            ledger.BeginBack();
            Running = true;
        }

        /// <summary>
        /// Evaluates candidates until none is left or the deadline passes between two of them (at least one per call, so a tight
        /// budget still progresses). Publishes the ledger and returns true when the last one is done.
        /// </summary>
        internal bool Step(FeasibilityLedger ledger, long deadlineTicks, Func<long> clock)
        {
            if (!Running) return false;
            int doneThisCall = 0;
            while (NextCandidate < PlanCount)
            {
                if (doneThisCall > 0 && clock != null && clock() >= deadlineTicks) return false;
                ManeuverFeasibility.EvaluateCandidate(this, NextCandidate, ledger);
                NextCandidate++;
                doneThisCall++;
            }
            ledger.Publish(Input.NowHours);
            Running = false;
            return true;
        }

        // The projection frame of PlayExecutorRuntime.ComputeZoneAssignments: the centroid and the frontage of every active own unit, unweighted,
        // and the enemy anchor of the Visible observations (PlayAnchor), or the axis heading when the side sees none.
        private void PrepareFrame()
        {
            FrameValid = false;
            BattleSnapshot s = Input.Snapshot;
            if (s == null) return;
            _xs.Clear();
            _zs.Clear();
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((own[i].Flags & UnitFlags.Active) == 0) continue;
                float x = own[i].X, z = own[i].Z;
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z)) continue;
                _xs.Add(x);
                _zs.Add(z);
            }
            PlayProjection.ComputeCentroid(_xs, _zs, out ArmyX, out ArmyZ, out int n);
            if (n == 0) return;
            PlayAnchor.EnemyAnchor(s, Input.Axis, ArmyX, ArmyZ, out EnemyX, out EnemyZ);
            float dx = EnemyX - ArmyX, dz = EnemyZ - ArmyZ;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            float forwardX = dist > 0.001f ? dx / dist : 0f;
            float forwardZ = dist > 0.001f ? dz / dist : 1f;
            Frontage = PlayProjection.EstimateFrontage(_xs, _zs, forwardX, forwardZ);
            EnvelopmentManeuver.EnemyPressureSplit(s, Input.Axis, out PressureLeft, out PressureRight);
            FrameValid = true;
        }

        private void PrepareChildren()
        {
            ChildCount = 0;
            OfferedCount = 0;
            Offered.Clear();
            BattleSnapshot s = Input.Snapshot;
            if (s == null || Input.ChildIds == null) return;
            int n = Math.Min(Math.Max(Input.ChildCount, 0), ManeuverFeasibility.MaxChildren);
            if (_slotOfOwn.Length < s.OwnCount) _slotOfOwn = new int[Math.Max(s.OwnCount, _slotOfOwn.Length * 2)];
            for (int i = 0; i < s.OwnCount; i++) _slotOfOwn[i] = -1;
            UnitRecord[] own = s.Own;
            MarchRules rules = Input.Rules;
            for (int c = 0; c < n; c++)
            {
                int index = s.IndexOfOwn(DirectChildIds.InstanceId(Input.ChildIds[c]));
                var fact = new ChildFact { Id = Input.ChildIds[c], OwnIndex = index };
                _strengthAll[c] = 0f;
                _strengthPlayer[c] = 0f;
                _moraleSum[c] = 0f;
                _fatigueSum[c] = 0f;
                _regimentCount[c] = 0;
                _hasInfantry[c] = false;
                _hasCavalry[c] = false;
                _hasArtillery[c] = false;
                if (index >= 0)
                {
                    _slotOfOwn[index] = c;
                    fact.InstanceId = own[index].InstanceId;
                    fact.X = own[index].X;
                    fact.Z = own[index].Z;
                }
                fact.DeployedReserve = Input.DeployedReserve != null && c < Input.DeployedReserve.Length && Input.DeployedReserve[c];
                fact.Moving = Input.ChildMoving != null && c < Input.ChildMoving.Length && Input.ChildMoving[c];
                fact.BoundGuard = IsBoundGuard(Input.ChildIds[c]);
                Children[c] = fact;
            }
            ChildCount = n;
            if (_ownExcluded.Length < s.OwnCount) _ownExcluded = new bool[Math.Max(s.OwnCount, _ownExcluded.Length * 2)];
            Array.Clear(_ownExcluded, 0, _ownExcluded.Length);
            Graph.OwnExcluded = _ownExcluded;

            // One pass over the regiments: each climbs the parent map to the direct child it belongs to.
            const UnitFlags live = UnitFlags.Alive | UnitFlags.Active;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((own[i].Flags & live) != live || (own[i].Flags & (UnitFlags.IsGroup | UnitFlags.Routed)) != 0) continue;
                int cur = i;
                int slot = -1;
                for (int steps = 0; cur >= 0 && cur < s.OwnCount && steps < s.OwnCount; steps++)
                {
                    if (_slotOfOwn[cur] >= 0) { slot = _slotOfOwn[cur]; break; }
                    cur = own[cur].ParentIndex;
                }
                if (slot < 0) continue;
                if (Children[slot].BoundGuard) _ownExcluded[i] = true;
                float strength = own[i].Strength;
                _strengthAll[slot] += strength;
                _fatigueSum[slot] += own[i].Fatigue;
                _regimentCount[slot]++;
                if ((own[i].Flags & UnitFlags.IsCavalry) != 0) _hasCavalry[slot] = true;
                else if ((own[i].Flags & UnitFlags.IsArtillery) != 0) _hasArtillery[slot] = true;
                else _hasInfantry[slot] = true;
                if ((own[i].Flags & UnitFlags.PlayerOwned) != 0) _strengthPlayer[slot] += strength;
                else _moraleSum[slot] += own[i].Morale * strength;
            }

            for (int c = 0; c < ChildCount; c++)
            {
                ChildFact fact = Children[c];
                fact.HasPlayerOwned = _strengthPlayer[c] > 0f;
                float counted = _strengthAll[c] - _strengthPlayer[c];
                fact.Strength = counted;
                fact.Morale = counted > 0f ? _moraleSum[c] / counted : 0f;
                fact.MeanFatigue = _regimentCount[c] > 0 ? _fatigueSum[c] / _regimentCount[c] : 0f;
                fact.BaseSpeed = BaseSpeedOf(c, in rules);
                if (fact.OwnIndex >= 0)
                {
                    fact.CourierHours = ManeuverFeasibility.ChildCourierHours(s, fact.OwnIndex, Input.Grid, in rules);
                    fact.InContact = s.ClosestVisibleEnemy(fact.OwnIndex, out float contactDistance) >= 0 && contactDistance <= EnvelopmentManeuver.ContactMeters;
                    // An exploit asset below the child is away from its slot: the child stays in the layout at half strength (requirement-bc13-commitment).
                    if (Input.Exploits != null && Input.Exploits.HasExploitingDescendant(fact.InstanceId)) fact.Strength *= 0.5f;
                }
                bool exploiting = Input.Exploits != null && fact.InstanceId != 0 && Input.Exploits.IsExploiting(fact.InstanceId);
                fact.Offered = fact.OwnIndex >= 0 && !fact.DeployedReserve && !exploiting && counted > 0f;
                Children[c] = fact;
                if (!fact.Offered) continue;
                OfferedCount++;
                Offered.Add(new CorpsPosition(fact.Id, fact.X, fact.Z));
            }
        }

        private bool IsBoundGuard(string childId)
        {
            string[] bound = Input.BoundGuardChildIds;
            for (int i = 0; bound != null && i < Input.BoundGuardCount && i < bound.Length; i++)
                if (string.Equals(bound[i], childId, StringComparison.Ordinal)) return true;
            return false;
        }

        // GetSlowestSpeedOfGroup (requirement-march-time): a cavalry-only child marches at the mounted base; a child with foot or guns at the
        // slowest base among them, capped at one for a mixed group.
        private float BaseSpeedOf(int slot, in MarchRules rules)
        {
            if (_hasCavalry[slot] && !_hasInfantry[slot] && !_hasArtillery[slot]) return rules.CavalryMountedBase;
            float slowest = float.MaxValue;
            if (_hasInfantry[slot]) slowest = Math.Min(slowest, rules.InfantryBase);
            if (_hasArtillery[slot]) slowest = Math.Min(slowest, rules.ArtilleryLimberedBase);
            if (_hasCavalry[slot]) slowest = Math.Min(slowest, rules.CavalryMountedBase);
            if (slowest == float.MaxValue) slowest = rules.InfantryBase;
            return Math.Min(slowest, 1f);
        }
    }
}
