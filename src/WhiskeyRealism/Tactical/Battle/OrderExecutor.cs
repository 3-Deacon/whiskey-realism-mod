using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // NavMesh area costs are global and Regiment.AddPath rewrites them for every
    // path (130364-130394), so a Whiskey NavMesh.CalculatePath otherwise runs
    // on whatever the last AddPath left (Roads/Bridges 1 or 2, fences 2 or
    // 500). These are AddPath's battle values for a normal move: roads
    // allowed, path not interrupted.
    public static class VanillaPathAreaCosts
    {
        public const string RoadsAreaName = "Roads";
        public const string BridgesAreaName = "Bridges";
        public const string FencesWallsAreaName = "FencesWalls, Sea (C)";
        public const string RiversAreaName = "Rivers";
        public const float Roads = 1f;
        public const float Bridges = 1f;
        public const float FencesWalls = 2f;

        public static float Rivers(bool frozen)
        {
            return frozen ? 2f : 500000f;
        }
    }

    // The map clamp vanilla applies to every path target in
    // Regiment.RegimentSetPath (131054-131071): x in [1, size.x - 2],
    // z in [-(size.z - 2), -1] (battle maps run along negative z).
    public static class VanillaMapBounds
    {
        public static void ClampXZ(float x, float z, float sizeX, float sizeZ, out float clampedX, out float clampedZ)
        {
            clampedX = x;
            clampedZ = z;
            if (sizeX <= 3f || sizeZ <= 3f) return;
            if (clampedX < 1f) clampedX = 1f;
            if (clampedX > sizeX - 2f) clampedX = sizeX - 2f;
            if (clampedZ > -1f) clampedZ = -1f;
            if (clampedZ < -sizeZ + 2f) clampedZ = -sizeZ + 2f;
        }
    }

    // Battle clock helpers. Doctrine timers run on the doctrine clock
    // (TacticalBattleCoordinator.DoctrineBattleHours), which follows
    // GameVars.currenttimefromstart — in battle game hours (uniStormSystem.Hour +
    // battlepasseddays * 24, 86379 / 93545) — frozen overnight. The macro AI
    // cycle is sidestatupdatecycle = 0.015 game-hours ≈ 9 s real at 1x, so
    // 1 game-hour ≈ 600 s real at 1x; old wall-clock constants tuned at 1x
    // convert with RealSecondsAtOneXToHours.
    public static class TacticalBattleClock
    {
        public const float RealSecondsPerBattleHourAtOneX = 600f;
        public const float BattleSecondsPerHour = 3600f;

        public static float HoursToBattleSeconds(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours) || hours <= 0f) return 0f;
            return hours * BattleSecondsPerHour;
        }

        public static float RealSecondsAtOneXToHours(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return 0f;
            return seconds / RealSecondsPerBattleHourAtOneX;
        }
    }

    public static class CommandWaypointWritePolicy
    {
        private const float MovementFlagThreshold = 0.05f;

        // B-61clock (2026-10-14): vanilla AI group moves go through the courier
        // queue — UpdateMovingTargets (6959), RepositionAIGroupMicroTowardsEnemy
        // (4146), CheckDefensiveFacing (4768) and feud attacks (4957) all pass
        // useorderdelay: true. Vanilla skips the delay only for in-contact
        // reactions: AssaultMove (3643), MarchToSoundOfGuns (3763), the
        // CheckIfOutflanked retreat (6235) and CheckForSeparatedEnemy (8188).
        // The executor's only in-contact reaction is a withdrawal under contact
        // or flank risk; everything else is a planned group move.
        public static bool ShouldUseOrderDelayForExecutorWaypoint(
            CommandTaskType task,
            bool battleActive,
            bool inContactWithdrawal = false)
        {
            if (!battleActive) return true;
            if (task == CommandTaskType.FallBackToLine && inContactWithdrawal) return false;
            return true;
        }

        // A move was issued when the group (or a subordinate) gained paths or a
        // courier order was queued (SetWaypoint → AddToOrderQueue, 91470-91500).
        public static bool MovementIssued(int movingPathsBefore, int movingPathsAfter, int queuedBefore, int queuedAfter)
        {
            return movingPathsAfter > movingPathsBefore || queuedAfter > queuedBefore;
        }

        // M3: a write that did not materialize backs off on battle time instead
        // of retrying every macro cycle.
        public const float FailedWriteBackoffHours = 0.05f;

        public static float FailedWriteBackoffUntil(float nowHours)
        {
            return nowHours + FailedWriteBackoffHours;
        }

        public static bool IsBackedOff(float nowHours, float backoffUntilHours)
        {
            if (backoffUntilHours <= 0f) return false;
            if (nowHours < backoffUntilHours - FailedWriteBackoffHours) return false;
            return nowHours < backoffUntilHours;
        }

        public static bool ShouldUseBlockedMovingOrderForExecutorWaypoint(
            CommandTaskType task,
            float distance,
            float threshold,
            bool hasBlockedCrossingData,
            bool battleActive)
        {
            if (!battleActive) return false;
            if (!hasBlockedCrossingData) return false;
            if (!IsFinite(distance) || !IsFinite(threshold)) return false;
            if (threshold <= 0f || distance <= threshold) return false;

            switch (task)
            {
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.AdvanceToAssembly:
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldStampGroupMovementForExecutorWaypoint(CommandTaskType task)
        {
            return IsExecutorMovementTask(task);
        }

        public static bool IsMovementTask(CommandTaskType task)
        {
            return IsExecutorMovementTask(task);
        }

        public static bool IsExecutorMovementActive(
            bool pathInterrupted,
            int regimentPaths,
            int movementMode,
            float groupSubordinatesMoving,
            float groupSubordinatesMovingNonAi,
            bool hasLastWaypoint,
            float distanceToLastWaypoint,
            float minWaypointDistance)
        {
            if (pathInterrupted) return false;
            if (regimentPaths <= 0) return false;
            if (movementMode > 0) return true;
            if (groupSubordinatesMoving > MovementFlagThreshold ||
                groupSubordinatesMovingNonAi > MovementFlagThreshold)
                return true;
            if (!hasLastWaypoint) return false;
            if (!IsFinite(distanceToLastWaypoint) || !IsFinite(minWaypointDistance)) return false;
            return distanceToLastWaypoint > minWaypointDistance;
        }

        public static bool ShouldSkipDuplicateWaypoint(
            float currentWaypointX,
            float currentWaypointZ,
            float targetX,
            float targetZ,
            float tolerance,
            bool pathInterrupted)
        {
            return ShouldSkipDuplicateWaypoint(
                currentWaypointX,
                currentWaypointZ,
                targetX,
                targetZ,
                tolerance,
                pathInterrupted,
                regimentPaths: 1,
                activeMove: true,
                currentX: targetX,
                currentZ: targetZ);
        }

        public static bool ShouldSkipDuplicateWaypoint(
            float currentWaypointX,
            float currentWaypointZ,
            float targetX,
            float targetZ,
            float tolerance,
            bool pathInterrupted,
            int regimentPaths,
            bool activeMove,
            float currentX,
            float currentZ)
        {
            // Microshift fix (2026-05-21): the old "if (pathInterrupted) return false" bypass
            // unconditionally allowed re-writes whenever vanilla flagged the path as
            // interrupted (which happens frequently during active combat: terrain, friendly
            // unit-in-way, momentary path-recompute). Each re-write triggered vanilla's
            // pathfinder to recompute, causing the visible "brief move and revert" microshift
            // for units that were logically pursuing the same target across many cycles.
            //
            // New behavior: even when pathInterrupted, still apply distance-based dedup —
            // if the new target is within InterruptedReissueDistance of the current waypoint,
            // skip the re-write and let vanilla resolve the interrupt on its own path. Only
            // a TRUE target relocation (genuinely different objective) bypasses dedup.
            if (!IsFinite(currentWaypointX) || !IsFinite(currentWaypointZ)) return false;
            if (!IsFinite(targetX) || !IsFinite(targetZ)) return false;
            if (currentWaypointX == 0f && currentWaypointZ == 0f) return false;

            float safeTolerance = IsFinite(tolerance) && tolerance > 0f ? tolerance : 1f;
            float dx = currentWaypointX - targetX;
            float dz = currentWaypointZ - targetZ;
            float distanceSq = (dx * dx) + (dz * dz);

            // When path is interrupted, use a larger threshold so genuinely-different
            // targets still bypass dedup but probe-drift (~20-50m off the existing target)
            // does not. InterruptedReissueDistance is intentionally larger than tolerance
            // because path interrupt is signal that vanilla wants a fresh decision; we
            // honor that for real relocations.
            float effectiveTolerance = pathInterrupted
                ? (safeTolerance > InterruptedReissueDistance ? safeTolerance : InterruptedReissueDistance)
                : safeTolerance;
            if (distanceSq > effectiveTolerance * effectiveTolerance) return false;

            if (!activeMove && regimentPaths <= 0 && IsFinite(currentX) && IsFinite(currentZ))
            {
                float currentDx = currentX - targetX;
                float currentDz = currentZ - targetZ;
                if ((currentDx * currentDx) + (currentDz * currentDz) > effectiveTolerance * effectiveTolerance)
                    return false;
            }

            return true;
        }

        // Microshift fix tunable. When pathInterrupted is set, the new-target must be at
        // least this far from the current waypoint before dedup releases. Calibrated to
        // ~3x the standard MinWaypointDistance so probe targets that drift by tens of
        // meters as a unit moves still resolve to "same logical target" rather than
        // triggering a new path recompute every cycle.
        private const float InterruptedReissueDistance = 60f;

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsExecutorMovementTask(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.Scout:
                case CommandTaskType.Probe:
                case CommandTaskType.Screen:
                case CommandTaskType.FallBackToLine:
                case CommandTaskType.ReleaseReserve:
                case CommandTaskType.RecoverStuckOrder:
                    return true;
                default:
                    return false;
            }
        }
    }

    // The BattleSnapshot members the arbiter pipeline reads. BattleSnapshot
    // implements it; the harness drives the pure pipeline through an array-backed
    // fixture.
    public interface IBattleUnitView
    {
        int Alliance { get; }
        int OwnCount { get; }
        UnitRecord[] Own { get; }
        int IndexOfOwn(int instanceId);
        bool IsAncestorOf(int ancestorIndex, int descendantIndex);
        int ObservationCount { get; }
        EnemyObservation[] Observations { get; }
    }

    // Step 1's write guard. The runtime binds TacticalWriteGuard.CanWrite(in
    // UnitRecord, out SkipReason) (mode, PlayerOwned, Feud); the guard reads the
    // Tactical Commander Mode, which the pure pipeline cannot.
    public interface IOrderGuard
    {
        bool CanWrite(in UnitRecord unit, out SkipReason reason);
    }

    // One unit a Move went to and its target (a fanned-out child keeps its offset).
    public struct IssuedTarget
    {
        public int InstanceId;
        public float X, Z;
    }

    // The lastsetwaypointposition of a unit the outcome check will inspect (an
    // issued target or one of its descendants), read just before the vanilla
    // call. Target indexes the IssuedTarget it belongs to.
    public struct UnitWaypoint
    {
        public int InstanceId;
        public int Target;
        public float X, Z;
    }

    public struct ExecutionPlan
    {
        public bool Execute;
        public SkipReason Skip;
        // TargetIndices points into the executor's buffer: valid until its next Plan call.
        public ChainResolution Targets;
        public float X, Z;
        public bool UseOrderDelay;
        public int DispatchType;
    }

    // The single write-path pipeline (plan BC4 steps 1-6, 8; step 7 is
    // OrderExecutorRuntime). Per winner: guard → chain → waypoint validation →
    // silent-exit pre-check → dedupe → cooldown. Bookkeeping (recent orders, task
    // hold, courier cadence, failed backoff, pending moves, retries, last outcome)
    // is write-path state owned here, not doctrine state (D4).
    //
    // Outcomes (D6): there is no courier wait to observe. Under useorderdelay
    // SetWaypoint still queues the courier (AddToOrderQueue 91483-91510) but goes
    // on to SetGroupFormation / RegimentSetPath in the same call (91540/91559 with
    // newpath:false), and AddPath adds the path at once (regimentpaths++ 130519)
    // with pathstatus = 0, waiting for the courier (131243-131246). So a move is
    // Moved when the moving-unit count of its targets rose at the call or by the
    // next observation. A group that was already moving keeps its count when
    // redirected, and AddToOrderQueue is skipped while an order of that type is
    // in flight (91490/91502), so a move is also Moved once vanilla took the
    // target: a target unit's (or a descendant's) last waypoint moved onto it
    // within DedupeMeters. SetGroupFormation stamps lastsetwaypointposition = target on
    // every group move (92067; 91559 runs with newpath:false under the courier
    // delay too), RegimentSetPath on a regiment (131254/131258). That is "taken",
    // not "delivered". A unit whose waypoint was already on the target before the
    // call proves nothing and is ignored (every inspected unit's pre-call
    // waypoint is recorded). A move with none of these hit a silent vanilla exit
    // (SetWaypoint 91232-91640, which returns before the stamp) and is Failed at
    // the next observation. A failure retries once with the bearing rotated +30°,
    // else −30° when +30° fails validation, then gives up; the source reads
    // TryGetLastOutcome.
    public sealed class OrderExecutor
    {
        // = #61 RecentOrderHours, RealSecondsAtOneXToHours(30) = 0.05 h
        // (BattleCommandPostureExecutorPatch.cs:30). Spans ~3 macro cycles
        // (sidestatupdatecycle 0.015 h); 30 battle-s would expire inside one.
        public const float RecentOrderBattleSeconds = 180f;
        // Per-channel recent-order windows, #61's shipped values in battle seconds
        // (RealSecondsAtOneXToHours: 600 real s at 1x per battle hour):
        //   Move, Stance, Stop: RecentOrderBattleSeconds (#61 RecentOrderHours).
        //   Facing: #61 RegimentFacingPulseCooldownHours = RealSecondsAtOneXToHours(5)
        //     = 5/600 h = 30 battle-s.
        //   Formation: CommandFormationWritePolicy.CooldownHours 0.05 h = 180 battle-s;
        //     the policy's own 0.1 h under fire still applies at execution
        //     (OrderExecutorRuntime feeds it the last formation/facing write).
        //   CombatBehavior: #61 FireControlOrderCooldownHours =
        //     RealSecondsAtOneXToHours(45) = 0.075 h = 270 battle-s.
        public const float FacingRecentOrderBattleSeconds = 30f;
        public const float FormationRecentOrderBattleSeconds = CommandFormationWritePolicy.CooldownHours * TacticalBattleClock.BattleSecondsPerHour;
        public const float CombatBehaviorRecentOrderBattleSeconds = 270f;

        // Per-source overrides (0 = no executor window): the artillery doctrine
        // holds each battery itself on the doctrine clock
        // (Plugin.ArtilleryAuditMinHoldSeconds, BC5b), so its combat behavior,
        // formation and facing carry no executor window; the charge doctrine's
        // charge/revert is stateful (BC6a deferred disarm, refusal back-off) and
        // must not wait the fire-control window to revert.
        public static float RecentOrderBattleSecondsFor(OrderChannel channel, IntentSource source)
        {
            switch (source)
            {
                case IntentSource.ArtilleryDoctrine:
                case IntentSource.ArtilleryPointBlank:
                    if (channel == OrderChannel.CombatBehavior || channel == OrderChannel.Formation || channel == OrderChannel.Facing)
                        return 0f;
                    break;
                case IntentSource.ChargeDoctrine:
                    if (channel == OrderChannel.CombatBehavior) return 0f;
                    break;
            }

            switch (channel)
            {
                case OrderChannel.Facing: return FacingRecentOrderBattleSeconds;
                case OrderChannel.Formation: return FormationRecentOrderBattleSeconds;
                case OrderChannel.CombatBehavior: return CombatBehaviorRecentOrderBattleSeconds;
                default: return RecentOrderBattleSeconds;
            }
        }

        /// <summary>SetGroupFormation's refuseflank: -1 none, 0 left, 1 right.</summary>
        public static int VanillaRefuseFlank(RefuseFlank refuse)
        {
            switch (refuse)
            {
                case RefuseFlank.Left: return 0;
                case RefuseFlank.Right: return 1;
                default: return -1;
            }
        }
        public const float CourierIntervalBattleHours = 0.25f;      // was #61 OutboundCourierIntervalSeconds 900
        public const float FailedWriteBackoffHours = 0.05f;
        public const float RetryBearingDegrees = 30f;
        public const int MaxRetries = 1;
        // = #61 FacingRefreshToleranceDegrees.
        public const float FacingToleranceDegrees = 15f;
        public const int NoDispatch = TacticalDispatchTypeMapper.None;
        // groupstancename (65061): 4 = assault.
        private const int AssaultStance = 4;
        private const float RetryTicketMatchMeters = 1f;

        private static readonly OrderExecutor[] _byAlliance = { new OrderExecutor(), new OrderExecutor() };

        static OrderExecutor()
        {
            BattleScope.Register("order-executor", ResetAll);
        }

        private IOrderGuard _guard;
        private ITerrainProbe _terrain;
        private float _sizeX;
        private float _sizeZ;

        private readonly Dictionary<long, double> _lastOrderAt = new Dictionary<long, double>(256);
        private readonly Dictionary<int, TaskStamp> _taskHold = new Dictionary<int, TaskStamp>(128);
        private readonly Dictionary<int, double> _courierAt = new Dictionary<int, double>(128);
        private readonly Dictionary<int, double> _backoffUntil = new Dictionary<int, double>(128);
        private readonly Dictionary<long, OrderOutcome> _lastOutcome = new Dictionary<long, OrderOutcome>(256);
        private PendingMove[] _pending = new PendingMove[32];
        // Parallel to _pending, pooled per slot: the units each pending move went
        // to, and the units whose waypoint was already on their target before it.
        private IssuedTarget[][] _pendingTargets = new IssuedTarget[32][];
        private int[][] _pendingAlreadyThere = new int[32][];
        private int _pendingCount;
        private RetryTicket[] _tickets = new RetryTicket[8];
        private int _ticketCount;
        private int[] _targetBuffer = new int[64];
        private int[] _childBuffer = new int[64];

        /// <summary>
        /// Same-task re-issue hold for Move (was #61 _brigadeTaskHoldState). The runtime
        /// sets it from Plugin.BrigadeTaskHoldSeconds (real seconds at 1x).
        /// </summary>
        public float TaskHoldHours { get; set; } = TacticalBattleClock.RealSecondsAtOneXToHours(12f);

        /// <summary>Bound-checked: alliances 0 and 1 only; Europe (2) and anything else → null.</summary>
        public static OrderExecutor ForAlliance(int alliance)
        {
            if (alliance < 0 || alliance >= _byAlliance.Length) return null;
            return _byAlliance[alliance];
        }

        /// <summary>
        /// Seams for this executor. A null guard fails closed (every plan is
        /// ModeNotActive); a null terrain probe skips the water checks.
        /// </summary>
        public void Bind(IOrderGuard guard, ITerrainProbe terrain)
        {
            _guard = guard;
            _terrain = terrain;
        }

        /// <summary>Terrain.activeTerrain.terrainData.size x/z (0 = unknown: no clamp).</summary>
        public void SetMapSize(float sizeX, float sizeZ)
        {
            _sizeX = sizeX;
            _sizeZ = sizeZ;
        }

        public ExecutionPlan Plan(IBattleUnitView s, in BattleOrderIntent winner, double nowHours)
        {
            return PlanCore(s, winner, nowHours, retry: false);
        }

        /// <summary>
        /// Plan for a retry handed out by TakeRetries: the failed-write backoff, the
        /// recent-order cooldown and the task hold do not apply to it. Any other
        /// intent is planned like Plan.
        /// </summary>
        public ExecutionPlan PlanRetry(IBattleUnitView s, in BattleOrderIntent retry, double nowHours)
        {
            return PlanCore(s, retry, nowHours, retry: FindTicket(retry.GroupId, retry.TargetX, retry.TargetZ) >= 0);
        }

        private ExecutionPlan PlanCore(IBattleUnitView s, in BattleOrderIntent w, double nowHours, bool retry)
        {
            var plan = new ExecutionPlan { DispatchType = NoDispatch };
            if (_guard == null) return Skip(plan, SkipReason.ModeNotActive);
            int idx = s.IndexOfOwn(w.GroupId);
            if (idx < 0) return Skip(plan, SkipReason.Inactive);
            UnitRecord[] own = s.Own;
            ref UnitRecord rec = ref own[idx];

            // 1. Guard. PlayerOwned is never exempt; the Feud rule exempts only
            //    #42's replay of vanilla's own feud move.
            if ((rec.Flags & UnitFlags.PlayerOwned) != 0) return Skip(plan, SkipReason.PlayerOwned);
            bool feudReplay = w.Source == IntentSource.FeudReplay;
            if ((rec.Flags & UnitFlags.Feud) != 0 && !feudReplay) return Skip(plan, SkipReason.Feud);
            if (!_guard.CanWrite(rec, out SkipReason guardReason) && !(guardReason == SkipReason.Feud && feudReplay))
                return Skip(plan, guardReason == SkipReason.None ? SkipReason.ModeNotActive : guardReason);

            // 2. Chain.
            EnsureCapacity(s.OwnCount);
            plan.Targets = new ChainResolution { TargetIndices = _targetBuffer };
            TacticalChainOrderPlanner.ResolveTargets(own, s.OwnCount, idx, _childBuffer, ref plan.Targets);
            if (plan.Targets.Count <= 0) return Skip(plan, SkipReason.ChainNoTargets);

            // 3. Waypoint validation (Move, and Facing when it carries a point).
            float x = w.TargetX;
            float z = w.TargetZ;
            if (w.Channel == OrderChannel.Move || (w.Channel == OrderChannel.Facing && HasPoint(w.TargetX, w.TargetZ)))
            {
                SkipReason invalid = WaypointValidator.Validate(s, idx, w, _sizeX, _sizeZ, _terrain, out x, out z);
                if (invalid != SkipReason.None) return Skip(plan, invalid);
            }

            // 4. Silent-exit pre-check.
            SkipReason exit = SilentExitPrecheck.Check(rec, w.Channel);
            if (exit != SkipReason.None) return Skip(plan, exit);

            // 5. Dedupe. 6. Cooldown.
            if (IsDuplicate(rec, w, x, z)) return Skip(plan, SkipReason.Duplicate);
            if (!retry && InCooldown(rec, w, nowHours)) return Skip(plan, SkipReason.Cooldown);

            plan.Execute = true;
            plan.Skip = SkipReason.None;
            plan.X = x;
            plan.Z = z;
            plan.UseOrderDelay = UsesOrderDelay(w);
            // 9. The player's share of an ancestor's move goes out as one W&L
            //    dispatch per executed plan. Move only: the dispatch carries the
            //    move's target, and the Move cooldowns above (recent order, task
            //    hold, courier cadence, dedupe; a retry skips the first two once)
            //    are its throttle; Emit's 110 m record and vanilla's own repeat
            //    check (8643) sit behind it.
            if (w.Channel == OrderChannel.Move && plan.Targets.PlayerShareNeedsDispatch)
                plan.DispatchType = TacticalDispatchTypeMapper.Map(w.Task, EnemyObservedNear(s, x, z), rec.AiStanceOrdered == AssaultStance);
            return plan;
        }

        /// <summary>
        /// Bookkeeping after the vanilla call. For Move, <paramref name="targets"/>
        /// holds the units the call reached, <paramref name="waypointsBefore"/> the
        /// pre-call waypoints of those units and their descendants, and the counts
        /// are their moving units (unit or descendants with regimentpaths &gt; 0)
        /// summed live just before and after the calls.
        /// </summary>
        public void RecordIssued(in BattleOrderIntent i, double nowHours, IssuedTarget[] targets, int targetCount,
            UnitWaypoint[] waypointsBefore, int waypointCount, int pathsBefore, int pathsAfter)
        {
            long key = Key(i.GroupId, i.Channel);
            _lastOrderAt[key] = nowHours;
            SetLastOutcome(new OrderOutcome
            {
                GroupId = i.GroupId, Channel = i.Channel, Kind = OrderOutcomeKind.Issued, Source = i.Source, AtHours = nowHours,
            });
            if (i.Channel != OrderChannel.Move) return;

            if (i.Task != CommandTaskType.None) _taskHold[i.GroupId] = new TaskStamp(i.Task, nowHours);
            if (UsesOrderDelay(i)) _courierAt[i.GroupId] = nowHours;

            int retries = ConsumeTicket(i.GroupId, i.TargetX, i.TargetZ);
            int p = FindPending(i.GroupId);
            if (p < 0)
            {
                if (_pendingCount == _pending.Length)
                {
                    Array.Resize(ref _pending, _pending.Length * 2);
                    Array.Resize(ref _pendingTargets, _pending.Length);
                    Array.Resize(ref _pendingAlreadyThere, _pending.Length);
                }
                p = _pendingCount++;
            }

            int count = targets != null ? Math.Min(Math.Max(targetCount, 0), targets.Length) : 0;
            IssuedTarget[] slot = _pendingTargets[p];
            if (slot == null || slot.Length < count)
                _pendingTargets[p] = slot = new IssuedTarget[Math.Max(count, 4)];
            if (count > 0) Array.Copy(targets, slot, count);

            int waypoints = waypointsBefore != null ? Math.Min(Math.Max(waypointCount, 0), waypointsBefore.Length) : 0;
            int[] there = _pendingAlreadyThere[p];
            if (there == null || there.Length < waypoints)
                _pendingAlreadyThere[p] = there = new int[Math.Max(waypoints, 8)];
            int thereCount = 0;
            float r2 = OrderArbiter.DedupeMeters * OrderArbiter.DedupeMeters;
            for (int k = 0; k < waypoints; k++)
            {
                ref UnitWaypoint w = ref waypointsBefore[k];
                if (w.Target < 0 || w.Target >= count || !HasPoint(w.X, w.Z)) continue;
                if (DistSq(w.X, w.Z, slot[w.Target].X, slot[w.Target].Z) <= r2) there[thereCount++] = w.InstanceId;
            }

            _pending[p] = new PendingMove
            {
                Intent = i, PathsBefore = pathsBefore, MovedAtCall = pathsAfter > pathsBefore, Retries = retries,
                TargetCount = count, AlreadyThereCount = thereCount,
            };
        }

        /// <summary>
        /// Resolves last tick's moves against this tick's snapshot (D6) and writes the
        /// outcomes (Moved, Failed, Retried, GaveUp, Skipped) up to the
        /// buffer length. Every outcome also becomes the group's LastOutcome.
        /// </summary>
        public void ObserveOutcomes(IBattleUnitView s, double nowHours, OrderOutcome[] outcomes, out int count)
        {
            count = 0;
            int kept = 0;
            for (int t = 0; t < _ticketCount; t++)
            {
                if (_tickets[t].Handed) continue;
                _tickets[kept++] = _tickets[t];
            }
            _ticketCount = kept;

            for (int p = _pendingCount - 1; p >= 0; p--)
            {
                PendingMove pm = _pending[p];
                int group = pm.Intent.GroupId;
                int idx = s.IndexOfOwn(group);
                if (idx < 0)
                {
                    Emit(outcomes, ref count, pm, OrderOutcomeKind.Skipped, SkipReason.Inactive, nowHours, pm.Retries);
                    RemovePending(p);
                    continue;
                }

                IssuedTarget[] targets = _pendingTargets[p];
                if (pm.MovedAtCall ||
                    MovingUnits(s, targets, pm.TargetCount) > pm.PathsBefore ||
                    TargetTaken(s, targets, pm.TargetCount, _pendingAlreadyThere[p], pm.AlreadyThereCount))
                {
                    Emit(outcomes, ref count, pm, OrderOutcomeKind.Moved, SkipReason.None, nowHours, pm.Retries);
                    RemovePending(p);
                    continue;
                }

                Emit(outcomes, ref count, pm, OrderOutcomeKind.Failed, SkipReason.None, nowHours, pm.Retries);
                _backoffUntil[group] = nowHours + FailedWriteBackoffHours;
                RemovePending(p);
                if (pm.Retries < MaxRetries && TryRotatedTarget(s, idx, pm.Intent, out float rx, out float rz))
                {
                    AddTicket(pm.Intent, rx, rz, pm.Retries + 1);
                    Emit(outcomes, ref count, pm, OrderOutcomeKind.Retried, SkipReason.None, nowHours, pm.Retries + 1);
                }
                else
                {
                    Emit(outcomes, ref count, pm, OrderOutcomeKind.GaveUp, SkipReason.None, nowHours, pm.Retries);
                }
            }
        }

        /// <summary>
        /// Hands out the retry moves ObserveOutcomes created (each once). The caller
        /// plans them with PlanRetry, then the winners of the tick; a retry not
        /// executed this tick lapses at the next ObserveOutcomes.
        /// </summary>
        public int TakeRetries(BattleOrderIntent[] buffer)
        {
            int n = 0;
            int capacity = buffer != null ? buffer.Length : 0;
            for (int t = 0; t < _ticketCount && n < capacity; t++)
            {
                if (_tickets[t].Handed) continue;
                buffer[n++] = _tickets[t].Intent;
                _tickets[t].Handed = true;
            }
            return n;
        }

        public bool TryGetLastOutcome(int groupId, OrderChannel channel, out OrderOutcome outcome)
        {
            return _lastOutcome.TryGetValue(Key(groupId, channel), out outcome);
        }

        /// <summary>
        /// D4: an inline intent from a patch is refused when the last resolved
        /// arbiter claim on that (group, channel) outranks it.
        /// </summary>
        public static SkipReason InlineClaimCheck(OrderArbiter arbiter, in BattleOrderIntent intent)
        {
            if (arbiter == null) return SkipReason.None;
            if (arbiter.TryGetClaim(intent.GroupId, intent.Channel, out BattleOrderIntent claim) && claim.Priority > intent.Priority)
                return SkipReason.LowerPriorityThanClaim;
            return SkipReason.None;
        }

        /// <summary>
        /// A retry runs only while the source that failed still holds the (group,
        /// Move) claim this tick; any other winner, or none, supersedes it.
        /// </summary>
        public static bool RetryStillClaimed(OrderArbiter arbiter, in BattleOrderIntent retry)
        {
            return arbiter != null &&
                arbiter.TryGetClaim(retry.GroupId, OrderChannel.Move, out BattleOrderIntent claim) &&
                claim.Source == retry.Source &&
                claim.Priority == retry.Priority;
        }

        /// <summary>Step 9: one W&amp;L dispatch per executed plan, not per fanned-out target.</summary>
        public static bool ShouldDispatch(in ExecutionPlan plan, int executedTargets)
        {
            return plan.Execute && plan.DispatchType != NoDispatch && executedTargets > 0;
        }

        /// <summary>
        /// A fanned-out child keeps its offset from the ordered group, so the
        /// children arrive in the shape they hold instead of on one point.
        /// </summary>
        public static void OffsetTarget(in UnitRecord group, in UnitRecord child, float x, float z, float sizeX, float sizeZ, out float childX, out float childZ)
        {
            VanillaMapBounds.ClampXZ(x + child.X - group.X, z + child.Z - group.Z, sizeX, sizeZ, out childX, out childZ);
        }

        private bool IsDuplicate(in UnitRecord rec, in BattleOrderIntent w, float x, float z)
        {
            switch (w.Channel)
            {
                case OrderChannel.Move:
                {
                    // #61's rule (ShouldSkipDuplicateWaypoint at the 60 m
                    // MinWaypointDistance): same target while the group moves, or
                    // while it already stands there. Its wider radius for an
                    // interrupted path (InterruptedReissueDistance 60 m) equals the
                    // base radius since the 60 m bump, and the snapshot carries no
                    // pathinterrupted, so the interrupted flag is false here.
                    if (CommandWaypointWritePolicy.ShouldSkipDuplicateWaypoint(
                            rec.LastWaypointX, rec.LastWaypointZ, x, z, OrderArbiter.DedupeMeters,
                            pathInterrupted: false, rec.RegimentPaths, IsMoving(rec), rec.X, rec.Z))
                        return true;
                    int p = FindPending(w.GroupId);
                    return p >= 0 &&
                        DistSq(_pending[p].Intent.TargetX, _pending[p].Intent.TargetZ, x, z) <= OrderArbiter.DedupeMeters * OrderArbiter.DedupeMeters;
                }
                case OrderChannel.Stance:
                    // Never a duplicate: an already-ordered stance is a hold
                    // refresh. TacticalStanceWriter decides — its cheap path
                    // skips while vanilla's 4264 stamp is live, its full pass
                    // claims and re-stamps a lapsed one (the #45 agreement path).
                    return false;
                // A refused flank is a write of its own (the skewed slots), even
                // with the formation or facing already right.
                case OrderChannel.Formation:
                    return rec.Formation == w.Value && w.Refuse == RefuseFlank.None;
                case OrderChannel.CombatBehavior:
                    // A charge Disarm still has charge mode 3 to leave even when vanilla
                    // already re-ordered behavior 2 (cycle-6 cascade, 5631-5634).
                    if ((w.Flags & IntentFlags.LeaveChargeMode) != 0 && rec.MovementMode == ChargeDoctrine.ArmedMovementMode)
                        return false;
                    return rec.CombatBehaviorOrdered == w.Value;
                case OrderChannel.Facing:
                    return w.Heading >= 0f && AngleDifference(rec.RotationY, w.Heading) <= FacingToleranceDegrees &&
                        w.Refuse == RefuseFlank.None;
                case OrderChannel.Stop:
                    return !IsMoving(rec);
                default:
                    return false;
            }
        }

        private bool InCooldown(in UnitRecord rec, in BattleOrderIntent w, double now)
        {
            bool move = w.Channel == OrderChannel.Move;
            if (move && _backoffUntil.TryGetValue(w.GroupId, out double until) && now < until && now >= until - FailedWriteBackoffHours)
                return true;

            // A hold refresh of the stance the group already has is not a new
            // order (IsDuplicate): the recent-order cooldown does not apply.
            bool stanceRefresh = w.Channel == OrderChannel.Stance && rec.AiStanceOrdered == w.Value;
            if (w.Priority != IntentPriority.Safety && !stanceRefresh)
            {
                if (_lastOrderAt.TryGetValue(Key(w.GroupId, w.Channel), out double last) &&
                    Within(now, last, RecentOrderBattleSecondsFor(w.Channel, w.Source) / TacticalBattleClock.BattleSecondsPerHour))
                    return true;
                if (move && TaskHoldHours > 0f && w.Task != CommandTaskType.None &&
                    _taskHold.TryGetValue(w.GroupId, out TaskStamp stamp) && stamp.Task == w.Task &&
                    Within(now, stamp.AtHours, TaskHoldHours))
                    return true;
            }

            // Courier cadence (#61 TacticalOutboundCourierCadence): one courier move
            // per group per interval while the last one is still in flight.
            return move && UsesOrderDelay(w) && IsMoving(rec) &&
                _courierAt.TryGetValue(w.GroupId, out double courier) &&
                Within(now, courier, CourierIntervalBattleHours);
        }

        private bool TryRotatedTarget(IBattleUnitView s, int idx, in BattleOrderIntent failed, out float x, out float z)
        {
            ref UnitRecord rec = ref s.Own[idx];
            for (int sign = 1; sign >= -1; sign -= 2)
            {
                double a = sign * RetryBearingDegrees * Math.PI / 180.0;
                float vx = failed.TargetX - rec.X;
                float vz = failed.TargetZ - rec.Z;
                BattleOrderIntent rotated = failed;
                rotated.TargetX = rec.X + (float)(vx * Math.Cos(a) - vz * Math.Sin(a));
                rotated.TargetZ = rec.Z + (float)(vx * Math.Sin(a) + vz * Math.Cos(a));
                if (WaypointValidator.Validate(s, idx, rotated, _sizeX, _sizeZ, _terrain, out x, out z) == SkipReason.None)
                    return true;
            }
            x = 0f;
            z = 0f;
            return false;
        }

        // Vanilla took a target when the unit's or a descendant's last waypoint is
        // now within DedupeMeters of it and that unit's waypoint was not already
        // there before the call.
        private static bool TargetTaken(IBattleUnitView s, IssuedTarget[] targets, int count, int[] alreadyThere, int alreadyThereCount)
        {
            float r2 = OrderArbiter.DedupeMeters * OrderArbiter.DedupeMeters;
            UnitRecord[] own = s.Own;
            for (int t = 0; t < count; t++)
            {
                ref IssuedTarget target = ref targets[t];
                int idx = s.IndexOfOwn(target.InstanceId);
                if (idx < 0) continue;
                for (int j = 0; j < s.OwnCount; j++)
                {
                    if (j != idx && !s.IsAncestorOf(idx, j)) continue;
                    if (!HasPoint(own[j].LastWaypointX, own[j].LastWaypointZ) ||
                        DistSq(own[j].LastWaypointX, own[j].LastWaypointZ, target.X, target.Z) > r2)
                        continue;
                    if (Array.IndexOf(alreadyThere, own[j].InstanceId, 0, alreadyThereCount) < 0) return true;
                }
            }
            return false;
        }

        // Moving units (regimentpaths > 0) among the units a move went to and
        // their descendants: the set the runtime summed before the call.
        private static int MovingUnits(IBattleUnitView s, IssuedTarget[] targets, int count)
        {
            UnitRecord[] own = s.Own;
            int moving = 0;
            for (int t = 0; t < count; t++)
            {
                int idx = s.IndexOfOwn(targets[t].InstanceId);
                if (idx < 0) continue;
                for (int j = 0; j < s.OwnCount; j++)
                {
                    if (own[j].RegimentPaths <= 0) continue;
                    if (j == idx || s.IsAncestorOf(idx, j)) moving++;
                }
            }
            return moving;
        }

        private static bool EnemyObservedNear(IBattleUnitView s, float x, float z)
        {
            EnemyObservation[] obs = s.Observations;
            float r2 = WaypointValidator.FriendlyFootprintRadiusMeters * WaypointValidator.FriendlyFootprintRadiusMeters;
            for (int k = 0; k < s.ObservationCount; k++)
                if (DistSq(obs[k].X, obs[k].Z, x, z) < r2) return true;
            return false;
        }

        private void Emit(OrderOutcome[] outcomes, ref int count, in PendingMove pm, OrderOutcomeKind kind, SkipReason reason, double now, int retries)
        {
            var outcome = new OrderOutcome
            {
                GroupId = pm.Intent.GroupId, Channel = OrderChannel.Move, Kind = kind, Reason = reason,
                Source = pm.Intent.Source, AtHours = now, Retries = retries,
            };
            SetLastOutcome(outcome);
            if (outcomes != null && count < outcomes.Length) outcomes[count++] = outcome;
        }

        private void SetLastOutcome(in OrderOutcome outcome)
        {
            _lastOutcome[Key(outcome.GroupId, outcome.Channel)] = outcome;
        }

        private int FindPending(int groupId)
        {
            for (int p = 0; p < _pendingCount; p++)
                if (_pending[p].Intent.GroupId == groupId) return p;
            return -1;
        }

        // Swap-remove; the slot's target array moves with it so both stay pooled.
        private void RemovePending(int p)
        {
            int last = --_pendingCount;
            _pending[p] = _pending[last];
            IssuedTarget[] removed = _pendingTargets[p];
            _pendingTargets[p] = _pendingTargets[last];
            _pendingTargets[last] = removed;
            int[] removedThere = _pendingAlreadyThere[p];
            _pendingAlreadyThere[p] = _pendingAlreadyThere[last];
            _pendingAlreadyThere[last] = removedThere;
        }

        private void AddTicket(in BattleOrderIntent failed, float x, float z, int retries)
        {
            if (_ticketCount == _tickets.Length) Array.Resize(ref _tickets, _tickets.Length * 2);
            BattleOrderIntent intent = failed;
            intent.TargetX = x;
            intent.TargetZ = z;
            _tickets[_ticketCount++] = new RetryTicket { Intent = intent, Retries = retries };
        }

        private int FindTicket(int groupId, float x, float z)
        {
            float r2 = RetryTicketMatchMeters * RetryTicketMatchMeters;
            for (int t = 0; t < _ticketCount; t++)
            {
                if (_tickets[t].Intent.GroupId != groupId) continue;
                if (DistSq(_tickets[t].Intent.TargetX, _tickets[t].Intent.TargetZ, x, z) <= r2) return t;
            }
            return -1;
        }

        // The retry count of a recorded move: the ticket's when this is the retry,
        // else 0. Any recorded move of the group retires its ticket.
        private int ConsumeTicket(int groupId, float x, float z)
        {
            int retries = 0;
            int match = FindTicket(groupId, x, z);
            if (match >= 0) retries = _tickets[match].Retries;
            int kept = 0;
            for (int t = 0; t < _ticketCount; t++)
            {
                if (_tickets[t].Intent.GroupId == groupId) continue;
                _tickets[kept++] = _tickets[t];
            }
            _ticketCount = kept;
            return retries;
        }

        private void EnsureCapacity(int ownCount)
        {
            if (_targetBuffer.Length < ownCount)
            {
                int size = Math.Max(ownCount, _targetBuffer.Length * 2);
                _targetBuffer = new int[size];
                _childBuffer = new int[size];
            }
        }

        private void Reset()
        {
            _lastOrderAt.Clear();
            _taskHold.Clear();
            _courierAt.Clear();
            _backoffUntil.Clear();
            _lastOutcome.Clear();
            _pendingCount = 0;
            _ticketCount = 0;
        }

        private static void ResetAll()
        {
            for (int i = 0; i < _byAlliance.Length; i++) _byAlliance[i].Reset();
        }

        private static ExecutionPlan Skip(ExecutionPlan plan, SkipReason reason)
        {
            plan.Execute = false;
            plan.Skip = reason;
            return plan;
        }

        private static bool UsesOrderDelay(in BattleOrderIntent i)
        {
            return (i.Flags & IntentFlags.UseOrderDelay) != 0 && (i.Flags & IntentFlags.InContactWithdrawal) == 0;
        }

        private static bool IsMoving(in UnitRecord rec)
        {
            return rec.RegimentPaths > 0 || rec.QueuedOrders > 0;
        }

        // A stamp in the future (clock rewind: reload, new battle) has expired.
        private static bool Within(double now, double stamp, double window)
        {
            double age = now - stamp;
            return age >= 0.0 && age < window;
        }

        private static bool HasPoint(float x, float z)
        {
            return !(x == 0f && z == 0f) && !float.IsNaN(x) && !float.IsNaN(z);
        }

        private static float DistSq(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz;
        }

        private static float AngleDifference(float a, float b)
        {
            float d = Math.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        private static long Key(int groupId, OrderChannel channel)
        {
            return ((long)groupId << 3) | (long)(int)channel;
        }

        private struct PendingMove
        {
            public BattleOrderIntent Intent;
            public int PathsBefore;
            public int TargetCount;
            public int AlreadyThereCount;
            // The targets' moving-unit count rose during the vanilla call.
            public bool MovedAtCall;
            public int Retries;
        }

        private struct RetryTicket
        {
            public BattleOrderIntent Intent;
            public int Retries;
            public bool Handed;
        }

        private readonly struct TaskStamp
        {
            public TaskStamp(CommandTaskType task, double atHours)
            {
                Task = task;
                AtHours = atHours;
            }

            public CommandTaskType Task { get; }
            public double AtHours { get; }
        }
    }
}
