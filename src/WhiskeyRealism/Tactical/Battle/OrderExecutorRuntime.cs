using System;
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
                    RecordWaypointsBefore(unit, issued, ref waypoints);
                    _issued[issued++] = new IssuedTarget { InstanceId = own[targetIndex].InstanceId, X = x, Z = z };
                    pathsBefore += MovingUnits(unit);
                    wrote = IssueMove(bunits, unit, winner, plan.UseOrderDelay, x, z);
                    pathsAfter += MovingUnits(unit);
                }
                else
                {
                    wrote = IssueOther(bunits, unit, winner, feudReplay, nowHours);
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

                if (!BattleCore.TryGetSnapshot(alliance, out BattleSnapshot view)) { outcome.Reason = SkipReason.Inactive; return false; }

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
                OnceLog.Warning("tactical-battle-core:inline-failed",
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

        private static bool IssueOther(BattleUnits bunits, Regiment unit, in BattleOrderIntent w, bool feudReplay, double nowHours)
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
                    return IssueFacing(bunits, unit, w.Heading, feudReplay, nowHours);
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

        private static bool IssueFacing(BattleUnits bunits, Regiment unit, float heading, bool feudReplay, double nowHours)
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
                        // allattachedunits holds every descendant (129574), and a
                        // detached player command shields only its own node, so each
                        // rotated regiment passes the guard itself (BC3 136b445).
                        if (!TacticalWriteGuard.CanWrite(child, feudReplay)) continue;
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

        private static void Reset()
        {
            _firstFireLogged = false;
        }
    }
}
