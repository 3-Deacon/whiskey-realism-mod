using WhiskeyRealism.Tactical.PlayerOrders;
using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.Battle.Producers;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // The vanilla calls behind OrderExecutor (plan BC4 step 7, steps 8-9
    // bookkeeping). Runtime only; excluded from the test csproj. Every call
    // re-checks TacticalWriteGuard.CanWrite(Regiment) on the live unit, which
    // BattleSnapshotBuilder.UnitAt resolves from the snapshot the plan read.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   Move: BattleUnits.SetWaypoint (91232-91640) with showmovementoptions:false
    //     (skips the W&L control exit 91300 and the order-arrow UI) and
    //     useorderdelay from the plan; groups go on to SetGroupFormation (91559),
    //     regiments to RegimentSetPath (91540). Under useorderdelay the move is
    //     also queued with AddToOrderQueue (91483-91510), but the path is added in
    //     the same call (AddPath 130519, pathstatus 0 until the courier arrives,
    //     131243-131246), so the moving-unit count is the "issued" signal.
    //   Move with IntentFlags.AvoidCoveredCrossings: new AIBattle.BlockedMovingOrder
    //     (3054-3150) with the crossings and fire points the side's PERCEIVED enemy covers
    //     (CoveredCrossings: vanilla's UpdateBlockedCrossings rules 7917-7973 over the
    //     snapshot's observations, not its omniscient unit scan) and vanilla's
    //     150 m range, as vanilla's line-group move does (6953-6956). It carves
    //     NavMeshObstacles over the covered crossings away from the destination;
    //     UpdateOrders (3382, every AIBattle.Update frame, before the pause
    //     return) calls SetWaypoint(useorderdelay:false, manualfinalrotation -1)
    //     two frames later and removes them. The next arbiter tick (one
    //     sidestatupdatecycle, 0.015 game-hours, 84572) is many frames on, so D6
    //     sees the move through the waypoint stamp (SetGroupFormation 92067) or
    //     the moving-unit count; a same-clock re-run (vanilla's override calls)
    //     leaves it pending (OrderExecutor.ObserveOutcomes).
    //     Without a perceived covered point (an empty list) the move is a plain
    //     SetWaypoint with its courier delay (OrderExecutor.TakesBlockedMovingOrder).
    //   Move with IntentFlags.ImmediatePlacement (#60 deployment, BC10): SetGroupFormation(
    //     immediateplacement: true, newpath: true, ignoredeplyomentzone: false) at the line
    //     front, then the cleanup vanilla's own pass ends with - lastsetwaypointposition and
    //     immediateunitplacement cleared on the placed tree (85873-85883). No courier, no
    //     waypoint: the outcome is the group's position at the next observation.
    //   Stance: Util/TacticalStanceWriter (the one ChangeStance caller, 90772).
    //   Formation: BattleUnits.SetFormation (90971, courier) only when
    //     CommandFormationWritePolicy allows it — vanilla re-forms only a settled,
    //     non-moving group out of contact and cover (AdjustGroupFormations 5886).
    //   Facing: Regiment.RotateRegiment in place in contact, or
    //     SetGroupFormation(modifylastwaypoint, newpath:false) wheel out of contact
    //     (91822-92110; skips ClearTargets, re-paths children to rotated slots).
    //   CombatBehavior: Util/TacticalCombatBehaviorWriter (ChangeCombatBehavior
    //     90944-90966), per regiment for PerRegiment intents (D10); a charge Disarm
    //     (IntentFlags.LeaveChargeMode) also leaves charge mode 3 with
    //     Regiment.SetMovementMode(0) (124704; vanilla's refusal does the same, 122290).
    //   Stop: PerformActionForGroupForAllUnitsOnLevel(go, -1, "StopRegiment",
    //     StopOrder(true, true, false, false, true), sameaigrouponly:true) — the
    //     vanilla manual stop (117198).
    internal static class OrderExecutorRuntime
    {
        static OrderExecutorRuntime()
        {
            BattleScope.Register("order-executor-runtime", Reset);
            BattleScope.Register("order-executor-bombard-rows", () => _lastBombardRowAt.Clear());
        }

        // Step 1 over the snapshot flags: Commander Mode Active, not PlayerOwned,
        // not feuding (OrderExecutor exempts FeudReplay from the Feud refusal).
        private sealed class SnapshotOrderGuard : IOrderGuard
        {
            public bool CanWrite(in UnitRecord unit, out SkipReason reason)
            {
                return TacticalWriteGuard.CanWrite(unit, out reason);
            }
        }

        private const float FacingToleranceDegrees = OrderExecutor.FacingToleranceDegrees;

        private static readonly IOrderGuard Guard = new SnapshotOrderGuard();
        private static readonly Regiment.StopOrder ManualStop =
            new Regiment.StopOrder(_lastwaypointrotation: true, _manualstop: true, _skipmarchcolumnstop: false,
                _skipadvancefallbackstops: false, _clearregimentpathsbeforerotation: true);

        // Scratch for the in-place rotation walk (grown to the roster).
        private static int[] _rotationQueue = new int[64];
        private static int[] _rotationTargets = new int[64];
        private static float[] _rotationHeadings = new float[64];
        // The rotations the in-combat refuse ordered, to leave a regiment vanilla turned back alone for a while.
        private static readonly RefuseTurnBackMemory _refuseTurnBacks = new RefuseTurnBackMemory();

        // Set by the vanilla calls that re-lay a group's children (SetGroupFormation
        // directly or via SetWaypoint / SetFormation) for the target being issued;
        // Execute hands it to OrderExecutor.NoteChildLayout.
        private static bool _layoutTouched;
        private static RefuseFlank _layoutRefuse;

        // The perceived covered points per side (CoveredCrossings), rebuilt at most once per
        // snapshot (TickId) and only when a move carries AvoidCoveredCrossings. Pooled.
        // The buffers start at 64 points and grow to the ceiling (a long battle line of standing enemies
        // in cover can list hundreds); a list the ceiling cuts is logged once.
        private const int CoveredPointCapacity = 64;
        private const int CoveredPointCeiling = 1024;
        private static readonly float[][] _coveredX = { new float[CoveredPointCapacity], new float[CoveredPointCapacity] };
        private static readonly float[][] _coveredZ = { new float[CoveredPointCapacity], new float[CoveredPointCapacity] };
        private static readonly List<Vector3>[] _covered = { new List<Vector3>(CoveredPointCapacity), new List<Vector3>(CoveredPointCapacity) };
        private static readonly long[] _coveredTick = { -1L, -1L };

        // The units one Move reached (grown to the largest chain seen).
        private static IssuedTarget[] _issued = new IssuedTarget[64];
        // Pre-call waypoints of those units and their descendants (grown as needed).
        private static UnitWaypoint[] _waypointsBefore = new UnitWaypoint[256];
        // Per-battle latch for the first-fire line, so the message string is built
        // once per battle (its OnceLog key re-arms at battle end too).
        private static bool _firstFireLogged;
        private static bool _coveredLogged;
        private static bool _refuseLogged;

        /// <summary>Per-tick seams and settings for an executor.</summary>
        internal static void BindForTick(OrderExecutor executor)
        {
            executor.Bind(Guard, WaypointValidatorRuntime.Instance);
            executor.Crossings = Plugin.CrossingMoveGateEnabled() ? RiverCrossingGateRuntime.Instance : null;
            WaypointValidatorRuntime.TryMapSize(out float sizeX, out float sizeZ);
            executor.SetMapSize(sizeX, sizeZ);
            float holdSeconds = Plugin.BrigadeTaskHoldSeconds != null ? Plugin.BrigadeTaskHoldSeconds.Value : 12f;
            executor.TaskHoldHours = TacticalBattleClock.RealSecondsAtOneXToHours(holdSeconds);
        }

        /// <summary>
        /// Issues a planned winner to every chain target and records it. Returns the
        /// number of targets the vanilla call reached. The snapshot must be the
        /// alliance's latest build (BattleSnapshotBuilder.UnitAt).
        /// </summary>
        internal static int Execute(
            BattleUnits bunits,
            BattleSnapshot view,
            OrderExecutor executor,
            in BattleOrderIntent winner,
            in ExecutionPlan plan,
            double nowHours)
        {
            if (bunits == null || view == null || !plan.Execute) return 0;
            UnitRecord[] own = view.Own;
            int groupIndex = view.IndexOfOwn(winner.GroupId);
            if (groupIndex < 0) return 0;

            bool feudReplay = winner.Source == IntentSource.FeudReplay;
            int executed = 0;
            int issued = 0;
            int waypoints = 0;
            int pathsBefore = 0, pathsAfter = 0;
            bool viaBlockedOrder = false;
            // The queued moves this dispatch accompanies: the one carried out last carries the player's order.
            var carrier = default(WlMoveCarrier);
            if (_issued.Length < plan.Targets.Count) _issued = new IssuedTarget[Math.Max(plan.Targets.Count, _issued.Length * 2)];
            float sizeX = 0f, sizeZ = 0f;
            if (plan.Targets.FannedOut) WaypointValidatorRuntime.TryMapSize(out sizeX, out sizeZ);
            for (int t = 0; t < plan.Targets.Count; t++)
            {
                int targetIndex = plan.Targets.TargetIndices[t];
                if (targetIndex < 0 || targetIndex >= view.OwnCount) continue;
                Regiment unit = BattleSnapshotBuilder.UnitAt(view, targetIndex, enemy: false);
                if ((UnityEngine.Object)unit == null || !TacticalWriteGuard.CanWrite(unit, feudReplay)) continue;

                float x = plan.X;
                float z = plan.Z;
                if (plan.Targets.FannedOut)
                {
                    OrderExecutor.OffsetTarget(own[groupIndex], own[targetIndex], plan.X, plan.Z, sizeX, sizeZ, out x, out z);
                }

                bool wrote;
                _layoutTouched = false;
                if (OrderExecutor.IsImmediatePlacement(winner))
                {
                    // A deployment placement: one SetGroupFormation, judged by where the group
                    // stands (the executor compares the placed headquarters, not a waypoint).
                    wrote = IssueImmediatePlacement(bunits, unit, winner, x, z, out float hqX, out float hqZ);
                    if (wrote) _issued[issued++] = new IssuedTarget { InstanceId = own[targetIndex].InstanceId, X = hqX, Z = hqZ };
                }
                else if (winner.Channel == OrderChannel.Move)
                {
                    int waypointsAt = waypoints;
                    RecordWaypointsBefore(unit, issued, ref waypoints);
                    int before = MovingUnits(unit);
                    wrote = IssueMove(bunits, view, unit, winner, in plan, x, z, out bool blocked);
                    if (wrote)
                    {
                        _issued[issued++] = new IssuedTarget { InstanceId = own[targetIndex].InstanceId, X = x, Z = z };
                        if (unit.regimentpaths > 0 && WlMoveQueue.TryPending(unit, out float queuedAt, out float stamp, out int orderType))
                            carrier.Consider(own[targetIndex].InstanceId, queuedAt, stamp, orderType, x, z);
                        pathsBefore += before;
                        pathsAfter += MovingUnits(unit);
                        if (blocked) viaBlockedOrder = true;
                    }
                    else
                    {
                        waypoints = waypointsAt;
                    }
                }
                else
                {
                    wrote = IssueOther(bunits, view, targetIndex, unit, winner, feudReplay, nowHours,
                        EnemiesInRange(view, targetIndex, winner.Channel), LastShapeWriteHours(executor, own[targetIndex].InstanceId));
                }

                if (wrote) executed++;
                if (wrote && _layoutTouched) executor.NoteChildLayout(own[targetIndex].InstanceId, _layoutRefuse);
            }

            // A Move counts once SetWaypoint was called; whether it took is the
            // outcome observed next tick (D6).
            if (executed > 0)
            {
                BattleOrderIntent recorded = winner;
                recorded.TargetX = plan.X;
                recorded.TargetZ = plan.Z;
                // UpdateOrders' SetWaypoint skips the courier: no courier cadence
                // stamp. The recorded flag marks a move that went through
                // BlockedMovingOrder (its outcome waits for UpdateOrders).
                if (viaBlockedOrder) recorded.Flags &= ~IntentFlags.UseOrderDelay;
                else recorded.Flags &= ~IntentFlags.AvoidCoveredCrossings;
                executor.RecordIssued(recorded, nowHours, _issued, issued, _waypointsBefore, waypoints, pathsBefore, pathsAfter);
            }

            if (executed > 0 && !_firstFireLogged)
            {
                _firstFireLogged = true;
                OnceLog.Info("tactical-battle-core:arbiter-first-fire",
                    "[OrderArbiter] first vanilla order issued channel=" + winner.Channel + " source=" + winner.Source);
            }

            // Emit never throws (it logs its own failure once).
            if (OrderExecutor.ShouldDispatch(plan, executed))
            {
                Regiment ancestor = BattleSnapshotBuilder.UnitAt(view, groupIndex, enemy: false);
                if ((UnityEngine.Object)ancestor != null)
                    TacticalPlayerDispatchRuntime.Emit(ancestor, plan.DispatchType, plan.X, plan.Z, winner.Heading,
                        WlOrderTextRuntime.DestinationFor(plan.X, plan.Z, null), OrderOrigin.Executor, TacticalDispatchTypeMapper.IntentKind(winner.Task), in carrier);
            }

            return executed;
        }

        /// <summary>
        /// D4: a patch acting inside a vanilla micro call (#56 courier parity, #42
        /// feud replay) runs its intent through the same pipeline, refused under a
        /// higher-priority arbiter claim on the same (group, channel). Composed of
        /// TryPlanInline and ExecutePlannedInline.
        /// </summary>
        internal static bool ExecuteInline(BattleUnits bunits, Regiment group, in BattleOrderIntent intent, out OrderOutcome outcome)
        {
            if (!TryPlanInline(bunits, group, intent, out ExecutionPlan plan, out outcome)) return false;
            return ExecutePlannedInline(bunits, group, intent, plan, ref outcome);
        }

        /// <summary>
        /// The inline pipeline up to the vanilla call: claim check, then guard, chain,
        /// waypoint validation, silent-exit pre-check, dedupe and cooldown
        /// (OrderExecutor.Plan) against the side's latest snapshot. Writes nothing, so
        /// a caller (#56) can decide whether to touch vanilla state before executing.
        /// The plan's targets stay valid until the executor's next Plan call.
        /// </summary>
        internal static bool TryPlanInline(BattleUnits bunits, Regiment group, in BattleOrderIntent intent,
            out ExecutionPlan plan, out OrderOutcome outcome)
        {
            plan = default;
            outcome = new OrderOutcome
            {
                GroupId = intent.GroupId, Channel = intent.Channel, Kind = OrderOutcomeKind.Skipped, Source = intent.Source,
            };
            try
            {
                if (bunits == null || (UnityEngine.Object)group == null) { outcome.Reason = SkipReason.Inactive; return false; }
                int alliance = group.alliance;
                OrderArbiter arbiter = OrderArbiter.ForAlliance(alliance);
                OrderExecutor executor = OrderExecutor.ForAlliance(alliance);
                if (arbiter == null || executor == null) { outcome.Reason = SkipReason.NotAiControlled; return false; }

                SkipReason claim = OrderExecutor.InlineClaimCheck(arbiter, intent);
                if (claim != SkipReason.None) { outcome.Reason = claim; return false; }

                if (!BattleCore.TryGetSnapshot(alliance, out BattleSnapshot view)) { outcome.Reason = SkipReason.Inactive; return false; }

                double now = TacticalDoctrineTime.NowBattleHours;
                outcome.AtHours = now;
                BindForTick(executor);
                plan = executor.Plan(view, intent, now);
                if (!plan.Execute)
                {
                    outcome.Kind = plan.Skip == SkipReason.Duplicate ? OrderOutcomeKind.Deduped : OrderOutcomeKind.Skipped;
                    outcome.Reason = plan.Skip;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                plan = default;
                OnceLog.Warning("tactical-battle-core:inline-failed",
                    "[OrderArbiter] ExecuteInline threw: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        /// <summary>Issues a plan TryPlanInline returned for the same group and intent.</summary>
        internal static bool ExecutePlannedInline(BattleUnits bunits, Regiment group, in BattleOrderIntent intent,
            in ExecutionPlan plan, ref OrderOutcome outcome)
        {
            try
            {
                if (!plan.Execute || bunits == null || (UnityEngine.Object)group == null) return false;
                int alliance = group.alliance;
                OrderExecutor executor = OrderExecutor.ForAlliance(alliance);
                if (executor == null || !BattleCore.TryGetSnapshot(alliance, out BattleSnapshot view)) return false;

                int executed = Execute(bunits, view, executor, intent, plan, outcome.AtHours);
                outcome.Kind = executed > 0 ? OrderOutcomeKind.Issued : OrderOutcomeKind.Skipped;
                return executed > 0;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:inline-failed",
                    "[OrderArbiter] ExecuteInline threw: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static bool IssueMove(BattleUnits bunits, BattleSnapshot view, Regiment unit, in BattleOrderIntent w, in ExecutionPlan plan, float x, float z,
            out bool viaBlockedOrder)
        {
            bool useOrderDelay = plan.UseOrderDelay;
            viaBlockedOrder = false;
            Vector3 target = TacticalTerrainProbe.WithTerrainHeight(new Vector3(x, 0f, z));
            if ((w.Flags & IntentFlags.AvoidCoveredCrossings) != 0)
            {
                List<Vector3> crossings = PerceivedCoveredPoints(view);
                if (OrderExecutor.TakesBlockedMovingOrder(w, crossings != null ? crossings.Count : 0))
                {
                    // A queued order for this unit sets its waypoint within two
                    // frames; vanilla never dedupes the list, so a second one would
                    // carve a second obstacle set. Nothing issued: next tick retries.
                    if (AIBattle.BlockedMovingOrder.OrderRunning(unit)) return false;
                    new AIBattle.BlockedMovingOrder(unit, crossings, CommandWaypointWritePolicy.BlockedMoveDistanceMeters, target);
                    viaBlockedOrder = true;
                    NoteLayout(unit, RefuseFlank.None);
                    return true;
                }
            }
            // A limbered retreat (artillery producers): limber first, as
            // CheckArtyFallback does before its path (3519).
            if ((w.Flags & IntentFlags.Limber) != 0 && unit.unittyp == TacticalUnitType.Artillery && unit.mounted != 1)
                bunits.MountUnits(unit, 1);
            // Road march (BC10 decision 6): each via point is a leg of its own, and the legs
            // after the first append (newpath: false), the way vanilla's FlankMove does
            // (117089-117090). Under the courier delay the first leg queues the one order
            // (AddToOrderQueue 124917 marks the group's ordertypeactive, so the later legs'
            // queueing test, 91483-91510, is skipped) and every path added in the call is
            // activated when it is delivered (SetOrderStatus 125488-125506). A child of a fanned-out
            // order keeps its offset on the via points too.
            int legs = OrderExecutor.ViaLegs(w, 0, plan.ViaCount);
            if (legs > 0)
            {
                float offsetX = x - plan.X;
                float offsetZ = z - plan.Z;
                float v0x = plan.ViaX0 + offsetX, v0z = plan.ViaZ0 + offsetZ;
                float v1x = plan.ViaX1 + offsetX, v1z = plan.ViaZ1 + offsetZ;
                IssueLeg(bunits, unit, w, useOrderDelay, v0x, v0z, newPath: true,
                    OrderExecutor.LegHeading(v0x, v0z, legs > 1 ? v1x : x, legs > 1 ? v1z : z));
                if (legs > 1) IssueLeg(bunits, unit, w, useOrderDelay, v1x, v1z, newPath: false, OrderExecutor.LegHeading(v1x, v1z, x, z));
                IssueLeg(bunits, unit, w, useOrderDelay, x, z, newPath: false, w.Heading >= 0f ? w.Heading : -1f);
            }
            else
            {
                IssueLeg(bunits, unit, w, useOrderDelay, x, z, newPath: true, w.Heading >= 0f ? w.Heading : -1f);
            }
            NoteLayout(unit, RefuseFlank.None);
            // An in-contact withdrawal of an infantry/cavalry regiment is vanilla's
            // tactical fallback, which ends in SetMovementMode(2) (CheckLineFallbacks
            // 5178, MicroAICheckForRetreats 4898; mounted units map 2 to 4, 124714-124718):
            // the fallback walk facing the enemy, and the movementmode != 0 mark both
            // vanilla's filter (5137) and the Withdrawal producer read. SetWaypoint
            // leaves movementmode alone (its ResetMovementMode(0) acts only on 5, 124661).
            if ((w.Flags & IntentFlags.InContactWithdrawal) != 0 && unit.unittyp <= TacticalUnitType.Cavalry)
                unit.SetMovementMode(2);
            return true;
        }

        // #60's deployment write. The target is a LINE FRONT (SetGroupFormation puts the
        // headquarters depth + groupunitdistancetoback behind it, 92044-92058); the returned
        // point is that headquarters, which the executor's outcome check compares.
        private static bool IssueImmediatePlacement(BattleUnits bunits, Regiment unit, in BattleOrderIntent w, float x, float z, out float hqX, out float hqZ)
        {
            hqX = x;
            hqZ = z;
            if (unit.unittyp <= TacticalUnitType.MaxCombat || w.Heading < 0f) return false;
            float depth = unit.groupposition != null ? unit.groupposition.groupdepth : unit.depth;
            BattleWriterPolicy.DeploymentHqFromLineFront(x, z, w.Heading, depth, GamePrefs.groupunitdistancetoback, out hqX, out hqZ);
            bunits.SetGroupFormation(
                unit,
                unit.groupformation,
                manualfinalrotation: w.Heading,
                targetpos: TacticalTerrainProbe.WithTerrainHeight(new Vector3(x, 0f, z)),
                immediateplacement: true,
                newpath: true,
                modifylastwaypoint: false,
                newstate: 2,
                refuseflank: -1,
                ignoredeplyomentzone: false,
                skiprotation: false,
                showmovementoptions: false,
                placeentrenchments: false,
                adjustbyterrainshape: true);
            ClearImmediatePlacementState(unit, new HashSet<int>());
            NoteLayout(unit, RefuseFlank.None);
            return true;
        }

        // Vanilla ends its placement pass by clearing these on every unit (85873-85883); a
        // group placed after that pass needs the same, or the next order reads a stale waypoint.
        private static void ClearImmediatePlacementState(Regiment unit, HashSet<int> visited)
        {
            if ((UnityEngine.Object)unit == null || !visited.Add(unit.GetInstanceID())) return;
            unit.lastsetwaypointposition = default(Vector3);
            unit.immediateunitplacement = false;
            if (unit.groupposition == null || unit.groupposition.attachedunitreg == null) return;
            int attached = Math.Min(unit.groupposition.attachedunits, unit.groupposition.attachedunitreg.Length);
            for (int i = 0; i < attached; i++) ClearImmediatePlacementState(unit.groupposition.attachedunitreg[i], visited);
        }

        // One SetWaypoint call of a move: the target (or a via point) at terrain height.
        private static void IssueLeg(BattleUnits bunits, Regiment unit, in BattleOrderIntent w, bool useOrderDelay, float x, float z,
            bool newPath, float heading)
        {
            bunits.SetWaypoint(
                unit,
                TacticalTerrainProbe.WithTerrainHeight(new Vector3(x, 0f, z)),
                newpath: newPath,
                doublequick: (w.Flags & IntentFlags.DoubleQuick) != 0,
                manualfinalrotation: heading,
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
        }

        private static bool IssueOther(BattleUnits bunits, BattleSnapshot view, int targetIndex, Regiment unit, in BattleOrderIntent w,
            bool feudReplay, double nowHours, float enemiesInRange, float lastShapeWriteHours)
        {
            switch (w.Channel)
            {
                case OrderChannel.Stance:
                    return TacticalStanceWriter.EnsureStance(bunits, unit, w.Value, w.Reason, w.Source, (w.Flags & IntentFlags.WorksHold) != 0);
                case OrderChannel.CombatBehavior:
                {
                    // BC14W (D34): the focal assault's bombardment is started as vanilla starts one, not as a plain behavior write.
                    if ((w.Flags & IntentFlags.BombardStart) != 0) return StartBombard(bunits, unit, w.TargetX, w.TargetZ);
                    bool wrote = TacticalCombatBehaviorWriter.EnsureBehavior(bunits, unit, w.Value, (w.Flags & IntentFlags.PerRegiment) != 0);
                    // Charge doctrine Disarm (BC6b): behavior alone never ends charge mode 3
                    // (UpdateChargeTarget re-evaluates mode 3 whatever the behavior,
                    // 122226), so leave it as vanilla's own refusal does (SetMovementMode()
                    // 122290), also when behavior 2 was already ordered. Regiments only.
                    if ((w.Flags & IntentFlags.LeaveChargeMode) != 0 &&
                        unit.unittyp <= TacticalUnitType.MaxCombat &&
                        unit.movementmode == ChargeDoctrine.ArmedMovementMode)
                    {
                        unit.SetMovementMode(ChargeDoctrine.NormalMovementMode);
                        wrote = true;
                    }
                    if (!wrote) return false;
                    // The artillery doctrine's cease-bombardment: vanilla pairs 7 with
                    // bombardstarttime = 0 (CheckAIBombardment 3904).
                    if (BattleWriterPolicy.ResetsBombardStart(w)) unit.bombardstarttime = 0f;
                    return true;
                }
                case OrderChannel.Formation:
                    if ((w.Flags & (IntentFlags.Limber | IntentFlags.Unlimber)) != 0) return IssueMount(bunits, unit, (w.Flags & IntentFlags.Limber) != 0);
                    if ((w.Flags & IntentFlags.Deploy) != 0) return IssueDeploy(bunits, unit, w.Heading);
                    return IssueFormation(bunits, view, targetIndex, unit, w, feudReplay, nowHours, enemiesInRange, lastShapeWriteHours);
                case OrderChannel.Facing:
                    return IssueFacing(bunits, view, targetIndex, unit, w, feudReplay, nowHours, enemiesInRange, lastShapeWriteHours);
                case OrderChannel.Stop:
                    GameObject go = ((Component)unit).gameObject;
                    if (go == null) return false;
                    bunits.PerformActionForGroupForAllUnitsOnLevel(go, -1, "StopRegiment", ManualStop,
                        alsononactive: false, excluderoutedandretreatingunits: false, sameaigrouponly: true);
                    return true;
                default:
                    return false;
            }
        }

        // BC14W (D34, the one new battle write path): vanilla's bombardment start, as AIBattle.CheckAIBombardment writes it (3897-3900) without the W&L
        // feud timer it stamps (lastfeudactiontime: Whiskey never acts for the feud instance). Combat behavior 9 through the one behavior writer and, only
        // when that changed it (a battery already bombarding gets no write), the battery's bombardposition (the point, on the terrain) and bombardrange
        // (GamePrefs.bombardmentmaxrangesize, the radius vanilla's target filter then keeps the battery's fire inside, 118405/118433). Behind Commander
        // Mode Active and TacticalWriteGuard.CanWrite: never a player-owned battery.
        private static bool StartBombard(BattleUnits bunits, Regiment unit, float x, float z)
        {
            if (bunits == null || unit == null) return false;
            if (!TacticalWriteGuard.ModeAllowsWrites() || !TacticalWriteGuard.CanWrite(unit))
            {
                EmitBombardStart(unit, x, z, "refused-by-guard");
                return false;
            }
            if (!TacticalCombatBehaviorWriter.EnsureBehavior(bunits, unit, BattleWriterPolicy.CombatBehaviorBombard, perRegiment: false)) return false;
            unit.bombardposition = TacticalTerrainProbe.WithTerrainHeight(new Vector3(x, 0f, z));
            unit.bombardrange = GamePrefs.bombardmentmaxrangesize;
            EmitBombardStart(unit, x, z, "issued");
            return true;
        }

        // One TacticalBombardStart row per executed start (BC14W requirement-telemetry); a start the guard refused is throttled per battery. The plan is
        // always FocalAssault: the artillery producer submits a BombardStart only for that plan's preparation point.
        private static readonly Dictionary<string, float> _lastBombardRowAt = new Dictionary<string, float>();

        private static void EmitBombardStart(Regiment unit, float x, float z, string outcome)
        {
            try
            {
                int battery = unit.GetInstanceID();
                if (!TacticalTelemetry.ShouldEmit(_lastBombardRowAt, "tactical-bombard-start:" + battery, outcome, Environment.TickCount * 0.001f, 30f, false)) return;
                int side = unit.alliance;
                float range = GamePrefs.bombardmentmaxrangesize;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalBombardStart", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(side)
                        .WithDecision("TacticalBombardStart", outcome, "side=" + side + "|battery=" + battery)
                        .WithField("battery", battery)
                        .WithField("pointX", x)
                        .WithField("pointZ", z)
                        .WithField("range", range)
                        .WithField("plan", BattlePlanId.FocalAssault.ToString())
                        .WithField("outcome", outcome));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:bombard-start-row", "[BombardStart] row failed: " + ex.GetType().Name);
            }
        }

        // Artillery limber / unlimber: BattleUnits.MountUnits (90860-90890; formation
        // 3 + mounted 1, or formation 0 + mounted 0). Never on a moving battery
        // (BattleWriterPolicy.PlanArtilleryMicroWrite only plans idle ones).
        private static bool IssueMount(BattleUnits bunits, Regiment unit, bool limber)
        {
            if (unit.unittyp != TacticalUnitType.Artillery) return false;
            int mount = limber ? 1 : 0;
            if (unit.mounted == mount) return false;
            bunits.MountUnits(unit, mount);
            return true;
        }

        // Group formation (R8, CommandFormationWritePolicy): the courier
        // SetFormation for a real change out of contact and cover, else - for a
        // refused flank (BC5a) - the in-place wheel with the skew. The policy's
        // cooldown (0.05 h, 0.1 h under fire) runs from the group's last
        // formation or facing write, as #61's _lastFormationWriteAt did.
        // Unit churn: deploy an infantry regiment out of the march column (ColumnDeployment). The live column test comes first (the snapshot is a tick
        // old), then vanilla's own manual stop, which restores the formation the column replaced and ends distancemarchcolumn (132498-132511) and, with
        // the path cleared, keeps CheckFormationChange (127662-127668) from re-columning the regiment on its next look; then the line, facing the
        // threat (the manual rotation target of the formation param, in place of the march heading vanilla deploys to, 127677). Behind the executor's
        // guards like every battle write; the target passed TacticalWriteGuard.CanWrite in the plan.
        private static bool IssueDeploy(BattleUnits bunits, Regiment unit, float heading)
        {
            if (unit.unittyp != TacticalUnitType.Infantry || unit.isrouted || unit.markedforrout || unit.mounted != 0) return false;
            if (unit.formation != ColumnDeployment.RegimentMarchColumn) return false;
            GameObject go = ((Component)unit).gameObject;
            if (go == null) return false;
            bunits.PerformActionForGroupForAllUnitsOnLevel(go, -1, "StopRegiment", ManualStop,
                alsononactive: false, excluderoutedandretreatingunits: false, sameaigrouponly: true);
            if (unit.formation == ColumnDeployment.RegimentMarchColumn)
                unit.ChangeRegimentFormation(GameVars.SetFormationParam(ColumnDeployment.RegimentLine, manualset: true, -1, heading));
            return true;
        }

        private static bool IssueFormation(BattleUnits bunits, BattleSnapshot view, int targetIndex, Regiment unit, in BattleOrderIntent w,
            bool feudReplay, double nowHours, float enemiesInRange, float lastShapeWriteHours)
        {
            GameObject go = ((Component)unit).gameObject;
            int formation = w.Value;
            if (go == null || formation < 0) return false;
            if (unit.unittyp <= TacticalUnitType.MaxCombat)
            {
                bunits.SetFormation(go, formation, exitifsameformation: true);
                return true;
            }

            FormationWriteDecision decision = OrderExecutor.DecideFormation(w, unit.groupformation,
                unit.combatstatussubordinates, unit.groupsubordinatesmoving, unit.regimentpaths, unit.covervaluesubordinates,
                (float)nowHours, lastShapeWriteHours, enemiesInRange);
            switch (decision.Kind)
            {
                case FormationWriteKind.RefuseInPlace:
                    return RefuseInPlace(view, targetIndex, w, feudReplay);
                case FormationWriteKind.VanillaFormation:
                {
                    int index = CommandFormationWritePolicy.VanillaSetFormationIndexForGroup(formation);
                    if (index < 0) return false;
                    bunits.SetFormation(go, index, exitifsameformation: true);
                    NoteLayout(unit, RefuseFlank.None);
                    return true;
                }
                case FormationWriteKind.WheelGroup:
                    // #61 passed the threat heading (manualFinalRotation) when it
                    // had one; -1 would take lastsetwaypointrotation (91900-91902).
                    return Wheel(bunits, unit, w.Heading >= 0f ? w.Heading : -1f, w.Refuse, formation);
                default:
                    return false;
            }
        }

        // Group facing: in contact rotate the reachable regiments in place, out of
        // contact wheel the group (with a refused flank when the intent carries
        // one). The rotation walk is vanilla's group order: it stops at a
        // detached node (GetAttachedUnitsReg(excludedechainedunits) 119865) and at
        // player-owned units (PostureSense.CollectRotatableRegiments), and each
        // rotated regiment still passes the write guard.
        private static bool IssueFacing(BattleUnits bunits, BattleSnapshot view, int targetIndex, Regiment unit, in BattleOrderIntent w,
            bool feudReplay, double nowHours, float enemiesInRange, float lastShapeWriteHours)
        {
            float heading = w.Heading;
            if (heading < 0f) return false;
            if (unit.unittyp <= TacticalUnitType.MaxCombat) return RotateInPlace(unit, heading);

            FormationWriteDecision decision = OrderExecutor.DecideFacing(w,
                unit.combatstatussubordinates, unit.groupsubordinatesmoving, unit.regimentpaths, unit.covervaluesubordinates,
                (float)nowHours, lastShapeWriteHours, enemiesInRange);
            switch (decision.Kind)
            {
                case FormationWriteKind.RefuseInPlace:
                    return RefuseInPlace(view, targetIndex, w, feudReplay);
                case FormationWriteKind.RotateInPlace:
                {
                    if (_rotationQueue.Length < view.OwnCount)
                    {
                        _rotationQueue = new int[view.OwnCount];
                        _rotationTargets = new int[view.OwnCount];
                    }
                    int count = PostureSense.CollectRotatableRegiments(view, targetIndex, _rotationQueue, _rotationTargets);
                    int rotated = 0;
                    for (int k = 0; k < count; k++)
                    {
                        Regiment child = BattleSnapshotBuilder.UnitAt(view, _rotationTargets[k], enemy: false);
                        if ((UnityEngine.Object)child == null || !TacticalWriteGuard.CanWrite(child, feudReplay)) continue;
                        if (RotateInPlace(child, heading)) rotated++;
                    }
                    return rotated > 0;
                }
                case FormationWriteKind.WheelGroup:
                    return Wheel(bunits, unit, heading, w.Refuse, unit.groupformation);
                default:
                    return false;
            }
        }

        // BC11 decision 2: a refused flank in combat. The wing regiments FlankFacing.RefuseRotations picks
        // (setback at least RefuseWingShare of the refuse depth, not turned back by vanilla's micro turn
        // 4560-4608, at most MaxRefuseRotationsPerTick) rotate in place to the skewed-line rotation
        // (78155-78167) through RotateRegiment; each passes TacticalWriteGuard.CanWrite and RotateInPlace's
        // own standing/charging checks. No SetGroupFormation, so nothing is re-laid and NoteLayout is not
        // called (the leaf slot rule is unchanged).
        private static bool RefuseInPlace(BattleSnapshot view, int groupIndex, in BattleOrderIntent w, bool feudReplay)
        {
            if (view == null || groupIndex < 0 || groupIndex >= view.OwnCount || w.Refuse == RefuseFlank.None) return false;
            int n = view.OwnCount;
            if (_rotationQueue.Length < n)
            {
                _rotationQueue = new int[n];
                _rotationTargets = new int[n];
            }
            if (_rotationHeadings.Length < n) _rotationHeadings = new float[n];
            ref UnitRecord group = ref view.Own[groupIndex];
            MicroTurnRule rule = MicroTurnRule.From(GamePrefs.distancetoturntowardsenemy, GamePrefs.angletriggerforturningtowardsenemy,
                GamePrefs.additionalanglewhenincovermicro, GamePrefs.maxcovervalueforgrouprotation);
            int count = FlankFacing.RefuseRotations(view, groupIndex, w.Refuse, group.GroupWidth, GamePrefs.refuseflankdepth,
                GamePrefs.refuseflankskew, in rule, _rotationQueue, _rotationTargets, _rotationHeadings, out int skipped,
                _refuseTurnBacks, TacticalDoctrineTime.NowBattleHours);
            int rotated = 0;
            for (int k = 0; k < count; k++)
            {
                Regiment child = BattleSnapshotBuilder.UnitAt(view, _rotationTargets[k], enemy: false);
                if ((UnityEngine.Object)child == null || !TacticalWriteGuard.CanWrite(child, feudReplay)) continue;
                if (RotateInPlace(child, _rotationHeadings[k]))
                {
                    rotated++;
                    _refuseTurnBacks.NoteRotated(view.Own[_rotationTargets[k]].InstanceId, _rotationHeadings[k], TacticalDoctrineTime.NowBattleHours);
                }
            }
            if (rotated > 0 || skipped > 0) PostureTelemetry.RefuseInPlace(view, groupIndex, w.Refuse, rotated, skipped);
            if (rotated > 0) NoteRefuseFirstFire("InPlace", w.Refuse, rotated, skipped);
            return rotated > 0;
        }

        // SetGroupFormation(current formation, rotation, newpath:false,
        // modifylastwaypoint:true): skips ClearTargets (91960) and re-paths every
        // non-detached child to its rotated, optionally skewed slot (92095-92097).
        private static bool Wheel(BattleUnits bunits, Regiment unit, float heading, RefuseFlank refuse, int fallbackFormation)
        {
            int current = unit.groupformation;
            int formation = current >= 0 && current <= 4 ? current : fallbackFormation;
            if (formation < 0 || formation > 4) return false;
            bunits.SetGroupFormation(
                unit,
                formation,
                manualfinalrotation: heading,
                targetpos: default(Vector3),
                immediateplacement: false,
                newpath: false,
                modifylastwaypoint: true,
                newstate: 2,
                refuseflank: OrderExecutor.VanillaRefuseFlank(refuse),
                ignoredeplyomentzone: false,
                skiprotation: false,
                showmovementoptions: false,
                placeentrenchments: false,
                adjustbyterrainshape: true);
            NoteLayout(unit, refuse);
            if (refuse != RefuseFlank.None)
                NoteRefuseFirstFire("Wheel", refuse, unit.groupposition != null ? unit.groupposition.attachedunits : 0, 0);
            return true;
        }

        // Smoke item 2.26: the first refused flank of a battle, one line.
        private static void NoteRefuseFirstFire(string mode, RefuseFlank side, int regiments, int skipped)
        {
            if (_refuseLogged) return;
            _refuseLogged = true;
            OnceLog.Info("tactical-battle-core:refuse-first-fire",
                "[OrderExecutor] first refused flank: mode=" + mode + " flank=" + side + " regiments=" + regiments + " skipped=" + skipped);
        }

        private static void NoteLayout(Regiment unit, RefuseFlank refuse)
        {
            if (unit.unittyp <= TacticalUnitType.MaxCombat) return;
            _layoutTouched = true;
            _layoutRefuse = refuse;
        }

        // The last formation or facing write the executor issued for the unit
        // (#61 _lastFormationWriteAt covered both), or -1.
        private static float LastShapeWriteHours(OrderExecutor executor, int instanceId)
        {
            float last = -1f;
            if (executor.TryGetLastOutcome(instanceId, OrderChannel.Formation, out OrderOutcome formation) &&
                formation.Kind == OrderOutcomeKind.Issued)
                last = (float)formation.AtHours;
            if (executor.TryGetLastOutcome(instanceId, OrderChannel.Facing, out OrderOutcome facing) &&
                facing.Kind == OrderOutcomeKind.Issued && facing.AtHours > last)
                last = (float)facing.AtHours;
            return last;
        }

        // F7 (BC5a): the formation policy's "enemies in range" input, fog-honest.
        // Vanilla UpdateGroupStrength sets groupenemiesinrange from the group's
        // own unitrange.enemiesinrange (120558/120563, the HQ-local reach): enemy
        // strength within max(maximumlineofsight[unittyp],
        // distancetoenemytocancelinterruption) (InitMaxUnitRanges 122510,
        // UpdateUnitRangeFast 122608/122650-122655), never fog-filtered for the
        // AI side. Here: what this side perceives within that reach of the
        // group's own position (BattleWriterPolicy.SnapshotEnemyStrengthInRange).
        private static float EnemiesInRange(BattleSnapshot view, int index, OrderChannel channel)
        {
            if (channel != OrderChannel.Formation && channel != OrderChannel.Facing) return 0f;
            // Only the group path reads it (a regiment rotates or re-forms directly).
            if (index < 0 || index >= view.OwnCount || (view.Own[index].Flags & UnitFlags.IsGroup) == 0) return 0f;
            return BattleWriterPolicy.SnapshotEnemyStrengthInRange(view, index, GroupRangeReach());
        }

        // vanilla's enemiesinrange reach over a group's combat unit types
        // (BattlePerception.GroupRangeReach; #44 reads the same).
        internal static float GroupRangeReach()
        {
            return BattlePerception.GroupRangeReach(GamePrefs.maximumlineofsight, GamePrefs.distancetoenemytocancelinterruption);
        }

        // Vanilla turns a settled unit toward an enemy with RotateRegiment
        // (UnlimberArtilleryAIMicro 3491); never one that is moving, rotating,
        // routed or charging.
        private static bool RotateInPlace(Regiment unit, float heading)
        {
            if (unit.isrouted || unit.markedforrout) return false;
            if (unit.regimentpaths > 0 || unit.withinrotationprocess || unit.movementmode == 3) return false;
            if ((UnityEngine.Object)unit.chargetarget != null) return false;
            GameObject go = ((Component)unit).gameObject;
            if (go == null || !go.activeInHierarchy) return false;
            float d = Math.Abs(((Component)unit).transform.eulerAngles.y - heading) % 360f;
            if (d > 180f) d = 360f - d;
            if (d <= FacingToleranceDegrees) return false;
            unit.RotateRegiment(heading);
            return true;
        }

        // The unit's and every descendant's lastsetwaypointposition before the call
        // (allattachedunits holds all descendants), keyed as the snapshot keys them.
        private static void RecordWaypointsBefore(Regiment unit, int target, ref int count)
        {
            Regiment[] units = unit.allattachedunits;
            int needed = count + 1 + (units != null ? units.Length : 0);
            if (_waypointsBefore.Length < needed) Array.Resize(ref _waypointsBefore, Math.Max(needed, _waypointsBefore.Length * 2));
            AddWaypointBefore(unit, target, ref count);
            if (units == null) return;
            for (int i = 0; i < units.Length; i++)
                if ((UnityEngine.Object)units[i] != null) AddWaypointBefore(units[i], target, ref count);
        }

        // UnitRecord.InstanceId is the unit's GameObject instance id (BattleSnapshotBuilder).
        private static void AddWaypointBefore(Regiment unit, int target, ref int count)
        {
            GameObject go = ((Component)unit).gameObject;
            if (go == null) return;
            Vector3 w = unit.lastsetwaypointposition;
            _waypointsBefore[count++] = new UnitWaypoint { InstanceId = go.GetInstanceID(), Target = target, X = w.x, Z = w.z };
        }

        private static int MovingUnits(Regiment unit)
        {
            int moving = unit.regimentpaths > 0 ? 1 : 0;
            Regiment[] units = unit.allattachedunits;
            if (units == null) return moving;
            for (int i = 0; i < units.Length; i++)
                if (units[i] != null && units[i].regimentpaths > 0) moving++;
            return moving;
        }

        /// <summary>How many covered points the side perceives this snapshot (the posture pipeline skips a route when a move around them is due).</summary>
        internal static int CoveredPointCount(BattleSnapshot view)
        {
            List<Vector3> list = PerceivedCoveredPoints(view);
            return list != null ? list.Count : 0;
        }

        // The crossings and fire points the side's perceived enemy covers, as the world
        // positions BlockedMovingOrder carves obstacles at (terrain height: a NavMeshObstacle
        // far above or below the mesh carves nothing). Built once per snapshot.
        private static List<Vector3> PerceivedCoveredPoints(BattleSnapshot view)
        {
            int side = view.Alliance;
            if (side < 0 || side >= _covered.Length) return null;
            List<Vector3> list = _covered[side];
            if (_coveredTick[side] == view.TickId) return list;
            _coveredTick[side] = view.TickId;
            list.Clear();
            using (TelemetryPerf.Scope("tactical.covered-crossings", TelemetryLayer.Tactical, TelemetryCategory.Performance, 0.5))
            {
                try
                {
                    int count = CoveredCrossings.BuildPointsGrowing(view, BattleTerrainGridRuntime.Get(), ref _coveredX[side], ref _coveredZ[side],
                        CoveredPointCeiling, out int crossingPoints, out bool truncated);
                    if (truncated)
                        OnceLog.Warning("tactical-battle-core:covered-crossings-truncated",
                            "[OrderExecutor] the perceived covered-point list reached its " + CoveredPointCeiling + "-point ceiling; later points were cut");
                    for (int i = 0; i < count; i++)
                        list.Add(TacticalTerrainProbe.WithTerrainHeight(new Vector3(_coveredX[side][i], 0f, _coveredZ[side][i])));
                    if (count > 0 && !_coveredLogged)
                    {
                        _coveredLogged = true;
                        OnceLog.Info("tactical-battle-core:covered-crossings",
                            "[OrderExecutor] perceived covered crossings: alliance=" + side + " crossings=" + crossingPoints + " coverPoints=" + (count - crossingPoints));
                    }
                }
                catch (Exception ex)
                {
                    list.Clear();
                    OnceLog.Warning("tactical-battle-core:covered-crossings-failed",
                        "[OrderExecutor] covered-crossing build failed, moves use a plain SetWaypoint: " + ex.GetType().Name + " " + ex.Message);
                }
            }
            return list;
        }

        private static void Reset()
        {
            _firstFireLogged = false;
            _coveredLogged = false;
            _refuseLogged = false;
            _refuseTurnBacks.Clear();
            for (int a = 0; a < _covered.Length; a++)
            {
                _covered[a].Clear();
                _coveredTick[a] = -1L;
            }
        }
    }
}
