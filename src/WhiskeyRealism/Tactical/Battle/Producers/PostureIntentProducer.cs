using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // #61 (operations-ledger posture execution) as battle-core intent producers
    // (plan BC5a). The former BattleCommandPostureExecutorPatch Postfix on
    // AIBattle.AdjustGroupFormations is gone: BattleCoreArbiterPatch (the same
    // hook, D3) runs these producers, resolves one claim per (group, channel)
    // and issues the winners through OrderExecutor. Producers read the battle
    // snapshot (unit state, perception, command-parent map) and the side's
    // orchestrator doctrine state (ledger, doctrine orders, leaf map,
    // opportunity), never live vanilla unit fields; PostureSense holds the
    // snapshot proxies for what #61 read live.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   UpdateAITasks 5857-5872: AdjustGroupFormations (5870, the arbiter hook)
    //     runs after UpdateMovingTargets 5862, CheckFlankMoves 5867 and
    //     AdjustGroupAIStance 5869, so these orders are the macro cycle's last.
    //   AdjustGroupFormations 5875-5935: vanilla re-forms only settled groups;
    //     the executor's CommandFormationWritePolicy keeps that rule.
    //   SetWaypoint 91232-91640, SetGroupFormation 91822-92110, ChangeStance 90772,
    //     ChangeCombatBehavior 90944-90966, RotateRegiment 130159: issued by
    //     OrderExecutorRuntime, never here.
    //   GetAttachedUnitsReg(excludedechainedunits) 119865: group orders skip a
    //     detached subtree; PostureSense.CollectRotatableRegiments follows it.
    //   Regiment.AddPath area costs 130364-130394 (R5): PostureNavAreaCosts, set on a
    //     NavMeshQueryFilter the path probe passes to NavMesh.CalculatePath (BC10): the
    //     global area costs, which every AddPath rewrites, are never written.
    //
    //   AIBattle.BlockedMovingOrder 3054-3150 (UpdateOrders 3382): an
    //     objective-type group move over 150 m carries
    //     IntentFlags.AvoidCoveredCrossings, as shipped #61 sent it (vanilla's own
    //     line-group moves, 6953-6956); OrderExecutorRuntime issues it.
    //     The covered crossings it avoids are the perceived ones (CoveredCrossings, vanilla's
    //     UpdateBlockedCrossings rules 7917-7973 over the snapshot's observations).
    //
    // Deleted with #61, not ported (the executor owns them): the recent-order
    // stamps, failed-write backoff, brigade task hold, courier cadence, outbound
    // order ledger, duplicate-waypoint and safe-waypoint checks, the write gate
    // and the groupsubordinatesmoving stamp.

    // What the posture producer decided for one group this tick; the facing
    // pulse and fire control read it (they ran only on a group #61 left alone).
    internal struct GroupPosture
    {
        public bool Has;
        public CommandNodeOperationalState State;
        public bool CloseEngaged;
        public bool FlankRisk;
        public bool Submitted;
        public bool FacingSubmitted;
    }

    // Per-side scratch built once per snapshot (TickId): subtree aggregates, the
    // enemy record index, the friendly grid and the shared NavMesh budget.
    internal static class PostureTick
    {
        static PostureTick()
        {
            BattleScope.Register("posture-tick", Reset);
        }

        internal sealed class State
        {
            public long TickId = -1;
            public UnitFlags[] Descendants;
            public int[] Moving;
            public GroupPosture[] Groups = new GroupPosture[BattleSnapshot.InitialCapacity];
            public readonly Dictionary<int, int> EnemyIndexById = new Dictionary<int, int>(256);
            public readonly TacticalFriendlySpatialGrid Friendly = new TacticalFriendlySpatialGrid();
            // Up to 4 nav plans (12 NavMesh.CalculatePath) per arbiter tick and side,
            // consumed only by this producer (TryNavPlan).
            public readonly NavPlanBudget Nav = new NavPlanBudget(4);
            // BC10 decision 4: the NavMesh query filter of the path probe, built once per
            // snapshot from the live area costs plus AddPath's battle costs.
            public NavMeshQueryFilter NavFilter;
            public bool NavFilterReady;
            // BC10 decision 6: one road-route plan (a NavMesh path and an A*) per side and snapshot.
            public readonly NavPlanBudget RouteBudget = new NavPlanBudget(1);
            public readonly RoadRouteScratch RouteScratch = new RoadRouteScratch();
            public int LastOwnCount;
            public bool NavDeferred;
            public readonly List<float> SupportMainX = new List<float>(8);
            public readonly List<float> SupportMainZ = new List<float>(8);
            public bool SupportMainBuilt;
            // Grow-only, sized to the roster each tick.
            public int[] RegimentQueue = new int[64];
            public int[] Regiments = new int[64];
            public int[] Subtree = new int[64];
            public int[] Children = new int[64];
            // #44's perceived flank risk input (ArmyEvidenceBuilder.MeanFlankThreatByGroup).
            public float[] FlankThreatMean = new float[64];
            public int[] FlankThreatCount = new int[64];
        }

        private static readonly State[] _states = { new State(), new State() };

        /// <summary>The side's scratch for this snapshot, rebuilt on a new TickId.</summary>
        internal static State For(BattleSnapshot s)
        {
            if (s == null || s.Alliance < 0 || s.Alliance >= _states.Length) return null;
            State t = _states[s.Alliance];
            if (t.TickId == s.TickId) return t;

            int n = s.OwnCount;
            PostureSense.AggregateDescendants(s, ref t.Descendants, ref t.Moving);
            if (t.Groups.Length < n) t.Groups = new GroupPosture[Math.Max(n, t.Groups.Length * 2)];
            Array.Clear(t.Groups, 0, n);
            if (t.RegimentQueue.Length < n)
            {
                int size = Math.Max(n, t.RegimentQueue.Length * 2);
                t.RegimentQueue = new int[size];
                t.Regiments = new int[size];
                t.FlankThreatMean = new float[size];
                t.FlankThreatCount = new int[size];
            }
            ArmyEvidenceBuilder.MeanFlankThreatByGroup(s, t.FlankThreatMean, t.FlankThreatCount);

            t.EnemyIndexById.Clear();
            UnitRecord[] enemy = s.EnemyRecords;
            for (int e = 0; e < s.EnemyRecordCount; e++)
                if (enemy[e].InstanceId != 0) t.EnemyIndexById[enemy[e].InstanceId] = e;

            // #61 BuildFriendlyGrid from the snapshot: standing own combat units.
            t.Friendly.Clear();
            UnitRecord[] own = s.Own;
            for (int i = 0; i < n; i++)
            {
                UnitFlags f = own[i].Flags;
                if ((f & UnitFlags.Active) == 0 || PostureSense.Routed(own[i]) || (f & UnitFlags.PermanentlyDetached) != 0) continue;
                if (own[i].UnitTyp > TacticalUnitType.BattleGroupBrigade) continue;
                t.Friendly.Add(own[i].InstanceId, own[i].X, own[i].Z);
            }

            t.Nav.NextApply(t.LastOwnCount);
            t.NavFilterReady = false;
            t.RouteBudget.NextApply(0);
            t.LastOwnCount = n;
            t.SupportMainBuilt = false;
            t.TickId = s.TickId;
            return t;
        }

        /// <summary>The scratch only when the posture producer already ran on this snapshot.</summary>
        internal static State Current(BattleSnapshot s)
        {
            if (s == null || s.Alliance < 0 || s.Alliance >= _states.Length) return null;
            State t = _states[s.Alliance];
            return t.TickId == s.TickId ? t : null;
        }

        private static void Reset()
        {
            for (int a = 0; a < _states.Length; a++)
            {
                _states[a].TickId = -1;
                _states[a].LastOwnCount = 0;
                _states[a].Friendly.Clear();
                _states[a].EnemyIndexById.Clear();
            }
        }
    }

    /// <summary>
    /// The operations-ledger posture of every AI pool group (#61 TryApplyGroup) and
    /// of the leaf brigades nested under them (#61 TryApplyNestedLeafBrigades), at
    /// Priority.Executor.
    /// </summary>
    internal sealed class PostureIntentProducer : IBattleIntentProducer
    {
        static PostureIntentProducer()
        {
            BattleScope.Register("posture-producer", Reset);
        }

        public string Name => "Posture";

        // BR-M1 latch per Main leaf brigade, on the doctrine clock.
        private static readonly Dictionary<int, MainArrivalHoldLatch.State> _mainArrivalHold =
            new Dictionary<int, MainArrivalHoldLatch.State>();
        private static readonly List<TacticalMainArrivalSynchronizer.SupportMainPosition> _arrivalSupportScratch =
            new List<TacticalMainArrivalSynchronizer.SupportMainPosition>(8);

        public void Produce(BattleSnapshot s, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.posture", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PostureTick.State t = PostureTick.For(s);
                if (t == null || sink == null) return;
                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(s.Alliance);
                ArmyOrchestrator army = side?.Army;
                if (army == null) return;

                UnitRecord[] own = s.Own;
                int n = s.OwnCount;
                var leafMap = army.CurrentLeafBrigadeMap;
                // BC13: the opportunity producer (which runs just before this one) published the side's exploit table.
                ExploitTable exploits = ExploitTable.ForAlliance(s.Alliance);
                int start = t.Nav.StartIndex(n);
                for (int k = 0; k < n; k++)
                {
                    int i = (start + k) % n;
                    ref UnitRecord rec = ref own[i];
                    if (!IsCommandGroup(rec)) continue;

                    // #61 iterated AIBattle.unitsused, then the leaf brigades
                    // outside it; the pool is captured on the coordinator tick.
                    if (ArmyPostureInputsRuntime.InAiPool(s.Alliance, rec.InstanceId))
                    {
                        // An exploit order overrides the ledger order of the asset; a descendant follows (requirement-exploit-override).
                        bool hasState = PostureLedger.TryResolveState(rec.InstanceId, side, out CommandNodeOperationalState state);
                        bool hasOrder = false;
                        CommandDoctrineOrder order = default(CommandDoctrineOrder);
                        ExploitOverrideKind exploitKind = ExploitOverrideKind.None;
                        if (exploits.TryOverride(rec.InstanceId, out CommandDoctrineOrder exploitOrder, out CommandNodeOperationalState exploitState))
                        {
                            exploitKind = ExploitPosture.KindOf(in exploitOrder);
                            state = ExploitPosture.Overlay(state, hasState, exploitState, exploitKind);
                            hasState = true;
                            hasOrder = true;
                            order = exploitOrder;
                        }
                        else if (hasState)
                        {
                            hasOrder = PostureLedger.TryResolveDoctrineOrder(rec.InstanceId, army, state.NodeId, out order);
                        }
                        if (!hasState) continue;
                        bool submitted = PosturePipeline.Run(s, t, i, state, hasOrder, order, side, sink,
                            IntentPriority.Executor, IntentSource.PostureExecutor, PostureIntentPlan.ExecutorReason,
                            recordTick: true, exploitKind);
                        t.Groups[i].Submitted = submitted;
                        continue;
                    }

                    if (rec.UnitTyp < TacticalUnitType.BattleGroupBrigade || leafMap == null) continue;
                    if (!leafMap.TryGetValue(rec.InstanceId, out TacticalLeafBrigadeMap.LeafAssignment leaf)) continue;
                    if (leaf.LeafTask == CommandTaskType.None) continue;
                    ProduceLeaf(s, t, i, leaf, side, army, sink, exploits);
                }
            }
        }

        // AI-controlled command groups: Active, not player-owned (spec §3).
        internal static bool IsCommandGroup(in UnitRecord rec)
        {
            UnitFlags f = rec.Flags;
            return (f & UnitFlags.IsGroup) != 0 && (f & UnitFlags.Active) != 0 && (f & UnitFlags.PlayerOwned) == 0;
        }

        // #61 TryApplyLeafBrigade + ApplyLeafBrigadePosture (B-leaf, BR-M1).
        private static void ProduceLeaf(BattleSnapshot s, PostureTick.State t, int i, TacticalLeafBrigadeMap.LeafAssignment leaf,
            TacticalBattleOrchestrator side, ArmyOrchestrator army, IIntentSink sink, ExploitTable exploits)
        {
            UnitRecord[] own = s.Own;
            ref UnitRecord rec = ref own[i];
            int id = rec.InstanceId;

            // BC13: a leaf brigade that is an exploit's asset runs the posture pipeline with the exploit order instead of the leaf
            // slot path; one that follows an exploiting ancestor gets no Move or Formation (its facing pulse and fire control run).
            if (exploits.TryOverride(id, out CommandDoctrineOrder exploitOrder, out CommandNodeOperationalState exploitState))
            {
                _mainArrivalHold.Remove(id);
                bool hasLedger = PostureLedger.TryResolveState(id, side, out CommandNodeOperationalState ledgerState);
                ExploitOverrideKind leafKind = ExploitPosture.KindOf(in exploitOrder);
                CommandNodeOperationalState merged = ExploitPosture.Overlay(ledgerState, hasLedger, exploitState, leafKind);
                bool leafSubmitted = PosturePipeline.Run(s, t, i, merged, true, exploitOrder, side, sink,
                    IntentPriority.Executor, IntentSource.PostureExecutor, PostureIntentPlan.ExecutorReason,
                    recordTick: true, leafKind);
                t.Groups[i].Submitted = leafSubmitted;
                return;
            }

            ReinforcementOpportunity opportunity = ReinforcementOpportunity.NoOpportunity;
            try { opportunity = army.CurrentReinforcementOpportunity.Opportunity; }
            catch { /* NoOpportunity keeps the ledger task */ }
            CommandTaskType task = TacticalOpportunityTaskOverride.Override(leaf.LeafTask, leaf.LeafRole, opportunity, leaf.WorksHeld, out string overrideReason);

            // Arrival synchronization: a Main brigade on AttackObjective waits as
            // Screen for its SupportMain siblings (TacticalMainArrivalSynchronizer).
            if (task == CommandTaskType.AttackObjective && leaf.LeafRole == DirectChildRole.Main)
            {
                float aggression01 = Mathf.Clamp01((army.CommanderPersonality.Aggression + 1f) * 0.5f);
                CommandTaskType held = ResolveMainArrivalHold(s, t, i, aggression01, task, out bool holding, out string arrivalReason);
                if (holding)
                {
                    task = held;
                    if (overrideReason == null) overrideReason = arrivalReason;
                }
            }
            else
            {
                _mainArrivalHold.Remove(id);
            }

            // BC9 stance guard: a Defender's leaf brigade gets no Probe or attack task.
            task = PostureStanceGuard.Task(task, army.CurrentBattleStance);

            if (overrideReason != null)
                OnceLog.Info("tactical-leaf-cascade:opportunity-override",
                    "[TacticalLeafCascade] opportunity override: " + leaf.LeafTask + " -> " + task
                    + " (role=" + leaf.LeafRole + " opportunity=" + opportunity + " reason=" + overrideReason + ")");

            if (PostureSense.Routed(rec)) { PostureTelemetry.Leaf(s, i, leaf, task, "routed", false); return; }

            bool closeEngaged = PostureSense.CloseEngaged(s, i, t.Descendants[i]);
            bool flankRisk = BattleWriterPolicy.PerceivedFlankRisk(s, i, t.FlankThreatMean);
            bool activeMove = PostureSense.ActiveMove(rec, t.Moving[i]);
            bool pending = PostureSense.PendingOrder(rec, activeMove, allowStaleQueuedBypass: false);
            int slotParent = rec.ParentIndex;
            OrderExecutor executor = OrderExecutor.ForAlliance(s.Alliance);
            RefuseFlank parentRefuse = executor != null && slotParent >= 0 && slotParent < s.OwnCount
                ? executor.LastIssuedRefuse(own[slotParent].InstanceId)
                : RefuseFlank.None;
            bool hasSlot = LeafSlotTarget.TryResolve(s, i, GamePrefs.refuseflankdepth, GamePrefs.refuseflankskew, parentRefuse,
                out int parent, out float x, out float z, out float slotHeading);
            bool parentActive = false;
            if (parent >= 0)
            {
                bool parentMoving = PostureSense.ActiveMove(own[parent], t.Moving[parent]);
                parentActive = own[parent].RegimentPaths > 0 || parentMoving ||
                    PostureSense.PendingOrder(own[parent], parentMoving, allowStaleQueuedBypass: false);
            }

            LeafMoveDecision move = LeafBrigadeMovePolicy.Decide(task, parentActive, hasSlot, closeEngaged, pending, activeMove);
            if (!move.Move) { PostureTelemetry.Leaf(s, i, leaf, task, move.Reason, false); return; }

            sink.Submit(PostureIntentPlan.LeafMove(id, task, x, z, slotHeading, inContactWithdrawal: flankRisk));
            PostureTelemetry.Leaf(s, i, leaf, task, "submitted", true);
        }

        // BR-M1: holds a Main-effort brigade on AttackObjective as Screen until its
        // SupportMain siblings close on it, the budget elapses or an aggressive
        // commander releases it. Doctrine clock (the snapshot's hours).
        private static CommandTaskType ResolveMainArrivalHold(BattleSnapshot s, PostureTick.State t, int mainIndex,
            float aggression01, CommandTaskType attackTask, out bool holding, out string reason)
        {
            holding = false;
            reason = null;
            int id = s.Own[mainIndex].InstanceId;
            _mainArrivalHold.TryGetValue(id, out MainArrivalHoldLatch.State latch);
            if (!MainArrivalHoldLatch.ShouldEvaluate(latch)) return attackTask;

            BuildSupportMain(s, t);
            var supports = _arrivalSupportScratch;
            supports.Clear();
            float mx = s.Own[mainIndex].X;
            float mz = s.Own[mainIndex].Z;
            for (int k = 0; k < t.SupportMainX.Count; k++)
                supports.Add(new TacticalMainArrivalSynchronizer.SupportMainPosition(PostureSense.Distance(t.SupportMainX[k], t.SupportMainZ[k], mx, mz)));

            float now = (float)s.DoctrineHours;
            float elapsedSeconds = MainArrivalHoldLatch.ElapsedBattleSeconds(latch, now);
            CommandTaskType resolved = TacticalMainArrivalSynchronizer.ResolveMainAttackTask(
                DirectChildRole.Main, attackTask, supports, aggression01, elapsedSeconds,
                TacticalMainArrivalSynchronizer.DefaultSupportProximityThresholdMeters,
                MainArrivalHoldLatch.MaxHoldBattleSeconds,
                out holding);
            _mainArrivalHold[id] = MainArrivalHoldLatch.Next(latch, now, holding);
            if (holding) reason = "arrival-hold-screen";
            return resolved;
        }

        // SupportMain leaf positions, once per tick per side (BR-M6).
        private static void BuildSupportMain(BattleSnapshot s, PostureTick.State t)
        {
            if (t.SupportMainBuilt) return;
            t.SupportMainBuilt = true;
            t.SupportMainX.Clear();
            t.SupportMainZ.Clear();
            var leafMap = TacticalBattleCoordinator.GetSideOrchestrator(s.Alliance)?.Army?.CurrentLeafBrigadeMap;
            if (leafMap == null) return;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if (own[i].UnitTyp < TacticalUnitType.BattleGroupBrigade) continue;
                if ((own[i].Flags & UnitFlags.PlayerOwned) != 0 || PostureSense.Routed(own[i])) continue;
                if (!leafMap.TryGetValue(own[i].InstanceId, out var la) || la.LeafRole != DirectChildRole.SupportMain) continue;
                t.SupportMainX.Add(own[i].X);
                t.SupportMainZ.Add(own[i].Z);
            }
        }

        /// <summary>BC13: the Main-effort brigade is held as Screen until its SupportMain siblings arrive (an exploit never picks it).</summary>
        internal static bool UnderArrivalHold(int groupId)
        {
            return _mainArrivalHold.TryGetValue(groupId, out MainArrivalHoldLatch.State latch) && latch.Active && !latch.Released;
        }

        private static void Reset()
        {
            _mainArrivalHold.Clear();
            _arrivalSupportScratch.Clear();
        }
    }

    // Ledger lookups (#61 TryResolveLedgerState / TryResolveDoctrineOrder). Node
    // ids are "node-" + the GameObject instance id the snapshot keys units by.
    internal static class PostureLedger
    {
        internal static bool TryResolveState(int instanceId, TacticalBattleOrchestrator side, out CommandNodeOperationalState state)
        {
            state = default(CommandNodeOperationalState);
            try
            {
                ArmyOrchestrator army = side?.Army;
                if (army == null || instanceId == 0) return false;
                var operations = army.CurrentCommandOperations;
                if (operations != null)
                {
                    for (int i = 0; i < operations.Count; i++)
                    {
                        if (!TacticalPatchIds.NodeIdMatches(operations[i].NodeId, instanceId, 0)) continue;
                        state = operations[i];
                        return true;
                    }
                }

                CommandIntentResolution resolution = army.ResolveCommandIntentForGroup(0, instanceId);
                bool built = resolution.Found &&
                    CommandNodeOperationsRuntime.TryBuildSingle(resolution.Intent, army.CurrentOperation,
                        side.OperationsLedger?.CurrentObjectives, army.CurrentBattleStance, out state);
                // BC14W: a state built here is not one the store guarded; it passes the same guard with this side's context.
                if (built) state = army.GuardFallback(in state);
                return built;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryResolveDoctrineOrder(int instanceId, ArmyOrchestrator army, string nodeId, out CommandDoctrineOrder order)
        {
            order = default(CommandDoctrineOrder);
            try
            {
                var orders = army?.CurrentDoctrineOrders;
                if (orders == null) return false;
                for (int i = 0; i < orders.Count; i++)
                {
                    CommandDoctrineOrder candidate = orders[i];
                    if (!string.Equals(candidate.NodeId, nodeId, StringComparison.Ordinal) &&
                        !TacticalPatchIds.NodeIdMatches(candidate.NodeId, instanceId, 0))
                        continue;
                    if (!candidate.HasPurpose) return false;
                    order = candidate;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }

    // #61 TryApplyGroup minus the division-play override (D8: the division play
    // is its own Priority.Play producer): doctrine-order task, GT recon, cavalry
    // follow, local flank emergency, army opportunity override, then
    // CommandPostureExecutor.Decide and the target, as intents.
    internal static class PosturePipeline
    {
        internal const string ProbeReason = "probe-nomination";
        internal const string ManeuverFacingReason = "envelopment-turn";

        internal static bool Run(
            BattleSnapshot s,
            PostureTick.State t,
            int i,
            CommandNodeOperationalState state,
            bool hasOrder,
            CommandDoctrineOrder order,
            TacticalBattleOrchestrator side,
            IIntentSink sink,
            IntentPriority priority,
            IntentSource source,
            string reason,
            bool recordTick,
            ExploitOverrideKind exploitKind = ExploitOverrideKind.None)
        {
            ArmyOrchestrator army = side.Army;
            ref UnitRecord rec = ref s.Own[i];
            UnitFlags descendants = t.Descendants[i];

            if (hasOrder && order.Task != state.Task)
                state = state.WithRoleAndTask(order.Role, order.Task);

            bool closeEngaged = PostureSense.CloseEngaged(s, i, descendants);
            bool flankRisk = BattleWriterPolicy.PerceivedFlankRisk(s, i, t.FlankThreatMean);

            // BC13 requirement-exploit-override: a descendant of an exploiting asset follows. vanilla's group order re-lays every
            // child (SetGroupFormation 92076-92108), so the posture producer submits no Move and no Formation for it; its facing
            // pulse and fire control read the posture recorded here and run as usual.
            if (!ExploitPosture.SubmitsMoveOrFormation(exploitKind))
            {
                if (recordTick)
                {
                    ref GroupPosture followed = ref t.Groups[i];
                    followed.Has = true;
                    followed.State = state;
                    followed.CloseEngaged = closeEngaged;
                    followed.FlankRisk = flankRisk;
                }
                PostureTelemetry.Group(s, i, reason, state, new PostureExecutionDecision(PostureExecutionAction.NoWrite, ExploitLifecycle.FollowReason),
                    0, ExploitLifecycle.FollowReason);
                return false;
            }

            // An exploit order has already checked its target (spec D-C3): the probe nomination and hold, GT recon, the cavalry
            // follow rewrite and the army opportunity override do not apply to it. The local flank emergency stays.
            bool exploit = exploitKind == ExploitOverrideKind.Exploit;
            bool hasCavalry = ((rec.Flags | descendants) & UnitFlags.IsCavalry) != 0;
            bool visibleEnemy = s.ClosestVisibleEnemy(i, out _) >= 0;
            BattleStance stance = army != null ? army.CurrentBattleStance : BattleStance.Meeting;
            bool defender = stance == BattleStance.Defender;

            // BC9 reconnaissance in force: the recon planner nominated this group as its
            // band's probe in the Probe phase (BattleRecon.ProbeTick). The role is
            // assigned here, by the rewrite: the allocator's DirectChildRole has no Probe.
            bool probe = recordTick && !exploit && ReconIntentProducer.TryProbe(s.Alliance, rec.InstanceId, out _, out _);
            if (probe)
            {
                state = state.WithRoleAndTask(CommandNodeRole.Probe, CommandTaskType.Probe);
                if (hasOrder) order = order.WithTask(CommandTaskType.Probe, ProbeReason);
            }

            TacticalGrandTacticianReconDecision recon = exploit
                ? new TacticalGrandTacticianReconDecision(state.Task, "exploit")
                : TacticalGrandTacticianReconDoctrine.Decide(
                    new TacticalGrandTacticianReconInput(state.Role, state.Task, visibleEnemy, underRecentFire: closeEngaged,
                        sideDefending: defender));
            if (recon.Task != state.Task)
            {
                state = state.WithRoleAndTask(state.Role, recon.Task);
                if (hasOrder) order = order.WithTask(recon.Task, recon.Reason);
            }

            // BC14W: the cavalry follow turns a task into an attack on the nearest enemy; a group the works hold guard holds is left a hold.
            if (hasCavalry && !probe && !exploit && !WorksHoldGuard.Holds(state.Task, state.Reason) &&
                TryCavalryFollow(s, t, i, state, flankRisk, out CommandTaskType cavalryTask, out string cavalryReason))
            {
                state = state.WithRoleAndTask(state.Role, cavalryTask);
                if (hasOrder) order = order.WithTask(cavalryTask, cavalryReason);
            }

            CommandPhysicalState physical = PostureSense.Physical(rec, t.Moving[i]);
            TacticalIdleClassification idle = TacticalCommandMonitor.ClassifyIdle(state, physical);
            CommandTaskType emergency = CommandFormationCorrection.TaskForLocalFlankEmergency(state.Task, closeEngaged, flankRisk);
            if (emergency != state.Task)
            {
                state = state.WithRoleAndTask(state.Role, emergency);
                // The posture pipeline's local flank emergency rewrote an exploit's task: the opportunity producer reads this on its
                // next tick and aborts the exploit as flank-threat (one tick late by design: the table has one writer).
                if (exploit && recordTick) ExploitFeedback.SetFlankEmergency(rec.InstanceId);
            }

            // Army opportunity override at the brigade-posture layer (2026-05-21),
            // with Main as the role (brigades carry no DirectChildRole).
            ReinforcementOpportunity opportunity = ReinforcementOpportunity.NoOpportunity;
            try { if (army != null) opportunity = army.CurrentReinforcementOpportunity.Opportunity; }
            catch { /* NoOpportunity keeps the task */ }
            string overrideReason = null;
            CommandTaskType overridden = exploit
                ? state.Task
                : TacticalOpportunityTaskOverride.Override(state.Task, DirectChildRole.Main, opportunity, WorksHoldGuard.Holds(state.Task, state.Reason), out overrideReason);
            if (overridden != state.Task && overrideReason != null)
            {
                OnceLog.Info("tactical-brigade-posture:opportunity-override",
                    "[TacticalBrigadePosture] opportunity override: " + state.Task + " -> " + overridden
                    + " (opportunity=" + opportunity + " reason=" + overrideReason + ")");
                state = state.WithRoleAndTask(state.Role, overridden);
                if (hasOrder) order = order.WithTask(overridden, overrideReason);
            }

            // BC9 stance guard, after every rewrite above (GT recon, cavalry follow, local
            // emergency, opportunity): a Defender gets no Probe and no attack task.
            PostureStanceGuard.Apply(stance, ref state, hasOrder, ref order);

            // BC14W (requirement-works-hold): a group the works hold guard holds gets the hold's one stance (the mapper's, 2, flagged WorksHold) before
            // the posture decision and whatever it returns, so an idle group already in formation is still held out of vanilla's assault. A former
            // fix whose lane lies outside the works band still goes to its lane (its decision is the fix's) and holds and fires there.
            bool worksHeld = WorksHoldGuard.Holds(state.Task, state.Reason) || (hasOrder && WorksHoldGuard.Holds(order.Task, order.Reason));
            int worksStance = worksHeld && recordTick
                ? PostureIntentPlan.SubmitWorksHoldStance(sink, rec.InstanceId, priority, source, ExploitPosture.IntentExploitId(exploit, hasOrder, order.ExploitId))
                : 0;
            bool worksLane = worksHeld && hasOrder && WorksHoldGuard.LaneOpen(in order, army?.Feasibility?.HeldWorks);

            // BC9: a probe that ended short of a superior enemy holds the ground it gained
            // while the ledger keeps the task it probed under (BattleRecon.ProbeTick).
            float holdX = 0f, holdZ = 0f, holdDirX = 0f, holdDirZ = 0f;
            bool holding = recordTick && !probe && !exploit && ReconIntentProducer.TryHold(s.Alliance, rec.InstanceId, state.Task,
                out holdX, out holdZ, out holdDirX, out holdDirZ);

            int targetFormation = rec.UnitTyp > TacticalUnitType.MaxCombat
                ? CommandFormationCorrection.TargetFormationForTask(state.Task, rec.Formation)
                : -1;
            bool orderPending = PostureSense.PendingOrder(rec, physical.ActiveMove, CanBypassStaleQueuedOrder(state.Task));
            bool alreadyCorrect = !physical.PathInterrupted &&
                (physical.ActiveMove ||
                 ((targetFormation < 0 || rec.Formation == targetFormation) &&
                  idle == TacticalIdleClassification.ValidIdle && CommandPostureExecutor.IdleCanSatisfyTask(worksLane ? CommandTaskType.FixEnemy : state.Task)));
            var eligibility = new WriteEligibilitySnapshot(
                modeAllowsWrites: true,
                playerProtected: physical.PlayerProtected,
                routed: physical.Routed,
                orderPending: orderPending,
                recentOrder: false,
                alreadyDoingCorrectTask: alreadyCorrect,
                atAssignedLocation: idle == TacticalIdleClassification.ValidIdle,
                missingLedgerAssignment: false,
                closeEngaged: closeEngaged);

            PostureExecutionDecision decision = hasOrder
                ? CommandPostureExecutor.Decide(order, physical, eligibility, (float)s.DoctrineHours, worksLane)
                : CommandPostureExecutor.Decide(state, physical, eligibility);

            if (recordTick)
            {
                ref GroupPosture g = ref t.Groups[i];
                g.Has = true;
                g.State = state;
                g.CloseEngaged = closeEngaged;
                g.FlankRisk = flankRisk;
            }

            // BC11 decision 2: the refused flank. A group already doing its task (idle, formation right) used to
            // return here, so a refused flank never left the posture executor; a flank that is due is its own write.
            // A refuse already ordered on that side is not re-ordered (the executor keeps the last issued refuse).
            RefuseFlank refuse = PostureSense.RefuseFor(s, i, state.Task, closeEngaged, flankRisk);
            OrderExecutor refuseExecutor = OrderExecutor.ForAlliance(s.Alliance);
            bool refuseDue = refuse != RefuseFlank.None && rec.UnitTyp > TacticalUnitType.MaxCombat &&
                (refuseExecutor == null || refuseExecutor.LastIssuedRefuse(rec.InstanceId) != refuse);

            if (decision.Action == PostureExecutionAction.NoWrite)
            {
                if (refuseDue && PostureSense.RefusePromotesNoWrite(decision.Reason))
                {
                    decision = new PostureExecutionDecision(PostureExecutionAction.SetFormation, PostureSense.RefusePromotedReason,
                        PostureExecutionTarget.CurrentPosition);
                }
                else
                {
                    PostureRoutes.Forget(rec.InstanceId);
                    PostureTelemetry.Group(s, i, reason, state, decision, worksStance, decision.Reason);
                    return worksStance > 0;
                }
            }

            bool hasTarget = PostureTargets.TryResolve(s, t, i, side, state, order, decision.Target, out float x, out float z);
            if (!hasTarget && t.NavDeferred)
            {
                PostureTelemetry.Group(s, i, reason, state, decision, worksStance, "nav-budget-deferred");
                return worksStance > 0;
            }
            string targetReason = hasTarget ? decision.Reason : "target-unresolved";
            if (hasTarget && holding && BattleRecon.HoldBlocks(decision.Target, holdX, holdZ, holdDirX, holdDirZ, x, z))
            {
                // No move back over the probe's ground: the move intents need a target.
                hasTarget = false;
                targetReason = BattleRecon.HoldReason;
            }

            // BC10 road march: a long move through woods or swamp goes over a road when that is
            // clearly faster, after the group takes march column; the route and the group's
            // formation target are this pipeline's, so the arbiter sees one Formation intent.
            RoadRoute roadRoute = default(RoadRoute);
            bool columnWait = false;
            // BC14: a maneuvering wing's order carries the crossing it is routed through; that via replaces the road route and the
            // covered-crossing avoidance, neither of which can carry it.
            float maneuverViaX = 0f, maneuverViaZ = 0f;
            bool maneuverVia = hasTarget && hasOrder &&
                PostureIntentPlan.TryManeuverVia(in order, state.Task, exploit, decision.Target, out maneuverViaX, out maneuverViaZ);
            PostureIntentPlan.RouteFlagsFor(state.Task, maneuverVia, out bool roadRouteAllowed, out bool coveredAvoidanceAllowed);
            if (hasTarget && roadRouteAllowed && rec.UnitTyp > TacticalUnitType.MaxCombat && !closeEngaged && PostureRoutes.Applies(state.Task, decision.Action))
            {
                bool avoidCovered = PostureIntentPlan.AvoidsCoveredCrossings(state.Task, rec.X, rec.Z, x, z);
                if (PostureRoutes.Resolve(s, t, i, x, z, avoidCovered, out roadRoute, out columnWait) == PostureRoutes.Outcome.Deferred)
                {
                    PostureTelemetry.Group(s, i, reason, state, decision, worksStance, "road-route-deferred");
                    return worksStance > 0;
                }
                if (roadRoute.Use || columnWait) targetFormation = RoadRoutePlanner.ColumnGroupFormation;
                if (columnWait) targetReason = "road-column-wait";
                else if (roadRoute.Use) targetReason = "road-route";
            }

            // #61 SetFormation: threat facing for holding tasks and the refused
            // flank in contact, on the formation channels. BC11 decision 1: the facing is the strongest
            // perceived pressure on the front (FlankFacing), the closest visible enemy as before without it.
            float heading = -1f;
            bool hasThreatHeading = false;
            bool needsFacing = false;
            string facingSource = PostureTargets.NoFacingSource;
            if (CommandFormationCorrection.ShouldFaceThreat(state.Task) &&
                PostureTargets.TryFacingBearing(s, i, rec.RotationY, out heading, out facingSource))
            {
                hasThreatHeading = heading >= 0f;
                needsFacing = CommandFormationCorrection.NeedsFacingCorrection(rec.RotationY, heading, PostureSense.FacingToleranceDegrees);
            }

            var request = new PostureIntentRequest
            {
                GroupId = rec.InstanceId,
                Action = decision.Action,
                Task = state.Task,
                HasTarget = hasTarget,
                TargetX = x,
                TargetZ = z,
                TargetFormation = targetFormation,
                NeedsFormation = CommandFormationCorrection.NeedsCorrection(-1, -1, rec.Formation, targetFormation),
                InContactWithdrawal = closeEngaged || flankRisk,
                AvoidCoveredCrossings = hasTarget && coveredAvoidanceAllowed && PostureIntentPlan.AvoidsCoveredCrossings(state.Task, rec.X, rec.Z, x, z),
                Exploit = exploit,
                ExploitId = ExploitPosture.IntentExploitId(exploit, hasOrder, order.ExploitId),
                Refuse = refuseDue ? refuse : RefuseFlank.None,
                // #61 wheeled with the threat heading whenever it had one (not only
                // when the facing was off): a refuse-only wheel faces the threat.
                HasThreatHeading = hasThreatHeading,
                ThreatHeading = heading,
                Priority = priority,
                Source = source,
                Reason = reason,
                ViaCount = roadRoute.Use ? roadRoute.ViaCount : (maneuverVia ? 1 : 0),
                RoadRoute = roadRoute.Use,
                RoadLinkCount = roadRoute.Use ? roadRoute.LinkCount : 0,
                RoadLink0 = roadRoute.Link0,
                RoadLink1 = roadRoute.Link1,
                ViaX0 = roadRoute.Use ? roadRoute.ViaX0 : maneuverViaX,
                ViaZ0 = roadRoute.Use ? roadRoute.ViaZ0 : maneuverViaZ,
                ViaX1 = roadRoute.ViaX1,
                ViaZ1 = roadRoute.ViaZ1,
                ColumnWait = columnWait,
                StanceSubmitted = worksStance > 0,
            };
            int submitted = worksStance + PostureIntentPlan.Submit(sink, request);
            // BC11 decision 4: an EnvelopmentManeuver Turn faces the group at the enemy line end (the executor
            // wheels it out of combat, rotates its regiments in place in combat).
            if (hasOrder && order.ManeuverHeading >= 0f && rec.UnitTyp > TacticalUnitType.MaxCombat && !physical.ActiveMove &&
                CommandFormationCorrection.NeedsFacingCorrection(rec.RotationY, order.ManeuverHeading, PostureSense.FacingToleranceDegrees))
                submitted += PostureIntentPlan.SubmitFacing(sink, s, i, descendants, order.ManeuverHeading, state.Task, RefuseFlank.None,
                    priority, source, ManeuverFacingReason, t.RegimentQueue, t.Regiments);
            bool shapes = decision.Action == PostureExecutionAction.SetFormation ||
                (decision.Action == PostureExecutionAction.SetFormationAndWaypoint && hasTarget);
            // The facing keeps the refuse that is in force (not only a due one): a facing wheel re-lays every child, and a
            // wheel without the skew would undo the refused flank the formation intent just ordered or that is already issued.
            if (shapes && needsFacing && rec.UnitTyp > TacticalUnitType.MaxCombat)
                submitted += PostureIntentPlan.SubmitFacing(sink, s, i, descendants, heading, state.Task, refuse,
                    priority, source, reason, t.RegimentQueue, t.Regiments);
            PostureTelemetry.Group(s, i, reason, state, decision, submitted, targetReason, facingSource);
            return submitted > 0;
        }

        // #61 CanBypassStaleQueuedOrder: movement tasks may pass a stalled queued order.
        private static bool CanBypassStaleQueuedOrder(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.Scout:
                case CommandTaskType.Probe:
                case CommandTaskType.Screen:
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.ReleaseReserve:
                    return true;
                default:
                    return false;
            }
        }

        // #61 TryApplyCavalryFollowMode over the snapshot. The follow target is the
        // closest visible enemy; fort and close-support are read from that visible
        // enemy's own record and its visible neighbours.
        private static bool TryCavalryFollow(BattleSnapshot s, PostureTick.State t, int i, CommandNodeOperationalState state,
            bool flankRisk, out CommandTaskType task, out string reason)
        {
            task = state.Task;
            reason = null;
            TacticalCavalryFollowMode mode = CavalryFollowModeFor(state.Role, state.Task);
            if (mode == TacticalCavalryFollowMode.None) return false;

            int target = s.ClosestVisibleEnemy(i, out float distance);
            EnemyObservation[] obs = s.Observations;
            bool hasTarget = target >= 0;
            float fear = Mathf.Clamp01(s.Own[i].Morale + (flankRisk ? -0.25f : 0f));
            // The target is the closest VISIBLE enemy, so it is never hidden; its square
            // and its kind are read from its own record (BC6b precedent).
            TacticalCavalryFollowDecision follow = TacticalCavalryFollowDoctrine.Decide(new TacticalCavalryFollowInput(
                mode,
                hasFollowTarget: hasTarget,
                targetIsOfficer: hasTarget && BattleRecon.IsOfficerTarget(obs[target].UnitTyp),
                targetInFort: hasTarget && TargetGarrisoned(s, t, obs[target].InstanceId),
                targetInSquare: hasTarget && TargetInSquare(s, t, obs[target].InstanceId),
                targetHasInfantryOrArtillerySupport: hasTarget && TargetHasCloseSupport(s, target),
                canChargeTarget: hasTarget && distance <= 350f,
                fearAdvantage01: fear,
                enemyClose: hasTarget && distance > 0f && distance <= 350f,
                enemyDistance: hasTarget ? distance : 0f,
                longRange: 300f));

            switch (follow.Action)
            {
                case TacticalCavalryFollowAction.GetAway: task = CommandTaskType.FallBackToLine; break;
                case TacticalCavalryFollowAction.ChargeRaidTarget: task = CommandTaskType.AttackObjective; break;
                case TacticalCavalryFollowAction.MoveBehindTarget: task = CommandTaskType.GuardFlank; break;
                case TacticalCavalryFollowAction.RequestScoutLocation:
                    task = mode == TacticalCavalryFollowMode.Screen ? CommandTaskType.Screen : CommandTaskType.Scout;
                    break;
                case TacticalCavalryFollowAction.ClearFollowTarget: task = CommandTaskType.Screen; break;
                default: return false;
            }
            reason = follow.Reason;
            return task != state.Task;
        }

        private static bool TargetGarrisoned(BattleSnapshot s, PostureTick.State t, int enemyId)
        {
            return t.EnemyIndexById.TryGetValue(enemyId, out int e) && (s.EnemyRecords[e].Flags & UnitFlags.Garrisoned) != 0;
        }

        // Regiment formation 4 is the square (118174).
        private static bool TargetInSquare(BattleSnapshot s, PostureTick.State t, int enemyId)
        {
            return t.EnemyIndexById.TryGetValue(enemyId, out int e) &&
                BattleRecon.IsSquare(s.EnemyRecords[e].UnitTyp, s.EnemyRecords[e].Formation);
        }

        // A visible infantry or artillery enemy within close contact of the target.
        private static bool TargetHasCloseSupport(BattleSnapshot s, int target)
        {
            EnemyObservation[] obs = s.Observations;
            float r2 = PostureSense.CloseContactMeters * PostureSense.CloseContactMeters;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (k == target || obs[k].State != ObservationState.Visible || obs[k].IsReinforcement) continue;
                if (obs[k].UnitTyp != TacticalUnitType.Infantry && obs[k].UnitTyp != TacticalUnitType.Artillery) continue;
                float dx = obs[k].X - obs[target].X;
                float dz = obs[k].Z - obs[target].Z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        private static TacticalCavalryFollowMode CavalryFollowModeFor(CommandNodeRole role, CommandTaskType task)
        {
            if (task == CommandTaskType.GuardFlank || role == CommandNodeRole.Reserve) return TacticalCavalryFollowMode.Guard;
            if (task == CommandTaskType.Screen || role == CommandNodeRole.ScreeningForce) return TacticalCavalryFollowMode.Screen;
            if (task == CommandTaskType.Scout || task == CommandTaskType.Probe || role == CommandNodeRole.Probe) return TacticalCavalryFollowMode.Scout;
            if (task == CommandTaskType.AttackObjective || task == CommandTaskType.SupportAttack) return TacticalCavalryFollowMode.Raid;
            return TacticalCavalryFollowMode.None;
        }

    }

    // Road march routing glue (BC10 decision 6): plans a route for a posture Move, caches it,
    // and decides between the routed move, the column wait and the unrouted move
    // (RoadRoutePlanner.DecideColumn). The planner is pure; this holds the NavMesh path of
    // vanilla's own route, the per-side budget and the per-group latches.
    internal static class PostureRoutes
    {
        static PostureRoutes()
        {
            BattleScope.Register("posture-routes", Reset);
        }

        internal enum Outcome { NoRoute, Routed, ColumnWait, Deferred }

        private const float TargetBucketMeters = 50f;

        private static readonly RoutePlanCache _cache = new RoutePlanCache();
        // The doctrine hour each group first waited for march column.
        private static readonly Dictionary<int, double> _columnSince = new Dictionary<int, double>(64);
        private static float[] _pathX = new float[64];
        private static float[] _pathZ = new float[64];
        private static bool _firstLogged;

        // Movement tasks that march a long way to an objective (plan decision 6); fallbacks,
        // probes and scouts keep vanilla's pathing.
        internal static bool Applies(CommandTaskType task, PostureExecutionAction action)
        {
            if (Plugin.EnableDeploymentAndMarchDoctrine == null || !Plugin.EnableDeploymentAndMarchDoctrine.Value) return false;
            switch (action)
            {
                case PostureExecutionAction.SetWaypoint:
                case PostureExecutionAction.SetFormationAndWaypoint:
                case PostureExecutionAction.ReleaseReserve:
                    break;
                default:
                    return false;
            }
            switch (task)
            {
                case CommandTaskType.AdvanceToAssembly:
                case CommandTaskType.FormUp:
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.ReleaseReserve:
                    return true;
                default:
                    return false;
            }
        }

        // The move (x, z) of group i: its route (Use only when the group is in march column) and
        // whether it waits for the column. Deferred when a plan is wanted but this side's budget
        // for the snapshot is spent: the move is held one tick rather than sent unrouted.
        internal static Outcome Resolve(BattleSnapshot s, PostureTick.State t, int i, float x, float z, bool avoidCovered,
            out RoadRoute route, out bool columnWait)
        {
            route = default(RoadRoute);
            columnWait = false;
            ref UnitRecord rec = ref s.Own[i];
            // Safety before speed: a move around covered crossings takes BlockedMovingOrder, which has no via points.
            if (avoidCovered && OrderExecutorRuntime.CoveredPointCount(s) > 0)
            {
                Forget(rec.InstanceId);
                return Outcome.NoRoute;
            }

            BattleTerrainGrid grid = BattleTerrainGridRuntime.Get();
            if (!RoadRoutePlanner.Wanted(grid, rec.X, rec.Z, x, z))
            {
                Forget(rec.InstanceId);
                return Outcome.NoRoute;
            }
            if (!TryPlan(s, t, grid, i, x, z, out RoadRoute planned)) return Outcome.Deferred;
            if (!planned.Use)
            {
                Forget(rec.InstanceId);
                return Outcome.NoRoute;
            }

            double waited = 0.0;
            if (rec.Formation != RoadRoutePlanner.ColumnGroupFormation)
            {
                if (!_columnSince.TryGetValue(rec.InstanceId, out double since)) _columnSince[rec.InstanceId] = since = s.DoctrineHours;
                waited = Math.Max(0.0, s.DoctrineHours - since);
            }
            else
            {
                _columnSince.Remove(rec.InstanceId);
            }

            switch (RoadRoutePlanner.DecideColumn(true, rec.Formation, waited))
            {
                case ColumnDecision.Routed:
                    route = planned;
                    LogFirst(planned);
                    return Outcome.Routed;
                case ColumnDecision.Wait:
                    columnWait = true;
                    return Outcome.ColumnWait;
                default:
                    return Outcome.NoRoute; // TimedOutUnrouted: the latch stays so the wait is not restarted
            }
        }

        // A reserve release is a short courier move: it takes via points only when the group is
        // already in march column (the posture pipeline owns the group's formation).
        internal static void AttachIfInColumn(BattleSnapshot s, int i, ref BattleOrderIntent move)
        {
            if (Plugin.EnableDeploymentAndMarchDoctrine == null || !Plugin.EnableDeploymentAndMarchDoctrine.Value) return;
            if (s.Own[i].Formation != RoadRoutePlanner.ColumnGroupFormation) return;
            PostureTick.State t = PostureTick.For(s);
            BattleTerrainGrid grid = BattleTerrainGridRuntime.Get();
            if (t == null || !RoadRoutePlanner.Wanted(grid, s.Own[i].X, s.Own[i].Z, move.TargetX, move.TargetZ)) return;
            if (!TryPlan(s, t, grid, i, move.TargetX, move.TargetZ, out RoadRoute planned) || !planned.Use) return;
            move.ViaCount = planned.ViaCount;
            move.Flags |= IntentFlags.RoadRoute;
            move.RoadLinkCount = planned.LinkCount;
            move.RoadLink0 = planned.Link0;
            move.RoadLink1 = planned.Link1;
            move.ViaX0 = planned.ViaX0;
            move.ViaZ0 = planned.ViaZ0;
            move.ViaX1 = planned.ViaX1;
            move.ViaZ1 = planned.ViaZ1;
            LogFirst(planned);
        }

        internal static void Forget(int groupId)
        {
            _columnSince.Remove(groupId);
        }

        // The cached plan for (group, 50 m target bucket) while it is under 0.25 doctrine-h old, else a
        // new one if this side's budget allows. False = deferred.
        private static bool TryPlan(BattleSnapshot s, PostureTick.State t, BattleTerrainGrid grid, int i, float x, float z, out RoadRoute route)
        {
            ref UnitRecord rec = ref s.Own[i];
            int bx = (int)Math.Floor(x / TargetBucketMeters);
            int bz = (int)Math.Floor(z / TargetBucketMeters);
            long key = ((long)rec.InstanceId << 32) ^ (uint)(((bx & 0xFFFF) << 16) | (bz & 0xFFFF));
            double now = s.DoctrineHours;
            if (_cache.TryGet(key, now, out route)) return true;
            if (!t.RouteBudget.TryConsume())
            {
                route = default(RoadRoute);
                return false;
            }

            route = default(RoadRoute);
            using (TelemetryPerf.Scope("tactical.producer.posture.road-route", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    PostureTargets.EnsureNavFilter(t);
                    NavMeshPath path = PostureTargets.SharedPath();
                    var from = new Vector3(rec.X, rec.Y, rec.Z);
                    Vector3 to = PostureTargets.AtTerrain(x, z);
                    if (NavMesh.CalculatePath(from, to, t.NavFilter, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        int count = PostureTargets.ReadCorners(path, out Vector3[] corners);
                        if (_pathX.Length < count)
                        {
                            _pathX = new float[count * 2];
                            _pathZ = new float[count * 2];
                        }
                        for (int c = 0; c < count; c++)
                        {
                            _pathX[c] = corners[c].x;
                            _pathZ[c] = corners[c].z;
                        }
                        route = RoadRoutePlanner.Plan(grid, rec.X, rec.Z, x, z, _pathX, _pathZ, count, t.RouteScratch, FeasibilityRuntime.CrossingPicture(s.Alliance));
                    }
                }
                catch (Exception ex)
                {
                    route = default(RoadRoute);
                    OnceLog.Warning("tactical-producer-posture:road-route-failed",
                        "[PostureProducer] road route plan failed, the move goes unrouted: " + ex.GetType().Name + " " + ex.Message);
                }
            }
            _cache.Put(key, in route, now);
            return true;
        }

        private static void LogFirst(in RoadRoute route)
        {
            if (_firstLogged) return;
            _firstLogged = true;
            float ratio = route.VanillaCost > 0f ? route.RouteCost / route.VanillaCost : 0f;
            OnceLog.Info("tactical-battle-core:road-route-first-fire",
                "[OrderExecutor] first road march: via=" + route.ViaCount + " costRatio=" + ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " column=true");
        }

        private static void Reset()
        {
            _cache.Clear();
            _columnSince.Clear();
            _firstLogged = false;
        }
    }

    // #61 TryResolveTarget and the NavMesh plan, over the snapshot.
    internal static class PostureTargets
    {
        private const float ObjectiveApproachStandOff = 75f;
        private const float AssemblyStandOff = 200f;
        private const float ReserveStandOff = 350f;
        private const float FallbackStandOff = 250f;

        private static FieldInfo _bfsField;
        private static bool _bfsResolved;

        // Nav probes (perf slice F5), main thread only: one reused NavMeshPath (it owns a
        // native handle and a finalizer) read with GetCornersNonAlloc, the three samples
        // TacticalNavMeshPlanner.Plan reads during the call (it keeps none), and the area
        // indices, which are fixed project settings, resolved once.
        private static NavMeshPath _navPath;

        // One reused path for every probe and every route plan (single-threaded, consumed at once).
        internal static NavMeshPath SharedPath()
        {
            return _navPath ?? (_navPath = new NavMeshPath());
        }

        // Every corner of the path into one reused buffer: a full buffer may have cut the list short,
        // so grow and read again.
        internal static int ReadCorners(NavMeshPath path, out Vector3[] corners)
        {
            int count = path.GetCornersNonAlloc(_corners);
            while (count == _corners.Length)
            {
                _corners = new Vector3[_corners.Length * 2];
                count = path.GetCornersNonAlloc(_corners);
            }
            corners = _corners;
            return count;
        }
        private static Vector3[] _corners = new Vector3[32];
        private static readonly TacticalPathQualitySample[] _samples = new TacticalPathQualitySample[3];
        private static int[] _areaIndex;

        internal static bool TryResolve(BattleSnapshot s, PostureTick.State t, int i, TacticalBattleOrchestrator side,
            CommandNodeOperationalState state, CommandDoctrineOrder order, PostureExecutionTarget kind, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            t.NavDeferred = false;
            // BC11 decision 4: an EnvelopmentManeuver hold stands where the child is; no target to plan a route to,
            // so it burns no nav plan.
            if (kind == PostureExecutionTarget.DoctrinePrimaryTarget && order.ManeuverHold) return false;
            // BC9: a Scout or Probe task's forward target is where the recon planner looks
            // (its probe nomination, else BattleRecon.LookPoint), not an objective
            // stand-off; a break-off or recovery keeps its own target.
            if (BattleRecon.UsesLookPoint(state.Task, kind) && ReconIntentProducer.TryLookTarget(s, i, out x, out z))
                return PostureSense.IsMoveAway(s.Own[i], x, z);
            bool resolved;
            switch (kind)
            {
                case PostureExecutionTarget.DoctrinePrimaryTarget:
                    resolved = TryPoint(order.PrimaryTarget, out x, out z) || TryObjectiveApproach(s, i, side, ObjectiveApproachStandOff, out x, out z);
                    break;
                case PostureExecutionTarget.DoctrineSupportTarget:
                    resolved = TryPoint(order.SupportTarget, out x, out z) || TryObjectiveApproach(s, i, side, ObjectiveApproachStandOff, out x, out z);
                    break;
                case PostureExecutionTarget.DoctrineFallbackTarget:
                    resolved = TryPoint(order.FallbackTarget, out x, out z) || TryFallbackFromObjective(s, i, side, out x, out z);
                    break;
                case PostureExecutionTarget.ObjectiveApproach:
                case PostureExecutionTarget.ReleasePoint:
                    resolved = TryObjectiveApproach(s, i, side, ObjectiveApproachStandOff, out x, out z);
                    break;
                case PostureExecutionTarget.AssemblyArea:
                    resolved = TryObjectiveApproach(s, i, side, AssemblyStandOff, out x, out z);
                    break;
                case PostureExecutionTarget.FallbackLine:
                    resolved = TryFallbackFromObjective(s, i, side, out x, out z);
                    break;
                case PostureExecutionTarget.RecoveryPath:
                    resolved = TryRecoveryPath(s, i, side, out x, out z);
                    break;
                case PostureExecutionTarget.ReserveArea:
                    resolved = TryObjectiveApproach(s, i, side, ReserveStandOff, out x, out z);
                    break;
                default:
                    return false;
            }

            if (!resolved) return false;
            if ((kind == PostureExecutionTarget.DoctrinePrimaryTarget ||
                 kind == PostureExecutionTarget.DoctrineSupportTarget ||
                 kind == PostureExecutionTarget.DoctrineFallbackTarget) &&
                !TryNavPlan(s, t, i, state, order, kind, ref x, ref z))
                return false;
            return PostureSense.IsMoveAway(s.Own[i], x, z);
        }

        internal const string NoFacingSource = "none";
        internal const string PressureFacingSource = "pressure";
        internal const string ClosestFacingSource = "closest";

        /// <summary>
        /// BC11 decision 1: the bearing a holding group faces. The strongest perceived pressure within the slice
        /// range (FlankFacing.MostPressuredBearing, hysteresis on the group's current facing) with Enable Flank
        /// Response And Envelopment on, else the closest visible enemy (PostureTargets.TryThreat, as before).
        /// <paramref name="source"/> is the telemetry facingSource: pressure, closest or none.
        /// </summary>
        internal static bool TryFacingBearing(BattleSnapshot s, int i, float currentFacing, out float bearing, out string source)
        {
            bearing = -1f;
            source = NoFacingSource;
            if (s == null || i < 0 || i >= s.OwnCount) return false;
            if (Plugin.FlankResponseEnabled() && FlankFacing.MostPressuredBearing(s, i, currentFacing, out bearing, out _))
            {
                source = PressureFacingSource;
                return true;
            }
            if (!TryThreat(s, i, out float x, out float z)) return false;
            bearing = PostureSense.HeadingTo(s.Own[i].X, s.Own[i].Z, x, z);
            source = ClosestFacingSource;
            return true;
        }

        internal static bool TryThreat(BattleSnapshot s, int i, out float x, out float z)
        {
            int k = s.ClosestVisibleEnemy(i, out _);
            if (k < 0) { x = 0f; z = 0f; return false; }
            x = s.Observations[k].X;
            z = s.Observations[k].Z;
            return true;
        }

        private static bool TryPoint(DoctrineTargetPoint point, out float x, out float z)
        {
            x = point.X;
            z = point.Z;
            return point.HasValue && PostureSense.HasPoint(x, z);
        }

        private static bool TryObjectiveApproach(BattleSnapshot s, int i, TacticalBattleOrchestrator side, float standOff, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (!TryPrimaryObjectivePoint(s, i, side, out float ox, out float oz)) return false;
            ref UnitRecord rec = ref s.Own[i];
            float distance = PostureSense.Distance(rec.X, rec.Z, ox, oz);
            if (distance < PostureSense.MinWaypointMeters || distance > PostureSense.MaxWaypointMeters) return false;
            float offset = Math.Min(Math.Max(standOff, 0f), Math.Max(0f, distance - PostureSense.MinWaypointMeters));
            x = ox + (rec.X - ox) / distance * offset;
            z = oz + (rec.Z - oz) / distance * offset;
            return true;
        }

        private static bool TryFallbackFromObjective(BattleSnapshot s, int i, TacticalBattleOrchestrator side, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            ref UnitRecord rec = ref s.Own[i];
            TacticalMapPoint? objective = TryPrimaryObjectivePoint(s, i, side, out float ox, out float oz)
                ? new TacticalMapPoint(ox, oz)
                : (TacticalMapPoint?)null;
            TacticalMapPoint? threat = TryThreat(s, i, out float tx, out float tz)
                ? new TacticalMapPoint(tx, tz)
                : (TacticalMapPoint?)null;
            if (!CommandFallbackTargetResolver.TryResolve(
                    new TacticalMapPoint(rec.X, rec.Z), objective, threat, FallbackStandOff,
                    PostureSense.MinWaypointMeters, PostureSense.MaxWaypointMeters, out TacticalMapPoint fallback, out _))
                return false;
            x = fallback.X;
            z = fallback.Z;
            return true;
        }

        // The group's last waypoint when it is a real move away, else the assembly approach.
        private static bool TryRecoveryPath(BattleSnapshot s, int i, TacticalBattleOrchestrator side, out float x, out float z)
        {
            ref UnitRecord rec = ref s.Own[i];
            if (PostureSense.HasPoint(rec.LastWaypointX, rec.LastWaypointZ) &&
                PostureSense.Distance(rec.X, rec.Z, rec.LastWaypointX, rec.LastWaypointZ) >= PostureSense.MinWaypointMeters)
            {
                x = rec.LastWaypointX;
                z = rec.LastWaypointZ;
                return true;
            }
            return TryObjectiveApproach(s, i, side, AssemblyStandOff, out x, out z);
        }

        // The operation's primary objective from the ledger, else its first
        // objective, else the group's last waypoint. (#61 first read vanilla's
        // Regiment.currentsetobjective, which the snapshot does not carry.)
        private static bool TryPrimaryObjectivePoint(BattleSnapshot s, int i, TacticalBattleOrchestrator side, out float x, out float z)
        {
            if (TryOperationObjective(side, out x, out z, out bool listed) || listed) return PostureSense.HasPoint(x, z);

            ref UnitRecord rec = ref s.Own[i];
            x = rec.LastWaypointX;
            z = rec.LastWaypointZ;
            return PostureSense.HasPoint(x, z) &&
                PostureSense.Distance(rec.X, rec.Z, x, z) >= PostureSense.MinWaypointMeters;
        }

        /// <summary>
        /// The operation's primary objective from the ledger, else its first objective.
        /// <paramref name="listed"/> is true when the ledger lists any objective.
        /// </summary>
        internal static bool TryOperationObjective(TacticalBattleOrchestrator side, out float x, out float z, out bool listed)
        {
            x = 0f;
            z = 0f;
            listed = false;
            var objectives = side?.OperationsLedger?.CurrentObjectives;
            if (objectives == null || objectives.Count == 0) return false;
            listed = true;
            string primary = side.Army != null ? side.Army.CurrentOperation.PrimaryObjectiveId : "objective-unknown";
            int pick = 0;
            for (int k = 0; k < objectives.Count; k++)
            {
                if (!string.Equals(objectives[k].Observation.ObjectiveId, primary, StringComparison.Ordinal)) continue;
                pick = k;
                break;
            }
            x = objectives[pick].Observation.Location.X;
            z = objectives[pick].Observation.Location.Z;
            return PostureSense.HasPoint(x, z);
        }

        private static bool TryNavPlan(BattleSnapshot s, PostureTick.State t, int i, CommandNodeOperationalState state,
            CommandDoctrineOrder order, PostureExecutionTarget kind, ref float x, ref float z)
        {
            if (!t.Nav.TryConsume())
            {
                t.NavDeferred = true;
                return false;
            }

            using (TelemetryPerf.Scope("tactical.producer.posture.nav-plan", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    ref UnitRecord rec = ref s.Own[i];
                    bool hasThreat = TryThreat(s, i, out float threatX, out float threatZ);
                    bool hasWaypoint = PostureSense.HasPoint(rec.LastWaypointX, rec.LastWaypointZ);
                    DoctrineTargetPoint primary = DoctrineTargetPoint.From(x, z);
                    DoctrineTargetPoint fallback = order.FallbackTarget;
                    if (kind == PostureExecutionTarget.DoctrineFallbackTarget)
                    {
                        primary = order.PrimaryTarget.HasValue ? order.PrimaryTarget : DoctrineTargetPoint.From(x, z);
                        fallback = DoctrineTargetPoint.From(x, z);
                    }

                    TacticalNavPlanDecision plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
                        state.Task, rec.X, rec.Z, primary, fallback,
                        hasThreat, threatX, threatZ,
                        hasWaypoint, rec.LastWaypointX, rec.LastWaypointZ,
                        PostureSense.CloseEngaged(s, i, t.Descendants[i]),
                        PostureSense.MinWaypointMeters, PostureSense.MaxWaypointMeters,
                        PathSamples(s, t, i, x, z, state.Task, hasThreat, threatX, threatZ, hasWaypoint)));
                    if (plan.HasTarget && PostureSense.HasPoint(plan.Target.X, plan.Target.Z))
                    {
                        x = plan.Target.X;
                        z = plan.Target.Z;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("tactical-producer-posture:nav-plan-failed",
                        "[PostureProducer] nav plan failed, using the resolved target: " + ex.GetType().Name + " " + ex.Message);
                    return true;
                }
            }
        }

        private static TacticalPathQualitySample[] PathSamples(BattleSnapshot s, PostureTick.State t, int i, float tx, float tz,
            CommandTaskType task, bool hasThreat, float threatX, float threatZ, bool hasWaypoint)
        {
            ref UnitRecord rec = ref s.Own[i];
            var own = new Vector3(rec.X, rec.Y, rec.Z);
            var target = new Vector3(tx, 0f, tz);
            Vector3 direction = target - own;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1f) return Array.Empty<TacticalPathQualitySample>();
            direction.Normalize();
            var lateral = new Vector3(-direction.z, 0f, direction.x);
            var threat = new Vector3(threatX, 0f, threatZ);
            var waypoint = new Vector3(rec.LastWaypointX, 0f, rec.LastWaypointZ);
            EnsureNavFilter(t);
            BattleTerrainGrid grid = BattleTerrainGridRuntime.Get();
            _samples[0] = Sample(t, grid, rec.InstanceId, own, target, task, hasThreat, threat, hasWaypoint, waypoint);
            _samples[1] = Sample(t, grid, rec.InstanceId, own, target + lateral * 120f, task, hasThreat, threat, hasWaypoint, waypoint);
            _samples[2] = Sample(t, grid, rec.InstanceId, own, target - lateral * 120f, task, hasThreat, threat, hasWaypoint, waypoint);
            return _samples;
        }

        // R5 / BC10 decision 4: AddPath's battle costs (roads, bridges, fences, rivers) for the
        // probe's pathfinding, on a query filter instead of the global NavMesh area costs. A
        // fresh filter prices every area at 1, so every area's live cost is copied first
        // (areaMask all areas, agent type 0: the overload vanilla's CalculatePath uses).
        internal static void EnsureNavFilter(PostureTick.State t)
        {
            if (t.NavFilterReady) return;
            t.NavFilterReady = true;
            t.NavFilter = CreateNavFilter();
        }

        // The query filter itself, for the probe above and for the envelopment march-leg route (PlayExecutorRuntime).
        internal static NavMeshQueryFilter CreateNavFilter()
        {
            var filter = new NavMeshQueryFilter { areaMask = NavMesh.AllAreas, agentTypeID = 0 };
            try
            {
                bool frozen = GameVars.isfrozen();
                for (int a = 0; a < NavAreaCount; a++) filter.SetAreaCost(a, NavMesh.GetAreaCost(a));
                if (_areaIndex == null)
                {
                    var areas = new int[PostureNavAreaCosts.AreaNames.Length];
                    for (int a = 0; a < areas.Length; a++) areas[a] = NavMesh.GetAreaFromName(PostureNavAreaCosts.AreaNames[a]);
                    _areaIndex = areas;
                }
                for (int a = 0; a < _areaIndex.Length; a++)
                {
                    int area = _areaIndex[a];
                    if (area >= 0 && area < NavAreaCount) filter.SetAreaCost(area, PostureNavAreaCosts.Cost(a, frozen));
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-producer-posture:nav-filter-failed",
                    "[PostureProducer] NavMesh query filter costs failed, the probe prices every area at 1: " + ex.GetType().Name + " " + ex.Message);
            }
            return filter;
        }

        private const int NavAreaCount = 32; // NavMeshQueryFilter's cost table size

        private static TacticalPathQualitySample Sample(PostureTick.State t, BattleTerrainGrid grid, int selfId, Vector3 own, Vector3 candidate,
            CommandTaskType task, bool hasThreat, Vector3 threat, bool hasWaypoint, Vector3 waypoint)
        {
            float slopeCost = 0f, terrainRisk = 0f, bridgeRisk = 0f, congestion = 0f, roadPreference = 0f;
            float deadGround = 0f, threatExposure = 0f, routeContinuity = 0f, reservationPressure = 0f;
            float fallbackLaneConflict = 0f, artilleryDanger = 0f;
            try
            {
                NavMeshPath path = SharedPath();
                bool found = NavMesh.CalculatePath(own, AtTerrain(candidate.x, candidate.z), t.NavFilter, path);
                int count = ReadCorners(path, out Vector3[] corners);
                if (!found || path.status != NavMeshPathStatus.PathComplete || count <= 0)
                {
                    terrainRisk = 1f;
                }
                else
                {
                    congestion = Math.Min(1f, Math.Max(0f, (count - 4) * 0.12f));
                    float previousHeight = SampleHeight(own);
                    for (int c = 0; c < count; c++)
                    {
                        float height = SampleHeight(corners[c]);
                        slopeCost = Math.Max(slopeCost, Math.Min(1f, Math.Abs(height - previousHeight) / 35f));
                        previousHeight = height;
                    }
                    // BC10 decision 5: what the polyline walks on, sampled every 10 m on the grid.
                    PathTerrainScore terrain = ScoreCorners(grid, corners, count);
                    roadPreference = terrain.RoadShare;
                    terrainRisk = terrain.TerrainRisk;
                    bridgeRisk = terrain.BridgeRisk;
                }

                if (hasThreat)
                {
                    float distance = Flat(candidate, threat);
                    deadGround = PathTerrainScorer.DeadGround(hasThreat, TerrainBlocksSight(threat, candidate));
                    threatExposure = distance <= 100f ? 1f : Math.Max(0f, 1f - ((distance - 100f) / 350f));
                    artilleryDanger = threatExposure * (1f - deadGround) * 0.40f;
                    if (task == CommandTaskType.FallBackToLine)
                    {
                        float axisRisk = Math.Max(0f, 1f - (DistanceToSegment(candidate, threat, own) / 180f));
                        float closesThreat = distance < Flat(own, threat) ? 0.35f : 0f;
                        fallbackLaneConflict = Math.Max(axisRisk, closesThreat);
                    }
                }

                if (hasWaypoint) routeContinuity = Math.Max(0f, 1f - (Flat(candidate, waypoint) / 350f));
                reservationPressure = Math.Max(congestion, Math.Max(bridgeRisk * 0.75f, terrainRisk * 0.45f));
            }
            catch
            {
                terrainRisk = Math.Max(terrainRisk, 0.50f);
            }

            return new TacticalPathQualitySample(
                candidate.x, candidate.z, roadPreference, slopeCost, congestion, terrainRisk, bridgeRisk, deadGround,
                t.Friendly.EstimateFrontBlocker(selfId, own.x, own.z, candidate.x, candidate.z),
                threatExposure, routeContinuity, reservationPressure, fallbackLaneConflict, artilleryDanger);
        }

        private static float[] _cornerX = new float[64];
        private static float[] _cornerZ = new float[64];

        private static PathTerrainScore ScoreCorners(BattleTerrainGrid grid, Vector3[] corners, int count)
        {
            if (grid == null) return default(PathTerrainScore);
            if (_cornerX.Length < count)
            {
                _cornerX = new float[count * 2];
                _cornerZ = new float[count * 2];
            }
            for (int c = 0; c < count; c++)
            {
                _cornerX[c] = corners[c].x;
                _cornerZ[c] = corners[c].z;
            }
            return PathTerrainScorer.Score(grid, _cornerX, _cornerZ, count);
        }

        // Dead ground: a Map-layer raycast from the threat's eye to the candidate's eye, +4 m
        // each, as vanilla's own visibility paint casts (FogOfWar.DrawVisibility 101061-101063,
        // Physics.Raycast(startpos + 4 m, direction, range, LayerMask "Map")).
        private const float EyeHeightMeters = 4f;
        private static int _mapLayerMask;
        private static bool _mapLayerResolved;

        private static bool TerrainBlocksSight(Vector3 threat, Vector3 candidate)
        {
            if (!_mapLayerResolved)
            {
                _mapLayerResolved = true;
                try { _mapLayerMask = LayerMask.GetMask("Map"); }
                catch { _mapLayerMask = 0; }
            }
            if (_mapLayerMask == 0) return false;
            try
            {
                var from = new Vector3(threat.x, SampleHeight(threat) + EyeHeightMeters, threat.z);
                var to = new Vector3(candidate.x, SampleHeight(candidate) + EyeHeightMeters, candidate.z);
                Vector3 ray = to - from;
                float distance = ray.magnitude;
                return distance > 1f && Physics.Raycast(from, ray, distance, _mapLayerMask);
            }
            catch
            {
                return false;
            }
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            return PostureSense.Distance(a.x, a.z, b.x, b.z);
        }

        private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            float vx = end.x - start.x, vz = end.z - start.z;
            float len2 = vx * vx + vz * vz;
            if (len2 < 0.001f) return Flat(point, start);
            float u = Math.Max(0f, Math.Min(1f, ((point.x - start.x) * vx + (point.z - start.z) * vz) / len2));
            return PostureSense.Distance(point.x, point.z, start.x + u * vx, start.z + u * vz);
        }

        // A NavMesh query target at the terrain's height. CalculatePath snaps each end to the mesh within a small extent, so a
        // target built with y = 0 misses the mesh on any map with relief; vanilla paths from GetPosition() + (0, 1.5, 0) to waypoint
        // positions that carry the terrain height (AddPath 130259, GetTerrainHeight 27002).
        internal static Vector3 AtTerrain(float x, float z)
        {
            return new Vector3(x, SampleHeight(new Vector3(x, 0f, z)), z);
        }

        private static float SampleHeight(Vector3 point)
        {
            try
            {
                BattlefieldSetup bfs = Bfs();
                return bfs != null ? bfs.GetTerrainHeight(point) : point.y;
            }
            catch
            {
                return point.y;
            }
        }

        // BattleUnits keeps the scene's BattlefieldSetup in a private static (set at
        // 79055/79132, nulled on destroy at 79230).
        internal static BattlefieldSetup Bfs()
        {
            if (!_bfsResolved)
            {
                _bfsResolved = true;
                try { _bfsField = AccessTools.Field(typeof(BattleUnits), "bfs"); }
                catch { _bfsField = null; }
                if (_bfsField == null)
                    OnceLog.Warning("tactical-producer-posture:bfs-missing", "[PostureProducer] BattleUnits.bfs not found; path slope reads flat");
            }
            return _bfsField != null ? _bfsField.GetValue(null) as BattlefieldSetup : null;
        }
    }

    // Bounded decision telemetry (real-time throttle: telemetry, not doctrine).
    internal static class PostureTelemetry
    {
        static PostureTelemetry()
        {
            BattleScope.Register("posture-telemetry", Reset);
        }

        private const float TelemetrySeconds = 30f;
        private static readonly Dictionary<string, float> _lastAt = new Dictionary<string, float>();
        private static readonly RefuseRowThrottle _refuseRows = new RefuseRowThrottle();

        internal static string NameOf(BattleSnapshot s, int i)
        {
            var names = BattleSnapshotBuilder.NamesFor(s.Alliance);
            int id = s.Own[i].InstanceId;
            return names != null && names.TryGetValue(id, out string name) ? TacticalCurrentOrderSignature.Safe(name) : "#" + id;
        }

        internal static void Group(BattleSnapshot s, int i, string source, CommandNodeOperationalState state,
            PostureExecutionDecision decision, int submitted, string reason, string facingSource = null)
        {
            try
            {
                int id = s.Own[i].InstanceId;
                string signature = s.Alliance + "|" + id + "|" + state.Task + "|" + decision.Action + "|" + decision.Target
                    + "|" + reason + "|" + submitted;
                // Keyed per source: the posture and division-play passes decide the
                // same group with different tasks; one key would flip every tick.
                if (!TacticalTelemetry.ShouldEmit(_lastAt, "tactical-command-posture:" + source + ":" + s.Alliance + ":" + id, signature,
                        Time.realtimeSinceStartup, TelemetrySeconds, false))
                    return;
                string unit = NameOf(s, i);
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalCommandPosture", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(s.Alliance)
                        .WithUnit(unit)
                        .WithDecision(decision.Action.ToString(), reason, signature)
                        .WithField("source", source)
                        .WithField("node", TacticalOperationsTelemetry.SafeToken(state.NodeId))
                        .WithField("task", state.Task.ToString())
                        .WithField("selectedTarget", decision.Target.ToString())
                        .WithField("intents", submitted)
                        .WithField("facingSource", facingSource ?? PostureTargets.NoFacingSource)
                        .WithField("writeResult", submitted > 0 ? "submitted" : "none"));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-producer-posture:telemetry-failed", "[PostureProducer] telemetry threw: " + ex.Message);
            }
        }

        internal static void Leaf(BattleSnapshot s, int i, TacticalLeafBrigadeMap.LeafAssignment leaf, CommandTaskType task,
            string outcome, bool submitted)
        {
            try
            {
                int id = s.Own[i].InstanceId;
                DirectChildRole role = leaf.LeafRole;
                string signature = "TacticalLeafCascade|side=" + s.Alliance + "|leaf=" + id + "|role=" + role
                    + "|task=" + task + "|outcome=" + outcome + "|submitted=" + submitted;
                if (!TacticalTelemetry.ShouldEmit(_lastAt, "tactical-leaf-cascade:" + s.Alliance + ":" + id, signature,
                        Time.realtimeSinceStartup, TelemetrySeconds, false))
                    return;
                string unit = NameOf(s, i);
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalLeafCascade", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(s.Alliance)
                        .WithUnit(unit)
                        .WithDecision("TacticalLeafCascade", outcome, signature)
                        .WithField("role", role.ToString())
                        .WithField("task", task.ToString())
                        .WithField("chain", leaf.CascadeChainString)
                        .WithField("applied", submitted));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-producer-posture:telemetry-failed", "[PostureProducer] telemetry threw: " + ex.Message);
            }
        }

        internal static void Facing(BattleSnapshot s, int i, TacticalFacingPulseScope scope, CommandTaskType task,
            TacticalFacingPulseDecision decision, bool submitted, string facingSource = null)
        {
            try
            {
                int id = s.Own[i].InstanceId;
                string signature = s.Alliance + "|" + id + "|" + scope + "|" + task + "|" + decision.Action + "|"
                    + decision.Reason + "|" + submitted;
                if (!TacticalTelemetry.ShouldEmit(_lastAt, "tactical-facing-pulse:" + s.Alliance + ":" + id, signature,
                        Time.realtimeSinceStartup, TelemetrySeconds, false))
                    return;
                string unit = NameOf(s, i);
                TelemetryRouter.Emit(TelemetryLayer.Tactical,
                    decision.ShouldWrite ? TelemetryCategory.Write : TelemetryCategory.Decision,
                    "TacticalFacingPulse", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(s.Alliance)
                        .WithUnit(unit)
                        .WithDecision(decision.Action.ToString(), decision.Reason, signature)
                        .WithField("scope", scope.ToString())
                        .WithField("task", task.ToString())
                        .WithField("targetFormation", decision.TargetFormation)
                        .WithField("targetFacing", decision.TargetFacingDegrees)
                        .WithField("facingSource", facingSource ?? PostureTargets.NoFacingSource)
                        .WithField("writeResult", submitted ? "submitted" : "none"));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-producer-posture:telemetry-failed", "[PostureProducer] telemetry threw: " + ex.Message);
            }
        }

        // BC11 decision 2: a refused flank of a group in combat, rotated in place (limited by vanilla's micro turn).
        internal static void RefuseInPlace(BattleSnapshot s, int i, RefuseFlank side, int rotated, int skipped)
        {
            try
            {
                int id = s.Own[i].InstanceId;
                // The repeat test is numeric: no string is built unless a row is due.
                if (!_refuseRows.ShouldEmit(s.Alliance, id, side, rotated, skipped, Time.realtimeSinceStartup, TelemetrySeconds)) return;
                string signature = s.Alliance + "|" + id + "|" + side + "|" + rotated + "|" + skipped;
                string unit = NameOf(s, i);
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Write, "TacticalRefuseInPlace", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(s.Alliance)
                        .WithUnit(unit)
                        .WithDecision("RefuseInPlace", side.ToString(), signature)
                        .WithField("side", side.ToString())
                        .WithField("rotated", rotated)
                        .WithField("skippedByMicroTurn", skipped));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-producer-posture:telemetry-failed", "[PostureProducer] telemetry threw: " + ex.Message);
            }
        }

        private static void Reset()
        {
            _lastAt.Clear();
            _refuseRows.Clear();
        }
    }

    /// <summary>
    /// #61 TryApplyContactFacingPulse at Priority.Scorer on the Facing channel (and
    /// the brigade's formation correction on Formation), for groups the posture
    /// producer left without intents this tick. Regiments are faced one by one,
    /// following vanilla's detached-subtree exclusion (119865).
    /// </summary>
    internal sealed class FacingPulseIntentProducer : IBattleIntentProducer
    {
        public string Name => "FacingPulse";

        private const int MaxRegimentPulsesPerGroup = 4;
        // #61 cooldowns, real seconds at 1x on the doctrine clock.
        private static readonly double BrigadeCooldownHours = TacticalBattleClock.RealSecondsAtOneXToHours(12f);
        private static readonly double RegimentCooldownHours = TacticalBattleClock.RealSecondsAtOneXToHours(5f);

        public void Produce(BattleSnapshot s, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.facing-pulse", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (Plugin.EnableTacticalContactFacingPulse == null || !Plugin.EnableTacticalContactFacingPulse.Value) return;
                PostureTick.State t = PostureTick.Current(s);
                OrderExecutor executor = OrderExecutor.ForAlliance(s != null ? s.Alliance : -1);
                if (t == null || executor == null || sink == null) return;

                for (int i = 0; i < s.OwnCount; i++)
                {
                    ref GroupPosture g = ref t.Groups[i];
                    if (!g.Has || g.Submitted) continue;
                    TacticalFacingPulseScope scope = ScopeFor(g.State.Echelon);
                    if (scope == TacticalFacingPulseScope.Division)
                    {
                        TacticalFacingPulseInput axisInput = Input(s, t, executor, i, g.State.Task, scope, -1, g.CloseEngaged, g.FlankRisk, -1f);
                        TacticalFacingPulseDecision axis = TacticalFacingPulseDoctrine.Decide(axisInput);
                        PostureTelemetry.Facing(s, i, scope, g.State.Task, axis, false, FacingSourceOf(s, i, scope, axisInput));
                        continue;
                    }
                    if (scope != TacticalFacingPulseScope.Brigade) continue;

                    ref UnitRecord rec = ref s.Own[i];
                    int targetFormation = CommandFormationCorrection.TargetFormationForTask(g.State.Task, rec.Formation);
                    TacticalFacingPulseInput brigadeInput = Input(s, t, executor, i, g.State.Task, scope, targetFormation, g.CloseEngaged, g.FlankRisk, -1f);
                    TacticalFacingPulseDecision brigade = TacticalFacingPulseDoctrine.Decide(brigadeInput);
                    // #61 sent the brigade pulse through SetFormation, whose threat
                    // facing (holding tasks only; BC9: also a Screen/Probe watching an
                    // entry point) rotated the regiments in contact or wheeled the group
                    // out of it. On a clean subtree the group order is as shipped; a
                    // detached or player-owned descendant leaves the facing to the
                    // per-regiment pulses below.
                    bool cleanSubtree = PostureSense.SubtreeAcceptsGroupFacing(t.Descendants[i]);
                    bool submitted = PostureIntentPlan.SubmitBrigadePulse(sink, rec.InstanceId, brigadeInput, brigade, cleanSubtree) > 0;
                    PostureTelemetry.Facing(s, i, scope, g.State.Task, brigade, submitted, FacingSourceOf(s, i, scope, brigadeInput));
                    // BC9: regiments of a brigade turning to an entry point take the
                    // brigade's bearing; on a clean subtree the group facing turns them.
                    bool entry = brigade.ShouldWrite && brigadeInput.ThreatSource == TacticalFacingThreatSource.Entry;
                    float entryHeading = entry ? brigade.TargetFacingDegrees : -1f;

                    int regiments = PostureSense.CollectRotatableRegiments(s, i, t.RegimentQueue, t.Regiments);
                    int pulses = 0;
                    for (int r = 0; r < regiments && pulses < MaxRegimentPulsesPerGroup; r++)
                    {
                        int u = t.Regiments[r];
                        bool engaged = g.CloseEngaged || PostureSense.CloseEngaged(s, u, t.Descendants[u]);
                        bool flank = g.FlankRisk || BattleWriterPolicy.PerceivedFlankRisk(s, u, t.FlankThreatMean);
                        if (entry && cleanSubtree && s.ClosestVisibleEnemy(u, out _) < 0) continue;
                        // R9: regiments only rotate — the current formation is the target.
                        TacticalFacingPulseDecision d = TacticalFacingPulseDoctrine.Decide(Input(s, t, executor, u, g.State.Task,
                            TacticalFacingPulseScope.Regiment, s.Own[u].Formation, engaged, flank, entry ? entryHeading : -2f));
                        if (d.Action != TacticalFacingPulseAction.CorrectFacing) continue;
                        sink.Submit(PostureIntentPlan.FacingPulse(s.Own[u].InstanceId, d.TargetFacingDegrees));
                        PostureTelemetry.Facing(s, u, TacticalFacingPulseScope.Regiment, g.State.Task, d, true, PostureTargets.ClosestFacingSource);
                        pulses++;
                    }
                    g.FacingSubmitted = submitted || pulses > 0;
                }
            }
        }

        // Smoke item 2.27, telemetry facingSource: where the pulse's threat bearing came from (pressure, closest, entry or none).
        private static string FacingSourceOf(BattleSnapshot s, int u, TacticalFacingPulseScope scope, in TacticalFacingPulseInput input)
        {
            if (input.ThreatSource == TacticalFacingThreatSource.Entry) return "entry";
            if (input.ThreatSource == TacticalFacingThreatSource.None) return PostureTargets.NoFacingSource;
            return scope != TacticalFacingPulseScope.Regiment && Plugin.FlankResponseEnabled() &&
                FlankFacing.MostPressuredBearing(s, u, s.Own[u].RotationY, out _, out _)
                ? PostureTargets.PressureFacingSource
                : PostureTargets.ClosestFacingSource;
        }

        // entryHeading: >= 0 a bearing handed down by the brigade (regiments of an
        // entry-facing brigade); -1 look up the unit's own entry bearing; -2 none.
        private static TacticalFacingPulseInput Input(BattleSnapshot s, PostureTick.State t, OrderExecutor executor, int u,
            CommandTaskType task, TacticalFacingPulseScope scope, int targetFormation, bool closeEngaged, bool flankRisk, float entryHeading)
        {
            ref UnitRecord rec = ref s.Own[u];
            int k = s.ClosestVisibleEnemy(u, out float distance);
            bool hasThreat = k >= 0;
            float heading = hasThreat ? PostureSense.HeadingTo(rec.X, rec.Z, s.Observations[k].X, s.Observations[k].Z) : -1f;
            // BC11 decision 1: a brigade or division faces the strongest pressure on its front; the regiment pulse
            // stays on the closest visible enemy (vanilla's per-regiment rule, StopMicroUnitsCloseToEnemyAndRotateTowards 4539).
            if (hasThreat && scope != TacticalFacingPulseScope.Regiment && Plugin.FlankResponseEnabled() &&
                FlankFacing.MostPressuredBearing(s, u, rec.RotationY, out float pressureBearing, out _))
                heading = pressureBearing;
            TacticalFacingThreatSource source = hasThreat ? TacticalFacingThreatSource.Visible : TacticalFacingThreatSource.None;
            // BC9: with nothing visible, a screen, probe or flank guard faces where
            // unseen enemies come from (the doctrine refuses the source for other tasks).
            if (!hasThreat && TacticalFacingPulseDoctrine.WatchesEntry(task))
            {
                if (entryHeading >= 0f)
                {
                    heading = entryHeading;
                    source = TacticalFacingThreatSource.Entry;
                }
                else if (entryHeading > -1.5f && ReconIntentProducer.TryEntryBearing(s, u, out float own))
                {
                    heading = own;
                    source = TacticalFacingThreatSource.Entry;
                }
            }
            bool activeMove = PostureSense.ActiveMove(rec, t.Moving[u]);
            bool recent = scope == TacticalFacingPulseScope.Regiment
                ? PostureIntentPlan.IssuedWithin(executor, rec.InstanceId, OrderChannel.Facing, IntentSource.FacingPulse, s.DoctrineHours, RegimentCooldownHours)
                : PostureIntentPlan.IssuedWithin(executor, rec.InstanceId, OrderChannel.Formation, IntentSource.FacingPulse, s.DoctrineHours, BrigadeCooldownHours);
            return new TacticalFacingPulseInput(
                scope,
                task,
                rec.Formation,
                targetFormation,
                rec.RotationY,
                heading,
                source != TacticalFacingThreatSource.None,
                source,
                closeEngaged,
                flankRisk,
                (rec.Flags & UnitFlags.PlayerOwned) != 0,
                PostureSense.Routed(rec),
                PostureSense.PendingOrder(rec, activeMove, allowStaleQueuedBypass: true),
                activeMove,
                recent,
                PostureSense.FacingToleranceDegrees,
                isArtillery: (rec.Flags & UnitFlags.IsArtillery) != 0,
                // Fire range is not in the snapshot: a visible threat within close contact.
                threatWithinFireRange: hasThreat && distance <= PostureSense.CloseContactMeters);
        }

        private static TacticalFacingPulseScope ScopeFor(CommandEchelonKind echelon)
        {
            switch (echelon)
            {
                case CommandEchelonKind.ArmyLike:
                case CommandEchelonKind.CorpsLike:
                case CommandEchelonKind.DivisionLike:
                    return TacticalFacingPulseScope.Division;
                case CommandEchelonKind.BrigadeLike:
                    return TacticalFacingPulseScope.Brigade;
                default:
                    return TacticalFacingPulseScope.Unknown;
            }
        }
    }

    /// <summary>
    /// #61 TryApplyFireControl: per-regiment combat behavior (Priority.Executor,
    /// CombatBehavior channel, PerRegiment — D10) for a settled group the posture
    /// producer and the facing pulse left alone this tick.
    /// </summary>
    internal sealed class FireControlIntentProducer : IBattleIntentProducer
    {
        public string Name => "FireControl";

        // #61 FireControlOrderCooldownHours: 45 real seconds at 1x.
        private static readonly double CooldownHours = TacticalBattleClock.RealSecondsAtOneXToHours(45f);
        private const float FriendlyBlockerThreshold = 0.35f;
        private const float AlignedDot = 0.25f;

        public void Produce(BattleSnapshot s, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.fire-control", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PostureTick.State t = PostureTick.Current(s);
                OrderExecutor executor = OrderExecutor.ForAlliance(s != null ? s.Alliance : -1);
                if (t == null || executor == null || sink == null) return;
                UnitRecord[] own = s.Own;

                for (int g = 0; g < s.OwnCount; g++)
                {
                    ref GroupPosture posture = ref t.Groups[g];
                    if (!posture.Has || posture.Submitted || posture.FacingSubmitted) continue;
                    if (PostureSense.Routed(own[g])) continue;
                    bool groupMoving = PostureSense.ActiveMove(own[g], t.Moving[g]);
                    if (groupMoving || PostureSense.PendingOrder(own[g], groupMoving, allowStaleQueuedBypass: false)) continue;

                    // The group's own subtree (#61 read allattachedunits).
                    int count = PostureSense.CollectDescendants(s, g, ref t.Subtree, ref t.Children);
                    for (int k = 0; k < count; k++)
                        ProduceForUnit(s, t, executor, g, t.Subtree[k], posture.State, sink);
                }
            }
        }

        private static void ProduceForUnit(BattleSnapshot s, PostureTick.State t, OrderExecutor executor, int g, int u,
            CommandNodeOperationalState state, IIntentSink sink)
        {
            ref UnitRecord unit = ref s.Own[u];
            if (!PostureSense.IsFireControlUnit(unit)) return;
            int typ = unit.UnitTyp;
            UnitFlags f = unit.Flags;
            if ((f & (UnitFlags.PlayerOwned | UnitFlags.PermanentlyDetached)) != 0 || (f & UnitFlags.Active) == 0) return;
            if (PostureSense.Routed(unit)) return;
            bool moving = PostureSense.ActiveMove(unit, t.Moving[u]);
            if (moving || PostureSense.PendingOrder(unit, moving, allowStaleQueuedBypass: false)) return;
            if (PostureIntentPlan.IssuedWithin(executor, unit.InstanceId, OrderChannel.CombatBehavior, IntentSource.FireControl, s.DoctrineHours, CooldownHours))
                return;

            int k = s.ClosestVisibleEnemy(u, out float distance);
            if (k < 0) k = s.ClosestVisibleEnemy(g, out distance);
            bool hasTarget = k >= 0;
            EnemyObservation target = hasTarget ? s.Observations[k] : default(EnemyObservation);
            if (hasTarget) distance = PostureSense.Distance(unit.X, unit.Z, target.X, target.Z);

            TacticalFireControlDecision decision = TacticalFireControlDoctrine.Decide(new TacticalFireControlInput(
                typ,
                mounted: (f & UnitFlags.Mounted) != 0,
                state.Task,
                state.Role,
                hasTarget && distance > 0f,
                distance,
                effectiveFireRange: 0f,
                unit.Ammo01,
                unit.Morale,
                unit.Fatigue,
                inCover: PostureSense.InCover(unit),
                enemyAdvancing: hasTarget && EnemyAdvancing(s, t, target.InstanceId),
                friendlyBlockedAhead: hasTarget && t.Friendly.EstimateFrontBlocker(unit.InstanceId, unit.X, unit.Z, target.X, target.Z) > FriendlyBlockerThreshold,
                alignedToTarget: !hasTarget || Aligned(unit, target),
                loadedVolley: true,
                currentChargeOrdered: unit.CombatBehaviorOrdered == TacticalFireControlDoctrine.InfantryCharge ||
                    unit.CombatBehaviorOrdered == TacticalFireControlDoctrine.CavalryCharge || unit.MovementMode == 3,
                currentCombatBehaviorOrdered: unit.CombatBehaviorOrdered));
            if (decision.ShouldWrite) sink.Submit(PostureIntentPlan.FireControl(unit.InstanceId, decision.RecommendedCombatBehavior));
        }

        // The visible target's own record: moving (paths or movement mode).
        private static bool EnemyAdvancing(BattleSnapshot s, PostureTick.State t, int enemyId)
        {
            if (!t.EnemyIndexById.TryGetValue(enemyId, out int e)) return false;
            return s.EnemyRecords[e].RegimentPaths > 0 || s.EnemyRecords[e].MovementMode > 0;
        }

        private static bool Aligned(in UnitRecord unit, in EnemyObservation target)
        {
            float dx = target.X - unit.X;
            float dz = target.Z - unit.Z;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.1f) return true;
            double facing = unit.RotationY * Math.PI / 180.0;
            return (Math.Sin(facing) * dx + Math.Cos(facing) * dz) / len >= AlignedDot;
        }
    }
}
