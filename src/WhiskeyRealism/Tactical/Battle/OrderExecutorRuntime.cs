using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
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
    //     (3054-3150) with the side's own AIBattle.blockedcrossings and vanilla's
    //     150 m range, as vanilla's line-group move does (6953-6956). It carves
    //     NavMeshObstacles over the covered crossings away from the destination;
    //     UpdateOrders (3382, every AIBattle.Update frame, before the pause
    //     return) calls SetWaypoint(useorderdelay:false, manualfinalrotation -1)
    //     two frames later and removes them. The next arbiter tick (a macro cycle,
    //     0.015 game-hours) is many frames on, so D6 sees the move through the
    //     waypoint stamp (SetGroupFormation 92067) or the moving-unit count.
    //     Without crossing data (null on a map without river crossings,
    //     7919-7920) the move is a plain SetWaypoint.
    //   Stance: Util/TacticalStanceWriter (the one ChangeStance caller, 90772).
    //   Formation: BattleUnits.SetFormation (90971, courier) only when
    //     CommandFormationWritePolicy allows it — vanilla re-forms only a settled,
    //     non-moving group out of contact and cover (AdjustGroupFormations 5886).
    //   Facing: Regiment.RotateRegiment in place in contact, or
    //     SetGroupFormation(modifylastwaypoint, newpath:false) wheel out of contact
    //     (91822-92110; skips ClearTargets, re-paths children to rotated slots).
    //   CombatBehavior: Util/TacticalCombatBehaviorWriter (ChangeCombatBehavior
    //     90944-90966), per regiment for PerRegiment intents (D10).
    //   Stop: PerformActionForGroupForAllUnitsOnLevel(go, -1, "StopRegiment",
    //     StopOrder(true, true, false, false, true), sameaigrouponly:true) — the
    //     vanilla manual stop (117198).
    internal static class OrderExecutorRuntime
    {
        static OrderExecutorRuntime()
        {
            BattleScope.Register("order-executor-runtime", Reset);
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

        private static FieldInfo _blockedCrossingsField;
        private static bool _blockedCrossingsResolved;

        // The units one Move reached (grown to the largest chain seen).
        private static IssuedTarget[] _issued = new IssuedTarget[64];
        // Pre-call waypoints of those units and their descendants (grown as needed).
        private static UnitWaypoint[] _waypointsBefore = new UnitWaypoint[256];
        // Per-battle latch for the first-fire line, so the message string is built
        // once per battle (its OnceLog key re-arms at battle end too).
        private static bool _firstFireLogged;

        /// <summary>Per-tick seams and settings for an executor.</summary>
        internal static void BindForTick(OrderExecutor executor)
        {
            executor.Bind(Guard, WaypointValidatorRuntime.Instance);
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
                if (winner.Channel == OrderChannel.Move)
                {
                    int waypointsAt = waypoints;
                    RecordWaypointsBefore(unit, issued, ref waypoints);
                    int before = MovingUnits(unit);
                    wrote = IssueMove(bunits, unit, winner, plan.UseOrderDelay, x, z, out bool blocked);
                    if (wrote)
                    {
                        _issued[issued++] = new IssuedTarget { InstanceId = own[targetIndex].InstanceId, X = x, Z = z };
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
            }

            // A Move counts once SetWaypoint was called; whether it took is the
            // outcome observed next tick (D6).
            if (executed > 0)
            {
                BattleOrderIntent recorded = winner;
                recorded.TargetX = plan.X;
                recorded.TargetZ = plan.Z;
                // UpdateOrders' SetWaypoint skips the courier: no courier cadence stamp.
                if (viaBlockedOrder) recorded.Flags &= ~IntentFlags.UseOrderDelay;
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
                    TacticalPlayerDispatchRuntime.Emit(ancestor, plan.DispatchType, plan.X, plan.Z, winner.Heading, winner.Reason);
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

        private static bool IssueMove(BattleUnits bunits, Regiment unit, in BattleOrderIntent w, bool useOrderDelay, float x, float z,
            out bool viaBlockedOrder)
        {
            viaBlockedOrder = false;
            Vector3 target = TacticalTerrainProbe.WithTerrainHeight(new Vector3(x, 0f, z));
            // UpdateOrders' SetWaypoint carries no heading or double-quick: only a
            // move without them goes this way (posture moves carry neither).
            if ((w.Flags & IntentFlags.AvoidCoveredCrossings) != 0 && w.Heading < 0f && (w.Flags & IntentFlags.DoubleQuick) == 0)
            {
                List<Vector3> crossings = BlockedCrossings(bunits, unit.alliance);
                if (crossings != null)
                {
                    // A queued order for this unit sets its waypoint within two
                    // frames; vanilla never dedupes the list, so a second one would
                    // carve a second obstacle set. Nothing issued: next tick retries.
                    if (AIBattle.BlockedMovingOrder.OrderRunning(unit)) return false;
                    new AIBattle.BlockedMovingOrder(unit, crossings, CommandWaypointWritePolicy.BlockedMoveDistanceMeters, target);
                    viaBlockedOrder = true;
                    return true;
                }
            }
            // A limbered retreat (artillery producers): limber first, as
            // CheckArtyFallback does before its path (3519).
            if ((w.Flags & IntentFlags.Limber) != 0 && unit.unittyp == TacticalUnitType.Artillery && unit.mounted != 1)
                bunits.MountUnits(unit, 1);
            bunits.SetWaypoint(
                unit,
                target,
                newpath: true,
                doublequick: (w.Flags & IntentFlags.DoubleQuick) != 0,
                manualfinalrotation: w.Heading >= 0f ? w.Heading : -1f,
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

        private static bool IssueOther(BattleUnits bunits, BattleSnapshot view, int targetIndex, Regiment unit, in BattleOrderIntent w,
            bool feudReplay, double nowHours, float enemiesInRange, float lastShapeWriteHours)
        {
            switch (w.Channel)
            {
                case OrderChannel.Stance:
                    return TacticalStanceWriter.EnsureStance(bunits, unit, w.Value, w.Reason, w.Source);
                case OrderChannel.CombatBehavior:
                    if (!TacticalCombatBehaviorWriter.EnsureBehavior(bunits, unit, w.Value, (w.Flags & IntentFlags.PerRegiment) != 0)) return false;
                    // The artillery doctrine's cease-bombardment: vanilla pairs 7 with
                    // bombardstarttime = 0 (CheckAIBombardment 3904).
                    if (BattleWriterPolicy.ResetsBombardStart(w)) unit.bombardstarttime = 0f;
                    return true;
                case OrderChannel.Formation:
                    if ((w.Flags & (IntentFlags.Limber | IntentFlags.Unlimber)) != 0) return IssueMount(bunits, unit, (w.Flags & IntentFlags.Limber) != 0);
                    return IssueFormation(bunits, unit, w, nowHours, enemiesInRange, lastShapeWriteHours);
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
        private static bool IssueFormation(BattleUnits bunits, Regiment unit, in BattleOrderIntent w, double nowHours,
            float enemiesInRange, float lastShapeWriteHours)
        {
            GameObject go = ((Component)unit).gameObject;
            int formation = w.Value;
            if (go == null || formation < 0) return false;
            if (unit.unittyp <= TacticalUnitType.MaxCombat)
            {
                bunits.SetFormation(go, formation, exitifsameformation: true);
                return true;
            }

            FormationWriteDecision decision = CommandFormationWritePolicy.Decide(new FormationWriteInput(
                needsFormation: unit.groupformation != formation,
                needsFacing: false,
                needsRefuse: w.Refuse != RefuseFlank.None,
                unit.combatstatussubordinates,
                unit.groupsubordinatesmoving,
                unit.regimentpaths,
                unit.covervaluesubordinates,
                (float)nowHours,
                lastShapeWriteHours,
                enemiesInRange));
            switch (decision.Kind)
            {
                case FormationWriteKind.VanillaFormation:
                {
                    int index = CommandFormationWritePolicy.VanillaSetFormationIndexForGroup(formation);
                    if (index < 0) return false;
                    bunits.SetFormation(go, index, exitifsameformation: true);
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

            FormationWriteDecision decision = CommandFormationWritePolicy.Decide(new FormationWriteInput(
                needsFormation: false,
                needsFacing: true,
                needsRefuse: w.Refuse != RefuseFlank.None,
                unit.combatstatussubordinates,
                unit.groupsubordinatesmoving,
                unit.regimentpaths,
                unit.covervaluesubordinates,
                (float)nowHours,
                lastShapeWriteHours,
                enemiesInRange));
            switch (decision.Kind)
            {
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
            return true;
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
            return BattleWriterPolicy.SnapshotEnemyStrengthInRange(view, index, LineOfSightRange());
        }

        private static float LineOfSightRange()
        {
            float range = GamePrefs.distancetoenemytocancelinterruption;
            float[] sight = GamePrefs.maximumlineofsight;
            if (sight != null)
            {
                for (int t = 0; t <= TacticalUnitType.Skirmisher && t < sight.Length; t++)
                    if (sight[t] > range) range = sight[t];
            }
            return range;
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

        // The side's own AIBattle.blockedcrossings (private, 3340): the
        // crossings that instance's UpdateBlockedCrossings found covered by the
        // enemy of bunits.alliance[sideofai] (7939). The feud instance is skipped.
        private static List<Vector3> BlockedCrossings(BattleUnits bunits, int alliance)
        {
            try
            {
                if (!_blockedCrossingsResolved)
                {
                    _blockedCrossingsResolved = true;
                    _blockedCrossingsField = AccessTools.Field(typeof(AIBattle), "blockedcrossings");
                    if (_blockedCrossingsField == null)
                        OnceLog.Warning("tactical-battle-core:blocked-crossings-missing",
                            "[OrderExecutor] AIBattle.blockedcrossings not found; long objective moves use plain SetWaypoint");
                }
                if (_blockedCrossingsField == null) return null;
                AIBattle[] battles = bunits.aibattle;
                int[] alliances = bunits.alliance;
                if (battles == null || alliances == null) return null;
                for (int i = 0; i < battles.Length; i++)
                {
                    AIBattle battle = battles[i];
                    if ((UnityEngine.Object)battle == null || TacticalWriteGuard.IsFeudInstance(battle)) continue;
                    int side = battle.sideofai;
                    if (side < 0 || side >= alliances.Length || alliances[side] != alliance) continue;
                    return _blockedCrossingsField.GetValue(battle) as List<Vector3>;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:blocked-crossings-failed",
                    "[OrderExecutor] blockedcrossings read failed: " + ex.GetType().Name + " " + ex.Message);
            }
            return null;
        }

        private static void Reset()
        {
            _firstFireLogged = false;
        }
    }
}
