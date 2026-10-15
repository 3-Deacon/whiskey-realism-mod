using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Operations
{
    public enum PostureExecutionAction
    {
        NoWrite,
        SetFormation,
        SetWaypoint,
        SetFormationAndWaypoint,
        ChangeStance,
        ReleaseReserve,
        FallbackToLine,
        RecoverInterruptedOrder
    }

    public enum PostureExecutionTarget
    {
        None,
        CurrentPosition,
        AssemblyArea,
        ObjectiveApproach,
        ReserveArea,
        FallbackLine,
        RecoveryPath,
        ReleasePoint,
        DoctrinePrimaryTarget,
        DoctrineSupportTarget,
        DoctrineFallbackTarget
    }

    public readonly struct PostureExecutionDecision
    {
        public PostureExecutionDecision(
            PostureExecutionAction action,
            string reason,
            PostureExecutionTarget target = PostureExecutionTarget.None,
            bool clearInterruptedPaths = false)
        {
            Action = action;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            Target = target;
            ClearInterruptedPaths = clearInterruptedPaths;
        }

        public PostureExecutionAction Action { get; }
        public string Reason { get; }
        public PostureExecutionTarget Target { get; }
        public bool ClearInterruptedPaths { get; }
    }

    public readonly struct WriteEligibilitySnapshot
    {
        public WriteEligibilitySnapshot(
            bool modeAllowsWrites,
            bool playerProtected,
            bool routed,
            bool orderPending,
            bool recentOrder,
            bool alreadyDoingCorrectTask = false,
            bool atAssignedLocation = false,
            bool missingLedgerAssignment = false,
            bool closeEngaged = false)
        {
            ModeAllowsWrites = modeAllowsWrites;
            PlayerProtected = playerProtected;
            Routed = routed;
            OrderPending = orderPending;
            RecentOrder = recentOrder;
            AlreadyDoingCorrectTask = alreadyDoingCorrectTask;
            AtAssignedLocation = atAssignedLocation;
            MissingLedgerAssignment = missingLedgerAssignment;
            CloseEngaged = closeEngaged;
        }

        public bool ModeAllowsWrites { get; }
        public bool PlayerProtected { get; }
        public bool Routed { get; }
        public bool OrderPending { get; }
        public bool RecentOrder { get; }
        public bool AlreadyDoingCorrectTask { get; }
        public bool AtAssignedLocation { get; }
        public bool MissingLedgerAssignment { get; }
        public bool CloseEngaged { get; }
    }

    // Battle-level gate for the #61 posture executor (Postfix on
    // AIBattle.AdjustGroupFormations).
    // Vanilla anchors: AdjustGroupFormations itself returns for state < 5 or
    // macroai < 0 (5879); CheckGlobalAIStrategy returns once macroai == 3
    // (6374) and vanilla sets every group to screen for the army retreat (4258);
    // BattleUnits.eodcycle > 0 is the end-of-day phase where vanilla stops
    // issuing AI orders (8259, 83526) and autosaves (66904); UpdateAITasks (5857)
    // runs every instance under ai_vs_ai, and isplayeraiorfeud == 2 is the W&L
    // feud instance (SetSide 3408) that duplicates side 0's cycle.
    public static class PostureExecutorBattleGate
    {
        public static bool Allows(int state, int macroAi, int eodCycle, int isPlayerAiOrFeud, bool aiVsAi, out string reason)
        {
            if (state < 5) { reason = "battle-not-started"; return false; }
            if (macroAi < 0) { reason = "macro-inactive"; return false; }
            if (macroAi == 3) { reason = "vanilla-retreat"; return false; }
            if (eodCycle > 0) { reason = "end-of-day"; return false; }
            if (isPlayerAiOrFeud == 2) { reason = "feud-instance"; return false; }
            if (isPlayerAiOrFeud != 0 && !aiVsAi) { reason = "player-instance"; return false; }
            reason = "allow";
            return true;
        }
    }

    public enum FormationWriteKind
    {
        None,
        // Rotate each non-moving regiment in place (Regiment.RotateRegiment,
        // 130159): no target clearing, no slot re-lay.
        RotateInPlace,
        // SetGroupFormation(current formation, rotation, newpath:false,
        // modifylastwaypoint:true): skips ClearTargets (91960 runs it only for
        // !modifylastwaypoint || newpath) but re-paths every child to its
        // rotated slot — so only with no enemies in range.
        WheelGroup,
        // BattleUnits.SetFormation (90971): courier-delivered formation change,
        // the way AdjustGroupFormations does it (5927).
        VanillaFormation
    }

    public readonly struct FormationWriteInput
    {
        public FormationWriteInput(
            bool needsFormation,
            bool needsFacing,
            bool needsRefuse,
            float combatStatusSubordinates,
            float groupSubordinatesMoving,
            int regimentPaths,
            float coverValueSubordinates,
            float nowHours,
            float lastWriteHours,
            float groupEnemiesInRange)
        {
            NeedsFormation = needsFormation;
            NeedsFacing = needsFacing;
            NeedsRefuse = needsRefuse;
            CombatStatusSubordinates = combatStatusSubordinates;
            GroupSubordinatesMoving = groupSubordinatesMoving;
            RegimentPaths = regimentPaths;
            CoverValueSubordinates = coverValueSubordinates;
            NowHours = nowHours;
            LastWriteHours = lastWriteHours;
            GroupEnemiesInRange = groupEnemiesInRange;
        }

        public bool NeedsFormation { get; }
        public bool NeedsFacing { get; }
        public bool NeedsRefuse { get; }
        public float CombatStatusSubordinates { get; }
        public float GroupSubordinatesMoving { get; }
        public int RegimentPaths { get; }
        public float CoverValueSubordinates { get; }
        public float NowHours { get; }
        public float LastWriteHours { get; }
        // Regiment.groupenemiesinrange.
        public float GroupEnemiesInRange { get; }
    }

    public readonly struct FormationWriteDecision
    {
        public FormationWriteDecision(FormationWriteKind kind, string reason)
        {
            Kind = kind;
            Reason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
        }

        public FormationWriteKind Kind { get; }
        public string Reason { get; }
    }

    // R8 (2026-10-14): how #61 may change a group's formation / facing.
    // Vanilla AdjustGroupFormations (5875-5935) only re-forms a group when its
    // stance has settled, it has no path, subordinates are not moving
    // (groupsubordinatesmoving <= 0), it is not in combat
    // (combatstatussubordinates <= 0) and not in cover
    // (covervaluesubordinates <= 0), and it goes through SetFormation (courier).
    // SetGroupFormation with newpath (91884) runs ClearTargets +
    // ChangeRegimentFormation + RegimentSetPath on every regiment — a re-lay —
    // so it is never used on a group in contact.
    public static class CommandFormationWritePolicy
    {
        // 3 game-minutes between formation/facing writes; 6 under fire (each
        // rotation stops the regiments, StopRegiment manualstop).
        public const float CooldownHours = 0.05f;
        public const float UnderFireCooldownHours = 0.1f;

        public static FormationWriteDecision Decide(in FormationWriteInput input)
        {
            if (!input.NeedsFormation && !input.NeedsFacing && !input.NeedsRefuse)
                return new FormationWriteDecision(FormationWriteKind.None, "already-correct");

            bool inCombat = input.CombatStatusSubordinates > 0f;
            if (input.LastWriteHours >= 0f && input.NowHours >= input.LastWriteHours)
            {
                float cooldown = inCombat ? UnderFireCooldownHours : CooldownHours;
                if (input.NowHours - input.LastWriteHours < cooldown)
                    return new FormationWriteDecision(FormationWriteKind.None, inCombat ? "under-fire-cooldown" : "cooldown");
            }

            if (input.RegimentPaths > 0 || input.GroupSubordinatesMoving > 0f)
                return new FormationWriteDecision(FormationWriteKind.None, "moving");

            if (inCombat)
            {
                return input.NeedsFacing
                    ? new FormationWriteDecision(FormationWriteKind.RotateInPlace, "combat-rotate-in-place")
                    : new FormationWriteDecision(FormationWriteKind.None, "combat-no-relay");
            }

            if (input.NeedsFormation && input.CoverValueSubordinates <= 0f)
                return new FormationWriteDecision(FormationWriteKind.VanillaFormation, "vanilla-set-formation");

            if (input.NeedsFacing || input.NeedsRefuse)
            {
                return input.GroupEnemiesInRange > 0f
                    ? new FormationWriteDecision(FormationWriteKind.None, "enemies-in-range")
                    : new FormationWriteDecision(FormationWriteKind.WheelGroup, "wheel");
            }

            return new FormationWriteDecision(FormationWriteKind.None, "in-cover");
        }

        // SetFormation (90971) remaps its argument for groups (unittyp >= 14):
        // 0→3, 1→1, 2→1, 3→2, 4→4, 5→0. Returns the argument that yields the
        // given group formation, or -1.
        public static int VanillaSetFormationIndexForGroup(int groupFormation)
        {
            switch (groupFormation)
            {
                case 0: return 5;
                case 1: return 1;
                case 2: return 3;
                case 3: return 0;
                case 4: return 4;
                default: return -1;
            }
        }
    }

    // BR-M1: the Main-effort arrival hold (TacticalMainArrivalSynchronizer)
    // arms once per role/task assignment. When it ends — supports closed, the
    // budget ran out, or an aggressive commander released it — it stays
    // released until the caller drops the state (role/task changed). Without
    // the latch, an expired hold re-armed on the next tick and the brigade
    // flipped Screen (1) ↔ Attack (3) with a waypoint re-issue each time.
    public static class MainArrivalHoldLatch
    {
        // Hold budget in battle seconds (15 battle-minutes), measured on
        // GameVars.currenttimefromstart. Same value as T1's
        // TacticalMainArrivalSynchronizer.DefaultMaxHoldSeconds.
        public const float MaxHoldBattleSeconds = 15f * 60f;

        public readonly struct State
        {
            public State(bool active, bool released, float startedAtHours)
            {
                Active = active;
                Released = released;
                StartedAtHours = startedAtHours;
            }

            public bool Active { get; }
            public bool Released { get; }
            public float StartedAtHours { get; }
        }

        public static bool ShouldEvaluate(State state)
        {
            return !state.Released;
        }

        public static float ElapsedHours(State state, float nowHours)
        {
            if (!state.Active || nowHours < state.StartedAtHours) return 0f;
            return nowHours - state.StartedAtHours;
        }

        public static float ElapsedBattleSeconds(State state, float nowHours)
        {
            return TacticalBattleClock.HoursToBattleSeconds(ElapsedHours(state, nowHours));
        }

        public static State Next(State previous, float nowHours, bool holding)
        {
            if (previous.Released) return previous;
            if (!holding) return new State(false, true, previous.StartedAtHours);
            if (previous.Active && nowHours >= previous.StartedAtHours) return previous;
            return new State(true, false, nowHours);
        }
    }

    public readonly struct LeafMoveDecision
    {
        public LeafMoveDecision(bool move, string reason)
        {
            Move = move;
            Reason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
        }

        public bool Move { get; }
        public string Reason { get; }
    }

    // B-leaf (2026-10-14): when #61 may move a brigade nested inside an AI
    // group. Such a brigade is an attached sub-unit: vanilla moves it as part
    // of its parent's SetGroupFormation (slots built from
    // groupposition.unitoffsetposition, 92060-92090). So the leaf pass only
    // moves it for movement tasks, only to its slot in the parent's formation,
    // never while the parent itself has an order in flight, and with the same
    // gates as the group path (no move in contact, none over a pending order
    // or an active move).
    public static class LeafBrigadeMovePolicy
    {
        public static LeafMoveDecision Decide(
            CommandTaskType task,
            bool parentHasActiveOrder,
            bool hasFormationSlot,
            bool closeEngaged,
            bool pendingOrder,
            bool activeMove)
        {
            if (!CommandWaypointWritePolicy.IsMovementTask(task)) return new LeafMoveDecision(false, "non-movement-task");
            if (parentHasActiveOrder) return new LeafMoveDecision(false, "parent-ordered");
            if (!hasFormationSlot) return new LeafMoveDecision(false, "no-slot");
            if (closeEngaged) return new LeafMoveDecision(false, "close-engaged");
            if (pendingOrder) return new LeafMoveDecision(false, "order-pending");
            if (activeMove) return new LeafMoveDecision(false, "movement-in-progress");
            return new LeafMoveDecision(true, "move-to-slot");
        }
    }

    // Slot position math of BattlefieldSetup.GetAngleTerrainPosXYStatic
    // (26937-26949) without the terrain clamp/height sample: rotate an
    // (x, z) formation offset by the group's facing.
    public static class LeafFormationSlot
    {
        public static void OffsetXZ(float anchorX, float anchorZ, float angleDegrees, float distanceX, float distanceZ, out float x, out float z)
        {
            double a = (angleDegrees - 90.0) * Math.PI / 180.0;
            double b = angleDegrees * Math.PI / 180.0;
            x = (float)(anchorX - Math.Cos(a) * distanceZ - Math.Cos(b) * distanceX);
            z = (float)(anchorZ + Math.Sin(a) * distanceZ + Math.Sin(b) * distanceX);
        }
    }

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

    // Roster identity for per-battle caches (#61 perf): a cache built from the
    // unit roster stays valid until a unit is added/removed, a unit routs, or
    // a new battle starts — not merely until the next frame.
    public readonly struct TacticalRosterSignature : IEquatable<TacticalRosterSignature>
    {
        public TacticalRosterSignature(int allianceId, int unitCount, int routedCount, int battleKey)
        {
            AllianceId = allianceId;
            UnitCount = unitCount;
            RoutedCount = routedCount;
            BattleKey = battleKey;
        }

        public int AllianceId { get; }
        public int UnitCount { get; }
        public int RoutedCount { get; }
        public int BattleKey { get; }

        public bool Equals(TacticalRosterSignature other)
        {
            return AllianceId == other.AllianceId &&
                UnitCount == other.UnitCount &&
                RoutedCount == other.RoutedCount &&
                BattleKey == other.BattleKey;
        }

        public override bool Equals(object obj)
        {
            return obj is TacticalRosterSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((AllianceId * 397 ^ UnitCount) * 397 ^ RoutedCount) * 397 ^ BattleKey;
            }
        }
    }

    // W&L: the player's command is marked dlcw_isundercommander on the command
    // and every attached unit (DLC_WL.AppointCommand 44269, 45827), so removing
    // the marked probes removes a whole subtree. Filtering with
    // TacticalWriteGuard.IsPlayerProtected instead would also drop the AI
    // parents above the player (their allattachedunits hold the player's
    // units) and orphan their AI brigades.
    public static class PlayerCommandProbeFilter
    {
        public static IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> Without(
            IReadOnlyList<TacticalCommandTreeProbe.ExtendedProbe> probes,
            ICollection<int> playerCommandIds)
        {
            if (probes == null || playerCommandIds == null || playerCommandIds.Count == 0) return probes;
            var kept = new List<TacticalCommandTreeProbe.ExtendedProbe>(probes.Count);
            for (int i = 0; i < probes.Count; i++)
            {
                if (!playerCommandIds.Contains(probes[i].InstanceId)) kept.Add(probes[i]);
            }
            return kept;
        }
    }

    // Battle clock helpers. Doctrine timers run on GameVars.currenttimefromstart,
    // which in battle is game hours (uniStormSystem.Hour + battlepasseddays * 24,
    // 86379 / 93545). The macro AI cycle is sidestatupdatecycle = 0.015 game-hours
    // ≈ 9 s real at 1x, so 1 game-hour ≈ 600 s real at 1x; old wall-clock
    // constants tuned at 1x convert with RealSecondsAtOneXToHours.
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

    public static class CommandPostureExecutor
    {
        public static bool IdleCanSatisfyTask(CommandTaskType task)
        {
            return task == CommandTaskType.ReserveWait ||
                task == CommandTaskType.HoldObjective ||
                task == CommandTaskType.HoldChoke ||
                task == CommandTaskType.Delay ||
                task == CommandTaskType.Consolidate;
        }

        public static PostureExecutionDecision Decide(
            CommandDoctrineOrder order,
            CommandPhysicalState physical,
            float nowSeconds)
        {
            return Decide(
                order,
                physical,
                new WriteEligibilitySnapshot(
                    modeAllowsWrites: true,
                    playerProtected: physical.PlayerProtected,
                    routed: physical.Routed,
                    orderPending: false,
                    recentOrder: false),
                nowSeconds);
        }

        public static PostureExecutionDecision Decide(
            CommandDoctrineOrder order,
            CommandPhysicalState physical,
            WriteEligibilitySnapshot eligibility,
            float nowSeconds)
        {
            if (!eligibility.ModeAllowsWrites)
            {
                return NoWrite("mode-monitor-only");
            }

            if (eligibility.PlayerProtected || physical.PlayerProtected)
            {
                return NoWrite("player-protected");
            }

            if (eligibility.Routed || physical.Routed)
            {
                return NoWrite("routed");
            }

            if (eligibility.OrderPending)
            {
                return NoWrite("order-pending");
            }

            if (ShouldBlockForRecentOrder(order.Task, physical, eligibility.RecentOrder))
            {
                return NoWrite("recent-order");
            }

            if (!order.HasPurpose || order.Task == CommandTaskType.None)
            {
                return NoWrite("missing-ledger-assignment");
            }

            if (eligibility.AlreadyDoingCorrectTask)
            {
                return NoWrite("already-correct");
            }

            if (order.AllowsIdle && order.Task == CommandTaskType.ReserveWait)
            {
                if (order.PrimaryTarget.HasValue && !eligibility.AtAssignedLocation)
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetWaypoint,
                        "reserve-area",
                        PostureExecutionTarget.DoctrinePrimaryTarget);
                }

                return NoWrite("legal-idle");
            }

            if (eligibility.CloseEngaged)
            {
                if (TacticalDecisionDoctrine.ShouldBreakOffRecon(
                        order.Task,
                        closeEngaged: true,
                        hasFallbackTarget: order.FallbackTarget.HasValue))
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetFormationAndWaypoint,
                        "close-engaged-screen-breakoff",
                        PostureExecutionTarget.DoctrineFallbackTarget);
                }

                if (order.Task == CommandTaskType.FallBackToLine)
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetFormationAndWaypoint,
                        "close-engaged-fallback-line",
                        PostureExecutionTarget.DoctrineFallbackTarget);
                }

                return Formation("close-engaged-" + TaskReason(order.Task));
            }

            if (order.Task == CommandTaskType.FallBackToLine && physical.PathInterrupted && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.FallbackToLine,
                    "fallback-line",
                    PostureExecutionTarget.DoctrineFallbackTarget,
                    clearInterruptedPaths: true);
            }

            if (physical.PathInterrupted && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.RecoverInterruptedOrder,
                    "interrupted-inactive",
                    PostureExecutionTarget.RecoveryPath,
                    clearInterruptedPaths: true);
            }

            if (physical.ActiveMove)
            {
                return NoWrite("movement-in-progress");
            }

            switch (order.Task)
            {
                case CommandTaskType.FormUp:
                case CommandTaskType.AdvanceToAssembly:
                    return FormationAndWaypoint(TaskReason(order.Task), PostureExecutionTarget.DoctrinePrimaryTarget);
                case CommandTaskType.Scout:
                case CommandTaskType.Probe:
                case CommandTaskType.AttackObjective:
                case CommandTaskType.SupportAttack:
                case CommandTaskType.FixEnemy:
                case CommandTaskType.Screen:
                case CommandTaskType.GuardFlank:
                    return FormationAndWaypoint(TaskReason(order.Task), PostureExecutionTarget.DoctrinePrimaryTarget);
                case CommandTaskType.FallBackToLine:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.FallbackToLine,
                        "fallback-line",
                        PostureExecutionTarget.DoctrineFallbackTarget,
                        clearInterruptedPaths: true);
                default:
                    return Decide(
                        new CommandNodeOperationalState(
                            order.NodeId,
                            CommandEchelonKind.DivisionLike,
                            order.Role,
                            order.Task,
                            CommandTaskState.Committed),
                        physical,
                        eligibility);
            }
        }

        public static PostureExecutionDecision Decide(
            CommandNodeOperationalState state,
            CommandPhysicalState physical,
            WriteEligibilitySnapshot eligibility)
        {
            if (!eligibility.ModeAllowsWrites)
            {
                return NoWrite("mode-monitor-only");
            }

            if (eligibility.PlayerProtected || physical.PlayerProtected)
            {
                return NoWrite("player-protected");
            }

            if (eligibility.Routed || physical.Routed)
            {
                return NoWrite("routed");
            }

            if (eligibility.OrderPending)
            {
                return NoWrite("order-pending");
            }

            if (ShouldBlockForRecentOrder(state.Task, physical, eligibility.RecentOrder))
            {
                return NoWrite("recent-order");
            }

            if (eligibility.MissingLedgerAssignment || state.Task == CommandTaskType.None)
            {
                return NoWrite("missing-ledger-assignment");
            }

            if (eligibility.AlreadyDoingCorrectTask)
            {
                return NoWrite("already-correct");
            }

            if (physical.ActiveMove)
            {
                return NoWrite("movement-in-progress");
            }

            if (eligibility.CloseEngaged)
            {
                if (state.Task == CommandTaskType.FallBackToLine)
                {
                    return new PostureExecutionDecision(
                        PostureExecutionAction.SetFormationAndWaypoint,
                        "close-engaged-fallback-line",
                        PostureExecutionTarget.FallbackLine);
                }

                return Formation("close-engaged-" + TaskReason(state.Task));
            }

            if (physical.PathInterrupted && physical.Paths <= 0 && !physical.ActiveMove)
            {
                return new PostureExecutionDecision(
                    PostureExecutionAction.RecoverInterruptedOrder,
                    "illegal-idle-path-interrupted",
                    PostureExecutionTarget.RecoveryPath,
                    clearInterruptedPaths: true);
            }

            switch (state.Task)
            {
                case CommandTaskType.Scout:
                    return FormationAndWaypoint("scout", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.FormUp:
                    return FormationAndWaypoint("form-up", PostureExecutionTarget.AssemblyArea);
                case CommandTaskType.AdvanceToAssembly:
                    return FormationAndWaypoint("advance-to-assembly", PostureExecutionTarget.AssemblyArea);
                case CommandTaskType.AttackObjective:
                    return FormationAndWaypoint("attack-objective", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.HoldObjective:
                    return Formation("hold-objective");
                case CommandTaskType.FixEnemy:
                    return FormationAndWaypoint("fix-enemy", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.Screen:
                    return FormationAndWaypoint("screen", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.Probe:
                    return FormationAndWaypoint("probe", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.SupportAttack:
                    return FormationAndWaypoint("support-attack", PostureExecutionTarget.ObjectiveApproach);
                case CommandTaskType.HoldChoke:
                    return Formation("hold-choke");
                case CommandTaskType.GuardFlank:
                    return Formation("guard-flank");
                case CommandTaskType.Delay:
                    return Formation("delay");
                case CommandTaskType.Consolidate:
                    return Formation("consolidate");
                case CommandTaskType.FallBackToLine:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.FallbackToLine,
                        "fallback-line",
                        PostureExecutionTarget.FallbackLine);
                case CommandTaskType.ReleaseReserve:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.ReleaseReserve,
                        "release-reserve",
                        PostureExecutionTarget.ReleasePoint);
                case CommandTaskType.RecoverStuckOrder:
                    return new PostureExecutionDecision(
                        PostureExecutionAction.RecoverInterruptedOrder,
                        "recover-stuck-order",
                        PostureExecutionTarget.RecoveryPath,
                        clearInterruptedPaths: true);
                case CommandTaskType.ReserveWait:
                    return eligibility.AtAssignedLocation
                        ? Formation("reserve-hold")
                        : new PostureExecutionDecision(
                            PostureExecutionAction.SetWaypoint,
                            "reserve-area",
                            PostureExecutionTarget.ReserveArea);
                default:
                    return NoWrite("already-valid");
            }
        }

        private static PostureExecutionDecision NoWrite(string reason)
        {
            return new PostureExecutionDecision(PostureExecutionAction.NoWrite, reason);
        }

        public static bool ShouldBlockForRecentOrder(
            CommandTaskType task,
            CommandPhysicalState physical,
            bool recentOrder)
        {
            if (!recentOrder) return false;
            if (!IsMovementTask(task)) return true;
            return physical.ActiveMove || physical.Paths > 0;
        }

        private static bool IsMovementTask(CommandTaskType task)
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

        private static PostureExecutionDecision Formation(string reason)
        {
            return new PostureExecutionDecision(
                PostureExecutionAction.SetFormation,
                reason,
                PostureExecutionTarget.CurrentPosition);
        }

        private static PostureExecutionDecision FormationAndWaypoint(
            string reason,
            PostureExecutionTarget target)
        {
            return new PostureExecutionDecision(
                PostureExecutionAction.SetFormationAndWaypoint,
                reason,
                target);
        }

        private static string TaskReason(CommandTaskType task)
        {
            switch (task)
            {
                case CommandTaskType.Scout:
                    return "scout";
                case CommandTaskType.Probe:
                    return "probe";
                case CommandTaskType.Screen:
                    return "screen";
                case CommandTaskType.FormUp:
                    return "form-up";
                case CommandTaskType.AdvanceToAssembly:
                    return "advance-to-assembly";
                case CommandTaskType.AttackObjective:
                    return "attack-objective";
                case CommandTaskType.FixEnemy:
                    return "fix-enemy";
                case CommandTaskType.SupportAttack:
                    return "support-attack";
                case CommandTaskType.HoldObjective:
                    return "hold-objective";
                case CommandTaskType.HoldChoke:
                    return "hold-choke";
                case CommandTaskType.GuardFlank:
                    return "guard-flank";
                case CommandTaskType.ReserveWait:
                    return "reserve-wait";
                case CommandTaskType.ReleaseReserve:
                    return "release-reserve";
                case CommandTaskType.FallBackToLine:
                    return "fallback-line";
                case CommandTaskType.Delay:
                    return "delay";
                case CommandTaskType.Consolidate:
                    return "consolidate";
                case CommandTaskType.RecoverStuckOrder:
                    return "recover-stuck-order";
                default:
                    return "unknown";
            }
        }
    }
}
