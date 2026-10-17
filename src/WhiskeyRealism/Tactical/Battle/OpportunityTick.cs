using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 (spec docs/superpowers/specs/2026-10-15-battle-opportunity-design.md § 6, requirement-opportunity-producer): the
    // opportunity tick of one AI side, pure. The runtime producer (OpportunityIntentProducer) is a thin shell that supplies the
    // world (the army plan, the ledger roles, the arbiter and executor state), the perceived-ground oracle and the telemetry
    // sink; everything that decides lives here so the harness runs it on a snapshot fixture.
    //
    // Per tick, in this order:
    //   1. a new battle day ends every live exploit (stand-down) and forgets the decision state;
    //   2. aggregate the roster (descendant flags, strength and loss per group);
    //   3. detect the side's openings;
    //   4. step every live exploit (its subtree rebound, the step input read from the snapshot and the world);
    //   5. collect the ended ones: hesitation after an abort, the claim released, the asset's cooldown started;
    //   6. rebuild the commitment ledger from the survivors and derive the opening claims;
    //   7. let each due deciding commander decide, deepest first; a launch is created, bound and committed at once so a
    //      later decider in the same tick sees it.
    // It submits no intents: the posture producer, next in the tick, executes the table (requirement-exploit-override).
    public interface IOpportunityWorld
    {
        BattleStance Stance { get; }
        BattlePhase PlanPhase { get; }
        int MacroAi { get; }
        int MainEffortSector { get; }
        /// <summary>The battle day; a change ends every live exploit.</summary>
        int BattleDay { get; }
        /// <summary>One vanilla side-stat cycle in doctrine hours: the unit of the first-decision stagger.</summary>
        double SideStatCycleHours { get; }
        float CavalryMoraleBar { get; }
        /// <summary>The commander's vector (registry, derived or default) with the duty factor; false when there is none.</summary>
        bool TryPersonality(int commanderIndex, out OpportunityPersonality personality);
        /// <summary>In the posture producer's AI pool, or a leaf brigade with a leaf task.</summary>
        bool DrivenByPosture(int groupId);
        bool TryRoleTask(int groupId, out CommandNodeRole role, out CommandTaskType task);
        /// <summary>The army-level ancestor (a direct child of the army) has role Main or SupportMain.</summary>
        bool IsMainEffortChild(int armyChildGroupId);
        bool SafetyClaim(int groupId);
        bool RecentExecutorMove(int groupId, double nowHours);
        bool UnderManeuver(int groupId);
        bool UnderRecon(int groupId);
        bool UnderProbeHold(int groupId);
        bool UnderArrivalHold(int groupId);
        /// <summary>The executor gave up the asset's Move.</summary>
        bool AdvanceGaveUp(int groupId);
        /// <summary>The allocator gave the asset's parent line a crisis release.</summary>
        bool ParentCrisisRelease(int groupId);
    }

    public interface IOpportunitySink
    {
        void Launched(in ExploitEntry entry, in DecisionResult result, in OpportunityPersonality personality, int live);
        void Declined(int nodeId, int commanderIndex, in DecisionResult result, in OpportunityPersonality personality, int live);
        void PhaseChanged(in ExploitEntry entry, ExploitPhase from, int live);
        void Ended(in ExploitEntry entry, double nowHours, bool leftSector, int live);
        void OpeningSummary(OpportunityTick tick, ExploitTable table, double nowHours);
    }

    public sealed class OpportunityTick
    {
        public const double OpeningRowIntervalHours = 0.1;
        private const int MaxNodes = 32;
        private const int MaxChildren = 64;
        private const int MaxEnded = 8;
        private const float NoEnemyMeters = 1e9f;

        private sealed class NodeState
        {
            public double FirstSeen;
            public double LastDecision = double.NegativeInfinity;
            public int Decisions;
            public int LastDeclineOpening = -1;
            public string LastDeclineReason;
        }

        private readonly OpeningDetector _detector = new OpeningDetector();
        private readonly HesitationStore _hesitation = new HesitationStore();
        private readonly CommitmentLedger _ledger = new CommitmentLedger();
        private readonly Dictionary<int, NodeState> _nodes = new Dictionary<int, NodeState>();
        private readonly int[] _nodeIndex = new int[MaxNodes], _nodeDepth = new int[MaxNodes], _nodeId = new int[MaxNodes], _order = new int[MaxNodes];
        private readonly int[] _children = new int[MaxChildren];
        private readonly ExploitAsset[] _assets = new ExploitAsset[MaxChildren];
        private readonly Opening[] _openings = new Opening[OpeningDetector.MaxOpenings];
        private readonly ExploitEntry[] _ended = new ExploitEntry[MaxEnded];
        private readonly int[] _descendants = new int[ExploitTable.MaxSubtree];
        private readonly int[] _ancestors = new int[ExploitTable.MaxSubtree];

        private UnitFlags[] _descFlags = new UnitFlags[0];
        private int[] _moving = new int[0];
        private float[] _strength = new float[0], _lossSum = new float[0];
        private int[] _lossCount = new int[0];

        private int _nextExploitId = 1;
        private double _nextOpeningRow = double.NegativeInfinity;
        private int _day = int.MinValue;

        /// <summary>The side's detector: the runtime reads its openings for the TacticalOpening row.</summary>
        public OpeningDetector Detector => _detector;

        /// <summary>The detector's counters summed over the ticks since the last TacticalOpening row (the row's window).</summary>
        public int WindowProbePoints { get; private set; }
        public int WindowProbeCached { get; private set; }
        public int WindowLatticesStarted { get; private set; }
        public int WindowLatticesEnded { get; private set; }
        public int WindowDeferOverBound { get; private set; }

        /// <summary>Launches the table refused (a full table); 0 expected, the concurrency cap is far below its size.</summary>
        public int TableRefusals { get; private set; }

        public void Run(BattleSnapshot s, IOpportunityWorld w, IPerceivedGround ground, IOpeningTerrain terrain, in OpeningRules rules,
            ExploitTable table, IOpportunitySink sink)
        {
            double now = s.DoctrineHours;
            _terrain = terrain;
            if (w.BattleDay != _day)
            {
                if (_day != int.MinValue) EndAll(table, ExploitEnd.StandDown, now, sink);
                _day = w.BattleDay;
                Forget();
            }

            Aggregate(s);
            _detector.Detect(s, ground, terrain, in rules, w.Stance == BattleStance.Defender, now);
            WindowProbePoints += _detector.ProbePointsEvaluated;
            WindowProbeCached += _detector.ProbeCached;
            WindowLatticesStarted += _detector.LatticesStarted;
            WindowLatticesEnded += _detector.LatticesEnded;
            WindowDeferOverBound += _detector.DeferOverBound;

            RebuildLedger(s, table);
            StepEntries(s, w, in rules, table, sink, now);
            CollectEnded(table, sink, now);

            RebuildLedger(s, table);
            table.DeriveClaims(_detector);
            Decide(s, w, in rules, table, sink, now);

            if (now >= _nextOpeningRow)
            {
                _nextOpeningRow = now + OpeningRowIntervalHours;
                sink?.OpeningSummary(this, table, now);
                WindowProbePoints = WindowProbeCached = WindowLatticesStarted = WindowLatticesEnded = WindowDeferOverBound = 0;
            }
        }

        /// <summary>Ends every live exploit with <paramref name="reason"/> (a stand-down, a new battle day), emitting their end rows, and empties the table.</summary>
        public static void EndAll(ExploitTable table, ExploitEnd reason, double nowHours, IOpportunitySink sink)
        {
            if (sink != null)
            {
                for (int i = 0; i < table.Count; i++)
                {
                    ref ExploitEntry e = ref table.At(i);
                    if (e.Phase == ExploitPhase.None || e.Phase == ExploitPhase.Recall) continue;
                    e.End = reason;
                    e.Phase = ExploitPhase.Recall;
                    e.PlayerSkips += ExploitFeedback.TakePlayerSkips(table.Alliance, e.Id);
                    sink.Ended(in e, nowHours, false, table.Count - 1);
                }
            }
            table.Clear(reason);
        }

        /// <summary>A new battle: forget everything, ids start over.</summary>
        public void Reset()
        {
            _day = int.MinValue;
            _nextExploitId = 1;
            TableRefusals = 0;
            WindowProbePoints = WindowProbeCached = WindowLatticesStarted = WindowLatticesEnded = WindowDeferOverBound = 0;
            _detector.Reset();
            Forget();
        }

        public void Forget()
        {
            _nodes.Clear();
            _hesitation.Clear();
            _ledger.BeginTick();
            _nextOpeningRow = double.NegativeInfinity;
        }

        // ---------------------------------------------------------------------------------------- roster aggregates

        private void Aggregate(BattleSnapshot s)
        {
            int n = s.OwnCount;
            PostureSense.AggregateDescendants(s, ref _descFlags, ref _moving);
            if (_strength.Length < n)
            {
                int size = Math.Max(n, _strength.Length * 2);
                _strength = new float[size];
                _lossSum = new float[size];
                _lossCount = new int[size];
            }
            Array.Clear(_strength, 0, n);
            Array.Clear(_lossSum, 0, n);
            Array.Clear(_lossCount, 0, n);
            UnitRecord[] own = s.Own;
            for (int i = 0; i < n; i++)
            {
                UnitFlags f = own[i].Flags;
                if ((f & UnitFlags.IsGroup) != 0 || (f & UnitFlags.Alive) == 0 || (f & UnitFlags.Routed) != 0) continue;
                bool loss = (f & (UnitFlags.IsInfantry | UnitFlags.IsCavalry)) != 0;
                int p = own[i].ParentIndex;
                for (int steps = 0; p >= 0 && p < n && steps < n; steps++)
                {
                    _strength[p] += own[i].Strength;
                    if (loss)
                    {
                        _lossSum[p] += own[i].LossFraction;
                        _lossCount[p]++;
                    }
                    p = own[p].ParentIndex;
                }
            }
        }

        private float MeanLoss(int index) => _lossCount[index] > 0 ? _lossSum[index] / _lossCount[index] : 0f;

        private void RebuildLedger(BattleSnapshot s, ExploitTable table)
        {
            _ledger.BeginTick();
            for (int i = 0; i < table.Count; i++)
            {
                int ai = s.IndexOfOwn(table.At(i).AssetId);
                if (ai >= 0) _ledger.Add(ai);
            }
        }

        // ---------------------------------------------------------------------------------------- stepping

        private void StepEntries(BattleSnapshot s, IOpportunityWorld w, in OpeningRules rules, ExploitTable table, IOpportunitySink sink, double now)
        {
            for (int i = 0; i < table.Count; i++)
            {
                ref ExploitEntry e = ref table.At(i);
                if (e.Phase == ExploitPhase.None || e.Phase == ExploitPhase.Recall) continue;
                int ai = s.IndexOfOwn(e.AssetId);
                BindSubtree(s, table, e.Id, ai);
                e.PlayerSkips += ExploitFeedback.TakePlayerSkips(s.Alliance, e.Id);
                ExploitStepInput input = BuildStepInput(s, w, in rules, in e, ai, now);
                ExploitPhase before = e.Phase;
                ExploitLifecycle.Step(ref e, in input);
                if (sink != null && e.Phase != before && e.Phase != ExploitPhase.Recall) sink.PhaseChanged(in e, before, table.Count);
                // A line-crisis end caused by the own line opening a gap where the asset stood is the integrity breach (0 expected).
                if (e.Phase == ExploitPhase.Recall && e.End == ExploitEnd.LineCrisis && input.LineIntegrityFails) e.LeftSector = true;
            }
        }

        private ExploitStepInput BuildStepInput(BattleSnapshot s, IOpportunityWorld w, in OpeningRules rules, in ExploitEntry e, int ai, double now)
        {
            var input = new ExploitStepInput
            {
                NowHours = now,
                Audacity01 = 0.5f,
                CasualtyTolerance01 = 0.5f,
                ShakenMorale = rules.ShakenMorale,
                OpeningLive = OpeningLive(e.OpeningId),
                AdvanceGaveUp = e.Phase == ExploitPhase.Advance && w.AdvanceGaveUp(e.AssetId),
                PlanWithdraw = w.PlanPhase == BattlePhase.Withdraw,
                SafetyClaim = w.SafetyClaim(e.AssetId),
                ParentCrisisRelease = w.ParentCrisisRelease(e.AssetId),
                FlankEmergency = ExploitFeedback.TakeFlankEmergency(e.AssetId),
                NearestVisibleEnemyToAsset = NoEnemyMeters,
                NearestEnemyToAim = NearestEnemy(s, e.AimX, e.AimZ),
                LocalOdds = float.PositiveInfinity,
            };
            if (w.TryPersonality(e.CommanderIndex, out OpportunityPersonality p))
            {
                input.Audacity01 = p.Audacity01;
                input.CasualtyTolerance01 = p.CasualtyTolerance01;
            }

            input.AssetAlive = ai >= 0 && (s.Own[ai].Flags & UnitFlags.Alive) != 0 && !PostureSense.Routed(s.Own[ai]);
            if (ai >= 0)
            {
                ref UnitRecord asset = ref s.Own[ai];
                input.AssetX = asset.X;
                input.AssetZ = asset.Z;
                input.AssetLoss = MeanLoss(ai);
                input.AssetMorale = asset.GroupMorale;
                if (s.ClosestVisibleEnemy(ai, out float distance) >= 0) input.NearestVisibleEnemyToAsset = distance;
                input.LocalOdds = LocalOdds(s, e.AimX, e.AimZ, _strength[ai] * asset.GroupMorale);
                if (e.FromLine && s.FrontAxis.IsValid)
                    input.LineIntegrityFails = OpeningDetector.OwnGapAt(s, _ledger.Roots, e.Lateral) > 0f;
            }

            if (e.TargetObservation >= 0 && FindObservation(s, e.TargetObservation, out EnemyObservation target))
            {
                input.HasTarget = true;
                input.TargetPerceived = target.State == ObservationState.Visible || target.Confidence01 >= EnemySamples.RememberedMinConfidence;
                input.TargetX = target.X;
                input.TargetZ = target.Z;
                if (target.State == ObservationState.Visible && _detector.TryTargetRouted(target.InstanceId, out bool routed)) input.TargetRouted = routed;
                if (e.Kind == OpeningKind.Penetration &&
                    _detector.TryTraceBehind(target.X, target.Z, out float behind, out float traceX, out float traceZ))
                {
                    input.HasTrace = true;
                    input.TargetBehindTraceMeters = behind;
                    input.TraceX = traceX;
                    input.TraceZ = traceZ;
                }
            }
            return input;
        }

        private bool OpeningLive(int openingId)
        {
            ReadOnlySpan<Opening> open = _detector.Openings;
            for (int i = 0; i < open.Length; i++)
                if (open[i].Id == openingId) return true;
            return false;
        }

        private static bool FindObservation(BattleSnapshot s, int instanceId, out EnemyObservation found)
        {
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (s.Observations[k].InstanceId != instanceId) continue;
                found = s.Observations[k];
                return true;
            }
            found = default(EnemyObservation);
            return false;
        }

        // The nearest perceived enemy sample (Visible, or Remembered at the confidence floor) to a point.
        private static float NearestEnemy(BattleSnapshot s, float x, float z)
        {
            float best2 = float.MaxValue;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                ref EnemyObservation o = ref s.Observations[k];
                if (!EnemySamples.Accepts(in o)) continue;
                float dx = o.X - x, dz = o.Z - z;
                float d2 = dx * dx + dz * dz;
                if (d2 < best2) best2 = d2;
            }
            return best2 == float.MaxValue ? NoEnemyMeters : (float)Math.Sqrt(best2);
        }

        // requirement-local-odds: the asset's strength x morale over the perceived enemy within OddsRadiusMeters of the aim
        // (Visible x morale, Remembered x confidence x its last morale); +infinity with no enemy there.
        private static float LocalOdds(BattleSnapshot s, float x, float z, float assetValue)
        {
            float r2 = OpportunityDoctrine.OddsRadiusMeters * OpportunityDoctrine.OddsRadiusMeters;
            float enemy = 0f;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                ref EnemyObservation o = ref s.Observations[k];
                if (!EnemySamples.Accepts(in o)) continue;
                float dx = o.X - x, dz = o.Z - z;
                if (dx * dx + dz * dz > r2) continue;
                enemy += o.State == ObservationState.Visible ? o.Strength * o.Morale : o.Strength * o.Confidence01 * o.Morale;
            }
            return enemy > 0f ? assetValue / enemy : float.PositiveInfinity;
        }

        // The asset's group descendants (they get the follow order) and its ancestors (they count as exploiting).
        private void BindSubtree(BattleSnapshot s, ExploitTable table, int exploitId, int ai)
        {
            int d = 0, a = 0;
            if (ai >= 0)
            {
                for (int j = 0; j < s.OwnCount && d < _descendants.Length; j++)
                    if ((s.Own[j].Flags & UnitFlags.IsGroup) != 0 && s.IsAncestorOf(ai, j)) _descendants[d++] = s.Own[j].InstanceId;
                for (int p = s.Own[ai].ParentIndex, steps = 0; p >= 0 && a < _ancestors.Length && steps < s.OwnCount; p = s.Own[p].ParentIndex, steps++)
                    _ancestors[a++] = s.Own[p].InstanceId;
            }
            table.SetSubtree(exploitId, new ReadOnlySpan<int>(_descendants, 0, d), new ReadOnlySpan<int>(_ancestors, 0, a));
        }

        private void CollectEnded(ExploitTable table, IOpportunitySink sink, double now)
        {
            int collected;
            while ((collected = table.CollectEnded(_ended, now)) > 0)
            {
                for (int i = 0; i < collected; i++)
                {
                    _ended[i].PlayerSkips += ExploitFeedback.TakePlayerSkips(table.Alliance, _ended[i].Id);
                    ExploitLifecycle.MarkHesitation(_hesitation, in _ended[i], now);
                    sink?.Ended(in _ended[i], now, _ended[i].LeftSector, table.Count);
                }
                if (collected < _ended.Length) break;
            }
        }

        // ---------------------------------------------------------------------------------------- deciding

        private void Decide(BattleSnapshot s, IOpportunityWorld w, in OpeningRules rules, ExploitTable table, IOpportunitySink sink, double now)
        {
            if (!s.FrontAxis.IsValid) return;
            int nodeCount = OpportunityDoctrine.CollectDecidingNodes(s, _nodeIndex, _nodeDepth);
            if (nodeCount == 0) return;
            for (int k = 0; k < nodeCount; k++) _nodeId[k] = s.Own[_nodeIndex[k]].InstanceId;
            OpportunityDoctrine.DecisionOrder(_nodeDepth, _nodeId, nodeCount, _order);

            int openingCount = Math.Min(_detector.Count, _openings.Length);
            _detector.Openings.Slice(0, openingCount).CopyTo(new Span<Opening>(_openings, 0, openingCount));

            for (int o = 0; o < nodeCount; o++)
            {
                int k = _order[o];
                int ni = _nodeIndex[k];
                int nodeId = _nodeId[k];
                int commander = s.Own[ni].Commander;
                NodeState state = StateOf(nodeId, now);
                if (!OpportunityDoctrine.Due(now, state.FirstSeen, state.LastDecision, commander, w.SideStatCycleHours)) continue;
                if (!w.TryPersonality(commander, out OpportunityPersonality p))
                    p = new OpportunityPersonality { Agg01 = 0.5f, Caution01 = 0.5f, Audacity01 = 0.5f, CasualtyTolerance01 = 0.5f, Duty = 1f, Source = PersonalitySource.Default };

                int assetCount = GatherAssets(s, w, in rules, table, ni, now, out int childCount, out int uncommitted);
                var input = new DecisionInput
                {
                    NodeId = nodeId, CommanderIndex = commander, ChildCount = childCount, UncommittedChildren = uncommitted,
                    Assets = _assets, AssetCount = assetCount, Openings = _openings, OpeningCount = openingCount,
                    Stance = w.Stance, PlanPhase = w.PlanPhase, MacroAi = w.MacroAi, MainEffortSector = w.MainEffortSector,
                    SideLiveExploits = LiveCount(table), NodeHasExploit = NodeHasExploit(table, nodeId), CounterstrokeOn = rules.CounterstrokeOn,
                    BattleKey = s.BattleKey, DecisionNumber = state.Decisions, NowHours = now,
                    Terrain = _terrain, Snapshot = s, Hesitation = _hesitation, Ledger = _ledger,
                };
                DecisionResult result = OpportunityDoctrine.Decide(in input, in p);
                state.LastDecision = now;
                state.Decisions++;

                if (result.Launch)
                {
                    Launch(s, table, sink, in result, in p, nodeId, commander, openingCount, assetCount, now);
                    state.LastDeclineOpening = -1;
                    state.LastDeclineReason = null;
                }
                else if (ReferenceEquals(result.Reason, OpportunityDoctrine.RollMiss))
                {
                    _hesitation.Mark(commander, result.OpeningId, now);
                    Decline(sink, state, nodeId, commander, in result, in p, table);
                }
                else if (result.AssetId >= 0)
                {
                    Decline(sink, state, nodeId, commander, in result, in p, table);
                }
            }
        }

        // The terrain of this tick's Run (the recovery-path check reads it through the decision input).
        private IOpeningTerrain _terrain;

        // A decline row only when the verdict changed for that opening: a per-tick repeat of the same reason is not a row.
        private static void Decline(IOpportunitySink sink, NodeState state, int nodeId, int commander, in DecisionResult result, in OpportunityPersonality p, ExploitTable table)
        {
            if (sink == null) return;
            if (state.LastDeclineOpening == result.OpeningId && ReferenceEquals(state.LastDeclineReason, result.Reason)) return;
            state.LastDeclineOpening = result.OpeningId;
            state.LastDeclineReason = result.Reason;
            sink.Declined(nodeId, commander, in result, in p, table.Count);
        }

        private void Launch(BattleSnapshot s, ExploitTable table, IOpportunitySink sink, in DecisionResult result, in OpportunityPersonality p,
            int nodeId, int commander, int openingCount, int assetCount, double now)
        {
            int ai = -1, oi = -1;
            for (int i = 0; i < assetCount; i++) if (_assets[i].Id == result.AssetId) { ai = i; break; }
            for (int i = 0; i < openingCount; i++) if (_openings[i].Id == result.OpeningId) { oi = i; break; }
            if (ai < 0 || oi < 0) return;

            ExploitEntry entry = ExploitLifecycle.Launch(_nextExploitId++, commander, nodeId, in _assets[ai], in _openings[oi], result.RequiredOdds, now);
            if (!table.Add(in entry))
            {
                TableRefusals++;
                return;
            }
            BindSubtree(s, table, entry.Id, _assets[ai].OwnIndex);
            _ledger.Add(_assets[ai].OwnIndex);
            _detector.SetClaimed(entry.OpeningId);
            _openings[oi].Claimed = true;
            sink?.Launched(in entry, in result, in p, table.Count);
        }

        private NodeState StateOf(int nodeId, double now)
        {
            if (!_nodes.TryGetValue(nodeId, out NodeState state))
            {
                state = new NodeState { FirstSeen = now };
                _nodes[nodeId] = state;
            }
            return state;
        }

        private static int LiveCount(ExploitTable table)
        {
            int n = 0;
            for (int i = 0; i < table.Count; i++)
                if (table.At(i).Phase != ExploitPhase.None && table.At(i).Phase != ExploitPhase.Recall) n++;
            return n;
        }

        private static bool NodeHasExploit(ExploitTable table, int nodeId)
        {
            for (int i = 0; i < table.Count; i++)
                if (table.At(i).DecidingNodeId == nodeId && table.At(i).Phase != ExploitPhase.None && table.At(i).Phase != ExploitPhase.Recall) return true;
            return false;
        }

        // The deciding node's direct group children: the eligible ones become assets (requirement-asset-eligibility), and every
        // child counts toward the reserve floor, committed or not.
        private int GatherAssets(BattleSnapshot s, IOpportunityWorld w, in OpeningRules rules, ExploitTable table, int nodeIndex, double now,
            out int childCount, out int uncommitted)
        {
            childCount = 0;
            uncommitted = 0;
            int assets = 0;
            int n = s.DirectChildren(nodeIndex, _children);
            bool defender = w.Stance == BattleStance.Defender;
            TacticalFrontAxis axis = s.FrontAxis;
            for (int c = 0; c < n; c++)
            {
                int ci = _children[c];
                ref UnitRecord rec = ref s.Own[ci];
                if ((rec.Flags & UnitFlags.IsGroup) == 0 || (rec.Flags & UnitFlags.Alive) == 0) continue;
                childCount++;

                w.TryRoleTask(rec.InstanceId, out CommandNodeRole role, out CommandTaskType task);
                UnitFlags subtree = rec.Flags | _descFlags[ci];
                bool engaged = PostureSense.CloseEngaged(s, ci, _descFlags[ci]);
                bool exploiting = table.IsExploiting(rec.InstanceId);
                bool childUncommitted = !engaged && !exploiting &&
                    (role == CommandNodeRole.Reserve || task == CommandTaskType.ReserveWait || task == CommandTaskType.FormUp ||
                     task == CommandTaskType.HoldObjective || task == CommandTaskType.HoldChoke);
                if (childUncommitted) uncommitted++;
                if (assets >= _assets.Length) continue;

                bool activeMove = PostureSense.ActiveMove(rec, _moving[ci]);
                bool cavalry = (subtree & UnitFlags.IsCavalry) != 0 && (subtree & (UnitFlags.IsInfantry | UnitFlags.IsArtillery)) == 0;
                var ctx = new AssetContext
                {
                    DrivenByPosture = w.DrivenByPosture(rec.InstanceId),
                    DescendantPlayerOwned = (_descFlags[ci] & (UnitFlags.PlayerOwned | UnitFlags.PlayerCommand)) != 0,
                    DescendantFeud = (_descFlags[ci] & UnitFlags.Feud) != 0,
                    ActiveMove = activeMove,
                    PendingOrder = PostureSense.PendingOrder(rec, activeMove, false),
                    RecentExecutorMove = w.RecentExecutorMove(rec.InstanceId, now),
                    UnderManeuver = w.UnderManeuver(rec.InstanceId),
                    UnderRecon = w.UnderRecon(rec.InstanceId),
                    UnderProbeHold = w.UnderProbeHold(rec.InstanceId),
                    UnderArrivalHold = w.UnderArrivalHold(rec.InstanceId),
                    ExploitInSubtreeOrAncestor = exploiting,
                    SafetyClaim = w.SafetyClaim(rec.InstanceId),
                    InCooldown = table.InCooldown(rec.InstanceId, now),
                    Cavalry = cavalry,
                    LossFraction = MeanLoss(ci),
                    CavalryMoraleBar = w.CavalryMoraleBar,
                };
                if (!OpportunityDoctrine.Eligible(in rec, in ctx, out _)) continue;

                bool frontage = HasFrontage(role, task);
                if (defender)
                {
                    // A counterstroke asset holds no frontage: a Reserve, or a second-line node at least LineDepthBandMeters behind the own trace.
                    bool secondLine = role == CommandNodeRole.Reserve ||
                        (_detector.TryTraceBehind(rec.X, rec.Z, out float behind, out _, out _) && behind >= OpeningDetector.LineDepthBandMeters);
                    frontage = !secondLine;
                }
                _assets[assets++] = new ExploitAsset
                {
                    Id = rec.InstanceId,
                    OwnIndex = ci,
                    Sector = axis.SectorOf(rec.X, rec.Z),
                    X = rec.X,
                    Z = rec.Z,
                    Lateral = axis.LateralOf(rec.X, rec.Z),
                    Strength = _strength[ci],
                    GroupMorale = rec.GroupMorale,
                    GroupWidth = rec.GroupWidth,
                    LossFraction = ctx.LossFraction,
                    Cavalry = cavalry,
                    InContact = engaged,
                    HasFrontage = frontage,
                    InMainEffort = InMainEffort(s, w, ci, task),
                    Uncommitted = childUncommitted,
                    Task = task,
                };
            }
            return assets;
        }

        private static bool HasFrontage(CommandNodeRole role, CommandTaskType task)
        {
            if (role == CommandNodeRole.Reserve) return false;
            return task == CommandTaskType.HoldObjective || task == CommandTaskType.HoldChoke || task == CommandTaskType.AttackObjective ||
                task == CommandTaskType.SupportAttack || task == CommandTaskType.FixEnemy;
        }

        // A brigade or division asset is in the main effort when its army-level ancestor (the direct child of the army) has role Main or
        // SupportMain and its own task is AttackObjective, SupportAttack or FixEnemy; a reserve or holding brigade inside the
        // main-effort corps is that corps' reserve and eligible.
        private static bool InMainEffort(BattleSnapshot s, IOpportunityWorld w, int index, CommandTaskType task)
        {
            if (task != CommandTaskType.AttackObjective && task != CommandTaskType.SupportAttack && task != CommandTaskType.FixEnemy) return false;
            int a = index;
            for (int steps = 0; steps < s.OwnCount; steps++)
            {
                int parent = s.Own[a].ParentIndex;
                if (parent < 0 || s.Own[parent].ParentIndex < 0) break;
                a = parent;
            }
            return w.IsMainEffortChild(s.Own[a].InstanceId);
        }
    }
}
