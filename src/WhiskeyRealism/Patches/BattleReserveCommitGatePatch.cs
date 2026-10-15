using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #59 — Vanilla AIBattle.CheckUseOfReserves (decompile 6062, micro-AI task
    // slot 23, UpdateMicroAI 5579) directly issues RegimentSetPath for one reserve
    // support unit at 6170 (doublequick 6171). This patch snapshots the possible
    // support units before vanilla, then rolls back only newly-added paths when
    // TacticalReserveCommitGate denies: the calling command group resolves to a
    // Reserve role (ledger state first), unless the direct-child allocator already
    // released it or the unit is already engaged. The deleted B8 patch's doctrine
    // deny is the same rule (see TacticalReserveCommitGate). Vanilla picks the unit
    // itself (6121-6129); its PerformAIActionDLCWL (5101-5115) passes a W&L
    // under-commander unit whenever the group holds an AI stance, so it does not
    // keep player units out. The rollback skips the whole commit when any changed
    // unit fails TacticalWriteGuard (AnyNotWritable), so a player-owned unit is
    // never written.
    //
    // Cost: the Prefix mirrors vanilla's early exits first (6084 group gate,
    // 6093-6113 endangered-unit list) and only snapshots when a deny is
    // possible (Commander Mode Active, guard allows writes, role Reserve), and
    // then only the units 6121 could pick. Rollback is
    // skipped for units whose StopRegiment(manualstop) side effects
    // (RegimentSetPath newpath, 130836) cannot be undone: pontoon/obstacle
    // construction and pending re-forming.
    //
    // Local odds come from the side's battle snapshot only
    // (TacticalReserveCommitGate.LocalOdds: own strength bucket over fog-honest
    // slice pressure), never vanilla's groupenemiesinrange (122650) or
    // enemystrengthwithinangle, which count enemies the AI cannot see.
    //
    // Ordering: this Postfix runs at Priority.High, before #56
    // (TacticalReserveOrderDelayGuardPatch). A rollback leaves the unit's paths at
    // their pre-call count, so #56 sees no vanilla move and does nothing.
    [HarmonyPatch(typeof(AIBattle), "CheckUseOfReserves")]
    internal static class BattleReserveCommitGatePatch
    {
        private static readonly object TelemetryGate = new object();
        private static readonly Dictionary<string, float> _lastGateTelemetryAt = new Dictionary<string, float>();
        private static FieldInfo _lastDrawnPathCornerField;
        private static FieldInfo _firstWpAdjustmentMadeField;
        private static bool _lastDrawnPathCornerFieldMissing;
        private static bool _firstWpAdjustmentMadeFieldMissing;
        private const int WaypointPathSnapshotLimit = 75;
        private const int WaypointCornerSnapshotLimit = 200;

        internal sealed class ReserveCommitState
        {
            public CommandIntentResolution Resolution;
            public bool ReserveReleased;
            public UnitState[] Units = Array.Empty<UnitState>();
        }

        internal readonly struct UnitState
        {
            public UnitState(
                Regiment unit,
                int paths,
                int queueCount,
                bool doubleQuick,
                bool running,
                bool lieDown,
                bool inEngagement,
                int pathsBeforeRotation,
                Regiment unitWhoGaveLastMovingOrder,
                float coverValue,
                int coverObject,
                float coverValueTemp,
                int coverObjectTemp,
                float coverAngle,
                bool orderToCrossBridge,
                int[] blockObjectCurrentPath,
                int[] blockObjectCurrentCorner,
                bool distanceMarchColumn,
                int formation,
                int groupFormation,
                int formationOrdered,
                int lastFormation,
                Vector3 lastFormationChangePosition,
                int orderState,
                float timedMovement,
                float timeOfArrival,
                float aiArrivalFactor,
                GameObject unitToTarget,
                float lastFiredShotTime,
                int lastPathState,
                Vector3[] lastWaypointPosition,
                float[] lastWaypointRotation,
                Vector3 lastSetWaypointPosition,
                float lastSetWaypointRotation,
                float lastMovingUpdate,
                bool lastPathSetOutOfSafetyZone,
                bool lastPathSetOutOfManualMove,
                int priorMountingState,
                GameObject targetedEnemyUnit,
                Regiment targetedEnemyUnitReg,
                int targetedEnemyUnitType,
                bool reattachmentOrder,
                List<GameObject> gunObjectsToTake,
                WaypointAdjustmentState waypointAdjustment,
                WaypointAdjustmentState[] waypointBlockAdjustments,
                bool hasLastDrawnPathCorner,
                int lastDrawnPathCorner,
                bool hasFirstWpAdjustmentMade,
                bool firstWpAdjustmentMade,
                TransformState unitTransform,
                TransformState[] blockTransforms,
                bool rollbackSafe)
            {
                Unit = unit;
                RollbackSafe = rollbackSafe;
                Paths = Math.Max(0, paths);
                QueueCount = Math.Max(0, queueCount);
                DoubleQuick = doubleQuick;
                Running = running;
                LieDown = lieDown;
                InEngagement = inEngagement;
                PathsBeforeRotation = Math.Max(0, pathsBeforeRotation);
                UnitWhoGaveLastMovingOrder = unitWhoGaveLastMovingOrder;
                CoverValue = coverValue;
                CoverObject = coverObject;
                CoverValueTemp = coverValueTemp;
                CoverObjectTemp = coverObjectTemp;
                CoverAngle = coverAngle;
                OrderToCrossBridge = orderToCrossBridge;
                BlockObjectCurrentPath = Clone(blockObjectCurrentPath);
                BlockObjectCurrentCorner = Clone(blockObjectCurrentCorner);
                DistanceMarchColumn = distanceMarchColumn;
                Formation = formation;
                GroupFormation = groupFormation;
                FormationOrdered = formationOrdered;
                LastFormation = lastFormation;
                LastFormationChangePosition = lastFormationChangePosition;
                OrderState = orderState;
                TimedMovement = timedMovement;
                TimeOfArrival = timeOfArrival;
                AiArrivalFactor = aiArrivalFactor;
                UnitToTarget = unitToTarget;
                LastFiredShotTime = lastFiredShotTime;
                LastPathState = lastPathState;
                LastWaypointPosition = Clone(lastWaypointPosition);
                LastWaypointRotation = Clone(lastWaypointRotation);
                LastSetWaypointPosition = lastSetWaypointPosition;
                LastSetWaypointRotation = lastSetWaypointRotation;
                LastMovingUpdate = lastMovingUpdate;
                LastPathSetOutOfSafetyZone = lastPathSetOutOfSafetyZone;
                LastPathSetOutOfManualMove = lastPathSetOutOfManualMove;
                PriorMountingState = priorMountingState;
                TargetedEnemyUnit = targetedEnemyUnit;
                TargetedEnemyUnitReg = targetedEnemyUnitReg;
                TargetedEnemyUnitType = targetedEnemyUnitType;
                ReattachmentOrder = reattachmentOrder;
                GunObjectsToTake = Clone(gunObjectsToTake);
                WaypointAdjustment = waypointAdjustment;
                WaypointBlockAdjustments = Clone(waypointBlockAdjustments);
                HasLastDrawnPathCorner = hasLastDrawnPathCorner;
                LastDrawnPathCorner = lastDrawnPathCorner;
                HasFirstWpAdjustmentMade = hasFirstWpAdjustmentMade;
                FirstWpAdjustmentMade = firstWpAdjustmentMade;
                UnitTransform = unitTransform;
                BlockTransforms = Clone(blockTransforms);
            }

            public Regiment Unit { get; }
            public bool RollbackSafe { get; }
            public int Paths { get; }
            public int QueueCount { get; }
            public bool DoubleQuick { get; }
            public bool Running { get; }
            public bool LieDown { get; }
            public bool InEngagement { get; }
            public int PathsBeforeRotation { get; }
            public Regiment UnitWhoGaveLastMovingOrder { get; }
            public float CoverValue { get; }
            public int CoverObject { get; }
            public float CoverValueTemp { get; }
            public int CoverObjectTemp { get; }
            public float CoverAngle { get; }
            public bool OrderToCrossBridge { get; }
            public int[] BlockObjectCurrentPath { get; }
            public int[] BlockObjectCurrentCorner { get; }
            public bool DistanceMarchColumn { get; }
            public int Formation { get; }
            public int GroupFormation { get; }
            public int FormationOrdered { get; }
            public int LastFormation { get; }
            public Vector3 LastFormationChangePosition { get; }
            public int OrderState { get; }
            public float TimedMovement { get; }
            public float TimeOfArrival { get; }
            public float AiArrivalFactor { get; }
            public GameObject UnitToTarget { get; }
            public float LastFiredShotTime { get; }
            public int LastPathState { get; }
            public Vector3[] LastWaypointPosition { get; }
            public float[] LastWaypointRotation { get; }
            public Vector3 LastSetWaypointPosition { get; }
            public float LastSetWaypointRotation { get; }
            public float LastMovingUpdate { get; }
            public bool LastPathSetOutOfSafetyZone { get; }
            public bool LastPathSetOutOfManualMove { get; }
            public int PriorMountingState { get; }
            public GameObject TargetedEnemyUnit { get; }
            public Regiment TargetedEnemyUnitReg { get; }
            public int TargetedEnemyUnitType { get; }
            public bool ReattachmentOrder { get; }
            public List<GameObject> GunObjectsToTake { get; }
            public WaypointAdjustmentState WaypointAdjustment { get; }
            public WaypointAdjustmentState[] WaypointBlockAdjustments { get; }
            public bool HasLastDrawnPathCorner { get; }
            public int LastDrawnPathCorner { get; }
            public bool HasFirstWpAdjustmentMade { get; }
            public bool FirstWpAdjustmentMade { get; }
            public TransformState UnitTransform { get; }
            public TransformState[] BlockTransforms { get; }

            private static int[] Clone(int[] source)
            {
                if (source == null) return null;
                var clone = new int[source.Length];
                Array.Copy(source, clone, source.Length);
                return clone;
            }

            private static Vector3[] Clone(Vector3[] source)
            {
                if (source == null) return null;
                var clone = new Vector3[source.Length];
                Array.Copy(source, clone, source.Length);
                return clone;
            }

            private static float[] Clone(float[] source)
            {
                if (source == null) return null;
                var clone = new float[source.Length];
                Array.Copy(source, clone, source.Length);
                return clone;
            }

            private static List<GameObject> Clone(List<GameObject> source)
            {
                if (source == null) return null;
                return new List<GameObject>(source);
            }

            private static TransformState[] Clone(TransformState[] source)
            {
                if (source == null) return null;
                var clone = new TransformState[source.Length];
                Array.Copy(source, clone, source.Length);
                return clone;
            }

            private static WaypointAdjustmentState[] Clone(WaypointAdjustmentState[] source)
            {
                if (source == null) return null;
                var clone = new WaypointAdjustmentState[source.Length];
                Array.Copy(source, clone, source.Length);
                return clone;
            }
        }

        internal readonly struct TransformState
        {
            public TransformState(bool hasValue, Vector3 position, Quaternion rotation)
            {
                HasValue = hasValue;
                Position = position;
                Rotation = rotation;
            }

            public bool HasValue { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        internal readonly struct WaypointAdjustmentState
        {
            public WaypointAdjustmentState(bool hasValue, PathState[] paths)
            {
                HasValue = hasValue;
                Paths = paths;
            }

            public bool HasValue { get; }
            public PathState[] Paths { get; }
        }

        internal readonly struct PathState
        {
            public PathState(int pathIndex, Vector3[] corners)
            {
                PathIndex = Math.Max(0, pathIndex);
                Corners = Clone(corners);
            }

            public int PathIndex { get; }
            public Vector3[] Corners { get; }

            private static Vector3[] Clone(Vector3[] source)
            {
                if (source == null) return null;
                var clone = new Vector3[source.Length];
                Array.Copy(source, clone, source.Length);
                return clone;
            }
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment aigroup, out ReserveCommitState __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.reserve-commit-gate-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 5.0))
            {
                PatchFirstFire.Mark("tactical.patch.reserve-commit-gate-prefix");
                __state = null;
                try
                {
                    if (!Enabled()) return;
                    if (!VanillaMayCommitReserve(aigroup)) return;
                    if (!TacticalWriteGuard.CanWrite(aigroup)) return;

                    CommandIntentResolution resolution = ResolveIntent(aigroup, out CommandIntentResolution allocator);
                    if (!resolution.Found || resolution.Intent.Role != DirectChildRole.Reserve) return;

                    __state = new ReserveCommitState
                    {
                        Resolution = resolution,
                        ReserveReleased = TacticalReserveCommitGate.ReleasedByAllocator(resolution, allocator),
                        Units = SnapshotReserveCandidates(aigroup)
                    };
                }
                catch (Exception ex)
                {
                    __state = null;
                    OnceLog.Warning(
                        "tactical-reserve-commit-gate:prefix",
                        "[TacticalReserveCommitGate] Prefix failed; vanilla reserve movement remains active: " + ex.Message);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        internal static void Postfix(Regiment aigroup, ReserveCommitState __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.reserve-commit-gate", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("tactical.patch.reserve-commit-gate");
                try
                {
                    if (__state == null || __state.Units == null || aigroup == null) return;
                    if (!Enabled()) return;

                    UnitState[] changed = FindChangedUnits(__state);
                    if (changed.Length == 0) return;

                    var input = new TacticalReserveCommitGate.Input(
                        vanillaCommitted: true,
                        resolution: __state.Resolution,
                        playerControlled: TacticalWriteGuard.IsPlayerProtected(aigroup) || AnyNotWritable(changed),
                        committedUnitAlreadyEngaged: AnyAlreadyEngaged(changed),
                        ownStrengthRatio: OwnStrengthRatio(aigroup),
                        localOdds: LocalOdds(aigroup),
                        reserveReleased: __state.ReserveReleased);

                    TacticalReserveCommitGate.Decision decision = TacticalReserveCommitGate.Decide(input);
                    if (decision.Action == TacticalReserveCommitGate.Action.Deny)
                    {
                        if (!AllRollbackSafe(changed))
                        {
                            Log(aigroup, TacticalReserveCommitGate.Action.Observe, decision.Role, "rollback-unsafe-stopregiment", changed.Length);
                            return;
                        }

                        RollBackChangedUnits(changed);
                        LogProtectedReserveDrift(aigroup, __state, decision, changed.Length);
                    }

                    Log(aigroup, decision.Action, decision.Role, decision.Reason, changed.Length);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "tactical-reserve-commit-gate:postfix",
                        "[TacticalReserveCommitGate] Postfix failed; vanilla reserve movement remains active: " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null
                    && Plugin.Instance.Enabled != null
                    && Plugin.Instance.Enabled.Value
                    && Plugin.EnableTacticalBattleOrchestrator != null
                    && Plugin.EnableTacticalBattleOrchestrator.Value
                    && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate != null
                    && Plugin.Instance.EnableTacticalOrchestratorReserveCommitGate.Value;
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:enabled",
                    "[TacticalReserveCommitGate] Enable check failed; patch disabled: " + ex.Message);
                return false;
            }
        }

        // Vanilla's early exits in CheckUseOfReserves (6084-6113). False means
        // vanilla returns before issuing any reserve path this call.
        internal static bool VanillaMayCommitReserve(Regiment group)
        {
            try
            {
                if ((UnityEngine.Object)group == null) return false;
                if (!TacticalReserveCommitGate.GroupMayCommit(((Component)group).gameObject.activeInHierarchy, group.unittyp)) return false;
                Regiment[] attached = group.allattachedunits;
                if (attached == null) return false;

                for (int i = 0; i < attached.Length; i++)
                {
                    Regiment unit = attached[i];
                    if ((UnityEngine.Object)unit == null) continue;
                    if (BattleWriterPolicy.IsEndangeredForReserveSupport(
                            unit.unittyp, unit.regimentpaths, unit.isrouted, unit.outflanked, unit.ownonflank))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Snapshot only the units CheckUseOfReserves 6121 could send.
        private static UnitState[] SnapshotReserveCandidates(Regiment group)
        {
            Regiment[] attached = group != null ? group.allattachedunits : null;
            if (attached == null) return Array.Empty<UnitState>();

            List<UnitState> units = null;
            for (int i = 0; i < attached.Length; i++)
            {
                Regiment unit = attached[i];
                try
                {
                    if ((UnityEngine.Object)unit == null) continue;
                    if (!BattleWriterPolicy.IsReserveSupportCandidate(
                            unit.unittyp, unit.regimentpaths, unit.isrouted, unit.outflanked, unit.inengagement))
                        continue;

                    if (units == null) units = new List<UnitState>();
                    units.Add(SnapshotUnit(unit));
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "tactical-reserve-commit-gate:snapshot-unit",
                        "[TacticalReserveCommitGate] Could not snapshot one reserve unit; unit skipped: " + ex.Message);
                }
            }

            return units != null ? units.ToArray() : Array.Empty<UnitState>();
        }

        private static UnitState SnapshotUnit(Regiment unit)
        {
            if ((UnityEngine.Object)unit == null)
                return default(UnitState);

            int lastDrawnPathCorner = 0;
            bool hasLastDrawnPathCorner = TryGetPrivateInt(unit, ref _lastDrawnPathCornerField, "lastdrawnpathcorner", ref _lastDrawnPathCornerFieldMissing, out lastDrawnPathCorner);
            bool firstWpAdjustmentMade = false;
            bool hasFirstWpAdjustmentMade = TryGetPrivateBool(unit, ref _firstWpAdjustmentMadeField, "firstwpadjustmentmade", ref _firstWpAdjustmentMadeFieldMissing, out firstWpAdjustmentMade);

            return new UnitState(
                unit,
                SafePathCount(unit),
                SafeQueueCount(unit),
                unit.doublequick,
                unit.running,
                unit.liedown,
                unit.inengagement,
                unit.regimentpathsbeforerotation,
                unit.unitwhogavelastmovingorder,
                unit.covervalue,
                unit.coverobject,
                unit.covervaluetemp,
                unit.coverobjecttemp,
                unit.coverangle,
                unit.ordertocrossbridge,
                unit.blockobjectcurrentpath,
                unit.blockobjectcurrentcorner,
                unit.distancemarchcolumn,
                unit.formation,
                unit.groupformation,
                unit.formationordered,
                unit.lastformation,
                unit.lastformationchangeposition,
                unit.orderstate,
                unit.timedmovement,
                unit.timeofarrival,
                unit.aiarrivalfactor,
                unit.unittotarget,
                unit.lastfiredshottime,
                unit.lastpathstate,
                unit.lastwaypointposition,
                unit.lastwaypointrotation,
                unit.lastsetwaypointposition,
                unit.lastsetwaypointrotation,
                unit.lastmovingupdate,
                unit.lastpathsetoutofsafetyzone,
                unit.lastpathsetoutofmanualmove,
                unit.priormountingstate,
                unit.targetedenemyunit,
                unit.targetedenemyunitreg,
                unit.targetedenemyunittype,
                unit.reattachmentorder,
                SnapshotGameObjectList(unit.gunobjectstotake),
                SnapshotWaypointAdjustment(unit.waypointadjustment, SafeWaypointPathLimit(unit)),
                SnapshotWaypointBlockAdjustments(unit),
                hasLastDrawnPathCorner,
                lastDrawnPathCorner,
                hasFirstWpAdjustmentMade,
                firstWpAdjustmentMade,
                SnapshotTransform(unit),
                SnapshotBlockTransforms(unit),
                BattleWriterPolicy.ReserveRollbackIsSafe(
                    (UnityEngine.Object)unit.pontoonconstruction != null,
                    (UnityEngine.Object)unit.obstacleconstruction != null,
                    unit.formation,
                    unit.lastformation));
        }

        private static bool AllRollbackSafe(UnitState[] changed)
        {
            for (int i = 0; i < changed.Length; i++)
                if (!changed[i].RollbackSafe) return false;
            return true;
        }

        private static UnitState[] FindChangedUnits(ReserveCommitState state)
        {
            var changed = new List<UnitState>();
            bool useOrderDelays = SafeUseOrderDelays();
            for (int i = 0; i < state.Units.Length; i++)
            {
                UnitState before = state.Units[i];
                Regiment unit = before.Unit;
                if (unit == null) continue;

                if (TacticalReserveCommitGate.VanillaCommittedUnit(
                        before.Paths, SafePathCount(unit), before.QueueCount, SafeQueueCount(unit), useOrderDelays))
                    changed.Add(before);
            }

            return changed.ToArray();
        }

        private static void RollBackChangedUnits(UnitState[] changed)
        {
            for (int i = 0; i < changed.Length; i++)
            {
                UnitState before = changed[i];
                Regiment unit = before.Unit;
                if (unit == null) continue;

                RemoveAddedPaths(unit, before.Paths, SafePathCount(unit));
                RestoreMovementState(unit, before);
            }
        }

        private static void RestoreMovementState(Regiment unit, UnitState before)
        {
            try { unit.regimentpathsbeforerotation = before.PathsBeforeRotation; } catch { }
            try { unit.unitwhogavelastmovingorder = before.UnitWhoGaveLastMovingOrder; } catch { }
            try { unit.covervalue = before.CoverValue; } catch { }
            try { unit.coverobject = before.CoverObject; } catch { }
            try { unit.covervaluetemp = before.CoverValueTemp; } catch { }
            try { unit.coverobjecttemp = before.CoverObjectTemp; } catch { }
            try { unit.coverangle = before.CoverAngle; } catch { }
            try { unit.ordertocrossbridge = before.OrderToCrossBridge; } catch { }
            try { RestoreIntArray(ref unit.blockobjectcurrentpath, before.BlockObjectCurrentPath); } catch { }
            try { RestoreIntArray(ref unit.blockobjectcurrentcorner, before.BlockObjectCurrentCorner); } catch { }
            try { unit.distancemarchcolumn = before.DistanceMarchColumn; } catch { }
            try { unit.formation = before.Formation; } catch { }
            try { unit.groupformation = before.GroupFormation; } catch { }
            try { unit.formationordered = before.FormationOrdered; } catch { }
            try { unit.lastformation = before.LastFormation; } catch { }
            try { unit.lastformationchangeposition = before.LastFormationChangePosition; } catch { }
            try { unit.orderstate = before.OrderState; } catch { }
            try { unit.timedmovement = before.TimedMovement; } catch { }
            try { unit.timeofarrival = before.TimeOfArrival; } catch { }
            try { unit.aiarrivalfactor = before.AiArrivalFactor; } catch { }
            try { unit.unittotarget = before.UnitToTarget; } catch { }
            try { unit.lastfiredshottime = before.LastFiredShotTime; } catch { }
            try { unit.lastpathstate = before.LastPathState; } catch { }
            try { RestoreVectorArray(ref unit.lastwaypointposition, before.LastWaypointPosition); } catch { }
            try { RestoreFloatArray(ref unit.lastwaypointrotation, before.LastWaypointRotation); } catch { }
            try { unit.lastsetwaypointposition = before.LastSetWaypointPosition; } catch { }
            try { unit.lastsetwaypointrotation = before.LastSetWaypointRotation; } catch { }
            try { unit.lastmovingupdate = before.LastMovingUpdate; } catch { }
            try { unit.lastpathsetoutofsafetyzone = before.LastPathSetOutOfSafetyZone; } catch { }
            try { unit.lastpathsetoutofmanualmove = before.LastPathSetOutOfManualMove; } catch { }
            try { unit.priormountingstate = before.PriorMountingState; } catch { }
            try { unit.targetedenemyunit = before.TargetedEnemyUnit; } catch { }
            try { unit.targetedenemyunitreg = before.TargetedEnemyUnitReg; } catch { }
            try { unit.targetedenemyunittype = before.TargetedEnemyUnitType; } catch { }
            try { unit.reattachmentorder = before.ReattachmentOrder; } catch { }
            try { RestoreGameObjectList(ref unit.gunobjectstotake, before.GunObjectsToTake); } catch { }
            RestoreWaypointAdjustment(unit.waypointadjustment, before.WaypointAdjustment);
            RestoreWaypointBlockAdjustments(unit, before.WaypointBlockAdjustments);
            try { unit.doublequick = before.DoubleQuick; } catch { }
            try { unit.running = before.Running; } catch { }
            try { unit.liedown = before.LieDown; } catch { }
            if (before.HasLastDrawnPathCorner)
                TrySetPrivateInt(unit, ref _lastDrawnPathCornerField, "lastdrawnpathcorner", ref _lastDrawnPathCornerFieldMissing, before.LastDrawnPathCorner);
            if (before.HasFirstWpAdjustmentMade)
                TrySetPrivateBool(unit, ref _firstWpAdjustmentMadeField, "firstwpadjustmentmade", ref _firstWpAdjustmentMadeFieldMissing, before.FirstWpAdjustmentMade);
            RestoreTransform(unit, before.UnitTransform);
            RestoreBlockTransforms(unit, before.BlockTransforms);
        }

        private static void RestoreIntArray(ref int[] target, int[] snapshot)
        {
            if (snapshot == null)
            {
                target = null;
                return;
            }

            if (target == null || target.Length != snapshot.Length)
                target = new int[snapshot.Length];

            int max = Math.Min(target.Length, snapshot.Length);
            for (int i = 0; i < max; i++)
                target[i] = snapshot[i];
        }

        private static void RestoreVectorArray(ref Vector3[] target, Vector3[] snapshot)
        {
            if (snapshot == null)
            {
                target = null;
                return;
            }

            if (target == null || target.Length != snapshot.Length)
                target = new Vector3[snapshot.Length];

            int max = Math.Min(target.Length, snapshot.Length);
            for (int i = 0; i < max; i++)
                target[i] = snapshot[i];
        }

        private static void RestoreFloatArray(ref float[] target, float[] snapshot)
        {
            if (snapshot == null)
            {
                target = null;
                return;
            }

            if (target == null || target.Length != snapshot.Length)
                target = new float[snapshot.Length];

            int max = Math.Min(target.Length, snapshot.Length);
            for (int i = 0; i < max; i++)
                target[i] = snapshot[i];
        }

        private static List<GameObject> SnapshotGameObjectList(List<GameObject> source)
        {
            try { return source != null ? new List<GameObject>(source) : null; }
            catch { return null; }
        }

        private static void RestoreGameObjectList(ref List<GameObject> target, List<GameObject> snapshot)
        {
            if (target == null)
                target = new List<GameObject>();

            target.Clear();
            if (snapshot == null) return;

            for (int i = 0; i < snapshot.Count; i++)
                target.Add(snapshot[i]);
        }

        private static int SafeWaypointPathLimit(Regiment unit)
        {
            int limit = 0;
            try
            {
                if (unit != null && unit.regimentpath != null)
                    limit = Math.Max(limit, unit.regimentpath.Length);
            }
            catch { }

            try
            {
                if (unit != null && unit.pathstatus != null)
                    limit = Math.Max(limit, unit.pathstatus.Length);
            }
            catch { }

            limit = Math.Max(limit, SafePathCount(unit));
            if (limit <= 0) return WaypointPathSnapshotLimit;
            return Math.Min(WaypointPathSnapshotLimit, limit);
        }

        private static WaypointAdjustmentState SnapshotWaypointAdjustment(Regiment.WaypointAdjustment adjustment, int pathLimit)
        {
            try
            {
                if (adjustment == null)
                    return default(WaypointAdjustmentState);

                int safePathLimit = Math.Min(WaypointPathSnapshotLimit, Math.Max(0, pathLimit));
                var paths = new List<PathState>();
                for (int path = 0; path < safePathLimit; path++)
                {
                    int corners = SafeCornerLength(adjustment, path);
                    if (corners <= 0) continue;

                    int safeCorners = Math.Min(WaypointCornerSnapshotLimit, corners);
                    var values = new Vector3[safeCorners];
                    for (int corner = 0; corner < safeCorners; corner++)
                        values[corner] = SafePathCorner(adjustment, path, corner);

                    paths.Add(new PathState(path, values));
                }

                return new WaypointAdjustmentState(true, paths.ToArray());
            }
            catch
            {
                return default(WaypointAdjustmentState);
            }
        }

        private static WaypointAdjustmentState[] SnapshotWaypointBlockAdjustments(Regiment unit)
        {
            try
            {
                if (unit == null || unit.waypointblockadjustment == null)
                    return null;

                int pathLimit = SafeWaypointPathLimit(unit);
                var snapshots = new WaypointAdjustmentState[unit.waypointblockadjustment.Count];
                for (int i = 0; i < snapshots.Length; i++)
                    snapshots[i] = SnapshotWaypointAdjustment(unit.waypointblockadjustment[i], pathLimit);
                return snapshots;
            }
            catch
            {
                return null;
            }
        }

        private static void RestoreWaypointAdjustment(Regiment.WaypointAdjustment target, WaypointAdjustmentState snapshot)
        {
            try
            {
                if (target == null || !snapshot.HasValue) return;

                target.ClearAllValues();
                if (snapshot.Paths == null) return;

                for (int i = 0; i < snapshot.Paths.Length; i++)
                {
                    PathState path = snapshot.Paths[i];
                    if (path.Corners == null) continue;

                    for (int corner = 0; corner < path.Corners.Length; corner++)
                        target.SetPathCorner(path.PathIndex, corner, path.Corners[corner]);
                }
            }
            catch { }
        }

        private static void RestoreWaypointBlockAdjustments(Regiment unit, WaypointAdjustmentState[] snapshots)
        {
            try
            {
                if (unit == null || snapshots == null) return;
                if (unit.waypointblockadjustment == null)
                    unit.waypointblockadjustment = new List<Regiment.WaypointAdjustment>();

                for (int i = 0; i < snapshots.Length; i++)
                {
                    while (unit.waypointblockadjustment.Count <= i)
                        unit.waypointblockadjustment.Add(new Regiment.WaypointAdjustment());

                    RestoreWaypointAdjustment(unit.waypointblockadjustment[i], snapshots[i]);
                }
            }
            catch { }
        }

        private static int SafeCornerLength(Regiment.WaypointAdjustment adjustment, int path)
        {
            try { return adjustment != null ? Math.Max(0, adjustment.GetCornerLength(path)) : 0; }
            catch { return 0; }
        }

        private static Vector3 SafePathCorner(Regiment.WaypointAdjustment adjustment, int path, int corner)
        {
            try { return adjustment != null ? adjustment.GetPathCorner(path, corner) : default(Vector3); }
            catch { return default(Vector3); }
        }

        // Ledger command state first, then the command-node intent; allocator is
        // always the command-node intent (the direct-child allocator's current view).
        private static CommandIntentResolution ResolveIntent(Regiment group, out CommandIntentResolution allocator)
        {
            allocator = new CommandIntentResolution(false, default, "unresolved");
            try
            {
                if (group == null)
                    return new CommandIntentResolution(false, default, "no-group");

                TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(group.alliance);
                if (side == null || side.Army == null)
                    return new CommandIntentResolution(false, default, "no-side-orchestrator");

                int componentInstanceId = TacticalPatchIds.ComponentInstanceId(group);
                int gameObjectInstanceId = TacticalPatchIds.GameObjectInstanceId(group);
                allocator = side.Army.ResolveCommandIntentForGroup(componentInstanceId, gameObjectInstanceId);
                return TacticalReserveCommitGate.HeldResolution(
                    side.Army.CurrentCommandOperations, gameObjectInstanceId, componentInstanceId, allocator);
            }
            catch (Exception ex)
            {
                return new CommandIntentResolution(false, default, "resolve-error:" + ex.GetType().Name);
            }
        }

        // A changed unit Whiskey may not write (W&L player-owned, feud): the whole
        // rollback is skipped, as for a player-protected group.
        private static bool AnyNotWritable(UnitState[] changed)
        {
            for (int i = 0; i < changed.Length; i++)
            {
                Regiment unit = changed[i].Unit;
                if (unit != null && !TacticalWriteGuard.CanWrite(unit)) return true;
            }
            return false;
        }

        private static bool AnyAlreadyEngaged(UnitState[] changed)
        {
            for (int i = 0; i < changed.Length; i++)
            {
                Regiment unit = changed[i].Unit;
                if (unit != null && (changed[i].InEngagement || SafeInEngagement(unit)))
                    return true;
            }

            return false;
        }

        private static float OwnStrengthRatio(Regiment group)
        {
            try
            {
                if (group == null) return 1f;
                float active = Sanitize(group.groupstrengthactive);
                float total = Math.Max(1f, Sanitize(group.groupstrength));
                return active / total;
            }
            catch
            {
                return 1f;
            }
        }

        // Snapshot-only odds (fog-honest); no snapshot or the group not in it: 1.
        private static float LocalOdds(Regiment group)
        {
            try
            {
                if (group == null || !BattleCore.TryGetSnapshot(group.alliance, out BattleSnapshot snapshot)) return 1f;
                return TacticalReserveCommitGate.LocalOdds(snapshot, snapshot.IndexOfOwn(TacticalPatchIds.GameObjectInstanceId(group)));
            }
            catch
            {
                return 1f;
            }
        }

        internal static void RemoveAddedPaths(Regiment unit, int before, int after)
        {
            int safeBefore = Math.Max(0, before);
            int safeAfter = Math.Max(safeBefore, after);
            if (unit.regimentpath != null)
            {
                int max = Math.Min(safeAfter, unit.regimentpath.Length);
                for (int i = safeBefore; i < max; i++)
                    unit.regimentpath[i] = new NavMeshPath();
            }

            if (unit.pathstatus != null)
            {
                int max = Math.Min(safeAfter, unit.pathstatus.Length);
                for (int i = safeBefore; i < max; i++)
                    unit.pathstatus[i] = 0;
            }

            unit.regimentpaths = safeBefore;
        }

        private static int SafePathCount(Regiment unit)
        {
            try { return unit != null ? Math.Max(0, unit.regimentpaths) : 0; }
            catch { return 0; }
        }

        private static int SafeQueueCount(Regiment unit)
        {
            try { return unit != null && unit.orderqueue != null ? unit.orderqueue.Count : 0; }
            catch { return 0; }
        }

        private static bool SafeUseOrderDelays()
        {
            try { return GameVars.useorderdelays; }
            catch { return false; }
        }

        private static bool SafeInEngagement(Regiment unit)
        {
            try { return unit != null && unit.inengagement; }
            catch { return false; }
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static TransformState SnapshotTransform(Regiment unit)
        {
            try
            {
                if (unit == null) return default(TransformState);
                Transform transform = unit.transform;
                if (transform == null) return default(TransformState);
                return new TransformState(true, transform.position, transform.rotation);
            }
            catch
            {
                return default(TransformState);
            }
        }

        private static TransformState SnapshotTransform(GameObject gameObject)
        {
            try
            {
                if (gameObject == null) return default(TransformState);
                Transform transform = gameObject.transform;
                if (transform == null) return default(TransformState);
                return new TransformState(true, transform.position, transform.rotation);
            }
            catch
            {
                return default(TransformState);
            }
        }

        private static TransformState[] SnapshotBlockTransforms(Regiment unit)
        {
            try
            {
                if (unit == null || unit.blockobject == null) return null;
                var snapshots = new TransformState[unit.blockobject.Length];
                for (int i = 0; i < snapshots.Length; i++)
                    snapshots[i] = SnapshotTransform(unit.blockobject[i]);
                return snapshots;
            }
            catch
            {
                return null;
            }
        }

        private static void RestoreTransform(Regiment unit, TransformState snapshot)
        {
            try
            {
                if (!snapshot.HasValue || unit == null) return;
                Transform transform = unit.transform;
                if (transform == null) return;
                transform.position = snapshot.Position;
                transform.rotation = snapshot.Rotation;
            }
            catch { }
        }

        private static void RestoreTransform(GameObject gameObject, TransformState snapshot)
        {
            try
            {
                if (!snapshot.HasValue || gameObject == null) return;
                Transform transform = gameObject.transform;
                if (transform == null) return;
                transform.position = snapshot.Position;
                transform.rotation = snapshot.Rotation;
            }
            catch { }
        }

        private static void RestoreBlockTransforms(Regiment unit, TransformState[] snapshots)
        {
            try
            {
                if (unit == null || snapshots == null || unit.blockobject == null) return;
                int max = Math.Min(unit.blockobject.Length, snapshots.Length);
                for (int i = 0; i < max; i++)
                    RestoreTransform(unit.blockobject[i], snapshots[i]);
            }
            catch { }
        }

        private static bool TryGetPrivateInt(
            Regiment unit,
            ref FieldInfo field,
            string fieldName,
            ref bool missingLogged,
            out int value)
        {
            value = 0;
            try
            {
                field = ResolvePrivateField(ref field, fieldName, ref missingLogged);
                if (field == null) return false;
                object raw = field.GetValue(unit);
                if (raw is int intValue)
                {
                    value = intValue;
                    return true;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:read-private",
                    "[TacticalReserveCommitGate] failed reading Regiment." + fieldName + ": " + ex.Message);
            }

            return false;
        }

        private static bool TryGetPrivateBool(
            Regiment unit,
            ref FieldInfo field,
            string fieldName,
            ref bool missingLogged,
            out bool value)
        {
            value = false;
            try
            {
                field = ResolvePrivateField(ref field, fieldName, ref missingLogged);
                if (field == null) return false;
                object raw = field.GetValue(unit);
                if (raw is bool boolValue)
                {
                    value = boolValue;
                    return true;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:read-private",
                    "[TacticalReserveCommitGate] failed reading Regiment." + fieldName + ": " + ex.Message);
            }

            return false;
        }

        private static void TrySetPrivateInt(
            Regiment unit,
            ref FieldInfo field,
            string fieldName,
            ref bool missingLogged,
            int value)
        {
            try
            {
                field = ResolvePrivateField(ref field, fieldName, ref missingLogged);
                if (field != null) field.SetValue(unit, value);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:write-private",
                    "[TacticalReserveCommitGate] failed restoring Regiment." + fieldName + ": " + ex.Message);
            }
        }

        private static void TrySetPrivateBool(
            Regiment unit,
            ref FieldInfo field,
            string fieldName,
            ref bool missingLogged,
            bool value)
        {
            try
            {
                field = ResolvePrivateField(ref field, fieldName, ref missingLogged);
                if (field != null) field.SetValue(unit, value);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:write-private",
                    "[TacticalReserveCommitGate] failed restoring Regiment." + fieldName + ": " + ex.Message);
            }
        }

        private static FieldInfo ResolvePrivateField(ref FieldInfo field, string fieldName, ref bool missingLogged)
        {
            if (field != null) return field;
            if (missingLogged) return null;

            field = AccessTools.Field(typeof(Regiment), fieldName);
            if (field == null)
            {
                missingLogged = true;
                OnceLog.Warning(
                    "tactical-reserve-commit-gate:missing-private",
                    "[TacticalReserveCommitGate] missing Regiment." + fieldName + " anchor; restore will skip that field.");
            }

            return field;
        }

        private static void Log(
            Regiment group,
            TacticalReserveCommitGate.Action action,
            DirectChildRole role,
            string reason,
            int changedUnits)
        {
            try
            {
                string groupName = SafeGroupName(group);
                string safeReason = TacticalCurrentOrderSignature.Safe(reason);
                string key = action + ":" + role + ":" + safeReason;
                if (!ShouldEmitGateTelemetry(key))
                    return;

                TelemetryRouter.Emit(
                    TelemetryLayer.Tactical,
                    TelemetryCategory.Gate,
                    "TacticalReserveCommitGate",
                    TelemetrySeverity.Info,
                    ev => ev
                        .WithUnit(groupName)
                        .WithDecision(action.ToString(), safeReason, key)
                        .WithField("confidence", 1.0)
                        .WithField("score", Math.Max(0, changedUnits))
                        .WithField("selectedTarget", groupName)
                        .WithField("gateResult", action == TacticalReserveCommitGate.Action.Deny ? "deny" : "observe")
                        .WithField("gateReason", safeReason)
                        .WithField("writeAction", "CheckUseOfReserves")
                        .WithField("writeResult", action == TacticalReserveCommitGate.Action.Deny ? "restored" : "observed")
                        .WithField("group", groupName)
                        .WithField("role", role.ToString())
                        .WithField("changedUnits", Math.Max(0, changedUnits)));
            }
            catch { }
        }

        // Bounded: one row per action/role/reason signature per 30 s real time
        // (telemetry throttle only; no doctrine timing).
        private static bool ShouldEmitGateTelemetry(string signature)
        {
            try
            {
                lock (TelemetryGate)
                {
                    return TacticalTelemetry.ShouldEmit(
                        _lastGateTelemetryAt, "reserve-commit-gate", signature, Time.realtimeSinceStartup, 30f, false);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void LogProtectedReserveDrift(
            Regiment group,
            ReserveCommitState state,
            TacticalReserveCommitGate.Decision decision,
            int changedUnits)
        {
            try
            {
                OnceLog.Info(
                    "tactical-reserve-drift:commit-gate",
                    "[TacticalReserveDrift] surface=CheckUseOfReserves group=" + SafeGroupName(group)
                    + " action=" + decision.Action
                    + " role=" + decision.Role
                    + " reason=" + TacticalCurrentOrderSignature.Safe(decision.Reason)
                    + " assignment=" + TacticalCurrentOrderSignature.Safe(state.Resolution.Reason)
                    + " changedUnits=" + Math.Max(0, changedUnits));
            }
            catch { }
        }

        private static string SafeGroupName(Regiment group)
        {
            try { return group != null ? TacticalCurrentOrderSignature.Safe(group.name) : "-"; }
            catch { return "-"; }
        }
    }
}
