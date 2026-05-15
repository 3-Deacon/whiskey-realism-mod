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
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Slice B0 observer for vanilla AIBattle tactical methods. This patch reads
    // battle/group state after vanilla runs and emits bounded telemetry only.
    [HarmonyPatch]
    internal static class TacticalObserverPatch
    {
        private static readonly Dictionary<string, float> _lastEmittedAt = new Dictionary<string, float>();
        private static readonly Dictionary<string, string> _operationsTelemetrySignatures = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _operationsPendingTelemetrySignatures = new Dictionary<string, string>();
        private static readonly Dictionary<string, float> _operationsSummaryEmittedAt = new Dictionary<string, float>();
        private static readonly Dictionary<string, FieldInfo> _sideInfoFieldCache = new Dictionary<string, FieldInfo>();
        private const float OperationsDetailTelemetrySeconds = 30f;
        private const float OperationsSummaryTelemetrySeconds = 15f;
        private static int _chargeBeforeId;
        private static TacticalObserverSnapshot _chargeBefore = TacticalObserverSnapshot.Empty();
        private static TacticalObserverSnapshot _feudBefore = TacticalObserverSnapshot.Empty();

        // Per-session lifecycle detector. Static so it preserves state across observer ticks.
        // CheckGlobalAIStrategyPostfix fires once per AI side per tick (~2× per frame when both
        // sides are AI-controlled); the two-tick hysteresis in the detector handles this safely.
        private static readonly TacticalBattleLifecycleDetector _lifecycleDetector = new TacticalBattleLifecycleDetector();

        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _bunitsField;
        private static FieldInfo _unitsUsedField;
        private static FieldInfo _allGroupsAssignedField;
        private static FieldInfo _objectiveChainField;
        private static FieldInfo _orderedStanceField;
        private static FieldInfo _parentRegimentField;
        private static FieldInfo _allianceField;
        private static FieldInfo _sideField;
        private static bool _sideFieldResolved;
        private static FieldInfo _orderStateField;
        private static FieldInfo _chainCenterUnitField;
        private static FieldInfo _currentSetObjectiveField;
        private static FieldInfo _changeToMarchColumnActiveField;

        internal readonly struct CurrentOrderState
        {
            public CurrentOrderState(TacticalCurrentOrderSignature order, int session)
            {
                Order = order;
                Session = session;
            }

            public TacticalCurrentOrderSignature Order { get; }
            public int Session { get; }
        }

        internal readonly struct CourierQueueState
        {
            public CourierQueueState(int queueCount, int appendIndex)
            {
                QueueCount = queueCount;
                AppendIndex = appendIndex;
            }

            public int QueueCount { get; }
            public int AppendIndex { get; }
        }

        internal readonly struct WaypointState
        {
            public WaypointState(
                int pathCount,
                int queueCount,
                bool activeMoveOrder,
                float x,
                float z,
                float startX,
                float startZ,
                float targetX,
                float targetZ,
                int movementMode,
                int formation,
                int formationOrdered,
                int lastFormation,
                int groupFormation,
                bool marchColumnActive,
                bool distanceMarchColumn,
                int orderState,
                int lastTransmittedPath)
            {
                PathCount = pathCount;
                QueueCount = queueCount;
                ActiveMoveOrder = activeMoveOrder;
                X = x;
                Z = z;
                StartX = startX;
                StartZ = startZ;
                TargetX = targetX;
                TargetZ = targetZ;
                MovementMode = movementMode;
                Formation = formation;
                FormationOrdered = formationOrdered;
                LastFormation = lastFormation;
                GroupFormation = groupFormation;
                MarchColumnActive = marchColumnActive;
                DistanceMarchColumn = distanceMarchColumn;
                OrderState = orderState;
                LastTransmittedPath = lastTransmittedPath;
            }

            public int PathCount { get; }
            public int QueueCount { get; }
            public bool ActiveMoveOrder { get; }
            public float X { get; }
            public float Z { get; }
            public float StartX { get; }
            public float StartZ { get; }
            public float TargetX { get; }
            public float TargetZ { get; }
            public int MovementMode { get; }
            public int Formation { get; }
            public int FormationOrdered { get; }
            public int LastFormation { get; }
            public int GroupFormation { get; }
            public bool MarchColumnActive { get; }
            public bool DistanceMarchColumn { get; }
            public int OrderState { get; }
            public int LastTransmittedPath { get; }
        }

        internal readonly struct PathShapeSummary
        {
            public PathShapeSummary(
                bool pathCreated,
                int cornerCount,
                float directDistance,
                float pathLength,
                float pathRatio,
                float firstSegmentDelta,
                string navStatus,
                int pathStatus,
                float firstX,
                float firstZ,
                float finalX,
                float finalZ)
            {
                PathCreated = pathCreated;
                CornerCount = cornerCount;
                DirectDistance = directDistance;
                PathLength = pathLength;
                PathRatio = pathRatio;
                FirstSegmentDelta = firstSegmentDelta;
                NavStatus = TacticalCurrentOrderSignature.Safe(navStatus);
                PathStatus = pathStatus;
                FirstX = firstX;
                FirstZ = firstZ;
                FinalX = finalX;
                FinalZ = finalZ;
            }

            public bool PathCreated { get; }
            public int CornerCount { get; }
            public float DirectDistance { get; }
            public float PathLength { get; }
            public float PathRatio { get; }
            public float FirstSegmentDelta { get; }
            public string NavStatus { get; }
            public int PathStatus { get; }
            public float FirstX { get; }
            public float FirstZ { get; }
            public float FinalX { get; }
            public float FinalZ { get; }
        }

        internal readonly struct FormationChangeState
        {
            public FormationChangeState(
                int formation,
                int formationOrdered,
                int lastFormation,
                int groupFormation,
                bool marchColumnActive,
                bool distanceMarchColumn,
                int pathCount,
                int orderState,
                int lastTransmittedPath)
            {
                Formation = formation;
                FormationOrdered = formationOrdered;
                LastFormation = lastFormation;
                GroupFormation = groupFormation;
                MarchColumnActive = marchColumnActive;
                DistanceMarchColumn = distanceMarchColumn;
                PathCount = pathCount;
                OrderState = orderState;
                LastTransmittedPath = lastTransmittedPath;
            }

            public int Formation { get; }
            public int FormationOrdered { get; }
            public int LastFormation { get; }
            public int GroupFormation { get; }
            public bool MarchColumnActive { get; }
            public bool DistanceMarchColumn { get; }
            public int PathCount { get; }
            public int OrderState { get; }
            public int LastTransmittedPath { get; }
        }

        internal sealed class ObjectiveMoveState
        {
            public readonly List<ObjectiveUnitSnapshot> Units = new List<ObjectiveUnitSnapshot>();
            public int ExposedChainCount;
            public Regiment FirstCenter;
        }

        internal readonly struct ObjectiveUnitSnapshot
        {
            public ObjectiveUnitSnapshot(
                Regiment unit,
                bool center,
                int pathCount,
                float positionX,
                float positionZ,
                float waypointX,
                float waypointZ,
                int objectiveId)
            {
                Unit = unit;
                Center = center;
                PathCount = pathCount;
                PositionX = positionX;
                PositionZ = positionZ;
                WaypointX = waypointX;
                WaypointZ = waypointZ;
                ObjectiveId = objectiveId;
            }

            public Regiment Unit { get; }
            public bool Center { get; }
            public int PathCount { get; }
            public float PositionX { get; }
            public float PositionZ { get; }
            public float WaypointX { get; }
            public float WaypointZ { get; }
            public int ObjectiveId { get; }
        }

        [HarmonyPatch(typeof(AIBattle), "CheckGlobalAIStrategy")]
        [HarmonyPostfix]
        internal static void CheckGlobalAIStrategyPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Macro, null, null);
            Observe(__instance, TacticalObservedEvent.Odds, null, null);

            // Orchestrator lifecycle: count units in battle, detect start/end transitions,
            // then drive TacticalBattleCoordinator. Anchored here because
            // CheckGlobalAIStrategy is the macro-strategy entry — fires once per AI side
            // per tick, giving us a consistent per-tick observation cadence.
            try
            {
                if (Plugin.EnableTacticalBattleOrchestrator.Value)
                {
                    int unitsInBattle = CountUnitsInBattleAcrossSides();
                    var ev = _lifecycleDetector.Observe(unitsInBattle);
                    switch (ev)
                    {
                        case BattleLifecycleEvent.BattleStart:
                            ResetOperationsTelemetry();
                            TacticalBattleCoordinator.OnBattleStart(__instance);
                            break;
                        case BattleLifecycleEvent.BattleEnd:
                            TacticalBattleCoordinator.OnBattleEnd();
                            ResetOperationsTelemetry();
                            break;
                    }
                    // First Tick is intentionally colocated with OnBattleStart to give the coordinator
                    // a zero-latency cold start (observer-driven cadence; we don't want to wait for the
                    // next CheckGlobalAIStrategy invocation to fire the first tick telemetry).
                    if (TacticalBattleCoordinator.IsActive)
                    {
                        TacticalBattleCoordinator.Tick(__instance);
                        EmitOperationsMonitorTelemetry();
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] observer wiring skipped: " + e.GetType().Name + " " + e.Message);
            }
        }

        private static void ResetOperationsTelemetry()
        {
            _operationsTelemetrySignatures.Clear();
            _operationsPendingTelemetrySignatures.Clear();
            _operationsSummaryEmittedAt.Clear();
        }

        /// <summary>
        /// Returns a positive value when a battle scene is loaded, 0 otherwise. Used by
        /// the lifecycle detector as the "battle is running" signal.
        ///
        /// Vanilla nullifies `BattleUnits.completeunitlist` on battle end (decompile line
        /// 79219) and treats `completeunitlist == null` as "no battle" in many call sites
        /// (lines 11306, 36228, 36316, 65947, 105437, 210259, 210294). The presence of a
        /// non-empty list is therefore the canonical signal that a tactical battle scene
        /// is currently loaded — irrespective of `Regiment.inbattle` (which tracks
        /// campaign-side engagement state and does not necessarily flip true on
        /// battle-instance Regiments). Returns 0 on any exception so a vanilla rename
        /// fails gracefully.
        /// </summary>
        private static int CountUnitsInBattleAcrossSides()
        {
            try
            {
                var all = BattleUnits.completeunitlist;
                if (all == null || all.Count == 0) return 0;
                return all.Count;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] CountUnitsInBattle: " + e.GetType().Name + " " + e.Message);
                return 0;
            }
        }

        [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
        [HarmonyPostfix]
        internal static void AdjustGroupAIStancePostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Group, null, null);
            Observe(__instance, TacticalObservedEvent.Sector, null, null);
            Observe(__instance, TacticalObservedEvent.Order, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
        [HarmonyPrefix]
        internal static void MicroAICheckForChargesPrefix(AIBattle __instance, Regiment aigroup)
        {
            if (!Enabled()) return;
            try
            {
                int key = SafeInstanceId(aigroup);
                _chargeBeforeId = key;
                _chargeBefore = key != 0 ? SnapshotGroup(aigroup) : TacticalObserverSnapshot.Empty();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:charge-prefix", "Tactical charge observer Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForCharges")]
        [HarmonyPostfix]
        internal static void MicroAICheckForChargesPostfix(AIBattle __instance, Regiment aigroup)
        {
            TacticalObserverSnapshot before = null;
            try
            {
                int key = SafeInstanceId(aigroup);
                if (key != 0 && key == _chargeBeforeId) before = _chargeBefore;
            }
            catch
            {
                before = null;
            }

            Observe(__instance, TacticalObservedEvent.Charge, before, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckForFeudGroupActions")]
        [HarmonyPrefix]
        internal static void CheckForFeudGroupActionsPrefix(AIBattle __instance)
        {
            if (!Enabled()) return;
            try
            {
                _feudBefore = SnapshotBattle(__instance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:feud-prefix", "Tactical feud observer Prefix failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(AIBattle), "CheckForFeudGroupActions")]
        [HarmonyPostfix]
        internal static void CheckForFeudGroupActionsPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Feud, _feudBefore, null);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
        [HarmonyPostfix]
        internal static void CheckUseOfReservesPostfix(AIBattle __instance, Regiment aigroup)
        {
            Observe(__instance, TacticalObservedEvent.Reserve, null, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "LinkReservesToLineGroup")]
        [HarmonyPostfix]
        internal static void LinkReservesToLineGroupPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Reserve, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "AssignReserves")]
        [HarmonyPostfix]
        internal static void AssignReservesPostfix(AIBattle __instance)
        {
            Observe(__instance, TacticalObservedEvent.Reserve, null, null);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckAIBombardment")]
        [HarmonyPostfix]
        internal static void CheckAIBombardmentPostfix(AIBattle __instance, Regiment aigroup)
        {
            Observe(__instance, TacticalObservedEvent.Artillery, null, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckLineFallbacks")]
        [HarmonyPostfix]
        internal static void CheckLineFallbacksPostfix(AIBattle __instance, Regiment aigroup)
        {
            Observe(__instance, TacticalObservedEvent.Fallback, null, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "MicroAICheckForRetreats")]
        [HarmonyPostfix]
        internal static void MicroAICheckForRetreatsPostfix(AIBattle __instance, Regiment aigroup)
        {
            Observe(__instance, TacticalObservedEvent.Fallback, null, aigroup);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckCurrentOrderUpdate")]
        [HarmonyPrefix]
        internal static void CheckCurrentOrderUpdatePrefix(out CurrentOrderState __state)
        {
            __state = new CurrentOrderState(ReadGivenOrderSignature(), ReadGivenOrdersSession());
        }

        [HarmonyPatch(typeof(AIBattle), "CheckCurrentOrderUpdate")]
        [HarmonyPostfix]
        internal static void CheckCurrentOrderUpdatePostfix(
            Regiment unit,
            int type,
            Vector3 position,
            string destinationname,
            float rotation,
            bool calledfromcampaign,
            CurrentOrderState __state)
        {
            if (!BugTelemetryEnabled()) return;

            try
            {
                var next = ReadGivenOrderSignature();
                if (next.IsEmpty)
                    next = new TacticalCurrentOrderSignature(SafeInstanceId(unit), type, position.x, position.z, rotation, destinationname);

                var decision = TacticalBattlefieldBugDiagnostics.ClassifyCurrentOrderReplacement(
                    calledfromcampaign,
                    __state.Order,
                    next,
                    nearDistance: 110f,
                    nearRotationDegrees: 35f);
                int sessionAfter = ReadGivenOrdersSession();
                bool sessionChanged = __state.Session != sessionAfter;

                if (!decision.IsRisk && !calledfromcampaign && !sessionChanged) return;

                EmitDirect(
                    "TacticalCurrentOrder",
                    "current-order|" + SafeInstanceId(unit) + "|" + type + "|" + __state.Session + "|" + sessionAfter + "|" + decision.Reason,
                    "[TacticalCurrentOrder] calledFromCampaign=" + calledfromcampaign +
                    " unit=" + SafeUnitName(unit) +
                    " oldType=" + __state.Order.Type +
                    " newType=" + next.Type +
                    " duplicateRisk=" + decision.IsRisk +
                    " reason=" + decision.Reason +
                    " sessionBefore=" + __state.Session +
                    " sessionAfter=" + sessionAfter);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-current-order", "Tactical current-order observer failed: " + ex.Message);
            }
        }

        // B2 command/order friction stays read-only: these Postfixes interpret vanilla queue/courier state.
        // They must not call SetWaypoint, AddToOrderQueue, SetOrderStatus, or mutate Regiment order fields.
        [HarmonyPatch(typeof(Regiment), "AddToOrderQueue")]
        [HarmonyPostfix]
        internal static void AddToOrderQueuePostfix(
            Regiment __instance,
            GameObject advisedunit,
            bool queueprocessingtime,
            int ordertype,
            float timetomove,
            float manualfinalrotation,
            bool modifylastwaypoint,
            bool clearpaths,
            bool overridebugle,
            bool _usecover,
            bool _newpath)
        {
            ObserveQueuedOrder(__instance, advisedunit, queueprocessingtime, ordertype, timetomove, modifylastwaypoint, _newpath);
        }

        [HarmonyPatch(typeof(Regiment), "AddOrderCourierline")]
        [HarmonyPrefix]
        internal static void AddOrderCourierlinePrefix(Regiment __instance, bool secondarycourier, out CourierQueueState __state)
        {
            int count = __instance != null && __instance.orderqueue != null ? __instance.orderqueue.Count : 0;
            __state = new CourierQueueState(count, count - 1);
        }

        [HarmonyPatch(typeof(Regiment), "AddOrderCourierline")]
        [HarmonyPostfix]
        internal static void AddOrderCourierlinePostfix(
            Regiment __instance,
            Regiment sourceunit,
            Regiment _targetunit,
            bool overridebugle,
            bool secondarycourier,
            CourierQueueState __state)
        {
            ObserveCourierLine(__instance, sourceunit, _targetunit, secondarycourier);
            ObserveCourierQueue(__instance, secondarycourier, __state);
        }

        [HarmonyPatch(typeof(BattleUnits), "SetWaypoint", new[]
        {
            typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool), typeof(float),
            typeof(bool), typeof(bool), typeof(float), typeof(int), typeof(bool),
            typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
        })]
        [HarmonyPrefix]
        internal static void SetWaypointPrefix(Regiment reg, Vector3 targetpos, out WaypointState __state)
        {
            __state = new WaypointState(
                SafeRegimentPaths(reg),
                SafeOrderQueueCount(reg),
                HasActiveMoveOrder(reg),
                SafeLastWaypointX(reg),
                SafeLastWaypointZ(reg),
                SafePositionX(reg),
                SafePositionZ(reg),
                targetpos.x,
                targetpos.z,
                SafeMovementMode(reg),
                SafeFormation(reg),
                SafeFormationOrdered(reg),
                SafeLastFormation(reg),
                SafeGroupFormation(reg),
                SafeMarchColumnActive(reg),
                SafeDistanceMarchColumn(reg),
                SafeOrderState(reg),
                SafePathId(reg, ignoreOrderDelay: false));
        }

        [HarmonyPatch(typeof(BattleUnits), "SetWaypoint", new[]
        {
            typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool), typeof(float),
            typeof(bool), typeof(bool), typeof(float), typeof(int), typeof(bool),
            typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
        })]
        [HarmonyPostfix]
        internal static void SetWaypointPostfix(
            Regiment reg,
            bool newpath,
            bool modifylastwaypoint,
            bool useorderdelay,
            int direction,
            bool showmovementoptions,
            WaypointState __state)
        {
            ObserveWaypointDrift(reg, useorderdelay, __state);
            ObservePathShape(reg, newpath, modifylastwaypoint, useorderdelay, direction, showmovementoptions, __state);
        }

        [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
        [HarmonyPrefix]
        internal static void CheckUseOfReservesPrefix(Regiment aigroup, out WaypointState __state)
        {
            __state = new WaypointState(
                CountGroupPaths(aigroup),
                SafeOrderQueueCount(aigroup),
                HasActiveMoveOrder(aigroup),
                SafeLastWaypointX(aigroup),
                SafeLastWaypointZ(aigroup),
                SafePositionX(aigroup),
                SafePositionZ(aigroup),
                0f,
                0f,
                SafeMovementMode(aigroup),
                SafeFormation(aigroup),
                SafeFormationOrdered(aigroup),
                SafeLastFormation(aigroup),
                SafeGroupFormation(aigroup),
                SafeMarchColumnActive(aigroup),
                SafeDistanceMarchColumn(aigroup),
                SafeOrderState(aigroup),
                SafePathId(aigroup, ignoreOrderDelay: false));
        }

        [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
        [HarmonyPostfix]
        internal static void CheckUseOfReservesBugPostfix(Regiment aigroup, WaypointState __state)
        {
            ObserveReserveMove(aigroup, __state);
        }

        [HarmonyPatch(typeof(Regiment), "ChangeRegimentFormation")]
        [HarmonyPrefix]
        internal static void ChangeRegimentFormationPrefix(Regiment __instance, out FormationChangeState __state)
        {
            __state = SnapshotFormationChange(__instance);
        }

        [HarmonyPatch(typeof(Regiment), "ChangeRegimentFormation")]
        [HarmonyPostfix]
        internal static void ChangeRegimentFormationPostfix(
            Regiment __instance,
            GameVars.FormationParam fparam,
            FormationChangeState __state)
        {
            ObserveFormationChange(__instance, fparam, __state);
        }

        [HarmonyPatch(typeof(AIBattle), "UpdateMovingTargets")]
        [HarmonyPrefix]
        internal static void UpdateMovingTargetsPrefix(AIBattle __instance, out ObjectiveMoveState __state)
        {
            __state = CaptureObjectiveMoveState(__instance);
        }

        [HarmonyPatch(typeof(AIBattle), "UpdateMovingTargets")]
        [HarmonyPostfix]
        internal static void UpdateMovingTargetsPostfix(AIBattle __instance, ObjectiveMoveState __state)
        {
            ObserveObjectiveChainMove(__instance);
            ObserveObjectiveChainMutation(__state);
        }

        private static void Observe(AIBattle battle, TacticalObservedEvent eventType, TacticalObserverSnapshot before, Regiment group)
        {
            if (!Enabled()) return;

            try
            {
                OnceLog.Info("tactical-observer", "TacticalObserverPatch wired");

                var context = BuildContext(battle, group);
                EmitDecisionMatrix(eventType, battle, group, context);
                string signature = TacticalTelemetry.Signature(eventType, context);
                bool verbose = Plugin.Instance != null && Plugin.Instance.TacticalObserverVerboseLogging.Value;
                float minSeconds = Plugin.Instance != null
                    ? Mathf.Max(1f, Plugin.Instance.TacticalObserverMinSecondsBetweenSummaries.Value)
                    : 30f;
                float now = Time.realtimeSinceStartup;
                string key = eventType.ToString();

                if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, key, signature, now, minSeconds, verbose))
                    return;

                string message = TacticalTelemetry.Summary(eventType, context);
                if (before != null)
                {
                    var after = group != null ? SnapshotGroup(group) : SnapshotBattle(battle);
                    message += " delta=" + TacticalTelemetry.Delta(before, after);
                }

                Plugin.Log.LogInfo(message);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:" + eventType, "Tactical observer " + eventType + " failed: " + ex.Message);
            }
        }

        private static void EmitOperationsMonitorTelemetry()
        {
            if (!DecisionMatrixEnabled()) return;

            try
            {
                EmitOperationsMonitorTelemetryForSide(0, TacticalBattleCoordinator.GetSideOrchestrator(0));
                EmitOperationsMonitorTelemetryForSide(1, TacticalBattleCoordinator.GetSideOrchestrator(1));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-ops-monitor", "Tactical operations monitor telemetry failed: " + ex.Message);
            }
        }

        private static void EmitOperationsMonitorTelemetryForSide(int side, TacticalBattleOrchestrator orchestrator)
        {
            if (orchestrator == null || orchestrator.Army == null) return;

            var army = orchestrator.Army;
            if (!TacticalCommanderModePolicy.EmitsLedgerTelemetry(army.CommanderMode)) return;

            var commandOperations = army.CurrentCommandOperations ?? Array.Empty<CommandNodeOperationalState>();
            var operation = army.CurrentOperation;
            var strategic = army.CurrentStrategicBattleIntent;
            float now = Time.realtimeSinceStartup;

            string ledgerSignature = TacticalOperationsTelemetry.OpsLedgerSignature(
                side,
                army.CommanderMode,
                operation,
                strategic,
                commandOperations.Count);
            if (TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(
                _operationsTelemetrySignatures,
                _operationsPendingTelemetrySignatures,
                _operationsSummaryEmittedAt,
                "ops-ledger:" + side,
                ledgerSignature,
                now,
                OperationsDetailTelemetrySeconds,
                verbose: false))
            {
                Plugin.Log.LogInfo(TacticalOperationsTelemetry.OpsLedger(
                    side,
                    army.CommanderMode,
                    operation,
                    strategic,
                    commandOperations.Count));
            }

            int validIdle = 0;
            int illegalIdle = 0;
            int recoveringStuck = 0;
            int activeAttacks = 0;
            int reservesWaiting = 0;

            for (int i = 0; i < commandOperations.Count; i++)
            {
                var state = commandOperations[i];
                string assignmentSignature = TacticalOperationsTelemetry.CommandAssignmentSignature(
                    side,
                    state,
                    operation);
                if (TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(
                    _operationsTelemetrySignatures,
                    _operationsPendingTelemetrySignatures,
                    _operationsSummaryEmittedAt,
                    "command-assignment:" + side + ":" + state.NodeId,
                    assignmentSignature,
                    now,
                    OperationsDetailTelemetrySeconds,
                    verbose: false))
                {
                    Plugin.Log.LogInfo(TacticalOperationsTelemetry.CommandAssignment(side, state, operation));
                }

                Regiment group = FindCommandNodeGroup(state.NodeId);
                var physical = BuildCommandPhysicalState(group);
                var idle = TacticalCommandMonitor.ClassifyIdle(state, physical);
                var decision = CommandPostureExecutor.Decide(
                    state,
                    physical,
                    new WriteEligibilitySnapshot(
                        modeAllowsWrites: TacticalCommanderModePolicy.AllowsWrites(army.CommanderMode),
                        playerProtected: physical.PlayerProtected,
                        routed: physical.Routed,
                        orderPending: group != null && (SafeOrderQueueCount(group) > 0 || SafeOrderState(group) > 0),
                        recentOrder: false,
                        alreadyDoingCorrectTask: false,
                        atAssignedLocation: idle == TacticalIdleClassification.ValidIdle,
                        missingLedgerAssignment: false,
                        closeEngaged: false));

                CountPosture(state, physical, idle, ref validIdle, ref illegalIdle, ref recoveringStuck, ref activeAttacks, ref reservesWaiting);

                if (decision.Action != PostureExecutionAction.NoWrite) continue;

                string postureSignature = TacticalOperationsTelemetry.CommandPostureSignature(side, state, decision, idle);
                if (!TacticalOperationsTelemetry.ShouldEmitChangedAfterInterval(
                    _operationsTelemetrySignatures,
                    _operationsPendingTelemetrySignatures,
                    _operationsSummaryEmittedAt,
                    "TacticalCommandPosture:" + side + ":" + TacticalOperationsTelemetry.SafeToken(state.NodeId),
                    postureSignature,
                    now,
                    OperationsDetailTelemetrySeconds,
                    verbose: false))
                {
                    continue;
                }

                Plugin.Log.LogInfo(TacticalOperationsTelemetry.CommandPosture(side, state, decision, idle));
            }

            if (TacticalOperationsTelemetry.ShouldEmitInterval(
                _operationsSummaryEmittedAt,
                "posture-summary:" + side,
                now,
                OperationsSummaryTelemetrySeconds,
                verbose: false))
            {
                Plugin.Log.LogInfo(TacticalOperationsTelemetry.PostureSummary(
                    side,
                    validIdle,
                    illegalIdle,
                    recoveringStuck,
                    activeAttacks,
                    reservesWaiting));
            }
        }

        private static void CountPosture(
            CommandNodeOperationalState state,
            CommandPhysicalState physical,
            TacticalIdleClassification idle,
            ref int validIdle,
            ref int illegalIdle,
            ref int recoveringStuck,
            ref int activeAttacks,
            ref int reservesWaiting)
        {
            if (idle == TacticalIdleClassification.ValidIdle) validIdle++;
            if (idle == TacticalIdleClassification.IllegalIdle) illegalIdle++;
            if ((physical.PathInterrupted && physical.Paths <= 0 && !physical.ActiveMove) ||
                state.Task == CommandTaskType.RecoverStuckOrder)
            {
                recoveringStuck++;
            }
            if (state.Task == CommandTaskType.AttackObjective || state.Task == CommandTaskType.SupportAttack)
            {
                activeAttacks++;
            }
            if (state.Task == CommandTaskType.ReserveWait)
            {
                reservesWaiting++;
            }
        }

        private static CommandPhysicalState BuildCommandPhysicalState(Regiment group)
        {
            return new CommandPhysicalState(
                routed: SafeRouted(group),
                playerProtected: group != null && group.dlcw_isundercommander,
                pathInterrupted: group != null && group.pathinterrupted,
                paths: SafeRegimentPaths(group),
                activeMove: HasActiveMoveOrder(group),
                formation: SafeFormation(group));
        }

        private static Regiment FindCommandNodeGroup(string nodeId)
        {
            int instanceId = ParseCommandNodeInstanceId(nodeId);
            if (instanceId == 0) return null;

            try
            {
                var all = BattleUnits.completeunitlist;
                if (all == null) return null;
                for (int i = 0; i < all.Count; i++)
                {
                    var reg = all[i] as Regiment;
                    if (reg == null) continue;
                    if (SafeInstanceId(reg) == instanceId || TacticalPatchIds.GameObjectInstanceId(reg) == instanceId)
                        return reg;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static int ParseCommandNodeInstanceId(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return 0;
            const string prefix = "node-";
            if (nodeId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return int.TryParse(nodeId.Substring(prefix.Length), out int id) ? id : 0;
            }

            return TacticalBattleCoordinator.ParseInstanceIdFromChildId(nodeId);
        }

        private static void ObserveQueuedOrder(
            Regiment issuer,
            GameObject advisedunit,
            bool queueProcessingTime,
            int orderType,
            float timeToMove,
            bool modifyLastWaypoint,
            bool newPath)
        {
            if (!Enabled()) return;

            try
            {
                var target = SafeRegiment(advisedunit);
                var queued = FindLatestQueuedOrder(issuer, advisedunit, orderType);
                if (queued == null) return;

                bool sourceUnderCommander = issuer != null && issuer.dlcw_isundercommander;
                bool targetUnderCommander = target != null && target.dlcw_isundercommander;
                string relation = OrderRelation(sourceUnderCommander, targetUnderCommander);
                float delay = queued.processingtime - GameVars.currenttimefromstart;
                int queueCount = issuer != null && issuer.orderqueue != null ? issuer.orderqueue.Count : -1;
                string signature = "queued|" + SafeInstanceId(issuer) + "|" + SafeInstanceId(target) + "|" +
                    orderType + "|" + queueCount + "|" + BucketSeconds(delay) + "|" + relation;

                EmitDirect(
                    "PlayerOrderQueued",
                    signature,
                    "[TacticalPlayerOrder] event=queued relation=" + relation +
                    " source=" + SafeUnitName(issuer) +
                    " sourceUnderCommander=" + sourceUnderCommander +
                    " target=" + SafeUnitName(target) +
                    " targetUnderCommander=" + targetUnderCommander +
                    " orderType=" + OrderTypeName(orderType) +
                    " queueCount=" + queueCount +
                    " delayHrs=" + FormatHours(delay) +
                    " queueProcessing=" + queueProcessingTime +
                    " newPath=" + newPath +
                    " modifyLast=" + modifyLastWaypoint +
                    " timedMove=" + FormatHours(timeToMove) +
                    " dlcWl=" + SafeDlcWlActive());

                var friction = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
                    orderDelayEnabled: SafeUseOrderDelays(),
                    queueProcessing: queueProcessingTime,
                    queueDelayHours: delay,
                    delivery: TacticalOrderDelivery.Unknown,
                    deliveryProcessHours: 0f,
                    courierMissing: false,
                    orderState: SafeOrderState(target),
                    intendedPathId: SafePathId(target, true),
                    transmittedPathId: SafePathId(target, false),
                    contactChangedMaterially: false,
                    commanderInitiative01: 0.50f));
                var command = TacticalCommandLedger.Summarize(
                    BuildCommanderProfile(issuer),
                    BuildCommanderProfile(target),
                    friction);

                EmitDirect(
                    "TacticalCommandQueued",
                    "command-queued|" + SafeInstanceId(issuer) + "|" + SafeInstanceId(target) + "|" + command.Signature(),
                    "[TacticalCommand] event=queued relation=" + relation +
                    " source=" + SafeUnitName(issuer) +
                    " target=" + SafeUnitName(target) +
                    " summary=" + command.Signature() +
                    " reason=" + command.Reason +
                    " dlcWl=" + SafeDlcWlActive());
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:player-order-queued", "Tactical player-order queue observer failed: " + ex.Message);
            }
        }

        private static void ObserveCourierLine(Regiment owner, Regiment sourceunit, Regiment targetunit, bool secondaryCourier)
        {
            if (!Enabled()) return;

            try
            {
                var line = FindLatestCourierLine(owner);
                var lineSource = line != null ? SafeRegiment(line.sourceunit) : sourceunit;
                var lineTarget = line != null ? SafeRegiment(line.targetunit) : targetunit;
                bool sourceUnderCommander = lineSource != null && lineSource.dlcw_isundercommander;
                bool targetUnderCommander = lineTarget != null && lineTarget.dlcw_isundercommander;
                string relation = OrderRelation(sourceUnderCommander, targetUnderCommander);
                string delivery = line == null ? "unknown" : (line.type == 0 ? "bugle" : "courier");
                TacticalOrderDelivery deliveryKind = DeliveryKind(delivery);
                float processTime = line != null ? line.processtime : 0f;
                string signature = "courier|" + SafeInstanceId(lineSource) + "|" + SafeInstanceId(lineTarget) + "|" +
                    delivery + "|" + BucketSeconds(processTime) + "|" + relation;

                EmitDirect(
                    "PlayerOrderCourier",
                    signature,
                    "[TacticalPlayerOrder] event=delivery relation=" + relation +
                    " source=" + SafeUnitName(lineSource) +
                    " sourceUnderCommander=" + sourceUnderCommander +
                    " target=" + SafeUnitName(lineTarget) +
                    " targetUnderCommander=" + targetUnderCommander +
                    " delivery=" + delivery +
                    " processHrs=" + FormatHours(processTime) +
                    " secondary=" + secondaryCourier +
                    " dlcWl=" + SafeDlcWlActive());

                bool courierMissing = line != null &&
                    deliveryKind == TacticalOrderDelivery.Courier &&
                    line.lineactive &&
                    line.courierref == null;
                var friction = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
                    orderDelayEnabled: SafeUseOrderDelays(),
                    queueProcessing: false,
                    queueDelayHours: 0f,
                    delivery: deliveryKind,
                    deliveryProcessHours: processTime,
                    courierMissing: courierMissing,
                    orderState: SafeOrderState(lineTarget),
                    intendedPathId: SafePathId(lineTarget, true),
                    transmittedPathId: SafePathId(lineTarget, false),
                    contactChangedMaterially: false,
                    commanderInitiative01: 0.50f));
                var command = TacticalCommandLedger.Summarize(
                    BuildCommanderProfile(lineSource),
                    BuildCommanderProfile(lineTarget),
                    friction);

                EmitDirect(
                    "TacticalOrderDelivery",
                    "order-delivery|" + SafeInstanceId(lineSource) + "|" + SafeInstanceId(lineTarget) + "|" +
                    delivery + "|" + command.Signature(),
                    "[TacticalOrder] event=delivery relation=" + relation +
                    " source=" + SafeUnitName(lineSource) +
                    " target=" + SafeUnitName(lineTarget) +
                    " delivery=" + delivery +
                    " friction=" + friction.State +
                    " delivered=" + friction.IsDelivered +
                    " delayed=" + friction.IsDelayed +
                    " pathLag=" + friction.TransmittedPathDiffers +
                    " pressure=" + FormatHours(friction.DelayPressure) +
                    " command=" + command.Signature() +
                    " dlcWl=" + SafeDlcWlActive());
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:player-order-courier", "Tactical player-order courier observer failed: " + ex.Message);
            }
        }

        private static void ObserveCourierQueue(Regiment owner, bool secondaryCourier, CourierQueueState state)
        {
            if (!BugTelemetryEnabled()) return;

            try
            {
                var decision = TacticalBattlefieldBugDiagnostics.ClassifyCourierQueueIndex(
                    secondaryCourier,
                    state.QueueCount,
                    activeQueueIndex: -1,
                    appendQueueIndex: state.AppendIndex);
                if (!secondaryCourier && !decision.IsRisk) return;

                EmitDirect(
                    "TacticalCourierQueue",
                    "courier-queue|" + SafeInstanceId(owner) + "|" + state.QueueCount + "|" + state.AppendIndex + "|" + decision.Reason,
                    "[TacticalCourierQueue] owner=" + SafeUnitName(owner) +
                    " secondary=" + secondaryCourier +
                    " queueCount=" + state.QueueCount +
                    " appendIndex=" + state.AppendIndex +
                    " risk=" + decision.IsRisk +
                    " reason=" + decision.Reason);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-courier-queue", "Tactical courier queue observer failed: " + ex.Message);
            }
        }

        private static void ObserveWaypointDrift(Regiment unit, bool useOrderDelay, WaypointState state)
        {
            if (!BugTelemetryEnabled()) return;

            try
            {
                int afterPaths = SafeRegimentPaths(unit);
                int afterQueues = SafeOrderQueueCount(unit);
                bool queueAdded = afterQueues > state.QueueCount;
                var decision = TacticalBattlefieldBugDiagnostics.ClassifyDelayedWaypointDrift(
                    orderDelayEnabled: useOrderDelay,
                    activeMoveOrder: state.ActiveMoveOrder,
                    queueAdded: queueAdded,
                    pathCountBefore: state.PathCount,
                    pathCountAfter: afterPaths,
                    xBefore: state.X,
                    zBefore: state.Z,
                    xAfter: SafeLastWaypointX(unit),
                    zAfter: SafeLastWaypointZ(unit));
                if (!decision.IsRisk) return;

                EmitDirect(
                    "TacticalWaypointDrift",
                    "waypoint-drift|" + SafeInstanceId(unit) + "|" + state.PathCount + "|" + afterPaths + "|" + decision.Reason,
                    "[TacticalWaypointDrift] unit=" + SafeUnitName(unit) +
                    " useDelay=" + useOrderDelay +
                    " activeMoveOrder=" + state.ActiveMoveOrder +
                    " queueBefore=" + state.QueueCount +
                    " queueAfter=" + afterQueues +
                    " pathBefore=" + state.PathCount +
                    " pathAfter=" + afterPaths +
                    " risk=" + decision.IsRisk +
                    " reason=" + decision.Reason);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-waypoint-drift", "Tactical waypoint drift observer failed: " + ex.Message);
            }
        }

        private static void ObservePathShape(
            Regiment unit,
            bool newPath,
            bool modifyLastWaypoint,
            bool useOrderDelay,
            int direction,
            bool showMovementOptions,
            WaypointState state)
        {
            if (!BugTelemetryEnabled() || !showMovementOptions) return;

            try
            {
                int afterPaths = SafeRegimentPaths(unit);
                var shape = BuildPathShape(unit, state, afterPaths, newPath, modifyLastWaypoint);
                var decision = TacticalBattlefieldBugDiagnostics.ClassifyPathShape(
                    showMovementOptions: showMovementOptions,
                    pathCreated: shape.PathCreated,
                    cornerCount: shape.CornerCount,
                    directDistance: shape.DirectDistance,
                    pathLength: shape.PathLength,
                    firstSegmentDeltaDegrees: shape.FirstSegmentDelta,
                    navStatus: shape.NavStatus,
                    pathStatus: shape.PathStatus,
                    orderDelayEnabled: useOrderDelay);

                if (!decision.IsRisk && !shape.PathCreated) return;

                EmitDirect(
                    "TacticalPathShape",
                    "path-shape|" + SafeInstanceId(unit) + "|" + state.PathCount + "|" + afterPaths + "|" +
                    BucketForObserver(state.TargetX) + "|" + BucketForObserver(state.TargetZ) + "|" + decision.Reason,
                    "[TacticalPathShape] unit=" + SafeUnitName(unit) +
                    " paths=" + state.PathCount + "->" + afterPaths +
                    " start=" + PointSignature(state.StartX, state.StartZ) +
                    " target=" + PointSignature(state.TargetX, state.TargetZ) +
                    " first=" + PointSignature(shape.FirstX, shape.FirstZ) +
                    " final=" + PointSignature(shape.FinalX, shape.FinalZ) +
                    " corners=" + shape.CornerCount +
                    " direct=" + BucketForObserver(shape.DirectDistance) +
                    " length=" + BucketForObserver(shape.PathLength) +
                    " ratio=" + BucketForObserver(shape.PathRatio) +
                    " firstDelta=" + BucketForObserver(shape.FirstSegmentDelta) +
                    " navStatus=" + shape.NavStatus +
                    " pathStatus=" + shape.PathStatus +
                    " moveMode=" + state.MovementMode + "->" + SafeMovementMode(unit) +
                    " formation=" + FormatFormation(state.Formation) + "->" + FormatFormation(SafeFormation(unit)) +
                    " orderedFormation=" + FormatFormation(state.FormationOrdered) + "->" + FormatFormation(SafeFormationOrdered(unit)) +
                    " lastFormation=" + FormatFormation(state.LastFormation) + "->" + FormatFormation(SafeLastFormation(unit)) +
                    " groupFormation=" + FormatFormation(state.GroupFormation) + "->" + FormatFormation(SafeGroupFormation(unit)) +
                    " marchColumnActive=" + state.MarchColumnActive + "->" + SafeMarchColumnActive(unit) +
                    " distanceMarchColumn=" + state.DistanceMarchColumn + "->" + SafeDistanceMarchColumn(unit) +
                    " orderState=" + state.OrderState + "->" + SafeOrderState(unit) +
                    " transmittedPath=" + state.LastTransmittedPath + "->" + SafePathId(unit, ignoreOrderDelay: false) +
                    " autoColumnEligible=" + AutoColumnEligible(state) +
                    " newPath=" + newPath +
                    " modifyLast=" + modifyLastWaypoint +
                    " useDelay=" + useOrderDelay +
                    " direction=" + direction +
                    " risk=" + decision.IsRisk +
                    " reason=" + decision.Reason);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-path-shape", "Tactical path-shape observer failed: " + ex.Message);
            }
        }

        private static void ObserveFormationChange(Regiment unit, GameVars.FormationParam fparam, FormationChangeState state)
        {
            if (!BugTelemetryEnabled()) return;

            try
            {
                int requested = fparam != null ? fparam.newformation : -1;
                bool relevant =
                    requested == 3 ||
                    state.Formation == 3 ||
                    state.LastFormation == 3 ||
                    SafeFormation(unit) == 3 ||
                    SafeLastFormation(unit) == 3 ||
                    state.DistanceMarchColumn ||
                    SafeDistanceMarchColumn(unit);
                if (!relevant) return;

                EmitDirect(
                    "TacticalFormationChange",
                    "formation-change|" + SafeInstanceId(unit) + "|" + state.Formation + "|" + SafeFormation(unit) + "|" +
                    requested + "|" + SafeRegimentPaths(unit) + "|" + SafeDistanceMarchColumn(unit),
                    "[TacticalFormationChange] unit=" + SafeUnitName(unit) +
                    " requested=" + FormatFormation(requested) +
                    " manualSet=" + (fparam != null && fparam.manualset) +
                    " setOrdered=" + (fparam != null && fparam.setalsoformationorderedvariable) +
                    " formation=" + FormatFormation(state.Formation) + "->" + FormatFormation(SafeFormation(unit)) +
                    " orderedFormation=" + FormatFormation(state.FormationOrdered) + "->" + FormatFormation(SafeFormationOrdered(unit)) +
                    " lastFormation=" + FormatFormation(state.LastFormation) + "->" + FormatFormation(SafeLastFormation(unit)) +
                    " groupFormation=" + FormatFormation(state.GroupFormation) + "->" + FormatFormation(SafeGroupFormation(unit)) +
                    " marchColumnActive=" + state.MarchColumnActive + "->" + SafeMarchColumnActive(unit) +
                    " distanceMarchColumn=" + state.DistanceMarchColumn + "->" + SafeDistanceMarchColumn(unit) +
                    " paths=" + state.PathCount + "->" + SafeRegimentPaths(unit) +
                    " orderState=" + state.OrderState + "->" + SafeOrderState(unit) +
                    " transmittedPath=" + state.LastTransmittedPath + "->" + SafePathId(unit, ignoreOrderDelay: false));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-formation-change", "Tactical formation-change observer failed: " + ex.Message);
            }
        }

        private static void ObserveReserveMove(Regiment group, WaypointState state)
        {
            if (!BugTelemetryEnabled()) return;

            try
            {
                int afterPaths = CountGroupPaths(group);
                var decision = TacticalBattlefieldBugDiagnostics.ClassifyReserveDirectPathBypass(
                    reserveSupportMove: afterPaths > state.PathCount,
                    orderDelayEnabled: SafeUseOrderDelays(),
                    directPathIssued: afterPaths > state.PathCount,
                    queuedOrderIssued: SafeOrderQueueCount(group) > state.QueueCount,
                    reserveCandidateCount: CountAttachedUnits(group));
                if (!decision.IsRisk && afterPaths <= state.PathCount) return;

                EmitDirect(
                    "TacticalReserveMove",
                    "reserve-move|" + SafeInstanceId(group) + "|" + state.PathCount + "|" + afterPaths + "|" + decision.Reason,
                    "[TacticalReserveMove] group=" + SafeUnitName(group) +
                    " pathBefore=" + state.PathCount +
                    " pathAfter=" + afterPaths +
                    " queueBefore=" + state.QueueCount +
                    " queueAfter=" + SafeOrderQueueCount(group) +
                    " risk=" + decision.IsRisk +
                    " reason=" + decision.Reason);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-reserve-move", "Tactical reserve move observer failed: " + ex.Message);
            }
        }

        private static void ObserveObjectiveChainMove(AIBattle battle)
        {
            if (!BugTelemetryEnabled()) return;

            try
            {
                var chain = SafeList(battle, ref _objectiveChainField, "objective" + "chain");
                if (chain == null || chain.Count <= 0) return;

                int centerUnderCommander = 0;
                int attachedUnderCommander = 0;
                Regiment firstCenter = null;
                for (int i = 0; i < chain.Count; i++)
                {
                    Regiment center = SafeRegimentField(chain[i], ref _chainCenterUnitField, "linegroup_centerunit");
                    if (center == null) continue;
                    if (firstCenter == null) firstCenter = center;
                    if (center.dlcw_isundercommander) centerUnderCommander++;
                    attachedUnderCommander += CountAttachedUnderCommander(center);
                }

                var decision = TacticalBattlefieldBugDiagnostics.ClassifyObjectiveChainMovement(
                    objectiveChainMove: true,
                    centerGroupUnderPlayerCommander: centerUnderCommander > 0,
                    attachedPlayerSubordinate: attachedUnderCommander > 0,
                    attachedUnitCount: attachedUnderCommander);
                if (!decision.IsRisk && attachedUnderCommander <= 0 && centerUnderCommander <= 0) return;

                EmitDirect(
                    "TacticalObjectiveMove",
                    "objective-move|" + SafeInstanceId(firstCenter) + "|" + centerUnderCommander + "|" + attachedUnderCommander + "|" + decision.Reason,
                    "[TacticalObjectiveMove] center=" + SafeUnitName(firstCenter) +
                    " chains=" + chain.Count +
                    " centerUnderCommanderCount=" + centerUnderCommander +
                    " attachedUnderCommanderCount=" + attachedUnderCommander +
                    " risk=" + decision.IsRisk +
                    " reason=" + decision.Reason);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-objective-move", "Tactical objective-chain observer failed: " + ex.Message);
            }
        }

        private static ObjectiveMoveState CaptureObjectiveMoveState(AIBattle battle)
        {
            var state = new ObjectiveMoveState();
            if (!BugTelemetryEnabled()) return state;

            try
            {
                var chain = SafeList(battle, ref _objectiveChainField, "objective" + "chain");
                if (chain == null || chain.Count <= 0) return state;

                for (int i = 0; i < chain.Count; i++)
                {
                    Regiment center = SafeRegimentField(chain[i], ref _chainCenterUnitField, "linegroup_centerunit");
                    if (center == null) continue;

                    int attachedUnderCommander = CountAttachedUnderCommander(center);
                    if (!center.dlcw_isundercommander && attachedUnderCommander <= 0) continue;

                    state.ExposedChainCount++;
                    if (state.FirstCenter == null) state.FirstCenter = center;
                    state.Units.Add(SnapshotObjectiveUnit(center, center: true));

                    if (center.allattachedunits == null) continue;
                    for (int j = 0; j < center.allattachedunits.Length; j++)
                    {
                        Regiment attached = center.allattachedunits[j];
                        if (attached != null && attached.dlcw_isundercommander)
                            state.Units.Add(SnapshotObjectiveUnit(attached, center: false));
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-objective-mutation:capture", "Tactical objective-chain mutation capture failed: " + ex.Message);
            }

            return state;
        }

        private static void ObserveObjectiveChainMutation(ObjectiveMoveState state)
        {
            if (!BugTelemetryEnabled() || state == null || state.Units.Count <= 0) return;

            try
            {
                bool centerMutated = false;
                bool attachedMutated = false;
                int changed = 0;

                for (int i = 0; i < state.Units.Count; i++)
                {
                    ObjectiveUnitSnapshot snapshot = state.Units[i];
                    if (!ObjectiveSnapshotChanged(snapshot)) continue;

                    changed++;
                    if (snapshot.Center) centerMutated = true;
                    else attachedMutated = true;
                }

                var decision = TacticalBattlefieldBugDiagnostics.ClassifyObjectiveChainMutation(
                    exposedPlayerSubordinateChain: state.ExposedChainCount > 0,
                    centerMutated: centerMutated,
                    attachedPlayerSubordinateMutated: attachedMutated,
                    changedUnitCount: changed);

                if (!decision.IsRisk && changed <= 0) return;

                EmitDirect(
                    "TacticalObjectiveMutation",
                    "objective-mutation|" + SafeInstanceId(state.FirstCenter) + "|" + changed + "|" + centerMutated + "|" + attachedMutated + "|" + decision.Reason,
                    "[TacticalObjectiveMutation] center=" + SafeUnitName(state.FirstCenter) +
                    " exposedChains=" + state.ExposedChainCount +
                    " changedUnits=" + changed +
                    " centerMutated=" + centerMutated +
                    " attachedPlayerSubordinateMutated=" + attachedMutated +
                    " risk=" + decision.IsRisk +
                    " reason=" + decision.Reason);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-objective-mutation", "Tactical objective-chain mutation observer failed: " + ex.Message);
            }
        }

        private static ObjectiveUnitSnapshot SnapshotObjectiveUnit(Regiment unit, bool center)
        {
            return new ObjectiveUnitSnapshot(
                unit,
                center,
                SafeRegimentPaths(unit),
                SafePositionX(unit),
                SafePositionZ(unit),
                SafeLastWaypointX(unit),
                SafeLastWaypointZ(unit),
                SafeCurrentObjectiveId(unit));
        }

        private static bool ObjectiveSnapshotChanged(ObjectiveUnitSnapshot snapshot)
        {
            Regiment unit = snapshot.Unit;
            if (unit == null) return false;

            return SafeRegimentPaths(unit) != snapshot.PathCount ||
                Math.Abs(SafePositionX(unit) - snapshot.PositionX) > 1f ||
                Math.Abs(SafePositionZ(unit) - snapshot.PositionZ) > 1f ||
                Math.Abs(SafeLastWaypointX(unit) - snapshot.WaypointX) > 1f ||
                Math.Abs(SafeLastWaypointZ(unit) - snapshot.WaypointZ) > 1f ||
                SafeCurrentObjectiveId(unit) != snapshot.ObjectiveId;
        }

        private static void EmitDirect(string key, string signature, string message)
        {
            bool verbose = Plugin.Instance != null && Plugin.Instance.TacticalObserverVerboseLogging.Value;
            float minSeconds = Plugin.Instance != null
                ? Mathf.Max(1f, Plugin.Instance.TacticalObserverMinSecondsBetweenSummaries.Value)
                : 30f;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, key, signature, Time.realtimeSinceStartup, minSeconds, verbose))
                return;

            Plugin.Log.LogInfo(message);
        }

        private static void EmitDecisionMatrix(
            TacticalObservedEvent eventType,
            AIBattle battle,
            Regiment focusGroup,
            TacticalBattleContext context)
        {
            if (!DecisionMatrixEnabled() || battle == null) return;

            try
            {
                if (context == null) context = TacticalBattleContext.Empty();

                IList units = SafeList(battle, ref _unitsUsedField, "unitsused");
                if (units == null || units.Count <= 0)
                    units = SafeList(battle, ref _allGroupsAssignedField, "allgroupsassigned");

                int maxRows = Plugin.Instance != null
                    ? Mathf.Clamp(Plugin.Instance.TacticalDecisionMatrixMaxRows.Value, 1, 300)
                    : 80;
                float minSeconds = Plugin.Instance != null
                    ? Mathf.Max(1f, Plugin.Instance.TacticalDecisionMatrixMinSecondsBetweenSnapshots.Value)
                    : 1f;
                bool verbose = Plugin.Instance != null && Plugin.Instance.TacticalObserverVerboseLogging.Value;
                string baseSignature = eventType + "|" + TacticalTelemetry.Signature(eventType, context);

                if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "DecisionMatrixSummary", baseSignature, Time.realtimeSinceStartup, minSeconds, verbose))
                    return;

                Plugin.Log.LogInfo("[TacticalDecisionMatrix] event=" + eventType +
                    " row=battle side=" + context.Side +
                    " alliance=" + context.Alliance +
                    " macro=" + TacticalTelemetry.MacroName(context.MacroAi) +
                    " groups=" + context.GroupCount +
                    " charging=" + context.ChargingCount +
                    " feud=" + context.FeudGroupCount +
                    " reserves=" + context.ReserveGroupCount +
                    " artillery=" + context.ArtilleryGroupCount +
                    " fallback=" + context.FallbackCount +
                    " retreating=" + context.RetreatingCount +
                    " visibleEnemy=" + context.VisibleEnemyCount +
                    " chains=" + context.ObjectiveChainCount +
                    " forceBalance=" + BucketForObserver(context.ForceBalance) +
                    " currentOdds=" + BucketForObserver(context.CurrentGlobalOdds) +
                    " projectedOdds=" + BucketForObserver(context.ProjectedGlobalOdds) +
                    " decisive=" + context.DecisiveSectorId +
                    " odds=" + TacticalCurrentOrderSignature.Safe(context.OddsSummary) +
                    " cap=" + maxRows +
                    " dlcWl=" + SafeDlcWlActive());

                if (focusGroup != null)
                {
                    EmitDecisionMatrixRow(eventType, context, focusGroup, 0, true);
                    return;
                }

                if (units == null || units.Count <= 0) return;

                int emitted = 0;
                for (int i = 0; i < units.Count && emitted < maxRows; i++)
                {
                    var group = units[i] as Regiment;
                    if (group == null || group.unittyp <= 13) continue;
                    EmitDecisionMatrixRow(eventType, context, group, i, false);
                    emitted++;
                }

                if (units.Count > maxRows)
                {
                    Plugin.Log.LogInfo("[TacticalDecisionMatrix] event=" + eventType +
                        " row=truncated totalCandidates=" + units.Count +
                        " emitted=" + emitted +
                        " cap=" + maxRows);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-decision-matrix", "Tactical decision-matrix observer failed: " + ex.Message);
            }
        }

        private static void EmitDecisionMatrixRow(
            TacticalObservedEvent eventType,
            TacticalBattleContext context,
            Regiment group,
            int index,
            bool focus)
        {
            if (group == null || context == null) return;

            var sector = BuildMatrixSector(group, index);
            var wlGuard = TacticalWlActionGuard.Decide(
                true,
                SafeDlcWlActive(),
                TacticalWlGuardAction.FeudMovement,
                group.dlcw_isundercommander,
                group.dlcw_isundercommander,
                CountAttachedUnderCommander(group) > 0);
            bool orderFrictionAllows = MatrixOrderFrictionAllowsChange(group);
            int vanillaStance = SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered);
            var stanceDecision = TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.unittyp)
                ? TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
                    vanillaStance,
                    context.MacroAi,
                    sector,
                    orderFrictionAllows,
                    wlGuard.Allow))
                : new TacticalGroupStanceDecision(TacticalDoctrineDecisionKind.Skip, vanillaStance, "command-scope");

            Plugin.Log.LogInfo("[TacticalDecisionMatrix] event=" + eventType +
                " row=group index=" + index +
                " focus=" + focus +
                " side=" + context.Side +
                " macro=" + TacticalTelemetry.MacroName(context.MacroAi) +
                " unit=" + SafeUnitName(group) +
                " type=" + group.unittyp +
                " top=" + group.istopunit +
                " underCommander=" + group.dlcw_isundercommander +
                " attachedUnderCommander=" + CountAttachedUnderCommander(group) +
                " parent=" + SafeParentId(group) +
                " aiStance=" + group.ai_stance +
                " orderedStance=" + vanillaStance +
                " whiskeyStanceKind=" + stanceDecision.Kind +
                " whiskeyStance=" + stanceDecision.GroupStance +
                " whiskeyReason=" + stanceDecision.Reason +
                " wlGuard=" + (wlGuard.Allow ? "allow" : "deny") + ":" + wlGuard.Reason +
                " orderFrictionAllows=" + orderFrictionAllows +
                " sector=" + sector.SectorId +
                " mission=" + sector.Mission +
                " sectorOdds=" + BucketForObserver(sector.Odds) +
                " own=" + BucketForObserver(sector.OwnStrength) +
                " enemy=" + BucketForObserver(sector.EnemyStrength) +
                " confidence=" + BucketForObserver(sector.Confidence) +
                " movement=" + SafeMovementMode(group) +
                " formation=" + FormatFormation(SafeFormation(group)) +
                " orderedFormation=" + FormatFormation(SafeFormationOrdered(group)) +
                " paths=" + SafeRegimentPaths(group) +
                " pathInterrupted=" + group.pathinterrupted +
                " queue=" + SafeOrderQueueCount(group) +
                " activeMove=" + HasActiveMoveOrder(group) +
                " receivedFire=" + MatrixReceivedFire(group) +
                " closestEnemy=" + MatrixClosestEnemy(group) +
                " angleEnemy=" + BucketForObserver(MatrixEnemyAngleStrength(group)) +
                " flankThreat=" + BucketForObserver(group.flanksthreated) +
                " outflanked=" + group.outflanked +
                " cover=" + BucketForObserver(group.covervalue) +
                " fort=" + (group.fortinrange ? "1" : "0") +
                " feud=" + group.ai_feudstance +
                " objective=" + SafeCurrentObjectiveId(group) +
                " position=" + PointSignature(SafePositionX(group), SafePositionZ(group)) +
                " waypoint=" + PointSignature(SafeLastWaypointX(group), SafeLastWaypointZ(group)) +
                " orderState=" + SafeOrderState(group));
        }

        private static TacticalSectorAssessment BuildMatrixSector(Regiment group, int index)
        {
            Regiment closest = group != null && group.unitrange != null ? group.unitrange.closestenemyunitfarreg : null;
            return TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
                index,
                group != null ? Math.Max(group.groupowninrange, group.groupstrengthaigroup) : 0f,
                group != null ? group.groupenemiesinrange : 0f,
                MatrixEnemyAngleStrength(group),
                closest != null ? closest.strength : 0f,
                closest != null ? closest.unittyp : -1,
                closest != null ? SafeUnitName(closest) : string.Empty,
                closest != null && closest.isrouted,
                closest != null && closest.permanentlydetached,
                group != null && (group.flanksthreated > 0f || group.outflanked > 0),
                group != null && (group.covervalue > 0.5f || group.fortinrange)));
        }

        private static bool MatrixOrderFrictionAllowsChange(Regiment group)
        {
            return TacticalOrderSettlementGate.Evaluate(new TacticalOrderSettlementGate.Input
            {
                OrderQueueCount = SafeOrderQueueCount(group),
                OrderState = SafeOrderState(group),
                RegimentPaths = group != null ? group.regimentpaths : 0,
                PathInterrupted = group != null && group.pathinterrupted,
                MovementMode = group != null ? group.movementmode : -1,
                ActiveMove = HasActiveMoveOrder(group)
            }).AllowChange;
        }

        private static bool MatrixReceivedFire(Regiment group)
        {
            try
            {
                if (group == null) return false;
                var field = AccessTools.Field(group.GetType(), "receivedfire");
                var received = field != null ? field.GetValue(group) as IList : null;
                return received != null && received.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static float MatrixEnemyAngleStrength(Regiment group)
        {
            try
            {
                if (group == null || group.unitrange == null || group.unitrange.enemystrengthwithinangle == null) return 0f;
                float total = 0f;
                for (int i = 0; i < group.unitrange.enemystrengthwithinangle.Length; i++)
                    total += Math.Max(0f, group.unitrange.enemystrengthwithinangle[i]);
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static string MatrixClosestEnemy(Regiment group)
        {
            try
            {
                if (group == null || group.unitrange == null) return "-";
                if (group.unitrange.closestenemyunitfarreg != null)
                    return SafeUnitName(group.unitrange.closestenemyunitfarreg);
                if (group.unitrange.closestenemyunit != null)
                {
                    var nearest = group.unitrange.closestenemyunit as Regiment[];
                    return nearest != null
                        ? "enemy-array-" + nearest.Length
                        : TacticalCurrentOrderSignature.Safe(group.unitrange.closestenemyunit.GetType().Name);
                }
                return "-";
            }
            catch
            {
                return "-";
            }
        }

        private static TacticalBattleContext BuildContext(AIBattle battle, Regiment group)
        {
            var context = TacticalBattleContext.Empty();
            if (battle == null) return context;

            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            var bunits = SafeField<BattleUnits>(battle, ref _bunitsField, "bunits");
            var unitsUsed = SafeList(battle, ref _unitsUsedField, "unitsused");
            var allGroups = SafeList(battle, ref _allGroupsAssignedField, "allgroupsassigned");
            var chain = SafeList(battle, ref _objectiveChainField, "objective" + "chain");
            bool groupScoped = group != null;

            context.Side = side;
            context.MacroAi = macro;
            context.Alliance = SafeAlliance(bunits, side);
            context.GroupCount = groupScoped ? 1 : CountList(allGroups);
            context.ObjectiveChainCount = CountList(chain);
            context.SectorSource = context.ObjectiveChainCount > 0
                ? TacticalSectorSource.ObjectiveChain
                : TacticalSectorSource.None;
            context.SectorSignature = "chains=" + context.ObjectiveChainCount + ",groups=" + context.GroupCount;
            context.OrderSignature = groupScoped ? BuildGroupOrderSignature(group) : BuildOrderSignature(unitsUsed);
            context.ForceBalance = SafeForceBalance(bunits, side);
            context.ReinforcementsWithin24Hours = SafeReinforcements(bunits, side);

            if (groupScoped) MergeGroupCounts(group, context);
            else CountUnits(unitsUsed, context);

            var odds = BuildOddsDoctrine(battle, bunits, unitsUsed, side);
            context.CurrentGlobalOdds = odds.CurrentGlobalOdds;
            context.ProjectedGlobalOdds = odds.ProjectedGlobalOdds;
            context.DecisiveSectorId = odds.DecisiveSectorId;
            context.OddsSignature = "cur=" + BucketForObserver(odds.CurrentGlobalOdds)
                + ",proj=" + BucketForObserver(odds.ProjectedGlobalOdds)
                + ",decisive=" + odds.DecisiveSectorId
                + ",posture=" + odds.InferiorForcePosture;
            context.OddsSummary = "posture=" + odds.InferiorForcePosture
                + ",confidence=" + FormatHours(odds.Confidence)
                + ",assault=" + (odds.AllowAssault ? "1" : "0");

            return context;
        }

        private static TacticalOddsAssessment BuildOddsDoctrine(
            AIBattle battle,
            BattleUnits bunits,
            IList unitsUsed,
            int side)
        {
            float own = Math.Max(1f, SafeSideInfoFloat(bunits, side, "totalactiveforce"));
            float confirmedEnemy = EstimateVisibleEnemyStrength(unitsUsed);
            float inferredEnemy = EstimateInferredEnemyStrength(unitsUsed);
            float enemy = Math.Max(confirmedEnemy, inferredEnemy);
            if (enemy <= 0f)
            {
                float forceBalance = SafeForceBalance(bunits, side);
                if (forceBalance > 0.01f && forceBalance < 0.99f)
                    enemy = own * Math.Max(0.1f, (1f - forceBalance) / Math.Max(0.1f, forceBalance));
            }

            var contact = TacticalContactLedger.Classify(new TacticalContactInput(
                confirmedEnemy,
                confirmedEnemy,
                inferredEnemy,
                confirmedEnemy > 0f ? 0f : 9999f,
                AnyReceivedFire(unitsUsed),
                confirmedEnemy <= 0f));

            return TacticalOddsDoctrine.Evaluate(new TacticalOddsInput(
                own,
                confirmedEnemy,
                confirmedEnemy,
                Math.Max(enemy, inferredEnemy),
                SafeReinforcements(bunits, side),
                0f,
                contact,
                BuildSectorAssessments(battle, unitsUsed)));
        }

        private static TacticalSectorAssessment[] BuildSectorAssessments(AIBattle battle, IList units)
        {
            if (units == null) return Array.Empty<TacticalSectorAssessment>();

            var sectors = new List<TacticalSectorAssessment>();
            int sectorId = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var group = units[i] as Regiment;
                if (group == null || group.unittyp <= 13) continue;

                Regiment closest = group.unitrange != null ? group.unitrange.closestenemyunitfarreg : null;
                sectors.Add(TacticalGroupSectorEstimator.BuildSector(new TacticalGroupContactInput(
                    sectorId++,
                    Math.Max(group.groupowninrange, group.groupstrengthaigroup),
                    group.groupenemiesinrange,
                    MatrixEnemyAngleStrength(group),
                    closest != null ? closest.strength : 0f,
                    closest != null ? closest.unittyp : -1,
                    closest != null ? SafeUnitName(closest) : string.Empty,
                    closest != null && closest.isrouted,
                    closest != null && closest.permanentlydetached,
                    group.flanksthreated > 0f || group.outflanked > 0,
                    group.covervalue > 0.5f || group.fortinrange)));
            }

            return sectors.ToArray();
        }

        private static float EstimateVisibleEnemyStrength(IList units)
        {
            if (units == null) return 0f;

            float total = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null || unit.unitrange == null) continue;
                if (unit.unitrange.closestenemyunitfarreg != null)
                    total += Math.Max(0, unit.unitrange.closestenemyunitfarreg.strength);
                else if (unit.unitrange.closestenemyunit != null)
                    total += 100f;
            }

            return total;
        }

        private static float EstimateInferredEnemyStrength(IList units)
        {
            if (units == null) return 0f;

            float total = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null || unit.unitrange == null || unit.unitrange.enemystrengthwithinangle == null) continue;
                for (int j = 0; j < unit.unitrange.enemystrengthwithinangle.Length; j++)
                    total += Math.Max(0f, unit.unitrange.enemystrengthwithinangle[j]);
            }

            return total;
        }

        private static bool AnyReceivedFire(IList units)
        {
            if (units == null) return false;

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                try
                {
                    var field = AccessTools.Field(unit.GetType(), "receivedfire");
                    var received = field != null ? field.GetValue(unit) as IList : null;
                    if (received != null && received.Count > 0) return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static TacticalObserverSnapshot SnapshotBattle(AIBattle battle)
        {
            var context = BuildContext(battle, null);
            return new TacticalObserverSnapshot
            {
                GroupCount = context.GroupCount,
                ChargingCount = context.ChargingCount,
                FeudGroupCount = context.FeudGroupCount,
                ReserveGroupCount = context.ReserveGroupCount,
                ArtilleryGroupCount = context.ArtilleryGroupCount,
                FallbackCount = context.FallbackCount,
                RetreatingCount = context.RetreatingCount,
                Signature = TacticalTelemetry.Signature(TacticalObservedEvent.Macro, context)
            };
        }

        private static TacticalObserverSnapshot SnapshotGroup(Regiment group)
        {
            var snapshot = TacticalObserverSnapshot.Empty();
            if (group == null || group.allattachedunits == null) return snapshot;

            snapshot.GroupCount = 1;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit == null) continue;
                if (unit.movementmode == 3) snapshot.ChargingCount++;
                if (unit.movementmode == 2) snapshot.FallbackCount++;
                if (unit.movementmode == 5 || unit.movementmode == 6) snapshot.RetreatingCount++;
                if (unit.unittyp == 2) snapshot.ArtilleryGroupCount++;
            }

            snapshot.Signature = "g=" + SafeInstanceId(group) + "|c=" + snapshot.ChargingCount + "|f=" + snapshot.FallbackCount;
            return snapshot;
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalObserver.Value;
        }

        private static bool BugTelemetryEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalBugTelemetry.Value;
        }

        private static bool DecisionMatrixEnabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalDecisionMatrixLogging.Value;
        }

        private static void CountUnits(IList units, TacticalBattleContext context)
        {
            if (units == null || context == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                CountUnit(unit, context);
            }
        }

        private static void MergeGroupCounts(Regiment group, TacticalBattleContext context)
        {
            if (group == null || group.allattachedunits == null || context == null) return;
            for (int i = 0; i < group.allattachedunits.Length; i++)
                CountUnit(group.allattachedunits[i], context);
        }

        private static void CountUnit(Regiment unit, TacticalBattleContext context)
        {
            if (unit == null || context == null) return;
            if (unit.movementmode == 3) context.ChargingCount++;
            if (unit.movementmode == 2) context.FallbackCount++;
            if (unit.movementmode == 5 || unit.movementmode == 6) context.RetreatingCount++;
            if (unit.ai_feudstance >= 0) context.FeudGroupCount++;
            if (unit.unittyp == 2) context.ArtilleryGroupCount++;
            if (unit.unittyp > 13 && SafeIntField(unit, ref _orderedStanceField, "ai_" + "stanceordered", -1) == 1)
                context.ReserveGroupCount++;
            if (unit.unitrange != null)
            {
                if (unit.unitrange.closestenemyunitfarreg != null) context.VisibleEnemyCount++;
                else if (unit.unitrange.closestenemyunit != null) context.VisibleEnemyCount++;
            }
        }

        private static string BuildOrderSignature(IList units)
        {
            if (units == null) return "-";

            int moving = 0;
            int waiting = 0;
            int interrupted = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i] as Regiment;
                if (unit == null) continue;
                if (HasActiveMoveSignal(unit)) moving++;
                if (unit.pathinterrupted) interrupted++;
                if (!HasActiveMoveSignal(unit) && unit.movementmode == 0) waiting++;
            }

            return "moving=" + moving + ",waiting=" + waiting + ",interrupted=" + interrupted;
        }

        private static string BuildGroupOrderSignature(Regiment group)
        {
            if (group == null || group.allattachedunits == null) return "-";

            int moving = 0;
            int waiting = 0;
            int interrupted = 0;
            for (int i = 0; i < group.allattachedunits.Length; i++)
            {
                var unit = group.allattachedunits[i];
                if (unit == null) continue;
                if (HasActiveMoveSignal(unit)) moving++;
                if (unit.pathinterrupted) interrupted++;
                if (!HasActiveMoveSignal(unit) && unit.movementmode == 0) waiting++;
            }

            return "group=" + SafeInstanceId(group) + ",moving=" + moving + ",waiting=" + waiting + ",interrupted=" + interrupted;
        }

        private static bool HasActiveMoveSignal(Regiment unit)
        {
            try
            {
                if (unit == null) return false;
                Vector3 lastWaypoint = unit.lastsetwaypointposition;
                bool hasLastWaypoint = !IsZeroVector(lastWaypoint);
                float distance = hasLastWaypoint ? Vector3.Distance(unit.transform.position, lastWaypoint) : 0f;
                return CommandWaypointWritePolicy.IsExecutorMovementActive(
                    unit.pathinterrupted,
                    unit.regimentpaths,
                    unit.movementmode,
                    unit.groupsubordinatesmoving,
                    unit.groupsubordinatesmovingnonai,
                    hasLastWaypoint,
                    distance,
                    15f);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsZeroVector(Vector3 value)
        {
            return value.x == 0f && value.y == 0f && value.z == 0f;
        }

        private static TacticalCurrentOrderSignature ReadGivenOrderSignature()
        {
            try
            {
                var order = DLC_WL.givenorder;
                if (order == null) return TacticalCurrentOrderSignature.Empty;
                return new TacticalCurrentOrderSignature(
                    SafeInstanceId(order.groupunit),
                    order.type,
                    order.position.x,
                    order.position.z,
                    order.arearotation,
                    order.destinationname);
            }
            catch
            {
                return TacticalCurrentOrderSignature.Empty;
            }
        }

        private static int ReadGivenOrdersSession()
        {
            try
            {
                Type givenOrdersType = typeof(DLC_WL).GetNestedType("GivenOrders", BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo sessionField = givenOrdersType != null
                    ? givenOrdersType.GetField("givenorderssession", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    : null;
                return sessionField != null ? Convert.ToInt32(sessionField.GetValue(null)) : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static Regiment.OrderQueue FindLatestQueuedOrder(Regiment issuer, GameObject advisedUnit, int orderType)
        {
            if (issuer == null || issuer.orderqueue == null) return null;
            for (int i = issuer.orderqueue.Count - 1; i >= 0; i--)
            {
                var queued = issuer.orderqueue[i];
                if (queued == null) continue;
                if (queued.ordertype == orderType && queued.advisedunit == advisedUnit) return queued;
            }

            return null;
        }

        private static Regiment.OrderQueue.CourierLine FindLatestCourierLine(Regiment issuer)
        {
            if (issuer == null || issuer.orderqueue == null) return null;
            for (int i = issuer.orderqueue.Count - 1; i >= 0; i--)
            {
                var queued = issuer.orderqueue[i];
                if (queued == null || queued.courierline == null || queued.courierline.Count <= 0) continue;
                return queued.courierline[queued.courierline.Count - 1];
            }

            return null;
        }

        private static Regiment SafeRegiment(GameObject unit)
        {
            try
            {
                return unit != null ? unit.GetComponent<Regiment>() : null;
            }
            catch
            {
                return null;
            }
        }

        private static string OrderRelation(bool sourceUnderCommander, bool targetUnderCommander)
        {
            if (!sourceUnderCommander && targetUnderCommander) return "ai-to-player-subordinate";
            if (sourceUnderCommander && targetUnderCommander) return "player-chain";
            if (sourceUnderCommander && !targetUnderCommander) return "player-to-ai";
            return "ai-chain";
        }

        private static string OrderTypeName(int orderType)
        {
            if (orderType == 0) return "move-append";
            if (orderType == 1) return "move-new";
            if (orderType == 2) return "stop";
            if (orderType >= 3 && orderType <= 9) return "stance-" + (orderType - 3);
            if (orderType >= 10 && orderType <= 19) return "formation-" + (orderType - 10);
            if (orderType == 20) return "refuse-left";
            if (orderType == 21) return "refuse-right";
            if (orderType == 23) return "detach-toggle";
            if (orderType >= 30 && orderType <= 39) return "combat-" + (orderType - 30);
            if (orderType >= 100 && orderType < 120) return "campaign-stance-" + (orderType - 100);
            if (orderType >= 120 && orderType < 130) return "cavalry-" + (orderType - 120);
            return "type-" + orderType;
        }

        private static string SafeUnitName(Regiment unit)
        {
            try
            {
                if (unit == null) return "-";
                string name = ((UnityEngine.Object)unit).name;
                return (string.IsNullOrEmpty(name) ? "unit" : name.Replace(' ', '_')) + "#" + SafeInstanceId(unit);
            }
            catch
            {
                return "unit#" + SafeInstanceId(unit);
            }
        }

        private static bool SafeDlcWlActive()
        {
            try
            {
                return DLC_WL.dlc_scenarioactive;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatHours(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.00";
            return value.ToString("0.00");
        }

        private static string BucketSeconds(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return Mathf.Round(value * 2f).ToString("0");
        }

        private static int SafeAlliance(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                if (side < 0 || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch
            {
                return -1;
            }
        }

        private static float SafeForceBalance(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                return bunits.sideinformation[side].forcebalance;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SafeReinforcements(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                return bunits.sideinformation[side].reinforcementarrivalswithin24hrs;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SafeSideInfoFloat(BattleUnits bunits, int side, string fieldName)
        {
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                if (side < 0 || side >= bunits.sideinformation.Length) return 0f;
                var info = bunits.sideinformation[side];
                if (info == null) return 0f;
                var field = ResolveSideInfoField(info.GetType(), fieldName);
                if (field == null) return 0f;
                object value = field.GetValue(info);
                return value == null ? 0f : Convert.ToSingle(value);
            }
            catch
            {
                return 0f;
            }
        }

        private static FieldInfo ResolveSideInfoField(Type infoType, string fieldName)
        {
            if (infoType == null) return null;

            string key = infoType.FullName + ":" + fieldName;
            if (_sideInfoFieldCache.ContainsKey(key))
                return _sideInfoFieldCache[key];

            var field = infoType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _sideInfoFieldCache[key] = field;
            return field;
        }

        private static string BucketForObserver(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.0";
            return (Math.Round(value * 2f) / 2f).ToString("0.0");
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return (int)cache.GetValue(instance);
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

        private static TacticalCommanderProfile BuildCommanderProfile(Regiment unit)
        {
            if (unit == null)
            {
                return TacticalCommanderProfile.FromVanillaShape(
                    0,
                    "unknown",
                    -1,
                    false,
                    false,
                    -1,
                    -1,
                    -1,
                    0.50f);
            }

            try
            {
                return TacticalCommanderProfile.FromVanillaShape(
                    SafeInstanceId(unit),
                    SafeUnitName(unit),
                    unit.unittyp,
                    unit.istopunit,
                    unit.dlcw_isundercommander,
                    SafeParentId(unit),
                    SafeAlliance(unit),
                    SafeSide(unit),
                    0.50f);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:commander-profile", "Tactical commander profile lookup failed: " + ex.Message);
                return TacticalCommanderProfile.FromVanillaShape(
                    SafeInstanceId(unit),
                    SafeUnitName(unit),
                    -1,
                    false,
                    false,
                    -1,
                    -1,
                    -1,
                    0.50f);
            }
        }

        private static int SafePathId(Regiment unit, bool ignoreOrderDelay)
        {
            try
            {
                return unit != null ? unit.GetLastTransmittedPath(ignoreOrderDelay) : -1;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-observer:path-id", "Tactical path id lookup failed: " + ex.Message);
                return -1;
            }
        }

        private static int SafeParentId(Regiment unit)
        {
            try
            {
                if (unit == null) return -1;
                if (unit.parentregiment != null)
                {
                    var parent = SafeRegiment(unit.parentregiment);
                    return SafeInstanceId(parent);
                }

                if (_parentRegimentField == null) _parentRegimentField = AccessTools.Field(typeof(Regiment), "parentregiment");
                var parentObj = _parentRegimentField != null ? _parentRegimentField.GetValue(unit) as GameObject : null;
                var reflectedParent = SafeRegiment(parentObj);
                return SafeInstanceId(reflectedParent);
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeAlliance(Regiment unit)
        {
            try
            {
                if (unit == null) return -1;
                if (_allianceField == null) _allianceField = AccessTools.Field(typeof(Regiment), "alliance");
                if (_allianceField != null) return (int)_allianceField.GetValue(unit);
                return unit.alliance;
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeSide(Regiment unit)
        {
            try
            {
                if (unit == null) return -1;
                if (!_sideFieldResolved)
                {
                    _sideField = typeof(Regiment).GetField("side", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _sideFieldResolved = true;
                }

                return _sideField != null ? (int)_sideField.GetValue(unit) : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeOrderState(Regiment unit)
        {
            return SafeIntField(unit, ref _orderStateField, "orderstate", -1);
        }

        private static int SafeRegimentPaths(Regiment unit)
        {
            try { return unit != null ? unit.regimentpaths : -1; }
            catch { return -1; }
        }

        private static int SafeOrderQueueCount(Regiment unit)
        {
            try { return unit != null && unit.orderqueue != null ? unit.orderqueue.Count : 0; }
            catch { return 0; }
        }

        private static bool HasActiveMoveOrder(Regiment unit)
        {
            try
            {
                return unit != null &&
                    unit.ordertypeactive != null &&
                    unit.ordertypeactive.Length > 1 &&
                    (unit.ordertypeactive[0] || unit.ordertypeactive[1]);
            }
            catch
            {
                return false;
            }
        }

        private static float SafeLastWaypointX(Regiment unit)
        {
            try { return unit != null ? unit.lastsetwaypointposition.x : 0f; }
            catch { return 0f; }
        }

        private static float SafeLastWaypointZ(Regiment unit)
        {
            try { return unit != null ? unit.lastsetwaypointposition.z : 0f; }
            catch { return 0f; }
        }

        private static float SafePositionX(Regiment unit)
        {
            try { return unit != null ? ((Component)unit).transform.position.x : 0f; }
            catch { return 0f; }
        }

        private static float SafePositionZ(Regiment unit)
        {
            try { return unit != null ? ((Component)unit).transform.position.z : 0f; }
            catch { return 0f; }
        }

        private static int SafeMovementMode(Regiment unit)
        {
            try { return unit != null ? unit.movementmode : -1; }
            catch { return -1; }
        }

        private static bool SafeRouted(Regiment unit)
        {
            try { return unit != null && (unit.isrouted || unit.markedforrout); }
            catch { return false; }
        }

        private static FormationChangeState SnapshotFormationChange(Regiment unit)
        {
            return new FormationChangeState(
                SafeFormation(unit),
                SafeFormationOrdered(unit),
                SafeLastFormation(unit),
                SafeGroupFormation(unit),
                SafeMarchColumnActive(unit),
                SafeDistanceMarchColumn(unit),
                SafeRegimentPaths(unit),
                SafeOrderState(unit),
                SafePathId(unit, ignoreOrderDelay: false));
        }

        private static int SafeFormation(Regiment unit)
        {
            try { return unit != null ? unit.formation : -1; }
            catch { return -1; }
        }

        private static int SafeFormationOrdered(Regiment unit)
        {
            try { return unit != null ? unit.formationordered : -1; }
            catch { return -1; }
        }

        private static int SafeLastFormation(Regiment unit)
        {
            try { return unit != null ? unit.lastformation : -1; }
            catch { return -1; }
        }

        private static int SafeGroupFormation(Regiment unit)
        {
            try { return unit != null ? unit.groupformation : -1; }
            catch { return -1; }
        }

        private static bool SafeMarchColumnActive(Regiment unit)
        {
            try
            {
                if (unit == null) return false;
                if (_changeToMarchColumnActiveField == null)
                    _changeToMarchColumnActiveField = AccessTools.Field(typeof(Regiment), "changetomarchcolumnactive");
                return _changeToMarchColumnActiveField != null && (bool)_changeToMarchColumnActiveField.GetValue(unit);
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeDistanceMarchColumn(Regiment unit)
        {
            try { return unit != null && unit.distancemarchcolumn; }
            catch { return false; }
        }

        private static bool AutoColumnEligible(WaypointState state)
        {
            try
            {
                return state.MarchColumnActive &&
                    state.Formation != 3 &&
                    state.PathCount <= 0 &&
                    XzDistance(state.StartX, state.StartZ, state.TargetX, state.TargetZ) > GamePrefs.mindistanceformarchcolumn &&
                    (state.MovementMode == 0 || state.MovementMode == 3);
            }
            catch
            {
                return false;
            }
        }

        private static string FormatFormation(int formation)
        {
            return formation + ":" + FormationName(formation);
        }

        private static string FormationName(int formation)
        {
            switch (formation)
            {
                case 0:
                    return "Line";
                case 1:
                    return "Column";
                case 2:
                    return "DoubleLine";
                case 3:
                    return "MarchColumn";
                case 4:
                    return "Skirmish";
                default:
                    return "Unknown";
            }
        }

        private static PathShapeSummary BuildPathShape(
            Regiment unit,
            WaypointState state,
            int afterPaths,
            bool newPath,
            bool modifyLastWaypoint)
        {
            if (unit == null || afterPaths <= 0 || unit.regimentpath == null)
                return EmptyPathShape();

            int startIndex = SelectObservedPathStart(state.PathCount, afterPaths, newPath, modifyLastWaypoint);
            int endIndex = Math.Min(afterPaths - 1, unit.regimentpath.Length - 1);
            if (startIndex < 0 || startIndex > endIndex)
                startIndex = endIndex;

            float direct = XzDistance(state.StartX, state.StartZ, state.TargetX, state.TargetZ);
            float pathLength = 0f;
            int corners = 0;
            bool foundFirst = false;
            float firstX = 0f;
            float firstZ = 0f;
            float finalX = 0f;
            float finalZ = 0f;
            float previousX = state.StartX;
            float previousZ = state.StartZ;
            string navStatus = "-";
            int pathStatus = SafePathStatus(unit, endIndex);

            for (int pathIndex = startIndex; pathIndex <= endIndex; pathIndex++)
            {
                NavMeshPath path = SafeNavMeshPath(unit, pathIndex);
                if (path == null || path.corners == null || path.corners.Length <= 0) continue;

                navStatus = SafeNavStatus(path);
                for (int cornerIndex = 0; cornerIndex < path.corners.Length; cornerIndex++)
                {
                    Vector3 corner = path.corners[cornerIndex];
                    float segment = XzDistance(previousX, previousZ, corner.x, corner.z);
                    if (segment > 0.1f)
                    {
                        pathLength += segment;
                        previousX = corner.x;
                        previousZ = corner.z;
                    }

                    if (!foundFirst && XzDistance(state.StartX, state.StartZ, corner.x, corner.z) > 2f)
                    {
                        foundFirst = true;
                        firstX = corner.x;
                        firstZ = corner.z;
                    }

                    finalX = corner.x;
                    finalZ = corner.z;
                    corners++;
                }
            }

            if (corners <= 0)
                return EmptyPathShape();

            if (!foundFirst)
            {
                firstX = finalX;
                firstZ = finalZ;
            }

            float targetAngle = AngleDegrees(state.StartX, state.StartZ, state.TargetX, state.TargetZ);
            float firstAngle = AngleDegrees(state.StartX, state.StartZ, firstX, firstZ);
            float delta = AngleDifference(targetAngle, firstAngle);
            float ratio = direct <= 0.1f ? 0f : pathLength / direct;

            return new PathShapeSummary(
                pathCreated: true,
                cornerCount: corners,
                directDistance: direct,
                pathLength: pathLength,
                pathRatio: ratio,
                firstSegmentDelta: delta,
                navStatus: navStatus,
                pathStatus: pathStatus,
                firstX: firstX,
                firstZ: firstZ,
                finalX: finalX,
                finalZ: finalZ);
        }

        private static PathShapeSummary EmptyPathShape()
        {
            return new PathShapeSummary(false, 0, 0f, 0f, 0f, 0f, "-", -1, 0f, 0f, 0f, 0f);
        }

        private static int SelectObservedPathStart(int beforePaths, int afterPaths, bool newPath, bool modifyLastWaypoint)
        {
            _ = newPath;
            if (afterPaths <= 0) return -1;
            if (afterPaths <= beforePaths) return 0;
            if (modifyLastWaypoint) return Math.Max(0, afterPaths - 1);
            return Math.Max(0, beforePaths);
        }

        private static NavMeshPath SafeNavMeshPath(Regiment unit, int pathIndex)
        {
            try
            {
                if (unit == null || unit.regimentpath == null) return null;
                if (pathIndex < 0 || pathIndex >= unit.regimentpath.Length) return null;
                return unit.regimentpath[pathIndex];
            }
            catch
            {
                return null;
            }
        }

        private static string SafeNavStatus(NavMeshPath path)
        {
            try
            {
                return path != null ? path.status.ToString() : "-";
            }
            catch
            {
                return "-";
            }
        }

        private static int SafePathStatus(Regiment unit, int pathIndex)
        {
            try
            {
                if (unit == null || unit.pathstatus == null) return -1;
                if (pathIndex < 0 || pathIndex >= unit.pathstatus.Length) return -1;
                return unit.pathstatus[pathIndex];
            }
            catch
            {
                return -1;
            }
        }

        private static string PointSignature(float x, float z)
        {
            return "x=" + BucketForObserver(x) + ",z=" + BucketForObserver(z);
        }

        private static float XzDistance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float AngleDegrees(float ax, float az, float bx, float bz)
        {
            float dx = bx - ax;
            float dz = bz - az;
            if (Mathf.Abs(dx) < 0.001f && Mathf.Abs(dz) < 0.001f) return 0f;
            float angle = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            return angle < 0f ? angle + 360f : angle;
        }

        private static float AngleDifference(float a, float b)
        {
            float delta = Mathf.Abs((a - b) % 360f);
            return delta > 180f ? 360f - delta : delta;
        }

        private static int SafeCurrentObjectiveId(Regiment unit)
        {
            try
            {
                var objective = SafeField<UnityEngine.Object>(unit, ref _currentSetObjectiveField, "currentsetobjective");
                return SafeInstanceId(objective);
            }
            catch
            {
                return 0;
            }
        }

        private static int CountGroupPaths(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null) return -1;
                int paths = 0;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null) paths += Math.Max(0, unit.regimentpaths);
                }

                return paths;
            }
            catch
            {
                return -1;
            }
        }

        private static int CountAttachedUnits(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null) return 0;
                int count = 0;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    if (group.allattachedunits[i] != null) count++;
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static int CountAttachedUnderCommander(Regiment group)
        {
            try
            {
                if (group == null || group.allattachedunits == null) return 0;
                int count = 0;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    Regiment unit = group.allattachedunits[i];
                    if (unit != null && unit.dlcw_isundercommander) count++;
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static Regiment SafeRegimentField(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as Regiment : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool SafeUseOrderDelays()
        {
            try
            {
                return GameVars.useorderdelays;
            }
            catch
            {
                return false;
            }
        }

        private static TacticalOrderDelivery DeliveryKind(string delivery)
        {
            if (delivery == "bugle") return TacticalOrderDelivery.Bugle;
            if (delivery == "courier") return TacticalOrderDelivery.Courier;
            if (delivery == "immediate") return TacticalOrderDelivery.Immediate;
            return TacticalOrderDelivery.Unknown;
        }

        private static int CountList(IList list)
        {
            return list == null ? 0 : list.Count;
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try
            {
                return obj != null ? obj.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
