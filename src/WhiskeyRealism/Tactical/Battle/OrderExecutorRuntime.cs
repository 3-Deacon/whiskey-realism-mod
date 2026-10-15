using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // The vanilla calls behind OrderExecutor (plan BC4 step 7, steps 8-9
    // bookkeeping). Runtime only; excluded from the test csproj. Every call
    // re-checks TacticalWriteGuard.CanWrite(Regiment) on the live unit.
    //
    // Vanilla anchors (decompile, build 14703111):
    //   Move: BattleUnits.SetWaypoint (91232-91640) with showmovementoptions:false
    //     (skips the W&L control exit 91300 and the order-arrow UI) and
    //     useorderdelay from the plan; groups go on to SetGroupFormation (91559),
    //     regiments to RegimentSetPath (91540). Under useorderdelay the move is
    //     queued with AddToOrderQueue on the courier's unit (91483-91510), which
    //     sets ordertypeactive[0|1] on every receiving unit (124994-124999) until
    //     delivery clears it (124878); that flag plus the unit's own orderqueue is
    //     the "queued" signal, so no transform.parent walk is needed.
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

        // BC3 seam (plan step 9): TacticalPlayerDispatchRuntime.Emit. Null until
        // BC3 merges; with no dispatch mapper bound no plan carries a dispatch type.
        internal interface IDispatchEmitter
        {
            bool Emit(Regiment ancestorOrPlayerCommand, int vanillaType, float x, float z, float rotation, string destinationName);
        }

        internal static IDispatchEmitter DispatchEmitter;

        // BC3 seam: the guard until TacticalWriteGuard.CanWrite(in UnitRecord, out
        // SkipReason) exists — Tactical Commander Mode only; PlayerOwned and Feud come
        // from the snapshot flags inside OrderExecutor.Plan.
        private sealed class ModeOrderGuard : IOrderGuard
        {
            public bool CanWrite(in UnitRecord unit, out SkipReason reason)
            {
                bool allowed = TacticalWriteGuard.ModeAllowsWrites();
                reason = allowed ? SkipReason.None : SkipReason.ModeNotActive;
                return allowed;
            }
        }

        private const float FacingToleranceDegrees = OrderExecutor.FacingToleranceDegrees;

        private static readonly IOrderGuard Guard = new ModeOrderGuard();
        private static readonly IChainResolver Chain = new SelfOnlyChainResolver();
        private static readonly Regiment.StopOrder ManualStop =
            new Regiment.StopOrder(_lastwaypointrotation: true, _manualstop: true, _skipmarchcolumnstop: false,
                _skipadvancefallbackstops: false, _clearregimentpathsbeforerotation: true);

        // instance id (component or GameObject) → Regiment, rebuilt from
        // BattleUnits.completeunitlist when the roster grows or a lookup misses
        // (at most once per tick).
        private static readonly Dictionary<int, Regiment> _regiments = new Dictionary<int, Regiment>(1024);
        // The units one Move reached (grown to the largest chain seen).
        private static IssuedTarget[] _issued = new IssuedTarget[64];
        // Session latch for the first-fire line (built only once).
        private static bool _firstFireLogged;
        private static int _lookupUnitCount = -1;
        private static int _lookupFrame = -1;

        /// <summary>Per-tick seams and settings for an executor.</summary>
        internal static void BindForTick(OrderExecutor executor)
        {
            executor.Bind(Guard, Chain, null, WaypointValidatorRuntime.Instance);
            WaypointValidatorRuntime.TryMapSize(out float sizeX, out float sizeZ);
            executor.SetMapSize(sizeX, sizeZ);
            float holdSeconds = Plugin.BrigadeTaskHoldSeconds != null ? Plugin.BrigadeTaskHoldSeconds.Value : 12f;
            executor.TaskHoldHours = TacticalBattleClock.RealSecondsAtOneXToHours(holdSeconds);
        }

        /// <summary>
        /// Issues a planned winner to every chain target and records it. Returns the
        /// number of targets the vanilla call reached.
        /// </summary>
        internal static int Execute(
            BattleUnits bunits,
            IBattleUnitView view,
            OrderExecutor executor,
            in BattleOrderIntent winner,
            in ExecutionPlan plan,
            double nowHours)
        {
            if (bunits == null || !plan.Execute) return 0;
            UnitRecord[] own = view.Own;
            int groupIndex = view.IndexOfOwn(winner.GroupId);
            if (groupIndex < 0) return 0;

            int executed = 0;
            int issued = 0;
            int queuedBefore = 0, pathsBefore = 0, queuedAfter = 0, pathsAfter = 0;
            if (_issued.Length < plan.Targets.Count) _issued = new IssuedTarget[Math.Max(plan.Targets.Count, _issued.Length * 2)];
            for (int t = 0; t < plan.Targets.Count; t++)
            {
                int targetIndex = plan.Targets.TargetIndices[t];
                if (targetIndex < 0 || targetIndex >= view.OwnCount) continue;
                if (!TryGetRegiment(own[targetIndex].InstanceId, out Regiment unit)) continue;
                if (!TacticalWriteGuard.CanWrite(unit)) continue;

                float x = plan.X;
                float z = plan.Z;
                if (plan.Targets.FannedOut)
                {
                    WaypointValidatorRuntime.TryMapSize(out float sizeX, out float sizeZ);
                    OrderExecutor.OffsetTarget(own[groupIndex], own[targetIndex], plan.X, plan.Z, sizeX, sizeZ, out x, out z);
                }

                bool wrote;
                if (winner.Channel == OrderChannel.Move)
                {
                    Vector3 waypointBefore = unit.lastsetwaypointposition;
                    _issued[issued++] = new IssuedTarget
                    {
                        InstanceId = own[targetIndex].InstanceId, X = x, Z = z,
                        WaypointBeforeX = waypointBefore.x, WaypointBeforeZ = waypointBefore.z,
                    };
                    queuedBefore += QueuedSignal(unit);
                    pathsBefore += MovingUnits(unit);
                    wrote = IssueMove(bunits, unit, winner, plan.UseOrderDelay, x, z);
                    queuedAfter += QueuedSignal(unit);
                    pathsAfter += MovingUnits(unit);
                }
                else
                {
                    wrote = IssueOther(bunits, unit, winner, nowHours);
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
                executor.RecordIssued(recorded, nowHours, _issued, issued, queuedBefore, pathsBefore, queuedAfter, pathsAfter);
            }

            if (executed > 0 && !_firstFireLogged)
            {
                _firstFireLogged = true;
                OnceLog.Info("battle-core:arbiter-first-fire",
                    "[OrderArbiter] first vanilla order issued channel=" + winner.Channel + " source=" + winner.Source);
            }

            if (OrderExecutor.ShouldDispatch(plan, executed) && DispatchEmitter != null &&
                TryGetRegiment(winner.GroupId, out Regiment ancestor))
            {
                try
                {
                    DispatchEmitter.Emit(ancestor, plan.DispatchType, plan.X, plan.Z, winner.Heading, winner.Reason);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("battle-core:dispatch-failed",
                        "[OrderArbiter] W&L dispatch threw: " + ex.GetType().Name + " " + ex.Message);
                }
            }

            return executed;
        }

        /// <summary>
        /// D4: a patch acting inside a vanilla micro call (#56 courier parity, #42
        /// feud replay) runs its intent through the same pipeline, refused under a
        /// higher-priority arbiter claim on the same (group, channel).
        /// </summary>
        internal static bool ExecuteInline(BattleUnits bunits, Regiment group, in BattleOrderIntent intent, out OrderOutcome outcome)
        {
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

                if (!BattleCore.TryGetSnapshot(alliance, out BattleSnapshot snapshot)) { outcome.Reason = SkipReason.Inactive; return false; }
                IBattleUnitView view = BattleCoreTick.ViewOf(snapshot);
                if (view == null) { outcome.Reason = SkipReason.Inactive; return false; }

                double now = TacticalDoctrineTime.NowBattleHours;
                outcome.AtHours = now;
                BindForTick(executor);
                ExecutionPlan plan = executor.Plan(view, intent, now);
                if (!plan.Execute)
                {
                    outcome.Kind = plan.Skip == SkipReason.Duplicate ? OrderOutcomeKind.Deduped : OrderOutcomeKind.Skipped;
                    outcome.Reason = plan.Skip;
                    return false;
                }

                int executed = Execute(bunits, view, executor, intent, plan, now);
                outcome.Kind = executed > 0 ? OrderOutcomeKind.Issued : OrderOutcomeKind.Skipped;
                return executed > 0;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("battle-core:inline-failed",
                    "[OrderArbiter] ExecuteInline threw: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static bool IssueMove(BattleUnits bunits, Regiment unit, in BattleOrderIntent w, bool useOrderDelay, float x, float z)
        {
            Vector3 target = TacticalTerrainProbe.WithTerrainHeight(new Vector3(x, 0f, z));
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
            return true;
        }

        private static bool IssueOther(BattleUnits bunits, Regiment unit, in BattleOrderIntent w, double nowHours)
        {
            switch (w.Channel)
            {
                case OrderChannel.Stance:
                    return TacticalStanceWriter.EnsureStance(bunits, unit, w.Value, w.Reason, w.Source);
                case OrderChannel.CombatBehavior:
                    return TacticalCombatBehaviorWriter.EnsureBehavior(bunits, unit, w.Value, (w.Flags & IntentFlags.PerRegiment) != 0);
                case OrderChannel.Formation:
                    return IssueFormation(bunits, unit, w.Value, nowHours);
                case OrderChannel.Facing:
                    return IssueFacing(bunits, unit, w.Heading, nowHours);
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

        private static bool IssueFormation(BattleUnits bunits, Regiment unit, int formation, double nowHours)
        {
            GameObject go = ((Component)unit).gameObject;
            if (go == null || formation < 0) return false;
            if (unit.unittyp <= TacticalUnitType.MaxCombat)
            {
                bunits.SetFormation(go, formation, exitifsameformation: true);
                return true;
            }

            FormationWriteDecision decision = CommandFormationWritePolicy.Decide(new FormationWriteInput(
                needsFormation: true,
                needsFacing: false,
                needsRefuse: false,
                unit.combatstatussubordinates,
                unit.groupsubordinatesmoving,
                unit.regimentpaths,
                unit.covervaluesubordinates,
                (float)nowHours,
                lastWriteHours: -1f,
                unit.groupenemiesinrange));
            if (decision.Kind != FormationWriteKind.VanillaFormation) return false;
            int index = CommandFormationWritePolicy.VanillaSetFormationIndexForGroup(formation);
            if (index < 0) return false;
            bunits.SetFormation(go, index, exitifsameformation: true);
            return true;
        }

        private static bool IssueFacing(BattleUnits bunits, Regiment unit, float heading, double nowHours)
        {
            if (heading < 0f) return false;
            if (unit.unittyp <= TacticalUnitType.MaxCombat) return RotateInPlace(unit, heading);

            FormationWriteDecision decision = CommandFormationWritePolicy.Decide(new FormationWriteInput(
                needsFormation: false,
                needsFacing: true,
                needsRefuse: false,
                unit.combatstatussubordinates,
                unit.groupsubordinatesmoving,
                unit.regimentpaths,
                unit.covervaluesubordinates,
                (float)nowHours,
                lastWriteHours: -1f,
                unit.groupenemiesinrange));
            switch (decision.Kind)
            {
                case FormationWriteKind.RotateInPlace:
                {
                    int rotated = 0;
                    Regiment[] units = unit.allattachedunits;
                    if (units == null) return false;
                    for (int i = 0; i < units.Length; i++)
                    {
                        Regiment child = units[i];
                        if (child == null || child.unittyp > TacticalUnitType.MaxCombat) continue;
                        if (child.unittyp == TacticalUnitType.Artillery) continue;
                        if (child.permanentlydetached) continue;
                        if (RotateInPlace(child, heading)) rotated++;
                    }
                    return rotated > 0;
                }
                case FormationWriteKind.WheelGroup:
                {
                    int current = unit.groupformation;
                    if (current < 0 || current > 4) return false;
                    bunits.SetGroupFormation(
                        unit,
                        current,
                        manualfinalrotation: heading,
                        targetpos: default(Vector3),
                        immediateplacement: false,
                        newpath: false,
                        modifylastwaypoint: true,
                        newstate: 2,
                        refuseflank: -1,
                        ignoredeplyomentzone: false,
                        skiprotation: false,
                        showmovementoptions: false,
                        placeentrenchments: false,
                        adjustbyterrainshape: true);
                    return true;
                }
                default:
                    return false;
            }
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

        // Courier orders in flight for the unit: its own queue (a root unit queues
        // on itself, 91500) plus every unit under it that a queued move or behavior
        // order is addressed to (ordertypeactive[0|1], 124994-124999).
        private static int QueuedSignal(Regiment unit)
        {
            int queued = unit.orderqueue != null ? unit.orderqueue.Count : 0;
            if (HasActiveMoveOrder(unit)) queued++;
            Regiment[] units = unit.allattachedunits;
            if (units == null) return queued;
            for (int i = 0; i < units.Length; i++)
                if (units[i] != null && HasActiveMoveOrder(units[i])) queued++;
            return queued;
        }

        private static bool HasActiveMoveOrder(Regiment unit)
        {
            bool[] active = unit.ordertypeactive;
            return active != null && active.Length > 1 && (active[0] || active[1]);
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

        internal static bool TryGetRegiment(int instanceId, out Regiment unit)
        {
            if (instanceId != 0 && _regiments.TryGetValue(instanceId, out unit) && (UnityEngine.Object)unit != null)
                return true;
            unit = null;
            if (instanceId == 0) return false;

            var list = BattleUnits.completeunitlist;
            int count = list != null ? list.Count : 0;
            int frame = UnityEngine.Time.frameCount;
            if (count == _lookupUnitCount && frame == _lookupFrame) return false;
            _lookupUnitCount = count;
            _lookupFrame = frame;
            _regiments.Clear();
            for (int i = 0; i < count; i++)
            {
                Regiment r = list[i];
                if ((UnityEngine.Object)r == null) continue;
                _regiments[r.GetInstanceID()] = r;
                GameObject go = ((Component)r).gameObject;
                if (go != null) _regiments[go.GetInstanceID()] = r;
            }
            return _regiments.TryGetValue(instanceId, out unit) && (UnityEngine.Object)unit != null;
        }

        private static void Reset()
        {
            _regiments.Clear();
            _lookupUnitCount = -1;
            _lookupFrame = -1;
        }
    }
}
