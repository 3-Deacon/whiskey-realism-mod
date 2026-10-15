using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
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
    //   Regiment.AddPath area costs 130364-130394 (R5): PostureNavAreaCosts,
    //     applied before every NavMesh.CalculatePath of the path probe.
    //
    // Deleted with #61, not ported (the executor owns them): the recent-order
    // stamps, failed-write backoff, brigade task hold, courier cadence, outbound
    // order ledger, duplicate-waypoint and safe-waypoint checks, the write gate,
    // the groupsubordinatesmoving stamp, and the BlockedMovingOrder path
    // (vanilla UpdateBlockedCrossings 7917 handles crossings).
    internal static class PostureProducers
    {
        /// <summary>
        /// Runs the producers' static constructors, which register them with
        /// BattleCoreTick in plan order. Called from the coordinator tick
        /// (ArmyPostureInputsRuntime.Refresh, CheckGlobalAIStrategy 5866) before the
        /// arbiter's first AdjustGroupFormations Postfix.
        /// </summary>
        internal static void EnsureRegistered()
        {
            PostureIntentProducer.Register();
            FacingPulseIntentProducer.Register();
            FireControlIntentProducer.Register();
            DivisionPlayIntentProducer.Register();
        }
    }

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
            // shared by the posture and division-play producers.
            public readonly NavPlanBudget Nav = new NavPlanBudget(4);
            public int LastOwnCount;
            public bool NavDeferred;
            public readonly List<float> SupportMainX = new List<float>(8);
            public readonly List<float> SupportMainZ = new List<float>(8);
            public bool SupportMainBuilt;
            public readonly int[] RegimentQueue = new int[64];
            public readonly int[] Regiments = new int[64];
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
        private static readonly PostureIntentProducer Instance = new PostureIntentProducer();

        static PostureIntentProducer()
        {
            BattleScope.Register("posture-producer", Reset);
            BattleCoreTick.RegisterProducer(Instance);
        }

        internal static void Register() { }

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
                        if (!PostureLedger.TryResolveState(rec.InstanceId, side, out CommandNodeOperationalState state)) continue;
                        bool hasOrder = PostureLedger.TryResolveDoctrineOrder(rec.InstanceId, army, state.NodeId, out CommandDoctrineOrder order);
                        bool submitted = PosturePipeline.Run(s, t, i, state, hasOrder, order, side, sink,
                            IntentPriority.Executor, IntentSource.PostureExecutor, PostureIntentPlan.ExecutorReason,
                            recordTick: true);
                        t.Groups[i].Submitted = submitted;
                        continue;
                    }

                    if (rec.UnitTyp < TacticalUnitType.BattleGroupBrigade || leafMap == null) continue;
                    if (!leafMap.TryGetValue(rec.InstanceId, out TacticalLeafBrigadeMap.LeafAssignment leaf)) continue;
                    if (leaf.LeafTask == CommandTaskType.None) continue;
                    ProduceLeaf(s, t, i, leaf, army, sink);
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
            ArmyOrchestrator army, IIntentSink sink)
        {
            UnitRecord[] own = s.Own;
            ref UnitRecord rec = ref own[i];
            int id = rec.InstanceId;

            ReinforcementOpportunity opportunity = ReinforcementOpportunity.NoOpportunity;
            try { opportunity = army.CurrentReinforcementOpportunity.Opportunity; }
            catch { /* NoOpportunity keeps the ledger task */ }
            CommandTaskType task = TacticalOpportunityTaskOverride.Override(leaf.LeafTask, leaf.LeafRole, opportunity, out string overrideReason);

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

            if (overrideReason != null)
                OnceLog.Info("tactical-leaf-cascade:opportunity-override",
                    "[TacticalLeafCascade] opportunity override: " + leaf.LeafTask + " -> " + task
                    + " (role=" + leaf.LeafRole + " opportunity=" + opportunity + " reason=" + overrideReason + ")");

            if (PostureSense.Routed(rec)) { PostureTelemetry.Leaf(s, i, leaf, task, "routed", false); return; }

            bool closeEngaged = PostureSense.CloseEngaged(s, i, t.Descendants[i]);
            bool flankRisk = PostureSense.FlankRisk(s, i);
            bool activeMove = PostureSense.ActiveMove(rec, t.Moving[i]);
            bool pending = PostureSense.PendingOrder(rec, activeMove, allowStaleQueuedBypass: false);
            bool hasSlot = LeafSlotTarget.TryResolve(s, i, out int parent, out float x, out float z);
            bool parentActive = false;
            if (parent >= 0)
            {
                bool parentMoving = PostureSense.ActiveMove(own[parent], t.Moving[parent]);
                parentActive = own[parent].RegimentPaths > 0 || parentMoving ||
                    PostureSense.PendingOrder(own[parent], parentMoving, allowStaleQueuedBypass: false);
            }

            LeafMoveDecision move = LeafBrigadeMovePolicy.Decide(task, parentActive, hasSlot, closeEngaged, pending, activeMove);
            if (!move.Move) { PostureTelemetry.Leaf(s, i, leaf, task, move.Reason, false); return; }

            sink.Submit(PostureIntentPlan.LeafMove(id, task, x, z, inContactWithdrawal: flankRisk));
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
                return resolution.Found &&
                    CommandNodeOperationsRuntime.TryBuildSingle(resolution.Intent, army.CurrentOperation,
                        side.OperationsLedger?.CurrentObjectives, out state);
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
            bool recordTick)
        {
            ArmyOrchestrator army = side.Army;
            ref UnitRecord rec = ref s.Own[i];
            UnitFlags descendants = t.Descendants[i];

            if (hasOrder && order.Task != state.Task)
                state = WithRoleAndTask(state, order.Role, order.Task);

            bool closeEngaged = PostureSense.CloseEngaged(s, i, descendants);
            bool flankRisk = PostureSense.FlankRisk(s, i);
            bool hasCavalry = ((rec.Flags | descendants) & UnitFlags.IsCavalry) != 0;
            bool visibleEnemy = s.ClosestVisibleEnemy(i, out _) >= 0;

            TacticalGrandTacticianReconDecision recon = TacticalGrandTacticianReconDoctrine.Decide(
                new TacticalGrandTacticianReconInput(state.Role, state.Task, hasCavalry, visibleEnemy, underRecentFire: closeEngaged));
            if (recon.Task != state.Task)
            {
                state = WithRoleAndTask(state, state.Role, recon.Task);
                if (hasOrder) order = order.WithTask(recon.Task, recon.Reason);
            }

            if (hasCavalry && TryCavalryFollow(s, t, i, state, flankRisk, out CommandTaskType cavalryTask, out string cavalryReason))
            {
                state = WithRoleAndTask(state, state.Role, cavalryTask);
                if (hasOrder) order = order.WithTask(cavalryTask, cavalryReason);
            }

            CommandPhysicalState physical = PostureSense.Physical(rec, t.Moving[i]);
            TacticalIdleClassification idle = TacticalCommandMonitor.ClassifyIdle(state, physical);
            CommandTaskType emergency = CommandFormationCorrection.TaskForLocalFlankEmergency(state.Task, closeEngaged, flankRisk);
            if (emergency != state.Task) state = WithRoleAndTask(state, state.Role, emergency);

            // Army opportunity override at the brigade-posture layer (2026-05-21),
            // with Main as the role (brigades carry no DirectChildRole).
            ReinforcementOpportunity opportunity = ReinforcementOpportunity.NoOpportunity;
            try { if (army != null) opportunity = army.CurrentReinforcementOpportunity.Opportunity; }
            catch { /* NoOpportunity keeps the task */ }
            CommandTaskType overridden = TacticalOpportunityTaskOverride.Override(state.Task, DirectChildRole.Main, opportunity, out string overrideReason);
            if (overridden != state.Task && overrideReason != null)
            {
                OnceLog.Info("tactical-brigade-posture:opportunity-override",
                    "[TacticalBrigadePosture] opportunity override: " + state.Task + " -> " + overridden
                    + " (opportunity=" + opportunity + " reason=" + overrideReason + ")");
                state = WithRoleAndTask(state, state.Role, overridden);
                if (hasOrder) order = order.WithTask(overridden, overrideReason);
            }

            int targetFormation = rec.UnitTyp > TacticalUnitType.MaxCombat
                ? CommandFormationCorrection.TargetFormationForTask(state.Task, rec.Formation)
                : -1;
            bool orderPending = PostureSense.PendingOrder(rec, physical.ActiveMove, CanBypassStaleQueuedOrder(state.Task));
            bool alreadyCorrect = !physical.PathInterrupted &&
                (physical.ActiveMove ||
                 ((targetFormation < 0 || rec.Formation == targetFormation) &&
                  idle == TacticalIdleClassification.ValidIdle && CommandPostureExecutor.IdleCanSatisfyTask(state.Task)));
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
                ? CommandPostureExecutor.Decide(order, physical, eligibility, (float)s.DoctrineHours)
                : CommandPostureExecutor.Decide(state, physical, eligibility);

            if (recordTick)
            {
                ref GroupPosture g = ref t.Groups[i];
                g.Has = true;
                g.State = state;
                g.CloseEngaged = closeEngaged;
                g.FlankRisk = flankRisk;
            }

            if (decision.Action == PostureExecutionAction.NoWrite)
            {
                PostureTelemetry.Group(s, i, reason, state, decision, 0, decision.Reason);
                return false;
            }

            bool hasTarget = PostureTargets.TryResolve(s, t, i, side, state, order, decision.Target, out float x, out float z);
            if (!hasTarget && t.NavDeferred)
            {
                PostureTelemetry.Group(s, i, reason, state, decision, 0, "nav-budget-deferred");
                return false;
            }

            bool hasFacing = false;
            float heading = -1f;
            if (CommandFormationCorrection.ShouldFaceThreat(state.Task) &&
                PostureSense.SubtreeAcceptsGroupFacing(descendants) &&
                PostureTargets.TryThreat(s, i, out float threatX, out float threatZ))
            {
                heading = PostureSense.HeadingTo(rec.X, rec.Z, threatX, threatZ);
                hasFacing = CommandFormationCorrection.NeedsFacingCorrection(rec.RotationY, heading, PostureSense.FacingToleranceDegrees);
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
                HasFacing = hasFacing,
                Heading = heading,
                InContactWithdrawal = closeEngaged || flankRisk,
                Priority = priority,
                Source = source,
                Reason = reason,
            };
            int submitted = PostureIntentPlan.Submit(sink, request);
            PostureTelemetry.Group(s, i, reason, state, decision, submitted, hasTarget ? decision.Reason : "target-unresolved");
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
                case CommandTaskType.RecoverStuckOrder:
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
            TacticalCavalryFollowDecision follow = TacticalCavalryFollowDoctrine.Decide(new TacticalCavalryFollowInput(
                mode,
                hasFollowTarget: hasTarget,
                targetHidden: false,
                targetIsOfficer: hasTarget && obs[target].UnitTyp == TacticalUnitType.Officer,
                targetInFort: hasTarget && TargetGarrisoned(s, t, obs[target].InstanceId),
                targetInSquare: false,
                targetHasInfantryOrArtillerySupport: hasTarget && TargetHasCloseSupport(s, target),
                canChargeTarget: hasTarget && distance <= 350f,
                leaderAllowsCharge: true,
                fearAdvantage01: fear,
                enemyClose: hasTarget && distance > 0f && distance <= 350f,
                enemyDistance: hasTarget ? distance : 0f,
                longRange: 300f,
                atFollowLocation: false));

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

        internal static CommandNodeOperationalState WithRoleAndTask(CommandNodeOperationalState state, CommandNodeRole role, CommandTaskType task)
        {
            return new CommandNodeOperationalState(state.NodeId, state.Echelon, role, task, state.TaskState, state.X, state.Z, state.FacingDegrees);
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

        internal static bool TryResolve(BattleSnapshot s, PostureTick.State t, int i, TacticalBattleOrchestrator side,
            CommandNodeOperationalState state, CommandDoctrineOrder order, PostureExecutionTarget kind, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            t.NavDeferred = false;
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
            x = 0f;
            z = 0f;
            var objectives = side?.OperationsLedger?.CurrentObjectives;
            if (objectives != null && objectives.Count > 0)
            {
                string primary = side.Army != null ? side.Army.CurrentOperation.PrimaryObjectiveId : "objective-unknown";
                for (int k = 0; k < objectives.Count; k++)
                {
                    if (!string.Equals(objectives[k].Observation.ObjectiveId, primary, StringComparison.Ordinal)) continue;
                    x = objectives[k].Observation.Location.X;
                    z = objectives[k].Observation.Location.Z;
                    return PostureSense.HasPoint(x, z);
                }
                x = objectives[0].Observation.Location.X;
                z = objectives[0].Observation.Location.Z;
                return PostureSense.HasPoint(x, z);
            }

            ref UnitRecord rec = ref s.Own[i];
            x = rec.LastWaypointX;
            z = rec.LastWaypointZ;
            return PostureSense.HasPoint(x, z) &&
                PostureSense.Distance(rec.X, rec.Z, x, z) >= PostureSense.MinWaypointMeters;
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
            ApplyVanillaPathAreaCosts();
            return new[]
            {
                Sample(t, rec.InstanceId, own, target, task, hasThreat, threat, hasWaypoint, waypoint),
                Sample(t, rec.InstanceId, own, target + lateral * 120f, task, hasThreat, threat, hasWaypoint, waypoint),
                Sample(t, rec.InstanceId, own, target - lateral * 120f, task, hasThreat, threat, hasWaypoint, waypoint),
            };
        }

        // R5: AddPath's battle area costs before probing; every AddPath sets them again.
        private static void ApplyVanillaPathAreaCosts()
        {
            try
            {
                bool frozen = GameVars.isfrozen();
                for (int a = 0; a < PostureNavAreaCosts.AreaNames.Length; a++)
                {
                    int area = NavMesh.GetAreaFromName(PostureNavAreaCosts.AreaNames[a]);
                    if (area >= 0) NavMesh.SetAreaCost(area, PostureNavAreaCosts.Cost(a, frozen));
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-producer-posture:area-cost-failed",
                    "[PostureProducer] NavMesh area costs failed: " + ex.Message);
            }
        }

        private static TacticalPathQualitySample Sample(PostureTick.State t, int selfId, Vector3 own, Vector3 candidate,
            CommandTaskType task, bool hasThreat, Vector3 threat, bool hasWaypoint, Vector3 waypoint)
        {
            float slopeCost = 0f, terrainRisk = 0f, bridgeRisk = 0f, congestion = 0f, roadPreference = 0f;
            float deadGround = 0f, threatExposure = 0f, routeContinuity = 0f, reservationPressure = 0f;
            float fallbackLaneConflict = 0f, artilleryDanger = 0f;
            try
            {
                var path = new NavMeshPath();
                bool found = NavMesh.CalculatePath(own, candidate, NavMesh.AllAreas, path);
                Vector3[] corners = path.corners;
                int count = corners != null ? corners.Length : 0;
                if (!found || path.status != NavMeshPathStatus.PathComplete || count <= 0)
                {
                    terrainRisk = 1f;
                }
                else
                {
                    roadPreference = count <= 3 ? 0.65f : 0.25f;
                    congestion = Math.Min(1f, Math.Max(0f, (count - 4) * 0.12f));
                    float previousHeight = SampleHeight(own);
                    for (int c = 0; c < count; c++)
                    {
                        float height = SampleHeight(corners[c]);
                        slopeCost = Math.Max(slopeCost, Math.Min(1f, Math.Abs(height - previousHeight) / 35f));
                        previousHeight = height;
                        int terrain = TerrainAt(corners[c]);
                        if (terrain == 4 || terrain == 8 || terrain == 6)
                        {
                            terrainRisk = Math.Max(terrainRisk, 0.85f);
                            bridgeRisk = Math.Max(bridgeRisk, 0.70f);
                        }
                        if (NearCrossingTerrain(corners[c])) bridgeRisk = Math.Max(bridgeRisk, 0.55f);
                    }
                }

                if (hasThreat)
                {
                    float distance = Flat(candidate, threat);
                    deadGround = distance >= 250f ? 0.70f : Math.Max(0f, distance / 250f * 0.45f);
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

        private static int TerrainAt(Vector3 point)
        {
            try { return BattlefieldSetup.GetCurrentTerrainOnPos(point); }
            catch { return -1; }
        }

        private static readonly int[] CrossingTerrain = { 4, 8 };

        private static bool NearCrossingTerrain(Vector3 point)
        {
            try { return BattlefieldSetup.SearchTerrainInRangePos(point, CrossingTerrain, 2) != null; }
            catch { return false; }
        }

        // BattleUnits keeps the scene's BattlefieldSetup in a private static (set at
        // 79055/79132, nulled on destroy at 79230).
        private static BattlefieldSetup Bfs()
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

        internal static string NameOf(BattleSnapshot s, int i)
        {
            var names = BattleSnapshotBuilder.NamesFor(s.Alliance);
            int id = s.Own[i].InstanceId;
            return names != null && names.TryGetValue(id, out string name) ? TacticalCurrentOrderSignature.Safe(name) : "#" + id;
        }

        internal static void Group(BattleSnapshot s, int i, string source, CommandNodeOperationalState state,
            PostureExecutionDecision decision, int submitted, string reason)
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
            TacticalFacingPulseDecision decision, bool submitted)
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
                        .WithField("writeResult", submitted ? "submitted" : "none"));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-producer-posture:telemetry-failed", "[PostureProducer] telemetry threw: " + ex.Message);
            }
        }

        private static void Reset()
        {
            _lastAt.Clear();
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
        private static readonly FacingPulseIntentProducer Instance = new FacingPulseIntentProducer();

        static FacingPulseIntentProducer()
        {
            BattleCoreTick.RegisterProducer(Instance);
        }

        internal static void Register() { }

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
                        TacticalFacingPulseDecision axis = Evaluate(s, t, executor, i, g.State.Task, scope, -1, g.CloseEngaged, g.FlankRisk);
                        PostureTelemetry.Facing(s, i, scope, g.State.Task, axis, false);
                        continue;
                    }
                    if (scope != TacticalFacingPulseScope.Brigade) continue;

                    ref UnitRecord rec = ref s.Own[i];
                    int targetFormation = CommandFormationCorrection.TargetFormationForTask(g.State.Task, rec.Formation);
                    TacticalFacingPulseDecision brigade = Evaluate(s, t, executor, i, g.State.Task, scope, targetFormation, g.CloseEngaged, g.FlankRisk);
                    bool submitted = false;
                    if (brigade.ShouldWrite && brigade.TargetFormation >= 0 && brigade.TargetFormation != rec.Formation)
                    {
                        sink.Submit(PostureIntentPlan.FacingPulseFormation(rec.InstanceId, brigade.TargetFormation));
                        submitted = true;
                    }
                    PostureTelemetry.Facing(s, i, scope, g.State.Task, brigade, submitted);

                    int regiments = PostureSense.CollectRotatableRegiments(s, i, t.RegimentQueue, t.Regiments);
                    int pulses = 0;
                    for (int r = 0; r < regiments && pulses < MaxRegimentPulsesPerGroup; r++)
                    {
                        int u = t.Regiments[r];
                        bool engaged = g.CloseEngaged || PostureSense.CloseEngaged(s, u, t.Descendants[u]);
                        bool flank = g.FlankRisk || PostureSense.FlankRisk(s, u);
                        // R9: regiments only rotate — the current formation is the target.
                        TacticalFacingPulseDecision d = Evaluate(s, t, executor, u, g.State.Task, TacticalFacingPulseScope.Regiment,
                            s.Own[u].Formation, engaged, flank);
                        if (d.Action != TacticalFacingPulseAction.CorrectFacing) continue;
                        sink.Submit(PostureIntentPlan.FacingPulse(s.Own[u].InstanceId, d.TargetFacingDegrees));
                        PostureTelemetry.Facing(s, u, TacticalFacingPulseScope.Regiment, g.State.Task, d, true);
                        pulses++;
                    }
                    g.FacingSubmitted = submitted || pulses > 0;
                }
            }
        }

        private static TacticalFacingPulseDecision Evaluate(BattleSnapshot s, PostureTick.State t, OrderExecutor executor, int u,
            CommandTaskType task, TacticalFacingPulseScope scope, int targetFormation, bool closeEngaged, bool flankRisk)
        {
            ref UnitRecord rec = ref s.Own[u];
            int k = s.ClosestVisibleEnemy(u, out float distance);
            bool hasThreat = k >= 0;
            float heading = hasThreat ? PostureSense.HeadingTo(rec.X, rec.Z, s.Observations[k].X, s.Observations[k].Z) : -1f;
            bool activeMove = PostureSense.ActiveMove(rec, t.Moving[u]);
            bool recent = scope == TacticalFacingPulseScope.Regiment
                ? PostureIntentPlan.IssuedWithin(executor, rec.InstanceId, OrderChannel.Facing, IntentSource.FacingPulse, s.DoctrineHours, RegimentCooldownHours)
                : PostureIntentPlan.IssuedWithin(executor, rec.InstanceId, OrderChannel.Formation, IntentSource.FacingPulse, s.DoctrineHours, BrigadeCooldownHours);
            return TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
                scope,
                task,
                rec.Formation,
                targetFormation,
                rec.RotationY,
                heading,
                hasThreat,
                hasThreat ? TacticalFacingThreatSource.Visible : TacticalFacingThreatSource.None,
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
                threatWithinFireRange: hasThreat && distance <= PostureSense.CloseContactMeters));
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
        private static readonly FireControlIntentProducer Instance = new FireControlIntentProducer();

        static FireControlIntentProducer()
        {
            BattleCoreTick.RegisterProducer(Instance);
        }

        internal static void Register() { }

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

                    for (int u = 0; u < s.OwnCount; u++)
                    {
                        if (!s.IsAncestorOf(g, u)) continue;
                        ProduceForUnit(s, t, executor, g, u, posture.State, sink);
                    }
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
                mounted: false,
                state.Task,
                state.Role,
                hasTarget && distance > 0f,
                distance,
                effectiveFireRange: 0f,
                unit.Ammo01,
                unit.Morale,
                unit.Fatigue,
                inCover: false,
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
