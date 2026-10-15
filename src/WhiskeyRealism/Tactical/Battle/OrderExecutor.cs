using WhiskeyRealism.Tactical.Operations;

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
}
