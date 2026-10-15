using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AIBattle.AdjustGroupFormations chooses a group formation after
    // stance updates. This Postfix applies the active operations-ledger posture
    // executor after vanilla, using only vanilla formation/waypoint/stance APIs.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupFormations")]
    internal static class BattleCommandPostureExecutorPatch
    {
        // Doctrine timers run on the doctrine clock (TacticalBattleCoordinator.
        // DoctrineBattleHours: battle hours, frozen overnight). Former wall-clock
        // values were tuned at 1x and convert 1:1 at 1x.
        private static readonly float RecentOrderHours = TacticalBattleClock.RealSecondsAtOneXToHours(30f);
        private const float TelemetrySeconds = 30f;
        private const float ObjectiveApproachStandOff = 75f;
        private const float AssemblyStandOff = 200f;
        private const float ReserveStandOff = 350f;
        private const float FallbackStandOff = 250f;
        private const float MaxConservativeWaypointDistance = 2500f;
        // Bumped 15f → 60f on 2026-05-21 to fix unit "microshift" pattern at high
        // compression: probe targets drift by 20-50m as units move; with the prior
        // 15m tolerance the executor re-issued SetWaypoint every cycle, triggering a
        // vanilla pathfinder recompute that visibly reversed the unit's direction
        // briefly. 60m covers typical probe drift while still letting a genuine
        // 100m+ relocation bypass dedup.
        private const float MinWaypointDistance = 60f;
        private const float VanillaBlockedMoveDistance = 150f;
        private const float FacingRefreshToleranceDegrees = 15f;
        // CommandDoctrineOrder.CourierIntervalSeconds is battle seconds: 900 = 0.25 game-hours.
        private const float OutboundCourierIntervalSeconds = 900f;
        private static readonly float FireControlOrderCooldownHours = TacticalBattleClock.RealSecondsAtOneXToHours(45f);
        private static readonly float BrigadeFacingPulseCooldownHours = TacticalBattleClock.RealSecondsAtOneXToHours(12f);
        private static readonly float RegimentFacingPulseCooldownHours = TacticalBattleClock.RealSecondsAtOneXToHours(5f);
        private const int MaxRegimentFacingPulseWritesPerGroup = 4;

        private static readonly Dictionary<int, float> _lastExecutorOrderAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _lastFacingPulseAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _lastFireControlAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _lastFormationWriteAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _writeBackoffUntil = new Dictionary<int, float>();
        private static readonly TacticalFriendlySpatialGrid _friendlyGrid = new TacticalFriendlySpatialGrid();
        private static int _friendlyGridAlliance = -1;
        // Up to 4 nav plans (12 NavMesh.CalculatePath) per Postfix call.
        private static readonly NavPlanBudget _navBudget = new NavPlanBudget(4);
        private static bool _navPlanDeferred;
        private static FieldInfo _bfsField;
        private static readonly Dictionary<string, float> _lastCourierAt = new Dictionary<string, float>();
        private static readonly Dictionary<string, string> _lastOutboundOrderSignatureByGroup = new Dictionary<string, string>();
        private static readonly Dictionary<string, float> _lastTelemetryAt = new Dictionary<string, float>();

        private static FieldInfo _stateField;
        private static FieldInfo _macroAiField;
        private static FieldInfo _isPlayerAiOrFeudField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _blockedCrossingsField;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(AIBattle __instance)
        {
            using (TelemetryPerf.Scope("tactical.patch.posture-executor", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                if (!EnabledForWrites() || __instance == null) return;

                try
                {
                    OnceLog.Info("tactical-command-posture-executor", "BattleCommandPostureExecutorPatch wired");
                    Apply(__instance);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "tactical-command-posture-executor:failed",
                        "BattleCommandPostureExecutorPatch failed: " + ex.Message);
                }
            }
        }

        private static void Apply(AIBattle battle)
        {
            _supportMainPositionsByAlliance.Clear();
            _friendlyGridAlliance = -1;

            int state = SafeIntField(battle, ref _stateField, "state", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int isPlayerAiOrFeud = SafeIntField(battle, ref _isPlayerAiOrFeudField, "isplayeraiorfeud", -1);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");

            if (side < 0 || bunits == null || units == null) return;
            // B-macro3: stand down for vanilla army retreat (macroai 3), the
            // end-of-day phase, and the W&L feud instance.
            if (!PostureExecutorBattleGate.Allows(state, macro, SafeEodCycle(bunits), isPlayerAiOrFeud, GameVars.ai_vs_ai, out string gateReason))
            {
                if (gateReason == "vanilla-retreat") TacticalStanceWriter.ReleaseForRetreat(battle);
                return;
            }

            Dictionary<string, TacticalDivisionPlayOrder> runtimePlayOrders = BuildRuntimeDivisionPlayOrders(bunits, units);
            var iteratedInstanceIds = new HashSet<int>();
            // Start where the NavMesh budget stopped last time so groups the
            // budget deferred are served first (resumable queue).
            int start = _navBudget.StartIndex(units.Count);
            for (int n = 0; n < units.Count; n++)
            {
                var group = units[(start + n) % units.Count] as Regiment;
                if (!IsEligibleCommandGroup(group)) continue;
                int unitInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                if (unitInstanceId != 0) iteratedInstanceIds.Add(unitInstanceId);
                TryApplyGroup(battle, bunits, side, group, runtimePlayOrders);
            }
            _navBudget.NextApply(units.Count);

            // Second pass: walk leaf brigades nested inside divisions that
            // vanilla excluded from unitsused. These are the Union-AI-doesn't-
            // move case — the orchestrator assigns a role to the parent division
            // (which appears in unitsused) but never to the brigades nested
            // under it. This pass uses the depth-agnostic TacticalLeafBrigadeMap
            // cascade to derive per-leaf-brigade tasks from the parent division's
            // role and writes posture per leaf.
            TryApplyNestedLeafBrigades(battle, bunits, side, iteratedInstanceIds, runtimePlayOrders);
        }

        private static void TryApplyGroup(
            AIBattle battle,
            BattleUnits bunits,
            int side,
            Regiment group,
            IReadOnlyDictionary<string, TacticalDivisionPlayOrder> runtimePlayOrders)
        {
            if (!TryResolveLedgerState(group, out CommandNodeOperationalState state, out TacticalBattleOrchestrator orchestrator))
                return;

            int instanceId = SafeInstanceId(group);
            bool hasDoctrineOrder = TryResolveDoctrineOrder(group, orchestrator?.Army, state.NodeId, out CommandDoctrineOrder doctrineOrder);
            if (runtimePlayOrders != null &&
                runtimePlayOrders.TryGetValue(state.NodeId, out TacticalDivisionPlayOrder playOrder) &&
                playOrder.HasOrder)
            {
                state = new CommandNodeOperationalState(
                    state.NodeId,
                    state.Echelon,
                    state.Role,
                    playOrder.Task,
                    state.TaskState);
                if (hasDoctrineOrder)
                {
                    doctrineOrder = doctrineOrder
                        .WithTask(playOrder.Task, playOrder.Reason)
                        .WithDelivery(playOrder.Delivery, playOrder.ParentNodeId, OutboundCourierIntervalSeconds, playOrder.Reason);
                }
            }
            if (hasDoctrineOrder && doctrineOrder.Task != state.Task)
            {
                state = new CommandNodeOperationalState(
                    state.NodeId,
                    state.Echelon,
                    doctrineOrder.Role,
                    doctrineOrder.Task,
                    state.TaskState);
            }

            bool playerProtected = IsPlayerProtected(group);
            bool routed = SafeRouted(group);
            bool hasCavalryCapability = HasCavalryCapability(group);
            bool hasFowVisibleEnemy = HasFowVisibleEnemy(group);
            bool underRecentFire = HasRecentReceivedFire(group);
            if (TryApplyGrandTacticianReconDoctrine(
                    state,
                    doctrineOrder,
                    hasDoctrineOrder,
                    hasCavalryCapability,
                    hasFowVisibleEnemy,
                    underRecentFire,
                    out CommandNodeOperationalState reconState,
                    out CommandDoctrineOrder reconOrder,
                    out bool reconHasDoctrineOrder))
            {
                state = reconState;
                doctrineOrder = reconOrder;
                hasDoctrineOrder = reconHasDoctrineOrder;
            }

            if (TryApplyCavalryFollowMode(group, state, hasDoctrineOrder, doctrineOrder, playerProtected,
                    hasCavalryCapability,
                    out CommandNodeOperationalState cavalryState,
                    out CommandDoctrineOrder cavalryOrder,
                    out bool cavalryHasDoctrineOrder))
            {
                state = cavalryState;
                doctrineOrder = cavalryOrder;
                hasDoctrineOrder = cavalryHasDoctrineOrder;
            }

            var physical = BuildPhysicalState(group, playerProtected, routed);
            TacticalIdleClassification idle = TacticalCommandMonitor.ClassifyIdle(state, physical);
            bool closeEngaged = HasCloseEngagement(group);
            bool flankRisk = HasLocalFlankRisk(group);
            CommandTaskType effectiveTask = CommandFormationCorrection.TaskForLocalFlankEmergency(
                state.Task,
                closeEngaged,
                flankRisk);
            if (effectiveTask != state.Task)
            {
                state = new CommandNodeOperationalState(
                    state.NodeId,
                    state.Echelon,
                    state.Role,
                    effectiveTask,
                    state.TaskState);
            }

            // Army opportunity override (2026-05-21). Same logic the leaf-cascade
            // path already uses, but applied at the BRIGADE-POSTURE layer too —
            // smoke revealed that brigades coming through TryApplyGroup (vs
            // TryApplyLeafBrigade) bypassed the override, so half the army got
            // FallBackToLine on Withdrawal opportunity while the other half kept
            // its playbook Probe/Screen/AttackObjective task, producing crossing
            // paths ("H formation") and "indecisive" appearance for brigades
            // pulled between opportunity-aware and opportunity-blind paths.
            //
            // Brigade level doesn't carry a DirectChildRole; we pass Main as the
            // safe default. WithdrawalToFightLater / DefensiveHold / WaitAndConsolidate
            // branches don't read role; the AttackNow branch's upgrade is safe on
            // any brigade because we only upgrade Hold/Consolidate to AttackObjective
            // (not other defensive tasks).
            ReinforcementOpportunity opportunityForBrigade = ReinforcementOpportunity.NoOpportunity;
            try
            {
                if (orchestrator?.Army != null)
                    opportunityForBrigade = orchestrator.Army.CurrentReinforcementOpportunity.Opportunity;
            }
            catch { /* default NoOpportunity preserves prior behavior */ }
            string brigadeOverrideReason;
            CommandTaskType brigadeOverriddenTask = TacticalOpportunityTaskOverride.Override(
                state.Task,
                WhiskeyRealism.Tactical.Orchestrator.DirectChildRole.Main,
                opportunityForBrigade,
                out brigadeOverrideReason);
            if (brigadeOverriddenTask != state.Task && brigadeOverrideReason != null)
            {
                OnceLog.Info(
                    "tactical-brigade-posture:opportunity-override:" + side + ":" + opportunityForBrigade,
                    "[TacticalBrigadePosture] opportunity override: " + state.Task
                    + " → " + brigadeOverriddenTask + " (opportunity=" + opportunityForBrigade
                    + " reason=" + brigadeOverrideReason + " unit=" + SafeName(group) + ")");
                state = new CommandNodeOperationalState(
                    state.NodeId,
                    state.Echelon,
                    state.Role,
                    brigadeOverriddenTask,
                    state.TaskState);
                // Also override the doctrine-order task so target resolution
                // downstream uses the new task. Mirrors the leaf-cascade fix.
                if (hasDoctrineOrder)
                {
                    doctrineOrder = doctrineOrder.WithTask(brigadeOverriddenTask, brigadeOverrideReason);
                }
            }

            TacticalRefuseFlankIntent.Decision refusedFlank = ResolveRefusedFlank(group, state.Task, closeEngaged, flankRisk);
            int targetFormation = TargetFormationForTask(state.Task, group);
            bool allowStaleQueuedBypass = CanBypassStaleQueuedOrder(state.Task);
            // R8: no pending-order bypass and no shortened cooldown in contact —
            // the old 5 s "urgent" retry re-laid engaged brigades repeatedly.
            bool orderPending = HasPendingOrder(group, allowStaleQueuedBypass);
            float recentOrderHours = RecentOrderHours;
            bool recentOrder = CommandPostureExecutor.ShouldBlockForRecentOrder(
                state.Task,
                physical,
                HasRecentExecutorOrder(instanceId, recentOrderHours));
            bool alreadyCorrect = IsAlreadyDoingCorrectTask(group, state, physical, targetFormation, idle);
            bool pendingOrderOnChild = HasPendingOrder(group);

            var eligibility = new WriteEligibilitySnapshot(
                modeAllowsWrites: true,
                playerProtected: playerProtected,
                routed: routed,
                orderPending: orderPending,
                recentOrder: recentOrder,
                alreadyDoingCorrectTask: alreadyCorrect,
                atAssignedLocation: idle == TacticalIdleClassification.ValidIdle,
                missingLedgerAssignment: false,
                closeEngaged: closeEngaged);

            if (hasDoctrineOrder &&
                doctrineOrder.Delivery == TacticalOrderDelivery.Courier &&
                !AllowsOutboundCourier(side, group, doctrineOrder, playerProtected, pendingOrderOnChild, physical.ActiveMove, out TacticalOutboundCourierDecision courierDecision))
            {
                EmitPostureTelemetry(
                    side,
                    group,
                    state,
                    new PostureExecutionDecision(PostureExecutionAction.NoWrite, "courier-" + courierDecision.Reason),
                    idle,
                    applied: false,
                    extraReason: "courier-" + courierDecision.Reason);
                return;
            }

            // M3: a write that failed to materialize backs off on battle time.
            if (_writeBackoffUntil.TryGetValue(instanceId, out float backoffUntil) &&
                CommandWaypointWritePolicy.IsBackedOff(BattleHours(), backoffUntil))
            {
                EmitPostureTelemetry(side, group, state,
                    new PostureExecutionDecision(PostureExecutionAction.NoWrite, "write-backoff"),
                    idle, applied: false, extraReason: "write-backoff");
                return;
            }

            PostureExecutionDecision decision;
            using (TelemetryPerf.Scope("tactical.patch.posture-executor.decide", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                decision = hasDoctrineOrder
                    ? CommandPostureExecutor.Decide(doctrineOrder, physical, eligibility, BattleHours())
                    : CommandPostureExecutor.Decide(state, physical, eligibility);
            }
            if (decision.Action == PostureExecutionAction.NoWrite)
            {
                if (TryApplyContactFacingPulse(bunits, side, group, state, closeEngaged, flankRisk))
                {
                    _lastExecutorOrderAt[instanceId] = BattleHours();
                    EmitPostureTelemetry(side, group, state, decision, idle, applied: true, extraReason: "contact-facing-pulse");
                    return;
                }

                if (CanWrite(group, state.Task, eligibility, physical, recentOrderHours, allowStaleQueuedBypass) &&
                    TryApplyFireControl(bunits, group, state, out TacticalFireControlDecision fireDecision))
                {
                    _lastExecutorOrderAt[instanceId] = BattleHours();
                    EmitPostureTelemetry(side, group, state, decision, idle, applied: true, extraReason: "fire-control-" + fireDecision.Reason);
                    return;
                }

                EmitPostureTelemetry(side, group, state, decision, idle, applied: false, extraReason: decision.Reason);
                return;
            }

            if (!CanWrite(group, state.Task, eligibility, physical, recentOrderHours, allowStaleQueuedBypass))
            {
                EmitPostureTelemetry(side, group, state, decision, idle, applied: false, extraReason: "write-gate-denied");
                return;
            }

            bool hasTarget = TryResolveTarget(group, orchestrator, state, doctrineOrder, decision.Target, out Vector3 target);
            if (!hasTarget && _navPlanDeferred)
            {
                EmitPostureTelemetry(side, group, state, decision, idle, applied: false, extraReason: "nav-budget-deferred");
                return;
            }
            string outboundSignature = OutboundOrderSignature(state, decision, targetFormation, hasTarget, target);
            if (!AllowsOutboundOrderLedger(
                    group,
                    state,
                    outboundSignature,
                    pendingOrderOnChild,
                    physical.ActiveMove,
                    out TacticalOutboundOrderLedgerDecision outboundDecision))
            {
                string reason = "outbound-" + outboundDecision.Reason;
                EmitPostureTelemetry(
                    side,
                    group,
                    state,
                    new PostureExecutionDecision(PostureExecutionAction.NoWrite, reason),
                    idle,
                    applied: false,
                    extraReason: reason);
                return;
            }

            bool wrote = ApplyDecision(
                battle,
                bunits,
                group,
                state,
                decision,
                targetFormation,
                eligibility.CloseEngaged,
                refusedFlank,
                hasTarget,
                target);

            if (wrote)
            {
                _lastExecutorOrderAt[instanceId] = BattleHours();
                RecordOutboundOrder(group, state, outboundSignature);
                if (hasDoctrineOrder && doctrineOrder.Delivery == TacticalOrderDelivery.Courier)
                    _lastCourierAt[CourierChildKey(side, doctrineOrder)] = BattleHours();
                _writeBackoffUntil.Remove(instanceId);
                EmitPostureTelemetry(side, group, state, decision, idle, applied: true, extraReason: decision.Reason);
            }
            else
            {
                _writeBackoffUntil[instanceId] = CommandWaypointWritePolicy.FailedWriteBackoffUntil(BattleHours());
                EmitPostureTelemetry(side, group, state, decision, idle, applied: false, extraReason: hasTarget ? "movement-unmaterialized" : "target-unresolved");
            }
        }

        private sealed class RuntimePlayChild
        {
            public RuntimePlayChild(string parentNodeId, TacticalDivisionPlaySubordinate subordinate)
            {
                ParentNodeId = parentNodeId;
                Subordinate = subordinate;
            }

            public string ParentNodeId { get; }
            public TacticalDivisionPlaySubordinate Subordinate { get; }
        }

        private static Dictionary<string, TacticalDivisionPlayOrder> BuildRuntimeDivisionPlayOrders(BattleUnits bunits, IList units)
        {
            var result = new Dictionary<string, TacticalDivisionPlayOrder>();
            if (units == null) return result;

            var byParent = new Dictionary<string, List<RuntimePlayChild>>();
            var defendingByAlliance = new Dictionary<int, bool>();
            var allianceByParent = new Dictionary<string, int>();
            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (!IsEligibleCommandGroup(group)) continue;
                // A W&L-protected group neither receives a play order nor
                // anchors one for its AI siblings.
                if (IsPlayerProtected(group)) continue;
                if (!TryResolveLedgerState(group, out CommandNodeOperationalState state, out _)) continue;

                // Vanilla command parent; groups directly under the units root
                // have no division to play with (B-divplay).
                string parentKey = ParentNodeKey(group);
                if (string.IsNullOrWhiteSpace(parentKey)) continue;
                if (!byParent.TryGetValue(parentKey, out List<RuntimePlayChild> children))
                {
                    children = new List<RuntimePlayChild>();
                    byParent[parentKey] = children;
                    allianceByParent[parentKey] = group.alliance;
                }

                children.Add(new RuntimePlayChild(
                    parentKey,
                    new TacticalDivisionPlaySubordinate(
                        state.NodeId,
                        state.Role,
                        hasTargets: HasVisibleTarget(group),
                        engagingEnemy: HasCloseEngagement(group),
                        underCloseFire: HasCloseEngagement(group) || HasLocalFlankRisk(group),
                        inTrouble: HasLocalFlankRisk(group) || SafeMorale(group) < 0.35f,
                        isArtillery: HasArtilleryCapability(group),
                        enemyDistance: SafeEnemyDistance(group),
                        hasOrders: HasPendingOrder(group) || HasActiveMoveMakingProgress(group),
                        currentTask: state.Task)));
                if (!defendingByAlliance.ContainsKey(group.alliance))
                    defendingByAlliance[group.alliance] = SideDefending(bunits, group.alliance);
            }

            foreach (var pair in byParent)
            {
                if (pair.Value.Count < 2) continue;
                var subordinates = new TacticalDivisionPlaySubordinate[pair.Value.Count];
                for (int i = 0; i < pair.Value.Count; i++) subordinates[i] = pair.Value[i].Subordinate;

                bool defending = allianceByParent.TryGetValue(pair.Key, out int parentAlliance) &&
                    defendingByAlliance.TryGetValue(parentAlliance, out bool d) && d;
                TacticalDivisionPlayDecision decision = TacticalDivisionPlayExecutor.Decide(
                    new TacticalDivisionPlayInput(pair.Key, subordinates, defending));
                if (!decision.HasAnchor) continue;

                for (int i = 0; i < decision.Orders.Count; i++)
                {
                    TacticalDivisionPlayOrder order = decision.Orders[i];
                    if (order.HasOrder) result[order.NodeId] = order;
                }
            }

            return result;
        }

        private static bool TryApplyCavalryFollowMode(
            Regiment group,
            CommandNodeOperationalState state,
            bool hasDoctrineOrder,
            CommandDoctrineOrder doctrineOrder,
            bool playerProtected,
            bool hasCavalryCapability,
            out CommandNodeOperationalState updatedState,
            out CommandDoctrineOrder updatedOrder,
            out bool updatedHasDoctrineOrder)
        {
            updatedState = state;
            updatedOrder = doctrineOrder;
            updatedHasDoctrineOrder = hasDoctrineOrder;

            if (!hasCavalryCapability || playerProtected) return false;

            TacticalCavalryFollowMode mode = CavalryFollowModeFor(state.Role, state.Task);
            if (mode == TacticalCavalryFollowMode.None) return false;

            Regiment target = ClosestEnemy(group);
            float enemyDistance = SafeEnemyDistance(group);
            TacticalCavalryFollowDecision follow = TacticalCavalryFollowDoctrine.Decide(
                new TacticalCavalryFollowInput(
                    mode,
                    hasFollowTarget: target != null,
                    targetHidden: false,
                    targetIsOfficer: target != null && target.unittyp == TacticalUnitType.Officer,
                    targetInFort: TargetInFort(target),
                    targetInSquare: false,
                    targetHasInfantryOrArtillerySupport: TargetHasCloseSupport(target),
                    canChargeTarget: target != null && enemyDistance <= 350f,
                    leaderAllowsCharge: !playerProtected,
                    fearAdvantage01: CavalryFearAdvantage(group),
                    enemyClose: enemyDistance > 0f && enemyDistance <= 350f,
                    enemyDistance: enemyDistance,
                    longRange: Math.Max(300f, SafeFireRange(group)),
                    atFollowLocation: false));

            CommandTaskType task = state.Task;
            switch (follow.Action)
            {
                case TacticalCavalryFollowAction.GetAway:
                    task = CommandTaskType.FallBackToLine;
                    break;
                case TacticalCavalryFollowAction.ChargeRaidTarget:
                    task = CommandTaskType.AttackObjective;
                    break;
                case TacticalCavalryFollowAction.MoveBehindTarget:
                    task = CommandTaskType.GuardFlank;
                    break;
                case TacticalCavalryFollowAction.RequestScoutLocation:
                    task = mode == TacticalCavalryFollowMode.Screen ? CommandTaskType.Screen : CommandTaskType.Scout;
                    break;
                case TacticalCavalryFollowAction.ClearFollowTarget:
                    task = CommandTaskType.Screen;
                    break;
                default:
                    return false;
            }

            if (task == state.Task) return false;

            updatedState = new CommandNodeOperationalState(
                state.NodeId,
                state.Echelon,
                state.Role,
                task,
                state.TaskState);
            if (hasDoctrineOrder)
            {
                updatedOrder = doctrineOrder
                    .WithTask(task, follow.Reason)
                    .WithDelivery(TacticalOrderDelivery.Courier, ParentNodeKey(group), OutboundCourierIntervalSeconds, follow.Reason);
            }

            return true;
        }

        private static bool TryApplyGrandTacticianReconDoctrine(
            CommandNodeOperationalState state,
            CommandDoctrineOrder doctrineOrder,
            bool hasDoctrineOrder,
            bool hasCavalryCapability,
            bool hasFowVisibleEnemy,
            bool underRecentFire,
            out CommandNodeOperationalState updatedState,
            out CommandDoctrineOrder updatedOrder,
            out bool updatedHasDoctrineOrder)
        {
            updatedState = state;
            updatedOrder = doctrineOrder;
            updatedHasDoctrineOrder = hasDoctrineOrder;

            TacticalGrandTacticianReconDecision recon = TacticalGrandTacticianReconDoctrine.Decide(
                new TacticalGrandTacticianReconInput(
                    state.Role,
                    state.Task,
                    hasCavalryCapability,
                    hasFowVisibleEnemy,
                    underRecentFire));

            if (recon.Task == state.Task) return false;

            updatedState = WithTask(state, recon.Task);
            if (hasDoctrineOrder)
            {
                updatedOrder = doctrineOrder
                    .WithTask(recon.Task, recon.Reason)
                    .WithDelivery(TacticalOrderDelivery.Courier, state.NodeId, OutboundCourierIntervalSeconds, recon.Reason);
            }

            return true;
        }

        private static CommandNodeOperationalState WithTask(CommandNodeOperationalState state, CommandTaskType task)
        {
            return new CommandNodeOperationalState(
                state.NodeId,
                state.Echelon,
                state.Role,
                task,
                state.TaskState,
                state.X,
                state.Z,
                state.FacingDegrees);
        }

        private static bool AllowsOutboundCourier(
            int side,
            Regiment group,
            CommandDoctrineOrder order,
            bool playerProtected,
            bool pendingOrderOnChild,
            bool childActiveMove,
            out TacticalOutboundCourierDecision decision)
        {
            // Courier cadence per child on battle time (B-61clock): a parent
            // sends to each subordinate on its own interval instead of one
            // courier per parent starving the siblings.
            _lastCourierAt.TryGetValue(CourierChildKey(side, order), out float lastCourierAt);
            decision = TacticalOutboundCourierCadence.Decide(
                new TacticalOutboundCourierInput(
                    CourierParentKey(side, order),
                    order.NodeId,
                    BattleHours(),
                    lastCourierAt,
                    pendingOrderOnChild,
                    commanderHasOrder: order.HasPurpose,
                    commanderHasPlay: order.Task != CommandTaskType.None,
                    childIsPlayerControlled: playerProtected,
                    courierIntervalSeconds: order.CourierIntervalSeconds / 3600f,
                    allowObjectiveMovementContinuation: IsCourierObjectiveContinuationTask(order.Task) && order.HasConcreteMovementTarget,
                    childActiveMove: childActiveMove));
            return decision.AllowIssue;
        }

        private static bool IsCourierObjectiveContinuationTask(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.Scout:
                case CommandTaskType.Probe:
                case CommandTaskType.Screen:
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                    return true;
                default:
                    return false;
            }
        }

        private static bool AllowsOutboundOrderLedger(
            Regiment group,
            CommandNodeOperationalState state,
            string desiredSignature,
            bool childHasOrders,
            bool activeMove,
            out TacticalOutboundOrderLedgerDecision decision)
        {
            string key = OutboundOrderKey(group, state);
            string previous = null;
            if (!string.IsNullOrWhiteSpace(key))
                _lastOutboundOrderSignatureByGroup.TryGetValue(key, out previous);

            bool hasPrevious = !string.IsNullOrWhiteSpace(previous);
            bool duplicate = hasPrevious && string.Equals(previous, desiredSignature, StringComparison.Ordinal);
            bool changed = hasPrevious && !duplicate;
            decision = TacticalOutboundOrderLedger.Decide(
                new TacticalOutboundOrderLedgerInput(
                    childHasOrders,
                    duplicate,
                    activeMove,
                    changed));
            return decision.AllowIssue;
        }

        private static void RecordOutboundOrder(Regiment group, CommandNodeOperationalState state, string signature)
        {
            string key = OutboundOrderKey(group, state);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(signature)) return;
            _lastOutboundOrderSignatureByGroup[key] = signature;
        }

        private static string OutboundOrderKey(Regiment group, CommandNodeOperationalState state)
        {
            int instanceId = SafeInstanceId(group);
            string nodeId = string.IsNullOrWhiteSpace(state.NodeId) ? "node-unknown" : state.NodeId.Trim();
            return instanceId + "|" + nodeId;
        }

        private static string OutboundOrderSignature(
            CommandNodeOperationalState state,
            PostureExecutionDecision decision,
            int targetFormation,
            bool hasTarget,
            Vector3 target)
        {
            return TacticalOperationsTelemetry.SafeToken(state.NodeId) +
                "|" + state.Role +
                "|" + state.Task +
                "|" + decision.Action +
                "|" + decision.Target +
                "|formation-" + targetFormation +
                "|" + TargetSignature(hasTarget, target);
        }

        private static string TargetSignature(bool hasTarget, Vector3 target)
        {
            if (!hasTarget || IsDefaultVector(target)) return "target-none";
            return "target-" + BucketCoordinate(target.x) + "-" + BucketCoordinate(target.z);
        }

        private static int BucketCoordinate(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            return (int)Math.Round(value / 10f, MidpointRounding.AwayFromZero);
        }

        private static string CourierChildKey(int side, CommandDoctrineOrder order)
        {
            return "side-" + side + ":child:" + (string.IsNullOrWhiteSpace(order.NodeId) ? "unknown" : order.NodeId);
        }

        private static string CourierParentKey(int side, CommandDoctrineOrder order)
        {
            if (!string.IsNullOrWhiteSpace(order.ParentNodeId)) return order.ParentNodeId;
            return "side-" + side + ":parent-unknown";
        }

        private static bool ApplyDecision(
            AIBattle battle,
            BattleUnits bunits,
            Regiment group,
            CommandNodeOperationalState state,
            PostureExecutionDecision decision,
            int targetFormation,
            bool closeEngaged,
            TacticalRefuseFlankIntent.Decision refusedFlank,
            bool hasTarget,
            Vector3 target)
        {
            switch (decision.Action)
            {
                case PostureExecutionAction.SetFormation:
                    return SetFormation(bunits, group, state.Task, targetFormation, closeEngaged, refusedFlank);
                case PostureExecutionAction.SetFormationAndWaypoint:
                    if (!hasTarget) return false;
                    // Vanilla never re-forms a group that has a path
                    // (AdjustGroupFormations 5886); a courier formation order
                    // delivered mid-move re-lays the group at its current
                    // position (SetGroupFormation newpath, 91884) and cancels
                    // the move. Move first; re-form only when no move went out.
                    if (SetWaypoint(battle, bunits, group, state.Task, target)) return true;
                    return SetFormation(bunits, group, state.Task, targetFormation, closeEngaged, refusedFlank);
                case PostureExecutionAction.SetWaypoint:
                case PostureExecutionAction.ReleaseReserve:
                case PostureExecutionAction.FallbackToLine:
                case PostureExecutionAction.RecoverInterruptedOrder:
                    return hasTarget && SetWaypoint(battle, bunits, group, state.Task, target);
                case PostureExecutionAction.ChangeStance:
                    return TacticalStanceWriter.EnsureStanceForTask(bunits, group, state.Task, null, StanceWriteSource.Executor);
                default:
                    return false;
            }
        }

        // R8: formation/facing writes follow CommandFormationWritePolicy —
        // never a re-lay in contact, vanilla SetFormation (courier) for real
        // formation changes, battle-time cooldown that is longer under fire.
        private static bool SetFormation(
            BattleUnits bunits,
            Regiment group,
            CommandTaskType task,
            int targetFormation,
            bool closeEngaged,
            TacticalRefuseFlankIntent.Decision refusedFlank)
        {
            if (!CanUseGroupFormation(group)) return false;
            if (targetFormation < 0 || targetFormation > 4) return false;

            bool needsFormation = CommandFormationCorrection.NeedsCorrection(
                SafeFormation(group),
                SafeFormationOrdered(group),
                SafeGroupFormation(group),
                targetFormation);
            bool hasThreatFacing = TryThreatFacingRotation(group, task, out float manualFinalRotation);
            bool needsFacing = hasThreatFacing &&
                CommandFormationCorrection.NeedsFacingCorrection(
                    SafeRotationY(group),
                    manualFinalRotation,
                    FacingRefreshToleranceDegrees);
            int refuseFlankParameter = CommandFormationCorrection.RefuseFlankParameter(
                refusedFlank,
                task,
                closeEngaged);

            int instanceId = SafeInstanceId(group);
            float now = BattleHours();
            float last = _lastFormationWriteAt.TryGetValue(instanceId, out float l) ? l : -1f;
            FormationWriteDecision decision;
            try
            {
                decision = CommandFormationWritePolicy.Decide(new FormationWriteInput(
                    needsFormation,
                    needsFacing,
                    refuseFlankParameter >= 0,
                    group.combatstatussubordinates,
                    group.groupsubordinatesmoving,
                    group.regimentpaths,
                    group.covervaluesubordinates,
                    now,
                    last,
                    group.groupenemiesinrange));
            }
            catch
            {
                return false;
            }

            bool wrote = false;
            switch (decision.Kind)
            {
                case FormationWriteKind.RotateInPlace:
                    wrote = RotateRegimentsInPlace(group, manualFinalRotation) > 0;
                    break;
                case FormationWriteKind.WheelGroup:
                {
                    int current = SafeGroupFormation(group);
                    bunits.SetGroupFormation(
                        group,
                        current >= 0 && current <= 4 ? current : targetFormation,
                        manualfinalrotation: hasThreatFacing ? manualFinalRotation : -1f,
                        targetpos: default(Vector3),
                        immediateplacement: false,
                        newpath: false,
                        modifylastwaypoint: true,
                        newstate: 2,
                        refuseflank: refuseFlankParameter,
                        ignoredeplyomentzone: false,
                        skiprotation: false,
                        showmovementoptions: false,
                        placeentrenchments: false,
                        adjustbyterrainshape: true);
                    wrote = true;
                    break;
                }
                case FormationWriteKind.VanillaFormation:
                {
                    int index = CommandFormationWritePolicy.VanillaSetFormationIndexForGroup(targetFormation);
                    GameObject gameObject = UnityObject(group);
                    if (index < 0 || gameObject == null) return false;
                    bunits.SetFormation(gameObject, index, exitifsameformation: true);
                    wrote = true;
                    break;
                }
            }

            if (wrote) _lastFormationWriteAt[instanceId] = now;
            return wrote;
        }

        // Rotate each settled combat regiment of the group in place, the way
        // vanilla turns a unit toward an enemy (UnlimberArtilleryAIMicro 3491:
        // RotateRegiment). No ClearTargets, no new slots. Artillery is left to
        // vanilla (RotateRegiment re-applies lastformation, which may unlimber
        // or limber a battery, 135440-135452).
        private static int RotateRegimentsInPlace(Regiment group, float rotation)
        {
            int rotated = 0;
            try
            {
                Regiment[] units = group.allattachedunits;
                if (units == null) return 0;
                for (int i = 0; i < units.Length; i++)
                {
                    Regiment unit = units[i];
                    if (unit == null || unit.unittyp > TacticalUnitType.MaxCombat) continue;
                    if (unit.unittyp == TacticalUnitType.Artillery) continue;
                    if (unit.isrouted || unit.markedforrout || unit.permanentlydetached) continue;
                    if (unit.regimentpaths > 0 || unit.withinrotationprocess || unit.movementmode == 3) continue;
                    if ((UnityEngine.Object)unit.chargetarget != null) continue;
                    GameObject gameObject = UnityObject(unit);
                    if (gameObject == null || !gameObject.activeInHierarchy) continue;
                    if (!CommandFormationCorrection.NeedsFacingCorrection(SafeRotationY(unit), rotation, FacingRefreshToleranceDegrees)) continue;
                    unit.RotateRegiment(rotation);
                    rotated++;
                }
            }
            catch { }

            return rotated;
        }

        private static bool TryThreatFacingRotation(
            Regiment group,
            CommandTaskType task,
            out float manualFinalRotation)
        {
            manualFinalRotation = -1f;
            if (!CommandFormationCorrection.ShouldFaceThreat(task)) return false;

            return TryThreatFacingRotation(group, out manualFinalRotation);
        }

        private static bool TryThreatFacingRotation(
            Regiment group,
            out float manualFinalRotation)
        {
            manualFinalRotation = -1f;

            try
            {
                Regiment enemy = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
                if (enemy == null) return false;

                Vector3 own = SafePosition(group);
                Vector3 enemyPosition = SafePosition(enemy);
                if (IsDefaultVector(own) || IsDefaultVector(enemyPosition)) return false;

                BattlefieldSetup bfs = SafeBattlefieldSetup();
                manualFinalRotation = bfs != null
                    ? bfs.GetAngleTerrain(own, enemyPosition) + 180f
                    : CommandFormationCorrection.ThreatFacingRotationDegrees(own.x, own.z, enemyPosition.x, enemyPosition.z);
                return !float.IsNaN(manualFinalRotation) && !float.IsInfinity(manualFinalRotation);
            }
            catch
            {
                manualFinalRotation = -1f;
                return false;
            }
        }

        private static bool SetWaypoint(AIBattle battle, BattleUnits bunits, Regiment group, CommandTaskType task, Vector3 target)
        {
            if (!IsSafeWaypoint(group, target)) return false;
            if (ShouldSkipDuplicateWaypoint(group, target)) return false;

            // Brigade task-commitment hold (2026-05-22). Ports Scourge's
            // offai.cpp:343 `if (Orders()) return` semantic: once we've given
            // a brigade a task waypoint, don't re-issue the SAME task for the
            // hold window. Brigade gets to actually execute the order. Task
            // changes (Probe → AttackObjective) bypass the hold so legitimate
            // role transitions still propagate.
            if (ShouldSuppressTaskRefresh(group, task))
            {
                EmitTaskHoldSuppressedTelemetry(group, task);
                return false;
            }

            if (TryQueueBlockedMovingOrder(battle, group, task, target))
            {
                MarkGroupMovementStarted(group, task);
                return true;
            }

            // Scope B refactor (2026-05-22). Plug Whiskey into vanilla/W&L's
            // canonical order pipeline: ChangeStance(mapped_stance) BEFORE
            // SetWaypoint. ChangeStance updates ai_stanceordered (vanilla
            // micro-AI reads this for execution context), manages aibattle[1]
            // pool membership when player-side, and cascades to subordinates
            // through groupaiobject. Doing only SetWaypoint orphans the unit
            // from vanilla's stance-driven decision pipeline — which is why
            // 4th Michigan-style units behaved erratically (movement but no
            // stance context).
            //
            // Map task → vanilla stance. Skip if already at target stance
            // (prevents redundant W&L queue entries).
            EnsureStanceMatchesTask(bunits, group, task);

            int beforeMovingPaths = CountMovingPaths(group);
            int beforeQueued = QueuedOrderCount(group);
            bool useOrderDelay = CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(
                task,
                battleActive: true,
                inContactWithdrawal: HasCloseEngagement(group) || HasLocalFlankRisk(group));

            bunits.SetWaypoint(
                group,
                target,
                newpath: true,
                doublequick: false,
                manualfinalrotation: -1f,
                modifylastwaypoint: false,
                useorderdelay: useOrderDelay,
                timetomove: -1f,
                direction: -1,
                showmovementoptions: false,
                ignorebattlemonuments: false,
                groupmoveonly: false,
                ignoredisabledships: false,
                checkforreadiness: true,
                clearinterruptionpaths: true);

            if (!CommandWaypointWritePolicy.MovementIssued(beforeMovingPaths, CountMovingPaths(group), beforeQueued, QueuedOrderCount(group)))
                return false;

            MarkGroupMovementStarted(group, task);
            RecordTaskWrite(group, task);  // task-hold (2026-05-22)

            return true;
        }

        private static bool TryQueueBlockedMovingOrder(AIBattle battle, Regiment group, CommandTaskType task, Vector3 target)
        {
            try
            {
                if (battle == null || group == null) return false;
                List<Vector3> blockedCrossings = SafeField<List<Vector3>>(battle, ref _blockedCrossingsField, "blockedcrossings");
                float distance = SafeDistance(SafePosition(group), target);
                if (!CommandWaypointWritePolicy.ShouldUseBlockedMovingOrderForExecutorWaypoint(
                        task,
                        distance,
                        VanillaBlockedMoveDistance,
                        blockedCrossings != null,
                        battleActive: true))
                    return false;

                if (AIBattle.BlockedMovingOrder.OrderRunning(group))
                    return true;

                new AIBattle.BlockedMovingOrder(group, blockedCrossings, VanillaBlockedMoveDistance, target);
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-command-posture:blocked-move-failed",
                    "BattleCommandPostureExecutorPatch blocked movement queue failed: " + ex.Message);
                return false;
            }
        }

        // Brigade task-commitment state (2026-05-22). Per-instance (instanceId →
        // last task + timestamp). On SetWaypoint, if the proposed task matches
        // the last-written task within the hold window, suppress. Different
        // task bypasses. Reset between battles via ResetTaskHoldForBattleEnd.
        private static readonly Dictionary<int, (CommandTaskType task, float ts)> _brigadeTaskHoldState
            = new Dictionary<int, (CommandTaskType, float)>();

        internal static void ResetTaskHoldForBattleEnd()
        {
            _brigadeTaskHoldState.Clear();
        }

        // Returns true when the proposed task waypoint should be suppressed
        // because the brigade was given the SAME task recently. Hold window
        // is Plugin.BrigadeTaskHoldSeconds (default 12), read as real seconds
        // at 1x and run on battle time. Set to 0 to disable.
        private static bool ShouldSuppressTaskRefresh(Regiment group, CommandTaskType task)
        {
            try
            {
                float holdSeconds = Plugin.BrigadeTaskHoldSeconds != null
                    ? Plugin.BrigadeTaskHoldSeconds.Value
                    : 12.0f;
                if (holdSeconds <= 0f) return false;
                float hold = TacticalBattleClock.RealSecondsAtOneXToHours(holdSeconds);
                int id = SafeInstanceId(group);
                if (id == 0) return false;
                if (!_brigadeTaskHoldState.TryGetValue(id, out var prior)) return false;
                if (prior.task != task) return false;  // legitimate task change — bypass
                float age = BattleHours() - prior.ts;
                return age >= 0f && age < hold;
            }
            catch { return false; }
        }

        // Records a successful write so subsequent SetWaypoint calls in the
        // hold window are suppressed for the same task. Called from the
        // vanilla-SetWaypoint success path below.
        private static void RecordTaskWrite(Regiment group, CommandTaskType task)
        {
            try
            {
                int id = SafeInstanceId(group);
                if (id == 0) return;
                _brigadeTaskHoldState[id] = (task, BattleHours());
            }
            catch { }
        }

        // Telemetry for suppressed writes — bounded by OnceLog (the same task
        // for the same group during a sustained hold doesn't spam).
        private static void EmitTaskHoldSuppressedTelemetry(Regiment group, CommandTaskType task)
        {
            try
            {
                int id = SafeInstanceId(group);
                OnceLog.Info(
                    "tactical-brigade-task-hold:suppressed:" + task,
                    "[TacticalBrigadeTaskHold] suppressed " + task + " write for "
                    + SafeName(group) + " (id=" + id + ") — within hold window");
            }
            catch { }
        }

        private static void MarkGroupMovementStarted(Regiment group, CommandTaskType task)
        {
            try
            {
                if (group == null) return;
                if (!CommandWaypointWritePolicy.ShouldStampGroupMovementForExecutorWaypoint(task)) return;
                group.groupsubordinatesmoving = 1f;
                group.groupsubordinatesmovingnotfar = 1f;
            }
            catch { }
        }

        // SetWaypoint with order delay queues the move on the command parent
        // (or the unit itself at the Units root) via AddToOrderQueue (91470-91500).
        private static int QueuedOrderCount(Regiment group)
        {
            try
            {
                if (group == null) return 0;
                int count = group.orderqueue != null ? group.orderqueue.Count : 0;
                GameObject self = ((Component)group).gameObject;
                GameObject parentObject = BattleUnits.GrabNextParentUnit(self);
                if (parentObject != null && parentObject != self)
                {
                    Regiment parent = parentObject.GetComponent<Regiment>();
                    if (parent != null && parent.orderqueue != null) count += parent.orderqueue.Count;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static int CountMovingPaths(Regiment group)
        {
            int moving = 0;
            try
            {
                if (group == null) return 0;
                if (group.regimentpaths > 0) moving++;
                if (group.allattachedunits == null) return moving;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null && unit.regimentpaths > 0) moving++;
                }
            }
            catch { }

            return moving;
        }

        private static bool ShouldSkipDuplicateWaypoint(Regiment group, Vector3 target)
        {
            try
            {
                if (group == null) return false;
                Vector3 currentWaypoint = group.lastsetwaypointposition;
                Vector3 currentPosition = SafePosition(group);
                return CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint(
                    currentWaypoint.x,
                    currentWaypoint.z,
                    target.x,
                    target.z,
                    MinWaypointDistance,
                    group.pathinterrupted,
                    CountMovingPaths(group),
                    HasActiveMoveMakingProgress(group),
                    currentPosition.x,
                    currentPosition.z);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryApplyContactFacingPulse(
            BattleUnits bunits,
            int side,
            Regiment group,
            CommandNodeOperationalState state,
            bool closeEngaged,
            bool flankRisk)
        {
            if (!ContactFacingPulseEnabled() || bunits == null || group == null) return false;

            bool wrote = false;
            TacticalFacingPulseScope scope = ScopeFor(state.Echelon);
            if (scope == TacticalFacingPulseScope.Division)
            {
                if (TryEvaluateFacingPulse(group, state.Task, scope, targetFormation: -1, closeEngaged, flankRisk, out TacticalFacingPulseDecision decision, out TacticalFacingThreatSource source))
                    EmitFacingPulseTelemetry(side, group, group, state, scope, decision, source, applied: false);
            }
            else if (scope == TacticalFacingPulseScope.Brigade &&
                     TryEvaluateFacingPulse(group, state.Task, scope, TargetFormationForTask(state.Task, group), closeEngaged, flankRisk, out TacticalFacingPulseDecision decision, out TacticalFacingThreatSource source))
            {
                if (decision.ShouldWrite &&
                    SetFormation(bunits, group, state.Task, decision.TargetFormation, closeEngaged, TacticalRefuseFlankIntent.Decision.NoRefuse))
                {
                    MarkFacingPulse(group);
                    EmitFacingPulseTelemetry(side, group, group, state, scope, decision, source, applied: true);
                    wrote = true;
                }
                else
                {
                    EmitFacingPulseTelemetry(side, group, group, state, scope, decision, source, applied: false);
                }
            }

            int regimentWrites = scope == TacticalFacingPulseScope.Brigade
                ? TryApplyRegimentFacingPulses(
                    side,
                    group,
                    state,
                    closeEngaged,
                    flankRisk)
                : 0;
            return wrote || regimentWrites > 0;
        }

        private static int TryApplyRegimentFacingPulses(
            int side,
            Regiment group,
            CommandNodeOperationalState state,
            bool groupCloseEngaged,
            bool groupFlankRisk)
        {
            int wrote = 0;
            var visited = new HashSet<int>();
            TryApplyRegimentFacingPulse(side, group, group, state, groupCloseEngaged, groupFlankRisk, visited, ref wrote);

            try
            {
                Regiment[] units = group.allattachedunits;
                if (units == null) return wrote;

                for (int i = 0; i < units.Length && wrote < MaxRegimentFacingPulseWritesPerGroup; i++)
                    TryApplyRegimentFacingPulse(side, group, units[i], state, groupCloseEngaged, groupFlankRisk, visited, ref wrote);
            }
            catch { }

            return wrote;
        }

        private static void TryApplyRegimentFacingPulse(
            int side,
            Regiment parent,
            Regiment unit,
            CommandNodeOperationalState state,
            bool groupCloseEngaged,
            bool groupFlankRisk,
            ISet<int> visited,
            ref int wrote)
        {
            if (wrote >= MaxRegimentFacingPulseWritesPerGroup) return;
            if (!IsCombatRegiment(unit)) return;

            int unitId = SafeInstanceId(unit);
            if (unitId == 0 || (visited != null && !visited.Add(unitId))) return;

            bool closeEngaged = groupCloseEngaged || HasCloseEngagement(unit);
            bool flankRisk = groupFlankRisk || HasLocalFlankRisk(unit);
            // R9: regiments only rotate — the current formation is passed as
            // the target so the doctrine never asks for a formation change.
            if (!TryEvaluateFacingPulse(
                    unit,
                    state.Task,
                    TacticalFacingPulseScope.Regiment,
                    targetFormation: SafeFormation(unit),
                    closeEngaged,
                    flankRisk,
                    out TacticalFacingPulseDecision decision,
                    out TacticalFacingThreatSource source))
            {
                return;
            }

            if (decision.ShouldWrite && ApplyRegimentFacingPulse(unit, decision))
            {
                MarkFacingPulse(unit);
                EmitFacingPulseTelemetry(side, parent, unit, state, TacticalFacingPulseScope.Regiment, decision, source, applied: true);
                wrote++;
                return;
            }

            EmitFacingPulseTelemetry(side, parent, unit, state, TacticalFacingPulseScope.Regiment, decision, source, applied: false);
        }

        private static bool TryEvaluateFacingPulse(
            Regiment unit,
            CommandTaskType task,
            TacticalFacingPulseScope scope,
            int targetFormation,
            bool closeEngaged,
            bool flankRisk,
            out TacticalFacingPulseDecision decision,
            out TacticalFacingThreatSource source)
        {
            decision = TacticalFacingPulseDecision.NoWrite(-1f, "not-evaluated");
            source = TacticalFacingThreatSource.None;
            if (unit == null) return false;

            bool hasThreatFacing = TryThreatFacingRotation(unit, out float targetFacing);
            source = hasThreatFacing ? TacticalFacingThreatSource.Visible : TacticalFacingThreatSource.None;
            int instanceId = SafeInstanceId(unit);
            float cooldown = scope == TacticalFacingPulseScope.Regiment
                ? RegimentFacingPulseCooldownHours
                : BrigadeFacingPulseCooldownHours;

            decision = TacticalFacingPulseDoctrine.Decide(new TacticalFacingPulseInput(
                scope,
                task,
                SafeFormation(unit),
                targetFormation,
                SafeRotationY(unit),
                targetFacing,
                hasThreatFacing,
                source,
                closeEngaged,
                flankRisk,
                IsPlayerProtected(unit),
                SafeRouted(unit),
                HasPendingOrder(unit, allowStaleQueuedBypass: true),
                HasActiveMoveMakingProgress(unit),
                HasRecentFacingPulse(instanceId, cooldown),
                FacingRefreshToleranceDegrees,
                isArtillery: IsArtilleryUnit(unit),
                threatWithinFireRange: ThreatWithinFireRange(unit)));

            return decision.Action != TacticalFacingPulseAction.NoWrite || source != TacticalFacingThreatSource.None;
        }

        // Vanilla turns a single unit toward an enemy with RotateRegiment
        // (UnlimberArtilleryAIMicro 3491) — no formation change. Artillery
        // never reaches here (doctrine: artillery-vanilla).
        private static bool ApplyRegimentFacingPulse(Regiment unit, TacticalFacingPulseDecision decision)
        {
            if (unit == null || decision.Action != TacticalFacingPulseAction.CorrectFacing) return false;
            if (IsArtilleryUnit(unit)) return false;
            if (!WhiskeyRealism.Tactical.TacticalWriteGuard.CanWrite(unit)) return false;

            try
            {
                if (unit.regimentpaths > 0 || unit.withinrotationprocess || (UnityEngine.Object)unit.chargetarget != null) return false;
                unit.RotateRegiment(decision.TargetFacingDegrees);
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-facing-pulse:regiment-write-failed",
                    "Tactical contact-facing regiment write failed: " + ex.Message);
                return false;
            }
        }

        private static bool TryApplyFireControl(
            BattleUnits bunits,
            Regiment group,
            CommandNodeOperationalState state,
            out TacticalFireControlDecision appliedDecision)
        {
            appliedDecision = TacticalFireControlDecision.NoWrite(0, "no-fire-control-write");
            if (bunits == null || group == null || IsPlayerProtected(group) || SafeRouted(group)) return false;

            bool wrote = false;
            var visited = new HashSet<int>();
            Regiment[] units = group.allattachedunits;
            if (units != null)
            {
                for (int i = 0; i < units.Length; i++)
                {
                    if (TryApplyFireControlToUnit(bunits, group, units[i], state, visited, out TacticalFireControlDecision decision))
                    {
                        appliedDecision = decision;
                        wrote = true;
                    }
                }
            }

            if (TryApplyFireControlToUnit(bunits, group, group, state, visited, out TacticalFireControlDecision groupDecision))
            {
                appliedDecision = groupDecision;
                wrote = true;
            }

            return wrote;
        }

        private static bool TryApplyFireControlToUnit(
            BattleUnits bunits,
            Regiment group,
            Regiment unit,
            CommandNodeOperationalState state,
            ISet<int> visited,
            out TacticalFireControlDecision decision)
        {
            decision = TacticalFireControlDecision.NoWrite(0, "no-unit");
            try
            {
                if (!IsFireControlUnit(unit)) return false;
                int instanceId = SafeInstanceId(unit);
                if (instanceId == 0 || !visited.Add(instanceId)) return false;
                if (HasRecentFireControlOrder(instanceId)) return false;
                if (IsPlayerProtected(unit) || SafeRouted(unit) || unit.permanentlydetached) return false;
                if (HasPendingOrder(unit) || HasActiveMoveMakingProgress(unit)) return false;

                GameObject gameObject = UnityObject(unit);
                if (gameObject == null || !gameObject.activeInHierarchy) return false;

                Regiment target = ClosestEnemy(unit) ?? ClosestEnemy(group);
                float targetDistance = SafeEnemyDistance(unit);
                if (targetDistance <= 0f && target != null)
                    targetDistance = SafeDistance(SafePosition(unit), SafePosition(target));

                decision = TacticalFireControlDoctrine.Decide(new TacticalFireControlInput(
                    unit.unittyp,
                    unit.mounted > 0,
                    state.Task,
                    state.Role,
                    target != null && targetDistance > 0f,
                    targetDistance,
                    SafeEffectiveFireRange(unit),
                    SafeAmmoRatio(unit),
                    SafeMorale(unit),
                    SafeFatigue(unit),
                    UnitInCover(unit),
                    EnemyAdvancing(target),
                    target != null && EstimateFriendlyFrontBlocker(group, SafePosition(unit), SafePosition(target)) > 0.35f,
                    IsAlignedToTarget(unit, target),
                    UnitLoadedVolley(unit),
                    IsChargeOrdered(unit),
                    unit.combatbehaviorordered));

                if (!decision.ShouldWrite) return false;
                if (!TacticalCombatBehaviorWriter.EnsureBehavior(bunits, unit, decision.RecommendedCombatBehavior)) return false;
                _lastFireControlAt[instanceId] = BattleHours();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveTarget(
            Regiment group,
            TacticalBattleOrchestrator orchestrator,
            CommandNodeOperationalState state,
            CommandDoctrineOrder doctrineOrder,
            PostureExecutionTarget targetKind,
            out Vector3 target)
        {
            target = default(Vector3);
            _navPlanDeferred = false;
            bool resolved;
            switch (targetKind)
            {
                case PostureExecutionTarget.DoctrinePrimaryTarget:
                    if (TryDoctrineTarget(doctrineOrder.PrimaryTarget, out target) && IsSafeWaypoint(group, target))
                    {
                        resolved = true;
                        break;
                    }
                    resolved = TryObjectiveApproach(group, orchestrator, ObjectiveApproachStandOff, out target);
                    break;
                case PostureExecutionTarget.DoctrineSupportTarget:
                    if (TryDoctrineTarget(doctrineOrder.SupportTarget, out target) && IsSafeWaypoint(group, target))
                    {
                        resolved = true;
                        break;
                    }
                    resolved = TryObjectiveApproach(group, orchestrator, ObjectiveApproachStandOff, out target);
                    break;
                case PostureExecutionTarget.DoctrineFallbackTarget:
                    if (TryDoctrineTarget(doctrineOrder.FallbackTarget, out target) && IsSafeWaypoint(group, target))
                    {
                        resolved = true;
                        break;
                    }
                    resolved = TryFallbackFromObjective(group, orchestrator, out target);
                    break;
                case PostureExecutionTarget.ObjectiveApproach:
                case PostureExecutionTarget.ReleasePoint:
                    resolved = TryObjectiveApproach(group, orchestrator, ObjectiveApproachStandOff, out target);
                    break;
                case PostureExecutionTarget.AssemblyArea:
                    resolved = TryObjectiveApproach(group, orchestrator, AssemblyStandOff, out target);
                    break;
                case PostureExecutionTarget.FallbackLine:
                    resolved = TryFallbackFromObjective(group, orchestrator, out target);
                    break;
                case PostureExecutionTarget.RecoveryPath:
                    resolved = TryRecoveryPath(group, orchestrator, out target);
                    break;
                case PostureExecutionTarget.ReserveArea:
                    resolved = TryObjectiveApproach(group, orchestrator, ReserveStandOff, out target);
                    break;
                case PostureExecutionTarget.CurrentPosition:
                case PostureExecutionTarget.None:
                    return false;
                default:
                    return false;
            }

            if (!resolved) return false;
            return TryApplyNavPlan(group, state, doctrineOrder, targetKind, target, out target);
        }

        private static bool TryApplyNavPlan(
            Regiment group,
            CommandNodeOperationalState state,
            CommandDoctrineOrder doctrineOrder,
            PostureExecutionTarget targetKind,
            Vector3 resolvedTarget,
            out Vector3 target)
        {
            target = resolvedTarget;

            if (targetKind != PostureExecutionTarget.DoctrinePrimaryTarget &&
                targetKind != PostureExecutionTarget.DoctrineSupportTarget &&
                targetKind != PostureExecutionTarget.DoctrineFallbackTarget)
                return IsSafeWaypoint(group, target);

            if (!_navBudget.TryConsume())
            {
                _navPlanDeferred = true;
                return false;
            }

            using (TelemetryPerf.Scope("tactical.patch.posture-executor.nav-plan", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            try
            {
                Vector3 own = SafePosition(group);
                if (IsDefaultVector(own)) return IsSafeWaypoint(group, target);

                bool hasThreat = TryClosestEnemyPoint(group, out Vector3 threat);
                Vector3 currentWaypoint = default(Vector3);
                bool hasCurrentWaypoint = false;
                try
                {
                    currentWaypoint = group != null ? group.lastsetwaypointposition : default(Vector3);
                    hasCurrentWaypoint = !IsDefaultVector(currentWaypoint);
                }
                catch { }

                DoctrineTargetPoint primary = DoctrineTargetPoint.From(resolvedTarget.x, resolvedTarget.z);
                DoctrineTargetPoint fallback = doctrineOrder.FallbackTarget;
                if (targetKind == PostureExecutionTarget.DoctrineFallbackTarget)
                {
                    primary = doctrineOrder.PrimaryTarget.HasValue
                        ? doctrineOrder.PrimaryTarget
                        : DoctrineTargetPoint.From(resolvedTarget.x, resolvedTarget.z);
                    fallback = DoctrineTargetPoint.From(resolvedTarget.x, resolvedTarget.z);
                }

                TacticalNavPlanDecision plan = TacticalNavMeshPlanner.Plan(new TacticalNavPlanInput(
                    state.Task,
                    own.x,
                    own.z,
                    primary,
                    fallback,
                    hasThreat,
                    hasThreat ? threat.x : 0f,
                    hasThreat ? threat.z : 0f,
                    hasCurrentWaypoint,
                    hasCurrentWaypoint ? currentWaypoint.x : 0f,
                    hasCurrentWaypoint ? currentWaypoint.z : 0f,
                    HasCloseEngagement(group),
                    MinWaypointDistance,
                    MaxConservativeWaypointDistance,
                    BuildRuntimePathQualitySamples(
                        group,
                        own,
                        resolvedTarget,
                        state.Task,
                        hasThreat,
                        hasThreat ? threat : default(Vector3),
                        hasCurrentWaypoint,
                        currentWaypoint)));

                if (!plan.HasTarget) return IsSafeWaypoint(group, target);

                Vector3 plannedTarget = PlannedTargetVector(plan.Target);
                if (!IsSafeWaypoint(group, plannedTarget)) return IsSafeWaypoint(group, target);

                target = plannedTarget;
                return true;
            }
            catch
            {
                target = resolvedTarget;
                return IsSafeWaypoint(group, target);
            }
        }

        private static Vector3 PlannedTargetVector(DoctrineTargetPoint point)
        {
            return new Vector3(point.X, SafeBattleY(), point.Z);
        }

        private static TacticalPathQualitySample[] BuildRuntimePathQualitySamples(
            Regiment group,
            Vector3 own,
            Vector3 target,
            CommandTaskType task,
            bool hasThreat,
            Vector3 threat,
            bool hasCurrentWaypoint,
            Vector3 currentWaypoint)
        {
            try
            {
                if (IsDefaultVector(own) || IsDefaultVector(target))
                    return Array.Empty<TacticalPathQualitySample>();

                Vector3 direction = target - own;
                direction.y = 0f;
                if (direction.sqrMagnitude < 1f)
                    return Array.Empty<TacticalPathQualitySample>();

                direction.Normalize();
                Vector3 lateral = new Vector3(-direction.z, 0f, direction.x);
                ApplyVanillaPathAreaCosts();
                return new[]
                {
                    BuildPathQualitySample(group, own, target, task, hasThreat, threat, hasCurrentWaypoint, currentWaypoint),
                    BuildPathQualitySample(group, own, target + lateral * 120f, task, hasThreat, threat, hasCurrentWaypoint, currentWaypoint),
                    BuildPathQualitySample(group, own, target - lateral * 120f, task, hasThreat, threat, hasCurrentWaypoint, currentWaypoint)
                };
            }
            catch
            {
                return Array.Empty<TacticalPathQualitySample>();
            }
        }

        // Set the global NavMesh area costs AddPath would use for this move
        // before probing; no restore needed, every AddPath sets them again.
        private static void ApplyVanillaPathAreaCosts()
        {
            try
            {
                SetAreaCost(VanillaPathAreaCosts.RiversAreaName, VanillaPathAreaCosts.Rivers(GameVars.isfrozen()));
                SetAreaCost(VanillaPathAreaCosts.RoadsAreaName, VanillaPathAreaCosts.Roads);
                SetAreaCost(VanillaPathAreaCosts.BridgesAreaName, VanillaPathAreaCosts.Bridges);
                SetAreaCost(VanillaPathAreaCosts.FencesWallsAreaName, VanillaPathAreaCosts.FencesWalls);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-command-posture:area-cost-failed",
                    "BattleCommandPostureExecutorPatch area costs failed: " + ex.Message);
            }
        }

        private static void SetAreaCost(string areaName, float cost)
        {
            int area = NavMesh.GetAreaFromName(areaName);
            if (area >= 0) NavMesh.SetAreaCost(area, cost);
        }

        private static TacticalPathQualitySample BuildPathQualitySample(
            Regiment group,
            Vector3 own,
            Vector3 candidate,
            CommandTaskType task,
            bool hasThreat,
            Vector3 threat,
            bool hasCurrentWaypoint,
            Vector3 currentWaypoint)
        {
            float slopeCost = 0f;
            float terrainRisk = 0f;
            float bridgeRisk = 0f;
            float congestion = 0f;
            float roadPreference = 0f;
            float deadGround = 0f;
            float threatExposure = 0f;
            float routeContinuity = 0f;
            float reservationPressure = 0f;
            float fallbackLaneConflict = 0f;
            float artilleryDanger = 0f;

            try
            {
                var path = new NavMeshPath();
                bool pathFound = NavMesh.CalculatePath(own, candidate, NavMesh.AllAreas, path);
                Vector3[] corners = path != null ? path.corners : null;
                int count = corners != null ? corners.Length : 0;
                if (!pathFound || path.status != NavMeshPathStatus.PathComplete || count <= 0)
                {
                    terrainRisk = 1f;
                }
                else
                {
                    roadPreference = count <= 3 ? 0.65f : 0.25f;
                    congestion = Math.Min(1f, Math.Max(0f, (count - 4) * 0.12f));
                    float previousHeight = SampleHeight(own);
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 corner = corners[i];
                        float height = SampleHeight(corner);
                        slopeCost = Math.Max(slopeCost, Math.Min(1f, Math.Abs(height - previousHeight) / 35f));
                        previousHeight = height;
                        int terrain = SafeTerrain(corner);
                        if (terrain == 4 || terrain == 8 || terrain == 6)
                        {
                            terrainRisk = Math.Max(terrainRisk, 0.85f);
                            bridgeRisk = Math.Max(bridgeRisk, 0.70f);
                        }
                        if (SafeSearchCrossingTerrain(corner))
                        {
                            bridgeRisk = Math.Max(bridgeRisk, 0.55f);
                        }
                    }
                }

                if (hasThreat && !IsDefaultVector(threat))
                {
                    float distance = Vector3.Distance(candidate, threat);
                    deadGround = distance >= 250f ? 0.70f : Math.Max(0f, distance / 250f * 0.45f);
                    threatExposure = distance <= 100f ? 1f : Math.Max(0f, 1f - ((distance - 100f) / 350f));
                    artilleryDanger = threatExposure * (1f - deadGround) * 0.40f;
                    if (task == CommandTaskType.FallBackToLine)
                    {
                        float axisDistance = DistanceToSegment2D(candidate, threat, own);
                        float axisRisk = Math.Max(0f, 1f - (axisDistance / 180f));
                        float closesThreat = distance < Vector3.Distance(own, threat) ? 0.35f : 0f;
                        fallbackLaneConflict = Math.Max(axisRisk, closesThreat);
                    }
                }

                if (hasCurrentWaypoint && !IsDefaultVector(currentWaypoint))
                    routeContinuity = Math.Max(0f, 1f - (Vector3.Distance(candidate, currentWaypoint) / 350f));

                reservationPressure = Math.Max(
                    congestion,
                    Math.Max(bridgeRisk * 0.75f, terrainRisk * 0.45f));
            }
            catch
            {
                terrainRisk = Math.Max(terrainRisk, 0.50f);
            }

            return new TacticalPathQualitySample(
                candidate.x,
                candidate.z,
                roadPreference,
                slopeCost,
                congestion,
                terrainRisk,
                bridgeRisk,
                deadGround,
                EstimateFriendlyFrontBlocker(group, own, candidate),
                threatExposure,
                routeContinuity,
                reservationPressure,
                fallbackLaneConflict,
                artilleryDanger);
        }

        private static float DistanceToSegment2D(Vector3 point, Vector3 start, Vector3 end)
        {
            try
            {
                Vector3 segment = end - start;
                segment.y = 0f;
                float lengthSquared = segment.sqrMagnitude;
                if (lengthSquared < 0.001f) return SafeDistance(point, start);

                Vector3 rel = point - start;
                rel.y = 0f;
                float t = Vector3.Dot(rel, segment) / lengthSquared;
                t = Math.Max(0f, Math.Min(1f, t));
                Vector3 projected = start + segment * t;
                projected.y = point.y;
                return SafeDistance(point, projected);
            }
            catch
            {
                return 9999f;
            }
        }

        // Friendly corridor blocker via a per-Postfix spatial grid of the
        // group's alliance (built once, reused for every candidate path and
        // fire-control check in this call).
        private static float EstimateFriendlyFrontBlocker(Regiment group, Vector3 own, Vector3 candidate)
        {
            try
            {
                if (group == null || IsDefaultVector(own) || IsDefaultVector(candidate)) return 0f;
                if (_friendlyGridAlliance != group.alliance) BuildFriendlyGrid(group.alliance);
                return _friendlyGrid.EstimateFrontBlocker(SafeInstanceId(group), own.x, own.z, candidate.x, candidate.z);
            }
            catch
            {
                return 0f;
            }
        }

        private static void BuildFriendlyGrid(int alliance)
        {
            _friendlyGrid.Clear();
            _friendlyGridAlliance = alliance;
            var units = BattleUnits.completeunitlist as IList;
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                var friend = units[i] as Regiment;
                if (friend == null || friend.alliance != alliance) continue;
                if (friend.isrouted || friend.markedforrout || friend.permanentlydetached) continue;
                if (friend.unittyp > 14) continue;
                Vector3 pos = SafePosition(friend);
                if (IsDefaultVector(pos)) continue;
                _friendlyGrid.Add(SafeInstanceId(friend), pos.x, pos.z);
            }
        }

        private static float SampleHeight(Vector3 point)
        {
            try
            {
                BattlefieldSetup bfs = SafeBattlefieldSetup();
                return bfs != null ? bfs.GetTerrainHeight(point) : point.y;
            }
            catch
            {
                return point.y;
            }
        }

        private static int SafeTerrain(Vector3 point)
        {
            try { return BattlefieldSetup.GetCurrentTerrainOnPos(point); }
            catch { return -1; }
        }

        private static bool SafeSearchCrossingTerrain(Vector3 point)
        {
            try { return BattlefieldSetup.SearchTerrainInRangePos(point, new[] { 4, 8 }, 2) != null; }
            catch { return false; }
        }

        private static bool TryDoctrineTarget(DoctrineTargetPoint point, out Vector3 target)
        {
            target = default(Vector3);
            if (!point.HasValue) return false;

            target = new Vector3(point.X, SafeBattleY(), point.Z);
            return !IsDefaultVector(target);
        }

        private static bool TryObjectiveApproach(Regiment group, TacticalBattleOrchestrator orchestrator, float standOff, out Vector3 target)
        {
            target = default(Vector3);
            if (!TryPrimaryObjectivePoint(group, orchestrator, out Vector3 objective)) return false;

            Vector3 current = SafePosition(group);
            if (IsDefaultVector(current)) return false;

            float distance = SafeDistance(current, objective);
            if (distance < MinWaypointDistance || distance > MaxConservativeWaypointDistance) return false;

            float offset = Math.Min(Math.Max(standOff, 0f), Math.Max(0f, distance - MinWaypointDistance));
            target = Vector3.MoveTowards(objective, current, offset);
            return IsSafeWaypoint(group, target);
        }

        private static bool TryFallbackFromObjective(
            Regiment group,
            TacticalBattleOrchestrator orchestrator,
            out Vector3 target)
        {
            target = default(Vector3);
            Vector3 current = SafePosition(group);
            if (IsDefaultVector(current)) return false;

            TacticalMapPoint? objectivePoint = TryPrimaryObjectivePoint(group, orchestrator, out Vector3 objective)
                ? new TacticalMapPoint(objective.x, objective.z)
                : (TacticalMapPoint?)null;
            TacticalMapPoint? threatPoint = TryClosestEnemyPoint(group, out Vector3 threat)
                ? new TacticalMapPoint(threat.x, threat.z)
                : (TacticalMapPoint?)null;

            if (!CommandFallbackTargetResolver.TryResolve(
                    new TacticalMapPoint(current.x, current.z),
                    objectivePoint,
                    threatPoint,
                    FallbackStandOff,
                    MinWaypointDistance,
                    MaxConservativeWaypointDistance,
                    out TacticalMapPoint fallback,
                    out _))
                return false;

            target = new Vector3(fallback.X, SafeBattleY(), fallback.Z);
            return IsSafeWaypoint(group, target);
        }

        private static bool TryClosestEnemyPoint(Regiment group, out Vector3 target)
        {
            target = default(Vector3);
            try
            {
                Regiment enemy = TacticalFogOfWarContact.ClosestVisibleEnemy(group);
                if (enemy == null) return false;

                Vector3 position = SafePosition(enemy);
                if (IsDefaultVector(position)) return false;
                target = position;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRecoveryPath(
            Regiment group,
            TacticalBattleOrchestrator orchestrator,
            out Vector3 target)
        {
            target = default(Vector3);
            try
            {
                if (group != null)
                {
                    Vector3 lastWaypoint = group.lastsetwaypointposition;
                    if (IsSafeWaypoint(group, lastWaypoint))
                    {
                        target = lastWaypoint;
                        return true;
                    }
                }
            }
            catch { }

            return TryObjectiveApproach(group, orchestrator, AssemblyStandOff, out target);
        }

        private static bool TryPrimaryObjectivePoint(
            Regiment group,
            TacticalBattleOrchestrator orchestrator,
            out Vector3 objective)
        {
            objective = default(Vector3);
            if (TryCurrentSetObjectivePoint(group, out objective)) return true;

            var objectives = orchestrator?.OperationsLedger?.CurrentObjectives;
            if (objectives == null || objectives.Count == 0)
                return TryLastWaypointPoint(group, out objective);

            string primary = orchestrator.Army != null
                ? orchestrator.Army.CurrentOperation.PrimaryObjectiveId
                : "objective-unknown";
            for (int i = 0; i < objectives.Count; i++)
            {
                var record = objectives[i];
                if (!string.Equals(record.Observation.ObjectiveId, primary, StringComparison.Ordinal))
                    continue;

                objective = new Vector3(record.Observation.Location.X, SafeBattleY(), record.Observation.Location.Z);
                return !IsDefaultVector(objective);
            }

            if (objectives.Count > 0)
            {
                var fallback = objectives[0];
                objective = new Vector3(fallback.Observation.Location.X, SafeBattleY(), fallback.Observation.Location.Z);
                return !IsDefaultVector(objective);
            }

            return false;
        }

        private static bool TryLastWaypointPoint(Regiment group, out Vector3 objective)
        {
            objective = default(Vector3);
            try
            {
                if (group == null) return false;
                Vector3 waypoint = group.lastsetwaypointposition;
                if (IsDefaultVector(waypoint)) return false;
                objective = new Vector3(waypoint.x, SafeBattleY(), waypoint.z);
                return IsSafeWaypoint(group, objective);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCurrentSetObjectivePoint(Regiment group, out Vector3 objective)
        {
            objective = default(Vector3);
            try
            {
                if (group == null) return false;
                object current = group.GetType().GetField("currentsetobjective")?.GetValue(group);
                var component = current as Component;
                if (component == null) return false;
                Vector3 position = component.transform.position;
                objective = new Vector3(position.x, SafeBattleY(), position.z);
                return !IsDefaultVector(objective);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSafeWaypoint(Regiment group, Vector3 target)
        {
            Vector3 current = SafePosition(group);
            if (IsDefaultVector(current) || IsDefaultVector(target)) return false;
            float distance = SafeDistance(current, target);
            return distance >= MinWaypointDistance && distance <= MaxConservativeWaypointDistance;
        }

        private static bool CanWrite(
            Regiment group,
            CommandTaskType task,
            WriteEligibilitySnapshot eligibility,
            CommandPhysicalState physical,
            float recentOrderHours,
            bool allowStaleQueuedBypass)
        {
            if (!eligibility.ModeAllowsWrites) return false;
            if (eligibility.PlayerProtected || physical.PlayerProtected) return false;
            if (eligibility.Routed || physical.Routed) return false;
            if (eligibility.OrderPending) return false;
            if (eligibility.RecentOrder) return false;
            if (physical.ActiveMove) return false;
            if (HasPendingOrder(group, allowStaleQueuedBypass)) return false;
            if (CommandPostureExecutor.ShouldBlockForRecentOrder(
                    task,
                    physical,
                    HasRecentExecutorOrder(SafeInstanceId(group), recentOrderHours)))
                return false;
            return true;
        }

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

        private static CommandPhysicalState BuildPhysicalState(Regiment group, bool playerProtected, bool routed)
        {
            return new CommandPhysicalState(
                routed: routed,
                playerProtected: playerProtected,
                pathInterrupted: group != null && group.pathinterrupted,
                paths: SafeRegimentPaths(group),
                activeMove: HasActiveMoveMakingProgress(group),
                formation: SafeGroupFormation(group));
        }

        private static bool IsAlreadyDoingCorrectTask(
            Regiment group,
            CommandNodeOperationalState state,
            CommandPhysicalState physical,
            int targetFormation,
            TacticalIdleClassification idle)
        {
            if (group == null || physical.PathInterrupted) return false;
            if (physical.ActiveMove) return true;
            if (targetFormation >= 0 && SafeGroupFormation(group) != targetFormation) return false;
            return idle == TacticalIdleClassification.ValidIdle &&
                CommandPostureExecutor.IdleCanSatisfyTask(state.Task);
        }

        private static bool TryResolveLedgerState(
            Regiment group,
            out CommandNodeOperationalState state,
            out TacticalBattleOrchestrator side)
        {
            state = default;
            side = null;

            try
            {
                if (group == null) return false;
                side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                ArmyOrchestrator army = side?.Army;
                var operations = army?.CurrentCommandOperations;
                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                if (operations != null)
                {
                    for (int i = 0; i < operations.Count; i++)
                    {
                        if (!TacticalPatchIds.NodeIdMatches(operations[i].NodeId, gameObjectInstanceId, componentInstanceId)) continue;
                        state = operations[i];
                        return true;
                    }
                }

                var resolution = army?.ResolveCommandIntentForGroup(componentInstanceId, gameObjectInstanceId);
                if (resolution.HasValue &&
                    resolution.Value.Found &&
                    CommandNodeOperationsRuntime.TryBuildSingle(
                        resolution.Value.Intent,
                        army.CurrentOperation,
                        side?.OperationsLedger?.CurrentObjectives,
                        out state))
                    return true;
            }
            catch { }

            return false;
        }

        private static bool TryResolveDoctrineOrder(
            Regiment group,
            ArmyOrchestrator army,
            string nodeId,
            out CommandDoctrineOrder order)
        {
            order = default(CommandDoctrineOrder);

            try
            {
                var orders = army?.CurrentDoctrineOrders;
                if (orders == null || orders.Count == 0) return false;

                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                for (int i = 0; i < orders.Count; i++)
                {
                    CommandDoctrineOrder candidate = orders[i];
                    if (!string.Equals(candidate.NodeId, nodeId, StringComparison.Ordinal) &&
                        !TacticalPatchIds.NodeIdMatches(candidate.NodeId, gameObjectInstanceId, componentInstanceId))
                        continue;

                    if (!candidate.HasPurpose) return false;
                    order = candidate;
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Second pass that writes posture decisions for leaf brigades nested
        /// inside division-tier nodes. Builds the depth-agnostic leaf brigade
        /// map (TacticalLeafBrigadeMap) keyed by the alliance's nested probe
        /// tree, then iterates BattleUnits.completeunitlist filtered by alliance
        /// and brigade tier (unittyp == TacticalUnitType.BattleGroupBrigade) to
        /// apply per-leaf posture writes for any brigade NOT already iterated
        /// in the unitsused loop.
        ///
        /// Translates SoW's CUnitDivThink → SubBrigade iteration pattern to
        /// GTCW's flat-unitsused surface. Better than SoW because the cascade
        /// is hierarchy-depth-agnostic and personality-modulated.
        /// </summary>
        private static void TryApplyNestedLeafBrigades(
            AIBattle battle,
            BattleUnits bunits,
            int side,
            HashSet<int> alreadyIterated,
            IReadOnlyDictionary<string, TacticalDivisionPlayOrder> runtimePlayOrders)
        {
            try
            {
                int allianceId = bunits != null && bunits.alliance != null && side >= 0 && side < bunits.alliance.Length
                    ? bunits.alliance[side]
                    : -1;
                if (allianceId < 0 || allianceId >= 2) return;

                // B-61state: the leaf map, live initiative and reinforcement
                // opportunity are refreshed by the coordinator tick
                // (ArmyPostureInputsRuntime.Refresh); this Postfix only reads them.
                ArmyOrchestrator army = TacticalBattleCoordinator.GetSideOrchestrator(allianceId)?.Army;
                if (army == null) return;

                var leafMap = army.CurrentLeafBrigadeMap;
                if (leafMap == null || leafMap.Count == 0) return;

                var units = BattleUnits.completeunitlist as System.Collections.IList;
                if (units == null) return;

                for (int i = 0; i < units.Count; i++)
                {
                    var group = units[i] as Regiment;
                    if (group == null) continue;
                    if (group.alliance != allianceId) continue;
                    // Allow ANY command-tier group (brigade and above) — the leaf
                    // map may record a Division/Corps as a degenerate leaf when
                    // discovery couldn't find brigade-tier children under it.
                    // Strict brigade-tier filtering here would silently drop
                    // those nodes; relaxing to >= BattleGroupBrigade lets the
                    // posture write reach whatever the leaf map registered.
                    if (group.unittyp < TacticalUnitType.BattleGroupBrigade) continue;
                    if (!IsEligibleCommandGroup(group)) continue;
                    int instanceId = TacticalPatchIds.GameObjectInstanceId(group);
                    if (instanceId == 0) continue;
                    if (alreadyIterated.Contains(instanceId)) continue;
                    if (!leafMap.TryGetValue(instanceId, out var leafAssignment)) continue;
                    if (leafAssignment.LeafTask == CommandTaskType.None) continue;

                    // The brigade has a cascaded role/task but no ledger state.
                    // Synthesize a CommandNodeOperationalState for it and reuse
                    // the existing TryApplyGroup logic — which understands
                    // doctrine orders + courier delivery + posture write paths.
                    TryApplyLeafBrigade(battle, bunits, side, group, leafAssignment, runtimePlayOrders);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-leaf-cascade:apply-failed",
                    "TryApplyNestedLeafBrigades failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void TryApplyLeafBrigade(
            AIBattle battle,
            BattleUnits bunits,
            int side,
            Regiment group,
            TacticalLeafBrigadeMap.LeafAssignment leafAssignment,
            IReadOnlyDictionary<string, TacticalDivisionPlayOrder> runtimePlayOrders)
        {
            // Synthesize an operational state from the leaf assignment. The
            // NodeId follows the same "child-<instanceId>" pattern used by
            // DirectChildAllocator for direct children so downstream resolvers
            // can still pattern-match.
            int instanceId = TacticalPatchIds.GameObjectInstanceId(group);

            // Opportunity-override (2026-05-21). Without this, the cascade applies
            // the brigade's role-default task (e.g. RefuseLeft → GuardFlank) regardless
            // of what the army's high-level opportunity analysis has decided
            // (WithdrawalToFightLater, AttackNow, DefensiveHold, WaitAndConsolidate).
            // The user observed that "withdrawal" opportunity decisions never
            // propagated to brigade behavior — brigades kept guarding their flank
            // even when the army wanted to withdraw. Wire the decision through.
            ReinforcementOpportunity opportunity = ReinforcementOpportunity.NoOpportunity;
            ArmyOrchestrator army = null;
            try
            {
                var sideOrch = group != null
                    ? TacticalBattleCoordinator.GetSideOrchestrator(group.alliance)
                    : null;
                army = sideOrch?.Army;
                if (army != null)
                {
                    opportunity = army.CurrentReinforcementOpportunity.Opportunity;
                }
            }
            catch { /* default = NoOpportunity, preserves prior behavior */ }

            CommandTaskType effectiveTask = TacticalOpportunityTaskOverride.Override(
                leafAssignment.LeafTask,
                leafAssignment.LeafRole,
                opportunity,
                out string overrideReason);

            // Arrival synchronization (2026-05-25 / B): a Main-effort brigade
            // committed to AttackObjective must not charge in alone. Hold it as
            // Screen (skirmish forward, ai_stance=1) until its SupportMain
            // siblings close the line, the hold budget elapses, or an aggressive
            // commander (≥ 0.7) overrides. Wires the previously-dead
            // TacticalMainArrivalSynchronizer — root cause of the "lone brigade
            // attacks a superior force without support" smoke pattern.
            if (effectiveTask == CommandTaskType.AttackObjective
                && leafAssignment.LeafRole == DirectChildRole.Main
                && army != null)
            {
                float aggr01 = (army.CommanderPersonality.Aggression + 1f) * 0.5f;
                if (aggr01 < 0f) aggr01 = 0f;
                if (aggr01 > 1f) aggr01 = 1f;
                var heldTask = ResolveMainArrivalHold(
                    group, instanceId, army, aggr01, effectiveTask,
                    out bool arrivalHolding, out string arrivalReason);
                if (arrivalHolding)
                {
                    effectiveTask = heldTask;
                    if (overrideReason == null) overrideReason = arrivalReason;
                }
            }
            else
            {
                // Role/task changed: the next Main attack may arm a fresh hold.
                _mainArrivalHold.Remove(instanceId);
            }

            // Override-propagation fix (2026-05-21): the earlier override fix only
            // updated synthesizedState.Task, but the leaf move read
            // leafAssignment.LeafTask directly to pick the movement target — so the
            // unit still moved per its ORIGINAL role (e.g. RefuseLeft → GuardFlank
            // position) even when the army said WithdrawalToFightLater. Smoke logs
            // showed Union brigades still getting `applied-GuardFlank` despite the
            // opportunity decision being Withdrawal.
            //
            // Fix: when the override changes the task, construct a NEW LeafAssignment
            // with the overridden task and pass that downstream. All target-resolution
            // and telemetry paths now see the overridden task.
            TacticalLeafBrigadeMap.LeafAssignment effectiveLeafAssignment = effectiveTask == leafAssignment.LeafTask
                ? leafAssignment
                : new TacticalLeafBrigadeMap.LeafAssignment(
                    instanceId: leafAssignment.InstanceId,
                    displayName: leafAssignment.DisplayName,
                    leafRole: leafAssignment.LeafRole,
                    leafTask: effectiveTask,
                    cascadeChain: leafAssignment.CascadeChain,
                    parentNameChain: leafAssignment.ParentNameChain,
                    topLevelChildIndex: leafAssignment.TopLevelChildIndex);

            if (overrideReason != null)
            {
                OnceLog.Info(
                    "tactical-leaf-cascade:opportunity-override:" + side + ":" + leafAssignment.LeafRole + ":" + opportunity,
                    "[TacticalLeafCascade] opportunity override: " + leafAssignment.LeafTask
                    + " → " + effectiveTask + " (role=" + leafAssignment.LeafRole
                    + " opportunity=" + opportunity + " reason=" + overrideReason + ")");
            }

            var synthesizedState = new CommandNodeOperationalState(
                nodeId: "leaf-" + instanceId,
                echelon: CommandEchelonKind.BrigadeLike,
                role: MapDirectChildRoleToNodeRole(leafAssignment.LeafRole),
                task: effectiveTask,
                taskState: CommandTaskState.Planning);

            // Reuse the same physical/eligibility/decision pipeline as the
            // primary unitsused iteration, but with the synthesized leaf state.
            // The TryApplyGroup signature requires resolving ledger state; for
            // nested brigades we don't have ledger state, so we replicate the
            // minimal posture flow inline. Pass effectiveLeafAssignment so target
            // resolution sees the overridden task.
            ApplyLeafBrigadePosture(battle, bunits, side, group, synthesizedState, effectiveLeafAssignment, runtimePlayOrders);
        }

        // ---- Arrival synchronization (BR-M1) ----
        // Per-Main latch on battle time (MainArrivalHoldLatch): arms once per
        // role/task assignment, stays released after expiry until the leaf's
        // role/task changes. Cleared on battle end.
        private static readonly Dictionary<int, MainArrivalHoldLatch.State> _mainArrivalHold =
            new Dictionary<int, MainArrivalHoldLatch.State>();
        private static readonly List<TacticalMainArrivalSynchronizer.SupportMainPosition> _arrivalSupportScratch =
            new List<TacticalMainArrivalSynchronizer.SupportMainPosition>(8);

        // SupportMain leaf positions, built once per Apply per alliance instead
        // of a completeunitlist walk per Main brigade (BR-M6).
        private static readonly Dictionary<int, List<Vector3>> _supportMainPositionsByAlliance =
            new Dictionary<int, List<Vector3>>();

        private static List<Vector3> SupportMainPositions(int alliance, IReadOnlyDictionary<int, TacticalLeafBrigadeMap.LeafAssignment> leafMap)
        {
            if (_supportMainPositionsByAlliance.TryGetValue(alliance, out List<Vector3> cached)) return cached;
            var positions = new List<Vector3>(8);
            var units = BattleUnits.completeunitlist as IList;
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    var r = units[i] as Regiment;
                    if (r == null || r.alliance != alliance) continue;
                    if (r.unittyp < TacticalUnitType.BattleGroupBrigade) continue;
                    int id = TacticalPatchIds.GameObjectInstanceId(r);
                    if (id == 0 || !leafMap.TryGetValue(id, out var la)) continue;
                    if (la.LeafRole != DirectChildRole.SupportMain || SafeRouted(r)) continue;
                    if (WhiskeyRealism.Tactical.TacticalWriteGuard.IsPlayerProtected(r)) continue;
                    positions.Add(SafePosition(r));
                }
            }
            _supportMainPositionsByAlliance[alliance] = positions;
            return positions;
        }

        /// <summary>
        /// Holds a Main-effort brigade on AttackObjective as Screen until its
        /// SupportMain siblings close on it, the hold budget elapses, or an
        /// aggressive commander releases it. Battle time; never throws.
        /// </summary>
        private static CommandTaskType ResolveMainArrivalHold(
            Regiment mainGroup,
            int mainInstanceId,
            ArmyOrchestrator army,
            float commanderAggression01,
            CommandTaskType attackTask,
            out bool holding,
            out string reason)
        {
            holding = false;
            reason = null;
            try
            {
                var leafMap = army?.CurrentLeafBrigadeMap;
                if (leafMap == null || mainGroup == null) return attackTask;

                _mainArrivalHold.TryGetValue(mainInstanceId, out MainArrivalHoldLatch.State latch);
                if (!MainArrivalHoldLatch.ShouldEvaluate(latch)) return attackTask;

                Vector3 mainPos = SafePosition(mainGroup);
                var supports = _arrivalSupportScratch;
                supports.Clear();
                List<Vector3> supportPositions = SupportMainPositions(mainGroup.alliance, leafMap);
                for (int i = 0; i < supportPositions.Count; i++)
                    supports.Add(new TacticalMainArrivalSynchronizer.SupportMainPosition(SafeDistance(supportPositions[i], mainPos)));

                // Battle seconds on the doctrine clock (hours x 3600).
                float now = BattleHours();
                float elapsedSeconds = MainArrivalHoldLatch.ElapsedBattleSeconds(latch, now);
                var resolved = TacticalMainArrivalSynchronizer.ResolveMainAttackTask(
                    DirectChildRole.Main, attackTask, supports,
                    commanderAggression01, elapsedSeconds,
                    TacticalMainArrivalSynchronizer.DefaultSupportProximityThresholdMeters,
                    MainArrivalHoldLatch.MaxHoldBattleSeconds,
                    out holding);

                _mainArrivalHold[mainInstanceId] = MainArrivalHoldLatch.Next(latch, now, holding);
                if (holding)
                {
                    reason = "arrival-hold-screen(supports=" + supports.Count
                        + " elapsedSeconds=" + elapsedSeconds.ToString("0") + ")";
                }
                return resolved;
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-arrival-sync:resolve-failed",
                    "ResolveMainArrivalHold failed: " + e.GetType().Name + " " + e.Message);
                holding = false;
                return attackTask;
            }
        }

        // B-leaf: a leaf brigade nested inside an AI group is moved only for
        // movement tasks, only to its slot in the parent's formation, never
        // while the parent has an order in flight, and through the same gates
        // as the group path (LeafBrigadeMovePolicy). No stance write: the leaf
        // is not an AI group (TacticalStanceWriter refuses it anyway).
        private static void ApplyLeafBrigadePosture(
            AIBattle battle,
            BattleUnits bunits,
            int side,
            Regiment group,
            CommandNodeOperationalState state,
            TacticalLeafBrigadeMap.LeafAssignment leafAssignment,
            IReadOnlyDictionary<string, TacticalDivisionPlayOrder> runtimePlayOrders)
        {
            try
            {
                if (IsPlayerProtected(group)) { EmitLeafCascadeTelemetry(side, group, leafAssignment, "player-protected", false); return; }
                if (SafeRouted(group)) { EmitLeafCascadeTelemetry(side, group, leafAssignment, "routed", false); return; }

                int instanceId = TacticalPatchIds.GameObjectInstanceId(group);
                if (HasRecentExecutorOrder(instanceId)) { EmitLeafCascadeTelemetry(side, group, leafAssignment, "recent-order", false); return; }
                if (_writeBackoffUntil.TryGetValue(instanceId, out float backoffUntil) &&
                    CommandWaypointWritePolicy.IsBackedOff(BattleHours(), backoffUntil))
                {
                    EmitLeafCascadeTelemetry(side, group, leafAssignment, "write-backoff", false);
                    return;
                }

                Regiment parent = CommandParent(group);
                bool parentActive = parent != null &&
                    (parent.regimentpaths > 0 || HasPendingOrder(parent) || HasActiveMoveMakingProgress(parent));
                bool hasSlot = TryFormationSlot(group, parent, out Vector3 target, out float slotRotation);
                LeafMoveDecision move = LeafBrigadeMovePolicy.Decide(
                    leafAssignment.LeafTask,
                    parentActive,
                    hasSlot,
                    HasCloseEngagement(group),
                    HasPendingOrder(group),
                    HasActiveMoveMakingProgress(group));
                if (!move.Move) { EmitLeafCascadeTelemetry(side, group, leafAssignment, move.Reason, false); return; }
                if (!IsSafeWaypoint(group, target)) { EmitLeafCascadeTelemetry(side, group, leafAssignment, "unsafe-target", false); return; }
                if (ShouldSkipDuplicateWaypoint(group, target)) { EmitLeafCascadeTelemetry(side, group, leafAssignment, "duplicate-waypoint", false); return; }
                if (ShouldSuppressTaskRefresh(group, leafAssignment.LeafTask)) { EmitLeafCascadeTelemetry(side, group, leafAssignment, "task-hold", false); return; }

                int beforeMovingPaths = CountMovingPaths(group);
                int beforeQueued = QueuedOrderCount(group);
                bool useOrderDelay = CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(
                    leafAssignment.LeafTask,
                    battleActive: true,
                    inContactWithdrawal: HasLocalFlankRisk(group));
                bunits.SetWaypoint(
                    group,
                    target,
                    newpath: true,
                    doublequick: false,
                    manualfinalrotation: slotRotation,
                    modifylastwaypoint: false,
                    useorderdelay: useOrderDelay,
                    timetomove: -1f,
                    direction: -1,
                    showmovementoptions: false,
                    ignorebattlemonuments: false,
                    groupmoveonly: false,
                    ignoredisabledships: false,
                    checkforreadiness: true,
                    clearinterruptionpaths: true);

                if (!CommandWaypointWritePolicy.MovementIssued(beforeMovingPaths, CountMovingPaths(group), beforeQueued, QueuedOrderCount(group)))
                {
                    _writeBackoffUntil[instanceId] = CommandWaypointWritePolicy.FailedWriteBackoffUntil(BattleHours());
                    EmitLeafCascadeTelemetry(side, group, leafAssignment, "movement-unmaterialized", false);
                    return;
                }

                _lastExecutorOrderAt[instanceId] = BattleHours();
                _writeBackoffUntil.Remove(instanceId);
                RecordTaskWrite(group, leafAssignment.LeafTask);
                EmitLeafCascadeTelemetry(side, group, leafAssignment, "applied-" + leafAssignment.LeafTask, true);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-leaf-cascade:posture-failed",
                    "ApplyLeafBrigadePosture failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Vanilla command parent (BattleUnits.GrabNextParentUnit 86886): the
        // transform parent, or null at the Units root.
        private static Regiment CommandParent(Regiment group)
        {
            try
            {
                if (group == null) return null;
                GameObject self = ((Component)group).gameObject;
                GameObject parent = BattleUnits.GrabNextParentUnit(self);
                if (parent == null || parent == self) return null;
                return parent.GetComponent<Regiment>();
            }
            catch
            {
                return null;
            }
        }

        // The brigade's slot in its parent's formation, computed the way
        // SetGroupFormation places a group child (92060-92075): from the
        // parent's last waypoint and rotation, offset by
        // groupposition.unitoffsetposition[i] (lookup as GetOffsetPositionInGroup)
        // plus the child's group depth, then clamped to the map like
        // RegimentSetPath (131054-131071). No parent or slot → no move.
        private static bool TryFormationSlot(Regiment group, Regiment parent, out Vector3 slot, out float rotation)
        {
            slot = default(Vector3);
            rotation = -1f;
            try
            {
                if (group == null || parent == null || parent.groupposition == null) return false;
                Vector3 anchor = parent.lastsetwaypointposition;
                if (IsDefaultVector(anchor)) return false;

                BattleUnits.GroupPosition layout = parent.groupposition;
                GameObject self = ((Component)group).gameObject;
                int index = -1;
                for (int i = 0; layout.attachedunitid != null && i < layout.attachedunitid.Length; i++)
                {
                    if (layout.attachedunitid[i] == self) { index = i; break; }
                }
                if (index < 0 || layout.unitoffsetposition == null || index >= layout.unitoffsetposition.Length) return false;

                Vector3 offset = layout.unitoffsetposition[index];
                float depth = group.groupposition != null ? group.groupposition.groupdepth : 0f;
                rotation = parent.lastsetwaypointrotation;
                LeafFormationSlot.OffsetXZ(anchor.x, anchor.z, rotation, -offset.x, offset.z + depth, out float x, out float z);
                Vector3 size = TerrainSize();
                VanillaMapBounds.ClampXZ(x, z, size.x, size.z, out x, out z);
                slot = new Vector3(x, anchor.y, z);
                return !IsDefaultVector(slot);
            }
            catch
            {
                return false;
            }
        }

        // Terrain.activeTerrain.terrainData.size for the vanilla map clamp,
        // read by reflection (UnityEngine.TerrainModule is not referenced);
        // zero when unavailable, and VanillaMapBounds then leaves the target
        // unchanged.
        private static PropertyInfo _activeTerrainProperty;
        private static PropertyInfo _terrainDataProperty;
        private static PropertyInfo _terrainSizeProperty;
        private static bool _terrainReflectionTried;

        private static Vector3 TerrainSize()
        {
            try
            {
                if (!_terrainReflectionTried)
                {
                    _terrainReflectionTried = true;
                    Type terrainType = AccessTools.TypeByName("UnityEngine.Terrain");
                    Type terrainDataType = AccessTools.TypeByName("UnityEngine.TerrainData");
                    _activeTerrainProperty = terrainType != null ? AccessTools.Property(terrainType, "activeTerrain") : null;
                    _terrainDataProperty = terrainType != null ? AccessTools.Property(terrainType, "terrainData") : null;
                    _terrainSizeProperty = terrainDataType != null ? AccessTools.Property(terrainDataType, "size") : null;
                    if (_activeTerrainProperty == null || _terrainDataProperty == null || _terrainSizeProperty == null)
                        OnceLog.Warning("tactical-command-posture:terrain-size-missing",
                            "BattleCommandPostureExecutorPatch: Terrain size not readable; leaf targets are not map-clamped");
                }

                if (_activeTerrainProperty == null || _terrainDataProperty == null || _terrainSizeProperty == null)
                    return Vector3.zero;
                object terrain = _activeTerrainProperty.GetValue(null, null);
                object data = terrain != null ? _terrainDataProperty.GetValue(terrain, null) : null;
                return data != null ? (Vector3)_terrainSizeProperty.GetValue(data, null) : Vector3.zero;
            }
            catch
            {
                return Vector3.zero;
            }
        }

        private static CommandNodeRole MapDirectChildRoleToNodeRole(DirectChildRole role)
        {
            switch (role)
            {
                case DirectChildRole.Main:        return CommandNodeRole.MainEffort;
                case DirectChildRole.SupportMain: return CommandNodeRole.SupportingAttack;
                case DirectChildRole.Fix:         return CommandNodeRole.FixingForce;
                case DirectChildRole.Screen:      return CommandNodeRole.ScreeningForce;
                case DirectChildRole.RefuseLeft:  return CommandNodeRole.FlankMarch;
                case DirectChildRole.RefuseRight: return CommandNodeRole.FlankMarch;
                case DirectChildRole.Reserve:     return CommandNodeRole.Reserve;
                case DirectChildRole.Fallback:    return CommandNodeRole.FallbackGuard;
                case DirectChildRole.Unknown:
                default:                          return CommandNodeRole.Unknown;
            }
        }

        private static void EmitLeafCascadeTelemetry(
            int side,
            Regiment group,
            TacticalLeafBrigadeMap.LeafAssignment leafAssignment,
            string outcome,
            bool applied)
        {
            try
            {
                string unit = SafeName(group);
                int instanceId = TacticalPatchIds.GameObjectInstanceId(group);
                string chain = leafAssignment.CascadeChainString;
                string parents = string.Join(">", new List<string>(leafAssignment.ParentNameChain));
                // Signature dedupes outcome + role + task. Same brigade re-emitting
                // the same outcome within TelemetrySeconds (30s) is suppressed —
                // bounds the per-brigade row count to roughly 2/min in steady
                // state, lifting only when the cascade-driven role or outcome
                // changes. Follows AGENTS.md bounded-logs guidance.
                string sig = "TacticalLeafCascade|side=" + side
                    + "|leaf=" + unit
                    + "|role=" + leafAssignment.LeafRole
                    + "|task=" + leafAssignment.LeafTask
                    + "|outcome=" + outcome
                    + "|applied=" + applied;
                string key = "tactical-leaf-cascade:" + side + ":" + instanceId;
                if (!TacticalTelemetry.ShouldEmit(_lastTelemetryAt, key, sig, Time.realtimeSinceStartup, TelemetrySeconds, false))
                    return;

                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "TacticalLeafCascade",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithUnit(unit)
                        .WithDecision("TacticalLeafCascade", outcome, sig)
                        .WithField("role", leafAssignment.LeafRole.ToString())
                        .WithField("task", leafAssignment.LeafTask.ToString())
                        .WithField("chain", chain)
                        .WithField("parents", parents)
                        .WithField("applied", applied));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-leaf-cascade:telemetry-failed",
                    "EmitLeafCascadeTelemetry threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static bool IsEligibleCommandGroup(Regiment group)
        {
            try
            {
                if (group == null) return false;
                if (group.alliance == GameVars.playeralliance && !GameVars.ai_vs_ai) return false;
                GameObject gameObject = UnityObject(group);
                return gameObject != null && gameObject.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlayerProtected(Regiment group)
        {
            // Scope lives in TacticalWriteGuard (shared by every battle writer):
            // player-owned units, the W&L player's command + chain, units with
            // dlcw_isundercommander attached, and DLC-taken-over units.
            if (!WhiskeyRealism.Tactical.TacticalWriteGuard.IsPlayerProtected(group, out string reason)) return false;
            EmitPlayerProtectedDiagnostic(group, reason);
            return true;
        }

        /// <summary>
        /// Signature-deduped diagnostic emit: which W&amp;L protection check
        /// flagged a given group as player-protected. Lets smoke verify the
        /// right scope (only the player's directly-commanded unit + chain
        /// + DLC-takenover, NOT the whole player alliance).
        /// </summary>
        private static void EmitPlayerProtectedDiagnostic(Regiment group, string reason)
        {
            try
            {
                if (group == null) return;
                float nowSeconds = System.Environment.TickCount * 0.001f;
                string name = SafeName(group);
                int alliance;
                try { alliance = group.alliance; } catch { alliance = -1; }
                string sig = "alliance=" + alliance + "|name=" + name + "|reason=" + reason;
                string key = "tactical-player-protected:" + alliance + ":" + name;
                if (!WhiskeyRealism.Tactical.TacticalTelemetry.ShouldEmit(_lastPlayerProtectedTelemetryAt, key, sig, nowSeconds, PlayerProtectedTelemetrySeconds, false))
                    return;
                WhiskeyRealism.Telemetry.TelemetryRouter.Emit(
                    WhiskeyRealism.Telemetry.TelemetryLayer.Tactical,
                    WhiskeyRealism.Telemetry.TelemetryCategory.Gate,
                    "TacticalPlayerProtected",
                    WhiskeyRealism.Telemetry.TelemetrySeverity.Info,
                    ev => ev
                        .WithDecision("TacticalPlayerProtected", reason, sig)
                        .WithUnit(name)
                        .WithField("alliance", alliance)
                        .WithField("reason", reason));
            }
            catch { }
        }

        private static readonly System.Collections.Generic.Dictionary<string, float> _lastPlayerProtectedTelemetryAt = new System.Collections.Generic.Dictionary<string, float>();
        private const float PlayerProtectedTelemetrySeconds = 30f;

        private static bool HasPendingOrder(Regiment group)
        {
            return HasPendingOrder(group, allowStaleQueuedBypass: false);
        }

        // allattachedunits holds every descendant, so sub-units use their own
        // fields only (no nested CountMovingPaths walk) — O(A), not O(A²).
        private static bool HasPendingOrder(Regiment group, bool allowStaleQueuedBypass)
        {
            if (PendingOrderOn(group, allowStaleQueuedBypass, HasActiveMoveMakingProgress(group))) return true;
            try
            {
                if (group == null || group.allattachedunits == null) return false;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit == null) continue;
                    bool activeMove = !unit.pathinterrupted && unit.regimentpaths > 0 && unit.movementmode > 0;
                    if (PendingOrderOn(unit, allowStaleQueuedBypass, activeMove)) return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static bool PendingOrderOn(Regiment unit, bool allowStaleQueuedBypass, bool activeMove)
        {
            try
            {
                if (unit == null) return false;
                return TacticalOrderSettlementGate.HasBlockingPendingOrder(new TacticalOrderSettlementGate.Input
                {
                    OrderQueueCount = unit.orderqueue != null ? unit.orderqueue.Count : 0,
                    OrderState = unit.orderstate,
                    RegimentPaths = unit.regimentpaths,
                    PathInterrupted = unit.pathinterrupted,
                    MovementMode = unit.movementmode,
                    ActiveMove = activeMove,
                    AllowStaleQueuedBypass = allowStaleQueuedBypass
                });
            }
            catch
            {
                return true;
            }
        }

        private static bool HasRecentExecutorOrder(int instanceId)
        {
            return HasRecentExecutorOrder(instanceId, RecentOrderHours);
        }

        private static bool HasRecentExecutorOrder(int instanceId, float cooldownHours)
        {
            if (instanceId == 0) return true;
            if (!_lastExecutorOrderAt.TryGetValue(instanceId, out float last)) return false;
            return WithinBattleWindow(last, cooldownHours);
        }

        private static bool HasRecentFacingPulse(int instanceId, float cooldownHours)
        {
            if (instanceId == 0) return true;
            if (!_lastFacingPulseAt.TryGetValue(instanceId, out float last)) return false;
            return WithinBattleWindow(last, cooldownHours);
        }

        // True while stampHours + windowHours is ahead of the battle clock. A
        // clock that went backwards (reload, new battle) expires the window.
        private static bool WithinBattleWindow(float stampHours, float windowHours)
        {
            float age = BattleHours() - stampHours;
            return age >= 0f && age < Math.Max(0f, windowHours);
        }

        private static void MarkFacingPulse(Regiment unit)
        {
            int instanceId = SafeInstanceId(unit);
            if (instanceId == 0) return;
            _lastFacingPulseAt[instanceId] = BattleHours();
            _lastExecutorOrderAt[instanceId] = BattleHours();
        }

        private static bool HasRecentFireControlOrder(int instanceId)
        {
            if (instanceId == 0) return true;
            if (!_lastFireControlAt.TryGetValue(instanceId, out float last)) return false;
            return WithinBattleWindow(last, FireControlOrderCooldownHours);
        }

        private static bool HasActiveMoveMakingProgress(Regiment group)
        {
            try
            {
                if (group == null) return false;
                Vector3 lastWaypoint = group.lastsetwaypointposition;
                bool hasLastWaypoint = !IsDefaultVector(lastWaypoint);
                float distance = hasLastWaypoint ? SafeDistance(SafePosition(group), lastWaypoint) : 0f;
                return CommandWaypointWritePolicy.IsExecutorMovementActive(
                    group.pathinterrupted,
                    CountMovingPaths(group),
                    group.movementmode,
                    group.groupsubordinatesmoving,
                    group.groupsubordinatesmovingnonai,
                    hasLastWaypoint,
                    distance,
                    MinWaypointDistance);
            }
            catch
            {
                return true;
            }
        }

        private static bool HasCloseEngagement(Regiment group)
        {
            try
            {
                return group != null &&
                    (group.combatstatussubordinates > 0.2f ||
                     group.combatstatussubordinatesengagednonari > 0.2f ||
                     group.groupenemiesinrange > 0f);
            }
            catch
            {
                return true;
            }
        }

        private static bool HasLocalFlankRisk(Regiment group)
        {
            try
            {
                return group != null &&
                    (group.flanksthreated > 0f || group.outflanked > 0);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFireControlUnit(Regiment unit)
        {
            try
            {
                return unit != null &&
                    (unit.unittyp == TacticalUnitType.Infantry ||
                     unit.unittyp == TacticalUnitType.Skirmisher ||
                     unit.unittyp == TacticalUnitType.Cavalry);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsArtilleryUnit(Regiment unit)
        {
            try { return unit != null && (unit.unittyp == TacticalUnitType.Artillery || unit.guns > 0); }
            catch { return true; }
        }

        // Vanilla UnlimberArtilleryAIMicro only turns toward
        // GetClosestEnemyUnit(firerange) (3487).
        private static bool ThreatWithinFireRange(Regiment unit)
        {
            float range = SafeFireRange(unit);
            float distance = SafeEnemyDistance(unit);
            return range > 0f && distance > 0f && distance <= range;
        }

        private static bool IsCombatRegiment(Regiment unit)
        {
            try
            {
                return unit != null &&
                    unit.unittyp <= TacticalUnitType.MaxCombat &&
                    (unit.unittyp == TacticalUnitType.Infantry ||
                     unit.unittyp == TacticalUnitType.Cavalry ||
                     unit.unittyp == TacticalUnitType.Artillery ||
                     unit.unittyp == TacticalUnitType.Skirmisher);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasVisibleTarget(Regiment group)
        {
            try
            {
                return group != null &&
                    (TacticalFogOfWarContact.HasVisibleEnemy(group) ||
                     group.groupenemiesinrange > 0f);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasFowVisibleEnemy(Regiment group)
        {
            try
            {
                return TacticalFogOfWarContact.HasVisibleEnemy(group);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasRecentReceivedFire(Regiment group)
        {
            try
            {
                if (group == null) return false;
                if (HasReceivedFire(group)) return true;
                if (group.allattachedunits == null) return false;

                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    if (HasReceivedFire(group.allattachedunits[i])) return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool HasReceivedFire(Regiment unit)
        {
            try
            {
                return unit != null && unit.receivedfire != null && unit.receivedfire.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static Regiment ClosestEnemy(Regiment group)
        {
            try
            {
                return TacticalFogOfWarContact.ClosestVisibleEnemy(group);
            }
            catch
            {
                return null;
            }
        }

        private static float SafeEnemyDistance(Regiment group)
        {
            try
            {
                if (!TacticalFogOfWarContact.TryClosestVisibleEnemy(group, out _, out float value)) return 0f;
                return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f ? value : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SafeMorale(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                float value = group.morale;
                return !float.IsNaN(value) && !float.IsInfinity(value) ? value : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        private static float SafeFireRange(Regiment group)
        {
            try
            {
                if (group == null) return 0f;
                float value = group.firerange;
                return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f ? value : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SafeEffectiveFireRange(Regiment unit)
        {
            try
            {
                if (unit == null) return 0f;
                float value = unit.GetFireRange(true);
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value > 0f)
                    return value;
            }
            catch { }

            return SafeFireRange(unit);
        }

        private static float SafeAmmoRatio(Regiment unit)
        {
            try
            {
                if (unit == null || unit.ammo == null || unit.ammo.Length == 0) return 1f;
                float total = 0f;
                int count = 0;
                for (int i = 0; i < unit.ammo.Length; i++)
                {
                    float value = unit.ammo[i];
                    if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                    total += Math.Max(0f, value);
                    count++;
                }

                return count > 0 ? Clamp01(total / count) : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        private static float SafeFatigue(Regiment unit)
        {
            try
            {
                if (unit == null) return 0f;
                float value = unit.fatigue;
                return !float.IsNaN(value) && !float.IsInfinity(value) ? Clamp01(value) : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool UnitInCover(Regiment unit)
        {
            try
            {
                return unit != null && unit.covervalue > 0.05f && unit.coverobject != 3;
            }
            catch
            {
                return false;
            }
        }

        private static bool EnemyAdvancing(Regiment target)
        {
            try
            {
                return target != null && (target.regimentpaths > 0 || target.movementmode > 0);
            }
            catch
            {
                return false;
            }
        }

        private static bool UnitLoadedVolley(Regiment unit)
        {
            try
            {
                if (unit == null) return false;
                if (unit.allspritesreloaded) return true;
                return GameVars.currenttimefromstart - unit.lastfiredshottime > 0.05f;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsChargeOrdered(Regiment unit)
        {
            try
            {
                return unit != null &&
                    (unit.combatbehaviorordered == TacticalFireControlDoctrine.InfantryCharge ||
                     unit.combatbehaviorordered == TacticalFireControlDoctrine.CavalryCharge ||
                     unit.movementmode == 3 ||
                     (UnityEngine.Object)unit.chargetarget != null);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAlignedToTarget(Regiment unit, Regiment target)
        {
            try
            {
                if (unit == null || target == null) return true;
                Vector3 own = SafePosition(unit);
                Vector3 enemy = SafePosition(target);
                if (IsDefaultVector(own) || IsDefaultVector(enemy)) return true;

                Vector3 direction = enemy - own;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.01f) return true;
                direction.Normalize();

                Vector3 forward = unit.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) return true;
                forward.Normalize();

                return Vector3.Dot(forward, direction) >= 0.25f;
            }
            catch
            {
                return true;
            }
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static bool HasArtilleryCapability(Regiment group)
        {
            try
            {
                if (group == null) return false;
                if (group.unittyp == TacticalUnitType.Artillery || group.guns > 0) return true;
                if (group.allattachedunits == null) return false;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null && (unit.unittyp == TacticalUnitType.Artillery || unit.guns > 0)) return true;
                }
            }
            catch { }

            return false;
        }

        private static bool HasCavalryCapability(Regiment group)
        {
            try
            {
                if (group == null) return false;
                if (group.unittyp == TacticalUnitType.Cavalry) return true;
                if (group.allattachedunits == null) return false;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null && unit.unittyp == TacticalUnitType.Cavalry) return true;
                }
            }
            catch { }

            return false;
        }

        private static TacticalCavalryFollowMode CavalryFollowModeFor(CommandNodeRole role, CommandTaskType task)
        {
            if (task == CommandTaskType.GuardFlank || role == CommandNodeRole.Reserve)
                return TacticalCavalryFollowMode.Guard;
            if (task == CommandTaskType.Screen || role == CommandNodeRole.ScreeningForce)
                return TacticalCavalryFollowMode.Screen;
            if (task == CommandTaskType.Scout || task == CommandTaskType.Probe || role == CommandNodeRole.Probe)
                return TacticalCavalryFollowMode.Scout;
            if (task == CommandTaskType.AttackObjective || task == CommandTaskType.SupportAttack)
                return TacticalCavalryFollowMode.Raid;
            return TacticalCavalryFollowMode.None;
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

        private static bool TargetInFort(Regiment target)
        {
            try { return target != null && target.fortinrange != null; }
            catch { return false; }
        }

        private static bool TargetHasCloseSupport(Regiment target)
        {
            try
            {
                if (target == null || target.unitrange == null || target.unitrange.temp_owninrangeregs == null) return false;
                for (int i = 0; i < target.unitrange.temp_owninrangeregs.Count; i++)
                {
                    Regiment support = target.unitrange.temp_owninrangeregs[i];
                    if (support == null || support.isrouted || support.markedforrout) continue;
                    if (support.unittyp == TacticalUnitType.Infantry || support.unittyp == TacticalUnitType.Artillery)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static float CavalryFearAdvantage(Regiment group)
        {
            try
            {
                if (group == null) return 0.5f;
                float morale = SafeMorale(group);
                float closeThreat = HasLocalFlankRisk(group) ? -0.25f : 0f;
                return Math.Max(0f, Math.Min(1f, morale + closeThreat));
            }
            catch
            {
                return 0.5f;
            }
        }

        // Vanilla command parent: BattleUnits.GrabNextParentUnit (86886) walks
        // transform.parent and returns the unit itself at the "Units" root;
        // GetAttachedUnitsReg(directonly) (119854) defines direct subordinates
        // the same way. Groups under the root have no parent node (null) —
        // the old "role-parent" fallback lumped every such group of an echelon
        // into one play.
        private static string ParentNodeKey(Regiment group)
        {
            try
            {
                if (group == null) return null;
                GameObject self = ((Component)group).gameObject;
                GameObject parent = BattleUnits.GrabNextParentUnit(self);
                if (parent == null || parent == self) return null;
                return "parent-go:" + parent.GetInstanceID();
            }
            catch
            {
                return null;
            }
        }

        // Vanilla GetBattleType (86095) with readiness, as AdjustGroupAIStance
        // uses it (4252): 2 = this alliance holds the objectives (defending).
        private static bool SideDefending(BattleUnits bunits, int alliance)
        {
            try { return bunits != null && bunits.GetBattleType(alliance, usereadinesswhendraw: true) == 2; }
            catch { return false; }
        }

        private static TacticalRefuseFlankIntent.Decision ResolveRefusedFlank(
            Regiment group,
            CommandTaskType task,
            bool closeEngaged,
            bool flankRisk)
        {
            if (!closeEngaged || !flankRisk)
                return TacticalRefuseFlankIntent.Decision.NoRefuse;

            int candidate = CommandFormationCorrection.RefuseFlankParameter(
                TacticalRefuseFlankIntent.Decision.RefuseLeft,
                task,
                closeEngaged);
            if (candidate < 0)
                return TacticalRefuseFlankIntent.Decision.NoRefuse;

            try
            {
                Vector3 own = SafePosition(group);
                if (IsDefaultVector(own)) return TacticalRefuseFlankIntent.Decision.NoRefuse;
                if (!TryClosestEnemyPoint(group, out Vector3 threat)) return TacticalRefuseFlankIntent.Decision.NoRefuse;

                float threatRotation = CommandFormationCorrection.ThreatFacingRotationDegrees(
                    own.x,
                    own.z,
                    threat.x,
                    threat.z);
                return CommandFormationCorrection.RefuseDecisionForThreatFacing(
                    SafeRotationY(group),
                    threatRotation);
            }
            catch
            {
                return TacticalRefuseFlankIntent.Decision.NoRefuse;
            }
        }

        private static int TargetFormationForTask(CommandTaskType task, Regiment group)
        {
            if (!CanUseGroupFormation(group)) return -1;
            return CommandFormationCorrection.TargetFormationForTask(task, SafeGroupFormation(group));
        }

        private static bool CanUseGroupFormation(Regiment group)
        {
            try
            {
                return group != null && group.unittyp > 13;
            }
            catch
            {
                return false;
            }
        }

        // Stance before waypoint (vanilla ChangeStance → SetWaypoint pipeline).
        // Goes through the single stance owner: refused for non-AI groups
        // (attached sub-units, nested leaf brigades), deduped on
        // ai_stanceordered, and held against #45 / vanilla flips.
        private static void EnsureStanceMatchesTask(BattleUnits bunits, Regiment group, CommandTaskType task)
        {
            TacticalStanceWriter.EnsureStanceForTask(bunits, group, task, null, StanceWriteSource.Executor);
        }

        // Doctrine clock (battle hours, frozen across the night end-of-day
        // cycle). Every doctrine timer in this patch runs on it; only
        // comparisons against vanilla-stamped fields read the raw clock.
        private static float BattleHours()
        {
            return (float)TacticalBattleCoordinator.DoctrineBattleHours;
        }

        private static int SafeEodCycle(BattleUnits bunits)
        {
            try { return bunits != null ? bunits.eodcycle : 1; }
            catch { return 1; }
        }

        private static bool SafeRouted(Regiment group)
        {
            try { return group == null || group.isrouted || group.markedforrout; }
            catch { return true; }
        }

        private static int SafeRegimentPaths(Regiment group)
        {
            try { return group != null ? Math.Max(0, group.regimentpaths) : 0; }
            catch { return 0; }
        }

        private static int SafeGroupFormation(Regiment group)
        {
            try { return group != null ? group.groupformation : -1; }
            catch { return -1; }
        }

        private static int SafeFormation(Regiment group)
        {
            try { return group != null ? group.formation : -1; }
            catch { return -1; }
        }

        private static int SafeFormationOrdered(Regiment group)
        {
            try { return group != null ? group.formationordered : -1; }
            catch { return -1; }
        }

        private static Vector3 SafePosition(Regiment group)
        {
            try { return group != null ? group.transform.position : default(Vector3); }
            catch { return default(Vector3); }
        }

        // BattleUnits keeps the scene's BattlefieldSetup in a private static
        // (set at 79055/79132, nulled on destroy at 79230); read it instead of
        // GameObject.Find("GameController") per call.
        private static BattlefieldSetup SafeBattlefieldSetup()
        {
            try
            {
                if (_bfsField == null) _bfsField = AccessTools.Field(typeof(BattleUnits), "bfs");
                return _bfsField != null ? _bfsField.GetValue(null) as BattlefieldSetup : null;
            }
            catch
            {
                return null;
            }
        }

        private static float SafeRotationY(Regiment group)
        {
            try
            {
                if (group == null) return float.NaN;
                return group.transform.rotation.eulerAngles.y;
            }
            catch
            {
                return float.NaN;
            }
        }

        private static float SafeBattleY()
        {
            return 0f;
        }

        private static bool IsDefaultVector(Vector3 value)
        {
            return float.IsNaN(value.x) ||
                float.IsNaN(value.y) ||
                float.IsNaN(value.z) ||
                float.IsInfinity(value.x) ||
                float.IsInfinity(value.y) ||
                float.IsInfinity(value.z) ||
                (Math.Abs(value.x) < 0.01f && Math.Abs(value.z) < 0.01f);
        }

        private static float SafeDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static void EmitPostureTelemetry(
            int side,
            Regiment group,
            CommandNodeOperationalState state,
            PostureExecutionDecision decision,
            TacticalIdleClassification idle,
            bool applied,
            string extraReason)
        {
            string reason = string.IsNullOrEmpty(extraReason) ? decision.Reason : extraReason;
            string signature = side + "|" + SafeInstanceId(group) + "|" + state.Task + "|" + decision.Action +
                "|" + decision.Target + "|" + reason + "|" + applied + "|" + idle;
            string key = "tactical-command-posture:" + side + ":" + SafeInstanceId(group);
            if (!TacticalTelemetry.ShouldEmit(_lastTelemetryAt, key, signature, Time.realtimeSinceStartup, TelemetrySeconds, false))
                return;

            TelemetryRouter.Emit(
                TelemetryLayer.Tactical,
                TelemetryCategory.Decision,
                "TacticalCommandPosture",
                TelemetrySeverity.Info,
                ev => ev
                    .WithSide(side)
                    .WithUnit(SafeName(group))
                    .WithDecision(decision.Action.ToString(), reason, signature)
                    .WithField("node", TacticalOperationsTelemetry.SafeToken(state.NodeId))
                    .WithField("group", SafeName(group) + "#" + SafeInstanceId(group))
                    .WithField("task", state.Task.ToString())
                    .WithField("confidence", 1.0)
                    .WithField("score", applied ? 1.0 : 0.0)
                    .WithField("selectedTarget", decision.Target.ToString())
                    .WithField("gateResult", applied ? "allow" : "deny")
                    .WithField("gateReason", TacticalOperationsTelemetry.SafeToken(reason))
                    .WithField("writeAction", decision.Action.ToString())
                    .WithField("writeResult", applied ? "applied" : "not-applied")
                    .WithField("target", decision.Target.ToString())
                    .WithField("applied", applied)
                    .WithField("currentFormation", SafeGroupFormation(group))
                    .WithField("paths", SafeRegimentPaths(group))
                    .WithField("movingFlag", SafeGroupMovingFlag(group))
                    .WithField("pathInterrupted", group != null && group.pathinterrupted)
                    .WithField("activeMove", HasActiveMoveMakingProgress(group))
                    // Microshift-fix diagnostic fields (2026-05-21). Smoke can now verify
                    // whether near-duplicate waypoint targets are being deduped correctly
                    // by comparing currentWaypointX/Z to targetX/Z in tactical.jsonl.
                    .WithField("currentWaypointX", group != null ? group.lastsetwaypointposition.x : 0f)
                    .WithField("currentWaypointZ", group != null ? group.lastsetwaypointposition.z : 0f)
                    .WithField("groupPositionX", group != null ? SafePosition(group).x : 0f)
                    .WithField("groupPositionZ", group != null ? SafePosition(group).z : 0f));
        }

        private static void EmitFacingPulseTelemetry(
            int side,
            Regiment parent,
            Regiment unit,
            CommandNodeOperationalState state,
            TacticalFacingPulseScope scope,
            TacticalFacingPulseDecision decision,
            TacticalFacingThreatSource source,
            bool applied)
        {
            string signature = side + "|" + SafeInstanceId(unit) + "|" + scope + "|" + state.Task +
                "|" + decision.Action + "|" + decision.Reason + "|" + applied + "|" +
                BucketCoordinate(SafeRotationY(unit)) + "|" + BucketCoordinate(decision.TargetFacingDegrees);
            string key = "tactical-facing-pulse:" + side + ":" + SafeInstanceId(unit);
            if (!TacticalTelemetry.ShouldEmit(_lastTelemetryAt, key, signature, Time.realtimeSinceStartup, TelemetrySeconds, false))
                return;

            TelemetryRouter.Emit(
                TelemetryLayer.Tactical,
                decision.ShouldWrite ? TelemetryCategory.Write : TelemetryCategory.Decision,
                "TacticalFacingPulse",
                TelemetrySeverity.Info,
                ev => ev
                    .WithSide(side)
                    .WithUnit(SafeName(unit))
                    .WithDecision(decision.Action.ToString(), decision.Reason, signature)
                    .WithField("scope", scope.ToString())
                    .WithField("parent", SafeName(parent) + "#" + SafeInstanceId(parent))
                    .WithField("unit", SafeName(unit) + "#" + SafeInstanceId(unit))
                    .WithField("node", TacticalOperationsTelemetry.SafeToken(state.NodeId))
                    .WithField("task", state.Task.ToString())
                    .WithField("currentFormation", SafeFormation(unit))
                    .WithField("targetFormation", decision.TargetFormation)
                    .WithField("currentFacing", SafeRotationY(unit))
                    .WithField("targetFacing", decision.TargetFacingDegrees)
                    .WithField("threatSource", source.ToString())
                    .WithField("urgent", decision.Urgent)
                    .WithField("writeResult", applied ? "applied" : "not-applied")
                    .WithField("gateReason", TacticalOperationsTelemetry.SafeToken(decision.Reason)));
        }

        private static string SafeGroupMovingFlag(Regiment group)
        {
            try
            {
                if (group == null) return "0/0";
                return group.groupsubordinatesmoving.ToString("0.##") + "/" +
                    group.groupsubordinatesmovingnotfar.ToString("0.##");
            }
            catch
            {
                return "?/?";
            }
        }

        private static bool EnabledForWrites()
        {
            try
            {
                return Plugin.Instance != null &&
                    Plugin.Instance.Enabled.Value &&
                    TacticalCommanderModePolicy.AllowsWrites(Plugin.Instance.TacticalCommanderModeValue);
            }
            catch
            {
                return false;
            }
        }

        private static bool ContactFacingPulseEnabled()
        {
            try
            {
                return EnabledForWrites() &&
                    Plugin.EnableTacticalContactFacingPulse != null &&
                    Plugin.EnableTacticalContactFacingPulse.Value;
            }
            catch
            {
                return false;
            }
        }

        internal static void Reset()
        {
            _lastExecutorOrderAt.Clear();
            _lastFacingPulseAt.Clear();
            _lastFireControlAt.Clear();
            _lastFormationWriteAt.Clear();
            _writeBackoffUntil.Clear();
            _friendlyGrid.Clear();
            _friendlyGridAlliance = -1;
            _brigadeTaskHoldState.Clear();
            _lastCourierAt.Clear();
            _lastOutboundOrderSignatureByGroup.Clear();
            _lastTelemetryAt.Clear();
            _mainArrivalHold.Clear();
            _supportMainPositionsByAlliance.Clear();
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return Convert.ToInt32(cache.GetValue(instance));
            }
            catch
            {
                return fallback;
            }
        }

        private static T SafeField<T>(object instance, ref FieldInfo cache, string name) where T : class
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as T : null;
            }
            catch
            {
                return null;
            }
        }

        private static IList SafeList(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as IList : null;
            }
            catch
            {
                return null;
            }
        }

        private static GameObject UnityObject(Regiment unit)
        {
            try { return unit != null ? unit.gameObject : null; }
            catch { return null; }
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try { return obj != null ? obj.GetInstanceID() : 0; }
            catch { return 0; }
        }

        private static string SafeName(Regiment group)
        {
            try { return group != null ? TacticalCurrentOrderSignature.Safe(group.name) : "-"; }
            catch { return "-"; }
        }
    }
}
