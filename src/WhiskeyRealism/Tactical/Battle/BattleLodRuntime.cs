using System;
using System.Collections.Generic;
using System.Text;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // The runtime half of the battle AI level of detail (BC15, plan docs/superpowers/plans/2026-10-15-battle-core.md § BC15). Runtime only.
    //
    // BattleCoreTick.RunArbiter calls BeginTick once per arbiter tick, right after arbiter.BeginTick() and before the first producer: it builds the
    // side's inputs (the vanilla side-stat cycle, the plan signature, the ledger and leaf-map lookups, the friendly grid), runs BattleLod.Classify
    // on that tick's snapshot, clears what the pass read (the perception latches, last tick's executor refusals) and carries the published claims
    // of every Distant group that is not due into the new tick's table. The tiered producers then ask IsDue before they evaluate a group.
    //
    // Fail-safe: any exception in the pass resets every group to Full for the minimum hold and drops the carried cells, so a fault costs the saving
    // and nothing else. Off (Enable Battle AI Level Of Detail): the pass never runs, no snapshot is covered, every unit is due and nothing is
    // carried, which is the behaviour before BC15.
    //
    // Which producers are tiered (decision 5 as amended by the verify round 1 ruling: tier a producer only where every input it reads is a promotion
    // trigger or a Distant condition):
    //   Tiered:    Posture (and the leaf-brigade path inside it) and FacingPulse, which carry the NavMesh cost; GroupStance, whose two inputs beyond the
    //              posture's (the coordinator's cached #45 decision, the stance ordered on the group) are signature fields (LodPromotion.Stance).
    //   Inherits:  FireControl is not tiered, but it reads the posture record of a group, which a carried group keeps whole (PostureRecordCache:
    //              Submitted and FacingSubmitted included, so fire control and recon see what the last full-rate tick showed them).
    //   Full-only: DivisionPlay (decides across siblings), ArtilleryDoctrine (reads the battery's combat behavior, ammo, morale, hold state and the
    //              enemy guns in range; batteries are few), Withdrawal, ArtilleryPointBlank, ChargeDemotion, Opportunity, Recon, ReserveRelease
    //              (this is where LeafReserveRelease's moves and the Defender's reserve slots are issued, every tick, side-wide) and Charge: they
    //              act in contact, on scouts, across siblings or side-wide. Their effect on a tiered group reaches it as a trigger: a released
    //              reserve changes its leaf task (trigger c) and a move makes it unsettled.
    //   Full-only groups (HasLatchOrManeuver): a live exploit (asset, descendants, ancestors), a works hold or pin, a recon probe or hold or the
    //              scout, a main-arrival hold, an envelopment maneuver (heading, hold, via). The BC14W contest turns and the BC14O chain contest need a
    //              path to act on, and a group with a path is never settled, so a vanilla move onto a Distant group promotes it on the tick it shows.
    //   Unsettled below: an order of any unit under a group that the executor has not finished with (a pending move, a retry ticket, a refusal other
    //              than Duplicate) keeps the group Full (BattleLod.FoldUnsettled).
    internal static class BattleLodRuntime
    {
        private const double RowIntervalHours = 0.1;

        // Index = bit of LodPromotion (the order of the enum's single flags).
        private static readonly string[] ReasonNames =
        {
            "observation", "parent", "target", "bearing", "corridor", "plan", "distance", "fire", "moving", "pending", "player-chain", "stance",
        };

        private static readonly PostureLodLookup[] Lookups = { new PostureLodLookup(), new PostureLodLookup() };
        private static readonly double[] LastRowHours = { double.NaN, double.NaN };
        private static readonly StringBuilder RowText = new StringBuilder(128);
        private static bool _firstFireLogged;

        static BattleLodRuntime()
        {
            BattleScope.Register("battle-lod-runtime", Reset);
        }

        private static void Reset()
        {
            LastRowHours[0] = LastRowHours[1] = double.NaN;
            _firstFireLogged = false;
        }

        /// <summary>The tier pass and the carry of one arbiter tick; call after <c>arbiter.BeginTick()</c>, before any producer.</summary>
        internal static void BeginTick(BattleSnapshot s, OrderArbiter arbiter, OrderExecutor executor, double nowHours)
        {
            if (s == null || arbiter == null || executor == null) return;
            BattleLodState state = BattleLodState.ForAlliance(s.Alliance);
            if (state == null || !Plugin.BattleAiLevelOfDetailEnabled()) return;
            using (TelemetryPerf.Scope("tactical.lod", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.5))
            {
                try
                {
                    Pass(s, state, arbiter, executor, nowHours);
                }
                catch (Exception ex)
                {
                    state.ResetAllFull(nowHours);
                    PostureTick.ForgetLast(s.Alliance);
                    arbiter.BeginTick();
                    OnceLog.Warning("tactical-battle-core:lod-failed",
                        "[BattleLod] tier pass threw; the side runs at full rate: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        private static void Pass(BattleSnapshot s, BattleLodState state, OrderArbiter arbiter, OrderExecutor executor, double nowHours)
        {
            TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(s.Alliance);
            ArmyOrchestrator army = side?.Army;
            // The posture scratch builds once per snapshot; the pass needs its moving-unit counts and friendly grid before any producer runs.
            PostureTick.State tick = PostureTick.For(s);
            if (army == null || tick == null)
            {
                state.ResetAllFull(nowHours);
                return;
            }

            PostureLodLookup lookup = Lookups[s.Alliance];
            lookup.Bind(s, tick, side, army, executor);
            var input = new LodSideInput
            {
                CycleHours = GamePrefs.sidestatupdatecycle,
                PlanSignature = PlanSignature(army),
                Executor = executor,
                Tasks = lookup,
                Friendly = lookup,
                MovingDescendants = tick.Moving,
            };
            BattleLod.Classify(s, state, in input, nowHours);
            BattlePerceptionProbe.ConsumeChangeLatch(s.Alliance);
            executor.ClearRefusals();
            CarryClaims(s, state, arbiter);
            Report(s, state, nowHours);
        }

        // The side's plan, and what the posture pipeline rewrites a task by: the reinforcement opportunity and the battle stance (decision 2(f)).
        private static int PlanSignature(ArmyOrchestrator army)
        {
            unchecked
            {
                int h = army.HasPlan ? 1 : 0;
                if (army.HasPlan)
                {
                    h = h * 31 + (int)army.CurrentPlan.PlanId;
                    h = h * 31 + (int)army.CurrentPlan.Phase;
                    h = h * 31 + army.CurrentPlan.MainEffortSector;
                }
                h = h * 31 + (int)ReadOpportunity(army);
                h = h * 31 + (int)army.CurrentBattleStance;
                return h;
            }
        }

        internal static ReinforcementOpportunity ReadOpportunity(ArmyOrchestrator army)
        {
            try { return army.CurrentReinforcementOpportunity.Opportunity; }
            catch { return ReinforcementOpportunity.NoOpportunity; }
        }

        // Every unit under a Distant group that is not due keeps the claims of the tiered sources it published last tick (the group's own Move,
        // Stance and Formation cells, its regiments' facing cells); a source that runs every tick resubmits and is not carried.
        private static void CarryClaims(BattleSnapshot s, BattleLodState state, OrderArbiter arbiter)
        {
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if (state.IsDue(i)) continue;
                arbiter.CarryForward(own[i].InstanceId);
            }
        }

        /// <summary>
        /// Whether the tiered producers evaluate own unit <paramref name="ownIndex"/> this tick. A snapshot the pass did not cover (the flag is off,
        /// the pass failed, no pass yet) is always due.
        /// </summary>
        internal static bool IsDue(BattleSnapshot s, int ownIndex)
        {
            BattleLodState state = BattleLodState.ForAlliance(s.Alliance);
            if (state == null || !state.Covers(s) || state.IsDue(ownIndex)) return true;

            // Not due. The opportunity producer runs ahead of the posture producer in this very tick and may have launched an exploit on the group,
            // which changes the order its posture pipeline resolves (decision 12): wake it now, as the same-tick rule requires.
            ref UnitRecord rec = ref s.Own[ownIndex];
            if ((rec.Flags & UnitFlags.IsGroup) == 0 || !ExploitTable.ForAlliance(s.Alliance).IsExploiting(rec.InstanceId)) return false;
            if (!state.PromoteNow(s, ownIndex, LodPromotion.Target, s.DoctrineHours)) return false;
            OrderArbiter.ForAlliance(s.Alliance)?.DropCarried(rec.InstanceId);
            return true;
        }

        /// <summary>The posture producer's NavMesh budget was spent before this group's plan: it stays Full until a plan runs for it.</summary>
        internal static void NavDeferred(BattleSnapshot s, int groupId)
        {
            BattleLodState.ForAlliance(s.Alliance)?.MarkNavDeferred(groupId);
        }

        /// <summary>A posture evaluation of the group began, or its plan ran: no deferral is outstanding.</summary>
        internal static void NavPlanned(BattleSnapshot s, int groupId)
        {
            BattleLodState.ForAlliance(s.Alliance)?.MarkNavPlanned(groupId);
        }

        /// <summary>A side stood down: every group Full for the minimum hold, no remembered posture. The stand-down cleared the claims already.</summary>
        internal static void StandDown(int alliance)
        {
            BattleLodState state = BattleLodState.ForAlliance(alliance);
            if (state == null) return;
            state.ResetAllFull(TacticalDoctrineTime.NowBattleHours);
            PostureTick.ForgetLast(alliance);
            LastRowHours[alliance] = double.NaN;
        }

        /// <summary>A new battle day (vanilla's night end-of-day cycle ended): both sides start over.</summary>
        internal static void NewBattleDay()
        {
            StandDown(0);
            StandDown(1);
        }

        // ---- telemetry ---------------------------------------------------------------------------------

        private static void Report(BattleSnapshot s, BattleLodState state, double nowHours)
        {
            if (state.DistantCount > 0 && !_firstFireLogged)
            {
                _firstFireLogged = true;
                OnceLog.Info("tactical-battle-core:lod-first-fire",
                    "[BattleLod] first distant tier: alliance=" + s.Alliance + " full=" + state.FullCount + " distant=" + state.DistantCount);
            }

            int a = s.Alliance;
            if (nowHours - LastRowHours[a] < RowIntervalHours) return;
            LastRowHours[a] = nowHours;
            try
            {
                if (Plugin.Instance != null && Plugin.Instance.EnableTacticalObserver != null && Plugin.Instance.EnableTacticalObserver.Value)
                    EmitRow(a, state);
            }
            finally
            {
                state.ClearPromotionCounts();
            }
        }

        private static void EmitRow(int alliance, BattleLodState state)
        {
            RowText.Clear();
            for (int bit = 0; bit < ReasonNames.Length; bit++)
            {
                int n = state.PromotionCountOf((LodPromotion)(1 << bit));
                if (n == 0) continue;
                if (RowText.Length > 0) RowText.Append(';');
                RowText.Append(ReasonNames[bit]).Append('=').Append(n);
            }
            string promotions = RowText.Length > 0 ? RowText.ToString() : "-";
            int full = state.FullCount, distant = state.DistantCount, due = state.DueCount, carried = state.CarriedCount, deferred = state.NavDeferredCount;
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalLod", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(alliance)
                    .WithField("full", full)
                    .WithField("distant", distant)
                    .WithField("due", due)
                    .WithField("carried", carried)
                    .WithField("navDeferred", deferred)
                    .WithField("promotions", promotions));
        }

        // ---- the lookups the pass reads (ITaskLookup, IFriendlyCorridor) -----------------------------------

        /// <summary>
        /// What the posture pipeline resolves for a group, for the tier pass: the task it decides from, its target, its latches, its leaf slot, and
        /// the friendly units in its path corridor. Indexes the ledger's command states and doctrine orders by group id once per snapshot (the
        /// pipeline's own lookups scan them per group; first match wins, as those scans do); spends no NavMesh budget.
        /// </summary>
        internal sealed class PostureLodLookup : ITaskLookup, IFriendlyCorridor
        {
            private BattleSnapshot _s;
            private PostureTick.State _tick;
            private TacticalBattleOrchestrator _side;
            private ArmyOrchestrator _army;
            private OrderExecutor _executor;
            private ExploitTable _exploits;
            private IReadOnlyList<CommandNodeOperationalState> _states;
            private IReadOnlyList<CommandDoctrineOrder> _orders;
            private IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> _leaves;
            private ReinforcementOpportunity _opportunity;
            private BattleStance _stance;
            private long _indexedTick = -1;
            private int _indexedAlliance = -1;
            private readonly Dictionary<int, int> _stateAt = new Dictionary<int, int>(128);
            private readonly Dictionary<int, int> _orderAt = new Dictionary<int, int>(128);

            internal void Bind(BattleSnapshot s, PostureTick.State tick, TacticalBattleOrchestrator side, ArmyOrchestrator army, OrderExecutor executor)
            {
                _s = s;
                _tick = tick;
                _side = side;
                _army = army;
                _executor = executor;
                _exploits = ExploitTable.ForAlliance(s.Alliance);
                _opportunity = ReadOpportunity(army);
                _stance = army.CurrentBattleStance;
                _states = army.CurrentCommandOperations;
                _orders = army.CurrentDoctrineOrders;
                _leaves = army.CurrentLeafBrigadeMap;
                if (_indexedTick == s.TickId && _indexedAlliance == s.Alliance) return;
                _indexedTick = s.TickId;
                _indexedAlliance = s.Alliance;
                _stateAt.Clear();
                if (_states != null)
                    for (int i = 0; i < _states.Count; i++)
                        if (TacticalPatchIds.TryParseNodeId(_states[i].NodeId, out int stateId) && stateId != 0) _stateAt.TryAdd(stateId, i);
                _orderAt.Clear();
                if (_orders != null)
                    for (int i = 0; i < _orders.Count; i++)
                        if (TacticalPatchIds.TryParseNodeId(_orders[i].NodeId, out int orderId) && orderId != 0) _orderAt.TryAdd(orderId, i);
            }

            private bool TryOrder(int groupId, out CommandDoctrineOrder order)
            {
                if (_orderAt.TryGetValue(groupId, out int at) && _orders[at].HasPurpose)
                {
                    order = _orders[at];
                    return true;
                }
                order = default(CommandDoctrineOrder);
                return false;
            }

            private bool InPool(int groupId) => ArmyPostureInputsRuntime.InAiPool(_s.Alliance, groupId);

            public CommandTaskType TaskFor(int groupId)
            {
                if (InPool(groupId))
                {
                    if (!_stateAt.TryGetValue(groupId, out int at)) return CommandTaskType.None;
                    CommandNodeOperationalState state = _states[at];
                    CommandTaskType task = state.Task;
                    bool worksHeld = WorksHoldGuard.Holds(state.Task, state.Reason);
                    if (TryOrder(groupId, out CommandDoctrineOrder order))
                    {
                        task = order.Task;
                        worksHeld |= WorksHoldGuard.Holds(order.Task, order.Reason);
                    }
                    return Rewritten(task, DirectChildRole.Main, worksHeld);
                }
                if (_leaves != null && _leaves.TryGetValue(groupId, out TacticalLeafBrigadeMap.LeafAssignment leaf) && leaf.LeafTask != CommandTaskType.None)
                    return Rewritten(leaf.LeafTask, leaf.LeafRole, leaf.WorksHeld);
                return CommandTaskType.None;
            }

            // The rewrites that turn a ledger task into the one the producer decides from, both the pool pipeline and the leaf cascade.
            private CommandTaskType Rewritten(CommandTaskType task, DirectChildRole role, bool worksHeld)
            {
                CommandTaskType overridden = TacticalOpportunityTaskOverride.Override(task, role, _opportunity, worksHeld, out _);
                return PostureStanceGuard.Task(overridden, _stance);
            }

            public bool TryTarget(int groupId, out float x, out float z, out float maneuverHeading, out int orderFormation)
            {
                x = 0f;
                z = 0f;
                maneuverHeading = -1f;
                orderFormation = -1;
                if (!InPool(groupId)) return false;   // a leaf brigade is steered by its layout slot (TrySlot)
                if (TryOrder(groupId, out CommandDoctrineOrder order))
                {
                    maneuverHeading = order.ManeuverHeading;
                    orderFormation = order.HasFormation ? order.Formation : -1;
                    if (order.PrimaryTarget.HasValue && PostureSense.HasPoint(order.PrimaryTarget.X, order.PrimaryTarget.Z))
                    {
                        x = order.PrimaryTarget.X;
                        z = order.PrimaryTarget.Z;
                        return true;
                    }
                }
                // No target on the order: the posture pipeline walks to the operation's primary objective, so a change of operation moves the target.
                return PostureTargets.TryOperationObjective(_side, out x, out z, out _);
            }

            public bool HasLatchOrManeuver(int groupId)
            {
                int alliance = _s.Alliance;
                if (_exploits.IsExploiting(groupId)) return true;
                if (ReconIntentProducer.HasProbeOrHold(alliance, groupId) || ReconIntentProducer.MissionScoutId(alliance) == groupId) return true;
                if (PostureIntentProducer.UnderArrivalHold(groupId)) return true;
                if (_stateAt.TryGetValue(groupId, out int at))
                {
                    CommandNodeOperationalState state = _states[at];
                    if (WorksHoldGuard.Holds(state.Task, state.Reason) || _army.PinsWorksNode(state.NodeId)) return true;
                }
                if (TryOrder(groupId, out CommandDoctrineOrder order) &&
                    (WorksHoldGuard.Holds(order.Task, order.Reason) || order.ManeuverHeading >= 0f || order.ManeuverHold || order.HasVia))
                    return true;
                return _leaves != null && _leaves.TryGetValue(groupId, out TacticalLeafBrigadeMap.LeafAssignment leaf) && leaf.WorksHeld;
            }

            public int StanceDecision(int ownIndex)
            {
                if (!GroupStanceIntentProducer.TryResolvedDecision(_s, ownIndex, out TacticalResolvedGroupDecision r)) return -1;
                unchecked
                {
                    int h = (int)r.Kind + 1;
                    h = h * 31 + r.FinalStance;
                    h = h * 31 + (r.FromDoctrine ? 1 : r.FromLedger ? 2 : 0);
                    return h;
                }
            }

            public bool TrySlot(int ownIndex, out float x, out float z, out float heading)
            {
                x = 0f;
                z = 0f;
                heading = -1f;
                ref UnitRecord rec = ref _s.Own[ownIndex];
                if (InPool(rec.InstanceId) || _leaves == null ||
                    !_leaves.TryGetValue(rec.InstanceId, out TacticalLeafBrigadeMap.LeafAssignment leaf) || leaf.LeafTask == CommandTaskType.None)
                    return false;
                int parent = rec.ParentIndex;
                RefuseFlank parentRefuse = parent >= 0 && parent < _s.OwnCount ? _executor.LastIssuedRefuse(_s.Own[parent].InstanceId) : RefuseFlank.None;
                return LeafSlotTarget.TryResolve(_s, ownIndex, GamePrefs.refuseflankdepth, GamePrefs.refuseflankskew, parentRefuse,
                    out _, out x, out z, out heading);
            }

            public int Signature(int selfId, float x0, float z0, float x1, float z1, float widthMeters, out int count)
            {
                return _tick.Friendly.CorridorSignature(selfId, x0, z0, x1, z1, widthMeters, out count);
            }
        }
    }
}
