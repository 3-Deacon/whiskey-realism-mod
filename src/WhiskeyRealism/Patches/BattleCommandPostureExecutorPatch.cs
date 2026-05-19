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
        private const float RecentOrderSeconds = 30f;
        private const float UrgentFormationRetrySeconds = 5f;
        private const float TelemetrySeconds = 30f;
        private const float ObjectiveApproachStandOff = 75f;
        private const float AssemblyStandOff = 200f;
        private const float ReserveStandOff = 350f;
        private const float FallbackStandOff = 250f;
        private const float MaxConservativeWaypointDistance = 2500f;
        private const float MinWaypointDistance = 15f;
        private const float VanillaBlockedMoveDistance = 150f;
        private const float FacingRefreshToleranceDegrees = 15f;
        private const float OutboundCourierIntervalSeconds = 900f;
        private const float FireControlOrderCooldownSeconds = 45f;
        private const float BrigadeFacingPulseCooldownSeconds = 12f;
        private const float RegimentFacingPulseCooldownSeconds = 5f;
        private const int MaxRegimentFacingPulseWritesPerGroup = 4;

        private static readonly Dictionary<int, float> _lastExecutorOrderAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _lastFacingPulseAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _lastFireControlAt = new Dictionary<int, float>();
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

        private static void Apply(AIBattle battle)
        {
            int state = SafeIntField(battle, ref _stateField, "state", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int isPlayerAiOrFeud = SafeIntField(battle, ref _isPlayerAiOrFeudField, "isplayeraiorfeud", -1);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var units = SafeList(battle, ref _unitsUsedField, "unitsused");

            if (state < 5 || macro < 0 || side < 0 || bunits == null || units == null) return;
            if (isPlayerAiOrFeud != 0 && !GameVars.ai_vs_ai) return;

            Dictionary<string, TacticalDivisionPlayOrder> runtimePlayOrders = BuildRuntimeDivisionPlayOrders(units);
            var iteratedInstanceIds = new HashSet<int>();
            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (!IsEligibleCommandGroup(group)) continue;
                int unitInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                if (unitInstanceId != 0) iteratedInstanceIds.Add(unitInstanceId);
                TryApplyGroup(battle, bunits, side, group, runtimePlayOrders);
            }

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

            TacticalRefuseFlankIntent.Decision refusedFlank = ResolveRefusedFlank(group, state.Task, closeEngaged, flankRisk);
            int targetFormation = TargetFormationForTask(state.Task, group);
            bool visibleFormationMismatch = CommandFormationCorrection.NeedsCorrection(
                SafeFormation(group),
                SafeFormationOrdered(group),
                SafeGroupFormation(group),
                targetFormation);
            bool allowStaleQueuedBypass = CanBypassStaleQueuedOrder(state.Task);
            bool allowPendingLocalFormation = CommandFormationCorrection.CanBypassPendingOrderForLocalFormation(
                closeEngaged,
                flankRisk,
                visibleFormationMismatch,
                state.Task);
            bool orderPending = HasPendingOrder(group, allowStaleQueuedBypass) && !allowPendingLocalFormation;
            float recentOrderSeconds = CommandFormationCorrection.RecentOrderCooldownSeconds(
                closeEngaged,
                visibleFormationMismatch,
                state.Task,
                RecentOrderSeconds,
                UrgentFormationRetrySeconds);
            bool recentOrder = CommandPostureExecutor.ShouldBlockForRecentOrder(
                state.Task,
                physical,
                HasRecentExecutorOrder(instanceId, recentOrderSeconds));
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

            PostureExecutionDecision decision;
            using (TelemetryPerf.Scope("tactical.posture-executor", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                decision = hasDoctrineOrder
                    ? CommandPostureExecutor.Decide(doctrineOrder, physical, eligibility, Time.realtimeSinceStartup)
                    : CommandPostureExecutor.Decide(state, physical, eligibility);
            }
            if (decision.Action == PostureExecutionAction.NoWrite)
            {
                if (TryApplyContactFacingPulse(bunits, side, group, state, closeEngaged, flankRisk))
                {
                    _lastExecutorOrderAt[instanceId] = Time.realtimeSinceStartup;
                    EmitPostureTelemetry(side, group, state, decision, idle, applied: true, extraReason: "contact-facing-pulse");
                    return;
                }

                if (CanWrite(group, state.Task, eligibility, physical, recentOrderSeconds, allowPendingLocalFormation, allowStaleQueuedBypass) &&
                    TryApplyFireControl(bunits, group, state, out TacticalFireControlDecision fireDecision))
                {
                    _lastExecutorOrderAt[instanceId] = Time.realtimeSinceStartup;
                    EmitPostureTelemetry(side, group, state, decision, idle, applied: true, extraReason: "fire-control-" + fireDecision.Reason);
                    return;
                }

                EmitPostureTelemetry(side, group, state, decision, idle, applied: false, extraReason: decision.Reason);
                return;
            }

            if (!CanWrite(group, state.Task, eligibility, physical, recentOrderSeconds, allowPendingLocalFormation, allowStaleQueuedBypass))
            {
                EmitPostureTelemetry(side, group, state, decision, idle, applied: false, extraReason: "write-gate-denied");
                return;
            }

            bool hasTarget = TryResolveTarget(group, orchestrator, state, doctrineOrder, decision.Target, out Vector3 target);
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
                _lastExecutorOrderAt[instanceId] = Time.realtimeSinceStartup;
                RecordOutboundOrder(group, state, outboundSignature);
                if (hasDoctrineOrder && doctrineOrder.Delivery == TacticalOrderDelivery.Courier)
                    _lastCourierAt[CourierParentKey(side, doctrineOrder)] = Time.realtimeSinceStartup;
                EmitPostureTelemetry(side, group, state, decision, idle, applied: true, extraReason: decision.Reason);
            }
            else
            {
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

        private static Dictionary<string, TacticalDivisionPlayOrder> BuildRuntimeDivisionPlayOrders(IList units)
        {
            var result = new Dictionary<string, TacticalDivisionPlayOrder>();
            if (units == null) return result;

            var byParent = new Dictionary<string, List<RuntimePlayChild>>();
            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (!IsEligibleCommandGroup(group)) continue;
                if (!TryResolveLedgerState(group, out CommandNodeOperationalState state, out _)) continue;

                string parentKey = ParentNodeKey(group, state);
                if (string.IsNullOrWhiteSpace(parentKey)) continue;
                if (!byParent.TryGetValue(parentKey, out List<RuntimePlayChild> children))
                {
                    children = new List<RuntimePlayChild>();
                    byParent[parentKey] = children;
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
                        hasOrders: HasPendingOrder(group) || HasActiveMoveMakingProgress(group))));
            }

            foreach (var pair in byParent)
            {
                if (pair.Value.Count < 2) continue;
                var subordinates = new TacticalDivisionPlaySubordinate[pair.Value.Count];
                for (int i = 0; i < pair.Value.Count; i++) subordinates[i] = pair.Value[i].Subordinate;

                TacticalDivisionPlayDecision decision = TacticalDivisionPlayExecutor.Decide(
                    new TacticalDivisionPlayInput(pair.Key, Time.realtimeSinceStartup, subordinates));
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
                    .WithDelivery(TacticalOrderDelivery.Courier, ParentNodeKey(group, state), OutboundCourierIntervalSeconds, follow.Reason);
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
            string parentKey = CourierParentKey(side, order);
            _lastCourierAt.TryGetValue(parentKey, out float lastCourierAt);
            decision = TacticalOutboundCourierCadence.Decide(
                new TacticalOutboundCourierInput(
                    parentKey,
                    order.NodeId,
                    Time.realtimeSinceStartup,
                    lastCourierAt,
                    pendingOrderOnChild,
                    commanderHasOrder: order.HasPurpose,
                    commanderHasPlay: order.Task != CommandTaskType.None,
                    childIsPlayerControlled: playerProtected,
                    courierIntervalSeconds: order.CourierIntervalSeconds,
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
                    bool formed = SetFormation(bunits, group, state.Task, targetFormation, closeEngaged, refusedFlank);
                    return SetWaypoint(battle, bunits, group, state.Task, target) || formed;
                case PostureExecutionAction.SetWaypoint:
                case PostureExecutionAction.ReleaseReserve:
                case PostureExecutionAction.FallbackToLine:
                case PostureExecutionAction.RecoverInterruptedOrder:
                    return hasTarget && SetWaypoint(battle, bunits, group, state.Task, target);
                case PostureExecutionAction.ChangeStance:
                    return ChangeStance(bunits, group, StanceForTask(state.Task));
                default:
                    return false;
            }
        }

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
            bool needsRefusedFlank = refuseFlankParameter >= 0;
            if (!needsFormation && !needsFacing && !needsRefusedFlank) return false;
            bool useNewPath = CommandFormationCorrection.ShouldUseNewPathForFormationCorrection(
                closeEngaged,
                needsFormation);

            bunits.SetGroupFormation(
                group,
                targetFormation,
                manualfinalrotation: hasThreatFacing ? manualFinalRotation : -1f,
                targetpos: default(Vector3),
                immediateplacement: false,
                newpath: useNewPath,
                modifylastwaypoint: !useNewPath && needsFacing,
                newstate: 2,
                refuseflank: refuseFlankParameter,
                ignoredeplyomentzone: false,
                skiprotation: false,
                showmovementoptions: false,
                placeentrenchments: false,
                adjustbyterrainshape: true);
            return true;
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

            if (TryQueueBlockedMovingOrder(battle, group, task, target))
            {
                MarkGroupMovementStarted(group, task);
                return true;
            }

            int beforeMovingPaths = CountMovingPaths(group);
            bool useOrderDelay = CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(task, battleActive: true);

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

            if (!MovementMaterialized(group, beforeMovingPaths))
                return false;

            MarkGroupMovementStarted(group, task);
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

        private static bool MovementMaterialized(Regiment group, int beforeMovingPaths)
        {
            try
            {
                if (group == null) return false;
                if (group.regimentpaths > 0) return true;
                return CountMovingPaths(group) > beforeMovingPaths;
            }
            catch
            {
                return false;
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

        private static bool ChangeStance(BattleUnits bunits, Regiment group, int stance)
        {
            if (stance < 0 || stance > 3) return false;
            if (group.ai_stanceordered == stance) return false;

            GameObject gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return false;

            bunits.ChangeStance(gameObject, stance, immediate: false, overwriteaigroups: false);
            return true;
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
            if (!TryEvaluateFacingPulse(
                    unit,
                    state.Task,
                    TacticalFacingPulseScope.Regiment,
                    targetFormation: 0,
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
                ? RegimentFacingPulseCooldownSeconds
                : BrigadeFacingPulseCooldownSeconds;

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
                FacingRefreshToleranceDegrees));

            return decision.Action != TacticalFacingPulseAction.NoWrite || source != TacticalFacingThreatSource.None;
        }

        private static bool ApplyRegimentFacingPulse(Regiment unit, TacticalFacingPulseDecision decision)
        {
            if (unit == null || !decision.ShouldWrite) return false;
            if (decision.TargetFormation < 0 || decision.TargetFormation > 4) return false;

            try
            {
                unit.ChangeRegimentFormation(GameVars.SetFormationParam(
                    decision.TargetFormation,
                    manualset: true,
                    newmounted: -1,
                    manualrotationtarget: decision.TargetFacingDegrees,
                    setalsoformationorderedvariable: true));
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
                if (decision.RecommendedCombatBehavior == unit.combatbehaviorordered) return false;

                bunits.ChangeCombatBehavior(gameObject, decision.RecommendedCombatBehavior);
                _lastFireControlAt[instanceId] = Time.realtimeSinceStartup;
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

        private static float EstimateFriendlyFrontBlocker(Regiment group, Vector3 own, Vector3 candidate)
        {
            try
            {
                if (group == null || IsDefaultVector(own) || IsDefaultVector(candidate))
                    return 0f;
                var units = BattleUnits.completeunitlist as IList;
                if (units == null || units.Count == 0) return 0f;

                Vector3 axis = candidate - own;
                axis.y = 0f;
                float axisLength = axis.magnitude;
                if (axisLength < 1f) return 0f;
                axis.Normalize();

                float worst = 0f;
                for (int i = 0; i < units.Count; i++)
                {
                    var friend = units[i] as Regiment;
                    if (friend == null || friend == group) continue;
                    if (friend.alliance != group.alliance) continue;
                    if (friend.isrouted || friend.markedforrout || friend.permanentlydetached) continue;
                    if (friend.unittyp > 14) continue;

                    Vector3 pos = SafePosition(friend);
                    if (IsDefaultVector(pos)) continue;
                    Vector3 rel = pos - own;
                    rel.y = 0f;
                    float forward = Vector3.Dot(rel, axis);
                    if (forward < 30f || forward > axisLength + 120f) continue;

                    float lateral = (rel - axis * forward).magnitude;
                    if (lateral > 120f) continue;
                    float distanceToCandidate = Vector3.Distance(pos, candidate);
                    float proximity = distanceToCandidate <= 80f ? 1f : Math.Max(0f, 1f - ((distanceToCandidate - 80f) / 220f));
                    float lateralBlock = 1f - Math.Min(1f, lateral / 120f);
                    worst = Math.Max(worst, Math.Max(proximity, lateralBlock * 0.8f));
                }

                return Math.Min(1f, worst);
            }
            catch
            {
                return 0f;
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
            float recentOrderSeconds,
            bool allowPendingLocalFormation,
            bool allowStaleQueuedBypass)
        {
            if (!eligibility.ModeAllowsWrites) return false;
            if (eligibility.PlayerProtected || physical.PlayerProtected) return false;
            if (eligibility.Routed || physical.Routed) return false;
            if (eligibility.OrderPending) return false;
            if (eligibility.RecentOrder) return false;
            if (physical.ActiveMove) return false;
            if (!allowPendingLocalFormation && HasPendingOrder(group, allowStaleQueuedBypass)) return false;
            if (CommandPostureExecutor.ShouldBlockForRecentOrder(
                    task,
                    physical,
                    HasRecentExecutorOrder(SafeInstanceId(group), recentOrderSeconds)))
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

        // Cache for extended probe lists per alliance. completeunitlist may have
        // 200+ regiments and walking it twice per Postfix per side is expensive.
        // Cache invalidated by a coarse signature (alliance unit count + frame
        // index) so a fresh probe build happens only when the unit roster
        // materially changes between ticks.
        private static readonly Dictionary<int, CachedExtendedProbes> _extendedProbeCache = new Dictionary<int, CachedExtendedProbes>();

        private readonly struct CachedExtendedProbes
        {
            public CachedExtendedProbes(int unitCount, int frame, IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> probes)
            {
                UnitCount = unitCount; Frame = frame; Probes = probes;
            }
            public int UnitCount { get; }
            public int Frame { get; }
            public IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> Probes { get; }
        }

        private static IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> GetCachedExtendedProbes(int allianceId)
        {
            int frame = -1;
            int unitCount = -1;
            try
            {
                var units = BattleUnits.completeunitlist as System.Collections.IList;
                unitCount = units != null ? units.Count : 0;
                frame = UnityEngine.Time.frameCount;
            }
            catch { }
            if (_extendedProbeCache.TryGetValue(allianceId, out var cached) &&
                cached.UnitCount == unitCount &&
                cached.Frame == frame &&
                cached.Probes != null)
                return cached.Probes;
            var probes = DirectChildDiscovery.BuildExtendedProbesForAlliance(allianceId);
            _extendedProbeCache[allianceId] = new CachedExtendedProbes(unitCount, frame, probes);
            return probes;
        }

        internal static void ResetExtendedProbeCache()
        {
            _extendedProbeCache.Clear();
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

                var sideOrch = TacticalBattleCoordinator.GetSideOrchestrator(allianceId);
                ArmyOrchestrator army = sideOrch?.Army;
                if (army == null) return;

                var extendedProbes = GetCachedExtendedProbes(allianceId);
                if (extendedProbes == null || extendedProbes.Count == 0) return;

                var tree = TacticalCommandTreeProbe.BuildTree(extendedProbes, 0);
                army.UpdateLeafBrigadeMap(tree);

                // Refresh the reinforcement-opportunity decision off the same
                // Postfix tick as the leaf-map update. Reads BattleUnits.
                // scheduledarrival + sideinformation; bounded by the doctrine's
                // own pure deterministic path. Default-on rollback via
                // Enable Tactical Reinforcement Opportunity Doctrine config flag.
                if (Plugin.EnableTacticalReinforcementOpportunityDoctrine != null
                    && Plugin.EnableTacticalReinforcementOpportunityDoctrine.Value)
                {
                    // Refresh live commander initiative from vanilla using the
                    // proven pattern from BattleMacroStrategyPatch.CommanderAggression01:
                    // bunits.GetCommandingOfficerFromSide(side) → GameVars.commander[id]
                    // .GetCommanderInitiative(). The orchestrator caches the validated
                    // value; doctrine consumes it via army.LiveCommanderInitiative01.
                    float liveInitiative01 = ReadLiveCommanderInitiative01(bunits, side);
                    army.UpdateLiveCommanderInitiative(liveInitiative01);

                    // Aggression remains the PersonalityVector-derived historical
                    // baseline (Hood vs McClellan etc.) — distinct from live
                    // initiative which may differ per vanilla state. Map from
                    // [-1, 1] PersonalityVector range to [0, 1] doctrine domain.
                    float commanderAggression01 = (army.CommanderPersonality.Aggression + 1f) * 0.5f;
                    if (commanderAggression01 < 0f) commanderAggression01 = 0f;
                    if (commanderAggression01 > 1f) commanderAggression01 = 1f;

                    var forceBalance = ArmyEvidenceBuilder.BuildForceBalance(
                        battle, allianceId, army.LiveCommanderInitiative01, commanderAggression01);
                    if (forceBalance.HasValue)
                    {
                        army.UpdateReinforcementOpportunity(forceBalance.Value);
                        EmitReinforcementOpportunityTelemetry(side, army.CurrentReinforcementOpportunity);
                    }
                }

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
            var synthesizedState = new CommandNodeOperationalState(
                nodeId: "leaf-" + instanceId,
                echelon: CommandEchelonKind.BrigadeLike,
                role: MapDirectChildRoleToNodeRole(leafAssignment.LeafRole),
                task: leafAssignment.LeafTask,
                taskState: CommandTaskState.Planning);

            // Reuse the same physical/eligibility/decision pipeline as the
            // primary unitsused iteration, but with the synthesized leaf state.
            // The TryApplyGroup signature requires resolving ledger state; for
            // nested brigades we don't have ledger state, so we replicate the
            // minimal posture flow inline.
            ApplyLeafBrigadePosture(battle, bunits, side, group, synthesizedState, leafAssignment, runtimePlayOrders);
        }

        private static void ApplyLeafBrigadePosture(
            AIBattle battle,
            BattleUnits bunits,
            int side,
            Regiment group,
            CommandNodeOperationalState state,
            TacticalLeafBrigadeMap.LeafAssignment leafAssignment,
            IReadOnlyDictionary<string, TacticalDivisionPlayOrder> runtimePlayOrders)
        {
            // Minimal posture write path for a leaf brigade that doesn't carry
            // its own ledger state. Reuses the same player-protected, routed,
            // recent-order, and active-move gates as the primary flow, but
            // simplifies the doctrine-order resolution (we synthesize the
            // doctrine order from the leaf assignment task).
            try
            {
                bool playerProtected = IsPlayerProtected(group);
                if (playerProtected) { EmitLeafCascadeTelemetry(side, group, leafAssignment, "player-protected", false); return; }
                if (SafeRouted(group)) { EmitLeafCascadeTelemetry(side, group, leafAssignment, "routed", false); return; }

                int instanceId = TacticalPatchIds.GameObjectInstanceId(group);
                if (HasRecentExecutorOrder(instanceId)) { EmitLeafCascadeTelemetry(side, group, leafAssignment, "recent-order", false); return; }
                if (group.regimentpaths > 0 || group.movementmode > 0)
                {
                    EmitLeafCascadeTelemetry(side, group, leafAssignment, "movement-in-progress", false);
                    return;
                }

                // Mirror the primary executor's pending-order gate: don't overwrite
                // a vanilla courier-in-flight or a queued order. Without this check
                // the cascade could clobber an order that's already on its way.
                if (HasPendingOrder(group, allowStaleQueuedBypass: false))
                {
                    EmitLeafCascadeTelemetry(side, group, leafAssignment, "order-pending", false);
                    return;
                }

                // W&L feud-action gate parity: if the group contains a player-
                // subordinate attached unit (dlcw_isundercommander), deny the
                // write. Mirrors the IsPlayerProtected walk used elsewhere in
                // this patch — the existing IsPlayerProtected check above only
                // catches the group itself, not its attached units.
                if (HasPlayerSubordinateAttached(group))
                {
                    EmitLeafCascadeTelemetry(side, group, leafAssignment, "wl-player-subordinate-attached", false);
                    return;
                }

                // Emit the cascade telemetry BEFORE attempting the write, so we
                // always see "what task was derived for this brigade" even if
                // the actual write is gated.
                EmitLeafCascadeTelemetry(side, group, leafAssignment, "considering", false);

                // For the actual movement write, we use vanilla SetWaypoint via
                // the existing flow. Build a minimal CommandDoctrineOrder so the
                // executor can pick a target.
                if (TryResolveLeafMovementTarget(group, leafAssignment, out Vector3 target))
                {
                    bool useOrderDelay = CommandWaypointWritePolicy.ShouldUseOrderDelayForExecutorWaypoint(state.Task, battleActive: true);
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
                    _lastExecutorOrderAt[instanceId] = Time.realtimeSinceStartup;
                    EmitLeafCascadeTelemetry(side, group, leafAssignment, "applied-" + leafAssignment.LeafTask.ToString(), true);
                }
                else
                {
                    EmitLeafCascadeTelemetry(side, group, leafAssignment, "no-target", false);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-leaf-cascade:posture-failed",
                    "ApplyLeafBrigadePosture failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static bool HasPlayerSubordinateAttached(Regiment group)
        {
            try
            {
                if (group == null) return false;
                if (group.dlcw_isundercommander) return true;
                var units = group.allattachedunits;
                if (units == null) return false;
                for (int i = 0; i < units.Length; i++)
                {
                    var unit = units[i];
                    if (unit == null) continue;
                    if (unit.dlcw_isundercommander) return true;
                }
                return false;
            }
            catch
            {
                // Fail closed: assume player-attached on error so we never write
                // to a unit we can't verify is safe.
                return true;
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

        private static bool TryResolveLeafMovementTarget(
            Regiment group,
            TacticalLeafBrigadeMap.LeafAssignment leafAssignment,
            out Vector3 target)
        {
            target = default(Vector3);
            try
            {
                if (group == null) return false;
                Vector3 own = SafePosition(group);
                if (IsDefaultVector(own)) return false;

                // Priority 1: parent regiment's last waypoint (SoW brigade-think
                // pattern of following parent TACOBJLoc). Most accurate when the
                // orchestrator has already issued a move to the parent division.
                try
                {
                    var parentTransform = ((Component)group).gameObject.transform != null
                        ? ((Component)group).gameObject.transform.parent
                        : null;
                    if (parentTransform != null)
                    {
                        var parentReg = parentTransform.GetComponent<Regiment>();
                        if (parentReg != null && !IsDefaultVector(parentReg.lastsetwaypointposition))
                        {
                            target = parentReg.lastsetwaypointposition;
                            return true;
                        }
                    }
                }
                catch { }

                // Priority 2: fall back to the GROUP's own last-set waypoint
                // (vanilla may have ordered the brigade before the orchestrator
                // came online). Keeps the brigade on its existing line of
                // advance rather than redirecting.
                try
                {
                    if (!IsDefaultVector(group.lastsetwaypointposition))
                    {
                        target = group.lastsetwaypointposition;
                        return true;
                    }
                }
                catch { }

                // Priority 3: nudge the brigade forward in its facing direction.
                // 150m (was 75m) is large enough that vanilla won't immediately
                // gate the order as "already there"; bounded so the brigade
                // doesn't run off the map if facing is malformed.
                try
                {
                    var tf = ((Component)group).gameObject.transform;
                    if (tf == null) return false;
                    Vector3 forward = tf.forward;
                    // Guard against zero-length forward (malformed transform).
                    float fwdLen = forward.x * forward.x + forward.z * forward.z;
                    if (fwdLen < 0.001f) return false;
                    target = new Vector3(own.x + forward.x * 150f, own.y, own.z + forward.z * 150f);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                target = default(Vector3);
                return false;
            }
        }


        /// <summary>
        /// Live commander initiative for the given side, read defensively from
        /// vanilla. Mirrors the proven BattleMacroStrategyPatch.CommanderAggression01
        /// pattern: bunits.GetCommandingOfficerFromSide(side) → GameVars.commander[id]
        /// .GetCommanderInitiative(), with NaN/range guards. Returns 0.5 mid-band
        /// on any failure so the doctrine degrades gracefully rather than locking
        /// to a misleading 0 or NaN.
        /// </summary>
        private static float ReadLiveCommanderInitiative01(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return 0.5f;
                if (side < 0 || side >= bunits.alliance.Length) return 0.5f;
                int commanderId = bunits.GetCommandingOfficerFromSide(side);
                if (GameVars.commander == null || commanderId < 0 || commanderId >= GameVars.commander.Count) return 0.5f;
                var commander = GameVars.commander[commanderId];
                if (commander == null) return 0.5f;
                float init = commander.GetCommanderInitiative();
                if (float.IsNaN(init) || float.IsInfinity(init)) return 0.5f;
                if (init < 0f) return 0f;
                if (init > 1f) return 1f;
                return init;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-reinforcement-opportunity:init-read-failed",
                    "ReadLiveCommanderInitiative01 threw: " + ex.GetType().Name + " " + ex.Message);
                return 0.5f;
            }
        }

        private static void EmitReinforcementOpportunityTelemetry(int side, ReinforcementOpportunityDecision decision)
        {
            try
            {
                string sig = "TacticalReinforcementOpportunity|side=" + side
                    + "|outcome=" + decision.Opportunity
                    + "|reason=" + decision.Reason
                    + "|ratio=" + decision.CurrentRatio.ToString("0.00")
                    + "|enemyParityHrs=" + (decision.ParityHoursForEnemy >= 999f ? "never" : decision.ParityHoursForEnemy.ToString("0.0"))
                    + "|ownParityHrs=" + (decision.ParityHoursForOwn >= 999f ? "never" : decision.ParityHoursForOwn.ToString("0.0"))
                    + "|threshold=" + decision.AttackThreshold.ToString("0.00")
                    + "|window=" + decision.AttackWindowHours.ToString("0.0");
                string key = "tactical-reinforcement-opportunity:" + side;
                if (!TacticalTelemetry.ShouldEmit(_lastTelemetryAt, key, sig, Time.realtimeSinceStartup, TelemetrySeconds, false))
                    return;

                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Decision,
                    "TacticalReinforcementOpportunity",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithDecision("TacticalReinforcementOpportunity", decision.Opportunity.ToString(), sig)
                        .WithField("outcome", decision.Opportunity.ToString())
                        .WithField("reason", decision.Reason)
                        .WithField("currentRatio", decision.CurrentRatio)
                        .WithField("enemyParityHours", decision.ParityHoursForEnemy >= 999f ? -1f : decision.ParityHoursForEnemy)
                        .WithField("ownParityHours", decision.ParityHoursForOwn >= 999f ? -1f : decision.ParityHoursForOwn)
                        .WithField("attackThreshold", decision.AttackThreshold)
                        .WithField("attackWindowHours", decision.AttackWindowHours));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-reinforcement-opportunity:telemetry-failed",
                    "EmitReinforcementOpportunityTelemetry threw: " + ex.GetType().Name + " " + ex.Message);
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
            try
            {
                if (group == null) return true;
                // Removed 2026-05-19: the prior blanket alliance check
                // `group.alliance == GameVars.playeralliance && !ai_vs_ai`
                // returned true for EVERY unit on the player's alliance,
                // not just the player's directly-commanded unit. Symptom:
                // in W&L play the AI could not issue orders to any brigade
                // on the player's side because the protection fired on
                // every group — the player's own brigade just sat at
                // vanilla "hold" with no orchestrator activity.
                //
                // W&L scope is the player's specific commanded unit +
                // ancestors/descendants of it (via transform chain) +
                // any DLC-taken-over unit. The three checks below capture
                // that scope correctly.
                if (!WlOwnershipSafe(group)) { EmitPlayerProtectedDiagnostic(group, "wl-ownership"); return true; }
                if (IsWlCurrentCommandOrChain(group)) { EmitPlayerProtectedDiagnostic(group, "wl-current-command"); return true; }
                if (SafeDlcTakenOver(group)) { EmitPlayerProtectedDiagnostic(group, "dlc-taken-over"); return true; }
                return false;
            }
            catch
            {
                return true;
            }
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

        private static bool WlOwnershipSafe(Regiment group)
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return true;
                if (GameVars.ai_vs_ai) return true;
                if (group == null) return false;
                if (group.dlcw_isundercommander) return false;
                if (group.allattachedunits == null) return true;

                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null && unit.dlcw_isundercommander) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWlCurrentCommandOrChain(Regiment group)
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return false;
                if (DLC_WL.dlc_chosencommander < 0) return false;
                if (GameVars.commander == null || DLC_WL.dlc_chosencommander >= GameVars.commander.Count) return true;

                Regiment current = GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
                if ((UnityEngine.Object)current == null) return false;
                if ((UnityEngine.Object)current == (UnityEngine.Object)group) return true;

                Transform currentTransform = current.transform;
                Transform groupTransform = group.transform;
                if (currentTransform == null || groupTransform == null) return true;

                return currentTransform.IsChildOf(groupTransform) || groupTransform.IsChildOf(currentTransform);
            }
            catch
            {
                return true;
            }
        }

        private static bool HasPendingOrder(Regiment group)
        {
            return HasPendingOrder(group, allowStaleQueuedBypass: false);
        }

        private static bool HasPendingOrder(Regiment group, bool allowStaleQueuedBypass)
        {
            if (PendingOrderOn(group, allowStaleQueuedBypass)) return true;
            try
            {
                if (group == null || group.allattachedunits == null) return false;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    if (PendingOrderOn(group.allattachedunits[i], allowStaleQueuedBypass)) return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static bool PendingOrderOn(Regiment unit, bool allowStaleQueuedBypass)
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
                    ActiveMove = HasActiveMoveMakingProgress(unit),
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
            return HasRecentExecutorOrder(instanceId, RecentOrderSeconds);
        }

        private static bool HasRecentExecutorOrder(int instanceId, float cooldownSeconds)
        {
            if (instanceId == 0) return true;
            if (!_lastExecutorOrderAt.TryGetValue(instanceId, out float last)) return false;
            return Time.realtimeSinceStartup - last < Math.Max(0f, cooldownSeconds);
        }

        private static bool HasRecentFacingPulse(int instanceId, float cooldownSeconds)
        {
            if (instanceId == 0) return true;
            if (!_lastFacingPulseAt.TryGetValue(instanceId, out float last)) return false;
            return Time.realtimeSinceStartup - last < Math.Max(0f, cooldownSeconds);
        }

        private static void MarkFacingPulse(Regiment unit)
        {
            int instanceId = SafeInstanceId(unit);
            if (instanceId == 0) return;
            _lastFacingPulseAt[instanceId] = Time.realtimeSinceStartup;
            _lastExecutorOrderAt[instanceId] = Time.realtimeSinceStartup;
        }

        private static bool HasRecentFireControlOrder(int instanceId)
        {
            if (instanceId == 0) return true;
            if (!_lastFireControlAt.TryGetValue(instanceId, out float last)) return false;
            return Time.realtimeSinceStartup - last < FireControlOrderCooldownSeconds;
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

        private static string ParentNodeKey(Regiment group, CommandNodeOperationalState state)
        {
            try
            {
                if (group != null)
                {
                    FieldInfo parentField = AccessTools.Field(typeof(Regiment), "parentregiment");
                    object parent = parentField != null ? parentField.GetValue(group) : null;
                    var parentGameObject = parent as GameObject;
                    if (parentGameObject != null) return "parent-go:" + parentGameObject.GetInstanceID();
                    var parentComponent = parent as Component;
                    if (parentComponent != null) return "parent-co:" + parentComponent.GetInstanceID();

                    Transform parentTransform = group.transform != null ? group.transform.parent : null;
                    if (parentTransform != null) return "parent-tr:" + parentTransform.GetInstanceID();
                }
            }
            catch { }

            return "role-parent:" + state.Echelon + ":" + state.Role;
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

        private static int StanceForTask(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                    return 3;
                case CommandTaskType.FixEnemy:
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.ReserveWait:
                case CommandTaskType.FallBackToLine:
                    return 2;
                default:
                    return -1;
            }
        }

        private static bool SafeRouted(Regiment group)
        {
            try { return group == null || group.isrouted || group.markedforrout; }
            catch { return true; }
        }

        private static bool SafeDlcTakenOver(Regiment group)
        {
            try { return DLC_WL.dlc_scenarioactive && group != null && DLC_WL.IsUnitTakenOver(group); }
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

        private static BattlefieldSetup SafeBattlefieldSetup()
        {
            try
            {
                GameObject controller = GameObject.Find("GameController");
                return controller != null ? controller.GetComponent<BattlefieldSetup>() : null;
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
                    .WithField("activeMove", HasActiveMoveMakingProgress(group)));
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
            _lastCourierAt.Clear();
            _lastOutboundOrderSignatureByGroup.Clear();
            _lastTelemetryAt.Clear();
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
